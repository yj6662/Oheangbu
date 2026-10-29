using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>One template (Frame x Verb): positions, sizes and the beats of the plan's table B. A recipe turns it into strokes; nothing here decides how long something lives
    /// (the executors do: material expiry, the cast's end, the return report).</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Vol2/Template", fileName = "Template")]
    public sealed class Vol2VfxTemplate : ScriptableObject
    {
        public string id = "P0";
        public Frame frame;
        public Verb verb;
        public Vol2Recipe recipe;
        [Tooltip("false = the slot exists (24 ids) but its look is a later slice")] public bool filled;
        public string summary = "";

        [Header("beats (plan B, seconds after the event)")]
        public float revealSeconds = 0.08f;
        public float outlineSeconds = 0.18f;
        public float surfaceSeconds = 0.30f;
        [Tooltip("contact accent (B-1 default)")] public float accentSeconds = 0.09f;
        [Tooltip("after the accent the contact look holds this long before it dries")] public float accentStableSeconds = 0.12f;
        [Tooltip("the path dwells this long after its head stops (accent + stable), then dries")] public float holdAfterHeadSeconds = 0.21f;
        [Tooltip("expiry warning window before the scheduled end")] public float expiryWarnSeconds = 0.4f;

        [Header("end reasons (plan B table, seconds the look takes after the event frame)")]
        public float endExpireSeconds = 0.30f;
        public float endConsumeSeconds = 0.15f;
        public float endChildSeconds = 0.30f;
        public float endReplaceSeconds = 0.20f;
        public float endHitCancelSeconds = 0.08f;
        public float endCleanseSeconds = 0.25f;
        [Tooltip("a plain drying (the path after its head stopped): plan B P0 0.12 - 0.18")] public float dryAfterContactSeconds = 0.15f;

        [Header("drawing sizes (metres)")]
        public float lineWidth = 0.30f;
        public float lineLift = 0.04f;
        public float tailMeters = 3.6f;
        public float strokeSpeed = 14f;
        public float height = 0.9f;
        public float branchWidth = 0.20f;
        public float forkAngleDegrees = 34f;
        public float bundleLength = 0.6f;
        public float bundleSpread = 0.3f;
        public float dotHeight = 0.45f;
        public float dotRadius = 0.14f;
        public float lipGap = 0.12f;
        public float lipLength = 0.6f;
        public float cutFaceHeight = 0.45f;
        public float loopRadius = 0.35f;
        public float accentLength = 0.65f;
        public int fleckCount = 4;
        [Tooltip("path ink: how much of the stroke is hollow (my strokes are hollow, plan E)")] [Range(0f, 1f)] public float hollow = 1f;

        public float EndSeconds(Vol2EndCode c)
        {
            switch (c)
            {
                case Vol2EndCode.Expire: return endExpireSeconds;
                case Vol2EndCode.Consume: return endConsumeSeconds;
                case Vol2EndCode.Child: return endChildSeconds;
                case Vol2EndCode.Replace: return endReplaceSeconds;
                case Vol2EndCode.HitCancel: return endHitCancelSeconds;
                case Vol2EndCode.Cleanse: return endCleanseSeconds;
                default: return endExpireSeconds;
            }
        }
    }
}
