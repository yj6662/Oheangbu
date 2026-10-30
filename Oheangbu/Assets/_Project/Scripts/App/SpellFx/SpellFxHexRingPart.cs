using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅜ 둘레 in the hex-cell membrane grammar (the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"; the ㅗ 면 trial passed the Fable gate, D308-157; PLAN-308 §2.2): the ring round the caster
    /// (the rule's ring: radius = the band's centre line) as a LOW WALL OF STANDING CELLS - a cell lying on the ground gets a thick seam and a flat pattern at the shoulder camera (ㅗ F1 - F5). Shader
    /// Oheangbu/SpellFx/HexDome, material = the ㅜ ward's glass. Told apart from the dome at a glance (the Fable r1 judgements: not read as a shield, direction right): knee-to-waist high, ONE row of cells
    /// 0.22 - 0.25 m round, open on top, a gap between cells (centre spacing = 1.25 x the cell), each cell leaning 10 - 15 deg outward at its own height (+- 0.1 m), no foot ring (the frame is upright and sunk,
    /// and _BaseRingK is set to 0.2 where the material has it), nothing continuous. The five final-consonant verbs of the wood body (국 극치 / 군 번진다 / 굼 심는다 / 굿 가른다 / 궁 돌아온다) and the
    /// ordinary 남긴다 (눅, <see cref="HexRingBite.Leave"/>) are <see cref="HexRingBite"/>. Built on <see cref="SpellFxHexKit"/> (the kit the other membrane parts share; this part only uses what it offers):
    ///  - <see cref="HexSheet.EmitStandingEx"/>: a standing cell with its own shading normal (|n.v| 0.5 - 0.75: the dome shader thins the fill of a cell facing the camera to ~0.1), a corner turn and a per-corner
    ///    vertex alpha (the cell's end, <see cref="HexBodyEnd"/>); <see cref="HexFlashSlots"/>: the shader's four extra struck-cell slots - a reaction is the accent seam for a few frames and a 0.15 s decay, never
    ///    the single legacy slot's white plate cut hard by the next flash; <see cref="HexSheet.EmitStrand"/>: threads and strands that face the camera.
    /// Common grammar (look time, from the launch): the wall comes FIRST - two cells fly from the brush tip to the start point (0.10 s; small at the hand, a spit of ink within 0.15 m of the tip, thrown away from
    /// the caster), then the cells rise out of the ground round both ways in 0.25 s (the drawing front = the cells), the ones within 50 deg of the camera's side left out (that side of the ring is 0.8 m in front of
    /// the shoulder camera); only then the verb. A body's reaction (flash, hold, spit, 2-frame look hit-stop) comes after the wall is closed, at the frame the visible cells pass it. The breath is 1.5 Hz +- 12 %;
    /// the patterns pulse at the slowest bloom the shader has (1.5 Hz in two layers). An end is each cell's own, the last drawn first: a 2-frame flash, then the body's ending (<see cref="HexBodyEnd"/>: wood =
    /// the cell narrows and drifts out while 2 - 3 short ink strands run outward and a few small ink drops leave; the others burst their way). The brush, throw, rise and contact ink is always the small plain ink.
    /// Seeds and pillars are set clear of a body that stands on the ring (the cell is moved up to +- 25 deg along the ring). Look only - nothing here touches a rule.
    /// Verbs (look time from the launch):
    ///  국 Pillar: from 0.65 s 6 - 8 pillars (0.08 s apart, left to right) stack on wall cells: two standing cells (a cell every 0.15 s, edge to edge, no overlap) and a flat hexagon laid on top as the footing
    ///    (about 1.5 m), a small spit at the foot as they stack; kept to 9.5 s, then the far ones go first.
    ///  눅 Leave (the ordinary 남긴다, fire): the wall with 3 - 4 clumps of one or two cells stacked on it (no cap), kept to 9.5 s.
    ///  군 Spread: the wall stands (to 2.6 s) and STAYS. A body that crosses sends 3 - 4 NEW cells out of the wall at its bearing (they do not use wall cells), 0.3 s after the wall is drawn, one after another (0.1 s
    ///    apart, 0.25 s each, apex 0.6 m) along its way outward - to its feet if it is beyond the wall, else through it and 2.2 m past the wall - and float there at knee height for 3 s; the last one lands on the body.
    ///  굼 Seed: no wall; 8 - 10 hanging eggs along the front arc (floating at 0.42 m +- 0.1, turned tangent to the ring, leaning +- 10 deg, a slow sway, growing 5 % over 8 s) on a thin, slightly curved ink
    ///    thread to the ground with a small ink mark at its end and a drop now and then, thrown one after another from the brush tip (0.3 + 0.15 k s, 0.11 s in the air); 0.8 Hz pulse +- 8 %. A body crossing
    ///    makes the 3 nearest eggs fly to its surface and become its hold.
    ///  굿 Cut: the wall is a row of blades (pointy hexagons 1.3 x tall and narrow, a cream edge along the two upper sides, leaning 25 deg out) standing on a dark crack in the ground (4 - 8 cm, irregular, a cream
    ///    edge inside and a bleed outside, at the blades' foot radius) drawn with them; a body crossing it loses plates (torn armour plates in the body's own edge colour) and keeps two hold cells.
    ///  궁 Return: born at 2 m (a flash and a spit on the bodies there as the wall closes), the cells spread apart out to 4 m (0.4 - 0.9 s, a third bigger), stay at the peak 0.3 s and come home in 0.8 s (2.0 s: ease-in,
    ///    fast at the end); the out and back flashes are tied to the moment the VISIBLE cells pass a body; at 2.0 s every cell flashes at once, a spit at the caster's feet and a 2-frame hit-stop.</summary>
    public sealed class SpellFxHexRingPart : MonoBehaviour, ISpellFxPart
    {
        /// <summary>국 Pillar · 군 Spread · 굼 Seed · 굿 Cut · 궁 Return · 눅 Leave (the ordinary 남긴다; appended last so the older prefabs keep their value).</summary>
        public enum HexRingBite { Pillar, Spread, Seed, Cut, Return, Leave }

        [SerializeField] Material material;
        [SerializeField] HexRingBite bite = HexRingBite.Pillar;
        [Tooltip("What the ring's END is made of (<HexBodyEnd>: wood = the cell narrows, short ink strands, a few small drops; the others burst their own way). The brush / throw / rise / contact spits are always the small plain ink.")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Water;
        [Tooltip("A wall cell's circumradius in metres, drawn between these (0.22 - 0.25).")]
        [SerializeField] float cellRadiusMin = 0.22f, cellRadiusMax = 0.25f;
        [Tooltip("Centre spacing = this x the cell's width (a gap between cells; no continuous honeycomb).")]
        [SerializeField] float gapFactor = 1.25f;
        [Tooltip("The wall cells' centre height above the ground (m) and its +- scatter: tops at 0.35 - 0.8 m.")]
        [SerializeField] float wallHeight = 0.40f, heightJitter = 0.10f;
        [Tooltip("Each wall cell leans outward by a degree between these (10 - 15).")]
        [SerializeField] float leanMinDeg = 10f, leanMaxDeg = 15f;
        [Tooltip("The cells within this many degrees of the camera's side are left out.")]
        [SerializeField] float cameraCone = 50f;
        [Tooltip("Used when the cast carries no spec: the ring's centre line (metres).")]
        [SerializeField] float defaultRadius = 2f;
        [Tooltip("The first cells fly from the brush tip to the ring's start (s); the wall is drawn round in closeSeconds.")]
        [SerializeField] float leadSeconds = 0.10f, closeSeconds = 0.25f;
        [Tooltip("궁: how far out the ring is pushed (the rule's space.returnRing.outer - copied from the data, not read) and the look timeline: out at / for, stay, back.")]
        [SerializeField] float returnOuter = 4f, returnOutAt = 0.4f, returnOutSeconds = 0.5f, returnStaySeconds = 0.3f, returnBackSeconds = 0.8f;
        [Tooltip("Metres a seed / pillar / clump may be moved along the ring (as an angle, radians) to be clear of a body standing on it: tries 0, +25 deg, -25 deg, +50 deg, -50 deg.")]
        [SerializeField] float clearAngle = 0.436f;
        [SerializeField] bool followGround = true;

        static readonly int[] PatChoices = SpellFxHexKit.PatChoices;
        static float Smooth(float x) => SpellFxHexKit.Smooth(x);
        const float Sqrt3Half = 0.8660254f;

        // ------------------------------------------------------------ state

        /// <summary>Every standing cell of the ring: a wall cell, a stacked cell, an egg, a cell that has gone out on a road; a lying cap on a pillar.</summary>
        sealed class Piece
        {
            public Vector2 Pos, Out;            // plan x, z of its centre on the ring (the wall's place) and the unit direction out
            public float Y, RiseFrom;           // centre height above the ground when up / at the start of its rise (0 = the centre at the ground)
            public float Rx, Rv, Lean, CornerRot, Yaw;      // horizontal / vertical corner radius, lean (rad, top outward), the corner turn (pi/2 = a pointy top), a turn about the vertical
            public float Theta, Phase, Hash, Ground;
            public int Pat;
            public float Born = -1f, Pop = 0.18f, LeaveAt = float.MaxValue, UsedAt = -1f, Grow;
            public bool Wall, Seed, Cap, Hidden, EndStarted, Blade;
            public float HopT0, HopT1; public Vector3 HopFrom, HopTo;       // 군: it leaves the wall and lands at HopTo
            public Vector2 CapDir;                                           // the footing's flat sides run along this (plan x, z)
            public float ThreadPhase; public Vector2 ThreadFoot;
        }

        readonly List<Piece> _pieces = new List<Piece>();
        readonly List<HexPlate> _plates = new List<HexPlate>();          // the hold cells on bodies
        readonly List<HexFlight> _flights = new List<HexFlight>();
        readonly HexFlashSlots _flash = new HexFlashSlots();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly HashSet<long> _seen = new HashSet<long>(), _returned = new HashSet<long>();
        readonly Dictionary<long, List<HexPlate>> _holds = new Dictionary<long, List<HexPlate>>();
        readonly Collider[] _overlap = new Collider[16];
        readonly List<Vector3> _pts = new List<Vector3>(8);

        HexSheet _sheet;
        System.Random _rng;
        bool _built, _launched, _ended, _tipSpat;
        float _age, _real, _look, _stop;
        Vector3 _origin, _fwd, _right;
        float _R, _a0, _camAng, _thetaMax, _fadeStart, _spread = 0.5f, _endAt, _lead, _wallDone, _seedLast = 1f, _boostAt = -10f, _backStart, _backEnd;
        int _hopWave;
        Vector2? _primaryFeet;                      // 군: where the first body stands (a neighbour's road starts there)

        // ------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast) { _rng = new System.Random((int)(cast.CastId & 0x7fffffff)); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            if (_sheet != null) _sheet.SetActive(true);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;
            _ended = true;
            if (_sheet != null) _sheet.SetActive(false);
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            if (!_launched || _ended || _sheet == null) { _real = ageSeconds; return; }
            // the look clock: it follows the cast's age but stands still for the 2-frame hit-stop a reaction asks for (the shader's own clock and the rule's keep running)
            float dt = Mathf.Max(0f, ageSeconds - _real); _real = ageSeconds;
            if (_stop > 0f) { float used = Mathf.Min(dt, _stop); _stop -= used; dt -= used; }
            _look += dt; _age = _look;
            float a = _look;
            _sheet.Main.SetClock(ageSeconds, 0f); _sheet.Air.SetClock(ageSeconds, 0f);
            if (a > _endAt) { if (_sheet.Main.Go.activeSelf) _sheet.SetActive(false); return; }
            _ink.Drain(a);
            _flash.Drive(_sheet, a);
            if (_flash.TakeStop()) _stop = Mathf.Max(_stop, 2f / 60f);
            float boost = a >= _boostAt && a < _boostAt + 0.15f ? 1f + 1.4f * Mathf.Exp(-(a - _boostAt) * 25f) : 1f;     // 궁: every cell at once
            _sheet.Main.Breathe(ageSeconds, 1.5f, 0.12f, boost); _sheet.Air.Breathe(ageSeconds, 1.5f, 0.12f);
            _sheet.Age = a;
            Rebuild(a);
        }

        void OnDestroy()
        {
            if (_sheet != null) _sheet.Destroy();
        }

        // ------------------------------------------------------------ build

        float Rand() => (float)_rng.NextDouble();
        float PickHash(float phase) => SpellFxHexKit.PickHashSlow(_rng, phase);
        int PickPat() => SpellFxHexKit.PickPat(_rng, burstBody == SpellFxBurstBody.Wood ? 2 : -1);          // the wood atlas' slot 2 is the lotus: a solid pale plate at a small cell
        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;
        float GroundAt(Vector2 p) => _sheet.Ground.At(p);
        static Vector2 Dir(float phi) => new Vector2(Mathf.Sin(phi), Mathf.Cos(phi));          // a bearing (0 = ahead, + = right) as plan x, z
        static float Bearing(Vector2 p) => Mathf.Atan2(p.x, p.y);

        /// <summary>Angle of a plan point from the ring's start point, 0 .. pi.</summary>
        float ThetaOf(Vector2 p)
        {
            if (p.sqrMagnitude < 1e-4f) return 0f;
            return Mathf.Abs(Mathf.DeltaAngle(Bearing(p) * Mathf.Rad2Deg, _a0 * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
        }

        bool InCone(float phi) => Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, _camAng * Mathf.Rad2Deg)) < cameraCone;
        float ThetaN(float theta) => Mathf.Clamp01(theta / _thetaMax);

        /// <summary>When the drawing front reaches the angle <paramref name="theta"/> from the start.</summary>
        float BornOf(float theta) => _lead + closeSeconds * ThetaN(theta);

        /// <summary>When a cell at angle <paramref name="theta"/> starts to leave: the last drawn goes first.</summary>
        float LeaveOf(float theta) => _fadeStart + (1f - ThetaN(theta)) * _spread;

        /// <summary>When a body's reaction may show: after the wall is closed AND where the drawing has reached it.</summary>
        float ReactAt(float theta) => Mathf.Max(_age, Mathf.Max(BornOf(theta) + 0.1f, _wallDone + 0.05f));

        // 궁: how far out the visible ring is, 0 .. 1 of the way to returnOuter (out: smooth, stay, back: ease-in - slow away, fast home)
        float Prof(float a)
        {
            float outEnd = returnOutAt + returnOutSeconds, backStart = outEnd + returnStaySeconds;
            if (a < returnOutAt) return 0f;
            if (a < outEnd) return Smooth((a - returnOutAt) / returnOutSeconds);
            if (a < backStart) return 1f;
            float u = Mathf.Clamp01((a - backStart) / returnBackSeconds);
            return 1f - u * u;
        }
        float RScale(float a) => (_R + (returnOuter - _R) * Prof(a)) / _R;

        /// <summary>궁: the look time at which the visible ring, on its way OUT, reaches the radius <paramref name="d"/> (a body on the ring: the moment the wall is closed).</summary>
        float OutTimeFor(float d)
        {
            float rr = d / _R, maxS = returnOuter / _R;
            if (rr <= 1.02f) return _wallDone + 0.05f;
            if (rr >= maxS) return returnOutAt + returnOutSeconds;
            float target = (rr - 1f) / (maxS - 1f), lo = 0f, hi = 1f;
            for (int i = 0; i < 14; i++) { float mid = 0.5f * (lo + hi); if (Smooth(mid) < target) lo = mid; else hi = mid; }
            return returnOutAt + returnOutSeconds * hi;
        }

        /// <summary>궁: the look time at which the visible ring, coming BACK, passes the radius <paramref name="d"/> (a body on the ring: the arrival).</summary>
        float BackTimeFor(float d)
        {
            float rr = d / _R, maxS = returnOuter / _R;
            if (rr <= 1.02f) return _backEnd;
            if (rr >= maxS) return _backStart;
            float prof = (rr - 1f) / (maxS - 1f);
            return _backStart + returnBackSeconds * Mathf.Sqrt(Mathf.Clamp01(1f - prof));
        }

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            var spec = cast.Spec;
            _R = spec != null && spec.RingRadius > 0.5f ? spec.RingRadius : defaultRadius;
            _lead = leadSeconds; _wallDone = _lead + closeSeconds;
            _spread = 0.5f;
            _backStart = returnOutAt + returnOutSeconds + returnStaySeconds; _backEnd = _backStart + returnBackSeconds;
            switch (bite)
            {
                // the rule's lives (the spec): 국 / 눅 10 s, 군 the ring 2.2 s + 3 s marks, 굼 8 s, 굿 10 s - the break-up starts 0.5 s before so the last cell is gone at about that time
                case HexRingBite.Pillar: case HexRingBite.Leave: case HexRingBite.Cut: _fadeStart = 9.5f; break;
                case HexRingBite.Spread: _fadeStart = 2.6f; break;
                case HexRingBite.Seed: _fadeStart = 7.8f; break;
                default: _fadeStart = _backEnd + 0.3f; _spread = 0.4f; break;          // 궁: home at 2.0 s, the wall stands 0.3 s more
            }
            _endAt = _fadeStart + _spread + 0.25f + HexBodyEnd.Duration(burstBody) + 0.4f;

            // an UPRIGHT frame sunk 1 m (the cells stand all round the caster; the shader's reveal would discard the half behind a lying object, and its base-ring term is 0 here); the brush tip is sampled in the sheet's constructor
            _sheet = new HexSheet(transform, cast, material, HexFrame.UprightSunk(_origin, _right, _fwd, 1f), 0f, followGround, "HexRing");
            _sheet.Main.VertexAlpha = true;                                  // a cell's end is a per-cell alpha (HexBodyEnd)
            _sheet.Main.SetFloatIfAny("_BaseRingK", 0.2f);                   // the wall has no bright foot
            _sheet.Air.SetFloatIfAny("_BaseRingK", 0f);
            _sheet.Air.SetFloatIfAny("_BloomBoost", 1f);                     // a thrown cell is dark glass from its first frame, never a white flash
            // the start point: away from the camera (the far side), else ahead
            Vector2 away = -_sheet.ToCamPlan;
            if (away.sqrMagnitude < 0.04f) away = new Vector2(0f, 1f);
            _a0 = Bearing(away);
            _camAng = _a0 + Mathf.PI;
            _thetaMax = Mathf.Max(1f, Mathf.PI - cameraCone * Mathf.Deg2Rad);

            if (bite == HexRingBite.Seed) { PickSeeds(); MakeFlights(); }
            else
            {
                BuildWall();
                if (bite == HexRingBite.Pillar) PickStacks(6 + _rng.Next(3), 2, true);
                else if (bite == HexRingBite.Leave) PickStacks(3 + _rng.Next(2), 1 + _rng.Next(2), false);
                MakeFlights();
                if (bite == HexRingBite.Return)
                {
                    // home at 2.0 s: every cell flashes at once (the sheet's seam and pattern boost), a spit at the caster's feet, the start cell takes the shader's flash, a 2-frame look hit-stop
                    _boostAt = _backEnd;
                    _ink.Spit(_backEnd, PlanW(new Vector3(0f, GroundAt(Vector2.zero) + 0.06f, 0f)), Vector3.up);
                    Piece first = null;
                    foreach (var p in _pieces) if (p.Wall && (first == null || p.Theta < first.Theta)) first = p;
                    Schedule(first, _backEnd);
                }
            }
            _built = true;
        }

        /// <summary>The wall: single standing cells round the ring, centre spacing gapFactor x the cell's width, out of the camera's cone. 굿: blades - pointy hexagons, narrow and tall, leaning 25 deg, their
        /// foot on the ring's radius.</summary>
        void BuildWall()
        {
            float rcMid = 0.5f * (cellRadiusMin + cellRadiusMax);
            int n = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * _R / (gapFactor * 2f * rcMid)));
            float dphi = 2f * Mathf.PI / n;
            bool blade = bite == HexRingBite.Cut;
            for (int k = 0; k < n; k++)
            {
                float phi = _a0 + (k - (n - 1) * 0.5f) * dphi;
                if (InCone(phi)) continue;
                Vector2 o = Dir(phi);
                float rc = Mathf.Lerp(cellRadiusMin, cellRadiusMax, Rand());
                var p = new Piece { Out = o, Wall = true, Phase = Rand() * 6.28f, Blade = blade };
                if (blade)
                {
                    p.Rx = 0.62f * rc; p.Rv = 1.2f * rc; p.CornerRot = Mathf.PI * 0.5f; p.Lean = 25f * Mathf.Deg2Rad;
                    p.Pos = o * (_R + p.Rv * Mathf.Sin(p.Lean));              // the lowest corner is on the ring's radius
                    p.Y = p.Rv * Mathf.Cos(p.Lean) + 0.03f;
                }
                else
                {
                    p.Rx = rc; p.Rv = rc; p.Pos = o * _R;
                    p.Y = wallHeight + (Rand() * 2f - 1f) * heightJitter;
                    p.Lean = Mathf.Lerp(leanMinDeg, leanMaxDeg, Rand()) * Mathf.Deg2Rad;
                }
                p.Hash = PickHash(Rand()); p.Pat = PickPat();
                p.Theta = ThetaOf(p.Pos); p.Ground = GroundAt(p.Pos);
                p.Born = BornOf(p.Theta); p.LeaveAt = LeaveOf(p.Theta) + Rand() * 0.08f;
                _pieces.Add(p);
            }
        }

        /// <summary>Is a body (a rigid body or a character controller - the kit's "not ground") standing within half a metre of this ring point at chest height?</summary>
        bool BodyAt(Vector2 pos, float ground)
        {
            Vector3 w = PlanW(new Vector3(pos.x, ground + 0.7f, pos.y));
            int n = Physics.OverlapSphereNonAlloc(w, 0.5f, _overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _overlap[i];
                if (c != null && (c.attachedRigidbody != null || c is CharacterController)) return true;
            }
            return false;
        }

        /// <summary>The ring angle nearest to <paramref name="t"/> (an offset from the start) that has no body on it: moved by up to +- 25 deg, +- 50 deg along the ring, else as it was.</summary>
        float ClearT(float t, float radius)
        {
            float[] tries = { 0f, 1f, -1f, 2f, -2f };
            for (int i = 0; i < tries.Length; i++)
            {
                float tt = t + tries[i] * clearAngle;
                Vector2 pos = Dir(_a0 + tt) * radius;
                if (!BodyAt(pos, GroundAt(pos))) return tt;
            }
            return t;
        }

        /// <summary>굼: 8 - 10 hanging eggs over the front arc (+- 109 deg of the start; moved clear of a body), floating at knee height, turned tangent to the ring and leaning a little either way, planted nearest the
        /// start first, each with its thread's foot.</summary>
        void PickSeeds()
        {
            int want = 8 + _rng.Next(3);
            var list = new List<Piece>();
            for (int k = 0; k < want; k++)
            {
                float t = ClearT(((k + 0.5f) / want - 0.5f) * 3.8f + (Rand() - 0.5f) * 0.12f, _R);
                Vector2 o = Dir(_a0 + t);
                float rc = 0.25f + 0.02f * Rand();
                var p = new Piece { Out = o, Seed = true, Phase = Rand() * 6.28f, Rx = rc, Rv = rc, Y = 0.42f + (Rand() * 2f - 1f) * 0.1f, Lean = (Rand() * 2f - 1f) * 10f * Mathf.Deg2Rad };
                p.Pos = o * (_R + (Rand() - 0.5f) * 0.2f);
                p.Hash = PickHash(Rand()); p.Pat = PickPat();
                p.Theta = ThetaOf(p.Pos); p.Ground = GroundAt(p.Pos);
                p.LeaveAt = _fadeStart + Rand() * 0.4f;
                p.ThreadPhase = Rand() * 6.28f;
                list.Add(p);
            }
            list.Sort((p, q) => p.Theta.CompareTo(q.Theta));
            for (int k = 0; k < list.Count; k++)
            {
                var p = list[k];
                p.Born = 0.3f + 0.15f * k;
                _pieces.Add(p);
                // a drop of ink now and then where the thread meets the ground
                int drops = 2 + _rng.Next(2);
                for (int d = 0; d < drops; d++) _ink.Drip(1.2f + Rand() * (_fadeStart - 2f), PlanW(new Vector3(p.Pos.x, p.Ground + 0.04f, p.Pos.y)));
            }
            _seedLast = list.Count > 0 ? list[list.Count - 1].Born : 0.6f;
        }

        /// <summary>국 / 눅: <paramref name="count"/> places over the front arc (left to right, 0.08 s apart; the start point itself free - a body is set there to test the ring; moved clear of a body) where cells
        /// are stacked ON a wall cell: <paramref name="stack"/> standing cells (a cell every 0.15 s, edge to edge with the one below, no overlap) and, for a pillar, a flat hexagon laid on top as the footing.
        /// A small spit at the foot as the first and the last come. They go the far ones first, the top cell first.</summary>
        void PickStacks(int count, int stack, bool cap)
        {
            var offs = new List<float> { -1.75f, -1.3f, -0.85f, -0.4f, 0.4f, 0.85f, 1.3f, 1.75f };
            while (offs.Count > count) offs.RemoveAt(_rng.Next(offs.Count));
            offs.Sort();
            int order = 0;
            foreach (float t0 in offs)
            {
                float t = ClearT(t0, _R);
                Piece wall = null; float bd = float.MaxValue;
                foreach (var q in _pieces)
                {
                    if (!q.Wall) continue;
                    float d = Mathf.Abs(Mathf.DeltaAngle(Bearing(q.Pos) * Mathf.Rad2Deg, (_a0 + t) * Mathf.Rad2Deg));
                    if (d < bd) { bd = d; wall = q; }
                }
                if (wall == null) continue;
                float start = _wallDone + 0.3f + 0.08f * order++;
                float leave = LeaveOf(wall.Theta);
                float rx = wall.Rx * (0.92f + 0.16f * Rand()), h = 2f * Sqrt3Half * rx * 1.04f;          // the cell's height + a hair of gap: edge to edge
                float lean = wall.Lean * 0.5f, sl = Mathf.Sin(lean), cl = Mathf.Cos(lean);
                Vector3 ctr = new Vector3(wall.Pos.x, wall.Y, wall.Pos.y);                                  // the wall cell under it
                Vector3 outW = new Vector3(wall.Out.x, 0f, wall.Out.y);
                float topY = wall.Y + Sqrt3Half * wall.Rv * Mathf.Cos(wall.Lean);
                Vector3 tc = new Vector3(wall.Pos.x, topY, wall.Pos.y) + outW * (Sqrt3Half * wall.Rv * Mathf.Sin(wall.Lean));          // the top edge of the wall cell
                for (int j = 1; j <= stack; j++)
                {
                    // the cell's centre half a height up its own (leaning) plane from the edge it sits on
                    Vector3 c = tc + (Vector3.up * cl + outW * sl) * (h * 0.5f);
                    var p = new Piece { Pos = new Vector2(c.x, c.z), Out = wall.Out, Rx = rx, Rv = rx, Y = c.y, Lean = lean, Ground = wall.Ground, Theta = wall.Theta, Phase = Rand() * 6.28f };
                    p.Y = c.y; p.RiseFrom = c.y - 0.18f;
                    p.Hash = PickHash(Rand()); p.Pat = PickPat();
                    p.Born = start + 0.15f * (j - 1);
                    p.LeaveAt = leave + 0.04f * (stack + 1 - j);
                    _pieces.Add(p);
                    if (j == 1 || (j == stack && !cap)) _ink.Spit(p.Born, PlanW(new Vector3(wall.Pos.x, wall.Ground + 0.05f, wall.Pos.y)), (Vector3.up + outW).normalized);
                    tc = c + (Vector3.up * cl + outW * sl) * (h * 0.5f);
                }
                if (cap)
                {
                    // the footing: a hexagon laid flat on the top edge, its flat sides along the wall's direction
                    float capR = 0.30f;
                    var cp = new Piece { Cap = true, Pos = new Vector2(tc.x, tc.z), Out = wall.Out, Rx = capR, Rv = capR, Y = tc.y + 0.02f, Ground = wall.Ground, Theta = wall.Theta, Phase = Rand() * 6.28f };
                    cp.RiseFrom = cp.Y - 0.1f;
                    cp.CapDir = new Vector2(wall.Out.y, -wall.Out.x);
                    cp.Hash = PickHash(Rand()); cp.Pat = PickPat();
                    cp.Born = start + 0.15f * stack;
                    cp.LeaveAt = leave;
                    _pieces.Add(cp);
                    _ink.Spit(cp.Born, PlanW(new Vector3(wall.Pos.x, wall.Ground + 0.05f, wall.Pos.y)), (Vector3.up + outW).normalized);
                }
            }
        }

        /// <summary>The throw starts at ONE point, the brush tip, sampled at each throw's first frame (a small spit of ink within 0.15 m of the tip, thrown away from the caster). Wall verbs: two cells fly to the
        /// two wall cells nearest the start in 0.10 s and stand there (the real cells rise under them); the drawing begins then. 굼: every egg is thrown from the same point to its place, nearest the start first,
        /// 0.11 s in the air (no waiting in the hand), a spit at the foot where every third lands. The thrown cells stay standing facing the camera all the way and are small when they leave the hand.</summary>
        void MakeFlights()
        {
            if (bite == HexRingBite.Seed)
            {
                int k = 0;
                foreach (var p in _pieces)
                {
                    var to = new Vector3(p.Pos.x, p.Ground + p.Y, p.Pos.y);
                    _flights.Add(new HexFlight { From = _sheet.TipPlan, To = to, T0 = p.Born - 0.11f, T1 = p.Born, Rc = p.Rx * 0.9f, Arc = 0.5f + 0.06f * p.Pos.magnitude, Hash = p.Hash, Quick = true, Standing = true, StartScale = 0.45f });
                    if (k % 3 == 0) _ink.Spit(p.Born, PlanW(new Vector3(p.Pos.x, p.Ground + 0.05f, p.Pos.y)), Vector3.up);
                    k++;
                }
                return;
            }
            // the two cells nearest the start - but not one with a body standing at it (a body is set exactly there to test the ring: the flight's arc would pass over its head)
            var near = new List<Piece>();
            foreach (var p in _pieces) if (p.Wall && !BodyAt(p.Pos, p.Ground)) near.Add(p);
            if (near.Count == 0) foreach (var p in _pieces) if (p.Wall) near.Add(p);
            if (near.Count == 0) return;
            near.Sort((p, q) => p.Theta.CompareTo(q.Theta));
            for (int k = 0; k < 2 && k < near.Count; k++)
            {
                var p = near[k];
                float t0 = 0.03f * k, t1 = _lead + 0.03f * k;
                Vector2 at = bite == HexRingBite.Return ? p.Pos * RScale(t1) : p.Pos;
                _flights.Add(new HexFlight { From = _sheet.TipPlan, To = new Vector3(at.x, p.Ground + p.Y, at.y), T0 = t0, T1 = t1, Rc = p.Rx * 0.9f, Arc = 0.4f, Hash = p.Hash, Standing = true, StartScale = 0.45f });
                p.Born = Mathf.Max(0f, t1 - 0.05f);          // the real cell rises under the landing one
            }
        }

        // ------------------------------------------------------------ places

        /// <summary>A cell's centre in plan space at time <paramref name="a"/> (also what its flash is drawn at - the same number the mesh gives the shader as the cell's centre).</summary>
        Vector3 PieceCtr(Piece p, float a)
        {
            if (p.HopT1 > 0f)
            {
                if (a >= p.HopT1) return p.HopTo;
                if (a >= p.HopT0)
                {
                    float u = (a - p.HopT0) / (p.HopT1 - p.HopT0);
                    return Vector3.Lerp(p.HopFrom, p.HopTo, Smooth(u)) + Vector3.up * (0.6f * Mathf.Sin(Mathf.PI * u));
                }
                return p.HopFrom;
            }
            float rise = p.Born >= 0f ? Smooth((a - p.Born) / 0.25f) : 1f;
            float y = p.Ground + Mathf.Lerp(p.RiseFrom, p.Y, rise);
            if (p.Seed) y += 0.015f * Mathf.Sin(a * Mathf.PI * 2f * 0.4f + p.Phase);                      // a slow bob
            Vector2 xz = p.Pos;
            if (bite == HexRingBite.Return && p.Wall) xz *= RScale(a);
            return new Vector3(xz.x, y, xz.y);
        }

        // a reaction's flash holds its slot 0.3 s: the shader's terms decay on their own (seams exp(-18 t), the pale membrane exp(-11 t)); a 0.15 s release would still cut about a quarter of the seam flash in one frame
        void Schedule(Piece p, float at, bool stop = true, float life = 0.3f) { if (p != null) _flash.Schedule(() => PieceCtr(p, _age), at, life, stop); }
        void Schedule(HexPlate plate, float at, bool stop = true) { if (plate != null) _flash.Schedule(() => plate.Ctr, at, 0.3f, stop); }

        /// <summary>The standing cell (wall, stacked or sent out) whose centre is nearest the plan point at time <paramref name="a"/>.</summary>
        Piece NearestStanding(Vector2 p, float a)
        {
            Piece best = null; float bd = float.MaxValue;
            foreach (var c in _pieces)
            {
                if (c.Seed || c.Cap || c.Hidden || c.EndStarted) continue;
                Vector3 q = PieceCtr(c, a);
                float d = (new Vector2(q.x, q.z) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------ hits

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (!_built || _ended) return;
            long key = hit.TargetId != 0 ? hit.TargetId : 1000000L + _seen.Count + _returned.Count;
            bool back = bite == HexRingBite.Return && hit.Kind == "Return";           // the way back of 궁 is its own contact kind
            if (back) { if (!_returned.Add(key)) return; }
            else if (!_seen.Add(key)) return;
            Vector3 rel = hit.Feet - _origin;
            var feet = new Vector2(Vector3.Dot(rel, _right), Vector3.Dot(rel, _fwd));
            float theta = ThetaOf(feet);
            switch (bite)
            {
                case HexRingBite.Pillar: case HexRingBite.Leave: HitStanding(feet, ReactAt(theta)); break;
                case HexRingBite.Spread: HitSpread(hit, feet, theta, ReactAt(theta)); break;
                case HexRingBite.Seed: HitSeed(hit, feet, ReactAt(theta)); break;
                case HexRingBite.Cut: HitCut(hit, feet, ReactAt(theta)); break;
                default: HitReturn(hit, key, feet, back); break;
            }
        }

        /// <summary>The hold cells on a body (the kit: on its real surface, a ray per cell; the cells are turned half way to the camera when drawn so none stands edge-on), the first one flashed (the accent seam,
        /// a short decay, a 2-frame look hit-stop) and spat at; returns them.</summary>
        List<HexPlate> Hold(SpellFxHit hit, Vector2 feet, float t0, int n, float leaveAt)
        {
            HexBody body = HexHolds.BodyOf(_sheet, hit, feet);
            int before = _plates.Count;
            HexPlate front = HexHolds.Add(_sheet, _rng, _plates, body, t0, n, leaveAt, _R * 1.25f);
            if (front != null)
            {
                float tt = front.Start + 0.05f;
                Schedule(front, tt);
                _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            }
            return _plates.GetRange(before, _plates.Count - before);
        }

        /// <summary>국 / 눅: the standing cell nearest the body flashes and spits (the pillars are the look; nothing holds the body).</summary>
        void HitStanding(Vector2 feet, float at)
        {
            Piece cell = NearestStanding(feet, at);
            if (cell == null) return;
            Schedule(cell, at);
            Vector3 c = PieceCtr(cell, at);
            _ink.Spit(at, PlanW(new Vector3(c.x, cell.Ground + 0.05f, c.z)), Vector3.up);
        }

        /// <summary>군: the road. 3 - 4 NEW cells come out of the wall at the body's bearing (the wall keeps all its cells) 0.3 s after the wall is drawn, one after another (0.1 s apart, 0.25 s each, apex 0.6 m,
        /// each turned a little either way), along the way outward - to the body's feet if it is beyond the wall, else through the body and 2.2 m past the wall (a neighbour reached by the spread: from the first
        /// body's feet to this one's) - and float there at knee height for 3 s; the last one lands on the body (a flash and a spit) and the hold is put on it.</summary>
        void HitSpread(SpellFxHit hit, Vector2 feet, float theta, float react)
        {
            bool neighbour = hit.Kind != null && hit.Kind.Contains("Spread") && _primaryFeet.HasValue;
            if (_primaryFeet == null) _primaryFeet = feet;
            float dist = feet.magnitude;
            Vector2 radial = dist > 0.1f ? feet / dist : Dir(_a0);
            Piece src = null; float bd = float.MaxValue;
            foreach (var q in _pieces)
            {
                if (!q.Wall) continue;
                float d = Mathf.Abs(Mathf.DeltaAngle(Bearing(q.Pos) * Mathf.Rad2Deg, Bearing(radial) * Mathf.Rad2Deg));
                if (d < bd) { bd = d; src = q; }
            }
            if (src == null) return;
            int n = 3 + _rng.Next(2);
            Vector2 from = neighbour ? _primaryFeet.Value : src.Pos;
            Vector2 to = neighbour || dist > _R + 0.6f ? feet : radial * (_R + 2.2f);
            Vector2 d2 = to - from; float len = d2.magnitude;
            Vector2 dn = len > 1e-3f ? d2 / len : radial;
            float start = Mathf.Max(react, _wallDone + 0.3f) + 0.12f * _hopWave++;
            float t1 = start;
            for (int k = 0; k < n; k++)
            {
                Vector2 target = from + dn * (len * (k + 1) / n);
                float t0 = start + 0.1f * k; t1 = t0 + 0.25f;
                float g = GroundAt(target);
                var p = new Piece { Pos = src.Pos, Out = Rotate(src.Out, (Rand() * 2f - 1f) * 0.26f), Rx = src.Rx, Rv = src.Rv, Lean = src.Lean, Y = 0.4f, Ground = g, Theta = src.Theta, Phase = Rand() * 6.28f, Pop = 0.05f };
                p.Hash = PickHash(Rand()); p.Pat = PickPat();
                p.Born = t0;
                p.HopFrom = new Vector3(src.Pos.x, src.Ground + src.Y, src.Pos.y);
                p.HopTo = new Vector3(target.x, g + 0.4f, target.y);
                p.HopT0 = t0; p.HopT1 = t1;
                p.LeaveAt = t1 + 3.0f;                                            // the road it leaves: 3 s
                _endAt = Mathf.Max(_endAt, p.LeaveAt + HexBodyEnd.Duration(burstBody) + 0.4f);
                _ink.Spit(t1, PlanW(new Vector3(target.x, g + 0.05f, target.y)), Vector3.up);
                _pieces.Add(p);
                if (k == n - 1) Schedule(p, t1);
            }
            Hold(hit, feet, t1, 5, Mathf.Min(LeaveOf(theta) + 0.1f, _fadeStart + _spread));
        }

        static Vector2 Rotate(Vector2 v, float rad) { float c = Mathf.Cos(rad), s = Mathf.Sin(rad); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }

        /// <summary>굼: the 3 eggs nearest the body go off - each flies to the body's surface and becomes one of its hold cells (a flash and a spit on the first) - and the hold lasts 3 s.</summary>
        void HitSeed(SpellFxHit hit, Vector2 feet, float react)
        {
            var near = new List<Piece>();
            foreach (var p in _pieces) if (p.Seed && p.UsedAt < 0f) near.Add(p);
            near.Sort((p, q) => (p.Pos - feet).sqrMagnitude.CompareTo((q.Pos - feet).sqrMagnitude));
            if (near.Count > 3) near.RemoveRange(3, near.Count - 3);
            float t = react;
            if (near.Count > 0) t = Mathf.Max(react, near[0].Born + 0.1f);
            var plates = Hold(hit, feet, t, 3, t + 3.0f);
            for (int i = 0; i < near.Count; i++)
            {
                Piece s = near[i];
                s.UsedAt = t;
                if (i < plates.Count)
                    _flights.Add(new HexFlight { From = PieceCtr(s, t), To = plates[i].Ctr, T0 = t, T1 = plates[i].Start + 0.05f, Rc = s.Rx * 0.9f, Arc = 0.15f, Hash = s.Hash, Sampled = true, Quick = true, Standing = true, StartScale = 1f });
            }
            if (near.Count > 0)
            {
                Vector3 c = PieceCtr(near[0], t);
                _ink.Spit(t, PlanW(new Vector3(c.x, near[0].Ground + 0.05f, c.z)), Vector3.up);
            }
        }

        /// <summary>굿: the body crossing the blades loses plates: torn armour plates in the body's own edge colour fly off it and two hold cells stay on it.</summary>
        void HitCut(SpellFxHit hit, Vector2 feet, float at)
        {
            Piece cell = NearestStanding(feet, at);
            if (cell != null) Schedule(cell, at);
            Vector3 outward = feet.sqrMagnitude > 1e-4f ? (_right * feet.x + _fwd * feet.y).normalized : _fwd;
            _ink.Plates(at, hit.Position, (outward + Vector3.up * 0.5f).normalized, _sheet.Main.RimColor);          // the body's own edge colour (_Rim), as the vine's plates took
            Hold(hit, feet, at, 2, at + 4.0f);
        }

        /// <summary>궁: out - the moment the visible wall reaches the body (a body on the ring: as the wall closes): a flash, a spit and the hold; back (hit kind Return) - the moment the visible ring comes back past it: a
        /// second flash and spit, and the hold lets go.</summary>
        void HitReturn(SpellFxHit hit, long key, Vector2 feet, bool back)
        {
            float d = feet.magnitude;
            float at = Mathf.Max(_age, back ? BackTimeFor(d) : OutTimeFor(d));
            Piece cell = NearestStanding(feet, at);
            if (cell != null) Schedule(cell, at);
            if (!_holds.TryGetValue(key, out var held))
            {
                held = Hold(hit, feet, at, 4, back ? at + 0.4f : _backEnd + 0.3f);
                _holds[key] = held;
                if (back) return;                                                   // the hold's own contact flash and spit are this one
            }
            if (back)
            {
                for (int k = 0; k < held.Count; k++) held[k].LeaveAt = Mathf.Min(held[k].LeaveAt, at + 0.35f + 0.02f * k);
                if (held.Count > 0) { Schedule(held[0], at); _ink.Spit(at, HexHolds.ContactPoint(_sheet, held[0]), HexHolds.ContactNormal(_sheet, held[0])); }
            }
        }

        // ------------------------------------------------------------ mesh

        float VisOf(Piece p, float a)
        {
            if (p.Hidden) return 0f;
            float v = p.Born >= 0f ? Smooth((a - p.Born) / p.Pop) : 1f;
            if (p.UsedAt >= 0f) v *= 1f - Smooth((a - p.UsedAt) / 0.15f);
            return v;
        }

        void Rebuild(float a)
        {
            _sheet.Begin();
            foreach (var p in _pieces)
            {
                if (a >= p.LeaveAt && !p.EndStarted) StartEnd(p, a);
                float vis = VisOf(p, a);
                if (vis <= 0.02f) continue;
                EmitPiece(p, vis, a);
                if (p.Seed) EmitThread(p, vis, a);
            }
            foreach (var p in _plates)
            {
                float g = HexHolds.PlateG(p, a, 0.2f);
                if (g <= 0.02f) continue;
                // a hold cell sits on the body's surface, and a body's flank is edge-on to the shoulder camera: it is turned half way toward the camera (the shading normal is the kit's per-cell one)
                Vector2 dir = p.Dir + 0.8f * _sheet.ToCamPlan;
                if (dir.sqrMagnitude < 1e-4f) dir = _sheet.ToCamPlan;
                _sheet.EmitStanding(p.Ctr, dir.normalized, p.Rx * g, p.Rv * g, 0f, p.Hash, p.Pat, p.D01);
            }
            if (bite == HexRingBite.Cut) EmitCrack(a);
            foreach (var f in _flights)
            {
                if (a < f.T0 || a >= f.T1) continue;
                if (!f.Sampled)
                {
                    _sheet.SampleFlight(f);
                    if (!_tipSpat) { _tipSpat = true; TipSpit(f.From); }
                }
                _sheet.EmitFlight(f, a);
            }
            _sheet.Flush();
        }

        /// <summary>The ink of the brush: a small spit within 0.15 m of the tip, thrown AWAY from the caster (it fell on the caster's shoulder and back).</summary>
        void TipSpit(Vector3 tipPlan)
        {
            Vector3 w = PlanW(tipPlan);
            Vector3 away = new Vector3(w.x - _origin.x, 0f, w.z - _origin.z);
            if (away.sqrMagnitude < 1e-3f) away = _fwd; away.Normalize();
            SpellFxLabShot.InkBurst(w, (away + Vector3.up * 0.15f).normalized, SpellFxBurstBody.Water, null, 0.2f);
        }

        /// <summary>A cell's end begins: a 2-frame flash of its seams, and the body's own ink and burst (<see cref="HexBodyEnd.Begin"/>) - wood on every cell (it is a few small drops), the others on every other cell.</summary>
        void StartEnd(Piece p, float a)
        {
            p.EndStarted = true;
            if (p.Hidden || p.UsedAt >= 0f || p.Cap) return;
            Schedule(p, a, false, 0.12f);          // (a cell's end flash is short: up to 30 cells end per second and the shader has four slots)
            if (burstBody == SpellFxBurstBody.Wood || ((int)(p.Hash * 977f) & 1) == 0)
            {
                Vector3 c = PieceCtr(p, a);
                HexBodyEnd.Begin(burstBody, PlanW(c), new Vector3(p.Out.x, 0f, p.Out.y), p.Rx);
            }
        }

        /// <summary>One standing cell: out, leaning outward, the kit's per-cell shading normal; 궁's cells are a third bigger at the far end; an egg pulses 0.8 Hz +- 8 % and grows 5 % over 8 s. In its end: the pose
        /// of <see cref="HexBodyEnd.Pose"/> (offset, size, vertex alpha - fire burns away from the lower edge).</summary>
        void EmitPiece(Piece p, float vis, float a)
        {
            Vector3 c = PieceCtr(p, a);
            float k = bite == HexRingBite.Return && p.Wall ? 1f + 0.35f * Tri01(a) : 1f;
            if (p.Seed) k *= (1f + 0.08f * Mathf.Sin(a * Mathf.PI * 2f * 0.8f + p.Phase)) * (1f + 0.05f * Mathf.Clamp01(a / 8f));
            float s = vis * k, sx = s, sv = s;
            bool ending = a > p.LeaveAt;
            HexEndPose pose = default;
            Vector3 outW = new Vector3(p.Out.x, 0f, p.Out.y);
            if (ending)
            {
                pose = HexBodyEnd.Pose(burstBody, a - p.LeaveAt);
                if (pose.Done || pose.Alpha <= 0.01f) return;
                c += pose.Offset(outW); sx *= pose.ScaleH; sv *= pose.ScaleV;
            }
            if (p.Cap)
            {
                float rad = p.Rx * sx * (ending ? pose.Alpha : 1f);          // (the flat footing has no vertex alpha: it shrinks out instead of popping)
                if (rad > 0.01f) _sheet.EmitFlat(new Vector2(c.x, c.z), c.y, rad, p.Hash, p.Pat, 0.7f, false, Mathf.Atan2(p.CapDir.y, p.CapDir.x));
                return;
            }
            Vector2 dir = Rotate(p.Out, p.Yaw);
            System.Func<float, float> alphaOf = null;
            if (ending) { var ps = pose; alphaOf = h => HexBodyEnd.VertexAlpha(ps, h); }
            _sheet.EmitStandingEx(c, dir, p.Rx * sx, p.Rv * sv, p.Lean, p.CornerRot, p.Hash, p.Pat, 0.7f, alphaOf);
            if (p.Blade && vis > 0.3f) EmitBladeEdges(p, c, dir, vis, ending ? pose.Alpha : 1f);
            if (ending && burstBody == SpellFxBurstBody.Wood) EmitStrands(p, c, a - p.LeaveAt, pose.Alpha);
        }

        float Tri01(float a) => Prof(a);

        /// <summary>굿: a cream edge along the two upper sides of a blade (1.5 x the seam's usual line) - the cutting edge.</summary>
        void EmitBladeEdges(Piece p, Vector3 c, Vector2 dir, float vis, float alpha)
        {
            var cs = _sheet.Main.Cs;                                           // the corners the cell was just built from (0 = the top, 1 = upper left, 5 = upper right)
            Vector3 n3 = new Vector3(dir.x, 0f, dir.y);
            Vector3 faceN = n3 * Mathf.Cos(p.Lean) - Vector3.up * Mathf.Sin(p.Lean);
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.ShadeFor(faceN, c));
            Vector3 front = Vector3.Dot(faceN, new Vector3(_sheet.ToCamPlan.x, 0.15f, _sheet.ToCamPlan.y)) >= 0f ? faceN : -faceN;
            float hw = 0.007f * vis;
            for (int e = 0; e < 2; e++)
            {
                Vector3 a0 = e == 0 ? cs[0] : cs[5], a1 = e == 0 ? cs[1] : cs[0];
                Vector3 side = Vector3.Cross(faceN, a1 - a0);
                if (side.sqrMagnitude < 1e-8f) continue;
                side = side.normalized * hw;
                _sheet.Main.QuadVertsA(a0 - side, a0 + side, a1 - side, a1 + side, sh, _R * 1.25f, false, front, alpha);
            }
        }

        /// <summary>목: 2 - 3 short ink strands run outward from a leaving cell (the CSV's "잔뿌리로 풀림"), growing to 0.3 m while they fade; every one a thin dark thread that faces the camera.</summary>
        void EmitStrands(Piece p, Vector3 c, float t, float alpha)
        {
            float e = Smooth(t / HexBodyEnd.Duration(burstBody)), fade = 1f - Smooth((t / HexBodyEnd.Duration(burstBody) - 0.4f) / 0.6f);
            if (fade <= 0.02f) return;
            int count = 2 + ((int)(p.Hash * 31f) & 1);
            Vector3 outW = new Vector3(p.Out.x, 0f, p.Out.y), side = new Vector3(p.Out.y, 0f, -p.Out.x);
            for (int i = 0; i < count; i++)
            {
                float ang = (i - (count - 1) * 0.5f) * 0.5f + (Smooth(p.Hash * 7f % 1f) - 0.5f) * 0.3f, len = (0.14f + 0.16f * ((p.Hash * 13f + i * 0.37f) % 1f)) * e;
                Vector3 d = (outW * Mathf.Cos(ang) + side * Mathf.Sin(ang)).normalized;
                _pts.Clear();
                for (int s = 0; s <= 4; s++)
                {
                    float u = s / 4f;
                    _pts.Add(c + d * (len * u) + Vector3.up * (0.05f * Mathf.Sin(u * Mathf.PI) - 0.04f * u) + side * (0.02f * Mathf.Sin(u * 6.28f + i)));
                }
                _sheet.EmitStrand(_pts, 0.012f, 3, true, fade);
            }
        }

        /// <summary>굼: the thin thread an egg hangs on - from its lower edge to the ground, slightly curved and swaying a little, plain dark glass facing the camera - with a small ink mark on the ground at its end.</summary>
        void EmitThread(Piece p, float vis, float a)
        {
            Vector3 c = PieceCtr(p, a);
            float y0 = p.Ground + 0.02f, y1 = c.y - p.Rv * Sqrt3Half * vis;
            if (y1 - y0 < 0.05f) return;
            Vector2 across = new Vector2(_sheet.ToCamPlan.y, -_sheet.ToCamPlan.x);
            float sway = 0.01f * Mathf.Sin(a * 1.6f + p.ThreadPhase), bow = 0.035f * Mathf.Sin(p.ThreadPhase);
            _pts.Clear();
            for (int s = 0; s <= 5; s++)
            {
                float u = s / 5f;                                              // 0 = the ground .. 1 = the egg
                float off = bow * Mathf.Sin(u * Mathf.PI) + sway * u;
                _pts.Add(new Vector3(c.x + across.x * off, Mathf.Lerp(y0, y1, u), c.z + across.y * off));
            }
            float w = 0.012f * Mathf.Clamp01(vis * 2f);
            _sheet.EmitStrand(_pts, w, 3, true, 1f);
            // the mark at the foot: a small diamond of dark glass lying on the ground
            float r = 0.055f * Mathf.Clamp01(vis * 2f);
            Vector3 g = new Vector3(c.x, y0 + 0.004f, c.z);
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            for (int pass = 0; pass < 3; pass++)
                _sheet.Main.QuadVerts(g + new Vector3(-r, 0f, 0f), g + new Vector3(0f, 0f, -r), g + new Vector3(0f, 0f, r), g + new Vector3(r, 0f, 0f), sh, _R * 1.25f, true);
        }

        /// <summary>굿: the dark crack in the ground at the blades' foot radius - 4 - 8 cm wide, irregular, plain dark glass drawn three times for body (the shader has no black), a cream edge hair inside on both sides and
        /// a wider faint bleed outside - drawn as the blades are (from the start point both ways round, out of the camera's cone) and going as they go.</summary>
        void EmitCrack(float a)
        {
            const int N = 96;
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            float reach = _R * 1.25f;
            Vector2 p0 = default, n0 = default; float w0 = 0f; bool have = false;
            for (int i = 0; i <= N; i++)
            {
                float phi = _a0 - Mathf.PI + i / (float)N * 2f * Mathf.PI;
                float th = Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, _a0 * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float vis = InCone(phi) ? 0f : Smooth((a - BornOf(th)) / 0.18f) * (1f - Smooth((a - LeaveOf(th)) / 0.2f));
                float noise = 0.5f + 0.5f * Mathf.Sin(phi * 23f + 1.3f) * Mathf.Sin(phi * 7f + 0.4f);          // 0..1, irregular
                float w = (0.04f + 0.04f * noise) * vis;
                Vector2 n = Dir(phi), p = n * _R;
                if (have && (w0 > 0.004f || w > 0.004f))
                {
                    Vector3 a0 = CrackPt(p0, n0, -w0 * 0.5f), b0 = CrackPt(p0, n0, w0 * 0.5f), a1 = CrackPt(p, n, -w * 0.5f), b1 = CrackPt(p, n, w * 0.5f);
                    for (int r = 0; r < 3; r++) _sheet.Main.QuadVerts(a0, b0, a1, b1, sh, reach, true);
                    // the bleed: wider and faint, outside only
                    Vector3 c0 = CrackPt(p0, n0, w0 * 0.5f + 0.03f * (w0 > 0f ? 1f : 0f)), c1 = CrackPt(p, n, w * 0.5f + 0.03f * (w > 0f ? 1f : 0f));
                    _sheet.Main.QuadVerts(b0, c0, b1, c1, sh, reach, true);
                    // the cream edge hair inside, both sides
                    float e0 = Mathf.Min(w0 * 0.12f, 0.006f), e1 = Mathf.Min(w * 0.12f, 0.006f);
                    _sheet.Main.QuadVerts(CrackPt(p0, n0, -w0 * 0.5f - e0), CrackPt(p0, n0, -w0 * 0.5f + e0), CrackPt(p, n, -w * 0.5f - e1), CrackPt(p, n, -w * 0.5f + e1), sh, reach, false);
                    _sheet.Main.QuadVerts(CrackPt(p0, n0, w0 * 0.5f - e0), CrackPt(p0, n0, w0 * 0.5f + e0), CrackPt(p, n, w * 0.5f - e1), CrackPt(p, n, w * 0.5f + e1), sh, reach, false);
                }
                p0 = p; n0 = n; w0 = w; have = true;
            }
        }

        Vector3 CrackPt(Vector2 p, Vector2 n, float off) { Vector2 q = p + n * off; return new Vector3(q.x, _sheet.Ground.Q(p) + 0.012f, q.y); }
    }
}
