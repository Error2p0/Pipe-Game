# Progress

Current phase: **1 · Physics core** (`docs/spec/14-roadmap.md`).

## Phase 1 · Physics core

- [ ] 1. Project setup: folders, asmdefs, packages, URP 2D, Input System, test assemblies, `.gitignore`, git, `docs/dev/*` files, Unity CLI notes.
- [ ] 2. `Core`: units, ids, PCG32 RNG, data repository + validator, JSON loading.
- [ ] 3. Fluid tables: `tools/fluidtables/generate.py` with CoolProp; air table committed (hand-checked air allowed temporarily).
- [ ] 4. Friction factor, segment Δp, fittings K, elevation term. Tests T1–T4.
- [ ] 5. Graph model + GGA solver (reference dense solver + sparse PCG). Tests T5–T6, convergence tests, determinism test.
- [ ] 6. Fan devices with curves, speed scaling. Tests T5, affinity laws.
- [ ] 7. One-room test house (hand-made layout, air intake, 3 vents), building view, camera.
- [ ] 8. Drawing tool: orthogonal duct drawing, auto elbows/tees/reducers, live K and Δp labels, delete, size picker, sharp vs long-radius elbow toggle.
- [ ] 9. Pipe rendering, plain view, velocity heatmap on ducts (rooms stay plain in this phase), legend, hover readout.
- [ ] 10. Particles along ducts.
- [ ] 11. Debug overlay (dev only): solver iterations, residual, per-link table.
- [ ] Phase end: all tests green, Windows build in `Builds/phase-1/`, `docs/dev/phase-1-report.md`, gate playtest.
