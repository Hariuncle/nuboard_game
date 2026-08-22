import test from "node:test";
import assert from "node:assert/strict";

import { advanceImpactEffects, createImpactEffect } from "../game/src/effects.mjs";

test("a hit creates a strong flower burst at the reticle", () => {
  const effect = createImpactEffect({ x: 0.4, y: 0.6, hit: true }, () => 0.5);

  assert.equal(effect.hit, true);
  assert.equal(effect.x, 0.4);
  assert.equal(effect.y, 0.6);
  assert.equal(effect.particles.length, 18);
  assert.equal(effect.life, 1);
});

test("a miss creates a smaller dust response", () => {
  const effect = createImpactEffect({ x: 0.2, y: 0.3, hit: false }, () => 0.5);

  assert.equal(effect.hit, false);
  assert.equal(effect.particles.length, 5);
  assert.ok(effect.particles.every((particle) => particle.kind === "dust"));
});

test("impact animation advances particles and removes expired effects", () => {
  const effect = createImpactEffect({ x: 0.5, y: 0.5, hit: true }, () => 0.75);
  const advanced = advanceImpactEffects([effect], 0.1);

  assert.equal(advanced.length, 1);
  assert.ok(advanced[0].life < effect.life);
  assert.notEqual(advanced[0].particles[0].x, effect.particles[0].x);
  assert.deepEqual(advanceImpactEffects(advanced, 2), []);
});
