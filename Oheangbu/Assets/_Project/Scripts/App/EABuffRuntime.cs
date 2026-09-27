using System;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.App.SpellVFX120;
using UnityEngine;

namespace Oheangbu.App
{
    // Temporary combat state. Unlocks belong to the campaign save; buffs never enter that save.
    public sealed class EABuffRuntime : IDisposable
    {
        static readonly char[] Letters = { '걱', '넉', '먹', '석', '억' };
        readonly EABuffProfileSO profile;
        readonly PlayerVitals vitals;
        readonly CombatLoopWiring wiring;
        readonly Func<bool> unlocked;
        readonly Func<float> clock;
        readonly float[] ends = new float[5];
        readonly Func<IncomingDamageKind, float> damageScale;
        readonly GameObject[] visuals = new GameObject[5];
        readonly Vfx120Profile[] visualProfiles = new Vfx120Profile[5];
        float lastTick, auraSampleAt, auraScale = 1f;
        bool disposed, ticking;
        public EABuffRuntime(EABuffProfileSO profile, PlayerVitals vitals, CombatLoopWiring wiring,
            Func<bool> unlocked, float now, Func<float> clock = null)
        {
            if (profile == null || !profile.Valid || vitals == null || wiring == null || unlocked == null)
                throw new ArgumentException("Valid buff profile and live combat owners required");
            this.profile = profile; this.vitals = vitals; this.wiring = wiring; this.unlocked = unlocked;
            this.clock = clock ?? (() => Time.time);
            lastTick = now; damageScale = IncomingScale;
            vitals.BindIncomingDamageScale(damageScale);
        }
        public bool Unlocked => !disposed && unlocked();
        public bool Active(char letter, float now)
        {
            int index = Array.IndexOf(Letters, letter);
            return !disposed && index >= 0 && ends[index] > now;
        }
        public float Remaining(char letter, float now)
        { int index=Array.IndexOf(Letters,letter);return !disposed&&index>=0?Mathf.Max(0,ends[index]-now):0; }
        public bool CanActivate(char letter) => Unlocked && profile.Valid && vitals.Hp01 > 0
            && Array.IndexOf(Letters, letter) >= 0;
        public bool Activate(char letter, float now)
        {
            if (!CanActivate(letter) || !float.IsFinite(now)) return false;
            Tick(now); // Finish the old interval before refreshing its deadline.
            if (letter == '넉') FlushAura(now);
            int index = Array.IndexOf(Letters, letter);
            ends[index] = now + (letter == '넉' ? profile.AuraDuration : profile.Duration);
            if (letter == '넉') { auraScale = wiring.SummonDamageScale(Element.Fire); auraSampleAt = now; }
            RefreshVisual(index, ends[index]-now);
            return true;
        }
        public bool HasVisual(char letter)
        { int index=Array.IndexOf(Letters,letter);return index>=0&&visuals[index]!=null; }
        void RefreshVisual(int index,float duration)
        {
            ClearVisual(index);
            if(profile.PresentationPrefabs==null||index>=profile.PresentationPrefabs.Length||profile.PresentationPrefabs[index]==null)return;
            var go=UnityEngine.Object.Instantiate(profile.PresentationPrefabs[index]);
            var fx=go.GetComponent<Vfx120Effect>();
            if(fx==null||fx.Profile==null){UnityEngine.Object.Destroy(go);return;}
            visuals[index]=go;go.name="EA_Buff_"+Letters[index];go.SetActive(true);
            visualProfiles[index]=UnityEngine.Object.Instantiate(fx.Profile);
            visualProfiles[index].Duration=duration;fx.Profile=visualProfiles[index];
            var owner=wiring.SummonPlayer;
            fx.Begin(owner.position,null,owner.position+owner.forward,fx.Profile.Pigment);
            go.transform.SetParent(owner,true);
        }
        void ClearVisual(int index)
        {
            if(visuals[index]!=null)UnityEngine.Object.Destroy(visuals[index]);
            if(visualProfiles[index]!=null)UnityEngine.Object.Destroy(visualProfiles[index]);
            visuals[index]=null;visualProfiles[index]=null;
        }
        public float CostScale(float now) => Active('억', now) ? 1f - profile.CostReduction : 1f;
        public float HoldPower(float holdSeconds, float now)
        {
            if (!float.IsFinite(holdSeconds)) return profile.MinimumHoldPower;
            float grace = profile.HoldGrace + (Active('석', now) ? profile.ExtraHoldGrace : 0f);
            return Mathf.Max(profile.MinimumHoldPower, 1f - Mathf.Max(0, holdSeconds - grace) * profile.HoldLossPerSecond);
        }
        float IncomingScale(IncomingDamageKind kind) => (kind == IncomingDamageKind.Ranged || kind == IncomingDamageKind.ElementalRanged) && Active('먹', clock())
            ? 1f - profile.RangedReduction : 1f;
        public void Tick(float now)
        {
            if (disposed || ticking || !float.IsFinite(now) || now <= lastTick) return;
            float from = lastTick; lastTick = now;
            if (vitals.Hp01 <= 0 || !wiring.isActiveAndEnabled) { Clear(now); return; }
            ticking = true;
            try
            {
                float regenSeconds = Mathf.Max(0, Mathf.Min(now, ends[0]) - from);
                if (regenSeconds > 0) vitals.Heal(vitals.MaxHp * profile.RegenMaxHpPerSecond * regenSeconds);
                if (now >= auraSampleAt + .5f || now >= ends[1]) FlushAura(now);
                for(int i=0;i<ends.Length;i++)if(now>=ends[i])ClearVisual(i);
            }
            finally { ticking = false; }
        }
        void FlushAura(float now)
        {
                float to = Mathf.Min(now, ends[1]);
                float auraSeconds = Mathf.Max(0, to - auraSampleAt);
                auraSampleAt = Mathf.Max(auraSampleAt, to);
                if (auraSeconds <= 0) return;
                // One target entry per actor, independent of its collider count. No direct-hit completion credit.
                var origin = wiring.SummonPlayer.position + Vector3.up * .5f;
                var attack = AttackProvenance.Create(wiring.SummonPlayer, DamageSource.PersistentSpell, Element.Fire);
                foreach (var target in wiring.SummonTargets)
                {
                    if (disposed || ends[1] <= 0 || vitals.Hp01 <= 0) break;
                    if (target == null || !target.IsAlive || !target.isActiveAndEnabled) continue;
                    if (Vector3.Distance(origin, target.transform.position + Vector3.up * .5f) > profile.AuraRadius) continue;
                    wiring.ApplyPersistentSpellHit(target, profile.AuraDamagePerSecond * auraSeconds * auraScale,
                        origin, attack, '넉');
                }
        }
        public void Clear(float now)
        { Array.Clear(ends, 0, ends.Length); lastTick = auraSampleAt = now; auraScale = 1f;for(int i=0;i<visuals.Length;i++)ClearVisual(i); }
        public void Dispose()
        {
            if (disposed) return;
            Clear(clock()); disposed = true; vitals.UnbindIncomingDamageScale(damageScale);
        }
    }
}
