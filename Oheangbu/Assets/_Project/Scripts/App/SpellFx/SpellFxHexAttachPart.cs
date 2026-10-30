using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 ㅓ 부착 in the hex-cell membrane grammar (PLAN-308 §2.3; lane C; the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"). The caster's own body wears the cells: the shaft of the brush,
    /// the sleeve, the flank of the shoulder - never the middle of the back (the 3rd-person camera stands behind the caster, 2.8 m: a cell there hides the caster and reads as a flat sign). No mark on the
    /// ground (the CSV: "세계에 자국 없음"). Cells are 0.10 - 0.18 m (circumradius), standing cells only (<see cref="HexSheet.EmitStanding"/>: the kit's per-cell shading normal turned to |n.v| 0.5 - 0.75, the
    /// dome shader thins a face-on cell to ~0.1), every cell drawn as two layers by the kit. One family with the ward (우 H) and the ㅗ fan / ㅜ ring: translucent glass, cream seam, a KCISA pattern in each
    /// cell, the body's one accent colour, a 2 Hz breath while it stands. Ink: the brush tip, a landing, a contact, a rise are always the SMALL plain ink (<see cref="SpellFxHexKit.Spit"/>); only the end
    /// (cells letting go) bursts as the body (wood: none). Look only - nothing here touches a rule.
    /// Seven verbs (times from the launch; the hold is the rule's own life, copied from the data - <see cref="holdSeconds"/> overrides the default):
    ///  거 Parry (0.5 s): 4 cells fly from the brush tip (0.09 s) and stand as a palm-sized two-row patch (hex cluster, r 0.10) at the brush hand's front right; at the hold (0.5) the accent flash (the shader's
    ///    hit slot on the centre cell, a second 0.1 s later, the seam pulsing to the accent colour for ~0.1 s, the cells popping +22 %), then they break away outward / up with a spit and the body's burst.
    ///  걱 Heal (3 s, 극치 - the apex mark): 10 cells, one every 0.3 s, left and right in turn, start at the feet and climb the side of the silhouette (0.9 s each) and melt into small ink at head height.
    ///    Nothing on the back. An <c>OnEnd(Done)</c> that arrives between 0.3 s and hold - 0.25 s is read as the interrupt (hit during the heal): no further cells, the climbing ones let go.
    ///  건 Cling (6 s): 2 cells on the brush shaft + 4 on the flanks of the shoulders / upper arms (the brush side first), placed in 0 - 0.36 s, flying from the tip. A contact: a shaft cell crosses to the body (the
    ///    kit's hold on its real surface); a contact of kind "Spread" (a neighbour reached from the first body): 3 cells hop from the first body's chest to knee height along the way and stay as a
    ///    road for 3 s, then the neighbour is held.
    ///  검 SelfSeed (8 s): 3 egg cells circle the waist slowly (r 0.5 m, 0.22 rev/s, a vertical ink thread under each); the arc of the camera side is thinned (see below). The first contact is the "I was hit" activation
    ///    (a guess - there is no other event): the eggs burst, a low ring wall (r = the spec's ring radius, else 2 m, one row, 0.3 s standing) rises round the caster and lets go, the contacting body is held 2 s.
    ///  것 Blade (6 s): 5 lozenge cells in a row along the brush shaft (growing toward the tip, tilted 25 deg about the shaft), grown from the hand to the tip (0.04 - 0.4 s), a dark crack line (4 cm) on the shaft;
    ///  when the tip moves fast, 2 cells of the shaft as it was 0.05 / 0.10 / 0.15 s ago (smaller each time) are the swing band. A contact: torn plates in the body's edge colour + 2 hold cells.
    ///  겅 Orbit (6 s): 8 cells circle the caster at r 1.1 m, waist - chest (0.5 rev/s), each turned toward the camera by half; the camera-side arc is thinned; a LabShot that crosses the orbit inward turns to ink
    ///  (<see cref="SpellFxLabShot.Dissolve"/>); a contact holds the body (2 cells).
    ///  넉 Residue (fire, 6 s): the 건 flank cells only (4, no brush cells, no road), the body's end: they let go one by one, upward, as embers and ash. A contact: 2 hold cells.
    /// Alpha substitute (the dome shader has no vertex alpha yet - lane E): the cells in the arc of the camera side (the 겅 orbit and the 검 eggs) are made SMALLER the nearer they are to the camera bearing
    /// (full size beyond 75 deg, nothing inside 25 deg: <see cref="ConeGate"/>). The body cells are placed 68 deg or more off the camera bearing to begin with.
    /// The anchor is the cast's fixed origin: the cast has no caster transform, so a walking caster leaves the body cells behind (the vine's SpawnSelf had the same limit). The brush cells follow the rig's
    /// real grip / tip when they are plausible.</summary>
    public sealed class SpellFxHexAttachPart : MonoBehaviour, ISpellFxPart
    {
        /// <summary>거 Parry · 걱 Heal · 건 Cling · 검 SelfSeed · 것 Blade · 겅 Orbit · (넉 and the other 일반 '남긴다') Residue.</summary>
        public enum HexAttachBite { Parry, Heal, Cling, SelfSeed, Blade, Orbit, Residue }

        [SerializeField] Material material;
        [SerializeField] HexAttachBite bite = HexAttachBite.Cling;
        [Tooltip("What the END of the cells is made of (cells letting go; Wood = none). The brush / landing / contact / rise spits are always the small plain ink.")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Water;
        [Tooltip("How long the cells stand (s) before they let go - the rule's own life: parry 0.5, heal 3, seed 8, the rest 6. 0 or less = that default. The setup command writes it from its table.")]
        [SerializeField] float holdSeconds = -1f;
        [Tooltip("A body cell's circumradius in metres, drawn between these (0.12 - 0.18).")]
        [SerializeField] float cellRadiusMin = 0.12f, cellRadiusMax = 0.18f;
        [Tooltip("극치 mark: every cell x1.25 and the body's main pattern (atlas slot 1).")]
        [SerializeField] bool apexMark;
        [Tooltip("The arc of the camera's side (degrees from its bearing): body cells stay out of it; the orbit and the eggs shrink to nothing in its inner part.")]
        [SerializeField] float cameraCone = 60f;
        [Tooltip("How long a cell takes to let go at the end (s).")]
        [SerializeField] float leaveSeconds = 0.3f;
        [Tooltip("겅: the orbit's radius (m) and speed (turns per second).")]
        [SerializeField] float orbitRadius = 1.1f, orbitTurnsPerSecond = 0.5f;
        [Tooltip("검: the eggs' orbit radius (m), speed (turns per second) and the ring wall's radius when the cast carries no spec.")]
        [SerializeField] float seedRadius = 0.5f, seedTurnsPerSecond = 0.22f, seedRingRadius = 2f;
        [Tooltip("걱: seconds between two cells, and seconds a cell takes to climb from the feet to the head.")]
        [SerializeField] float healSpacing = 0.3f, healRiseSeconds = 0.9f;
        [Tooltip("Sample the ground under a road cell / the ring wall (a ray, once).")]
        [SerializeField] bool followGround = true;

        // ------------------------------------------------------------ state

        /// <summary>Where a cell is at a moment: centre (plan: x right of the caster, y up from the cast origin, z ahead), the horizontal direction it faces, its lean (rad), an extra size factor.</summary>
        struct Place { public Vector3 Ctr; public Vector2 Dir; public float Lean, K; }

        sealed class Cell
        {
            public Func<float, Place> At;
            public float Rc, Hash, Phase; public int Pat;
            public float Born, Grow = 0.14f, LeaveAt = float.MaxValue, LeaveLen = 0.3f, UsedAt = -1f;
            public Vector3 Drift;               // how far it travels while it lets go (plan)
            public bool Blade, Seed;
        }

        struct BrushSample { public float T, Len; public Vector3 Hand, Dir; }

        readonly List<Cell> _cells = new List<Cell>();
        readonly List<Cell> _shaftCells = new List<Cell>(), _seeds = new List<Cell>();
        readonly List<HexPlate> _plates = new List<HexPlate>();
        readonly List<HexFlight> _flights = new List<HexFlight>();
        readonly List<BrushSample> _hist = new List<BrushSample>();
        readonly HexFlashQueue _flash = new HexFlashQueue();
        readonly HexInkQueue _ink = new HexInkQueue();
        readonly HashSet<long> _seen = new HashSet<long>();

        HexSheet _sheet;
        SpellFxCast _cast;
        System.Random _rng;
        bool _built, _launched, _ended, _seedEnded;
        float _age, _hold, _endAt, _pulseAt = -1f, _clockBias, _activatedAt = -1f, _brushAt = -1f;
        Vector3 _origin, _fwd, _right;
        Vector3 _hand, _shaftDir;
        float _shaftLen;
        Vector2? _primaryFeet;

        static readonly float[] GhostLag = { 0.05f, 0.10f, 0.15f }, GhostSize = { 0.6f, 0.42f, 0.26f };

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
            if (reason == SpellFxEndReason.Aborted)
            {
                _ended = true;
                if (_sheet != null) _sheet.SetActive(false);
                return;
            }
            if (bite == HexAttachBite.Heal && reason == SpellFxEndReason.Done && _built) Interrupt(cast);
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _ended || _sheet == null) return;
            float a = ageSeconds;
            _sheet.Main.SetClock(a + _clockBias, 0f); _sheet.Air.SetClock(a + _clockBias, 0f);
            if (a > _endAt) { if (_sheet.Main.Go.activeSelf) _sheet.SetActive(false); return; }
            UpdateBrush(a, false);
            SeedEnd(a);
            Shots(a);
            _ink.Drain(a);
            _flash.Drive(_sheet, a);
            _sheet.Main.Breathe(a); _sheet.Air.Breathe(a);
            Pulse(a);
            _sheet.Age = a;
            Rebuild(a);
        }

        void OnDestroy()
        {
            if (_sheet != null) _sheet.Destroy();
        }

        // ------------------------------------------------------------ small helpers

        float Rand() => (float)_rng.NextDouble();
        Vector3 PlanW(Vector3 p) => _origin + _right * p.x + Vector3.up * p.y + _fwd * p.z;
        float GroundAt(Vector2 p) => _sheet.Ground.At(p);
        static Vector2 Norm(Vector2 v) => v.sqrMagnitude < 1e-6f ? new Vector2(0f, 1f) : v.normalized;
        static float Smooth(float x) => SpellFxHexKit.Smooth(x);

        /// <summary>Plan x, z of the horizontal direction toward the camera.</summary>
        Vector2 Cam => _sheet.ToCamPlan;
        /// <summary>Square to <see cref="Cam"/>: the caster's right when the camera is behind him.</summary>
        Vector2 SideV { get { Vector2 c = _sheet.ToCamPlan; return new Vector2(-c.y, c.x); } }
        Vector3 CamDir3(float y) { Vector2 c = _sheet.ToCamPlan; return new Vector3(c.x, y, c.y).normalized; }

        static float DefaultHold(HexAttachBite b)
        {
            switch (b)
            {
                case HexAttachBite.Parry: return 0.5f;
                case HexAttachBite.Heal: return 3f;
                case HexAttachBite.SelfSeed: return 8f;
                default: return 6f;
            }
        }

        float Radius() => Mathf.Lerp(cellRadiusMin, cellRadiusMax, Rand()) * (apexMark ? 1.25f : 1f);

        /// <summary>How long a cell takes to let go (metal: 3 frames, the body's snap), and where it goes while it does (the body's end; wood: the roots reaching ahead).</summary>
        float EndLen => burstBody == SpellFxBurstBody.Metal ? 0.06f : leaveSeconds;

        Vector3 EndDrift(Vector2 outward)
        {
            var o = new Vector3(outward.x, 0f, outward.y);
            switch (burstBody)
            {
                case SpellFxBurstBody.Fire: return Vector3.up * 0.32f + o * 0.05f;            // embers and ash rise
                case SpellFxBurstBody.Earth: return Vector3.down * 0.12f;                       // it sinks
                case SpellFxBurstBody.Metal: return o * 0.3f;                                   // shards fly aside
                case SpellFxBurstBody.Water: return Vector3.down * 0.16f;                       // it runs down
                default: return Vector3.forward * 0.2f + o * 0.04f;                             // wood: the roots let go, reaching ahead
            }
        }

        /// <summary>A point on the caster's body ellipse (half widths <paramref name="rx"/> across, <paramref name="rz"/> front to back) at the angle <paramref name="psiDeg"/> from the camera's bearing
        /// (+ toward <see cref="SideV"/>); <paramref name="nrm"/> = the outward horizontal normal there.</summary>
        Vector2 BodyPoint(float psiDeg, float rx, float rz, out Vector2 nrm)
        {
            float s = Mathf.Sin(psiDeg * Mathf.Deg2Rad), c = Mathf.Cos(psiDeg * Mathf.Deg2Rad);
            Vector2 cam = Cam, side = SideV;
            nrm = Norm(cam * (c / rz) + side * (s / rx));
            return cam * (rz * c) + side * (rx * s);
        }

        /// <summary>0 inside 25 deg of the camera's bearing, 1 beyond 75 deg (for a 60 deg cone): the size factor that stands in for the missing vertex alpha.</summary>
        float ConeGate(float psiDeg)
        {
            float ang = Mathf.Abs(Mathf.DeltaAngle(0f, psiDeg));
            return Smooth((ang - cameraCone * 0.42f) / (cameraCone * 0.83f));
        }

        Cell NewCell(float rc)
        {
            var c = new Cell { Rc = rc, Phase = Rand() * 6.2832f, Hash = SpellFxHexKit.PickHash(_rng, Rand()) };
            c.Pat = apexMark ? 1 : SpellFxHexKit.PatChoices[_rng.Next(SpellFxHexKit.PatChoices.Length)];
            _cells.Add(c);
            return c;
        }

        /// <summary>The cell is thrown from the brush tip (sampled at the flight's first frame; a small spit there when <paramref name="tipInk"/>) and lands at its place at t0 + dur; the cell itself grows under it.</summary>
        void Fly(Cell c, float t0, float dur, bool tipInk)
        {
            float t1 = t0 + dur;
            _flights.Add(new HexFlight { From = _sheet.TipPlan, To = c.At(t1).Ctr, T0 = t0, T1 = t1, Rc = c.Rc * 0.9f, Arc = 0.18f, Hash = c.Hash, TipInk = tipInk, Quick = true, Standing = true });
            c.Born = Mathf.Max(0f, t1 - 0.05f);
        }

        // ------------------------------------------------------------ the brush

        /// <summary>The brush hand and shaft in plan coordinates: the rig's real grip when it is within 1.4 m of the caster and above the knees, else the vine's hand (right 0.32, up 1.25, ahead 0.25); the shaft
        /// is grip -> tip when the tip is 0.7 - 3.2 m from the hand (the rig's tip lies by the hand in some poses - the vine's r1), else a raised blade from the hand (up 0.9, ahead 0.45), 1.5 m long. Smoothed
        /// (24 /s) so a pose change does not jump a cell.</summary>
        void UpdateBrush(float a, bool snap)
        {
            Vector3 hand = new Vector3(0.32f, 1.25f, 0.25f), dir = new Vector3(0f, 0.894f, 0.447f);
            float len = 1.5f;
            try
            {
                Vector3? g = _cast != null && _cast.BrushGrip != null ? _cast.BrushGrip() : null;
                if (g.HasValue)
                {
                    Vector3 gp = _sheet.ToPlan(g.Value);
                    if (new Vector2(gp.x, gp.z).magnitude < 1.4f && gp.y > 0.6f && gp.y < 2.2f) hand = gp;
                }
                Vector3? t = _cast != null && _cast.BrushTip != null ? _cast.BrushTip() : null;
                if (t.HasValue)
                {
                    Vector3 tp = _sheet.ToPlan(t.Value);
                    Vector3 d = tp - hand; float l = d.magnitude;
                    if (l >= 0.7f && l <= 3.2f && tp.y < 3.4f) { dir = d / l; len = Mathf.Clamp(l, 0.9f, 2.2f); }
                }
            }
            catch (Exception) { /* a rig that is gone: the fallback stands */ }
            float dt = Mathf.Clamp(a - _brushAt, 0f, 0.1f);
            if (snap || _brushAt < 0f || dt <= 0f) { _hand = hand; _shaftDir = dir; _shaftLen = len; }
            else
            {
                float k = 1f - Mathf.Exp(-dt * 24f);
                _hand = Vector3.Lerp(_hand, hand, k); _shaftDir = Vector3.Slerp(_shaftDir, dir, k).normalized; _shaftLen = Mathf.Lerp(_shaftLen, len, k);
            }
            _brushAt = a;
            if (bite == HexAttachBite.Blade)
            {
                _hist.Add(new BrushSample { T = a, Hand = _hand, Dir = _shaftDir, Len = _shaftLen });
                while (_hist.Count > 0 && _hist[0].T < a - 0.4f) _hist.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------ build

        void Build(SpellFxCast cast)
        {
            if (_built || material == null || cast == null) return;
            if (_rng == null) _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            _cast = cast;
            _origin = cast.Origin;
            _fwd = cast.Forward; _fwd.y = 0f; if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.forward; _fwd.Normalize();
            _right = Vector3.Cross(Vector3.up, _fwd);
            _hold = holdSeconds > 0f ? holdSeconds : DefaultHold(bite);
            // an UPRIGHT frame sunk 1 m (cells stand all round the caster; the shader's base-ring term is 0 and nothing is discarded); the brush tip is sampled in the sheet's constructor
            _sheet = new HexSheet(transform, cast, material, HexFrame.UprightSunk(_origin, _right, _fwd, 1f), 0f, followGround, "HexAttach");
            UpdateBrush(0f, true);
            switch (bite)
            {
                case HexAttachBite.Parry: BuildParry(); break;
                case HexAttachBite.Heal: BuildHeal(); break;
                case HexAttachBite.Cling: BuildFlank(true); break;
                case HexAttachBite.SelfSeed: BuildSeeds(); break;
                case HexAttachBite.Blade: BuildBlade(); break;
                case HexAttachBite.Orbit: BuildOrbit(); break;
                default: BuildFlank(false); break;
            }
            PickEndInk();
            ComputeEnd();
            _built = true;
        }

        /// <summary>The part's own life: the last cell gone (plus its flights and holds) and a little.</summary>
        void ComputeEnd()
        {
            float e = 0f;
            foreach (var c in _cells) if (c.LeaveAt < 1e5f) e = Mathf.Max(e, c.LeaveAt + c.LeaveLen);
            foreach (var f in _flights) e = Mathf.Max(e, f.T1);
            _endAt = Mathf.Max(_endAt, e + 0.3f);
        }

        /// <summary>The end: up to four of the cells (spread over the order they let go) burst as the body (wood: none; the 검 eggs burst at the end of their own wait, see <see cref="SeedEnd"/>).</summary>
        void PickEndInk()
        {
            if (burstBody == SpellFxBurstBody.Wood || bite == HexAttachBite.SelfSeed) return;
            var order = new List<Cell>();
            foreach (var c in _cells) if (c.LeaveAt < 1e5f) order.Add(c);
            if (order.Count == 0) return;
            order.Sort((p, q) => p.LeaveAt.CompareTo(q.LeaveAt));
            int n = Mathf.Min(4, order.Count);
            for (int k = 0; k < n; k++)
            {
                var c = order[Mathf.Min(order.Count - 1, (int)((k + 0.5f) * order.Count / n))];
                _ink.End(c.LeaveAt, PlanW(PlaceOf(c, c.LeaveAt).Ctr), Vector3.up, burstBody);
            }
        }

        /// <summary>거: the four cells of a palm-sized patch (a hex cluster: one cell, three round it) at the brush hand's front right, thrown from the tip in 0.09 s. At the hold the accent flash (two cells, 0.07 s
        /// apart, as the queue allows), the seam pulses to the accent colour and the cells pop; then they break away outward and up with a spit and the body's burst.</summary>
        void BuildParry()
        {
            float rr = 0.10f * (apexMark ? 1.25f : 1f);
            Vector3 centre = _hand + new Vector3(0.14f, 0.04f, 0.12f);
            Vector2 dir = Norm(Cam * 0.7f + new Vector2(0.5f, 0f));         // back-right: toward the camera, turned to the brush side
            var u = new Vector3(dir.y, 0f, -dir.x);
            Vector2[] lattice = { new Vector2(0f, 0f), new Vector2(1.5f, 0.866f), new Vector2(1.5f, -0.866f), new Vector2(-1.5f, 0.866f) };
            for (int k = 0; k < lattice.Length; k++)
            {
                Vector3 ctr = centre + u * (lattice[k].x * rr) + Vector3.up * (lattice[k].y * rr);
                var c = NewCell(rr);
                c.At = _ => new Place { Ctr = ctr, Dir = dir, Lean = 0.05f, K = 1f };
                c.Grow = 0.06f;
                c.LeaveAt = _hold + 0.02f * k; c.LeaveLen = 0.17f;
                Vector3 off = ctr - centre; off.y = 0f;
                c.Drift = off.normalized * 0.22f + Vector3.up * 0.14f;
                Fly(c, 0.015f * k, 0.09f, k == 0);
            }
            Cell first = _cells[0], second = _cells[1];
            _flash.Schedule(() => PlaceOf(first, _age).Ctr, _hold);
            _flash.Schedule(() => PlaceOf(second, _age).Ctr, _hold + 0.1f);          // the queue keeps flashes 0.09 s apart: any closer is pushed to +0.1 anyway
            _pulseAt = _hold;
            _clockBias = 0.3f;              // the shader's own fade-in (0.16 s) and first bloom would eat most of a half-second glyph
            _ink.Spit(_hold, PlanW(centre), (Vector3.up + Vector3.right * 0.5f).normalized);
            _ink.Spit(_hold + 0.04f, PlanW(centre + new Vector3(0.06f, 0.1f, 0.04f)), (Vector3.up + Vector3.right).normalized);
        }

        /// <summary>The horizontal half width of the silhouette at a height (m): legs, then the shoulders, then the head.</summary>
        static float Lateral(float y)
        {
            if (y < 1.5f) return Mathf.Lerp(0.27f, 0.36f, Mathf.InverseLerp(0.95f, 1.3f, y));
            return Mathf.Lerp(0.36f, 0.22f, Mathf.InverseLerp(1.5f, 1.75f, y));
        }

        /// <summary>걱: a cell every <see cref="healSpacing"/> s, left and right in turn; each starts at the feet, climbs the side of the silhouette in <see cref="healRiseSeconds"/> and melts into small ink at head height.</summary>
        void BuildHeal()
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(_hold / healSpacing));
            float rise = Mathf.Max(0.3f, healRiseSeconds);
            _ink.Spit(0.03f, PlanW(_sheet.TipPlan), Vector3.up);
            for (int k = 0; k < n; k++)
            {
                float sg = (k & 1) == 0 ? 1f : -1f, born = healSpacing * k, riseK = rise, ph = Rand() * 6.28f;
                var c = NewCell(Radius());
                c.Born = born; c.Grow = 0.2f; c.LeaveAt = born + rise * 0.8f; c.LeaveLen = rise * 0.2f + 0.12f;
                c.Drift = Vector3.up * 0.1f;
                c.At = a =>
                {
                    float u = Mathf.Clamp01((a - born) / riseK);
                    float e = 1f - Mathf.Pow(1f - u, 1.6f);
                    float y = Mathf.Lerp(0.12f, 1.78f, e);
                    Vector2 xz = SideV * (sg * Lateral(y));
                    return new Place { Ctr = new Vector3(xz.x, y, xz.y), Dir = Norm(SideV * (sg * 0.55f) + Cam * 0.6f), Lean = 0.1f, K = 1f - 0.6f * Smooth((u - 0.75f) / 0.25f) };
                };
                if ((k & 1) == 1) _ink.Spit(c.LeaveAt, PlanW(c.At(c.LeaveAt).Ctr), Vector3.up);          // melting into ink at the head
            }
        }

        /// <summary>건 / 넉: cells on the flanks of the shoulders and upper arms (68 deg or more off the camera's bearing, the brush side first), thrown from the tip one after another in 0 - 0.36 s; 건 also has two
        /// cells on the brush shaft. They breathe and let go one by one at the hold, as the body's end.</summary>
        void BuildFlank(bool cling)
        {
            float t = 0f;
            if (cling)
            {
                var s0 = AddShaftCell(0.35f, 0.11f); Fly(s0, 0f, 0.10f, true);
                var s1 = AddShaftCell(0.80f, 0.10f); Fly(s1, 0.04f, 0.10f, false);
                _shaftCells.Add(s0); _shaftCells.Add(s1);
                s0.LeaveAt = _hold; s1.LeaveAt = _hold + 0.05f;
                s0.LeaveLen = s1.LeaveLen = EndLen;
                s0.Drift = s1.Drift = EndDrift(new Vector2(_shaftDir.x, _shaftDir.z));
                t = 0.06f;
            }
            float[,] spots = { { 72f, 1.42f }, { 98f, 1.27f }, { -76f, 1.40f }, { -100f, 1.25f } };
            float minAng = cameraCone + 8f;
            for (int k = 0; k < 4; k++)
            {
                float psi = spots[k, 0] + (Rand() - 0.5f) * 6f;
                psi = Mathf.Sign(psi) * Mathf.Max(Mathf.Abs(psi), minAng);
                float y = spots[k, 1] + (Rand() - 0.5f) * 0.05f;
                Vector2 p = BodyPoint(psi, 0.31f, 0.19f, out Vector2 nrm);
                Vector2 xz = p + nrm * 0.05f, face = Norm(nrm * 0.55f + Cam * 0.45f);
                var c = NewCell(Radius());
                var ctr = new Vector3(xz.x, y, xz.y);
                float lean = 0.12f;
                c.At = _ => new Place { Ctr = ctr, Dir = face, Lean = lean, K = 1f };
                c.LeaveAt = _hold + 0.05f * (k + (cling ? 2 : 0)); c.LeaveLen = EndLen;
                c.Drift = EndDrift(nrm);
                Fly(c, t + 0.06f * k, 0.12f, !cling && k == 0);
            }
        }

        Cell AddShaftCell(float dist, float rc)
        {
            var c = NewCell(rc * (apexMark ? 1.25f : 1f));
            c.At = a =>
            {
                Vector3 off = new Vector3(Cam.x, 0f, Cam.y) * 0.07f;          // a hand's breadth toward the camera: it sits on the near side of the shaft
                return new Place { Ctr = _hand + _shaftDir * dist + off, Dir = Cam, Lean = 0.1f, K = 1f };
            };
            return c;
        }

        /// <summary>The orbit / egg place: a bearing <paramref name="psi0Deg"/> off the camera's, turning at <paramref name="turnsPerSecond"/>; the cell faces half toward the camera. K carries the camera-arc thinning.</summary>
        Place OrbitPlace(float psi0Deg, float y, float radius, float turnsPerSecond, float a, float scale, float lean)
        {
            float psi = psi0Deg + a * turnsPerSecond * 360f, rad = psi * Mathf.Deg2Rad;
            Vector2 radial = Norm(Cam * Mathf.Cos(rad) + SideV * Mathf.Sin(rad)), xz = radial * radius;
            return new Place { Ctr = new Vector3(xz.x, y, xz.y), Dir = Norm(radial + Cam * 0.6f), Lean = lean, K = ConeGate(psi) * scale };
        }

        /// <summary>검: three egg cells circling the waist, thrown from the tip one after another; they wait to the end of the hold (a spit and the body's burst) unless a contact sets them off first.</summary>
        void BuildSeeds()
        {
            float[] psi0 = { 110f, 235f, 345f };
            for (int k = 0; k < psi0.Length; k++)
            {
                float p0 = psi0[k], y = 1.0f + (Rand() - 0.5f) * 0.1f, ph = Rand() * 6.28f;
                var c = NewCell(0.17f * (apexMark ? 1.25f : 1f));
                c.Seed = true;
                c.At = a => OrbitPlace(p0, y + 0.02f * Mathf.Sin(a * Mathf.PI + ph), seedRadius, seedTurnsPerSecond, a, 1f + 0.08f * Mathf.Sin(a * Mathf.PI * 2f * 0.8f + ph), 0.1f);
                c.LeaveAt = _hold - 0.15f; c.LeaveLen = 0.3f;
                Fly(c, 0.12f + 0.15f * k, 0.11f, (k & 1) == 0);
                _seeds.Add(c);
            }
        }

        /// <summary>것: five lozenge cells in a row along the brush shaft (long axis on the shaft, tilted 25 deg about it, the tip end biggest), grown from the hand to the tip; the tip lets go first.</summary>
        void BuildBlade()
        {
            float[] along = { 0.20f, 0.40f, 0.58f, 0.78f, 0.96f }, rLong = { 0.13f, 0.15f, 0.17f, 0.20f, 0.24f };
            _ink.Spit(0.04f, PlanW(_hand), Vector3.up);
            for (int k = 0; k < along.Length; k++)
            {
                float f = along[k];
                var c = NewCell(rLong[k] * (apexMark ? 1.25f : 1f));
                c.Blade = true;
                c.At = _ => new Place { Ctr = _hand + _shaftDir * (f * _shaftLen), Dir = Cam, Lean = 0f, K = 1f };
                c.Born = 0.04f + 0.07f * k; c.Grow = 0.2f;
                c.LeaveAt = _hold + 0.05f * (along.Length - 1 - k); c.LeaveLen = EndLen;
                c.Drift = EndDrift(new Vector2(_shaftDir.x, _shaftDir.z));
            }
        }

        /// <summary>겅: eight cells round the caster, alternately at waist and chest height, thrown from the tip one after another (0.05 s apart); they let go one by one at the hold.</summary>
        void BuildOrbit()
        {
            const int n = 8;
            for (int k = 0; k < n; k++)
            {
                float p0 = k * (360f / n), y = (k & 1) == 0 ? 0.92f : 1.2f, ph = Rand() * 6.28f;
                var c = NewCell(Radius());
                c.At = a => OrbitPlace(p0, y + 0.04f * Mathf.Sin(a * 2f + ph), orbitRadius, orbitTurnsPerSecond, a, 1f, 0.26f);
                c.LeaveAt = _hold + 0.05f * k; c.LeaveLen = EndLen;
                Fly(c, 0.05f * k, 0.13f, k == 0);
                Vector2 radial = Norm(Cam * Mathf.Cos((p0 + _hold * orbitTurnsPerSecond * 360f) * Mathf.Deg2Rad) + SideV * Mathf.Sin((p0 + _hold * orbitTurnsPerSecond * 360f) * Mathf.Deg2Rad));
                c.Drift = EndDrift(radial);
            }
        }

        // ------------------------------------------------------------ places

        Place PlaceOf(Cell c, float a)
        {
            Place p = c.At(a);
            if (a > c.LeaveAt && c.Drift.sqrMagnitude > 0f) p.Ctr += c.Drift * Smooth((a - c.LeaveAt) / Mathf.Max(c.LeaveLen, 0.02f));
            return p;
        }

        float VisOf(Cell c, float a)
        {
            float v = Smooth((a - c.Born) / Mathf.Max(c.Grow, 0.02f));
            if (a > c.LeaveAt) v *= 1f - Smooth((a - c.LeaveAt) / Mathf.Max(c.LeaveLen, 0.02f));
            if (c.UsedAt >= 0f) v *= 1f - Smooth((a - c.UsedAt) / 0.12f);
            return v;
        }

        /// <summary>Pop of the 거 patch at the flash: +22 % for a few frames.</summary>
        float Pop(float a) => _pulseAt >= 0f && a >= _pulseAt ? 1f + 0.22f * Mathf.Exp(-(a - _pulseAt) * 30f) : 1f;

        /// <summary>The seam of the whole patch goes to the accent colour at the flash and back in ~0.1 s (the material's _Line, over the breath).</summary>
        void Pulse(float a)
        {
            if (_pulseAt < 0f || a < _pulseAt) return;
            float f = Mathf.Exp(-(a - _pulseAt) * 26f);
            if (f < 0.03f) return;
            var m = _sheet.Main.Mat;
            Color acc = m.HasProperty("_Accent") ? m.GetColor("_Accent") : Color.white;
            m.SetColor("_Line", Color.Lerp(_sheet.Main.LineColor, new Color(acc.r * 1.6f, acc.g * 1.6f, acc.b * 1.6f, 1f), Mathf.Clamp01(f)));
        }

        // ------------------------------------------------------------ interrupts and the seed's end

        /// <summary>걱: the rule ended the heal early (a hit) - no further cells, the ones on their way let go. An end that comes at once (an executor that ends a cast on the spot) or at the hold is no interrupt.</summary>
        void Interrupt(SpellFxCast cast)
        {
            if (cast == null || cast.EndUs < 0) return;
            float endAge = cast.AgeAt(cast.EndUs);
            if (endAge < 0.3f || endAge > _hold - 0.25f) return;
            foreach (var c in _cells)
            {
                if (c.Born > endAge) c.Born = 1e6f;
                else if (c.LeaveAt > endAge + 0.3f) { c.LeaveAt = endAge; c.LeaveLen = 0.25f; }
            }
            ComputeEnd();
        }

        /// <summary>검: at the end of the wait with no contact the eggs let go as the body's burst with a small spit each.</summary>
        void SeedEnd(float a)
        {
            if (bite != HexAttachBite.SelfSeed || _seedEnded || a < _hold - 0.15f) return;
            _seedEnded = true;
            if (_activatedAt >= 0f) return;
            foreach (var e in _seeds)
            {
                Vector3 w = PlanW(PlaceOf(e, a).Ctr);
                _ink.Spit(a, w, Vector3.up);
                _ink.End(a, w, Vector3.up, burstBody);
            }
        }

        /// <summary>겅: a lab shot that crosses the orbit inward while the cells stand turns to ink there (the kit's <see cref="SpellFxLabShot.Dissolve"/>; the ward dome does the same), a spit, and the nearest
        /// orbit cell flashes. Look only: the shots are the lab's, no rule sees any of this.</summary>
        void Shots(float a)
        {
            if (bite != HexAttachBite.Orbit || a < 0.25f || a > _hold) return;
            for (int i = SpellFxLabShot.Active.Count - 1; i >= 0; i--)
            {
                var s = SpellFxLabShot.Active[i];
                if (s == null || !s.Flying) continue;
                Vector3 p = _sheet.ToPlan(s.Position), q = _sheet.ToPlan(s.PreviousPosition);
                float rp = new Vector2(p.x, p.z).magnitude, rq = new Vector2(q.x, q.z).magnitude;
                if (rq <= orbitRadius || rp > orbitRadius || p.y < -0.2f || p.y > 2.4f) continue;           // not crossing inward this frame
                float k = (rq - orbitRadius) / Mathf.Max(rq - rp, 1e-4f);
                Vector3 hitW = Vector3.Lerp(s.PreviousPosition, s.Position, k);
                Vector3 nrm = hitW - _origin; nrm.y = 0f; nrm = nrm.sqrMagnitude > 1e-6f ? nrm.normalized : _fwd;
                s.Dissolve(hitW, nrm, burstBody);
                _ink.Spit(a, hitW, nrm);
                Cell near = NearestCell(new Vector2(p.x, p.z), a);
                if (near != null) _flash.Schedule(() => PlaceOf(near, _age).Ctr, a + 0.02f);
            }
        }

        Cell NearestCell(Vector2 plan, float a)
        {
            Cell best = null; float bd = float.MaxValue;
            foreach (var c in _cells)
            {
                if (c.Seed || VisOf(c, a) < 0.3f) continue;
                Place pl = PlaceOf(c, a);
                if (pl.K < 0.3f) continue;
                float d = (new Vector2(pl.Ctr.x, pl.Ctr.z) - plan).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------ hits

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (!_built || _ended) return;
            if (bite == HexAttachBite.Parry || bite == HexAttachBite.Heal) return;            // the parry is timed by the rule's 0.5 s; the heal answers nobody
            long key = hit.TargetId != 0 ? hit.TargetId : 1000000L + _seen.Count;
            if (!_seen.Add(key)) return;
            float arrive = Mathf.Max(_age, 0.15f);
            if (arrive > _hold) return;
            Vector3 rel = hit.Feet - _origin;
            var feet = new Vector2(Vector3.Dot(rel, _right), Vector3.Dot(rel, _fwd));
            switch (bite)
            {
                case HexAttachBite.Cling: HitCling(hit, feet, arrive); break;
                case HexAttachBite.SelfSeed: HitSeed(hit, feet, arrive); break;
                case HexAttachBite.Blade: HitBlade(hit, feet, arrive); break;
                case HexAttachBite.Orbit: HitOrbit(hit, feet, arrive); break;
                default: Hold(hit, feet, arrive, 2, arrive + 2.0f); break;                  // 넉: the fire warms the body it touches
            }
            ComputeEnd();
        }

        /// <summary>The hold cells on a body (the kit: on its real surface, a ray per cell), the first one flashed and spat at; returns them.</summary>
        List<HexPlate> Hold(SpellFxHit hit, Vector2 feet, float t0, int n, float leaveAt)
        {
            HexBody body = HexHolds.BodyOf(_sheet, hit, feet);
            int before = _plates.Count;
            HexPlate front = HexHolds.Add(_sheet, _rng, _plates, body, t0, n, leaveAt, 3f);
            if (front != null)
            {
                float tt = front.Start + 0.05f;
                _flash.Schedule(() => front.Ctr, tt);
                _ink.Spit(tt, HexHolds.ContactPoint(_sheet, front), HexHolds.ContactNormal(_sheet, front));
            }
            _endAt = Mathf.Max(_endAt, leaveAt + 0.6f);
            return _plates.GetRange(before, _plates.Count - before);
        }

        /// <summary>건: the first body touched - a shaft cell crosses to its surface and becomes one of its hold cells. A contact of kind "Spread" (a neighbour reached from the first body): 3 cells hop from the first
        /// body's chest toward the neighbour (0.1 s apart, 0.25 s each, apex 0.8 m) and stay at knee height as a road for 3 s; the neighbour is held.</summary>
        void HitCling(SpellFxHit hit, Vector2 feet, float arrive)
        {
            bool neighbour = hit.Kind != null && hit.Kind.Contains("Spread") && _primaryFeet.HasValue;
            if (_primaryFeet == null) _primaryFeet = feet;
            if (!neighbour)
            {
                var plates = Hold(hit, feet, arrive, 3, arrive + 2.0f);
                Cell src = null;
                foreach (var s in _shaftCells) if (s.UsedAt < 0f) { src = s; break; }
                if (src != null && plates.Count > 0)
                {
                    src.UsedAt = arrive;
                    _flights.Add(new HexFlight { From = PlaceOf(src, arrive).Ctr, To = plates[0].Ctr, T0 = arrive, T1 = plates[0].Start + 0.05f, Rc = src.Rc * 0.9f, Arc = 0.15f, Hash = src.Hash, Sampled = true, Quick = true, Standing = true });
                }
                return;
            }
            Vector2 from = _primaryFeet.Value, d = feet - from;
            float len = d.magnitude;
            Vector2 dn = len > 1e-3f ? d / len : new Vector2(0f, 1f);
            const int n = 3;
            float tLast = arrive;
            Vector3 start = new Vector3(from.x, GroundAt(from) + 0.9f, from.y);
            for (int k = 0; k < n; k++)
            {
                Vector2 target = from + dn * (len * (k + 1) / (n + 1));
                float ht0 = arrive + 0.1f * k, ht1 = ht0 + 0.25f, g = GroundAt(target), ph = Rand() * 6.28f;
                Vector3 land = new Vector3(target.x, g + 0.4f, target.y);
                var c = NewCell(0.16f);
                c.At = a =>
                {
                    if (a >= ht1) return new Place { Ctr = land + Vector3.up * (0.025f * Mathf.Sin(a * Mathf.PI * 2f * 0.7f + ph)), Dir = Cam, Lean = 0f, K = 1f };
                    float u = Mathf.Clamp01((a - ht0) / (ht1 - ht0));
                    return new Place { Ctr = Vector3.Lerp(start, land, Smooth(u)) + Vector3.up * (0.8f * Mathf.Sin(Mathf.PI * u)), Dir = Cam, Lean = 0f, K = 1f };
                };
                c.Born = ht0; c.Grow = 0.08f;
                c.LeaveAt = ht1 + 3.0f; c.LeaveLen = 0.25f;
                _ink.Spit(ht1, PlanW(new Vector3(target.x, g + 0.05f, target.y)), Vector3.up);
                tLast = ht1;
                _endAt = Mathf.Max(_endAt, c.LeaveAt + 0.6f);
            }
            Hold(hit, feet, tLast, 3, tLast + 2.0f);
        }

        /// <summary>검: the first contact is the activation (the only event there is for "I was hit"): the eggs burst, a low ring wall rises round the caster (one row, 0.3 s standing) and lets go, the body that
        /// touched is held for 2 s. Later contacts are only held.</summary>
        void HitSeed(SpellFxHit hit, Vector2 feet, float arrive)
        {
            if (_activatedAt >= 0f) { Hold(hit, feet, arrive, 3, arrive + 2.0f); return; }
            _activatedAt = arrive;
            foreach (var e in _seeds)
            {
                _ink.Spit(arrive, PlanW(PlaceOf(e, arrive).Ctr), Vector3.up);
                e.LeaveAt = arrive; e.LeaveLen = 0.08f;
            }
            BuildWall(arrive + 0.04f);
            Hold(hit, feet, arrive + 0.1f, 3, arrive + 2.2f);
        }

        /// <summary>검's wall: a single row of standing cells (r 0.22 - 0.25, 0.4 m +- 0.1, leaning out 10 - 15 deg, a gap between) at the spec's ring radius (else <see cref="seedRingRadius"/>), the camera's 50 deg arc left
        /// out, rising out of the ground from the far side round (0.12 s), standing 0.3 s, then letting go; a spit at some feet and the body's burst on a few.</summary>
        void BuildWall(float t0)
        {
            float R = seedRingRadius;
            if (_cast != null && _cast.Spec != null && _cast.Spec.RingRadius > 1f && _cast.Spec.RingRadius < 4f) R = _cast.Spec.RingRadius;
            const float rcMin = 0.22f, rcMax = 0.25f;
            int n = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * R / (1.25f * (rcMin + rcMax))));
            Vector2 away = -Cam;
            float a0 = Mathf.Atan2(away.x, away.y), camAng = a0 + Mathf.PI, dphi = 2f * Mathf.PI / n;
            float thetaMax = Mathf.Max(1f, Mathf.PI - 50f * Mathf.Deg2Rad);
            int made = 0;
            for (int k = 0; k < n; k++)
            {
                float phi = a0 + (k - (n - 1) * 0.5f) * dphi;
                if (Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, camAng * Mathf.Rad2Deg)) < 50f) continue;
                var o = new Vector2(Mathf.Sin(phi), Mathf.Cos(phi));
                Vector2 pos = o * R;
                float theta = Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, a0 * Mathf.Rad2Deg)) * Mathf.Deg2Rad, thetaN = Mathf.Clamp01(theta / thetaMax);
                float g = GroundAt(pos), yy = 0.4f + (Rand() * 2f - 1f) * 0.1f, lean = Mathf.Lerp(10f, 15f, Rand()) * Mathf.Deg2Rad;
                var c = NewCell(Mathf.Lerp(rcMin, rcMax, Rand()));
                float born = t0 + 0.12f * thetaN;
                c.Born = born; c.Grow = 0.14f;
                c.At = a =>
                {
                    float rise = Smooth((a - born) / 0.14f);
                    return new Place { Ctr = new Vector3(pos.x, g + yy - 0.22f * (1f - rise), pos.y), Dir = o, Lean = lean, K = 1f };
                };
                c.LeaveAt = t0 + 0.12f + 0.3f + 0.12f * (1f - thetaN); c.LeaveLen = 0.2f;
                if (made % 3 == 0) _ink.Spit(born, PlanW(new Vector3(pos.x, g + 0.05f, pos.y)), Vector3.up);
                if (made % 4 == 0) _ink.End(c.LeaveAt, PlanW(new Vector3(pos.x, g + yy, pos.y)), Vector3.up, burstBody);
                made++;
            }
            _endAt = Mathf.Max(_endAt, t0 + 1.2f);
        }

        /// <summary>것: the swing met a body - torn plates in the body's own edge colour fly off it and two hold cells stay on it (2.2 s).</summary>
        void HitBlade(SpellFxHit hit, Vector2 feet, float arrive)
        {
            Vector3 outward = feet.sqrMagnitude > 1e-4f ? (_right * feet.x + _fwd * feet.y).normalized : _fwd;
            _ink.Plates(arrive, hit.Position, (outward + Vector3.up * 0.5f).normalized, _sheet.Main.RimColor);
            Hold(hit, feet, arrive, 2, arrive + 2.2f);
        }

        /// <summary>겅: a body the orbit touched - the orbit cell nearest it flashes and the body is held (2 cells, 2 s).</summary>
        void HitOrbit(SpellFxHit hit, Vector2 feet, float arrive)
        {
            Cell near = NearestCell(feet, arrive);
            if (near != null) _flash.Schedule(() => PlaceOf(near, _age).Ctr, arrive);
            Hold(hit, feet, arrive, 2, arrive + 2.0f);
        }

        // ------------------------------------------------------------ mesh

        void Rebuild(float a)
        {
            _sheet.Begin();
            foreach (var c in _cells)
            {
                float vis = VisOf(c, a);
                if (vis <= 0.02f) continue;
                EmitCellAt(c, vis, a);
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
            if (bite == HexAttachBite.Blade) EmitBladeExtras(a);
            _sheet.Flush();
        }

        void EmitCellAt(Cell c, float vis, float a)
        {
            Place p = PlaceOf(c, a);
            float s = vis * p.K * Pop(a);
            if (s <= 0.04f) return;
            if (c.Blade)
            {
                EmitBlade(p.Ctr, _shaftDir, 25f, c.Rc * s, c.Rc * 0.55f * s, c.Hash, c.Pat);
                return;
            }
            _sheet.EmitStanding(p.Ctr, p.Dir, c.Rc * s, c.Rc * s, p.Lean, c.Hash, c.Pat, 0.7f);
            if (c.Seed) EmitThread(p.Ctr, c.Rc * s, s, a, c.Phase);
        }

        /// <summary>A cell whose plane holds <paramref name="axis"/> (its long corners lie on it): the plane is turned toward the camera, then tilted <paramref name="tiltDeg"/> about the axis; the kit's per-cell shading normal.</summary>
        void EmitBlade(Vector3 ctr, Vector3 axis, float tiltDeg, float rLong, float rShort, float hash, int pat)
        {
            axis = axis.normalized;
            Vector3 cam3 = CamDir3(0.25f);
            Vector3 n = cam3 - axis * Vector3.Dot(cam3, axis);
            if (n.sqrMagnitude < 1e-4f) n = Vector3.Cross(axis, Vector3.right);
            n.Normalize();
            n = Quaternion.AngleAxis(tiltDeg, axis) * n;
            Vector3 v = Vector3.Cross(n, axis).normalized;
            var cs = _sheet.Main.Cs;
            for (int k = 0; k < 6; k++) cs[k] = ctr + axis * (SpellFxHexKit.Cos6[k] * rLong) + v * (SpellFxHexKit.Sin6[k] * rShort);
            Vector3 faceN = Vector3.Cross(axis, v).normalized;
            Vector3 wind = Vector3.Dot(faceN, cam3) >= 0f ? faceN : -faceN;
            _sheet.Main.EmitCell(ctr, cs, hash, pat, 0.7f, wind, _sheet.ShadeFor(faceN, ctr), ctr);
        }

        /// <summary>검: the thin vertical ink thread an egg hangs on (0.4 m, plain dark glass, a hair wide, swaying a little), drawn to the camera's side.</summary>
        void EmitThread(Vector3 c, float rv, float vis, float a, float phase)
        {
            float y1 = c.y - rv * 0.8660254f, y0 = y1 - 0.4f * Mathf.Clamp01(vis * 1.5f);
            if (y1 - y0 < 0.05f) return;
            Vector2 across = new Vector2(Cam.y, -Cam.x);
            float hw = 0.0125f * Mathf.Clamp01(vis * 2f), sway = 0.012f * Mathf.Sin(a * 2.4f + phase);
            var b0 = new Vector3(c.x + across.x * (sway - hw), y0, c.z + across.y * (sway - hw));
            var b1 = new Vector3(c.x + across.x * (sway + hw), y0, c.z + across.y * (sway + hw));
            var t0 = new Vector3(c.x + across.x * -hw, y1, c.z + across.y * -hw);
            var t1 = new Vector3(c.x + across.x * hw, y1, c.z + across.y * hw);
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            Vector3 front = new Vector3(Cam.x, 0.15f, Cam.y);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, 3f, true, front);
            _sheet.Main.QuadVerts(b0, b1, t0, t1, sh, 3f, true, front);
        }

        /// <summary>것: the dark crack line on the shaft (a 4 cm strip of plain dark glass - the shader has no black, so it is drawn three times - a hand's breadth in front of the cells' plane, grown after the cells
        /// and gone with them), and the swing band: when the tip has moved 0.3 m or more since 0.05 / 0.10 / 0.15 s ago, two cells of the shaft as it was then, smaller each time.</summary>
        void EmitBladeExtras(float a)
        {
            float lineVis = Smooth((a - 0.12f) / 0.25f) * (1f - Smooth((a - _hold) / 0.3f));
            if (lineVis < 0.05f) return;
            Vector3 cam3 = CamDir3(0.2f);
            Vector3 across = Vector3.Cross(_shaftDir, cam3);
            if (across.sqrMagnitude < 1e-4f) across = Vector3.right;
            across = across.normalized * (0.02f * lineVis);
            Vector3 front = cam3 * 0.035f;
            Vector3 A = _hand + _shaftDir * 0.1f + front, B = _hand + _shaftDir * (_shaftLen * 0.98f) + front;
            Vector3 sh = _sheet.Main.Frame.NormalLocal(_sheet.Tilt);
            for (int r = 0; r < 3; r++) _sheet.Main.QuadVerts(A - across, A + across, B - across, B + across, sh, 3f, true, cam3);

            Vector3 tipNow = _hand + _shaftDir * _shaftLen;
            for (int i = 0; i < GhostLag.Length; i++)
            {
                if (!TryHistory(a - GhostLag[i], out BrushSample old)) continue;
                if ((tipNow - (old.Hand + old.Dir * old.Len)).magnitude < 0.3f) continue;
                float s = GhostSize[i] * lineVis;
                for (int j = 0; j < 2; j++)
                {
                    float f = j == 0 ? 0.6f : 0.94f, rl = (j == 0 ? 0.17f : 0.24f) * s;
                    EmitBlade(old.Hand + old.Dir * (f * old.Len), old.Dir, 25f, rl, rl * 0.55f, 0.31f + 0.17f * j, SpellFxHexKit.PatChoices[(i + j) % SpellFxHexKit.PatChoices.Length]);
                }
            }
        }

        bool TryHistory(float t, out BrushSample sample)
        {
            sample = default;
            bool got = false;
            foreach (var h in _hist) { if (h.T <= t) { sample = h; got = true; } else break; }
            return got;
        }
    }
}
