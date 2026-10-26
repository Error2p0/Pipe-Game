using System.IO;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using NUnit.Framework;

namespace HeadLoss.Core.Tests
{
    public class RngTests
    {
        [Test]
        public void Rng_SameSeed_SameSequence()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void Rng_DifferentSeed_DifferentSequence()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.NextUInt() == b.NextUInt()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void Rng_KnownPcg32Output_MatchesReference()
        {
            // pcg32 reference (pcg32-demo): seed 42, stream 54 → first outputs.
            var r = new Rng(42, 54);
            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (uint e in expected) Assert.AreEqual(e, r.NextUInt());
        }

        [Test]
        public void Rng_StateRoundTrip_ContinuesSequence()
        {
            var a = new Rng(99);
            for (int i = 0; i < 10; i++) a.NextUInt();
            var b = Rng.FromState(a.State, a.Increment);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void Rng_NextDouble_InUnitInterval()
        {
            var r = new Rng(7);
            double sum = 0;
            for (int i = 0; i < 20000; i++)
            {
                double d = r.NextDouble();
                Assert.GreaterOrEqual(d, 0.0);
                Assert.Less(d, 1.0);
                sum += d;
            }
            Assert.AreEqual(0.5, sum / 20000, 0.01);
        }

        [Test]
        public void Rng_NextInt_InRange()
        {
            var r = new Rng(3);
            var seen = new bool[6];
            for (int i = 0; i < 1000; i++)
            {
                int v = r.NextInt(-2, 4);
                Assert.That(v, Is.InRange(-2, 3));
                seen[v + 2] = true;
            }
            foreach (bool s in seen) Assert.IsTrue(s);
        }
    }

    public class UnitsTests
    {
        [Test]
        public void Units_FlowConversions_RoundTrip()
        {
            Assert.AreEqual(360.0, Units.M3sToM3h(0.1), 1e-12);
            Assert.AreEqual(0.1, Units.M3hToM3s(360.0), 1e-12);
            Assert.AreEqual(2.0, Units.M3sToLps(0.002), 1e-12);
        }

        [Test]
        public void Units_PressureAndTemperature_Convert()
        {
            Assert.AreEqual(0.2469, Units.PaToBar(24690), 1e-12);
            Assert.AreEqual(25.0, Units.PaToMbar(2500), 1e-12);
            Assert.AreEqual(293.15, Units.CToK(20), 1e-12);
            Assert.AreEqual(101425.0, Units.GaugeToAbs(100, 101325), 1e-9);
        }

        [Test]
        public void Units_Format_UsesInvariantCulture()
        {
            Assert.AreEqual("132 m³/h", Units.FormatAirFlow(Units.M3hToM3s(132.26)));
            Assert.AreEqual("3.54 m/s", Units.FormatVelocity(3.5368));
        }
    }

    public class IdsTests
    {
        [TestCase("small-axial-fan", true)]
        [TestCase("copper", true)]
        [TestCase("t1", true)]
        [TestCase("Small", false)]
        [TestCase("a--b", false)]
        [TestCase("-a", false)]
        [TestCase("a-", false)]
        [TestCase("a_b", false)]
        [TestCase("", false)]
        public void Ids_IsValid_KebabOnly(string id, bool valid) => Assert.AreEqual(valid, Ids.IsValid(id));
    }

    public class HexColorTests
    {
        [Test]
        public void HexColor_ParsesRgbAndRgba()
        {
            Assert.IsTrue(HexColor.TryParse("#FF8000", out double r, out double g, out double b, out double a));
            Assert.AreEqual(1.0, r, 1e-9);
            Assert.AreEqual(128 / 255.0, g, 1e-9);
            Assert.AreEqual(0.0, b, 1e-9);
            Assert.AreEqual(1.0, a, 1e-9);
            Assert.IsTrue(HexColor.TryParse("#00000080", out _, out _, out _, out a));
            Assert.AreEqual(128 / 255.0, a, 1e-9);
            Assert.IsFalse(HexColor.TryParse("red", out _, out _, out _, out _));
        }
    }

    public class DataTests
    {
        public static string DataRoot => Path.Combine(Directory.GetCurrentDirectory(), "Assets", "HeadLoss", "Data");

        [Test]
        public void DataValidator_AllDataFiles_NoErrors()
        {
            DataRepository data = DataRepository.Load(new FileDataSource(DataRoot));
            var errors = DataValidator.Validate(data);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void DataRepository_LoadsPhase1Content()
        {
            DataRepository data = DataRepository.Load(new FileDataSource(DataRoot));
            Assert.AreEqual("Head Loss", data.Game.DisplayName);
            Assert.IsNotNull(data.FindEquipment("small-axial-fan"));
            Assert.IsNotNull(data.FindFitting("elbow-90-long-radius"));
            Assert.IsNotNull(data.FindHandmadeBuilding("test-house"));
            Assert.IsTrue(data.FluidTables.ContainsKey("air"));
        }

        [Test]
        public void DataRepository_UnknownField_IsLoadError()
        {
            var src = new MemoryDataSource();
            foreach (string f in new FileDataSource(DataRoot).ListFiles())
                src.Add(f, File.ReadAllText(Path.Combine(DataRoot, f)));
            src.Add("game.json", "{ \"displayName\": \"X\", \"dispalyName\": \"typo\" }");
            Assert.Throws<DataLoadException>(() => DataRepository.Load(src));
        }

        [Test]
        public void DataValidator_BrokenReference_IsReported()
        {
            var src = new MemoryDataSource();
            foreach (string f in new FileDataSource(DataRoot).ListFiles())
                src.Add(f, File.ReadAllText(Path.Combine(DataRoot, f)));
            src.Add("outlets.json", "[{ \"id\": \"air-supply-vent\", \"displayName\": \"V\", \"fluid\": \"lava\", \"measured\": \"flow\", \"unit\": \"m3/h\", \"grille\": \"vent-grille\", \"grilleFaceAreaM2\": 0.04, \"tiers\": { \"1\": { \"min\": 25, \"ideal\": 35, \"max\": 60 } } }]");
            var errors = DataValidator.Validate(DataRepository.Load(src));
            Assert.IsTrue(errors.Exists(e => e.Contains("lava")), string.Join("\n", errors));
        }
    }
}
