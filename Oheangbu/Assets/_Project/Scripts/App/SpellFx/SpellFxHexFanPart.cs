using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅗ 면 in the hex-cell membrane grammar, REWRITTEN IN THE AIR (D308-159, PLAN-308 §2.1; the user: "모든 'ㅗ'술식에 바닥판을 깔지 않고 ... 공중에 띄울 수 있는 것들은 공중에 띄우는 게
    /// 어때?"). The fan used to be a sheet of hexagon cells lying on the ground - at the shoulder camera (2.8 m behind the caster, looking almost along the ground) a lying cell gets a thick seam, a flat
    /// pattern and a smear (ㅗ F1 - F5, the defects of every round). Now NOTHING lies on the ground: the cells stand or float (shader Oheangbu/SpellFx/HexDome, the ㅜ ward's glass, one family with the dome
    /// and the ring), and the ground gets a faint ink wash only (not a plate). The frame is the kit's UPRIGHT one (<see cref="HexFrame.UprightSunk"/>, an object 1 m below the cast origin: the shader's
    /// base-ring term is 0 and nothing is discarded), every standing cell is <see cref="HexSheet.EmitStanding"/> (its own shading normal, |n.v| 0.5 - 0.75, so the dome shader never thins the fill to the
    /// F1 wire cage). Look only - nothing here touches a rule; the part reads <see cref="SpellFxCast"/> events and its own clock.
    /// Common: a small ink and two cells leave the brush tip (0.12 s) for the wall's first two cells (한 번 also gathers its ink at the tip while the brush swings: <see cref="ISpellFxPreLaunch"/>); the
    /// wall is a patch of the dome's honeycomb (two rows of cells, 0.4 - 1.5 m high, as wide as the fan is at that distance) that sweeps from 1.3 m (nothing standing nearer: the caster hides 0 - 2 m)
    /// to the fan's depth and, where a body is met, leaves 3 - 5 cells ON the body's real surface (the kit's hold: a ray per cell; they follow the body when it moves) with a flash and a small ink; a 2 Hz
    /// breath while it stands; the end is cells breaking into ink, the far ones first (the body's own burst only there; wood: small plain ink). Every mid-way ink is the small plain ink.
    /// A hit-stop (2 frames) is the part's own VISUAL clock standing still when a body is passed - no rule clock is touched.
    /// Verbs (times from the launch, TEST):
    ///  고 Sweep: 0 tip ink + 2 cells; 0.12 wall stands at 1.3 m; 0.12 - 0.55 sweeps to the depth; body passed: hit-stop + flash + hold (to 2.0); 0.55 - 0.85 the wall breaks, outer cells first; 2.0 - 2.3 the hold lets go.
    ///  곡 Thicket: the same sweep (0.12 - 0.5, the wall just passes); from 0.6 big plates (2 cells wide, 1.0 / 1.4 / 1.8 m high, floating 0.3 - 0.5 m off the ground, +- 15 deg) stand up one after another, 10 by
    ///    2.2 s then 5 more beyond the depth by 3.0 (the fan x1.5), a foot ink each; 3.5 - 4.0 they break, far first.
    ///  곤 FanSpread: the sweep; 0.2 s after the first body is held 4 - 6 cells hop (0.1 s apart, 0.25 s each, apex 0.8 m) to the neighbour and the ones that land first stay floating at knee height as a road for 3 s;
    ///    the last lands on the neighbour (hold + flash + ink).
    ///  곰 FanSeed: from 0.3 s 10 - 14 egg cells are thrown from the brush tip one after another (0.17 s apart, nearest first) to knee - waist height (0.4 - 0.9 m) and wait there (a thin ink thread under each,
    ///    +- 3 cm bob, a +- 6 % size pulse on top of the 2 Hz breath); when a body comes in the 3 nearest fly to its surface; an unused egg bursts at 7.8 s.
    ///  곳 FanCut: a blade (ONE row of cells, leaning 20 deg forward) passes in 0.3 s (0.12 - 0.42) and leaves a dark 4 cm crack on the axis for 3 s; a body passed: hit-stop + torn plates in its colour + 2 hold cells.
    ///  공 FanReturn: the sweep out (0.12 - 0.55), 0.3 s at the end, then every cell really travels (ease-in) to ONE cell near the caster (0.85 - 1.65, the far cells first); the body is touched again as they pass
    ///    (second flash + ink, the hold lets go); the home cell grows as they arrive and flashes, spits and stops the clock for 2 frames when the last lands, then breaks.</summary>
    public sealed class SpellFxHexFanPart : MonoBehaviour, ISpellFxPart, ISpellFxPreLaunch
    {
        /// <summary>Same names as the vine fan's verbs: 고 Sweep · 곡 Thicket · 곤 FanSpread · 곰 FanSeed · 곳 FanCut · 공 FanReturn. (The order is serialised by the setup: do not reorder.)</summary>
        public enum HexFanBite { Sweep, Thicket, FanSpread, FanSeed, FanCut, FanReturn }

        [SerializeField] Material material;
        [SerializeField] HexFanBite bite = HexFanBite.Sweep;
        [Tooltip("What the END burst is made of (cells breaking up; Wood = small plain ink only). The brush / throw / contact / release spits are always the small plain ink.")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Water;
        [Tooltip("A wall cell's horizontal corner radius in metres (0.275 = a 0.55 m cell, the dome's).")]
        [SerializeField] float cellRadius = 0.275f;
        [Tooltip("Nothing stands nearer than this (the caster's body hides 0 - 2 m from the shoulder camera).")]
        [SerializeField] float startDistance = 1.3f;
        [Tooltip("Used when the cast carries no spec.")]
        [SerializeField] float defaultDepth = 5f;
        [SerializeField] float minHalfAngle = 0.78f;
        [Tooltip("The wall's lowest cell edge above the ground (m); two rows reach about 1.5 m.")]
        [SerializeField] float wallBottom = 0.40f;
        [Tooltip("The first cells fly from the brush tip to the wall's start; the wall stands when they land (s).")]
        [SerializeField] float leadSeconds = 0.12f;
        [Tooltip("The sweep from 1.3 m to the depth (s); the blade of 곳 is faster.")]
        [SerializeField] float sweepSeconds = 0.43f, cutSeconds = 0.30f;
        [Tooltip("공: the cell the wall is called into - metres ahead of the caster, to the right, and its height above the ground (chest, so the caster's body does not hide it).")]
        [SerializeField] float homeDistance = 1.2f, homeSide = 0.5f, homeHeight = 1.1f;
        [Tooltip("The hit-stop: the part's own visual clock stands still this long when a body is passed (2 frames at 60 fps).")]
        [SerializeField] float hitStopSeconds = 2f / 60f;
        [Tooltip("The ground wash is a dark glass blot lit as if |n.v| = this: the dome shader's fill is 0.8 x glassK x sil, sil falls to ~0.3 as |n.v| rises - 0.8 gives about 0.15 a layer (TEST).")]
        [SerializeField] float washCos = 0.80f;
        [Tooltip("Sample the ground under every cell (a ray, once per 0.2 m lattice point).")]
        [SerializeField] bool followGround = true;

        const float Sqrt3 = SpellFxHexKit.Sqrt3;
        static readonly int[] PatChoices = SpellFxHexKit.PatChoices;

        static float Smooth(float x) => SpellFxHexKit.Smooth(x);

        // ------------------------------------------------------------ state

        /// <summary>One cell of the sweeping wall. S = its arc coordinate from the axis (m): at radius r its bearing is S / r, so the patch keeps its cell size and gains columns as it widens.</summary>
        sealed class WallCell
        {
            public int Col, Row;
            public float S, Dy;                       // arc coordinate, centre height above the ground
            public float Hash, Phase;
            public int Pat;
            public float LeaveAt = float.MaxValue;
            public float DepT, DepDur, HomeD;         // 공: when it sets off for the home cell, how long it travels, how far from home it stood
            public Vector3 Rest;                      // 공: where it stands at the far end (plan)
        }

        /// <summary>A cell that stands or floats on its own: an egg (곰), a plate (곡), a road stone (곤), the home cell (공).</summary>
        sealed class Floater
        {
            public Vector3 Pos;                       // plan centre (y = ground + height)
            public Vector2 Dir;
            public float Rx, Rv, Lean;
            public float Born, Grow = 0.2f, LeaveAt = float.MaxValue, LeaveLen = 0.25f, UsedAt = -1f;
            public float Bob, Phase, Phase2, Pulse, RiseFrom, Size = 1f, Ground;
            public float Hash; public int Pat;
            public bool Face, Thread, Seed;
        }

        /// <summary>A blot of the faint ink wash on the ground (two overlapping dark-glass quads, shrinking away).</summary>
        sealed class Dab { public Vector2 P; public float G, Yaw, Len, Wid, T0, T1, Fade = 0.5f; }

        /// <summary>The body a hold sits on: its cells follow it when it moves (a push, D308-158 2).</summary>
        sealed class HoldGroup { public Transform T; public Vector3 W0, Delta; }

        readonly List<WallCell> _wall = new List<WallCell>();
        readonly List<Floater> _fl = new List<Floater>();
        readonly List<Dab> _dabs = new List<Dab>();
        readonly List<HexPlate> _plates = new List<HexPlate>();
        readonly Dictionary<HexPlate, HoldGroup> _groupOf = new Dictionary<HexPlate, HoldGroup>();
        readonly List<HoldGroup> _groups = new List<HoldGroup>();
        readonly List<HexFlight> _flights = new List<HexFlight>();
        readonly HexFlashQueue _flash = new HexFlashQueue();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly HexTipPreview _preview = new HexTipPreview();
        readonly List<float> _stops = new List<float>();

        HexSheet _sheet;
        System.Random _rng;
        bool _built, _launched, _ended;
        float _v;                                   // the part's visual age (the director's age since the launch, with the hit-stops stood still)
        float _vOff, _stopV, _stopEnd; bool _stopOn; int _stopsAdded;
        Vector3 _origin, _fwd, _right;
        float _depth, _reach, _half, _lead, _airBias, _sweepDur, _wallLeave0, _wallSpread, _wallLeaveLen = 0.2f, _leaveLen = 0.3f, _holdLeave, _fadeStart, _endAt, _tRet, _crackLeave;
        float _rx, _rv, _hCell;
        Vector3 _homePos; Floater _home;
        HexPlate _primaryFront; Vector2? _primaryFeet;
        Vector3 _camPlan; bool _haveCam;

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

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            if (!_launched || _ended || _sheet == null) return;
            float a = VisualAge(ageSeconds);
            _v = a;
            // both layers' clocks run 0.16 s ahead: the shader fades a layer in over its first 0.16 s (_RiseTime 0) and the cells grow on their own, so the tip cells that leave at 0 are seen at once
            _sheet.Main.SetClock(a + 0.16f, 0f);
            _sheet.Air.SetClock(a + _airBias + 0.16f, 0f);          // the air layer's clock runs on from the commit (a 한 번 preview was drawn on it before the launch)
            if (a > _endAt) { if (_sheet.Main.Go.activeSelf) _sheet.SetActive(false); return; }
            UpdateGroups();                                         // before the flashes: a flash is asked for the SAME centre the mesh will use this frame (the cell's flash cell is matched to within 5 cm)
            if (_home != null) UpdateHome(a);
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

        // ------------------------------------------------------------ the visual clock (hit-stop)

        /// <summary>The part's own time: the director's age minus the time spent standing still. A stop is scheduled at a visual time; when the visual age reaches it the age stands still for
        /// <see cref="hitStopSeconds"/> of real time (the flash that was just started holds on its first frame, the wall stops, the ink queue waits - particles already thrown keep flying). Nothing
        /// but this part's drawing reads it: the rule's clock is the director's and is not touched.</summary>
        float VisualAge(float real)
        {
            if (_stopOn)
            {
                if (real < _stopEnd) return _stopV;
                _stopOn = false; _vOff += hitStopSeconds;
            }
            float v = real - _vOff;
            for (int i = 0; i < _stops.Count; i++)
            {
                if (v < _stops[i]) continue;
                _stops.RemoveAt(i);
                _stopOn = true; _stopV = v; _stopEnd = real + hitStopSeconds;
                return v;
            }
            return v;
        }

        void AddStop(float at)
        {
            if (_stopsAdded >= 3) return;
            foreach (var s in _stops) if (Mathf.Abs(s - at) < 0.15f) return;           // two contacts within 0.15 s are one hitch
            if (_stopOn && Mathf.Abs(_stopV - at) < 0.15f) return;
            _stopsAdded++; _stops.Add(at);
        }

        // ------------------------------------------------------------ helpers

        float Rand() => (float)_rng.NextDouble();
        float PickHash(float phase) => SpellFxHexKit.PickHash(_rng, phase);
        int PickPat() => PatChoices[_rng.Next(PatChoices.Length)];
        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;
        void Ext(float t) { if (t > _endAt) _endAt = t; }

        /// <summary>Ground height (relative to the cast origin) under a plan point (x, z), bilinear over the kit's cached 0.2 m lattice: no stair-steps on a slope.</summary>
        float Gnd(Vector2 p)
        {
            if (!followGround) return 0f;
            const float st = 0.2f;
            float fx = p.x / st, fz = p.y / st;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float g00 = _sheet.Ground.Q(new Vector2(ix * st, iz * st)), g10 = _sheet.Ground.Q(new Vector2((ix + 1) * st, iz * st));
            float g01 = _sheet.Ground.Q(new Vector2(ix * st, (iz + 1) * st)), g11 = _sheet.Ground.Q(new Vector2((ix + 1) * st, (iz + 1) * st));
            return Mathf.Lerp(Mathf.Lerp(g00, g10, tx), Mathf.Lerp(g01, g11, tx), tz);
        }

        static Vector2 Rot(Vector2 v, float ang)
        {
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The wall's radius at time <paramref name="a"/>: it leaves the start point fast and settles at the depth (ease-out).</summary>
        float RAt(float a)
        {
            float u = Mathf.Clamp01((a - _lead) / Mathf.Max(_sweepDur, 0.05f));
            u = 1f - (1f - u) * (1f - u);
            return Mathf.Lerp(startDistance, _depth, u);
        }

        /// <summary>When the wall's front reaches radius <paramref name="r"/> (the inverse of <see cref="RAt"/>).</summary>
        float TAt(float r)
        {
            float u = Mathf.Clamp01((r - startDistance) / Mathf.Max(0.1f, _depth - startDistance));
            return _lead + _sweepDur * (1f - Mathf.Sqrt(1f - u));
        }

        /// <summary>A body that lets go of the sheet: the body's own burst for the end of something (wood has none: a small plain ink instead).</summary>
        void EndInk(float at, Vector3 plan, Vector3 normal)
        {
            if (burstBody == SpellFxBurstBody.Wood) _ink.Spit(at, PlanW(plan), normal);
            else _ink.End(at, PlanW(plan), normal, burstBody);
        }

        // ------------------------------------------------------------ build

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            var spec = cast.Spec;
            _depth = spec != null && spec.FanDepth > 0.5f ? Mathf.Min(9f, spec.FanDepth) : defaultDepth;
            _half = Mathf.Clamp(Mathf.Max(minHalfAngle, spec != null ? spec.FanHalfAngleRad : 0f), 0.3f, 1.2f);
            _reach = bite == HexFanBite.Thicket ? _depth * 1.5f : _depth;
            _lead = leadSeconds;
            _airBias = cast.LaunchDelaySeconds;
            _rx = cellRadius; _rv = cellRadius * 0.95f; _hCell = Sqrt3 * _rv;
            _sweepDur = sweepSeconds;
            _wallSpread = 0.25f; _wallLeaveLen = 0.2f; _leaveLen = 0.3f;
            _endAt = 0.5f;
            switch (bite)
            {
                case HexFanBite.Thicket: _sweepDur = 0.38f; _wallLeave0 = _lead + _sweepDur; _holdLeave = 3.5f; _fadeStart = 3.5f; break;
                case HexFanBite.FanSpread: _wallLeave0 = _lead + _sweepDur; _holdLeave = 3.0f; break;
                case HexFanBite.FanCut: _sweepDur = cutSeconds; _wallLeave0 = _lead + _sweepDur + 0.08f; _holdLeave = 3.0f; _crackLeave = 3.0f; break;
                case HexFanBite.FanReturn: _tRet = _lead + _sweepDur + 0.3f; _holdLeave = 3.0f; break;
                case HexFanBite.FanSeed: _holdLeave = 3.0f; _fadeStart = 7.8f; break;
                default: _wallLeave0 = _lead + _sweepDur; _holdLeave = 2.0f; break;
            }

            // an UPRIGHT frame sunk 1 m (the shader's base-ring term is 0 and nothing is discarded) and _RiseTime 0: the cells' own growth is the reveal; the brush tip is sampled in the sheet's constructor
            _sheet = new HexSheet(transform, cast, material, HexFrame.UprightSunk(_origin, _right, _fwd, 1f), 0f, followGround, "HexFan");

            if (bite == HexFanBite.FanSeed) BuildSeeds();
            else
            {
                BuildWall();
                if (bite != HexFanBite.FanCut) BuildWash();
                MakeTipFlights();
                switch (bite)
                {
                    case HexFanBite.Thicket: BuildThicket(); PlanWallBreak(false); break;
                    case HexFanBite.FanReturn: BuildReturn(); break;
                    case HexFanBite.FanCut: PlanWallBreak(true); Ext(_crackLeave + 0.5f + 0.3f + 0.2f); break;
                    default: PlanWallBreak(true); break;
                }
            }
            foreach (var c in _wall) if (c.LeaveAt < float.MaxValue) Ext(c.LeaveAt + _wallLeaveLen + 0.2f);
            foreach (var f in _fl) if (f.LeaveAt < float.MaxValue) Ext(f.LeaveAt + f.LeaveLen + 0.2f);
            foreach (var d in _dabs) Ext(d.T1 + d.Fade + 0.1f);
            _built = true;
        }

        // ---- the wall (고 곡 곤 곳 공)

        /// <summary>The wall: the dome's honeycomb in two rows (columns 1.5 rx apart, every other column half a cell up, 0.4 - 1.5 m high); 곳: ONE row of cells, a blade. One cell per column slot over the
        /// widest the fan gets; a cell exists (is drawn) once the wedge at the wall's radius is wide enough for its arc coordinate.</summary>
        void BuildWall()
        {
            bool blade = bite == HexFanBite.FanCut;
            float pitch = blade ? 1.9f * _rx : 1.5f * _rx;
            float sMax = _depth * _half;
            int cols = Mathf.CeilToInt(sMax / pitch);
            for (int i = -cols; i <= cols; i++)
            {
                float s = i * pitch;
                if (Mathf.Abs(s) > sMax + 0.05f) continue;
                bool odd = (Mathf.Abs(i) & 1) == 1;
                int rows = blade ? 1 : 2;
                for (int k = 0; k < rows; k++)
                {
                    var c = new WallCell { Col = i, Row = k, S = s, Phase = Rand() * 6.28f, Pat = PickPat() };
                    c.Hash = PickHash(Rand());
                    c.Dy = blade ? 0.72f : wallBottom + (odd ? _hCell : 0.5f * _hCell) + k * _hCell;
                    if (bite != HexFanBite.FanReturn) c.LeaveAt = _wallLeave0 + (1f - Mathf.Abs(s) / Mathf.Max(sMax, 0.1f)) * _wallSpread + (k == 1 ? 0f : 0.03f) + Rand() * 0.05f;      // the outer cells first
                    _wall.Add(c);
                }
            }
        }

        Vector3 RestOf(WallCell c)
        {
            float phi = c.S / _depth;
            float x = _depth * Mathf.Sin(phi), z = _depth * Mathf.Cos(phi);
            return new Vector3(x, Gnd(new Vector2(x, z)) + c.Dy, z);
        }

        /// <summary>A wall cell's centre (plan) and the direction it faces at time <paramref name="a"/>. The same function the mesh is built from (a flash asks it every frame).</summary>
        Vector3 WallCtr(WallCell c, float a, out Vector2 dir)
        {
            float r = RAt(a);
            float phi = c.S / Mathf.Max(r, 0.3f);
            Vector2 radial = new Vector2(Mathf.Sin(phi), Mathf.Cos(phi));
            dir = radial;
            if (bite == HexFanBite.FanReturn && c.DepDur > 0f && a > c.DepT)
            {
                float u = Mathf.Clamp01((a - c.DepT) / Mathf.Max(c.DepDur, 0.05f)), e = u * u;            // ease-in: it starts slowly and snaps in
                Vector3 q = Vector3.Lerp(c.Rest, _homePos, e);
                q.y += 0.12f * Mathf.Sin(Mathf.PI * u);
                Vector2 toHome = new Vector2(_homePos.x - c.Rest.x, _homePos.z - c.Rest.z);
                if (toHome.sqrMagnitude > 1e-4f)
                {
                    // turn from facing outward to facing the way it flies - by angle: a straight lerp between two opposite directions passes through a zero vector (a zero normal = NaN, the white smear)
                    float turn = Mathf.LerpAngle(Mathf.Atan2(radial.x, radial.y) * Mathf.Rad2Deg, Mathf.Atan2(toHome.x, toHome.y) * Mathf.Rad2Deg, e) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Sin(turn), Mathf.Cos(turn));
                }
                return q;
            }
            var p = new Vector3(r * radial.x, 0f, r * radial.y);
            p.y = Gnd(new Vector2(p.x, p.z)) + c.Dy;
            return p;
        }

        float WallVis(WallCell c, float a)
        {
            float r = RAt(a);
            float v = Smooth((a - (_lead - 0.06f)) / 0.1f) * Smooth((r * _half - Mathf.Abs(c.S)) / 0.25f);      // the wall stands at the start; a column exists once the wedge is wide enough
            if (a > c.LeaveAt) v *= 1f - Smooth((a - c.LeaveAt) / _wallLeaveLen);
            if (bite == HexFanBite.FanReturn && c.DepDur > 0f && a > c.DepT)
            {
                float u = Mathf.Clamp01((a - c.DepT) / Mathf.Max(c.DepDur, 0.05f));
                v *=(1f - 0.4f * u * u) * (1f - Smooth((u - 0.88f) / 0.12f));                              // it shrinks on the way and is gone as it arrives
            }
            return v;
        }

        /// <summary>Two cells fly from the brush tip (sampled at the throw's first frame, a small ink there) to the wall's first two cells in 0.12 s and stand there; the wall grows under them.
        /// They stay standing facing the camera all the way (nothing tips flat).</summary>
        void MakeTipFlights()
        {
            WallCell c0 = null, c1 = null;
            bool blade = bite == HexFanBite.FanCut;
            foreach (var c in _wall)
            {
                if (c.Col == 0 && c.Row == 0) c0 = c;
                if (c.Col == 1 && c.Row == (blade ? 0 : 1)) c1 = c;
            }
            var pair = new[] { c0, c1 };
            for (int k = 0; k < 2; k++)
            {
                var c = pair[k];
                if (c == null) continue;
                float t0 = 0.03f * k, t1 = _lead + 0.03f * k;
                Vector2 d;
                Vector3 to = WallCtr(c, _lead, out d);
                _flights.Add(new HexFlight { From = _sheet.TipPlan, To = to, T0 = t0, T1 = t1, Rc = _rx * 0.78f, Arc = 0.4f, Hash = c.Hash, TipInk = k == 0, Standing = true });
            }
        }

        /// <summary>The wall's end: it breaks outer cells first (each cell shrinks over 0.2 s). The body's own burst on about six of them (three for wood: plain ink), a small plain ink on a few more.
        /// 곡: the wall only passes - small ink only (the plates have the end).</summary>
        void PlanWallBreak(bool bursts)
        {
            if (_wall.Count == 0) return;
            var order = new List<WallCell>(_wall);
            order.Sort((pa, pb) => pa.LeaveAt.CompareTo(pb.LeaveAt));
            bool wood = burstBody == SpellFxBurstBody.Wood;
            int ends = bursts ? (wood ? 2 : 6) : 0;
            Vector2 d;
            for (int k = 0; k < ends; k++)
            {
                var c = order[Mathf.Min(order.Count - 1, (int)((k + 0.5f) * order.Count / ends))];
                EndInk(c.LeaveAt, WallCtr(c, c.LeaveAt, out d), Vector3.up);
            }
            int spits = Mathf.Min(wood ? 1 : 3, order.Count);
            for (int k = 0; k < spits; k++)
            {
                var c = order[(k * 7 + 2) % order.Count];
                _ink.Spit(c.LeaveAt + 0.03f, PlanW(WallCtr(c, c.LeaveAt, out d)), Vector3.up);
            }
        }

        // ---- the faint ink wash on the ground (not a plate: a few dark glass blots where the wall has passed, shrinking away in 1.5 s)

        void BuildWash()
        {
            for (float r = startDistance + 0.25f; r <= _depth; r += 0.55f)
            {
                float t = TAt(r), span = r * _half * 0.95f;
                int n = Mathf.Max(1, Mathf.RoundToInt(2f * span / 1.25f));
                for (int j = 0; j < n; j++)
                {
                    float s = ((j + 0.5f) / n * 2f - 1f) * span + (Rand() - 0.5f) * 0.5f;
                    float phi = s / r;
                    var p = new Vector2(r * Mathf.Sin(phi), r * Mathf.Cos(phi));
                    _dabs.Add(new Dab { P = p, G = Gnd(p), Yaw = Rand() * Mathf.PI, Len = 0.30f + 0.20f * Rand(), Wid = 0.16f + 0.10f * Rand(), T0 = t, T1 = t + 1.0f, Fade = 0.5f });
                }
            }
        }

        // ---- 곡

        void PickSpots(int want, float r0, float r1, List<Vector2> into, List<Vector2> others, float gap)
        {
            int made = 0;
            for (int pass = 0; pass < 2 && made < want; pass++)
            {
                float g = pass == 0 ? gap : gap * 0.6f;
                for (int tries = 0; tries < 700 && made < want; tries++)
                {
                    float r = Mathf.Sqrt(Mathf.Lerp(r0 * r0, r1 * r1, Rand()));
                    float phi = (Rand() * 2f - 1f) * _half * 0.92f;
                    var p = new Vector2(r * Mathf.Sin(phi), r * Mathf.Cos(phi));
                    bool ok = true;
                    foreach (var o in into) if ((o - p).sqrMagnitude < g * g) { ok = false; break; }
                    if (ok && others != null) foreach (var o in others) if ((o - p).sqrMagnitude < g * g) { ok = false; break; }
                    if (!ok) continue;
                    into.Add(p); made++;
                }
            }
        }

        /// <summary>곡: the floating forest. Big plates (a cell x 2 wide; 1.0 / 1.4 / 1.8 m high) 0.3 - 0.5 m off the ground, near ones first: ten over the depth by 2.2 s, five more beyond it (the fan x1.5) by
        /// 3.0 s. Each faces the caster +- 15 deg (not all square to the camera: no billboards), rises 0.3 m into place as it grows, has a small ink at its foot when it stands and a faint wash under it,
        /// and they break far side first from 3.5 s (the body's burst on eight of them).</summary>
        void BuildThicket()
        {
            var near = new List<Vector2>(); var far = new List<Vector2>();
            PickSpots(10, 2.4f, _depth, near, null, 1.4f);
            PickSpots(5, _depth + 0.15f, _reach, far, near, 1.4f);
            near.Sort((pa, pb) => pa.sqrMagnitude.CompareTo(pb.sqrMagnitude)); far.Sort((pa, pb) => pa.sqrMagnitude.CompareTo(pb.sqrMagnitude));
            var all = new List<Vector2>(near); all.AddRange(far);
            float[] tiers = { 1.0f, 1.4f, 1.8f };
            int off = _rng.Next(3), n1 = near.Count;
            var made = new List<Floater>();
            for (int k = 0; k < all.Count; k++)
            {
                Vector2 p = all[k];
                float dist = p.magnitude, h = tiers[(k + off) % 3], g = Gnd(p);
                Vector2 toC = dist > 1e-3f ? -p / dist : -Vector2.up;
                float born = k < n1 ? 0.6f + 0.18f * k : 2.4f + 0.15f * (k - n1);
                var f = new Floater
                {
                    Pos = new Vector3(p.x, g + 0.3f + 0.2f * Rand() + 0.5f * h, p.y), Dir = Rot(toC, (Rand() * 2f - 1f) * 15f * Mathf.Deg2Rad),
                    Rx = 2f * _rx, Rv = h / (2f * 0.8660254f), Born = born, Grow = 0.45f, RiseFrom = -0.3f, Ground = g,
                    LeaveAt = _fadeStart + (1f - Mathf.Clamp01(dist / _reach)) * 0.5f + Rand() * 0.08f, LeaveLen = 0.25f, Hash = PickHash(Rand()), Pat = PickPat(), Phase = Rand() * 6.28f,
                };
                _fl.Add(f); made.Add(f);
                _ink.Spit(born + 0.1f, PlanW(new Vector3(p.x, g + 0.05f, p.y)), Vector3.up);
                _dabs.Add(new Dab { P = p, G = g, Yaw = Rand() * Mathf.PI, Len = 0.62f, Wid = 0.40f, T0 = born, T1 = f.LeaveAt, Fade = 0.4f });
            }
            made.Sort((pa, pb) => pa.LeaveAt.CompareTo(pb.LeaveAt));
            int ends = Mathf.Min(burstBody == SpellFxBurstBody.Wood ? 3 : 8, made.Count);
            for (int k = 0; k < ends; k++)
            {
                var f = made[Mathf.Min(made.Count - 1, (int)((k + 0.5f) * made.Count / ends))];
                EndInk(f.LeaveAt, f.Pos, Vector3.up);
            }
        }

        // ---- 곰

        /// <summary>곰: 10 - 14 egg cells at knee - waist height (0.4 - 0.9 m) scattered over the wedge, thrown from the brush tip one after another (from 0.3 s, 0.17 s apart, nearest first, 0.11 s in the
        /// air), each on a thin ink thread, bobbing +- 3 cm. An egg nobody used bursts one after another from 7.8 s (the body's burst on every other one).</summary>
        void BuildSeeds()
        {
            int want = 10 + _rng.Next(5);
            var pts = new List<Vector2>();
            for (int tries = 0; tries < 800 && pts.Count < want; tries++)
            {
                float r = startDistance + 0.3f + Rand() * Mathf.Max(0.5f, _depth - startDistance - 0.3f);
                float phi = (Rand() * 2f - 1f) * _half * 0.88f;
                var p = new Vector2(r * Mathf.Sin(phi), r * Mathf.Cos(phi));
                bool ok = true;
                foreach (var o in pts) if ((o - p).sqrMagnitude < 1.1f * 1.1f) { ok = false; break; }
                if (ok) pts.Add(p);
            }
            pts.Sort((pa, pb) => pa.sqrMagnitude.CompareTo(pb.sqrMagnitude));
            int n = pts.Count;
            for (int k = 0; k < n; k++)
            {
                Vector2 p = pts[k];
                float g = Gnd(p), born = 0.3f + 0.17f * k;
                var f = new Floater
                {
                    Pos = new Vector3(p.x, g + 0.4f + 0.5f * Rand(), p.y), Rx = 0.26f, Rv = 0.26f, Lean = (6f + 8f * Rand()) * Mathf.Deg2Rad, Born = born - 0.04f, Grow = 0.12f,
                    LeaveAt = _fadeStart + (n - 1 - k) * 0.05f, LeaveLen = 0.25f, Bob = 0.03f, Phase = Rand() * 6.28f, Pulse = 0.06f, Face = true, Thread = true, Seed = true, Ground = g,
                    Hash = PickHash(Rand()), Pat = PickPat(),
                };
                f.Phase2 = (Rand() - 0.5f) * 1.2f;          // the eggs pulse nearly together: a pulse out of step cancels over the field and reads as nothing (r2: +- 3 %)
                _fl.Add(f);
                _flights.Add(new HexFlight { From = _sheet.TipPlan, To = f.Pos, T0 = born - 0.11f, T1 = born, Rc = f.Rx * 0.9f, Arc = 0.5f + 0.06f * p.magnitude, Hash = f.Hash, TipInk = k % 2 == 0, Quick = true, Standing = true });
                if (k % 3 == 0) _ink.Spit(born, PlanW(new Vector3(p.x, g + 0.05f, p.y)), Vector3.up);
                if (k % 2 == 0) EndInk(f.LeaveAt, f.Pos, Vector3.up); else _ink.Spit(f.LeaveAt, PlanW(f.Pos), Vector3.up);
                _dabs.Add(new Dab { P = p, G = g, Yaw = Rand() * Mathf.PI, Len = 0.26f, Wid = 0.17f, T0 = born, T1 = f.LeaveAt, Fade = 0.3f });
            }
        }

        // ---- 공

        /// <summary>공: the wall stands at the far end 0.3 s, then every cell sets off for ONE cell near the caster (to the right, chest high so the caster's body does not hide it): far cells first, ease-in,
        /// 0.5 s each over 0.3 s of start times - the whole return is 0.8 s (0.85 - 1.65). The home cell grows as they arrive; a small ink when the first lands, a flash + ink + hit-stop when the last does.</summary>
        void BuildReturn()
        {
            var hxz = new Vector2(homeSide, homeDistance);
            _homePos = new Vector3(hxz.x, Gnd(hxz) + homeHeight, hxz.y);
            float dmax = 0.1f;
            foreach (var c in _wall)
            {
                c.Rest = RestOf(c);
                c.HomeD = new Vector2(c.Rest.x - _homePos.x, c.Rest.z - _homePos.z).magnitude;
                dmax = Mathf.Max(dmax, c.HomeD);
            }
            float first = float.MaxValue, last = 0f;
            foreach (var c in _wall)
            {
                c.DepT = _tRet + (1f - c.HomeD / dmax) * 0.3f; c.DepDur = 0.5f;
                first = Mathf.Min(first, c.DepT + c.DepDur); last = Mathf.Max(last, c.DepT + c.DepDur);
            }
            if (_wall.Count == 0) return;
            _home = new Floater
            {
                Pos = _homePos, Dir = Vector2.up, Rx = 0.3f, Rv = 0.3f, Born = first - 0.12f, Grow = 0.15f, LeaveAt = last + 0.35f, LeaveLen = 0.3f, Face = true, Pulse = 0.06f,
                Hash = PickHash(Rand()), Pat = PickPat(), Ground = _homePos.y - homeHeight,
            };
            var home = _home;
            _fl.Add(home);
            _ink.Spit(first, PlanW(_homePos), Vector3.up);
            _ink.Spit(last, PlanW(_homePos), Vector3.up);
            _flash.Schedule(() => FloCtr(home, _v), last);
            AddStop(last);
            EndInk(home.LeaveAt, _homePos, Vector3.up);
        }

        void UpdateHome(float a)
        {
            int arrived = 0;
            foreach (var c in _wall) if (a >= c.DepT + c.DepDur) arrived++;
            _home.Size = 1f + 0.8f * arrived / Mathf.Max(1, _wall.Count);
        }

        /// <summary>When the cells that carry the wall past a body at plan point <paramref name="feet"/> pass it on the way home: the wall cell of its bearing, at the point of its path nearest the body.</summary>
        float PassTime(Vector2 feet)
        {
            float sb = Mathf.Atan2(feet.x, Mathf.Max(0.01f, feet.y)) * _depth;
            WallCell best = null; float bd = float.MaxValue;
            foreach (var c in _wall) { float d = Mathf.Abs(c.S - sb); if (d < bd) { bd = d; best = c; } }
            if (best == null) return _tRet + 0.3f;
            float bu = 0f, bdist = float.MaxValue;
            for (int i = 0; i <= 25; i++)
            {
                float u = i / 25f;
                Vector3 p = Vector3.Lerp(best.Rest, _homePos, u * u);
                float dd = (new Vector2(p.x, p.z) - feet).sqrMagnitude;
                if (dd < bdist) { bdist = dd; bu = u; }
            }
            return best.DepT + bu * best.DepDur;
        }

        // ------------------------------------------------------------ floaters

        float FloVis(Floater f, float a)
        {
            float v = Smooth((a - f.Born) / Mathf.Max(f.Grow, 0.05f));
            if (a > f.LeaveAt) v *= 1f - Smooth((a - f.LeaveAt) / f.LeaveLen);
            if (f.UsedAt >= 0f) v *= 1f - Smooth((a - f.UsedAt) / 0.15f);
            return v;
        }

        /// <summary>A floater's centre (plan) at time <paramref name="a"/>; also what its flash is drawn at.</summary>
        Vector3 FloCtr(Floater f, float a)
        {
            Vector3 p = f.Pos;
            if (f.RiseFrom != 0f) p.y += f.RiseFrom * (1f - Smooth((a - f.Born) / Mathf.Max(f.Grow, 0.05f)));
            if (f.Bob > 0f) p.y += f.Bob * Mathf.Sin(a * Mathf.PI * 2f * 0.5f + f.Phase);
            if (a > f.LeaveAt) p.y -= 0.08f * Smooth((a - f.LeaveAt) / f.LeaveLen);
            return p;
        }

        // ------------------------------------------------------------ hits

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (!_built || _ended) return;
            long key = hit.TargetId != 0 ? hit.TargetId : 1000000L + _seen.Count;
            if (!_seen.Add(key)) return;                                       // each body once
            Vector3 rel = hit.Feet - _origin;
            var feet = new Vector2(Vector3.Dot(rel, _right), Mathf.Max(0f, Vector3.Dot(rel, _fwd)));
            float dist = feet.magnitude, ang = Mathf.Atan2(feet.x, feet.y);
            bool inside = dist <= _reach + 0.4f && Mathf.Abs(ang) <= _half + 0.2f;
            if (bite == HexFanBite.FanSeed) { HitSeed(hit, feet); return; }
            if (bite == HexFanBite.FanSpread && (!inside || (hit.Kind != null && hit.Kind.Contains("Spread")))) { HitSpread(hit, feet); return; }
            if (!inside) return;
            HitWall(hit, feet, dist);
        }

        /// <summary>The wall met a body: 3 - 5 cells (2 for the blade) stay ON its real surface (the kit's hold: a ray per cell at 46 - 70 % of its height, never over the face), a flash and a small
        /// ink on the first, a 2-frame hit-stop on that frame. 곳: torn plates in the body's colour fly off it as well. 공: the cells that carry the wall home pass it again - a second flash and ink,
        /// and the hold lets go then.</summary>
        void HitWall(SpellFxHit hit, Vector2 feet, float dist)
        {
            if (!_primaryFeet.HasValue) _primaryFeet = feet;
            bool cut = bite == HexFanBite.FanCut;
            float face = Mathf.Clamp(dist - Mathf.Clamp(hit.TargetRadius, 0.3f, 0.9f), startDistance, _depth);      // the wall touches the body's near face, not its feet
            float arrive = Mathf.Max(_v, TAt(face) + (cut ? 0.08f : 0.05f));
            float leave = _holdLeave, pass = 0f;
            if (bite == HexFanBite.FanReturn) { pass = PassTime(feet); leave = pass + 0.2f; }
            var made = Hold(hit, feet, arrive, cut ? 2 : 4, leave);
            HexPlate front = made.Count > 0 ? made[0] : null;
            if (_primaryFront == null) _primaryFront = front;
            if (cut)
            {
                Vector3 outward = feet.sqrMagnitude > 1e-4f ? (_right * feet.x + _fwd * feet.y).normalized : _fwd;
                Vector3 nrm = (outward + Vector3.up * 0.5f).normalized;
                _ink.Plates(arrive, hit.Position, nrm, _sheet.Main.RimColor);                       // the body's own edge colour (_Rim): the torn plates
                _ink.Plates(arrive + 0.04f, hit.Position, (nrm + _right * 0.3f).normalized, _sheet.Main.RimColor);
                if (_rng.Next(2) == 0) _ink.Plates(arrive + 0.08f, hit.Position, (nrm - _right * 0.3f).normalized, _sheet.Main.RimColor);
            }
            if (bite == HexFanBite.FanReturn && front != null)
            {
                ScheduleFlash(front, pass);
                _ink.Spit(pass, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            }
        }

        /// <summary>The hold cells on a body: the kit puts them on its surface; they are drawn at their place plus the body's move since the hit. The first is flashed and spat at (and the clock stands
        /// still for 2 frames), and the hold lets go with a small ink. Returns them.</summary>
        List<HexPlate> Hold(SpellFxHit hit, Vector2 feet, float t0, int n, float leaveAt)
        {
            HexBody body = HexHolds.BodyOf(_sheet, hit, feet);
            int before = _plates.Count;
            HexHolds.Add(_sheet, _rng, _plates, body, t0, n, leaveAt, _reach);
            var made = _plates.GetRange(before, _plates.Count - before);
            if (made.Count == 0) return made;
            var grp = new HoldGroup { T = hit.Target, W0 = hit.Target != null ? hit.Target.position : Vector3.zero };
            _groups.Add(grp);
            foreach (var p in made) _groupOf[p] = grp;
            HexPlate front = made[0];
            float tt = front.Start + 0.05f;
            ScheduleFlash(front, tt);
            _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            AddStop(tt);
            _ink.Spit(leaveAt + 0.08f, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            Ext(leaveAt + 0.02f * n + _leaveLen + 0.25f);
            return made;
        }

        Vector3 GroupDelta(HexPlate p) => _groupOf.TryGetValue(p, out var g) ? g.Delta : Vector3.zero;

        void ScheduleFlash(HexPlate p, float at) { _flash.Schedule(() => p.Ctr + GroupDelta(p), at); }

        void UpdateGroups()
        {
            foreach (var g in _groups)
            {
                if (g.T == null) continue;                      // gone (or a synthetic body): the last offset stays
                Vector3 d = g.T.position - g.W0;
                g.Delta = new Vector3(Vector3.Dot(d, _right), d.y, Vector3.Dot(d, _fwd));
            }
        }

        /// <summary>곤: the spread. 4 - 6 cells hop from the first body's hold cell (or, for a body outside the wedge with no first body, from the wall's edge at that bearing) to the neighbour, 0.1 s apart,
        /// 0.25 s each, apex 0.8 m; all but the last stay floating at knee height as a road (3 s, a small ink where each lands); the last lands on the neighbour's surface: hold + flash + ink.</summary>
        void HitSpread(SpellFxHit hit, Vector2 feet)
        {
            float dist = feet.magnitude;
            Vector3 from; float t0;
            bool havePrimary = _primaryFront != null && _primaryFeet.HasValue && (_primaryFeet.Value - feet).magnitude > 0.6f;
            if (havePrimary) { from = _primaryFront.Ctr + GroupDelta(_primaryFront); t0 = Mathf.Max(_v, _primaryFront.Start + 0.25f); }
            else
            {
                float rr = Mathf.Clamp(dist, startDistance, _depth);
                float phi = Mathf.Clamp(Mathf.Atan2(feet.x, Mathf.Max(0.05f, feet.y)), -_half * 0.95f, _half * 0.95f);
                var q0 = new Vector2(Mathf.Sin(phi), Mathf.Cos(phi)) * rr;
                from = new Vector3(q0.x, Gnd(q0) + 1.0f, q0.y);
                t0 = Mathf.Max(_v, TAt(rr) + 0.1f);
            }
            var f0 = new Vector2(from.x, from.z);
            float len = (feet - f0).magnitude;
            int n = Mathf.Clamp(4 + _rng.Next(3), 2, Mathf.Max(2, Mathf.CeilToInt(len / 0.45f)));
            const float hop = 0.25f;
            for (int k = 0; k < n - 1; k++)
            {
                Vector2 q = Vector2.Lerp(f0, feet, (k + 1f) / n);
                float g = Gnd(q), tk0 = t0 + 0.1f * k, tk1 = tk0 + hop, hash = PickHash(Rand());
                var to = new Vector3(q.x, g + 0.4f + 0.04f * Rand(), q.y);
                _flights.Add(new HexFlight { From = from, To = to, T0 = tk0, T1 = tk1, Rc = 0.17f, Arc = 0.8f, Hash = hash, Sampled = true, Standing = true });
                var road = new Floater
                {
                    Pos = to, Dir = Vector2.up, Rx = 0.18f, Rv = 0.18f, Lean = 0.08f, Born = tk1 - 0.02f, Grow = 0.1f, LeaveAt = tk1 + 3.0f, LeaveLen = 0.25f, Bob = 0.015f, Phase = Rand() * 6.28f,
                    Face = true, Ground = g, Hash = hash, Pat = PickPat(),
                };
                _fl.Add(road);
                _ink.Spit(tk1, PlanW(new Vector3(q.x, g + 0.05f, q.y)), Vector3.up);
                if (k == n - 2) EndInk(road.LeaveAt, to, Vector3.up); else _ink.Spit(road.LeaveAt, PlanW(to), Vector3.up);
                _dabs.Add(new Dab { P = q, G = g, Yaw = Rand() * Mathf.PI, Len = 0.2f, Wid = 0.13f, T0 = tk1, T1 = road.LeaveAt, Fade = 0.3f });
                Ext(road.LeaveAt + road.LeaveLen + 0.2f);
            }
            float tl0 = t0 + 0.1f * (n - 1), tLand = tl0 + hop;
            var made = Hold(hit, feet, tLand - 0.05f, 3, tLand + 3.0f);
            if (made.Count > 0)
                _flights.Add(new HexFlight { From = from, To = made[0].Ctr + GroupDelta(made[0]), T0 = tl0, T1 = made[0].Start, Rc = 0.17f, Arc = 0.8f, Hash = made[0].Hash, Sampled = true, Standing = true });
        }

        /// <summary>곰: a body came in. The 3 nearest eggs fly to its surface and become its hold (a flash + small ink on the first); the eggs they were are gone.</summary>
        void HitSeed(SpellFxHit hit, Vector2 feet)
        {
            var near = new List<Floater>();
            foreach (var f in _fl) if (f.Seed && f.UsedAt < 0f) near.Add(f);
            near.Sort((pa, pb) => (new Vector2(pa.Pos.x, pa.Pos.z) - feet).sqrMagnitude.CompareTo((new Vector2(pb.Pos.x, pb.Pos.z) - feet).sqrMagnitude));
            if (near.Count > 3) near.RemoveRange(3, near.Count - 3);
            float t = _v;
            if (near.Count > 0) t = Mathf.Max(t, near[0].Born + 0.1f);
            var made = Hold(hit, feet, t, 3, t + 3.0f);
            for (int i = 0; i < near.Count; i++)
            {
                var s = near[i];
                s.UsedAt = t;
                if (i < made.Count)
                    _flights.Add(new HexFlight { From = FloCtr(s, t), To = made[i].Ctr + GroupDelta(made[i]), T0 = t, T1 = made[i].Start + 0.05f, Rc = s.Rx * 0.9f, Arc = 0.15f, Hash = s.Hash, Sampled = true, Quick = true, Standing = true });
            }
            if (near.Count > 0)
            {
                Vector3 c = FloCtr(near[0], t);
                _ink.Spit(t, PlanW(new Vector3(c.x, near[0].Ground + 0.05f, c.z)), Vector3.up);
            }
        }

        // ------------------------------------------------------------ mesh

        void Rebuild(float a)
        {
            _sheet.Begin();
            var cam = Camera.main;
            _haveCam = cam != null;
            if (_haveCam) _camPlan = _sheet.ToPlan(cam.transform.position);
            EmitWash(a);
            if (bite == HexFanBite.FanCut) EmitCrack(a);
            EmitWall(a);
            EmitFloaters(a);
            foreach (var p in _plates)
            {
                float g = HexHolds.PlateG(p, a, _leaveLen);
                if (g <= 0.02f) continue;
                _sheet.EmitStanding(p.Ctr + GroupDelta(p), p.Dir, p.Rx * g, p.Rv * g, 0f, p.Hash, p.Pat, p.D01);
            }
            foreach (var fl in _flights)
            {
                if (a < fl.T0 || a >= fl.T1) continue;
                if (!fl.Sampled) _sheet.SampleFlight(fl);
                _sheet.EmitFlight(fl, a);
            }
            _sheet.Flush();
        }

        void EmitWall(float a)
        {
            if (_wall.Count == 0 || a < _lead - 0.12f) return;
            bool blade = bite == HexFanBite.FanCut;
            float lean = blade ? 20f * Mathf.Deg2Rad : 0.06f, rvK = blade ? 0.8f : 1f;
            foreach (var c in _wall)
            {
                float vis = WallVis(c, a);
                if (vis <= 0.02f) continue;
                Vector2 dir;
                Vector3 ctr = WallCtr(c, a, out dir);
                if (a > c.LeaveAt) ctr.y -= 0.1f * Smooth((a - c.LeaveAt) / _wallLeaveLen);
                _sheet.EmitStanding(ctr, dir, _rx * vis, _rv * rvK * vis, lean, c.Hash, c.Pat, 0.7f);
            }
        }

        void EmitFloaters(float a)
        {
            foreach (var f in _fl)
            {
                float vis = FloVis(f, a);
                if (vis <= 0.02f) continue;
                Vector3 c = FloCtr(f, a);
                float s = vis * f.Size;
                if (f.Pulse > 0f) s *= 1f + f.Pulse * Mathf.Sin(a * Mathf.PI * 2f * 2f + f.Phase2);
                Vector2 dir = f.Face ? _sheet.ToCamPlan : f.Dir;
                _sheet.EmitStanding(c, dir, f.Rx * s, f.Rv * s, f.Lean, f.Hash, f.Pat, 0.7f);
                if (f.Thread) EmitThread(f, c, vis, a);
            }
        }

        /// <summary>The thin vertical ink thread an egg hangs on, from the ground to the egg's lower corner (plain dark glass, a hair wide, swaying a little), drawn to the camera's side (the ring's).</summary>
        void EmitThread(Floater f, Vector3 c, float vis, float a)
        {
            float y0 = f.Ground + 0.03f, y1 = c.y - f.Rv * 0.8660254f * vis;
            if (y1 - y0 < 0.05f) return;
            Vector2 across = new Vector2(_sheet.ToCamPlan.y, -_sheet.ToCamPlan.x);
            float hw = 0.0125f * Mathf.Clamp01(vis * 2f), sway = 0.012f * Mathf.Sin(a * 2.4f + f.Phase);
            Vector3 b0 = new Vector3(c.x + across.x * (sway - hw), y0, c.z + across.y * (sway - hw)), b1 = new Vector3(c.x + across.x * (sway + hw), y0, c.z + across.y * (sway + hw));
            Vector3 t0 = new Vector3(c.x + across.x * (0f - hw), y1, c.z + across.y * (0f - hw)), t1 = new Vector3(c.x + across.x * hw, y1, c.z + across.y * hw);
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            Vector3 front = new Vector3(_sheet.ToCamPlan.x, 0.15f, _sheet.ToCamPlan.y);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, _reach, true, front);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, _reach, true, front);
        }

        // ---- ground wash

        /// <summary>The shading normal that lights a ground quad as if |n.v| = <see cref="washCos"/>: the dome shader's fill is 0.8 x glassK x sil and sil falls toward 0.25 as |n.v| rises, so a ground
        /// blot drawn this way is a faint dark glass (about 0.15 a layer) instead of the dark sheet a grazing view would make of it. (Arithmetic from the shader text - TEST, not a capture.)</summary>
        Vector3 WashShade(Vector3 ctr)
        {
            Vector3 v = _haveCam ? _camPlan - ctr : new Vector3(_sheet.ToCamPlan.x * 3f, 2f, _sheet.ToCamPlan.y * 3f);
            if (v.sqrMagnitude < 1e-6f) return Vector3.up;
            v.Normalize();
            Vector3 perp = Vector3.up - v.y * v;
            if (perp.sqrMagnitude < 1e-6f) return Vector3.up;
            perp.Normalize();
            float c = Mathf.Clamp(washCos, 0.5f, 0.95f);
            return (c * v + Mathf.Sqrt(1f - c * c) * perp).normalized;
        }

        void DabQuad(Vector3 c, float yaw, float hl, float hw, Vector3 shLocal)
        {
            Vector3 u = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), w = new Vector3(-u.z, 0f, u.x);
            _sheet.Main.QuadVerts(c - u * hl - w * hw, c - u * hl + w * hw, c + u * hl - w * hw, c + u * hl + w * hw, shLocal, _reach, true);
        }

        void EmitWash(float a)
        {
            foreach (var d in _dabs)
            {
                float s = Smooth((a - d.T0) / 0.15f) * (1f - Smooth((a - d.T1) / d.Fade));
                if (s < 0.04f) continue;
                var ctr = new Vector3(d.P.x, d.G + 0.05f, d.P.y);
                Vector3 sh = _sheet.Main.Frame.NormalLocal(WashShade(ctr));
                DabQuad(ctr, d.Yaw, d.Len * s, d.Wid * s, sh);
                DabQuad(ctr, d.Yaw + 0.95f, d.Len * 0.7f * s, d.Wid * 0.75f * s, sh);
            }
        }

        // ---- 곳: the crack

        /// <summary>곳: the dark crack on the axis the blade leaves (plain dark glass, no seam, no pattern; the shader has no black, so it is drawn three times for body), 4 cm wide with a little wander,
        /// growing with the blade's front and lasting 3 s, then going far end first.</summary>
        void EmitCrack(float a)
        {
            if (a < _lead) return;
            float front = RAt(a);
            const int N = 44;
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            Vector3 p0 = default; float w0 = 0f; bool have = false;
            for (int i = 0; i <= N; i++)
            {
                float z = Mathf.Lerp(startDistance - 0.2f, _depth, i / (float)N);
                float zn = Mathf.Clamp01((z - startDistance) / Mathf.Max(0.5f, _depth - startDistance));
                float grow = z <= front ? Smooth((front - z) / 0.25f) : 0f;
                float leave = Smooth((a - (_crackLeave + (1f - zn) * 0.5f)) / 0.3f);
                float w = 0.04f * (0.75f + 0.5f * Mathf.PerlinNoise(z * 7.1f, 0.37f)) * grow * (1f - leave);
                float x = 0.03f * Mathf.Sin(z * 5.3f) + 0.018f * Mathf.Sin(z * 12.7f + 1.3f);
                var pt = new Vector3(x, Gnd(new Vector2(x, z)) + 0.05f, z);
                if (have && (w0 > 0.004f || w > 0.004f))
                {
                    Vector3 hw0 = new Vector3(w0 * 0.5f, 0f, 0f), hw1 = new Vector3(w * 0.5f, 0f, 0f);          // plan x = across the crack
                    for (int r = 0; r < 3; r++) _sheet.Main.QuadVerts(p0 - hw0, p0 + hw0, pt - hw1, pt + hw1, sh, _reach, true);
                }
                p0 = pt; w0 = w; have = true;
            }
        }
    }
}
