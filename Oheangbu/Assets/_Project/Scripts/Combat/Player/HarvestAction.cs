using UnityEngine;

namespace Oheangbu.Combat
{
    // 갈무리 — COMBAT-HARVEST: 클릭(구 평타 자리) · 락온 대상 한정 · 사거리 내 · 약피해 + 먹 수급.
    // 「갈무리만으로 처치 성립 불가」 — 피해를 HP 1에서 바닥 처리한다(§10.1 최소 해석).
    public sealed class HarvestAction : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private LockOn _lockOn;

        private InkPool _ink;
        private float _cooldownUntil;

        public void Init(InkPool ink)
        {
            _ink = ink;
        }

        public bool TryHarvest()
        {
            if (_config == null || _lockOn == null || Time.time < _cooldownUntil) return false;
            var target = _lockOn.Target;
            if (target == null) return false;
            if (Vector3.Distance(transform.position, target.transform.position) > _config.HarvestRange) return false;

            _cooldownUntil = Time.time + _config.HarvestCooldown;
            target.TakeDamage(_config.HarvestDamage, floorAtOneHp: true);
            _ink?.Gain(_config.HarvestInkGain); // 먹 수급 — 교전·락온에 묶인 수입(먹 3원)
            return true;
        }
    }
}
