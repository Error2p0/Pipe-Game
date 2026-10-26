"""TEMPORARY hand-checked air table for Phase 1 (06-fluids.md allows this until
the end of Phase 2, when generate.py with CoolProp must replace it).

Models (all textbook, no CoolProp needed):
- density: ideal gas, rho = p M / (R T), M = 0.028965 kg/mol
- viscosity: Sutherland, mu0 = 1.716e-5 Pa s at 273.15 K, S = 110.4 K
- c_p: 1002.5 + 275e-6 (T - 200)^2 J/(kg K)  (fit, +-0.3% over -30..120 C)
- conductivity: Sutherland form, k0 = 0.0241 W/(m K) at 273.15 K, S = 194 K

Hand checks at 20 C, 1 atm: rho 1.2041 (ref 1.204), mu 1.8133e-5 (ref 1.813e-5),
c_p 1004.9 (ref 1005), k 0.02569 (ref 0.0257).

Usage: python tools/fluidtables/handchecked_air.py
"""

import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "HeadLoss", "Data", "fluids", "air.json")

R = 8.314462618
M = 0.028965


def main():
    ts = [-30.0 + 2.0 * i for i in range(76)]
    pmin, pmax, n = 0.8e5, 50e5, 20
    r = (pmax / pmin) ** (1.0 / (n - 1))
    ps = [pmin * r ** i for i in range(n)]
    data = {
        "fluid": "air",
        "source": "hand-checked (ideal gas, Sutherland viscosity and conductivity, c_p fit); temporary until CoolProp generation",
        "temperaturesC": ts,
        "pressuresPa": [round(p, 3) for p in ps],
        "density": [], "viscosity": [], "specificHeat": [], "conductivity": [],
        "molarMass": M,
        "gamma": 1.4,
        "speedOfSound20C": round((1.4 * R * 293.15 / M) ** 0.5, 3),
    }
    for p in ps:
        rho, mu, cp, k = [], [], [], []
        for t in ts:
            tk = t + 273.15
            rho.append(float(f"{p * M / (R * tk):.6g}"))
            mu.append(float(f"{1.716e-5 * (tk / 273.15) ** 1.5 * (273.15 + 110.4) / (tk + 110.4):.6g}"))
            cp.append(float(f"{1002.5 + 275e-6 * (tk - 200.0) ** 2:.6g}"))
            k.append(float(f"{0.0241 * (tk / 273.15) ** 1.5 * (273.15 + 194.0) / (tk + 194.0):.6g}"))
        data["density"].append(rho)
        data["viscosity"].append(mu)
        data["specificHeat"].append(cp)
        data["conductivity"].append(k)
    with open(OUT, "w", newline="\n") as f:
        json.dump(data, f, separators=(",", ":"))
        f.write("\n")
    print("wrote", OUT)


if __name__ == "__main__":
    main()
