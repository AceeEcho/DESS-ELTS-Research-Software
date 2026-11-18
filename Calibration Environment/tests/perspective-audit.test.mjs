import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as T from "../web/vendor/build/three.module.js";
import { pose, point, offAxis, screenPose, screenDistance, compose, inverse, recenteredRegistration, registrationTiltDegrees, trackerToEye } from "../web/math.js";
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
        for (const distance of [0.001, 0.01, 0.3, 1.91, 5]) {
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

test("Eye and legacy head-centre reset measurements use their stated reference", () => {
    for (const reference of ["eye", "head"]) {
    const config = structuredClone(defaults);
    const raw = pose({ position: [0.2, 1.7, 1], rotation: [0, 20, 0] });
    const current = compose(raw, inverse(pose(config.head.mount)));
    config.registration = recenteredRegistration(config, current, 2, 1.72, reference);
    const recovered = compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    const anchor = reference === "eye" ? point(recovered, config.head.eye) : recovered.p;
    close(anchor.y, 1.72);
    close(anchor.z, 2);
    config.screen.rotation = [15, 30, 8];
    config.registration = recenteredRegistration(config, recovered, 2, 1.72, reference);
    const tilted = compose(compose(pose(config.registration), raw), inverse(pose(config.head.mount)));
    const tiltedAnchor = reference === "eye" ? point(tilted, config.head.eye) : tilted.p;
    close(tiltedAnchor.y, 1.72);
    const screen = screenPose(config);
    const local = tiltedAnchor.clone().sub(screen.p).applyQuaternion(screen.q.clone().invert());
    close(local.x, 0);
    close(local.z, 2);
    assert.throws(() => recenteredRegistration(config, recovered, 2, NaN));
    }
});

test("Reset while nodding or rolling cannot mix a subsequent crouch into screen distance", () => {
    for (const angles of [[15, 0, 0], [-20, 30, 25], [0, -45, -30]]) {
        const config = structuredClone(defaults);
        config.registration = { position: [1, 0.2, -0.7], rotation: [0, 35, 0] };
        const before = pose(config.registration);
        const head = pose({ position: [0, 1.7, 2], rotation: angles });
        const rawHead = compose(inverse(before), head);
        const original = JSON.stringify(config);
        const registration = recenteredRegistration(config, head, 1.5, 1.65);
        assert.equal(JSON.stringify(config), original, "Reset calculation does not mutate saved geometry");
        config.registration = registration;
        close(registrationTiltDegrees(config), 0);
        const after = compose(pose(registration), rawHead);
        const crouched = compose(pose(registration), {
            p: rawHead.p.clone().add(new T.Vector3(0, -0.3, 0)), q: rawHead.q,
        });
        close(crouched.p.y - after.p.y, -0.3);
        close(screenDistance(config, point(crouched, config.head.eye)), 1.5);
        close(Math.abs(after.q.dot(head.q)), 1);
    }
});

test("Direct tracker-to-eye measurement agrees with the full rotated mount transform", () => {
    const config = structuredClone(defaults);
    config.head.mount = { position: [0.04, 0.12, 0.08], rotation: [75, 20, 160] };
    const tracker = pose({ position: [0.2, 1.7, 2], rotation: [20, -30, 5] });
    const head = compose(tracker, inverse(pose(config.head.mount)));
    const directEye = trackerToEye(config).applyQuaternion(head.q).add(tracker.p);
    close(directEye.distanceTo(point(head, config.head.eye)), 0);
});

test("Close approach keeps finite angular size while the near plane follows the eye", () => {
    // A 1 m wide object 5 m behind the glass: the pixels shrink, but the angle
    // at the moving physical eye grows toward 2*atan(0.5/5), never zero/infinity.
    const config = structuredClone(defaults), screen = screenPose(config);
    let previousAngle = 0;
    for (const distance of [2, 0.5, 0.02, 0.001, 0.000001]) {
        const eye = point(screen, [0, 0, distance]);
        const camera = new T.PerspectiveCamera();
        assert.ok(offAxis(camera, eye, config));
        assert.ok(camera.near > 0 && camera.near < distance);
        const left = point(screen, [-0.5, 0, -5]).project(camera);
        const right = point(screen, [0.5, 0, -5]).project(camera);
        const physicalSpan = (right.x - left.x) * config.screen.width / 2;
        const angle = 2 * Math.atan(physicalSpan / (2 * distance));
        close(angle, 2 * Math.atan(0.5 / (distance + 5)));
        assert.ok(angle > previousAngle && angle < 2 * Math.atan(0.5 / 5));
        previousAngle = angle;
    }
    assert.equal(offAxis(new T.PerspectiveCamera(), point(screen, [0, 0, 0]), config), false);
    assert.equal(offAxis(new T.PerspectiveCamera(), point(screen, [0, 0, -0.01]), config), false);
});
