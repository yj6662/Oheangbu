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
            shortcutDoors ??= Array.FindAll(FindObjectsByType<WorldShortcutDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None), d => d.gameObject.scene == gameObject.scene);
            return Array.Find(shortcutDoors, d => d != null && d.Id == id);
        }
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
