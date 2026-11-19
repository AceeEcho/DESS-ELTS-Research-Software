// Aim-at-dots zeroing for the gun. The participant aims the gun at dots drawn at
// known places on the physical screen; each capture records the raw SteamVR
// tracker pose. The solver finds the gun's forward (shot) direction and its
// up direction in tracker axes, then returns the mounting rotation that makes
// the gun drawing and shot line match. Like zeroing a sight, the shot line is
// set to pass from the muzzle through each dot at the screen distance.
// The limits below are editable software checks, not physical acceptance values.
import * as T from "./vendor/build/three.module.js";
import { compose, degrees, fromNative, point, pose, screenPose, vec } from "./math.js";

export const AIM_LIMITS = Object.freeze({
    /** Dot positions as fractions of the visible screen (0 = left/bottom). */
    dots: Object.freeze([
        [0.5, 0.5], [0.2, 0.8], [0.8, 0.8], [0.8, 0.2], [0.2, 0.2],
        [0.5, 0.8], [0.8, 0.5], [0.5, 0.2], [0.2, 0.5],
    ]),
    /** Hold this still for this long to capture without the trigger. */
    steadyMs: 1500,
    /** Largest gun rotation (degrees) during a steady hold. */
    steadyDeg: 0.6,
    /** The gun must turn this far (degrees) after a capture before the next steady capture can start. */
    rearmDeg: 2,
    /** A trigger capture uses samples from this window before the press, so the pull does not move the aim. */
    triggerWindowMs: [60, 300],
    minSamplesPerCapture: 3,
    /** Above this, one dot probably was not aimed at; redo is recommended. */
    maxResidualDeg: 2,
    /** Warn when the tracking-origin registration tilts room "up" away from SteamVR's gravity. */
    maxRegistrationTiltDeg: 2,
    /** Where ELTS draws targets behind the screen (m); keep aligned with
     *  targetDistanceM in config/development/scenario.json. The shot is zeroed here. */
    zeroDepthBehindScreenM: 7.5,
    /** Warn when the eye's position across the sight line varies more than this (m). */
    maxEyeSpreadM: 0.03,
    iterations: 12,
});

/** Dot centres in room metres for each layout entry. */
export function dotTargets(config, dots = AIM_LIMITS.dots) {
    const screen = screenPose(config);
    return dots.map(([u, v]) => ({
        u, v,
        point: point(screen, [(u - 0.5) * config.screen.width, (v - 0.5) * config.screen.height, 0]),
    }));
}

/** Mean of nearly equal native poses; q and −q are aligned before averaging. */
export function averageNative(matrices) {
    const poses = matrices.map(fromNative);
    // Vector4() defaults to w = 1; the sum must start at zero.
    const p = new T.Vector3(), sum = new T.Vector4(0, 0, 0, 0);
    for (const sample of poses) {
        p.add(sample.p);
        const sign = poses[0].q.dot(sample.q) < 0 ? -1 : 1;
        sum.add(new T.Vector4(...sample.q.toArray()).multiplyScalar(sign));
    }
    return { p: p.divideScalar(poses.length), q: new T.Quaternion(...sum.toArray()).normalize() };
}

/** Largest angle (degrees) between any sample and the first one. */
export function rotationSpreadDeg(matrices) {
    const poses = matrices.map(fromNative);
    return Math.max(0, ...poses.map((s) => s.q.angleTo(poses[0].q) * 180 / Math.PI));
}

/** Gun axes in tracker coordinates from a forward direction and SteamVR's gravity up. */
function gunAxes(forward, captures) {
    // SteamVR's standing frame is gravity aligned: its +Y is true up. Averaging
    // over every capture keeps a slight cant on some dots from tilting the result.
    const upSum = captures.reduce((sum, c) =>
        sum.add(vec([0, 1, 0]).applyQuaternion(c.native.q.clone().invert())), new T.Vector3());
    const up = upSum.addScaledVector(forward, -upSum.dot(forward));
    if (up.length() < 0.2) throw new Error("The gun was pointed too steeply to find its up direction.");
    up.normalize();
    // Gun axes: −Z forward, +Y up, +X right.
    const back = forward.clone().negate(), right = new T.Vector3().crossVectors(up, back);
    return { up, gunToTracker: new T.Quaternion().setFromRotationMatrix(new T.Matrix4().makeBasis(right, up, back)) };
}

/** Bore correction (pitch, yaw, roll degrees) that turns gun −Z onto a gun-frame direction. */
export function boreFor(direction) {
    const d = direction.clone().normalize();
    // rotation([p, y, 0]) maps (0, 0, −1) to (−sin y cos p, sin p, −cos y cos p).
    return [Math.asin(Math.max(-1, Math.min(1, d.y))) * 180 / Math.PI, Math.atan2(-d.x, -d.z) * 180 / Math.PI, 0];
}

/**
 * captures: [{ native: {p, q}, target: Vector3, eye?: Vector3 }]. native is the raw
 * SteamVR gun-tracker pose, target the dot and eye the head-tracked viewing eye,
 * both in room metres.
 *
 * With the eye on every capture (head tracker worn), the participant's sight line
 * is the line eye → dot. It is found in the gun's own axes, so it does not matter
 * how the gun is held (stock in the shoulder, angled in, slightly canted). The gun's
 * forward axis is that sight line; the barrel aim correction then points the shot
 * from the muzzle to cross the sight line at the target distance, like zeroing a
 * rifle at range. Without the eye, the shot line is aimed from the muzzle through
 * each dot (zeroed at the screen) and a warning explains the far-target error.
 * The measured tracker-to-muzzle offset (mount.position − muzzle, gun axes) is kept.
 */
