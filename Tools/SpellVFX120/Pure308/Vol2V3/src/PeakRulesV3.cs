using System;
namespace Oheangbu.Vol2.Rules
{
    public static class PeakRulesV3
    {
        public sealed class Result
        {
            public decimal StructuralTotal { get; }
            public decimal NormalTotal { get; }
            public decimal StructureForceCap { get; }
            public decimal FinalTotal { get; }
            public decimal FinalMultiplier => NormalTotal == 0 ? 0 : FinalTotal / NormalTotal;
            public bool WithinStructuralCap => StructuralTotal <= StructureForceCap;
            internal Result(decimal structure, decimal normal, decimal cap, decimal total)
            { StructuralTotal = structure; NormalTotal = normal; StructureForceCap = cap; FinalTotal = total; }
        }
        public static Result Evaluate(GlyphRulesV3.Plan p, ContactProfile c, decimal force, bool counter)
        {
            if (p == null || c == null || c.Targets < 0 || c.Contacts < 0 || c.Contacts > 2) throw new ArgumentException("Contact profile");
            var n = p.Numbers;
            if (force < n.Get("combat.force.min") || force > n.Get("combat.force.max")) throw new ArgumentOutOfRangeException(nameof(force));
            decimal structure = 0; int targets = p.EffectiveTargets(c.Targets);
            foreach (var t in p.EffectTerms)
            {
                if (t.Shared) { if (c.Shared && targets > 0) structure += t.Total; continue; }
                decimal count = t.Kind switch { EffectKind.Second => c.Contacts >= 2 ? 1 : 0, EffectKind.Instant => c.Contacts >= 1 ? 1 : 0,
                    EffectKind.Rear => Math.Min(c.Contacts, t.Count), EffectKind.Return => c.Returned ? 1 : 0, EffectKind.Activation => c.Activated ? 1 : 0, _ => t.Count };
                structure += t.Amount * count * targets;
            }
            decimal normal;
            if (p.Glyph.Kind != SpellKind.Peak) normal = structure;
            else
            {
                var g = p.Glyph; string prefix = g.Frame == Frame.Attachment ? "action.eo." : "action.normal.";
                decimal per = n.Get(prefix + RuleTypesV3.VerbKey(g.Verb) + ".perTarget");
                decimal shared = n.Get(prefix + RuleTypesV3.VerbKey(g.Verb) + ".shared");
                if (g.Verb == Verb.Seed) { if (!c.Activated) per = 0; }
                else
                {
                    if (c.Contacts == 0) per -= n.Get("action.instant");
                    if (g.Frame == Frame.Attachment && c.Contacts < 2) per -= n.Get("action.eo.second");
                    if (g.Frame != Frame.Attachment && g.Verb == Verb.Return && !c.Returned) per -= n.Get("action.return.amount");
                }
                normal = per * targets + (c.Shared && targets > 0 ? shared : 0);
            }
            decimal cap = normal * n.Get("combat.peak.structureForceCap");
            decimal output = Math.Min(structure * force, cap) * (counter ? n.Get("combat.counter.multiplier") : 1);
            return new Result(structure, normal, cap, output);
        }
        public sealed class State
        {
            public long WindowId { get; }
            public char PreviousPeak { get; }
            public State(long windowId = 0, char previousPeak = '\0') { WindowId = windowId; PreviousPeak = previousPeak; }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            internal Step(State state, bool accepted) { State = state; Accepted = accepted; }
        }
        public static Step TryCritical(State state, long windowId, GlyphRulesV3.Plan plan)
        {
            if (windowId <= 0) throw new ArgumentOutOfRangeException(nameof(windowId));
            if (state.WindowId == windowId && state.PreviousPeak == plan.Glyph.Letter && plan.Glyph.Kind == SpellKind.Peak) return new Step(state, false);
            return new Step(new State(windowId, plan.Glyph.Kind == SpellKind.Peak ? plan.Glyph.Letter : '\0'), true);
        }
    }
}
