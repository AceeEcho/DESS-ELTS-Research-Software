import { METRES_PER_INCH, distanceFields, readDistance } from "./distance-fields.js";
import { nextUnusedId, normalizedId, participantIdPicker } from "./participant-id.js";
import { CalibrationScene } from "./scene.js";
import { triggerIndicator } from "./trigger-indicator.js";
import { installHeadCalibration } from "./head-calibration-ui.js";
import { DIRECTION_LIMITS } from "./head-calibration.js";
import { installAimScreen } from "./aim-calibration-ui.js";
import {
    vec,
    rotation,
    pose,
    compose,
    fromNative,
    degrees,
    resolveObject,
    relativeAngles,
    screenPose,
    screenDistance,
    registrationTiltDegrees,
    trackerToEye,
    recenteredRegistration,
} from "./math.js";

const $ = (id) => document.getElementById(id);
const query = new URLSearchParams(location.search);
const isScreen = query.has("screen");
if (isScreen) document.body.classList.add("screen-only");
const DRAFT_KEY = "elts-calibration-v1-draft";
let datasetGeneration = "";
// The browser retains only a random lookup key. The station owns draft contents
// so its permanent reset can remove them even when this browser is closed.
const DRAFT_ID_KEY = "elts-calibration-draft-id";
let draftId = "", queuedDraft, draftWrite;
let resetHeight; // Undefined in older drafts, initially uses screen-centre height.
let resetDistance = 2; // Metres, stored with the local draft for repeatable resets.
let resetReference = "eye"; // Older drafts are explicitly restored as head-centre measurements.
let config,
    defaults,
    token,
    scene,
    frame = null,
    receivedAt = 0,
    source = "manual",
    tab = "room";
let head = { valid: false },
    weapon = { valid: false },
    modified = false,
    // The participant this draft belongs to; empty until saved or loaded.
    participantId = "";
let lastScreenState = 0,
    lastMonitor = 0,
    noticeTimer,
    deviceSignature = "",
    pollFailures = 0;
// Each dashboard owns its screen window, so opening another dashboard cannot
// silently replace the first screen's geometry or input source.
const session = isScreen
    ? query.get("session")
    : sessionStorage.getItem("calibration-screen-session") ||
      crypto.randomUUID();
if (!isScreen) sessionStorage.setItem("calibration-screen-session", session);
const channel = new BroadcastChannel("elts-calibration-v1-" + session);

function notice(message) {
    $("notice").textContent = message;
    $("notice").hidden = false;
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => ($("notice").hidden = true), 6500);
}
async function api(path, body) {
    const response = await fetch(
        "/api/" + path,
        body === undefined
            ? {}
            : {
                  method: "POST",
                  headers: {
                      "Content-Type": "application/json",
                      "X-Calibration-Token": token,
                      "X-Dataset-Generation": datasetGeneration,
                  },
                  body: JSON.stringify(body),
              },
    );
    const result = await response.json();
    if (!response.ok)
        throw new Error(result.error || "Local server request failed");
    return result;
}
function get(path) {
    return path.split(".").reduce((value, key) => value[key], config);
}
function set(path, value) {
    const parts = path.split("."),
        key = parts.pop();
    parts.reduce((object, key) => object[key], config)[key] = value;
}
function persistDraft() {
    if (!draftId || isScreen) return;
    queuedDraft = {id: draftId, config: structuredClone(config), participantId, resetDistance, resetHeight, resetReference};
    if (!draftWrite) draftWrite = writeDrafts();
    return draftWrite;
}
async function writeDrafts() {
    try {
        while (queuedDraft) {
            const next = queuedDraft;
            queuedDraft = null;
            await api("draft", next);
        }
        return true;
    } catch (error) {
        notice("Could not save the calibration draft. " + error.message);
        return false;
    } finally {
        draftWrite = null;
    }
}
function syncScreen() {
    if (!isScreen) channel.postMessage({ config, source });
}
function changed(path = "") {
    modified = true;
    $("save-state").textContent = "Unsaved calibration changes";
    // References depend on bindings, room registration and the fitted mount.
    for (const role of ["head", "weapon"])
        if (
            path.startsWith(role + ".mount") ||
            path === role + ".serial" ||
            path.startsWith("registration") ||
            path.startsWith("screen")
        )
            config[role].reference = null;
    scene.configure(config);
    persistDraft();
    syncScreen();
}
function field(
    path,
    label,
    { min = -100, max = 100, unit = "m" } = {},
) {
    return `<div class="field"><label for="f-${path}">${label}${unit ? " (" + unit + ")" : ""}</label><input id="f-${path}" data-path="${path}" type="number" min="${min}" max="${max}" step="any" value="${Number(get(path).toFixed(4))}"></div>`;
}
function profileDistance(path, label, directions) {
    return distanceFields(path, label, get(path), directions);
}

