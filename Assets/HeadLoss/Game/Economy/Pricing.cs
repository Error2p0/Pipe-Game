using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Economy
{
    /// <summary>What a Commit costs, split for the breakdown. All values in $.</summary>
    public sealed class CostSummary
    {
        /// <summary>New parts at full price plus the price difference of upgrades in place.</summary>
        public double Charge;
        /// <summary>Refunds for removed (or downgraded) committed parts.</summary>
        public double Refund;
        public double DuctCharge;
        public double FittingCharge;
        public double EquipmentCharge;
        /// <summary>Places (duct metres, fitting tiles, equipment slots) that differ.</summary>
        public int Changes;

        /// <summary>Money the Commit takes from the wallet (negative = money back).</summary>
        public double Net => Charge - Refund;
    }

    /// <summary>
    /// Prices (<c>13-balancing.md</c> §Prices) and the cost of committing a design. The diff is geometric: each
    /// metre of duct, each fitting tile and each equipment slot is compared between the committed state and the
    /// design. A part only in the design costs its full price; a part only in the committed state refunds
    /// <c>refundFraction</c>; a duct or fitting replaced in place (a larger size, a long-radius elbow) costs the
    /// price difference (<c>07-equipment.md</c> §Network upgrades), or refunds the fraction of it when cheaper.
    /// Equipment swaps count as a removal plus a purchase.
    /// </summary>
    public static class Pricing
    {
        /// <summary>Duct or pipe price per metre: base × material factor × (D / D_ref)^exponent, $/m.</summary>
        /// <param name="diameterMm">mm.</param>
        public static double PerMetre(DataRepository data, string materialId, double diameterMm)
        {
            PricesDef p = data.Balancing.Prices;
            MaterialDef m = data.Material(materialId);
            bool duct = m.Kind == "duct";
            double basePrice = duct ? p.DuctBasePerMetre : p.PipeBasePerMetre;
            double dRef = duct ? p.DuctReferenceDiameterMm : p.PipeReferenceDiameterMm;
            return basePrice * m.PriceFactor * Math.Pow(diameterMm / dRef, p.DiameterExponent);
        }

        /// <summary>Fitting price, × D / D_ref for sizes above the reference, $.</summary>
        /// <param name="diameterMm">Size of the fitting, mm.</param>
        /// <param name="duct">Duct (true) or pipe reference diameter.</param>
        public static double Fitting(DataRepository data, string fittingId, double diameterMm, bool duct)
        {
            PricesDef p = data.Balancing.Prices;
            double dRef = duct ? p.DuctReferenceDiameterMm : p.PipeReferenceDiameterMm;
            return data.Fitting(fittingId).Price * Math.Max(1.0, diameterMm / dRef);
        }

        /// <summary>Catalogue value of everything in a state, $ (pre-placed equipment counts 0).</summary>
        public static double Value(DataRepository data, BuildingLayout layout, BuildingState s)
        {
            Bill b = Bill.Of(data, layout, s);
            double v = 0;
            foreach (var kv in b.Ducts) v += kv.Value.Price;
            foreach (var kv in b.Fittings) v += kv.Value.Price;
            foreach (var kv in b.Equipment) v += kv.Value.Price;
            return v;
        }

        /// <summary>Cost of turning <paramref name="committed"/> into <paramref name="design"/>.</summary>
        public static CostSummary Diff(DataRepository data, BuildingLayout layout, BuildingState committed, BuildingState design)
        {
            double refund = data.Balancing.Economy.RefundFraction;
            Bill a = Bill.Of(data, layout, committed), b = Bill.Of(data, layout, design);
            var c = new CostSummary();
            Compare(a.Ducts, b.Ducts, refund, true, c, ref c.DuctCharge);
            Compare(a.Fittings, b.Fittings, refund, true, c, ref c.FittingCharge);
            Compare(a.Equipment, b.Equipment, refund, false, c, ref c.EquipmentCharge);
            return c;
        }

        static void Compare<TKey>(Dictionary<TKey, Item> before, Dictionary<TKey, Item> after, double refundFraction,
            bool upgradeInPlace, CostSummary c, ref double charge) where TKey : notnull
        {
            foreach (var kv in after)
            {
                if (!before.TryGetValue(kv.Key, out Item old))
                {
                    c.Charge += kv.Value.Price;
                    charge += kv.Value.Price;
                    c.Changes++;
                    continue;
                }
                if (old.Spec == kv.Value.Spec) continue;
                c.Changes++;
                if (upgradeInPlace)
                {
                    double d = kv.Value.Price - old.Price;
                    if (d >= 0) { c.Charge += d; charge += d; }
                    else c.Refund += -d * refundFraction;
                }
                else
                {
                    c.Refund += old.Price * refundFraction;
                    c.Charge += kv.Value.Price;
                    charge += kv.Value.Price;
                }
            }
            foreach (var kv in before)
            {
                if (after.ContainsKey(kv.Key)) continue;
                c.Refund += kv.Value.Price * refundFraction;
                c.Changes++;
            }
        }

        /// <summary>A priced part at a place; <see cref="Spec"/> identifies what it is (equal spec = unchanged).</summary>
        readonly struct Item
        {
            public readonly string Spec;
            public readonly double Price;

            public Item(string spec, double price)
            {
                Spec = spec;
                Price = price;
            }
        }

        /// <summary>A state's parts by place: duct metres by tile edge, fittings and inline parts by tile, equipment by opening.</summary>
        sealed class Bill
        {
            public readonly Dictionary<(TilePos, TilePos), Item> Ducts = new Dictionary<(TilePos, TilePos), Item>();
            public readonly Dictionary<TilePos, Item> Fittings = new Dictionary<TilePos, Item>();
            public readonly Dictionary<string, Item> Equipment = new Dictionary<string, Item>();

            public static Bill Of(DataRepository data, BuildingLayout layout, BuildingState s)
            {
                var bill = new Bill();
                DuctNetworkState net = s.Air;
                foreach (DuctSegment seg in net.Segments)
                {
                    TilePos a = net.FindNode(seg.NodeA)!.Pos, b = net.FindNode(seg.NodeB)!.Pos;
                    (int dx, int dy) = a.DirectionTo(b);
                    double perMetre = PerMetre(data, seg.MaterialId, seg.DiameterMm);
                    string spec = $"{seg.MaterialId}/{seg.DiameterMm}";
                    for (TilePos t = a; t != b; )
                    {
                        var next = new TilePos(t.X + dx, t.Y + dy);
                        bill.Ducts[Edge(t, next)] = new Item(spec, perMetre);
                        t = next;
                    }
                }

                var adjacent = new List<DuctSegment>(4);
                foreach (DuctNode n in net.Nodes)
                {
                    adjacent.Clear();
                    foreach (DuctSegment seg in net.Segments)
                        if (seg.NodeA == n.Id || seg.NodeB == n.Id) adjacent.Add(seg);
                    if (adjacent.Count == 0) continue;
                    double dMax = 0;
                    foreach (DuctSegment seg in adjacent) dMax = Math.Max(dMax, seg.DiameterMm);
                    bool duct = data.Material(adjacent[0].MaterialId).Kind == "duct";
                    var specs = new List<string>();
                    double price = 0;
                    void Add(string fittingId, double size)
                    {
                        specs.Add($"{fittingId}/{size}");
                        price += Fitting(data, fittingId, size, duct);
                    }

                    if (n.Kind == DuctNodeKind.Free)
                    {
                        if (adjacent.Count == 2 && DuctEditor.IsElbow(net, n)) Add(n.SmoothFittings ? "elbow-90-long-radius" : "elbow-90-sharp", dMax);
                        else if (adjacent.Count == 3) Add("tee-sharp", dMax);
                        else if (adjacent.Count >= 4) Add("cross-sharp", dMax);
                    }
                    else if (n.Kind == DuctNodeKind.Inline)
                    {
                        EquipmentDef? fan = data.FindEquipment(n.PartId);
                        if (fan != null)
                        {
                            specs.Add(fan.Id);
                            price += fan.Price;
                        }
                        else Add(n.PartId, dMax);
                    }
                    if (n.Kind == DuctNodeKind.Free || n.Kind == DuctNodeKind.Inline)
                        foreach (DuctSegment seg in adjacent)
                            if (seg.DiameterMm < dMax) Add("reducer", dMax);
                    if (specs.Count == 0) continue;
                    specs.Sort(StringComparer.Ordinal);
                    bill.Fittings[n.Pos] = new Item(string.Join("+", specs), price);
                }

                foreach (EquipmentState e in s.Equipment)
                {
                    bool preplaced = false;
                    foreach (EquipmentPlacementDef p in layout.InitialEquipment)
                        if (p.OpeningId == e.OpeningId && p.EquipmentId == e.EquipmentId) preplaced = true;
                    bill.Equipment[e.OpeningId] = new Item(e.EquipmentId, preplaced ? 0 : data.EquipmentItem(e.EquipmentId).Price);
                }
                return bill;
            }

            static (TilePos, TilePos) Edge(TilePos a, TilePos b) =>
                a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a);
        }
    }
}
