// Guided direction alignment for the independent preview. These editable limits
// reject unclear gestures; they are not physical accuracy or acceptance criteria.
import * as T from "./vendor/build/three.module.js";
import { compose, degrees, inverse, point, screenPose, vec } from "./math.js";

export const DIRECTION_LIMITS = Object.freeze({
    captureDelayMs: 3000,
    captureMs: 1000,
    timeoutMs: 3000,
    minSamples: 12,
    maxPositionSpreadM: 0.025,
    maxRotationSpreadDeg: 3,
    minSideStepM: 0.15,
    maxSideStepM: 2,
    maxOffAxisFraction: 0.5,
    minTurnDeg: 15,
    maxTurnDeg: 100,
    minLookUpDeg: 10,
    maxLookUpDeg: 65,
    maxCrossAxisDeg: 15,
    maxForwardDifferenceDeg: 12,
    maxReturnDistanceM: 0.25,
});

export const DIRECTION_STEPS = [
    { key: "forward", title: "Face the screen", symbol: "◎",
        instruction: "Wear the tracker in its final position. Stand at your centre position, head upright, looking straight toward the screen. Hold still." },
    { key: "lookLeft", title: "Look left", symbol: "↶",
        instruction: "Stay in the same place. Turn your head left about 30–45°, without tilting it. Hold still. The exact angle is not important." },
    { key: "lookRight", title: "Look right", symbol: "↷",
        instruction: "Stay in the same place. Turn your head right about 30–45° from straight ahead, without tilting it. Hold still." },
    { key: "lookUp", title: "Look up", symbol: "↑",
        instruction: "Face forward again, then gently look upward about 20°. Keep your body in place and hold still." },
    { key: "moveLeft", title: "Move left, facing forward", symbol: "←",
        instruction: "Face straight ahead again. Move your whole body about 30 cm to your left, parallel to the screen. Keep your head upright and facing the same forward direction. Hold still." },
    { key: "moveRight", title: "Move right, facing forward", symbol: "→",
        instruction: "Move across to about 30 cm right of your original centre position. Keep facing the same forward direction; do not turn toward the screen centre. Hold still." },
    { key: "centre", title: "Return to centre", symbol: "◎",
        instruction: "Return to your original centre position. Face straight ahead with your head upright and hold still. This is the position that will be centred in the preview." },
];

export function averageStablePoses(samples, limits = DIRECTION_LIMITS) {
    if (samples.length < limits.minSamples)
        throw new Error("Not enough fresh samples. Check tracking and try again.");
    const p = new T.Vector3(), sum = new T.Vector4(0, 0, 0, 0);
    const reference = samples[0].q;
    for (const sample of samples) {
        if (![...sample.p.toArray(), ...sample.q.toArray()].every(Number.isFinite))
            throw new Error("Invalid tracking sample. Try again.");
        p.add(sample.p);
        // q and -q describe the same rotation. Align signs before averaging.
        const sign = reference.dot(sample.q) < 0 ? -1 : 1;
        sum.add(new T.Vector4(...sample.q.toArray()).multiplyScalar(sign));
    }
    p.divideScalar(samples.length);
    const q = new T.Quaternion(...sum.toArray()).normalize();
    const positionSpread = Math.max(...samples.map((s) => s.p.distanceTo(p)));
    const rotationSpread = Math.max(...samples.map((s) => s.q.angleTo(q) * 180 / Math.PI));
    if (positionSpread > limits.maxPositionSpreadM || rotationSpread > limits.maxRotationSpreadDeg)
        throw new Error("The tracker moved during capture. Hold still and capture this step again.");
    return { p, q };
}

