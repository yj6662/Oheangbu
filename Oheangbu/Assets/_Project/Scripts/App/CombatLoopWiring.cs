using System;
using System.Collections.Generic;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEngine;
using VContainer;

namespace Oheangbu.App
{
    // 전투 코어 루프의 배선부 — 채널·이벤트를 잇기만 하고 규칙을 만들지 않는다.
    // 규칙의 소유: 해석=SpellResolver / 판정=ParryJudge / 게이지=GroggyMeter·Vitals / 경제=InkPool.
    // 작도 계층(Drawing)은 여기서도 「읽기와 InterruptLetter 호출」만 — 인식 불가침 유지.
    public sealed partial class CombatLoopWiring : MonoBehaviour
    {
        [Header("채널 (기존 재사용)")]
        [SerializeField] private DrawnLetterEventChannelSO _letterDrawn;
        [SerializeField] private VoidEventChannelSO _misfired;
        [SerializeField] private FloatEventChannelSO _inkChanged;

        [Header("작도 계층")]
        [SerializeField] private DrawingInputController _drawingInput;
        [SerializeField] private BrushStrokeFeedAdapter _brushAdapter;

        [Header("전투 계층")]
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private PlayerVitals _playerVitals;
        [SerializeField] private HarvestAction _harvest;
        [SerializeField] private LockOn _lockOn;
        [SerializeField] private EnemyController _enemy;
        [SerializeField] private EnemyVitals _enemyVitals;
        [Tooltip("씬의 전체 적 — 광역 cone 실판정 대상 [#137]. 전역 탐색 대신 씬 배선(싱글턴 금지)")]
        [SerializeField] private EnemyVitals[] _enemies = System.Array.Empty<EnemyVitals>();
        [SerializeField] private Transform _playerTransform;
        [Tooltip("먹 자연 회복의 막힘·작도 판정 — 비우면 _playerVitals·_playerTransform에서 찾는다(D306)")]
        [SerializeField] private PlayerMotor _motor;

        [Header("표현")]
        [SerializeField] private ElementPaletteSO _palette;
        [SerializeField] private HudController _hud;
        [SerializeField] private SpellVFX120.KtpContactProfile _contactVfx;
        private readonly List<SpellVFX120.KtpContactEffect> _contacts = new List<SpellVFX120.KtpContactEffect>();
        [Tooltip("덩어리 뽑기 이펙트 — 덩어리가 붓에 닿는 순간 HUD 먹 획이 번쩍인다(값은 끊는 순간 즉시). 비우면 플레이어 루트에서 찾는다(D306)")]
        [SerializeField] private HarvestInkStreamEffect _harvestVfx;

        [Header("비락온 자유 조준의 프로토 근사(§10.1) — 전방 원뿔 명중")]
        [SerializeField, Range(1f, 45f)] private float _freeAimAngle = 15f;
        [SerializeField, Min(1f)] private float _freeAimRange = 20f;
        [SerializeField] private bool _environmentOcclusion;

        private SpellResolver _resolver;
        private ParryJudge _judge;
        private GroggyMeter _groggy;
        private InkPool _ink;
        private float _lastPlayerHitTime = float.NegativeInfinity;
        // 보스 교전(D306 #8 [TEST]) — _targets와 같은 순번. 서로 피해를 주고받은 시각과 그때의 LifeRevision(되살림·목줄 초기화면 무효)
        private float[] _bossContactAt = Array.Empty<float>();
        private uint[] _bossContactLife = Array.Empty<uint>();
        private EnemyVitals _engagedBoss;
        private uint _engagedBossLife;
        private EnemyVitals _diedBoss306; // 이번 프레임에 죽은 교전 보스 — HUD에 한 번 넘겨 빈 바 유지(BossDeadHoldMs)를 시작한다
        public EnemyVitals EngagedBoss => _engagedBoss;
        // 먹 자연 회복(D306 [TEST]) — 오행 마석 등급은 캠페인이 InkRegen.SetGrade로 알린다
        public InkRegenerator InkRegen { get; private set; }
        public HarvestAction Harvest => _harvest;

        // 날아가는 술식 [TEST 5차 검수] — 커밋 시 대상·착탄 시각 확정(유도 보장), 착탄에 피해.
        // 적 화염구와 같은 문법: 시각이 규칙이고, 비행은 연출이 따라온다
        private struct PendingCast
        {
            public EnemyVitals Target;
            public float ImpactTime;
            public Element Element;
            public char Letter;
            public float Power;
            public Vector3 Origin;
            public AttackProvenance Attack;
            public uint TargetLifeRevision;
        }
        private char _guardVisualLetter;
        private uint _guardVisualRevision;

        private readonly List<PendingCast> _pendingCasts = new List<PendingCast>();

        // 씬의 적 컨트롤러 전수 — _enemy + _enemies에서 파생(과녁처럼 컨트롤러 없는 적은 자연 제외, 중복 제거).
        // 판정기 주입·텔레그래프 색·만개 스턴 배선은 적이 몇이든 이 파일의 몫이다
        private readonly List<EnemyController> _controllers = new List<EnemyController>();

        // 조준 후보 전수 — _enemyVitals + _enemies(중복 제거). 락온(LockOn)과 자유 조준이 같은 집합을 본다
        private readonly List<EnemyVitals> _targets = new List<EnemyVitals>();

        // 판정 결과의 표현 측 재방송(읽기 전용) — 하네스(패링장 판정판)가 구독한다. 규칙 분기는 OnParryImpactResolved 그대로
        public event Action<ParryOutcome, Element, Vector3> ParryResolved;

        // 시전 판정 계획 재방송(읽기 전용) — 하네스(사격장)가 계획 대 실제 착탄을 대조한다 [SPELL-AREA-SHAPES §3]
        public event Action<CastPlan> CastPlanned;

        // 먹 지출까지 승인된 소환의 표현 포즈 재방송. 전투 대상·피해·AI는 만들지 않는다.
        public event Action<SpellCast, Vector3, Vector3> SummonAccepted;

        // 먹 지출과 규칙 분기를 통과한 실제 시전. 인식 성공만으로는 발화하지 않는다.
        public event Action<SpellCast, Vector3, Vector3> CastAccepted;

