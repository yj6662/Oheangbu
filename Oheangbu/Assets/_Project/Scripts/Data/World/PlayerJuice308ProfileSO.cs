using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 player juice (SPEC-ANIM-JUICE-308) [TEST 전부]: 작도 중 · 작도 후 · 자동차 호출의 손맛 수치와 카메라 반응(D308-20 답 4).
    // 표현 전용이다 — 인식 · 필세 · 판정 · 먹 비용 어디에도 들어가지 않는다. 런타임은 리그의 직렬화 칸 → Resources
    // "Juice308/PlayerJuice308Profile" 순서로 찾는다. 에셋이 없으면 주스 코드 경로를 타지 않는다(거절 억제 B-1만 예외).
    // 필드 초기값 = 메인 프로필 값: "캡처를 본 뒤 켠다"는 효과(A-1 · A-2 · D-3)는 꺼진 채 나간다. 미리보기 구동기(Juice308Build)가
    // 메모리 사본에서만 켠다. 아직 짓지 않은 효과의 묶음은 두지 않는다.
    [CreateAssetMenu(menuName = "Oheangbu/World/Player Juice 308 TEST", fileName = "PlayerJuice308Profile")]
    public sealed class PlayerJuice308ProfileSO : ScriptableObject
    {
        public const string ResourcesPath = "Juice308/PlayerJuice308Profile";

        [Header("Common [TEST]")]
        [Tooltip("Master scale of every added value (0 = today's motion; the rejection fix B-1 is not scaled).")]
        [Range(0f, 2f)] public float Intensity = 1f;
        [Range(0f, 2f)] public float DrawIntensity = 1f;
        [Range(0f, 2f)] public float CastIntensity = 1f;
        [Range(0f, 2f)] public float CallIntensity = 1f;
        [Tooltip("Scale of the camera reaction (section D) on top of Intensity.")]
        [Range(0f, 2f)] public float CameraIntensity = 1f;
        [Tooltip("ReducedMotion setting: hand / body additions are multiplied by this.")]
        [Range(0f, 1f)] public float ReducedMotionScale = .35f;
        [Tooltip("ReducedMotion setting: the camera reaction is multiplied by this (0 = no camera reaction).")]
        [Range(0f, 1f)] public float CameraReducedMotionScale = 0f;
        [Tooltip("Ceiling of the summed brush shaft lean (degrees, clamped after the sum).")]
        [Range(0f, 8f)] public float MaxShaftLeanDegrees = 5f;
        [Tooltip("Ceiling of the bristle splay added on top of the stroke's own splay.")]
        [Range(0f, 1f)] public float MaxSplayAdd = .30f;
        [Tooltip("The shaft lean fades to 0 over this last stretch of arm reach (m), so the reach clamp never pushes the tip.")]
        [Min(.001f)] public float HeadroomMeters = .05f;
        [Tooltip("Below this frame rate a ring's frequency is lowered to fps / 5 (the closed form is unchanged).")]
        [Min(1f)] public float MinRingFrameRate = 45f;

        [Serializable]
        public sealed class StrokeStartSet   // A-1 기필 누름
        {
            [Tooltip("OFF until the grip comparison captures (P1) were seen by the user.")]
            public bool Enabled = false;
            [Tooltip("The shaft stands up toward the drawing-plane normal by this much (degrees); the tip stays on the pointer.")]
            [Range(0f, 6f)] public float PressTiltDegrees = 3f;
            [Min(.005f)] public float PressAttack = .06f;
            [Min(.005f)] public float PressRelease = .16f;
            [Range(0f, 1f)] public float PressSplay = .25f;
            [Min(.005f)] public float SplayAttack = .04f;
            [Min(.005f)] public float SplayRelease = .20f;
            [Tooltip("Second and later strokes of a letter.")]
            [Range(0f, 1f)] public float LaterStrokeScale = .7f;
            [Tooltip("A stroke begun within this many seconds of the previous stroke's end is scaled by RapidScale.")]
            [Min(0f)] public float RapidWindow = .12f;
            [Range(0f, 1f)] public float RapidScale = .6f;
        }

        [Serializable]
        public sealed class StrokeEndSet   // A-2 수필 삐침
        {
            [Tooltip("OFF until the grip comparison captures (P1) were seen by the user.")]
            public bool Enabled = false;
            [Tooltip("Shaft lean along the stroke direction at SpeedLow / SpeedHigh (degrees).")]
            [Range(0f, 6f)] public float FlickLeanMin = 1.2f;
            [Range(0f, 6f)] public float FlickLeanMax = 4f;
            [Min(.1f)] public float FlickHz = 5f;
            [Range(.05f, .99f)] public float FlickDamping = .6f;
            [Min(.02f)] public float FlickSeconds = .26f;
            [Tooltip("Displayed stroke speed in the rig's centred viewport units per second (2 = the screen width in one second).")]
            [Min(0f)] public float SpeedLow = .6f;
            [Min(.01f)] public float SpeedHigh = 3f;
            [Tooltip("Smoothing time of the displayed stroke velocity that the flick reads (s).")]
            [Min(.001f)] public float SpeedWindow = .05f;
        }

        [Serializable]
        public sealed class RejectSet   // B-1 거절 억제
        {
            [Tooltip("A recognised letter whose cast was refused (ink, empty slot, lock) gets no success throw and no torso twist. " +
                     "The code does this without a profile too; false restores the old behaviour.")]
            public bool SuppressOnReject = true;
        }

        [Serializable]
        public sealed class CastBeatSet   // B-2 뿌리기 재박자
        {
            public bool Enabled = true;
            [Min(0f)] public float CockSeconds = .045f;
            [Tooltip("Pull-back before the throw as a share of the path (last pointer -> launch point).")]
            [Range(0f, .3f)] public float CockBack = .08f;
            [Min(.01f)] public float FlickSeconds = .09f;
            [Min(0f)] public float HoldSecondsLow = 0f;
            [Min(0f)] public float HoldSecondsMid = .045f;
            [Min(0f)] public float HoldSecondsHigh = .07f;
            [Min(.02f)] public float DropSeconds = .16f;
            [Tooltip("Throw past the launch point (viewport units), by power tier.")]
            [Range(0f, .2f)] public float OvershootLow = .05f;
            [Range(0f, .2f)] public float OvershootMid = .07f;
            [Range(0f, .2f)] public float OvershootHigh = .10f;
            [Tooltip("Share of the overshoot the tip comes back by while it holds.")]
            [Range(0f, 1f)] public float Rebound = .35f;
            [Tooltip("Power proxy thresholds of the middle and the high tier.")]
            [Range(0f, 1f)] public float TierMid = .40f;
            [Range(0f, 1f)] public float TierHigh = .75f;
            [Range(0f, .5f)] public float ViewportMin = .05f;
            [Range(.5f, 1f)] public float ViewportMax = .95f;
            [Tooltip("Scale of the torso twist that follows the throw, by power tier.")]
            [Range(0f, 2f)] public float TwistScaleLow = .75f;
            [Range(0f, 2f)] public float TwistScaleMid = 1f;
            [Range(0f, 2f)] public float TwistScaleHigh = 1.2f;
            [Tooltip("The torso twist starts when the close-up hold ends (instead of the fixed CastDelay of the reaction profile).")]
            public bool CastDelayFollowsHold = true;
            [Tooltip("Seconds the camera rig keeps the close-up beyond the hold (the rig's own +0.1 s margin, now data).")]
            [Min(0f)] public float CloseupMargin = .1f;
        }

        [Serializable]
        public sealed class CallEndSet   // C-1 수필 넘침 + 버팀
        {
            public bool Enabled = true;
            [Tooltip("Overshoot past the stroke's end as a share of the path length.")]
            [Range(0f, .2f)] public float OverShare = .06f;
            [Min(.005f)] public float OverRise = .035f;
            [Min(.1f)] public float OverHz = 7f;
            [Range(.05f, .99f)] public float OverDamping = .5f;
            [Min(.02f)] public float OverSeconds = .14f;
            [Tooltip("The arm keeps its weight (stays extended) until this progress, then lowers. Never below the call moment.")]
            [Range(0f, .95f)] public float HoldTo = .70f;
            [Range(0f, .3f)] public float MaxOverShare = .08f;
        }

        [Serializable]
        public sealed class CallResponseSet   // C-3 응답 차등
        {
            [Range(0f, 1f)] public float RefusedOverScale = .3f;
            [Tooltip("A refused call: the tip sinks by this much (m) after the call moment.")]
            [Range(0f, .2f)] public float RefusedDroopMeters = .05f;
            [Min(.02f)] public float DroopSeconds = .20f;
        }

        [Serializable]
        public sealed class ArrivalSet   // C-4 차 내려앉기
        {
            public bool Enabled = true;
            [Tooltip("The car's visual body sinks by this much when the ink has revealed it (m). Physics is untouched.")]
            [Range(0f, .1f)] public float DropMeters = .03f;
            [Min(.01f)] public float FallSeconds = .08f;
            [Min(.1f)] public float Hz = 3.2f;
            [Range(.05f, .99f)] public float Damping = .45f;
            [Min(.05f)] public float SettleSeconds = .47f;
            [Tooltip("Recall: the visual body lifts by this much as the ink starts to cover it (m).")]
            [Range(0f, .1f)] public float RecallLiftMeters = .02f;
            [Min(.05f)] public float RecallLiftSeconds = .30f;
        }

        [Serializable]
        public sealed class CameraSet   // D 공통 상한
        {
            public bool Enabled = true;
            [Tooltip("Ceilings of the summed camera reaction (clamped after the sum). Position is never moved.")]
            [Range(0f, 3f)] public float MaxPitchDegrees = 1.2f;
            [Range(0f, 2f)] public float MaxRollDegrees = .6f;
            [Range(0f, 3f)] public float MaxFovDegrees = 1.5f;
            [Tooltip("Comfort floor: no camera reaction reaches its peak sooner than this (s). An attack below it is raised to it.")]
            [Min(0f)] public float MinAttackSeconds = .03f;
        }

        [Serializable]
        public sealed class CastKickSet   // D-1 시전 킥
        {
            public bool Enabled = true;
            [Tooltip("Nod at full power (degrees; + = the view dips).")]
            [Range(-2f, 2f)] public float PitchDegrees = .9f;
            [Min(.03f)] public float PitchAttack = .05f;
            [Min(.02f)] public float PitchRelease = .17f;
            [Tooltip("Field-of-view kick at full power (degrees; + = wider, the same sign as the groggy-hit breath).")]
            [Range(-2f, 2f)] public float FovDegrees = .8f;
            [Min(.03f)] public float FovAttack = .04f;
            [Min(.02f)] public float FovRelease = .14f;
            [Tooltip("Seconds after the commit at which the kick starts (the glyph has left the camera by then).")]
            [Min(0f)] public float DelaySeconds = .03f;
            [Tooltip("Scale at power proxy 0 (1 at power 1).")]
            [Range(0f, 1f)] public float PowerFloor = .5f;
        }

        [Serializable]
        public sealed class CallSettleSet   // D-2a 호출 정착
        {
            public bool Enabled = true;
            [Range(-2f, 2f)] public float PitchDegrees = .5f;
            [Min(.03f)] public float Attack = .06f;
            [Min(.02f)] public float Release = .22f;
        }

        [Serializable]
        public sealed class SetDownThumpSet   // D-2b 내려앉기 쿵
        {
            public bool Enabled = true;
            [Range(-2f, 2f)] public float PitchDegrees = .7f;
            [Min(.1f)] public float Hz = 6f;
            [Range(.05f, .99f)] public float Damping = .55f;
            [Min(.05f)] public float Seconds = .32f;
            [Range(-2f, 2f)] public float FovDegrees = .4f;
            [Min(.03f)] public float FovAttack = .03f;
            [Min(.02f)] public float FovRelease = .15f;
            [Tooltip("Flat distance player - car: full strength up to FullDistance, none from ZeroDistance (m).")]
            [Min(0f)] public float FullDistance = 8f;
            [Min(.1f)] public float ZeroDistance = 25f;
        }

        [Serializable]
        public sealed class ThrowRollSet   // D-3 뿌리기 기울임
        {
            [Tooltip("OFF until the camera captures (P8) were seen by the user.")]
            public bool Enabled = false;
            [Range(-2f, 2f)] public float RollDegrees = .5f;
            [Min(.03f)] public float Attack = .05f;
            [Min(.02f)] public float Release = .15f;
        }

        [Header("A  drawing [TEST]")]
        public StrokeStartSet StrokeStart = new StrokeStartSet();
        public StrokeEndSet StrokeEnd = new StrokeEndSet();

        [Header("B  after drawing [TEST]")]
        public RejectSet Reject = new RejectSet();
        public CastBeatSet CastBeat = new CastBeatSet();

        [Header("C  vehicle call [TEST]")]
        public CallEndSet CallEnd = new CallEndSet();
        public CallResponseSet CallResponse = new CallResponseSet();
        public ArrivalSet Arrival = new ArrivalSet();

        [Header("D  camera reaction (D308-20) [TEST]")]
        public CameraSet Camera = new CameraSet();
        public CastKickSet CastKick = new CastKickSet();
        public CallSettleSet CallSettle = new CallSettleSet();
        public SetDownThumpSet SetDownThump = new SetDownThumpSet();
        public ThrowRollSet ThrowRoll = new ThrowRollSet();

        /// <summary>0 low, 1 middle, 2 high (B-2). The power proxy is read only.</summary>
        public int Tier(float power01) => power01 >= CastBeat.TierHigh ? 2 : power01 >= CastBeat.TierMid ? 1 : 0;

        /// <summary>The preview driver's copy: everything the main profile keeps off until a capture was seen is switched on.</summary>
        public void EnablePreviewOnly()
        {
            StrokeStart.Enabled = true; StrokeEnd.Enabled = true; ThrowRoll.Enabled = true;
        }
    }
}
