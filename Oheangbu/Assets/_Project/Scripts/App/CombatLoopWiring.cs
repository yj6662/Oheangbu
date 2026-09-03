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
    public sealed class CombatLoopWiring : MonoBehaviour
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

        [Header("표현")]
        [SerializeField] private ElementPaletteSO _palette;
        [SerializeField] private HudController _hud;

        [Header("비락온 자유 조준의 프로토 근사(§10.1) — 전방 원뿔 명중")]
        [SerializeField, Range(1f, 45f)] private float _freeAimAngle = 15f;
        [SerializeField, Min(1f)] private float _freeAimRange = 20f;

        private SpellResolver _resolver;
        private ParryJudge _judge;
        private GroggyMeter _groggy;
        private InkPool _ink;

        // 날아가는 술식 [TEST 5차 검수] — 커밋 시 대상·착탄 시각 확정(유도 보장), 착탄에 피해.
        // 적 화염구와 같은 문법: 시각이 규칙이고, 비행은 연출이 따라온다
        private struct PendingCast
        {
            public EnemyVitals Target;
            public float ImpactTime;
            public float Power;
        }

        private readonly List<PendingCast> _pendingCasts = new List<PendingCast>();

        // 씬의 적 컨트롤러 전수 — _enemy + _enemies에서 파생(과녁처럼 컨트롤러 없는 적은 자연 제외, 중복 제거).
        // 판정기 주입·텔레그래프 색·만개 스턴 배선은 적이 몇이든 이 파일의 몫이다
        private readonly List<EnemyController> _controllers = new List<EnemyController>();

        // 조준 후보 전수 — _enemyVitals + _enemies(중복 제거). 락온(LockOn)과 자유 조준이 같은 집합을 본다
        private readonly List<EnemyVitals> _targets = new List<EnemyVitals>();

        // 판정 결과의 표현 측 재방송(읽기 전용) — 하네스(패링장 판정판)가 구독한다. 규칙 분기는 OnParryImpactResolved 그대로
        public event Action<ParryOutcome, Element, Vector3> ParryResolved;

        [Inject]
        public void Construct(SpellResolver resolver, ParryJudge judge, GroggyMeter groggy, InkPool ink)
        {
            _resolver = resolver;
            _judge = judge;
            _groggy = groggy;
            _ink = ink;
        }

        private void Awake()
        {
            CollectControllers();
        }

        private void CollectControllers()
        {
            _targets.Clear();
            if (_enemyVitals != null) _targets.Add(_enemyVitals);
            foreach (var vitals in _enemies)
            {
                if (vitals != null && !_targets.Contains(vitals)) _targets.Add(vitals);
            }

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
            if (_enemyVitals != null) _enemyVitals.Died += OnEnemyDied;
            TryHookServices(); // 재활성화 시 첫 프레임 구독 공백 방지(주입 완료 후엔 즉시 성공)
        }

        private void OnDisable()
        {
            if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
            if (_misfired != null) _misfired.Unsubscribe(OnMisfired);
            if (_inkChanged != null) _inkChanged.Unsubscribe(OnInkChanged);
            if (_playerVitals != null) _playerVitals.Damaged -= OnPlayerDamaged;
            if (_enemyVitals != null) _enemyVitals.Died -= OnEnemyDied;
            UnhookServices();
            _pendingCasts.Clear(); // 비활성 동안의 기한 지난 착탄이 재활성 시 유령 피해가 되지 않게
        }

        private bool _hooked;

        private void Update()
        {
            TryHookServices();
            TickPendingCasts();
        }

        // 착탄 시각 도래 = 피해 적용(피해=착탄 동기화 — 5차 검수). 대상이 먼저 죽었으면 허공이 된다
        private void TickPendingCasts()
        {
            for (int i = _pendingCasts.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pendingCasts[i].ImpactTime) continue;
                PendingCast pending = _pendingCasts[i];
                _pendingCasts.RemoveAt(i);
                if (pending.Target != null && pending.Target.IsAlive) pending.Target.TakeDamage(pending.Power);
            }
        }

        // [Inject]는 씬 로드 직후 실행되므로 서비스 이벤트는 OnEnable(재활성) 또는 첫 프레임에 건다
        private void TryHookServices()
        {
            if (_hooked || _groggy == null) return;
            _groggy.Blossomed += OnBlossomed;
            _groggy.Changed += RefreshHud;
            if (_judge != null) _judge.ImpactResolved += OnParryImpactResolved;
            foreach (var controller in _controllers) controller.StunEnded += OnStunEnded;
            _hooked = true;
        }

        private void UnhookServices()
        {
            if (!_hooked) return;
            _groggy.Blossomed -= OnBlossomed;
            _groggy.Changed -= RefreshHud;
            if (_judge != null) _judge.ImpactResolved -= OnParryImpactResolved;
            foreach (var controller in _controllers)
            {
                if (controller != null) controller.StunEnded -= OnStunEnded;
            }
            _hooked = false;
        }

        private void LateUpdate()
        {
            _hud?.UpdateReticle(_lockOn != null && _lockOn.Target != null ? _lockOn.Target.transform : null, Camera.main);
        }

        // ---- 작도 → 전투: 커밋된 글자 하나가 술식 한 발이 된다 ----
        private void OnLetterDrawn(DrawnLetter letter)
        {
            if (_resolver == null || _config == null) return;

            if (!_resolver.TryResolve(letter, out SpellCast cast))
            {
                // 프로토 미러 밖의 글자 — 효과 없음, 먹만 소모(불발 취급). CSV 완주는 임포터 이후.
                // 표현도 불발을 따른다(7차 검수) — 플래시·문양 대신 증발
                _ink?.SpendClamped(_config.MisfireInkCost);
                _brushAdapter?.NotifyCastFailed();
                return;
            }

            switch (cast.Kind)
            {
                case SpellKind.Parry:
                    ResolveParry(cast);
                    break;
                case SpellKind.AttackSingle:
                case SpellKind.AttackArea:
                    ResolveAttack(cast);
                    break;
            }
        }

        private void ResolveParry(SpellCast cast)
        {
            // 먹 부족 = 불발 취급(§10.1) — 방어막 자체가 서지 않고, 표현도 증발한다(7차 검수:
            // 없는 방어를 개화로 보여주지 않는다)
            if (_ink == null || !_ink.TrySpend(_config.ParryInkCost))
            {
                _brushAdapter?.NotifyCastFailed();
                return;
            }

            // [TEST §10.1 2차 플레이 검수] 작도 잔존 방어막: 완성이 방어막을 세우고, 판정은 임팩트가 한다.
            // 성공 보상(그로기·환급·이펙트)은 OnParryImpactResolved에서 — 판정점이 임팩트로 옮겨갔으므로.
            // ⚠ 어휘 CSV 「허공 시전 잔존 없음」의 전이 실험 — 채택 시 DECISIONS+CSV 정본 반영 필요
            _judge?.RaiseGuard(cast.Element, Time.time);
        }

        // 방어막에 임팩트가 닿은 순간(판정점) — 성공만 보상이 있다(그로기의 유일한 증가 경로 유지)
        private void OnParryImpactResolved(ParryOutcome outcome, Element guardElement, Vector3 impactPoint)
        {
            ParryResolved?.Invoke(outcome, guardElement, impactPoint);

            // 반성공 소피드백 [SPELL-FIDELITY §4.6] — 보상 없이 축소 버스트만: 「반쪽으로 받아냈다」
            if (outcome == ParryOutcome.Half && _palette != null)
            {
                ParryBurstEffect.Spawn(impactPoint,
                    _palette.GetBaseColor(InitialOf(guardElement)), Camera.main, 0.5f);
                return;
            }
            if (outcome != ParryOutcome.Success) return;
            _ink?.Gain(_config.ParryInkRefund);  // 소모 초과 환급 = 순증(COMBAT-PARRY)
            _groggy?.AddFromParry();
            _hud?.PulseReticle();                // 살짝의 효과 — 결투 계약의 고리가 응답한다

            // 접점 버스트(임시 — 3차 검수): 투사체가 방어막에 부딪혀 꺼지는 자리에서
            // 방어막 속성색 조각이 터진다. 색=팔레트 단일 출처(색=의미)
            Color burst = _palette != null ? _palette.GetBaseColor(InitialOf(guardElement)) : Color.white;
            ParryBurstEffect.Spawn(impactPoint, burst, Camera.main);
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
            if (_ink == null || !_ink.TrySpend(_config.SpellInkCost))
            {
                _brushAdapter?.NotifyCastFailed();
                return;
            }

            // 광역 실판정 [#137 — §9-1 부분 해제]: 형상이 있는 광역은 대상 조준 없이 영역 전수
            if (cast.AreaShape == AreaShape.Cone)
            {
                ResolveConeAttack(cast);
                return;
            }

            EnemyVitals target = _lockOn != null ? _lockOn.Target : null;
            if (target == null) target = FindFreeAimTarget();
            if (target == null)
            {
                _brushAdapter?.SetPatternAttackTarget(null, 0f); // 허공 — 먹만 소모, 연출은 전방 허공 착탄
                return;
            }

            // 피해=착탄 동기화(5차 검수 — 즉발 절단면 폐기): 커밋 시 대상·비행시간 확정 = 유도 보장
            // (COMBAT-ATTACK 락온 유도). 문양 투사체 연출도 같은 비행시간을 쓴다 — 시각이 하나뿐이라 어긋나지 않는다.
            // 탄속 배율=규칙 계층(SpellBook — 사=최속·아=느림): 판정·연출이 같은 시계를 나눠 쓴다(SPELL-FIDELITY §4.3)
            float speed = Mathf.Max(1f, _config.SpellProjectileSpeed * cast.SpeedMul);
            float duration = Vector3.Distance(_playerTransform != null ? _playerTransform.position : transform.position,
                target.transform.position) / speed;
            _pendingCasts.Add(new PendingCast
            {
                Target = target,
                ImpactTime = Time.time + duration,
                Power = cast.Power,
            });
            _brushAdapter?.SetPatternAttackTarget(target.transform, duration);
        }

        // 전방 cone 다중 히트 [#137 — 노 「화염 방사」]: 락온·조준과 무관하게 시전자 전방 부채꼴의
        // 모든 생존 적에게 착탄을 예약한다. 판정 시각=형성 딜레이(연출의 분사 개시와 동기 — §8 예외 1).
        // 대상 0체=허공 방사(먹만 소모). 연출은 자체 시계(SpellSequenceEffect)가 전방을 그린다
        private void ResolveConeAttack(SpellCast cast)
        {
            Transform origin = _playerTransform != null ? _playerTransform : transform;
            Vector3 forward = origin.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

            float impactTime = Time.time + _config.AreaImpactDelay;
            foreach (var enemy in _enemies)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                Vector3 to = enemy.transform.position - origin.position;
                Vector3 flat = new Vector3(to.x, 0f, to.z); // 수평 판정 — 높이 차는 묻지 않는다 [TEST]
                if (flat.magnitude > _config.AreaConeRange) continue;
                if (Vector3.Angle(forward, flat) > _config.AreaConeAngle) continue;
                _pendingCasts.Add(new PendingCast
                {
                    Target = enemy,
                    ImpactTime = impactTime,
                    Power = cast.Power,
                });
            }
            _brushAdapter?.SetPatternAttackTarget(null, 0f); // 연출 목표=전방(자체 시계) — 유도 없음
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
                if (candidate == null || !candidate.IsAlive) continue;
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
        private void OnPlayerDamaged(float _)
        {
            _drawingInput?.InterruptLetter();
            RefreshHud();
        }

        private void OnBlossomed()
        {
            // 만개 = 급소창(스턴 + 피해 증폭) — 결투에 참여 중(활성)인 적 전수. 잠든 적(하네스 비활성)은 결투 밖이다
            foreach (var controller in _controllers)
            {
                if (controller != null && controller.isActiveAndEnabled) controller.EnterStun();
            }
        }

        private void OnStunEnded()
        {
            _groggy?.Reset(); // 급소창 소진 후 재시작 [TEST — §10.1]
        }

        private void OnEnemyDied()
        {
            _groggy?.Reset(); // 판 단위 소멸(COMBAT-GROGGY 무감쇠의 반대급부)
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
            if (_groggy != null) _hud.SetGroggy01(_groggy.Value01);
        }
    }
}
