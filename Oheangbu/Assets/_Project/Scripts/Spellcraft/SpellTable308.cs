// PURE308
using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;

namespace Oheangbu.Spellcraft
{
    // #308 dispatch table (SPEC-SPELL-120-308 section 2). One row per glyph of the 120-row vocabulary, imported from
    // Docs/오행부_작도어휘_v0_1.csv plus the rule sheets (Data/Spells/Rules308_*.csv). No UnityEngine.Object in this file:
    // the same types run inside Unity and in the offline runner (Tools/SpellVFX120/spell120_pure308.py).

    public enum SpellCategory { Attack, Install, Parry, Buff, Summon, Ward, Field, Blank }

    // Final consonant of a glyph. Value = Jamo value + 1 (None = no final), same order as the CSV column.
    public enum SpellFinal { None, Giyeok, Nieun, Mieum, Siot, Ieung }

    // Final = the row follows the final-consonant unlock, Open = the row is usable whatever its final (the five summons today),
    // Test = the row follows its final's TEST unlock only: the main-game grant of that final never opens it (a glyph whose
    // world side does not exist yet). Appended last: the serialized value of the other two does not move.
    public enum SpellGateMode { Final, Open, Test }

    // The 36 glyphs that resolve today keep their existing code path; this key picks it. None = a registered handler runs the row.
    public enum SpellLegacyFeature { None, Book, Ward, BuffG, Guk, Giyeok, Mum }

    // Ok = cast. Every other value is the same misfire as before (ink MisfireInkCost, the strokes evaporate).
    public enum SpellResolveStatus { Ok, NotInVocabulary, Blank, Unimplemented, Locked, Unavailable }

    [Serializable]
    public struct SpellParam
    {
        public string Key;
        public float Value;
    }

    // One final consonant's unlock, as data (Data/Spells/Unlock308.csv). EvidenceId = an encounter that must also be defeated.
    [Serializable]
    public struct SpellUnlockRule
    {
        public SpellFinal Final;
        public string LedgerId, EvidenceId;
        public bool GrantedInMain;
    }

    // A parameter key a handler reads through SpellRow.F. The importer and the static check read these declarations.
    public readonly struct SpellParamSpec
    {
        public readonly string Key;
        public readonly float Min, Max, Fallback;
        public readonly bool Required;
        public SpellParamSpec(string key, float min, float max, bool required = true, float fallback = 0f)
        { Key = key; Min = min; Max = max; Required = required; Fallback = fallback; }
        public bool Accepts(float value) => !float.IsNaN(value) && value >= Min && value <= Max;
    }

    [Serializable]
    public sealed class SpellRow
    {
        public string Letter = "";
        public int Index;                     // 1..120 = CSV order = VFX catalogue number (0 = a legacy book entry outside the grid)
        public Element Element;
        public Jamo Medial;
        public SpellFinal Final;
        public SpellCategory Category;
        public SpellKind Kind;
        public string Handler = "";           // "" = declared misfire (blank, reserved or not built yet)
        public string Pending = "";           // work package that owns a reserved row
        public SpellGateMode Gate;
        public SpellLegacyFeature Feature;
        public float BasePower;
        public float ProjectileSpeedMul;
        public AreaShape AreaShape;
        public float AreaAngle, AreaRadius, AreaLength, AreaSpeed, AreaImpactDelay;
        public int VolleyShots;
        public float VolleyInterval;
        public bool ScatterVolley;
        public SpellParam[] Params = Array.Empty<SpellParam>();

        public bool Is(char letter) => !string.IsNullOrEmpty(Letter) && Letter[0] == letter;
        public bool HasHandler => !string.IsNullOrEmpty(Handler);

        // The only way a handler reads a number.
        public float F(string key, float fallback)
        {
            if (Params != null)
                for (int i = 0; i < Params.Length; i++)
                    if (Params[i].Key == key) return Params[i].Value;
            return fallback;
        }

        public bool Has(string key)
        {
            if (Params != null)
                for (int i = 0; i < Params.Length; i++)
                    if (Params[i].Key == key) return true;
            return false;
        }

