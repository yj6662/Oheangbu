// PURE308
using System;
using System.Collections.Generic;
using System.Text;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;

namespace Oheangbu.EditorTools.WorldMacro
{
    // The resolver as it was before #308, kept word for word as the reference of the equivalence proof
    // (SPEC-SPELL-120-308 A4). This is the ONLY new file allowed to name vocabulary glyphs: it is the old list itself.
    // Used by the offline runner (Tools/SpellVFX120/spell120_pure308.py) and by Spell120Checks308 "table" in the editor.
    public static class SpellOracle308
    {
        public enum Branch { Misfire, Ward, Buff, Guk, Mum, Giyeok, Book }

        public static bool TryResolve(SpellBookSO.Entry[] entries, bool hasBook, char letter, float brush, out SpellCast cast, out Branch branch,
            bool allowGuk, bool allowEABuffs, bool allowWards, bool allowGiyeok, bool allowMum)
        {
            branch = Branch.Misfire;
            int wardIndex = "구누무수우".IndexOf(letter);
            if (hasBook && allowWards && wardIndex >= 0)
            {
                cast = new SpellCast(letter, SpellKind.Ward, (Element)wardIndex, 0f, default, 1f);
                branch = Branch.Ward; return true;
            }
            int buffIndex = "걱넉먹석억".IndexOf(letter);
            if (hasBook && allowEABuffs && buffIndex >= 0)
            {
                cast = new SpellCast(letter, SpellKind.Buff, (Element)buffIndex, 0f, default, 1f);
                branch = Branch.Buff; return true;
            }
            if (hasBook && allowGuk && letter == '국')
            {
                cast = new SpellCast('국', SpellKind.Field, Element.Wood, 0f, default, 1f);
                branch = Branch.Guk; return true;
            }
            if (letter == '뭄')
            {
                if (!hasBook || !allowMum) { cast = default; return false; }
                cast = new SpellCast('뭄', SpellKind.Field, Element.Earth, 0f, default, 1f);
                branch = Branch.Mum; return true;
            }
            char lookup = allowGiyeok && (letter == '각' || letter == '낙' || letter == '삭' || letter == '악') ? (char)(letter - 1) : letter;
            if (!hasBook || !TryGet(entries, lookup, out var entry))
            {
                cast = default;
                return false;
            }
            float speedMul = entry.ProjectileSpeedMul > 0f ? entry.ProjectileSpeedMul : 1f;
            cast = new SpellCast(letter, entry.Kind, entry.Element, entry.BasePower * brush, entry.ToAreaSpec(), speedMul);
            branch = lookup != letter ? Branch.Giyeok : Branch.Book;
            return true;
        }

