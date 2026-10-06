// PURE308
using Oheangbu.Core.Domain;

namespace Oheangbu.Spellcraft
{
    // One cell of the 5 x 4 x 6 grid (initial x medial x final).
    public readonly struct SpellGlyph
    {
        public readonly char Letter;
        public readonly int Index;        // 1..120, CSV order
        public readonly Jamo Initial, Medial;
        public readonly SpellFinal Final;
        public SpellGlyph(char letter, int index, Jamo initial, Jamo medial, SpellFinal final)
        { Letter = letter; Index = index; Initial = initial; Medial = medial; Final = final; }
        public Element Element => ElementRelations.FromInitial(Initial);
        public SpellCategory Category => SpellGrammar308.CategoryOf(Initial, Medial, Final);
    }

    // The grammar grid of Spellcraft Bible chapters 3, 12 and 13 as arithmetic on Hangul syllable code points.
    // This is a linguistic fact (like ElementRelations.FromInitial), not balance data: no glyph is named here.
    public static class SpellGrammar308
    {
        public const int Count = 120;
        const int SyllableBase = 0xAC00, VowelCount = 21, TailCount = 28;
        // Positions inside the Unicode syllable block: lead consonants g n m s (ieung), vowels a eo o u, tails none g n m s ng.
        static readonly int[] Lead = { 0, 2, 6, 9, 11 };
        static readonly int[] Vowel = { 0, 4, 8, 13 };
        static readonly int[] Tail = { 0, 1, 4, 16, 19, 21 };

        public static char LetterAt(int index)
        {
            if (index < 1 || index > Count) return '\0';
            int i = index - 1, lead = i / 24, vowel = i % 24 / 6, tail = i % 6;
            return (char)(SyllableBase + (Lead[lead] * VowelCount + Vowel[vowel]) * TailCount + Tail[tail]);
        }

        public static bool TryDecompose(char letter, out SpellGlyph glyph)
        {
            glyph = default;
            int code = letter - SyllableBase;
            if (code < 0 || code >= 19 * VowelCount * TailCount) return false;
            int lead = Find(Lead, code / (VowelCount * TailCount));
            int vowel = Find(Vowel, code / TailCount % VowelCount);
            int tail = Find(Tail, code % TailCount);
            if (lead < 0 || vowel < 0 || tail < 0) return false;
            glyph = new SpellGlyph(letter, lead * 24 + vowel * 6 + tail + 1, (Jamo)lead, (Jamo)((int)Jamo.A + vowel), (SpellFinal)tail);
            return true;
        }

        public static int IndexOf(char letter) => TryDecompose(letter, out var glyph) ? glyph.Index : 0;

        // Same initial and medial without a final (the old resolver's "letter - 1" for a giyeok final).
        public static char BaseOf(char letter)
        {
            if (!TryDecompose(letter, out var glyph)) return '\0';
            return LetterAt(glyph.Index - (int)glyph.Final);
        }

        static int Find(int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++) if (values[i] == value) return i;
            return -1;
        }

        // a = attack (mieum final: harmony install) / eo = parry without a final, buff with one /
        // o = attack (mieum final: summon) / u = ward without a final, field when the final is the initial, blank otherwise.
        public static SpellCategory CategoryOf(Jamo initial, Jamo medial, SpellFinal final)
        {
            switch (medial)
            {
                case Jamo.A: return final == SpellFinal.Mieum ? SpellCategory.Install : SpellCategory.Attack;
                case Jamo.Eo: return final == SpellFinal.None ? SpellCategory.Parry : SpellCategory.Buff;
                case Jamo.O: return final == SpellFinal.Mieum ? SpellCategory.Summon : SpellCategory.Attack;
                default:
                    if (final == SpellFinal.None) return SpellCategory.Ward;
                    return (int)final == (int)initial + 1 ? SpellCategory.Field : SpellCategory.Blank;
            }
        }

        public static SpellKind DefaultKind(SpellCategory category)
        {
            switch (category)
            {
                case SpellCategory.Install: return SpellKind.Install;
                case SpellCategory.Parry: return SpellKind.Parry;
                case SpellCategory.Buff: return SpellKind.Buff;
                case SpellCategory.Summon: return SpellKind.Summon;
                case SpellCategory.Ward: return SpellKind.Ward;
                case SpellCategory.Field: return SpellKind.Field;
                default: return SpellKind.AttackSingle;
            }
        }

        public static bool KindFits(SpellCategory category, SpellKind kind)
        {
            if (category == SpellCategory.Attack) return kind == SpellKind.AttackSingle || kind == SpellKind.AttackArea;
            return category != SpellCategory.Blank && kind == DefaultKind(category);
        }

        // ---- legacy routing without a table (Temporary Exception) ----
        // Books that carry no imported rows (SpellBook_Proto, the audit baseline books, a deploy before the first import) and
        // callers that hand the wiring a bare SpellCast must behave exactly as before. The old resolver named these glyphs
        // in string literals; here the same sixteen cells are described by their jamo. The imported table carries the same
        // facts as data (Feature column, area.spikes / area.seed / trace.pierce), and the offline check compares the two.

        // Ward = the five u glyphs without a final / BuffG = eo + giyeok final / Guk, Mum = the wood and earth field glyphs /
        // Giyeok = a + giyeok final except the earth initial (never wired). Book is decided by the book's own entries.
        public static SpellLegacyFeature LegacyFeatureOf(in SpellGlyph glyph)
        {
            SpellCategory category = glyph.Category;
            if (category == SpellCategory.Ward) return SpellLegacyFeature.Ward;
            if (glyph.Medial == Jamo.Eo && glyph.Final == SpellFinal.Giyeok) return SpellLegacyFeature.BuffG;
            if (category == SpellCategory.Field && glyph.Initial == Jamo.Giyeok) return SpellLegacyFeature.Guk;
            if (category == SpellCategory.Field && glyph.Initial == Jamo.Mieum) return SpellLegacyFeature.Mum;
            if (glyph.Medial == Jamo.A && glyph.Final == SpellFinal.Giyeok && glyph.Initial != Jamo.Mieum) return SpellLegacyFeature.Giyeok;
            return SpellLegacyFeature.None;
        }

        public const string SpikesKey = "area.spikes", SeedKey = "area.seed", PierceKey = "trace.pierce";

        // 1 when the old code special-cased this glyph: staggered spikes (wood circle), a per-cast visual seed (earth path),
        // actor piercing (metal giyeok trace). 0 otherwise.
        public static float LegacyTrait(char letter, string key)
        {
            if (!TryDecompose(letter, out var glyph)) return 0f;
            if (key == SpikesKey) return glyph.Initial == Jamo.Giyeok && glyph.Medial == Jamo.O && glyph.Final == SpellFinal.None ? 1f : 0f;
            if (key == SeedKey) return glyph.Initial == Jamo.Mieum && glyph.Medial == Jamo.O && glyph.Final == SpellFinal.None ? 1f : 0f;
            if (key == PierceKey) return glyph.Initial == Jamo.Siot && glyph.Medial == Jamo.A && glyph.Final == SpellFinal.Giyeok ? 1f : 0f;
            return 0f;
        }
    }
}
