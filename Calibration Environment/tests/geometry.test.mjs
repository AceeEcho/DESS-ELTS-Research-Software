import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as T from "../web/vendor/build/three.module.js";
import {
    pose,
    compose,
    inverse,
    point,
    rotation,
    screenPose,
    resolveObject,
    relativeAngles,
    hitScreen,
    offAxis,
    fromNative,
    recenteredRegistration,
} from "../web/math.js";
const defaults = JSON.parse(
    readFileSync(new URL("../defaults.json", import.meta.url)),
);
function close(a, b, epsilon = 1e-8) {
    assert.ok(Math.abs(a - b) < epsilon, `${a} != ${b}`);
}
function closeVector(a, b) {
    a.toArray().forEach((v, i) => close(v, b.toArray()[i]));
}

test("Head reset handles tilted screens, mount offsets and an existing registration", () => {
    const config = structuredClone(defaults);
    config.screen.rotation = [12, 35, -8];
    config.registration = { position: [1, -2, 0.4], rotation: [7, 50, -3] };
    config.head.mount.rotation = [20, -30, 8];
    const raw = pose({ position: [0.3, 1.8, -1], rotation: [15, 80, 2] });
    const current = compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    config.registration = recenteredRegistration(config, current, 2);
    const reset = compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    const screen = screenPose(config);
    closeVector(reset.p, point(screen, [0, 0, 2]));
    close(Math.abs(reset.q.dot(screen.q)), 1);
    const repeated = recenteredRegistration(config, reset, 2);
    closeVector(pose(repeated).p, pose(config.registration).p);
    assert.throws(() => recenteredRegistration(config, reset, NaN));
});

test("Tracker-on-object inversion recovers a translated and rotated object", () => {
    const object = pose({ position: [1, 2, 3], rotation: [20, 70, -12] });
    const mount = pose({
        position: [0.12, 0.17, -0.08],
        rotation: [31, -91, 15],
    });
    const tracker = compose(object, mount),
        recovered = compose(tracker, inverse(mount));
    closeVector(recovered.p, object.p);
    close(Math.abs(recovered.q.dot(object.q)), 1);
});
test("Forward, yaw and eye offsets obey the documented right-handed room basis", () => {
    const object = pose({ position: [0, 1.7, 2], rotation: [0, 90, 0] });
    closeVector(point(object, [0, 0, -0.1]), new T.Vector3(-0.1, 1.7, 2));
    closeVector(
        new T.Vector3(0, 0, -1).applyQuaternion(object.q),
        new T.Vector3(-1, 0, 0),
    );
});
test("Native matrix conversion preserves rotation and translation without a second reflection", () => {
    const result = fromNative([
        [0, 0, 1, 1],
        [0, 1, 0, 2],
        [-1, 0, 0, 3],
    ]);
    closeVector(result.p, new T.Vector3(1, 2, 3));
    closeVector(
        new T.Vector3(0, 0, -1).applyQuaternion(result.q),
        new T.Vector3(-1, 0, 0),
    );
});
test("Live input does not substitute manual values for invalid, stale or unassigned devices", () => {
    const config = structuredClone(defaults);
    config.head.serial = "head-1";
    const frame = {
        status: "connected",
        ageMs: 0,
        devices: [
            {
                serial: "head-1",
                kind: "tracker",
                valid: true,
                matrix: [
                    [1, 0, 0, 0],
                    [0, 1, 0, 1.78],
                    [0, 0, 1, 2.53],
                ],
            },
        ],
    };
    assert.equal(resolveObject(config, "head", "live", frame).valid, true);
    assert.equal(
        resolveObject(config, "head", "live", { ...frame, ageMs: 501 }).valid,
        false,
    );
    assert.equal(
        resolveObject(config, "head", "live", {
            ...frame,
            status: "unavailable",
        }).valid,
        false,
    );
    frame.devices[0].valid = false;
    assert.equal(resolveObject(config, "head", "live", frame).valid, false);
    config.head.serial = "";
    assert.equal(resolveObject(config, "head", "live", frame).valid, false);
});
test("Standing origin registration and mount offset compose in the correct order", () => {
    const config = structuredClone(defaults);
    config.head.serial = "head-1";
    config.registration = { position: [2, 0.5, 3], rotation: [0, 90, 0] };
    const frame = {
        status: "connected",
        ageMs: 0,
        devices: [
            {
                serial: "head-1",
                kind: "tracker",
                valid: true,
                matrix: [
                    [1, 0, 0, 0],
                    [0, 1, 0, 1.78],
                    [0, 0, 1, 2.53],
                ],
            },
        ],
    };
    const r = resolveObject(config, "head", "live", frame);
    closeVector(r.object.p, new T.Vector3(4.5, 2.15, 3));
});
test("Forward references are bound to both the selected serial and input source", () => {
    const object = pose({ position: [0, 0, 0], rotation: [11, 28, 8] });
    const ref = {
        quaternion: object.q.toArray(),
        source: "live",
        serial: "head-a",
    };
    relativeAngles(object, ref, "live", "head-a").forEach((v) => close(v, 0));
    assert.equal(relativeAngles(object, ref, "manual", "head-a"), null);
    assert.equal(relativeAngles(object, ref, "live", "head-b"), null);
});
test("Off-axis projection maps all physical corners to viewport corners for an offset eye and tilted screen", () => {
    const config = structuredClone(defaults);
    config.screen.rotation = [8, 32, -5];
    config.screen.x = 0.7;
    const sp = screenPose(config),
        eye = point(sp, [0.4, 0.15, 2.3]),
        camera = new T.PerspectiveCamera();
    assert.equal(offAxis(camera, eye, config), true);
    for (const x of [-1, 1])
        for (const y of [-1, 1]) {
            const projected = point(sp, [
                (x * config.screen.width) / 2,
                (y * config.screen.height) / 2,
                0,
            ]).project(camera);
            close(projected.x, x);
            close(projected.y, y);
        }
    assert.equal(offAxis(camera, point(sp, [0, 0, -1]), config), false);
});
test("Bore ray intersection distinguishes on-screen, off-screen, parallel and backwards rays", () => {
    const config = structuredClone(defaults),
        sp = screenPose(config);
    const forward = new T.Vector3(0, 0, -1);
    const hit = hitScreen(point(sp, [0, 0, 2]), forward, config);
    assert.equal(hit.inside, true);
    close(hit.distance, 2);
    close(hit.x, 0);
    close(hit.y, 0);
    assert.equal(
        hitScreen(point(sp, [2, 0, 2]), forward, config).inside,
        false,
    );
    assert.equal(
        hitScreen(point(sp, [0, 0, 2]), new T.Vector3(1, 0, 0), config),
        null,
    );
    assert.equal(
        hitScreen(point(sp, [0, 0, 2]), new T.Vector3(0, 0, 1), config),
        null,
    );
});
