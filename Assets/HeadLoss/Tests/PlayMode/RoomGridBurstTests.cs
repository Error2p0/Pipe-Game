using System;
using HeadLoss.Game.City;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Thermal;
using HeadLoss.Sim.Rooms;
using HeadLoss.Unity.Jobs;
using NUnit.Framework;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>05-simulation.md §3: the Burst room grid matches the CPU reference over 200 steps on a sample building.</summary>
    public class RoomGridBurstTests
    {
        [Test]
        public void Burst_MatchesTheReference_Over200Steps()
        {
            var data = HeadLoss.Core.Data.DataRepository.Load(new HeadLoss.Core.Data.FileDataSource("Assets/HeadLoss/Data"));
            GeneratedBuilding g = BuildingFactory.Create(data, new BuildingRequest("plot-1", "family-house", 11, "south"), DistrictKind.Residential)!;
            RoomGridLayout l = RoomGridBuilder.Build(data, g.Layout);
            RoomGridParams p = RoomGridBuilder.Params(data);
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.OutsideTemperature = 8;
            var rng = new HeadLoss.Core.Rng(5);
            int vents = 0;
            for (int c = 0; c < l.Cells && vents < 6; c += 37)
            {
                if (l.Room[c] < 0) continue;
                inp.SupplyFlow[c] = 0.004f;
                inp.SupplyTemperature[c] = 12;
                inp.Jets.Add(new VentJet { Cell = c, DirX = vents % 2 == 0 ? 1 : 0, DirY = vents % 2 == 0 ? 0 : -1, FaceVelocity = 0.4f });
                vents++;
            }
            for (int c = 0; c < l.Cells; c++)
            {
                if (l.Room[c] < 0) continue;
                inp.HeatLower[c] = (float)rng.NextDouble() * 5;
                inp.Co2Lower[c] = (float)rng.NextDouble() * 2e-7f;
            }
            RoomGridState a = RoomGridState.Initial(l, 20, 420, 0.209f), b = a.Clone();
            var reference = new RoomGridReference();
            using var burst = new BurstRoomGridStepper();
            for (int i = 0; i < 200; i++)
            {
                reference.Step(l, a, inp, p, 0.1f);
                burst.Step(l, b, inp, p, 0.1f);
            }
            AssertClose(a.T, b.T, "temperature");
            AssertClose(a.Co2, b.Co2, "CO₂");
            AssertClose(a.O2, b.O2, "O₂");
            AssertClose(a.Vx, b.Vx, "velocity x");
            AssertClose(a.Vy, b.Vy, "velocity y");
        }

        /// <summary>01-architecture.md budget: room grid step for a whole building of 10 floors under 2 ms with Burst.</summary>
        [Test]
        public void Burst_TenFloorBuilding_StepsUnderTwoMilliseconds()
        {
            // The largest tier-5 footprint (40 × 28 m), 10 floors, rooms of 4 × 4 m with a door each way.
            var l = new RoomGridLayout { NX = 80, NY = 56, Floors = 10, RoomCount = 70 * 10 };
            int n = l.Cells;
            l.Room = new int[n]; l.OpenE = new bool[n]; l.OpenN = new bool[n]; l.Door = new bool[n]; l.ExteriorArea = new float[n]; l.Stair = new bool[n];
            l.RoomVolume = new float[l.RoomCount];
            for (int f = 0; f < 10; f++)
                for (int y = 0; y < 56; y++)
                    for (int x = 0; x < 80; x++)
                    {
                        int c = l.Index(f, x, y);
                        l.Room[c] = f * 70 + (y / 8) * 10 + x / 8;
                        l.OpenE[c] = x < 79 && (x % 8 != 7 || y % 8 == 3);
                        l.OpenN[c] = y < 55 && (y % 8 != 7 || x % 8 == 3);
                        l.Stair[c] = x < 2 && y < 2;
                    }
            for (int r = 0; r < l.RoomCount; r++) l.RoomVolume[r] = 64 * 0.25f * 2.7f;
            RoomGridParams p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            for (int c = 0; c < n; c += 97) { inp.SupplyFlow[c] = 0.003f; inp.SupplyTemperature[c] = 15; inp.Jets.Add(new VentJet { Cell = c, DirX = 1, FaceVelocity = 0.3f }); }
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            using var burst = new BurstRoomGridStepper();
            for (int i = 0; i < 5; i++) burst.Step(l, s, inp, p, 0.1f);
            var times = new System.Collections.Generic.List<double>();
            for (int i = 0; i < 21; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                burst.Step(l, s, inp, p, 0.1f);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            UnityEngine.Debug.Log($"Room grid (Burst), {n} cells: median {times[10]:0.00} ms");
            Assert.Less(times[10], 2.0, $"median step {times[10]:0.00} ms for {n} cells");
        }

        static void AssertClose(float[] a, float[] b, string what)
        {
            float scale = 1e-6f;
            foreach (float v in a) scale = Math.Max(scale, Math.Abs(v));
            float worst = 0;
            for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
            Assert.Less(worst / scale, 1e-3f, $"{what}: max difference {worst} of scale {scale}");
        }
    }
}
