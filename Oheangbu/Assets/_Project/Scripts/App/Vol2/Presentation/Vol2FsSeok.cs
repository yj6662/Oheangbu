using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>석 (metal, ㅓ attachment, 남긴다) - free showcase batch 3, concept seok_a (steel blades have grown along the whole brush shaft like the branches of a steel
    /// tree, turning the brush into a long GLAIVE, thin blades along the forearm; the swing cuts with a brilliant white slash and sparks). Unlike 서's single crescent
    /// guard it is a branching blade-tree that STAYS ON the weapon for the material's 6 s. Beats, executor facts only: the launch = blades sprout up the shaft in
    /// sequence, a long blade extends past the bristles -> worn for the Attached material's life (glints run along the edges); the brush's real motion leaves a
    /// razor-thin silver arc; a body the swing touched = white slash lines and sparks on it -> the last 0.4 s = the blades shiver and ring -> natural expiry (CSV:
    /// 쨍하게 끊기며 파편이 옆으로 튐) = they snap off with a ring and the shards fly SIDEWAYS.</summary>
    public sealed class Vol2FsSeok : Vol2FsWear
    {
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _sparks, _shards, _glints, _flash; Light _light;
        Material _steel, _steelHot;
        sealed class B { public Transform T; public float U, Ang, Len, Tilt, At, Rnd; public bool Arm; }
        readonly List<B> _blades = new List<B>();
        Mesh _trailMesh, _slashMesh, _inkMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips(), _slash = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        readonly List<(Vector3 C, Vector3 Side, float T)> _cuts = new List<(Vector3, Vector3, float)>();
        bool _snapped;
        // 2026-10-28 to concept seok_a (the user, after 우: "됐어, 다른 글자로"): a body the glaive touches is cut by ONE GREAT SLASH - a white-gold arc sweeping
        // from high behind the caster's left, through the body, out low right (camera-left to camera-right as in the concept), a black ink wing riding above it
        // that dries away slower, silver leaf-blades and ink bits shed off its head, sparks along it. Look only: it is laid at the contact the executor
        // reported and never moves a hit.
        // r5: the slash is a quadratic curve laid across the camera's view (P0 high left -> P1 over the body -> P2 low right past it), parameter s = 0 .. 1
        // (r1 - r4: a circle round the caster - seen from behind it was edge-on, a diagonal line off the caster's head)
        sealed class Great { public Vector3 P0, P1, P2, Up; public float T0, Rnd, LastHead; }
        readonly List<Great> _great = new List<Great>();
        Mesh _arcMesh, _haloMesh, _wingMesh; readonly Vol2FsStrips _arc = new Vol2FsStrips(), _halo = new Vol2FsStrips(), _wing = new Vol2FsStrips();
        ParticleSystem _feathers, _inkBits;
        const float HeadSeconds = 0.13f, CoreTail = 0.42f, WingTail = 0.95f;
        protected override float ExpireSeconds => 0.9f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);
        static Quaternion Look(Vector3 d, Vector3 up) => Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d.normalized, up.normalized)) > 0.97f ? Vector3.forward : up);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.82f, 0.86f, 0.95f), 0.5f, 0.82f, new Color(0.5f, 0.56f, 0.68f), "fs_steel");      // r2: less self-light
            _steelHot = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.95f, 0.93f, 0.88f), 0.6f, 0.85f, new Color(0.95f, 0.85f, 0.6f), "fs_steel");
            var blade = Kit.SharedMesh("blade", () => Vol2FsB1.Blade());
            // the glaive's long blade past the bristles, the branch blades up the shaft (both sides, angled forward like leaves on a twig), and thin blades on the forearm
            AddBlade(root, blade, 1.02f, 0f, 1.5f, 0f, 0f, false, true);
            // 10-28: denser and longer, an ear of steel leaves (seok_a) - r1 - batch 3: 12 blades read as a few spikes
            for (int k = 0; k < 22; k++)
            {
                float u = Mathf.Lerp(0.12f, 0.97f, k / 21f);
                AddBlade(root, blade, u, (k % 2 == 0 ? 1f : -1f) * (50f + 30f * Vol2Fs.Hash(Seed, k)) + 15f * Mathf.Sin(k * 1.7f), Mathf.Lerp(0.6f, 1.05f, Vol2Fs.Hash(Seed, k + 10)) * (0.7f + 0.75f * u), Mathf.Lerp(28f, 48f, Vol2Fs.Hash(Seed, k + 20)), 0.012f * k, false, k % 3 == 0);
            }
            for (int k = 0; k < 5; k++) AddBlade(root, blade, -0.05f - 0.04f * k, 90f + 60f * k, 0.45f, 70f, 0.03f * k, true, false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 14f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.12f, Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 900 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 1f, Speed1 = 2f, Size0 = 0.1f, Size1 = 0.24f, Radius = 0.05f, Gravity = 1.3f, Drag = 0.7f, Spin = 900f, Max = 160 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.14f, Size1 = 0.28f, Radius = 0.01f, Max = 120, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.1f, Size1 = 1.5f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 8, SortFudge = -30 }, root, "Flash");
            _feathers = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = blade, Life0 = 0.45f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.3f, Size1 = 0.65f, Gravity = 0.6f, Drag = 1.2f, Spin = 260f,
                SizeEnd = 0.6f, Max = 220 }, root, "Feathers");
            _inkBits = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.InkSolid("fs_sumi_drops", Vol2FsInkStage.InkC, Color.black, 0.05f, 0.1f, 0.35f, 1f), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 0f, Speed1 = 0.1f,
                Size0 = 0.04f, Size1 = 0.14f, Gravity = 1.3f, Drag = 0.4f, Tiles = 4, Max = 500, NoFlip = true }, root, "InkBits");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash, _feathers, _inkBits }) ps.Play(false);
            // r2: a full brush-stroke body (r1: fs_streak is a hairline in v - a 0.2 m strip of it hardly showed)
            var core = M("fs_stroke2", White, 4.5f, 1f); core.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "GreatArc", Kit.Mat(core), out _arcMesh);
            var halo = M("fs_stroke2", new Color(1f, 0.95f, 0.86f), 1.1f, 1f); halo.NoiseAmt = 0.4f; halo.Opacity = 0.25f;
            Vol2Fs.MeshObject(root, "GreatHalo", Kit.Mat(halo), out _haloMesh);
            Vol2Fs.MeshObject(root, "GreatWing", Kit.InkSolid("fs_sumi_stroke", Vol2FsInkStage.InkC, new Color(0.24f, 0.22f, 0.2f), 0.30f, 0.42f, 0.30f, 3.5f), out _wingMesh);
            var tr = M("fs_streak", White, 5f, 1f); tr.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "SilverArc", Kit.Mat(tr), out _trailMesh);
            Vol2Fs.MeshObject(root, "Slashes", Kit.Mat(tr), out _slashMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 0.85f, 0.7f, 1f), 0.05f, 0.45f, 0.3f, 1.5f), out _inkMesh);
            _light = Vol2Fs.PointLight(root, "SteelLight", new Color(1f, 0.95f, 0.85f), 4f);
        }

        void AddBlade(Transform root, Mesh mesh, float u, float ang, float len, float tilt, float at, bool arm, bool hot)
        {
            var r = Vol2FsB1.Prop(root, "Blade" + _blades.Count, mesh, hot ? _steelHot : _steel, false);
            r.transform.localScale = Vector3.one * 0.001f;
            _blades.Add(new B { T = r.transform, U = u, Ang = ang, Len = len, Tilt = tilt, At = at, Rnd = Vol2Fs.Hash(Seed, _blades.Count + 90), Arm = arm });
        }

        protected override void OnCharge(float t, float k) { if (Random.value < 0.3f * k) Vol2FsB1.Emit(_glints, Tip(), 1); }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { Vol2FsB1.Emit(_glints, Tip(), 3); Vol2FsB1.Emit(_sparks, Tip(), 40); }      // r2: no big flash at the tip

        void Cut(Vector3 c, Vector3 away)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(c - CamPos));
            float now = Inst.Rel(NowUs);
            _cuts.Add((c, side, now));
            Vol2FsB1.EmitV(_flash, c, Vector3.zero, 0.5f, -1f); Vol2FsB1.Emit(_sparks, c, 220); Vol2FsB1.Emit(_glints, c, 6);      // r7: a small flash (1.1 - 1.5 m read as a pale ball)
            // the great slash (one per 0.4 s): its plane tilts high-left -> low-right as the camera sees it, its radius carries it through the body
            if (_great.Exists(g => now - g.T0 < 0.4f)) return;
            // r6: laid in the CAMERA's frame (r5 used caster -> body: with the body at the caster's side that axis was the camera's right, so the arc ran into depth)
            Vector3 o = Inst.Origin, fw = FlatN(c - CamPos), rt = Vector3.Cross(Vector3.up, fw);
            if (Vector3.Dot(rt, CamRight()) < 0f) rt = -rt;
            // a little beyond the body so it reads in front of the caster; high on the left at head height, over the body's top, out low right
            Vector3 b = new Vector3(c.x, o.y, c.z) + fw * 0.35f;
            var gr = new Great { P0 = b - rt * 3.3f + Vector3.up * 2.1f - fw * 0.6f, P1 = b - rt * 0.3f + Vector3.up * 2.3f, P2 = b + rt * 2.8f - Vector3.up * 0.1f + fw * 0.4f,
                Up = (Vector3.up - fw * 0.3f).normalized, T0 = now - 0.06f, Rnd = Random.value, LastHead = 0f };
            _great.Add(gr);
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_inkBits, c + Random.insideUnitSphere * 0.3f, (rt * Random.Range(1f, 6f) + Random.insideUnitSphere * 3f + Vector3.up * 1.5f), -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_feathers, c + Random.insideUnitSphere * 0.3f, rt * Random.Range(3f, 8f) + Random.insideUnitSphere * 3f + Vector3.up * 2f, -1f, -1f);
        }
        Vector3 CamRight() { var cam = Camera.main; return cam != null ? cam.transform.right : Right; }
        static Vector3 ArcAt(Great g, float s) { float k = 1f - s; return k * k * g.P0 + 2f * k * s * g.P1 + s * s * g.P2; }
        float CamFade(Vector3 p) => Vol2Fs.Smooth(1.0f, 2.0f, Vector3.Distance(p, CamPos));

        /// <summary>The great slashes: a white-gold core (head drawn in 0.13 s, tail chasing 0.42 s behind), a gold halo, and the ink wing above that dries slower.</summary>
        void DrawGreat(float t, Vector3 cam)
        {
            _arc.Clear(); _halo.Clear(); _wing.Clear();
            const int n = 44;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var wp = new Vector3[n]; var ww = new float[n]; var wa = new float[n];
            foreach (var g in _great)
            {
                float a = t - g.T0; if (a < 0f || a > WingTail + 0.6f) continue;
                float hx = 1f - Mathf.Clamp01(a / HeadSeconds), head = 1f - hx * hx * hx;
                float tailC = Mathf.Clamp01((a - 0.06f) / CoreTail), tailW = Mathf.Clamp01((a - 0.15f) / WingTail);
                // shed off the moving head: silver leaves, ink bits, sparks
                if (head > g.LastHead + 0.01f)
                    for (float ph = g.LastHead; ph < head; ph += 0.04f)
                    {
                        Vector3 p = ArcAt(g, ph), tan = (ArcAt(g, ph + 0.02f) - p).normalized, outw = g.Up;
                        if (CamFade(p) < 0.6f) continue;
                        Vol2FsB1.EmitV(_feathers, p, tan * Random.Range(3f, 7f) + outw * Random.Range(0.5f, 2.5f) + Vector3.up * Random.Range(0f, 1.5f), -1f, -1f);
                        for (int k = 0; k < 2; k++) Vol2FsB1.EmitV(_inkBits, p + outw * 0.2f, tan * Random.Range(1f, 4f) + outw * Random.Range(0.5f, 2.5f) + Vector3.up, -1f, -1f);
                        for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_sparks, p, tan * Random.Range(4f, 10f) + Random.insideUnitSphere * 3f, -1f, -1f);
                    }
                g.LastHead = head;
                // the core and halo, from tail to head
                if (head > tailC + 0.01f)
                {
                    for (int j = 0; j < n; j++)
                    {
                        float s = j / (n - 1f), ph = Mathf.Lerp(tailC, head, s);
                        var p = ArcAt(g, ph);
                        pts[j] = p;
                        // thick behind the head, needle at both ends (r3: Max - sin(pi) is a hair below 0 and Pow made it NaN, which drops the whole strip)
                        float body = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(s * Mathf.PI)), 0.35f) * Mathf.Lerp(0.6f, 1f, s);      // r7: fuller along its length (r6: the tail half read as nothing)
                        w[j] = 0.24f * body;      // r5: a thinner, whiter blade (r4: 0.32 with a gold halo x3 read as a golden band)
                        al[j] = body * CamFade(p) * (1f - Vol2Fs.Smooth(0.2f, CoreTail + 0.06f, a) * (1f - s));
                    }
                    // the stroke's wet head (u = 0) rides the arc's head
                    _arc.Strip(pts, w, null, Color.white, al, g.Rnd, cam, 1f, 0f);
                    for (int j = 0; j < n; j++) w[j] *= 2.4f;
                    for (int j = 0; j < n; j++) al[j] *= 0.6f;
                    _halo.Strip(pts, w, null, Color.white, al, g.Rnd + 0.3f, cam, 1f, 0f);
                }
                // the ink wing: rides above and outside the core, wide, ragged, drying from its tail
                if (head > tailW + 0.01f)
                {
                    float dry = Vol2Fs.Smooth(0.35f, WingTail + 0.15f, a);
                    for (int j = 0; j < n; j++)
                    {
                        float s = j / (n - 1f), ph = Mathf.Lerp(tailW, head, s);
                        var p = ArcAt(g, ph);
                        wp[j] = p + g.Up * (0.55f + 0.15f * Mathf.Sin(s * 9f + g.Rnd * 6f));
                        float body = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(s * Mathf.PI)), 0.7f);
                        ww[j] = 1.1f * body * (1f - 0.4f * dry) * (0.8f + 0.3f * Mathf.Sin(s * 5f + g.Rnd * 3f));
                        wa[j] = (1f - dry * (1.2f - s)) * CamFade(wp[j]);
                    }
                    // r4: the sheet's broad dry-brush row (bottom), its wet head on the arc's head (r1 - r3: the thin second row read as a line)
                    _wing.Strip(wp, ww, null, Color.white, wa, g.Rnd, cam, 1f, 0f, 0.012f, 0.238f);
                }
            }
            _arc.Write(_arcMesh); _halo.Write(_haloMesh); _wing.Write(_wingMesh);
        }
        protected override void OnStruck(Struck s) { Cut(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }
        protected override void OnTouch(Struck s) { if (Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.05f)) return; Cut(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }

        protected override void OnResidueEnd()
        {
            if (_snapped) return;
            _snapped = true;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(Inst.Origin - CamPos));
            Vol2FsB1.Emit(_flash, Tip(), NaturalEnd ? 2 : 1);
            foreach (var b in _blades)
            {
                if (b.T.localScale.z < 0.01f) continue;
                Vector3 mid = b.T.position + b.T.forward * b.T.localScale.z * 0.5f;
                int n = NaturalEnd ? 3 : 1;
                for (int j = 0; j < n; j++)
                    Vol2FsB1.EmitV(_shards, mid, NaturalEnd ? side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(4f, 9f) + Vector3.up * Random.Range(0.5f, 2.5f) : Random.insideUnitSphere * 1.2f, Random.Range(0.1f, 0.22f), -1f);
                if (NaturalEnd) Vol2FsB1.EmitV(_sparks, mid, side * Random.Range(-10f, 10f) + Vector3.up * 1.5f, -1f, -1f);
                b.T.gameObject.SetActive(false);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float worn = Worn(t), warn = Warn(NowUs), pulse = DrawPulse(t);
            Vector3 tip = Tip(), grip = Grip(), sleeve = Sleeve();
            Vector3 ax = (tip - grip).sqrMagnitude > 1e-6f ? (tip - grip).normalized : Vector3.up;
            Vector3 n0 = Vector3.Cross(ax, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(ax, Vector3.up).normalized : Right;
            float grow = h;
            foreach (var b in _blades)
            {
                if (_snapped) break;
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((grow - b.At - (b.Arm ? 0.1f : 0.2f * Mathf.Clamp01(b.U))) / 0.18f)) * worn;
                Vector3 at = b.U < 0f ? Vector3.Lerp(grip, sleeve, -b.U / 0.25f) : Vector3.LerpUnclamped(grip, tip, b.U);
                Vector3 outN = Quaternion.AngleAxis(b.Ang, ax) * n0;
                Vector3 dir = b.U >= 1f ? ax : (Quaternion.AngleAxis(-b.Tilt, Vector3.Cross(outN, ax)) * outN);
                dir = (dir + ax * (b.Arm ? 0.2f : 0.55f)).normalized;
                if (warn > 0f) dir = Quaternion.Euler((Mathf.PerlinNoise(t * 47f, b.Rnd * 9f) - 0.5f) * 10f * warn, (Mathf.PerlinNoise(b.Rnd * 7f, t * 43f) - 0.5f) * 10f * warn, 0f) * dir;
                b.T.SetPositionAndRotation(at, Look(dir, ax) * Quaternion.AngleAxis(b.Rnd * 90f, Vector3.forward));
                b.T.localScale = new Vector3(b.U >= 1f ? 3.2f : 2.2f, 2.2f, Mathf.Max(0.001f, b.Len * g));
                if (g > 0.9f && Random.value < 0.01f + 0.05f * warn) Vol2FsB1.Emit(_glints, at + dir * b.Len * Random.Range(0.3f, 1f), 1);
            }
            // the razor-thin silver arc of the swing (real motion only) + a few ink threads
            _trail.Clear(); _ink.Clear();
            float tl = worn * Vol2Fs.Smooth(1.5f, 4f, TipSpeed(t));
            if (tl > 0.01f) { SwingTrail(_trail, t, 0.16f, 0.12f, tl, 0.21f); SwingTrail(_ink, t, 0.2f, 0.05f, tl * 0.8f, 0.61f, 0.03f); }
            _trail.Write(_trailMesh); _ink.Write(_inkMesh);
            // the cut lines on the struck bodies
            _slash.Clear();
            foreach (var c in _cuts)
            {
                float a = t - c.T, l = 1f - Vol2Fs.Smooth(0.08f, 0.45f, a); if (l <= 0.01f) continue;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 d = (c.Side * Mathf.Cos(0.5f + 0.2f * j) - Vector3.up * Mathf.Sin(0.5f + 0.2f * j)).normalized; float s = 0.9f * Vol2Fs.EaseOutBack(a / 0.06f);
                    Vector3 o = Vector3.up * (0.22f * (j - 1));
                    _slash.Strip(new[] { c.C + o - d * s, c.C + o, c.C + o + d * s }, new[] { 0.015f, 0.09f, 0.015f }, null, Color.white, new[] { l, l, l }, 0.3f + 0.2f * j, cam, 0f, 1f);
                }
            }
            _slash.Write(_slashMesh);
            DrawGreat(t, cam);
            _light.transform.position = Vector3.Lerp(grip, tip, 0.7f);
            _light.intensity = (_snapped ? Mathf.Max(0f, 8f * (1f - ExitK(t) * 3f)) : 1.6f * worn * (1f + 1.5f * pulse) * (warn > 0f ? 0.6f + 0.4f * Mathf.Sin(t * 60f) : 1f));
        }
    }
}
