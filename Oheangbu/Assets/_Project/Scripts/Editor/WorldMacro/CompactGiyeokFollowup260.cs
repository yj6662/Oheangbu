using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string GiyeokFollowupBuild260()
  {
   var scene=FrontageScene249();var session=VillageSession();var profile=session.EAGiyeokProfile;
   if(profile==null)throw new Exception("Candidate giyeok profile required");
   string backup=ContinuationOutput258+"/Followup260Before";Directory.CreateDirectory(backup);
   var path=AssetDatabase.GetAssetPath(profile);if(!File.Exists(backup+"/EAGiyeok_TEST.asset"))File.Copy(path,backup+"/EAGiyeok_TEST.asset");
   var visuals=AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>("Assets/_Project/Art/SpellVFX120/Data/SpellVisualSet_120.asset");
   if(!visuals.TryGet('삭',out var pierce)||!visuals.TryGet('악',out var bounce))throw new Exception("Missing followup art");
   profile.PiercePrefab=pierce.FxPrefab;profile.ReturnPrefab=bounce.FxPrefab;
   profile.PierceRange=18;profile.PierceRadius=.45f;profile.ReturnDelay=.65f;profile.ReturnReach=2;
   EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();return "Candidate opts into distinct pierce and same-target return; six giyeok attacks still outstanding";
  }
  public static string GiyeokFollowupChecks260()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit preview only");
   var scene=EditorSceneManager.NewPreviewScene();var result=new List<string>();var profile=ScriptableObject.CreateInstance<EAGiyeokProfileSO>();EAGiyeokRuntime runtime=null;
   void C(bool ok,string label){result.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllText(ContinuationOutput258+"/giyeok-followup260.txt",string.Join("\n",result));if(!ok)throw new Exception(label);}
   GameObject Make(string name,Vector3 p){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=p;return go;}
   void Set(object o,string f,object v)=>o.GetType().GetField(f,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);
   var origin=new Vector3(3000,3000,3000);var player=Make("TraceOwner260",origin);var hp=player.AddComponent<PlayerVitals>();hp.Restore();var wiring=player.AddComponent<CombatLoopWiring>();
   Set(wiring,"_contactVfx",null);Set(wiring,"_environmentOcclusion",true);
   EnemyVitals Enemy(string n,Vector3 p){var e=Make(n,p).AddComponent<EnemyVitals>();e.gameObject.AddComponent<CapsuleCollider>();e.Restore();return e;}
   var a=Enemy("Front",origin+Vector3.forward*3);var b=Enemy("Behind",origin+Vector3.forward*6);var c=Enemy("Side",origin+new Vector3(2,0,4));
   a.gameObject.AddComponent<BoxCollider>();Set(wiring,"_enemies",new[]{a,b,c});typeof(CombatLoopWiring).GetMethod("CollectControllers",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(wiring,null);
   bool unlocked=false;runtime=new EAGiyeokRuntime(profile,wiring,hp,()=>unlocked);float now=10;
   SpellCast Spell(char ch)=>new SpellCast(ch,SpellKind.AttackSingle,ch=='삭'?Element.Metal:Element.Water,4,default,1);
   bool Cast(char ch,EnemyVitals target=null)=>runtime.CastSpell(Spell(ch),target??a,origin+Vector3.up*.4f,a.transform.position+Vector3.up*.4f,1,now);
   void Sync(){Physics.SyncTransforms();var physics=scene.GetPhysicsScene();if(physics.IsValid()&&!physics.Equals(Physics.defaultPhysicsScene))physics.Simulate(.001f);}
   void Reset(){runtime.Clear();hp.Restore();a.Restore();b.Restore();c.Restore();a.transform.position=origin+Vector3.forward*3;b.transform.position=origin+Vector3.forward*6;wiring.PlayerDamageScale=null;now+=10;Sync();}
   try
   {
    var resolver=new SpellResolver(AssetDatabase.LoadAssetAtPath<SpellBookSO>("Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset"));
    foreach(char ch in "삭악")
    {var letter=new DrawnLetter(ch,default,default,null,.6f,.6f,1,1,3);C(!resolver.TryResolve(letter,out _,true,true,true),"other opt-ins cannot unlock "+ch);C(resolver.TryResolve(letter,out var cast,false,false,false,true)&&cast.Power>0&&cast.Kind==SpellKind.AttackSingle,"base brush law preserved "+ch);C(!Cast(ch),"saved unlock required "+ch);}
    unlocked=true;Sync();
    runtime.CastSpell(new SpellCast('각',SpellKind.AttackSingle,Element.Wood,4,default,1),a,origin+Vector3.up*.4f,a.transform.position,1,now);
    runtime.Tick(now+1);C(a.Hp==a.MaxHp-4&&a.Control.BlocksActions(now+1),"existing root still reaches owned-scene target");
    runtime.Clear();C(!a.Control.BlocksActions(now+1),"root owner cleanup remains isolated");a.Restore();
    runtime.CastSpell(new SpellCast('낙',SpellKind.AttackSingle,Element.Fire,4,default,1),a,origin+Vector3.up*.4f,a.transform.position,1,now);
    runtime.Tick(now+2);C(a.Hp==a.MaxHp-5,"existing burn keeps initial and one-second periodic damage");
    Reset();C(Cast('삭'),"pierce accepted");runtime.Tick(now+.8f);C(a.Hp==a.MaxHp&&b.Hp==b.MaxHp,"no damage ahead of projectile front");
    runtime.Tick(now+1.1f);C(a.Hp==a.MaxHp-4&&b.Hp==b.MaxHp,"first target hit once despite multiple colliders");
    runtime.Tick(now+2.1f);C(b.Hp==b.MaxHp-4&&c.Hp==c.MaxHp,"rear target pierced and off-axis actor spared");
    runtime.Tick(now+2.1f);C(a.Hp==a.MaxHp-4&&b.Hp==b.MaxHp-4,"repeated tick never repeats a pierce hit");
    runtime.Tick(now+6.1f);C(runtime.ActiveCount==0,"pierce retires at configured range");
    Reset();Cast('삭');a.transform.position+=Vector3.right*2;Sync();runtime.Tick(now+1.1f);C(a.Hp==a.MaxHp,"moving sideways avoids fixed line");
    Reset();Cast('삭');b.Restore();runtime.Tick(now+2.1f);C(b.Hp==b.MaxHp,"new life cannot inherit pending pierce");
    Reset();var wall=Make("PhysicalWall",origin+new Vector3(0,1,4.5f));var box=wall.AddComponent<BoxCollider>();box.size=new Vector3(3,4,.3f);Sync();Cast('삭');runtime.Tick(now+6.1f);
    C(a.Hp==a.MaxHp-4&&b.Hp==b.MaxHp&&runtime.ActiveCount==0,"world wall stops penetration after front actor");wall.SetActive(false);Sync();
    Reset();wiring.PlayerDamageScale=_=>1.5f;Cast('악');wiring.PlayerDamageScale=_=>4;runtime.Tick(now+1.01f);C(a.Hp==a.MaxHp-6&&b.Hp==b.MaxHp,"return first impact uses captured power");
    a.transform.position+=Vector3.right*.2f;Sync();runtime.Tick(now+1+profile.ReturnDelay);C(a.Hp==a.MaxHp-12&&b.Hp==b.MaxHp&&runtime.ActiveCount==0,"second impact returns to same moving actor at captured power");
    runtime.Tick(now+5);C(a.Hp==a.MaxHp-12,"return cannot produce third hit");
    Reset();Cast('악');runtime.Tick(now+1.01f);a.Restore();runtime.Tick(now+2);C(a.Hp==a.MaxHp&&runtime.ActiveCount==0,"respawn cancels return instead of changing target");
    Reset();Cast('악');a.TakeDamage(float.MaxValue);runtime.Tick(now+2);C(runtime.ActiveCount==0&&b.Hp==b.MaxHp,"dead first target gives no secondary hit");
    Reset();wall.transform.position=origin+new Vector3(0,1,1.5f);wall.SetActive(true);Sync();Cast('악');runtime.Tick(now+2);C(a.Hp==a.MaxHp&&runtime.ActiveCount==0,"blocked first hit cannot launch return");wall.SetActive(false);Sync();
    Reset();Cast('악');runtime.Tick(now+1.01f);wall.transform.position=origin+new Vector3(0,1,4);wall.SetActive(true);Sync();runtime.Tick(now+2);C(a.Hp==a.MaxHp-4,"new wall blocks return path");wall.SetActive(false);Sync();
    Reset();Cast('삭');Cast('악');runtime.Clear();runtime.Tick(now+10);C(a.Hp==a.MaxHp&&b.Hp==b.MaxHp&&runtime.ActiveCount==0,"owner clear cancels both pending verbs");
    Reset();Cast('삭');hp.ApplyFatalFall();runtime.Tick(now+10);C(a.Hp==a.MaxHp&&runtime.ActiveCount==0,"caster death cancels pending line");
    // Exercise the production resolver/ink/accepted-event path without Play or input devices.
    Reset();runtime.Dispose();var config=ScriptableObject.CreateInstance<CombatConfigSO>();
    try
    {
     var ink=new InkPool(config,null);wiring.Construct(resolver,new ParryJudge(config),new GroggyMeter(config),ink);
     Set(wiring,"_config",config);Set(wiring,"_playerVitals",hp);Set(wiring,"_playerTransform",player.transform);
     var locking=player.AddComponent<LockOn>();Set(locking,"_target",a);Set(locking,"_locked",true);Set(wiring,"_lockOn",locking);
     wiring.ConfigureEAGiyeok(profile,()=>unlocked);runtime=wiring.EAGiyeok;int accepted=0;
     wiring.CastAccepted+=(cast,position,forward)=>accepted++;
     var draw=typeof(CombatLoopWiring).GetMethod("OnLetterDrawn",BindingFlags.Instance|BindingFlags.NonPublic);
     void Draw(char ch)=>draw.Invoke(wiring,new object[]{new DrawnLetter(ch,default,default,null,.6f,.6f,1,1,3)});
     foreach(char ch in "삭악")
     {
      runtime.Clear();a.Restore();b.Restore();unlocked=false;ink.Restore();int beforeAccepted=accepted;Draw(ch);
      C(accepted==beforeAccepted&&runtime.ActiveCount==0,"central resolver rejects locked "+ch);
      unlocked=true;ink.Restore();float beforeInk=ink.Value;Draw(ch);
      C(accepted==beforeAccepted+1&&runtime.ActiveCount==1&&Mathf.Abs(beforeInk-ink.Value-config.SpellInkCost)<.0001f,"central cast spends one configured cost and publishes once "+ch);
      ink.Restore(0);Draw(ch);C(accepted==beforeAccepted+1&&runtime.ActiveCount==1,"insufficient ink keeps pending cast without duplicate event "+ch);
      float beforeHp=a.Hp;runtime.Tick(Time.time+100);C(a.Hp<beforeHp,"central cast reaches real registered target "+ch);
     }
    }
    finally{runtime.Clear();Object.DestroyImmediate(config);}
    return string.Join("\n",result);
   }
   finally{runtime?.Dispose();EditorSceneManager.ClosePreviewScene(scene);Object.DestroyImmediate(profile);}
  }
 }
}