        public AreaSpec ToAreaSpec()
        {
            return new AreaSpec(AreaShape, AreaAngle, AreaRadius, AreaLength, AreaSpeed, AreaImpactDelay, VolleyShots, VolleyInterval, ScatterVolley);
        }
    }

    // Read-only view over 120 rows in grid order. Built by SpellTableBuilder308 (pure) or wrapped around SpellBookSO._rows.
    public sealed class SpellTable
    {
        private readonly SpellRow[] _rows;
        private readonly SpellUnlockRule[] _unlocks;
        public string SourceHash { get; }
        public string TableHash { get; }
        public IReadOnlyList<SpellRow> Rows => _rows;
        public IReadOnlyList<SpellUnlockRule> Unlocks => _unlocks;

        public SpellTable(SpellRow[] rows, SpellUnlockRule[] unlocks, string sourceHash, string tableHash)
        {
            if (rows == null || rows.Length != SpellGrammar308.Count) throw new ArgumentException("A spell table has exactly 120 rows.");
            _rows = rows; _unlocks = unlocks ?? Array.Empty<SpellUnlockRule>();
            SourceHash = sourceHash ?? ""; TableHash = tableHash ?? "";
        }

        public bool TryGet(char letter, out SpellRow row)
        {
            int index = SpellGrammar308.IndexOf(letter);
            row = index > 0 ? _rows[index - 1] : null;
            if (row != null && row.Is(letter)) return true;
            row = null; return false;
        }

        public bool TryGetUnlock(SpellFinal final, out SpellUnlockRule rule)
        {
            for (int i = 0; i < _unlocks.Length; i++)
                if (_unlocks[i].Final == final) { rule = _unlocks[i]; return true; }
            rule = default; return false;
        }
    }

    // Ok, Locked or Unavailable for a row that has a handler. The resolver never opens a row the gate refuses.
    public interface ISpellGate
    {
        SpellResolveStatus Judge(SpellRow row);
    }

    // Final-consonant unlock policy (SPEC-SPELL-120-308 section 7): the main game grants a final only when the unlock sheet
    // says that final is granted in the main game (today: giyeok alone) AND the saved ledger holds its id (plus the named
    // encounter evidence). A ledger id by itself never opens a final the sheet does not grant, so the unlock order of the
    // main game is a data decision (Unlock308.csv, GrantedInMain), not something a save file can jump. Every final can also
    // be opened by the editor-only TEST unlock, but only in a session whose save file is the isolated test store.
    public static class SpellUnlockPolicy308
    {
        public static bool Unlocked(bool campaignActive, bool grantedInMain, bool ledgerHasId, bool evidenceComplete, bool testUnlock, bool isolatedStore)
            => (campaignActive && grantedInMain && ledgerHasId && evidenceComplete) || (testUnlock && isolatedStore);

        // false = the caller must refuse (throw): a TEST unlock is only accepted while playing in the isolated store.
        public static bool TestUnlockAccepted(bool enable, bool playing, bool isolatedStore)
            => !enable || (playing && isolatedStore);

        // The unlock rule one row is judged with. A TEST-only row (Gate = Test) takes its final's rule without the main-game
        // grant, so a complete main-game proof of that final leaves it locked and only the TEST unlock reaches it. Which row
        // is TEST-only is data (the Gate column of its rule sheet); no glyph is named in code.
        public static SpellUnlockRule RuleFor(SpellRow row, SpellUnlockRule finalRule)
        {
            if (row != null && row.Gate == SpellGateMode.Test) finalRule.GrantedInMain = false;
            return finalRule;
        }

        // New rows: a registered handler, and an open row, a row without a final, or an unlocked final.
        public static SpellResolveStatus JudgeNew(SpellRow row, bool handlerRegistered, bool finalUnlocked)
        {
            if (row == null || !handlerRegistered) return SpellResolveStatus.Unavailable;
            if (row.Gate == SpellGateMode.Open || row.Final == SpellFinal.None || finalUnlocked) return SpellResolveStatus.Ok;
            return SpellResolveStatus.Locked;
        }
    }
}
