using System;
using System.Collections.Generic;

namespace HeadLoss.Sim.Rooms
{
    /// <summary>Room grid parameters (<c>05-simulation.md</c> §3.2), SI.</summary>
    public sealed class RoomGridParams
    {
        /// <summary>Cell size, m.</summary>
        public float CellSize = 0.5f;
        /// <summary>Lower layer height, m.</summary>
        public float LowerHeight = 1.5f;
        /// <summary>Upper layer height, m (lower + upper = room air height).</summary>
        public float UpperHeight = 1.2f;
        /// <summary>Eddy diffusivity, m²/s.</summary>
        public float Diffusivity = 0.02f;
        /// <summary>Vent jet decay length, m.</summary>
        public float JetLength = 2f;
        /// <summary>Buoyancy transfer k_b, 1/s.</summary>
        public float Stratification = 0.5f;
        /// <summary>Slow layer mixing, 1/s.</summary>
        public float LayerMixing = 0.02f;
        /// <summary>Floor-to-floor exchange through stairwells, 1/s.</summary>
        public float FloorExchange = 0.1f;
        /// <summary>Heat capacity multiple of the air (thermal mass).</summary>
        public float ThermalMass = 5f;
        public int SorIterations = 20;
        public float SorOmega = 1.6f;
        /// <summary>Air density × c_p used for heat, J/(m³·K).</summary>
        public float RhoCp = 1.204f * 1005f;
        /// <summary>Exterior wall U-value, W/(m²·K).</summary>
        public float WallU = 0.6f;
        /// <summary>Molar masses for buoyancy and density, kg/mol.</summary>
        public float AirMolarMass = 0.028965f, Co2MolarMass = 0.04401f, O2MolarMass = 0.031999f;
    }

    /// <summary>
    /// The static description of a building's room grid: 0.5 m cells per floor, which room each cell is in, which
    /// cell faces air may cross (inside a room, or at a door), exterior wall and window areas, stairwell links.
    /// </summary>
    public sealed class RoomGridLayout
    {
        public int NX, NY, Floors;
        public int Cells => NX * NY * Floors;
        /// <summary>Room index per cell (−1 = not a room).</summary>
        public int[] Room = Array.Empty<int>();
        /// <summary>Air may cross the cell's east (+x) / north (+y) face.</summary>
        public bool[] OpenE = Array.Empty<bool>(), OpenN = Array.Empty<bool>();
        /// <summary>The cell is beside a door (scalars may be sampled across rooms there).</summary>
        public bool[] Door = Array.Empty<bool>();
        /// <summary>Exterior wall area of the cell's outside faces (full room height), m².</summary>
        public float[] ExteriorArea = Array.Empty<float>();
        /// <summary>Vertical link to the floor above (stairwell or shaft).</summary>
        public bool[] Stair = Array.Empty<bool>();
        public int RoomCount;
        /// <summary>Room air volume, m³.</summary>
        public float[] RoomVolume = Array.Empty<float>();
        /// <summary>Doors between rooms (room a, room b), one entry per door.</summary>
        public readonly List<(int a, int b)> Doors = new List<(int, int)>();

        public int Index(int floor, int x, int y) => (floor * NY + y) * NX + x;

        // Room balance scratch, built on first use (the layout does not change once built).
        internal int[] DoorStart = Array.Empty<int>(), DoorOther = Array.Empty<int>();
        internal float[] Facade = Array.Empty<float>(), Supply = Array.Empty<float>(), Pressure = Array.Empty<float>(), Outflow = Array.Empty<float>();

        internal void PrepareBalance()
        {
            if (DoorStart.Length == RoomCount + 1) return;
            var count = new int[RoomCount + 1];
            foreach ((int a, int b) in Doors) { count[a + 1]++; count[b + 1]++; }
            for (int r = 0; r < RoomCount; r++) count[r + 1] += count[r];
            DoorOther = new int[count[RoomCount]];
            var fill = (int[])count.Clone();
            foreach ((int a, int b) in Doors)
            {
                DoorOther[fill[a]++] = b;
                DoorOther[fill[b]++] = a;
            }
            DoorStart = count;
            Facade = new float[RoomCount];
            Supply = new float[RoomCount];
            Pressure = new float[RoomCount];
            Outflow = new float[RoomCount];
            for (int c = 0; c < Cells; c++) if (Room[c] >= 0) Facade[Room[c]] += ExteriorArea[c];
        }
    }

