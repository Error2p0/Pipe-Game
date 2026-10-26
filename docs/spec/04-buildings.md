# 04 · Buildings

[doc] Each building is a procedurally generated multi-floor tile grid with procedurally generated outlets and entry openings, and a requirement sheet shown before the player starts.

## Requirement sheet [doc]

Example (3-floor apartment block):

- 12 air vents: min 40 m³/h each, ideal 60, code max 90 (noise)
- 9 water taps: min 1.5 bar, ideal 3, code max 5
- 3 gas stove outlets: min 20 mbar, natural gas
- Budget cap for initial build
- Building codes in force (see `09-codes.md`)

[impl] The sheet is a panel shown when a building is opened for the first time (and on demand with `Tab`/sheet button). It lists, grouped by fluid: outlet count, outlet type, min / ideal / code max with units and the reason for the code max, base pay per outlet, the budget cap, every code in force (short rule + why), entry openings, and the building's tier and floors. Each outlet row later shows live delivery and its zone colour.

## Grid and dimensions [impl]

- Building tile = 1 m × 1 m. Room grid cell = 0.5 m (2 × 2 per tile).
- Floor-to-floor height = 3 m (`balancing.building.floorHeight`). Elevation of a node = floor index × floor height + layer offset (floor layer 0.3 m, ceiling layer 2.7 m, wall cavity 1.5 m).
- Footprints by tier (min–max tiles): T1 8×6–10×8, T2 10×8–14×10, T3 14×10–20×14, T4 20×14–32×22, T5 24×18–40×28.
- Floors per tier as in the tier table in `03-city.md`. Tier 4–5 buildings may have one basement (floor −1) with 40% chance; basements are required for some codes and events (propane, flooding).

## Floors and risers [doc]

- The player switches floors with a tab or hotkey; one floor is shown at a time, with faded ghosts of pipes above and below.
- Vertical risers occupy a shaft tile on every floor they pass through.
- Elevation matters: water loses roughly 0.1 bar per metre climbed, so upper floors need more pump head.
- Hot air rises and gas density matters (lighter-than-air gases collect high, heavy ones low), which affects leaks and ventilation.

[impl]
- Each generated building has 1–3 **shafts** (2×2 tiles, stacked on every floor) plus the stairwell. Risers may only run vertically through shaft tiles. A riser is a vertical segment of length = floor height × floors spanned; its elevation change feeds the solver (`ρ g Δh`).
- Ghosts: pipes on the floor above and below drawn at 20% opacity, no interaction.
- Stairwells and shafts connect room-grid air between floors (vertical exchange), so light gas rises up stairwells and heavy gas sinks to basements.
- Room grid has two vertical layers per cell (`upper`, `lower`) for gas stratification (`05-simulation.md`).

## Layers per floor [doc]

| Layer | Holds | Notes |
| --- | --- | --- |
| Floor | Equipment (fans, pumps, boilers, aircons), outlets, people, furniture | Equipment takes floor space |
| Ceiling | Ducts and pipes | Each tile has limited capacity |
| Wall cavity | Small pipes only | Optional, for later tiers |

Ceiling capacity: each tile holds a capacity budget (for example 4 units). A large duct might take 3 units, a small water pipe 1. Older buildings or prestige modifiers can lower capacity.

[impl]
- Default ceiling capacity 4 units per tile (3 with the `old-buildings` modifier). Capacity units per pipe/duct size are in `pipes.json` (`capacityUnits`).
- Wall cavity: unlocked at tier 4. Only pipes with `capacityUnits == 1` and diameter ≤ 22 mm. Capacity 1 per wall tile edge.
- Floor layer: equipment footprints (1×1 to 4×3 tiles). Equipment cannot overlap furniture, outlets or doors. Furniture is generated decoration that blocks equipment, not pipes.
- Connecting equipment (floor) to ceiling pipes: an automatic vertical drop at the equipment tile, no capacity cost.
- Layer switcher (Floor / Ceiling / Wall cavity) always visible; hotkey `L` cycles.

## Electrical panel [doc]

Each building has one electrical panel placed on the floor layer, with a total kW limit. All powered equipment draws from it; there is no wiring to route. Exceeding the panel limit trips it and stops all equipment until load is reduced. Panel upgrades (larger panel, then a dedicated transformer room for industrial buildings) raise the limit.

