using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class Content308
 {
  internal static void CrArm308(PrologueEncounter actor,WorldMacroPlaytestSO.Encounter row,Object profile,Object projectile,int mode,bool ranged,float preferred)=>PopArm308(actor,row,profile,projectile,mode,ranged,preferred);
 }

 // #308 D308-125 적용 3 [TEST]: a role gives some members of this tool's own groups another attack than their species' (first: the 적로
 // 불승냥이 charge, one wolf a pack). Data: creatures308.json roles[] {contentId, profile, members, why}; members = the trailing n of
 // "<prefix><realm>_<key>_<group>_<n>", empty = every member of that contentId.
 //   attacks         each member gets the role's profile (controller _attackProfile, _attackMode = the profile's Mode, its projectile kept),
 //                   the actor's Ranged / PreferredDistance (the brain's stop distance reads those, not the profile) and its content row's
 //                   Ranged. Scene + content backed up first, then saved. place and swap:apply arm the actors they make again.
 //   attacks:revert  the species prefab's attack back on each member (profile, projectile, mode, PreferredDistance, Ranged false)
 public static partial class Creatures308
 {
  [Serializable] sealed class RoleRow{public string contentId="",profile="",why="";public int[] members=Array.Empty<int>();}

  static int Member(string id){int cut=id!=null?id.LastIndexOf('_'):-1;return cut>=0&&int.TryParse(id.Substring(cut+1),out int n)?n:-1;}
  static RoleRow RoleFor(Cfg cfg,WorldMacroPlaytestSO.Encounter row)=>row==null?null:(cfg.roles??Array.Empty<RoleRow>()).FirstOrDefault(r=>r!=null&&!string.IsNullOrEmpty(r.profile)&&r.contentId==row.ContentId&&(r.members==null||r.members.Length==0||r.members.Contains(Member(row.Id))));
  static EnemyAttackProfileSO RoleProfile(RoleRow r)
  {
   var p=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(r.profile);string why=null;
   if(p==null||!p.TryValidate(out why))throw new Refuse(r.contentId+": role profile "+r.profile+(p==null?" is missing":" is invalid ("+why+")"));
   return p;
  }
  // the species whose prefab dressed this actor last: the swap ledger, else the plan
  static string SpeciesOf(string id)
  {
   if(File.Exists(SwapLedgerPath)){var done=JsonUtility.FromJson<SwapLedger>(File.ReadAllText(SwapLedgerPath)).rows.FirstOrDefault(r=>r.id==id);if(done!=null)return done.species;}
   if(!File.Exists(PlanFile))return null;var plan=PlanData();
   return plan.enemies.FirstOrDefault(e=>e.id==id)?.species??plan.bosses.FirstOrDefault(b=>b.id==id)?.species;
  }

  // arm (or put back) every role member in the scene; everything is looked up before the first write, so a refusal leaves the scene untouched
  static string Roles(Cfg cfg,Scene scene,WorldMacroPlaytestSO content,bool arm)
  {
   var rows=content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>();
   var work=new List<(PrologueEncounter actor,WorldMacroPlaytestSO.Encounter row,Object profile,Object projectile,int mode,bool ranged,float preferred)>();
   foreach(var a in Content308.CrSaved308<PrologueEncounter>(scene).Where(a=>Own(cfg,a.Id)))
   {
    var row=rows.FirstOrDefault(e=>e!=null&&e.Id==a.Id);var role=RoleFor(cfg,row);if(role==null)continue;
    if(arm)
    {
     var p=RoleProfile(role);var own=new SerializedObject(a.GetComponent<EnemyController>());
     work.Add((a,row,p,own.FindProperty("_projectilePrefab").objectReferenceValue,(int)p.Mode,p.Mode==EnemyController.AttackMode.RangedOnly,p.PreferredDistanceHint));continue;
    }
    string key=SpeciesOf(a.Id);var prefab=key!=null?AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,key)):null;
    if(prefab==null||prefab.GetComponent<EnemyController>()==null||prefab.GetComponent<PrologueEncounter>()==null)throw new Refuse(a.Id+": no species prefab to put back ("+(key??"species unknown")+")");
    var pso=new SerializedObject(prefab.GetComponent<EnemyController>());
    work.Add((a,row,pso.FindProperty("_attackProfile").objectReferenceValue,pso.FindProperty("_projectilePrefab").objectReferenceValue,pso.FindProperty("_attackMode").intValue,false,prefab.GetComponent<PrologueEncounter>().PreferredDistance));
   }
   foreach(var w in work)Content308.CrArm308(w.actor,w.row,w.profile,w.projectile,w.mode,w.ranged,w.preferred);
   return (arm?"roles armed ":"roles put back ")+work.Count+" actor(s)"+(work.Count>0?" ("+string.Join(", ",work.Select(w=>w.actor.Id))+")":"");
  }
  // place and swap:apply: a refusal here must not stop the write they are in the middle of; it is reported instead
  static string RolesAfter(Cfg cfg,Scene scene,WorldMacroPlaytestSO content){try{return Roles(cfg,scene,content,true);}catch(Refuse r){return "roles NOT armed: "+r.Message+" (fix, then run attacks)";}}

  static string Attacks(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,bool arm)
  {
   string dir=Path.Combine(Stage,"Backup",DateTime.Now.ToString("yyyyMMddTHHmmss")+"_"+(arm?"attacks":"attacks_revert"));Directory.CreateDirectory(dir);
   File.Copy(Path.GetFullPath(scene.path),Path.Combine(dir,Path.GetFileName(scene.path)),true);File.Copy(Path.GetFullPath(cfg.content),Path.Combine(dir,Path.GetFileName(cfg.content)),true);
   string done=Roles(cfg,scene,content,arm);
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
   return "Creatures308 "+(arm?"attacks":"attacks:revert")+", backup "+dir+"\n  "+done+"\n"+Check(cfg,scene,session,content);
  }

  // Check: a role names a real kind, and its members carry its attack (the brain's stop distance with it)
  static string RoleCheck(Cfg cfg,WorldMacroPlaytestSO.Encounter[] rows,PrologueEncounter[] actors,out bool ok)
  {
   var roles=(cfg.roles??Array.Empty<RoleRow>()).Where(r=>r!=null).ToArray();
   var empty=roles.Where(r=>!rows.Any(e=>RoleFor(cfg,e)==r)).Select(r=>r.contentId).ToArray();
   var members=actors.Where(a=>RoleFor(cfg,rows.FirstOrDefault(e=>e.Id==a.Id))!=null).ToArray();
   var off=members.Where(a=>
   {
    var role=RoleFor(cfg,rows.FirstOrDefault(e=>e.Id==a.Id));var p=new SerializedObject(a.GetComponent<EnemyController>()).FindProperty("_attackProfile").objectReferenceValue as EnemyAttackProfileSO;
    return p==null||AssetDatabase.GetAssetPath(p)!=role.profile||!Mathf.Approximately(a.PreferredDistance,p.PreferredDistanceHint)||a.Ranged!=(p.Mode==EnemyController.AttackMode.RangedOnly);
   }).Select(a=>a.Id).ToArray();
   ok=empty.Length==0&&off.Length==0;
   return "roles: "+roles.Length+" row(s), "+members.Length+" member actor(s) carry their role's attack (run attacks)"+(empty.Length>0?" - matches nothing: "+string.Join(", ",empty):"")+(off.Length>0?" - not armed: "+string.Join(", ",off):"");
  }
 }
}