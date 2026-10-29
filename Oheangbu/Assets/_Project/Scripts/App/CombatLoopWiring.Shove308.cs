using System;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-129 (토론 17, TEST): who may push the player. After a real hit (AttackDamaged: a dodge's invulnerability never
    // gets here) the attacker's current profile names the push (ShoveDistance / ShoveSeconds, 0 = none: the 적로 불승냥이 charge
    // pushes, its Fallback bite does not), along the attacker's facing (a charge runs the line it locked). 숨 keeps the player in
    // place: no push starts inside its domain, and one under way stops on the step the player is inside (the motor asks
    // ShoveBlocked308 every step). The damage is never touched here, and nothing happens to the attacker (no hit reward).
    public sealed partial class CombatLoopWiring
    {
        PlayerMotor _shoveMotor308;
        PlayerVitals _shoveVitals308;

        // Hits 숨 kept from moving the player (the checks read it; the look will).
        public int ShovesHeld308 { get; private set; }

        void HookShove308()
        {
            UnhookShove308();
            if (_playerVitals == null || _motor == null) return;
            _shoveVitals308 = _playerVitals; _shoveMotor308 = _motor;
            _shoveVitals308.AttackDamaged += OnAttackDamaged308;
            _shoveMotor308.ShoveBlocked308 = Anchored308;
        }

        void UnhookShove308()
        {
            if (_shoveVitals308 != null) _shoveVitals308.AttackDamaged -= OnAttackDamaged308;
            if (_shoveMotor308 != null && _shoveMotor308.ShoveBlocked308 == (Func<bool>)Anchored308) _shoveMotor308.ShoveBlocked308 = null;
            _shoveVitals308 = null; _shoveMotor308 = null;
        }

        bool Anchored308() => _playerDomain308.Anchored(Time.time);

        void OnAttackDamaged308(PlayerHitInfo hit)
        {
            if (_shoveMotor308 == null || hit.Attacker == null) return;
            var attacker = hit.Attacker.GetComponent<EnemyController>();
            var profile = attacker != null ? attacker.AttackProfile : null;
            if (profile == null || !(profile.ShoveDistance > 0f) || !(profile.ShoveSeconds > 0f)) return;
            if (Anchored308()) { ShovesHeld308++; return; }
            _shoveMotor308.Shove308(attacker.transform.forward, profile.ShoveDistance, profile.ShoveSeconds);
        }
    }
}
