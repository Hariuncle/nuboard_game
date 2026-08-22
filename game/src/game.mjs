import { createGameState, spawnDrone, fireAt, tickGame } from "./engine.mjs";
import { normalizeControllerEvent } from "./controller.mjs";
import { createSfx } from "./sfx.mjs";
import { advanceImpactEffects, createImpactEffect } from "./effects.mjs";
import {
  advanceTouchGesture,
  aimFromClientPoint,
  moveAimByDelta,
  toScreenPoint,
} from "./aim.mjs";

const $ = (selector) => document.querySelector(selector);
const screens = {
  title: $("#title-screen"),
  cutscene: $("#cutscene-screen"),
  game: $("#game-screen"),
};

const canvas = $("#game-canvas");
const context = canvas.getContext("2d", { alpha: true });
const video = $("#intro-video");
const sfx = createSfx();
const spriteSheets = Object.fromEntries(
  Object.entries({
    raiders: "./assets/images/raiders-sheet.png",
    minstrel: "./assets/images/minstrel-sheet.png",
    support: "./assets/images/support-sheet.png",
    defenders: "./assets/images/defenders-sheet.png",
  }).map(([key, src]) => {
    const image = new Image();
    image.src = src;
    return [key, image];
  }),
);

const meadowSprites = [
  { sheet: "raiders", source: [0, 45, 520, 625] },
  { sheet: "raiders", source: [0, 760, 520, 630] },
  { sheet: "minstrel", source: [0, 45, 535, 610] },
  { sheet: "minstrel", source: [0, 760, 535, 620] },
  { sheet: "support", source: [0, 30, 520, 640] },
  { sheet: "support", source: [0, 750, 520, 640] },
  { sheet: "defenders", source: [0, 40, 520, 640] },
  { sheet: "defenders", source: [0, 745, 520, 645] },
];

const ui = {
  score: $("#score-value"),
  combo: $("#combo-value"),
  signal: $("#signal-fill"),
  signalTrack: $(".signal-track"),
  time: $("#time-value"),
  bossHud: $("#boss-hud"),
  bossFill: $("#boss-fill"),
  callout: $("#game-callout"),
  results: $("#result-overlay"),
  resultKicker: $("#result-kicker"),
  resultTitle: $("#result-title"),
  resultCopy: $("#result-copy"),
  resultScore: $("#result-score"),
  resultAccuracy: $("#result-accuracy"),
  resultCombo: $("#result-combo"),
};

let state = null;
let mode = "title";
let animationFrame = 0;
let previousTime = 0;
let spawnClock = 0;
let calloutTimer = 0;
let impactEffects = [];
let hitFlashTimer = 0;
let lastControllerFireAt = -Infinity;
const crosshair = { x: 0.5, y: 0.5, recoil: 0 };
const touchAim = { id: null, x: 0, y: 0, distance: 0 };

function numberFrom(object, keys, fallback = 0) {
  for (const key of keys) {
    const value = object?.[key];
    if (typeof value === "number" && Number.isFinite(value)) return value;
  }
  return fallback;
}

function listFrom(object, keys) {
  for (const key of keys) if (Array.isArray(object?.[key])) return object[key];
  return [];
}

function nextState(result) {
  return result && typeof result === "object" ? result : state;
}

function showScreen(name) {
  mode = name;
  Object.entries(screens).forEach(([key, element]) => {
    element.hidden = key !== name;
    element.classList.toggle("is-active", key === name);
  });
}

function beginCutscene() {
  void sfx.unlock();
  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches || video.error) {
    startGame();
    return;
  }
  showScreen("cutscene");
  video.currentTime = 0;
  const playback = video.play();
  if (playback?.catch) {
    playback.catch(() => {
      $("#video-fallback").hidden = false;
      window.setTimeout(startGame, 900);
    });
  }
}

function startGame() {
  void sfx.unlock();
  if (!video.paused) video.pause();
  showScreen("game");
  screens.game.inert = false;
  ui.results.hidden = true;
  state = createGameState();
  spawnClock = 0;
  previousTime = performance.now();
  crosshair.x = 0.5;
  crosshair.y = 0.5;
  impactEffects = [];
  lastControllerFireAt = -Infinity;
  resizeCanvas();
  announce("LINK START");
  cancelAnimationFrame(animationFrame);
  animationFrame = requestAnimationFrame(frame);
}

