using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string GiyeokBuild258()
  {
   var scene=FrontageScene249();var session=VillageSession();string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Continuation258";
   Directory.CreateDirectory(folder);string path=folder+"/EAGiyeok_TEST.asset";var profile=AssetDatabase.LoadAssetAtPath<EAGiyeokProfileSO>(path);
   if(profile==null){profile=ScriptableObject.CreateInstance<EAGiyeokProfileSO>();AssetDatabase.CreateAsset(profile,path);}
   var visuals=AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>("Assets/_Project/Art/SpellVFX120/Data/SpellVisualSet_120.asset");
   if(!visuals.TryGet('각',out var root)||!visuals.TryGet('낙',out var burn))throw new Exception("Missing giyeok art");
   profile.RootPrefab=root.FxPrefab;profile.BurnPrefab=burn.FxPrefab;session.EAGiyeokProfile=profile;
   EditorUtility.SetDirty(profile);EditorUtility.SetDirty(session);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "First two giyeok attacks connected behind saved boss unlock; other eight still outstanding";
  }
  public static string GiyeokChecks258()
  {
   JourneySession258();var output=new List<string>();var objects=new List<Object>();
   void Check(bool ok,string message){output.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllText(ContinuationOutput258+"/giyeok-contracts.txt",string.Join("\n",output));if(!ok)throw new Exception(message);}
   var profile=ScriptableObject.CreateInstance<EAGiyeokProfileSO>();objects.Add(profile);
   var player=new GameObject("GiyeokTest258");objects.Add(player);player.transform.position=new Vector3(-12000,500,-12000);
   var hp=player.AddComponent<PlayerVitals>();var w=player.AddComponent<CombatLoopWiring>();
   typeof(CombatLoopWiring).GetField("_contactVfx",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(w,null);
   typeof(CombatLoopWiring).GetField("_environmentOcclusion",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(w,true);
   var targetGo=new GameObject("RootTarget258");objects.Add(targetGo);targetGo.transform.position=player.transform.position+Vector3.forward*3;
   var target=targetGo.AddComponent<EnemyVitals>();targetGo.AddComponent<CapsuleCollider>();
   var enemy=targetGo.AddComponent<EnemyController>();
   typeof(CombatLoopWiring).GetField("_enemies",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(w,new[]{target});
   typeof(CombatLoopWiring).GetMethod("CollectControllers",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,null);
   bool unlocked=false;var runtime=new EAGiyeokRuntime(profile,w,hp,()=>unlocked);float now=Time.time;
   SpellCast Spell(char c)=>new SpellCast(c,SpellKind.AttackSingle,c=='각'?Element.Wood:Element.Fire,4,default,1);
   bool Cast(char c)=>runtime.CastSpell(Spell(c),target,player.transform.position+Vector3.up*.4f,target.transform.position,.5f,now);
   try
   {
    var resolver=new SpellResolver(AssetDatabase.LoadAssetAtPath<SpellBookSO>("Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset"));
    foreach(char c in "각낙")
    {
     var letter=new DrawnLetter(c,default,default,null,.6f,.6f,1,1,3);
     Check(!resolver.TryResolve(letter,out _,false,true,true),"other opt-ins cannot unlock "+c);
     Check(resolver.TryResolve(letter,out var cast,false,false,false,true)&&cast.Power>0&&cast.Kind==SpellKind.AttackSingle,"giyeok resolver uses base brush law for "+c);
     Check(!Cast(c),"locked runtime rejects "+c);
    }
    unlocked=true;Check(Cast('각'),"root schedules after unlock");float before=target.Hp;
    runtime.Tick(now+.49f);Check(target.Hp==before&&!target.Control.BlocksActions(now+.49f),"root waits for actual impact clock");
    now+=.5f;runtime.Tick(now);Check(Mathf.Abs(before-target.Hp-4)<.001f&&target.Control.BlocksActions(now)&&target.Control.MovementScale(now)==0,"field root applies initial hit and stops actions/movement");
    Check(!target.WeakPointActive&&target.DamageMultiplier==1,"root never masquerades as stagger or damage amplification");
    before=target.Hp;target.TakeDamage(2);Check(target.Hp==before-2&&target.Control.BlocksActions(now+1),"hitting a root neither breaks it nor adds shatter damage");
    now+=profile.RootDuration;runtime.Tick(now);Check(!target.Control.BlocksActions(now)&&target.Control.MovementScale(now)==1,"root expires solely on duration");
    var boss=targetGo.AddComponent<CheongryongCombatController>();target.Restore();Cast('각');now+=.5f;runtime.Tick(now);
    Check(!target.Control.BlocksActions(now)&&Mathf.Abs(target.Control.MovementScale(now)-profile.BossRootSpeed)<.001f,"boss root slows movement without blocking actions");
    runtime.Clear();Object.DestroyImmediate(boss);Check(target.Control.MovementScale(now)==1,"owner clear removes its root modifier");
    // #308 WP-00: the boss rule reads EnemyVitals.IsBoss (data). A target with an IsBoss profile and neither boss controller is slowed, not bound.
    var bossProfile308=ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();objects.Add(bossProfile308);
    typeof(EnemyVitalsProfileSO).GetField("_isBoss",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bossProfile308,true);
    target.ConfigureProfile(bossProfile308);target.Restore();Cast('각');now+=.5f;runtime.Tick(now);
    Check(target.IsBoss&&!target.Control.BlocksActions(now)&&Mathf.Abs(target.Control.MovementScale(now)-profile.BossRootSpeed)<.001f,"IsBoss profile target: root slows movement without blocking actions (#308)");
    runtime.Clear();target.ConfigureProfile(null);target.Restore();Check(!target.IsBoss&&target.Control.MovementScale(now)==1,"profile removed: ordinary target again (#308)");
    target.Restore();w.PlayerDamageScale=_=>1.5f;Cast('낙');w.PlayerDamageScale=_=>4f;before=target.Hp;now+=.5f;runtime.Tick(now);
    Check(Mathf.Abs(before-target.Hp-6)<.001f,"burn initial hit uses cast-time gear snapshot");before=target.Hp;now+=1;runtime.Tick(now);
    Check(Mathf.Abs(before-target.Hp-1.5f)<.001f,"burn ticks attached to target at snapshot power");
    before=target.Hp;runtime.Tick(now);Check(target.Hp==before,"same tick cannot duplicate burn damage");
    targetGo.transform.position+=Vector3.right*4;Physics.SyncTransforms();now+=1;runtime.Tick(now);
    Check(target.Hp<before,"burn follows moving target");
    target.OpenWeakPoint();before=target.WeakPointElementMask;now+=.5f;runtime.Tick(now);Check(target.WeakPointElementMask==before,"burn ticks cannot complete direct five-element sequence");
    target.Restore();before=target.Hp;now+=1;runtime.Tick(now);Check(target.Hp==before&&runtime.ActiveCount==0,"new life revision never inherits burn");
    targetGo.transform.position=player.transform.position+Vector3.forward*3;w.PlayerDamageScale=null;
    var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(wall);wall.transform.position=player.transform.position+new Vector3(0,1,1.5f);wall.transform.localScale=new Vector3(3,3,.2f);Physics.SyncTransforms();
    Cast('각');before=target.Hp;now+=.5f;runtime.Tick(now);Check(target.Hp==before&&!target.Control.BlocksActions(now),"wall before impact cancels damage and root");wall.SetActive(false);
    Cast('낙');target.TakeDamage(float.MaxValue);now+=.5f;runtime.Tick(now);Check(runtime.ActiveCount==0,"dead target produces no secondary effect");
    target.Restore();Cast('각');now+=.5f;runtime.Tick(now);hp.ApplyFatalFall();runtime.Tick(now+.1f);
    Check(runtime.ActiveCount==0&&!target.Control.BlocksActions(now+.1f),"caster death clears owned effects");
    return string.Join("\n",output);
   }
   finally{runtime.Dispose();foreach(var o in objects)if(o!=null)Object.DestroyImmediate(o);}
  }
 }
}