        // 예약만 된 계획이 아니라 실제 생존 대상에게 적용된 술식 착탄.
        public event Action<Vector3, Element> EnemyHitResolved;
        public event Action<EnemyDamageResult> EnemyDamageResolved;
        public EnemyVitals GroggyHudTarget => _lockOn != null ? _lockOn.Target : null;
        // Campaign-owned runtime upgrades; no shared CombatConfig asset mutation.
        public Func<Element, float> PlayerDamageScale { get; set; }
        // Optional demo owner. Scenes without it retain the approved static summon preview.
        public Demo.DemoSummonCombatManager SummonCombat { get; set; }
        public Demo.FieldSpellService FieldSpells { get; set; }
        public Demo.MumBridgeService MumBridges { get; set; }
        public bool FieldPlacementPreviewRequested => _drawingInput != null && _drawingInput.InDrawMode;
        public EABuffRuntime EABuffs { get; private set; }
        public EAWardRuntime EAWards { get; private set; }
        public EAGiyeokRuntime EAGiyeok { get; private set; }
        public void ConfigureEAGiyeok(EAGiyeokProfileSO profile, Func<bool> unlocked)
        {
            EAGiyeok?.Dispose();EAGiyeok = profile != null ? new EAGiyeokRuntime(profile,this,_playerVitals,unlocked) : null;
        }
        public void ConfigureEAWards(EAWardProfileSO profile)
        {
            EAWards?.Dispose();
            EAWards = profile != null ? new EAWardRuntime(profile, _playerVitals, this) : null;
        }
        public void ConfigureEABuffs(EABuffProfileSO profile, Func<bool> unlocked)
        {
            EABuffs?.Dispose();
            EABuffs = profile != null ? new EABuffRuntime(profile, _playerVitals, this, unlocked, Time.time) : null;
        }
        private void OnDestroy() { EABuffs?.Dispose(); EABuffs = null; EAWards?.Dispose(); EAWards = null; EAGiyeok?.Dispose(); EAGiyeok = null; }
        public IReadOnlyList<EnemyVitals> SummonTargets => _targets;
        public EnemyVitals SummonLockTarget => _lockOn != null ? _lockOn.Target : null;
        public Transform SummonPlayer => _playerTransform != null ? _playerTransform : transform;
        public float SummonDamageScale(Element element)
        {
            float scale = PlayerDamageScale != null ? PlayerDamageScale(element) : 1f;
            return float.IsFinite(scale) ? Mathf.Max(0, scale) : 1f;
        }
        public void NotifyCombatSummonStarted(char letter, Vector3 point)
        { _brushAdapter?.NotifyExternalSummonStarted(letter, point); }
        public void NotifyCombatSummonReleased(char letter, Vector3 point)
        { _brushAdapter?.NotifyExternalSummonReleased(letter, point); }

        [Inject]
        public void Construct(SpellResolver resolver, ParryJudge judge, GroggyMeter groggy, InkPool ink)
        {
            _resolver = resolver;
            _judge = judge;
            _groggy = groggy;
            _ink = ink;
            InkRegen = _ink != null && _config != null ? new InkRegenerator(_config, _ink) : null;
        }

        private void Awake()
        {
            if (_motor == null) _motor = _playerVitals != null ? _playerVitals.GetComponent<PlayerMotor>() : _playerTransform != null ? _playerTransform.GetComponent<PlayerMotor>() : null;
            if (_harvestVfx == null && _harvest != null) _harvestVfx = _harvest.transform.root.GetComponentInChildren<HarvestInkStreamEffect>(true);
            CollectControllers();
            if (_contactVfx == null)
                _contactVfx = Resources.Load<SpellVFX120.KtpContactProfile>(SpellVFX120.KtpContactProfile.ResourcePath);
        }

        private void CollectControllers()
        {
            _targets.Clear();
            if (_enemyVitals != null) _targets.Add(_enemyVitals);
            foreach (var vitals in _enemies)
            {
                if (vitals != null && !_targets.Contains(vitals)) _targets.Add(vitals);
            }

            _bossContactAt = new float[_targets.Count]; _bossContactLife = new uint[_targets.Count];
            for (int i = 0; i < _bossContactAt.Length; i++) _bossContactAt[i] = float.NegativeInfinity;
            _controllers.Clear();
            if (_enemy != null) _controllers.Add(_enemy);
            foreach (var vitals in _targets)
            {
                var controller = vitals.GetComponent<EnemyController>();
                if (controller != null && !_controllers.Contains(controller)) _controllers.Add(controller);
            }
        }

        private void Start()
        {
            // 텔레그래프 색 = 팔레트가 단일 출처(색=의미) — 적 자신의 원거리 속성 초성 기본색 / 무속성=무채
            var neutral = new Color(0.45f, 0.43f, 0.41f);
            foreach (var controller in _controllers)
            {
                controller.Init(_judge);
                if (_palette != null)
                {
                    controller.SetTelegraphColors(_palette.GetBaseColor(InitialOf(controller.RangedElement)), neutral);
                }
            }
            _harvest?.Init(_ink);
            _lockOn?.SetCandidates(_targets); // 락온 후보=씬 배선 전수 — 선정 규칙은 LockOn이 소유(#139)

            _ink?.Broadcast();
            RefreshHud();
        }

        private void OnEnable()
        {
            if (_letterDrawn != null) _letterDrawn.Subscribe(OnLetterDrawn);
            if (_misfired != null) _misfired.Subscribe(OnMisfired);
            if (_inkChanged != null) _inkChanged.Subscribe(OnInkChanged);
            if (_playerVitals != null) _playerVitals.Damaged += OnPlayerDamaged;
            if (_playerVitals != null) _playerVitals.HpChanged += RefreshHud;
            if (_playerVitals != null) _playerVitals.Died += OnPlayerDied;
            if (_lockOn != null) _lockOn.Changed += RefreshHud;
            if (_harvestVfx != null) _harvestVfx.ChunkAbsorbed += OnHarvestChunkAbsorbed;
            TryHookServices(); // 재활성화 시 첫 프레임 구독 공백 방지(주입 완료 후엔 즉시 성공)
        }

        private void OnDisable()
        {
            EABuffs?.Clear(Time.time); EAWards?.Clear(); EAGiyeok?.Clear();
            SummonCombat?.Clear();
            _summonResolved.Clear();
            if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
            if (_misfired != null) _misfired.Unsubscribe(OnMisfired);
            if (_inkChanged != null) _inkChanged.Unsubscribe(OnInkChanged);
            if (_playerVitals != null) _playerVitals.Damaged -= OnPlayerDamaged;
            if (_playerVitals != null) _playerVitals.HpChanged -= RefreshHud;
            if (_playerVitals != null) _playerVitals.Died -= OnPlayerDied;
            if (_lockOn != null) _lockOn.Changed -= RefreshHud;
            if (_harvestVfx != null) _harvestVfx.ChunkAbsorbed -= OnHarvestChunkAbsorbed;
            UnhookServices();
            ClearBossEngagement(); if (_hud != null) { _hud.SetTargetHealth(null); _hud.SetBossHealth(null); } // 씬 해제 중 파괴된 HUD 보호
            ResetEncounterGroggy();
            _pendingCasts.Clear(); // 비활성 동안의 기한 지난 착탄이 재활성 시 유령 피해가 되지 않게
            _guardVisualLetter = default;
            foreach (var effect in _contacts) if (effect != null) Destroy(effect.gameObject);
            _contacts.Clear();
        }

        private bool _hooked;

        private void Update()
        {
            TryHookServices();
            EABuffs?.Tick(Time.time); EAWards?.Tick(Time.time); EAGiyeok?.Tick(Time.time);
            TickPendingCasts();
            TickInkRegen();
            _contacts.RemoveAll(effect => effect == null);
        }

        // 자연 회복 — 메뉴·입력 막힘·작도 중·사망 중에는 돌지 않는다. scaled dt(정지=멈춤, 작도 감속=느려짐)
        private void TickInkRegen()
        {
            if (InkRegen == null && _ink != null && _config != null) InkRegen = new InkRegenerator(_config, _ink);
            if (InkRegen == null) return;
            bool drawing = _drawingInput != null && _drawingInput.InDrawMode || _motor != null && _motor.IsDrawing;
            bool blocked = _motor != null && (_motor.InputBlocked || !_motor.isActiveAndEnabled) || _playerVitals != null && _playerVitals.Hp01 <= 0f;
            bool inCombat = _lockOn != null && _lockOn.Target != null || Time.time - _lastPlayerHitTime < _config.InkRegenCombatLinger;
            InkRegen.Tick(Time.time, Time.deltaTime, drawing, blocked, inCombat);
        }

