"""Writes Assets/HeadLoss/Data/equipment.json: air movers, water movers, panels, gas and safety gear, add-ons and accessories.

07-equipment.md: curves are dp(Q) = dp0 (1 - (Q/Qmax)^2) sampled at 6 points,
with an efficiency curve peaking at 55-65% of Qmax. Here the efficiency is
eta(x) = eta_peak * (1 - ((x - 0.6) / 0.6)^2), x = Q/Qmax, with a floor of
0.1 * eta_peak at shutoff so input power stays finite.

Usage: python tools/equipment/fan_curves.py [small-fan dp0 Pa] [small-fan Qmax m3/h]
"""
import json, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "HeadLoss", "Data", "equipment.json")

import sys

# Small axial fan curve: 07-equipment.md starts at 80 Pa / 350 m3/h; Phase 2 balancing tuned it to 30 Pa / 250 m3/h (D18, D22).
SMALL_DP0 = float(sys.argv[1]) if len(sys.argv) > 1 else 30
SMALL_QMAX = float(sys.argv[2]) if len(sys.argv) > 2 else 250

# id, name, tier, dp0 Pa, qmax m3/h, peak eta (None = no power), speed, footprint w,h,layer, placement, connection mm, price $, notes
FANS = [
    ("hand-crank-fan", "Hand-crank fan", 1, 30, 150, None, "crank", (1, 1, "floor"), "intake", 125, 10, "Only at an air intake; the player cranks it"),
    ("small-axial-fan", "Small axial fan", 1, SMALL_DP0, SMALL_QMAX, 0.35, "on-off", (1, 1, "floor"), "intake", 160, 40, "At an intake"),
    ("inline-duct-fan", "Inline duct fan", 2, 300, 700, 0.45, "on-off", (1, 1, "ceiling"), "inline", 200, 300, "Inline anywhere (also the mid-line air booster)"),
    ("centrifugal-blower", "Centrifugal blower", 3, 900, 1800, 0.60, "on-off", (2, 1, "floor"), "intake", 315, 2500, ""),
    ("ec-fan", "EC fan", 3, 700, 2600, 0.70, "variable", (2, 2, "floor"), "intake", 400, 9000, "Built-in variable speed"),
    ("industrial-ahu", "Industrial air handling unit", 4, 1500, 20000, 0.75, "variable", (4, 3, "floor"), "intake", 1000, 120000, "Slots: filter, heat recovery, heating coil, cooling coil"),
]

def curve(dp0, qmax, peak):
    pts = []
    for i in range(6):
        x = i / 5.0
        eta = 0.0 if peak is None else round(max(0.1 * peak, peak * (1 - ((x - 0.6) / 0.6) ** 2)), 4)
        pts.append({"flowM3h": round(x * qmax, 6), "pressurePa": round(dp0 * (1 - x * x), 6), "efficiency": eta})
    return pts

# Seconds to slow to a stop and to reach full speed (05-simulation.md section 2): big rotors take longer, and pumps,
# with water to brake them, stop sooner than fans of their size.
SPIN = {
    "small-axial-fan": (6, 2), "inline-duct-fan": (6, 2), "centrifugal-blower": (10, 4), "ec-fan": (8, 4), "industrial-ahu": (20, 8),
    "small-booster-pump": (3, 1.5), "circulation-pump": (3, 1.5), "inline-booster-pump": (3, 1.5), "multistage-pump": (5, 2), "vs-pump-set": (6, 3),
}


def spin(fid):
    return {"runDownSeconds": SPIN[fid][0], "spinUpSeconds": SPIN[fid][1]} if fid in SPIN else {}


items = []
for fid, name, tier, dp0, qmax, peak, speed, fp, place, conn, price, notes in FANS:
    items.append({
        "id": fid, "displayName": name, "tier": tier, "category": "air-mover",
        "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]},
        "fluids": ["air"], "powerModel": "none" if peak is None else "curve",
        "isIgnitionSource": peak is not None, "speedControl": speed, "placement": place,
        "curve": curve(dp0, qmax, peak), "reverseK": 5.0, "connectionDiameterMm": conn, **spin(fid), "price": price, "notes": notes,
    })

