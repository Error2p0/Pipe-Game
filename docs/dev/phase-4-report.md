# Phase 4 report · Water, floors and power

Status: **gate passed 2026-11-04.** Decisions confirmed by the user; no 45° drawing (D106). Everything is merged to `main` from `phase-4/water-core`. The build is `Builds/phase-4/HeadLoss.exe`.

## What was built

| # | Task | Where |
| --- | --- | --- |
| 1 | Water with its own property table; PVC, copper and steel with a wall per size, a rating per size and a wave speed; pipe sizes 15–108 mm | `materials.json`, `pipes.json`, `MaterialDef.RatedPressure` |
| 2 | Taps as demand nodes (delivery is the pressure left at the tap); mains pressure only at the water connection; small booster, multistage and inline booster pumps | `NetworkCompiler`, `equipment.json` |
| 3 | Buildings with floors: stairwells, shafts, risers only in shafts, ghosts of the floors above and below, floor tabs and PageUp/PageDown. Tier 2–3 buildings generate with water; the auto-router works in 3D | `FloorPlanGenerator`, `AutoRouter`, `BuildingViewState` |
| 4 | Layers: floor and ceiling (L, or the buttons); ceiling capacity units shared by both networks; the wall cavity button stays grey until tier 4; core drilling through interior walls from tier 2; large equipment takes floor space | `DuctEditor`, `EquipmentFits` |
| 5 | City utilities: route tool with a live quote (cost, red tiles with reasons, resulting pressure), main sizes with capacity, recurring fines, crossing costs, directional drilling, corridors, branching, removal with a refund, city solve with capacity sharing, utilisation heatmap with a per-utility filter, roof icons | `Utilities`, `CityNetwork`, `CityHud`, `CityScene`, `CityRenderer` |
| 6 | Bursts: stress per pipe, Weibull hazard, weeping, stress heatmap and bar, income cut (0 in game, 25% offline), emergency and standard crews with an optional upgrade, shutoffs that isolate a burst, relief valves, regulators, check valves, maintenance contracts | `BuildingFailures`, `OfflineCatchUp`, `BuildingHud` |
| 7 | Water hammer on valve closes, pump changes, check valves slamming and bursts; a hover note ("Surge: +3.1 bar") and a calm ripple along the pipes; the pressure tank cuts surges | `Hammer`, `FlowParticles` |
| 8 | Codes: noise, tap pressure, no water above electrics, fire dampers. Red outlines and a list; clicking a row moves to it. Blocking codes keep the building offline | `CodeChecker`, `DuctRenderer`, `BuildingHud` |
| 9 | Electricity: basic connection until a cable arrives, substations, cables by kW, brownouts furthest first (fans and pumps slow down), panels, trip after 2 s (building offline until you reset it), connection kW in the readout | `CityNetwork`, `BuildingSession.TickPower` |
| 10 | Leak sensors (alert with the expected time to a burst), engineering software (24 h stress forecast and time to 50% burst chance), insurance | `BuildingHud`, `CityHud` |
| 11 | Unlocks to tier 3; the shop lists water movers, panels, pipe sizes and materials, and services | `Catalogue`, `Tech` |

Carry-over from earlier phases: ceiling capacity replaces one duct per tile (D8); stress readout and heatmap (D15, D64); the noise code runs in the code system (D37, with the limit question in D89); large equipment takes floor space (D62).

Also added: the offline timeline now stops at sampled bursts and finished repairs, and charges fines, premiums and crew bills, all listed in the away report. There is a water layer in the audio, and pumps hum.

## Tests

EditMode: 296 of 296 pass. PlayMode: 27 pass, 0 fail, 2 skipped (input-package tests the package itself marks unstable). The data validator passes. New this phase: `ControlValveTests`, `WaterNetworkTests`, `FailureTests`, `CodeTests`, `CityUtilityTests`, `OfflineFailureTests`, a route-tool drag test and a city solve performance test.

The reference cases from `15-testing.md` that apply are in: T8 (Joukowsky), T12 and T13 (Weibull). A Monte Carlo test confirms the median sampled burst time matches the forecast within 5%, which is the fairness the gate asks about. Another confirms stress builds at the same rate in game and offline.

Performance: city solve for 500 segments has a median of 2.2 ms (budget 10 ms). The building network solve for 2,000 segments sits right at its 5 ms budget in the Editor: it measured 5.05 ms once in a full run and passed when re-run alone, so that test can fail occasionally on timing. A player build is faster. The 72 h catch-up test now solves real water networks and stays inside its budget. The 10-minute play session left the console clean: 10 minutes with 12 built buildings and 10 water mains running in the city view, the first 2 minutes also switching views by script, with 0 errors and 0 warnings. Nobody played by hand, so your playtest is the first real test of the input feel. The Windows build started and stayed responsive for 25 s at the main menu with no exceptions in its log; I did not play a session in the build itself.

## Balancing report (`Head Loss/Balancing Report`, 20 generated buildings per tier)

| Tier | Mean cost | Mean net | Payback | Target | Electricity |
| --- | --- | --- | --- | --- | --- |
| 1 | $106 | $0.25/s | 7.2 min | 5 min | 15% |
| 2 | $883 + $1,000 main | $4.20/s | 7.9 min | 10 min | 13% |
| 3 | $3.64K + $1,000 main | $36.05/s | 2.3 min | 20 min (**off target**) | 17% |

Tier 3 pays back far too fast against the target. Pay cannot fix it, because lower pay pushes electricity above 25% (D91). I'd look at it again when gas joins tier 3 in Phase 5, since gas will add cost. Tell me if you would rather raise tier 3 prices now.

## How to playtest (gate)

