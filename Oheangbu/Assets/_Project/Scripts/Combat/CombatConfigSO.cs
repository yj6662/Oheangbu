using UnityEngine;

namespace Oheangbu.Combat
{
    // 전투 수치 전량 [TEST — SPEC-COMBAT-CORE-LOOP §8 시작값] — 코드에 상수를 두지 않는다.
    // 어떤 값도 규칙을 바꾸지 않는다: 규칙은 Combat Bible LOCKED, 여기는 그 세기·길이뿐.
    [CreateAssetMenu(menuName = "Oheangbu/Combat/Combat Config", fileName = "CombatConfig")]
    public sealed class CombatConfigSO : ScriptableObject
    {
        [Header("이동 (결정 3 — 1인칭·점프 없음)")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 4.5f;
        [SerializeField, Min(0.01f)] private float _lookSensitivity = 0.12f;
        [SerializeField] private float _gravity = -20f;

        [Header("회피 — 즉발 무적·먹 무소모·무보상(COMBAT-DEFENSE 사다리 최하단)")]
        [SerializeField, Min(0.1f)] private float _dodgeDistance = 3f;
        [SerializeField, Min(0.05f)] private float _dodgeDuration = 0.25f;
        [SerializeField, Min(0.05f)] private float _dodgeInvulnerable = 0.3f;
        [SerializeField, Min(0f)] private float _dodgeCooldown = 0.6f;

        [Header("플레이어")]
        [SerializeField, Min(1f)] private float _playerMaxHp = 100f;
        [Tooltip("락온 소프트 당김(초당) — 엘든링식: 카메라가 대상 쪽으로 은은히 끌리되 고정되지 않는다. 0=끔")]
        [SerializeField, Range(0f, 10f)] private float _lockOnCameraPull = 2.5f;

        [Header("카메라 [실험 2026-08-27] — 숄더뷰(V 토글)·작도 클로즈업. 채택=DECISIONS 문답 필요(결정 3)")]
        [Tooltip("시작 포즈를 숄더뷰로(실험 A/B 기본값). V키로 언제든 토글")]
        [SerializeField] private bool _shoulderStart = true;
        [Tooltip("숄더뷰 오프셋 — CameraPivot 기준 정중앙 상단·뒤(1차 카메라 검수 2026-08-27)")]
        [SerializeField] private Vector3 _shoulderOffset = new Vector3(0f, 0.55f, -2.8f);
        [Tooltip("작도 클로즈업 오프셋 — 시선 근처까지 전진(플레이어가 거의 안 보임)·살짝 우상. 추후 이 프레임에 팔·붓이 들어온다")]
        [SerializeField] private Vector3 _shoulderDrawOffset = new Vector3(0.35f, 0.25f, -0.2f);
        [Tooltip("평시·복귀 블렌드 속도(지수 수렴·unscaled)")]
        [SerializeField, Min(0.5f)] private float _cameraBlendSpeed = 8f;
        [Tooltip("클로즈업 진입 블렌드 속도 — 컷은 어색(3차 검수), 빠른 블렌드로")]
        [SerializeField, Min(0.5f)] private float _drawCloseupBlendSpeed = 20f;
        [Tooltip("클로즈업 FOV(스냅) — 포즈 이동만으론 글자 크기가 안 변해 FOV가 체감을 담당. 0=끔")]
        [SerializeField, Range(0f, 80f)] private float _drawCloseupFov = 52f;
        [Tooltip("숄더뷰 피치 클램프 — 오프셋 궤도가 지면을 관통하지 않는 범위")]
        [SerializeField, Range(-80f, 0f)] private float _shoulderPitchMin = -35f;
        [SerializeField, Range(0f, 80f)] private float _shoulderPitchMax = 60f;
        [Tooltip("몸 렌더러 표시 문턱 — 카메라 오프셋이 이 거리보다 멀 때만 몸을 그린다(니어클립 단면 방지)")]
        [SerializeField, Min(0.1f)] private float _bodyShowDistance = 0.8f;

        [Header("먹 경제 — 몸을 써야 먹이 찬다(COMBAT-HARVEST 먹 3원 중 2원)")]
        [SerializeField, Range(0f, 1f)] private float _inkStart = 1f;
        [SerializeField, Range(0f, 1f)] private float _spellInkCost = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _misfireInkCost = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _parryInkCost = 0.10f;
        [Tooltip("정답 패링 환급 — 소모 초과 환급이 곧 「순증」(COMBAT-PARRY)")]
        [SerializeField, Range(0f, 1f)] private float _parryInkRefund = 0.15f;

        [Header("갈무리 — 홀드·락온 한정·단독 처치 불가(COMBAT-HARVEST). 원거리 12m. 누르는 동안 붓이 먹을 뽑아낸다(3차 검수 확정)")]
        [SerializeField, Min(0.5f)] private float _harvestRange = 12f;
        [Tooltip("홀드 중 초당 먹 수급(0..1 비율)")]
        [SerializeField, Range(0f, 1f)] private float _harvestInkPerSecond = 0.1f;
        [Tooltip("홀드 중 초당 약피해 — HP 1 바닥(단독 처치 불가)")]
        [SerializeField, Min(0f)] private float _harvestDamagePerSecond = 4f;

        [Header("술식 투사체 [TEST 5차 검수 2026-08-28 — 피해=착탄 동기화]")]
        [Tooltip("공격 술식 투사체 속도(m/s) — 커밋 시 대상·비행시간 확정(락온 유도 보장), 착탄 시각에 피해")]
        [SerializeField, Min(1f)] private float _spellProjectileSpeed = 18f;

        [Header("패링·방어막 [TEST 2차 플레이 검수 2026-08-28 — COMBAT-PARRY 전이 실험: 판정점=임팩트]")]
        [Tooltip("방어막 앞 구간(초) — 글자 완성 후 이 시간 안의 임팩트는 패링 3단 판정. 창 기준이 임팩트 전→완성 후로 전이")]
        [SerializeField, Min(0.05f)] private float _parryWindow = 0.5f;
        [Tooltip("작도 잔존 방어막 총 지속(초) — 앞 구간 이후는 일반 방어. 새 패링 글자는 이전 방어막을 대체")]
        [SerializeField, Min(0.5f)] private float _guardDuration = 4f;
        [Tooltip("일반 방어 구간의 피해 배율(속성 공격 한정 — 무속성은 회피만이 답)")]
        [SerializeField, Range(0f, 1f)] private float _guardBlockFactor = 0.5f;
        [Tooltip("반성공의 피해 배율(경감)")]
        [SerializeField, Range(0f, 1f)] private float _halfParryDamageFactor = 0.4f;

        [Header("그로기 — 정답 패링만 쌓인다·무감쇠(COMBAT-GROGGY)")]
        [SerializeField, Min(1)] private int _parriesToBlossom = 3;
        [Tooltip("만개=급소창: 스턴 길이(義 오상이 후일 이 값을 늘린다)")]
        [SerializeField, Min(0.5f)] private float _blossomStunDuration = 4f;
        [SerializeField, Min(1f)] private float _blossomDamageMultiplier = 1.5f;

        [Header("적 — 일반몹 규칙: 무속성 근접 + 단일 속성 원거리, 콤보 없음(COMBAT-ENEMY)")]
        [SerializeField, Min(1f)] private float _enemyMaxHp = 60f;
        [SerializeField, Min(0.5f)] private float _enemyEngageRange = 12f;
        [SerializeField, Min(0.5f)] private float _enemyMeleePreferRange = 3f;
        [SerializeField, Min(0.1f)] private float _meleeTelegraph = 0.8f;
        [SerializeField, Min(0.5f)] private float _meleeRange = 2.4f;
        [SerializeField, Min(0f)] private float _meleeDamage = 15f;
        [SerializeField, Min(0.1f)] private float _rangedTelegraph = 1.2f;
        [Tooltip("화염구 비행 시간 — 임팩트 시각 = 텔레그래프 종료 + 비행")]
        [SerializeField, Min(0.05f)] private float _projectileFlight = 0.45f;
        [SerializeField, Min(0f)] private float _rangedDamage = 12f;
        [SerializeField] private Vector2 _attackCooldownRange = new Vector2(1.2f, 2.2f);

        public float MoveSpeed => _moveSpeed;
        public float LookSensitivity => _lookSensitivity;
        public float Gravity => _gravity;
        public float DodgeDistance => _dodgeDistance;
        public float DodgeDuration => _dodgeDuration;
        public float DodgeInvulnerable => _dodgeInvulnerable;
        public float DodgeCooldown => _dodgeCooldown;
        public float PlayerMaxHp => _playerMaxHp;
        public float LockOnCameraPull => _lockOnCameraPull;
        public bool ShoulderStart => _shoulderStart;
        public Vector3 ShoulderOffset => _shoulderOffset;
        public Vector3 ShoulderDrawOffset => _shoulderDrawOffset;
        public float CameraBlendSpeed => _cameraBlendSpeed;
        public float DrawCloseupBlendSpeed => _drawCloseupBlendSpeed;
        public float DrawCloseupFov => _drawCloseupFov;
        public float ShoulderPitchMin => _shoulderPitchMin;
        public float ShoulderPitchMax => _shoulderPitchMax;
        public float BodyShowDistance => _bodyShowDistance;
        public float InkStart => _inkStart;
        public float SpellInkCost => _spellInkCost;
        public float MisfireInkCost => _misfireInkCost;
        public float ParryInkCost => _parryInkCost;
        public float ParryInkRefund => _parryInkRefund;
        public float HarvestRange => _harvestRange;
        public float HarvestInkPerSecond => _harvestInkPerSecond;
        public float HarvestDamagePerSecond => _harvestDamagePerSecond;
        public float SpellProjectileSpeed => _spellProjectileSpeed;
        public float ParryWindow => _parryWindow;
        public float GuardDuration => _guardDuration;
        public float GuardBlockFactor => _guardBlockFactor;
        public float HalfParryDamageFactor => _halfParryDamageFactor;
        public int ParriesToBlossom => _parriesToBlossom;
        public float BlossomStunDuration => _blossomStunDuration;
        public float BlossomDamageMultiplier => _blossomDamageMultiplier;
        public float EnemyMaxHp => _enemyMaxHp;
        public float EnemyEngageRange => _enemyEngageRange;
        public float EnemyMeleePreferRange => _enemyMeleePreferRange;
        public float MeleeTelegraph => _meleeTelegraph;
        public float MeleeRange => _meleeRange;
        public float MeleeDamage => _meleeDamage;
        public float RangedTelegraph => _rangedTelegraph;
        public float ProjectileFlight => _projectileFlight;
        public float RangedDamage => _rangedDamage;
        public Vector2 AttackCooldownRange => _attackCooldownRange;
    }
}
