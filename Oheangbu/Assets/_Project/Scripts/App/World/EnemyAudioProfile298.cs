using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World
{
    public enum EnemyAudioRole298 { Alert, Windup, Hit, Death }

    [CreateAssetMenu(menuName = "Oheangbu/Audio/Folklore Enemy 298")]
    public sealed class EnemyAudioProfile298 : ScriptableObject
    {
        public string ActorId;
        public WorldMacroPlaytestAudioProfileSO.Cue Alert = Spatial(.4f);
        public WorldMacroPlaytestAudioProfileSO.Cue Windup = Spatial(.08f);
        public WorldMacroPlaytestAudioProfileSO.Cue HitA = Spatial(.1f);
        public WorldMacroPlaytestAudioProfileSO.Cue HitB = Spatial(.1f);
        public WorldMacroPlaytestAudioProfileSO.Cue Death = Spatial(0f);

        static WorldMacroPlaytestAudioProfileSO.Cue Spatial(float cooldown) =>
            new WorldMacroPlaytestAudioProfileSO.Cue { SpatialBlend = 1f, Cooldown = cooldown, MaxConcurrent = 3 };

        public static bool Playable(WorldMacroPlaytestAudioProfileSO.Cue cue) => cue != null &&
            cue.Clip != null && cue.Clip.samples > 0 && float.IsFinite(cue.Clip.length) &&
            cue.Clip.length > 0 && cue.Clip.loadState != AudioDataLoadState.Failed;

        public WorldMacroPlaytestAudioProfileSO.Cue Find(EnemyAudioRole298 role, int hitIndex = 0)
        {
            switch (role)
            {
                case EnemyAudioRole298.Alert: return Playable(Alert) ? Alert : null;
                case EnemyAudioRole298.Windup: return Playable(Windup) ? Windup : null;
                case EnemyAudioRole298.Death: return Playable(Death) ? Death : null;
                default:
                    var first = (hitIndex & 1) == 0 ? HitA : HitB;
                    var other = (hitIndex & 1) == 0 ? HitB : HitA;
                    return Playable(first) ? first : Playable(other) ? other : null;
            }
        }

        // Import may intentionally leave empty roles. Never replace these with silent placeholder clips.
        public string MissingClips
        {
            get
            {
                var missing = new List<string>();
                if (!Playable(Alert)) missing.Add("alert");
                if (!Playable(Windup)) missing.Add("windup");
                if (!Playable(HitA)) missing.Add("hit_a");
                if (!Playable(HitB)) missing.Add("hit_b");
                if (!Playable(Death)) missing.Add("death");
                return string.Join(",", missing);
            }
        }
    }
}
