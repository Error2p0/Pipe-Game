using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Codes;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Explosions, ruins and chain reactions (<c>08-failures.md</c>), the gas codes (<c>09-codes.md</c>) and gas mains (<c>03-city.md</c>).</summary>
    public class ExplosionTests
    {
        const string Gas = BuildingState.GasFluid;

        [Test]
        public void T16_ExplosionRadius()
        {
            double e = Blast.EnergyJoules(2, 50e6, 0.1);
            Assert.AreEqual(1e7, e, 1e-3);
            Assert.AreEqual(15.08, Blast.RadiusMetres(e, 0.07), 15.08 * 0.005);
        }

        [Test]
        public void Damage_FallsOffWithDistance()
        {
            Assert.AreEqual(1, Blast.Damage(0, 15), 1e-12);
            Assert.AreEqual(0.5, Blast.Damage(7.5, 15), 1e-12);
            Assert.AreEqual(0, Blast.Damage(20, 15), 1e-12);
        }

        /// <summary>A city with everything unlocked, money, and the reference layout built in two neighbouring tier 3 buildings.</summary>
        static GameContext CityWithNeighbours(out BuildingSession a, out BuildingSession b, out double distance, ulong seed = 11)
        {
            GameState g = GameContext.NewGame(Data, seed);
            g.Wallet = 1e9;
            g.City!.UnlockedTier = 3;
            var ctx = new GameContext(Data, g);
            List<Plot> t3 = ctx.City!.Plots.FindAll(p => p.Tier == 3);
            Plot? pa = null, pb = null;
            double best = double.MaxValue;
            foreach (Plot x in t3)
                foreach (Plot y in t3)
                {
                    if (x == y) continue;
                    double d = Edge(ctx, x, y);
                    if (d < best && d > 0) { best = d; pa = x; pb = y; }
                }
            Assert.NotNull(pa);
            distance = best;
            a = Build(ctx, pa!);
            b = Build(ctx, pb!);
            ctx.Leave();
            return ctx;
        }

        static double Edge(GameContext ctx, Plot from, Plot to)
        {
            double dx = System.Math.Max(0, System.Math.Max(to.X - from.CentreX, from.CentreX - (to.X + to.Width)));
            double dy = System.Math.Max(0, System.Math.Max(to.Y - from.CentreY, from.CentreY - (to.Y + to.Height)));
            return System.Math.Sqrt(dx * dx + dy * dy) * ctx.City!.TileMetres;
        }

        static BuildingSession Build(GameContext ctx, Plot p)
        {
            BuildingSession s = ctx.Open(p.Id);
            GeneratedBuilding made = ctx.CityBuildings!.Get(p.Id)!;
            Assert.IsTrue(made.Reference.Solvable, made.Reference.Reason);
            s.Record.Design = made.Reference.State.Clone();
            s.MarkDirty();
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.Supply = new WaterSupply(true, BuildingSession.ReferenceWaterPressure(ctx.Data));
            s.GasSupply = new WaterSupply(true, BuildingSession.ReferenceGasPressure(ctx.Data));
            s.SolveNow();
            return s;
        }

        /// <summary>Blast energy whose radius gives a damage value at a distance.</summary>
        static double EnergyFor(double damage, double distance)
        {
            double radius = distance / (1 - damage);
            double e = radius / Data.Balancing.Failure.BlastRadiusK;
            return e * e * e;
        }

        [Test]
        public void Explosion_MakesARuin_ThatCannotBeOpenedOrRebuilt()
        {
            GameContext ctx = CityWithNeighbours(out BuildingSession a, out _, out _);
            string id = a.Record.BuildingId;
            ctx.Explode(a, 1e5);
            Assert.IsTrue(ctx.IsRuin(id));
            Assert.IsNull(ctx.FindSession(id));
            Assert.IsFalse(ctx.CanOpen(id));
            Assert.IsNull(ctx.Game.FindBuilding(id));
            Assert.IsFalse(ctx.Reconstruct(id), "only with Reconstruction permits");
            Assert.AreEqual(1, ctx.Game.Statistics.Explosions);
        }

        [Test]
        public void Explosion_Fine_TheLargerOfTwoHoursGrossAndTheMinimum_InsuranceHalvesIt()
        {
            GameContext ctx = CityWithNeighbours(out BuildingSession a, out _, out _);
            double gross = a.CommittedEvaluation.PotentialGross;
            double expected = System.Math.Max(2 * 3600 * gross, 5000 * 25);
            double wallet = ctx.Game.Wallet;
            ExplosionReport r = ctx.Explode(a, 1);
            Assert.AreEqual(expected, r.Fine, expected * 1e-6);
            Assert.AreEqual(wallet - expected, ctx.Game.Wallet, 1);

            GameContext insured = CityWithNeighbours(out BuildingSession c, out _, out _);
            Assert.IsTrue(insured.SetInsurance(true));
            ExplosionReport ri = insured.Explode(c, 1);
            Assert.AreEqual(expected / 2, ri.Fine, expected * 1e-6);
        }

        [Test]
        public void Explosion_HeavyDamageRuinsANeighbour_LightDamageKnocksPartsOffline()
        {
            GameContext ctx = CityWithNeighbours(out BuildingSession a, out BuildingSession b, out double d);
            string bid = b.Record.BuildingId;
            ExplosionReport heavy = ctx.Explode(a, EnergyFor(0.8, d));
            Assert.IsTrue(ctx.IsRuin(bid), "damage 0.8 is heavy");
            Assert.IsTrue(heavy.Neighbours.Exists(n => n.PlotId == bid && n.Ruined));

            GameContext ctx2 = CityWithNeighbours(out BuildingSession a2, out BuildingSession b2, out double d2);
            ExplosionReport light = ctx2.Explode(a2, EnergyFor(0.5, d2));
            NeighbourDamage hit = light.Neighbours.Find(n => n.PlotId == b2.Record.BuildingId)!;
            Assert.IsFalse(hit.Ruined);
            Assert.AreEqual(0.5, hit.Damage, 1e-6);
            FailureState f = b2.Record.Failures;
            Assert.Greater(f.Bursts.Count + f.DamagedEquipment.Count + f.Leaks.Count, 0, "something gave way");
            Assert.IsFalse(ctx2.IsRuin(b2.Record.BuildingId));
        }

        [Test]
        public void Explosion_BelowLightDamage_LeavesANeighbourAlone()
        {
            GameContext ctx = CityWithNeighbours(out BuildingSession a, out BuildingSession b, out double d);
            ExplosionReport r = ctx.Explode(a, EnergyFor(0.05, d));
            Assert.IsFalse(r.Neighbours.Exists(n => n.PlotId == b.Record.BuildingId));
            Assert.AreEqual(0, b.Record.Failures.Leaks.Count + b.Record.Failures.Bursts.Count);
        }

        [Test]
        public void ChainReaction_TheNeighbourLeaks_AndEscalatesOnItsOwnTimer()
        {
            GameContext ctx = CityWithNeighbours(out BuildingSession a, out BuildingSession b, out double d);
            ctx.Explode(a, EnergyFor(0.55, d));
            Assert.Greater(b.Record.Failures.Leaks.Count, 0, "a gas building at damage 0.55 starts a leak");
            Assert.IsFalse(b.GasSafety.Evacuated, "never instantly: its leak has to fill its own rooms first");
            b.GasSafety.RaiseAlarm();
            Assert.AreEqual(Data.Balancing.Failure.ExplosionTimerSeconds, b.GasSafety.TimerLeft, 1e-9);
        }

        [Test]
        public void Reconstruction_NeedsThePermit_AndRebuildsWithNoEdits()
        {
            GameContext ctx = CityWithNeighbours(out BuildingSession a, out _, out _);
            string id = a.Record.BuildingId;
            ctx.Explode(a, 1);
            ctx.Game.Prestige[PrestigeEffects.ReconstructionPermits] = 1;
            double price = ctx.ReconstructionPrice(id);
            Assert.AreEqual(20 * ctx.CityBuildings!.Get(id)!.Reference.Cost, price, 1e-6);
            double wallet = ctx.Game.Wallet;
            Assert.IsTrue(ctx.Reconstruct(id));
            Assert.AreEqual(wallet - price, ctx.Game.Wallet, 1e-3);
            Assert.IsFalse(ctx.IsRuin(id));
            BuildingSession rebuilt = ctx.Open(id);
            Assert.AreEqual(0, rebuilt.Committed.Air.Segments.Count, "no player edits");
        }

        // ---------- gas codes ----------

        static BuildingSession Coded(params string[] codes)
        {
            BuildingLayoutDef d = Data.HandmadeBuilding("gas-house");
            var def = new BuildingLayoutDef
            {
                Id = d.Id, DisplayName = d.DisplayName, Tier = d.Tier, BuildingType = d.BuildingType, Width = d.Width, Height = d.Height, Floors = d.Floors,
                Rooms = d.Rooms, EntryOpenings = d.EntryOpenings, Outlets = d.Outlets, Equipment = d.Equipment, Doors = d.Doors, Codes = new List<string>(codes),
            };
            var layout = BuildingLayout.From(Data, def);
            var g = new GameState { Wallet = 1e7 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            g.Buildings.Add(r);
            return new BuildingSession(Data, layout, r, g);
        }

        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        [Test]
        public void GasSeparation_NearThePanel_Violates()
        {
            BuildingSession s = Coded("gas-separation");
            // The panel is at (0, 7). A pipe in the hall within 2 tiles of it breaks the code; one further off does not.
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5), (7, 5), (2, 5)), 22, "copper", false, Gas)));
            List<Violation> near = CodeChecker.Check(Data, s.Layout, s.State);
            Assert.IsTrue(near.Exists(v => v.CodeId == "gas-separation" && v.Tiles.Contains(new TilePos(2, 5))), "2 tiles from the panel");

            BuildingSession ok = Coded("gas-separation");
            Assert.IsTrue(ok.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5), (7, 5), (3, 5)), 22, "copper", false, Gas)));
            Assert.AreEqual(0, CodeChecker.Check(Data, ok.Layout, ok.State).Count);
        }

        [Test]
        public void AccessibleShutoffs_EveryBranchNeedsAValve()
        {
            BuildingSession s = Coded("accessible-shutoffs");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), 28, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(CodeChecker.Check(Data, s.Layout, s.State).Exists(v => v.CodeId == "accessible-shutoffs"), "a tee with an unprotected branch to a stove");
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), "shutoff-valve", false, Gas)));
            Assert.AreEqual(0, CodeChecker.Check(Data, s.Layout, s.State).Count);
        }

        [Test]
        public void AccessibleShutoffs_AnAutomaticValveCounts_ASingleRunNeedsNone()
        {
            BuildingSession s = Coded("accessible-shutoffs");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.AreEqual(0, CodeChecker.Check(Data, s.Layout, s.State).Count, "no tee, no branch");

            BuildingSession t = Coded("accessible-shutoffs");
            Assert.IsTrue(t.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 4)), 28, "copper", false, Gas)));
            Assert.IsTrue(t.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(t.Execute(new PlaceInlineCommand(new TilePos(8, 2), "auto-shutoff-valve", false, Gas)));
            Assert.AreEqual(0, CodeChecker.Check(Data, t.Layout, t.State).Count, "an automatic valve counts; the capped stub reaches no outlet");
        }

        [Test]
        public void GasRoomVentilation_BlocksOnline_UntilThereIsEnoughAir()
        {
            BuildingSession s = Coded("gas-room-ventilation");
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("gas", "gas-regulator")));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Violations.Exists(v => v.CodeId == "gas-room-ventilation"), "a kitchen with a stove and no air");
            Assert.Greater(s.CommittedEvaluation.BlockingViolations, 0);
            // A passive vent gives 30 m³/h: 0.56 air changes in this 54 m³ kitchen.
            Assert.IsTrue(s.Execute(new PlaceSafetyCommand("passive-vent-opening", new TilePos(9, 3))));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            Assert.IsFalse(s.CommittedEvaluation.Violations.Exists(v => v.CodeId == "gas-room-ventilation"));
        }

        // ---------- gas mains ----------

        static GameContext NewCity()
        {
            GameState g = GameContext.NewGame(Data, 7);
            g.Wallet = 1e7;
            g.City!.UnlockedTier = 5;
            return new GameContext(Data, g);
        }

        static List<(int x, int y)> L((int x, int y) a, (int x, int y) b)
        {
            var path = new List<(int, int)> { a };
            int x = a.x, y = a.y;
            while (x != b.x) { x += System.Math.Sign(b.x - x); path.Add((x, y)); }
            while (y != b.y) { y += System.Math.Sign(b.y - y); path.Add((x, y)); }
            return path;
        }

        static FacilityPlacement Station(GameContext c) => c.City!.Facilities.Find(f => f.Utility == "gas")!;

        [Test]
        public void GasMain_PressureClasses_PriceAndCapacity()
        {
            PipeFamilyDef f = Utilities.FamilyFor(Data, "gas")!;
            double lp = Utilities.PricePerTile(Data, f, 100, 0, "lp"), mp = Utilities.PricePerTile(Data, f, 100, 0, "mp");
            Assert.AreEqual(60, lp, 1e-9, "gas × 1.2 of the water main");
            Assert.Greater(mp, lp);
            double d = 0.125;
            Assert.AreEqual(System.Math.PI * d * d / 4 * 10, Utilities.CapacityM3s(f, 125, "lp"), 1e-9);
            Assert.AreEqual(System.Math.PI * d * d / 4 * 20, Utilities.CapacityM3s(f, 125, "mp"), 1e-9);
        }

        [Test]
        public void GasMain_ConnectsABuilding_AtItsClassPressure()
        {
            GameContext c = NewCity();
            Plot plot = c.City!.Plots.Find(p => p.Tier == 3)!;
            FacilityPlacement st = Station(c);
            RouteQuote lp = c.LayMain("gas", 90, 0, L((st.X, st.Y), (plot.X, plot.Y)), true, "lp");
            Assert.IsTrue(lp.Valid, lp.Error);
            c.Open(plot.Id);
            c.SolveCity();
            Assert.IsTrue(c.CityResult!.Gas.TryGetValue(plot.Id, out WaterConnection w) && w.Connected);
            Assert.AreEqual(7500, w.PressurePa, 500);
            Assert.IsTrue(c.FindSession(plot.Id)!.GasSupply.Connected);

            GameContext m = NewCity();
            m.LayMain("gas", 90, 0, L((st.X, st.Y), (plot.X, plot.Y)), true, "mp");
            m.Open(plot.Id);
            m.SolveCity();
            Assert.AreEqual(200000, m.CityResult!.Gas[plot.Id].PressurePa, 5000);
        }

        [Test]
        public void GasMain_BranchesOnlyFromItsOwnClass()
        {
            GameContext c = NewCity();
            FacilityPlacement st = Station(c);
            (int x, int y) a = (st.X, st.Y), b = (st.X, st.Y - 3 >= 4 ? st.Y - 3 : st.Y + 3);
            Assert.IsTrue(c.LayMain("gas", 90, 0, L(a, b), true, "lp").Valid);
            RouteQuote mp = Utilities.Quote(Data, c.City!, c.Game.City!.Utilities, "gas", 90, 0, L(b, (b.x + 2, b.y)), true, 1, "mp");
            Assert.IsFalse(mp.Valid, "an MP branch cannot start on an LP main");
            RouteQuote lp = Utilities.Quote(Data, c.City!, c.Game.City!.Utilities, "gas", 90, 0, L(b, (b.x + 2, b.y)), true, 1, "lp");
            Assert.IsTrue(lp.Valid, lp.Error);
        }

        [Test]
        public void SeparationViolations_GasNearSubstationAndNextToWater()
        {
            GameContext c = NewCity();
            CityLayout city = c.City!;
            FacilityPlacement sub = city.Facilities.Find(f => f.Utility == "power")!;
            FacilityPlacement st = Station(c);
            RouteQuote q = Utilities.Quote(Data, city, c.Game.City!.Utilities, "gas", 90, 0, L((st.X, st.Y), (sub.X, sub.Y)), false, 1, "lp");
            Assert.IsTrue(q.Valid, q.Error);
            Assert.IsTrue(q.Violations.Exists(v => v.Kind == "near-substation"));
            RouteQuote drilled = Utilities.Quote(Data, city, c.Game.City!.Utilities, "gas", 90, 0, L((st.X, st.Y), (sub.X, sub.Y)), true, 1, "lp");
            Assert.AreEqual(0, drilled.Violations.Count, "directional drilling is legal");

            FacilityPlacement plant = city.Facilities.Find(f => f.Utility == "water")!;
            (int x, int y) far = (plant.X + 6, plant.Y);
            Assert.IsTrue(c.LayMain("water", 100, 0, L((plant.X, plant.Y), far), true).Valid);
            var u = c.Game.City!.Utilities;
            RouteQuote beside = Utilities.Quote(Data, city, u, "gas", 90, 0, L((st.X, st.Y), (plant.X + 3, plant.Y + 1)), false, 1, "lp");
            Assert.IsTrue(beside.Violations.Exists(v => v.Kind == "main-separation"));
        }

        [Test]
        public void TableDensity_ForMpMains_DiffersSlightlyFromTheIdealGasLaw()
        {
            FluidDef def = Data.Fluid(Gas);
            var ideal = new Sim.Fluids.TableFluid(def, Data.FluidTables[Gas], Data.Balancing.Physics.GasConstant);
            var real = new Sim.Fluids.TableFluid(def, Data.FluidTables[Gas], Data.Balancing.Physics.GasConstant, true);
            double p = 3e5, t = 253.15;
            double ratio = real.Density(p, t) / ideal.Density(p, t);
            Assert.AreNotEqual(1.0, ratio, "D21");
            Assert.AreEqual(1.0, ratio, 0.02);
        }

        // ---------- save ----------

        [Test]
        public void Save_GasStateRoundTrips()
        {
            BuildingSession s = Coded("noise");
            s.Record.Failures.Leaks.Add(new LeakState { Key = "leak-1", Cause = "fitting", X = 7, Y = 2, AreaM2 = 1e-5 });
            s.Record.Failures.Gas.Alarm = true;
            s.Record.Failures.Gas.TimerLeft = 123;
            s.Record.Failures.Certificate.Issued = true;
            s.Record.Failures.Certificate.SecondsLeft = 77;
            s.Record.Committed.Safety.Add(new SafetyDevice { EquipmentId = "gas-detector", X = 7, Y = 3 });
            s.Game.Prestige["pay-rates"] = 2;
            s.Game.Reputation = 5;
            GameState copy = SaveSerializer.Copy(s.Game);
            BuildingRecord r = copy.Buildings[0];
            Assert.AreEqual("leak-1", r.Failures.Leaks[0].Key);
            Assert.AreEqual(123, r.Failures.Gas.TimerLeft, 1e-9);
            Assert.AreEqual(77, r.Failures.Certificate.SecondsLeft, 1e-9);
            Assert.AreEqual("gas-detector", r.Committed.Safety[0].EquipmentId);
            Assert.AreEqual(2, copy.Prestige["pay-rates"]);
            Assert.AreEqual(5, copy.Reputation);
        }

        [Test]
        public void OldSave_Tier3Rebuilt_Tier2Kept()
        {
            GameState g = GameContext.NewGame(Data, 11);
            g.Wallet = 1e6;
            g.City!.UnlockedTier = 3;
            var ctx = new GameContext(Data, g);
            Plot t2 = ctx.City!.Plots.Find(p => p.Tier == 2)!, t3 = ctx.City.Plots.Find(p => p.Tier == 3)!;
            ctx.Open(t2.Id).Record.SpentTotal = 100;
            ctx.Open(t3.Id).Record.SpentTotal = 1000;
            ctx.Leave();
            GameState saved = SaveSerializer.Copy(g);
            saved.GeneratorVersion = 3;
            double wallet = saved.Wallet;
            var reloaded = new GameContext(Data, saved);
            Assert.AreEqual(wallet + 1000, reloaded.Game.Wallet, 1e-6, "tier 3 refunded for its new gas network");
            Assert.AreEqual(100, reloaded.Game.FindBuilding(t2.Id)!.SpentTotal, 1e-9);
            Assert.AreEqual(0, reloaded.Game.FindBuilding(t3.Id)!.SpentTotal, 1e-9);
            Assert.AreEqual(SaveSerializer.CurrentGeneratorVersion, reloaded.Game.GeneratorVersion);
        }
    }
}
