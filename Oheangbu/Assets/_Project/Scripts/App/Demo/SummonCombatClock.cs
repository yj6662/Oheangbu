using System;

namespace Oheangbu.App.Demo
{
    public enum SummonPhase { Forming, Following, Approaching, Attacking, Returning, Dissolving, Ended }

    // No Unity time, global state, AI, damage or presentation side effects.
    // The manager advances this clock with scaled delta time and applies a hit only when consumption succeeds.
    public sealed class SummonCombatClock
    {
        private readonly double _formation, _activity, _dissolve;
        private double _elapsed, _phaseStarted, _dissolveStarted = double.PositiveInfinity, _readyAt;
        private double _hitAt, _attackCooldown;
        private long _lastAcceptedAttack, _pendingAttack, _target;
        private uint _targetRevision;
        public SummonPhase Phase { get; private set; }
        public float Elapsed => (float)_elapsed;
        public float PhaseElapsed => (float)Math.Max(0, _elapsed - _phaseStarted);
        public float ActivityElapsed => (float)Math.Max(0, Math.Min(_activity, Math.Min(_elapsed, _dissolveStarted) - _formation));
        public float ActivityRemaining => Phase == SummonPhase.Forming ? (float)_activity : IsCombatActive ? (float)Math.Max(0, _formation + _activity - _elapsed) : 0;
        public float FormationProgress01 => _formation <= 0 ? 1 : Clamp01(_elapsed / _formation);
        public float DissolveProgress01 => Phase == SummonPhase.Ended ? 1 : Phase != SummonPhase.Dissolving ? 0 : _dissolve <= 0 ? 1 : Clamp01((_elapsed - _dissolveStarted) / _dissolve);
        public bool IsCombatActive => Phase == SummonPhase.Following || Phase == SummonPhase.Approaching || Phase == SummonPhase.Attacking || Phase == SummonPhase.Returning;
        public bool CanAttack => IsCombatActive && _pendingAttack == 0 && _elapsed >= _readyAt;
        public long PendingAttackId => _pendingAttack;
        public long LastAcceptedAttackId => _lastAcceptedAttack;
        public event Action<SummonPhase> PhaseChanged;

        public SummonCombatClock(float formationSeconds, float activitySeconds, float dissolveSeconds)
        {
            RequireFiniteNonNegative(formationSeconds, nameof(formationSeconds));
            RequireFiniteNonNegative(dissolveSeconds, nameof(dissolveSeconds));
            RequireFiniteNonNegative(activitySeconds, nameof(activitySeconds));
            if (activitySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(activitySeconds));
            _formation = formationSeconds; _activity = activitySeconds; _dissolve = dissolveSeconds;
            Phase = formationSeconds > 0 ? SummonPhase.Forming : SummonPhase.Following;
        }

        public void Advance(float scaledDeltaTime)
        {
            RequireFiniteNonNegative(scaledDeltaTime, nameof(scaledDeltaTime));
            if (scaledDeltaTime == 0 || Phase == SummonPhase.Ended) return;
            _elapsed += scaledDeltaTime;
            if (Phase == SummonPhase.Forming && _elapsed >= _formation) Transition(SummonPhase.Following, _formation);
            if (IsCombatActive && _elapsed >= _formation + _activity) StartDissolveAt(_formation + _activity);
            if (Phase == SummonPhase.Dissolving && _elapsed >= _dissolveStarted + _dissolve)
            {
                _elapsed = _dissolveStarted + _dissolve;
                Transition(SummonPhase.Ended, _elapsed);
            }
        }

        // Pending attacks must be explicitly cancelled before changing navigation state.
        public bool SetActivityPhase(SummonPhase phase)
        {
            if (!IsCombatActive || _pendingAttack != 0 ||
                (phase != SummonPhase.Following && phase != SummonPhase.Approaching && phase != SummonPhase.Returning)) return false;
            Transition(phase, _elapsed); return true;
        }

