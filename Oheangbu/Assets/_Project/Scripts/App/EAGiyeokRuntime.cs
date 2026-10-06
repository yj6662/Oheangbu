using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.App.SpellVFX120;
using UnityEngine;

namespace Oheangbu.App
{
    // First final-consonant attack tranche: root and target-attached burn.
    public sealed partial class EAGiyeokRuntime:IDisposable
    {
        sealed class Cast
        {
            public SpellCast Spell;public EnemyVitals Target;public uint Life;
            public float At,Until,SampleAt,Power;public bool Hit;
            public Vector3 Origin;public AttackProvenance Attack;
            public GameObject Visual;public Vfx120Profile VisualProfile;public Vfx120Effect Fx;
        }
        readonly List<Cast> casts=new List<Cast>();
        readonly EAGiyeokProfileSO profile;
        readonly CombatLoopWiring wiring;
        readonly PlayerVitals vitals;
        readonly Func<bool> unlocked;
        bool disposed;
        public bool Unlocked=>!disposed&&profile.Valid&&unlocked();
        public bool CanCast(char letter)=>Unlocked&&Owns(letter)&&wiring.isActiveAndEnabled&&vitals.Hp01>0;
        public static bool Owns(char letter)=>letter=='각'||letter=='낙'||letter=='삭'||letter=='악';
        public int ActiveCount=>casts.Count+traces.Count;
        public EAGiyeokRuntime(EAGiyeokProfileSO profile,CombatLoopWiring wiring,PlayerVitals vitals,Func<bool> unlocked)
        {
            if(profile==null||!profile.Valid||wiring==null||vitals==null||unlocked==null)throw new ArgumentException();
            this.profile=profile;this.wiring=wiring;this.vitals=vitals;this.unlocked=unlocked;
        }
        public bool CastSpell(SpellCast spell,EnemyVitals target,Vector3 origin,Vector3 missPoint,float flight,float now)
        {
            if(!CanCast(spell.Letter)||!float.IsFinite(now)||!float.IsFinite(flight)||flight<=0)return false;
            if(spell.Letter=='삭'||spell.Letter=='악')return CastTrace(spell,target,origin,missPoint,flight,now);
            float duration=spell.Letter=='각'?profile.RootDuration:profile.BurnDuration;
            var cast=new Cast{Spell=spell,Target=target,Life=target!=null?target.LifeRevision:0,At=now+flight,Until=now+flight+duration,
                Origin=origin,Power=spell.Power*wiring.SummonDamageScale(spell.Element),
                Attack=AttackProvenance.Create(wiring.SummonPlayer,DamageSource.PlayerDirect,spell.Element)};
            var prefab=spell.Letter=='각'?profile.RootPrefab:profile.BurnPrefab;
            if(prefab!=null)
            {
                cast.Visual=UnityEngine.Object.Instantiate(prefab);cast.Visual.name="EA_Giyeok_"+spell.Letter;
                cast.Fx=cast.Visual.GetComponent<Vfx120Effect>();
                if(cast.Fx!=null&&cast.Fx.Profile!=null)
                {
                    cast.VisualProfile=UnityEngine.Object.Instantiate(cast.Fx.Profile);cast.VisualProfile.Duration=flight+duration;
                    cast.Fx.Profile=cast.VisualProfile;cast.Fx.PreviewControlled=false;cast.Fx.DemonstrationCues=false;
                    wiring.DeployDirector308?.HostEffect(cast.Fx,spell.Brush01);   // #308 forms2 S1 (a field assignment; never throws)
                    cast.Fx.SetImpactClock(flight);cast.Fx.Begin(origin,target!=null?target.transform:null,missPoint,cast.Fx.Profile.Pigment);
                }
            }
            casts.Add(cast);return true;
        }
        public void Tick(float now)
        {
            if(disposed||!float.IsFinite(now))return;
            if(!wiring.isActiveAndEnabled||vitals.Hp01<=0){Clear();return;}
            TickTraces(now);
            // Damage callbacks can clear the owner, so remove from the list before callbacks.
            var frame=casts.ToArray();
            foreach(var cast in frame)
            {
                if(!casts.Contains(cast))continue;
                if(now<cast.At)continue;
                if(cast.Target==null||!cast.Target.IsAlive||!cast.Target.isActiveAndEnabled||cast.Target.LifeRevision!=cast.Life)
                {Remove(cast);continue;}
                if(!cast.Hit)
                {
                    cast.Hit=true;cast.SampleAt=cast.At;
                    var result=wiring.ApplyGiyeokDirectHit(cast.Target,cast.Life,cast.Power,cast.Origin,cast.Attack,cast.Spell.Letter);
                    if(!casts.Contains(cast))continue;
                    if(result.AppliedDamage<=0||result.Killed||cast.Target.LifeRevision!=cast.Life){Remove(cast);continue;}
                    // Recasting refreshes this actor's same effect after a confirmed hit, never before.
                    foreach(var old in frame)
                        if(old!=cast&&old.Hit&&old.Target==cast.Target&&old.Spell.Letter==cast.Spell.Letter&&casts.Contains(old))
                        {if(old.Spell.Letter=='낙')Burn(old,now);Remove(old);}
                    if(!casts.Contains(cast)||!cast.Target.IsAlive||cast.Target.LifeRevision!=cast.Life){Remove(cast);continue;}
                    if(cast.Spell.Letter=='각')
                    {
                        // #308 WP-00: boss = the enemy's data (EnemyVitals.IsBoss; this also covers the mine boss). The two controller
                        // types remain only for pre-#306 scenes whose bosses carry no vitals profile.
                        bool boss=cast.Target.IsBoss||cast.Target.GetComponent<Demo.CheongryongCombatController>()!=null||cast.Target.GetComponent<Demo.SouthGateGeneralController>()!=null;
                        RootBindRule308.Resolve(boss,profile.BossRootSpeed,out float rootSpeed,out bool blocks);
                        cast.Target.Control.Apply(cast.Attack.AttackId,cast.Until,rootSpeed,blocks);
                        if(blocks)cast.Target.GetComponent<EnemyController>()?.StopAttack();
                    }
                }
                if(cast.Spell.Letter=='낙'&&(now>=cast.SampleAt+.5f||now>=cast.Until))
                    Burn(cast,now);
                if(casts.Contains(cast)&&(now>=cast.Until||!cast.Target.IsAlive))Remove(cast);
            }
        }
        void Burn(Cast cast,float now)
        {
            float to=Mathf.Min(now,cast.Until),seconds=Mathf.Max(0,to-cast.SampleAt);cast.SampleAt=to;
            if(seconds>0)wiring.ApplyPersistentSpellHit(cast.Target,cast.Power*profile.BurnPowerPerSecond*seconds,
                cast.Target.transform.position+Vector3.up*.6f,
                new AttackProvenance(cast.Attack.AttackId,cast.Attack.Instigator,DamageSource.PersistentSpell,Element.Fire),'낙');
        }
        void Remove(Cast cast)
        {
            if(!casts.Remove(cast))return;
            if(cast.Target!=null)cast.Target.Control.Remove(cast.Attack.AttackId);
            if(cast.Visual!=null)UnityEngine.Object.Destroy(cast.Visual);
            if(cast.VisualProfile!=null)UnityEngine.Object.Destroy(cast.VisualProfile);
        }
        public void Clear(){foreach(var trace in traces.ToArray())RemoveTrace(trace);foreach(var cast in casts.ToArray())Remove(cast);}
        public void Dispose(){if(disposed)return;Clear();disposed=true;}
    }
}