function finishGame() {
  if (mode !== "game") return;
  mode = "result";
  cancelAnimationFrame(animationFrame);
  document.exitPointerLock?.();
  screens.game.inert = true;
  const won = state?.phase === "victory";
  sfx.play(won ? "victory" : "defeat");
  ui.resultKicker.textContent = won ? "MEADOW RESTORED" : "WARD EXPIRED";
  ui.resultTitle.textContent = won ? "BLOSSOM SAVED" : "TRY AGAIN";
  ui.resultCopy.textContent = won
    ? "악몽 포자를 모두 정화했습니다. 포모라의 왕국이 다시 피어납니다."
    : "수호 결계가 약해졌습니다. 블래스터를 재보정하고 다시 도전하세요.";
  ui.resultScore.textContent = Math.round(numberFrom(state, ["score"], 0)).toLocaleString("ko-KR");
  ui.resultAccuracy.textContent = `${state.shots ? Math.round((state.hits / state.shots) * 100) : 0}%`;
  ui.resultCombo.textContent = `×${state.maxCombo || 0}`;
  ui.results.hidden = false;
  $("#replay-button").focus();
}

function resizeCanvas() {
  const ratio = Math.min(devicePixelRatio || 1, 2);
  const rect = canvas.getBoundingClientRect();
  canvas.width = Math.max(1, Math.round(rect.width * ratio));
  canvas.height = Math.max(1, Math.round(rect.height * ratio));
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
}

function frame(now) {
  if (mode !== "game" || !state) return;
  const elapsed = Math.min((now - previousTime) / 1000, 0.05);
  previousTime = now;
  spawnClock -= elapsed;

  const bossWasSpawned = Boolean(state.bossSpawned);
  state = nextState(tickGame(state, elapsed));
  impactEffects = advanceImpactEffects(impactEffects, elapsed);
  if (!bossWasSpawned && state.bossSpawned) sfx.play("boss");
  if (spawnClock <= 0) {
    state = nextState(spawnDrone(state));
    const remaining = numberFrom(state, ["timeRemaining", "timeLeft", "remaining"], 60);
    spawnClock = Math.max(0.45, 1.15 - (60 - remaining) * 0.008);
  }

  render();
  updateHud();
  crosshair.recoil = Math.max(0, crosshair.recoil - elapsed * 6);
  calloutTimer = Math.max(0, calloutTimer - elapsed);

  if (isRunFinished()) {
    finishGame();
    return;
  }
  animationFrame = requestAnimationFrame(frame);
}

function entities() {
  return listFrom(state, ["drones", "targets", "entities"]);
}

function isRunFinished() {
  return state?.phase === "victory" || state?.phase === "defeat";
}

function updateHud() {
  const score = numberFrom(state, ["score"], 0);
  const combo = Math.max(1, numberFrom(state, ["combo", "multiplier"], 1));
  const signal = Math.max(0, Math.min(100, numberFrom(state, ["signal", "signalMeter"], 100)));
  const time = Math.max(0, numberFrom(state, ["timeRemaining", "timeLeft", "remaining"], 60));
  ui.score.textContent = Math.round(score).toString().padStart(6, "0");
  ui.combo.textContent = `×${combo}`;
  ui.signal.style.width = `${signal}%`;
  ui.signalTrack.classList.toggle("is-low", signal <= 25);
  ui.signalTrack.setAttribute("aria-valuenow", String(Math.round(signal)));
  ui.time.textContent = time.toFixed(1);

  const boss = entities().find((entity) => entity.kind === "boss");
  const bossHp = numberFrom(boss, ["hp", "health"], 0);
  const bossMax = Math.max(1, numberFrom(boss, ["maxHp", "maxHealth"], bossHp));
  ui.bossHud.hidden = !boss || bossHp <= 0;
  if (boss) {
    const bossPercent = Math.max(0, Math.min(100, (bossHp / bossMax) * 100));
    ui.bossFill.style.width = `${bossPercent}%`;
    ui.bossFill.parentElement.setAttribute("aria-valuenow", String(Math.round(bossPercent)));
  }
}

