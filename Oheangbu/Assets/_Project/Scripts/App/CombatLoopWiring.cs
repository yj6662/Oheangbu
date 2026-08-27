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

        [Inject]
        public void Construct(SpellResolver resolver, ParryJudge judge, GroggyMeter groggy, InkPool ink)
        {
            _resolver = resolver;
            _judge = judge;
            _groggy = groggy;
            _ink = ink;
        }

        private void Start()
        {
            _enemy?.Init(_judge);
            _harvest?.Init(_ink);

            // 텔레그래프 색 = 팔레트가 단일 출처(색=의미) — 화(ㄴ) 기본색 / 무속성=무채
            if (_enemy != null && _palette != null)
            {
                _enemy.SetTelegraphColors(_palette.GetBaseColor('ㄴ'), new Color(0.45f, 0.43f, 0.41f));
            }

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
        }

        private void OnDisable()
        {
            if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
            if (_misfired != null) _misfired.Unsubscribe(OnMisfired);
            if (_inkChanged != null) _inkChanged.Unsubscribe(OnInkChanged);
            if (_playerVitals != null) _playerVitals.Damaged -= OnPlayerDamaged;
            if (_enemyVitals != null) _enemyVitals.Died -= OnEnemyDied;
            UnhookServices();
        }

        private bool _hooked;

        private void Update()
        {
            // [Inject]는 씬 로드 직후 실행되므로 서비스 이벤트는 첫 프레임에 건다
            if (!_hooked && _groggy != null)
            {
                _groggy.Blossomed += OnBlossomed;
                _groggy.Changed += RefreshHud;
                if (_enemy != null) _enemy.StunEnded += OnStunEnded;
                _hooked = true;
            }
        }

        private void UnhookServices()
        {
            if (!_hooked) return;
            _groggy.Blossomed -= OnBlossomed;
            _groggy.Changed -= RefreshHud;
            if (_enemy != null) _enemy.StunEnded -= OnStunEnded;
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
                // 프로토 미러 밖의 글자 — 효과 없음, 먹만 소모(불발 취급). CSV 완주는 임포터 이후
                _ink?.SpendClamped(_config.MisfireInkCost);
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
            // 먹 부족 = 불발 취급(§10.1) — 판정 자체가 서지 않는다
            if (_ink == null || !_ink.TrySpend(_config.ParryInkCost)) return;

            ParryOutcome outcome = _judge != null ? _judge.Judge(cast.Element, Time.time) : ParryOutcome.None;
            if (outcome == ParryOutcome.Success)
            {
                _ink.Gain(_config.ParryInkRefund);  // 소모 초과 환급 = 순증(COMBAT-PARRY)
                _groggy?.AddFromParry();            // 그로기의 유일한 증가 경로(프로토)
            }
            // 반성공=경감·실패=무효과 — 피해 처리는 임팩트 시 적이 Resolution을 읽는다.
            // 허공 시전(None)은 잔존 없음(CSV) — 먹만 쓰고 사라진다
        }

        private void ResolveAttack(SpellCast cast)
        {
            if (_ink == null || !_ink.TrySpend(_config.SpellInkCost)) return;

            EnemyVitals target = _lockOn != null ? _lockOn.Target : null;
            if (target == null) target = FindFreeAimTarget();
            if (target == null) return; // 허공 — 먹만 소모(락온 유도가 정석: COMBAT-ATTACK 조준 혼합)

            // 프로토 절단면: 명중·피해 즉발(유도 보장). 투사체 연출은 후속 — 수치 검증이 먼저
            target.TakeDamage(cast.Power);
        }

        private EnemyVitals FindFreeAimTarget()
        {
            if (_enemyVitals == null || !_enemyVitals.IsAlive || _playerTransform == null) return null;
            Vector3 to = _enemyVitals.transform.position - _playerTransform.position;
            if (to.magnitude > _freeAimRange) return null;
            return Vector3.Angle(_playerTransform.forward, to) <= _freeAimAngle ? _enemyVitals : null;
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
            _enemy?.EnterStun(); // 만개 = 급소창(스턴 + 피해 증폭)
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
