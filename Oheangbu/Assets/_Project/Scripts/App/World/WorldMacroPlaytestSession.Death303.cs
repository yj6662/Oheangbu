using System;
using Oheangbu.App.Prologue;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #303 (SPEC-PLAYER-FEEL-300 H): a combat death falls first, then the session's own respawn runs under the veil.
    // The durable part is unchanged: the terrain-query path (BeginEnvironmentRecovery) commits the recovery before it
    // moves the player; the plain path drops, resets, returns to the rest spot, restores and saves.
    public sealed partial class WorldMacroPlaytestSession
    {
        public WorldMacroPlayerDeath303 DeathPresentation;
        bool deathRespawnPending;
        Action pendingRespawn;
        public bool DeathRespawnPending => deathRespawnPending;

        bool TryBeginDeathPresentation(Action respawn)
        {
            if(DeathPresentation==null)DeathPresentation=FindFirstObjectByType<WorldMacroPlayerDeath303>();
            if(DeathPresentation==null||!DeathPresentation.CanPresent||DeathPresentation.IsActive)return false;
            deathRespawnPending=true;respawning=true;pendingRespawn=respawn;   // Update, autosave and interaction pause while the body falls
            DeathPresentation.Begin(RunPendingRespawn);
            return true;
        }

        void RunPendingRespawn()
        {
            if(!deathRespawnPending)return;
            deathRespawnPending=false;respawning=false;
            var respawn=pendingRespawn;pendingRespawn=null;respawn?.Invoke();
        }

        void CompleteDeathRespawn()
        {
            respawning=true;
            PrologueProgressStore.Drop(Progress.ledger,lastSafe);ResetCombat();Teleport(Checkpoint(),CheckpointYaw());
            vitals.Restore();ink.Restore();RefreshDrop();respawning=false;Show("마지막 쉼터에서 눈을 떴다. 남긴 통보를 되찾을 수 있다.");Save();
        }
    }
}
