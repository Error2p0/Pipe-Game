# Phase 5 report · Gas and prestige

Status: **waiting for your playtest.** Everything is on `main`. The build is `Builds/phase-5/HeadLoss.exe`, the demo is `Builds/demo/HeadLoss.exe`.

Two things to know first:

- **I have not played crank to first prestige in one sitting.** The rules for each stage have tests, and the time estimate comes from the balancing report. The whole loop is the first thing your playtest checks.
- **Tier 3 still pays back too fast:** 3.5 min against a 20 min target (D109). It is the biggest balance question left, and I need your view after playing it.

## What was built

| # | Task | Where |
| --- | --- | --- |
| 1 | Natural gas with buoyancy in the network, gas regulator (25 mbar), LP and MP mains with their own graphs and table density for MP, the receiving station, separation violations | `fluids.json`, `pipes.json`, `NetworkCompiler`, `CityNetwork`, `Utilities` |
| 2 | Gas stoves in kitchens; apartment blocks and restaurants now need gas, and the auto-router lays it, with shutoffs and kitchen vents | `outlets.json`, `FloorPlanGenerator`, `AutoRouter` |
| 3 | Gas in the room grid: two layers, buoyant rise, mixing between floors through shafts; concentration heatmap with its species picker; the Burst stepper matches the reference | `RoomGrid`, `BurstRoomGridStepper`, `HeatmapState`, `RoomHeatmapRenderer` |
| 4 | Leaks from bursts, weeps, fittings and blasts; detectors; alarm and evacuation; the 10-minute timer and its pause offline; automatic shutoffs (also close on power loss); spark-free add-on; passive vents with wind; ignition sources | `GasSafety`, `OfflineGas`, `BuildingFailures`, `Commands`, `BuildingHud`, `EquipmentLayer` |
| 5 | Explosions: energy, radius, damage falloff, ruins, light damage to neighbours, chain reactions on their own timers, fines. Presented with a slow ring, a soft thump and a notice, with no flash or shake | `GameContext`, `CityRenderer`, `ProceduralAudio` |
| 6 | Gas codes (separation, accessible shutoffs, room ventilation) and the gas safety certificate | `CodeChecker`, `GasSafety`, `BuildingHud` |
| 7 | Prestige: milestone, Reputation, the six-node tree, Reconstruction permits, new cities with modifiers (cold climate, old buildings) | `Prestige`, `PrestigeScreen`, `TopBar`, `prestige.json` |
| 8 | Capsules and screenshots rendered from the game, demo build, Early Access checklist, store text draft | `SteamAssets`, `ReleaseBuilds`, `docs/dev/steam/` |

Carry-overs: table density for MP mains (D21), concentration heatmap's species picker (D64), tier 3 with gas (D101), and tier 3 rebalanced with gas (D91, now D109).

## Is gas fair? Measured

A real generated apartment block on its reference layout, a burst on the stove's 15 mm pipe, into a 65 m³ kitchen (`Showcase.LeakTimeline`). The timer is 10 minutes from the alarm.

| Case | Leak | Alarm | Reaches the flammable limit | Clears after the leak stops |
| --- | --- | --- | --- | --- |
| Burst, fans on, no detector | 5.7 m³/h | 0.9 min | never | 12 min |
| Burst, fans on, detector | 5.7 m³/h | 0.5 min | never | 12 min |
| Burst, fans off | 5.7 m³/h | 0.8 min | 2.8 min | 21 min |
| Fitting leak, fans on | 0.5 m³/h | never (peak 0.37%) | never | at once |

So working ventilation keeps a burst from ever becoming explosive, and losing it gives you the full 10 minutes to close a valve. That reads as tense but fair to me. Two things you may want different:

- **A detector adds little on a burst** (0.5 against 0.9 min), since air right next to the leak passes 50% of the limit almost at once. It matters on slower leaks and in rooms next to the source.
- **A small fitting leak in a ventilated room rings no alarm.** You see it only from its leak row and the heatmap.

## Tests

EditMode: 373 of 373 pass. PlayMode: 34 pass, 0 fail, 2 skipped (input-package tests the package itself marks unstable). The data validator passes. T16 (explosion radius) is in, and the 200-seed solvability test now includes gas in tier 3. New this phase: `GasTests`, `ExplosionTests`, `PrestigeTests`, `GasGridTests`, `Phase5Tests`, the v4 save sample, and a Burst agreement test with gas.

Performance: the budget tests pass (network 2,000 segments under 5 ms, city 500 segments under 10 ms, room grid under 2 ms, 72 h offline for 100 buildings under 2 s). The room grid one sits at its limit in the Editor: it measured 2.01 ms and failed once in a full run, then passed 5 of 5 alone and in the next full run, so expect it to fail now and then under load.

**Frames.** Game logic per frame at 60 fps, in the Editor, no rendering (`Showcase.FrameCost`), 600 frames twice:

