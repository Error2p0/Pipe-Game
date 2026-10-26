# Phase 2 report · First playable building

Status: **built, waiting for your gate playtest.** Branches `phase-2/economy` and `phase-2/gate-fixes`, merged to `main`.

## Changes after your review

- Confirmed D22, D23, D24, D35, D36.
- **Dampers now need a Commit** (D25): changing a damper's opening with its Open slider is free and previews at once, but the built damper changes when you press Commit. Fan on/off stays instant. The fan speed slider you asked for is the variable-speed drive, which the roadmap puts in Phase 3 (task 7). It will apply without a Commit, like on/off.
- The income breakdown opens above the design bar (it was underneath).
- Selected (and hovered) intakes, outlets, dampers and inline fans get a square outline.
- With something selected, the right panel keeps its buttons while you hover other things (D43), so buying at the intake or resizing a duct works without moving the pointer away first.
- Box select draws its rectangle and highlights the ducts it covers while you drag.

## What was built

| # | Task | Where |
| --- | --- | --- |
| 1 | Outlet requirements (min / ideal / code max, in SI), the payout multiplier exactly as `02-economy.md` (square-root overdelivery, cap 2× / 2.5× risky, compressed to the code max when it is below 5× min), zones, and the online rule (every outlet at its minimum) | `Game/Economy/Payout.cs`, `BuildingEconomy.cs` |
| 2 | Requirement sheet (opens by itself the first time, then `Tab`), outlet list with live zones, multiplier, pay and a min / ideal / code-max gauge per outlet | `Runtime/UI/RequirementSheet.cs`, `OutletList.cs` |
| 3 | Wallet, debt (repaid first; purchases wait while in debt), base pay × pay factor, electricity `P = QΔp/η` × price, income at 10 Hz for every building (open or not), money with K / M / B / T / aa suffixes, a breakdown on hover or tap | `Game/GameState.cs`, `GameLoop.cs`, `Runtime/UI/TopBar.cs` |
| 4 | Hand crank (click, or hold `C` / the crank button), small axial fan at the intake, inline duct fan anywhere on a straight duct (turn it with `R`) | `Game/Session/BuildingSession.cs`, `Game/Networks/NetworkCompiler.cs` |
| 5 | Dampers on straight ducts with a 5–100% slider, `K = K_open/θ²`; moving one is free and takes effect on Commit | `Game/Buildings/DuctEditor.cs`, `Game/Commands/Commands.cs` |
| 6 | Design mode: you edit a draft, the heatmap and outlet list preview it, Commit pays (Enter), Discard reverts. Removing built parts refunds 50%; upgrades in place cost the difference; budget cap until the first time online | `Game/Economy/Pricing.cs`, `BuildingSession.Commit` |
| 7 | Undo / redo (`Ctrl+Z` / `Ctrl+Y` and buttons), unlimited, cleared when you leave the building; damper drags merge into one step | `Game/Commands/Commands.cs` |
| 8 | Small house: 5 rooms with doors (ducts cross walls only at doors), 5 vents, empty intake on the south wall. The one-room test house is now the starter, with a hand crank already in place | `Data/handmade/*.json` |
| 9 | Saves: one JSON per profile, atomic write (`.tmp` then replace), 3 rolling backups, gzip above 1 MB, versioned with a migration step table, falls back to the newest readable backup, drops edits that no longer fit. Autosave every 30 s, on Commit, on leaving a building, on focus loss, pause and quit. Main menu with Continue / New game | `Game/Save/`, `Runtime/GameRoot.cs` |
| 10 | Generated (CoolProp) fluid tables, done in Phase 1 | `Data/fluids/` |
| 11 | Procedural audio, no files: soft pink-noise airflow that opens up with throughput, a quiet hum per running fan (40–120 Hz by speed), soft click on every button, a "tock" when placing, a two-note chime on Commit, soft limiter | `Runtime/Audio/ProceduralAudio.cs` |
| 12 | `Head Loss/Balancing Report` editor window. It builds each reference layout and checks the `13-balancing.md` targets (payback, electricity share, crank time, doubling speed, next-tier item) | `Game/Balancing/BalancingReport.cs`, `Editor/BalancingReportWindow.cs` |

Also: interior walls with door gaps, equipment symbols (crank with a turning handle, fans whose blades turn with their flow, inline fans with a direction arrow, damper blades that turn as they close), a building list standing in for the city (D34), `codes.json` with the noise code (D37), and a validator that covers every new data field.

Build: `Builds/phase-2/HeadLoss.exe` (Windows 64-bit). 0 build errors, 0 warnings; a headless start reached the main menu with no errors or exceptions in the log.

## Tests

- **EditMode: 173/173 pass** (Phase 1: 86). New: T14 and T15 payout reference cases, every zone, both caps, the degenerate band, SI conversion; online/offline and electricity while offline; income per economy step, debt accrual and repayment; money formatting; prices, the commit diff (new, sold, upgraded, downgraded, moved, equipment swap, pre-placed crank), budget cap, "need $X more", debt blocks purchases but not sales; undo/redo depth, redo cleared by new edits, undo after commit, Discard undoable, history cleared on leaving; crank click, decay, hold, and the starter coming online by cranking; dampers (K(θ), clamping, balancing effect, slider merge, live on built parts), inline fans (open louvre, direction, off = closed), parts removed with their duct; doors; the solvability check of both reference layouts and their budget caps; saves (round trip, backup rotation, a crash before replace, corrupt save → backup, gzip, clock moved back, offline cap, newer version refused, every sample version loads, sanitising); the balancing report.
- **PlayMode: 16/16 pass** (2 more are skipped inside the Input System package itself). New: menu on boot, Enter commits and charges, holding C brings the starter online and earns, the part tool places a damper and Esc puts it away, Ctrl+Z / Ctrl+Y, Esc to the building list and opening another building, the requirement sheet on first open and Tab, box select drawn and live while dragging.
- 10-minute scripted play session in the Editor (switching buildings, drawing, committing, dampers, inline fan, undo/redo, Discard, heatmap modes, the sheet, autosaves): 611 s, 37 steps, **0 errors and 0 warnings** in the console. Continue after quitting restored the game (test house with the fan built, wallet, online flag, rotated backups). The save files from these runs were removed afterwards.