export function solveHeadDirections(config, samples, distance, limits = DIRECTION_LIMITS) {
    for (const { key } of DIRECTION_STEPS)
        if (!samples[key]) throw new Error("Capture every step before applying directions.");
    if (!Number.isFinite(distance) || distance < 0.25 || distance > 10)
        throw new Error("Choose a screen distance between 0.25 and 10 metres.");
    const { forward, lookLeft, lookRight, lookUp, moveLeft, moveRight, centre } = samples;
    // SteamVR's standing frame supplies gravity/up. Lateral movement supplies
    // room yaw independently of the arbitrary orientation of the mounted tracker.
    const up = vec([0, 1, 0]);
    const right = moveRight.p.clone().sub(moveLeft.p);
    right.y = 0;
    if (right.length() < 2 * limits.minSideStepM)
        throw new Error("Sideways movement was too small. Repeat the move-left and move-right steps.");
    right.normalize();
    for (const [sample, sign, label] of [[moveLeft, -1, "left"], [moveRight, 1, "right"]]) {
        const displacement = sample.p.clone().sub(forward.p);
        const along = displacement.dot(right);
        const offAxis = displacement.clone().addScaledVector(right, -along).length();
        if (sign * along < limits.minSideStepM || Math.abs(along) > limits.maxSideStepM ||
            offAxis > Math.abs(along) * limits.maxOffAxisFraction)
            throw new Error(`Move ${label} from your original centre, parallel to the screen, and capture again.`);
    }
    const back = right.clone().cross(up).normalize();
    const nativeBasis = new T.Quaternion().setFromRotationMatrix(new T.Matrix4().makeBasis(right, up, back));
    // Express observed turns in the movement-derived screen axes. The neutral
    // tracker rotation cancels the mount, so a sideways/upside-down mount works.
    const relative = (sample) => degrees(nativeBasis.clone().invert()
        .multiply(sample.q).multiply(forward.q.clone().invert()).multiply(nativeBasis));
    const leftAngles = relative(lookLeft), rightAngles = relative(lookRight), upAngles = relative(lookUp);
    for (const [angles, sign, label] of [[leftAngles, 1, "left"], [rightAngles, -1, "right"]]) {
        if (sign * angles[1] < limits.minTurnDeg || sign * angles[1] > limits.maxTurnDeg ||
            Math.abs(angles[0]) > limits.maxCrossAxisDeg || Math.abs(angles[2]) > limits.maxCrossAxisDeg)
            throw new Error(`The look-${label} sample does not match a clear ${label} turn. Keep your head upright and repeat that step.`);
    }
    if (upAngles[0] < limits.minLookUpDeg || upAngles[0] > limits.maxLookUpDeg ||
        Math.abs(upAngles[1]) > limits.maxCrossAxisDeg || Math.abs(upAngles[2]) > limits.maxCrossAxisDeg)
        throw new Error("The look-up sample does not match the movement axes. Face forward, look upward, and repeat that step.");
    for (const [sample, label] of [[moveLeft, "Move left"], [moveRight, "Move right"], [centre, "Return to centre"]])
        if (sample.q.angleTo(forward.q) * 180 / Math.PI > limits.maxForwardDifferenceDeg)
            throw new Error(`${label}: face the same forward direction as the first step, then capture again.`);
    if (centre.p.distanceTo(forward.p) > limits.maxReturnDistanceM)
        throw new Error("Return closer to your original centre position and capture the last step again.");

    const screen = screenPose(config);
    const registrationQ = screen.q.clone().multiply(nativeBasis.clone().invert());
    // q_roomTracker = q_head * q_mount; solve mount rotation from forward pose.
    const mountQ = screen.q.clone().invert().multiply(registrationQ).multiply(centre.q);
    const mount = { p: vec(config.head.mount.position), q: mountQ };
    const rawHead = compose(centre, inverse(mount));
    const targetHead = { p: point(screen, [0, 0, distance]), q: screen.q };
    const registration = compose(targetHead, inverse(rawHead));
    return {
        registration: { position: registration.p.toArray(), rotation: degrees(registration.q) },
        mountRotation: degrees(mountQ),
        summary: {
            leftTurn: leftAngles[1], rightTurn: -rightAngles[1], lookUp: upAngles[0],
            sidewaysSpan: moveLeft.p.distanceTo(moveRight.p), distance,
        },
    };
}
