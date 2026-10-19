using UnityEngine;

namespace Oheangbu.Data
{
    // SPEC-PACE-308 (D308-116) [every number TEST]: the pace pass. Every default here is the game before it, so a missing asset or
    // a default asset changes nothing. Runtime lookup: Resources "Pace308/PaceProfile308", read once by each consumer (no static cache).
    // Recognition and the brush force never read this: the draw slow-down only changes how long the world runs slower.
    [CreateAssetMenu(menuName = "Oheangbu/Pace Profile 308", fileName = "PaceProfile308")]
    public sealed class PaceProfile308 : ScriptableObject
    {
        public const string ResourcePath = "Pace308/PaceProfile308";

        [Header("작도 감속 [TEST]")]
        [Tooltip("작도 진입 뒤 감속이 걸리는 실시간(초). 0 = 작도 내내 감속(이전 규칙).")]
        [Min(0)] public float DrawSlowSeconds;

        [Header("적 리듬 [TEST]")]
        [Tooltip("적 공격 뒤 회복 시간 배율. 1 = 그대로.")]
        public float EnemyRecoveryScale = 1f;

        [Header("달리기 [TEST]")]
        [Tooltip("달리는 동안 더하는 FOV(도). 0 = 없음.")]
        [Min(0)] public float SprintFovDegrees;
        [Tooltip("달리기 FOV가 오르내리는 시간(실시간 초).")]
        public float SprintFovSmoothSeconds = .15f;
        [Tooltip("달리는 동안 바람 소리 볼륨에 더하는 값. 0 = 없음.")]
        [Min(0)] public float WindSprintGain;
        [Tooltip("바람 소리 볼륨 상한.")]
        public float WindMax = .55f;

        public static PaceProfile308 Load() => Resources.Load<PaceProfile308>(ResourcePath);

        public bool IsValid(out string error)
        {
            error = DrawSlowSeconds < 0f || DrawSlowSeconds > 2f ? "DrawSlowSeconds 0-2"
                : EnemyRecoveryScale < .3f || EnemyRecoveryScale > 1.5f ? "EnemyRecoveryScale .3-1.5"
                : SprintFovDegrees < 0f || SprintFovDegrees > 15f ? "SprintFovDegrees 0-15"
                : SprintFovSmoothSeconds < .01f || SprintFovSmoothSeconds > 1f ? "SprintFovSmoothSeconds .01-1"
                : WindSprintGain < 0f || WindSprintGain > 1f ? "WindSprintGain 0-1"
                : WindMax < 0f || WindMax > 1f ? "WindMax 0-1" : null;
            return error == null;
        }

        // PURE308: each consumer asks for its own value; an invalid profile reads as the game before this pass.
        public static float DrawSlowOf(PaceProfile308 p) => p != null && p.IsValid(out _) ? p.DrawSlowSeconds : 0f;
        public static float RecoveryScaleOf(PaceProfile308 p) => p != null && p.IsValid(out _) ? p.EnemyRecoveryScale : 1f;
        public static float SprintFovOf(PaceProfile308 p) => p != null && p.IsValid(out _) ? p.SprintFovDegrees : 0f;
        public static float WindSprintGainOf(PaceProfile308 p) => p != null && p.IsValid(out _) ? p.WindSprintGain : 0f;
    }
}
