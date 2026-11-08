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
        /// <summary>A device's port where the network feeds it (water heater cold in, boiler gas, a loop's return); AnchorId = device key, PartId = role.</summary>
        DeviceIn,
        /// <summary>A device's port where it feeds the network (water heater hot out, boiler flow); AnchorId = device key, PartId = role.</summary>
        DeviceOut,
    }

    /// <summary>A node of the player's duct layout (one per tile at most).</summary>
    public sealed class DuctNode
    {
        public int Id;
        public int X;
        public int Y;
        /// <summary>Floor index.</summary>
        public int Floor;
        public DuctNodeKind Kind;
        /// <summary>Entry opening id (Intake) or outlet id (Outlet).</summary>
        public string AnchorId = "";
        /// <summary>Long-radius elbow at this node instead of a sharp one.</summary>
        public bool SmoothFittings;
        /// <summary>Inline nodes: fitting id (damper, valve, regulator, relief valve) or equipment id (inline fan or pump, pressure tank).</summary>
        public string PartId = "";
        /// <summary>Inline nodes: unit direction along the run; fans blow this way.</summary>
        public int DirX;
        public int DirY;
        /// <summary>Dampers: opening θ (minOpening–1).</summary>
        public double Opening = 1;
        /// <summary>Inline fans and pumps: switched on. Shutoff valves: open.</summary>
        public bool On = true;
        /// <summary>Regulators: downstream setpoint; relief valves: opening pressure. Pa gauge (0 = the fitting's default).</summary>
        public double Setting;
        /// <summary>Inline fans: a variable-speed drive is fitted.</summary>
        public bool Vsd;
        /// <summary>Inline fans with a VSD: speed fraction (0.2–1).</summary>
        public double Speed = 1;
        /// <summary>Inline electrical parts: the spark-free variant (not an ignition source).</summary>
        public bool SparkFree;
        /// <summary>A gas manifold: the branches whose valve is closed, by the direction they leave in (e, w, n, s, up, down).</summary>
        public List<string> ClosedBranches = new List<string>();
        public TilePos Pos => new TilePos(X, Y, Floor);
        public DuctNode Clone()
        {
            var c = (DuctNode)MemberwiseClone();
            c.ClosedBranches = new List<string>(ClosedBranches);
            return c;
        }
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
        /// <summary>Runs in the wall cavity (tier 4, <c>04</c>): small pipes along walls, outside the ceiling's capacity (D170).</summary>
        public bool Cavity;

        public int Other(int node) => node == NodeA ? NodeB : NodeA;
        public DuctSegment Clone() => (DuctSegment)MemberwiseClone();

        /// <summary>A new segment between two nodes with this one's size, material and upgrades.</summary>
        public DuctSegment Like(int id, int nodeA, int nodeB) => new DuctSegment
        {
            Id = id, NodeA = nodeA, NodeB = nodeB, DiameterMm = DiameterMm, MaterialId = MaterialId, Sealing = Sealing, Insulation = Insulation, Cavity = Cavity,
        };

        /// <summary>Same size, material and upgrades (two such segments in a straight line merge into one).</summary>
        public bool SameSpec(DuctSegment o) => DiameterMm == o.DiameterMm && MaterialId == o.MaterialId && Sealing == o.Sealing && Insulation == o.Insulation && Cavity == o.Cavity;
    }

    /// <summary>The player's layout for one network (one fluid system, <c>networks.json</c>) in one building.</summary>
    public sealed class DuctNetworkState
    {
        /// <summary>
        /// The network id (<c>networks.json</c>). It equals the fluid id for every network except the air exhaust, so code that
        /// names a network by its fluid ("water", "natural-gas") keeps working; the physics reads the fluid through the data.
        /// </summary>
        public string FluidId = "air";
        public List<DuctNode> Nodes = new List<DuctNode>();
        public List<DuctSegment> Segments = new List<DuctSegment>();
        public int NextNodeId = 1;
        public int NextSegmentId = 1;

        public DuctNode? FindNode(int id) => Nodes.Find(n => n.Id == id);
        public DuctSegment? FindSegment(int id) => Segments.Find(s => s.Id == id);
        public DuctNode? NodeAt(TilePos t) => Nodes.Find(n => n.X == t.X && n.Y == t.Y && n.Floor == t.F);

        public int Degree(int nodeId)
        {
            int d = 0;
            foreach (DuctSegment s in Segments) if (s.NodeA == nodeId || s.NodeB == nodeId) d++;
            return d;
        }

        /// <summary>Nothing drawn and nothing anchored.</summary>
        public bool IsEmpty => Nodes.Count == 0 && Segments.Count == 0;

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
        /// <summary>The spark-free variant: not an ignition source (<c>07-equipment.md</c> §Safety gear).</summary>
        public bool SparkFree;
        /// <summary>Gas regulator outlet pressure set by the player, Pa (0 = its default).</summary>
        public double SetpointPa;

        public EquipmentState Clone() => (EquipmentState)MemberwiseClone();
    }

    /// <summary>Safety gear placed in a room or on an exterior wall: gas detectors and passive vents.</summary>
    public sealed class SafetyDevice
    {
        /// <summary>Equipment id (gas-detector, passive-vent-opening).</summary>
        public string EquipmentId = "";
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Passive vents: the outside wall it opens through (north, south, east, west).</summary>
        public string Facing = "";
        /// <summary>Passive vents: high in the wall (feeds the upper layer) or low (the lower layer).</summary>
        public bool High = true;

        public TilePos Pos => new TilePos(X, Y, Floor);
        public SafetyDevice Clone() => (SafetyDevice)MemberwiseClone();
    }

    /// <summary>
    /// One version of a building's player edits (serialisable; the layout is regenerated). A building keeps two:
    /// the committed one that earns and the design draft (<see cref="BuildingRecord"/>).
    /// </summary>
    public sealed class BuildingState
    {
        public string BuildingId = "";
        /// <summary>
        /// Every network the player has drawn on or that has anchors (one per network id, <c>networks.json</c>). A network that
        /// is asked for and missing is added empty, so the list only holds what a building uses.
        /// </summary>
        public List<DuctNetworkState> Networks = new List<DuctNetworkState>();
        public List<EquipmentState> Equipment = new List<EquipmentState>();
        /// <summary>Gas detectors and passive vents.</summary>
        public List<SafetyDevice> Safety = new List<SafetyDevice>();
        /// <summary>Devices on room floors that join networks (water heaters, boilers, …).</summary>
        public List<PlacedDevice> Devices = new List<PlacedDevice>();

        /// <summary>Fluid id of the gas network.</summary>
        public const string GasFluid = "natural-gas";

        /// <summary>The air supply network.</summary>
        public DuctNetworkState Air => Net("air");
        /// <summary>Cold water network (tier 2+).</summary>
        public DuctNetworkState Water => Net("water");
        /// <summary>Natural gas network (tier 3+).</summary>
        public DuctNetworkState Gas => Net(GasFluid);

        /// <summary>The network of an entry opening's utility before data is at hand (layouts built without data, old callers); "" for power.</summary>
        public static string UtilityFluid(string utility) => utility switch
        {
            "air" => "air",
            "water" => "water",
            "gas" => GasFluid,
            "heating" => "heating-water",
            "exhaust" => "air-exhaust",
            "power" => "",
            "" => "",
            _ => utility,
        };

        public EquipmentState? EquipmentAt(string openingId) => Equipment.Find(e => e.OpeningId == openingId);

        /// <summary>The network with this id (added empty when the building has none yet).</summary>
        public DuctNetworkState Net(string networkId)
        {
            if (networkId.Length == 0) throw new System.ArgumentException("No network id");
            foreach (DuctNetworkState n in Networks) if (n.FluidId == networkId) return n;
            var created = new DuctNetworkState { FluidId = networkId };
            // A fixed order (air, water, gas, then by id) whatever order the networks were added in, so anything that walks
            // the networks (failure rolls, fingerprints, saves) does it the same way every time.
            int at = 0;
            while (at < Networks.Count && Before(Networks[at].FluidId, networkId)) at++;
            Networks.Insert(at, created);
            return created;
        }

        static int Rank(string id) => id == "air" ? 0 : id == "water" ? 1 : id == GasFluid ? 2 : 3;

        static bool Before(string a, string b)
        {
            int ra = Rank(a), rb = Rank(b);
            return ra != rb ? ra < rb : string.CompareOrdinal(a, b) < 0;
        }

        /// <summary>Replaces the network with the same id (or adds it).</summary>
        public void SetNet(DuctNetworkState net)
        {
            DuctNetworkState old = Net(net.FluidId);
            Networks[Networks.IndexOf(old)] = net;
        }

        /// <summary>The network with this id, or null when the building has none (never adds one).</summary>
        public DuctNetworkState? FindNet(string networkId)
        {
            foreach (DuctNetworkState n in Networks) if (n.FluidId == networkId) return n;
            return null;
        }

        public BuildingState Clone()
        {
            var c = new BuildingState { BuildingId = BuildingId };
            foreach (DuctNetworkState n in Networks) c.Networks.Add(n.Clone());
            foreach (EquipmentState e in Equipment) c.Equipment.Add(e.Clone());
            foreach (SafetyDevice d in Safety) c.Safety.Add(d.Clone());
            foreach (PlacedDevice d in Devices) c.Devices.Add(d.Clone());
            return c;
        }

        /// <summary>
        /// A description of the physical layout and settings that ignores ids, so two states that look and run
        /// the same compare equal even when they were reached by different edits.
        /// </summary>
        public string Fingerprint()
        {
            var parts = new List<string>();
            foreach (DuctNetworkState net in Networks)
            {
                string w = net.FluidId == "air" ? "" : net.FluidId + ":";
                foreach (DuctSegment s in net.Segments)
                {
                    TilePos a = net.FindNode(s.NodeA)!.Pos, b = net.FindNode(s.NodeB)!.Pos;
                    if (a.F > b.F || (a.F == b.F && (a.X > b.X || (a.X == b.X && a.Y > b.Y)))) (a, b) = (b, a);
                    string fa = a.F == 0 && b.F == 0 ? "" : $"f{a.F},{b.F},";
                    parts.Add($"{w}s{fa}{a.X},{a.Y},{b.X},{b.Y},{s.DiameterMm},{s.MaterialId},{s.Sealing},{s.Insulation}{(s.Cavity ? ",cavity" : "")}");
                }
                foreach (DuctNode n in net.Nodes)
                {
                    if (n.Kind == DuctNodeKind.Free && !n.SmoothFittings && n.PartId.Length == 0) continue;
                    string f = n.Floor == 0 ? "" : $"f{n.Floor},";
                    string set = n.Setting == 0 ? "" : $",{n.Setting}";
                    string spark = n.SparkFree ? ",spark" : "";
                    string closed = n.ClosedBranches.Count == 0 ? "" : ",closed:" + string.Join("/", n.ClosedBranches);
                    parts.Add($"{w}n{f}{n.X},{n.Y},{(int)n.Kind},{n.AnchorId},{n.SmoothFittings},{n.PartId},{n.DirX},{n.DirY},{n.Opening},{n.On},{n.Vsd},{n.Speed}{set}{spark}{closed}");
                }
            }
            foreach (EquipmentState e in Equipment) parts.Add($"e{e.OpeningId},{e.EquipmentId},{e.On},{e.Speed},{e.Vsd}{(e.SparkFree ? ",spark" : "")}{(e.SetpointPa > 0 ? $",set{e.SetpointPa}" : "")}");
            foreach (SafetyDevice d in Safety) parts.Add($"d{d.EquipmentId},{d.X},{d.Y},{d.Floor},{d.Facing},{d.High}");
            foreach (PlacedDevice d in Devices) parts.Add($"x{d.Key},{d.Facing},{d.On},{d.SetpointC}");
            parts.Sort(System.StringComparer.Ordinal);
            return string.Join(";", parts);
        }

        /// <summary>
        /// The network of an outlet type before data is at hand (layouts built without data). Layouts built from data use
        /// <see cref="BuildingLayout.NetworkOf(HeadLoss.Core.Data.Defs.OutletPlacementDef)"/>, which reads <c>outlets.json</c>.
        /// </summary>
        public static string OutletFluid(string outletType) =>
            outletType == "hot-water-tap" ? "hot-water" : outletType.Contains("water-tap") ? "water" : outletType == "radiator" ? "heating-water"
            : outletType.StartsWith("gas-", System.StringComparison.Ordinal) ? GasFluid : "air";

        /// <summary>A fresh state for a layout: anchor nodes at openings and outlets, initial equipment.</summary>
        public static BuildingState CreateFor(BuildingLayout layout)
        {
            var s = new BuildingState { BuildingId = layout.Id };
            foreach (var o in layout.EntryOpenings)
            {
                string fluid = layout.NetworkOf(o);
                if (fluid.Length == 0) continue;
                DuctNetworkState net = s.Net(fluid);
                net.Nodes.Add(new DuctNode { Id = net.NextNodeId++, X = o.X, Y = o.Y, Floor = o.Floor, Kind = DuctNodeKind.Intake, AnchorId = o.Id });
            }
            foreach (var o in layout.Outlets)
            {
                DuctNetworkState net = s.Net(layout.NetworkOf(o));
                net.Nodes.Add(new DuctNode { Id = net.NextNodeId++, X = o.X, Y = o.Y, Floor = o.Floor, Kind = DuctNodeKind.Outlet, AnchorId = o.Id });
            }
            foreach (var e in layout.InitialEquipment)
                s.Equipment.Add(new EquipmentState { EquipmentId = e.EquipmentId, OpeningId = e.OpeningId, On = e.On });
            return s;
        }
    }
}
