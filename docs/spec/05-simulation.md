# 05 · Simulation

[doc] Pipes and ducts are solved as a graph network; only open rooms use a grid. This keeps the game real, fast, deterministic and able to run offline earnings analytically.

All quantities SI inside the simulation. Gauge pressure (relative to atmosphere, `p_atm = 101 325 Pa`) unless a name ends in `Abs`.

---

## 1. Network solver (pipes and ducts) [doc]

- Nodes: junctions, outlets, equipment ports, the mains entry. Edges: pipe or duct segments.
- Each edge stores length, internal diameter, roughness (by material), elevation change and a sum of fitting loss coefficients (elbows, tees, valves).
- Friction uses Darcy-Weisbach, with the friction factor from the Swamee-Jain approximation of Colebrook (laminar below Re ≈ 2300).

```
Δp = ( f · L/D + ΣK ) · ρ v² / 2 + ρ g Δh
```

- Sharp 90° elbow K ≈ 0.9 to 1.5, long-radius elbow K ≈ 0.2 to 0.4. This is the "sharp corners lose flow" mechanic, straight from real tables.
- Solve steady-state flow with Newton-Raphson on nodal pressures (the global gradient algorithm used by EPANET). Re-solve only when the network changes or equipment settings change, not every frame.
- Gases at low speeds (Mach < 0.3) are treated as incompressible with density from the ideal gas law at local pressure and temperature. High-pressure gas lines in later tiers can add a compressible correction.

### 1.1 Graph model [impl]

**Node kinds**

| Kind | Unknown | Notes |
| --- | --- | --- |
| `Junction` | pressure | Tees, bends, reducers, equipment internal ports. Zero demand. |
| `FixedPressure` | — (known) | Air intake (0 Pa), room discharge behind an air vent (0 Pa), city connection (pressure from city solve), heating-loop expansion vessel (set pressure), facility source in the city graph. |
| `Demand` | pressure | Water taps, gas outlets, special-gas outlets: fixed design flow out of the network. Delivery = node pressure. |

**Link kinds**

| Kind | Δp(Q) |
| --- | --- |
| `Pipe` (pipe or duct segment, including the fittings at its ends) | `(f L/D + ΣK) · ρ/(2A²) · Q|Q|` + elevation term |
| `Vent` (air outlet) | `K_grille · ρ/(2A_grille²) · Q|Q|`, discharges to a FixedPressure room node. Delivery = Q (m³/s → shown m³/h). |
| `Fan` / `Pump` / `Compressor` / `Booster` | `−Δp_device(Q, s)` (adds pressure, see §2) |
| `Valve` (shutoff, damper, balancing valve, check valve, fire damper) | `K(θ) · ρ/(2A²) · Q|Q|`; closed = removed from the graph for the solve |
| `Regulator` (pressure-reducing) | Active-set control: keeps downstream pressure ≤ setpoint |
| `Relief` | Opens to a FixedPressure "vent/drain" node when upstream pressure > set |
| `HeatExchanger`, `Filter`, `Coil` | Pipe-like loss with a K or Δp-at-rated-flow from data |
| `Leak` | Orifice to room: `Q = C_d A_leak sqrt(2|Δp|/ρ)` (gas, water) or duct leakage `Q = C_L · A_surface · p^0.65` (air) |

Every fitting placed by the drawing tool (elbow at each turn, tee at each branch, reducer at each size change) adds its K to the adjacent segment's `ΣK` using the K referenced to that segment's velocity. Tees use separate K for run and branch (`fittings.json`).

**Elevation:** each node has an elevation (`04-buildings.md` §Grid). For constant-density fluids use piezometric pressure `p* = p + ρ g z` internally and convert back. For gases, add `ρ_link g Δz` per link with the link's density.

### 1.2 Friction factor [impl]

```
Re = ρ v D / μ
f  = 64 / Re                                                 Re ≤ 2300
f  = 0.25 / [ log10( ε/(3.7 D) + 5.74 / Re^0.9 ) ]²          Re ≥ 4000   (Swamee-Jain)
f  = linear interpolation between f(2300) and f(4000)        2300 < Re < 4000
```

Non-circular ducts use hydraulic diameter `D_h = 4A/P` for friction and true area for velocity.

