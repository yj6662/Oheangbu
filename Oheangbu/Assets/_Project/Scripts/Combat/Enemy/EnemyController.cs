using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 프로토 적 1종 — COMBAT-ENEMY 일반몹 규칙의 최소 구현:
    //   무속성 근접(회피가 답) + 단일 속성(화) 원거리(패링이 답). 혼합 콤보 없음(보스 전용).
    //   면역 없음 — 피해는 EnemyVitals가 항상 받는다(3원칙).
    // 텔레그래프 색 = 대응 프롬프트(COMBAT-DEFENSE): 속성색/무색. 색 값은 배선부가 팔레트에서 주입한다
    // (색=의미의 단일 출처 유지 — Combat은 표현 모듈을 모른다).
    // 상태머신 패턴(아키텍처 절대 규칙) — Idle → Telegraph → Impact/Flight → Recover, 별도로 Stunned.
    public sealed class EnemyController : MonoBehaviour
    {
        private enum State { Idle, Telegraph, Flight, Recover, Stunned, Dead }
        private enum Pattern { Melee, Ranged }

        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private EnemyVitals _vitals;
        [SerializeField] private Transform _player;
        [SerializeField] private PlayerVitals _playerVitals;
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Element _rangedElement = Element.Fire; // 결정 2 — 화

        private const float ParriedFlashDuration = 0.25f; // 패링 성공 플래시 — 연출 미세 시간(기술 상수)

        private ParryJudge _judge;
        private State _state = State.Idle;
        private Pattern _pattern;
        private float _stateUntil;
        private float _cooldown;
        private float _impactTime;
        private Transform _projectile;
        private Vector3 _projectileStart;
        private Color _baseColor;
        private Color _elementColor = new Color(0.72f, 0.36f, 0.22f);
        private Color _neutralColor = new Color(0.45f, 0.43f, 0.41f);
        private float _parriedFlashUntil;
        private Color _parriedFlashColor;
        private Color _stateTint; // 상태가 정한 기본 틴트 — 플래시 종료 시 복원 대상

        public event System.Action StunEnded; // 만개 소진 — 배선부가 그로기 리셋[TEST]에 쓴다
        public Element RangedElement => _rangedElement;

        private void Awake()
        {
            if (_renderer != null) _baseColor = _renderer.material.color;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "EnemyProjectile";
            Destroy(sphere.GetComponent<Collider>()); // 명중 판정은 임팩트 시각으로 — 물리 충돌 불사용
            sphere.transform.localScale = Vector3.one * 0.35f;
            sphere.SetActive(false);
            _projectile = sphere.transform;

            if (_vitals != null) _vitals.Died += OnDied;
            _cooldown = 1f;
        }

        private void OnDestroy()
        {
            if (_vitals != null) _vitals.Died -= OnDied;
            if (_projectile != null) Destroy(_projectile.gameObject);
        }

        public void Init(ParryJudge judge)
        {
            _judge = judge;
        }

        // 만개 = 급소창(COMBAT-GROGGY): 스턴(무행동) + 전 공격 피해 증폭. 진행 중 공격은 취소된다
        public void EnterStun()
        {
            if (_state == State.Dead || _config == null) return;
            CancelAttack();
            _state = State.Stunned;
            _stateUntil = Time.time + _config.BlossomStunDuration;
            if (_vitals != null) _vitals.DamageMultiplier = _config.BlossomDamageMultiplier;
            Tint(Color.black); // 그레이박스 임시 — 자세가 무너진 먹빛 [TEST]
        }

        public void SetTelegraphColors(Color elemental, Color neutral)
        {
            _elementColor = elemental;
            _neutralColor = neutral;
        }

        private void Update()
        {
            if (_state == State.Dead || _config == null || _player == null) return;

            if (_state != State.Stunned) FacePlayer();

            switch (_state)
            {
                case State.Idle:
                    _cooldown -= Time.deltaTime;
                    if (_cooldown <= 0f && Distance() <= _config.EnemyEngageRange) BeginTelegraph();
                    break;

                case State.Telegraph:
                    if (Time.time >= _stateUntil) OnTelegraphEnd();
                    break;

                case State.Flight:
                    TickProjectile();
                    break;

                case State.Recover:
                    if (Time.time >= _stateUntil) EnterIdle();
                    break;

                case State.Stunned:
                    if (Time.time >= _stateUntil)
                    {
                        if (_vitals != null) _vitals.DamageMultiplier = 1f;
                        StunEnded?.Invoke();
                        EnterIdle();
                    }
                    break;
            }

            // 패링 성공 플래시 — 「튕겨냈다」의 응답(2차 플레이 검수: 살짝의 효과).
            // 상태 틴트 위에 잠시 덮었다가, 끝나면 상태가 정한 색으로 복원한다(스턴 먹빛을 가리지 않게)
            if (Time.time < _parriedFlashUntil)
            {
                SetColor(_parriedFlashColor);
            }
            else if (_parriedFlashUntil > 0f)
            {
                SetColor(_stateTint);
                _parriedFlashUntil = 0f;
            }
        }

        private void BeginTelegraph()
        {
            _pattern = Distance() <= _config.EnemyMeleePreferRange ? Pattern.Melee : Pattern.Ranged;
            float duration = _pattern == Pattern.Melee ? _config.MeleeTelegraph : _config.RangedTelegraph;
            _state = State.Telegraph;
            _stateUntil = Time.time + duration;

            if (_pattern == Pattern.Ranged)
            {
                // scaled time — 감속 중엔 비행도 함께 느려진다(작도할 시간을 주는 감속의 목적)
                _impactTime = _stateUntil + _config.ProjectileFlight;
                Tint(_elementColor);
            }
            else
            {
                Tint(_neutralColor);
            }
        }

        private void OnTelegraphEnd()
        {
            if (_pattern == Pattern.Melee)
            {
                // 근접 임팩트 즉발 — 무적(회피) 판정은 PlayerVitals가 안다
                if (Distance() <= _config.MeleeRange) _playerVitals?.TakeDamage(_config.MeleeDamage);
                EnterRecover();
            }
            else
            {
                _projectileStart = transform.position + Vector3.up * 1.2f;
                _projectile.position = _projectileStart;
                _projectile.gameObject.SetActive(true);
                _state = State.Flight;
            }
        }

        private void TickProjectile()
        {
            float remain = _impactTime - Time.time;
            float t = Mathf.Clamp01(1f - remain / Mathf.Max(_config.ProjectileFlight, 0.01f));
            _projectile.position = Vector3.Lerp(_projectileStart, _player.position, t);

            if (remain <= 0f)
            {
                // 접점 = 눈높이에서 접근 방향으로 1m 앞 — 투사체 lerp 목표(플레이어 중심)는 몸 안이라,
                // 방어막의 개념 위치(작도면 앵커와 같은 전방 1m)로 보정한다. 「글자가 선 곳에서 튕긴다」
                Vector3 eye = _player.position + Vector3.up * 1.1f;
                Vector3 approach = (_player.position - _projectileStart).normalized;
                Vector3 contact = eye - approach * 1.0f;

                // 판정점 = 임팩트 시각 — 방어막(작도 잔존) 상태 조회(2차 플레이 검수 확정).
                // 무속성 근접은 애초에 조회하지 않는다(이원법 — 회피만이 답)
                ParryOutcome outcome = _judge != null
                    ? _judge.ResolveImpact(_rangedElement, Time.time, contact)
                    : ParryOutcome.None;

                float damage = _config.RangedDamage;
                switch (outcome)
                {
                    case ParryOutcome.Success:
                        damage = 0f; // 받아쳐진 화염구 — 방어막에 닿아 꺼진다
                        FlashParried();
                        break;
                    case ParryOutcome.Half:
                        damage *= _config.HalfParryDamageFactor;
                        break;
                    case ParryOutcome.Block:
                        damage *= _config.GuardBlockFactor; // 일반 방어 구간 — 경감만
                        break;
                }
                if (damage > 0f) _playerVitals?.TakeDamage(damage);

                // ResolveImpact는 동기로 만개 연쇄(성공→그로기→Blossomed→EnterStun)를 부를 수 있다 —
                // 상태가 이미 Stunned로 바뀌었으면 Recover로 덮지 않는다(프로젝타일은 CancelAttack이 정리)
                if (_state != State.Flight) return;
                FinishProjectile();
            }
        }

        private void FlashParried()
        {
            _parriedFlashUntil = Time.time + ParriedFlashDuration;
            _parriedFlashColor = Color.Lerp(_elementColor, Color.white, 0.35f);
        }

        private void FinishProjectile()
        {
            _projectile.gameObject.SetActive(false);
            EnterRecover();
        }

        private void CancelAttack()
        {
            _projectile.gameObject.SetActive(false);
        }

        private void EnterRecover()
        {
            Tint(_baseColor);
            _state = State.Recover;
            _stateUntil = Time.time + 0.4f;
        }

        private void EnterIdle()
        {
            Tint(_baseColor);
            _state = State.Idle;
            var range = _config.AttackCooldownRange;
            _cooldown = Random.Range(range.x, range.y);
        }

        private void OnDied()
        {
            CancelAttack();
            _state = State.Dead;
            Tint(new Color(0.2f, 0.19f, 0.18f));
        }

        private float Distance()
        {
            return Vector3.Distance(transform.position, _player.position);
        }

        private void FacePlayer()
        {
            Vector3 to = _player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        }

        private void Tint(Color color)
        {
            _stateTint = color;
            SetColor(color);
        }

        private void SetColor(Color color)
        {
            if (_renderer != null) _renderer.material.color = color;
        }
    }
}
