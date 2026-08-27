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

        private ParryJudge _judge;
        private State _state = State.Idle;
        private Pattern _pattern;
        private float _stateUntil;
        private float _cooldown;
        private IncomingAttack _currentAttack;
        private Transform _projectile;
        private Vector3 _projectileStart;
        private Color _baseColor;
        private Color _elementColor = new Color(0.72f, 0.36f, 0.22f);
        private Color _neutralColor = new Color(0.45f, 0.43f, 0.41f);

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
        }

        private void BeginTelegraph()
        {
            _pattern = Distance() <= _config.EnemyMeleePreferRange ? Pattern.Melee : Pattern.Ranged;
            float duration = _pattern == Pattern.Melee ? _config.MeleeTelegraph : _config.RangedTelegraph;
            _state = State.Telegraph;
            _stateUntil = Time.time + duration;

            if (_pattern == Pattern.Ranged)
            {
                // 속성 공격만 패링 판정에 등록 — 무속성은 회피만이 답(COMBAT-DEFENSE 이원법)
                _currentAttack = new IncomingAttack
                {
                    Element = _rangedElement,
                    ImpactTime = _stateUntil + _config.ProjectileFlight,
                };
                _judge?.Register(_currentAttack);
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
            var attack = _currentAttack;
            if (attack == null) { EnterRecover(); return; }

            // 정답 패링 = 피해 무효 — 받아쳐진 화염구는 그 자리에서 꺼진다
            if (attack.Resolution == ParryOutcome.Success)
            {
                FinishProjectile();
                return;
            }

            float remain = attack.ImpactTime - Time.time;
            float t = Mathf.Clamp01(1f - remain / Mathf.Max(_config.ProjectileFlight, 0.01f));
            _projectile.position = Vector3.Lerp(_projectileStart, _player.position, t);

            if (remain <= 0f)
            {
                float damage = _config.RangedDamage;
                if (attack.Resolution == ParryOutcome.Half) damage *= _config.HalfParryDamageFactor; // 반성공=경감
                _playerVitals?.TakeDamage(damage);
                FinishProjectile();
            }
        }

        private void FinishProjectile()
        {
            _projectile.gameObject.SetActive(false);
            _judge?.Clear();
            _currentAttack = null;
            EnterRecover();
        }

        private void CancelAttack()
        {
            _projectile.gameObject.SetActive(false);
            _judge?.Clear();
            _currentAttack = null;
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
            if (_renderer != null) _renderer.material.color = color;
        }
    }
}
