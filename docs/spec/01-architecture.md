# 01 · Architecture

[doc] Pipes and ducts are solved as a graph network; only open rooms use a grid. Keep simulation in plain C# so it can be unit-tested against known textbook pipe problems. Run the room grid in a compute shader or Burst job; the network solve fits a Burst-compiled sparse solver.

[impl] Everything below.

## Layers

```
HeadLoss.Unity  (MonoBehaviours, UI Toolkit, rendering, input, audio, Burst job adapters)
      │ reads state, sends commands
HeadLoss.Game   (rules: economy, failures, codes, events, progression, generation, save, offline)
      │ uses
HeadLoss.Sim    (physics: fluids, network solver, devices, room grid CPU reference, thermal)
      │ uses
HeadLoss.Core   (units, ids, RNG, math, data loading, events bus)
```

- `Core`, `Sim`, `Game` have `noEngineReferences: true`. They can be tested without entering play mode and without Unity types.
- `Unity` owns the frame loop. It calls `GameLoop.Tick(dtSeconds)` from `Update`, renders state, and turns input into `ICommand` objects (`PlaceSegment`, `PlaceEquipment`, `CommitDesign`, `Repair`, …).
- Commands go through `CommandStack` (undo/redo) in `Game`. Nothing outside `Game` mutates game state.
- Burst/Jobs implementations live in `HeadLoss.Unity.Jobs` and implement interfaces defined in `Sim` (`IRoomGridStepper`, `ISparseSolver`). `Sim` ships a plain C# reference implementation of each, used by tests and as a fallback. A test asserts that the Burst version matches the reference within tolerance.

## Assemblies and folders

```
Assets/HeadLoss/
  Core/            HeadLoss.Core.asmdef           (noEngineReferences)
  Sim/             HeadLoss.Sim.asmdef            (noEngineReferences) → Core
  Game/            HeadLoss.Game.asmdef           (noEngineReferences) → Core, Sim
  Runtime/         HeadLoss.Unity.asmdef          → Core, Sim, Game, Unity packages
  Runtime/Jobs/    HeadLoss.Unity.Jobs.asmdef     → Core, Sim, Burst, Collections, Mathematics
  Editor/          HeadLoss.Editor.asmdef         (Editor only) → all
  Data/            *.json game data (TextAssets)
  Shaders/         heatmap, pipe, particle, city shaders (URP)
  UI/              UXML, USS
  Scenes/          Boot.unity, Main.unity
  Tests/
    EditMode/Core/    HeadLoss.Core.Tests
    EditMode/Sim/     HeadLoss.Sim.Tests
    EditMode/Game/    HeadLoss.Game.Tests
    PlayMode/         HeadLoss.PlayMode.Tests
tools/
  fluidtables/     generate.py (CoolProp) → Assets/HeadLoss/Data/fluids/*.json
docs/
  spec/            this spec
  dev/             progress.md, decisions.md, unity-cli-notes.md, phase-N-report.md
Builds/            (gitignored)
```

## Packages

Allowed: Universal Render Pipeline (2D Renderer), Input System, UI Toolkit (built in), Burst, Collections, Mathematics, Jobs, Test Framework, `com.unity.nuget.newtonsoft-json`, `com.unity.pipeline` (for the CLI). Phase 3 onward: Steamworks.NET (git URL package) for the Steam playtest build. Anything else: ask first.

## Data

- All game data is JSON in `Assets/HeadLoss/Data/`, loaded through `Core.Data.DataRepository`. In the game it reads TextAssets via a small Unity adapter; in tests it reads the files from disk by path. Same parser (Newtonsoft).
- Files: `game.json`, `balancing.json`, `fluids/*.json`, `materials.json`, `pipes.json`, `fittings.json`, `equipment.json`, `outlets.json`, `codes.json`, `buildings.json` (tiers, room types, generation rules), `city.json`, `events.json`, `inspections.json`, `prestige.json`, `upgrades.json`.
- Every data type has a C# definition class in `Core.Data.Defs`, a JSON schema-like validator (`DataValidator`) and an editor menu item `Head Loss/Validate Data`. A test runs the validator on all data files.
- Ids are lowercase kebab strings (`small-axial-fan`, `copper`, `air-supply-vent`). References between files use ids and are validated.

## State model

- `GameState` (root, serializable): wallet, reputation, prestige bonuses, unlocks, current `CityState`, settings, timestamps, RNG states.
- `CityState`: seed, modifiers, tile map, facilities, plots, `MainsNetwork` per utility, power cables, active events, violations, fines ledger.
- `BuildingState` (per plot, created lazily when first opened): seed, generated layout (regenerable from seed, not saved), player edits (segments, equipment, settings), stress per segment, failures, certificates, energy rating, online flag, stats.
- Generated content is **not** saved; it is regenerated from seeds. Only player edits and dynamic state are saved. A generator version number is saved; if it changes, the loader warns and keeps edits that still fit.

## Rendering [impl]

- URP with the 2D Renderer, orthographic camera, pan (right/middle drag, WASD, edge optional) and zoom (wheel, pinch later).
- Tiles, walls, rooms, plots: procedural meshes batched per floor/district.
- Pipes and ducts: a procedural line mesh per network, width by diameter, vertex colour from the active heatmap value (or fluid colour in plain view).
- Room heatmaps: one `RFloat` texture per floor at room-grid resolution (0.5 m cells), sampled by a shader with a palette ramp texture (viridis default).
- Flow particles: GPU instanced quads moved along segment polylines, speed proportional to local velocity (clamped for readability).
- City: simple extruded-looking flat blocks (two-tone top/side), emissive tint when online, grey when offline, ruins as broken outlines.
- No post-processing except optional subtle bloom on online buildings (off by default on low settings).

## Performance budgets [impl]

| Item | Budget (mid-range PC, 1080p) |
| --- | --- |
| Frame | 60 fps, < 8 ms CPU main thread |
| Building network solve (2,000 segments) | < 5 ms, off the main thread when > 1 ms |
| City mains solve (500 segments, all utilities) | < 10 ms |
| Room grid step (whole building, 10 floors) | < 2 ms with Burst |
| Offline catch-up for 72 h, 100 buildings | < 2 s, with a progress indicator |
| Memory | < 1.5 GB |

Add a PlayMode performance test per budget once the system exists.

## Threading

- The network solve runs on a worker (`Task` or a Job) when the network changes; results swap in atomically. The UI shows the last valid result while solving.
- Room grid stepping is a Burst job scheduled in `Update` and completed in `LateUpdate`.
- Game rules (`Game` assembly) run on the main thread at their tick rates (`05-simulation.md` §Tick rates).
