using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 scene ledger (SPEC-REGION-SKY-308 씬 원장). Idempotent, three scenes in promotion order (or one with scene=...):
 //   L1 (1a)  LocationArrivals262/WorldRealmAtmosphere.m_Enabled = 0 (never deleted; #263 code recreates a missing one). The
 //            #263 invariants (Catalog, View, SkySource, TintStrength) are snapshotted once for AC-S1.
 //   L2 (1b)  Macro_WorldLook/WorldLookDriver._regionalSkyProfile = RegionalSky308 (RegionalSky297 stays for lighting-revert)
 //   L3 (1b)  WorldLookDriver._interiorMap = the scene's session map (PlaytestUiRoot.MapData); _sealedCells = every
 //            InteriorSight307SO in Resources/Perf307
 //   L4 (2)   WorldLookDriver._skyboxMaterial = Art/World/RegionSky308/Materials/RealmInkSky308.mat. Explicit step only
 //            (ledger:*:step=2; the step-less apply stays 1a+1b). Needs the material on a shader without errors and RegionalSky308
 //            with step-2 data (data:apply:step=2) and the driver on RegionalSky308 (L2). apply also needs :ac19=<perf307 run id>
 //            — Spec AC-S19: L4 is connected only when the Perf307 A/B is within budget. The id must name a PASS (valid) run of
 //            ab sky308:1 tier=pc whose GPU delta is <= +.10 ms (Ac19Verdict reads its summary.json); id + evidence go in the item.
 // Each item reads the current value and skips when it is already the target; the first "before" is kept in
 // Art/World/RegionSky308/scene-ledger.json; a scene file is copied to Art/World/RegionSky308/Backups/<utc>/ before a write;
 // only that scene is saved. revert restores items whose value is still the applied one.
 public static partial class RegionSky308
 {
  sealed class Planned{public SceneItem item;public Action write;public string note;}

  static string Ledger(string mode,string step,Dictionary<string,string> o)
  {
   if(step.Length>0&&step!="1a"&&step!="1b"&&step!="2")throw new PostLedger308.Refused("unknown step '"+step+"' (1a | 1b | 2)");
   var steps=step.Length>0?new[]{step}:mode=="revert"?new[]{"2","1b","1a"}:new[]{"1a","1b"};
   string ac19=o.TryGetValue("ac19",out var g)?g.Trim():"";
   if(mode=="apply"&&steps.Contains("2")&&ac19.Length==0)
    throw new PostLedger308.Refused("L4 (RealmInkSky308) is connected only after AC-S19 (perf307 ab \"sky308:1\" tier=pc within +.10 ms GPU): re-run with :ac19=<perf307 run id>");
   // the id must name a finished, valid sky308:1 A/B within budget (Art/Performance/Perf307/<id>/summary.json), not just any text
   if(mode=="apply"&&steps.Contains("2"))ac19=ac19+" "+Ac19Verdict(ac19);
   PostLedger308.RequireEditable();
   var scenes=PostLedger308.Select(o);
   var ledger=ReadJson<SceneLedger>(SceneLedgerPath);string utc=PostLedger308.Utc();
   var sb=new StringBuilder("ledger:"+mode+" step "+string.Join("+",steps)+"\n");
   foreach(var path in scenes)
   {
    var scene=PostLedger308.Open(path);sb.AppendLine(" "+PostLedger308.Short(path)+" ("+path+")");
    List<Planned> plan;
    try{plan=mode=="revert"?PlanRevert(scene,ledger,steps):PlanApply(scene,ledger,steps,utc,ac19);}
    catch(PostLedger308.Refused r){sb.AppendLine("  refused: "+r.Message);continue;}
    foreach(var p in plan)sb.AppendLine("  "+p.item.item+" "+p.item.objectPath+"."+p.item.property+": "+p.note);
    var writes=plan.Where(p=>p.write!=null).ToList();
    if(writes.Count==0){sb.AppendLine("  no-op");continue;}
    if(mode=="dry"){sb.AppendLine("  dry: "+writes.Count+" change(s) not written");continue;}
    string backup=PostLedger308.Backup(BackupDir,utc,path);ledger.backups.Add(backup);
    foreach(var p in writes)p.write();
    PostLedger308.SaveScene(scene);
    WriteJson(SceneLedgerPath,ledger);
    sb.AppendLine("  saved "+writes.Count+" change(s); backup "+backup);
   }
   if(mode!="dry")WriteJson(SceneLedgerPath,ledger);
   return sb.Append(" ledger "+SceneLedgerPath).ToString();
  }

  static T Single<T>(Scene scene,string what) where T:Component
  {
   var all=PostLedger308.All<T>(scene).ToArray();
   if(all.Length!=1)throw new PostLedger308.Refused(what+" count "+all.Length+" (expected 1)");
   return all[0];
  }

  internal static string SealedTarget()=>string.Join("|",AssetDatabase.FindAssets("t:InteriorSight307SO",new[]{SealedFolder}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x,StringComparer.Ordinal));
  static string Paths(SerializedProperty array){var l=new List<string>();for(int i=0;i<array.arraySize;i++)l.Add(PostLedger308.AssetRef(array.GetArrayElementAtIndex(i).objectReferenceValue));return string.Join("|",l);}
  static void SetPaths(SerializedProperty array,string joined)
  {
   var paths=string.IsNullOrEmpty(joined)?new string[0]:joined.Split('|');array.arraySize=paths.Length;
   for(int i=0;i<paths.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(paths[i]);
  }

  static SceneItem Record(SceneLedger ledger,string scene,string step,string item,string objectPath,string property,string before,string after,string utc)
  {
   var rec=ledger.items.FirstOrDefault(x=>x.scene==scene&&x.item==item);
   if(rec==null){rec=new SceneItem{scene=scene,step=step,item=item,objectPath=objectPath,property=property,before=before};ledger.items.Add(rec);}
   rec.after=after;rec.utc=utc;return rec;
  }

  static SceneItem Record(SceneLedger ledger,string scene,string step,string item,string objectPath,string property,string before,string after,string utc,string note)
  {
   var rec=Record(ledger,scene,step,item,objectPath,property,before,after,utc);rec.note=note??"";return rec;
  }

  static List<Planned> PlanApply(Scene scene,SceneLedger ledger,string[] steps,string utc,string ac19)
  {
   var plan=new List<Planned>();string sp=scene.path;
   if(steps.Contains("1a"))
   {
    var atm=Single<WorldRealmAtmosphere>(scene,"WorldRealmAtmosphere");
    string objectPath=PostLedger308.PathOf(atm.transform);
    var so=new SerializedObject(atm);var en=so.FindProperty("m_Enabled");
    // #263 invariants, recorded once (AC-S1 compares the scene against this)
    if(!ledger.items.Any(x=>x.scene==sp&&x.item=="L1.snapshot"))
     ledger.items.Add(new SceneItem{scene=sp,step="1a",item="L1.snapshot",objectPath=objectPath,property="Catalog,View,SkySource,TintStrength",before=Snapshot263(atm),after="",utc=utc});
    bool on=en.boolValue;
    plan.Add(new Planned{item=new SceneItem{item="L1",objectPath=objectPath,property="m_Enabled"},note=on?"1 -> 0":"already 0",
     write=!on?null:(Action)(()=>{var s=new SerializedObject(atm);s.FindProperty("m_Enabled").boolValue=false;s.ApplyModifiedPropertiesWithoutUndo();Record(ledger,sp,"1a","L1",objectPath,"m_Enabled","1","0",utc);})});
    if(!objectPath.EndsWith("LocationArrivals262",StringComparison.Ordinal))plan.Add(new Planned{item=new SceneItem{item="L1",objectPath=objectPath,property="(path)"},note="warning: not on LocationArrivals262"});
   }
   if(steps.Contains("1b"))
   {
    var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath)??throw new PostLedger308.Refused("RegionalSky308 missing: run data:apply first");
    var driver=Single<WorldLookDriver>(scene,"WorldLookDriver");string objectPath=PostLedger308.PathOf(driver.transform);
    var so=new SerializedObject(driver);
    var prof=so.FindProperty("_regionalSkyProfile");
    string nowProfile=PostLedger308.AssetRef(prof.objectReferenceValue);
    plan.Add(new Planned{item=new SceneItem{item="L2",objectPath=objectPath,property="_regionalSkyProfile"},note=nowProfile==ProfilePath?"already RegionalSky308":(nowProfile.Length>0?Path.GetFileName(nowProfile):"null")+" -> RegionalSky308",
     write=nowProfile==ProfilePath?null:(Action)(()=>{var s=new SerializedObject(driver);s.FindProperty("_regionalSkyProfile").objectReferenceValue=profile;s.ApplyModifiedPropertiesWithoutUndo();Record(ledger,sp,"1b","L2",objectPath,"_regionalSkyProfile",nowProfile,ProfilePath,utc);})});
    var mapProp=so.FindProperty("_interiorMap");var sealedProp=so.FindProperty("_sealedCells");
    if(mapProp==null||sealedProp==null)throw new PostLedger308.Refused("field missing _interiorMap/_sealedCells (deploy the #308 WorldLookDriver first)");
    var ui=Single<PlaytestUiRoot>(scene,"PlaytestUiRoot");
    if(ui.MapData==null)throw new PostLedger308.Refused("PlaytestUiRoot.MapData is null");
    string mapTarget=PostLedger308.AssetRef(ui.MapData),mapNow=PostLedger308.AssetRef(mapProp.objectReferenceValue);
    plan.Add(new Planned{item=new SceneItem{item="L3.map",objectPath=objectPath,property="_interiorMap"},note=mapNow==mapTarget?"already the session map":(mapNow.Length>0?mapNow:"null")+" -> "+mapTarget,
     write=mapNow==mapTarget?null:(Action)(()=>{var s=new SerializedObject(driver);s.FindProperty("_interiorMap").objectReferenceValue=ui.MapData;s.ApplyModifiedPropertiesWithoutUndo();Record(ledger,sp,"1b","L3.map",objectPath,"_interiorMap",mapNow,mapTarget,utc);})});
    string sealedTarget=SealedTarget(),sealedNow=Paths(sealedProp);
    if(sealedTarget.Length==0)plan.Add(new Planned{item=new SceneItem{item="L3.sealed",objectPath=objectPath,property="_sealedCells"},note="warning: no InteriorSight307SO under "+SealedFolder});
    else plan.Add(new Planned{item=new SceneItem{item="L3.sealed",objectPath=objectPath,property="_sealedCells"},note=sealedNow==sealedTarget?"already "+sealedTarget:"["+sealedNow+"] -> ["+sealedTarget+"]",
     write=sealedNow==sealedTarget?null:(Action)(()=>{var s=new SerializedObject(driver);SetPaths(s.FindProperty("_sealedCells"),sealedTarget);s.ApplyModifiedPropertiesWithoutUndo();Record(ledger,sp,"1b","L3.sealed",objectPath,"_sealedCells",sealedNow,sealedTarget,utc);})});
   }
   if(steps.Contains("2"))
   {
    // gates: a shader without errors, the material on it, step-2 data in RegionalSky308, the driver on RegionalSky308 (L2)
    var shader=Sky2Shader(out string why)??throw new PostLedger308.Refused(why);
    var mat=AssetDatabase.LoadAssetAtPath<Material>(Sky2MatPath)??throw new PostLedger308.Refused("RealmInkSky308.mat missing: run data:apply:step=2 first");
    if(mat.shader!=shader)throw new PostLedger308.Refused(Sky2MatPath+" uses shader \""+(mat.shader!=null?mat.shader.name:"null")+"\" (expected \""+Sky2ShaderName+"\")");
    var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath)??throw new PostLedger308.Refused("RegionalSky308 missing: run data:apply first");
    if(!profile.HasShapeData)throw new PostLedger308.Refused("RegionalSky308 has no step-2 data for every realm: run data:apply:step=2 first");
    var driver=Single<WorldLookDriver>(scene,"WorldLookDriver");string objectPath=PostLedger308.PathOf(driver.transform);
    var so=new SerializedObject(driver);
    var matProp=so.FindProperty("_skyboxMaterial")??throw new PostLedger308.Refused("field missing _skyboxMaterial");
    string nowProfile=PostLedger308.AssetRef(so.FindProperty("_regionalSkyProfile")?.objectReferenceValue);
    if(nowProfile!=ProfilePath&&!steps.Contains("1b"))throw new PostLedger308.Refused("the driver is on "+(nowProfile.Length>0?Path.GetFileName(nowProfile):"no profile")+", not RegionalSky308: apply L2 (step 1b) first — RealmInkSky308 needs the step-2 data");
    var useSkybox=so.FindProperty("_useSkybox");
    if(useSkybox!=null&&!useSkybox.boolValue)plan.Add(new Planned{item=new SceneItem{item="L4",objectPath=objectPath,property="_useSkybox"},note="warning: _useSkybox is off (the driver draws no skybox)"});
    string nowMat=PostLedger308.AssetRef(matProp.objectReferenceValue);
    plan.Add(new Planned{item=new SceneItem{item="L4",objectPath=objectPath,property="_skyboxMaterial"},
     note=nowMat==Sky2MatPath?"already RealmInkSky308":(nowMat.Length>0?Path.GetFileName(nowMat):"null")+" -> RealmInkSky308"+(ac19.Length>0?" (AC-S19 "+ac19+")":""),
     write=nowMat==Sky2MatPath?null:(Action)(()=>{var s=new SerializedObject(driver);s.FindProperty("_skyboxMaterial").objectReferenceValue=mat;s.ApplyModifiedPropertiesWithoutUndo();Record(ledger,sp,"2","L4",objectPath,"_skyboxMaterial",nowMat,Sky2MatPath,utc,"ac19="+ac19);})});
   }
   return plan;
  }

  // ---------- AC-S19 gate (perf307 ab "sky308:1" summary) ----------
  internal const float Ac19BudgetMs=.10f;
  [Serializable] sealed class Ac19Row{public string key="";public float gpuA=-1,gpuB=-1,gpuDelta;}
  [Serializable] sealed class Ac19Summary{public string status="",command="",tier="",toggle="",runId="";public int screenWidth,screenHeight;public List<Ac19Row> ab=new List<Ac19Row>();}

  /// <summary>Reads Art/Performance/Perf307/&lt;id&gt;/summary.json: an "ab" run of toggle sky308:1 on tier pc whose status is PASS (not
  /// INVALID) and whose GPU frame time delta B - A is within +.10 ms (both the median of the per-pose deltas and the difference of
  /// the per-pose medians; rows without a GPU value are skipped). Returns the evidence line; throws Refused otherwise.</summary>
  internal static string Ac19Verdict(string id)
  {
   if(id.IndexOfAny(Path.GetInvalidFileNameChars())>=0||id.Contains(".."))throw new PostLedger308.Refused("ac19 must be a perf307 run id (folder name under Art/Performance/Perf307), got '"+id+"'");
   string path=PostLedger308.RepoPath("Art/Performance/Perf307/"+id+"/summary.json");
   if(!File.Exists(path))throw new PostLedger308.Refused("AC-S19 run "+id+": no summary at "+path+" (run perf307.py ab \"sky308:1\" tier=pc to the end first)");
   Ac19Summary s;try{s=JsonUtility.FromJson<Ac19Summary>(File.ReadAllText(path));}catch(Exception e){throw new PostLedger308.Refused("AC-S19 run "+id+": summary unreadable ("+e.Message+")");}
   if(s==null)throw new PostLedger308.Refused("AC-S19 run "+id+": empty summary");
   if(s.command!="ab"||s.toggle!="sky308:1")throw new PostLedger308.Refused("AC-S19 run "+id+" is '"+s.command+" "+s.toggle+"', not ab sky308:1");
   if(s.tier!="pc")throw new PostLedger308.Refused("AC-S19 run "+id+" ran on tier '"+s.tier+"' (AC-S19 needs tier=pc)");
   if(s.status==null||!s.status.StartsWith("PASS",StringComparison.Ordinal))throw new PostLedger308.Refused("AC-S19 run "+id+" is not valid: "+(s.status??"no status"));
   var rows=(s.ab??new List<Ac19Row>()).Where(r=>r!=null&&r.gpuA>=0f&&r.gpuB>=0f).ToList();
   if(rows.Count==0)throw new PostLedger308.Refused("AC-S19 run "+id+" has no A/B row with GPU frame time (frame timing off?)");
   float medDelta=Median(rows.Select(r=>r.gpuDelta)),medDiff=Median(rows.Select(r=>r.gpuB))-Median(rows.Select(r=>r.gpuA));
   string line="AC-S19 "+id+": GPU median of deltas "+medDelta.ToString("+0.000;-0.000",CultureInfo.InvariantCulture)+" ms, median B - median A "+
    medDiff.ToString("+0.000;-0.000",CultureInfo.InvariantCulture)+" ms over "+rows.Count+" pose rows, "+s.screenWidth+"x"+s.screenHeight;
   if(!(Mathf.Max(medDelta,medDiff)<=Ac19BudgetMs))throw new PostLedger308.Refused(line+" > +"+Ac19BudgetMs.ToString("0.00",CultureInfo.InvariantCulture)+
    " ms budget: set \"_Octaves\": 4 in the seeds' material section, data:apply:step=2, measure again; still over -> L4 is not applied");
   return line;
  }

  static float Median(IEnumerable<float> values)
  {
   var v=values.OrderBy(x=>x).ToList();int n=v.Count;
   return n==0?float.NaN:n%2==1?v[n/2]:.5f*(v[n/2-1]+v[n/2]);
  }

  /// <summary>The step-1 sky of a scene: the L4 "before" material when L4 is applied, else the driver's current material (Perf307
  /// sky308:0 uses it as its B side).</summary>
  internal static Material Step1SkyMaterial(string scenePath,Material current)
  {
   var rec=ReadJson<SceneLedger>(SceneLedgerPath).items.FirstOrDefault(x=>x.scene==scenePath&&x.item=="L4");
   if(rec!=null&&rec.before.Length>0)return AssetDatabase.LoadAssetAtPath<Material>(rec.before);
   return current!=null&&current.shader!=null&&current.shader.name==Sky2ShaderName?null:current;
  }

  static List<Planned> PlanRevert(Scene scene,SceneLedger ledger,string[] steps)
  {
   var plan=new List<Planned>();string sp=scene.path;
   var items=ledger.items.Where(x=>x.scene==sp&&steps.Contains(x.step)&&!x.item.EndsWith(".snapshot",StringComparison.Ordinal)).Reverse().ToList();
   foreach(var rec in items)
   {
    var t=PostLedger308.Find(scene,rec.objectPath);
    if(t==null){plan.Add(new Planned{item=rec,note="object missing, kept in the ledger"});continue;}
    if(rec.item=="L1")
    {
     var atm=t.GetComponent<WorldRealmAtmosphere>();if(atm==null){plan.Add(new Planned{item=rec,note="component missing"});continue;}
     var en=new SerializedObject(atm).FindProperty("m_Enabled");string now=en.boolValue?"1":"0";
     if(now!=rec.after){plan.Add(new Planned{item=rec,note="changed since apply (now "+now+"), kept"});continue;}
     var r=rec;plan.Add(new Planned{item=rec,note=now+" -> "+rec.before,write=()=>{var s=new SerializedObject(atm);s.FindProperty("m_Enabled").boolValue=r.before=="1";s.ApplyModifiedPropertiesWithoutUndo();ledger.items.Remove(r);}});
     continue;
    }
    var driver=t.GetComponent<WorldLookDriver>();if(driver==null){plan.Add(new Planned{item=rec,note="WorldLookDriver missing"});continue;}
    var so=new SerializedObject(driver);var prop=so.FindProperty(rec.property);
    if(prop==null){plan.Add(new Planned{item=rec,note="field missing"});continue;}
    string cur=prop.isArray&&prop.propertyType!=SerializedPropertyType.String?Paths(prop):PostLedger308.AssetRef(prop.objectReferenceValue);
    if(cur!=rec.after){plan.Add(new Planned{item=rec,note="changed since apply (now "+cur+"), kept"});continue;}
    var rr=rec;
    plan.Add(new Planned{item=rec,note=cur+" -> "+(rec.before.Length>0?rec.before:"null"),write=()=>
    {
     var s=new SerializedObject(driver);var p=s.FindProperty(rr.property);
     if(p.isArray&&p.propertyType!=SerializedPropertyType.String)SetPaths(p,rr.before);else p.objectReferenceValue=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rr.before);
     s.ApplyModifiedPropertiesWithoutUndo();ledger.items.Remove(rr);
    }});
   }
   return plan;
  }

  internal static string Snapshot263(WorldRealmAtmosphere atm)
  {
   string catalog=atm.Catalog!=null?AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(atm.Catalog)):"";
   string view=atm.View!=null?PostLedger308.PathOf(atm.View.transform):"";
   string sky=atm.SkySource!=null?AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(atm.SkySource)):"";
   return "catalog="+catalog+";view="+view+";sky="+sky+";tint="+atm.TintStrength.ToString("R",CultureInfo.InvariantCulture);
  }
 }
}
