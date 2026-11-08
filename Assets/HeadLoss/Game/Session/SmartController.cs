using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;

namespace HeadLoss.Game.Session
{
    /// <summary>One setting the controller changed: where, what, from and to.</summary>
    public readonly struct ControllerChange
    {
        /// <summary>Network of an inline part, or "" for the device at an opening.</summary>
        public readonly string Fluid;
        public readonly TilePos Tile;
        public readonly string OpeningId;
        /// <summary>opening (a damper or balancing valve) or speed.</summary>
        public readonly string Kind;
        public readonly double From, To;
        public readonly string Label;

        public ControllerChange(string fluid, TilePos tile, string openingId, string kind, double from, double to, string label)
        {
            Fluid = fluid;
            Tile = tile;
            OpeningId = openingId;
            Kind = kind;
            From = from;
            To = to;
            Label = label;
        }
    }

    /// <summary>
    /// The smart building controller (<c>07-equipment.md</c> §Building equipment): coordinate descent over every damper and
    /// balancing valve opening (± <c>openingStep</c>) and every variable speed (± <c>speedStep</c>), solving what is built at
    /// steady state for each try, keeping a change only when it raises the building's net income without more outlets below
    /// their minimum or above their code maximum than before. At most <c>maxSolves</c> solves a run. Pure: it works on a copy
    /// and returns the changes, so it can run on a worker.
    /// </summary>
    public static class SmartController
    {
        sealed class Variable
        {
            public string Fluid = "";
            public TilePos Tile;
            public string OpeningId = "";
            public string Kind = "";
            public double Min, Max, Step;
            public Func<double> Get = null!;
            public Action<double> Set = null!;
            public string Label = "";
        }

        static List<Variable> Variables(DataRepository data, BuildingState s, MetaUpgradeDef def)
        {
            var list = new List<Variable>();
            foreach (EquipmentState e in s.Equipment)
            {
                EquipmentDef? d = data.FindEquipment(e.EquipmentId);
                if (d == null || !e.On || !(e.Vsd || d.SpeedControl == "variable")) continue;
                EquipmentState eq = e;
                list.Add(new Variable { OpeningId = e.OpeningId, Kind = "speed", Min = 0.2, Max = 1, Step = def.SpeedStep, Get = () => eq.Speed, Set = v => eq.Speed = v, Label = d.DisplayName });
            }
            foreach (DuctNetworkState net in s.Networks)
                foreach (DuctNode n in net.Nodes)
                {
                    if (n.Kind != DuctNodeKind.Inline) continue;
                    DuctNode node = n;
                    EquipmentDef? eq = data.FindEquipment(n.PartId);
                    if (eq != null)
                    {
                        if (n.On && (n.Vsd || eq.SpeedControl == "variable"))
                            list.Add(new Variable { Fluid = net.FluidId, Tile = n.Pos, Kind = "speed", Min = 0.2, Max = 1, Step = def.SpeedStep, Get = () => node.Speed, Set = v => node.Speed = v, Label = eq.DisplayName });
                        continue;
                    }
                    FittingDef? f = data.FindFitting(n.PartId);
                    if (f == null || f.Model != "valve" || f.MinOpening >= 1) continue;
                    list.Add(new Variable { Fluid = net.FluidId, Tile = n.Pos, Kind = "opening", Min = f.MinOpening, Max = 1, Step = def.OpeningStep, Get = () => node.Opening, Set = v => node.Opening = v, Label = f.DisplayName });
                }
            return list;
        }

        /// <summary>Runs one optimisation on a copy of <paramref name="built"/>.</summary>
        /// <param name="evaluate">Solves a state at steady state and evaluates it (thread-safe for one caller).</param>
        /// <param name="payFactor">Factors on gross pay after the outlets (rating, inspection, prestige, certificate).</param>
        /// <param name="electricityFactor">Factor on electricity (prestige).</param>
        /// <param name="solves">Solves used.</param>
        public static List<ControllerChange> Run(DataRepository data, BuildingState built, MetaUpgradeDef def, Func<BuildingState, BuildingEvaluation> evaluate,
            double payFactor, double electricityFactor, out int solves)
        {
            BuildingState s = built.Clone();
            List<Variable> vars = Variables(data, s, def);
            var start = new Dictionary<Variable, double>();
            foreach (Variable v in vars) start[v] = v.Get();
            solves = 0;
            var changes = new List<ControllerChange>();
            if (vars.Count == 0 || def.MaxSolves <= 1) return changes;
            BuildingEvaluation baseline = evaluate(s);
            solves++;
            int below = baseline.BelowMinimum, above = baseline.AboveCodeMax;
            double Score(BuildingEvaluation ev) =>
                ev.BelowMinimum > below || ev.AboveCodeMax > above ? double.NegativeInfinity : ev.Gross * payFactor - ev.Electricity * electricityFactor;
            double best = Score(baseline);
            bool improved = true;
            while (improved && solves < def.MaxSolves)
            {
                improved = false;
                foreach (Variable v in vars)
                {
                    foreach (int sign in new[] { 1, -1 })
                    {
                        if (solves >= def.MaxSolves) break;
                        double was = v.Get();
                        double next = Math.Round(Math.Max(v.Min, Math.Min(v.Max, was + sign * v.Step)) * 1000) / 1000;
                        if (Math.Abs(next - was) < 1e-9) continue;
                        v.Set(next);
                        double score = Score(evaluate(s));
                        solves++;
                        if (score > best + 1e-9)
                        {
                            best = score;
                            improved = true;
                            break;
                        }
                        v.Set(was);
                    }
                }
            }
            foreach (Variable v in vars)
                if (Math.Abs(v.Get() - start[v]) > 1e-9)
                    changes.Add(new ControllerChange(v.Fluid, v.Tile, v.OpeningId, v.Kind, start[v], v.Get(), v.Label));
            return changes;
        }

        /// <summary>Applies changes to a state: the matching damper, valve or speed, if it is still there.</summary>
        public static void Apply(DataRepository data, BuildingState s, IReadOnlyList<ControllerChange> changes)
        {
            foreach (ControllerChange c in changes)
            {
                if (c.OpeningId.Length > 0)
                {
                    if (s.EquipmentAt(c.OpeningId) is EquipmentState e && c.Kind == "speed") e.Speed = c.To;
                    continue;
                }
                if (s.Net(c.Fluid).NodeAt(c.Tile) is not DuctNode n || n.Kind != DuctNodeKind.Inline) continue;
                if (c.Kind == "speed") n.Speed = c.To;
                else n.Opening = c.To;
            }
        }

        /// <summary>A log line for a change: "Damper at (4, 7): 60% → 55%".</summary>
        public static string Describe(ControllerChange c)
        {
            string where = c.OpeningId.Length > 0 ? "" : $" at ({c.Tile.X}, {c.Tile.Y}{(c.Tile.F != 0 ? $", floor {c.Tile.F}" : "")})";
            string what = c.Kind == "speed" ? " speed" : "";
            return $"{c.Label}{what}{where}: {c.From * 100:0}% → {c.To * 100:0}%";
        }
    }
}