function vector(path, label, angles = false, names = null) {
    names ??= angles
        ? ["Pitch X", "Yaw Y", "Roll Z"]
        : ["X · right", "Y · up", "Z · back"];
    return `<div class="field-title">${label} (${angles ? "°" : "m"})</div><div class="vector">${names.map((name, i) => `<label for="f-${path}.${i}">${name}<input id="f-${path}.${i}" data-path="${path}.${i}" type="number" min="${angles ? -360 : -100}" max="${angles ? 360 : 100}" step="any" value="${Number(get(path)[i].toFixed(4))}"></label>`).join("")}</div>`;
}
function check(path, label) {
    return `<label class="check-field"><input type="checkbox" data-path="${path}" ${get(path) ? "checked" : ""}>${label}</label>`;
}
function section(title, body, help = "") {
    return `<section><h3>${title}</h3>${help ? `<p class="help">${help}</p>` : ""}${body}</section>`;
}
// Object-local axes shared by every offset: +X right, +Y up, +Z back toward
// the participant (−Z is forward). Each pair lists the +, then − direction.
const AXIS_DIRECTIONS = [
    ["X · left / right", ["Right", "Left"]],
    ["Y · up / down", ["Above", "Below"]],
    ["Z · forward / back", ["Behind (toward you)", "In front"]],
];
const TILT_NAMES = ["Pitch · nose up", "Yaw · turn left", "Roll · twist"];
// The gun is entered relative to the muzzle tip, the point the bullet leaves.
// Stored values are unchanged: weapon.mount.position is still measured from the
// drawing's centre, so the tracker-to-muzzle vector is mount.position − muzzle.
const TRACKER_FROM_MUZZLE = "weapon.trackerFromMuzzle";
const TRACKER_TO_EYE = "head.trackerToEye";
function trackerFromMuzzle(i) {
    return config.weapon.mount.position[i] - config.weapon.muzzle[i];
}
// Aim-at-dots zero: the screen window measures, the dashboard applies.
let aimRun = null, aimUndo = null, aimSummary = "Not done yet for this setup.";
function openScreen() {
    const opened = window.open(
        "/?screen=1&session=" + encodeURIComponent(session),
        "elts-calibration-screen-" + session,
        "width=1280,height=720",
    );
    syncScreen();
    return opened;
}
function startAim() {
    if (source !== "live") return notice("Aim zero needs Live SteamVR trackers (Tracking tab).");
    if (!config.weapon.serial) return notice("Pick the gun tracker under 1 · Gun tracker first.");
    if (!openScreen()) return notice("The browser blocked the screen window. Allow pop-ups for this page.");
    const id = crypto.randomUUID();
    aimRun = { id, acknowledged: false };
    // Repeat until the screen window has loaded and acknowledged the request.
    const started = performance.now();
    const ask = () => {
        if (aimRun?.id !== id || aimRun.acknowledged) return;
        if (performance.now() - started > 15000)
            return notice("The screen window did not respond. Open screen, then try again.");
        channel.postMessage({ type: "aim-start", id });
        setTimeout(ask, 400);
    };
    ask();
    notice("Dots are showing in the screen window. Put it on the TV and press F for fullscreen.");
}
function applyAim(result) {
    aimUndo = { rotation: [...config.weapon.mount.rotation], bore: [...config.weapon.bore], summary: aimSummary };
    config.weapon.mount.rotation = result.mountRotation;
    // The tilt points the gun's forward axis along the sight line; the small barrel
    // correction makes the shot from the muzzle cross it at the target distance.
    config.weapon.bore = result.boreRotation ?? [0, 0, 0];
    aimSummary = `Last zero: average ${result.meanDeg.toFixed(2)}°, worst ${result.maxDeg.toFixed(2)}° (dot ${result.worstDot + 1}). ` +
        (result.mode === "sight line"
            ? `Sights ${(result.sightHeightM * 100).toFixed(1)} cm above the barrel (many AR-style rifles are about 6–7 cm; a very different value means the muzzle or eye measurement needs checking); zeroed at ${result.zeroDistanceM.toFixed(1)} m. `
            : "") +
        (result.warnings.join(" ") || "Applied. Save calibration to keep it.");
    changed("weapon.mount.rotation");
    renderSettings();
    notice("Aim zero applied to the gun. Save calibration to keep it.");
}
function undoAim() {
    if (!aimUndo) return;
    config.weapon.mount.rotation = aimUndo.rotation;
    config.weapon.bore = aimUndo.bore;
    aimSummary = aimUndo.summary;
    aimUndo = null;
    changed("weapon.mount.rotation");
    renderSettings();
    notice("Previous gun tilt and aim correction restored.");
}
function mountTilt(role) {
    return vector(role + ".mount.rotation", "Tracker tilt on the " + (role === "head" ? "head" : "gun"), true, TILT_NAMES) +
        `<button class="wide" data-action="align-${role}">Align ${role === "head" ? "head" : "gun"} forward to screen</button>`;
}
function headMountSection() {
    return section(
        "Where the tracker sits on the head",
        vector("head.mount.position", "Centre of the head → centre of the tracker", false,
            ["Right +", "Up +", "Behind +"]) + mountTilt("head"),
        "These offsets locate the head centre. Changing that reference keeps the measured tracker-to-eye offset below. Tilt is how the tracker is turned relative to the head; use the head-direction guide to establish room alignment.",
    );
}
function manualSection(role) {
    return section(
        "Manual test pose",
        vector(
            role + ".manual.position",
            role === "head" ? "Head centre in room" : "Gun drawing centre in room",
        ) + vector(role + ".manual.rotation", "Rotation", true),
        "Used only in Manual preview, to try offsets without equipment. Positive yaw turns left; positive pitch looks up.",
    );
}
function binding(role) {
    return `<label for="${role}-serial">Assigned tracker serial</label><select id="${role}-serial" class="device-select" data-role="${role}"></select><div class="tracker-detail" id="${role}-tracker-detail">Waiting for pose…</div>`;
}
function renderSettings() {
    if (!config) return;
    let html = "";
    if (tab === "room") {
        html += section(
            "TV / screen",
            `<div class="single-row">${field("screen.width", "Visible width", { min: 0.05, max: 20 })}${field("screen.height", "Visible height", { min: 0.05, max: 20 })}</div>` +
                field("screen.bottom", "Bottom edge above floor", {
                    min: 0,
                    max: 10,
                }) +
                `<div class="single-row">${field("screen.x", "Left / right")}${field("screen.z", "Front / back")}</div>` +
                `<details><summary>Screen angle &amp; size calculator</summary>${vector("screen.rotation", "Screen rotation", true)}<div class="single-row"><div class="field"><label for="diagonal">Diagonal (inches)</label><input id="diagonal" type="number" min="1" max="300" value="65"></div><div class="field"><label for="aspect">Aspect ratio</label><select id="aspect"><option value="1.7777777778">16:9</option><option value="1.6">16:10</option><option value="1.3333333333">4:3</option><option value="2.3333333333">21:9</option></select></div></div><button class="wide" data-action="diagonal">Calculate width &amp; height</button></details>`,
            "Enter the illuminated display area, excluding the bezel. Bottom height is the midpoint of the lower edge.",
        );
        html += section(
            "Room &amp; display aids",
            `<div class="single-row">${field("room.width", "Room width", { min: 1, max: 30 })}${field("room.depth", "Room depth", { min: 1, max: 30 })}</div>` +
                field("room.height", "Room height", { min: 1, max: 10 }) +
                check("view.axes", "Show local XYZ axes") +
                check("view.rays", "Show head & weapon rays") +
                check("view.frustum", "Show eye-to-screen perspective lines") +
                check("view.labels", "Show object labels"),
            "Width and depth also size the virtual floor extending behind the screen. Both floors use room height zero.",
        );
    } else if (tab === "head") {
        html += section(
            "Head tracker",
            binding("head"),
            "Head and weapon must use different serial numbers. Select live input in Tracking.",
        );
        html += headMountSection();
        html += section(
            "Viewing eye",
            `<div class="field-title">Tracker centre → viewing eye</div>` +
                distanceFields(`${TRACKER_TO_EYE}.0`, "X · left / right", trackerToEye(config).x, ["Right", "Left"]) +
                distanceFields(`${TRACKER_TO_EYE}.1`, "Y · up / down", trackerToEye(config).y, ["Up", "Down"]) +
                distanceFields(`${TRACKER_TO_EYE}.2`, "Z · front / back", trackerToEye(config).z, ["Back", "Front"]),
            "Measure directly from the tracker centre to your viewing eye, or the midpoint between your eyes, with the headband fitted. Directions follow your head: a rear-mounted tracker puts the eye in Front. Use this same eye point when entering reset measurements.",
        );
        html += section(
            "Person drawing",
            profileDistance("person.height", "Participant height") +
                field("person.radius", "Body radius", { min: 0.05, max: 0.6 }),
            "These dimensions size the body preview. They do not determine the eye position.",
        );
        html += manualSection("head");
    } else if (tab === "weapon") {
        html += section(
            "1 · Gun tracker",
            binding("weapon"),
            "Pick the tracker fixed to the gun. It must differ from the head tracker.",
        );
        html += section(
            "2 · Where the tracker sits on the gun",
            `<div class="field-title">Muzzle tip → centre of the tracker</div>` +
                AXIS_DIRECTIONS.map(([label, directions], i) =>
                    distanceFields(`${TRACKER_FROM_MUZZLE}.${i}`, label, trackerFromMuzzle(i), directions)).join("") +
                mountTilt("weapon"),
            "Measure from the muzzle tip (where the bullet leaves) straight along the barrel and across it to the centre of the tracker. A tracker mounted back along the barrel is Behind. ELTS uses exactly this measurement. Tilt is how the tracker is turned relative to the barrel; use Align with the gun level and aimed at the screen.",
        );
        html += section(
            "3 · Zero the aim",
            `<button class="wide primary" data-action="aim-start">Aim at dots to zero the gun…</button>` +
                (aimUndo ? `<button class="wide" data-action="aim-undo">Undo last aim zero</button>` : "") +
                `<p class="help" id="aim-result">${aimSummary}</p>` +
                `<details><summary>Manual barrel aim correction</summary>${vector("weapon.bore", "Turn the shot line", true, TILT_NAMES)}</details>`,
            "Opens the screen window with dots. Wear the head tracker, make the window fullscreen on the TV, and aim through the sights at each dot, holding the gun the way you normally shoot. Your eye and each dot give the sight line, so the hold does not matter. This sets the tracker tilt and a small barrel correction so shots land on the sights at the target distance. Do Where the tracker sits and the head setup first.",
        );
        html += section(
            "Gun drawing (preview only)",
            `<details><summary>Model size and centre</summary>` +
                vector("weapon.muzzle", "Muzzle tip from drawing centre") +
                field("weapon.length", "Drawn length", { min: 0.1, max: 2.5 }) +
                `</details>`,
            "These only change the 3D drawing. The tracker-to-muzzle measurement above stays the same when you move the muzzle here.",
        );
        html += manualSection("weapon");
    } else {
        html += section(
            "Input source",
            `<label for="source-select">Pose source</label><select id="source-select"><option value="manual">Manual preview · no hardware</option><option value="live">Live SteamVR trackers</option></select><button id="connect-tracking" class="wide" data-action="connect">Connect / retry SteamVR</button><button class="wide" data-action="disconnect">Disconnect live tracking</button><p class="help" id="bridge-message"></p>`,
            "For VIVE trackers: pair devices in SteamVR, then restart START-CALIBRATION.cmd and answer Y to set up live tracking (once per PC). Select each device by serial under Head and Weapon.",
        );
        html += section(
            "Detected devices",
            '<div class="device-list" id="device-list"></div>',
        );
        html += section(
            "Tracking origin → room",
            vector("registration.position", "SteamVR origin in this room") +
                vector("registration.rotation", "SteamVR axes rotation", true),
            "Set this transform so measured tracker locations line up with your screen; the head-direction guide sets it for you. Redo it after the base station moves, SteamVR room setup, or recentering.",
        );
        html += section(
            "Perspective &amp; freshness",
            field("view.virtualDepth", "Virtual target behind screen", {
                min: 0.2,
                max: 30,
            }) +
                `<div class="single-row">${field("view.near", "Near clipping", { min: 0.001, max: 1 })}${field("view.far", "Far clipping", { min: 2, max: 100 })}</div>` +
                field("view.staleMs", "Hide poses older than", {
                    min: 100,
                    max: 500,
                    unit: "ms",
                }),
            "The screen preview is an off-axis window based on eye position. Head rotation changes eye offset and the head direction ray; it does not rotate the physical TV.",
        );
    }
    $("settings").innerHTML = html;
    if ($("source-select")) $("source-select").value = source;
    updateDevices(true);
    updateMonitor();
}

