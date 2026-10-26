# 02 · Core loop and economy

[doc] A building pays only once every outlet meets its minimum. After that, extra delivery pays more, and electricity is the main running cost.

## Core loop [doc]

1. Pick a building in the city and read its requirement sheet.
2. Design the network: route pipes and ducts, place fans, pumps, valves and equipment.
3. Run the simulation and check the heatmaps. Fix weak branches.
4. All minimums met: the building comes online and earns continuously, including offline.
5. Spend income on better equipment, pipe upgrades and new buildings.
6. Return to older buildings to optimise them as new tech unlocks.

[impl] Step 2–3 happen in **design mode**: edits are free drafts and the simulation previews the result live ("simulate before you buy"). **Commit** charges the cost difference and applies the design. Removing committed parts refunds `balancing → economy.refundFraction` (default 50%) of their price.

## Outlet payout bands [doc]

Each outlet has three thresholds: minimum, ideal, code maximum.

| Zone | Delivery | Payout |
| --- | --- | --- |
| Below minimum | Under requirement | Building stays offline (0 for the whole building) |
| Working band | Minimum to ideal | Base rate |
| Overdelivery | Ideal to code maximum | Bonus with diminishing returns, capped at 2× base income at 5× the minimum delivery (2.5× for risky fluids) |
| Code violation | Above code maximum | Outlet pays 0 and shows a warning |

The code maximum keeps overdelivery honest. Examples: air too fast in a bedroom is noisy, water pressure too high at a tap is unsafe.

Risky fluids are flammable or hazardous gases (natural gas, propane, hydrogen, oxygen). Their higher cap rewards the extra danger. Where a code maximum sits below 5× the minimum, the code maximum wins and the cap is reached earlier.

### Payout multiplier formula [impl]

For an outlet with minimum `min`, ideal `ideal`, code maximum `max` (may be absent), and delivery `d`:

```
r      = d / min                       // delivery ratio
rIdeal = ideal / min
rCap   = min(5, max / min)              // 5 = balancing.economy.overdeliveryCapRatio; if no code max, rCap = 5
M      = 2.5 if fluid is risky else 2.0 // balancing.economy.capMultiplier / capMultiplierRisky

if r < 1:            building offline (whole building pays 0)
elif d > max:        m = 0, outlet warning "Above code maximum: <reason>"
elif r <= rIdeal:    m = 1
else:                m = 1 + (M − 1) · sqrt( clamp01( (r − rIdeal) / (rCap − rIdeal) ) )
```

- `sqrt` gives diminishing returns; `m` reaches exactly `M` at `rCap` and stays flat above it (until the code max zeroes it).
- [confirm] When `max/min < 5`, `rCap = max/min`, so the full cap is reached right at the code maximum.
- If `rCap <= rIdeal` (degenerate data), `m = 1` in the whole band. The data validator rejects outlet definitions where `ideal > max`.
- For outlets measured in pressure (taps, gas), "delivery" is the residual pressure at the outlet while it draws its design flow (see `05-simulation.md`). For heating/cooling outlets it is heat delivered in W.

Outlet payout per second = `baseRate(outletType, tier) × m × buildingMultipliers`.

## Electricity [doc]

Every powered device draws electricity proportional to the flow it moves times the pressure it adds, divided by its efficiency. Fans and pumps follow the affinity laws, so power rises roughly with the cube of speed. Doubling flow by spinning a fan faster costs about 8× the power, which creates a natural efficiency sweet spot per building.

[impl]

```
P_device [W] = Q [m³/s] × Δp [Pa] / η(Q)          // fans, pumps, compressors, boosters
P_device [W] = Q_heat [W] / COP                    // aircon, heat pump
P_device [W] = ratedPower                          // fixed-draw items: controllers, detectors, sump pumps, chillers
electricityCost per s = Σ P_device / 1000 × balancing.economy.electricityPricePerKWs × prestigeElectricityFactor
```

- `η(Q)` comes from the device's efficiency curve (interpolated). Motor efficiency is folded into it.
- The hand-crank fan uses no electricity.
- Electricity is charged continuously (per tick), online and offline.

## Income formula [doc + impl]

[doc] Income per building = sum of outlet payouts − electricity cost − active penalties.

[impl] Full order of operations, per second:

```
gross    = Σ_outlets baseRate × m
gross   *= energyRatingMultiplier × inspectionMultiplier × eventMultiplier × prestigePayMultiplier
gross   *= certificatePenalty          // 0.5 when a gas safety certificate is expired, else 1
gross   *= burstFactor                 // any unrepaired burst on a water/air line that is not on an isolated branch:
                                       // 0 while the player is in game, 0.25 while offline (75% cut); else 1
gross   *= evacuationFactor            // 0 while evacuated (gas alarm, asphyxiation, flood)
net      = gross − electricityCost − recurringFinesPerSecond − insurancePremiumPerSecond
```

- `net` can go negative. [impl] The wallet never drops below 0: anything that cannot be paid (electricity, fines, premiums) accrues as **debt**, shown in red next to the wallet and repaid automatically from the next income before the wallet grows. Equipment keeps running while in debt. Purchases are blocked while debt > 0.
- A building that is offline (any outlet below minimum, or blocking violations) has `gross = 0` but still pays electricity for any equipment left running. The building panel shows this clearly ("Offline: equipment still drawing 0.4 kW").

## Recurring fines and the income cycle [doc + impl]

- City routing violations charge a **recurring fine every income cycle** until the route is moved ([doc]). The player can choose to accept a fine when the shortcut is worth it.
- Income cycle default: 60 s real time [impl]. Fines are defined per cycle in `balancing.json` and charged pro rata each tick (so the wallet moves smoothly), shown as "−$X per cycle" in the UI.
- One-off fines (explosion, failed inspection, damaged neighbours) are charged immediately.

## Budget cap [doc + confirm]

[doc] Each building has an initial build budget. Equipment and pipe beyond it must be paid from income later.

[confirm] Implementation: before a building first comes online, the total committed spend on it may not exceed its budget cap; the Commit button is disabled with "Over budget cap by $X" when it would. Money comes from the player's wallet. After the building has been online once, the cap no longer applies.

## Hand crank and active play [doc + impl]

[doc] The hand-crank fan: the player clicks to spin; the only active-play device.

[impl]
- Each click (or hold of the crank button / key `C`) adds `crank.speedPerClick` to crank speed (0–1), capped at 1. Speed decays exponentially with time constant `crank.decaySeconds`.
- The crank fan's curve scales with the affinity laws by its speed (`05-simulation.md`).
- Offline, crank speed is 0. A crank-only building therefore earns nothing offline, which nudges the player to buy the small axial fan.
- Balance target: the starter house reaches all minimums at ≥ 70% crank speed with a sensible layout, and cranking pays for a small axial fan plus ducting in about 2–3 minutes (`13-balancing.md`).

## Money display [impl]

- Currency symbol `$` (data-driven). Short scale suffixes: K, M, B, T, then aa, ab, … for idle-scale numbers.
- Wallet, income per second (net), and a breakdown tooltip (gross, electricity, fines, premiums) are always visible in the top bar.
