using System;

namespace HeadLoss.Sim.Rooms
{
    /// <summary>
    /// CPU reference room grid step (<c>05-simulation.md</c> §3.2), single precision so the Burst version can match
    /// it. Order: 1 sources (vents inject into the upper layer, occupants breathe CO₂ into the lower one), 2 room
    /// balance (<see cref="RoomGridMath.NetSources"/>), 3 velocity from the potential ∇²φ = −q with no flux through
    /// walls (SOR, warm start) plus a decaying jet in front of each vent, 4 semi-Lagrangian advection, 5 explicit
    /// diffusion (sub-stepped), 6 heat (sources and exterior wall loss with thermal mass), 7 stratification (unstable
    /// layers overturn at k_b·|Δρ|/ρ, slow mixing, heavy CO₂ settles), 8 exchange between floors at stairwells.
    /// The sign of q is taken as a net source (air flows away from vents), see D63.
    /// </summary>
    public sealed class RoomGridReference : IRoomGridStepper
    {
        float[] _a = Array.Empty<float>(), _b = Array.Empty<float>();

        public void Step(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p, float dt)
        {
            int n = l.Cells;
            if (_a.Length != 2 * n)
            {
                _a = new float[2 * n];
                _b = new float[2 * n];
            }
            RoomGridMath.NetSources(l, inp, s.NetSource);
            Sources(l, s, inp, p, dt);
            Potential(l, s, p);
            Velocity(l, s, inp, p);
            Advect(l, s.T, s, p, dt);
            Advect(l, s.Co2, s, p, dt);
            Advect(l, s.O2, s, p, dt);
            Diffuse(l, s, p, dt);
            Heat(l, s, inp, p, dt);
            Stratify(l, s, inp, p, dt);
            Floors(l, s, p, dt);
        }

        static void Sources(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p, float dt)
        {
            int n = l.Cells;
            float area = p.CellSize * p.CellSize, vu = area * p.UpperHeight, vl = area * p.LowerHeight;
            for (int c = 0; c < n; c++)
            {
                if (l.Room[c] < 0) continue;
                float q = inp.SupplyFlow[c];
                if (q > 0)
                {
                    float f = Math.Min(1f, q * dt / vu), ft = Math.Min(1f, q * dt / (vu * p.ThermalMass));
                    s.Co2[n + c] += f * (inp.OutdoorCo2Ppm - s.Co2[n + c]);
                    s.O2[n + c] += f * (inp.OutdoorO2 - s.O2[n + c]);
                    s.T[n + c] += ft * (inp.SupplyTemperature[c] - s.T[n + c]);
                }
                float co2 = inp.Co2Lower[c];
                if (co2 > 0) s.Co2[c] += co2 * dt / vl * 1e6f;
            }
        }

        /// <summary>SOR on φ: Σ_open (φ_c − φ_n) = q / H over each cell (H = room air height).</summary>
        static void Potential(RoomGridLayout l, RoomGridState s, RoomGridParams p)
        {
            float h = p.LowerHeight + p.UpperHeight, w = p.SorOmega;
            for (int it = 0; it < p.SorIterations; it++)
                for (int f = 0; f < l.Floors; f++)
                    for (int y = 0; y < l.NY; y++)
                        for (int x = 0; x < l.NX; x++)
                        {
                            int c = l.Index(f, x, y);
                            if (l.Room[c] < 0) continue;
                            float sum = 0;
                            int k = 0;
                            if (x + 1 < l.NX && l.OpenE[c]) { sum += s.Phi[c + 1]; k++; }
                            if (x > 0 && l.OpenE[c - 1]) { sum += s.Phi[c - 1]; k++; }
                            if (y + 1 < l.NY && l.OpenN[c]) { sum += s.Phi[c + l.NX]; k++; }
                            if (y > 0 && l.OpenN[c - l.NX]) { sum += s.Phi[c - l.NX]; k++; }
                            if (k == 0) continue;
                            float target = (sum + s.NetSource[c] / h) / k;
                            s.Phi[c] += w * (target - s.Phi[c]);
                        }
        }

        /// <summary>v = −∇φ at cell centres (average of the two face velocities, zero through walls) plus vent jets.</summary>
        static void Velocity(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p)
        {
            float cs = p.CellSize;
            for (int f = 0; f < l.Floors; f++)
                for (int y = 0; y < l.NY; y++)
                    for (int x = 0; x < l.NX; x++)
                    {
                        int c = l.Index(f, x, y);
                        if (l.Room[c] < 0) { s.Vx[c] = s.Vy[c] = 0; continue; }
                        float ue = x + 1 < l.NX && l.OpenE[c] ? (s.Phi[c] - s.Phi[c + 1]) / cs : 0;
                        float uw = x > 0 && l.OpenE[c - 1] ? (s.Phi[c - 1] - s.Phi[c]) / cs : 0;
                        float un = y + 1 < l.NY && l.OpenN[c] ? (s.Phi[c] - s.Phi[c + l.NX]) / cs : 0;
                        float us = y > 0 && l.OpenN[c - l.NX] ? (s.Phi[c - l.NX] - s.Phi[c]) / cs : 0;
                        s.Vx[c] = 0.5f * (ue + uw);
                        s.Vy[c] = 0.5f * (un + us);
                    }
            foreach (VentJet j in inp.Jets) Jet(l, s, p, j);
        }

