using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>국 (wood, ㅜ ring, 남긴다 PEAK) - free showcase batch 3, concept guk_a (on the 2 m ring round the caster roots grow into a circle of thick living tree
    /// trunks, 2 - 3 m tall with flat leafy crowns, like the pillars of a round pavilion one could climb; jade light in the bark; leaves drifting). Unlike 구's crown
    /// of thorns (an instant snare) and 각's cage it is a ring of PILLARS that stays - the field's platform (the executor asks the field for it; this look is its
    /// picture). Its peak also stretches the life of MY other residues standing inside the ring x1.5 (the executor's Log.Extended): those get a jade thread from the
    /// nearest trunk. Beats, executor facts only: the launch = roots burst out of the band and twist up into trunks -> they stand for the residue material's life
    /// (crowns swaying, motes rising); a body on / crossing the band = vines lash it for the bind (a mirror) -> the last 0.4 s = the crowns shed leaves -> natural
    /// expiry (CSV 목 끝: 잔뿌리로 풀림, 앞으로 뻗으며) = the trunks sink back into the ground and unravel into rootlets reaching outward.</summary>
    public sealed class Vol2FsGuk : Vol2FsLeave
    {
        static readonly Color Jade = new Color(0.5f, 1f, 0.4f), Ink = new Color(0.03f, 0.035f, 0.025f);
        ParticleSystem _charge, _leafFx, _motes, _dirt; Light _chargeLight; Light[] _lights;
        readonly Vol2FsTubes _trunks = new Vol2FsTubes(), _roots = new Vol2FsTubes(), _wraps = new Vol2FsTubes(), _rootlets = new Vol2FsTubes();
        readonly Vol2FsLeaves _crowns = new Vol2FsLeaves();
        Mesh _trunkMesh, _rootMesh, _crownMesh, _wrapMesh, _rootletMesh, _threadMesh, _crackMesh; readonly Vol2FsStrips _threads = new Vol2FsStrips(), _crack = new Vol2FsStrips();
        sealed class Pillar { public float Ang, H, At; public int TFirst, TCount, LFirst, LCount; public Vector3 Base; }
        readonly List<Pillar> _pillars = new List<Pillar>();
        readonly List<(Struck S, int First, int Count)> _bound = new List<(Struck, int, int)>();
        readonly List<long> _extended = new List<long>();
        bool _built, _rootletsMade;
        protected override float ExpireSeconds => 1.5f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);
        Material BarkMat => Kit.Bark(new Color(0.85f, 0.72f, 0.55f), new Color(0.3f, 0.95f, 0.2f) * 0.8f);
        Material RootMat => Kit.Bark(new Color(0.7f, 0.58f, 0.44f), new Color(0.12f, 0.2f, 0.06f), 0.18f, "fs_bark", "fs_bark");      // r4: roots without the jade vein (r3: bright green chevrons by the camera)      // r2: lighter bark, a soft jade vein (r1: black trunks with a white line)

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3f, 1f)), Rate = 60f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.55f, World = false, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3f);
            _charge.transform.position = Tip(); _charge.Play(false);
            var leaf = M("fs_leaf", new Color(0.9f, 1f, 0.75f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFx = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.6f, Life1 = 2.8f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.12f, Size1 = 0.24f, Radius = 0.5f, Gravity = 0.2f, Drag = 1.6f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 240f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _motes = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.2f, 1f)), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.1f, Speed1 = 0.4f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.2f, VelOverLife = new Vector3(0f, 0.6f, 0f), Noise = 0.6f, Max = 300 }, root, "Motes");
            var dirt = M("fs_ink", new Color(0.32f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.06f, Size1 = 0.2f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.2f, ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 600 }, root, "Dirt");
            foreach (var ps in new[] { _leafFx, _motes, _dirt }) ps.Play(false);
            var tr = Vol2Fs.MeshObject(root, "Trunks", BarkMat, out _trunkMesh); tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "Roots", RootMat, out _rootMesh);
            Vol2Fs.MeshObject(root, "Crowns", Kit.Leaf(new Color(0.7f, 1f, 0.45f), new Color(0.3f, 0.8f, 0.12f)), out _crownMesh);
            Vol2Fs.MeshObject(root, "Wraps", BarkMat, out _wrapMesh);
            Vol2Fs.MeshObject(root, "Rootlets", BarkMat, out _rootletMesh);
            var th = M("fs_streak", Jade, 3.5f, 1f); th.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "LifeThreads", Kit.Mat(th), out _threadMesh);
            var cr = M("fs_ring", Jade * 0.8f, 1.1f, 0.6f);      // r4: dimmer (r3/r4: green puddles in front of the caster) cr.NoiseAmt = 0.55f; cr.NoiseScale = 2f; cr.Soft = 0.001f;
            Vol2Fs.MeshObject(root, "JadeCrack", Kit.Mat(cr), out _crackMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "RingLight" + k, Jade, 5f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            int seed = Seed + 211;
            const int N = 8;
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, seed, 0.3f), back = Back(ang);
                float front = Front(ang);
                if (front < 0.15f) continue;                                       // r2: none on the back / far sides (the camera sits inside the ring)
                var p = new Pillar { Ang = ang, H = Mathf.Lerp(2.1f, 2.9f, Vol2Fs.Hash(seed, k)) * Mathf.Lerp(0.4f, 1f, front), At = 0.06f + 0.05f * k, Base = OnBand(ang, R), TFirst = _trunks.Tubes.Count, LFirst = _crowns.Items.Count };
                Vector3 outw = Dir(ang), side = Vector3.Cross(Vector3.up, outw), b = p.Base;
                float h = p.H, r0 = Mathf.Lerp(0.22f, 0.3f, Vol2Fs.Hash(seed, k + 10));
                // a twisting trunk, flaring at the foot, splitting into a flat crown of short branches
                var trunk = _trunks.Add(new[] { b + Vector3.down * 0.2f, b + Vector3.up * h * 0.35f + side * 0.12f, b + Vector3.up * h * 0.7f - side * 0.1f, b + Vector3.up * h }, r0, r0 * 0.6f, 20); trunk.Knots = 0.6f; trunk.Sharp = 0.15f; trunk.Sides = 9;
                for (int j = 0; j < 5; j++)
                {
                    float a = (j / 5f + 0.1f * Vol2Fs.Hash(seed, k * 5 + j)) * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 s0 = b + Vector3.up * h * 0.92f, s1 = s0 + o * 0.6f + Vector3.up * 0.2f, s2 = s1 + o * 0.35f + Vector3.up * 0.05f;
                    var br = _trunks.Add(new[] { s0, s1, s2 }, 0.08f, 0.02f, 8); br.Sharp = 0.2f; br.Sides = 6;
                    for (int q = 0; q < 14; q++)
                    {
                        Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 31 + j * 9 + q) * 360f, Vector3.up) * Vector3.forward;
                        Vector3 lp = Vector3.Lerp(s0, s2, 0.2f + 0.06f * q) + Vector3.up * (0.04f + 0.25f * Vol2Fs.Hash(seed, k * 77 + j * 9 + q)) + nrm * 0.2f * Vol2Fs.Hash(seed, k * 91 + j * 9 + q);
                        Vector3 ln = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 53 + j * 9 + q) * 360f, Vector3.up) * Vector3.forward;
                        _crowns.Items.Add(new Vol2FsLeaves.Leaf { Pos = lp, Normal = (ln + Vector3.up * 0.35f).normalized, Up = (Vector3.up + nrm * 0.8f).normalized, Size = 0.34f, Grow = 0f, Cell = (j + q) % 4 });      // r3: leaves turned every way, smaller (r2: flat beige plates)
                    }
                }
                // buttress roots along the band
                for (int j = -1; j <= 1; j += 2)
                {
                    var pts = new Vector3[4];
                    for (int q = 0; q < 4; q++) { float u = q / 3f; pts[q] = OnBand(ang + j * 0.35f * u, R + 0.25f * Mathf.Sin(u * 3f)) + Vector3.up * (0.25f * (1f - u)); }
                    var rt = _roots.Add(pts, r0 * 0.55f, 0.03f, 10); rt.Knots = 0.5f; rt.Sharp = 0.4f; rt.Sides = 6;
                }
                p.TCount = _trunks.Tubes.Count - p.TFirst; p.LCount = _crowns.Items.Count - p.LFirst;
                _pillars.Add(p);
                Vol2FsB1.Emit(_dirt, b, 14);
            }
            for (int k = 0; k < 3; k++) _lights[k].transform.position = OnBand(-1.2f + 1.2f * k, R) + Vector3.up * 1.5f;
        }

        void Bind(Struck s)
        {
            int first = _wraps.Tubes.Count;
            Vector3 c = s.B.center; float rx = s.B.extents.x + 0.15f, rz = s.B.extents.z + 0.15f, hy = s.B.size.y;
            for (int j = 0; j < 3; j++)
            {
                var pts = new Vector3[10];
                for (int k = 0; k < 10; k++) { float u = k / 9f, a = j * 2.09f + u * 8f; pts[k] = new Vector3(c.x + Mathf.Cos(a) * rx, s.Feet.y + 0.05f + u * hy * 0.8f, c.z + Mathf.Sin(a) * rz); }
                var tb = _wraps.Add(pts, 0.07f, 0.02f, 30); tb.Knots = 0.4f; tb.Sharp = 0.3f; tb.Sides = 6;
            }
            _bound.Add((s, first, _wraps.Tubes.Count - first));
            Vol2FsB1.Emit(_leafFx, c, 24);
        }
        protected override void OnStruck(Struck s) { Bind(s); }
        protected override void OnTouch(Struck s) { if (!_bound.Exists(b => b.S.Id == s.Id && Mathf.Abs(b.S.At - s.At) < 0.1f)) Bind(s); }

        protected override void OnResidueEnd()
        {
            if (!NaturalEnd) { foreach (var p in _pillars) Vol2FsB1.Emit(_leafFx, p.Base + Vector3.up * p.H, 8); return; }
            _rootletsMade = true;
            foreach (var p in _pillars)
                for (int j = 0; j < 3; j++)
                {
                    Vector3 o = Dir(p.Ang + (j - 1) * 0.3f);
                    var pts = new Vector3[5];
                    for (int q = 0; q < 5; q++) { float u = q / 4f; pts[q] = p.Base + o * (0.2f + 2.2f * u) + Vector3.Cross(Vector3.up, o) * Mathf.Sin(u * 5f + j) * 0.2f; pts[q].y = Ground + 0.03f + 0.05f * (1f - u); }
                    var tb = _rootlets.Add(pts, 0.08f, 0.012f, 12); tb.Knots = 0.45f; tb.Sharp = 0.5f; tb.Sides = 5;
                }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float sink = NaturalEnd ? Vol2Fs.Smooth(0f, 0.85f, ex) : Vol2Fs.Smooth(0f, 0.4f, ex);
            foreach (var p in _pillars)
            {
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((h - p.At) / 0.5f)) * (1f - sink) * (1f - noRes);
                for (int k = 0; k < p.TCount; k++) _trunks.Tubes[p.TFirst + k].Grow = k == 0 ? g : g * Mathf.Clamp01((h - p.At - 0.3f) / 0.3f);
                for (int k = 0; k < p.LCount; k++)
                {
                    var l = _crowns.Items[p.LFirst + k];
                    l.Grow = Vol2Fs.Smooth(p.At + 0.45f, p.At + 0.8f, h) * (1f - sink) * (1f - noRes) * (1f - Mathf.Clamp01(warn * 0.8f - (k % 4) * 0.2f));
                    _crowns.Items[p.LFirst + k] = l;
                }
                if (!Exiting && h > p.At + 0.8f && Random.value < 0.01f + 0.06f * warn) Vol2FsB1.Emit(_leafFx, p.Base + Vector3.up * p.H, 1);
                if (!Exiting && h > p.At + 0.5f && Time.frameCount % 9 == 0) Vol2FsB1.Emit(_motes, p.Base + Vector3.up * 0.3f, 1);
            }
            foreach (var r in _roots.Tubes) r.Grow = Vol2Fs.Smooth(0f, 0.3f, h) * (1f - sink) * (1f - noRes);
            _trunks.Write(_trunkMesh); _roots.Write(_rootMesh); _crowns.Write(_crownMesh);
            foreach (var b in _bound) { float a = t - b.S.At, gw = Vol2Fs.Smooth(0f, 0.25f, a) * (1f - Vol2Fs.Smooth(1.5f, 1.9f, a)); for (int k = 0; k < b.Count; k++) _wraps.Tubes[b.First + k].Grow = gw; }
            _wraps.Write(_wrapMesh);
            if (_rootletsMade) { float rg = Vol2Fs.Smooth(0.1f, 0.7f, ex), rf = Vol2Fs.Smooth(0.8f, 1f, ex); foreach (var r in _rootlets.Tubes) r.Grow = rg * (1f - rf); _rootlets.Write(_rootletMesh); }
            // the peak: jade threads to MY other residues whose life this ring stretched (the executor's Log.Extended)
            _threads.Clear();
            var co = Inst.Model.Coordinator;
            if (co != null && !Exiting && h > 0.3f)
            {
                if (_extended.Count == 0 && Inst.Log.Extended.Count > 0)
                    foreach (var e in Inst.Log.Extended) { int c = e.IndexOf(':'); if (c > 0 && !e.EndsWith("refused") && long.TryParse(e.Substring(0, c), out long id)) _extended.Add(id); }
                foreach (long id in _extended)
                {
                    if (!co.Materials.TryGet(new MaterialId(id), out var m) || !m.Alive || m.Footprint == null) continue;
                    Vector3 c = m.Footprint.Centre; c.y = Ground + 0.4f;
                    Pillar best = null; float bd = 99f; foreach (var p in _pillars) { float d = Vector3.Distance(p.Base, c); if (d < bd) { bd = d; best = p; } }
                    if (best == null) continue;
                    Vector3 a = best.Base + Vector3.up * 1f; float l = 0.7f + 0.3f * Mathf.Sin(t * 6f + id);
                    _threads.Strip(new[] { a, (a + c) * 0.5f + Vector3.up * 0.4f, c }, new[] { 0.05f, 0.08f, 0.05f }, null, Color.white, new[] { l, l, l }, 0.4f, cam, 0f, 1f);
                }
            }
            _threads.Write(_threadMesh);
            _crack.Clear();
            float cl = Vol2Fs.Smooth(0f, 0.25f, h) * (1f - Vol2Fs.Smooth(0.5f, 1f, ex)) * (1f - noRes);
            if (cl > 0.01f) Vol2FsB2.GroundRing(_crack, Apex, F, Ground, Inner - 0.15f, Outer + 0.15f, 72, a => cl * Front(a), 0.33f, cam, 0.03f, 0f, 5f);
            _crack.Write(_crackMesh);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = 2.5f * Vol2Fs.Smooth(0f, 0.5f, h) * (1f - sink) * (1f - noRes) * (1f + pulse);
        }
    }
}
