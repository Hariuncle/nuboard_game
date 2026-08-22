import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const gameSource = await readFile(new URL("../src/game.mjs", import.meta.url), "utf8");

function readMeadowSpriteCrops(source) {
  const block = source.match(/const meadowSprites = \[([\s\S]*?)\n\];/);
  assert.ok(block, "game.mjs should declare meadowSprites");

  return [...block[1].matchAll(
    /\{ sheet: "([^"]+)", source: \[(\d+), (\d+), (\d+), (\d+)\] \}/g,
  )].map((match) => ({
    sheet: match[1],
    source: match.slice(2).map(Number),
  }));
}

test("meadow enemies crop only the eight large character illustrations", () => {
  assert.deepEqual(readMeadowSpriteCrops(gameSource), [
    { sheet: "raiders", source: [5, 0, 520, 570] },
    { sheet: "raiders", source: [10, 710, 520, 550] },
    { sheet: "minstrel", source: [0, 0, 550, 505] },
    { sheet: "minstrel", source: [15, 825, 520, 370] },
    { sheet: "support", source: [0, 35, 520, 550] },
    { sheet: "support", source: [0, 740, 520, 500] },
    { sheet: "defenders", source: [10, 245, 450, 410] },
    { sheet: "defenders", source: [10, 955, 440, 370] },
  ]);
});
