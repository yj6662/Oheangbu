using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-DUNGEON-308] D308-57: the three path-opening field glyphs (눈 · 숫 · 웅) open SpellFieldGate308 objects; this is the
    // world side the spell side left open - a gate a cast opened stays open across saves.
    //   - Only gates of the session's own scene with an id take part (a streamed realm scene holds no gameplay object; the TEST gates
    //     a check spawns in Play have ids too, but they are made after this bind and are never listened to).
    //   - Saved fact = "gate308:<id>" in ledger.completed: doors (CompactMountainGate) read the same fact through RequiredCompleted.
    //   - Saved means committed: a gate whose fact could not be written closes again and the save error is shown.
    //   - With the presentation switch on (DemoAllFinals308, D308-53) nothing is written: the gate stays open for the session only.
    public sealed partial class WorldMacroPlaytestSession
    {
        public const string FieldGateFactPrefix308 = "gate308:";
        public static string FieldGateFact308(string gateId) => FieldGateFactPrefix308 + gateId;

        SpellFieldGate308[] fieldGates308;

        void BindFieldGates308()
        {
            UnbindFieldGates308();
            fieldGates308 = Array.FindAll(FindObjectsByType<SpellFieldGate308>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                g => g.gameObject.scene == gameObject.scene && !string.IsNullOrEmpty(g.Id));
            foreach (var gate in fieldGates308)
            {
                gate.SetOpen(Progress != null && Progress.ledger.completed.Contains(FieldGateFact308(gate.Id)));
                gate.Opened += OnFieldGateOpened308;
            }
        }

        void UnbindFieldGates308()
        {
            if (fieldGates308 == null) return;
            foreach (var gate in fieldGates308) if (gate != null) gate.Opened -= OnFieldGateOpened308;
            fieldGates308 = null;
        }

        void OnFieldGateOpened308(SpellFieldGate308 gate)
        {
            if (gate == null || Progress == null || DemoAllFinals308) return;
            string fact = FieldGateFact308(gate.Id);
            if (Progress.ledger.completed.Contains(fact)) return;
            var proposal = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            proposal.ledger.completed.Add(fact);
            if (TryCommitInteraction(proposal, out var error)) return;
            gate.SetOpen(false); Show(error);
        }
    }
}
