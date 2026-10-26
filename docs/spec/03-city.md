# 03 · City, utility network, progression and prestige

## City as hub [doc]

The city is the hub and the level select. Every building is open from the start; physics gates progress instead of locks. A warehouse is selectable on day one, but a hand-crank fan cannot reach its far outlets, so players naturally start with a small house.

- Procedurally generated from a seed: districts (residential, commercial, industrial), roads, and a mix of building types and sizes.
- Each building shows a difficulty hint on hover: size, floors, fluids needed, rough flow demand.
- Online buildings glow and animate; offline ones stay grey. Burst or leak warnings float above the roof.
- Buildings can be revisited any time to optimise, upgrade or repair.

## Building tiers [doc]

| Tier | Examples | Fluids | Floors |
| --- | --- | --- | --- |
| 1 | Small house, kiosk | Air | 1 |
| 2 | Family house, shop | Air, water | 1–2 |
| 3 | Apartment block, restaurant | Air, water, natural gas | 2–4 |
| 4 | Warehouse, office | Air, water, gas, heating/cooling | 1–6 |
| 5 | Factory, lab, hospital | Special gases, flammables, mixed | 2–10 |

Building types per tier and their room programs are in `Data/buildings.json` (`04-buildings.md`).

## City generation [impl]

Deterministic from `citySeed` (PCG32). Steps, each with its own derived sub-seed (`hash(citySeed, "step-name")`) so changing one step does not reshuffle the others:

1. **Grid.** City tile map, default 96 × 96 city tiles (1 city tile = 10 m). Edge band of 4 tiles is countryside (no plots).
2. **Districts.** Voronoi partition from 6–10 seed points; assign district type by weights (residential 50%, commercial 25%, industrial 25%), with industrial biased to one edge and residential to the opposite side. Guarantee at least one of each.
3. **Roads.** Main roads: 2–3 lines crossing the city (perturbed straight lines). Local roads: a grid per district with spacing 4–6 tiles, jittered, connected to main roads. Every plot touches a road.
4. **Rail.** 0–1 rail line (60% chance), crossing the city edge to edge, mostly along industrial districts.
5. **Parks and protected sites.** 2–5 parks (2×2 to 4×4 tiles) in residential/commercial districts. 1–3 protected sites. 2–6 historic buildings in the oldest district (the one nearest the centre).
6. **Facilities.** Place one water treatment plant, one gas receiving station, one district heating plant, one industrial gas depot (in industrial), and one electrical substation per district. Facilities sit on the city edge side of their district where possible, so distance varies by district. Facilities are placed by the procedural city generator, so every city has a different routing puzzle. Distance from a facility is part of a building's difficulty. [doc]
7. **Plots.** Fill blocks between roads with plots. Plot size by building tier (tier 1: 1×1 city tile, tier 2: 1×1, tier 3: 2×1, tier 4: 2×2, tier 5: 3×2). Tier mix by district: residential mostly 1–3, commercial 2–4, industrial 3–5. Target ~120 plots.
8. **Starter guarantee.** At least 3 tier-1 plots within 6 tiles of the city centre, one of which is the **starter house**: 2 rooms, 2 air vents, intake no more than 6 m from the farthest vent, solvable with the hand crank at 70% speed (checked by the solvability check, regenerate its seed until true).
9. **Modifiers.** Roll city modifiers (see Prestige).

Each plot stores `buildingType`, `tier`, `buildingSeed = hash(citySeed, plotId)`. Buildings are generated lazily when first hovered (summary only) or opened (full layout).

### Difficulty hint [doc + impl]

Hover tooltip shows: footprint (m), floors, fluids needed (icons), number of outlets, rough flow demand (sum of minimums, display units), distance to each needed facility, and a 1–5 dot difficulty estimate = f(total demand, max path length from entry to outlet, floors, codes count).

## City utility network [doc]

Every utility except air is supplied from central facilities in the city, so before a building can come online the player must route underground mains to it in the city view. Air is the only local resource: each building draws it from its own facade intake.

### Central facilities and city infrastructure

| Facility | Role | Effect on routing |
| --- | --- | --- |
| Water treatment plant | Supplies clean cold water | Source node for water mains |
| Gas receiving station | Supplies natural gas | Source node for gas mains |
| District heating plant | Supplies hot water for heating (later tier) | Source node, insulated mains |
| Industrial gas depot | Supplies special gases (later tier) | Source node, strictest routing rules |
| Electrical substation | Powers a district | Source node for power cables; risky zone that gas mains must keep clear of |
| Rail lines, main roads | Transport | Crossing allowed but expensive |
| Parks, protected sites | Public space | Crossing restricted |

### Routing in the city view [doc]

- Mains run along a city tile grid, ideally under roads and utility corridors.
- Running mains under houses, through protected sites or too close to risky places (substations, other utility mains) is allowed but counts as a violation.
- Each violation charges a **recurring fine** every income cycle until the route is moved. The player can choose to accept a fine when the shortcut is worth it.
- One main can branch to feed several buildings, so trunk planning matters.

[impl] Routing tool: select utility and main size, drag from a facility (or an existing main of the same utility) along city tiles; release on a plot to connect it to that building's matching entry opening. Live preview shows cost, violations (red tile outlines with a one-line reason each) and the resulting connection pressure. Crossing a main road or rail tile adds a one-off crossing cost (`balancing.city.crossingCost`).

Violation rules (distances in city tiles, data in `codes.json`):

