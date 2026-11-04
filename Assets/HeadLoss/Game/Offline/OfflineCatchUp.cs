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
        /// <summary>Bursts while away, and repairs that finished.</summary>
        public int Bursts, RepairsDone;
        /// <summary>Gas leaks that started while away, and alarms raised.</summary>
        public int Leaks, Alarms;
    }

    /// <summary>A building waiting with a paused explosion timer: first in the report, with a "Go there" button.</summary>
    public sealed class PausedTimerLine
    {
        public string BuildingId = "";
        public string Name = "";
        /// <summary>Timer left when it resumes, s.</summary>
        public double SecondsLeft;
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
        /// <summary>Bursts while away, repairs that finished, and what contract crews charged, $ (paid on top of the net).</summary>
        public int Bursts, RepairsDone;
        public double RepairCosts;
        /// <summary>Gas leaks that started while away, alarms raised and certificates that ran out.</summary>
        public int Leaks, Alarms, CertificatesExpired;
        /// <summary>Buildings waiting with a paused explosion timer.</summary>
        public readonly List<PausedTimerLine> PausedTimers = new List<PausedTimerLine>();
        public double WalletBefore, WalletAfter, DebtBefore, DebtAfter;
        public readonly List<OfflineBuildingLine> Buildings = new List<OfflineBuildingLine>();
        /// <summary>Tiers unlocked during the catch-up.</summary>
        public readonly List<int> Unlocked = new List<int>();
        /// <summary>How long the catch-up took, ms (budget: 72 h × 100 buildings under 2 s).</summary>
        public double ComputeMilliseconds;
    }

    /// <summary>
    /// Offline progress (<c>12-save-offline.md</c> §Offline catch-up). Every building's built network is solved at steady
    /// state with the crank at 0, then income × time is added analytically between stops: the climate steps of
    /// <c>offline.climateStepHours</c> (each at the outside temperature of its middle hour), a burst drawn from the Weibull
    /// model, a random gas fitting leak, a room reaching its alarm threshold (well-mixed per room), gas clearing, a certificate
    /// running out, a crew finishing a repair. Explosion timers stay paused. City fines and premiums are charged and debt is
    /// handled as in game. Events are the stop still to come (Phase 6).
    /// </summary>
    public static class OfflineCatchUp
    {
        static OfflineGas GasFor(BuildingSession s, Dictionary<BuildingSession, OfflineGas> all)
        {
            if (!all.TryGetValue(s, out OfflineGas? g)) all[s] = g = new OfflineGas(s);
            return g;
        }

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

            // Connections from the city, then each building's draw and water use back into a second city solve.
            ctx.SolveCity();
            foreach (BuildingSession s in ctx.Sessions) s.PrepareOffline();
            if (ctx.City != null)
            {
                ctx.SolveCity();
                foreach (BuildingSession s in ctx.Sessions) s.PrepareOffline();
            }
            foreach (BuildingSession s in ctx.Sessions)
            {
                s.OfflineRules = true;
                s.Failures.OfflineMode = true;
                s.GasSafety.OfflineMode = true;
                s.Failures.OfflinePay = price =>
                {
                    g.ApplyIncome(-price);
                    r.RepairCosts += price;
                    return true;
                };
                s.Failures.DispatchContract(s.Committed);
            }
            GameLoop.FinesPerSecond(ctx, out double finesPerSecond);
            var gasRooms = new Dictionary<BuildingSession, OfflineGas>();

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
                    OfflineBuildingLine line = lines[s];
                    double premium = GameLoop.PremiumFraction(ctx, s);
                    double left = dt, now = g.PlayTimeSeconds + t;
                    BuildingEvaluation ev = s.SteadyState(outside);
                    while (left > 1e-9)
                    {
                        Failures.BuildingFailures f = s.Failures;
                        NetworkSnapshot snap = s.LastSteadySnapshot!;
                        double adv = left;
                        Failures.OfflineBurstSample? burst = f.SampleNextBurst(snap, s.Committed);
                        bool bursts = burst != null && burst.Seconds < adv;
                        if (bursts) adv = burst!.Seconds;
                        bool repairing = f.State.Repairs.Count > 0;
                        if (repairing && f.State.Repairs[0].Remaining <= adv)
                        {
                            adv = f.State.Repairs[0].Remaining;
                            bursts = false;
                        }
                        OfflineGas? gas = s.HasGas ? GasFor(s, gasRooms) : null;
                        OfflineGasStop stop = gas != null ? gas.NextStop(snap, adv, f) : default;
                        if (stop.Kind != OfflineGasStopKind.None)
                        {
                            adv = stop.Seconds;
                            bursts = false;
                        }

                        line.Gross += ev.Gross * adv;
                        line.Electricity += ev.Electricity * adv;
                        line.Net += ev.Net * adv;
                        if (ev.Online)
                        {
                            onlineAtEnd.Add(s);
                            line.OnlineSeconds += adv;
                            s.Record.EverOnline = true;
                        }
                        else onlineAtEnd.Remove(s);
                        s.Record.LifetimeIncome += ev.Gross * adv;
                        r.Gross += ev.Gross * adv;
                        r.Electricity += ev.Electricity * adv;
                        r.Fines += ev.Fines * adv;
                        r.Premiums += (ev.Premiums + ev.Gross * premium) * adv;
                        stepNet += (ev.Net - ev.Gross * premium) * adv;

                        f.AdvanceStress(snap, s.Committed, adv);
                        left -= adv;
                        now += adv;
                        bool changed = false;
                        if (gas != null) changed |= gas.Advance(snap, adv, stop, now, line, r);
                        if (repairing && f.TickRepairs(adv, s.Record))
                        {
                            line.RepairsDone++;
                            r.RepairsDone++;
                            changed = true;
                        }
                        if (bursts)
                        {
                            f.BurstSampled(snap, s.Committed, burst!, now);
                            line.Bursts++;
                            r.Bursts++;
                            changed = true;
                        }
                        if (changed) ev = s.SteadyState(outside);
                    }
                }
                r.Fines += finesPerSecond * dt;
                stepNet -= finesPerSecond * dt;
                g.ApplyIncome(stepNet);
                g.LifetimeIncome += stepNet;
            }
            foreach (BuildingSession s in ctx.Sessions)
            {
                s.OfflineRules = false;
                s.Failures.OfflineMode = false;
                s.Failures.OfflinePay = null;
                s.GasSafety.OfflineMode = false;
                if (gasRooms.TryGetValue(s, out OfflineGas? og)) og.CarryIntoGrid();
                s.MarkDirty();
                if (s.GasSafety.TimerRunning)
                    r.PausedTimers.Add(new PausedTimerLine { BuildingId = s.Record.BuildingId, Name = s.Layout.DisplayName, SecondsLeft = s.GasSafety.TimerLeft });
            }
            r.Net = r.Gross - r.Electricity - r.Fines - r.Premiums;
            foreach (BuildingSession s in ctx.Sessions)
                if (lines[s].Gross > 0 || lines[s].Electricity > 0 || lines[s].Bursts > 0 || lines[s].RepairsDone > 0 || lines[s].Leaks > 0 || lines[s].Alarms > 0) r.Buildings.Add(lines[s]);
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
