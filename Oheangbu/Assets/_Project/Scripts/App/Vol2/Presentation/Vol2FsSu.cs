using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>수 (metal, ㅜ ring, 한 번 · 벤다) - free showcase batch 2, concept su_a (a whirling ring of long steel blades spins round the caster at waist height on
    /// the 2 m ring like a circular saw of knives, each blade dragging a razor-thin silver streak and black ink, the whole ring a blur of light; the bear on the ring
    /// is slashed - a bright cut line and sparks; then (the CSV's end) the blades snap with a ringing flash, their shards flying out sideways). Under the whirl the
    /// KCISA saw-toothed ring (Pattern_79, its toothed band only) turns on the ground in pale gold light - the pattern is a saw blade, and so is the spell.</summary>
    public sealed class Vol2FsSu : Vol2FsRing
    {
        static readonly Color White = new Color(1f, 0.96f, 0.86f), Gold = new Color(1f, 0.83f, 0.48f), Ink = new Color(0.03f, 0.03f, 0.035f);
        sealed class B { public Transform T; public float Phase, Y, Len, Tilt; }
        readonly List<B> _blades = new List<B>();
        ParticleSystem _sparks, _shards, _glints; Light _light, _light2;
        Mesh _trailMesh, _inkMesh, _sawMesh, _cutMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _saw = new Vol2FsStrips(), _cut = new Vol2FsStrips();
        const float SnapAt = 0.62f, Turns = 1.7f;
        bool _snapped;
        protected override float LookSeconds => SnapAt + 1.2f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        float Spin(float a) => Mathf.PI * 2f * Turns * (1f - Mathf.Exp(-Mathf.Max(0f, a) * 3.2f));

        protected override void OnBegin()
        {
            var root = Root.transform;
            var steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.92f, 0.94f, 1f), 0.45f, 0.75f, new Color(0.8f, 0.84f, 0.92f) * 1.4f, "fs_steel");      // r3: r2 read pink-brown
            var mesh = Kit.SharedMesh("blade", Vol2FsB1.Blade);
            const int N = 26;
            for (int k = 0; k < N; k++)
            {
                var b = new B { T = Vol2FsB1.Prop(root, "Blade", mesh, steel, false).transform, Phase = Vol2FsB2.Angle(k, N, Seed, 0.5f), Y = Mathf.Lerp(0.55f, 1.35f, Vol2Fs.Hash(Seed, k + 30)), Len = Mathf.Lerp(1.4f, 2.3f, Vol2Fs.Hash(Seed, k + 40)), Tilt = (Vol2Fs.Hash(Seed, k + 50) - 0.5f) * 30f };
                b.T.gameObject.SetActive(false);
                _blades.Add(b);
            }
            Vol2Fs.MeshObject(root, "LightTrails", Kit.Mat(M("fs_streak", White, 7f, 1f)), out _trailMesh);
            var ink = M("fs_stroke2", Ink, 1f, 0f); ink.Soft = 0.001f; ink.NoiseAmt = 0.5f; ink.Erode = 0.3f; ink.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "InkTrails", Kit.Mat(ink), out _inkMesh);
            var saw = M("fs_kcisa79", Gold, 2.6f, 1f); saw.Soft = 0.001f; saw.NoiseAmt = 0.15f; saw.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "SawRing", Kit.Mat(saw), out _sawMesh);
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(M("fs_streak", White, 7f, 1f)), out _cutMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 7f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.05f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f,
                Gravity = 1.6f, Drag = 0.5f, Stretch = true, StretchVel = 0.035f, StretchLen = 1.5f, Max = 1600 }, root, "Sparks");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.1f, Life1 = 0.25f, Size0 = 0.15f, Size1 = 0.35f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.05f,
                SizeStart = 0.3f, SizePeak = 1.2f, SizePeakAt = 0.2f, SizeEnd = 0f, Max = 150 }, root, "Glints");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("su_shard", () => Vol2FsB1.Shard(13)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 4f, Speed1 = 10f, Size0 = 0.07f, Size1 = 0.18f,
                Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Gravity = 1.8f, Drag = 0.3f, Spin = 900f, Max = 300 }, root, "Shards");
            foreach (var ps in new[] { _sparks, _glints, _shards }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "Flash", White, 7f); _light2 = Vol2Fs.PointLight(root, "Flash2", White, 5f);
        }

        protected override void OnLaunch(long nowUs) { _light.transform.position = Apex + F * R + Vector3.up * 1f; _light2.transform.position = Apex + Vector3.up * 0.6f; }
        protected override void OnStruck(Struck s) { }

        Vector3 BladePos(B b, float ang, float r)
        {
            float back = Back(ang);
            var p = OnBand(ang, r); p.y += Mathf.Lerp(b.Y, 0.3f, back);
            return p;
        }

        protected override void OnRing(float t, float a, bool ended)
        {
            var cam = CamPos;
            float spin = Spin(a);
            float r = Mathf.Lerp(0.4f, R, Vol2Fs.Smooth(0f, ExpandSeconds, a));
            float show = _snapped ? 0f : Vol2Fs.Smooth(0f, 0.04f, a);
            _trail.Clear(); _ink.Clear();
            for (int k = 0; k < _blades.Count; k++)
            {
                var b = _blades[k];
                float ang = b.Phase + spin;
                Vector3 p = BladePos(b, ang, r), tan = Vector3.Cross(Dir(ang), Vector3.up) * -1f;   // the whirl turns clockwise seen from above
                b.T.gameObject.SetActive(show > 0.01f);
                // the blade lies along the ring, its point leading
                b.T.SetPositionAndRotation(p - tan * b.Len * 0.5f, Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(0f, 0f, 90f + b.Tilt));
                b.T.localScale = new Vector3(2.4f, 1.6f, b.Len) * show;
                // trails: an arc behind each blade (light), every third one inked
                if (show > 0.01f)
                {
                    float speed = Mathf.Exp(-a * 3.2f);
                    float arc = Mathf.Lerp(0.15f, 0.85f, speed);
                    var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                    for (int j = 0; j < 8; j++) { float u = j / 7f; pts[j] = BladePos(b, ang - arc * u, r) - Vector3.Cross(Dir(ang - arc * u), Vector3.up) * -0.0f; w[j] = 0.16f * (1f - u) + 0.02f; al[j] = show * (1f - u) * (0.4f + 0.6f * speed); }
                    _trail.Strip(pts, w, null, Color.white, al, k * 0.07f, cam, 0f, 1f);
                    if (k % 3 == 0)
                    {
                        for (int j = 0; j < 8; j++) { float u = j / 7f; pts[j] = BladePos(b, ang - arc * 1.2f * u, r * (1f + 0.05f * u)) + Vector3.down * 0.12f; w[j] = 0.22f * (1f - 0.7f * u); al[j] = show * (1f - u) * (0.5f + 0.5f * speed); }
                        _ink.Strip(pts, w, null, Color.white, al, k * 0.11f + 0.3f, cam, 0f, 1f);
                    }
                }
            }
            _trail.Write(_trailMesh); _ink.Write(_inkMesh);
            // the saw-toothed ring on the ground, turning with the whirl
            _saw.Clear();
            float sl = Vol2Fs.Smooth(0f, ExpandSeconds, a) * (1f - Vol2Fs.Smooth(SnapAt, SnapAt + 0.6f, a));
            if (sl > 0.01f)
            {
                Vector3 rr = Quaternion.AngleAxis(-spin * Mathf.Rad2Deg * 0.5f, Vector3.up) * Vector3.Cross(Vector3.up, F), ff = Vector3.Cross(rr, Vector3.up) * -1f;
                _saw.Quad(new Vector3(Apex.x, Ground + 0.03f, Apex.z), rr, ff, (R + 0.55f) * 2f * Mathf.Lerp(0.3f, 1f, Vol2Fs.Smooth(0f, ExpandSeconds, a)), new Color(1f, 1f, 1f, sl), 0.5f);
            }
            _saw.Write(_sawMesh);
            if (!_snapped && a < SnapAt && Time.frameCount % 2 == 0) { var b = _blades[Random.Range(0, _blades.Count)]; Vol2FsB1.Emit(_glints, BladePos(b, b.Phase + spin, r), 1); }
            // the snap (the CSV's end): every blade breaks with a ringing flash, shards out to the sides of their path
            if (!_snapped && a >= SnapAt)
            {
                _snapped = true;
                foreach (var b in _blades)
                {
                    float ang = b.Phase + spin; Vector3 p = BladePos(b, ang, r), tan = Vector3.Cross(Dir(ang), Vector3.up) * -1f;
                    for (int j = 0; j < 3; j++) Vol2FsB1.EmitV(_shards, p, (tan * Random.Range(2f, 5f) + Dir(ang) * Random.Range(2f, 6f) + Vector3.up * Random.Range(0.5f, 2.5f)) * (1f - 0.5f * Back(ang)));
                    for (int j = 0; j < 10; j++) Vol2FsB1.EmitV(_sparks, p, (tan + Dir(ang) * 0.8f + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 11f));
                    Vol2FsB1.Emit(_glints, p, 1);
                }
            }
            float flash = a < ExpandSeconds + 0.1f ? 7f * Vol2Fs.Smooth(0f, ExpandSeconds, a) : 2.5f * Mathf.Exp(-(a - ExpandSeconds) * 3f);
            if (_snapped) flash += 14f * Mathf.Exp(-(a - SnapAt) * 10f);
            _light.intensity = flash; _light2.intensity = flash * 0.6f;
            // the struck bodies: a bright cut across them, a spray of sparks
            _cut.Clear();
            foreach (var s in Hits)
            {
                float sa = a - s.At; if (sa < 0f) continue;
                var c = CentreNow(s);
                Vector3 tan = Vector3.Cross(Dir(s.Ang), Vector3.up) * -1f;
                if (!s.Started) { s.Started = true; for (int k = 0; k < 160; k++) Vol2FsB1.EmitV(_sparks, c, (tan * 0.9f + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(4f, 12f)); Vol2FsB1.Emit(_glints, c, 6); }
                float life = 1f - Vol2Fs.Smooth(0.08f, 0.45f, sa), grow = Mathf.Clamp01(sa / 0.05f);
                if (life <= 0.01f) continue;
                for (int k = 0; k < 2; k++)
                {
                    Vector3 d = (tan + Vector3.up * (k == 0 ? 0.35f : -0.25f)).normalized; float l = 1.3f * grow;
                    _cut.Strip(new[] { c - d * l, c, c + d * l }, new[] { 0.02f, 0.1f, 0.02f }, null, Color.white, new[] { life, life, life }, 0.2f * k, cam, 0f, 1f);
                }
            }
            _cut.Write(_cutMesh);
        }
    }
}