function render() {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  context.clearRect(0, 0, width, height);

  for (const entity of entities()) {
    const point = toScreenPoint(entity, width, height);
    const isBoss = entity.kind === "boss";
    const radius = numberFrom(entity, ["radius"], isBoss ? .12 : .055) * Math.min(width, height);
    drawDrone(point.x, point.y, radius, entity, isBoss);
  }
  for (const effect of impactEffects) drawImpactEffect(effect, width, height);
  const reticle = toScreenPoint(crosshair, width, height);
  drawCrosshair(reticle.x, reticle.y);
}

function drawImpactEffect(effect, width, height) {
  const originX = effect.x * width;
  const originY = effect.y * height;
  const scale = Math.min(width, height);
  const alpha = Math.max(0, Math.min(1, effect.life));
  context.save();
  context.globalAlpha = alpha;

  const ringRadius = (effect.hit ? 22 : 12) + effect.age * (effect.hit ? 105 : 45);
  context.strokeStyle = effect.hit ? "#fff3a8" : "rgba(255,255,255,.55)";
  context.lineWidth = effect.hit ? 4 : 2;
  context.shadowColor = effect.hit ? "#ff719f" : "transparent";
  context.shadowBlur = effect.hit ? 18 : 0;
  context.beginPath();
  context.arc(originX, originY, ringRadius, 0, Math.PI * 2);
  context.stroke();

  for (const particle of effect.particles) {
    const x = originX + particle.x * scale;
    const y = originY + particle.y * scale;
    context.save();
    context.translate(x, y);
    context.rotate(particle.rotation);
    if (particle.kind === "petal") {
      context.fillStyle = "#ff8caf";
      context.beginPath();
      context.ellipse(0, 0, particle.size * .55, particle.size, 0, 0, Math.PI * 2);
      context.fill();
    } else {
      context.fillStyle = effect.hit ? "#ffe789" : "rgba(255,255,255,.65)";
      context.beginPath();
      context.arc(0, 0, particle.size * .45, 0, Math.PI * 2);
      context.fill();
    }
    context.restore();
  }

  if (effect.hit) {
    const marker = 11 + effect.age * 8;
    context.strokeStyle = "#ffffff";
    context.lineWidth = 3;
    context.beginPath();
    context.moveTo(originX - marker, originY - marker); context.lineTo(originX - 3, originY - 3);
    context.moveTo(originX + marker, originY - marker); context.lineTo(originX + 3, originY - 3);
    context.moveTo(originX - marker, originY + marker); context.lineTo(originX - 3, originY + 3);
    context.moveTo(originX + marker, originY + marker); context.lineTo(originX + 3, originY + 3);
    context.stroke();
  }
  context.restore();
}

