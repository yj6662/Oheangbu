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
    public sealed partial class EnemyController : MonoBehaviour
    {
        public enum AttackMode { LegacyDistance, MeleeOnly, RangedOnly }
        [SerializeField] private AttackMode _attackMode;
        [SerializeField] private bool _environmentOcclusion;
        public bool AttackEnabled {get;set;} = true;
        public bool AttackInProgress => _state != State.Idle && _state != State.Dead;
        public bool IsStunned => _state == State.Stunned;
        // Read-only presentation clock: visual motion never advances combat or moves its root.
        public bool IsTelegraphing => _state == State.Telegraph;
        public bool IsRecovering => _state == State.Recover;
        public bool IsProjectileFlying => _state == State.Flight;
        public float TelegraphProgress => IsTelegraphing ? Mathf.Clamp01(1f - (_stateUntil - Time.time) / Mathf.Max(.01f, TelegraphDuration)) : 0f;
        public float RecoveryProgress => IsRecovering ? Mathf.Clamp01(1f - (_stateUntil - Time.time) / Mathf.Max(.01f, RecoveryDuration)) : 0f;

        public void ResetEncounter() { CancelAttack(); _vitals?.Restore(); _parriedFlashUntil=0; EnterIdle(); }
        public void StopAttack() { if(_state!=State.Dead && _state!=State.Stunned){CancelAttack();EnterIdle();} }
        public bool HasLineOfSight()
        {
            if(!_environmentOcclusion && !ProfileActive)return true;
            if(_player==null)return false;
            Vector3 a=transform.position+Vector3.up*.4f,b=_player.position+Vector3.up*.4f;
            return !Obstructed(a,b);
        }
        bool Obstructed(Vector3 a,Vector3 b)
        {
            int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,a,b-a,(b-a).magnitude,~0,ref _occlusionHits);
            for(int i=0;i<count;i++) if(!_occlusionHits[i].transform.IsChildOf(transform) && !(_player!=null && _occlusionHits[i].transform.IsChildOf(_player)))return true;
            return false;
        }
        private RaycastHit[] _occlusionHits;
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
        private AttackProvenance _attack;
        private EnemyVitals _subscribedVitals;
        private State _state = State.Idle;
        private Pattern _pattern;
        private float _stateUntil;
        private float _cooldown;
        private float _impactTime;
        private Transform _projectile;
        private Vector3 _projectileStart;
        private Color _baseColor;
        private string _tintProperty;
        private Color _elementColor = new Color(0.72f, 0.36f, 0.22f);
        private Color _neutralColor = new Color(0.45f, 0.43f, 0.41f);
        private float _parriedFlashUntil;
        private Color _parriedFlashColor;
        private Color _stateTint; // 상태가 정한 기본 틴트 — 플래시 종료 시 복원 대상

        public event System.Action StunEnded; // 만개 소진 — 배선부가 그로기 리셋[TEST]에 쓴다
        public Element RangedElement => ProfileActive ? _attackProfile.Element : _rangedElement;

        private void Awake()
        {
            ValidateAuthoredProfile();
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_renderer != null){var material=_renderer.material;_tintProperty=material.HasProperty("_BaseColor")?"_BaseColor":material.HasProperty("_Color")?"_Color":null;if(_tintProperty!=null)_baseColor=material.GetColor(_tintProperty);}

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "EnemyProjectile";
            Destroy(sphere.GetComponent<Collider>()); // 명중 판정은 임팩트 시각으로 — 물리 충돌 불사용
            sphere.transform.localScale = Vector3.one * 0.35f;
            sphere.SetActive(false);
            _projectile = sphere.transform;

            BindVitals();
            _cooldown = 1f;
        }

        private void BindVitals()
        {
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_subscribedVitals == _vitals) return;
            UnbindVitals();
            _subscribedVitals = _vitals;
            if (_subscribedVitals != null)
            {
                _subscribedVitals.Died += OnDied;
                _subscribedVitals.WeakPointOpened += EnterStun;
                _subscribedVitals.WeakPointClosed += OnWeakPointClosed;
            }
        }

        private void UnbindVitals()
        {
            if (_subscribedVitals != null)
            {
                _subscribedVitals.Died -= OnDied;
                _subscribedVitals.WeakPointOpened -= EnterStun;
                _subscribedVitals.WeakPointClosed -= OnWeakPointClosed;
            }
            _subscribedVitals = null;
        }

        private void OnDestroy()
        {
            UnbindVitals();
            if (_projectile != null) Destroy(_projectile.gameObject);
        }

        // 잠듦(enabled=false)·씬 해제 — 진행 중 공격은 버린다: 비활성 중 지나간 임팩트 시각이 재활성 프레임의
        // 유령 피해가 되지 않게. 죽은 적은 그대로 둔다. 스턴 중이었으면 급소창을 정상 종료로 닫는다
        private void OnDisable()
        {
            EndAuthoredCue(true);
            if (_state == State.Dead) return;
            if (_projectile != null) _projectile.gameObject.SetActive(false);
            if (_state == State.Stunned)
            {
                _vitals?.ResetCombatState();
            }
            _state = State.Idle;
            _cooldown = 1f;
            _parriedFlashUntil = 0f;
            if (gameObject.activeInHierarchy) Tint(_baseColor);
        }

        public void Init(ParryJudge judge)
        {
            _judge = judge;
            BindVitals();
        }

        // 만개 = 급소창(COMBAT-GROGGY): 스턴(무행동) + 전 공격 피해 증폭. 진행 중 공격은 취소된다
        public void EnterStun()
        {
            if (_state == State.Dead || _state == State.Stunned || _config == null || _vitals == null || !_vitals.IsAlive) return;
            CancelAttack();
            _state = State.Stunned;
            _stateUntil = Time.time + _config.BlossomStunDuration;
            if (!_vitals.WeakPointActive) _vitals.OpenWeakPoint();
            Tint(Color.black); // 그레이박스 임시 — 자세가 무너진 먹빛 [TEST]
        }

        private void OnWeakPointClosed()
        {
            if (_state != State.Stunned) return;
            StunEnded?.Invoke();
            if (_vitals != null && _vitals.IsAlive) EnterIdle();
        }

        public void SetTelegraphColors(Color elemental, Color neutral)
        {
            _elementColor = elemental;
            _neutralColor = neutral;
        }

        private bool _controlBlocked;
        private void Update()
        {
            bool bound=_vitals!=null&&_vitals.Control.BlocksActions(Time.time);
            if(bound){if(!_controlBlocked)StopAttack();_controlBlocked=true;return;}
            _controlBlocked=false;
            if (_state == State.Dead || _config == null || _player == null) return;

            if (_state != State.Stunned && (!ProfileActive || (_state != State.Telegraph && _state != State.Flight))) FacePlayer();

            if (ProfileActive) TickAuthoredAttack();
            else switch (_state)
            {
                case State.Idle:
                    _cooldown -= Time.deltaTime;
                    float range = _attackMode==AttackMode.MeleeOnly ? _config.MeleeRange : _config.EnemyEngageRange;
                    if (AttackEnabled && _cooldown <= 0f && Distance() <= range && HasLineOfSight()) BeginTelegraph();
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
                    _vitals?.ExpireWeakPoint(Time.time);
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
            _pattern = _attackMode==AttackMode.MeleeOnly ? Pattern.Melee : _attackMode==AttackMode.RangedOnly ? Pattern.Ranged : Distance() <= _config.EnemyMeleePreferRange ? Pattern.Melee : Pattern.Ranged;
            _attack = AttackProvenance.Create(_vitals, DamageSource.Enemy, _pattern == Pattern.Ranged ? _rangedElement : (Element?)null);
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
                if (AttackEnabled && Distance() <= _config.MeleeRange && HasLineOfSight()) _playerVitals?.TakeAttackDamage(_config.MeleeDamage, IncomingDamageKind.Melee);
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
            if(_environmentOcclusion && (!AttackEnabled || !HasLineOfSight())) {FinishProjectile();return;}
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
                    ? _judge.ResolveImpact(_rangedElement, Time.time, contact, _attack)
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
                if (damage > 0f) _playerVitals?.TakeAttackDamage(damage, IncomingDamageKind.ElementalRanged);

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
            if(_projectile!=null)_projectile.gameObject.SetActive(false);
            EndAuthoredCue(true);
        }

        private void EnterRecover()
        {
            Tint(_baseColor);
            _state = State.Recover;
            _stateUntil = Time.time + (ProfileActive ? _attackProfile.Recovery : 0.4f);
        }

        private void EnterIdle()
        {
            Tint(_baseColor);
            _state = State.Idle;
            var range = ProfileActive ? _attackProfile.CooldownRange : _config != null ? _config.AttackCooldownRange : new Vector2(1.2f, 2.2f);
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
            if (_renderer != null && _tintProperty != null) _renderer.material.SetColor(_tintProperty,color);
        }
    }
}