# Water movers (07-equipment.md): dp(Q) = rho g H0 (1 - (Q/Qmax)^2), same efficiency shape as the fans.
RHO_G = 998.2 * 9.80665
# id, name, tier, shutoff head m, qmax m3/h, peak eta, speed, footprint, placement, connection mm, price $, notes
PUMPS = [
    ("small-booster-pump", "Small booster pump", 2, 25, 3, 0.45, "on-off", (1, 1, "floor"), "intake", 22, 400, "At the water connection"),
    ("multistage-pump", "Multistage pump", 3, 80, 8, 0.60, "on-off", (2, 1, "floor"), "intake", 35, 3500, "At the water connection"),
    ("vs-pump-set", "Variable-speed pump set", 4, 100, 20, 0.70, "variable", (2, 2, "floor"), "intake", 54, 40000, "Built-in variable speed and pressure tank"),
    ("circulation-pump", "Circulation pump", 3, 6, 3, 0.40, "on-off", (1, 1, "ceiling"), "inline", 22, 250, "For hot-water return loops and heating loops"),
    ("inline-booster-pump", "Inline booster pump", 3, 30, 5, 0.50, "on-off", (1, 1, "ceiling"), "inline", 28, 1500, "Mid-line water booster"),
]
for fid, name, tier, head, qmax, peak, speed, fp, place, conn, price, notes in PUMPS:
    extra = {"builtInPressureTank": True} if fid == "vs-pump-set" else {}
    items.append({
        "id": fid, "displayName": name, "tier": tier, "category": "water-mover",
        "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]},
        # The circulation pump moves water round hot-water return loops and heating loops (07), not the cold supply.
        "fluids": ["hot-water", "heating-water"] if fid == "circulation-pump" else ["water"], "powerModel": "curve", "isIgnitionSource": True,
        "speedControl": speed, "placement": place,
        "curve": curve(round(RHO_G * head, 1), qmax, peak), "reverseK": 5.0, "connectionDiameterMm": conn, **spin(fid), "price": price, "notes": notes,
        **extra,
    })

# Gas boosters (07 Gas supply): adds up to 50 mbar, Qmax 30 m3/h, peak efficiency 0.5; at the gas connection after the city
# main, or inline on a long run (07 Mid-line boosters). Category gas-supply so the connection offers it.
for fid, name, place, fp, notes in [
    ("gas-booster-compressor", "Gas booster compressor", "intake", (2, 1, "floor"), "At the gas connection: adds up to 50 mbar to the city's pressure"),
    ("gas-booster-inline", "Inline gas booster", "inline", (1, 1, "ceiling"), "On a long gas run: adds up to 50 mbar where losses pile up"),
]:
    items.append({
        "id": fid, "displayName": name, "tier": 4, "category": "gas-supply",
        "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]},
        "fluids": ["natural-gas"], "powerModel": "curve", "isIgnitionSource": True, "speedControl": "on-off", "placement": place,
        "curve": curve(5000, 30, 0.5), "reverseK": 5.0, "connectionDiameterMm": 28, "runDownSeconds": 4, "spinUpSeconds": 2,
        "price": 8000, "notes": notes,
    })


def plain(fid, name, tier, category, fluids, placement, price, notes, fp=(1, 1, "floor"), ignition=False, **extra):
    d = {"id": fid, "displayName": name, "tier": tier, "category": category,
         "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]}, "fluids": fluids, "powerModel": "none",
         "isIgnitionSource": ignition, "speedControl": "none", "placement": placement, "curve": [], "reverseK": 0,
         "connectionDiameterMm": 0, "price": price, "notes": notes}
    d.update(extra)
    return d


items.append(plain("mains-connection", "Mains pressure only", 2, "water-mover", ["water"], "intake", 0,
                   "A plain connection fitting with no pump: city pressure, weak on upper floors"))
items.append(plain("variable-speed-drive", "Variable-speed drive", 2, "control", ["air"], "addon", 250,
                   "Add-on to an on/off fan: speed slider 20-100%; costs 3% efficiency", ignition=True,
                   efficiencyFactor=0.97, fitsCategories=["air-mover"]))
items.append(plain("variable-speed-drive-pump", "Variable-speed drive (pump)", 2, "control", ["water"], "addon", 600,
                   "Add-on to an on/off pump: speed slider 20-100%; costs 3% efficiency", ignition=True,
                   efficiencyFactor=0.97, fitsCategories=["water-mover"]))
