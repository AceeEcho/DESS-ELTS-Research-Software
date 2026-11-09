// Synthetic UI coverage. Physical input is checked separately through the live bridge.
import { pathToFileURL, fileURLToPath } from "node:url";
import { mkdir } from "node:fs/promises";
import assert from "node:assert/strict";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH
    ? pathToFileURL(process.env.PLAYWRIGHT_MODULE_PATH).href : "playwright");
const browser = await chromium.launch({ headless: true, args: ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"] });
const output = new URL("../test-results/", import.meta.url);
await mkdir(output, { recursive: true });
const initial = await (await fetch("http://127.0.0.1:8766/api/state")).json();
initial.defaults.weapon.serial = "TEST-GUN";
initial.defaults.head.serial = "TEST-HEAD";
const errors = [];
try {
    for (const [name, width, height] of [["desktop", 1440, 1000], ["mobile", 390, 844]]) {
        const page = await browser.newPage({ viewport: { width, height } });
        page.on("pageerror", error => errors.push(error.message));
        await page.addInitScript(config => localStorage.setItem("elts-calibration-v1-draft", JSON.stringify({ config })), initial.defaults);
        let pressed = false, ageMs = 0;
        await page.route("**/api/tracking", route => route.fulfill({ json: route.request().method() === "POST" ? { ok: true } : {
            status: "connected", message: "Synthetic UI check", sequence: 1, ageMs,
            devices: [{ serial: "TEST-GUN", connected: true, kind: "controller", valid: false,
                trigger: { available: true, pressed, presses: 2, releases: 2 } }],
        } }));
        await page.goto("http://127.0.0.1:8766");
        await page.waitForFunction(() => document.querySelector("#trigger-indicator").dataset.state === "released");
        pressed = true;
        await page.waitForFunction(() => document.querySelector("#trigger-indicator").dataset.state === "pressed");
        await page.locator("#trigger-indicator").scrollIntoViewIfNeeded();
        await page.screenshot({ path: fileURLToPath(new URL(`trigger-${name}.png`, output)) });
        const layout = await page.locator("#trigger-indicator").evaluate(el => ({ width: el.clientWidth, scroll: el.scrollWidth, x: el.getBoundingClientRect().left, right: el.getBoundingClientRect().right }));
        assert.ok(layout.scroll <= layout.width && layout.x >= 0 && layout.right <= width, "Indicator must fit without horizontal overflow");
        ageMs = 1000;
        await page.waitForFunction(() => document.querySelector("#trigger-indicator").dataset.state === "unavailable");
        await page.close();
    }
    assert.deepEqual(errors, []);
    console.log("PASS: desktop/mobile trigger indicator, pressed/released/stale states and layout");
} finally { await browser.close(); }
