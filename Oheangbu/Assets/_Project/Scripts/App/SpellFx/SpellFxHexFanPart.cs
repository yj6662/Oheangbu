using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅗ 면 trial (the user: "'ㅜ' 술식 계열처럼 정돈된 형태로" -> "ㅗ 면 6자로 먼저 시험"): the fan in front drawn in the ㅜ ward's grammar -
    /// a translucent hexagon-cell membrane (shader Oheangbu/SpellFx/HexDome), cream seams, a KCISA pattern in the cells - lying on the ground as a wedge. Motion (frame and verb) is the
    /// vine fan's; only the material changed. The cells are built at runtime and rebuilt into ONE dynamic mesh every tick.
    /// Shader contract (read from SpellFxHexDome.shader, unchanged): uv0 = pattern uv in the cell, uv1 = (cell hash, 0 centre .. 1 border), uv2 = (pattern 0..7, height 0..1),
    /// uv3 = the cell's centre in object space. The shader's reveal reads the VERTEX object-space y (not uv2.y), so the object is laid on its side: object y = metres
    /// ahead of the caster (scaled so 0.85 object units = the whole reach), object z = down. Look only - nothing here touches a rule.
    /// Round 2 (Fable r1 f_*_r1: 63 - 76, below the vine look), the four gates:
    ///  1 material = the dome's: a dark translucent fill, a doubled cream seam with a jade bleed inside it, a pattern in more cells - the shader blooms a pattern only
    ///    sporadically (~0.4 s of a 0.9 - 1.34 s period per cell), so every cell is drawn as TWO layers whose hashes keep the same bloom period half a period apart
    ///    (hash B = hash A + 1/13.7) and the cells' hashes are picked for short periods: about 65 % of the cells carry a pattern at any moment, not 100 % (a cap of the shader); the fan's outline is a 6 cm cream ribbon in the same mesh (the old 1 px HexGround strip is gone);
    ///  2 the hold is cells ON the body (3 - 5 plates round the shoulders, a bridge cell up from the feet, none over the face), a flash and one ink spit where it touches;
    ///  3 ink: a first cell or two thrown from the brush tip, the hit's ink spit, cells that break into ink far side first at the end, a 2 Hz breath while it stands;
    ///  4 the return (공): the sheet withdraws far cells first toward the caster over 0.8 s (the cells slide home), a second flash and spit as the front passes the body.
    /// Round 3 (Fable f_*_r2: 80 - 85; the hold sat over the bear's head, the brush start was not seen, the outline was a lime line of its own, 공 shrank in place, 곤 had no path):
    ///  a the hold cells sit ON the target's real surface (its colliders' bounds, a ray per cell, 3 cm out), at 40 - 75 % of its height, and never over the camera-facing face; the neck-height
    ///    ring of r2 (the bridge cell + five cells at one height) is gone, and the shader's hit age is switched off once the flash has died;
    ///  b the throw starts at ONE point - the brush tip sampled at the throw's first frame (else caster + 0.32 right + 1.25 up + 0.6 ahead) - with an ink spit there; two cells (x1.3) fly
    ///    in 0.12 s and lie down on the first row; the old landing spit under the cloak is gone;
    ///  c the outline is a cream ribbon that is the sweep's front line: it grows with the cells' sweep and leaves with their leaving (far side first); 곰 wipes it 0.3 s after the last seed lands;
    ///  d 공: every cell glides to ONE cell 1.2 m ahead of the caster (ease-in, shrinking), the cell there grows as they arrive and flashes + spits when the last one lands;
    ///  e 곤: the spread is a path of cells lit one by one (0.1 s apart) from the first body's cell (or the sheet's edge, for a body outside) to the neighbour; the lit cells stay.
    /// Round 4 (the kit; 2026-10-30, the ㅗ 30 glyphs' stills): the pieces the ring part shares moved to <see cref="SpellFxHexKit"/> (frame, mesh builder, ground, hold, tip, ink and flash queues,
    /// clock) with the look and the times unchanged, and two defects fixed:
    ///  i  the ink spits of the brush, a throw, a landing, a contact and a breaking line are always the SMALL plain ink (<see cref="SpellFxHexKit.Spit"/>) - only the end (cells breaking up, far
    ///    side first) bursts as the body (wood: none). The seed glyphs (곰 놈 몸 솜 옴) threw a body burst at the brush tip for every seed and covered the screen;
    ///  ii the brush-thrown cell was an opaque cream hexagon (the shader's BASE RING term exp(-objectY / 0.03) is 1 where the object-space y is ~0, and at the brush tip - beside / behind the caster -
    ///    the lying sheet's object y is metres ahead / sy ~ 0): the thrown cells now live on the kit's AIR layer (an upright object 1 m below the caster, own material and clock), and a seed's
    ///    cell no longer waits in the hand (0.11 s, ease-out, flat by mid-flight).
    /// 한 번 (Sweep) also shows its ink-gathering cell at the brush tip while the great brush swings toward the apex (<see cref="ISpellFxPreLaunch"/>, D308-158 1): the cell grows there and the
    /// motes are drawn into it, and at the launch it is the first thrown cell.</summary>
    public sealed class SpellFxHexFanPart : MonoBehaviour, ISpellFxPart, ISpellFxPreLaunch
    {
        /// <summary>Same names as the vine fan's verbs: 고 Sweep · 곡 Thicket · 곤 FanSpread · 곰 FanSeed · 곳 FanCut · 공 FanReturn.</summary>
        public enum HexFanBite { Sweep, Thicket, FanSpread, FanSeed, FanCut, FanReturn }

        [SerializeField] Material material;
        [SerializeField] HexFanBite bite = HexFanBite.Sweep;
        [Tooltip("What the sheet's END burst is made of (cells breaking up far side first; Wood = none). The brush / throw / contact spits are always the small plain ink.")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Water;
        [Tooltip("Cell circumradius in metres (0.275 = a 0.55 m cell).")]
        [SerializeField] float cellRadius = 0.275f;
        [SerializeField] float startDistance = 1.3f;
        [Tooltip("Used when the cast carries no spec.")]
        [SerializeField] float defaultDepth = 5f;
        [SerializeField] float minHalfAngle = 0.78f;
        [Tooltip("Height of the sheet above the ground (2 - 4 cm).")]
        [SerializeField] float lift = 0.03f;
        [Tooltip("The sheet breathes up by at most this much (a slow wave running out from the caster).")]
        [SerializeField] float breathe = 0.06f;
        [Tooltip("The sweep from the first row to the last (s).")]
        [SerializeField] float revealSeconds = 0.35f;
        [Tooltip("The first cells fly from the brush tip to the fan's start; the sweep begins when they land (s).")]
        [SerializeField] float leadSeconds = 0.12f;
        [Tooltip("공: the point (metres ahead of the caster) every cell glides to when the sheet is called home.")]
        [SerializeField] float homeDistance = 1.2f;
        [Tooltip("The outline ribbon's width in metres.")]
        [SerializeField] float ribbonWidth = 0.06f;
        [Tooltip("Sample the ground under every cell (a ray, once, when the cell is made).")]
        [SerializeField] bool followGround = true;

        const float Sqrt3 = SpellFxHexKit.Sqrt3;
        static readonly int[] PatChoices = SpellFxHexKit.PatChoices;

        static float Smooth(float x) => SpellFxHexKit.Smooth(x);
        static long Key(int i, int j) => SpellFxHexKit.Key(i, j);

        // ------------------------------------------------------------ state

        sealed class Cell
        {
            public int I, J;
            public Vector2 Pos;                 // plan: x right, y (= metres) forward
            public Vector2 Shift;               // 곳: the halves open
            public Vector2 Slide;               // 공: the cell slides home
            public float Hash, Phase, Ground;
            public int Pat;
            public float Born = -1f;            // pop-in start
            public bool Patch, Seed, Axis;
            public float OpenAt = -1f;          // 곳: when this row opens / the axis cell breaks
            public float LeaveAt = float.MaxValue;
            public float LeaveLen = -1f;        // < 0: the part's _leaveLen
            public bool Home;                   // 공: the one cell the others glide into
            public float Size = 1f;             // x the cell radius (the home cell grows as the others arrive)
            public float Dy;                    // 공: ground height difference while it glides
            public float MarkAt = -1f;          // 곤: lit as a step of the path at this time (a second layer over the cell, kept)
            public float MarkHash; public int MarkPat;
        }

        readonly List<Cell> _cells = new List<Cell>();
        readonly Dictionary<long, Cell> _grid = new Dictionary<long, Cell>();
        readonly List<HexPlate> _plates = new List<HexPlate>();
        readonly List<HexFlight> _flights = new List<HexFlight>();
        readonly HexFlashQueue _flash = new HexFlashQueue();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly HexTipPreview _preview = new HexTipPreview();

        HexSheet _sheet;
        System.Func<Vector2, float> _ribbonHeight;
        System.Random _rng;
        bool _built, _launched, _ended;
        float _age;
        Vector3 _origin, _fwd, _right;
        float _depth, _reach, _half, _sy, _fadeStart, _spread = 0.5f, _leaveLen = 0.2f, _endAt, _lead, _airBias;
        Vector2? _primaryFeet;                      // 곤: where the first body stands (the path to a neighbour inside the sheet starts there)
        float _seedLast = 1f;                       // 곰: when the last seed is planted
        Cell _home; float _homeGround;              // 공

        // ------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast) { _rng = new System.Random((int)(cast.CastId & 0x7fffffff)); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            _preview.Stop();
            if (_sheet != null) _sheet.SetActive(true);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;
            _ended = true;
            _preview.Stop();
            if (_sheet != null) _sheet.SetActive(false);
        }

        /// <summary>한 번 only: from the commit to the apex the brush swings; the ink gathers in a small cell at its tip (drawn on the air layer, which has its own clock - the sheet's _Age stays unset
        /// until the launch), and at the launch that cell is the first flight's (same size, place, hash).</summary>
        public void OnPreLaunch(SpellFxCast cast, float secondsToLaunch)
        {
            if (bite != HexFanBite.Sweep || _ended || _launched) return;
            Build(cast);
            if (!_built || _flights.Count == 0) return;
            float delay = cast.LaunchDelaySeconds, since = Mathf.Max(0f, delay - secondsToLaunch);
            var f = _flights[0];
            _sheet.Air.Go.SetActive(true);
            _sheet.Air.SetClock(since, 0f);
            _sheet.Air.Breathe(since);
            _sheet.Air.Begin();
            _preview.Show(_sheet, transform, since, delay, f.Rc * 0.8f, f.Hash);
            _sheet.Air.Flush();
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (!_built || _ended || bite == HexFanBite.FanSeed) return;      // seeds wait: nothing answers a body
            long key = hit.TargetId != 0 ? hit.TargetId : 1000000L + _seen.Count;
            if (!_seen.Add(key)) return;                                       // each body once
            Vector3 rel = hit.Feet - _origin;
            float fx = Vector3.Dot(rel, _right), fz = Mathf.Max(0f, Vector3.Dot(rel, _fwd));
            float d = Mathf.Sqrt(fx * fx + fz * fz), ang = Mathf.Atan2(fx, fz);
            bool inside = d <= _reach + 0.4f && Mathf.Abs(ang) <= _half + 0.2f;
            bool spread = bite == HexFanBite.FanSpread && (!inside || (hit.Kind != null && hit.Kind.Contains("Spread")));
            float arrive = Mathf.Max(_age, _lead + fz / _depth * revealSeconds + 0.1f);        // when the sweep reaches the body
            var feet = new Vector2(fx, fz);
            // F5 still: the hold cells sat over the bear's head like a crown - hit.Feet is not the ground (the vine's old pitfall) and twice the centre's height
            // overshot a four-legged body; the ground under the feet, and the body's height as 1.3 x its centre above that ground. Round 3: the hold sits on the body's real surface (its colliders).
            HexBody hold = HexHolds.BodyOf(_sheet, hit, feet);
            if (spread) { SpawnPatch(feet, arrive, hold); return; }
            if (!inside) return;
            if (_primaryFeet == null) _primaryFeet = feet;
            float dist = feet.magnitude;
            HexPlate front;
            if (bite == HexFanBite.FanCut)
            {
                // the cut runs row by row; the cells settle on the body once the opening has passed it
                arrive = Mathf.Max(arrive, 0.75f + 0.1f * Mathf.Max(0f, fz - startDistance) / (Sqrt3 * cellRadius) + 0.1f);
                front = AddHold(hold, arrive, 2, LeaveOf(dist) + 0.1f);
            }
            else
            {
                Cell cell = Nearest(feet, false);
                if (cell != null) Schedule(cell, null, arrive);                                   // the ground cell at the feet answers first
                float tCross = LeaveOf(dist);
                front = AddHold(hold, arrive, bite == HexFanBite.Thicket ? 4 : 5, bite == HexFanBite.FanReturn ? tCross + 0.25f : tCross + 0.1f);
                if (bite == HexFanBite.FanReturn && front != null)
                {
                    // the second flash and spit: as the withdrawing front (far side first) passes the body
                    Schedule(null, front, tCross - 0.02f);
                    _ink.Spit(tCross, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
                    if (cell != null) Schedule(cell, null, tCross + 0.1f);
                }
            }
            if (front != null)
            {
                float tt = front.Start + 0.05f;
                Schedule(null, front, tt);
                _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            }
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _ended || _sheet == null) return;
            float a = ageSeconds;
            _sheet.Main.SetClock(a, 0f);
            _sheet.Air.SetClock(a + _airBias, 0f);          // the air layer's clock runs on from the commit (a 한 번 preview was drawn on it before the launch)
            if (a > _endAt) { if (_sheet.Main.Go.activeSelf) _sheet.SetActive(false); return; }
            _ink.Drain(a);
            _flash.Drive(_sheet, a);
            Animate(a);
            // a breath while it stands: 2 Hz, +- 10 % on the seam, the pattern and the fill
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

        /// <summary>A hash whose bloom phase is near <paramref name="phase"/> and whose period is short (see <see cref="SpellFxHexKit.PickHash"/>).</summary>
        float PickHash(float phase) => SpellFxHexKit.PickHash(_rng, phase);

        /// <summary>When a cell at <paramref name="dist"/> metres starts to leave: the far side first, over the spread.</summary>
        float LeaveOf(float dist) => _fadeStart + (1f - Mathf.Clamp01(dist / Mathf.Max(_reach, 0.1f))) * _spread;

        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;

        float GroundAt(Vector2 p) => _sheet.Ground.At(p);

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            _depth = cast.Spec != null && cast.Spec.FanDepth > 0.5f ? Mathf.Min(9f, cast.Spec.FanDepth) : defaultDepth;
            _half = Mathf.Clamp(Mathf.Max(minHalfAngle, cast.Spec != null ? cast.Spec.FanHalfAngleRad : 0f), 0.3f, 1.2f);
            _reach = bite == HexFanBite.Thicket ? _depth * 1.5f : _depth;
            _sy = _reach / 0.85f;
            _lead = leadSeconds;
            _airBias = cast.LaunchDelaySeconds;
            _spread = 0.5f; _leaveLen = 0.2f;
            switch (bite)
            {
                case HexFanBite.Thicket: _fadeStart = 3.5f; break;
                case HexFanBite.FanSpread: _fadeStart = 3.0f; break;
                case HexFanBite.FanSeed: _fadeStart = 8.0f; break;
                case HexFanBite.FanCut: _fadeStart = 2.3f; break;
                // 0.5 s after the sweep the cells set off, far side first: they leave over 0.4 s and each glides 0.4 s to the home cell, so the whole call-home takes 0.8 s
                case HexFanBite.FanReturn: _fadeStart = _lead + revealSeconds + 0.18f + 0.5f; _spread = 0.4f; _leaveLen = 0.4f; break;
                default: _fadeStart = 2.35f; break;
            }
            _endAt = _fadeStart + _spread + 0.25f + _leaveLen + 0.2f;
            if (bite == HexFanBite.FanReturn) _endAt += 0.5f;      // the home cell outlives the last arrival

            // the object lies on its side (see the class comment): local x = right, local y = ahead, local z = down; the brush tip is sampled in the sheet's constructor
            _sheet = new HexSheet(transform, cast, material, HexFrame.Lying(_origin, _right, _fwd, _sy), 0.06f, followGround, "HexFan") { Breath = breathe, Lift = lift };
            _ribbonHeight = p => _sheet.Ground.Q(p) + lift + 0.012f;

            BuildCells();
            _built = true;
        }

        Vector2 CenterOf(int i, int j) => SpellFxHexKit.CenterOf(i, j, cellRadius);

        void SnapTo(Vector2 p, out int bi, out int bj) => SpellFxHexKit.SnapTo(p, cellRadius, out bi, out bj);

        Cell NewCell(int i, int j)
        {
            var c = new Cell { I = i, J = j, Pos = CenterOf(i, j), Phase = Rand() * 6.28f, Pat = PatChoices[_rng.Next(PatChoices.Length)] };
            c.Hash = PickHash(Rand());
            c.LeaveAt = LeaveOf(c.Pos.magnitude) + Rand() * 0.08f;
            _cells.Add(c); _grid[Key(i, j)] = c;
            return c;
        }

        void BuildCells()
        {
            float dx = 1.5f * cellRadius, dz = Sqrt3 * cellRadius;
            int nI = Mathf.CeilToInt(_reach * Mathf.Sin(_half) / dx) + 2, nJ = Mathf.CeilToInt(_reach / dz) + 2;
            for (int i = -nI; i <= nI; i++)
                for (int j = 0; j <= nJ; j++)
                {
                    Vector2 p = CenterOf(i, j);
                    float r = p.magnitude;
                    if (r < startDistance || r > _reach || Mathf.Abs(Mathf.Atan2(p.x, p.y)) > _half) continue;       // the centre must lie inside the wedge
                    var c = NewCell(i, j);
                    if (bite == HexFanBite.Thicket && r > _depth) c.Born = 0.4f + ((r / _depth - 1f) / 0.5f) * 2.6f;   // the wedge widens to x1.5 over the life
                    if (bite == HexFanBite.FanCut)
                    {
                        c.Axis = i == 0;
                        c.OpenAt = 0.75f + 0.1f * Mathf.Max(0f, p.y - startDistance) / dz;      // row by row, one every 6 frames: the cut RUNS from the near end to the far end
                    }
                }
            if (bite == HexFanBite.FanSeed) PickSeeds();
            foreach (var c in _cells) c.Ground = GroundAt(c.Pos);
            // the sweep: near row first, the far row at revealSeconds after the cells' first landing
            foreach (var c in _cells)
                if (c.Born < 0f) c.Born = _lead + revealSeconds * Mathf.Clamp01((c.Pos.magnitude - startDistance) / Mathf.Max(0.5f, _depth - startDistance));
            if (bite == HexFanBite.Thicket) PickSteles();
            if (bite == HexFanBite.FanCut) QueueAxisInk();
            PickEndInk();
            MakeFlights();
            if (bite == HexFanBite.FanReturn) MakeHome();
        }

        /// <summary>곰: 10 - 14 scattered cells only, planted one after another outward.</summary>
        void PickSeeds()
        {
            var all = new List<Cell>(_cells);
            _cells.Clear(); _grid.Clear();
            int want = 10 + _rng.Next(5);
            for (int tries = 0; tries < 600 && _cells.Count < want; tries++)
            {
                var c = all[_rng.Next(all.Count)];
                bool ok = true;
                foreach (var o in _cells) if ((o.Pos - c.Pos).sqrMagnitude < 1.1f * 1.1f) { ok = false; break; }
                if (!ok) continue;
                c.Seed = true; _cells.Add(c); _grid[Key(c.I, c.J)] = c;
            }
            _cells.Sort((p, q) => p.Pos.y.CompareTo(q.Pos.y));
            for (int k = 0; k < _cells.Count; k++) _cells[k].Born = 0.3f + 0.17f * k;
            _seedLast = _cells.Count > 0 ? _cells[_cells.Count - 1].Born : 0.6f;
        }

        /// <summary>곡: ten big plates (a cell x 2 wide) stand up one after another at three heights (waist, chest, head), near ones first, spread over the wedge.</summary>
        void PickSteles()
        {
            var pool = new List<Cell>();
            foreach (var c in _cells) if (c.Pos.magnitude >= 2.4f) pool.Add(c);
            for (int i = pool.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp; }
            var picked = new List<Cell>();
            foreach (var c in pool)
            {
                if (picked.Count >= 10) break;
                bool ok = true;
                foreach (var o in picked) if ((o.Pos - c.Pos).sqrMagnitude < 1.4f * 1.4f) { ok = false; break; }
                if (ok) picked.Add(c);
            }
            picked.Sort((p, q) => p.Pos.y.CompareTo(q.Pos.y));
            float step = 1.8f / Mathf.Max(1, picked.Count);
            float[] tiers = { 1.0f, 1.4f, 1.8f };
            for (int k = 0; k < picked.Count; k++)
            {
                var c = picked[k];
                float h = tiers[k % 3], dist = c.Pos.magnitude;
                Vector2 toCaster = dist > 1e-3f ? -c.Pos / dist : -Vector2.up;
                _plates.Add(new HexPlate
                {
                    Ctr = new Vector3(c.Pos.x, c.Ground + lift, c.Pos.y), Dir = toCaster, Rx = 2f * cellRadius, Rv = h / (2f * 0.8660254f),
                    Start = Mathf.Max(0.6f + k * step, c.Born + 0.2f), Grow = 0.45f, LeaveAt = LeaveOf(dist) + Rand() * 0.08f,
                    Hash = PickHash(Rand()), D01 = Mathf.Clamp(dist / _reach, 0.3f, 1f), Pat = PatChoices[(c.Pat + k) % PatChoices.Length], Sprout = true,
                });
            }
        }

        /// <summary>The end: about eight cells (far side first) burst as the body as they break up (wood: none).</summary>
        void PickEndInk()
        {
            if (burstBody == SpellFxBurstBody.Wood || _cells.Count == 0) return;
            var order = new List<Cell>(_cells);
            order.Sort((p, q) => q.Pos.sqrMagnitude.CompareTo(p.Pos.sqrMagnitude));
            int n = Mathf.Min(bite == HexFanBite.FanReturn ? 3 : 8, order.Count);
            for (int k = 0; k < n; k++)
            {
                var c = order[Mathf.Min(order.Count - 1, (int)((k + 0.5f) * order.Count / n))];
                _ink.End(c.LeaveAt, PlanW(new Vector3(c.Pos.x, c.Ground + lift + 0.05f, c.Pos.y)), Vector3.up, burstBody);
            }
        }

        /// <summary>곳: every other cell of the axis column spits ink as the cut breaks it open (at most four).</summary>
        void QueueAxisInk()
        {
            int made = 0;
            foreach (var c in _cells)
            {
                if (!c.Axis || made >= 4) continue;
                int row = Mathf.RoundToInt(Mathf.Max(0f, c.Pos.y - startDistance) / (Sqrt3 * cellRadius));
                if (row % 2 != 1) continue;
                made++;
                _ink.Spit(c.OpenAt, PlanW(new Vector3(c.Pos.x, c.Ground + lift + 0.1f, c.Pos.y)), (Vector3.up + _right * (made % 2 == 0 ? 0.7f : -0.7f)).normalized);
            }
        }

        /// <summary>Gate 3a (round 3): the throw starts at ONE point, the brush tip, sampled at each throw's first frame (<see cref="HexSheet.SampleFlight"/>) with a small ink spit there. Two cells (x1.3 the
        /// r2 size) fly to the two first-row cells nearest the axis in 0.12 s and lie down on them (the real cells pop in as they land); the sweep begins then. For 곰 every seed is thrown from the
        /// same point to its place, planted nearest first (a spit at the tip for every other throw, one where every third lands); round 4: a seed's cell is in the air for 0.11 s and does not
        /// wait in the hand (<see cref="HexFlight.Quick"/>). There is no landing spit under the cloak any more.</summary>
        void MakeFlights()
        {
            if (bite == HexFanBite.FanSeed)
            {
                int k = 0;
                foreach (var c in _cells)
                {
                    var to = new Vector3(c.Pos.x, c.Ground + lift, c.Pos.y);
                    _flights.Add(new HexFlight { From = _sheet.TipPlan, To = to, T0 = c.Born - 0.11f, T1 = c.Born, Rc = cellRadius * 0.91f, Arc = 0.5f + 0.06f * c.Pos.magnitude, Hash = c.Hash, TipInk = k % 2 == 0, Quick = true });
                    if (k % 3 == 0) _ink.Spit(c.Born, PlanW(to + Vector3.up * 0.04f), Vector3.up);
                    k++;
                }
                return;
            }
            if (_cells.Count == 0) return;
            var near = new List<Cell>(_cells);
            near.Sort((p, q) => (p.Pos.magnitude + 0.35f * Mathf.Abs(p.Pos.x)).CompareTo(q.Pos.magnitude + 0.35f * Mathf.Abs(q.Pos.x)));
            Cell c0 = near[0], c1 = null;
            foreach (var c in near)
                if (c != c0 && Mathf.Abs(c.Pos.x - c0.Pos.x) > 0.4f && c.Pos.magnitude < startDistance + 0.9f) { c1 = c; break; }
            if (c1 == null) c1 = near.Count > 1 ? near[1] : null;
            var pair = new[] { c0, c1 };
            for (int k = 0; k < 2; k++)
            {
                var c = pair[k];
                if (c == null) continue;
                float t0 = 0.03f * k, t1 = _lead + 0.03f * k;
                _flights.Add(new HexFlight { From = _sheet.TipPlan, To = new Vector3(c.Pos.x, c.Ground + lift, c.Pos.y), T0 = t0, T1 = t1, Rc = cellRadius * 0.78f, Arc = 0.4f, Hash = c.Hash, TipInk = k == 0 });
                c.Born = Mathf.Max(0f, t1 - 0.05f);          // the real cell grows under the landing one
            }
        }

        /// <summary>공: the one cell 1.2 m ahead of the caster that the sheet is called into. It appears as the first cell lands, grows as the others arrive, and flashes + spits when the last one lands.</summary>
        void MakeHome()
        {
            if (_cells.Count == 0) return;
            float first = float.MaxValue, last = 0f;
            foreach (var c in _cells) { float ar = c.LeaveAt + _leaveLen; first = Mathf.Min(first, ar); last = Mathf.Max(last, ar); }
            Vector2 hp = new Vector2(0f, homeDistance);
            _homeGround = GroundAt(hp);
            _home = new Cell { Home = true, Pos = hp, Ground = _homeGround, Pat = PatChoices[_rng.Next(PatChoices.Length)], Hash = PickHash(Rand()), Born = first - 0.12f, LeaveAt = last + 0.3f, LeaveLen = 0.25f };
            _cells.Add(_home);
            Schedule(_home, null, last);
            Vector3 pw = PlanW(new Vector3(hp.x, _homeGround + lift + 0.06f, hp.y));
            _ink.Spit(first, pw, Vector3.up);
            _ink.Spit(last, pw, Vector3.up);
        }

        // ------------------------------------------------------------ hits

        Cell Nearest(Vector2 p, bool skipAxis)
        {
            Cell best = null; float bd = float.MaxValue;
            foreach (var c in _cells)
            {
                if (c.Patch || c.Home || (skipAxis && c.Axis)) continue;
                float d = (c.Pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        Vector2 CellPlan(Cell c) => c.Pos + c.Shift + c.Slide;

        /// <summary>The plan point a cell's flash is drawn at (the ctrPlan the cell's mesh gives the shader), asked every frame the flash lives.</summary>
        Vector3 CellPlanV(Cell c) { Vector2 p = CellPlan(c); return new Vector3(p.x, c.Ground + c.Dy + lift, p.y); }

        /// <summary>One latest-hit slot in the shader: the queue keeps the flashes 0.1 s apart so each one reads.</summary>
        void Schedule(Cell cell, HexPlate plate, float at)
        {
            if (cell != null) _flash.Schedule(() => CellPlanV(cell), at);
            else if (plate != null) _flash.Schedule(() => plate.Ctr, at);
        }

        HexPlate AddHold(HexBody h, float t0, int n, float leaveAt) => HexHolds.Add(_sheet, _rng, _plates, h, t0, n, leaveAt, _reach);

        /// <summary>곤 (round 3): the spread is a PATH of cells lit one by one, 0.1 s (6 frames) apart, from the first body's cell (a neighbour inside the sheet) or the sheet's nearest edge cell
        /// (a body beyond it) to the neighbour's feet - at most ten. A cell the sheet already has gets a second, denser layer from its moment (and keeps it); a cell it lacks is made (a patch).
        /// Then the neighbour is held. r2 only made patches beyond the edge, so a neighbour inside the wedge got a single flash.</summary>
        void SpawnPatch(Vector2 feet, float arrive, HexBody hold)
        {
            Cell start = Nearest(feet, false);
            if (start == null) return;
            if (_primaryFeet.HasValue && (start.Pos - feet).magnitude < 0.6f && (_primaryFeet.Value - feet).magnitude > 0.9f)
            {
                Cell pc = Nearest(_primaryFeet.Value, false);
                if (pc != null) start = pc;
            }
            Schedule(start, null, arrive);                                  // the first cell lights: the crawl begins there
            float step = Sqrt3 * cellRadius;
            Vector2 dir = feet - start.Pos; float len = dir.magnitude;
            Vector2 dn = len > 1e-3f ? dir / len : Vector2.up;
            int want = Mathf.CeilToInt(len / step), steps = Mathf.Clamp(want, 1, 10);
            Vector2 from = steps == want ? start.Pos : feet - dn * (step * steps);        // a longer path keeps its last ten cells
            const float iv = 0.1f;
            float t = arrive;
            long lastKey = Key(start.I, start.J);
            for (int s = 1; s <= steps; s++)
            {
                Vector2 q = s == steps ? feet : from + dn * (step * s);
                SnapTo(q, out int i, out int j);
                long key = Key(i, j);
                if (key == lastKey) continue;
                lastKey = key;
                t += iv;
                if (_grid.TryGetValue(key, out Cell have))
                {
                    if (have.MarkAt < 0f)
                    {
                        have.MarkAt = t; have.MarkHash = PickHash(Rand());
                        have.MarkPat = PatChoices[(System.Array.IndexOf(PatChoices, have.Pat) + 2) % PatChoices.Length];
                    }
                    Schedule(have, null, t);
                }
                else MakePatch(CenterOf(i, j), t);
            }
            HexPlate front = AddHold(hold, t, 5, LeaveOf(feet.magnitude) + 0.1f);
            if (front != null)
            {
                float tt = front.Start + 0.05f;
                Schedule(null, front, tt);
                _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            }
        }

        bool MakePatch(Vector2 q, float born)
        {
            SnapTo(q, out int i, out int j);
            if (_grid.ContainsKey(Key(i, j))) return false;
            Vector2 c0 = CenterOf(i, j);
            if (c0.y < 0.2f) return false;
            var c = NewCell(i, j);
            c.Patch = true; c.Born = born; c.Ground = GroundAt(c.Pos);
            c.LeaveAt = LeaveOf(c.Pos.magnitude) + 0.05f;
            return true;
        }

        // ------------------------------------------------------------ animation

        void Animate(float a)
        {
            int arrived = 0;
            Vector2 hp = new Vector2(0f, homeDistance);
            foreach (var c in _cells)
            {
                if (bite == HexFanBite.FanCut && !c.Axis) c.Shift.x = Mathf.Sign(c.I) * 0.15f * Smooth((a - c.OpenAt) / 0.25f);       // the halves open 0.15 m each way
                if (bite == HexFanBite.FanReturn && !c.Home && a > c.LeaveAt)
                {
                    // the return (round 3): the cell really travels to the home cell 1.2 m ahead of the caster - ease-in, so it starts slowly and snaps in (far cells set off first)
                    float u = Mathf.Clamp01((a - c.LeaveAt) / _leaveLen), e = u * u;
                    c.Slide = (hp - c.Pos) * e;
                    c.Dy = (_homeGround - c.Ground) * e;
                    if (u >= 1f) arrived++;
                }
            }
            if (_home != null) _home.Size = 1f + 0.8f * arrived / Mathf.Max(1, _cells.Count - 1);
        }

        float VisOf(Cell c, float a)
        {
            float v = c.Born >= 0f ? Smooth((a - c.Born) / 0.18f) : 1f;
            float ll = c.LeaveLen > 0f ? c.LeaveLen : _leaveLen;
            if (a > c.LeaveAt)
            {
                if (bite == HexFanBite.FanReturn && !c.Home)
                {
                    // it shrinks while it glides and is gone the moment it arrives (the home cell takes it)
                    v *= 1f - 0.6f * Mathf.Clamp01((a - c.LeaveAt) / ll);
                    v *= 1f - Smooth((a - c.LeaveAt - ll) / 0.06f);
                }
                else v *= 1f - Smooth((a - c.LeaveAt) / ll);
            }
            if (c.Seed) v *= 1f + 0.08f * Mathf.Sin(a * Mathf.PI * 2f * 2f + c.Phase);
            if (c.Axis && c.OpenAt >= 0f) v *= 1f - Smooth((a - c.OpenAt) / 0.12f);
            return v;
        }

        // ------------------------------------------------------------ mesh

        void Rebuild(float a)
        {
            _sheet.Begin();
            foreach (var c in _cells)
            {
                float vis = VisOf(c, a);
                if (vis <= 0.02f) continue;
                EmitFloor(c, vis, false);
                if (c.MarkAt >= 0f && a > c.MarkAt) EmitFloor(c, vis * Mathf.Max(0.02f, Smooth((a - c.MarkAt) / 0.12f)), true);
            }
            foreach (var p in _plates)
            {
                float g = HexHolds.PlateG(p, a, _leaveLen);
                if (g > 0.02f) _sheet.EmitPlate(p, g);
            }
            foreach (var f in _flights)
            {
                if (a < f.T0 || a >= f.T1) continue;
                if (!f.Sampled) _sheet.SampleFlight(f);
                _sheet.EmitFlight(f, a);
            }
            EmitRibbon(a);
            _sheet.Flush();
        }

        /// <summary><paramref name="mark"/>: the 곤 path's second layer over the cell (own hash and pattern, a hair above it, a hair smaller).</summary>
        void EmitFloor(Cell c, float s, bool mark)
        {
            Vector2 p = CellPlan(c);
            float baseY = c.Ground + c.Dy + lift + (mark ? 0.008f : 0f);
            float rad = cellRadius * s * c.Size * (mark ? 0.94f : 1f);
            _sheet.EmitFlat(p, baseY, rad, mark ? c.MarkHash : c.Hash, mark ? c.MarkPat : c.Pat, Mathf.Clamp(p.y / _reach, 0.3f, 1f));
        }

        // ------------------------------------------------------------ outline ribbon

        /// <summary>Where the sweep's front line is at time <paramref name="a"/> (metres from the caster): the cells' growth front (곡 goes on to x1.5 over its life); 곰 has no sweep, so the
        /// front follows the seeds being planted, near to far.</summary>
        float FrontR(float a)
        {
            if (bite == HexFanBite.FanSeed) return Mathf.Lerp(startDistance, _reach, Mathf.Clamp01((a - 0.3f) / Mathf.Max(0.3f, _seedLast - 0.3f)));
            float u = Mathf.Clamp01((a - _lead) / revealSeconds);
            if (bite == HexFanBite.Thicket && u >= 1f) return _depth * (1f + 0.5f * Mathf.Clamp01((a - 0.4f) / 2.6f));
            return Mathf.Lerp(startDistance, _depth, u);
        }

        /// <summary>The radius the remaining sheet reaches while it leaves, far side first (the inverse of <see cref="LeaveOf"/>); unbounded before it starts.</summary>
        float LeaveR(float a) => a <= _fadeStart ? float.MaxValue : _reach * (1f - (a - _fadeStart) / Mathf.Max(_spread, 1e-3f));

        /// <summary>The wedge's outline (round 3): a cream ribbon (ribbonWidth m) on the ground, in the same mesh, that IS the sweep's front line - its outer arc runs out with the cells as
        /// they grow and runs back in with them as they leave, far side first (공: the same, as they glide home); the sides lengthen and shorten with it. No part of it is drawn before or after
        /// the cells (r2's dotted line before the sweep and alone at the end). 곰 wipes it in 0.3 s once the last seed has landed. Its vertices carry edge = 1, so the shader draws the seam's
        /// colour (cream, a jade bleed) with no fill pattern.</summary>
        void EmitRibbon(float a)
        {
            float t0 = bite == HexFanBite.FanSeed ? 0.3f : _lead;
            float rcRaw = Mathf.Min(FrontR(a) - 0.1f, LeaveR(a));
            float k = Smooth((a - (t0 - 0.03f)) / 0.1f) * Mathf.Clamp01((rcRaw - startDistance + 0.25f) / 0.4f);      // it closes up as the wedge closes
            if (bite == HexFanBite.FanSeed) k *= 1f - Smooth((a - (_seedLast + 0.18f)) / 0.3f);
            float w = ribbonWidth * k;
            if (w < 0.004f) return;
            float m = cellRadius * 0.85f, rIn = startDistance - m, rOut = Mathf.Max(startDistance, rcRaw) + m, th = _half + 0.08f;
            const int arcN = 28, sideN = 8;
            var pts = new Vector2[arcN + 1]; var nrm = new Vector2[arcN + 1];
            for (int i = 0; i <= arcN; i++) { float t = Mathf.Lerp(-th, th, i / (float)arcN); var rd = new Vector2(Mathf.Sin(t), Mathf.Cos(t)); pts[i] = rd * rOut; nrm[i] = rd; }
            Strip(pts, nrm, w);
            for (int i = 0; i <= arcN; i++) { float t = Mathf.Lerp(-th, th, i / (float)arcN); var rd = new Vector2(Mathf.Sin(t), Mathf.Cos(t)); pts[i] = rd * rIn; }
            Strip(pts, nrm, w);
            var sp = new Vector2[sideN + 1]; var sn = new Vector2[sideN + 1];
            for (int s = 0; s < 2; s++)
            {
                float sg = s == 0 ? 1f : -1f, t = sg * th;
                var rd = new Vector2(Mathf.Sin(t), Mathf.Cos(t)); var tan = new Vector2(Mathf.Cos(t), -Mathf.Sin(t));
                for (int i = 0; i <= sideN; i++) { sp[i] = rd * Mathf.Lerp(rIn, rOut, i / (float)sideN); sn[i] = tan; }
                Strip(sp, sn, w);
            }
        }

        void Strip(Vector2[] pts, Vector2[] nrm, float w) => _sheet.Main.Strip(pts, nrm, w, _ribbonHeight, _sheet.Tilt, _reach);
    }
}
