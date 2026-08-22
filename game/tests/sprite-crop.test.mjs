import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [html, styles, gameSource] = await Promise.all([
  readFile(new URL("../index.html", import.meta.url), "utf8"),
  readFile(new URL("../styles.css", import.meta.url), "utf8"),
  readFile(new URL("../src/game.mjs", import.meta.url), "utf8"),
]);

test("gameplay layers a WebGL actor canvas below the 2D effects canvas", () => {
  const webglIndex = html.indexOf('id="webgl-canvas"');
  const overlayIndex = html.indexOf('id="game-canvas"');

  assert.ok(webglIndex >= 0, "index.html should contain #webgl-canvas");
  assert.ok(webglIndex < overlayIndex, "the WebGL canvas should precede the effects canvas");
  assert.match(styles, /#webgl-canvas[\s\S]*?#game-canvas|#webgl-canvas\s*,\s*#game-canvas/);
});

test("gameplay drives createScene3D without sprite-sheet actor rendering", () => {
  assert.match(gameSource, /import\s*\{\s*createScene3D\s*\}\s*from\s*["']\.\/scene3d\.mjs["']/);
  assert.match(gameSource, /\.sync\(/);
  assert.match(gameSource, /\.render\(/);
  assert.doesNotMatch(gameSource, /spriteSheets|meadowSprites|drawDrone|drawDefeatedActor/);
  assert.doesNotMatch(gameSource, /context\.drawImage\s*\(\s*image\s*,\s*sx\s*,\s*sy\s*,\s*sw\s*,\s*sh/);
});

test("intro video uses the composed H3 meadow output", () => {
  assert.match(html, /src="\.\/assets\/video\/h3-meadow-intro\.mp4"/);
  assert.match(html, /poster="\.\/assets\/images\/meadow-intro-01\.png"/);
  assert.match(html, /id="video-fallback"/);
});
