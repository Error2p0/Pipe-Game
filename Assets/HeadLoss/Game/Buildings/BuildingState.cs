using System.Collections.Generic;

namespace HeadLoss.Game.Buildings
{
    public enum DuctNodeKind
    {
        /// <summary>A corner, tee, reducer or dead end created by drawing.</summary>
        Free,
        /// <summary>Anchored at an entry opening (network start).</summary>
        Intake,
        /// <summary>Anchored at an outlet.</summary>
        Outlet,
        /// <summary>An inline part (damper or inline fan) on a straight run, between exactly two segments.</summary>
        Inline,
    }

    /// <summary>A node of the player's duct layout (one per tile at most).</summary>
    public sealed class DuctNode
    {
        public int Id;
        public int X;
        public int Y;
        public DuctNodeKind Kind;
        /// <summary>Entry opening id (Intake) or outlet id (Outlet).</summary>
        public string AnchorId = "";
        /// <summary>Long-radius elbow at this node instead of a sharp one.</summary>
        public bool SmoothFittings;
        /// <summary>Inline nodes: fitting id (damper) or equipment id (inline fan).</summary>
        public string PartId = "";
        /// <summary>Inline nodes: unit direction along the run; fans blow this way.</summary>
        public int DirX;
        public int DirY;
        /// <summary>Dampers: opening θ (minOpening–1).</summary>
        public double Opening = 1;
        /// <summary>Inline fans: switched on.</summary>
        public bool On = true;
        /// <summary>Inline fans: a variable-speed drive is fitted.</summary>
        public bool Vsd;
        /// <summary>Inline fans with a VSD: speed fraction (0.2–1).</summary>
        public double Speed = 1;

        public TilePos Pos => new TilePos(X, Y);
        public DuctNode Clone() => (DuctNode)MemberwiseClone();
    }

    /// <summary>A straight duct run between two nodes.</summary>
    public sealed class DuctSegment
    {
        public int Id;
        public int NodeA;
        public int NodeB;
        /// <summary>Nominal diameter, mm.</summary>
        public double DiameterMm;
        public string MaterialId = "";
        /// <summary>Sealing class id from <c>upgrades.json</c> ("" = unsealed).</summary>
        public string Sealing = "";
        /// <summary>Insulation id from <c>upgrades.json</c> ("" = none).</summary>
        public string Insulation = "";

        public int Other(int node) => node == NodeA ? NodeB : NodeA;
        public DuctSegment Clone() => (DuctSegment)MemberwiseClone();

        /// <summary>A new segment between two nodes with this one's size, material and upgrades.</summary>
        public DuctSegment Like(int id, int nodeA, int nodeB) => new DuctSegment
        {
            Id = id, NodeA = nodeA, NodeB = nodeB, DiameterMm = DiameterMm, MaterialId = MaterialId, Sealing = Sealing, Insulation = Insulation,
        };

        /// <summary>Same size, material and upgrades (two such segments in a straight line merge into one).</summary>
        public bool SameSpec(DuctSegment o) => DiameterMm == o.DiameterMm && MaterialId == o.MaterialId && Sealing == o.Sealing && Insulation == o.Insulation;
    }

    /// <summary>The player's layout for one network (one fluid) in one building.</summary>
    public sealed class DuctNetworkState
    {
        public string FluidId = "air";
        public List<DuctNode> Nodes = new List<DuctNode>();
        public List<DuctSegment> Segments = new List<DuctSegment>();
        public int NextNodeId = 1;
        public int NextSegmentId = 1;

        public DuctNode? FindNode(int id) => Nodes.Find(n => n.Id == id);
        public DuctSegment? FindSegment(int id) => Segments.Find(s => s.Id == id);
        public DuctNode? NodeAt(TilePos t) => Nodes.Find(n => n.X == t.X && n.Y == t.Y);

        public int Degree(int nodeId)
        {
            int d = 0;
            foreach (DuctSegment s in Segments) if (s.NodeA == nodeId || s.NodeB == nodeId) d++;
            return d;
        }

