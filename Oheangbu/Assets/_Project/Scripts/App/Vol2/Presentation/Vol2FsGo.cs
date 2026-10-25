using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>고 (wood, ㅗ fan, 한 번) - free showcase, concept go_b: at the launch a great black ink arc sweeps across the fan in front of the caster and a forest
    /// of sharp bamboo shoots bursts out of the ground along it (the fan's own apex / forward / depth / half angle from the vol.2 spec), every body the fan
    /// touched is snared by shoots and vines; then the shoots sink and crumble - nothing is left. The snare is held for the look's own seconds (the 2 s slow is
    /// the status sink's; the look does not read or decide it).</summary>
    public sealed class Vol2FsGo : Vol2FsRun
    {
        const float SweepSeconds = 0.3f, HoldSeconds = 0.75f, SinkSeconds = 0.4f, SnareSeconds = 2.0f;
        static readonly Color Jade = new Color(0.55f, 1f, 0.3f), Gold = new Color(1f, 0.85f, 0.4f), Ink = new Color(0.03f, 0.035f, 0.025f);

        sealed class Shoot { public Vol2FsTubes.Tube Tube; public float At, Height; public Vector3 Base; public bool Burst; }
        sealed class Snare { public long Id; public Transform T; public Bounds B; public float At; public Vol2FsTubes Tubes = new Vol2FsTubes(); public Mesh Mesh; public MeshRenderer R; public bool Shed; public Vector3 Feet; }

        readonly Vol2FsTubes _shoots = new Vol2FsTubes();
        readonly List<Shoot> _list = new List<Shoot>();
        Mesh _shootMesh; MeshRenderer _shootR;
        Mesh _arcMesh, _glowMesh, _fanMesh; readonly Vol2FsStrips _fan = new Vol2FsStrips(); readonly Vol2FsStrips _arc = new Vol2FsStrips(), _glow = new Vol2FsStrips();
        readonly Vol2FsLeaves _leaves = new Vol2FsLeaves(); Mesh _leafMesh;
        ParticleSystem _charge, _dirt, _dust, _leafFx, _sparks, _drops; Light _light, _chargeLight;
        readonly List<Snare> _snares = new List<Snare>();
        bool _launched, _shed; float _launchAt = -1f;
        Vector3 _apex, _fwd; float _depth, _half, _sweepSign;
        Material _bamboo, _vine;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsMat.Make(tex, tint, intensity, addMix);

        public override void Began(long nowUs)
        {
            var root = Root.transform;
            var spec = Inst.Log.Spec;
            _apex = spec.Origin; _fwd = spec.Forward; _depth = spec.FanDepth; _half = spec.FanHalfAngleRad;
            _sweepSign = 1f;     // the brush comes down from the caster's left to the right (the area swing)
            var mote = M("fs_dot", Jade, 3f, 1f);
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mote), Rate = 70f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.6f, World = false, Max = 100 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3f);
            _charge.transform.position = Tip();
            _charge.Play(false);
        }

        Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * _fwd;

        void Launch(long nowUs)
        {
            _launched = true; _launchAt = Now(nowUs);
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            var root = Root.transform;
            _bamboo = Kit.Bark(new Color(1f, 1f, 0.75f), new Color(0.75f, 0.72f, 0.3f), 0.45f, "fs_bamboo", "fs_bamboo");      // self-lit olive-gold (r3: back-lit shoots read black)
            _vine = Kit.Bark(new Color(0.42f, 0.38f, 0.28f), new Color(0.5f, 1.2f, 0.25f) * 0.8f);
            _shootR = Vol2Fs.MeshObject(root, "Shoots", _bamboo, out _shootMesh);
            _shootR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // the shoots: polar samples inside the fan (denser toward its far half), each erupting when the sweep passes its angle, leaning outward
            int seed = (int)(Inst.Cast.Value & 0x7fff);
            int n = 64;
            for (int k = 0; k < n; k++)
            {
                float u = (k + Vol2Fs.Hash(seed, k)) / n;                       // sweep order (angle)
                float ang = Mathf.Lerp(-_half * 1.05f, _half * 1.05f, u) * _sweepSign;
                float r = Mathf.Lerp(1.9f, _depth * 1.02f, Mathf.Sqrt(Vol2Fs.Hash(seed, k + 77)));
                Vector3 d = Dir(ang);
                Vector3 b = _apex + d * r; b.y = Ground - 0.15f;
                float h = Mathf.Lerp(1.5f, 2.9f, Vol2Fs.Hash(seed, k + 155)) * Mathf.Lerp(0.7f, 1.15f, r / _depth);
                Vector3 lean = (d * Mathf.Lerp(0.6f, 1.2f, Vol2Fs.Hash(seed, k + 233)) + Vector3.Cross(Vector3.up, d) * (Vol2Fs.Hash(seed, k + 311) - 0.5f) * 0.6f + Vector3.up * 1.6f).normalized;
                Vector3 bend = Vector3.Cross(lean, Vector3.Cross(Vector3.up, lean)).normalized * 0.12f * (Vol2Fs.Hash(seed, k + 401) - 0.5f);
                var ctrl = new[] { b, b + lean * h * 0.35f + bend, b + lean * h * 0.7f + bend * 2f, b + lean * h };
                float rad = Mathf.Lerp(0.06f, 0.11f, Vol2Fs.Hash(seed, k + 509));
                var tube = _shoots.Add(ctrl, rad, rad * 0.75f, 10);
                tube.Sharp = 0.45f; tube.NodeEvery = 0.32f; tube.Sides = 6;
                _list.Add(new Shoot { Tube = tube, At = SweepSeconds * u * 0.95f + 0.04f * Vol2Fs.Hash(seed, k + 600), Height = h, Base = b + Vector3.up * 0.15f });
            }
            // leaves on a third of the shoots
            _leafMesh = null;
            var leafR = Vol2Fs.MeshObject(root, "ShootLeaves", Kit.Leaf(new Color(0.75f, 0.85f, 0.55f), new Color(0.15f, 0.35f, 0.05f)), out _leafMesh);
            for (int k = 0; k < _list.Count; k += 2)
            {
                var s = _list[k];
                for (int j = 0; j < 2; j++)
                {
                    Vector3 p = s.Tube.Ctrl[2];
                    Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 7 + j) * 360f, Vector3.up) * Vector3.forward;
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = nrm, Up = (Vector3.up + nrm * 0.8f + Vector3.Cross(Vector3.up, nrm) * (j == 0 ? 1f : -1f)).normalized, Size = 0.22f, Grow = 0f, Cell = (k + j) % 4 });
                }
            }
            // the ink arc: one fat black stroke sweeping across the fan at mid depth, its rim glowing jade; and a thin gold streak along it
            Vol2Fs.MeshObject(root, "InkArc", Kit.InkSolid("fs_stroke2", Ink, new Color(1.1f, 2.0f, 0.45f, 1f), 0.14f, 0.5f, 0.28f, 1.6f), out _arcMesh);
            Vol2Fs.MeshObject(root, "GlowArc", Kit.Mat(M("fs_dot", Gold, 3.5f, 1f)), out _glowMesh);
            var fan = M("fs_stroke2", new Color(0.03f, 0.05f, 0.025f, 1f), 1f, 0f); fan.Soft = 0.001f; fan.NoiseAmt = 0.5f; fan.Erode = 0.3f; fan.Opacity = 0.8f; fan.Edge = new Color(0.5f, 1.3f, 0.25f, 1f); fan.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "FanInk", Kit.Mat(fan), out _fanMesh);
            // particles
            var dirt = M("fs_ink", new Color(0.32f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 2.5f, Speed1 = 5.5f, Size0 = 0.07f, Size1 = 0.22f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.2f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 600 }, root, "Dirt");
            var dust = M("fs_puff", new Color(0.45f, 0.33f, 0.26f, 1f), 1f, 0f); dust.Opacity = 0.45f; dust.NoiseAmt = 0.6f; dust.Erode = 0.35f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.4f, Speed1 = 1.2f, Size0 = 0.8f, Size1 = 1.6f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 300, SortFudge = 10 }, root, "Dust");
            var leaf = M("fs_leaf", new Color(0.9f, 1f, 0.75f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFx = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 2f, Speed1 = 4.5f, Size0 = 0.12f, Size1 = 0.25f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Gravity = 0.35f, Drag = 1.6f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 240f, AlphaHoldTo = 0.75f, Max = 300 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.025f, Size1 = 0.06f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f,
                Drag = 1.5f, Stretch = true, StretchVel = 0.04f, Noise = 1.5f, NoiseFreq = 2f, Max = 600 }, root, "Sparks");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.6f, 1.3f, 0.3f, 1f); drop.EdgeWidth = 0.15f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.3f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkDrops");
            foreach (var ps in new[] { _dirt, _dust, _leafFx, _sparks, _drops }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "SweepLight", new Color(0.75f, 1f, 0.45f), 7f);
            Note = "launch fan depth " + _depth.ToString("0.0") + " half " + (_half * Mathf.Rad2Deg).ToString("0") + " deg, shoots " + _list.Count;
        }

        void Emit(ParticleSystem ps, Vector3 at, int count)
        {
            var e = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true };
            ps.Emit(e, count);
        }

        public override void Contact(Vol2ContactRecord rec)
        {
            if (!rec.BudgetAccepted || _snares.Exists(s => s.Id == rec.TargetId)) return;
            var t = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (t == null) return;
            var sn = new Snare { Id = rec.TargetId, T = tr, Feet = t.Position, B = BodyBounds(tr, t.Position, t.Radius) };
            // the snare starts when the sweep passes the body's angle (presentation only; the contact already happened)
            Vector3 d = t.Position - _apex; d.y = 0f;
            float ang = Vector3.SignedAngle(_fwd, d, Vector3.up) * Mathf.Deg2Rad * _sweepSign;
            sn.At = SweepSeconds * Mathf.Clamp01((ang / Mathf.Max(0.01f, _half) + 1f) * 0.5f) + 0.05f;
            BuildSnare(sn);
            _snares.Add(sn);
        }

        void BuildSnare(Snare sn)
        {
            sn.R = Vol2Fs.MeshObject(Root.transform, "Snare", _vine ?? Kit.Bark(new Color(0.32f, 0.3f, 0.2f), new Color(0.5f, 1.2f, 0.25f) * 1.2f), out sn.Mesh);
            int seed = (int)(sn.Id * 31 + (Inst.Cast.Value & 0xfff));
            var c = sn.B.center; float rx = sn.B.extents.x * 1.05f, rz = sn.B.extents.z * 1.05f, hy = sn.B.size.y;
            float g = sn.Feet.y;
            // vines spiralling up the legs / body, and shoots crossing over the back
            for (int k = 0; k < 6; k++)
            {
                float a0 = k / 6f * Mathf.PI * 2f + Vol2Fs.Hash(seed, k);
                var pts = new Vector3[7];
                for (int j = 0; j < 7; j++)
                {
                    float u = j / 6f; float a = a0 + u * 2.4f * (k % 2 == 0 ? 1f : -1f);
                    float shrink = 1f - 0.25f * u;
                    pts[j] = new Vector3(c.x + Mathf.Cos(a) * rx * shrink, g + u * hy * 0.85f, c.z + Mathf.Sin(a) * rz * shrink);
                }
                pts[0].y = g - 0.1f;
                var tube = sn.Tubes.Add(pts, 0.05f, 0.025f, 18); tube.Knots = 0.4f; tube.Sharp = 0.3f;
            }
            for (int k = 0; k < 4; k++)
            {
                float a = k / 4f * Mathf.PI * 2f + 0.4f;
                Vector3 s0 = new Vector3(c.x + Mathf.Cos(a) * rx * 1.35f, g - 0.1f, c.z + Mathf.Sin(a) * rz * 1.35f);
                Vector3 s1 = new Vector3(c.x - Mathf.Cos(a) * rx * 0.3f, g + hy * 1.15f, c.z - Mathf.Sin(a) * rz * 0.3f);
                var tube = sn.Tubes.Add(new[] { s0, Vector3.Lerp(s0, s1, 0.5f) + Vector3.up * 0.25f, s1 }, 0.055f, 0.035f, 12); tube.Sharp = 0.4f; tube.NodeEvery = 0.3f; tube.Sides = 6;
            }
        }

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            float t = Now(nowUs);
            if (!_launched)
            {
                _charge.transform.position = Tip();
                _chargeLight.intensity = 3f * (LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f);
                if (t >= LaunchRel - 1e-4f) Launch(nowUs); else return;
            }
            float a = t - _launchAt;
            float sink = Vol2Fs.Smooth(SweepSeconds + HoldSeconds, SweepSeconds + HoldSeconds + SinkSeconds, a);
            // shoots
            foreach (var s in _list)
            {
                float g = Vol2Fs.EaseOutBack((a - s.At) / 0.14f);
                s.Tube.Grow = (a < s.At ? 0f : g) * (1f - sink);
                if (!s.Burst && a >= s.At)
                {
                    s.Burst = true;
                    Emit(_dirt, s.Base, 7); Emit(_dust, s.Base, 1); Emit(_sparks, s.Base + Vector3.up * 0.4f, 6);
                    if ((s.Tube.R0 * 1000) % 3 < 1) Emit(_leafFx, s.Base + Vector3.up * 0.5f, 2);
                }
            }
            if (sink > 0f && !_shed)
            {
                _shed = true;
                foreach (var s in _list) { Emit(_dust, s.Base, 1); Emit(_leafFx, s.Base + Vector3.up * 0.6f, 1); }
            }
            _shoots.Write(_shootMesh);
            for (int k = 0; k < _leaves.Items.Count; k++)
            {
                var l = _leaves.Items[k];
                var s = _list[Mathf.Min(_list.Count - 1, (k / 2) * 2)];
                l.Grow = Vol2Fs.Smooth(s.At + 0.08f, s.At + 0.25f, a) * (1f - sink);
                l.Pos = Vol2FsTubes.TipOf(s.Tube) - Vector3.up * 0.25f * s.Height;
                _leaves.Items[k] = l;
            }
            _leaves.Write(_leafMesh);
            // the arc: revealed with the sweep, eroded after
            _arc.Clear(); _glow.Clear();
            const int N = 28;
            float head = Mathf.Clamp01(a / SweepSeconds);
            float life = 1f - Vol2Fs.Smooth(SweepSeconds + 0.15f, SweepSeconds + 0.9f, a);
            if (life > 0.01f && head > 0.02f)
            {
                var pts = new List<Vector3>(N); var w = new List<float>(N); var al = new List<float>(N); var side = new List<Vector3>(N);
                var gp = new List<Vector3>(N); var gw = new List<float>(N); var ga = new List<float>(N);
                for (int k = 0; k < N; k++)
                {
                    float u = k / (N - 1f) * head;
                    float ang = Mathf.Lerp(-_half * 1.6f, _half * 1.6f, u) * _sweepSign;
                    float r = Mathf.Lerp(1.3f, _depth * 0.95f, Mathf.Sqrt(u));          // r6: a spiral out of the brush, like go_b
                    Vector3 d = Dir(ang);
                    Vector3 p = _apex + d * r; p.y = Ground + Mathf.Lerp(1.1f, 0.45f, u) + 0.2f * Mathf.Sin(u * 6f);
                    pts.Add(p);
                    float taper = Mathf.Sin(Mathf.Clamp01(u / Mathf.Max(0.05f, head)) * Mathf.PI);
                    w.Add(2.3f * (0.3f + 0.7f * taper));
                    al.Add(life * (0.65f + 0.35f * taper));
                    side.Add((d * 0.6f + Vector3.up * 0.8f).normalized);
                    gp.Add(p + Vector3.up * 0.05f); gw.Add(0.22f * taper); ga.Add(life * taper);
                }
                _arc.Strip(pts, w, side, Color.white, al, 0.31f, CamPos, 1f, 0f);      // the fat wet head leads the sweep
                _glow.Strip(gp, gw, null, Color.white, ga, 0.6f, CamPos, 0f, 1f);
                if (a < SweepSeconds + 0.02f)
                {
                    Vector3 hp = pts[pts.Count - 1];
                    Emit(_drops, hp, 2); Emit(_sparks, hp, 14); Emit(_dirt, new Vector3(hp.x, Ground, hp.z), 6);
                    _light.transform.position = hp + Vector3.up * 0.6f;
                }
            }
            _arc.Write(_arcMesh); _glow.Write(_glowMesh);
            // the fan itself on the ground: one giant brush sweep of dark ink laid over the whole fan, revealed with the sweep, then drying away
            _fan.Clear();
            float fanLife = 1f - Vol2Fs.Smooth(SweepSeconds + HoldSeconds, SweepSeconds + HoldSeconds + 0.9f, a);
            if (fanLife > 0.01f && head > 0.02f)
            {
                var fp = new List<Vector3>(); var fw = new List<float>(); var fa = new List<float>(); var fs = new List<Vector3>();
                for (int k = 0; k < 20; k++)
                {
                    float u = k / 19f * head;
                    float ang = Mathf.Lerp(-_half, _half, u) * _sweepSign;
                    Vector3 d = Dir(ang);
                    float rm = (1.0f + _depth) * 0.5f;
                    Vector3 p = _apex + d * rm; p.y = Ground + 0.03f;
                    fp.Add(p); fw.Add((_depth - 1.0f) * 1.02f); fa.Add(fanLife); fs.Add(d);
                }
                _fan.Strip(fp, fw, fs, Color.white, fa, 0.52f, CamPos, 1f, 0f);
            }
            _fan.Write(_fanMesh);
            _light.intensity = a < SweepSeconds ? 5f : Mathf.Max(0f, 5f * (1f - (a - SweepSeconds) / 0.8f));
            // snares
            foreach (var sn in _snares)
            {
                float sa = a - sn.At;
                float rel = Vol2Fs.Smooth(SnareSeconds - 0.4f, SnareSeconds, sa);
                float grow = sa <= 0f ? 0f : Mathf.Clamp01(Vol2Fs.EaseOutBack(sa / 0.3f)) * (1f - rel);
                foreach (var tb in sn.Tubes.Tubes) tb.Grow = grow;
                if (sn.T != null)
                {
                    Vector3 shift = sn.T.position - sn.Feet;
                    sn.R.transform.position = shift;
                }
                sn.Tubes.Write(sn.Mesh);
                if (sa > 0f && sa < 0.05f) { Emit(_leafFx, sn.B.center, 6); Emit(_drops, sn.B.center, 6); }
                if (rel > 0f && !sn.Shed) { sn.Shed = true; Emit(_leafFx, sn.B.center, 10); Emit(_dust, sn.Feet, 2); }
            }
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float a = Now(nowUs) - _launchAt;
            if (!_launched) return Now(nowUs) > LaunchRel + 3f;
            float end = Mathf.Max(SweepSeconds + HoldSeconds + SinkSeconds + 0.3f, _snares.Count > 0 ? SweepSeconds + SnareSeconds + 0.2f : 0f);
            return a > end && (!ParticlesAlive() || a > end + 2.5f);
        }
    }
}
