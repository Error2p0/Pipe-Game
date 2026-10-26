using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Symbols for the design's equipment and inline parts (intake fan or crank, inline fans, dampers). Views are
    /// recreated when the layout changes and animated from the latest solve (fans spin with their flow, the crank
    /// with its speed, damper blades follow their opening).
    /// </summary>
    public sealed class EquipmentLayer : MonoBehaviour
    {
        ViewPalette _palette = null!;
        Material _material = null!;
        DataRepository _data = null!;
        string _builtFingerprint = "\u0000";
        readonly Dictionary<string, FanView> _fans = new Dictionary<string, FanView>();
        readonly Dictionary<TilePos, FanView> _inlineFans = new Dictionary<TilePos, FanView>();
        readonly Dictionary<TilePos, DamperView> _dampers = new Dictionary<TilePos, DamperView>();

        public void Init(DataRepository data, ViewPalette palette, Material material)
        {
            _data = data;
            _palette = palette;
            _material = material;
        }

        /// <summary>Recreates the symbols when the equipment or part layout changed.</summary>
        public void Sync(BuildingLayout layout, BuildingState design)
        {
            string fp = Fingerprint(design);
            if (fp == _builtFingerprint) return;
            _builtFingerprint = fp;
            foreach (Transform child in transform) Destroy(child.gameObject);
            _fans.Clear();
            _inlineFans.Clear();
            _dampers.Clear();

            foreach (EquipmentState e in design.Equipment)
            {
                EntryOpeningDef? o = layout.FindOpening(e.OpeningId);
                if (o == null) continue;
                bool crank = _data.FindEquipment(e.EquipmentId)?.SpeedControl == "crank";
                FanView v = Child<FanView>("Intake " + e.EquipmentId);
                v.Build(DuctGeometry.Centre(new TilePos(o.X, o.Y)), _palette, _material, crankHandle: crank);
                _fans[e.OpeningId] = v;
            }
            foreach (DuctNode n in design.Air.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline) continue;
                Vector2 c = DuctGeometry.Centre(n.Pos), dir = new Vector2(n.DirX, n.DirY);
                if (_data.FindEquipment(n.PartId) != null)
                {
                    FanView v = Child<FanView>("Inline fan");
                    v.Build(c, _palette, _material, 0.32f, false, dir);
                    _inlineFans[n.Pos] = v;
                }
                else
                {
                    DamperView d = Child<DamperView>("Damper");
                    d.Build(c, dir, _palette, _material);
                    d.SetOpening(n.Opening);
                    _dampers[n.Pos] = d;
                }
            }
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        static string Fingerprint(BuildingState s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (EquipmentState e in s.Equipment) sb.Append(e.OpeningId).Append(':').Append(e.EquipmentId).Append(';');
            foreach (DuctNode n in s.Air.Nodes)
                if (n.Kind == DuctNodeKind.Inline) sb.Append(n.X).Append(',').Append(n.Y).Append(n.PartId).Append(n.DirX).Append(n.DirY).Append(';');
            return sb.ToString();
        }

        /// <summary>Per frame: spin rates and damper angles from the design and its latest solve.</summary>
        /// <param name="degreesPerM3h">Blade animation rate per m³/h of flow.</param>
        public void Animate(BuildingSession s, double degreesPerM3h)
        {
            NetworkSnapshot? snap = s.Current;
            foreach (var kv in _fans)
            {
                double q = 0;
                if (snap != null && snap.Compiled.FanLink.TryGetValue(kv.Key, out int link)) q = snap.Result.Flow[link];
                kv.Value.Spin = (float)(Units.M3sToM3h(q) * degreesPerM3h);
            }
            foreach (var kv in _inlineFans)
            {
                double q = 0;
                DuctNode? n = snap?.State.Air.NodeAt(kv.Key);
                if (snap != null && n != null && snap.Compiled.InlineLink.TryGetValue(n.Id, out int link)) q = snap.Result.Flow[link];
                kv.Value.Spin = (float)(Units.M3sToM3h(q) * degreesPerM3h);
            }
            foreach (var kv in _dampers)
                if (s.State.Air.NodeAt(kv.Key) is DuctNode d) kv.Value.SetOpening(d.Opening);
        }
    }
}
