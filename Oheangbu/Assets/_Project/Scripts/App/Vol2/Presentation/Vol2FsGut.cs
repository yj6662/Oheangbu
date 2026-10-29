using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>굿 (wood, ㅜ ring, 가른다) - free showcase batch 6, concept gut_a (a taut ring of living razor thorn-vine stretched just above the ground circles the
    /// caster like barbed wire over a glowing jade gouge; the bear stepping over it is lashed, its crystal armour shredded and vines whipping round its legs; the
    /// stone wall on the ring is cut in two). Executor facts only: the line's launch = two twisted vine strands whip round the band from the front (both ways,
    /// the base's race), razor thorns and leaves sprout along them; the 10 s line stands (a faint jade pulse; the template's warning: the leaves wilt and fall);
    /// every Instant (the bear standing on the band at the launch, the walker crossing at 1.0 s) = the strands lash up at that angle (three whips, leaves, jade
    /// sparks) - the base's crossing slash, the book's seam / fuse and the shared bind do the rest; the emitter's EndUs = the strands slacken down into the gouge
    /// and loosen into rootlets reaching outward (CSV 끝: 잔뿌리로 풀림, 앞으로 뻗으며).</summary>
    public sealed class Vol2FsGut : Vol2FsCutRing
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.5f);
        const float Wrap = 11f, Twist = 0.075f;
        ParticleSystem _leaves, _sparks;
        Material _vine, _thorn;
        Mesh _strandMesh, _thornMesh, _leafMesh, _whipMesh, _rootMesh, _glowMesh; readonly Vol2FsStrips _glowS = new Vol2FsStrips();
        readonly Vol2FsTubes _strands = new Vol2FsTubes(), _thorns = new Vol2FsTubes(), _whips = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsLeaves _leafCards = new Vol2FsLeaves();
        readonly List<(Vol2FsTubes.Tube T, float Ang)> _thornAt = new List<(Vol2FsTubes.Tube, float)>();
        readonly List<(Vol2FsTubes.Tube T, float At)> _whipAt = new List<(Vol2FsTubes.Tube, float)>();
        readonly List<float> _leafAng = new List<float>();
        bool _built, _rootsBuilt;
        Light _glowL;
        protected override Color CutGlow => new Color(0.45f, 1.5f, 0.55f);
        protected override float EndSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein");
            _thorn = Kit.Metal(1.1f, 0f, new Color(0.05f, 0.25f, 0.1f), new Color(0.55f, 1f, 0.65f)) ?? _vine;
            Vol2Fs.MeshObject(root, "Strands", _vine, out _strandMesh);
            Vol2Fs.MeshObject(root, "Thorns", _thorn, out _thornMesh);
            Vol2Fs.MeshObject(root, "Whips", _vine, out _whipMesh);
            Vol2Fs.MeshObject(root, "Rootlets", _vine, out _rootMesh);
            var gl = M("fs_streak", Jade, 2.6f, 1f); gl.NoiseAmt = 0.3f; gl.Soft = 0.1f; gl.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "StrandGlow", Kit.Mat(gl), out _glowMesh);
            Vol2Fs.MeshObject(root, "RingLeaves", Kit.Leaf(new Color(0.55f, 1f, 0.5f), Jade * 0.6f), out _leafMesh);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.8f, Speed0 = 1.2f, Speed1 = 4f, Size0 = 0.1f, Size1 = 0.22f, Gravity = 0.4f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.3f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 6f, Size0 = 0.025f, Size1 = 0.06f, Drag = 1.3f, Gravity = 0.3f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 700 }, root, "Sparks");
            foreach (var ps in new[] { _leaves, _sparks }) ps.Play(false);
            _glowL = Vol2Fs.PointLight(root, "JadeGlow", Jade, 5f);
        }

        /// <summary>A point of strand <paramref name="s"/> (0 / 1, twisted round each other) at angle <paramref name="ang"/>: knee-low, lower on the arc behind the caster.</summary>
        Vector3 StrandAt(int s, float ang)
        {
            float ph = ang * Wrap + s * Mathf.PI, hgt = 0.3f * Mathf.Lerp(0.45f, 1f, Front(ang));
            return OnBand(ang, R + Twist * Mathf.Sin(ph)) + Vector3.up * (hgt + Twist * Mathf.Cos(ph));
        }

        void Build()
        {
            _built = true;
            for (int s = 0; s < 2; s++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var ctrl = new Vector3[37];
                    for (int k = 0; k < ctrl.Length; k++) ctrl[k] = StrandAt(s, sgn * Mathf.PI * k / (ctrl.Length - 1));
                    var tb = _strands.Add(ctrl, 0.07f, 0.07f, 140); tb.Sides = 6; tb.Knots = 0.25f; tb.Sharp = 0.15f;
                }
            // razor thorns along the strands: pointing out, up or along, a jade blade each
            for (int k = 0; k < 64; k++)
            {
                float ang = -Mathf.PI + (k + Vol2Fs.Hash(Seed, k)) / 64f * Mathf.PI * 2f;
                int s = k % 2;
                Vector3 p = StrandAt(s, ang);
                Vector3 d = (Dir(ang) * (Vol2Fs.Hash(Seed, k + 100) - 0.3f) + Vector3.up * (0.4f + 0.6f * Vol2Fs.Hash(Seed, k + 200)) + Tangent(ang) * (Vol2Fs.Hash(Seed, k + 300) - 0.5f) * 1.4f).normalized;
                float len = (0.16f + 0.2f * Vol2Fs.Hash(Seed, k + 400)) * Mathf.Lerp(0.5f, 1f, Front(ang));
                var tb = _thorns.Add(new[] { p, p + d * len * 0.5f, p + d * len }, 0.05f, 0.005f, 4); tb.Sharp = 0.08f; tb.Sides = 4;
                _thornAt.Add((tb, ang));
            }
            for (int k = 0; k < 40; k++)
            {
                float ang = -Mathf.PI + (k + Vol2Fs.Hash(Seed, k + 500)) / 40f * Mathf.PI * 2f;
                Vector3 p = StrandAt(k % 2, ang);
                _leafCards.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = Dir(ang) + Vector3.up * 0.4f, Up = Tangent(ang) * (k % 2 == 0 ? 1f : -1f) + Vector3.up * 0.6f, Size = (0.13f + 0.08f * Vol2Fs.Hash(Seed, k + 600)) * Mathf.Lerp(0.5f, 1f, Front(ang)), Grow = 0f, Cell = k % 4 });
                _leafAng.Add(ang);
            }
        }

        protected override void Cut(float t)
        {
            if (!_built) Build();
            for (int k = 0; k < 24; k++) { float a = -Mathf.PI + k / 24f * Mathf.PI * 2f; if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_sparks, StrandAt(0, a), Tangent(a) * (a > 0f ? 2f : -2f) + Vector3.up * 1.2f, -1f, -1f); }
            _glowL.transform.position = Apex + F * R + Vector3.up * 0.6f; _glowL.intensity = 4f;
        }

        protected override void Crossed(Struck s, float ang)
        {
            if (!_built) Build();
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            float t = Now(NowUs);
            for (int k = 0; k < 3; k++)
            {
                float a = ang + (k - 1) * 0.12f;
                Vector3 p = StrandAt(k % 2, a), mid = Vector3.Lerp(p, b.center, 0.55f) + Vector3.up * (0.7f + 0.3f * k), end = b.center + Tangent(ang) * (k - 1) * 0.35f + Vector3.up * (0.2f * k - 0.1f) + FlatN(b.center - Apex) * 0.3f;
                var tb = _whips.Add(new[] { p, mid, end, end + Vector3.down * 0.4f + FlatN(b.center - Apex) * 0.2f }, 0.07f, 0.012f, 22); tb.Sides = 5;
                _whipAt.Add((tb, t + 0.02f * k));
            }
            Vol2FsB1.Emit(_leaves, b.center, 50); Vol2FsB1.Emit(_sparks, b.center, 110);
            _glowL.transform.position = b.center; _glowL.intensity = 6f;
        }

        protected override void Ended()
        {
            if (_rootsBuilt) return;
            _rootsBuilt = true;
            for (int k = 0; k < 20; k++)
            {
                float ang = -Mathf.PI + (k + Vol2Fs.Hash(Seed, k + 700)) / 20f * Mathf.PI * 2f;
                if (Front(ang) < 0.25f) continue;
                Vector3 d = Quaternion.AngleAxis((Vol2Fs.Hash(Seed, k + 710) - 0.5f) * 50f, Vector3.up) * Dir(ang);
                float len = 0.7f + 0.6f * Vol2Fs.Hash(Seed, k + 720);
                Vector3 a = OnBand(ang, R);
                var pts = new Vector3[6];
                for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = a + d * len * u + Vector3.Cross(Vector3.up, d) * Mathf.Sin(u * 5f + k) * 0.1f + Vector3.up * (0.03f + 0.05f * (1f - u)); }
                var tb = _roots.Add(pts, 0.04f, 0.007f, 16); tb.Sides = 5; tb.Knots = 0.4f;
            }
            for (int k = 0; k < 20; k++) { float a = -Mathf.PI + k / 20f * Mathf.PI * 2f; if (Front(a) > 0.3f) Vol2FsB1.EmitV(_leaves, StrandAt(0, a), Vector3.up * 0.6f + Dir(a) * 0.5f, -1f, -1f); }
        }

        protected override void Live(float t, float h)
        {
            if (!_built) Build();
            float stand = Standing(t), warn = LineWarn(t), end = EndK(t), race = Vol2Fs.Smooth(LaunchAt, LaunchAt + RaceSeconds, t);
            foreach (var tb in _strands.Tubes) tb.Grow = race * (1f - Vol2Fs.Smooth(0f, 0.7f, end));
            _strands.Write(_strandMesh);
            // r2: a jade glow running along the strands (r1: the vine ring read dark and thin)
            _glowS.Clear();
            if (stand > 0.01f)
                for (int s = 0; s < 2; s++)
                {
                    int n = 96; var p = new Vector3[n + 1]; var w = new float[n + 1]; var a = new float[n + 1];
                    for (int k = 0; k <= n; k++)
                    {
                        float ang = -Mathf.PI + k / (float)n * Mathf.PI * 2f;
                        p[k] = StrandAt(s, ang); w[k] = 0.16f * RingK(ang); a[k] = stand * Reached(ang, t) * (1f - Vol2Fs.Smooth(0f, 0.6f, end)) * Flick(t) * (0.55f + 0.45f * Mathf.Sin(ang * 5f - t * 4f + s));
                    }
                    _glowS.Strip(p, w, null, Color.white, a, 0.3f + 0.4f * s, CamPos, 0f, 8f);
                }
            _glowS.Write(_glowMesh);
            foreach (var th in _thornAt) th.T.Grow = Vol2Fs.Smooth(0.2f, 1f, Reached(th.Ang, t)) * (1f - Vol2Fs.Smooth(0f, 0.4f, end));
            _thorns.Write(_thornMesh);
            for (int i = 0; i < _leafCards.Items.Count; i++)
            {
                var lf = _leafCards.Items[i]; float since = SincePassed(_leafAng[i], t);
                lf.Grow = since < 0f ? 0f : Vol2Fs.Smooth(0.03f, 0.25f, since) * (1f - 0.5f * warn) * (1f - Vol2Fs.Smooth(0f, 0.3f, end));
                _leafCards.Items[i] = lf;
            }
            _leafCards.Write(_leafMesh);
            foreach (var w in _whipAt)
            {
                float a = t - w.At;
                w.T.Grow = a < 0f ? 0f : Vol2Fs.Smooth(0f, 0.1f, a) * (1f - Vol2Fs.Smooth(0.45f, 0.8f, a));
            }
            _whips.Write(_whipMesh);
            float rg = Vol2Fs.Smooth(0.1f, 0.6f, end) * (1f - Vol2Fs.Smooth(0.75f, 1f, end));
            foreach (var tb in _roots.Tubes) tb.Grow = rg;
            if (_rootsBuilt) _roots.Write(_rootMesh);
            // the standing line breathes a jade light; the warning: the leaves wilt and drop
            if (stand > 0.01f && Time.frameCount % 6 == 0) { float a = Random.Range(-1.9f, 1.9f); Vol2FsB1.EmitV(_sparks, StrandAt(0, a), Vector3.up * 0.4f + Random.insideUnitSphere * 0.2f, -1f, 0.6f); }
            if (warn > 0f && Time.frameCount % 2 == 0) { float a = Random.Range(-1.9f, 1.9f); Vol2FsB1.EmitV(_leaves, StrandAt(1, a), Vector3.down * 0.3f + Random.insideUnitSphere * 0.3f, -1f, -1f); }
            _glowL.intensity = Mathf.MoveTowards(_glowL.intensity, stand * (1.2f + 0.4f * Mathf.Sin(t * 3f)) * Flick(t), Time.deltaTime * 10f);
            if (_glowL.intensity < 2f) _glowL.transform.position = Apex + F * R + Vector3.up * 0.6f;
        }
    }
}
