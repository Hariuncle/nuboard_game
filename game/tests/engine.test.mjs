import test from 'node:test';
import assert from 'node:assert/strict';

import {
  createGameState,
  fireAt,
  spawnDrone,
  tickGame,
} from '../src/engine.mjs';

test('createGameState returns a render-ready sixty second round', () => {
  const state = createGameState();

  assert.deepEqual(
    {
      phase: state.phase,
      score: state.score,
      combo: state.combo,
      maxCombo: state.maxCombo,
      signal: state.signal,
      timeLeft: state.timeLeft,
      shots: state.shots,
      hits: state.hits,
      drones: state.drones,
    },
    {
      phase: 'playing',
      score: 0,
      combo: 0,
      maxCombo: 0,
      signal: 100,
      timeLeft: 60,
      shots: 0,
      hits: 0,
      drones: [],
    },
  );
});

test('spawnDrone appends a normal target without mutating the previous state', () => {
  const initial = createGameState();
  const next = spawnDrone(initial, { x: 0.25, y: 0.75, vx: 0.1, vy: -0.1 });

  assert.equal(initial.drones.length, 0);
  assert.equal(next.drones.length, 1);
  assert.deepEqual(
    {
      id: next.drones[0].id,
      kind: next.drones[0].kind,
      x: next.drones[0].x,
      y: next.drones[0].y,
      hp: next.drones[0].hp,
      maxHp: next.drones[0].maxHp,
      score: next.drones[0].score,
    },
    { id: 1, kind: 'normal', x: 0.25, y: 0.75, hp: 1, maxHp: 1, score: 100 },
  );
});

test('default drones spawn inside opposite edges and move inward', () => {
  const leftSpawn = spawnDrone(createGameState());
  const bothSides = spawnDrone(leftSpawn);

  assert.deepEqual(
    bothSides.drones.map(({ x, vx }) => ({ x, vx })),
    [
      { x: 0.05, vx: 0.12 },
      { x: 0.95, vx: -0.12 },
    ],
  );
});

test('fixed-world drone motion does not wrap across screen edges', () => {
  const state = spawnDrone(createGameState(), {
    x: 0.95,
    y: 0.5,
    vx: 0.1,
    curve: 0,
  });

  const moved = tickGame(state, 1);

  assert.ok(Math.abs(moved.drones[0].x - 1.05) < Number.EPSILON);
  assert.equal(moved.drones[0].y, 0.5);
});

test('ordinary drones are retired only after moving fully beyond the fixed viewport', () => {
  const partlyVisible = spawnDrone(createGameState(), {
    x: 1.04,
    y: 0.5,
    vx: 0.01,
    curve: 0,
  });
  const fullyGone = spawnDrone(createGameState(), {
    x: 1.08,
    y: 0.5,
    vx: 0.01,
    curve: 0,
  });

  assert.equal(tickGame(partlyVisible, 1).drones.length, 1);
  assert.equal(tickGame(fullyGone, 1).drones.length, 0);
});

test('the default boss remains on-screen in the fixed world', () => {
  const state = spawnDrone(createGameState(), { kind: 'boss', curve: 0 });

  const moved = tickGame(state, 10);

  assert.equal(moved.drones[0].x, 0.5);
});

test('fireAt destroys a normal drone, scores it, and builds a timed combo', () => {
  let state = createGameState();
  state = spawnDrone(state, { x: 0.2, y: 0.2 });
  state = spawnDrone(state, { x: 0.8, y: 0.8 });

  const first = fireAt(state, 0.2, 0.2, 0.1);
  const second = fireAt(first, 0.8, 0.8, 1.0);

  assert.equal(first.score, 100);
  assert.equal(first.combo, 1);
  assert.equal(second.score, 300);
  assert.equal(second.combo, 2);
  assert.equal(second.maxCombo, 2);
  assert.equal(second.shots, 2);
  assert.equal(second.hits, 2);
  assert.equal(second.drones.length, 0);
  assert.equal(state.drones.length, 2);
});

test('fireAt does not hit a target rendered on the opposite screen edge', () => {
  const state = spawnDrone(createGameState(), { x: 0.99, y: 0.5, radius: 0.055 });

  const result = fireAt(state, 0.01, 0.5, 0.1, { width: 1920, height: 1080 });

  assert.equal(result.hits, 0);
  assert.equal(result.signal, 90);
});

