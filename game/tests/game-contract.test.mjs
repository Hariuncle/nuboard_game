import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../src/game.mjs", import.meta.url), "utf8");

function functionBody(name) {
  const start = source.indexOf(`function ${name}(`);
  assert.notEqual(start, -1, `${name} should exist`);
  const next = source.indexOf("\nfunction ", start + 1);
  return source.slice(start, next === -1 ? source.length : next);
}

test("finished rounds reject fire before shot or miss audio can play", () => {
  const shoot = functionBody("shoot");
  const phaseGuard = shoot.indexOf('state.phase !== "playing"');
  const shotAudio = shoot.indexOf('audio.play("shot")');
  const missAudio = shoot.indexOf('audio.play("miss")');

  assert.ok(phaseGuard >= 0);
  assert.ok(phaseGuard < shotAudio);
  assert.ok(phaseGuard < missAudio);
});

test("starting a new round cancels every pending enemy exit sound timer", () => {
  const startGame = functionBody("startGame");
  const cancelTimers = startGame.indexOf("for (const timer of exitTimers) window.clearTimeout(timer)");
  const clearTimers = startGame.indexOf("exitTimers.clear()");

  assert.ok(cancelTimers >= 0);
  assert.ok(cancelTimers < clearTimers);
});

test("defeated actors are synchronized into the 3D fall animation", () => {
  const render = functionBody("render");
  const syncActors = render.indexOf("scene3d?.sync(entities(), defeatedActors, state.elapsed)");
  const renderScene = render.indexOf("scene3d?.render()");

  assert.ok(syncActors >= 0, "3D scene should receive defeated actors");
  assert.ok(syncActors < renderScene, "actor state should be synchronized before rendering");
});