function drawDrone(x, y, radius, drone, isBoss) {
  context.save();
  context.translate(x, y);
  const rawId = String(drone.id ?? drone.spawnedAt ?? `${x}:${y}`);
  const hash = [...rawId].reduce((total, character) => total + character.charCodeAt(0), 0);
  const sprite = meadowSprites[isBoss ? 0 : hash % meadowSprites.length];
  const image = spriteSheets[sprite.sheet];
  if (image.complete && image.naturalWidth) {
    const size = radius * (isBoss ? 2.65 : 2.35);
    const [sx, sy, sw, sh] = sprite.source;
    context.shadowColor = isBoss ? "#ff719f" : "#fff2bd";
    context.shadowBlur = isBoss ? 30 : 15;
    context.beginPath();
    context.arc(0, 0, size * .48, 0, Math.PI * 2);
    context.fillStyle = isBoss ? "rgba(113,36,61,.35)" : "rgba(255,250,225,.2)";
    context.fill();
    context.globalCompositeOperation = "multiply";
    context.drawImage(image, sx, sy, sw, sh, -size / 2, -size / 2, size, size);
    context.globalCompositeOperation = "source-over";
  } else {
    context.rotate(numberFrom(drone, ["rotation"], performance.now() * 0.0005));
    context.shadowColor = isBoss ? "#ff719f" : "#ffe98a";
    context.shadowBlur = isBoss ? 28 : 17;
    context.fillStyle = isBoss ? "rgba(255,63,210,.2)" : "rgba(57,244,255,.15)";
    context.strokeStyle = isBoss ? "#ff719f" : "#ffe98a";
    context.lineWidth = isBoss ? 4 : 2;
    context.beginPath();
    for (let point = 0; point < 8; point += 1) {
      const angle = (Math.PI * 2 * point) / 8;
      const distance = point % 2 ? radius * 0.72 : radius;
      const px = Math.cos(angle) * distance;
      const py = Math.sin(angle) * distance;
      point ? context.lineTo(px, py) : context.moveTo(px, py);
    }
    context.closePath();
    context.fill();
    context.stroke();
    context.fillStyle = isBoss ? "#ff719f" : "#fff7d6";
    context.beginPath();
    context.arc(0, 0, radius * 0.24, 0, Math.PI * 2);
    context.fill();
  }
  context.restore();

  const hp = numberFrom(drone, ["hp", "health"], 1);
  const maxHp = Math.max(1, numberFrom(drone, ["maxHp", "maxHealth"], hp));
  if (maxHp > 1 && !isBoss) {
    context.fillStyle = "rgba(0,0,0,.6)";
    context.fillRect(x - radius, y + radius + 9, radius * 2, 4);
    context.fillStyle = "#ff3fd2";
    context.fillRect(x - radius, y + radius + 9, radius * 2 * (hp / maxHp), 4);
  }
}

function drawCrosshair(x, y) {
  const radius = 18 + crosshair.recoil * 11;
  context.save();
  context.translate(x, y);
  context.strokeStyle = "#f4feff";
  context.shadowColor = "#ffcf67";
  context.shadowBlur = 12;
  context.lineWidth = 1.5;
  context.beginPath();
  context.arc(0, 0, radius, 0, Math.PI * 2);
  context.moveTo(-radius - 12, 0); context.lineTo(-radius + 3, 0);
  context.moveTo(radius - 3, 0); context.lineTo(radius + 12, 0);
  context.moveTo(0, -radius - 12); context.lineTo(0, -radius + 3);
  context.moveTo(0, radius - 3); context.lineTo(0, radius + 12);
  context.stroke();
  context.fillStyle = "#ff719f";
  context.fillRect(-1.5, -1.5, 3, 3);
  context.restore();
}

function shoot() {
  if (mode !== "game" || !state) return;
  sfx.play("shot");
  const previousHits = state.hits;
  const result = fireAt(state, crosshair.x, crosshair.y, state.elapsed, {
    width: canvas.clientWidth,
    height: canvas.clientHeight,
  });
  state = nextState(result);
  crosshair.recoil = 1;
  const didHit = state.hits > previousHits;
  impactEffects.push(createImpactEffect({ x: crosshair.x, y: crosshair.y, hit: didHit }));
  if (impactEffects.length > 8) impactEffects.splice(0, impactEffects.length - 8);
  if (didHit) {
    sfx.play("hit");
    announce("CLEANSE HIT");
    screens.game.classList.remove("impact-flash");
    void screens.game.offsetWidth;
    screens.game.classList.add("impact-flash");
    window.clearTimeout(hitFlashTimer);
    hitFlashTimer = window.setTimeout(() => screens.game.classList.remove("impact-flash"), 180);
    navigator.vibrate?.(22);
  } else {
    sfx.play("miss");
  }
}

function moveAim(deltaX, deltaY) {
  Object.assign(crosshair, moveAimByDelta(crosshair, deltaX, deltaY, {
    width: canvas.clientWidth,
    height: canvas.clientHeight,
  }));
}

function requestPointerLock() {
  if (mode !== "game" || document.pointerLockElement === canvas) return;
  try {
    const result = canvas.requestPointerLock?.({ unadjustedMovement: true });
    result?.catch?.(() => {
      try {
        const fallback = canvas.requestPointerLock?.();
        fallback?.catch?.(() => {});
      } catch { /* Pointer Lock is optional. */ }
    });
  } catch {
    try {
      const fallback = canvas.requestPointerLock?.();
      fallback?.catch?.(() => {});
    } catch { /* Pointer Lock is optional. */ }
  }
}

