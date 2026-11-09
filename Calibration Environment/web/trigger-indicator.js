// Use the explicitly assigned weapon tracker; never substitute the head tracker.
export function triggerIndicator(source, serial, frame, staleMs) {
    if (source !== "live") return { state: "unavailable", text: "Trigger · manual preview", detail: "Select live tracking to test the gun trigger." };
    if (!serial) return { state: "unavailable", text: "Trigger · assign weapon", detail: "Choose the gun tracker under Weapon." };
    const device = frame?.devices?.find((item) => item.serial === serial);
    const input = device?.trigger;
    if (!Number.isFinite(frame?.ageMs) || frame.ageMs > staleMs || !device?.connected || !input?.available)
        return { state: "unavailable", text: "Trigger · unavailable", detail: `${serial} · SteamVR: Held in hand + Left or Right Hand` };
    return { state: input.pressed ? "pressed" : "released", text: input.pressed ? "Trigger · PRESSED" : "Trigger · released",
        detail: `${serial} · ${input.presses} presses / ${input.releases} releases` };
}