        // SpellBookSO.TryGet as it is.
        public static bool TryGet(SpellBookSO.Entry[] entries, char letter, out SpellBookSO.Entry entry)
        {
            if (entries != null)
            {
                foreach (var candidate in entries)
                {
                    if (!string.IsNullOrEmpty(candidate.Letter) && candidate.Letter[0] == letter)
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        // The old wiring's glyph special cases, for the trait cross-check.
        public static float OldTrait(char letter, string key)
        {
            if (key == SpellGrammar308.SpikesKey) return letter == '고' ? 1f : 0f;
            if (key == SpellGrammar308.SeedKey) return letter == '모' ? 1f : 0f;
            if (key == SpellGrammar308.PierceKey) return letter == '삭' ? 1f : 0f;
            return 0f;
        }
    }

    // Old resolver against new resolver, glyph by glyph and flag set by flag set.
    public static class SpellEquivalence308
    {
        public const int FlagSets = 32;

        public sealed class Result
        {
            public int Cases, Mismatches, OldResolved, NewResolved;
            public readonly List<string> Details = new List<string>();
            public void Add(Result other)
            {
                Cases += other.Cases; Mismatches += other.Mismatches; OldResolved += other.OldResolved; NewResolved += other.NewResolved;
                foreach (string line in other.Details) if (Details.Count < 40) Details.Add(line);
            }
        }

        // The 120 grid glyphs plus characters outside the vocabulary (a syllable with another final, another initial, Latin, a bare jamo, NUL).
        public static char[] Letters()
        {
            var letters = new List<char>(SpellGrammar308.Count + 6);
            for (int i = 1; i <= SpellGrammar308.Count; i++) letters.Add(SpellGrammar308.LetterAt(i));
            letters.Add((char)(0xAC00 + 8)); letters.Add((char)0xD55C); letters.Add('A'); letters.Add('0'); letters.Add((char)0x3131); letters.Add('\0');
            return letters.ToArray();
        }

        public static void Flags(int set, out bool guk, out bool buffs, out bool wards, out bool giyeok, out bool mum)
        {
            guk = (set & 1) != 0; buffs = (set & 2) != 0; wards = (set & 4) != 0; giyeok = (set & 8) != 0; mum = (set & 16) != 0;
        }

        public static SpellLegacyFeature FeatureOf(SpellOracle308.Branch branch)
        {
            switch (branch)
            {
                case SpellOracle308.Branch.Ward: return SpellLegacyFeature.Ward;
                case SpellOracle308.Branch.Buff: return SpellLegacyFeature.BuffG;
                case SpellOracle308.Branch.Guk: return SpellLegacyFeature.Guk;
                case SpellOracle308.Branch.Mum: return SpellLegacyFeature.Mum;
                case SpellOracle308.Branch.Giyeok: return SpellLegacyFeature.Giyeok;
                case SpellOracle308.Branch.Book: return SpellLegacyFeature.Book;
                default: return SpellLegacyFeature.None;
            }
        }

        // rows: glyph -> the row the new resolver would use (null = none). resolveNew: the new resolver for one glyph and gate.
        // entries / hasBook: what the old resolver read. brush: the multiplier both sides use.
        public static Result Compare(string label, SpellBookSO.Entry[] entries, bool hasBook, float brush, Func<char, SpellRow> rows,
            Func<char, ISpellGate, SpellRow, KeyValuePair<SpellResolveStatus, SpellCast>> resolveNew)
        {
            var result = new Result();
            char[] letters = Letters();
            for (int set = 0; set < FlagSets; set++)
            {
                Flags(set, out bool guk, out bool buffs, out bool wards, out bool giyeok, out bool mum);
                var gate = new LegacySpellGate(guk, buffs, wards, giyeok, mum);
                foreach (char letter in letters)
                {
                    result.Cases++;
                    bool oldOk = SpellOracle308.TryResolve(entries, hasBook, letter, brush, out SpellCast oldCast, out var branch, guk, buffs, wards, giyeok, mum);
                    SpellRow row = rows(letter);
                    var answer = resolveNew(letter, gate, row);
                    bool newOk = answer.Key == SpellResolveStatus.Ok;
                    if (oldOk) result.OldResolved++;
                    if (newOk) result.NewResolved++;
                    string difference = null;
                    if (oldOk != newOk) difference = "old " + (oldOk ? branch.ToString() : "misfire") + " / new " + answer.Key;
                    else if (oldOk)
                    {
                        if (row == null || row.Feature != FeatureOf(branch)) difference = "route old " + branch + " / new " + (row != null ? row.Feature.ToString() : "no row");
                        else difference = CastDifference(oldCast, answer.Value);
                    }
                    if (difference == null) continue;
                    result.Mismatches++;
                    if (result.Details.Count < 40) result.Details.Add(label + " flags " + set + " U+" + ((int)letter).ToString("X4") + ": " + difference);
                }
            }
            return result;
        }

        public static string CastDifference(in SpellCast a, in SpellCast b)
        {
            if (a.Letter != b.Letter) return "letter";
            if (a.Kind != b.Kind) return "kind " + a.Kind + " / " + b.Kind;
            if (a.Element != b.Element) return "element " + a.Element + " / " + b.Element;
            if (!a.Power.Equals(b.Power)) return "power " + a.Power.ToString("R") + " / " + b.Power.ToString("R");
            if (!a.SpeedMul.Equals(b.SpeedMul)) return "speed " + a.SpeedMul.ToString("R") + " / " + b.SpeedMul.ToString("R");
            if (!a.HoldScale.Equals(b.HoldScale)) return "hold";
            var x = a.Area; var y = b.Area;
            if (x.Shape != y.Shape || !x.Angle.Equals(y.Angle) || !x.Radius.Equals(y.Radius) || !x.Length.Equals(y.Length) || !x.Speed.Equals(y.Speed) ||
                !x.ImpactDelay.Equals(y.ImpactDelay) || x.Shots != y.Shots || !x.Interval.Equals(y.Interval) || x.ScatterVolley != y.ScatterVolley)
                return "area " + x.Shape + " / " + y.Shape;
            return null;
        }

        // One line per glyph for one flag set: what the old resolver did, what the new one does.
        public static List<string> BeforeAfter(SpellBookSO.Entry[] entries, float brush, int flagSet, Func<char, SpellRow> rows,
            Func<char, ISpellGate, SpellRow, KeyValuePair<SpellResolveStatus, SpellCast>> resolveNew, out int same)
        {
            var lines = new List<string>(SpellGrammar308.Count); same = 0;
            Flags(flagSet, out bool guk, out bool buffs, out bool wards, out bool giyeok, out bool mum);
            var gate = new LegacySpellGate(guk, buffs, wards, giyeok, mum);
            for (int i = 1; i <= SpellGrammar308.Count; i++)
            {
                char letter = SpellGrammar308.LetterAt(i);
                bool oldOk = SpellOracle308.TryResolve(entries, true, letter, brush, out SpellCast oldCast, out var branch, guk, buffs, wards, giyeok, mum);
                SpellRow row = rows(letter);
                var answer = resolveNew(letter, gate, row);
                bool newOk = answer.Key == SpellResolveStatus.Ok;
                bool equal = oldOk == newOk && (!oldOk || (row != null && row.Feature == FeatureOf(branch) && CastDifference(oldCast, answer.Value) == null));
                if (equal) same++;
                var line = new StringBuilder();
                line.Append(i).Append('\t').Append(letter).Append('\t');
                line.Append(oldOk ? branch + " " + oldCast.Kind + " " + oldCast.Element + " p" + oldCast.Power.ToString("R") + " " + oldCast.AreaShape : "misfire");
                line.Append('\t');
                line.Append(newOk ? row.Feature + " " + answer.Value.Kind + " " + answer.Value.Element + " p" + answer.Value.Power.ToString("R") + " " + answer.Value.AreaShape
                    : "misfire(" + answer.Key + ")");
                line.Append('\t').Append(row != null ? row.Handler : "").Append('\t').Append(equal ? "same" : "DIFFERENT");
                lines.Add(line.ToString());
            }
            return lines;
        }

        // The legacy traits: data (the row) against the old glyph special cases, for every glyph.
        public static int TraitMismatches(Func<char, SpellRow> rows, List<string> details)
        {
            int mismatches = 0;
            foreach (string key in new[] { SpellGrammar308.SpikesKey, SpellGrammar308.SeedKey, SpellGrammar308.PierceKey })
                for (int i = 1; i <= SpellGrammar308.Count; i++)
                {
                    char letter = SpellGrammar308.LetterAt(i);
                    float old = SpellOracle308.OldTrait(letter, key);
                    SpellRow row = rows(letter);
                    float now = row != null ? row.F(key, 0f) : 0f;
                    float grammar = SpellGrammar308.LegacyTrait(letter, key);
                    if (old == now && old == grammar) continue;
                    mismatches++;
                    if (details != null && details.Count < 40) details.Add(key + " cell " + i + ": old " + old + " row " + now + " grammar " + grammar);
                }
            return mismatches;
        }
    }
}
