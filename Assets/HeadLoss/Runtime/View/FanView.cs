using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>Fan symbol at an intake: a housing ring and blades that turn with the fan's flow.</summary>
    public sealed class FanView : MonoBehaviour
    {
        Transform? _blades;
        float _angle;
        /// <summary>Blade rotation, degrees per second.</summary>
        public float Spin;

        public void Build(Vector2 centre, ViewPalette palette, Material material)
        {
            transform.localPosition = new Vector3(centre.x, centre.y, 0.8f);
            var ring = new MeshBuilder("FanHousing");
            ring.Disc(Vector2.zero, 0.42f, 28, 0.02f, palette.Paper);
            ring.Arc(Vector2.zero, 0.38f, 0.06f, 0, Mathf.PI * 2, 28, 0, palette.Walls);
            ring.Upload();
            RenderLayers.Attach(gameObject, ring.Mesh, material, RenderLayers.Equipment);

            var blades = new GameObject("Blades");
            blades.transform.SetParent(transform, false);
            blades.transform.localPosition = new Vector3(0, 0, -0.01f);
            var b = new MeshBuilder("FanBlades");
            Color c = palette.Walls;
            c.a = 0.85f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2 / 3;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                b.Line(d * 0.06f, d * 0.32f, 0.11f, 0, c);
            }
            b.Disc(Vector2.zero, 0.07f, 12, 0, palette.Walls);
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
}
