using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
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
        // #308 WP-16 (D308-114): flat radius of this enemy's body, for the zone glyphs that count a body edge in the circle
        // (zone.blind, zone.haze). The first solid CharacterController, then capsule, then sphere on this object or below,
        // times the larger flat scale of its transform; 0.4 m when there is none. Measured on first read, again after a disable.
        public float BodyRadius { get { if (_bodyRadius < 0f) _bodyRadius = MeasureBodyRadius(); return _bodyRadius; } }
        private float _bodyRadius = -1f;
        private const float DefaultBodyRadius = .4f;
        private float MeasureBodyRadius()
        {
            foreach (var body in GetComponentsInChildren<CharacterController>()) if (body.enabled) return Flat(body.radius, body.transform);
            foreach (var body in GetComponentsInChildren<CapsuleCollider>()) if (body.enabled && !body.isTrigger) return Flat(body.radius, body.transform);
            foreach (var body in GetComponentsInChildren<SphereCollider>()) if (body.enabled && !body.isTrigger) return Flat(body.radius, body.transform);
            return DefaultBodyRadius;
        }
        private static float Flat(float radius, Transform t)
        {
            float r = radius * Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.z));
            return r > 0f && !float.IsNaN(r) && !float.IsInfinity(r) ? r : DefaultBodyRadius;
        }
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
        // D308-147 (riposte window key): counts the windows this enemy has opened (OpenWeakPoint). With LifeRevision it names one
        // window: the same enemy, the same life, the same opening. A window that closed and opened again is a new generation.
        public uint WeakPointGeneration { get; private set; }
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
        // #308 WP-17 (눅, D308-121): an enemy standing in a sealing domain does not heal at all.
        public float Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return 0f;
            if (Modifiers.Domain.Sealed(Time.time)) return 0f;
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

        public void OpenWeakPoint() => OpenWeakPointCore(_config != null ? _config.BlossomStunDuration : 4f);

        // S5-b slice 2 (D308-149): the vol.2 posture bridge opens the window for the length the rules computed (2 s x 0.6^(n-1), a bundle
        // of at most 5 s). Same state, same generation, same events as the legacy bloom: the riposte's TryConsumeWeakPointWindow and the
        // HUD read it unchanged. false = the window was not opened (dead, already open, or a non-positive / non-finite length).
        public bool OpenWeakPointFor(float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds)) return false;
            return OpenWeakPointCore(seconds);
        }

        private bool OpenWeakPointCore(float seconds)
        {
            if (!IsAlive || WeakPointActive) return false;
            WeakPointGeneration++;
            _weakPointUntil = Time.time + seconds;
            DamageMultiplier = _config != null ? _config.BlossomDamageMultiplier : 1.5f;
            _elementMask = 0; _completionAwarded = false; _directAttacks.Clear();
            WeakPointOpened?.Invoke();
            CombatStateChanged?.Invoke();
            return true;
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

        // D308-147: the end of one window, by key. Closes the window and its groggy only if that very window (this life, this
        // opening) is still the open one; a late caller whose window has gone - or has been replaced by the next one - changes
        // nothing. Narrower than ResetCombatState: the attack-identity records (_parriedAttacks, _groggyAttacks) belong to no
        // window and stay. Nothing else is kept on this object (there is no repeat counter or extreme-hit record in EnemyVitals).
        public bool TryConsumeWeakPointWindow(uint life, uint generation)
        {
            if (!IsAlive || LifeRevision != life || WeakPointGeneration != generation || float.IsNegativeInfinity(_weakPointUntil)) return false;
            _weakPointUntil = float.NegativeInfinity;
            DamageMultiplier = 1f; _elementMask = 0; _completionAwarded = false;
            _directAttacks.Clear();
            _groggy?.Reset();
            WeakPointClosed?.Invoke();
            CombatStateChanged?.Invoke();
            return true;
        }

        private void Update() { ExpireWeakPoint(Time.time); }
        private void OnDisable() { LifeRevision++; Control.Clear(); Modifiers.Clear(); ResetCombatState(); _bodyRadius = -1f; }
        public void ExpireWeakPoint(float now)
        {
            if (!float.IsNegativeInfinity(_weakPointUntil) && now >= _weakPointUntil) ResetCombatState();
        }

        // #308 D308-124: the element this enemy's attacks carry. The one place the counter-element sword strike and the
        // weak-point counter scale read it. A controller that owns these vitals registers itself (its vitals may be wired from
        // another object); otherwise an IEnemyAttackElement308 on this object answers. No source = none.
        public bool TryAttackElement(out Element element)
        {
            element = default;
            var source = _elementSource308;
            if (source is UnityEngine.Object registered && registered == null) source = _elementSource308 = null;
            if (source == null) source = GetComponent<IEnemyAttackElement308>();
            return source != null && source.TryAttackElement(out element);
        }
        public void SetAttackElementSource308(IEnemyAttackElement308 source) { _elementSource308 = source; }
        public void ClearAttackElementSource308(IEnemyAttackElement308 source) { if (ReferenceEquals(_elementSource308, source)) _elementSource308 = null; }
        private IEnemyAttackElement308 _elementSource308;

        // #308 D308-124 (debate decision, TEST): inside the window a spell of the player whose element overcomes this enemy's
        // attack element is multiplied (CombatConfigSO, 1.25). Before defence, like the window's own multiplier; the five-element
        // completion bonus is not multiplied. Spell = the player's main shot and sword form, a companion shot, a lasting zone,
        // a mark detonation; summons and retaliation are not.
        private float CounterScale308(in AttackProvenance attack)
        {
            if (!WeakPointActive || !attack.Element.HasValue || !IsSpell308(attack.Source)) return 1f;
            bool has = TryAttackElement(out Element enemy);
            return CounterElementRule308.Scale(true, true, attack.Element, has, enemy, _config != null ? _config.CounterElementWindowMultiplier : 1.25f);
        }
        private static bool IsSpell308(DamageSource source) => source == DamageSource.PlayerDirect || source == DamageSource.Companion ||
            source == DamageSource.PersistentSpell || source == DamageSource.Harmony;

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
            float damage = amount * DamageMultiplier * CounterScale308(attack);
            if (damage <= 0f || float.IsNaN(damage)) return default;
            // #308 WP-07 intake: this enemy's defence (data, default 0), the defence shred and damage-taken brand a spell left
            // on it, and whether this very hit ignores defence. An enemy with defence 0 and no modifier gets the value back
            // untouched, so every enemy and every spell that existed before behaves exactly as it did.
            // #308 WP-15: defence is cut to the config cap, and a raised guard acts only on the caster's own spell shots
            // (PlayerDirect · Companion). #308 WP-16 (D308-114): a melee hit (the sword form, the riposte) passes the guard.
            damage = Modifiers.Intake(damage, Defence, attack.AttackId, Time.time, _config != null ? _config.DefenceCap : 1f,
                (attack.Source == DamageSource.PlayerDirect || attack.Source == DamageSource.Companion) && !attack.Melee);
            if (damage <= 0f || float.IsNaN(damage)) return default; // only a full defence (1) the hit does not ignore
            float bonus = 0f;
            if (WeakPointActive && !floorAtOneHp && attack.Source == DamageSource.PlayerDirect && !attack.NoCredit &&
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

        // ---- S5-b slice 2 (D308-149, design section 5): the vol.2 damage entry ----
        // The vol.2 rules compute the whole damage themselves (D = H x min(R x S, 1.5) x K x B x (1 + M) x (1 + min(A, 10) / 100), B judged at
        // this very frame). So this entry applies NONE of the old terms: not DamageMultiplier (the window's legacy 1.5 - the riposte still reads
        // it), not CounterScale308, not the five-element completion bonus, not the five-element mask. It applies exactly ONE thing of the target
        // itself: its own armour state, once, through Modifiers.Intake (base defence, the strip / shred a spell left, a brand, a pierce
        // entry, the guard stance). Then HP, the optional one-HP floor, death and the same signals as TakeDamage (HpChanged, DamageResolved,
        // Died). hp is already in real HP (units x HpPerUnit, converted by the caller; the factor is in the terms for the log).
        public Vol2HitRecord LastVol2Hit { get; private set; }
        public event Action<Vol2HitRecord> Vol2DamageApplied;

        public EnemyDamageResult TakeResolvedVol2Damage(float hp, AttackProvenance attack, bool floorAtOneHp = false)
            => TakeResolvedVol2Damage(hp, attack, default, false, floorAtOneHp);

        public EnemyDamageResult TakeResolvedVol2Damage(float hp, AttackProvenance attack, in Vol2DamageTerms terms, bool floorAtOneHp = false)
            => TakeResolvedVol2Damage(hp, attack, terms, true, floorAtOneHp);

        private EnemyDamageResult TakeResolvedVol2Damage(float hp, AttackProvenance attack, in Vol2DamageTerms terms, bool hasTerms, bool floorAtOneHp)
        {
            ExpireWeakPoint(Time.time);
            if (!IsAlive || !(hp > 0f) || float.IsInfinity(hp)) return default;
            float damage = Modifiers.Intake(hp, Defence, attack.AttackId, Time.time, _config != null ? _config.DefenceCap : 1f,
                (attack.Source == DamageSource.PlayerDirect || attack.Source == DamageSource.Companion) && !attack.Melee);
            if (damage <= 0f || float.IsNaN(damage)) return default; // a full defence (1) that the hit does not ignore
            float floor = floorAtOneHp ? 1f : 0f;
            float before = _hp;
            _hp = Mathf.Max(Mathf.Min(floor, _hp), _hp - damage);
            bool floored = floorAtOneHp && _hp <= floor && before - damage < floor;
            var result = new EnemyDamageResult(this, attack, before - _hp, 0f, _hp <= 0f);
            LastVol2Hit = new Vol2HitRecord(terms, hasTerms, attack.AttackId, hp, damage, before - _hp, floored, _hp <= 0f);
            HpChanged?.Invoke();
            DamageResolved?.Invoke(result);
            Vol2DamageApplied?.Invoke(LastVol2Hit);
            if (_hp <= 0f) { LifeRevision++; Control.Clear(); Modifiers.Clear(); ResetCombatState(); Died?.Invoke(); }
            return result;
        }
    }
}
