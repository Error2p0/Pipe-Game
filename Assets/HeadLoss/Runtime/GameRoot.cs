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
        GameContext? _ctx;
        GameLoop? _loop;
        bool _saving;
        double _sinceSave;
        ScreenKind _screen = ScreenKind.Menu;
        BuildingInput? _input;
        CameraController? _camera;
        ProceduralAudio? _audio;
        HeatmapState? _heatmap;
        ViewPalette? _palette;
        UiKit? _ui;
        VisualElement? _uiRoot;
        TopBar? _topBar;
        MenuScreen? _menu;
        BuildingListScreen? _list;
        CityScene? _city;
        BuildingScene? _scene;
        Jobs.BurstRoomGridStepper? _burstRooms;
        ShopScreen? _shop;
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
            _palette = new ViewPalette(_data);
            _ui = new UiKit(_palette);
            _heatmap = new HeatmapState(_data.Balancing.Heatmap);

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

            ShowMenu();
        }

        // ---------- screens ----------

        void ShowMenu()
        {
            CloseGame(save: true);
            _screen = ScreenKind.Menu;
            string? info = null;
            if (_store!.Exists(Profile))
            {
                LoadResult? peek = _store.Load(Profile, _data!);
                if (peek != null) info = $"Played {FormatDuration(peek.File.PlayTimeSeconds)} · wallet {Units.FormatMoney(peek.File.GameState.Wallet, _data!.Game.CurrencySymbol)}";
            }
            _menu = new MenuScreen(_uiRoot!, _ui!, _data!.Game.DisplayName, info, Continue, () => StartGame(GameContext.NewGame(_data!, NewCitySeed()), saving: true), Quit);
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
            if (r.OfflineSeconds > 1)
                Debug.Log($"Save: away for {FormatDuration(r.OfflineSeconds)}. Offline earnings arrive with Phase 3.");
            StartGame(r.File.GameState, saving: true);
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
            foreach (string note in _ctx.Notes) Debug.Log("Save: " + note);
            if (!_ctx.StarterSolvable) Debug.LogWarning("Head Loss: no solvable starter house in this city (dev report).");
            _loop = new GameLoop(_ctx);
            _online.Clear();
            foreach (BuildingSession s in _ctx.Sessions) Hook(s);
            _ctx.SessionCreated += Hook;
            _ctx.TierUnlocked += OnTierUnlocked;
            _topBar = new TopBar(_uiRoot!, _ui!, _ctx);
            _notice = new Notice(_uiRoot!, _ui!);
            if (_ctx.Building != null) OpenBuilding(_ctx.Building.Record.BuildingId);
            else if (_ctx.City != null) ShowCity();
            else ShowList();
            if (_saving) SaveNow();
        }

        void Hook(BuildingSession s)
        {
            s.DesignCommitted += OnCommitted;
            s.Commands.Executed += OnCommand;
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
            _city = new CityScene(_ctx, _materials, _palette!, _ui!, _camera!, _input!, _uiRoot!, () => false);
            _topBar!.BringToFront();
        }

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
            _burstRooms ??= new Jobs.BurstRoomGridStepper();
            _scene = new BuildingScene(s, _materials, _palette!, _ui!, _heatmap!, _camera!, _input!, _uiRoot!, Debug.isDebugBuild, _burstRooms);
            GameContext ctx = _ctx;
            _scene.Hud.UnlockCondition = tier => Game.Progression.Tech.Condition(ctx.Data, ctx, tier);
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
            _list?.Dispose();
            _list = null;
            _city?.Dispose();
            _city = null;
            CloseShop();
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
            if (_saving)
            {
                _sinceSave += dt;
                if (_sinceSave >= _data!.Balancing.Save.AutosaveSeconds) SaveNow();
            }
            _notice?.Update(Time.unscaledTime);
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
                if (_city.Hud.ShopRequested)
                {
                    _city.Hud.ShopRequested = false;
                    OpenShop();
                    return;
                }
            }
            ChimeWhenOnline();
            _list?.Update(Time.unscaledTime);
            _topBar?.Update(Time.unscaledTime);
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

        /// <summary>Airflow follows the design's total delivered and escaping air; one hum per running fan.</summary>
        void UpdateAudio(BuildingSession s)
        {
            NetworkSnapshot? snap = s.Current;
            if (snap == null || _audio == null) return;
            double q = 0;
            foreach (var kv in snap.Compiled.OutletLink) q += Math.Max(0, snap.Result.Flow[kv.Value]);
            foreach (var kv in snap.Compiled.OpenEndLink) q += Math.Max(0, snap.Result.Flow[kv.Value]);
            _audio.SetAirflow(Units.M3sToM3h(q));
            int fan = 0;
            _audio.ClearFans();
            foreach (var kv in snap.Compiled.FanLink) _audio.SetFan(fan++, snap.Compiled.Graph.Links[kv.Value].Speed);
            foreach (var kv in snap.Compiled.InlineLink)
                if (snap.Compiled.Graph.Links[kv.Value].Kind == Sim.Network.LinkKind.Fan) _audio.SetFan(fan++, snap.Compiled.Graph.Links[kv.Value].Speed);
        }

        void OnDestroy()
        {
            _scene?.Dispose();
            _city?.Dispose();
            _burstRooms?.Dispose();
            _input?.Dispose();
            foreach (Material m in _ownedMaterials) Destroy(m);
        }
    }
}
