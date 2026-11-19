// Read-only numerical investigation. All poses below are synthetic; this does
// not measure the TV, tracker accuracy, a person's eyes, or display latency.
// Run from the repository root: node tools/diagnostics/audit_window_projection.mjs
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as T from "../../Calibration Environment/web/vendor/build/three.module.js";
import {
    compose, inverse, offAxis, point, pose, recenteredRegistration, rotation, screenPose,
} from "../../Calibration Environment/web/math.js";

const defaults = JSON.parse(readFileSync(new URL("../../Calibration Environment/defaults.json", import.meta.url)));
let checks = 0;
function close(actual, expected, label, tolerance = 1e-9) {
    assert.ok(Math.abs(actual - expected) < tolerance, `${label}: ${actual} != ${expected}`);
    checks++;
}
function projectedWidth(config, renderDistanceM, depthM = 5, widthM = 1) {
    const screen = screenPose(config);
    const eye = point(screen, [0, 0, renderDistanceM]);
    const camera = new T.PerspectiveCamera();
    assert.ok(offAxis(camera, eye, config), "Synthetic eye must be outside the near clip");
    const left = point(screen, [-widthM / 2, 0, -depthM]).project(camera);
    const right = point(screen, [widthM / 2, 0, -depthM]).project(camera);
    return (right.x - left.x) * config.screen.width / 2;
}
function visualAngle(spanM, physicalDistanceM) {
    return 2 * Math.atan(spanM / (2 * physicalDistanceM)) * 180 / Math.PI;
}
const rounded = (value) => Number(value.toFixed(6));

// Compare the actual projection against independent 3D ray/plane geometry.
// Include close eyes, off-centre eyes, tilted screens and points behind the TV:
// mapping just the screen corners cannot catch a wrong physical eye position.
for (const angles of [[0, 0, 0], [17, 42, -9]]) {
    const config = structuredClone(defaults);
    config.screen.rotation = angles;
    const screen = screenPose(config);
    const screenNormal = new T.Vector3(0, 0, 1).applyQuaternion(screen.q);
    for (const distance of [5, 2, 0.5, 0.1, 0.021]) {
        for (const eyeX of [-0.6, 0, 0.6]) {
            const eye = point(screen, [eyeX, 0.2, distance]);
            const camera = new T.PerspectiveCamera();
            assert.ok(offAxis(camera, eye, config));
            for (const depth of [0, 0.2, 5, 20]) {
                const target = point(screen, [0.25, -0.4, -depth]);
                const ray = target.clone().sub(eye);
                const fraction = screen.p.clone().sub(eye).dot(screenNormal) / ray.dot(screenNormal);
                const expected = eye.clone().addScaledVector(ray, fraction);
                const ndc = target.clone().project(camera);
                const displayed = point(screen, [ndc.x * config.screen.width / 2, ndc.y * config.screen.height / 2, 0]);
                close(displayed.distanceTo(expected), 0, "Projection equals eye-ray intersection");
                close(displayed.clone().sub(eye).normalize().dot(ray.normalize()), 1,
                    "Physical viewing ray points at the stationary virtual object");
            }
        }
    }
}

const correct = [];
let previousSpan = Infinity, previousAngle = 0;
for (const distance of [2, 1, 0.5, 0.25, 0.1, 0.021, 0.001, 0.000001]) {
    const span = projectedWidth(defaults, distance);
    const angle = visualAngle(span, distance);
    close(span, distance / (distance + 5), "Independent similar-triangles size");
    close(angle, visualAngle(1, distance + 5), "TV and real object subtend the same angle");
    assert.ok(span < previousSpan && angle > previousAngle,
        "Approaching must shrink the screen footprint while increasing the visual angle");
    previousSpan = span;
    previousAngle = angle;
    correct.push({ eyeDistanceM: distance, screenWidthCm: rounded(span * 100), visualAngleDeg: rounded(angle) });
}

