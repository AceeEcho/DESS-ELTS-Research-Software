// Synthetic browser regression checks for scene geometry and distance persistence.
import assert from "node:assert/strict";
import { installDraftFixture } from "./draft-fixture.mjs";
import { pathToFileURL } from "node:url";
const { chromium } = await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE_PATH).href);
const browser = await chromium.launch({ headless: true, channel: "chromium", args: ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"] });
const page = await browser.newPage();
const drafts = await installDraftFixture(page);
const errors = [];
page.on("pageerror", e => errors.push(e.message));
await page.route("**/api/tracking", route => route.fulfill({ json: { status: "unavailable", devices: [], ageMs: 0 } }));
try {
    await page.goto(process.env.CALIBRATION_BASE_URL || "http://127.0.0.1:8766");
    await page.waitForFunction(() => document.querySelector("#reset-distance-feet"));
    assert.equal(await page.locator("#reset-reference").inputValue(), "eye");
    await page.locator("#reset-distance-feet").fill("8");
    await page.locator("#reset-distance-inches").fill("4.5");
    await page.locator("#reset-distance-inches").press("Tab");
    await page.locator("#reset-height-feet").fill("5");
    await page.locator("#reset-height-inches").fill("7");
    await page.locator("#reset-height-inches").press("Tab");
    await drafts.waitFor(draft => Math.abs(draft.resetHeight - 1.7018) < 0.0001);
    await page.reload();
    assert.equal(await page.locator("#reset-distance-feet").inputValue(), "8");
    assert.equal(await page.locator("#reset-distance-inches").inputValue(), "4.5");
    assert.equal(await page.locator("#reset-height-feet").inputValue(), "5");
    assert.equal(await page.locator("#reset-height-inches").inputValue(), "7");
    assert.equal(await page.locator("#reset-reference").inputValue(), "eye");
    // An old draft has no reference field: preserve its head-centre meaning
    // and its geometry instead of silently reinterpreting an eye measurement.
    const oldConfig = await page.evaluate(draft => {
        const key = "elts-calibration-v1-draft";
        delete draft.resetReference;
        localStorage.setItem(key, JSON.stringify(draft));
        return draft.config;
    }, drafts.read());
    drafts.clear();
    await page.reload();
    await page.waitForFunction(() => document.querySelector("#reset-distance-feet"));
    assert.equal(await page.locator("#reset-reference").inputValue(), "head");
    assert.equal(await page.locator("#reset-distance-inches").inputValue(), "4.5");
    const restored = (await drafts.waitFor(draft => draft.resetReference === "head")).config;
    assert.deepEqual(restored, oldConfig);
    await page.locator("#reset-reference").selectOption("eye");
    await drafts.waitFor(draft => draft.resetReference === "eye");
    await page.reload();
    await page.waitForFunction(() => document.querySelector("#reset-distance-feet"));
    assert.equal(await page.locator("#reset-reference").inputValue(), "eye");
    const results = await page.evaluate(async () => {
        const { CalibrationScene } = await import("/scene.js");
        const { Vector3, Box3 } = await import("/vendor/build/three.module.js");
        const config = (await (await fetch("/api/state")).json()).defaults;
        const canvases = [document.createElement("canvas"), document.createElement("canvas")];
        const scene = new CalibrationScene(...canvases, document.createElement("div"));
        const results = [];
        for (const [width, depth, bottom, angles] of [[6, 7, 0.9, [0,0,0]], [9.2, 11.3, 1.4, [15,35,9]]]) {
            config.room.width = width;
            config.room.depth = depth;
            config.screen.bottom = bottom;
            config.screen.rotation = angles;
            scene.configure(config);
            scene.virtual.updateMatrixWorld(true);
            const positions = scene.virtualFloor.geometry.getAttribute("position");
            let maxFloorHeight = 0;
            for (let i=0; i<positions.count; i++) {
                const point = new Vector3().fromBufferAttribute(positions, i);
                scene.virtualFloor.localToWorld(point);
                maxFloorHeight = Math.max(maxFloorHeight, Math.abs(point.y));
            }
            scene.virtualFloor.geometry.computeBoundingBox();
            const bounds = scene.virtualFloor.geometry.boundingBox;
            const objectBottoms = scene.referenceProps.map(object => new Box3().setFromObject(object).min.y);
            const floor = scene.referenceRoom.floor;
            const floorTop = new Box3().setFromObject(floor).max.y;
            results.push({ floorTop, floorWidth: floor.scale.x, floorDepth: floor.scale.z, width: bounds.max.x - bounds.min.x, depth: -bounds.min.z, front: bounds.max.z, maxFloorHeight, objectBottoms });
        }
        scene.controls.dispose();
        scene.renderer.dispose();
        scene.previewRenderer.dispose();
        return results;
    });
    for (const [i, dimensions] of [[6, 7], [9.2, 11.3]].entries()) {
        const result = results[i];
        assert.ok(Math.abs(result.width - dimensions[0]) < 1e-5);
        assert.ok(Math.abs(result.depth - dimensions[1]) < 1e-5);
        assert.equal(result.front, 0);
        assert.ok(Math.abs(result.floorTop) < 1e-6);
        assert.equal(result.floorWidth, dimensions[0]);
        assert.equal(result.floorDepth, dimensions[1]);
        assert.ok(Math.abs(result.maxFloorHeight - 0.004) < 1e-8);
        result.objectBottoms.forEach(y => assert.ok(Math.abs(y) < 1e-6));
    }
    assert.deepEqual(errors, []);
    console.log("PASS: reset measurements and reference survive reload; legacy drafts retain head-centre meaning and geometry; floor dimensions follow configuration; floor and props stay at world Y=0 with raised/tilted screens.");
} catch (error) {
    console.error({ pageErrors: errors, status: await page.locator("#connection-status").textContent(),
        notice: await page.locator("#notice").textContent() });
    throw error;
} finally { await browser.close(); }
