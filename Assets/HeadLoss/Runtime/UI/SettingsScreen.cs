using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Game.Save;
using HeadLoss.Unity.Interaction;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// Settings (<c>11-ui-visuals-audio.md</c> §Screens 7): audio sliders (master, ambience, UI), heatmap palette,
    /// theme, UI scale (80–150%), key rebinding, autosave interval and reduce motion. Volumes, palette, scale, motion and
    /// keys apply at once (<see cref="Changed"/>); a theme change applies when the screen closes.
    /// </summary>
    public sealed class SettingsScreen
    {
        readonly VisualElement _panel;
        readonly UiKit _ui;
        readonly BuildingInput _input;
        readonly PlayerSettings _s;
        readonly Action<PlayerSettings> _changed;
        readonly List<(InputAction action, int binding, Label shown)> _keys = new List<(InputAction, int, Label)>();
        InputActionRebindingExtensions.RebindingOperation? _rebind;
        Button? _light, _dark, _motion;

        /// <summary>The settings being edited (a copy; the caller stores it on close).</summary>
        public PlayerSettings Settings => _s;
        /// <summary>A key is being rebound (Esc then cancels the rebind, not the screen).</summary>
        public bool Rebinding => _rebind != null;

        public SettingsScreen(VisualElement parent, UiKit ui, DataRepository data, BuildingInput input, PlayerSettings settings,
            Action<PlayerSettings> changed, Action onClose)
        {
            _ui = ui;
            _input = input;
            _s = settings;
            _changed = changed;
            _panel = ui.Panel(parent, "hl-settings");
            VisualElement head = UiKit.Row(_panel);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            UiKit.Text(head, "Settings", "hl-title");
            ui.Button(head, "Close (Esc)", onClose);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("hl-shop-list");
            _panel.Add(scroll);

            UiKit.Text(scroll, "Sound", "hl-section");
            Percent(scroll, "Master", _s.MasterVolume, v => _s.MasterVolume = v);
            Percent(scroll, "Ambience (air, fans)", _s.AmbienceVolume, v => _s.AmbienceVolume = v);
            Percent(scroll, "Interface (clicks, chimes)", _s.UiVolume, v => _s.UiVolume = v);

            UiKit.Text(scroll, "Look", "hl-section");
            var palettes = new List<string>(data.Balancing.Heatmap.Palettes.Keys);
            var pal = new DropdownField("Heatmap palette", palettes, Math.Max(0, palettes.IndexOf(_s.Palette)));
            pal.AddToClassList("hl-dropdown");
            ui.StyleField(pal);
            pal.RegisterValueChangedCallback(e => { _s.Palette = e.newValue; Notify(); });
            scroll.Add(pal);
            VisualElement theme = UiKit.Row(scroll);
            UiKit.Text(theme, "Theme", "hl-readout-line").style.minWidth = 160;
            _light = ui.Button(theme, "Light", () => SetTheme("light"));
            _dark = ui.Button(theme, "Dark", () => SetTheme("dark"));
            UiKit.Text(scroll, "A new theme applies when you close the settings.", "hl-subtitle");
            var scale = new SliderInt("UI scale (%)", 80, 150) { value = (int)Math.Round(_s.UiScale * 100), showInputField = true };
            scale.AddToClassList("hl-slider");
            scale.RegisterValueChangedCallback(e => { _s.UiScale = e.newValue / 100.0; Notify(); });
            scroll.Add(scale);
            VisualElement motion = UiKit.Row(scroll);
            UiKit.Text(motion, "Reduce motion", "hl-readout-line").style.minWidth = 160;
            _motion = ui.Button(motion, "", () => { _s.ReduceMotion = !_s.ReduceMotion; Notify(); });
            UiKit.Text(scroll, "Steady particles, no glow breathing, no drifting clouds.", "hl-subtitle");

            UiKit.Text(scroll, "Saving", "hl-section");
            var autosave = new SliderInt("Autosave every (s)", 10, 300) { value = (int)Math.Round(_s.AutosaveSeconds), showInputField = true };
            autosave.AddToClassList("hl-slider");
            autosave.RegisterValueChangedCallback(e => { _s.AutosaveSeconds = e.newValue; Notify(); });
            scroll.Add(autosave);

            UiKit.Text(scroll, "Keys", "hl-section");
            foreach ((InputAction action, int binding, string label) in input.Rebindable())
            {
                VisualElement row = UiKit.Row(scroll);
                row.style.alignItems = Align.Center;
                UiKit.Text(row, label, "hl-readout-line").style.minWidth = 220;
                Label shown = UiKit.Text(row, "", "hl-readout-title");
                shown.style.minWidth = 120;
                InputAction a = action;
                int b = binding;
                ui.Button(row, "Change", () => StartRebind(a, b, shown));
                _keys.Add((action, binding, shown));
            }
            ui.Button(scroll, "Reset keys to defaults", ResetKeys).style.alignSelf = Align.FlexStart;
            UiKit.Text(scroll, "Esc, the mouse buttons and the wheel stay as they are.", "hl-subtitle");
            Refresh();
        }

        void Percent(VisualElement parent, string label, double value, Action<double> set)
        {
            var s = new SliderInt(label + " (%)", 0, 100) { value = (int)Math.Round(value * 100), showInputField = true };
            s.AddToClassList("hl-slider");
            s.RegisterValueChangedCallback(e => { set(e.newValue / 100.0); Notify(); });
            parent.Add(s);
        }

        void SetTheme(string theme)
        {
            _s.Theme = theme;
            Refresh();
        }

        void Notify()
        {
            Refresh();
            _changed(_s);
        }

        void Refresh()
        {
            if (_light != null) _ui.Style(_light, _s.Theme == "light");
            if (_dark != null) _ui.Style(_dark, _s.Theme == "dark");
            if (_motion != null)
            {
                _motion.text = _s.ReduceMotion ? "On" : "Off";
                _ui.Style(_motion, _s.ReduceMotion);
            }
            foreach ((InputAction action, int binding, Label shown) in _keys)
                shown.text = action.GetBindingDisplayString(binding);
        }

        void StartRebind(InputAction action, int binding, Label shown)
        {
            if (_rebind != null) return;
            shown.text = "Press a key…";
            _input.MouseKeyboard.Disable();
            _rebind = action.PerformInteractiveRebinding(binding)
                .WithControlsExcluding("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(_ => EndRebind())
                .OnCancel(_ => EndRebind())
                .Start();
        }

        void EndRebind()
        {
            _rebind?.Dispose();
            _rebind = null;
            _input.MouseKeyboard.Enable();
            _s.Bindings = _input.MouseKeyboard.SaveBindingOverridesAsJson();
            Notify();
        }

        void ResetKeys()
        {
            _input.MouseKeyboard.RemoveAllBindingOverrides();
            _s.Bindings = "";
            Notify();
        }

        public void Dispose()
        {
            if (_rebind != null)
            {
                _rebind.Cancel();
                _rebind?.Dispose();
                _rebind = null;
                _input.MouseKeyboard.Enable();
            }
            _panel.RemoveFromHierarchy();
        }
    }
}
