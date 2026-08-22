# NEON BREACH Aim, Audio, and Story Intro Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the arena fixed while the reticle moves across the full screen, add responsive synthesized effects, and replace the single-shot H3 intro with a three-act 1344×768 story sequence.

**Architecture:** Three independent workstreams run concurrently: a pure aim geometry module plus renderer integration, a self-contained Web Audio sound engine, and an H3 media pipeline that produces three shots and a final stitched MP4. `game.mjs` remains the orchestration boundary and is integrated only after the independently tested modules exist.

**Tech Stack:** Browser Canvas, Pointer Lock and Pointer Events, Web Audio API, Node `node:test`, KT AI Nexus H3 ComfyUI, official `comfy-mcp`, FFmpeg/PyAV.

---

### Task 1: Pure moving-reticle geometry

**Files:**
- Create: `game/src/aim.mjs`
- Create: `game/tests/aim.test.mjs`
- Modify: `game/src/engine.mjs`
- Modify: `game/tests/engine.test.mjs`

- [ ] **Step 1: Write failing aim tests**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { moveAimByDelta, aimFromClientPoint, toScreenPoint } from '../src/aim.mjs';

test('relative movement moves the reticle while preserving a visible margin', () => {
  assert.deepEqual(
    moveAimByDelta({ x: .5, y: .5 }, 2000, -2000, { width: 1000, height: 500 }, 28),
    { x: .972, y: .056 },
  );
});

test('visible cursor coordinates map directly to aim coordinates', () => {
  assert.deepEqual(
    aimFromClientPoint(250, 150, { left: 50, top: 50, width: 400, height: 200 }, 0),
    { x: .5, y: .5 },
  );
});

test('normalized entity coordinates map to a fixed screen point', () => {
  assert.deepEqual(toScreenPoint({ x: .25, y: .75 }, 800, 600), { x: 200, y: 450 });
});
```

- [ ] **Step 2: Run tests and verify RED**

Run: `node --test game/tests/aim.test.mjs`

Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `aim.mjs`.

- [ ] **Step 3: Implement the aim module**

```js
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

export function moveAimByDelta(aim, dx, dy, viewport, margin = 28) {
  const b = bounds(viewport.width, viewport.height, margin);
  return {
    x: clamp(aim.x + dx / Math.max(1, viewport.width), b.minX, b.maxX),
    y: clamp(aim.y + dy / Math.max(1, viewport.height), b.minY, b.maxY),
  };
}

export function aimFromClientPoint(clientX, clientY, rect, margin = 28) {
  const b = bounds(rect.width, rect.height, margin);
  return {
    x: clamp((clientX - rect.left) / Math.max(1, rect.width), b.minX, b.maxX),
    y: clamp((clientY - rect.top) / Math.max(1, rect.height), b.minY, b.maxY),
  };
}

