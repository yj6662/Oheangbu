using System;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Bounded growth mechanic. Head/tail/root/projectile attacks and their presentation remain separate.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyVitals))]
    public sealed class CheongryongGrowthController : MonoBehaviour
    {
        [SerializeField] private EnemyVitals _vitals;
        private EnemyVitals _subscribedVitals;
        private readonly CheongryongGrowthCycle _cycle = new CheongryongGrowthCycle();
        // Isolated editor checks replace the clock; gameplay always uses Unity's shared scaled time.
        private Func<double> _clock;
        private double Now => _clock != null ? _clock() : Time.timeAsDouble;
        public EnemyVitals Vitals => _vitals;
        public CheongryongGrowthState State => _cycle.State;
        public CheongryongGrowthState CurrentState => State;
        public bool WasInterrupted => State == CheongryongGrowthState.Interrupted;
        public bool AttemptUsed => _cycle.AttemptUsed;
        public float WindupRemaining => (float)_cycle.WindupRemaining;
        public bool IsWindingUp => State == CheongryongGrowthState.Windup;
        public bool IsPhaseTwo => State == CheongryongGrowthState.PhaseTwo;
        public event Action<CheongryongGrowthState> StateChanged;
        public event Action GrowthInterrupted;

        private void OnEnable()
        {
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_vitals == null) return;
            if (_subscribedVitals == _vitals) return;
            _subscribedVitals = _vitals;
            _vitals.HpChanged += OnHpChanged;
            _vitals.DamageResolved += OnDamageResolved;
            _vitals.Died += OnDied;
            OnHpChanged();
        }

        private void OnDisable()
        {
            if (_subscribedVitals != null)
            {
                _subscribedVitals.HpChanged -= OnHpChanged;
                _subscribedVitals.DamageResolved -= OnDamageResolved;
                _subscribedVitals.Died -= OnDied;
            }
            _subscribedVitals = null;
            End();
        }

        private void Update() { Sample(false); }

        private void OnHpChanged()
        {
            if (_vitals == null) return;
            // Dormant campaign actors can be restored before their stage becomes available.
            // Do not consume that new life revision while disabled, or activation stays Ended.
            if (!_vitals.isActiveAndEnabled) { End(); return; }
            var previous = State;
            _cycle.SynchronizeLife(Now, _vitals.LifeRevision);
            if (!_vitals.IsAlive) _cycle.End();
            Notify(previous);
            // DamageResolved, emitted after HpChanged, owns threshold and Metal ordering.
        }

        private void OnDied() { OnHpChanged(); End(); }
        private void OnDamageResolved(EnemyDamageResult result)
        {
            if (result.Target != _vitals || result.AppliedDamage <= 0f) return;
            bool metal = result.Attack.Element == Element.Metal && result.Attack.AttackId > 0 &&
                result.Attack.Instigator != null &&
                (result.Attack.Source == DamageSource.PlayerDirect || result.Attack.Source == DamageSource.Summon);
            Sample(metal);
        }

        private void Sample(bool confirmedMetalHit)
        {
            if (!isActiveAndEnabled || _vitals == null) return;
            var previous = State;
            if (!_vitals.isActiveAndEnabled) { End(); return; }
            bool heal = _cycle.Sample(Now, _vitals.Hp, _vitals.MaxHp, _vitals.LifeRevision, confirmedMetalHit);
            if (heal) _vitals.Heal(_vitals.MaxHp);
            Notify(previous);
        }

        private void End() { var previous = State; _cycle.End(); Notify(previous); }
        private void Notify(CheongryongGrowthState previous)
        {
            if (State == previous) return;
            bool interrupted = State == CheongryongGrowthState.Interrupted;
            StateChanged?.Invoke(State);
            if (interrupted) GrowthInterrupted?.Invoke();
        }
    }
}
