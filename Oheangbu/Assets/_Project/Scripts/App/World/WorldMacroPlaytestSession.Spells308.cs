using System;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 final-consonant unlocks (SPEC-SPELL-120-308 section 7; Q2: the main game keeps its unlock order, the other finals
    // are reachable only through a TEST unlock in an isolated editor save). The session answers "does the player own this
    // final?" from the saved ledger; which ledger id and which encounter evidence a final needs, and whether the main game
    // grants that final at all (GrantedInMain: giyeok only today), is data (SpellBookSO._unlocks, imported from
    // Data/Spells/Unlock308.csv). The existing giyeok and mieum gates (HasDemoGuk, HasMumBridge) are untouched
    // and keep gating the 36 glyphs they gated before; this answer is only asked for rows that run a registered handler.
    public sealed partial class WorldMacroPlaytestSession : ISpellUnlocks
    {
        // Fixed suffix: not one of the timestamped harness suffixes StaleHarnessSuffix307 clears.
        public const string Spell308TestSuffix = "_spell308_test";

        // Read from the store the session opened, so changing TestSaveSuffix later cannot unlock the ordinary save.
        bool Spell308IsolatedStore => store != null && Content != null && store.FilePath != null &&
            store.FilePath.EndsWith(Content.SaveSlot + Spell308TestSuffix + ".json", StringComparison.Ordinal);

#if UNITY_EDITOR
        [NonSerialized] readonly bool[] spell308TestUnlock = new bool[6]; // indexed by SpellFinal

        public bool Spell308TestStore => Spell308IsolatedStore;

        // Read-only, for the editor harness report (Spell308TestUnlock): the file the session opened (name only, "" before
        // Start) and whether the TEST unlock of a final is on. Neither can change a slot or an unlock.
        public string Spell308StoreFileName => store != null && store.FilePath != null ? System.IO.Path.GetFileName(store.FilePath) : "";
        public bool Spell308TestUnlocked(SpellFinal final) => (int)final > 0 && (int)final < spell308TestUnlock.Length && spell308TestUnlock[(int)final];

        // Editor checks only. Refused (throws) unless the session is playing on the isolated store <slot>_spell308_test.json.
        public void SetSpell308TestUnlock(SpellFinal final, bool enabled)
        {
            if (final == SpellFinal.None) throw new ArgumentOutOfRangeException(nameof(final));
            if (!SpellUnlockPolicy308.TestUnlockAccepted(enabled, Application.isPlaying, Spell308IsolatedStore))
                throw new InvalidOperationException("Spell308 test unlock requires a session opened on the isolated store " + Spell308TestSuffix + ".");
            spell308TestUnlock[(int)final] = enabled;
        }
#endif

        public bool FinalUnlocked(SpellUnlockRule rule)
        {
            if (!ready || !isActiveAndEnabled || Progress == null || rule.Final == SpellFinal.None) return false;
            bool ledger = !string.IsNullOrEmpty(rule.LedgerId) && Progress.ledger?.completed != null &&
                Progress.ledger.completed.Contains(rule.LedgerId);
            // the named encounter must be both recorded as evidence and defeated (the mieum precedent, MumBridgeUnlock295)
            bool evidence = string.IsNullOrEmpty(rule.EvidenceId) ||
                (Progress.campaign?.EncounterEvidence != null && Progress.campaign.EncounterEvidence.Contains(rule.EvidenceId) &&
                 Progress.defeated != null && Progress.defeated.Contains(rule.EvidenceId));
            bool test = false;
#if UNITY_EDITOR
            test = spell308TestUnlock[(int)rule.Final];
#endif
            return SpellUnlockPolicy308.Unlocked(DemoCampaignActive, rule.GrantedInMain, ledger, evidence, test, Spell308IsolatedStore);
        }
    }
}
