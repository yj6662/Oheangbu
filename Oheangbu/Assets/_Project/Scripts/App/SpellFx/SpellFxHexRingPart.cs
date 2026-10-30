using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅜ 둘레 in the hex-cell membrane grammar (the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"; the ㅗ 면 trial passed the Fable gate, D308-157; main's direction change, PLAN-308 §2.2:
    /// "not a band lying on the ground but a LOW WALL OF STANDING CELLS" - a cell lying on the ground gets a thick seam and a flat pattern at the shoulder camera, ㅗ F1 - F5): the ring round the caster
    /// (the rule's ring: radius = the band's centre line) as a ring of single standing hexagon cells (shader Oheangbu/SpellFx/HexDome, material = the ㅜ ward's glass), one family with the ward's dome
    /// and the ㅗ fan but told apart from the dome at a glance: knee-to-waist high (0.35 - 0.8 m), ONE row of cells 0.22 - 0.25 m round, open on top, a gap between cells (centre spacing = 1.25 x the cell), each cell leaning
    /// 10 - 15 deg outward and standing at its own height (+- 0.1 m), no bright foot ring (the frame is upright and sunk, the shader's base-ring term is 0), nothing continuous. The five final-consonant verbs
    /// (국 극치 / 군 번진다 / 굼 심는다 / 굿 가른다 / 궁 돌아온다) are <see cref="HexRingBite"/>. Built on <see cref="SpellFxHexKit"/>, the kit the fan part uses (<see cref="HexSheet.EmitStanding"/>: a standing
    /// cell with its own shading normal, turned about the view vector until |n.v| is 0.5 - 0.75 - the dome shader thins the fill of a cell that faces the camera to ~0.1).
    /// Common grammar (times from the launch): two cells fly from the brush tip to the ring's start point (the point away from the camera; 0.10 s, a small ink spit at the tip); from there the wall rises both ways
    /// round (0.25 s over the half turn, each cell rising out of the ground and growing); the cells within 50 deg of the camera's side are left out (the ring's back is 0.8 m in front of the shoulder camera: it would
    /// fill the screen); a 2 Hz breath while it stands; the end is the cells breaking up (the body's own burst, the last drawn first, wood: none). Mid-way ink (the brush, a throw, a rise, a contact) is always the small
    /// plain ink; only the end bursts as the body. Look only - nothing here touches a rule.
    /// Verbs:
    ///  국 Pillar: from 0.6 s 6 - 8 pillars (0.08 s apart, left to right, the start point itself free) replace wall cells: 3 - 4 cells stacked on the ground, a cell every 0.15 s (1.1 - 1.5 m, uneven), a small spit at the
    ///    foot as they stack; kept to 9.5 s, then the far ones go first.
    ///  군 Spread: the wall stands (to 2.6 s); a body that crosses sends 3 - 4 cells of the wall nearest its bearing hopping out along its way (0.1 s apart, 0.25 s each, apex 0.6 m) to float at knee height as a road
    ///    (3 s) - the last lands on the body and the hold is put on it.
    ///  굼 Seed: no wall; 8 - 10 egg cells floating at knee height (0.42 m, +- 3 cm bob, 0.8 Hz +- 8 %) on a thin vertical ink thread, thrown one after another from the brush tip (0.3 + 0.15 k s, 0.11 s in the
    ///    air); when a body crosses, the 3 nearest seeds fly to its surface and become its hold.
    ///  굿 Cut: the wall is a row of low blades (0.3 m, leaning 25 deg out) over a dark crack on the ground (a 5 cm ring line, kept to 9.5 s); a body crossing it loses torn plates (the 가른다 burst in its body colour)
    ///    and keeps two hold cells.
    ///  궁 Return: the wall is pushed out to 4 m (0.5 s) and back to 2 m (0.5 s) - the rule's own linear trip - the cells spreading apart and a third bigger at the far end; a body touched on the way out gets a flash, a
    ///    spit and the hold; touched again on the way back (hit kind Return) a second flash and spit and the hold lets go; a spit at the caster's feet and a flash on the start cell as the wall is home.</summary>
    public sealed class SpellFxHexRingPart : MonoBehaviour, ISpellFxPart
    {
        /// <summary>국 Pillar · 군 Spread · 굼 Seed · 굿 Cut · 궁 Return.</summary>
        public enum HexRingBite { Pillar, Spread, Seed, Cut, Return }

        [SerializeField] Material material;
        [SerializeField] HexRingBite bite = HexRingBite.Pillar;
        [Tooltip("What the ring's END burst is made of (cells breaking up; Wood = none). The brush / throw / rise / contact spits are always the small plain ink.")]
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
        [Tooltip("궁: the ring's outer reach (the rule's space.returnRing.outer) and the time of each leg (the rule's 0.5 s out, 0.5 s back) - copied from the rule's data, not read from it.")]
        [SerializeField] float returnOuter = 4f, returnLegSeconds = 0.5f;
        [SerializeField] bool followGround = true;

        static readonly int[] PatChoices = SpellFxHexKit.PatChoices;
        static float Smooth(float x) => SpellFxHexKit.Smooth(x);

        // ------------------------------------------------------------ state

        /// <summary>Every standing cell of the ring: a wall cell, a pillar's cell, an egg, a cell that has hopped out.</summary>
        sealed class Piece
        {
            public Vector2 Pos, Out;            // plan x, z of its centre on the ring (the wall's place) and the unit direction out
            public float Y, RiseFrom;           // centre height above the ground when up / at the start of its rise (0 = the centre at the ground)
            public float Rc, Rv, Lean;          // horizontal / vertical corner radius, lean (rad, top outward)
            public float Theta, Phase, Hash, Ground;
            public int Pat;
            public float Born = -1f, LeaveAt = float.MaxValue, LeaveLen = -1f, UsedAt = -1f;
            public bool Wall, Seed, Face, Hidden;      // Face: turned to the camera instead of out; Hidden: a pillar stands in its place
            public float HopT0, HopT1; public Vector3 HopFrom, HopTo;       // 군: it leaves the wall and lands at HopTo
        }

        readonly List<Piece> _pieces = new List<Piece>();
        readonly List<HexPlate> _plates = new List<HexPlate>();          // the hold cells on bodies
        readonly List<HexFlight> _flights = new List<HexFlight>();
        readonly HexFlashQueue _flash = new HexFlashQueue();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly HashSet<long> _seen = new HashSet<long>(), _returned = new HashSet<long>();
        readonly Dictionary<long, List<HexPlate>> _holds = new Dictionary<long, List<HexPlate>>();

        HexSheet _sheet;
        System.Random _rng;
        bool _built, _launched, _ended;
        float _age;
        Vector3 _origin, _fwd, _right;
        float _R, _a0, _camAng, _thetaMax, _fadeStart, _spread = 0.5f, _leaveLen = 0.2f, _endAt, _lead, _seedLast = 1f;
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
            _age = ageSeconds;
            if (!_launched || _ended || _sheet == null) return;
            float a = ageSeconds;
            _sheet.Main.SetClock(a, 0f); _sheet.Air.SetClock(a, 0f);
            if (a > _endAt) { if (_sheet.Main.Go.activeSelf) _sheet.SetActive(false); return; }
            _ink.Drain(a);
            _flash.Drive(_sheet, a);
            _sheet.Main.Breathe(a); _sheet.Air.Breathe(a);
            _sheet.Age = a;
            Rebuild(a);
        }

        void OnDestroy()
        {
            if (_sheet != null) _sheet.Destroy();
        }

        // ------------------------------------------------------------ build

        float Rand() => (float)_rng.NextDouble();
        float PickHash(float phase) => SpellFxHexKit.PickHash(_rng, phase);
        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;
        float GroundAt(Vector2 p) => _sheet.Ground.At(p);
        static Vector2 Dir(float phi) => new Vector2(Mathf.Sin(phi), Mathf.Cos(phi));          // a bearing (0 = ahead, + = right) as plan x, z

        /// <summary>Angle of a plan point from the ring's start point, 0 .. pi.</summary>
        float ThetaOf(Vector2 p)
        {
            if (p.sqrMagnitude < 1e-4f) return 0f;
            return Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg, _a0 * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
        }

        bool InCone(float phi) => Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, _camAng * Mathf.Rad2Deg)) < cameraCone;

        float ThetaN(float theta) => Mathf.Clamp01(theta / _thetaMax);

        /// <summary>When the drawing front reaches the angle <paramref name="theta"/> from the start.</summary>
        float BornOf(float theta) => _lead + closeSeconds * ThetaN(theta);

        /// <summary>When a cell at angle <paramref name="theta"/> starts to leave: the last drawn goes first.</summary>
        float LeaveOf(float theta) => _fadeStart + (1f - ThetaN(theta)) * _spread;

        // 궁: the ring's radius over the trip as the rule runs it (linear out and back), as a factor of the ring's own
        float Tri(float a) { float leg = Mathf.Max(returnLegSeconds, 0.1f); return a < leg ? Mathf.Clamp01(a / leg) : Mathf.Clamp01(1f - (a - leg) / leg); }
        float RScale(float a) => (_R + (returnOuter - _R) * Tri(a)) / _R;

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            var spec = cast.Spec;
            _R = spec != null && spec.RingRadius > 0.5f ? spec.RingRadius : defaultRadius;
            _lead = leadSeconds;
            _spread = 0.5f; _leaveLen = 0.2f;
            switch (bite)
            {
                // the rule's lives (the spec): 국 10 s, 군 the ring 2.2 s + 3 s marks, 굼 8 s, 굿 10 s - the break-up starts 0.5 s before so the last cell is gone at about that time
                case HexRingBite.Pillar: _fadeStart = 9.5f; break;
                case HexRingBite.Spread: _fadeStart = 2.6f; break;
                case HexRingBite.Seed: _fadeStart = 7.8f; break;
                case HexRingBite.Cut: _fadeStart = 9.5f; break;
                default: _fadeStart = 1.45f; _spread = 0.4f; break;          // 궁: the trip is over at 1.0 s; the wall stands 0.45 s more
            }
            _endAt = _fadeStart + _spread + 0.25f + _leaveLen + 0.2f;

            // an UPRIGHT frame sunk 1 m (the cells stand all round the caster; the shader's reveal would discard the half behind a lying object, and its base-ring term is 0 here); the brush tip is sampled in the sheet's constructor
            _sheet = new HexSheet(transform, cast, material, HexFrame.UprightSunk(_origin, _right, _fwd, 1f), 0f, followGround, "HexRing");
            // the start point: away from the camera (the far side), else ahead
            Vector2 away = -_sheet.ToCamPlan;
            if (away.sqrMagnitude < 0.04f) away = new Vector2(0f, 1f);
            _a0 = Mathf.Atan2(away.x, away.y);
            _camAng = _a0 + Mathf.PI;
            _thetaMax = Mathf.Max(1f, Mathf.PI - cameraCone * Mathf.Deg2Rad);

            if (bite == HexRingBite.Seed) { PickSeeds(); MakeFlights(); PickEndInk(); }
            else
            {
                BuildWall();
                if (bite == HexRingBite.Pillar) PickPillars();
                PickEndInk();
                MakeFlights();
                if (bite == HexRingBite.Return) MakeHomeSpit();
            }
            _built = true;
        }

        /// <summary>The wall: single standing cells round the ring, centre spacing gapFactor x the cell's width, out of the camera's cone. 굿: low blades (0.3 m, leaning 25 deg).</summary>
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
                var p = new Piece { Pos = o * _R, Out = o, Wall = true, Phase = Rand() * 6.28f };
                p.Rc = Mathf.Lerp(cellRadiusMin, cellRadiusMax, Rand());
                p.Rv = blade ? 0.15f / 0.8660254f : p.Rc;
                p.Y = blade ? 0.15f + (Rand() * 2f - 1f) * 0.03f : wallHeight + (Rand() * 2f - 1f) * heightJitter;
                p.Lean = (blade ? 25f : Mathf.Lerp(leanMinDeg, leanMaxDeg, Rand())) * Mathf.Deg2Rad;
                p.Hash = PickHash(Rand()); p.Pat = PatChoices[_rng.Next(PatChoices.Length)];
                p.Theta = ThetaOf(p.Pos); p.Ground = GroundAt(p.Pos);
                p.Born = BornOf(p.Theta); p.LeaveAt = LeaveOf(p.Theta) + Rand() * 0.08f;
                _pieces.Add(p);
            }
        }

        /// <summary>굼: 8 - 10 egg cells over the front arc (+- 109 deg of the start), a little off the line, floating at knee height, planted nearest the start first.</summary>
        void PickSeeds()
        {
            int want = 8 + _rng.Next(3);
            var list = new List<Piece>();
            for (int k = 0; k < want; k++)
            {
                float t = ((k + 0.5f) / want - 0.5f) * 3.8f + (Rand() - 0.5f) * 0.12f;
                Vector2 o = Dir(_a0 + t);
                var p = new Piece { Out = o, Seed = true, Face = true, Phase = Rand() * 6.28f, Rc = 0.26f, Rv = 0.26f, Y = 0.42f + (Rand() - 0.5f) * 0.1f, Lean = (6f + 8f * Rand()) * Mathf.Deg2Rad };
                p.Pos = o * (_R + (Rand() - 0.5f) * 0.2f);
                p.Hash = PickHash(Rand()); p.Pat = PatChoices[_rng.Next(PatChoices.Length)];
                p.Theta = ThetaOf(p.Pos); p.Ground = GroundAt(p.Pos);
                p.LeaveAt = _fadeStart + Rand() * 0.4f;
                list.Add(p);
            }
            list.Sort((p, q) => p.Theta.CompareTo(q.Theta));
            for (int k = 0; k < list.Count; k++) { list[k].Born = 0.3f + 0.15f * k; _pieces.Add(list[k]); }
            _seedLast = list.Count > 0 ? list[list.Count - 1].Born : 0.6f;
        }

        /// <summary>국: 6 - 8 pillars over the front arc (left to right, 0.08 s apart, the start point itself free - a body is set there to test the ring): each takes the place of a wall cell and is 3 - 4 cells
        /// stacked on the ground, a cell every 0.15 s, 1.1 - 1.5 m high and a little uneven; a small spit at the foot as they stack. They go far side first, the top cell first.</summary>
        void PickPillars()
        {
            var offs = new List<float> { -1.75f, -1.3f, -0.85f, -0.4f, 0.4f, 0.85f, 1.3f, 1.75f };
            int want = 6 + _rng.Next(3);
            while (offs.Count > want) offs.RemoveAt(_rng.Next(offs.Count));
            offs.Sort();
            int order = 0;
            foreach (float t in offs)
            {
                Piece wall = null; float bd = float.MaxValue;
                foreach (var q in _pieces)
                {
                    if (!q.Wall || q.Hidden) continue;
                    float d = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(q.Pos.x, q.Pos.y) * Mathf.Rad2Deg, (_a0 + t) * Mathf.Rad2Deg));
                    if (d < bd) { bd = d; wall = q; }
                }
                if (wall == null) continue;
                wall.Hidden = true;
                float start = 0.6f + 0.08f * order++;
                int n = 3 + _rng.Next(2);
                const float rc = 0.22f;
                float hcell = 2f * 0.8660254f * rc, leave = LeaveOf(wall.Theta);
                Vector3 outW = new Vector3(wall.Out.x, 0f, wall.Out.y);
                for (int j = 0; j < n; j++)
                {
                    var p = new Piece { Pos = wall.Pos, Out = wall.Out, Rc = rc, Rv = rc, Ground = wall.Ground, Theta = wall.Theta, Phase = Rand() * 6.28f, Lean = 0.1f };
                    p.Y = 0.8660254f * rc + j * hcell * 1.03f + 0.03f;
                    p.RiseFrom = p.Y - 0.2f;
                    p.Hash = PickHash(Rand()); p.Pat = PatChoices[_rng.Next(PatChoices.Length)];
                    p.Born = start + 0.15f * j;
                    p.LeaveAt = leave + 0.04f * (n - 1 - j);
                    _pieces.Add(p);
                    if (j % 2 == 0) _ink.Spit(p.Born, PlanW(new Vector3(wall.Pos.x, wall.Ground + 0.05f, wall.Pos.y)), (Vector3.up + outW).normalized);
                }
                if ((order & 1) == 0) _ink.End(leave, PlanW(new Vector3(wall.Pos.x, wall.Ground + n * hcell + 0.1f, wall.Pos.y)), Vector3.up, burstBody);
            }
        }

        /// <summary>The end: about eight cells (the first to leave) burst as the body as they break up (wood: none).</summary>
        void PickEndInk()
        {
            if (burstBody == SpellFxBurstBody.Wood || _pieces.Count == 0) return;
            var order = new List<Piece>();
            foreach (var p in _pieces) if (!p.Hidden) order.Add(p);
            order.Sort((p, q) => p.LeaveAt.CompareTo(q.LeaveAt));
            int n = Mathf.Min(8, order.Count);
            for (int k = 0; k < n; k++)
            {
                var c = order[Mathf.Min(order.Count - 1, (int)((k + 0.5f) * order.Count / n))];
                _ink.End(c.LeaveAt, PlanW(new Vector3(c.Pos.x, c.Ground + c.Y, c.Pos.y)), Vector3.up, burstBody);
            }
        }

        /// <summary>The throw starts at ONE point, the brush tip, sampled at each throw's first frame with a small ink spit there. Wall verbs: two cells fly to the two wall cells nearest the start in 0.10 s and stand
        /// there (the real cells rise under them); the drawing begins then. 굼: every egg is thrown from the same point to its place, nearest the start first, 0.11 s in the air (no waiting in the hand), a spit at
        /// the tip for every other throw and one at the foot where every third lands. The thrown cells stay standing facing the camera all the way.</summary>
        void MakeFlights()
        {
            if (bite == HexRingBite.Seed)
            {
                int k = 0;
                foreach (var p in _pieces)
                {
                    var to = new Vector3(p.Pos.x, p.Ground + p.Y, p.Pos.y);
                    _flights.Add(new HexFlight { From = _sheet.TipPlan, To = to, T0 = p.Born - 0.11f, T1 = p.Born, Rc = p.Rc * 0.9f, Arc = 0.5f + 0.06f * p.Pos.magnitude, Hash = p.Hash, TipInk = k % 2 == 0, Quick = true, Standing = true });
                    if (k % 3 == 0) _ink.Spit(p.Born, PlanW(new Vector3(p.Pos.x, p.Ground + 0.05f, p.Pos.y)), Vector3.up);
                    k++;
                }
                return;
            }
            var near = new List<Piece>();
            foreach (var p in _pieces) if (p.Wall && !p.Hidden) near.Add(p);
            if (near.Count == 0) return;
            near.Sort((p, q) => p.Theta.CompareTo(q.Theta));
            for (int k = 0; k < 2 && k < near.Count; k++)
            {
                var p = near[k];
                float t0 = 0.03f * k, t1 = _lead + 0.03f * k;
                Vector2 at = bite == HexRingBite.Return ? p.Pos * RScale(t1) : p.Pos;
                _flights.Add(new HexFlight { From = _sheet.TipPlan, To = new Vector3(at.x, p.Ground + p.Y, at.y), T0 = t0, T1 = t1, Rc = p.Rc * 0.9f, Arc = 0.4f, Hash = p.Hash, TipInk = k == 0, Standing = true });
                p.Born = Mathf.Max(0f, t1 - 0.05f);          // the real cell rises under the landing one
            }
        }

        /// <summary>궁: the wall is home at 1.0 s: a spit at the caster's feet and a flash on the start cell.</summary>
        void MakeHomeSpit()
        {
            float back = returnLegSeconds * 2f;
            _ink.Spit(back, PlanW(new Vector3(0f, GroundAt(Vector2.zero) + 0.06f, 0f)), Vector3.up);
            Piece first = null;
            foreach (var p in _pieces) if (p.Wall && (first == null || p.Theta < first.Theta)) first = p;
            if (first != null) Schedule(first, back);
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
            }
            float rise = p.Born >= 0f ? Smooth((a - p.Born) / 0.25f) : 1f;
            float y = p.Ground + Mathf.Lerp(p.RiseFrom, p.Y, rise);
            if (p.Seed) y += 0.03f * Mathf.Sin(a * Mathf.PI * 2f * 0.5f + p.Phase);                      // +- 3 cm
            Vector2 xz = p.Pos;
            if (bite == HexRingBite.Return && p.Wall) xz *= RScale(a);
            return new Vector3(xz.x, y, xz.y);
        }

        void Schedule(Piece p, float at) { if (p != null) _flash.Schedule(() => PieceCtr(p, _age), at); }
        void Schedule(HexPlate plate, float at) { if (plate != null) _flash.Schedule(() => plate.Ctr, at); }

        /// <summary>The standing cell (wall, pillar or hopped) whose centre is nearest the plan point at time <paramref name="a"/>.</summary>
        Piece NearestStanding(Vector2 p, float a)
        {
            Piece best = null; float bd = float.MaxValue;
            foreach (var c in _pieces)
            {
                if (c.Seed || c.Hidden) continue;
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
            float arrive = Mathf.Max(_age, BornOf(theta) + 0.1f);                     // a contact exists from the commit; it shows once the drawing has reached it
            switch (bite)
            {
                case HexRingBite.Pillar: HitPillar(feet, arrive); break;
                case HexRingBite.Spread: HitSpread(hit, feet, theta, arrive); break;
                case HexRingBite.Seed: HitSeed(hit, feet, arrive); break;
                case HexRingBite.Cut: HitCut(hit, feet, theta, arrive); break;
                default: HitReturn(hit, key, feet, back, arrive); break;
            }
        }

        /// <summary>The hold cells on a body (the kit: on its real surface, a ray per cell), the first one flashed and spat at; returns them.</summary>
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

        /// <summary>국: the standing cell nearest the body flashes and spits (the pillars are the look; nothing holds the body).</summary>
        void HitPillar(Vector2 feet, float arrive)
        {
            Piece cell = NearestStanding(feet, arrive);
            if (cell == null) return;
            Schedule(cell, arrive);
            Vector3 c = PieceCtr(cell, arrive);
            _ink.Spit(arrive, PlanW(new Vector3(c.x, cell.Ground + 0.05f, c.z)), Vector3.up);
        }

        /// <summary>군: the road. 3 - 4 wall cells nearest the body's bearing (a neighbour reached by the spread: nearest it, from the first body's feet) hop out one after another (0.1 s apart, 0.25 s each, apex
        /// 0.6 m) along the way outward - to the body's feet if it is beyond the wall, else through the body and 2.2 m past the wall - and float there at knee height for 3 s; the last one lands on the body (a
        /// flash and a spit) and the hold is put on it.</summary>
        void HitSpread(SpellFxHit hit, Vector2 feet, float theta, float arrive)
        {
            bool neighbour = hit.Kind != null && hit.Kind.Contains("Spread") && _primaryFeet.HasValue;
            if (_primaryFeet == null) _primaryFeet = feet;
            float dist = feet.magnitude;
            Vector2 radial = dist > 0.1f ? feet / dist : Dir(_a0);
            float bearing = Mathf.Atan2(radial.x, radial.y) * Mathf.Rad2Deg;
            var cands = new List<Piece>();
            foreach (var p in _pieces) if (p.Wall && !p.Hidden && p.HopT1 <= 0f) cands.Add(p);
            cands.Sort((p, q) => Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.Pos.x, p.Pos.y) * Mathf.Rad2Deg, bearing)).CompareTo(Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(q.Pos.x, q.Pos.y) * Mathf.Rad2Deg, bearing))));
            int n = Mathf.Min(3 + _rng.Next(2), cands.Count);
            if (n == 0) return;
            Vector2 from = neighbour ? _primaryFeet.Value : cands[0].Pos;
            Vector2 to = neighbour || dist > _R + 0.6f ? feet : radial * (_R + 2.2f);
            Vector2 d = to - from; float len = d.magnitude;
            Vector2 dn = len > 1e-3f ? d / len : radial;
            float t1 = arrive;
            for (int k = 0; k < n; k++)
            {
                Piece p = cands[k];
                Vector2 target = from + dn * (len * (k + 1) / n);
                float t0 = arrive + 0.1f * k; t1 = t0 + 0.25f;
                float g = GroundAt(target);
                p.HopFrom = new Vector3(p.Pos.x, p.Ground + p.Y, p.Pos.y);
                p.HopTo = new Vector3(target.x, g + 0.4f, target.y);
                p.HopT0 = t0; p.HopT1 = t1;
                p.LeaveAt = t1 + 3.0f; p.LeaveLen = 0.25f;                        // the road it leaves: 3 s
                _endAt = Mathf.Max(_endAt, p.LeaveAt + 0.25f + 0.2f);
                _ink.Spit(t1, PlanW(new Vector3(target.x, g + 0.05f, target.y)), Vector3.up);
                if (k == n - 1) Schedule(p, t1);
            }
            Hold(hit, feet, t1, 5, Mathf.Min(LeaveOf(theta) + 0.1f, _fadeStart + _spread));
        }

        /// <summary>굼: the 3 eggs nearest the body go off - each flies to the body's surface and becomes one of its hold cells (a flash and a spit on the first) - and the hold lasts 3 s.</summary>
        void HitSeed(SpellFxHit hit, Vector2 feet, float arrive)
        {
            var near = new List<Piece>();
            foreach (var p in _pieces) if (p.Seed && p.UsedAt < 0f) near.Add(p);
            near.Sort((p, q) => (p.Pos - feet).sqrMagnitude.CompareTo((q.Pos - feet).sqrMagnitude));
            if (near.Count > 3) near.RemoveRange(3, near.Count - 3);
            float t = arrive;
            if (near.Count > 0) t = Mathf.Max(arrive, near[0].Born + 0.1f);
            var plates = Hold(hit, feet, t, 3, t + 3.0f);
            for (int i = 0; i < near.Count; i++)
            {
                Piece s = near[i];
                s.UsedAt = t;
                if (i < plates.Count)
                    _flights.Add(new HexFlight { From = PieceCtr(s, t), To = plates[i].Ctr, T0 = t, T1 = plates[i].Start + 0.05f, Rc = s.Rc * 0.9f, Arc = 0.15f, Hash = s.Hash, Sampled = true, Quick = true, Standing = true });
            }
            if (near.Count > 0)
            {
                Schedule(near[0], t);
                Vector3 c = PieceCtr(near[0], t);
                _ink.Spit(t, PlanW(new Vector3(c.x, near[0].Ground + 0.05f, c.z)), Vector3.up);
            }
        }

        /// <summary>굿: the body crossing the blades loses plates: torn armour plates in the body's own edge colour fly off it and two hold cells stay on it.</summary>
        void HitCut(SpellFxHit hit, Vector2 feet, float theta, float arrive)
        {
            Piece cell = NearestStanding(feet, arrive);
            if (cell != null) Schedule(cell, arrive);
            Vector3 outward = feet.sqrMagnitude > 1e-4f ? (_right * feet.x + _fwd * feet.y).normalized : _fwd;
            _ink.Plates(arrive, hit.Position, (outward + Vector3.up * 0.5f).normalized, _sheet.Main.RimColor);          // the body's own edge colour (_Rim), as the vine's plates took
            Hold(hit, feet, arrive, 2, arrive + 4.0f);
        }

        /// <summary>궁: out - a flash, a spit and the hold; back (hit kind Return) - a second flash and spit and the hold lets go.</summary>
        void HitReturn(SpellFxHit hit, long key, Vector2 feet, bool back, float arrive)
        {
            Piece cell = NearestStanding(feet, arrive);
            if (cell != null) Schedule(cell, arrive);
            if (!_holds.TryGetValue(key, out var held))
            {
                held = Hold(hit, feet, arrive, 4, back ? arrive + 0.4f : returnLegSeconds * 2f + 0.3f);
                _holds[key] = held;
                if (back) return;                                                   // the hold's own contact flash and spit are this one
            }
            if (back)
            {
                for (int k = 0; k < held.Count; k++) held[k].LeaveAt = Mathf.Min(held[k].LeaveAt, arrive + 0.35f + 0.02f * k);
                if (held.Count > 0) { Schedule(held[0], arrive); _ink.Spit(arrive, HexHolds.ContactPoint(_sheet, held[0]), HexHolds.ContactNormal(_sheet, held[0])); }
            }
        }

        // ------------------------------------------------------------ mesh

        float VisOf(Piece p, float a)
        {
            if (p.Hidden) return 0f;
            float v = p.Born >= 0f ? Smooth((a - p.Born) / 0.18f) : 1f;
            float ll = p.LeaveLen > 0f ? p.LeaveLen : _leaveLen;
            if (a > p.LeaveAt) v *= 1f - Smooth((a - p.LeaveAt) / ll);
            if (p.UsedAt >= 0f) v *= 1f - Smooth((a - p.UsedAt) / 0.15f);
            return v;
        }

        void Rebuild(float a)
        {
            _sheet.Begin();
            foreach (var p in _pieces)
            {
                float vis = VisOf(p, a);
                if (vis <= 0.02f) continue;
                EmitPiece(p, vis, a);
                if (p.Seed) EmitThread(p, vis, a);
            }
            foreach (var p in _plates)
            {
                float g = HexHolds.PlateG(p, a, _leaveLen);
                if (g > 0.02f) _sheet.EmitPlate(p, g);
            }
            if (bite == HexRingBite.Cut) EmitCrack(a);
            foreach (var f in _flights)
            {
                if (a < f.T0 || a >= f.T1) continue;
                if (!f.Sampled) _sheet.SampleFlight(f);
                _sheet.EmitFlight(f, a);
            }
            _sheet.Flush();
        }

        /// <summary>One standing cell: out (or to the camera for an egg), leaning outward, the kit's per-cell shading normal. 궁's cells are a third bigger at the far end; an egg pulses 0.8 Hz +- 8 %.</summary>
        void EmitPiece(Piece p, float vis, float a)
        {
            Vector3 c = PieceCtr(p, a);
            float k = bite == HexRingBite.Return && p.Wall ? 1f + 0.35f * Tri(a) : 1f;
            if (p.Seed) k *= 1f + 0.08f * Mathf.Sin(a * Mathf.PI * 2f * 0.8f + p.Phase);
            float s = vis * k;
            _sheet.EmitStanding(c, p.Face ? _sheet.ToCamPlan : p.Out, p.Rc * s, p.Rv * s, p.Lean, p.Hash, p.Pat, 0.7f);
        }

        /// <summary>굼: the thin vertical ink thread an egg hangs on, from the ground to the egg's lower corner (plain dark glass, a hair wide, swaying a little), drawn to the camera's side.</summary>
        void EmitThread(Piece p, float vis, float a)
        {
            Vector3 c = PieceCtr(p, a);
            float y0 = p.Ground + 0.02f, y1 = c.y - p.Rv * 0.8660254f * vis;
            if (y1 - y0 < 0.05f) return;
            Vector2 across = new Vector2(_sheet.ToCamPlan.y, -_sheet.ToCamPlan.x);
            float hw = 0.0125f * Mathf.Clamp01(vis * 2f), sway = 0.012f * Mathf.Sin(a * 2.4f + p.Phase);
            Vector3 b0 = new Vector3(c.x + across.x * (sway - hw), y0, c.z + across.y * (sway - hw)), b1 = new Vector3(c.x + across.x * (sway + hw), y0, c.z + across.y * (sway + hw));
            Vector3 t0 = new Vector3(c.x + across.x * (0f - hw), y1, c.z + across.y * (0f - hw)), t1 = new Vector3(c.x + across.x * hw, y1, c.z + across.y * hw);
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            Vector3 front = new Vector3(_sheet.ToCamPlan.x, 0.15f, _sheet.ToCamPlan.y);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, _R * 1.25f, true, front);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, _R * 1.25f, true, front);
        }

        /// <summary>굿: the dark crack round the foot of the blades - a 5 cm ring line of plain dark glass (no seam, no pattern; the shader has no black, so it is drawn three times for body) on the ground, drawn as the
        /// blades are (from the start point both ways round, out of the camera's cone) and going as they go.</summary>
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
                float vis = InCone(phi) ? 0f : Smooth((a - BornOf(th)) / 0.18f) * (1f - Smooth((a - LeaveOf(th)) / _leaveLen));
                float w = 0.05f * vis;
                Vector2 n = Dir(phi), p = n * _R;
                if (have && (w0 > 0.005f || w > 0.005f))
                {
                    Vector3 a0 = CrackPt(p0, n0, -w0 * 0.5f), b0 = CrackPt(p0, n0, w0 * 0.5f), a1 = CrackPt(p, n, -w * 0.5f), b1 = CrackPt(p, n, w * 0.5f);
                    for (int r = 0; r < 3; r++) _sheet.Main.QuadVerts(a0, b0, a1, b1, sh, reach, true);
                }
                p0 = p; n0 = n; w0 = w; have = true;
            }
        }

        Vector3 CrackPt(Vector2 p, Vector2 n, float off) { Vector2 q = p + n * off; return new Vector3(q.x, _sheet.Ground.Q(p) + 0.012f, q.y); }
    }
}
