using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Oheangbu.Vol2.Rules
{
    public static class GlyphRulesV3
    {
        public static bool TryDecode(char letter, out Glyph glyph)
        {
            glyph = default;
            int code = letter - 0xAC00;
            if (code < 0 || code >= 11172) return false;
            int ci = code / 588, ji = code % 588 / 28, fi = code % 28;
            int b = ci switch { 0 => 0, 2 => 1, 6 => 2, 9 => 3, 11 => 4, _ => -1 };
            int f = ji switch { 0 => 0, 4 => 1, 8 => 2, 13 => 3, _ => -1 };
            int v = fi switch { 0 => 0, 1 => 1, 4 => 2, 16 => 3, 19 => 4, 21 => 5, _ => -1 };
            if (b < 0 || f < 0 || v < 0) return false;
            glyph = new Glyph(letter, (Body)b, (Frame)f, (Verb)v); return true;
        }
        public static char Encode(Body body, Frame frame, Verb verb)
        {
            if ((uint)body > 4 || (uint)frame > 3 || (uint)verb > 5) throw new ArgumentOutOfRangeException();
            int c = body switch { Body.Wood => 0, Body.Fire => 2, Body.Earth => 6, Body.Metal => 9, _ => 11 };
            int j = frame switch { Frame.Path => 0, Frame.Attachment => 4, Frame.Fan => 8, _ => 13 };
            int t = verb switch { Verb.Once => 0, Verb.Leave => 1, Verb.Spread => 4, Verb.Seed => 16, Verb.Split => 19, _ => 21 };
            return (char)(0xAC00 + (c * 21 + j) * 28 + t);
        }
        public sealed class Plan
        {
            public Glyph Glyph { get; }
            public bool IsRecovery => Glyph.Letter=='걱'&&EffectTerms.Count==0;
            public NumbersV3Dto Numbers { get; }
            public ReadOnlyCollection<EffectTerm> EffectTerms { get; }
            public decimal Cost { get; }
            public decimal PerTarget { get; }
            public decimal Shared { get; }
            public decimal Total => PerTarget + Shared;
            public bool Limited => !Glyph.IsParry && (Glyph.Frame == Frame.Attachment || Glyph.Frame == Frame.Path && Glyph.Verb == Verb.Seed);
            public bool FirstOnly => Glyph.Frame == Frame.Path && Glyph.Kind != SpellKind.Peak && (Glyph.Verb == Verb.Once || Glyph.Verb == Verb.Split);
            public long LifeUs { get; }
            internal Plan(Glyph glyph, NumbersV3Dto n, IEnumerable<EffectTerm> terms)
            {
                Glyph = glyph; Numbers = n; EffectTerms = RuleTypesV3.Freeze(terms);
                foreach (var t in EffectTerms) { if (t.Shared) Shared += t.Total; else PerTarget += t.Total; }
                string f = RuleTypesV3.FrameKey(glyph.Frame);
                Cost = glyph.IsParry ? 0 : n.Get("ink." + (glyph.Kind == SpellKind.Bare ? "bare." : glyph.Kind == SpellKind.Peak ? "peak." : "normal.") + f);
                string life = glyph.Verb == Verb.Seed ? "timing.seed.life" : glyph.Frame == Frame.Attachment ? "timing.attachment.life" : glyph.Verb == Verb.Spread ? "timing.n.life" : glyph.Verb == Verb.Split && glyph.Frame == Frame.Ring ? "timing.s.ringLife" : glyph.Verb == Verb.Return ? "timing.return.roundTrip" : "timing.g.life";
                LifeUs = RuleTypesV3.Microseconds(n.Get(life));
                if(IsRecovery)
                { var decision=new DefenceNumbersV3Dto(n); Cost=decision.HealingCost; LifeUs=decision.HealingLifeUs; }
            }
            public int EffectiveTargets(int count) => count <= 0 ? 0 : FirstOnly ? 1 : Limited ? Math.Min(count, (int)Numbers.Get("action.effectTargetCap")) : count;
            public decimal TotalFor(int count) => count <= 0 ? 0 : PerTarget * EffectiveTargets(count) + Shared;
            public decimal ValuePerInk(int count) => Cost == 0 ? 0 : Numbers.Get("body." + RuleTypesV3.BodyKey(Glyph.Body) + ".total") * TotalFor(count) / Cost;
            public decimal Term(EffectKind kind) { decimal sum = 0; foreach (var t in EffectTerms) if (t.Kind == kind) sum += t.Total; return sum; }
        }
        public static Plan Build(Glyph g, NumbersV3Dto n)
        {
            if (n == null || !n.IsValidated) throw new InvalidOperationException("Validated import required before casting");
            var plan=BuildCore(g,n);
            // 원본 CSV 대조는 BuildCore, 실행은 결정 D308-145의 회복 경로를 쓴다.
            return g.Letter=='걱'?new Plan(g,n,new EffectTerm[0]):plan;
        }
        internal static Plan BuildCore(Glyph g, NumbersV3Dto n)
        {
            if (!TryDecode(g.Letter, out var actual) || actual.Body != g.Body || actual.Frame != g.Frame || actual.Verb != g.Verb)
                throw new ArgumentException("Glyph fields disagree with Unicode");
            var terms = new List<EffectTerm>();
            void Add(EffectKind k, decimal a, int count = 1) { if (a != 0) terms.Add(new EffectTerm(k, a, count)); }
            decimal instant = n.Get("action.instant");
            bool peak = g.Kind == SpellKind.Peak, eo = g.Frame == Frame.Attachment;
            if (g.IsParry) return new Plan(g, n, terms);
            if (g.Verb == Verb.Once) { Add(EffectKind.Instant, n.Get("action.normal.none.perTarget")); return new Plan(g, n, terms); }
            if (g.Verb != Verb.Seed)
            {
                Add(EffectKind.Instant, instant * (g.Letter == '삿' ? n.Get("peak.삿.firstHitMultiplier") : 1));
                if (eo) Add(EffectKind.Second, n.Get("action.eo.second"));
            }
            switch (g.Verb)
            {
                case Verb.Leave:
                    Add(EffectKind.Residue, n.Get("action.normal.g.shared"));
                    if (g.Letter == '걱') Add(EffectKind.Residue, n.Get("peak.걱.rootAmount"), (int)n.Get("peak.걱.rootCount"));
                    break;
                case Verb.Spread:
                    if (peak && (g.Frame == Frame.Path || eo)) Add(EffectKind.Spread, n.Get("peak.spread.amountPerTarget"), (int)n.Get("peak.spread.peakTargets"));
                    else Add(EffectKind.Spread, n.Get("action.spread.total"));
                    if (g.Letter == '논') Add(EffectKind.ReturnSpread, n.Get("peak.논.returnSpread"));
                    break;
                case Verb.Seed:
                    Add(EffectKind.Activation, n.Get("action.seed.activation") * (g.Letter == '몸' ? n.Get("peak.몸.activationMultiplier") : 1)); break;
                case Verb.Split:
                    if (g.Letter == '솟') Add(EffectKind.Residue, n.Get("peak.솟.residueAmount"));
                    Add(EffectKind.Strip, n.Get("action.strip.utility"));
                    if (g.Letter == '섯') Add(EffectKind.Rear, instant * n.Get("peak.섯.rearMultiplier"), (int)n.Get("peak.섯.swings"));
                    break;
                case Verb.Return:
                    if (!eo) Add(EffectKind.Return, instant * n.Get(g.Letter == '앙' ? "action.return.peakMultiplier" : "action.return.multiplier")); break;
            }
            return new Plan(g, n, terms);
        }
    }
}
