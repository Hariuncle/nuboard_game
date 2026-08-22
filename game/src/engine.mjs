import { projectRadius } from './projection.mjs';

const COMBO_WINDOW_SECONDS = 1.2;
const MAX_COMBO = 8;
const MISS_SIGNAL_COST = 10;
const BOSS_SPAWN_SECONDS = 40;
const WAVE_TWO_SECONDS = 20;
export const BREACH_DEPTH = 0;

const DRONE_TYPES = {
  normal: { hp: 1, radius: 0.055, score: 100, approachSpeed: 0.075, breachDamage: 15 },
  armored: { hp: 2, radius: 0.07, score: 250, approachSpeed: 0.055, breachDamage: 25 },
  boss: { hp: 8, radius: 0.12, score: 1000, approachSpeed: 0.02, breachDamage: 60 },
};

export function createGameState(overrides = {}) {
  return {
    phase: 'playing',
    score: 0,
    combo: 0,
    maxCombo: 0,
    signal: 100,
    timeLeft: 60,
    shots: 0,
    hits: 0,
    drones: [],
    elapsed: 0,
    lastHitAt: null,
    nextDroneId: 1,
    bossSpawned: false,
    chapter: 1,
    nextShotSeq: 1,
    lastShot: null,
    endReason: null,
    ...overrides,
    drones: [...(overrides.drones ?? [])],
  };
}

export function spawnDrone(state, options = {}) {
  if (state.phase !== 'playing') return state;

  const kind = DRONE_TYPES[options.kind] ? options.kind : 'normal';
  const type = DRONE_TYPES[kind];
  const id = state.nextDroneId;
  const fromRight = id % 2 === 0;
  const defaultX = kind === 'boss' ? 0.5 : fromRight ? 0.66 : 0.34;
  const defaultY = kind === 'boss' ? 0.2 : 0.18 + ((id * 0.23) % 0.64);
  const laneX = options.laneX ?? options.x ?? defaultX;

  const drone = {
    id,
    kind,
    x: laneX,
    y: options.y ?? defaultY,
    curve: options.curve ?? (kind === 'boss' ? 0.025 : 0.04),
    curveSpeed: options.curveSpeed ?? (kind === 'boss' ? 1.2 : 2 + (id % 3) * 0.35),
    curvePhase: options.curvePhase ?? id * 0.8,
    radius: options.radius ?? type.radius,
    hp: options.hp ?? type.hp,
    maxHp: options.maxHp ?? options.hp ?? type.hp,
    score: options.score ?? type.score,
    depth: options.depth ?? 1,
    approachSpeed: options.approachSpeed ?? type.approachSpeed,
    laneX,
    breachDamage: options.breachDamage ?? type.breachDamage,
    spawnedAt: state.elapsed,
    baseX: laneX,
  };

  return {
    ...state,
    drones: [...state.drones, drone],
    nextDroneId: id + 1,
    bossSpawned: state.bossSpawned || kind === 'boss',
  };
}

export function fireAt(state, x, y, now = state.elapsed, viewport = { width: 1, height: 1 }) {
  if (state.phase !== 'playing') return state;

  const targetIndex = nearestHitIndex(state.drones, x, y, viewport);
  if (targetIndex === -1) {
    const signal = Math.max(0, state.signal - MISS_SIGNAL_COST);
    return {
      ...state,
      shots: state.shots + 1,
      combo: 0,
      lastHitAt: null,
      signal,
      phase: signal === 0 ? 'defeat' : state.phase,
      endReason: signal === 0 ? 'signal' : state.endReason,
      nextShotSeq: state.nextShotSeq + 1,
      lastShot: {
        seq: state.nextShotSeq,
        hit: false,
        destroyed: false,
        target: null,
      },
    };
  }

  const combo =
    state.lastHitAt !== null && now - state.lastHitAt <= COMBO_WINDOW_SECONDS
      ? Math.min(MAX_COMBO, state.combo + 1)
      : 1;
  const target = state.drones[targetIndex];
  const damaged = { ...target, hp: target.hp - 1, hitAt: now, stunUntil: now + 0.16 };
  const destroyed = damaged.hp <= 0;
  const drones = [...state.drones];

  if (destroyed) drones.splice(targetIndex, 1);
  else drones[targetIndex] = damaged;

  return {
    ...state,
    drones,
    shots: state.shots + 1,
    hits: state.hits + 1,
    combo,
    maxCombo: Math.max(state.maxCombo, combo),
    lastHitAt: now,
    score: state.score + (destroyed ? target.score * combo : 0),
    phase: destroyed && target.kind === 'boss' ? 'victory' : state.phase,
    endReason: destroyed && target.kind === 'boss' ? 'boss' : state.endReason,
    nextShotSeq: state.nextShotSeq + 1,
    lastShot: {
      seq: state.nextShotSeq,
      hit: true,
      destroyed,
      target: { ...target },
      hpBefore: target.hp,
      hpAfter: damaged.hp,
    },
  };
}

