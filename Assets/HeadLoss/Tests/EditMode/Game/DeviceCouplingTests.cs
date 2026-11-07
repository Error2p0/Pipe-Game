using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
using HeadLoss.Game.Thermal;
using HeadLoss.Sim.Network;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>
    /// Phase 6 foundations: devices that join networks and the coupled solve (<c>05-simulation.md</c> §1.5): hot → cold demand,
    /// heat → gas demand, solved in dependency order and twice over.
    /// </summary>
    public class DeviceCouplingTests
    {
        static List<TilePos> At0(params (int x, int y)[] points) => Run(points);

        /// <summary>The water house at tier 4 with its kitchen tap turned hot and, optionally, a gas connection on the kitchen's east wall.</summary>
        static BuildingSession House(bool gas)
        {
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(Data.HandmadeBuilding("water-house")))!;
            def.Id = "coupled-house";
            def.Tier = 4;
            def.Outlets.Find(o => o.Id == "tap-1")!.Type = "hot-water-tap";
            if (gas) def.EntryOpenings.Add(new EntryOpeningDef { Id = "gas", Utility = "gas", X = 9, Y = 3, Floor = 0, Facing = "east" });
            BuildingLayout layout = BuildingLayout.From(Data, def);
            var game = new GameState { Wallet = 1e6 };
            BuildingRecord record = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(record);
            return new BuildingSession(Data, layout, record, game);
        }

        /// <summary>A water heater in the kitchen: cold from the connection to its in port, hot from its out port to the hot tap.</summary>
        static (BuildingSession s, string key) WithWaterHeater()
        {
            BuildingSession s = House(false);
            var place = new PlaceDeviceCommand("water-heater", new TilePos(6, 2), "east");
            Assert.IsTrue(s.Execute(place), place.LastError.ToString());
            Assert.IsTrue(s.Execute(new PlaceRunCommand(At0((9, 1), (9, 2), (6, 2)), 22, "copper", false, "water")), "cold to the heater");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(At0((7, 2), (7, 1)), 15, "copper", false, "hot-water")), "hot to the tap");
            s.SolveNow();
            return (s, s.State.Devices[0].Key);
        }

        [Test]
        public void PlaceDevice_AddsAnAnchorAtEachPort_OnItsNetwork()
        {
            (BuildingSession s, string key) = WithWaterHeater();
            DuctNode cold = s.State.Water.Nodes.Find(n => n.AnchorId == key)!;
            Assert.AreEqual(DuctNodeKind.DeviceIn, cold.Kind);
            Assert.AreEqual(new TilePos(6, 2), cold.Pos);
            List<DuctNode> hot = s.State.Net("hot-water").Nodes.FindAll(n => n.AnchorId == key);
            Assert.AreEqual(2, hot.Count, "out and return");
            Assert.AreEqual(new TilePos(7, 2), hot.Find(n => n.PartId == "out")!.Pos);
            Assert.AreEqual(DuctNodeKind.DeviceOut, hot.Find(n => n.PartId == "out")!.Kind);
            Assert.Greater(s.PendingCost.Net, Data.EquipmentItem("water-heater").Price - 1e-9, "the heater is bought with the pipes");
        }

        [Test]
        public void PlaceDevice_NotOnAnOutletOrOverAnotherDevice()
        {
            BuildingSession s = House(false);
            var onTap = new PlaceDeviceCommand("water-heater", new TilePos(7, 1), "east");
            Assert.IsFalse(s.Execute(onTap));
            Assert.AreEqual(DrawError.NoFloorSpace, onTap.LastError);
            Assert.IsTrue(s.Execute(new PlaceDeviceCommand("water-heater", new TilePos(6, 2), "east")));
            var over = new PlaceDeviceCommand("water-heater", new TilePos(7, 2), "north");
            Assert.IsFalse(s.Execute(over));
            Assert.AreEqual(DrawError.NoFloorSpace, over.LastError);
        }

        [Test]
        public void Boiler_NeedsAnExteriorWall()
        {
            BuildingSession s = House(true);
            var inner = new PlaceDeviceCommand("gas-boiler", new TilePos(6, 2), "east");
            Assert.IsFalse(s.Execute(inner));
            Assert.AreEqual(DrawError.NeedsExteriorWall, inner.LastError);
            Assert.IsTrue(s.Execute(new PlaceDeviceCommand("gas-boiler", new TilePos(6, 0), "east")));
        }

        [Test]
        public void RemoveDevice_TakesItsPortsAndThePipesOnThem()
        {
            (BuildingSession s, string key) = WithWaterHeater();
            Assert.IsTrue(s.Execute(new RemoveDeviceCommand(key)));
            Assert.IsEmpty(s.State.Devices);
            Assert.IsNull(s.State.Water.Nodes.Find(n => n.AnchorId == key));
            Assert.IsEmpty(s.State.Net("hot-water").Segments, "the hot pipe ended on the heater");
            Assert.IsTrue(s.Undo());
            Assert.AreEqual(1, s.State.Devices.Count);
        }

        [Test]
        public void Order_SolvesTheChildBeforeItsParent()
        {
            var couplings = new List<Coupling>
            {
                new Coupling { Key = "h", Parent = "water", Child = "hot-water" },
                new Coupling { Key = "b", Parent = BuildingState.GasFluid, Child = "heating-water", Fuel = true },
            };
            List<string> order = Couplings.Order(new[] { "air", "water", "hot-water", "heating-water", BuildingState.GasFluid }, couplings);
            Assert.AreEqual("air", order[0]);
            Assert.Less(order.IndexOf("hot-water"), order.IndexOf("water"), "hot before cold");
            Assert.Less(order.IndexOf("heating-water"), order.IndexOf(BuildingState.GasFluid), "heat before gas");
            Assert.AreEqual(5, order.Count);
        }

        [Test]
        public void WaterHeater_ColdNetworkCarriesWhatTheHotTapDraws()
        {
            (BuildingSession s, string key) = WithWaterHeater();
            NetworkSnapshot snap = s.Current!;
            FluidSolve cold = snap.Water!, hot = snap.For("hot-water")!;
            Assert.IsTrue(cold.Result.Converged && hot.Result.Converged);
            double fromCity = cold.Compiled.SupplyFlow(cold.Result);
            Assert.AreEqual(snap.CouplingDemand[key], fromCity, 1e-12, "the city supplies what the heater draws");
            double tap = NetworkCompiler.DesignFlow(Data, s.Layout, s.Layout.FindOutlet("tap-1")!);
            Assert.AreEqual(tap, fromCity, tap * 0.01, "the hot tap's draw (densities differ by under 1%)");
            Assert.AreEqual(tap, cold.Result.Flow[cold.Compiled.Graph.Links.FindIndex(l => l.Label == $"drop-{key}:in")], tap * 0.01);
        }

        [Test]
        public void WaterHeater_HotSideTakesTheColdSidesPressure()
        {
            (BuildingSession s, string key) = WithWaterHeater();
            FluidSolve cold = s.Current!.Water!, hot = s.Current.For("hot-water")!;
            double inlet = cold.Result.NodePressure[cold.Compiled.DeviceInlet[key]];
            Assert.Greater(inlet, 0);
            Assert.AreEqual(inlet, hot.Compiled.Graph.Nodes[hot.Compiled.DeviceSource[key]].FixedPressure, 1.0,
                "solved twice: the hot side's source sits at the cold inlet's pressure");
            OutletStatus st = s.DesignEvaluation.Find("tap-1")!;
            Assert.IsTrue(st.Connected);
            Assert.Greater(st.Delivery, 0, "the hot tap has pressure");
            Assert.Less(st.Delivery, inlet, "less the heater and pipes");
        }

        [Test]
        public void WaterHeater_HeatsWithinItsCapacity_AndDrawsElectricity()
        {
            (BuildingSession s, string key) = WithWaterHeater();
            FluidSolve hot = s.Current!.For("hot-water")!;
            int heater = hot.Compiled.HeaterLink[key];
            EquipmentDef def = Data.EquipmentItem("water-heater");
            Assert.AreEqual(def.HeatOutputW, hot.Thermal!.HeatAdded[heater], 1e-6, "0.1 L/s from 12 °C to 60 °C needs 20 kW: 3 kW is all it has");
            Assert.Less(hot.Thermal.LinkOutT[heater], Units.CToK(def.SetpointC));
            Assert.Greater(hot.Thermal.LinkOutT[heater], Units.CToK(Data.Balancing.Water.MainsTemperatureC) + 5);
            Assert.AreEqual(def.HeatOutputW / def.ThermalEfficiency, s.DesignEvaluation.PowerW, 1e-6, "no fans or pumps run: all the power is the heater's");

            Assert.IsTrue(s.Execute(new SetDeviceCommand(key, on: false)));
            s.SolveNow();
            hot = s.Current!.For("hot-water")!;
            Assert.AreEqual(0, hot.Thermal!.HeatAdded[hot.Compiled.HeaterLink[key]], 1e-9);
            Assert.AreEqual(0, s.DesignEvaluation.PowerW, 1e-9, "switched off it draws nothing");
        }

        [Test]
        public void SmallerDraw_ReachesTheSetpoint()
        {
            var h = new HeadLoss.Sim.Thermal.Heater(Units.CToK(60), 3000);
            Assert.AreEqual(Units.CToK(60), h.Apply(Units.CToK(12), 0.01, 4180), 1e-9, "0.01 kg/s needs 2 kW to reach 60 °C");
        }

        /// <summary>A boiler on the kitchen's south wall: gas from the east connection, a heating loop round the kitchen with a circulation pump.</summary>
        static (BuildingSession s, string key) WithBoiler()
        {
            BuildingSession s = House(true);
            Assert.IsTrue(s.Execute(new PlaceDeviceCommand("gas-boiler", new TilePos(6, 0), "east")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(At0((9, 3), (9, 0), (6, 0)), 22, "copper", false, BuildingState.GasFluid)), "gas");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(At0((7, 0), (7, 3), (6, 3), (6, 0)), 22, "copper", false, "heating-water")), "loop");
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(7, 2), "circulation-pump", false, "heating-water")), "pump");
            s.SolveNow();
            return (s, s.State.Devices[0].Key);
        }

        [Test]
        public void Boiler_HeatsItsLoop_AndBurnsGasForTheHeat()
        {
            (BuildingSession s, string key) = WithBoiler();
            NetworkSnapshot snap = s.Current!;
            FluidSolve loop = snap.For("heating-water")!, gas = snap.Gas!;
            Assert.IsTrue(loop.Result.Converged && gas.Result.Converged);
            int heater = loop.Compiled.HeaterLink[key];
            Assert.Greater(loop.Result.MassFlow[heater], 0.01, "the pump drives the loop");
            double heat = loop.Thermal!.HeatAdded[heater];
            EquipmentDef def0 = Data.EquipmentItem("gas-boiler");
            double losses = 0;
            foreach (DuctSegment seg in s.State.Net("heating-water").Segments) losses += loop.Thermal.HeatToAmbient[loop.Compiled.SegmentLink[seg.Id]];
            Assert.AreEqual(losses, heat, losses * 0.02, "settled: the boiler replaces what the loop's pipes give the room");
            Assert.AreEqual(Units.CToK(def0.SetpointC), loop.Thermal.LinkOutT[heater], 1e-6, "well within its 24 kW: it holds 70 °C");
            Assert.Greater(heat, 50, "70 °C water loses heat to a 20 °C kitchen along bare copper");
            Assert.IsTrue(loop.Thermal.Loops);

            EquipmentDef def = Data.EquipmentItem("gas-boiler");
            double pAbs = Data.Balancing.Physics.AtmosphericPressurePa + snap.CouplingPressure[key];
            double rho = s.Properties(BuildingState.GasFluid).Density(pAbs, Units.CToK(Data.Balancing.Gas.SupplyTemperatureC));
            double expected = heat / (def.ThermalEfficiency * Data.Fluid(BuildingState.GasFluid).LowerHeatingValueMJPerKg * 1e6 * rho);
            Assert.AreEqual(expected, snap.CouplingDemand[key], expected * 1e-9);
            Assert.AreEqual(expected, gas.Compiled.SupplyFlow(gas.Result), expected * 1e-6, "the gas main supplies the boiler's burn");
            Assert.AreEqual(expected, Couplings.FuelFlow(heat, def.ThermalEfficiency, 50, rho), expected * 1e-9);
        }

        [Test]
        public void Boiler_Off_BurnsNothing()
        {
            (BuildingSession s, string key) = WithBoiler();
            Assert.IsTrue(s.Execute(new SetDeviceCommand(key, on: false)));
            s.SolveNow();
            Assert.AreEqual(0, s.Current!.CouplingDemand[key], 1e-15);
            Assert.AreEqual(0, s.Current.Gas!.Compiled.SupplyFlow(s.Current.Gas.Result), 1e-12);
        }
    }
}
