using System;

namespace Oheangbu.App.Demo
{
    public enum CheongryongGrowthState { Ready, Windup, Interrupted, PhaseTwo, Ended }

    // Encounter-local only; campaign saves retain boss defeat, not this temporary windup.
    // The adapter supplies a single scaled clock for both Update and resolved damage.
    public sealed class CheongryongGrowthCycle
    {
        public const double WindupSeconds = 4d;
        private bool _initialized;
        private uint _lifeRevision;
        private double _sampleTime, _deadline;
        public CheongryongGrowthState State { get; private set; }
        public bool AttemptUsed { get; private set; }
        public double WindupRemaining => State == CheongryongGrowthState.Windup ? Math.Max(0d, _deadline - _sampleTime) : 0d;

        // Returns true exactly once when a surviving windup reaches its deadline and needs full healing.
        // A hit at the deadline is too late; a threshold-crossing hit starts, rather than cancels, windup.
        public bool Sample(double scaledTime, float hp, float maxHp, uint lifeRevision, bool confirmedMetalHit = false)
        {
            SynchronizeLife(scaledTime, lifeRevision);
            if (hp <= 0f) { End(); return false; }
            if (State == CheongryongGrowthState.Ended) return false;
            if (State == CheongryongGrowthState.Windup)
            {
                if (scaledTime >= _deadline) { State = CheongryongGrowthState.PhaseTwo; return true; }
                if (confirmedMetalHit) State = CheongryongGrowthState.Interrupted;
            }
            else if (State == CheongryongGrowthState.Ready && Finite(hp) && Finite(maxHp) && maxHp > 0f && hp <= maxHp * .5f)
            {
                AttemptUsed = true;
                _deadline = scaledTime + WindupSeconds;
                State = CheongryongGrowthState.Windup;
            }
            return false;
        }

        public void SynchronizeLife(double scaledTime, uint lifeRevision)
        {
            if (double.IsNaN(scaledTime) || double.IsInfinity(scaledTime) || scaledTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(scaledTime));
            if (!_initialized || lifeRevision != _lifeRevision)
            {
                _initialized = true; _lifeRevision = lifeRevision;
                State = CheongryongGrowthState.Ready; AttemptUsed = false; _deadline = 0d;
            }
            else if (scaledTime < _sampleTime)
                throw new ArgumentOutOfRangeException(nameof(scaledTime), "Growth time cannot rewind within an encounter.");
            _sampleTime = scaledTime;
        }

        // Death/disable cannot leave a pending heal; only a new LifeRevision re-arms the attempt.
        public void End() { State = CheongryongGrowthState.Ended; _deadline = 0d; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