export function tickGame(state, deltaSeconds) {
  if (state.phase !== 'playing') return state;

  const delta = Math.max(0, Number.isFinite(deltaSeconds) ? deltaSeconds : 0);
  const elapsed = state.elapsed + delta;
  const timeLeft = Math.max(0, state.timeLeft - delta);
  const chapter = elapsed >= BOSS_SPAWN_SECONDS ? 3 : elapsed >= WAVE_TWO_SECONDS ? 2 : 1;
  const comboExpired =
    state.lastHitAt !== null && elapsed - state.lastHitAt > COMBO_WINDOW_SECONDS;
  const movedDrones = state.drones.map((drone) => moveDrone(drone, elapsed, delta));
  const breachedDrones = movedDrones.filter((drone) => drone.depth <= BREACH_DEPTH);
  const drones = movedDrones.filter((drone) => drone.depth > BREACH_DEPTH);
  const breachDamage = breachedDrones.reduce(
    (total, drone) => total + Math.max(0, finiteOr(drone.breachDamage, 0)),
    0,
  );
  const signal = Math.max(0, state.signal - breachDamage);

  let next = {
    ...state,
    elapsed,
    timeLeft,
    chapter,
    drones,
    signal,
    combo: comboExpired ? 0 : state.combo,
    lastHitAt: comboExpired ? null : state.lastHitAt,
  };

  if (signal === 0) {
    return { ...next, phase: 'defeat', endReason: 'signal' };
  }

  if (timeLeft === 0) {
    return { ...next, phase: 'defeat', endReason: 'timeout' };
  }

  if (!next.bossSpawned && elapsed >= BOSS_SPAWN_SECONDS) {
    next = spawnDrone(next, { kind: 'boss' });
  }

  return next;
}

function nearestHitIndex(drones, x, y, viewport) {
  let match = -1;
  let nearestDistance = Infinity;
  const width = positiveDimension(viewport?.width);
  const height = positiveDimension(viewport?.height);
  const unit = Math.min(width, height);

  for (let index = 0; index < drones.length; index += 1) {
    const drone = drones[index];
    const distance = Math.hypot(
      (x - drone.x) * width,
      (y - drone.y) * height,
    ) / unit;
    const hitRadius = projectRadius(drone.depth, drone.radius);
    if (distance <= hitRadius && distance < nearestDistance) {
      match = index;
      nearestDistance = distance;
    }
  }

  return match;
}

function positiveDimension(value) {
  return Number.isFinite(value) && value > 0 ? value : 1;
}

function moveDrone(drone, elapsed, delta) {
  if (elapsed < (drone.stunUntil ?? -Infinity)) return drone;
  const depth = finiteOr(drone.depth, 1) - Math.max(0, finiteOr(drone.approachSpeed, 0)) * delta;
  if (drone.kind === 'boss') {
    const lostHealth = Math.max(0, (drone.maxHp ?? 8) - drone.hp);
    const amplitude = drone.curve + lostHealth * 0.012;
    return {
      ...drone,
      depth,
      x: boundedSway(drone.laneX, amplitude, elapsed * (1.2 + lostHealth * 0.12)),
    };
  }
  return {
    ...drone,
    depth,
    x: boundedSway(
      drone.laneX,
      Math.max(0, finiteOr(drone.curve, 0)),
      elapsed * drone.curveSpeed + drone.curvePhase,
    ),
  };
}

function boundedSway(laneX, amplitude, phase) {
  const lane = finiteOr(laneX, 0.5);
  return Math.max(0, Math.min(1, lane + Math.sin(phase) * amplitude));
}

function finiteOr(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}
