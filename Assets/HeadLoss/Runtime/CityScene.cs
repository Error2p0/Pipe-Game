using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game;
using HeadLoss.Game.City;
using HeadLoss.Game.Session;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.UI;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity
{
    /// <summary>
    /// The city view (<c>03-city.md</c> §City as hub): the map, plots that light up when their building is online,
    /// hover for the difficulty hint, click or tap to select (the panel offers Open), double-click or Enter to open.
    /// Esc clears the selection, then asks for the menu.
    /// </summary>
    public sealed class CityScene
    {
        public CityHud Hud { get; }
        /// <summary>Plot index under the pointer (−1 = none).</summary>
        public int Hovered { get; private set; } = -1;
        /// <summary>Selected plot index (−1 = none).</summary>
        public int Selected { get; private set; } = -1;

        readonly GameContext _ctx;
        readonly CityLayout _city;
        readonly CityRenderer _renderer;
        readonly CameraController _camera;
        readonly BuildingInput _input;
        readonly GameObject _root;
        readonly ViewPalette _palette;
        readonly List<PlotLook> _looks = new List<PlotLook>();
        readonly System.Func<bool> _reduceMotion;
        readonly HeatmapState _heatmap;
        readonly CityOverlay _overlay = new CityOverlay();
        bool _routing;
        RouteQuote? _quote;
        bool _panning;
        float _lastClick = -10f;
        int _lastClickPlot = -1;
        int _starter;

        public CityScene(GameContext ctx, Materials materials, ViewPalette palette, UiKit ui, CameraController camera, BuildingInput input,
            VisualElement uiRoot, HeatmapState heatmap, System.Func<bool> reduceMotion, List<CityRing>? rings = null)
        {
            _overlay.Rings = rings;
            _ctx = ctx;
            _heatmap = heatmap;
            _overlay.Palette = palette;
            _city = ctx.City!;
            _camera = camera;
            _input = input;
            _palette = palette;
            _reduceMotion = reduceMotion;
            _root = new GameObject("City " + _city.Name);
            _renderer = _root.AddComponent<CityRenderer>();
            _renderer.Build(_city, palette.Theme.City, materials.Building, materials.Ducts, materials.Overlay);
            Hud = new CityHud(uiRoot, ui, ctx, camera);
            _starter = _city.Starter != null ? _city.Plots.IndexOf(_city.Starter) : -1;
            for (int i = 0; i < _city.Plots.Count; i++) _looks.Add(PlotLook.Offline);

            CameraDef cam = ctx.Data.Balancing.Camera;
            camera.SetZoomLimits(cam.CityZoomMin, cam.CityZoomMax);
            float t = (float)_city.TileMetres;
            // Open on the city centre, close enough to read the starter area.
            camera.Frame(new Rect((float)(_city.CentreX - 14) * t, (float)(_city.CentreY - 9) * t, 28 * t, 18 * t), 0);
        }

        /// <summary>Plot id whose building the player asked to open, or null.</summary>
        public string? ConsumeOpen()
        {
            string? id = Hud.OpenRequested;
            Hud.OpenRequested = null;
            return id;
        }

        public bool ConsumeMenu()
        {
            bool m = Hud.MenuRequested;
            Hud.MenuRequested = false;
            return m;
        }

        public void Update(float dt, float time)
        {
            Vector2 screen = _input.PointerPosition;
            bool overUi = Hud.IsPointerOverUi(screen);

            float scroll = _input.Scroll.ReadValue<Vector2>().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f) _camera.Zoom(Mathf.Sign(scroll), screen);
            _camera.KeyPan(_input.Pan.ReadValue<Vector2>(), dt);
            if ((_input.Secondary.WasPressedThisFrame() || _input.Middle.WasPressedThisFrame()) && !overUi)
            {
                _panning = true;
                _camera.BeginDrag(screen);
            }
            if (_panning)
            {
                _camera.UpdateDrag(screen);
                if (!_input.Secondary.IsPressed() && !_input.Middle.IsPressed())
                {
                    _panning = false;
                    _camera.EndDrag();
                }
            }

            Vector2 world = _camera.ScreenToWorld(screen);
            Hovered = overUi ? -1 : PlotAt(world);
            float tileSize = (float)_city.TileMetres;
            (int x, int y) tile = (Mathf.FloorToInt(world.x / tileSize), Mathf.FloorToInt(world.y / tileSize));
            if (Hud.Tool == CityTool.Route) RouteTool(overUi, tile);
            else if (Hud.Tool == CityTool.Corridor)
            {
                CancelRoute();
                if (!overUi && _input.Primary.IsPressed() && _ctx.BuildCorridor(tile.x, tile.y)) Hud.Clicked = true;
            }
            else
            {
                CancelRoute();
                if (!overUi && _input.Primary.WasPressedThisFrame())
                {
                    if (Hovered >= 0 && Hovered == _lastClickPlot && time - _lastClick < 0.4f) Hud.OpenRequested = _city.Plots[Hovered].Id;
                    Selected = Hovered;
                    _lastClick = time;
                    _lastClickPlot = Hovered;
                    // Off the plots, a click picks the main or cable running there.
                    UtilityState u = _ctx.Game.City!.Utilities;
                    int main = 0;
                    if (Hovered < 0)
                    {
                        foreach (string utility in new[] { "water", "gas", "power" })
                            if (main == 0 && (Hud.Filter.Length == 0 || Hud.Filter == utility)) main = Utilities.MainAt(u, utility, tile.x, tile.y);
                    }
                    Hud.SelectedMain = main;
                }
            }
            if (_input.Commit.WasPressedThisFrame() && Selected >= 0) Hud.OpenRequested = _city.Plots[Selected].Id;
            if (_input.Cancel.WasPressedThisFrame())
            {
                if (_routing) CancelRoute();
                else if (Hud.Tool != CityTool.None) Hud.Tool = CityTool.None;
                else if (Hud.SelectedMain != 0) Hud.SelectedMain = 0;
                else if (Selected >= 0) Selected = -1;
                else Hud.MenuRequested = true;
            }
            if (Hud.OpenRequested != null && !_ctx.CanOpen(Hud.OpenRequested)) Hud.OpenRequested = null;

            int shown = Selected >= 0 ? Selected : Hovered;
            Hud.Update(time, shown >= 0 ? _city.Plots[shown].Id : "", Selected >= 0);
        }

        /// <summary>
        /// The route tool (<c>03-city.md</c> §Routing): press on a facility or an existing main of the utility, drag along city
        /// tiles with a live quote, release to lay it. Right click cancels.
        /// </summary>
        void RouteTool(bool overUi, (int x, int y) tile)
        {
            List<(int x, int y)> route = _overlay.Route;
            if (!_routing && !overUi && _input.Primary.WasPressedThisFrame() && _city.InMap(tile.x, tile.y))
            {
                _routing = true;
                route.Clear();
                route.Add(tile);
                Requote();
            }
            if (!_routing) return;
            if (_input.Secondary.WasPressedThisFrame())
            {
                CancelRoute();
                return;
            }
            if (_city.InMap(tile.x, tile.y) && Extend(route, tile)) Requote();
            if (_input.Primary.IsPressed()) return;

            // Released: lay it if it can be laid.
            RouteQuote result = _ctx.LayMain(Hud.Utility, Hud.DiameterMm, Hud.CapacityKW, route, Hud.Drilled, Hud.PressureClass);
            if (result.Valid)
            {
                Hud.Laid = true;
                Hud.ShowRoute(null, null);
            }
            else Hud.ShowRoute(result, null);
            _routing = false;
            route.Clear();
            _overlay.RouteViolations = null;
            _quote = null;
        }

        /// <summary>Walks the route to a tile: x first, then y; stepping back onto the route undoes back to there. True when it changed.</summary>
        static bool Extend(List<(int x, int y)> route, (int x, int y) to)
        {
            bool changed = false;
            for (int guard = 0; guard < 512 && route[route.Count - 1] != to; guard++)
            {
                (int x, int y) last = route[route.Count - 1];
                (int x, int y) next = last.x != to.x ? (last.x + System.Math.Sign(to.x - last.x), last.y) : (last.x, last.y + System.Math.Sign(to.y - last.y));
                int at = route.IndexOf(next);
                if (at >= 0) route.RemoveRange(at + 1, route.Count - at - 1);
                else route.Add(next);
                changed = true;
            }
            return changed;
        }

        void Requote()
        {
            UtilityState u = _ctx.Game.City!.Utilities;
            _quote = Utilities.Quote(_ctx.Data, _city, u, Hud.Utility, Hud.DiameterMm, Hud.CapacityKW, _overlay.Route, Hud.Drilled, 1, Hud.PressureClass);
            // One tile is not a route yet: judge the start alone, so a bad press says why at once.
            if (_overlay.Route.Count == 1)
            {
                (int x, int y) s = _overlay.Route[0];
                bool onFacility = _city.Facilities.Exists(f => f.Utility == Hud.Utility && s.x >= f.X && s.y >= f.Y && s.x < f.X + f.Width && s.y < f.Y + f.Height);
                bool onMain = Utilities.MainAt(u, Hud.Utility, s.x, s.y, Hud.PressureClass) != 0;
                _overlay.RouteValid = onFacility || onMain;
                _overlay.RouteViolations = null;
                Hud.ShowRoute(_overlay.RouteValid ? _quote : QuoteError(Hud.Utility == "power" ? "Start at a substation or an existing cable" : "Start at the facility or an existing main"), null);
                return;
            }
            _overlay.RouteValid = _quote.Valid;
            _overlay.RouteViolations = _quote.Violations;
            WaterConnection? w = Hud.Utility == "water" ? _ctx.PreviewWater(Hud.DiameterMm, _overlay.Route, Hud.Drilled, _quote)
                : Hud.Utility == "gas" ? _ctx.PreviewGas(Hud.DiameterMm, Hud.PressureClass, _overlay.Route, Hud.Drilled, _quote) : null;
            Hud.ShowRoute(_quote, w);
        }

        static RouteQuote QuoteError(string error)
        {
            var q = new RouteQuote();
            q.Error = error;
            return q;
        }

        void CancelRoute()
        {
            if (!_routing && _overlay.Route.Count == 0) return;
            _routing = false;
            _overlay.Route.Clear();
            _overlay.RouteViolations = null;
            _quote = null;
            Hud.ShowRoute(null, null);
        }

        public void LateUpdate(float dt, float time)
        {
            for (int i = 0; i < _city.Plots.Count; i++)
            {
                Plot p = _city.Plots[i];
                BuildingSession? s = _ctx.FindSession(p.Id);
                _looks[i] = _ctx.IsRuin(p.Id) ? PlotLook.Ruin : !_ctx.CityBuildings!.Available(p) ? PlotLook.Locked : s != null && s.CommittedEvaluation.Online ? PlotLook.Online : PlotLook.Offline;
            }
            Hud.PositionStartHere();
            Hud.PositionAlarms();
            bool still = _reduceMotion();
            FillOverlay();
            _renderer.DrawPlots(_looks, Hovered, Selected, _starter, time, still, _palette.Walls, _overlay);
            _renderer.DrawClouds(dt, still);
        }

        void FillOverlay()
        {
            CityOverlay o = _overlay;
            o.Utilities = _ctx.Game.City?.Utilities;
            o.Utilisation = _ctx.CityResult?.Utilisation;
            o.Heatmap = Hud.Heatmap;
            o.Filter = Hud.Filter;
            o.HeatmapPalette = _heatmap.Palette;
            Hud.SetLegendPalette(_heatmap.Palette);
            o.SelectedMain = Hud.SelectedMain;
            o.Violations = _ctx.CityViolations;
            o.RouteUtility = Hud.Utility;
            o.RouteDiameterMm = Hud.DiameterMm;
            o.Time = UnityEngine.Time.unscaledTime;
            o.ReduceMotion = _reduceMotion();
            o.Rings?.RemoveAll(r => o.Time - r.Start > CityRenderer.RingSeconds);
            o.Icons.Clear();
            foreach (Plot p in _city.Plots)
            {
                BuildingSession? s = _ctx.FindSession(p.Id);
                o.Icons.Add(s == null ? RoofIcon.None : IconOf(s));
            }
        }

        RoofIcon IconOf(BuildingSession s)
        {
            Game.Failures.GasSafety gas = s.GasSafety;
            if (gas.TimerRunning) return RoofIcon.Alarm;
            if (gas.Evacuated) return RoofIcon.Evacuated;
            if (s.Record.Failures.Leaks.Count > 0) return s.Record.Failures.Repairs.Count > 0 ? RoofIcon.Crew : RoofIcon.Leak;
            if (gas.CertificateExpired || (s.Record.Failures.Certificate.Issued && s.Record.Failures.Certificate.Reminded)) return RoofIcon.Certificate;
            if (s.Record.Failures.Bursts.Count > 0 || s.Record.Failures.DamagedEquipment.Count > 0) return s.Record.Failures.Repairs.Count > 0 ? RoofIcon.Crew : RoofIcon.Burst;
            if (s.Record.PanelTripped) return RoofIcon.PanelTripped;
            // Only what leak sensors can see floats over the roof; otherwise weeping shows on the stress heatmap.
            foreach (var kv in s.Record.Failures.Stress)
                if (kv.Value >= _ctx.Data.Balancing.Failure.WeepThreshold && s.Record.Failures.WeepAlerted.Contains(kv.Key)) return RoofIcon.Weeping;
            return RoofIcon.None;
        }

        int PlotAt(Vector2 world)
        {
            float t = (float)_city.TileMetres;
            int x = Mathf.FloorToInt(world.x / t), y = Mathf.FloorToInt(world.y / t);
            Plot? p = _city.PlotAt(x, y);
            return p != null ? _city.Plots.IndexOf(p) : -1;
        }

        public void Dispose()
        {
            Hud.Dispose();
            Object.Destroy(_root);
        }
    }
}