    /// <summary>A supply jet in front of a vent (<c>05-simulation.md</c> §3.2 step 3).</summary>
    public struct VentJet
    {
        public int Cell;
        /// <summary>Unit throw direction (one of ±x, ±y).</summary>
        public int DirX, DirY;
        /// <summary>Air speed at the grille face, m/s.</summary>
        public float FaceVelocity;
    }

    /// <summary>What drives one step: sources, heat and the outside, all per cell (arrays sized to the layout).</summary>
    public sealed class RoomGridInputs
    {
        /// <summary>Air injected into the upper layer (vents, duct leaks), m³/s.</summary>
        public float[] SupplyFlow = Array.Empty<float>();
        /// <summary>Temperature of that air, °C (flow-weighted when several sources share a cell).</summary>
        public float[] SupplyTemperature = Array.Empty<float>();
        /// <summary>Heat into the lower / upper layer (occupants, machines, sun, ducts), W.</summary>
        public float[] HeatLower = Array.Empty<float>(), HeatUpper = Array.Empty<float>();
        /// <summary>CO₂ breathed into the lower layer, m³/s.</summary>
        public float[] Co2Lower = Array.Empty<float>();
        public readonly List<VentJet> Jets = new List<VentJet>();
        public float OutsideTemperature = 15f;
        public float OutdoorCo2Ppm = 420f;
        public float OutdoorO2 = 0.209f;
        /// <summary>Door and facade conductances for the room balance, m³/s per Pa (per door / per m² of wall).</summary>
        public float DoorConductance = 0.05f, FacadeConductancePerM2 = 0.0004f;

        public static RoomGridInputs For(RoomGridLayout l)
        {
            int n = l.Cells;
            return new RoomGridInputs
            {
                SupplyFlow = new float[n], SupplyTemperature = new float[n], HeatLower = new float[n], HeatUpper = new float[n], Co2Lower = new float[n],
            };
        }

        public void Clear()
        {
            Array.Clear(SupplyFlow, 0, SupplyFlow.Length);
            Array.Clear(SupplyTemperature, 0, SupplyTemperature.Length);
            Array.Clear(HeatLower, 0, HeatLower.Length);
            Array.Clear(HeatUpper, 0, HeatUpper.Length);
            Array.Clear(Co2Lower, 0, Co2Lower.Length);
            Jets.Clear();
        }
    }

    /// <summary>The fields (<c>05-simulation.md</c> §3.1). Scalars are stored layer-major: [0, n) lower, [n, 2n) upper.</summary>
    public sealed class RoomGridState
    {
        /// <summary>Temperature, °C.</summary>
        public float[] T = Array.Empty<float>();
        /// <summary>CO₂, ppm.</summary>
        public float[] Co2 = Array.Empty<float>();
        /// <summary>O₂ volume fraction.</summary>
        public float[] O2 = Array.Empty<float>();
        /// <summary>Velocity potential (warm start), m²/s.</summary>
        public float[] Phi = Array.Empty<float>();
        /// <summary>Cell velocity, m/s (2D, shared by both layers).</summary>
        public float[] Vx = Array.Empty<float>(), Vy = Array.Empty<float>();
        /// <summary>Net volumetric source per cell this step (supply − outflow), m³/s.</summary>
        public float[] NetSource = Array.Empty<float>();

        public static RoomGridState Initial(RoomGridLayout l, float temperatureC, float co2Ppm, float o2)
        {
            int n = l.Cells;
            var s = new RoomGridState
            {
                T = new float[2 * n], Co2 = new float[2 * n], O2 = new float[2 * n], Phi = new float[n], Vx = new float[n], Vy = new float[n], NetSource = new float[n],
            };
            for (int i = 0; i < 2 * n; i++)
            {
                s.T[i] = temperatureC;
                s.Co2[i] = co2Ppm;
                s.O2[i] = o2;
            }
            return s;
        }

        public RoomGridState Clone() => new RoomGridState
        {
            T = (float[])T.Clone(), Co2 = (float[])Co2.Clone(), O2 = (float[])O2.Clone(), Phi = (float[])Phi.Clone(),
            Vx = (float[])Vx.Clone(), Vy = (float[])Vy.Clone(), NetSource = (float[])NetSource.Clone(),
        };
    }

    /// <summary>Steps the room grid by <c>dt</c> seconds (CPU reference here; a Burst version in Unity.Jobs).</summary>
    public interface IRoomGridStepper
    {
        void Step(RoomGridLayout layout, RoomGridState state, RoomGridInputs inputs, RoomGridParams p, float dt);
    }

