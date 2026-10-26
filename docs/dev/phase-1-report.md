# Phase 1 report · Physics core

Status: **all Phase 1 tasks done, waiting for your gate playtest.** Branch `phase-1/test-house`, merged to `main`.

## What was built

| # | Task | Where |
| --- | --- | --- |
| 1 | Project setup: HeadLoss folders, 6 assemblies (Core/Sim/Game are `noEngineReferences`), 4 test assemblies, allowed packages only, Boot + Main scenes, `docs/dev/*` | `Assets/HeadLoss/`, `docs/dev/` |
| 2 | Core: `Units`, `Ids`, PCG32 `Rng`, `DataRepository` (Newtonsoft), `DataValidator`, menu *Head Loss → Validate Data* | `Core/` |
| 3 | Fluid tables: CoolProp script ready; air table is the allowed hand-checked version (see D3) | `tools/fluidtables/`, `Data/fluids/air.json` |
| 4 | Swamee-Jain / laminar / transition friction, Darcy-Weisbach segment Δp, fitting K, elevation term | `Sim/Hydraulics/` |
| 5 | Graph model + global gradient algorithm solver; sparse LDLᵀ (minimum-degree), IC(0)-PCG and dense reference solvers | `Sim/Network/` |
| 6 | Fan curves (monotone cubic, linear beyond Q_max, reverse flow, affinity-law speed) | `Sim/Devices/` |
| 7 | One-room test house (10 × 7 m, air intake on the west wall, 3 vents, small axial fan), building view, pan/zoom camera | `Data/handmade/test-house.json`, `Runtime/` |
| 8 | Drawing tool: drag-to-draw orthogonal ducts, auto elbows/tees/reducers, live K and Δp labels while drawing, delete, size picker, sharp vs long-radius toggle, "Upgrade all bends", box select | `Game/Buildings/`, `Runtime/Interaction/` |
| 9 | Duct line mesh, plain view, velocity heatmap (viridis, 0–6 m/s, lockable), legend, hover + tap/select readout | `Runtime/View/`, `Runtime/UI/` |
| 10 | GPU-instanced flow particles, speed follows velocity | `Runtime/View/FlowParticles.cs` |
| 11 | Debug overlay (`F3`, Editor/dev builds only): iterations, residual, imbalance, solve time, per-link table | `Runtime/UI/BuildingHud.cs` |

Build: `Builds/phase-1/HeadLoss.exe` (Windows 64-bit, Mono). 0 build errors, 0 warnings; a headless start ran with no errors in the log.

## Tests

- **EditMode: 75/75 pass.** Reference cases T1a/b, T2, T2b, T3, T4, T4b, T5, T5b, T6, T7, T10, T11 (T3/T4 also with table properties). Solver behaviour: convergence and mass conservation (< 1e-9 m³/s) on 200 random networks, bit-identical determinism, dense vs PCG vs sparse Cholesky agreement, warm start, closed valve, fan off, dead ends, series fans add pressure, parallel fans add flow, reverse flow through a fan, a 3-loop network against an independent Hardy Cross solution, laminar Hagen-Poiseuille, compressible density. Data validator on all files. Drawing rules, fitting assignment, and the gate case in the test house.
- **PlayMode: 6/6 pass.** Scene boot. Drawing a two-bend run with real Input System mouse events. Click-select and Delete. Heatmap hotkeys. The gate test. The 2,000-segment solve budget.
- T8, T9, T12–T16 belong to later phases (water hammer, thermal, failures, payout, explosions); they get tests when their formulas are built.

Performance (Editor, Mono): 2,000-segment network solve takes **3.8 ms** median for a re-solve with an unchanged topology and **5.1–5.7 ms** for the first solve after a topology change (this includes building the ordering). A warm re-solve takes 0.7 ms. Any solve over 1 ms moves to a worker thread automatically. The test-house solves take ≈ 0.03 ms.

## How to playtest

