using System;
using System.Collections.Generic;
using System.Diagnostics;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Offline
{
    /// <summary>One building's share of the time away.</summary>
    public sealed class OfflineBuildingLine
    {
        public string BuildingId = "";
        public string Name = "";
        /// <summary>Money over the whole time away, $.</summary>
        public double Gross, Electricity, Net;
        /// <summary>Seconds the building was online.</summary>
        public double OnlineSeconds;
    }

    /// <summary>The "While you were away" report (<c>11-ui-visuals-audio.md</c> §Screens, <c>12-save-offline.md</c>).</summary>
    public sealed class OfflineReport
    {
        /// <summary>Real time since the save, s (before the cap).</summary>
        public double AwaySeconds;
        /// <summary>Time credited, s (capped at <c>offline.maxHours</c>).</summary>
        public double Seconds;
        public bool Capped;
        /// <summary>Totals over the time away, $.</summary>
        public double Gross, Electricity, Fines, Premiums, Net;
        public double WalletBefore, WalletAfter, DebtBefore, DebtAfter;
        public readonly List<OfflineBuildingLine> Buildings = new List<OfflineBuildingLine>();
        /// <summary>Tiers unlocked during the catch-up.</summary>
        public readonly List<int> Unlocked = new List<int>();
        /// <summary>How long the catch-up took, ms (budget: 72 h × 100 buildings under 2 s).</summary>
        public double ComputeMilliseconds;
    }

    /// <summary>
    /// Offline progress (<c>12-save-offline.md</c> §Offline catch-up), Phase 3 scope. Every building's built network is
    /// solved at steady state with the crank at 0, then income × time is added analytically in steps of
    /// <c>offline.climateStepHours</c> (each step at the outside temperature of its middle hour), with electricity
    /// charged and debt handled as in game. The event timeline's other stops (bursts, leaks, repairs, certificates,
    /// events) arrive with those systems in Phases 4–6; nothing changes a building's steady state offline yet.
    /// </summary>
    public static class OfflineCatchUp
    {
        /// <param name="awaySeconds">Real time since the save, s (already 0 when the clock went back).</param>
        /// <param name="startLocalHour">Local hour when the save was written (the daily temperature curve).</param>
        public static OfflineReport Run(GameContext ctx, double awaySeconds, double startLocalHour)
        {
            var sw = Stopwatch.StartNew();
            GameState g = ctx.Game;
            double cap = ctx.Data.Balancing.Offline.MaxHours * 3600;
            var r = new OfflineReport
            {
                AwaySeconds = awaySeconds,
                Seconds = Math.Max(0, Math.Min(awaySeconds, cap)),
                Capped = awaySeconds > cap,
                WalletBefore = g.Wallet,
                DebtBefore = g.Debt,
            };
            var lines = new Dictionary<BuildingSession, OfflineBuildingLine>();
            var onlineAtEnd = new HashSet<BuildingSession>();
            foreach (BuildingSession s in ctx.Sessions)
                lines[s] = new OfflineBuildingLine { BuildingId = s.Record.BuildingId, Name = s.Layout.DisplayName };

            double step = Math.Max(60, ctx.Data.Balancing.Offline.ClimateStepHours * 3600);
            for (double t = 0; t < r.Seconds - 1e-9; t += step)
            {
                double dt = Math.Min(step, r.Seconds - t);
                double hour = ((startLocalHour + (t + dt / 2) / 3600) % 24 + 24) % 24;
                double outside = ctx.Climate.TemperatureAt(hour);
                double stepNet = 0;
                onlineAtEnd.Clear();
                foreach (BuildingSession s in ctx.Sessions)
                {
                    BuildingEvaluation ev = s.SteadyState(outside);
                    OfflineBuildingLine line = lines[s];
                    line.Gross += ev.Gross * dt;
                    line.Electricity += ev.Electricity * dt;
                    line.Net += ev.Net * dt;
                    if (ev.Online)
                    {
                        onlineAtEnd.Add(s);
                        line.OnlineSeconds += dt;
                        s.Record.EverOnline = true;
                    }
                    s.Record.LifetimeIncome += ev.Gross * dt;
                    r.Gross += ev.Gross * dt;
                    r.Electricity += ev.Electricity * dt;
                    r.Fines += ev.Fines * dt;
                    r.Premiums += ev.Premiums * dt;
                    stepNet += ev.Net * dt;
                }
                g.ApplyIncome(stepNet);
                g.LifetimeIncome += stepNet;
            }
            r.Net = r.Gross - r.Electricity - r.Fines - r.Premiums;
            foreach (BuildingSession s in ctx.Sessions)
                if (lines[s].Gross > 0 || lines[s].Electricity > 0) r.Buildings.Add(lines[s]);
            r.Buildings.Sort((a, b) => b.Net.CompareTo(a.Net));

            int before = Progression.Tech.UnlockedTier(g);
            if (r.Seconds > 0) ctx.UpdateUnlocks(s => onlineAtEnd.Contains(s));
            for (int t = before + 1; t <= Progression.Tech.UnlockedTier(g); t++) r.Unlocked.Add(t);
            r.WalletAfter = g.Wallet;
            r.DebtAfter = g.Debt;
            r.ComputeMilliseconds = sw.Elapsed.TotalMilliseconds;
            return r;
        }
    }
}
