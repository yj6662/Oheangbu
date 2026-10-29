using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 플레이어 생명 — 피격의 유일 창구. 피격 보상 금지(헌법)·응보 허용은 여기 훅에 무엇을
    // 달지 않는 것으로 지켜진다. 「피격=작도 중단」은 Damaged 이벤트를 App 배선이
    // InterruptLetter로 연결해 이행한다(COMBAT-ATTACK — 표현·입력 계층에 직접 닿지 않는다).
    public enum EnvironmentDeathCause { None, Fall, DeepWater }
    // #308 D308-125 적용 5: Lingering = burning ground and the scorch it leaves (TakeLingeringDamage308 only)
    public enum IncomingDamageKind { Unspecified, Melee, Ranged, ElementalMelee, ElementalRanged, Lingering }
    // #308: one damaging hit as the player took it. Attacker = null for the environment and for old callers.
    public readonly struct PlayerHitInfo
    {
        public readonly EnemyVitals Attacker;
        public readonly float Amount;
        public readonly IncomingDamageKind Kind;
        public PlayerHitInfo(EnemyVitals attacker, float amount, IncomingDamageKind kind) { Attacker = attacker; Amount = amount; Kind = kind; }
    }
    public sealed class PlayerVitals : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private DodgeAction _dodge;

        private float _hp;
        private Func<float,bool> _lethalDamageGuard;
        private readonly List<Func<IncomingDamageKind,float>> _incomingDamageScales = new List<Func<IncomingDamageKind,float>>();
        public void BindIncomingDamageScale(Func<IncomingDamageKind,float> scale)
        {
            if(scale==null)throw new ArgumentNullException(nameof(scale));
            if(!_incomingDamageScales.Contains(scale))_incomingDamageScales.Add(scale);
        }
        public void UnbindIncomingDamageScale(Func<IncomingDamageKind,float> scale)
        { _incomingDamageScales.Remove(scale); }

        // #308 D308-115: a single ranged ward owner (the water summon). A second owner is refused, not replaced.
        private IRangedWard308 _rangedWard308;
        public bool BindRangedWard308(IRangedWard308 ward)
        {
            if(ward==null)throw new ArgumentNullException(nameof(ward));
            if(_rangedWard308!=null&&_rangedWard308!=ward&&!(_rangedWard308 is UnityEngine.Object gone&&gone==null))return false;
            _rangedWard308=ward;return true;
        }
        public void UnbindRangedWard308(IRangedWard308 ward){if(_rangedWard308==ward)_rangedWard308=null;}
        public bool HasRangedWard308=>_rangedWard308!=null&&!(_rangedWard308 is UnityEngine.Object gone&&gone==null);
        // Would TakeAttackDamage really take this hit (finite and positive after the incoming scales, no dodge
        // invulnerability, still alive)? Reads only — nothing changes.
        public bool WouldLand308(float amount, IncomingDamageKind kind)
        {
            if(!float.IsFinite(amount)||amount<=0)return false;
            if(_dodge!=null&&_dodge.IsInvulnerable)return false;
            if(_hp<=0f)return false;
            foreach(var modifier in _incomingDamageScales)
            { float scale=modifier(kind);if(float.IsFinite(scale))amount*=Mathf.Clamp01(scale); }
            return amount>0;
        }
        // EnemyStrike308.Deliver only. Ranged kinds that would land are offered to the ward; everything else passes untouched.
        public bool TryRangedWard308(EnemyVitals attacker, float amount, IncomingDamageKind kind)
        {
            if(!HasRangedWard308)return false;
            if(kind!=IncomingDamageKind.Ranged&&kind!=IncomingDamageKind.ElementalRanged)return false;
            if(!WouldLand308(amount,kind))return false;
            return _rangedWard308.TryBlockRanged308(attacker,amount,kind);
        }
        public float Heal(float amount)
        {
            if(_hp<=0||!float.IsFinite(amount)||amount<=0)return 0;
            float gained=Mathf.Min(amount,Mathf.Max(0,MaxHp-_hp));
            if(gained>0){_hp+=gained;HpChanged?.Invoke();}return gained;
        }
        private float _maxHpScale=1f;
        public float MaxHp => (_config!=null?_config.PlayerMaxHp:100f)*_maxHpScale;
        public void SetMaxHpMultiplier(float scale)
        {
            if(!float.IsFinite(scale)||scale<1f)throw new ArgumentOutOfRangeException(nameof(scale));
            float fraction=Hp01;_maxHpScale=scale;_hp=MaxHp*fraction;HpChanged?.Invoke();
        }

        public event Action<float> Damaged; // 실피해량 — 무적으로 막힌 피격은 발화하지 않는다
        public event Action<PlayerHitInfo> AttackDamaged; // #308: the same hit with its attacker (after Damaged). No reward hangs here: retaliation only
        public PlayerHitInfo LastHit { get; private set; } // #308: set before Damaged fires, so a Damaged listener can read who struck
        public long HitSerial308 { get; private set; } // S5-b slice 8: counts real hits (LastHit changes with it); the vol.2 hit gate reports each hit once, whoever listens first
        public event Action Died;
        public event Action HpChanged;
        // #308 D308-125 적용 5: burning-ground ticks. No Damaged, no AttackDamaged, no LastHit: a tick neither breaks a letter
        // nor counts as a hit (EnemyStrike308 Spec §10 exception, recorded in DECISIONS).
        public event Action<float> LingeringDamaged;
        // #308 D308-125 적용 5: the lethal guard just kept the player alive (any damage path). Listeners end what keeps burning.
        public event Action Spared308;

        // One App owner may durably accept survival before HP or damage events become observable.
        public void BindLethalDamageGuard(Func<float,bool> guard)
        {
            if(guard==null)throw new ArgumentNullException(nameof(guard));
            if(_lethalDamageGuard!=null&&_lethalDamageGuard!=guard)
                throw new InvalidOperationException("A lethal damage guard is already bound.");
            _lethalDamageGuard=guard;
        }
        public void UnbindLethalDamageGuard(Func<float,bool> guard)
        {if(_lethalDamageGuard==guard)_lethalDamageGuard=null;}

        public float Hp01 => MaxHp>0f?Mathf.Clamp01(_hp/MaxHp):0f;
        public void Restore(float fraction = 1f)
        { LastEnvironmentDeath=EnvironmentDeathCause.None; _hp = MaxHp * Mathf.Clamp01(fraction); HpChanged?.Invoke(); }
        public EnvironmentDeathCause LastEnvironmentDeath {get;private set;}
        public void ApplyFatalFall()=>ApplyEnvironmentalDeath(EnvironmentDeathCause.Fall);
        public void ApplyEnvironmentalDeath(EnvironmentDeathCause cause)
        { if(_hp<=0)return;LastEnvironmentDeath=cause; _hp=0; HpChanged?.Invoke(); Died?.Invoke(); }

        private void Awake()
        {
            _hp = MaxHp;
        }

        // true = 실제로 맞았다(회피 무적이면 false — 판정만 있고 결과 없음)
        public bool TakeDamage(float amount)
            => TakeAttackDamage(amount, IncomingDamageKind.Unspecified);

        public bool TakeAttackDamage(float amount, IncomingDamageKind kind)
            => TakeAttackDamage(amount, kind, null);

        // #308: enemy attacks arrive through EnemyStrike308.Deliver with their attacker. Amount, order and events are unchanged.
        public bool TakeAttackDamage(float amount, IncomingDamageKind kind, EnemyVitals attacker)
        {
            if(!float.IsFinite(amount)||amount<=0)return false;
            if (_dodge != null && _dodge.IsInvulnerable) return false;
            if (_hp <= 0f) return false;

            foreach(var modifier in _incomingDamageScales)
            { float scale=modifier(kind);if(float.IsFinite(scale))amount*=Mathf.Clamp01(scale); }
            if(amount<=0)return false;

            bool spared=Lose308(amount);
            HpChanged?.Invoke();
            LastHit = new PlayerHitInfo(attacker, amount, kind); HitSerial308++;
            Damaged?.Invoke(amount);
            AttackDamaged?.Invoke(LastHit);
            if (spared) Spared308?.Invoke();
            if (_hp <= 0f) Died?.Invoke();
            return true;
        }

        // #308 D308-125 적용 5: one tick of burning ground (dodgeable = a dodge's invulnerability skips it) or of the scorch
        // (not dodgeable). The incoming scales see Lingering; the lethal guard is the same as for a hit.
        public bool TakeLingeringDamage308(float amount, bool dodgeable)
        {
            if(!float.IsFinite(amount)||amount<=0)return false;
            if(dodgeable&&_dodge!=null&&_dodge.IsInvulnerable)return false;
            if(_hp<=0f)return false;
            foreach(var modifier in _incomingDamageScales)
            { float scale=modifier(IncomingDamageKind.Lingering);if(float.IsFinite(scale))amount*=Mathf.Clamp01(scale); }
            if(amount<=0)return false;
            bool spared=Lose308(amount);
            HpChanged?.Invoke();
            LingeringDamaged?.Invoke(amount);
            if(spared)Spared308?.Invoke();
            if(_hp<=0f)Died?.Invoke();
            return true;
        }

        // true = the lethal guard held the player at the surviving HP
        private bool Lose308(float amount)
        {
            float remaining=Mathf.Max(0f,_hp-amount);
            float survivingHp=Mathf.Min(1f,MaxHp);
            bool spared=remaining<=0f&&survivingHp>0f&&_lethalDamageGuard!=null&&_lethalDamageGuard(survivingHp/MaxHp);
            if(spared)remaining=survivingHp;
            _hp=remaining;
            return spared;
        }
    }
}
