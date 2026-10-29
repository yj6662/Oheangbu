using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>엇 (water, ㅓ attachment, 가른다) - free showcase batch 6, concept eot_b (a long thin ribbon of high-pressure water lashes from the brush tip like a whip
    /// with a cutting edge; it loops through the air with black ink swirling in it, the sill is split with water sheeting through the cut, the bear's crystal
    /// armour is blasted off in spray). Executor facts only: the attachment's 6 s = the water whip on the brush tip (a clear blue-green ribbon ~2 m long, its
    /// bright edge film, an ink thread inside, rippling and dripping), every swing contact = the whip lashes OUT along the swing's arc (a long water ribbon
    /// riding the base's crescent, spray and drops flung off it) and across the struck body (a burst of spray), the strip book opens the seam, the body action
    /// soaks it 4 s (drips, the dark wet ground). The attachment's end = the whip loses its edge and pours down off the brush into a splash (CSV 끝: 아래로 흘러내림).</summary>
    public sealed class Vol2FsEot : Vol2FsCutWear
    {
        static readonly Color Teal = new Color(0.45f, 0.85f, 1f);
        Mesh _whipMesh, _inkMesh, _rimMesh; readonly Vol2FsStrips _whip = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _rim = new Vol2FsStrips();
        ParticleSystem _drops, _spray;
        float _endAt = -1f; Vector3 _pourFrom;
        readonly List<(Slash S, float At)> _lashes = new List<(Slash, float)>();
        protected override Color SlashTint => new Color(0.5f, 0.85f, 1f);
        protected override Color CutGlow => new Color(0.6f, 1.2f, 1.6f);
        protected override float SlashWidth => 0.3f;
        protected override Color BodyCutTint => new Color(0.28f, 0.82f, 0.95f);      // r3: teal

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Whip", Vol2FsB1.Water(Kit, 1.6f, 0.4f), out _whipMesh);
            var ink = M("fs_stroke2", InkC, 1f, 0f); ink.NoiseAmt = 0.35f; ink.Soft = 0.08f;
            Vol2Fs.MeshObject(root, "WhipInk", Kit.Mat(ink), out _inkMesh);
            var rim = M("fs_streak", new Color(0.55f, 0.92f, 1f), 3.2f, 1f);      // r2: a bright teal edge rim.NoiseAmt = 0.2f; rim.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "WhipEdge", Kit.Mat(rim), out _rimMesh);
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.6f, 0.9f, 1f), 2.6f, 0.7f)), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 700 }, root, "Drops");
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.35f, Life1 = 0.75f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.32f, Gravity = 1.6f, Drag = 0.6f, Max = 500 }, root, "Spray");
            foreach (var ps in new[] { _drops, _spray }) ps.Play(false);
        }

        protected override void OnLineEnd() { _endAt = Now(NowUs); _pourFrom = Tip(); }

        /// <summary>The whip hanging off the tip: out along the shaft, then curling down and back, rippling (u 0..1 of its length).</summary>
        Vector3 WhipPoint(float u, float len, float t)
        {
            Vector3 ax = ShaftAxis, side = ShaftSide;
            Vector3 p = Tip() + ax * len * 0.55f * u + Vector3.down * len * 0.45f * u * u + side * len * 0.25f * Mathf.Sin(u * 3.2f);
            p += side * 0.12f * Mathf.Sin(u * 10f - t * 9f) * u + Vector3.up * 0.08f * Mathf.Sin(u * 7f - t * 11f) * u;
            return p;
        }

        protected override void Blade(float t, float life, float warn)
        {
            _whip.Clear(); _ink.Clear(); _rim.Clear();
            var cam = CamPos;
            if (life > 0.01f && _endAt < 0f)
            {
                float len = 2.4f * Vol2Fs.Smooth(0f, 1f, life);
                int n = 22; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var wi = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1);
                    pts[k] = WhipPoint(u, len, t);
                    w[k] = 0.26f * (1f - 0.6f * u)      // r2: thick (r1: 0.16 m, a thread)
                         * (1f + 0.25f * Mathf.Sin(u * 12f - t * 14f)) * (1f + 0.4f * warn);
                    wi[k] = w[k] * 0.35f; al[k] = life * (1f - 0.4f * u);
                }
                _whip.Strip(pts, w, null, Color.white, al, 0.3f, cam, 0f, 2.5f);
                _ink.Strip(pts, wi, null, Color.white, al, 0.71f, cam, 0f, 3f);
                var rw = new float[n]; for (int k = 0; k < n; k++) rw[k] = w[k] * 0.38f;
                _rim.Strip(pts, rw, null, Color.white, al, 0.52f, cam, 0f, 2f);
                if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_drops, WhipPoint(Random.Range(0.3f, 1f), len, t), Vector3.down * 0.3f + Random.insideUnitSphere * 0.3f, -1f, -1f);
                if (Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_spray, WhipPoint(Random.Range(0.4f, 1f), len, t), Random.insideUnitSphere * 1.2f + Vector3.up * 0.4f, Random.Range(0.1f, 0.2f), -1f);      // r2: spray off the whip
            }
            else if (_endAt >= 0f)
            {
                // pour: the water runs off the brush down to the ground and splashes
                float a = t - _endAt;
                if (a < 0.6f)
                {
                    for (int k = 0; k < 3; k++) Vol2FsB1.EmitV(_drops, _pourFrom + Random.insideUnitSphere * 0.1f, Vector3.down * Random.Range(1f, 3f), 0.06f, 0.6f);
                    if (a < 0.05f) for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_spray, G(_pourFrom) + Vector3.up * 0.05f, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1.5f, 3.5f), -1f, -1f);
                }
            }
            // the lashes: a water ribbon riding each swing's arc, thick at its head
            foreach (var l in _lashes)
            {
                float a = t - l.At; if (a < 0f || a > 0.6f) continue;
                float reveal = Vol2Fs.Smooth(0f, 0.09f, a), lf = 1f - Vol2Fs.Smooth(0.2f, 0.6f, a);
                int n = 18; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float v = k / (float)(n - 1), u = v * reveal;
                    pts[k] = ArcPoint(l.S, u) + Vector3.up * 0.08f * Mathf.Sin(v * 9f - a * 30f);
                    w[k] = 0.5f * Mathf.Sin(Mathf.Pow(v, 1.3f) * Mathf.PI * 0.95f + 0.05f); al[k] = lf * (0.4f + 0.6f * v);
                }
                _whip.Strip(pts, w, null, Color.white, al, 0.37f, cam, 0f, 3f);
                var iw = new float[n]; for (int k = 0; k < n; k++) iw[k] = w[k] * 0.3f;
                _ink.Strip(pts, iw, null, Color.white, al, 0.81f, cam, 0f, 3f);
            }
            _whip.Write(_whipMesh); _ink.Write(_inkMesh); _rim.Write(_rimMesh);
        }

        protected override void OnSlash(Slash s, Struck by)
        {
            if (s.Arc)
            {
                _lashes.Add((s, s.At));
                for (int k = 0; k < 24; k++) { float u = k / 23f; Vector3 p = ArcPoint(s, u); Vol2FsB1.EmitV(_spray, p, ((ArcPoint(s, Mathf.Min(1f, u + 0.05f)) - p).normalized * 3f + Vector3.up * 1f + Random.insideUnitSphere), -1f, -1f); }
                return;
            }
            Vector3 c = (s.A + s.B) * 0.5f;
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_drops, c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 2f, -1f, -1f);
        }

        protected override void OnCleave(Cleave c)
        {
            // water sheets through the cut
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_spray, c.Centre + Vector3.up * Random.Range(0.1f, 0.6f), c.Side * (k % 2 == 0 ? 1f : -1f) * Random.Range(1f, 3f) + Vector3.up * Random.Range(0.5f, 2f), -1f, -1f);
        }

        protected override void Live(float t, float h) { }
    }
}
