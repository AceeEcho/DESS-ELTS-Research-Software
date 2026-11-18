import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { nextUnusedId, normalizedId } from "../web/participant-id.js";

const corpus = JSON.parse(readFileSync(new URL("./participant-ids.json", import.meta.url), "utf8"));
test("calibration drafts normalize the shared participant ID examples", () => {
    for (const { text, id } of corpus.inputs)
        assert.equal(normalizedId(text), id === null ? "" : String(id));
});

test("the suggested ID skips both readable and damaged profiles", () => {
    assert.equal(nextUnusedId([]), 0);
    assert.equal(nextUnusedId([{ id: 0, available: false }, { id: 2, available: true }]), 1);
});
