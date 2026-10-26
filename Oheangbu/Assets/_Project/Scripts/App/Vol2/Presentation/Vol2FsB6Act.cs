using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Batch 6: the body's own action on the exposed seam (the executor's FirstBody record - "bind x0.5 2s" / "burn" / "push 1m" / "wet 4s" / "cut"), drawn the
    /// same way by every 가른다 glyph of a body so the twenty share one grammar (the glyph colours it, adds its own around it). Mirrors only: the status sink owns
    /// the slow / the burn ticks / the push / the wet; the times here are the table's (bind 2 s, burn 2 s, wet 4 s) as the earlier batches drew them.
    /// End clauses (CSV 몸거동 끝): wood loosens into rootlets reaching forward, fire burns to ash rising, earth sinks / the ground lifts in plates, water runs down,
    /// metal snaps with shards to the side.</summary>
    public sealed class Vol2FsCutAct
    {
        public const float BindSeconds = 2f, BurnSeconds = 2f, WetSeconds = 4f;
        readonly Vol2FsKit _kit; readonly Transform _root; readonly Body _body; readonly int _seed; readonly Color _acc;
        ParticleSystem _a, _b, _c;
        Material _vine; Mesh _tubeMesh, _leafMesh, _stripMesh; readonly Vol2FsTubes _tubes = new Vol2FsTubes(); readonly Vol2FsLeaves _leaves = new Vol2FsLeaves(); readonly Vol2FsStrips _strips = new Vol2FsStrips();
        static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);

        sealed class One { public Transform T; public Bounds B0; public Vector3 Feet0, Dir; public float At; public int Index; public List<Vol2FsTubes.Tube> Coils = new List<Vol2FsTubes.Tube>(), Roots = new List<Vol2FsTubes.Tube>(); public int Leaf0, Leaf1; public bool RootsBuilt; }
        readonly List<One> _ones = new List<One>();
        public int Count => _ones.Count;

        public Vol2FsCutAct(Vol2FsKit kit, Transform root, Body body, int seed, Color accent)
        {
            _kit = kit; _root = root; _body = body; _seed = seed; _acc = accent;
            switch (body)
            {
                case Body.Wood:
                    _vine = kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein");
                    Vol2Fs.MeshObject(root, "ActVines", _vine, out _tubeMesh);
                    Vol2Fs.MeshObject(root, "ActLeaves", kit.Leaf(new Color(0.55f, 1f, 0.5f), new Color(0.4f, 1f, 0.45f) * 0.6f), out _leafMesh);
                    var leaf = Vol2FsB1.M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
                    _a = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(leaf), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.1f, Size1 = 0.22f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 300 }, root, "ActLeaf");
                    _b = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB1.M("fs_dot", new Color(0.4f, 1f, 0.45f), 4f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.03f, Size1 = 0.06f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 2f, Max = 300 }, root, "ActJade");
                    break;
                case Body.Fire:
                    _a = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB3.Fire(1.6f, 0.85f)), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 0.4f, Speed1 = 1.4f, Size0 = 0.25f, Size1 = 0.55f, Gravity = -0.6f, Noise = 0.8f, NoiseFreq = 1.4f, SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 0.3f, Max = 600 }, root, "ActFlame");
                    _b = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB1.M("fs_dot", new Color(1f, 0.55f, 0.2f), 4f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.4f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.5f, Max = 500 }, root, "ActEmber");
                    _c = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.25f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 400 }, root, "ActAsh");
                    break;
                case Body.Earth:
                    _a = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB3.Dust(new Color(0.48f, 0.34f, 0.25f, 1f), 0.55f)), Life0 = 0.7f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.4f, Max = 300 }, root, "ActDust");
                    _b = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(kit, "fs_rock", new Color(0.58f, 0.5f, 0.44f), 0f, 0.12f), Mesh = kit.SharedMesh("actRock", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.06f, Size1 = 0.16f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 200 }, root, "ActRocks");
                    Vol2Fs.MeshObject(root, "ActSkid", kit.InkSolid("fs_stroke2", InkC, _acc * 1.4f, 0.16f, 0.45f, 0.3f, 1.3f), out _stripMesh);
                    break;
                case Body.Water:
                    _a = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 5.5f, Size0 = 0.12f, Size1 = 0.3f, Gravity = 1.6f, Drag = 0.6f, Max = 400 }, root, "ActSplash");
                    _b = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB1.M("fs_dot", new Color(0.55f, 0.85f, 1f), 2.4f, 0.7f)), Life0 = 0.4f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 500 }, root, "ActDrip");
                    var wet = Vol2FsB1.M("fs_wet", new Color(0.1f, 0.16f, 0.2f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.3f; wet.Soft = 0.2f;
                    Vol2Fs.MeshObject(root, "ActWet", kit.Mat(wet), out _stripMesh);
                    break;
                default:
                    _a = Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(Vol2FsB1.M("fs_dot", new Color(1f, 0.9f, 0.65f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.5f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 600 }, root, "ActSparks");
                    var x = Vol2FsB1.M("fs_streak", new Color(1f, 0.82f, 0.5f), 2.4f, 0.85f);      // r2: was 4 (white crosses) x.NoiseAmt = 0.15f; x.Soft = 0.02f; x.Erode = 0.1f;
                    Vol2Fs.MeshObject(root, "ActX", kit.Mat(x), out _stripMesh);
                    break;
            }
            foreach (var ps in new[] { _a, _b, _c }) if (ps != null) ps.Play(false);
        }

        /// <summary>The action lands on a body (t = the look clock; dir = the push / cut direction on the ground).</summary>
        public void Begin(Transform t, Bounds b, Vector3 feet, float at, Vector3 dir)
        {
            var o = new One { T = t, B0 = b, Feet0 = feet, At = at, Index = _ones.Count, Dir = dir.sqrMagnitude > 1e-4f ? new Vector3(dir.x, 0f, dir.z).normalized : Vector3.forward };
            _ones.Add(o);
            Vector3 c = b.center;
            switch (_body)
            {
                case Body.Wood:
                    {
                        int seed = _seed + o.Index * 17;
                        for (int k = 0; k < 4; k++)
                        {
                            var ctrl = Vol2FsB4F.Coil(b, feet.y, 9, k * 1.57f + Vol2Fs.Hash(seed, k), 1.4f + 0.5f * Vol2Fs.Hash(seed, k + 9), 0.8f, 0.1f);
                            var tb = _tubes.Add(ctrl, 0.09f, 0.035f, 36); tb.Knots = 0.3f; tb.Sides = 6; o.Coils.Add(tb);
                        }
                        o.Leaf0 = _leaves.Items.Count;
                        for (int k = 0; k < 14; k++)
                        {
                            var tb = o.Coils[k % o.Coils.Count]; Vector3 p = Vol2FsTubes.CatmullRom(tb.Ctrl, 0.2f + 0.75f * Vol2Fs.Hash(seed, k + 70));
                            _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = Flat(p - c) + Vector3.up * 0.3f, Up = Random.onUnitSphere + Vector3.up, Size = 0.16f + 0.08f * Vol2Fs.Hash(seed, k + 90), Grow = 0f, Cell = k % 4 });
                        }
                        o.Leaf1 = _leaves.Items.Count;
                        Vol2FsB1.Emit(_a, c, 40); Vol2FsB1.Emit(_b, c, 60);
                        break;
                    }
                case Body.Fire: Vol2FsB1.Emit(_b, c, 80); break;
                case Body.Earth:
                    for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_a, feet + Random.insideUnitSphere * 0.4f + Vector3.up * 0.2f, (Flat(Random.insideUnitSphere) * 1.5f + o.Dir * 1.5f + Vector3.up * 0.5f), -1f, -1f);
                    for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_b, c + Random.insideUnitSphere * 0.3f, o.Dir * Random.Range(2f, 5f) + Vector3.up * Random.Range(1f, 3.5f) + Random.insideUnitSphere, -1f, -1f);
                    break;
                case Body.Water:
                    for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_a, c + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.8f).normalized * Random.Range(2f, 5.5f), -1f, -1f);
                    break;
                default:
                    for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_a, c, (Random.insideUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(5f, 13f), -1f, -1f);
                    break;
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        Bounds Now(One o) => o.T != null ? Vol2FsRunBounds.Of(o.T, o.T.position, Mathf.Max(o.B0.extents.x, o.B0.extents.z)) : o.B0;
        Vector3 FeetNow(One o) => o.T != null ? o.T.position : o.Feet0;

        /// <summary>true while any action still shows.</summary>
        public bool Busy(float t)
        {
            foreach (var o in _ones) if (t - o.At < Span) return true;
            return false;
        }
        float Span => _body == Body.Wood ? BindSeconds + 1.2f : _body == Body.Fire ? BurnSeconds + 1.2f : _body == Body.Water ? WetSeconds + 0.8f : 1.2f;

        public void Tick(float t, Vector3 cam, float ground)
        {
            if (_ones.Count == 0) return;
            _strips.Clear();
            foreach (var o in _ones)
            {
                float a = t - o.At; if (a < 0f) continue;
                var b = Now(o); Vector3 c = b.center, feet = FeetNow(o);
                switch (_body)
                {
                    case Body.Wood:
                        {
                            float grow = Vol2Fs.Smooth(0f, 0.25f, a), loose = Vol2Fs.Smooth(BindSeconds, BindSeconds + 0.8f, a);
                            foreach (var tb in o.Coils) tb.Grow = grow * (1f - loose);
                            for (int i = o.Leaf0; i < o.Leaf1; i++) { var lf = _leaves.Items[i]; lf.Grow = Vol2Fs.Smooth(0.08f + 0.01f * (i - o.Leaf0), 0.35f + 0.01f * (i - o.Leaf0), a) * (1f - loose); _leaves.Items[i] = lf; }
                            if (a > BindSeconds && !o.RootsBuilt)
                            {
                                o.RootsBuilt = true; int seed = _seed + o.Index * 13;
                                for (int k = 0; k < 5; k++)
                                {
                                    float ang = (k / 4f - 0.5f) * 1.3f + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.3f;
                                    Vector3 d = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * o.Dir; float L = 1.1f * (0.7f + 0.5f * Vol2Fs.Hash(seed, k + 5));
                                    var pts = new Vector3[6];
                                    for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = new Vector3(feet.x, ground, feet.z) + d * L * u + Vector3.Cross(Vector3.up, d) * Mathf.Sin(u * 5f + k) * 0.12f + Vector3.up * (0.04f + 0.06f * (1f - u)); }
                                    var tb = _tubes.Add(pts, 0.045f, 0.008f, 18); tb.Sides = 5; tb.Knots = 0.4f; o.Roots.Add(tb);
                                }
                            }
                            float r = Vol2Fs.Smooth(BindSeconds, BindSeconds + 0.5f, a) * (1f - Vol2Fs.Smooth(BindSeconds + 0.8f, BindSeconds + 1.2f, a));
                            foreach (var tb in o.Roots) tb.Grow = r;
                            if (a < BindSeconds && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_b, c + Random.insideUnitSphere * 0.4f, 1);
                            break;
                        }
                    case Body.Fire:
                        if (a < BurnSeconds)
                        {
                            if (Time.frameCount % 2 == 0)
                                for (int k = 0; k < 2; k++) { Vector3 p = c + Vector3.Scale(Random.insideUnitSphere, b.extents) * 0.9f; Vol2FsB1.EmitV(_a, p, Vector3.up * Random.Range(0.6f, 1.6f), Random.Range(0.25f, 0.5f), -1f); }
                            if (Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_b, c + Random.insideUnitSphere * 0.5f, Vector3.up * 1.2f + Random.insideUnitSphere, -1f, -1f);
                        }
                        else if (a < BurnSeconds + 0.6f && Time.frameCount % 2 == 0) for (int k = 0; k < 3; k++) Vol2FsB1.EmitV(_c, c + Vector3.Scale(Random.insideUnitSphere, b.extents), Vector3.up * Random.Range(0.5f, 1.4f) + Random.insideUnitSphere * 0.3f, -1f, -1f);
                        break;
                    case Body.Earth:
                        {
                            // the skid: the feet dragged from where it stood to where the push left it (the body's own move)
                            float life = 1f - Vol2Fs.Smooth(0.6f, 1.2f, a);
                            Vector3 from = new Vector3(o.Feet0.x, ground, o.Feet0.z), to = new Vector3(feet.x, ground, feet.z);
                            if ((to - from).sqrMagnitude > 0.04f) { Vector3 side = Vector3.Cross(Vector3.up, Flat(to - from)) * 0.25f; Vol2FsB6.Crack(_strips, from - side, to - side, ground, 0.12f, 0f, 1f, life, 0.3f, _seed + o.Index, 0.05f); Vol2FsB6.Crack(_strips, from + side, to + side, ground, 0.12f, 0f, 1f, life, 0.7f, _seed + o.Index + 3, 0.05f); }
                            if (a < 0.35f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_a, feet + Random.insideUnitSphere * 0.3f, -o.Dir * 0.8f + Vector3.up * 0.4f, -1f, -1f);
                            break;
                        }
                    case Body.Water:
                        if (a < WetSeconds)
                        {
                            if (Time.frameCount % 2 == 0) for (int k = 0; k < 2; k++) Vol2FsB1.EmitV(_b, c + new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-0.1f, b.extents.y * 0.6f), Random.Range(-b.extents.z, b.extents.z)), Vector3.down * 0.3f, -1f, -1f);
                            float wl = Vol2Fs.Smooth(0f, 0.3f, a) * (1f - Vol2Fs.Smooth(WetSeconds - 0.6f, WetSeconds + 0.6f, a));
                            Vector3 f = Flat(cam - feet), r = Vector3.Cross(Vector3.up, f);
                            _strips.Quad(new Vector3(feet.x, ground + 0.025f, feet.z), r, f, Mathf.Max(b.size.x, b.size.z) * 1.5f, new Color(1f, 1f, 1f, wl), 0.4f + o.Index * 0.2f);
                        }
                        break;
                    default:
                        {
                            float xa = a; if (xa > 0.6f) break;
                            float life = 1f - Vol2Fs.Smooth(0.12f, 0.6f, xa), open = Vol2Fs.Smooth(0f, 0.05f, xa);
                            Vector3 at = c + Flat(cam - c) * (Mathf.Max(b.extents.x, b.extents.z) + 0.3f);
                            Vector3 view = (cam - at).normalized, right = Vector3.Cross(Vector3.up, view).normalized, up = Vector3.Cross(view, right);
                            for (int k = 0; k < 2; k++)
                            {
                                Vector3 d = (right * (k == 0 ? 1f : -1f) + up * 1.1f).normalized;
                                _strips.Strip(new[] { at - d * 0.95f * open, at, at + d * 0.95f * open }, new[] { 0.02f, 0.12f, 0.02f }, null, Color.white, new[] { life, life, life }, 0.3f + k * 0.4f, cam);
                            }
                            break;
                        }
                }
            }
            if (_tubeMesh != null) _tubes.Write(_tubeMesh);
            if (_leafMesh != null) _leaves.Write(_leafMesh);
            if (_stripMesh != null) _strips.Write(_stripMesh);
        }
    }

    /// <summary>Body bounds outside a run (the same reading as Vol2FsRun.BodyBounds).</summary>
    public static class Vol2FsRunBounds
    {
        public static Bounds Of(Transform t, Vector3 feet, float radius)
        {
            var b = new Bounds(feet + Vector3.up * 0.6f, new Vector3(radius * 2f, 1.2f, radius * 2f));
            if (t == null) return b;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (r == null || !r.enabled) continue;
                var rb = r.bounds;
                if (!(rb.size.y <= 3f && rb.size.x <= 4f) || float.IsNaN(rb.center.x + rb.center.y + rb.center.z) || float.IsInfinity(rb.center.x + rb.center.y + rb.center.z)) continue;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            Vector3 ext = b.extents;
            ext.y = Mathf.Clamp(ext.y, 0.3f, 0.8f); ext.x = Mathf.Clamp(ext.x, 0.3f, 1.0f); ext.z = Mathf.Clamp(ext.z, 0.3f, 1.0f);
            float bottom = Mathf.Max(feet.y, b.min.y);
            return new Bounds(new Vector3(b.center.x, bottom + ext.y, b.center.z), ext * 2f);
        }
    }
}
