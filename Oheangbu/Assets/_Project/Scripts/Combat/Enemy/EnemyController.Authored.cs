using System;
using Oheangbu.Core.Domain;
using Oheangbu.Data;
using UnityEngine;
using UnityEngine.AI;

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
        public float TelegraphDuration => IsTelegraphing && _telegraph308 > 0f ? _telegraph308 : ProfileActive ? _attackProfile.Telegraph : LegacyMeleeSelected ? (_config != null ? _config.MeleeTelegraph : .8f) : (_config != null ? _config.RangedTelegraph : 1.2f);
        // SPEC-PACE-308 section 5 (D308-116): one recovery for the state clock, its progress and the rig motion; the pace scale is read once in Awake
        public float RecoveryDuration => (ProfileActive ? (_chargeWall308 ? _attackProfile.ChargeWallRecovery : _attackProfile.Recovery) : .4f) * _recoveryScale308;
        private float _recoveryScale308 = 1f;
        private void LoadPace308() => _recoveryScale308 = PaceProfile308.RecoveryScaleOf(PaceProfile308.Load());
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
            _home308 = null; CloseIn308 = false; _closeInCheck308 = 0f;
            return true;
        }

        public void Configure(EnemyAttackProfileSO profile)
        {
            if (profile != null && !profile.TryValidate(out var error)) throw new ArgumentException(error, nameof(profile));
            StopAttack();
            _attackProfile = profile; _authoredProfileValid = profile != null;
            _home308 = null; CloseIn308 = false; _closeInCheck308 = 0f;
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
                {
                    _cooldown -= Time.deltaTime;
                    bool ready = AttackEnabled && !AttackHeld && !DomainHeld308() && _cooldown <= 0 && Distance() <= _attackProfile.Range && HasLineOfSight()
                        && !(_player != null && DomainFloorBlocked308(_player.position));
                    if (_attackProfile.Delivery != EnemyAttackDelivery.Charge) { CloseIn308 = false; if (ready) BeginAuthoredTelegraph(); }
                    else if (ChargePathOpen308(ready)) { if (ready) BeginAuthoredTelegraph(); }
                    else if (ready && CloseIn308 && _attackProfile.Fallback != null && Distance() <= _attackProfile.Fallback.Range) BeginFallback308();
                    break;
                }
                case State.Telegraph:
                    if (!AttackEnabled || !HasLineOfSight() || DomainFloorBlocked308(_authoredPoint)) { CancelAttack(); EnterRecover(); break; }
                    if (_attackProfile.Delivery == EnemyAttackDelivery.Charge && !TickChargeTelegraph308()) { CancelAttack(); EnterRecover(); break; }
                    if (Time.time >= _stateUntil) FinishAuthoredTelegraph();
                    break;
                case State.Flight:
                    if (!AttackEnabled || !HasLineOfSight()) { CancelAttack(); EnterRecover(); break; }
                    TickAuthoredProjectile(); break;
                case State.Charge:
                    if (!AttackEnabled) { CancelAttack(); EnterRecover(); break; }
                    TickAuthoredCharge(); break;
                case State.Recover:
                    if (Time.time >= _stateUntil) EnterIdle(); break;
                case State.Stunned:
                    _vitals?.ExpireWeakPoint(Time.time); break;
            }
        }

        private void BeginAuthoredTelegraph()
        {
            if (!ProfileActive || !AttackEnabled || _player == null || !HasLineOfSight()) return;
            ResetCharge308();
            _authoredPoint = _player.position;
            if (_attackProfile.Delivery == EnemyAttackDelivery.GroundEruption && !TryAuthoredGround(_player.position, out _authoredPoint))
            { EnterRecover(); return; }
            _authoredOrigin = transform.position;
            // #308 WP-17 웃: a slowed body swings where it faces
            _authoredForward = Vector3.ProjectOnPlane(DomainTurned308() ? transform.forward : _player.position - transform.position, Vector3.up).normalized;
            if (_authoredForward.sqrMagnitude < .01f) _authoredForward = transform.forward;
            _attack = AttackProvenance.Create(_vitals, DamageSource.Enemy, _attackProfile.Elemental ? _attackProfile.Element : (Element?)null);
            float tell = _attackProfile.Telegraph * DomainTelegraph308();
            if (_attackProfile.Delivery == EnemyAttackDelivery.Charge) _chargeLockAt308 = Time.time + Mathf.Max(0f, tell - _attackProfile.ChargeLockLead);
            _state = State.Telegraph; _stateUntil = Time.time + tell; AttackStartTime = Time.time; RefreshOrganTint();
            StartTelegraph308(tell);
            _authoredCueActive = true;
            if (!_attackProfile.Elemental) Tint(_neutralColor); else if (_tintBodyOnTelegraph) Tint(_elementColor); // neutral keeps the darkening-ink cue
            AttackTelegraphed?.Invoke(new EnemyAttackCue(_attack, _attackProfile.Delivery, _authoredPoint, tell, _attackProfile.ImpactRadius));
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
            if (delivery == EnemyAttackDelivery.Charge)
            {
                if (!_chargeLocked308) LockCharge308();
                _chargeDone308 = 0f; _chargeHit308 = _chargeWall308 = false;
                _state = State.Charge;
                return;
            }
            _projectileStart = transform.position + Vector3.up * 1.2f;
            _authoredProjectilePrevious = _projectileStart;
            _authoredPoint = AimTarget308(_player.position) + Vector3.up * .5f; // Aimed water shots stop following here (or where a domain locked the aim).
            _authoredFlightDuration = Mathf.Max(.05f, Vector3.Distance(_projectileStart, _authoredPoint) / _attackProfile.ProjectileSpeed);
            _impactTime = Time.time + _authoredFlightDuration;
            if (_projectile != null) { _projectile.position = _projectileStart; ShowProjectile(); }
            _state = State.Flight;
        }

        private void TickAuthoredProjectile()
        {
            float remaining = _impactTime - Time.time;
            bool homing = _attackProfile.Delivery == EnemyAttackDelivery.HomingProjectile && !_aimLocked308;
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
            // a charge is the body itself: it lands as melee (D308-125 적용 3)
            bool melee = _attackProfile.Delivery == EnemyAttackDelivery.MeleeArc || _attackProfile.Delivery == EnemyAttackDelivery.Charge;
            if (damage > 0) EnemyStrike308.Deliver(_playerVitals, _vitals, damage, melee ? (_attack.Element.HasValue ? IncomingDamageKind.ElementalMelee : IncomingDamageKind.Melee) : (_attack.Element.HasValue ? IncomingDamageKind.ElementalRanged : IncomingDamageKind.Ranged));
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

        // #308 D308-125 적용 3 (TEST): a charge runs the body itself. The telegraph tracks the player until ChargeLockLead before
        // its end (or until 눙 locked the aim); the body then runs that line at ProjectileSpeed x 묵 dash for the player's distance
        // + ChargeOvershoot (ChargeMinTravel..Range), clipped by the navmesh, steep solids and a domain's circle. The swept segment
        // hits the player once (a dodge that takes it still spends it) and the run goes on. Stopped short = ChargeWallRecovery.
        private bool _chargeLocked308, _chargeHit308, _chargeWall308;
        private float _chargeLockAt308 = float.PositiveInfinity, _chargeTravel308, _chargeDone308;
        private NavMeshAgent _agent308; private bool _agentRead308;
        private RaycastHit[] _chargeHits308;
        private bool ChargeTracking308 => ProfileActive && _state == State.Telegraph && _attackProfile.Delivery == EnemyAttackDelivery.Charge && !_chargeLocked308;
        private NavMeshAgent ChargeAgent308
        {
            get
            {
                if (!_agentRead308) { _agent308 = GetComponent<NavMeshAgent>(); _agentRead308 = true; }
                return _agent308 != null && _agent308.enabled && _agent308.isOnNavMesh ? _agent308 : null;
            }
        }
        private void ResetCharge308()
        {
            _chargeLocked308 = _chargeHit308 = _chargeWall308 = false;
            _chargeDone308 = _chargeTravel308 = 0f; _chargeLockAt308 = float.PositiveInfinity;
        }
        // below the walking speed a dash is a walk (묵 dash 0 on a common enemy): no charge starts and one under way stops
        private bool ChargeDashes308() => _attackProfile.ProjectileSpeed * DomainDash308() >= _attackProfile.MovementSpeedHint;

        // Read-only clock (PredictedImpactTime): the telegraph's end + the run to the player; while running, when the body reaches
        // the player's place along its line. NegativeInfinity once it hit or ran past.
        private float ChargeImpactTime308()
        {
            if (!ProfileActive || _player == null || _chargeHit308) return float.NegativeInfinity;
            float speed = Mathf.Max(.1f, _attackProfile.ProjectileSpeed * DomainDash308());
            Vector3 to = Vector3.ProjectOnPlane(_player.position - transform.position, Vector3.up);
            if (_state == State.Telegraph) return _stateUntil + Mathf.Max(0f, to.magnitude - _attackProfile.ImpactRadius) / speed;
            float along = Vector3.Dot(to, _authoredForward);
            return along < -_attackProfile.ImpactRadius ? float.NegativeInfinity : Time.time + Mathf.Max(0f, along - _attackProfile.ImpactRadius) / speed;
        }

        private bool ChargeCanStart308()
        {
            if (_player == null || !ChargeDashes308()) return false;
            Vector3 dir = Vector3.ProjectOnPlane(_player.position - transform.position, Vector3.up);
            if (dir.sqrMagnitude < .0001f) dir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (dir.sqrMagnitude < .0001f) return false;
            float need = _attackProfile.ChargeMinTravel;
            return ChargePathLength308(dir.normalized, need) >= need - 1e-3f;
        }

        // How far the body can run straight along dir, up to max: the navmesh edge or the nearest steep solid (a wall, a packmate).
        private float ChargePathLength308(Vector3 dir, float max)
        {
            Vector3 from = transform.position; float length = max;
            var agent = ChargeAgent308;
            if (agent != null && NavMesh.Raycast(from, from + dir * max, out var navHit, agent.areaMask)) length = Mathf.Min(length, navHit.distance);
            float radius = agent != null ? agent.radius : .5f;
            float wall = ChargeWallDistance308(from, dir, max + radius);
            if (wall < float.PositiveInfinity) length = Mathf.Min(length, Mathf.Max(0f, wall - radius));
            return length;
        }

        // Nearest solid along a knee-high ray that is not this body, not the player and not ground one can run up (normal.y < .5).
        private float ChargeWallDistance308(Vector3 from, Vector3 dir, float distance)
        {
            float nearest = float.PositiveInfinity;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, from + Vector3.up * .4f, dir, distance, ~0, ref _chargeHits308);
            for (int i = 0; i < count; i++)
            {
                var hit = _chargeHits308[i];
                if (hit.transform.IsChildOf(transform) || (_player != null && hit.transform.IsChildOf(_player)) || hit.normal.y >= .5f) continue;
                if (hit.distance < nearest) nearest = hit.distance;
            }
            return nearest;
        }

        // Every telegraph frame of a charge; false = call it off (the body may no longer dash).
        private bool TickChargeTelegraph308()
        {
            if (!ChargeDashes308()) return false;
            if (!_chargeLocked308 && (Time.time >= _chargeLockAt308 || _aimLocked308)) LockCharge308();
            return true;
        }

        private void LockCharge308()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude > .0001f) _authoredForward = fwd.normalized;
            float distance = _player != null ? Vector3.ProjectOnPlane(AimTarget308(_player.position) - transform.position, Vector3.up).magnitude : 0f;
            _chargeTravel308 = Mathf.Clamp(distance + _attackProfile.ChargeOvershoot, _attackProfile.ChargeMinTravel, _attackProfile.Range);
            _chargeLocked308 = true;
        }

        private void TickAuthoredCharge()
        {
            if (!ChargeDashes308()) { EndCharge308(); return; } // 묵: ran into the circle; it stops there, no hit
            Vector3 fwd = _authoredForward, from = transform.position;
            float step = Mathf.Min(_attackProfile.ProjectileSpeed * DomainDash308() * Time.deltaTime, Mathf.Max(0f, _chargeTravel308 - _chargeDone308));
            if (step > 0f)
            {
                Vector3 want = from + fwd * step;
                var agent = ChargeAgent308;
                if (agent != null && NavMesh.Raycast(from, want, out var navHit, agent.areaMask)) want = navHit.position;
                float radius = agent != null ? agent.radius : .5f;
                float wall = ChargeWallDistance308(from, fwd, step + radius);
                if (wall < float.PositiveInfinity)
                {
                    float allowed = Mathf.Max(0f, wall - radius);
                    if (Vector3.Dot(Vector3.ProjectOnPlane(want - from, Vector3.up), fwd) > allowed) want = new Vector3(from.x, want.y, from.z) + fwd * allowed;
                }
                if (Domain308 != null) want = Domain308.Contain(want, Time.time);
                if (agent != null) agent.Move(want - from); else transform.position = want;
            }
            transform.rotation = Quaternion.LookRotation(fwd);
            float advanced = Vector3.Dot(Vector3.ProjectOnPlane(transform.position - from, Vector3.up), fwd);
            _chargeDone308 += Mathf.Max(0f, advanced);
            if (!_chargeHit308 && _player != null && ChargeSwept308(from, transform.position))
            {
                _chargeHit308 = true;
                ResolveAuthoredContact(_player.position + Vector3.up, true);
                if (_state != State.Charge) return;
            }
            if (step > 1e-4f && advanced < .5f * step && _chargeDone308 < _chargeTravel308 - 1e-3f) _chargeWall308 = true;
            if (_chargeWall308 || _chargeDone308 >= _chargeTravel308 - 1e-3f) EndCharge308();
        }

        // The player within ImpactRadius of the segment the body swept this frame (ground plane, 2 m of height).
        private bool ChargeSwept308(Vector3 a, Vector3 b)
        {
            Vector3 p = _player.position;
            if (Mathf.Abs(p.y - b.y) > 2f) return false;
            float abx = b.x - a.x, abz = b.z - a.z, apx = p.x - a.x, apz = p.z - a.z, ab2 = abx * abx + abz * abz;
            float t = ab2 > 1e-6f ? Mathf.Clamp01((apx * abx + apz * abz) / ab2) : 0f;
            float dx = apx - abx * t, dz = apz - abz * t, r = _attackProfile.ImpactRadius;
            return dx * dx + dz * dz <= r * r;
        }

        private void EndCharge308()
        {
            if (!_chargeHit308) AttackImpactResolved?.Invoke(new EnemyAttackImpact(_attack, transform.position, ParryOutcome.None, 0, false));
            EndAuthoredCue(false); EnterRecover();
        }

        // #308 D308-125 적용 4 (TEST, 토론 6): a charger that cannot start its run (묵's circle, no straight path) closes in and
        // lands its Fallback melee arc once; back in idle it is the charge again and the charge's cooldown runs. CloseIn308 is the
        // same path check the attack choice uses (re-read every CloseInCheck308 s while idle, fresh at the moment of choice; once on,
        // it holds until that choice), and the brain walks in on it. Only the path or 묵 closes it in, never a cooldown or a missing turn.
        private const float CloseInCheck308 = .25f;
        private EnemyAttackProfileSO _home308;
        private float _closeInCheck308;
        private bool _chargeOpen308;
        /// <summary>Read only: the charger cannot start its run and has a fallback, so its owner should walk it in to bite.</summary>
        public bool CloseIn308 { get; private set; }
        private EnemyAttackProfileSO CloseInProfile308
        {
            get
            {
                var home = _home308 != null ? _home308 : _attackProfile;
                return ProfileActive && home.Delivery == EnemyAttackDelivery.Charge ? home.Fallback : null;
            }
        }
        public float CloseInRange308 => CloseInProfile308 != null ? CloseInProfile308.Range : AttackRange;
        public float CloseInDistance308 => CloseInProfile308 != null ? CloseInProfile308.PreferredDistanceHint : PreferredDistanceHint;

        private bool ChargePathOpen308(bool fresh)
        {
            _closeInCheck308 -= Time.deltaTime;
            if (!fresh && _closeInCheck308 > 0f) return _chargeOpen308;
            _closeInCheck308 = CloseInCheck308;
            bool near = _player != null && Distance() <= _attackProfile.Range;
            _chargeOpen308 = near && ChargeCanStart308();
            // a close-in holds until the next choice, so a packmate crossing the line does not flip the walk in and out (토론 6 Fable)
            CloseIn308 = near && _attackProfile.Fallback != null && (!_chargeOpen308 || CloseIn308 && !fresh);
            return _chargeOpen308;
        }

        private void BeginFallback308()
        {
            _home308 = _attackProfile; _attackProfile = _home308.Fallback;
            BeginAuthoredTelegraph();
            if (_state == State.Idle) RestoreHome308(); // it did not start
        }

        // every way back to idle (recovery, a stun's end, a bind, a reset), a disable and a death
        private void RestoreHome308()
        {
            if (_home308 != null) { _attackProfile = _home308; _home308 = null; }
            _closeInCheck308 = 0f;
        }
    }
}
