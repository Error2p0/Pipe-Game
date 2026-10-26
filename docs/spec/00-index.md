# 00 · Index, pillars, glossary

## What the game is

A relaxing, minimal top-down 2D idle/incremental game for PC (Steam), with mobile as a possible later port. The player designs pipe and duct networks inside buildings across a procedurally generated city, and earns money for every outlet that receives enough flow.

Working title: **Head Loss** (the engineering term for pressure lost to friction), the strongest name so far. Alternatives still under consideration: Laminar, Throughput, Mains, Undercurrent, Flowtown, Pressure Point. Keep the name in one place (`Data/game.json → displayName`) so it can change.

The hook: the physics is real. Friction, bend losses, fan and pump curves, temperature and fluid properties all follow engineering formulas. Switchable heatmaps let the player *see* why a design works or fails.

## Design pillars (apply to every decision)

- **Real but readable.** Every number comes from real physics, but the player reads it as colour on a heatmap, not as equations.
- **Calm by default.** Short, satisfying sessions. Open the game, improve one building, leave it earning.
- **Efficiency beats brute force.** A clean layout with a modest fan should beat a messy layout with a huge one, because electricity costs rise steeply with flow.
- **Each fluid adds new mechanics, not just bigger numbers.** Air teaches friction, water adds gravity and pressure, gases add danger.
- **The city is the progress bar.** Buildings light up as they come online, and the whole map visibly fills.

## Reading order

`CLAUDE.md` → this file → `14-roadmap.md` (find the current phase) → the files that phase lists → `13-balancing.md` for numbers → `15-testing.md` for tests.

## Notation

- **[doc]** = taken directly from the approved design doc. Must be built as written.
- **[impl]** = an implementation detail this spec adds so the doc can be built (formulas, data layouts, defaults). Follow it; it may be tuned.
- **[confirm]** = an interpretation the spec had to make. Build it as written, and list it in the phase report so the user can confirm or change it.

## Glossary (use these exact names in code and UI)

| Term | Meaning |
| --- | --- |
| City | One procedurally generated map with districts, roads, facilities and building plots. The hub and the level select. |
| Plot | A city tile area holding one building (or a ruin). |
| Building | A procedurally generated multi-floor tile grid with rooms, outlets and entry openings. One "level". |
| Requirement sheet | The list of outlets with min / ideal / code max, the budget cap and the codes in force, shown before the player starts. |
| Outlet | A generated end point that pays when it receives enough (vent, tap, stove outlet, radiator, …). Never placed by the player. |
| Entry opening | A generated start point on an outside wall where one utility network must begin (air intake, water/gas/heating/power connection). |
| Source equipment | The equipment placed at an entry opening (fan, pump, regulator, heat exchanger, panel). |
| Network | One connected graph of segments and nodes carrying one fluid inside one building. A building has one network per fluid system. |
| Segment | A straight run of pipe or duct between two nodes (an edge). |
| Node | A junction, fitting, equipment port, outlet or entry point. |
| Fitting | Elbow, tee, reducer, valve, damper: a node with a loss coefficient K. |
| Main | An underground city pipe (water, gas, heating, special gas). Never bursts; has a maximum flow capacity. |
| Cable | An underground city power line. A main with capacity in kW. |
| Facility | A central city building that is the source of a utility (water treatment plant, gas receiving station, district heating plant, industrial gas depot, electrical substation). |
| Connection point | Where a city main or cable meets a building's entry opening. |
| Online | A building whose every outlet meets its minimum and which has no blocking violations. Only online buildings earn. |
| Delivery | The measured quantity at an outlet (flow, pressure, heat, …) in its own unit. |
| Delivery ratio (r) | Delivery ÷ minimum. |
| Payout multiplier (m) | Multiplier on an outlet's base rate from its delivery ratio (see `02-economy.md`). |
| Risky fluid | Natural gas, propane, hydrogen, oxygen. Higher overdelivery cap. |
| Income cycle | The interval at which recurring fines and premiums are charged (`balancing → economy.incomeCycleSeconds`). |
| Stress | Accumulated overpressure damage on a segment; drives burst chance. |
| Ruin | A plot whose building exploded. Cannot earn, cannot be rebuilt (except Reconstruction permits). |
| Design mode | Editing a building without paying; the simulation previews the result ("simulate before you buy"). |
| Commit | Paying for and applying the design-mode changes. |
| Prestige / new city | Resetting the current city for permanent bonuses once the milestone is met. |
| Reputation | Prestige currency [impl]. |

## Assumptions to confirm (list them in each phase report where relevant)

1. [confirm] **Budget cap:** spending on a building before it first comes online cannot exceed its budget cap (paid from the player's wallet). After it is online, there is no cap. (Doc: "each building has an initial build budget. Equipment and pipe beyond it must be paid from income later.")
2. [confirm] **Overdelivery cap when code max is below 5× minimum:** the payout curve is compressed so the full cap (2× or 2.5×) is reached at the code maximum.
3. [confirm] **Historic foundations:** treated as a city violation with a much larger recurring fine (5× the under-house fine) rather than a hard block; directional drilling makes it legal.
4. [confirm] **Oxygen enrichment:** handled like a flammable leak (alarm, evacuation, timer), and at timer end causes a small fire-explosion (blast radius × 0.5) when an ignition source is present.
5. [confirm] **Offline events:** events can start while the player is offline; every catastrophe and deadline timer pauses offline (doc rule), and bonuses apply analytically.
6. [confirm] **Content after phase 5:** heating/cooling, hot water, propane, special gases, events, inspections and the power outage cascade are not named in the doc's 5-phase roadmap. This spec assigns them to Phase 6 (see `14-roadmap.md`) so nothing is lost. Ask the user at the Phase 5 gate whether any of it must move into Early Access launch.
7. [confirm] **Tech unlocks:** equipment tiers unlock when enough buildings of the previous tier are online (see `03-city.md` §Progression). The doc says new tech unlocks but not how.
8. [confirm] **Offline time cap:** offline progress is capped at 72 hours (tunable). Not in the doc; common idle-game protection.
