# 06 · Fluids

[doc] Each fluid uses its real properties and introduces at least one new mechanic. Later buildings mix several fluids on separate networks.

| Fluid | Unlocks at | Key properties (approx., 20 °C) | New mechanic |
| --- | --- | --- | --- |
| Air | Start | Density 1.2 kg/m³, low viscosity | Friction, bend losses, fan curves, duct leaks, room spread |
| Water (cold) | Tier 2 | Density 998 kg/m³, nearly incompressible | Gravity and elevation, pump head, bursts, water hammer |
| Hot water / heating | Tier 3–4 | Viscosity drops with temperature | Heat loss, insulation, boilers, return loops |
| Natural gas (methane) | Tier 3 | Density ~0.67 kg/m³, lighter than air, flammable 5–15% in air | Leaks rise, detectors, escalation to explosion |
| Refrigerants / cooling | Tier 4 | Phase change in equipment | Aircons and heat pumps as network parts |
| Heavy flammables (propane) | Tier 5 | Denser than air, flammable ~2–10% | Leaks pool low, basement danger |
| Special gases (CO₂, nitrogen, oxygen, hydrogen) | Tier 5 | Each with real density and hazards | Labs and hospitals: purity, oxygen enrichment, asphyxiation zones, hydrogen's wide flammable range |

Property values in the shipped game should come from a reference database (for example the open-source CoolProp library) rather than hand-typed numbers. [doc]

## Property tables [impl]

- `tools/fluidtables/generate.py` uses the CoolProp Python package (`pip install CoolProp`) to write `Assets/HeadLoss/Data/fluids/<id>.json`.
- Grid: temperature −30 °C to 120 °C in 2 K steps; absolute pressure 0.8 bar to 50 bar on a log grid (20 points). Liquids (water) only temperature-dependent at 1 bar plus compressibility for water hammer.
- Per point: density ρ (kg/m³), dynamic viscosity μ (Pa·s), specific heat c_p (J/kg·K), thermal conductivity k (W/m·K). Plus constants: molar mass M (kg/mol), ratio of heats γ, speed of sound at 20 °C.
- Runtime lookup: bilinear interpolation in (T, log p). For ideal gases the solver may use `ρ = p_abs M / (R T)` directly; a test asserts it agrees with the table within 1% for air and methane in the game's range.
- Commit the generated JSON. Phase 1 may start with a hand-checked air table, but must switch to the generated one before Phase 2 ends (`14-roadmap.md`).
- The script also writes `fluids/_sources.md` with the CoolProp version and date.

## Fluid definitions (`fluids.json`) [impl]

| Id | Display | Colour (plain view) | Risky | Hazards | Notes |
| --- | --- | --- | --- | --- | --- |
| `air` | Air | soft blue | no | — | Room species baseline |
| `water` | Water | teal | no | — | Wave speed for hammer from pipe material |
| `hot-water` | Hot water | warm orange | no | scald (no gameplay) | Same tables as water |
| `heating-water` | Heating water | terracotta | no | — | Closed loop |
| `natural-gas` | Natural gas | yellow | yes | LFL 5.0%, UFL 15.0% | Modelled as methane; LHV 50.0 MJ/kg |
| `propane` | Propane | amber | yes | LFL 2.1%, UFL 9.5% | Denser than air (≈ 1.88 kg/m³) |
| `hydrogen` | Hydrogen | pale violet | yes | LFL 4%, UFL 75% | Very light (≈ 0.084 kg/m³), rises fast |
| `oxygen` | Oxygen | white-blue | yes | enrichment > 23.5% | Oxidiser, not flammable itself |
| `nitrogen` | Nitrogen | grey | no | asphyxiant (O₂ < 19.5%) | |
| `co2` | CO₂ | dark grey | no | asphyxiant / toxic (> 5000 ppm sustained; O₂ < 19.5%) | Denser than air |
| `refrigerant` | Refrigerant (R-32) | ice blue | no | — | Equipment-internal only (`05-simulation.md` §4) |

Colours must also pass the colour-blind check in `11-ui-visuals-audio.md` (fluids are also distinguished by line pattern: solid, dashed, dotted, double).

## Hazard thresholds (`fluids.json → hazards`) [impl]

| Hazard | Threshold | Effect (see `08-failures.md`, `10-events.md`) |
| --- | --- | --- |
| Flammable detector alarm | 20% of LFL at a detector cell (no detector: 50% of LFL anywhere in the room, "smelled by occupants") | Alarm and evacuation |
| Explosion possible | Any cell layer between LFL and UFL within 1 m of an ignition source | Explosion when the escalation timer ends |
| Asphyxiation | O₂ < 19.5% in any occupied room's `lower` layer | Evacuation until O₂ ≥ 20.0% |
| CO₂ toxicity | > 5000 ppm room mean | Evacuation until < 3000 ppm |
| Oxygen enrichment | O₂ > 23.5% | Treated as a flammable leak [confirm] |

## Purity (special gases, tier 5) [impl]

Lab and hospital special-gas outlets have a purity requirement (e.g. medical oxygen 99.5%). Purity at an outlet = 100% − Σ contamination:

- Incompatible material on the path: −2% per segment (oxygen requires cleaned copper or stainless; other special gases require stainless).
- Dead legs (pipe stubs with no flow longer than 1 m): −0.5% per metre, capped at −5% per leg.
- Missing check valve where two sources can mix: −10%.

Purity below the requirement means the outlet counts as below minimum.
