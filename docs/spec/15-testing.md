# 15 · Testing

[doc] Keep simulation in plain C# so it can be unit-tested against known textbook pipe problems. Phase 1 includes unit tests against textbook pipe problems.

## Test layout [impl]

- **EditMode, pure** (`HeadLoss.Core.Tests`, `HeadLoss.Sim.Tests`, `HeadLoss.Game.Tests`): physics, rules, generation, save, offline. No Unity types. Fast (whole suite < 30 s).
- **PlayMode** (`HeadLoss.PlayMode.Tests`): scene boot, drawing tool, commit flow, UI smoke, performance budgets, Burst-vs-reference comparisons.
- Run through the Unity CLI (`CLAUDE.md` §2). Every phase ends with a green full run.
- Naming: `MethodOrFeature_Condition_Expected`.
- Tolerances are relative unless stated.

## Physics reference cases (expected values computed independently) [impl]

Fluid properties for these cases use the fixed values given (not the tables), so the tests check the math. A second set of tests repeats T3 and T4 with table properties and a 1% looser tolerance.

| Id | Case | Inputs | Expected | Tolerance |
| --- | --- | --- | --- | --- |
| T1a | Swamee-Jain vs Colebrook | Re = 1e5, ε/D = 1e-4 | Colebrook f = 0.018514; Swamee-Jain f = 0.018452 | SJ within 1% of Colebrook |
| T1b | Swamee-Jain vs Colebrook | Re = 1e6, ε/D = 1e-3 | Colebrook 0.019943; Swamee-Jain 0.020029 | within 1% |
| T2 | Laminar | Re = 1000 | f = 0.064 | 1e-9 |
| T2b | Transition | Re = 3000 | between f(2300) laminar and f(4000) SJ, linear | exact |
| T3 | Water pipe friction | ρ 998.2, μ 1.002e-3, D 0.05 m, L 100 m, ε 0.045 mm, Q 2 L/s | v 1.0186 m/s, Re 50 736, f 0.023838, Δp 24 688 Pa (0.2469 bar) | 0.5% |
| T4 | Air duct with fittings | ρ 1.204, μ 1.813e-5, D 0.1 m, L 10 m, ε 0.15 mm, Q 100 m³/h; fittings: 2 × elbow K 1.2 + exit K 1.0 | v 3.5368 m/s, Re 23 487, f 0.028361, q 7.5303 Pa, Δp friction 21.357 Pa, total 46.960 Pa | 0.5% |
| T4b | Same with long-radius elbows (K 0.3) | as T4 | total 33.405 Pa | 0.5% |
| T5 | Fan operating point, sharp vs long-radius (the Phase 1 gate case) | Fan Δp = 100 · (1 − (Q/300)²) Pa with Q in m³/h; system = T4 duct (sharp ΣK 3.4) | Q 132.26 m³/h, Δp 80.56 Pa | 0.5% |
| T5b | Same, long-radius (ΣK 1.6) | | Q 152.11 m³/h, Δp 74.29 Pa (≈ +15% flow) | 0.5% |
| T6 | Parallel pipes split | water as T3, total 5 L/s; pipe 1 D 0.05 L 50; pipe 2 D 0.04 L 30; ε 0.045 mm | Q1 2.9010 L/s, Q2 2.0990 L/s, Δp 24 761 Pa | 0.5% |
| T7 | Hydrostatic | water ρ 998.2, 10 m rise | 0.97890 bar (≈ the doc's 0.1 bar per metre) | 0.1% |
| T8 | Joukowsky | ρ 998.2, a 1200 m/s, Δv 1.5 m/s, instant | 1.7968 MPa | 0.1% |
| T9 | Pipe cooling | T_in 60 °C, T_amb 20 °C, D 0.02 m, L 20 m, U 10 W/m²K, ṁ 0.05 kg/s, c_p 4180 | T_out 57.666 °C | 0.01 K |
| T10 | Affinity laws | any fan, s = 0.5 | at the operating point on a pure quadratic system: Q × 0.5, Δp × 0.25, P × 0.125 | 0.5% |
| T11 | Ideal gas density | air at 0 Pa gauge, 20 °C; methane same | 1.2041 kg/m³ (M 0.028965); 0.6669 kg/m³ (M 0.016043) | 0.5% |
| T12 | Weibull stress | x = 2, defaults | S = 1 after 3600 s; cumulative burst probability at S = 1 → 1 − e⁻¹ = 0.6321 (Monte Carlo over 20 000 seeded trials) | ±0.01 |
| T13 | Weibull at x = 1.2 | defaults | S = 1 after 40 249 s (11.18 h) | 0.5% |
| T14 | Payout curve | min 40, ideal 60, max 90, non-risky | r=1 → m 1; d 60 → 1; d 90 (rCap 2.25) → 2.0; d 91 → 0 (code max); d 39 → building offline; risky same → 2.5 at 90 | exact |
| T15 | Payout curve, no code max | min 20, ideal 30, no max | d 100 (r 5) → 2.0; d 200 → 2.0 | exact |
| T16 | Explosion radius | 2 kg methane in range, LHV 50 MJ/kg, yield 0.1, k_r 0.07 | 15.08 m | 0.5% |

## Solver behaviour tests [impl]

- Mass conservation at every junction (< 1e-9 m³/s) on random generated networks (200 seeds).
- Convergence within 60 iterations on all auto-router reference layouts for tiers 1–3.
- Same input → bit-identical output (determinism) across two runs.
- Closed valve removes the branch; check valve blocks reverse flow; regulator caps downstream pressure; relief opens above set pressure.
- Series fans add pressure; parallel fans add flow (compare to analytic curve sums).
- Loop network (Hardy Cross textbook three-loop example) matches an independent solution computed by a separate simple iterative solver in the test.

## Game rule tests [impl]

- Income formula: each multiplier and penalty in order (`02-economy.md`), negative net, debt accrual and repayment.
- Burst income: in game 0, offline 0.25, isolated branch exempt.
- Gas escalation: leak → alarm at threshold → timer → explosion only when in flammable range near ignition; timer pauses offline; auto-shutoff prevents the timer.
- Chain reaction: two gas buildings in range; second one leaks and escalates on its own timer.
- City capacity sharing: overloaded trunk scales downstream demands proportionally; power brownout reduces the furthest building first.
- Codes: one positive and one negative test per code.
- Generation: same seed → same city and buildings; starter guarantee; every generated tier-1–3 building passes the solvability check (200 seeds per tier).
- Save: round trip; migration from every sample version; atomic write survives a simulated crash.
- Offline vs real-time equivalence (`12-save-offline.md`).
- Data validator on all data files; every id reference resolves.
