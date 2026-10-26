# 09 · Building codes and constraints

[doc] Codes turn layout into a puzzle: they limit where things can go and cap how hard the system can be pushed. Each building lists the codes in force; harder buildings add more.

## Two kinds of violation [impl]

- **Layout violations** (where things are): checked live in design mode and on Commit. [doc] Violations are shown live while building as red outlines with a one-line reason. A building with violations cannot come online.
- **Operating limits** (how hard it runs): the code maximum at outlets (`02-economy.md`) and room limits (CO₂, condensation). Exceeding one makes the affected outlet(s) pay 0 with a warning; it does not block the whole building, except where stated.

Each code in `codes.json`: `id`, `displayName`, `kind` (layout / operating / city), `tiers` where it is in force, building types that add it, parameters, one-line reason text, "why" text for the requirement sheet.

## Example codes [doc]

| Code | Rule | Real-world basis |
| --- | --- | --- |
| Noise | Max air velocity at vents in bedrooms and offices | Duct noise rises sharply with speed |
| Tap pressure | Max static pressure at taps | Fixtures and safety limits |
| Gas separation | Gas pipes at least N tiles from electrical panels and heat sources | Ignition risk |
| No water above electrics | No water pipe over an electrical room | Leak safety |
| Gas room ventilation | Rooms with gas appliances need minimum air changes | Combustion air and leak dilution |
| Fire dampers | Ducts crossing fire walls need a fire damper | Fire compartments |
| Accessible shutoffs | Every branch with gas needs a shutoff valve | Isolation for repair |

[impl] Parameters and kinds:

| Code | Kind | Parameters (defaults) | Check |
| --- | --- | --- | --- |
| Noise | operating | max face velocity 2.5 m/s bedrooms, 3.5 m/s offices | Vent face velocity = Q / A_grille. This is the code max of those vents (e.g. 90 m³/h on the example vent). |
| Tap pressure | operating | 5 bar | Tap delivery above → tap pays 0 |
| Gas separation | layout | N = 2 tiles | No gas segment within N tiles (Chebyshev, same floor) of the panel or a heat source (boiler flue, heaters, ovens) |
| No water above electrics | layout | — | No water/hot-water/heating segment on any ceiling tile above an electrical room or the panel tile |
| Gas room ventilation | operating, blocks online | 0.5 ACH min (kitchens), 1.0 ACH (boiler rooms) | Room ACH from the air network; below → building cannot come online |
| Fire dampers | layout | — | Every duct segment crossing a fire wall has a fire damper at the crossing |
| Accessible shutoffs | layout | — | Every gas branch (from each tee) has a shutoff valve or manifold valve before its first outlet |

## Advanced and city codes [doc]

| Code | Rule | Real-world basis |
| --- | --- | --- |
| Legionella | Hot water at taps must stay above about 55 °C; needs circulation loops and insulation | Legionella bacteria grow in lukewarm water |
| Indoor air quality | Max CO₂ in occupied rooms; ventilation scales with occupants, not just outlet flow | Ventilation standards |
| Condensation | Cold pipes through warm, humid rooms must be insulated | Dripping and mould |
| Propane | No propane equipment in basements | Heavier than air, pools low |
| Hydrogen | Rooms with hydrogen need high-level ventilation | Rises fast, collects at ceilings |
| Backflow prevention | Special-use buildings need a backflow preventer at the city connection | Protects the public water supply |
| Main separation (city) | Gas and water mains keep a minimum distance apart | Damage and contamination risk |
| Historic foundations (city) | No mains or cables under historic buildings; stricter than the normal under-house fine | Protected structures |

[impl]

| Code | Kind | Parameters | Check |
| --- | --- | --- | --- |
| Legionella | operating | 55 °C | Hot tap below → tap counts as below minimum (it is a requirement, not a max) |
| Indoor air quality | operating | 1 000 ppm | Room mean CO₂ above → every air outlet in that room pays 0 |
| Condensation | layout | dew point from room RH (`05-simulation.md` §4) | Uninsulated cold segment whose fluid temperature is below the dew point of a room it passes over |
| Propane | layout | — | No propane equipment or outlet on a basement floor |
| Hydrogen | layout | ≥ 1 high-level exhaust grille or high passive vent per hydrogen room | — |
| Backflow prevention | layout | building types: lab, hospital, factory | Backflow preventer on the water connection |
| Main separation | city | 1 tile | See `03-city.md` |
| Historic foundations | city | 5 × under-house fine [confirm] | See `03-city.md` |

## Constraints [doc]

- **Budget:** each building has an initial build budget. Equipment and pipe beyond it must be paid from income later. (Interpretation in `02-economy.md`.)
- **Ceiling space:** per-tile capacity (see `04-buildings.md`). Big ducts eat space, forcing smaller ducts with more fans, or smarter routing.
- **Fixed elements:** walls, structural columns, stairwells, and protected rooms that pipes cannot cross.

[impl] Walls: pipes cross walls only through door tiles or by "core drilling" a wall tile (small one-off cost, tier 2+) — not through fire walls without a fire damper, never through exterior walls except at entry openings, outdoor units and passive vents. Columns, stairwells (except their shaft tiles) and protected rooms block every layer.

## Presentation [impl]

- Red outline on the offending tiles/segments, and a small badge with the code icon. Hover/tap shows the one-line reason, e.g. "Gas separation: gas pipe 1 tile from panel (needs 2)".
- A violations list in the building panel; clicking a row pans to it.
- The Commit button stays enabled with violations (the player may want to save progress), but the building cannot come online until they are fixed; the status says "Blocked by 2 code violations".
