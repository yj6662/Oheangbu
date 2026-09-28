using System;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyVitals))]
    public sealed class CheongryongCombatController : MonoBehaviour
    {
        [SerializeField] private CheongryongCombatProfile _profile;
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private EnemyVitals _vitals;
        [SerializeField] private Transform _head, _tail;
        private Transform _player;
        private PlayerVitals _playerVitals, _subscribedPlayer;
        private ParryJudge _judge;
        private EnemyVitals _subscribedVitals;
        private CheongryongGrowthController _growth, _subscribedGrowth;
        private RaycastHit[] _occlusionHits, _groundHits;
        private bool _attackEnabled = true, _homeInitialized, _profileValid;
        private uint _lifeRevision;
        private Vector3 _home;
        private float _readyAt;
        private int _nextPattern;
        // Isolated checks substitute the same scaled clock for Update and event callbacks.
        private Func<float> _clock;
        private float Now => _clock != null ? _clock() : Time.time;

        public CheongryongCombatProfile Profile => _profile;
        public EnemyVitals Vitals => _vitals;
        public CheongryongAttackPlan CurrentPlan { get; private set; }
        public CheongryongCombatState State { get; private set; } = CheongryongCombatState.Idle;
        public bool AttackInProgress => CurrentPlan != null && !CurrentPlan.IsCancelled;
        public bool PhaseTwoCadence => _growth != null && _growth.IsPhaseTwo;
        public bool CanNavigate => isActiveAndEnabled && _attackEnabled && _vitals != null && _vitals.IsAlive &&
            !_vitals.WeakPointActive && !GrowthBlocks && (State == CheongryongCombatState.Idle || State == CheongryongCombatState.Recovery);
        public bool AttackEnabled
        {
            get => _attackEnabled;
            set { if (_attackEnabled == value) return; _attackEnabled = value; if (!value) StopAttack(); }
        }
        public event Action<CheongryongAttackPlan> AttackStarted;
        public event Action<CheongryongAttackImpact> AttackImpactResolved;
        public event Action<CheongryongAttackPlan, bool> AttackEnded;
        public event Action<CheongryongCombatState> StateChanged;

        private bool GrowthBlocks => _growth != null && _growth.isActiveAndEnabled && _growth.IsWindingUp;
        private void Awake() { Bind(); ValidateProfile(); ResetEncounter(); }
        private void OnEnable() { Bind(); ValidateProfile(); ResetEncounter(); }
        private void OnDisable() { CancelPlan(); Unbind(); SetState(_vitals != null && !_vitals.IsAlive ? CheongryongCombatState.Dead : CheongryongCombatState.Idle); }
        private void OnDestroy() { Unbind(); }

        public void Configure(Transform player, PlayerVitals playerVitals, ParryJudge judge)
        {
            StopAttack();
            _player = player; _playerVitals = playerVitals; _judge = judge;
            Bind(); SynchronizeLife();
        }

        public void ConfigureProfile(CheongryongCombatProfile profile, CombatConfigSO config = null)
        {
            if (profile != null && !profile.TryValidate(out var error)) throw new ArgumentException(error, nameof(profile));
            // Explicit authoring/diagnostic configuration may precede Unity's Awake callback.
            // Bind the actual life owner before ResetEncounter captures its revision.
            Bind();
            StopAttack(); _profile = profile; _config = config; ValidateProfile(); ResetEncounter();
        }

        // Presentation sockets may move freely; their positions are sampled only when windup starts.
        public void ConfigureSockets(Transform head, Transform tail) { _head = head; _tail = tail; }

        // Does not Restore vitals: encounter ownership remains with the existing encounter reset path.
        public void ResetEncounter()
        {
            CancelPlan();
            // Culling can re-enable a displaced boss; it must not move the authored encounter boundary.
            if (!_homeInitialized) { _home = transform.position; _homeInitialized = true; }
            _nextPattern = 0;
            if (_vitals != null) _lifeRevision = _vitals.LifeRevision;
            _readyAt = Now + (_profile != null ? _profile.InitialDelay : 1f);
            SetState(_vitals != null && !_vitals.IsAlive ? CheongryongCombatState.Dead : CheongryongCombatState.Idle);
        }

        public void StopAttack()
        {
            CancelPlan(); _readyAt = Now + (_profile != null ? _profile.CancelRecoverySeconds : .65f);
            if (State != CheongryongCombatState.Dead && State != CheongryongCombatState.Stunned && State != CheongryongCombatState.Growth)
                SetState(CheongryongCombatState.Recovery);
        }

        private void ValidateProfile() { _profileValid = _profile != null && _profile.TryValidate(out _); }
        private void SynchronizeLife()
        { if (_vitals != null && _lifeRevision != _vitals.LifeRevision) ResetEncounter(); }
        private void Bind()
        {
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_growth == null) _growth = GetComponent<CheongryongGrowthController>();
            if (_subscribedVitals != _vitals)
            {
                if (_subscribedVitals != null) { _subscribedVitals.Died -= OnDied; _subscribedVitals.WeakPointOpened -= OnWeakPoint; }
                _subscribedVitals = _vitals;
                if (_subscribedVitals != null) { _subscribedVitals.Died += OnDied; _subscribedVitals.WeakPointOpened += OnWeakPoint; }
            }
            if (_subscribedPlayer != _playerVitals)
            {
                if (_subscribedPlayer != null) _subscribedPlayer.Died -= OnPlayerDied;
                _subscribedPlayer = _playerVitals;
                if (_subscribedPlayer != null) _subscribedPlayer.Died += OnPlayerDied;
            }
            if (_subscribedGrowth != _growth)
            {
                if (_subscribedGrowth != null) _subscribedGrowth.StateChanged -= OnGrowthChanged;
                _subscribedGrowth = _growth;
                if (_subscribedGrowth != null) _subscribedGrowth.StateChanged += OnGrowthChanged;
            }
        }

        private void Unbind()
        {
            if (_subscribedVitals != null) { _subscribedVitals.Died -= OnDied; _subscribedVitals.WeakPointOpened -= OnWeakPoint; }
            if (_subscribedPlayer != null) _subscribedPlayer.Died -= OnPlayerDied;
            if (_subscribedGrowth != null) _subscribedGrowth.StateChanged -= OnGrowthChanged;
            _subscribedVitals = null; _subscribedPlayer = null; _subscribedGrowth = null;
        }

        private void OnDied() { CancelPlan(); SetState(CheongryongCombatState.Dead); }
        private void OnPlayerDied() { StopAttack(); }
        private void OnWeakPoint() { CancelPlan(); SetState(CheongryongCombatState.Stunned); }
        private void OnGrowthChanged(CheongryongGrowthState growthState)
        {
            if (growthState == CheongryongGrowthState.Windup)
            { CancelPlan(); SetState(CheongryongCombatState.Growth); }
            else if (State == CheongryongCombatState.Growth)
            {
                _readyAt = Now + (_profile != null ? _profile.CancelRecoverySeconds : .65f);
                SetState(_vitals != null && !_vitals.IsAlive ? CheongryongCombatState.Dead : CheongryongCombatState.Recovery);
            }
        }

        private bool Eligible()
        {
            return isActiveAndEnabled && _profileValid && _profile != null && _attackEnabled &&
                _vitals != null && _vitals.isActiveAndEnabled && _vitals.IsAlive && !_vitals.WeakPointActive &&
                _player != null && _player.gameObject.activeInHierarchy && _playerVitals != null &&
                _playerVitals.isActiveAndEnabled && _playerVitals.Hp01 > 0f && !GrowthBlocks &&
                (_player.position - _home).sqrMagnitude <= _profile.LeashRange * _profile.LeashRange &&
                (transform.position - _home).sqrMagnitude <= _profile.LeashRange * _profile.LeashRange &&
                (_player.position - transform.position).sqrMagnitude <= _profile.LeashRange * _profile.LeashRange;
        }

        private void Update()
        {
            if (!isActiveAndEnabled) return;
            SynchronizeLife();
            if (_vitals == null || !_vitals.IsAlive) { OnDied(); return; }
            if (GrowthBlocks) { CancelPlan(); SetState(CheongryongCombatState.Growth); return; }
            if (_vitals.WeakPointActive) { OnWeakPoint(); return; }
            if (State == CheongryongCombatState.Stunned || State == CheongryongCombatState.Growth)
            { _readyAt = Now + (_profile != null ? _profile.CancelRecoverySeconds : .65f); SetState(CheongryongCombatState.Recovery); }
            if (!Eligible()) { if (CurrentPlan != null) StopAttack(); return; }
            float now = Now;
            if (CurrentPlan != null) { TickAttack(now); return; }
            if (now < _readyAt) return;
            SetState(CheongryongCombatState.Idle);
            // A bounded rotating selection preserves all four attacks when their geometry is reachable.
            // At long range the two close attacks are skipped, rather than hitting from the arena center.
            for (int i = 0; i < 4; i++)
            {
                var kind = (CheongryongAttackKind)_nextPattern;
                _nextPattern = (_nextPattern + 1) % 4;
                if (TryBeginAttack(kind)) return;
            }
            _readyAt = now + _profile.CancelRecoverySeconds;
        }

        public bool TryBeginAttack(CheongryongAttackKind kind)
        {
            // Restore can arrive between Update and an explicit scheduling request.
            // Never accept a plan against the previous life and discard it on the next frame.
            SynchronizeLife();
            if (!_homeInitialized) ResetEncounter();
            if (!Eligible() || CurrentPlan != null || Now < _readyAt || kind < CheongryongAttackKind.HeadBite || kind > CheongryongAttackKind.WoodProjectile) return false;
            if ((_player.position - transform.position).sqrMagnitude > _profile.EngageRange * _profile.EngageRange) return false;
            Vector3 origin = kind == CheongryongAttackKind.TailSweep
                ? (_tail != null ? _tail.position : transform.TransformPoint(_profile.TailOriginOffset))
                : (_head != null ? _head.position : transform.TransformPoint(_profile.HeadOriginOffset));
            Vector3 target = _player.position;
            if (kind == CheongryongAttackKind.RootEruption && !TryGround(target, out target)) return false;
            Vector3 direction = kind == CheongryongAttackKind.WoodProjectile
                ? (_player.position + Vector3.up * .8f - origin)
                : Vector3.ProjectOnPlane(_player.position - origin, Vector3.up);
            if (direction.sqrMagnitude < .0001f) direction = transform.forward;
            if (Obstructed(origin, _player.position + Vector3.up * .8f)) return false;
            var attack = AttackProvenance.Create(_vitals, DamageSource.Enemy,
                kind >= CheongryongAttackKind.RootEruption ? Element.Wood : (Element?)null);
            var plan = new CheongryongAttackPlan(_profile, kind, attack, origin, direction, target, Now);
            if ((kind == CheongryongAttackKind.HeadBite || kind == CheongryongAttackKind.TailSweep) && !plan.Contains(_player.position)) return false;
            CurrentPlan = plan; SetState(CheongryongCombatState.Windup); AttackStarted?.Invoke(plan);
            return CurrentPlan == plan;
        }

        private void TickAttack(float now)
        {
            var plan = CurrentPlan;
            if (plan == null || !Eligible()) { StopAttack(); return; }
            plan.AdvanceTo(now);
            // Do not reconstruct damage from an attack whose entire active/recovery interval was skipped.
            if (now > plan.EndAt) { FinishAttack(plan, now); return; }
            if (now < plan.ReleaseAt) return;
            SetState(CheongryongCombatState.Active);
            Vector3 point = _player.position;
            bool contact = false;
            if (plan.Kind == CheongryongAttackKind.WoodProjectile)
            {
                ClipProjectile(plan);
                point += Vector3.up * .8f;
                contact = plan.SweptProjectileContains(point);
            }
            else if (plan.Kind == CheongryongAttackKind.TailSweep)
                contact = SweptTailContains(plan, point);
            else if (!plan.ContactConsumed)
            {
                // One release pulse. A late player entering a vanished eruption never receives a stored hit.
                contact = plan.IsActive && plan.Contains(point);
                if (!contact) ResolveContact(plan, point, false, now);
            }
            if (contact && !plan.ContactConsumed)
            {
                Vector3 rayPoint = plan.Kind == CheongryongAttackKind.RootEruption ? plan.TargetPoint + Vector3.up * .4f : point;
                if (plan.Kind != CheongryongAttackKind.WoodProjectile) rayPoint += Vector3.up * .4f;
                bool clear = !Obstructed(plan.Origin, rayPoint) &&
                    (plan.Kind != CheongryongAttackKind.RootEruption || !Obstructed(plan.TargetPoint + Vector3.up * .4f, point + Vector3.up * .4f));
                ResolveContact(plan, point, clear, now);
            }
            // ResolveImpact can synchronously open a weak point or kill/disable an actor.
            if (CurrentPlan != plan) return;
            bool projectileBlocked = plan.Kind == CheongryongAttackKind.WoodProjectile && plan.ProjectileDistance >= plan.ClearDistance;
            if (now >= plan.ActiveEndAt || projectileBlocked || (plan.Kind == CheongryongAttackKind.WoodProjectile && plan.ContactConsumed))
                FinishAttack(plan, now);
        }

        private static bool SweptTailContains(CheongryongAttackPlan plan, Vector3 point)
        {
            if (plan.ContactConsumed || !plan.Contains(point) || plan.PreviousSampleTime >= plan.ActiveEndAt) return false;
            Vector3 offset = Vector3.ProjectOnPlane(point - plan.Origin, Vector3.up);
            float angle = Vector3.SignedAngle(plan.Direction, offset, Vector3.up);
            float previousProgress = Mathf.Clamp01((plan.PreviousSampleTime - plan.ReleaseAt) / (plan.ActiveEndAt - plan.ReleaseAt));
            float from = Mathf.Lerp(-plan.HalfAngleDegrees, plan.HalfAngleDegrees, previousProgress);
            float to = Mathf.Lerp(-plan.HalfAngleDegrees, plan.HalfAngleDegrees, plan.Active01);
            return angle >= from - 8f && angle <= to + 8f;
        }

        private void ResolveContact(CheongryongAttackPlan plan, Vector3 point, bool contact, float now)
        {
            if (plan.ContactConsumed || CurrentPlan != plan || !Eligible()) return;
            plan.ConsumeContact(); // Before callbacks: re-entrant reward/stun/death handlers cannot repeat this attack.
            if (!contact) { AttackImpactResolved?.Invoke(new CheongryongAttackImpact(plan, point, ParryOutcome.None, 0f, false)); return; }
            Vector3 parryPoint = _player.position + Vector3.up * 1.1f - (point - plan.Origin).normalized;
            ParryOutcome outcome = plan.IsElemental && _judge != null
                ? _judge.ResolveImpact(Element.Wood, now, parryPoint, plan.Attack) : ParryOutcome.None;
            float damage = plan.Damage;
            if (outcome == ParryOutcome.Success) damage = 0f;
            else if (outcome == ParryOutcome.Half) damage *= _config != null ? _config.HalfParryDamageFactor : .4f;
            else if (outcome == ParryOutcome.Block) damage *= _config != null ? _config.GuardBlockFactor : .5f;
            float before = _playerVitals.Hp01 * _playerVitals.MaxHp;
            if (damage > 0f && CurrentPlan == plan && Eligible()) _playerVitals.TakeAttackDamage(damage,
                plan.Kind == CheongryongAttackKind.RootEruption || plan.Kind == CheongryongAttackKind.WoodProjectile ? IncomingDamageKind.ElementalRanged : IncomingDamageKind.Melee);
            float applied = Mathf.Max(0f, before - _playerVitals.Hp01 * _playerVitals.MaxHp);
            // Reward ownership stays exclusively with the existing OwnedImpactResolved listener.
            AttackImpactResolved?.Invoke(new CheongryongAttackImpact(plan, point, outcome, applied, true));
        }

        private void FinishAttack(CheongryongAttackPlan plan, float now)
        {
            if (CurrentPlan != plan) return;
            if (!plan.ContactConsumed) ResolveContact(plan, plan.TargetPoint, false, now);
            if (CurrentPlan != plan) return;
            CurrentPlan = null;
            _readyAt = now + (plan.EndAt - plan.ActiveEndAt) + _profile.CooldownSeconds * (PhaseTwoCadence ? _profile.PhaseTwoCooldownFactor : 1f);
            SetState(CheongryongCombatState.Recovery); AttackEnded?.Invoke(plan, false);
        }

        private void CancelPlan()
        {
            var plan = CurrentPlan; CurrentPlan = null;
            if (plan == null) return;
            plan.Cancel(); AttackEnded?.Invoke(plan, true);
        }
        private void SetState(CheongryongCombatState state) { if (State == state) return; State = state; StateChanged?.Invoke(state); }

        private bool Ignore(RaycastHit hit) => hit.transform == null || hit.transform.IsChildOf(transform) ||
            (_player != null && hit.transform.IsChildOf(_player)) || hit.transform.GetComponentInParent<EnemyVitals>() != null ||
            hit.transform.GetComponentInParent<PlayerVitals>() != null;

        private bool Obstructed(Vector3 origin, Vector3 end)
        {
            Vector3 delta = end - origin;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, origin, delta, delta.magnitude, _profile.EnvironmentMask, ref _occlusionHits);
            for (int i = 0; i < count; i++) if (!Ignore(_occlusionHits[i])) return true;
            return false;
        }

        private void ClipProjectile(CheongryongAttackPlan plan)
        {
            // Recheck the whole travelled ray; clipping is permanent even if a struck wall disappears.
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, plan.Origin, plan.Direction,
                Mathf.Min(plan.Range, Mathf.Max(0f, plan.SampleTime - plan.ReleaseAt) * plan.Speed), _profile.EnvironmentMask, ref _occlusionHits);
            for (int i = 0; i < count; i++) if (!Ignore(_occlusionHits[i])) plan.ClipTo(_occlusionHits[i].distance);
        }

        private bool TryGround(Vector3 target, out Vector3 ground)
        {
            ground = target; float nearest = float.PositiveInfinity; bool found = false;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, target + Vector3.up * _profile.GroundProbeUp, Vector3.down,
                _profile.GroundProbeUp + _profile.GroundProbeDown, _profile.EnvironmentMask, ref _groundHits);
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (Ignore(hit) || hit.normal.y < .5f || hit.distance >= nearest) continue;
                nearest = hit.distance; ground = hit.point; found = true;
            }
            return found;
        }
    }
}
