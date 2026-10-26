using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Core.Data
{
    /// <summary>
    /// Checks every loaded data file: id format, uniqueness, references between files,
    /// value ranges and curve shapes. Returns human-readable errors (empty = valid).
    /// </summary>
    public static class DataValidator
    {
        static readonly HashSet<string> MaterialKinds = new HashSet<string> { "duct", "pipe" };
        static readonly HashSet<string> FittingModels = new HashSet<string> { "fixed", "elbow", "tee", "reducer", "valve", "filter" };
        static readonly HashSet<string> SpeedControls = new HashSet<string> { "crank", "on-off", "variable" };
        static readonly HashSet<string> Placements = new HashSet<string> { "intake", "inline" };
        static readonly HashSet<string> PowerModels = new HashSet<string> { "none", "curve" };
        static readonly HashSet<string> Layers = new HashSet<string> { "floor", "ceiling" };
        static readonly HashSet<string> Utilities = new HashSet<string> { "air", "water", "gas", "heating", "power" };
        static readonly HashSet<string> Facings = new HashSet<string> { "north", "south", "east", "west" };
        static readonly HashSet<string> LinePatterns = new HashSet<string> { "solid", "dashed", "dotted", "double" };
        static readonly HashSet<string> Measured = new HashSet<string> { "flow", "pressure", "heat" };

        public static List<string> Validate(DataRepository data)
        {
            var errors = new List<string>(data.LoadIssues);
            ValidateGame(data, errors);
            ValidateBalancing(data, errors);
            ValidateFluids(data, errors);
            ValidateMaterials(data, errors);
            ValidatePipes(data, errors);
            ValidateFittings(data, errors);
            ValidateEquipment(data, errors);
            ValidateOutlets(data, errors);
            ValidateHandmade(data, errors);
            return errors;
        }

        static void Id(string file, string id, List<string> errors)
        {
            if (!Ids.IsValid(id)) errors.Add($"{file}: id '{id}' is not lowercase kebab");
        }

        static void Colour(string where, string hex, List<string> errors)
        {
            if (!HexColor.TryParse(hex, out _, out _, out _, out _)) errors.Add($"{where}: '{hex}' is not a #RRGGBB colour");
        }

        static void Positive(string where, double value, List<string> errors)
        {
            if (!(value > 0)) errors.Add($"{where} must be > 0 (is {value})");
        }

        static void Tier(string where, int tier, List<string> errors)
        {
            if (tier < 1 || tier > 5) errors.Add($"{where}: tier {tier} outside 1–5");
        }

        static void ValidateGame(DataRepository data, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(data.Game.DisplayName)) errors.Add("game.json: displayName is empty");
            ThemeDef t = data.Game.Theme;
            Colour("game.json theme.paper", t.Paper, errors);
            Colour("game.json theme.gridLines", t.GridLines, errors);
            Colour("game.json theme.walls", t.Walls, errors);
            Colour("game.json theme.text", t.Text, errors);
            Colour("game.json theme.warning", t.Warning, errors);
            Colour("game.json theme.danger", t.Danger, errors);
            foreach (var kv in t.RoomFills) Colour($"game.json theme.roomFills.{kv.Key}", kv.Value, errors);
            if (t.RoomFillAlpha < 0 || t.RoomFillAlpha > 1) errors.Add("game.json theme.roomFillAlpha outside 0–1");
        }

        static void ValidateBalancing(DataRepository data, List<string> errors)
        {
            BalancingDef b = data.Balancing;
            const string f = "balancing.json";
            Positive(f + " physics.gravity", b.Physics.Gravity, errors);
            Positive(f + " physics.gasConstant", b.Physics.GasConstant, errors);
            Positive(f + " physics.atmosphericPressurePa", b.Physics.AtmosphericPressurePa, errors);
            if (!(b.Physics.LaminarReynolds > 0 && b.Physics.TurbulentReynolds > b.Physics.LaminarReynolds))
                errors.Add(f + " physics: need 0 < laminarReynolds < turbulentReynolds");
            if (b.Solver.MaxIterations < 1) errors.Add(f + " solver.maxIterations must be ≥ 1");
            Positive(f + " solver.flowTolerance", b.Solver.FlowTolerance, errors);
            Positive(f + " solver.massImbalanceTolerance", b.Solver.MassImbalanceTolerance, errors);
            Positive(f + " solver.gMinFactor", b.Solver.GMinFactor, errors);
            Positive(f + " solver.zeroFlowThreshold", b.Solver.ZeroFlowThreshold, errors);
            if (!(b.Solver.Relaxation > 0 && b.Solver.Relaxation <= 1)) errors.Add(f + " solver.relaxation must be in (0, 1]");
            Positive(f + " solver.initialVelocity", b.Solver.InitialVelocity, errors);
            Positive(f + " solver.pcgTolerance", b.Solver.PcgTolerance, errors);
            if (b.Solver.PcgMaxIterations < 1) errors.Add(f + " solver.pcgMaxIterations must be ≥ 1");
            Positive(f + " building.floorHeight", b.Building.FloorHeight, errors);
            Positive(f + " heatmap.velocityAnchorMaxAir", b.Heatmap.VelocityAnchorMaxAir, errors);
            Positive(f + " heatmap.velocityAnchorMaxWater", b.Heatmap.VelocityAnchorMaxWater, errors);
            Positive(f + " heatmap.autoRangeStep", b.Heatmap.AutoRangeStep, errors);
            if (!b.Heatmap.Palettes.ContainsKey(b.Heatmap.DefaultPalette))
                errors.Add($"{f} heatmap.defaultPalette '{b.Heatmap.DefaultPalette}' not in palettes");
            foreach (var kv in b.Heatmap.Palettes)
            {
                if (kv.Value.Count < 2) errors.Add($"{f} heatmap.palettes.{kv.Key} needs ≥ 2 stops");
                foreach (string c in kv.Value) Colour($"{f} heatmap.palettes.{kv.Key}", c, errors);
            }
            MaterialDef? mat = data.FindMaterial(b.Drawing.DefaultMaterial);
            if (mat == null) errors.Add($"{f} drawing.defaultMaterial '{b.Drawing.DefaultMaterial}' unknown");
            PipeFamilyDef? fam = data.FindPipeFamily(b.Drawing.DefaultDuctFamily);
            if (fam == null) errors.Add($"{f} drawing.defaultDuctFamily '{b.Drawing.DefaultDuctFamily}' unknown");
            else if (fam.Sizes.Find(s => s.DiameterMm == b.Drawing.DefaultDuctSizeMm) == null)
                errors.Add($"{f} drawing.defaultDuctSizeMm {b.Drawing.DefaultDuctSizeMm} not a size of '{fam.Id}'");
            if (b.Drawing.MaxNodeDegree < 2) errors.Add(f + " drawing.maxNodeDegree must be ≥ 2");
            Positive(f + " particles.spacingMetres", b.Particles.SpacingMetres, errors);
            if (b.Particles.MaxParticles < 1) errors.Add(f + " particles.maxParticles must be ≥ 1");
            if (!(b.Camera.ZoomMin > 0 && b.Camera.ZoomMax > b.Camera.ZoomMin)) errors.Add(f + " camera: need 0 < zoomMin < zoomMax");
        }

        static void ValidateFluids(DataRepository data, List<string> errors)
        {
            foreach (FluidDef fl in data.Fluids)
            {
                Id("fluids.json", fl.Id, errors);
                Colour($"fluids.json '{fl.Id}' colour", fl.Colour, errors);
                if (!LinePatterns.Contains(fl.LinePattern)) errors.Add($"fluids.json '{fl.Id}': linePattern '{fl.LinePattern}' unknown");
                Positive($"fluids.json '{fl.Id}' molarMass", fl.MolarMass, errors);
                Positive($"fluids.json '{fl.Id}' gamma", fl.Gamma, errors);
                if (!string.IsNullOrEmpty(fl.Table) && data.FluidTables.TryGetValue(fl.Id, out FluidTableDef table))
                    ValidateTable(data, fl, table, errors);
            }
        }

        static void ValidateTable(DataRepository data, FluidDef fluid, FluidTableDef t, List<string> errors)
        {
            string f = fluid.Table;
            // Several fluids may share one table (hot water and heating water use the water table).
            if (data.FindFluid(t.Fluid) == null) errors.Add($"{f}: fluid '{t.Fluid}' unknown");
            if (string.IsNullOrWhiteSpace(t.Source)) errors.Add($"{f}: source is empty");
            Ascending(f + " temperaturesC", t.TemperaturesC, errors);
            Ascending(f + " pressuresPa", t.PressuresPa, errors);
            Grid(f, "density", t.Density, t, errors);
            Grid(f, "viscosity", t.Viscosity, t, errors);
            Grid(f, "specificHeat", t.SpecificHeat, t, errors);
            Grid(f, "conductivity", t.Conductivity, t, errors);
            Positive(f + " molarMass", t.MolarMass, errors);
            Positive(f + " gamma", t.Gamma, errors);
            Positive(f + " speedOfSound20C", t.SpeedOfSound20C, errors);
        }

        static void Ascending(string where, List<double> values, List<string> errors)
        {
            if (values.Count < 2) { errors.Add($"{where}: needs ≥ 2 values"); return; }
            for (int i = 1; i < values.Count; i++)
                if (!(values[i] > values[i - 1])) { errors.Add($"{where}: not strictly ascending at index {i}"); return; }
        }

        static void Grid(string file, string name, List<List<double>> grid, FluidTableDef t, List<string> errors)
        {
            if (grid.Count != t.PressuresPa.Count) { errors.Add($"{file} {name}: {grid.Count} rows, expected {t.PressuresPa.Count}"); return; }
            for (int i = 0; i < grid.Count; i++)
            {
                if (grid[i].Count != t.TemperaturesC.Count) { errors.Add($"{file} {name}[{i}]: {grid[i].Count} values, expected {t.TemperaturesC.Count}"); return; }
                foreach (double v in grid[i])
                    if (!(v > 0)) { errors.Add($"{file} {name}[{i}]: non-positive value {v}"); return; }
            }
        }

        static void ValidateMaterials(DataRepository data, List<string> errors)
        {
            foreach (MaterialDef m in data.Materials)
            {
                string w = $"materials.json '{m.Id}'";
                Id("materials.json", m.Id, errors);
                if (!MaterialKinds.Contains(m.Kind)) errors.Add($"{w}: kind '{m.Kind}' unknown");
                if (m.RoughnessMm < 0) errors.Add($"{w}: roughnessMm negative");
                Positive(w + " ratedPressurePa", m.RatedPressurePa, errors);
                Positive(w + " uUninsulated", m.UUninsulated, errors);
                Tier(w, m.Tier, errors);
                foreach (string fl in m.Fluids)
                    if (data.FindFluid(fl) == null) errors.Add($"{w}: fluid '{fl}' unknown");
            }
        }

        static void ValidatePipes(DataRepository data, List<string> errors)
        {
            foreach (PipeFamilyDef p in data.PipeFamilies)
            {
                string w = $"pipes.json '{p.Id}'";
                Id("pipes.json", p.Id, errors);
                if (!MaterialKinds.Contains(p.Kind)) errors.Add($"{w}: kind '{p.Kind}' unknown");
                if (p.Sizes.Count == 0) errors.Add($"{w}: no sizes");
                for (int i = 0; i < p.Sizes.Count; i++)
                {
                    Positive($"{w} sizes[{i}].diameterMm", p.Sizes[i].DiameterMm, errors);
                    if (p.Sizes[i].CapacityUnits < 1) errors.Add($"{w} sizes[{i}]: capacityUnits must be ≥ 1");
                    if (i > 0 && !(p.Sizes[i].DiameterMm > p.Sizes[i - 1].DiameterMm)) errors.Add($"{w}: sizes not ascending at {i}");
                }
            }
        }

        static void ValidateFittings(DataRepository data, List<string> errors)
        {
            foreach (FittingDef ft in data.Fittings)
            {
                string w = $"fittings.json '{ft.Id}'";
                Id("fittings.json", ft.Id, errors);
                Tier(w, ft.Tier, errors);
                if (!FittingModels.Contains(ft.Model)) { errors.Add($"{w}: model '{ft.Model}' unknown"); continue; }
                switch (ft.Model)
                {
                    case "fixed": Positive(w + " k", ft.K, errors); break;
                    case "valve":
                        Positive(w + " k", ft.K, errors);
                        if (!(ft.MinOpening > 0 && ft.MinOpening <= 1)) errors.Add($"{w}: minOpening must be in (0, 1]");
                        break;
                    case "elbow":
                        Positive(w + " kDuct", ft.KDuct, errors);
                        Positive(w + " kPipe", ft.KPipe, errors);
                        Positive(w + " angleDegrees", ft.AngleDegrees, errors);
                        break;
                    case "tee":
                        Positive(w + " kRun", ft.KRun, errors);
                        Positive(w + " kBranch", ft.KBranch, errors);
                        break;
                    case "reducer": Positive(w + " kContraction", ft.KContraction, errors); break;
                    case "filter": Positive(w + " ratedPressureDropPa", ft.RatedPressureDropPa, errors); break;
                }
            }
        }

        static void ValidateEquipment(DataRepository data, List<string> errors)
        {
            foreach (EquipmentDef e in data.Equipment)
            {
                string w = $"equipment.json '{e.Id}'";
                Id("equipment.json", e.Id, errors);
                Tier(w, e.Tier, errors);
                if (!SpeedControls.Contains(e.SpeedControl)) errors.Add($"{w}: speedControl '{e.SpeedControl}' unknown");
                if (!Placements.Contains(e.Placement)) errors.Add($"{w}: placement '{e.Placement}' unknown");
                if (!PowerModels.Contains(e.PowerModel)) errors.Add($"{w}: powerModel '{e.PowerModel}' unknown");
                if (!Layers.Contains(e.Footprint.Layer)) errors.Add($"{w}: footprint.layer '{e.Footprint.Layer}' unknown");
                if (e.Footprint.Width < 1 || e.Footprint.Height < 1) errors.Add($"{w}: footprint must be ≥ 1×1");
                foreach (string fl in e.Fluids)
                    if (data.FindFluid(fl) == null) errors.Add($"{w}: fluid '{fl}' unknown");
                Positive(w + " connectionDiameterMm", e.ConnectionDiameterMm, errors);
                Positive(w + " reverseK", e.ReverseK, errors);
                ValidateCurve(w, e, errors);
            }
        }

        static void ValidateCurve(string w, EquipmentDef e, List<string> errors)
        {
            List<CurvePointDef> c = e.Curve;
            if (c.Count < 5 || c.Count > 8) { errors.Add($"{w}: curve needs 5–8 points (has {c.Count})"); return; }
            if (c[0].FlowM3h != 0) errors.Add($"{w}: curve must start at Q = 0 (shutoff)");
            if (c[c.Count - 1].PressurePa != 0) errors.Add($"{w}: curve must end at Δp = 0 (Q_max)");
            for (int i = 0; i < c.Count; i++)
            {
                if (i > 0 && !(c[i].FlowM3h > c[i - 1].FlowM3h)) errors.Add($"{w}: curve flow not ascending at {i}");
                if (i > 0 && c[i].PressurePa > c[i - 1].PressurePa) errors.Add($"{w}: curve pressure rises at {i}");
                if (c[i].PressurePa < 0) errors.Add($"{w}: curve pressure negative at {i}");
                if (e.PowerModel == "curve" && !(c[i].Efficiency > 0 && c[i].Efficiency <= 1))
                    errors.Add($"{w}: curve efficiency at {i} must be in (0, 1]");
            }
        }

        static void ValidateOutlets(DataRepository data, List<string> errors)
        {
            foreach (OutletDef o in data.Outlets)
            {
                string w = $"outlets.json '{o.Id}'";
                Id("outlets.json", o.Id, errors);
                if (data.FindFluid(o.Fluid) == null) errors.Add($"{w}: fluid '{o.Fluid}' unknown");
                if (!Measured.Contains(o.Measured)) errors.Add($"{w}: measured '{o.Measured}' unknown");
                if (!string.IsNullOrEmpty(o.Grille))
                {
                    if (data.FindFitting(o.Grille) == null) errors.Add($"{w}: grille '{o.Grille}' unknown");
                    Positive(w + " grilleFaceAreaM2", o.GrilleFaceAreaM2, errors);
                }
                if (o.Tiers.Count == 0) errors.Add($"{w}: no tiers");
                foreach (var kv in o.Tiers)
                {
                    if (!int.TryParse(kv.Key, out int tier) || tier < 1 || tier > 5) errors.Add($"{w}: tier key '{kv.Key}' invalid");
                    OutletRequirementDef r = kv.Value;
                    if (!(r.Min > 0 && r.Ideal >= r.Min)) errors.Add($"{w} tier {kv.Key}: need 0 < min ≤ ideal");
                    if (r.Max != 0 && r.Max < r.Ideal) errors.Add($"{w} tier {kv.Key}: code max below ideal");
                }
            }
        }

        static void ValidateHandmade(DataRepository data, List<string> errors)
        {
            foreach (HandmadeBuildingDef b in data.HandmadeBuildings)
            {
                string w = $"handmade '{b.Id}'";
                Id("handmade", b.Id, errors);
                Tier(w, b.Tier, errors);
                if (b.Width < 1 || b.Height < 1) { errors.Add($"{w}: size must be ≥ 1×1"); continue; }
                if (b.Rooms.Count == 0) errors.Add($"{w}: no rooms");
                foreach (RoomDef r in b.Rooms)
                {
                    Id(w + " rooms", r.Id, errors);
                    if (r.X < 0 || r.Y < 0 || r.Width < 1 || r.Height < 1 || r.X + r.Width > b.Width || r.Y + r.Height > b.Height)
                        errors.Add($"{w}: room '{r.Id}' outside the footprint");
                    if (!data.Game.Theme.RoomFills.ContainsKey(r.Type)) errors.Add($"{w}: room '{r.Id}' type '{r.Type}' has no theme fill");
                }
                var openingIds = new HashSet<string>();
                foreach (EntryOpeningDef o in b.EntryOpenings)
                {
                    Id(w + " entryOpenings", o.Id, errors);
                    openingIds.Add(o.Id);
                    if (!Utilities.Contains(o.Utility)) errors.Add($"{w}: opening '{o.Id}' utility '{o.Utility}' unknown");
                    if (!Facings.Contains(o.Facing)) errors.Add($"{w}: opening '{o.Id}' facing '{o.Facing}' unknown");
                    bool onEdge = o.Facing switch
                    {
                        "west" => o.X == 0,
                        "east" => o.X == b.Width - 1,
                        "south" => o.Y == 0,
                        "north" => o.Y == b.Height - 1,
                        _ => false,
                    };
                    if (!onEdge || o.X < 0 || o.Y < 0 || o.X >= b.Width || o.Y >= b.Height)
                        errors.Add($"{w}: opening '{o.Id}' is not on its {o.Facing} wall");
                }
                foreach (OutletPlacementDef o in b.Outlets)
                {
                    Id(w + " outlets", o.Id, errors);
                    if (data.FindOutlet(o.Type) == null) errors.Add($"{w}: outlet '{o.Id}' type '{o.Type}' unknown");
                    if (o.X < 0 || o.Y < 0 || o.X >= b.Width || o.Y >= b.Height) errors.Add($"{w}: outlet '{o.Id}' outside the footprint");
                }
                foreach (EquipmentPlacementDef e in b.Equipment)
                {
                    if (data.FindEquipment(e.EquipmentId) == null) errors.Add($"{w}: equipment '{e.EquipmentId}' unknown");
                    if (!openingIds.Contains(e.OpeningId)) errors.Add($"{w}: equipment '{e.EquipmentId}' opening '{e.OpeningId}' unknown");
                }
            }
        }
    }
}
