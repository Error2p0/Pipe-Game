using System;
using System.Collections.Generic;
using HeadLoss.Sim.Devices;

namespace HeadLoss.Sim.Network
{
    /// <summary>Node kinds (<c>05-simulation.md</c> §1.1).</summary>
    public enum NodeKind
    {
        /// <summary>Unknown pressure, zero demand (tees, bends, reducers, equipment ports).</summary>
        Junction,
        /// <summary>Known pressure (air intake, room behind a vent, city connection).</summary>
        FixedPressure,
        /// <summary>Unknown pressure with a fixed flow out of the network (taps, gas outlets).</summary>
        Demand,
    }

    /// <summary>Link kinds (<c>05-simulation.md</c> §1.1).</summary>
    public enum LinkKind
    {
        /// <summary>Pipe or duct segment including the fittings at its ends.</summary>
        Pipe,
        /// <summary>Air outlet grille discharging to a fixed-pressure room node.</summary>
        Vent,
        /// <summary>Fan or pump: adds pressure from its curve.</summary>
        Fan,
        /// <summary>Damper or valve: K(θ) = K_open/θ²; closed = removed from the solve.</summary>
        Valve,
    }

    public sealed class NetworkNode
    {
        public NodeKind Kind;
        /// <summary>Gauge pressure for <see cref="NodeKind.FixedPressure"/> nodes, Pa.</summary>
        public double FixedPressure;
        /// <summary>Flow out of the network at <see cref="NodeKind.Demand"/> nodes, m³/s (negative = injection).</summary>
        public double Demand;
        /// <summary>Elevation, m.</summary>
        public double Elevation;
        public string Label = "";
    }

    public sealed class NetworkLink
    {
        public LinkKind Kind;
        /// <summary>Start node index (positive flow runs From → To).</summary>
        public int From;
        public int To;
        /// <summary>Length, m (pipes).</summary>
        public double Length;
        /// <summary>Hydraulic diameter, m (pipes, valves; fans: connection diameter).</summary>
        public double Diameter;
        /// <summary>Flow area, m² (vents: grille face area; fans: connection area).</summary>
        public double Area;
        /// <summary>Absolute roughness, m.</summary>
        public double Roughness;
        /// <summary>Pipes: ΣK of fittings; vents: grille K; valves: K fully open.</summary>
        public double SumK;
        /// <summary>Valve opening θ ∈ (0, 1].</summary>
        public double Opening = 1;
        /// <summary>Closed valves are removed from the solve.</summary>
        public bool Closed;
        public FanCurve? Fan;
        /// <summary>Fan speed fraction; 0 = off (treated as a closed valve).</summary>
        public double Speed = 1;
        /// <summary>Fluid temperature in the link, K.</summary>
        public double Temperature = 293.15;
        public string Label = "";

        /// <summary>True when the link takes part in the solve.</summary>
        public bool IsOpen => !Closed && !(Kind == LinkKind.Fan && Speed <= 0);
    }

    /// <summary>One connected network of one fluid (graph of nodes and links).</summary>
    public sealed class NetworkGraph
    {
        public readonly List<NetworkNode> Nodes = new List<NetworkNode>();
        public readonly List<NetworkLink> Links = new List<NetworkLink>();

        public int AddNode(NodeKind kind, double elevation = 0, double fixedPressure = 0, double demand = 0, string label = "")
        {
            Nodes.Add(new NetworkNode { Kind = kind, Elevation = elevation, FixedPressure = fixedPressure, Demand = demand, Label = label });
            return Nodes.Count - 1;
        }

        public int AddJunction(double elevation = 0, string label = "") => AddNode(NodeKind.Junction, elevation, label: label);

        /// <param name="pressure">Gauge pressure, Pa.</param>
        public int AddFixed(double pressure, double elevation = 0, string label = "") => AddNode(NodeKind.FixedPressure, elevation, pressure, label: label);

        /// <param name="demand">Flow out of the network, m³/s.</param>
        public int AddDemand(double demand, double elevation = 0, string label = "") => AddNode(NodeKind.Demand, elevation, demand: demand, label: label);

        /// <summary>Adds a round pipe/duct segment.</summary>
        /// <param name="length">m.</param>
        /// <param name="diameter">Internal diameter, m.</param>
        /// <param name="roughness">Absolute roughness, m.</param>
        /// <param name="sumK">Fittings ΣK referenced to this segment.</param>
        public int AddPipe(int from, int to, double length, double diameter, double roughness, double sumK = 0, string label = "")
        {
            Links.Add(new NetworkLink
            {
                Kind = LinkKind.Pipe, From = from, To = to, Length = length, Diameter = diameter,
                Area = Math.PI * diameter * diameter / 4.0, Roughness = roughness, SumK = sumK, Label = label,
            });
            return Links.Count - 1;
        }

        /// <summary>Adds a vent grille.</summary>
        /// <param name="grilleK">K referenced to the grille face.</param>
        /// <param name="faceArea">Grille face area, m².</param>
        public int AddVent(int from, int to, double grilleK, double faceArea, string label = "")
        {
            Links.Add(new NetworkLink
            {
                Kind = LinkKind.Vent, From = from, To = to, SumK = grilleK, Area = faceArea,
                Diameter = Math.Sqrt(4 * faceArea / Math.PI), Label = label,
            });
            return Links.Count - 1;
        }

        /// <summary>Adds a fan (inlet = from, outlet = to).</summary>
        /// <param name="connectionDiameter">m.</param>
        public int AddFan(int from, int to, FanCurve curve, double connectionDiameter, double speed = 1, string label = "")
        {
            Links.Add(new NetworkLink
            {
                Kind = LinkKind.Fan, From = from, To = to, Fan = curve, Speed = speed, Diameter = connectionDiameter,
                Area = Math.PI * connectionDiameter * connectionDiameter / 4.0, Label = label,
            });
            return Links.Count - 1;
        }

        /// <summary>Adds a damper/valve.</summary>
        /// <param name="kOpen">K at 100% open.</param>
        /// <param name="diameter">m.</param>
        public int AddValve(int from, int to, double kOpen, double diameter, double opening = 1, string label = "")
        {
            Links.Add(new NetworkLink
            {
                Kind = LinkKind.Valve, From = from, To = to, SumK = kOpen, Opening = opening, Diameter = diameter,
                Area = Math.PI * diameter * diameter / 4.0, Label = label,
            });
            return Links.Count - 1;
        }

        /// <summary>Hash of the solve topology (endpoints, kinds, open state) for warm-start reuse.</summary>
        public long TopologyKey()
        {
            unchecked
            {
                long h = 1469598103934665603L;
                h = (h ^ Nodes.Count) * 1099511628211L;
                foreach (NetworkNode n in Nodes) h = (h ^ (int)n.Kind) * 1099511628211L;
                foreach (NetworkLink l in Links)
                {
                    h = (h ^ l.From) * 1099511628211L;
                    h = (h ^ l.To) * 1099511628211L;
                    h = (h ^ (int)l.Kind) * 1099511628211L;
                    h = (h ^ (l.IsOpen ? 1 : 0)) * 1099511628211L;
                }
                return h;
            }
        }
    }
}
