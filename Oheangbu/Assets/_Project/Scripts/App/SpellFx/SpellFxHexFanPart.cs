using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅗ 면 trial (the user: "'ㅜ' 술식 계열처럼 정돈된 형태로" -> "ㅗ 면 6자로 먼저 시험"): the fan in front drawn in the ㅜ ward's grammar -
    /// a translucent hexagon-cell membrane (shader Oheangbu/SpellFx/HexDome), cream seams, a KCISA pattern in every cell - lying on the ground as a wedge. Motion (frame and verb) is the
    /// vine fan's; only the material changed. The cells are built at runtime and rebuilt into ONE dynamic mesh every tick.
    /// Shader contract (read from SpellFxHexDome.shader, unchanged): uv0 = pattern uv in the cell, uv1 = (cell hash, 0 centre .. 1 border), uv2 = (pattern 0..7, height 0..1),
    /// uv3 = the cell's centre in object space. The shader's reveal / end wipe reads the VERTEX object-space y (not uv2.y), so the object is laid on its side: object y = metres
    /// ahead of the caster (scaled so 0.85 object units = the whole reach), object z = down. Then _RiseTime sweeps the sheet forward and _Fade withdraws it from the far side.
    /// Look only - nothing here touches a rule.</summary>
    public sealed class SpellFxHexFanPart : MonoBehaviour, ISpellFxPart
    {
        /// <summary>Same names as the vine fan's verbs: 고 Sweep · 곡 Thicket · 곤 FanSpread · 곰 FanSeed · 곳 FanCut · 공 FanReturn.</summary>
        public enum HexFanBite { Sweep, Thicket, FanSpread, FanSeed, FanCut, FanReturn }

        [SerializeField] Material material;
        [Tooltip("Optional outline of the wedge on the ground (Oheangbu/SpellFx/HexGround); none = no outline.")]
        [SerializeField] Material groundMaterial;
        [SerializeField] HexFanBite bite = HexFanBite.Sweep;
        [Tooltip("What the end of the sheet breaks into (Wood = nothing).")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Wood;
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
        [SerializeField] float revealSeconds = 0.35f;
        [SerializeField] float fadeSeconds = 0.6f;
        [Tooltip("Sample the ground under every cell (a ray, once, when the cell is made).")]
        [SerializeField] bool followGround = true;

        static readonly int AgeId = Shader.PropertyToID("_Age"), FadeId = Shader.PropertyToID("_Fade"), RiseId = Shader.PropertyToID("_RiseTime"),
            HitPosId = Shader.PropertyToID("_HitPos"), HitAgeId = Shader.PropertyToID("_HitAge"), HitCellId = Shader.PropertyToID("_HitCell"),
            LineId = Shader.PropertyToID("_Line"), PatternId = Shader.PropertyToID("_Pattern");

        const float Sqrt3 = 1.7320508f;
        static readonly float[] Cos6 = new float[6], Sin6 = new float[6];
        static readonly Vector2[] Uv6 = new Vector2[6];
        static readonly int[] PatChoices = { 1, 2, 3, 4, 5, 7 };      // as the dome: not 0 / 6 (they read as filled stamps)

        static SpellFxHexFanPart()
        {
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f; Cos6[k] = Mathf.Cos(a); Sin6[k] = Mathf.Sin(a);
                Uv6[k] = new Vector2(0.5f + 0.54f * Cos6[k], 0.5f + 0.54f * Sin6[k]);      // the dome's 1.08 / (2 R)
            }
        }

        // ------------------------------------------------------------ state

        sealed class Cell
        {
            public int I, J;
            public Vector2 Pos;                 // plan: x right, y (= metres) forward
            public Vector2 Shift;               // 곳: the halves open
            public float Hash, Phase, Ground;
            public int Pat;
            public float Born = -1f;            // pop-in start (< 0 = there from the launch)
            public bool Patch, Seed, Axis;
            public float OpenAt = -1f;          // 곳: when this cell's column opens / the axis cell breaks
            public bool Burst;
            public float RiseStart = -1f;       // 곡: when it becomes a standing plate
            public int Stack; public float StackFy = 1f;
        }

        sealed class Ring { public Vector2 Feet; public float Ground, Born, Rh, Fy, Phase; }
        struct Flash { public float At; public Cell Cell; }

        readonly List<Cell> _cells = new List<Cell>();
        readonly Dictionary<long, Cell> _grid = new Dictionary<long, Cell>();
        readonly List<Ring> _rings = new List<Ring>();
        readonly List<Flash> _flashes = new List<Flash>();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly RaycastHit[] _hits = new RaycastHit[8];

        // mesh scratch
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>(), _c = new List<Vector3>();
        readonly List<Vector2> _uv0 = new List<Vector2>(), _uv1 = new List<Vector2>(), _uv2 = new List<Vector2>();
        readonly List<int> _t = new List<int>();
        readonly Vector3[] _cs = new Vector3[6];
        readonly List<Vector3> _gv = new List<Vector3>(); readonly List<Vector2> _guv = new List<Vector2>(); readonly List<int> _gt = new List<int>();

        GameObject _go, _ground;
        Mesh _mesh, _groundMesh;
        Material _mat, _groundMat;
        Color _lineBase, _patBase;
        System.Random _rng;
        bool _built, _launched, _ended, _endBurst;
        float _age;
        Vector3 _origin, _fwd, _right;
        float _depth, _reach, _half, _sy, _fadeStart, _fadeLen, _lastReachDrawn = -1f;
        Vector3 _tilt = Vector3.up;                 // shading normal of the flat cells: leaned toward the camera so the pattern gate (|n.v|) stays open at a low view
        Cell _hitCell; float _hitAt = -1f;
        int _burstDone;

        // ------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast) { _rng = new System.Random((int)(cast.CastId & 0x7fffffff)); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            if (_go != null) _go.SetActive(true);
            if (_ground != null) _ground.SetActive(true);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;
            _ended = true;
            if (_go != null) _go.SetActive(false);
            if (_ground != null) _ground.SetActive(false);
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
            float arrive = Mathf.Max(_age, fz / _depth * revealSeconds + 0.1f);        // when the sweep reaches the body
            var feet = new Vector2(fx, fz);
            if (spread) { SpawnPatch(feet, arrive); return; }
            if (!inside) return;
            Cell cell = Nearest(feet, bite == HexFanBite.FanCut);
            if (cell != null) Schedule(cell, arrive);
            if (bite == HexFanBite.Sweep || bite == HexFanBite.Thicket || bite == HexFanBite.FanSpread) AddRing(feet, arrive, hit.TargetRadius);
            if (bite == HexFanBite.FanReturn && cell != null)
            {
                // the second flash: as the withdrawing wipe (far side first) passes the body
                float f = Mathf.Max(0.05f, (1f - Mathf.Clamp01(fz / _reach)) / 1.35f - 0.06f);
                Schedule(cell, _fadeStart + _fadeLen * f);
            }
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _ended || _mat == null) return;
            float a = ageSeconds;
            float fade = Mathf.Clamp01((a - _fadeStart) / _fadeLen);
            _mat.SetFloat(AgeId, a); _mat.SetFloat(FadeId, fade);
            if (_groundMat != null) { _groundMat.SetFloat(AgeId, a); _groundMat.SetFloat(FadeId, fade); }
            if (a > _fadeStart + _fadeLen + 0.1f) { if (_go.activeSelf) _go.SetActive(false); if (_ground != null && _ground.activeSelf) _ground.SetActive(false); return; }
            if (!_endBurst && a >= _fadeStart) { _endBurst = true; EndBurst(); }
            Flashes(a);
            Animate(a);
            if (bite == HexFanBite.FanSeed && a > 0.3f)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(a * Mathf.PI * 2f * 0.8f);        // 0.8 Hz
                float k = 0.75f + 0.5f * p;
                _mat.SetColor(LineId, new Color(_lineBase.r * k, _lineBase.g * k, _lineBase.b * k, _lineBase.a));
                _mat.SetColor(PatternId, new Color(_patBase.r, _patBase.g, _patBase.b, _patBase.a * (0.55f + 0.45f * p)));
            }
            if (bite == HexFanBite.Thicket) DrawGround(Mathf.Min(_reach, _depth * (1f + 0.5f * Mathf.Clamp01((a - 0.4f) / 2.6f))));
            Rebuild(a);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat); if (_groundMat != null) Destroy(_groundMat);
            if (_mesh != null) Destroy(_mesh); if (_groundMesh != null) Destroy(_groundMesh);
        }

        // ------------------------------------------------------------ build

        float Rand() => (float)_rng.NextDouble();
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static long Key(int i, int j) => ((long)i << 32) | (uint)j;

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
            switch (bite)
            {
                case HexFanBite.Thicket: _fadeStart = 3.5f; _fadeLen = fadeSeconds; break;
                case HexFanBite.FanSpread: _fadeStart = 3.0f; _fadeLen = fadeSeconds; break;
                case HexFanBite.FanSeed: _fadeStart = 8.0f; _fadeLen = fadeSeconds; break;
                case HexFanBite.FanCut: _fadeStart = 2.3f; _fadeLen = fadeSeconds; break;
                case HexFanBite.FanReturn: _fadeStart = revealSeconds + 0.5f; _fadeLen = 0.8f; break;      // 0.5 s after the sweep the wipe runs back toward the caster
                default: _fadeStart = 2.35f; _fadeLen = fadeSeconds; break;
            }
            // the camera sits behind the caster: lean the flat cells' shading normal toward it (only the shading; the geometry stays flat)
            var cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position - _origin : -_fwd; toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-4f) toCam = -_fwd;
            toCam.Normalize();
            Vector2 tc = new Vector2(Vector3.Dot(toCam, _right), Vector3.Dot(toCam, _fwd));
            _tilt = new Vector3(tc.x * 0.5f, 0.866f, tc.y * 0.5f).normalized;

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
            _mat.SetFloat(RiseId, revealSeconds * _reach / _depth);         // the whole fan (to _depth) is swept in revealSeconds
            _mat.SetFloat(AgeId, -1f); _mat.SetFloat(FadeId, 0f); _mat.SetFloat(HitAgeId, -1f);
            _lineBase = _mat.HasProperty(LineId) ? _mat.GetColor(LineId) : Color.white;
            _patBase = _mat.HasProperty(PatternId) ? _mat.GetColor(PatternId) : Color.white;
            _go.SetActive(false);

            if (groundMaterial != null)
            {
                _ground = new GameObject("HexFanGround");
                _ground.transform.SetParent(transform, false);
                _ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (layerId >= 0) _ground.layer = layerId;
                _groundMesh = new Mesh { name = "HexFanGround" };
                _ground.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
                var gr = _ground.AddComponent<MeshRenderer>();
                _groundMat = new Material(groundMaterial);
                gr.sharedMaterial = _groundMat; gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gr.receiveShadows = false;
                _groundMat.SetFloat(AgeId, -1f); _groundMat.SetFloat(FadeId, 0f);
                DrawGround(bite == HexFanBite.Thicket ? _depth : _reach);
                _ground.SetActive(false);
            }
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
            var c = new Cell { I = i, J = j, Pos = CenterOf(i, j), Hash = Rand(), Phase = Rand() * 6.28f, Pat = PatChoices[_rng.Next(PatChoices.Length)] };
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
                    if (bite == HexFanBite.FanCut) { c.Axis = i == 0; c.OpenAt = 0.45f + Mathf.Clamp01(p.y / _depth) * 0.35f; }
                }
            if (bite == HexFanBite.FanSeed) PickSeeds();
            if (bite == HexFanBite.Thicket) PickSteles();
            foreach (var c in _cells) c.Ground = GroundAt(c.Pos);
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
            for (int k = 0; k < _cells.Count; k++) _cells[k].Born = 0.25f + 0.17f * k;
        }

        /// <summary>곡: about a quarter of the cells (not the nearest ones) stand up one after another into plates 0.6 - 1.4 m tall, built of stacked hexagons.</summary>
        void PickSteles()
        {
            var order = new List<Cell>();
            foreach (var c in _cells) if (c.Pos.magnitude >= 2.2f && _rng.NextDouble() < 0.25) order.Add(c);
            order.Sort((p, q) => p.Pos.y.CompareTo(q.Pos.y));
            float step = 1.8f / Mathf.Max(1, order.Count);
            for (int k = 0; k < order.Count; k++)
            {
                var c = order[k];
                float h = 0.6f + 0.8f * Rand(), hex = Sqrt3 * cellRadius;
                c.Stack = Mathf.Max(1, Mathf.RoundToInt(h / hex)); c.StackFy = h / (c.Stack * hex);
                c.RiseStart = Mathf.Max(0.6f + k * step, c.Born > 0f ? c.Born + 0.2f : 0f);
            }
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

        Vector2 CellPlan(Cell c) => c.Pos + c.Shift;

        void Schedule(Cell cell, float at)
        {
            // one latest-hit slot in the shader: keep the flashes 0.1 s apart so each one reads
            for (int guard = 0; guard < 40; guard++)
            {
                bool clash = false;
                foreach (var f in _flashes) if (Mathf.Abs(f.At - at) < 0.1f) { clash = true; break; }
                if (!clash) break;
                at += 0.1f;
            }
            var fl = new Flash { At = at, Cell = cell };
            int k = 0; while (k < _flashes.Count && _flashes[k].At <= at) k++;
            _flashes.Insert(k, fl);
        }

        void Flashes(float a)
        {
            while (_flashes.Count > 0 && _flashes[0].At <= a)
            {
                _hitCell = _flashes[0].Cell; _hitAt = Mathf.Max(a, 0f);
                _flashes.RemoveAt(0);
            }
            if (_hitCell == null) { _mat.SetFloat(HitAgeId, -1f); return; }
            Vector2 p = CellPlan(_hitCell);
            Vector3 w = _go.transform.TransformPoint(ToLocal(new Vector3(p.x, _hitCell.Ground + lift, p.y)));
            _mat.SetVector(HitPosId, w);
            _mat.SetVector(HitCellId, new Vector4(w.x, w.y, w.z, 1f));
            _mat.SetFloat(HitAgeId, a - _hitAt);
        }

        void AddRing(Vector2 feet, float born, float bodyRadius)
        {
            float rh = Mathf.Clamp(Mathf.Clamp(bodyRadius, 0.25f, 0.9f) * 0.8f + 0.3f, 0.55f, 1.0f);       // the hexagon's circumradius = its side
            float rp = rh * 0.5f;
            _rings.Add(new Ring { Feet = feet, Ground = GroundAt(feet), Born = born, Rh = rh, Fy = 0.6f / (Sqrt3 * rp), Phase = Rand() * 1.05f });
        }

        /// <summary>곤: from the sheet's nearest cell a chain of cells crawls to the body beside it and closes round its feet (5 - 6 cells, one per 0.3 s).</summary>
        void SpawnPatch(Vector2 feet, float arrive)
        {
            Cell start = Nearest(feet, false);
            if (start == null) return;
            int made = 0; float t = arrive;
            float step = Sqrt3 * cellRadius;
            Vector2 dir = feet - start.Pos; float len = dir.magnitude;
            Vector2 dn = len > 1e-3f ? dir / len : Vector2.up;
            int chain = Mathf.Min(3, Mathf.Max(0, Mathf.FloorToInt((len - 0.45f) / step)));
            for (int s = 1; s <= chain; s++) if (MakePatch(start.Pos + dn * (step * s), t)) { made++; t += 0.3f; }
            SnapTo(feet, out int fi, out int fj);
            if (MakePatch(CenterOf(fi, fj), t)) { made++; t += 0.3f; }
            var ring = new List<Vector2>();
            for (int k = 0; k < 6; k++)
            {
                float ang = (30f + 60f * k) * Mathf.Deg2Rad;
                ring.Add(CenterOf(fi, fj) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * step);
            }
            ring.Sort((p, q) => (p - start.Pos).sqrMagnitude.CompareTo((q - start.Pos).sqrMagnitude));
            foreach (var q in ring) { if (made >= 6) break; if (MakePatch(q, t)) { made++; t += 0.3f; } }
        }

        bool MakePatch(Vector2 q, float born)
        {
            SnapTo(q, out int i, out int j);
            if (_grid.ContainsKey(Key(i, j))) return false;
            Vector2 c0 = CenterOf(i, j);
            if (c0.y < 0.2f) return false;
            var c = NewCell(i, j);
            c.Patch = true; c.Born = born; c.Ground = GroundAt(c.Pos);
            return true;
        }

        void EndBurst()
        {
            if (burstBody == SpellFxBurstBody.Wood) return;
            Color edge = _mat.HasProperty(LineId) ? _mat.GetColor(LineId) : Color.white;
            edge = new Color(Mathf.Clamp01(edge.r), Mathf.Clamp01(edge.g), Mathf.Clamp01(edge.b), 1f);
            for (int k = 0; k < 2; k++)
            {
                float th = (k == 0 ? 0f : _half * 0.5f), r = _depth * (k == 0 ? 0.85f : 0.7f);
                Vector2 p = new Vector2(Mathf.Sin(th) * r, Mathf.Cos(th) * r);
                Vector3 w = PlanToWorld(p, GroundAt(p) + 0.05f);
                Vector3 outward = w - _origin; outward.y = 0f; outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : _fwd;
                Vector3 n = burstBody == SpellFxBurstBody.Metal ? outward : Vector3.up;
                if (burstBody == SpellFxBurstBody.Earth) SpellFxLabShot.InkBurst(w, Vector3.up, SpellFxBurstBody.Armour, edge);
                else SpellFxLabShot.InkBurst(w, n, burstBody);
            }
        }

        // ------------------------------------------------------------ animation

        void Animate(float a)
        {
            if (bite != HexFanBite.FanCut) return;
            int axisIdx = 0;
            foreach (var c in _cells)
            {
                if (c.Axis)
                {
                    if (!c.Burst && a >= c.OpenAt)
                    {
                        c.Burst = true;
                        if (axisIdx % 3 == 1 && _burstDone < 5)
                        {
                            _burstDone++;
                            Vector3 w = PlanToWorld(c.Pos, c.Ground + lift + 0.1f);
                            Vector3 nrm = (Vector3.up + _right * (_burstDone % 2 == 0 ? 0.7f : -0.7f)).normalized;
                            Color e = _mat.HasProperty(LineId) ? _mat.GetColor(LineId) : Color.white;
                            SpellFxLabShot.InkBurst(w, nrm, SpellFxBurstBody.Armour, new Color(Mathf.Clamp01(e.r), Mathf.Clamp01(e.g), Mathf.Clamp01(e.b), 1f));
                        }
                    }
                    axisIdx++;
                }
                else c.Shift.x = Mathf.Sign(c.I) * 0.15f * Smooth((a - c.OpenAt) / 0.25f);       // the halves open 0.15 m each way
            }
        }

        float VisOf(Cell c, float a)
        {
            float v = c.Born >= 0f ? Smooth((a - c.Born) / 0.2f) : 1f;
            if (c.Seed) v *= 1f + 0.07f * Mathf.Sin(a * Mathf.PI * 2f * 0.8f + c.Phase);
            if (c.Axis && c.OpenAt >= 0f) v *= 1f - Smooth((a - c.OpenAt) / 0.12f);
            return v;
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
        Vector3 PlanToWorld(Vector2 xz, float y) => _origin + _right * xz.x + Vector3.up * y + _fwd * xz.y;

        void Rebuild(float a)
        {
            _v.Clear(); _n.Clear(); _c.Clear(); _uv0.Clear(); _uv1.Clear(); _uv2.Clear(); _t.Clear();
            foreach (var c in _cells)
            {
                float vis = VisOf(c, a);
                if (vis > 0.02f) EmitFloor(c, vis);
                if (c.Stack > 0 && c.RiseStart >= 0f)
                {
                    float g = Smooth((a - c.RiseStart) / 0.45f);
                    if (g > 0.02f) EmitStele(c, g);
                }
            }
            foreach (var r in _rings) EmitRing(r, a);
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv0); _mesh.SetUVs(1, _uv1); _mesh.SetUVs(2, _uv2); _mesh.SetUVs(3, _c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>One hexagon cell as a fan of six triangles: centre vertex edge 0, corner vertices edge 1 (the dome's own build). <paramref name="faceN"/> picks the winding
        /// (the front face is the one it looks at); <paramref name="shadeN"/> is the shading normal.</summary>
        void Emit(Vector3 ctr, Vector3[] cs, float hash, int pat, float d01, Vector3 faceN, Vector3 shadeN, Vector3 ctrPlan)
        {
            int b = _v.Count;
            Vector3 sn = NormalLocal(shadeN), cl = ToLocal(ctrPlan);
            _v.Add(ToLocal(ctr)); _n.Add(sn); _uv0.Add(new Vector2(0.5f, 0.5f)); _uv1.Add(new Vector2(hash, 0f)); _uv2.Add(new Vector2(pat, d01)); _c.Add(cl);
            for (int k = 0; k < 6; k++)
            {
                _v.Add(ToLocal(cs[k])); _n.Add(sn); _uv0.Add(Uv6[k]); _uv1.Add(new Vector2(hash, 1f)); _uv2.Add(new Vector2(pat, d01)); _c.Add(cl);
            }
            bool flip = Vector3.Dot(Vector3.Cross(cs[1] - ctr, cs[0] - ctr), faceN) < 0f;
            for (int k = 0; k < 6; k++)
            {
                int p = b + 1 + k, q = b + 1 + (k + 1) % 6;
                if (!flip) { _t.Add(b); _t.Add(q); _t.Add(p); } else { _t.Add(b); _t.Add(p); _t.Add(q); }
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

        void EmitStele(Cell c, float g)
        {
            Vector2 p = CellPlan(c);
            Vector2 toCaster = p.sqrMagnitude > 1e-4f ? -p.normalized : -Vector2.up;
            var n3 = new Vector3(toCaster.x, 0f, toCaster.y);
            var u = new Vector3(n3.z, 0f, -n3.x);
            Vector3 shade = (n3 + Vector3.up * 0.25f).normalized;
            float hexH = Sqrt3 * cellRadius * c.StackFy * g;
            for (int s = 0; s < c.Stack; s++)
            {
                var ctr = new Vector3(p.x, c.Ground + lift + hexH * (s + 0.5f), p.y);
                for (int k = 0; k < 6; k++)
                    _cs[k] = ctr + u * (Cos6[k] * cellRadius) + Vector3.up * (Sin6[k] * cellRadius * c.StackFy * g);
                Emit(ctr, _cs, Mathf.Repeat(c.Hash + 0.17f * s, 1f), PatChoices[(c.Pat + s) % PatChoices.Length], Mathf.Clamp(p.y / _reach, 0.3f, 1f), n3, shade, ctr);
            }
        }

        /// <summary>붙듦: six upright hexagon plates round the body's feet, a hexagonal barrel 0.6 m high (plates on the sides of a hexagon of circumradius Rh).</summary>
        void EmitRing(Ring r, float a)
        {
            float rp = r.Rh * 0.5f, apothem = r.Rh * 0.8660254f;
            for (int k = 0; k < 6; k++)
            {
                float g = Smooth((a - r.Born - 0.04f * k) / 0.25f);
                if (g < 0.03f) continue;
                float ang = r.Phase + (30f + 60f * k) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 pc = r.Feet + dir * apothem;
                var n3 = new Vector3(dir.x, 0f, dir.y);
                var u = new Vector3(n3.z, 0f, -n3.x);
                float half = Sqrt3 * 0.5f * rp * r.Fy * g;
                var ctr = new Vector3(pc.x, r.Ground + lift + half, pc.y);
                for (int j = 0; j < 6; j++)
                    _cs[j] = ctr + u * (Cos6[j] * rp) + Vector3.up * (Sin6[j] * rp * r.Fy * g);
                Emit(ctr, _cs, Mathf.Repeat(r.Phase * 3f + 0.19f * k, 1f), PatChoices[k % PatChoices.Length], Mathf.Clamp(pc.y / _reach, 0.3f, 1f), n3, (n3 + Vector3.up * 0.3f).normalized, ctr);
            }
        }

        // ------------------------------------------------------------ ground outline

        /// <summary>The wedge's outline on the ground in the ground mark's language (a cream line, ink seeping outward): four strips, world-space mesh (the object sits at the origin).</summary>
        void DrawGround(float reachNow)
        {
            if (_groundMesh == null || Mathf.Abs(reachNow - _lastReachDrawn) < 0.02f) return;
            _lastReachDrawn = reachNow;
            _gv.Clear(); _guv.Clear(); _gt.Clear();
            float rIn = startDistance - 0.15f, rOut = reachNow + 0.15f, th = _half + 0.1f, y = 0.02f;
            const int arcN = 28, sideN = 8;
            var pts = new Vector2[arcN + 1]; var outs = new Vector2[arcN + 1];
            for (int i = 0; i <= arcN; i++) { float t = Mathf.Lerp(-th, th, i / (float)arcN); Vector2 rd = new Vector2(Mathf.Sin(t), Mathf.Cos(t)); pts[i] = rd * rOut; outs[i] = rd; }
            Strip(pts, outs, y);
            for (int i = 0; i <= arcN; i++) { float t = Mathf.Lerp(-th, th, i / (float)arcN); Vector2 rd = new Vector2(Mathf.Sin(t), Mathf.Cos(t)); pts[i] = rd * rIn; outs[i] = -rd; }
            Strip(pts, outs, y);
            var sp = new Vector2[sideN + 1]; var so = new Vector2[sideN + 1];
            for (int s = 0; s < 2; s++)
            {
                float sg = s == 0 ? 1f : -1f, t = sg * th;
                Vector2 rd = new Vector2(Mathf.Sin(t), Mathf.Cos(t)), tan = new Vector2(Mathf.Cos(t), -Mathf.Sin(t)) * sg;
                for (int i = 0; i <= sideN; i++) { sp[i] = rd * Mathf.Lerp(rIn, rOut, i / (float)sideN); so[i] = tan; }
                Strip(sp, so, y);
            }
            _groundMesh.Clear();
            _groundMesh.SetVertices(_gv); _groundMesh.SetUVs(0, _guv); _groundMesh.SetTriangles(_gt, 0);
            _groundMesh.RecalculateBounds();
        }

        void Strip(Vector2[] pts, Vector2[] outs, float y)
        {
            const float W = 0.45f;       // band width; the mark's foot line sits at 0.8 of it (the shader's _Foot) = on the outline
            int b = _gv.Count;
            for (int i = 0; i < pts.Length; i++)
            {
                float v = i / (float)(pts.Length - 1);
                _gv.Add(PlanToWorld(pts[i] - outs[i] * (0.8f * W), y)); _guv.Add(new Vector2(0f, v));
                _gv.Add(PlanToWorld(pts[i] + outs[i] * (0.2f * W), y)); _guv.Add(new Vector2(1f, v));
                if (i < pts.Length - 1) { int k = b + i * 2; _gt.Add(k); _gt.Add(k + 1); _gt.Add(k + 2); _gt.Add(k + 1); _gt.Add(k + 3); _gt.Add(k + 2); }
            }
        }
    }
}
