using System;
using UnityEngine;
namespace Oheangbu.Data.World
{
    // NARR-VOICE [TEST] attitude (D306): the same value drives speech (SPEC-NPC-VOICE-306) and posture (NpcJobActor).
    public enum NpcAttitude306 { Wary, Neutral, Hostile }
    // SPEC-PLAYTEST-306 #5 / PLAN §2-5: one role's work loop, hand tools, glance and talk posture. Every value TEST.
    // Clips live in Controller (an AnimatorOverrideController of AC_NpcJob); a behaviour names the state it plays.
    [CreateAssetMenu(menuName="Oheangbu/World/NPC Job Profile")]
    public sealed class NpcJobProfileSO : ScriptableObject
    {
        public enum Slot { Idle, Work0, Work1, Work2, Sit }
        [Serializable] public sealed class Behaviour
        {
            public string Label="";
            [Tooltip("AC_NpcJob state; Sit enters through SitDown and leaves through StandUp when those states exist.")]
            public Slot State=Slot.Work0;
            [Tooltip("Reference only (the override controller carries the clip); audit compares the two.")]
            public AnimationClip Clip;
            [Min(.5f)] public float MinSeconds=6;
            [Min(.5f)] public float MaxSeconds=12;
            [Min(0)] public float Weight=1;
            [Header("Hand tool (no collider, no emission; built by NpcJobs306 author)")]
            public GameObject HandTool;
            public HumanBodyBones HandBone=HumanBodyBones.RightHand;
            [Tooltip("World length of the tool's longest side, metres.")]
            [Min(.02f)] public float ToolLength=.5f;
            public Vector3 ToolLocalPosition=new Vector3(0,.04f,.02f);
            public Vector3 ToolLocalEuler;
            [Header("Workplace (from the home spot along the home facing; clamped to point radius - RadiusMargin)")]
            public Vector3 Offset;
            [Tooltip("Facing yaw relative to the home facing, degrees.")]
            public float Yaw;
        }
        public string RoleId="";
        public NpcAttitude306 Attitude=NpcAttitude306.Neutral;
        [Tooltip("AnimatorOverrideController of AC_NpcJob; empty = keep the model's controller (Generic rig: body turn only).")]
        public RuntimeAnimatorController Controller;
        public Behaviour[] Behaviours=Array.Empty<Behaviour>();
        [Tooltip("Reference only; the override maps it onto Talk / Glance.")]
        public AnimationClip TalkClip,GlanceClip;
        [Header("Glance (head IK only)")]
        [Min(.5f)] public float ApproachDistance=4.5f;
        [Min(.5f)] public float LeaveDistance=6.5f;
        [Range(0,1)] public float HeadWeight=.4f;
        [Range(0,1)] public float HeadClamp=.55f;
        [Min(.1f)] public float HeadBlendSeconds=.45f;
        [Tooltip("Glance state is played once on approach for this long (0 = never); seated behaviours skip it.")]
        [Min(0)] public float GlanceHoldSeconds=1.6f;
        [Tooltip("Neutral workers may square up while glancing; wary and hostile never do (D306).")]
        public bool GlanceTurnsBody;
        [Header("Talk (unscaled time: the game is paused while the dialogue view is open)")]
        [Min(1)] public float TurnDegreesPerSecond=150;
        [Range(0,180)] public float MaxTurnDegrees=180;
        [Min(0)] public float ReturnDelaySeconds=1.2f;
        [Tooltip("A dialogue whose source position lies within this of the actor counts as addressed to it.")]
        [Min(.1f)] public float MatchDistance=1.5f;
        [Tooltip("A talk started by the interaction whose game never paused (a short receipt): hold the talk pose this long.")]
        [Min(.5f)] public float FallbackTalkSeconds=3;
        [Min(5)] public float TalkTimeoutSeconds=120;
        [Header("Moving between workplaces")]
        [Min(.1f)] public float WalkSpeed=1.1f;
        [Min(0)] public float RadiusMargin=.6f;
        [Min(.02f)] public float CrossFadeSeconds=.25f;
        [Tooltip("Holder parent moving faster than this (m/s) = carried (escort companion walking or riding): no glance turn, no workplace walk.")]
        [Min(0)] public float CarriedSpeed=.2f;
        public static float DefaultHeadWeight(NpcAttitude306 a)=>a==NpcAttitude306.Wary?.6f:a==NpcAttitude306.Hostile?.8f:.4f;
        public static float DefaultTurnRate(NpcAttitude306 a)=>a==NpcAttitude306.Hostile?90:a==NpcAttitude306.Wary?150:180;
        public static float DefaultMaxTurn(NpcAttitude306 a)=>a==NpcAttitude306.Hostile?60:180;
    }
}
