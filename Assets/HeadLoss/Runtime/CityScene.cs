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
        bool _panning;
        float _lastClick = -10f;
        int _lastClickPlot = -1;
        int _starter;

        public CityScene(GameContext ctx, Materials materials, ViewPalette palette, UiKit ui, CameraController camera, BuildingInput input,
            VisualElement uiRoot, System.Func<bool> reduceMotion)
        {
            _ctx = ctx;
            _city = ctx.City!;
            _camera = camera;
            _input = input;
            _palette = palette;
            _reduceMotion = reduceMotion;
            _root = new GameObject("City " + _city.Name);
            _renderer = _root.AddComponent<CityRenderer>();
            _renderer.Build(_city, ctx.Data.Game.Theme.City, materials.Building, materials.Ducts, materials.Overlay);
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

            Hovered = overUi ? -1 : PlotAt(_camera.ScreenToWorld(screen));
            if (!overUi && _input.Primary.WasPressedThisFrame())
            {
                if (Hovered >= 0 && Hovered == _lastClickPlot && time - _lastClick < 0.4f) Hud.OpenRequested = _city.Plots[Hovered].Id;
                Selected = Hovered;
                _lastClick = time;
                _lastClickPlot = Hovered;
            }
            if (_input.Commit.WasPressedThisFrame() && Selected >= 0) Hud.OpenRequested = _city.Plots[Selected].Id;
            if (_input.Cancel.WasPressedThisFrame())
            {
                if (Selected >= 0) Selected = -1;
                else Hud.MenuRequested = true;
            }
            if (Hud.OpenRequested != null && !_ctx.CanOpen(Hud.OpenRequested)) Hud.OpenRequested = null;

            int shown = Selected >= 0 ? Selected : Hovered;
            Hud.Update(time, shown >= 0 ? _city.Plots[shown].Id : "", Selected >= 0);
        }

        public void LateUpdate(float dt, float time)
        {
            for (int i = 0; i < _city.Plots.Count; i++)
            {
                Plot p = _city.Plots[i];
                BuildingSession? s = _ctx.FindSession(p.Id);
                _looks[i] = !_ctx.CityBuildings!.Available(p) ? PlotLook.Locked : s != null && s.CommittedEvaluation.Online ? PlotLook.Online : PlotLook.Offline;
            }
            Hud.PositionStartHere();
            bool still = _reduceMotion();
            _renderer.DrawPlots(_looks, Hovered, Selected, _starter, time, still, _palette.Walls);
            _renderer.DrawClouds(dt, still);
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
