// Geometry uses native OpenVR / Three.js right-handed axes throughout. It is
// deliberately separate from ELTS's left-handed Unity configuration format.
import * as T from "./vendor/build/three.module.js";
const radians = Math.PI / 180;
export const vec = (a) => new T.Vector3(...a);
export function rotation(degrees) {
    return new T.Quaternion().setFromEuler(
        new T.Euler(...degrees.map((v) => v * radians), "YXZ"),
    );
}
export function degrees(q) {
    const e = new T.Euler().setFromQuaternion(q, "YXZ");
    return [e.x, e.y, e.z].map((v) => v / radians);
}
export function pose(settings) {
    return { p: vec(settings.position), q: rotation(settings.rotation) };
}
export function compose(a, b) {
    return {
        p: b.p.clone().applyQuaternion(a.q).add(a.p),
        q: a.q.clone().multiply(b.q),
    };
}
export function inverse(a) {
    const q = a.q.clone().invert();
    return { p: a.p.clone().negate().applyQuaternion(q), q };
}
export function point(a, local) {
    return vec(local).applyQuaternion(a.q).add(a.p);
}
export function fromNative(rows) {
    const m = new T.Matrix4().set(
        ...rows[0],
        ...rows[1],
        ...rows[2],
        0,
        0,
        0,
        1,
    );
    return {
        p: new T.Vector3().setFromMatrixPosition(m),
        q: new T.Quaternion().setFromRotationMatrix(m).normalize(),
    };
}
export function screenPose(config) {
    const s = config.screen;
    const lower = {
        p: new T.Vector3(s.x, s.bottom, s.z),
        q: rotation(s.rotation),
    };
    return { p: point(lower, [0, s.height / 2, 0]), q: lower.q };
}
// Move the tracking origin, not the raw samples or measured mounting offsets.
// The current head becomes screen-centred, facing screen-local -Z.
export function recenteredRegistration(config, currentHead, distance) {
    if (!Number.isFinite(distance) || distance < 0.25 || distance > 10)
        throw new Error("Choose a reset distance between 0.25 and 10 metres.");
    const screen = screenPose(config);
    const target = { p: point(screen, [0, 0, distance]), q: screen.q };
    const registration = compose(
        compose(target, inverse(currentHead)),
        pose(config.registration),
    );
    return { position: registration.p.toArray(), rotation: degrees(registration.q) };
}
export function resolveObject(config, role, source, frame) {
    const settings = config[role];
    let tracker;
    if (source === "manual") {
        const object = pose(settings.manual);
        return {
            valid: true,
            object,
            tracker: compose(object, pose(settings.mount)),
            reason: "Manual test pose",
        };
    }
    if (!settings.serial) return { valid: false, reason: "Assign a tracker" };
    if (config.head.serial === config.weapon.serial)
        return { valid: false, reason: "Tracker assigned twice" };
    const device = frame?.devices?.find(
        (d) => d.serial === settings.serial && d.kind !== "base station",
    );
    if (
        frame?.status !== "connected" ||
        !device?.valid ||
        !device.matrix ||
        frame.ageMs > config.view.staleMs
    ) {
        return { valid: false, reason: device?.reason || "No valid live pose" };
    }
    tracker = compose(pose(config.registration), fromNative(device.matrix));
    // mount is tracker-on-object, so recover the object using its inverse.
    return {
        valid: true,
        tracker,
        object: compose(tracker, inverse(pose(settings.mount))),
        reason: "Live tracker",
    };
}
export function relativeAngles(object, reference, source, serial) {
    if (
        !reference ||
        reference.source !== source ||
        reference.serial !== (source === "live" ? serial : "")
    )
        return null;
    return degrees(
        new T.Quaternion(...reference.quaternion).invert().multiply(object.q),
    );
}
export function hitScreen(origin, direction, config) {
    const local = inverse(screenPose(config));
    const p = origin.clone().applyQuaternion(local.q).add(local.p);
    const d = direction.clone().applyQuaternion(local.q);
    if (Math.abs(d.z) < 1e-8) return null;
    const distance = -p.z / d.z;
    if (distance <= 0) return null;
    const hit = p.addScaledVector(d, distance);
    return {
        x: hit.x,
        y: hit.y,
        distance,
        inside:
            Math.abs(hit.x) <= config.screen.width / 2 &&
            Math.abs(hit.y) <= config.screen.height / 2,
        world: origin.clone().addScaledVector(direction, distance),
    };
}
export function offAxis(camera, eye, config) {
    const s = screenPose(config);
    const localEye = eye.clone().sub(s.p).applyQuaternion(s.q.clone().invert());
    const near = config.view.near,
        far = config.view.far;
    if (localEye.z <= near) return false;
    const scale = near / localEye.z;
    camera.position.copy(eye);
    camera.quaternion.copy(s.q);
    camera.projectionMatrix.makePerspective(
        (-config.screen.width / 2 - localEye.x) * scale,
        (config.screen.width / 2 - localEye.x) * scale,
        (config.screen.height / 2 - localEye.y) * scale,
        (-config.screen.height / 2 - localEye.y) * scale,
        near,
        far,
    );
    camera.projectionMatrixInverse.copy(camera.projectionMatrix).invert();
    camera.updateMatrixWorld();
    return true;
}
