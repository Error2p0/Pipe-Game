# Phase 6.2 report · Tier 4 shell (task 3)

Session of 2026-11-08, on `phase-6/tier4-shell`, merged into `main` (not pushed). It is not a gate (D136). Per D135 this is the tier 4 *shell*: tier 4 buildings and parts exist and are tested, but tier 4 does not open to players until the "finish" pass, after hot water and heating (task 1) and cooling (task 2). That last item of task 3, "Tier 4 opens", is left open on purpose.

## What was built

| Item | What it does | Decisions |
| --- | --- | --- |
| Warehouse and office | Room programs, a basement in 40% of tier 4 buildings (storage, plant room), L/U shapes, fire walls, columns. Offices are 2–4 floors on at most 24 × 16 m (median 58 vents); warehouses 1–2 floors (median 30). Where the CO₂ code is in force, rooms get enough vents for their people. | D162, D163, D155 |
| Auto-router | A fully throttled damper now caps the fan; ducts cross fire walls straight on so a fire damper fits; from tier 4 the reference leaves ceiling room for pipes; fans too small for the total minimum flow are skipped (time only); the reference aims each room at its CO₂ limit. 200 seeds solvable at tiers 1–4. | D164 |
| Deeper ceilings | 6 capacity units per tile at tiers 4–5 (old buildings: one unit less on every tier), so a 500 mm trunk and a pipe share a tile. | D165 |
| Slow ageing | 8.3 × 10⁻⁸ stress a second on every segment from tier 4: roughly one burst every 5 days in a 300-segment building running all the time; weeping after ~2 000 hours. Edge keys are cached so the catch-up stays within budget. | D166 |
| Gas regulator setpoint | Adjustable 18–30 mbar in 1 mbar steps from tier 4, a slider in the gas connection's readout (live, like a valve). | — |
| Transformer room | 250 kW, 3 × 3 at the power connection, warehouses, factories and labs only; the generator keeps the floor space for it. | D167 |
| Gas booster compressor | At the gas connection (adds up to 50 mbar, $8 000) and inline (`gas-booster-inline`). | D168 |
| Gas manifold | Fitted on a gas tee or cross ($2 000): the accessible shutoff for every branch, a valve per branch (open and close from the readout), a built-in detector. | D168 |
| VS pump set | Its built-in pressure tank now damps surges. | D168 |
| Smart building controller | $3 000 a building, 20 W: every 30 s, on a worker, it nudges dampers, balancing valves and variable speeds for the best net income without breaking a minimum or a code maximum (≤ 40 solves); once at catch-up; a change log in the toolbox. | D169 |
| Wall cavity | On the Wall layer: pipes ≤ 22 mm along walls, one per wall tile, outside the ceiling's capacity; joined to the ceiling network at nodes; drawn thinner. | D170 |

## How to look at it

Tier 4 cannot be reached in play yet (that is the finish pass). What can be seen now:

1. The CO₂ heatmap no longer pools low in lived-in rooms (from 6.1).
2. The smart building controller in any tier 3 building: buy it in the toolbox's protection section, wait 30 s, and read the last changes under the button.
3. The dev helpers in `HeadLoss.Editor.Showcase` (`GenerationStats`, `GenerationTiming`, `GenerationPicture`) generate and route tier 4 buildings in the Editor.

## Tests

- EditMode: 466 tests; all pass. Two failures in the full run were looked at: a data rule that wrongly required a setpoint on the gas boosters (fixed), and `DeviceRunDownTests.SwitchedBackOn_MidStop_TurnsRoundFromWhereItIs`, which passed with all 18 run-down tests when rerun unchanged (a timing-sensitive test, like the one noted in 6.1).
- PlayMode: 46 pass, 2 skipped (Unity's own input tests). `RoomGridBurstTests.Burst_TenFloorBuilding_StepsUnderTwoMilliseconds` missed its limit once (2.02 ms against 2.0) and passed when rerun.
- New: `AgeingTests`, `GasRegulatorSetpointTests`, `TierFourEquipmentTests` (transformer, controller), `WallCavityTests`, manifold and booster tests in `GasTests`, the built-in tank in `FailureTests`, and tier 4 in the 200-seed solvability test (about 11 minutes).

No play-mode screenshots this session: nothing new is reachable in play before tier 4 opens.

## Decisions confirmed by you after the session (2026-11-08)

- **D163** · the warehouse and office room programs.
- **D166** · the ageing rate.
- **D167** · which buildings count as industrial (warehouse, factory, lab).
- **D168** · the gas manifold's form (fitted on a junction, branches named by direction).

D162, D165 and D170 were your choices this session; D164 and D169 are implementation.

## Known issues and notes

- Two timing-sensitive tests (run-down flow after a timed ramp, the Burst step budget) fail now and then under load and pass on rerun.
- Tier 4 buildings take about 1–3 s each to generate and route. The city view generates on a worker, but opening or loading a save builds them on the main thread; worth a look when tier 4 opens.
- The tier 4 solvability test takes about 11 minutes of the EditMode run.
- `13` lists the gas manifold among tier 4's reference equipment; the references keep a regulator and a shutoff per branch (D168).