items.append(plain("pressure-tank", "Pressure tank", 2, "water-accessory", ["water"], "inline", 350,
                   "Accumulator teed into a water pipe: surges on its network x accumulatorSurgeFactor", fp=(1, 1, "ceiling")))
# Electrical panels (07 Electrical panels and connections): limit in kW, placed at the power connection.
for fid, name, tier, kw, price, fp, notes in [
    ("panel-basic", "Basic panel", 1, 4, 0, (1, 1, "floor"), "Pre-placed in every building"),
    ("panel-standard", "Standard panel", 2, 12, 500, (1, 1, "floor"), ""),
    ("panel-large", "Large panel", 3, 40, 5000, (1, 1, "floor"), ""),
]:
    items.append(plain(fid, name, tier, "panel", [], "intake", price, notes, fp=fp, ignition=True, panelLimitKW=kw))
# Gas supply and safety gear (07 Gas, 08 Failures).
items.append(plain("gas-regulator", "Gas regulator", 3, "gas-supply", ["natural-gas"], "intake", 150,
                   "At the gas connection: outlet pressure 25 mbar, adjustable 18-30 mbar from tier 4", setpointPa=2500,
                   setpointFromTier=4, setpointMinPa=1800, setpointMaxPa=3000, setpointStepPa=100, regulatorK=2.0))
# The gas manifold (07): fitted on a gas junction, a shutoff for each of its branches (up to 6) and a detector in its room.
items.append(plain("gas-manifold", "Gas manifold", 4, "gas-fitting", ["natural-gas"], "junction", 2000,
                   "Fitted on a gas tee or cross: a shutoff valve on each branch (up to 6) and a built-in detector; counts as the accessible shutoff for every branch",
                   fp=(1, 1, "ceiling"), maxBranches=6))
items.append(plain("gas-detector", "Gas detector", 3, "safety", ["natural-gas"], "room", 80,
                   "Wall-mounted in one room: alarm at 20% of the lower flammable limit at its cell", fp=(1, 1, "none")))
items.append(plain("passive-vent-opening", "Passive vent", 3, "safety", ["air"], "wall", 120,
                   "Exterior-wall opening, high or low: power-free ventilation, about 1 air change an hour for a 30 m\u00b3 room, more or less with the wind",
                   fp=(1, 1, "none")))
items.append(plain("transformer-room", "Transformer room", 4, "panel", [], "intake", 60000,
                   "Needs a 3x3 room on the ground floor or basement; industrial buildings only", fp=(3, 3, "floor"), ignition=True, panelLimitKW=250,
                   buildingTypes=["warehouse", "factory", "lab-building"]))

# Climate devices that join networks (07 Climate equipment, 05 1.5 coupling nodes). Ports are tile offsets in the footprint
# before it is turned: "in" draws the same flow the "out" port sends, "fuel" draws gas for the heat made, "flow"/"return" feed a
# closed loop with an expansion vessel.
items.append(plain("water-heater", "Water heater", 3, "climate", ["water", "hot-water"], "room", 600,
                   "Cold in, hot out at 60 \u00b0C; 3 kW electric. Its hot side takes the cold side's pressure",
                   fp=(2, 1, "floor"), ignition=True, powerModel="heat", connectionDiameterMm=22, resistanceK=2.0,
                   heatOutputW=3000, setpointC=60, thermalEfficiency=0.98,
                   ports=[{"network": "water", "role": "in", "dx": 0, "dy": 0},
                          {"network": "hot-water", "role": "return", "dx": 0, "dy": 0},
                          {"network": "hot-water", "role": "out", "dx": 1, "dy": 0}]))
items.append(plain("gas-boiler", "Gas boiler", 3, "climate", ["natural-gas", "heating-water"], "room", 2500,
                   "24 kW at 92%: burns gas for the heat it gives the loop; flue through an exterior wall",
                   fp=(2, 1, "floor"), ignition=True, needsExteriorWall=True, connectionDiameterMm=22, resistanceK=3.0,
                   heatOutputW=24000, setpointC=70, thermalEfficiency=0.92,
                   ports=[{"network": "natural-gas", "role": "fuel", "dx": 0, "dy": 0},
                          {"network": "heating-water", "role": "return", "dx": 0, "dy": 0},
                          {"network": "heating-water", "role": "flow", "dx": 1, "dy": 0}]))

with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write("[\n")
    f.write(",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items))
    f.write("\n]\n")
print("wrote", OUT)