### 1.3 Solver: global gradient algorithm [impl]

Follow Todini & Pilati (1988) as implemented in EPANET 2 (Rossman, EPANET 2 Users Manual, Appendix D). Per iteration:

1. For each link k with current flow `Q_k`, evaluate `Δp_k(Q_k)` and its derivative `g_k = dΔp_k/dQ`. Use `g_min` regularisation near zero flow (`|g_k| ≥ 1e-6 · scale`) and a laminar-like linear term for `|Q| < 1e-9 m³/s`.
2. `p_k = 1/g_k`, `y_k = p_k · Δp_k(Q_k)`.
3. Assemble the symmetric positive definite matrix over unknown-pressure nodes: `A_ii = Σ p_k` over links at i, `A_ij = −p_k` for the link between i and j. Right side: `F_i = Σ_inflow (Q_k − y_k) − Σ_outflow (Q_k − y_k) − demand_i + Σ p_k · P_fixed` for links to fixed-pressure neighbours.
4. Solve `A P = F` (sparse; preconditioned conjugate gradient with incomplete Cholesky, or sparse Cholesky with AMD ordering; a dense LDLᵀ reference for tests).
5. Update flows: `Q_k ← Q_k − ( y_k − p_k (P_i − P_j) )` with the link oriented i → j.
6. Converged when `Σ|ΔQ| / Σ|Q| < 1e-6` (and max node mass imbalance < 1e-9 m³/s). Max 60 iterations. If the residual grows for 3 iterations, apply relaxation 0.5. If not converged, keep the previous valid solution, mark the network `Unstable` and log details (dev only; the player sees "Network still settling").
7. Status checks between iterations (EPANET style): check valves close on reverse flow; regulators switch between active / open / closed; relief valves open or close; repeat until statuses are stable.

Initial guess: last solution if topology unchanged, else velocity 1 m/s in every link.

Gas density: after each iteration, update every link's density from the ideal gas law `ρ = p_abs M / (R T)` using the link's mean pressure and temperature. Warn (dev and hover) when `v > 0.3 c` (speed of sound `c = sqrt(γ R T / M)`). Tier 5 high-pressure lines: use the mean density over the segment and iterate (compressible correction).

### 1.4 Results per solve

Per link: Q, v, Re, f, Δp (friction / fittings / elevation split), p at both ends, temperature in/out, mass flow. Per node: p, T. Per outlet: delivery, zone, multiplier. Per device: operating point (Q, Δp, η, P). The hover readout and heatmaps read from this.

### 1.5 Multiple networks and coupling

- One graph per fluid system per building: air supply, air exhaust (optional), cold water, hot water, heating loop, natural gas, each special gas, cooling (refrigerant lines are equipment-internal, see §4).
- Coupling nodes: a water heater/boiler is a demand on the cold-water network and the source of the hot-water network with the same flow; a gas boiler is a demand on the gas network sized from its heat output (`Q_gas = P_heat / (η_boiler · LHV · ρ_gas)`). Solve in dependency order (hot → cold demand, heat → gas demand) and iterate twice.
- A building's networks depend on the city solve through connection pressures; see `03-city.md`.

---

## 2. Fans and pumps [doc]

- Each model has a performance curve (pressure rise vs. flow) and an efficiency curve, stored as a few points and interpolated.
- The operating point is where the device curve meets the system resistance, which falls out of the network solve naturally.
- Variable-speed devices scale with the affinity laws: flow ∝ speed, pressure ∝ speed², power ∝ speed³.

[impl]
- Curves in `equipment.json` as 5–8 points `(Q m³/h, Δp Pa, η)` from `Q = 0` (shutoff pressure) to `Q_max` (Δp = 0). Interpolate with monotone cubic (Fritsch–Carlson) so there are no overshoots.
- Beyond `Q_max`: extend linearly with the last segment's slope (negative Δp = the device becomes a resistance). Reverse flow: `Δp = Δp(0) − K_rev ρ/(2A²) Q|Q|`.
- Speed `s ∈ [0, 1]`: `Δp(Q, s) = s² · Δp_1(Q / s)`, `η(Q, s) ≈ η_1(Q / s)`. `s = 0` → device is a closed valve.
- Pumps display head `H = Δp / (ρ g)` in m; fans display Pa.
- Devices without a variable-speed drive run at `s = 1` when on, `s = 0` when off. With a VSD (or an EC fan / variable-speed pump set) the player sets `s` from 0.2 to 1.0 in 0.01 steps. The hand crank's `s` comes from cranking (`02-economy.md`).
- Devices in series and parallel work naturally in the graph.
- Shutoff head is the highest pressure the device can create: the burst model (`08-failures.md`) uses it when outlets are closed.

