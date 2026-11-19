// Screen-window side of the aim-at-dots zero. It runs in the fullscreen screen
// window so dots land on the physical display, captures the raw gun-tracker pose
// for each dot, solves, and sends the result to the dashboard. Nothing changes
// in the dashboard until it receives a finished result.
import { AIM_LIMITS, averageNative, dotTargets, rotationSpreadDeg, solveAim } from "./aim-calibration.js";
import { compose, fromNative, inverse, point, pose } from "./math.js";

/** Physical dot diameter in metres; drawn to scale on the configured screen. */
const DOT_DIAMETER_M = 0.03;
/** Recent raw samples kept for steady and pre-trigger windows (ms). */
const BUFFER_MS = 2500;
/** Sampling/update interval (ms); tracking frames arrive about every 50 ms. */
const STEP_MS = 30;

export function installAimScreen({ readState, send }) {
    const overlay = document.createElement("div");
    overlay.id = "aim-overlay";
    overlay.hidden = true;
    overlay.innerHTML = `
        <div id="aim-dot"><svg viewBox="0 0 100 100" aria-hidden="true">
            <circle cx="50" cy="50" r="46" class="aim-track"/><circle id="aim-progress" cx="50" cy="50" r="46" class="aim-fill" pathLength="100"/>
            <line x1="50" y1="12" x2="50" y2="88"/><line x1="12" y1="50" x2="88" y2="50"/></svg></div>
        <div id="aim-panel" role="status">
            <div id="aim-step"></div><div id="aim-text"></div><div id="aim-status"></div>
            <div class="aim-keys">Trigger or Space: capture · Hold steady: auto-capture · Backspace: redo last dot · F: fullscreen · Esc: cancel</div>
        </div>`;
    document.body.append(overlay);
    const $ = (id) => document.getElementById(id);
    let run = null;

    function device(state, role = "weapon") {
        const serial = state.config[role].serial;
        return serial ? state.frame?.devices?.find((d) => d.serial === serial && d.kind !== "base station") : null;
    }
    function fresh(state, d) {
        return state.source === "live" && state.frame?.status === "connected" && d?.valid && d.matrix &&
            Number.isFinite(state.frame.ageMs) && state.frame.ageMs <= state.config.view.staleMs;
    }
    function start(id) {
        if (run?.id === id) return;
        const { config } = readState();
        run = { id, index: 0, captures: [], buffer: [], lastSequence: null, lastPresses: null,
            // The gun must move away from its starting pose before a steady hold
            // counts, so a gun resting on the table cannot capture dot 1.
            capturedQ: null, needsMove: true, targets: dotTargets(config), finished: false };
        overlay.hidden = false;
        send({ type: "aim-ack", id });
        showDot();
        // A timer (not animation frames) keeps sampling if the window is briefly hidden.
        const current = run;
        current.timer = setInterval(() => (run === current ? step() : clearInterval(current.timer)), STEP_MS);
    }
    function stop(message) {
        if (run && !run.finished) send({ type: "aim-cancel", id: run.id, message });
        run = null;
        overlay.hidden = true;
    }
    function showDot() {
        const { config } = readState();
        const total = run.targets.length;
        const dot = $("aim-dot");
        if (run.finished) { dot.hidden = true; return; }
        const { u, v } = run.targets[run.index];
        const size = DOT_DIAMETER_M / config.screen.width * window.innerWidth * 3;
        Object.assign(dot.style, { left: `${u * 100}%`, top: `${(1 - v) * 100}%`, width: `${size}px`, height: `${size}px` });
        dot.hidden = false;
        $("aim-step").textContent = `Dot ${run.index + 1} of ${total}`;
        $("aim-text").textContent = run.index === 0
            ? "Wear the head tracker. Stand where participants stand, hold the gun the way you normally shoot (stock in your shoulder), and aim through the sights at the centre of the dot."
            : "Aim through the sights at the centre of the dot. Pull the trigger, press Space, or hold steady.";
        progress(0);
    }
    function progress(fraction) {
        $("aim-progress").style.strokeDasharray = `${Math.max(0, Math.min(1, fraction)) * 100} 100`;
    }
    function status(text) { $("aim-status").textContent = text; }
    /** Viewing eye in room metres from the head tracker, or null when it was not tracked throughout. */
    function eyeFrom(samples) {
        if (!samples.every((s) => s.head)) return null;
        const { config } = readState();
        const head = compose(compose(pose(config.registration), averageNative(samples.map((s) => s.head))),
            inverse(pose(config.head.mount)));
        return point(head, config.head.eye);
    }
    function capture(samples, how) {
        if (samples.length < AIM_LIMITS.minSamplesPerCapture)
            return status("Not enough fresh tracking samples. Keep the tracker visible and try again.");
        const native = averageNative(samples.map((s) => s.matrix));
        const eye = eyeFrom(samples);
        run.captures.push({ native, target: run.targets[run.index].point, ...(eye ? { eye } : {}) });
        run.capturedQ = native.q;
        run.index++;
        status(`Captured dot ${run.index} (${how})` + (eye ? "." : " without the head tracker; the zero will be less accurate."));
        if (run.index === run.targets.length) finish();
        else showDot();
    }
    function finish() {
        run.finished = true;
        showDot();
        $("aim-step").textContent = "Aim zero complete";
        try {
            const { config } = readState();
            const result = solveAim(config, run.captures);
            send({ type: "aim-result", id: run.id, result });
            $("aim-text").textContent = `Average ${result.meanDeg.toFixed(2)}° · worst ${result.maxDeg.toFixed(2)}° (dot ${result.worstDot + 1}). ` +
                (result.mode === "sight line"
                    ? `Sights ${(result.sightHeightM * 100).toFixed(1)} cm above the barrel; zeroed at ${result.zeroDistanceM.toFixed(1)} m. `
                    : "Zeroed at the screen. ") +
                "Applied to the dashboard's gun settings. Review there, then Save calibration.";
            status(result.warnings.join(" ") || "Press Esc to close, or R to aim all dots again.");
        } catch (error) {
            $("aim-text").textContent = error.message;
            status("Press R to start again, or Esc to cancel.");
        }
    }
    function windowSamples(fromMs, toMs) {
        return run.buffer.filter((s) => s.t >= fromMs && s.t <= toMs);
    }
    function step() {
        if (run.finished) return;
        const state = readState();
        const d = device(state);
        if (!fresh(state, d)) { progress(0); return status("Waiting for a fresh gun-tracker pose…"); }
        const now = performance.now(), sampleTime = now - state.frame.ageMs;
        if (state.frame.sequence !== run.lastSequence) {
            run.lastSequence = state.frame.sequence;
            const head = device(state, "head");
            // The eye is used only when the head tracker is fresh in the same frame.
            run.buffer.push({ t: sampleTime, matrix: d.matrix, head: fresh(state, head) ? head.matrix : null });
            run.buffer = run.buffer.filter((s) => s.t > now - BUFFER_MS);
        }
        // Trigger: use the pose from just before the pull, so the pull itself does not count.
        const presses = d.trigger?.available ? d.trigger.presses : null;
        if (presses !== null && run.lastPresses !== null && presses > run.lastPresses) {
            run.lastPresses = presses;
            const [late, early] = AIM_LIMITS.triggerWindowMs;
            return capture(windowSamples(sampleTime - early, sampleTime - late), "trigger");
        }
        run.lastPresses = presses;
        // Steady hold: the longest recent stretch within the rotation limit.
        const newest = run.buffer.at(-1), q = fromNative(newest.matrix).q;
        if (run.needsMove) { run.capturedQ = q; run.needsMove = false; }
        if (run.capturedQ && q.angleTo(run.capturedQ) * 180 / Math.PI < AIM_LIMITS.rearmDeg) {
            progress(0);
            return status(run.index === 0 ? "Pick up the gun and aim at the dot." : "Move to the next dot.");
        }
        run.capturedQ = null;
        let start = run.buffer.length - 1;
        while (start > 0 && rotationSpreadDeg([newest.matrix, run.buffer[start - 1].matrix]) <= AIM_LIMITS.steadyDeg) start--;
        const held = newest.t - run.buffer[start].t;
        progress(held / AIM_LIMITS.steadyMs);
        if (held >= AIM_LIMITS.steadyMs) capture(run.buffer.slice(start), "held steady");
    }
    window.addEventListener("keydown", (event) => {
        if (!run) return;
        if (event.key === "Escape") return stop("Aim zero cancelled.");
        if (event.key === "f" || event.key === "F")
            return document.fullscreenElement ? document.exitFullscreen() : document.body.requestFullscreen();
        if (event.key === "r" || event.key === "R") { const id = run.id; run = null; return start(id); }
        if (run.finished) return;
        if (event.key === " ") {
            event.preventDefault();
            const now = performance.now();
            capture(windowSamples(now - 400, now), "Space");
        } else if (event.key === "Backspace" && run.index > 0) {
            run.captures.pop();
            run.index--;
            run.capturedQ = null;
            showDot();
            status("Redo this dot.");
        }
    });
    window.addEventListener("resize", () => run && showDot());
    return { start, stop };
}