## Balancing report (current data)

| Check | Result | Target |
| --- | --- | --- |
| Starter reference meets every minimum at 70% crank | yes (32 / 29 / 29 m³/h) | yes |
| Cranking (full speed) pays the small fan + ducts after the starting wallet | 2.3 min | 2–3 min |
| Starter: small fan on the same layout, vents above code max | 1 (63 / 58 / 58 m³/h) | ≤ 1 (D18) |
| Small house reference payback | 4.8 min | ≈ 5 min |
| Small house electricity share | 10% | 10–25% |
| Small house fan at 2× speed | −$0.20/s vs +$0.33/s | lower |

## How to playtest (gate)

1. Run `Builds/phase-2/HeadLoss.exe`, or open `Assets/HeadLoss/Scenes/Main.unity` in the Editor and press Play. Choose **New game**.
2. You are in the **test house** with $40 and a hand crank at the intake. The requirement sheet opens: three vents, each needs 25 m³/h, ideal 35, code max 60 (noise). Close it with **Tab**.
3. Drag ducts from the crank to all three vents. Each corner shows its loss while you drag. The design bar at the top shows the cost; press **Enter** (or Commit) to buy it. Tip: a wide trunk to the middle of the room and narrower branches keep the three vents even.
4. Hold **C** (or hold the Crank button). The crank spins up in about 2 s; when every vent passes 25 m³/h the status turns green, "Online", and money flows. Let go and it slows down.
5. With about $40 more, click the intake and choose **Small axial fan** (Commit). It runs by itself. On a good layout one vent goes slightly **above code max** (red, pays nothing). Fix it: pick **Damper** in the toolbox, click the straight duct right after the fan, select the damper and slide it to about 60% until every vent is green (**overdelivery**) in the preview, then **Commit**. On the reference layout that takes income from about $0.15/s to $0.25/s, close to the most this house can earn. A damper on one branch instead pushes the air into the other vents and over their code max. Try it.
6. Press **Esc** (twice if something is selected) for the building list and open the **small house**: 5 rooms, ducts pass walls only at doors, budget cap $150. Place a fan at the intake and route to all five vents. Use the outlet list on the right to balance: overdelivery up to the code max pays up to 2×.
7. The electricity sweet spot: save up for the **Inline duct fan** ($300) and put it on the trunk. Watch the money breakdown (click the income next to the wallet): vents go above code max and the fan costs more per second than it earns; running it makes less money. Switch it off or sell it.
8. Try **Ctrl+Z / Ctrl+Y**, **Discard**, and quit and **Continue** (the game saves itself).

## Gate checklist (from `14-roadmap.md`)

- [ ] A fresh save.
- [ ] Crank, buy the small fan.
- [ ] Optimise the house and reach overdelivery.
- [ ] Feel the electricity sweet spot (spinning harder does not pay).
- [ ] A 20-minute session feels satisfying.
- [ ] *(Definition of done)* No console errors during a 10-minute session (scripted session above; please watch during your own).

## Screenshots

`docs/dev/screenshots/phase-2/`:
- `menu.png`, `menu-continue.png`: main menu, first run and with a save.
- `start.png`: first open of the starter with the requirement sheet.
- `drawn.png`: a run being designed, the design bar with its cost.
- `cranking.png`: the starter built and cranked.
- `budget-cap.png`: the small fan previewed before the first time online, one vent above the code max, Commit blocked by the budget cap.
- `small-house-damper.png`: the small house online, a damper selected with its slider.
- `building-list.png`: the building list.

## Known issues

- No sound controls yet: master volume is fixed at a low default until the settings screen (Phase 3, D42).
- Offline time is measured and logged, but offline earnings arrive in Phase 3 (D39).
- Saves sit in `AppData/LocalLow/DefaultCompany/Pipe Game/saves/`; company and product name are still the template's. Renaming them later moves the save folder.
- An inline fan placed downstream of a branch can suck room air backwards through the vents before it. That is the real physics; the readout says "Air is flowing backwards", and the vent counts as 0.
- Parts cannot be moved; remove one and place it again (removal is free until committed).
- The requirement sheet covers the design bar while it is open.
- The HUD is laid out for 1920 × 1080 and scales down; at 1280 × 720 text is small but nothing overlaps.

## Needs your confirmation

`[confirm]` items from `00-index.md` that apply now:
1. **Budget cap:** spending before the first time online may not exceed the cap; after that there is none. Built as written.
2. **Overdelivery cap below 5× min:** the curve reaches the full cap at the code max (T14: 2.0 at 90 m³/h). Built as written.

Confirmed at review: D22, D23, D24, D35, D36. D25 revised as you asked. Nothing else is open.
