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
    ///  4 the return (공): the sheet withdraws far cells first toward the caster over 0.8 s (the cells slide home), a second flash and spit as the front passes the body.</summary>
    public sealed class SpellFxHexFanPart : MonoBehaviour, ISpellFxPart
    {
        /// <summary>Same names as the vine fan's verbs: 고 Sweep · 곡 Thicket · 곤 FanSpread · 곰 FanSeed · 곳 FanCut · 공 FanReturn.</summary>
        public enum HexFanBite { Sweep, Thicket, FanSpread, FanSeed, FanCut, FanReturn }

        [SerializeField] Material material;
        [SerializeField] HexFanBite bite = HexFanBite.Sweep;
        [Tooltip("What the sheet's ink spits are made of (Wood = no spits at all).")]
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
        [SerializeField] float leadSeconds = 0.14f;
        [Tooltip("The outline ribbon's width in metres.")]
        [SerializeField] float ribbonWidth = 0.06f;
        [Tooltip("Sample the ground under every cell (a ray, once, when the cell is made).")]
        [SerializeField] bool followGround = true;

        static readonly int AgeId = Shader.PropertyToID("_Age"), FadeId = Shader.PropertyToID("_Fade"), RiseId = Shader.PropertyToID("_RiseTime"),
            HitPosId = Shader.PropertyToID("_HitPos"), HitAgeId = Shader.PropertyToID("_HitAge"), HitCellId = Shader.PropertyToID("_HitCell"),
            LineId = Shader.PropertyToID("_Line"), PatternId = Shader.PropertyToID("_Pattern"), GlassId = Shader.PropertyToID("_Glass");

        const float Sqrt3 = 1.7320508f, HashStep = 1f / 13.7f;
        static readonly float[] Cos6 = new float[6], Sin6 = new float[6];
        static readonly Vector2[] Uv6 = new Vector2[6];
        static readonly int[] PatChoices = { 1, 2, 3, 4, 5, 7 };      // as the dome: not 0 / 6 (they read as filled stamps)
        /// <summary>(hash, bloom phase) of every hash whose bloom period is short (0.9 - 1.34 s, the shader's h3 &lt; 0.4) and below 0.8 (so hash + 1/13.7 stays under the 0.88 flicker step).</summary>
        static readonly List<Vector2> HashTable = new List<Vector2>(1800);
        static readonly float[] WrapAng = { 0f, 70f, -70f, 140f, -140f };         // round the body, from the side that faces the caster
        static readonly float[] WrapHeight = { 0.56f, 0.66f, 0.66f, 0.74f, 0.74f };      // x the body's height: chest, shoulders, neck - never the face

        static float Frac(float x) => x - Mathf.Floor(x);

        static SpellFxHexFanPart()
        {
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f; Cos6[k] = Mathf.Cos(a); Sin6[k] = Mathf.Sin(a);
                Uv6[k] = new Vector2(0.5f + 0.54f * Cos6[k], 0.5f + 0.54f * Sin6[k]);      // the dome's 1.08 / (2 R)
            }
            for (int i = 0; i < 4096; i++)
            {
                float h = i / 4096f * 0.8f;
                if (Frac(h * 13.7f + 0.11f) > 0.4f) continue;
                HashTable.Add(new Vector2(h, Frac(h * 7.13f + 0.37f)));
            }
        }

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
        }

        /// <summary>A standing hexagon: a 곡 stele (grows up from the ground) or a hold cell on a body (grows from its centre). Dir = the horizontal direction it faces (plan x, z).</summary>
        sealed class Plate
        {
            public Vector3 Ctr;                 // plan x, y (relative to the cast origin), z; a sprout's Ctr.y is its foot
            public Vector2 Dir;
            public float Rx, Rv;                // horizontal / vertical corner radius at full size
            public float Start, Grow = 0.2f, LeaveAt = float.MaxValue;
            public float Hash, D01; public int Pat; public bool Sprout;
        }

        sealed class Flight { public Vector3 From, To; public float T0, T1, Rc, Arc, Hash; }
        struct Flash { public float At; public Cell Cell; public Plate Plate; }
        struct Ink { public float At; public Vector3 Point, Normal; }

        readonly List<Cell> _cells = new List<Cell>();
        readonly Dictionary<long, Cell> _grid = new Dictionary<long, Cell>();
        readonly List<Plate> _plates = new List<Plate>();
        readonly List<Flight> _flights = new List<Flight>();
        readonly List<Flash> _flashes = new List<Flash>();
        readonly List<Ink> _ink = new List<Ink>();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly RaycastHit[] _hits = new RaycastHit[8];
        readonly Dictionary<long, float> _groundCache = new Dictionary<long, float>();

        // mesh scratch
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>(), _c = new List<Vector3>();
        readonly List<Vector2> _uv0 = new List<Vector2>(), _uv1 = new List<Vector2>(), _uv2 = new List<Vector2>();
        readonly List<int> _t = new List<int>();
        readonly Vector3[] _cs = new Vector3[6];

        GameObject _go;
        Mesh _mesh;
        Material _mat;
        Color _lineBase, _patBase, _glassBase;
        System.Random _rng;
        bool _built, _launched, _ended;
        float _age;
        Vector3 _origin, _fwd, _right;
        float _depth, _reach, _half, _sy, _fadeStart, _spread = 0.5f, _leaveLen = 0.2f, _endAt, _lead;
        Vector3 _tilt = Vector3.up;                 // shading normal: leaned toward the camera so the shader's |n.v| gates (noNew 0.2 - 0.35, facing 0.45 - 0.95) stay open at a low view
        Vector3 _faceCam = Vector3.up;              // the side a standing plate's front face is wound to (the camera's), so it always draws as the strong front pass
        Vector3 _tipPlan;                           // the brush tip in plan coordinates (x right, y up from the origin, z ahead)
        Cell _hitCell; Plate _hitPlate; float _hitAt = -1f;

        // ------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast) { _rng = new System.Random((int)(cast.CastId & 0x7fffffff)); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            if (_go != null) _go.SetActive(true);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;
            _ended = true;
            if (_go != null) _go.SetActive(false);
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
            float R = hit.TargetRadius > 0.05f ? Mathf.Clamp(hit.TargetRadius, 0.3f, 0.9f) : 0.5f;
            // F5 still: the hold cells sat over the bear's head like a crown - hit.Feet is not the ground (the vine's old pitfall) and twice the centre's height
            // overshot a four-legged body; the ground under the feet, and the body's height as 1.3 x its centre above that ground
            float gy = Mathf.Clamp(GroundAt(feet), -1f, 1f);
            float dy = hit.Position.y - (_origin.y + gy);
            float H = dy > 0.2f ? Mathf.Clamp(dy * 1.3f, 0.8f, 2.4f) : 1.2f;
            if (spread) { SpawnPatch(feet, arrive, R, H, gy); return; }
            if (!inside) return;
            float dist = feet.magnitude;
            Plate front;
            if (bite == HexFanBite.FanCut)
            {
                // the cut runs row by row; the cells settle on the body once the opening has passed it
                arrive = Mathf.Max(arrive, 0.75f + 0.1f * Mathf.Max(0f, fz - startDistance) / (Sqrt3 * cellRadius) + 0.1f);
                front = AddWrap(feet, gy, R, H, arrive, 2, LeaveOf(dist) + 0.1f);
            }
            else
            {
                Cell cell = Nearest(feet, false);
                if (cell != null) Schedule(cell, null, arrive);                                   // the ground cell at the feet answers first
                float tCross = LeaveOf(dist);
                front = AddWrap(feet, gy, R, H, arrive, bite == HexFanBite.Thicket ? 4 : 5, bite == HexFanBite.FanReturn ? tCross + 0.25f : tCross + 0.1f);
                if (bite == HexFanBite.FanReturn && front != null)
                {
                    // the second flash and spit: as the withdrawing front (far side first) passes the body
                    Schedule(null, front, tCross - 0.02f);
                    QueueInk(tCross, ContactPoint(front), ContactNormal(front));
                    if (cell != null) Schedule(cell, null, tCross + 0.1f);
                }
            }
            if (front != null)
            {
                float tt = front.Start + 0.05f;
                Schedule(null, front, tt);
                QueueInk(tt, ContactPoint(front), ContactNormal(front));
            }
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _ended || _mat == null) return;
            float a = ageSeconds;
            _mat.SetFloat(AgeId, a); _mat.SetFloat(FadeId, 0f);
            if (a > _endAt) { if (_go.activeSelf) _go.SetActive(false); return; }
            Inks(a);
            Flashes(a);
            Animate(a);
            // a breath while it stands: 2 Hz, +- 10 % on the seam, the pattern and the fill
            float k = 1f + 0.1f * Mathf.Sin(a * Mathf.PI * 2f * 2f);
            _mat.SetColor(LineId, new Color(_lineBase.r * k, _lineBase.g * k, _lineBase.b * k, _lineBase.a));
            _mat.SetColor(PatternId, new Color(_patBase.r * k, _patBase.g * k, _patBase.b * k, _patBase.a));
            if (_mat.HasProperty(GlassId)) _mat.SetColor(GlassId, new Color(_glassBase.r, _glassBase.g, _glassBase.b, Mathf.Clamp01(_glassBase.a * k)));
            Rebuild(a);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_mesh != null) Destroy(_mesh);
        }

        // ------------------------------------------------------------ build

        float Rand() => (float)_rng.NextDouble();
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static long Key(int i, int j) => ((long)i << 32) | (uint)j;

        /// <summary>A hash whose bloom phase is near <paramref name="phase"/> and whose period is short: the shader blooms each cell's pattern at its own moment, a few tenths of a
        /// second in about a second, so a short period keeps the most cells lit (the second layer's hash + 1/13.7 shares the period and is half a period out).</summary>
        float PickHash(float phase)
        {
            var near = new List<float>();
            float best = float.MaxValue, bh = 0.1f;
            foreach (var t in HashTable)
            {
                float d = Mathf.Abs(t.y - phase); d = Mathf.Min(d, 1f - d);
                if (d < best) { best = d; bh = t.x; }
                if (d < 0.03f) near.Add(t.x);
            }
            return near.Count > 0 ? near[_rng.Next(near.Count)] : bh;
        }

        /// <summary>When a cell at <paramref name="dist"/> metres starts to leave: the far side first, over the spread.</summary>
        float LeaveOf(float dist) => _fadeStart + (1f - Mathf.Clamp01(dist / Mathf.Max(_reach, 0.1f))) * _spread;

        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;

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
            _spread = 0.5f; _leaveLen = 0.2f;
            switch (bite)
            {
                case HexFanBite.Thicket: _fadeStart = 3.5f; break;
                case HexFanBite.FanSpread: _fadeStart = 3.0f; break;
                case HexFanBite.FanSeed: _fadeStart = 8.0f; break;
                case HexFanBite.FanCut: _fadeStart = 2.3f; break;
                case HexFanBite.FanReturn: _fadeStart = _lead + revealSeconds + 0.18f + 0.5f; _spread = 0.8f; _leaveLen = 0.3f; break;      // 0.5 s after the sweep the cells leave, far side first, over 0.8 s
                default: _fadeStart = 2.35f; break;
            }
            _endAt = _fadeStart + _spread + 0.25f + _leaveLen + 0.2f;
            // the camera sits behind the caster: lean the cells' shading normal toward it (only the shading; the geometry stays flat)
            var cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position - _origin : -_fwd; toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-4f) toCam = -_fwd;
            toCam.Normalize();
            Vector2 tc = new Vector2(Vector3.Dot(toCam, _right), Vector3.Dot(toCam, _fwd));
            _tilt = new Vector3(tc.x * 0.35f, 0.937f, tc.y * 0.35f).normalized;      // lean 0.35 (was 0.5): |n.v| ~0.55 - 0.77 at a 13 - 30 deg view keeps the pattern gates open but leaves the fill (sil) some body
            _faceCam = new Vector3(tc.x, 0.15f, tc.y);
            // the brush tip (as the vine's shot leaves it)
            Vector3? tip = cast.BrushTip != null ? cast.BrushTip() : null;
            Vector3 tw = tip ?? (cast.Origin + Vector3.up * 1.35f + _fwd * 0.4f + _right * 0.25f);
            tw.y = Mathf.Min(tw.y, cast.Origin.y + 1.75f);
            Vector3 trel = tw - _origin;
            _tipPlan = new Vector3(Vector3.Dot(trel, _right), trel.y, Vector3.Dot(trel, _fwd));

            BuildCells();

            // the object lies on its side (see the class comment): local x = right, local y = ahead, local z = down
            _go = new GameObject("HexFan");
            _go.transform.SetParent(transform, false);
            _go.transform.SetPositionAndRotation(_origin, Quaternion.LookRotation(Vector3.down, _fwd));
            _go.transform.localScale = new Vector3(1f, _sy, 1f);
            int layerId = VfxAfterFog308.Layer;
            if (layerId >= 0) _go.layer = layerId;
            _mesh = new Mesh { name = "HexFan" }; _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            _mat = new Material(material);
            r.sharedMaterial = _mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            _mat.SetFloat(RiseId, 0.06f);               // the shader's own sweep is short: the cells' growth (Born) is the sweep now
            _mat.SetFloat(AgeId, -1f); _mat.SetFloat(FadeId, 0f); _mat.SetFloat(HitAgeId, -1f);
            _lineBase = _mat.HasProperty(LineId) ? _mat.GetColor(LineId) : Color.white;
            _patBase = _mat.HasProperty(PatternId) ? _mat.GetColor(PatternId) : Color.white;
            _glassBase = _mat.HasProperty(GlassId) ? _mat.GetColor(GlassId) : Color.clear;
            _go.SetActive(false);
            _built = true;
        }

        Vector2 CenterOf(int i, int j) => new Vector2(i * 1.5f * cellRadius, (j + 0.5f * (i & 1)) * Sqrt3 * cellRadius);

        void SnapTo(Vector2 p, out int bi, out int bj)
        {
            float dx = 1.5f * cellRadius, dz = Sqrt3 * cellRadius, best = float.MaxValue;
            int i0 = Mathf.RoundToInt(p.x / dx); bi = i0; bj = 0;
            for (int di = -1; di <= 1; di++)
            {
                int i = i0 + di, j0 = Mathf.RoundToInt(p.y / dz - 0.5f * (i & 1));
                for (int dj = -1; dj <= 1; dj++)
                {
                    int j = j0 + dj; float d = (CenterOf(i, j) - p).sqrMagnitude;
                    if (d < best) { best = d; bi = i; bj = j; }
                }
            }
        }

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
                _plates.Add(new Plate
                {
                    Ctr = new Vector3(c.Pos.x, c.Ground + lift, c.Pos.y), Dir = toCaster, Rx = 2f * cellRadius, Rv = h / (2f * 0.8660254f),
                    Start = Mathf.Max(0.6f + k * step, c.Born + 0.2f), Grow = 0.45f, LeaveAt = LeaveOf(dist) + Rand() * 0.08f,
                    Hash = PickHash(Rand()), D01 = Mathf.Clamp(dist / _reach, 0.3f, 1f), Pat = PatChoices[(c.Pat + k) % PatChoices.Length], Sprout = true,
                });
            }
        }

        /// <summary>The end: about eight cells (far side first) spit ink as they break up.</summary>
        void PickEndInk()
        {
            if (burstBody == SpellFxBurstBody.Wood || _cells.Count == 0) return;
            var order = new List<Cell>(_cells);
            order.Sort((p, q) => q.Pos.sqrMagnitude.CompareTo(p.Pos.sqrMagnitude));
            int n = Mathf.Min(bite == HexFanBite.FanReturn ? 5 : 8, order.Count);
            for (int k = 0; k < n; k++)
            {
                var c = order[Mathf.Min(order.Count - 1, (int)((k + 0.5f) * order.Count / n))];
                QueueInk(c.LeaveAt, PlanW(new Vector3(c.Pos.x, c.Ground + lift + 0.05f, c.Pos.y)), Vector3.up);
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
                QueueInk(c.OpenAt, PlanW(new Vector3(c.Pos.x, c.Ground + lift + 0.1f, c.Pos.y)), (Vector3.up + _right * (made % 2 == 0 ? 0.7f : -0.7f)).normalized);
            }
        }

        /// <summary>Gate 3a: the first cell or two come off the brush tip and fly to the fan's start (or, for 곰, every seed is thrown to its place); the sweep begins as they land.</summary>
        void MakeFlights()
        {
            if (bite == HexFanBite.FanSeed)
            {
                int k = 0;
                foreach (var c in _cells)
                {
                    var to = new Vector3(c.Pos.x, c.Ground + lift, c.Pos.y);
                    _flights.Add(new Flight { From = _tipPlan, To = to, T0 = c.Born - 0.16f, T1 = c.Born, Rc = cellRadius * 0.7f, Arc = 0.5f + 0.06f * c.Pos.magnitude, Hash = c.Hash });
                    if (burstBody != SpellFxBurstBody.Wood && k % 3 == 0) QueueInk(c.Born, PlanW(to + Vector3.up * 0.04f), Vector3.up);
                    k++;
                }
                return;
            }
            float gy = GroundAt(new Vector2(0f, startDistance));
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -0.15f : 0.15f;
                var to = new Vector3(side, gy + lift, startDistance + 0.1f * k);
                _flights.Add(new Flight { From = _tipPlan, To = to, T0 = 0.05f * k, T1 = _lead + 0.02f * k, Rc = cellRadius * 0.6f, Arc = 0.35f, Hash = PickHash(Rand()) });
            }
            if (burstBody != SpellFxBurstBody.Wood) QueueInk(_lead, PlanW(new Vector3(0f, gy + lift + 0.05f, startDistance)), Vector3.up);
        }

        float GroundAt(Vector2 p)
        {
            if (!followGround) return 0f;
            Vector3 from = _origin + _right * p.x + _fwd * p.y + Vector3.up * 1.2f;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.collider is CharacterController || h.normal.y < 0.5f) continue;       // bodies are not ground
                best = Mathf.Max(best, h.point.y - _origin.y);
            }
            return best > float.NegativeInfinity ? Mathf.Clamp(best, -1f, 1f) : 0f;
        }

        /// <summary>The ribbon's ground, sampled on a 0.2 m lattice and kept (the 곡 outline moves every tick).</summary>
        float GroundQ(Vector2 p)
        {
            if (!followGround) return 0f;
            int qx = Mathf.RoundToInt(p.x / 0.2f), qz = Mathf.RoundToInt(p.y / 0.2f);
            long key = Key(qx, qz);
            if (!_groundCache.TryGetValue(key, out float g)) { g = GroundAt(new Vector2(qx * 0.2f, qz * 0.2f)); _groundCache[key] = g; }
            return g;
        }

        // ------------------------------------------------------------ hits

        Cell Nearest(Vector2 p, bool skipAxis)
        {
            Cell best = null; float bd = float.MaxValue;
            foreach (var c in _cells)
            {
                if (c.Patch || (skipAxis && c.Axis)) continue;
                float d = (c.Pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        Vector2 CellPlan(Cell c) => c.Pos + c.Shift + c.Slide;

        void Schedule(Cell cell, Plate plate, float at)
        {
            // one latest-hit slot in the shader: keep the flashes 0.1 s apart so each one reads
            for (int guard = 0; guard < 40; guard++)
            {
                bool clash = false;
                foreach (var f in _flashes) if (Mathf.Abs(f.At - at) < 0.1f) { clash = true; break; }
                if (!clash) break;
                at += 0.1f;
            }
            var fl = new Flash { At = at, Cell = cell, Plate = plate };
            int k = 0; while (k < _flashes.Count && _flashes[k].At <= at) k++;
            _flashes.Insert(k, fl);
        }

        void Flashes(float a)
        {
            while (_flashes.Count > 0 && _flashes[0].At <= a)
            {
                _hitCell = _flashes[0].Cell; _hitPlate = _flashes[0].Plate; _hitAt = Mathf.Max(a, 0f);
                _flashes.RemoveAt(0);
            }
            if (_hitCell == null && _hitPlate == null) { _mat.SetFloat(HitAgeId, -1f); return; }
            Vector3 plan;
            if (_hitPlate != null) plan = _hitPlate.Ctr;
            else { Vector2 p = CellPlan(_hitCell); plan = new Vector3(p.x, _hitCell.Ground + lift, p.y); }        // = the ctrPlan EmitFloor gives the shader
            Vector3 w = _go.transform.TransformPoint(ToLocal(plan));
            _mat.SetVector(HitPosId, w);
            _mat.SetVector(HitCellId, new Vector4(w.x, w.y, w.z, 1f));
            _mat.SetFloat(HitAgeId, a - _hitAt);
        }

        void QueueInk(float at, Vector3 worldPoint, Vector3 normal)
        {
            if (burstBody == SpellFxBurstBody.Wood) return;
            var e = new Ink { At = at, Point = worldPoint, Normal = normal };
            int k = 0; while (k < _ink.Count && _ink[k].At <= at) k++;
            _ink.Insert(k, e);
        }

        void Inks(float a)
        {
            while (_ink.Count > 0 && _ink[0].At <= a)
            {
                var e = _ink[0]; _ink.RemoveAt(0);
                SpellFxLabShot.InkBurst(e.Point, e.Normal, burstBody);
            }
        }

        Vector3 FaceDirW(Plate p) => _right * p.Dir.x + _fwd * p.Dir.y;
        Vector3 ContactPoint(Plate p) => PlanW(p.Ctr) + FaceDirW(p) * 0.04f;
        Vector3 ContactNormal(Plate p) => (FaceDirW(p) * 0.8f + Vector3.up * 0.4f).normalized;

        /// <summary>Gate 2, the hold: a bridge cell stands up the front of the body from the ground cell, and <paramref name="n"/> (2 - 5) cells wrap the chest, shoulders and neck -
        /// facing outward, never over the face - each growing in 0.2 s, 0.045 s apart. Returns the first (the contact) cell.</summary>
        Plate AddWrap(Vector2 feet, float gy, float R, float H, float t0, int n, float leaveAt)
        {
            Vector2 toC = feet.sqrMagnitude > 1e-4f ? -feet.normalized : -Vector2.up;
            float baseAng = Mathf.Atan2(toC.y, toC.x), ringR = R * 0.85f + 0.03f, rc = Mathf.Clamp(0.3f * R + 0.08f, 0.14f, 0.26f);
            float d01 = Mathf.Clamp(feet.magnitude / _reach, 0.3f, 1f);
            Plate front = null;
            _plates.Add(new Plate
            {
                Ctr = new Vector3(feet.x + toC.x * ringR, gy + H * 0.28f, feet.y + toC.y * ringR), Dir = toC, Rx = rc * 0.7f, Rv = rc * 0.7f,
                Start = t0 + 0.02f, Grow = 0.14f, LeaveAt = leaveAt, Hash = PickHash(Rand()), D01 = d01, Pat = PatChoices[_rng.Next(PatChoices.Length)],
            });
            for (int k = 0; k < n && k < WrapAng.Length; k++)
            {
                float ang = baseAng + WrapAng[k] * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var p = new Plate
                {
                    Ctr = new Vector3(feet.x + dir.x * ringR, gy + H * WrapHeight[k], feet.y + dir.y * ringR), Dir = dir, Rx = rc, Rv = rc,
                    Start = t0 + 0.07f + 0.045f * k, Grow = 0.2f, LeaveAt = leaveAt + 0.02f * k, Hash = PickHash(Rand()), D01 = d01, Pat = PatChoices[(k + _rng.Next(PatChoices.Length)) % PatChoices.Length],
                };
                _plates.Add(p);
                if (front == null) front = p;
            }
            return front;
        }

        /// <summary>곤: from the sheet's nearest cell a chain of cells crawls one by one (6 frames each) across the ground to the body beside it, then the body is held.</summary>
        void SpawnPatch(Vector2 feet, float arrive, float R, float H, float gy)
        {
            Cell start = Nearest(feet, false);
            if (start == null) return;
            Schedule(start, null, arrive);                                  // the edge cell lights: the crawl begins there
            float step = Sqrt3 * cellRadius;
            Vector2 dir = feet - start.Pos; float len = dir.magnitude;
            Vector2 dn = len > 1e-3f ? dir / len : Vector2.up;
            int chain = Mathf.Clamp(Mathf.FloorToInt((len - 0.45f) / step), 0, 8);
            float iv = chain > 4 ? 0.08f : 0.1f, t = arrive + iv;
            for (int s = 1; s <= chain; s++) if (MakePatch(start.Pos + dn * (step * s), t)) t += iv;
            SnapTo(feet, out int fi, out int fj);
            if (MakePatch(CenterOf(fi, fj), t)) t += iv;                    // the cell under its feet
            Plate front = AddWrap(feet, gy, R, H, t, 5, LeaveOf(feet.magnitude) + 0.1f);
            if (front != null)
            {
                float tt = front.Start + 0.05f;
                Schedule(null, front, tt);
                QueueInk(tt, ContactPoint(front), ContactNormal(front));
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
            foreach (var c in _cells)
            {
                if (bite == HexFanBite.FanCut && !c.Axis) c.Shift.x = Mathf.Sign(c.I) * 0.15f * Smooth((a - c.OpenAt) / 0.25f);       // the halves open 0.15 m each way
                if (bite == HexFanBite.FanReturn && a > c.LeaveAt)
                {
                    // the return: the cell slides home toward the caster as it leaves (far cells first, so the edge runs inward)
                    Vector2 home = c.Pos.sqrMagnitude > 1e-4f ? -c.Pos.normalized : -Vector2.up;
                    float u = Mathf.Clamp01((a - c.LeaveAt) / _leaveLen);
                    c.Slide = home * (0.9f * u * u);
                }
            }
        }

        float VisOf(Cell c, float a)
        {
            float v = c.Born >= 0f ? Smooth((a - c.Born) / 0.18f) : 1f;
            if (a > c.LeaveAt) v *= 1f - Smooth((a - c.LeaveAt) / _leaveLen);
            if (c.Seed) v *= 1f + 0.08f * Mathf.Sin(a * Mathf.PI * 2f * 2f + c.Phase);
            if (c.Axis && c.OpenAt >= 0f) v *= 1f - Smooth((a - c.OpenAt) / 0.12f);
            return v;
        }

        float PlateG(Plate p, float a)
        {
            float g = Smooth((a - p.Start) / p.Grow);
            if (a > p.LeaveAt) g *= 1f - Smooth((a - p.LeaveAt) / _leaveLen);
            return g;
        }

        float Wave(float x, float z)
        {
            float ramp = Smooth((_age - 0.25f) / 0.7f);
            float r = Mathf.Sqrt(x * x + z * z);
            float w = 0.5f + 0.5f * Mathf.Sin(r * 2.4f - _age * 2.4f + Mathf.Sin(x * 1.9f) * 0.9f);       // a function of position: shared corners stay shared
            return breathe * ramp * w * w;
        }

        // ------------------------------------------------------------ mesh

        // plan (x right, y up, z ahead) -> object space of the lying object
        Vector3 ToLocal(Vector3 p) => new Vector3(p.x, Mathf.Max(p.z, 0.001f) / _sy, -p.y);
        Vector3 NormalLocal(Vector3 w) => new Vector3(w.x, w.z * _sy, -w.y).normalized;

        void Rebuild(float a)
        {
            _v.Clear(); _n.Clear(); _c.Clear(); _uv0.Clear(); _uv1.Clear(); _uv2.Clear(); _t.Clear();
            foreach (var c in _cells)
            {
                float vis = VisOf(c, a);
                if (vis > 0.02f) EmitFloor(c, vis);
            }
            foreach (var p in _plates)
            {
                float g = PlateG(p, a);
                if (g > 0.02f) EmitPlate(p, g);
            }
            foreach (var f in _flights) if (a >= f.T0 && a < f.T1) EmitFlight(f, a);
            EmitRibbon(a);
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv0); _mesh.SetUVs(1, _uv1); _mesh.SetUVs(2, _uv2); _mesh.SetUVs(3, _c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>One hexagon cell as a fan of six triangles: centre vertex edge 0, corner vertices edge 1 (the dome's own build). <paramref name="faceN"/> picks the winding
        /// (the front face is the one it looks at); <paramref name="shadeN"/> is the shading normal. Drawn as TWO layers (<paramref name="dbl"/>): the second has hash + 1/13.7 - the
        /// same bloom period, half a period out - so a pattern is lit in one of them most of the time (the shader blooms each pattern only sporadically).</summary>
        void Emit(Vector3 ctr, Vector3[] cs, float hash, int pat, float d01, Vector3 faceN, Vector3 shadeN, Vector3 ctrPlan, bool dbl = true)
        {
            Vector3 sn = NormalLocal(shadeN), cl = ToLocal(ctrPlan);
            bool flip = Vector3.Dot(Vector3.Cross(cs[1] - ctr, cs[0] - ctr), faceN) < 0f;
            for (int layer = 0; layer < (dbl ? 2 : 1); layer++)
            {
                float h = layer == 0 ? hash : hash + HashStep;
                int b = _v.Count;
                _v.Add(ToLocal(ctr)); _n.Add(sn); _uv0.Add(new Vector2(0.5f, 0.5f)); _uv1.Add(new Vector2(h, 0f)); _uv2.Add(new Vector2(pat, d01)); _c.Add(cl);
                for (int k = 0; k < 6; k++)
                {
                    _v.Add(ToLocal(cs[k])); _n.Add(sn); _uv0.Add(Uv6[k]); _uv1.Add(new Vector2(h, 1f)); _uv2.Add(new Vector2(pat, d01)); _c.Add(cl);
                }
                for (int k = 0; k < 6; k++)
                {
                    int p = b + 1 + k, q = b + 1 + (k + 1) % 6;
                    if (!flip) { _t.Add(b); _t.Add(q); _t.Add(p); } else { _t.Add(b); _t.Add(p); _t.Add(q); }
                }
            }
        }

        void EmitFloor(Cell c, float s)
        {
            Vector2 p = CellPlan(c);
            float baseY = c.Ground + lift;
            var ctr = new Vector3(p.x, baseY + Wave(p.x, p.y), p.y);
            for (int k = 0; k < 6; k++)
            {
                float x = p.x + Cos6[k] * cellRadius * s, z = p.y + Sin6[k] * cellRadius * s;
                _cs[k] = new Vector3(x, baseY + Wave(x, z), z);
            }
            Emit(ctr, _cs, c.Hash, c.Pat, Mathf.Clamp(p.y / _reach, 0.3f, 1f), Vector3.up, _tilt, new Vector3(p.x, baseY, p.y));
        }

        /// <summary>A standing hexagon facing <see cref="Plate.Dir"/>: a stele sprouts up from its foot, a hold cell grows from its centre. Wound to the camera's side.</summary>
        void EmitPlate(Plate p, float g)
        {
            float rx, rv; Vector3 ctr = p.Ctr;
            if (p.Sprout) { rx = p.Rx; rv = p.Rv * g; ctr.y += 0.8660254f * rv; }
            else { rx = p.Rx * g; rv = p.Rv * g; }
            var u = new Vector3(p.Dir.y, 0f, -p.Dir.x);
            for (int k = 0; k < 6; k++) _cs[k] = ctr + u * (Cos6[k] * rx) + Vector3.up * (Sin6[k] * rv);
            Emit(ctr, _cs, p.Hash, p.Pat, p.D01, _faceCam, _tilt, ctr);
        }

        /// <summary>A thrown cell in flight (one layer): from the brush tip along a low arc to its place.</summary>
        void EmitFlight(Flight f, float a)
        {
            float u = Mathf.Clamp01((a - f.T0) / Mathf.Max(f.T1 - f.T0, 1e-3f));
            Vector3 p = Vector3.Lerp(f.From, f.To, Smooth(u)) + Vector3.up * (f.Arc * Mathf.Sin(Mathf.PI * u));
            float rc = f.Rc * (0.5f + 0.5f * Smooth(u * 3f));
            for (int k = 0; k < 6; k++) _cs[k] = new Vector3(p.x + Cos6[k] * rc, p.y, p.z + Sin6[k] * rc);
            Emit(p, _cs, f.Hash, PatChoices[0], 0.5f, Vector3.up, _tilt, p, false);
        }

        // ------------------------------------------------------------ outline ribbon

        /// <summary>The wedge's outline: a cream ribbon (ribbonWidth m) on the ground, in the same mesh. Its vertices carry edge = 1, so the shader draws the seam's colour
        /// (cream, a jade bleed) with no fill pattern; it grows in with the sweep, widens with 곡's reach and thins away as the sheet leaves.</summary>
        void EmitRibbon(float a)
        {
            float w = ribbonWidth * Smooth((a - _lead) / 0.2f) * (1f - Smooth((a - _fadeStart - _spread * 0.5f) / (_spread * 0.5f + _leaveLen + 0.1f)));
            if (w < 0.004f) return;
            float reachNow = bite == HexFanBite.Thicket ? Mathf.Min(_reach, _depth * (1f + 0.5f * Mathf.Clamp01((a - 0.4f) / 2.6f))) : _reach;
            float m = cellRadius * 0.85f, rIn = startDistance - m, rOut = reachNow + m, th = _half + 0.08f;
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

        void Strip(Vector2[] pts, Vector2[] nrm, float w)
        {
            Vector3 sh = NormalLocal(_tilt);
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector3 a0 = RibbonPt(pts[i], nrm[i], -w * 0.5f), b0 = RibbonPt(pts[i], nrm[i], w * 0.5f);
                Vector3 a1 = RibbonPt(pts[i + 1], nrm[i + 1], -w * 0.5f), b1 = RibbonPt(pts[i + 1], nrm[i + 1], w * 0.5f);
                int b = _v.Count;
                RibbonVert(a0, sh); RibbonVert(b0, sh); RibbonVert(a1, sh); RibbonVert(b1, sh);
                bool up = Vector3.Cross(b0 - a0, a1 - a0).y > 0f;          // the triangle (a0, b0, a1) faces up when its plan cross product points up
                if (up) { _t.Add(b); _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 1); _t.Add(b + 3); _t.Add(b + 2); }
                else { _t.Add(b); _t.Add(b + 2); _t.Add(b + 1); _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 3); }
            }
        }

        Vector3 RibbonPt(Vector2 p, Vector2 nrm, float off)
        {
            Vector2 q = p + nrm * off;
            return new Vector3(q.x, GroundQ(p) + lift + 0.012f, q.y);
        }

        void RibbonVert(Vector3 plan, Vector3 shadeLocal)
        {
            _v.Add(ToLocal(plan)); _n.Add(shadeLocal); _uv0.Add(new Vector2(0.5f, 0.5f)); _uv1.Add(new Vector2(0.3f, 1f));
            _uv2.Add(new Vector2(PatChoices[0], Mathf.Clamp(plan.z / _reach, 0.3f, 1f))); _c.Add(ToLocal(plan));
        }
    }
}
