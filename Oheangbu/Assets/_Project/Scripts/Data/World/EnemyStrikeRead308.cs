using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 D308-70 (SPEC-ENEMY-BRAIN-308, TEST): how an EnemyRigMotion298 actor shows WHEN its attack lands, as data. Presentation
    // only: the telegraph and recovery clocks, the contact time, damage, the hit shape, parry and dodge are EnemyController's and
    // never read this. The attack clip used to be stretched evenly over the telegraph (a 0.3 s jab became 0.8 s of slow motion with
    // no beat to dodge on). With this asset the same clip is played as three beats that end on the same contact time:
    //   draw back (eased, most of the telegraph) -> hold (the drawn-back pose stands still) -> swing (the last StrikeSeconds, fast)
    // and the contact pose stands for a moment before the recovery. A humanoid also leans back over the draw and forward on the swing.
    // No asset, or one that is not filled in = the even stretch as before. Every value is authored in the asset.
    [CreateAssetMenu(menuName = "Oheangbu/World/Enemy Strike Read 308")]
    public sealed class EnemyStrikeRead308 : ScriptableObject
    {
        [Header("휘두름 [TEST]")]
        [Tooltip("타격 직전, 휘두르는 데 쓰는 실제 시간(초). 이 순간에 휘두르는 소리가 난다.")]
        [Min(0)] public float StrikeSeconds;
        [Tooltip("클립에서 타격 자세 바로 앞의 이만큼(클립 초)을 휘두름으로 본다.")]
        [Min(0)] public float StrikeClipSeconds;
        [Tooltip("휘두름이 예고 시간에서 차지할 수 있는 최대 몫(짧은 예고 보호).")]
        [Range(0, .9f)] public float MaxStrikeShareOfTelegraph;
        [Tooltip("휘두름이 클립의 타격 앞 구간에서 차지할 수 있는 최대 몫(짧은 클립 보호).")]
        [Range(0, .9f)] public float MaxSwingShareOfClip;

        [Header("모으기 · 멈칫 [TEST]")]
        [Tooltip("휘두르기 직전, 모은 자세로 멈춰 서는 시간(초). 예고가 짧으면 줄어든다.")]
        [Min(0)] public float HoldSeconds;
        [Tooltip("모으는 동작에 남겨 둘 최소 시간(초). 멈칫은 이것을 침범하지 않는다.")]
        [Min(0)] public float MinWindupSeconds;
        [Tooltip("모으기 곡선의 지수(1 = 고르게, 클수록 빨리 모으고 천천히 멎는다).")]
        [Min(1)] public float WindupEase;
        [Tooltip("휘두름 곡선의 지수(1 = 고르게, 클수록 끝에서 빨라진다).")]
        [Min(1)] public float StrikeEase;

        [Header("타격 순간 [TEST]")]
        [Tooltip("타격 자세로 멈춰 서는 시간(초).")]
        [Min(0)] public float ImpactHoldSeconds;
        [Tooltip("플레이어가 실제로 피해를 입은 타격에 더하는 멈춤(초).")]
        [Min(0)] public float ConnectedHoldSeconds;
        [Tooltip("타격 멈춤이 회수 시간에서 차지할 수 있는 최대 몫.")]
        [Range(0, .9f)] public float MaxImpactHoldShare;

        [Header("몸 기울임 (Humanoid) [TEST]")]
        [Tooltip("모으는 동안 윗몸을 뒤로 젖히는 각(도).")]
        [Range(0, 40)] public float WindupLeanDegrees;
        [Tooltip("휘두를 때 윗몸을 앞으로 던지는 각(도).")]
        [Range(0, 40)] public float StrikeLeanDegrees;
        [Tooltip("던진 윗몸이 제자리로 돌아오는 데 쓰는 회수 시간의 몫.")]
        [Range(0, 1)] public float LeanRecoverShare;

        public bool Usable =>
            Finite(StrikeSeconds) && StrikeSeconds > 0 && Finite(StrikeClipSeconds) && StrikeClipSeconds > 0 &&
            Finite(MaxStrikeShareOfTelegraph) && MaxStrikeShareOfTelegraph > 0 && Finite(MaxSwingShareOfClip) && MaxSwingShareOfClip > 0 &&
            Finite(HoldSeconds) && Finite(MinWindupSeconds) && Finite(WindupEase) && WindupEase >= 1 && Finite(StrikeEase) && StrikeEase >= 1 &&
            Finite(ImpactHoldSeconds) && Finite(ConnectedHoldSeconds) && Finite(MaxImpactHoldShare) &&
            Finite(WindupLeanDegrees) && Finite(StrikeLeanDegrees) && Finite(LeanRecoverShare);

        static bool Finite(float v) => float.IsFinite(v) && v >= 0;

        // The three beats of a telegraph of `telegraph` seconds. They always add up to the telegraph.
        public void Beats(float telegraph, out float windup, out float hold, out float strike)
        {
            float t = Mathf.Max(0f, telegraph);
            strike = Mathf.Min(StrikeSeconds, t * MaxStrikeShareOfTelegraph);
            hold = Mathf.Clamp(t - strike - MinWindupSeconds, 0f, HoldSeconds);
            windup = Mathf.Max(0f, t - strike - hold);
        }

        /// <summary>Real seconds before the contact at which the swing starts (the moment the swing sound belongs to).</summary>
        public float StrikeLead(float telegraph) { Beats(telegraph, out _, out _, out float strike); return strike; }

        // Clip phase (0..1) of the drawn-back pose: the contact phase minus the swing.
        public float DrawnPhase(float peak01, float clipLength)
        {
            if (clipLength <= 0f) return peak01;
            float before = peak01 * clipLength;
            return (before - Mathf.Min(StrikeClipSeconds, before * MaxSwingShareOfClip)) / clipLength;
        }

        /// <summary>Clip phase (0..1) `elapsed` seconds into a telegraph. Never decreases, and is exactly `peak01` at the end:
        /// the contact pose and the contact time stay where the even stretch had them.</summary>
        public float WindupPhase(float peak01, float clipLength, float telegraph, float elapsed)
        {
            Beats(telegraph, out float windup, out float hold, out float strike);
            float drawn = strike > 0f ? DrawnPhase(peak01, clipLength) : peak01;
            if (elapsed >= telegraph) return peak01;
            if (elapsed < windup) return drawn * EaseOut(windup > 0f ? elapsed / windup : 1f, WindupEase);
            if (elapsed < windup + hold || strike <= 0f) return drawn;
            return Mathf.Lerp(drawn, peak01, Mathf.Pow(Mathf.Clamp01((elapsed - windup - hold) / strike), StrikeEase));
        }

        /// <summary>Clip phase `elapsed` seconds into a recovery: the contact pose stands, then the rest of the clip plays out.</summary>
        public float RecoveryPhase(float peak01, float recovery, float elapsed, bool connected)
        {
            float hold = ImpactHold(recovery, connected);
            if (elapsed <= hold) return peak01;
            float rest = recovery - hold;
            return peak01 + (1f - peak01) * (rest > 0f ? Mathf.Clamp01((elapsed - hold) / rest) : 1f);
        }

        public float ImpactHold(float recovery, bool connected) =>
            Mathf.Min(ImpactHoldSeconds + (connected ? ConnectedHoldSeconds : 0f), Mathf.Max(0f, recovery) * MaxImpactHoldShare);

        /// <summary>Signed upper-body lean in degrees during a telegraph: negative = leaning back (drawing), positive = thrown forward.</summary>
        public float WindupLean(float telegraph, float elapsed)
        {
            Beats(telegraph, out float windup, out float hold, out float strike);
            if (elapsed < windup) return -WindupLeanDegrees * EaseOut(windup > 0f ? elapsed / windup : 1f, WindupEase);
            if (elapsed < windup + hold || strike <= 0f) return -WindupLeanDegrees;
            return Mathf.Lerp(-WindupLeanDegrees, StrikeLeanDegrees, Mathf.Pow(Mathf.Clamp01((elapsed - windup - hold) / strike), StrikeEase));
        }

        /// <summary>Lean in degrees during a recovery: thrown forward through the contact hold, then back to upright.</summary>
        public float RecoveryLean(float recovery, float elapsed, bool connected)
        {
            float hold = ImpactHold(recovery, connected), back = Mathf.Max(0f, recovery - hold) * LeanRecoverShare;
            if (elapsed <= hold) return StrikeLeanDegrees;
            return back > 0f ? StrikeLeanDegrees * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - hold) / back))) : 0f;
        }

        static float EaseOut(float u, float power) => 1f - Mathf.Pow(1f - Mathf.Clamp01(u), power);
    }
}
