using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>궁 (wood, ㅜ ring, 돌아온다) - free showcase batch 7, concept gung_b (a ring of vine petals opens outward round the caster like a flower blooming to 4 m and
    /// closes back; jade light, ink sweeps and leaves). Executor facts only: ten curved vine petals (bark tubes with leaves, a jade glow ribbon and an ink sweep under
    /// each) whose tips ride the band's radius (the report's legs: 2 m -> 4 m in the out leg, back to 2 m in the back leg) - they open low and wide going out and
    /// curl up like a closing bud coming back; front petals brighter (the camera stands inside); the first touch binds (the shared action, 2 s, rootlets after), the
    /// return touch = one thin lash coil round the body for 0.6 s (x0.5); a shot taken = leaf shred; the turn = a leaf burst off every petal tip. Home: the petals
    /// fold into the ground and shed their leaves (the bound bodies loosen into rootlets reaching forward on the shared action's clock).</summary>
    public sealed class Vol2FsGung : Vol2FsBackRing
    {
        static readonly Color Jade = new Color(0.35f, 1f, 0.55f);
        const int Petals = 10;
        Mesh _vineMesh, _leafMesh, _glowMesh, _inkMesh;
        readonly Vol2FsTubes _tubes = new Vol2FsTubes(); readonly Vol2FsLeaves _leaves = new Vol2FsLeaves();
        readonly Vol2FsStrips _glow = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        ParticleSystem _leafPs, _mote;
        sealed class Lash { public Struck S; public float At; }
        readonly List<Lash> _lashes = new List<Lash>();
        float _shed = -1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Petals", Kit.Bark(new Color(0.55f, 0.8f, 0.45f), new Color(0.2f, 0.9f, 0.35f) * 1.6f, 0.2f, "fs_vine", "fs_vein"), out _vineMesh);
            Vol2Fs.MeshObject(root, "PetalLeaves", Kit.Leaf(new Color(0.55f, 1f, 0.5f), new Color(0.4f, 1f, 0.45f) * 0.6f), out _leafMesh);
            var g = M("fs_streak", new Color(0.2f, 0.85f, 0.4f), 1.5f, 0.85f);      // r2: jade, not a white edge (r1 read as white blades) g.NoiseAmt = 0.3f; g.Soft = 0.04f; g.Erode = 0.15f; g.Scroll = new Vector2(-2.5f, 0f);
            Vol2Fs.MeshObject(root, "PetalGlow", Kit.Mat(g), out _glowMesh);
            Vol2Fs.MeshObject(root, "PetalInk", Kit.InkSolid("fs_stroke2", InkC, Jade * 1.3f, 0.16f, 0.45f, 0.3f, 1.4f), out _inkMesh);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafPs = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.1f, Size1 = 0.24f, Gravity = 0.35f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 800 }, root, "Leaves");
            _mote = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.03f, Size1 = 0.07f, Drag = 1.2f, Noise = 1.2f, NoiseFreq = 2f, Max = 600 }, root, "Motes");
            _leafPs.Play(false); _mote.Play(false);
        }

        float PetalAngle(int k, float t) => k * Vol2ReturnGeometry.Tau / Petals + 0.35f * Mathf.Max(0f, t - LaunchAt);

        protected override void OnTurn()
        {
            float t = Now(NowUs), r = RingNow(t);
            for (int k = 0; k < Petals; k++)
            {
                float a = PetalAngle(k, t) + 0.55f; if (Front(a) < 0.3f) continue;
                Vector3 p = RingPoint(a, r) + Vector3.up * 0.3f;
                for (int i = 0; i < 8; i++) Vol2FsB1.EmitV(_leafPs, p, (Dir(a) + Vector3.up * 0.8f + Random.insideUnitSphere * 0.5f).normalized * Random.Range(1.5f, 4f), -1f, -1f);
            }
        }

        protected override void OnStruck(Struck s) { var c = CentreNow(s); for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(2f, 6f), -1f, -1f); }

        protected override void OnReturnHit(Struck s)
        {
            _lashes.Add(new Lash { S = s, At = Now(NowUs) });
            var c = CentreNow(s); for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere + Vector3.up * 0.3f).normalized * Random.Range(1.5f, 4f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 36; k++) Vol2FsB1.EmitV(_leafPs, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f); }

        protected override void OnHome() { _shed = Now(NowUs); }

        protected override void DrawRing(float t, float r, float k, bool back, float life)
        {
            var cam = CamPos;
            _tubes.Tubes.Clear(); _leaves.Items.Clear(); _glow.Clear(); _ink.Clear();
            float close = back ? Vol2Fs.Smooth(TurnRel, HomeRel > TurnRel ? HomeRel : TurnRel + 0.5f, t) : 0f;
            float fold = _shed >= 0f ? Vol2Fs.Smooth(_shed, _shed + 0.5f, t) : 0f;
            float grow = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.12f, t) * (1f - fold);
            if (life > 0.01f && grow > 0.01f)
            {
                for (int p = 0; p < Petals; p++)
                {
                    float a = PetalAngle(p, t), fr = Front(a + 0.3f);
                    float h = (0.85f + 0.25f * Vol2Fs.Hash(Seed, p)) * (1f + 0.7f * close);
                    Vector3 p0 = RingPoint(a, Mathf.Max(0.6f, r * 0.42f)) + Vector3.up * 0.03f;
                    Vector3 p1 = RingPoint(a + 0.22f, r * (0.72f - 0.1f * close)) + Vector3.up * h;
                    Vector3 p2 = RingPoint(a + 0.48f, r * (0.98f - 0.12f * close)) + Vector3.up * (0.35f + 0.9f * close);
                    Vector3 p3 = RingPoint(a + 0.62f - 0.25f * close, r * (0.9f - 0.25f * close)) + Vector3.up * (0.15f + 1.2f * close);
                    var ctrl = new[] { p0, p1, p2, p3 };
                    var tb = _tubes.Add(ctrl, 0.085f, 0.03f, 22); tb.Grow = grow; tb.Knots = 0.35f; tb.Sides = 6; tb.Sharp = 0.25f;
                    var ctrl2 = new Vector3[4]; for (int i = 0; i < 4; i++) ctrl2[i] = ctrl[i] + Vector3.up * 0.12f * Mathf.Sin(i * 1.7f + t * 6f + p) + Dir(a) * 0.1f * Mathf.Cos(i * 1.3f + p);
                    var tb2 = _tubes.Add(ctrl2, 0.04f, 0.015f, 22); tb2.Grow = grow * 0.95f; tb2.Sides = 5;
                    var pts = new List<Vector3>(); var gw = new List<float>(); var ga = new List<float>(); var ip = new List<Vector3>(); var iw = new List<float>();
                    for (int i = 0; i <= 14; i++)
                    {
                        float u = i / 14f; Vector3 q = Vol2FsTubes.CatmullRom(ctrl, u * grow);
                        pts.Add(q); gw.Add(0.14f * Mathf.Max(0f, Mathf.Sin(u * Mathf.PI))); ga.Add(life * (0.2f + 0.5f * fr));
                        Vector3 gq = q; gq.y = Ground + 0.03f; ip.Add(gq); iw.Add(0.6f * Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)) + 0.06f);
                    }
                    _glow.Strip(pts, gw, null, Color.white, ga, 0.3f + p * 0.07f, cam);
                    _ink.Strip(ip, iw, null, Color.white, ga, 0.2f + p * 0.05f, cam);
                    for (int i = 1; i < 8; i++)
                    {
                        Vector3 q = Vol2FsTubes.CatmullRom(ctrl, Mathf.Min(grow, 0.12f * i + 0.05f));
                        float sw = (i % 2 == 0 ? 1f : -1f);
                        _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = q, Normal = Vector3.up + Dir(a + 1.57f * sw) * 0.6f, Up = Dir(a + 1.2f * sw) + Vector3.up * 0.5f, Size = 0.3f, Grow = grow, Cell = (p + i) % 4 });
                    }
                    if (fr > 0.5f && Time.frameCount % 5 == p % 5) Vol2FsB1.EmitV(_mote, p2 + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere, -1f, -1f);
                }
            }
            // the fold at home: leaves shed off the petals as they sink
            if (_shed >= 0f && t - _shed < 0.5f && Time.frameCount % 2 == 0)
                for (int p = 0; p < Petals; p++) { float a = PetalAngle(p, t) + 0.4f; if (Front(a) > 0.3f) Vol2FsB1.EmitV(_leafPs, RingPoint(a, r * 0.8f) + Vector3.up * 0.4f, Random.insideUnitSphere * 1.5f + Vector3.up, -1f, -1f); }
            foreach (var l in _lashes)
            {
                float a = t - l.At; if (a < 0f || a > 0.6f) continue;
                var ctrl = Vol2FsB4F.Coil(BodyNow(l.S), Ground, 8, l.S.Index * 1.3f + 0.5f, 1.2f, 0.75f, 0.1f);
                var tb = _tubes.Add(ctrl, 0.04f, 0.02f, 30); tb.Grow = Vol2Fs.Smooth(0f, 0.12f, a) * (1f - Vol2Fs.Smooth(0.35f, 0.6f, a)); tb.Sides = 5;
            }
            _tubes.Write(_vineMesh); _leaves.Write(_leafMesh); _glow.Write(_glowMesh); _ink.Write(_inkMesh);
        }
    }
}
