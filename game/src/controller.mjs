function clampUnit(value) {
  return Math.min(1, Math.max(0, value));
}

export function normalizeControllerEvent(event) {
  if (!event || typeof event !== 'object') {
    return null;
  }

  if (event.type === 'aim') {
    if (!Number.isFinite(event.x) || !Number.isFinite(event.y)) {
      return null;
    }
    return {
      type: 'aim',
      x: clampUnit(event.x),
      y: clampUnit(event.y),
    };
  }

  if (event.type === 'aimDelta') {
    if (!Number.isFinite(event.dx) || !Number.isFinite(event.dy)) {
      return null;
    }
    return {
      type: 'aimDelta',
      dx: event.dx,
      dy: event.dy,
    };
  }

  if (event.type === 'fire') {
    return { type: 'fire' };
  }

  return null;
}
