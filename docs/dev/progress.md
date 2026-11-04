# Progress

Current phase: **5 · Gas and prestige** (not started) (`docs/spec/14-roadmap.md`).

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

Task list from `14-roadmap.md` §Phase 3:

- [x] 1. City generation (districts, roads, rail, parks, protected and historic sites, facilities, plots, starter guarantee). Seeds and tests.
- [x] 2. City view rendering, plot hover difficulty hint, open building, online glow / offline grey.
- [x] 3. Procedural building generation for tier 1–2 single-floor buildings, air only: footprint, rooms, doors, walls, outlets by room type, air intake, occupants, heat loads.
- [x] 4. Auto-router + solvability check; reference layout; budget cap from it.
- [x] 5. Room grid CPU reference (velocity field, advection, diffusion, temperature, CO₂), Burst version, comparison test. Room heatmaps for velocity, temperature, density.
- [x] 6. Thermal model along ducts; outside temperature (climate), solar gain, occupants.
- [x] 7. Tech unlocks (tier 1 → 2), upgrade shop with compare view. Centrifugal blower, EC fan, VSD (speed slider applies without Commit, D25), duct sizes, galvanized vs flex vs spiral, duct sealing, insulation.
- [x] 8. Offline earnings: catch-up algorithm, "While you were away" report, offline cap (offline time is already measured, D39).
- [x] 9. Steamworks.NET integration (app id 480 placeholder until the user provides one), Steam Cloud for saves, playtest build.
- [x] 10. Settings screen (audio, palette, UI scale, theme, reduce motion, keys).
- [x] Phase end: all tests green (EditMode 231, PlayMode 25), Windows build in `Builds/phase-3/`, `docs/dev/phase-3-report.md`.
- [x] Gate playtest by the user: **passed 2026-10-30**.
- [x] Carry-over (done: city view replaces the building list; tier 2 tuned with the report, D61; hand-made references retired, D45): the building list (D34) gives way to the city view; tune tier 2+ prices and base pay with the balancing report on generated buildings (D23, D36); retire the hand-made reference layouts as the auto-router takes over (D28).

### Session notes (2026-10-30)

- Phase 3 complete and merged; report in `docs/dev/phase-3-report.md`. Gate passed 2026-10-30.

## Phase 4 · Water, floors and power

Task list from `14-roadmap.md` §Phase 4:

- [x] 1. Water fluid, pipe materials (PVC, copper, steel), pipe sizes, wave speeds.
- [x] 2. Taps (demand nodes), mains-pressure-only connection, small booster pump, multistage pump, inline booster.
- [x] 3. Multi-floor buildings: floors, stairwells, shafts, risers, ghosts, floor switcher; tier 2–3 buildings with water.
- [x] 4. Layers: floor, ceiling with capacity units, (wall cavity stub for tier 4).
- [x] 5. City utility network: water treatment plant source, routing tool, mains sizes and capacity, violations and recurring fines, crossing costs, utility corridor, directional drilling, city solve with capacity sharing, utilisation heatmap, building ↔ city coupling.
- [x] 6. Burst model: ratings, stress, Weibull hazard, weeping, stress heatmap, burst effects on income (in game / offline), repairs (emergency / standard), shutoff isolation, relief valves, regulators, check valves, maintenance contracts.
- [x] 7. Water hammer surges and the pressure tank / accumulator.
- [x] 8. Building codes: noise, tap pressure, no water above electrics, fire dampers, constraints (walls, columns, protected rooms, core drilling); violation outlines and list.
- [x] 9. Electricity: basic connection until unlock; substations, cables, kW capacity, brownouts (furthest first), panels (basic → standard → large), trip and reset, connection kW, power heatmap.
- [x] 10. Pipe-wall leak sensors, engineering software (stress forecast), insurance.
- [x] 11. Tech unlocks to tier 3.
- [x] Carry-over from earlier phases: one duct per tile gives way to ceiling capacity (D8); stress readout (D15) and stress heatmap (D64); noise code checked by the code system instead of only the vent max (D37); larger equipment (centrifugal blower 2×1, EC fan 2×2) takes floor space on the floor layer (D62, phase-3 report).
- [x] Phase end: all tests green, Windows build in `Builds/phase-4/`, `docs/dev/phase-4-report.md`.
- [x] Gate playtest by the user: **passed 2026-11-04**.

### Session notes (2026-11-03, Phase 4 session 1)

Done on `phase-4/water-core` (commits up to "city utility network"): water data, solver control valves (check, regulator, relief), floors/risers/water network/ceiling capacity/core drilling/floor space, multi-floor tier 2–3 generation with water and the 3D auto-router (200-seed solvability passes for tiers 1–3), save v3 (tier 2+ refunded and reset, D83), burst model (stress per edge, Weibull, isolation, income cut, repairs, water hammer, pressure tank), building codes, city mains/cables model + city solve + power allocation + panel trip/brownout + fines/insurance/meta actions (game side), floor-aware renderers and interaction. Decisions D71–D89 logged (D90 starvation pressure ∝ share² still to write up).

Session 2 (same day): building HUD, city tools, offline timeline, tier 3 tuning, decisions D90–D105. See `docs/dev/phase-4-report.md`.

Phase 4 complete and merged; report in `docs/dev/phase-4-report.md`. Gate passed 2026-11-04, with D74–D105 confirmed and no 45° drawing (D106).

## Phase 5 · Gas and prestige

Task list from `14-roadmap.md` §Phase 5:

- [x] 1. Natural gas fluid (ideal-gas density, compressibility warning), gas regulator, gas mains (LP/MP), gas receiving station, separation violations.
- [x] 2. Gas stove outlets and tier 3 buildings (apartment block, restaurant).
- [x] 3. Room grid species, two layers, stratification, floor exchange; concentration heatmap.
- [x] 4. Leaks (all causes), detectors, alarm and evacuation, explosion timer, offline pause, auto-shutoff, spark-free equipment, passive vents, ignition sources.
- [x] 5. Explosions: energy, radius, damage, ruins, chain reactions, fines, calm presentation.
- [x] 6. Gas codes; gas safety certificate.
- [x] 7. Prestige: milestone, Reputation, tree, Reconstruction permits, new city with modifiers (cold climate, old buildings).
- [x] 8. Steam page assets (generated in-engine screenshots and a capsule rendered from the game), demo build configuration, Early Access checklist (achievements optional, store text draft for the user).
- [x] Carry-over from earlier phases: table density for MP mains (D21); concentration heatmap's species picker (D64); tier 3 buildings gain gas, replacing the air-and-water stopgap (D101); re-run the balancing report for tier 3 with gas in its cost, payback was 2.3 min against the 20 min target (D91).
- [x] Phase end: all tests green, Windows build in `Builds/phase-5/`, `docs/dev/phase-5-report.md`.
- [ ] Gate playtest by the user. Checklist: the whole loop from crank to first prestige plays without dev tools; gas danger feels tense but fair and calm; the user agrees it is ready for Early Access. At this gate, ask the user which Phase 6 items must be in the Early Access launch.
