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

        [Header("먹 경제 — 몸을 써야 먹이 찬다(COMBAT-HARVEST 먹 3원 중 2원)")]
        [SerializeField, Range(0f, 1f)] private float _inkStart = 1f;
        [SerializeField, Range(0f, 1f)] private float _spellInkCost = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _misfireInkCost = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _parryInkCost = 0.10f;
        [Tooltip("정답 패링 환급 — 소모 초과 환급이 곧 「순증」(COMBAT-PARRY)")]
        [SerializeField, Range(0f, 1f)] private float _parryInkRefund = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _harvestInkGain = 0.05f;

        [Header("갈무리 — 클릭·락온 한정·단독 처치 불가(COMBAT-HARVEST). 원거리다(예준 검수 2026-08-27)")]
        [SerializeField, Min(0.5f)] private float _harvestRange = 12f;
        [SerializeField, Min(0f)] private float _harvestDamage = 2f;
        [SerializeField, Min(0.05f)] private float _harvestCooldown = 0.5f;

        [Header("패링 — 판정점=글자 완성(COMBAT-PARRY)")]
        [Tooltip("임팩트 전 이 시간부터 성공창 개방(초). 임팩트 이후 완성=실패")]
        [SerializeField, Min(0.05f)] private float _parryWindow = 0.5f;
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
        public float InkStart => _inkStart;
        public float SpellInkCost => _spellInkCost;
        public float MisfireInkCost => _misfireInkCost;
        public float ParryInkCost => _parryInkCost;
        public float ParryInkRefund => _parryInkRefund;
        public float HarvestInkGain => _harvestInkGain;
        public float HarvestRange => _harvestRange;
        public float HarvestDamage => _harvestDamage;
        public float HarvestCooldown => _harvestCooldown;
        public float ParryWindow => _parryWindow;
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
