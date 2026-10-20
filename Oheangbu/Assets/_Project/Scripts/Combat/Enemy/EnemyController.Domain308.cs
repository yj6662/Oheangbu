using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 WP-17 (SPEC-SPELL-120-308, D308-121 결계): what a player's domain does to this enemy's attacks, read at the attack
    // seams only (EnemyVitals.Modifiers.Domain). An enemy without vitals or without an entry behaves exactly as before.
    //   굿 hold       no new attack starts while held (one already started goes on)
    //   욱 telegraph  the telegraph is read once when it starts and lasts that many times as long
    //   웃 turn       the body turns slower; a melee arc then swings where the body faces, not where the player stands
    //   눙 aim        the aim locks at a share of the telegraph; a player who moved off the locked point is missed
    public sealed partial class EnemyController
    {
        private float _aimLockAt308 = float.PositiveInfinity, _aimRadius308, _telegraph308;
        private bool _aimLocked308;
        private Vector3 _aimPoint308;

        private EnemyDomainState308 Domain308 => _vitals != null ? _vitals.Modifiers.Domain : null;
        private bool DomainHeld308() => Domain308 != null && Domain308.AttackHeld(Time.time);
        private float DomainTelegraph308() => Domain308 != null ? Domain308.TelegraphScale(Time.time) : 1f;
        private float DomainTurn308(float rate) => Domain308 != null ? Domain308.TurnRate(rate, Time.time) : rate;
        private bool DomainTurned308() => Domain308 != null && Domain308.TurnScale(Time.time) < 1f;

        /// <summary>The aim of the current attack was locked by a domain (눙); where it locked.</summary>
        public bool AimLocked308 => _aimLocked308;
        public Vector3 AimLockPoint308 => _aimPoint308;

        // A telegraph of this length (domain scale included) starts now: TelegraphDuration answers it (so the rig's windup
        // stretches with it) and the aim entry is read once.
        private void StartTelegraph308(float telegraph)
        {
            _telegraph308 = telegraph;
            _aimLocked308 = false; _aimRadius308 = 0f;
            float share = Domain308 != null ? Domain308.AimLockShare(Time.time, out _aimRadius308) : -1f;
            _aimLockAt308 = share < 0f ? float.PositiveInfinity : Time.time + share * Mathf.Max(0f, telegraph);
            TickAimLock308();
        }

        // Called every frame of a telegraph or a flight.
        private void TickAimLock308()
        {
            if (_aimLocked308 || _player == null || !(Time.time >= _aimLockAt308)) return;
            _aimLocked308 = true; _aimPoint308 = _player.position;
            // a homing shot already in the air stops following here
            if (ProfileActive && _state == State.Flight && _attackProfile.Delivery == EnemyAttackDelivery.HomingProjectile)
                _authoredPoint = _aimPoint308 + Vector3.up * .5f;
        }

        private Vector3 AimTarget308(Vector3 live) => _aimLocked308 ? _aimPoint308 : live;

        // An attack with no radius of its own (the legacy shot) misses a player who stands off the locked point.
        private bool AimMissed308(Vector3 player)
        {
            if (!_aimLocked308 || !(_aimRadius308 > 0f)) return false;
            float dx = player.x - _aimPoint308.x, dz = player.z - _aimPoint308.z;
            return dx * dx + dz * dz > _aimRadius308 * _aimRadius308;
        }

        private void EndAimLock308() { _aimLocked308 = false; _aimLockAt308 = float.PositiveInfinity; }
    }
}
