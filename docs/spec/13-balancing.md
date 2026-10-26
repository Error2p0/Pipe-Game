# 13 · Balancing defaults

Every number here goes into `Assets/HeadLoss/Data/balancing.json` (or the data file named). All are **starting values to tune** [impl], except values marked [doc], which come from the design doc and must not change without the user's approval.

From Phase 2 on, keep an editor window `Head Loss/Balancing Report` that, for every tier, generates 20 buildings, runs the auto-router reference layout, and prints: build cost, minutes of income to pay it back, electricity as % of gross, and whether the next tier's cheapest item is affordable within the target time below. Tune with it, not by feel.

## Targets [impl]

| Target | Value |
| --- | --- |
| Starter house: cranking pays a small axial fan + ducts | 2–3 min |
| Each tier's first building payback | T1 5 min · T2 10 min · T3 20 min · T4 40 min · T5 80 min |
| Electricity share of gross at the reference layout | 10–25% at every tier |
| Doubling flow by speed on a reference layout | net income lower than at the sweet spot |
| First prestige available | 15–25 h of play including offline |

## Scaling factors [impl]

| Key | T1 | T2 | T3 | T4 | T5 |
| --- | --- | --- | --- | --- | --- |
| `tierFactor` (fines, repairs, renewals) | 1 | 5 | 25 | 125 | 625 |
| `electricityTierFactor` | 1 | 1 | 3 | 10 | 30 |
| `basePay` air vent ($/s) | 0.15 | 0.9 | 5 | 30 | 180 |

Outlet type pay factor (× `basePay`): air supply vent 1.0, clean-air vent 1.5, cold tap 1.2, hot tap 1.5, gas stove / burner 2.0, propane outlet 2.2, radiator 1.5, cooling terminal 1.8, special-gas outlet 3.0.

## Economy (`balancing.economy`)

| Key | Default |
| --- | --- |
| `overdeliveryCapRatio` [doc] | 5 |
| `capMultiplier` [doc] | 2.0 |
| `capMultiplierRisky` [doc] | 2.5 |
| `offlineBurstFactor` [doc] | 0.25 (75% cut) |
| `inGameBurstFactor` [doc] | 0 |
| `electricityPricePerKWs` | $5.0 per kW·s × `electricityTierFactor` of the building |
| `incomeCycleSeconds` | 60 |
| `refundFraction` | 0.5 |
| `startingWallet` | $30 |

## Hand crank (`balancing.crank`)

| Key | Default |
| --- | --- |
| `speedPerClick` | 0.12 |
| `decaySeconds` (time constant) | 4 |
| `holdClicksPerSecond` | 5 (holding the button/key counts as clicking) |

## Outlets (`outlets.json`) [impl, except the T3 example values which are doc]

| Outlet type | Measured | T1 | T2 | T3 | T4 | T5 | Design flow | Code max reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `air-supply-vent` | m³/h | 25 / 35 / 60 | 30 / 45 / 75 | **40 / 60 / 90** [doc] | 60 / 90 / 140 | 80 / 120 / 180 | — | Noise |
| `clean-air-vent` | m³/h + HEPA | — | — | — | — | 100 / 150 / 220 | — | Noise |
| `cold-water-tap` | bar | — | 1.0 / 2.0 / 4.0 | **1.5 / 3 / 5** [doc] | 1.5 / 3 / 5 | 1.5 / 3 / 5 | 0.10 L/s (T4+ 0.15) | Tap pressure |
| `hot-water-tap` | bar, ≥ 55 °C | — | — | — | 1.5 / 3 / 5 | 1.5 / 3 / 5 | 0.10 L/s | Tap pressure |
| `gas-stove-outlet` | mbar | — | — | **min 20** [doc] / 23 / 28 | 20 / 23 / 28 | 20 / 23 / 28 | 0.54 m³/h (5 kW) | Appliance inlet limit |
| `gas-burner-outlet` (industrial) | mbar | — | — | — | — | 100 / 150 / 300 | 20 m³/h | Appliance inlet limit |
| `propane-outlet` | mbar | — | — | — | — | 30 / 37 / 45 | 0.2 m³/h | Appliance inlet limit |
| `radiator` | W | — | — | room load ×1.0 / 1.2 / 1.6 | same | same | — | Overheating |
| `cooling-terminal` | W | — | — | — | room load ×1.0 / 1.2 / 1.6 | same | — | Overcooling |
| `oxygen-outlet` | bar, purity 99.5% | — | — | — | — | 4.0 / 4.5 / 5.0 | 20 L/min | Equipment limit |
| `nitrogen-outlet` | bar, purity 99.9% | — | — | — | — | 5 / 6 / 8 | 20 L/min | Equipment limit |
| `co2-outlet` | bar | — | — | — | — | 3 / 4 / 5 | 10 L/min | Equipment limit |
| `hydrogen-outlet` | bar, purity 99.99% | — | — | — | — | 2.0 / 2.5 / 3.0 | 10 L/min | Equipment limit |

