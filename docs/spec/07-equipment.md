# 07 · Equipment, pipes, fittings and upgrades

[doc] Upgrades come in two kinds: equipment that adds energy to the flow, and network parts that waste less of it.

Physical stats live here and in `Data/equipment.json`, `pipes.json`, `materials.json`, `fittings.json`. Prices are in `13-balancing.md`. Every item has: `id`, `displayName`, `tier`, `category`, `footprint` (tiles, layer), `fluids` it works with, `powerModel`, `isIgnitionSource` (true for anything electrical or with a flame unless spark-free), and its physics data.

---

## Air movers [doc]

1. Hand-crank fan (player clicks to spin; the only active-play device)
2. Small axial fan
3. Inline duct fan
4. Centrifugal blower
5. Variable-speed EC fan
6. Industrial air handling unit

[impl] Curves are generated in data as `Δp(Q) = Δp0 · (1 − (Q/Qmax)²)` sampled at 6 points, with an efficiency curve peaking at 55–65% of `Qmax`.

| Id | Tier | Δp0 (Pa) | Qmax (m³/h) | Peak η | Speed control | Footprint | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `hand-crank-fan` | 1 | 30 | 150 | — (no power) | crank | 1×1 floor | Only at an air intake |
| `small-axial-fan` | 1 | 80 | 350 | 0.35 | on/off | 1×1 floor | At an intake |
| `inline-duct-fan` | 2 | 300 | 700 | 0.45 | on/off | 1×1 ceiling | Inline anywhere (also the mid-line air booster) |
| `centrifugal-blower` | 3 | 900 | 1 800 | 0.60 | on/off | 2×1 floor | |
| `ec-fan` | 3 | 700 | 2 600 | 0.70 | built-in variable speed | 2×2 floor | |
| `industrial-ahu` | 4 | 1 500 | 20 000 | 0.75 | built-in variable speed | 4×3 floor | Slots: filter, heat recovery, heating coil, cooling coil |

All fans can serve a supply network or an exhaust network.

## Water movers [doc]

1. Mains pressure only (free, fixed, weak on upper floors)
2. Small booster pump
3. Multistage pump
4. Variable-speed pump set with pressure tank

| Id | Tier | Shutoff head (m) | Qmax (m³/h) | Peak η | Speed control | Footprint |
| --- | --- | --- | --- | --- | --- | --- |
| `mains-connection` | 2 | — (city pressure) | — | — | — | opening only |
| `small-booster-pump` | 2 | 25 | 3 | 0.45 | on/off | 1×1 |
| `multistage-pump` | 3 | 80 | 8 | 0.60 | on/off | 2×1 |
| `vs-pump-set` | 4 | 100 | 20 | 0.70 | built-in variable speed + built-in pressure tank | 2×2 |
| `circulation-pump` [impl] | 3 | 6 | 3 | 0.40 | on/off | 1×1 inline | For hot-water return loops and heating loops |
| `inline-booster-pump` [impl] | 3 | 30 | 5 | 0.50 | on/off | 1×1 inline | Mid-line water booster |

## Gas supply [doc]

1. Mains regulator (fixed outlet pressure)
2. Booster compressor
3. Gas manifold with shutoff valves and detectors

| Id | Tier | Data |
| --- | --- | --- |
| `gas-regulator` | 3 | Outlet setpoint 25 mbar (fixed; adjustable 18–30 mbar from tier 4) |
| `gas-booster-compressor` | 4 | Adds up to 50 mbar, Qmax 30 m³/h, η 0.5 |
| `gas-manifold` | 4 | Distribution node with up to 6 branch shutoff valves and one built-in detector; counts as the "accessible shutoff" for each branch |
| `special-gas-manifold` [impl] | 5 | Regulator + manifold for special gases, purity-safe (stainless) |

## Mid-line boosters [doc]

Booster fans and pumps placed along a long run restore pressure where losses pile up. They cost power and ceiling or floor space. ([impl] `inline-duct-fan`, `inline-booster-pump`, `gas-booster-compressor` inline variant.)

## Climate equipment [doc]

- Heater, boiler, aircon, heat pump
- Heat recovery unit (recuperator): exchanges heat between exhaust and fresh air, cutting heating or cooling cost

