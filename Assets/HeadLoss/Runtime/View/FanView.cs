using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Fan symbol: a housing ring and blades that turn with the fan's flow. Variants: a hand crank (a handle on
    /// the blades) and an inline fan (smaller, with an arrow for the air direction).
    /// </summary>
    public sealed class FanView : MonoBehaviour
    {
        Transform? _blades;
        float _angle;
        /// <summary>Blade rotation, degrees per second.</summary>
        public float Spin;

        /// <param name="radius">Housing radius, m (0.42 at an intake).</param>
        /// <param name="crankHandle">Draw a crank handle on the rotor.</param>
        /// <param name="arrow">Air direction (zero = none).</param>
        public void Build(Vector2 centre, ViewPalette palette, Material material, float radius = 0.42f, bool crankHandle = false, Vector2 arrow = default)
        {
            transform.localPosition = new Vector3(centre.x, centre.y, 0.8f);
            float k = radius / 0.42f;
            var ring = new MeshBuilder("FanHousing");
            ring.Disc(Vector2.zero, radius, 28, 0.02f, palette.Paper);
            ring.Arc(Vector2.zero, radius - 0.04f * k, 0.06f * k, 0, Mathf.PI * 2, 28, 0, palette.Walls);
            if (arrow != Vector2.zero)
            {
                // A chevron outside the ring pointing the way the air goes.
                Vector2 d = arrow.normalized, n = new Vector2(-d.y, d.x);
                Vector2 tip = d * (radius + 0.16f);
                Color c = palette.Walls;
                ring.Line(tip, tip - d * 0.12f + n * 0.1f, 0.05f, 0, c);
                ring.Line(tip, tip - d * 0.12f - n * 0.1f, 0.05f, 0, c);
            }
            ring.Upload();
            RenderLayers.Attach(gameObject, ring.Mesh, material, RenderLayers.Equipment);

            var blades = new GameObject("Blades");
            blades.transform.SetParent(transform, false);
            blades.transform.localPosition = new Vector3(0, 0, -0.01f);
            var b = new MeshBuilder("FanBlades");
            Color col = palette.Walls;
            col.a = 0.85f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2 / 3;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                b.Line(d * 0.06f * k, d * 0.32f * k, 0.11f * k, 0, col);
            }
            b.Disc(Vector2.zero, 0.07f * k, 12, 0, palette.Walls);
            if (crankHandle)
            {
                // Arm out past the ring, then a round grip.
                b.Line(Vector2.zero, new Vector2(radius + 0.12f, 0), 0.07f, -0.005f, palette.Text);
                b.Disc(new Vector2(radius + 0.12f, 0), 0.09f, 14, -0.006f, palette.Text);
            }
            b.Upload();
            RenderLayers.Attach(blades, b.Mesh, material, RenderLayers.Equipment + 1);
            _blades = blades.transform;
        }

        void Update()
        {
            if (_blades == null) return;
            _angle = (_angle - Spin * Time.deltaTime) % 360f;
            _blades.localRotation = Quaternion.Euler(0, 0, _angle);
        }
    }

    /// <summary>Damper symbol: a small ring with a blade that turns across the duct as it closes.</summary>
    public sealed class DamperView : MonoBehaviour
    {
        Transform? _blade;

        public void Build(Vector2 centre, Vector2 along, ViewPalette palette, Material material)
        {
            transform.localPosition = new Vector3(centre.x, centre.y, 0.8f);
            var ring = new MeshBuilder("DamperHousing");
            ring.Disc(Vector2.zero, 0.3f, 24, 0.02f, palette.Paper);
            ring.Arc(Vector2.zero, 0.27f, 0.05f, 0, Mathf.PI * 2, 24, 0, palette.Walls);
            ring.Upload();
            RenderLayers.Attach(gameObject, ring.Mesh, material, RenderLayers.Equipment);
            var blade = new GameObject("Blade");
            blade.transform.SetParent(transform, false);
            blade.transform.localPosition = new Vector3(0, 0, -0.01f);
            var b = new MeshBuilder("DamperBlade");
            b.Line(new Vector2(-0.24f, 0), new Vector2(0.24f, 0), 0.07f, 0, palette.Text);
            b.Disc(Vector2.zero, 0.05f, 10, -0.005f, palette.Text);
            b.Upload();
            RenderLayers.Attach(blade, b.Mesh, material, RenderLayers.Equipment + 1);
            _blade = blade.transform;
            _along = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
        }

        float _along;

        /// <summary>Blade along the duct when open (θ = 1), across it when nearly closed.</summary>
        public void SetOpening(double theta)
        {
            if (_blade == null) return;
            float closed = (1f - Mathf.Clamp01((float)theta)) * 90f;
            _blade.localRotation = Quaternion.Euler(0, 0, _along + closed);
        }
    }
}
