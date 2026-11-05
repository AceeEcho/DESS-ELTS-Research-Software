export const METRES_PER_INCH = 0.0254;

export function distanceFields(path, label, metres, directions) {
    const totalInches = Math.round(Math.abs(metres) / METRES_PER_INCH * 100) / 100;
    const feet = Math.floor(totalInches / 12);
    const inches = Number((totalInches - feet * 12).toFixed(2));
    const direction = directions
        ? `<label for="${path}-direction">Direction<select id="${path}-direction">
            <option value="1" ${metres >= 0 ? "selected" : ""}>${directions[0]}</option>
            <option value="-1" ${metres < 0 ? "selected" : ""}>${directions[1]}</option>
        </select></label>`
        : "";
    return `<div data-distance-path="${path}" role="group" aria-label="${label}">
        <div class="field-title">${label}</div>
        <div class="vector${directions ? "" : " single-row"}">
            <label for="${path}-feet">Feet<input id="${path}-feet" type="number" min="0" step="1" value="${feet}" required></label>
            <label for="${path}-inches">Inches<input id="${path}-inches" type="number" min="0" max="12" step="any" value="${inches}" required></label>
            ${direction}
        </div>
    </div>`;
}

// Convert the entered magnitude and optional axis direction to profile metres.
export function readDistance(path) {
    const feet = document.getElementById(path + "-feet");
    const inches = document.getElementById(path + "-inches");
    if (!feet.checkValidity() || !inches.checkValidity() || inches.valueAsNumber >= 12)
        throw new Error("Enter whole, non-negative feet and inches from 0 up to (but below) 12.");
    const direction = Number(document.getElementById(path + "-direction")?.value ?? 1);
    return direction * (feet.valueAsNumber * 12 + inches.valueAsNumber) * METRES_PER_INCH;
}
