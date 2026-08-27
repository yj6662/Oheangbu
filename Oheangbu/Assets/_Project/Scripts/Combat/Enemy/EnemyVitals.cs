using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 적 생명 — 이층 구조의 「체력」 쪽(COMBAT-CHARTER: 딜은 체력을 무너뜨린다).
    // 그로기는 여기 없다 — 별도 게이지(GroggyMeter)라는 분리 자체가 이층 구조의 코드 증명.
    public sealed class EnemyVitals : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;

        private float _hp;

        public event Action HpChanged;
        public event Action Died;

        // 만개(급소창) 동안 컨트롤러가 올린다 — 「전 공격 피해 증폭」(COMBAT-GROGGY)
        public float DamageMultiplier { get; set; } = 1f;

        public bool IsAlive => _hp > 0f;
        public float Hp01 => _config != null && _config.EnemyMaxHp > 0f ? _hp / _config.EnemyMaxHp : 0f;

        private void Awake()
        {
            _hp = _config != null ? _config.EnemyMaxHp : 60f;
        }

        // floorAtOneHp: 갈무리 전용 — 단독 처치 성립 불가(COMBAT-HARVEST)
        public void TakeDamage(float amount, bool floorAtOneHp = false)
        {
            if (!IsAlive) return;
            float damage = amount * DamageMultiplier;
            float floor = floorAtOneHp ? 1f : 0f;
            _hp = Mathf.Max(floor, _hp - damage);
            HpChanged?.Invoke();
            if (_hp <= 0f) Died?.Invoke();
        }
    }
}
