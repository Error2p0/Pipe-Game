# Progress

Current phase: **2 · First playable building** (not started; waiting for the go-ahead) (`docs/spec/14-roadmap.md`).

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
- [x] Gate playtest by the user: **passed 2026-10-26**.

## Phase 2 · First playable building

Not started. Task list from `14-roadmap.md` §Phase 2 (task 10, generated fluid tables, is already done):

- [ ] 1. Outlets with min/ideal/code max; payout multiplier formula; building online/offline rule. Tests for every zone and both caps.
- [ ] 2. Requirement sheet panel and outlet list with live zones.
- [ ] 3. Economy: wallet, base pay, electricity cost (device power), income formula, money display with suffixes, income breakdown.
- [ ] 4. Hand crank (click/hold), small axial fan, inline duct fan. VSD not yet.
- [ ] 5. Dampers with position slider; balancing.
- [ ] 6. Design mode, Commit/Discard, costs, refunds, budget cap rule [confirm].
- [ ] 7. Undo/redo command stack.
- [ ] 8. One hand-built small house (multi-room, 1 floor, 4–5 vents) plus the one-room test house.
- [ ] 9. Save/load (format, autosave, backups, atomic write, migrations scaffold).
- [x] 10. Generated fluid tables in use (no hand-typed air table left). Done in Phase 1 (D3).
- [ ] 11. Calm audio first pass (airflow, fan hum, UI clicks).
- [ ] 12. Balancing report editor window (`13-balancing.md`).
- [ ] Carry-over: D18, small axial fan overdelivers against tier 1 vent limits.
