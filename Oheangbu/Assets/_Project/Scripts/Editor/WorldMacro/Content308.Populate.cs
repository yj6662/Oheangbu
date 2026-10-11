using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-63 - field enemies in all five realms and name-only sub NPC spots [TEST]. Data: Tools/Art/populate308.py ->
 // Art/World/Compact/Rebuild/Populate308/plan.json (what and how many: Tools/Unity/Stage308_populate/populate308.json).
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Content308 Populate "<command>"
 //   status   counts (read only)
 //   apply    enemies: each row = a copy of its template row in the content asset (Id / ContentId / Feet / Patrol replaced) + a clone of the
 //            template ACTOR under Populate308/Actors, added to session.Actors and CombatLoopWiring._enemies. A row without loaded NavMesh
 //            within nav_radius_m, or whose template is not in the scene, is left out and listed (the session throws at start on an actor
 //            off the NavMesh). The body, attacks and numbers are the template's: a stand-in until the realm's own model exists.
 //            sub NPCs: empty EditorOnly objects under Populate308/SubNpcSpots/<Realm>, named "<이름> (<직업>)". No body, no content row,
 //            nothing at run time: a place and a name for later.
 //   revert   removes exactly that: the root, the session / wiring links and the content rows whose Id starts with "field308/"
 //   check    one actor per field308 row, linked and wired, on the NavMesh; no field308 actor without a row
 // Only the main play scene and its content asset are written, both backed up first (Tools/Unity/Stage308_populate/Backup). No dialog.
 public static partial class Content308
 {
  const string PopRoot308="Populate308",PopPrefix308="field308/",PopContent308="Assets/_Project/Scenes/World/Main/WorldContent_Main.asset";
  [Serializable] sealed class PopEnemy308{public string id="",name="",contentId="",template="",realm="",group="";public float[] feet=Array.Empty<float>(),patrol2=Array.Empty<float>();}
  [Serializable] sealed class PopNpc308{public string id="",name="",job="",realm="";public float[] pos=Array.Empty<float>();}
  [Serializable] sealed class PopPatrol308{public float walk_speed_mps=1.7f,pause_min_s=1.5f,pause_max_s=4f;}
  [Serializable] sealed class PopIdle308{public float breath_period_s=4.2f,breath_chest_deg=2.6f,breath_shoulder_m=.013f,sway_deg=.9f,combat_leg_deg=11f,combat_arm_deg=17f,combat_lean_deg=5f,combat_range_m=26f;}
  [Serializable] sealed class PopCfg308{public PopPatrol308 patrol=new PopPatrol308();public PopIdle308 idle=new PopIdle308();}
  [Serializable] sealed class PopPlan308{public string version="";public float nav_radius_m=2f;public PopEnemy308[] enemies=Array.Empty<PopEnemy308>();public PopNpc308[] npcs=Array.Empty<PopNpc308>();}
  static string PopStage308=>Path.Combine(Harness303.RepoRoot,"Tools","Unity","Stage308_populate");
  static PopPlan308 PopData308()
  {
   string f=Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Populate308","plan.json");
   if(!File.Exists(f))throw new Refuse308("no "+f+" (run python Tools/Art/populate308.py Tools/Unity/Stage308_populate/populate308.json)");
   return JsonUtility.FromJson<PopPlan308>(File.ReadAllText(f));
  }
  static Vector3 PopV308(float[] a)=>new Vector3(a[0],a[1],a[2]);

  public static string Populate(string command)
  {
   string c=(command??"").Trim();
   try
   {
    var scene=SceneManager.GetActiveScene();if(scene.name!="W_Demo_Main")return "REFUSED open W_Demo_Main first (active scene is "+scene.name+")";
    var session=Saved308<WorldMacroPlaytestSession>(scene).FirstOrDefault();if(session==null)return "REFUSED no WorldMacroPlaytestSession in the play scene";
    var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(PopContent308);if(content==null||session.Content!=content)return "REFUSED the session does not read "+PopContent308;
    if(c=="status")return PopStatus308(scene,session,content);
    if(c=="check")return PopCheck308(scene,session,content);
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)return "REFUSED scripts are compiling or failed to compile";
    if(scene.isDirty)return "REFUSED the play scene has unsaved changes";
    if(c=="finals:on"||c=="finals:off")      // D308-53's presentation switch on the content asset: every final counts as owned for the session (nothing is written to a save)
    {
     bool before=content.DemoAllFinals;content.DemoAllFinals=c=="finals:on";EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
     return "Content308.Populate "+c+": DemoAllFinals "+before+" -> "+content.DemoAllFinals+" in "+PopContent308;
    }
    if(c=="idle")      // D308-67: the player's idle numbers -> the profile asset; the lean component learns its session (combat idle)
    {
     var ic=JsonUtility.FromJson<PopCfg308>(File.ReadAllText(Path.Combine(PopStage308,"populate308.json"))).idle;int n=0;
     foreach(var lean in Saved308<WorldMacroPlayerLean303>(scene))
     {
      lean.Session=session;EditorUtility.SetDirty(lean);n++;var pf=lean.Profile;if(pf==null)continue;
      pf.BreathPeriodSeconds=ic.breath_period_s;pf.BreathChestPitchDegrees=ic.breath_chest_deg;pf.BreathShoulderRiseMeters=ic.breath_shoulder_m;pf.IdleSwayDegrees=ic.sway_deg;
      pf.CombatLegSpreadDegrees=ic.combat_leg_deg;pf.CombatArmSpreadDegrees=ic.combat_arm_deg;pf.CombatLeanDegrees=ic.combat_lean_deg;pf.CombatRangeMeters=ic.combat_range_m;EditorUtility.SetDirty(pf);AssetDatabase.SaveAssetIfDirty(pf);
     }
     EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
     return "Content308.Populate idle: "+n+" lean components wired to the session, profile numbers written (breath "+ic.breath_chest_deg+" deg / "+ic.breath_shoulder_m+" m, combat legs "+ic.combat_leg_deg+" deg, arms "+ic.combat_arm_deg+" deg)";
    }
    if(c=="patrol"||c=="patrol:revert")      // D308-66: every ordinary actor (no boss controller) walks its patrol and pauses at the points
    {
     var pc=JsonUtility.FromJson<PopCfg308>(File.ReadAllText(Path.Combine(PopStage308,"populate308.json"))).patrol;int n=0;
     foreach(var a in Saved308<PrologueEncounter>(scene))
     {
      if(a.GetComponent<Oheangbu.App.Demo.CheongryongCombatController>()!=null||a.GetComponent<Oheangbu.App.Demo.SouthGateGeneralController>()!=null||a.GetComponent<Oheangbu.App.Demo.MineBossController>()!=null)continue;
      bool on=c=="patrol";a.PatrolSpeedScale=on?Mathf.Clamp(pc.walk_speed_mps/Mathf.Max(.1f,a.Speed),.1f,1f):1f;a.PatrolPauseMin=on?pc.pause_min_s:0f;a.PatrolPauseMax=on?pc.pause_max_s:0f;EditorUtility.SetDirty(a);n++;
     }
     EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
     return "Content308.Populate "+c+": "+n+" actors (walk "+pc.walk_speed_mps+" m/s, pause "+pc.pause_min_s+"-"+pc.pause_max_s+" s)";
    }
    if(c!="apply"&&c!="revert")return "REFUSED unknown Content308.Populate command '"+c+"' (status | apply | revert | check | patrol | patrol:revert)";
    string dir=Path.Combine(PopStage308,"Backup",DateTime.Now.ToString("yyyyMMddTHHmmss")+"_"+c);Directory.CreateDirectory(dir);
    File.Copy(Path.GetFullPath(scene.path),Path.Combine(dir,Path.GetFileName(scene.path)),true);File.Copy(Path.GetFullPath(PopContent308),Path.Combine(dir,Path.GetFileName(PopContent308)),true);
    var sb=new StringBuilder("Content308.Populate "+c+", backup "+dir);
    sb.Append("\n  removed first: ").Append(PopRemove308(scene,session,content));
    if(c=="apply")PopApply308(scene,session,content,sb);
    EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);EditorUtility.SetDirty(session);
    EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
    return sb.Append('\n').Append(PopCheck308(scene,session,content)).ToString();
   }
   catch(Refuse308 r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
  }

  static string PopRemove308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   int actors=0,rows=0;
   foreach(var a in (session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)).ToArray()){SetWired308(session,a.GetComponent<EnemyVitals>(),false);actors++;}
   session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&!(a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))).ToArray();
   var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==PopRoot308);if(old!=null)Object.DestroyImmediate(old);
   var keep=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&!(e.Id!=null&&e.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))).ToArray();
   rows=(content.Encounters?.Length??0)-keep.Length;content.Encounters=keep;
   return actors+" actors, "+rows+" content rows"+(old!=null?", root":"");
  }

  static void PopApply308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,StringBuilder sb)
  {
   var data=PopData308();var root=new GameObject(PopRoot308);SceneManager.MoveGameObjectToScene(root,scene);
   var actorsHome=new GameObject("Actors").transform;actorsHome.SetParent(root.transform,false);var spots=new GameObject("SubNpcSpots").transform;spots.SetParent(root.transform,false);
   var all=Saved308<PrologueEncounter>(scene).ToArray();var rows=new List<WorldMacroPlaytestSO.Encounter>(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>());
   var added=new List<PrologueEncounter>();int placed=0;var left=new List<string>();var perRealm=new Dictionary<string,int>();
   foreach(var e in data.enemies)
   {
    var template=all.FirstOrDefault(x=>x.Id==e.template);var trow=Encounter308(content,e.template);
    if(template==null||trow==null){left.Add(e.id+" (template "+e.template+(template==null?" has no actor in the scene":" has no content row")+")");continue;}
    if(!NavMesh.SamplePosition(PopV308(e.feet),out var hit,data.nav_radius_m,NavMesh.AllAreas)){left.Add(e.id+" (no NavMesh within "+data.nav_radius_m+" m)");continue;}
    var feet=hit.position;var p2=NavMesh.SamplePosition(PopV308(e.patrol2),out var hit2,data.nav_radius_m*2f,NavMesh.AllAreas)?hit2.position:feet;
    rows.Add(new WorldMacroPlaytestSO.Encounter{Id=e.id,ContentId=e.contentId,Feet=feet,Patrol=new[]{feet,p2},Ranged=trow.Ranged,RespawnOnRest=true,Detection=trow.Detection,Leash=trow.Leash,Speed=trow.Speed,Activation=trow.Activation});
    var agent=template.GetComponent<NavMeshAgent>();var at=feet+Vector3.up*(agent!=null?agent.baseOffset:0f);
    var go=Object.Instantiate(template.gameObject);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(actorsHome,true);go.name=e.id.Substring(PopPrefix308.Length)+" ["+e.name+"]";
    go.transform.localScale=template.transform.lossyScale;go.transform.SetPositionAndRotation(at,Quaternion.Euler(0f,Mathf.Atan2(p2.x-feet.x,p2.z-feet.z)*Mathf.Rad2Deg,0f));go.SetActive(template.gameObject.activeSelf);
    var actor=go.GetComponent<PrologueEncounter>();actor.Id=e.id;actor.Ranged=trow.Ranged;actor.DetectionRange=trow.Detection;actor.Leash=trow.Leash;actor.Speed=trow.Speed;actor.PatrolPoints=new[]{feet,p2};
    added.Add(actor);SetWired308(session,go.GetComponent<EnemyVitals>(),true);placed++;perRealm[e.realm]=perRealm.TryGetValue(e.realm,out int n)?n+1:1;
   }
   content.Encounters=rows.ToArray();session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Concat(added).ToArray();
   int npcs=0;
   foreach(var p in data.npcs)
   {
    var home=spots.Find(p.realm);if(home==null){home=new GameObject(p.realm).transform;home.SetParent(spots,false);}
    var g=new GameObject(p.name+" ("+p.job+")");g.transform.SetParent(home,false);g.transform.position=PopV308(p.pos);g.tag="EditorOnly";npcs++;
   }
   sb.Append("\n  enemies placed ").Append(placed).Append(" of ").Append(data.enemies.Length).Append(" (").Append(string.Join(", ",perRealm.OrderBy(k=>k.Key).Select(k=>k.Key+" "+k.Value))).Append("), sub NPC spots ").Append(npcs);
   foreach(string s in left)sb.Append("\n  LEFT OUT ").Append(s);
  }

  static string PopStatus308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==PopRoot308);var spots=root!=null?root.transform.Find("SubNpcSpots"):null;
   return "Content308.Populate status: content rows "+(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Count(e=>e!=null&&e.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))
    +", actors "+(session.Actors??Array.Empty<PrologueEncounter>()).Count(a=>a!=null&&a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))+", sub NPC spots "+(spots!=null?spots.Cast<Transform>().Sum(t=>t.childCount):0)+", root "+(root!=null?"present":"absent");
  }

  static string PopCheck308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var sb=new StringBuilder("Content308.Populate check\n");int pass=0,fail=0;void C(bool ok,string text){sb.Append(ok?"  [PASS] ":"  [FAIL] ").Append(text).Append('\n');if(ok)pass++;else fail++;}
   var rows=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&e.Id!=null&&e.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)).ToArray();
   var actors=Saved308<PrologueEncounter>(scene).Where(a=>a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)).ToArray();
   C(rows.Select(r=>r.Id).Distinct().Count()==rows.Length&&rows.All(r=>actors.Count(a=>a.Id==r.Id)==1)&&actors.All(a=>rows.Any(r=>r.Id==a.Id)),"one actor per field row and no actor without a row ("+rows.Length+" rows, "+actors.Length+" actors)");
   C(actors.All(a=>(session.Actors??Array.Empty<PrologueEncounter>()).Contains(a)&&Wired308(session,a.GetComponent<EnemyVitals>())),"every field actor is in session.Actors and wired for combat");
   int off=actors.Count(a=>{var ag=a.GetComponent<NavMeshAgent>();return ag==null||a.GetComponent<EnemyVitals>()==null||!NavMesh.SamplePosition(a.transform.position-Vector3.up*ag.baseOffset,out _,2f,NavMesh.AllAreas);});
   C(off==0,"every field actor has an agent, vitals and loaded NavMesh within 2 m (the session's start rule) - off: "+off);
   var all=session.Actors??Array.Empty<PrologueEncounter>();C(all.All(a=>a!=null)&&all.Select(a=>a.Id).Distinct().Count()==all.Length,"session.Actors has no hole and no duplicate id ("+all.Length+")");
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==PopRoot308);var spots=root!=null?root.transform.Find("SubNpcSpots"):null;
   C(spots==null||spots.GetComponentsInChildren<Component>(true).All(x=>x is Transform),"sub NPC spots are names only: no component, no body");
   return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString();
  }
 }
}
