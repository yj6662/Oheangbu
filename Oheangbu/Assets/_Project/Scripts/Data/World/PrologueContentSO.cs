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
            public string[] RequiredCompleted=Array.Empty<string>();
            public string[] RequiredDefeated=Array.Empty<string>();
            [TextArea] public string LockedText="아직 확인할 일이 남아 있다.";
        }
        [Serializable] public sealed class Commission
        {
            public string Id, GiverId, EvidenceId;
            [TextArea] public string OfferText, WaitingText, ReportText, CompletedText;
            public int Reward;
        }
        public Commission[] Commissions=Array.Empty<Commission>();
        [Serializable] public sealed class EncounterRule
        {
            public string Id;
            public string[] RequiredCompleted=Array.Empty<string>();
            public bool PersistentDefeat;
            public bool OverrideDefeatReward;
            public int DefeatReward;
        }
        public EncounterRule[] EncounterRules=Array.Empty<EncounterRule>();
        public string SaveSlot="cheongrim-prologue-v1";
        public Vector3 StartPosition;
        public float StartYaw=0;
        public Point[] Points=Array.Empty<Point>();
        public int EnemyReward=12;
        public float FallDeathHeight=6;
        public float AutosaveSeconds=8;
        public bool RestRequiresSafety;
        public Vector3[] MainPath=Array.Empty<Vector3>();
        public Vector3[] BranchPath=Array.Empty<Vector3>();
    }
}
