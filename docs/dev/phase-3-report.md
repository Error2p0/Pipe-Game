# Phase 3 report · Idle city

Status: **waiting for your playtest of the gate.** Branches `phase-3/city-gen`, `phase-3/city-view`, `phase-3/unlocks`, `phase-3/room-grid`, `phase-3/offline`, `phase-3/settings`, `phase-3/steam`, all merged to `main`.

## What was built

| # | Task | Where |
| --- | --- | --- |
| 1 | City from a seed: Voronoi districts (one of each kind; industrial leans to one edge), 2–3 main roads, a local road grid per district (every road joined to the main roads), 0–1 rail line along the industrial side, parks, protected sites, historic buildings in the oldest district, every facility (water plant, gas station, heating plant, gas depot, a substation per district) on its district's city-edge side, about 120 plots filled from the centre outward with a tier mix per district, the starter guarantee (3 tier-1 plots near the centre, one a solvable starter house), modifiers from the second city. Each step has its own sub-seed. | `Game/City/CityGenerator.cs`, `Data/city.json` |
| 2 | City view: map, roads with verges, blocks that light up with a slow 4 s glow when online and stay grey otherwise, later tiers pale, drifting cloud shadows, a "Start here" label. Hover shows the difficulty hint (footprint, floors, fluids, outlets, total demand, longest run, codes, 1–5 dots, budget cap); a click or tap selects and offers Open; double-click or Enter opens; Esc goes back to the city. A quiet chime when a building comes online. | `Runtime/CityScene.cs`, `Runtime/View/CityRenderer.cs`, `Runtime/UI/CityHud.cs` |
| 3 | Buildings from a seed (tiers 1–2, one floor, air only): footprint, rooms by BSP from the type's room program, a door tree, windows, vents by room type with ±10% requirements, the air intake (street side half the time), occupants and machine heat, codes in force. Types: starter house, small house, kiosk, family house, shop. | `Game/Generation/BuildingGenerator.cs`, `Data/buildings.json` |
| 4 | Auto-router and solvability: a trunk with a straight lead-in from the intake, branches to every vent, ducts sized by velocity limit and grown where a vent is short, long-radius elbows from tier 2, the tier's reference equipment, proportional damper balancing (and a speed drive at tier 2) so every vent sits in its band. Openings move, then rooms regenerate, then the plot drops a tier. Budget cap = 1.6 × the reference cost. The hand-made houses are test fixtures now. | `Game/Generation/AutoRouter.cs`, `BuildingFactory.cs` |
| 5 | Room grid per 05 §3 (0.5 m cells, two layers): vent sources and jets, room air balance through doors and walls, potential flow, advection, diffusion, heat, stratification, CO₂ from people, floor exchange. A CPU reference and a Burst version (one job per floor) that match within 1e-3. Room heatmaps for velocity, temperature and density; hovering a room shows its temperature, CO₂ and air changes per hour. | `Sim/Rooms/`, `Runtime/Jobs/BurstRoomGridStepper.cs`, `Runtime/View/RoomHeatmapRenderer.cs` |
| 6 | Thermal model: outside temperature from your local clock (15 °C ± 6, warmest at 15:00), sun through windows by facade, people and machines; duct air warms or cools toward the rooms it crosses (T9 formula, U from material or insulation), junctions mix, duct heat goes into the rooms; the flow solve uses those temperatures. Pressure and flow-rate heatmaps on ducts as well. | `Sim/Thermal/PipeThermal.cs`, `Game/Thermal/` |
| 7 | Tech unlocks (tier N+1 with 2/3/3/2 tier-N buildings online at once; every command checks tiers), the shop (every item by category and tier with price, stats and unlock condition; "Compare with placed" simulates a fan swap in the open building and compares ducts by friction), centrifugal blower and EC fan, the variable-speed drive (live speed slider), duct sizes by tier, flex / galvanized / spiral ducts, duct sealing (duct leakage now real), insulation, smooth tees. | `Game/Progression/`, `Runtime/UI/ShopScreen.cs`, `Data/upgrades.json` |
| 8 | Offline earnings: every building's built network at steady state (crank at 0) through the time away in 6-hour blocks at the outside temperature of the hour, electricity and debt as in game, capped at 72 h; then "While you were away" (earned, electricity, net, wallet before and after, per building, unlocks). | `Game/Offline/OfflineCatchUp.cs`, `Runtime/UI/Screens.cs` |
| 9 | Steamworks.NET 2025.165.0 with app id 480: the Steam API starts when Steam runs, callbacks pumped, nothing breaks without Steam; `steam_appid.txt` next to playtest builds. Steam Cloud needs Auto-Cloud set on the Steamworks site (see Known issues). | `Runtime/SteamService.cs`, `Editor/SteamBuildStep.cs` |
| 10 | Settings (menu and city): master / ambience / interface volume, heatmap palette, light or dark theme, UI scale 80–150%, reduce motion, autosave interval, key rebinding. Stored in `settings.json`, shared by all saves. | `Runtime/UI/SettingsScreen.cs`, `Game/Save/PlayerSettings.cs` |

