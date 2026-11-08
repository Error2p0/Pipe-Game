using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>Glyphs for parts that are not fans or dampers.</summary>
    public enum SymbolKind { Valve, Check, Regulator, Relief, Tank, Panel, FireDamper, Manifold }

    /// <summary>
    /// A small housing with a glyph: shutoff and ball valves (an hourglass, a bar across when closed), check valves (an
    /// arrow), regulators (an arrow through a box), relief valves (a spring with an outlet), pressure tanks and the
    /// electrical panel (<c>11-ui-visuals-audio.md</c>: never colour alone, every part has its own shape).
    /// </summary>
    public sealed class SymbolView : MonoBehaviour
    {
        Transform? _bar;

        public void Build(Vector2 centre, Vector2 along, SymbolKind kind, Color tint, ViewPalette palette, Material material, float radius = 0.3f)
        {
            transform.localPosition = new Vector3(centre.x, centre.y, 0.8f);
            var m = new MeshBuilder("Symbol");
            Vector2 d = along == Vector2.zero ? Vector2.right : along.normalized, n = new Vector2(-d.y, d.x);
            Color ink = palette.Text;
            if (kind == SymbolKind.Panel || kind == SymbolKind.Tank)
            {
                m.Rect(-radius, -radius, radius, radius, 0.02f, palette.Paper);
                m.Outline(-radius, -radius, radius, radius, 0.05f, 0f, tint);
            }
            else
            {
                m.Disc(Vector2.zero, radius, 24, 0.02f, palette.Paper);
                m.Arc(Vector2.zero, radius - 0.03f, 0.05f, 0, Mathf.PI * 2, 24, 0, tint);
            }
            float r = radius * 0.7f;
            switch (kind)
            {
                case SymbolKind.Valve:
                    // Hourglass: two lines crossing along the pipe.
                    m.Line(-d * r + n * r * 0.6f, d * r - n * r * 0.6f, 0.05f, -0.002f, ink);
                    m.Line(-d * r - n * r * 0.6f, d * r + n * r * 0.6f, 0.05f, -0.002f, ink);
                    break;
                case SymbolKind.Check:
                    m.Line(-d * r, d * r, 0.05f, -0.002f, ink);
                    m.Line(d * r, d * (r * 0.4f) + n * r * 0.5f, 0.05f, -0.002f, ink);
                    m.Line(d * r, d * (r * 0.4f) - n * r * 0.5f, 0.05f, -0.002f, ink);
                    m.Line(d * r * 0.6f + n * r * 0.6f, d * r * 0.6f - n * r * 0.6f, 0.05f, -0.002f, ink);
                    break;
                case SymbolKind.Regulator:
                    m.Outline(-r * 0.55f, -r * 0.55f, r * 0.55f, r * 0.55f, 0.04f, -0.002f, ink);
                    m.Line(-d * r, d * r, 0.04f, -0.003f, ink);
                    m.Line(d * r, d * (r * 0.5f) + n * r * 0.4f, 0.04f, -0.003f, ink);
                    break;
                case SymbolKind.Relief:
                    for (int i = 0; i < 4; i++)
                    {
                        float a = -r + i * r * 0.5f, b = a + r * 0.5f;
                        m.Line(n * a + d * (i % 2 == 0 ? -r * 0.4f : r * 0.4f), n * b + d * (i % 2 == 0 ? r * 0.4f : -r * 0.4f), 0.04f, -0.002f, ink);
                    }
                    break;
                case SymbolKind.Tank:
                    m.Rect(-r * 0.6f, -r * 0.8f, r * 0.6f, r * 0.2f, -0.002f, tint);
                    break;
                case SymbolKind.Panel:
                    m.Line(new Vector2(r * 0.15f, r * 0.8f), new Vector2(-r * 0.3f, 0), 0.06f, -0.002f, palette.Warning);
                    m.Line(new Vector2(-r * 0.3f, 0), new Vector2(r * 0.3f, 0), 0.06f, -0.002f, palette.Warning);
                    m.Line(new Vector2(r * 0.3f, 0), new Vector2(-r * 0.15f, -r * 0.8f), 0.06f, -0.002f, palette.Warning);
                    break;
                case SymbolKind.Manifold:
                    // A bar with valve stubs: a gas manifold on a junction.
                    m.Rect(-r * 0.7f, -r * 0.18f, r * 0.7f, r * 0.18f, -0.002f, tint);
                    m.Rect(-r * 0.5f, r * 0.18f, -r * 0.3f, r * 0.55f, -0.002f, tint);
                    m.Rect(r * 0.3f, r * 0.18f, r * 0.5f, r * 0.55f, -0.002f, tint);
                    break;
                case SymbolKind.FireDamper:
                    m.Line(-n * r, n * r, 0.07f, -0.002f, palette.Danger);
                    break;
            }
            m.Upload();
            RenderLayers.Attach(gameObject, m.Mesh, material, RenderLayers.Equipment);
            if (kind == SymbolKind.Valve)
            {
                var bar = new GameObject("Closed");
                bar.transform.SetParent(transform, false);
                bar.transform.localPosition = new Vector3(0, 0, -0.01f);
                var b = new MeshBuilder("ValveClosed");
                b.Line(-n * radius * 0.9f, n * radius * 0.9f, 0.09f, 0, palette.Warning);
                b.Upload();
                RenderLayers.Attach(bar, b.Mesh, material, RenderLayers.Equipment + 1);
                _bar = bar.transform;
            }
        }

        /// <summary>Shutoff valves: a warning bar across the pipe while closed.</summary>
        public void SetOpen(bool open)
        {
            if (_bar != null) _bar.gameObject.SetActive(!open);
        }
    }
}
