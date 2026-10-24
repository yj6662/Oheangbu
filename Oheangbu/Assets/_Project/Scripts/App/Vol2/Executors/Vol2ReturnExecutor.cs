using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Everything the 돌아온다 executor did for one cast that the contact log (Vol2CastLog) has no column for: the legs of the trip, the shots it took, the
    /// reflections, the drags, the cleansed materials, the carried one. Read by the lab provider and the fixture through <see cref="Vol2ReturnExecutor.ReportOf"/>.</summary>
    public sealed class Vol2ReturnReport
    {
        public sealed class Intercept
        {
            public long Key; public string Leg = ""; public long AtUs; public bool Reflected; public string Reason = "";
            /// <summary>The shot's power in rule units (HP / HpPerUnit) and what the rules lent from it (x0.5, capped by the total).</summary>
            public decimal Original, Borrowed; public long ShooterId; public bool HadShooter; public float PowerHp;
        }
        public sealed class Drag
        {
            public long TargetId; public string Name = ""; public bool Boss, Airborne; public float Meters; public long DurationUs; public Vector3 From, To; public string Sink = "";
        }
        public CastId Cast;
        public Vol2ShapeKind Shape;
        public long LegUs, OutEndUs, BackStartUs, BackEndUs, TripEndUs;
        public readonly List<Intercept> Intercepts = new List<Intercept>();
        public readonly List<Drag> Drags = new List<Drag>();
        public readonly List<long> Cleansed = new List<long>();
        public readonly List<long> WetRequests = new List<long>();
        public long CarriedId; public Vector3 CarriedFrom, CarriedTo; public string CarryNote = "";
        public decimal BorrowedPaid; public int Reflections;
        public int OrbitRevolutions;
        public float HpPerUnit;
        public int Reflected { get { int n = 0; foreach (var i in Intercepts) if (i.Reflected) n++; return n; } }
    }

    /// <summary>돌아온다 (S5-b slice 7, table rows ㅇ, 20 glyphs): the shape goes out and comes back, and what it is touches along the way.
    /// ㅏ (P5) the narrow path travels to its end (12 m or the first wall) and back; ㅗ (F5) the fan ITSELF travels out and back (its apex moves fan-depth metres); ㅜ (R5) the
    /// ring spreads from radius 2 m to 4 m and shrinks back; ㅓ (A5) goes nowhere: it circles the caster for its 6 s life, a ring of touch that meets what comes close.
    /// What a touch is: the first time the shape touches a body it is a normal first touch (the instant term). The SECOND pass over a body already hit (the way back) pays the
    /// return term once (EffectBudgetV3 ContactKind.Return, 0.35 = 0.7 x 0.5; 앙 0.525 = 0.7 x 0.75) - and the body's action (bind / burn / push / cut / wet) happens only at the first
    /// touch. A5 has no return term (action.eo.return = 0): its contacts are the budget's first and second contact (a later pass of the circle, a separate SwingId per
    /// revolution, at least 0.3 s after the first), three bodies at most. Every shape also takes the enemy shots it touches out of the air (the old seam, ISpellInterceptable308).
    /// The whole trip stays inside timing.return.roundTrip (1.2 s, which is also the budget's life), so no return is refused as "Expired".
    /// Peaks: 앙 (path) on the way back the returning head carries ONE of my materials to the point 2 m ahead of the caster (the first it meets, the farthest along; moved, never remade:
    /// same id, remaining, expiry); 엉 (orbit) a shot the circle touches is turned back at its shooter: the rules lend half its original power (borrowed ledger, three shots at most, 1.0 in all,
    /// apart from the cast's own budget), delivered when the shot arrives; the bodies the circle touches get wet (water's action); 옹 (fan) every body the fan touches is wet, and on
    /// the way back it is pulled toward the caster for at most combat.bind.maxDuration - a boss is exempt, an airborne body is pulled down to the ground; 웅 (ring) the band
    /// cleanses the ENEMY materials it passes over (at most 4; they end Cleansed, with no product), asks the field for the non-combat pollution gate, and notes my materials in the band as wet.</summary>
    public sealed class Vol2ReturnExecutor : IVol2VerbExecutor
    {
        readonly Dictionary<long, Vol2ReturnReport> _reports = new Dictionary<long, Vol2ReturnReport>();
        readonly Queue<long> _order = new Queue<long>();
        public Verb Verb => Verb.Return;
        public string Name => "Return(돌아온다)";
        public Vol2ReturnTuning Tuning { get; }
        /// <summary>Where the enemy shots come from. A fixture / the lab sets its own source; the default is <see cref="Hub"/>.</summary>
        public IVol2ProjectileSource Projectiles { get; set; }
        public Vol2ProjectileHub Hub { get; }

        public Vol2ReturnExecutor(Vol2ReturnTuning tuning = null, IVol2ProjectileSource projectiles = null)
        {
            Tuning = tuning ?? new Vol2ReturnTuning();
            Hub = new Vol2ProjectileHub();
            Projectiles = projectiles ?? Hub;
        }

        public IVol2VerbRun Begin(Vol2CastRun run)
        {
            var report = new Vol2ReturnReport { Cast = run.Cast, HpPerUnit = Tuning.HpPerUnit };
            _reports[run.Cast.Value] = report; _order.Enqueue(run.Cast.Value);
            while (_order.Count > 256) _reports.Remove(_order.Dequeue());
            return new ReturnRun(run, this, report);
        }

        public bool TryGetReport(CastId cast, out Vol2ReturnReport report) => _reports.TryGetValue(cast.Value, out report);
        public Vol2ReturnReport ReportOf(CastId cast) { _reports.TryGetValue(cast.Value, out var r); return r; }

        struct Sub { public long Us; public float P0, P1; public bool Back; }
        sealed class Hit { public IVol2Target Target; public long Rev; public bool Returned, Dragged; }
        struct ShotView { public IVol2Projectile Shot; public Vector3 Prev, Pos; }
        sealed class Pending { public IVol2Target Target; public long Rev; public decimal Units; public long DueUs; public AttackId Attack; public Vol2ContactRecord Rec; public Vector3 Point; public float PowerHp; }

        sealed class ReturnRun : Vol2VerbRunBase
        {
            readonly Vol2ReturnExecutor _ex;
            readonly Vol2ReturnTuning _t;
            readonly Vol2ReturnReport _rep;
            readonly Vol2ShapeKind _kind;
            readonly bool _carry, _reflect, _drag, _cleanse, _hasReturn;
            readonly long _startUs, _legUs, _tripEndUs, _endUs;
            readonly float _fanTravel, _r0, _r1, _pathHalf;
            readonly Dictionary<long, Hit> _hit = new Dictionary<long, Hit>();
            readonly HashSet<long> _refused = new HashSet<long>();
            readonly HashSet<long> _orbitDone = new HashSet<long>();
            readonly HashSet<(int rev, long target)> _passed = new HashSet<(int, long)>();
            readonly Dictionary<int, long> _revSwing = new Dictionary<int, long>();
            readonly List<Sub> _subs = new List<Sub>();
            readonly List<IVol2Projectile> _found = new List<IVol2Projectile>();
            readonly List<ShotView> _shots = new List<ShotView>();
            readonly Dictionary<object, Vector3> _shotPrev = new Dictionary<object, Vector3>();
            readonly Dictionary<object, long> _shotKey = new Dictionary<object, long>();
            readonly HashSet<object> _shotDone = new HashSet<object>();
            readonly List<Pending> _pending = new List<Pending>();
            readonly List<(float key, IVol2Target t)> _touched = new List<(float, IVol2Target)>();
            readonly RaycastHit[] _rays = new RaycastHit[16];
            long _prevUs, _backStartUs, _backEndUs, _prevBackUs;
            float _headEnd, _prevAlong;
            bool _primed, _aborted, _outDone, _backStarted, _backDone, _backPrimed;
            int _carried;

            public ReturnRun(Vol2CastRun run, Vol2ReturnExecutor ex, Vol2ReturnReport rep) : base(run)
            {
                _ex = ex; _t = ex.Tuning; _rep = rep; _kind = run.Spec.Kind;
                char c = run.Glyph.Letter;
                _carry = c == '앙'; _reflect = c == '엉'; _drag = c == '옹'; _cleanse = c == '웅';
                _hasReturn = run.Plan.Term(EffectKind.Return) > 0m;
                _startUs = run.StartUs; _prevUs = _startUs;
                long life = run.Plan.LifeUs;
                _legUs = Math.Max(1, Math.Min(_t.LegUs, (life - _t.TripMarginUs) / 2));
                _tripEndUs = _startUs + 2 * _legUs;
                _endUs = _kind == Vol2ShapeKind.Attachment ? _startUs + life : _tripEndUs;
                _fanTravel = _t.FanTravel > 0f ? _t.FanTravel : run.Spec.FanDepth;
                _r0 = run.Spec.RingRadius; _r1 = (float)Numbers.Get("space.returnRing.outer");
                _pathHalf = run.Spec.PathWidth * 0.5f;
                _rep.Shape = _kind; _rep.LegUs = _legUs; _rep.TripEndUs = _kind == Vol2ShapeKind.Path ? 0 : _tripEndUs;
                _rep.OutEndUs = _kind == Vol2ShapeKind.Fan || _kind == Vol2ShapeKind.Ring ? _startUs + _legUs : 0;
                _rep.BackStartUs = _rep.OutEndUs; _rep.BackEndUs = _rep.TripEndUs;
            }

            // ------------------------------------------------------------------ run interface ------------------------------------------------------------------

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path: return new Vol2PathEmitter(spec, nowUs, false, Host.Wall, Host.Tuning.ChestHeight);
                    case Vol2ShapeKind.Fan:
                    case Vol2ShapeKind.Ring: return new Vol2TimedEmitter(spec, nowUs, 2 * _legUs);
                    default: return new Vol2AttachmentHost(spec, nowUs, Run.Plan.LifeUs);
                }
            }

            public override void Start(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                if (_cleanse) Host.FieldRequest(Run, "pollution gate: cleanse the ring's ground (" + Run.Spec.RingRadius.ToString("0.##", CultureInfo.InvariantCulture) + " m to " + _r1.ToString("0.##", CultureInfo.InvariantCulture) + " m)", f => f.OnCleanse(Run.Cast, Run.Spec.Origin, Run.Spec.RingRadius, _r1));
            }

            public override void OnContact(Vol2ShapeContact c, long nowUs)
            {
                if (_kind == Vol2ShapeKind.Path) FirstTouch(c, nowUs, false);
                else if (_kind == Vol2ShapeKind.Attachment) Pass(c.Target, c.SwingId, nowUs, c.Along);      // a real brush swing: a contact of the same budget (TE-S7-4)
            }

            public override void Tick(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                if (_aborted) return;
                if (_kind == Vol2ShapeKind.Path) TickPath(nowUs, targets); else TickSweep(nowUs, targets);
                DeliverReflects(nowUs);
                _rep.BorrowedPaid = Run.Budget.State.BorrowedPaid; _rep.Reflections = Run.Budget.State.Reflections;
            }

            public override bool Idle(long nowUs)
            {
                if (_aborted) return true;
                if (_pending.Count > 0) return false;
                return _kind == Vol2ShapeKind.Path ? _backStarted && _backDone : _prevUs >= _endUs;
            }

            public override void Abort(long nowUs) { _aborted = true; _pending.Clear(); }

            // ------------------------------------------------------------------ path ------------------------------------------------------------------

            void TickPath(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var pe = (Vol2PathEmitter)Run.Emitter;
                if (!_outDone)
                {
                    float head = pe.HeadAlong;
                    GatherShots();
                    TakePathShots(_prevAlong, head, "out", nowUs);
                    _prevAlong = head;
                    if (pe.Finished) { _outDone = true; StartBack(head); }
                }
                if (_backStarted && !_backDone) SweepBack(nowUs, targets);
            }

            void StartBack(float head)
            {
                _backStarted = true; _headEnd = head;
                long leg = (long)Math.Round(head / Math.Max(0.01f, Run.Spec.PathSpeed) * 1000000.0);
                _backStartUs = _startUs + leg; _backEndUs = _backStartUs + leg; _prevBackUs = _backStartUs;
                _rep.OutEndUs = _backStartUs; _rep.BackStartUs = _backStartUs; _rep.BackEndUs = _backEndUs; _rep.TripEndUs = _backEndUs;
                if (leg <= 0) _backDone = true;
            }

            void SweepBack(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                long tNow = Math.Min(nowUs, _backEndUs);
                if (tNow > _prevBackUs || !_backPrimed)
                {
                    _backPrimed = true;
                    float speed = Run.Spec.PathSpeed;
                    float aPrev = Mathf.Max(0f, _headEnd - speed * Vol2Time.ToSecondsF(_prevBackUs - _backStartUs));
                    float aNow = Mathf.Max(0f, _headEnd - speed * Vol2Time.ToSecondsF(tNow - _backStartUs));
                    _touched.Clear();
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var t = targets[i];
                        if (t == null || !t.Alive || _refused.Contains(t.Id)) continue;
                        if (_hit.TryGetValue(t.Id, out var h) && (h.Rev != t.LifeRevision || h.Returned)) continue;
                        if (Vol2ShapeGeometry.PathStep(Run.Spec.Origin, Run.Spec.Forward, aNow, aPrev, _pathHalf, t.Position, t.Radius, out float entry, out float _)) _touched.Add((entry, t));
                    }
                    _touched.Sort((a, b) => { int c = b.key.CompareTo(a.key); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });      // the returning head meets the farthest first
                    var order = new List<(float key, IVol2Target t)>(_touched);
                    foreach (var (entry, t) in order) Engage(t, nowUs, entry, Run.Spec.Origin + Run.Spec.Forward * entry + Vector3.up * Host.Tuning.ChestHeight, true);
                    if (_carry) TryCarry(aNow, aPrev, nowUs);
                    GatherShots();
                    TakePathShots(aNow, aPrev, "back", nowUs);
                    _prevBackUs = tNow;
                }
                if (tNow >= _backEndUs) _backDone = true;
            }

            void TakePathShots(float aLo, float aHi, string leg, long nowUs)
            {
                float lo = Mathf.Min(aLo, aHi), hi = Mathf.Max(aLo, aHi);
                for (int i = 0; i < _shots.Count; i++)
                {
                    var s = _shots[i];
                    if (_shotDone.Contains(s.Shot.Identity)) continue;
                    bool touch = Vol2ShapeGeometry.PathStep(Run.Spec.Origin, Run.Spec.Forward, lo, hi, _pathHalf, s.Pos, _t.ProjectileRadius, out float _, out float _)
                              || Vol2ShapeGeometry.PathStep(Run.Spec.Origin, Run.Spec.Forward, lo, hi, _pathHalf, s.Prev, _t.ProjectileRadius, out float _, out float _);
                    if (touch) TakeShot(s.Shot, nowUs, leg);
                }
            }

            // ------------------------------------------------------------------ fan, ring, orbit ------------------------------------------------------------------

            float Frac(long x)
            {
                if (x <= 0) return 0f;
                if (x <= _legUs) return (float)((double)x / _legUs);
                if (x <= 2 * _legUs) return 1f - (float)((double)(x - _legUs) / _legUs);
                return 0f;
            }

            float Psi(long us) => Vol2ReturnGeometry.Tau * (float)((us - _startUs) / (double)_t.OrbitRevolutionUs);

            void BuildSubs(long tCur)
            {
                _subs.Clear();
                if (tCur > _prevUs)
                {
                    int n = (int)Math.Max(1, Math.Min(_t.MaxSubsteps, (long)Math.Ceiling((tCur - _prevUs) / (double)Math.Max(1, _t.SubstepUs))));
                    long before = _prevUs;
                    for (int k = 1; k <= n; k++)
                    {
                        long us = k == n ? tCur : _prevUs + (tCur - _prevUs) * k / n;
                        _subs.Add(MakeSub(before, us)); before = us;
                    }
                }
                else if (!_primed) _subs.Add(MakeSub(tCur, tCur));
                _primed = true;
            }

            Sub MakeSub(long before, long us)
            {
                var s = new Sub { Us = us, Back = us - _startUs > _legUs && _kind != Vol2ShapeKind.Attachment };
                switch (_kind)
                {
                    case Vol2ShapeKind.Fan: s.P0 = _fanTravel * Frac(us - _startUs); break;
                    case Vol2ShapeKind.Ring: s.P0 = _r0 + (_r1 - _r0) * Frac(us - _startUs); break;
                    default: s.P0 = Psi(before); s.P1 = Psi(us); break;
                }
                return s;
            }

            void TickSweep(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                long tCur = Math.Min(nowUs, _endUs);
                BuildSubs(tCur);
                if (_subs.Count > 0)
                {
                    GatherShots();
                    for (int i = 0; i < _subs.Count; i++)
                    {
                        var sub = _subs[i];
                        SweepBodies(sub, targets, nowUs);
                        SweepShots(sub, nowUs);
                        if (_cleanse) Cleanse(sub, nowUs);
                    }
                }
                if (tCur > _prevUs) _prevUs = tCur;
            }

            bool BodyTouches(in Sub sub, IVol2Target t, out float key)
            {
                key = 0f;
                switch (_kind)
                {
                    case Vol2ShapeKind.Fan:
                        key = Vol2ReturnGeometry.FlatDistance(Run.Spec.Origin, t.Position);
                        return Vol2ReturnGeometry.FanTouches(Run.Spec, sub.P0, t.Position, t.Radius);
                    case Vol2ShapeKind.Ring:
                        key = Vol2ReturnGeometry.FlatDistance(Run.Spec.Origin, t.Position);
                        return Vol2ReturnGeometry.RingTouches(Run.Spec, sub.P0, t.Previous, t.Position, t.Radius);
                    default:
                        return Vol2ReturnGeometry.OrbitTouches(Run.Spec.Origin, Run.Spec.Forward, _t.OrbitRadius, _t.OrbitHalfWidth, sub.P0, sub.P1, t.Position, t.Radius, out key);
                }
            }

            void SweepBodies(in Sub sub, IReadOnlyList<IVol2Target> targets, long nowUs)
            {
                _touched.Clear();
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.Alive || _refused.Contains(t.Id)) continue;
                    if (_kind == Vol2ShapeKind.Attachment) { if (_orbitDone.Contains(t.Id)) continue; }
                    else if (_hit.TryGetValue(t.Id, out var h) && (h.Rev != t.LifeRevision || h.Returned || !sub.Back)) continue;       // hit before: only the way back can matter
                    if (BodyTouches(sub, t, out float key)) _touched.Add((key, t));
                }
                if (_touched.Count > 0)
                {
                    _touched.Sort((a, b) => { int c = a.key.CompareTo(b.key); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
                    var order = new List<(float key, IVol2Target t)>(_touched);
                    foreach (var (key, t) in order)
                    {
                        if (_kind == Vol2ShapeKind.Attachment) OrbitPass(t, key, nowUs);
                        else Engage(t, nowUs, key, t.Position, sub.Back);
                    }
                }
                if (_drag && sub.Back) DragPending(nowUs);
            }

            void SweepShots(in Sub sub, long nowUs)
            {
                for (int i = 0; i < _shots.Count; i++)
                {
                    var s = _shots[i];
                    if (_shotDone.Contains(s.Shot.Identity)) continue;
                    bool touch;
                    switch (_kind)
                    {
                        case Vol2ShapeKind.Fan: touch = Vol2ReturnGeometry.FanTouches(Run.Spec, sub.P0, s.Pos, _t.ProjectileRadius); break;
                        case Vol2ShapeKind.Ring: touch = Vol2ReturnGeometry.RingTouches(Run.Spec, sub.P0, s.Prev, s.Pos, _t.ProjectileRadius); break;
                        default: touch = Vol2ReturnGeometry.OrbitTouches(Run.Spec.Origin, Run.Spec.Forward, _t.OrbitRadius, _t.OrbitHalfWidth, sub.P0, sub.P1, s.Pos, _t.ProjectileRadius, out float _); break;
                    }
                    if (touch) TakeShot(s.Shot, nowUs, _kind == Vol2ShapeKind.Attachment ? "orbit" : sub.Back ? "back" : "out");
                }
            }

            // ------------------------------------------------------------------ contacts ------------------------------------------------------------------

            /// <summary>The shape touches a body. Never hit: a normal first touch. Already hit and this is the way back: the return, once.</summary>
            void Engage(IVol2Target t, long nowUs, float along, Vector3 point, bool back)
            {
                if (t == null || !t.Alive || _refused.Contains(t.Id)) return;
                if (_hit.TryGetValue(t.Id, out var h))
                {
                    if (h.Rev != t.LifeRevision || !back || h.Returned || !_hasReturn) return;
                    ReturnHit(h, nowUs, along, point);
                    return;
                }
                var c = new Vol2ShapeContact { Shape = _kind, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = nowUs, SwingId = 0, Point = point, Along = along, Note = back ? "back" : "out" };
                FirstTouch(c, nowUs, back);
            }

            void FirstTouch(Vol2ShapeContact c, long nowUs, bool back)
            {
                var t = c.Target;
                if (t == null || _hit.ContainsKey(t.Id)) return;
                var rec = Touch(c, nowUs);
                if (rec.BudgetAccepted && rec.Kind == "Instant")
                {
                    _hit[t.Id] = new Hit { Target = t, Rev = t.LifeRevision, Returned = back };
                    if (_drag && back) DragOne(_hit[t.Id], nowUs);
                }
                else if (!rec.BudgetAccepted) _refused.Add(t.Id);
            }

            void ReturnHit(Hit h, long nowUs, float along, Vector3 point)
            {
                var t = h.Target;
                h.Returned = true;
                var attack = Host.NewAttack(Run, 0, nowUs);
                var c = MakeContact(t, nowUs, 0, along); c.Point = point; c.Note = "back";
                var rec = Host.NewRecord(Run, c, attack, "Return");
                var step = Run.Budget.Pay(attack.Value, ContactKind.Return, EffectKind.Return, 0m, TriggerKind.None, t.Id, t.LifeRevision, 0, nowUs, 0);
                rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                if (step.Accepted)
                {
                    int ordinal = 0;
                    foreach (var cmd in step.Commands)
                    {
                        if (cmd.Effect != EffectKind.Return) continue;
                        Host.Hit(Run, t, rec, cmd.ActionAmount, attack, nowUs, point, 0, ordinal++);
                    }
                }
                Host.NotifyContact(Run.Log, rec);
                if (_drag) DragOne(h, nowUs);
            }

            // ---- the orbit: a pass of the circle over a body, or a real brush swing ----

            void OrbitPass(IVol2Target t, float psiHit, long nowUs)
            {
                int rev = Mathf.Max(0, Mathf.FloorToInt(psiHit / Vol2ReturnGeometry.Tau));
                if (!_passed.Add((rev, t.Id))) return;
                if (!_revSwing.TryGetValue(rev, out long swing)) { swing = Host.Services.Ids.NextSwing().Value; _revSwing[rev] = swing; if (rev + 1 > _rep.OrbitRevolutions) _rep.OrbitRevolutions = rev + 1; }
                Pass(t, swing, nowUs, Vol2ReturnGeometry.FlatDistance(Run.Spec.Origin, t.Position));
            }

            void Pass(IVol2Target t, long swing, long nowUs, float along)
            {
                if (t == null || !t.Alive || _orbitDone.Contains(t.Id)) return;
                var rec = Touch(MakeContact(t, nowUs, swing, along), nowUs);
                if (rec.BudgetAccepted) { if (rec.Kind == "Second") _orbitDone.Add(t.Id); }
                else if (rec.BudgetReason == "TargetCap" || rec.BudgetReason == "Expired" || rec.BudgetReason == "LifeRevisionMismatch" || rec.BudgetReason == "InvalidInput") _orbitDone.Add(t.Id);
            }

            // ------------------------------------------------------------------ enemy shots ------------------------------------------------------------------

            void GatherShots()
            {
                _found.Clear(); _shots.Clear();
                var source = _ex.Projectiles;
                if (source == null) return;
                source.Gather(Host, Run.Spec.Origin, _t.ProjectileReach, _found);
                for (int i = 0; i < _found.Count; i++)
                {
                    var p = _found[i];
                    if (p == null || !p.InFlight) continue;
                    Vector3 pos = p.Position;
                    Vector3 prev = _shotPrev.TryGetValue(p.Identity, out var pv) ? pv : pos;
                    _shotPrev[p.Identity] = pos;
                    _shots.Add(new ShotView { Shot = p, Prev = prev, Pos = pos });
                }
            }

            long KeyOf(IVol2Projectile p)
            {
                if (!_shotKey.TryGetValue(p.Identity, out long key)) { key = Host.Services.Ids.NextMaterial().Value; _shotKey[p.Identity] = key; }
                return key;
            }

            IVol2Target FindTarget(EnemyVitals v)
            {
                if (v == null) return null;
                var all = Host.Targets;
                for (int i = 0; i < all.Count; i++) if (all[i] is Vol2EnemyTarget e && e.Vitals == v) return e;
                return null;
            }

            /// <summary>The shape touched a shot in the air: it is stopped (the old seam). 엉 turns it back at its shooter: the rules lend half its power.</summary>
            void TakeShot(IVol2Projectile p, long nowUs, string leg)
            {
                if (!p.InFlight || _shotDone.Contains(p.Identity)) return;
                var entry = new Vol2ReturnReport.Intercept { Key = KeyOf(p), Leg = leg, AtUs = nowUs, PowerHp = p.PowerHp };
                if (!p.Intercept(Run.Cast.Value)) return;
                _shotDone.Add(p.Identity);
                _rep.Intercepts.Add(entry);
                Run.Log.Notes.Add("shot " + entry.Key + " taken (" + leg + ") at " + nowUs + " us");
                if (_reflect && p.PowerHp > 0f) Reflect(p, entry, nowUs);
            }

            void Reflect(IVol2Projectile p, Vol2ReturnReport.Intercept entry, long nowUs)
            {
                var shooter = FindTarget(p.Shooter);
                decimal units = (decimal)p.PowerHp / (decimal)_t.HpPerUnit;
                entry.Original = units; entry.HadShooter = shooter != null; entry.ShooterId = shooter != null ? shooter.Id : 0;
                var attack = Host.NewAttack(Run, 0, nowUs);
                long tid = shooter != null ? shooter.Id : WorldTargetId;
                long rev = shooter != null ? shooter.LifeRevision : 0;
                var c = new Vol2ShapeContact { Shape = _kind, Target = shooter, TargetId = tid, LifeRevision = rev, NowUs = nowUs, Point = p.Position, Along = Vol2ReturnGeometry.FlatDistance(Run.Spec.Origin, p.Position), Note = "reflect" };
                var rec = Host.NewRecord(Run, c, attack, "Reflect");
                var step = Run.Budget.Pay(attack.Value, ContactKind.Reflect, EffectKind.Return, units, TriggerKind.None, tid, rev, 0, nowUs, entry.Key);
                rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason; rec.MaterialId = entry.Key;
                entry.Reason = step.Reason; entry.Reflected = step.Accepted;
                if (!step.Accepted || step.Commands.Count == 0) { Host.NotifyContact(Run.Log, rec); return; }
                decimal borrowed = step.Commands[0].HpDamage;
                entry.Borrowed = borrowed; rec.Amount = borrowed; rec.Units = borrowed;
                if (shooter == null) { rec.DamageNote = "NoShooter: booked, nothing to hit"; Host.NotifyContact(Run.Log, rec); return; }
                float flight = Vol2ReturnGeometry.FlatDistance(p.Position, shooter.Position) / Mathf.Max(0.1f, _t.ReflectSpeed);
                long delay = Math.Min(_t.ReflectMaxFlightUs, (long)Math.Round(flight * 1000000.0));
                _pending.Add(new Pending { Target = shooter, Rev = rev, Units = borrowed, DueUs = nowUs + delay, Attack = attack, Rec = rec, Point = p.Position, PowerHp = p.PowerHp });
            }

            void DeliverReflects(long nowUs)
            {
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var p = _pending[i];
                    if (nowUs < p.DueUs) continue;
                    _pending.RemoveAt(i);
                    var rec = p.Rec; var t = p.Target;
                    if (!t.Alive || t.LifeRevision != p.Rev) { rec.DamageNote = "TargetGone"; Host.NotifyContact(Run.Log, rec); continue; }
                    if (!Host.Services.Commands.TryBegin(new CommandKey(Host.Services.Ids.ScopeId, CommandRuleKind.Damage, p.Attack.Value, 0))) { rec.DuplicateCommands++; continue; }
                    rec.DamageRequested = true;
                    string unit = " | power " + p.PowerHp.ToString("0.##", CultureInfo.InvariantCulture) + " HP / " + _t.HpPerUnit.ToString("0.##", CultureInfo.InvariantCulture) + " HP per unit (TEST) x0.5 = " + p.Units.ToString("0.#####", CultureInfo.InvariantCulture) + " units";
                    if (Host.Damage == null) rec.DamageNote = "NoDamageSink" + unit;
                    else
                    {
                        var request = new Vol2DamageRequest(Run.Cast, p.Attack, 0, t, Run.Glyph, Run.Body.Element, Run.Body.Instant, p.Units, 1m, p.Units, 1m, true, nowUs, p.Point, Host.Caster != null ? Host.Caster.Anchor : null, Vol2DamageOrigin.Borrowed);
                        var res = Host.Damage.Apply(request);
                        rec.DamageApplied = res.Applied; rec.HpApplied = res.Hp; rec.Killed = res.Killed; rec.DamageNote = res.Reason + unit;
                    }
                    Host.NotifyContact(Run.Log, rec);
                }
            }

            // ------------------------------------------------------------------ 앙: the carry ------------------------------------------------------------------

            void TryCarry(float aLo, float aHi, long nowUs)
            {
                int cap = (int)Numbers.Get("peak.앙.carryCount");
                if (_carried >= cap) return;
                Vol2Material best = null; float bestAlong = -1f;
                foreach (var m in Host.Materials.AliveSnapshot())
                {
                    if (m.FactionId != Vol2Faction.Player || m.Cast == Run.Cast || m.Attached || !m.Alive || nowUs >= m.ExpiresUs) continue;
                    if (!Vol2ReturnGeometry.CorridorEntry(m.Footprint, Run.Spec.Origin, Run.Spec.Forward, aLo, aHi, _pathHalf, nowUs, out float along)) continue;
                    if (best == null || along > bestAlong || (Mathf.Approximately(along, bestAlong) && m.Id.Value < best.Id.Value)) { best = m; bestAlong = along; }
                }
                if (best == null) return;
                float distance = (float)Numbers.Get("peak.앙.destinationDistance");
                Vector3 dest = Run.Spec.Origin + Run.Spec.Forward * distance; dest.y = Run.Spec.Origin.y;
                var fp = Relocate(best.Footprint, dest);
                Vector3 from = best.Footprint.Centre;
                if (fp != null && Host.Materials.TryMove(best.Id, fp, nowUs))
                {
                    _carried++;
                    _rep.CarriedId = best.Id.Value; _rep.CarriedFrom = from; _rep.CarriedTo = dest; _rep.CarryNote = "carried " + best.Id + " at " + nowUs + " us";
                    Run.Log.Notes.Add(_rep.CarryNote + " from (" + from.x.ToString("0.##", CultureInfo.InvariantCulture) + ", " + from.z.ToString("0.##", CultureInfo.InvariantCulture) + ") to (" + dest.x.ToString("0.##", CultureInfo.InvariantCulture) + ", " + dest.z.ToString("0.##", CultureInfo.InvariantCulture) + ")");
                }
                else _rep.CarryNote = "carry of " + best.Id + " refused";
            }

            /// <summary>The same footprint, centred on <paramref name="dest"/>: a disc stays a disc, a fan or a ring is rebuilt there with its own shape (and a fan's growth).</summary>
            Vol2Footprint Relocate(Vol2Footprint fp, Vector3 dest)
            {
                switch (fp.Kind)
                {
                    case Vol2FootprintKind.Disc: return Vol2Footprint.Disc(dest, fp.Radius);
                    case Vol2FootprintKind.Fan: return Vol2Footprint.Fan(Vol2ShapeSpec.Create(Frame.Fan, Numbers, dest, fp.Forward, Host.Tuning), fp.GrowLinear, fp.GrowStartUs, fp.GrowEndUs);
                    case Vol2FootprintKind.Ring: return Vol2Footprint.Ring(Vol2ShapeSpec.Create(Frame.Ring, Numbers, dest, fp.Forward, Host.Tuning));
                    default: return null;
                }
            }

            // ------------------------------------------------------------------ 옹: the drag ------------------------------------------------------------------

            void DragPending(long nowUs)
            {
                var snapshot = new List<Hit>(_hit.Values);
                foreach (var h in snapshot) if (!h.Dragged && h.Target.Alive && h.Target.LifeRevision == h.Rev) DragOne(h, nowUs);
            }

            long Us(string key) => RuleTypesV3.Microseconds(Numbers.Get(key));

            float GroundOf(IVol2Target t, out float groundY)
            {
                if (_t.GroundHeight != null)
                {
                    float g = _t.GroundHeight(t);
                    if (!float.IsNaN(g)) { groundY = g; return t.Position.y - g; }
                }
                Vector3 o = t.Position + Vector3.up * 0.05f;
                int n = Physics.RaycastNonAlloc(o, Vector3.down, _rays, 60f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.PositiveInfinity;
                var anchor = Host.Caster != null ? Host.Caster.Anchor : null;
                for (int i = 0; i < n; i++)
                {
                    var col = _rays[i].collider;
                    if (col == null || col.GetComponentInParent<EnemyVitals>() != null) continue;
                    if (anchor != null && col.transform.IsChildOf(anchor)) continue;
                    if (_rays[i].distance < best) best = _rays[i].distance;
                }
                if (!float.IsPositiveInfinity(best)) { groundY = o.y - best; return t.Position.y - groundY; }
                groundY = Run.Spec.Origin.y;
                return t.Position.y - groundY;
            }

            void DragOne(Hit h, long nowUs)
            {
                if (h.Dragged) return;
                h.Dragged = true;
                var t = h.Target;
                var drag = new Vol2ReturnReport.Drag { TargetId = t.Id, Name = t.Name, From = t.Position, To = t.Position };
                _rep.Drags.Add(drag);
                bool boss = Host.BossTest != null && Host.BossTest(t);
                drag.Boss = boss;
                long cap = boss ? Us("combat.bind.bossExempt") : Us("combat.bind.maxDuration");
                long duration = Math.Min(_tripEndUs - nowUs, cap);
                if (duration <= 0) { drag.Sink = boss ? "boss exempt" : "no time left"; Run.Log.Notes.Add("drag of " + t.Name + ": " + drag.Sink); return; }
                float gap = GroundOf(t, out float groundY);
                bool air = gap > _t.AirborneGap;
                drag.Airborne = air;
                Vector3 pos = t.Position;
                Vector3 flat = pos - Run.Spec.Origin; flat.y = 0f;
                float dist = flat.magnitude;
                Vector3 dir = dist > 1e-3f ? flat / dist : Run.Spec.Forward;
                Vector3 dest = new Vector3(pos.x, air ? groundY : pos.y, pos.z);
                if (dist > _t.DragStopDistance) { dest.x = Run.Spec.Origin.x + dir.x * _t.DragStopDistance; dest.z = Run.Spec.Origin.z + dir.z * _t.DragStopDistance; }
                Vector3 delta = dest - pos;
                float meters = delta.magnitude;
                drag.To = dest; drag.Meters = meters; drag.DurationUs = duration;
                if (meters < 0.05f) { drag.Sink = "already there"; return; }
                var attack = Host.NewAttack(Run, 0, nowUs);
                var rec = Host.NewRecord(Run, MakeContact(t, nowUs, 0, dist), attack, "Drag");
                if (!Host.Services.Commands.TryBegin(new CommandKey(Host.Services.Ids.ScopeId, CommandRuleKind.Status, attack.Value, 1))) { rec.DuplicateCommands++; return; }
                string sink = Host.Status != null
                    ? Host.Status.Apply(new Vol2BodyEffectRequest(Vol2BodyActionKind.Push, Run.Cast, attack, t, nowUs, duration, 1f, delta / meters, meters))
                    : "NoStatusSink";
                drag.Sink = sink;
                rec.BodyEffect = "drag " + meters.ToString("0.##", CultureInfo.InvariantCulture) + "m " + Vol2Time.ToSecondsF(duration).ToString("0.##", CultureInfo.InvariantCulture) + "s" + (air ? " (down from " + gap.ToString("0.##", CultureInfo.InvariantCulture) + "m)" : "") + " [" + sink + "]";
                Host.NotifyContact(Run.Log, rec);
            }

            // ------------------------------------------------------------------ 웅: the cleanse ------------------------------------------------------------------

            void Cleanse(in Sub sub, long nowUs)
            {
                float half = Run.Spec.RingThickness * 0.5f;
                float inner = Mathf.Max(0f, sub.P0 - half), outer = sub.P0 + half;
                int cap = (int)Numbers.Get("peak.웅.cleanseCount");
                var snapshot = Host.Materials.AliveSnapshot();
                var enemy = new List<Vol2Material>();
                foreach (var m in snapshot)
                {
                    if (!m.Alive || m.Attached || nowUs >= m.ExpiresUs || !m.Footprint.OverlapsBand(Run.Spec.Origin, inner, outer)) continue;
                    if (m.FactionId == Vol2Faction.Enemy) enemy.Add(m);
                    else if (m.FactionId == Vol2Faction.Player && m.Cast != Run.Cast && !_rep.WetRequests.Contains(m.Id.Value))
                    {
                        _rep.WetRequests.Add(m.Id.Value);
                        Host.Materials.TrySetWet(m.Id, nowUs + Run.Body.DurationUs, nowUs);      // slice 8: the wet flag exists now (Vol2Material.Wet)
                        Run.Log.Notes.Add("wet request for my material " + m.Id + " (set wet for " + Run.Body.DurationUs + " us)");
                    }
                }
                if (_rep.Cleansed.Count >= cap || enemy.Count == 0) return;
                enemy.Sort((a, b) => { int c = Vol2ReturnGeometry.FlatDistance(Run.Spec.Origin, a.Footprint.Centre).CompareTo(Vol2ReturnGeometry.FlatDistance(Run.Spec.Origin, b.Footprint.Centre)); return c != 0 ? c : a.Id.Value.CompareTo(b.Id.Value); });
                foreach (var m in enemy)
                {
                    if (_rep.Cleansed.Count >= cap) break;
                    if (Host.Materials.TryEnd(m.Id, Vol2MaterialEnd.Cleansed, nowUs)) { _rep.Cleansed.Add(m.Id.Value); Run.Log.Notes.Add("cleansed " + m.Id + " at " + nowUs + " us"); }
                }
            }
        }
    }
}
