# Progress

Current phase: **1 · Physics core** (`docs/spec/14-roadmap.md`).

## Phase 1 · Physics core

- [x] 1. Project setup: folders, asmdefs, packages, URP 2D, Input System, test assemblies, `.gitignore`, git, `docs/dev/*` files, Unity CLI notes.
- [x] 2. `Core`: units, ids, PCG32 RNG, data repository + validator, JSON loading.
- [x] 3. Fluid tables (hand-checked air table committed; CoolProp generation pending D3): `tools/fluidtables/generate.py` with CoolProp; air table committed (hand-checked air allowed temporarily).
- [x] 4. Friction factor, segment Δp, fittings K, elevation term. Tests T1–T4.
- [x] 5. Graph model + GGA solver (reference dense solver + sparse PCG). Tests T5–T6, convergence tests, determinism test.
- [x] 6. Fan devices with curves, speed scaling. Tests T5, affinity laws.
- [x] 7. One-room test house (hand-made layout, air intake, 3 vents), building view, camera.
- [x] 8. Drawing tool: orthogonal duct drawing, auto elbows/tees/reducers, live K and Δp labels, delete, size picker, sharp vs long-radius elbow toggle.
- [x] 9. Pipe rendering, plain view, velocity heatmap on ducts (rooms stay plain in this phase), legend, hover readout.
- [x] 10. Particles along ducts.
- [x] 11. Debug overlay (dev only): solver iterations, residual, per-link table.
- [x] Phase end: all tests green (EditMode 75, PlayMode 6), Windows build in `Builds/phase-1/`, `docs/dev/phase-1-report.md`.
- [ ] Gate playtest by the user (waiting).