        /// <summary>A jet along the vent's throw: v_face · exp(−d / L_jet), half as strong in the two side cells, stopped by walls.</summary>
        static void Jet(RoomGridLayout l, RoomGridState s, RoomGridParams p, VentJet j)
        {
            int c = j.Cell;
            int steps = (int)Math.Ceiling(3 * p.JetLength / p.CellSize);
            for (int k = 0; k < steps; k++)
            {
                float v = j.FaceVelocity * (float)Math.Exp(-k * p.CellSize / p.JetLength);
                s.Vx[c] += v * j.DirX;
                s.Vy[c] += v * j.DirY;
                // Side spread: perpendicular neighbours that share an open face.
                if (j.DirX != 0)
                {
                    if (CanStep(l, c, 0, 1)) { s.Vx[c + l.NX] += 0.5f * v * j.DirX; }
                    if (CanStep(l, c, 0, -1)) { s.Vx[c - l.NX] += 0.5f * v * j.DirX; }
                }
                else
                {
                    if (CanStep(l, c, 1, 0)) { s.Vy[c + 1] += 0.5f * v * j.DirY; }
                    if (CanStep(l, c, -1, 0)) { s.Vy[c - 1] += 0.5f * v * j.DirY; }
                }
                if (!CanStep(l, c, j.DirX, j.DirY)) break;
                c += j.DirX + j.DirY * l.NX;
            }
        }

        /// <summary>True when air may move from cell c one step in (dx, dy) within its floor.</summary>
        public static bool CanStep(RoomGridLayout l, int c, int dx, int dy)
        {
            int x = c % l.NX, y = c / l.NX % l.NY;
            if (dx > 0) return x + 1 < l.NX && l.OpenE[c];
            if (dx < 0) return x > 0 && l.OpenE[c - 1];
            if (dy > 0) return y + 1 < l.NY && l.OpenN[c];
            return y > 0 && l.OpenN[c - l.NX];
        }

        /// <summary>Semi-Lagrangian advection of both layers of a scalar: sample the old field where the air came from.</summary>
        void Advect(RoomGridLayout l, float[] field, RoomGridState s, RoomGridParams p, float dt)
        {
            int n = l.Cells;
            Array.Copy(field, _a, 2 * n);
            float scale = dt / p.CellSize;
            for (int f = 0; f < l.Floors; f++)
                for (int y = 0; y < l.NY; y++)
                    for (int x = 0; x < l.NX; x++)
                    {
                        int c = l.Index(f, x, y);
                        int room = l.Room[c];
                        if (room < 0) continue;
                        float gx = x - s.Vx[c] * scale, gy = y - s.Vy[c] * scale;
                        int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
                        float fx = gx - x0, fy = gy - y0;
                        for (int layer = 0; layer < 2; layer++)
                        {
                            int o = layer * n;
                            float sum = 0, wsum = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int sx = x0 + (k & 1), sy = y0 + (k >> 1);
                                if (sx < 0 || sy < 0 || sx >= l.NX || sy >= l.NY) continue;
                                int sc = l.Index(f, sx, sy);
                                int sr = l.Room[sc];
                                if (sr < 0 || (sr != room && !l.Door[c] && !l.Door[sc])) continue;
                                float w = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
                                sum += w * _a[o + sc];
                                wsum += w;
                            }
                            field[o + c] = wsum > 1e-6f ? sum / wsum : _a[o + c];
                        }
                    }
        }

        /// <summary>Explicit diffusion through open faces, sub-stepped so dt ≤ dx² / (4 D_t).</summary>
        void Diffuse(RoomGridLayout l, RoomGridState s, RoomGridParams p, float dt)
        {
            float limit = p.CellSize * p.CellSize / (4 * p.Diffusivity);
            int sub = Math.Max(1, (int)Math.Ceiling(dt / limit));
            float coef = p.Diffusivity * (dt / sub) / (p.CellSize * p.CellSize);
            for (int i = 0; i < sub; i++)
            {
                DiffuseOnce(l, s.T, coef);
                DiffuseOnce(l, s.Co2, coef);
                DiffuseOnce(l, s.O2, coef);
            }
        }