| Id | Tier | Capacity | Efficiency | Needs |
| --- | --- | --- | --- | --- |
| `electric-heater` | 3 | 2 kW air heating | 100% | power |
| `gas-boiler` | 3 | 24 kW | 92% | gas network, exterior wall tile (flue), feeds heating loop or hot water |
| `water-heater` | 3 | 3 kW, setpoint 60 °C | 98% | power; cold in, hot out |
| `district-heat-exchanger` | 4 | 50 kW | ε 0.8 | heating connection |
| `aircon-split` | 4 | 3.5 kW cooling | COP 3.5 | outdoor unit on exterior wall, refrigerant line ≤ 15 m |
| `heat-pump` | 4 | 8 kW heat/cool | COP 3.5 | outdoor unit, power |
| `heat-recovery-unit` | 3 | up to 1 500 m³/h | ε 0.70 | supply and exhaust networks through it |
| `hr-unit-high-eff` | 4 | up to 5 000 m³/h | ε 0.85 | as above |
| `heating-coil` / `cooling-coil` | 4 | AHU slot | — | heating loop / chilled supply |
| `lab-chiller` [impl] | 5 | Keeps a gas storage tank cold | fixed 3 kW draw | power; safety-critical |

## Pipes and ducts [impl]

### Materials (`materials.json`)

| Id | Kind | Roughness ε (mm) | Rated pressure | U uninsulated (W/m²K) | Wave speed (m/s) | Fluids | Tier |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `flex-duct` | duct | 3.0 | 750 Pa | 4 | — | air | 1 |
| `galvanized-duct` | duct | 0.15 | 1 500 Pa | 6 | — | air | 1 |
| `spiral-duct` | duct | 0.05 | 2 500 Pa | 6 | — | air | 3 |
| `pvc` | pipe | 0.0015 | 6 bar at 22 mm | 3 | 400 | cold water only (≤ 45 °C) | 2 |
| `copper` | pipe | 0.0015 | 10 bar at 22 mm | 10 | 1 200 | water, hot water, heating, natural gas, oxygen (cleaned) | 3 |
| `steel` | pipe | 0.045 | 25 bar at 25 mm | 10 | 1 200 | water, heating, all fuel gases | 3 |
| `stainless` | pipe | 0.015 | 40 bar at 25 mm | 10 | 1 200 | everything, purity-safe | 4 |

[doc] Material upgrade path PVC → copper → steel → stainless: lower roughness, higher pressure rating, gas-safe.

Rated pressure scales with diameter (thin-wall hoop stress): `p_rated(D) = p_ref · D_ref / D`.

### Sizes (`pipes.json`)

| Family | Sizes | Ceiling capacity units |
| --- | --- | --- |
| Round duct | 80, 100, 125, 160, 200, 250, 315, 400, 500 mm | 80–100: 1 · 125–200: 2 · 250–315: 3 · 400–500: 4 |
| Pipe (OD) | 15, 22, 28, 35, 42, 54, 76, 108 mm | 15–42: 1 · 54–76: 2 · 108: 3 |
| Water main | 100, 150, 200, 300, 400 mm | city; capacity at 2 m/s |
| Gas main | 63, 90, 125, 180 mm × pressure class LP (75 mbar) / MP (2 bar) / HP (7 bar, tier 5) | city; capacity at 20 m/s (LP 10 m/s) |
| Heating main | 80, 125, 200 mm pre-insulated | city; U 0.3 |
| Special-gas main | 25, 50 mm stainless | city |
| Power cable | 50, 150, 400, 1 000, 2 500 kW | city |

Pipe internal diameter = OD − 2 × wall (wall from data per material and size). Larger size = more capacity units, more price.

### Insulation

| Id | Thickness | U (W/m²K) | Tier |
| --- | --- | --- | --- |
| none | — | material U | — |
| `insulation-13` | 13 mm | 1.5 | 2 |
| `insulation-25` | 25 mm | 0.8 | 3 |
| `insulation-50` | 50 mm | 0.45 | 4 |

Insulation adds 0 capacity units for sizes ≤ 42 mm, +1 above.

### Duct sealing

