import test from 'node:test';
import assert from 'node:assert/strict';

import {
  AIM_MARGIN_PX,
  advanceTouchGesture,
  aimFromClientPoint,
  moveAimByDelta,
  toScreenPoint,
} from '../src/aim.mjs';

test('visual safety margin covers the crosshair at full recoil', () => {
  assert.equal(AIM_MARGIN_PX, 44);
  assert.ok(AIM_MARGIN_PX >= 41);
});

test('relative movement reaches every edge while preserving the visual safety margin', () => {
  assert.deepEqual(
    moveAimByDelta({ x: 0.5, y: 0.5 }, 2000, -2000, { width: 1000, height: 500 }),
    { x: 0.956, y: 0.088 },
  );
});

test('visible cursor coordinates map directly to aim coordinates', () => {
  assert.deepEqual(
    aimFromClientPoint(250, 150, { left: 50, top: 50, width: 400, height: 200 }, 0),
    { x: 0.5, y: 0.5 },
  );
});

test('normalized entity coordinates map to a fixed screen point', () => {
  assert.deepEqual(toScreenPoint({ x: 0.25, y: 0.75 }, 800, 600), { x: 200, y: 450 });
});

test('an active touch gesture ignores additional touch starts', () => {
  const idle = { id: null, x: 0, y: 0, distance: 0 };
  const first = advanceTouchGesture(idle, {
    type: 'start',
    pointerId: 7,
    clientX: 100,
    clientY: 200,
  });
  const second = advanceTouchGesture(first.gesture, {
    type: 'start',
    pointerId: 8,
    clientX: 300,
    clientY: 400,
  });

  assert.deepEqual(first.gesture, { id: 7, x: 100, y: 200, distance: 0 });
  assert.strictEqual(second.gesture, first.gesture);
});

test('the active touch accumulates drag distance and a short finish fires', () => {
  const started = advanceTouchGesture(
    { id: null, x: 0, y: 0, distance: 0 },
    { type: 'start', pointerId: 7, clientX: 100, clientY: 200 },
  );
  const moved = advanceTouchGesture(started.gesture, {
    type: 'move',
    pointerId: 7,
    clientX: 103,
    clientY: 204,
  });
  const finished = advanceTouchGesture(moved.gesture, { type: 'finish', pointerId: 7 });

  assert.deepEqual(
    { deltaX: moved.deltaX, deltaY: moved.deltaY, distance: moved.gesture.distance },
    { deltaX: 3, deltaY: 4, distance: 5 },
  );
  assert.equal(finished.shouldFire, true);
  assert.equal(finished.gesture.id, null);
});

test('cancelling an active touch clears it without firing', () => {
  const active = { id: 7, x: 100, y: 200, distance: 0 };
  const cancelled = advanceTouchGesture(active, { type: 'cancel', pointerId: 7 });

  assert.equal(cancelled.gesture.id, null);
  assert.equal(cancelled.shouldFire, false);
});