1. Open `Builds/phase-1/HeadLoss.exe`, or open `Assets/HeadLoss/Scenes/Main.unity` in the Editor and press Play.
2. The test house opens in the **velocity heatmap**. The small axial fan sits at the louvred intake on the left wall.
3. **Draw a duct:** left-drag from the fan to the top-left vent so the run has two corners, for example right 2 tiles, up 2 tiles, right 1 tile. While you drag, the run previews live, and each corner shows its **K and the pressure lost there**. Release to place it. The vent label shows its delivery in m³/h, and particles start flowing.
4. Hover a duct, corner, vent or the fan for its readout. Click to select it; the right panel adds actions.
5. **Gate check:** click one corner and choose **Long-radius**, then the other (or press **Upgrade all bends**). The vent's m³/h rises, the corners turn into smooth arcs, and the ducts brighten on the heatmap, all at once.
6. Try more: branch to the other vents by dragging from the middle of a duct (this makes a tee), change sizes in the toolbox or the selection panel, Delete or Backspace to remove, drag on empty floor to box-select, `0` for plain view, `1` for velocity, *Lock range* in the legend.
7. Camera: mouse wheel to zoom; right- or middle-drag, WASD or arrow keys to pan. Esc cancels a drag or clears the selection. In the Editor, `F3` opens the solver overlay.

## Gate checklist (from `14-roadmap.md`)

- [ ] In the test house, a duct run with two sharp 90° bends feeds a vent.
- [ ] Switching both bends to long-radius raises that vent's flow by a visible amount. Reference ~+15% for T5; measured in the test house with 125 mm ducts: **204 → 243 m³/h (+18.9%)**. With 100 mm: +24.5%; with 160 mm: +10.6%.
- [ ] The velocity heatmap changes colour accordingly, within one second, without reading numbers. Measured: the recolour lands **7 ms** after the change, colour difference 0.62 (RGB sum).
- [ ] *(Definition of done)* No console errors during a 10-minute session. I could not do a hands-on 10-minute session, because the Editor only takes input while it has OS focus. Please watch for errors while you play.

## Screenshots

- `screenshots/phase-1/velocity-heatmap.png`: three branches, sharp and long-radius corners, particles, vent deliveries.
- `screenshots/phase-1/drawing-preview-labels.png`: a run being drawn, with live K / Δp at the new tee and corner.
- `screenshots/phase-1/debug-overlay-plain-view.png`: plain view, duct selection panel, `F3` solver table.

## Known issues

- The air table is the hand-checked one (ideal gas + Sutherland), not CoolProp yet. It must be replaced before Phase 2 ends (D3).
- At 1280 × 720 the heatmap buttons sit close to the building's top wall. The HUD is laid out for 1920 × 1080.
- In the drawing preview, the new run's corner caps look slightly darker where translucent pieces overlap.
- The first solve after an edit on a very large (2,000-segment) network is ~5–6 ms in the Editor, at the budget line. It runs off the main thread. See D12.
- The Boot scene only loads Main; the main menu comes with saving.
- The template's `Assets/Scenes/SampleScene.unity` and `Assets/Settings` are still in the project (not in the build). I can delete them if you want.
- No undo, design mode, money or audio yet (Phase 2).

## Needs your confirmation

From `docs/dev/decisions.md`:

- **D3:** may I `pip install CoolProp` to generate the real fluid tables?
- **D6:** vent grille face area 0.04 m² (200 × 200 mm).
- **D7:** air network elevation; also a heads-up for Phase 5 that gas buoyancy should probably use `(ρ_gas − ρ_air)·g·Δz`.
- **D8:** drawing rules: one duct per tile, at most 3 ducts per node, dead ends are capped.
- **D11:** heatmap opens in velocity mode; the scale is fixed at 0–6 m/s unless exceeded.
- **D18:** the small axial fan overdelivers against tier 1 vent limits (balancing for Phase 2).

Also logged without a confirmation tag: template packages removed (D1), long-radius elbows available in Phase 1 for the gate (D10), sparse Cholesky as the default solver (D12), and the Input System test framework enabled via `testables` (D17).

None of the eight `[confirm]` assumptions in `00-index.md` apply to Phase 1. They start with budgets and payouts in Phase 2.
