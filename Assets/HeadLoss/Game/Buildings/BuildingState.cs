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

        public int Other(int node) => node == NodeA ? NodeB : NodeA;
        public DuctSegment Clone() => (DuctSegment)MemberwiseClone();
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
        /// <summary>Speed fraction when on (1 for on/off devices).</summary>
        public double Speed = 1;

        public EquipmentState Clone() => (EquipmentState)MemberwiseClone();
    }

    /// <summary>Player edits and dynamic state of one building (serialisable; the layout is regenerated).</summary>
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