Format: min / ideal / code max. Seeded ±10% variation per outlet at generation (`04-buildings.md`). Radiator and cooling loads are computed per room at design outside temperature (−5 °C heating, +32 °C cooling; climate modifiers shift these).

## Prices [impl]

**Ducts and pipes (per metre):** `price = base × materialFactor × (D / D_ref)^1.3`.
- Ducts: base $1.0 at 100 mm; flex 0.5, galvanized 1.0, spiral 1.6.
- Pipes: base $2.0 at 22 mm; PVC 1.0, copper 3.0, steel 4.0, stainless 8.0.
- Insulation: +$0.5 (13 mm), +$1.2 (25 mm), +$2.5 (50 mm) per metre at 22 mm, scaled the same way.
- Duct sealing: +$0.3/m (sealed), +$0.8/m (premium).

**Fittings:** sharp elbow $1, long-radius elbow $3, sharp tee $2, smooth tee $5, reducer $1, shutoff $10, ball valve $15, balancing valve $25, damper $8, check valve $12, fire damper $60, backflow preventer $400, basic filter $40, HEPA filter $600. (× `(D / D_ref)` for sizes above reference.)

**Equipment:**

| Item | Price | Item | Price |
| --- | --- | --- | --- |
| hand-crank-fan | $0 (pre-placed in the starter house; $10 elsewhere) | small-axial-fan | $40 |
| inline-duct-fan | $300 | centrifugal-blower | $2 500 |
| ec-fan | $9 000 | industrial-ahu | $120 000 |
| small-booster-pump | $400 | multistage-pump | $3 500 |
| vs-pump-set | $40 000 | circulation-pump | $250 |
| inline-booster-pump | $1 500 | gas-regulator | $150 |
| gas-booster-compressor | $8 000 | gas-manifold | $2 000 |
| special-gas-manifold | $25 000 | electric-heater | $200 |
| gas-boiler | $2 500 | water-heater | $600 |
| district-heat-exchanger | $15 000 | aircon-split | $1 800 |
| heat-pump | $6 000 | heat-recovery-unit | $4 000 |
| hr-unit-high-eff | $18 000 | lab-chiller | $12 000 |
| smart-building-controller | $3 000 | variable-speed-drive | $250 (fans) / $600 (pumps) |
| pressure-tank | $350 | leak-sensors (per network) | $500 |
| backup-generator 5 / 20 / 100 kW | $2 000 / $9 000 / $50 000 (fuel $0.002 per kW·s) | gas-detector | $80 |
| auto-shutoff-valve | $300 | passive-vent-opening | $120 |
| panel-standard / large / transformer room | $500 / $5 000 / $60 000 | raised panel | +25% |
| engineering software | $25 000 one-off | maintenance contract | 2% of the building's gross income per cycle |

Spark-free variants ×1.5.

## City (`balancing.city`)

| Key | Default |
| --- | --- |
| Grid | 96 × 96 tiles, 10 m per tile |
| `facilityPressure.water` | 4.0 bar |
| `facilityPressure.gasLP` / `gasMP` / `gasHP` | 75 mbar / 2 bar / 7 bar |
| `facilityPressure.heating` | 6 bar, supply 90 °C |
| `facilityPressure.specialGas` | 10 bar |
| Main price per tile | water 100 mm $50 → ×(D/100)^1.3; gas ×1.2; heating ×2.5; special gas ×4 |
| Cable price per tile | 50 kW $40, 150 kW $90, 400 kW $200, 1 MW $450, 2.5 MW $1 000 |
| `crossingCost` (main road or rail tile) | $300 × tile count |
| Directional drilling | $2 000 per tile |
| Utility corridor | $500 per road tile, holds up to 4 mains/cables |
| Booster station | water $20 000, gas $35 000 |
| Substation upgrade | 2 → 5 MW $250 000; 5 → 12 MW $1 500 000 |

**Recurring fines per cycle** (× destination building `tierFactor`): under house $5, protected site $8, near substation $10, main separation $6, historic foundations $25 (5 × under house [confirm]). Industrial gas depot mains: all ×2.

## Power (`balancing.power`)

| Key | Default |
| --- | --- |
| `basicConnectionKW` (before power unlock) | 6 kW |
| Substation capacity per district | 2 MW |
| Panel trip delay | 2 s |

## Progression and prestige

