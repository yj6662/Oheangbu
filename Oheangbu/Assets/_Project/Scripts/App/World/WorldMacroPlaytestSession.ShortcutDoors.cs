using System;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        WorldShortcutDoor[] shortcutDoors;
        WorldShortcutDoor ShortcutDoor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureShortcutDoors();
            // #307 item 2: Array.Find's first match without a capturing lambda (called for every focus candidate in range)
            for (int i = 0; i < shortcutDoors.Length; i++) { var d = shortcutDoors[i]; if (d != null && d.Id == id) return d; }
            return null;
        }
        void EnsureShortcutDoors() =>
            shortcutDoors ??= Array.FindAll(FindObjectsByType<WorldShortcutDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None), d => d.gameObject.scene == gameObject.scene);
        // An unbarred door stays open and is no longer an interaction.
        bool ShortcutDoorOpened(string id) => ShortcutDoor(id) != null && Progress != null && Progress.ledger.completed.Contains(id);
        bool TryHandleShortcutDoor(PrologueContentSO.Point point, out bool success)
        {
            success = false;
            if (point == null || ShortcutDoor(point.Id) == null) return false;
            if (Progress.ledger.completed.Contains(point.Id)) return true;
            var proposal = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            proposal.ledger.completed.Add(point.Id);
            if (!TryCommitInteraction(proposal, out var error)) { Show(error); return true; }
            if (!string.IsNullOrEmpty(point.Text)) Show(point.Text);
            InteractionResolved?.Invoke(point.Kind, point.Position);
            success = true; return true;
        }
    }
}
