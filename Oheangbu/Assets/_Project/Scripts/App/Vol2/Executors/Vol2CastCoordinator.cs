using System;
using System.Collections.Generic;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>One contact of a cast, with everything that came of it. The lab writes these into the CSV; a fixture asserts on them.</summary>
    public sealed class Vol2ContactRecord
    {
        public Vol2ShapeKind Shape;
        public long TargetId, LifeRevision, NowUs, SwingId;
        public string TargetName = "";
        public AttackId Attack;
        public float Along;
        /// <summary>The rules' budget answer (EffectBudgetV3.Step.Reason): FirstContact, FirstTargetOnly, ... "ParryNoEffect" for a ㅓ 한 번.</summary>
        public bool BudgetAccepted;
        public string BudgetReason = "";
        public bool DamageRequested;
        public decimal Units;
        public decimal StatusFactor = 1m;
        public bool DamageApplied;
        public float HpApplied;
        public bool Killed;
        public string DamageNote = "";
        public string BodyEffect = "";
        public int DuplicateCommands;
    }

    /// <summary>One lasting-damage tick (the burn).</summary>
    public sealed class Vol2TickRecord
    {
        public long TargetId, NowUs; public AttackId Attack; public decimal Units; public bool Applied; public float HpApplied; public string Note = "";
    }

    /// <summary>The whole story of one cast: kept by the coordinator after the cast ends (the last 512), read by the lab and by fixtures.</summary>
    public sealed class Vol2CastLog
    {
        public CastId Cast;
        public Glyph Glyph;
        public Vol2TemplateId Template;
        public Vol2BodyActionKind BodyAction;
        public Vol2ShapeKind Shape;
        public Vol2ShapeSpec Spec;
        public IVol2ShapeEmitter Emitter;
        public long StartUs, EndUs;
        /// <summary>Running | Done | Unsupported | InkRefused</summary>
        public string Status = "Running";
        public bool Finished;
        public decimal InkRequested, InkPaid;
        public readonly List<Vol2ContactRecord> Contacts = new List<Vol2ContactRecord>();
        public readonly List<Vol2TickRecord> Ticks = new List<Vol2TickRecord>();
        public bool ParryWindowOpened;
        public long WindowStartUs, WindowEndUs;
        public int SwingReports, SwingDuplicates;
        public int PendingTicks;

        public int ContactCount => Contacts.Count;
        public decimal TotalUnits { get { decimal u = 0; foreach (var c in Contacts) if (c.DamageApplied) u += c.Units; foreach (var t in Ticks) if (t.Applied) u += t.Units; return u; } }
        public float TotalHp { get { float h = 0; foreach (var c in Contacts) h += c.HpApplied; foreach (var t in Ticks) h += t.HpApplied; return h; } }
        public int PaidContacts { get { int n = 0; foreach (var c in Contacts) if (c.BudgetAccepted) n++; return n; } }
    }

    /// <summary>Marks wet bodies for body.water.duration. Keyed by the body and its life: a new life is dry.</summary>
    public sealed class Vol2StatusBook
    {
        readonly Dictionary<(long id, long life), long> _wetUntil = new Dictionary<(long, long), long>();
        public void MarkWet(IVol2Target t, long untilUs) { _wetUntil[(t.Id, t.LifeRevision)] = untilUs; }
        public bool IsWet(IVol2Target t, long nowUs) => _wetUntil.TryGetValue((t.Id, t.LifeRevision), out long until) && nowUs < until;
        public int Count => _wetUntil.Count;
        public void Clear() { _wetUntil.Clear(); }
    }

    /// <summary>S5-b slice 3: CastAccepted -> shape -> contacts -> rules' budget -> damage / body action, for the 한 번 (Verb.Once) glyphs.
    /// The other verbs are answered "Unsupported" (a status on the log, no error) until slices 4 to 8 add their executors.
    /// What it owns: the shape of a cast, the budget State of a cast, the lasting damage ticks, the wet marks. What it does NOT own: the
    /// ink rules, the damage entry, the defence judge (ports, slice 2). One instance per combat scope; no statics.</summary>
    public sealed class Vol2CastCoordinator : IDisposable
    {
        sealed class Active
        {
            public CastId Cast; public Vol2CastDecision Decision; public GlyphRulesV3.Plan Plan;
            public IVol2ShapeEmitter Emitter; public EffectBudgetV3.State State; public Vol2BodyProfile Body; public Vol2CastLog Log;
        }
        sealed class Dot
        {
            public Active Owner; public AttackId Attack; public IVol2Target Target; public long LifeRevision;
            public long NextUs, TickUs; public int Remaining, Index; public decimal UnitsPerTick; public Vector3 Point;
        }

        readonly Vol2Services _services;
        readonly NumbersV3Dto _numbers;
        readonly DefenceNumbersV3Dto _defence;
        readonly IVol2Clock _clock;
        readonly Vol2TargetRegistry _registry;
        readonly IVol2Caster _caster;
        readonly IVol2Obstruction _wall;
        readonly Vol2ShapeTuning _tuning;
        readonly Vol2SwingSignal _swings;
        readonly Dictionary<Body, Vol2BodyProfile> _profiles = new Dictionary<Body, Vol2BodyProfile>();
        readonly List<Active> _active = new List<Active>();
        readonly List<Dot> _dots = new List<Dot>();
        readonly Dictionary<long, Vol2CastLog> _logs = new Dictionary<long, Vol2CastLog>();
        readonly Queue<long> _logOrder = new Queue<long>();
        readonly List<Vol2ShapeContact> _contacts = new List<Vol2ShapeContact>();
        bool _disposed;

        public IVol2DamageSink Damage { get; set; }
        public IVol2StatusSink Status { get; set; }
        public IVol2InkSpender Ink { get; set; }
        public IVol2ParryPort ParryPort { get; set; }
        public Vol2StatusBook Wet { get; } = new Vol2StatusBook();
        public Vol2TargetRegistry Registry => _registry;
        public Vol2SwingSignal Swings => _swings;
        public Vol2ShapeTuning Tuning => _tuning;
        public IReadOnlyList<Vol2CastLog> ActiveLogs { get { var l = new List<Vol2CastLog>(); foreach (var a in _active) l.Add(a.Log); return l; } }
        public int ActiveCount => _active.Count;
        public int UnsupportedCasts { get; private set; }

        public event Action<Vol2CastLog> CastBegan, CastEnded, CastUnsupported;
        public event Action<Vol2CastLog, Vol2ContactRecord> ContactResolved;

        public Vol2CastCoordinator(Vol2Services services, Vol2TargetRegistry registry, IVol2Caster caster, IVol2Obstruction wall,
            IVol2DamageSink damage, IVol2StatusSink status, IVol2InkSpender ink, Vol2ShapeTuning tuning, Vol2SwingSignal swings)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            if (!services.Route.IsVol2 || services.Rulebook == null || !services.Rulebook.Ok) throw new InvalidOperationException("Vol2CastCoordinator needs a Vol2 scope with a loaded rulebook");
            _numbers = services.Rulebook.Numbers; _defence = new DefenceNumbersV3Dto(_numbers); _clock = services.Clock;
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _wall = wall; Damage = damage; Status = status; Ink = ink; _tuning = tuning ?? new Vol2ShapeTuning(); _swings = swings ?? new Vol2SwingSignal();
            foreach (Body b in Enum.GetValues(typeof(Body))) _profiles[b] = Vol2BodyProfile.For(b, _numbers);
            services.Route.Adapter.CastAccepted += OnAccepted;
            _swings.Contact += OnSwing;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _services.Route.Adapter.CastAccepted -= OnAccepted;
            _swings.Contact -= OnSwing;
            _active.Clear(); _dots.Clear();
        }

        public bool TryGetLog(CastId cast, out Vol2CastLog log) => _logs.TryGetValue(cast.Value, out log);
        public Vol2BodyProfile Profile(Body body) => _profiles[body];

        /// <summary>The window slice 2's defence judge asks about.</summary>
        public bool TryGetParryWindow(long nowUs, out Vol2ParryWindow window)
        {
            window = default;
            foreach (var a in _active)
                if (a.Decision.IsParry && a.Emitter is Vol2AttachmentHost host && host.Open(nowUs)) { window = new Vol2ParryWindow(a.Cast, a.Body.Element, host.StartUs, host.EndUs); return true; }
            return false;
        }

        // ---- accept ----

        void OnAccepted(Vol2CastDecision d)
        {
            if (_disposed) return;
            try { Begin(d); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Begin(Vol2CastDecision d)
        {
            var g = d.Glyph; var plan = d.Plan;
            long now = _clock.NowUs;
            var log = new Vol2CastLog { Cast = d.CastId, Glyph = g, Template = d.Template, BodyAction = Vol2BodyProfile.ActionOf(g.Body), StartUs = now, InkRequested = plan.Cost };
            Remember(log);
            if (g.Verb != Verb.Once)
            {
                log.Status = "Unsupported"; log.Finished = true; log.EndUs = now; UnsupportedCasts++;
                CastUnsupported?.Invoke(log); return;
            }
            if (!g.IsParry && plan.Cost > 0m)
            {
                decimal paid = 0m;
                if (Ink != null && !Ink.TrySpend(d.CastId, plan.Cost, out paid)) { log.Status = "InkRefused"; log.Finished = true; log.EndUs = now; return; }
                log.InkPaid = paid;
            }
            var targets = _registry.Refresh();
            var spec = Vol2ShapeSpec.Create(g.Frame, _numbers, _caster.Position, _caster.AimDirection, _tuning);
            IVol2ShapeEmitter emitter;
            switch (spec.Kind)
            {
                case Vol2ShapeKind.Path: emitter = new Vol2PathEmitter(spec, now, plan.FirstOnly, _wall, _tuning.ChestHeight); break;
                case Vol2ShapeKind.Fan: emitter = new Vol2FanEmitter(spec, now); break;
                case Vol2ShapeKind.Ring: emitter = new Vol2RingEmitter(spec, now); break;
                default: emitter = new Vol2AttachmentHost(spec, now, _defence.ParryWindowUs); break;
            }
            var a = new Active { Cast = d.CastId, Decision = d, Plan = plan, Emitter = emitter, Body = _profiles[g.Body], Log = log,
                State = g.IsParry ? null : new EffectBudgetV3.State(d.CastId.Value, plan, now) };
            log.Shape = spec.Kind; log.Spec = spec; log.Emitter = emitter;
            _active.Add(a);
            if (emitter is Vol2AttachmentHost host)
            {
                log.ParryWindowOpened = true; log.WindowStartUs = host.StartUs; log.WindowEndUs = host.EndUs;
                ParryPort?.WindowOpened(new Vol2ParryWindow(a.Cast, a.Body.Element, host.StartUs, host.EndUs));
            }
            CastBegan?.Invoke(log);
            Step(a, now, targets);
            Settle(a, now);
        }

        void Remember(Vol2CastLog log)
        {
            _logs[log.Cast.Value] = log; _logOrder.Enqueue(log.Cast.Value);
            while (_logOrder.Count > 512) _logs.Remove(_logOrder.Dequeue());
        }

        // ---- tick ----

        /// <summary>Once per frame. dt = scaled delta seconds (pause and slow motion follow the rest of combat); time itself is the scope clock.</summary>
        public void Tick(float dt)
        {
            if (_disposed) return;
            long now = _clock.NowUs;
            var targets = _registry.Refresh();
            for (int i = 0; i < _active.Count; i++) Step(_active[i], now, targets);
            TickDots(now);
            for (int i = _active.Count - 1; i >= 0; i--) Settle(_active[i], now);
            Status?.Tick(dt);
        }

        void Step(Active a, long now, IReadOnlyList<IVol2Target> targets)
        {
            var found = new List<Vol2ShapeContact>();
            a.Emitter.Tick(now, targets, found);
            foreach (var c in found) Resolve(a, c, now);
        }

        void Settle(Active a, long now)
        {
            if (a.Emitter is Vol2AttachmentHost host) { a.Log.SwingReports = host.Reports; a.Log.SwingDuplicates = host.Duplicates; }
            if (!a.Emitter.Finished) return;
            int pending = 0; foreach (var dot in _dots) if (dot.Owner == a) pending++;
            a.Log.PendingTicks = pending;
            if (pending > 0) return;
            a.Log.Finished = true; a.Log.Status = "Done"; a.Log.EndUs = now;
            _active.Remove(a);
            _services.Attacks.ReleaseCast(a.Cast);
            CastEnded?.Invoke(a.Log);
        }

        // ---- swings ----

        void OnSwing(Vol2SwingEvent e)
        {
            if (_disposed) return;
            try
            {
                var targets = _registry.Refresh();
                for (int i = 0; i < _active.Count; i++)
                {
                    var a = _active[i];
                    if (!(a.Emitter is Vol2AttachmentHost host)) continue;
                    var found = new List<Vol2ShapeContact>();
                    host.OnSwing(e, targets, found);
                    foreach (var c in found) Resolve(a, c, e.NowUs);
                    a.Log.SwingReports = host.Reports; a.Log.SwingDuplicates = host.Duplicates;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // ---- a contact ----

        void Resolve(Active a, Vol2ShapeContact c, long now)
        {
            var g = a.Decision.Glyph;
            var attack = _services.Ids.NextAttack();
            _services.Attacks.Register(new Vol2AttackMetadata(attack, a.Cast, new SwingId(c.SwingId), g, now));
            var rec = new Vol2ContactRecord { Shape = c.Shape, TargetId = c.TargetId, LifeRevision = c.LifeRevision, NowUs = c.NowUs, SwingId = c.SwingId, Attack = attack, Along = c.Along,
                TargetName = c.Target != null ? c.Target.Name : "" };
            a.Log.Contacts.Add(rec);
            if (g.IsParry) { rec.BudgetReason = "ParryNoEffect"; ContactResolved?.Invoke(a.Log, rec); return; }

            var contact = new Contact(attack.Value, a.Cast.Value, c.TargetId, now, c.SwingId, ContactKind.Touch, EffectKind.Instant, 0m, TriggerKind.None, c.LifeRevision, 0);
            var step = EffectBudgetV3.Apply(a.State, contact, a.Plan);
            a.State = step.State; rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
            if (step.Accepted)
            {
                int ordinal = 0;
                foreach (var cmd in step.Commands)
                {
                    if (cmd.Effect != EffectKind.Instant) continue;     // 한 번 pays the instant term only; the others belong to later verbs
                    if (!_services.Commands.TryBegin(new CommandKey(_services.Ids.ScopeId, CommandRuleKind.Damage, attack.Value, ordinal++))) { rec.DuplicateCommands++; continue; }
                    Hit(a, c, rec, cmd, attack, now);
                    if (_services.Commands.TryBegin(new CommandKey(_services.Ids.ScopeId, CommandRuleKind.Status, attack.Value, 0))) BodyEffect(a, c, rec, attack, now);
                    else rec.DuplicateCommands++;
                }
            }
            ContactResolved?.Invoke(a.Log, rec);
        }

        void Hit(Active a, Vol2ShapeContact c, Vol2ContactRecord rec, EffectBudgetV3.Command cmd, AttackId attack, long now)
        {
            decimal force = _numbers.Get("combat.force.min");
            decimal h = a.Body.Instant, r = cmd.ActionAmount;
            decimal baseUnits = _defence.Damage(h, r, force, false, true, false);      // H x min(R x S, cap); B and K are the entry's, at the hit
            decimal factor = a.Body.WetFactor(c.Target != null && Wet.IsWet(c.Target, now));
            rec.StatusFactor = factor; rec.Units = baseUnits * factor; rec.DamageRequested = true;
            if (Damage == null) { rec.DamageNote = "NoDamageSink"; return; }
            var request = new Vol2DamageRequest(a.Cast, attack, c.SwingId, c.Target, a.Decision.Glyph, a.Body.Element, h, r, force, rec.Units, factor, false, now, c.Point, _caster.Anchor);
            var result = Damage.Apply(request);
            rec.DamageApplied = result.Applied; rec.HpApplied = result.Hp; rec.Killed = result.Killed; rec.DamageNote = result.Reason;
        }

        void BodyEffect(Active a, Vol2ShapeContact c, Vol2ContactRecord rec, AttackId attack, long now)
        {
            var p = a.Body; var t = c.Target;
            Vector3 dir = Vol2ShapeGeometry.FlatUnit(t.Position - a.Log.Spec.Origin);
            long duration = p.DurationUs; float magnitude = 1f, meters = 0f;
            string note;
            switch (p.Action)
            {
                case Vol2BodyActionKind.Bind:
                    magnitude = (float)p.MoveMultiplier; note = "bind x" + magnitude.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); break;
                case Vol2BodyActionKind.Burn:
                    {
                        long tick = Math.Max(1, _tuning.DotTickUs);
                        int ticks = (int)Math.Max(1, p.DurationUs / tick);
                        _dots.Add(new Dot { Owner = a, Attack = attack, Target = t, LifeRevision = c.LifeRevision, NextUs = now + tick, TickUs = tick, Remaining = ticks, UnitsPerTick = p.BurnTotal / ticks, Point = c.Point });
                        note = "burn " + p.BurnTotal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "u/" + ticks + " ticks"; break;
                    }
                case Vol2BodyActionKind.Push:
                    meters = _tuning.PushMeters; duration = Vol2Time.FromSeconds(_tuning.PushSeconds); note = "push " + meters.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "m"; break;
                case Vol2BodyActionKind.Wet:
                    Wet.MarkWet(t, now + p.DurationUs); note = "wet " + Vol2Time.ToSecondsF(p.DurationUs).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s"; break;
                default:
                    note = "cut" + (rec.StatusFactor != 1m ? " wet x" + rec.StatusFactor.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""); break;
            }
            string sinkNote = Status != null ? Status.Apply(new Vol2BodyEffectRequest(p.Action, a.Cast, attack, t, now, duration, magnitude, dir, meters)) : "NoStatusSink";
            rec.BodyEffect = note + (sinkNote.Length > 0 && sinkNote != note ? " [" + sinkNote + "]" : "");
        }

        void TickDots(long now)
        {
            for (int i = _dots.Count - 1; i >= 0; i--)
            {
                var d = _dots[i];
                if (now < d.NextUs) continue;
                var t = d.Target;
                var tick = new Vol2TickRecord { TargetId = t.Id, NowUs = now, Attack = d.Attack };
                bool alive = t.Alive && t.LifeRevision == d.LifeRevision;
                if (!alive) { tick.Note = "TargetGone"; d.Remaining = 0; }
                else
                {
                    var key = new CommandKey(_services.Ids.ScopeId, CommandRuleKind.Damage, d.Attack.Value, 100 + d.Index);
                    if (!_services.Commands.TryBegin(key)) tick.Note = "DuplicateCommand";
                    else
                    {
                        decimal factor = d.Owner.Body.WetFactor(Wet.IsWet(t, now));
                        tick.Units = d.UnitsPerTick * factor;
                        if (Damage == null) tick.Note = "NoDamageSink";
                        else
                        {
                            var request = new Vol2DamageRequest(d.Owner.Cast, d.Attack, 0, t, d.Owner.Decision.Glyph, d.Owner.Body.Element, d.Owner.Body.Instant, 0m, 1m, tick.Units, factor, true, now, d.Point, _caster.Anchor);
                            var res = Damage.Apply(request);
                            tick.Applied = res.Applied; tick.HpApplied = res.Hp; tick.Note = res.Reason;
                        }
                    }
                    d.Index++; d.Remaining--; d.NextUs += d.TickUs;
                }
                d.Owner.Log.Ticks.Add(tick);
                if (d.Remaining <= 0) _dots.RemoveAt(i);
            }
        }
    }
}