function updateDevices(force = false) {
    const devices = frame?.devices || [];
    if (!isScreen && source === "live") {
        // Keep saved assignments stable. A single tracker belongs to Head;
        // only a second, distinct real tracker can fill the Weapon slot.
        const available = devices.filter((d) => d.kind === "tracker" && d.connected)
            .sort((a, b) => a.serial.localeCompare(b.serial));
        for (const role of ["head", "weapon"]) {
            // A virtual headset is stationary and must never drive an object.
            if (devices.some((d) => d.serial === config[role].serial && d.kind === "headset")) {
                config[role].serial = "";
                changed(role + ".serial");
                force = true;
            }
            if (config[role].serial) continue;
            const device = available.find((d) =>
                d.serial !== config.head.serial && d.serial !== config.weapon.serial);
            if (!device) continue;
            config[role].serial = device.serial;
            changed(role + ".serial");
            force = true;
            notice(`Assigned ${device.serial} to ${role === "head" ? "Head" : "Weapon"}.`);
        }
    }
    const signature = JSON.stringify(
        devices.map((d) => [d.serial, d.kind, d.valid, d.connected]),
    );
    if (signature === deviceSignature && !force) return;
    deviceSignature = signature;
    for (const role of ["head", "weapon"]) {
        const select = $(role + "-serial");
        if (!select) continue;
        select.replaceChildren(new Option("Not assigned", ""));
        const list = devices.filter((d) => d.kind === "tracker" || d.kind === "controller");
        if (
            config[role].serial &&
            !list.some((d) => d.serial === config[role].serial)
        )
            select.add(
                new Option(
                    config[role].serial + " · unavailable",
                    config[role].serial,
                ),
            );
        for (const device of list)
            select.add(
                new Option(
                    `${device.serial} · ${device.kind} · ${device.valid ? "valid" : "unavailable"}`,
                    device.serial,
                ),
            );
        select.value = config[role].serial;
    }
    const list = $("device-list");
    if (list) {
        list.replaceChildren();
        if (!devices.length)
            list.textContent =
                source === "manual"
                    ? "Connect SteamVR to discover devices."
                    : "No devices available. Check SteamVR and tracker power.";
        for (const d of devices) {
            const row = document.createElement("div");
            row.className = "device-row";
            const title = document.createElement("strong");
            title.textContent = d.serial;
            const state = document.createElement("span");
            state.textContent = `${d.kind} · ${d.valid ? "valid pose" : d.reason}`;
            row.append(title, state);
            list.append(row);
        }
    }
}
function currentFrame() {
    return frame
        ? {
              ...frame,
              ageMs: (frame.ageMs ?? Infinity) + performance.now() - receivedAt,
          }
        : null;
}
function currentPoses() {
    const fresh = currentFrame();
    head = resolveObject(config, "head", source, fresh);
    weapon = resolveObject(config, "weapon", source, fresh);
    // A detached screen must not continue presenting an old dashboard setup.
    if (isScreen && performance.now() - lastScreenState > 1500)
        head = weapon = { valid: false, reason: "Waiting for dashboard" };
    return { head, weapon };
}
function formatVector(value, digits = 3) {
    return value.map((v) => v.toFixed(digits)).join(" / ");
}
function updateMonitor(result) {
    if (!config) return;
    const tilt = registrationTiltDegrees(config);
    $("registration-warning").hidden = tilt <= DIRECTION_LIMITS.roomTiltWarningDeg;
    $("registration-warning").textContent = `Tracking axes are tilted ${tilt.toFixed(1)}° from room up. Re-run Calibrate head directions. Position reset preserves the current alignment.`;
    currentPoses();
    $("reset-head").disabled = source !== "live" || !head.valid;
    $("calibrate-head").disabled = source !== "live" || !head.valid;
    for (const role of ["head", "weapon"]) {
        const resolved = role === "head" ? head : weapon;
        const relative = resolved.valid
            ? relativeAngles(
                  resolved.object,
                  config[role].reference,
                  source,
                  config[role].serial,
              )
            : null;
        const angles = resolved.valid
            ? relative || degrees(resolved.object.q)
            : null;
        $(role + "-quality").textContent = resolved.valid
            ? source === "live"
                ? "Live"
                : "Manual"
            : "Unavailable";
        $(role + "-angles").innerHTML = ["Pitch", "Yaw", "Roll"]
            .map(
                (name, i) =>
                    `<div><strong>${angles ? angles[i].toFixed(1) + "°" : "—"}</strong><span>${name}</span></div>`,
            )
            .join("");
        $(role + "-position").textContent = resolved.valid
            ? "XYZ " + formatVector(resolved.object.p.toArray()) + " m"
            : resolved.reason;
        $(role + "-reference").textContent = relative
            ? "Relative to captured forward"
            : "Room rotation · forward not set";
        $(role + "-forward").disabled = !resolved.valid;
        const detail = $(role + "-tracker-detail");
        if (detail)
            detail.textContent = resolved.valid
                ? "Tracker XYZ: " +
                  formatVector(resolved.tracker.p.toArray()) +
                  " m | Pitch / yaw / roll: " +
                  formatVector(degrees(resolved.tracker.q), 1) +
                  "°"
                : resolved.reason;
    }
    $("sample-status").textContent =
        source === "manual"
            ? "Manual values"
            : `Sample ${frame?.sequence ?? "—"} · ${Math.round(currentFrame()?.ageMs ?? 0)} ms old`;
    $("mode-label").textContent =
        source === "manual" ? "MANUAL PREVIEW" : "LIVE INPUT";
    $("mode-label").classList.toggle("live", source === "live");
    const status =
        source === "manual"
            ? "Example / manually entered poses · no hardware measurements"
            : frame?.message || "Connecting to local tracker bridge…";
    $("connection-status").textContent = status;
    if ($("bridge-message"))
        $("bridge-message").textContent =
            frame?.message || "Live tracking is off.";
    if (result) {
        const hit = result.hit;
        $("aim-readout").textContent = !weapon.valid
            ? "Screen intersection: no valid weapon pose"
            : hit
              ? `Aim ${hit.inside ? "on screen" : "outside screen"} · X ${hit.x.toFixed(3)} m / Y ${hit.y.toFixed(3)} m from centre · distance ${hit.distance.toFixed(2)} m`
              : "Aim misses screen plane · parallel or pointing away";
        $("eye-readout").textContent = result.eye
            ? "Eye XYZ " +
              formatVector(result.eye.toArray()) +
              " m · off-axis perspective"
            : "No valid head pose for perspective.";
        const distance = result.eye ? screenDistance(config, result.eye) : null;
        $("eye-distance-readout").textContent = distance === null
            ? "Eye → screen: unavailable"
            : `Eye → screen: ${distance.toFixed(3)} m · ${(distance / METRES_PER_INCH).toFixed(1)} in perpendicular to the display`;
        $("preview-message").hidden = result.previewValid;
        $("preview-message").textContent = result.graphicsUnavailable
            ? "3D graphics paused. Refresh the page and check browser graphics acceleration."
            : head.valid
              ? "The calculated eye is on or behind the screen. Check the eye measurement and room alignment."
              : head.reason || "No valid head pose";
        $("graphics-error").hidden = !scene.roomContextLost;
        $("screen-status").textContent =
            `${source === "manual" ? "MANUAL PREVIEW" : "LIVE INPUT"} · ${result.previewValid ? "Eye-position perspective" : $("preview-message").textContent}`;
    }
}

