using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
namespace Oheangbu.EditorTools.SpellVFX120
{
 [InitializeOnLoad] public static class KtpOriginalPlayAudit
 {
  const string Key="KtpOriginalPlayAudit";static bool setup,background;static float start;static Scene scene;
  static readonly List<Vfx120TraditionalMotif> effects=new List<Vfx120TraditionalMotif>();static readonly List<string> checks=new List<string>(),errors=new List<string>();static readonly HashSet<int> visible=new HashSet<int>();
  static Vfx120Effect bolt;static int initialEffects;static bool tails;static GameObject rig;
  [Serializable]class Report{public string status,mvid;public string[] checks,errors;public int visibleRoles;public bool detachedTail;public float seconds;}
  static KtpOriginalPlayAudit(){EditorApplication.update+=Tick;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Key,false)&&(t==LogType.Error||t==LogType.Exception))errors.Add(m);};}
  public static string Start(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play first");SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;return "PLAY_REQUESTED";}
  static void Need(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
  static void Tick()
  {
   if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
   try
   {
    if(!setup){Setup();setup=true;return;}
    for(int i=0;i<effects.Count;i++)if(effects[i]!=null&&effects[i].LiveParticleCount>0)visible.Add(i);
    if(Time.time-start>2&&bolt==null)tails|=UnityEngine.Object.FindObjectsByType<Vfx120TraditionalMotif>(FindObjectsSortMode.None).Any(x=>x.DestroyHostOnCompletion&&!x.IsComplete);
    if(Time.time-start<13)return;
    Need(visible.Count==20,"All twenty original role sources emitted in native Update");Need(tails,"Original cast tail survived short spell root");Need(bolt==null,"Short spell root kept its original lifetime");Need(effects.All(x=>x==null||x.IsComplete),"All native role sources ended");Need(!UnityEngine.Object.FindObjectsByType<Vfx120TraditionalMotif>(FindObjectsSortMode.None).Any(x=>x.DestroyHostOnCompletion),"Detached hosts destroyed after original tail");
    rig.SetActive(false);Finish();
   }
   catch(Exception e){errors.Add(e.ToString());Finish();}
  }
  static void Setup()
  {
   checks.Clear();errors.Clear();visible.Clear();effects.Clear();background=Application.runInBackground;Application.runInBackground=true;
   for(int i=0;i<SceneManager.sceneCount;i++)foreach(var root in SceneManager.GetSceneAt(i).GetRootGameObjects())root.SetActive(false);
   scene=SceneManager.CreateScene("OriginalKtp_IsolatedAudit");start=Time.time;
   for(int family=0;family<5;family++)for(int role=0;role<4;role++)
   {
    var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/KtpOriginal/Original_"+family+"_"+role+".prefab");
    var host=new GameObject("OriginalRole");SceneManager.MoveGameObjectToScene(host,scene);var fx=host.AddComponent<Vfx120TraditionalMotif>();var settings=Vfx120TraditionalMotif.Settings.DefaultFor((Vfx120TraditionalMotif.Role)role);settings.PreserveAuthored=true;
    Need(fx.Configure(source,new Color(1,.3f,.1f),Color.black,(Vfx120TraditionalMotif.Role)role,settings),"Configured original "+family+"/"+role+":"+fx.Diagnostic);Need(fx.ParticleBudget==fx.SourceParticleCapacity,"Unreduced capacity "+family+"/"+role);effects.Add(fx);
   }
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var profile=catalog.Entries.Single(e=>e.Glyph=="나").Profile;var go=new GameObject("ShortSpell");SceneManager.MoveGameObjectToScene(go,scene);bolt=go.AddComponent<Vfx120Effect>();bolt.Profile=profile;bolt.Begin(Vector3.zero,null,Vector3.forward*3,Color.white);Need(bolt.NativeImpact==null,"No initial impact without confirmed hit");
   rig=new GameObject("EnemyContactAudit");SceneManager.MoveGameObjectToScene(rig,scene);var wiring=rig.AddComponent<CombatLoopWiring>();var cp=Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath);Need(cp!=null,"Original contact resource present");Set(wiring,"_contactVfx",cp);
   var enemyGo=new GameObject("Target");SceneManager.MoveGameObjectToScene(enemyGo,scene);var enemy=enemyGo.AddComponent<EnemyVitals>();
   var pendingType=typeof(CombatLoopWiring).GetNestedType("PendingCast",BindingFlags.NonPublic);var list=(System.Collections.IList)typeof(CombatLoopWiring).GetField("_pendingCasts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(wiring);
   Action<float> schedule=power=>{var item=Activator.CreateInstance(pendingType);pendingType.GetField("Target").SetValue(item,enemy);pendingType.GetField("Power").SetValue(item,power);pendingType.GetField("ImpactTime").SetValue(item,Time.time);list.Add(item);typeof(CombatLoopWiring).GetMethod("TickPendingCasts",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(wiring,null);};
   initialEffects=UnityEngine.Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None).Length;schedule(0);Need(UnityEngine.Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None).Length==initialEffects,"Zero enemy damage creates no impact");schedule(5);Need(UnityEngine.Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None).Length==initialEffects+1,"Confirmed enemy damage creates one original impact");schedule(10000);Need(!enemy.IsAlive&&UnityEngine.Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None).Length==initialEffects+2,"Lethal enemy hit emits once");schedule(5);Need(UnityEngine.Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None).Length==initialEffects+2,"Dead target emits no further hit");
  }
  static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
  static void Finish(){File.WriteAllText(Path.Combine(Vfx120Editor.Output,"original_play_audit.json"),JsonUtility.ToJson(new Report{status=errors.Count==0?"PASS_TECHNICAL_ONLY":"FAIL",mvid=typeof(Vfx120Effect).Module.ModuleVersionId.ToString(),checks=checks.ToArray(),errors=errors.ToArray(),visibleRoles=visible.Count,detachedTail=tails,seconds=Time.time-start},true));Application.runInBackground=background;SessionState.SetBool(Key,false);setup=false;EditorApplication.isPlaying=false;}
 }
}
