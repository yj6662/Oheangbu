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
 // D308-119 (SPEC-ENEMY-BODIES-308 §1) - a field enemy that wears another creature's body gets its own: every actor whose Id starts
 // with a swaps[] row's match is replaced by that species' Creatures308 prefab (bodies:build first). Kept from the old actor: Id, place
 // (feet), facing, parent, name, patrol, detection, leash, brain and its content row (only the row's Speed follows the new body's gait).
 // Taken from the species: body, clips, attack and vitals profiles. Data: creatures308.json swaps[] {match, species, note}.
 //   swap:dry     what would change (read only)
 //   swap:apply   replace, back up the scene + content first, save. Idempotent: an actor that already wears the species is left alone.
 // own: true on a row also re-dresses this tool's own placed actors (field308b/, fieldboss308/) where they stand now - a species
 // whose body changed after place, without a full place that would undo moves other tools made since (Hanyang T15 moved own actors).
 // Undo = the Populate pipeline (Content308 Populate apply -> Content308 content-apply:main -> Texts308 content-apply:main) clones the
 // template bodies again; run swap:apply after every Populate apply.
 public static partial class Creatures308
 {
  [Serializable] sealed class SwapRow{public string match="",species="",note="";public bool own;}
  [Serializable] sealed class SwapLedgerRow{public string id="",species="",from="",utc="";}
  [Serializable] sealed class SwapLedger{public List<SwapLedgerRow> rows=new List<SwapLedgerRow>();}
  static string SwapLedgerPath=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Creatures308","swap_ledger.json");

  static string BodyName(PrologueEncounter a)
  {
   var r=a.GetComponentsInChildren<Renderer>(true).Where(k=>k is SkinnedMeshRenderer).Select(k=>((SkinnedMeshRenderer)k).sharedMesh).FirstOrDefault(m=>m!=null);
   return (a.transform.Find("Creature308_Visual")!=null?"Creature308:":"")+(r!=null?r.name:"?");
  }

  // the numbers a swap must not change (SPEC-ENEMY-BODIES-308 invariant): hp, damage, telegraph; detection is copied over
  static string Stats(GameObject a)
  {
   var v=a.GetComponent<EnemyVitals>();var c=a.GetComponent<EnemyController>();
   var ap=c!=null?new SerializedObject(c).FindProperty("_attackProfile")?.objectReferenceValue as EnemyAttackProfileSO:null;
   return "hp "+(v!=null?F(v.MaxHp):"-")+" dmg "+(ap!=null?F(ap.Damage):"-")+" tel "+(ap!=null?F(ap.Telegraph):"-");
  }

  static string Swap(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,bool write)
  {
   var sb=new StringBuilder("Creatures308 swap "+(write?"apply":"dry"));
   if(cfg.swaps==null||cfg.swaps.Length==0)return sb.Append(": no swaps[] rows in the data").ToString();
   var ledger=File.Exists(SwapLedgerPath)?JsonUtility.FromJson<SwapLedger>(File.ReadAllText(SwapLedgerPath)):new SwapLedger();
   var actors=(session.Actors??Array.Empty<PrologueEncounter>()).ToList();var rows=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).ToList();
   if(write)
   {
    string dir=Path.Combine(Stage,"Backup",DateTime.Now.ToString("yyyyMMddTHHmmss")+"_swap");Directory.CreateDirectory(dir);
    File.Copy(Path.GetFullPath(scene.path),Path.Combine(dir,Path.GetFileName(scene.path)),true);File.Copy(Path.GetFullPath(cfg.content),Path.Combine(dir,Path.GetFileName(cfg.content)),true);
    sb.Append(", backup ").Append(dir);
   }
   int swapped=0,kept=0,refused=0;
   foreach(var row in cfg.swaps)
   {
    var s=cfg.species.FirstOrDefault(k=>k.key==row.species);var prefab=s!=null?AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,s.key)):null;var template=s!=null?FindActor(scene,s.template):null;
    var hits=actors.Where(a=>a!=null&&a.Id!=null&&a.Id.StartsWith(row.match,StringComparison.Ordinal)&&(row.own||!Own(cfg,a.Id))).ToArray();
    sb.Append("\n  ").Append(row.match).Append("* -> ").Append(row.species).Append(" (").Append(hits.Length).Append(" actor(s))");
    if(s==null||prefab==null||template==null){sb.Append(" REFUSED ").Append(s==null?"unknown species":prefab==null?"no prefab - bodies:build:"+row.species+" first":"template "+s.template+" not in the scene");refused+=hits.Length;continue;}
    if(hits.Length==0){sb.Append(" WARN nothing matches");continue;}
    var newAgent=prefab.GetComponent<NavMeshAgent>();float newOffset=newAgent!=null?newAgent.baseOffset:0f;
    foreach(var old in hits)
    {
     var oldAgent=old.GetComponent<NavMeshAgent>();var feet=old.transform.position-Vector3.up*(oldAgent!=null?oldAgent.baseOffset:0f);
     var done=ledger.rows.FirstOrDefault(r=>r.id==old.Id);
     if(done!=null&&done.species==s.key&&old.transform.Find("Creature308_Visual")!=null){kept++;sb.Append("\n    = ").Append(old.Id).Append(" already ").Append(s.key);continue;}
     bool onNav=NavMesh.SamplePosition(feet,out _,2f,NavMesh.AllAreas);
     string from=BodyName(old);
     sb.Append("\n    ").Append(write?"* ":"~ ").Append(old.Id).Append(" ").Append(from).Append(" -> ").Append(s.key).Append(" at ").Append(V(feet)).Append(onNav?"":" (WARN no NavMesh within 2 m)")
      .Append(" | ").Append(Stats(old.gameObject)).Append(" -> ").Append(Stats(prefab));
     if(!write)continue;
     var go=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(old.transform.parent,true);go.name=old.gameObject.name;
     go.transform.SetPositionAndRotation(feet+Vector3.up*newOffset,old.transform.rotation);go.SetActive(old.gameObject.activeSelf);
     Rewire(go,template.gameObject);
     var actor=go.GetComponent<PrologueEncounter>();actor.Id=old.Id;actor.Brain=old.Brain;actor.PatrolPoints=old.PatrolPoints;actor.Ranged=false;
     actor.DetectionRange=old.DetectionRange;actor.Leash=old.Leash;actor.Speed=s.speed;
     var rowAt=rows.FindIndex(r=>r!=null&&r.Id==old.Id);if(rowAt>=0){rows[rowAt].Speed=s.speed;rows[rowAt].Ranged=false;}
     var rig=go.GetComponent<EnemyRigMotion298>();if(rig!=null&&rig.Animator!=null&&rig.Idle!=null)Pose(rig.Animator,rig.Idle,0);
     Content308.CrSetWired308(session,old.GetComponent<EnemyVitals>(),false);
     actors[actors.IndexOf(old)]=actor;Content308.CrSetWired308(session,go.GetComponent<EnemyVitals>(),true);
     Object.DestroyImmediate(old.gameObject);
     ledger.rows.RemoveAll(r=>r.id==actor.Id);ledger.rows.Add(new SwapLedgerRow{id=actor.Id,species=s.key,from=from,utc=DateTime.UtcNow.ToString("o")});swapped++;
    }
   }
   sb.Append("\n  ").Append(write?"swapped ":"would swap ").Append(write?swapped:actors.Count(a=>a!=null&&cfg.swaps.Any(r=>a.Id!=null&&a.Id.StartsWith(r.match,StringComparison.Ordinal))&&!ledger.rows.Any(l=>l.id==a.Id&&a.transform.Find("Creature308_Visual")!=null)))
     .Append(", already ").Append(kept).Append(", refused ").Append(refused);
   if(!write)return sb.ToString();
   if(swapped==0)return sb.Append("\n  nothing written").ToString();
   session.Actors=actors.ToArray();content.Encounters=rows.ToArray();
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);EditorUtility.SetDirty(session);
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return sb.Append("\nFAILED the play scene was not saved").ToString();
   Directory.CreateDirectory(Path.GetDirectoryName(SwapLedgerPath));File.WriteAllText(SwapLedgerPath,JsonUtility.ToJson(ledger,true));
   return sb.Append("\n  ledger ").Append(SwapLedgerPath).ToString();
  }
 }
}
