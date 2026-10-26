# 14 · Roadmap, phases and gates

[doc] The first milestone is the smallest thing that proves the core is fun: one room, air only, a fan, and a heatmap that reacts when you fix a bad bend. Each phase only starts once its gate passes; if phase 1's gate fails, the physics presentation needs rework before anything else is built.

| Phase | Scope [doc] | Gate to move on [doc] |
| --- | --- | --- |
| 1 · Physics core | Air only, one-room test house, graph solver, fan curves, velocity heatmap. Unit tests against textbook pipe problems | Fixing a sharp bend visibly raises flow |
| 2 · First playable building | Requirement sheet, payout bands, electricity cost, 3 fans, dampers. One hand-built small house, save and load, undo | A 20-minute session feels satisfying |
| 3 · Idle city | Procedural city and houses, offline earnings, room grid, temperature. Upgrade shop, first Steam playtest build | Playtesters return the next day |
| 4 · Water, floors and power | Multi-floor buildings, risers, pumps, burst model, building codes. Ceiling layer, repairs, relief valves, city power cables, electrical panels | Bursts feel fair, never arbitrary |
| 5 · Gas and prestige | Natural gas, leaks, alarms, explosions, new-city prestige. Steam page, demo, Early Access prep | Ready for Early Access |
| 6 · Full content [confirm] | Everything in the spec not named above (see list) | Agreed with the user at the Phase 5 gate |

## Definition of done (every phase) [impl]

- All tasks below ticked in `docs/dev/progress.md`.
- All EditMode and PlayMode tests pass; no compiler warnings from project code; no errors in the console during a 10-minute play session.
- Data validator passes. Performance budgets met for the systems that exist.
- Windows standalone build in `Builds/phase-N/` starts and plays.
- `docs/dev/phase-N-report.md` written: what was built, how to playtest (step by step), the gate checklist, screenshots, known issues, `[confirm]` and `NEEDS CONFIRMATION` items.
- Then **stop and ask the user to playtest the gate.**

---

## Phase 1 · Physics core

Spec files: `01`, `05` §1–2, `06`, `07` (air movers, ducts, fittings), `11` (heatmaps, drawing), `15`.

1. Project setup: folders, asmdefs, packages, URP 2D, Input System, test assemblies, `.gitignore`, git, `docs/dev/*` files, Unity CLI notes.
2. `Core`: units, ids, PCG32 RNG, data repository + validator, JSON loading.
3. Fluid tables: `tools/fluidtables/generate.py` with CoolProp; air table committed (hand-checked air allowed temporarily).
4. Friction factor, segment Δp, fittings K, elevation term. Tests T1–T4 (`15-testing.md`).
5. Graph model + GGA solver (reference dense solver + sparse PCG). Tests T5–T6, convergence tests, determinism test.
6. Fan devices with curves, speed scaling. Tests T5, affinity laws.
7. One-room test house (hand-made layout, air intake, 3 vents), building view, camera.
8. Drawing tool: orthogonal duct drawing, auto elbows/tees/reducers, live K and Δp labels, delete, size picker, sharp vs long-radius elbow toggle.
9. Pipe rendering, plain view, velocity heatmap on ducts (rooms stay plain in this phase), legend, hover readout.
10. Particles along ducts.
11. Debug overlay (dev only): solver iterations, residual, per-link table.

**Gate checklist:** in the test house, a duct run with two sharp 90° bends feeding a vent; switching both to long-radius elbows raises that vent's flow by a visible amount (reference: ~+15% for the T5 case) and the velocity heatmap changes colour accordingly, within one second, without the player reading numbers.

## Phase 2 · First playable building

Spec files: `02`, `04` (requirement sheet), `07` (3 fans: hand crank, small axial, inline duct fan; dampers), `11`, `12` (save/load), `13`.

1. Outlets with min/ideal/code max; payout multiplier formula; building online/offline rule. Tests for every zone and both caps.
2. Requirement sheet panel and outlet list with live zones.
3. Economy: wallet, base pay, electricity cost (device power), income formula, money display with suffixes, income breakdown.
4. Hand crank (click/hold), small axial fan, inline duct fan. VSD not yet.
5. Dampers with position slider; balancing.
6. Design mode, Commit/Discard, costs, refunds, budget cap rule [confirm].
7. Undo/redo command stack.
8. One hand-built small house (multi-room, 1 floor, 4–5 vents) plus the one-room test house.
9. Save/load (format, autosave, backups, atomic write, migrations scaffold).
10. Generated fluid tables in use (no hand-typed air table left).
11. Calm audio first pass (airflow, fan hum, UI clicks).
12. Balancing report editor window (`13-balancing.md`).

**Gate checklist:** a fresh save; crank, buy the small fan, optimise the house, reach overdelivery, and feel the electricity sweet spot (spinning harder does not pay). A 20-minute session feels satisfying.

## Phase 3 · Idle city

Spec files: `03` (city, generation, difficulty hint, progression unlocks, shop), `04` (generation, auto-router, solvability), `05` §3–4 (room grid, thermal), `11`, `12` (offline), `13`.

