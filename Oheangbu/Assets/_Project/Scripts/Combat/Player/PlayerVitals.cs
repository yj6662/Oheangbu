using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 플레이어 생명 — 피격의 유일 창구. 피격 보상 금지(헌법)·응보 허용은 여기 훅에 무엇을
    // 달지 않는 것으로 지켜진다. 「피격=작도 중단」은 Damaged 이벤트를 App 배선이
    // InterruptLetter로 연결해 이행한다(COMBAT-ATTACK — 표현·입력 계층에 직접 닿지 않는다).
    public sealed class PlayerVitals : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private DodgeAction _dodge;

        private float _hp;

        public event Action<float> Damaged; // 실피해량 — 무적으로 막힌 피격은 발화하지 않는다
        public event Action Died;
        public event Action HpChanged;

        public float Hp01 => _config != null && _config.PlayerMaxHp > 0f ? _hp / _config.PlayerMaxHp : 0f;

        private void Awake()
        {
            _hp = _config != null ? _config.PlayerMaxHp : 100f;
        }

        // true = 실제로 맞았다(회피 무적이면 false — 판정만 있고 결과 없음)
        public bool TakeDamage(float amount)
        {
            if (_dodge != null && _dodge.IsInvulnerable) return false;
            if (_hp <= 0f) return false;

            _hp = Mathf.Max(0f, _hp - amount);
            HpChanged?.Invoke();
            Damaged?.Invoke(amount);
            if (_hp <= 0f) Died?.Invoke();
            return true;
        }
    }
}