function announce(message) {
  ui.callout.textContent = message;
  ui.callout.classList.remove("show");
  void ui.callout.offsetWidth;
  ui.callout.classList.add("show");
  calloutTimer = 1;
}

export function dispatchControllerEvent(rawEvent) {
  const event = normalizeControllerEvent(rawEvent);
  if (!event) return false;
  if (event.type === "aim") {
    const rect = canvas.getBoundingClientRect();
    Object.assign(crosshair, aimFromClientPoint(
      rect.left + event.x * rect.width,
      rect.top + event.y * rect.height,
      rect,
    ));
    return true;
  }
  if (event.type === "fire") {
    const now = performance.now();
    if (now - lastControllerFireAt < 80) return true;
    lastControllerFireAt = now;
    shoot();
    return true;
  }
  return false;
}

window.dispatchControllerEvent = dispatchControllerEvent;

$("#start-button").addEventListener("click", beginCutscene);
$("#skip-button").addEventListener("click", startGame);
$("#replay-button").addEventListener("click", startGame);
video.addEventListener("ended", startGame);
video.addEventListener("error", () => { $("#video-fallback").hidden = false; });

const briefDialog = $("#brief-dialog");
$("#brief-button").addEventListener("click", () => briefDialog.showModal());

canvas.addEventListener("pointerdown", (event) => {
  event.preventDefault();
  void sfx.unlock();
  if (event.pointerType === "touch") {
    const result = advanceTouchGesture(touchAim, {
      type: "start",
      pointerId: event.pointerId,
      clientX: event.clientX,
      clientY: event.clientY,
    });
    if (result.gesture === touchAim) return;
    Object.assign(touchAim, result.gesture);
    canvas.setPointerCapture(event.pointerId);
    return;
  }
  if (event.button === 0) {
    requestPointerLock();
    shoot();
  }
});

canvas.addEventListener("pointermove", (event) => {
  if (event.pointerType !== "touch") return;
  const result = advanceTouchGesture(touchAim, {
    type: "move",
    pointerId: event.pointerId,
    clientX: event.clientX,
    clientY: event.clientY,
  });
  if (result.gesture === touchAim) return;
  Object.assign(touchAim, result.gesture);
  moveAim(result.deltaX, result.deltaY);
});

canvas.addEventListener("pointerup", (event) => {
  if (event.pointerType !== "touch") return;
  const result = advanceTouchGesture(touchAim, {
    type: "finish",
    pointerId: event.pointerId,
  });
  if (result.gesture === touchAim) return;
  Object.assign(touchAim, result.gesture);
  if (result.shouldFire) shoot();
});

function cancelTouchGesture(event) {
  if (event.pointerType !== "touch") return;
  const result = advanceTouchGesture(touchAim, {
    type: "cancel",
    pointerId: event.pointerId,
  });
  if (result.gesture !== touchAim) Object.assign(touchAim, result.gesture);
}

canvas.addEventListener("pointercancel", cancelTouchGesture);
canvas.addEventListener("lostpointercapture", cancelTouchGesture);

document.addEventListener("mousemove", (event) => {
  if (mode !== "game") return;
  if (document.pointerLockElement === canvas) {
    moveAim(event.movementX, event.movementY);
    return;
  }
  if (event.target === canvas) {
    Object.assign(crosshair, aimFromClientPoint(
      event.clientX,
      event.clientY,
      canvas.getBoundingClientRect(),
    ));
  }
});

document.addEventListener("pointerdown", (event) => {
  if (event.button !== 0 || event.target.closest("button, dialog, canvas")) return;
  if (mode === "title") beginCutscene();
  else if (mode === "cutscene") startGame();
  else if (mode === "result") startGame();
});

window.addEventListener("resize", () => {
  if (mode === "game") resizeCanvas();
});

for (const image of document.querySelectorAll("img")) {
  image.addEventListener("error", () => image.closest(".portrait-frame")?.classList.add("image-failed"));
}