        public DuctNetworkState Clone()
        {
            var c = new DuctNetworkState { FluidId = FluidId, NextNodeId = NextNodeId, NextSegmentId = NextSegmentId };
            foreach (DuctNode n in Nodes) c.Nodes.Add(n.Clone());
            foreach (DuctSegment s in Segments) c.Segments.Add(s.Clone());
            return c;
        }
    }

    /// <summary>Source equipment placed at an entry opening.</summary>
    public sealed class EquipmentState
    {
        public string EquipmentId = "";
        public string OpeningId = "";
        public bool On = true;
        /// <summary>Speed fraction when on (used with a VSD or built-in variable speed; 0.2–1).</summary>
        public double Speed = 1;
        /// <summary>A variable-speed drive is fitted (on/off fans only).</summary>
        public bool Vsd;

        public EquipmentState Clone() => (EquipmentState)MemberwiseClone();
    }

    /// <summary>
    /// One version of a building's player edits (serialisable; the layout is regenerated). A building keeps two:
    /// the committed one that earns and the design draft (<see cref="BuildingRecord"/>).
    /// </summary>
    public sealed class BuildingState
    {
        public string BuildingId = "";
        public DuctNetworkState Air = new DuctNetworkState();
        public List<EquipmentState> Equipment = new List<EquipmentState>();

        public EquipmentState? EquipmentAt(string openingId) => Equipment.Find(e => e.OpeningId == openingId);

        public BuildingState Clone()
        {
            var c = new BuildingState { BuildingId = BuildingId, Air = Air.Clone() };
            foreach (EquipmentState e in Equipment) c.Equipment.Add(e.Clone());
            return c;
        }

        /// <summary>
        /// A description of the physical layout and settings that ignores ids, so two states that look and run
        /// the same compare equal even when they were reached by different edits.
        /// </summary>
        public string Fingerprint()
        {
            var parts = new List<string>();
            foreach (DuctSegment s in Air.Segments)
            {
                TilePos a = Air.FindNode(s.NodeA)!.Pos, b = Air.FindNode(s.NodeB)!.Pos;
                if (a.X > b.X || (a.X == b.X && a.Y > b.Y)) (a, b) = (b, a);
                parts.Add($"s{a.X},{a.Y},{b.X},{b.Y},{s.DiameterMm},{s.MaterialId},{s.Sealing},{s.Insulation}");
            }
            foreach (DuctNode n in Air.Nodes)
            {
                if (n.Kind == DuctNodeKind.Free && !n.SmoothFittings) continue;
                parts.Add($"n{n.X},{n.Y},{(int)n.Kind},{n.AnchorId},{n.SmoothFittings},{n.PartId},{n.DirX},{n.DirY},{n.Opening},{n.On},{n.Vsd},{n.Speed}");
            }
            foreach (EquipmentState e in Equipment) parts.Add($"e{e.OpeningId},{e.EquipmentId},{e.On},{e.Speed},{e.Vsd}");
            parts.Sort(System.StringComparer.Ordinal);
            return string.Join(";", parts);
        }

        /// <summary>A fresh state for a layout: anchor nodes at openings and outlets, initial equipment.</summary>
        public static BuildingState CreateFor(BuildingLayout layout)
        {
            var s = new BuildingState { BuildingId = layout.Id };
            foreach (var o in layout.EntryOpenings)
            {
                if (o.Utility != "air") continue;
                s.Air.Nodes.Add(new DuctNode { Id = s.Air.NextNodeId++, X = o.X, Y = o.Y, Kind = DuctNodeKind.Intake, AnchorId = o.Id });
            }
            foreach (var o in layout.Outlets)
                s.Air.Nodes.Add(new DuctNode { Id = s.Air.NextNodeId++, X = o.X, Y = o.Y, Kind = DuctNodeKind.Outlet, AnchorId = o.Id });
            foreach (var e in layout.InitialEquipment)
                s.Equipment.Add(new EquipmentState { EquipmentId = e.EquipmentId, OpeningId = e.OpeningId, On = e.On });
            return s;
        }
    }
}
