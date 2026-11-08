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
        static readonly HashSet<string> FamilyKinds = new HashSet<string> { "duct", "pipe", "main", "cable" };
        static readonly HashSet<string> FittingModels = new HashSet<string> { "fixed", "elbow", "tee", "reducer", "valve", "check", "regulator", "relief", "filter" };
        static readonly HashSet<string> SpeedControls = new HashSet<string> { "crank", "on-off", "variable", "none" };
        static readonly HashSet<string> Placements = new HashSet<string> { "intake", "inline", "addon", "room", "wall" };
        static readonly HashSet<string> PowerModels = new HashSet<string> { "none", "curve", "heat" };
        static readonly HashSet<string> PortRoles = new HashSet<string> { "in", "fuel", "out", "return", "flow" };
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
            ValidateNetworks(data, errors);
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
            ValidatePrestige(data, errors);
            ValidateEvents(data, errors);
            ValidateInspections(data, errors);
            ValidateThermal(data, errors);
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
            foreach (ThemeDef t in new[] { data.Game.Theme, data.Game.ThemeDark }) ValidateTheme(data, t, errors);
        }

        static void ValidateTheme(DataRepository data, ThemeDef t, List<string> errors)
        {
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
                string? model = data.FindFitting(id)?.Model;
                bool valve = model == "valve" || model == "check" || model == "regulator" || model == "relief" || id == "fire-damper";
                if (!fan && !valve) errors.Add($"game.json toolbox: '{id}' is not an inline fan or valve");
            }
            foreach (string id in data.Game.Toolbox.Safety)
            {
                string? placement = data.FindEquipment(id)?.Placement;
                if (placement != "room" && placement != "wall") errors.Add($"game.json toolbox: '{id}' is not safety gear");
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
            Positive(f + " heatmap.jointBlendMetres", b.Heatmap.JointBlendMetres, errors);
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
            if (b.Heatmap.DivergingPalette.Count < 3 || b.Heatmap.DivergingPalette.Count % 2 == 0) errors.Add($"{f} heatmap.divergingPalette needs an odd number (≥ 3) of stops, its middle one at the centre");
            foreach (string c in b.Heatmap.DivergingPalette) Colour($"{f} heatmap.divergingPalette", c, errors);
            if (b.Heatmap.Palettes.ContainsKey("diverging")) errors.Add($"{f} heatmap.palettes: 'diverging' is the temperature ramp's name");
            Positive(f + " heatmap.divergingHalfRangeK", b.Heatmap.DivergingHalfRangeK, errors);
            if (!(b.Heatmap.DivergingCentreC >= b.Climate.HeatingSetpointC && b.Heatmap.DivergingCentreC <= b.Climate.CoolingSetpointC))
                errors.Add($"{f} heatmap.divergingCentreC should lie between the heating and cooling setpoints");
            Fraction(f + " drawing.ghostOpacity", b.Drawing.GhostOpacity, errors);
            Fraction(f + " drawing.otherNetworkOpacity", b.Drawing.OtherNetworkOpacity, errors);
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
            if (b.Building.CeilingCapacityByTier.Count != 5 || b.Building.CeilingCapacityByTier.Exists(c => c < 1))
                errors.Add(f + " building.ceilingCapacityByTier needs 5 entries, each at least 1");

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

            Fraction(f + " devices.spinResolveStep", b.Devices.SpinResolveStep, errors);

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
                if (fl.FlammableLower != 0)
                {
                    if (!(fl.FlammableLower > 0 && fl.FlammableUpper > fl.FlammableLower && fl.FlammableUpper < 1)) errors.Add($"fluids.json '{fl.Id}': need 0 < flammableLower < flammableUpper < 1");
                    Positive($"fluids.json '{fl.Id}' lowerHeatingValueMJPerKg", fl.LowerHeatingValueMJPerKg, errors);
                }
                if (!string.IsNullOrEmpty(fl.Table) && data.FluidTables.TryGetValue(fl.Id, out FluidTableDef table))
                    ValidateTable(data, fl, table, errors);
            }
        }

        static readonly HashSet<string> Conduits = new HashSet<string> { "duct", "pipe" };
        static readonly HashSet<string> PressureUnits = new HashSet<string> { "Pa", "mbar", "bar" };
        static readonly HashSet<string> FlowUnits = new HashSet<string> { "m3/h", "L/s", "L/min" };

        /// <summary>An entry opening utility: one that starts a network, or power.</summary>
        static bool IsUtility(DataRepository data, string utility) => utility == "power" || (utility.Length > 0 && data.NetworkForUtility(utility).Length > 0);

        static void ValidateNetworks(DataRepository data, List<string> errors)
        {
            if (data.Networks.Count == 0) errors.Add("networks.json: no networks");
            var utilities = new HashSet<string>();
            foreach (NetworkDef n in data.Networks)
            {
                string w = $"networks.json '{n.Id}'";
                Id("networks.json", n.Id, errors);
                FluidDef? fl = data.FindFluid(n.Fluid);
                if (fl == null) errors.Add($"{w}: fluid '{n.Fluid}' unknown");
                if (fl != null && fl.Id != n.Id && data.FindNetwork(fl.Id) == null) errors.Add($"{w}: fluid '{fl.Id}' has no network of its own id");
                if (string.IsNullOrWhiteSpace(n.DisplayName) || string.IsNullOrWhiteSpace(n.ShortName)) errors.Add($"{w}: displayName and shortName are required");
                if (!Conduits.Contains(n.Conduit)) errors.Add($"{w}: conduit '{n.Conduit}' unknown");
                if (!PressureUnits.Contains(n.PressureUnit)) errors.Add($"{w}: pressureUnit '{n.PressureUnit}' unknown");
                if (!FlowUnits.Contains(n.FlowUnit)) errors.Add($"{w}: flowUnit '{n.FlowUnit}' unknown");
                Tier(w, n.Tier, errors);
                if (n.Utility.Length > 0 && !utilities.Add(n.Utility)) errors.Add($"{w}: utility '{n.Utility}' starts two networks");
                if (n.Utility == "power") errors.Add($"{w}: utility 'power' has no network");
                if (n.Colour.Length > 0) Colour(w + " colour", n.Colour, errors);
                if (n.LinePattern.Length > 0 && !LinePatterns.Contains(n.LinePattern)) errors.Add($"{w}: linePattern '{n.LinePattern}' unknown");
                MaterialDef? m = data.FindMaterial(n.DefaultMaterial);
                if (m == null) errors.Add($"{w}: defaultMaterial '{n.DefaultMaterial}' unknown");
                else if (!m.Fluids.Contains(n.Fluid)) errors.Add($"{w}: defaultMaterial '{m.Id}' does not carry {n.Fluid}");
                else if ((m.Kind == "duct") != n.IsDuct) errors.Add($"{w}: defaultMaterial '{m.Id}' is a {m.Kind}, the network uses {n.Conduit}s");
                if (m != null && !SizeExists(data, m.Kind, n.DefaultSizeMm)) errors.Add($"{w}: defaultSizeMm {n.DefaultSizeMm} is not a {m.Kind} size");
            }
        }

        static bool SizeExists(DataRepository data, string kind, double mm)
        {
            foreach (PipeFamilyDef f in data.PipeFamilies)
                if (f.Kind == kind)
                    foreach (PipeSizeDef s in f.Sizes) if (s.DiameterMm == mm) return true;
            return false;
        }

        /// <summary>Devices that join networks: ports on known networks, inside the footprint, in pairs that make sense.</summary>
        static void ValidatePorts(DataRepository data, EquipmentDef e, List<string> errors)
        {
            if (e.Ports.Count == 0) return;
            string w = $"equipment.json '{e.Id}'";
            var tiles = new HashSet<(string, int, int)>();
            bool Has(string role, string? network = null) => e.Ports.Exists(p => p.Role == role && (network == null || p.Network == network));
            foreach (PortDef p in e.Ports)
            {
                if (data.FindNetwork(p.Network) == null) errors.Add($"{w}: port network '{p.Network}' unknown");
                if (!PortRoles.Contains(p.Role)) errors.Add($"{w}: port role '{p.Role}' unknown");
                if (p.Dx < 0 || p.Dy < 0 || p.Dx >= e.Footprint.Width || p.Dy >= e.Footprint.Height) errors.Add($"{w}: port {p.Role} lies outside the footprint");
                if (!tiles.Add((p.Network, p.Dx, p.Dy))) errors.Add($"{w}: two ports on {p.Network} share a tile");
                if (p.Role == "out" && !e.Ports.Exists(q => q.Role == "in" && q.Network != p.Network)) errors.Add($"{w}: an out port needs an in port on another network");
                if (p.Role == "in" && !e.Ports.Exists(q => q.Role == "out" && q.Network != p.Network)) errors.Add($"{w}: an in port needs an out port on another network");
                if (p.Role == "flow" && !Has("return", p.Network)) errors.Add($"{w}: a flow port needs a return port on {p.Network}");
                if (p.Role == "return" && !Has("flow", p.Network) && !Has("out", p.Network)) errors.Add($"{w}: a return port needs a flow or out port on {p.Network}");
                if (p.Role == "fuel" && !Has("flow") && !Has("out")) errors.Add($"{w}: a fuel port needs something to heat");
            }
            if (Has("flow") || Has("out"))
            {
                Positive(w + " connectionDiameterMm", e.ConnectionDiameterMm, errors);
                Positive(w + " resistanceK", e.ResistanceK, errors);
            }
            if (e.HeatOutputW > 0 || Has("fuel"))
            {
                Positive(w + " heatOutputW", e.HeatOutputW, errors);
                Positive(w + " setpointC", e.SetpointC, errors);
                if (!(e.ThermalEfficiency > 0 && e.ThermalEfficiency <= 1)) errors.Add($"{w}: thermalEfficiency must be in (0, 1]");
            }
            if (Has("fuel") && !data.FluidOf(e.Ports.Find(p => p.Role == "fuel")!.Network).IdealGas) errors.Add($"{w}: a fuel port takes gas");
            if (e.PowerModel == "heat" && !(e.HeatOutputW > 0)) errors.Add($"{w}: powerModel heat needs heatOutputW");
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
                if (m.Kind == "pipe")
                {
                    if (m.Walls.Count == 0) errors.Add($"{w}: pipes need wall thicknesses");
                    if (!(m.WaveSpeed > 0)) errors.Add($"{w}: pipes need a wave speed");
                    foreach (PipeWallDef wall in m.Walls)
                        if (!(wall.WallMm > 0 && 2 * wall.WallMm < wall.DiameterMm)) errors.Add($"{w}: wall {wall.WallMm} mm does not fit {wall.DiameterMm} mm");
                }
            }
        }

        static void ValidatePipes(DataRepository data, List<string> errors)
        {
            foreach (PipeFamilyDef p in data.PipeFamilies)
            {
                string w = $"pipes.json '{p.Id}'";
                Id("pipes.json", p.Id, errors);
                if (!FamilyKinds.Contains(p.Kind)) errors.Add($"{w}: kind '{p.Kind}' unknown");
                if (p.Sizes.Count == 0) errors.Add($"{w}: no sizes");
                bool city = p.Kind == "main" || p.Kind == "cable";
                if (city && !Utilities.Contains(p.Utility)) errors.Add($"{w}: utility '{p.Utility}' unknown");
                if (p.Kind == "main")
                {
                    Positive(w + " capacityVelocity", p.CapacityVelocity, errors);
                    Positive(w + " pricePerTile", p.PricePerTile, errors);
                    Positive(w + " priceReferenceDiameterMm", p.PriceReferenceDiameterMm, errors);
                    foreach (PressureClassDef c in p.PressureClasses)
                    {
                        Id(w + " pressureClasses", c.Id, errors);
                        Tier($"{w} class '{c.Id}'", c.Tier, errors);
                        Positive($"{w} class '{c.Id}' capacityVelocity", c.CapacityVelocity, errors);
                        Positive($"{w} class '{c.Id}' priceFactor", c.PriceFactor, errors);
                        if (!data.Balancing.City.FacilityPressure.ContainsKey(c.FacilityPressureKey)) errors.Add($"{w} class '{c.Id}': facility pressure '{c.FacilityPressureKey}' unknown");
                    }
                }
                for (int i = 0; i < p.Sizes.Count; i++)
                {
                    Tier($"{w} sizes[{i}]", p.Sizes[i].Tier, errors);
                    if (p.Kind == "cable")
                    {
                        Positive($"{w} sizes[{i}].capacityKW", p.Sizes[i].CapacityKW, errors);
                        Positive($"{w} sizes[{i}].pricePerTile", p.Sizes[i].PricePerTile, errors);
                        if (i > 0 && !(p.Sizes[i].CapacityKW > p.Sizes[i - 1].CapacityKW)) errors.Add($"{w}: sizes not ascending at {i}");
                        continue;
                    }
                    Positive($"{w} sizes[{i}].diameterMm", p.Sizes[i].DiameterMm, errors);
                    if (!city && p.Sizes[i].CapacityUnits < 1) errors.Add($"{w} sizes[{i}]: capacityUnits must be ≥ 1");
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
                    case "fixed":
                    case "check": Positive(w + " k", ft.K, errors); break;
                    case "regulator":
                    case "relief":
                        Positive(w + " k", ft.K, errors);
                        Positive(w + " settingStep", ft.SettingStep, errors);
                        if (!(ft.SettingMin > 0 && ft.SettingMin <= ft.DefaultSetting && ft.DefaultSetting <= ft.SettingMax))
                            errors.Add($"{w}: need 0 < settingMin ≤ defaultSetting ≤ settingMax");
                        break;
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
                ValidatePorts(data, e, errors);
                string w = $"equipment.json '{e.Id}'";
                Id("equipment.json", e.Id, errors);
                Tier(w, e.Tier, errors);
                if (!SpeedControls.Contains(e.SpeedControl)) errors.Add($"{w}: speedControl '{e.SpeedControl}' unknown");
                if (e.Curve.Count > 0 && e.SpeedControl != "crank")
                {
                    Positive(w + " runDownSeconds", e.RunDownSeconds, errors);
                    Positive(w + " spinUpSeconds", e.SpinUpSeconds, errors);
                }
                if (!Placements.Contains(e.Placement)) errors.Add($"{w}: placement '{e.Placement}' unknown");
                if (!PowerModels.Contains(e.PowerModel)) errors.Add($"{w}: powerModel '{e.PowerModel}' unknown");
                if (!Layers.Contains(e.Footprint.Layer) && e.Footprint.Layer != "none") errors.Add($"{w}: footprint.layer '{e.Footprint.Layer}' unknown");
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
                if (e.Category == "panel")
                {
                    Positive(w + " panelLimitKW", e.PanelLimitKW, errors);
                    continue;
                }
                if (e.Category == "gas-supply")
                {
                    Positive(w + " setpointPa", e.SetpointPa, errors);
                    Positive(w + " regulatorK", e.RegulatorK, errors);
                    continue;
                }
                if (e.Curve.Count == 0)
                {
                    if (e.PowerModel == "curve") errors.Add($"{w}: powerModel curve needs a curve");
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
                if (data.FindNetwork(o.Fluid) == null) errors.Add($"{w}: network '{o.Fluid}' unknown (outlets name the network they are on)");
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
                    if (o.Measured == "pressure" && !(r.DesignFlowLps > 0)) errors.Add($"{w} tier {kv.Key}: pressure outlets need designFlowLps");
                }
                // Heat outlets are rated in watts against the room's load: their tier values are factors on it.
                bool heat = o.Measured == "heat";
                if (heat != (o.Unit == "W")) errors.Add($"{w}: heat outlets and only they are measured in W");
                if (heat != (o.LoadBased == "heating" || o.LoadBased == "cooling")) errors.Add($"{w}: heat outlets need loadBased heating or cooling (and others none)");
            }
        }

        static readonly HashSet<string> EventScopes = new HashSet<string> { "city", "district", "facility", "building" };
        static readonly HashSet<string> EventRequires = new HashSet<string> { "heating", "cooling", "power", "online-building" };
        static readonly HashSet<string> RewardRules = new HashSet<string> { "", "keep-up", "adapt" };

        static void ValidateEvents(DataRepository data, List<string> errors)
        {
            EventsBalancingDef b = data.Balancing.Events;
            Positive("balancing.json events.tickHz", b.TickHz, errors);
            Positive("balancing.json events.meanIntervalHours", b.MeanIntervalHours, errors);
            if (b.MinGapMinutes < 0) errors.Add("balancing.json events.minGapMinutes must be ≥ 0");
            if (b.MaxPerDistrict < 1 || b.MaxCityWide < 1 || b.LogSize < 1) errors.Add("balancing.json events: maxPerDistrict, maxCityWide and logSize must be ≥ 1");
            if (data.Events.Count == 0) errors.Add("events.json: no events");
            foreach (EventDef e in data.Events)
            {
                string w = $"events.json '{e.Id}'";
                Id("events.json", e.Id, errors);
                if (string.IsNullOrWhiteSpace(e.DisplayName) || string.IsNullOrWhiteSpace(e.Banner)) errors.Add($"{w}: displayName and banner are required");
                if (!EventScopes.Contains(e.Scope)) errors.Add($"{w}: scope '{e.Scope}' unknown");
                Positive(w + " weight", e.Weight, errors);
                if (!(e.MinMinutes > 0 && e.MaxMinutes >= e.MinMinutes)) errors.Add($"{w}: need 0 < minMinutes ≤ maxMinutes");
                foreach (string r in e.Requires) if (!EventRequires.Contains(r)) errors.Add($"{w}: requires '{r}' unknown");
                foreach (string o in e.RequirementOutlets) if (data.FindOutlet(o) == null) errors.Add($"{w}: requirement outlet '{o}' unknown");
                foreach (string f in e.Facilities) if (!data.City.Facilities.Exists(x => x.Id == f)) errors.Add($"{w}: facility '{f}' unknown");
                foreach (string k in e.DistrictKinds) if (!data.City.Districts.Weights.ContainsKey(k)) errors.Add($"{w}: district kind '{k}' unknown");
                if (e.Facilities.Count > 0 && e.Scope != "facility") errors.Add($"{w}: facilities need facility scope");
                if (e.DistrictKinds.Count > 0 && e.Scope != "district") errors.Add($"{w}: districtKinds need district scope");
                if (e.PowerOutage && e.Scope != "district") errors.Add($"{w}: a power outage hits a district");
                Positive(w + " solarFactor", e.SolarFactor, errors);
                Positive(w + " requirementFactor", e.RequirementFactor, errors);
                Positive(w + " facilityFactor", e.FacilityFactor, errors);
                Positive(w + " rewardPayFactor", e.RewardPayFactor, errors);
                if (e.ChangeShareMax > 0 && !(e.ChangeShareMin > 0 && e.ChangeShareMax >= e.ChangeShareMin && e.ChangeShareMax <= 1))
                    errors.Add($"{w}: need 0 < changeShareMin ≤ changeShareMax ≤ 1");
                if (!RewardRules.Contains(e.RewardRule)) errors.Add($"{w}: rewardRule '{e.RewardRule}' unknown");
                if ((e.RewardPayFactor != 1) != (e.RewardRule.Length > 0)) errors.Add($"{w}: a reward needs a rewardRule and a rewardPayFactor");
                if (e.RewardRule == "adapt" && !(e.AdaptMinutes > 0 && e.RewardMinutes > 0)) errors.Add($"{w}: the adapt rule needs adaptMinutes and rewardMinutes");
            }
        }

        static void ValidateInspections(DataRepository data, List<string> errors)
        {
            InspectionDef i = data.Balancing.Inspection;
            if (!(i.ChancePerHour > 0 && i.ChancePerHour < 1)) errors.Add("balancing.json inspection.chancePerHour must be in (0, 1)");
            Positive("balancing.json inspection.passPayFactor", i.PassPayFactor, errors);
            Positive("balancing.json inspection.passMinutes", i.PassMinutes, errors);
            Positive("balancing.json inspection.fixDeadlineMinutes", i.FixDeadlineMinutes, errors);
            if (i.InspectorWalkSeconds < 0) errors.Add("balancing.json inspection.inspectorWalkSeconds must be ≥ 0");
            Positive("balancing.json inspection.tickHz", i.TickHz, errors);
            Positive("balancing.json inspection.ratingFlowExponent", i.RatingFlowExponent, errors);
            Positive("balancing.json inspection.ratingPressureExponent", i.RatingPressureExponent, errors);
            Positive("balancing.json inspection.ratingMinReferenceW", i.RatingMinReferenceW, errors);
            Positive("balancing.json fines.inspectionFailGrossMinutes", data.Balancing.Fines.InspectionFailGrossMinutes, errors);
            Positive("balancing.json fines.inspectionFailMinimum", data.Balancing.Fines.InspectionFailMinimum, errors);
            var kinds = new HashSet<string>();
            foreach (InspectionKindDef k in data.Inspections.Kinds)
            {
                Id("inspections.json", k.Id, errors);
                if (!kinds.Add(k.Id)) errors.Add($"inspections.json: duplicate kind '{k.Id}'");
                if (string.IsNullOrWhiteSpace(k.DisplayName) || string.IsNullOrWhiteSpace(k.Checks)) errors.Add($"inspections.json '{k.Id}': displayName and checks are required");
            }
            List<RatingBandDef> bands = data.Inspections.EnergyRating;
            if (bands.Count == 0) errors.Add("inspections.json: no energy rating bands");
            for (int n = 0; n < bands.Count; n++)
            {
                RatingBandDef r = bands[n];
                string w = $"inspections.json energy rating '{r.Grade}'";
                if (string.IsNullOrWhiteSpace(r.Grade)) errors.Add("inspections.json: an energy rating band has no grade");
                Positive(w + " payFactor", r.PayFactor, errors);
                bool last = n == bands.Count - 1;
                if (last != (r.MaxScore == 0)) errors.Add($"{w}: only the last band has no upper limit (maxScore 0)");
                if (!last && n > 0 && !(r.MaxScore > bands[n - 1].MaxScore)) errors.Add($"{w}: maxScore must rise band by band");
                if (n > 0 && !(r.PayFactor < bands[n - 1].PayFactor)) errors.Add($"{w}: payFactor must fall band by band");
            }
        }

        static void ValidateThermal(DataRepository data, List<string> errors)
        {
            ClimateDef c = data.Balancing.Climate;
            if (!(c.DesignHeatingC < c.HeatingSetpointC)) errors.Add("balancing.json climate: designHeatingC must be below heatingSetpointC");
            if (!(c.DesignCoolingC > c.CoolingSetpointC)) errors.Add("balancing.json climate: designCoolingC must be above coolingSetpointC");
            if (!c.RoomHumidity.ContainsKey("other")) errors.Add("balancing.json climate.roomHumidity needs an 'other' entry");
            foreach (var kv in c.RoomHumidity)
            {
                if (kv.Key != "other" && data.FindRoomType(kv.Key) == null) errors.Add($"balancing.json climate.roomHumidity: room type '{kv.Key}' unknown");
                if (!(kv.Value > 0 && kv.Value < 1)) errors.Add($"balancing.json climate.roomHumidity '{kv.Key}' must be in (0, 1)");
            }
            OutageDef o = data.Balancing.Outage;
            Positive("balancing.json outage.gasSafetyWindowSeconds", o.GasSafetyWindowSeconds, errors);
            Positive("balancing.json outage.tankWarmRateKPerMinute", o.TankWarmRateKPerMinute, errors);
            Positive("balancing.json outage.groundwaterLpsPer100M2", o.GroundwaterLpsPer100M2, errors);
            Positive("balancing.json outage.floodPanelLevelM", o.FloodPanelLevelM, errors);
            Positive("balancing.json outage.generatorStartSeconds", o.GeneratorStartSeconds, errors);
            Positive("balancing.json outage.generatorFuelPerKWs", o.GeneratorFuelPerKWs, errors);
            Positive("balancing.json outage.raisedPanelPriceFactor", o.RaisedPanelPriceFactor, errors);
            Positive("balancing.json gas.backgroundLeakPerFitting", data.Balancing.Gas.BackgroundLeakPerFitting, errors);
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
                if (c.Id == "indoor-air-quality" && !(c.Parameters.TryGetValue("maxCo2Ppm", out double ppm) && ppm > data.Balancing.Climate.OutdoorCo2Ppm))
                    errors.Add($"{w}: maxCo2Ppm must be above the outdoor CO₂ ({data.Balancing.Climate.OutdoorCo2Ppm} ppm)");
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
                var ids = new HashSet<string>();
                foreach (RoomDef r in b.Rooms) if (!ids.Add(r.Id)) errors.Add($"{w}: room id '{r.Id}' used twice");
                foreach (RoomDef r in b.Rooms)
                {
                    Id(w + " rooms", r.Id, errors);
                    if (r.X < 0 || r.Y < 0 || r.Width < 1 || r.Height < 1 || r.X + r.Width > b.Width || r.Y + r.Height > b.Height)
                        errors.Add($"{w}: room '{r.Id}' outside the footprint");
                    if (data.FindRoomType(r.Type) == null) errors.Add($"{w}: room '{r.Id}' type '{r.Type}' unknown");
                    if (r.Floor < b.LowestFloor || r.Floor >= b.LowestFloor + System.Math.Max(1, b.Floors)) errors.Add($"{w}: room '{r.Id}' floor {r.Floor} outside the building");
                    if (r.Occupants < 0 || r.MachinesW < 0) errors.Add($"{w}: room '{r.Id}' loads must be ≥ 0");
                }
                var openingIds = new HashSet<string>();
                foreach (EntryOpeningDef o in b.EntryOpenings)
                {
                    Id(w + " entryOpenings", o.Id, errors);
                    openingIds.Add(o.Id);
                    if (!IsUtility(data, o.Utility)) errors.Add($"{w}: opening '{o.Id}' utility '{o.Utility}' unknown");
                    if (!Facings.Contains(o.Facing)) errors.Add($"{w}: opening '{o.Id}' facing '{o.Facing}' unknown");
                    // On an outside wall: the tile is in a room and the one beyond its facing wall is not (L and U shapes
                    // have outside walls inside the bounding box).
                    int bx = o.X + (o.Facing == "east" ? 1 : o.Facing == "west" ? -1 : 0), by = o.Y + (o.Facing == "north" ? 1 : o.Facing == "south" ? -1 : 0);
                    bool onEdge = RoomAt(b, o.X, o.Y, o.Floor) >= 0 && RoomAt(b, bx, by, o.Floor) < 0;
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
                    if (RoomAt(b, o.X, o.Y, o.Floor) < 0) errors.Add($"{w}: outlet '{o.Id}' is not in a room");
                }
                foreach (WindowDef win in b.Windows)
                    if (!Facings.Contains(win.Side) || RoomAt(b, win.X, win.Y, win.Floor) < 0) errors.Add($"{w}: window at ({win.X}, {win.Y}) {win.Side} is not on a room wall");
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

        static void ValidatePrestige(DataRepository data, List<string> errors)
        {
            const string w = "prestige.json";
            var ids = new HashSet<string>();
            foreach (PrestigeNodeDef n in data.Prestige.Nodes)
            {
                Id(w, n.Id, errors);
                if (!ids.Add(n.Id)) errors.Add($"{w}: duplicate node '{n.Id}'");
                if (n.Costs.Count == 0) errors.Add($"{w} '{n.Id}': no levels");
                foreach (int c in n.Costs) if (c <= 0) errors.Add($"{w} '{n.Id}': costs must be > 0");
                if (n.RequiresOtherNodes < 0 || n.RequiresOtherNodes >= data.Prestige.Nodes.Count) errors.Add($"{w} '{n.Id}': requiresOtherNodes out of range");
            }
            PrestigeBalancingDef b = data.Balancing.Prestige;
            Fraction("balancing.json prestige.shareOnline", b.ShareOnline, errors);
            Positive("balancing.json prestige.incomeTargetFirst", b.IncomeTargetFirst, errors);
            Positive("balancing.json prestige.incomeTargetGrowth", b.IncomeTargetGrowth, errors);
            Positive("balancing.json prestige.k", b.K, errors);
        }

        static int RoomAt(BuildingLayoutDef b, int x, int y, int floor = 0)
        {
            for (int i = 0; i < b.Rooms.Count; i++)
            {
                RoomDef r = b.Rooms[i];
                if (r.Floor == floor && x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return i;
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
                int ra = RoomAt(b, d.X, d.Y, d.Floor), rb = RoomAt(b, nx, ny, d.Floor);
                if (ra < 0 || rb < 0) errors.Add($"{where}: not between two rooms");
                else if (ra == rb) errors.Add($"{where}: both sides are the same room");
            }
        }
    }
}
