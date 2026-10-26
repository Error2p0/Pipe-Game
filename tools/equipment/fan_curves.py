"""Writes the air-mover entries of Assets/HeadLoss/Data/equipment.json.

07-equipment.md: curves are dp(Q) = dp0 (1 - (Q/Qmax)^2) sampled at 6 points,
with an efficiency curve peaking at 55-65% of Qmax. Here the efficiency is
eta(x) = eta_peak * (1 - ((x - 0.6) / 0.6)^2), x = Q/Qmax, with a floor of
0.1 * eta_peak at shutoff so input power stays finite.

Usage: python tools/equipment/fan_curves.py
"""
import json, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "HeadLoss", "Data", "equipment.json")

# id, name, tier, dp0 Pa, qmax m3/h, peak eta (None = no power), speed, footprint w,h,layer, placement, connection mm, notes
FANS = [
    ("hand-crank-fan", "Hand-crank fan", 1, 30, 150, None, "crank", (1, 1, "floor"), "intake", 125, "Only at an air intake; the player cranks it"),
    ("small-axial-fan", "Small axial fan", 1, 80, 350, 0.35, "on-off", (1, 1, "floor"), "intake", 160, "At an intake"),
    ("inline-duct-fan", "Inline duct fan", 2, 300, 700, 0.45, "on-off", (1, 1, "ceiling"), "inline", 200, "Inline anywhere (also the mid-line air booster)"),
    ("centrifugal-blower", "Centrifugal blower", 3, 900, 1800, 0.60, "on-off", (2, 1, "floor"), "intake", 315, ""),
    ("ec-fan", "EC fan", 3, 700, 2600, 0.70, "variable", (2, 2, "floor"), "intake", 400, "Built-in variable speed"),
    ("industrial-ahu", "Industrial air handling unit", 4, 1500, 20000, 0.75, "variable", (4, 3, "floor"), "intake", 1000, "Slots: filter, heat recovery, heating coil, cooling coil"),
]

def curve(dp0, qmax, peak):
    pts = []
    for i in range(6):
        x = i / 5.0
        eta = 0.0 if peak is None else round(max(0.1 * peak, peak * (1 - ((x - 0.6) / 0.6) ** 2)), 4)
        pts.append({"flowM3h": round(x * qmax, 6), "pressurePa": round(dp0 * (1 - x * x), 6), "efficiency": eta})
    return pts

items = []
for fid, name, tier, dp0, qmax, peak, speed, fp, place, conn, notes in FANS:
    items.append({
        "id": fid, "displayName": name, "tier": tier, "category": "air-mover",
        "footprint": {"width": fp[0], "height": fp[1], "layer": fp[2]},
        "fluids": ["air"], "powerModel": "none" if peak is None else "curve",
        "isIgnitionSource": peak is not None, "speedControl": speed, "placement": place,
        "curve": curve(dp0, qmax, peak), "reverseK": 5.0, "connectionDiameterMm": conn, "notes": notes,
    })

with open(OUT, "w", newline="\n") as f:
    f.write("[\n")
    f.write(",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items))
    f.write("\n]\n")
print("wrote", OUT)
