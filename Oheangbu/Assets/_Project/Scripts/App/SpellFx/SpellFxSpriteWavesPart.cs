using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153c (2026-10-29, variants R = Recraft sprites / I = GPT-image sprites): the water ring 우 as MANY painted wave pieces. Each piece is a 2-D sprite (mound / crest / claw /
    /// splash / ink stroke, RGBA, made outside the engine) stood up on a card round the caster: 2 - 3 concentric rows of mound cards (the back row smaller and farther out, the front row bigger
    /// and nearer the centre, neighbours overlapping ~35 %), 5 - 7 tall crest cards rising out of the rows that tip and hook forward as they rise, a foam claw on every crest tip, and 2 - 3 black
    /// brush strokes bent along the ring as curved ribbons. The camera-side arc (|theta| -> pi) stays low so the caster is seen. Where a contact lands, 2 big crest cards rear up behind the
    /// target, fold over it, and collapse onto it while a few claws / splashes fly. Everything is rebuilt every tick from the scope-clock age (no coroutine, no Time, no Physics); the meshes are
    /// laid in WORLD space under children at the world origin, one mesh + one material per texture. The sprite colours are shown as painted (shader Oheangbu/SpellFx/Sprite). Look only. (r1)</summary>
    public sealed class SpellFxSpriteWavesPart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Shader spriteShader;
        [SerializeField] Texture2D moundTex, crestTex, clawTex, inkTex, splashTex;
        [SerializeField] float radius = 2.2f;
        [SerializeField] float lifeSeconds = 1.8f;
        [Tooltip("Which way the hook of crest.png points when drawn unflipped: +1 = toward the image's right, -1 = left (the setup measures it from the alpha).")]
        [SerializeField] float crestHookSign = 1f;

        // ---- timeline (seconds on the 1.8 s base timeline; lifeSeconds stretches all of it) ----
        const float BaseLife = 1.8f;
        const float Ground = 0.12f;                                   // the cards stand this far below the cast point (uneven ground)
        const float MoundRise = 0.30f;                                // one mound card rises over this long (ease-back), staggered round the ring: all up by ~0.45 s
        const float CrestRise = 0.34f;
        const float CurlA = 0.05f, CurlB = 0.42f;                     // after its own start a crest tips / hooks between these moments
        const float ErodeA = 1.05f, ErodeB = 1.75f;                   // everything dissolves between these moments (per-card stagger +-0.12)
        const float SinkA = 1.10f, SinkB = 1.65f;                     // and sinks back into the ground
        const float InkFadeA = 1.0f, InkFadeB = 1.55f;

        // ---- mound rows: radius offset from the ring, card height, half arc (rad, from the aim, both ways), drift (rad/s), start delay, dip where a hit lands ----
        struct RowDef { public float DR, H, HalfArc, Drift, Delay, Dip; public RowDef(float dr, float h, float arc, float drift, float delay, float dip) { DR = dr; H = h; HalfArc = arc; Drift = drift; Delay = delay; Dip = dip; } }
        static readonly RowDef[] Rows =
        {
            new RowDef(+0.40f, 0.95f, 3.00f, -0.05f, 0.00f, 0.15f),   // back row: smaller, outermost, nearly all round (low where it is the camera side)
            new RowDef(+0.10f, 1.30f, 2.70f, +0.07f, 0.04f, 0.45f),   // middle row
            new RowDef(-0.20f, 1.65f, 2.25f, +0.10f, 0.08f, 0.70f),   // front row: bigger, nearer the centre
        };
        const float Overlap = 0.65f;                                  // card step = this x card width (35 % overlap)
        const float MaxCardW = 4.5f;
        const float MaxChord = 0.55f;                                 // a flat mound card is at most this x its row radius wide (its ends then stay within ~4 % of the ring); a wider sprite is shown as a random slice
        const float BackLow = 0.88f;                                  // height factor = 1 - this x Back(theta): the camera-side arc stays low
        const float DipHalf = 0.38f;                                  // rad: the cards this close to a hit sink to make room for the bear
        const float BobAmp = 0.04f;

        // ---- crests ----
        const int CrestCount = 6;
        const float CrestArc = 2.3f, CrestDR = 0.15f;                 // spread over theta = -CrestArc .. +CrestArc at ring + CrestDR
        const float CrestHMin = 1.8f, CrestHMax = 2.4f;
        const float CrestPhiMinDeg = 55f, CrestPhiMaxDeg = 80f;       // inward fold of the top (~ g^2)
        const float CrestBendMin = 0.22f, CrestBendMax = 0.38f;       // sideways hook of the top, x card height
        const int CrestRows = 8;
        const float ClawSize = 0.5f;

        // ---- hit ----
        const int MaxHits = 2, SplashCount = 7;
        const float HitHMin = 1.8f, HitHMax = 2.4f;
        const float HitRise = 0.26f;                                  // rears up (ease-back)
        const float HitFoldA = 0.22f, HitFoldB = 0.52f;               // folds over AFTER the rear-up
        const float HitCollA = 0.65f, HitCollB = 1.08f;               // collapses onto the target
        const float HitErA = 0.85f, HitErB = 1.30f;                   // its own dissolve
        const float HitLife = 1.4f;
        const float HitPhiDeg = 110f, HitPhiCollDeg = 25f, HitBend = 0.28f;
        const float HitBehind = 0.40f;                                // the wave stands this far behind the target (away from the caster) and folds inward over it
        const float Gravity = 7.5f;

        // ---- ink ribbons ----
        const int InkCount = 3, InkSegs = 28;
        const float InkDR = 0.12f;                                    // ribbon radius = ring + this (between the middle and back rows)
        const float InkBackLimit = 0.6f;

        static readonly int TexId = Shader.PropertyToID("_MainTex"), CutoffId = Shader.PropertyToID("_Cutoff"), SoftId = Shader.PropertyToID("_SoftW"), ZWriteId = Shader.PropertyToID("_ZWrite");

        // ---- state ----
        Vector3 _o, _f, _r;
        float _spin = 1f, _R = 2.2f, _tScale = 1f;
        float _aMound = 2f, _aCrest = 0.8f, _aClaw = 1f, _aSplash = 1f, _aInk = 4f;
        bool _dead;
        readonly List<Batch> _batches = new List<Batch>();
        readonly Dictionary<Texture2D, Batch> _byTex = new Dictionary<Texture2D, Batch>();
        Batch _bMound, _bCrest, _bClaw, _bSplash, _bInk;
        Mound[] _mounds;
        Crest[] _crests;
        Rib[] _ribs;
        readonly List<Hit> _hits = new List<Hit>();

        struct Mound { public int Row; public float Th0, W, H, Seed, Delay, U0, UW; public bool Flip; }
        struct Crest { public float Th0, W, H, Seed, Delay, Phi, Bend; public bool Flip; }
        struct Rib { public float Th0, Span, Width, At, Y0, Lift; }
        struct Splash { public Vector3 Vel; public float Size, Spin, Seed, Delay; public bool UseClaw; }
        sealed class Hit { public float T0, ThH, Dist, Seed, H; public Vector3 Centre; public Splash[] Sp; }

        /// <summary>One mesh + one material for all the cards that share a texture. Vertex colour = (local erosion, seed, 0, fade).</summary>
        sealed class Batch
        {
            public Mesh Mesh; public Material Mat;
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public void Clear() { V.Clear(); UV.Clear(); C.Clear(); T.Clear(); }
            public void Apply()
            {
                Mesh.Clear();
                if (V.Count == 0) return;
                Mesh.SetVertices(V); Mesh.SetUVs(0, UV); Mesh.SetColors(C); Mesh.SetTriangles(T, 0); Mesh.RecalculateBounds();
            }
        }

        // ---------------------------------------------------------------- small maths
        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float EaseBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        static float Wrap(float a) => Mathf.Repeat(a + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
        static float Back(float th) => Sm(0.55f, 1f, Mathf.Abs(th) / Mathf.PI);
        Vector3 Dir(float th) => _f * Mathf.Cos(th) + _r * Mathf.Sin(th);
        Vector3 Tan(float th) => -_f * Mathf.Sin(th) + _r * Mathf.Cos(th);
        static float Asp(Texture2D t, float fallback) => t != null && t.height > 0 ? Mathf.Clamp(t.width / (float)t.height, 0.35f, 4f) : fallback;

        // ---------------------------------------------------------------- ISpellFxPart
        public void OnBegin(SpellFxCast cast)
        {
            _o = cast.Origin; _f = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            _spin = (cast.CastId & 1) == 0 ? 1f : -1f;
            _R = cast.Radius > 0.5f ? cast.Radius : radius;
            _tScale = BaseLife / Mathf.Max(0.3f, lifeSeconds);
            var sh = spriteShader != null ? spriteShader : Shader.Find("Oheangbu/SpellFx/Sprite");
            if (sh == null) { Debug.LogWarning("SpellFxSpriteWavesPart: shader Oheangbu/SpellFx/Sprite not found; nothing is drawn."); _dead = true; return; }

            // fall back gracefully: a missing sprite is replaced by its nearest relative
            Texture2D mound = moundTex != null ? moundTex : crestTex, crest = crestTex != null ? crestTex : moundTex;
            Texture2D claw = clawTex != null ? clawTex : crest, splash = splashTex != null ? splashTex : claw;
            if (mound == null && crest == null) { Debug.LogWarning("SpellFxSpriteWavesPart: neither mound nor crest sprite is set; nothing is drawn."); _dead = true; return; }
            _aMound = Asp(mound, 2f); _aCrest = Asp(crest, 0.8f); _aClaw = Asp(claw, 1f); _aSplash = Asp(splash, 1f); _aInk = Asp(inkTex, 4f);

            _bMound = GetBatch("Mound", mound, 2997, 0.5f, 0.3f, true);
            _bCrest = GetBatch("Crest", crest, 2998, 0.5f, 0.3f, true);
            _bClaw = GetBatch("Claw", claw, 2998, 0.5f, 0.3f, true);
            _bSplash = GetBatch("Splash", splash, 2998, 0.5f, 0.3f, true);
            _bInk = inkTex != null ? GetBatch("Ink", inkTex, 3000, 0.25f, 0.5f, false) : null;       // drawn last, tested against the cards' depth, writes none

            var rnd = new System.Random((int)(cast.CastId * 7919 + 31));
            float Rnd() => (float)rnd.NextDouble();

            // ---- mound rows ----
            var list = new List<Mound>();
            for (int ri = 0; ri < Rows.Length; ri++)
            {
                var row = Rows[ri];
                float rr = Mathf.Max(0.8f, _R + row.DR), full = row.H * _aMound;                  // full = the sprite at its natural width for this row height
                float w0 = Mathf.Min(Mathf.Min(MaxCardW, MaxChord * rr), full), frac = full > 1e-4f ? w0 / full : 1f;       // a flat card must not outgrow its circle: wide sprites are cut into slices
                int n = Mathf.Max(3, Mathf.CeilToInt(2f * row.HalfArc * rr / (Overlap * w0)));
                float step = 2f * row.HalfArc / n;
                for (int i = 0; i < n; i++)
                {
                    float s = Mathf.Lerp(0.88f, 1.12f, Rnd());
                    list.Add(new Mound
                    {
                        U0 = Rnd() * (1f - frac), UW = frac, Row = ri, Th0 = -row.HalfArc + (i + 0.5f) * step + (Rnd() - 0.5f) * step * 0.25f, W = w0 * s, H = row.H * s * Mathf.Lerp(0.92f, 1.08f, Rnd()),
                        Seed = Rnd(), Flip = Rnd() < 0.5f, Delay = row.Delay + 0.10f * Mathf.Abs(i - (n - 1) * 0.5f) / Mathf.Max(1f, n * 0.5f) + 0.03f * Rnd(),
                    });
                }
            }
            _mounds = list.ToArray();

            // ---- crests: the hook goes the way the cast spins; the card is flipped when the sprite's own hook points the other way ----
            bool crestFlip = crestHookSign * _spin < 0f;
            _crests = new Crest[CrestCount];
            for (int i = 0; i < CrestCount; i++)
            {
                float h = Mathf.Lerp(CrestHMin, CrestHMax, Rnd());
                _crests[i] = new Crest
                {
                    Th0 = Mathf.Lerp(-CrestArc, CrestArc, (i + 0.5f) / CrestCount) + (Rnd() - 0.5f) * 0.18f, H = h, W = Mathf.Min(MaxCardW, h * _aCrest),
                    Delay = 0.10f + 0.22f * Rnd(), Seed = Rnd(), Flip = crestFlip,
                    Phi = Mathf.Lerp(CrestPhiMinDeg, CrestPhiMaxDeg, Rnd()) * Mathf.Deg2Rad, Bend = Mathf.Lerp(CrestBendMin, CrestBendMax, Rnd()),
                };
            }

            // ---- ink strokes ----
            _ribs = new Rib[InkCount];
            float[] starts = { -1.9f, -0.1f, 1.5f };
            for (int i = 0; i < InkCount; i++)
            {
                float span = Mathf.Lerp(100f, 140f, Rnd()) * Mathf.Deg2Rad;
                float arcLen = span * (_R + InkDR);
                _ribs[i] = new Rib
                {
                    Th0 = starts[i % starts.Length] + (Rnd() - 0.5f) * 0.3f, Span = span, Width = Mathf.Clamp(arcLen / _aInk * 0.9f, 0.3f, 1.1f),
                    At = 0.12f + 0.05f * i, Y0 = Mathf.Lerp(0.55f, 0.95f, Rnd()), Lift = Mathf.Lerp(0.4f, 0.8f, Rnd()),
                };
            }
            VfxAfterFog308.Route(transform);          // every card is in the transparent range: after the fog, depth-tested against the world
        }

        public void OnTick(SpellFxCast cast, float age)
        {
            if (_dead || _bMound == null) return;
            float t = age * _tScale;
            foreach (var b in _batches) b.Clear();
            if (t < BaseLife - 0.02f) { BuildMounds(t); BuildCrests(t); BuildInk(t); }
            BuildHits(t);
            foreach (var b in _batches) b.Apply();
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            if (_dead || _bMound == null || _hits.Count >= MaxHits) return;
            Vector3 feet = hit.Feet.sqrMagnitude > 1e-6f ? hit.Feet : new Vector3(hit.Position.x, _o.y, hit.Position.z);
            Vector3 rel = feet - _o; rel.y = 0f;
            float thH = Mathf.Atan2(Vector3.Dot(rel, _r), Vector3.Dot(rel, _f));            // Dir(th) = f cos + r sin
            var rnd = new System.Random((int)(cast.CastId * 104729 + 17 * (_hits.Count + 1)));
            float Rnd() => (float)rnd.NextDouble();
            var h = new Hit
            {
                T0 = Mathf.Max(0f, cast.AgeAt(hit.NowUs) * _tScale), ThH = thH, Dist = Mathf.Max(1.2f, rel.magnitude), Seed = Rnd(), H = Mathf.Lerp(HitHMin, HitHMax, Rnd()),
                Centre = new Vector3(feet.x, feet.y + 0.8f, feet.z), Sp = new Splash[SplashCount],
            };
            Vector3 outward = rel.sqrMagnitude > 1e-6f ? rel.normalized : _f;
            for (int i = 0; i < SplashCount; i++)
            {
                Vector3 side = Vector3.Cross(Vector3.up, outward);
                h.Sp[i] = new Splash
                {
                    Vel = (outward * Mathf.Lerp(-1.2f, 1.6f, Rnd()) + side * Mathf.Lerp(-1.8f, 1.8f, Rnd())) + Vector3.up * Mathf.Lerp(2.6f, 4.4f, Rnd()),
                    Size = Mathf.Lerp(0.26f, 0.46f, Rnd()), Spin = Mathf.Lerp(-6f, 6f, Rnd()), Seed = Rnd(), Delay = 0.12f * Rnd(), UseClaw = (i & 1) == 0,
                };
            }
            _hits.Add(h);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;        // Done / TimedOut: the 1.8 s animation plays out through the player's tail
            _dead = true;
            foreach (var b in _batches) if (b.Mesh != null) b.Mesh.Clear();
        }

        // ---------------------------------------------------------------- scaffolding
        Batch GetBatch(string name, Texture2D tex, int queue, float cutoff, float soft, bool zwrite)
        {
            if (tex == null) return null;
            if (_byTex.TryGetValue(tex, out var have)) return have;
            var sh = spriteShader != null ? spriteShader : Shader.Find("Oheangbu/SpellFx/Sprite");
            var mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            mat.SetTexture(TexId, tex); mat.SetFloat(CutoffId, cutoff); mat.SetFloat(SoftId, soft); mat.SetFloat(ZWriteId, zwrite ? 1f : 0f);
            mat.renderQueue = queue; mat.SetOverrideTag("RenderType", "Transparent");
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave }; mesh.MarkDynamic(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var b = new Batch { Mesh = mesh, Mat = mat };
            _byTex[tex] = b; _batches.Add(b);
            return b;
        }

        /// <summary>A standing strip of <paramref name="rows"/> rows from baseP: it climbs by height h, turning inward (nin) by phiTop x g^pow, and its top is shifted sideways by bend (x g^2): the card tips and hooks.
        /// The width runs along tang; the texture's u goes along tang (flip = mirrored), v = the row fraction. Returns the top centre and the climb direction there.</summary>
        static void Strip(Batch b, Vector3 baseP, Vector3 tang, Vector3 nin, float w, float h, int rows, float phiTop, float pow, float bend, bool flip, float uA, float uB, float alpha, float er, float seed, out Vector3 topP, out Vector3 topDir)
        {
            int k0 = b.V.Count;
            Vector3 p = baseP; float seg = h / rows; topDir = Vector3.up;
            var col = new Color(er, seed, 0f, alpha);
            float u0 = flip ? uB : uA, u1 = flip ? uA : uB;
            for (int j = 0; j <= rows; j++)
            {
                float g = j / (float)rows;
                if (j > 0)
                {
                    float ph = phiTop * Mathf.Pow((j - 0.5f) / rows, pow);
                    topDir = Vector3.up * Mathf.Cos(ph) + nin * Mathf.Sin(ph);
                    p += topDir * seg;
                }
                Vector3 c = p + tang * (bend * g * g);
                b.V.Add(c - tang * (w * 0.5f)); b.V.Add(c + tang * (w * 0.5f));
                b.UV.Add(new Vector2(u0, g)); b.UV.Add(new Vector2(u1, g));
                b.C.Add(col); b.C.Add(col);
            }
            topP = p + tang * bend;
            for (int j = 0; j < rows; j++)
            {
                int i = k0 + 2 * j;
                b.T.Add(i); b.T.Add(i + 2); b.T.Add(i + 1); b.T.Add(i + 1); b.T.Add(i + 2); b.T.Add(i + 3);
            }
        }

        static void Quad(Batch b, Vector3 c, Vector3 right, Vector3 up, float w, float h, bool flip, float alpha, float er, float seed)
        {
            int k = b.V.Count;
            Vector3 rx = right * (w * 0.5f), uy = up * (h * 0.5f);
            b.V.Add(c - rx - uy); b.V.Add(c + rx - uy); b.V.Add(c - rx + uy); b.V.Add(c + rx + uy);
            float u0 = flip ? 1f : 0f, u1 = flip ? 0f : 1f;
            b.UV.Add(new Vector2(u0, 0f)); b.UV.Add(new Vector2(u1, 0f)); b.UV.Add(new Vector2(u0, 1f)); b.UV.Add(new Vector2(u1, 1f));
            var col = new Color(er, seed, 0f, alpha);
            b.C.Add(col); b.C.Add(col); b.C.Add(col); b.C.Add(col);
            b.T.Add(k); b.T.Add(k + 2); b.T.Add(k + 1); b.T.Add(k + 1); b.T.Add(k + 2); b.T.Add(k + 3);
        }

        // ---------------------------------------------------------------- the mound rows
        void BuildMounds(float t)
        {
            if (_bMound == null) return;
            foreach (var c in _mounds)
            {
                var row = Rows[c.Row];
                float a = (t - c.Delay) / MoundRise;
                if (a <= 0f) continue;
                float er = Sm(ErodeA, ErodeB, t + c.Seed * 0.12f);
                if (er > 0.995f) continue;
                float th = Wrap(c.Th0 + _spin * row.Drift * t);
                float hf = 1f - BackLow * Back(th);
                float dip = 1f;
                foreach (var h in _hits)
                {
                    float ha = t - h.T0;
                    if (ha < 0f || ha > HitLife) continue;
                    float m = Sm(0f, 1f, 1f - Mathf.Abs(Wrap(th - h.ThH)) / DipHalf);
                    dip *= 1f - row.Dip * m * Sm(0f, 0.15f, ha) * (1f - Sm(1.0f, 1.4f, ha));
                }
                float hh = c.H * hf * dip;
                if (hh < 0.03f) continue;
                float rise = EaseBack(a), sink = Sm(SinkA, SinkB, t + c.Seed * 0.15f);
                float yoff = -(1f - rise) * hh - sink * hh * 0.85f + BobAmp * Mathf.Sin(t * 4f + c.Seed * 6.283f) * Mathf.Clamp01(rise) * (1f - sink);
                Vector3 p = _o + Dir(th) * Mathf.Max(0.8f, _R + row.DR); p.y = _o.y - Ground + yoff;
                float lean = 0.10f + 0.05f * Mathf.Sin(t * 3f + c.Seed * 6.283f);
                Strip(_bMound, p, Tan(th), -Dir(th), c.W, hh, 3, lean, 1f, 0f, c.Flip, c.U0, c.U0 + c.UW, 1f, er, c.Seed, out _, out _);
            }
        }

        // ---------------------------------------------------------------- the crests (and the claws on their tips)
        void BuildCrests(float t)
        {
            if (_bCrest == null) return;
            foreach (var c in _crests)
            {
                float ct = t - c.Delay;
                if (ct <= 0f) continue;
                float er = Sm(ErodeA, ErodeB, t + c.Seed * 0.12f);
                if (er > 0.995f) continue;
                float th = Wrap(c.Th0 + _spin * 0.08f * t);
                float hf = 1f - 0.55f * Back(th);
                float hh = c.H * hf;
                float rise = EaseBack(ct / CrestRise), sink = Sm(SinkA, SinkB, t + c.Seed * 0.15f);
                float curl = Sm(CurlA, CurlB, ct);
                float yoff = -(1f - rise) * hh - sink * hh * 0.9f;
                Vector3 tang = Tan(th), nin = -Dir(th);
                Vector3 p = _o + Dir(th) * (_R + CrestDR); p.y = _o.y - Ground + yoff;
                Strip(_bCrest, p, tang, nin, c.W, hh, CrestRows, c.Phi * curl, 2f, _spin * c.Bend * hh * curl, c.Flip, 0f, 1f, 1f, er, c.Seed, out Vector3 top, out Vector3 topDir);
                AddClaw(t, ct, c.Seed, c.Flip, er, top, topDir, tang, nin, ClawSize * Mathf.Lerp(0.85f, 1.15f, c.Seed));
            }
        }

        /// <summary>A foam claw sitting on a crest tip: it grows once the crest begins to hook, then rocks a little.</summary>
        void AddClaw(float t, float ct, float seed, bool flip, float er, Vector3 top, Vector3 topDir, Vector3 tang, Vector3 nin, float size)
        {
            if (_bClaw == null) return;
            float grow = Sm(0.18f, 0.5f, ct);
            if (grow < 0.02f) return;
            float roll = 0.35f * Mathf.Sin(t * 5f + seed * 6.283f) + _spin * 0.3f;
            Vector3 right = tang * Mathf.Cos(roll) + topDir * Mathf.Sin(roll), up = topDir * Mathf.Cos(roll) - tang * Mathf.Sin(roll);
            float w = size * grow * Mathf.Min(1.6f, _aClaw), h = size * grow / Mathf.Max(0.6f, _aClaw) * Mathf.Min(1.6f, _aClaw);
            Vector3 c = top + tang * (_spin * 0.22f * size) + topDir * (0.12f * size) + nin * (0.05f * size);
            Quad(_bClaw, c, right, up, w, h, flip, 1f, er, seed);
        }

        // ---------------------------------------------------------------- the ink strokes: curved ribbons along the ring
        void BuildInk(float t)
        {
            if (_bInk == null) return;
            float fade = 1f - Sm(InkFadeA, InkFadeB, t);
            if (fade < 0.01f) return;
            for (int ri = 0; ri < _ribs.Length; ri++)
            {
                var rib = _ribs[ri];
                float g = Sm(rib.At, rib.At + 0.40f, t);
                if (g < 0.02f) continue;
                float drift = _spin * 0.35f * Mathf.Max(0f, t - rib.At);
                float sinkY = 0.35f * Sm(InkFadeA, InkFadeB + 0.2f, t);
                int prev = -1;
                for (int j = 0; j < InkSegs; j++)
                {
                    float s = j / (InkSegs - 1f);                      // 0 = tail, 1 = the growing head
                    float th = Wrap(rib.Th0 + drift + _spin * rib.Span * g * s);
                    if (Back(th) > InkBackLimit) { prev = -1; continue; }
                    float rr = _R + InkDR + 0.2f * s * g;
                    float y = rib.Y0 + rib.Lift * s * s * g - sinkY;
                    Vector3 c = _o + Dir(th) * rr; c.y = _o.y + y;
                    float wd = rib.Width * 0.5f;
                    float a = fade * Sm(0f, 0.08f, s);
                    int k0 = _bInk.V.Count;
                    _bInk.V.Add(c - Vector3.up * wd); _bInk.V.Add(c + Vector3.up * wd);
                    _bInk.UV.Add(new Vector2(1f - s, 0f)); _bInk.UV.Add(new Vector2(1f - s, 1f));      // the wet head of the stroke (u = 0 of the texture) leads
                    var col = new Color(Sm(InkFadeA, InkFadeB, t) * 0.6f, ri * 0.31f, 0f, a);
                    _bInk.C.Add(col); _bInk.C.Add(col);
                    if (prev >= 0) { var T = _bInk.T; T.Add(prev); T.Add(k0); T.Add(prev + 1); T.Add(prev + 1); T.Add(k0); T.Add(k0 + 1); }
                    prev = k0;
                }
            }
        }

        // ---------------------------------------------------------------- the hit: two big crests rear up behind the target, fold over it and collapse; claws and splashes fly
        void BuildHits(float t)
        {
            if (_bCrest == null) return;
            foreach (var h in _hits)
            {
                float a = t - h.T0;
                if (a < 0f || a > HitLife) continue;
                bool flip = crestHookSign * _spin < 0f;
                for (int k = 0; k < 2; k++)
                {
                    float ak = a - 0.07f * k;
                    if (ak < 0f) continue;
                    float hh0 = h.H * (k == 0 ? 1f : 0.74f);
                    float rise = EaseBack(ak / HitRise), fold = Sm(HitFoldA, HitFoldB, ak), col = Sm(HitCollA, HitCollB, ak), er = Sm(HitErA, HitErB, ak);
                    if (er > 0.995f) continue;
                    float hh = hh0 * (1f - 0.35f * col);
                    float th = h.ThH + (k == 0 ? 0f : (h.Seed > 0.5f ? 0.28f : -0.28f));
                    Vector3 tang = Tan(th), nin = -Dir(th);
                    Vector3 p = _o + Dir(th) * (h.Dist + HitBehind - 0.15f * k) + nin * (0.35f * col);
                    p.y = _o.y - Ground - (1f - rise) * hh0 - col * hh0 * 0.7f;
                    float w = Mathf.Min(MaxCardW, hh0 * _aCrest) * (k == 0 ? 1.05f : 0.9f);
                    float phi = HitPhiDeg * Mathf.Deg2Rad * fold + HitPhiCollDeg * Mathf.Deg2Rad * col;
                    Strip(_bCrest, p, tang, nin, w, hh, CrestRows + 2, phi, 1.8f, _spin * HitBend * hh * fold, flip, 0f, 1f, 1f, er, h.Seed + 0.31f * k, out Vector3 top, out Vector3 topDir);
                    if (k == 0 && fold > 0.05f) AddClaw(t, ak + 0.3f, h.Seed, flip, er, top, topDir, tang, nin, ClawSize * 1.6f);
                }
                // flying claws / splashes: p = p0 + v b - g b^2 / 2 (no Physics)
                Vector3 tg = Tan(h.ThH);
                for (int i = 0; i < h.Sp.Length; i++)
                {
                    var sp = h.Sp[i];
                    float b = a - (HitCollA - 0.05f) - sp.Delay;
                    if (b < 0f || b > 0.9f) continue;
                    var bt = sp.UseClaw ? _bClaw : _bSplash;
                    if (bt == null) continue;
                    Vector3 pos = h.Centre + sp.Vel * b - Vector3.up * (0.5f * Gravity * b * b);
                    float ang = sp.Spin * b, size = sp.Size * Sm(0f, 0.08f, b) * (1f - 0.3f * b);
                    Vector3 right = tg * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang), up = Vector3.up * Mathf.Cos(ang) - tg * Mathf.Sin(ang);
                    float asp = Mathf.Min(1.6f, sp.UseClaw ? _aClaw : _aSplash);
                    Quad(bt, pos, right, up, size * asp, size / asp, (i & 2) != 0, 1f, Sm(0.55f, 0.9f, b), sp.Seed);
                }
            }
        }

        void OnDestroy()
        {
            foreach (var b in _batches)
            {
                if (b.Mesh != null) Destroy(b.Mesh);
                if (b.Mat != null) Destroy(b.Mat);
            }
            _batches.Clear(); _byTex.Clear();
        }
    }
}