function captureForward(role) {
    currentPoses();
    const resolved = role === "head" ? head : weapon;
    if (!resolved.valid) return notice("A current valid pose is required.");
    config[role].reference = {
        quaternion: resolved.object.q.toArray(),
        source,
        serial: source === "live" ? config[role].serial : "",
    };
    changed();
    notice(
        `${role === "head" ? "Head" : "Weapon"} forward captured for ${source === "live" ? "this live tracker" : "manual preview"}.`,
    );
}
function alignMount(role) {
    currentPoses();
    const resolved = role === "head" ? head : weapon;
    if (!resolved.valid) return notice("A current valid pose is required.");
    if (source !== "live")
        return notice(
            "Mount alignment requires Live input. Enter mount angles directly for manual tests.",
        );
    if (
        !confirm(
            `Hold the ${role === "head" ? "head looking" : "gun pointing"} straight ahead, parallel to the screen normal, with no roll. Use the current pose to set all three mounting angles?`,
        )
    )
        return;
    // q_tracker = q_object * q_mount; desired object forward is screen -Z.
    config[role].mount.rotation = degrees(
        screenPose(config).q.clone().invert().multiply(resolved.tracker.q),
    );
    config[role].reference = null;
    changed(role + ".mount");
    renderSettings();
    notice(
        "Mount rotation aligned. Position offsets still need measured values.",
    );
}
function setSource(next) {
    source = next;
    syncScreen();
    updateMonitor();
    if (next === "live")
        api("tracking", { enabled: true }).catch((e) => notice(e.message));
}

