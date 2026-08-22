import test from 'node:test';
import assert from 'node:assert/strict';

import { normalizeControllerEvent } from '../src/controller.mjs';

test('clamps aim coordinates to the normalized 0..1 range', () => {
  assert.deepEqual(normalizeControllerEvent({ type: 'aim', x: -0.25, y: 1.4 }), {
    type: 'aim',
    x: 0,
    y: 1,
  });
});

test('preserves aim coordinates already inside the normalized range', () => {
  assert.deepEqual(normalizeControllerEvent({ type: 'aim', x: 0.25, y: 0.75 }), {
    type: 'aim',
    x: 0.25,
    y: 0.75,
  });
});

test('normalizes a fire signal to a protocol-free action event', () => {
  assert.deepEqual(normalizeControllerEvent({ type: 'fire', source: 'trigger' }), {
    type: 'fire',
  });
});

test('preserves finite relative aim deltas from a BLE HID bridge', () => {
  assert.deepEqual(normalizeControllerEvent({ type: 'aimDelta', dx: -18, dy: 7 }), {
    type: 'aimDelta',
    dx: -18,
    dy: 7,
  });
  assert.equal(normalizeControllerEvent({ type: 'aimDelta', dx: Number.NaN, dy: 0 }), null);
});

test('returns null for unsupported or malformed event types', () => {
  assert.equal(normalizeControllerEvent({ type: 'reload' }), null);
  assert.equal(normalizeControllerEvent({}), null);
  assert.equal(normalizeControllerEvent({ type: 'aim' }), null);
  assert.equal(normalizeControllerEvent({ type: 'aim', x: Number.NaN, y: 0.5 }), null);
  assert.equal(normalizeControllerEvent({ type: 'aim', x: 0.5, y: Number.POSITIVE_INFINITY }), null);
  assert.equal(normalizeControllerEvent(null), null);
});
