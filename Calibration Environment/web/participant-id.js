// Shared by the browser's draft reader and its cross-language identity checks.
export function normalizedId(value) {
    if (typeof value !== "string" && typeof value !== "number") return "";
    const text = String(value).trim();
    return /^[0-9]{1,6}$/.test(text) ? String(Number(text)) : "";
}

export function nextUnusedId(participants) {
    // A damaged profile still occupies its ID.
    const used = new Set(participants.map(record => record.id));
    let id = 0;
    while (used.has(id)) id++;
    return id <= 999999 ? id : null;
}

// Each place is a native combobox, so every ID is reachable without typing.
export function participantIdPicker(container) {
    const places = ["Hundred thousands", "Ten thousands", "Thousands", "Hundreds", "Tens", "Ones"];
    const digits = places.map(place => {
        const select = document.createElement("select");
        select.setAttribute("aria-label", place);
        for (let value = 0; value <= 9; value++) select.add(new Option(String(value), String(value)));
        container.append(select);
        return select;
    });
    return {
        get() { return normalizedId(digits.map(select => select.value).join("")); },
        set(id) {
            const text = String(id).padStart(6, "0");
            digits.forEach((select, index) => { select.value = text[index]; });
        },
        focus() { digits[digits.length - 1].focus(); },
    };
}