        // 착탄 시각 도래 = 피해 적용(피해=착탄 동기화 — 5차 검수). 대상이 먼저 죽었으면 허공이 된다
        private void TickPendingCasts()
        {
            for (int i = _pendingCasts.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pendingCasts[i].ImpactTime) continue;
                PendingCast pending = _pendingCasts[i];
                _pendingCasts.RemoveAt(i);
                if (pending.Target != null && pending.Target.IsAlive && pending.Target.isActiveAndEnabled &&
                    pending.Target.LifeRevision == pending.TargetLifeRevision && TargetVisible(pending.Target,pending.Origin))
                {
                    ApplyConfirmedEnemyHit(pending.Target, pending.Power, pending.Attack, pending.Letter);
                }
            }
        }

        // [Inject]는 씬 로드 직후 실행되므로 서비스 이벤트는 OnEnable(재활성) 또는 첫 프레임에 건다
        private void TryHookServices()
        {
            if (_hooked || _judge == null) return;
            if (_groggy != null) _groggy.ResetRequested += ResetEncounterGroggy;
            if (_ink != null) _ink.Gained += OnInkReceived;
            _judge.OwnedImpactResolved += OnOwnedParryImpactResolved;
            foreach (var target in _targets)
                if (target != null) { target.CombatStateChanged += RefreshHud; target.DamageResolved += OnTargetDamageResolved; }
            _hooked = true;
        }

        private void UnhookServices()
        {
            if (!_hooked) return;
            if (_groggy != null) _groggy.ResetRequested -= ResetEncounterGroggy;
            if (_ink != null) _ink.Gained -= OnInkReceived;
            if (_judge != null) _judge.OwnedImpactResolved -= OnOwnedParryImpactResolved;
            foreach (var target in _targets)
                if (target != null) { target.CombatStateChanged -= RefreshHud; target.DamageResolved -= OnTargetDamageResolved; }
            _hooked = false;
        }

        // 덩어리 수입은 값이 즉시 들어오고, 번쩍임만 덩어리가 붓에 닿을 때로 미룬다(이펙트가 없거나 못 받으면 즉시)
        private void OnInkReceived(float received)
        {
            // 덩어리 번쩍임 띠는 Gain 직후의 먹 값을 함께 맡긴다(비행 중 패링 환급·지출로 띠가 밀리지 않게)
            if (_harvest != null && _harvest.PayingChunk && _harvestVfx != null && _harvestVfx.QueueChunkFeedback(received, _ink != null ? _ink.Value : 1f)) return;
            _hud?.NotifyInkGained(received);
        }
        private void OnHarvestChunkAbsorbed(float received, float to01, float kick)
        { _hud?.NotifyInkGained(received, to01); if (kick > 0f) _hud?.KickInk(kick); }

        private void LateUpdate()
        {
            _hud?.UpdateReticle(_lockOn != null && _lockOn.Target != null ? _lockOn.Target.transform : null, Camera.main);
            TickHealthBars();
        }

        // ---- 적 체력 표시(D306 #8 [TEST]) — 계약 스텁에 매 프레임 넘긴다(그리기는 HUD 몫). 할당 없음 ----
        // 락온 체력 획 = 락온 대상(프로필 ShowLockOnBar), 교전 중 보스는 획 대신 하단 보스 바.
        private void TickHealthBars()
        {
            if (_hud == null) return;
            EnemyVitals locked = _lockOn != null ? _lockOn.Target : null;
            EnemyVitals boss = SelectEngagedBoss(locked);
            // 교전 중 보스가 방금 죽었으면 그 보스를 한 번 넘긴다(빈 바 유지 → 소멸). 이어지는 null은 유지를 끊지 않는다
            if (boss == null && _diedBoss306 != null) _hud.SetBossHealth(_diedBoss306); else _hud.SetBossHealth(boss);
            _diedBoss306 = null;
            if (locked != null && locked.IsAlive && locked.ShowLockOnBar && locked != boss) _hud.SetTargetHealth(locked); else _hud.SetTargetHealth(null);
        }

