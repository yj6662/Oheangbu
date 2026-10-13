using System.Collections.Generic;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-71 field-boss drops (SPEC-FIELD-BOSS-308) [TEST]. Which boss leaves which piece is data: Content.BossDrops. There is no
    // chance roll and no object on the ground: once a boss's defeat is on record (campaign.EncounterEvidence, durable across rests) and
    // its piece is not owned, the piece is added to the owned equipment in the same save as the defeat. Owning the piece is the
    // once-only key, so nothing new is written to the save format (version unchanged). The piece is not put on: the player wears it
    // from the equipment page like any other. Same shape as the final-consonant grants (FinalGrants308): apply to a detached
    // proposal, commit through the session's ordinary interaction commit, announce after the commit, and a one-second catch-up for
    // defeats committed by another route (the dragon, the general, a mountain boss) or proven before this table existed.
    public sealed partial class WorldMacroPlaytestSession
    {
        [System.NonSerialized] float nextBossDropCheck;

        bool BossDropDue308(WorldMacroProgress progress, Oheangbu.Data.World.WorldMacroPlaytestSO.BossDrop308 row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.BossId) || string.IsNullOrWhiteSpace(row.ItemId)) return false;
            var item = Content.EquipmentCatalog != null ? Content.EquipmentCatalog.Find(row.ItemId) : null;
            if (item == null || item.Starter) return false;   // a row that names no catalog piece leaves nothing (the save must keep matching the catalog)
            var proofs = progress?.campaign?.EncounterEvidence;
            return proofs != null && proofs.Contains(row.BossId) && progress.equipment != null && !progress.equipment.Has(row.ItemId);
        }

        bool BossDropsPending308(WorldMacroProgress progress)
        {
            if (Content == null || Content.BossDrops == null || Content.BossDrops.Length == 0 || progress?.equipment == null ||
                !progress.equipment.Matches(Content.EquipmentCatalog)) return false;
            foreach (var row in Content.BossDrops) if (BossDropDue308(progress, row)) return true;
            return false;
        }

        // Adds every due piece to a detached proposal (the caller commits it). Returns the rows that were granted, or null.
        List<Oheangbu.Data.World.WorldMacroPlaytestSO.BossDrop308> ApplyBossDrops308(WorldMacroProgress proposal)
        {
            if (!BossDropsPending308(proposal)) return null;
            var granted = new List<Oheangbu.Data.World.WorldMacroPlaytestSO.BossDrop308>();
            foreach (var row in Content.BossDrops)
            {
                if (!BossDropDue308(proposal, row)) continue;
                proposal.equipment.Owned.Add(new OwnedEquipment { Id = row.ItemId }); granted.Add(row);
            }
            if (granted.Count > 0 && proposal.equipment.Revision < long.MaxValue) proposal.equipment.Revision++;
            return granted.Count > 0 ? granted : null;
        }

        // After the commit succeeded: the receipts of the data, on the session's ordinary notice line (no new HUD element).
        void AnnounceBossDrops308(List<Oheangbu.Data.World.WorldMacroPlaytestSO.BossDrop308> granted)
        {
            if (granted == null) return;
            foreach (var row in granted) if (!string.IsNullOrEmpty(row.Notice)) Show(row.Notice);
        }

        void TickBossDrops308()
        {
            if (Time.unscaledTime < nextBossDropCheck) return;
            nextBossDropCheck = Time.unscaledTime + 1f;
            if (Progress == null || HasPendingDefeats || encounterDeathPending || SaveBlocked || vitals == null || vitals.Hp01 <= 0 || !BossDropsPending308(Progress)) return;
            var proposal = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            var granted = ApplyBossDrops308(proposal);
            if (granted == null) return;
            if (!TryCommitInteraction(proposal, out _)) { nextBossDropCheck = Time.unscaledTime + 5f; return; }
            AnnounceBossDrops308(granted);
        }

        /// <summary>Read only (checks): the pieces this progress is owed right now.</summary>
        public int BossDropsDue308 { get { int n = 0; if (BossDropsPending308(Progress)) foreach (var row in Content.BossDrops) if (BossDropDue308(Progress, row)) n++; return n; } }
    }
}
