using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Macro Playtest")]
    public sealed class WorldMacroPlaytestSO:ScriptableObject
    {
        [Serializable] public sealed class Encounter
        {
            public string Id,ContentId;
            public Vector3 Feet;
            public Vector3[] Patrol;
            public bool Ranged;
            public bool RespawnOnRest=true;
            public float Detection=16,Leash=28,Speed=2.6f,Activation=180;
        }
        public string SaveSlot="world-macro-playtest-v2";
        public string TerrainRevision="macro-first-section-1";
        public PrologueContentSO TestRules;
        public Vector3 StartFeet;
        public float StartYaw;
        [Tooltip("Optional village opening for new saves only. Existing mine start coordinates and saved progress are preserved.")]
        public WorldMacroOpeningProfileSO Opening;
        public PrologueContentSO.Point[] Points=Array.Empty<PrologueContentSO.Point>();
        public Encounter[] Encounters=Array.Empty<Encounter>();
        public Vector3[] MainPath=Array.Empty<Vector3>(),BranchPath=Array.Empty<Vector3>();
        public Vector3 InnCheckpointFeet;
    }
}
