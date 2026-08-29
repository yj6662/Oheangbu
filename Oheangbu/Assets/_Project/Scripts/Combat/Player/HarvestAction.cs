using UnityEngine;

namespace Oheangbu.Combat
{
    // 갈무리 — COMBAT-HARVEST: 락온 대상 한정 · 사거리 내 · 약피해 + 먹 수급.
    // 입력은 홀드다(3차 플레이 검수 확정 2026-08-28) — 누르는 동안 붓이 상대의 먹을 뽑아낸다.
    // 「갈무리만으로 처치 성립 불가」 — 피해를 HP 1에서 바닥 처리한다(§10.1 최소 해석).
    public sealed class HarvestAction : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private LockOn _lockOn;

        private InkPool _ink;

        public void Init(InkPool ink)
        {
            _ink = ink;
        }

        // 홀드 중 매 프레임 — 초당 비율 × dt. 게이트를 통과 못 하면 그 프레임은 아무 일도 없다
        public void TickHarvest(float deltaTime)
        {
            if (_config == null || _lockOn == null || deltaTime <= 0f) return;
            var target = _lockOn.Target;
            if (target == null || !target.IsAlive) return;
            if (Vector3.Distance(transform.position, target.transform.position) > _config.HarvestRange) return;

            target.TakeDamage(_config.HarvestDamagePerSecond * deltaTime, floorAtOneHp: true);
            _ink?.Gain(_config.HarvestInkPerSecond * deltaTime); // 먹 수급 — 교전·락온에 묶인 수입(먹 3원)
        }
    }
}
