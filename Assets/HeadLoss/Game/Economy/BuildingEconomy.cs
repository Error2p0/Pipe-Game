using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
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
        /// <summary>Measured delivery, SI (flow m³/s for vents, residual pressure Pa for taps).</summary>
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
        /// <summary>An unrepaired burst that no shutoff isolates cuts the gross (in game to 0, offline to 25%).</summary>
        public bool Burst;
        /// <summary>The gross before the burst cut, $/s.</summary>
        public double GrossBeforeBurst;
        /// <summary>The building is evacuated (gas alarm): it pays nothing (<c>02-economy.md</c>: evacuationFactor).</summary>
        public bool Evacuated;
        /// <summary>The gas safety certificate has expired: pay × the certificate penalty.</summary>
        public bool CertificateExpired;

        /// <summary>Layout code violations (each blocking one keeps the building offline).</summary>
        public readonly List<Codes.Violation> Violations = new List<Codes.Violation>();
        /// <summary>Blocking violations stop the building coming online (<c>09-codes.md</c>).</summary>
        public int BlockingViolations;

        /// <summary>Applies layout violations: any blocking one keeps the building offline (gross 0).</summary>
        public void ApplyViolations(List<Codes.Violation> violations)
        {
            Violations.Clear();
            Violations.AddRange(violations);
            BlockingViolations = 0;
            foreach (Codes.Violation v in violations) if (v.Blocking) BlockingViolations++;
            if (BlockingViolations == 0 || !Online) return;
            Online = false;
            Gross = 0;
            foreach (OutletStatus o in Outlets) o.Pay = 0;
        }

        /// <summary>Applies the burst cut (<c>02-economy.md</c> §Income formula: burstFactor).</summary>
        public void ApplyBurst(double factor)
        {
            Burst = true;
            GrossBeforeBurst = Gross;
            Gross *= factor;
            foreach (OutletStatus o in Outlets) o.Pay *= factor;
        }

        /// <summary>Applies a factor on all pay (prestige pay rates, the certificate penalty).</summary>
        public void ApplyPayFactor(double factor)
        {
            Gross *= factor;
            PotentialGross *= factor;
            foreach (OutletStatus o in Outlets) o.Pay *= factor;
        }

        /// <summary>The evacuation factor (<c>02-economy.md</c> §Income formula): the building pays nothing while evacuated and does not count as online.</summary>
        public void ApplyEvacuation()
        {
            Evacuated = true;
            Online = false;
            Gross = 0;
            foreach (OutletStatus o in Outlets) o.Pay = 0;
        }

        /// <summary>A tripped panel stops all equipment and takes the building offline (<c>04-buildings.md</c> §Electrical panel).</summary>
        public void ApplyPanelTrip()
        {
            Online = false;
            Gross = 0;
            foreach (OutletStatus o in Outlets) o.Pay = 0;
        }

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
            var r = placement.Min <= 0 ? tier : new OutletRequirementDef { Min = placement.Min, Ideal = placement.Ideal, Max = placement.Max };
            // The code maximum is the noise or tap-pressure code: it only applies where that code is in force (09).
            string code = Codes.CodeChecker.CodeForOutlet(def);
            if (code.Length > 0 && r.Max > 0 && !Codes.CodeChecker.InForce(data, layout).Contains(code))
                r = new OutletRequirementDef { Min = r.Min, Ideal = r.Ideal, Max = 0 };
            return OutletRequirement.FromDef(def, r);
        }

        /// <summary>Electrical input power of a device link, W (a variable-speed drive divides by its efficiency factor).</summary>
        static double DevicePower(CompiledNetwork network, SolveResult result, int link) =>
            network.DriveFactor.TryGetValue(link, out double f) ? result.Power[link] / f : result.Power[link];

        /// <summary>Evaluates an air-only solve (the auto-router, tools and tests).</summary>
        public static BuildingEvaluation Evaluate(DataRepository data, BuildingLayout layout, CompiledNetwork? network, SolveResult? result) =>
            Evaluate(data, layout, network != null && result != null ? new[] { new FluidSolve(network, result) } : null);

        /// <summary>Evaluates every network of a snapshot.</summary>
        public static BuildingEvaluation Evaluate(DataRepository data, BuildingLayout layout, NetworkSnapshot? snapshot) =>
            Evaluate(data, layout, snapshot?.All);

        /// <summary>Evaluates solves. Null (not solved yet) leaves every outlet unconnected.</summary>
        /// <param name="onlyFluid">Evaluate only this fluid's outlets (the auto-router's per-network passes); null = all.</param>
        /// <param name="skipFluid">Leave this fluid's outlets out (the auto-router before that network is routed); null = none.</param>
        public static BuildingEvaluation Evaluate(DataRepository data, BuildingLayout layout, IReadOnlyList<FluidSolve>? solves, string? onlyFluid = null, string? skipFluid = null)
        {
            EconomyDef e = data.Balancing.Economy;
            var ev = new BuildingEvaluation();
            foreach (OutletPlacementDef placement in layout.Outlets)
            {
                OutletDef def = data.Outlet(placement.Type);
                if ((onlyFluid != null && def.Fluid != onlyFluid) || def.Fluid == skipFluid) continue;
                var st = new OutletStatus
                {
                    OutletId = placement.Id,
                    Def = def,
                    Requirement = Requirement(data, layout, placement),
                    BaseRate = BaseRate(data, def, layout.Tier),
                };
                FluidSolve? solve = null;
                if (solves != null) foreach (FluidSolve fs in solves) if (fs.Fluid == def.Fluid) solve = fs;
                if (solve != null && solve.Compiled.OutletLink.TryGetValue(placement.Id, out int link))
                {
                    st.Connected = true;
                    st.Delivery = System.Math.Max(0, solve.Result.Flow[link]);
                }
                else if (solve != null && solve.Compiled.OutletNode.TryGetValue(placement.Id, out int node))
                {
                    st.Connected = true;
                    st.Delivery = solve.Result.NodeActive[node] ? System.Math.Max(0, solve.Result.NodePressure[node]) : 0;
                }
                bool risky = data.FluidOf(def.Fluid).Risky;
                st.Multiplier = Payout.Multiplier(st.Requirement, st.Delivery, risky, e, out st.Zone);
                if (!st.Connected) st.Zone = OutletZone.NotConnected;
                if (solve != null && solve.Compiled.IsolatedOutlets.Contains(placement.Id))
                {
                    st.Zone = OutletZone.Isolated;
                    st.Multiplier = 0;
                }
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

            if (solves != null)
                foreach (FluidSolve fs in solves)
                {
                    foreach (var kv in fs.Compiled.FanLink) ev.PowerW += DevicePower(fs.Compiled, fs.Result, kv.Value);
                    foreach (var kv in fs.Compiled.InlineLink) ev.PowerW += DevicePower(fs.Compiled, fs.Result, kv.Value);
                }
            ev.Electricity = ev.PowerW / 1000.0 * e.ElectricityPricePerKWs * e.ElectricityTierFactor[layout.Tier - 1];
            return ev;
        }
    }
}
