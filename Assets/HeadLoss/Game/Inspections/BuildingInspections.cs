using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Inspections
{
    /// <summary>
    /// Random inspections of one building (<c>10-events.md</c> §Inspections). Each online building has
    /// <c>inspection.chancePerHour</c> of a visit; the inspector walks over (cosmetic), then checks the codes and the
    /// operating limits of what is built. A pass pays × <c>passPayFactor</c> for <c>passMinutes</c>. A fail costs a fine and
    /// starts a fix deadline that pauses while the player is away; missing it costs a second fine and keeps the building
    /// offline until the findings are fixed. In game only: no inspection starts while the player is away (D161).
    /// </summary>
    public sealed class BuildingInspections
    {
        readonly BuildingSession _s;
        readonly ulong _seed;
        double _sinceTick;

        public BuildingInspections(BuildingSession session, ulong seed)
        {
            _s = session;
            _seed = seed;
        }

        DataRepository Data => _s.Data;
        InspectionDef Def => Data.Balancing.Inspection;
        public InspectionState State => _s.Record.Inspection;

        /// <summary>An inspector is on the way to the building.</summary>
        public bool InspectorComing => State.WalkLeft > 0;
        /// <summary>Share of the inspector's walk done, 0–1 (the city view moves the icon by it).</summary>
        public double WalkProgress => Def.InspectorWalkSeconds > 0 ? 1 - Math.Max(0, State.WalkLeft) / Def.InspectorWalkSeconds : 1;
        /// <summary>A passed inspection's bonus is running.</summary>
        public bool Bonus => State.BonusLeft > 0;
        /// <summary>The pay factor of a passed inspection now (1 when none runs).</summary>
        public double PayFactor => Bonus ? Def.PassPayFactor : 1;
        /// <summary>The inspector's findings are waiting for a fix.</summary>
        public bool Failed => State.Failed;
        /// <summary>The fix deadline passed: the building is offline until the findings are fixed.</summary>
        public bool Closed => State.Closed;

        /// <summary>A calm line for the player (the Unity layer shows it as a notice).</summary>
        public event Action<string>? Alert;

        /// <summary>Advances at <c>inspection.tickHz</c>: the bonus, the walk, the deadline, and the roll for a new visit.</summary>
        public void Tick(double dt)
        {
            _sinceTick += dt;
            if (_sinceTick < 1.0 / Math.Max(0.01, Def.TickHz)) return;
            double elapsed = _sinceTick;
            _sinceTick = 0;
            InspectionState st = State;
            if (st.BonusLeft > 0)
            {
                st.BonusLeft = Math.Max(0, st.BonusLeft - elapsed);
                if (st.BonusLeft == 0) _s.MarkDirty();
            }
            if (st.WalkLeft > 0)
            {
                st.WalkLeft -= elapsed;
                if (st.WalkLeft <= 0) Inspect();
                return;
            }
            if (st.Failed)
            {
                FollowUp(elapsed);
                return;
            }
            // One visit at a time: none while a pass bonus runs (D161).
            if (st.BonusLeft > 0 || !_s.CommittedEvaluation.Online) return;
            double p = 1 - Math.Exp(-Def.ChancePerHour * elapsed / Units.SecondsPerHour);
            if (Roll() < p) Begin();
        }

        /// <summary>Sends an inspector now (the roll's outcome; also tests and dev tools).</summary>
        public void Begin()
        {
            if (State.WalkLeft > 0 || State.Failed) return;
            State.WalkLeft = Math.Max(1e-6, Def.InspectorWalkSeconds);
        }

        /// <summary>The time away: the bonus runs on (it is a reward, applied analytically); the walk and the deadline pause.</summary>
        /// <param name="seconds">Time credited, s.</param>
        /// <returns>True when the bonus ran out (the building's income changes).</returns>
        public bool AdvanceOffline(double seconds)
        {
            if (State.BonusLeft <= 0) return false;
            State.BonusLeft = Math.Max(0, State.BonusLeft - seconds);
            return State.BonusLeft <= 1e-9;
        }

        /// <summary>
        /// What an inspector would find in what is built now: every code on the code list (blocking or not) and every outlet
        /// above its code maximum. Empty = a pass.
        /// </summary>
        public List<string> Findings()
        {
            var list = new List<string>();
            BuildingEvaluation ev = _s.CommittedEvaluation;
            foreach (Codes.Violation v in ev.Violations) if (!list.Contains(v.Reason)) list.Add(v.Reason);
            int above = 0;
            string reason = "";
            foreach (OutletStatus o in ev.Outlets)
            {
                if (o.Zone != OutletZone.AboveCodeMax) continue;
                above++;
                reason = o.Def.CodeMaxReason.ToLowerInvariant();
            }
            if (above == 1) list.Add($"An outlet is above its code maximum ({reason})");
            else if (above > 1) list.Add($"{above} outlets are above their code maximum");
            return list;
        }

        /// <summary>A failed inspection's fine: max(<c>inspectionFailGrossMinutes</c> of gross, <c>inspectionFailMinimum</c> × tier factor), × 0.5 insured, $.</summary>
        public double FailFine()
        {
            FinesDef f = Data.Balancing.Fines;
            double fine = Math.Max(f.InspectionFailGrossMinutes * 60 * _s.CommittedEvaluation.PotentialGross,
                f.InspectionFailMinimum * Data.Balancing.Economy.TierFactor[_s.Layout.Tier - 1]);
            return _s.Game.City?.Utilities.Insurance == true ? fine * City.Utilities.InsuranceFineFactor(Data) : fine;
        }

        void Inspect()
        {
            InspectionState st = State;
            st.WalkLeft = 0;
            // A building that went offline while the inspector walked over is left for another day.
            if (!_s.CommittedEvaluation.Online) return;
            List<string> found = Findings();
            string symbol = Data.Game.CurrencySymbol;
            if (found.Count == 0)
            {
                st.BonusLeft = Def.PassMinutes * 60;
                _s.MarkDirty();
                Alert?.Invoke($"inspection passed · ×{Def.PassPayFactor:0.##} pay for {Def.PassMinutes:0} min");
                return;
            }
            double fine = Charge();
            st.Failed = true;
            st.Closed = false;
            st.DeadlineLeft = Def.FixDeadlineMinutes * 60;
            st.Findings = found;
            Alert?.Invoke($"inspection found {found.Count} problem{(found.Count == 1 ? "" : "s")} · fine {Units.FormatMoney(fine, symbol)} · fix within {Def.FixDeadlineMinutes:0} min");
        }

        /// <summary>After a fail: cleared once nothing is found; otherwise the deadline counts down (in game only).</summary>
        void FollowUp(double elapsed)
        {
            InspectionState st = State;
            List<string> found = Findings();
            if (found.Count == 0)
            {
                bool wasClosed = st.Closed;
                st.Failed = false;
                st.Closed = false;
                st.DeadlineLeft = 0;
                st.Findings.Clear();
                if (wasClosed) _s.MarkDirty();
                Alert?.Invoke(wasClosed ? "inspection findings fixed · the building can come online again" : "inspection findings fixed in time");
                return;
            }
            st.Findings = found;
            if (st.Closed) return;
            st.DeadlineLeft -= elapsed;
            if (st.DeadlineLeft > 0) return;
            st.DeadlineLeft = 0;
            st.Closed = true;
            double fine = Charge();
            _s.MarkDirty();
            Alert?.Invoke($"inspection deadline missed · second fine {Units.FormatMoney(fine, Data.Game.CurrencySymbol)} · offline until the findings are fixed");
        }

        double Charge()
        {
            double fine = FailFine();
            _s.Game.ApplyIncome(-fine);
            _s.Game.LifetimeIncome -= fine;
            return fine;
        }

        double Roll() => new Rng(Seeds.Derive(Seeds.Derive(_seed, "inspection"), State.Counter++)).NextDouble();
    }
}