        // 교전 = 살아 있는 IsBoss 적이 (락온 중이거나 BossEngageMemory초 안에 서로 피해를 주고받았고) BossEngageRange 안.
        // 죽음·Restore/목줄 초기화(LifeRevision 변화)·플레이어 사망에 풀린다
        private EnemyVitals SelectEngagedBoss(EnemyVitals locked)
        {
            if (_config == null || _playerVitals != null && _playerVitals.Hp01 <= 0f) { ClearBossEngagement(); return null; }
            if (_engagedBoss != null && (_engagedBoss.LifeRevision != _engagedBossLife || !BossEngaged(_targets.IndexOf(_engagedBoss), locked)))
            {
                if (!_engagedBoss.IsAlive) _diedBoss306 = _engagedBoss; // 처치(초기화·목줄 복원은 살아 있다)
                _engagedBoss = null;
            }
            if (_engagedBoss != null && (locked == null || locked == _engagedBoss || !locked.IsBoss)) return _engagedBoss; // 다른 보스를 락온하면 그쪽으로 바꾼다
            int best = -1; float bestAt = float.NegativeInfinity;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (!BossEngaged(i, locked)) continue;
                float at = _targets[i] == locked ? float.PositiveInfinity : _bossContactAt[i];
                if (best < 0 || at > bestAt) { best = i; bestAt = at; }
            }
            if (best < 0) return null;
            _engagedBoss = _targets[best]; _engagedBossLife = _engagedBoss.LifeRevision;
            return _engagedBoss;
        }

        private bool BossEngaged(int index, EnemyVitals locked)
        {
            if (index < 0 || index >= _targets.Count || index >= _bossContactAt.Length) return false;
            var boss = _targets[index];
            if (boss == null || !boss.IsBoss || !boss.IsAlive || !boss.isActiveAndEnabled) return false;
            if ((boss.transform.position - SummonPlayer.position).sqrMagnitude > _config.BossEngageRange * _config.BossEngageRange) return false;
            return boss == locked || _bossContactLife[index] == boss.LifeRevision && Time.time - _bossContactAt[index] <= _config.BossEngageMemory;
        }

        private void MarkBossContact(int index)
        {
            if (index < 0 || index >= _bossContactAt.Length) return;
            var boss = _targets[index];
            if (boss == null || !boss.IsBoss || !boss.IsAlive) return;
            _bossContactAt[index] = Time.time; _bossContactLife[index] = boss.LifeRevision;
        }

        // 플레이어 쪽 출처(직접·소환·지속 술식·갈무리·완주)의 실피해만 교전으로 친다
        private void OnTargetDamageResolved(EnemyDamageResult result)
        {
            if (result.AppliedDamage <= 0f || result.Target == null || !result.Target.IsBoss) return;
            var source = result.Attack.Source;
            if (source == DamageSource.Enemy || source == DamageSource.Unknown) return;
            MarkBossContact(_targets.IndexOf(result.Target));
        }

        // 적 → 플레이어 피해에는 가해자 출처가 없다(PlayerVitals.Damaged) — 사거리 안 가장 가까운 산 적이 보스일 때만 보스로 돌린다
        // [TEST 근사]: 보스 근처 잡몹 싸움이 보스 바를 띄우지 않게. 속성 공격은 방어 판정의 Instigator로 정확히 잡는다
        private void MarkPlayerHitBoss()
        {
            if (_config == null) return;
            int best = -1; float bestSqr = _config.BossEngageRange * _config.BossEngageRange;
            Vector3 player = SummonPlayer.position;
            for (int i = 0; i < _targets.Count; i++)
            {
                var enemy = _targets[i];
                if (enemy == null || !enemy.IsAlive || !enemy.isActiveAndEnabled) continue;
                float sqr = (enemy.transform.position - player).sqrMagnitude;
                if (sqr <= bestSqr) { best = i; bestSqr = sqr; }
            }
            MarkBossContact(best); // 보스가 아니면 MarkBossContact가 거른다
        }

        private void ClearBossEngagement()
        {
            _engagedBoss = null; _diedBoss306 = null;
            if (_hud != null) _hud.HideBossHealthNow(); // 플레이어 사망·비활성: 빈 바 유지 중이어도 즉시 치운다(부활 위에 남지 않게)
            for (int i = 0; i < _bossContactAt.Length; i++) _bossContactAt[i] = float.NegativeInfinity;
        }

        // ---- 작도 → 전투: 커밋된 글자 하나가 술식 한 발이 된다 ----
        private void OnLetterDrawn(DrawnLetter letter)
        {
            if (_resolver == null || _config == null) return;

            if (!_resolver.TryResolve(letter, out SpellCast cast, FieldSpells != null && FieldSpells.IsUnlocked, EABuffs != null && EABuffs.Unlocked, EAWards != null && EAWards.Available, EAGiyeok != null && EAGiyeok.Unlocked, MumBridges != null && MumBridges.IsUnlocked))
            {
                // 프로토 미러 밖의 글자 — 효과 없음, 먹만 소모(불발 취급). CSV 완주는 임포터 이후.
                // 표현도 불발을 따른다(7차 검수) — 플래시·문양 대신 증발
                _ink?.SpendClamped(_config.MisfireInkCost);
                _brushAdapter?.NotifyCastFailed();
                return;
            }

            if (EABuffs != null)
                cast = new SpellCast(cast.Letter, cast.Kind, cast.Element,
                    cast.Power * EABuffs.HoldPower(letter.HoldDuration, Time.time), cast.Area, cast.SpeedMul, EABuffs.HoldPower(letter.HoldDuration, Time.time));
            switch (cast.Kind)
            {
                case SpellKind.Parry:
                    ResolveParry(cast);
                    break;
                case SpellKind.AttackSingle:
                case SpellKind.AttackArea:
                    ResolveAttack(cast);
                    break;
                case SpellKind.Summon:
                    ResolveSummon(cast);
                    break;
                case SpellKind.Field:
                    ResolveField(cast);
                    break;
                case SpellKind.Ward:
                    ResolveWard(cast);
                    break;
                case SpellKind.Buff:
                    ResolveBuff(cast);
                    break;
            }
        }

        private bool TrySpendSpell(float cost) => _ink != null && _ink.TrySpend(cost * (EABuffs?.CostScale(Time.time) ?? 1f));
        private void OnPlayerDied() { EABuffs?.Clear(Time.time); EAWards?.Clear(); EAGiyeok?.Clear(); ResetEncounterGroggy(); ClearBossEngagement(); }
        private void ResolveWard(SpellCast cast)
        {
            if (EAWards == null || !EAWards.TryPrepare(cast.Letter, out var centre) || !TrySpendSpell(_config.SpellInkCost))
            { _brushAdapter?.NotifyCastFailed(); return; }
            if (!EAWards.Activate(cast.Letter, centre, Time.time)) { _brushAdapter?.NotifyCastFailed(); return; }
            if (EAWards.HasVisual) _brushAdapter?.NotifyFieldPresentationOwned();
            CastAccepted?.Invoke(cast, centre, PlayerForward());
        }
        private void ResolveBuff(SpellCast cast)
        {
            if (EABuffs == null || !EABuffs.CanActivate(cast.Letter) || !TrySpendSpell(_config.SpellInkCost))
            { _brushAdapter?.NotifyCastFailed(); return; }
            if (!EABuffs.Activate(cast.Letter, Time.time)) { _brushAdapter?.NotifyCastFailed(); return; }
            if (EABuffs.HasVisual(cast.Letter)) _brushAdapter?.NotifyFieldPresentationOwned();
            CastAccepted?.Invoke(cast, PlayerPosition(), PlayerForward());
        }

        public EnemyDamageResult ApplyGiyeokDirectHit(EnemyVitals target,uint life,float power,Vector3 origin,AttackProvenance attack,char letter)
        {
            if(!isActiveAndEnabled||target==null||!target.IsAlive||!target.isActiveAndEnabled||target.LifeRevision!=life
                ||!_targets.Contains(target)||!float.IsFinite(power)||power<=0||attack.Source!=DamageSource.PlayerDirect
                ||attack.AttackId<=0||attack.Instigator==null||!EAGiyeokRuntime.Owns(letter)||!GiyeokTargetVisible(target,origin,letter=='삭'))return default;
            return ApplyConfirmedEnemyHit(target,power,attack,letter);
        }
        private bool GiyeokTargetVisible(EnemyVitals target,Vector3 origin,bool piercesActors)
        {
            if(!_environmentOcclusion)return true;
            var delta=target.transform.position+Vector3.up*.4f-origin;
            int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,origin,delta,delta.magnitude,~0,ref _summonOcclusionHits);
            for(int i=0;i<count;i++)
            {
                var hit=_summonOcclusionHits[i];
                if(hit.collider.isTrigger||hit.transform.IsChildOf(target.transform)||hit.transform.IsChildOf(SummonPlayer))continue;
                if(piercesActors&&hit.transform.GetComponentInParent<EnemyVitals>()!=null)continue;
                return false;
            }
            return true;
        }
        public EnemyDamageResult ApplyPersistentSpellHit(EnemyVitals target, float power, Vector3 origin, AttackProvenance attack, char letter)
        {
            if (!isActiveAndEnabled || target == null || !target.IsAlive || !target.isActiveAndEnabled ||
                !_targets.Contains(target) || !float.IsFinite(power) || power <= 0 || attack.Source != DamageSource.PersistentSpell) return default;
            Vector3 delta = target.transform.position + Vector3.up * .4f - origin;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, origin, delta, delta.magnitude, ~0, ref _summonOcclusionHits);
            for (int i = 0; i < count; i++)
            {
                var hit = _summonOcclusionHits[i].transform;
                if (hit.IsChildOf(target.transform) || hit.IsChildOf(SummonPlayer) || hit.GetComponentInParent<EnemyVitals>() != null) continue;
                return default;
            }
            return ApplyConfirmedEnemyHit(target, power, attack, letter);
        }

        private void ResolveField(SpellCast cast)
        {
            if(cast.Letter=='뭄')
            {
                if(MumBridges==null||!MumBridges.TryPrepare(cast,out _)){_brushAdapter?.NotifyCastFailed();return;}
                float before=_ink!=null?_ink.Value:0;
                if(_ink==null||_config==null||!TrySpendSpell(_config.SpellInkCost))
                {MumBridges.CancelPrepared();_brushAdapter?.NotifyCastFailed();return;}
                if(!MumBridges.CommitPrepared()){_ink.Restore(before);_brushAdapter?.NotifyCastFailed();return;}
                _brushAdapter?.NotifyFieldPresentationOwned();CastAccepted?.Invoke(cast,PlayerPosition(),PlayerForward());return;
            }
            if (FieldSpells == null || !FieldSpells.TryPrepare(cast, out _))
            { _brushAdapter?.NotifyCastFailed(); return; }
            float fieldInkBefore=_ink!=null?_ink.Value:0;
            if (_ink == null || _config == null || !TrySpendSpell(_config.SpellInkCost))
            { FieldSpells.CancelPrepared(); _brushAdapter?.NotifyCastFailed(); return; }
            if (!FieldSpells.CommitPrepared()) { _ink.Restore(fieldInkBefore); _brushAdapter?.NotifyCastFailed(); return; }
            _brushAdapter?.NotifyFieldPresentationOwned();
            CastAccepted?.Invoke(cast, PlayerPosition(), PlayerForward());
        }

        private void ResolveSummon(SpellCast cast)
        {
            Vector3 origin = PlayerPosition();
            Vector3 forward = PlayerForward();
            bool combat = SummonCombat != null && SummonCombat.isActiveAndEnabled && SummonCombat.Supports(cast.Letter);
            if (combat && !SummonCombat.TryPrepare(cast, origin, forward, out _))
            {
                _brushAdapter?.NotifyCastFailed();
                return;
            }
            // Validate/create the inactive candidate before spending. A failed replacement keeps the old actor.
            if (_ink == null || _config == null || !TrySpendSpell(_config.SpellInkCost * (combat ? 2f : 1f)))
            {
                if (combat) SummonCombat.CancelPrepared();
                _brushAdapter?.NotifyCastFailed();
                return;
            }
            if (combat) _brushAdapter?.ClearActiveSummons();
            else SummonCombat?.Clear();
            _brushAdapter?.SetPatternSummonPose(cast.Letter, origin, forward, combat);
            CastAccepted?.Invoke(cast, origin, forward);
            SummonAccepted?.Invoke(cast, origin, forward);
        }

        private EnemyDamageResult ApplyConfirmedEnemyHit(EnemyVitals target, float power, AttackProvenance attack, char letter)
        {
            var result = target.TakeDamage(power, attack);
            if (result.AppliedDamage <= 0) return result;
            var point = target.transform.position + Vector3.up * .8f;
            EnemyDamageResolved?.Invoke(result);
            if (attack.Element.HasValue)
            {
                var element = attack.Element.Value;
                EnemyHitResolved?.Invoke(point, element);
                if (_contactVfx != null) SpawnContact(_contactVfx.ParrySource(element), point, _contactVfx.ParryScale, element, letter);
            }
            return result;
        }

        private RaycastHit[] _summonOcclusionHits = new RaycastHit[16];
        private readonly HashSet<(long attack, int target, uint life)> _summonResolved = new HashSet<(long, int, uint)>();
        public EnemyDamageResult ApplySummonHit(EnemyVitals target, uint lifeRevision, float power,
            Vector3 origin, AttackProvenance attack, char letter)
        {
            if (!isActiveAndEnabled || target == null || !target.isActiveAndEnabled || !target.IsAlive ||
                target.LifeRevision != lifeRevision || attack.Source != DamageSource.Summon ||
                attack.AttackId <= 0 || attack.Instigator == null || !attack.Element.HasValue ||
                !float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z) ||
                !float.IsFinite(power) || power <= 0 || !_targets.Contains(target)) return default;
            var key = (attack.AttackId, target.GetInstanceID(), lifeRevision);
            if (_summonResolved.Contains(key)) return default;
            Vector3 delta = target.transform.position + Vector3.up * .4f - origin;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, origin, delta, delta.magnitude,
                ~0, ref _summonOcclusionHits);
            for (int i = 0; i < count; i++)
            {
                var hit = _summonOcclusionHits[i].transform;
                if (hit.IsChildOf(target.transform) || hit.IsChildOf(SummonPlayer) ||
                    hit.GetComponentInParent<EnemyVitals>() != null) continue;
                return default;
            }
            if (_summonResolved.Count >= 4096) _summonResolved.Clear();
            _summonResolved.Add(key);
            return ApplyConfirmedEnemyHit(target, power, attack, letter);
        }

        private void ResolveParry(SpellCast cast)
        {
            _guardVisualLetter = default;
            // 먹 부족 = 불발 취급(§10.1) — 방어막 자체가 서지 않고, 표현도 증발한다(7차 검수:
            // 없는 방어를 개화로 보여주지 않는다)
            if (_ink == null || !TrySpendSpell(_config.ParryInkCost))
            {
                _brushAdapter?.NotifyCastFailed();
                return;
            }

            // [TEST §10.1 2차 플레이 검수] 작도 잔존 방어막: 완성이 방어막을 세우고, 판정은 임팩트가 한다.
            // 성공 보상(그로기·환급·이펙트)은 OnParryImpactResolved에서 — 판정점이 임팩트로 옮겨갔으므로.
            // ⚠ 어휘 CSV 「허공 시전 잔존 없음」의 전이 실험 — 채택 시 DECISIONS+CSV 정본 반영 필요
            _judge?.RaiseGuard(cast.Element, Time.time, cast.HoldScale);
            if (_judge != null) _brushAdapter?.SetPatternGuardClock(_judge.GuardLifetime, _judge.GuardWindow);
            if (_judge != null) { _guardVisualLetter = cast.Letter; _guardVisualRevision = _judge.GuardRevision; }
            CastAccepted?.Invoke(cast, PlayerPosition(), PlayerForward());
        }

        // 방어막에 임팩트가 닿은 순간(판정점) — 성공만 보상이 있다(그로기의 유일한 증가 경로 유지)
        private void OnOwnedParryImpactResolved(ParryImpactResult result)
        {
            // Legacy diagnostic calls have no owner: retain their feedback/refund but never guess a target.
            OnParryImpactResolved(result.Outcome, result.GuardElement, result.Point);
            if (result.Attack.Instigator is EnemyVitals source && source != null && source.IsBoss) MarkBossContact(_targets.IndexOf(source)); // 막아낸 보스 공격도 교전
            ApplyParryProgress(result);
        }

        private void ApplyParryProgress(ParryImpactResult result)
        {
            if (result.Outcome == ParryOutcome.Success && result.Attack.Instigator is EnemyVitals attacker &&
                _targets.Contains(attacker)) { attacker.AddParry(result.Attack); OnParryCountered306(result, attacker); }
        }

        private void OnParryImpactResolved(ParryOutcome outcome, Element guardElement, Vector3 impactPoint)
        {
            ParryResolved?.Invoke(outcome, guardElement, impactPoint);
            bool originalContact = false;
            if (_contactVfx != null && (outcome == ParryOutcome.Success || outcome == ParryOutcome.Half))
            {
                float scale = _contactVfx.ParryScale * (outcome == ParryOutcome.Half ? _contactVfx.HalfScale : 1f);
                char letter = _judge != null && _guardVisualRevision == _judge.GuardRevision ? _guardVisualLetter : default;
                originalContact = SpawnContact(_contactVfx.ParrySource(guardElement), impactPoint, scale,guardElement,letter);
            }
            if(outcome == ParryOutcome.Success || outcome == ParryOutcome.Fail || outcome == ParryOutcome.None)
                _guardVisualLetter = default;
            bool authoredGuardContact = outcome == ParryOutcome.Success && ((guardElement == Element.Wood
                && SpellVFX120.Vfx120Effect.TrySignalBambooParry(impactPoint, originalContact)) || (guardElement == Element.Fire && SpellVFX120.Vfx120Effect.TrySignalFireParry(impactPoint, originalContact)));

            // 반성공 소피드백 [SPELL-FIDELITY §4.6] — 보상 없이 축소 버스트만: 「반쪽으로 받아냈다」
            if (outcome == ParryOutcome.Half && !originalContact && _palette != null)
            {
                ParryBurstEffect.Spawn(impactPoint,
                    _palette.GetBaseColor(InitialOf(guardElement)), Camera.main, 0.5f);
                return;
            }
            if (outcome != ParryOutcome.Success) return;
            _ink?.Gain(_config.ParryInkRefund);  // 소모 초과 환급 = 순증(COMBAT-PARRY)
            PulseReticle306();                   // 살짝의 효과 — 결투 계약의 고리가 응답한다 (#306: C2 hook may delay it to the counter stroke)

            // 접점 버스트(임시 — 3차 검수): 투사체가 방어막에 부딪혀 꺼지는 자리에서
            // 방어막 속성색 조각이 터진다. 색=팔레트 단일 출처(색=의미)
            Color burst = _palette != null ? _palette.GetBaseColor(InitialOf(guardElement)) : Color.white;
            if (!originalContact && !authoredGuardContact) ParryBurstEffect.Spawn(impactPoint, burst, Camera.main, PlayerParryBurstScale306());
        }

        private bool SpawnContact(GameObject source, Vector3 point, float scale, Element? element=null, char letter=default)
        {
            var emphasis = _contactVfx.ForSpell(letter);
            if(emphasis != null) scale *= emphasis.KtpContactMultiplier;
            bool elementalGuard=emphasis!=null&&emphasis.KtpPatternShield&&emphasis.GuardContactPrefab!=null;
            if(elementalGuard)source=emphasis.GuardContactPrefab;
            bool areaContact=emphasis!=null&&emphasis.AreaContactPrefab!=null;
            if(areaContact)source=emphasis.AreaContactPrefab;
            var cam = Camera.main;
            if(!elementalGuard&&emphasis!=null&&cam!=null&&emphasis.KtpContactSurfaceOffset>0)
                point+=(cam.transform.position-point).normalized*emphasis.KtpContactSurfaceOffset;
            var facing = cam != null ? cam.transform.rotation : Quaternion.identity;
            var effect = SpellVFX120.KtpContactEffect.Spawn(source, point,
                areaContact?facing:elementalGuard?(emphasis.KtpRectShield?facing:Quaternion.identity):facing * Quaternion.Euler(_contactVfx.SourceEuler), scale, gameObject.scene);
            if (effect == null) return false;
            if(!elementalGuard&&!areaContact)
            {
                if(emphasis != null) effect.ApplyElementColor(emphasis.Pigment, emphasis.KtpContactBrightness>0?emphasis.KtpContactBrightness:emphasis.KtpBrightness);
                else if(element.HasValue&&_palette!=null)effect.ApplyElementColor(_palette.GetBaseColor(InitialOf(element.Value)));
            }
            _contacts.Add(effect);
            return true;
        }

        // Element → 초성(팔레트 키). 배속은 ElementRelations.FromInitial의 역방향(언어적 사실 — 밸런스 아님)
        private static char InitialOf(Element element)
        {
            switch (element)
            {
                case Element.Fire: return 'ㄴ';
                case Element.Earth: return 'ㅁ';
                case Element.Metal: return 'ㅅ';
                case Element.Water: return 'ㅇ';
                default: return 'ㄱ'; // Wood
            }
        }

        private void ResolveAttack(SpellCast cast)
        {
            // 먹 부족 = 불발 취급 — 투사체(문양)도 나가지 않는다(7차 검수): 표현은 증발
            if (_ink == null || !TrySpendSpell(_config.SpellInkCost))
            {
                _brushAdapter?.NotifyCastFailed();
                return;
            }

            CastAccepted?.Invoke(cast, PlayerPosition(), PlayerForward());
            if(EAGiyeok!=null&&EAGiyeokRuntime.Owns(cast.Letter))
            {
                var destination=AimedTarget();var origin=PlayerPosition()+Vector3.up*.4f;
                var point=destination!=null?destination.transform.position+Vector3.up*.4f:AimPoint(_freeAimRange);
                float flight=Mathf.Max(.08f,Vector3.Distance(origin,point)/Mathf.Max(1,_config.SpellProjectileSpeed*cast.SpeedMul));
                EAGiyeok.CastSpell(cast,destination,origin,point,flight,Time.time);
                _brushAdapter?.NotifyFieldPresentationOwned();return;
            }

            // 광역 실판정 [#137 §9-1 해제 · #141 기하 3종]: 형상이 있는 광역은 형상별 판정 — 계획을 만들어
            // 피해(PendingCast)·연출(어댑터→SetAreaPlan)·계측(CastPlanned)에 같은 시계로 준다
            switch (cast.AreaShape)
            {
                case AreaShape.Cone: ResolveConeAttack(cast); return;
                case AreaShape.Circle: ResolveCircleAttack(cast); return;
                case AreaShape.Path: ResolvePathAttack(cast); return;
                case AreaShape.Volley: ResolveVolleyAttack(cast); return;
            }

            var plan = new CastPlan { Cast = cast };
            EnemyVitals target = AimedTarget();
            if (target == null)
            {
                _brushAdapter?.SetPatternAttackTarget(null, 0f); // 허공 — 먹만 소모, 연출은 전방 허공 착탄
                CastPlanned?.Invoke(plan);
                return;
            }

            // 피해=착탄 동기화(5차 검수 — 즉발 절단면 폐기): 커밋 시 대상·비행시간 확정 = 유도 보장
            // (COMBAT-ATTACK 락온 유도). 문양 투사체 연출도 같은 비행시간을 쓴다 — 시각이 하나뿐이라 어긋나지 않는다.
            // 탄속 배율=규칙 계층(SpellBook — 사=최속·아=느림): 판정·연출이 같은 시계를 나눠 쓴다(SPELL-FIDELITY §4.3)
            float speed = Mathf.Max(1f, _config.SpellProjectileSpeed * cast.SpeedMul);
            float duration = Vector3.Distance(PlayerPosition(), target.transform.position) / speed;
            Schedule(plan, target, Time.time + duration, cast.Power);
            _brushAdapter?.SetPatternAttackTarget(target.transform, duration);
            CastPlanned?.Invoke(plan);
        }

        // ---- 광역 형상별 판정 [SPELL-AREA-SHAPES §2] — 기하는 AreaGeometry(하네스와 공유), 수치는 SpellBook(0=기본값) ----

        // 전방 cone 다중 히트 [#137 — 노 「화염 방사」]: 락온·조준과 무관하게 시전자 전방 부채꼴의
        // 모든 생존 적에게 착탄을 예약한다. 판정 시각=형성 딜레이(연출의 분사 개시와 동기).
        // 대상 0체=허공 방사(먹만 소모). 반각·사거리는 CombatConfig가 정본(FIDELITY §8-1 승계)
        private void ResolveConeAttack(SpellCast cast)
        {
            Vector3 origin = PlayerPosition();
            Vector3 forward = PlayerForward();
            float delay = cast.Area.ImpactDelay > 0f ? cast.Area.ImpactDelay : _config.AreaImpactDelay;
            var plan = new CastPlan
            {
                Cast = cast,
                Area = new AreaImpactPlan { Shape = AreaShape.Cone, Point = origin, Direction = forward, Length = _config.AreaConeRange, Delay = delay, Angle = _config.AreaConeAngle }, // Angle=연출 미러(판정은 아래 InCone이 _config를 직접 읽는다)
            };
            float impactTime = Time.time + delay;
            foreach (var enemy in _targets)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (!AreaGeometry.InCone(origin, forward, enemy.transform.position, _config.AreaConeAngle, _config.AreaConeRange, out _, out _)) continue;
                Schedule(plan, enemy, impactTime, cast.Power);
            }
            _brushAdapter?.SetPatternAttackTarget(null, 0f); // 연출 목표=전방(자체 시계) — 유도 없음
            _brushAdapter?.SetPatternAreaPlan(plan.Area);
            CastPlanned?.Invoke(plan);
        }

        // 원형 일제 [고 「지정 영역에서 가시 일제 솟음」]: 중심=조준 대상 위치(없으면 조준 전방 고정 거리),
        // 반경 안 생존 적 전수가 같은 시각(형성 딜레이)에 맞는다. 연출(ThornRise)의 중심=계획의 점
        private int _areaSpikeSeed;
        private int _scatterVolleySeed;
        private void ResolveCircleAttack(SpellCast cast)
        {
            float radius = cast.Area.Radius > 0f ? cast.Area.Radius : 2.5f;
            float delay = cast.Area.ImpactDelay > 0f ? cast.Area.ImpactDelay : _config.AreaImpactDelay;
            float aimRange = cast.Area.Length > 0f ? cast.Area.Length : _freeAimRange;
            EnemyVitals target = AimedTarget();
            Vector3 center = target != null ? target.transform.position : AimPoint(aimRange);
            var plan = new CastPlan
            {
                Cast = cast,
                Area = new AreaImpactPlan { Shape = AreaShape.Circle, Point = center, Radius = radius, Delay = delay },
            };
            if(cast.Letter=='고') AreaSpikePlanner.Fill(plan.Area, ++_areaSpikeSeed);
            float impactTime = Time.time + delay;
            foreach (var enemy in _targets)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (!AreaGeometry.InCircle(center, enemy.transform.position, radius, out _)) continue;
                float at=cast.Letter=='고'?Time.time+AreaSpikePlanner.NearestRise(plan.Area,enemy.transform.position):impactTime;
                Schedule(plan, enemy, at, cast.Power);
            }
            _brushAdapter?.SetPatternAttackTarget(target != null ? target.transform : null, 0f);
            _brushAdapter?.SetPatternAreaPlan(plan.Area);
            CastPlanned?.Invoke(plan);
        }

        // 경로 타격 [오 「전진하는 느린 파도」·모 「직선 경로 모래폭풍」]: 시전자 발치에서 대상(없으면 전방)으로
        // 복도가 뻗고, 전선이 닿는 시각(차오름 + 경로 위 거리/속도)에 적별로 맞는다. 연출(GroundWave)이 같은 계획으로 전진
        private int _earthRiftSeed;
        private void ResolvePathAttack(SpellCast cast)
        {
            float halfWidth = cast.Area.Radius > 0f ? cast.Area.Radius : 1.5f;
            float length = cast.Area.Length > 0f ? cast.Area.Length : 14f;
            float speed = cast.Area.Speed > 0f ? cast.Area.Speed : 6f;
            float delay = cast.Area.ImpactDelay > 0f ? cast.Area.ImpactDelay : _config.AreaImpactDelay;
            EnemyVitals target = AimedTarget();
            Vector3 start = PlayerPosition();
            Vector3 direction = target != null ? AreaGeometry.Flat(target.transform.position - start) : PlayerForward();
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : PlayerForward();
            var plan = new CastPlan
            {
                Cast = cast,
                Area = new AreaImpactPlan { Shape = AreaShape.Path, Point = start, Direction = direction, Radius = halfWidth, Length = length, Speed = speed, Delay = delay, VisualSeed = cast.Letter == '모' ? ++_earthRiftSeed : 0 },
            };
            foreach (var enemy in _targets)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (!AreaGeometry.InCorridor(start, direction, enemy.transform.position, halfWidth, length, out float along, out _)) continue;
                Schedule(plan, enemy, Time.time + delay + along / speed, cast.Power);
            }
            _brushAdapter?.SetPatternAttackTarget(target != null ? target.transform : null, 0f);
            _brushAdapter?.SetPatternAreaPlan(plan.Area);
            CastPlanned?.Invoke(plan);
        }

        // 다연발 [소 「다연발 송곳 속사」]: 전방 부채꼴 후보(각 오름차순)에 발수만큼 순환 배분 — 발당 위력=위력÷발수,
        // 발 i 착탄 = 기준 딜레이 + i×간격 + 거리/탄속. 연출(SpikeVolley)이 발마다 그 대상·시각으로 발사. 후보 0=허공 속사
        private void ResolveVolleyAttack(SpellCast cast)
        {
            float halfAngle = cast.Area.Angle > 0f ? cast.Area.Angle : 25f;
            float range = cast.Area.Length > 0f ? cast.Area.Length : 16f;
            float speed = cast.Area.Speed > 0f ? cast.Area.Speed : 32f;
            float delay = cast.Area.ImpactDelay > 0f ? cast.Area.ImpactDelay : _config.AreaImpactDelay;
            int shots = cast.Area.Shots > 0 ? cast.Area.Shots : 9;
            float interval = cast.Area.Interval > 0f ? cast.Area.Interval : 0.1f;

            Vector3 origin = PlayerPosition();
            Vector3 forward = PlayerForward();
            var candidates = new List<(EnemyVitals vitals, float angle)>();
            foreach (var enemy in _targets)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (!AreaGeometry.InCone(origin, forward, enemy.transform.position, halfAngle, range, out float angle, out _)) continue;
                candidates.Add((enemy, angle));
            }
            candidates.Sort((a, b) => a.angle.CompareTo(b.angle));

            var plan = new CastPlan
            {
                Cast = cast,
                Area = new AreaImpactPlan { Shape = AreaShape.Volley, Direction = forward, Length = range, Speed = speed, Delay = delay, Radius = halfAngle, CreatedAt=Time.time, ShotCount=shots, ShotInterval=interval },
            };
            if (cast.Area.ScatterVolley)
            {
                // Stratified fan directions are fixed once. Only rays crossing a target reserve damage.
                var random=new System.Random(++_scatterVolleySeed);int[] order=new int[shots];
                for(int i=0;i<shots;i++)order[i]=i;
                for(int i=shots-1;i>0;i--){int j=random.Next(i+1);int n=order[i];order[i]=order[j];order[j]=n;}
                for(int i=0;i<shots;i++)
                {
                    float angle=((order[i]+.2f+(float)random.NextDouble()*.6f)/shots*2-1)*halfAngle;
                    Vector3 ray=Quaternion.AngleAxis(angle,Vector3.up)*forward;
                    EnemyVitals hitTarget=null;float distance=range;
                    foreach(var candidate in candidates)
                    {
                        Vector3 relative=AreaGeometry.Flat(candidate.vitals.transform.position-origin);float along=Vector3.Dot(relative,ray);
                        if(along<=0||along>distance||(relative-ray*along).sqrMagnitude>.65f*.65f)continue;
                        hitTarget=candidate.vitals;distance=along;
                    }
                    float launch=Time.time+delay+i*interval,impact=launch+distance/speed;
                    var hit=hitTarget!=null?Schedule(plan,hitTarget,impact,cast.Power/shots):new PlannedHit{ImpactTime=impact};
                    hit.LaunchTime=launch;hit.HasImpactPoint=true;hit.ImpactPoint=origin+ray*distance+Vector3.up*(.65f+(float)random.NextDouble()*.8f);
                    plan.Area.Shots.Add(hit);
                }
                plan.Area.Point=origin+forward*range;
                _brushAdapter?.SetPatternAttackTarget(null,0f);_brushAdapter?.SetPatternAreaPlan(plan.Area);CastPlanned?.Invoke(plan);return;
            }
            if (candidates.Count == 0)
            {
                plan.Area.Point = AimPoint(range);
                _brushAdapter?.SetPatternAttackTarget(null, 0f);
                _brushAdapter?.SetPatternAreaPlan(plan.Area);
                CastPlanned?.Invoke(plan);
                return;
            }

            float perShot = cast.Power / shots;
            for (int i = 0; i < shots; i++)
            {
                var target = candidates[i % candidates.Count].vitals;
                float flight = Vector3.Distance(origin, target.transform.position) / speed;
                var hit = Schedule(plan, target, Time.time + delay + i * interval + flight, perShot);
                hit.LaunchTime=Time.time+delay+i*interval;
                plan.Area.Shots.Add(hit);
            }
            plan.Area.Point = candidates[0].vitals.transform.position;
            _brushAdapter?.SetPatternAttackTarget(candidates[0].vitals.transform, 0f);
            _brushAdapter?.SetPatternAreaPlan(plan.Area);
            CastPlanned?.Invoke(plan);
        }

        // 착탄 예약 = 피해 시계(PendingCast) + 계획 기록 — 같은 값 한 번만
        private PlannedHit Schedule(CastPlan plan, EnemyVitals target, float impactTime, float power)
        {
            float scale = PlayerDamageScale != null ? PlayerDamageScale(plan.Cast.Element) : 1f;
            if (float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
            power *= Mathf.Max(0f, scale);
            _pendingCasts.Add(new PendingCast { Target = target, ImpactTime = impactTime, Power = power, Element=plan.Cast.Element, Letter=plan.Cast.Letter, Origin=_playerTransform!=null?_playerTransform.position+Vector3.up*.4f:Vector3.zero,
                Attack=AttackProvenance.Create(_playerVitals != null ? (UnityEngine.Object)_playerVitals : this, DamageSource.PlayerDirect, plan.Cast.Element), TargetLifeRevision=target.LifeRevision });
            var hit = new PlannedHit { Target = target, ImpactTime = impactTime, Power = power };
            plan.Hits.Add(hit);
            return hit;
        }

        private EnemyVitals AimedTarget()
        {
            EnemyVitals target = _lockOn != null ? _lockOn.Target : null;
            return target != null && TargetVisible(target,PlayerPosition()+Vector3.up*.4f) ? target : FindFreeAimTarget();
        }

        private bool TargetVisible(EnemyVitals target,Vector3 origin)
        {
            if(!_environmentOcclusion)return true;
            if(target==null)return false;
            Vector3 delta=target.transform.position+Vector3.up*.4f-origin;
            foreach(var hit in Physics.RaycastAll(origin,delta.normalized,delta.magnitude,1,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(target.transform) && (_playerTransform==null||!hit.transform.IsChildOf(_playerTransform)))return false;
            return true;
        }

        private Vector3 PlayerPosition()
        {
            return _playerTransform != null ? _playerTransform.position : transform.position;
        }

        private Vector3 PlayerForward()
        {
            Vector3 forward = AreaGeometry.Flat(_playerTransform != null ? _playerTransform.forward : transform.forward);
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        // 무대상 조준점 — 조준(카메라) 전방 고정 거리, 높이는 플레이어(수평 판정)
        private Vector3 AimPoint(float range)
        {
            var cam = Camera.main;
            Transform origin = cam != null ? cam.transform : _playerTransform;
            if (origin == null) return PlayerPosition() + Vector3.forward * range;
            Vector3 point = origin.position + AreaGeometry.Flat(origin.forward).normalized * range;
            point.y = PlayerPosition().y;
            return point;
        }

        private EnemyVitals FindFreeAimTarget()
        {
            // 조준 기준 = 카메라(보는 곳이 곧 겨눈 곳) — 숄더뷰 실험의 어깨 오프셋 시차를 없애고,
            // 1인칭에서도 yaw 전용이던 원뿔이 피치를 따라간다 [실험 2026-08-27]
            var cam = Camera.main;
            Transform origin = cam != null ? cam.transform : _playerTransform;
            if (origin == null) return null;

            // 후보 = 씬 배선 전수(_targets) — 원뿔 안에서 시선에 가장 가까운 생존 적 1체.
            // 다수 배치 씬에서도 규칙은 그대로다(전방 원뿔 명중·대상 하나) — 후보 집합만 넓어진다
            EnemyVitals best = null;
            float bestAngle = _freeAimAngle;
            foreach (var candidate in _targets)
            {
                if (candidate == null || !candidate.IsAlive || !TargetVisible(candidate,origin.position)) continue;
                Vector3 to = candidate.transform.position - origin.position;
                if (to.magnitude > _freeAimRange) continue;
                float angle = Vector3.Angle(origin.forward, to);
                if (angle > bestAngle) continue;
                best = candidate;
                bestAngle = angle;
            }
            return best;
        }

        // 불발 — 먹만 소모(SPEC-DRAWING-INPUT §3-3의 첫 소비자)
        private void OnMisfired()
        {
            _ink?.SpendClamped(_config != null ? _config.MisfireInkCost : 0.15f);
        }

        // 피격 = 작도 중단(COMBAT-ATTACK 기본 규칙) — 글자만 소멸·모드 유지(SPEC-DRAWING-INPUT §3)
        private void OnPlayerDamaged(float damage)
        {
            _lastPlayerHitTime = Time.time;
            if (damage > 0) MarkPlayerHitBoss();
            _drawingInput?.InterruptLetter();
            var cam = Camera.main;
            if (damage > 0 && _contactVfx != null && cam != null)
            {
                var offset = _contactVfx.PlayerHitOffset;
                var point = cam.transform.TransformPoint(new Vector3(offset.x, offset.y,
                    Mathf.Max(cam.nearClipPlane + .2f, _contactVfx.PlayerHitDistance)));
                SpawnContact(_contactVfx.PlayerHit, point, _contactVfx.PlayerHitScale);
            }
        }

        public void ResetEncounterGroggy()
        {
            foreach (var target in _targets) if (target != null) target.ResetCombatState();
            RefreshHud();
        }

        private void OnInkChanged(float value)
        {
            if (_brushAdapter != null) _brushAdapter.InkNormalized = value; // 하네스 슬라이더 은퇴 — 실경제 연결
            _hud?.SetInk01(value);
        }

        private void RefreshHud()
        {
            if (_hud == null) return;
            if (_playerVitals != null) _hud.SetHp01(_playerVitals.Hp01);
            var target = GroggyHudTarget;
            _hud.SetGroggy01(DisplayGroggy306(target));
        }
    }
}
