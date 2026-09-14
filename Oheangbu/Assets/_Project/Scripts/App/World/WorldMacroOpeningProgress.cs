using System;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>Save policy only; it neither teleports a player nor changes an existing progress object.</summary>
    public static class WorldMacroOpeningProgress
    {
        public static bool Configured(WorldMacroOpeningProfileSO opening)=>opening!=null&&opening.Enabled&&opening.IsConfigured;
        public static bool Enrolled(WorldMacroProgress progress,WorldMacroOpeningProfileSO opening)=>Configured(opening)&&
            progress?.ledger?.completed!=null&&progress.ledger.completed.Contains(WorldMacroPlaytestSession.OpeningStartedId);
        public static WorldMacroProgress CreateNew(WorldMacroPlaytestSO content,bool allowVillageOpening)
        {
            bool office=allowVillageOpening&&content.Opening!=null&&content.Opening.Enabled;
            if(office&&!content.Opening.IsConfigured)throw new InvalidOperationException("Village opening requires valid spawn, village_commission conversation, and zero reward.");
            var progress=WorldMacroProgress.CreateNew(content.TerrainRevision,office?content.Opening.StartFeet:content.StartFeet,office?content.Opening.StartYaw:content.StartYaw);
            if(office)
            {
                progress.ledger.checkpoint=WorldMacroOpeningProfileSO.CheckpointId;
                progress.ledger.completed.Add(WorldMacroPlaytestSession.OpeningStartedId);
            }
            return progress;
        }
        public static bool KnownCheckpoint(string id)=>id=="mine_start"||id=="geumpyo_inn"||id==WorldMacroOpeningProfileSO.CheckpointId;
        public static Vector3 CheckpointFeet(WorldMacroPlaytestSO content,WorldMacroProgress progress)
        {
            if(progress.ledger.checkpoint=="geumpyo_inn")return content.InnCheckpointFeet;
            if(progress.ledger.checkpoint==WorldMacroOpeningProfileSO.CheckpointId)
                return Configured(content.Opening)?content.Opening.StartFeet:progress.ledger.checkpointPosition;
            return content.StartFeet;
        }
        public static float CheckpointYaw(WorldMacroPlaytestSO content,WorldMacroProgress progress)
        {
            if(progress.ledger.checkpoint!=WorldMacroOpeningProfileSO.CheckpointId)return content.StartYaw;
            if(Configured(content.Opening))return content.Opening.StartYaw;
            return float.IsFinite(progress.ledger.yaw)?progress.ledger.yaw:content.StartYaw;
        }
    }
}