| Key | Default |
| --- | --- |
| `progression.unlockOnline` (buildings of tier N online to unlock N+1) | T1→T2: 2 · T2→T3: 3 · T3→T4: 3 · T4→T5: 2 |
| `prestige.shareOnline` | 0.60 of non-ruin plots |
| `prestige.incomeTarget` (lifetime city income) | city 1: $5e8; ×10 per later city |
| `prestige.k` (Reputation = floor(k · log10(lifetime income))) | 3 |
| Prestige node costs | electricity-contracts 3/level · starter-kit 5, 10 · pay-rates 4/level · materials 6, 12 · rapid-response 5/level · reconstruction-permits 25 |
| Reconstruction (per ruin) | 20 × the plot's reference build cost |

## Building generation

| Key | Default |
| --- | --- |
| `building.floorHeight` | 3 m |
| `building.budgetCapFactor` | 1.6 × reference layout cost |
| Ceiling capacity | 4 units (3 with `old-buildings`) |
| Reference equipment for solvability | T1 small-axial-fan (starter: hand crank at 70%) · T2 inline-duct-fan, small-booster-pump or mains · T3 centrifugal-blower, multistage-pump, gas-regulator · T4 ec-fan or industrial-ahu, vs-pump-set, gas-manifold, district-heat-exchanger · T5 industrial-ahu, vs-pump-set, special-gas-manifold |

## Failures (`balancing.failure`)

| Key | Default |
| --- | --- |
| `k_s` | 1/3600 s⁻¹ |
| `a` | 1.5 |
| Weibull `β`, `η` | 3, 1 |
| Weep threshold | S = 0.6 |
| `fittingLeakPerHour` (gas fittings) | 0.0005 |
| `explosionTimerSeconds` | 600 |
| Explosion `yieldFactor`, `k_r` | 0.1, 0.07 m/J^(1/3) |
| Heavy / light damage thresholds | 0.6 / 0.1 |
| Repair, standard | 2 h; $5 × metres × (D/D_ref) × `tierFactor`, min $10 × `tierFactor` |
| Repair, emergency | 3 min; 5 × standard price |
| `ageing` | off |
| `hammer.durationSeconds` | 2 |
| Accumulator surge factor | 0.2 |

**One-off fines:** explosion = max(2 h of that building's gross income, $5 000 × `tierFactor`); each damaged neighbour = 0.5 h of its gross income × damage (min $500 × its `tierFactor`); failed inspection = max(10 min of gross, $50 × `tierFactor`). Insurance ×0.5.

## Events, inspections, outage

| Key | Default |
| --- | --- |
| `events.meanIntervalHours` | 3 (min gap 45 min) |
| `insurance.premiumFraction` | 0.03 of gross per cycle |
| `inspection.chancePerHour` | 0.08 per online building |
| Inspection pass multiplier / duration | ×1.25 / 60 min |
| Inspection fix deadline | 30 min (pauses offline) |
| `inspection.gasCertHours` | 48 |
| Gas certificate renewal | $100 × `tierFactor`, 10 min check |
| Expired certificate pay factor | 0.5 |
| `gas.backgroundLeakPerFitting` | 1e-8 m³/s |
| `outage.gasSafetyWindowSeconds` | 600 |
| `outage.tankWarmRate` | 0.5 K/min |
| `outage.groundwaterLps` | 0.2 L/s per 100 m² of basement |
| Flood level for basement panel | 0.3 m |
| Generator start delay | 5 s |

## Climate (`balancing.climate`)

| Key | Default |
| --- | --- |
| Outside temperature | 15 °C mean, ±6 °C daily sine following the player's local clock (warmest 15:00) |
| `cold-climate` modifier | −10 °C, heating requirements ×1.5 |
| Solar gain peak | 300 W/m² of window, by facade (S 1.0, W 0.8, E 0.6, N 0.2), daytime only |
| Wall U-value | 0.6 W/m²K (old buildings 1.2) |
| Room relative humidity | bathroom 70%, kitchen 60%, other 45% |
| Occupant heat | 100 W total, 70 W sensible |
| Occupant CO₂ | 0.005 L/s |
| Outdoor CO₂ | 420 ppm |

## Room grid

| Key | Default |
| --- | --- |
| Cell size | 0.5 m |
| Eddy diffusivity `D_t` | 0.02 m²/s |
| Jet length `L_jet` | 2 m |
| Stratification `k_b`, mixing | 0.5, 0.02 /s |
| Floor exchange `k_v` | 0.1 /s |
| Thermal mass factor | 5 × air |

## Offline and save

| Key | Default |
| --- | --- |
| `offline.maxHours` | 72 [confirm] |
| Autosave interval | 30 s |