$("settings").addEventListener("change", (event) => {
    const input = event.target;
    if (input.id === "source-select") {
        setSource(input.value);
        return;
    }
    if (input.dataset.role) {
        const role = input.dataset.role,
            other = role === "head" ? "weapon" : "head";
        if (input.value && input.value === config[other].serial) {
            input.value = config[role].serial;
            return notice(
                "Head and weapon must have different tracker serials.",
            );
        }
        config[role].serial = input.value;
        changed(role + ".serial");
        return;
    }
    const distance = input.closest("[data-distance-path]");
    if (distance) {
        const path = distance.dataset.distancePath;
        let metres;
        try {
            metres = readDistance(path);
        } catch (error) {
            return notice(error.message);
        }
        if (path === "person.height" &&
            (metres < 0.5 || metres > 2.7 || metres < 2 * config.person.radius))
            return notice("Height must be between 0.5 and 2.7 m and at least twice the body radius.");
        if (Math.abs(metres) > 100)
            return notice("The distance must be within 100 metres of the origin.");
        if (path.startsWith(TRACKER_FROM_MUZZLE + ".")) {
            // Store as the drawing-centre mount position the profile format uses.
            const i = Number(path.slice(TRACKER_FROM_MUZZLE.length + 1));
            config.weapon.mount.position[i] = metres + config.weapon.muzzle[i];
            changed("weapon.mount.position");
            return;
        }
        if (path.startsWith(TRACKER_TO_EYE + ".")) {
            const i = Number(path.slice(TRACKER_TO_EYE.length + 1));
            config.head.eye[i] = config.head.mount.position[i] + metres;
            changed("head.eye");
            return;
        }
        set(path, metres);
        changed(path);
        return;
    }
    const path = input.dataset.path;
    if (!path) return;
    const value =
        input.type === "checkbox" ? input.checked : input.valueAsNumber;
    if (
        input.type !== "checkbox" &&
        (!input.checkValidity() || !Number.isFinite(value))
    ) {
        input.reportValidity();
        return;
    }
    const before = get(path);
    set(path, value);
    if (
        config.view.near >= config.view.far ||
        config.person.height < 2 * config.person.radius
    ) {
        set(path, before);
        input.value = before;
        return notice("Check clipping distances and person dimensions.");
    }
    // Moving the drawn muzzle keeps the measured tracker-to-muzzle offset.
    const muzzleAxis = path.match(/^weapon\.muzzle\.(\d)$/);
    if (muzzleAxis) {
        config.weapon.mount.position[muzzleAxis[1]] += value - before;
        changed("weapon.mount.position");
        return;
    }
    const headMountAxis = path.match(/^head\.mount\.position\.(\d)$/);
    if (headMountAxis) {
        // Re-express the same measured eye location after moving the head-centre
        // reference. The tracker-to-eye vector must not change with that choice.
        config.head.eye[headMountAxis[1]] += value - before;
    }
    changed(path);
});
$("settings").addEventListener("click", async (event) => {
    const action = event.target.closest("[data-action]")?.dataset.action;
    try {
        if (action === "diagonal") {
            const diagonal = $("diagonal").valueAsNumber;
            if (!$("diagonal").checkValidity() || !Number.isFinite(diagonal))
                return $("diagonal").reportValidity();
            const aspect = Number($("aspect").value),
                height = (diagonal * METRES_PER_INCH) / Math.sqrt(1 + aspect * aspect);
            config.screen.height = Number(height.toFixed(4));
            config.screen.width = Number((height * aspect).toFixed(4));
            changed("screen");
            renderSettings();
        } else if (action?.startsWith("align-")) alignMount(action.slice(6));
        else if (action === "aim-start") startAim();
        else if (action === "aim-undo") undoAim();
        else if (action === "connect") {
            setSource("live");
            if ($("source-select")) $("source-select").value = "live";
        } else if (action === "disconnect") {
            await api("tracking", { enabled: false });
            setSource("manual");
            renderSettings();
        }
    } catch (error) {
        notice(error.message);
    }
});
document.querySelectorAll("[data-tab]").forEach(
    (button) =>
        (button.onclick = () => {
            tab = button.dataset.tab;
            document
                .querySelectorAll("[data-tab]")
                .forEach((b) =>
                    b.setAttribute("aria-pressed", String(b === button)),
                );
            renderSettings();
        }),
);
document
    .querySelectorAll("[data-view]")
    .forEach(
        (button) => (button.onclick = () => scene.setView(button.dataset.view)),
    );