---

## 3. Room grid (air spreading and temperature) [doc]

- Each room is a coarse 2D grid (about 0.5 m cells). Vents inject air; return grilles and leaks remove it.
- A simple diffusion-advection step spreads air and heat, enough to show dead corners and drafts on the heatmap without full CFD.
- Rooms have heat loads (people, machines, sun on walls) and lose heat through outside walls.

### 3.1 Fields per cell [impl]

Cell = 0.5 m × 0.5 m, two vertical layers (`upper` = above 1.5 m, `lower`). Per layer: temperature T (°C), species volume fractions (each gas present in the building, CO₂, O₂), and derived density. Per cell (2D): velocity (vx, vy). Cell flags: wall, door, window, exterior wall, stairwell/shaft (vertical link), ignition source, detector, occupant, machine.

### 3.2 Step (5–10 Hz) [impl]

1. **Sources and sinks.** Each supply vent injects its solved flow at its cell (ceiling vents into `upper`, floor vents into `lower`) with the supply temperature and species mix. Exhaust/return grilles remove flow (high-level grilles from `upper` only). Duct leaks inject into the cell under the leak. Gas leaks inject their leak flow at the leaking node's cell (ceiling layer → `upper`).
2. **Room balance.** Net inflow (supply − exhaust − leaks out) leaves through door cells (to the neighbouring room) and through exterior-wall leakage cells (to outside), split by conductance. Rooms are treated as near-ambient pressure.
3. **Velocity field.** Solve the potential `∇²φ = s` (s = net source per cell) with no-flux walls, 20 SOR iterations warm-started from the last step; `v = −∇φ`. Add a decaying jet in front of each vent along its facing direction: `v_jet = v_face · exp(−d / L_jet)` with `L_jet` from data (default 2 m). This is what shows drafts and dead corners.
4. **Advection.** Semi-Lagrangian advection of every scalar field (unconditionally stable).
5. **Diffusion.** Explicit diffusion with eddy diffusivity `D_t` (default 0.02 m²/s), substepped so `dt ≤ dx² / (4 D_t)`.
6. **Heat.** Add occupants (100 W each, sensible 70 W), machines (data per machine), solar gain on window cells (W/m² by facade orientation × time-of-day curve, `balancing.climate`), and exterior wall loss `q = U_wall · A · (T − T_outside)`. Heat capacity of the room air plus a thermal mass factor (default 5× air) so temperatures move calmly.
7. **Stratification.** Between `lower` and `upper` in the same cell, buoyancy transfer of each species and of heat: light species and warm air move up, heavy species and cold air move down, at rate `k_b · |ρ_species − ρ_air| / ρ_air` per second (default `k_b` = 0.5). Plus slow mixing (0.02 /s).
8. **Between floors.** Stairwell/shaft cells exchange `upper` of floor n with `lower` of floor n+1 at rate `k_v` (default 0.1 /s), so light gas climbs and heavy gas sinks to the basement.
9. **Occupant CO₂.** Each occupant adds 0.005 L/s of CO₂ to `lower`; supply air carries outdoor CO₂ (420 ppm).
10. **Oxygen.** O₂ starts at 20.9%; nitrogen or CO₂ leaks displace it; oxygen leaks enrich it.

Room-level values used by rules: mean T, mean and max concentration per species and layer, CO₂ (ppm), O₂ (%), air changes per hour `ACH = 3600 · Q_supply / V_room`.

The CPU reference implementation lives in `Sim`; the Burst implementation in `Unity.Jobs`. A test runs both for 200 steps on a sample building and compares fields (max abs difference < 1e-3 relative).

---

## 4. Thermal model [doc]

