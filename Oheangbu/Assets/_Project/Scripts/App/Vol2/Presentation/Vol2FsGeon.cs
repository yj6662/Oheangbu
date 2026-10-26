using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>건 (wood, ㅓ attachment, 번진다) - free showcase batch 4, concept geon_a (a whirlwind of ink-vine: dark ink ribbons veined with jade light wind round the
    /// brush and the sleeve; the swing strikes the close bear and the ribbons whirl round it, then stream on to the two other bears and wind round them too, leaves
    /// flying). Beats, all executor facts: the attachment host's 6 s = ink-vine ribbons spiralling up the brush shaft and the sleeve (HostLife; the last 0.4 s fray)
    /// -> every swing (the real brush motion) leaves an ink-vine arc -> the "Instant" touch = a vortex of ribbons round that body -> every "Spread" contact = a pair
    /// of ribbons streaming on an S-curve from it to the neighbour (width by the share), which winds round the neighbour (its snare) -> the Mark (3 s) = the winding
    /// slowly turning + a mossy ink stain at the feet; warning = the ribbons fray -> natural expiry (CSV: 잔뿌리로 풀림, 앞으로 뻗으며) = the winding slides down and
    /// unravels into ink rootlets running forward along the ground; other reasons = their shorter exits.</summary>
    public sealed class Vol2FsGeon : Vol2FsSpread
    {
        static readonly Color Jade = new Color(0.3f, 1f, 0.3f), Ink = new Color(0.04f, 0.16f, 0.05f), Rim = new Color(0.2f, 0.95f, 0.22f, 1f);      // r2: greener ink, a lower rim (r1 ribbons read pale cream: the rim and the glow saturated)
        ParticleSystem _leaves, _sparks, _drops; Light _brushLight;
        Mesh _inkMesh, _glowMesh, _mossMesh, _rootMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _glow = new Vol2FsStrips(), _moss = new Vol2FsStrips(), _roots = new Vol2FsStrips();
        sealed class Whirl { public Vector3 Feet; public Bounds B; public Transform T; public float At; public float Phase; }
        readonly List<Whirl> _struck = new List<Whirl>();
        // r3 (Phantom Blade Zero reference, principles 2 + 5): the swing as a blade-edged crescent + two phantom afterimages in the body's accent colour
        Mesh _phantomMesh; readonly Vol2FsStrips _phantom = new Vol2FsStrips(); float _lastFast = -9f;
        protected override float HopSpeed => 8f;
        protected override float HopMin => 0.3f;
        protected override float HopMax => 0.55f;
        protected override float HopStagger => 0.11f;
        protected override float MarkExpireSeconds => 0.9f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var leaf = M("fs_leaf", new Color(0.6f, 0.95f, 0.45f, 1f), 1.2f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.0f, Life1 = 2.2f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.25f, Gravity = 0.2f, Drag = 1.5f,
                Noise = 1.3f, NoiseFreq = 0.8f, Tiles = 2, Spin = 320f, AlphaHoldTo = 0.75f, Max = 700 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1.0f, Speed0 = 0.4f, Speed1 = 2.5f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.2f,
                Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.2f, NoiseFreq = 2f, Max = 1000 }, root, "Sparks");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = Rim; drop.EdgeWidth = 0.16f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.22f, Radius = 0.2f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkDrops");
            foreach (var ps in new[] { _leaves, _sparks, _drops }) ps.Play(false);
            // the ribbons: solid ink with a jade rim (InkSolid: depth-writing, no paper wash over the sky) + a thin jade glow line riding them
            Vol2Fs.MeshObject(root, "InkVine", Kit.InkSolid("fs_stroke", Ink, Rim, 0.24f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var glow = M("fs_streak", new Color(0.15f, 0.9f, 0.2f), 1.1f, 1f); glow.NoiseAmt = 0.5f; glow.Erode = 0.25f;
            Vol2Fs.MeshObject(root, "VineGlow", Kit.Mat(glow), out _glowMesh);
            var moss = M("fs_moss", new Color(0.3f, 0.55f, 0.22f, 1f), 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.4f; moss.Opacity = 0.85f; moss.Edge = new Color(0.25f, 0.9f, 0.2f, 1f); moss.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "Moss", Kit.Mat(moss), out _mossMesh);
            Vol2Fs.MeshObject(root, "InkRootlets", Kit.InkSolid("fs_stroke2", Ink, Rim * 0.7f, 0.12f, 0.5f, 0.3f, 1.6f), out _rootMesh);
            _brushLight = Vol2Fs.PointLight(root, "BrushLight", Jade, 3f);
            Vol2Fs.MeshObject(root, "Phantoms", Vol2FsB4A.PhantomMat(Kit, new Color(0.25f, 1f, 0.45f)), out _phantomMesh);
        }

        protected override void OnCharge(float t, float k)
        {
            // the vine gathers on the brush as the swing winds up
            if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_sparks, Shaft(Random.value) + Random.insideUnitSphere * 0.25f, 1);
        }

        protected override void OnLaunch(long nowUs) { Vol2FsB1.Emit(_leaves, Tip(), 24); Vol2FsB1.Emit(_sparks, Tip(), 30); }
        protected override void OnHit(long nowUs) { }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind == "Second") { Vol2FsB1.Emit(_leaves, CentreNow(s), 30); Vol2FsB1.Emit(_drops, CentreNow(s), 20); return; }
            _struck.Add(new Whirl { Feet = s.Feet, B = s.B, T = s.T, At = s.At, Phase = Vol2Fs.Hash(Seed, (int)s.Id) * 6f });
            var c = CentreNow(s);
            Vol2FsB1.Emit(_leaves, c, 120); Vol2FsB1.Emit(_sparks, c, 120); Vol2FsB1.Emit(_drops, c, 50);
        }

        protected override void OnLand(Hop h) { var c = CentreNow(h); Vol2FsB1.Emit(_leaves, c, Mathf.RoundToInt(70 * ShareScale(h.Share))); Vol2FsB1.Emit(_sparks, c, 60); }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vector3 c = m.Centre + Vector3.up * 0.5f;
            Vol2FsB1.Emit(_leaves, c, Natural(m) ? 25 : 45); if (!Natural(m)) Vol2FsB1.Emit(_sparks, c, 40);
        }

        /// <summary>A winding of 3 ribbons round a body (its snare): rotating, rising, the life driving width and erosion.</summary>
        void Winding(Vector3 c, Bounds b, float t, float phase, float life, float fray, float slide, float k, Vector3 cam, float rnd)
        {
            float rx = b.extents.x + 0.22f, rz = b.extents.z + 0.22f;
            for (int r = 0; r < 3; r++)
            {
                float y = b.min.y + 0.15f + b.size.y * (0.18f + 0.3f * r) * (1f - slide) - 0.2f * slide;
                float a0 = phase + r * 2.1f + t * (2.2f - 0.5f * r) * (r % 2 == 0 ? 1f : -1f);
                float l = life * (fray > 0f ? (0.6f + 0.4f * Mathf.PerlinNoise(t * 17f, r * 3.1f)) * (1f - 0.5f * fray) : 1f);
                Vol2FsB4A.Wrap(_ink, c, rx * (1f + 0.12f * r), rz * (1f + 0.12f * r), y, a0, Mathf.PI * (1.25f + 0.2f * r), 0.36f * k, l, rnd + r * 0.13f, cam, 0.25f, 20, 0.12f);
                Vol2FsB4A.Wrap(_glow, c, rx * (1f + 0.12f * r) + 0.01f, rz * (1f + 0.12f * r) + 0.01f, y + 0.02f, a0 + 0.1f, Mathf.PI * (1.1f + 0.2f * r), 0.045f * k, l * 0.9f, rnd + r * 0.21f, cam, 0.25f, 20, 0.12f);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _ink.Clear(); _glow.Clear(); _moss.Clear(); _roots.Clear(); _phantom.Clear();
            // the dress: three ink-vine ribbons spiralling up the brush shaft from the sleeve, for the host's life; frayed in its last 0.4 s
            float dress = HostLife(t, 0.25f, 0.4f), warn = HostWarn(t);
            if (dress > 0.01f)
            {
                Vector3 g = Grip(), tp = Tip(), axis = ShaftAxis;
                Vector3 n = Vector3.Cross(axis, Vector3.up); if (n.sqrMagnitude < 1e-4f) n = Right; n.Normalize(); Vector3 bn = Vector3.Cross(axis, n);
                Vector3 sl = Sleeve();
                for (int v = 0; v < 3; v++)
                {
                    var pts = new Vector3[16]; var w = new float[16]; var al = new float[16];
                    for (int j = 0; j < 16; j++)
                    {
                        float u = j / 15f, a = u * Mathf.PI * 2f * 2.2f + v * 2.09f + t * 3.5f;
                        Vector3 on = u < 0.2f ? Vector3.Lerp(sl, g, u / 0.2f) : Vector3.Lerp(g, tp + axis * 0.15f, (u - 0.2f) / 0.8f);
                        float rad = 0.07f + 0.05f * Mathf.Sin(u * Mathf.PI);
                        pts[j] = on + (n * Mathf.Cos(a) + bn * Mathf.Sin(a)) * rad;
                        w[j] = 0.1f * (0.6f + 0.4f * Mathf.Sin(u * Mathf.PI)); al[j] = dress * (warn > 0f ? 0.55f + 0.45f * Mathf.PerlinNoise(t * 19f, v + u * 3f) : 1f);
                    }
                    _ink.Strip(pts, w, null, Color.white, al, 0.1f + v * 0.17f, cam, 0f, 1f);
                    for (int j = 0; j < 16; j++) w[j] *= 0.35f;
                    _glow.Strip(pts, w, null, Color.white, al, 0.3f + v * 0.11f, cam, 0f, 1f);
                }
                _brushLight.transform.position = tp; _brushLight.intensity = 2.2f * dress * (1f - 0.6f * warn);
                if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_sparks, Shaft(Random.value), 1);
                if (Time.frameCount % 12 == 0) Vol2FsB1.Emit(_leaves, Tip(), 1);
                // the swing: an ink-vine arc along the real brush path while it moves fast
                if (TipSpeed() > 3f) _lastFast = t;
                if (t - _lastFast < 0.12f) { Vol2FsB4A.BladeTrail(_ink, TipPath, t, 0.22f, 0.42f, dress, 0.77f, cam); Vol2FsB4A.BladeTrail(_glow, TipPath, t, 0.2f, 0.08f, dress, 0.55f, cam); Vol2FsB4A.Phantoms(_phantom, TipPath, t, 0.22f, 0.42f, dress, cam); }
            }
            else _brushLight.intensity = 0f;
            // the direct touches: a vortex round the struck body (it is the spread's source), held while its neighbours' marks stand
            float lastMark = -1f; bool anyLive = false;
            foreach (var m in MarkList) { if (m.EndAt < 0f) anyLive = true; else lastMark = Mathf.Max(lastMark, m.EndAt); }
            foreach (var w in _struck)
            {
                float a = t - w.At; if (a < 0f) continue;
                float life = Vol2Fs.Smooth(0f, 0.2f, a) * (anyLive || a < 1.2f ? 1f : 1f - Vol2Fs.Smooth(0f, 0.7f, t - Mathf.Max(lastMark, w.At + 1.2f)));
                if (life <= 0.01f) continue;
                var bb = w.T != null ? BodyBounds(w.T, w.T.position, 0.5f) : w.B;
                Winding(bb.center, bb, t, w.Phase, life, 0f, 0f, 1.1f, cam, 0.42f);
            }
            // the hops: a pair of ribbons streaming on an S-curve from the struck body to the neighbour, then standing as a thin link while the mark stands
            foreach (var hop in Hops)
            {
                float g = HopK(hop, t); if (t < hop.Start) continue;
                float ex = hop.Mark != null ? MarkExit(hop.Mark, t) : (t > hop.Start + 3.5f ? 1f : 0f);
                if (ex >= 1f) continue;
                float k = ShareScale(hop.Share);
                Vector3 a = hop.FromP + Vector3.up * 1.0f, b = CentreNow(hop);
                float swing = (hop.Order % 2 == 0 ? 1f : -1f) * 0.6f, lob = 1.3f;
                float head = g, tail = g < 1f ? Mathf.Max(0f, g - 0.55f) : Mathf.Lerp(0f, 0.85f, Vol2Fs.Smooth(0f, 0.6f, t - hop.Start - hop.Travel));
                float life = (1f - ex) * (g < 1f ? 1f : 1f - 0.6f * Vol2Fs.Smooth(0f, 0.6f, t - hop.Start - hop.Travel));
                for (int v = 0; v < 2; v++)
                {
                    float ph = v * 2.4f + hop.Order;
                    Vol2FsB4A.SRibbon(_ink, a, b, lob + 0.15f * v, swing * (1f - 0.3f * v), tail, head, 0.42f * k, life, 0.2f + 0.31f * v + hop.Order * 0.1f, cam, 18, 0.18f, ph + t * 4f);
                    Vol2FsB4A.SRibbon(_glow, a, b, lob + 0.15f * v, swing * (1f - 0.3f * v), tail, head, 0.05f * k, life, 0.4f + 0.2f * v, cam, 18, 0.18f, ph + t * 4f);
                }
                if (g > 0f && g < 1f) { var p = Vol2FsB4A.SPath(a, b, lob, swing, g); Vol2FsB1.Emit(_sparks, p, 5); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_leaves, p, 1); if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drops, p, 1); }
            }
            // the marks: the winding on the neighbour (its snare, slowing), a mossy ink stain; natural expiry = the winding slides down into rootlets running forward
            foreach (var m in MarkList)
            {
                float age = MarkAge(m, t); if (age < 0f) continue;
                float ex = MarkExit(m, t), warn2 = MarkWarn(m, t), k = ShareScale(m.Share);
                var hop = m.Hop; Bounds bb = hop != null ? (hop.T != null ? BodyBounds(hop.T, hop.T.position, 0.5f) : hop.B) : new Bounds(m.Centre + Vector3.up * 0.6f, Vector3.one * 1.2f);
                float grow = Vol2Fs.Smooth(0f, 0.3f, age);
                bool natural = Natural(m) && m.EndAt >= 0f;
                float slide = natural ? Vol2Fs.Smooth(0f, 0.6f, ex) : 0f;
                float life = grow * (natural ? 1f - Vol2Fs.Smooth(0.4f, 1f, ex) : 1f - ex);
                Winding(bb.center, bb, t * (1f - 0.4f * Mathf.Clamp01(age / 3f)), m.Id * 0.7f, life, warn2, slide, k, cam, 0.6f + m.Id % 5 * 0.07f);
                float sl = Vol2Fs.Smooth(0f, 0.35f, age) * (1f - ex) * (1f - 0.4f * warn2);
                if (sl > 0.01f) _moss.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, m.Radius * 3.4f * k, new Color(1f, 1f, 1f, sl), 0.27f + m.Id % 7 * 0.1f);
                if (natural && ex > 0.2f)
                {
                    // ink rootlets running forward along the ground (CSV 잔뿌리로 풀림)
                    Vector3 fwd = FlatN(m.Centre - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
                    float rg = Vol2Fs.Smooth(0.2f, 0.75f, ex), rf = 1f - Vol2Fs.Smooth(0.75f, 1f, ex);
                    for (int j = 0; j < 7; j++)
                    {
                        float s = (j / 6f - 0.5f) * 1.3f; Vector3 a = m.Centre + side * s * 0.5f, b = a + (fwd + side * s * 0.5f).normalized * (0.9f + 0.7f * Vol2Fs.Hash(Seed + (int)m.Id, j));
                        Vol2FsB4.Runner(_roots, a, b, Ground, 0f, rg, 0.07f, rf, 0.15f + j * 0.09f, cam, 0.12f, j * 1.3f, 10, 0.03f);
                    }
                }
                if (ex <= 0f && Time.frameCount % 5 == (int)(m.Id % 5)) Vol2FsB1.Emit(_sparks, bb.center + Random.insideUnitSphere * 0.5f, 1);
            }
            _ink.Write(_inkMesh); _glow.Write(_glowMesh); _moss.Write(_mossMesh); _roots.Write(_rootMesh); _phantom.Write(_phantomMesh);
        }
    }
}