Carry-over from Phase 2: the building list gave way to the city (D34 retired); tier 2 prices and base pay tuned with the balancing report (D61); the hand-made reference layouts are retired (D45, D28).

Build: `Builds/phase-3/HeadLoss.exe` (Windows 64-bit) with `steam_appid.txt` (480) and `steam_api64.dll`. 0 build errors, 0 warnings; a headless start loaded the data and reached the Steam check ("Steam is not running; playing without it") with no exceptions.

## Tests

- **EditMode: 231/231 pass** (Phase 2: 173). New: city generation (determinism, districts, facilities, plots touching roads, connected roads, starter area, rail share, modifiers), building generation (determinism, valid layouts, every room reachable, vents by room type, street-side intakes, nudges), solvability of 200 generated buildings per tier, auto-router trees and sizes, the starter guarantee over 25 cities, the city game (new game in the city, tier 3+ plots closed), Phase 2 save migration and a v2 sample save, duct leakage (power law, conservation), sealing, flex ducts, smooth tees, the speed drive (price, efficiency, live slider, undo merge), EC fan, unlocks (locked items, two tier-1 buildings unlock tier 2, unlocks stay), T9 and the thermal solve, climate curve and sun, room grid (sealed room, cooling with ACH, walls and doors, room balance, people), building climate, offline catch-up (matches real time within 1%, cap, crank earns nothing, electricity and debt, unlocks, 72 h budget), settings (round trip, clamping, broken file).
- **PlayMode: 25 pass** (2 more are skipped inside the Input System package). New: new city opens the city view; click selects, double-click opens, Esc returns; Enter opens the selection and Esc clears it first; tier 3+ plots do not open; the shop opens from a building and Esc closes it first; the offline report shows and Enter closes it; settings open from the city, apply live and close with Esc; the Burst room grid matches the CPU reference over 200 steps; the 10-floor room grid step stays under 2 ms with Burst.
- 10-minute scripted play session in the Editor (685 s, 24 steps: a new city, the starter built and cranked, the small fan bought, every heatmap, undo/redo/discard, the shop, two tier-1 houses and the tier 2 unlock, settings, a tier 2 family house with its speed drive, the temperature view, 3 h of offline catch-up and the report, back to the starter, edits discarded, menu and another new city): **0 errors and 0 warnings** in the console and the Editor log. Saving was off, so your saves were not touched.

## Balancing report (`Head Loss/Balancing Report`, 20 generated buildings per tier)

| Check | Result | Target |
| --- | --- | --- |
| Starter: every minimum met at 70% crank | 5 of 5 cities | all |
| Starter: cranking at full speed pays the small fan + ducts | 2.3 min | 2–3 min |
| Starter: small fan on the same layout, vents over code max | ≤ 1 in every city | ≤ 1 (D18) |
| Tier 1 (small house, kiosk): reference solvable | 20 of 20 | all |
| Tier 1: payback of the reference build | 7.2 min | ≈ 5 min |
| Tier 1: electricity share | 15% | 10–25% |
| Tier 2 (family house, shop): reference solvable | 20 of 20 | all |
| Tier 2: payback | 6.0 min | ≈ 10 min |
| Tier 2: electricity share | 24% | 10–25% |
| Doubling flow by fan speed earns less | 20 of 20 at both tiers | all |

A tier-1 house earns about $0.25/s, a tier-2 house about $2.0/s.

## How to playtest (gate)

