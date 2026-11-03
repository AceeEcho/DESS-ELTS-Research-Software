// Desktop-only synthetic interaction checks; no physical accuracy claims.
import { pathToFileURL, fileURLToPath } from "node:url";
import { mkdir } from "node:fs/promises";
import assert from "node:assert/strict";
import * as T from "../web/vendor/build/three.module.js";
import { compose, pose } from "../web/math.js";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH
    ? pathToFileURL(process.env.PLAYWRIGHT_MODULE_PATH).href : "playwright");
const output = new URL("../test-results/", import.meta.url);
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true,
    args: ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"] });
const page = await browser.newPage({ viewport: { width: 1500, height: 1000 } });
const errors = [];
page.on("pageerror", (error) => errors.push(error.message));
const nativeRoom = pose({ position: [1, -0.5, 0.8], rotation: [0, 67, 0] });
const mount = pose({ position: [0, 0.13, 0.03], rotation: [80, -30, 140] });
function trackerMatrix(x, angles) {
    const tracker = compose(nativeRoom, compose(pose({ position: [x, 1.6, 2], rotation: angles }), mount));
    const elements = new T.Matrix4().compose(tracker.p, tracker.q, new T.Vector3(1, 1, 1)).elements;
    return [0, 1, 2].map((r) => [elements[r], elements[r + 4], elements[r + 8], elements[r + 12]]);
}
let sequence = 0, stale = false, freeze = false, matrix = trackerMatrix(0, [0, 0, 0]);
await page.route("**/api/tracking", (route) => {
    if (route.request().method() === "POST") return route.fulfill({ json: { ok: true } });
    if (!freeze) sequence++;
    return route.fulfill({ json: { status: "connected", message: "Synthetic direction-guide check",
        sequence, ageMs: stale ? 2000 : 0, devices: [{ serial: "TEST-HEAD", kind: "tracker", connected: true,
            valid: true, matrix }] } });
});
const draft = () => page.evaluate(() => JSON.parse(localStorage.getItem("elts-calibration-v1-draft")).config);
try {
    await page.goto("http://127.0.0.1:8766");
    await page.waitForFunction(() => document.querySelector("#head-quality").textContent === "Live");
    const before = await draft();
    await page.locator("#calibrate-head").click();
    await page.screenshot({ path: fileURLToPath(new URL("head-directions-desktop.png", output)) });
    await page.locator("#direction-cancel").click();
    assert.deepEqual(await draft(), before, "Cancel leaves settings intact");
    await page.locator("#calibrate-head").click();
    await page.locator("#direction-capture").click();
    stale = true;
    await page.waitForFunction(() => document.querySelector("#direction-status").textContent.includes("No fresh"));
    assert.equal(await page.locator("#direction-progress").textContent(), "Step 1 of 7");
    stale = false;
    await page.waitForFunction(() => document.querySelector("#head-quality").textContent === "Live");
    freeze = true;
    await page.locator("#direction-capture").click();
    await page.waitForFunction(() => document.querySelector("#direction-status").textContent.includes("stopped updating"));
    freeze = false;
    const captures = [
        [0, [0, 0, 0]], [0, [0, 35, 0]], [0, [0, -35, 0]], [0, [20, 0, 0]],
        [-0.3, [0, 0, 0]], [0.3, [0, 0, 0]], [0, [0, 0, 0]],
    ];
    for (const [index, [x, angles]] of captures.entries()) {
        matrix = trackerMatrix(x, angles);
        await page.locator("#direction-capture").click();
        await page.waitForFunction((text) => document.querySelector("#direction-progress").textContent === text,
            index === 6 ? "Review directions" : `Step ${index + 2} of 7`);
    }
    assert.deepEqual(await draft(), before, "Review does not apply settings automatically");
    await page.screenshot({ path: fileURLToPath(new URL("head-directions-review-desktop.png", output)) });
    await page.locator("#direction-apply").click();
    await page.waitForFunction(() => !document.querySelector("#direction-dialog").open);
    await page.waitForFunction(() => document.querySelector("#head-position").textContent.includes("1.305 / 2.000"));
    const after = await draft();
    assert.notDeepEqual(after.head.mount.rotation, before.head.mount.rotation);
    assert.deepEqual(after.head.mount.position, before.head.mount.position);
    assert.equal(after.head.reference, null);
    matrix = trackerMatrix(0.4, [0, 30, 0]);
    await page.waitForFunction(() => document.querySelector("#head-position").textContent.includes("0.400 / 1.305 / 2.000"));
    await page.waitForFunction(() => document.querySelector("#head-angles").textContent.includes("30.0°"));
    assert.deepEqual(errors, []);
    console.log("PASS: cancel, stale capture, repeated sample rejection, seven guided captures, explicit Apply, preserved offsets, live movement and yaw after alignment, no page errors.");
} finally { await browser.close(); }
