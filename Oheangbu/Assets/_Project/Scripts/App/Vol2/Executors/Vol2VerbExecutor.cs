using System;
using System.Collections.Generic;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 4: the verb executor pattern. The coordinator (Vol2CastCoordinator) owns what every cast needs - the shape's spec, the ink, the
    // active list, the lasting-damage ticks, the damage / status ports, the wet marks, the contact log. A VERB is one IVol2VerbExecutor: it
    // answers "which verb" and builds one IVol2VerbRun per accepted cast; the run decides what a contact of its shape means (the budget
    // contact, the damage, the material, the spread) and when it is finished. HOW TO ADD A VERB (slices 5 to 8): write ONE file
    // Executors/Vol2XExecutor.cs with a class implementing IVol2VerbExecutor (Verb = your verb) whose Begin returns a run derived from
    // Vol2VerbRunBase, then add ONE line - `reg.Add(new Vol2XExecutor());` - in Vol2VerbRegistry.CreateDefault(), the single list. Nothing else
    // in the coordinator changes. A verb nobody registered is answered "Unsupported" (a status on the log, no error).

    /// <summary>One verb. Stateless: the state of a cast lives in the run.</summary>
    public interface IVol2VerbExecutor
    {
        Verb Verb { get; }
        string Name { get; }
        /// <summary>Builds the run of one accepted, paid cast. Must not return null (the ink is already taken); a verb that cannot run says so
        /// through its run (Idle at once), not by refusing here.</summary>
        IVol2VerbRun Begin(Vol2CastRun run);
    }

    public interface IVol2VerbRun
    {
        /// <summary>The emitter that carries this cast's shape (the coordinator ticks it and hands its contacts to <see cref="OnContact"/>).</summary>
        IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs);
        /// <summary>After the emitter exists and CastBegan was raised, before the first step.</summary>
        void Start(long nowUs, IReadOnlyList<IVol2Target> targets);
        /// <summary>One contact of the shape (or of a reported swing).</summary>
        void OnContact(Vol2ShapeContact contact, long nowUs);
        /// <summary>Every coordinator tick, after the emitter stepped: materials, delayed passes, footprints.</summary>
        void Tick(long nowUs, IReadOnlyList<IVol2Target> targets);
        /// <summary>true = nothing of this cast is still pending (its materials ended, its delayed passes are done). The cast is Done when its
        /// emitter is finished, it is idle and its lasting-damage ticks are over.</summary>
        bool Idle(long nowUs);
        /// <summary>Scope reset / lab: drop everything pending.</summary>
        void Abort(long nowUs);
    }

    /// <summary>The cast's budget: the rules' EffectBudgetV3 state and the one place a contact is turned into a rules Contact. Every contact of the
    /// cast goes through <see cref="Pay"/>; EventId = the AttackId, so a replayed event is refused by the rules (DuplicateEvent).</summary>
    public sealed class Vol2CastBudget
    {
        readonly GlyphRulesV3.Plan _plan;
        public EffectBudgetV3.State State { get; private set; }
        public Vol2CastBudget(CastId cast, GlyphRulesV3.Plan plan, long startUs) { _plan = plan; State = new EffectBudgetV3.State(cast.Value, plan, startUs); }

        public EffectBudgetV3.Step Pay(long eventId, ContactKind kind, EffectKind effect, decimal amount, TriggerKind trigger, long targetId, long lifeRevision, long swingId, long nowUs, long materialId)
        {
            var contact = new Contact(eventId, State.CastId, targetId, nowUs, swingId, kind, effect, amount, trigger, lifeRevision, materialId);
            var step = EffectBudgetV3.Apply(State, contact, _plan);
            State = step.State;
            return step;
        }

        public decimal Transferred => State.Transferred;
        public decimal PaidShared => State.PaidShared;
        public decimal PaidPerTarget => State.PaidPerTarget;
        public decimal SharedRemaining => State.SharedRemaining;
    }

    /// <summary>Everything one accepted cast is: the decision, the plan, the budget, the body, the emitter, the verb's run and the log.</summary>
    public sealed class Vol2CastRun
    {
        public CastId Cast;
        public Vol2CastDecision Decision;
        public GlyphRulesV3.Plan Plan;
        public Vol2CastCoordinator Host;
        public Vol2BodyProfile Body;
        public Vol2CastLog Log;
        public Vol2ShapeSpec Spec;
        public IVol2ShapeEmitter Emitter;
        public IVol2VerbRun Verb;
        public Vol2CastBudget Budget;
        public long StartUs;
        public Glyph Glyph => Decision.Glyph;
    }

    /// <summary>The common part of the runs of slices 4 to 8: the budget touch (instant / second contact), one body action per (cast, target),
    /// small helpers. A verb overrides what differs.</summary>
    public abstract class Vol2VerbRunBase : IVol2VerbRun
    {
        /// <summary>The TargetId a budget event uses when it belongs to no body (a material put on the ground): the rules want a positive id.</summary>
        public const long WorldTargetId = long.MaxValue;

        protected readonly Vol2CastRun Run;
        readonly HashSet<long> _bodyDone = new HashSet<long>();
        protected Vol2CastCoordinator Host => Run.Host;
        protected NumbersV3Dto Numbers => Run.Host.Numbers;

        protected Vol2VerbRunBase(Vol2CastRun run) { Run = run ?? throw new ArgumentNullException(nameof(run)); }

        public abstract IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs);
        public virtual void Start(long nowUs, IReadOnlyList<IVol2Target> targets) { }
        public abstract void OnContact(Vol2ShapeContact contact, long nowUs);
        public virtual void Tick(long nowUs, IReadOnlyList<IVol2Target> targets) { }
        public virtual bool Idle(long nowUs) => true;
        public virtual void Abort(long nowUs) { }

        /// <summary>A contact for a body that no emitter reported (a footprint test the run made itself).</summary>
        protected Vol2ShapeContact MakeContact(IVol2Target t, long nowUs, long swingId = 0, float along = 0f)
            => new Vol2ShapeContact { Shape = Run.Log.Shape, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = nowUs, SwingId = swingId, Point = t.Position, Along = along };

        /// <summary>The direct touch: the rules' budget (Touch), then for each paid instant / second term one damage request, and the body action
        /// ONCE per (cast, target). Returns the record (also in the log, and announced).</summary>
        protected Vol2ContactRecord Touch(Vol2ShapeContact c, long nowUs)
        {
            var attack = Host.NewAttack(Run, c.SwingId, nowUs);
            var rec = Host.NewRecord(Run, c, attack, "Touch");
            var step = Run.Budget.Pay(attack.Value, ContactKind.Touch, EffectKind.Instant, 0m, TriggerKind.None, c.TargetId, c.LifeRevision, c.SwingId, nowUs, 0);
            rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
            if (step.Accepted)
            {
                int ordinal = 0;
                foreach (var cmd in step.Commands)
                {
                    if (cmd.Effect != EffectKind.Instant && cmd.Effect != EffectKind.Second) continue;     // the later verbs' terms (rear, return ...) are theirs
                    rec.Kind = cmd.Effect == EffectKind.Second ? "Second" : "Instant";
                    Host.Hit(Run, c.Target, rec, cmd.ActionAmount, attack, nowUs, c.Point, c.SwingId, ordinal++);
                    FirstBody(c.Target, rec, attack, nowUs);
                }
            }
            Host.NotifyContact(Run.Log, rec);
            return rec;
        }

        /// <summary>The body's action (bind / burn / push / cut / wet) the first time this cast touches this body; never again for the same body.</summary>
        protected void FirstBody(IVol2Target t, Vol2ContactRecord rec, AttackId attack, long nowUs)
        {
            if (t != null && _bodyDone.Add(t.Id)) ApplyBody(t, rec, attack, nowUs);
        }

        protected virtual void ApplyBody(IVol2Target t, Vol2ContactRecord rec, AttackId attack, long nowUs) { Host.BodyEffect(Run, t, rec, attack, nowUs); }

        protected bool BodyAlreadyApplied(long targetId) => _bodyDone.Contains(targetId);

        protected static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }
    }
}
