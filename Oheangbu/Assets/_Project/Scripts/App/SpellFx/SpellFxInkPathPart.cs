using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-31 ㅏ 경로 as a SHARP INK BRUSH STROKE (D308-161: the hexagon membrane is defence only; D308-162: "것 = 붓털 + 휘두른 호 날 · 감 = 표면 납작 먹 자국"; PLAN-308-INK-STROKE-SPELLS 1, 2.1, 4).
    /// One stroke leaves the brush tip at a visible 22 m/s (TEST 20 - 25) in a plane turned 35 deg off the camera-facing plane with a 0.3 - 0.45 m arc (수: an S whip), its afterimages 0.05 s apart behind it;
    /// the rule's shot (36 m/s) arrives first and its look is held until the head gets there (the hex path's way). Contact = ONE frame of three things (ink splats on and up, the body's accent sparks, the core lit)
    /// and a 2-frame stop of the look clock; then the slow part: afterimages catch up and fade (0.4 s), the mark stays 0.6 - 1.0 s, the stroke cracks dry in 0.4 - 0.6 s (the shader's _Dry), the body's end.
    /// The five bodies are the stroke's SHAPE (<see cref="InkBody"/>): 목 awl + thorns + coil, 화 wedge + 3 tongues + ash, 토 short blunt + stones + slab, 금 thinnest + blade flick + side sparks, 수 S whip + runs.
    /// Verbs (times from the launch; Tl = the head's arrival, TEST):
    ///  가 Once    the head (an awl of 4 bristle strands) lodges on the body's camera hemisphere (46 - 70 % high; a bust 60 - 78 %), 3 strands COIL the body once like a rope (R7), a faint wash 0.6 m at the foot;
    ///             the mark and the coil stay to Tl + 2 (the rule's grip), then end (wood: root threads). The brush tip gathers ink from the commit (<see cref="ISpellFxPreLaunch"/>).
    ///  각 Cage    (극치: width x1.3, standing core, 3 afterimages) as 가 for 1.5 s; the coil runs down to the ground and pools (Tl + 0.25), 8 branch strokes rise OUT OF THE POOL round the body
    ///             (radius = the body's measured radius + 0.3, 1.4 m, a gap toward the camera, 0.08 s apart from Tl + 0.4), hooked thorns, 3 thin threads wavering over them; it stays to Tl + 10.
    ///  간 Spread  as 가 on the first body; a contact of ANOTHER body sends a flat wet bleed 0.3 m wide along the ground to it at 10 m/s; where it arrives a half-size awl rises into the neighbour and coils it.
    ///  갓 Cut     the stroke stands like a blade (turned 25 deg off edge-on toward the camera), goes THROUGH the body (the stop, sparks, 2 flat armour flakes) and on 1 m past its far side, then cracks dry;
    ///             a 4 cm ink line runs on the ground from under the brush tip to the stroke's end (grown under the head, 3 s); a short slash mark stays on the body 2.5 s.
    ///  강 Return  the head meets the body at the SHOULDER (R9: the U-turn is the contact), 2 strands coil the shoulder and stay 2 s; the stroke goes on round a closed loop (2 m wide, tilted 35 deg) back to the
    ///             brush tip in 0.4 s, its tail kept at the brush so the ink draws the whole loop; a small second contact at the tip, then the tail clears in 0.5 s. A lab shot touching the drawn
    ///             path turns to ink (<see cref="SpellFxLabShot.Dissolve"/>).
    /// Look only: nothing here touches a rule (range, damage, lifetime, recognition).</summary>
    public sealed class SpellFxInkPathPart : MonoBehaviour, ISpellFxPart, ISpellFxPreLaunch
    {
        /// <summary>가 Once · 각 Cage · 간 Spread · 갓 Cut · 강 Return (the order is written into prefabs by index - append only).</summary>
        public enum InkPathBite { Once, Cage, Spread, Cut, Return }

        [Tooltip("The ONE stroke material (Oheangbu/SpellFx/InkStrokeA or B, the body's accent in _Accent); every renderer group shares it with a property block.")]
        [SerializeField] Material material;
        [SerializeField] Body body = Body.Wood;
        [SerializeField] InkPathBite bite = InkPathBite.Once;
        [Tooltip("극치 표식 (PLAN 2.5): width x1.3, the core line standing (not only at the hit), one more afterimage.")]
        [SerializeField] bool peak;

        [Header("Flight (TEST)")]
        [Tooltip("Visible head speed (m/s); the rule's path is 36.")]
        [SerializeField] float headSpeed = 22f;
        [Tooltip("Used when the cast carries no spec: how far the stroke flies without a hit (m).")]
        [SerializeField] float maxReach = 12f;
        [Tooltip("The flying plane contains the line brush -> target and is turned this far (deg) off the plane that faces the camera most (RiposteStroke308's PlaneYawDegrees).")]
        [SerializeField] float planeYawDeg = 35f;
        [Tooltip("The arc's bulge (m, 0.3 - 0.45) in that plane; 수's S amplitude.")]
        [SerializeField] float arcBulge = 0.38f, whipAmp = 0.3f;
        [SerializeField] float widthScale = 1f, lengthScale = 1f;
        [Tooltip("Afterimages (PBZ phantom): count (극치 +1), delay between them (s), their fade after the head stops (s), the first's alpha, the slash offset per copy (m).")]
        [SerializeField] int afterimages = 2;
        [SerializeField] float afterLag = 0.05f, afterFade = 0.4f, afterAlpha = 0.5f, afterSlash = 0.035f;
        [SerializeField] bool followGround = true;
        [Tooltip("2026-10-31 shoulder-camera readability (TEST): the shoulder camera looks along the cast, so a stroke aimed at the body ahead is seen almost end-on (a 1.4 m stroke shrinks to a dot over the body). " +
            "When the line brush -> target spans less than this angle of view (radians), the arc bulges until it does (0 = off), at most maxScreenBulge metres.")]
        [SerializeField] float screenSpread = 0.3f, maxScreenBulge = 1.3f;
        [Tooltip("The afterimages' slash offset per copy as an angle of view (radians x the camera distance; at least afterSlash), so the stacked copies part on screen even end-on.")]
        [SerializeField] float afterSpread = 0.016f;

        [Header("Beat (TEST)")]
        [SerializeField] int hitStopFrames = 2;
        [Tooltip("The core line at a contact: lit this long, then 0.12 s back to the body's resting weight.")]
        [SerializeField] float coreFlashSeconds = 0.15f;
        [Tooltip("The flying stroke stays this long after its head stopped (the mark, 0.6 - 1.0), then its tail clears and it cracks dry.")]
        [SerializeField] float markSeconds = 0.8f, tailCutSeconds = 0.3f, drySeconds = 0.5f;
        [Tooltip("Hand-drawn timing of growing strands and branches: held in steps of 1 / this (30 = every 2 frames).")]
        [SerializeField] float stepHz = 30f;

        [Header("Verbs (TEST)")]
        [Tooltip("The grip the mark and the coil stay for (가 · 간 · 강: the rule's 2 s); 갓's slash mark; 각's hold before the cage stands alone.")]
        [SerializeField] float holdSeconds = 2f, cutHoldSeconds = 2.5f, cageHoldSeconds = 1.5f;
        [Tooltip("목's coil: strands, turns round the body.")]
        [SerializeField] int coilStrands = 3;
        [SerializeField] float coilTurns = 1f;
        [Tooltip("각: branches, height (m), gap outside the body's measured radius (m), how long it stands (s).")]
        [SerializeField] int cageBranches = 8;
        [SerializeField] float cageHeight = 1.4f, cageGap = 0.3f, cageStandSeconds = 10f;
        [Tooltip("간: the bleed's width (m), speed (m/s), life (s); the neighbour's awl as a fraction of the stroke.")]
        [SerializeField] float spreadWidth = 0.3f, spreadSpeed = 10f, roadSeconds = 3f, neighbourScale = 0.5f;
        [Tooltip("갓: how far past the body's far side the blade goes (m); the ground line's width (m) and life (s).")]
        [SerializeField] float cutThrough = 1f, crackWidth = 0.04f, crackSeconds = 3f;
        [Tooltip("강: the loop's width (m) and the way home (s).")]
        [SerializeField] float returnWidth = 2f, returnSeconds = 0.4f;
        [Tooltip("가 · 간 · 강: the faint wash at the struck body's foot toward the caster (m).")]
        [SerializeField] float washLength = 0.6f;

        const float Frame60 = 1f / 60f;

        // ------------------------------------------------------------ types

        struct Call { public float At; public System.Action Do; }

        /// <summary>A strand that grows along its own path from <see cref="T0"/> over <see cref="Grow"/> (a coil, a run, a drip): drawn lying on a body round <see cref="Axis"/>.</summary>
        sealed class Strand
        {
            public readonly InkCurve Path = new InkCurve();
            public float T0, Grow = 0.22f, Seed;
            public InkStrokeShape Shape;
            public Vector3 Axis;
            public bool FaceCam;                // drawn turned to the camera (a drip in the air) instead of lying on the body
            public System.Action Done; bool _done;
            public void Fire() { if (_done) return; _done = true; Done?.Invoke(); }
        }

        /// <summary>What stays on ONE struck body: the mark, the coil / runs / tongues, the slab is the ground's. Its group follows the body (the earth push) until <see cref="FreezeAt"/>.</summary>
        sealed class HoldSet
        {
            public InkLayer Layer;
            public HexBody Body;
            public Transform Tr; public Vector3 Anchor, Delta; public float FreezeAt = float.MaxValue;
            public readonly InkCurve Mark = new InkCurve();
            public readonly List<Strand> Strands = new List<Strand>();
            public InkStrokeShape MarkShape;
            public float T0, End = float.MaxValue, FlashAt = -1f, Scale = 1f;
            public Vector3 E, Face, Travel;
            public bool EndDone;
            // 간's neighbour: the small awl that rises into it before the hold
            public readonly InkCurve Awl = new InkCurve();
            public InkStrokeShape AwlShape; public float AwlT0 = -1f, AwlT1;
        }

        sealed class Branch { public readonly InkCurve Path = new InkCurve(); public float Born, Grow = 0.2f, LeaveAt, Seed; public Vector3 Foot; public bool EndDone; }

        // ------------------------------------------------------------ state

        readonly List<Call> _calls = new List<Call>();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly InkTipGather _gather = new InkTipGather();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Dictionary<long, float> _groundCache = new Dictionary<long, float>();
        readonly InkCurve _path = new InkCurve(), _full = new InkCurve(), _loop = new InkCurve(), _scratch = new InkCurve(), _scratch2 = new InkCurve();
        readonly List<Branch> _branches = new List<Branch>();
        readonly List<Strand> _drips = new List<Strand>();
        readonly List<Vector3> _crackPts = new List<Vector3>(); readonly List<float> _crackS = new List<float>();
        readonly List<Vector3> _roadPts = new List<Vector3>();

        HexSheet _sheet;
        System.Random _rng;
        bool _built, _launched, _ended, _hasHit, _retOn, _primaryDone;
        bool _faceCam;          // the flight is seen end-on: its ribbon turns to the camera (BuildFlight)
        Vector3 _aim;           // the launch's guessed landing point
        float _real, _look, _stop, _endAt, _reach, _seed;
        Vector3 _origin, _fwd, _right, _cam;
        Color _accent;
        InkStrokeShape _shape;
        float _coreBase;
        long _primaryKey;

        // the flight (world space)
        Vector3 _start, _planeN, _outDir, _home, _loopLat;
        float _lenOut, _totalOut, _tl = -1f, _loopLen, _homeArr = -1f, _missAt = -1f, _flashFly = -1f, _nextAsh;
        int _ashCount;

        InkLayer _fly, _ground, _cage, _road;
        HoldSet _hold, _side;

        // ground marks: the wash (가 간 강 각), the crack (갓), the slab (토)
        Vector3 _washFrom, _washDir; float _washT0 = -1f;
        Vector3 _slabC; float _slabT0 = -1f;
        // 각
        Vector3 _poolC; float _poolR, _poolT0 = -1f, _cageEnd;
        // 간
        float _roadT0 = -1f, _roadLen;

        // ------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast) { _rng = new System.Random((int)(cast.CastId & 0x7fffffff)); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            if (!_built) return;
            _launched = true;
            _gather.Stop();
            _start = TipWorld();
            _cam = CamPos();
            Vector3 aim = GuessAim();
            _aim = aim;
            BuildFlight(_start, aim, true);
            SpellFxHexKit.Spit(_start, _sheet.AwayFromCamera());
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;
            _ended = true;
            _gather.Stop();
            HideAll();
        }

        /// <summary>한 번 only (the shape leaves at the swing's apex): ink motes gather at the brush tip and a thin ink arc follows its last 0.15 s.</summary>
        public void OnPreLaunch(SpellFxCast cast, float secondsToLaunch)
        {
            if (_ended || _launched) return;
            Build(cast);
            if (!_built) return;
            float delay = cast.LaunchDelaySeconds, since = Mathf.Max(0f, delay - secondsToLaunch);
            Vector3 tip = TipWorld();
            _cam = CamPos();
            _gather.Show(transform, tip, since, delay);
            _fly.Begin();
            var tr = _gather.Trail;
            if (tr.Count >= 2)
            {
                _scratch.Clear();
                for (int i = 0; i < tr.Count; i++) _scratch.Add(tr[i]);
                var thin = new InkStrokeShape { HeadW = 0.022f, TorsoW = 0.016f, TailW = 0.004f, HeadZone = 0.05f, TailZone = 0.15f, HeadPoint = 0.03f, TailPoint = 0.08f, Wet0 = 0f, Wet1 = 0.8f, Core = 0f };
                InkStrokeBuilder.Ribbon(_fly.Buf, _scratch, 0f, _scratch.Length, thin, InkFace.Camera(_cam), InkPaint.Main(_seed), null);
            }
            _fly.TailCut = 0f; _fly.Dry = 0f; _fly.CoreK = 0f;
            _fly.Flush();
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            if (!_launched || _ended || _sheet == null) { _real = ageSeconds; return; }
            // the look clock: it follows the cast's age but stands still for the stop a contact asks for (the rule's clock keeps running)
            float dt = Mathf.Max(0f, ageSeconds - _real); _real = ageSeconds;
            if (_stop > 0f) { float used = Mathf.Min(dt, _stop); _stop -= used; dt -= used; }
            _look += dt;
            float a = _look;
            if (a > _endAt) { HideAll(); return; }
            _cam = CamPos();
            Follow(_hold, a); Follow(_side, a);
            if (_retOn) { _home = TipWorld(); RebuildLoop(); }
            DrainCalls(a);
            _ink.Drain(a);
            Shots(a);
            FireAsh(a);
            CheckMiss(a);
            EmitFly(a);
            EmitHold(_hold, a);
            EmitHold(_side, a);
            EmitGround(a);
            EmitCage(a);
            EmitRoad(a);
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (!_built || _ended || !_launched) return;
            long key = hit.TargetId != 0 ? hit.TargetId : 1000000L + _seen.Count;
            Vector3 rel = hit.Feet - _origin;
            var feet = new Vector2(Vector3.Dot(rel, _right), Vector3.Dot(rel, _fwd));
            if (!_primaryDone)
            {
                _primaryDone = true; _primaryKey = key; _seen.Add(key);
                Primary(hit, feet);
                return;
            }
            if (!_seen.Add(key)) { if (key == _primaryKey) SecondHit(); return; }
            if (bite == InkPathBite.Spread) Neighbour(hit, feet);
        }

        void OnDestroy()
        {
            _fly?.Destroy(); _ground?.Destroy(); _cage?.Destroy(); _road?.Destroy();
            _hold?.Layer?.Destroy(); _side?.Layer?.Destroy();
        }

        // ------------------------------------------------------------ helpers

        float Rand() => (float)_rng.NextDouble();
        Vector3 PlanW(Vector3 p) => _sheet.PlanW(p);
        Vector3 TipWorld() => _sheet.PlanW(_sheet.SampleTipPlan());
        void Extend(float t) { if (t > _endAt) _endAt = t; }
        void Stop() { _stop = Mathf.Max(_stop, Mathf.Max(0, hitStopFrames) * Frame60); }
        bool Wood => body == Body.Wood;

        Vector3 CamPos()
        {
            var c = UnityEngine.Camera.main;
            return c != null ? c.transform.position : _origin - _fwd * 2.8f + Vector3.up * 2.25f + _right * 0.7f;
        }

        void At(float t, System.Action act)
        {
            int k = 0; while (k < _calls.Count && _calls[k].At <= t) k++;
            _calls.Insert(k, new Call { At = t, Do = act });
        }

        void DrainCalls(float a)
        {
            while (_calls.Count > 0 && _calls[0].At <= a)
            {
                var c = _calls[0]; _calls.RemoveAt(0);
                c.Do?.Invoke();
            }
        }

        /// <summary>The core line at a contact <paramref name="t0"/>: 1 for <see cref="coreFlashSeconds"/>, back to 0 over 0.12 s (-1 = none).</summary>
        float Flash(float a, float t0)
        {
            if (t0 < 0f || a < t0) return 0f;
            float u = a - t0;
            return u < coreFlashSeconds ? 1f : 1f - InkMath.Smooth((u - coreFlashSeconds) / 0.12f);
        }

        float CoreK(float a, float flashAt) => Mathf.Max(_coreBase, Flash(a, flashAt));

        /// <summary>The ground under a world point: a ray down that skips the struck bodies (the kit's ground only skips rigid bodies), kept on a 0.2 m lattice.</summary>
        float GroundAt(Vector3 w)
        {
            if (!followGround) return _origin.y;
            int qx = Mathf.RoundToInt(w.x / 0.2f), qz = Mathf.RoundToInt(w.z / 0.2f);
            long key = SpellFxHexKit.Key(qx, qz);
            if (_groundCache.TryGetValue(key, out float cached)) return cached;
            Vector3 from = new Vector3(qx * 0.2f, _origin.y + 1.6f, qz * 0.2f);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 3.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.collider is CharacterController || h.normal.y < 0.5f || IsBodyCollider(h.collider)) continue;
                best = Mathf.Max(best, h.point.y);
            }
            float g = best > float.NegativeInfinity ? Mathf.Clamp(best, _origin.y - 1f, _origin.y + 1f) : _origin.y;
            _groundCache[key] = g;
            return g;
        }

        bool IsBodyCollider(Collider c) => OfHold(_hold, c) || OfHold(_side, c);

        static bool OfHold(HoldSet hs, Collider c)
        {
            if (hs == null) return false;
            if (hs.Tr != null && c.transform.IsChildOf(hs.Tr)) return true;
            if (hs.Body != null && hs.Body.Cols != null) foreach (var b in hs.Body.Cols) if (b == c) return true;
            return false;
        }

        Vector3 OnGround(Vector3 w, float lift = 0.012f) => new Vector3(w.x, GroundAt(w) + lift, w.z);

        void HideAll()
        {
            _fly?.Hide(); _ground?.Hide(); _cage?.Hide(); _road?.Hide();
            _hold?.Layer?.Hide(); _side?.Layer?.Hide();
        }

        // ------------------------------------------------------------ build

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _seed = Rand();
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            _reach = cast.Spec != null && cast.Spec.PathLength > 0.5f ? Mathf.Min(maxReach, cast.Spec.PathLength) : maxReach;
            _sheet = HexSheet.Probe(cast, HexFrame.UprightSunk(_origin, _right, _fwd, 1f), followGround);
            float w = widthScale * (peak ? 1.3f : 1f);
            _shape = InkBody.Shape(body).Scaled(w, lengthScale);
            _coreBase = peak ? 1f : InkBody.CoreBase(body);
            _accent = InkBody.AccentOf(material, body);
            _fly = new InkLayer(transform, "InkFly", material);
            _ground = new InkLayer(transform, "InkGround", material);
            _cam = CamPos();
            _start = TipWorld();
            _endAt = _reach / Mathf.Max(1f, headSpeed) + 1.6f;
            _built = true;
        }

        /// <summary>Before anything is known to be hit: the body straight ahead if a sphere cast finds one (a look-only guess, so the arc / S is drawn to it from the first frame), else a chest-high point 4.5 m ahead.
        /// The contact later bends the rest of the path onto the real landing point.</summary>
        Vector3 GuessAim()
        {
            Vector3 o = _origin + Vector3.up * 1.0f;
            int n = Physics.SphereCastNonAlloc(o, 0.4f, _fwd, _hits, _reach, ~0, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue; Bounds best = default; bool got = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                var c = h.collider;
                if (c == null || c.isTrigger || c is CharacterController || h.distance < 0.6f) continue;
                Bounds b = c.bounds;
                if (b.size.y < 0.3f || b.size.y > 3.5f || Mathf.Max(b.extents.x, b.extents.z) > 2.5f || b.max.y < _origin.y + 0.3f) continue;
                if (h.distance < bestD) { bestD = h.distance; best = b; got = true; }
            }
            if (got)
            {
                Vector3 ctr = best.center;
                float y = Mathf.Lerp(best.min.y, best.max.y, best.size.y < 1.05f ? 0.66f : 0.55f);
                Vector3 toC = InkMath.Flat(ctr - _start); toC = toC.sqrMagnitude > 1e-4f ? toC.normalized : _fwd;
                float rad = Mathf.Min(best.extents.x, best.extents.z);
                return new Vector3(ctr.x, y, ctr.z) - toC * (rad * 0.8f);
            }
            float yp = Mathf.Min(_start.y, _origin.y + 1.0f);
            Vector3 p = _origin + _fwd * Mathf.Clamp(_reach * 0.4f, 3f, 4.5f);
            return new Vector3(p.x, yp, p.z);
        }

        /// <summary>The flying plane's normal for this bite: the camera-facing plane through the line turned <see cref="planeYawDeg"/>; 갓's blade stands vertical and is turned 25 deg toward the camera (never edge-on).</summary>
        void PlaneFor(Vector3 from, Vector3 to, out Vector3 normal, out Vector3 lateral)
        {
            InkStrokeBuilder.FlightPlane(from, to, _cam, planeYawDeg, out normal, out lateral);
            if (bite != InkPathBite.Cut) return;
            Vector3 d = (to - from).sqrMagnitude > 1e-6f ? (to - from).normalized : _fwd;
            Vector3 nv = Vector3.Cross(d, Vector3.up); if (nv.sqrMagnitude < 1e-6f) nv = _right; nv.Normalize();
            Vector3 n0 = InkMath.Perp(d, _cam - (from + to) * 0.5f);
            if (Vector3.Dot(nv, n0) < 0f) nv = -nv;
            normal = Vector3.RotateTowards(nv, n0, 25f * Mathf.Deg2Rad, 0f).normalized;
            lateral = Vector3.Cross(normal, d).normalized; if (lateral.y < 0f) lateral = -lateral;
        }

        /// <summary>The path from <paramref name="from"/> to <paramref name="to"/>: the body's arc / S in the flying plane; <paramref name="extend"/> = straight on to the reach (a miss flies on).</summary>
        void BuildFlight(Vector3 from, Vector3 to, bool extend)
        {
            PlaneFor(from, to, out _planeN, out Vector3 lateral);
            _path.Clear();
            float dist = Vector3.Distance(from, to);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / 0.15f), 10, 48);
            float bulge = arcBulge * (body == Body.Metal ? 0.35f : body == Body.Earth ? 0.6f : 1f) * (bite == InkPathBite.Cut ? 0.3f : 1f);
            // seen end-on from the shoulder: the arc opens up on screen (금 and 갓 the least: the straightest stroke, the standing blade)
            float scr = ScreenBulge(from, to) * (body == Body.Metal ? 0.6f : body == Body.Earth ? 0.85f : 1f) * (bite == InkPathBite.Cut ? 0.6f : 1f);
            bulge = Mathf.Max(bulge, scr);
            // ... and then a ribbon laid in a plane through the line of sight is seen edge-on (a hairline): it turns to the camera instead, and the arc opens up and away from
            // the caster's side of the screen (over open ground, not over her own head). 갓's blade keeps its standing plane.
            _faceCam = bite != InkPathBite.Cut && scr > 0.05f;
            if (_faceCam) lateral = ScreenLateral(from, to);
            if (body == Body.Water && bite != InkPathBite.Cut) InkStrokeBuilder.Whip(_path, from, to, lateral, Mathf.Max(whipAmp, scr * 0.7f), 0.15f, n);
            else if (_faceCam) ScreenArc(_path, from, to, lateral, bulge / Mathf.Max(0.5f, Vector3.Distance((from + to) * 0.5f, _cam)), n);
            else InkStrokeBuilder.Arc(_path, from, to, lateral, bulge, n);
            if (extend)
            {
                Vector3 d = (to - from).sqrMagnitude > 1e-6f ? (to - from).normalized : _fwd;
                float far = Mathf.Max(_reach, dist + 1f);
                _path.Add(from + d * far);
            }
            _totalOut = _path.Length; _lenOut = _totalOut;
            _outDir = _path.Dir(_totalOut);
        }

        /// <summary>The sideways bulge (m) the path from <paramref name="from"/> to <paramref name="to"/> still needs so that it spans <see cref="screenSpread"/> of the camera's view: 0 when the line already reads
        /// (a side view, a body off to one side), up to <see cref="maxScreenBulge"/> when it is seen end-on (the body on the shoulder camera's axis).</summary>
        float ScreenBulge(Vector3 from, Vector3 to)
        {
            if (screenSpread <= 0f) return 0f;
            // measured from 30 % along: the first metre leaves the brush over the caster's shoulder, behind her own head on screen (the tip sits ~2 m from the camera), so it does not count as seen
            Vector3 a = Vector3.Lerp(from, to, 0.3f) - _cam, b = to - _cam;
            if (a.sqrMagnitude < 0.01f || b.sqrMagnitude < 0.01f) return 0f;
            float seen = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            float dist = Vector3.Distance((from + to) * 0.5f, _cam);
            return Mathf.Clamp((screenSpread - seen) * dist, 0f, Mathf.Max(0f, maxScreenBulge));
        }

        /// <summary>An end-on flight's path, shaped on SCREEN rather than in the world: the sideways offset is an angle of view (x the camera distance at each point) that flicks out to
        /// <paramref name="thetaMax"/> over the first 30 % (a quarter sine) and then comes back in to the target at an even screen speed. A world sine arc peaks where its tangent runs into
        /// the screen, so the head stood still on screen at the top for ~5 frames and the stroke shrank to a blob there; this one turns at its peak in ~2 frames.</summary>
        void ScreenArc(InkCurve into, Vector3 from, Vector3 to, Vector3 lateral, float thetaMax, int n)
        {
            const float U0 = 0.3f;
            float cap = Mathf.Max(0f, maxScreenBulge);
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                float f = u < U0 ? Mathf.Sin(0.5f * Mathf.PI * u / U0) : 1f - Mathf.Pow((u - U0) / (1f - U0), 1.25f);
                Vector3 p = Vector3.Lerp(from, to, u);
                into.Add(p + lateral * Mathf.Min(cap, thetaMax * f * Vector3.Distance(p, _cam)));
            }
        }

        /// <summary>The bulge's direction for an end-on flight: up (0.5) and out (0.87) toward the screen side away from the caster, square to the line - mostly sideways, so the way in
        /// crosses open ground rather than coming down over the body's own (dark) top.</summary>
        Vector3 ScreenLateral(Vector3 from, Vector3 to)
        {
            Vector3 d = (to - from).sqrMagnitude > 1e-6f ? (to - from).normalized : _fwd;
            Vector3 view = (from + to) * 0.5f - _cam; view = view.sqrMagnitude > 1e-6f ? view.normalized : _fwd;
            Vector3 r = Vector3.Cross(Vector3.up, view); r = r.sqrMagnitude > 1e-6f ? r.normalized : _right;
            Vector3 u = Vector3.Cross(view, r).normalized;
            Vector3 toT = to - _cam, toC = _origin + Vector3.up * 1.2f - _cam;
            float side = (toT.sqrMagnitude > 1e-6f && toC.sqrMagnitude > 1e-6f && Vector3.Dot(r, toT.normalized) < Vector3.Dot(r, toC.normalized)) ? -1f : 1f;
            Vector3 lat = InkMath.Perp(d, u * 0.5f + r * (0.87f * side));
            return lat.y < 0f ? -lat : lat;
        }

        /// <summary>The landing point is known: the part flown so far stays; the rest bends onto <paramref name="e"/> (a guessed aim that already passes within 0.25 m is only trimmed). <paramref name="beyond"/> metres
        /// more are added straight on past it (갓).</summary>
        void RetargetTo(Vector3 e, float beyond)
        {
            float sHead = Mathf.Min(HeadS(_look), _path.Length);
            if (_look <= 0.02f || sHead < 0.05f) BuildFlight(_start, e, false);
            else
            {
                // the guess passes close by: cut it where it is nearest to e
                float bestS = -1f, bestD = 0.25f;
                for (float s = sHead; s <= _path.Length; s += 0.05f) { float d = Vector3.Distance(_path.At(s), e); if (d < bestD) { bestD = d; bestS = s; } }
                if (bestS >= 0f) { _path.TruncateTo(bestS); _path.Add(e); }
                else if (_faceCam && Vector3.Distance(_aim, e) < 1.2f)
                {
                    // 2026-10-31: an end-on flight whose guess landed near the real point: the same screen arc redrawn onto it (a few cm shift) - bending the rest in from the head
                    // ran it straight into the screen, where the head hung still at the top of the arc for ~6 frames before dropping onto the body
                    BuildFlight(_start, e, false);
                }
                else
                {
                    Vector3 h = _path.At(sHead), dIn = _path.Dir(sHead);
                    _path.TruncateTo(sHead);
                    float dist = Vector3.Distance(h, e);
                    _path.AddQuadratic(h, h + dIn * (dist * 0.45f), e, 10, true);
                }
            }
            _lenOut = _path.Length;
            _outDir = _path.Dir(_lenOut);
            if (beyond > 0f) _path.Add(e + _outDir * beyond);
            _totalOut = _path.Length;
        }

        // ------------------------------------------------------------ the head's place

        /// <summary>Metres along the path the head is at look time <paramref name="a"/>: constant speed; 강 after the contact eases out round the loop into the hand.</summary>
        float HeadS(float a)
        {
            a = Mathf.Max(0f, a);
            if (bite != InkPathBite.Return || !_retOn || _tl < 0f) return headSpeed * a;
            if (a <= _tl) return Mathf.Min(headSpeed * a, _lenOut);
            float u = Mathf.Clamp01((a - _tl) / Mathf.Max(0.1f, returnSeconds));
            return _lenOut + _loopLen * (1f - (1f - u) * (1f - u));
        }

        /// <summary>Where the drawn head is (clamped to where the stroke stops for this bite).</summary>
        float FlyHead(float a)
        {
            float s = HeadS(a);
            if (!_hasHit) return Mathf.Min(s, _totalOut);
            if (bite == InkPathBite.Return && _retOn) return Mathf.Min(s, _lenOut + _loopLen);
            if (bite == InkPathBite.Cut) return Mathf.Min(s, _totalOut);
            return Mathf.Min(s, _lenOut);
        }

        float FlyTail(float sHead) => bite == InkPathBite.Return ? 0f : Mathf.Max(0f, sHead - _shape.Length);

        /// <summary>The look time the head stopped (the contact, 갓's end past the body, 강's arrival home, a miss's reach); -1 while it flies.</summary>
        float HeadStopped()
        {
            if (_hasHit)
            {
                if (bite == InkPathBite.Return) return _homeArr;
                if (bite == InkPathBite.Cut) return _totalOut / headSpeed;
                return _tl;
            }
            return _missAt;
        }

        /// <summary>When the flying stroke starts to go (its tail clears and it cracks dry).</summary>
        float FlyGoAt()
        {
            float st = HeadStopped();
            if (st < 0f) return float.MaxValue;
            if (bite == InkPathBite.Cut && _hasHit) return st + 0.15f;
            if (bite == InkPathBite.Return && _hasHit) return st + 0.05f;
            return _hasHit ? st + markSeconds : st + 0.2f;
        }

        void CheckMiss(float a)
        {
            if (_hasHit || _missAt >= 0f) return;
            if (HeadS(a) < _totalOut - 0.02f) return;
            _missAt = a;
            SpellFxHexKit.Spit(_path.Last, Vector3.up);
            Extend(a + 0.2f + drySeconds + 0.6f);
        }

        /// <summary>The curve the flying stroke is drawn on: the path, and for 강 after the contact the path to the landing point and the loop home.</summary>
        InkCurve DrawPath()
        {
            if (!(bite == InkPathBite.Return && _retOn)) return _path;
            _full.Clear();
            for (int i = 0; i < _path.Count; i++) _full.Add(_path[i]);
            for (int i = 1; i < _loop.Count; i++) _full.Add(_loop[i]);
            return _full;
        }

        // ------------------------------------------------------------ hits

        void Primary(SpellFxHit hit, Vector2 feet)
        {
            var body0 = HexHolds.BodyOf(_sheet, hit, feet);
            bool bust = body0.Bust;
            float az, frac;
            switch (bite)
            {
                case InkPathBite.Return: az = 28f; frac = bust ? 0.72f : 0.68f; break;           // the shoulder (R9: the U-turn is here)
                case InkPathBite.Cut: az = 0f; frac = bust ? 0.62f : 0.55f; break;
                default: az = (Rand() < 0.5f ? -1f : 1f) * (bust ? 28f : 10f); frac = bust ? 0.72f : 0.55f; break;     // the chest on the camera's hemisphere; a bust: the kit's lead slot (cheek / shoulder, not the muzzle - judge Q6)
            }
            HexHolds.Surface(_sheet, body0, az, frac, out Vector3 cP, out Vector2 o);
            Vector2 face = HexHolds.TurnToCamera(_sheet, cP, o);
            Vector3 e = PlanW(cP);
            _hasHit = true;
            float beyond = bite == InkPathBite.Cut ? Mathf.Max(0.5f, 1.6f * body0.Radius) + cutThrough : 0f;
            RetargetTo(e, beyond);
            _tl = Mathf.Max(_lenOut / headSpeed, _look);
            float tl = _tl;

            _hold = NewHold("InkHold", body0, hit.Target, e, _right * face.x + _fwd * face.y, _outDir, tl, 1f);
            Vector3 toCasterFlat = InkMath.Flat(_origin - PlanW(new Vector3(body0.Centre.x, 0f, body0.Centre.y)));
            toCasterFlat = toCasterFlat.sqrMagnitude > 1e-4f ? toCasterFlat.normalized : -_fwd;

            At(tl, () => Impact(tl));
            switch (bite)
            {
                case InkPathBite.Once:
                case InkPathBite.Spread:
                    _hold.End = tl + holdSeconds;
                    BodyHold(_hold, frac, Wood ? coilStrands : 0);
                    Wash(body0, toCasterFlat, tl);
                    Extend(tl + holdSeconds + drySeconds + 1f);
                    break;
                case InkPathBite.Cage:
                    _hold.End = tl + cageHoldSeconds;
                    BodyHold(_hold, frac, Wood ? coilStrands : 0);
                    Cage(body0, tl);
                    break;
                case InkPathBite.Cut:
                    _hold.End = tl + cutHoldSeconds;          // the slash mark (NewHold lays it level across the body)
                    Crack(tl);
                    At(tl, () => InkBody.ArmourFlakes(e, _outDir, _cam - e, _accent));
                    float tEnd = _totalOut / headSpeed;
                    At(tEnd, () => SpellFxHexKit.Spit(_path.Last, (_outDir + Vector3.up * 0.3f).normalized));
                    Extend(Mathf.Max(tl + Mathf.Max(crackSeconds, cutHoldSeconds), tEnd + markSeconds) + drySeconds + 1f);
                    break;
                case InkPathBite.Return:
                    _hold.End = tl + holdSeconds;
                    BodyHold(_hold, frac, Wood ? 2 : 0);
                    Wash(body0, toCasterFlat, tl);
                    Return(tl);
                    break;
            }
            if (body == Body.Earth && bite != InkPathBite.Cut)
            {
                // 토: the ground under the struck body lifts as a slab (on the caster's side of it, where the camera sees it)
                _slabC = OnGround(PlanW(new Vector3(body0.Centre.x, 0f, body0.Centre.y)) + toCasterFlat * (body0.Radius * 0.7f), 0.005f);
                _slabT0 = tl;
            }
        }

        HoldSet NewHold(string name, HexBody b, Transform tr, Vector3 e, Vector3 face, Vector3 travel, float t0, float scale)
        {
            var h = new HoldSet
            {
                Layer = new InkLayer(transform, name, material), Body = b, Tr = tr, Anchor = tr != null ? tr.position : Vector3.zero,
                E = e, Face = face.sqrMagnitude > 1e-6f ? face.normalized : -_fwd, Travel = travel.sqrMagnitude > 1e-6f ? travel.normalized : _fwd, T0 = t0, Scale = scale, FlashAt = t0,
            };
            // the mark: the head pasted on the fur, along the stroke's way projected on the face's plane, ending at the contact
            Vector3 dir = h.Travel - h.Face * Vector3.Dot(h.Travel, h.Face);
            if (dir.sqrMagnitude < 0.04f) dir = Vector3.Cross(h.Face, Vector3.up);
            dir.Normalize();
            if (bite == InkPathBite.Cut) { dir = InkMath.Flat(dir).sqrMagnitude > 1e-4f ? InkMath.Flat(dir).normalized : Vector3.Cross(Vector3.up, h.Face).normalized; }
            h.MarkShape = InkBody.Mark(body).Scaled(widthScale * (peak ? 1.3f : 1f) * scale, scale);
            float L = h.MarkShape.Length;
            Vector3 lift = h.Face * 0.03f;                 // 3 cm off: the flying stroke's head stops AT e and both write depth (no z-fight line at the contact)
            h.Mark.Add(e - dir * (L * 0.85f) + lift);
            h.Mark.Add(e - dir * (L * 0.4f) + lift + h.Face * 0.01f);
            h.Mark.Add(e + dir * (L * 0.15f) + lift);
            return h;
        }

        /// <summary>The body's hold: 목 coils <paramref name="strands"/> strands round it (R7), 수 runs 3 lines of ink down it (the tongues, the slab and the blade are drawn from the mark).</summary>
        void BodyHold(HoldSet h, float frac, int strands)
        {
            Vector3 axis = PlanW(new Vector3(h.Body.Centre.x, 0f, h.Body.Centre.y));
            float H = Mathf.Max(0.3f, h.Body.Y1 - h.Body.Y0);
            if (strands > 0)
            {
                for (int k = 0; k < strands; k++)
                {
                    var s = new Strand { T0 = h.T0 + 0.02f + 0.03f * k, Grow = 0.22f + 0.04f * k, Seed = InkMath.Frac(_seed + 0.21f * (k + 1)), Shape = InkBody.Strand(body, h.Scale), Axis = axis };
                    float f0 = Mathf.Clamp(frac + (k - (strands - 1) * 0.5f) * 0.07f, 0.1f, 0.9f);
                    Vector3 st = h.E + Vector3.up * ((f0 - frac) * H);
                    InkStrokeBuilder.Coil(s.Path, _sheet, h.Body, st, f0, coilTurns + 0.08f * k, 0.03f, 0.012f, 1f, 16);
                    h.Strands.Add(s);
                }
            }
            if (body == Body.Water)
            {
                float frac1 = Mathf.Max(0.04f, frac - 0.4f / H);
                Vector3 side = Vector3.Cross(Vector3.up, h.Face).normalized;
                for (int k = 0; k < 3; k++)
                {
                    var s = new Strand { T0 = h.T0 + 0.08f + 0.1f * k, Grow = 0.4f, Seed = InkMath.Frac(_seed + 0.31f * (k + 1)), Shape = InkBody.Strand(body, h.Scale), Axis = axis };
                    InkStrokeBuilder.Run(s.Path, _sheet, h.Body, h.E + side * ((k - 1) * 0.07f), frac1 + 0.05f * k, 6f, s.Seed, 8);
                    var sp = s;
                    s.Done = () => InkBody.RunDrops(sp.Path.Last + h.Delta, _accent);
                    h.Strands.Add(s);
                }
            }
        }

        /// <summary>The contact frame (PLAN 1.5): the stop, the core lit (flying stroke and hold), ink splats on and up, the body's sparks; 토's stones break off.</summary>
        void Impact(float t)
        {
            Stop();
            _flashFly = t;
            if (_hold != null) _hold.FlashAt = t;
            Vector3 e = _hold != null ? _hold.E : _path.At(_lenOut);
            InkBody.Impact(body, e, _outDir, _accent, 1f);
            if (body == Body.Earth) InkBody.StonesOff(e, _outDir, 4, _accent);
        }

        void SecondHit()
        {
            if (_hold == null) return;
            float at = Mathf.Max(_look, _tl + 0.05f);
            _hold.FlashAt = at;
            _ink.Spit(at, _hold.E + _hold.Face * 0.04f, (_hold.Face * 0.8f + Vector3.up * 0.4f).normalized);
        }

        // ---- 가 · 간 · 강 / 갓 ground marks

        void Wash(HexBody b, Vector3 toCaster, float t0)
        {
            Vector3 c = PlanW(new Vector3(b.Centre.x, 0f, b.Centre.y));
            _washFrom = c + toCaster * (b.Radius + 0.1f);
            _washDir = toCaster;
            _washT0 = t0;
        }

        /// <summary>갓: the ground line from under the brush tip to the stroke's end (1 m past the body), grown under the head.</summary>
        void Crack(float tl)
        {
            _crackPts.Clear(); _crackS.Clear();
            float cseed = Rand() * 10f;
            const int N = 30;
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N, s = _totalOut * t;
                Vector3 q = _path.At(s), td = _path.Dir(s);
                Vector3 perp = InkMath.Flat(Vector3.Cross(Vector3.up, td)); if (perp.sqrMagnitude > 1e-6f) perp.Normalize();
                q += perp * ((Mathf.PerlinNoise(t * 40f, cseed) - 0.5f) * 0.06f);
                _crackPts.Add(OnGround(q)); _crackS.Add(s);
            }
        }

        // ---- 각

        void Cage(HexBody b, float tl)
        {
            _cage = new InkLayer(transform, "InkCage", material);
            Vector3 c = PlanW(new Vector3(b.Centre.x, 0f, b.Centre.y));
            float groundY = GroundAt(c);
            float bodyH = Mathf.Max(0.5f, b.Y1 - b.Y0);
            float ringR = MeasureRadius(b, 0.5f) + cageGap;
            float height = Mathf.Clamp(cageHeight, 0.8f, bodyH + 0.5f);
            _poolC = new Vector3(c.x, groundY + 0.01f, c.z); _poolR = ringR + 0.08f; _poolT0 = tl + 0.25f;
            // the coil runs down to the ground (three drips from the contact), then the pool
            Vector3 axis = c;
            for (int k = 0; k < 3; k++)
            {
                var s = new Strand { T0 = tl + 0.22f + 0.03f * k, Grow = 0.16f, Seed = InkMath.Frac(_seed + 0.41f * (k + 1)), Shape = InkBody.Strand(body, 1f), Axis = axis };
                Vector3 st = _hold.E + Vector3.Cross(Vector3.up, _hold.Face).normalized * ((k - 1) * 0.12f);
                InkStrokeBuilder.Run(s.Path, _sheet, b, st, 0.02f, 10f, s.Seed, 8);
                _drips.Add(s);
            }
            // the branches: round the body with a gap toward the camera (the body stays seen), 0.08 s apart
            Vector3 toCam = InkMath.Flat(_cam - c); toCam = toCam.sqrMagnitude > 1e-4f ? toCam.normalized : -_fwd;
            float baseAng = Mathf.Atan2(toCam.z, toCam.x);
            const float OpenDeg = 30f;
            int n = Mathf.Clamp(cageBranches, 4, 12);
            float span = (360f - 2f * OpenDeg) * Mathf.Deg2Rad, step = span / n;
            for (int i = 0; i < n; i++)
            {
                float ang = baseAng + OpenDeg * Mathf.Deg2Rad + (i + 0.5f) * step;
                Vector3 o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var br = new Branch { Born = tl + 0.4f + 0.08f * i, Seed = InkMath.Frac(_seed + 0.137f * (i + 1)) };
                br.Foot = new Vector3(c.x + o.x * ringR, 0f, c.z + o.z * ringR); br.Foot.y = GroundAt(br.Foot) + 0.01f;
                InkStrokeBuilder.Branch(br.Path, br.Foot, o, height * Mathf.Lerp(0.88f, 1.05f, InkMath.Hash(br.Seed * 9f)), br.Seed, 14);
                float far01 = Mathf.Clamp01(Vector3.Distance(br.Foot, _origin) / (_reach + 1f));
                br.LeaveAt = tl + cageStandSeconds + 0.4f * (1f - far01);
                _branches.Add(br);
                if ((i & 1) == 0) _ink.Spit(br.Born, br.Foot + Vector3.up * 0.04f, Vector3.up);
            }
            _cageEnd = tl + cageStandSeconds;
            _hold.FreezeAt = tl + cageHoldSeconds;
            Extend(_cageEnd + 0.4f + drySeconds + 1f);
        }

        /// <summary>The body's real radius at <paramref name="frac"/> of its height: eight surface rays (the bounds' radius runs big).</summary>
        float MeasureRadius(HexBody b, float frac)
        {
            float sum = 0f; int got = 0;
            for (int i = 0; i < 8; i++)
            {
                if (!HexHolds.Surface(_sheet, b, i * 45f, frac, out Vector3 c, out Vector2 o)) continue;
                sum += new Vector2(c.x - b.Centre.x, c.z - b.Centre.y).magnitude - 0.03f; got++;
            }
            return Mathf.Clamp(got >= 4 ? sum / got : b.Radius, 0.25f, 1.2f);
        }

        // ---- 간

        void Neighbour(SpellFxHit hit, Vector2 feet)
        {
            if (_hold == null || _side != null) return;
            HexBody nb = HexHolds.BodyOf(_sheet, hit, feet);
            float t0 = Mathf.Max(_look, _tl + 0.2f);
            Vector3 ca = PlanW(new Vector3(_hold.Body.Centre.x, 0f, _hold.Body.Centre.y)), cb = PlanW(new Vector3(nb.Centre.x, 0f, nb.Centre.y));
            Vector3 dir = InkMath.Flat(cb - ca); if (dir.sqrMagnitude < 1e-4f) return; dir.Normalize();
            Vector3 from = OnGround(ca + dir * (_hold.Body.Radius * 0.9f)), to = OnGround(cb - dir * (nb.Radius * 0.9f));
            _roadLen = Vector3.Distance(InkMath.Flat(from), InkMath.Flat(to));
            _roadPts.Clear();
            int N = Mathf.Clamp(Mathf.CeilToInt(_roadLen / 0.12f), 4, 60);
            for (int i = 0; i <= N; i++) _roadPts.Add(OnGround(Vector3.Lerp(from, to, i / (float)N)));
            _roadT0 = t0;
            _road = new InkLayer(transform, "InkRoad", material);
            float tArr = t0 + _roadLen / Mathf.Max(1f, spreadSpeed);
            // the half-size awl rises from where the bleed arrives into the neighbour's chest (camera hemisphere), then coils it
            float frac = nb.Bust ? 0.66f : 0.5f;
            HexHolds.Surface(_sheet, nb, 0f, frac, out Vector3 cP, out Vector2 o);
            Vector2 face = HexHolds.TurnToCamera(_sheet, cP, o);
            Vector3 e2 = PlanW(cP);
            float tHit = tArr + 0.1f;
            _side = NewHold("InkSide", nb, hit.Target, e2, _right * face.x + _fwd * face.y, (e2 - to).normalized, tHit, Mathf.Clamp(neighbourScale, 0.3f, 1f));
            _side.AwlShape = _shape.Scaled(_side.Scale, _side.Scale);
            _side.AwlShape.Split = Mathf.Min(_side.AwlShape.Split, 3);
            Vector3 root = to + Vector3.up * 0.03f;
            _side.Awl.AddQuadratic(root, root + Vector3.up * ((e2.y - root.y) * 0.9f) - dir * 0.05f, e2, 10, false);
            _side.AwlT0 = tArr; _side.AwlT1 = tHit;
            _side.End = tHit + holdSeconds;
            _side.FlashAt = -1f;
            BodyHold(_side, frac, Wood ? 2 : 0);
            var sd = _side;
            At(tArr, () => _ink.Spit(tArr, root, Vector3.up));
            At(tHit, () => { Stop(); sd.FlashAt = tHit; InkBody.Impact(body, sd.E, sd.Travel, _accent, 0.5f); });
            Extend(Mathf.Max(t0 + roadSeconds, tHit + holdSeconds) + drySeconds + 1f);
        }

        // ---- 강

        void Return(float tl)
        {
            _retOn = true;
            _home = TipWorld();
            Vector3 d = InkMath.Flat(_outDir); if (d.sqrMagnitude < 1e-4f) d = _fwd; d.Normalize();
            Vector3 left = Vector3.Cross(d, Vector3.up).normalized;                 // the loop goes out on the side away from the right-shoulder camera
            if (Vector3.Dot(left, _right) > 0f) left = -left;
            _loopLat = InkMath.Rotate(left, d, -35f).normalized;                    // tilted 35 deg up toward the camera: its depth reads
            if (_loopLat.y < 0f) _loopLat = InkMath.Rotate(left, d, 35f).normalized;
            RebuildLoop();
            _homeArr = tl + Mathf.Max(0.1f, returnSeconds);
            float ha = _homeArr;
            At(ha, () =>
            {
                Stop();
                _flashFly = ha;
                Vector3 dirIn = _loop.Dir(_loopLen);
                InkBody.Impact(body, _home, dirIn, _accent, 0.5f);
                SpellFxHexKit.Spit(_home, (Vector3.up + _fwd * 0.3f).normalized);
            });
            Extend(Mathf.Max(ha + Mathf.Max(0.5f, tailCutSeconds) + drySeconds, tl + holdSeconds + drySeconds) + 1f);
        }

        /// <summary>강's way home: a cubic from the contact out round the side (2 m wide) and back to the brush tip as it is NOW.</summary>
        void RebuildLoop()
        {
            if (_hold == null) return;
            Vector3 p0 = _hold.E, home = _home;
            Vector3 d = _outDir.sqrMagnitude > 1e-6f ? _outDir.normalized : _fwd;
            Vector3 ax = home - p0; float dh = ax.magnitude; ax = dh > 1e-3f ? ax / dh : -d;
            float W = Mathf.Max(0.3f, returnWidth * 0.5f);
            Vector3 p1 = p0 + d * (W * 0.6f) + _loopLat * (W * 1.1f);
            Vector3 p2 = home - ax * (dh * 0.3f) + _loopLat * (W * 0.9f);
            _loop.Clear();
            _loop.AddCubic(p0, p1, p2, home, 24, false);
            _loopLen = _loop.Length;
        }

        /// <summary>강: a lab shot touching the drawn path (within 0.3 m) turns to ink there, a spit, the core flashes.</summary>
        void Shots(float a)
        {
            if (bite != InkPathBite.Return || a < 0.02f || SpellFxLabShot.Active.Count == 0) return;
            // 2026-10-31: as long as the drawn loop stands (its tail clears from FlyGoAt over max(0.5, tailCutSeconds)) - a shot that arrives after the head is home still meets the ink
            // (the 강 shot came in at 8.5 m/s from 7.5 m and reached the drawn path only after the old window, so its look flew on into the camera)
            float until = _homeArr > 0f ? Mathf.Max(_homeArr + 0.15f, FlyGoAt() + Mathf.Max(0.5f, tailCutSeconds) * 0.6f) : _totalOut / headSpeed + 0.3f;
            if (a > until) return;
            var path = DrawPath();
            float sMax = Mathf.Min(FlyHead(a), path.Length);
            if (sMax < 0.1f) return;
            const float Reach = 0.3f;
            for (int i = SpellFxLabShot.Active.Count - 1; i >= 0; i--)
            {
                var shot = SpellFxLabShot.Active[i];
                if (shot == null || !shot.Flying) continue;
                Vector3 q0 = shot.PreviousPosition, q1 = shot.Position;
                float best = Reach; Vector3 bestW = default, bestP = default; bool got = false;
                for (float s = 0f; ; s += 0.2f)
                {
                    Vector3 pw = path.At(Mathf.Min(s, sMax));
                    float dist = PointSegDist(pw, q0, q1, out float t);
                    if (dist < best) { best = dist; bestW = Vector3.Lerp(q0, q1, t); bestP = pw; got = true; }
                    if (s >= sMax) break;
                }
                if (!got) continue;
                Vector3 nrm = bestW - bestP;
                if (nrm.sqrMagnitude < 1e-6f) nrm = -shot.Velocity;
                nrm = nrm.sqrMagnitude > 1e-6f ? nrm.normalized : Vector3.up;
                shot.Dissolve(bestW, nrm, InkBody.Burst(body));
                _ink.Spit(a, bestW, nrm);
                _flashFly = a;
            }
        }

        static float PointSegDist(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
            t = l2 < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return Vector3.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------ per tick

        void Follow(HoldSet h, float a)
        {
            if (h == null) return;
            if (h.Tr != null && a < h.FreezeAt) h.Delta = h.Tr.position - h.Anchor;
        }

        /// <summary>화 in flight: a few ash flakes off the tail (at most 4 calls per flight, one with an ember).</summary>
        void FireAsh(float a)
        {
            if (body != Body.Fire || _ashCount >= 4 || a < _nextAsh) return;
            float sH = FlyHead(a), sT = FlyTail(sH);
            if (sH - sT < 0.3f || HeadStopped() >= 0f) return;
            var path = DrawPath();
            _nextAsh = a + 0.07f;
            InkBody.FlightAsh(path.At(sT + (sH - sT) * 0.25f), path.Dir(sH), _accent, _ashCount == 1);
            _ashCount++;
        }

        void EmitFly(float a)
        {
            _fly.Begin();
            var path = DrawPath();
            float go = FlyGoAt();
            float tailCutLen = bite == InkPathBite.Return ? Mathf.Max(0.5f, tailCutSeconds) : tailCutSeconds;
            if (a <= go + 0.1f + drySeconds + 0.05f)
            {
                var face = _faceCam ? InkFace.Camera(_cam) : InkFace.Plane(_planeN, _cam);
                Vector3? cap = bite == InkPathBite.Return && _retOn && a > _tl ? _home : _start;
                float sH = FlyHead(a), sT = FlyTail(sH);
                bool stonesOn = !_hasHit || a < _tl;
                if (sH - sT > 0.02f)
                {
                    var paint = InkPaint.Main(_seed);
                    InkStrokeBuilder.Ribbon(_fly.Buf, path, sT, sH, _shape, face, paint, cap);
                    InkBody.RideFlight(body, _fly.Buf, path, sT, sH, _shape, face, paint, cap, InkMath.Step(a, 15f), stonesOn, _scratch);
                }
                // afterimages: the same stroke a little behind in time, offset like a slash, fading once they have caught up
                int nAfter = Mathf.Clamp(afterimages + (peak ? 1 : 0), 0, 4);
                float stopped = HeadStopped();
                for (int k = 1; k <= nAfter; k++)
                {
                    float lag = afterLag * k, ak = a - lag;
                    if (ak <= 0.01f) continue;
                    float sHk = FlyHead(ak), sTk = Mathf.Max(0f, sHk - _shape.Length);
                    if (sHk - sTk < 0.05f) continue;
                    float al = afterAlpha * (k == 1 ? 1f : 0.6f / (k - 1));
                    if (stopped >= 0f && ak > stopped) al *= 1f - InkMath.Smooth((ak - stopped) / Mathf.Max(0.05f, afterFade));
                    if (al < 0.02f) continue;
                    float slash = Mathf.Max(afterSlash, afterSpread * Vector3.Distance(_cam, path.At(sHk)));
                    var pk = new InkPaint { Seed = InkMath.Frac(_seed + 0.13f * k), Layer = 0.5f, Alpha = al, CoreMul = k == nAfter ? 0.35f : 0f, WidthMul = 1f - 0.06f * k, Lateral = slash * k };
                    InkStrokeBuilder.Ribbon(_fly.Buf, path, sTk, sHk, _shape, face, pk, cap);
                }
                // the ignition: a needle star at the brush tip for two frames
                if (a < 2f * Frame60 + 1e-4f)
                {
                    Vector3 cr = Vector3.Cross(_planeN, _fwd).normalized, up = Vector3.Cross(_fwd, cr).normalized;
                    for (int k = 0; k < 5; k++)
                    {
                        float ang = k * 72f + 18f + _seed * 40f;
                        Vector3 d = cr * Mathf.Cos(ang * Mathf.Deg2Rad) + up * Mathf.Sin(ang * Mathf.Deg2Rad);
                        InkStrokeBuilder.Needle(_fly.Buf, _start, d, 0.12f + 0.06f * InkMath.Hash(_seed + k), 0.012f, _cam, _seed, 1f);
                    }
                }
            }
            _fly.TailCut = InkMath.Smooth((a - go) / tailCutLen);
            _fly.Dry = InkMath.Smooth((a - go - 0.1f) / Mathf.Max(0.1f, drySeconds));
            _fly.CoreK = CoreK(a, _flashFly);
            _fly.AfterAlpha = 1f;
            _fly.Flush();
        }

        void EmitHold(HoldSet h, float a)
        {
            if (h == null) return;
            var L = h.Layer;
            L.Begin();
            var burst = InkBody.Burst(body);
            float endLen = body == Body.Metal ? HexBodyEnd.Duration(burst) : Mathf.Max(drySeconds, HexBodyEnd.Duration(burst));
            float tEnd = a - h.End;
            if (tEnd > endLen + 0.05f) { L.Hide(); return; }
            // 간's neighbour: the small awl rising out of the bleed into the body
            if (h.AwlT0 >= 0f && a >= h.AwlT0 && h.Awl.Count >= 2)
            {
                float g = InkMath.Smooth(InkMath.Step(a - h.AwlT0, stepHz) / Mathf.Max(0.02f, h.AwlT1 - h.AwlT0));
                float sH = h.Awl.Length * g;
                float goA = h.AwlT1 + markSeconds * 0.6f;
                if (sH > 0.03f && a < goA + 0.3f)
                {
                    var pa = InkPaint.Main(InkMath.Frac(_seed + 0.5f));
                    pa.Alpha = 1f - InkMath.Smooth((a - goA) / 0.3f);
                    InkStrokeBuilder.Ribbon(L.Buf, h.Awl, 0f, sH, h.AwlShape, InkFace.Camera(_cam), pa, null);
                }
            }
            if (a >= h.T0)
            {
                int v0 = L.Buf.Count;
                float u = a - h.T0;
                // the mark (it appears in two frames: the head pasted on)
                float mg = InkMath.Smooth(InkMath.Step(u, stepHz) / 0.05f);
                if (mg > 0.02f)
                {
                    var face = InkFace.Plane(h.Face, _cam);
                    var pm = InkPaint.Main(InkMath.Frac(_seed + 0.7f));
                    InkStrokeBuilder.Ribbon(L.Buf, h.Mark, h.Mark.Length * (1f - mg), h.Mark.Length, h.MarkShape, face, pm, null);
                    if (Wood) InkStrokeBuilder.Thorns(L.Buf, h.Mark, 0f, h.Mark.Length, h.Mark.Length, h.MarkShape, face, pm, 2, pm.Seed, null);
                }
                // strands (coil / runs)
                foreach (var s in h.Strands) EmitStrand(L, s, a);
                // 화: three tongues licking up off the mark while it holds
                if (body == Body.Fire && tEnd < 0f)
                {
                    var ts = InkBody.Tongue(0.07f * h.Scale, 0.55f);
                    float tt = InkMath.Step(a, 15f);
                    for (int k = 0; k < 3; k++)
                    {
                        float grow = InkMath.Smooth((u - 0.03f * k) / 0.15f);
                        if (grow < 0.05f) continue;
                        Vector3 root = h.Mark.At(h.Mark.Length * (0.3f + 0.25f * k));
                        _scratch.Clear();
                        InkStrokeBuilder.Tongue(_scratch, root, -h.Travel + h.Face * 0.3f, (0.36f - 0.05f * k) * h.Scale * grow, _seed * 5f + k * 1.9f, tt, 8);
                        var pt = InkPaint.Main(InkMath.Frac(_seed + 0.23f * (k + 1)));
                        InkStrokeBuilder.Ribbon(L.Buf, _scratch, 0f, _scratch.Length, ts, InkFace.Camera(_cam), pt, null);
                    }
                }
                // the contact's needles: 4 thin lines on along the stroke's way and up, three frames
                if (u < 3f * Frame60 + 1e-4f && h.FlashAt >= 0f)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 d = InkMath.Rotate(h.Travel, Vector3.up, (k - 1.5f) * 22f) + Vector3.up * (0.15f + 0.2f * k);
                        InkStrokeBuilder.Needle(L.Buf, h.E, d, (0.25f + 0.12f * InkMath.Hash(_seed + k * 2f)) * h.Scale, 0.015f, _cam, _seed, 1f);
                    }
                }
                // the end: the body's own (wood threads, fire burns up from its lower edge, earth sinks, metal cut in 5 frames, water slides down)
                float upOff = 0f;
                if (tEnd >= 0f)
                {
                    if (!h.EndDone)
                    {
                        h.EndDone = true;
                        HexBodyEnd.EndCell(burst, h.E + h.Delta, h.Face, 0.22f * h.Scale);
                        if (Wood) foreach (var s in h.Strands) if (s.Path.Count > 2) HexBodyEnd.EndCell(burst, s.Path.At(s.Path.Length * 0.5f) + h.Delta, (s.Path.At(s.Path.Length * 0.5f) - s.Axis).normalized, 0.12f, null, true);
                    }
                    float tp = body == Body.Metal ? tEnd : tEnd * HexBodyEnd.Duration(burst) / endLen;
                    HexEndPose pose = HexBodyEnd.Pose(burst, tp);
                    upOff = pose.Up;
                    VertexEnd(L.Buf, v0, pose);
                    L.Dry = body == Body.Metal ? 0f : InkMath.Smooth(tEnd / endLen);
                }
                else L.Dry = 0f;
                L.Follow(h.Delta + Vector3.up * upOff);
            }
            L.TailCut = 0f;
            L.CoreK = CoreK(a, h.FlashAt);
            L.Flush();
        }

        void EmitStrand(InkLayer L, Strand s, float a)
        {
            if (a < s.T0 || s.Path.Count < 2) return;
            float g = Mathf.Clamp01(InkMath.Step(a - s.T0, stepHz) / Mathf.Max(0.02f, s.Grow));
            float sH = s.Path.Length * g;
            if (g >= 1f) s.Fire();
            if (sH < 0.03f) return;
            var face = s.FaceCam ? InkFace.Camera(_cam) : InkFace.Radial(s.Axis, _cam);
            InkStrokeBuilder.Ribbon(L.Buf, s.Path, 0f, sH, s.Shape, face, InkPaint.Main(s.Seed), null);
        }

        /// <summary>The end pose on every vertex written since <paramref name="from"/>: alpha x the pose's (fire: the burn front climbing from the lowest vertex).</summary>
        static void VertexEnd(InkMeshBuffer buf, int from, in HexEndPose pose)
        {
            if (buf.Count <= from) return;
            float y0 = float.MaxValue, y1 = float.MinValue;
            for (int i = from; i < buf.Count; i++) { float y = buf.V[i].y; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            for (int i = from; i < buf.Count; i++)
            {
                var c = buf.C[i];
                float k = HexBodyEnd.VertexAlpha(pose, HexBodyEnd.HeightFrac(buf.V[i].y, y0, y1));
                c.a = (byte)Mathf.RoundToInt(c.a * Mathf.Clamp01(k));
                buf.C[i] = c;
            }
        }

        void EmitGround(float a)
        {
            _ground.Begin();
            var gface = InkFace.OnGround(_cam);
            float groundGo = -1f;
            // the wash at the foot (가 간 강 각): wet ink 0.6 m toward the caster, 0.12 -> 0.24 m wide, layer 1
            if (_washT0 >= 0f && a >= _washT0)
            {
                float grow = InkMath.Smooth(InkMath.Step(a - _washT0, stepHz) / 0.25f);
                if (grow > 0.02f)
                {
                    // drawn from the far end back to the foot: the wet head (uv0.x = 1) is at the struck body
                    _scratch2.Clear();
                    for (int i = 6; i >= 0; i--) _scratch2.Add(OnGround(_washFrom + _washDir * (washLength * grow * i / 6f), 0.015f));
                    var sh = new InkStrokeShape { HeadW = 0.26f, TorsoW = 0.2f, TailW = 0.12f, HeadZone = 0.3f, TailZone = 0.15f, HeadPoint = 0.08f, HeadRound = true, TailPoint = 0.1f, Wet0 = 1f, Wet1 = 1f, Core = 0.25f };
                    var pw = new InkPaint { Seed = InkMath.Frac(_seed + 0.9f), Layer = 1f, Alpha = 0.6f, CoreMul = 1f, WidthMul = 1f };
                    InkStrokeBuilder.Ribbon(_ground.Buf, _scratch2, 0f, _scratch2.Length, sh, gface, pw, null);
                }
                groundGo = _washT0 + 1.4f;
            }
            // 갓's line under the path, grown under the head (4 cm, layer 1, a little core on its edges for the red ground)
            if (_crackPts.Count > 1 && _tl >= 0f)
            {
                float sLead = FlyHead(a);
                _scratch2.Clear();
                for (int i = 0; i < _crackPts.Count; i++) { if (_crackS[i] > sLead) { if (i > 0) { float f = Mathf.InverseLerp(_crackS[i - 1], _crackS[i], sLead); _scratch2.Add(Vector3.Lerp(_crackPts[i - 1], _crackPts[i], f)); } break; } _scratch2.Add(_crackPts[i]); }
                if (_scratch2.Count > 1)
                {
                    var sh = new InkStrokeShape { HeadW = crackWidth, TorsoW = crackWidth, TailW = crackWidth * 0.4f, HeadZone = 0.1f, TailZone = 0.6f, HeadPoint = 0.12f, TailPoint = 0.3f, FlickHead = 0.4f, Wet0 = 0.2f, Wet1 = 0.5f, Core = 0.35f };
                    var pc = new InkPaint { Seed = InkMath.Frac(_seed + 0.95f), Layer = 1f, Alpha = 0.9f, CoreMul = 1f, WidthMul = 1f };
                    InkStrokeBuilder.Ribbon(_ground.Buf, _scratch2, 0f, _scratch2.Length, sh, gface, pc, null);
                }
                groundGo = _tl + crackSeconds;
            }
            // 토's slab: it lifts 10 cm and settles back (0.6 s), a dark plate; it dries with the rest
            if (_slabT0 >= 0f && a >= _slabT0)
            {
                float u = Mathf.Clamp01((a - _slabT0) / 0.6f);
                float lift = 0.10f * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.6f)) + 0.012f * u;
                InkStrokeBuilder.Slab(_ground.Buf, _slabC, 0.45f, lift, InkMath.Frac(_seed + 0.33f), _cam, 1f);
                if (groundGo < 0f) groundGo = _slabT0 + 1.4f;
            }
            _ground.Dry = groundGo >= 0f ? InkMath.Smooth((a - groundGo) / 0.45f) : 0f;
            _ground.CoreK = 1f;
            if (groundGo >= 0f && a > groundGo + 0.5f) { _ground.Hide(); return; }
            _ground.Flush();
        }

        void EmitCage(float a)
        {
            if (_cage == null) return;
            _cage.Begin();
            var cam = InkFace.Camera(_cam);
            // the pool at the body's foot (layer 1, wet)
            if (_poolT0 >= 0f && a >= _poolT0)
            {
                float g = InkMath.Smooth(InkMath.Step(a - _poolT0, stepHz) / 0.2f);
                if (g > 0.02f) InkStrokeBuilder.Polygon(_cage.Buf, _poolC, Vector3.up, _fwd, _poolR * g, 9, InkMath.Frac(_seed + 0.61f), 1f, 0.25f, 1f, 0.75f);
            }
            foreach (var s in _drips) EmitStrand(_cage, s, a);
            // the branches (극치: the core stands): grow from the pool, thorns, then the far ones end first
            float bodyH = _hold != null ? Mathf.Max(0.5f, _hold.Body.Y1 - _hold.Body.Y0) : 1f;
            var bshape = new InkStrokeShape { HeadW = 0.05f, TorsoW = 0.085f, TailW = 0.12f, HeadZone = 0.4f, TailZone = 0.3f, HeadPoint = 0.22f, TailPoint = 0.03f, FlickHead = 0.4f, Wet0 = 1f, Wet1 = 0.6f, Core = 1f }
                .Scaled(widthScale * (peak ? 1.3f : 1f), 1f);
            int bi = 0;
            foreach (var br in _branches)
            {
                bi++;
                if (a < br.Born) continue;
                float g = Mathf.Clamp01(InkMath.Step(a - br.Born, stepHz) / br.Grow);
                float sH = br.Path.Length * g;
                if (sH < 0.04f) continue;
                int v0 = _cage.Buf.Count;
                var pb = InkPaint.Main(br.Seed);
                InkStrokeBuilder.Ribbon(_cage.Buf, br.Path, 0f, sH, bshape, cam, pb, null);
                if (g > 0.5f) InkStrokeBuilder.Thorns(_cage.Buf, br.Path, 0.15f, sH, sH, bshape, cam, pb, 3, br.Seed, null);
                // three thin threads wavering up from three branch tips (at most half the body's height over the cage)
                if (g >= 1f && (bi == 2 || bi == 5 || bi == 7) && a > br.Born + br.Grow + 0.3f)
                {
                    float tg = InkMath.Smooth((a - br.Born - br.Grow - 0.3f) / 0.4f);
                    _scratch.Clear();
                    InkStrokeBuilder.Tongue(_scratch, br.Path.Last, Vector3.zero, Mathf.Min(0.45f, bodyH * 0.5f) * tg, br.Seed * 11f, a * 0.35f, 8);
                    var pt = new InkPaint { Seed = br.Seed, Layer = 0f, Alpha = 1f, CoreMul = 0f, WidthMul = 1f };
                    InkStrokeBuilder.Ribbon(_cage.Buf, _scratch, 0f, _scratch.Length, InkBody.Tongue(0.014f, 0f), cam, pt, null);
                }
                if (a > br.LeaveAt)
                {
                    if (!br.EndDone) { br.EndDone = true; HexBodyEnd.EndCell(SpellFxBurstBody.Wood, br.Path.At(br.Path.Length * 0.6f), (_cam - br.Foot).normalized, 0.15f, null, true); }
                    HexEndPose pose = HexBodyEnd.Pose(SpellFxBurstBody.Wood, (a - br.LeaveAt) * HexBodyEnd.Duration(SpellFxBurstBody.Wood) / Mathf.Max(drySeconds, 0.3f));
                    VertexEnd(_cage.Buf, v0, pose);
                }
            }
            _cage.CoreK = 1f;
            _cage.Dry = InkMath.Smooth((a - _cageEnd) / Mathf.Max(0.1f, drySeconds + 0.4f));
            if (a > _cageEnd + 0.4f + drySeconds + 0.1f) { _cage.Hide(); return; }
            // the cage stays where it stood once the hold lets go (the body may leave)
            if (_hold != null) _cage.Follow(_hold.Delta);          // Follow() stops updating Delta at FreezeAt
            _cage.Flush();
        }

        void EmitRoad(float a)
        {
            if (_road == null || _roadT0 < 0f || a < _roadT0) return;
            _road.Begin();
            float got = Mathf.Clamp01(InkMath.Step(a - _roadT0, stepHz) * Mathf.Max(1f, spreadSpeed) / Mathf.Max(0.1f, _roadLen));
            int n = Mathf.Max(2, Mathf.CeilToInt(got * (_roadPts.Count - 1)) + 1);
            if (got > 0.02f && _roadPts.Count > 1)
            {
                _scratch2.Clear();
                for (int i = 0; i < n && i < _roadPts.Count; i++) _scratch2.Add(_roadPts[i]);
                var sh = new InkStrokeShape { HeadW = spreadWidth * 1.15f, TorsoW = spreadWidth, TailW = spreadWidth * 0.8f, HeadZone = 0.3f, TailZone = 0.3f, HeadPoint = 0.12f, HeadRound = true, TailPoint = 0.1f, Wet0 = 1f, Wet1 = 1f, Core = 0.25f };
                var pr = new InkPaint { Seed = InkMath.Frac(_seed + 0.77f), Layer = 1f, Alpha = 0.7f, CoreMul = 1f, WidthMul = 1f };
                InkStrokeBuilder.Ribbon(_road.Buf, _scratch2, 0f, _scratch2.Length, sh, InkFace.OnGround(_cam), pr, null);
            }
            float go = _roadT0 + roadSeconds;
            _road.Dry = InkMath.Smooth((a - go) / Mathf.Max(0.1f, drySeconds));
            _road.CoreK = 1f;
            if (a > go + drySeconds + 0.1f) { _road.Hide(); return; }
            _road.Flush();
        }
    }
}
