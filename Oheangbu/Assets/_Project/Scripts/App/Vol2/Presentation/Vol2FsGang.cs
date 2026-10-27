using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>강 (wood, ㅏ path, 돌아온다) - free showcase batch 7, concept gang_a (a long jade-veined vine shoots from the brush tip low along the path, its head a
    /// coiled thorny knot; it reaches the far end and snaps BACK along a second line, the two lines closing into one loop; each bear wrapped by a loop of vine, a
    /// thinner second loop from the way back; the dark shot shredded into leaves). Executor facts only: the head = a spinning knot of thorny vine coils at the
    /// executor's head (out 36 m/s to where the path stopped, back home at the same speed), the vine body = the trail the head flew (the base's history) joined
    /// to the brush tip - a whip that whips out and is reeled in; leaves and jade motes shed along it; at the turn the vine cracks (leaf burst, hit-stop);
    /// the first touch binds (the shared body action, 2 s, rootlets after), the return touch throws one thin lash coil round the body for 0.6 s (x0.5, weaker);
    /// a shot taken = leaf shred. Home: the vine is reeled into the brush in a puff of leaves.</summary>
    public sealed class Vol2FsGang : Vol2FsBackPath
    {
        static readonly Color Jade = new Color(0.35f, 1f, 0.55f);
        Material _vine; Mesh _vineMesh, _leafMesh, _glowMesh;
        readonly Vol2FsTubes _tubes = new Vol2FsTubes(); readonly Vol2FsLeaves _leaves = new Vol2FsLeaves(); readonly Vol2FsStrips _glow = new Vol2FsStrips();
        ParticleSystem _leafPs, _mote;
        sealed class Lash { public Struck S; public float At; }
        readonly List<Lash> _lashes = new List<Lash>();
        protected override Color LoopGlow => new Color(0.45f, 1.5f, 0.7f);

        protected override void OnSetup()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.75f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.35f) * 4f, 0.3f, "fs_vine", "fs_vein");
            Vol2Fs.MeshObject(root, "Vine", _vine, out _vineMesh);
            Vol2Fs.MeshObject(root, "VineLeaves", Kit.Leaf(new Color(0.55f, 1f, 0.5f), new Color(0.4f, 1f, 0.45f) * 0.6f), out _leafMesh);
            var g = M("fs_streak", Jade * 1.6f, 2.6f, 1f); g.NoiseAmt = 0.3f; g.Soft = 0.04f; g.Erode = 0.15f; g.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "VineGlow", Kit.Mat(g), out _glowMesh);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafPs = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.1f, Size1 = 0.24f, Gravity = 0.35f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 700 }, root, "Leaves");
            _mote = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.03f, Size1 = 0.07f, Drag = 1.2f, Noise = 1.2f, NoiseFreq = 2f, Max = 600 }, root, "Motes");
            _leafPs.Play(false); _mote.Play(false);
        }

        protected override void Run(float t, float along, float prev, bool back)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(along - prev) * 2f), 1, 12);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = TripPoint(Mathf.Lerp(prev, along, (k + 0.5f) / n), back);
                Vol2FsB1.EmitV(_leafPs, p + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 1.5f + Vector3.up * 0.8f, -1f, -1f);
                Vol2FsB1.EmitV(_mote, p + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 0.8f, -1f, -1f);
            }
        }

        protected override void OnTurn()
        {
            Vector3 p = HeadPos(Now(NowUs));
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_leafPs, p, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_mote, p, Random.onUnitSphere * Random.Range(1f, 3f), -1f, -1f);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            _lashes.Add(new Lash { S = s, At = Now(NowUs) });
            var c = CentreNow(s);
            for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere + Vector3.up * 0.3f).normalized * Random.Range(1.5f, 4f), -1f, -1f);
        }

        protected override void OnShot(Shot s)
        {
            for (int k = 0; k < 36; k++) Vol2FsB1.EmitV(_leafPs, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f);
        }

        protected override void OnHome()
        {
            Vector3 tip = Tip();
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_leafPs, tip, Random.onUnitSphere * Random.Range(1f, 3f), -1f, -1f);
        }

        protected override void DrawHead(float t, Vector3 pos, Vector3 dir, bool back, float life)
        {
            _tubes.Tubes.Clear(); _leaves.Items.Clear(); _glow.Clear();
            var cam = CamPos;
            if (life > 0.01f)
            {
                // the vine body: brush tip -> the trail it flew (last 0.5 s) -> the head; reeled in on the way back (the trail shortens to the head)
                var pts = new List<Vector3> { Tip() };
                var tr = TrailPoints(t, back ? 0.18f : 0.5f, 14);
                foreach (var p in tr) if ((p - pts[pts.Count - 1]).sqrMagnitude > 0.04f) pts.Add(p);
                if ((pos - pts[pts.Count - 1]).sqrMagnitude > 0.01f) pts.Add(pos);
                if (pts.Count >= 3)
                {
                    var body = _tubes.Add(pts.ToArray(), 0.11f, 0.07f, Mathf.Clamp(pts.Count * 4, 12, 60)); body.Grow = 1f; body.Sharp = 0.05f; body.Knots = 0.35f; body.Sides = 6;
                    var thin = new Vector3[pts.Count];
                    for (int k = 0; k < pts.Count; k++) thin[k] = pts[k] + Vector3.Cross(dir, Vector3.up) * 0.12f * Mathf.Sin(k * 1.7f + t * 9f) + Vector3.up * 0.1f * Mathf.Cos(k * 1.3f + t * 7f);
                    var b2 = _tubes.Add(thin, 0.035f, 0.02f, Mathf.Clamp(pts.Count * 4, 12, 60)); b2.Grow = 1f; b2.Sharp = 0.05f; b2.Sides = 5;
                    var gw = new float[pts.Count]; var ga = new float[pts.Count];
                    for (int k = 0; k < pts.Count; k++) { float u = k / (float)(pts.Count - 1); gw[k] = 0.22f * Mathf.Max(0.15f, u); ga[k] = life * (0.3f + 0.7f * u); }
                    _glow.Strip(pts, gw, null, Color.white, ga, 0.4f, cam);
                    for (int k = 1; k < pts.Count - 1; k += 2) _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = pts[k], Normal = Vector3.up + Random.insideUnitSphere * 0.3f, Up = Vector3.Cross(dir, Vector3.up) + Vector3.up * 0.5f, Size = 0.22f, Grow = life, Cell = k % 4 });
                }
                // the head: a coiled thorny knot spinning round the travel direction
                Vector3 a = Vector3.Cross(dir, Vector3.up).normalized, b = Vector3.Cross(dir, a).normalized;
                for (int c = 0; c < 4; c++)
                {
                    var coil = new Vector3[10];
                    for (int k = 0; k < 10; k++)
                    {
                        float u = k / 9f, ang = u * 5.5f + c * 1.57f + t * 14f, r = 0.55f * (1f - 0.55f * Mathf.Abs(u - 0.5f) * 2f);
                        coil[k] = pos - dir * (0.7f - 1.4f * u) + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r;
                    }
                    var ct = _tubes.Add(coil, 0.06f, 0.025f, 24); ct.Grow = life; ct.Knots = 0.4f; ct.Sides = 6; ct.Sharp = 0.15f;
                }
                for (int k = 0; k < 6; k++)
                {
                    float ang = k * 1.05f + t * 10f;
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = pos + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * 0.4f, Normal = a * Mathf.Cos(ang) + b * Mathf.Sin(ang), Up = dir, Size = 0.3f, Grow = life, Cell = k % 4 });
                }
            }
            // the return lashes: one thin coil round the body, 0.6 s
            foreach (var l in _lashes)
            {
                float a = t - l.At; if (a < 0f || a > 0.6f) continue;
                var bb = BodyNow(l.S);
                var ctrl = Vol2FsB4F.Coil(bb, Ground, 8, l.S.Index * 1.3f + 0.5f, 1.2f, 0.75f, 0.1f);
                var tb = _tubes.Add(ctrl, 0.04f, 0.02f, 30); tb.Grow = Vol2Fs.Smooth(0f, 0.12f, a) * (1f - Vol2Fs.Smooth(0.35f, 0.6f, a)); tb.Sides = 5;
            }
            _tubes.Write(_vineMesh); _leaves.Write(_leafMesh); _glow.Write(_glowMesh);
            if (life > 0.01f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_mote, pos + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere, -1f, -1f);
        }
    }
}
