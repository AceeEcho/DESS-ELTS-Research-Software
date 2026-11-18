// Keep browser rehearsals isolated from the station's retained draft files.
export async function installDraftFixture(page) {
    let value = null;
    await page.context().route("**/api/draft*", route => {
        if (route.request().method() === "POST") {
            value = route.request().postDataJSON();
            return route.fulfill({ json: { ok: true } });
        }
        return route.fulfill({ contentType: "application/json", body: JSON.stringify(value) });
    });
    return {
        read: () => value,
        clear: () => { value = null; },
        async waitFor(predicate) {
            for (let i = 0; i < 100; i++) {
                if (value && predicate(value)) return value;
                await page.waitForTimeout(30);
            }
            throw new Error("Expected calibration draft was not saved.");
        }
    };
}
