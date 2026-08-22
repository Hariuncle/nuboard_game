# NEON BREACH Game Design

## Goal

Build a small, polished browser shooting game that uses ImageGen artwork for the visual identity and an H3/Comfy MCP-rendered video as its opening cutscene. The prototype must run on desktop and mobile without a build step.

## Concept and story

The player sees the arena directly through RIN's first-person visor. RIN links a physical Bluetooth blaster to the NEON BREACH arena, where a rogue control signal has turned the training drones hostile. The player must restore three signal sectors, defeat the Overseer drone, and recover the link core.

The story is delivered in three beats:

1. Link — RIN powers up the blaster and enters the corrupted arena.
2. Breach — waves of target drones cross the playfield while the signal meter falls.
3. Restore — a boss drone appears; destroying it stabilizes the link.

## Chosen approach

Use a standalone HTML/CSS/JavaScript first-person Canvas game. This keeps the prototype fast to load and lets the physical gun use the operating system's standard mouse input path. ImageGen supplies the character portrait, storyboard, arena background, and enemy drone sprite. H3 animates the character artwork into a short opening cutscene that is played before the game.

Alternatives rejected:

- DOM-only rhythm cards: quicker, but visually weak and less game-like.
- Phaser or another engine: stronger for a larger title, but unnecessary for a one-screen prototype.

## Gameplay

- A round lasts 60 seconds.
- The crosshair stays at screen center while Pointer Lock mouse deltas rotate the first-person view.
- The player shoots with the left mouse button; touch drag and tap provide a mobile fallback. Gameplay does not use keyboard controls.
- Drones enter from screen edges and move along curved paths.
- Normal drones award 100 points; armored drones require two hits and award 250.
- Consecutive hits within 1.2 seconds increase a combo multiplier up to 8×.
- Misses reduce the signal meter. A depleted meter ends the run.
- At 40 seconds the Overseer boss appears with a visible health bar.
- The run ends in victory when the boss is destroyed or defeat when time/signal expires.

## Screens and assets

- Title screen: logo, RIN portrait, short story copy, Start button.
- Cutscene overlay: locally hosted H3 MP4 with Skip control and graceful fallback.
- Game screen: full-bleed first-person arena, center crosshair, parallax camera response, Canvas entities, score/combo/time/signal HUD.
- Results overlay: score, accuracy, maximum combo, Replay button.
- Storyboard: a separate three-panel image displayed in the title screen's Mission Brief drawer.

Project assets:

- `game/assets/images/rin-character.png`
- `game/assets/images/neon-arena.png`
- `game/assets/images/overseer-drone.png`
- `game/assets/images/storyboard.png`
- `game/assets/video/h3-rin-intro.mp4`

## Architecture

- `game/src/engine.mjs`: deterministic state transitions, spawning, scoring, combo, damage, and end-state rules. It contains no DOM code and is tested with Node's built-in test runner.
- `game/src/game.mjs`: Canvas rendering, animation loop, pointer/touch/keyboard input, media flow, and screen transitions.
- `game/index.html`: semantic screen structure and media elements.
- `game/styles.css`: responsive presentation and accessibility states.
- `game/tests/engine.test.mjs`: red-green tests for the gameplay rules.

The gun console is paired at the operating-system level as a BLE HID mouse. IMU yaw/pitch deltas become relative mouse movement and the trigger/FSR becomes the left mouse button. The game therefore receives the same `mousemove` and `mousedown` events from a physical gun or an ordinary mouse. A small normalized controller module remains available for touch and future direct Web Bluetooth support, but the first prototype does not invent a private GATT protocol.

The existing `all_parts_activated.ino` has no BLE implementation, so a separate minimal firmware validation sketch is provided. It isolates MPU-9250 aiming, FSR trigger detection, calibration, dead-zone/smoothing, and BLE HID mouse output before those parts are merged back into the full hardware sketch.

## Data flow

Relative mouse movement updates camera yaw/pitch while the center crosshair remains fixed. A left-button event requests a shot at the center of the current view. The game module calls pure engine functions, receives the next state, then renders that state. The animation loop advances entities using elapsed seconds. Media playback is isolated from gameplay: ending, skipping, or failing the video all transition to the same ready state.

## Error handling and accessibility

- If the H3 video cannot autoplay, the title screen remains usable and Start enters the game directly.
- Missing images fall back to CSS gradients and Canvas shapes.
- Reduced-motion users skip the cutscene and nonessential flashes.
- Buttons retain normal accessibility focus, but keyboard events are not mapped to aiming, firing, or movement.
- Pointer Lock failure falls back to visible cursor aiming without blocking play.
- Audio is not required for play.

## Testing and acceptance

- Engine tests cover spawning, hits, combo expiry, armored targets, misses, boss victory, and timeout defeat.
- A static server opens the game with no console errors.
- Desktop and mobile viewport checks confirm that controls and HUD remain visible.
- The H3 video decodes locally and transitions into gameplay.
- The game can complete a full round and replay without reloading.
- Standard mouse movement/click and normalized touch input drive the same first-person aiming and firing boundary.
