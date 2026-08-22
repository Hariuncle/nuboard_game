# Blossom Breach — Unity light-gun defense

Unity 6000.4.9f1 URP project for a stationary arcade light-gun FPS. The gun is
treated as a BLE HID mouse: pointer movement aims and left click fires. No
keyboard input, movement keys, or reload keys are required.

## Game loop

- Shoot the large title and mission targets to start.
- Defend the Purity Core through three 20-second acts.
- At most four enemies are active at once for low-end hardware.
- Weak-point hits stagger enemies; five consecutive hits grant three overdrive
  shots. Bombers can chain-explode nearby enemies.
- The final boss alternates between a protected state and an exposed core.
- Every screen, including restart, is usable with aim and trigger only.

## Build

1. Activate a Unity Personal license in Unity Hub.
2. Open `BlossomBreach` with Unity 6000.4.9f1.
3. Run `Blossom Breach > Build Windows` or call the editor method
   `BuildBlossom.BuildWindows` in batch mode.

The generated player is written to `Builds/Windows/BlossomBreach.exe`.

An optional intro is loaded from
`Assets/StreamingAssets/h3-meadow-intro.mp4`. The game remains playable when
the video is absent.
