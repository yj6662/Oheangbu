using UnityEngine;

namespace Oheangbu.Combat
{
    // 회피 — COMBAT-DEFENSE 사다리 최하단: 무료 · 즉발 무적 · **무보상**.
    // 그로기·먹에 닿는 코드가 이 파일에 없다는 것 자체가 조항의 증명이다.
    public sealed class DodgeAction : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;

        private float _dashUntil;
        private float _invulnerableUntil;
        private float _cooldownUntil;
        private Vector3 _dashVelocity;

        public bool IsInvulnerable => Time.time < _invulnerableUntil;

        public bool TryDodge(Vector3 direction)
        {
            if (_config == null || Time.time < _cooldownUntil) return false;
            float speed = _config.DodgeDistance / Mathf.Max(_config.DodgeDuration, 0.01f);
            _dashVelocity = direction.normalized * speed;
            _dashUntil = Time.time + _config.DodgeDuration;
            _invulnerableUntil = Time.time + _config.DodgeInvulnerable; // 즉발 — 입력 프레임부터 무적
            _cooldownUntil = Time.time + _config.DodgeCooldown;
            return true;
        }

        // 대시 중이면 이동 속도를 대시로 대체한다(모터가 매 프레임 묻는다)
        public bool TryGetDashVelocity(out Vector3 velocity)
        {
            if (Time.time < _dashUntil)
            {
                velocity = _dashVelocity;
                return true;
            }
            velocity = default;
            return false;
        }
    }
}