1. City generation (districts, roads, rail, parks, protected and historic sites, facilities, plots, starter guarantee). Seeds and tests.
2. City view rendering, plot hover difficulty hint, open building, online glow / offline grey.
3. Procedural building generation for tier 1–2 single-floor buildings, air only (water arrives in Phase 4): footprint, rooms, doors, walls, outlets by room type, air intake, occupants, heat loads.
4. Auto-router + solvability check; reference layout; budget cap from it.
5. Room grid CPU reference (velocity field, advection, diffusion, temperature, CO₂), Burst version, comparison test. Room heatmaps for velocity, temperature, density.
6. Thermal model along ducts; outside temperature (climate), solar gain, occupants.
7. Tech unlocks (tier 1 → 2), upgrade shop with compare view. Centrifugal blower, EC fan, VSD, duct sizes, galvanized vs flex vs spiral, duct sealing, insulation.
8. Offline earnings: catch-up algorithm, "While you were away" report, offline cap.
9. Steamworks.NET integration (app id 480 placeholder until the user provides one), Steam Cloud for saves, playtest build.
10. Settings screen (audio, palette, UI scale, theme, reduce motion, keys).

**Gate checklist:** playtesters (the user and friends) play a session, close the game, and come back the next day because their city earned while away and they want the next building.

## Phase 4 · Water, floors and power

Spec files: `03` (utility network, mains, routing, violations, fines, city solve, electricity), `04` (floors, risers, layers, ceiling capacity, panel, entry openings), `05` (water, elevation, coupling, water hammer), `07` (water movers, pipes, materials, control parts, panels, pressure tank, leak sensors, VSD for pumps, insulation), `08` (burst model, repairs, shutoff isolation), `09` (codes and constraints), `13`.

1. Water fluid, pipe materials (PVC, copper, steel), pipe sizes, wave speeds.
2. Taps (demand nodes), mains-pressure-only connection, small booster pump, multistage pump, inline booster.
3. Multi-floor buildings: floors, stairwells, shafts, risers, ghosts, floor switcher; tier 2–3 buildings with water.
4. Layers: floor, ceiling with capacity units, (wall cavity stub for tier 4).
5. City utility network: water treatment plant source, routing tool, mains sizes and capacity, violations and recurring fines, crossing costs, utility corridor, directional drilling, city solve with capacity sharing, utilisation heatmap, building ↔ city coupling.
6. Burst model: ratings, stress, Weibull hazard, weeping, stress heatmap, burst effects on income (in game / offline), repairs (emergency / standard), shutoff isolation, relief valves, regulators, check valves, maintenance contracts.
7. Water hammer surges and the pressure tank / accumulator.
8. Building codes: noise, tap pressure, no water above electrics, fire dampers, constraints (walls, columns, protected rooms, core drilling); violation outlines and list.
9. Electricity: basic connection until unlock; substations, cables, kW capacity, brownouts (furthest first), panels (basic → standard → large), trip and reset, connection kW, power heatmap.
10. Pipe-wall leak sensors, engineering software (stress forecast), insurance.
11. Tech unlocks to tier 3.

**Gate checklist:** push a water network above rating on purpose and see stress build visibly before any burst; bursts happen roughly when the forecast says; relief valves and shutoffs clearly limit the damage. Bursts feel fair, never arbitrary.

## Phase 5 · Gas and prestige

Spec files: `03` (gas mains, prestige, modifiers), `06` (natural gas), `07` (gas supply, safety gear), `08` (gas escalation, explosions, blast radius, chain reactions, ruins, offline gas rules), `09` (gas separation, gas room ventilation, accessible shutoffs), `10` (gas safety certificate), `13`.

1. Natural gas fluid (ideal-gas density, compressibility warning), gas regulator, gas mains (LP/MP), gas receiving station, separation violations.
2. Gas stove outlets and tier 3 buildings (apartment block, restaurant).
3. Room grid species, two layers, stratification, floor exchange; concentration heatmap.
4. Leaks (all causes), detectors, alarm and evacuation, explosion timer, offline pause, auto-shutoff, spark-free equipment, passive vents, ignition sources.
5. Explosions: energy, radius, damage, ruins, chain reactions, fines, calm presentation.
6. Gas codes; gas safety certificate.
7. Prestige: milestone, Reputation, tree, Reconstruction permits, new city with modifiers (cold climate, old buildings).
8. Steam page assets (generated in-engine screenshots and a capsule rendered from the game), demo build configuration, Early Access checklist (achievements optional, store text draft for the user).

**Gate checklist:** the whole loop from crank to first prestige plays without dev tools; gas danger feels tense but fair and calm; the user agrees it is ready for Early Access. At this gate, ask the user which Phase 6 items must be in the Early Access launch.

## Phase 6 · Full content [confirm]

Everything in the spec that the doc's roadmap does not name. Order to agree with the user.

1. Hot water and heating: water heater, hot taps, circulation loops, legionella code, district heating plant and mains, heat exchanger, gas boiler, radiators, heating loops, condensation code (tier 3–4).
2. Cooling: aircon split, heat pump, refrigerant lines, cooling terminals, heat recovery units, exhaust networks, industrial AHU with slots (tier 4).
3. Tier 4 buildings (warehouse, office), wall cavity layer, transformer room, smart building controller, gas booster compressor, gas manifold, VS pump set.
4. Indoor air quality (CO₂) code with occupants.
5. Events: heatwave, cold snap, plant maintenance, power outage with full cascade (gas ventilation loss, lab chillers and tank venting, basement flooding, sump pumps, raised panels, backup generators), new tenant, city festival.
6. Inspections: random inspection, energy rating A–G.
7. Tier 5: factory, lab, hospital; propane (basement code), special gases (CO₂, nitrogen, oxygen, hydrogen), purity, oxygen enrichment, asphyxiation zones, hydrogen high-level ventilation, backflow prevention, industrial gas depot and its strict mains, gas storage tanks, air filters (basic, HEPA), clean-air vents.
8. Substation upgrades, booster/compressor stations, HP gas mains, historic foundations code.
9. Mobile readiness pass (touch action map, UI review) if the user wants it.
