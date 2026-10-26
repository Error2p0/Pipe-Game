using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class PayoutTests
    {
        static EconomyDef E => Data.Balancing.Economy;
        static readonly OutletRequirement T14 = new OutletRequirement(40, 60, 90);
        static readonly OutletRequirement T15 = new OutletRequirement(20, 30, 0);

        static double M(OutletRequirement r, double d, bool risky, out OutletZone zone) => Payout.Multiplier(r, d, risky, E, out zone);

        [Test]
        public void T14_AtMinimum_WorkingBandPaysBase()
        {
            Assert.AreEqual(1.0, M(T14, 40, false, out OutletZone z));
            Assert.AreEqual(OutletZone.Working, z);
        }

        [Test]
        public void T14_AtIdeal_StillBase()
        {
            Assert.AreEqual(1.0, M(T14, 60, false, out OutletZone z));
            Assert.AreEqual(OutletZone.Working, z);
        }

        [Test]
        public void T14_AtCodeMax_CapReachedEarly()
        {
            // max/min = 2.25 < 5, so rCap = 2.25 and the full cap is reached at the code maximum [confirm].
            Assert.AreEqual(2.0, M(T14, 90, false, out OutletZone z), 1e-12);
            Assert.AreEqual(OutletZone.Overdelivery, z);
        }

        [Test]
        public void T14_AboveCodeMax_PaysZero()
        {
            Assert.AreEqual(0.0, M(T14, 91, false, out OutletZone z));
            Assert.AreEqual(OutletZone.AboveCodeMax, z);
        }

        [Test]
        public void T14_BelowMinimum_ZoneBelowMinimum()
        {
            Assert.AreEqual(0.0, M(T14, 39, false, out OutletZone z));
            Assert.AreEqual(OutletZone.BelowMinimum, z);
        }

        [Test]
        public void T14_Risky_CapIs2_5AtCodeMax()
        {
            Assert.AreEqual(2.5, M(T14, 90, true, out _), 1e-12);
        }

        [Test]
        public void T14_MidOverdelivery_SquareRootDiminishingReturns()
        {
            // r = 1.875, rIdeal 1.5, rCap 2.25 → x = 0.5 → m = 1 + sqrt(0.5).
            Assert.AreEqual(1 + System.Math.Sqrt(0.5), M(T14, 75, false, out OutletZone z), 1e-12);
            Assert.AreEqual(OutletZone.Overdelivery, z);
        }

        [Test]
        public void T15_NoCodeMax_CapAtFiveTimesMinimum()
        {
            Assert.AreEqual(2.0, M(T15, 100, false, out OutletZone z), 1e-12);
            Assert.AreEqual(OutletZone.Overdelivery, z);
        }

        [Test]
        public void T15_NoCodeMax_FlatAboveCap()
        {
            Assert.AreEqual(2.0, M(T15, 200, false, out OutletZone z), 1e-12);
            Assert.AreEqual(OutletZone.Overdelivery, z);
            Assert.AreEqual(2.5, M(T15, 200, true, out _), 1e-12);
        }

        [Test]
        public void DegenerateCap_IdealAboveCapRatio_PaysBaseInWholeBand()
        {
            var r = new OutletRequirement(10, 60, 0);
            Assert.AreEqual(1.0, M(r, 70, false, out OutletZone z));
            Assert.AreEqual(OutletZone.Overdelivery, z);
        }

        [Test]
        public void Requirement_FromData_ConvertsM3hToSi()
        {
            OutletDef vent = Data.Outlet("air-supply-vent");
            OutletRequirement r = OutletRequirement.FromDef(vent, vent.Tiers["3"]);
            Assert.AreEqual(Units.M3hToM3s(40), r.Min, 1e-15);
            Assert.AreEqual(Units.M3hToM3s(60), r.Ideal, 1e-15);
            Assert.AreEqual(Units.M3hToM3s(90), r.Max, 1e-15);
        }
    }

    public class IncomeTests
    {
        [Test]
        public void Building_OneVentUnconnected_IsOfflineAndPaysNothing()
        {
            BuildingSession s = TestHouseWithFan();
            DrawAndCommit(s, Run((0, 3), (3, 3), (3, 5)));
            BuildingEvaluation ev = s.CommittedEvaluation;
            Assert.IsFalse(ev.Online);
            Assert.AreEqual(2, ev.BelowMinimum);
            Assert.AreEqual(0.0, ev.Gross);
            Assert.Greater(ev.Find("vent-1")!.Delivery, 0.0);
            Assert.AreEqual(OutletZone.NotConnected, ev.Find("vent-2")!.Zone);
        }

        [Test]
        public void Building_Offline_StillPaysElectricity()
        {
            BuildingSession s = TestHouseWithFan();
            DrawAndCommit(s, Run((0, 3), (3, 3), (3, 5)));
            BuildingEvaluation ev = s.CommittedEvaluation;
            Assert.Greater(ev.PowerW, 0.0);
            Assert.Greater(ev.Electricity, 0.0);
            Assert.Less(ev.Net, 0.0);
        }

        [Test]
        public void Electricity_IsPowerTimesPrice()
        {
            BuildingSession s = TestHouseWithFan();
            DrawAndCommit(s, Run((0, 3), (3, 3), (3, 5)));
            EconomyDef e = Data.Balancing.Economy;
            BuildingEvaluation ev = s.CommittedEvaluation;
            int fan = s.CommittedSnapshot!.Compiled.FanLink["air-intake"];
            double p = s.CommittedSnapshot.Result.Power[fan];
            Assert.AreEqual(p, ev.PowerW, 1e-12);
            Assert.AreEqual(p / 1000 * e.ElectricityPricePerKWs * e.ElectricityTierFactor[0], ev.Electricity, 1e-12);
        }

        [Test]
        public void Design_DoesNotEarnUntilCommitted()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            s.SolveNow();
            Assert.IsTrue(s.HasPendingDesign);
            Assert.Greater(s.DesignEvaluation.PowerW, 0.0, "the design preview runs the fan");
            Assert.AreEqual(0.0, s.CommittedEvaluation.PowerW, "nothing is built yet");
        }

        [Test]
        public void EconomyStep_OnlineBuildingEarnsNetAndDropsBudgetCap()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (2, 3), (2, 5), (3, 5)));
            Draw(s, Run((2, 4), (8, 4), (8, 5)));
            Draw(s, Run((7, 4), (7, 1)));
            s.Record.Committed = s.Record.Design.Clone();  // built (the commit rules have their own tests)
            s.MarkDirty();
            s.SolveNow();
            BuildingEvaluation ev = s.CommittedEvaluation;
            Assert.IsTrue(ev.Online, "every vent connected and fed");
            double before = s.Game.Wallet;
            GameLoop.EconomyStep(s.Game, new[] { s }, 2.0);
            Assert.AreEqual(before + ev.Net * 2.0, s.Game.Wallet, 1e-9);
            Assert.IsTrue(s.Record.EverOnline);
        }

        [Test]
        public void Debt_AccruesWhenWalletEmpty_RepaidFromIncomeFirst()
        {
            var g = new GameState { Wallet = 1.0 };
            g.ApplyIncome(-3.0);
            Assert.AreEqual(0.0, g.Wallet);
            Assert.AreEqual(2.0, g.Debt, 1e-12);
            Assert.IsFalse(g.CanAfford(0.5), "purchases are blocked while in debt");
            g.ApplyIncome(1.5);
            Assert.AreEqual(0.0, g.Wallet);
            Assert.AreEqual(0.5, g.Debt, 1e-12);
            g.ApplyIncome(2.0);
            Assert.AreEqual(1.5, g.Wallet, 1e-12);
            Assert.AreEqual(0.0, g.Debt);
        }
    }

    public class MoneyFormatTests
    {
        [TestCase(0.15, "$0.15")]
        [TestCase(12.4, "$12.40")]
        [TestCase(999.9, "$999")]
        [TestCase(1250, "$1.25K")]
        [TestCase(3_400_000, "$3.40M")]
        [TestCase(2.5e9, "$2.50B")]
        [TestCase(7.1e12, "$7.10T")]
        [TestCase(1e15, "$1.00aa")]
        [TestCase(4.2e18, "$4.20ab")]
        [TestCase(-5.5, "−$5.50")]
        public void FormatMoney_ShortScaleSuffixes(double amount, string expected)
        {
            Assert.AreEqual(expected, Units.FormatMoney(amount, "$"));
        }

        [Test]
        public void FormatMoney_RoundsUpIntoNextSuffix()
        {
            Assert.AreEqual("$1.00M", Units.FormatMoney(999_999, "$"));
        }
    }
}