- Fluid temperature changes along each pipe based on insulation, pipe material and surroundings.
- Equipment like boilers, aircons and heat recovery units add or move heat.
- Fluid properties (density, viscosity, specific heat) are looked up per fluid from real tables as a function of temperature.

[impl]
- Along a segment (steady state):
  ```
  T_out = T_amb + (T_in − T_amb) · exp( −U · π · D_o · L / (ṁ · c_p) )
  ```
  `U` (W/m²K, per outer area) from material and insulation (`materials.json`, `pipes.json`). `T_amb` = room temperature of the cells the segment passes over (ceiling layer → `upper`). Heat lost or gained is added to those room cells.
- Junctions mix by mass flow: `T = Σ ṁ_i T_i / Σ ṁ_i`.
- Thermal solve runs after the flow solve at ~1 Hz, in flow order (topological sort; loops iterate 3 times).
- Equipment:
  - Heater / boiler: raises outlet temperature toward its setpoint, limited by capacity `P_max`; gas boilers draw gas, electric heaters draw electricity.
  - Water heater: cold network in, hot network out at setpoint (default 60 °C).
  - Heat exchanger (district heating connection): `ε`-NTU with effectiveness from data (default 0.8).
  - Aircon: cools supply air toward setpoint, capacity `Q_cool`, electricity `Q_cool / COP(T_out, T_in)` with `COP = COP_rated · (1 − 0.02 · (T_outside − 35))` clamped [1.5, 6].
  - Heat pump: same model, heating or cooling mode.
  - Heat recovery unit (recuperator): `T_supply = T_fresh + ε (T_exhaust − T_fresh)`, ε 0.6–0.85 by model; needs both supply and exhaust networks through it.
  - Radiator: `ε = 1 − exp(−UA / (ṁ c_p))`, `T_out = T_in − ε (T_in − T_room)`, heat to room `ṁ c_p (T_in − T_out)`.
  - Refrigerant lines (indoor ↔ outdoor unit): equipment-internal, phase change not simulated in pipes; the line has a max length (data) and adds a capacity loss of 1% per metre beyond 7.5 m.
- Condensation check (`09-codes.md`): pipe surface temperature (≈ fluid temperature for uninsulated pipes) below the room's dew point, using per-room-type relative humidity from data (bathroom 70%, kitchen 60%, other 45%).

---

## 5. Tick rates [doc]

| System | Update | Why |
| --- | --- | --- |
| Network solve | On change, then every ~1 s for slow drift | Steady state is cheap to keep |
| Room grid | 5–10 Hz | Smooth heatmap animation |
| Thermal | ~1 Hz | Changes slowly |
| Failure checks | ~1 Hz | Stress accumulation |
| Offline | Analytic | Steady-state income × time, then roll failure events |

[impl] Additional game ticks: economy 10 Hz (smooth wallet), events and inspections 1 Hz, city solve on change and every 5 s. Only buildings that are open in the building view run the room grid at full rate; other buildings run it at 0.2 Hz with 4× larger time steps (enough for gas leaks, CO₂ and temperature rules). A building whose state is steady (no leaks, no transients) may run its room grid at 0.05 Hz.

---

## 6. Water hammer [impl]

Water's new mechanic includes water hammer ([doc], `06-fluids.md`). Model as transient pressure spikes:

- Triggers: a shutoff valve closing, a pump starting or stopping, a panel trip, a power outage, a burst elsewhere on the same network, a check valve closing on reverse flow.
- Joukowsky surge: `Δp_surge = ρ · a · Δv`, with wave speed `a` per material (steel/copper 1200 m/s, PVC 400 m/s). If the change takes longer than `2L/a` (L = distance to the nearest reflecting boundary: tank, source, open end), scale by `(2L/a) / t_change`. Manual valve closing takes 1 s; pump stop 0.5 s; burst 0.05 s.
- The spike is applied to every segment of the affected network for `balancing.hammer.durationSeconds` (default 2 s) on top of steady pressure, and feeds the stress model (`08-failures.md`).
- A pressure tank / accumulator on the network reduces surges on that network by its factor (default ×0.2).
- The pressure heatmap flashes nothing: show the surge as a short ripple animation along the pipe and a hover note "Surge: +3.1 bar". Calm by default.
