using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅏ 경로 in the hex-cell membrane grammar (lane D of PLAN-308-MEMBRANE-SPELLS, section 2.4; the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"): the SHOT is a chain of
    /// hexagon cells (a lead cell x1.3 and four behind it, 0.35 m apart, standing and leaning 30 deg toward the camera, a thin ink tail that dies in 0.3 s), shot from the brush tip at
    /// 22 m/s (TEST, 20 - 25) along the aim. The rule's path is faster (36 m/s) so the hit arrives first: its look is held until the lead reaches the struck body (the vine's way: the hit is
    /// kept and the chain flies to the very point the bite starts from). Built on <see cref="SpellFxHexKit"/> (frame, sheet, hold cells on the body's real surface, flash and ink queues).
    /// Everything stands in an UPRIGHT sunk frame (the brush tip is beside the caster, the fan's lying frame cannot draw there); a standing cell is drawn with the kit's per-cell shading normal
    /// (<see cref="HexSheet.EmitStanding"/>) so its fill is kept at the shoulder camera. Mid-way ink (the brush, a landing, a contact) is always the small plain ink; only the END bursts as the
    /// body (wood: none). Look only - nothing here touches a rule.
    /// Verbs (times from the launch; Tl = the lead's arrival, TEST):
    ///  가 Once    the lead lands on the body's flank (the hold's first cell), the four behind peel off onto the other hold cells (46 - 70 % of its height, none over the face): a flash, a small ink
    ///             spit, a 2-frame visual stop of the chain; a faint ink wash 0.6 m long at the foot toward the caster; the cells break up at Tl + 2.0. The brush-tip cell is drawn from the commit
    ///             (<see cref="ISpellFxPreLaunch"/>, the kit's tip preview).
    ///  각 Cage    as 가 (3 hold cells, 1.5 s), then 8 - 10 cells rise round the body into a cage ring (0.5 - 1.4 m, the caster side kept short), follow it for 1.5 s, then stay where they stand
    ///             until Tl + 10.
    ///  간 Spread  as 가 on the first body; a later contact of ANOTHER body (kind "Spread") sends three cells hopping from the first body's hold cell (0.1 s apart, 0.25 s each, apex 0.55 - 0.7 m);
    ///             two land and stay at knee height as a road for 3 s, the third lands on the neighbour and holds it for 2 s.
    ///  감 Seed    the lead alone stays, lodged on the body's flank (breathing, +- 3 cm, a thin vertical ink thread over it); the cells behind scatter into ink. A later contact of the SAME
    ///             body sets it off: it bursts into four hold cells and a flash. Without one it ends after 8 s.
    ///  갓 Cut     the chain stands edge-on like blades and goes THROUGH the body (a 2-frame stop at the crossing, torn armour plates in the body's edge colour, two hold cells for 2.5 s) and
    ///             on 1 m, then breaks into ink; a dark crack 4 cm wide runs along the ground under it (grown under the lead, 3 s).
    ///  강 Return  the lead touches (a flash, two cells stay on the body for 2 s), then turns, goes back to the brush tip it has now (0.4 s, an oval: out on one side, back on the other) and
    ///             flashes and spits there.
    ///  낙 Residue the chain lands (two cells, 1.2 s) and 3 - 4 cells stack into a small pile on the ground a little toward the caster of the body, kept 10 s, then the top goes first
    ///             (the body's end burst: fire, embers up). No extreme-glyph mark (낙 is the plain 'leave it' verb).</summary>
    public sealed class SpellFxHexPathPart : MonoBehaviour, ISpellFxPart, ISpellFxPreLaunch
    {
        /// <summary>가 Once · 각 Cage · 간 Spread · 감 Seed · 갓 Cut · 강 Return · 낙 Residue (the order is written into prefabs by index).</summary>
        public enum HexPathBite { Once, Cage, Spread, Seed, Cut, Return, Residue }

        [SerializeField] Material material;
        [SerializeField] HexPathBite bite = HexPathBite.Once;
        [Tooltip("What the END burst is made of (cells breaking up; Wood = none). The brush / landing / contact spits are always the small plain ink.")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Water;

        [Header("Chain (the shot)")]
        [Tooltip("Visible head speed in m/s (the rule's path is 36; TEST 20 - 25).")]
        [SerializeField] float headSpeed = 22f;
        [Tooltip("The lead cell plus the cells behind it, and their spacing (m).")]
        [SerializeField] int chainCells = 5;
        [SerializeField] float chainSpacing = 0.35f;
        [Tooltip("Circumradius of a follower (m, 0.16 - 0.2); the lead is x leadScale.")]
        [SerializeField] float cellRadius = 0.18f;
        [SerializeField] float leadScale = 1.3f;
        [Tooltip("A flying cell stands leaning this far toward the camera (deg).")]
        [SerializeField] float tiltDeg = 30f;
        [Tooltip("The ink tail: how long it takes to die (s) and how wide it is at the lead (m).")]
        [SerializeField] float inkTailSeconds = 0.3f, inkTailWidth = 0.09f;
        [Tooltip("The chain stands still this long (s, about 2 frames) at the contact: a visual hit-stop only.")]
        [SerializeField] float hitStopSeconds = 0.034f;
        [Tooltip("Used when the cast carries no spec: how far the chain flies without a hit (m).")]
        [SerializeField] float maxReach = 12f;
        [SerializeField] bool followGround = true;

        [Header("Verbs")]
        [Tooltip("Seconds a hold lasts after the lead lands (the rule's 2 s grip; 갓 hold, 낙 mark and 각 hold are set per verb).")]
        [SerializeField] float holdSeconds = 2f, cutHoldSeconds = 2.5f, cageHoldSeconds = 1.5f, residueMarkSeconds = 1.2f;
        [Tooltip("간: seconds between hops, one hop's flight, how long a road cell stays.")]
        [SerializeField] float hopInterval = 0.1f, hopSeconds = 0.25f, roadSeconds = 3f;
        [Tooltip("각: cells in the ring, how long they stand after the contact (s), their size.")]
        [SerializeField] int cageCells = 10;
        [SerializeField] float cageStandSeconds = 10f, cageCellRadius = 0.2f;
        [Tooltip("감: how long the seed waits (s).")]
        [SerializeField] float seedWaitSeconds = 8f;
        [Tooltip("갓: how far past the body's centre the blades go (m), the crack's width (m) and life (s).")]
        [SerializeField] float cutThrough = 1f, crackWidth = 0.04f, crackSeconds = 3f;
        [Tooltip("강: the pause at the body before the turn (s after the last cell arrives) and the way home (s).")]
        [SerializeField] float returnPauseSeconds = 0.05f, returnSeconds = 0.4f;
        [Tooltip("낙: how long the pile stays (s) and how many cells it is.")]
        [SerializeField] float pileSeconds = 10f;
        [SerializeField] int pileCells = 4;

        const float ClockBias = 0.5f;          // the shader fades a layer in over its first 0.16 s of _Age: an offset clock keeps a shot cell opaque from its first frame
        const int TailN = 8;

        static readonly int[] PatChoices = SpellFxHexKit.PatChoices;
        static float Smooth(float x) => SpellFxHexKit.Smooth(x);

        // ------------------------------------------------------------ state

        /// <summary>One cell of the shot. It is drawn on the path until <see cref="GoneAt"/> and shrinks away over the <see cref="FadeLen"/> before it (0 = it simply stops: a cell that flies on to a hold cell).</summary>
        sealed class Link { public float Hash; public int Pat; public float GoneAt = float.MaxValue, FadeLen = 0.08f; }

        /// <summary>Any other standing cell: a cage bar, a hopping cell and the road it becomes, the seed, a pile cell.</summary>
        sealed class Cell
        {
            public Vector3 Pos, From;                 // plan: where it stands / where a hop starts
            public Vector2 Dir; public float Rx, Rv, Lean;
            public float Hash; public int Pat;
            public float Born, GrowLen = 0.25f, LeaveAt = float.MaxValue, LeaveLen = 0.25f, UsedAt = -1f;
            public float Rise;                        // metres it climbs while it grows in
            public bool Hop, Seed;
            public float HopT0, HopT1, Arc, Phase;
            public Held Group;
        }

        /// <summary>Things that stand on a struck body follow its transform (a push may move it): the group's offset is the body's move since the hit; frozen at <see cref="FreezeAt"/>.</summary>
        sealed class Held
        {
            public Transform Tr; public Vector3 Anchor; public float FreezeAt = float.MaxValue; public Vector3 Delta; public bool Frozen;
            public readonly List<HexPlate> Plates = new List<HexPlate>();
            public readonly List<Vector3> Base = new List<Vector3>();
            public void Add(HexPlate p) { Plates.Add(p); Base.Add(p.Ctr); }
            public void Step(HexSheet s, float a)
            {
                if (!Frozen)
                {
                    if (a >= FreezeAt) Frozen = true;
                    else if (Tr != null) Delta = s.ToPlan(Tr.position) - Anchor;
                }
                for (int i = 0; i < Plates.Count; i++) Plates[i].Ctr = Base[i] + Delta;
            }
        }

        struct Stop { public float At, Len; }
        struct Call { public float At; public System.Action Do; }

        readonly List<Link> _links = new List<Link>();
        readonly List<Cell> _cells = new List<Cell>();
        readonly List<HexPlate> _plates = new List<HexPlate>();
        readonly List<HexFlight> _flights = new List<HexFlight>();
        readonly List<Held> _groups = new List<Held>();
        readonly List<Stop> _stops = new List<Stop>();
        readonly List<Call> _calls = new List<Call>();
        readonly HexFlashQueue _flash = new HexFlashQueue();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly HexTipPreview _preview = new HexTipPreview();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly Vector3[] _tail = new Vector3[TailN + 1];
        readonly RaycastHit[] _hits = new RaycastHit[8];
        readonly Dictionary<long, float> _groundCache = new Dictionary<long, float>();

        HexSheet _sheet;
        System.Random _rng;
        bool _built, _launched, _ended, _hasHit, _leadEndDone;
        float _age, _lastAge, _airBias, _reach, _endAt, _tailUntil;
        Vector3 _origin, _fwd, _right;

        // the shot's path (plan space: x right, y up from the cast origin, z ahead)
        Vector3 _start, _aim, _end, _endNow;
        Vector2 _dir, _side;
        float _bulge, _lenNow, _totalOut, _tl;

        // the struck body
        HexBody _body;
        Transform _targetTr;
        Held _group, _cageGroup;
        HexPlate _primaryFront;
        long _primaryKey;
        bool _hasPrimary;

        // verb state
        float _washT0 = -1f, _washY; Vector2 _washFrom, _washDir;
        readonly List<Vector3> _crack = new List<Vector3>();
        bool _crackOn;
        Cell _seedCell; bool _seedActivated; Held _actGroup;
        float _retStart = -1f; Vector3 _homeNow;
        const float ReturnLag = 0.03f;

        // ------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast) { _rng = new System.Random((int)(cast.CastId & 0x7fffffff)); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            if (!_built) return;
            _launched = true;
            _preview.Stop();
            _start = _sheet.SampleTipPlan();                 // the brush tip NOW: the chain leaves from the very point (the preview cell becomes the lead)
            _sheet.TipPlan = _start;
            RecalcPath();
            _totalOut = _lenNow;
            _sheet.SetActive(true);
            _ink.Spit(0f, PlanW(_start), (_fwd * 0.6f + Vector3.up).normalized);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;
            _ended = true;
            _preview.Stop();
            if (_sheet != null) _sheet.SetActive(false);
        }

        /// <summary>한 번 only: while the great brush swings to the apex a small cell takes shape at its tip and ink motes are drawn into it; at the launch that cell is the lead.</summary>
        public void OnPreLaunch(SpellFxCast cast, float secondsToLaunch)
        {
            if (bite != HexPathBite.Once || _ended || _launched) return;
            Build(cast);
            if (!_built || _links.Count == 0) return;
            float delay = cast.LaunchDelaySeconds, since = Mathf.Max(0f, delay - secondsToLaunch);
            _sheet.Air.Go.SetActive(true);
            _sheet.Air.SetClock(since, 0f);
            _sheet.Air.Breathe(since);
            _sheet.Air.Begin();
            _preview.Show(_sheet, transform, since, delay, cellRadius * leadScale * 0.8f, _links[0].Hash);
            _sheet.Air.Flush();
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _ended || _sheet == null) return;
            float a = ageSeconds, dt = Mathf.Clamp(a - _lastAge, 0f, 0.1f);
            _lastAge = a;
            _sheet.Main.SetClock(a + ClockBias, 0f);
            _sheet.Air.SetClock(a + _airBias + ClockBias, 0f);
            if (a > _endAt) { if (_sheet.Main.Go.activeSelf) _sheet.SetActive(false); return; }
            // places first (the path end, the tip, the cells that follow a body), THEN the flash: it asks a cell's centre and must get the one the mesh below is built with
            UpdateAim(a, dt);
            foreach (var g in _groups) g.Step(_sheet, a);
            DrainCalls(a);
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

        // ------------------------------------------------------------ helpers

        float Rand() => (float)_rng.NextDouble();
        float PickHash(float phase) => SpellFxHexKit.PickHash(_rng, phase);
        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;
        static Vector3 Flat(Vector2 v, float y) => new Vector3(v.x, y, v.y);
        void Extend(float t) { if (t > _endAt) _endAt = t; }

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

        /// <summary>Time on the chain's own clock: the real time minus the hit-stops it has stood still for.</summary>
        float Vis(float a)
        {
            float v = a;
            for (int i = 0; i < _stops.Count; i++) v -= Mathf.Clamp(a - _stops[i].At, 0f, _stops[i].Len);
            return v;
        }

        /// <summary>The ground under a plan point (x, z) for things that stand under or beside the struck body: a ray that skips the body's own colliders (the kit's ground only skips rigid bodies,
        /// and a static-collider body would give its back). Kept on a 0.2 m lattice.</summary>
        float GroundSkip(Vector2 p)
        {
            if (!followGround) return 0f;
            int qx = Mathf.RoundToInt(p.x / 0.2f), qz = Mathf.RoundToInt(p.y / 0.2f);
            long key = SpellFxHexKit.Key(qx, qz);
            if (_groundCache.TryGetValue(key, out float cached)) return cached;
            Vector3 from = _origin + _right * (qx * 0.2f) + _fwd * (qz * 0.2f) + Vector3.up * 1.6f;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 3.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.collider is CharacterController || h.normal.y < 0.5f || IsBodyCollider(h.collider)) continue;
                best = Mathf.Max(best, h.point.y - _origin.y);
            }
            float g = best > float.NegativeInfinity ? Mathf.Clamp(best, -1f, 1f) : 0f;
            _groundCache[key] = g;
            return g;
        }

        bool IsBodyCollider(Collider c)
        {
            if (_targetTr != null && c.transform.IsChildOf(_targetTr)) return true;
            if (_body != null && _body.Cols != null) foreach (var b in _body.Cols) if (b == c) return true;
            return false;
        }

        // ------------------------------------------------------------ build

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            _airBias = cast.LaunchDelaySeconds;
            _reach = cast.Spec != null && cast.Spec.PathLength > 0.5f ? Mathf.Min(maxReach, cast.Spec.PathLength) : maxReach;
            // an UPRIGHT frame sunk 1 m (the brush tip is beside the caster; the shader's base-ring term is 0 and nothing is discarded); the brush tip is sampled in the sheet's constructor
            _sheet = new HexSheet(transform, cast, material, HexFrame.UprightSunk(_origin, _right, _fwd, 1f), 0f, followGround, "HexPath");
            _start = _sheet.TipPlan;
            _aim = new Vector3(0f, 1f, _reach);
            _end = _endNow = _aim;
            _bulge = (Rand() < 0.5f ? -1f : 1f) * (bite == HexPathBite.Cut ? 0.04f : 0.16f);
            int n = Mathf.Clamp(chainCells, 2, 6);
            for (int k = 0; k < n; k++) _links.Add(new Link { Hash = PickHash(Rand()), Pat = PatChoices[_rng.Next(PatChoices.Length)] });
            RecalcPath();
            _totalOut = _lenNow;
            _endAt = _reach / headSpeed + 1.4f;
            _tailUntil = _reach / headSpeed + inkTailSeconds + 0.2f;
            _built = true;
        }

        /// <summary>The out leg's length and side from the start to the end now.</summary>
        void RecalcPath()
        {
            Vector3 d = _endNow - _start;
            _lenNow = Mathf.Max(0.5f, d.magnitude);
            Vector2 h = new Vector2(d.x, d.z);
            _dir = h.sqrMagnitude > 1e-4f ? h.normalized : new Vector2(0f, 1f);
            _side = new Vector2(_dir.y, -_dir.x);
        }

        /// <summary>Before the hit the chain flies at the aim end; once the hit is known the end slides to the landing point (snapped when the lead has arrived).</summary>
        void UpdateAim(float a, float dt)
        {
            Vector3 target = _hasHit ? _end : _aim;
            if (_hasHit && a >= _tl) _endNow = _end;
            else _endNow = Vector3.MoveTowards(_endNow, target, 60f * dt);
            RecalcPath();
            if (!_hasHit) _totalOut = _lenNow;
            if (_hasHit && bite == HexPathBite.Return && a >= _retStart - 0.15f) _homeNow = _sheet.SampleTipPlan();
        }

        // ------------------------------------------------------------ the chain's places

        /// <summary>A point on the out leg <paramref name="s"/> metres from the start: a straight line with a small side bulge and arc; beyond the end (갓) straight on.</summary>
        Vector3 PathAt(float s)
        {
            if (s <= _lenNow)
            {
                float u = Mathf.Max(0f, s) / _lenNow, sn = Mathf.Sin(Mathf.PI * u);
                Vector3 b = Vector3.Lerp(_start, _endNow, u);
                return b + new Vector3(_side.x, 0f, _side.y) * (_bulge * sn) + Vector3.up * (0.08f * sn);
            }
            Vector3 dir = (_endNow - _start).normalized;
            return _endNow + dir * (s - _lenNow);
        }

        /// <summary>강: the way back, an oval with the out leg (the bulge on the other side, wider), to the brush tip as it is now.</summary>
        Vector3 RetPos(float u)
        {
            float sn = Mathf.Sin(Mathf.PI * u);
            Vector3 p = Vector3.Lerp(_end, _homeNow, u);
            return p + new Vector3(_side.x, 0f, _side.y) * (-_bulge * 2.4f * sn) + Vector3.up * (0.15f * sn);
        }

        float ReturnU(int k, float a) => _retStart < 0f ? -1f : Mathf.Clamp01((a - _retStart - ReturnLag * k) / returnSeconds);

        /// <summary>Where chain cell <paramref name="k"/> is at time <paramref name="a"/>.</summary>
        Vector3 LinkPos(int k, float a)
        {
            if (bite == HexPathBite.Return && _hasHit && _retStart >= 0f && a >= _retStart + ReturnLag * k) return RetPos(Smooth(ReturnU(k, a)));
            float s = HeadS(a) - k * chainSpacing;
            return PathAt(Mathf.Clamp(s, 0f, _totalOut));
        }

        /// <summary>How far the lead would be along the out leg (not clamped): the chain's own clock times its speed.</summary>
        float HeadS(float a) => headSpeed * Vis(a);

        // ------------------------------------------------------------ hits

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (!_built || _ended || !_launched) return;
            long key = hit.TargetId != 0 ? hit.TargetId : 1000000L + _seen.Count;
            Vector3 rel = hit.Feet - _origin;
            var feet = new Vector2(Vector3.Dot(rel, _right), Vector3.Dot(rel, _fwd));
            if (!_hasPrimary)
            {
                _hasPrimary = true; _primaryKey = key; _seen.Add(key);
                Primary(hit, feet);
                return;
            }
            if (!_seen.Add(key)) { if (key == _primaryKey) SecondHit(); return; }          // the first body again: a follow-up contact
            if (bite == HexPathBite.Spread) Neighbour(hit, feet);                           // another body: only the spread answers
        }

        int PlatesFor(HexPathBite b)
        {
            switch (b)
            {
                case HexPathBite.Once: return 5;
                case HexPathBite.Cage: return 3;
                case HexPathBite.Spread: return 4;
                case HexPathBite.Seed: return 1;
                default: return 2;                    // Cut, Return, Residue
            }
        }

        /// <summary>The first body: where the lead lands, when, and what each verb makes of it.</summary>
        void Primary(SpellFxHit hit, Vector2 feet)
        {
            _body = HexHolds.BodyOf(_sheet, hit, feet);
            _targetTr = hit.Target;
            _group = NewGroup(_targetTr);
            var plates = new List<HexPlate>();
            HexPlate front = HexHolds.Add(_sheet, _rng, plates, _body, 0f, PlatesFor(bite), 0f, _reach * 1.25f);
            if (front == null) return;
            _primaryFront = front;
            _hasHit = true;

            // the end of the out leg: the lead's landing cell on the body's surface; 갓 goes through the body's axis at that height
            Vector3 e = front.Ctr;
            if (bite == HexPathBite.Cut) e = new Vector3(_body.Centre.x, front.Ctr.y, _body.Centre.y);
            _end = e;
            float len = Vector3.Distance(_start, e);
            float tl = Mathf.Max(len / headSpeed, _age);
            _tl = tl;
            _totalOut = bite == HexPathBite.Cut ? len + cutThrough : len;
            if (_age <= 0.02f) _endNow = e;                                  // known in the commit frame: no slide
            RecalcPath();
            _stops.Add(new Stop { At = tl, Len = hitStopSeconds });
            var arr = new float[_links.Count];
            for (int k = 0; k < arr.Length; k++) arr[k] = tl + k * chainSpacing / headSpeed + (k > 0 ? hitStopSeconds : 0f);
            float last = arr[arr.Length - 1];
            _tailUntil = last + inkTailSeconds + 0.3f;

            switch (bite)
            {
                case HexPathBite.Once: DoOnce(plates, front, arr); break;
                case HexPathBite.Cage: DoCage(plates, front, arr); break;
                case HexPathBite.Spread: DoLand(plates, front, arr, tl + holdSeconds, 0, true); Extend(tl + holdSeconds + 1f); break;
                case HexPathBite.Seed: DoSeed(front, arr); break;
                case HexPathBite.Cut: DoCut(plates, front, arr); break;
                case HexPathBite.Return: DoReturn(plates, front, arr); break;
                default: DoResidue(plates, front, arr); break;
            }
        }

        Held NewGroup(Transform tr)
        {
            var g = new Held { Tr = tr, Anchor = tr != null ? _sheet.ToPlan(tr.position) : Vector3.zero };
            _groups.Add(g);
            return g;
        }

        /// <summary>Chain cell (i + <paramref name="shift"/>) becomes hold cell i: the lead (cell 0) lands as the first one and hands over to it; each cell behind flies on from the landing point to its
        /// own hold cell (0.11 s) as it arrives. With <paramref name="shift"/> 1 (강) the lead is not one of them - it goes home. Cells with no hold cell scatter into one small spit
        /// (<paramref name="dissolveRest"/>). A hold cell's own growth is set short (0.1 s) so the chain cell and the plate hand over at the contact. Then the flash and spit at the contact and the
        /// end: two hold cells break up as the body.</summary>
        void DoLand(List<HexPlate> plates, HexPlate front, float[] arr, float holdEnd, int shift, bool dissolveRest)
        {
            for (int i = 0; i < plates.Count; i++)
            {
                var p = plates[i];
                int k = i + shift;
                float ak = arr[Mathf.Min(k, arr.Length - 1)];
                p.Grow = 0.1f;
                p.Start = k == 0 ? ak + 0.02f : ak + 0.06f;
                p.LeaveAt = holdEnd + 0.02f * i;
                _plates.Add(p); _group.Add(p);
                if (k > 0 && k < _links.Count)
                {
                    _flights.Add(new HexFlight { From = _end, To = p.Ctr, T0 = ak, T1 = p.Start + 0.05f, Rc = cellRadius * 0.9f, Arc = 0.1f, Hash = _links[k].Hash, Sampled = true, Quick = true, Standing = true });
                    _links[k].GoneAt = ak; _links[k].FadeLen = 0f;
                }
            }
            if (shift == 0) _links[0].GoneAt = arr[0] + 0.08f;
            if (dissolveRest)
            {
                bool first = true;
                for (int k = plates.Count + shift; k < _links.Count; k++)
                {
                    if (k == 0) continue;
                    _links[k].GoneAt = arr[k]; _links[k].FadeLen = 0.04f;
                    if (first) { _ink.Spit(arr[k], PlanW(_end), Vector3.up); first = false; }
                }
            }
            // a flash and one small spit at the contact (the lead's cell), then the end: two cells break up as the body
            float tt = front.Start + 0.05f;
            _flash.Schedule(() => front.Ctr, tt);
            _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            for (int i = 0; i < plates.Count && i < 2; i++)
                _ink.End(plates[i].LeaveAt, PlanW(plates[i].Ctr), Vector3.up, burstBody);
            Extend(holdEnd + 0.8f);
        }

        // ---- 가

        void DoOnce(List<HexPlate> plates, HexPlate front, float[] arr)
        {
            DoLand(plates, front, arr, arr[0] + holdSeconds, 0, true);
            // the wash: a faint ink line 0.6 m long at the foot, from the body toward the caster
            Vector2 c = _body.Centre;
            Vector2 toCaster = c.sqrMagnitude > 1e-4f ? -c.normalized : new Vector2(0f, -1f);
            _washFrom = c + toCaster * (_body.Radius + 0.1f);
            _washDir = toCaster;
            _washY = GroundSkip(_washFrom) + 0.012f;
            _washT0 = arr[0];
            Extend(arr[0] + 2.2f);
        }

        // ---- 각

        void DoCage(List<HexPlate> plates, HexPlate front, float[] arr)
        {
            float tl = arr[0];
            DoLand(plates, front, arr, tl + cageHoldSeconds, 0, true);
            _cageGroup = new Held { Tr = _targetTr, Anchor = _group.Anchor, FreezeAt = tl + cageHoldSeconds };
            _groups.Add(_cageGroup);
            Vector2 c = _body.Centre;
            Vector2 toCaster = c.sqrMagnitude > 1e-4f ? -c.normalized : new Vector2(0f, -1f);
            float baseAng = Mathf.Atan2(toCaster.y, toCaster.x), ringR = _body.Radius + 0.3f, ground = GroundSkip(c);
            int n = Mathf.Clamp(cageCells, 6, 12);
            var list = new List<Cell>();
            for (int k = 0; k < n; k++)
            {
                float ang = baseAng + (k + 0.5f) / n * Mathf.PI * 2f;
                float diff = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, baseAng * Mathf.Rad2Deg));
                if (diff < 38f) continue;                                    // the caster's side stays open: the face is not covered
                Vector2 o = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float h = 0.5f + (list.Count % 3) * 0.32f + Rand() * 0.12f;
                if (diff < 75f) h = Mathf.Min(h, 0.62f);                    // short on the caster's side
                var cell = new Cell
                {
                    Pos = Flat(c + o * ringR, ground + h), Dir = o, Rx = cageCellRadius, Rv = cageCellRadius * 1.4f, Lean = (10f + 5f * Rand()) * Mathf.Deg2Rad,
                    Hash = PickHash(Rand()), Pat = PatChoices[_rng.Next(PatChoices.Length)], GrowLen = 0.28f, Rise = 0.35f, Group = _cageGroup,
                };
                list.Add(cell);
            }
            // rising one after another from the caster's right round to the left, a spit at the foot of every other bar
            list.Sort((p, q) => Mathf.DeltaAngle(Mathf.Atan2(p.Dir.y, p.Dir.x) * Mathf.Rad2Deg, baseAng * Mathf.Rad2Deg).CompareTo(Mathf.DeltaAngle(Mathf.Atan2(q.Dir.y, q.Dir.x) * Mathf.Rad2Deg, baseAng * Mathf.Rad2Deg)));
            float leave0 = tl + cageStandSeconds;
            for (int i = 0; i < list.Count; i++)
            {
                var cell = list[i];
                cell.Born = tl + 0.25f + 0.045f * i;
                float distN = Mathf.Clamp01(new Vector2(cell.Pos.x, cell.Pos.z).magnitude / (_reach + 1f));
                cell.LeaveAt = leave0 + 0.4f * (1f - distN);                  // far ones go first
                _cells.Add(cell);
                if (i % 2 == 0) _ink.Spit(cell.Born, PlanW(new Vector3(cell.Pos.x, ground + 0.05f, cell.Pos.z)), Vector3.up);
                if (i % 3 == 0) _ink.End(cell.LeaveAt, PlanW(cell.Pos), Vector3.up, burstBody);
            }
            // let go: it stays where it stands (a small spit at three feet)
            for (int i = 0; i < list.Count && i < 3; i++)
            {
                var cell = list[(i * 2 + 1) % list.Count];
                _ink.Spit(tl + cageHoldSeconds, PlanW(new Vector3(cell.Pos.x, ground + 0.05f, cell.Pos.z)), Vector3.up);
            }
            Extend(leave0 + 1.2f);
            _tailUntil = Mathf.Max(_tailUntil, arr[arr.Length - 1] + 0.6f);
        }

        // ---- 감

        void DoSeed(HexPlate front, float[] arr)
        {
            float tl = arr[0];
            var seed = new Cell
            {
                Pos = front.Ctr, Dir = front.Dir, Rx = cellRadius * leadScale, Rv = cellRadius * leadScale, Lean = 0.12f, Hash = _links[0].Hash, Pat = _links[0].Pat,
                Born = tl + 0.02f, GrowLen = 0.12f, LeaveAt = tl + seedWaitSeconds, LeaveLen = 0.3f, Seed = true, Phase = Rand() * 6.28f, Group = _group,
            };
            _cells.Add(seed); _seedCell = seed;
            _links[0].GoneAt = tl + 0.08f;
            bool first = true;
            for (int k = 1; k < _links.Count; k++)
            {
                _links[k].GoneAt = arr[k];
                if (k % 2 == 1) _ink.Spit(arr[k], PlanW(_end), (Vector3.up + new Vector3(_side.x, 0f, _side.y) * (first ? 0.6f : -0.6f)).normalized);
                first = false;
            }
            float tt = tl + 0.07f;
            _flash.Schedule(() => CellPos(seed, _age), tt);
            _ink.Spit(tt, PlanW(front.Ctr) + new Vector3(front.Dir.x, 0f, front.Dir.y) * 0.04f, (new Vector3(front.Dir.x, 0f, front.Dir.y) * 0.8f + Vector3.up * 0.4f).normalized);
            _ink.End(seed.LeaveAt, PlanW(front.Ctr), Vector3.up, burstBody);
            Extend(seed.LeaveAt + 1f);
        }

        void SecondHit()
        {
            if (bite == HexPathBite.Seed && _seedCell != null && !_seedActivated) ActivateSeed(Mathf.Max(_age, _seedCell.Born + 0.2f));
            else if (bite == HexPathBite.Return && _plates.Count > 0 && _retStart >= 0f)
            {
                var p = _plates[0];
                float at = Mathf.Max(_age, _retStart + 0.02f);
                _flash.Schedule(() => p.Ctr, at);
                _ink.Spit(at, HexHolds.ContactPoint(_sheet, p), HexHolds.ContactNormal(_sheet, p));
            }
        }

        /// <summary>감: a later contact of the same body sets the seed off - the cell bursts into four hold cells on the body (a flash, a spit) for 2 s.</summary>
        void ActivateSeed(float at)
        {
            _seedActivated = true;
            _seedCell.UsedAt = at;
            var plates = new List<HexPlate>();
            HexPlate f = HexHolds.Add(_sheet, _rng, plates, _body, at, 4, at + holdSeconds, _reach * 1.25f);
            if (f == null) return;
            _actGroup = new Held { Tr = _targetTr, Anchor = _group.Anchor };
            _groups.Add(_actGroup);
            Vector3 seedPos = _seedCell.Pos + _group.Delta;
            _ink.Spit(at, PlanW(seedPos), Vector3.up);
            for (int i = 0; i < plates.Count; i++)
            {
                var p = plates[i];
                p.Grow = 0.1f; p.Start = at + 0.07f + 0.04f * i; p.LeaveAt = at + holdSeconds + 0.02f * i;
                _plates.Add(p); _actGroup.Add(p);
                _flights.Add(new HexFlight { From = seedPos, To = p.Ctr, T0 = at, T1 = p.Start + 0.05f, Rc = cellRadius * 0.8f, Arc = 0.12f, Hash = _links[Mathf.Min(i + 1, _links.Count - 1)].Hash, Sampled = true, Quick = true, Standing = true });
                if (i < 2) _ink.End(p.LeaveAt, PlanW(p.Ctr), Vector3.up, burstBody);
            }
            float tt = f.Start + 0.05f;
            _flash.Schedule(() => f.Ctr, tt);
            _ink.Spit(tt, HexHolds.ContactPoint(_sheet, f), HexHolds.ContactNormal(_sheet, f));
            Extend(at + holdSeconds + 1f);
        }

        // ---- 갓

        void DoCut(List<HexPlate> plates, HexPlate front, float[] arr)
        {
            float tl = arr[0];
            // two hold cells stay once the blades have passed (they grow in right after the crossing)
            for (int i = 0; i < plates.Count; i++)
            {
                var p = plates[i];
                p.Grow = 0.12f; p.Start = tl + 0.06f + 0.045f * i; p.LeaveAt = tl + cutHoldSeconds + 0.02f * i;
                _plates.Add(p); _group.Add(p);
                if (i == 0) _ink.End(p.LeaveAt, PlanW(p.Ctr), Vector3.up, burstBody);
            }
            float tt = front.Start + 0.05f;
            _flash.Schedule(() => front.Ctr, tt);
            _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            // the armour torn off: two or three plates in the body's own edge colour, thrown off the contact side (two small bursts)
            Vector3 outward = new Vector3(front.Dir.x, 0f, front.Dir.y);
            Vector3 world = PlanW(front.Ctr);
            Color rim = _sheet.Main.RimColor;
            At(tl, () => SpellFxLabShot.InkBurst(world, (_right * 0.6f + outward + Vector3.up * 0.5f).normalized, SpellFxBurstBody.Armour, rim, 0.3f));
            At(tl + 0.05f, () => SpellFxLabShot.InkBurst(world, (-_right * 0.6f + outward + Vector3.up * 0.4f).normalized, SpellFxBurstBody.Armour, rim, 0.3f));
            // the blades end in ink 1 m past the body
            float endT = tl + hitStopSeconds + cutThrough / headSpeed + (_links.Count - 1) * chainSpacing / headSpeed;
            _ink.Spit(tl + hitStopSeconds + cutThrough / headSpeed, PlanW(_end + new Vector3(_dir.x, 0f, _dir.y) * cutThrough), Vector3.up);
            // the crack along the ground: 4 cm, from 0.9 m ahead of the caster to the end, grown under the lead
            Vector2 d = new Vector2(_end.x, _end.z);
            float endDist = d.magnitude + cutThrough;
            Vector2 cdir = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(0f, 1f);
            float cseed = Rand() * 10f;
            _crack.Clear();
            const int N = 24;
            Vector2 side = new Vector2(cdir.y, -cdir.x);
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N, dist = Mathf.Lerp(0.9f, endDist, t);
                Vector2 q = cdir * dist + side * ((Mathf.PerlinNoise(t * 40f, cseed) - 0.5f) * 0.09f);
                _crack.Add(Flat(q, GroundSkip(q) + 0.012f));
            }
            _crackOn = true;
            Extend(tl + Mathf.Max(crackSeconds, cutHoldSeconds) + 1f);
            _tailUntil = Mathf.Max(_tailUntil, endT + inkTailSeconds + 0.2f);
        }

        // ---- 강

        void DoReturn(List<HexPlate> plates, HexPlate front, float[] arr)
        {
            float tl = arr[0];
            // cells 1 and 2 stay on the body as the two hold cells; the lead (and any behind them) turn and go home
            DoLand(plates, front, arr, tl + holdSeconds, 1, false);
            _retStart = arr[arr.Length - 1] + returnPauseSeconds;
            _homeNow = _sheet.SampleTipPlan();
            float home = _retStart + returnSeconds;
            for (int k = 0; k < _links.Count; k++)
            {
                if (k >= 1 && k <= plates.Count) continue;                // these became hold cells (their GoneAt is set by DoLand)
                _links[k].GoneAt = home + ReturnLag * k + (k == 0 ? 0.3f : 0.14f);
            }
            // the lead's own contact flash is the plate's (same place); at home: a flash on the lead, a small spit, the lead stays 0.3 s
            float th = home + 0.02f;
            _flash.Schedule(() => LinkPos(0, _age), th);
            At(home, () => SpellFxHexKit.Spit(PlanW(_homeNow), (Vector3.up + _fwd * 0.3f).normalized));
            _tailUntil = home + inkTailSeconds + 0.35f;
            Extend(Mathf.Max(home + 0.8f, tl + holdSeconds + 1f));
        }

        // ---- 낙

        void DoResidue(List<HexPlate> plates, HexPlate front, float[] arr)
        {
            float tl = arr[0];
            DoLand(plates, front, arr, tl + residueMarkSeconds, 0, true);
            // the pile: 3 - 4 cells stacked on the ground a little toward the caster of the body, one every 0.12 s, top first at the end
            Vector2 c = _body.Centre;
            Vector2 toCaster = c.sqrMagnitude > 1e-4f ? -c.normalized : new Vector2(0f, -1f);
            Vector2 p0 = c + toCaster * (_body.Radius + 0.4f);
            float g = GroundSkip(p0);
            int n = Mathf.Clamp(pileCells, 3, 5);
            float stack = g + 0.02f;
            float born0 = tl + 0.15f;
            for (int j = 0; j < n; j++)
            {
                float rc = Mathf.Lerp(0.16f, 0.11f, j / (float)Mathf.Max(1, n - 1));
                float hcell = 2f * 0.8660254f * rc;
                Vector2 jitter = new Vector2((Rand() - 0.5f) * 0.14f, (Rand() - 0.5f) * 0.14f);
                float yaw = (Rand() - 0.5f) * 0.9f;
                Vector2 face = Rotate(_sheet.ToCamPlan, yaw);
                var cell = new Cell
                {
                    Pos = Flat(p0 + jitter * (0.4f + j * 0.35f), stack + hcell * 0.5f), Dir = face, Rx = rc, Rv = rc, Lean = 0.1f + 0.1f * Rand(),
                    Hash = PickHash(Rand()), Pat = PatChoices[_rng.Next(PatChoices.Length)], Born = born0 + 0.12f * j, GrowLen = 0.2f, Rise = 0.22f,
                    LeaveAt = born0 + pileSeconds + 0.12f * (n - 1 - j), LeaveLen = 0.3f,
                };
                stack += hcell * 0.9f;
                _cells.Add(cell);
                if (j % 2 == 0) _ink.Spit(cell.Born, PlanW(new Vector3(p0.x, g + 0.05f, p0.y)), Vector3.up);
                if (j >= n - 2) _ink.End(cell.LeaveAt, PlanW(cell.Pos), Vector3.up, burstBody);
            }
            Extend(born0 + pileSeconds + 0.12f * n + 1f);
        }

        static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ---- 간

        /// <summary>간: another body reached by the spread. After the first landing three cells hop from the first body's hold cell toward it (0.1 s apart, 0.25 s each); the first two land at knee
        /// height on the way and stay as a road (3 s), the third lands on the neighbour's surface and the neighbour is held (2 s).</summary>
        void Neighbour(SpellFxHit hit, Vector2 feet)
        {
            if (_primaryFront == null) return;
            HexBody nb = HexHolds.BodyOf(_sheet, hit, feet);
            var plates = new List<HexPlate>();
            HexPlate nf = HexHolds.Add(_sheet, _rng, plates, nb, 0f, 3, 0f, _reach * 1.25f);
            if (nf == null) return;
            float t0 = Mathf.Max(_age, _tl + hitStopSeconds + 0.3f);
            Vector3 from = _primaryFront.Ctr, to = nf.Ctr;
            const int hops = 3;
            float tLand = t0;
            for (int k = 0; k < hops; k++)
            {
                float u = (k + 1f) / hops, h0 = t0 + hopInterval * k, h1 = h0 + hopSeconds;
                Vector3 land;
                if (k == hops - 1) land = to;
                else
                {
                    Vector2 xz = Vector2.Lerp(new Vector2(from.x, from.z), new Vector2(to.x, to.z), u);
                    land = new Vector3(xz.x, GroundSkip(xz) + 0.4f, xz.y);
                }
                var cell = new Cell
                {
                    From = from, Pos = land, Hop = true, HopT0 = h0, HopT1 = h1, Arc = k == hops - 1 ? 0.7f : 0.55f, Dir = _sheet.ToCamPlan, Rx = cellRadius, Rv = cellRadius,
                    Lean = tiltDeg * Mathf.Deg2Rad * 0.6f, Hash = PickHash(Rand()), Pat = PatChoices[_rng.Next(PatChoices.Length)], Born = h0, GrowLen = 0.06f,
                };
                if (k < hops - 1)
                {
                    cell.LeaveAt = h1 + roadSeconds; cell.LeaveLen = 0.25f;
                    _ink.Spit(h1, PlanW(new Vector3(land.x, land.y - 0.35f, land.z)), Vector3.up);
                }
                else cell.UsedAt = h1 + 0.06f;
                _cells.Add(cell);
                tLand = h1;
            }
            var group = NewGroup(hit.Target);
            for (int i = 0; i < plates.Count; i++)
            {
                var p = plates[i];
                p.Grow = 0.1f; p.Start = tLand - 0.01f + 0.045f * i; p.LeaveAt = tLand + holdSeconds + 0.02f * i;
                _plates.Add(p); group.Add(p);
                if (i == 0) _ink.End(p.LeaveAt, PlanW(p.Ctr), Vector3.up, burstBody);
            }
            float tt = nf.Start + 0.05f;
            _flash.Schedule(() => nf.Ctr, tt);
            _ink.Spit(tt, HexHolds.ContactPoint(_sheet, nf), HexHolds.ContactNormal(_sheet, nf));
            Extend(tLand + Mathf.Max(roadSeconds, holdSeconds) + 1f);
        }

        // ------------------------------------------------------------ mesh

        float LinkVis(int k, float a)
        {
            var l = _links[k];
            if (a >= l.GoneAt) return 0f;
            float s = HeadS(a) - k * chainSpacing;
            if (s < -chainSpacing) return 0f;
            if (!_hasHit && s >= _totalOut) return 0f;
            if (bite == HexPathBite.Cut && _hasHit && s >= _totalOut) return 0f;
            float v = 1f;
            if (s < 0f) v *= Smooth((s + chainSpacing) / chainSpacing);          // leaves the tip one cell at a time
            if (k == 0) v *= 0.8f + 0.2f * Smooth(s / 1.2f);                   // the lead starts at the brush-tip cell's size and fills out
            if (l.GoneAt < float.MaxValue && l.FadeLen > 0f) v *= 1f - Smooth((a - (l.GoneAt - l.FadeLen)) / l.FadeLen);
            return v;
        }

        /// <summary>A shot that found nobody ends where its reach ends: one small ink spit when the lead goes out (a hit that comes later still draws its look).</summary>
        void CheckLeadEnd(float a)
        {
            if (_leadEndDone || _hasHit || a < 0.1f) return;
            if (HeadS(a) < _totalOut - 0.05f) return;
            _leadEndDone = true;
            SpellFxHexKit.Spit(PlanW(PathAt(_totalOut)), Vector3.up);
        }

        void EmitLinks(float a)
        {
            float tilt = tiltDeg * Mathf.Deg2Rad;
            for (int k = 0; k < _links.Count; k++)
            {
                float v = LinkVis(k, a);
                if (v <= 0.02f) continue;
                float r = cellRadius * (k == 0 ? leadScale : 1f) * (1f - 0.05f * k) * v;
                Vector3 p = LinkPos(k, a);
                if (bite == HexPathBite.Cut)
                {
                    // blades: standing upright with the face turned 28 deg off the flight line toward the camera (an edge-on blade: tall and thin along the flight; TEST 0.85 / 1.2)
                    Vector2 an = _side; if (Vector2.Dot(an, _sheet.ToCamPlan) < 0f) an = -an;
                    float yaw = 28f * Mathf.Deg2Rad;
                    Vector2 fd = (an * Mathf.Cos(yaw) - _dir * Mathf.Sin(yaw)).normalized;
                    _sheet.EmitStanding(p, fd, r * 0.85f, r * 1.2f, 0f, _links[k].Hash, _links[k].Pat, 0.7f);
                }
                else _sheet.EmitStanding(p, _sheet.ToCamPlan, r, r, tilt, _links[k].Hash, _links[k].Pat, 0.7f);
            }
        }

        /// <summary>The thin ink tail of the lead: its last 0.3 s as a flat dark ribbon, wide at the lead and nothing at the old end. Dies as the lead stops (the old end catches up).</summary>
        void EmitTail(float a)
        {
            if (a > _tailUntil || _links.Count == 0) return;
            for (int i = 0; i <= TailN; i++) _tail[i] = LinkPos(0, Mathf.Max(0f, a - inkTailSeconds * (1f - i / (float)TailN)));
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            for (int i = 0; i < TailN; i++)
            {
                Vector3 d = _tail[i + 1] - _tail[i];
                Vector2 h = new Vector2(d.x, d.z);
                if (h.sqrMagnitude < 1e-6f) continue;
                h.Normalize();
                Vector3 perp = new Vector3(h.y, 0f, -h.x);
                float w0 = inkTailWidth * Smooth(i / (float)TailN), w1 = inkTailWidth * Smooth((i + 1) / (float)TailN);
                if (w1 < 0.004f) continue;
                for (int r = 0; r < 2; r++)
                    _sheet.Main.QuadVerts(_tail[i] - perp * (w0 * 0.5f), _tail[i] + perp * (w0 * 0.5f), _tail[i + 1] - perp * (w1 * 0.5f), _tail[i + 1] + perp * (w1 * 0.5f), sh, _reach * 1.25f, true);
            }
        }

        float CellVis(Cell c, float a)
        {
            float v = Smooth((a - c.Born) / Mathf.Max(c.GrowLen, 1e-3f));
            if (a > c.LeaveAt) v *= 1f - Smooth((a - c.LeaveAt) / c.LeaveLen);
            if (c.UsedAt >= 0f) v *= 1f - Smooth((a - c.UsedAt) / 0.12f);
            return v;
        }

        Vector3 CellPos(Cell c, float a)
        {
            Vector3 p = c.Pos;
            if (c.Hop && a < c.HopT1)
            {
                float u = Mathf.Clamp01((a - c.HopT0) / Mathf.Max(c.HopT1 - c.HopT0, 1e-3f));
                p = Vector3.Lerp(c.From, c.Pos, Smooth(u)) + Vector3.up * (c.Arc * Mathf.Sin(Mathf.PI * u));
            }
            else
            {
                if (c.Rise > 0f) p.y -= c.Rise * (1f - Smooth((a - c.Born) / Mathf.Max(c.GrowLen, 1e-3f)));
                if (c.Group != null) p += c.Group.Delta;
            }
            if (c.Seed) p.y += 0.03f * Mathf.Sin(a * Mathf.PI + c.Phase);        // +- 3 cm, 0.5 Hz
            return p;
        }

        void Rebuild(float a)
        {
            _sheet.Begin();
            CheckLeadEnd(a);
            EmitLinks(a);
            EmitTail(a);
            foreach (var c in _cells)
            {
                float v = CellVis(c, a);
                if (v <= 0.02f) continue;
                float k = c.Seed ? 1f + 0.08f * Mathf.Sin(a * Mathf.PI * 1.6f + c.Phase) : 1f;
                Vector3 p = CellPos(c, a);
                _sheet.EmitStanding(p, c.Dir, c.Rx * v * k, c.Rv * v * k, c.Lean, c.Hash, c.Pat, 0.7f);
                if (c.Seed) EmitThread(c, p, v, a);
            }
            foreach (var p in _plates)
            {
                float g = HexHolds.PlateG(p, a, 0.2f);
                if (g > 0.02f) _sheet.EmitPlate(p, g);
            }
            foreach (var f in _flights)
            {
                if (a < f.T0 || a >= f.T1) continue;
                if (!f.Sampled) _sheet.SampleFlight(f);
                _sheet.EmitFlight(f, a);
            }
            if (bite == HexPathBite.Once) EmitWash(a);
            if (bite == HexPathBite.Cut) EmitCrack(a);
            _sheet.Flush();
        }

        /// <summary>감: the thin vertical ink thread over the lodged seed, from its top corner up half a metre (plain dark glass, swaying a little).</summary>
        void EmitThread(Cell c, Vector3 p, float vis, float a)
        {
            Vector2 across = new Vector2(_sheet.ToCamPlan.y, -_sheet.ToCamPlan.x);
            float y0 = p.y + c.Rv * 0.8660254f * vis, y1 = y0 + 0.5f * vis;
            float hw0 = 0.014f * Mathf.Clamp01(vis * 2f), hw1 = 0.006f * Mathf.Clamp01(vis * 2f), sway = 0.012f * Mathf.Sin(a * 2.4f + c.Phase);
            Vector3 b0 = new Vector3(p.x + across.x * -hw0, y0, p.z + across.y * -hw0), b1 = new Vector3(p.x + across.x * hw0, y0, p.z + across.y * hw0);
            Vector3 t0 = new Vector3(p.x + across.x * (sway - hw1), y1, p.z + across.y * (sway - hw1)), t1 = new Vector3(p.x + across.x * (sway + hw1), y1, p.z + across.y * (sway + hw1));
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            Vector3 front = new Vector3(_sheet.ToCamPlan.x, 0.15f, _sheet.ToCamPlan.y);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, _reach * 1.25f, true, front);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, _reach * 1.25f, true, front);
        }

        /// <summary>가: the faint wash at the foot - one pass of plain dark glass (so it reads lighter than the crack's three), 0.6 m toward the caster, widening from 6 to 12 cm.</summary>
        void EmitWash(float a)
        {
            if (_washT0 < 0f) return;
            float grow = Smooth((a - _washT0) / 0.25f), fade = 1f - Smooth((a - (_washT0 + 1.4f)) / 0.4f);
            if (grow < 0.02f || fade < 0.02f) return;
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            Vector2 perp = new Vector2(_washDir.y, -_washDir.x);
            const int N = 6;
            for (int i = 0; i < N; i++)
            {
                float t0 = i / (float)N, t1 = (i + 1) / (float)N;
                Vector2 q0 = _washFrom + _washDir * (0.6f * grow * t0), q1 = _washFrom + _washDir * (0.6f * grow * t1);
                float w0 = (0.03f + 0.03f * t0) * fade, w1 = (0.03f + 0.03f * t1) * fade;
                _sheet.Main.QuadVerts(Flat(q0 - perp * w0, _washY), Flat(q0 + perp * w0, _washY), Flat(q1 - perp * w1, _washY), Flat(q1 + perp * w1, _washY), sh, _reach * 1.25f, true);
            }
        }

        /// <summary>갓: the dark crack - 4 cm, three passes, from 0.9 m ahead of the caster along the aim to 1 m past the body; it is drawn as far as the lead has gone and fades out after 3 s.</summary>
        void EmitCrack(float a)
        {
            if (!_crackOn || _crack.Count < 2) return;
            float tl = _tl;
            float fade = 1f - Smooth((a - (tl + crackSeconds)) / 0.5f);
            if (fade < 0.02f) return;
            float reach01 = Mathf.Clamp01(HeadS(a) / Mathf.Max(0.5f, _totalOut));          // how far the lead has gone along its whole way
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            int n = _crack.Count - 1;
            float w = crackWidth * fade;
            for (int i = 0; i < n; i++)
            {
                float t1 = (i + 1f) / n;
                if ((i / (float)n) > reach01) break;
                Vector3 p0 = _crack[i], p1 = _crack[i + 1];
                Vector2 d = new Vector2(p1.x - p0.x, p1.z - p0.z);
                if (d.sqrMagnitude < 1e-8f) continue;
                d.Normalize();
                Vector3 perp = new Vector3(d.y, 0f, -d.x) * (w * 0.5f);
                float cut = Mathf.Clamp01((reach01 - i / (float)n) / (t1 - i / (float)n));
                p1 = Vector3.Lerp(p0, p1, cut);
                for (int r = 0; r < 3; r++) _sheet.Main.QuadVerts(p0 - perp, p0 + perp, p1 - perp, p1 + perp, sh, _reach * 1.25f, true);
            }
        }
    }
}