// Near/far clipping changes visibility, not the projected scale of visible points.
for (const distance of [0.1, 0.5, 2]) {
    for (const near of [0.001, 0.01, 0.02]) {
        for (const far of [30, 100]) {
            const config = structuredClone(defaults);
            config.view.near = near;
            config.view.far = far;
            close(projectedWidth(config, distance), projectedWidth(defaults, distance), "Clip planes do not set magnification");
        }
    }
}
const screen = screenPose(defaults);
for (const distance of [0, -0.1]) {
    assert.equal(offAxis(new T.PerspectiveCamera(), point(screen, [0, 0, distance]), defaults), false);
}

// Example only: the renderer puts the eye 20 cm closer than the real eye.
// The equivalent depth uses familiar object size; it is NOT a measurement of
// subjective perceived depth. A zero/negative eye distance remains undefined.
const distanceBiasM = 0.2;
const mismatch = [2, 1, 0.5, 0.3, 0.25, 0.23].map((physicalDistanceM) => {
    const renderDistanceM = physicalDistanceM - distanceBiasM;
    const span = projectedWidth(defaults, renderDistanceM);
    const equivalentDepthM = physicalDistanceM / span - physicalDistanceM;
    close(equivalentDepthM, 5 * physicalDistanceM / renderDistanceM, "Distance bias stretches equivalent depth");
    return { physicalDistanceM, renderDistanceM: rounded(renderDistanceM),
        screenWidthCm: rounded(span * 100), visualAngleDeg: rounded(visualAngle(span, physicalDistanceM)),
        equivalentDepthBehindScreenM: rounded(equivalentDepthM) };
});
assert.ok(mismatch.at(-1).visualAngleDeg < mismatch[0].visualAngleDeg);

// Regression for the reset defect: a correctly registered room and a temporary
// 15-degree head pitch. A position reset must preserve the room's orientation.
const config = structuredClone(defaults);
config.registration = { position: [1, 0.2, -0.7], rotation: [0, 35, 0] };
const beforeRegistration = pose(config.registration);
const currentHead = pose({ position: [0, 1.7, 2], rotation: [15, 0, 0] });
const rawHead = compose(inverse(beforeRegistration), currentHead);
const resetRegistration = pose(recenteredRegistration(config, currentHead, 2, 1.7));
const upAfterReset = new T.Vector3(0, 1, 0).applyQuaternion(resetRegistration.q);
const resetTiltDeg = upAfterReset.angleTo(new T.Vector3(0, 1, 0)) * 180 / Math.PI;
const resetHead = compose(resetRegistration, rawHead);
const movedRawHead = { p: rawHead.p.clone().add(new T.Vector3(0, -0.3, 0)), q: rawHead.q };
const movedHead = compose(resetRegistration, movedRawHead);
const mappedCrouch = movedHead.p.clone().sub(resetHead.p);
close(resetTiltDeg, 0, "Position reset preserves the level room registration");
close(mappedCrouch.z, 0, "Crouch does not acquire a depth component");

// Snapshot values observed in the active calibration UI, 2026-11-16.
// This computes their mathematical consequence, not physical tracker accuracy.
const observedRegistrationAngles = [1.3166, 81.7645, -10.2219];
const observedQ = rotation(observedRegistrationAngles);
const observedVerticalMove = new T.Vector3(0, 0.3, 0).applyQuaternion(observedQ);

console.log(`PASS: ${checks} numerical comparisons plus visibility/monotonicity assertions.`);
console.log("Synthetic correct window: 1 m object, 5 m behind screen");
console.table(correct);
console.log("Synthetic distance error: rendered eye 0.20 m too close (NOT a measured error)");
console.table(mismatch);
console.log(JSON.stringify({
    syntheticReset: { headPitchDeg: 15, resultingRoomTiltDeg: rounded(resetTiltDeg),
        crouchDisplacementM: mappedCrouch.toArray().map(rounded) },
    observedSettingsConsequence: { registrationAnglesDeg: observedRegistrationAngles,
        nativeUp30CmInRoomM: observedVerticalMove.toArray().map(rounded) },
}, null, 2));
