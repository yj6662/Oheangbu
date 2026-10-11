using System;
using System.Collections.Generic;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308b final-consonant grants and the presentation switch (SPEC-SPELL-120-308 "받침 지급 #308b", D308-51 · D308-53) [TEST].
    // The chain (which realm boss grants which final, in earning order) is data: Content.FinalGrants. The order rule is
    // FinalGrantRule308 (pure). The proof of a defeat is campaign.EncounterEvidence (durable: it survives rest respawns).
    // This file only reads the chain, applies the rule to a detached proposal and commits it through the session's ordinary
    // interaction commit: no save file of its own, no static state. An entry without a boss id grants nothing.
    public sealed partial class WorldMacroPlaytestSession
    {
        // A presentation build started with this argument counts every final as owned (same as Content.DemoAllFinals).
        public const string DemoAllFinalsArgument = "-demoAllFinals";
        [NonSerialized] int demoAllFinalsArgument;   // 0 = not read yet, 1 = absent, 2 = present (instance state: nothing static to reset)
        [NonSerialized] string[] finalGrantLedgerIds, finalGrantBossIds;
        [NonSerialized] Oheangbu.Data.World.WorldMacroPlaytestSO finalGrantSource;
        [NonSerialized] float nextFinalGrantCheck;

        // The switch never touches Progress or the save: it only changes what this session answers while it runs.
        public bool DemoAllFinals308
        {
            get
            {
                if (Content != null && Content.DemoAllFinals) return true;
                if (demoAllFinalsArgument == 0)
                    demoAllFinalsArgument = Array.IndexOf(Environment.GetCommandLineArgs(), DemoAllFinalsArgument) >= 0 ? 2 : 1;
                return demoAllFinalsArgument == 2;
            }
        }

        // The chain as the rule reads it; built once per content asset. false = this content grants nothing.
        bool FinalGrantChain308()
        {
            if (Content == null || Content.FinalGrants == null || Content.FinalGrants.Length == 0 || !DemoCampaignActive) return false;
            if (finalGrantSource != Content || finalGrantLedgerIds == null || finalGrantLedgerIds.Length != Content.FinalGrants.Length)
            {
                int count = Content.FinalGrants.Length;
                finalGrantLedgerIds = new string[count]; finalGrantBossIds = new string[count];
                for (int i = 0; i < count; i++)
                {
                    finalGrantLedgerIds[i] = Content.FinalGrants[i] != null ? Content.FinalGrants[i].LedgerId : "";
                    finalGrantBossIds[i] = Content.FinalGrants[i] != null ? Content.FinalGrants[i].BossId : "";
                }
                finalGrantSource = Content;
            }
            return true;
        }

        // Adds every due ledger id to a detached proposal (the caller commits it): called on a defeat proposal, so the
        // defeat and the final it earns are one save. Returns the granted chain indexes, or null.
        List<int> ApplyFinalGrants308(WorldMacroProgress proposal)
        {
            var ledger = proposal?.ledger?.completed; var proofs = proposal?.campaign?.EncounterEvidence;
            if (!FinalGrantChain308() || FinalGrantRule308.Pending(finalGrantLedgerIds, finalGrantBossIds, ledger, proofs) == 0) return null;
            var granted = new List<int>();
            FinalGrantRule308.Pending(finalGrantLedgerIds, finalGrantBossIds, ledger, proofs, granted);
            foreach (int index in granted) ledger.Add(finalGrantLedgerIds[index]);
            return granted;
        }

        // After the commit succeeded: the receipts of the data, on the session's ordinary notice line.
        void AnnounceFinalGrants308(List<int> granted)
        {
            if (granted == null) return;
            string text = "";
            foreach (int index in granted)
            {
                string notice = Content.FinalGrants[index].Notice;
                if (!string.IsNullOrEmpty(notice)) text += (text.Length > 0 ? "\n" : "") + notice;
            }
            if (text.Length > 0) Show(text);
        }

        // Catch-up, from the session's per-frame defeat flush (about once a second): a boss whose defeat was committed by
        // another route, a save that proved the boss before this rule existed, or an earlier final earned after its
        // successor's boss fell. Waits while a witnessed death is still uncommitted; a failed commit is asked again later.
        void TickFinalGrants308()
        {
            if (Time.unscaledTime < nextFinalGrantCheck) return;
            nextFinalGrantCheck = Time.unscaledTime + 1f;
            if (Progress == null || HasPendingDefeats || encounterDeathPending || SaveBlocked || vitals == null || vitals.Hp01 <= 0 || !FinalGrantChain308() ||
                FinalGrantRule308.Pending(finalGrantLedgerIds, finalGrantBossIds, Progress.ledger?.completed, Progress.campaign?.EncounterEvidence) == 0) return;
            var proposal = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            var granted = ApplyFinalGrants308(proposal);
            if (granted == null) return;
            if (!TryCommitInteraction(proposal, out _)) { nextFinalGrantCheck = Time.unscaledTime + 5f; return; }
            AnnounceFinalGrants308(granted);
        }
    }
}
