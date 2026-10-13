using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 D308-70 (SPEC-ENEMY-BRAIN-308, TEST): how an ordinary field enemy (a PrologueEncounter with no boss controller) notices
    // the player, closes in, waits for its turn to attack and gives up, as data. An actor without this asset, or with one that is not
    // filled in, keeps the pre-D308-70 behaviour (seen all around inside DetectionRange, chases at once, everybody attacks together,
    // follows the true position for 4 s after losing sight). Attack shapes, damage, telegraph and recovery clocks, parry and dodge
    // rules never read it (EnemyController / EnemyAttackProfileSO own those). Every value is authored in the asset; the code holds
    // no default numbers.
    [CreateAssetMenu(menuName = "Oheangbu/World/Enemy Brain Profile 308")]
    public sealed class EnemyBrainProfile308 : ScriptableObject
    {
        [Header("알아챔 [TEST]")]
        [Tooltip("순찰 중인 적의 시야 반각(도). 몸 앞쪽 이 각 안 + DetectionRange 안 + 시선이 통하면 알아챈다. 180 = 사방(예전과 같다).")]
        [Range(1, 180)] public float VisionHalfAngleDegrees;
        [Tooltip("이 거리(m) 안은 등 뒤에서도 알아챈다(시선은 통해야 한다).")]
        [Min(0)] public float ProximityRadius;
        [Tooltip("순찰 중 피해를 입으면 거리 · 방향과 상관없이 알아챈다(맞은 때의 플레이어 자리로 간다).")]
        public bool AlertOnDamage;
        [Tooltip("알아챈 뒤 멈춰서 플레이어 쪽으로 몸을 돌리는 시간(초). 이 동안 움직이지도 공격하지도 않는다. 경고음은 이 박자의 시작에 난다.")]
        [Min(0)] public float NoticeSeconds;
        [Tooltip("제 눈으로 알아챈 적이 이 거리(m) 안의 순찰 중인 동료를 부른다. 0 = 부르지 않는다. 불려 온 적은 다시 부르지 않는다.")]
        [Min(0)] public float CallRadius;
        [Tooltip("부름을 들은 동료가 알아채기까지(초).")]
        [Min(0)] public float CallDelaySeconds;

        [Header("몸 방향 [TEST]")]
        [Tooltip("플레이어 쪽으로 도는 빠르기(도/초). 0 = 한 프레임에 돈다(예전과 같다).")]
        [Min(0)] public float TurnDegreesPerSecond;
        [Tooltip("공격 사거리 + 이 값(m) 안에서만 플레이어를 바라본다. 그 밖에서 움직일 때는 가는 방향을 본다(옆걸음 없음).")]
        [Min(0)] public float FaceMargin;
        [Tooltip("몸 앞과 플레이어 방향의 차가 이 각(도) 안일 때만 새 공격을 시작한다.")]
        [Range(1, 180)] public float AttackFacingDegrees;

        [Header("공격 차례 [TEST]")]
        [Tooltip("같은 플레이어를 동시에 공격(예고 ~ 회수)할 수 있는 근접 적 수. 0 = 제한 없음.")]
        [Min(0)] public int MaxMeleeAttackers;
        [Tooltip("같은 플레이어를 동시에 공격할 수 있는 원거리 적 수. 0 = 제한 없음.")]
        [Min(0)] public int MaxRangedAttackers;
        [Tooltip("한 적이 차례를 받은 뒤 다음 적이 차례를 받기까지의 최소 간격(초).")]
        [Min(0)] public float TurnGapSeconds;
        [Tooltip("차례를 받고 이 시간(초) 안에 공격을 시작하지 못하면 차례를 내놓는다.")]
        [Min(0)] public float TurnClaimSeconds;
        [Tooltip("공격을 마친 적이 다시 차례를 청하기까지(초). 기다리는 동료가 먼저 받는다.")]
        [Min(0)] public float RetakeSeconds;
        [Tooltip("차례를 기다리는 근접 적이 서는 거리 = 공격 사거리 + 이 값(m). 이미 더 가까이 있으면 그 자리에 선다(뒷걸음 없음).")]
        [Min(0)] public float WaitRingMargin;
        [Tooltip("공격 사거리 + 이 값(m) 안에 들어와야 차례를 청한다.")]
        [Min(0)] public float ClaimMargin;

        [Header("놓침 [TEST]")]
        [Tooltip("시야에서 놓친 뒤 이 시간(초)이 지나면 '찾는 중'이 된다. 놓친 순간부터 마지막으로 본 자리로 간다(보이지 않는 플레이어의 지금 자리를 따라가지 않는다).")]
        [Min(0)] public float LoseSightSeconds;
        [Tooltip("놓친 뒤 이 시간(초)이 지나면 포기하고 돌아간다(예전 4초).")]
        [Min(0)] public float GiveUpSeconds;
        [Tooltip("마지막으로 본 자리에 이 거리(m) 안으로 오면 서서 둘러본다.")]
        [Min(0)] public float SearchArriveMeters;
        [Tooltip("둘러볼 때 좌우로 고개(몸)를 돌리는 각(도)과 한 번 왕복하는 시간(초).")]
        [Min(0)] public float SearchLookDegrees;
        [Min(0)] public float SearchLookPeriodSeconds;

        // An asset that was created but never filled in must not switch anything on.
        public bool Usable =>
            Finite(VisionHalfAngleDegrees) && VisionHalfAngleDegrees >= 1 && Finite(ProximityRadius) && Finite(NoticeSeconds) &&
            Finite(CallRadius) && Finite(CallDelaySeconds) && Finite(TurnDegreesPerSecond) && Finite(FaceMargin) &&
            Finite(AttackFacingDegrees) && AttackFacingDegrees >= 1 && MaxMeleeAttackers >= 0 && MaxRangedAttackers >= 0 &&
            Finite(TurnGapSeconds) && Finite(TurnClaimSeconds) && TurnClaimSeconds > 0 && Finite(RetakeSeconds) &&
            Finite(WaitRingMargin) && Finite(ClaimMargin) && ClaimMargin >= WaitRingMargin &&
            Finite(LoseSightSeconds) && Finite(GiveUpSeconds) && GiveUpSeconds > 0 && GiveUpSeconds >= LoseSightSeconds &&
            Finite(SearchArriveMeters) && Finite(SearchLookDegrees) && Finite(SearchLookPeriodSeconds);

        static bool Finite(float v) => float.IsFinite(v) && v >= 0;
    }
}
