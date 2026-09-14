using System;
using UnityEngine;
namespace Oheangbu.Data.World
{
    public enum MacroContentKind { Encounter, Npc, Evidence, Event, Dungeon, Boss, Rest, GatePreview }
    [CreateAssetMenu(menuName="Oheangbu/World/Macro Content")]
    public sealed class WorldMacroContentSheetSO:ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string Id,Label,Realm,Anchor,Source,Stage,Role,Text,Requires;
            public MacroContentKind Kind;
            public Vector3 Offset,Position;
            public float Yaw,Radius=3,ActivationDistance=180;
            public int Count=1;
            public bool Resolved,Preserve=true;
            public string State="TEST";
        }
        public string Version="content-layout-v1";
        public Entry[] Entries=Array.Empty<Entry>();
    }
}