export function toScreenPoint(point, width, height) {
  return { x: point.x * width, y: point.y * height };
}
```

- [ ] **Step 4: Change the engine boundary test to fixed-screen behavior**

Replace the old wrapped-boundary expectation with:

```js
test('fireAt does not hit a target rendered on the opposite screen edge', () => {
  let state = spawnDrone(createGameState(), { x: .99, y: .5, radius: .055 });
  const result = fireAt(state, .01, .5, .1, { width: 1920, height: 1080 });
  assert.equal(result.hits, 0);
  assert.equal(result.signal, 90);
});
```

Run: `node --test game/tests/engine.test.mjs`

Expected: FAIL because the current engine wraps the horizontal distance.

- [ ] **Step 5: Remove wrapping from hit distance**

Use direct deltas in `nearestHitIndex`:

```js
const distance = Math.hypot(
  (x - drone.x) * width,
  (y - drone.y) * height,
) / unit;
```

Delete the unused engine `wrappedDelta` helper.

- [ ] **Step 6: Verify GREEN**

Run: `node --test game/tests/aim.test.mjs game/tests/engine.test.mjs`

Expected: all aim and engine tests PASS.

### Task 2: Fixed arena and moving reticle integration

**Files:**
- Modify: `game/src/game.mjs`
- Modify: `game/index.html`

- [ ] **Step 1: Import the aim functions**

```js
import { aimFromClientPoint, moveAimByDelta, toScreenPoint } from './aim.mjs';
```

- [ ] **Step 2: Render entities without camera offset and move the crosshair**

```js
for (const entity of entities()) {
  const point = toScreenPoint(entity, width, height);
  const radius = numberFrom(entity, ['radius'], entity.kind === 'boss' ? .12 : .055) * Math.min(width, height);
  drawDrone(point.x, point.y, radius, entity, entity.kind === 'boss');
}
const reticle = toScreenPoint(crosshair, width, height);
drawCrosshair(reticle.x, reticle.y);
```

Delete the renderer `wrappedDelta` helper.

- [ ] **Step 3: Route relative and visible-cursor movement through aim.mjs**

```js
function moveAim(deltaX, deltaY) {
  Object.assign(crosshair, moveAimByDelta(crosshair, deltaX, deltaY, {
    width: canvas.clientWidth,
    height: canvas.clientHeight,
  }));
}
```

For unlocked mouse movement:

```js
Object.assign(crosshair, aimFromClientPoint(
  event.clientX,
  event.clientY,
  canvas.getBoundingClientRect(),
));
```

- [ ] **Step 4: Update on-screen guidance**

Change the desktop hint to `총을 움직여 조준점 이동 · 방아쇠로 발사` and keep the mobile hint `DRAG TO AIM // TAP TO FIRE`.

- [ ] **Step 5: Verify integration**

Run: `node --check game/src/game.mjs`

Expected: no output and exit 0.

### Task 3: Synthesized Web Audio effects

**Files:**
- Create: `game/src/sfx.mjs`
- Create: `game/tests/sfx.test.mjs`
- Modify: `game/src/game.mjs`

- [ ] **Step 1: Write failing cue-definition tests**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { cueDefinition } from '../src/sfx.mjs';

test('every gameplay event has a bounded sound cue', () => {
  for (const name of ['shot', 'hit', 'miss', 'boss', 'victory', 'defeat']) {
    const cue = cueDefinition(name);
    assert.ok(cue.length > 0);
    assert.ok(cue.every((note) => note.duration > 0 && note.duration <= .45));
  }
});