        void DiffuseOnce(RoomGridLayout l, float[] field, float coef)
        {
            int n = l.Cells;
            Array.Copy(field, _b, 2 * n);
            for (int c = 0; c < n; c++)
            {
                if (l.Room[c] < 0) continue;
                int x = c % l.NX, y = c / l.NX % l.NY;
                for (int layer = 0; layer < 2; layer++)
                {
                    int o = layer * n;
                    float v = _b[o + c], d = 0;
                    if (x + 1 < l.NX && l.OpenE[c]) d += _b[o + c + 1] - v;
                    if (x > 0 && l.OpenE[c - 1]) d += _b[o + c - 1] - v;
                    if (y + 1 < l.NY && l.OpenN[c]) d += _b[o + c + l.NX] - v;
                    if (y > 0 && l.OpenN[c - l.NX]) d += _b[o + c - l.NX] - v;
                    field[o + c] = v + coef * d;
                }
            }
        }

        /// <summary>Heat sources and exterior wall loss q = U·A·(T − T_out), into air with thermal mass.</summary>
        static void Heat(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p, float dt)
        {
            int n = l.Cells;
            float area = p.CellSize * p.CellSize, height = p.LowerHeight + p.UpperHeight;
            float capL = p.RhoCp * area * p.LowerHeight * p.ThermalMass, capU = p.RhoCp * area * p.UpperHeight * p.ThermalMass;
            for (int c = 0; c < n; c++)
            {
                if (l.Room[c] < 0) continue;
                float aL = l.ExteriorArea[c] * p.LowerHeight / height, aU = l.ExteriorArea[c] * p.UpperHeight / height;
                float qL = inp.HeatLower[c] - p.WallU * aL * (s.T[c] - inp.OutsideTemperature);
                float qU = inp.HeatUpper[c] - p.WallU * aU * (s.T[n + c] - inp.OutsideTemperature);
                s.T[c] += qL * dt / capL;
                s.T[n + c] += qU * dt / capU;
            }
        }

        /// <summary>
        /// Between the two layers of each cell: when the lower layer is lighter (warmer) the layers overturn at
        /// k_b·|Δρ|/ρ per second, always plus the slow mixing; heavy CO₂ above the outdoor level settles at
        /// k_b·(M_CO₂ − M_air)/M_air. Transfers conserve each scalar's amount (volume-weighted).
        /// </summary>
        static void Stratify(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p, float dt)
        {
            int n = l.Cells;
            float vl = p.LowerHeight, vu = p.UpperHeight, wv = vl * vu / (vl + vu);
            float settle = p.Stratification * Math.Abs(p.Co2MolarMass - p.AirMolarMass) / p.AirMolarMass;
            for (int c = 0; c < n; c++)
            {
                if (l.Room[c] < 0) continue;
                float rl = RoomGridMath.Density(p, s.T[c], s.Co2[c]), ru = RoomGridMath.Density(p, s.T[n + c], s.Co2[n + c]);
                float rate = p.LayerMixing + (rl < ru ? p.Stratification * (ru - rl) / ((rl + ru) * 0.5f) : 0);
                float f = Math.Min(0.5f, rate * dt);
                Exchange(s.T, c, n, f, vl, vu, wv);
                Exchange(s.Co2, c, n, f, vl, vu, wv);
                Exchange(s.O2, c, n, f, vl, vu, wv);
                float excess = s.Co2[n + c] - inp.OutdoorCo2Ppm;
                if (excess > 0)
                {
                    float m = Math.Min(0.5f, settle * dt) * excess * vu;
                    s.Co2[n + c] -= m / vu;
                    s.Co2[c] += m / vl;
                }
            }
        }

        static void Exchange(float[] x, int c, int n, float f, float vl, float vu, float wv)
        {
            float m = f * (x[c] - x[n + c]) * wv;
            x[c] -= m / vl;
            x[n + c] += m / vu;
        }

        /// <summary>Stairwell cells exchange the upper layer of floor f with the lower layer of floor f + 1 at k_v.</summary>
        static void Floors(RoomGridLayout l, RoomGridState s, RoomGridParams p, float dt)
        {
            int n = l.Cells, perFloor = l.NX * l.NY;
            float vl = p.LowerHeight, vu = p.UpperHeight, wv = vl * vu / (vl + vu);
            float f = Math.Min(0.5f, p.FloorExchange * dt);
            for (int c = 0; c < n - perFloor; c++)
            {
                if (!l.Stair[c] || !l.Stair[c + perFloor]) continue;
                int up = n + c, low = c + perFloor;
                foreach (float[] x in new[] { s.T, s.Co2, s.O2 })
                {
                    float m = f * (x[up] - x[low]) * wv;
                    x[up] -= m / vu;
                    x[low] += m / vl;
                }
            }
        }
    }
}
