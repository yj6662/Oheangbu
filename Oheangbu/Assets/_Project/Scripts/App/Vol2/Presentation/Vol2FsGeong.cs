using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Batch 7 ㅓ helper (the five orbit glyphs): a thick band laid along the executor's circle behind the orbiter - flat (its width across the radius, so it
    /// reads as a ring lying round the caster from the shoulder) or camera-facing; fades on the half-turn behind the caster (it passes 1.2 m in front of the camera).</summary>
    public static class Vol2FsOrbitBand
    {
        public static float FrontOf(float ang) => 1f - Vol2Fs.Smooth(100f, 165f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)));

        /// <summary>Points from the orbiter (psi) back <paramref name="span"/> radians at height <paramref name="h"/>; width tapers by <paramref name="taper"/> toward the tail.</summary>
        public static void Arc(Vol2FsStrips s, Vector3 centre, Vector3 fwd, float ground, float r, float psi, float span, float h, float width, float life, float rnd, Vector3 cam,
            bool flat, int n = 44, float wobble = 0f, float t = 0f, float taper = 0.65f, float backFade = 0.2f, float lead = 0f, float u0 = 0f, float u1 = 4f)
        {
            if (life <= 0.01f) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var sd = flat ? new Vector3[n] : null;
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1), a = Mathf.Max(0f, psi + lead - span * u);
                Vector3 p = Vol2ReturnGeometry.PointOnOrbit(centre, fwd, r, a);
                p.y = ground + h + wobble * Mathf.Sin(k * 0.8f + t * 9f);
                pts[k] = p;
                float head = Mathf.Clamp01((u * span) / 0.25f);      // a sharp leading point
                w[k] = width * Mathf.Max(0.05f, (1f - taper * u) * (0.3f + 0.7f * head));
                al[k] = life * Mathf.Max(0f, 1f - 0.6f * u) * Mathf.Lerp(backFade, 1f, FrontOf(a));
                if (flat) { Vector3 rad = p - centre; rad.y = 0f; sd[k] = rad.sqrMagnitude > 1e-6f ? rad.normalized : Vector3.right; }
            }
            s.Strip(pts, w, sd, Color.white, al, rnd, cam, u0, u1);
        }
    }

    /// <summary>겅 (wood, ㅓ attachment, 돌아온다) - free showcase batch 7, concept geong_b (a giant spinning leaf-blade circles the caster at chest height with a long
    /// trail of leaves forming a ring of green afterimages; tendrils snag the close bear; the incoming shots burst into leaves). Executor facts only: the
    /// orbiter = the lead of a long band of big overlapping jade leaf blades laid along the executor's circle (1.6 m, one turn a second, 6 s) over two thirds
    /// of a turn behind it, a thick vine running through them, a flat jade glow band and an ink band under them; the first pass = the shared bind (2 s, rootlets
    /// after) + a leaf burst; the second contact (a later turn) = one thin tendril lash round the body for 0.6 s; a shot taken = shredded into leaves. Home (the
    /// host's end): the blades come apart into falling leaves and rootlets reach forward from the caster's feet (CSV 끝: 잔뿌리로 풀림(앞으로 뻗으며)).
    /// r2 (r1 read as one small jade card): the band of blades + vine, 0.5 m wide.</summary>
    public sealed class Vol2FsGeong : Vol2FsBackOrbit
    {
        static readonly Color Jade = new Color(0.35f, 1f, 0.55f);
        Mesh _vineMesh, _leafMesh, _glowMesh, _inkMesh, _edgeMesh;
        readonly Vol2FsTubes _tubes = new Vol2FsTubes(); readonly Vol2FsLeaves _leaves = new Vol2FsLeaves(); readonly Vol2FsStrips _glow = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _edge = new Vol2FsStrips();
        ParticleSystem _leafPs, _mote;
        sealed class Lash { public Struck S; public float At; }
        readonly List<Lash> _lashes = new List<Lash>();
        readonly List<Vol2FsTubes.Tube> _roots = new List<Vol2FsTubes.Tube>();
        float _homeAt = -1f;
        const float Span = 4.2f;
        protected override Color TrailGlow => new Color(0.45f, 1.5f, 0.7f);

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Vine", Kit.Bark(new Color(0.75f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.35f) * 4f, 0.3f, "fs_vine", "fs_vein"), out _vineMesh);
            // r3 (r2: a flat pale-mint plastic ribbon): deep jade leaves and a dark jade band over ink; only the leading edge stays bright
            Vol2Fs.MeshObject(root, "BladeLeaves", Kit.Leaf(new Color(0.3f, 0.72f, 0.36f), new Color(0.2f, 0.7f, 0.3f) * 0.45f), out _leafMesh);
            var g = M("fs_ink", new Color(0.08f, 0.32f, 0.17f, 1f), 1.1f, 0.25f); g.Edge = Jade * 1.3f; g.EdgeWidth = 0.12f; g.NoiseAmt = 0.45f; g.Soft = 0.06f; g.Erode = 0.2f; g.Scroll = new Vector2(-2.5f, 0f);
            Vol2Fs.MeshObject(root, "BladeGlow", Kit.Mat(g), out _glowMesh);
            var e = M("fs_streak", Jade * 1.6f, 2.8f, 1f); e.NoiseAmt = 0.25f; e.Soft = 0.03f; e.Erode = 0.1f; e.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "BladeEdge", Kit.Mat(e), out _edgeMesh);
            Vol2Fs.MeshObject(root, "BladeInk", Kit.InkSolid("fs_stroke2", InkC, Jade * 1.3f, 0.16f, 0.5f, 0.33f, 1.4f), out _inkMesh);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafPs = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.8f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.14f, Size1 = 0.3f, Gravity = 0.3f, Drag = 1.6f, Noise = 1.1f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 900 }, root, "Leaves");
            _mote = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.3f, Speed1 = 1.4f, Size0 = 0.03f, Size1 = 0.07f, Drag = 1.2f, Noise = 1.2f, NoiseFreq = 2f, Max = 600 }, root, "Motes");
            _leafPs.Play(false); _mote.Play(false);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < (s.Kind == "Second" ? 20 : 45); k++) Vol2FsB1.EmitV(_leafPs, c, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(2f, 6f), -1f, -1f);
            if (s.Kind == "Second") _lashes.Add(new Lash { S = s, At = Now(NowUs) });
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_leafPs, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f); }

        protected override void OnHome()
        {
            float t = Now(NowUs); _homeAt = t;
            float psi = Psi(t);
            for (int k = 0; k < 70; k++) { float a = psi - Random.Range(0f, Span); if (Vol2FsOrbitBand.FrontOf(a) < 0.3f) continue; Vol2FsB1.EmitV(_leafPs, OrbitPoint(a, OrbitH), Random.insideUnitSphere * 1.5f + Vector3.down * 0.5f, -1f, -1f); }
            for (int k = 0; k < 6; k++)
            {
                float ang = (k / 5f - 0.5f) * 1.4f + (Vol2Fs.Hash(Seed, k) - 0.5f) * 0.3f;
                Vector3 d = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F; float L = 1.3f * (0.7f + 0.5f * Vol2Fs.Hash(Seed, k + 5));
                var pts = new Vector3[6];
                for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = G(Apex) + d * (0.3f + L * u) + Vector3.Cross(Vector3.up, d) * Mathf.Sin(u * 5f + k) * 0.12f + Vector3.up * (0.04f + 0.05f * (1f - u)); }
                var tb = _tubes.Add(pts, 0.045f, 0.008f, 18); tb.Sides = 5; tb.Knots = 0.4f; _roots.Add(tb);
            }
        }

        protected override void DrawOrbiter(float t, Vector3 pos, Vector3 tangent, float psi, float life)
        {
            var cam = CamPos;
            float band = FrontNow > 0.01f ? life / FrontNow : 0f;      // the band fades per point by where it is, not by where the orbiter is
            _tubes.Tubes.Clear(); foreach (var r in _roots) _tubes.Tubes.Add(r);
            _leaves.Items.Clear(); _glow.Clear(); _ink.Clear(); _edge.Clear();
            if (band > 0.01f)
            {
                Vol2FsOrbitBand.Arc(_ink, G(Apex), F, Ground, OrbitR, psi, Span, OrbitH - 0.2f, 0.45f, band * 0.95f, 0.27f, cam, false, 44, 0.04f, t);
                Vol2FsOrbitBand.Arc(_glow, G(Apex), F, Ground, OrbitR + 0.05f, psi, Span * 0.9f, OrbitH + 0.02f, 0.55f, band * 0.9f, 0.55f, cam, false, 44, 0.05f, t);
                Vol2FsOrbitBand.Arc(_edge, G(Apex), F, Ground, OrbitR - 0.05f, psi, Span * 0.6f, OrbitH + 0.28f, 0.06f, band, 0.81f, cam, false, 30, 0.06f, t);
                // the blades: big overlapping leaves along the band, the leading ones the biggest, tips forward along the travel
                int nb = 22;
                for (int k = 0; k < nb; k++)
                {
                    float u = k / (float)(nb - 1), a = psi - Span * u;
                    if (a < 0f) break;
                    float fr = Vol2FsOrbitBand.FrontOf(a); if (fr < 0.15f) continue;
                    Vector3 p = OrbitPoint(a, OrbitH + 0.06f * Mathf.Sin(k * 1.9f + t * 6f));
                    Vector3 tan = Tangent(a), outw = FlatN(p - G(Apex));
                    Vector3 up = (tan + outw * 0.25f * Mathf.Sin(k * 2.3f) + Vector3.up * 0.1f).normalized;
                    float sz = (1.0f - 0.55f * u) * (0.85f + 0.3f * Vol2Fs.Hash(Seed, k));
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p - up * sz * 0.5f + outw * 0.1f * Mathf.Sin(k * 1.3f), Normal = (Vector3.up * 0.75f + outw * 0.35f).normalized, Up = up, Size = sz, Grow = band * fr, Cell = k % 4 });
                }
                // the vine running through the blades
                int n = 16; var vp = new Vector3[n];
                for (int k = 0; k < n; k++) { float a = Mathf.Max(0f, psi - Span * 0.8f * k / (n - 1f)); vp[k] = OrbitPoint(a, OrbitH - 0.05f + 0.1f * Mathf.Sin(k * 1.4f + t * 6f)) + FlatN(OrbitPoint(a, 0f) - G(Apex)) * 0.08f * Mathf.Cos(k * 1.1f); }
                var vine = _tubes.Add(vp, 0.075f, 0.03f, 40); vine.Grow = band; vine.Sides = 6; vine.Knots = 0.35f; vine.Sharp = 0.05f;
                if (Random.value < FrontNow) Vol2FsB1.EmitV(_leafPs, pos - tangent * 0.4f + Random.insideUnitSphere * 0.3f, -tangent * Random.Range(0.3f, 1.5f) + Vector3.up * 0.3f + Random.insideUnitSphere * 0.4f, -1f, -1f);
                if (Time.frameCount % 2 == 0 && Random.value < FrontNow) Vol2FsB1.EmitV(_mote, pos + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere, -1f, -1f);
            }
            foreach (var l in _lashes)
            {
                float a = t - l.At; if (a < 0f || a > 0.6f) continue;
                var ctrl = Vol2FsB4F.Coil(BodyNow(l.S), Ground, 8, l.S.Index * 1.3f + 0.5f, 1.2f, 0.75f, 0.1f);
                var tb = _tubes.Add(ctrl, 0.04f, 0.02f, 30); tb.Grow = Vol2Fs.Smooth(0f, 0.12f, a) * (1f - Vol2Fs.Smooth(0.35f, 0.6f, a)); tb.Sides = 5;
            }
            if (_homeAt >= 0f) { float a = t - _homeAt, g = Vol2Fs.Smooth(0f, 0.4f, a) * (1f - Vol2Fs.Smooth(0.9f, 1.4f, a)); foreach (var r in _roots) r.Grow = g; }
            _tubes.Write(_vineMesh); _leaves.Write(_leafMesh); _glow.Write(_glowMesh); _ink.Write(_inkMesh); _edge.Write(_edgeMesh);
        }

        protected override float Tail => 1.5f;
    }
}