[impl]
- The panel is placed at the power connection opening (it is that opening's source equipment). Before power is unlocked, every building starts with a basic panel already placed.
- Trip: when total running draw > panel limit for more than 2 s, the panel trips. All equipment stops, the building goes offline, an icon shows "Panel tripped: 5.4 kW of 4.0 kW". The player resets it with a click after reducing load (or upgrading). It does not auto-reset. Offline: a trip pauses that building's income as offline.
- The available power is `min(panel limit, connection kW, city allocation)`.
- Panel location matters for codes (gas separation, no water above electrics) and for flooding (basement panel floods unless raised).

## Entry openings [doc]

Outlets and entry openings are generated with the rooms; the player never places them. Each required utility gets an opening where its network must start: an air intake on the facade, and connection points where city water, gas, heating and power arrive.

The player starts each network at its opening, places the matching source equipment there, and routes pipes or ducts to every outlet:

| Opening | Source equipment placed there |
| --- | --- |
| Air intake | Fan or air handling unit |
| Water connection | Booster pump (or mains pressure only, early) |
| Gas connection | Regulator, later booster compressor |
| Heating connection | Heat exchanger or boiler |
| Power connection | Electrical panel |

Opening positions are part of each building's difficulty: an intake on the far side of the building means long duct runs and more losses.

[impl] "Mains pressure only" = a plain connection fitting with no pump, free. Special-gas connections (tier 5) take a regulator manifold. Openings are on the ground floor (or basement for water/heating when one exists).

## Procedural generation [doc + impl]

[doc]
- Generate a footprint, then rooms (BSP or room-growth), then doors and walls.
- Place outlets by room type: bathrooms get taps, kitchens get gas, every room gets air.
- Place one entry opening per required utility on outside walls (see Entry openings).
- Run a solvability check: confirm a reference layout with tier-appropriate equipment can meet all minimums before the building is offered.

[impl] Algorithm, deterministic from `buildingSeed`:

1. **Footprint:** rectangle within the tier range; tiers 3+ may become L or U shapes (30%) by cutting a rectangle out.
2. **Fixed elements:** stairwell (2×3) if floors > 1; shafts; structural columns every 6–8 m on a grid in tier 3+ (1×1, block everything on all layers); fire walls in tier 3+ splitting floors over 300 m² into compartments.
3. **Rooms:** BSP split per floor down to room-size ranges from the building type's room program (`buildings.json`: e.g. apartment unit = bedroom ×2, living, kitchen, bathroom, hall). Corridors connect units to the stairwell. Room types: bedroom, living, kitchen, bathroom, hall, corridor, office, shop floor, storage, warehouse hall, workshop, lab, ward, electrical room, plant room, protected room (server/archive: pipes cannot cross).
4. **Doors and walls:** one door per room into a corridor/hall/adjacent room; exterior walls get windows (affect solar gain).
5. **Outlets by room type** (counts and types from the room program): bedrooms/living/offices/wards get air supply vents; kitchens get gas stove outlets (tier 3+), and a cold tap; bathrooms get cold taps (hot taps tier 4+); labs get special-gas outlets and clean-air vents; wards get clean-air vents and oxygen outlets; radiators in occupied rooms when the building needs heating; cooling terminals in offices/wards when it needs cooling.
6. **Requirements per outlet:** from `outlets.json` per type and tier, with ±10% seeded variation, rounded to readable numbers (5 m³/h, 0.1 bar, 1 mbar).
7. **Entry openings:** one per required utility on outside walls, positioned with a seeded bias to make difficulty vary: 50% near the street side (closest to its city main route), 50% anywhere on the facade.
8. **Occupants and heat loads:** people per room from type (bedroom 2, office 1 per 10 m², ward 2 per bed…), machines in workshops/labs, windows facing south/west add solar gain (`05-simulation.md`).
9. **Codes in force:** base codes for the tier plus building-type specials (`09-codes.md`).
10. **Budget cap:** `balancing.building.budgetCapFactor × referenceLayoutCost` (default 1.6).
11. **Solvability check:** the auto-router (below) builds a reference layout with the tier's reference equipment (`13-balancing.md`). If any minimum fails or any code is violated, nudge (move openings, then regenerate rooms with a new sub-seed, max 20 attempts). If still failing, mark the plot as a tier lower. Log failures in a dev report.

### Auto-router (dev tool and solvability check) [impl]

Not exposed to the player. A* over the ceiling layer per fluid, respecting walls (doors and openings are passable), columns, protected rooms, capacity, and codes. Builds a tree: trunk from the opening toward the centroid of outlets, branches to each outlet, picks the smallest pipe size meeting a velocity limit (air 4 m/s trunk, 2.5 m/s branch; water 1.5 m/s), uses long-radius elbows when tier allows, risers through the nearest shaft. Then sizes the source equipment from the tier list (smallest that meets all minimums). Output: a `ReferenceLayout` (cost, power, delivery per outlet). Also used by tests and balancing reports.

## Building screen states [impl]

`NotStarted` → `Designing` (never online) → `Online` ↔ `Offline(reason)` → `Ruin`. Reasons: below minimum, blocking violation, burst, panel tripped, evacuated (gas/asphyxiation/flood), flooded panel, power outage, no city connection.
