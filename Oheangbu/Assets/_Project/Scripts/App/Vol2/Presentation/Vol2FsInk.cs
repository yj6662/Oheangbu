using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // 2026-10-27 먹 세계 맞추기 (the user, 10-27: "이제 가시성은 좋지만, 세계관, 월드, 맵, 컨셉과 조금 다른 부분도 많고, vfx도 부족한 부분이 많아."; design =
    // Docs/Research/vfx120/world_ink_pass_design.md). The concepts the user chose are 60 - 70 % black sumi ink (huge brush-sweep strokes with dry-brush / flying white,
    // hundreds of droplets, pooled ink on the ground) with ONE body-coloured accent; the engine looks had almost no ink. ONE shared layer every showcase look inherits
    // (the 120 casts through Vol2FsRun.KitTick / KitContact, the 25 reactions through Vol2FsReact): it draws on top of whatever the look itself draws and never decides,
    // blocks or delays anything - it reads only the executor's facts the look already has (the launch clock, the brush tip's real path, the spec's frame geometry, the
    // path head, the accepted contacts and where the bodies are). Switch: free:ink:on|off (Vol2FsWorld.InkOn).
    //  a. the swing: a big sumi crescent round the caster in the brush's direction (replaces the accent crescent)
    //  b. the launch splash at the tip      c. a big gesture of the frame (path flanks / fan arc / ring enso / attachment spiral)
    //  d. the contact burst (splat, droplets, X slashes, accent sparks)      e. ground stains      f. the slow ink-ash aftermath

    /// <summary>One brush stroke: a polyline drawn as a camera-facing (or ground-flat) ribbon of the sumi stroke sheet.</summary>
    public sealed class InkStroke
    {
        /// <summary>The polyline, start (wet head) -> end (dry tail). A live stroke replaces it every frame.</summary>
        public Vector3[] P;
        public float W = 0.7f;
        /// <summary>The row of fs_sumi_stroke (0 pressed fat, 1 whip, 2 hook, 3 split dry brush).</summary>
        public int Row;
        public float T0, Grow = 0.2f, Hold = 0.3f, Dry = 1f;
        /// <summary>> 0: the drying starts here (a live stroke sets it when it freezes); live strokes without it never dry.</summary>
        public float HoldUntil = -1f;
        public bool Live;
        public float Rnd = 0.3f;
        /// <summary>Width of the accent's thin inner leading edge as a share of <see cref="W"/> (0 = none).</summary>
        public float AccW = 0.14f;
        /// <summary>The side the accent edge sits on (toward this point); also the "outward" of the head spray.</summary>
        public Vector3 Pivot; public bool HasPivot;
        /// <summary>A ground-flat ribbon: its width stands radially round <see cref="Pivot"/> (the ring).</summary>
        public bool Flat;
        /// <summary>Flings droplets off the growing head.</summary>
        public bool Spray;
        /// <summary>While drying the stroke is drawn toward this point (the brush swallows the spiral).</summary>
        public Func<Vector3> Absorb;
        public bool Dead;
        /// <summary>r2: the pressure profile - the head is pressed fat, the tail whips thin (0 = even width, 0.85 = the tail at 15 %).</summary>
        public float TailThin;
        /// <summary>r2: the sight window - where the stroke crosses the camera's line to this point it breaks into dry-brush hairline (it never hides what the spell does).</summary>
        public Vector3 WinAim; public bool HasWin;
    }

    /// <summary>The shared ink layer of ONE look (a cast or a reaction). Lives under that look's root; every piece is placed in world coordinates.</summary>
    public sealed class Vol2FsInkStage
    {
        public static readonly Color InkC = new Color(0.014f, 0.012f, 0.010f, 1f);
        readonly Vol2FsKit _kit; readonly Transform _root; readonly Color _acc; readonly int _seed;
        readonly List<InkStroke> _strokes = new List<InkStroke>();
        readonly Vol2FsStrips _inkS = new Vol2FsStrips(), _accS = new Vol2FsStrips(), _stainS = new Vol2FsStrips();
        Mesh _inkM, _accM, _stainM;
        ParticleSystem _drops, _flakes, _splat, _pool, _ash, _sparks;
        readonly List<Stain> _stains = new List<Stain>();
        float _lastT = float.NaN, _lastEvent = -1e9f;
        Vector3 _cam;
        public string Note = "";
        public int Events;

        struct Stain { public Vector3 P; public float R, T0, Spread, Life, Rnd, Yaw; public int Cell; }

        public Vol2FsInkStage(Vol2FsKit kit, Transform root, Color accent, int seed)
        {
            _kit = kit; _root = root; _acc = accent; _seed = seed;
            var c = Camera.main; _cam = c != null ? c.transform.position : Vector3.zero;
            Build();
        }

        // ---------------------------------------------------------------- build
        void Build()
        {
            // solid ink (alpha cut + depth: the world post treats it as a thing in the scene - a transparent stroke over the sky is washed to paper)
            // r3: tone in the body - a wash-grey rim bleeding 0.3 in from the cut and noise pockets broken into streaks (r2: one flat near-black fill read as a shadow on the soil)
            var stroke = _kit.InkSolid("fs_sumi_stroke", InkC, new Color(0.24f, 0.22f, 0.20f), 0.30f, 0.42f, 0.30f, 3.5f);
            var accent = Vol2FsMat.Make("fs_streak", _acc, 3.4f, 1f); accent.NoiseAmt = 0.25f; accent.Soft = 0.02f; accent.Erode = 0.2f; accent.Scroll = new Vector2(-1.2f, 0f);
            var stain = Vol2FsMat.Make("fs_sumi_stain", new Color(0.05f, 0.045f, 0.04f), 1f, 0f); stain.NoiseAmt = 0.12f; stain.Erode = 1f; stain.Soft = 0.01f; stain.Scroll = Vector2.zero; stain.NoiseScale = 2f;
            stain.Opacity = 0.72f;      // r3: wet ink on the ground, not a hole (r2: opaque near-black read as a shadow)
            Vol2Fs.MeshObject(_root, "InkStrokes", stroke, out _inkM);
            Vol2Fs.MeshObject(_root, "InkAccent", _kit.Mat(accent), out _accM);
            Vol2Fs.MeshObject(_root, "InkStains", _kit.Mat(stain), out _stainM);
            // r2: the drops are the teardrop / tailed shapes of fs_sumi_drops, stretched along the flight (r1: round dots under a hard cut read as black gravel)
            var dropM = _kit.InkSolid("fs_sumi_drops", InkC, Color.black, 0.05f, 0.1f, 0.35f, 1f);
            var flakeM = _kit.InkSolid("fs_sumi_drops", InkC, Color.black, 0.05f, 0.15f, 0.35f, 1.5f);
            var splatM = _kit.InkSolid("fs_sumi_splat", InkC, Color.black, 0.05f, 0.2f, 0.32f, 1.2f);
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = dropM, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.03f, Size1 = 0.09f, Gravity = 1f, Tiles = 4, Stretch = true, StretchVel = 0.09f, StretchLen = 1.6f, AlphaHoldTo = 0.7f, Max = 900, NoFlip = true }, _root, "InkDrops");
            _flakes = Vol2Fs.Build(new Vol2FsPs { Mat = flakeM, Life0 = 0.5f, Life1 = 0.95f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 1.2f, Drag = 0.3f, Tiles = 4, Spin = 200f, AlphaHoldTo = 0.6f, Max = 500, NoFlip = true }, _root, "InkFlakes");
            _splat = Vol2Fs.Build(new Vol2FsPs { Mat = splatM, Life0 = 0.55f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.05f, Size0 = 0.8f, Size1 = 1.2f, SizeStart = 0.5f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 1.15f, Tiles = 4, AlphaHoldTo = 0.35f, Max = 48, NoFlip = true }, _root, "InkSplat");
            // r3: the contact splat lies on the ground as a pool at the body's feet (r2: an upright disc beside the body read as a black flag)
            _pool = Vol2Fs.Build(new Vol2FsPs { Mat = splatM, Life0 = 0.3f, Life1 = 0.45f, Speed0 = 0f, Speed1 = 0f, Size0 = 1f, Size1 = 1.2f, SizeStart = 0.35f, SizePeak = 1f, SizePeakAt = 0.35f, SizeEnd = 1.1f, Tiles = 4, Horizontal = true, AlphaHoldTo = 0.3f, Max = 24, NoFlip = true }, _root, "InkPool");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = flakeM, Life0 = 2.0f, Life1 = 3.2f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.05f, Size1 = 0.11f, Gravity = 0.12f, Drag = 0.6f, Noise = 0.35f, NoiseFreq = 0.8f, Tiles = 4, Spin = 90f, AlphaHoldTo = 0.5f, Max = 360, NoFlip = true }, _root, "InkAsh");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(Vol2FsMat.Make("fs_dot", _acc, 4.5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.2f, Drag = 1.0f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 500, NoFlip = true }, _root, "InkSparks");
            foreach (var ps in new[] { _drops, _flakes, _splat, _pool, _ash, _sparks }) ps.Play(false);
        }

        // ---------------------------------------------------------------- state
        /// <summary>Anything still drawn or falling (the look must not be retired while it is).</summary>
        public bool Busy(float t) => _strokes.Count > 0 || _stains.Count > 0 || t < _lastEvent + 3.4f;
        void Touch(float t) { _lastEvent = Mathf.Max(_lastEvent, t); Events++; }
        public InkStroke Add(InkStroke s) { _strokes.Add(s); return s; }
        public void Freeze(InkStroke s, float t, float hold) { if (s != null && s.HoldUntil < 0f) s.HoldUntil = t + hold; }

        // ---------------------------------------------------------------- emitters
        static Vector3 Spread(Vector3 axis, float cone)
        {
            var r = UnityEngine.Random.onUnitSphere;
            return (axis.normalized + r * cone).normalized;
        }

        /// <summary>Ink droplets flung from a point (the stretched drop sprite + a few flakes).</summary>
        public void Drops(Vector3 at, Vector3 axis, float cone, int n, float v0, float v1, float s0, float s1)
        {
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            for (int k = 0; k < n; k++)
            {
                Vector3 vel = Spread(axis, cone) * UnityEngine.Random.Range(v0, v1);
                Vol2FsB1.EmitV(_drops, at + UnityEngine.Random.insideUnitSphere * 0.08f, vel, s0 + (s1 - s0) * Mathf.Pow(UnityEngine.Random.value, 2.2f), -1f);
            }
            for (int k = 0; k < n / 10; k++)
                Vol2FsB1.EmitV(_flakes, at + UnityEngine.Random.insideUnitSphere * 0.1f, Spread(axis, cone * 1.2f) * UnityEngine.Random.Range(v0 * 0.5f, v1 * 0.8f), UnityEngine.Random.Range(0.04f, 0.11f), -1f);
        }

        public void Sparks(Vector3 at, Vector3 axis, float cone, int n, float v0, float v1)
        {
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_sparks, at + UnityEngine.Random.insideUnitSphere * 0.1f, Spread(axis, cone) * UnityEngine.Random.Range(v0, v1), -1f, -1f);
        }

        public void SplatAt(Vector3 at, float size) { Vol2FsB1.EmitV(_splat, at, Vector3.zero, size, UnityEngine.Random.Range(0.55f, 0.8f)); }

        public void Ash(Vector3 at, int n, float radius)
        {
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_ash, at + UnityEngine.Random.insideUnitSphere * radius + Vector3.up * 0.2f, new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(0.2f, 1.1f), UnityEngine.Random.Range(-0.5f, 0.5f)), -1f, -1f);
        }

        public void StainAt(Vector3 p, float radius, float t, float delay = 0f, float life = 4f)
        {
            _stains.Add(new Stain { P = p, R = radius, T0 = t + delay, Spread = Mathf.Lerp(0.15f, 0.4f, Mathf.Clamp01(radius / 1.4f)), Life = life, Rnd = UnityEngine.Random.value, Yaw = UnityEngine.Random.value * 360f, Cell = UnityEngine.Random.Range(0, 4) });
            Touch(t + delay + life - 3f);
        }

        // ---------------------------------------------------------------- b. the launch splash (the brush tip, the launch moment)
        public void Splash(Vector3 tip, Vector3 aim, Vector3 tipVel, float ground, Vector3 origin, Vector3 fwd, Vector3 right, float t)
        {
            Touch(t);
            // r2b: forward, away from the camera (r2a: the tip's own velocity sent drops back past the lens as big black ovals)
            Vector3 tl = tipVel - Vector3.Dot(tipVel, aim.normalized) * aim.normalized; tl.y = 0f;
            Vector3 axis = aim.normalized * 0.75f + (tl.sqrMagnitude > 1e-4f ? tl.normalized : Vector3.zero) * 0.25f + Vector3.up * 0.15f;
            int n = UnityEngine.Random.Range(70, 121);
            Drops(tip, axis, 0.62f, n, 4f, 9f, 0.03f, 0.1f);
            // r2: small splats only (r1: 0.9 - 1.8 m blots at the brush, a metre from the camera, covered the cast)
            // r3: no splat discs at the launch any more (r2b: the one thrown off the tip hung beside the target as a black flag) - the drops carry the splash
            Sparks(tip, axis, 0.5f, 18, 4f, 10f);
            // where the drops land: small stains ahead of the caster
            for (int k = 0; k < 11; k++)
            {
                Vector3 p = origin + fwd * UnityEngine.Random.Range(1.0f, 5.5f) + right * UnityEngine.Random.Range(-2.2f, 2.2f); p.y = ground;
                StainAt(p, UnityEngine.Random.Range(0.1f, 0.3f), t, UnityEngine.Random.Range(0.18f, 0.55f), UnityEngine.Random.Range(2f, 3f));
            }
        }

        // ---------------------------------------------------------------- d. the contact burst (the body's real place)
        /// <param name="away">Flat direction away from the caster.</param>
        public void Burst(Vector3 at, Vector3 feet, Vector3 away, float scale, float t)
        {
            Touch(t);
            away.y = 0f; away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            var c = Camera.main; if (c != null) _cam = c.transform.position;
            float sc = Mathf.Max(0.3f, scale);
            Vol2FsB1.EmitV(_pool, new Vector3(feet.x, feet.y + 0.04f, feet.z) + away * 0.3f, Vector3.zero, UnityEngine.Random.Range(1.0f, 1.5f) * sc, -1f);      // r3: a ground pool (r2: an upright splat beside the body)
            Drops(at, away + Vector3.up * 0.35f, 0.85f, Mathf.RoundToInt(UnityEngine.Random.Range(110f, 200f) * sc), 3f, 10f, 0.035f * (0.6f + 0.4f * sc), 0.11f * (0.6f + 0.4f * sc));
            Sparks(at, away + Vector3.up * 0.2f, 0.9f, Mathf.RoundToInt(UnityEngine.Random.Range(20f, 40f) * sc), 5f, 12f);
            // two short X slashes through the body (sharp, drawn in 0.1 s, dried in 0.5 s)
            Vector3 view = _cam - at; view = view.sqrMagnitude > 1e-6f ? view.normalized : Vector3.back;
            Vector3 rt = Vector3.Cross(Vector3.up, view).normalized; Vector3 up = Vector3.Cross(view, rt);
            for (int k = 0; k < 2; k++)
            {
                Vector3 d = (rt * 1.5f + up * (k == 0 ? 0.8f : -0.8f)).normalized * UnityEngine.Random.Range(1.8f, 2.6f) * sc;
                Vector3 bow = Vector3.Cross(d.normalized, view) * d.magnitude * (k == 0 ? 0.16f : -0.16f);
                var pts = new Vector3[14];
                for (int j = 0; j < pts.Length; j++) { float u = j / (pts.Length - 1f); pts[j] = at - d * 0.5f + d * u + bow * 4f * u * (1f - u); }
                Add(new InkStroke { P = pts, W = 0.5f * sc, Row = 1 + k, T0 = t, Grow = 0.10f, Hold = 0.15f, Dry = 0.4f, Rnd = UnityEngine.Random.value, AccW = 0.16f, Pivot = at, HasPivot = true });
            }
            StainAt(new Vector3(feet.x, feet.y, feet.z), UnityEngine.Random.Range(0.8f, 1.3f) * sc, t);      // r2: 1.6 - 2.6 m across (r1: up to 4 m)
            Ash(at, Mathf.RoundToInt(UnityEngine.Random.Range(10f, 30f) * sc), 0.9f);
        }

        // ---------------------------------------------------------------- the per-frame draw
        static float Smooth(float a, float b, float x) { float k = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a)); return k * k * (3f - 2f * k); }
        static float EaseOut(float x) { x = Mathf.Clamp01(x); float k = 1f - x; return 1f - k * k * k; }

        static Vector3 At(Vector3[] p, float u)
        {
            u = Mathf.Clamp01(u) * (p.Length - 1);
            int i = Mathf.Min(p.Length - 2, Mathf.FloorToInt(u));
            return Vector3.Lerp(p[i], p[i + 1], u - i);
        }

        /// <summary>Every frame (t = the look's clock): draws the strokes and stains, flings the heads' spray.</summary>
        public void Tick(float t, Vector3 cam)
        {
            _cam = cam;
            float dt = float.IsNaN(_lastT) ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f); _lastT = t;
            _inkS.Clear(); _accS.Clear(); _stainS.Clear();
            for (int i = _strokes.Count - 1; i >= 0; i--)
            {
                var s = _strokes[i];
                if (s.Dead || s.P == null || s.P.Length < 2) { _strokes.RemoveAt(i); continue; }
                float a = t - s.T0;
                if (a < 0f) continue;
                float r = s.Grow <= 0.001f ? 1f : EaseOut(a / s.Grow);
                float dryAt = s.HoldUntil >= 0f ? s.HoldUntil : s.Live ? 1e9f : s.T0 + s.Grow + s.Hold;
                float dryK = Smooth(0f, 1f, (t - dryAt) / Mathf.Max(0.05f, s.Dry));
                if (dryK >= 0.999f) { _strokes.RemoveAt(i); continue; }
                DrawStroke(s, r, dryK, a, cam, dt);
            }
            for (int i = _stains.Count - 1; i >= 0; i--)
            {
                var st = _stains[i];
                float a = t - st.T0;
                if (a < 0f) continue;
                if (a > st.Life) { _stains.RemoveAt(i); continue; }
                float size = st.R * 2f * Mathf.Lerp(0.35f, 1f, EaseOut(a / st.Spread));
                float life = 1f - Smooth(st.Life * 0.45f, st.Life, a);
                float yaw = st.Yaw * Mathf.Deg2Rad;
                Vector3 rt = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), fw = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                _stainS.QuadCell(new Vector3(st.P.x, st.P.y + 0.025f, st.P.z), rt, fw, size, new Color(1f, 1f, 1f, life), st.Rnd, st.Cell);
            }
            _inkS.Write(_inkM); _accS.Write(_accM); _stainS.Write(_stainM);
        }

        void DrawStroke(InkStroke s, float r, float dryK, float age, Vector3 cam, float dt)
        {
            int n = s.P.Length;
            int m = Mathf.Clamp(Mathf.CeilToInt(r * (n - 1) * 1.5f) + 1, 3, 64);
            var pts = new Vector3[m]; var w = new float[m]; var al = new float[m]; var sd = s.Flat ? new Vector3[m] : null;
            Vector3 tgt = s.Absorb != null ? s.Absorb() : Vector3.zero;
            for (int k = 0; k < m; k++)
            {
                float u = k / (m - 1f) * r;
                Vector3 p = At(s.P, u);
                if (s.Absorb != null) p = Vector3.Lerp(p, tgt, dryK * dryK);
                pts[k] = p;
                float tip = Mathf.Sqrt(Mathf.Clamp01((r - u) / 0.06f + (r >= 0.999f ? 0f : 0f)));
                w[k] = s.W * tip * (s.Absorb != null ? 1f - 0.6f * dryK : 1f) * Mathf.Clamp01(((p - cam).magnitude - 1.2f) / 2f);      // a stroke never fills the screen at the camera
                if (s.TailThin > 0f) w[k] *= Mathf.Lerp(1f, 1f - s.TailThin, Mathf.Pow(u, 1.3f)) * Mathf.Lerp(0.12f, 1f, Smooth(0f, 0.14f, u));      // r2b: the brush enters thin (r2a: a square-cut black block at the head)
                if (s.HasWin) w[k] *= Mathf.Lerp(0.08f, 1f, Smooth(7f, 18f, Vector3.Angle(p - cam, s.WinAim - cam)));
                // dry tail -> head: the front of the erosion walks from the tail (u = 1) to the head
                float f = dryK * 1.25f;
                float e = Mathf.Clamp01((f - (1f - u)) / 0.25f);
                al[k] = 1f - 0.97f * e;
                if (sd != null) { Vector3 d = p - s.Pivot; d.y = 0f; sd[k] = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right; }
            }
            float v0 = (3 - Mathf.Clamp(s.Row, 0, 3)) * 0.25f + 0.012f, v1 = (4 - Mathf.Clamp(s.Row, 0, 3)) * 0.25f - 0.012f;
            _inkS.Strip(pts, w, sd, Color.white, al, s.Rnd, cam, 0f, r, v0, v1);
            // the accent: a thin glowing line on the inner leading edge, hot for a moment, then gone into the ink
            if (s.AccW > 0f)
            {
                float accLife = 1f - Smooth(s.Grow * 0.6f, s.Grow + 0.3f, age);
                if (accLife > 0.01f)
                {
                    var ap = new Vector3[m]; var aw = new float[m]; var aa = new float[m];
                    for (int k = 0; k < m; k++)
                    {
                        Vector3 tan = (pts[Mathf.Min(m - 1, k + 1)] - pts[Mathf.Max(0, k - 1)]).normalized;
                        Vector3 side = sd != null ? sd[k] : Vector3.Cross(tan, (cam - pts[k]).normalized).normalized;
                        if (sd == null && s.HasPivot && Vector3.Dot(side, s.Pivot - pts[k]) < 0f) side = -side;
                        float u = k / (m - 1f);
                        ap[k] = pts[k] + (sd != null ? -side : side) * s.W * 0.30f * (1f - 0.5f * u);
                        aw[k] = w[k] * s.AccW;
                        aa[k] = accLife * (1f - u * 0.7f) * al[k];
                    }
                    _accS.Strip(ap, aw, null, Color.white, aa, s.Rnd + 0.4f, cam, 0f, Mathf.Min(1f, r * 0.8f));
                }
            }
            // droplets off the growing head
            if (s.Spray && r < 0.999f && dt > 0f)
            {
                Vector3 head = At(s.P, r);
                Vector3 outward = s.HasPivot ? (head - s.Pivot) : Vector3.up; outward.y = Mathf.Max(0f, outward.y);
                Vector3 toCam = (cam - head).normalized; float back = Vector3.Dot(outward, toCam); if (back > 0f) outward -= toCam * back;      // r2b: never flung at the lens
                int cnt = Mathf.RoundToInt(dt * 320f * Mathf.Clamp(s.W, 0.3f, 1.2f));
                Drops(head, outward + Vector3.up * 0.4f, 0.7f, cnt, 3f, 8f, 0.05f, 0.18f);
                Touch(age + s.T0);
            }
        }

        // ---------------------------------------------------------------- a. the swing (a designed crescent round the caster; the brush tip's real path gives its direction)
        // r2 (2026-10-28): r1 scaled the tip's real path up round the caster - the overhead swing runs in the vertical plane through the camera, so from the shoulder it stood
        // as a black plank between the caster and the target in every cast. Now: a low diagonal crescent round the caster (radius 1.8 m, from beside the caster, through
        // the front, down to the other side), pressed fat at the head and whipping thin at the tail, broken to a dry hairline where it crosses the sight line to the target.
        InkStroke _swing;
        /// <summary>Lays the swing stroke once, just before the launch (call every frame; it does nothing after the first).</summary>
        public void SwingFeed(IList<(Vector3 P, float T)> path, float t, float launch, Vector3 pivot, Vector3 fwd, Vector3 right, float ground)
        {
            if (_swing != null || t < launch - 0.08f) return;
            // which way the brush came across: the tip's lateral run over the last quarter second (no run -> the seed decides)
            Vector3 a = Vector3.zero, b = Vector3.zero; bool any = false;
            foreach (var p in path) { if (p.T < launch - 0.3f) continue; if (!any) { a = p.P; any = true; } b = p.P; }
            float lat = any ? Vector3.Dot(b - a, right) : 0f;
            float sg = Mathf.Abs(lat) > 0.08f ? Mathf.Sign(lat) : ((_seed & 1) == 0 ? 1f : -1f);
            var o = new List<Vector3>();
            for (int i = 0; i < 26; i++)
            {
                float u = i / 25f;
                float ang = sg * Mathf.Lerp(-105f, 105f, u) * Mathf.Deg2Rad;
                float R = 1.8f + 0.25f * Mathf.Sin(u * Mathf.PI);
                Vector3 q = pivot + (fwd * Mathf.Cos(ang) + right * Mathf.Sin(ang)) * R;
                q.y = Mathf.Max(ground + 0.15f, pivot.y + Mathf.Lerp(0.30f, -0.45f, u));
                if ((q - _cam).magnitude > 2.2f) o.Add(q);              // never a stroke through the camera
            }
            if (o.Count < 4) return;
            // r3b: lying flat round the caster (a sword arc seen from the shoulder; r3: a camera-facing ribbon's fat head stood beside the target as a black flag)
            _swing = Add(new InkStroke { P = o.ToArray(), W = 0.65f, Row = 0, T0 = t, Grow = 0.16f, Hold = 0.12f, Dry = 0.55f, Rnd = 0.37f, AccW = 0.14f, Pivot = pivot, HasPivot = true, Flat = true,
                Spray = true, TailThin = 0.85f, HasWin = true, WinAim = pivot + fwd * 4.5f + Vector3.down * 0.3f });
            Touch(t + 1f);
        }

        // ---------------------------------------------------------------- c. the frame's big gesture
        InkStroke _flankL, _flankR; bool _flankDone;
        /// <summary>The ㅏ path flanks: two long strokes either side of the path, drawn along with the head (call every frame from the launch).</summary>
        public void PathFlanks(float t, float launch, Vector3 o, Vector3 f, Vector3 r, float ground, float reach, bool finished, int seed)
        {
            if (_flankDone) return;
            if (_flankL == null)
            {
                _flankL = Add(new InkStroke { W = 0.6f, TailThin = 0.6f, Row = 1, T0 = launch, Grow = 0f, Live = true, Dry = 0.7f, Rnd = 0.21f, AccW = 0.13f });
                _flankR = Add(new InkStroke { W = 0.6f, TailThin = 0.6f, Row = 1, T0 = launch, Grow = 0f, Live = true, Dry = 0.7f, Rnd = 0.63f, AccW = 0.13f });
            }
            reach = Mathf.Max(reach, 2.2f);
            for (int side = 0; side < 2; side++)
            {
                float sg = side == 0 ? 1f : -1f;
                var p = new Vector3[22];
                for (int i = 0; i < p.Length; i++)
                {
                    float s = Mathf.Lerp(0.9f, reach, i / (p.Length - 1f));
                    float lat = Mathf.Min(2.0f, 0.8f + 0.10f * s) * (1f + 0.15f * Mathf.Sin(s * 0.7f + side * 2f));
                    float h = 0.7f + 0.5f * Mathf.Sin(s * 0.42f + side * 1.7f + seed * 0.01f);
                    p[i] = o + f * s + r * sg * lat; p[i].y = ground + h;
                }
                var st = side == 0 ? _flankL : _flankR;
                st.P = p; st.Pivot = o + Vector3.up * 1.1f; st.HasPivot = true;
            }
            Touch(t);
            if (finished) { Freeze(_flankL, t, 1.0f); Freeze(_flankR, t, 1.0f); _flankDone = true; }
        }

        /// <summary>The ㅗ fan: one wide arc across the fan (radius 0.65 of the depth, 0.6 - 1.2 m high), drawn left -> right over the sweep, flinging ink outward.</summary>
        public void FanArc(float t, Vector3 o, Vector3 f, Vector3 r, float ground, float depth, float halfAngle)
        {
            float R = depth * 0.62f; halfAngle = Mathf.Max(halfAngle * 2.4f, 55f * Mathf.Deg2Rad); var p = new Vector3[36];
            for (int i = 0; i < p.Length; i++)
            {
                float u = i / (p.Length - 1f), a = Mathf.Lerp(-halfAngle * 0.95f, halfAngle * 0.95f, u);
                p[i] = o + (f * Mathf.Cos(a) + r * Mathf.Sin(a)) * R; p[i].y = ground + 0.6f + 0.55f * Mathf.Sin(u * Mathf.PI);
            }
            Add(new InkStroke { P = p, W = 0.6f, TailThin = 0.65f, Row = 3, T0 = t, Grow = 0.30f, Hold = 0.12f, Dry = 0.45f, Rnd = 0.41f, AccW = 0.12f, Pivot = o + Vector3.up * 0.9f, HasPivot = true, Spray = true,
                HasWin = true, WinAim = o + f * depth * 0.8f + Vector3.up * 0.8f });      // r2: the arc crosses the fan in front of the targets - it breaks there
            Touch(t + 1.5f);
        }

        /// <summary>The ㅜ ring: an open brush ring (enso) round the caster at the ring's radius, ground-flat, drawn in 0.25 s with ink flung outward.</summary>
        public void RingEnso(float t, Vector3 o, float ground, float radius, int seed)
        {
            float th0 = (seed % 628) / 100f; var p = new Vector3[40];
            for (int i = 0; i < p.Length; i++)
            {
                float a = th0 + i / (p.Length - 1f) * Mathf.PI * 2f * 0.92f;
                p[i] = new Vector3(o.x + Mathf.Cos(a) * radius, ground + 0.12f + 0.03f * Mathf.Sin(a * 3f), o.z + Mathf.Sin(a) * radius);
            }
            Add(new InkStroke { P = p, W = 0.6f, TailThin = 0.6f, Row = 2, T0 = t, Grow = 0.25f, Hold = 0.8f, Dry = 0.9f, Rnd = 0.53f, AccW = 0.12f, Pivot = new Vector3(o.x, ground, o.z), HasPivot = true, Flat = true, Spray = true });
            Touch(t + 2f);
        }

        /// <summary>The ㅓ attachment: an ink spiral wound twice up the caster, then swallowed by the brush.</summary>
        public void Spiral(float t, Vector3 o, float ground, Func<Vector3> brush)
        {
            var p = new Vector3[34];
            for (int i = 0; i < p.Length; i++)
            {
                float u = i / (p.Length - 1f), a = u * Mathf.PI * 4f;
                float rad = Mathf.Lerp(0.65f, 0.28f, u);
                p[i] = new Vector3(o.x + Mathf.Cos(a) * rad, ground + 0.25f + u * 1.7f, o.z + Mathf.Sin(a) * rad);
            }
            Add(new InkStroke { P = p, W = 0.5f, Row = 1, T0 = t, Grow = 0.5f, Hold = 0.05f, Dry = 0.55f, Rnd = 0.29f, AccW = 0.14f, Pivot = o + Vector3.up * 1.0f, HasPivot = true, Absorb = brush });
            Touch(t + 1f);
        }
    }
}
