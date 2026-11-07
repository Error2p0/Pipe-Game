using System;
using System.IO;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Game;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using HeadLoss.Unity.Audio;
using HeadLoss.Unity.Data;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.UI;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace HeadLoss.Unity
{
    /// <summary>
    /// Main scene entry point and frame loop. Screens: main menu → city view ⇄ building view. Owns the game
    /// context, autosave (interval, leaving a building, Commit, pause/focus loss/quit), the camera, input, UI
    /// document and procedural audio. No static state (Fast Enter Play Mode).
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        enum ScreenKind { Menu, City, List, Building }

        [SerializeField] DataCatalog? catalog;
        [SerializeField] PanelSettings? panelSettings;
        [SerializeField] StyleSheet? styleSheet;
        [SerializeField] Shader? unlitShader;
        [SerializeField] Shader? heatmapShader;

        public const int Profile = 1;

        DataRepository? _data;
        SaveStore? _store;
        SettingsStore? _settingsStore;
        PlayerSettings _settings = new PlayerSettings();
        SettingsScreen? _settingsScreen;
        GameContext? _ctx;
        GameLoop? _loop;
        bool _saving;
        double _sinceSave;
        ScreenKind _screen = ScreenKind.Menu;
        BuildingInput? _input;
        CameraController? _camera;
        ProceduralAudio? _audio;
        SteamService? _steam;
        HeatmapState? _heatmap;
        ViewPalette? _palette;
        UiKit? _ui;
        VisualElement? _uiRoot;
        TopBar? _topBar;
        MenuScreen? _menu;
        BuildingListScreen? _list;
        CityScene? _city;
        BuildingScene? _scene;
        /// <summary>A room grid stepper per live session (they hold native copies of its grid, about 300 KB for an apartment block).</summary>
        readonly System.Collections.Generic.Dictionary<BuildingSession, Jobs.BurstRoomGridStepper> _roomSteppers = new System.Collections.Generic.Dictionary<BuildingSession, Jobs.BurstRoomGridStepper>();
        ShopScreen? _shop;
        AwayScreen? _away;
        PrestigeScreen? _prestige;
        readonly System.Collections.Generic.List<CityRing> _rings = new System.Collections.Generic.List<CityRing>();
        float _sinceAlarmChime;
        Notice? _notice;
        readonly System.Collections.Generic.HashSet<BuildingSession> _online = new System.Collections.Generic.HashSet<BuildingSession>();
        readonly Materials _materials = new Materials();
        readonly System.Collections.Generic.List<Material> _ownedMaterials = new System.Collections.Generic.List<Material>();

        public DataRepository? Data => _data;
        public GameContext? Context => _ctx;
        public BuildingSession? Session => _scene?.Session;
        public BuildingInteraction? Interaction => _scene?.Interaction;
        public HeatmapState? Heatmap => _heatmap;
        public BuildingHud? Hud => _scene?.Hud;
        public DuctRenderer? Ducts => _scene?.Ducts;
        /// <summary>What the duct layer showed at its last rebuild.</summary>
        public DuctViewInput View => _scene?.View ?? new DuctViewInput();
        /// <summary>Session solve revision the duct layer last rebuilt for.</summary>
        public int RenderedSolveRevision => _scene?.RenderedSolveRevision ?? -1;
        public bool InMenu => _screen == ScreenKind.Menu;
        public bool InBuildingList => _screen == ScreenKind.List;
        public bool InCity => _screen == ScreenKind.City;
        public CityScene? City => _city;
        public ShopScreen? Shop => _shop;
        public SettingsScreen? SettingsOpen => _settingsScreen;
        /// <summary>The settings in force.</summary>
        public PlayerSettings Settings => _settings;
        public AwayScreen? Away => _away;
        public PrestigeScreen? Prestige => _prestige;

        void Awake()
        {
            // UI text is English-only for now; format every number the same way as Core.Units (invariant).
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        }

        void Start()
        {
            if (catalog == null || panelSettings == null || styleSheet == null || unlitShader == null)
            {
                Debug.LogError("GameRoot is missing references. Run Head Loss/Setup/Create Scenes.");
                enabled = false;
                return;
            }
            _data = catalog.Load();
            foreach (string e in DataValidator.Validate(_data)) Debug.LogError("Data: " + e);
            _store = new SaveStore(Path.Combine(Application.persistentDataPath, "saves"), _data.Balancing.Save);
            _settingsStore = new SettingsStore(Path.Combine(Application.persistentDataPath, "saves"));
            _settings = _settingsStore.Load(_data);
            _palette = new ViewPalette(_data, _settings.Theme);
            _ui = new UiKit(_palette);
            _heatmap = new HeatmapState(_data.Balancing.Heatmap) { GasLflPercent = (float)(_data.Fluid(Game.Buildings.BuildingState.GasFluid).FlammableLower * 100) };

            Material Layer(int layer, string name)
            {
                var m = new Material(unlitShader) { name = name, renderQueue = RenderLayers.Queue(layer) };
                _ownedMaterials.Add(m);
                return m;
            }
            _materials.Building = Layer(RenderLayers.Building, "HeadLoss Building");
            _materials.Walls = Layer(RenderLayers.Walls, "HeadLoss Walls");
            Shader heat = heatmapShader != null ? heatmapShader : Shader.Find("HeadLoss/RoomHeatmap");
            _materials.RoomHeat = new Material(heat) { name = "HeadLoss RoomHeat", renderQueue = RenderLayers.Queue(RenderLayers.RoomHeat) };
            _ownedMaterials.Add(_materials.RoomHeat);
            _materials.Overlay = Layer(RenderLayers.Overlay, "HeadLoss Overlay");
            _materials.Ducts = Layer(RenderLayers.Ducts, "HeadLoss Ducts");
            _materials.Equipment = Layer(RenderLayers.Equipment, "HeadLoss Equipment");
            _materials.Particles = Layer(RenderLayers.Particles, "HeadLoss Particles");
            _materials.Particles.enableInstancing = true;
            Color particleTint = _palette.Paper;
            particleTint.a = 0.9f;
            _materials.Particles.SetColor("_Tint", particleTint);

            _camera = new GameObject("Camera").AddComponent<CameraController>();
            _camera.Init(_data.Balancing.Camera, _palette.Paper);
            _camera.gameObject.AddComponent<AudioListener>();
            _audio = _camera.gameObject.AddComponent<ProceduralAudio>();
            _audio.Init(_data.Balancing.Audio, (float)_data.Balancing.Audio.MasterVolume);

            _steam = gameObject.AddComponent<SteamService>();
            _steam.Init(_data.Game.Steam);
            _input = new BuildingInput();
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            _uiRoot = doc.rootVisualElement;
            _uiRoot.styleSheets.Add(styleSheet);
            _uiRoot.AddToClassList("hl-root");
            _uiRoot.pickingMode = PickingMode.Ignore;
            _uiRoot.style.color = _palette.Text;
            // Every button press gives a soft click.
            _uiRoot.RegisterCallback<ClickEvent>(e => { if (e.target is Button) _audio.PlayClick(); }, TrickleDown.TrickleDown);

            ApplySettings(_settings);
            ShowMenu();
        }

        // ---------- settings ----------

        /// <summary>Applies everything except the theme (which rebuilds the screens, <see cref="ApplyTheme"/>).</summary>
        void ApplySettings(PlayerSettings s)
        {
            _audio!.SetVolumes((float)s.MasterVolume, (float)s.AmbienceVolume, (float)s.UiVolume);
            _heatmap!.SetPalette(s.Palette);
            _heatmap.SetDiverging(s.TemperatureDiverging);
            if (panelSettings != null) panelSettings.scale = (float)s.UiScale;
            if (_scene != null) _scene.ReduceMotion = s.ReduceMotion;
            try
            {
                if (s.Bindings.Length > 0) _input!.MouseKeyboard.LoadBindingOverridesFromJson(s.Bindings);
                else _input!.MouseKeyboard.RemoveAllBindingOverrides();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Head Loss: key bindings could not be read, using the defaults: " + e.Message);
                _input!.MouseKeyboard.RemoveAllBindingOverrides();
            }
        }

        void OpenSettings()
        {
            CloseSettings();
            _settingsScreen = new SettingsScreen(_uiRoot!, _ui!, _data!, _input!, _settings.Clone(), ApplySettings, CloseSettings);
            _topBar?.BringToFront();
        }

        void CloseSettings()
        {
            if (_settingsScreen == null) return;
            PlayerSettings s = _settingsScreen.Settings;
            s.Sanitize(_data!);
            _settingsScreen.Dispose();
            _settingsScreen = null;
            bool themeChanged = s.Theme != _settings.Theme;
            _settings = s;
            try
            {
                // Test games (saving off) never touch the player's files.
                if (_ctx == null || _saving) _settingsStore!.Save(_settings);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Head Loss: could not save the settings: " + e.Message);
            }
            ApplySettings(_settings);
            if (themeChanged) ApplyTheme();
        }

        /// <summary>A new theme: new palette and UI kit, then the current screen is built again in it.</summary>
        void ApplyTheme()
        {
            _palette = new ViewPalette(_data!, _settings.Theme);
            _ui = new UiKit(_palette);
            _camera!.Camera.backgroundColor = _palette.Paper;
            _uiRoot!.style.color = _palette.Text;
            Color particleTint = _palette.Paper;
            particleTint.a = 0.9f;
            _materials.Particles.SetColor("_Tint", particleTint);
            if (_ctx == null)
            {
                _menu?.Dispose();
                _menu = null;
                ShowMenu();
                return;
            }
            _topBar?.Root.RemoveFromHierarchy();
            _topBar = new TopBar(_uiRoot!, _ui!, _ctx);
            _notice?.Root.RemoveFromHierarchy();
            _notice = new Notice(_uiRoot!, _ui!);
            if (_scene != null) OpenBuilding(_scene.Session.Record.BuildingId);
            else if (_city != null) ShowCity();
            else ShowList();
        }

        // ---------- screens ----------

        void ShowMenu()
        {
            _menu?.Dispose();
            _menu = null;
            CloseGame(save: true);
            _screen = ScreenKind.Menu;
            string? info = null;
            if (_store!.Exists(Profile))
            {
                LoadResult? peek = _store.Load(Profile, _data!);
                if (peek != null) info = $"Played {FormatDuration(peek.File.PlayTimeSeconds)} · wallet {Units.FormatMoney(peek.File.GameState.Wallet, _data!.Game.CurrencySymbol)}";
            }
            if (info != null && _steam != null && _steam.Status.Length > 0) info = $"{info} · {_steam.Status}";
            _menu = new MenuScreen(_uiRoot!, _ui!, MenuTitle, info, Continue, () => StartGame(GameContext.NewGame(_data!, NewCitySeed()), saving: true), OpenSettings, Quit);
        }

        /// <summary>A random seed for a new city (the only randomness outside the seeded generators; never used in game rules).</summary>
        static ulong NewCitySeed() => Core.Seeds.Mix((ulong)DateTime.UtcNow.Ticks ^ ((ulong)Environment.TickCount << 32));

        static string FormatDuration(double s) => s < 3600 ? $"{s / 60:0} min" : $"{s / 3600:0.0} h";

        void Continue()
        {
            LoadResult? r = _store!.Load(Profile, _data!);
            if (r == null)
            {
                Debug.LogWarning("Head Loss: no readable save; starting a new game.");
                StartGame(GameContext.NewGame(_data!, NewCitySeed()), saving: true);
                return;
            }
            foreach (string note in r.Notes) Debug.Log("Save: " + note);
            StartGame(r.File.GameState, saving: true);
            CatchUp(r);
        }

        /// <summary>Starts playing a game state. <paramref name="saving"/> = false keeps tests from touching the save.</summary>
        public void StartGame(GameState game, bool saving)
        {
            _menu?.Dispose();
            _menu = null;
            CloseGame(save: false);
            _saving = saving;
            _sinceSave = 0;
            _ctx = new GameContext(_data!, game);
#if HEADLOSS_DEMO
            _ctx.Demo = true;
#endif
            _ctx.Exploded += OnExploded;
            _rings.Clear();
            foreach (string note in _ctx.Notes) Debug.Log("Save: " + note);
            if (!_ctx.StarterSolvable) Debug.LogWarning("Head Loss: no solvable starter house in this city (dev report).");
            _loop = new GameLoop(_ctx);
            _online.Clear();
            foreach (BuildingSession s in _ctx.Sessions) Hook(s);
            _ctx.SessionCreated += Hook;
            _ctx.SessionRemoved += OnSessionRemoved;
            _ctx.TierUnlocked += OnTierUnlocked;
            _topBar = new TopBar(_uiRoot!, _ui!, _ctx);
            _notice = new Notice(_uiRoot!, _ui!);
            if (_ctx.Building != null) OpenBuilding(_ctx.Building.Record.BuildingId);
            else if (_ctx.City != null) ShowCity();
            else ShowList();
            if (_saving) SaveNow();
        }

        void ReleaseRoomSteppers()
        {
            foreach (Jobs.BurstRoomGridStepper stepper in _roomSteppers.Values) stepper.Dispose();
            _roomSteppers.Clear();
        }

        void OnSessionRemoved(BuildingSession s)
        {
            if (!_roomSteppers.TryGetValue(s, out Jobs.BurstRoomGridStepper stepper)) return;
            stepper.Dispose();
            _roomSteppers.Remove(s);
        }

        void Hook(BuildingSession s)
        {
            var stepper = new Jobs.BurstRoomGridStepper();
            _roomSteppers[s] = stepper;
            s.Rooms.Stepper = stepper;
            s.DesignCommitted += OnCommitted;
            s.Commands.Executed += OnCommand;
            s.SolveProblem += message => Debug.LogWarning("Head Loss (dev report): " + message);
            s.Failures.Alert += text => _notice?.Show($"{s.Layout.DisplayName}: {text}", Time.unscaledTime);
            s.PanelTrippedEvent += () => _notice?.Show($"{s.Layout.DisplayName}: panel tripped · reduce the load and reset it", Time.unscaledTime);
            s.GasSafety.AlarmRaised += () =>
            {
                _sinceAlarmChime = 0;
                _audio?.PlayAlarm();
            };
            if (s.CommittedEvaluation.Online) _online.Add(s);
        }

        void ShowCity()
        {
            _scene?.Dispose();
            _scene = null;
            _list?.Dispose();
            _list = null;
            _city?.Dispose();
            _ctx!.Leave();
            _audio!.SetAirflow(_data!.Balancing.Audio.AirflowFullM3h * 0.06);
            _audio.ClearFans();
            _screen = ScreenKind.City;
            _audio.SetWater(0);
            _city = new CityScene(_ctx, _materials, _palette!, _ui!, _camera!, _input!, _uiRoot!, _heatmap!, () => _settings.ReduceMotion, _rings);
            _topBar!.BringToFront();
        }

        /// <summary>Shows the city view (tools and tests that change the game from outside the screens).</summary>
        public void ShowCityView() => ShowCity();

        /// <summary>The scene of the open building (dev tools and tests).</summary>
        public BuildingScene? Scene => _scene;

        /// <summary>F3 and F4: development builds, or a player build started with <c>-headloss-dev</c>.</summary>
        static bool DevTools => Debug.isDebugBuild || Array.IndexOf(Environment.GetCommandLineArgs(), "-headloss-dev") >= 0;

#if HEADLOSS_DEMO
        string MenuTitle => _data!.Game.DisplayName + " · Demo";
#else
        string MenuTitle => _data!.Game.DisplayName;
#endif

        /// <summary>The hand-made building list (test fixtures without a city only, D34).</summary>
        void ShowList()
        {
            _scene?.Dispose();
            _scene = null;
            _ctx!.Leave();
            _audio!.SetAirflow(0);
            _audio.ClearFans();
            _screen = ScreenKind.List;
            _list?.Dispose();
            _list = new BuildingListScreen(_uiRoot!, _ui!, _ctx, OpenBuilding, ShowMenu);
            _topBar!.BringToFront();
        }

        /// <summary>Opens a building in the building view.</summary>
        public void OpenBuilding(string id)
        {
            if (!_ctx!.CanOpen(id)) return;
            _list?.Dispose();
            _list = null;
            _city?.Dispose();
            _city = null;
            _scene?.Dispose();
            BuildingSession s = _ctx!.Open(id);
            _scene = new BuildingScene(s, _materials, _palette!, _ui!, _heatmap!, _camera!, _input!, _uiRoot!, DevTools, null);
            GameContext ctx = _ctx;
            _scene.Hud.UnlockCondition = tier => Game.Progression.Tech.Condition(ctx.Data, ctx, tier);
            _scene.ReduceMotion = _settings.ReduceMotion;
            _topBar!.BringToFront();
            _screen = ScreenKind.Building;
        }

        void LeaveBuilding()
        {
            SaveNow();
            if (_ctx!.City != null) ShowCity();
            else ShowList();
        }

        void CloseGame(bool save)
        {
            if (_ctx == null) return;
            if (save) SaveNow();
            _scene?.Dispose();
            _scene = null;
            ReleaseRoomSteppers();
            _list?.Dispose();
            _list = null;
            _city?.Dispose();
            _city = null;
            CloseShop();
            CloseAway();
            ClosePrestige();
            _notice?.Root.RemoveFromHierarchy();
            _notice = null;
            _topBar?.Root.RemoveFromHierarchy();
            _topBar = null;
            _ctx = null;
            _loop = null;
        }

        void Quit()
        {
            SaveNow();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Offline progress (<c>12-save-offline.md</c>): the time since the save is credited at steady state, then the
        /// "While you were away" report shows (after at least <c>offline.reportMinimumSeconds</c>).
        /// </summary>
        void CatchUp(LoadResult r)
        {
            if (_ctx == null || r.AwaySeconds <= 0) return;
            double startHour = 12;
            if (DateTime.TryParse(r.File.SavedAtUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime saved))
            {
                DateTime local = saved.ToLocalTime();
                startHour = local.Hour + local.Minute / 60.0;
            }
            RunCatchUp(r.AwaySeconds, startHour);
        }

        /// <summary>Credits <paramref name="awaySeconds"/> of offline time from <paramref name="startHour"/> (local) and shows the report.</summary>
        public void RunCatchUp(double awaySeconds, double startHour)
        {
            if (_ctx == null) return;
            Game.Offline.OfflineReport report = Game.Offline.OfflineCatchUp.Run(_ctx, awaySeconds, startHour);
            Debug.Log($"Head Loss: away {FormatDuration(report.AwaySeconds)}, credited {FormatDuration(report.Seconds)}, net {Units.FormatMoney(report.Net, _data!.Game.CurrencySymbol)} ({report.ComputeMilliseconds:0} ms).");
            SaveNow();
            if (report.AwaySeconds < _data!.Balancing.Offline.ReportMinimumSeconds) return;
            _away = new AwayScreen(_uiRoot!, _ui!, _ctx, report, CloseAway, id =>
            {
                CloseAway();
                OpenBuilding(id);
            });
            _topBar?.BringToFront();
        }

        void CloseAway()
        {
            _away?.Dispose();
            _away = null;
        }

        // ---------- prestige ----------

        void OpenPrestige()
        {
            ClosePrestige();
            _prestige = new PrestigeScreen(_uiRoot!, _ui!, _ctx!, ClosePrestige, FoundNewCity);
            _topBar?.BringToFront();
        }

        void ClosePrestige()
        {
            _prestige?.Dispose();
            _prestige = null;
        }

        /// <summary>Founds a new city (<c>03-city.md</c> §Prestige): a typed seed, or a random one when the text is empty.</summary>
        public void FoundNewCity(string seedText)
        {
            if (_ctx == null) return;
            string typed = seedText.Trim();
            ulong seed = typed.Length == 0 ? NewCitySeed() : ulong.TryParse(typed, out ulong n) ? n : Core.Seeds.Derive(0x5EED_C17EUL, typed);
            GameState? next = _ctx.FoundNewCity(seed);
            if (next == null) return;
            bool saving = _saving;
            ClosePrestige();
            CloseGame(save: false);
            StartGame(next, saving);
        }

        /// <summary>A building exploded: the soft ring, the muffled thump and one calm line. If it was open, back to the city.</summary>
        void OnExploded(ExplosionReport r)
        {
            _audio?.PlayThump();
            Game.City.Plot? plot = _ctx?.City?.FindPlot(r.PlotId);
            if (plot != null) _rings.Add(new CityRing { Centre = new Vector2((float)plot.CentreX, (float)plot.CentreY), RadiusMetres = (float)r.RadiusMetres, Start = Time.unscaledTime });
            string neighbours = r.Neighbours.Count > 0 ? $" · {r.Neighbours.Count} neighbour{(r.Neighbours.Count == 1 ? "" : "s")} damaged" : "";
            _notice?.Show($"A building exploded · fines {Units.FormatMoney(r.Fine, _data!.Game.CurrencySymbol)}{neighbours}", Time.unscaledTime);
        }

        // ---------- shop and notices ----------

        void OpenShop()
        {
            CloseShop();
            _shop = new ShopScreen(_uiRoot!, _ui!, _ctx!, _scene?.Session, CloseShop);
            _topBar?.BringToFront();
        }

        void CloseShop()
        {
            _shop?.Dispose();
            _shop = null;
        }

        void OnTierUnlocked(int tier)
        {
            _audio?.PlayChime();
            _notice?.Show($"Tier {tier} unlocked · new equipment, sizes and upgrades in the shop", Time.unscaledTime);
        }

        // ---------- events ----------

        void OnCommitted()
        {
            _audio?.PlayChime();
            SaveNow();
        }

        void OnCommand(ICommand c)
        {
            if (c is PlaceRunCommand || c is PlaceInlineCommand || c is SetIntakeEquipmentCommand) _audio?.PlayTock();
        }

        /// <summary>Writes the save now (no-op when saving is off). Errors are logged; play continues.</summary>
        public void SaveNow()
        {
            if (!_saving || _ctx == null || _store == null) return;
            _sinceSave = 0;
            try
            {
                _store.Save(Profile, _ctx.Game, _data!.Version);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Head Loss: could not save: " + e.Message);
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveNow();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) SaveNow();
        }

        void OnApplicationQuit() => SaveNow();

        // ---------- frame loop ----------

        void Update()
        {
            if (_loop == null || _ctx == null) return;
            float dt = Time.deltaTime;
            // Outside temperature and the sun follow the player's local clock (13-balancing.md §Climate).
            DateTime now = DateTime.Now;
            _ctx.Climate.LocalHour = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;
            _loop.Tick(dt);
            // The open building may be a ruin now.
            if (_scene != null && _ctx.FindSession(_scene.Session.Record.BuildingId) == null)
            {
                if (_ctx.City != null) ShowCity();
                else ShowList();
            }
            TickAlarmChime(dt);
            if (_topBar != null && _topBar.PrestigeRequested)
            {
                _topBar.PrestigeRequested = false;
                OpenPrestige();
            }
            if (_saving)
            {
                _sinceSave += dt;
                if (_sinceSave >= _settings.AutosaveSeconds) SaveNow();
            }
            _notice?.Update(Time.unscaledTime);
            if (_settingsScreen != null)
            {
                // Modal: Esc closes it (unless a key is being rebound, when Esc cancels the rebind).
                if (!_settingsScreen.Rebinding && _input!.Cancel.WasPressedThisFrame()) CloseSettings();
                _topBar?.Update(Time.unscaledTime);
                return;
            }
            if (_away != null)
            {
                if (_input!.Cancel.WasPressedThisFrame() || _input.Commit.WasPressedThisFrame()) CloseAway();
                _topBar?.Update(Time.unscaledTime);
                return;
            }
            if (_prestige != null)
            {
                if (_input!.Cancel.WasPressedThisFrame()) ClosePrestige();
                _prestige?.Update(Time.unscaledTime);
                _topBar?.Update(Time.unscaledTime);
                return;
            }
            if (_shop != null)
            {
                // The shop is modal: Esc closes it, the scenes wait.
                if (_input!.Cancel.WasPressedThisFrame()) CloseShop();
                _topBar?.Update(Time.unscaledTime);
                return;
            }
            if (_scene != null)
            {
                if (_scene.Hud.ShopRequested)
                {
                    _scene.Hud.ShopRequested = false;
                    OpenShop();
                    return;
                }
                _scene.Update(dt, Time.unscaledTime);
                if (_scene.ConsumeBack())
                {
                    LeaveBuilding();
                    return;
                }
                UpdateAudio(_scene.Session);
            }
            if (_city != null)
            {
                _city.Update(dt, Time.unscaledTime);
                string? open = _city.ConsumeOpen();
                if (open != null)
                {
                    OpenBuilding(open);
                    return;
                }
                if (_city.ConsumeMenu())
                {
                    ShowMenu();
                    return;
                }
                if (_city.Hud.Clicked)
                {
                    _city.Hud.Clicked = false;
                    _audio?.PlayClick();
                }
                if (_city.Hud.Laid)
                {
                    _city.Hud.Laid = false;
                    _audio?.PlayTock();
                }
                if (_city.Hud.ShopRequested)
                {
                    _city.Hud.ShopRequested = false;
                    OpenShop();
                    return;
                }
                if (_city.Hud.SettingsRequested)
                {
                    _city.Hud.SettingsRequested = false;
                    OpenSettings();
                    return;
                }
                if (_city.Hud.PrestigeRequested)
                {
                    _city.Hud.PrestigeRequested = false;
                    OpenPrestige();
                    return;
                }
            }
            ChimeWhenOnline();
            _list?.Update(Time.unscaledTime);
            _topBar?.Update(Time.unscaledTime);
        }

        /// <summary>While any explosion timer runs: the soft two-tone chime every 4 s (<c>11-ui-visuals-audio.md</c>).</summary>
        void TickAlarmChime(float dt)
        {
            bool any = false;
            foreach (BuildingSession s in _ctx!.Sessions) any |= s.GasSafety.TimerRunning;
            if (!any) return;
            _sinceAlarmChime += dt;
            if (_sinceAlarmChime < 4f) return;
            _sinceAlarmChime = 0;
            _audio?.PlayAlarm();
        }

        /// <summary>A quiet chime whenever a building comes online (<c>11-ui-visuals-audio.md</c> §Procedural audio).</summary>
        void ChimeWhenOnline()
        {
            foreach (BuildingSession s in _ctx!.Sessions)
            {
                bool on = s.CommittedEvaluation.Online;
                if (on && _online.Add(s)) _audio?.PlayChime();
                else if (!on) _online.Remove(s);
            }
        }

        void LateUpdate()
        {
            _scene?.LateUpdate(Time.deltaTime);
            _city?.LateUpdate(Time.deltaTime, Time.unscaledTime);
        }

        /// <summary>Airflow and water follow the design's throughput; one hum per running fan and pump.</summary>
        void UpdateAudio(BuildingSession s)
        {
            NetworkSnapshot? snap = s.Current;
            if (snap == null || _audio == null) return;
            double q = 0;
            foreach (var kv in snap.Compiled.OutletLink) q += Math.Max(0, snap.Result.Flow[kv.Value]);
            foreach (var kv in snap.Compiled.OpenEndLink) q += Math.Max(0, snap.Result.Flow[kv.Value]);
            _audio.SetAirflow(Units.M3sToM3h(q));
            FluidSolve? water = snap.Water;
            _audio.SetWater(water != null && water.Compiled.SupplyNode >= 0 ? Math.Max(0, water.Compiled.SupplyFlow(water.Result)) * 1000 : 0);
            int fan = 0;
            _audio.ClearFans();
            foreach (FluidSolve fs in snap.All)
            {
                foreach (var kv in fs.Compiled.FanLink) _audio.SetFan(fan++, fs.Compiled.Graph.Links[kv.Value].Speed);
                foreach (var kv in fs.Compiled.InlineLink)
                    if (fs.Compiled.Graph.Links[kv.Value].Kind == Sim.Network.LinkKind.Fan) _audio.SetFan(fan++, fs.Compiled.Graph.Links[kv.Value].Speed);
            }
        }

        void OnDestroy()
        {
            _scene?.Dispose();
            _city?.Dispose();
            ReleaseRoomSteppers();
            _input?.Dispose();
            foreach (Material m in _ownedMaterials) Destroy(m);
        }
    }
}