// Keep the measured distance across reloads, including guide Apply.
function showResetDistance() {
    const referenceLabel = resetReference === "eye" ? "Viewing eye" : "Head centre";
    $("reset-reference").value = resetReference;
    $("reset-distance-fields").innerHTML = distanceFields(
        "reset-distance", `${referenceLabel} distance from screen`, resetDistance,
    );
    $("reset-height-fields").innerHTML = distanceFields(
        "reset-height", `${referenceLabel} above floor at reset`,
        resetHeight ?? screenPose(config).p.y,
    );
}
$("reset-reference").addEventListener("change", () => {
    resetReference = $("reset-reference").value;
    showResetDistance();
    persistDraft();
    notice("Enter distance and height measured from the selected point. Apply them with Reset position.");
});
$("reset-height-fields").addEventListener("change", () => {
    try {
        const height = readDistance("reset-height");
        if (height > 10) throw new Error("Height must be below approximately 32 feet 10 inches.");
        resetHeight = height;
        persistDraft();
    } catch (error) { notice(error.message); }
});
$("reset-distance-fields").addEventListener("change", () => {
    try {
        const distance = readDistance("reset-distance");
        if (distance < 0.25 || distance > 10)
            throw new Error("Reset distance must be between approximately 10 inches and 32 feet 10 inches.");
        resetDistance = distance;
        persistDraft();
    } catch (error) { notice(error.message); }
});
$("head-forward").onclick = () => captureForward("head");
$("reset-head").onclick = () => {
    currentPoses();
    if (source !== "live" || !head.valid)
        return notice("Connect a head tracker with a current valid pose first.");
    try {
        config.registration = recenteredRegistration(
            config, head.object, readDistance("reset-distance"), readDistance("reset-height"), resetReference,
        );
        changed("registration");
        renderSettings();
        notice("Position reset at the entered measurements. Room alignment and head orientation are preserved.");
    } catch (error) { notice(error.message); }
};
$("weapon-forward").onclick = () => captureForward("weapon");
installHeadCalibration({
    readState: () => ({ config, source, frame: currentFrame() }),
    readResetMeasurements: () => ({ reference: resetReference,
        distance: readDistance("reset-distance"), height: readDistance("reset-height") }),
    applyResult: (result) => {
        resetDistance = result.summary.distance;
        resetHeight = result.summary.height;
        resetReference = result.summary.reference;
        showResetDistance();
        config.head.mount.rotation = result.mountRotation;
        config.registration = result.registration;
        changed("registration");
        renderSettings();
    },
    notify: notice,
});
$("room-canvas").addEventListener("keydown", (event) => {
    const delta = scene.camera.position.clone().sub(scene.controls.target);
    if (event.key === "ArrowLeft" || event.key === "ArrowRight")
        delta.applyAxisAngle(
            vec([0, 1, 0]),
            event.key === "ArrowLeft" ? 0.1 : -0.1,
        );
    else if (event.key === "ArrowUp" || event.key === "ArrowDown")
        delta.y = Math.max(
            0.1,
            delta.y + (event.key === "ArrowUp" ? 0.25 : -0.25),
        );
    else if (event.key === "+" || event.key === "=") delta.multiplyScalar(0.9);
    else if (event.key === "-") delta.multiplyScalar(1.1);
    else return;
    event.preventDefault();
    scene.camera.position.copy(scene.controls.target).add(delta);
    scene.controls.update();
});

