# NEON BREACH Game Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a responsive first-person browser shooting game using ImageGen art, an H3/Comfy MCP opening cutscene, and a BLE HID gun-as-mouse input path.

**Architecture:** A pure JavaScript engine owns deterministic gameplay state while a separate browser module owns rendering, input, media, and screens. Static assets are project-local and the app runs from a simple HTTP server without bundling.

**Tech Stack:** HTML5 Canvas, CSS, ES modules, Node `node:test`, ImageGen, H3 ComfyUI through official Comfy MCP.

---

### Task 1: Generate and place visual assets

**Files:**
- Create: `game/assets/images/rin-character.png`
- Create: `game/assets/images/neon-arena.png`
- Create: `game/assets/images/overseer-drone.png`
- Create: `game/assets/images/storyboard.png`

- [ ] Generate each distinct asset with one built-in ImageGen call using a consistent cyan, magenta, navy arcade style.
- [ ] Inspect every result for subject, composition, unwanted text, and project suitability.
- [ ] Copy final images into `game/assets/images/` without overwriting unrelated files.

### Task 2: Render and retrieve the H3 cutscene

**Files:**
- Create: `game/assets/video/h3-rin-intro.mp4`
- Create remotely: `minimax-h3/runs/neon-breach/h3-rin-intro-api.json`

- [ ] Upload `rin-character.png` to the KT mounted folder and copy it into the H3 input directory.
- [ ] Create a 56-frame reference motion video and a short H3 workflow copy at 512×288 with six sampling steps.
- [ ] Call MCP `validate_workflow`; require `valid: true` and zero errors.
- [ ] Call MCP `run_workflow` and poll MCP `job` until `completed`.
- [ ] Call MCP `fetch_outputs`, download the MP4, and save it as `game/assets/video/h3-rin-intro.mp4`.
- [ ] Decode all frames locally and assert H.264, nonzero duration, and 512×288 dimensions.

### Task 3: Implement gameplay rules with TDD

**Files:**
- Create: `game/tests/engine.test.mjs`
- Create: `game/src/engine.mjs`

- [ ] Write failing tests for `createGameState`, `spawnDrone`, `fireAt`, and `tickGame` covering score, combo, armor, signal loss, victory, and timeout.
- [ ] Run `node --test game/tests/engine.test.mjs` and confirm failure because `engine.mjs` is missing.
- [ ] Implement the smallest pure engine API that satisfies the tests.
- [ ] Re-run the tests and require zero failures.
- [ ] Refactor names and constants while keeping the suite green.

### Task 4: Build the playable screen

**Files:**
- Create: `game/index.html`
- Create: `game/styles.css`
- Create: `game/src/game.mjs`

- [ ] Add semantic title, cutscene, game, mission brief, and result layers in `index.html`.
- [ ] Add responsive arcade styling, safe-area spacing, focus states, and reduced-motion rules in `styles.css`.
- [ ] Implement first-person Canvas rendering, center crosshair, Pointer Lock relative movement, left-click firing, touch fallback, the animation loop, HUD updates, cutscene fallback, and replay in `game.mjs`; do not map keyboard gameplay input.
- [ ] Normalize fallback input through `normalizeControllerEvent` while treating the physical BLE gun as a standard HID mouse.

### Task 5: Add the BLE HID gun validation firmware

**Files:**
- Create: `firmware/ble_hid_mouse_controller.ino`
- Create: `firmware/README.md`

- [ ] Reuse the existing MPU-9250 and FSR pin/initialization assumptions from `all_parts_activated.ino` in an isolated sketch.
- [ ] Calibrate a neutral pose, apply dead-zone and smoothing, and convert yaw/pitch change to relative mouse movement.
- [ ] Convert a debounced trigger threshold crossing to BLE HID left-button press/release.
- [ ] Document the required nRF52840 Arduino board core and BLE HID library, pairing steps, sensitivity constants, and the fact that compilation must be verified on the user's actual NU-40 toolchain.

### Task 6: Verify the integrated game

**Files:**
- Verify: `game/index.html`
- Verify: `game/assets/video/h3-rin-intro.mp4`

- [ ] Run `node --test game/tests/engine.test.mjs`; require all tests to pass.
- [ ] Start `python -m http.server 4173 --directory game`.
- [ ] Open `http://127.0.0.1:4173/`, verify title → cutscene → Pointer Lock gameplay → result → replay, and check browser console errors.
- [ ] Check a desktop viewport and a narrow mobile viewport for visible HUD and usable controls.
- [ ] Re-run local media decoding and record codec, dimensions, frame count, duration, and SHA-256.
