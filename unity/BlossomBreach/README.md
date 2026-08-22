# Blossom Breach Unity prototype

Unity 6 stationary-defense prototype for a BLE HID light gun. The gun is treated
as a standard mouse: pointer movement aims and the left trigger fires. Touch drag
and tap are the mobile fallback. Gameplay has no keyboard, movement, or reload
input.

## Player flow

The complete flow is pointer-only:

1. Optional H3 opening movie and a large **SKIP** target
2. Title card and **ENTER THE MEADOW**
3. Mission briefing and **BEGIN MISSION**
4. Stationary defense gameplay
5. Mission result and **PLAY AGAIN**

The HUD shows score, chain, core integrity, time, wave, objective, overdrive,
breach warnings, and boss health. UI targets block gameplay shots, so shooting a
button never applies a miss penalty.

## Refresh Unity Personal on this machine

The installed Hub is `C:\Program Files\Unity Hub\Unity Hub.exe`. In the current
environment, Hub 3.18 exits during bootstrap because `ALLUSERSPROFILE` is absent.
The included launcher supplies `C:\ProgramData` only to the Hub process; it does
not change the machine or user environment permanently.

From PowerShell in this directory:

```powershell
.\Launch-UnityHub.ps1
```

Sign in inside Hub and refresh or activate Unity Personal. The existing local
entitlement file has exceeded its offline-validity period, so the Editor cannot
build until Hub renews it online. Do not delete license or token files as a first
step.

Hub 3.18's embedded headless CLI is deprecated. The newer standalone `unity` CLI
is not installed on this machine, so the reliable build path here is the Editor
batch method after Personal activation.

## Build Windows

After Personal is active:

```powershell
.\Build-Windows.ps1
```

The script invokes Unity 6000.4.9f1 and calls
`BlossomBreach.BuildBlossom.BuildWindows`. That method creates and saves
`Assets/Scenes/BlossomBreach.unity` from an empty scene, enables only that scene,
sets a 1920×1080 window and the product title, optionally copies
`game/assets/video/h3-meadow-intro.mp4` into StreamingAssets, and produces:

```text
Builds/Windows/BlossomBreach.exe
```

Build logs are written to `Builds/Logs/windows-build.log`.

Unity references:

- [Unity Editor command-line arguments](https://docs.unity3d.com/Manual/EditorCommandLineArguments.html)
- [Unity Hub CLI and deprecation notice](https://docs.unity.com/en-us/hub/hub-cli)
