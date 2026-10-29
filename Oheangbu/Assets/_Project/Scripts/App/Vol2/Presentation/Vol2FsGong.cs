using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>공 (wood, ㅗ fan, 돌아온다) - free showcase batch 7, concept gong_a (a wide fan-shaped net of vines and leaves is flung out over the area, slides another
    /// 5 m out and is hauled back toward the caster, snagging both bears twice). Executor facts only: the net = the executor's sector where it is now (the base's
    /// TripFraction: the apex slides FanTravel out in the out leg, home in the back leg) woven of jade-veined vines - radial strands and three cross arcs, leaves at the
    /// knots - flung up off the ground going out (its far edge billowing in the air) and dragging low coming back; two haul ropes run from the brush tip to the net's
    /// near corners and go taut on the way back; leaves and jade motes shed from it; the first touch binds (the shared action, 2 s, rootlets after), the return touch
    /// flashes the strands over the body and throws a burst of leaves (x0.5); a shot taken = shredded leaves. Home: the net balls up into the brush in a puff of leaves.</summary>
    public sealed class Vol2FsGong : Vol2FsBackFan
    {
        static readonly Color Jade = new Color(0.35f, 1f, 0.55f);
        Mesh _vineMesh, _leafMesh, _glowMesh;
        readonly Vol2FsTubes _tubes = new Vol2FsTubes(); readonly Vol2FsLeaves _leaves = new Vol2FsLeaves(); readonly Vol2FsStrips _glow = new Vol2FsStrips();
        ParticleSystem _leafPs, _mote;
        sealed class Snag { public Struck S; public float At; }
        readonly List<Snag> _snags = new List<Snag>();
        const int Radial = 7, Arcs = 4;
        const float Swing = 2.6f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Net", Kit.Bark(new Color(0.75f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.35f) * 4f, 0.3f, "fs_vine", "fs_vein"), out _vineMesh);
            Vol2Fs.MeshObject(root, "NetLeaves", Kit.Leaf(new Color(0.55f, 1f, 0.5f), new Color(0.4f, 1f, 0.45f) * 0.6f), out _leafMesh);
            var g = M("fs_streak", Jade * 1.5f, 2.4f, 1f); g.NoiseAmt = 0.3f; g.Soft = 0.04f; g.Erode = 0.15f; g.Scroll = new Vector2(-2.5f, 0f);
            Vol2Fs.MeshObject(root, "NetGlow", Kit.Mat(g), out _glowMesh);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafPs = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.16f, Size1 = 0.34f, Gravity = 0.35f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 800 }, root, "Leaves");
            _mote = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.03f, Size1 = 0.07f, Drag = 1.2f, Noise = 1.2f, NoiseFreq = 2f, Max = 600 }, root, "Motes");
            _leafPs.Play(false); _mote.Play(false);
        }

        /// <summary>A net point: u -1..1 across, v 0..1 out; lifted off the ground (billowing on the way out, low on the way back).</summary>
        Vector3 NetPoint(Vector3 apex, float u, float v, float t, bool back, float lift)
        {
            float h = back ? 0.3f + 0.4f * v : 0.4f + 2.0f * v * v;
            h *= lift;
            float wave = 0.25f * Mathf.Sin(u * 3f + v * 5f - t * 9f) * lift;
            // r2 (the main agent's r1 review: a narrow column from the shoulder): the net flares out WIDE beside the sector (up to ~2.6 m at its middle depth,
            // the outer strands most; the middle strands stay narrow and pass through the struck bodies)
            float wide = Swing * u * Mathf.Abs(u) * Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))) * lift;
            return FanPoint(apex, u, Mathf.Lerp(0.18f, 1f, v)) + Side * wide + Vector3.up * (h + wave);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            _snags.Add(new Snag { S = s, At = Now(NowUs) });
            var c = CentreNow(s);
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere - F * 0.6f + Vector3.up * 0.3f).normalized * Random.Range(1.5f, 4.5f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 36; k++) Vol2FsB1.EmitV(_leafPs, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f); }

        protected override void OnTurn()
        {
            Vector3 apex = ApexNow(Now(NowUs));
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_leafPs, FanPoint(apex, Random.Range(-1f, 1f), 1f) + Vector3.up * 1.2f, Random.onUnitSphere * Random.Range(1f, 3f), -1f, -1f);
        }

        protected override void OnHome()
        {
            Vector3 tip = Tip();
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_leafPs, tip, Random.onUnitSphere * Random.Range(1f, 3.5f), -1f, -1f);
        }

        protected override void DrawFan(float t, Vector3 apex, float k, bool back, float life)
        {
            var cam = CamPos;
            _tubes.Tubes.Clear(); _leaves.Items.Clear(); _glow.Clear();
            if (life > 0.01f && !IsHome)
            {
                float lift = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.15f, t);
                // radial strands
                for (int r = 0; r < Radial; r++)
                {
                    float u = -1f + 2f * r / (Radial - 1);
                    var pts = new Vector3[7];
                    for (int j = 0; j < 7; j++) pts[j] = NetPoint(apex, u, j / 6f, t, back, lift);
                    var tb = _tubes.Add(pts, 0.15f, 0.09f, 22); tb.Grow = life; tb.Sharp = 0.05f; tb.Knots = 0.45f; tb.Sides = 6;
                    // r3 (r2 review: a thin wireframe): a second thinner vine twisting round each strand, leaves all along it
                    var tw = new Vector3[7];
                    for (int j = 0; j < 7; j++) tw[j] = pts[j] + Vector3.Cross(Dir(u * Half), Vector3.up) * 0.14f * Mathf.Sin(j * 2.1f + t * 5f + r) + Vector3.up * 0.12f * Mathf.Cos(j * 1.7f + r);
                    var tb2 = _tubes.Add(tw, 0.06f, 0.035f, 22); tb2.Grow = life; tb2.Sharp = 0.05f; tb2.Knots = 0.4f; tb2.Sides = 5;
                    for (int j = 1; j < 7; j++)
                        for (int q = 0; q < 2; q++)
                        {
                            Vector3 lp = Vector3.Lerp(pts[j - 1], pts[j], 0.3f + 0.4f * q);
                            float la = (j * 2 + q) * 1.9f + r;
                            _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = lp, Normal = new Vector3(Mathf.Cos(la), 0.6f, Mathf.Sin(la)), Up = Dir(u * Half) + Vector3.up * 0.4f, Size = 0.34f, Grow = life, Cell = (j + q + r) % 4 });
                        }
                    var gw = new float[7]; var ga = new float[7];
                    for (int j = 0; j < 7; j++) { gw[j] = 0.32f; ga[j] = life * 0.55f; }
                    _glow.Strip(pts, gw, null, Color.white, ga, 0.3f + r * 0.1f, cam);
                }
                // cross arcs
                for (int a = 0; a < Arcs; a++)
                {
                    float v = (a + 1f) / Arcs;
                    var pts = new Vector3[11];
                    for (int j = 0; j < 11; j++) pts[j] = NetPoint(apex, -1f + 2f * j / 10f, v, t, back, lift);
                    var tb = _tubes.Add(pts, 0.11f, 0.11f, 26); tb.Grow = life; tb.Sharp = 0.05f; tb.Knots = 0.3f; tb.Sides = 5;
                    // leaves at the knots
                    for (int j = 0; j < Radial; j++)
                    {
                        float u = -1f + 2f * j / (Radial - 1);
                        Vector3 p = NetPoint(apex, u, v, t, back, lift);
                        _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = Vector3.up + F * 0.3f, Up = Dir(u * Half) + Vector3.up * 0.3f, Size = 0.5f, Grow = life, Cell = (a + j) % 4 });
                    }
                }
                // haul ropes: brush tip -> the net's near corners (taut on the way back)
                for (int s = 0; s < 2; s++)
                {
                    Vector3 corner = NetPoint(apex, s == 0 ? -1f : 1f, 0f, t, back, lift), tip = Tip();
                    float sag = back ? 0.05f : 0.35f;
                    var pts = new Vector3[6];
                    for (int j = 0; j < 6; j++) { float u = j / 5f; pts[j] = Vector3.Lerp(tip, corner, u) + Vector3.down * sag * 4f * u * (1f - u); }
                    var tb = _tubes.Add(pts, 0.06f, 0.045f, 14); tb.Grow = life; tb.Sharp = 0.02f; tb.Sides = 5;
                }
                // shed leaves / motes along the far edge as it travels
                if (Time.frameCount % 2 == 0)
                    for (int j = 0; j < 6; j++)
                    {
                        Vector3 p = NetPoint(apex, Random.Range(-1f, 1f), back ? Random.Range(0f, 0.4f) : Random.Range(0.6f, 1f), t, back, lift);
                        Vol2FsB1.EmitV(_leafPs, p, Random.insideUnitSphere * 1.5f + (back ? -F : F) * 1.5f, -1f, -1f);
                        Vol2FsB1.EmitV(_mote, p, Random.insideUnitSphere, -1f, -1f);
                    }
            }
            // the return snag: the strands over the body flash jade
            foreach (var sn in _snags)
            {
                float a = t - sn.At; if (a < 0f || a > 0.5f) continue;
                var bb = BodyNow(sn.S); float lf = 1f - Vol2Fs.Smooth(0.1f, 0.5f, a);
                Vol2FsB5.Halo(_glow, bb.center, FlatN(cam - bb.center), Mathf.Max(bb.extents.x, bb.extents.y) + 0.2f, 0.06f, lf * 0.7f, 0.5f, cam, 24, t * 3f);
            }
            _tubes.Write(_vineMesh); _leaves.Write(_leafMesh); _glow.Write(_glowMesh);
        }
    }
}
