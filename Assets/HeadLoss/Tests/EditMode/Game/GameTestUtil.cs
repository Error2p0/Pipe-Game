using System.Collections.Generic;
using System.IO;
using HeadLoss.Core.Data;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Tests
{
    /// <summary>Shared data and helpers for the Game rule tests.</summary>
    static class GameTestUtil
    {
        static DataRepository? _data;
        public static DataRepository Data => _data ??= DataRepository.Load(new FileDataSource(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "HeadLoss", "Data")));

        /// <summary>Tiles from the first point through the corners (orthogonal legs).</summary>
        public static List<TilePos> Run(params (int x, int y)[] points)
        {
            var path = new List<TilePos> { new TilePos(points[0].x, points[0].y) };
            for (int i = 1; i < points.Length; i++)
            {
                TilePos cur = path[path.Count - 1];
                var target = new TilePos(points[i].x, points[i].y);
                (int dx, int dy) = cur.DirectionTo(target);
                while (cur != target)
                {
                    cur = new TilePos(cur.X + dx, cur.Y + dy);
                    path.Add(cur);
                }
            }
            return path;
        }

        public static BuildingSession Open(string id) => BuildingSession.OpenHandmade(Data, id);

        /// <summary>The test house with a small axial fan instead of the crank (the Phase 1 physics setup).</summary>
        public static BuildingSession TestHouseWithFan()
        {
            BuildingSession s = Open("test-house");
            s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan"));
            return s;
        }

        public static bool Draw(BuildingSession s, List<TilePos> path, double d = 125, bool smooth = false) =>
            s.Execute(new PlaceRunCommand(path, d, "galvanized-duct", smooth));

        /// <summary>Draws a run and commits the design with plenty of money and no budget cap, then solves.</summary>
        public static void DrawAndCommit(BuildingSession s, List<TilePos> path, double d = 125)
        {
            Draw(s, path, d);
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            if (!s.Commit()) throw new System.InvalidOperationException("commit failed: " + s.CheckCommit().Reason);
            s.SolveNow();
        }
    }
}
