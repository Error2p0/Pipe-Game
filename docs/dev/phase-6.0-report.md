# Phase 6.0 report · Foundations (task 0)

Session of 2026-11-07. Phase 6 task 0 is done, tested and merged into `main` (not pushed). It is not a gate: Phase 6 has one gate at its end (D136). The next session, 6.1, starts with tasks 4 (indoor air quality) and 6 (inspections and energy rating), as agreed in D135.

## What was built

| Item (`progress.md` task 0) | What it does | Where |
| --- | --- | --- |
| One graph per fluid system | Building networks are a data-driven list: air supply, exhaust, cold and hot water, heating, natural gas, propane and the four special gases. A network's id equals its fluid's, except `air-exhaust`. Each network has its display units, opening, tier, and the drawing tool's default size and material. Dashed and double line patterns are drawn; dotted already was. | `Data/networks.json`, `NetworkDef`, `BuildingState`, `NetworkSnapshot`, `NetworkCompiler`, `DuctRenderer` |
| Coupling nodes and solve order | Devices stand on a room floor and connect to several networks through ports. A device's child network is solved before its parent, twice over: hot before cold, heating before gas. A water heater makes the cold side carry the hot taps' draw, and its hot side takes the cold inlet's pressure. A boiler burns `P_heat / (η · LHV · ρ)` of gas. Electric heaters draw heat ÷ efficiency. | `Devices.cs`, `Coupling.cs`, `BuildingSession.SolveState`, `Pricing`, `Commands` (`PlaceDeviceCommand`, `RemoveDeviceCommand`, `SetDeviceCommand`) |
| Thermal solve on water networks | Temperatures along every water, hot-water and heating pipe, with each solve. Inputs are room temperatures, insulation, heaters with setpoints and capacity, and radiator-style UA links. Loops relax 3 times, warm-started from the last solve. Pipe heat goes into the rooms, and temperatures feed back into the flow solve (viscosity). | `NetworkThermal.cs`, `PipeThermal.cs`, `BuildingClimate.PipeHeat` |
| Outlets in watts, room loads | `radiator` and `cooling-terminal` outlets are paid in W, rated at the setpoint. Room heating and cooling loads are figured at −5 °C and +32 °C. Cooling terminals deliver `ṁ · c_p · (24 °C − T_supply)`. | `outlets.json`, `RoomLoads.cs`, `HeatDelivery.cs`, `BuildingEconomy` |
| Data for events and inspections | `events.json` holds the six events of `10`. `inspections.json` holds the inspection kinds and the energy rating bands A–G. New balancing sections: `events`, `outage`, inspection and fine numbers, climate setpoints and humidity, background gas leak. All have validator rules. | `EventDef.cs`, `BalancingDef.cs`, `DataValidator.cs` |
| Diverging temperature palette | Temperature reads blue (colder), white at 22 °C, red (warmer), widening evenly. It is a setting ("Temperature vs setpoint"), on by default. | `HeatmapState`, `ViewPalette`, `SettingsScreen`, `PlayerSettings` |
| Save and generator version 5 | Networks are saved as a list, and devices are saved. The v4 → v5 migration moves the old `air`/`water`/`gas` fields. A v5 sample save is in the tests. Generator 5 rebuilds tier 3 buildings with a refund (D137). | `SaveFile.cs`, `SaveStore.Sanitize`, `GameLoop.RegenerateOlder` |

Also:
- The generator script `tools/equipment/fan_curves.py` now writes `water-heater` and `gas-boiler`, and the circulation pump fits hot water and heating, not cold water.
- `01` lists `networks.json`, and `13` has the new defaults.

## A fault found and fixed

The Phase 1 temperature code mixed only the inflows it had already computed in a pass, so a loop never settled. Air ducts have no loops, so nothing visible changed. Loops are now ordered from the heater's outlet and warm-started (D150).

## How to look at it

The player can see little of this yet: the new networks get their parts and outlets in tasks 1, 2 and 7.

1. Open a built building and press `4` (temperature). Rooms and ducts read on the blue–white–red ramp, and the legend centre is 22 °C.
2. In Settings, switch "Temperature vs setpoint" off. Temperature goes back to the chosen palette.
3. Switch networks in a water building. The legend names the network ("Cold water (solid line)").
4. Load a Phase 5 save. Tier 3 buildings come back rebuilt with their spending refunded, and other tiers keep their edits.

## Tests

- EditMode 424 of 424 pass.
- PlayMode 47 pass. The 2 skipped are Unity's own input tests, skipped as before.

New test files:
- `HeatOutletTests`: loads, cooling terminals, the radiator formula.
- `PipeThermalTests`: water warming, insulation, feedback, heat into rooms.
- `DeviceCouplingTests`: placement, ports, removal, solve order, water heater, boiler.
- `ThermalTests`: heating loop energy balance, heater capacity.
- `TemperatureRampTests`.
- `SaveTests`: v4 migration, network order, generator 5.

A play-mode check with a 30-building city showed the temperature and water views drawing correctly, with no console errors or warnings.

## Decisions

All confirmed by you on 2026-11-07: D144, D145, D146, D147, D148, D151 and D152, and from Phase 5.5 D139, D141, D142 and D143. No decision is left waiting for confirmation.

- **D153 · your pick: option 1, water heaters store hot water in a tank.** A 3 kW heater cannot heat a steady 0.10 L/s tap to 55 °C. One thing to keep in mind for task 1: taps in the game draw their design flow all the time, and a tank under a steady draw only delays the cold water. So the tank model has to give the heater credit for the time between draws (tank volume, recovery rate and the draw pattern it assumes). I will propose the exact model in task 1 before building it.
- D149, D150 and D154 needed no confirmation: they are implementation (solve order, where the thermal solve runs, save versions).

## Known issues and notes for 6.1

- Devices cannot be placed from the toolbox yet, and the toolbox has no parts for the new networks. Both come with task 1, where the water heater and boiler become playable, and task 2.
- Radiators and cooling terminals are in the data but not generated in buildings yet (tasks 1 and 2).
- The generator version is already 5, before tier 3 radiators exist. A development save made before task 1 lands will not get the tier 3 rebuild when radiators arrive.
- `Showcase.BuiltCity` left 2 of 18 water buildings without a main in the test city: the helper does not route to every plot. This is not a game fault.
- Git Bash in this environment fails on long `python - <<EOF` heredocs. Write the script to a file with the Write tool and run it (already in `unity-cli-notes.md`).
