using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Generation;

namespace HeadLoss.Game.City
{
    /// <summary>The hover summary of a plot (<c>03-city.md</c> §Difficulty hint).</summary>
    public sealed class DifficultyHint
    {
        public string PlotId = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>The building can be generated and opened in this version.</summary>
        public bool Available;
        /// <summary>Why it cannot be opened yet ("" when available).</summary>
        public string UnavailableNote = "";
        /// <summary>Footprint, m.</summary>
        public int WidthM;
        public int HeightM;
        public int Floors = 1;
        /// <summary>Fluid ids the building needs.</summary>
        public readonly List<string> Fluids = new List<string>();
        public int Outlets;
        /// <summary>Sum of the outlets' minimum flow, m³/s.</summary>
        public double FlowDemand;
        /// <summary>Longest duct path from the entry opening to an outlet in the reference layout, m.</summary>
        public double MaxPathMetres;
        /// <summary>Codes in force.</summary>
        public int Codes;
        /// <summary>Distance to each needed facility (utility → m); air needs none.</summary>
        public readonly Dictionary<string, double> FacilityDistances = new Dictionary<string, double>();
        /// <summary>Difficulty estimate, 1–5 dots.</summary>
        public int Dots;
        /// <summary>Budget cap of the first build, $.</summary>
        public double BudgetCap;
        public bool Starter;
    }

    /// <summary>
    /// The buildings of a city, generated lazily from their plots (<c>03-city.md</c>: generated when first hovered
    /// or opened) and cached. Generation can run on a worker thread for hover summaries; everything it uses is
    /// read-only data and per-call state.
    /// </summary>
    public sealed class CityBuildings
    {
        public DataRepository Data { get; }
        public CityLayout City { get; }

        readonly Dictionary<string, GeneratedBuilding?> _cache = new Dictionary<string, GeneratedBuilding?>();
        readonly Dictionary<string, Task> _pending = new Dictionary<string, Task>();
        readonly object _lock = new object();

        public CityBuildings(DataRepository data, CityLayout city)
        {
            Data = data;
            City = city;
        }

        /// <summary>True when the plot's tier is generated in this version.</summary>
        public bool Available(Plot p) => Data.BuildingTier(p.Tier).Available;

        /// <summary>The plot's building, generated now if needed. Null for unknown plots and tiers not built yet.</summary>
        public GeneratedBuilding? Get(string plotId)
        {
            lock (_lock) if (_cache.TryGetValue(plotId, out GeneratedBuilding? g)) return g;
            Plot? p = City.FindPlot(plotId);
            GeneratedBuilding? made = p != null && Available(p) ? BuildingFactory.Create(Data, p, City.Districts[p.District].Kind) : null;
            lock (_lock) _cache[plotId] = made;
            return made;
        }

        /// <summary>The building if it is ready; otherwise starts generating it on a worker and returns null.</summary>
        public GeneratedBuilding? Peek(string plotId, out bool ready)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(plotId, out GeneratedBuilding? g)) { ready = true; return g; }
                if (!_pending.ContainsKey(plotId)) _pending[plotId] = Task.Run(() => Get(plotId));
            }
            ready = false;
            return null;
        }

        /// <summary>The difficulty hint; null while the building is still being generated (see <see cref="Peek"/>).</summary>
        public DifficultyHint? Hint(string plotId)
        {
            Plot? p = City.FindPlot(plotId);
            if (p == null) return null;
            BuildingTypeDef type = Data.BuildingType(p.BuildingType);
            BuildingTierDef tier = Data.BuildingTier(p.Tier);
            var h = new DifficultyHint
            {
                PlotId = p.Id,
                DisplayName = type.DisplayName,
                Tier = p.Tier,
                Available = tier.Available,
                UnavailableNote = tier.Available ? "" : tier.UnavailableNote,
                Floors = tier.FloorsMin,
                Starter = p.Starter,
            };
            if (!tier.Available)
            {
                h.Fluids.AddRange(tier.Fluids);
                h.Dots = Math.Min(5, p.Tier + 1);
                h.WidthM = tier.FootprintMax[0];
                h.HeightM = tier.FootprintMax[1];
                AddFacilities(h, p);
                return h;
            }
            GeneratedBuilding? g = Peek(plotId, out bool ready);
            if (!ready) return null;
            if (g == null) return h;
            BuildingLayout l = g.Layout;
            h.DisplayName = l.DisplayName;
            h.Tier = l.Tier;
            h.WidthM = l.Width;
            h.HeightM = l.Height;
            h.Outlets = l.Outlets.Count;
            h.Codes = l.Codes.Count;
            h.MaxPathMetres = g.Reference.MaxPathMetres;
            h.BudgetCap = g.BudgetCap;
            foreach (var o in l.Outlets)
            {
                OutletDef def = Data.Outlet(o.Type);
                if (!h.Fluids.Contains(def.Fluid)) h.Fluids.Add(def.Fluid);
                h.FlowDemand += BuildingEconomy.Requirement(Data, l, o).Min;
            }
            AddFacilities(h, p);
            h.Dots = Dots(Data.Balancing.Difficulty, h);
            return h;
        }

        void AddFacilities(DifficultyHint h, Plot p)
        {
            foreach (string fluid in h.Fluids)
            {
                string utility = UtilityOf(fluid);
                if (utility.Length == 0) continue;
                double best = double.MaxValue;
                foreach (FacilityPlacement f in City.Facilities)
                    if (f.Utility == utility) best = Math.Min(best, City.DistanceMetres(p, f));
                if (best < double.MaxValue) h.FacilityDistances[utility] = best;
            }
        }

        /// <summary>The city utility that supplies a fluid ("" for air, which comes from the building's own intake).</summary>
        public static string UtilityOf(string fluid) => fluid switch
        {
            "air" => "",
            "water" => "water",
            "hot-water" => "water",
            "natural-gas" => "gas",
            "heating-water" => "heating",
            _ => "special-gas",
        };

        /// <summary>1–5 dots = round(demand / demandRef + path / pathRef + (floors − 1) × floorWeight + codes × codeWeight).</summary>
        public static int Dots(DifficultyDef d, DifficultyHint h)
        {
            double score = Units.M3sToM3h(h.FlowDemand) / d.DemandReferenceM3h + h.MaxPathMetres / d.PathReferenceMetres
                + (h.Floors - 1) * d.FloorWeight + h.Codes * d.CodeWeight;
            return Math.Max(1, Math.Min(5, (int)Math.Round(score)));
        }
    }
}
