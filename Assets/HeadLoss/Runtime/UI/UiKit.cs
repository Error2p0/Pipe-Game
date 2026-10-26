using System;
using HeadLoss.Game.Economy;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>Builds UI Toolkit elements in the game's flat style (colours from <c>game.json</c>, layout from USS).</summary>
    public sealed class UiKit
    {
        public ViewPalette P { get; }

        public UiKit(ViewPalette palette) => P = palette;

        /// <summary>A rounded paper panel with the given layout class.</summary>
        public VisualElement Panel(VisualElement parent, string cls)
        {
            var v = new VisualElement();
            v.AddToClassList("hl-panel");
            v.AddToClassList(cls);
            Color bg = P.Paper;
            bg.a = 0.95f;
            v.style.backgroundColor = bg;
            Border(v, P.GridLines);
            parent.Add(v);
            return v;
        }

        public static void Border(VisualElement v, Color c) =>
            v.style.borderTopColor = v.style.borderBottomColor = v.style.borderLeftColor = v.style.borderRightColor = c;

        public Button Button(VisualElement? parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("hl-button");
            Style(b, false);
            parent?.Add(b);
            return b;
        }

        /// <summary>Active buttons are filled (selected tool, current mode).</summary>
        public void Style(Button b, bool active)
        {
            b.style.backgroundColor = active ? P.Walls : P.Paper;
            b.style.color = active ? P.Paper : P.Text;
            Border(b, P.Walls);
            b.EnableInClassList("hl-button--active", active);
        }

        public static Label Text(VisualElement parent, string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        public static VisualElement Row(VisualElement parent)
        {
            var r = new VisualElement();
            r.AddToClassList("hl-row");
            parent.Add(r);
            return r;
        }

        /// <summary>Calm zone colours; never used alone (every zone also has a word).</summary>
        public Color ZoneColour(OutletZone z) => z switch
        {
            OutletZone.Overdelivery => P.Good,
            OutletZone.Working => P.Text,
            OutletZone.AboveCodeMax => P.Danger,
            _ => P.Warning,
        };

        public static string ZoneWord(OutletZone z) => z switch
        {
            OutletZone.NotConnected => "not connected",
            OutletZone.BelowMinimum => "below minimum",
            OutletZone.Working => "working",
            OutletZone.Overdelivery => "overdelivery",
            OutletZone.AboveCodeMax => "above code max",
            _ => z.ToString(),
        };
    }
}
