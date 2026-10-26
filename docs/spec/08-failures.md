# 08 · Failure systems

[doc] Pipes fail randomly but fairly: running above rated pressure builds stress, and the further and longer you push, the likelier a burst. Flammable leaks escalate from alarm to explosion if ignored.

All random rolls use the building's failure RNG stream (`Rng` seeded from `buildingSeed` + failure counter) so results are reproducible in tests.

---

## Burst model [doc]

- Each segment has a rated pressure from its material and diameter.
- Below rating: no stress builds (slow optional ageing in later tiers).
- Above rating: stress accumulates each tick, faster the higher the overpressure ratio.
- Each tick, burst chance rises with accumulated stress (a Weibull-style hazard curve). Result: "holds X pressure for roughly X time, then bursts," with fair randomness.
- Hovering a pipe shows its stress as a bar, so players see trouble coming.
- Relief valves and regulators are the player's protection: they cap pressure on a branch so a mistake can't propagate.

[impl]

```
x       = p_max_segment / p_rated            // highest pressure along the segment, incl. water-hammer surges
dS/dt   = k_s · max(0, x − 1)^a              // stress, dimensionless; k_s, a in balancing (defaults 1/3600 s⁻¹, 1.5)
h(S)    = (β / η) · (S / η)^(β − 1)          // Weibull hazard per unit stress; defaults β = 3, η = 1
P_burst this tick = 1 − exp( −h(S) · dS )    // probability uses the stress gained this tick
```

- With the defaults, a segment held at 2× its rating reaches S = 1 in about an hour, where the cumulative burst chance is ~63%. At 1.2× it takes ~11 hours. This is the "holds X for roughly X time" feel.
- Stress never decays on its own. Repairing or replacing a segment resets it to 0.
- Slow ageing (tier 4+, optional, `balancing.failure.ageing`): `dS/dt += k_age` even below rating, so very old untouched pipes eventually need attention. Off by default; enable only if the user approves.
- **Weeping** (pre-burst): at S ≥ 0.6 the segment weeps (a tiny leak: water drips, air hisses) and shows a droplet icon. Pipe-wall leak sensors alert at this point.
- **Stress heatmap** (mode 7) colours segments by S (0 → 1+). Hover shows the stress bar, current x, and (with engineering software) expected time to 50% burst chance.
- Ducts burst too (seam failure): same model with duct ratings in Pa.
- Underground city mains never burst (`03-city.md`).

### Burst effects on income [doc]

| State | Player in game | Player offline |
| --- | --- | --- |
| Burst on water or air line | Building income cut to 0, warning above the roof | Income cut by 75% |
| Repaired | Back to normal immediately | Back to normal when repaired on return |

[impl] A burst segment is removed from the network (open end: water spills, air escapes) and its branch outlets lose delivery. Closing a shutoff upstream isolates it (`07-equipment.md` §Control parts) and lifts the building-wide cut. A burst on a gas line becomes a gas leak (below), not a burst.

### Repairs [doc]

Fixing a burst: click the segment, choose a repair option, and optionally upgrade the segment in the same action. The normal burst income rules apply until the repair finishes.

| Repair option | Cost | Time |
| --- | --- | --- |
| Emergency crew | Expensive | Fast (minutes) |
| Standard crew | Cheap | Slow (hours; keeps working while offline) |

Maintenance contracts dispatch the player's preferred option automatically while offline.

[impl] Defaults: emergency 3 minutes at 5× the standard price; standard 2 hours. Prices scale with segment size and building tier (`13-balancing.md`). The prestige bonus `rapid-response` shortens both. A repair in progress shows a crew icon and a progress ring. Only one crew per building at a time; extra repairs queue (emergency jumps the queue).

---

## Flammable gas escalation [doc]

1. **Leak:** a segment or fitting starts leaking. Gas spreads through the room grid and the concentration heatmap shows it.
2. **Alarm and evacuation:** once concentration passes a detector threshold, the building evacuates and pays nothing.
3. **Explosion:** if the leak is not fixed within a timer, and concentration in a room sits between the lower and upper flammability limits near an ignition source, the building explodes.
4. **Aftermath:** a large fine, and the building is permanently damaged for this city.

