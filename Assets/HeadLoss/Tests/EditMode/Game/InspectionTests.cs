using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Codes;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Inspections;
using HeadLoss.Game.Offline;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Random inspections and the energy rating (<c>10-events.md</c> §Inspections, Phase 6 task 6).</summary>
    public class InspectionTests
    {
        // ---------- energy rating ----------

        static BuildingEvaluation Evaluation(string fluid, string measured, double delivery, double powerW, bool online = true)
        {
            var ev = new BuildingEvaluation { Online = online, PowerW = powerW };
            ev.PowerByNetwork[fluid] = powerW;
            ev.Outlets.Add(new OutletStatus { OutletId = "o", Def = new OutletDef { Fluid = fluid, Measured = measured }, Delivery = delivery });
            return ev;
        }

        [Test]
        public void Bands_FollowTheSpec()
        {
            var expected = new (double score, string grade, double pay)[]
            {
                (0.3, "A", 1.15), (0.6, "A", 1.15), (0.61, "B", 1.08), (0.8, "B", 1.08), (1.0, "C", 1.00), (1.2, "D", 0.95),
                (1.5, "E", 0.90), (2.0, "F", 0.85), (2.01, "G", 0.80), (40, "G", 0.80),
            };
            foreach (var (score, grade, pay) in expected)
            {
                RatingBandDef b = EnergyRating.Band(Data, score);
                Assert.AreEqual(grade, b.Grade, $"score {score}");
                Assert.AreEqual(pay, EnergyRating.PayFactor(Data, b.Grade), 1e-12);
            }
            Assert.AreEqual(1, EnergyRating.PayFactor(Data, EnergyRating.NotRated));
        }

        [Test]
        public void ReferencePower_ScalesWithTheCubeOfFlow_AndPressureToThePower1point5()
        {
            EnergyReference air = EnergyReference.From(Evaluation("air", "flow", 0.1, 100));
            Assert.AreEqual(100, EnergyRating.ScaledReferencePower(Data, air, Evaluation("air", "flow", 0.1, 0)), 1e-9);
            Assert.AreEqual(800, EnergyRating.ScaledReferencePower(Data, air, Evaluation("air", "flow", 0.2, 0)), 1e-9, "twice the flow: 2³");
            Assert.AreEqual(12.5, EnergyRating.ScaledReferencePower(Data, air, Evaluation("air", "flow", 0.05, 0)), 1e-9);

            EnergyReference water = EnergyReference.From(Evaluation("water", "pressure", 2e5, 50));
            Assert.AreEqual(50 * System.Math.Pow(2, 1.5), EnergyRating.ScaledReferencePower(Data, water, Evaluation("water", "pressure", 4e5, 0)), 1e-9);

            // Twice the reference's flow at the reference's own power per m³ cubed: score = 400 / 800 → A.
            Assert.IsTrue(EnergyRating.Score(Data, air, Evaluation("air", "flow", 0.2, 400), out double score));
            Assert.AreEqual(0.5, score, 1e-12);
            Assert.AreEqual("A", EnergyRating.Grade(Data, air, Evaluation("air", "flow", 0.2, 400), out _));
            Assert.AreEqual("G", EnergyRating.Grade(Data, air, Evaluation("air", "flow", 0.1, 250), out _));
        }

        [Test]
        public void NotRated_WithoutAnElectricReference_OrWhileOffline()
        {
            EnergyReference crank = EnergyReference.From(Evaluation("air", "flow", 0.1, 0));
            Assert.IsFalse(EnergyRating.Score(Data, crank, Evaluation("air", "flow", 0.1, 50), out _), "a hand crank draws nothing: no rating");
            EnergyReference fan = EnergyReference.From(Evaluation("air", "flow", 0.1, 100));
            Assert.IsFalse(EnergyRating.Score(Data, fan, Evaluation("air", "flow", 0.1, 50, online: false), out _));
            // A pump on a network the reference feeds from the mains counts against the grade.
            EnergyReference mixed = EnergyReference.From(Evaluation("air", "flow", 0.1, 100));
            mixed.PowerByNetwork["water"] = 0;
            BuildingEvaluation withPump = Evaluation("air", "flow", 0.1, 100);
            withPump.PowerByNetwork["water"] = 60;
            withPump.PowerW = 160;
            Assert.IsTrue(EnergyRating.Score(Data, mixed, withPump, out double s));
            Assert.AreEqual(1.6, s, 1e-12);
        }

        /// <summary>The small house (its reference has a small axial fan) built as its reference, committed and online.</summary>
        static GameContext SmallHouse()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "small-house"));
            BuildingSession s = ctx.Building!;
            s.Record.Design = ReferenceLayout.Build(Data, s.Layout);
            s.MarkDirty();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Online, "the reference layout is online");
            return ctx;
        }

        [Test]
        public void TheReferenceLayoutItself_RatesAroundC_AndTheGradeIsInThePay()
        {
            BuildingSession s = SmallHouse().Building!;
            InspectionState st = s.Record.Inspection;
            Assert.IsTrue(st.Rated);
            Assert.AreEqual(1, st.Score, 0.15, "the reference against itself (outside air differs a little)");
            StringAssert.IsMatch("^[BCD]$", st.Grade);
            BuildingEvaluation ev = s.CommittedEvaluation;
            Assert.AreEqual(st.Grade, ev.EnergyGrade);
            Assert.AreEqual(EnergyRating.PayFactor(Data, st.Grade), ev.EnergyFactor, 1e-12);
            double outlets = 0;
            foreach (OutletStatus o in ev.Outlets) outlets += o.BaseRate * o.Multiplier;
            Assert.AreEqual(outlets * ev.EnergyFactor * Progression.PrestigeEffects.PayFactor(Data, s.Game), ev.Gross, 1e-9);
        }

        [Test]
        public void ADesignChange_ProjectsItsRating_AndCommitRatesIt()
        {
            BuildingSession s = SmallHouse().Building!;
            string before = s.Record.Inspection.Grade;
            double scoreBefore = s.Record.Inspection.Score;
            // One duct a size wider or narrower, wherever the house stays online (the reference is balanced tightly).
            bool found = false;
            var segs = new List<(int id, double mm)>();
            foreach (DuctSegment seg in s.State.Air.Segments) segs.Add((seg.Id, seg.DiameterMm));
            foreach ((int id, double mm) in segs)
            {
                foreach (double to in new[] { 160.0, 100.0 })
                {
                    if (to == mm) continue;
                    Assert.IsTrue(s.Execute(new SetSegmentSizeCommand(id, to)));
                    s.SolveNow();
                    if (s.DesignEvaluation.Online && s.HasPendingDesign) { found = true; break; }
                    Assert.IsTrue(s.Undo());
                }
                if (found) break;
            }
            Assert.IsTrue(found, "some duct can change size and the house stays online");
            Assert.IsTrue(EnergyRating.Score(Data, s.EnergyReference, s.DesignEvaluation, out double direct));
            Assert.AreEqual(direct, s.ProjectedScore, 1e-12, "the projection reads the design preview");
            Assert.AreNotEqual(scoreBefore, s.ProjectedScore);
            Assert.AreEqual(EnergyRating.Band(Data, direct).Grade, s.ProjectedGrade);
            Assert.AreEqual(before, s.Record.Inspection.Grade, "what is built keeps its grade until Commit");
            double projected = s.ProjectedScore;
            Assert.IsTrue(s.Commit());
            Assert.IsFalse(s.Record.Inspection.Rated);
            s.SolveNow();
            Assert.IsTrue(s.Record.Inspection.Rated);
            Assert.AreEqual(projected, s.Record.Inspection.Score, 0.05 * projected, "the projection was right");
        }

        [Test]
        public void TheCrankStarter_IsNotRated()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "test-house"));
            BuildingSession s = ctx.Building!;
            Assert.Less(s.EnergyReference.PowerW, Data.Balancing.Inspection.RatingMinReferenceW);
            s.Record.Design = ReferenceLayout.Build(Data, s.Layout);
            s.Record.Design.Equipment[0].EquipmentId = "small-axial-fan";
            s.MarkDirty();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Online);
            Assert.AreEqual(EnergyRating.NotRated, s.Record.Inspection.Grade, "a fan in the starter is not punished against a crank");
            Assert.AreEqual(1, s.CommittedEvaluation.EnergyFactor);
        }

        // ---------- random inspections ----------

        /// <summary>The test house with three vents on ducts of a size, people for a share of the air, the IAQ code in force.</summary>
        static BuildingSession Crowded(double share, double ductMm)
        {
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(Data.HandmadeBuilding("test-house")))!;
            def.Codes = new List<string> { IndoorAirQuality.CodeId };
            var layout = BuildingLayout.From(Data, def);
            var game = new GameState { Wallet = 1e6 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(r);
            var s = new BuildingSession(Data, layout, r, game, budgetCap: 1e9);
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan")));
            Assert.IsTrue(Draw(s, Run((0, 3), (3, 3), (3, 5)), ductMm));
            Assert.IsTrue(Draw(s, Run((3, 3), (7, 3), (7, 1)), ductMm));
            Assert.IsTrue(Draw(s, Run((7, 3), (8, 3), (8, 5)), ductMm));
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            double q = 0;
            foreach (OutletStatus o in s.CommittedEvaluation.Outlets) q += o.Delivery;
            def.Rooms[0].Occupants = share * q / IndoorAirQuality.RequiredSupply(Data, 1);
            var crowded = new BuildingSession(Data, BuildingLayout.From(Data, def), r, game, budgetCap: 1e9);
            crowded.SolveNow();
            Assert.IsTrue(crowded.CommittedEvaluation.Online);
            return crowded;
        }

        static void TickSeconds(BuildingSession s, double seconds)
        {
            for (double t = 0; t < seconds; t += 0.5) s.Tick(0.5);
            s.SolveNow();
        }

        [Test]
        public void Pass_PaysAQuarterMoreForAnHour_ThenStops()
        {
            BuildingSession s = Crowded(0.5, 125);
            double gross = s.CommittedEvaluation.Gross;
            Assert.Greater(gross, 0);
            s.Inspections.Begin();
            Assert.IsTrue(s.Inspections.InspectorComing);
            TickSeconds(s, Data.Balancing.Inspection.InspectorWalkSeconds + 1.5);
            Assert.IsFalse(s.Inspections.InspectorComing);
            Assert.IsTrue(s.Inspections.Bonus, "nothing found: a pass");
            Assert.IsFalse(s.Inspections.Failed);
            Assert.IsTrue(s.CommittedEvaluation.InspectionBonus);
            Assert.AreEqual(gross * Data.Balancing.Inspection.PassPayFactor, s.CommittedEvaluation.Gross, gross * 1e-6);
            s.Record.Inspection.BonusLeft = 1;
            TickSeconds(s, 2);
            Assert.IsFalse(s.Inspections.Bonus);
            Assert.AreEqual(gross, s.CommittedEvaluation.Gross, gross * 1e-6);
        }

        [Test]
        public void Fail_FinesAndStartsADeadline_FixingClearsIt()
        {
            BuildingSession s = Crowded(1.1, 100);
            Assert.Greater(s.CommittedEvaluation.AirQualityCut, 0, "the narrow ducts leave the room above 1 000 ppm");
            double wallet = s.Game.Wallet, fine = s.Inspections.FailFine();
            Assert.AreEqual(System.Math.Max(Data.Balancing.Fines.InspectionFailGrossMinutes * 60 * s.CommittedEvaluation.PotentialGross,
                Data.Balancing.Fines.InspectionFailMinimum * Data.Balancing.Economy.TierFactor[0]), fine, 1e-9);
            s.Inspections.Begin();
            TickSeconds(s, Data.Balancing.Inspection.InspectorWalkSeconds + 1.5);
            Assert.IsTrue(s.Inspections.Failed);
            Assert.IsFalse(s.Inspections.Bonus);
            Assert.AreEqual(wallet - fine, s.Game.Wallet, 1e-6);
            InspectionState st = s.Record.Inspection;
            Assert.AreEqual(Data.Balancing.Inspection.FixDeadlineMinutes * 60 - 1, st.DeadlineLeft, 1.01);
            Assert.IsNotEmpty(st.Findings);
            StringAssert.StartsWith("Indoor air quality", st.Findings[0]);
            Assert.IsTrue(s.CommittedEvaluation.Online, "a fail alone does not take the building offline");

            Widen(s, 160);
            double afterFix = s.Game.Wallet;
            TickSeconds(s, 1.5);
            Assert.IsFalse(s.Inspections.Failed, "fixed in time");
            Assert.IsEmpty(st.Findings);
            Assert.AreEqual(afterFix, s.Game.Wallet, 1e-6, "no second fine");
        }

        static void Widen(BuildingSession s, double mm)
        {
            var ids = new List<int>();
            foreach (DuctSegment seg in s.State.Air.Segments) ids.Add(seg.Id);
            Assert.IsTrue(s.Execute(new SetSegmentSizeCommand(ids, mm)));
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            Assert.AreEqual(0, s.CommittedEvaluation.AirQualityCut);
        }

        [Test]
        public void MissedDeadline_SecondFine_OfflineUntilFixed()
        {
            BuildingSession s = Crowded(1.1, 100);
            s.Inspections.Begin();
            TickSeconds(s, Data.Balancing.Inspection.InspectorWalkSeconds + 1.5);
            Assert.IsTrue(s.Inspections.Failed);
            double wallet = s.Game.Wallet, fine = s.Inspections.FailFine();
            s.Record.Inspection.DeadlineLeft = 2;
            TickSeconds(s, 3);
            Assert.IsTrue(s.Inspections.Closed);
            Assert.AreEqual(wallet - fine, s.Game.Wallet, 1e-6, "the second fine");
            Assert.IsFalse(s.CommittedEvaluation.Online);
            Assert.IsTrue(s.CommittedEvaluation.InspectionClosed);
            Assert.AreEqual(0, s.CommittedEvaluation.Gross);

            Widen(s, 160);
            TickSeconds(s, 1.5);
            Assert.IsFalse(s.Inspections.Closed);
            Assert.IsFalse(s.Inspections.Failed);
            Assert.IsTrue(s.CommittedEvaluation.Online);
        }

        [Test]
        public void Rolls_ComeAtAboutTheChancePerHour()
        {
            // Over many simulated hours the visits come at 0.08 per hour of being online (pass bonuses excluded).
            BuildingSession s = Crowded(0.5, 125);
            int visits = 0;
            double online = 0;
            for (int i = 0; i < 4000000; i++)
            {
                bool counting = !s.Inspections.Bonus && !s.Inspections.InspectorComing;
                s.Inspections.Tick(1);
                if (counting) online += 1;
                if (s.Inspections.InspectorComing)
                {
                    visits++;
                    s.Record.Inspection.WalkLeft = 0;
                }
            }
            double expected = Data.Balancing.Inspection.ChancePerHour * online / 3600;
            Assert.AreEqual(expected, visits, 4 * System.Math.Sqrt(expected), $"{visits} visits in {online / 3600:0} h");
        }

        [Test]
        public void Offline_NoVisitStarts_TheDeadlinePauses_TheBonusRunsOutAtItsTime()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "small-house"));
            {
                BuildingSession s0 = ctx.Building!;
                s0.Record.Design = ReferenceLayout.Build(Data, s0.Layout);
                s0.MarkDirty();
                s0.Game.Wallet = 1e6;
                s0.Record.EverOnline = true;
                Assert.IsTrue(s0.Commit());
                s0.SolveNow();
                ctx.Leave();
            }
            BuildingSession s = ctx.Sessions[0];
            double gross = s.SteadyState(20).Gross;
            s.Record.Inspection.BonusLeft = 300;
            double wallet = ctx.Game.Wallet;
            OfflineReport r = OfflineCatchUp.Run(ctx, 900, 12);
            Assert.AreEqual(0, s.Record.Inspection.BonusLeft, "the bonus ran out while away");
            Assert.AreEqual(0, s.Record.Inspection.WalkLeft, "no inspector starts while away");
            double expected = gross * (300 * Data.Balancing.Inspection.PassPayFactor + 600);
            Assert.AreEqual(expected, r.Gross, expected * 0.01);

            s.Record.Inspection.Failed = true;
            s.Record.Inspection.DeadlineLeft = 500;
            s.Record.Inspection.Findings.Add("test");
            OfflineCatchUp.Run(ctx, 3600, 12);
            Assert.AreEqual(500, s.Record.Inspection.DeadlineLeft, "the fix deadline pauses while away");
        }

        [Test]
        public void InspectionState_SurvivesASave()
        {
            GameContext ctx = SmallHouse();
            InspectionState st = ctx.Sessions[0].Record.Inspection;
            st.BonusLeft = 123;
            st.Failed = true;
            st.DeadlineLeft = 45;
            st.Findings.Add("An outlet is above its code maximum (noise)");
            st.Counter = 7;
            GameState copy = SaveSerializer.Copy(ctx.Game);
            InspectionState back = copy.Buildings[0].Inspection;
            Assert.AreEqual(123, back.BonusLeft);
            Assert.IsTrue(back.Failed);
            Assert.AreEqual(45, back.DeadlineLeft);
            Assert.AreEqual(st.Findings, back.Findings);
            Assert.AreEqual(7, back.Counter);
            Assert.AreEqual(st.Grade, back.Grade);
            Assert.AreEqual(st.Rated, back.Rated);
        }
    }
}
