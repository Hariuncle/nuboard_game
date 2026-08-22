# External 3D asset slot

The game always runs with the procedural low-poly actors in
`Assets/Scripts/Presentation`. External models are optional presentation
replacements and never own health, hit zones, movement, or collision.

## Acorn Bomber integration

Preferred import: an optimized FBX or prefab named
`AcornBomber_Optimized`. Unity does not import GLB without an extra importer, so
the source GLB is reference material only unless it has first been converted to
FBX. No runtime GLB package is required.

For editor play mode, place the optimized asset at one of:

- `Assets/ExternalAssets/Meshy/AcornBomberOptimized/AcornBomber_Optimized.prefab`
- `Assets/ExternalAssets/Meshy/AcornBomberOptimized/AcornBomber_Optimized.fbx`
- `Assets/ExternalAssets/Meshy/AcornBomberOptimized/model.fbx`
- `Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber_Optimized.prefab`
- `Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber_Optimized.fbx`
- `Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber.fbx`

For a player build, put the optimized FBX/prefab at
`Assets/Resources/Meshy/AcornBomber/AcornBomber_Optimized.fbx` (the runtime
Resources key omits the extension), or register a
referenced prefab once at startup with:

```csharp
OptionalEnemyModelAdapter.RegisterBomberPrefab(acornBomberPrefab);
```

`OptionalEnemyModelAdapter` normalizes the model to the procedural character
height, disables imported colliders, keeps the glowing procedural weak point,
and uses shared imported materials. It permits at most four external instances.
Models over 12,000 triangles or four shared materials are rejected and the
procedural Acorn Bomber remains visible.

## Animator states

An Animator Controller is optional. If present, put compatible states on Base
Layer using either name in each row:

| Action | Preferred state | Alias |
| --- | --- | --- |
| Forward loop | `RUN_FORWARD` | `Running` or `CHARGE` |
| Hit reaction | `HIT_RECOIL` | `BeHit` |
| Death | `DEATH` | `Dead` or `DEATH_FALL_BACK` |

Disable root motion in the imported controller. `EnemyActor` remains responsible
for forward movement and knockback. Missing assets, missing clips, import errors,
or budget rejection all fall back to the procedural 3D enemy without changing
BLE HID mouse-gun controls or wave logic.