[impl]
- **What starts a leak:** a gas segment reaching burst (stress model), a weeping gas segment (small leak, 5% of the burst leak area), a random fitting leak (`balancing.failure.fittingLeakPerHour`, default 0.0005 per gas fitting per hour, flat rate), explosion damage, and the power-outage cascade (`10-events.md`).
- **Leak rate:** orifice equation `Q = C_d · A · sqrt(2 Δp / ρ)` with `C_d = 0.6` and leak area from the cause (data). Injected into the room grid at the leak's cell (`05-simulation.md` §3).
- **Alarm:** threshold from `06-fluids.md` (20% LFL at a detector, or 50% LFL anywhere without detectors). On alarm: building evacuates (income 0), alarm icon above the roof, the **explosion timer** starts (`balancing.failure.explosionTimerSeconds`, default 600 s), countdown shown above the building and in the building view.
- **Fixing:** the player closes the gas supply (shutoff/manifold valve or auto-shutoff) to stop the leak, and repairs the leaking part (same repair options). Ventilation then clears the gas. When concentration falls below 10% LFL everywhere and the leak is repaired, evacuation ends.
- **Explosion check:** when the timer reaches 0, if any cell layer is between LFL and UFL within 1 m of an ignition source → explosion. If not, the timer restarts (the danger continues until fixed), and the building stays evacuated.
- **Ignition sources:** the electrical panel, any equipment with `isIgnitionSource`, and gas stoves and boilers (burner). Room lights and appliances are not modelled. Spark-free variants are not ignition sources.
- **Automatic shutoff valve** closes the supply at the alarm stage and on power loss, which stops escalation entirely ([doc]): the timer does not start.

### Prevention gear [doc]

Gas detectors (earlier alarm), automatic shutoff valves (stop the leak at the alarm stage), ventilation that keeps concentration below the lower flammability limit, spark-free equipment in risky rooms.

### Offline rules for gas [doc]

While the player is offline, a leak escalates to alarm and evacuation (building pays nothing), but the explosion timer pauses. When the player returns, the timer resumes with a clear countdown above the building. An automatic shutoff valve stops escalation entirely.

[impl] On return, the "While you were away" report lists every building waiting with a paused timer at the top, with a "Go there" button.

---

## Explosions [doc]

### Ruins

An exploded building becomes a ruin. The plot cannot earn and cannot be rebuilt in this city, except with the late prestige upgrade Reconstruction permits at a very high cost. Founding a new city clears ruins.

### Blast radius and chain reactions

- Explosion size scales with the amount of flammable gas in the building when it ignites (leak volume and concentration).
- Buildings within the blast radius take damage that falls off with distance. Light damage knocks pipes and equipment offline (repairable); heavy damage near the centre ruins the building.
- A damaged building that carries flammable gas can start its own leak, which can escalate and explode in turn. Dense industrial districts can therefore chain-react.
- Every damaged building adds its own fine on top of the original explosion fine.
- This makes spacing, shutoff valves and detectors on gas buildings a city-planning decision, not only a building one.

[impl]
```
E          = Σ_cells (flammable gas volume in flammable range) · ρ_gas · LHV · yieldFactor   // J; yieldFactor 0.1
radius_m   = k_r · E^(1/3)                     // cube-root scaling; with k_r = 0.07 a kitchen leak (~2 kg methane) ≈ 15 m, a factory hall (~35 kg) ≈ 40 m
damage(d)  = clamp01(1 − d / radius)           // d = distance from the exploding building's centre to the other building's nearest edge
```
- `damage ≥ 0.6` → heavy → ruin. `0.1 ≤ damage < 0.6` → light: each piece of equipment and each segment in that building goes offline (needs repair) with probability = damage; a gas building with light damage starts a leak with probability = damage (it then follows normal escalation, including the alarm and its own timer, so a chain reaction plays out over minutes, never instantly). `< 0.1` → no effect.
- Damage to city mains: none (they never burst). Facilities in the radius: no damage (out of scope, keeps it calm).
- Fines: explosion fine = `balancing.fines.explosion[tier]`; each damaged building adds `balancing.fines.neighbourDamage × damage × its tier factor`. Insurance halves fines, not damage.
- Presentation: a soft expanding ring and a quiet low thump, the building turns into a dark broken outline. No screen shake, no flashing (calm by default).
- A ruin shows "Ruin · cannot be rebuilt in this city" (or "Rebuild with Reconstruction permit: $X" once that prestige node is owned). Rebuilding regenerates the same building from its seed with no player edits.
- Ruins count as plots that are not online for the prestige milestone.
