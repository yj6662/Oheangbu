using System;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyVitals))]
    public sealed class SouthGateGeneralController : MonoBehaviour
    {
        [SerializeField] SouthGateGeneralProfile _profile;
        [SerializeField] CombatConfigSO _config;
        [SerializeField] EnemyVitals _vitals;
        [SerializeField] Transform _weapon;
        Transform _player;
        PlayerVitals _playerVitals, _subscribedPlayer;
        PlayerMotor _playerMotor;
        EnemyVitals _subscribedVitals;
        ParryJudge _judge;
        RaycastHit[] _coverHits;
        bool _attackEnabled = true, _homeInitialized, _profileValid;
        Vector3 _home;
        uint _lifeRevision;
        int _nextPattern;
        float _readyAt;
        Func<float> _clock; // Isolated checks supply a shared scaled clock; normal gameplay uses Time.time.
        float Now => _clock != null ? _clock() : Time.time;
        bool InputPaused => Time.timeScale <= .0001f || (_playerMotor != null && _playerMotor.RuntimeState != null && _playerMotor.RuntimeState.InputBlocked);
        public SouthGateGeneralProfile Profile => _profile;
        public EnemyVitals Vitals => _vitals;
        public SouthGateGeneralAttackPlan CurrentPlan { get; private set; }
        public SouthGateCombatState State { get; private set; } = SouthGateCombatState.Idle;
        public bool AttackInProgress => CurrentPlan != null && !CurrentPlan.IsCancelled;
        public bool CounterWindowActive => State == SouthGateCombatState.CounterOpportunity && Now < _readyAt;
        public bool CanNavigate => isActiveAndEnabled && _attackEnabled && _vitals != null && _vitals.isActiveAndEnabled && _vitals.IsAlive &&
            !_vitals.WeakPointActive && (State == SouthGateCombatState.Idle || State == SouthGateCombatState.Recovery);
        public bool AttackEnabled
        { get => _attackEnabled; set { if (_attackEnabled == value) return; _attackEnabled = value; if (!value) StopAttack(); } }
        public event Action<SouthGateGeneralAttackPlan> AttackStarted;
        public event Action<SouthGateGeneralImpact> AttackImpactResolved;
        public event Action<SouthGateGeneralAttackPlan, bool> AttackEnded;
        public event Action<SouthGateGeneralAttackPlan> EmpowerInterrupted;
        public event Action<SouthGateCombatState> StateChanged;
        void Awake() { Bind(); ValidateProfile(); ResetEncounter(); }
        void OnEnable() { Bind(); ValidateProfile(); ResetEncounter(); }
        void OnDisable() { CancelPlan(); Unbind(); SetState(_vitals != null && !_vitals.IsAlive ? SouthGateCombatState.Dead : SouthGateCombatState.Idle); }
        void OnDestroy() { Unbind(); }
        public void ConfigureProfile(SouthGateGeneralProfile profile, CombatConfigSO config = null)
        {
            if (profile != null && !profile.TryValidate(out var error)) throw new ArgumentException(error, nameof(profile));
            Bind(); StopAttack(); _profile = profile; _config = config; ValidateProfile(); ResetEncounter();
        }
        public void Configure(Transform player, PlayerVitals playerVitals, ParryJudge judge)
        { StopAttack(); _player = player; _playerVitals = playerVitals; _playerMotor = playerVitals != null ? playerVitals.GetComponent<PlayerMotor>() : null; _judge = judge; Bind(); SynchronizeLife(); }
        public void ConfigureWeapon(Transform weapon) { _weapon = weapon; }
        public void ResetEncounter()
        {
            CancelPlan(); if (!_homeInitialized) { _home = transform.position; _homeInitialized = true; }
            _nextPattern = 0; if (_vitals != null) _lifeRevision = _vitals.LifeRevision;
            _readyAt = Now + (_profile != null ? _profile.InitialDelay : 1);
            SetState(_vitals != null && !_vitals.IsAlive ? SouthGateCombatState.Dead : SouthGateCombatState.Idle);
        }
        public void StopAttack()
        {
            CancelPlan(); _readyAt = Now + (_profile != null ? _profile.CancelRecovery : .65f);
            if (State != SouthGateCombatState.Dead && State != SouthGateCombatState.Stunned) SetState(SouthGateCombatState.Recovery);
        }
        void Bind()
        {
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_subscribedVitals != _vitals)
            {
                if (_subscribedVitals != null) { _subscribedVitals.Died -= OnDied; _subscribedVitals.WeakPointOpened -= OnWeakPoint; _subscribedVitals.DamageResolved -= OnDamage; }
                _subscribedVitals = _vitals;
                if (_subscribedVitals != null) { _subscribedVitals.Died += OnDied; _subscribedVitals.WeakPointOpened += OnWeakPoint; _subscribedVitals.DamageResolved += OnDamage; }
            }
            if (_subscribedPlayer != _playerVitals)
            {
                if (_subscribedPlayer != null) _subscribedPlayer.Died -= OnPlayerDied;
                _subscribedPlayer = _playerVitals;
                if (_subscribedPlayer != null) _subscribedPlayer.Died += OnPlayerDied;
            }
        }
        void Unbind()
        {
            if (_subscribedVitals != null) { _subscribedVitals.Died -= OnDied; _subscribedVitals.WeakPointOpened -= OnWeakPoint; _subscribedVitals.DamageResolved -= OnDamage; }
            if (_subscribedPlayer != null) _subscribedPlayer.Died -= OnPlayerDied;
            _subscribedVitals = null; _subscribedPlayer = null;
        }
        void SynchronizeLife() { if (_vitals != null && _lifeRevision != _vitals.LifeRevision) ResetEncounter(); }
        void ValidateProfile() { _profileValid = _profile != null && _profile.TryValidate(out _); }
        void OnDied() { CancelPlan(); SetState(SouthGateCombatState.Dead); }
        void OnPlayerDied() { StopAttack(); }
        void OnWeakPoint() { CancelPlan(); SetState(SouthGateCombatState.Stunned); }
        void OnDamage(EnemyDamageResult result)
        {
            var plan = CurrentPlan;
            if (!isActiveAndEnabled || !_attackEnabled || _vitals == null || !_vitals.isActiveAndEnabled || !_vitals.IsAlive ||
                result.Target != _vitals || result.Killed || result.AppliedDamage <= 0 || result.Attack.Element != Element.Wood ||
                result.Attack.AttackId <= 0 || result.Attack.Instigator == null ||
                (result.Attack.Source != DamageSource.PlayerDirect && result.Attack.Source != DamageSource.Summon) ||
                plan == null || _lifeRevision != _vitals.LifeRevision || !plan.InterruptEmpower(Now)) return;
            // Cancel only the unexecuted Earth follow-up. Do not change prior neutral contact, global groggy or damage multipliers.
            CurrentPlan = null; _readyAt = Now + plan.CounterSeconds; SetState(SouthGateCombatState.CounterOpportunity);
            EmpowerInterrupted?.Invoke(plan); AttackEnded?.Invoke(plan, true);
        }
        bool Eligible()
        {
            return isActiveAndEnabled && _profileValid && _profile != null && _attackEnabled &&
                _vitals != null && _vitals.isActiveAndEnabled && _vitals.IsAlive && !_vitals.WeakPointActive &&
                _player != null && _player.gameObject.activeInHierarchy && _playerVitals != null && _playerVitals.isActiveAndEnabled && _playerVitals.Hp01 > 0 &&
                (transform.position - _home).sqrMagnitude <= _profile.LeashRange * _profile.LeashRange &&
                (_player.position - _home).sqrMagnitude <= _profile.LeashRange * _profile.LeashRange &&
                (_player.position - transform.position).sqrMagnitude <= _profile.LeashRange * _profile.LeashRange;
        }
        void Update()
        {
            if (!isActiveAndEnabled || InputPaused) return;
            SynchronizeLife();
            if (_vitals == null || !_vitals.IsAlive) { OnDied(); return; }
            if (_vitals.WeakPointActive) { OnWeakPoint(); return; }
            if (State == SouthGateCombatState.Stunned) { _readyAt = Now + (_profile != null ? _profile.CancelRecovery : .65f); SetState(SouthGateCombatState.Recovery); }
            if (!Eligible()) { if (CurrentPlan != null) StopAttack(); return; }
            if (CurrentPlan != null) { TickAttack(Now); return; }
            if (Now < _readyAt) return;
            SetState(SouthGateCombatState.Idle);
            for (int i = 0; i < 4; i++)
            { var kind = (SouthGateAttackKind)_nextPattern; _nextPattern = (_nextPattern + 1) % 4; if (TryBeginAttack(kind)) return; }
            _readyAt = Now + _profile.CancelRecovery;
        }
        public bool TryBeginAttack(SouthGateAttackKind kind)
        {
            if(InputPaused)return false;
            SynchronizeLife(); if (!_homeInitialized) ResetEncounter();
            if (!Eligible() || CurrentPlan != null || Now < _readyAt || kind < SouthGateAttackKind.Thrust || kind > SouthGateAttackKind.NeutralEarthCombo ||
                (_player.position - transform.position).sqrMagnitude > _profile.EngageRange * _profile.EngageRange) return false;
            Vector3 origin = _weapon != null ? _weapon.position : transform.TransformPoint(_profile.WeaponOffset);
            Vector3 direction = Vector3.ProjectOnPlane(_player.position - origin, Vector3.up);
            if (direction.sqrMagnitude < .0001f) direction = transform.forward;
            if (Obstructed(origin, _player.position + Vector3.up * .8f)) return false;
            var plan = new SouthGateGeneralAttackPlan(_profile, kind, _vitals, origin, direction, Now);
            if (kind != SouthGateAttackKind.EarthShockwave && !plan.Contains(plan.Pulses[0], _player.position)) return false;
            CurrentPlan = plan; SetState(SouthGateCombatState.Windup); AttackStarted?.Invoke(plan); return CurrentPlan == plan;
        }
        void TickAttack(float now)
        {
            if(InputPaused)return;
            var plan = CurrentPlan; if (plan == null || !Eligible()) { StopAttack(); return; }
            plan.AdvanceTo(now);
            if (now > plan.EndAt) { Finish(plan, now); return; }
            SetState(plan.InEarthPreparation(now) ? SouthGateCombatState.EarthPreparation :
                now < plan.Pulses[0].ReleaseAt ? SouthGateCombatState.Windup : SouthGateCombatState.Active);
            foreach (var pulse in plan.Pulses)
            {
                if (pulse.Consumed || pulse.Cancelled || now < pulse.ReleaseAt) continue;
                if (now > pulse.ActiveEndAt) { Resolve(plan, pulse, false, now); if (CurrentPlan != plan) return; continue; }
                bool contact = plan.SweptContains(pulse, _player.position);
                if (contact) Resolve(plan, pulse, !Obstructed(plan.Origin, _player.position + Vector3.up * .6f), now);
                else if (pulse.Shape == SouthGatePulseShape.Thrust || now >= pulse.ActiveEndAt) Resolve(plan, pulse, false, now);
                if (CurrentPlan != plan) return;
            }
            if (now >= plan.EndAt) Finish(plan, now);
        }
        void Resolve(SouthGateGeneralAttackPlan plan, SouthGateAttackPulse pulse, bool contact, float now)
        {
            if(InputPaused)return;
            if (CurrentPlan != plan || pulse.Consumed || pulse.Cancelled || !Eligible()) return;
            pulse.Consume(); Vector3 point = _player.position;
            if (!contact) { AttackImpactResolved?.Invoke(new SouthGateGeneralImpact(plan, pulse, point, ParryOutcome.None, 0, false)); return; }
            Vector3 parryPoint = point + Vector3.up * 1.1f - plan.Direction;
            ParryOutcome outcome = pulse.Attack.Element.HasValue && _judge != null
                ? _judge.ResolveImpact(pulse.Attack.Element.Value, now, parryPoint, pulse.Attack) : ParryOutcome.None;
            float damage = pulse.Damage;
            if (outcome == ParryOutcome.Success) damage = 0;
            else if (outcome == ParryOutcome.Half) damage *= _config != null ? _config.HalfParryDamageFactor : .4f;
            else if (outcome == ParryOutcome.Block) damage *= _config != null ? _config.GuardBlockFactor : .5f;
            float before = _playerVitals.Hp01 * _playerVitals.MaxHp;
            if (damage > 0 && CurrentPlan == plan && Eligible()) _playerVitals.TakeAttackDamage(damage,
                pulse.Shape == SouthGatePulseShape.Wave ? (pulse.Attack.Element.HasValue ? IncomingDamageKind.ElementalRanged : IncomingDamageKind.Ranged) : (pulse.Attack.Element.HasValue ? IncomingDamageKind.ElementalMelee : IncomingDamageKind.Melee));
            float applied = Mathf.Max(0, before - _playerVitals.Hp01 * _playerVitals.MaxHp);
            // Existing OwnedImpactResolved owns all guard rewards/groggy. This component never adds a second reward.
            AttackImpactResolved?.Invoke(new SouthGateGeneralImpact(plan, pulse, point, outcome, applied, true));
        }
        void Finish(SouthGateGeneralAttackPlan plan, float now)
        {
            if (CurrentPlan != plan) return;
            foreach (var pulse in plan.Pulses) { if (!pulse.Cancelled && !pulse.Consumed) Resolve(plan, pulse, false, now); if (CurrentPlan != plan) return; }
            CurrentPlan = null; _readyAt = now + _profile.Cooldown; SetState(SouthGateCombatState.Recovery); AttackEnded?.Invoke(plan, false);
        }
        void CancelPlan()
        { var old = CurrentPlan; CurrentPlan = null; if (old == null) return; old.Cancel(); AttackEnded?.Invoke(old, true); }
        void SetState(SouthGateCombatState state) { if (State == state) return; State = state; StateChanged?.Invoke(state); }
        bool Obstructed(Vector3 origin, Vector3 end)
        {
            Vector3 delta = end - origin;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, origin, delta, delta.magnitude, _profile.EnvironmentMask, ref _coverHits);
            for (int i = 0; i < count; i++)
            {
                var t = _coverHits[i].transform;
                if (t == null || t.IsChildOf(transform) || (_player != null && t.IsChildOf(_player)) ||
                    t.GetComponentInParent<EnemyVitals>() != null || t.GetComponentInParent<PlayerVitals>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
