"""Writes Assets/HeadLoss/Data/equipment.json: air movers, water movers, panels, add-ons and accessories.

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

items = []
for fid, name, tier, dp0, qmax, peak, speed, fp, place, conn, price, notes in FANS:
    items.append({
        "id": fid, "displayName": name, "tier": tier, "category": "air-mover",
        "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]},
        "fluids": ["air"], "powerModel": "none" if peak is None else "curve",
        "isIgnitionSource": peak is not None, "speedControl": speed, "placement": place,
        "curve": curve(dp0, qmax, peak), "reverseK": 5.0, "connectionDiameterMm": conn, "price": price, "notes": notes,
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
    items.append({
        "id": fid, "displayName": name, "tier": tier, "category": "water-mover",
        "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]},
        "fluids": ["water"], "powerModel": "curve", "isIgnitionSource": True, "speedControl": speed, "placement": place,
        "curve": curve(round(RHO_G * head, 1), qmax, peak), "reverseK": 5.0, "connectionDiameterMm": conn, "price": price, "notes": notes,
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
    ("transformer-room", "Transformer room", 4, 250, 60000, (3, 3, "floor"), "Needs a 3x3 room on the ground floor or basement; industrial buildings only"),
]:
    items.append(plain(fid, name, tier, "panel", [], "intake", price, notes, fp=fp, ignition=True, panelLimitKW=kw))

with open(OUT, "w", newline="\n") as f:
    f.write("[\n")
    f.write(",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items))
    f.write("\n]\n")
print("wrote", OUT)