| Live buildings | Mean | 99th percentile | Worst | Frames over 8 ms |
| --- | --- | --- | --- | --- |
| 40, before the fix below | 2.5 ms | 39 ms | 545 ms | 14 |
| 40 | 0.8 ms | 5–10 ms | 29 ms | 2–12 |
| 90, before the fix below | 3.6–5.7 ms | 70 ms | 1,258 ms | 15–17 |
| 90 | 2.0 ms | 11–19 ms | 77 ms | 11–17 |

The mean is well inside the 8 ms budget, but **up to 3% of frames still go over it**, and this table leaves out rendering. I think that is fine for a calm game with no twitch input, but watch for stutter in a large city and tell me. I have not measured the standalone build, which should do better than the Editor.

The freezes were found at this phase's end: every building stepped its rooms in the same frame, on the slow managed code, about 0.5–1.3 s every 5 s. Background buildings now run on Burst, spread over frames (D130).

**10-minute session:** 0 warnings or errors, on a live city of 40 buildings with a leak escalating for 4 minutes and a blast. That session ran before D130. After D130, the shorter frame measurements above were also clean, as were all tests.

## A bug found late

During the screenshot pass, one session threw NaN errors from the solver, once a second, right after a blast (judging by the timing). I could not reproduce it: about ten more live blasts and a test of 240 damage patterns stayed clean, so **the root cause is not proven**. The solver can no longer throw on non-finite numbers, and the building keeps its last good solve (D129). If you see a `Head Loss (dev report)` line in `Player.log`, please send it to me: it names the state that caused it. Before this fix, that error would have stopped the whole game tick for as long as it lasted.

## Balancing report (`Head Loss/Balancing Report`, 20 generated buildings per tier)

| Tier | Mean cost | Mean net | Payback | Target | Electricity |
| --- | --- | --- | --- | --- | --- |
| 1 | $106 | $0.25/s | 7.2 min | 5 min | 15% |
| 2 | $883 + $1,000 main | $4.20/s | 7.9 min | 10 min | 13% |
| 3 | $4.03K + $1,660 mains | $29.53/s | 3.5 min | 20 min (**off target**) | 20% |

First prestige estimate: **20.2 h** at 3 buildings an hour (target 15–25 h), with 57–65 buildings online and $30M. That is an estimate from reference layouts, not from play.

The report's other off-target line is the next tier's cheapest item: 22.6 min against 20. It has no effect yet, because tier 4 is not in this version.

## How to playtest (gate)

**Start with New city.** A save from an earlier build refunds and rebuilds its tier 3 buildings (D123), but only a new city tests the whole loop.

1. Run `Builds/phase-5/HeadLoss.exe` and pick **New city**. Play from the crank up to tier 3 as usual. Use no dev tools at all: this is what the gate is about.
2. **Route gas.** In the city, pick **Gas**, choose **LP** or **MP**, and **Route** from the receiving station to an apartment block or restaurant. Gas may not run right next to water or a substation: the quote shows red tiles and why.
3. **Plumb it.** Open the building and click **Natural gas**. Click the gas connection and buy the **Gas regulator**, then draw to each stove. Gas pipes are the yellow dotted lines. Stoves want 20–28 mbar. Commit.
4. **Read the codes.** Without a shutoff per branch or enough kitchen ventilation you get a red entry in the list, and the building stays offline. Clicking the entry moves you to the problem.
5. **Make a leak.** In normal play you cannot force one: gas pipes are rated far above any gas main until Phase 6's boosters, and fitting leaks are rare by the spec's numbers (0.0005 per fitting per hour, about one every 5 hours even with 40 gas buildings). So for this step **start the game with the `-headloss-dev` argument** (in a shortcut, or `HeadLoss.exe -headloss-dev`) and press **F4** in a gas building: it bursts a gas pipe on the floor you are viewing. This is the one dev tool in the test: play the loop itself without it. A leak gets a quiet alert and a panel with crews.
6. **Watch it.** Press **6** for concentration. Gas spreads from the leak and rises. The top of the scale is the lower flammable limit. Switch floors with PageUp and PageDown to see it travel up the shaft.
7. **The alarm.** The building evacuates and pays nothing, an alarm icon and countdown appear above the roof, and a soft chime sounds. **The gate: does this feel tense but fair and calm?** Close a shutoff upstream, send a crew, let the air clear.
8. **Let one explode, once.** Turn the fans off, or ignore the timer. Note a slow ring, a thump, a fine, a ruin, and damage to neighbours that plays out over minutes.
9. **Safety gear.** In the Safety group: a **Gas detector** (in a room), a **Passive vent** (on an outside wall), and **Automatic shutoff valves** (as a part on a pipe, stops the timer from starting). Select an electrical item in a gas room to add **Spark-free**.
10. **Close the game with a leak running.** On return, the away report lists waiting buildings at the top with **Go there**, and their timers have paused.
11. **Certificate.** A gas building's certificate lasts 48 h. Renew it from the building's panel (a 10-minute check). Expired, it pays half.
12. **New city.** The top bar shows your progress. Click it, or **New city** at the bottom left, to read the milestone and buy nodes. Found a new one: you keep Reputation and the tree, and later cities can roll cold climate or old buildings.
13. **Reconstruction permits.** With this node, select a ruin in the city and press **Rebuild**. It costs 20 × the reference price and leaves the building with no edits.
14. **Optional: the demo.** Run `Builds/demo/HeadLoss.exe`. Tiers stop at 2, gas says "Available in the full game", and new cities are off. It uses the same save folder as the full game, so back up your save if you care about it.