    /// <summary>Room-level values used by rules and readouts (<c>05-simulation.md</c> §3.2).</summary>
    public struct RoomSummary
    {
        /// <summary>Mean temperature, °C.</summary>
        public float MeanTemperature;
        /// <summary>Mean CO₂, ppm.</summary>
        public float Co2Ppm;
        /// <summary>Mean O₂ fraction.</summary>
        public float O2;
        /// <summary>Supply air changes per hour.</summary>
        public float AirChangesPerHour;
    }

    /// <summary>Shared preparation and read-outs for every stepper.</summary>
    public static class RoomGridMath
    {
        /// <summary>
        /// Step 2, room balance: each room's net supply leaves through its doors and its exterior-wall leakage, split
        /// by conductance (rooms near ambient pressure: a linear network solved by Gauss-Seidel). The outflow to the
        /// outside is spread over the room's exterior cells in proportion to their wall area, so every cell's net source
        /// sums to zero over a connected floor and the potential solve has a solution.
        /// </summary>
        public static void NetSources(RoomGridLayout l, RoomGridInputs inp, float[] net)
        {
            l.PrepareBalance();
            int n = l.Cells, rooms = l.RoomCount;
            float[] supply = l.Supply, facade = l.Facade, p = l.Pressure, gOut = l.Outflow;
            Array.Clear(supply, 0, rooms);
            for (int c = 0; c < n; c++)
                if (l.Room[c] >= 0) supply[l.Room[c]] += inp.SupplyFlow[c];
            for (int r = 0; r < rooms; r++) gOut[r] = Math.Max(1e-6f, facade[r] * inp.FacadeConductancePerM2);
            float gd = inp.DoorConductance;
            for (int it = 0; it < 200; it++)
            {
                float change = 0;
                for (int r = 0; r < rooms; r++)
                {
                    float g = gOut[r], rhs = supply[r];
                    for (int k = l.DoorStart[r]; k < l.DoorStart[r + 1]; k++)
                    {
                        g += gd;
                        rhs += gd * p[l.DoorOther[k]];
                    }
                    float v = rhs / g;
                    change = Math.Max(change, Math.Abs(v - p[r]));
                    p[r] = v;
                }
                if (change < 1e-9f) break;
            }
            for (int c = 0; c < n; c++)
            {
                int r = l.Room[c];
                net[c] = r < 0 ? 0 : inp.SupplyFlow[c] - (facade[r] > 0 ? gOut[r] * p[r] * l.ExteriorArea[c] / facade[r] : 0);
            }
        }

        /// <summary>Per-room summaries from the state (ACH from the supply in the inputs).</summary>
        public static RoomSummary[] Summaries(RoomGridLayout l, RoomGridState s, RoomGridInputs inp, RoomGridParams p)
        {
            int n = l.Cells;
            var sums = new double[l.RoomCount, 4];
            var count = new int[l.RoomCount];
            for (int c = 0; c < n; c++)
            {
                int r = l.Room[c];
                if (r < 0) continue;
                float wl = p.LowerHeight, wu = p.UpperHeight, w = wl + wu;
                sums[r, 0] += (s.T[c] * wl + s.T[n + c] * wu) / w;
                sums[r, 1] += (s.Co2[c] * wl + s.Co2[n + c] * wu) / w;
                sums[r, 2] += (s.O2[c] * wl + s.O2[n + c] * wu) / w;
                sums[r, 3] += inp.SupplyFlow[c];
                count[r]++;
            }
            var res = new RoomSummary[l.RoomCount];
            for (int r = 0; r < l.RoomCount; r++)
            {
                if (count[r] == 0) continue;
                res[r] = new RoomSummary
                {
                    MeanTemperature = (float)(sums[r, 0] / count[r]),
                    Co2Ppm = (float)(sums[r, 1] / count[r]),
                    O2 = (float)(sums[r, 2] / count[r]),
                    AirChangesPerHour = l.RoomVolume[r] > 0 ? (float)(3600 * sums[r, 3] / l.RoomVolume[r]) : 0,
                };
            }
            return res;
        }

        /// <summary>Air density of a layer cell from temperature and CO₂ (ideal gas at 1 atm), kg/m³.</summary>
        public static float Density(RoomGridParams p, float temperatureC, float co2Ppm)
        {
            float x = co2Ppm * 1e-6f;
            float m = p.AirMolarMass * (1 - x) + p.Co2MolarMass * x;
            return 101325f * m / (8.314462618f * (temperatureC + 273.15f));
        }
    }
}
