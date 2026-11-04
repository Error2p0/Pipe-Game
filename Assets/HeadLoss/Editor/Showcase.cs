using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Session;
using HeadLoss.Unity;
using UnityEditor;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>
    /// Builds scenes for screenshots and for looking at gas behaviour in play mode (dev tool, never in a player build): a
    /// lit-up city with its mains, a building with a leak filling a room, an alarm with its countdown, a ruin. Call from
    /// the CLI with <c>script eval</c> while in play mode. Games started here never write the save.
    /// </summary>
    public static class Showcase
    {
        static GameRoot Root => Object.FindAnyObjectByType<GameRoot>();

        /// <summary>A map path of city tiles, x first, then y.</summary>
        static List<(int x, int y)> L((int x, int y) a, (int x, int y) b)
        {
            var path = new List<(int, int)> { a };
            int x = a.x, y = a.y;
            while (x != b.x) { x += System.Math.Sign(b.x - x); path.Add((x, y)); }
            while (y != b.y) { y += System.Math.Sign(b.y - y); path.Add((x, y)); }
            return path;
        }

        /// <summary>
        /// A city with many buildings built to their reference layouts and connected by drilled mains (legal and tidy), the
        /// nearest plots to the centre first. Returns a line about what it built.
        /// </summary>
        public static string BuiltCity(ulong seed = 7, int buildings = 30)
        {
            GameRoot root = Root;
            GameState game = GameContext.NewGame(root.Data!, seed);
            game.Wallet = 5e8;
            game.City!.UnlockedTier = 3;
            root.StartGame(game, false);
            GameContext ctx = root.Context!;
            CityLayout city = ctx.City!;
            var plots = city.Plots.FindAll(p => root.Data!.BuildingTier(p.Tier).Available);
            plots.Sort((a, b) => city.DistanceToCentre(a.CentreX, a.CentreY).CompareTo(city.DistanceToCentre(b.CentreX, b.CentreY)));
            int built = 0;
            foreach (Plot p in plots)
            {
                if (built >= buildings) break;
                GeneratedBuilding? g = ctx.CityBuildings!.Get(p.Id);
                if (g == null || !g.Reference.Solvable) continue;
                BuildingSession s = ctx.Open(p.Id);
                s.Record.Design = g.Reference.State.Clone();
                s.MarkDirty();
                s.Record.EverOnline = true;
                if (!s.Commit()) continue;
                Connect(ctx, city, p, g);
                built++;
            }
            ctx.Leave();
            root.Context!.SolveCity();
            root.ShowCityView();
            return $"{built} buildings, wallet {game.Wallet:0}, {game.City.Utilities.Mains.Count} mains";
        }

        static void Connect(GameContext ctx, CityLayout city, Plot p, GeneratedBuilding g)
        {
            // Branch from the nearest tile of what is already laid (or the facility), so the mains grow as a tree.
            void Main(string utility, double d, string cls)
            {
                var starts = new List<(int x, int y)>();
                foreach (FacilityPlacement x in city.Facilities) if (x.Utility == utility) starts.Add((x.X, x.Y));
                foreach (MainState m in ctx.Game.City!.Utilities.Mains)
                    if (m.Utility == utility && m.PressureClass == cls)
                        foreach (int[] t in m.Tiles) starts.Add((t[0], t[1]));
                starts.Sort((a, b) => (System.Math.Abs(a.x - p.X) + System.Math.Abs(a.y - p.Y)).CompareTo(System.Math.Abs(b.x - p.X) + System.Math.Abs(b.y - p.Y)));
                foreach ((int x, int y) s in starts)
                {
                    if (s == (p.X, p.Y)) return;
                    if (ctx.LayMain(utility, d, 0, L(s, (p.X, p.Y)), true, cls).Valid) return;
                }
            }
            if (g.Layout.OpeningFor("water") != null) Main("water", 150, "");
            if (g.Layout.OpeningFor("gas") != null) Main("gas", 90, "lp");
        }

        /// <summary>
        /// A timed leak in a real kitchen (reference layout of a generated apartment block), with and without its vent running
        /// and a detector: when the alarm comes, when the air near the stove reaches the lower limit, and how long the air takes to
        /// clear once the supply is closed. Runs in edit mode: pure rules at 1 s ticks. The numbers that say whether gas is fair.
        /// </summary>
        /// <param name="scenario">0 burst without a detector, 1 with one, 2 fans off, 3 a fitting leak.</param>
        public static string LeakTimeline(int scenario)
        {
            var data = HeadLoss.Core.Data.DataRepository.Load(new HeadLoss.Core.Data.FileDataSource(DataTools.DataFolder));
            var sb = new System.Text.StringBuilder();
            GeneratedBuilding g = BuildingFactory.Create(data, new BuildingRequest("plot-1", "apartment-block", 11, "south"), DistrictKind.Residential)!;
            var scenarios = new[]
            {
                ("burst, no detector", true, false, 1.0), ("burst, detector", true, true, 1.0),
                ("burst, fans off", false, false, 1.0), ("fitting leak", true, false, 0.08),
            };
            {
                (string name, bool vent, bool detector, double areaShare) = scenarios[scenario];
                var game = new GameState { Wallet = 1e8 };
                BuildingRecord record = BuildingRecord.CreateFor(g.Layout);
                game.Buildings.Add(record);
                var s = new BuildingSession(data, g.Layout, record, game);
                record.Design = g.Reference.State.Clone();
                record.EverOnline = true;
                s.MarkDirty();
                s.Commit();
                HeadLoss.Core.Data.Defs.OutletPlacementDef stove = g.Layout.Def.Outlets.Find(o => o.Type == "gas-stove-outlet")!;
                TilePos at = BuildingLayout.PosOf(stove);
                DuctNetworkState net = s.Committed.Gas;
                DuctNode node = net.Nodes.Find(n => n.Kind == DuctNodeKind.Outlet && n.AnchorId == stove.Id)!;
                DuctSegment seg = net.Segments.Find(x => x.NodeA == node.Id || x.NodeB == node.Id)!;
                if (detector) record.Committed.Safety.Add(new SafetyDevice { EquipmentId = GasSafetyIds.Detector, X = at.X, Y = at.Y, Floor = at.F });
                if (!vent) foreach (EquipmentState e in record.Committed.Equipment) e.On = false;
                s.SolveNow();
                LeakState leak = s.Failures.AddLeak("burst", "", at, s.Failures.SegmentArea(seg) * data.Balancing.Gas.BurstLeakAreaFraction * areaShare, 0);
                s.SolveNow();
                double lfl = s.GasSafety.Lfl, alarm = -1, flammable = -1, flow = s.GasSafety.LeakFlow(leak);
                int room = g.Layout.RoomIndexAt(at);
                bool blew = false;
                s.GasSafety.Exploded += _ => blew = true;
                double t = 0;
                for (; t < 30 * 60 && !blew; t += 5)
                {
                    s.Tick(5);
                    if (alarm < 0 && s.GasSafety.Evacuated) alarm = t;
                    if (flammable < 0 && s.Rooms.Rooms[room].GasMax >= lfl) flammable = t;
                    if (flammable >= 0 && alarm >= 0) break;
                }
                sb.Append($"{name}: {Units.M3sToM3h(flow):0.0} m³/h into a {s.Rooms.Grid.RoomVolume[room]:0} m³ room · alarm {(alarm < 0 ? "never" : $"{alarm / 60:0.0} min")} · lower limit {(flammable < 0 ? "never" : $"{flammable / 60:0.0} min")}");
                // Close the supply: how long until the air is clear with what ventilation there is.
                s.Failures.State.Leaks.Clear();
                s.SolveNow();
                double clear = 0;
                for (; clear < 90 * 60; clear += 5)
                {
                    s.Tick(5);
                    if (s.GasSafety.MaxGas() < data.Balancing.Gas.ClearFraction * lfl) break;
                }
                sb.AppendLine($" · clear in {clear / 60:0} min after the leak stops · peak then {s.GasSafety.MaxGas() * 100:0.00}%");
            }
            return sb.ToString();
        }

        /// <summary>Opens the nearest gas building and bursts a gas pipe in it, so its room fills. Returns its plot id.</summary>
        public static string LeakInGasBuilding(float gasShare = 0f)
        {
            GameRoot root = Root;
            GameContext ctx = root.Context!;
            BuildingSession? s = null;
            foreach (BuildingSession x in ctx.Sessions)
                if (x.HasGas && x.Committed.Gas.Segments.Count > 0 && (s == null || x.Layout.Rooms.Count > s.Layout.Rooms.Count)) s = x;
            if (s == null) return "no gas building";
            root.OpenBuilding(s.Record.BuildingId);
            DuctSegment seg = s.Committed.Gas.Segments[s.Committed.Gas.Segments.Count - 1];
            DuctNetworkState net = s.Committed.Gas;
            string edge = BuildingFailures.Edges(BuildingState.GasFluid, net, seg)[0];
            s.Failures.AddLeak("burst", edge, BuildingFailures.MidTile(net, seg), s.Failures.SegmentArea(seg) * s.Data.Balancing.Gas.BurstLeakAreaFraction, ctx.Game.PlayTimeSeconds);
            if (gasShare > 0)
            {
                TilePos t = BuildingFailures.MidTile(net, seg);
                s.Rooms.SeedRoomGas(s.Layout.RoomIndexAt(t), gasShare);
            }
            s.SolveNow();
            return s.Record.BuildingId;
        }

        /// <summary>Raises the alarm in the open building.</summary>
        public static string Alarm()
        {
            BuildingSession? s = Root.Session;
            if (s == null) return "no open building";
            s.GasSafety.RaiseAlarm();
            s.SolveNow();
            return $"timer {s.GasSafety.TimerLeft}";
        }

        /// <summary>Blows up a building by plot id, or the first built gas building, at a chosen radius.</summary>
        public static string Explode(float radiusMetres = 45)
        {
            GameContext ctx = Root.Context!;
            BuildingSession? s = null;
            foreach (BuildingSession x in ctx.Sessions) if (x.HasGas) { s = x; break; }
            if (s == null) return "no gas building";
            double e = System.Math.Pow(radiusMetres / ctx.Data.Balancing.Failure.BlastRadiusK, 3);
            ExplosionReport r = ctx.Explode(s, e);
            return $"{r.PlotId}: fine {r.Fine:0}, {r.Neighbours.Count} neighbours";
        }
    }
}
