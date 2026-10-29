using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Networks;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Economy
{
    /// <summary>One outlet's delivery, zone and pay from a solve.</summary>
    public sealed class OutletStatus
    {
        public string OutletId = "";
        public OutletDef Def = null!;
        public OutletRequirement Requirement;
        /// <summary>A duct reaches the outlet.</summary>
        public bool Connected;
        /// <summary>Measured delivery, SI (flow m³/s for vents).</summary>
        public double Delivery;
        public OutletZone Zone;
        /// <summary>Payout multiplier m.</summary>
        public double Multiplier;
        /// <summary>Base rate of this outlet, $/s.</summary>
        public double BaseRate;
        /// <summary>What the outlet pays now, $/s (0 while the building is offline).</summary>
        public double Pay;
    }

    /// <summary>A building's outlets, online state and money per second from one solve (<c>02-economy.md</c>).</summary>
    public sealed class BuildingEvaluation
    {
        public readonly List<OutletStatus> Outlets = new List<OutletStatus>();
        /// <summary>Every outlet meets its minimum (no blocking violations exist yet).</summary>
        public bool Online;
        /// <summary>Outlets below their minimum (not connected counts).</summary>
        public int BelowMinimum;
        /// <summary>Outlets above their code maximum (they pay 0).</summary>
        public int AboveCodeMax;
        /// <summary>Gross income, $/s (0 when offline).</summary>
        public double Gross;
        /// <summary>What the outlets would pay if the building were online, $/s.</summary>
        public double PotentialGross;
        /// <summary>Electrical input power of all running devices, W.</summary>
        public double PowerW;
        /// <summary>Electricity cost, $/s.</summary>
        public double Electricity;
        /// <summary>Recurring fines, $/s (none before city routing exists).</summary>
        public double Fines;
        /// <summary>Insurance premium, $/s (none yet).</summary>
        public double Premiums;

        /// <summary>Net income, $/s (can be negative).</summary>
        public double Net => Gross - Electricity - Fines - Premiums;

        public OutletStatus? Find(string outletId) => Outlets.Find(o => o.OutletId == outletId);
    }

    /// <summary>Applies the payout bands, the online rule and the income formula to a solved network.</summary>
    public static class BuildingEconomy
    {
        /// <summary>Base rate of an outlet type in a tier, $/s (tier base pay × the outlet's pay factor).</summary>
        public static double BaseRate(DataRepository data, OutletDef outlet, int tier) =>
            data.Balancing.Economy.BasePay[tier - 1] * outlet.PayFactor;

        /// <summary>The outlet's requirement in SI for the layout's tier.</summary>
        public static OutletRequirement Requirement(DataRepository data, BuildingLayout layout, OutletPlacementDef placement)
        {
            OutletDef def = data.Outlet(placement.Type);
            OutletRequirementDef tier = def.Tiers[layout.Tier.ToString()];
            if (placement.Min <= 0) return OutletRequirement.FromDef(def, tier);
            return OutletRequirement.FromDef(def, new OutletRequirementDef { Min = placement.Min, Ideal = placement.Ideal, Max = placement.Max });
        }

        /// <summary>Evaluates a solve. A null network (not solved yet) leaves every outlet unconnected.</summary>
        public static BuildingEvaluation Evaluate(DataRepository data, BuildingLayout layout, CompiledNetwork? network, SolveResult? result)
        {
            EconomyDef e = data.Balancing.Economy;
            var ev = new BuildingEvaluation();
            foreach (OutletPlacementDef placement in layout.Outlets)
            {
                OutletDef def = data.Outlet(placement.Type);
                var st = new OutletStatus
                {
                    OutletId = placement.Id,
                    Def = def,
                    Requirement = Requirement(data, layout, placement),
                    BaseRate = BaseRate(data, def, layout.Tier),
                };
                if (network != null && result != null && network.OutletLink.TryGetValue(placement.Id, out int link))
                {
                    st.Connected = true;
                    st.Delivery = System.Math.Max(0, result.Flow[link]);
                }
                bool risky = data.Fluid(def.Fluid).Risky;
                st.Multiplier = Payout.Multiplier(st.Requirement, st.Delivery, risky, e, out st.Zone);
                if (!st.Connected) st.Zone = OutletZone.NotConnected;
                if (st.Zone == OutletZone.NotConnected || st.Zone == OutletZone.BelowMinimum) ev.BelowMinimum++;
                if (st.Zone == OutletZone.AboveCodeMax) ev.AboveCodeMax++;
                ev.PotentialGross += st.BaseRate * st.Multiplier;
                ev.Outlets.Add(st);
            }
            ev.Online = ev.BelowMinimum == 0;
            if (ev.Online)
            {
                foreach (OutletStatus st in ev.Outlets) st.Pay = st.BaseRate * st.Multiplier;
                ev.Gross = ev.PotentialGross;
            }

            if (network != null && result != null)
            {
                foreach (var kv in network.FanLink) ev.PowerW += result.Power[kv.Value];
                foreach (var kv in network.InlineLink) ev.PowerW += result.Power[kv.Value];
            }
            ev.Electricity = ev.PowerW / 1000.0 * e.ElectricityPricePerKWs * e.ElectricityTierFactor[layout.Tier - 1];
            return ev;
        }
    }
}