Duct leakage `Q_leak = C_L · A_surface · p^0.65` per segment. Leakage classes: unsealed `C_L = 1.0e-4`, sealed (tier 2 upgrade) `2.5e-5`, premium (tier 4) `8e-6` (m³/s per m² per Pa^0.65). Leaked air enters the room below the segment and does not count for outlets.

## Fittings (`fittings.json`) [impl]

| Id | K | Tier | Notes |
| --- | --- | --- | --- |
| `elbow-90-sharp` | 1.2 (ducts), 0.9 (pipes) | 1 | Auto-placed at every 90° turn by default |
| `elbow-90-long-radius` | 0.3 | 2 | Drawing option once unlocked; "upgrade bends" button per network |
| `elbow-45-sharp` / `elbow-45-long` | 0.4 / 0.15 | 1 / 2 | Diagonal runs (tier 2+, 45° drawing mode) |
| `tee-sharp` | run 0.4, branch 1.0 | 1 | |
| `tee-smooth` | run 0.2, branch 0.6 | 2 | |
| `reducer` | contraction 0.2; expansion (1 − A₁/A₂)² | 1 | Auto-placed at size changes |
| `intake-entry` | 0.5 | 1 | At the air intake |
| `vent-grille` | 2.0 (referenced to grille face) | 1 | Outlet grille, fixed by the outlet |
| `shutoff-valve` | 0.2 open | 2 | |
| `ball-valve` | 0.05 open | 3 | |
| `balancing-valve` | 6 at 100% open, `K(θ) = K_open / θ²` | 2 | θ = 0.05–1.0 |
| `damper` | 0.3 at 100% open, `K(θ) = K_open / θ²` | 1 | |
| `check-valve` | 2.0 | 2 | Blocks reverse flow |
| `fire-damper` | 0.5 | 3 | Required where ducts cross fire walls |
| `backflow-preventer` | 10 | 5 | Required by code at some connections |
| `filter-basic` | 50 Pa at rated flow, ∝ Q² | 3 | |
| `filter-hepa` | 250 Pa at rated flow, ∝ Q² | 5 | Required for clean-air outlets |

## Network upgrades [doc]

| Upgrade | Effect |
| --- | --- |
| Larger diameter | Much lower friction (losses scale roughly with 1/D⁵ at fixed flow), takes more ceiling space |
| Better material (PVC → copper → steel → stainless) | Lower roughness, higher pressure rating, gas-safe |
| Long-radius elbows, smooth tees | Lower fitting losses |
| Insulation | Less heat loss or gain along runs |
| Duct sealing | Fewer air leaks |

[impl] Upgrades are applied per segment (click), per selection (box select) or per network ("Upgrade all bends"). Upgrading in design mode previews and costs the difference on Commit.

## Control parts [doc]

- Dampers and balancing valves: limit flow to a branch to send more elsewhere
- Pressure regulators: cap pressure downstream
- Relief valves: vent above a set pressure to protect pipes
- Check valves: stop backflow
- Shutoff valves: isolate a branch for repair without taking the building offline

[impl] Shutoff semantics (this is how "isolate a branch without taking the building offline" works): closing a shutoff valve removes its branch from the network. Outlets on an isolated branch pay 0 but are **exempt from the minimum check** while isolated, so the rest of the building stays online. A burst on an isolated branch no longer triggers the building-wide burst income cut; only that branch stops earning. This is the player's tool to limit the damage of a burst (the user's original "restriction" idea), together with regulators and relief valves. Regulator setpoint adjustable in steps of 0.1 bar / 1 mbar / 10 Pa. Relief valve set pressure adjustable; vented water costs nothing; vented gas is released outdoors (safe) or into the room if no outdoor vent line is routed [impl].

## Building equipment (approved additions) [doc]

| Item | Effect |
| --- | --- |
| Smart building controller | Re-balances dampers and valves automatically as conditions change, so a building tunes itself while the player is away |
| Variable-speed drive | Sets fan or pump speed precisely instead of on/off; large electricity savings |
| Pressure tank / accumulator | Smooths pressure spikes (water hammer); pipes build less stress |
| Pipe-wall leak sensors | Detect small leaks before they become bursts |
| Air filters (basic → HEPA) | Required for clean-air outlets in hospitals and labs; adds pressure loss |
| Backup generator | Keeps critical equipment running during power outages (see `10-events.md`) |