test('fireAt scales normalized coordinates to the rendered viewport aspect ratio', () => {
  let state = createGameState();
  state = spawnDrone(state, { x: 0.5, y: 0.5, radius: 0.055 });

  const visuallyOutside = fireAt(state, 0.55, 0.5, 0.1, { width: 1920, height: 1080 });

  assert.equal(visuallyOutside.hits, 0);
  assert.equal(visuallyOutside.signal, 90);
});

test('tickGame expires a combo after 1.2 seconds without a hit', () => {
  let state = createGameState();
  state = spawnDrone(state, { x: 0.5, y: 0.5 });
  state = fireAt(state, 0.5, 0.5, 0);

  const next = tickGame(state, 1.21);

  assert.equal(next.combo, 0);
  assert.equal(next.maxCombo, 1);
});

test('an armored drone takes two hits and awards 250 points only when destroyed', () => {
  let state = createGameState();
  state = spawnDrone(state, { kind: 'armored', x: 0.5, y: 0.5 });

  const damaged = fireAt(state, 0.5, 0.5, 0.1);
  const destroyed = fireAt(damaged, 0.5, 0.5, 0.5);

  assert.equal(damaged.drones[0].hp, 1);
  assert.equal(damaged.score, 0);
  assert.equal(damaged.hits, 1);
  assert.equal(damaged.lastShot.destroyed, false);
  assert.ok(damaged.drones[0].stunUntil > 0.1);
  assert.equal(destroyed.drones.length, 0);
  assert.equal(destroyed.lastShot.destroyed, true);
  assert.equal(destroyed.lastShot.target.kind, 'armored');
  assert.equal(destroyed.score, 500);
  assert.equal(destroyed.combo, 2);
});

test('a miss reduces signal and depleted signal ends the round', () => {
  let state = createGameState({ signal: 10 });

  state = fireAt(state, 0.5, 0.5, 0);

  assert.equal(state.shots, 1);
  assert.equal(state.hits, 0);
  assert.equal(state.signal, 0);
  assert.equal(state.phase, 'defeat');
  assert.equal(state.endReason, 'signal');
});

test('tickGame moves drones and automatically spawns the boss at forty elapsed seconds', () => {
  let state = createGameState();
  state = spawnDrone(state, {
    x: 0.2,
    y: 0.3,
    vx: 0.1,
    vy: 0.2,
    curve: 0,
  });

  const moved = tickGame(state, 1);

  assert.ok(Math.abs(moved.drones[0].x - 0.3) < Number.EPSILON);
  assert.ok(Math.abs(moved.drones[0].y - 0.5) < Number.EPSILON);

  state = createGameState({ elapsed: 39, timeLeft: 21 });
  const next = tickGame(state, 1);

  const boss = next.drones.find((drone) => drone.kind === 'boss');

  assert.ok(boss);
  assert.equal(boss.hp, 8);
  assert.equal(next.bossSpawned, true);
  assert.equal(next.timeLeft, 20);
  assert.equal(next.chapter, 3);
});

test('story enters the armored second chapter at twenty seconds', () => {
  const next = tickGame(createGameState({ elapsed: 19, timeLeft: 41 }), 1);
  assert.equal(next.chapter, 2);
});

test('destroying the boss wins the round', () => {
  let state = createGameState();
  state = spawnDrone(state, { kind: 'boss', x: 0.5, y: 0.5, hp: 1 });

  state = fireAt(state, 0.5, 0.5, 0.1);

  assert.equal(state.phase, 'victory');
  assert.equal(state.endReason, 'boss');
  assert.equal(state.score, 1000);
});

test('time expiring causes defeat and further input cannot change a finished round', () => {
  const state = tickGame(createGameState({ timeLeft: 0.5 }), 1);
  const afterFire = fireAt(state, 0.5, 0.5, 2);

  assert.equal(state.timeLeft, 0);
  assert.equal(state.phase, 'defeat');
  assert.equal(state.endReason, 'timeout');
  assert.strictEqual(afterFire, state);
});
