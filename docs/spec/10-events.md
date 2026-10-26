# 10 · Events and inspections

[doc] Events and inspections give players a reason to check in without punishing absence: they create short windows of bonus or risk, and every catastrophe timer follows the offline pause rule.

## Event scheduler [impl]

- City-level scheduler, 1 Hz. Mean time between events `balancing.events.meanIntervalHours` (default 3 h), exponential with a 45-minute minimum gap. At most one active event per district, two city-wide.
- Weights and durations per event in `events.json`. Events only target districts/buildings where they make sense (no cold snap before heating exists; no outage before power is unlocked, unless the outage only costs income).
- A calm banner at the top of the city view announces an event with a short line and a "Show me" button. A log keeps the last 20 events.
- Offline [confirm]: events can start while offline; bonus effects apply analytically during catch-up; every catastrophe and deadline timer pauses while offline (doc rule).

## Events [doc]

| Event | What happens | Player response |
| --- | --- | --- |
| Heatwave | Cooling demand jumps for a period | Buildings that keep up earn a bonus |
| Cold snap | Heating demand jumps for a period | Same, for heating |
| Plant maintenance | A central facility runs at reduced output for a day | Mains compete for limited supply; prioritise with valves |
| Power outage | A substation fails and its district loses power | Only buildings with a backup generator keep earning; see the cascade below |
| New tenant | A building's requirements change | Adapting quickly earns a bonus |
| City festival | Temporary demand spike in one district | Boosted pay while it lasts |

[impl] Defaults (`events.json`):

| Event | Scope | Duration | Effect | Reward |
| --- | --- | --- | --- | --- |
| Heatwave | city | 60–120 min | outside temperature +8 °C, solar gain ×1.3; cooling outlet requirements ×1.4 | buildings meeting all (raised) minimums get ×1.5 pay during the event |
| Cold snap | city | 60–120 min | outside temperature −12 °C; heating requirements ×1.4 | same, for heating |
| Plant maintenance | one facility | 2 h ("a day" compressed; tunable) | facility source pressure and capacity ×0.6 | — (risk only) |
| Power outage | one substation's district | 15–45 min | district has 0 kW except backup generators; triggers the cascade | — |
| New tenant | one online building | until adapted or 24 h | requirements regenerated for 30–60% of outlets (same building, new seed) | ×1.5 pay for 2 h if all minimums are met again within 30 min of the change (timer pauses offline) |
| City festival | one commercial or residential district | 45–90 min | outlet minimums ×1.3 | ×2 pay for buildings that keep up |

A building that drops below a raised minimum during heatwave/cold snap/festival goes offline as usual; when the event ends the minimums return. A "temporarily raised" tag shows on those outlets.

## Power outage cascade [doc]

Without a backup generator, an outage is not just lost income: equipment that keeps a building safe stops too.

1. **Gas buildings lose ventilation.** Fans stop, so the small background leakage every gas installation has (plus exhaust from gas appliances) is no longer cleared. Concentration climbs on the heatmap. If the outage outlasts a safety window, the building enters the normal leak escalation: alarm and evacuation, then explosion if power and ventilation are not restored in time.
2. **Labs and industrial gas storage lose cooling.** Pressurised or cryogenic gas tanks kept cold by electric chillers warm up, pressure rises, and relief valves vent gas into the room. Nitrogen or CO₂ creates an asphyxiation zone and forces evacuation; hydrogen or other flammables follow the explosion path, with blast radius and chain reactions.
3. **Basements flood.** Sump pumps stop, groundwater rises, and a flooded electrical panel takes the whole building offline until repaired, even after power returns.

Protection: backup generators (sized to cover safety-critical equipment), passive ventilation openings in gas rooms (slower but power-free), automatic gas shutoff on power loss, and raised electrical panels. Insurance lowers the fines but not the damage.

[impl]
1. **Background leakage:** every gas network leaks `balancing.gas.backgroundLeakPerFitting` (tiny, default 1e-8 m³/s per fitting) all the time; gas appliances add combustion products while running (modelled as CO₂). Normally ventilation clears it. When fans stop, the room grid shows the build-up. Safety window: `balancing.outage.gasSafetyWindowSeconds` (default 600 s online) before the leak escalation begins at the alarm stage. Passive vents slow the build-up; auto-shutoff on power loss stops the background leak and appliances.
2. **Gas storage tanks** are generated fixtures in tier-5 labs and factories (`04-buildings.md`), each with a `lab-chiller` flagged safety-critical. Without power: tank temperature rises at `balancing.outage.tankWarmRate` (K/min); tank pressure follows the ideal gas law / vapour pressure from the fluid table; at the relief set pressure the relief valve vents into the room at its rated flow, unless a vent line to outdoors is routed. Asphyxiant gases → asphyxiation evacuation (`06-fluids.md`); flammables → normal escalation.
3. **Flooding:** basements have a groundwater inflow `balancing.outage.groundwaterLps`; the sump pump removes it. Without it, water level rises (shown as a blue fill on the basement floor). When the level reaches 0.3 m, a basement panel that is not raised floods: the building goes offline with "Flooded panel: repair needed" even after power returns. Repair = standard or emergency crew (panel repair price).
- All three timers pause while offline (doc rule). When power returns, fans restart, chillers resume, sump pumps drain.
- Backup generators start 5 s after power loss, cover `safetyCritical` loads first, and burn fuel while running.

## Inspections [doc]

| Inspection | How it works | Result |
| --- | --- | --- |
| Random inspection | An inspector visits a building and checks codes | Pass: temporary pay multiplier. Fail: fine and a fix deadline |
| Energy rating (A–G) | Permanent efficiency grade per building | Adjusts pay; rewards clean layouts over brute force |
| Gas safety certificate | Gas buildings need a periodic check | Expired: pay cut until renewed |

[impl]

**Random inspection**
- Each online building has `balancing.inspection.chancePerHour` (default 0.08). An inspector icon walks to the building (cosmetic, 10 s), then checks all layout codes and current operating limits.
- Pass: ×1.25 pay for 60 minutes (`inspectionMultiplier`).
- Fail: fine `balancing.fines.inspectionFail[tier]` and a 30-minute fix deadline (pauses offline). If not fixed by the deadline: a second fine and the building goes offline until fixed.

**Energy rating (A–G)**
- Score = `P_actual / P_reference`, where `P_reference` is the auto-router's reference layout power for that building's current delivery (`04-buildings.md`). Bands: A ≤ 0.6, B ≤ 0.8, C ≤ 1.0, D ≤ 1.2, E ≤ 1.5, F ≤ 2.0, G > 2.0.
- Pay multiplier: A 1.15, B 1.08, C 1.00, D 0.95, E 0.90, F 0.85, G 0.80.
- "Permanent" = it stays with the building; it is recomputed when the player commits a change (and shown live in design mode as "Projected rating: B").

**Gas safety certificate**
- Any building with a gas network needs one. Valid for `balancing.inspection.gasCertHours` (default 48 h real time, counts while offline).
- Renewal: a button with a cost and a 10-minute check; passes if there are no gas leaks, no gas layout violations and stress on every gas segment < 0.6.
- Expired: pay × 0.5 until renewed. A reminder appears 2 h before expiry.
