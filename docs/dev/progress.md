# Progress

Current phase: **6 · Full content**, started 2026-11-07. Order agreed in D135. Session 6.0 did task 0 (report: `docs/dev/phase-6.0-report.md`). Session 6.1 did tasks 4 and 6 (report: `docs/dev/phase-6.1-report.md`). Next: task 3 (tier 4 shell, no heating yet).

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
- [x] Gate playtest by the user: **passed 2026-11-04**, with D107, D108, D109, D110, D112, D117, D121 and D126 confirmed and Phase 6 in the Early Access launch (D132).

## Phase 5.5 · Fixes and polish

An intermediate phase the user added on 2026-11-04, before Phase 6 starts. It is not in `14-roadmap.md`: the user sends each task after the last one is done, and it has no gate of its own.

- [x] 1. Switching off a fan or pump slows it to a stop instead of cutting it, and the values follow (D138). Each device has its own times, and switching on ramps up too (D139).
- [x] 2. Risers can be selected and deleted (D140).
- [x] 3. Ghost floors show long-radius fittings and are fainter, so they are not mistaken for the floor on show (D141).
- [x] 4. Generator for `equipment.json` brought up to date (D139).
- [x] 5. The network not in focus is faint, so ducts and pipes are not mistaken for each other (D142).
- [x] 6. Heatmap colours run smoothly along pipes instead of jumping at joints (D143).

## Phase 6 · Full content

Scope, set by the user at the Phase 5 gate (2026-11-04): roadmap items 1–8 go into the Early Access launch, in full (D132). Item 9, mobile readiness, is dropped (D133). Task numbers below are the roadmap's. Sub-items come from the spec file named on each task. Spec files: `03`–`11`, `13`, `15`.

Started 2026-11-07.

### Agreed with the user (2026-11-04)

- [x] **Build order (D135):** 0 → 4 and 6 → 3 (shell, no heating yet) → 1 → 2 → 3 (finish, tier 4 opens) → 5 (without the lab branch) → 8 → 7 (with the lab branch, tier 5 opens) → balance and phase end. A playtest build after tier 4 opens and after events; neither is a gate.
- [x] **The gate (D136):** from a new city, tiers 4 and 5 can be reached and brought online without dev tools; heating and cooling read clearly on the temperature heatmap; a power outage feels fair and each protection visibly helps; a special-gas leak feels different from natural gas; every tier and the prestige pace feel right; the user agrees it is ready for Early Access.
- [x] **Radiators in tier 3: yes (D137).** Tier 3 needs rebalancing, and tier 3 buildings in old saves are rebuilt with a refund. Hot taps stay tier 4+, so the tier 3 water heater has no tier 3 outlet to feed.
- [x] **Slow ageing on (D134).**
- [x] **No mobile readiness pass (D133).**

### Tasks

- [x] 0. Foundations for the new networks (`05` §1.5, §4; `01`; `12`)
  - [x] One graph per fluid system: air exhaust, hot water, heating loop, each special gas (`networks.json`, D144). Toolbox entries, fluid colours and line patterns (dashed and double drawn now, `06`). The toolbox lists a building's networks; the parts for the new ones come with their tasks.
  - [x] Coupling nodes and solve order: hot → cold demand, heat → gas demand, iterate twice (D148, D149). Devices with ports, placed on room floors; `water-heater` and `gas-boiler` data. Placing them from the toolbox comes with task 1.
  - [x] Thermal solve on all water networks, with loops iterating 3 times; `hot-water` and `heating-water` on the water tables (D150).
  - [x] Outlets measured in watts (`02`), room loads at design temperature −5 °C and +32 °C (`13`; D145–D147). `radiator` and `cooling-terminal` in `outlets.json`; generating them comes with tasks 1 and 2.
  - [x] `events.json`, `inspections.json`, new balancing sections, definitions and validator rules (D152).
  - [x] Diverging palette for temperature relative to setpoint (`11`, D151).
  - [x] Save version 5 and generator version 5, migration step and sample save test; old saves rebuild tier 3 with a refund (D154).
