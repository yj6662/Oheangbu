using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 적 생명 — 이층 구조의 「체력」 쪽(COMBAT-CHARTER: 딜은 체력을 무너뜨린다).
    // 체력과 별도인 GroggyMeter를 적마다 소유한다. 다른 적의 패링·사망은 이 게이지를 바꾸지 않는다.
    // #306: 적별 체력 프로필(EnemyVitalsProfileSO) — 없으면 설정값. HUD 체력 획·보스 바의 원천(IHealthBarSource306).
    public sealed class EnemyVitals : MonoBehaviour, IHealthBarSource306
    {
        [SerializeField] private CombatConfigSO _config;
        [Tooltip("적별 체력·이름·보스 여부 [TEST D306]. 비우면 CombatConfigSO.EnemyMaxHp")]
        [SerializeField] private EnemyVitalsProfileSO _profile;

        private float _hp;
        private GroggyMeter _groggy;
        private float _weakPointUntil = float.NegativeInfinity;
        private int _elementMask;
        private bool _completionAwarded;
        private readonly HashSet<long> _parriedAttacks = new HashSet<long>();
        private readonly HashSet<long> _directAttacks = new HashSet<long>();
        private readonly HashSet<long> _groggyAttacks = new HashSet<long>(); // #308: one groggy gain per attack identity
        public uint LifeRevision { get; private set; }
        public EnemyControlState Control { get; } = new EnemyControlState();
        // #308 WP-07: timed modifiers a spell hung on this enemy (damage taken, defence, its own attacks). Empty unless a
        // WP-07 spell hit it; cleared wherever Control is cleared (new life, disable, death).
        public EnemyModifierState308 Modifiers { get; } = new EnemyModifierState308();
        // #308 WP-07 (D308-13 Q4): share of incoming damage this enemy's armour removes, 0..1. Enemy data, default 0.
        public float Defence => _profile != null ? _profile.Defence : 0f;
        public float Hp => _hp;
        // 최대 체력의 유일 경로 — Awake·Restore·Hp01·Heal이 모두 여기를 읽는다(부활 때 60으로 돌아가지 않게)
        public float MaxHp => _profile != null && _profile.MaxHp > 0f ? _profile.MaxHp : _config != null ? _config.EnemyMaxHp : 60f;
        public EnemyVitalsProfileSO Profile => _profile;
        public string DisplayName => _profile != null ? _profile.DisplayName : "";
        public bool IsBoss => _profile != null && _profile.IsBoss;
        public bool ShowLockOnBar => _profile == null || _profile.ShowLockOnBar;
        bool IHealthBarSource306.Alive => IsAlive;
        // 표현 계층(피격 자세·소리)이 이 출처의 피해에 반응해도 되는가 — 갈무리 덩어리는 CombatConfigSO.HarvestChunkReaction(D306)
        // 뽑은 HarvestAction(Instigator)의 설정이 우선 — 적 설정 사본이 플레이어 쪽과 달라도 플래그 하나로 모인다
        public bool ReactsTo(AttackProvenance attack) => attack.Source != DamageSource.Harvest
            || (attack.Instigator is HarvestAction harvest && harvest != null ? harvest.ChunkReaction : ReactsTo(attack.Source));
        public bool ReactsTo(DamageSource source) => source != DamageSource.Harvest || _config == null || _config.HarvestChunkReaction;
        // 실행 중 프로필 교체(생성 도구·조우 등록) — 살아 있으면 채움 비율을 지킨다
        public void ConfigureProfile(EnemyVitalsProfileSO profile)
        {
            float fill = Hp01; _profile = profile;
            if (IsAlive) { _hp = Mathf.Max(Mathf.Min(1f, MaxHp), MaxHp * Mathf.Clamp01(fill)); HpChanged?.Invoke(); }
        }
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
        { LifeRevision++; Control.Clear(); Modifiers.Clear(); ResetCombatState(); _hp=MaxHp; HpChanged?.Invoke(); }
        public float Hp01 { get { float max = MaxHp; return max > 0f ? _hp / max : 0f; } }

        private void Awake()
        {
            _hp = MaxHp;
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

        // #308: the two non-parry groggy sources (the parry path stays AddParry). Harmony = the detonation of an installed
        // mark (DamageSource.Harmony), SwordCounter = a counter-element sword strike (DamageSource.PlayerDirect).
        // One gain per attack identity; steps comes from data. No other source may raise groggy.
        public bool AddGroggy(GroggySource source, AttackProvenance attack, int steps = 1)
        {
            ExpireWeakPoint(Time.time);
            if (source == GroggySource.Parry || steps <= 0 || !IsAlive || !isActiveAndEnabled || attack.AttackId <= 0 || attack.Instigator == null) return false;
            DamageSource required = source == GroggySource.Harmony ? DamageSource.Harmony : DamageSource.PlayerDirect;
            if (attack.Source != required || !_groggyAttacks.Add(attack.AttackId)) return false;
            if (_groggyAttacks.Count > 256) { _groggyAttacks.Clear(); _groggyAttacks.Add(attack.AttackId); }
            Groggy.Add(source, steps);
            return true;
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
            _directAttacks.Clear(); _parriedAttacks.Clear(); _groggyAttacks.Clear();
            _groggy?.Reset();
            if (wasOpen) WeakPointClosed?.Invoke();
            CombatStateChanged?.Invoke();
        }

        private void Update() { ExpireWeakPoint(Time.time); }
        private void OnDisable() { LifeRevision++; Control.Clear(); Modifiers.Clear(); ResetCombatState(); }
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
            // #308 WP-07 intake: this enemy's defence (data, default 0), the defence shred and damage-taken brand a spell left
            // on it, and whether this very hit ignores defence. An enemy with defence 0 and no modifier gets the value back
            // untouched, so every enemy and every spell that existed before behaves exactly as it did.
            damage = Modifiers.Intake(damage, Defence, attack.AttackId, Time.time);
            if (damage <= 0f || float.IsNaN(damage)) return default; // only a full defence (1) the hit does not ignore
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
            if (_hp <= 0f) { LifeRevision++; Control.Clear(); Modifiers.Clear(); ResetCombatState(); Died?.Invoke(); }
            return result;
        }
    }
}
