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
// Perpendicular distance, in metres, from a room point to the display plane.
// Positive is on the viewer's side; it is not distance to the screen centre.
export function screenDistance(config, position) {
    const screen = screenPose(config);
    return position.clone().sub(screen.p).applyQuaternion(screen.q.clone().invert()).z;
}
export function registrationTiltDegrees(config) {
    const up = vec([0, 1, 0]);
    return up.angleTo(up.clone().applyQuaternion(rotation(config.registration.rotation))) / radians;
}
// This is measured in head-facing axes, not the arbitrarily rotated puck axes.
// Keep the existing profile fields: eye minus mount is the tracker-to-eye vector.
export function trackerToEye(config) {
    return vec(config.head.eye).sub(vec(config.head.mount.position));
}
export function resetReferencePoint(config, head, reference = "eye") {
    if (reference === "eye") return point(head, config.head.eye);
    if (reference === "head") return head.p.clone();
    throw new Error("Choose viewing eye or head centre as the measurement reference.");
}
// Place the selected measurement point at the requested distance and floor
// height. This determines a position only; the screen never sets gravity-up.
export function resetTargetPosition(config, distance, height) {
    if (!Number.isFinite(distance) || distance < 0.25 || distance > 10)
        throw new Error("Choose a reset distance between 0.25 and 10 metres.");
    const screen = screenPose(config);
    const target = point(screen, [0, 0, distance]);
    if (height !== undefined) {
        if (!Number.isFinite(height) || height < 0 || height > 10)
            throw new Error("Enter the selected point's height from zero to approximately 32 feet 10 inches.");
        // Slide along screen-up so measured height does not change the
        // perpendicular distance to a pitched/rolled screen.
        const up = vec([0, 1, 0]).applyQuaternion(screen.q);
        if (Math.abs(up.y) < 1e-6)
            throw new Error("This screen orientation cannot use a floor-based reset height.");
        target.addScaledVector(up, (height - target.y) / up.y);
    }
    return target;
}
export function recenteredRegistration(config, currentHead, distance, height, reference = "eye") {
    const target = resetTargetPosition(config, distance, height);
    const current = resetReferencePoint(config, currentHead, reference);
    // A position reset translates the shared room registration. Absorbing the
    // head's current rotation here would make a nod/roll tilt both tracker axes.
    return {
        position: vec(config.registration.position).add(target.sub(current)).toArray(),
        rotation: [...config.registration.rotation],
    };
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
    if (!Number.isFinite(localEye.z) || localEye.z <= 0) return false;
    // Keep the near plane before the glass even at close viewing distances.
    // Only clipping changes: never clamp the eye distance or the field of view.
    const near = Math.min(config.view.near, localEye.z / 2),
        far = config.view.far;
    const scale = near / localEye.z;
    camera.position.copy(eye);
    camera.quaternion.copy(s.q);
    camera.near = near;
    camera.far = far;
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
