using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpAreaBranchBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/AreaBranch";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||glyph!="모")throw new Exception("Stopped editor and 모 required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"AreaBranch");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   if(!File.Exists(Folder+"/Baseline_모.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_모.asset");
   if(!File.Exists(Folder+"/BaselineSpellBook.asset"))AssetDatabase.CopyAsset("Assets/_Project/Data/Configs/SpellBook_Proto.asset",Folder+"/BaselineSpellBook.asset");
   p.BranchedEarthRift=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   string output=Path.Combine(Vfx120Editor.Output,"AreaBranch",glyph);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));return "AREA_BRANCH_BUILT 모";
  }
  [Serializable] class Report{public string status;public List<string> checks=new List<string>(),errors=new List<string>();}
  static void Need(Report r,bool condition,string name){if(!condition)throw new Exception(name);r.checks.Add(name);}
  public static string Audit()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");var r=new Report();
   try
   {
    var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var profile=catalog.Entries.Single(x=>x.Glyph=="모").Profile;
    Need(r,profile.EarthRift&&profile.BranchedEarthRift,"only opted-in earth rift uses branch structure");
    for(int lane=1;lane<7;lane++)
    {
     int parent=lane<3?0:lane<5?1:2;float split=Vfx120Effect.BranchStart(lane,12);
     Need(r,Mathf.Abs(split-Vfx120Effect.BranchEnd(parent,12))<.0001f&&Mathf.Abs(Vfx120Effect.BranchAcross(lane,split,12)-Vfx120Effect.BranchAcross(parent,split,12))<.0001f,"connected junction "+lane);
    }
    foreach(int fps in new[]{30,60,120})foreach(float speed in new[]{1f,.2f})
    {
     var host=new GameObject("BranchAudit");
     try
     {
      var fx=host.AddComponent<Vfx120Effect>();fx.Profile=profile;fx.PreviewControlled=true;
      var plan=new AreaImpactPlan{Shape=AreaShape.Path,Point=new Vector3(0,30,0),Direction=Vector3.forward,Radius=6,Length=12,Speed=7,Delay=.4f,CreatedAt=Time.time};string snapshot=JsonUtility.ToJson(plan);
      fx.SetAreaPlan(plan);fx.Begin(new Vector3(0,31,0),null,new Vector3(0,30,12),Color.white);
      var lines=host.GetComponentsInChildren<LineRenderer>();
      for(float t=0;t<.7f;t+=speed/fps)fx.Sample(t);
      Need(r,lines.Count(x=>x.enabled)==1,"single trunk first "+fps+"/"+speed);
      fx.Sample(1.25f);Need(r,lines.Count(x=>x.enabled)==3,"two primary branches after trunk "+fps+"/"+speed);
      fx.Sample(1.95f);Need(r,lines.Count(x=>x.enabled)==7,"four smaller terminal branches "+fps+"/"+speed);
      Need(r,lines[0].startWidth>lines[1].startWidth&&lines[1].startWidth>lines[3].startWidth,"descending branch widths "+fps+"/"+speed);
      Need(r,fx.AreaParticles>0,"branch debris emits "+fps+"/"+speed);
      for(int lane=1;lane<7;lane++){int parent=lane<3?0:lane<5?1:2;Need(r,Vector3.Distance(lines[parent].GetPosition(24),lines[lane].GetPosition(0))<.001f,"world junction continuous "+lane+" "+fps+"/"+speed);}
      Need(r,JsonUtility.ToJson(plan)==snapshot,"damage plan unchanged "+fps+"/"+speed);
      fx.Sample(fx.Life+.7f);Need(r,lines.All(x=>!x.enabled)&&fx.AreaParticles==0,"branches and debris end "+fps+"/"+speed);
     }finally{Object.DestroyImmediate(host);}
    }
    r.status="PASS";
   }catch(Exception e){r.status="FAIL";r.errors.Add(e.ToString());}
   Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"AreaBranch"));File.WriteAllText(Path.Combine(Vfx120Editor.Output,"AreaBranch","audit.json"),JsonUtility.ToJson(r,true));return r.status+" "+r.checks.Count+" checks "+string.Join(";",r.errors);
  }
 }
}
