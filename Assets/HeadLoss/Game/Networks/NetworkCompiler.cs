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
        /// <summary>Fluid id (air, water).</summary>
        public string Fluid = "air";
        public NetworkGraph Graph = new NetworkGraph();
        /// <summary>Duct segment id → graph link index.</summary>
        public Dictionary<int, int> SegmentLink = new Dictionary<int, int>();
        /// <summary>Duct node id → graph node index.</summary>
        public Dictionary<int, int> NodeIndex = new Dictionary<int, int>();
        /// <summary>Outlet id → vent link index (air outlets: delivery is the link flow).</summary>
        public Dictionary<string, int> OutletLink = new Dictionary<string, int>();
        /// <summary>Outlet id → demand node index (taps: delivery is the node pressure at the design flow).</summary>
        public Dictionary<string, int> OutletNode = new Dictionary<string, int>();
        /// <summary>Entry opening id → fan or pump link index.</summary>
        public Dictionary<string, int> FanLink = new Dictionary<string, int>();
        /// <summary>Duct node id of an open end (or a bare intake) → discharge link index.</summary>
        public Dictionary<int, int> OpenEndLink = new Dictionary<int, int>();
        /// <summary>Inline node id (damper, valve, regulator, inline fan or pump) → its link index.</summary>
        public Dictionary<int, int> InlineLink = new Dictionary<int, int>();
        /// <summary>Inline relief valve node id → its discharge link index (to a drain).</summary>
        public Dictionary<int, int> ReliefLink = new Dictionary<int, int>();
        /// <summary>Duct segment id → its two leak links (half the duct surface at each end), into the room.</summary>
        public Dictionary<int, (int a, int b)> SegmentLeaks = new Dictionary<int, (int, int)>();
        /// <summary>Burst segment id → its two spill links (open ends at both of its nodes).</summary>
        public Dictionary<int, (int a, int b)> BurstLinks = new Dictionary<int, (int, int)>();
        /// <summary>Fan link index → efficiency multiplier of a fitted variable-speed drive.</summary>
        public Dictionary<int, double> DriveFactor = new Dictionary<int, double>();
        /// <summary>Gas leak key → its orifice link to the room.</summary>
        public Dictionary<string, int> LeakLinks = new Dictionary<string, int>();
        /// <summary>The city connection node (water, gas): flow out of it is the building's draw from the main. −1 when none.</summary>
        public int SupplyNode = -1;
        /// <summary>Outlets cut off from the supply by a closed shutoff (exempt from the minimum check, pay 0).</summary>
        public HashSet<string> IsolatedOutlets = new HashSet<string>();
        /// <summary>Segments (burst ones included) cut off from the supply by a closed shutoff.</summary>
        public HashSet<int> IsolatedSegments = new HashSet<int>();
        public List<CompiledFitting> Fittings = new List<CompiledFitting>();

        /// <summary>Total flow drawn from the supply node, m³/s.</summary>
        public double SupplyFlow(SolveResult r)
        {
            if (SupplyNode < 0) return 0;
            double q = 0;
            for (int k = 0; k < Graph.Links.Count; k++)
            {
                NetworkLink l = Graph.Links[k];
                if (l.From == SupplyNode) q += r.Flow[k];
                else if (l.To == SupplyNode) q -= r.Flow[k];
            }
            return q;
        }
    }

    /// <summary>Operating conditions a network is compiled under.</summary>
    public sealed class CompileOptions
    {
        /// <summary>Outside air temperature (at the intake, and in ducts without a thermal result), K.</summary>
        public double AirTemperatureK = 293.15;
        /// <summary>Hand-crank speed fraction (0–1) for crank equipment.</summary>
        public double CrankSpeed;
        /// <summary>Air temperature per duct segment from the thermal solve, K.</summary>
        public IReadOnlyDictionary<int, double>? SegmentTemperatureK;
        /// <summary>Air temperature per duct node from the thermal solve, K.</summary>
        public IReadOnlyDictionary<int, double>? NodeTemperatureK;
        /// <summary>Water: the building has a city water connection.</summary>
        public bool WaterConnected;
        /// <summary>Water: connection pressure from the city solve, Pa gauge at ground level.</summary>
        public double WaterPressurePa;
        /// <summary>Speed factor on every powered fan and pump: 0 when the panel has tripped, cbrt(available/draw) in a brownout.</summary>
        public double PowerSpeed = 1;
        /// <summary>Burst segments per fluid (segment ids): removed, with an open end at each of their nodes.</summary>
        public IReadOnlyDictionary<string, HashSet<int>>? BurstSegments;
        /// <summary>Gas: the building has a city gas connection.</summary>
        public bool GasConnected;
        /// <summary>Gas: connection pressure from the city solve, Pa gauge.</summary>
        public double GasPressurePa;
        /// <summary>Gas leaks to open: orifice links into the room (<c>05-simulation.md</c> §1.1).</summary>
        public IReadOnlyList<Failures.LeakState>? Leaks;
        /// <summary>Automatic shutoff valves closed now (by the alarm or on power loss), tiles.</summary>
        public HashSet<TilePos>? AutoClosed;
        /// <summary>Equipment knocked offline by a blast (see <see cref="Failures.FailureState.DamagedEquipment"/>).</summary>
        public HashSet<string>? Damaged;
        /// <summary>Switched-off fans and pumps still slowing down: share of speed left, 0–1, by damage key. Null = all stopped (steady state).</summary>
        public IReadOnlyDictionary<string, double>? RunDown;
        /// <summary>Share of speed every powered device has left after the panel tripped, 0–1 (1 = not tripping).</summary>
        public double PowerRunDown = 1;
    }

    /// <summary>
    /// Turns the player's layout into a solver graph (<c>05-simulation.md</c> §1.1): one Pipe link per segment (risers
    /// carry their height), a Fan link from the outside at each air intake with equipment, a Vent link to the room at
    /// each connected air outlet, a Demand node with its drop pipe at each connected tap, the city connection (a fixed
    /// pressure, through a pump when one is placed), a Valve / Check / Regulator / Fan link across each inline part,
    /// and every auto fitting's K added to the segment whose velocity it is referenced to.
    /// </summary>
    public static class NetworkCompiler
    {
        /// <summary>Compiles the air network (the Phase 1–3 signature).</summary>
        public static CompiledNetwork Compile(BuildingLayout layout, BuildingState state, DataRepository data, double airTemperatureK, double crankSpeed = 0,
            IReadOnlyDictionary<int, double>? segmentTemperatureK = null, IReadOnlyDictionary<int, double>? nodeTemperatureK = null) =>
            Compile(layout, state, data, "air", new CompileOptions
            {
                AirTemperatureK = airTemperatureK, CrankSpeed = crankSpeed, SegmentTemperatureK = segmentTemperatureK, NodeTemperatureK = nodeTemperatureK,
            });

        public static CompiledNetwork Compile(BuildingLayout layout, BuildingState state, DataRepository data, string fluid, CompileOptions o)
        {
            bool water = fluid != "air";
            bool gas = fluid == BuildingState.GasFluid;
            double tWater = Units.CToK(gas ? data.Balancing.Gas.SupplyTemperatureC : data.Balancing.Water.MainsTemperatureC);
            double SegT(int id) => water ? tWater : o.SegmentTemperatureK != null && o.SegmentTemperatureK.TryGetValue(id, out double t) ? t : o.AirTemperatureK;
            double NodeT(int id) => water ? tWater : o.NodeTemperatureK != null && o.NodeTemperatureK.TryGetValue(id, out double t) ? t : o.AirTemperatureK;
            DuctNetworkState net = state.Net(fluid);
            var c = new CompiledNetwork { Fluid = fluid };
            NetworkGraph g = c.Graph;
            BuildingBalancingDef bb = data.Balancing.Building;
            // Gauge pressure is referenced to the local atmosphere: a gas column's elevation term is its density minus the air's (D7).
            if (gas) g.AmbientDensity = IdealAir(data, o.AirTemperatureK);
            // Air nodes all sit at one elevation: the outside air column cancels the duct's (D7). Water nodes sit at
            // their floor's ceiling layer; taps and the connection at the floor layer.
            double Ceiling(int floor) => water ? floor * bb.FloorHeight + bb.CeilingLayerOffset : bb.CeilingLayerOffset;
            double FloorLayer(int floor) => water ? floor * bb.FloorHeight + bb.FloorLayerOffset : bb.CeilingLayerOffset;
            var burst = o.BurstSegments != null && o.BurstSegments.TryGetValue(fluid, out HashSet<int>? bs) ? bs : new HashSet<int>();

            var inlineOut = new Dictionary<int, int>();
            foreach (DuctNode n in net.Nodes)
            {
                c.NodeIndex[n.Id] = g.AddJunction(Ceiling(n.Floor), $"n{n.Id}");
                if (n.Kind == DuctNodeKind.Inline) inlineOut[n.Id] = g.AddJunction(Ceiling(n.Floor), $"n{n.Id}-out");
            }

            int Endpoint(DuctNode n, TilePos other)
            {
                if (n.Kind != DuctNodeKind.Inline) return c.NodeIndex[n.Id];
                int along = (other.X - n.X) * n.DirX + (other.Y - n.Y) * n.DirY;
                return along < 0 ? c.NodeIndex[n.Id] : inlineOut[n.Id];
            }

            // Segments → pipes (burst ones become two open ends instead).
            var segLink = new Dictionary<int, int>();
            foreach (DuctSegment s in net.Segments)
            {
                MaterialDef mat = data.Material(s.MaterialId);
                DuctNode na = net.FindNode(s.NodeA)!, nb = net.FindNode(s.NodeB)!;
                TilePos a = na.Pos, b = nb.Pos;
                double d = Units.MmToM(mat.InternalDiameterMm(s.DiameterMm));
                int from = Endpoint(na, b), to = Endpoint(nb, a);
                if (burst.Contains(s.Id))
                {
                    double area = Math.PI * d * d / 4;
                    double k = data.Balancing.Water.OpenEndK;
                    int sa = g.AddVent(from, g.AddFixed(0, g.Nodes[from].Elevation, $"spill-{s.Id}a"), k, area, $"burst-s{s.Id}a");
                    int sb = g.AddVent(to, g.AddFixed(0, g.Nodes[to].Elevation, $"spill-{s.Id}b"), k, area, $"burst-s{s.Id}b");
                    g.Links[sa].Temperature = g.Links[sb].Temperature = SegT(s.Id);
                    c.BurstLinks[s.Id] = (sa, sb);
                    continue;
                }
                int link = g.AddPipe(from, to, a.LengthTo(b, bb.FloorHeight), d, Units.MmToM(mat.RoughnessMm), 0, $"s{s.Id}");
                g.Links[link].Temperature = SegT(s.Id);
                segLink[s.Id] = link;
                c.SegmentLink[s.Id] = link;
            }

            DuctSegment? FirstSegment(DuctNode n) => net.Segments.Find(x => x.NodeA == n.Id || x.NodeB == n.Id);
            double InnerD(DuctSegment s) => Units.MmToM(data.Material(s.MaterialId).InternalDiameterMm(s.DiameterMm));
            double speedFactor = Math.Max(0, o.PowerSpeed);

            // Entry openings.
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Intake) continue;
                EquipmentState? eq = state.EquipmentAt(n.AnchorId);
                EquipmentDef? def = eq != null ? data.FindEquipment(eq.EquipmentId) : null;
                bool device = def != null && def.Curve.Count > 0;
                if (!water)
                {
                    // Outside (fixed 0 Pa) → fan → intake node. A bare intake is an open louvre: the intake node itself
                    // is at outside pressure, so an inline fan further on can draw through it.
                    if (!device)
                    {
                        NetworkNode node = g.Nodes[c.NodeIndex[n.Id]];
                        node.Kind = NodeKind.FixedPressure;
                        node.FixedPressure = 0;
                        continue;
                    }
                    int outside = g.AddFixed(0, Ceiling(n.Floor), $"outside-{n.AnchorId}");
                    double left = SpeedLeft(o, Failures.DamageKeys.Opening(n.AnchorId), eq!.On, def!);
                    int fan = g.AddFan(outside, c.NodeIndex[n.Id], FanCurve.FromDef(def!), Units.MmToM(def!.ConnectionDiameterMm),
                        DeviceSpeed(def, eq!, o.CrankSpeed, speedFactor, left), eq!.EquipmentId);
                    SetRunDown(g.Links[fan], left);
                    if (IsDamaged(o, Failures.DamageKeys.Opening(n.AnchorId))) g.Links[fan].Speed = 0;
                    g.Links[fan].Temperature = o.AirTemperatureK;
                    c.FanLink[n.AnchorId] = fan;
                    if (eq.Vsd) c.DriveFactor[fan] = DriveEfficiency(data, def.Category);
                    continue;
                }

                // Water and gas: city (fixed, connection pressure) → [pump or regulator] → connection → drop up to the ceiling pipe.
                if (!(gas ? o.GasConnected : o.WaterConnected)) continue;
                DuctSegment? first = FirstSegment(n);
                double z = FloorLayer(n.Floor);
                int city = g.AddFixed(gas ? o.GasPressurePa : o.WaterPressurePa, z, $"city-{n.AnchorId}");
                c.SupplyNode = city;
                int connection = city;
                bool damaged = IsDamaged(o, Failures.DamageKeys.Opening(n.AnchorId));
                if (gas && def != null && def.Category == "gas-supply" && first != null)
                {
                    // A damaged regulator lets no gas through.
                    connection = g.AddJunction(z, $"connection-{n.AnchorId}");
                    int reg = g.AddRegulator(city, connection, def.RegulatorK, InnerD(first), def.SetpointPa, def.Id);
                    g.Links[reg].Temperature = tWater;
                    if (damaged) g.Links[reg].Closed = true;
                }
                else if (device)
                {
                    connection = g.AddJunction(z, $"connection-{n.AnchorId}");
                    double left = SpeedLeft(o, Failures.DamageKeys.Opening(n.AnchorId), eq!.On, def!);
                    int pump = g.AddFan(city, connection, FanCurve.FromDef(def!), Units.MmToM(def!.ConnectionDiameterMm),
                        DeviceSpeed(def, eq!, 0, speedFactor, left), eq!.EquipmentId);
                    SetRunDown(g.Links[pump], left);
                    if (damaged) g.Links[pump].Speed = 0;
                    g.Links[pump].Temperature = tWater;
                    c.FanLink[n.AnchorId] = pump;
                    if (eq.Vsd) c.DriveFactor[pump] = DriveEfficiency(data, def.Category);
                }
                if (first == null) continue;
                MaterialDef m = data.Material(first.MaterialId);
                int drop = g.AddPipe(connection, c.NodeIndex[n.Id], bb.CeilingLayerOffset - bb.FloorLayerOffset, InnerD(first), Units.MmToM(m.RoughnessMm), 0, $"drop-{n.AnchorId}");
                g.Links[drop].Temperature = tWater;
            }

            // Inline parts.
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || net.Degree(n.Id) != 2) continue;
                double dMm = double.MaxValue, dIn = double.MaxValue;
                foreach (DuctSegment s in net.Segments)
                    if (s.NodeA == n.Id || s.NodeB == n.Id)
                    {
                        dMm = Math.Min(dMm, s.DiameterMm);
                        dIn = Math.Min(dIn, InnerD(s));
                    }
                int from = c.NodeIndex[n.Id], to = inlineOut[n.Id];
                int link;
                EquipmentDef? devDef = data.FindEquipment(n.PartId);
                if (devDef != null && devDef.Curve.Count > 0)
                {
                    bool variable = devDef.SpeedControl == "variable" || n.Vsd;
                    string key = Failures.DamageKeys.Inline(fluid, n.Pos);
                    double left = SpeedLeft(o, key, n.On, devDef);
                    double speed = (IsDamaged(o, key) ? 0 : left * (variable ? n.Speed : 1)) * speedFactor;
                    link = g.AddFan(from, to, FanCurve.FromDef(devDef), Units.MmToM(devDef.ConnectionDiameterMm), speed, $"{n.PartId}-n{n.Id}");
                    SetRunDown(g.Links[link], left);
                    if (n.Vsd) c.DriveFactor[link] = DriveEfficiency(data, devDef.Category);
                }
                else if (devDef != null)
                {
                    // A pressure tank is teed into the line: the flow passes the tee's run.
                    link = g.AddValve(from, to, data.Fitting("tee-sharp").KRun, dIn, 1, $"{n.PartId}-n{n.Id}");
                }
                else
                {
                    FittingDef f = data.Fitting(n.PartId);
                    double setting = n.Setting > 0 ? n.Setting : f.DefaultSetting;
                    switch (f.Model)
                    {
                        case "check":
                            link = g.AddCheck(from, to, f.K, dIn, $"{n.PartId}-n{n.Id}");
                            break;
                        case "regulator":
                            link = g.AddRegulator(from, to, f.K, dIn, setting, $"{n.PartId}-n{n.Id}");
                            break;
                        case "relief":
                        {
                            link = g.AddValve(from, to, data.Fitting("tee-sharp").KRun, dIn, 1, $"{n.PartId}-n{n.Id}");
                            int drain = g.AddFixed(0, g.Nodes[from].Elevation, $"drain-n{n.Id}");
                            int relief = g.AddRelief(from, drain, f.K, dIn, setting, $"relief-n{n.Id}");
                            g.Links[relief].Temperature = NodeT(n.Id);
                            c.ReliefLink[n.Id] = relief;
                            break;
                        }
                        default:
                        {
                            // Dampers, balancing valves (K(θ) = K_open/θ²), shutoffs and ball valves (open or closed), fire dampers.
                            double theta = f.Model == "valve" ? Math.Max(f.MinOpening, Math.Min(1, n.Opening)) : 1;
                            link = g.AddValve(from, to, f.K, f.Model == "valve" || water ? dIn : Units.MmToM(dMm), theta, $"{n.PartId}-n{n.Id}");
                            bool autoClosed = f.AutoShutoff && o.AutoClosed != null && o.AutoClosed.Contains(n.Pos);
                            if (f.Model == "valve" && f.MinOpening >= 1 && (!n.On || autoClosed)) g.Links[link].Closed = true;
                            break;
                        }
                    }
                }
                g.Links[link].Temperature = NodeT(n.Id);
                c.InlineLink[n.Id] = link;
            }

            if (!water)
            {
                // Air outlets: vent grille → room (fixed 0 Pa), only when a duct reaches the outlet.
                foreach (DuctNode n in net.Nodes)
                {
                    if (n.Kind != DuctNodeKind.Outlet || net.Degree(n.Id) == 0) continue;
                    OutletPlacementDef placement = layout.FindOutlet(n.AnchorId)!;
                    OutletDef outlet = data.Outlet(placement.Type);
                    FittingDef grille = data.Fitting(outlet.Grille);
                    int room = g.AddFixed(0, Ceiling(n.Floor), $"room-{n.AnchorId}");
                    int vent = g.AddVent(c.NodeIndex[n.Id], room, grille.K, outlet.GrilleFaceAreaM2, n.AnchorId);
                    g.Links[vent].Temperature = NodeT(n.Id);
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
                        double d = InnerD(FirstSegment(n)!);
                        area = Math.PI * d * d / 4;
                    }
                    else if (n.Kind == DuctNodeKind.Intake && degree == 0 && c.FanLink.TryGetValue(n.AnchorId, out int fanLink))
                    {
                        area = g.Links[fanLink].Area;
                    }
                    else continue;
                    int room = g.AddFixed(0, Ceiling(n.Floor), $"room-open-{n.Id}");
                    int link = g.AddVent(c.NodeIndex[n.Id], room, openEnd.K, area, $"open-n{n.Id}");
                    g.Links[link].Temperature = NodeT(n.Id);
                    c.OpenEndLink[n.Id] = link;
                }

                // Duct leakage (07 §Duct sealing): Q = C_L · A_surface · p^n, half the surface at each end into the room.
                double exponent = data.Upgrades.LeakageExponent;
                foreach (DuctSegment s in net.Segments)
                {
                    if (!segLink.TryGetValue(s.Id, out int link)) continue;
                    double d = Units.MmToM(s.DiameterMm);
                    double half = data.Sealing(s.Sealing).LeakageCoefficient * Math.PI * d * g.Links[link].Length * 0.5;
                    double z = g.Nodes[g.Links[link].From].Elevation;
                    int roomA = g.AddFixed(0, z, $"room-leak-{s.Id}a"), roomB = g.AddFixed(0, z, $"room-leak-{s.Id}b");
                    int la = g.AddLeak(g.Links[link].From, roomA, half, exponent, $"leak-s{s.Id}a");
                    int lb = g.AddLeak(g.Links[link].To, roomB, half, exponent, $"leak-s{s.Id}b");
                    g.Links[la].Temperature = NodeT(s.NodeA);
                    g.Links[lb].Temperature = NodeT(s.NodeB);
                    c.SegmentLeaks[s.Id] = (la, lb);
                }
            }
            else
            {
                // Taps: a drop pipe from the ceiling down to the tap, which draws its fixed design flow.
                foreach (DuctNode n in net.Nodes)
                {
                    if (n.Kind != DuctNodeKind.Outlet) continue;
                    DuctSegment? first = FirstSegment(n);
                    if (first == null) continue;
                    OutletPlacementDef placement = layout.FindOutlet(n.AnchorId)!;
                    double demand = DesignFlow(data, layout, placement);
                    int tap = g.AddDemand(demand, FloorLayer(n.Floor), n.AnchorId);
                    MaterialDef m = data.Material(first.MaterialId);
                    int drop = g.AddPipe(c.NodeIndex[n.Id], tap, bb.CeilingLayerOffset - bb.FloorLayerOffset, InnerD(first), Units.MmToM(m.RoughnessMm), 0, $"drop-{n.AnchorId}");
                    g.Links[drop].Temperature = tWater;
                    c.OutletNode[n.AnchorId] = tap;
                }
                // Dead ends on water pipes are capped: they carry no flow (D71).
                if (gas && o.Leaks != null)
                {
                    // A leak is an orifice to the room at its tile: Q = C_d·A·sqrt(2Δp/ρ), a power law with n = 0.5.
                    GasDef gd = data.Balancing.Gas;
                    double rhoGas = IdealGasDensity(data, fluid, tWater);
                    foreach (Failures.LeakState leak in o.Leaks)
                    {
                        int attach = LeakNode(net, leak.Pos, c);
                        if (attach < 0) continue;
                        double coefficient = gd.LeakDischargeCoefficient * leak.AreaM2 * Math.Sqrt(2 / rhoGas);
                        int room = g.AddFixed(0, g.Nodes[attach].Elevation, $"room-{leak.Key}");
                        int link = g.AddLeak(attach, room, coefficient, 0.5, leak.Key);
                        g.Links[link].Temperature = tWater;
                        c.LeakLinks[leak.Key] = link;
                    }
                }
            }

            AssignFittings(net, data, c, segLink, water);
            foreach (CompiledFitting f in c.Fittings) g.Links[f.LinkIndex].SumK += f.K;
            if (c.SupplyNode >= 0) MarkIsolated(net, c);
            if (water && c.SupplyNode >= 0) LoadRunDown(c, gas ? o.GasPressurePa : o.WaterPressurePa);
            return c;
        }

        static bool IsDamaged(CompileOptions o, string key) => o.Damaged != null && o.Damaged.Contains(key);

        /// <summary>Air density at atmospheric pressure, kg/m³ (the ambient of a gas column).</summary>
        static double IdealAir(DataRepository data, double temperatureK) =>
            HeadLoss.Sim.Fluids.IdealGas.Density(data.Balancing.Physics.AtmosphericPressurePa, data.Fluid("air").MolarMass, temperatureK, data.Balancing.Physics.GasConstant);

        /// <summary>A gas's density at atmospheric pressure, kg/m³ (leak orifices, the energy of a cloud).</summary>
        public static double IdealGasDensity(DataRepository data, string fluid, double temperatureK) =>
            HeadLoss.Sim.Fluids.IdealGas.Density(data.Balancing.Physics.AtmosphericPressurePa, data.Fluid(fluid).MolarMass, temperatureK, data.Balancing.Physics.GasConstant);

        /// <summary>The graph node a leak at a tile opens from: the duct node there, or the end of the segment that covers it.</summary>
        static int LeakNode(DuctNetworkState net, TilePos tile, CompiledNetwork c)
        {
            DuctNode? n = net.NodeAt(tile);
            if (n != null) return c.NodeIndex.TryGetValue(n.Id, out int i) ? i : -1;
            foreach (DuctSegment s in net.Segments)
                if (c.SegmentLink.TryGetValue(s.Id, out int link))
                    foreach (TilePos t in DuctEditor.Tiles(net, s))
                        if (t == tile) return c.Graph.Links[link].From;
            return -1;
        }

        /// <summary>
        /// Shutoff isolation (<c>07-equipment.md</c> §Control parts): what the supply reaches with every shutoff open but
        /// not with the closed ones as they are is an isolated branch.
        /// </summary>
        static void MarkIsolated(DuctNetworkState net, CompiledNetwork c)
        {
            NetworkGraph g = c.Graph;
            // A burst segment still joins its two ends as far as isolation goes (the shutoff decides, not the rupture).
            var bridges = new List<(int, int)>();
            foreach (var kv in c.BurstLinks) bridges.Add((g.Links[kv.Value.a].From, g.Links[kv.Value.b].From));
            bool[] Reach(bool openShutoffs)
            {
                var seen = new bool[g.Nodes.Count];
                var stack = new Stack<int>();
                seen[c.SupplyNode] = true;
                stack.Push(c.SupplyNode);
                void Visit(int v)
                {
                    if (v < 0 || seen[v] || g.Nodes[v].Kind == NodeKind.FixedPressure) return;
                    seen[v] = true;
                    stack.Push(v);
                }
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (NetworkLink l in g.Links)
                    {
                        if (l.Closed && !openShutoffs) continue;
                        Visit(l.From == u ? l.To : l.To == u ? l.From : -1);
                    }
                    foreach ((int a, int b) in bridges) Visit(a == u ? b : b == u ? a : -1);
                }
                return seen;
            }
            bool[] now = Reach(false), all = Reach(true);
            bool Isolated(int node) => all[node] && !now[node];
            foreach (var kv in c.OutletNode) if (Isolated(kv.Value)) c.IsolatedOutlets.Add(kv.Key);
            foreach (var kv in c.SegmentLink)
                if (Isolated(g.Links[kv.Value].From) || Isolated(g.Links[kv.Value].To)) c.IsolatedSegments.Add(kv.Key);
            foreach (var kv in c.BurstLinks)
                if (Isolated(g.Links[kv.Value.a].From) && Isolated(g.Links[kv.Value.b].From)) c.IsolatedSegments.Add(kv.Key);
        }

        /// <summary>
        /// Taps fed through a pump that is running down (<c>05-simulation.md</c> §2). A tap draws a fixed flow, which alone would
        /// hold the flow up until the pump stops. So what only that pump can reach draws f × its design flow, and the pump's
        /// loss is sized to take (1 − f²) of the upstream pressure at that flow: flow fades with f and pressure with f², as
        /// through a closing valve. Taps with another way to the supply are not touched.
        /// </summary>
        /// <param name="upstreamPa">Connection pressure, Pa gauge.</param>
        static void LoadRunDown(CompiledNetwork c, double upstreamPa)
        {
            NetworkGraph g = c.Graph;
            double left = 0;
            foreach (NetworkLink l in g.Links) if (RunsDown(l)) left = Math.Max(left, l.Opening);
            if (left <= 0) return;

            var seen = new bool[g.Nodes.Count];
            var stack = new Stack<int>();
            seen[c.SupplyNode] = true;
            stack.Push(c.SupplyNode);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (NetworkLink l in g.Links)
                {
                    if (!l.IsOpen || RunsDown(l)) continue;
                    int v = l.From == u ? l.To : l.To == u ? l.From : -1;
                    if (v < 0 || seen[v] || g.Nodes[v].Kind == NodeKind.FixedPressure) continue;
                    seen[v] = true;
                    stack.Push(v);
                }
            }

            double flow = 0;
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                if (seen[i] || g.Nodes[i].Kind != NodeKind.Demand) continue;
                g.Nodes[i].Demand *= left;
                flow += g.Nodes[i].Demand;
            }
            if (flow <= 0 || upstreamPa <= 0) return;
            foreach (NetworkLink l in g.Links)
                if (RunsDown(l)) l.RunDownLoss = (1 - l.Opening * l.Opening) * upstreamPa / (flow * flow);
        }

        static bool RunsDown(NetworkLink l) => l.Kind == LinkKind.Fan && l.Opening < 1 && l.IsOpen;

        /// <summary>Design flow of a pressure-measured outlet (taps), m³/s.</summary>
        public static double DesignFlow(DataRepository data, BuildingLayout layout, OutletPlacementDef placement)
        {
            OutletDef def = data.Outlet(placement.Type);
            return def.Tiers.TryGetValue(layout.Tier.ToString(), out OutletRequirementDef? r) ? r.DesignFlowLps / 1000.0 : 0;
        }

        /// <summary>Speed of a device at an opening: the crank's, its set speed with variable speed, else on/off; × the share left × the power factor.</summary>
        static double DeviceSpeed(EquipmentDef def, EquipmentState eq, double crankSpeed, double powerSpeed, double left)
        {
            if (def.SpeedControl == "crank") return crankSpeed;
            bool variable = def.SpeedControl == "variable" || eq.Vsd;
            return left * (variable ? eq.Speed : 1) * (def.PowerModel == "none" ? 1 : powerSpeed);
        }

        /// <summary>Share of its speed a device has now: 1 running, 0 stopped, in between while running down (<c>05-simulation.md</c> §2).</summary>
        static double SpeedLeft(CompileOptions o, string key, bool on, EquipmentDef def)
        {
            if (def.SpeedControl == "crank") return 1;
            double left = on ? 1 : o.RunDown != null && o.RunDown.TryGetValue(key, out double f) ? f : 0;
            return def.PowerModel == "none" ? left : left * o.PowerRunDown;
        }

        /// <summary>Marks a link as running down so the solver loads it; a stopped link is out of the solve anyway.</summary>
        static void SetRunDown(NetworkLink link, double left)
        {
            if (left > 0 && left < 1) link.Opening = left;
        }

        /// <summary>Efficiency multiplier of a variable-speed drive (<c>07-equipment.md</c>: costs 3% efficiency).</summary>
        static double DriveEfficiency(DataRepository data, string category)
        {
            foreach (EquipmentDef e in data.Equipment)
                if (e.Placement == "addon" && e.FitsCategories.Contains(category)) return e.EfficiencyFactor;
            return 1;
        }

        static void AssignFittings(DuctNetworkState net, DataRepository data, CompiledNetwork c, Dictionary<int, int> segLink, bool water)
        {
            // Flow direction for fitting K comes from hop distance to the intake (D9).
            Dictionary<int, int> dist = DuctEditor.HopsFromIntake(net);
            FittingDef entry = data.Fitting("intake-entry");
            FittingDef sharp = data.Fitting("elbow-90-sharp");
            FittingDef smooth = data.Fitting("elbow-90-long-radius");
            FittingDef tee = data.Fitting("tee-sharp");
            FittingDef teeSmooth = data.Fitting("tee-smooth");
            FittingDef cross = data.Fitting("cross-sharp");
            FittingDef reducer = data.Fitting("reducer");
            var segs = new List<DuctSegment>(4);

            foreach (DuctNode n in net.Nodes)
            {
                segs.Clear();
                foreach (DuctSegment s in net.Segments) if ((s.NodeA == n.Id || s.NodeB == n.Id) && segLink.ContainsKey(s.Id)) segs.Add(s);
                if (segs.Count == 0) continue;

                if (n.Kind == DuctNodeKind.Intake)
                {
                    if (!water) Add(c, n.Id, FittingKind.Entry, entry.Id, entry.K, segLink[segs[0].Id]);
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
                    if (!TilePos.Collinear(upPos, pos, downPos))
                    {
                        FittingDef elbow = n.SmoothFittings ? smooth : sharp;
                        Add(c, n.Id, FittingKind.Elbow, elbow.Id, ElbowK(elbow, data, down), segLink[down.Id]);
                    }
                    AddReducer(c, n.Id, reducer, up, down, segLink);
                    continue;
                }

                // Tee (3 ducts) or cross (4 ducts): the run continues straight from upstream, branches turn 90°.
                FittingDef junction = segs.Count >= 4 ? cross : n.SmoothFittings ? teeSmooth : tee;
                for (int i = 1; i < segs.Count; i++)
                {
                    DuctSegment out_ = segs[i];
                    TilePos outPos = net.FindNode(out_.Other(n.Id))!.Pos;
                    bool run = TilePos.Collinear(upPos, pos, outPos);
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
