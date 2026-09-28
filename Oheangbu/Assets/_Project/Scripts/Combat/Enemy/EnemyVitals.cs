using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 적 생명 — 이층 구조의 「체력」 쪽(COMBAT-CHARTER: 딜은 체력을 무너뜨린다).
    // 체력과 별도인 GroggyMeter를 적마다 소유한다. 다른 적의 패링·사망은 이 게이지를 바꾸지 않는다.
    public sealed class EnemyVitals : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;

        private float _hp;
        private GroggyMeter _groggy;
        private float _weakPointUntil = float.NegativeInfinity;
        private int _elementMask;
        private bool _completionAwarded;
        private readonly HashSet<long> _parriedAttacks = new HashSet<long>();
        private readonly HashSet<long> _directAttacks = new HashSet<long>();
        public uint LifeRevision { get; private set; }
        public EnemyControlState Control { get; } = new EnemyControlState();
        public float Hp => _hp;
        public float MaxHp => _config != null ? _config.EnemyMaxHp : 60f;
        public GroggyMeter Groggy { get { EnsureGroggy(); return _groggy; } }
        public bool WeakPointActive => IsAlive && Time.time < _weakPointUntil;
        public int WeakPointElementMask => _elementMask;
        public bool CompletionAwarded => _completionAwarded;

        public event Action HpChanged;
        public event Action Died;
        public event Action CombatStateChanged;
        public event Action WeakPointOpened;
        public event Action WeakPointClosed;
        public event Action<EnemyDamageResult> DamageResolved;

        // 만개(급소창) 동안 이 적의 전 공격 피해 증폭. Setter는 기존 검수 도구 호환용이다.
        public float DamageMultiplier { get; set; } = 1f;

        public bool IsAlive => _hp > 0f;
        // In-encounter healing: never resurrect or reset pending-hit ownership, groggy or weak points.
        public float Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return 0f;
            float restored = Mathf.Max(0f, Mathf.Min(amount, MaxHp - _hp));
            if (restored <= 0f) return 0f;
            _hp += restored;
            HpChanged?.Invoke();
            return restored;
        }
        public void Restore()
        { LifeRevision++; Control.Clear(); ResetCombatState(); _hp=_config!=null?_config.EnemyMaxHp:60f; HpChanged?.Invoke(); }
        public float Hp01 => _config != null && _config.EnemyMaxHp > 0f ? _hp / _config.EnemyMaxHp : 0f;

        private void Awake()
        {
            _hp = _config != null ? _config.EnemyMaxHp : 60f;
        }

        private void EnsureGroggy()
        {
            if (_groggy != null) return;
            _groggy = new GroggyMeter(_config);
            _groggy.Blossomed += OpenWeakPoint;
            _groggy.Changed += () => CombatStateChanged?.Invoke();
        }

        public void AddParry(AttackProvenance attack)
        {
            ExpireWeakPoint(Time.time);
            if (!IsAlive || !isActiveAndEnabled || attack.Source != DamageSource.Enemy ||
                attack.Instigator != this || attack.AttackId <= 0 || !_parriedAttacks.Add(attack.AttackId)) return;
            // Only the recent resolved attacks need retention; the judge also rejects duplicate events.
            if (_parriedAttacks.Count > 256) { _parriedAttacks.Clear(); _parriedAttacks.Add(attack.AttackId); }
            Groggy.AddFromParry();
        }

        public void OpenWeakPoint()
        {
            if (!IsAlive || WeakPointActive) return;
            _weakPointUntil = Time.time + (_config != null ? _config.BlossomStunDuration : 4f);
            DamageMultiplier = _config != null ? _config.BlossomDamageMultiplier : 1.5f;
            _elementMask = 0; _completionAwarded = false; _directAttacks.Clear();
            WeakPointOpened?.Invoke();
            CombatStateChanged?.Invoke();
        }

        public void ResetCombatState()
        {
            bool wasOpen = !float.IsNegativeInfinity(_weakPointUntil);
            _weakPointUntil = float.NegativeInfinity;
            DamageMultiplier = 1f; _elementMask = 0; _completionAwarded = false;
            _directAttacks.Clear(); _parriedAttacks.Clear();
            _groggy?.Reset();
            if (wasOpen) WeakPointClosed?.Invoke();
            CombatStateChanged?.Invoke();
        }

        private void Update() { ExpireWeakPoint(Time.time); }
        private void OnDisable() { LifeRevision++; Control.Clear(); ResetCombatState(); }
        public void ExpireWeakPoint(float now)
        {
            if (!float.IsNegativeInfinity(_weakPointUntil) && now >= _weakPointUntil) ResetCombatState();
        }

        // floorAtOneHp: 갈무리 전용 — 단독 처치 성립 불가(COMBAT-HARVEST)
        public void TakeDamage(float amount, bool floorAtOneHp = false)
        {
            TakeDamage(amount, new AttackProvenance(0, null,
                floorAtOneHp ? DamageSource.Harvest : DamageSource.Unknown, null), floorAtOneHp);
        }

        public EnemyDamageResult TakeDamage(float amount, AttackProvenance attack, bool floorAtOneHp = false)
        {
            ExpireWeakPoint(Time.time);
            if (!IsAlive || amount <= 0f || float.IsNaN(amount)) return default;
            float damage = amount * DamageMultiplier;
            if (damage <= 0f || float.IsNaN(damage)) return default;
            float bonus = 0f;
            if (WeakPointActive && !floorAtOneHp && attack.Source == DamageSource.PlayerDirect &&
                attack.Instigator != null && attack.AttackId > 0 && attack.Element.HasValue &&
                !_completionAwarded && _directAttacks.Add(attack.AttackId))
            {
                int element = (int)attack.Element.Value;
                if (element >= 0 && element < 5) _elementMask |= 1 << element;
                if (_elementMask == 31)
                {
                    _completionAwarded = true;
                    bonus = _config != null ? _config.FiveElementCompletionDamage : 30f;
                }
                CombatStateChanged?.Invoke();
            }
            float floor = floorAtOneHp ? 1f : 0f;
            float before = _hp;
            _hp = Mathf.Max(Mathf.Min(floor, _hp), _hp - damage - bonus);
            float appliedBonus = Mathf.Min(bonus, Mathf.Max(0f, before - damage));
            var result = new EnemyDamageResult(this, attack, before - _hp, appliedBonus, _hp <= 0f);
            HpChanged?.Invoke();
            DamageResolved?.Invoke(result);
            if (_hp <= 0f) { LifeRevision++; Control.Clear(); ResetCombatState(); Died?.Invoke(); }
            return result;
        }
    }
}
