using System;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Fluids;

namespace HeadLoss.Game.Thermal
{
    /// <summary>
    /// What heat outlets deliver, rated at the room setpoint rather than at the room's temperature now, so pay depends on the network
    /// alone and a room that is already warm (or cool) does not make its outlet look short (<c>02-economy.md</c>: delivery of a
    /// heating or cooling outlet is heat in W). The heat that actually enters the room grid uses the room's temperature.
    /// </summary>
    public static class HeatDelivery
    {
        /// <summary>
        /// Cooling terminals on an air network: <c>ṁ · c_p · (T_set,cool − T_supply)</c>, W, with T_supply the air reaching the
        /// terminal (the last duct thermal solve). Warmer air than the setpoint delivers nothing.
        /// </summary>
        public static void FillAirTerminals(DataRepository data, BuildingLayout layout, FluidSolve fs, IFluidProperties air)
        {
            if (data.FindNetwork(fs.Fluid)?.IsDuct != true) return;
            double setK = Units.CToK(data.Balancing.Climate.CoolingSetpointC);
            double pAbs = data.Balancing.Physics.AtmosphericPressurePa;
            foreach (var kv in fs.Compiled.OutletLink)
            {
                OutletPlacementDef? o = layout.FindOutlet(kv.Key);
                if (o == null) continue;
                OutletDef def = data.Outlet(o.Type);
                if (def.Measured != "heat" || def.LoadBased != "cooling") continue;
                double m = Math.Max(0, fs.Result.MassFlow[kv.Value]);
                double t = fs.Compiled.Graph.Links[kv.Value].Temperature;
                fs.OutletHeat[kv.Key] = CoolingW(m, air.SpecificHeat(pAbs, t), t, setK);
            }
        }

        /// <summary>Cooling delivered by air at <paramref name="supplyK"/> to a room held at <paramref name="setpointK"/>, W (≥ 0).</summary>
        /// <param name="massFlow">kg/s.</param>
        /// <param name="cp">J/(kg·K).</param>
        public static double CoolingW(double massFlow, double cp, double supplyK, double setpointK) =>
            Math.Max(0, massFlow) * cp * Math.Max(0, setpointK - supplyK);

        /// <summary>
        /// Heat a radiator gives at the setpoint (<c>05-simulation.md</c> §4): <c>ε = 1 − exp(−UA / (ṁ c_p))</c>,
        /// <c>Q = ṁ c_p ε (T_in − T_set)</c>, W (≥ 0).
        /// </summary>
        /// <param name="massFlow">kg/s.</param>
        /// <param name="cp">J/(kg·K).</param>
        /// <param name="ua">The radiator's UA, W/K.</param>
        public static double RadiatorW(double massFlow, double cp, double ua, double inletK, double setpointK)
        {
            if (massFlow <= 1e-12 || inletK <= setpointK) return 0;
            double eps = 1 - Math.Exp(-ua / (massFlow * cp));
            return massFlow * cp * eps * (inletK - setpointK);
        }
    }
}
