using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 D308-125 적용 6 (막 · 공): a spell's cover stops a shot that is already in the air where its path meets the cover.
    // The cover asks where the shot is; stopping it ends the attack the way a shot that hits the terrain ends
    // (TickAuthoredProjectile: Obstructed -> cancel, recover), so the enemy's rhythm does not change.
    public sealed partial class EnemyController
    {
        /// <summary>Where the shot in the air is now (the authored flight's last ticked point).</summary>
        public Vector3 FlightPoint308 => ProfileActive ? _authoredProjectilePrevious : _projectile != null ? _projectile.position : _projectileStart;
        /// <summary>Where the shot in the air left the hand.</summary>
        public Vector3 FlightStart308 => _projectileStart;

        /// <summary>Ends the shot in the air as if it hit the terrain. False when nothing is in the air.</summary>
        public bool StopFlight308()
        {
            if (_state != State.Flight) return false;
            CancelAttack();
            EnterRecover();
            return true;
        }
    }
}
