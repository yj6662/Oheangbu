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
        private float _cooldownLength;   // #308 HUD: length of the running cooldown (display only)
        private Vector3 _dashVelocity;
        private float _dashStarted;

        public bool IsInvulnerable => Time.time < _invulnerableUntil;
        public bool IsDashing => Time.time < _dashUntil;
        public Vector3 Direction => _dashVelocity.sqrMagnitude > 0f ? _dashVelocity.normalized : Vector3.zero;
        public float Progress => IsDashing ? Mathf.Clamp01((Time.time - _dashStarted) / Mathf.Max(_dashUntil - _dashStarted, 0.0001f)) : 1f;
        // #308 HUD (SPEC-HUD-LIQUID-308 §3.2): read-only cooldown for the dodge mark. Still nothing here touches groggy or ink.
        public float CooldownRemaining => Mathf.Max(0f, _cooldownUntil - Time.time);
        public float Cooldown01 => _cooldownLength > 0f ? 1f - Mathf.Clamp01(CooldownRemaining / _cooldownLength) : 1f;

        public bool TryDodge(Vector3 direction) => TryDodge(direction, 0f);
        public void Cancel() { _dashUntil = _invulnerableUntil = 0f; _dashVelocity = Vector3.zero; }

        // Only the opt-in crouch roll changes travel duration. Distance and immunity stay in CombatConfig.
        public bool TryDodge(Vector3 direction, float durationOverride)
        {
            if (_config == null || IsDashing || Time.time < _cooldownUntil) return false;
            float duration = durationOverride > 0f ? Mathf.Clamp(durationOverride, .4f, 1.2f) : _config.DodgeDuration;
            float speed = _config.DodgeDistance / Mathf.Max(duration, 0.01f);
            _dashVelocity = direction.normalized * speed;
            _dashStarted = Time.time;
            _dashUntil = Time.time + duration;
            _invulnerableUntil = Time.time + _config.DodgeInvulnerable; // 즉발 — 입력 프레임부터 무적
            _cooldownLength = _config.DodgeCooldown;
            _cooldownUntil = Time.time + _cooldownLength;
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
