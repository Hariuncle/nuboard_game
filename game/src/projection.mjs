export const CAMERA_FOV_DEGREES = 45;
export const CAMERA_NEAR = 0.1;
export const CAMERA_FAR = 20;
export const CAMERA_POSITION_Z = 4;
export const WORLD_DEPTH_SPAN = 4;
export const DEFAULT_CAMERA_ASPECT = 16 / 9;

export function projectRadius(depth, radius) {
  const safeRadius = Math.max(0, finiteOr(radius, 0));
  const cameraDistance = CAMERA_POSITION_Z + clampedDepth(depth) * WORLD_DEPTH_SPAN;
  return safeRadius * (CAMERA_POSITION_Z / cameraDistance);
}

export function projectWorldPosition(entity, aspect = DEFAULT_CAMERA_ASPECT) {
  const depth = clampedDepth(entity?.depth);
  const cameraDistance = CAMERA_POSITION_Z + depth * WORLD_DEPTH_SPAN;
  const halfHeight = Math.tan((CAMERA_FOV_DEGREES * Math.PI) / 360) * cameraDistance;
  const safeAspect = positiveOr(aspect, DEFAULT_CAMERA_ASPECT);
  const screenX = clamp(finiteOr(entity?.x, entity?.laneX ?? 0.5), 0, 1);
  const screenY = clamp(finiteOr(entity?.y, 0.5), 0, 1);

  return {
    x: (screenX * 2 - 1) * halfHeight * safeAspect,
    y: (1 - screenY * 2) * halfHeight,
    z: -depth * WORLD_DEPTH_SPAN,
  };
}

function clampedDepth(value) {
  return clamp(finiteOr(value, 1), 0, 1);
}

function finiteOr(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}

function positiveOr(value, fallback) {
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function clamp(value, minimum, maximum) {
  return Math.max(minimum, Math.min(maximum, value));
}