[impl]
- **Smart building controller** (tier 3, 20 W): every 30 s (and once at the start of offline catch-up), runs coordinate descent over every damper/balancing valve position (±5% steps) and VSD speed (±0.02), re-solving the network, and keeps changes that raise the building's net income without breaking minimums or code maxima. Max 40 solves per run. Shows a log of what it changed.
- **Variable-speed drive** (tier 2): add-on to any on/off fan or pump; adds a speed slider; costs 3% efficiency.
- **Pressure tank / accumulator** (tier 2): placed on a water network; surge factor ×0.2 for that network.
- **Pipe-wall leak sensors** (tier 3): per network. When a segment's stress passes the weep threshold (`08-failures.md`), it alerts immediately ("Weeping joint, burst likely in ~2 h") instead of the player only seeing it on the stress heatmap.
- **Backup generator** (tier 3): sizes 5 kW, 20 kW, 100 kW. During a power outage it powers the equipment flagged `safetyCritical` first (ventilation in gas rooms, sump pumps, lab chillers, detectors, automatic shutoffs), then everything else up to its size. Runtime fuel cost per hour while running (`13-balancing.md`).

## Safety gear [doc + impl]

From the failure and outage sections of the doc (prevention gear, protection):

| Id | Tier | Effect |
| --- | --- | --- |
| `gas-detector` | 3 | Wall-mounted, one room; alarm at 20% LFL at its cell (earlier alarm) |
| `auto-shutoff-valve` | 3 | Closes the gas supply at alarm and on power loss; stops escalation entirely |
| spark-free variants | 3 | Any electrical item can be bought spark-free (×1.5 price): not an ignition source |
| `passive-vent-opening` | 3 | Exterior-wall opening, high or low; power-free ventilation (≈ 1 ACH for a 30 m³ room, scaled by wind factor 0.5–1.5 from climate) |
| `sump-pump` | 4 | Generated in every basement, placed already, `safetyCritical`, 0.5 kW while running |
| raised panel option | 4 | Panel mounted above flood level (+25% panel price) |
| `fire-damper` | 3 | See fittings |
| `backflow-preventer` | 5 | See fittings |

## Electrical panels and connections [doc + impl]

| Id | Tier | Limit |
| --- | --- | --- |
| `panel-basic` | 1 | 4 kW (pre-placed in every building) |
| `panel-standard` | 2 | 12 kW |
| `panel-large` | 3 | 40 kW |
| `transformer-room` | 4 | 250 kW, needs a 3×3 room on the ground floor or basement; industrial buildings only |

## City equipment [doc]

| Item | Effect |
| --- | --- |
| Booster pumping / compressor station | Restores pressure along long mains |
| Utility corridor | Shared trench under a road; several mains and cables run together cheaply, with no separation violations between them |
| Directional drilling | Crosses under protected sites or rail legally, for a high one-off cost instead of a recurring fine |
| Trunk main sizes and pressure classes | Upgrade path for main capacity |

[impl] Booster station: placed on a main tile; water +3 bar (Qmax by size), gas +2 bar (MP). Draws power from the nearest substation (counts against its capacity). Substation upgrades: 2 MW → 5 MW → 12 MW (tier 4–5).

## Meta upgrades [doc]

| Upgrade | Effect |
| --- | --- |
| Insurance | Lowers fines, at a recurring premium |
| Engineering software | Shows a pipe stress forecast before a design is committed |
| Maintenance contracts | Repair bursts automatically while offline, at a cost |

[impl]
- **Insurance:** city-wide toggle. Premium = `balancing.insurance.premiumFraction` (default 3%) of gross income per cycle; all fines × 0.5. Lowers fines but not damage ([doc], `10-events.md`).
- **Engineering software:** one-off purchase (tier 3). In design mode, the stress heatmap shows projected stress after 24 h at the previewed steady state, and each segment's hover shows "Expected time to 50% burst chance".
- **Maintenance contracts:** per building, recurring fee per cycle. While offline, bursts are dispatched automatically with the player's preferred repair option (`08-failures.md`).
