using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string GiyeokLive258(string command)
  {
   var s=JourneySession258();var w=s.Walker.Wiring;var runtime=w.EAGiyeok;
   const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
   void Check(bool ok,string message){File.AppendAllText(ContinuationOutput258+"/giyeok-live.txt",(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
   Check(runtime!=null,"candidate profile owns giyeok service");
   var ink=(InkPool)typeof(WorldMacroPlaytestSession).GetField("ink",flags).GetValue(s);
   void Cast(char c)=>typeof(CombatLoopWiring).GetMethod("OnLetterDrawn",flags).Invoke(w,new object[]{new DrawnLetter(c,default,default,null,.6f,.6f,1,1,3)});
   var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
   if(command=="reload"){Check(s.HasDemoGuk&&runtime.Unlocked&&runtime.ActiveCount==0,"restart retains unlock without temporary attack effects");return "Restart checked";}
   if(command=="locked")
   {Check(!s.HasDemoGuk&&!runtime.Unlocked,"fresh save cannot enable final attacks");ink.Restore();Cast('각');Cast('낙');Check(runtime.ActiveCount==0,"central cast rejects both locked attacks");return "Locked path checked";}
   if(command!="active")throw new ArgumentException(command);
   Check(s.HasDemoGuk&&runtime.Unlocked,"actual saved boss defeat unlocks attacks");
   Check(ApproachVillage("village_rest"),"door approach fixture prepared for lifecycle test");
   var enemies=typeof(CombatLoopWiring).GetField("_enemies",flags);var prior=(EnemyVitals[])enemies.GetValue(w);
   var collect=typeof(CombatLoopWiring).GetMethod("CollectControllers",flags);
   var locking=(LockOn)typeof(CombatLoopWiring).GetField("_lockOn",flags).GetValue(w);
   var targetField=typeof(LockOn).GetField("_target",flags);var lockedField=typeof(LockOn).GetField("_locked",flags);
   var priorTarget=targetField.GetValue(locking);var priorLocked=lockedField.GetValue(locking);
   var go=new GameObject("CentralGiyeokFixture258");go.transform.position=w.SummonPlayer.position+w.SummonPlayer.forward*2;
   var target=go.AddComponent<EnemyVitals>();go.AddComponent<CapsuleCollider>();
   int accepted=0;void Accepted(SpellCast c,Vector3 p,Vector3 f){if(EAGiyeokRuntime.Owns(c.Letter))accepted++;}
   w.CastAccepted+=Accepted;
   try
   {
    enemies.SetValue(w,prior.Concat(new[]{target}).ToArray());collect.Invoke(w,null);
    targetField.SetValue(locking,target);lockedField.SetValue(locking,true);Physics.SyncTransforms();
    runtime.Clear();ink.Restore();float hp=target.Hp,cost=ink.Value;Cast('각');runtime.Tick(Time.time+1);
    Check(target.Hp<hp&&target.Control.BlocksActions(Time.time+1)&&ink.Value<cost,"central root spends ink and damages/controls registered target");
    runtime.Clear();target.Restore();hp=target.Hp;cost=ink.Value;Cast('낙');runtime.Tick(Time.time+1);
    Check(target.Hp<hp&&runtime.ActiveCount==1&&ink.Value<cost,"central burn schedules attached damage once");
    Check(accepted==2,"one accepted audio/presentation event per cast");
    ink.Restore(0);Cast('각');Check(accepted==2&&runtime.ActiveCount==1,"insufficient ink does not replace effect or publish cast");
    string save=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
    using(var locked=new FileStream(save+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {Check(!s.Interact("village_rest")&&runtime.ActiveCount==1,"failed rest save preserves owned attack effect");}
    Check(s.Interact("village_rest")&&runtime.ActiveCount==0,"successful door rest clears effect after persistence");
    return "Central cast/unlock/cost/rest checked with temporary target fixture; not manual combat";
   }
   finally
   {
    w.CastAccepted-=Accepted;runtime.Clear();enemies.SetValue(w,prior);collect.Invoke(w,null);
    targetField.SetValue(locking,priorTarget);lockedField.SetValue(locking,priorLocked);Object.DestroyImmediate(go);
   }
  }
 }
}
