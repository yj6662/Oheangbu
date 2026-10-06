using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>pass 4b (Q6): a host that can draw an airborne tailed drop with a ceiling of its own on the drawn length (the
    /// thrown head). The stage asks for it only when an op names one; a host without it draws the drop as it always did.</summary>
    public interface ISpellAirTailHost308
    {
        void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY, float tailMax);
    }

    // #308 present add-on: the scene half of the spell presenter on the deploy layer (SPEC-SPELL-120-308 section 9 seam,
    // drawn by SPEC-SPELL-DEPLOY-308; decisions D308-10c / D308-13b). One per deploy director (created in its Boot; no
    // global instance, no static state). SpellDeployPresenter308 hands it the presenter calls of the glyphs the layer has
    // switched on; SpellPresentRules308 (pure) turns a call into ops; this component carries the ops out through the layer's
    // host (ISpellDeployHost308): deploy bursts, ground marks, airborne drops, and the bodies of InkPresentForms308.
    // It also shows what no presenter call announces, by reading only:
    //   - a confirmed hit of a presented glyph that no live cast took (a handler's second stage, a derived shot, a companion
    //     shot, a detonation, a sword strike): the layer's hit splash, once the wiring has named the hit's glyph,
    //   - what a spell left on an enemy (its control / modifier state): an ink mark the enemy carries.
    // Presentation only. It never writes to combat: it reads positions, the enemy state readers and the damage re-broadcast.
    [DefaultExecutionOrder(-890)]   // after the director (-900), before the residue field draws (960)
    public sealed class SpellPresentStage308 : MonoBehaviour
    {
        private const int MaxPendingHits = 8, MaxTargets = 8, MaxHeldGroups = 32, MaxDripGates = 16, MaxFreeTimes = 16;
        private const int HeldBase = 0x40000000;        // held-mark owner ids of this stage (the director's start at int.MinValue)
        private const float EyeHeight = 1.6f;

        private struct Live
        {
            public bool Used, Timed;
            public int Owner, Serial, HeldId, Body, Cast;
            public float Until;
            public SpellFxRequest Request;
            public SpellDeploy308MapSO.Row Row;
        }
        private bool[] _ringLive;
        private float[] _ringStarted;
        private struct CastSlot { public InkDeployRuntime308 Runtime; public bool Busy, Standing; public float Start; public int Serial, Owner; }
        private struct Delayed { public bool Used; public PresentOp308 Op; public float At; public int HeldId, Owner; }
        private struct HeldGroup { public int Id, Count, Serial; }
        private struct PendingHit { public EnemyVitals Target; public Element Element; public PresentHitSource308 Source; public bool Routed; public int Frame; }
        private struct DripGate { public int Target; public float NextAt; }

        private SpellDeploy308ProfileSO _profile;
        private SpellPresent308SheetSO _sheet;
        private ISpellDeployHost308 _host;
        private CombatLoopWiring _wiring;
        private SpellVisualSetSO _visuals;
        private DeployTier308 _tier;
        private bool _ownsSheet, _configured, _manualClock;
        private float _manualNow, _nextWatch;
        private int _serial, _heldSerial, _heldTotal, _heldGroups, _heldBudget, _pendingCount, _freeNext, _events;

        private Live[] _live;
        private CastSlot[] _casts;
        private InkPresentBody308[] _bodies;
        private Delayed[] _delayed;
        private PresentOp308[] _ops, _freeOps;
        private DeployDrop308[] _hitScratch;
        private readonly Vector3[] _targetPoints = new Vector3[MaxTargets];
        private readonly PendingHit[] _pending = new PendingHit[MaxPendingHits];
        private readonly HeldGroup[] _held = new HeldGroup[MaxHeldGroups];
        private readonly DripGate[] _drips = new DripGate[MaxDripGates];
        private readonly float[] _freeTimes = new float[MaxFreeTimes];
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private readonly HashSet<int> _unroutedSeen = new HashSet<int>();

        public SpellPresent308SheetSO Sheet => _sheet;
        // (stepped by hand - the edit-mode unit command - the component is never enabled by the engine, so the clock mode stands in)
        public bool Ready => _configured && _profile != null && _host != null && _sheet != null && (_manualClock || isActiveAndEnabled);
        public bool SheetIsAsset => _sheet != null && !_ownsSheet;
        public int Begun { get; private set; }
        public int Cues { get; private set; }
        public int Ended { get; private set; }
        /// <summary>Presenter calls no route answered (nothing was drawn for them). The offline check keeps this at 0 for every call
        /// the handlers make; a number here means a handler sends a cue the sheet does not know.</summary>
        public int Unrouted { get; private set; }
        public int CastsStarted { get; private set; }
        public int CastsRefused { get; private set; }
        public int Stamps { get; private set; }
        public int AirSprites { get; private set; }
        public int BodiesShown { get; private set; }
        public int BodiesRefused { get; private set; }
        /// <summary>forms3 (D9): ring strokes laid for zones (each one is a body, held until its handle lets go).</summary>
        public int RingsShown { get; private set; }
        public int RingsEvicted { get; private set; }
        public int FreeSplashes { get; private set; }
        public int FreeDropped { get; private set; }
        public int Drips { get; private set; }
        public int HeldEvicted { get; private set; }
        public int Expired { get; private set; }
        public int StateMarks { get; private set; }
        public int HeldNow => _heldTotal;
        public int LiveCount { get { int n = 0; if (_live != null) foreach (var live in _live) if (live.Used) n++; return n; } }
        public int ActiveCasts { get { int n = 0; if (_casts != null) foreach (var cast in _casts) if (cast.Busy) n++; return n; } }
        public int ActiveBodies { get { int n = 0; if (_bodies != null) foreach (var body in _bodies) if (body != null && body.Busy) n++; return n; } }
        public InkPresentBody308 BodyAt(int index) => _bodies != null && index >= 0 && index < _bodies.Length ? _bodies[index] : null;
        public int BodySlots => _bodies != null ? _bodies.Length : 0;
        private float Now => _manualClock ? _manualNow : Time.time;

        /// <summary>sheet = null: Resources/Deploy308/SpellPresent308Sheet, else the code defaults. wiring = null in checks.</summary>
        public void Configure(SpellDeploy308ProfileSO profile, ISpellDeployHost308 host, CombatLoopWiring wiring, SpellPresent308SheetSO sheet = null)
        {
            _profile = profile; _host = host; _wiring = wiring;
            if (profile == null || host == null) return;
            _tier = host.Tier;
            if (_ownsSheet && _sheet != null) DestroyNow(_sheet);
            _ownsSheet = false;
            _sheet = sheet != null ? sheet : Resources.Load<SpellPresent308SheetSO>(SpellPresent308SheetSO.ResourcesPath);
            if (_sheet == null)
            {
                _sheet = ScriptableObject.CreateInstance<SpellPresent308SheetSO>();
                _sheet.name = "SpellPresent308Sheet (code defaults)"; _sheet.hideFlags = HideFlags.DontSave; _ownsSheet = true;
            }
            var pool = _sheet.Pool;
            // held marks are never pushed out of the residue field: the budget is a share of THIS tier's spell ring (Mobile: 64 marks)
            _heldBudget = _sheet.HeldMarks(Mathf.Clamp(profile.Tier(_tier).SpellResidue, 8, 256));
            _live = new Live[Mathf.Clamp(pool.Handles, 8, 128)];
            _casts = new CastSlot[Mathf.Clamp(pool.Casts, 1, 8)];
            _bodies = new InkPresentBody308[Mathf.Clamp(pool.Bodies, 1, 24)];
            _delayed = new Delayed[SpellPresent308SheetSO.MaxOps];
            _ops = new PresentOp308[SpellPresent308SheetSO.MaxOps];
            _freeOps = new PresentOp308[SpellPresent308SheetSO.MaxOps];
            _hitScratch = new DeployDrop308[InkDeployForms308.MaxHitDrops];
            _serial = _heldSerial = _heldTotal = _heldGroups = _pendingCount = _freeNext = 0;
            for (int i = 0; i < MaxFreeTimes; i++) _freeTimes[i] = float.NegativeInfinity;
            _configured = true;
        }

        public void BindVisuals(SpellVisualSetSO visuals) { _visuals = visuals; }

        /// <summary>Is this glyph shown here (the layer's data switch)? Otherwise the interim presenter keeps it.</summary>
        public bool Uses(char letter) => Ready && SpellPresentRules308.Uses(_profile, letter);

        // ---- the presenter's three calls ----

        public void Begin(int owner, in SpellFxRequest request)
        {
            if (!Ready || owner == 0 || !_profile.Map.TryGet(request.Letter, out var row)) return;
            Begun++;
            int slot = Rent(owner);
            _live[slot].Request = request; _live[slot].Row = row;
            _live[slot].Timed = request.Duration > 0f;
            _live[slot].Until = Now + request.Duration + _sheet.Pool.HandleSlack;
            Run(slot, PresentMoment308.Begin, default, default);
        }

        public void Cue(int owner, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!Ready) return;
            int slot = Find(owner);
            if (slot < 0) return;       // the handle ended (or was pushed out): a late cue shows nothing, like a body that is gone
            Cues++;
            Run(slot, PresentMoment308.Cue, cue, args);
        }

        public void End(int owner, bool immediate)
        {
            if (!Ready) return;
            int slot = Find(owner);
            if (slot < 0) return;
            Ended++;
            // immediate = replaced by a recast or pushed out by a newer one: no end cue, what it holds goes at once
            if (immediate) Forget(slot, true);
            else Run(slot, PresentMoment308.End, default, default);
        }

        /// <summary>Death, rest, scene leave: every handle goes. Held ground marks start to dry; bodies and bursts are taken off.</summary>
        public void EndAll()
        {
            if (!_configured) return;
            for (int i = 0; i < _live.Length; i++) if (_live[i].Used) Forget(i, true);
            for (int i = 0; i < _delayed.Length; i++) _delayed[i] = default;
            for (int i = 0; i < _heldGroups; i++) _host.ReleaseHeld(_held[i].Id);
            _heldGroups = _heldTotal = 0;
            for (int i = 0; i < _casts.Length; i++)
            {
                if (_casts[i].Busy && _casts[i].Runtime != null) _casts[i].Runtime.Release();
                _casts[i].Busy = false;
            }
            foreach (var body in _bodies) if (body != null) body.Hide();
            _pendingCount = 0;
        }

        // ---- a presenter call -> ops -> the layer ----

        private void Run(int slot, PresentMoment308 moment, SpellFxCue cue, in SpellFxCueArgs args)
        {
            var e = Event(slot, moment, cue, args);
            var plan = SpellPresentRules308.Plan(_sheet, _profile, e, _ops);
            if (!plan.Routed)
            {
                Unrouted++;
                int key = ((int)e.Role << 16) | ((int)moment << 8) | (int)cue;
                if (_unroutedSeen.Add(key)) Debug.LogWarning("[Present308] no route for " + e.Role + " / " + moment + (moment == PresentMoment308.Cue ? " / " + cue : "") + ": nothing is drawn", this);
                if (moment == PresentMoment308.End) Forget(slot, false);
                return;
            }
            // (a burst that begins tells the layer the glyph has its own presentation - InkDeployRuntime308.Configure does; when the
            // layer's pool refuses the burst, its hook B lays the category's plain residue instead)
            for (int i = 0; i < plan.Count; i++) Execute(slot, _ops[i], args.Target, e.EyeForward);
            if (plan.Ends) Forget(slot, false);
        }

        private PresentEvent308 Event(int slot, PresentMoment308 moment, SpellFxCue cue, in SpellFxCueArgs args)
        {
            var request = _live[slot].Request;
            Vector3 feet = CasterFeet(request);
            Camera camera = _host.ViewCamera;
            Vector3 eye = camera != null ? camera.transform.position : feet + Vector3.up * EyeHeight;
            Vector3 eyeForward = camera != null ? camera.transform.forward : request.FallbackPoint - request.Origin;
            var e = new PresentEvent308
            {
                Role = request.Role, Moment = moment, Cue = cue, MapCategory = _live[slot].Row.Category, Element = request.Element, Tier = _tier,
                Grade = _profile.GradeIndex(request.Grade01), Seed = _live[slot].Serial * 7919 + (int)cue * 131 + (++_events),
                Origin = request.Origin, FallbackPoint = request.FallbackPoint, HasTarget = request.Target != null, HasPlan = request.Area != null,
                TargetPoint = request.Target != null ? request.Target.position + Vector3.up * InkDeployRuntime308.ChestHeight : request.FallbackPoint,
                ImpactClock = request.ImpactClock, Duration = request.Duration, Radius = request.Radius,
                Point = args.Point, Normal = args.Normal, CueHasTarget = args.Target != null, At = args.At, Value = args.Value, Now = Now,
                CueTargetPoint = args.Target != null ? args.Target.position + Vector3.up * InkDeployRuntime308.ChestHeight : args.Point,
                Eye = eye, EyeForward = eyeForward, CasterFeet = feet, TargetPoints = _targetPoints, HitScratch = _hitScratch,
            };
            if (args.Targets != null)
                for (int i = 0; i < args.Targets.Length && e.TargetCount < MaxTargets; i++)
                    if (args.Targets[i] != null) _targetPoints[e.TargetCount++] = args.Targets[i].position + Vector3.up * InkDeployRuntime308.ChestHeight;
            if (moment == PresentMoment308.Cue) e.GroundY = GroundY(args.Point, args.Point.y - InkDeployRuntime308.ChestHeight, args.Target);
            return e;
        }

        private void Execute(int slot, in PresentOp308 op, Transform cueTarget, Vector3 eyeForward)
        {
            // (forms3: a zone's ring stroke waits for the zone to form like its ground marks do)
            bool ring = op.Kind == PresentOpKind308.Body && op.Body == PresentBody308.ZoneRing;
            if (op.Delay > 0f && (op.Kind == PresentOpKind308.Stamp || op.Kind == PresentOpKind308.Air || (ring && slot >= 0))) { Queue(slot, op); return; }
            switch (op.Kind)
            {
                case PresentOpKind308.Cast: StartCast(slot, op, cueTarget, eyeForward); break;
                case PresentOpKind308.Stamp: Stamp(op, op.Held && slot >= 0 ? HeldIdOf(slot) : 0); break;
                case PresentOpKind308.Air: SpawnAir(op); break;
                case PresentOpKind308.Body: if (slot >= 0) { if (ring) ShowRing(slot, op); else ShowBody(slot, op, cueTarget, eyeForward); } break;
                case PresentOpKind308.EndBody: if (slot >= 0) EndBody(slot, false); break;
                case PresentOpKind308.ReleaseHeld: if (slot >= 0) ReleaseHeld(slot); break;
            }
        }

        // pass 4b (Q6): EVERY airborne drop of the stage is spawned here - at once, after its delay, or when the waiting list is
        // full (the delayed drops were spawned at their raw size: a throw's followers missed the least on-screen size). A drop
        // whose op names a tail ceiling (the thrown head) is handed to a host that can draw one; any other host draws it as before.
        private void SpawnAir(in PresentOp308 op)
        {
            if (op.TailMax > 0f && _host is ISpellAirTailHost308 tailed) tailed.SpawnAir(op.Point, op.Velocity, AirSizeOf(op), op.Cell, op.Life, op.Gravity, op.LandY, op.TailMax);
            else _host.SpawnAir(op.Point, op.Velocity, AirSizeOf(op), op.Cell, op.Life, op.Gravity, op.LandY);
            AirSprites++;
        }

        // forms4 (P8): the sheet's least on-screen size for a drop that flies far from the eye (the farther end of its flight)
        private float AirSizeOf(in PresentOp308 op)
        {
            // fix pass 4b (review S6): only the drops the rules mark (thrown ink, a zone's sustained drops) - a hit's splash keeps its
            // accepted size; the floor is worked out with the view's own field of view, and for the tailed drop on its short side
            if (!op.Floor) return op.Size;
            Camera camera = _host != null ? _host.ViewCamera : null;
            if (camera == null || _sheet == null) return op.Size;
            Vector3 eye = camera.transform.position;
            float wide = _profile != null && _profile.AirTail != null && _profile.AirTail.Tailed(op.Cell) ? _profile.AirTail.Width : 1f;
            return _sheet.AirSize(op.Size, Mathf.Max((op.Point - eye).magnitude, (op.Point + op.Velocity * op.Life - eye).magnitude), camera.fieldOfView, wide);
        }

        private void Queue(int slot, in PresentOp308 op)
        {
            int held = op.Kind == PresentOpKind308.Stamp && op.Held && slot >= 0 ? HeldIdOf(slot) : 0;
            for (int i = 0; i < _delayed.Length; i++)
            {
                if (_delayed[i].Used) continue;
                _delayed[i] = new Delayed { Used = true, Op = op, At = Now + op.Delay, HeldId = held, Owner = slot >= 0 ? _live[slot].Owner : 0 };
                return;
            }
            // no room to wait: it is laid now rather than lost
            if (op.Kind == PresentOpKind308.Body) { if (slot >= 0) ShowRing(slot, op); }
            else if (op.Kind == PresentOpKind308.Stamp) Stamp(op, held);
            else SpawnAir(op);
        }

        // heldId 0 = an ordinary mark; otherwise it stays wet until that group is released (if the group is still there and the
        // budget has room - else it is an ordinary mark that dries in the same seconds)
        private void Stamp(in PresentOp308 op, int heldId)
        {
            bool held = heldId != 0 && HoldOne(heldId);
            _host.StampResidue(new ResidueStamp308
            {
                Point = op.Point, Forward = op.Forward, Width = op.Size, Length = op.Size, Cell = op.Cell, Opacity = op.Opacity, Life = op.Life, Held = held, Owner = held ? heldId : 0,
            });
            Stamps++;
        }

        // ---- held ground marks: one group per handle, a budget over all of them ----

        private int HeldIdOf(int slot)
        {
            if (_live[slot].HeldId != 0) return _live[slot].HeldId;
            if (_heldGroups >= MaxHeldGroups) EvictHeld(0);
            int id = HeldBase + (++_heldSerial & 0xFFFFF);
            _held[_heldGroups++] = new HeldGroup { Id = id, Serial = _heldSerial };
            _live[slot].HeldId = id;
            return id;
        }

        private bool HoldOne(int id)
        {
            int group = -1;
            for (int i = 0; i < _heldGroups; i++) if (_held[i].Id == id) { group = i; break; }
            if (group < 0) return false;
            // over the budget: the oldest OTHER group starts to dry and gives its marks up
            while (_heldTotal + 1 > _heldBudget)
            {
                int oldest = -1;
                for (int i = 0; i < _heldGroups; i++) if (_held[i].Id != id && (oldest < 0 || _held[i].Serial < _held[oldest].Serial)) oldest = i;
                if (oldest < 0) return false;
                EvictHeld(oldest);
                for (int i = 0; i < _heldGroups; i++) if (_held[i].Id == id) { group = i; break; }
            }
            _held[group].Count++; _heldTotal++;
            return true;
        }

        private void EvictHeld(int index)
        {
            _host.ReleaseHeld(_held[index].Id);
            _heldTotal -= _held[index].Count;
            _held[index] = _held[--_heldGroups];
            HeldEvicted++;
        }

        private void ReleaseHeld(int slot)
        {
            ReleaseRings(_live[slot].Owner);
            int id = _live[slot].HeldId;
            if (id == 0) return;
            _live[slot].HeldId = 0;
            _host.ReleaseHeld(id);
            for (int i = 0; i < _heldGroups; i++)
                if (_held[i].Id == id) { _heldTotal -= _held[i].Count; _held[i] = _held[--_heldGroups]; break; }
            // marks of the group that were still waiting for their moment are not laid at all
            for (int i = 0; i < _delayed.Length; i++) if (_delayed[i].Used && _delayed[i].HeldId == id) _delayed[i] = default;
        }

        // ---- deploy bursts ----

        private void StartCast(int slot, in PresentOp308 op, Transform cueTarget, Vector3 eyeForward)
        {
            int c = RentCast();
            if (c < 0) { CastsRefused++; return; }
            var request = _live[slot].Request;
            var row = _live[slot].Row;
            if (row.Category != op.Category)
            {
                // another category's form for this glyph (a standing wall, a rising tree): the row's impact flags belong to its own
                row.Category = op.Category; row.ImpactFrame = false; row.ImpactOnTrigger = false;
            }
            Transform target = op.Target == PresentRef308.RequestTarget ? request.Target : op.Target == PresentRef308.CueTarget ? cueTarget : null;
            var plan = op.UsePlan ? request.Area : null;
            Vector3 fallback = op.Point;
            Vector3 gap = fallback - op.Origin; gap.y = 0f;
            Vector3 view = new Vector3(eyeForward.x, 0f, eyeForward.z);
            view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.forward;
            // a cast without a target whose point is its own origin, or lies behind the brush (a buff names the caster's feet),
            // still needs a direction: the view's
            if (target == null && plan == null && (gap.sqrMagnitude < .01f || Vector3.Dot(gap, view) <= 0f)) fallback = op.Origin + view;
            var cast = new DeployCast308
            {
                Profile = _profile, Row = row, Origin = op.Origin, Target = target, FallbackPoint = fallback, Plan = plan,
                ImpactClock = op.Triggered || op.HoldSeconds > 0f ? 0f : request.ImpactClock, Grade01 = request.Grade01, Tint = TintOf(request.Letter, request.Element),
                HoldSeconds = op.HoldSeconds, WardRadius = op.WardRadius, Triggered = op.Triggered, Tier = _tier,
                Cover = op.HoldSeconds > 0f && op.Category == DeployCategory308.Ward,   // forms3 fix pass: a cover stands as rising strokes
                Seed = plan != null && plan.VisualSeed != 0 ? plan.VisualSeed : request.Letter * 7919 + _live[slot].Serial * 31 + (++_events),
            };
            if (_casts[c].Runtime == null)
            {
                var holder = new GameObject("SpellPresent308_Cast" + c) { hideFlags = HideFlags.DontSave };
                holder.transform.SetParent(transform, false);
                _casts[c].Runtime = holder.AddComponent<InkDeployRuntime308>();
            }
            if (!_casts[c].Runtime.Configure(cast, _host)) { CastsRefused++; return; }   // the layer's burst pool is exhausted
            _casts[c].Busy = true; _casts[c].Start = Now; _casts[c].Serial = ++_serial; _casts[c].Owner = _live[slot].Owner; _casts[c].Standing = op.HoldSeconds > 0f;
            if (_casts[c].Standing) _live[slot].Cast = c;
            CastsStarted++;
        }

        private int RentCast()
        {
            int oldest = -1;
            for (int i = 0; i < _casts.Length; i++)
            {
                if (!_casts[i].Busy) return i;
                if (!_casts[i].Standing && (oldest < 0 || _casts[i].Serial < _casts[oldest].Serial)) oldest = i;
            }
            if (oldest < 0) return -1;      // every slot holds a standing wall
            _casts[oldest].Runtime.Release(); _casts[oldest].Busy = false;
            return oldest;
        }

        // ---- bodies ----

        private void ShowBody(int slot, in PresentOp308 op, Transform cueTarget, Vector3 eyeForward)
        {
            var request = _live[slot].Request;
            bool mark = op.Body == PresentBody308.MarkCarried || op.Body == PresentBody308.MarkInstall;
            Transform carrier = !mark ? null : op.Target == PresentRef308.RequestTarget ? request.Target : op.Target == PresentRef308.CueTarget ? cueTarget : null;
            if (mark && carrier == null) return;
            if (mark) EndBody(slot, false);       // one carried mark per handle
            int b = RentBody();
            if (b < 0) { BodiesRefused++; return; }
            Vector3 eye = Eye(request);
            var input = new PresentBodyInput308
            {
                Profile = _profile, Sheet = _sheet, Body = op.Body, Element = request.Element, Tinted = true, Grade = _profile.GradeIndex(request.Grade01),
                Seed = _live[slot].Serial * 131 + (++_events), Hit = op.Hit, Reach = op.Reach,
            };
            Vector3 root = eye, forward = eyeForward;
            if (mark) { Measure(carrier, out input.Height, out input.StandOff); root = carrier.position; forward = root - eye; }
            else if (op.Body == PresentBody308.Slash && (op.Point - eye).sqrMagnitude > .04f) forward = op.Point - eye;
            float hold = op.BodyHold;
            // a carried mark never outlives the life its handler asked for: some handlers never say that what the mark stands for
            // is gone (an ember after its last move), and the handle's own end comes HandleSlack later
            if (mark && _live[slot].Timed) hold = Mathf.Clamp(_live[slot].Until - _sheet.Pool.HandleSlack - Now, 0f, _sheet.MarkSeconds);
            if (!_bodies[b].Show(input, mark ? _live[slot].Owner : 0, carrier, root, forward, hold, TintOf(request.Letter, request.Element), Now)) { BodiesRefused++; return; }
            BodiesShown++;
            if (mark) _live[slot].Body = b;
        }

        // ---- forms3 (D9): the zone's ring stroke - a body lying on the ground, held by the handle like its held ground marks

        private void ShowRing(int slot, in PresentOp308 op)
        {
            // fix pass (review S1): the body pool is shared with slashes, blades and marks and a ring stroke is held for as long
            // as its zone lives - so only sheet.RingBodies of them are alive at once; the oldest melts to make room
            if (_ringLive == null || _ringLive.Length != _bodies.Length) { _ringLive = new bool[_bodies.Length]; _ringStarted = new float[_bodies.Length]; }
            for (int i = 0; i < _bodies.Length; i++)
            {
                _ringLive[i] = _bodies[i] != null && _bodies[i].Busy && _bodies[i].Form == PresentBody308.ZoneRing && !_bodies[i].Melting;
                _ringStarted[i] = _ringLive[i] ? _bodies[i].Started : 0f;
            }
            int oldest = SpellPresentRules308.RingToRelease(_sheet, _ringLive, _ringStarted, _bodies.Length);
            if (oldest >= 0) { _bodies[oldest].Release(Now); RingsEvicted++; }
            int b = RentBody();
            if (b < 0) { BodiesRefused++; return; }
            var request = _live[slot].Request;
            float radius = Mathf.Max(_sheet.Ring.MinRadius, op.Reach);
            // the ground the stroke lies on: the plane through four points of the ring itself (a ring on a slope leans with it)
            Vector3 centre = op.Point;
            float east = GroundY(centre + Vector3.right * radius, centre.y, null), west = GroundY(centre - Vector3.right * radius, centre.y, null);
            float north = GroundY(centre + Vector3.forward * radius, centre.y, null), south = GroundY(centre - Vector3.forward * radius, centre.y, null);
            Vector3 up = new Vector3((west - east) / (2f * radius), 1f, (south - north) / (2f * radius)).normalized;
            if (up.y < _profile.Residue.MinNormalY) up = Vector3.up;      // steeper than a mark may lie: level, at the mean height
            centre.y = (east + west + north + south) * .25f;
            var input = new PresentBodyInput308
            {
                Profile = _profile, Sheet = _sheet, Body = PresentBody308.ZoneRing, Element = request.Element, Tinted = true, Grade = _profile.GradeIndex(request.Grade01),
                Seed = _live[slot].Serial * 131 + (++_events), Radius = radius, Tier = _tier,
            };
            // held until the handle lets go (Release, End, its own stated life): the sheet's ceiling takes it off at the latest
            if (!_bodies[b].Show(input, _live[slot].Owner, null, centre, Vector3.forward, _sheet.RingSeconds, TintOf(request.Letter, request.Element), Now, up)) { BodiesRefused++; return; }
            BodiesShown++; RingsShown++;
        }

        // the rings of a handle melt (its zone is over, or the handle is gone) and the ones still waiting are not laid
        private void ReleaseRings(int owner)
        {
            if (owner == 0) return;
            float now = Now;
            for (int i = 0; i < _bodies.Length; i++)
                if (_bodies[i] != null && _bodies[i].Busy && _bodies[i].Form == PresentBody308.ZoneRing && _bodies[i].Owner == owner) _bodies[i].Release(now);
            for (int i = 0; i < _delayed.Length; i++)
                if (_delayed[i].Used && _delayed[i].Op.Kind == PresentOpKind308.Body && _delayed[i].Owner == owner) _delayed[i] = default;
        }

        private void EndBody(int slot, bool immediate)
        {
            int b = _live[slot].Body;
            _live[slot].Body = -1;
            if (b < 0 || _bodies[b] == null || !_bodies[b].Busy || _bodies[b].Owner != _live[slot].Owner) return;
            if (immediate) _bodies[b].Hide(); else _bodies[b].Release(Now);
        }

        private int RentBody()
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                if (_bodies[i] == null)
                {
                    var holder = new GameObject("SpellPresent308_Body" + i) { hideFlags = HideFlags.DontSave };
                    holder.transform.SetParent(transform, false);
                    _bodies[i] = holder.AddComponent<InkPresentBody308>();
                    _bodies[i].Prepare(_profile, _sheet, _tier);
                    return i;
                }
                if (!_bodies[i].Busy) return i;
            }
            return -1;
        }

        // where a mark sits on its carrier: a share of the collider's height, standing off its radius toward the eye
        private void Measure(Transform carrier, out float height, out float standOff)
        {
            height = InkDeployRuntime308.ChestHeight; standOff = _sheet.Mark.TowardEye;
            var body = carrier.GetComponent<Collider>();
            if (body == null) return;
            var bounds = body.bounds;
            if (bounds.size.y > .2f) height = Mathf.Max(.2f, bounds.max.y - carrier.position.y) * _sheet.Mark.HeightShare;
            standOff = Mathf.Max(standOff, Mathf.Max(bounds.extents.x, bounds.extents.z) + .1f);
        }

        // ---- handles ----

        private int Find(int owner)
        {
            if (owner == 0 || _live == null) return -1;
            for (int i = 0; i < _live.Length; i++) if (_live[i].Used && _live[i].Owner == owner) return i;
            return -1;
        }

        private int Rent(int owner)
        {
            int free = -1, oldest = -1;
            for (int i = 0; i < _live.Length; i++)
            {
                if (!_live[i].Used) { free = i; break; }
                if (oldest < 0 || _live[i].Serial < _live[oldest].Serial) oldest = i;
            }
            if (free < 0) { Forget(oldest, true); free = oldest; }   // bookkeeping bound: the oldest handle makes room
            _live[free] = new Live { Used = true, Owner = owner, Serial = ++_serial, Body = -1, Cast = -1 };
            return free;
        }

        // what the handle still holds is let go: ground marks dry, its mark melts, its standing wall goes when it was replaced
        private void Forget(int slot, bool immediate)
        {
            if (slot < 0 || !_live[slot].Used) return;
            ReleaseHeld(slot);
            EndBody(slot, immediate);
            int c = _live[slot].Cast;
            if (immediate && c >= 0 && _casts[c].Busy && _casts[c].Owner == _live[slot].Owner) { _casts[c].Runtime.Release(); _casts[c].Busy = false; }
            _live[slot] = default;
        }

        // ---- per frame ----

        private void Update() { if (_configured && !_manualClock) Step(Time.time); }

        /// <summary>One frame. The unit command steps it with its own clock in edit mode.</summary>
        public void Step(float now, bool manual = false)
        {
            if (!_configured) return;
            _manualClock = manual; _manualNow = now;
            _pendingCount = 0;      // a confirmed hit and its glyph always come in one call: nothing may wait across frames
            for (int i = 0; i < _casts.Length; i++)
            {
                if (!_casts[i].Busy) continue;
                var runtime = _casts[i].Runtime;
                if (runtime == null) { _casts[i].Busy = false; continue; }
                runtime.Sample(now - _casts[i].Start);
                if (!runtime.Configured) _casts[i].Busy = false;
            }
            for (int i = 0; i < _delayed.Length; i++)
            {
                if (!_delayed[i].Used || now < _delayed[i].At) continue;
                var due = _delayed[i]; _delayed[i] = default;
                if (due.Op.Kind == PresentOpKind308.Body) { int owner = Find(due.Owner); if (owner >= 0) ShowRing(owner, due.Op); }
                else if (due.Op.Kind == PresentOpKind308.Stamp) Stamp(due.Op, due.HeldId);
                else SpawnAir(due.Op);
            }
            for (int i = 0; i < _live.Length; i++)
            {
                // a handle whose own stated life is over is ended even when its handler never says so
                if (!_live[i].Used || !_live[i].Timed || now <= _live[i].Until) continue;
                Expired++;
                Forget(i, false);
            }
            if (!manual && now >= _nextWatch)
            {
                _nextWatch = now + 1f / Mathf.Clamp(_sheet.Mark.WatchHz, 1f, 30f);
                Watch(now);
            }
            Camera camera = _host.ViewCamera;
            Vector3 eye = camera != null ? camera.transform.position : transform.position + Vector3.up * EyeHeight;
            foreach (var body in _bodies) if (body != null && body.Busy) body.Sample(now, eye);
        }

        // ---- what a spell left on an enemy: read from the enemy, shown as a mark it carries ----

        private void Watch(float now)
        {
            if (_wiring == null) return;
            bool on = _sheet.Mark.StateMarks && !string.IsNullOrEmpty(_profile.EnabledLetters);
            var targets = ((ISpellCastHost)_wiring).Targets;
            Camera camera = _host.ViewCamera;
            for (int i = 0; targets != null && i < targets.Count; i++)
            {
                var enemy = targets[i];
                if (enemy == null) continue;
                var state = PresentState308.None;
                if (on && enemy.IsAlive && enemy.isActiveAndEnabled)
                    state = SpellPresentRules308.StateOf(_sheet, enemy.Control.BlocksActions(now), enemy.Control.MovementScale(now), enemy.Modifiers.DamageTakenScale(now),
                        enemy.Modifiers.DefenceShred(now), enemy.Modifiers.OutgoingScale(now));
                Transform carrier = enemy.transform;
                int b = -1;
                for (int k = 0; k < _bodies.Length; k++)
                    if (_bodies[k] != null && _bodies[k].Busy && !_bodies[k].Melting && _bodies[k].Form == PresentBody308.MarkState && _bodies[k].Carrier == carrier) { b = k; break; }
                if (state == PresentState308.None) { if (b >= 0) _bodies[b].Release(now); continue; }
                if (b >= 0 && _bodies[b].States == state) continue;
                if (b < 0) b = RentBody();
                if (b < 0) { BodiesRefused++; continue; }
                Vector3 eye = camera != null ? camera.transform.position : carrier.position - carrier.forward * 4f + Vector3.up * EyeHeight;
                var input = new PresentBodyInput308 { Profile = _profile, Sheet = _sheet, Body = PresentBody308.MarkState, States = state, Seed = enemy.GetInstanceID() * 31 + (int)state };
                Measure(carrier, out input.Height, out input.StandOff);
                if (_bodies[b].Show(input, 0, carrier, carrier.position, carrier.position - eye, -1f, Color.black, now)) StateMarks++;
            }
        }

        // ---- the confirmed hit no live cast took ----

        /// <summary>From the director's damage listener: a confirmed hit and whether a live cast took it. Nothing is shown yet -
        /// the hit's glyph follows in the same call of the wiring (ConfirmedHitLetter). Hits nest (a detonation inside a hit
        /// hook), so they wait on a small stack.</summary>
        public void NoteConfirmedHit(EnemyDamageResult result, bool routed)
        {
            if (!Ready || !result.Attack.Element.HasValue) return;      // the wiring names a glyph only for a hit with an element
            if (_pendingCount >= MaxPendingHits) { _pendingCount = 0; return; }
            _pending[_pendingCount++] = new PendingHit { Target = result.Target, Element = result.Attack.Element.Value, Source = SourceOf(result.Attack.Source), Routed = routed, Frame = Time.frameCount };
        }

        /// <summary>The wiring names the glyph of the hit it has just confirmed. Returns whether the layer shows that hit itself
        /// (the KTP contact then stays out): `owns` is the layer's own answer, widened to the glyphs this presenter shows. A hit
        /// of such a glyph that no live cast took throws the hit splash here.</summary>
        public bool ConfirmedHitLetter(char letter, bool owns)
        {
            if (!Ready) return owns;
            bool owned = owns || SpellPresentRules308.OwnsHit(_profile, _sheet, letter);
            if (_pendingCount == 0) return owned;
            var hit = _pending[--_pendingCount]; _pending[_pendingCount] = default;
            if (hit.Frame != Time.frameCount) { _pendingCount = 0; return owned; }
            int grade = SpellPresentRules308.FreeSplash(_sheet, hit.Source, hit.Routed, owned);
            if (grade == SpellPresentRules308.NoSplash || hit.Target == null) return owned;
            Transform target = hit.Target.transform;
            Vector3 point = target.position + Vector3.up * InkDeployRuntime308.ChestHeight;
            float ground = GroundY(point, target.position.y, target);
            float now = Now;
            int n = 0;
            if (grade == SpellPresentRules308.DripOnly)
            {
                if (!DripDue(hit.Target.GetInstanceID(), now)) return owned;
                SpellPresentRules308.DripAt(_freeOps, ref n, _sheet, _profile, point, ground, hit.Element, Time.frameCount * 31 + hit.Target.GetInstanceID());
                Drips++;
            }
            else
            {
                if (!FreeDue(now)) { FreeDropped++; return owned; }
                Camera camera = _host.ViewCamera;
                Vector3 eye = camera != null ? camera.transform.position : point - target.forward * 4f + Vector3.up * .5f;
                SpellPresentRules308.Splash(_freeOps, ref n, _sheet, _profile, point, eye, ground, hit.Element, grade, _tier, Time.frameCount * 31 + hit.Target.GetInstanceID(), _hitScratch);
                FreeSplashes++;
            }
            for (int i = 0; i < n; i++) Execute(-1, _freeOps[i], null, Vector3.forward);
            return owned;
        }

        private static PresentHitSource308 SourceOf(DamageSource source)
        {
            switch (source)
            {
                case DamageSource.PlayerDirect: return PresentHitSource308.Direct;
                case DamageSource.Companion: return PresentHitSource308.Companion;
                case DamageSource.Retaliation: return PresentHitSource308.Retaliation;
                case DamageSource.Harmony: return PresentHitSource308.Harmony;
                case DamageSource.PersistentSpell: return PresentHitSource308.Persistent;
                default: return PresentHitSource308.Other;
            }
        }

        private bool FreeDue(float now)
        {
            int recent = 0;
            for (int i = 0; i < MaxFreeTimes; i++) if (now - _freeTimes[i] < 1f && now >= _freeTimes[i]) recent++;
            if (recent >= Mathf.Clamp(_sheet.Splash.PerSecond, 1, MaxFreeTimes)) return false;
            _freeTimes[_freeNext] = now; _freeNext = (_freeNext + 1) % MaxFreeTimes;
            return true;
        }

        private bool DripDue(int target, float now)
        {
            int free = 0;
            for (int i = 0; i < MaxDripGates; i++)
            {
                if (_drips[i].Target == target) { if (now < _drips[i].NextAt) return false; free = i; break; }
                if (_drips[i].NextAt < _drips[free].NextAt) free = i;      // the gate that opened longest ago is reused
            }
            _drips[free] = new DripGate { Target = target, NextAt = now + _sheet.Splash.DripCooldown };
            return true;
        }

        // ---- scene reads ----

        private Vector3 CasterFeet(in SpellFxRequest request)
        {
            if (_wiring != null) return ((ISpellCastHost)_wiring).PlayerPosition;
            return request.Follow != null ? request.Follow.position : request.Origin;
        }

        private Vector3 Eye(in SpellFxRequest request)
        {
            Camera camera = _host.ViewCamera;
            return camera != null ? camera.transform.position : CasterFeet(request) + Vector3.up * EyeHeight;
        }

        // the ground under a point (the same reading the layer's runtime takes): not a body that moves, not the struck enemy
        private float GroundY(Vector3 point, float fallback, Transform skip)
        {
            var residue = _profile.Residue;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 4f, Vector3.down, _groundHits, 14f, residue.GroundLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity, height = fallback;
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.collider is CharacterController || hit.rigidbody != null || hit.normal.y < residue.MinNormalY) continue;
                if (skip != null && hit.transform.IsChildOf(skip)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance; height = hit.point.y;
            }
            return height;
        }

        // the glyph's wash: its catalogue profile's pigment when the visual set has one, else the sheet's element colour
        private Color TintOf(char letter, Element element)
        {
            if (_visuals != null && _visuals.TryGet(letter, out var visual) && visual.FxPrefab != null)
            {
                var fx = visual.FxPrefab.GetComponent<Vfx120Effect>();
                if (fx != null && fx.Profile != null) return fx.Profile.Pigment;
            }
            return _sheet.Tint((int)element);
        }

        public string Describe() =>
            "handles=" + LiveCount + "/" + (_live != null ? _live.Length : 0) + " casts=" + ActiveCasts + "/" + (_casts != null ? _casts.Length : 0) + " bodies=" + ActiveBodies + "/" + BodySlots
            + " held=" + _heldTotal + "/" + _heldBudget + " begun=" + Begun + " cues=" + Cues + " ended=" + Ended + " unrouted=" + Unrouted
            + " bursts=" + CastsStarted + " stamps=" + Stamps + " air=" + AirSprites + " shown=" + BodiesShown + " rings=" + RingsShown + " ringsEvicted=" + RingsEvicted + " stateMarks=" + StateMarks + " freeSplash=" + FreeSplashes + " drips=" + Drips
            + " refused(bursts=" + CastsRefused + " bodies=" + BodiesRefused + " splash=" + FreeDropped + ") heldEvicted=" + HeldEvicted + " expired=" + Expired
            + " sheet=" + (SheetIsAsset ? "asset" : "code defaults");

        private void OnDisable() { EndAll(); }

        private void OnDestroy() { ReleaseResources(); }

        /// <summary>Destroys what the stage made (also called by edit-mode tools, where OnDestroy does not run for DontSave objects).</summary>
        public void ReleaseResources()
        {
            if (_casts != null)
                for (int i = 0; i < _casts.Length; i++)
                {
                    if (_casts[i].Runtime == null) continue;
                    _casts[i].Runtime.Dispose();
                    DestroyNow(_casts[i].Runtime.gameObject);
                    _casts[i] = default;
                }
            if (_bodies != null)
                for (int i = 0; i < _bodies.Length; i++)
                {
                    if (_bodies[i] == null) continue;
                    _bodies[i].ReleaseResources();
                    DestroyNow(_bodies[i].gameObject);
                    _bodies[i] = null;
                }
            if (_ownsSheet && _sheet != null) DestroyNow(_sheet);
            _sheet = null; _ownsSheet = false; _configured = false;
        }

        private static void DestroyNow(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