| Violation | Rule | Fine per cycle |
| --- | --- | --- |
| Under house | Main/cable tile under a plot that is not its destination | `fines.underHouse` |
| Protected site | Through a park or protected site | `fines.protectedSite` |
| Near substation | Gas main within 2 tiles of a substation | `fines.nearSubstation` |
| Main separation | Gas and water mains closer than 1 tile apart (not in a utility corridor) | `fines.mainSeparation` |
| Historic foundations | Under a historic building | 5 × `fines.underHouse` [confirm] |

The industrial gas depot's mains ("strictest routing rules") double every fine and add: no special-gas main within 2 tiles of residential plots.

### Underground mains: capacity, not bursts [doc]

- Underground mains never burst.
- Each main has a **maximum flow capacity** set by its diameter (and, for gas, its pressure class). Demand above capacity is shared out across the buildings downstream, so overloading a trunk starves the far end.
- Upgrades: larger trunk diameter, parallel mains, booster pumping or compressor stations along long runs.
- The city view gets its own heatmap: utilisation per main (percent of capacity), so bottlenecks stand out.

The building's connection point is where the city main meets the in-building network; pressure and temperature at that point come from the city network solve.

### City network solve [impl]

- Each utility's mains form a graph solved with the same network solver as buildings (`05-simulation.md`). Facility = fixed-pressure source (`balancing.city.facilityPressure.<utility>`), each connected building = demand node equal to its current building-network inflow.
- Capacity: after solving, any main whose flow exceeds its capacity is limited. Every building downstream of it gets its demand scaled by `capacity / flow` (proportional share), and the solve repeats until stable (max 5 passes). A starved building's connection becomes a source with reduced maximum flow, so its far outlets fall short first.
- Coupling: building solve uses the connection pressure from the last city solve; city solve uses building inflows from the last building solves. Iterate building ↔ city up to 3 times per change; converge tolerance 1%.
- Heating mains carry temperature: heat loss along insulated mains with the pipe thermal model.
- Utilisation heatmap: flow / capacity per main, 0–100%+ (above 100% shown in the warning colour).

## Electricity (city level only) [doc]

Power is a routed city utility like water and gas, but it is never simulated as circuits inside buildings.

- Power cables run from electrical substations to buildings on the same city grid as mains, with the same rules: violations and recurring fines, branching, and a utilisation heatmap.
- Each cable has a maximum capacity in kW instead of m³/h. Demand above capacity browns out the furthest buildings first: fans and pumps run at reduced speed, so flow drops.
- A building's total equipment draw must fit inside its power connection. Big equipment needs a bigger cable or a closer substation.
- Substations have their own capacity per district; upgrading a substation is a late-game city investment.
- Power is a natural late unlock: early tiers assume a basic connection that covers small fans.

This gives substations a purpose beyond being an obstacle, and it reuses the mains system (a cable is a main with a capacity in kW).

[impl]
- Before the power unlock (Phase 4 content), every building has an implicit **basic connection** of `balancing.power.basicConnectionKW` and no cable is needed.
- Brownout allocation: for each overloaded cable or substation, sort the buildings it feeds by cable path length (furthest first) and reduce their available power until the total fits. A building with available power below its draw runs every fan and pump at speed `s = cbrt(available / draw)` (affinity laws: power ∝ speed³). Then re-solve its networks.
- Cable sizes in `pipes.json` (category `cable`) with kW capacity and cost per tile.

## Progression and tech unlocks [impl + confirm]

- Equipment, pipe sizes and materials have a `tier` in data. Tier 1 items are available from the start.
- Tier N+1 unlocks when `balancing.progression.unlockOnline[N]` buildings of tier N are online at the same time (defaults: 2, 3, 3, 2). Unlocked tiers stay unlocked for the rest of the city. Founding a new city resets unlocks, except those granted by prestige bonuses.
- Fluids unlock with tiers as in `06-fluids.md`. A building whose fluids are not unlocked can still be opened and inspected but shows "Requires <fluid> (tier N)".
- The **upgrade shop** lists all unlocked items with price, stats and a "compare with placed" view.

## Prestige: new city [doc]

When a city reaches its milestone, the player can found a new city. The milestone combines both a share of buildings online and a total income target; both must be met. The current city resets; permanent bonuses carry over.

Example permanent bonuses:

- Cheaper electricity contracts
- Starting equipment unlocks (skip the hand crank)
- Higher base pay rates
- Better pipe materials from the start
- Faster leak detection and repair crews

Late prestige upgrade, **Reconstruction permits**: allows rebuilding ruined plots (see `08-failures.md`) at a very high cost.

New cities can also roll modifiers: a cold climate where heating matters more, or old buildings with strict ceiling space.

[impl]
- Milestone defaults: ≥ 60% of non-ruin plots online **and** lifetime city income ≥ `balancing.prestige.incomeTarget[cityIndex]`. Ruined plots count as not online.
- Reward: **Reputation** = `floor(balancing.prestige.k × log10(lifetimeCityIncome))`, minimum 1.
- Prestige tree in `prestige.json`: each node has a cost in Reputation, max level, effect per level. Nodes: `electricity-contracts` (−10% price per level, 5 levels), `starter-kit` (start with small axial fan unlocked and one owned; level 2 adds inline duct fan), `pay-rates` (+10% base pay per level, 10 levels), `materials` (start with copper unlocked; level 2 steel), `rapid-response` (−20% repair and leak-detection time per level, 3 levels), `reconstruction-permits` (unlock rebuilding ruins; requires 3 other nodes).
- City modifiers (0–2 per new city after the first; first city has none): `cold-climate` (outside temperature −10 °C, heating demand ×1.5), `old-buildings` (ceiling capacity 3 instead of 4 in all buildings), more can be added in data.
- Founding a new city: confirmation dialog listing what resets (wallet, unlocks, buildings, ruins, mains) and what stays (Reputation bonuses, settings, statistics). New seed = random unless the player enters one.
