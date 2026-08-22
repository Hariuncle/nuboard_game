# WebGL 3D Approaching Enemies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace 2D sheet-crop enemies with lit WebGL mesh characters that advance toward the player along the depth axis.

**Architecture:** A local Three.js renderer owns pooled 3D actor groups on a canvas beneath the existing transparent effects/reticle canvas. A dependency-free projection module is shared by the renderer and game engine so visible size and hit detection stay aligned.

**Tech Stack:** JavaScript ES modules, Three.js 0.180.0, Canvas 2D overlay, Node test runner.

---

### Task 1: Depth projection and approach engine

**Files:**
- Create: `game/src/projection.mjs`
- Modify: `game/src/engine.mjs`
- Modify: `game/tests/engine.test.mjs`
- Create: `game/tests/projection.test.mjs`

- [ ] **Step 1: Write failing projection and engine tests**

Add tests that assert `projectRadius(0.2, baseRadius) > projectRadius(0.8, baseRadius)`, a spawned enemy keeps its lane while `depth` decreases, and crossing `BREACH_DEPTH` removes the enemy and reduces `signal`.

- [ ] **Step 2: Run the focused tests and observe failure**

Run `node --test game/tests/projection.test.mjs game/tests/engine.test.mjs` and expect missing projection/depth behavior failures.

- [ ] **Step 3: Implement projection and depth movement**

Export fixed camera constants plus `projectRadius(depth, radius)` and `projectWorldPosition(entity)`. Give spawned entities `depth`, `approachSpeed`, `laneX`, and `breachDamage`; update only depth plus a small bounded sway, and apply breach damage on arrival.

- [ ] **Step 4: Run focused tests**

Run `node --test game/tests/projection.test.mjs game/tests/engine.test.mjs` and expect all focused tests to pass.

### Task 2: Local Three.js renderer and procedural actors

**Files:**
- Create: `game/vendor/three.module.min.js`
- Create: `game/src/scene3d.mjs`
- Create: `game/tests/scene3d-contract.test.mjs`

- [ ] **Step 1: Vendor the pinned renderer**

Use the npm package `three@0.180.0` and copy `build/three.module.min.js` into `game/vendor/three.module.min.js`; record the version and upstream license beside the file.

- [ ] **Step 2: Write a failing scene contract test**

Assert that `scene3d.mjs` imports the local vendor module and exports `createScene3D`, and that no character sheet or `TextureLoader` is referenced.

- [ ] **Step 3: Implement `createScene3D`**

Create a transparent antialiased renderer, perspective camera, fog, hemisphere/key/rim lights, contact-shadow discs, shared geometry/materials, and pooled normal/armored/boss actor groups. Expose `resize(width,height,dpr)`, `sync(entities, defeatedActors, elapsed)`, `render()`, and `dispose()`.

- [ ] **Step 4: Run the scene contract test**

Run `node --test game/tests/scene3d-contract.test.mjs` and expect it to pass.

### Task 3: Layer the WebGL scene into gameplay

**Files:**
- Modify: `game/index.html`
- Modify: `game/styles.css`
- Modify: `game/src/game.mjs`
- Replace: `game/tests/sprite-crop.test.mjs`

- [ ] **Step 1: Replace the crop test with a failing WebGL integration contract**

Assert that the HTML contains `#webgl-canvas`, `game.mjs` imports `createScene3D`, and actor rendering no longer references `spriteSheets`, `meadowSprites`, or `context.drawImage(image, sx, sy, sw, sh`.

- [ ] **Step 2: Add layered canvases**

Place `#webgl-canvas` before `#game-canvas`; make both full-bleed, set WebGL below the transparent effects canvas, and keep HUD/result controls above both.

- [ ] **Step 3: Connect state to the renderer**

Initialize the scene without blocking title controls, resize it with the 2D canvas, call `sync`/`render` each frame, remove sheet loading and `drawDrone`, and keep `drawImpactEffect` plus `drawCrosshair` unchanged.

- [ ] **Step 4: Run integration and full tests**

Run `node --test game/tests/*.test.mjs tests/*.test.mjs` and expect zero failures, then run `node --check` for every `game/src/*.mjs` module.

### Task 4: H3 intro integration and browser verification

**Files:**
- Modify: `game/index.html`
- Add: `game/assets/video/h3-meadow-intro.mp4`
- Add: `game/assets/video/h3/01-bombardment.mp4`
- Add: `game/assets/video/h3/02-rally.mp4`
- Add: `game/assets/video/h3/03-first-person.mp4`

- [ ] **Step 1: Point the intro element at the real H3 output**

Set the video source to `./assets/video/h3-meadow-intro.mp4` and keep the existing poster/error fallback.

- [ ] **Step 2: Verify the browser flow**

Serve `game` on port 4173, verify title -> moving H3 intro -> gameplay, confirm enemies grow while approaching, confirm the reticle moves independently of the fixed background, fire at an enemy, and confirm the 3D fall plus flower effect.

- [ ] **Step 3: Run final verification**

Run the full Node tests, syntax checks, `git diff --check`, and an HTTP 200 probe for `http://127.0.0.1:4173/`.
