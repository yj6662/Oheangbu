using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string BuffBuild258()
  {
   var scene=FrontageScene249();var session=VillageSession();
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Continuation258";
   Directory.CreateDirectory(folder);
   string path=folder+"/EABuffs_TEST.asset";
   var profile=AssetDatabase.LoadAssetAtPath<EABuffProfileSO>(path);
   if(profile==null){profile=ScriptableObject.CreateInstance<EABuffProfileSO>();AssetDatabase.CreateAsset(profile,path);}
   var visuals=AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>("Assets/_Project/Art/SpellVFX120/Data/SpellVisualSet_120.asset");
   profile.PresentationPrefabs="걱넉먹석억".Select(c=>{if(!visuals.TryGet(c,out var entry)||entry.FxPrefab==null)throw new Exception("Missing buff presentation "+c);return entry.FxPrefab;}).ToArray();
   EditorUtility.SetDirty(profile);
   session.EABuffProfile=profile;EditorUtility.SetDirty(session);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Candidate EA buff TEST profile connected; saved giyeok unlock required. Shared book and old scenes unchanged.";
  }
  public static string BuffChecks258()
  {
   JourneySession258(); // Requires the private diagnostic scene, even though test actors are temporary.
   var results=new List<string>();var objects=new List<Object>();
   void Check(bool ok,string message){results.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllText(ContinuationOutput258+"/buff-contracts.txt",string.Join("\n",results));if(!ok)throw new Exception(message);}
   var profile=ScriptableObject.CreateInstance<EABuffProfileSO>();objects.Add(profile);
   var book=AssetDatabase.LoadAssetAtPath<SpellBookSO>("Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset");
   var resolver=new SpellResolver(book);
   DrawnLetter Letter(char c,float hold=1)=>new DrawnLetter(c,default,default,null,.6f,.6f,1,hold,3);
   var player=new GameObject("Buff258TestPlayer");objects.Add(player);player.transform.position=new Vector3(-10000,500,-10000);
   var vitals=player.AddComponent<PlayerVitals>();var wiring=player.AddComponent<CombatLoopWiring>();
   // Remove contact art from the isolated target tests. Gameplay hit events remain connected.
   typeof(CombatLoopWiring).GetField("_contactVfx",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(wiring,null);
   float now=10;bool unlocked=false;EABuffRuntime runtime=null;
   try
   {
    runtime=new EABuffRuntime(profile,vitals,wiring,()=>unlocked,now,()=>now);
    foreach(char c in "걱넉먹석억")
    {
     Check(!resolver.TryResolve(Letter(c),out _,false),"legacy resolver does not unlock "+c);
     Check(!runtime.Activate(c,now),"locked buff rejected "+c);
     Check(resolver.TryResolve(Letter(c),out var cast,false,true)&&cast.Kind==SpellKind.Buff,"opt-in resolver classifies "+c);
    }
    unlocked=true;vitals.Restore(.5f);
    Check(runtime.Activate('걱',now),"regen activated");now+=10;runtime.Tick(now);
    Check(Mathf.Abs(vitals.Hp01-.53f)<.0001f,"10 seconds regenerate exactly 3% maximum HP");
    runtime.Activate('걱',now);now+=10;runtime.Tick(now);
    Check(Mathf.Abs(vitals.Hp01-.56f)<.0001f,"regen recast refreshes without doubling rate");
    runtime.Clear(now);now+=30;runtime.Tick(now);
    Check(Mathf.Abs(vitals.Hp01-.56f)<.0001f,"cleared regen does not leak after rest/death");
    vitals.Restore();runtime.Activate('먹',now);vitals.TakeAttackDamage(20,IncomingDamageKind.Ranged);
    Check(Mathf.Abs(vitals.Hp01-.83f)<.0001f,"ranged hit receives 15% reduction once");
    vitals.Restore();vitals.TakeAttackDamage(20,IncomingDamageKind.Melee);
    Check(Mathf.Abs(vitals.Hp01-.8f)<.0001f,"melee is not reduced");
    vitals.Restore();vitals.TakeDamage(20);
    Check(Mathf.Abs(vitals.Hp01-.8f)<.0001f,"unspecified legacy damage is not reduced");
    now+=31;vitals.Restore();vitals.TakeAttackDamage(20,IncomingDamageKind.Ranged);
    Check(Mathf.Abs(vitals.Hp01-.8f)<.0001f,"expired protection no longer reduces ranged damage");
    runtime.Clear(now);float before=runtime.HoldPower(8,now);runtime.Activate('석',now);
    Check(before<1&&runtime.HoldPower(8,now)==1,"sharpness adds grace without changing recognition input");
    Check(runtime.HoldPower(1000,now)==profile.MinimumHoldPower,"hold penalty has a floor");
    runtime.Activate('억',now);var config=ScriptableObject.CreateInstance<CombatConfigSO>();objects.Add(config);
    var ink=new InkPool(config,null);ink.Restore(1);Check(ink.TrySpend(.1f*runtime.CostScale(now))&&Mathf.Abs(ink.Value-.92f)<.0001f,"discount changes paid cost once");
    runtime.Activate('억',now);Check(Mathf.Abs(runtime.CostScale(now)-.8f)<.0001f,"cost discount does not stack on recast");
    runtime.Clear(now);Check(runtime.CostScale(now)==1,"clear restores normal costs");
    var targetGo=new GameObject("Buff258Target");objects.Add(targetGo);targetGo.transform.position=player.transform.position+Vector3.forward;
    var target=targetGo.AddComponent<EnemyVitals>();targetGo.AddComponent<CapsuleCollider>();targetGo.AddComponent<SphereCollider>();
    typeof(CombatLoopWiring).GetField("_enemies",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(wiring,new[]{target,target});
    typeof(CombatLoopWiring).GetMethod("CollectControllers",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(wiring,null);
    wiring.PlayerDamageScale=_=>1.5f;runtime.Activate('넉',now);wiring.PlayerDamageScale=_=>4f;
    float hp=target.Hp;now+=.5f;runtime.Tick(now);
    Check(Mathf.Abs(hp-target.Hp-1.5f)<.001f,"aura snapshots 1.5 scale and damages actor once despite duplicate entries/colliders");
    hp=target.Hp;runtime.Tick(now);Check(target.Hp==hp,"same time cannot deal aura twice");
    var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(wall);wall.transform.position=player.transform.position+new Vector3(0,.5f,.5f);wall.transform.localScale=new Vector3(3,3,.2f);Physics.SyncTransforms();
    now+=.5f;runtime.Tick(now);Check(target.Hp==hp,"opaque wall blocks aura");wall.SetActive(false);
    targetGo.transform.position=player.transform.position+Vector3.forward*3;Physics.SyncTransforms();now+=.5f;runtime.Tick(now);Check(target.Hp==hp,"aura does not reach beyond radius");
    targetGo.transform.position=player.transform.position+Vector3.forward;Physics.SyncTransforms();target.OpenWeakPoint();now+=.5f;runtime.Tick(now);
    Check(target.WeakPointElementMask==0,"persistent aura does not complete direct five-element sequence");
    vitals.ApplyFatalFall();now+=1;runtime.Tick(now);Check(!runtime.Active('넉',now)&&!runtime.Activate('걱',now)&&vitals.Hp01==0,"fatal environment bypasses defense and clears state; no resurrection");
    return string.Join("\n",results);
   }
   finally {runtime?.Dispose();foreach(var obj in objects)if(obj!=null)Object.DestroyImmediate(obj);}
  }
  public static string BuffLive258(string command)
  {
   var s=JourneySession258();var wiring=s.Walker.Wiring;var buff=wiring.EABuffs;
   void Check(bool ok,string message){File.AppendAllText(ContinuationOutput258+"/buff-live.txt",(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
   Check(buff!=null,"candidate profile owns live buff service");
   var ink=(InkPool)typeof(WorldMacroPlaytestSession).GetField("ink",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s);
   var hp=s.Walker.Body.GetComponent<PlayerVitals>();
   void Cast(char letter,float hold=1)=>typeof(CombatLoopWiring).GetMethod("OnLetterDrawn",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(wiring,new object[]{new DrawnLetter(letter,default,default,null,.6f,.6f,1,hold,3)});
   var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
   if(command.StartsWith("show:")&&command.Length==6)
   {
    Check(s.HasDemoGuk,"presentation review requires saved boss unlock");
    buff.Clear(Time.time);ink.Restore();hp.Restore(.65f);Cast(command[5]);
    return "Live player-owned presentation "+command[5]+" active="+buff.Active(command[5],Time.time);
   }
   if(command.StartsWith("capture:"))
   {
    string name=command.Substring(8);if(name.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'&&c!='_'))throw new ArgumentException("Capture label");
    ScreenCapture.CaptureScreenshot(Path.GetFullPath(ContinuationOutput258+"/"+name+".png"));return "Capture queued";
   }
   if(command=="locked")
   {
    Check(!s.HasDemoGuk&&!buff.Unlocked,"fresh saved giyeok remains locked");ink.Restore();Cast('걱');
    Check(!buff.Active('걱',Time.time),"central cast cannot bypass saved unlock");return "Locked candidate path checked";
   }
   if(command=="reload")
   {
    Check(s.HasDemoGuk&&buff.Unlocked,"restart retains actual boss unlock");
    Check("걱넉먹석억".All(c=>!buff.Active(c,Time.time)),"restart never revives temporary buffs");return "Reload checked";
   }
   if(command!="active")throw new ArgumentException(command);
   Check(s.HasDemoGuk&&buff.Unlocked,"successful boss save unlocks five buffs");
   buff.Clear(Time.time);hp.Restore(.5f);ink.Restore();
   int accepted=0;SpellCast last=default;void Accepted(SpellCast c,Vector3 p,Vector3 f){accepted++;last=c;}
   wiring.CastAccepted+=Accepted;
   try
   {
    foreach(char letter in "걱넉먹석억"){Cast(letter);Check(buff.Active(letter,Time.time),"central commit activates "+letter);Check(buff.HasVisual(letter),"player-owned buff presentation exists "+letter);}
    Check(accepted==5,"accepted-cast presentation/audio event once per buff");
    float left=buff.Remaining('걱',Time.time);ink.Restore(0);Cast('걱');
    Check(accepted==5&&Mathf.Abs(buff.Remaining('걱',Time.time)-left)<.01f,"insufficient ink cannot refresh buff or emit accepted event");
    buff.Clear(Time.time);ink.Restore();Cast('가');float basePower=last.Power;Cast('가',8);float held=last.Power;
    Cast('석');Cast('가',8);
    Check(basePower>0&&held<basePower&&Mathf.Abs(last.Power-basePower)<.001f,"central spell power applies hold loss and sharpness grace exactly once");
    Check(ApproachVillage("village_rest"),"candidate rest door reachable for buff persistence check");
    ink.Restore();Cast('걱');
    string save=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
    using(var locked=new FileStream(save+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {Check(!s.Interact("village_rest")&&buff.Active('걱',Time.time),"failed rest save preserves current buff");}
    Check(s.Interact("village_rest")&&"걱넉먹석억".All(c=>!buff.Active(c,Time.time)),"successful rest clears every temporary buff after save");
    Check("걱넉먹석억".All(c=>!buff.HasVisual(c)),"rest releases owned presentation instances");
    return "Live spell, unlock, cost, hold and rest transaction checks passed; wait for doorway return";
   }
   finally {wiring.CastAccepted-=Accepted;}
  }
 }
}
