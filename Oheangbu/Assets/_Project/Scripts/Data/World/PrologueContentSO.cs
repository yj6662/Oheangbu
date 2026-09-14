using System;
using UnityEngine;
namespace Oheangbu.Data.World
{
    public enum PrologueInteractionKind { Evidence, Currency, Rest, Conversation, Preview }
    [CreateAssetMenu(menuName="Oheangbu/World/Prologue Content")]
    public sealed class PrologueContentSO : ScriptableObject
    {
        [Serializable] public sealed class Point
        {
            public string Id; public PrologueInteractionKind Kind; public Vector3 Position;
            public string Prompt; [TextArea] public string Text; public int Currency; public float Radius=2.5f;
        }
        public string SaveSlot="cheongrim-prologue-v1";
        public Vector3 StartPosition;
        public float StartYaw=0;
        public Point[] Points=Array.Empty<Point>();
        public int EnemyReward=12;
        public float FallDeathHeight=6;
        public float AutosaveSeconds=8;
        public Vector3[] MainPath=Array.Empty<Vector3>();
        public Vector3[] BranchPath=Array.Empty<Vector3>();
    }
}
