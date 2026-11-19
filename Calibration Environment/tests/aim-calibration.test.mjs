// Synthetic fixtures only: exact poses are generated from a known mount and sight
// line. These check the solver's algebra, not tracker accuracy or a physical zero.
import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as T from "../web/vendor/build/three.module.js";
import { AIM_LIMITS, averageNative, dotTargets, solveAim } from "../web/aim-calibration.js";
import { compose, fromNative, inverse, point, pose, rotation, vec } from "../web/math.js";

const defaults = JSON.parse(readFileSync(new URL("../defaults.json", import.meta.url)));
const TRUE_MOUNT = [80, -30, 140];
const SIGHT_HEIGHT = 0.065; // sights above the bore, metres (typical AR-style rifle)

function levelLook(forward, cantDeg = 0) {
    const up = vec([0, 1, 0]).addScaledVector(forward, -forward.y).normalize();
    const back = forward.clone().negate();
    return new T.Quaternion().setFromRotationMatrix(new T.Matrix4().makeBasis(new T.Vector3().crossVectors(up, back), up, back))
        .multiply(rotation([0, 0, cantDeg]));
}

/**
 * The shooter's eye, rear and front sights and the dot are collinear. The gun can
 * be canted or turned about the sight line (stock in the shoulder, angled in).
 */
function fixture({ registration = { position: [0.4, -0.2, 1.1], rotation: [0, 35, 0] },
    noiseDeg = [], cantDeg = () => 0, eyes = true, eyeOffset = [] } = {}) {
    const config = structuredClone(defaults);
    config.registration = registration;
    config.weapon.muzzle = [0, 0.02, -0.45];
    // Tracker 25 cm behind and 8 cm above the muzzle tip, in gun axes.
    config.weapon.mount.position = [0.01, 0.10, -0.20];
    const mount = pose({ position: config.weapon.mount.position, rotation: TRUE_MOUNT });
    const sightOrigin = vec(config.weapon.muzzle).add(vec([0, SIGHT_HEIGHT, 0]));
    const captures = dotTargets(config).map(({ point: dot }, i) => {
        const eye = vec([0.03 * Math.sin(i), 1.5 + 0.01 * Math.cos(i), 2.4]);
        // Cheek position along the stock varies a little between dots.
        const eyeInGun = sightOrigin.clone().add(vec([0, 0, 0.55 + 0.01 * Math.sin(3 * i)]));
        if (eyeOffset[i]) eyeInGun.add(vec(eyeOffset[i]));
        const q = levelLook(dot.clone().sub(eye).normalize(), cantDeg(i));
        if (noiseDeg[i]) q.multiply(rotation([noiseDeg[i], 0, 0]));
        const gun = { p: eye.clone().sub(eyeInGun.clone().applyQuaternion(q)), q };
        const trackerRoom = compose(gun, mount);
        return { native: compose(inverse(pose(config.registration)), trackerRoom), target: dot, ...(eyes ? { eye } : {}) };
    });
    // Start from a wrong mount; the solver must not depend on it.
    config.weapon.mount.rotation = [0, 0, 0];
    return { config, captures };
}

function angleBetween(a, b) {
    return rotation(a).angleTo(rotation(b)) * 180 / Math.PI;
}

/** Shot ray from the solved calibration for one capture, in room metres. */
function shotRay(config, capture, result) {
    const solved = { ...config.weapon.mount, rotation: result.mountRotation };
    const gun = compose(compose(pose(config.registration), capture.native), inverse(pose(solved)));
    const origin = point(gun, config.weapon.muzzle);
    const direction = vec([0, 0, -1]).applyQuaternion(rotation(result.boreRotation)).applyQuaternion(gun.q);
    return { origin, direction };
}

/** Distance from the shot ray, at a given range, to the eye → dot sight line. */
function missAt(ray, capture, range) {
    const p = ray.origin.clone().addScaledVector(ray.direction, range);
    const line = capture.target.clone().sub(capture.eye).normalize();
    const v = p.clone().sub(capture.eye);
    return v.addScaledVector(line, -v.dot(line)).length();
}

