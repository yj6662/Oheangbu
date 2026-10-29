using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Oheangbu.Vol2.Rules
{
    public sealed class Vol2SpellRowDto
    {
        public int Line { get; }
        public string RawText { get; }
        public ReadOnlyCollection<string> Fields { get; }
        public Glyph Glyph { get; }
        public char Letter => Glyph.Letter;
        public Body Body => Glyph.Body;
        public Frame Frame => Glyph.Frame;
        public Verb Verb => Glyph.Verb;
        public SpellKind Kind => Glyph.Kind;
        public decimal Cost { get; }
        public string Definition => Fields[5];
        public string Outline => Fields[6];
        public string Behavior => Fields[7];
        public string Material => Fields[8];
        public string Note => Fields[9];
        public ReadOnlyCollection<EffectTerm> EffectTerms { get; }
        public Vol2SpellRowDto(int line, string raw, IEnumerable<string> fields, Glyph glyph, decimal cost, IEnumerable<EffectTerm> terms)
        { Line = line; RawText = raw; Fields = RuleTypesV3.Freeze(fields); Glyph = glyph; Cost = cost; EffectTerms = RuleTypesV3.Freeze(terms); }
    }
}
