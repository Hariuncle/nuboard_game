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

test("armored HP bars are drawn inside the actor fall transform", () => {
  const drawDrone = functionBody("drawDrone");
  const hpBar = drawDrone.indexOf("fillRect(-radius");
  const restore = drawDrone.indexOf("context.restore()");

  assert.ok(hpBar >= 0, "HP bar should use actor-local coordinates");
  assert.ok(hpBar < restore, "HP bar should be drawn before restoring the actor transform");
});
