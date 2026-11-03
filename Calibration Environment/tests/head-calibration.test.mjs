import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { averageStablePoses, solveHeadDirections, DIRECTION_LIMITS } from "../web/head-calibration.js";
import { compose, inverse, point, pose, rotation, screenPose } from "../web/math.js";

const defaults = JSON.parse(readFileSync(new URL("../defaults.json", import.meta.url)));
function fixture() {
    const config = structuredClone(defaults);
    config.screen.rotation = [5, -20, 3];
    config.head.mount.position = [0.06, 0.13, -0.04];
    // Deliberately wrong previous settings must not bias the new solution.
    config.registration = { position: [8, 9, 10], rotation: [30, 70, -10] };
    config.head.mount.rotation = [70, 20, 30];
    const nativeRoom = pose({ position: [2, -1, 3], rotation: [0, 65, 0] });
    const mount = pose({ position: config.head.mount.position, rotation: [90, -25, 160] });
    const headPose = (x, angles) => compose(nativeRoom, compose(pose({ position: [x, 1.6, 2], rotation: angles }), mount));
    const samples = {
        forward: headPose(0, [0, 0, 0]), lookLeft: headPose(0, [0, 35, 0]),
        lookRight: headPose(0, [0, -40, 0]), lookUp: headPose(0, [20, 0, 0]),
        moveLeft: headPose(-0.3, [0, 0, 0]), moveRight: headPose(0.3, [0, 0, 0]),
        centre: headPose(0, [0, 0, 0]),
    };
    return { config, samples, mount, headPose };
}
function near(a, b, tolerance = 1e-7) { assert.ok(Math.abs(a - b) < tolerance, `${a} differs from ${b}`); }

test("Direction guide solves a rotated mount and room; subsequent motion follows screen axes", () => {
    const { config, samples, mount, headPose } = fixture();
    const before = JSON.stringify(config);
    const result = solveHeadDirections(config, samples, 2.4);
    assert.equal(JSON.stringify(config), before, "Solve must not mutate the profile");
    near(rotation(result.mountRotation).angleTo(mount.q), 0);
    const registration = pose(result.registration), screen = screenPose(config);
    const recovered = compose(compose(registration, samples.centre), inverse(mount));
    near(recovered.p.distanceTo(point(screen, [0, 0, 2.4])), 0);
    near(recovered.q.angleTo(screen.q), 0);
    const moved = compose(compose(registration, headPose(0.4, [0, 30, 0])), inverse(mount));
    near(moved.p.distanceTo(point(screen, [0.4, 0, 2.4])), 0);
    near(moved.q.angleTo(screen.q.clone().multiply(rotation([0, 30, 0]))), 0);
});

test("Reject swapped gestures, small movements, wrong pitch and inconsistent neutral poses", () => {
    for (const [name, change] of [
        ["swapped turns", s => { [s.lookLeft, s.lookRight] = [s.lookRight, s.lookLeft]; }],
        ["swapped movement", s => { [s.moveLeft, s.moveRight] = [s.moveRight, s.moveLeft]; }],
        ["no movement", s => { s.moveLeft = s.forward; s.moveRight = s.forward; }],
        ["wrong look up", s => { s.lookUp = s.lookLeft; }],
        ["looking sideways during movement", s => { s.moveLeft.q = s.lookLeft.q; }],
        ["did not return", s => { s.centre = s.moveRight; }],
    ]) {
        const { config, samples } = fixture();
        change(samples);
        assert.throws(() => solveHeadDirections(config, samples, 2), undefined, name);
    }
});

test("Stable capture averages quaternion signs and rejects jitter and too few samples", () => {
    const reference = pose({ position: [0.2, 1.7, 2], rotation: [20, 50, 160] });
    const samples = Array.from({ length: DIRECTION_LIMITS.minSamples }, (_, i) => {
        const p = reference.p.clone(); p.x += (i % 2 ? 1 : -1) * 0.001;
        const q = reference.q.clone();
        if (i % 2) q.set(-q.x, -q.y, -q.z, -q.w);
        return { p, q };
    });
    const average = averageStablePoses(samples);
    near(average.p.distanceTo(reference.p), 0);
    near(average.q.angleTo(reference.q), 0);
    assert.throws(() => averageStablePoses(samples.slice(0, 2)));
    samples[0].p.x += 0.2;
    assert.throws(() => averageStablePoses(samples), /moved during capture/);
});
