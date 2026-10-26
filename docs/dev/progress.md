# Progress

Current phase: **3 · Idle city** (not started; waiting for the go-ahead) (`docs/spec/14-roadmap.md`).

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

Task list from `14-roadmap.md` §Phase 2:

- [x] 1. Outlets with min/ideal/code max; payout multiplier formula; building online/offline rule. Tests for every zone and both caps (T14, T15).
- [x] 2. Requirement sheet panel and outlet list with live zones.
- [x] 3. Economy: wallet, debt, base pay, electricity cost (device power), income formula at 10 Hz, money display with suffixes, income breakdown.
- [x] 4. Hand crank (click/hold), small axial fan, inline duct fan. VSD not yet.
- [x] 5. Dampers with position slider; balancing.
- [x] 6. Design mode, Commit/Discard, costs, refunds, budget cap rule [confirm].
- [x] 7. Undo/redo command stack.
- [x] 8. One hand-built small house (multi-room with doors, 1 floor, 5 vents) plus the one-room test house (now the starter, with a hand crank).
- [x] 9. Save/load (format, autosave, backups, atomic write, gzip, migrations scaffold, sanitising).
- [x] 10. Generated fluid tables in use (no hand-typed air table left). Done in Phase 1 (D3).
- [x] 11. Calm audio first pass (airflow, fan hum, UI clicks, placing tock, commit chime).
- [x] 12. Balancing report editor window (`Head Loss/Balancing Report`; hand-made buildings until the Phase 3 generator).
- [x] Carry-over: D18, small axial fan overdelivers against tier 1 vent limits (D22).
- [x] Phase end: all tests green, Windows build in `Builds/phase-2/`, `docs/dev/phase-2-report.md`.
- [x] Gate playtest by the user: **passed 2026-10-26**.

## Phase 3 · Idle city

Not started. Task list from `14-roadmap.md` §Phase 3:

- [ ] 1. City generation (districts, roads, rail, parks, protected and historic sites, facilities, plots, starter guarantee). Seeds and tests.
- [ ] 2. City view rendering, plot hover difficulty hint, open building, online glow / offline grey.
- [ ] 3. Procedural building generation for tier 1–2 single-floor buildings, air only: footprint, rooms, doors, walls, outlets by room type, air intake, occupants, heat loads.
- [ ] 4. Auto-router + solvability check; reference layout; budget cap from it.
- [ ] 5. Room grid CPU reference (velocity field, advection, diffusion, temperature, CO₂), Burst version, comparison test. Room heatmaps for velocity, temperature, density.
- [ ] 6. Thermal model along ducts; outside temperature (climate), solar gain, occupants.
- [ ] 7. Tech unlocks (tier 1 → 2), upgrade shop with compare view. Centrifugal blower, EC fan, VSD (speed slider applies without Commit, D25), duct sizes, galvanized vs flex vs spiral, duct sealing, insulation.
- [ ] 8. Offline earnings: catch-up algorithm, "While you were away" report, offline cap (offline time is already measured, D39).
- [ ] 9. Steamworks.NET integration (app id 480 placeholder until the user provides one), Steam Cloud for saves, playtest build.
- [ ] 10. Settings screen (audio, palette, UI scale, theme, reduce motion, keys).
- [ ] Carry-over: the building list (D34) gives way to the city view; tune tier 2+ prices and base pay with the balancing report on generated buildings (D23, D36); retire the hand-made reference layouts as the auto-router takes over (D28).