1. Run `Builds/phase-3/HeadLoss.exe` (Steam may run or not). Choose **New city** (a Phase 2 save loads into a new city with its spending refunded). Or open `Assets/HeadLoss/Scenes/Main.unity` in the Editor and press Play.
2. You see the city, centred on the **Start here** house. Hover plots for their difficulty; tier 3–5 plots say they arrive later. Click the starter house, then **Open** (or double-click).
3. The starter has two rooms and a hand crank. Drag ducts from the crank to both vents (through the door), **Commit**, hold **C**: it comes online and the city chimes softly.
4. Save up, click the intake and buy the **Small axial fan**: it earns by itself, also while the game is closed.
5. **Esc** to the city. Open small houses and kiosks next to the starter: each shows its requirement sheet and budget cap. Two tier-1 buildings online at once unlock **tier 2** (a notice and chime): the inline duct fan, speed drive, 200–250 mm ducts, smooth fittings, sealing and insulation. Look in the **Shop** (city or building), try **Compare with placed**.
6. Try the heatmaps: **1** velocity (rooms show drafts in front of vents), **4** temperature (cold outside air in the ducts), **5** density, **2** pressure, **3** flow rate; **H** switches the room layer. Hover a room for its temperature, CO₂ and air changes.
7. Open a family house or shop (tier 2): 7–10 vents; an inline fan with a speed drive, dampers on the branches.
8. Quit, come back later (the next day for the gate): **Continue** shows **While you were away** with what the city earned.
9. **Settings** (menu or city): sound, palette, dark theme, UI scale, reduce motion, autosave, keys.

## Gate checklist (from `14-roadmap.md`)

- [ ] Playtesters (you and friends) play a session and close the game.
- [ ] They come back the next day because the city earned while they were away.
- [ ] They want the next building.
- [ ] *(Definition of done)* No console errors during a 10-minute session (scripted session above; please watch during yours).

## Screenshots

`docs/dev/screenshots/phase-3/`: `menu.png` (main menu with Continue), `city.png` (a city with online buildings glowing), `city-hover.png` (a family house selected: difficulty hint and Open), `starter-sheet.png` (the starter's requirement sheet), `starter-built.png` (reference ducts with a small fan previewed, Commit blocked by the budget cap until the house has been online), `family-velocity.png`, `family-temperature.png`, `family-flow.png` (a tier 2 house on its reference: inline fan with a speed drive, branch dampers, room and duct heatmaps), `shop.png`, `away.png` (While you were away), `settings.png`, `city-dark.png`, `family-dark.png` (dark theme).

## Known issues

- The 10-floor room grid budget (2 ms with Burst) passes in the Editor with little margin (D66); a player build is faster.
- Saves and settings live in `AppData/LocalLow/Elevate Studios/Pipe Game/saves/` (D70). Saves from Phase 2 builds are in the old `DefaultCompany` folder; copy `profile-1.json*` across to continue one.
- Steam Cloud: Auto-Cloud has to be configured on your app in Steamworks: root `WinAppDataLocalLow`, subdirectory `Elevate Studios/Pipe Game/saves`, pattern `*`, recursive (D69). App id 480 cannot be configured.
- Larger equipment (centrifugal blower 2×1, EC fan 2×2) is drawn at the intake but does not take floor space yet (floor layer in Phase 4, D62).
- Rooms change temperature slowly by design (5× air heat capacity), so the temperature heatmap needs a few minutes to show a pattern.
- Tier 3–5 plots cannot be opened until their phases (D47).
- Parts still cannot be moved, only removed and placed again.

## Needs your confirmation

`[confirm]` items from `00-index.md` that apply now:
1. **Budget cap** (unchanged from Phase 2): spending before the first time online may not exceed the cap.
2. **Overdelivery cap below 5× min** (unchanged).
7. **Tech unlocks:** tier N+1 unlocks with 2 / 3 / 3 / 2 tier-N buildings online at the same time; unlocks stay for the city.
8. **Offline cap:** 72 h.

Confirmed at review: D46, D47, D59, D61; company name set (D70).

Also worth a look (not blocking): D44 duct sizes by tier (you chose this), D48 auto-router rules, D52 the city controls, D56 duct leakage on, D60 tier 2 references use a speed drive, D67 offline catch-up details, D68 settings in their own file.
