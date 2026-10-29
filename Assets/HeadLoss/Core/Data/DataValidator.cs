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
        static readonly HashSet<string> Placements = new HashSet<string> { "intake", "inline", "addon" };
        static readonly HashSet<string> PowerModels = new HashSet<string> { "none", "curve" };
        static readonly HashSet<string> Layers = new HashSet<string> { "floor", "ceiling" };
        static readonly HashSet<string> Utilities = new HashSet<string> { "air", "water", "gas", "heating", "power" };
        static readonly HashSet<string> Facings = new HashSet<string> { "north", "south", "east", "west" };
        static readonly HashSet<string> LinePatterns = new HashSet<string> { "solid", "dashed", "dotted", "double" };
        static readonly HashSet<string> Measured = new HashSet<string> { "flow", "pressure", "heat" };
        static readonly HashSet<string> OutletUnits = new HashSet<string> { "m3/h", "bar", "mbar", "W" };

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
            ValidateCodes(data, errors);
            ValidateHandmade(data, errors);
            ValidateCity(data, errors);
            ValidateBuildings(data, errors);
            ValidateUpgrades(data, errors);
            if (data.Balancing.Progression.UnlockOnline.Count != 4) errors.Add("balancing.json: progression.unlockOnline needs 4 entries (tier 1→2 … 4→5)");
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
            Colour("game.json theme.good", t.Good, errors);
            if (string.IsNullOrWhiteSpace(data.Game.CurrencySymbol)) errors.Add("game.json: currencySymbol is empty");
            foreach (string id in data.Game.Toolbox.Parts)
            {
                bool fan = data.FindEquipment(id)?.Placement == "inline";
                bool valve = data.FindFitting(id)?.Model == "valve";
                if (!fan && !valve) errors.Add($"game.json toolbox: '{id}' is not an inline fan or valve");
            }
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
            Positive(f + " building.budgetCapFactor", b.Building.BudgetCapFactor, errors);

            EconomyDef e = b.Economy;
            Positive(f + " economy.overdeliveryCapRatio", e.OverdeliveryCapRatio, errors);
            if (!(e.CapMultiplier >= 1)) errors.Add(f + " economy.capMultiplier must be ≥ 1");
            if (!(e.CapMultiplierRisky >= e.CapMultiplier)) errors.Add(f + " economy.capMultiplierRisky must be ≥ capMultiplier");
            Fraction(f + " economy.offlineBurstFactor", e.OfflineBurstFactor, errors);
            Fraction(f + " economy.inGameBurstFactor", e.InGameBurstFactor, errors);
            Fraction(f + " economy.refundFraction", e.RefundFraction, errors);
            Positive(f + " economy.electricityPricePerKWs", e.ElectricityPricePerKWs, errors);
            Positive(f + " economy.incomeCycleSeconds", e.IncomeCycleSeconds, errors);
            Positive(f + " economy.tickHz", e.TickHz, errors);
            if (e.StartingWallet < 0) errors.Add(f + " economy.startingWallet negative");
            PerTier(f + " economy.basePay", e.BasePay, errors);
            PerTier(f + " economy.tierFactor", e.TierFactor, errors);
            PerTier(f + " economy.electricityTierFactor", e.ElectricityTierFactor, errors);

            Fraction(f + " crank.speedPerClick", b.Crank.SpeedPerClick, errors);
            Positive(f + " crank.decaySeconds", b.Crank.DecaySeconds, errors);
            Positive(f + " crank.holdClicksPerSecond", b.Crank.HoldClicksPerSecond, errors);
            Positive(f + " crank.resolveSpeedStep", b.Crank.ResolveSpeedStep, errors);

            Positive(f + " prices.ductBasePerMetre", b.Prices.DuctBasePerMetre, errors);
            Positive(f + " prices.ductReferenceDiameterMm", b.Prices.DuctReferenceDiameterMm, errors);
            Positive(f + " prices.pipeBasePerMetre", b.Prices.PipeBasePerMetre, errors);
            Positive(f + " prices.pipeReferenceDiameterMm", b.Prices.PipeReferenceDiameterMm, errors);
            Positive(f + " prices.diameterExponent", b.Prices.DiameterExponent, errors);

            Positive(f + " save.autosaveSeconds", b.Save.AutosaveSeconds, errors);
            if (b.Save.BackupCount < 0) errors.Add(f + " save.backupCount negative");
            Positive(f + " save.gzipThresholdBytes", b.Save.GzipThresholdBytes, errors);
            Positive(f + " offline.maxHours", b.Offline.MaxHours, errors);

            if (b.Targets.StarterCrankPaysFanMinutes.Count != 2) errors.Add(f + " targets.starterCrankPaysFanMinutes needs [low, high]");
            PerTier(f + " targets.paybackMinutes", b.Targets.PaybackMinutes, errors);
            if (b.Targets.ElectricityShare.Count != 2) errors.Add(f + " targets.electricityShare needs [low, high]");

            AudioDef a = b.Audio;
            Fraction(f + " audio.masterVolume", a.MasterVolume, errors);
            Fraction(f + " audio.airflowGain", a.AirflowGain, errors);
            Fraction(f + " audio.humGain", a.HumGain, errors);
            Fraction(f + " audio.clickGain", a.ClickGain, errors);
            Fraction(f + " audio.limiterCeiling", a.LimiterCeiling, errors);
            Positive(f + " audio.airflowFullM3h", a.AirflowFullM3h, errors);
            if (!(a.AirflowCutoffMinHz > 0 && a.AirflowCutoffMaxHz > a.AirflowCutoffMinHz)) errors.Add(f + " audio: need 0 < airflowCutoffMinHz < airflowCutoffMaxHz");
            if (!(a.HumMinHz > 0 && a.HumMaxHz > a.HumMinHz)) errors.Add(f + " audio: need 0 < humMinHz < humMaxHz");
        }

        static void Fraction(string where, double value, List<string> errors)
        {
            if (!(value >= 0 && value <= 1)) errors.Add($"{where} must be in [0, 1] (is {value})");
        }

        static void PerTier(string where, List<double> values, List<string> errors)
        {
            if (values.Count != 5) { errors.Add($"{where} needs 5 values (tiers 1–5), has {values.Count}"); return; }
            foreach (double v in values)
                if (!(v > 0)) { errors.Add($"{where} values must be > 0"); return; }
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
                Positive(w + " priceFactor", m.PriceFactor, errors);
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
                if (ft.Price < 0) errors.Add($"{w}: price negative");
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
                if (e.Price < 0) errors.Add($"{w}: price negative");
                if (e.Placement == "addon")
                {
                    if (!(e.EfficiencyFactor > 0 && e.EfficiencyFactor <= 1)) errors.Add($"{w}: efficiencyFactor must be in (0, 1]");
                    if (e.FitsCategories.Count == 0) errors.Add($"{w}: add-on fits no category");
                    continue;
                }
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
                if (!OutletUnits.Contains(o.Unit)) errors.Add($"{w}: unit '{o.Unit}' unknown");
                Positive(w + " payFactor", o.PayFactor, errors);
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

        static readonly HashSet<string> CodeKinds = new HashSet<string> { "layout", "operating", "city" };

        static void ValidateCodes(DataRepository data, List<string> errors)
        {
            foreach (CodeDef c in data.Codes)
            {
                string w = $"codes.json '{c.Id}'";
                Id("codes.json", c.Id, errors);
                if (!CodeKinds.Contains(c.Kind)) errors.Add($"{w}: kind '{c.Kind}' unknown");
                if (c.Tiers.Count == 0 && c.BuildingTypes.Count == 0) errors.Add($"{w}: in force nowhere (no tiers, no building types)");
                foreach (int t in c.Tiers) Tier(w, t, errors);
                if (string.IsNullOrWhiteSpace(c.Reason) || string.IsNullOrWhiteSpace(c.Why)) errors.Add($"{w}: reason and why are needed for the requirement sheet");
            }
        }

        static void ValidateHandmade(DataRepository data, List<string> errors)
        {
            foreach (BuildingLayoutDef b in data.HandmadeBuildings) ValidateLayout($"handmade '{b.Id}'", b, data, errors);
        }

        /// <summary>Checks one building layout (hand-made or generated).</summary>
        public static void ValidateLayout(string w, BuildingLayoutDef b, DataRepository data, List<string> errors)
        {
            {
                Id(w, b.Id, errors);
                Tier(w, b.Tier, errors);
                if (b.Width < 1 || b.Height < 1) { errors.Add($"{w}: size must be ≥ 1×1"); return; }
                if (b.Rooms.Count == 0) errors.Add($"{w}: no rooms");
                foreach (RoomDef r in b.Rooms)
                {
                    Id(w + " rooms", r.Id, errors);
                    if (r.X < 0 || r.Y < 0 || r.Width < 1 || r.Height < 1 || r.X + r.Width > b.Width || r.Y + r.Height > b.Height)
                        errors.Add($"{w}: room '{r.Id}' outside the footprint");
                    if (data.FindRoomType(r.Type) == null) errors.Add($"{w}: room '{r.Id}' type '{r.Type}' unknown");
                    if (r.Occupants < 0 || r.MachinesW < 0) errors.Add($"{w}: room '{r.Id}' loads must be ≥ 0");
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
                    if (o.Min > 0 && !(o.Min < o.Ideal && (o.Max == 0 || o.Ideal <= o.Max))) errors.Add($"{w}: outlet '{o.Id}' needs min < ideal ≤ max");
                    if (o.Facing.Length > 0 && !Facings.Contains(o.Facing)) errors.Add($"{w}: outlet '{o.Id}' facing '{o.Facing}' unknown");
                    if (RoomAt(b, o.X, o.Y) < 0) errors.Add($"{w}: outlet '{o.Id}' is not in a room");
                }
                foreach (WindowDef win in b.Windows)
                    if (!Facings.Contains(win.Side) || RoomAt(b, win.X, win.Y) < 0) errors.Add($"{w}: window at ({win.X}, {win.Y}) {win.Side} is not on a room wall");
                foreach (string code in b.Codes)
                    if (data.FindCode(code) == null) errors.Add($"{w}: code '{code}' unknown");
                if (b.ReferenceCrankSpeed < 0 || b.ReferenceCrankSpeed > 1) errors.Add($"{w}: referenceCrankSpeed outside 0–1");
                foreach (EquipmentPlacementDef e in b.Equipment) Placement(w, e, data, openingIds, errors);
                ValidateDoors(w, b, errors);
            }
        }

        static void Placement(string w, EquipmentPlacementDef e, DataRepository data, HashSet<string> openingIds, List<string> errors)
        {
            EquipmentDef? def = data.FindEquipment(e.EquipmentId);
            if (def == null) errors.Add($"{w}: equipment '{e.EquipmentId}' unknown");
            else if (def.Placement != "intake") errors.Add($"{w}: equipment '{e.EquipmentId}' does not go at an opening");
            if (!openingIds.Contains(e.OpeningId)) errors.Add($"{w}: equipment '{e.EquipmentId}' opening '{e.OpeningId}' unknown");
        }

        static readonly string[] DistrictKinds = { "residential", "commercial", "industrial" };

        static void ValidateCity(DataRepository data, List<string> errors)
        {
            CityDef c = data.City;
            const string w = "city.json";
            if (c.Width < 2 * c.EdgeBand + 16 || c.Height < 2 * c.EdgeBand + 16) errors.Add($"{w}: map too small for its edge band");
            Positive(w + " tileMetres", c.TileMetres, errors);
            if (c.Districts.SeedPointsMin < 3 || c.Districts.SeedPointsMax < c.Districts.SeedPointsMin) errors.Add($"{w}: district seed points need 3 ≤ min ≤ max");
            foreach (string k in DistrictKinds)
                if (!c.Districts.Weights.ContainsKey(k)) errors.Add($"{w}: district weight '{k}' missing");
            if (c.Roads.MainMin < 1 || c.Roads.MainMax < c.Roads.MainMin) errors.Add($"{w}: main roads need 1 ≤ min ≤ max");
            if (c.Roads.LocalSpacingMin < 2 || c.Roads.LocalSpacingMax < c.Roads.LocalSpacingMin) errors.Add($"{w}: local road spacing needs 2 ≤ min ≤ max");
            foreach (CityAreaRuleDef a in new[] { c.Parks, c.ProtectedSites, c.Historic })
            {
                if (a.Min < 0 || a.Max < a.Min || a.SizeMin < 1 || a.SizeMax < a.SizeMin) errors.Add($"{w}: area rule ranges invalid");
                foreach (string k in a.Districts) if (System.Array.IndexOf(DistrictKinds, k) < 0) errors.Add($"{w}: district '{k}' unknown");
            }
            foreach (FacilityTypeDef f in c.Facilities)
            {
                Id(w + " facilities", f.Id, errors);
                if (f.Count != "one" && f.Count != "per-district") errors.Add($"{w}: facility '{f.Id}' count must be one or per-district");
                if (f.District.Length > 0 && System.Array.IndexOf(DistrictKinds, f.District) < 0) errors.Add($"{w}: facility '{f.Id}' district unknown");
                if (f.Width < 1 || f.Height < 1) errors.Add($"{w}: facility '{f.Id}' size must be ≥ 1×1");
            }
            if (c.Plots.TierSizes.Count != 5) errors.Add($"{w}: plots.tierSizes needs 5 entries");
            foreach (List<int> sz in c.Plots.TierSizes) if (sz.Count != 2 || sz[0] < 1 || sz[1] < 1) errors.Add($"{w}: plot size must be [w, h] ≥ 1");
            foreach (string k in DistrictKinds)
                if (!c.Plots.TierMix.TryGetValue(k, out List<double>? mix) || mix.Count != 5) errors.Add($"{w}: plots.tierMix '{k}' needs 5 weights");
            BuildingTypeDef? starter = data.FindBuildingType(c.Starter.BuildingType);
            if (starter == null) errors.Add($"{w}: starter building type '{c.Starter.BuildingType}' unknown");
            else if (starter.Tier != 1) errors.Add($"{w}: starter building type must be tier 1");
            if (c.Starter.Count < 1 || c.Starter.MaxAttempts < 1) errors.Add($"{w}: starter count and attempts must be ≥ 1");
            foreach (CityModifierDef m in c.Modifiers) Id(w + " modifiers", m.Id, errors);
            if (c.Names.First.Count == 0 || c.Names.Last.Count == 0) errors.Add($"{w}: names need first and last parts");
        }

        static void ValidateBuildings(DataRepository data, List<string> errors)
        {
            BuildingsDef b = data.Buildings;
            const string w = "buildings.json";
            for (int t = 1; t <= 5; t++)
            {
                BuildingTierDef? tier = b.Tiers.Find(x => x.Tier == t);
                if (tier == null) { errors.Add($"{w}: tier {t} missing"); continue; }
                if (tier.FootprintMin.Count != 2 || tier.FootprintMax.Count != 2 || tier.FootprintMin[0] > tier.FootprintMax[0] || tier.FootprintMin[1] > tier.FootprintMax[1])
                    errors.Add($"{w}: tier {t} footprint range invalid");
                foreach (string f in tier.Fluids) if (data.FindFluid(f) == null) errors.Add($"{w}: tier {t} fluid '{f}' unknown");
                foreach (string e in tier.ReferenceEquipment) if (data.FindEquipment(e) == null) errors.Add($"{w}: tier {t} reference equipment '{e}' unknown");
                if (tier.Available && tier.ReferenceEquipment.Count == 0) errors.Add($"{w}: tier {t} has no reference equipment");
            }
            foreach (RoomTypeDef r in b.RoomTypes)
            {
                Id(w + " roomTypes", r.Id, errors);
                if (!data.Game.Theme.RoomFills.ContainsKey(r.Id)) errors.Add($"{w}: room type '{r.Id}' has no theme fill");
                foreach (RoomOutletRuleDef o in r.Outlets)
                    if (data.FindOutlet(o.Type) == null) errors.Add($"{w}: room type '{r.Id}' outlet '{o.Type}' unknown");
            }
            foreach (BuildingTypeDef t in b.Types)
            {
                Id(w + " types", t.Id, errors);
                Tier($"{w} type '{t.Id}'", t.Tier, errors);
                BuildingTierDef? tier = b.Tiers.Find(x => x.Tier == t.Tier);
                if (tier != null && tier.Available && t.Program.Count == 0) errors.Add($"{w}: type '{t.Id}' has no room program");
                foreach (ProgramRoomDef p in t.Program)
                {
                    if (data.FindRoomType(p.Room) == null) errors.Add($"{w}: type '{t.Id}' room '{p.Room}' unknown");
                    if (p.CountMin < 0 || p.CountMax < p.CountMin || !(p.AreaWeight > 0)) errors.Add($"{w}: type '{t.Id}' room '{p.Room}' ranges invalid");
                }
                foreach (string k in t.Districts.Keys) if (System.Array.IndexOf(DistrictKinds, k) < 0) errors.Add($"{w}: type '{t.Id}' district '{k}' unknown");
                foreach (string e in t.InitialEquipment) if (data.FindEquipment(e)?.Placement != "intake") errors.Add($"{w}: type '{t.Id}' initial equipment '{e}' is not intake equipment");
                foreach (string code in t.Codes) if (data.FindCode(code) == null) errors.Add($"{w}: type '{t.Id}' code '{code}' unknown");
            }
            BuildingGenerationDef g = b.Generation;
            if (g.MinRoomSide < 1 || g.WindowSpacing < 1 || g.SolvabilityAttempts < 1) errors.Add($"{w}: generation ranges invalid");
            foreach (string f in g.PhaseFluids) if (data.FindFluid(f) == null) errors.Add($"{w}: phase fluid '{f}' unknown");
        }

        static void ValidateUpgrades(DataRepository data, List<string> errors)
        {
            UpgradesDef u = data.Upgrades;
            const string w = "upgrades.json";
            if (u.DuctSealing.Count == 0) errors.Add($"{w}: no sealing classes");
            else if (u.DuctSealing[0].Tier != 1 || u.DuctSealing[0].PricePerMetre != 0) errors.Add($"{w}: the first sealing class is the free tier-1 default");
            foreach (SealingDef s in u.DuctSealing)
            {
                Id(w, s.Id, errors);
                Tier($"{w} '{s.Id}'", s.Tier, errors);
                Positive($"{w} '{s.Id}' leakageCoefficient", s.LeakageCoefficient, errors);
            }
            if (!(u.LeakageExponent > 0 && u.LeakageExponent < 1)) errors.Add($"{w}: leakageExponent must be in (0, 1)");
            foreach (InsulationDef i in u.Insulation)
            {
                Id(w, i.Id, errors);
                Tier($"{w} '{i.Id}'", i.Tier, errors);
                Positive($"{w} '{i.Id}' u", i.U, errors);
            }
        }

        static int RoomAt(BuildingLayoutDef b, int x, int y)
        {
            for (int i = 0; i < b.Rooms.Count; i++)
            {
                RoomDef r = b.Rooms[i];
                if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return i;
            }
            return -1;
        }

        static void ValidateDoors(string w, BuildingLayoutDef b, List<string> errors)
        {
            foreach (DoorDef d in b.Doors)
            {
                string where = $"{w}: door at ({d.X}, {d.Y}) {d.Side}";
                if (!Facings.Contains(d.Side)) { errors.Add($"{where}: side unknown"); continue; }
                int nx = d.X + (d.Side == "east" ? 1 : d.Side == "west" ? -1 : 0);
                int ny = d.Y + (d.Side == "north" ? 1 : d.Side == "south" ? -1 : 0);
                int ra = RoomAt(b, d.X, d.Y), rb = RoomAt(b, nx, ny);
                if (ra < 0 || rb < 0) errors.Add($"{where}: not between two rooms");
                else if (ra == rb) errors.Add($"{where}: both sides are the same room");
            }
        }
    }
}
