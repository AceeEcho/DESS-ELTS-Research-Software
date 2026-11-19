// Desktop interaction verification. Run with a Playwright package available;
// PLAYWRIGHT_MODULE_PATH can point to an existing shared installation.
import { pathToFileURL, fileURLToPath } from "node:url";
import { mkdir, writeFile } from "node:fs/promises";
import assert from "node:assert/strict";
import { installDraftFixture } from "./draft-fixture.mjs";
const { chromium } = await import(
    process.env.PLAYWRIGHT_MODULE_PATH
        ? pathToFileURL(process.env.PLAYWRIGHT_MODULE_PATH).href
        : "playwright"
);
const output = new URL("../test-results/", import.meta.url);
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
    headless: true,
    // Use the full Chromium browser: headless-shell rejects popup fullscreen.
    channel: "chromium",
    args: process.env.CALIBRATION_SOFTWARE_GL
        ? ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"]
        : [],
});
const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
await installDraftFixture(page);
const errors = [];
page.on("pageerror", (e) => errors.push(e.message));
page.on("console", (msg) => {
    if (msg.type() === "error") errors.push(msg.text());
});
page.on("dialog", (dialog) => dialog.accept());
const checks = [];
try {
    await page.goto(process.env.CALIBRATION_BASE_URL || "http://127.0.0.1:8766");
    // Gun tracker position is entered from the muzzle tip in feet and inches.
    await page.getByRole("button", { name: "Weapon", exact: true }).click();
    const mounting = page.locator("section").filter({ has: page.getByRole("heading", { name: "2 · Where the tracker sits on the gun", exact: true }) });
    const x = mounting.getByRole("group", { name: "X · left / right" });
    await x.getByLabel("Direction").selectOption({ label: "Left" });
    await x.getByLabel("Feet").fill("0");
    await x.getByLabel("Inches").fill("0.5");
    await x.getByLabel("Inches").press("Tab");
    const y = mounting.getByRole("group", { name: "Y · up / down" });
    await y.getByLabel("Direction").selectOption({ label: "Above" });
    await y.getByLabel("Feet").fill("0");
    await y.getByLabel("Inches").fill("3.25");
    await y.getByLabel("Inches").press("Tab");
    const z = mounting.getByRole("group", { name: "Z · forward / back" });
    await z.getByLabel("Direction").selectOption({ label: "Behind (toward you)" });
    await z.getByLabel("Feet").fill("1");
    await z.getByLabel("Inches").fill("2");
    await z.getByLabel("Inches").press("Tab");
    assert.equal(await page.getByRole("heading", { name: "Base station 1" }).count(), 0);
    await page.getByRole("button", { name: "Tracking", exact: true }).click();
    await page.locator("#source-select").selectOption("manual");
    await page.waitForFunction(
        () => document.querySelector("#head-quality").textContent === "Manual",
    );
    await page.waitForTimeout(1000);
    const gpu = await page.locator("#room-canvas").evaluate((canvas) => {
        const gl = canvas.getContext("webgl2");
        return {
            lost: gl?.isContextLost(),
            width: canvas.width,
            height: canvas.height,
        };
    });
    assert.equal(gpu.lost, false, "Room WebGL context must remain valid");
    checks.push("3D rendering context valid");
    await page.screenshot({
        path: fileURLToPath(new URL("desktop-verified.png", output)),
        fullPage: true,
    });
    await page.getByRole("button", { name: "Head", exact: true }).click();
    const height = page.getByRole("group", { name: "Participant height" });
    const eyeDepth = page.getByRole("group", { name: "Z · front / back" });
    await eyeDepth.getByLabel("Feet").fill("0");
    await eyeDepth.getByLabel("Inches").fill("2.75");
    await eyeDepth.getByLabel("Direction").selectOption("-1");
    await height.getByLabel("Feet").fill("6");
    await height.getByLabel("Inches").fill("2.5");
    await height.getByLabel("Inches").press("Tab");
    await page.locator('[data-path="head.manual.rotation.1"]').fill("30");
    await page.locator('[data-path="head.manual.rotation.1"]').press("Tab");
    await page.waitForFunction(() =>
        document.querySelector("#head-angles").textContent.includes("30.0°"),
    );
    await page
        .getByRole("button", { name: "Set looking forward", exact: true })
        .click();
    await page.waitForFunction(
        () =>
            document.querySelector("#head-reference").textContent ===
            "Relative to captured forward",
    );
    await page.locator('[data-path="head.manual.rotation.1"]').fill("45");
    await page.locator('[data-path="head.manual.rotation.1"]').press("Tab");
    await page.waitForFunction(() =>
        document.querySelector("#head-angles").textContent.includes("15.0°"),
    );
    checks.push("Forward capture reports relative head rotation");
    await page.locator('[data-path="head.mount.position.0"]').fill("0.05");
    await page.locator('[data-path="head.mount.position.0"]').press("Tab");
    await page.waitForFunction(() =>
        document
            .querySelector("#head-reference")
            .textContent.includes("forward not set"),
    );
    checks.push("Mount edits invalidate dependent references");
    await page.getByRole("button", { name: "Weapon", exact: true }).click();
    await page.locator('[data-path="weapon.manual.rotation.1"]').fill("180");
    await page.locator('[data-path="weapon.manual.rotation.1"]').press("Tab");
    await page.waitForFunction(() =>
        document
            .querySelector("#aim-readout")
            .textContent.includes("pointing away"),
    );
    checks.push("Backwards weapon cannot hit screen");
    await page
        .getByRole("button", { name: "Profiles & export", exact: true })
        .click();
    await page.locator("#profiles-dialog").waitFor({ state: "visible" });
    const digits = page.locator("#participant-id select");
    assert.equal(await digits.count(), 6);
    for (const [index, value] of Array.from("990001").entries())
        await digits.nth(index).selectOption(value);
    await page
        .getByRole("button", { name: "Save calibration", exact: true })
        .last()
        .click();
    await page.waitForFunction(() =>
        document
            .querySelector("#profile-result")
            .textContent.startsWith("Saved."),
    );
    await page
        .getByRole("button", { name: "Load selected calibration", exact: true })
        .click();
    await page.waitForFunction(
        () => !document.querySelector("#profiles-dialog").open,
    );
    const savedProfile = await page.request.get(
        new URL("/api/participant?id=990001", page.url()).href,
    );
    assert.equal(savedProfile.ok(), true);
    const savedRecord = await savedProfile.json();
    assert.equal("name" in savedRecord, false);
    const savedConfig = savedRecord.calibration;
    checks.push("Calibration saves under an exact numeric ID without a name");
    assert.equal(savedConfig.person.height, 74.5 * 0.0254);
    const fromMuzzle = savedConfig.weapon.mount.position.map((v, i) => v - savedConfig.weapon.muzzle[i]);
    [-0.5, 3.25, 14].forEach((inches, i) => assert.ok(Math.abs(fromMuzzle[i] - inches * 0.0254) < 1e-9));
    checks.push("Muzzle-to-tracker distances save in metres; base station settings are gone");
    await page.getByRole("button", { name: "Head", exact: true }).click();
    assert.equal(await height.getByLabel("Feet").inputValue(), "6");
    assert.equal(await height.getByLabel("Inches").inputValue(), "2.5");
    assert.ok(Math.abs(savedConfig.head.eye[2] - savedConfig.head.mount.position[2] + 2.75 * 0.0254) < 1e-9);
    assert.equal(await eyeDepth.getByLabel("Inches").inputValue(), "2.75");
    assert.equal(await eyeDepth.getByLabel("Direction").inputValue(), "-1");
    checks.push("Signed viewing-eye depth round-trips through a metric profile");
    checks.push("Participant height round-trips from feet and inches through a metric profile");
    const popupPromise = page.waitForEvent("popup");
    await page
        .getByRole("button", { name: "Open screen ↗", exact: true })
        .click();
    const popup = await popupPromise;
    await popup.waitForLoadState();
    await popup.waitForFunction(() =>
        document
            .querySelector("#screen-status")
            .textContent.includes("Eye-position perspective"),
    );
    checks.push("Separate screen window receives dashboard settings");
    await popup.setViewportSize({ width: 900, height: 700 });
    const canvasFillsViewport = () => {
        const bounds = document.querySelector("#preview-canvas").getBoundingClientRect();
        return bounds.left === 0 && bounds.top === 0 &&
            bounds.right === innerWidth && bounds.bottom === innerHeight;
    };
    await popup.waitForFunction(canvasFillsViewport);
    await popup.locator("#fullscreen").click();
    await popup.waitForFunction(() => Boolean(document.fullscreenElement));
    await popup.waitForFunction(canvasFillsViewport);
    checks.push("Participant canvas fills a non-matching window and fullscreen viewport");
    await popup.screenshot({
        path: fileURLToPath(new URL("screen-verified.png", output)),
    });
    await popup.close();
    // Synthetic API fixture exercises the live path without requiring hardware
    // or starting SteamVR. It is never saved as physical evidence.
    let fixture = {
        status: "connected",
        message: "Synthetic browser test fixture",
        sequence: 1,
        ageMs: 0,
        devices: [],
    };
    await page.route("**/api/tracking", (route) =>
        route.fulfill({
            json: route.request().method() === "POST" ? { ok: true } : fixture,
        }),
    );
    await page.getByRole("button", { name: "Tracking", exact: true }).click();
    await page.locator("#source-select").selectOption("live");
    await page.waitForFunction(
        () =>
            document.querySelector("#head-quality").textContent ===
            "Unavailable",
    );
    assert.equal(await page.locator("#head-forward").isDisabled(), true);
    await page.waitForFunction(
        () => !document.querySelector("#preview-message").hidden,
    );
    checks.push(
        "Missing live devices hide poses and disable capture; no synthetic fallback",
    );
    fixture.devices = [
        {
            serial: "SYNTHETIC-HEAD",
            kind: "tracker",
            connected: true,
            valid: true,
            reason: "valid",
            matrix: [
                [1, 0, 0, 0],
                [0, 1, 0, 1.78],
                [0, 0, 1, 2.53],
            ],
        },
        {
            serial: "SYNTHETIC-WEAPON",
            kind: "tracker",
            connected: true,
            valid: true,
            reason: "valid",
            matrix: [
                [1, 0, 0, 0.28],
                [0, 1, 0, 1.335],
                [0, 0, 1, 2.2],
            ],
        },
    ];
    await page.getByRole("button", { name: "Head", exact: true }).click();
    await page.locator("#head-serial").selectOption("SYNTHETIC-HEAD");
    await page.waitForFunction(() =>
        document
            .querySelector("#head-position")
            .textContent.includes("-0.050 / 1.650 / 2.500"),
    );
    await page.waitForFunction(() => document.querySelector("#eye-readout").textContent.startsWith("Eye XYZ"));
    const eyeBeforeMountEdit = await page.locator("#eye-readout").textContent();
    await page.locator('[data-path="head.mount.position.0"]').fill("0.15");
    await page.locator('[data-path="head.mount.position.0"]').press("Tab");
    await page.waitForFunction(() =>
        document
            .querySelector("#head-position")
            .textContent.includes("-0.150 / 1.650 / 2.500"),
    );
    // Pose labels also update directly on edits; allow the separate 100 ms
    // rendered-eye monitor to refresh before comparing its displayed result.
    await page.waitForTimeout(250);
    assert.equal(await page.locator("#eye-readout").textContent(), eyeBeforeMountEdit,
        "Changing the schematic head centre preserves the measured live eye position");
    checks.push("Head-centre reference edits preserve the tracker-to-eye measurement and live viewing position");
    await page
        .getByRole("button", { name: "Set looking forward", exact: true })
        .click();
    fixture.devices[0].matrix = [
        [0, 0, 1, 0],
        [0, 1, 0, 1.78],
        [-1, 0, 0, 2.53],
    ];
    await page.waitForFunction(() =>
        document.querySelector("#head-angles").textContent.includes("90.0°"),
    );
    checks.push(
        "Synthetic live tracker binding, inverse mounting and relative rotation work end to end",
    );
    fixture.ageMs = 600;
    await page.waitForFunction(
        () =>
            document.querySelector("#head-quality").textContent ===
            "Unavailable",
    );
    assert.equal(await page.locator("#head-forward").isDisabled(), true);
    checks.push(
        "Synthetic stale live frame disables capture and hides perspective",
    );
    await page.getByRole("button", { name: "Tracking", exact: true }).click();
    await page
        .getByRole("button", { name: "Disconnect live tracking", exact: true })
        .click();
    await page.waitForFunction(
        () => document.querySelector("#head-quality").textContent === "Manual",
    );
    await page
        .getByRole("button", { name: "Room", exact: true })
        .first()
        .click();
    await page.getByRole("button", { name: "Side", exact: true }).click();
    const before = await page.locator("#room-canvas").screenshot();
    await page.getByRole("button", { name: "Top", exact: true }).click();
    await page.waitForTimeout(100);
    const after = await page.locator("#room-canvas").screenshot();
    assert.notDeepEqual(before, after);
    checks.push("Camera presets visibly change the room view");
    await page.reload();
    await page.waitForFunction(
        () =>
            document.querySelector("#save-state").textContent ===
            "Restored calibration draft",
    );
    checks.push("Calibration draft survives refresh");
    await page.setViewportSize({ width: 1366, height: 768 });
    await page
        .getByRole("button", { name: "Room", exact: true })
        .first()
        .click();
    await page
        .getByRole("button", { name: "Room", exact: true })
        .last()
        .click();
    await page.waitForTimeout(250);
    assert.equal(
        await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
    );
    await page.screenshot({
        path: fileURLToPath(new URL("desktop-1366.png", output)),
        fullPage: true,
    });
    checks.push("1366px desktop layout has no horizontal overflow");
    assert.deepEqual(errors, []);
    await writeFile(
        new URL("browser-check.json", output),
        JSON.stringify({ checks, errors, gpu, desktopOnly: true }, null, 2),
    );
    console.log(JSON.stringify({ checks, errors, gpu }, null, 2));
} finally {
    await browser.close();
}
