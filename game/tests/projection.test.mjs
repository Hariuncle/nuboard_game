import test from 'node:test';
import assert from 'node:assert/strict';

import {
  CAMERA_FAR,
  CAMERA_FOV_DEGREES,
  CAMERA_NEAR,
  CAMERA_POSITION_Z,
  projectRadius,
  projectWorldPosition,
} from '../src/projection.mjs';

test('nearer entities project to a larger apparent radius', () => {
  const baseRadius = 0.055;

  assert.ok(projectRadius(0.2, baseRadius) > projectRadius(0.8, baseRadius));
});

test('world projection places deeper entities farther from the camera', () => {
  const near = projectWorldPosition({ x: 0.5, y: 0.5, depth: 0.2 });
  const far = projectWorldPosition({ x: 0.5, y: 0.5, depth: 0.8 });

  assert.ok(Number.isFinite(CAMERA_FOV_DEGREES));
  assert.ok(CAMERA_NEAR > 0);
  assert.ok(CAMERA_FAR > CAMERA_NEAR);
  assert.ok(CAMERA_POSITION_Z > near.z);
  assert.ok(near.z > far.z);
  assert.deepEqual({ x: near.x, y: near.y }, { x: 0, y: 0 });
});
