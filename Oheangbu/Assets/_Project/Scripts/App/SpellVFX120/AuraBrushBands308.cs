using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>What the bands read: whether a buff runs, when it was last cast and in which order (presentation, read only).</summary>
    public interface IBuffBandSource308
    {
        bool Started(char letter, float now, out float since, out int order);
    }

    // The combat wiring's two buff stores: the #308 timers (self buffs and companions) and the five EA buffs.
    public sealed class WiringBandSource308 : IBuffBandSource308
    {
        private readonly CombatLoopWiring _wiring;
        public WiringBandSource308(CombatLoopWiring wiring) { _wiring = wiring; }

        public bool Started(char letter, float now, out float since, out int order)
        {
            since = 0f; order = 0;
            if (_wiring == null) return false;
            var buffs = ((ISpellCastHost)_wiring).Buffs;
            if (buffs != null && buffs.Started(letter, now, out since, out order)) return true;
            var ea = _wiring.EABuffs;
            return ea != null && ea.Started(letter, now, out since);
        }
    }

    // #308 D308-127 (debate 11 A, debate 14 - TEST): the buffs the player carries, shown as pigment bands wound round the great
    // brush (BuffBands308SO: which glyph, which of the three places). Polls the buff stores every frame (the EA buffs never pass
    // the spell presenter, so a presenter hook would miss them); a recast of a region's newest buff, or a newer buff of the same
    // region, winds that region's band on again in its own pigment. Each band is drawn on both brushes the player holds (world
    // and close-up) and copies its brush renderer's enabled state, layer and shadows. While a weapon head is on the grip the
    // bands sit below the hand (BuffBands308SO.Form). Presentation only: rules never read it.
    [ExecuteAlways]
    [DefaultExecutionOrder(1150)]   // after WorldMacroPlayerGestureRig (1000) and GreatBrushWeapon308's grow
    public sealed class AuraBrushBands308 : MonoBehaviour
    {
        private static readonly BandRegion308[] Regions = { BandRegion308.Attack, BandRegion308.Body, BandRegion308.Ink };

        private sealed class Unit
        {
            public GameObject Object; public Mesh Mesh; public MeshRenderer Renderer;
            public int[] Order;          // the band's triangles, quad by quad along the wind
            public int[] Bottom, Top, Paper;
            public float Shown = -1f;    // the wound share the mesh shows now
        }

        private sealed class Brush
        {
            public Transform Root, Grip; public Renderer Body;
            public readonly Unit[] Units = new Unit[4];   // by BandRegion308
        }

        private struct Showing { public int Entry; public float Born, Gone; public float Since; public int Order; }

        private BuffBands308SO _bands;
        private IBuffBandSource308 _source;
        private readonly List<Brush> _brushes = new List<Brush>();
        private readonly Showing[] _showing = new Showing[4];
        private bool[] _on; private float[] _since; private int[] _order; private BandRegion308[] _regions;
        private readonly List<int> _scratch = new List<int>(512);
        private bool _fresh = true, _explicit;
        private float _nextLook;
        private Material[] _two = new Material[2];

        /// <summary>Checks: the glyph a region shows now (default = none) and how far its band is wound (0..1).</summary>
        public char LetterOf(BandRegion308 region) => _bands != null && _showing[(int)region].Entry >= 0 && float.IsNaN(_showing[(int)region].Gone) ? _bands.Entries[_showing[(int)region].Entry].Letter[0] : default;
        public float WoundOf(BandRegion308 region, int brush = 0) => brush < _brushes.Count && _brushes[brush].Units[(int)region] != null ? Mathf.Max(0f, _brushes[brush].Units[(int)region].Shown) : 0f;
        public Transform UnitOf(BandRegion308 region, int brush = 0) => brush < _brushes.Count && _brushes[brush].Units[(int)region] != null ? _brushes[brush].Units[(int)region].Object.transform : null;
        public Renderer RendererOf(BandRegion308 region, int brush = 0) => brush < _brushes.Count && _brushes[brush].Units[(int)region] != null ? _brushes[brush].Units[(int)region].Renderer : null;
        public int BrushCount => _brushes.Count;
        public bool Formed(int brush = 0) => brush < _brushes.Count && _brushes[brush].Grip != null && _brushes[brush].Grip.GetComponentInChildren<GreatBrushWeapon308>(true) != null;

        public void Bind(IBuffBandSource308 source, BuffBands308SO bands)
        {
            if (_bands != bands) Clear();
            _source = source; _bands = bands; _fresh = true;
            for (int i = 0; i < _showing.Length; i++) _showing[i] = new Showing { Entry = -1, Gone = float.NaN };
            int n = bands != null ? bands.Entries.Length : 0;
            _on = new bool[n]; _since = new float[n]; _order = new int[n]; _regions = new BandRegion308[n];
            for (int i = 0; i < n; i++) _regions[i] = bands.Entries[i].Region;
        }

        /// <summary>Checks: draw on these brush roots instead of the player rig's.</summary>
        public void AttachTo(params Transform[] roots)
        {
            Clear(); _explicit = true;
            foreach (var root in roots) Add(root);
        }

        private void LateUpdate() { if (Application.isPlaying) Step(Time.time); }

        public void Step(float now)
        {
            if (_bands == null || _source == null || _on == null) return;
            if (!_explicit && now >= _nextLook && (_brushes.Count == 0 || _brushes.Exists(b => b.Root == null))) { _nextLook = now + .5f; Look(); }
            var entries = _bands.Entries;
            for (int i = 0; i < entries.Length; i++)
                _on[i] = !string.IsNullOrEmpty(entries[i].Letter) && _source.Started(entries[i].Letter[0], now, out _since[i], out _order[i]);
            foreach (var region in Regions)
            {
                int r = (int)region;
                int newest = BuffBands308SO.Newest(region, _regions, _on, _since, _order, entries.Length);
                ref var s = ref _showing[r];
                if (newest >= 0)
                {
                    // a newer buff of the region, or a recast of the one shown: wind it on again (the first frame after a bind
                    // shows what already runs, fully wound)
                    bool again = s.Entry != newest || !float.IsNaN(s.Gone) || _since[newest] != s.Since || _order[newest] != s.Order;
                    // (the wind starts at the cast itself, not at the frame this poll first sees it)
                    if (again) s = new Showing { Entry = newest, Born = _fresh ? float.NegativeInfinity : Mathf.Min(_since[newest], now), Gone = float.NaN, Since = _since[newest], Order = _order[newest] };
                }
                else if (s.Entry >= 0 && float.IsNaN(s.Gone)) s.Gone = now;
            }
            _fresh = false;
            foreach (var brush in _brushes) Draw(brush, now);
        }

        private void Draw(Brush brush, float now)
        {
            if (brush.Root == null || brush.Grip == null) return;
            bool formed = brush.Grip.GetComponentInChildren<GreatBrushWeapon308>(true) != null;
            bool shownBody = brush.Body == null || (brush.Body.enabled && brush.Body.gameObject.activeInHierarchy);
            foreach (var region in Regions)
            {
                var s = _showing[(int)region];
                float wound = 0f;
                if (s.Entry >= 0)
                {
                    wound = float.IsNegativeInfinity(s.Born) ? 1f : Mathf.Clamp01((now - s.Born) / _bands.EnterSeconds);
                    if (!float.IsNaN(s.Gone)) wound = Mathf.Min(wound, 1f - Mathf.Clamp01((now - s.Gone) / _bands.ExitSeconds));
                }
                var unit = brush.Units[(int)region];
                if (wound <= 0f)
                {
                    if (unit != null) { if (unit.Renderer.enabled) unit.Renderer.enabled = false; unit.Shown = -1f; }   // (-1: the next wind rewrites the mesh)
                    if (s.Entry >= 0 && !float.IsNaN(s.Gone) && now - s.Gone >= _bands.ExitSeconds) _showing[(int)region] = new Showing { Entry = -1, Gone = float.NaN };
                    continue;
                }
                if (unit == null) unit = brush.Units[(int)region] = Make(brush, region);
                var entry = _bands.Entries[s.Entry];
                _two[0] = _bands.PigmentOf(entry.Element); _two[1] = _bands.Paper;
                if (unit.Renderer.sharedMaterials.Length != 2 || unit.Renderer.sharedMaterials[0] != _two[0] || unit.Renderer.sharedMaterials[1] != _two[1]) unit.Renderer.sharedMaterials = _two;
                unit.Object.transform.localPosition = new Vector3(0f, (formed ? _bands.Form : _bands.Plain).Of(region), 0f);
                Wind(unit, SmoothWound(wound));
                unit.Renderer.enabled = shownBody;
                if (brush.Body != null)
                {
                    unit.Renderer.shadowCastingMode = brush.Body.shadowCastingMode;
                    if (unit.Object.layer != brush.Body.gameObject.layer) unit.Object.layer = brush.Body.gameObject.layer;
                }
            }
        }

        private static float SmoothWound(float x) => x * x * (3f - 2f * x);

        // the band's quads come on along the wind (from its lower end round and up); the paper underline with the first of them
        private void Wind(Unit unit, float wound)
        {
            if (Mathf.Abs(wound - unit.Shown) < .004f) return;
            unit.Shown = wound;
            _scratch.Clear();
            int quads = unit.Order.Length / 6, show = Mathf.Clamp(Mathf.CeilToInt(quads * wound), 0, quads);
            for (int i = 0; i < show * 6; i++) _scratch.Add(unit.Order[i]);
            if (show > 0) _scratch.AddRange(unit.Bottom);
            if (wound >= 1f) _scratch.AddRange(unit.Top);
            unit.Mesh.SetTriangles(_scratch, 0, false);
            _scratch.Clear();
            if (show > 0) _scratch.AddRange(unit.Paper);
            unit.Mesh.SetTriangles(_scratch, 1, false);
        }

        private void Look()
        {
            Clear();
            var rig = transform.root.GetComponentInChildren<WorldMacroPlayerGestureRig>(true);
            if (rig == null) rig = FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (rig == null) return;
            Add(rig.WorldBrushRoot308); Add(rig.NearBrushRoot308);
        }

        private void Add(Transform root)
        {
            if (root == null) return;
            var grip = Find(root, GreatBrushWeapon308.GripName);
            if (grip == null) return;
            Renderer body = null;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.GetComponentInParent<GreatBrushWeapon308>() != null || renderer.name.StartsWith("BuffBand308")) continue;
                foreach (var m in renderer.sharedMaterials) if (m != null && m.name.IndexOf("Bristle", System.StringComparison.OrdinalIgnoreCase) >= 0) { body = renderer; break; }
                if (body != null) break;
            }
            _brushes.Add(new Brush { Root = root, Grip = grip, Body = body });
        }

        private Unit Make(Brush brush, BandRegion308 region)
        {
            var go = new GameObject("BuffBand308_" + region) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(brush.Grip, false);
            var unit = new Unit { Object = go, Mesh = new Mesh { name = "BuffBand308", hideFlags = HideFlags.DontSave } };
            unit.Mesh.MarkDynamic();
            Build(unit);
            go.AddComponent<MeshFilter>().sharedMesh = unit.Mesh;
            unit.Renderer = go.AddComponent<MeshRenderer>();
            unit.Renderer.receiveShadows = true; unit.Renderer.lightProbeUsage = LightProbeUsage.BlendProbes; unit.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            unit.Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            unit.Renderer.enabled = false;
            return unit;
        }

        // one unit along +Y from 0: the paper underline (submesh 1), then the pigment band with shallow grooves wound round it
        // (submesh 0; the grooves never cut the face: one band from afar, not rings). Caps close both ends onto the shaft.
        private void Build(Unit unit)
        {
            var b = _bands;
            const int Sectors = 20, Rows = 18;
            var v = new List<Vector3>(); var n = new List<Vector3>();
            float under = b.UnderlineLength, len = b.BandLength, top = under + len;
            int Ring(float y, System.Func<float, float> radius)
            {
                int first = v.Count;
                for (int i = 0; i <= Sectors; i++)
                {
                    float a = i * Mathf.PI * 2f / Sectors; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * radius(a) + Vector3.up * y); n.Add(d);
                }
                return first;
            }
            float Wound(float a, float y) => b.BandRadius - b.Groove * (.5f + .5f * Mathf.Cos(2f * Mathf.PI * (b.Turns * (y - under) / len + a / (2f * Mathf.PI))));
            var rows = new int[Rows + 1];
            for (int j = 0; j <= Rows; j++) { float y = under + len * j / Rows; rows[j] = Ring(y, a => Wound(a, y)); }
            var quads = new List<(float At, int A, int B, int C, int D)>();
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Sectors; i++)
                {
                    // the wind: a quad comes on when the brush going round and up reaches it
                    float at = (j + .5f) / Rows * b.Turns + (i + .5f) / Sectors;
                    quads.Add((at, rows[j] + i, rows[j] + i + 1, rows[j + 1] + i + 1, rows[j + 1] + i));
                }
            quads.Sort((p, q) => p.At.CompareTo(q.At));
            var order = new List<int>(quads.Count * 6);
            foreach (var q in quads) { order.Add(q.A); order.Add(q.C); order.Add(q.B); order.Add(q.A); order.Add(q.D); order.Add(q.C); }
            unit.Order = order.ToArray();
            int[] Cap(float y, float inner, System.Func<float, float> outer, bool up)
            {
                int a = Ring(y, _ => inner), c = Ring(y, outer);
                for (int i = a; i < a + Sectors + 1; i++) n[i] = up ? Vector3.up : Vector3.down;
                for (int i = c; i < c + Sectors + 1; i++) n[i] = up ? Vector3.up : Vector3.down;
                var t = new List<int>();
                for (int i = 0; i < Sectors; i++)
                    if (up) { t.Add(a + i); t.Add(c + i + 1); t.Add(c + i); t.Add(a + i); t.Add(a + i + 1); t.Add(c + i + 1); }
                    else { t.Add(a + i); t.Add(c + i); t.Add(c + i + 1); t.Add(a + i); t.Add(c + i + 1); t.Add(a + i + 1); }
                return t.ToArray();
            }
            unit.Bottom = Cap(under, b.ShaftRadius, a => Wound(a, under), false);
            unit.Top = Cap(top, b.ShaftRadius, a => Wound(a, top), true);
            int p0 = Ring(0f, _ => b.UnderlineRadius), p1 = Ring(under, _ => b.UnderlineRadius);
            var paper = new List<int>();
            for (int i = 0; i < Sectors; i++) { paper.Add(p0 + i); paper.Add(p1 + i + 1); paper.Add(p0 + i + 1); paper.Add(p0 + i); paper.Add(p1 + i); paper.Add(p1 + i + 1); }
            paper.AddRange(Cap(0f, b.ShaftRadius, _ => b.UnderlineRadius, false));
            unit.Paper = paper.ToArray();
            unit.Mesh.SetVertices(v); unit.Mesh.SetNormals(n);
            unit.Mesh.subMeshCount = 2;
            unit.Mesh.bounds = new Bounds(new Vector3(0f, top * .5f, 0f), new Vector3(b.BandRadius * 2.2f, top + .01f, b.BandRadius * 2.2f));
        }

        private void Clear()
        {
            foreach (var brush in _brushes)
                foreach (var unit in brush.Units)
                {
                    if (unit == null) continue;
                    if (Application.isPlaying) { Destroy(unit.Object); Destroy(unit.Mesh); } else { DestroyImmediate(unit.Object); DestroyImmediate(unit.Mesh); }
                }
            _brushes.Clear(); _explicit = false;
        }

        private void OnDestroy() => Clear();

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { var found = Find(child, name); if (found != null) return found; }
            return null;
        }
    }
}
