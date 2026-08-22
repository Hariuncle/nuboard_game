export const AIM_MARGIN_PX = 44;

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function bounds(width, height, margin) {
  return {
    minX: margin / Math.max(1, width),
    maxX: 1 - margin / Math.max(1, width),
    minY: margin / Math.max(1, height),
    maxY: 1 - margin / Math.max(1, height),
  };
}

export function moveAimByDelta(aim, dx, dy, viewport, margin = AIM_MARGIN_PX) {
  const aimBounds = bounds(viewport.width, viewport.height, margin);
  return {
    x: clamp(aim.x + dx / Math.max(1, viewport.width), aimBounds.minX, aimBounds.maxX),
    y: clamp(aim.y + dy / Math.max(1, viewport.height), aimBounds.minY, aimBounds.maxY),
  };
}

export function aimFromClientPoint(clientX, clientY, rect, margin = AIM_MARGIN_PX) {
  const aimBounds = bounds(rect.width, rect.height, margin);
  return {
    x: clamp((clientX - rect.left) / Math.max(1, rect.width), aimBounds.minX, aimBounds.maxX),
    y: clamp((clientY - rect.top) / Math.max(1, rect.height), aimBounds.minY, aimBounds.maxY),
  };
}

export function toScreenPoint(point, width, height) {
  return { x: point.x * width, y: point.y * height };
}

export function advanceTouchGesture(gesture, action, tapDistance = 10) {
  const unchanged = {
    gesture,
    deltaX: 0,
    deltaY: 0,
    shouldFire: false,
  };

  if (action.type === 'start') {
    if (gesture.id !== null) return unchanged;
    return {
      ...unchanged,
      gesture: {
        id: action.pointerId,
        x: action.clientX,
        y: action.clientY,
        distance: 0,
      },
    };
  }

  if (gesture.id !== action.pointerId) return unchanged;

  if (action.type === 'move') {
    const deltaX = action.clientX - gesture.x;
    const deltaY = action.clientY - gesture.y;
    return {
      gesture: {
        ...gesture,
        x: action.clientX,
        y: action.clientY,
        distance: gesture.distance + Math.hypot(deltaX, deltaY),
      },
      deltaX,
      deltaY,
      shouldFire: false,
    };
  }

  if (action.type === 'finish' || action.type === 'cancel') {
    return {
      ...unchanged,
      gesture: { id: null, x: 0, y: 0, distance: 0 },
      shouldFire: action.type === 'finish' && gesture.distance < tapDistance,
    };
  }

  return unchanged;
}
