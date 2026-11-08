# Phase 6.1 report · Indoor air quality and inspections (tasks 4 and 6)

Session of 2026-11-08. Phase 6 tasks 4 and 6 are done and tested on `phase-6/air-quality`. It is not a gate: Phase 6 has one gate at its end (D136). The next session starts task 3 (the tier 4 shell, no heating yet), as agreed in D135.

## What was built

| Item | What it does | Where |
| --- | --- | --- |
| Indoor air quality code | In force at tiers 4–5 (D155). A room whose CO₂ is above 1 000 ppm gets nothing for its air outlets: they show "CO₂ too high" and pay 0, and the building stays online (D156). The room goes on the code list with its vents outlined, the CO₂ it settles at and the fresh air its people need. | `codes.json`, `IndoorAirQuality.cs`, `BuildingSession`, `BuildingHud` |
| Room CO₂ for the code | Each room's well-mixed steady state, `420 ppm + G / Q` from its built ventilation, the same in game, offline and in design previews (D158). A fix pays as soon as it is committed. When a building loads, its room grid starts at that steady state. | `IndoorAirQuality.SteadyPpm`, `BuildingClimate.SeedCo2` |
| CO₂ in the room grid | People's CO₂ no longer pools in the lower layer (D157). It used to climb toward 10 000 ppm in every occupied room; now a room settles near the well-mixed value in about four hours. The CO₂ heatmap was wrong in every tier until now. | `RoomGridReference`, `BurstRoomGridStepper` |
| Energy rating A–G | Score = power ÷ the reference layout's power at the building's delivery, scaled network by network with delivery³ (D159). Worked out from a steady solve when what is built first comes online after a Commit, kept until the next Commit (D160). The grade multiplies pay (A ×1.15 … G ×0.80). Design mode shows "projected rating: B". The status line and the city panel show the grade. The hand-cranked starter is not rated. | `EnergyRating.cs`, `BuildingSession.UpdateRating`, `BuildingHud`, `CityHud` |
| Random inspections | 0.08 an hour per online building, in game only (D161). A small figure walks up to the building in the city view over 10 s. A pass pays ×1.25 for 60 minutes. A fail charges a fine (10 min of gross, at least $50 × tier factor; insured ×0.5) and gives 30 minutes to fix the findings. Missing the deadline charges a second fine and keeps the building offline until fixed. A clipboard badge sits on the roof while findings are open. Notices are quiet one-liners. | `BuildingInspections.cs`, `InspectionState.cs`, `CityScene`, `CityRenderer`, `GameRoot` |
| Offline | No inspection starts while away. A visit in progress and a fix deadline pause; a pass bonus keeps running and its end is a catch-up stop. Rooms use their steady CO₂. | `OfflineCatchUp` |
| Save | Each building record has its inspection state and grade. The field is new; old saves get an empty one and are rated on load. No save version change was needed. | `BuildingRecord`, `SaveStore.Sanitize` |
| Dev key | F5 (development builds, or `-headloss-dev`) sends an inspector to the open building, since a visit comes about every 12 hours. | `BuildingInput`, `BuildingScene` |

## How to look at it

Tier 4 buildings arrive with task 3, so the air quality code cannot be seen on a real building yet; the tests cover it. Inspections and the rating work on every tier now:

1. Open any built building other than the starter house. The status line ends with "energy rating C (×1.00)" or similar.
2. Change a duct size without committing. The design bar shows "projected rating: …". Commit, and the status line takes the new grade.
3. Press F5 in a development build. In the city view, a small figure walks to the building; ten seconds later a notice says the inspection passed (×1.25 pay for 60 min), unless a vent is above its code maximum.
4. To see a fail: push one vent above its code maximum (the outlet list says "above code max"), press F5, and wait for the inspector. The fine is charged and the status line shows the deadline and what was found. Bring the vent down and commit: within a second the findings clear.
5. Press `4` and pick CO₂ in a lived-in building: rooms no longer show a deep pool of CO₂ near the floor.

## Tests

- EditMode: 445 of 445 pass (424 before this session, plus 21 new).
- PlayMode: 47 pass. The 2 skipped are Unity's own input tests, skipped as before.
- No compiler warnings.

New test files:
- `AirQualityTests` (9): the steady-state formula, cut and no cut, not in force, a fix paying at once on Commit, the seed on load, offline, the room grid settling near the well-mixed value with no pool in the lower layer.
- `InspectionTests` (12): rating bands, cubic and 1.5-power scaling, not rated without an electric reference or while offline, the reference rating itself near C, the projected rating and the re-rating on Commit, the starter not being rated, a pass and its end, a fail with its fine and fix, a missed deadline, visit rates over many hours, offline pauses and the bonus's end, the save round trip.

The Editor was closed this session, so tests ran in batch mode, and there was no play-mode screenshot check of the new UI (status line, design bar, city figure and badge). Please look at those when you open the game.

## Decisions

Decided by you this session: D155 (IAQ at tiers 4–5), D157 (CO₂ moves with the mixture's density), D158 (the code reads each room's steady state), D159 (the rating's reference scales with delivery³).

Waiting for your confirmation:
- **D160 · Rating details.** Rated from a steady solve; rated when first online after a Commit (old saves: on load) and kept until the next Commit; the hand-cranked starter is not rated; not online at steady state means not rated; the projection reads the design preview as it is.
- **D161 · Inspection details.** In game only, with the bonus running on while away; one visit at a time; the inspector checks the code list and outlets above their code maximum; a building that goes offline during the walk is skipped; findings clear as soon as nothing is found; fines use potential gross and insurance halves them; inspection rolls have their own seeded stream.

D156 needed no confirmation (it follows the operating-code rule in `09`).

## Known issues and notes

- Vent counts by occupants and the router's room-CO₂ target come with tier 4 generation (task 3, added to its checklist). Until then no generated building has the code in force.
- `DeviceRunDownTests.PanelTrip_ThingsCoastToAStop_IncomeStopsAtOnce_ResetRampsBackUp` failed once (flow 7% short after the ramp-up) and passed on every run since with no change to it. It compares flow after a timed ramp, and solves turn asynchronous when slow, so it looks timing-sensitive under batch-mode load.
- A reference layout is balanced right at its minimums, so most single changes to it take a building offline; that is why the rating test checks the mechanism rather than "wider ducts give a better grade".
