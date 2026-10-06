using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    public sealed partial class EnemyController
    {
        [SerializeField] private EnemyAttackProfileSO _attackProfile;
        private bool _authoredProfileValid;
        private bool _authoredCueActive;
        private Vector3 _authoredPoint, _authoredOrigin, _authoredForward;
        private Vector3 _authoredProjectilePrevious;
        private float _authoredFlightDuration;
        private RaycastHit[] _authoredGroundHits;
        private bool ProfileActive => _authoredProfileValid && _attackProfile != null;
        public EnemyAttackProfileSO AttackProfile => ProfileActive ? _attackProfile : null;
        public AttackMode EffectiveAttackMode => ProfileActive ? _attackProfile.Mode : _attackMode;
        public float MovementSpeedHint => ProfileActive ? _attackProfile.MovementSpeedHint : 2.6f;
        public float PreferredDistanceHint => ProfileActive ? _attackProfile.PreferredDistanceHint : EffectiveAttackMode == AttackMode.RangedOnly ? 9f : 1.7f;
        public float AttackRange => ProfileActive ? _attackProfile.Range : EffectiveAttackMode == AttackMode.MeleeOnly ? (_config != null ? _config.MeleeRange : 2.4f) : (_config != null ? _config.EnemyEngageRange : 12f);
        private bool LegacyMeleeSelected => _attackMode == AttackMode.MeleeOnly || (_attackMode == AttackMode.LegacyDistance && _player != null && Distance() <= (_config != null ? _config.EnemyMeleePreferRange : 3f));
        public float AttackDamage => ProfileActive ? _attackProfile.Damage : LegacyMeleeSelected ? (_config != null ? _config.MeleeDamage : 15f) : (_config != null ? _config.RangedDamage : 12f);
        public float TelegraphDuration => ProfileActive ? _attackProfile.Telegraph : LegacyMeleeSelected ? (_config != null ? _config.MeleeTelegraph : .8f) : (_config != null ? _config.RangedTelegraph : 1.2f);
        public float RecoveryDuration => ProfileActive ? _attackProfile.Recovery : .4f;
        public AttackProvenance CurrentAttack => _attack;
        public Vector3 AuthoredTargetPoint => _authoredPoint;
        public event Action<EnemyAttackCue> AttackTelegraphed;
        public event Action<EnemyAttackImpact> AttackImpactResolved;
        public event Action<AttackProvenance, bool> AttackPresentationEnded;

        // #306 #11 tutorial boss sequencing (SPEC-PLAYTEST-306): the combat rules stay here; a sequencer only chooses the next
        // authored profile while idle, holds new attacks, or holds a projectile short of the player until a predicate releases it.
        /// <summary>No new attack starts while true (an attack already under way is never cancelled by this flag).</summary>
        public bool AttackHeld { get; set; }
        /// <summary>While this returns true, an authored projectile within FlightHoldDistance of its target stops (its impact
        /// time slides with scaled time, so PredictedImpactTime and the organ telegraph keep pace). null = never held.</summary>
        public Func<bool> FlightHold { get; set; }
        [NonSerialized] public float FlightHoldDistance;
        public bool FlightHeld { get; private set; }

        /// <summary>Swaps the authored profile only between attacks (idle) and sets the next cooldown. Unlike Configure this
        /// never stops or restarts anything, so a combo follow-up can start right after a recovery.</summary>
        public bool TrySetIdleProfile(EnemyAttackProfileSO profile, float cooldown)
        {
            if (profile == null || _state != State.Idle || !float.IsFinite(cooldown) || !profile.TryValidate(out _)) return false;
            _attackProfile = profile; _authoredProfileValid = true; _cooldown = Mathf.Max(0f, cooldown);
            return true;
        }

        public void Configure(EnemyAttackProfileSO profile)
        {
            if (profile != null && !profile.TryValidate(out var error)) throw new ArgumentException(error, nameof(profile));
            StopAttack();
            _attackProfile = profile; _authoredProfileValid = profile != null;
            if (_state == State.Idle) EnterIdle();
        }

        private void ValidateAuthoredProfile()
        {
            _authoredProfileValid = _attackProfile != null && _attackProfile.TryValidate(out _);
            if (_attackProfile != null && !_authoredProfileValid)
                Debug.LogError("Invalid enemy attack profile; preserving legacy CombatConfig behavior.", this);
        }

        private void TickAuthoredAttack()
        {
            switch (_state)
            {
                case State.Idle:
                    _cooldown -= Time.deltaTime;
                    if (AttackEnabled && !AttackHeld && _cooldown <= 0 && Distance() <= _attackProfile.Range && HasLineOfSight()) BeginAuthoredTelegraph();
                    break;
                case State.Telegraph:
                    if (!AttackEnabled || !HasLineOfSight()) { CancelAttack(); EnterRecover(); break; }
                    if (Time.time >= _stateUntil) FinishAuthoredTelegraph();
                    break;
                case State.Flight:
                    if (!AttackEnabled || !HasLineOfSight()) { CancelAttack(); EnterRecover(); break; }
                    TickAuthoredProjectile(); break;
                case State.Recover:
                    if (Time.time >= _stateUntil) EnterIdle(); break;
                case State.Stunned:
                    _vitals?.ExpireWeakPoint(Time.time); break;
            }
        }

        private void BeginAuthoredTelegraph()
        {
            if (!ProfileActive || !AttackEnabled || _player == null || !HasLineOfSight()) return;
            _authoredPoint = _player.position;
            if (_attackProfile.Delivery == EnemyAttackDelivery.GroundEruption && !TryAuthoredGround(_player.position, out _authoredPoint))
            { EnterRecover(); return; }
            _authoredOrigin = transform.position;
            _authoredForward = Vector3.ProjectOnPlane(_player.position - transform.position, Vector3.up).normalized;
            if (_authoredForward.sqrMagnitude < .01f) _authoredForward = transform.forward;
            _attack = AttackProvenance.Create(_vitals, DamageSource.Enemy, _attackProfile.Elemental ? _attackProfile.Element : (Element?)null);
            _state = State.Telegraph; _stateUntil = Time.time + _attackProfile.Telegraph; AttackStartTime = Time.time; RefreshOrganTint();
            _authoredCueActive = true;
            if (!_attackProfile.Elemental) Tint(_neutralColor); else if (_tintBodyOnTelegraph) Tint(_elementColor); // neutral keeps the darkening-ink cue
            AttackTelegraphed?.Invoke(new EnemyAttackCue(_attack, _attackProfile.Delivery, _authoredPoint, _attackProfile.Telegraph, _attackProfile.ImpactRadius));
        }

        private void FinishAuthoredTelegraph()
        {
            if (!AttackEnabled || !HasLineOfSight()) { CancelAttack(); EnterRecover(); return; }
            var delivery = _attackProfile.Delivery;
            if (delivery == EnemyAttackDelivery.MeleeArc || delivery == EnemyAttackDelivery.GroundEruption)
            {
                bool inShape = delivery == EnemyAttackDelivery.MeleeArc
                    ? InAuthoredMeleeArc(_player.position)
                    : InAuthoredRadius(_player.position, _authoredPoint, _attackProfile.ImpactRadius);
                ResolveAuthoredContact(_authoredPoint, inShape);
                if (_state == State.Telegraph) FinishAuthoredAttack();
                return;
            }
            _projectileStart = transform.position + Vector3.up * 1.2f;
            _authoredProjectilePrevious = _projectileStart;
            _authoredPoint = _player.position + Vector3.up * .5f; // Aimed water shots stop following here.
            _authoredFlightDuration = Mathf.Max(.05f, Vector3.Distance(_projectileStart, _authoredPoint) / _attackProfile.ProjectileSpeed);
            _impactTime = Time.time + _authoredFlightDuration;
            if (_projectile != null) { _projectile.position = _projectileStart; ShowProjectile(); }
            _state = State.Flight;
        }

        private void TickAuthoredProjectile()
        {
            float remaining = _impactTime - Time.time;
            bool homing = _attackProfile.Delivery == EnemyAttackDelivery.HomingProjectile;
            Vector3 target = homing ? _player.position + Vector3.up * .5f : _authoredPoint;
            // #306 #11: a held shard waits short of the player; the impact time slides so nothing resolves while it waits
            bool hold = FlightHold != null && FlightHoldDistance > 0f && remaining > 0f &&
                remaining * _attackProfile.ProjectileSpeed <= FlightHoldDistance && FlightHold();
            if (hold) { _impactTime += Time.deltaTime; remaining = _impactTime - Time.time; }
            FlightHeld = hold;
            Vector3 nextPoint = Vector3.Lerp(_projectileStart, target, Mathf.Clamp01(1f - remaining / _authoredFlightDuration));
            if ((nextPoint - _authoredProjectilePrevious).sqrMagnitude > .0001f && Obstructed(_authoredProjectilePrevious, nextPoint))
            { CancelAttack(); EnterRecover(); return; }
            _authoredProjectilePrevious = nextPoint;
            if (_projectile != null) _projectile.position = nextPoint;
            if (remaining > 0) return;
            bool inShape = homing || InAuthoredRadius(_player.position + Vector3.up * .5f, target, _attackProfile.ImpactRadius);
            Vector3 eye = _player.position + Vector3.up * 1.1f;
            ResolveAuthoredContact(eye - (_player.position - _projectileStart).normalized, inShape);
            if (_state == State.Flight) FinishAuthoredAttack();
        }

        private void ResolveAuthoredContact(Vector3 point, bool inShape)
        {
            if (!inShape || !AttackEnabled || !HasLineOfSight() || _playerVitals == null)
            { AttackImpactResolved?.Invoke(new EnemyAttackImpact(_attack, point, ParryOutcome.None, 0, false)); return; }
            ParryOutcome outcome = _attack.Element.HasValue && _judge != null
                ? _judge.ResolveImpact(_attack.Element.Value, Time.time, point, _attack) : ParryOutcome.None;
            float damage = _attackProfile.Damage;
            if (outcome == ParryOutcome.Success) { damage = 0; FlashParried(); }
            else if (outcome == ParryOutcome.Half) damage *= _config.HalfParryDamageFactor;
            else if (outcome == ParryOutcome.Block) damage *= _config.GuardBlockFactor;
            float before = _playerVitals.Hp01 * _playerVitals.MaxHp;
            if (damage > 0) EnemyStrike308.Deliver(_playerVitals, _vitals, damage, _attackProfile.Delivery == EnemyAttackDelivery.MeleeArc ? (_attack.Element.HasValue ? IncomingDamageKind.ElementalMelee : IncomingDamageKind.Melee) : (_attack.Element.HasValue ? IncomingDamageKind.ElementalRanged : IncomingDamageKind.Ranged));
            float applied = Mathf.Max(0, before - _playerVitals.Hp01 * _playerVitals.MaxHp);
            AttackImpactResolved?.Invoke(new EnemyAttackImpact(_attack, point, outcome, applied, true));
        }

        private bool InAuthoredMeleeArc(Vector3 playerPoint)
        {
            Vector3 delta = playerPoint - _authoredOrigin;
            if (Mathf.Abs(delta.y) > 2 || delta.magnitude > _attackProfile.Range) return false;
            delta.y = 0;
            return delta.sqrMagnitude < .001f || Vector3.Angle(_authoredForward, delta) <= _attackProfile.ArcDegrees * .5f;
        }
        private static bool InAuthoredRadius(Vector3 playerPoint, Vector3 center, float radius)
        {
            Vector3 delta = playerPoint - center;
            if (Mathf.Abs(delta.y) > 2f) return false;
            delta.y = 0; return delta.sqrMagnitude <= radius * radius;
        }
        private bool TryAuthoredGround(Vector3 position, out Vector3 ground)
        {
            ground = position; float nearest = float.PositiveInfinity; bool found = false;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, position + Vector3.up * 2, Vector3.down, 6, ~0, ref _authoredGroundHits);
            for (int i = 0; i < count; i++)
            {
                var hit = _authoredGroundHits[i];
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(_player) || hit.normal.y < .35f || hit.distance >= nearest) continue;
                nearest = hit.distance; ground = hit.point; found = true;
            }
            return found;
        }
        private void FinishAuthoredAttack()
        {
            if (_projectile != null) _projectile.gameObject.SetActive(false);
            EndAuthoredCue(false); EnterRecover();
        }
        private void EndAuthoredCue(bool cancelled)
        {
            FlightHeld = false;
            if (!_authoredCueActive) return;
            _authoredCueActive = false; AttackPresentationEnded?.Invoke(_attack, cancelled);
        }
    }
}
