using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.App.SpellVFX120;
using UnityEngine;
namespace Oheangbu.App
{
 public sealed partial class EAGiyeokRuntime
 {
  sealed class Trace
  {
   public SpellCast Spell;public EnemyVitals Target;public uint Life;public float Start,Flight,End,Power,Front;
   public Vector3 Origin,Direction,Bounce;public bool FirstHit;
   public AttackProvenance Attack;
   public readonly Dictionary<EnemyVitals,uint> Lives=new Dictionary<EnemyVitals,uint>();
   public readonly HashSet<EnemyVitals> Hit=new HashSet<EnemyVitals>();
   public GameObject Visual;public Vfx120Effect Fx;public Vfx120Profile VisualProfile;
  }
  readonly List<Trace> traces=new List<Trace>();RaycastHit[] traceHits;
  bool CastTrace(SpellCast spell,EnemyVitals target,Vector3 origin,Vector3 point,float flight,float now)
  {
   var delta=point-origin;if(delta.sqrMagnitude<.0001f)delta=wiring.SummonPlayer.forward;
   bool pierce=spell.Letter=='삭';float speed=Mathf.Max(1,delta.magnitude/flight);
   float duration=pierce?profile.PierceRange/speed:flight;
   var t=new Trace{Spell=spell,Target=target,Life=target!=null?target.LifeRevision:0,Origin=origin,Direction=delta.normalized,
    Start=now,Flight=duration,End=now+duration+(pierce?0:profile.ReturnDelay),Power=spell.Power*wiring.SummonDamageScale(spell.Element),
    Attack=AttackProvenance.Create(wiring.SummonPlayer,DamageSource.PlayerDirect,spell.Element)};
   foreach(var e in wiring.SummonTargets)if(e!=null&&!t.Lives.ContainsKey(e))t.Lives.Add(e,e.LifeRevision);
   var prefab=pierce?profile.PiercePrefab:profile.ReturnPrefab;
   if(prefab!=null)
   {
    t.Visual=UnityEngine.Object.Instantiate(prefab);t.Visual.name="EA_Giyeok_"+spell.Letter;t.Fx=t.Visual.GetComponent<Vfx120Effect>();
    if(t.Fx!=null&&t.Fx.Profile!=null)
    {
     t.VisualProfile=UnityEngine.Object.Instantiate(t.Fx.Profile);t.VisualProfile.Duration=t.End-now;
     t.Fx.Profile=t.VisualProfile;t.Fx.PreviewControlled=false;t.Fx.DemonstrationCues=false;
     wiring.DeployDirector308?.HostEffect(t.Fx,spell.Brush01);   // #308 forms2 S1 (a field assignment; never throws)
     t.Fx.SetImpactClock(duration);t.Fx.Begin(origin,pierce?null:target!=null?target.transform:null,pierce?origin+t.Direction*profile.PierceRange:point,t.VisualProfile.Pigment);
    }
   }
   traces.Add(t);return true;
  }
  void TickTraces(float now)
  {
   foreach(var t in traces.ToArray())
   {
    if(!traces.Contains(t)||now<t.Start)continue;
    if(t.Spell.Letter=='삭')
    {
     float front=Mathf.Clamp01((now-t.Start)/t.Flight)*profile.PierceRange;
     // Once a wall has stopped the projectile, it cannot resume behind that wall.
     float stop=front;int count=ScenePhysicsQuery.RaycastAll(wiring.gameObject.scene,t.Origin,t.Direction,front,~0,ref traceHits);
     for(int i=0;i<count;i++)
     {
      var hit=traceHits[i];if(hit.collider.isTrigger||hit.transform.IsChildOf(wiring.SummonPlayer)||hit.transform.GetComponentInParent<EnemyVitals>()!=null)continue;
      stop=Mathf.Min(stop,hit.distance);
     }
     foreach(var pair in t.Lives)
     {
      if(!traces.Contains(t))break;var enemy=pair.Key;
      if(enemy==null||!enemy.IsAlive||!enemy.isActiveAndEnabled||enemy.LifeRevision!=pair.Value||t.Hit.Contains(enemy))continue;
      var delta=enemy.transform.position+Vector3.up*.4f-t.Origin;float along=Vector3.Dot(delta,t.Direction);
      if(along<t.Front-.001f||along>stop||along<0||(delta-t.Direction*along).sqrMagnitude>profile.PierceRadius*profile.PierceRadius)continue;
      t.Hit.Add(enemy);var hit=wiring.ApplyGiyeokDirectHit(enemy,pair.Value,t.Power,t.Origin,t.Attack,'삭');
      if(hit.AppliedDamage>0)t.Fx?.Signal(Vfx120Effect.Cue.Hit);
     }
     t.Front=front;if(stop<front||now>=t.End)RemoveTrace(t);
     continue;
    }
    if(now<t.Start+t.Flight)continue;
    var target=t.Target;
    if(target==null||!target.IsAlive||!target.isActiveAndEnabled||target.LifeRevision!=t.Life){RemoveTrace(t);continue;}
    if(!t.FirstHit)
    {
     t.FirstHit=true;var hit=wiring.ApplyGiyeokDirectHit(target,t.Life,t.Power,t.Origin,t.Attack,'악');
     if(!traces.Contains(t))continue;
     if(hit.AppliedDamage<=0||hit.Killed||target.LifeRevision!=t.Life){RemoveTrace(t);continue;}
     var impact=target.transform.position+Vector3.up*.4f;float reach=profile.ReturnReach;
     int count=ScenePhysicsQuery.RaycastAll(wiring.gameObject.scene,impact,t.Direction,reach,~0,ref traceHits);
     for(int i=0;i<count;i++)
     {var obstacle=traceHits[i];if(!obstacle.collider.isTrigger&&!obstacle.transform.IsChildOf(wiring.SummonPlayer)&&obstacle.transform.GetComponentInParent<EnemyVitals>()==null)reach=Mathf.Min(reach,Mathf.Max(0,obstacle.distance-.08f));}
     t.Bounce=impact+t.Direction*reach;
     t.Fx?.Signal(Vfx120Effect.Cue.Hit);
    }
    if(now>=t.End)
    {
     // Separate impact identity, same captured power, same actor and life. Never retarget.
     var attack=AttackProvenance.Create(wiring.SummonPlayer,DamageSource.PlayerDirect,t.Spell.Element);
     // Remove first: damage callbacks can clear this owner or tick it recursively.
     traces.Remove(t);wiring.ApplyGiyeokDirectHit(target,t.Life,t.Power,t.Bounce,attack,'악');DisposeTrace(t);
    }
   }
  }
  static void DisposeTrace(Trace t)
  {
   void Destroy(UnityEngine.Object obj){if(obj==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
   Destroy(t.Visual);Destroy(t.VisualProfile);
  }
  void RemoveTrace(Trace t){if(traces.Remove(t))DisposeTrace(t);}
 }
}
