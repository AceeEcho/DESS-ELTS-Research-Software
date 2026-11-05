import { distanceFields, readDistance } from "./distance-fields.js";
import { fromNative } from "./math.js";
import { DIRECTION_LIMITS, DIRECTION_STEPS, averageStablePoses, solveHeadDirections } from "./head-calibration.js";

// The wizard keeps captures private until Apply. Closing or cancelling never
// changes the active room registration, mount, or saved profile.
export function installHeadCalibration({ readState, applyResult, notify }) {
    const $ = (id) => document.getElementById(id);
    const dialog = $("direction-dialog");
    let step = 0, samples = {}, solution = null, original = null, signature = "", timer = null;
    const fingerprint = ({ config, source }) => JSON.stringify({
        source, serial: config.head.serial, weaponSerial: config.weapon.serial,
        screen: config.screen, mount: config.head.mount, registration: config.registration,
    });
    function stopCapture() {
        clearInterval(timer);
        timer = null;
    }
    function liveSample() {
        const state = readState();
        if (signature && signature !== fingerprint(state))
            throw new Error("Tracking assignment or geometry changed. Close this guide and start again.");
        const device = state.frame?.devices?.find((d) =>
            d.serial === state.config.head.serial && d.kind === "tracker");
        if (state.source !== "live" || state.frame?.status !== "connected" ||
            !device?.connected || !device.valid || !device.matrix ||
            !Number.isFinite(state.frame.ageMs) || state.frame.ageMs > state.config.view.staleMs)
            throw new Error("No fresh head-tracker pose. Keep its sensors visible to the base station, then retry.");
        return { pose: fromNative(device.matrix), sequence: state.frame.sequence };
    }
    function render() {
        const review = step === DIRECTION_STEPS.length;
        const current = DIRECTION_STEPS[step];
        $("direction-progress").textContent = review ? "Review directions" : `Step ${step + 1} of ${DIRECTION_STEPS.length}`;
        $("direction-symbol").textContent = review ? "✓" : current.symbol;
        $("direction-title").textContent = review ? "Directions agree" : current.title;
        $("direction-instruction").textContent = review
            ? "The captured turns and sideways movements agree. Apply to update the head mounting direction and room alignment."
            : current.instruction;
        $("direction-capture").hidden = review;
        $("direction-capture").disabled = !!timer;
        $("direction-back").disabled = !!timer || step === 0;
        $("direction-apply").hidden = !review;
        $("direction-distance-row").hidden = !review;
        $("direction-summary").hidden = !review;
        $("direction-status").textContent = review ? "No settings have changed yet." : "Click capture, then get into position during the 3-second countdown.";
        if (review) {
            const s = solution.summary;
            $("direction-summary").textContent = `Left turn ${s.leftTurn.toFixed(0)}° · Right turn ${s.rightTurn.toFixed(0)}° · Look up ${s.lookUp.toFixed(0)}° · Sideways span ${s.sidewaysSpan.toFixed(2)} m`;
        }
    }
    function fail(error) {
        stopCapture();
        render();
        $("direction-status").textContent = error.message;
    }
    $("calibrate-head").onclick = () => {
        signature = "";
        try {
            liveSample();
            original = structuredClone(readState().config);
            signature = fingerprint(readState());
            samples = {};
            step = 0;
            solution = null;
            $("direction-distance-row").innerHTML = distanceFields(
                "direction-distance", "Preview distance from screen", 2,
            );
            for (const unit of ["feet", "inches"])
                $(`direction-distance-${unit}`).value = $(`reset-distance-${unit}`).value;
            render();
            dialog.showModal();
        } catch (error) { notify(error.message); }
    };
    $("direction-capture").onclick = () => {
        if (timer) return;
        try { liveSample(); } catch (error) { return fail(error); }
        const readyAt = performance.now() + DIRECTION_LIMITS.captureDelayMs;
        const buffer = [];
        let lastSequence = null, firstSampleAt = null;
        timer = setInterval(() => {
            try {
                const sample = liveSample(), now = performance.now();
                if (now < readyAt) {
                    $("direction-status").textContent = `Get into position… ${Math.ceil((readyAt - now) / 1000)}`;
                    return;
                }
                if (sample.sequence !== lastSequence) {
                    lastSequence = sample.sequence;
                    firstSampleAt ??= now;
                    buffer.push(sample.pose);
                }
                $("direction-status").textContent = "Capturing… hold still.";
                if (firstSampleAt !== null && now - firstSampleAt >= DIRECTION_LIMITS.captureMs &&
                    buffer.length >= DIRECTION_LIMITS.minSamples) {
                    const captured = averageStablePoses(buffer);
                    stopCapture();
                    samples[DIRECTION_STEPS[step].key] = captured;
                    if (step === DIRECTION_STEPS.length - 1)
                        solution = solveHeadDirections(original, samples, readDistance("direction-distance"), undefined, readDistance("reset-height"));
                    step++;
                    render();
                } else if (now - readyAt > DIRECTION_LIMITS.timeoutMs) {
                    throw new Error("Tracking samples stopped updating. Check SteamVR and retry this step.");
                }
            } catch (error) { fail(error); }
        }, 40);
        render();
    };
    $("direction-back").onclick = () => {
        if (timer || step === 0) return;
        step--;
        // Recapturing an earlier pose invalidates everything captured after it.
        for (const { key } of DIRECTION_STEPS.slice(step)) delete samples[key];
        solution = null;
        render();
    };
    $("direction-apply").onclick = () => {
        try {
            liveSample();
            const result = solveHeadDirections(original, samples, readDistance("direction-distance"), undefined, readDistance("reset-height"));
            applyResult(result);
            dialog.close();
            notify("Head directions applied. Move and turn to check the preview, then save your profile.");
        } catch (error) { $("direction-status").textContent = error.message; }
    };
    $("direction-cancel").onclick = () => dialog.close();
    dialog.addEventListener("close", stopCapture);
    dialog.addEventListener("cancel", stopCapture);
}
