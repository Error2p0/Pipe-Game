using System;
using HeadLoss.Sim.Rooms;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace HeadLoss.Unity.Jobs
{
    /// <summary>
    /// Burst implementation of the room grid step (<c>01-architecture.md</c> §Threading): the same algorithm as
    /// <see cref="RoomGridReference"/>, on native arrays in one Burst-compiled job. The room balance (a few rooms) runs
    /// in managed code through the shared <see cref="RoomGridMath.NetSources"/>. A PlayMode test compares the two over
    /// 200 steps.
    /// </summary>
    public sealed class BurstRoomGridStepper : IRoomGridStepper, IDisposable
    {
        RoomGridLayout? _layout;
        RoomGridState? _state;
        RoomGridInputs? _inputs;
        int _inputsRevision = -1;
        NativeArray<int> _room;
        NativeArray<byte> _openE, _openN, _door, _stair;
        NativeArray<float> _ext, _t, _co2, _o2, _phi, _vx, _vy, _net, _supply, _supplyT, _heatL, _heatU, _co2L, _a, _b;
        NativeArray<VentJet> _jets;

        void Allocate(RoomGridLayout l)
        {
            Dispose();
            _layout = l;
            int n = l.Cells;
            _room = new NativeArray<int>(l.Room, Allocator.Persistent);
            _openE = Bytes(l.OpenE);
            _openN = Bytes(l.OpenN);
            _door = Bytes(l.Door);
            _stair = Bytes(l.Stair);
            _ext = new NativeArray<float>(l.ExteriorArea, Allocator.Persistent);
            _t = new NativeArray<float>(2 * n, Allocator.Persistent);
            _co2 = new NativeArray<float>(2 * n, Allocator.Persistent);
            _o2 = new NativeArray<float>(2 * n, Allocator.Persistent);
            _a = new NativeArray<float>(2 * n, Allocator.Persistent);
            _b = new NativeArray<float>(2 * n, Allocator.Persistent);
            _phi = new NativeArray<float>(n, Allocator.Persistent);
            _vx = new NativeArray<float>(n, Allocator.Persistent);
            _vy = new NativeArray<float>(n, Allocator.Persistent);
            _net = new NativeArray<float>(n, Allocator.Persistent);
            _supply = new NativeArray<float>(n, Allocator.Persistent);
            _supplyT = new NativeArray<float>(n, Allocator.Persistent);
            _heatL = new NativeArray<float>(n, Allocator.Persistent);
            _heatU = new NativeArray<float>(n, Allocator.Persistent);
            _co2L = new NativeArray<float>(n, Allocator.Persistent);
            _jets = new NativeArray<VentJet>(64, Allocator.Persistent);
        }

        static NativeArray<byte> Bytes(bool[] src)
        {
            var a = new NativeArray<byte>(src.Length, Allocator.Persistent);
            for (int i = 0; i < src.Length; i++) a[i] = src[i] ? (byte)1 : (byte)0;
            return a;
        }

        public void Step(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p, float dt)
        {
            if (_layout != l) Allocate(l);
            bool fresh = _inputs != inp || _inputsRevision != inp.Revision || _layout != l;
            _t.CopyFrom(s.T);
            _co2.CopyFrom(s.Co2);
            _o2.CopyFrom(s.O2);
            // The potential only warm-starts the next solve: it stays native while the same state is stepped.
            if (_state != s) _phi.CopyFrom(s.Phi);
            _state = s;
            if (fresh)
            {
                // Sub-steps with the same inputs reuse the room balance and the native copies.
                RoomGridMath.NetSources(l, inp, s.NetSource);
                _net.CopyFrom(s.NetSource);
                _supply.CopyFrom(inp.SupplyFlow);
                _supplyT.CopyFrom(inp.SupplyTemperature);
                _heatL.CopyFrom(inp.HeatLower);
                _heatU.CopyFrom(inp.HeatUpper);
                _co2L.CopyFrom(inp.Co2Lower);
                _inputs = inp;
                _inputsRevision = inp.Revision;
            }
            if (_jets.Length < inp.Jets.Count)
            {
                _jets.Dispose();
                _jets = new NativeArray<VentJet>(inp.Jets.Count * 2, Allocator.Persistent);
            }
            for (int i = 0; i < inp.Jets.Count; i++) _jets[i] = inp.Jets[i];
            var step = new StepJob
            {
                NX = l.NX, NY = l.NY, Floors = l.Floors, Dt = dt,
                Cell = p.CellSize, Lower = p.LowerHeight, Upper = p.UpperHeight, Diff = p.Diffusivity, Jet = p.JetLength,
                Strat = p.Stratification, Mix = p.LayerMixing, FloorK = p.FloorExchange, Mass = p.ThermalMass,
                Sor = p.SorIterations, Omega = p.SorOmega, RhoCp = p.RhoCp, WallU = p.WallU,
                MAir = p.AirMolarMass, MCo2 = p.Co2MolarMass,
                OutsideT = inp.OutsideTemperature, OutCo2 = inp.OutdoorCo2Ppm, OutO2 = inp.OutdoorO2,
                Room = _room, OpenE = _openE, OpenN = _openN, Door = _door, Stair = _stair, Ext = _ext,
                T = _t, Co2 = _co2, O2 = _o2, Phi = _phi, Vx = _vx, Vy = _vy, Net = _net,
                Supply = _supply, SupplyT = _supplyT, HeatL = _heatL, HeatU = _heatU, Co2L = _co2L,
                Jets = _jets, JetCount = inp.Jets.Count, A = _a, B = _b,
            };
            // Floors are independent within a step; the stairwell exchange between them runs after.
            JobHandle h = step.Schedule(l.Floors, 1);
            h = new FloorJob
            {
                NX = l.NX, NY = l.NY, Floors = l.Floors, Dt = dt, Lower = p.LowerHeight, Upper = p.UpperHeight, FloorK = p.FloorExchange,
                Stair = _stair, T = _t, Co2 = _co2, O2 = _o2,
            }.Schedule(h);
            h.Complete();
            _t.CopyTo(s.T);
            _co2.CopyTo(s.Co2);
            _o2.CopyTo(s.O2);
            _vx.CopyTo(s.Vx);
            _vy.CopyTo(s.Vy);
        }

        /// <summary>Copies the native potential back into the state (before another stepper takes it over).</summary>
        public void Release(RoomGridState s)
        {
            if (_state == s && _phi.IsCreated) _phi.CopyTo(s.Phi);
            if (_state == s) _state = null;
        }

        public void Dispose()
        {
            _state = null;
            _inputs = null;
            if (!_room.IsCreated) return;
            _room.Dispose(); _openE.Dispose(); _openN.Dispose(); _door.Dispose(); _stair.Dispose(); _ext.Dispose();
            _t.Dispose(); _co2.Dispose(); _o2.Dispose(); _a.Dispose(); _b.Dispose(); _phi.Dispose(); _vx.Dispose(); _vy.Dispose();
            _net.Dispose(); _supply.Dispose(); _supplyT.Dispose(); _heatL.Dispose(); _heatU.Dispose(); _co2L.Dispose(); _jets.Dispose();
            _layout = null;
        }

        /// <summary>One floor's step (index = floor): steps 1–7 of the reference, in the same order.</summary>
        [BurstCompile(FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct StepJob : IJobParallelFor
        {
            public int NX, NY, Floors, Sor, JetCount;
            public float Dt, Cell, Lower, Upper, Diff, Jet, Strat, Mix, FloorK, Mass, Omega, RhoCp, WallU, MAir, MCo2, OutsideT, OutCo2, OutO2;
            [ReadOnly] public NativeArray<int> Room;
            [ReadOnly] public NativeArray<byte> OpenE, OpenN, Door, Stair;
            [ReadOnly] public NativeArray<float> Ext, Net, Supply, SupplyT, HeatL, HeatU, Co2L;
            [ReadOnly] public NativeArray<VentJet> Jets;
            [NativeDisableParallelForRestriction] public NativeArray<float> T, Co2, O2, Phi, Vx, Vy, A, B;
            int _floor, _c0, _c1;

            int N => NX * NY * Floors;
            int Index(int f, int x, int y) => (f * NY + y) * NX + x;

            public void Execute(int floor)
            {
                _floor = floor;
                _c0 = floor * NX * NY;
                _c1 = _c0 + NX * NY;
                Sources();
                Potential();
                Velocity();
                Advect(T);
                Advect(Co2);
                Advect(O2);
                float limit = Cell * Cell / (4 * Diff);
                int sub = math.max(1, (int)math.ceil(Dt / limit));
                float coef = Diff * (Dt / sub) / (Cell * Cell);
                for (int i = 0; i < sub; i++)
                {
                    DiffuseOnce(T, coef);
                    DiffuseOnce(Co2, coef);
                    DiffuseOnce(O2, coef);
                }
                Heat();
                Stratify();
            }

            void Sources()
            {
                int n = N;
                float area = Cell * Cell, vu = area * Upper, vl = area * Lower;
                for (int c = _c0; c < _c1; c++)
                {
                    if (Room[c] < 0) continue;
                    float q = Supply[c];
                    if (q > 0)
                    {
                        float f = math.min(1f, q * Dt / vu), ft = math.min(1f, q * Dt / (vu * Mass));
                        Co2[n + c] += f * (OutCo2 - Co2[n + c]);
                        O2[n + c] += f * (OutO2 - O2[n + c]);
                        T[n + c] += ft * (SupplyT[c] - T[n + c]);
                    }
                    float co2 = Co2L[c];
                    if (co2 > 0) Co2[c] += co2 * Dt / vl * 1e6f;
                }
            }

            void Potential()
            {
                float h = Lower + Upper;
                for (int it = 0; it < Sor; it++)
                    for (int f = _floor; f <= _floor; f++)
                        for (int y = 0; y < NY; y++)
                            for (int x = 0; x < NX; x++)
                            {
                                int c = Index(f, x, y);
                                if (Room[c] < 0) continue;
                                float sum = 0;
                                int k = 0;
                                if (x + 1 < NX && OpenE[c] != 0) { sum += Phi[c + 1]; k++; }
                                if (x > 0 && OpenE[c - 1] != 0) { sum += Phi[c - 1]; k++; }
                                if (y + 1 < NY && OpenN[c] != 0) { sum += Phi[c + NX]; k++; }
                                if (y > 0 && OpenN[c - NX] != 0) { sum += Phi[c - NX]; k++; }
                                if (k == 0) continue;
                                float target = (sum + Net[c] / h) / k;
                                Phi[c] += Omega * (target - Phi[c]);
                            }
            }

            bool CanStep(int c, int dx, int dy)
            {
                int x = c % NX, y = c / NX % NY;
                if (dx > 0) return x + 1 < NX && OpenE[c] != 0;
                if (dx < 0) return x > 0 && OpenE[c - 1] != 0;
                if (dy > 0) return y + 1 < NY && OpenN[c] != 0;
                return y > 0 && OpenN[c - NX] != 0;
            }

            void Velocity()
            {
                for (int f = _floor; f <= _floor; f++)
                    for (int y = 0; y < NY; y++)
                        for (int x = 0; x < NX; x++)
                        {
                            int c = Index(f, x, y);
                            if (Room[c] < 0) { Vx[c] = 0; Vy[c] = 0; continue; }
                            float ue = x + 1 < NX && OpenE[c] != 0 ? (Phi[c] - Phi[c + 1]) / Cell : 0;
                            float uw = x > 0 && OpenE[c - 1] != 0 ? (Phi[c - 1] - Phi[c]) / Cell : 0;
                            float un = y + 1 < NY && OpenN[c] != 0 ? (Phi[c] - Phi[c + NX]) / Cell : 0;
                            float us = y > 0 && OpenN[c - NX] != 0 ? (Phi[c - NX] - Phi[c]) / Cell : 0;
                            Vx[c] = 0.5f * (ue + uw);
                            Vy[c] = 0.5f * (un + us);
                        }
                for (int i = 0; i < JetCount; i++)
                {
                    VentJet j = Jets[i];
                    int c = j.Cell;
                    if (c < _c0 || c >= _c1) continue;
                    int steps = (int)math.ceil(3 * Jet / Cell);
                    for (int k = 0; k < steps; k++)
                    {
                        float v = j.FaceVelocity * (float)math.exp(-(double)(k * Cell) / Jet);
                        Vx[c] += v * j.DirX;
                        Vy[c] += v * j.DirY;
                        if (j.DirX != 0)
                        {
                            if (CanStep(c, 0, 1)) Vx[c + NX] += 0.5f * v * j.DirX;
                            if (CanStep(c, 0, -1)) Vx[c - NX] += 0.5f * v * j.DirX;
                        }
                        else
                        {
                            if (CanStep(c, 1, 0)) Vy[c + 1] += 0.5f * v * j.DirY;
                            if (CanStep(c, -1, 0)) Vy[c - 1] += 0.5f * v * j.DirY;
                        }
                        if (!CanStep(c, j.DirX, j.DirY)) break;
                        c += j.DirX + j.DirY * NX;
                    }
                }
            }

            void Advect(NativeArray<float> field)
            {
                int n = N;
                NativeArray<float>.Copy(field, _c0, A, _c0, _c1 - _c0);
                NativeArray<float>.Copy(field, n + _c0, A, n + _c0, _c1 - _c0);
                float scale = Dt / Cell;
                for (int f = _floor; f <= _floor; f++)
                    for (int y = 0; y < NY; y++)
                        for (int x = 0; x < NX; x++)
                        {
                            int c = Index(f, x, y);
                            int room = Room[c];
                            if (room < 0) continue;
                            float gx = x - Vx[c] * scale, gy = y - Vy[c] * scale;
                            int x0 = (int)math.floor(gx), y0 = (int)math.floor(gy);
                            float fx = gx - x0, fy = gy - y0;
                            for (int layer = 0; layer < 2; layer++)
                            {
                                int o = layer * n;
                                float sum = 0, wsum = 0;
                                for (int k = 0; k < 4; k++)
                                {
                                    int sx = x0 + (k & 1), sy = y0 + (k >> 1);
                                    if (sx < 0 || sy < 0 || sx >= NX || sy >= NY) continue;
                                    int sc = Index(f, sx, sy);
                                    int sr = Room[sc];
                                    if (sr < 0 || (sr != room && Door[c] == 0 && Door[sc] == 0)) continue;
                                    float w = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
                                    sum += w * A[o + sc];
                                    wsum += w;
                                }
                                field[o + c] = wsum > 1e-6f ? sum / wsum : A[o + c];
                            }
                        }
            }

            void DiffuseOnce(NativeArray<float> field, float coef)
            {
                int n = N;
                NativeArray<float>.Copy(field, _c0, B, _c0, _c1 - _c0);
                NativeArray<float>.Copy(field, n + _c0, B, n + _c0, _c1 - _c0);
                for (int y = 0; y < NY; y++)
                for (int x = 0; x < NX; x++)
                {
                    int c = _c0 + y * NX + x;
                    if (Room[c] < 0) continue;
                    for (int layer = 0; layer < 2; layer++)
                    {
                        int o = layer * n;
                        float v = B[o + c], d = 0;
                        if (x + 1 < NX && OpenE[c] != 0) d += B[o + c + 1] - v;
                        if (x > 0 && OpenE[c - 1] != 0) d += B[o + c - 1] - v;
                        if (y + 1 < NY && OpenN[c] != 0) d += B[o + c + NX] - v;
                        if (y > 0 && OpenN[c - NX] != 0) d += B[o + c - NX] - v;
                        field[o + c] = v + coef * d;
                    }
                }
            }

            void Heat()
            {
                int n = N;
                float area = Cell * Cell, height = Lower + Upper;
                float capL = RhoCp * area * Lower * Mass, capU = RhoCp * area * Upper * Mass;
                for (int c = _c0; c < _c1; c++)
                {
                    if (Room[c] < 0) continue;
                    float aL = Ext[c] * Lower / height, aU = Ext[c] * Upper / height;
                    float qL = HeatL[c] - WallU * aL * (T[c] - OutsideT);
                    float qU = HeatU[c] - WallU * aU * (T[n + c] - OutsideT);
                    T[c] += qL * Dt / capL;
                    T[n + c] += qU * Dt / capU;
                }
            }

            float Density(float tc, float co2Ppm)
            {
                float x = co2Ppm * 1e-6f;
                float m = MAir * (1 - x) + MCo2 * x;
                return 101325f * m / (8.314462618f * (tc + 273.15f));
            }

            void Stratify()
            {
                int n = N;
                float vl = Lower, vu = Upper, wv = vl * vu / (vl + vu);
                float settle = Strat * math.abs(MCo2 - MAir) / MAir;
                for (int c = _c0; c < _c1; c++)
                {
                    if (Room[c] < 0) continue;
                    float rl = Density(T[c], Co2[c]), ru = Density(T[n + c], Co2[n + c]);
                    float rate = Mix + (rl < ru ? Strat * (ru - rl) / ((rl + ru) * 0.5f) : 0);
                    float f = math.min(0.5f, rate * Dt);
                    Exchange(T, c, n, f, vl, vu, wv);
                    Exchange(Co2, c, n, f, vl, vu, wv);
                    Exchange(O2, c, n, f, vl, vu, wv);
                    float excess = Co2[n + c] - OutCo2;
                    if (excess > 0)
                    {
                        float m = math.min(0.5f, settle * Dt) * excess * vu;
                        Co2[n + c] -= m / vu;
                        Co2[c] += m / vl;
                    }
                }
            }

            static void Exchange(NativeArray<float> x, int c, int n, float f, float vl, float vu, float wv)
            {
                float m = f * (x[c] - x[n + c]) * wv;
                x[c] -= m / vl;
                x[n + c] += m / vu;
            }
        }

        /// <summary>Step 8: stairwell cells exchange the upper layer of a floor with the lower layer of the next.</summary>
        [BurstCompile(FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
        struct FloorJob : IJob
        {
            public int NX, NY, Floors;
            public float Dt, Lower, Upper, FloorK;
            [ReadOnly] public NativeArray<byte> Stair;
            public NativeArray<float> T, Co2, O2;

            public void Execute()
            {
                int n = NX * NY * Floors, perFloor = NX * NY;
                float vl = Lower, vu = Upper, wv = vl * vu / (vl + vu);
                float f = math.min(0.5f, FloorK * Dt);
                for (int c = 0; c < n - perFloor; c++)
                {
                    if (Stair[c] == 0 || Stair[c + perFloor] == 0) continue;
                    int up = n + c, low = c + perFloor;
                    Swap(T, up, low, f, vl, vu, wv);
                    Swap(Co2, up, low, f, vl, vu, wv);
                    Swap(O2, up, low, f, vl, vu, wv);
                }
            }

            static void Swap(NativeArray<float> x, int up, int low, float f, float vl, float vu, float wv)
            {
                float m = f * (x[up] - x[low]) * wv;
                x[up] -= m / vu;
                x[low] += m / vl;
            }
        }
    }
}
