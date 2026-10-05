using Oheangbu.Core.Events;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 먹 풀 — COMBAT-HARVEST 먹 3원(D306 [TEST]: 갈무리 덩어리 뽑기 · 패링 순증 · 자연 회복)의 계좌.
    // 수입: Gain(갈무리·패링 환급 — HUD 번쩍임 있음) / Regenerate(자연 회복 — 번쩍임 없음, InkRegenerator가 부른다).
    // 순수 클래스(VContainer 등록) — 표시는 채널 방송을 구독하는 쪽(HUD·어댑터)의 몫.
    public sealed class InkPool
    {
        private readonly FloatEventChannelSO _changed;
        private float _value;
        public float CapacityMultiplier {get;private set;}=1f;
        public void SetCapacityMultiplier(float scale)
        {
            if(!float.IsFinite(scale)||scale<1f)throw new System.ArgumentOutOfRangeException(nameof(scale));
            // Capacity upgrades preserve the saved fill fraction and never raise Gained.
            CapacityMultiplier=scale;Broadcast();
        }
        // #308 (SPEC-SPELL-120-308 WP-08): a second, temporary capacity coefficient (a timed buff), multiplied with the
        // equipment multiplier above. Unlike an upgrade it keeps the amount of ink, not the fill fraction: widening the
        // vessel pours nothing in, and narrowing it spills only what no longer fits. Never raises Gained. Not saved:
        // the buff that set it puts it back to 1.
        public float TemporaryCapacity {get;private set;}=1f;
        public float TotalCapacity => CapacityMultiplier*TemporaryCapacity;
        public void SetTemporaryCapacity(float scale)
        {
            if(!float.IsFinite(scale)||scale<1f)throw new System.ArgumentOutOfRangeException(nameof(scale));
            float previous=_value;
            _value=Mathf.Clamp01(_value*TemporaryCapacity/scale);TemporaryCapacity=scale;
            if(_value!=previous)Broadcast(); // a full vessel that only narrows keeps reading full: no per-frame traffic
        }

        public float Value => _value;
        // 마지막 실지출 시각(Time.time, scaled) — 자연 회복 대기의 기준. 지출 전=음의 무한
        public float LastSpendTime { get; private set; } = float.NegativeInfinity;
        // Positive income only. Restore, spending, regeneration and an already-full pool never produce feedback.
        public event System.Action<float> Gained;
        public void Restore(float fraction=1f) { _value=Mathf.Clamp01(fraction); Broadcast(); }

        public InkPool(CombatConfigSO config, FloatEventChannelSO changedChannel)
        {
            _changed = changedChannel;
            _value = config != null ? config.InkStart : 1f;
        }

        // 부족하면 소비하지 않고 false — 먹 부족 술식=불발 취급(SPEC §10.1 [제안])
        public bool TrySpend(float amount)
        {
            amount=Mathf.Max(0f,amount)/TotalCapacity;
            if (_value + 1e-4f < amount) return false;
            _value = Mathf.Clamp01(_value - amount);
            if (amount > 0f) LastSpendTime = Time.time;
            _changed?.Raise(_value);
            return true;
        }

        // 불발 등 「있는 만큼만 깎이는」 지출 — 잔량이 모자라도 바닥까지는 소모된다
        public void SpendClamped(float amount)
        {
            amount=Mathf.Max(0f,amount)/TotalCapacity;
            _value = Mathf.Clamp01(_value - amount);
            if (amount > 0f) LastSpendTime = Time.time;
            _changed?.Raise(_value);
        }

        public void Gain(float amount)
        {
            amount=Mathf.Max(0f,amount)/TotalCapacity;
            float previous = _value;
            _value = Mathf.Clamp01(_value + amount);
            _changed?.Raise(_value);
            float received = _value - previous;
            if (received > 0f) Gained?.Invoke(received);
        }

        // 자연 회복 — 기본 용량 단위(Gain과 같은 척도), cap01(채움 비율)까지만. Gained를 부르지 않는다(D306).
        // 이미 상한이면 방송도 없다 — 가득 찬 통이 매 프레임 채널을 두드리지 않게. 반환=실제 채운 비율
        public float Regenerate(float amount, float cap01 = 1f)
        {
            cap01 = Mathf.Clamp01(cap01);
            amount = Mathf.Max(0f, amount) / TotalCapacity;
            if (amount <= 0f || _value >= cap01 || float.IsNaN(amount)) return 0f;
            float previous = _value;
            _value = Mathf.Min(cap01, _value + amount);
            _changed?.Raise(_value);
            return _value - previous;
        }

        // 초기 방송 — 구독자가 늦게 붙어도 첫 값을 받도록 배선부가 부른다
        public void Broadcast()
        {
            _changed?.Raise(_value);
        }
    }

    // 먹 자연 회복(D306 [TEST] — CONST-RULES 제3조 3항 개정): 모두에게 기본 회복, 쓴 뒤 대기, 작도 중 멈춤,
    // 오행 마석 등급 특전 = 회복 배율. 순수 클래스 — 배선부(CombatLoopWiring.Update)가 틱한다. 수치는 CombatConfigSO.
    public sealed class InkRegenerator
    {
        private readonly CombatConfigSO _config;
        private readonly InkPool _pool;
        public int Grade { get; private set; }
        public InkRegenerator(CombatConfigSO config, InkPool pool) { _config = config; _pool = pool; }

        // 오행 마석 등급(0=특전 없음) — 캠페인(세션)이 장착·등급 변경 시 알린다. 공유 설정 에셋은 바꾸지 않는다
        public void SetGrade(int grade) { Grade = Mathf.Max(0, grade); }
        public float GradeMultiplier => _config != null ? 1f + Grade * _config.InkRegenGradeBonus : 1f;

        // 이번 틱에 돌 수 있는가 — 막힘·작도·대기·상한. dt는 scaled(감속·정지 시 함께 느려지고 멈춘다)
        public bool CanRegenerate(float now, bool drawing, bool blocked)
            => _config != null && _pool != null && _config.InkRegenPerSecond > 0f && !drawing && !blocked
               && now - _pool.LastSpendTime >= _config.InkRegenDelay && _pool.Value < _config.InkRegenCap;

        public float Tick(float now, float deltaTime, bool drawing, bool blocked, bool inCombat)
        {
            if (deltaTime <= 0f || !CanRegenerate(now, drawing, blocked)) return 0f;
            float rate = _config.InkRegenPerSecond * GradeMultiplier * (inCombat ? 1f : _config.InkRegenOutOfCombatMultiplier);
            return _pool.Regenerate(rate * deltaTime, _config.InkRegenCap);
        }
    }
}