## Gate checklist (from `14-roadmap.md`)

- [ ] The whole loop from crank to first prestige plays without dev tools.
- [ ] Gas danger feels tense but fair and calm.
- [ ] You agree it is ready for Early Access.

## Question for this gate: what goes into the Early Access launch?

`14-roadmap.md` holds these for Phase 6. Which must be in the launch?

1. Hot water and heating (water heaters, circulation, boilers, radiators, legionella and condensation codes)
2. Cooling (aircon, heat pumps, heat recovery, exhaust networks)
3. Tier 4: warehouse and office, wall cavity, smart controller, gas booster, manifold
4. Indoor air quality (CO₂) code with occupants
5. Events: heatwave, cold snap, plant maintenance, the power outage cascade, new tenants, city projects
6. Inspections and energy ratings
7. Tier 5: factory, lab, hospital; propane; special gases
8. Substation upgrades, booster stations, HP gas mains, historic foundations
9. Mobile readiness

My suggestion is to launch without them apart from **5 (a few events) and 6 (inspections)**. They add variety to the content you already have without new networks, and tiers 1–3 otherwise have little happening once a city is built. But playing it is what should decide this.

The Steam side is in [early-access-checklist.md](steam/early-access-checklist.md) and [store-text-draft.md](steam/store-text-draft.md). Nothing has gone to Steam: the name, app ids, price, page and any trailer are yours.

## Screenshots

`docs/dev/screenshots/phase-5/`: `01-city.png` (lit city with mains), `02-air-velocity.png`, `03-water-pressure.png`, `04-gas-leak-concentration.png` (gas filling a kitchen), `05-alarm-countdown.png`, `07-ruin.png` (a deliberately oversized blast, to show ruins and damage), `08-prestige.png`, `capsule-header.png`. All from a showcase city built by a dev tool, hence the $499M. Store versions are in `docs/dev/steam/`.

## Known issues

- **Tiers 4 and 5 do not unlock yet** (D104), so the tree's and modifiers' later content is not reachable.
- **Cold climate** lowers the outside temperature, but its heating demand ×1.5 has nothing to act on until heating exists (D125).
- **The background leak and combustion CO₂** come with Phase 6's outage cascade and boilers (D125). The CO₂ heatmap works but has little to show.
- **Propane, hydrogen and O₂ excess** are greyed in the species picker until their gases arrive (D124).
- **HP mains** are in the data but have no use before booster stations.
- **The demo build uses the full game's placeholder Steam id** and the same save folder (D126).
- **No achievements** (optional in the spec). **No trailer** (not in the spec).
- Parts still cannot be moved, only removed and placed again. Outlet labels can still overlap on one network in a tight room; the outlet list has them all.

## Needs your confirmation

The big ones first:

1. **D109 · First city target $30M and tier 3 pay $0.75.** 13 says $500M and $5. At those numbers the first prestige is far beyond 13's own 15–25 h, and tier 3 pays back in under a minute. Payback is still off target at $0.75.
2. **D108 · Ruins count against the milestone.** 03 and 13 say both "60% of non-ruin plots" and "ruins count as not online". I went with the second, so a ruin costs progress.
3. **D126 · Demo stops at tier 2, with no gas.** That leaves out the game's most tense part. Ending at tier 3 would show it.
4. **D112 · Leak sizes** (burst 25% of bore, weep 5%, fitting 2%), which set the timeline above.
5. **D110 · Stoves vary ±5%** where other outlets vary ±10%, with a fixed 25 mbar regulator.
6. **D117 · Blast damage repairs:** 25% of the item's price, and contract crews handle them.
7. **D121 · Starter kit:** the first item of that kind placed in each city is free.
8. **D107 · Gas main prices:** LP ×1, MP ×1.5, HP ×2.5.

Smaller choices, all logged in `docs/dev/decisions.md`: D111 (stove count), D113 (30 s detection), D114 (automatic shutoffs), D115 (certificate price), D116 (spark-free minimum), D118 (ventilation code), D119 (empty plots, fines), D120 (offline gas), D122–D125, D127–D131.

`[confirm]` items from `00-index.md` that apply now and are unchanged: budget cap, overdelivery cap, unlock counts, offline cap 72 h, historic foundations fine (not reachable yet). Item 5 (offline events) waits for events. Item 6 is the launch question above.