- [ ] 1. Hot water and heating, tier 3–4 (`05` §4, `07`, `09`, `03`)
  - [ ] `water-heater` (cold in, hot out at 60 °C), `electric-heater` (`07`; the roadmap does not list it).
  - [ ] Hot water storage tank in the water heater (D153, the user's pick): propose the tank model before building it.
  - [ ] `hot-water-tap` outlets (bar and ≥ 55 °C) in bathrooms from tier 4.
  - [ ] Circulation loops: return runs with `circulation-pump`, heat loss along them.
  - [ ] Legionella code, with one passing and one failing test.
  - [ ] District heating plant in use: `heating-main` family (80, 125, 200 mm, U 0.3), 6 bar and 90 °C, routing, temperature along mains, heating connection opening.
  - [ ] `district-heat-exchanger` (ε-NTU), `gas-boiler` (flue on an exterior wall, gas demand from heat output).
  - [ ] Radiators (ε formula, heat into the room, pay factor 1.5), generated in occupied rooms; heating loops with an expansion vessel.
  - [ ] Boilers and heaters count as heat sources for gas separation (D122), boilers as ignition sources; boiler rooms need 1.0 ACH (`09`).
  - [ ] Combustion CO₂ from gas appliances (D125).
  - [ ] Condensation code: dew point from room humidity, cold water at 12 °C (D80), with tests.
  - [ ] `cold-climate` heating demand ×1.5 (D125).
- [ ] 2. Cooling, tier 4 (`05` §4, `07`)
  - [ ] Air exhaust networks: exhaust and high-level grilles, room balance, fans on supply or exhaust.
  - [ ] `aircon-split`: COP formula, outdoor unit on an exterior wall, refrigerant line ≤ 15 m with 1% loss per metre past 7.5 m.
  - [ ] `heat-pump`, heating and cooling modes.
  - [ ] `cooling-terminal` outlets (W, pay factor 1.8) in offices and wards.
  - [ ] `heat-recovery-unit` and `hr-unit-high-eff`, with both networks running through them.
  - [ ] `industrial-ahu` slots: filter, heat recovery, `heating-coil`, `cooling-coil`.
- [ ] 3. Tier 4 (`04`, `07`, `13`)
  - [x] Warehouse and office: footprints, L and U shapes, 40% chance of a basement, room programs (D163), codes, occupants and machines. Offices 2–4 floors on ≤ 24 × 16 m, warehouses 1–2 floors (D162).
  - [x] Auto-router and reference layouts with T4 equipment; solvability over 200 seeds (fall back a tier if it fails, `04`). Router fixes (D164), deeper ceilings from tier 4 (D165). The gas manifold joins the references when it is built.
  - [x] Indoor air quality at generation (D155): occupied rooms get vents for their people, the router aims at each room's CO₂, and the solvability check includes the code.
  - [ ] Wall cavity layer: pipes ≤ 22 mm, capacity 1 per wall tile edge, `L` cycles to it.
  - [ ] `transformer-room`: 250 kW, 3×3 room on the ground floor or basement, industrial only.
  - [ ] `smart-building-controller`: re-balances every 30 s and once at offline catch-up, at most 40 solves, with a change log. It is tier 3 in `07`, so tier 3 buildings can use it.
  - [ ] `gas-booster-compressor` and its inline variant, `gas-manifold` (6 valves, a detector, counts as accessible shutoffs), `vs-pump-set`.
  - [ ] `gas-regulator` setpoint adjustable 18–30 mbar from tier 4 (`07`; D110 covers the fixed 25 mbar only).
  - [x] Slow ageing on, tier 4+ (D134, rate in D166): `failure.ageingRate` applies to every tier today, so add the tier limit, pick and log a rate (`08` gives none), and test it. Every aged segment then has stress building, so check offline catch-up stays within budget, and gas pipes can now reach a leak through ageing.
  - [ ] Tier 4 opens (D104 lifts): unlocks, tree, milestone and tests that assume tier 3 is the top.
- [x] 4. Indoor air quality code (`09`, `05` §3)
  - [x] 1 000 ppm room mean CO₂: every air outlet in that room pays 0, on the sheet and in the code list, with tests. Tiers 4–5 (D155); zone "CO₂ too high" (D156).
  - [x] Room-level CO₂ for background buildings and offline, so the code holds while the building is not open: each room's well-mixed steady state, the same everywhere (D158). People's CO₂ no longer pools in the lower layer of the room grid (D157).
- [ ] 5. Events (`10`, `12`, `11`)
  - [ ] Scheduler at 1 Hz: mean 3 h, 45 min gap, one per district and two city-wide, seeded; only targets where it makes sense.
  - [ ] Calm banner with "Show me", log of the last 20, "temporarily raised" tag on outlets.
  - [ ] Heatwave, cold snap, plant maintenance, power outage, new tenant, city festival, with `10`'s durations, effects and rewards; `eventMultiplier` in income.
  - [ ] Offline: events can start, bonuses apply analytically, every timer pauses, the away report lists them; catch-up gains its event stop.
  - [ ] Outage, gas branch: background leak per fitting, combustion CO₂, safety window 600 s, then the normal escalation; passive vents and auto-shutoff help.
  - [ ] Outage, flood branch: groundwater, `sump-pump` generated in basements, blue fill, 0.3 m floods a panel unless raised (+25%), "Flooded panel" until repaired even after power returns.
  - [ ] Outage, lab branch: built in task 7 (tanks are tier 5).
  - [ ] `backup-generator` 5, 20, 100 kW: 5 s start, `safetyCritical` first, fuel cost. Water hammer on outage (`05` §6).
  - [ ] Cascade tests with every protection on and off; offline equivalence including events.
- [x] 6. Inspections (`10`)
  - [x] Random inspection: 0.08 per hour per online building, inspector icon, pass ×1.25 for 60 min, fail fine and 30 min deadline (pauses offline), second fine and offline if missed. In game only; the bonus runs on while away (D161).
  - [x] Energy rating A–G from `P_actual / P_reference`, pay multipliers in income, recomputed on Commit, "Projected rating: B" in design mode, on the status line. Reference scaled by delivery³ (D159); details in D160.
- [ ] 7. Tier 5 (`03`, `04`, `06`, `07`, `08`, `09`, `10`)
  - [ ] Factory, lab and hospital: footprints, 2–10 floors, basements, lab and ward programs; solvability over 200 seeds; tier 5 opens.
  - [ ] Propane: fluid, `propane-outlet`, pools low, basement code. **The spec names no source for propane**; decide and log it when building.
  - [ ] `gas-burner-outlet` (industrial, 100–300 mbar).
  - [ ] Special gases: oxygen, nitrogen, CO₂ and hydrogen outlets, `special-gas-manifold`.
  - [ ] Industrial gas depot in use: `special-gas-main` (25, 50 mm stainless), 10 bar, fines ×2, none within 2 tiles of residential plots.
  - [ ] Purity: material −2% per segment, dead legs −0.5% per metre capped at −5%, missing check valve −10%; below requirement counts as below minimum.
  - [ ] Hazards: oxygen enrichment as a leak with blast ×0.5, asphyxiation zones (O₂ < 19.5%), CO₂ toxicity (> 5 000 ppm), hydrogen's range; species picker complete (D124).
  - [ ] Codes: propane, hydrogen high-level ventilation, backflow prevention, each with tests.
  - [ ] `backflow-preventer`, `filter-basic`, `filter-hepa`, `clean-air-vent` outlets that need HEPA.
  - [ ] Gas storage tanks and `lab-chiller`: warming, pressure from the tables, relief into the room unless a vent line runs outdoors. The outage's lab branch (`10`).
  - [ ] Compressible correction for high-pressure lines (`05` §1.3).
  - [ ] Blast energy from each gas's own heating value.
- [ ] 8. City upgrades (`03`, `07`, `09`, `13`)
  - [ ] Substation upgrades 2 → 5 → 12 MW.
  - [ ] Booster stations on a main: water +3 bar, gas +2 bar, drawing from the nearest substation.
  - [ ] HP gas mains in play (7 bar, tier 5; D107).
  - [ ] Historic foundations code: routes already pay the fine and drilling already avoids it (`Game/City/Utilities.cs`). Still needed: a passing and a failing test (`15`), the city codes in `codes.json` (`09`), and a check of why the Phase 5 report calls it unreachable.
- [ ] Balance (`13`)
  - [ ] Balancing report covers tiers 4 and 5: payback 40 and 80 min, electricity 10–25%.
  - [ ] Base pay for T4 and T5: `balancing.json` still has `13`'s $30 and $180, a 40× jump from T3's confirmed $0.75 (D109). Re-cut them and log it.
  - [ ] First prestige still 15–25 h now that T4 and T5 plots count toward the 60% (D108); measure and retune.
  - [ ] T3 payback, still 3.5 min against 20 (D109), with heating in its cost (D137).
- [ ] Performance (`01`): budgets with tier 5 buildings and a full city. 10-floor room grid < 2 ms, 2 000 segments < 5 ms, city solve < 10 ms, 72 h catch-up with 100 buildings < 2 s, memory < 1.5 GB, frames under 8 ms. Up to 3% of frames were already over at the end of Phase 5, with smaller buildings.
- [ ] Spec sweep: read every spec file against the game for anything named and still missing (this is what `14-roadmap.md` means by "everything in the spec"), and fix or report each one. The Touch map is out (D133).
- [ ] Early Access material: store text, capsules and screenshots remade to show the new content; checklist updated. The demo stays at tier 2 (D126).
- [ ] Phase end: all tests green, 10-minute session clean, data validator clean, Windows build in `Builds/phase-6/`, `docs/dev/phase-6-report.md`.
- [ ] Gate playtest by the user.

Not in this phase: mobile readiness (D133); achievements and a trailer (optional or not in the spec, on the Early Access checklist).
