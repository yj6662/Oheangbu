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

        [Header("락온 대상 선정 [TEST — DECISIONS #139 2026-09-03: 사거리 안·화면 안 후보 중 화면 중앙 최근접]")]
        [Tooltip("락온 후보 사거리(m) — 카메라 기준")]
        [SerializeField, Min(1f)] private float _lockOnRange = 20f;
        [Tooltip("화면 밖 여유(뷰포트 비율) — 이만큼 벗어난 적까지 후보. 0=화면 안만")]
        [SerializeField, Range(0f, 0.5f)] private float _lockOnViewportMargin = 0.05f;
        [Tooltip("동률 가름 — 거리 가중(도/m): 화면 중앙 각에 더해 가까운 적을 우선")]
        [SerializeField, Min(0f)] private float _lockOnDistanceWeight = 0.5f;

        [Header("락온 중 작도 — 카메라 당김 규칙 [TEST — D308-21 · SPEC-LOCKON-DRAW-STABILITY-308]. 값이 없는 에셋은 아래 기본값으로 읽힌다")]
        [Tooltip("작도 중(작도 키를 누른 동안) 락온 당김. EdgeOnly=글자를 쓰는 동안 카메라 고정, 대상이 여유 구역을 벗어날 때만 느리게 따라감(권장 기본) / " +
            "FullPull=이전 동작(작도 중에도 계속 당김) / Hold=작도 중 당김 없음. 대상·레티클·유도 조준·그로기 표시는 어느 모드에서도 그대로다")]
        [SerializeField] private LockOnDrawMode _lockOnDrawMode = LockOnDrawMode.EdgeOnly;
        [Tooltip("작도 진입 때 당김이 0으로 가라앉는 시간(초, 실시간) — 클로즈업 블렌드와 겹친다. 0=즉시 멈춤. FullPull에서는 쓰지 않는다")]
        [SerializeField, Range(0f, 1f)] private float _lockOnDrawSettleSeconds = 0.25f;
        [Tooltip("작도가 끝난 뒤 당김이 돌아오기 전의 멈춤(초, 실시간) — 세상에 떼어 놓인 글자가 빛나는 동안 카메라가 밀지 않게")]
        [SerializeField, Range(0f, 2f)] private float _lockOnDrawResumeDelay = 0.5f;
        [Tooltip("멈춤 뒤 당김이 0에서 온전히 돌아오는 시간(초, 실시간) — 스냅 없이 서서히. 0=즉시")]
        [SerializeField, Range(0f, 3f)] private float _lockOnDrawResumeSeconds = 0.8f;
        [Tooltip("EdgeOnly 여유 구역 = 뷰포트 가장자리에서 이만큼 안쪽(x=좌우, y=상하, 0~0.49). 대상(뿌리+1.1 m)이 이 안에 있으면 카메라는 움직이지 않는다")]
        [SerializeField] private Vector2 _lockOnDrawEdgeMargin = new Vector2(0.25f, 0.2f);
        [Tooltip("EdgeOnly 추적 세기(초당) — 구역 밖으로 나간 각도 x 이 값 = 따라가는 각속도(상한까지). 대상이 움직이는 만큼만 따라 돈다")]
        [SerializeField, Range(0f, 10f)] private float _lockOnDrawEdgeGain = 4f;
        [Tooltip("EdgeOnly 추적 각속도 상한(도/초). 낮추면 더 느긋하지만 가까이서 옆걸음 칠 때 대상이 화면 밖으로 나간다" +
            "(오프라인 모의: 16이면 2 m 옆걸음에서 이탈, 45면 이탈 없음). 0=따라가지 않음(Hold와 같다)")]
        [SerializeField, Range(0f, 90f)] private float _lockOnDrawEdgeMaxSpeed = 45f;
        [Tooltip("EdgeOnly 추적 각가속도 상한(도/초²) — 시작·멈춤이 보이지 않게 속도를 서서히 바꾼다. 0=따라가지 않음")]
        [SerializeField, Range(0f, 360f)] private float _lockOnDrawEdgeAcceleration = 120f;
        [Tooltip("EdgeOnly 조준 유지(도) — 몸의 정면과 대상의 수평 각이 이보다 벌어지면 화면 안이어도 따라간다. " +
            "전방 부채꼴 술식(cone·다연발)이 몸의 정면을 쓰기 때문이다(부채꼴 반각보다 작게). 0=끔")]
        [SerializeField, Range(0f, 60f)] private float _lockOnDrawAimKeepDegrees = 20f;

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

        [Header("먹 경제 — 먹 3원: 갈무리 덩어리 뽑기 · 패링 순증 · 자연 회복(COMBAT-HARVEST, D306 [TEST])")]
        [SerializeField, Range(0f, 1f)] private float _inkStart = 1f;
        [SerializeField, Range(0f, 1f)] private float _spellInkCost = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _misfireInkCost = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _parryInkCost = 0.10f;
        [Tooltip("정답 패링 환급 — 소모 초과 환급이 곧 「순증」(COMBAT-PARRY)")]
        [SerializeField, Range(0f, 1f)] private float _parryInkRefund = 0.15f;

        [Header("먹 자연 회복 [TEST — D306: 모두에게 기본 회복, 오행 마석 등급=회복 배율]")]
        [Tooltip("초당 자연 회복(기본 용량 단위 — 0.05=빈 통에서 가득까지 20초). 0=끔")]
        [SerializeField, Range(0f, 1f)] private float _inkRegenPerSecond = 0.05f;
        [Tooltip("먹을 쓴 뒤 회복이 다시 시작되기까지(초, scaled)")]
        [SerializeField, Min(0f)] private float _inkRegenDelay = 1f;
        [Tooltip("자연 회복 상한(채움 비율) — 이 위는 갈무리·패링으로만")]
        [SerializeField, Range(0f, 1f)] private float _inkRegenCap = 1f;
        [Tooltip("비전투 배율(선택안 2) — 1=끔. 전투=락온 중이거나 최근 피격")]
        [SerializeField, Min(0f)] private float _inkRegenOutOfCombatMultiplier = 1f;
        [Tooltip("피격 뒤 전투로 보는 시간(초) — 비전투 배율 판정용")]
        [SerializeField, Min(0f)] private float _inkRegenCombatLinger = 5f;
        [Tooltip("오행 마석 등급 1당 회복 배율 가산(등급 3·0.25 → ×1.75)")]
        [SerializeField, Min(0f)] private float _inkRegenGradeBonus = 0.25f;

        [Header("갈무리 — 좌클릭 한 번 덩어리 뽑기 [TEST — D306]. 락온 한정·사거리·단독 처치 불가(HP 1 바닥)·그로기 없음")]
        [SerializeField, Min(0.5f)] private float _harvestRange = 12f;
        [Tooltip("뽑는 시간(초, scaled) — 끝에 한 덩어리를 끊는다. 회피·피격·작도·대상 사망·사거리 이탈=취소, 수입 0")]
        [SerializeField, Min(0.05f)] private float _harvestPullSeconds = 0.45f;
        [Tooltip("덩어리를 끊은 뒤 다음 뽑기까지(초, scaled). 취소에는 냉각 없음")]
        [SerializeField, Min(0f)] private float _harvestCooldown = 1.5f;
        [Tooltip("덩어리 하나의 먹 수입(기본 용량 단위)")]
        [SerializeField, Range(0f, 1f)] private float _harvestChunkInk = 0.30f;
        [Tooltip("덩어리 하나의 약피해 — 끊는 순간 한 번, HP 1 바닥")]
        [SerializeField, Min(0f)] private float _harvestChunkDamage = 2f;
        [Tooltip("끊는 순간 적 피격 반응 허용(한 번). 끄면 표현 계층이 Harvest 출처 피해에 반응하지 않아야 한다")]
        [SerializeField] private bool _harvestChunkReaction = true;
        [Tooltip("적 한 목숨당 덩어리 상한(선택안 3). 0=무제한")]
        [SerializeField, Min(0)] private int _harvestChunksPerLife = 0;
        [Tooltip("[LEGACY — #132 홀드 갈무리] 초당 먹 수급. 덩어리 뽑기에서는 읽지 않는다(구 검사 호환)")]
        [SerializeField, Range(0f, 1f)] private float _harvestInkPerSecond = 0.1f;
        [Tooltip("[LEGACY — #132 홀드 갈무리] 초당 약피해. 덩어리 뽑기에서는 읽지 않는다(구 검사 호환)")]
        [SerializeField, Min(0f)] private float _harvestDamagePerSecond = 4f;

        [Header("술식 투사체 [TEST 5차 검수 2026-08-28 — 피해=착탄 동기화]")]
        [Tooltip("공격 술식 투사체 속도(m/s) — 커밋 시 대상·비행시간 확정(락온 유도 보장), 착탄 시각에 피해. 글자별 배율은 SpellBook(규칙 계층)")]
        [SerializeField, Min(1f)] private float _spellProjectileSpeed = 18f;

        [Header("광역 cone 실판정 [TEST — SPELL-FIDELITY §4.1 · #137 §9-1 부분 해제]")]
        [Tooltip("전방 부채꼴 반각(도) — 수평 판정")]
        [SerializeField, Range(5f, 90f)] private float _areaConeAngle = 40f;
        [Tooltip("부채꼴 사거리(m)")]
        [SerializeField, Min(1f)] private float _areaConeRange = 10f;
        [Tooltip("커밋→판정까지 형성 딜레이(초) — 연출 분사 개시와 동기(§8 예외 1)")]
        [SerializeField, Min(0f)] private float _areaImpactDelay = 0.4f;

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
        [Tooltip("[TEST] 급소창 안 플레이어 직접 5속 완주 추가 피해. 순서 자유, 부분 보상 없음, 창당 1회. 이 추가 피해에는 급소 배율을 다시 곱하지 않는다.")]
        [SerializeField, Min(0f)] private float _fiveElementCompletionDamage = 30f;

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
        [Tooltip("적 시야 광선이 무시하는 레이어 이름 — #306 근접 자연물 충돌(NatureSolid)은 몸만 막고 시야는 막지 않는다 [TEST #308]")]
        [SerializeField] private string[] _enemySightIgnoreLayerNames = { "NatureSolid" };

        [Header("적 체력 표시 [TEST — D306 #8]: 락온 체력 획 · 교전 보스 바(속성색 없음)")]
        [Tooltip("보스 교전 기억(초, scaled) — 락온이 아니어도 이 시간 안에 서로 피해를 주고받았으면 보스 바를 보인다")]
        [SerializeField, Min(0f)] private float _bossEngageMemory = 8f;
        [Tooltip("보스 교전 거리(m) — 플레이어와 이보다 멀면 보스 바를 숨긴다(목줄 이탈 포함)")]
        [SerializeField, Min(1f)] private float _bossEngageRange = 45f;

        public string[] EnemySightIgnoreLayerNames => _enemySightIgnoreLayerNames;
        public float MoveSpeed => _moveSpeed;
        public float LookSensitivity => _lookSensitivity;
        public float Gravity => _gravity;
        public float DodgeDistance => _dodgeDistance;
        public float DodgeDuration => _dodgeDuration;
        public float DodgeInvulnerable => _dodgeInvulnerable;
        public float DodgeCooldown => _dodgeCooldown;
        public float PlayerMaxHp => _playerMaxHp;
        public float LockOnCameraPull => _lockOnCameraPull;
        public float LockOnRange => _lockOnRange;
        public float LockOnViewportMargin => _lockOnViewportMargin;
        public float LockOnDistanceWeight => _lockOnDistanceWeight;
        public LockOnDrawMode LockOnDrawMode => _lockOnDrawMode;
        public float LockOnDrawSettleSeconds => _lockOnDrawSettleSeconds;
        public float LockOnDrawResumeDelay => _lockOnDrawResumeDelay;
        public float LockOnDrawResumeSeconds => _lockOnDrawResumeSeconds;
        public Vector2 LockOnDrawEdgeMargin => _lockOnDrawEdgeMargin;
        public float LockOnDrawEdgeGain => _lockOnDrawEdgeGain;
        public float LockOnDrawEdgeMaxSpeed => _lockOnDrawEdgeMaxSpeed;
        public float LockOnDrawEdgeAcceleration => _lockOnDrawEdgeAcceleration;
        public float LockOnDrawAimKeepDegrees => _lockOnDrawAimKeepDegrees;
        // 규칙(LockOnDrawPull.Step)에 넘길 수치 한 묶음 — 값 형식이라 할당이 없다. pull·mode는 호출부가 정한다(검사 도구의 모드 재정의).
        public LockOnDrawPull.Settings GetLockOnDrawSettings(float pull, LockOnDrawMode mode) => new LockOnDrawPull.Settings
        {
            Mode = mode, Pull = pull,
            SettleSeconds = _lockOnDrawSettleSeconds, ResumeDelay = _lockOnDrawResumeDelay, ResumeSeconds = _lockOnDrawResumeSeconds,
            EdgeMarginX = _lockOnDrawEdgeMargin.x, EdgeMarginY = _lockOnDrawEdgeMargin.y,
            EdgeGain = _lockOnDrawEdgeGain, EdgeMaxSpeed = _lockOnDrawEdgeMaxSpeed, EdgeAcceleration = _lockOnDrawEdgeAcceleration,
            AimKeepDegrees = _lockOnDrawAimKeepDegrees,
        };
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
        public float InkRegenPerSecond => _inkRegenPerSecond;
        public float InkRegenDelay => _inkRegenDelay;
        public float InkRegenCap => _inkRegenCap;
        public float InkRegenOutOfCombatMultiplier => _inkRegenOutOfCombatMultiplier;
        public float InkRegenCombatLinger => _inkRegenCombatLinger;
        public float InkRegenGradeBonus => _inkRegenGradeBonus;
        public float HarvestPullSeconds => _harvestPullSeconds;
        public float HarvestCooldown => _harvestCooldown;
        public float HarvestChunkInk => _harvestChunkInk;
        public float HarvestChunkDamage => _harvestChunkDamage;
        public bool HarvestChunkReaction => _harvestChunkReaction;
        public int HarvestChunksPerLife => _harvestChunksPerLife;
        // [LEGACY — #132] 홀드 갈무리 수치. 런타임은 읽지 않는다
        public float HarvestInkPerSecond => _harvestInkPerSecond;
        public float HarvestDamagePerSecond => _harvestDamagePerSecond;
        public float SpellProjectileSpeed => _spellProjectileSpeed;
        public float AreaConeAngle => _areaConeAngle;
        public float AreaConeRange => _areaConeRange;
        public float AreaImpactDelay => _areaImpactDelay;
        public float ParryWindow => _parryWindow;
        public float GuardDuration => _guardDuration;
        public float GuardBlockFactor => _guardBlockFactor;
        public float HalfParryDamageFactor => _halfParryDamageFactor;
        public int ParriesToBlossom => _parriesToBlossom;
        public float BlossomStunDuration => _blossomStunDuration;
        public float BlossomDamageMultiplier => _blossomDamageMultiplier;
        public float FiveElementCompletionDamage => _fiveElementCompletionDamage;
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
        public float BossEngageMemory => _bossEngageMemory;
        public float BossEngageRange => _bossEngageRange;
    }
}
