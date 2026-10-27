using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>금>목 가시 (꺾음: metal overcomes wood) - free showcase batch 8, concept mw_a (one flash of steel across the enemy's vines: they are chopped all at once into
    /// short, low, sharp-cut thorns, the cut pieces and leaves flying, sparks along the line; the bear standing in them is struck by the cut thorns). Facts only: the
    /// commit consumes the shape and the enemy's wood patch (no child); the product is ONE damage request (OnDamageTick k = 0) - the thorn strike lands with it,
    /// no pulse after. The stubs stand a moment and wither.</summary>
    public sealed class Vol2FsRxMetalWood : Vol2FsReact
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        readonly Vol2FsTubes _stubs = new Vol2FsTubes();
        Mesh _stubMesh, _slashMesh, _phantomMesh, _inkMesh; MeshRenderer _stubR;
        readonly Vol2FsStrips _slash = new Vol2FsStrips(), _phantom = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        ParticleSystem _sparks, _chunks, _leaves, _glint;
        Light _light; Vol2FsCutAct _act; bool _hidden, _stabbed;
        readonly List<Vector3> _tips = new List<Vector3>();
        Vector3 _a, _b;
        int _bodyStub0 = -1;

        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            var bark = Kit.Bark(new Color(0.58f, 0.5f, 0.52f), new Color(1.2f, 0.95f, 0.6f) * 0.6f, 0.2f, "fs_vine", "fs_vein");
            _stubR = Vol2Fs.MeshObject(root, "CutThorns", bark, out _stubMesh); _stubR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // ~34 short, low, sharp-cut stubs where the vines lay, leaning every way
            for (int k = 0; k < 48; k++)
            {
                float a = Vol2Fs.Hash(Seed, k) * Mathf.PI * 2f, r = Radius * Mathf.Sqrt(Mathf.Max(0f, Vol2Fs.Hash(Seed, k + 40))) * 1.7f;      // r2: x1.6
                Vector3 b0 = G(P) + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r;
                Vector3 lean = (Vector3.up + new Vector3(Vol2Fs.Hash(Seed, k + 80) - 0.5f, 0f, Vol2Fs.Hash(Seed, k + 120) - 0.5f) * 1.3f).normalized;
                float h = 0.3f + 0.45f * Vol2Fs.Hash(Seed, k + 160);
                var tb = _stubs.Add(new[] { b0 - Vector3.up * 0.04f, b0 + lean * h * 0.5f, b0 + lean * h }, 0.075f + 0.035f * Vol2Fs.Hash(Seed, k + 200), 0.005f, 4);
                tb.Sides = 5; tb.Sharp = 0.07f; tb.Grow = 0f;
                _tips.Add(b0 + lean * h);
            }
            // a few taller cut thorns right under each body (the thorns that strike it)
            _bodyStub0 = _stubs.Tubes.Count;
            foreach (var s in Bodies)
                for (int k = 0; k < 5; k++)
                {
                    float a = k * 1.25f + s.Index;
                    Vector3 b0 = G(s.Feet) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Max(0.2f, s.B.extents.x * 0.6f);
                    Vector3 up = (Vector3.up + (G(s.Feet) - b0).normalized * 0.35f).normalized;
                    var tb = _stubs.Add(new[] { b0 - Vector3.up * 0.05f, b0 + up * 0.55f, b0 + up * 1.1f }, 0.09f, 0.005f, 5); tb.Sides = 5; tb.Sharp = 0.1f; tb.Grow = 0f;
                }
            var sl = M("fs_streak", new Color(1f, 0.78f, 0.42f), 2.2f, 0.85f);      // r2: gold, not white sl.NoiseAmt = 0.15f; sl.Soft = 0.02f; sl.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Slash", Kit.Mat(sl), out _slashMesh);
            Vol2Fs.MeshObject(root, "Phantoms", Vol2FsB4A.PhantomMat(Kit, Gold, 1.4f), out _phantomMesh);
            Vol2Fs.MeshObject(root, "SpeedInk", Kit.InkSolid("fs_stroke2", Vol2FsRxBreakKit.Ink, Gold * 2.4f, 0.14f, 0.45f, 0.3f, 1.4f), out _inkMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 5f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 800 }, root, "Sparks");
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = bark, Mesh = Kit.SharedMesh("rxbreak_shard", () => Vol2FsB1.Shard(57)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 2.5f, Speed1 = 6.5f, Size0 = 0.2f, Size1 = 0.45f, Gravity = 2.4f, Drag = 0.3f, Spin = 700f, Max = 160 }, root, "Chunks");
            var leaf = M("fs_leaf", new Color(0.8f, 0.9f, 0.65f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.12f, Size1 = 0.24f, Gravity = 0.4f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 400f, AlphaHoldTo = 0.7f, Max = 200 }, root, "Leaves");
            _glint = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 4f, 1f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.06f, Size1 = 0.12f, SizeStart = 1.4f, SizePeak = 1f, SizeEnd = 0.2f, Max = 120 }, root, "CutGlints");
            foreach (var ps in new[] { _sparks, _chunks, _leaves, _glint }) ps.Play(false);
            // the one flash: a long low steel line across the patch, square to the shape's way
            _a = G(P) - Side * Radius * 4.4f + Vector3.up * 0.55f + Dir * 0.15f; _b = G(P) + Side * Radius * 4.4f + Vector3.up * 0.35f - Dir * 0.1f;
            for (int k = 0; k < 220; k++) { float u = Random.value; Vol2FsB1.EmitV(_sparks, Vector3.Lerp(_a, _b, u), (Dir * Random.Range(0.5f, 1.5f) + Random.onUnitSphere * 0.8f + Vector3.up * 0.4f).normalized * Random.Range(5f, 14f), -1f, -1f); }
            Vector3 c = G(P) + Vector3.up * 0.3f;
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_chunks, c + Random.insideUnitSphere * Radius * 0.6f, (Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * 2f + Vector3.up * 1.3f) * Random.Range(1.4f, 3f), -1f, -1f);
            Vol2FsB1.Emit(_leaves, c, 70);
            _light = Vol2Fs.PointLight(root, "Flash", Gold, 7f); _light.transform.position = c + Vector3.up * 0.4f; _light.intensity = 12f;
            _act = new Vol2FsCutAct(Kit, root, Body.Metal, Seed, AccA);
            HitStop(0f, 0.1f);
        }

        protected override void OnDamageTick(int k, float t)
        {
            if (k != 0) return;
            _stabbed = true;
            foreach (var s in Bodies)
            {
                if (s.T == null || !Vol2FsRxBreakKit.Now(s, out var b, out var feet)) continue;
                _act.Begin(s.T, b, feet, t, s.Dir);
            }
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            // the vines go in the flash; the cut thorns stand (0.04 - 0.18 s), then wither and sink
            if (!_hidden && t > 0.035f && Patch != null)
            {
                _hidden = true;
                foreach (var n in new[] { "Vines", "Thorns", "Leaves" }) { var p = Vol2FsRxBreakKit.Prop(Patch, n); if (p != null) p.gameObject.SetActive(false); }
                foreach (var tip in _tips) Vol2FsB1.EmitV(_glint, tip, Vector3.zero, -1f, -1f);
            }
            float g = Vol2Fs.EaseOutBack(Vol2Fs.Smooth(0.03f, 0.18f, t));
            float wither = Vol2Fs.Smooth(1.4f, 2.1f, t);
            for (int i = 0; i < _stubs.Tubes.Count; i++)
                _stubs.Tubes[i].Grow = i >= _bodyStub0 ? Vol2Fs.EaseOutBack(Vol2Fs.Smooth(0f, 0.1f, t)) * (_stabbed || t > 0.05f ? 1f : 0f) * (1f - 0.6f * wither) : g * (1f - 0.6f * wither);
            if (t < 0.3f || (wither > 0f && wither < 1f)) _stubs.Write(_stubMesh);
            if (_stubR != null) { _stubR.transform.localPosition = Vector3.down * 0.25f * wither; _stubR.enabled = wither < 0.99f; }
            if (Patch != null) Patch.Pose(1f, 0.2f * wither, 1f);
            // the slash: thin -> wide, a short hit-stop, then two lagging phantoms; long ink speed lines out to the sides
            _slash.Clear(); _phantom.Clear(); _ink.Clear();
            float reveal = Vol2Fs.Smooth(0f, 0.06f, t), life = 1f - Vol2Fs.Smooth(0.25f, 0.75f, t);
            Vol2FsB6.Slash(_slash, _a, _b, Dir * 0.25f, 0.55f, reveal, life, 0.2f, cam, 18);
            Vol2FsB6.Slash(_phantom, _a + Vector3.up * 0.12f, _b + Vector3.up * 0.1f, Dir * 0.35f, 0.42f, reveal, (1f - Vol2Fs.Smooth(0.3f, 1.2f, t)) * 0.45f, 0.6f, cam, 18);
            Vol2FsB6.Slash(_phantom, _a - Vector3.up * 0.1f, _b - Vector3.up * 0.12f, Dir * 0.15f, 0.5f, reveal, (1f - Vol2Fs.Smooth(0.4f, 1.4f, t)) * 0.25f, 0.8f, cam, 18);
            float inkLife = 1f - Vol2Fs.Smooth(0.35f, 1.1f, t);
            if (inkLife > 0.01f)
                for (int k = 0; k < 8; k++)
                {
                    float side = k < 4 ? -1f : 1f, u0 = 0.15f + 0.2f * (k % 4);
                    Vector3 from = G(P) + Side * side * Radius * (0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 500)) + Vector3.up * (0.15f + 0.5f * Vol2Fs.Hash(Seed, k + 520));
                    Vector3 to = from + Side * side * (3.4f + 2.2f * Vol2Fs.Hash(Seed, k + 540)) * Vol2Fs.Smooth(0f, 0.12f, t) - Dir * 0.4f + Vector3.up * (Vol2Fs.Hash(Seed, k + 560) - 0.4f) * 0.6f;
                    var pts = new[] { from, Vector3.Lerp(from, to, 0.5f) + Vector3.up * 0.05f, to };
                    var w = new[] { 0.26f * (1f - u0 * 0.5f), 0.18f, 0.03f }; var al = new[] { inkLife, inkLife, inkLife * 0.6f };
                    _ink.Strip(pts, w, null, Color.white, al, 0.1f * k, cam, 0f, 1f);
                }
            _slash.Write(_slashMesh); _phantom.Write(_phantomMesh); _ink.Write(_inkMesh);
            if (_light != null) _light.intensity = Mathf.MoveTowards(_light.intensity, 0f, Time.deltaTime * 30f);
            _act.Tick(t, cam, Ground);
        }
    }
}
