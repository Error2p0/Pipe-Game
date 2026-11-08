using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Economy;

namespace HeadLoss.Game.Inspections
{
    /// <summary>What one kind of outlet on one network delivers in total, SI (flow m³/s, pressure Pa or heat W).</summary>
    public readonly struct DeliveryGroup
    {
        public readonly string Network;
        /// <summary>flow, pressure or heat.</summary>
        public readonly string Measured;
        public readonly double Total;
        public readonly int Outlets;

        public DeliveryGroup(string network, string measured, double total, int outlets)
        {
            Network = network;
            Measured = measured;
            Total = total;
            Outlets = outlets;
        }
    }

    /// <summary>The reference layout's power and delivery, network by network: what the energy rating compares against.</summary>
    public sealed class EnergyReference
    {
        /// <summary>Electrical input power per network, W.</summary>
        public readonly Dictionary<string, double> PowerByNetwork = new Dictionary<string, double>();
        public readonly List<DeliveryGroup> Deliveries = new List<DeliveryGroup>();

        /// <summary>Total electrical power of the reference, W.</summary>
        public double PowerW
        {
            get
            {
                double p = 0;
                foreach (double v in PowerByNetwork.Values) p += v;
                return p;
            }
        }

        /// <summary>From the reference layout's evaluation (the auto-router's last solve, every network at reference conditions).</summary>
        public static EnergyReference From(BuildingEvaluation reference)
        {
            var r = new EnergyReference();
            foreach (var kv in reference.PowerByNetwork) r.PowerByNetwork[kv.Key] = kv.Value;
            r.Deliveries.AddRange(EnergyRating.Deliveries(reference));
            return r;
        }
    }

    /// <summary>
    /// The energy rating A–G (<c>10-events.md</c> §Inspections): score = <c>P_actual / P_reference</c>, with the reference
    /// layout's power scaled to the building's current delivery. Network by network, the reference's power is multiplied
    /// by (delivery ÷ the reference's delivery) to the power of <c>inspection.ratingFlowExponent</c> (3: the affinity laws
    /// on the reference's own ducts and pipes) for flow and heat outlets and <c>ratingPressureExponent</c> (1.5) for
    /// pressure outlets (D159). Networks the reference runs without electricity (mains pressure, a gas regulator) add
    /// nothing, so a pump the player adds there counts against the grade. A reference that uses no electricity at all
    /// (the hand-cranked starter house) is not rated.
    /// </summary>
    public static class EnergyRating
    {
        /// <summary>"" when a building has no rating: its pay factor is 1.</summary>
        public const string NotRated = "";

        /// <summary>Outlet deliveries summed by network and measured quantity (outlets that are not connected count 0).</summary>
        public static List<DeliveryGroup> Deliveries(BuildingEvaluation ev)
        {
            var sums = new Dictionary<(string, string), (double total, int count)>();
            var order = new List<(string, string)>();
            foreach (OutletStatus o in ev.Outlets)
            {
                var key = (o.Def.Fluid, o.Def.Measured);
                if (!sums.TryGetValue(key, out var had)) { had = (0, 0); order.Add(key); }
                sums[key] = (had.total + Math.Max(0, o.Delivery), had.count + 1);
            }
            var list = new List<DeliveryGroup>();
            foreach (var key in order) list.Add(new DeliveryGroup(key.Item1, key.Item2, sums[key].total, sums[key].count));
            return list;
        }

        /// <summary>The reference's power at the delivery of an evaluation, W.</summary>
        public static double ScaledReferencePower(DataRepository data, EnergyReference reference, BuildingEvaluation actual)
        {
            InspectionDef d = data.Balancing.Inspection;
            List<DeliveryGroup> now = Deliveries(actual);
            double total = 0;
            foreach (var kv in reference.PowerByNetwork)
            {
                if (kv.Value <= 0) continue;
                // A network with several kinds of outlet scales by the outlet-weighted mean of their factors.
                double factor = 0, weight = 0;
                foreach (DeliveryGroup g in reference.Deliveries)
                {
                    if (g.Network != kv.Key || g.Total <= 0) continue;
                    double actualTotal = 0;
                    foreach (DeliveryGroup a in now) if (a.Network == g.Network && a.Measured == g.Measured) actualTotal = a.Total;
                    double k = g.Measured == "pressure" ? d.RatingPressureExponent : d.RatingFlowExponent;
                    factor += g.Outlets * Math.Pow(actualTotal / g.Total, k);
                    weight += g.Outlets;
                }
                total += kv.Value * (weight > 0 ? factor / weight : 1);
            }
            return total;
        }

        /// <summary>
        /// The score of an evaluation (its electrical power over the scaled reference's). False when the building cannot be
        /// rated: the reference uses no electricity, or the building is not online (it delivers nothing to compare).
        /// </summary>
        public static bool Score(DataRepository data, EnergyReference reference, BuildingEvaluation actual, out double score)
        {
            score = 0;
            double min = data.Balancing.Inspection.RatingMinReferenceW;
            if (reference.PowerW < min || !actual.Online) return false;
            double p = ScaledReferencePower(data, reference, actual);
            if (p < min) return false;
            score = actual.PowerW / p;
            return true;
        }

        /// <summary>The band a score falls in (the last band, max 0, takes everything above the others).</summary>
        public static RatingBandDef Band(DataRepository data, double score)
        {
            List<RatingBandDef> bands = data.Inspections.EnergyRating;
            foreach (RatingBandDef b in bands) if (b.MaxScore > 0 && score <= b.MaxScore) return b;
            return bands[bands.Count - 1];
        }

        /// <summary>The grade of an evaluation, or <see cref="NotRated"/>.</summary>
        public static string Grade(DataRepository data, EnergyReference reference, BuildingEvaluation actual, out double score) =>
            Score(data, reference, actual, out score) ? Band(data, score).Grade : NotRated;

        /// <summary>Pay factor of a grade (1 when not rated).</summary>
        public static double PayFactor(DataRepository data, string grade)
        {
            if (grade.Length == 0) return 1;
            foreach (RatingBandDef b in data.Inspections.EnergyRating) if (b.Grade == grade) return b.PayFactor;
            return 1;
        }
    }
}