        // IDs must be positive and monotonically increasing within this summon lifetime.
        // The manager may use the existing global combat provenance IDs; this class creates none.
        public bool TryBeginAttack(long attackId, long targetId, uint targetRevision, float windupSeconds, float cooldownSeconds)
        {
            RequireFiniteNonNegative(windupSeconds, nameof(windupSeconds));
            RequireFiniteNonNegative(cooldownSeconds, nameof(cooldownSeconds));
            if (!CanAttack || attackId <= _lastAcceptedAttack || attackId <= 0 || targetId == 0 ||
                _elapsed + windupSeconds >= _formation + _activity) return false;
            _lastAcceptedAttack = attackId; _pendingAttack = attackId; _target = targetId; _targetRevision = targetRevision;
            _hitAt = _elapsed + windupSeconds; _attackCooldown = cooldownSeconds;
            Transition(SummonPhase.Attacking, _elapsed); return true;
        }

        // Validation occurs at the strike, not just during targeting. A failed due strike consumes the
        // pending attack so a revived/re-entering target cannot receive a delayed ghost hit.
        public bool TryConsumeHit(long attackId, long targetId, uint targetRevision, bool targetAlive, bool inRange, bool lineOfSight)
        {
            if (!IsCombatActive || _pendingAttack == 0 || attackId != _pendingAttack || _elapsed < _hitAt) return false;
            bool hit = _target == targetId && _targetRevision == targetRevision && targetAlive && inRange && lineOfSight;
            _pendingAttack = 0; _target = 0; _readyAt = _elapsed + _attackCooldown;
            Transition(SummonPhase.Following, _elapsed); return hit;
        }

        public void CancelAttack()
        {
            if (_pendingAttack == 0) return;
            _pendingAttack = 0; _target = 0; _readyAt = _elapsed + _attackCooldown;
            if (IsCombatActive) Transition(SummonPhase.Following, _elapsed);
        }

        public void BeginDissolve()
        {
            if (Phase == SummonPhase.Ended || Phase == SummonPhase.Dissolving) return;
            StartDissolveAt(_elapsed);
            if (_dissolve == 0) Transition(SummonPhase.Ended, _elapsed);
        }

        public void EndImmediately()
        {
            if (Phase == SummonPhase.Ended) return;
            _pendingAttack = 0; _target = 0; _dissolveStarted = Math.Min(_elapsed, _dissolveStarted);
            Transition(SummonPhase.Ended, _elapsed);
        }

        private void StartDissolveAt(double at)
        {
            _pendingAttack = 0; _target = 0; _dissolveStarted = at;
            Transition(SummonPhase.Dissolving, at);
        }
        private void Transition(SummonPhase phase, double at)
        {
            if (Phase == phase) return;
            Phase = phase; _phaseStarted = at; PhaseChanged?.Invoke(phase);
        }
        private static float Clamp01(double value) => (float)Math.Max(0, Math.Min(1, value));
        private static void RequireFiniteNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name);
        }
    }

    // Transaction boundary for the manager: it checks spawn placement and commits ink before replacing.
    // Failed requests neither dismiss the existing summon nor advance its clock.
    public sealed class SummonCombatSlot
    {
        public SummonCombatClock Current { get; private set; }
        public bool TryReplace(SummonCombatClock candidate, bool placementValid, bool costCommitted)
        {
            if (!placementValid || !costCommitted || candidate == null || ReferenceEquals(Current, candidate) ||
                candidate.Phase == SummonPhase.Ended || candidate.Phase == SummonPhase.Dissolving) return false;
            Current?.EndImmediately(); Current = candidate; return true;
        }
        public void Advance(float scaledDeltaTime)
        {
            if (float.IsNaN(scaledDeltaTime) || float.IsInfinity(scaledDeltaTime) || scaledDeltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(scaledDeltaTime));
            Current?.Advance(scaledDeltaTime);
            if (Current != null && Current.Phase == SummonPhase.Ended) Current = null;
        }
        public void Clear()
        { Current?.EndImmediately(); Current = null; }
    }
}