test('unknown events stay silent', () => {
  assert.deepEqual(cueDefinition('unknown'), []);
});
```

- [ ] **Step 2: Run tests and verify RED**

Run: `node --test game/tests/sfx.test.mjs`

Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `sfx.mjs`.

- [ ] **Step 3: Implement cue definitions and safe player**

Define short oscillator/noise sequences for all six names, export `cueDefinition(name)`, and export `createSfx(AudioContextClass = globalThis.AudioContext || globalThis.webkitAudioContext)`. `unlock()` creates/resumes the context. `play(name)` returns without throwing when no context exists and schedules gain envelopes whose stop time never exceeds the cue duration.

- [ ] **Step 4: Verify cue tests**

Run: `node --test game/tests/sfx.test.mjs`

Expected: 2 tests PASS.

- [ ] **Step 5: Integrate event sounds**

In `game.mjs`, instantiate once:

```js
const sfx = createSfx();
```

Call `sfx.unlock()` in start, skip, replay, and canvas pointerdown handlers. In `shoot()`, play `shot`, compare `state.hits` before/after, then play `hit` or `miss`. In `frame()`, compare `bossSpawned` before/after `tickGame` and play `boss` on the rising edge. In `finishGame()`, play `victory` or `defeat` once.

### Task 4: Three H3 reference frames

**Files:**
- Create: `game/assets/images/intro-alert.png`
- Create: `game/assets/images/intro-rin-comms.png`
- Create: `game/assets/images/intro-breach.png`

- [ ] **Step 1: Generate three consistent 16:9 references**

Use ImageGen built-in mode at 1344×768 composition:

1. Alarm-lit neon training arena with Overseer drones waking, red/cyan light, no text.
2. RIN chest-up holographic communication portrait, stable silver-blue hair, magenta visor, navy-white armor, no text.
3. RIN raises the compact light gun toward camera; a cyan circular visor transition begins, no text.

- [ ] **Step 2: Inspect all three frames**

Use `view_image` and reject frames with text, extra fingers, mismatched weapon, or a changed face/armor identity.

### Task 5: Queue and collect three H3 shots

**Files:**
- Remote create: `/home/work/media-lab-data/minimax-h3/runs/neon-breach-story/shot-1.json`
- Remote create: `/home/work/media-lab-data/minimax-h3/runs/neon-breach-story/shot-2.json`
- Remote create: `/home/work/media-lab-data/minimax-h3/runs/neon-breach-story/shot-3.json`
- Local create: `game/assets/video/intro-shot-1.mp4`
- Local create: `game/assets/video/intro-shot-2.mp4`
- Local create: `game/assets/video/intro-shot-3.mp4`

- [ ] **Step 1: Transfer references and prepare motion guides**

Place all references in the H3 ComfyUI input directory and create matching 1344×768, 24fps, 56-frame motion guide videos with subtle zoom/pan.

- [ ] **Step 2: Build and validate workflows**

Clone the proven `minimax-h3-r2v-long-segment-guided-api.json`. Set each reference image/video, 1344×768, length 48, steps 8, fps 24, distinct seeds, and scene-specific prompts. Call `validate_workflow` with `workflow_path`; require `valid: true` and zero errors.

- [ ] **Step 3: Queue all shots through official MCP**

Call `run_workflow` with `wait: false` for each validated workflow, retain all prompt IDs, and poll `job {action: status}` until each reports `completed`. Fetch each with `fetch_outputs {prompt_id, out_dir}`.

- [ ] **Step 4: Decode each result**

Use PyAV to assert H.264, 1344×768, 24fps, and at least 48 decoded frames for every shot.

### Task 6: Stitch, caption, and integrate the story intro

**Files:**
- Create: `game/assets/video/h3-rin-story-intro.mp4`
- Modify: `game/index.html`
- Modify: `README.md`

- [ ] **Step 1: Compose the final MP4**

Use FFmpeg to concatenate the three shots, apply short crossfades, and burn these captions in order:

- `훈련망 침입 감지`
- `오버시어가 훈련망을 장악했다`
- `조준 링크 연결 — BREACH`

Use H.264 yuv420p at 1344×768 and 24fps. Keep total duration between 6.5 and 7.5 seconds.

- [ ] **Step 2: Switch the game video source**

Set `#intro-video` to `./assets/video/h3-rin-story-intro.mp4?v=story` while retaining the RIN poster and failure fallback.

- [ ] **Step 3: Decode final output**

Run PyAV and assert width 1344, height 768, fps 24, duration 6.5–7.5 seconds, and successful full-frame decoding.

### Task 7: Final verification

**Files:**
- Modify only if verification exposes a regression.

- [ ] **Step 1: Run all tests**

Run: `node --test game/tests/*.test.mjs`

Expected: all tests PASS with zero failures.

- [ ] **Step 2: Check syntax and keyboard absence**

Run `node --check` for every `game/src/*.mjs` and search for `keydown`, `keyup`, `keypress`, `BLEKeyboard`, and `Keyboard.h`.

Expected: syntax exit 0 and no gameplay keyboard bindings.

- [ ] **Step 3: Browser verification**

At desktop and 390×844 mobile sizes verify: fixed background, reticle reaches every edge, mouse/BLE click firing, touch drag/tap, all state transitions, story video dimensions and automatic transition, no horizontal overflow, and no console warnings/errors.

- [ ] **Step 4: Hardware status disclosure**

Keep the existing README statement that NU-40 FQBN/pin mapping and physical pairing remain unverified until the real board toolchain and device are available.
