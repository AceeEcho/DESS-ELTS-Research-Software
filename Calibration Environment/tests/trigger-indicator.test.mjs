import test from "node:test";
import assert from "node:assert/strict";
import { triggerIndicator } from "../web/trigger-indicator.js";

test("trigger uses the weapon binding and rejects stale or unavailable input", () => {
    const frame = { ageMs: 10, devices: [
        { serial: "head", connected: true, trigger: { available: true, pressed: true } },
        { serial: "gun", connected: true, valid: false, trigger: { available: true, pressed: false, presses: 3, releases: 3 } },
    ] };
    assert.equal(triggerIndicator("live", "gun", frame, 250).state, "released");
    frame.devices[1].trigger.pressed = true;
    assert.equal(triggerIndicator("live", "gun", frame, 250).state, "pressed");
    frame.ageMs = 251;
    assert.equal(triggerIndicator("live", "gun", frame, 250).state, "unavailable");
    frame.ageMs = 0; frame.devices[1].trigger.available = false;
    assert.equal(triggerIndicator("live", "gun", frame, 250).state, "unavailable");
    assert.equal(triggerIndicator("manual", "gun", frame, 250).state, "unavailable");
    assert.equal(triggerIndicator("live", "", frame, 250).state, "unavailable");
});
