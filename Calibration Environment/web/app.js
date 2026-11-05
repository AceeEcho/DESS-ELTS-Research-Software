import { METRES_PER_INCH, distanceFields, readDistance } from "./distance-fields.js";
import { CalibrationScene } from "./scene.js";
import { installHeadCalibration } from "./head-calibration-ui.js";
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
    recenteredRegistration,
} from "./math.js";

const $ = (id) => document.getElementById(id);
const query = new URLSearchParams(location.search);
const isScreen = query.has("screen");
if (isScreen) document.body.classList.add("screen-only");
const DRAFT_KEY = "elts-calibration-v1-draft";
let resetHeight; // Undefined in older drafts, initially uses screen-centre height.
let resetDistance = 2; // Metres, stored with the local draft for repeatable resets.
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
    profileName = "My calibration";
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
    try {
        localStorage.setItem(
            DRAFT_KEY,
            JSON.stringify({ config, profileName, resetDistance, resetHeight }),
        );
    } catch {
        notice(
            "Browser draft storage is unavailable. Save a profile to keep changes.",
        );
    }
}
function syncScreen() {
    if (!isScreen) channel.postMessage({ config, source });
}
function changed(path = "") {
    modified = true;
    $("save-state").textContent = "Unsaved profile changes";
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

function vector(path, label, angles = false) {
    const names = angles
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
function mountSection(role) {
    return section(
        "Tracker mounting offset",
        vector(role + ".mount.position", "Tracker centre on object") +
            vector(
                role + ".mount.rotation",
                "Tracker orientation on object",
                true,
            ) +
            `<button class="wide" data-action="align-${role}">Align ${role === "head" ? "head" : "gun"} forward to screen</button>`,
        "Position is measured from the " +
            (role === "head" ? "head centre" : "receiver centre") +
            " to the tracker, in object-local axes. −Z is forward. Live object pose uses the inverse of this mounting transform.",
    );
}
function manualSection(role) {
    return section(
        "Manual test pose",
        vector(
            role + ".manual.position",
            role === "head" ? "Head centre in room" : "Receiver centre in room",
        ) + vector(role + ".manual.rotation", "Object rotation", true),
        "Used only in Manual preview. Change these values to test offsets and perspective without equipment. Positive yaw turns left; positive pitch looks up.",
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
        config.baseStations.forEach((base, i) => {
            html += section(
                `Base station ${i + 1}`,
                check(`baseStations.${i}.enabled`, "Show this base station") +
                    profileDistance(`baseStations.${i}.position.0`, "X · left / right", ["Right", "Left"]) +
                    profileDistance(`baseStations.${i}.position.1`, "Y · centre height from floor", ["Above", "Below"]) +
                    profileDistance(`baseStations.${i}.position.2`, "Z · front / back", ["Back", "Front"]) +
                    vector(`baseStations.${i}.rotation`, "Orientation", true) +
                    `<label class="field" for="base-device-${i}">Copy a live base station pose</label><select id="base-device-${i}" class="base-device" data-index="${i}"></select><button class="wide" data-action="base-${i}">Copy current station pose</button>`,
                "Y is the base station centre height above the floor. These values place the model; they do not recalibrate SteamVR.",
            );
        });
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
        html += mountSection("head");
        html += section(
            "Eye &amp; person",
            `<div class="field-title">Viewing eye from head centre</div>` +
                profileDistance("head.eye.0", "X · left / right", ["Right", "Left"]) +
                profileDistance("head.eye.1", "Y · up / down", ["Up", "Down"]) +
                profileDistance("head.eye.2", "Z · front / back", ["Back", "Front"]) +
                profileDistance("person.height", "Participant height") +
                field("person.radius", "Body radius", { min: 0.05, max: 0.6 }),
            "Participant height sizes the body preview. Set the tracker mount and viewing-eye offsets separately for the actual headband fit.",
        );
        html += manualSection("head");
    } else if (tab === "weapon") {
        html += section(
            "Weapon tracker",
            binding("weapon"),
            "The gun is a schematic rifle. Its origin is the receiver centre and its forward axis is −Z.",
        );
        html += mountSection("weapon");
        html += section(
            "Muzzle &amp; bore",
            vector("weapon.muzzle", "Muzzle from receiver centre") +
                vector("weapon.bore", "Bore angular correction", true) +
                field("weapon.length", "Visual model length", {
                    min: 0.1,
                    max: 2.5,
                }),
            "The aim ray starts at the configured muzzle. Model length changes only the drawing; measure the muzzle and mount offsets separately.",
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
            "Standing-origin registration is independent of base-station placement. Set this transform so measured tracker locations line up with your screen. Recheck after SteamVR room setup or recentering.",
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
    document.querySelectorAll(".base-device").forEach((select) => {
        const old = select.value;
        select.replaceChildren(new Option("Select live base station", ""));
        devices
            .filter((d) => d.kind === "base station")
            .forEach((d) =>
                select.add(
                    new Option(
                        `${d.serial} · ${d.valid ? "valid" : "unavailable"}`,
                        d.serial,
                    ),
                ),
            );
        if (devices.some((d) => d.serial === old)) select.value = old;
    });
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
        $("preview-message").hidden = result.previewValid;
        $("preview-message").textContent = result.graphicsUnavailable
            ? "3D graphics paused. Refresh the page and check browser graphics acceleration."
            : head.valid
              ? "Eye must be in front of the screen, beyond the near clip."
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
        else if (action === "connect") {
            setSource("live");
            if ($("source-select")) $("source-select").value = "live";
        } else if (action === "disconnect") {
            await api("tracking", { enabled: false });
            setSource("manual");
            renderSettings();
        } else if (action?.startsWith("base-")) {
            const index = Number(action.slice(5)),
                serial = $("base-device-" + index).value;
            const fresh = currentFrame(),
                device = fresh?.devices.find((d) => d.serial === serial);
            if (
                fresh?.status !== "connected" ||
                !device?.valid ||
                fresh.ageMs > config.view.staleMs
            )
                return notice(
                    "Select a base station with a current valid pose.",
                );
            const p = compose(
                pose(config.registration),
                fromNative(device.matrix),
            );
            config.baseStations[index] = {
                enabled: true,
                position: p.p.toArray(),
                rotation: degrees(p.q),
            };
            changed();
            renderSettings();
            notice("Copied base station pose into this profile.");
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
    $("reset-distance-fields").innerHTML = distanceFields(
        "reset-distance", "Reset distance from screen", resetDistance,
    );
    $("reset-height-fields").innerHTML = distanceFields(
        "reset-height", "Head centre above floor at reset",
        resetHeight ?? screenPose(config).p.y,
    );
}
$("reset-height-fields").addEventListener("change", () => {
    try {
        const height = readDistance("reset-height");
        if (height > 10) throw new Error("Head height must be below approximately 32 feet 10 inches.");
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
            config, head.object, readDistance("reset-distance"), readDistance("reset-height"),
        );
        changed("registration");
        renderSettings();
        notice("Head reset at the entered distance and floor height, facing the screen. Live movement continues from here.");
    } catch (error) { notice(error.message); }
};
$("weapon-forward").onclick = () => captureForward("weapon");
installHeadCalibration({
    readState: () => ({ config, source, frame: currentFrame() }),
    applyResult: (result) => {
        resetDistance = result.summary.distance;
        resetHeight = readDistance("reset-height");
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

async function refreshProfiles() {
    const state = await api("state");
    $("profile-list").replaceChildren(
        ...state.profiles.map((name) => new Option(name, name)),
    );
    $("load-profile").disabled = !state.profiles.length;
    if (state.profiles.includes(profileName))
        $("profile-list").value = profileName;
}
async function showProfiles() {
    $("profile-name").value = profileName;
    $("profile-result").textContent = "";
    try {
        await refreshProfiles();
        $("profiles-dialog").showModal();
    } catch (e) {
        notice(e.message);
    }
}
$("profiles-button").onclick = showProfiles;
$("save-button").onclick = showProfiles;
$("save-profile").onclick = async () => {
    try {
        const name = $("profile-name").value.trim();
        if (
            [...$("profile-list").options].some((o) => o.value === name) &&
            !confirm(
                `Replace the saved profile “${name}” with the current settings?`,
            )
        )
            return;
        await api("profile", { name, config });
        profileName = name;
        modified = false;
        persistDraft();
        $("save-state").textContent = "Saved · " + name;
        $("profile-result").textContent =
            "Saved. The next START-ELTS launch will use the latest saved profile for synthetic rig geometry.";
        await refreshProfiles();
    } catch (error) {
        $("profile-result").textContent = error.message;
    }
};
function replaceConfig(next, name) {
    config = next;
    profileName = name;
    scene.configure(config);
    changed();
    renderSettings();
}
$("load-profile").onclick = async () => {
    if (
        modified &&
        !confirm(
            "Replace the current unsaved settings with the selected profile?",
        )
    )
        return;
    try {
        const name = $("profile-list").value;
        replaceConfig(
            await api("profile?name=" + encodeURIComponent(name)),
            name,
        );
        modified = false;
        $("save-state").textContent = "Loaded · " + name;
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
            (profileName.replace(/[^a-zA-Z0-9 _-]/g, "_") ||
                "Calibration profile") + ".json";
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
        replaceConfig(
            next,
            file.name
                .replace(/\.json$/i, "")
                .replace(/[^a-zA-Z0-9 _-]/g, "_")
                .slice(0, 64) || "Imported profile",
        );
        $("profile-name").value = profileName;
        $("profile-result").textContent =
            "Imported as an unsaved draft. Save to keep a named profile.";
    } catch (error) {
        $("profile-result").textContent = "Import failed: " + error.message;
    } finally {
        $("import-file").value = "";
    }
};
$("reset-profile").onclick = () => {
    if (
        !confirm(
            "Restore example dimensions and clear current bindings and forward references? Saved profile files are kept.",
        )
    )
        return;
    replaceConfig(structuredClone(defaults), "My calibration");
    $("profile-name").value = profileName;
    $("profiles-dialog").close();
};
$("open-screen").onclick = () => {
    window.open(
        "/?screen=1&session=" + encodeURIComponent(session),
        "elts-calibration-screen-" + session,
        "width=1280,height=720",
    );
    syncScreen();
};
$("fullscreen").onclick = () =>
    document.fullscreenElement
        ? document.exitFullscreen()
        : document.body.requestFullscreen();
channel.onmessage = (event) => {
    if (isScreen && event.data.config) {
        config = event.data.config;
        source = event.data.source;
        lastScreenState = performance.now();
        scene?.configure(config);
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
        updateMonitor(result);
        lastMonitor = now;
    }
    requestAnimationFrame(animate);
}
async function start() {
    try {
        const initial = await api("state");
        token = initial.token;
        defaults = initial.defaults;
        config = structuredClone(defaults);
        try {
            const draft = JSON.parse(localStorage.getItem(DRAFT_KEY));
            if (draft?.config) {
                config = await api("validate", draft.config);
                profileName = draft.profileName || "My calibration";
                if (Number.isFinite(draft.resetDistance) &&
                    draft.resetDistance >= 0.25 && draft.resetDistance <= 10)
                    resetDistance = draft.resetDistance;
                if (Number.isFinite(draft.resetHeight) &&
                    draft.resetHeight >= 0 && draft.resetHeight <= 10)
                    resetHeight = draft.resetHeight;
                modified = true;
                $("save-state").textContent = "Restored browser draft";
            }
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
