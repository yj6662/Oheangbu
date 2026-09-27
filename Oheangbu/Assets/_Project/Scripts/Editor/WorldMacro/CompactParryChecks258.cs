using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string ParryChecks258()
  {
   var session=JourneySession258();var results=new List<string>();
   void Check(bool ok,string message){results.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllText(ContinuationOutput258+"/parry-hold.txt",string.Join("\n",results));if(!ok)throw new Exception(message);}
   var config=ScriptableObject.CreateInstance<CombatConfigSO>();
   try
   {
    var judge=new ParryJudge(config);int rewardEvents=0;judge.OwnedImpactResolved+=r=>{if(r.Outcome==ParryOutcome.Success)rewardEvents++;};
    judge.RaiseGuard(Element.Metal,10);
    Check(judge.GuardLifetime==config.GuardDuration&&judge.GuardWindow==config.ParryWindow,"legacy callers preserve original window and lifetime");
    Check(judge.ResolveImpact(Element.Wood,10+config.ParryWindow*.8f,Vector3.zero)==ParryOutcome.Success,"legacy correct element inside window succeeds");
    Check(judge.ResolveImpact(Element.Wood,10+config.ParryWindow*.85f,Vector3.zero)==ParryOutcome.None,"successful guard is consumed");
    judge.RaiseGuard(Element.Metal,20,.4f);
    Check(Mathf.Abs(judge.GuardLifetime-config.GuardDuration*.4f)<.001f&&Mathf.Abs(judge.GuardWindow-config.ParryWindow*.4f)<.001f,"hold penalty shortens both clocks once");
    int before=rewardEvents;
    Check(judge.ResolveImpact(Element.Wood,20+config.ParryWindow*.8f,Vector3.zero)==ParryOutcome.Block&&rewardEvents==before,"previously valid late timing becomes block without success reward");
    Check(judge.ResolveImpact(Element.Wood,20+config.GuardDuration*.4f+.01f,Vector3.zero)==ParryOutcome.None,"weakened guard expires on shortened boundary");
    judge.RaiseGuard(Element.Water,30,.4f);
    Check(judge.ResolveImpact(Element.Wood,30+config.ParryWindow*.2f,Vector3.zero)==ParryOutcome.Fail,"generating element still fails rather than granting success");
    Check(judge.ResolveImpact(Element.Wood,30+config.ParryWindow*.8f,Vector3.zero)==ParryOutcome.Block,"generating failure leaves subsequent block period");
    judge.RaiseGuard(Element.Fire,40,.4f);
    Check(judge.ResolveImpact(Element.Wood,40+config.ParryWindow*.2f,Vector3.zero)==ParryOutcome.Half,"other element retains half outcome");
    judge.RaiseGuard(Element.Metal,50,.4f);var attack=AttackProvenance.Create(session.Walker.Wiring,DamageSource.Enemy,Element.Wood);before=rewardEvents;
    var a=judge.ResolveImpact(Element.Wood,50,Vector3.zero,attack);var b=judge.ResolveImpact(Element.Wood,50,Vector3.zero,attack);
    Check(a==ParryOutcome.Success&&b==a&&rewardEvents==before+1,"owned impact replay cannot double reward");
    var w=session.Walker.Wiring;var liveJudge=(ParryJudge)typeof(CombatLoopWiring).GetField("_judge",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w);
    var ink=(InkPool)typeof(WorldMacroPlaytestSession).GetField("ink",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
    var liveConfig=(CombatConfigSO)typeof(CombatLoopWiring).GetField("_config",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w);
    void Cast(float hold)=>typeof(CombatLoopWiring).GetMethod("OnLetterDrawn",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{new DrawnLetter('서',default,default,null,.6f,.6f,1,hold,3)});
    w.EABuffs.Clear(Time.time);ink.Restore();Cast(20);
    Check(Mathf.Abs(liveJudge.GuardLifetime-liveConfig.GuardDuration*.4f)<.001f,"actual candidate long hold reaches minimum guard lifetime");
    var adapter=(BrushStrokeFeedAdapter)typeof(CombatLoopWiring).GetField("_brushAdapter",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w);
    typeof(BrushStrokeFeedAdapter).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(adapter,null);
    float visual=(float)typeof(BrushStrokeFeedAdapter).GetField("_pendingGuardDuration",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(adapter);
    Check(Mathf.Abs(visual-liveJudge.GuardLifetime)<.001f,"visual event queue receives exact shortened gameplay clock");
    typeof(BrushStrokeFeedAdapter).GetMethod("ApplyCommitted",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(adapter,new object[]{false,Time.time});
    Check((float)typeof(BrushStrokeFeedAdapter).GetField("_pendingGuardDuration",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(adapter)==0,"commit clears clock; later legacy cast cannot inherit it");
    ink.Restore();Cast(1);Check(liveJudge.GuardLifetime==liveConfig.GuardDuration,"short hold restores full guard lifetime");
    uint revision=liveJudge.GuardRevision;ink.Restore(0);Cast(20);Check(liveJudge.GuardRevision==revision,"insufficient ink cannot replace guard clock");
    ink.Restore();liveJudge.ClearGuard();return string.Join("\n",results);
   }
   finally{UnityEngine.Object.DestroyImmediate(config);}
  }
 }
}
