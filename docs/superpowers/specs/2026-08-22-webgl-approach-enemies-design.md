# WebGL 3D Approaching Enemies Design

## Goal

Replace every cropped character-sheet target with a genuine WebGL mesh character. Enemies advance from the distant meadow toward the player rather than crossing the screen sideways, while BLE mouse aiming, firing, story chapters, audio, HUD, hit effects, and the fixed background remain intact.

## Rendering architecture

- Add a WebGL canvas beneath the existing transparent gameplay canvas.
- Vendor Three.js 0.180.0 locally so the game has no runtime CDN dependency.
- Build normal, armored, and boss characters from shared sphere, cone, cylinder, and petal geometry using lit 3D materials. No PNG character sheet is used as a texture.
- Keep the current 2D canvas for the reticle, petals, shockwaves, and hit marker.
- Keep a stable actor pool keyed by entity id; do not create meshes every frame.
- If WebGL initialization fails, show an explicit unsupported-device message and keep the title controls responsive.

## Gameplay and projection

- Each enemy has a normalized `depth`: `1` is the spawn distance and `0` is the player line.
- `tickGame` decreases depth. Horizontal movement is limited to a small evasive sway around a fixed lane; there is no edge-to-edge traversal or wrap.
- A projection module is the single source of truth for apparent screen radius. Both Three.js placement and hit testing consume the same projection constants.
- When a normal or armored enemy reaches the player line it is removed and reduces purity. A boss reaching the line causes a large purity loss.
- Near enemies are visibly larger and easier to hit, but are more urgent because they are closer to breaching.

## 3D animation

- Idle: breathing, ear motion, and shallow lateral evade.
- Hit: short backward recoil and squash.
- Defeat: character rotates backward, drops, fades, and is removed after the existing flower burst has played.
- Boss: larger thorn crown and armor, stronger emissive heart, slower advance, and a second-phase material pulse.

## Integration and tests

- Existing BLE `aim`/`aimDelta`/`fire` events continue to drive the same normalized reticle.
- Existing chapter timings remain: armored enemies at 20 seconds and the boss at 40 seconds.
- Unit tests prove depth decreases, screen radius grows as depth decreases, breach damage occurs, and sideways traversal is removed.
- Contract tests prove the sheet crops and `drawImage` actor path are gone and the WebGL scene module is present.
- Browser verification covers title -> H3 intro -> gameplay, visible 3D lighting, forward approach, hit, defeat, and replay.

## Asset boundary

The workspace contains no GLB, GLTF, FBX, OBJ, Blender, rig, or animation file. The first working version therefore uses procedural stylized 3D meshes. A future Pomora-quality GLB can replace only the character factory without changing BLE input, depth gameplay, projection, or story code.
