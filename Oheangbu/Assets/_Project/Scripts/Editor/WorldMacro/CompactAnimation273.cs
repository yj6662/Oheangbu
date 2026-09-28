using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string Animation273(string command)
  {
   if(command=="apply")return ApplyAnimation273();
   if(command!="audit")throw new ArgumentException(command);
   var scene=FrontageScene249();
   var rows=new System.Collections.Generic.List<string>();
   foreach(var enemy in scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<EnemyVitals>(true)))
   {
    rows.Add("ACTOR "+enemy.name+" | "+string.Join(",",enemy.GetComponents<Component>().Select(c=>c==null?"missing":c.GetType().Name)));
    foreach(var r in enemy.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled))
    {var sk=r as SkinnedMeshRenderer;var mf=r.GetComponent<MeshFilter>();var mesh=sk!=null?sk.sharedMesh:mf!=null?mf.sharedMesh:null;
     rows.Add("  MESH "+r.name+" bones="+(sk!=null?sk.bones.Length:0)+" path="+AssetDatabase.GetAssetPath(mesh));}
    foreach(var a in enemy.GetComponentsInChildren<Animator>(true))rows.Add("  ANIM "+a.name+" controller="+AssetDatabase.GetAssetPath(a.runtimeAnimatorController));
   }
   foreach(var id in AssetDatabase.FindAssets("t:SummonCombatProfile"))
   {var p=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(AssetDatabase.GUIDToAssetPath(id));
    rows.Add("SUMMON "+AssetDatabase.GetAssetPath(p)+" prefab="+AssetDatabase.GetAssetPath(p.PresentationPrefab));
    foreach(var f in typeof(SummonCombatProfile).GetFields().Where(f=>f.FieldType==typeof(AnimationClip)))
    {var c=f.GetValue(p) as AnimationClip;rows.Add("  "+f.Name+"="+(c==null?"MISSING":c.name+" @ "+AssetDatabase.GetAssetPath(c)));}
   }
   Directory.CreateDirectory("../Art/Characters/Animation273");File.WriteAllLines("../Art/Characters/Animation273/audit-before.txt",rows);return "Animation273 audit: "+rows.Count+" lines";
  }
  const string MotionFolder273="Assets/_Project/Art/Characters/Animation273";
  static AnimationClip MotionClip273(string family,string suffix)
  {
   return AssetDatabase.LoadAllAssetsAtPath(MotionFolder273+"/"+family+"_273.fbx").OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__")&&c.name.EndsWith("A273_"+suffix));
  }
  static string ApplyAnimation273()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Candidate has unsaved changes");
   string backup="../Art/Characters/Animation273/Recovery";Directory.CreateDirectory(backup);
   if(!File.Exists(backup+"/Candidate.unity")){File.Copy(scene.path,backup+"/Candidate.unity");File.Copy(scene.path+".meta",backup+"/Candidate.unity.meta");}
   foreach(var family in new[]{"WoodDeer","FireHaetae","MetalTiger","DokkaebiClub","WaterTurtle"})
   {
    var importer=AssetImporter.GetAtPath(MotionFolder273+"/"+family+"_273.fbx") as ModelImporter;if(importer==null)throw new Exception("Missing Blender export: "+family);
    importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.materialImportMode=ModelImporterMaterialImportMode.None;
    var clips=importer.defaultClipAnimations;foreach(var clip in clips){clip.loopTime=clip.name.EndsWith("Stun");clip.lockRootPositionXZ=true;clip.lockRootHeightY=true;clip.lockRootRotation=true;}importer.clipAnimations=clips;importer.SaveAndReimport();
   }
   var roots=scene.GetRootGameObjects();int profiles=0,enemies=0;
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Animation273";Directory.CreateDirectory(folder);
   foreach(var manager in roots.SelectMany(o=>o.GetComponentsInChildren<DemoSummonCombatManager>(true)))
   {
    manager.Profiles=manager.Profiles.Select(source=>
    {
     string family=Path.GetFileName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(source.PresentationPrefab)));
     string path=folder+"/"+family+".asset";var p=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(path);
     if(p==null){p=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(p,path);}
     p.FormationClip=MotionClip273(family,"Form");p.DissolveClip=MotionClip273(family,"Dissolve");EditorUtility.SetDirty(p);profiles++;return p;
    }).ToArray();EditorUtility.SetDirty(manager);
   }
   foreach(var enemy in roots.SelectMany(o=>o.GetComponentsInChildren<EnemyVitals>(true)))
   {
    var old=enemy.GetComponentInChildren<Oheangbu.App.World.OwnedActorMotion>(true);if(old==null||old.Animator==null)continue;
    string family=enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>AssetDatabase.GetAssetPath(r.sharedMesh)).Select(p=>new[]{"MetalTiger","FireHaetae","WoodDeer","WaterTurtle","DokkaebiClub"}.FirstOrDefault(p.Contains)).FirstOrDefault(n=>n!=null);
    if(family==null)continue;
    string source="Assets/_Project/Art/Demo/Summons/"+family+"/SM_"+family+"_Combat.fbx";
    var original=AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")).ToArray();
    var driver=enemy.GetComponent<Oheangbu.App.World.EnemyRigMotion273>()??enemy.gameObject.AddComponent<Oheangbu.App.World.EnemyRigMotion273>();
    driver.Animator=old.Animator;driver.Enemy=enemy.GetComponent<EnemyController>();driver.Vitals=enemy;
    driver.Idle=original.First(c=>c.name.Contains("Idle"));driver.Walk=original.First(c=>c.name.Contains("Walk")&&!c.name.Contains("Backward"));
    driver.Attack=MotionClip273(family,"EnemyAttack");driver.Hit=MotionClip273(family,"Hit");driver.Stun=MotionClip273(family,"Stun");driver.Death=MotionClip273(family,"Death");
    var encounter=enemy.GetComponent<Oheangbu.App.Prologue.PrologueEncounter>();encounter.DeathVisualSeconds=driver.Death.length+.1f;EditorUtility.SetDirty(encounter);
    old.enabled=false;EditorUtility.SetDirty(old);EditorUtility.SetDirty(driver);enemies++;
   }
   if(profiles!=5||enemies!=5)throw new Exception("Unexpected candidate coverage: "+profiles+" profiles, "+enemies+" enemies");
   AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
   return "Candidate only: "+profiles+" summon transition profiles, "+enemies+" enemies with Blender attack/hit/stun/death clips. Original profiles and models preserved.";
  }
 }
}
