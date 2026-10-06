using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-DEPLOY-308 section 8 (D308-10, photosensitivity): how often the whole screen may flip its values.
    // One impact = exactly one flip (the frames never darken twice). Three rules, all must pass:
    //   1. sliding window: grants inside any 1 s window stay below floor(perSecond), and that is clamped to 3 in code
    //   2. minimum interval between two grants
    //   3. token bucket (capacity, refill per second) for bursts
    // Pure: the caller supplies the clock (unscaled seconds), so the unit check drives it with a fake one.
    // The big-target hit flicker and the screen ink flood draw from the same limiter.
    public sealed class ImpactLimiter308
    {
        public const float HardPerSecond = 3f;
        private readonly double[] _grants = new double[3];   // the last grants, newest first
        private int _capacity = 2, _window = 2;
        private float _perSecond = 2f, _minInterval = .4f, _tokens = 2f;
        private double _last = double.NegativeInfinity, _refilled = double.NegativeInfinity;

        public int Granted { get; private set; }
        public int Denied { get; private set; }
        public float Tokens => _tokens;
        public int WindowLimit => _window;

        public ImpactLimiter308() { Reset(); }

        public void Configure(int capacity, float perSecond, float minInterval)
        {
            _capacity = Mathf.Clamp(capacity, 1, 3);
            _perSecond = Mathf.Clamp(perSecond, 0f, HardPerSecond);
            _window = Mathf.Clamp(Mathf.FloorToInt(_perSecond + .0001f), 0, (int)HardPerSecond);
            _minInterval = Mathf.Max(0f, minInterval);
            _tokens = Mathf.Min(_tokens, _capacity);
        }

        public void Reset()
        {
            for (int i = 0; i < _grants.Length; i++) _grants[i] = double.NegativeInfinity;
            _last = _refilled = double.NegativeInfinity; _tokens = _capacity; Granted = Denied = 0;
        }

        public bool CanTake(double now) => Check(now, false);
        public bool TryTake(double now) => Check(now, true);

        private bool Check(double now, bool take)
        {
            if (!double.IsNegativeInfinity(_refilled)) _tokens = Mathf.Min(_capacity, _tokens + (float)((now - _refilled) * _perSecond));
            _refilled = now;
            int recent = 0;
            for (int i = 0; i < _grants.Length; i++) if (now - _grants[i] < 1.0) recent++;
            bool ok = _window > 0 && recent < _window && now - _last >= _minInterval && _tokens >= 1f;
            if (!take) return ok;
            if (!ok) { Denied++; return false; }
            _tokens -= 1f; _last = now;
            _grants[2] = _grants[1]; _grants[1] = _grants[0]; _grants[0] = now;
            Granted++;
            return true;
        }
    }
}
