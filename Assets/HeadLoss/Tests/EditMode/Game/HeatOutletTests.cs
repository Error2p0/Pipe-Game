using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using HeadLoss.Game.Thermal;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Phase 6 foundations: room loads at the design temperatures and outlets measured in watts (<c>02</c>, <c>13</c>).</summary>
    public class HeatOutletTests
    {
        /// <summary>Two 4 × 3 rooms side by side; the west one has a south window, two people and a 150 W machine.</summary>
        static BuildingLayout TwoRooms()
        {
            var def = new BuildingLayoutDef
            {
                Id = "two-rooms", Tier = 4, Width = 8, Height = 3,
                Rooms =
                {
                    new RoomDef { Id = "west", Type = "office", X = 0, Y = 0, Width = 4, Height = 3, Occupants = 2, MachinesW = 150 },
                    new RoomDef { Id = "east", Type = "office", X = 4, Y = 0, Width = 4, Height = 3 },
                },
                Windows = { new WindowDef { X = 1, Y = 0, Floor = 0, Side = "south" } },
            };
            return BuildingLayout.From(Data, def);
        }

        [Test]
        public void ExteriorWall_CountsOnlyEdgesToTheOutside()
        {
            BuildingLayout l = TwoRooms();
            double h = Data.Balancing.RoomGrid.RoomHeight;
            // 3 west + 4 north + 4 south edges; the east side is the other room.
            Assert.AreEqual(11 * h, RoomLoads.ExteriorWallArea(l, 0, h), 1e-9);
            Assert.AreEqual(11 * h, RoomLoads.ExteriorWallArea(l, 1, h), 1e-9);
        }

        [Test]
        public void HeatingLoad_IsWallLossAtTheDesignWinterTemperature()
        {
            BuildingLayout l = TwoRooms();
            var climate = new Climate(Data, Array.Empty<string>());
            var c = Data.Balancing.Climate;
            double area = 11 * Data.Balancing.RoomGrid.RoomHeight;
            RoomLoad west = RoomLoads.For(Data, l, 0, climate);
            Assert.AreEqual(c.WallU * area * (c.HeatingSetpointC - c.DesignHeatingC), west.HeatingW, 1e-9);
            Assert.AreEqual(0.6 * area * 25, west.HeatingW, 1e-9, "13: U 0.6, −5 °C outside, 20 °C inside");
            Assert.AreEqual(west.HeatingW, RoomLoads.For(Data, l, 1, climate).HeatingW, 1e-9, "people and machines do not lower a heating design");
        }

        [Test]
        public void HeatingLoad_ColdClimate_IsColderAndOneAndAHalfTimes()
        {
            BuildingLayout l = TwoRooms();
            var cold = new Climate(Data, new[] { "cold-climate" });
            var c = Data.Balancing.Climate;
            double area = 11 * Data.Balancing.RoomGrid.RoomHeight;
            double expected = c.WallU * area * (c.HeatingSetpointC - (c.DesignHeatingC - 10)) * 1.5;
            Assert.AreEqual(expected, RoomLoads.For(Data, l, 0, cold).HeatingW, 1e-9);
        }

        [Test]
        public void CoolingLoad_AddsWallsSunPeopleAndMachines()
        {
            BuildingLayout l = TwoRooms();
            var climate = new Climate(Data, Array.Empty<string>());
            var c = Data.Balancing.Climate;
            double walls = c.WallU * 11 * Data.Balancing.RoomGrid.RoomHeight * (c.DesignCoolingC - c.CoolingSetpointC);
            double sun = c.SolarPeakWm2 * c.FacadeFactors["south"] * Data.Buildings.Generation.WindowAreaM2;
            Assert.AreEqual(walls + sun + 2 * c.OccupantSensibleW + 150, RoomLoads.For(Data, l, 0, climate).CoolingW, 1e-9);
            Assert.AreEqual(walls, RoomLoads.For(Data, l, 1, climate).CoolingW, 1e-9);
        }

        [Test]
        public void Radiator_EffectivenessFormula()
        {
            // ṁ = 0.05 kg/s, c_p 4180, UA 30 W/K: ε = 1 − e^(−30/209) = 0.13371; 70 °C in, 20 °C setpoint.
            double q = HeatDelivery.RadiatorW(0.05, 4180, 30, Units.CToK(70), Units.CToK(20));
            Assert.AreEqual(0.05 * 4180 * (1 - Math.Exp(-30 / 209.0)) * 50, q, 1e-9);
            Assert.AreEqual(0, HeatDelivery.RadiatorW(0.05, 4180, 30, Units.CToK(18), Units.CToK(20)), "water colder than the room gives nothing");
            Assert.AreEqual(30 * 50, HeatDelivery.RadiatorW(1e6, 4180, 30, Units.CToK(70), Units.CToK(20)), 1e-3, "at very high flow it tends to UA·ΔT");
        }

        /// <summary>The test house at tier 4 with its first vent turned into a cooling terminal rated in watts.</summary>
        static BuildingSession CoolingHouse(double outsideC)
        {
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(Data.HandmadeBuilding("test-house")))!;
            def.Id = "cool-house";
            def.Tier = 4;
            def.Outlets[0].Type = "cooling-terminal";
            def.Outlets[0].Min = 200;
            def.Outlets[0].Ideal = 240;
            def.Outlets[0].Max = 320;
            BuildingLayout layout = BuildingLayout.From(Data, def);
            var game = new GameState { Wallet = 1e6 };
            BuildingRecord record = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(record);
            var s = new BuildingSession(Data, layout, record, game, null, Climate.Fixed(Data, outsideC));
            s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan"));
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 125);
            s.SolveNow();
            return s;
        }

        [Test]
        public void CoolingTerminal_DeliversHeatTakenOutAtTheSetpoint()
        {
            BuildingSession s = CoolingHouse(12);
            FluidSolve air = s.Current!.Air;
            int link = air.Compiled.OutletLink["vent-1"];
            double m = air.Result.MassFlow[link];
            Assert.Greater(m, 0);
            double t = air.Compiled.Graph.Links[link].Temperature;
            double expected = m * s.Air.SpecificHeat(Data.Balancing.Physics.AtmosphericPressurePa, t) * (Units.CToK(Data.Balancing.Climate.CoolingSetpointC) - t);
            Assert.AreEqual(expected, air.OutletHeat["vent-1"], 1e-9);

            OutletStatus st = s.DesignEvaluation.Find("vent-1")!;
            Assert.IsTrue(st.Connected);
            Assert.AreEqual(expected, st.Delivery, 1e-9, "the economy reads the watts, not the flow");
            Assert.AreEqual(200, st.Requirement.Min, 1e-9, "W is SI already");
        }

        [Test]
        public void CoolingTerminal_AirWarmerThanTheSetpoint_DeliversNothing()
        {
            BuildingSession s = CoolingHouse(30);
            Assert.AreEqual(0, s.Current!.Air.OutletHeat["vent-1"]);
            Assert.AreEqual(OutletZone.BelowMinimum, s.DesignEvaluation.Find("vent-1")!.Zone);
        }

        [Test]
        public void HeatOutlet_NotConnected_IsBelowMinimum()
        {
            BuildingSession s = CoolingHouse(12);
            OutletStatus vent2 = s.DesignEvaluation.Find("vent-2")!;
            Assert.AreEqual(OutletZone.NotConnected, vent2.Zone);
            Assert.IsFalse(s.Current!.Air.OutletHeat.ContainsKey("vent-2"), "plain vents have no heat entry");
        }
    }
}