// Participant IDs are whole numbers only; keep this aligned with server.py.
let savedParticipants = [];
function participantLabel(id = participantId) {
    return id ? `Participant ${id}` : "Unsaved participant";
}
const idPicker = participantIdPicker($("participant-id"));
function savedParticipant(id) {
    return savedParticipants.find((p) => String(p.id) === id);
}
async function refreshProfiles() {
    const state = await api("state");
    savedParticipants = state.participants;
    $("profile-list").replaceChildren(
        ...savedParticipants.map(
            (p) => new Option(participantLabel(String(p.id)) + (p.available === false ? " · Unreadable" : ""), p.id),
        ),
    );
    if (savedParticipant(participantId))
        $("profile-list").value = participantId;
    else if (savedParticipants.some(p => p.available !== false))
        $("profile-list").value = String(savedParticipants.find(p => p.available !== false).id);
    updateProfileActions();
}
function updateProfileActions() {
    const blocked = savedParticipant(idPicker.get())?.available === false;
    $("save-profile").disabled = blocked;
    $("unreadable-profile").hidden = !blocked;
    const selection = savedParticipant($("profile-list").value);
    $("load-profile").disabled = !selection || selection.available === false;
}
$("participant-id").addEventListener("change", updateProfileActions);
$("profile-list").addEventListener("change", updateProfileActions);
async function showProfiles() {
    $("profile-result").textContent = "";
    try {
        await refreshProfiles();
        const next = nextUnusedId(savedParticipants);
        $("next-participant").disabled = next === null;
        idPicker.set(participantId || next || 0);
        updateProfileActions();
        $("profiles-dialog").showModal();
        idPicker.focus();
    } catch (e) {
        notice(e.message);
    }
}
$("profiles-button").onclick = showProfiles;
$("save-button").onclick = showProfiles;
$("next-participant").onclick = () => {
    const next = nextUnusedId(savedParticipants);
    if (next !== null) idPicker.set(next);
    updateProfileActions();
};
$("save-profile").onclick = async () => {
    try {
        const id = idPicker.get();
        if (
            savedParticipant(id) &&
            !confirm(
                `Replace the saved calibration for participant ${id} with the current settings?`,
            )
        )
            return;
        await api("participant", { participantId: Number(id), config });
        participantId = id;
        modified = false;
        persistDraft();
        $("save-state").textContent = "Saved · " + participantLabel();
        $("profile-result").textContent =
            `Saved. ELTS loads this calibration when participant ${id} is started.`;
        await refreshProfiles();
    } catch (error) {
        $("profile-result").textContent = error.message;
    }
};
function replaceConfig(next, id = "") {
    config = next;
    participantId = id;
    scene.configure(config);
    changed();
    renderSettings();
}
$("load-profile").onclick = async () => {
    if (
        modified &&
        !confirm(
            "Replace the current unsaved settings with the selected participant's calibration?",
        )
    )
        return;
    try {
        const saved = await api(
            "participant?id=" + encodeURIComponent($("profile-list").value),
        );
        replaceConfig(saved.calibration, String(saved.participantId));
        modified = false;
        persistDraft();
        $("save-state").textContent = "Loaded · " + participantLabel();
        $("profiles-dialog").close();
    } catch (error) {
        $("profile-result").textContent = error.message;
    }
};
$("export-profile").onclick = async () => {
    try {
        await api("validate", config);
        const blob = new Blob([JSON.stringify(config, null, 2)], {
                type: "application/json",
            }),
            url = URL.createObjectURL(blob),
            a = document.createElement("a");
        a.href = url;
        a.download =
            (participantId
                ? "participant-" + participantId
                : "calibration") + ".json";
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (error) {
        $("profile-result").textContent = error.message;
    }
};
$("import-profile").onclick = () => $("import-file").click();
$("import-file").onchange = async () => {
    const file = $("import-file").files[0];
    if (!file) return;
    try {
        if (file.size > 100000)
            throw new Error("Profile must be under 100 KB.");
        const next = await api("validate", JSON.parse(await file.text()));
        if (
            modified &&
            !confirm(
                "Replace current unsaved settings with the imported profile?",
            )
        )
            return;
        // Imported settings are not tied to a participant until saved under an ID.
        replaceConfig(next);
        idPicker.set(nextUnusedId(savedParticipants) ?? 0);
        updateProfileActions();
        $("profile-result").textContent =
            "Imported as an unsaved draft. Choose a participant ID and save to keep it.";
    } catch (error) {
        $("profile-result").textContent = "Import failed: " + error.message;
    } finally {
        $("import-file").value = "";
    }
};
$("save-room").onclick = async () => {
    try {
        await api("room", config);
        $("profile-result").textContent = "Station room saved. Restart ELTS to use it.";
    } catch (error) {
        $("profile-result").textContent = error.message;
    }
};
$("reset-profile").onclick = () => {
    if (
        !confirm(
            "Restore example dimensions and clear current bindings and forward references? Saved profile files are kept.",
        )
    )
        return;
    replaceConfig(structuredClone(defaults));
    $("profiles-dialog").close();
};
$("open-screen").onclick = () => { openScreen(); };
$("fullscreen").onclick = () =>
    document.fullscreenElement
        ? document.exitFullscreen()
        : document.body.requestFullscreen();
const aimScreen = isScreen
    ? installAimScreen({ readState: () => ({ config, source, frame: currentFrame() }), send: (m) => channel.postMessage(m) })
    : null;
channel.onmessage = (event) => {
    if (isScreen && event.data.config) {
        config = event.data.config;
        source = event.data.source;
        lastScreenState = performance.now();
        scene?.configure(config);
    }
    if (isScreen && event.data.type === "aim-start" && config) aimScreen.start(event.data.id);
    if (!isScreen && event.data.id && event.data.id === aimRun?.id) {
        if (event.data.type === "aim-ack") aimRun.acknowledged = true;
        else if (event.data.type === "aim-result") { aimRun = null; applyAim(event.data.result); }
        else if (event.data.type === "aim-cancel") { aimRun = null; notice(event.data.message || "Aim zero cancelled."); }
    }
};
function resizePreview() {
    if (isScreen) return;
    $("preview-wrap").style.aspectRatio = String(
        config.screen.width / config.screen.height,
    );
}
async function poll() {
    try {
        frame = await api("tracking");
        receivedAt = performance.now();
        pollFailures = 0;
        updateDevices();
    } catch (error) {
        pollFailures++;
        frame = {
            status: "unavailable",
            message: "Local server unavailable. Restart START-CALIBRATION.cmd.",
            devices: [],
            ageMs: 0,
        };
        receivedAt = performance.now();
    }
    setTimeout(poll, pollFailures ? 1000 : 50);
}
function animate(now) {
    const { head, weapon } = currentPoses();
    const result = scene.update(head, weapon);
    resizePreview();
    scene.draw();
    if (now - lastMonitor > 100) {
        const trigger = triggerIndicator(source, config.weapon.serial, currentFrame(), config.view.staleMs);
        $("trigger-indicator").dataset.state = trigger.state;
        $("trigger-state").textContent = trigger.text;
        $("trigger-detail").textContent = trigger.detail;
        updateMonitor(result);
        lastMonitor = now;
    }
    requestAnimationFrame(animate);
}
async function start() {
    try {
        const initial = await api("state");
        datasetGeneration = initial.generation || "";
        token = initial.token;
        defaults = initial.defaults;
        config = structuredClone(defaults);
        try {
            draftId = localStorage.getItem(DRAFT_ID_KEY) || crypto.randomUUID().replaceAll("-", "");
            localStorage.setItem(DRAFT_ID_KEY, draftId);
            const legacy = JSON.parse(localStorage.getItem(DRAFT_KEY));
            const stored = await api("draft?id=" + draftId);
            const draft = stored || (legacy && (legacy.generation || "") === datasetGeneration ? legacy : null);
            if (draft?.config) {
                config = await api("validate", draft.config);
                // Existing values meant head centre. Never silently subtract
                // the eye offset again by relabelling an older saved distance.
                resetReference = draft.resetReference === "eye" ? "eye" : "head";
                participantId = normalizedId(draft.participantId ?? "");
                if (Number.isFinite(draft.resetDistance) &&
                    draft.resetDistance >= 0.25 && draft.resetDistance <= 10)
                    resetDistance = draft.resetDistance;
                if (Number.isFinite(draft.resetHeight) &&
                    draft.resetHeight >= 0 && draft.resetHeight <= 10)
                    resetHeight = draft.resetHeight;
                modified = true;
                if (!stored && !isScreen && !await persistDraft())
                    throw new Error("Could not migrate the previous calibration draft.");
                $("save-state").textContent = "Restored calibration draft";
            }
            if (!isScreen || stored || !draft) localStorage.removeItem(DRAFT_KEY);
        } catch {
            notice(
                "The previous browser draft could not be loaded. Example setup restored.",
            );
        }
        showResetDistance();
        scene = new CalibrationScene(
            $("room-canvas"),
            $("preview-canvas"),
            $("scene-labels"),
        );
        scene.configure(config);
        renderSettings();
        poll();
        // Reset runs in the station's origin. Discard this tab's old profile on
        // first contact, including when a suspended tab becomes visible again.
        const checkDataset = async () => {
            try {
                const state = await api("dataset");
                if (typeof state.generation === "string" && state.generation !== datasetGeneration) {
                    try { localStorage.removeItem(DRAFT_KEY); } catch { /* Storage may be disabled. */ }
                    location.reload();
                }
            } catch { /* A reset may temporarily hold the generation file exclusively. */ }
        };
        setInterval(checkDataset, 3000);
        document.addEventListener("visibilitychange", () => { if (!document.hidden) checkDataset(); });
        if (!isScreen) setSource("live");
        requestAnimationFrame(animate);
        if (!isScreen) setInterval(syncScreen, 250);
    } catch (error) {
        $("connection-status").textContent =
            "Could not start: " + error.message;
        notice(
            "Could not start the 3D workspace. Check WebGL support and restart the launcher. " +
                error.message,
        );
    }
}
start();
