using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
namespace Oheangbu.Vol2.Rules
{
    public static class EffectBudgetV3
    {
        public sealed class Target
        {
            public long TargetId { get; internal set; }
            public long LifeRevision { get; internal set; }
            public long FirstUs { get; internal set; } = -1;
            public long FirstSwing { get; internal set; }
            public bool Second { get; internal set; }
            public bool Returned { get; internal set; }
            public bool Planted { get; internal set; }
            public bool Activated { get; internal set; }
            public decimal Paid { get; internal set; }
            internal Target Copy() => (Target)MemberwiseClone();
        }
        public sealed class State
        {
            internal readonly Dictionary<long, Target> targets;
            internal readonly HashSet<long> events;
            internal readonly Dictionary<long, decimal> spreadPaid;
            internal readonly Dictionary<long, int> materialSlots;
            internal readonly decimal[] remaining;
            internal readonly GlyphRulesV3.Plan plan;
            internal long activationSwing;
            public long CastId { get; }
            public long StartedUs { get; }
            public long LastUs { get; internal set; }
            public decimal PaidPerTarget { get; internal set; }
            public decimal PaidShared { get; internal set; }
            public decimal Transferred { get; internal set; }
            public decimal BorrowedPaid { get; internal set; }
            public int Reflections { get; internal set; }
            public decimal TotalPaid => PaidPerTarget + PaidShared;
            public decimal SharedRemaining => remaining.Sum();
            public ReadOnlyCollection<Target> Targets => RuleTypesV3.Freeze(targets.Values.Select(t => t.Copy()));
            public ReadOnlyCollection<long> ConsumedEventIds => RuleTypesV3.Freeze(events.OrderBy(x => x));
            public decimal Remaining(EffectKind kind)
            {
                decimal value = 0; int slot = 0;
                foreach (var term in plan.EffectTerms.Where(t => t.Shared))
                    for (int i = 0; i < term.Count; i++, slot++) if (term.Kind == kind) value += remaining[slot];
                return value;
            }
            public decimal TargetRemaining(long id) => targets.TryGetValue(id, out var t) ? plan.PerTarget - t.Paid : plan.PerTarget;
            public State(long castId, GlyphRulesV3.Plan plan, long startedUs = 0)
            {
                if (castId <= 0 || startedUs < 0 || plan == null || !plan.Numbers.IsValidated) throw new ArgumentException("Invalid cast state");
                CastId = castId; this.plan = plan; StartedUs = startedUs; LastUs = startedUs;
                targets = new Dictionary<long,Target>(); spreadPaid = new Dictionary<long,decimal>(); events = new HashSet<long>(); materialSlots = new Dictionary<long,int>();
                remaining = plan.EffectTerms.Where(t => t.Shared).SelectMany(t => Enumerable.Repeat(t.Amount, t.Count)).ToArray();
            }
            internal State(State s)
            {
                CastId = s.CastId; plan = s.plan; StartedUs = s.StartedUs; LastUs = s.LastUs;
                PaidPerTarget = s.PaidPerTarget; PaidShared = s.PaidShared; Transferred = s.Transferred;
                BorrowedPaid = s.BorrowedPaid; Reflections = s.Reflections; activationSwing = s.activationSwing;
                spreadPaid = new Dictionary<long,decimal>(s.spreadPaid);
                targets = s.targets.ToDictionary(p => p.Key, p => p.Value.Copy()); events = new HashSet<long>(s.events);
                materialSlots = new Dictionary<long,int>(s.materialSlots); remaining = (decimal[])s.remaining.Clone();
            }
        }
        public sealed class Command
        {
            public string Id { get; }
            public long CastId { get; }
            public long TargetId { get; }
            public long LifeRevision { get; }
            public long MaterialId { get; }
            public EffectKind Effect { get; }
            public decimal ActionAmount { get; }
            public decimal HpDamage { get; }
            public decimal UtilityValue { get; }
            public decimal ChildBudget { get; }
            public bool Borrowed { get; }
            internal Command(Contact e, EffectKind effect, decimal amount, decimal bodyDamage, bool transfer = false, bool borrowed = false)
            {
                Id = e.CastId + ":" + e.EventId + ":" + effect;
                CastId = e.CastId; TargetId = e.TargetId; LifeRevision = e.LifeRevision; MaterialId = e.MaterialId;
                Effect = effect; ActionAmount = transfer || borrowed ? 0 : amount; Borrowed = borrowed;
                ChildBudget = transfer ? amount : 0; UtilityValue = effect == EffectKind.Strip && !transfer ? amount : 0;
                HpDamage = transfer || effect == EffectKind.Strip ? 0 : borrowed ? amount : bodyDamage * amount;
            }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            public string Reason { get; }
            public ReadOnlyCollection<Command> Commands { get; }
            internal Step(State s, bool accepted, string reason, IEnumerable<Command> commands)
            { State = s; Accepted = accepted; Reason = reason; Commands = RuleTypesV3.Freeze(commands); }
        }
        public static Step Apply(State s, Contact e, GlyphRulesV3.Plan p)
        {
            var commands = new List<Command>();
            Step Reject(string why) => new Step(s, false, why, commands);
            if (s == null || e == null || p == null) throw new ArgumentNullException();
            if (!ReferenceEquals(s.plan, p) || e.CastId != s.CastId) return Reject("CastMismatch");
            if (e.EventId <= 0 || e.TargetId <= 0 || e.LifeRevision < 0 || e.MaterialId < 0 || e.SwingId < 0 || e.Amount < 0) return Reject("InvalidInput");
            if (s.events.Contains(e.EventId)) return Reject("DuplicateEvent");
            if (e.NowUs < s.LastUs) return Reject("TimeOrder");
            if (e.NowUs - s.StartedUs >= p.LifeUs) return Reject("Expired");
            if (p.Glyph.IsParry) return Reject("ParryOutsideBudget");
            var next = new State(s); next.events.Add(e.EventId); next.LastUs = e.NowUs;
            Step Done(bool accepted, string reason) => new Step(next, accepted, reason, commands);
            var g = p.Glyph; var n = p.Numbers;
            decimal damage = n.Get("body." + RuleTypesV3.BodyKey(g.Body) + ".instant");
            if (e.Kind == ContactKind.Reflect)
            {
                if (g.Letter != '엉') return Done(false, "BorrowedScope");
                if (next.Reflections >= n.Get("peak.엉.reflectionCount")) return Done(false, "ReflectionCount");
                // A projectile can be reflected only once, independent of callback ID.
                if (e.MaterialId == 0 || next.materialSlots.ContainsKey(-e.MaterialId)) return Done(false, "DuplicateProjectile");
                decimal amount = Math.Min(e.Amount * n.Get("combat.borrowed.multiplier"), n.Get("combat.borrowed.reflectTotalCap") - next.BorrowedPaid);
                if (amount <= 0) return Done(false, "BorrowedExhausted");
                next.Reflections++; next.materialSlots[-e.MaterialId] = -1; next.BorrowedPaid += amount;
                commands.Add(new Command(e, EffectKind.Return, amount, damage, borrowed: true)); return Done(true, "Reflected");
            }
            bool transfer = e.Kind == ContactKind.Transfer;
            if (!transfer)
            {
                if (next.targets.TryGetValue(e.TargetId, out var old) && old.LifeRevision != e.LifeRevision) return Done(false, "LifeRevisionMismatch");
                if (!next.targets.ContainsKey(e.TargetId))
                {
                    if (p.Limited && next.targets.Count >= n.Get("action.effectTargetCap")) return Done(false, "TargetCap");
                    if (p.FirstOnly && next.targets.Count > 0) return Done(false, "FirstTargetOnly");
                }
            }
            if (e.Kind == ContactKind.Shared || transfer)
            {
                if (e.Amount <= 0 || e.MaterialId <= 0) return Done(false, "SharedInput");
                if (transfer && e.Effect != EffectKind.Residue) return Done(false, "TransferRequiresMaterial");
                var slots = new List<int>(); int slot = 0;
                foreach (var t in p.EffectTerms.Where(t => t.Shared))
                    for (int i = 0; i < t.Count; i++, slot++) if (t.Kind == e.Effect) slots.Add(slot);
                if (slots.Count == 0) return Done(false, "NoSharedTerm");
                decimal requested = e.Amount;
                bool spreading = !transfer && e.Effect == EffectKind.Spread;
                bool fixedSpread = spreading && (g.Letter == '난' || g.Letter == '넌');
                if (spreading)
                {
                    int cap = (int)n.Get(fixedSpread ? "peak.spread.peakTargets" : "action.spread.targetCap");
                    if (!next.spreadPaid.ContainsKey(e.TargetId) && next.spreadPaid.Count >= cap) return Done(false, "SpreadTargetCap");
                    if (fixedSpread)
                    {
                        next.spreadPaid.TryGetValue(e.TargetId, out decimal paid);
                        requested = Math.Min(requested, n.Get("peak.spread.amountPerTarget") - paid);
                        if (requested <= 0) return Done(false, "SpreadTargetExhausted");
                    }
                }
                decimal amount;
                if (e.Effect == EffectKind.Residue)
                {
                    if (!next.materialSlots.TryGetValue(e.MaterialId, out int selected))
                    {
                        selected = slots.Where(i => !next.materialSlots.Values.Contains(i)).DefaultIfEmpty(-1).First();
                        if (selected < 0) return Done(false, "MaterialCount");
                        next.materialSlots[e.MaterialId] = selected;
                    }
                    if (!slots.Contains(selected)) return Done(false, "MaterialMismatch");
                    amount = Math.Min(requested, next.remaining[selected]); next.remaining[selected] -= amount;
                }
                else
                {
                    amount = Math.Min(requested, slots.Sum(i => next.remaining[i])); decimal pending = amount;
                    foreach (int i in slots) { decimal take = Math.Min(next.remaining[i], pending); next.remaining[i] -= take; pending -= take; }
                }
                if (amount <= 0) return Done(false, "SharedExhausted");
                if (spreading)
                { next.spreadPaid.TryGetValue(e.TargetId, out decimal paid); next.spreadPaid[e.TargetId] = paid + amount; }
                if (transfer) next.Transferred += amount;
                else { EnsureTarget(); next.PaidShared += amount; }
                commands.Add(new Command(e, e.Effect, amount, damage, transfer)); return Done(true, transfer ? "Transferred" : "SharedPaid");
            }
            Target EnsureTarget()
            {
                if (!next.targets.TryGetValue(e.TargetId, out var target))
                { target = new Target { TargetId = e.TargetId, LifeRevision = e.LifeRevision }; next.targets.Add(e.TargetId, target); }
                return target;
            }
            if (g.Verb == Verb.Seed)
            {
                if (e.Kind == ContactKind.Plant)
                {
                    if (next.targets.TryGetValue(e.TargetId, out var existing) && existing.Planted) return Done(false, "AlreadyPlanted");
                    var target = EnsureTarget(); target.Planted = true; target.FirstUs = e.NowUs; target.FirstSwing = e.SwingId;
                    return Done(true, "Planted");
                }
                TriggerKind required = g.Frame switch { Frame.Path => TriggerKind.OwnerAttack, Frame.Attachment => TriggerKind.DamageReceived, Frame.Fan => TriggerKind.EnterFan, _ => TriggerKind.CrossRing };
                if (e.Kind != ContactKind.Activate || e.Trigger != required || e.SwingId == 0 || required == TriggerKind.DamageReceived && e.Amount <= 0) return Done(false, "SeedTrigger");
                if (g.Frame == Frame.Path && (!next.targets.TryGetValue(e.TargetId, out var planted) || !planted.Planted || e.SwingId == planted.FirstSwing || e.NowUs <= planted.FirstUs)) return Done(false, "SeedNotReady");
                if (g.Frame != Frame.Path && next.activationSwing != 0 && next.activationSwing != e.SwingId) return Done(false, "SeedAlreadyTriggered");
                var target2 = EnsureTarget(); if (target2.Activated) return Done(false, "SeedConsumed");
                next.activationSwing = e.SwingId; target2.Activated = true;
                Pay(target2, EffectKind.Activation, p.Term(EffectKind.Activation)); return Done(true, "Activated");
            }
            if (e.Kind == ContactKind.Return)
            {
                if (p.Term(EffectKind.Return) == 0 || next.targets.TryGetValue(e.TargetId, out var oldTarget) && oldTarget.Returned) return Done(false, "NoReturn");
                var target = EnsureTarget(); target.Returned = true; Pay(target, EffectKind.Return, p.Term(EffectKind.Return)); return Done(true, "Returned");
            }
            if (e.Kind != ContactKind.Touch) return Done(false, "ContactKind");
            if (g.Letter == '삿' && next.targets.Values.Any(t => t.FirstUs >= 0 && t.TargetId != e.TargetId)) return Done(false, "FirstDamageOnly");
            var hit = EnsureTarget();
            if (hit.FirstUs < 0)
            {
                hit.FirstUs = e.NowUs; hit.FirstSwing = e.SwingId;
                Pay(hit, EffectKind.Instant, p.Term(EffectKind.Instant));
                if (g.Letter == '섯') Pay(hit, EffectKind.Rear, p.Term(EffectKind.Rear) / n.Get("peak.섯.swings"));
                return Done(true, "FirstContact");
            }
            if (g.Frame != Frame.Attachment || hit.Second || e.SwingId == 0 || e.SwingId == hit.FirstSwing || e.NowUs - hit.FirstUs < RuleTypesV3.Microseconds(n.Get("timing.eo.secondDelay"))) return Done(false, "NoSecondContact");
            hit.Second = true; Pay(hit, EffectKind.Second, p.Term(EffectKind.Second));
            if (g.Letter == '섯') Pay(hit, EffectKind.Rear, p.Term(EffectKind.Rear) / n.Get("peak.섯.swings"));
            return Done(true, "SecondContact");
            void Pay(Target target, EffectKind kind, decimal amount)
            {
                if (amount <= 0) return;
                target.Paid += amount; next.PaidPerTarget += amount; commands.Add(new Command(e, kind, amount, damage));
            }
        }
    }
}
