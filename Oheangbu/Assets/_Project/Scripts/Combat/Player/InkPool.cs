using Oheangbu.Core.Events;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 먹 풀 — COMBAT-HARVEST 먹 3원(프로토는 갈무리·패링 2원)의 계좌.
    // 자연 회복은 없다: 「몸을 써야 먹이 찬다」 — 수입 경로는 Gain 호출자(갈무리·패링 환급)뿐.
    // 순수 클래스(VContainer 등록) — 표시는 채널 방송을 구독하는 쪽(HUD·어댑터)의 몫.
    public sealed class InkPool
    {
        private readonly FloatEventChannelSO _changed;
        private float _value;

        public float Value => _value;

        public InkPool(CombatConfigSO config, FloatEventChannelSO changedChannel)
        {
            _changed = changedChannel;
            _value = config != null ? config.InkStart : 1f;
        }

        // 부족하면 소비하지 않고 false — 먹 부족 술식=불발 취급(SPEC §10.1 [제안])
        public bool TrySpend(float amount)
        {
            if (_value + 1e-4f < amount) return false;
            _value = Mathf.Clamp01(_value - amount);
            _changed?.Raise(_value);
            return true;
        }

        // 불발 등 「있는 만큼만 깎이는」 지출 — 잔량이 모자라도 바닥까지는 소모된다
        public void SpendClamped(float amount)
        {
            _value = Mathf.Clamp01(_value - amount);
            _changed?.Raise(_value);
        }

        public void Gain(float amount)
        {
            _value = Mathf.Clamp01(_value + amount);
            _changed?.Raise(_value);
        }

        // 초기 방송 — 구독자가 늦게 붙어도 첫 값을 받도록 배선부가 부른다
        public void Broadcast()
        {
            _changed?.Raise(_value);
        }
    }
}
