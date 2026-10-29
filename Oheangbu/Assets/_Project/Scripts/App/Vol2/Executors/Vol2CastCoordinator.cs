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
        /// <summary>What kind of contact it was (slice 4): Touch (a refused or not-yet-paid touch) | Instant | Second | Residue | Spread | ReturnSpread.</summary>
        public string Kind = "Touch";
        /// <summary>The material this contact drew from (Residue) or left behind (Spread / ReturnSpread mark); 0 = none.</summary>
        public long MaterialId;
        /// <summary>The action amount the contact was paid (R of the damage formula); 0 when nothing was paid.</summary>
        public decimal Amount;
        /// <summary>The rules' budget answer (EffectBudgetV3.Step.Reason): FirstContact, FirstTargetOnly, ... "ParryNoEffect" for a ㅓ 한 번; "ResidueShare" for a draw from a residue.</summary>
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
        /// <summary>Running | Done | Unsupported | InkRefused | Recovery(slice2) | Aborted</summary>
        public string Status = "Running";
        public bool Finished;
        public decimal InkRequested, InkPaid;
        public readonly List<Vol2ContactRecord> Contacts = new List<Vol2ContactRecord>();
        public readonly List<Vol2TickRecord> Ticks = new List<Vol2TickRecord>();
        public bool ParryWindowOpened;
        public long WindowStartUs, WindowEndUs;
        public int SwingReports, SwingDuplicates;
        public int PendingTicks;
        /// <summary>Slice 4: every material the cast made (residue, marks, carrier), in order; live objects, so the end reason is read from them.</summary>
        public readonly List<Vol2Material> Materials = new List<Vol2Material>();
        /// <summary>The cast's budget (null for a parry): Transferred, PaidShared, PaidPerTarget, SharedRemaining after the run.</summary>
        public Vol2CastBudget Budget;
        /// <summary>Materials of OTHER casts this cast ended by burning (눈), with the reason: "id:Consumed".</summary>
        public readonly List<string> Burned = new List<string>();
        /// <summary>Materials of other casts this cast stretched (국): "id:+µs".</summary>
        public readonly List<string> Extended = new List<string>();
        /// <summary>Requests to the field (platform, gate) and whether a field port answered or it was only recorded.</summary>
        public readonly List<string> FieldNotes = new List<string>();
        /// <summary>Free-form facts a verb wants on the log (cage start / end, spread rooms ...).</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>D308-156: the ㅜ ward of this cast stands until this scope-clock microsecond (-1 = the cast is no ward).</summary>
        public long WardUntilUs = -1;
        /// <summary>D308-156: what the ward did besides touching bodies - shots taken ("Intercept") and melee hits stopped ("Block"), in order.</summary>
        public readonly List<Vol2WardEvent> WardEvents = new List<Vol2WardEvent>();
        public int WardCount(string kind) { int n = 0; foreach (var e in WardEvents) if (e.Kind == kind) n++; return n; }

        public int ContactCount => Contacts.Count;
        public decimal TotalUnits { get { decimal u = 0; foreach (var c in Contacts) if (c.DamageApplied) u += c.Units; foreach (var t in Ticks) if (t.Applied) u += t.Units; return u; } }
        public float TotalHp { get { float h = 0; foreach (var c in Contacts) h += c.HpApplied; foreach (var t in Ticks) h += t.HpApplied; return h; } }
        public int PaidContacts { get { int n = 0; foreach (var c in Contacts) if (c.BudgetAccepted) n++; return n; } }
        public int Count(string kind) { int n = 0; foreach (var c in Contacts) if (c.Kind == kind && c.BudgetAccepted) n++; return n; }
        public decimal AmountOf(string kind) { decimal a = 0; foreach (var c in Contacts) if (c.Kind == kind && c.BudgetAccepted) a += c.Amount; return a; }
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

    /// <summary>The field's side of the five field glyphs (국's platform, 눈's vine door, 뭄's ground, 숫's rock / iron gate, 웅's pollution). Slice 8 binds the real
    /// one (<see cref="Vol2FieldGateAdapter"/>, over the existing SpellFieldGate308); without a port the coordinator only RECORDS the request in the cast log
    /// (FieldNotes). The two slice 8 methods have default bodies, so a fake of an earlier slice still compiles.</summary>
    public interface IVol2FieldPort
    {
        string OnPlatform(CastId cast, Vector3 centre, float radius, long lifeUs);
        string OnVineBurn(CastId cast, Vol2Material vine);
        /// <summary>숫: the cast cut the rock / iron gate within <paramref name="radius"/> of <paramref name="centre"/> (the ring's centre and radius). The answer is a note for the log.</summary>
        string OnGateCut(CastId cast, Vector3 centre, float radius) => "no gate cut on this field port: recorded only";
        /// <summary>웅: the returning ring cleansed the ground from <paramref name="inner"/> to <paramref name="outer"/> metres around <paramref name="centre"/> (the non-combat pollution gate).</summary>
        string OnCleanse(CastId cast, Vector3 centre, float inner, float outer) => "no cleanse on this field port: recorded only";
    }

    /// <summary>D308-156: a running ward as the coordinator asks it (implemented by Vol2OnceExecutor's ward run). Covers = the ward stands now and the caster is
    /// within space.ring.radius of its centre.</summary>
    public interface IVol2WardRun
    {
        bool Covers(long nowUs, Vector3 casterPosition);
        Vector3 BlockPoint(Vector3 casterPosition, IReadOnlyList<IVol2Target> targets);
        void RecordBlock(long nowUs, Vector3 point, string note);
    }

    /// <summary>S5-b slices 3 and 4: CastAccepted -> shape -> contacts -> rules' budget -> damage / body action / material, for the verbs whose
    /// executor is registered (<see cref="Vol2VerbRegistry"/>). A verb nobody registered is answered "Unsupported" (a status on the log, no error).
    /// 걱 (the recovery) is the defence kit's: the coordinator does not run it, it notes "Recovery(slice2)" and goes on.
    /// What it owns: the shape of a cast, the budget State of a cast, the lasting damage ticks, the wet marks, the material registry. What it does
    /// NOT own: the ink rules, the damage entry, the defence judge (ports, slice 2). One instance per combat scope; no statics.</summary>
    public sealed class Vol2CastCoordinator : IDisposable
    {
        sealed class Dot
        {
            public Vol2CastRun Owner; public AttackId Attack; public IVol2Target Target; public long LifeRevision;
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
        readonly Vol2VerbRegistry _verbs;
        readonly Vol2MaterialRegistry _materials;
        readonly Vol2VerbTuning _verbTuning;
        readonly Dictionary<Body, Vol2BodyProfile> _profiles = new Dictionary<Body, Vol2BodyProfile>();
        readonly List<Vol2CastRun> _active = new List<Vol2CastRun>();
        readonly List<Dot> _dots = new List<Dot>();
        readonly Dictionary<long, Vol2CastLog> _logs = new Dictionary<long, Vol2CastLog>();
        readonly Queue<long> _logOrder = new Queue<long>();
        IReadOnlyList<IVol2Target> _targets = new List<IVol2Target>();
        bool _disposed;

        public IVol2DamageSink Damage { get; set; }
        public IVol2StatusSink Status { get; set; }
        public IVol2InkSpender Ink { get; set; }
        public IVol2ParryPort ParryPort { get; set; }
        public IVol2FieldPort Field { get; set; }
        /// <summary>D308-156: the player's side of the ㅜ ward's melee block (bound by the scope's runtime; null in a fixture = the block is only asked through
        /// <see cref="TryBlockMelee"/>). The ward asks it to bind when a ward begins; it is released with the coordinator.</summary>
        public IVol2MeleeWardPort MeleeWard { get; set; }
        /// <summary>Is this body a boss (bind x0.5 ...)? Default: the enemy's own flag. A fixture sets its own.</summary>
        public Func<IVol2Target, bool> BossTest { get; set; } = t => t is Vol2EnemyTarget e && e.Vitals != null && e.Vitals.IsBoss;
        public Vol2StatusBook Wet { get; } = new Vol2StatusBook();
        /// <summary>S5-b slice 8: the reaction engine of this scope (ticked from <see cref="Tick"/>). Created OFF: <see cref="Vol2ReactionEngine.Enabled"/> is turned on by the
        /// scope's runtime (Vol2ShapeRuntime.CreateForScope) and by the fixtures that test it, so the fixtures of slices 3 to 7 see no reaction.</summary>
        public Vol2ReactionEngine Reactions { get; }
        public Vol2TargetRegistry Registry => _registry;
        public Vol2SwingSignal Swings => _swings;
        public Vol2ShapeTuning Tuning => _tuning;
        public Vol2VerbTuning VerbTuning => _verbTuning;
        public Vol2VerbRegistry Verbs => _verbs;
        public Vol2MaterialRegistry Materials => _materials;
        public Vol2Services Services => _services;
        public NumbersV3Dto Numbers => _numbers;
        public DefenceNumbersV3Dto Defence => _defence;
        public IVol2Caster Caster => _caster;
        public IVol2Obstruction Wall => _wall;
        /// <summary>The candidate bodies of the latest tick / swing.</summary>
        public IReadOnlyList<IVol2Target> Targets => _targets;
        public IReadOnlyList<Vol2CastLog> ActiveLogs { get { var l = new List<Vol2CastLog>(); foreach (var a in _active) l.Add(a.Log); return l; } }
        public int ActiveCount => _active.Count;
        public int UnsupportedCasts { get; private set; }
        public int RecoveryRouted { get; private set; }

        public event Action<Vol2CastLog> CastBegan, CastEnded, CastUnsupported;
        public event Action<Vol2CastLog, Vol2ContactRecord> ContactResolved;
        /// <summary>D308-156: a ward took an enemy shot at this point / stopped a melee hit (drawn at this point). Presentation listens (SpellFxDirector).</summary>
        public event Action<Vol2CastLog, Vector3> ShotIntercepted, MeleeBlocked;
        public int MeleeBlocks { get; private set; }

        public Vol2CastCoordinator(Vol2Services services, Vol2TargetRegistry registry, IVol2Caster caster, IVol2Obstruction wall,
            IVol2DamageSink damage, IVol2StatusSink status, IVol2InkSpender ink, Vol2ShapeTuning tuning, Vol2SwingSignal swings,
            Vol2VerbRegistry verbs = null, Vol2MaterialRegistry materials = null, Vol2VerbTuning verbTuning = null)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            if (!services.Route.IsVol2 || services.Rulebook == null || !services.Rulebook.Ok) throw new InvalidOperationException("Vol2CastCoordinator needs a Vol2 scope with a loaded rulebook");
            _numbers = services.Rulebook.Numbers; _defence = new DefenceNumbersV3Dto(_numbers); _clock = services.Clock;
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _wall = wall; Damage = damage; Status = status; Ink = ink; _tuning = tuning ?? new Vol2ShapeTuning(); _swings = swings ?? new Vol2SwingSignal();
            _verbs = verbs ?? Vol2VerbRegistry.CreateDefault();
            _materials = materials ?? new Vol2MaterialRegistry(_clock, services.Ids);
            _verbTuning = verbTuning ?? new Vol2VerbTuning();
            foreach (Body b in Enum.GetValues(typeof(Body))) _profiles[b] = Vol2BodyProfile.For(b, _numbers);
            Reactions = new Vol2ReactionEngine(this);
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
            Reactions.Dispose();
            try { MeleeWard?.Release(); } catch (Exception e) { Debug.LogException(e); }
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

        /// <summary>Scope reset / the spell lab between rows (design section 12 step 2): every running cast stops (status Aborted), every material
        /// ends with <see cref="Vol2MaterialEnd.Aborted"/>, the lasting damage stops. Nothing of the previous row survives.</summary>
        public void AbortAll()
        {
            long now = _clock.NowUs;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                try { a.Verb.Abort(now); } catch (Exception e) { Debug.LogException(e); }
                a.Log.Finished = true; a.Log.Status = "Aborted"; a.Log.EndUs = now;
                _services.Attacks.ReleaseCast(a.Cast);
            }
            _active.Clear(); _dots.Clear();
            _materials.EndAll(now);
            Reactions.Reset();
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
            if (d.IsRecovery)
            {
                // 걱 is the defence kit's (Vol2RecoveryExecutor): it was paid and begun by the payment gate before CastAccepted. Charging or running it here
                // would pay twice and build an attack out of a recovery.
                log.Status = "Recovery(slice2)"; log.Finished = true; log.EndUs = now; RecoveryRouted++;
                return;
            }
            if (!_verbs.TryGet(g.Verb, out var executor))
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
            _targets = _registry.Refresh();
            var spec = Vol2ShapeSpec.Create(g.Frame, _numbers, _caster.Position, _caster.AimDirection, _tuning);
            var run = new Vol2CastRun
            {
                Cast = d.CastId, Decision = d, Plan = plan, Host = this, Body = _profiles[g.Body], Log = log, Spec = spec, StartUs = now,
                Budget = g.IsParry ? null : new Vol2CastBudget(d.CastId, plan, now)
            };
            run.Verb = executor.Begin(run);
            if (run.Verb == null) { log.Status = "Unsupported"; log.Finished = true; log.EndUs = now; UnsupportedCasts++; CastUnsupported?.Invoke(log); return; }
            // TE-S3-6 (D308-150 V9, TEST): with LaunchAtApex a 한 번 path / fan leaves at the swing's apex (its emitter starts later and reports nothing before it)
            run.Emitter = run.Verb.CreateEmitter(spec, now + _tuning.LaunchDelayUs(g.Frame, g.Verb));
            log.Shape = spec.Kind; log.Spec = spec; log.Emitter = run.Emitter; log.Budget = run.Budget;
            _active.Add(run);
            if (g.IsParry && run.Emitter is Vol2AttachmentHost host)
            {
                log.ParryWindowOpened = true; log.WindowStartUs = host.StartUs; log.WindowEndUs = host.EndUs;
                ParryPort?.WindowOpened(new Vol2ParryWindow(run.Cast, run.Body.Element, host.StartUs, host.EndUs));
            }
            CastBegan?.Invoke(log);
            run.Verb.Start(now, _targets);
            Step(run, now, _targets);
            run.Verb.Tick(now, _targets);
            Reactions.Tick(now, _targets);          // slice 8: the shape's first step and the materials it made are read at once (a fan lands on a standing patch NOW, not next frame)
            Settle(run, now);
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
            _targets = _registry.Refresh();
            _materials.Sweep(now);
            for (int i = 0; i < _active.Count; i++) Step(_active[i], now, _targets);
            for (int i = 0; i < _active.Count; i++) _active[i].Verb.Tick(now, _targets);
            TickDots(now);
            Reactions.Tick(now, _targets);          // slice 8: one single-threaded pass - collect, order, reserve, resolve once, commit as one batch
            for (int i = _active.Count - 1; i >= 0; i--) Settle(_active[i], now);
            Status?.Tick(dt);
        }

        void Step(Vol2CastRun a, long now, IReadOnlyList<IVol2Target> targets)
        {
            var found = new List<Vol2ShapeContact>();
            a.Emitter.Tick(now, targets, found);
            Reactions.OnShapeStep(a, now);          // slice 8: the moving shape's reach this step is a reaction contact against standing materials (before its own body contacts)
            foreach (var c in found) a.Verb.OnContact(c, now);
        }

        void Settle(Vol2CastRun a, long now)
        {
            if (a.Emitter is Vol2AttachmentHost host) { a.Log.SwingReports = host.Reports; a.Log.SwingDuplicates = host.Duplicates; }
            if (!a.Emitter.Finished) return;
            if (!a.Verb.Idle(now)) return;
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
                _targets = _registry.Refresh();
                for (int i = 0; i < _active.Count; i++)
                {
                    var a = _active[i];
                    if (!(a.Emitter is Vol2AttachmentHost host)) continue;
                    var found = new List<Vol2ShapeContact>();
                    host.OnSwing(e, _targets, found);
                    foreach (var c in found) a.Verb.OnContact(c, e.NowUs);
                    a.Log.SwingReports = host.Reports; a.Log.SwingDuplicates = host.Duplicates;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // ---- services for the verb runs ----

        /// <summary>A new AttackId for one actual hit / draw / transfer of a cast, registered with its cast and swing. The id is also the rules' EventId.</summary>
        public AttackId NewAttack(Vol2CastRun a, long swingId, long nowUs)
        {
            var attack = _services.Ids.NextAttack();
            _services.Attacks.Register(new Vol2AttackMetadata(attack, a.Cast, new SwingId(swingId), a.Decision.Glyph, nowUs));
            return attack;
        }

        public Vol2ContactRecord NewRecord(Vol2CastRun a, Vol2ShapeContact c, AttackId attack, string kind)
        {
            var rec = new Vol2ContactRecord
            {
                Shape = c.Shape, TargetId = c.TargetId, LifeRevision = c.LifeRevision, NowUs = c.NowUs, SwingId = c.SwingId, Attack = attack, Along = c.Along,
                TargetName = c.Target != null ? c.Target.Name : "", Kind = kind
            };
            a.Log.Contacts.Add(rec);
            return rec;
        }

        public void NotifyContact(Vol2CastLog log, Vol2ContactRecord rec) { ContactResolved?.Invoke(log, rec); }

        // ---- D308-156: the ㅜ ward ----

        /// <summary>A ward began: the player's melee port asks <see cref="TryBlockMelee"/> from now on (idempotent).</summary>
        public void EnsureMeleeWard()
        {
            try { MeleeWard?.Ensure(TryBlockMelee); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>An enemy melee hit is about to land on the caster: if a standing ward covers the caster NOW (the first running ward that does), the hit is blocked -
        /// recorded on that cast's log and announced (<see cref="MeleeBlocked"/>). No parry, no reward, no groggy: nothing else happens. true = blocked.</summary>
        public bool TryBlockMelee(string note)
        {
            if (_disposed) return false;
            long now = _clock.NowUs;
            Vector3 at = _caster.Position;
            for (int i = 0; i < _active.Count; i++)
            {
                var a = _active[i];
                if (!(a.Verb is IVol2WardRun ward) || !ward.Covers(now, at)) continue;
                Vector3 point = ward.BlockPoint(at, _targets);
                ward.RecordBlock(now, point, note);
                MeleeBlocks++;
                try { MeleeBlocked?.Invoke(a.Log, point); } catch (Exception e) { Debug.LogException(e); }
                return true;
            }
            return false;
        }

        /// <summary>A ward took an enemy shot at <paramref name="point"/> (already recorded on the cast log by the ward): announce it.</summary>
        public void NotifyWardIntercept(Vol2CastRun a, Vector3 point)
        {
            try { ShotIntercepted?.Invoke(a.Log, point); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>One damage request for <paramref name="amount"/> action units (R). The units are H x min(R x S, cap); B and K are the damage entry's, at the hit.</summary>
        public void Hit(Vol2CastRun a, IVol2Target t, Vol2ContactRecord rec, decimal amount, AttackId attack, long now, Vector3 point, long swing, int ordinal)
        {
            if (!_services.Commands.TryBegin(new CommandKey(_services.Ids.ScopeId, CommandRuleKind.Damage, attack.Value, ordinal))) { rec.DuplicateCommands++; return; }
            decimal force = _numbers.Get("combat.force.min");
            decimal h = a.Body.Instant;
            decimal baseUnits = _defence.Damage(h, amount, force, false, true, false);
            decimal factor = a.Body.WetFactor(t != null && Wet.IsWet(t, now));
            rec.Amount = amount; rec.StatusFactor = factor; rec.Units = baseUnits * factor; rec.DamageRequested = true;
            if (Damage == null) { rec.DamageNote = "NoDamageSink"; return; }
            var request = new Vol2DamageRequest(a.Cast, attack, swing, t, a.Decision.Glyph, a.Body.Element, h, amount, force, rec.Units, factor, false, now, point, _caster.Anchor);
            var result = Damage.Apply(request);
            rec.DamageApplied = result.Applied; rec.HpApplied = result.Hp; rec.Killed = result.Killed; rec.DamageNote = result.Reason;
        }

        /// <summary>The body's action on a target (bind / burn / push / cut / wet). <paramref name="bindUs"/> &gt;= 0 overrides a bind's length (the peaks that cap it at 1.5 s).</summary>
        public void BodyEffect(Vol2CastRun a, IVol2Target t, Vol2ContactRecord rec, AttackId attack, long now, long bindUs = -1)
        {
            if (!_services.Commands.TryBegin(new CommandKey(_services.Ids.ScopeId, CommandRuleKind.Status, attack.Value, 0))) { rec.DuplicateCommands++; return; }
            var p = a.Body;
            Vector3 dir = Vol2ShapeGeometry.FlatUnit(t.Position - a.Log.Spec.Origin);
            long duration = p.DurationUs; float magnitude = 1f, meters = 0f;
            string note;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (p.Action)
            {
                case Vol2BodyActionKind.Bind:
                    if (bindUs >= 0) duration = bindUs;
                    magnitude = (float)p.MoveMultiplier; note = "bind x" + magnitude.ToString("0.##", inv); break;
                case Vol2BodyActionKind.Burn:
                    {
                        long tick = Math.Max(1, _tuning.DotTickUs);
                        int ticks = (int)Math.Max(1, p.DurationUs / tick);
                        _dots.Add(new Dot { Owner = a, Attack = attack, Target = t, LifeRevision = t.LifeRevision, NextUs = now + tick, TickUs = tick, Remaining = ticks, UnitsPerTick = p.BurnTotal / ticks, Point = t.Position });
                        note = "burn " + p.BurnTotal.ToString(inv) + "u/" + ticks + " ticks"; break;
                    }
                case Vol2BodyActionKind.Push:
                    meters = _tuning.PushMeters; duration = Vol2Time.FromSeconds(_tuning.PushSeconds); note = "push " + meters.ToString("0.##", inv) + "m"; break;
                case Vol2BodyActionKind.Wet:
                    Wet.MarkWet(t, now + p.DurationUs); note = "wet " + Vol2Time.ToSecondsF(p.DurationUs).ToString("0.##", inv) + "s"; break;
                default:
                    note = "cut" + (rec.StatusFactor != 1m ? " wet x" + rec.StatusFactor.ToString(inv) : ""); break;
            }
            string sinkNote = Status != null ? Status.Apply(new Vol2BodyEffectRequest(p.Action, a.Cast, attack, t, now, duration, magnitude, dir, meters)) : "NoStatusSink";
            rec.BodyEffect = note + (sinkNote.Length > 0 && sinkNote != note ? " [" + sinkNote + "]" : "");
        }

        /// <summary>The field request of a peak (국's platform, 눈's vine door): the field port answers if there is one; the request is ALWAYS written to the log.</summary>
        public string FieldRequest(Vol2CastRun a, string what, Func<IVol2FieldPort, string> ask)
        {
            string answer = Field != null ? ask(Field) : "no field port: recorded only";
            string note = what + " -> " + answer;
            a.Log.FieldNotes.Add(note);
            return note;
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