test("Sight-line aims recover the mount, sight height and a zero at target distance", () => {
    const { config, captures } = fixture();
    const result = solveAim(config, captures);
    assert.equal(result.mode, "sight line");
    assert.ok(angleBetween(result.mountRotation, TRUE_MOUNT) < 1e-4, `${result.mountRotation} vs ${TRUE_MOUNT}`);
    assert.ok(Math.abs(result.sightHeightM - SIGHT_HEIGHT) < 1e-6, String(result.sightHeightM));
    assert.ok(result.maxDeg < 1e-4);
    assert.deepEqual(result.warnings, []);
    for (const capture of captures) {
        const ray = shotRay(config, capture, result);
        // Crosses the sight line at the zero distance; within the sight height up close.
        assert.ok(missAt(ray, capture, result.zeroDistanceM) < 0.002, String(missAt(ray, capture, result.zeroDistanceM)));
        assert.ok(missAt(ray, capture, 1) < SIGHT_HEIGHT);
    }
    assert.equal(captures.length, AIM_LIMITS.dots.length);
});

test("Canted or angled-in holds still zero the shot onto the sight line", () => {
    // A different cant on every dot (stock in the shoulder, gun leaning in).
    const { config, captures } = fixture({ cantDeg: (i) => [12, -8, 15, 5, -10, 9, 14, -6, 11][i] });
    const result = solveAim(config, captures);
    assert.ok(result.maxDeg < 1e-4);
    for (const capture of captures) {
        const ray = shotRay(config, capture, result);
        assert.ok(missAt(ray, capture, result.zeroDistanceM) < 0.002, String(missAt(ray, capture, result.zeroDistanceM)));
    }
});

test("Aim noise stays small; a rushed dot and a shifted cheek are reported", () => {
    const solve = (options) => { const { config, captures } = fixture(options); return solveAim(config, captures); };
    const small = solve({ noiseDeg: [0.3, -0.2, 0.25] });
    assert.ok(small.maxDeg < 0.6 && angleBetween(small.mountRotation, TRUE_MOUNT) < 0.3);
    const bad = solve({ noiseDeg: [0, 0, 0, 6] });
    assert.equal(bad.worstDot, 3);
    assert.match(bad.warnings.join(" "), /Dot 4/);
    assert.match(solve({ eyeOffset: [, , [0.05, 0, 0]] }).warnings.join(" "), /cheek/);
});

test("Without the head tracker the gun is zeroed at the screen, with a warning", () => {
    const { config, captures } = fixture({ eyes: false });
    const result = solveAim(config, captures);
    assert.equal(result.mode, "muzzle");
    assert.deepEqual(result.boreRotation, [0, 0, 0]);
    assert.match(result.warnings.join(" "), /head tracker/);
});

test("A tilted room registration is flagged", () => {
    const { config, captures } = fixture({ registration: { position: [0, 0, 0], rotation: [8, 20, 0] } });
    assert.match(solveAim(config, captures).warnings.join(" "), /tilted/);
});

test("Too few captures are refused", () => {
    const { config, captures } = fixture();
    assert.throws(() => solveAim(config, captures.slice(0, 2)), /three dots/);
});

test("Averaging identical raw poses returns that pose", () => {
    // A raw SteamVR row-major 3×4 matrix: yaw of about −86° with a slight tilt.
    const matrix = [[-0.059, -0.024, -0.998, 1.03], [-0.035, 0.999, -0.022, -0.99], [0.998, 0.033, -0.06, -1.5]];
    const average = averageNative([matrix, matrix, matrix]);
    assert.ok(average.q.angleTo(fromNative(matrix).q) < 1e-6);
    assert.ok(average.p.distanceTo(fromNative(matrix).p) < 1e-12);
});
