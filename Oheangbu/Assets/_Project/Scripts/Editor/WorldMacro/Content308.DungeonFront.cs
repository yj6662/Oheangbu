using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
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
 // D308-117 C3 [TEST] - the enemies of the dungeon front sections opened before 청룡 (뿌리 굴 · 숯가마 골 땅굴). Data:
 // Tools/Unity/Stage308_populate/dungeon_front308.json (written by Tools/Art/FieldFirst308/field117_c3.py).
 //   front:apply    each row = a clone of a LIVE field actor (its body, brain and numbers as they stand now, Creatures308 body included) under
 //                  DungeonFront308/Actors, with a copy of that actor's content row (Id / Feet / Patrol replaced, ContentId kept). The feet are
 //                  3-D (the cave floor lies 10 - 27 m under the terrain): the NavMesh is sampled there, never from above.
 //   front:revert   removes exactly that: the root, the session / wiring links and the content rows whose Id starts with "dg308f/"
 //   front:check    one actor per dg308f row, linked and wired, on the NavMesh, feet within keep_m of the data
 // Its own prefix, so Populate apply / revert (which rebuild every field308/ row) never touch these. Backed up like Populate.
 public static partial class Content308
 {
  const string FrontRoot308="DungeonFront308",FrontPrefix308="dg308f/";
  [Serializable] sealed class FrontEnemy308{public string id="",name="",source="",dungeon="";public float[] feet=Array.Empty<float>(),patrol2=Array.Empty<float>();}
  [Serializable] sealed class FrontPlan308{public string version="";public float nav_radius_m=1.5f,keep_m=1f;public FrontEnemy308[] enemies=Array.Empty<FrontEnemy308>();}
  static FrontPlan308 FrontData308()
  {
   string f=Path.Combine(PopStage308,"dungeon_front308.json");
   if(!File.Exists(f))throw new Refuse308("no "+f+" (run python Tools/Art/FieldFirst308/field117_c3.py)");
   return JsonUtility.FromJson<FrontPlan308>(File.ReadAllText(f));
  }

  static string PopFront308(string c,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   if(c=="check")return FrontCheck308(scene,session,content);
   if(c!="apply"&&c!="revert")return "REFUSED unknown front command '"+c+"' (front:apply | front:revert | front:check)";
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
   if(EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)return "REFUSED scripts are compiling or failed to compile";
   if(scene.isDirty)return "REFUSED the play scene has unsaved changes";
   var data=c=="apply"?FrontData308():null;
   string dir=Path.Combine(PopStage308,"Backup",DateTime.Now.ToString("yyyyMMddTHHmmss")+"_front_"+c);Directory.CreateDirectory(dir);
   File.Copy(Path.GetFullPath(scene.path),Path.Combine(dir,Path.GetFileName(scene.path)),true);File.Copy(Path.GetFullPath(PopContent308),Path.Combine(dir,Path.GetFileName(PopContent308)),true);
   var sb=new StringBuilder("Content308.Populate front:"+c+", backup "+dir);
   // removed first: actors, links, rows, root of this prefix only
   int gone=0;
   foreach(var a in (session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&a.Id!=null&&a.Id.StartsWith(FrontPrefix308,StringComparison.Ordinal)).ToArray()){SetWired308(session,a.GetComponent<EnemyVitals>(),false);gone++;}
   session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&!(a.Id!=null&&a.Id.StartsWith(FrontPrefix308,StringComparison.Ordinal))).ToArray();
   var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==FrontRoot308);if(old!=null)Object.DestroyImmediate(old);
   var rows=new List<WorldMacroPlaytestSO.Encounter>((content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&!(e.Id!=null&&e.Id.StartsWith(FrontPrefix308,StringComparison.Ordinal))));
   sb.Append("\n  removed first: ").Append(gone).Append(" actors, ").Append((content.Encounters?.Length??0)-rows.Count).Append(" rows").Append(old!=null?", root":"");
   if(c=="apply")
   {
    var root=new GameObject(FrontRoot308);SceneManager.MoveGameObjectToScene(root,scene);var home=new GameObject("Actors").transform;home.SetParent(root.transform,false);
    var all=Saved308<PrologueEncounter>(scene).ToArray();var added=new List<PrologueEncounter>();
    foreach(var e in data.enemies)
    {
     if(!e.id.StartsWith(FrontPrefix308,StringComparison.Ordinal))throw new Refuse308("front row "+e.id+" does not start with "+FrontPrefix308);
     var src=all.FirstOrDefault(x=>x.Id==e.source);var srow=Encounter308(content,e.source);
     if(src==null||srow==null)throw new Refuse308("front row "+e.id+": source "+e.source+(src==null?" has no actor in the scene":" has no content row"));
     if(e.feet.Length!=3||e.patrol2.Length!=3)throw new Refuse308("front row "+e.id+": feet / patrol2 need x, y, z");
     if(!NavMesh.SamplePosition(PopV308(e.feet),out var hit,data.nav_radius_m,NavMesh.AllAreas))throw new Refuse308("front row "+e.id+": no NavMesh within "+data.nav_radius_m+" m of "+V308(PopV308(e.feet)));
     var feet=hit.position;var p2=NavMesh.SamplePosition(PopV308(e.patrol2),out var hit2,data.nav_radius_m,NavMesh.AllAreas)?hit2.position:feet;
     rows.Add(new WorldMacroPlaytestSO.Encounter{Id=e.id,ContentId=srow.ContentId,Feet=feet,Patrol=new[]{feet,p2},Ranged=srow.Ranged,RespawnOnRest=true,Detection=srow.Detection,Leash=srow.Leash,Speed=srow.Speed,Activation=srow.Activation});
     var agent=src.GetComponent<NavMeshAgent>();var at=feet+Vector3.up*(agent!=null?agent.baseOffset:0f);
     var go=Object.Instantiate(src.gameObject);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(home,true);go.name=e.id.Substring(FrontPrefix308.Length)+" ["+e.name+"]";
     go.transform.localScale=src.transform.lossyScale;go.transform.SetPositionAndRotation(at,Quaternion.Euler(0f,Mathf.Atan2(p2.x-feet.x,p2.z-feet.z)*Mathf.Rad2Deg,0f));go.SetActive(src.gameObject.activeSelf);
     var actor=go.GetComponent<PrologueEncounter>();actor.Id=e.id;actor.Ranged=srow.Ranged;actor.DetectionRange=srow.Detection;actor.Leash=srow.Leash;actor.Speed=srow.Speed;actor.PatrolPoints=new[]{feet,p2};
     EditorUtility.SetDirty(actor);added.Add(actor);SetWired308(session,go.GetComponent<EnemyVitals>(),true);
     sb.Append("\n  ").Append(e.id).Append(" [").Append(e.name).Append("] <- ").Append(e.source).Append(" at ").Append(V308(feet)).Append(" (data ").Append(F308(Vector3.Distance(feet,PopV308(e.feet)))).Append(" m), patrol ").Append(V308(p2));
    }
    session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Concat(added).ToArray();
   }
   content.Encounters=rows.ToArray();
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);EditorUtility.SetDirty(session);
   UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
   return sb.Append('\n').Append(FrontCheck308(scene,session,content)).ToString();
  }

  static string FrontCheck308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var sb=new StringBuilder("Content308.Populate front:check\n");int pass=0,fail=0;void C(bool ok,string text){sb.Append(ok?"  [PASS] ":"  [FAIL] ").Append(text).Append('\n');if(ok)pass++;else fail++;}
   FrontPlan308 data=null;try{data=FrontData308();}catch(Refuse308 r){C(false,r.Message);}
   var rows=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&e.Id!=null&&e.Id.StartsWith(FrontPrefix308,StringComparison.Ordinal)).ToArray();
   var actors=Saved308<PrologueEncounter>(scene).Where(a=>a.Id!=null&&a.Id.StartsWith(FrontPrefix308,StringComparison.Ordinal)).ToArray();
   if(data!=null)C(data.enemies.Length==rows.Length&&data.enemies.All(e=>rows.Any(r=>r.Id==e.id)),"every data row has its content row ("+rows.Length+" of "+data.enemies.Length+")");
   C(rows.Select(r=>r.Id).Distinct().Count()==rows.Length&&rows.All(r=>actors.Count(a=>a.Id==r.Id)==1)&&actors.All(a=>rows.Any(r=>r.Id==a.Id)),"one actor per front row and no actor without a row ("+rows.Length+" rows, "+actors.Length+" actors)");
   C(actors.All(a=>(session.Actors??Array.Empty<PrologueEncounter>()).Contains(a)&&Wired308(session,a.GetComponent<EnemyVitals>())),"every front actor is in session.Actors and wired for combat");
   var off=actors.Where(a=>{var ag=a.GetComponent<NavMeshAgent>();return ag==null||a.GetComponent<EnemyVitals>()==null||!NavMesh.SamplePosition(a.transform.position-Vector3.up*ag.baseOffset,out _,1f,NavMesh.AllAreas);}).Select(a=>a.Id).ToArray();
   C(off.Length==0,"every front actor stands on the NavMesh within 1 m (off: "+(off.Length==0?"none":string.Join(", ",off))+")");
   if(data!=null)
   {
    var far=data.enemies.Select(e=>(e,r:rows.FirstOrDefault(r=>r.Id==e.id))).Where(x=>x.r!=null&&Vector3.Distance(x.r.Feet,PopV308(x.e.feet))>data.keep_m).Select(x=>x.e.id+" "+F308(Vector3.Distance(x.r.Feet,PopV308(x.e.feet)))+" m").ToArray();
    C(far.Length==0,"feet within "+F308(data.keep_m)+" m of the data (the cave floor, not the terrain above) (far: "+(far.Length==0?"none":string.Join(", ",far))+")");
   }
   return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString().TrimEnd();
  }
 }
}
