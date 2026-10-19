using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 WP-16 (D308-114) guard-raising field enemies [TEST]. Content.GuardBrains names encounter ContentIds whose actors raise the
    // guard (EnemyBrainProfile308.GuardShare / GuardFacingDegrees, read by PrologueEncounter.Brain308). The shipped brain assets stay at
    // GuardShare = 0: the session clones the brain the actor already uses and writes the guard into the clone only, so other scenes and
    // other enemies sharing the asset are unchanged. A brain that already guards is left as it is. The clones live and die with the session.
    public sealed partial class WorldMacroPlaytestSession
    {
        [NonSerialized] readonly List<EnemyBrainProfile308> guardBrainClones308 = new List<EnemyBrainProfile308>();

        // Called once after the content brain has been handed out (Start). Returns how many actors got a guard (for the log only).
        int ApplyGuardBrains308()
        {
            var entries = Content != null ? Content.GuardBrains : null;
            if (entries == null || entries.Length == 0 || Actors == null || Content.Encounters == null) return 0;
            int guarded = 0;
            // one clone per (source brain, entry): actors of the same kind that share a brain share its guarded clone
            var made = new Dictionary<(EnemyBrainProfile308, WorldMacroPlaytestSO.GuardBrain308), EnemyBrainProfile308>();
            foreach (var actor in Actors)
            {
                if (actor == null || !actor.BrainActive308 || actor.Brain.GuardShare > 0f) continue;   // bosses, the general, the tutorial boss and growth mobs never use the brain
                var spec = Array.Find(Content.Encounters, x => x != null && x.Id == actor.Id);
                if (spec == null || string.IsNullOrEmpty(spec.ContentId)) continue;
                var entry = Array.Find(entries, e => e != null && e.ContentId == spec.ContentId);
                if (entry == null || !(entry.GuardShare > 0f) || entry.GuardFacingDegrees < 1f) continue;
                if (!made.TryGetValue((actor.Brain, entry), out var clone))
                {
                    clone = Instantiate(actor.Brain);
                    clone.name = actor.Brain.name + " (guard " + spec.ContentId + ")";
                    clone.hideFlags = HideFlags.DontSave;
                    clone.GuardShare = Mathf.Clamp01(entry.GuardShare);
                    clone.GuardFacingDegrees = Mathf.Clamp(entry.GuardFacingDegrees, 0f, 180f);
                    guardBrainClones308.Add(clone);
                    made[(actor.Brain, entry)] = clone;
                }
                actor.Brain = clone;
                guarded++;
            }
            return guarded;
        }

        void ReleaseGuardBrains308()
        {
            foreach (var clone in guardBrainClones308) if (clone != null) Destroy(clone);
            guardBrainClones308.Clear();
        }
    }
}