1. Run `Builds/phase-4/HeadLoss.exe`. **A save from an earlier build rebuilds its tier 2 buildings**: what you spent on them is refunded and they start fresh (D83). Tier 1 keeps its ducts. **New city** works too.
2. Get to tier 2 as before: two tier 1 buildings online at once.
3. **Route water.** In the city, pick **Water**, a size, then **Route**. Drag from the treatment plant (the blue block with the drop) to a family house and release. While dragging you see the cost, the resulting pressure, and red tiles with their reasons. Try a shortcut across other plots, then the same route with **Drilling on**.
4. **Plumb it.** Open the house. Click the **Water** tab, press **L** for the ceiling, and drag from the water connection to each tap. Upstairs taps need a riser: pipes may only change floor in the shaft. Select a pipe end in the shaft and click **Riser up**, or draw there. Commit.
5. **Read the taps.** A tap close to the plant can sit above its code maximum and pay nothing (D105). Click a straight pipe, add a **Pressure regulator**, and set its setpoint with the slider.
6. **The gate: push it over its rating.** Draw a trunk in 35 mm PVC (rated 3.8 bar; larger PVC is rated lower still). Click the water connection and buy the **Small booster pump**. Commit. Press **7** for the stress heatmap and click the trunk: the bar, the pressure as a multiple of its rating, and the stress are all there. Expect roughly an hour to a likely burst at about 1.4 × rating. At 1.1 × it takes hours, so use the larger sizes for a shorter session.
7. **Forecast.** Once tier 3 is unlocked, buy **Engineering software** in the city. Selecting a pipe then shows the time to a 50% burst chance, and pending designs show stress after 24 h. See whether bursts come roughly when it says.
8. **Limit the damage.** Add a **Shutoff valve** upstream of a branch. When that branch bursts (or just to test, close the valve), its taps stop paying but the building stays online. Add a **Relief valve** set just under the rating and watch the pressure stop climbing. Without these, a burst stops the building's income until a crew finishes.
9. **Repair.** Click the burst pipe: **Emergency** (3 min, 5 × the price) or **Standard** (2 h), with an optional better material in the same visit. Try the **Maintenance contract**, close the game, and come back: the away report lists bursts, repairs and the crew's bill.
10. **Surges.** Close a shutoff quickly on a busy line: a ripple runs along the pipes and the readout shows the surge. Add a **Pressure tank** and compare.
11. **Power.** At tier 3, route a cable from a substation (Power tab). Overload a thin cable with two buildings: the furthest one browns out first. Press **Utilisation** in the city and filter by Power. Put a panel under its load by adding pumps: after 2 s it trips, the building goes offline, and the power connection offers **Reset panel**.
12. **Codes.** Run a water pipe over the electrical room, or a duct through a fire wall without a fire damper: red outline, list entry, building blocked. Click the list row to jump to it.

## Gate checklist (from `14-roadmap.md`)

- [ ] Push a water network above rating on purpose and see stress build visibly before any burst.
- [ ] Bursts happen roughly when the forecast says.
- [ ] Relief valves and shutoffs clearly limit the damage.
- [ ] Bursts feel fair, never arbitrary.

## Screenshots

`docs/dev/screenshots/phase-4/`: `city-mains.png` (mains and lit buildings), `city-route.png` (the route tool refusing a full tile, on the utilisation heatmap), `building-water.png` (a family house, water network, pressure heatmap), `building-stress.png` (stress heatmap, weeping labels, forecast in the readout), `building-burst.png` (a burst with the repair panel), `building-tier3.png` (a restaurant's middle floor, flow heatmap).

## Known issues

- **Tiers 4 and 5 do not unlock yet.** There are no buildings for them (D104).
- **Booster stations and substation upgrades** are Phase 6 task 8, as the roadmap says, so a long main and a substation's capacity cannot be improved yet.
- Weeping labels can overlap outlet labels in tight rooms; zoom in or select the pipe.
- The route tool draws lines sharing a tile on top of each other (water offset from power, but two water lines overlap). The main's panel tells you which one you selected.
- Saves and settings live in `AppData/LocalLow/Elevate Studios/Head Loss/saves/`: the product name changed after D70 was written. If you set up Steam Auto-Cloud with the old `Pipe Game` subdirectory (D69), point it at `Head Loss`. Saves from before that rename are still in the `Pipe Game` folder.
- Parts still cannot be moved, only removed and placed again.

## Needs your confirmation

The big ones first:

1. **D91 · Tier 3 base pay $1.10** (13 says $5), payback still well under target, as above.
2. **D105 · Taps near the plant start above their code maximum**, so the first water building often needs a regulator. Is that the right first lesson, or too strict?
3. **D96 · Contract crews charge their normal price** on top of the 2% fee. Otherwise emergency is always the right choice.
4. **D106 · 45° drawing:** build it in a later phase, or drop it from the spec?
5. **D83 · Old saves rebuild their tier 2 buildings** with a refund.
6. **D89 · Noise limit:** the 09 face velocity limits are far above the 13 vent maxima, so the outlet maxima stay.

Smaller choices, all logged in `docs/dev/decisions.md`: D74–D77 (pipe tiers, prices, ceiling capacity, core drilling), D84–D85 (floor plans and programs), D90 (starved pressure ∝ share²), D92 (2 lines per tile, 4 per corridor), D93 (cable tiers), D94 (fines by destination tier), D95 (power heatmap is the utilisation filter), D98 (weeping is visual), D99 (heatmap per network, water in L/s and bar), D100 (what is paid at once), D102 (no shafts in one-floor buildings), D103 (drilling on top of the pipe price), D104 (tier 4 waits for its buildings).

`[confirm]` items from `00-index.md` that apply now and are unchanged: budget cap, overdelivery cap, unlock counts (2/3/3/2), offline cap 72 h, historic foundations fine (5 × under-house).
