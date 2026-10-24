using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // D301 앞잡(글자 난사): Q pressed and released with no stroke, with the locked target's weak point open and in range, fires a volley
    // of brush-script glyphs. D308-147 (plan C): the press itself fires nothing. The drawing controller enters its draw mode as usual
    // and asks this class (IDrawPressArbiter) whether the press is a riposte candidate; a candidate holds back the draw presentation
    // until the first stroke (then the gesture is a drawing and this window's riposte is gone), and a release without a stroke comes
    // back here after the mode is left quietly: the candidate is checked again (same lock, same life, same opening, in reach, able
    // to act) and fires once. A window is answered once: by this volley or by a drawing, named by the enemy + its life + the opening
    // of its weak point (RiposteWindowClaim308). Recognition is untouched: nothing here reads a stroke.
    // #302 presentation (SPEC-PLAYER-FEEL-300 F): magic circles open in an arc in front of and above the caster, each with
    // an initial at its centre; the glyphs pour out of them and weave to the target like 도깨비불, leaving faint
    // afterimages; every hit splashes ink and shakes the view, the last one holds the frame for a moment.
    // D308-147: with the sword out the Q press only puts the sword away (the wiring's ordinary release) - the volley is not
    // arbitrated there any more, and no aim-target fallback exists: a riposte is always the locked target's.
    [DefaultExecutionOrder(-80)]
    public sealed class Riposte301 : MonoBehaviour, IDrawPressArbiter
    {
        public Riposte301Profile Profile;
        public WorldMacroCombatWalker Walker;
        /// <summary>A riposte has taken the locked target's open window: the walker's draw gate stays shut for it (no new drawing in a
        /// window the volley answered). Never true while a draw mode is on - a drawing in progress is not cancelled by a lock change.</summary>
        public bool SuppressDraw => Walker != null && Walker.Drawing != null && !Walker.Drawing.InDrawMode && _candidate.Claims.BlocksNewDraw(KeyOf(LockedTarget()));
        /// <summary>The Q press that is held right now is a riposte candidate that has not drawn (the prompt's condition).</summary>
        public bool IsRiposteCandidate => _candidate.Active;
        /// <summary>The enemy the candidate was fixed on (null when there is none).</summary>
        public EnemyVitals CandidateTarget => _candidate.Active ? _candidateTarget : null;
        public int Volleys { get; private set; }
        public bool Active => _glyphs.Count > 0 || _circles.Count > 0;

        sealed class Glyph
        {
            public Transform T; public MeshRenderer R; public TrailRenderer Trail; public MaterialPropertyBlock Block; public Mesh Mesh; public Material Material; public float Size;
            public float LaunchAt, Flight, Loops, Phase; public Vector3 Start, Control, Side, Up; public bool Launched, Hit; public float HitAt, GhostAt; public Color Ink;
        }

        sealed class Circle
        {
            public Transform T, Centre; public MeshRenderer R, CentreR; public MaterialPropertyBlock Block, CentreBlock; public Color Ink;
            public float OpenAt, DoneAt, Size; public Vector3 Normal;
        }

        sealed class Mote { public Transform T; public MeshRenderer R; public MaterialPropertyBlock Block; public float Born, Life, Alpha; public Vector3 Velocity; public Color Ink; public float Size; }

        readonly List<Glyph> _glyphs = new List<Glyph>();
        readonly List<Circle> _circles = new List<Circle>();
        readonly List<Mote> _motes = new List<Mote>();
        EnemyVitals _target; uint _targetLife; float _clock, _damagePerGlyph; int _hits, _count;
        LockOn _lockOn;
        // D308-147: the windows already answered, the candidate of the held press, the window of the volley that runs
        readonly RiposteCandidate308 _candidate = new RiposteCandidate308();
        bool _beginning;
        EnemyVitals _candidateTarget; RiposteWindowKey308 _volleyKey;
        Riposte301Pose _pose;
        float _shake, _hitStopLeft, _savedScale = 1f;
        static Mesh[] _cells;
        static int _cellsFor;
        static Mesh _quad;
        // captures that render from their own camera (presentation films) face the glyphs to it instead of the player's eye
        public static Transform BillboardView;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { BillboardView = null; _cells = null; _cellsFor = 0; _quad = null; }   // Enter Play Mode keeps statics

        // one quad per atlas cell (UVs baked in; a property-block ST is ignored by the particle shader on meshes)
        static Mesh Cell(Riposte301Profile p, int cell)
        {
            int cols = Mathf.Max(1, p.AtlasColumns), rows = Mathf.Max(1, p.AtlasRows), key = cols * 1000 + rows;
            if (_cells == null || _cellsFor != key)
            {
                _cells = new Mesh[cols * rows]; _cellsFor = key;
                for (int i = 0; i < _cells.Length; i++)
                {
                    float u0 = (i % cols) / (float)cols, v1 = 1f - (i / cols) / (float)rows, u1 = u0 + 1f / cols, v0 = v1 - 1f / rows;
                    _cells[i] = Quad("RiposteGlyphCell_" + i, u1, v0, u0, v1);
                }
            }
            return _cells[cell % _cells.Length];
        }

        static Mesh Quad(string name, float ua, float va, float ub, float vb)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            m.uv = new[] { new Vector2(ua, va), new Vector2(ub, va), new Vector2(ua, vb), new Vector2(ub, vb) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds(); m.RecalculateNormals();
            return m;
        }

        static Mesh FullQuad() => _quad != null ? _quad : (_quad = Quad("RiposteQuad302", 0, 0, 1, 1));

        void OnEnable() { RenderPipelineManager.beginCameraRendering += ShakeBegin; RenderPipelineManager.endCameraRendering += ShakeEnd; }

        void Update()
        {
            if (Profile == null || Walker == null || Walker.Motor == null || Walker.Drawing == null) return;
            if (_lockOn == null) _lockOn = Walker.Motor.GetComponent<LockOn>();
            // D308-147: no press-time fire any more - the drawing controller calls the IDrawPressArbiter members below
            float real = Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
            // Paused by a menu or a card (0, while the hit stop itself runs at Profile.HitStopScale > 0): the countdown waits. It used to
            // run on real time and write the saved scale back over the pause.
            bool pausedByOther = Time.timeScale <= 0f && Profile.HitStopScale > 0f;
            if (!pausedByOther && _hitStopLeft > 0f && (_hitStopLeft -= real) <= 0f) Time.timeScale = Mathf.Max(1f, _savedScale);
            _shake = Mathf.Max(0f, _shake - _shake * Profile.ShakeDecay * real - real * .01f);
            if (Active) Step(Time.deltaTime);
            UpdateMotes(Time.deltaTime);
            if (_pose != null) _pose.Weight = PoseWeight();
        }

        // presentation hook (#302 trailer cue): fire the volley now if the same conditions as the released Q hold. It takes the same
        // window claim as the key does: a window answered by a drawing or an earlier volley refuses it.
        public bool Fire() => Ready() && TryBegin(LockedTarget());

        // ---- IDrawPressArbiter (D308-147, plan C): called by the drawing controller, in this order for one Q gesture:
        //      BeginCandidate (key down) -> OnStrokeChosen (first stroke) | OnReleasedWithoutStroke (key up, no stroke) | CancelCandidate

        // The key went down. A candidate fixes the lock target and its window and consumes nothing.
        DrawPressMode IDrawPressArbiter.BeginCandidate()
        {
            _candidate.Cancel(); _candidateTarget = null;
            if (!isActiveAndEnabled || !Ready() || Profile.GlyphMaterial == null) return DrawPressMode.None;
            var target = LockedTarget();
            // a candidate needs a real window nobody has answered yet (a drawing or a volley in it refuses the candidate)
            if (!_candidate.Begin(KeyOf(target), CanRiposteAt(target))) return DrawPressMode.None;
            _candidateTarget = target;
            return Profile.DeferPresentationOnRiposteCandidate ? DrawPressMode.Deferred : DrawPressMode.Immediate;
        }

        // The candidate's first stroke: a drawing. This window takes no riposte from now on, whatever happens to the drawing.
        void IDrawPressArbiter.OnStrokeChosen()
        {
            _candidate.StrokeChosen(); _candidateTarget = null;
        }

        // The key came up with no stroke (the draw mode is left already). The candidate is checked again: the same lock, the same life
        // and opening still open, in reach, able to act. Anything else is a miss - never another target, never a drawing or a sword.
        bool IDrawPressArbiter.OnReleasedWithoutStroke()
        {
            if (!_candidate.Active) return false;
            var target = _candidateTarget; _candidateTarget = null;
            bool eligible = isActiveAndEnabled && target != null && LockedTarget() == target && Ready();
            if (!_candidate.ReleaseWithoutStroke(KeyOf(target), eligible)) return false;
            return TryBegin(target);
        }

        void IDrawPressArbiter.CancelCandidate() { _candidate.Cancel(); _candidateTarget = null; }

        // The window the enemy has open right now (none when it is closed or the enemy is gone).
        static RiposteWindowKey308 KeyOf(EnemyVitals target)
            => target == null || !target.IsAlive || !target.WeakPointActive ? default : new RiposteWindowKey308(target.GetInstanceID(), target.LifeRevision, target.WeakPointGeneration);

        bool Ready()
        {
            if (Profile == null || Walker == null || Walker.Motor == null || Walker.Body == null || Walker.Drawing == null || Active || Walker.Drawing.InDrawMode) return false;
            if (_lockOn == null) _lockOn = Walker.Motor.GetComponent<LockOn>();
            return true;
        }

        EnemyVitals LockedTarget() => _lockOn != null ? _lockOn.Target : null;

        public bool CanRiposte(out EnemyVitals target)
        {
            target = LockedTarget();
            return CanRiposteAt(target) && _candidate.Claims.CanOfferRiposte(KeyOf(target));
        }

        bool CanRiposteAt(EnemyVitals target)
        {
            if (target == null || !target.IsAlive || !target.WeakPointActive || Walker.Seated || !Walker.Motor.CanBeginDrawing) return false;
            return Vector3.Distance(target.transform.position, Walker.Body.transform.position) <= Profile.Range;
        }

        Vector3 Chest(EnemyVitals t) => t.transform.position + Vector3.up * 1.2f;

        // The one door of a volley (the released key and the Fire() cue both come through here): not while one is being built, and only
        // if this window has not been answered - the claim is taken last, after every check that can refuse.
        bool TryBegin(EnemyVitals target)
        {
            if (_beginning || !CanRiposteAt(target) || Profile.GlyphMaterial == null) return false;
            var key = KeyOf(target);
            if (!_candidate.TryClaim(key)) return false;
            _beginning = true;
            try { return Launch(target, key); }
            finally { _beginning = false; }
        }

        bool Launch(EnemyVitals target, RiposteWindowKey308 key)
        {
            _volleyKey = key;
            _target = target; _targetLife = target.LifeRevision; _clock = 0f; _hits = 0;
            int n = Mathf.Max(1, Profile.GlyphCount); _count = n;
            _damagePerGlyph = Profile.TotalDamage / n;
            var caster = Walker.Body.transform;
            Vector3 toTarget = target.transform.position - caster.position; toTarget.y = 0f;
            Vector3 forward = toTarget.sqrMagnitude > .01f ? toTarget.normalized : caster.forward, right = Vector3.Cross(Vector3.up, forward);
            var random = new System.Random(Volleys * 7919 + 17);
            bool circles = Profile.MagicCircles && Profile.RingMaterial != null;
            int k = circles ? Mathf.Max(1, Profile.CircleCount) : 0;
            var mouths = new List<Vector3>(); var mouthNormals = new List<Vector3>();
            float ready = 0f;
            for (int i = 0; i < k; i++)
            {
                // a burst: circles scattered through a volume in front of and above the caster, in a random order and size
                float u = k > 1 ? i / (k - 1f) : .5f;
                float lateral = Mathf.Lerp(-Profile.CircleSpread, Profile.CircleSpread, (u * 7.31f + (float)random.NextDouble() * .3f) % 1f);
                float dist = Mathf.Lerp(Profile.CircleDistanceRange.x, Profile.CircleDistanceRange.y, (float)random.NextDouble());
                float height = Mathf.Lerp(Profile.CircleHeightRange.x, Profile.CircleHeightRange.y, (float)random.NextDouble());
                Vector3 pos = caster.position + forward * dist + right * lateral + Vector3.up * height;
                Vector3 normal = (Chest(target) - pos).normalized;
                int element = i % 5;
                float size = Mathf.Lerp(Profile.CircleSizeRange.x, Profile.CircleSizeRange.y, Mathf.Pow((float)random.NextDouble(), .8f));
                var c = new Circle { Ink = ElementInk(element), OpenAt = i * Profile.CircleStagger, Normal = normal, Size = size };
                c.T = NewQuad("RiposteCircle302_" + i, FullQuad(), Profile.RingMaterial, pos, out c.R, out c.Block);
                c.T.rotation = Quaternion.LookRotation(normal, Vector3.up);
                c.Centre = NewQuad("RiposteCircleGlyph302_" + i, Cell(Profile, CellOf(element, 0)), Profile.GlyphMaterial, pos - normal * .02f, out c.CentreR, out c.CentreBlock);
                _circles.Add(c); mouths.Add(pos); mouthNormals.Add(normal);
                ready = Mathf.Max(ready, c.OpenAt + Profile.CircleDraw);
            }
            for (int i = 0; i < n; i++)
            {
                float u = n > 1 ? i / (n - 1f) : .5f;
                Vector3 start;
                if (k > 0)
                {
                    int m = i % k; Vector3 nn = mouthNormals[m];
                    Vector3 across = Vector3.Cross(nn, Vector3.up).normalized, upIn = Vector3.Cross(across, nn);
                    float ang = (float)random.NextDouble() * 6.283f, rad = _circles[m].Size * .18f * (float)random.NextDouble();
                    start = mouths[m] + (across * Mathf.Cos(ang) + upIn * Mathf.Sin(ang)) * rad + nn * .05f;
                }
                else
                {
                    float angle = Mathf.Lerp(-100f, 100f, u) + (float)(random.NextDouble() - .5) * 18f;
                    Vector3 fan = Quaternion.AngleAxis(angle, Vector3.up) * -forward;
                    start = caster.position + Vector3.up * (Profile.SpawnHeight + (float)random.NextDouble() * .9f) + fan * Profile.SpawnRadius * (.75f + .5f * (float)random.NextDouble()) + forward * .6f;
                }
                // a circle's glyphs share its element: an initial of that element, flying in that element's ink
                int cell = k > 0 ? CellOf(i % k % 5, random.Next(Mathf.Max(1, CellsOf(i % k % 5)))) : random.Next(Mathf.Max(1, Profile.AtlasColumns * Profile.AtlasRows));
                var ink = k > 0 ? ElementInk(i % k % 5) : InkFor(cell);
                if (k > 0 && Profile.OrbMaterial != null) ink = Color.Lerp(ink, Color.white, Profile.OrbLighten);   // the flame reads over dark foliage
                bool orb = k > 0 && Profile.OrbMaterial != null;
                var mesh = orb ? FullQuad() : Cell(Profile, cell); var material = orb ? Profile.OrbMaterial : Profile.GlyphMaterial;
                var t = NewQuad(orb ? "RiposteOrb302_" + i : "RiposteGlyph301_" + i, mesh, material, start, out var r, out var block);
                t.localScale = Vector3.zero;
                TrailRenderer trail = null;
                if (Profile.TrailMaterial != null)
                {
                    trail = t.gameObject.AddComponent<TrailRenderer>();
                    trail.sharedMaterial = Profile.TrailMaterial; trail.time = .22f; trail.widthMultiplier = Profile.GlyphSize * .16f;
                    trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(ink, 0), new GradientColorKey(ink, 1) }, new[] { new GradientAlphaKey(Profile.TrailAlpha, 0), new GradientAlphaKey(0, 1) });
                    trail.colorGradient = g; trail.minVertexDistance = .04f; trail.emitting = false;
                    trail.shadowCastingMode = ShadowCastingMode.Off;
                }
                Vector3 aim = Chest(target);
                Vector3 mid = Vector3.Lerp(start, aim, .5f);
                Vector3 line = (aim - start).normalized, side = Vector3.Cross(Vector3.up, line).normalized, up = Vector3.Cross(line, side);
                _glyphs.Add(new Glyph
                {
                    T = t, R = r, Trail = trail, Block = block, Mesh = mesh, Material = material, Size = orb ? Profile.OrbSize : Profile.GlyphSize, Start = start, Ink = ink,
                    LaunchAt = k > 0 ? ready + i * Profile.FireInterval : Profile.LaunchSpread * u * (.8f + .4f * (float)random.NextDouble()),
                    Flight = k > 0 ? Mathf.Lerp(Profile.WispFlightMin, Profile.WispFlightMax, (float)random.NextDouble()) : Profile.Flight,
                    Loops = Mathf.Lerp(Profile.WispLoopsMin, Profile.WispLoopsMax, (float)random.NextDouble()),
                    Phase = (float)random.NextDouble() * 6.283f,
                    Control = mid + Vector3.up * Profile.ArcHeight * (k > 0 ? .35f : 1f) * (.6f + .8f * (float)random.NextDouble()) + side * (float)(random.NextDouble() - .5) * 3f,
                    Side = side, Up = up,
                });
            }
            if (Profile.CastPose)
            {
                var animator = Walker.Body.GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    _pose = animator.GetComponent<Riposte301Pose>() ?? animator.gameObject.AddComponent<Riposte301Pose>();
                    _pose.Bind(animator);
                }
            }
            Volleys++;
            return true;
        }

        Transform NewQuad(string name, Mesh mesh, Material material, Vector3 pos, out MeshRenderer r, out MaterialPropertyBlock block)
        {
            var go = new GameObject(name) { layer = Profile.Layer };
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            block = new MaterialPropertyBlock();
            return go.transform;
        }

        Color ElementInk(int element)
        {
            var inks = Profile.ElementInks;
            return inks != null && inks.Length > 0 ? inks[Mathf.Clamp(element, 0, inks.Length - 1)] : Profile.Ink;
        }

        int CellOf(int element, int nth)
        {
            var e = Profile.CellElements;
            if (e != null) for (int i = 0, seen = 0; i < e.Length; i++) if (e[i] == element && seen++ == nth) return i;
            return element;
        }

        int CellsOf(int element)
        {
            var e = Profile.CellElements; int n = 0;
            if (e != null) foreach (int v in e) if (v == element) n++;
            return n;
        }

        Color InkFor(int cell)
        {
            var e = Profile.CellElements; var inks = Profile.ElementInks;
            if (e == null || inks == null || inks.Length == 0 || cell >= e.Length) return Profile.Ink;
            return inks[Mathf.Clamp(e[cell], 0, inks.Length - 1)];
        }

        Vector3 Aim(Glyph g)
        {
            bool alive = _target != null && _target.IsAlive && _target.LifeRevision == _targetLife;
            return alive ? Chest(_target) : g.Control;
        }

        // 도깨비불: the base curve toward the target with a lateral weave and small loops that settle as the glyph arrives,
        // and an uneven pace (darts and hesitations)
        Vector3 WispPoint(Glyph g, float u)
        {
            float s = Mathf.Clamp01(u + .06f * Mathf.Sin(u * 12.566f + g.Phase) * (1f - u));
            s = s * s * (3f - 2f * s);
            Vector3 aim = Aim(g);
            Vector3 p = Vector3.Lerp(Vector3.Lerp(g.Start, g.Control, s), Vector3.Lerp(g.Control, aim, s), s);
            float settle = Mathf.Pow(1f - s, 1.3f) * Mathf.Clamp01(s * 6f), w = 6.283f * g.Loops * s + g.Phase;
            return p + (g.Side * Mathf.Sin(w) + g.Up * .65f * Mathf.Cos(w * 1.3f)) * Profile.WispAmplitude * settle;
        }

        void Step(float dt)
        {
            _clock += dt;
            var cam = Camera.main;
            Transform view = BillboardView != null ? BillboardView : cam != null ? cam.transform : null;
            // circles: draw in (grow, spin, ink up), hold while they fire, then thin out like ink bleeding into paper
            for (int i = _circles.Count - 1; i >= 0; i--)
            {
                var c = _circles[i];
                float t = _clock - c.OpenAt;
                float open = Mathf.Clamp01(t / Profile.CircleDraw), eased = 1f - (1f - open) * (1f - open);
                float fade = c.DoneAt > 0f ? Mathf.Clamp01((_clock - c.DoneAt) / .35f) : 0f;
                float back = 1f + Profile.CirclePop * Mathf.Sin(open * Mathf.PI) * (1f - open * .4f);   // bursts past its size, then settles
                float scale = c.Size * (t <= 0f ? 0f : (.35f + .65f * eased) * back) * (1f + .15f * fade);
                c.T.localScale = Vector3.one * scale;
                c.T.rotation = Quaternion.LookRotation(c.Normal, Vector3.up) * Quaternion.Euler(0, 0, _clock * Profile.CircleSpin * (i % 2 == 0 ? 1f : -1f) + (1f - eased) * 90f);
                c.Block.SetColor("_BaseColor", new Color(c.Ink.r * 1.15f, c.Ink.g * 1.15f, c.Ink.b * 1.15f, eased * (1f - fade)));
                c.R.SetPropertyBlock(c.Block);
                c.Centre.localScale = Vector3.one * scale * .42f;
                if (view != null) c.Centre.rotation = Quaternion.LookRotation(c.Centre.position - view.position, view.up) * Quaternion.Euler(0f, 180f, 0f);
                c.CentreBlock.SetColor("_BaseColor", new Color(c.Ink.r, c.Ink.g, c.Ink.b, eased * (1f - fade)));
                c.CentreR.SetPropertyBlock(c.CentreBlock);
                if (fade >= 1f) { Destroy(c.T.gameObject); Destroy(c.Centre.gameObject); _circles.RemoveAt(i); }
            }
            float lastLaunch = 0f;
            for (int i = _glyphs.Count - 1; i >= 0; i--)
            {
                var g = _glyphs[i];
                if (g.T == null) { _glyphs.RemoveAt(i); continue; }
                lastLaunch = Mathf.Max(lastLaunch, g.LaunchAt);
                float size = g.Size;
                if (!g.Hit)
                {
                    float t = _clock - g.LaunchAt;
                    if (t <= 0f) { g.T.localScale = Vector3.zero; continue; }
                    float u = Mathf.Clamp01(t / g.Flight);
                    g.T.position = WispPoint(g, u);
                    g.T.localScale = Vector3.one * size * Mathf.Clamp01(t / .08f);
                    g.Block.SetColor("_BaseColor", g.Ink); g.R.SetPropertyBlock(g.Block);
                    if (g.Trail != null) g.Trail.emitting = true;
                    if (_clock - g.GhostAt >= Profile.GhostInterval && u < .97f) { g.GhostAt = _clock; Ghost(g); }
                    if (u >= 1f) { g.Hit = true; g.HitAt = _clock; Land(g); }
                }
                else
                {
                    float after = _clock - g.HitAt, fade = Mathf.Clamp01(after / .18f);
                    g.T.localScale = Vector3.one * size * (1f + fade * .9f);
                    g.Block.SetColor("_BaseColor", Color.Lerp(Profile.HitFlash, new Color(g.Ink.r, g.Ink.g, g.Ink.b, 0f), fade));
                    g.R.SetPropertyBlock(g.Block);
                    if (g.Trail != null) g.Trail.emitting = false;
                    if (after >= .45f) { Destroy(g.T.gameObject); _glyphs.RemoveAt(i); continue; }
                }
                if (view != null) g.T.rotation = Quaternion.LookRotation(g.T.position - view.position, view.up) * Quaternion.Euler(0f, 180f, 0f);
            }
            // circles close once their share has left
            foreach (var c in _circles) if (c.DoneAt <= 0f && _clock > lastLaunch + .25f) c.DoneAt = _clock;
            if (_glyphs.Count == 0 && _circles.Count == 0) Finish();
        }

        void Ghost(Glyph g)
        {
            var t = NewQuad("RiposteGhost302", g.Mesh, g.Material, g.T.position, out var r, out var block);
            t.rotation = g.T.rotation; t.localScale = g.T.localScale * .96f;
            _motes.Add(new Mote { T = t, R = r, Block = block, Born = Time.time, Life = Profile.GhostLife, Alpha = Profile.GhostAlpha, Ink = g.Ink, Size = t.localScale.x });
        }

        void Splash(Vector3 at, Color ink)
        {
            if (Profile.SplashMaterial == null) return;
            for (int i = 0; i < Profile.SplashCount; i++)
            {
                var t = NewQuad("RiposteSplash302", FullQuad(), Profile.SplashMaterial, at, out var r, out var block);
                var v = (Random.onUnitSphere + Vector3.up * .6f).normalized * Profile.SplashSpeed * Random.Range(.5f, 1.2f);
                float s = Profile.SplashSize * Random.Range(.5f, 1.1f);
                _motes.Add(new Mote { T = t, R = r, Block = block, Born = Time.time, Life = Profile.SplashLife * Random.Range(.7f, 1.2f), Alpha = 1f, Velocity = v,
                    Ink = Color.Lerp(ink, Profile.Ink, .5f), Size = s });
            }
        }

        void UpdateMotes(float dt)
        {
            var cam = Camera.main;
            Transform view = BillboardView != null ? BillboardView : cam != null ? cam.transform : null;
            for (int i = _motes.Count - 1; i >= 0; i--)
            {
                var m = _motes[i];
                float age = (Time.time - m.Born) / Mathf.Max(.01f, m.Life);
                if (m.T == null || age >= 1f) { if (m.T != null) Destroy(m.T.gameObject); _motes.RemoveAt(i); continue; }
                if (m.Velocity != Vector3.zero)
                {
                    m.Velocity += Physics.gravity * .35f * dt; m.T.position += m.Velocity * dt;
                    m.T.localScale = Vector3.one * m.Size * (1f + age * .6f);
                    if (view != null) m.T.rotation = Quaternion.LookRotation(m.T.position - view.position, view.up);
                }
                m.Block.SetColor("_BaseColor", new Color(m.Ink.r, m.Ink.g, m.Ink.b, m.Alpha * (1f - age)));
                m.R.SetPropertyBlock(m.Block);
            }
        }

        void Land(Glyph g)
        {
            _hits++;
            Splash(g.T.position, g.Ink);
            _shake = Mathf.Min(Profile.ShakeAmplitude * 3f, _shake + Profile.ShakeAmplitude);
            if (_hits == _count && Profile.HitStopSeconds > 0f && _hitStopLeft <= 0f)
            {
                _savedScale = Mathf.Max(1f, Time.timeScale); Time.timeScale = Profile.HitStopScale; _hitStopLeft = Profile.HitStopSeconds; _shake += Profile.ShakeAmplitude * 2f;
            }
            if (_target == null || !_target.IsAlive || _target.LifeRevision != _targetLife) return;
            _target.TakeDamage(_damagePerGlyph, AttackProvenance.CreateMelee(this, DamageSource.PlayerDirect, null));   // #308 WP-16: melee passes a guard
        }

        float PoseWeight()
        {
            if (!Active) return 0f;
            float last = 0f; foreach (var g in _glyphs) last = Mathf.Max(last, g.LaunchAt);
            float up = Mathf.Clamp01(_clock / Mathf.Max(.01f, Profile.PoseRaise)), down = 1f - Mathf.Clamp01((_clock - last - .15f) / Mathf.Max(.01f, Profile.PoseLower));
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(up, down));
        }

        // view shake: offset each game camera just for its render, restore right after (the camera rig never sees it)
        readonly Dictionary<Camera, Vector3> _shaken = new Dictionary<Camera, Vector3>();

        void ShakeBegin(ScriptableRenderContext _, Camera camera)
        {
            if (_shake <= .0005f || camera.cameraType != CameraType.Game) return;
            float t = Time.time * 47f;
            var offset = new Vector3(Mathf.PerlinNoise(t, 1.3f) - .5f, Mathf.PerlinNoise(2.1f, t) - .5f, 0f) * 2f * _shake;
            _shaken[camera] = camera.transform.position;
            camera.transform.position += camera.transform.rotation * offset;
        }

        void ShakeEnd(ScriptableRenderContext _, Camera camera)
        {
            if (_shaken.TryGetValue(camera, out var pos)) { camera.transform.position = pos; _shaken.Remove(camera); }
        }

        // D308-147: the volley closes only its own window (this life, this opening). A late finish whose window has gone - or has been
        // replaced by the next one - changes nothing; the attack records on the enemy are not touched.
        void Finish()
        {
            if (Profile.ConsumesWindow && _target != null) _target.TryConsumeWeakPointWindow(_volleyKey.Life, _volleyKey.Generation);
            _target = null;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= ShakeBegin; RenderPipelineManager.endCameraRendering -= ShakeEnd;
            foreach (var kv in _shaken) if (kv.Key != null) kv.Key.transform.position = kv.Value;
            _shaken.Clear();
            if (_hitStopLeft > 0f) { Time.timeScale = _savedScale; _hitStopLeft = 0f; }
            foreach (var g in _glyphs) if (g.T != null) Destroy(g.T.gameObject);
            foreach (var c in _circles) { if (c.T != null) Destroy(c.T.gameObject); if (c.Centre != null) Destroy(c.Centre.gameObject); }
            foreach (var m in _motes) if (m.T != null) Destroy(m.T.gameObject);
            _glyphs.Clear(); _circles.Clear(); _motes.Clear(); _target = null; _candidate.Cancel(); _candidateTarget = null;
            if (_pose != null) _pose.Weight = 0f;
        }
    }
}
