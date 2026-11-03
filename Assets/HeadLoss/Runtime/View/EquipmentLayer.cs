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
    /// Symbols for the design's equipment and inline parts on the floor on show: source equipment at its opening with its
    /// floor footprint outlined (fans and pumps spin with their flow, the crank with its speed), the panel, and the inline
    /// parts of both networks (fans, pumps, dampers and valves). Recreated when the layout or the floor changes.
    /// </summary>
    public sealed class EquipmentLayer : MonoBehaviour
    {
        ViewPalette _palette = null!;
        Material _material = null!;
        DataRepository _data = null!;
        string _builtFingerprint = "\u0000";
        readonly Dictionary<string, FanView> _fans = new Dictionary<string, FanView>();
        readonly Dictionary<(string, TilePos), FanView> _inlineFans = new Dictionary<(string, TilePos), FanView>();
        readonly Dictionary<(string, TilePos), DamperView> _dampers = new Dictionary<(string, TilePos), DamperView>();
        readonly Dictionary<(string, TilePos), SymbolView> _valves = new Dictionary<(string, TilePos), SymbolView>();
        MeshBuilder? _footprints;
        GameObject? _footprintGo;

        public void Init(DataRepository data, ViewPalette palette, Material material)
        {
            _data = data;
            _palette = palette;
            _material = material;
        }

        /// <summary>Recreates the symbols when the equipment, part layout or floor changed.</summary>
        public void Sync(BuildingLayout layout, BuildingState design, int floor)
        {
            string fp = Fingerprint(design, floor);
            if (fp == _builtFingerprint) return;
            _builtFingerprint = fp;
            foreach (Transform child in transform) Destroy(child.gameObject);
            _fans.Clear();
            _inlineFans.Clear();
            _dampers.Clear();
            _valves.Clear();
            _footprints = new MeshBuilder("Footprints");
            _footprintGo = new GameObject("Footprints");
            _footprintGo.transform.SetParent(transform, false);
            _footprintGo.transform.localPosition = new Vector3(0, 0, 0.82f);

            foreach (EquipmentState e in design.Equipment)
            {
                EntryOpeningDef? o = layout.FindOpening(e.OpeningId);
                EquipmentDef? def = _data.FindEquipment(e.EquipmentId);
                if (o == null || def == null || o.Floor != floor) continue;
                // The footprint it takes on the floor layer.
                List<TilePos>? tiles = EquipmentFits.Footprint(_data, layout, design, o, def);
                if (tiles != null && tiles.Count > 1)
                {
                    Color fill = _palette.Walls;
                    fill.a = 0.12f;
                    foreach (TilePos t in tiles) _footprints.Rect(t.X + 0.06f, t.Y + 0.06f, t.X + 0.94f, t.Y + 0.94f, 0, fill);
                }
                Vector2 c = DuctGeometry.Centre(BuildingLayout.PosOf(o));
                if (def.Category == "panel")
                {
                    SymbolView sv = Child<SymbolView>("Panel " + def.Id);
                    sv.Build(c, Vector2.right, SymbolKind.Panel, _palette.Walls, _palette, _material, 0.34f);
                    continue;
                }
                if (def.Curve.Count == 0) continue;  // a plain mains connection
                bool crank = def.SpeedControl == "crank";
                FanView v = Child<FanView>("Intake " + e.EquipmentId);
                v.Build(c, _palette, _material, def.Category == "water-mover" ? 0.34f : 0.42f, crankHandle: crank);
                _fans[e.OpeningId] = v;
            }
            _footprints.Upload();
            RenderLayers.Attach(_footprintGo, _footprints.Mesh, _material, RenderLayers.Equipment - 1);

            foreach (DuctNetworkState net in design.Networks)
            {
                Color tint = net.FluidId == "air" ? _palette.Walls : _palette.Fluid(net.FluidId);
                foreach (DuctNode n in net.Nodes)
                {
                    if (n.Kind != DuctNodeKind.Inline || n.Floor != floor) continue;
                    Vector2 c = DuctGeometry.Centre(n.Pos), dir = new Vector2(n.DirX, n.DirY);
                    var key = (net.FluidId, n.Pos);
                    EquipmentDef? eq = _data.FindEquipment(n.PartId);
                    if (eq != null && eq.Curve.Count > 0)
                    {
                        FanView v = Child<FanView>("Inline " + eq.Id);
                        v.Build(c, _palette, _material, 0.32f, false, dir);
                        _inlineFans[key] = v;
                        continue;
                    }
                    if (eq != null)
                    {
                        SymbolView tank = Child<SymbolView>("Tank");
                        tank.Build(c, dir, SymbolKind.Tank, tint, _palette, _material);
                        _valves[key] = tank;
                        continue;
                    }
                    FittingDef f = _data.Fitting(n.PartId);
                    if (f.Model == "valve" && f.MinOpening < 1)
                    {
                        DamperView d = Child<DamperView>(f.DisplayName);
                        d.Build(c, dir, _palette, _material);
                        d.SetOpening(n.Opening);
                        _dampers[key] = d;
                        continue;
                    }
                    SymbolKind kind = f.Id == "fire-damper" ? SymbolKind.FireDamper : f.Model switch
                    {
                        "check" => SymbolKind.Check,
                        "regulator" => SymbolKind.Regulator,
                        "relief" => SymbolKind.Relief,
                        _ => SymbolKind.Valve,
                    };
                    SymbolView sv = Child<SymbolView>(f.DisplayName);
                    sv.Build(c, dir, kind, tint, _palette, _material);
                    sv.SetOpen(n.On);
                    _valves[key] = sv;
                }
            }
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        static string Fingerprint(BuildingState s, int floor)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(floor).Append('|');
            foreach (EquipmentState e in s.Equipment) sb.Append(e.OpeningId).Append(':').Append(e.EquipmentId).Append(';');
            foreach (DuctNetworkState net in s.Networks)
                foreach (DuctNode n in net.Nodes)
                    if (n.Kind == DuctNodeKind.Inline) sb.Append(net.FluidId).Append(n.X).Append(',').Append(n.Y).Append(',').Append(n.Floor).Append(n.PartId).Append(n.DirX).Append(n.DirY).Append(';');
            return sb.ToString();
        }

        /// <summary>Per frame: spin rates, damper angles and valve states from the design and its latest solve.</summary>
        /// <param name="degreesPerM3h">Blade animation rate per m³/h of flow.</param>
        public void Animate(BuildingSession s, double degreesPerM3h)
        {
            NetworkSnapshot? snap = s.Current;
            foreach (var kv in _fans)
            {
                double q = 0;
                if (snap != null)
                    foreach (FluidSolve fs in snap.All)
                        if (fs.Compiled.FanLink.TryGetValue(kv.Key, out int link)) q = fs.Result.Flow[link] * (fs.Fluid == "air" ? 1 : 200);
                kv.Value.Spin = (float)(Units.M3sToM3h(q) * degreesPerM3h);
            }
            foreach (var kv in _inlineFans)
            {
                double q = 0;
                FluidSolve? fs = snap?.For(kv.Key.Item1);
                DuctNode? n = snap?.State.Net(kv.Key.Item1).NodeAt(kv.Key.Item2);
                if (fs != null && n != null && fs.Compiled.InlineLink.TryGetValue(n.Id, out int link)) q = fs.Result.Flow[link] * (fs.Fluid == "air" ? 1 : 200);
                kv.Value.Spin = (float)(Units.M3sToM3h(q) * degreesPerM3h);
            }
            foreach (var kv in _dampers)
                if (s.State.Net(kv.Key.Item1).NodeAt(kv.Key.Item2) is DuctNode d) kv.Value.SetOpening(d.Opening);
            foreach (var kv in _valves)
                if (s.State.Net(kv.Key.Item1).NodeAt(kv.Key.Item2) is DuctNode v) kv.Value.SetOpen(v.On);
        }
    }
}
