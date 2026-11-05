import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as T from "../web/vendor/build/three.module.js";
import { pose, point, offAxis, screenPose, compose, inverse, recenteredRegistration } from "../web/math.js";
const defaults = JSON.parse(readFileSync(new URL("../defaults.json", import.meta.url)));
const close = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-8, `${actual} != ${expected}`);

test("Projection agrees with independent eye-to-object intersections at the physical screen", () => {
    // Intersect a world-space ray with the screen plane, without using the
    // projection matrix to calculate the expected screen position.
    for (const angles of [[0, 0, 0], [17, 42, -9]]) {
        const config = structuredClone(defaults);
        config.screen.rotation = angles;
        const screen = screenPose(config);
        const normal = new T.Vector3(0, 0, 1).applyQuaternion(screen.q);
        const right = new T.Vector3(1, 0, 0).applyQuaternion(screen.q);
        const up = new T.Vector3(0, 1, 0).applyQuaternion(screen.q);
        for (const distance of [0.3, 1.91, 5]) {
            for (const eyeX of [-0.6, 0, 0.6]) {
                for (const depth of [0, 0.2, 5, 20]) {
                    const eye = point(screen, [eyeX, 0.2, distance]);
                    const target = point(screen, [0.25, -0.4, -depth]);
                    const ray = target.clone().sub(eye);
                    const t = screen.p.clone().sub(eye).dot(normal) / ray.dot(normal);
                    const intersection = eye.clone().addScaledVector(ray, t).sub(screen.p);
                    const camera = new T.PerspectiveCamera();
                    assert.ok(offAxis(camera, eye, config));
                    const projected = target.clone().project(camera);
                    close(projected.x, 2 * intersection.dot(right) / config.screen.width);
                    close(projected.y, 2 * intersection.dot(up) / config.screen.height);
                }
            }
        }
    }
});

test("Repeated reset preserves translation magnitude through rotated tracker mounts", () => {
    const config = structuredClone(defaults);
    config.registration = { position: [-0.9721, 1.5648, 2.1352], rotation: [0, -88.596, 0] };
    config.head.mount = { position: [0.04, 0.13, 0.03], rotation: [-21.2631, -175.4581, -2.9448] };
    const raw = pose({ position: [1, 0.4, 0.7], rotation: [14, 31, 7] });
    const head = () => compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    for (let i = 0; i < 20; i++)
        config.registration = recenteredRegistration(config, head(), 2);
    const before = head().p;
    raw.p.x += 0.3048;
    close(head().p.distanceTo(before), 0.3048);
});

test("Measured reset head height is independent of display centre height", () => {
    const config = structuredClone(defaults);
    const raw = pose({ position: [0.2, 1.7, 1], rotation: [0, 20, 0] });
    const current = compose(raw, inverse(pose(config.head.mount)));
    config.registration = recenteredRegistration(config, current, 2, 1.72);
    const recovered = compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    close(recovered.p.y, 1.72);
    close(recovered.p.z, 2);
    config.screen.rotation = [15, 30, 8];
    config.registration = recenteredRegistration(config, recovered, 2, 1.72);
    const tilted = compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    close(tilted.p.y, 1.72);
    const screen = screenPose(config);
    const local = tilted.p.clone().sub(screen.p).applyQuaternion(screen.q.clone().invert());
    close(local.x, 0);
    close(local.z, 2);
    assert.throws(() => recenteredRegistration(config, recovered, 2, NaN));
});