export function solveAim(config, captures, limits = AIM_LIMITS) {
    if (captures.length < 3) throw new Error("Aim at least three dots before solving.");
    const registration = pose(config.registration);
    const trackerToMuzzle = vec(config.weapon.muzzle).sub(vec(config.weapon.mount.position));
    const rooms = captures.map((c) => compose(registration, c.native));
    const toTracker = (room, v) => v.clone().applyQuaternion(room.q.clone().invert());
    const withEyes = captures.every((c) => c.eye);
    const warnings = [];
    let forward, axes, wanted, extra = {};
    if (withEyes) {
        // Direction of each eye → dot line, in tracker axes.
        wanted = rooms.map((room, i) => {
            const toDot = captures[i].target.clone().sub(captures[i].eye);
            if (toDot.length() < 0.2) throw new Error("The eye is at a dot; stand back from the screen.");
            return toTracker(room, toDot.normalize());
        });
        forward = wanted.reduce((sum, w) => sum.add(w), new T.Vector3()).normalize();
        axes = gunAxes(forward, captures);
        // Every eye lies on the sight line. Its offset from the tracker across the
        // line is the mean of the eyes' components perpendicular to it.
        const eyes = rooms.map((room, i) => toTracker(room, captures[i].eye.clone().sub(room.p)));
        const across = eyes.map((e) => e.clone().addScaledVector(forward, -e.dot(forward)));
        const linePoint = across.reduce((sum, e) => sum.add(e), new T.Vector3()).divideScalar(across.length);
        const eyeSpread = Math.max(...across.map((e) => e.distanceTo(linePoint)));
        const muzzle = trackerToMuzzle.clone().applyQuaternion(axes.gunToTracker);
        // Zero point: on the sight line, the target distance in front of the muzzle.
        const muzzleOnLine = linePoint.clone().addScaledVector(forward, muzzle.dot(forward));
        const toScreen = rooms.map((room, i) =>
            captures[i].target.distanceTo(muzzle.clone().applyQuaternion(room.q).add(room.p)));
        const zeroDistance = toScreen.reduce((a, b) => a + b, 0) / toScreen.length + limits.zeroDepthBehindScreenM;
        const zeroPoint = muzzleOnLine.clone().addScaledVector(forward, zeroDistance);
        const shot = zeroPoint.sub(muzzle).normalize().applyQuaternion(axes.gunToTracker.clone().invert());
        extra = {
            mode: "sight line",
            boreRotation: boreFor(shot),
            sightHeightM: muzzle.distanceTo(muzzleOnLine),
            eyeSpreadM: eyeSpread,
            zeroDistanceM: zeroDistance,
        };
        if (eyeSpread > limits.maxEyeSpreadM)
            warnings.push(`The eye moved ${(eyeSpread * 100).toFixed(1)} cm off the sight line between dots. Keep the same cheek position on the stock, or check the eye offset under Head.`);
    } else {
        // No head tracker: aim from the muzzle through each dot. The muzzle point
        // depends on the solved axes, so iterate from the current mount.
        let gunToTracker = pose(config.weapon.mount).q.clone().invert();
        for (let k = 0; k < limits.iterations; k++) {
            const muzzle = trackerToMuzzle.clone().applyQuaternion(gunToTracker);
            wanted = rooms.map((room, i) => {
                const toDot = captures[i].target.clone().sub(muzzle.clone().applyQuaternion(room.q).add(room.p));
                if (toDot.length() < 0.05) throw new Error("The muzzle is at a dot; stand back from the screen.");
                return toTracker(room, toDot.normalize());
            });
            forward = wanted.reduce((sum, w) => sum.add(w), new T.Vector3()).normalize();
            axes = gunAxes(forward, captures);
            gunToTracker = axes.gunToTracker;
        }
        extra = { mode: "muzzle", boreRotation: [0, 0, 0] };
        warnings.push("The head tracker was not tracked on every dot, so the gun is zeroed at the screen. Targets behind the screen will land high by about the sight height × (target distance ÷ screen distance − 1). Wear the head tracker and redo this for an accurate zero.");
    }
    const residuals = wanted.map((w) => w.angleTo(forward) * 180 / Math.PI);
    const worst = Math.max(...residuals), worstDot = residuals.indexOf(worst);
    if (worst > limits.maxResidualDeg)
        warnings.unshift(`Dot ${worstDot + 1} is ${worst.toFixed(1)}° from the others; redo the aim if that dot was rushed.`);
    const tilt = vec([0, 1, 0]).applyQuaternion(registration.q).angleTo(vec([0, 1, 0])) * 180 / Math.PI;
    if (tilt > limits.maxRegistrationTiltDeg)
        warnings.push(`Tracking origin → room is tilted ${tilt.toFixed(1)}° from SteamVR's up. Run Calibrate head directions first, then redo this aim.`);
    return {
        ...extra,
        // mount is tracker-on-gun, the inverse of gun-in-tracker axes.
        mountRotation: degrees(axes.gunToTracker.clone().invert()),
        forwardInTracker: forward.toArray(),
        upInTracker: axes.up.toArray(),
        residualsDeg: residuals,
        meanDeg: residuals.reduce((a, b) => a + b, 0) / residuals.length,
        maxDeg: worst,
        worstDot,
        registrationTiltDeg: tilt,
        warnings,
    };
}
