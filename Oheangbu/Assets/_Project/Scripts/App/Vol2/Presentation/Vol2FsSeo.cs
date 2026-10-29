using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>서 (metal, ㅓ guard, 한 번) - free showcase batch 2, concept seo_a (a long polished steel crescent edge forms along the brush shaft like a halberd
    /// guard, a light line running along its edge; the paw strikes the steel: a brilliant white-gold flash and a great FOUNTAIN of sparks bursting SIDEWAYS,
    /// razor-thin light lines and fine ink threads, and (the CSV's end) the crescent snaps with a ringing flash, its shards flying out to the sides). Without a
    /// parry the crescent folds back into the shaft when the window closes.</summary>
    public sealed class Vol2FsSeo : Vol2FsAttach
    {
        static readonly Color White = new Color(1f, 0.95f, 0.82f), Gold = new Color(1f, 0.82f, 0.45f), Ink = new Color(0.03f, 0.03f, 0.035f);
        Transform _blade; ParticleSystem _sparks, _shards, _glints, _mist; Light _light;
        Mesh _edgeMesh, _linesMesh, _threadMesh; readonly Vol2FsStrips _edge = new Vol2FsStrips(), _lines = new Vol2FsStrips(), _threads = new Vol2FsStrips();
        struct Line { public Vector3 Dir; public float Len, Delay, Rnd; }
        readonly List<Line> _rays = new List<Line>(), _inkLines = new List<Line>();
        bool _snapped; float _snapAt = -1f;
        const float Z0 = 0.15f, Z1 = 1.75f, Bulge = 0.85f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        /// <summary>A crescent blade in local space: its spine along +Z (z0 .. z1, on the shaft), its edge bulging out along +X with hooked points; thin along Y.</summary>
        static Mesh Crescent()
        {
            const int N = 28;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            for (int k = 0; k <= N; k++)
            {
                float s = k / (float)N;
                float z = Mathf.Lerp(Z0 - 0.12f, Z1 + 0.18f, s);
                float bul = Bulge * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(s * Mathf.PI)), 0.85f);
                float spineX = 0.03f + 0.04f * Mathf.Sin(s * Mathf.PI);
                float hook = Mathf.Max(0f, Mathf.Abs(s - 0.5f) * 2f - 0.75f) * 0.6f;      // the points hook back toward the shaft
                Vector3 spine = new Vector3(spineX - hook * 0.2f, 0f, z), edge = new Vector3(Mathf.Max(spineX + 0.01f, bul) - hook * 0.1f, 0f, z + (s - 0.5f) * 0.12f);
                float th = 0.022f * Mathf.Sin(s * Mathf.PI) + 0.004f;
                v.Add(spine + Vector3.up * th); v.Add(spine - Vector3.up * th); v.Add(edge);
                uv.Add(new Vector2(s, 0f)); uv.Add(new Vector2(s, 0f)); uv.Add(new Vector2(s, 1f));
            }
            for (int k = 0; k < N; k++)
            {
                int a = k * 3, b = a + 3;
                idx.AddRange(new[] { a, b, a + 2, b, b + 2, a + 2 });           // upper bevel
                idx.AddRange(new[] { a + 1, a + 2, b + 1, b + 1, a + 2, b + 2 }); // lower bevel
                idx.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });             // spine
            }
            var fv = new List<Vector3>(); var fu = new List<Vector2>(); var fi = new List<int>();
            for (int k = 0; k < idx.Count; k++) { fv.Add(v[idx[k]]); fu.Add(uv[idx[k]]); fi.Add(k); }
            var m = new Mesh { name = "FsCrescent" };
            m.SetVertices(fv); m.SetUVs(0, fu); m.SetTriangles(fi, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        protected override void OnBegin()
        {
            var root = Root.transform;
            var steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.92f, 0.94f, 1f), 0.45f, 0.75f, new Color(0.78f, 0.82f, 0.9f) * 1.25f, "fs_steel");      // r3: r2 read brown (mirrored red ground)
            _blade = Vol2FsB1.Prop(root, "Crescent", Kit.SharedMesh("seo_crescent", Crescent), steel).transform;
            _blade.gameObject.SetActive(false);
            Vol2Fs.MeshObject(root, "EdgeLight", Kit.Mat(M("fs_streak", Gold, 5f, 1f)), out _edgeMesh);
            Vol2Fs.MeshObject(root, "LightLines", Kit.Mat(M("fs_streak", White, 6f, 1f)), out _linesMesh);
            var thr = M("fs_stroke", Ink, 1f, 0f); thr.Soft = 0.001f; thr.NoiseAmt = 0.4f; thr.Erode = 0.3f;
            Vol2Fs.MeshObject(root, "InkThreads", Kit.Mat(thr), out _threadMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 7f, 1f)), Life0 = 0.25f, Life1 = 0.8f, Speed0 = 5f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.05f,
                Gravity = 1.6f, Drag = 0.5f, Stretch = true, StretchVel = 0.035f, StretchLen = 1.5f, Max = 1400 }, root, "Sparks");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.12f, Size1 = 0.3f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.05f,
                SizeStart = 0.3f, SizePeak = 1.2f, SizePeakAt = 0.2f, SizeEnd = 0f, Max = 100 }, root, "Glints");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("seo_shard", () => Vol2FsB1.Shard(7)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 4f, Speed1 = 10f, Size0 = 0.06f, Size1 = 0.16f,
                Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Gravity = 1.8f, Drag = 0.3f, Spin = 900f, Max = 200 }, root, "Shards");
            var mist = M("fs_puff", new Color(0.85f, 0.85f, 0.8f, 1f), 1.2f, 0.3f); mist.Opacity = 0.35f; mist.Erode = 0.35f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.4f, Size1 = 0.8f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Drag = 2f, Tiles = 2, Max = 40 }, root, "Mist");
            foreach (var ps in new[] { _sparks, _glints, _shards, _mist }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "Flash", White, 7f);
        }

        Quaternion BladeRot()
        {
            Vector3 z = ShaftAxis, side = Vector3.Cross(Vector3.up, Away), x = side - Vector3.Dot(side, z) * z;     // r2: out to the right (r1 bulged toward the bear: end-on from the shoulder)
            if (x.sqrMagnitude < 1e-4f) x = Vector3.Cross(Vector3.up, z);
            x.Normalize();
            return Quaternion.LookRotation(z, Vector3.Cross(z, x));        // local +X = toward the strike, +Y = the blade's flat
        }

        protected override void OnGuard(float t, float open)
        {
            var cam = CamPos;
            bool show = open > 0.01f && !_snapped;
            _blade.gameObject.SetActive(show);
            float o = Mathf.Clamp01(Vol2Fs.EaseOutBack(open));
            var rot = BladeRot();
            _blade.SetPositionAndRotation(Grip(), rot);
            _blade.localScale = new Vector3(Mathf.Max(0.02f, o), 1f, Mathf.Lerp(0.6f, 1f, o));
            // the light running along the edge: a sweep when it forms, a steady glint after
            _edge.Clear();
            if (show)
            {
                float sweep = Mathf.Clamp01((t - WinStart) / 0.14f);
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
                for (int k = 0; k <= 14; k++)
                {
                    float s = k / 14f;
                    float z = Mathf.Lerp(Z0 - 0.12f, Z1 + 0.18f, s) * Mathf.Lerp(0.6f, 1f, o);
                    float bul = Bulge * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(s * Mathf.PI)), 0.85f) * o;
                    pts.Add(_blade.position + rot * new Vector3(bul + 0.01f, 0f, z));
                    float lead = Mathf.Exp(-Mathf.Pow((s - sweep) * 7f, 2f));
                    w.Add(0.05f + 0.16f * lead); al.Add(Mathf.Clamp01(0.45f + lead) * open);
                }
                _edge.Strip(pts, w, null, Color.white, al, 0.3f, cam, 0f, 1f);
                if (sweep < 1f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_glints, pts[Mathf.Clamp(Mathf.RoundToInt(sweep * 14f), 0, 14)], 1);
            }
            _edge.Write(_edgeMesh);
            // the snap (the CSV's end): after it caught the blow
            if (IsParried && !_snapped && t - ParryAt > 0.42f)
            {
                _snapped = true; _snapAt = t;
                Vector3 side = Vector3.Cross(Vector3.up, Away).normalized;
                for (int k = 0; k < 26; k++)
                {
                    float s = k / 25f; float z = Mathf.Lerp(Z0, Z1, s); float bul = Bulge * Mathf.Sin(s * Mathf.PI) * 0.6f;
                    Vector3 p = _blade.position + rot * new Vector3(bul, 0f, z);
                    Vol2FsB1.EmitV(_shards, p, (side * (k % 2 == 0 ? 1f : -1f) * Random.Range(4f, 9f) + Vector3.up * Random.Range(0.5f, 3f)));
                }
                Vol2FsB1.Emit(_glints, _blade.position + rot * new Vector3(0.3f, 0f, 0.8f), 6);
                for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_sparks, _blade.position + rot * new Vector3(0.25f, 0f, Random.Range(Z0, Z1)), (side * (k % 2 == 0 ? 1f : -1f) * Random.Range(4f, 10f) + Vector3.up * Random.Range(-1f, 3f)));
            }
            float flash = IsParried ? 22f * Mathf.Exp(-(t - ParryAt) * 9f) : 0f;
            if (_snapAt > 0f) flash += 10f * Mathf.Exp(-(t - _snapAt) * 12f);
            _light.intensity = flash + (show ? 1.2f * open : 0f);
            _light.transform.position = IsParried ? ParryPoint - Away * 0.2f : Shaft(0.6f);
        }

        protected override void OnParry()
        {
            Vector3 side = Vector3.Cross(Vector3.up, Away).normalized;
            // the fountain of sparks: mostly to the sides, up and out
            for (int k = 0; k < 520; k++)
            {
                float sgn = k % 2 == 0 ? 1f : -1f;
                Vector3 d = (side * sgn * Random.Range(0.6f, 1.2f) + Vector3.up * Random.Range(-0.2f, 0.9f) + Away * Random.Range(-0.3f, 0.5f)).normalized;
                Vol2FsB1.EmitV(_sparks, ParryPoint, d * Random.Range(5f, 15f));
            }
            Vol2FsB1.Emit(_glints, ParryPoint, 10); Vol2FsB1.Emit(_mist, ParryPoint, 6);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_shards, ParryPoint, (side * (k % 2 == 0 ? 1f : -1f) + Vector3.up * Random.Range(0f, 0.8f)).normalized * Random.Range(4f, 10f));
            for (int k = 0; k < 14; k++)
            {
                float sgn = k % 2 == 0 ? 1f : -1f;
                _rays.Add(new Line { Dir = (side * sgn + Vector3.up * Random.Range(-0.45f, 0.75f) + Away * Random.Range(-0.2f, 0.3f)).normalized, Len = Random.Range(1.2f, 3.2f), Delay = Random.Range(0f, 0.04f), Rnd = Random.value });
            }
            // the X flare
            Vector3 v = (CamPos - ParryPoint).normalized, a = Vector3.Cross(v, Vector3.up).normalized, b = Vector3.Cross(a, v);
            _rays.Add(new Line { Dir = (a + b).normalized, Len = -1.6f, Rnd = 0.1f }); _rays.Add(new Line { Dir = (a - b).normalized, Len = -1.6f, Rnd = 0.2f });
            for (int k = 0; k < 9; k++) _inkLines.Add(new Line { Dir = (side * (k % 2 == 0 ? 1f : -1f) + Vector3.up * Random.Range(-0.1f, 0.6f)).normalized, Len = Random.Range(1.0f, 2.2f), Delay = Random.Range(0.02f, 0.08f), Rnd = Random.value });
        }

        protected override void OnReaction(float t, float h)
        {
            var cam = CamPos;
            _lines.Clear();
            foreach (var r in _rays)
            {
                float a = h - r.Delay; if (a <= 0f) continue;
                if (r.Len < 0f)
                {   // the X: two long crossed streaks through the point
                    float l = -r.Len * Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.06f)), life = 1f - Vol2Fs.Smooth(0.08f, 0.3f, a);
                    if (life > 0.01f) _lines.Strip(new[] { ParryPoint - r.Dir * l, ParryPoint, ParryPoint + r.Dir * l }, new[] { 0.05f, 0.22f, 0.05f }, null, Color.white, new[] { life, life, life }, r.Rnd, cam, 0f, 1f);
                    continue;
                }
                float grow = Mathf.Clamp01(a / 0.07f), fade = 1f - Vol2Fs.Smooth(0.06f, 0.28f, a);
                if (fade <= 0.01f) continue;
                Vector3 p0 = ParryPoint + r.Dir * r.Len * Vol2Fs.Smooth(0.05f, 0.3f, a) * 0.6f, p1 = ParryPoint + r.Dir * r.Len * grow;
                _lines.Strip(new[] { p0, Vector3.Lerp(p0, p1, 0.5f), p1 }, new[] { 0.015f, 0.04f, 0.01f }, null, Color.white, new[] { fade, fade, fade * 0.6f }, r.Rnd, cam, 0f, 1f);
            }
            _lines.Write(_linesMesh);
            _threads.Clear();
            foreach (var r in _inkLines)
            {
                float a = h - r.Delay; if (a <= 0f) continue;
                float grow = Vol2Fs.Smooth(0f, 0.18f, a), life = 1f - Vol2Fs.Smooth(0.35f, 0.9f, a);
                if (life <= 0.01f) continue;
                var pts = new Vector3[6]; var w = new float[6]; var al = new float[6];
                for (int k = 0; k < 6; k++) { float u = k / 5f; pts[k] = ParryPoint + r.Dir * r.Len * grow * u + Vector3.down * 0.35f * u * u * Vol2Fs.Smooth(0.1f, 0.6f, a); w[k] = 0.035f * (1f - 0.7f * u); al[k] = life; }
                _threads.Strip(pts, w, null, Color.white, al, r.Rnd, cam, 0f, 1f);
            }
            _threads.Write(_threadMesh);
        }
    }
}
