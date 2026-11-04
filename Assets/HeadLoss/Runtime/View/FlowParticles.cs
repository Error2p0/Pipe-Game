using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Session;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Flow particles: small GPU-instanced discs moving along each duct at a speed proportional to the
    /// local velocity (clamped for readability), in every heatmap mode. Pooled buffers, no per-frame allocation.
    /// </summary>
    public sealed class FlowParticles : MonoBehaviour
    {
        const float Z = 0.5f;
        ParticlesDef? _def;
        Mesh? _disc;
        Material? _material;
        readonly List<Matrix4x4> _matrices = new List<Matrix4x4>(4096);
        RenderParams _params;
        double _time;

        /// <summary>Turns off speed variation (reduce motion); particles then move at one calm speed.</summary>
        public bool ReduceMotion;

        public void Init(ParticlesDef def, Material material)
        {
            _def = def;
            _material = material;
            _material.enableInstancing = true;
            _disc = BuildDisc(12);
            _params = new RenderParams(_material)
            {
                worldBounds = new Bounds(Vector3.zero, new Vector3(10000, 10000, 100)),
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                sortingOrder = RenderLayers.Particles,
            };
            if (_matrices.Capacity < def.MaxParticles) _matrices.Capacity = def.MaxParticles;
        }

        static Mesh BuildDisc(int sides)
        {
            var v = new Vector3[sides + 1];
            var c = new Color[sides + 1];
            var idx = new int[sides * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0);
            }
            for (int i = 0; i <= sides; i++) c[i] = Color.white;
            for (int i = 0; i < sides; i++)
            {
                idx[i * 3] = 0;
                idx[i * 3 + 1] = i + 1;
                idx[i * 3 + 2] = i == sides - 1 ? 1 : i + 2;
            }
            var m = new Mesh { name = "ParticleDisc", vertices = v, colors = c, triangles = idx };
            return m;
        }

        /// <param name="dt">Seconds since the last frame.</param>
        public void Draw(DuctViewInput input, float dt)
        {
            if (_def == null || _disc == null) return;
            _time += dt;
            _matrices.Clear();
            foreach (NetworkView view in input.Views)
            {
                if (view.Solve == null) continue;
                bool surge = !ReduceMotion && input.Surging?.Invoke(view.Fluid) == true;
                // Only the network in focus moves; moving dots would make the others read as the one being worked on. A surge still shows.
                if (view.Active || surge) Add(view.Geometry, view.Solve, surge);
            }
            if (_matrices.Count > 0) Graphics.RenderMeshInstanced(_params, _disc, 0, _matrices);
        }

        /// <param name="surge">A surge rides on this network: a short ripple along its pipes (nothing flashes, <c>05-simulation.md</c> §6).</param>
        void Add(DuctGeometry geometry, Game.Session.FluidSolve solve, bool surge)
        {
            float size = (float)_def!.Size;
            float spacing = (float)_def.SpacingMetres;
            var scale = new Vector3(size, size, 1);
            foreach (DuctGeometry.Segment s in geometry.Segments)
            {
                if (!solve.Compiled.SegmentLink.TryGetValue(s.Id, out int link)) continue;
                double v = solve.Result.Velocity[link];
                if (System.Math.Abs(v) < _def.MinVisibleVelocity) continue;
                float speed = ReduceMotion
                    ? (float)_def.MinSpeed * 2
                    : Mathf.Clamp((float)(System.Math.Abs(v) * _def.SpeedFactor), (float)_def.MinSpeed, (float)_def.MaxSpeed);
                float length = Vector2.Distance(s.A, s.B);
                if (length < 1e-3f) continue;
                Vector2 dir = (s.B - s.A) / length;
                // Positive flow runs A → B (segment NodeA → NodeB = link From → To).
                float phase = (float)((_time * speed + s.Id * 0.37) % spacing);
                if (v < 0) phase = spacing - phase;
                for (float d = phase; d < length; d += spacing)
                {
                    if (_matrices.Count >= _def.MaxParticles) break;
                    Vector2 p = s.A + dir * d;
                    Vector3 sized = scale;
                    if (surge)
                    {
                        float ripple = 1f + 0.9f * Mathf.Max(0f, Mathf.Sin((float)(_time * 6.0) - d * 1.4f));
                        sized = new Vector3(size * ripple, size * ripple, 1);
                    }
                    _matrices.Add(Matrix4x4.TRS(new Vector3(p.x, p.y, Z), Quaternion.identity, sized));
                }
            }
        }
    }
}
