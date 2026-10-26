using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Sim.Devices;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Networks
{
    public enum FittingKind { Entry, Elbow, TeeRun, TeeBranch, Reducer }

    /// <summary>An auto-placed fitting and the link whose velocity its K is referenced to.</summary>
    public sealed class CompiledFitting
    {
        public int NodeId;
        public FittingKind Kind;
        public string FittingId = "";
        public double K;
        /// <summary>Graph link index the K is added to.</summary>
        public int LinkIndex;
    }

    /// <summary>A building network ready to solve, plus the maps back to the player's layout.</summary>
    public sealed class CompiledNetwork
    {
        public NetworkGraph Graph = new NetworkGraph();
        /// <summary>Duct segment id → graph link index.</summary>
        public Dictionary<int, int> SegmentLink = new Dictionary<int, int>();
        /// <summary>Duct node id → graph node index.</summary>
        public Dictionary<int, int> NodeIndex = new Dictionary<int, int>();
        /// <summary>Outlet id → vent link index.</summary>
        public Dictionary<string, int> OutletLink = new Dictionary<string, int>();
        /// <summary>Entry opening id → fan link index.</summary>
        public Dictionary<string, int> FanLink = new Dictionary<string, int>();
        /// <summary>Duct node id of an open end (or a bare intake) → discharge link index.</summary>
        public Dictionary<int, int> OpenEndLink = new Dictionary<int, int>();
        /// <summary>Inline node id (damper or inline fan) → valve or fan link index.</summary>
        public Dictionary<int, int> InlineLink = new Dictionary<int, int>();
        public List<CompiledFitting> Fittings = new List<CompiledFitting>();
    }

    /// <summary>
    /// Turns the player's duct layout into a solver graph (<c>05-simulation.md</c> §1.1): one Pipe link per
    /// segment, a Fan link from the outside at each intake with equipment, a Vent link to the room at each
    /// connected outlet, a Valve or Fan link across each inline part, and every auto fitting's K added to the
    /// segment whose velocity it is referenced to.
    /// </summary>
    public static class NetworkCompiler
    {
        /// <param name="airTemperatureK">Air temperature in the ducts, K.</param>
        /// <param name="crankSpeed">Hand-crank speed fraction (0–1) for crank equipment.</param>
        public static CompiledNetwork Compile(BuildingLayout layout, BuildingState state, DataRepository data, double airTemperatureK, double crankSpeed = 0)
        {
            DuctNetworkState net = state.Air;
            var c = new CompiledNetwork();
            NetworkGraph g = c.Graph;
            BalancingDef bal = data.Balancing;
            double z = bal.Building.CeilingLayerOffset;

            // Inline parts sit between two graph nodes: NodeIndex is the upstream ("in") side, inlineOut the other.
            var inlineOut = new Dictionary<int, int>();
            foreach (DuctNode n in net.Nodes)
            {
                c.NodeIndex[n.Id] = g.AddJunction(z, $"n{n.Id}");
                if (n.Kind == DuctNodeKind.Inline) inlineOut[n.Id] = g.AddJunction(z, $"n{n.Id}-out");
            }

            int Endpoint(DuctNode n, TilePos other)
            {
                if (n.Kind != DuctNodeKind.Inline) return c.NodeIndex[n.Id];
                int along = (other.X - n.X) * n.DirX + (other.Y - n.Y) * n.DirY;
                return along < 0 ? c.NodeIndex[n.Id] : inlineOut[n.Id];
            }

            // Segments → pipes.
            var segLink = new Dictionary<int, int>();
            foreach (DuctSegment s in net.Segments)
            {
                MaterialDef mat = data.Material(s.MaterialId);
                DuctNode na = net.FindNode(s.NodeA)!, nb = net.FindNode(s.NodeB)!;
                TilePos a = na.Pos, b = nb.Pos;
                int link = g.AddPipe(Endpoint(na, b), Endpoint(nb, a), a.ManhattanTo(b), Units.MmToM(s.DiameterMm),
                    Units.MmToM(mat.RoughnessMm), 0, $"s{s.Id}");
                g.Links[link].Temperature = airTemperatureK;
                segLink[s.Id] = link;
                c.SegmentLink[s.Id] = link;
            }

            // Intakes: outside (fixed 0 Pa) → fan → intake node. An intake with no equipment is an open louvre:
            // the intake node itself is at outside pressure, so an inline fan further on can draw through it.
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Intake) continue;
                EquipmentState? eq = state.EquipmentAt(n.AnchorId);
                if (eq == null)
                {
                    NetworkNode node = g.Nodes[c.NodeIndex[n.Id]];
                    node.Kind = NodeKind.FixedPressure;
                    node.FixedPressure = 0;
                    continue;
                }
                EquipmentDef def = data.EquipmentItem(eq.EquipmentId);
                int outside = g.AddFixed(0, z, $"outside-{n.AnchorId}");
                double speed = def.SpeedControl == "crank" ? crankSpeed : eq.On ? eq.Speed : 0;
                int fan = g.AddFan(outside, c.NodeIndex[n.Id], FanCurve.FromDef(def), Units.MmToM(def.ConnectionDiameterMm),
                    speed, eq.EquipmentId);
                g.Links[fan].Temperature = airTemperatureK;
                c.FanLink[n.AnchorId] = fan;
            }

            // Inline parts: a damper is a valve K(θ) = K_open/θ², an inline fan a fan link in its direction.
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || net.Degree(n.Id) != 2) continue;
                double d = double.MaxValue;
                foreach (DuctSegment s in net.Segments)
                    if (s.NodeA == n.Id || s.NodeB == n.Id) d = Math.Min(d, s.DiameterMm);
                int from = c.NodeIndex[n.Id], to = inlineOut[n.Id];
                int link;
                EquipmentDef? fanDef = data.FindEquipment(n.PartId);
                if (fanDef != null)
                {
                    link = g.AddFan(from, to, FanCurve.FromDef(fanDef), Units.MmToM(fanDef.ConnectionDiameterMm), n.On ? 1 : 0, $"{n.PartId}-n{n.Id}");
                }
                else
                {
                    FittingDef valve = data.Fitting(n.PartId);
                    double theta = Math.Max(valve.MinOpening, Math.Min(1, n.Opening));
                    link = g.AddValve(from, to, valve.K, Units.MmToM(d), theta, $"{n.PartId}-n{n.Id}");
                }
                g.Links[link].Temperature = airTemperatureK;
                c.InlineLink[n.Id] = link;
            }

            // Outlets: vent grille → room (fixed 0 Pa), only when a duct reaches the outlet.
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Outlet || net.Degree(n.Id) == 0) continue;
                OutletPlacementDef placement = layout.FindOutlet(n.AnchorId)!;
                OutletDef outlet = data.Outlet(placement.Type);
                FittingDef grille = data.Fitting(outlet.Grille);
                int room = g.AddFixed(0, z, $"room-{n.AnchorId}");
                int vent = g.AddVent(c.NodeIndex[n.Id], room, grille.K, outlet.GrilleFaceAreaM2, n.AnchorId);
                g.Links[vent].Temperature = airTemperatureK;
                c.OutletLink[n.AnchorId] = vent;
            }

            // Open ends: a free node with one duct, or an intake with equipment and no duct, discharges into the
            // room through the open-end exit loss (referenced to the duct or fan connection area).
            FittingDef openEnd = data.Fitting("open-end");
            foreach (DuctNode n in net.Nodes)
            {
                double area;
                int degree = net.Degree(n.Id);
                if (n.Kind == DuctNodeKind.Free && degree == 1)
                {
                    DuctSegment seg = net.Segments.Find(x => x.NodeA == n.Id || x.NodeB == n.Id)!;
                    double d = Units.MmToM(seg.DiameterMm);
                    area = Math.PI * d * d / 4;
                }
                else if (n.Kind == DuctNodeKind.Intake && degree == 0 && c.FanLink.TryGetValue(n.AnchorId, out int fanLink))
                {
                    area = g.Links[fanLink].Area;
                }
                else continue;
                int room = g.AddFixed(0, z, $"room-open-{n.Id}");
                int link = g.AddVent(c.NodeIndex[n.Id], room, openEnd.K, area, $"open-n{n.Id}");
                g.Links[link].Temperature = airTemperatureK;
                c.OpenEndLink[n.Id] = link;
            }

            AssignFittings(net, data, c, segLink);
            foreach (CompiledFitting f in c.Fittings) g.Links[f.LinkIndex].SumK += f.K;
            return c;
        }

        static void AssignFittings(DuctNetworkState net, DataRepository data, CompiledNetwork c, Dictionary<int, int> segLink)
        {
            // Flow direction for fitting K comes from hop distance to the intake (D9).
            Dictionary<int, int> dist = DuctEditor.HopsFromIntake(net);
            FittingDef entry = data.Fitting("intake-entry");
            FittingDef sharp = data.Fitting("elbow-90-sharp");
            FittingDef smooth = data.Fitting("elbow-90-long-radius");
            FittingDef tee = data.Fitting("tee-sharp");
            FittingDef cross = data.Fitting("cross-sharp");
            FittingDef reducer = data.Fitting("reducer");
            var segs = new List<DuctSegment>(4);

            foreach (DuctNode n in net.Nodes)
            {
                segs.Clear();
                foreach (DuctSegment s in net.Segments) if (s.NodeA == n.Id || s.NodeB == n.Id) segs.Add(s);
                if (segs.Count == 0) continue;

                if (n.Kind == DuctNodeKind.Intake)
                {
                    Add(c, n.Id, FittingKind.Entry, entry.Id, entry.K, segLink[segs[0].Id]);
                    continue;
                }
                if ((n.Kind != DuctNodeKind.Free && n.Kind != DuctNodeKind.Inline) || segs.Count < 2) continue;

                // Upstream = the segment toward the intake (smallest distance at its far end; ties by id).
                segs.Sort((p, q) =>
                {
                    int dp = DistOf(dist, p.Other(n.Id)), dq = DistOf(dist, q.Other(n.Id));
                    return dp != dq ? dp.CompareTo(dq) : p.Id.CompareTo(q.Id);
                });
                DuctSegment up = segs[0];
                TilePos pos = n.Pos;
                TilePos upPos = net.FindNode(up.Other(n.Id))!.Pos;

                if (n.Kind == DuctNodeKind.Inline)
                {
                    AddReducer(c, n.Id, reducer, up, segs[1], segLink);
                    continue;
                }
                if (segs.Count == 2)
                {
                    DuctSegment down = segs[1];
                    TilePos downPos = net.FindNode(down.Other(n.Id))!.Pos;
                    bool straight = upPos.X == downPos.X || upPos.Y == downPos.Y;
                    if (!straight)
                    {
                        FittingDef elbow = n.SmoothFittings ? smooth : sharp;
                        Add(c, n.Id, FittingKind.Elbow, elbow.Id, ElbowK(elbow, data, down), segLink[down.Id]);
                    }
                    AddReducer(c, n.Id, reducer, up, down, segLink);
                    continue;
                }

                // Tee (3 ducts) or cross (4 ducts): the run continues straight from upstream, branches turn 90°.
                FittingDef junction = segs.Count >= 4 ? cross : tee;
                for (int i = 1; i < segs.Count; i++)
                {
                    DuctSegment out_ = segs[i];
                    TilePos outPos = net.FindNode(out_.Other(n.Id))!.Pos;
                    bool run = (upPos.X == pos.X && outPos.X == pos.X) || (upPos.Y == pos.Y && outPos.Y == pos.Y);
                    Add(c, n.Id, run ? FittingKind.TeeRun : FittingKind.TeeBranch, junction.Id, run ? junction.KRun : junction.KBranch, segLink[out_.Id]);
                    AddReducer(c, n.Id, reducer, up, out_, segLink);
                }
            }
        }

        static int DistOf(Dictionary<int, int> dist, int node) => dist.TryGetValue(node, out int d) ? d : int.MaxValue;

        static double ElbowK(FittingDef elbow, DataRepository data, DuctSegment seg) =>
            data.Material(seg.MaterialId).Kind == "duct" ? elbow.KDuct : elbow.KPipe;

        /// <summary>Size change from upstream to downstream: K on the smaller segment (contraction or Borda expansion).</summary>
        static void AddReducer(CompiledNetwork c, int nodeId, FittingDef reducer, DuctSegment up, DuctSegment down, Dictionary<int, int> segLink)
        {
            if (up.DiameterMm == down.DiameterMm) return;
            if (down.DiameterMm < up.DiameterMm)
            {
                Add(c, nodeId, FittingKind.Reducer, reducer.Id, reducer.KContraction, segLink[down.Id]);
            }
            else
            {
                double ratio = (up.DiameterMm * up.DiameterMm) / (down.DiameterMm * down.DiameterMm);
                Add(c, nodeId, FittingKind.Reducer, reducer.Id, (1 - ratio) * (1 - ratio), segLink[up.Id]);
            }
        }

        static void Add(CompiledNetwork c, int nodeId, FittingKind kind, string id, double k, int link) =>
            c.Fittings.Add(new CompiledFitting { NodeId = nodeId, Kind = kind, FittingId = id, K = k, LinkIndex = link });
    }
}
