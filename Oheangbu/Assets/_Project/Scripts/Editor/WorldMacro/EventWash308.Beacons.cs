using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 봉수 소등 (SPEC-EVENT-WASH-308 §1): ledger command, halo feature removal and the audit. The same extinguish routine runs at
 // the end of the #297 builder's beacon step (CompactFinish297.Attraction.cs), so re-running `attraction` keeps the beacons dark.
 public static partial class EventWash308
 {
  static readonly string[] Glowing={"Halo","Light","Core0","Core1","Core2"};
  static readonly string[] StoneLods={"LOD0","LOD1","LOD2"};
  // ART-INK 광원 3등급 allow list (AC-W3): 광맥, 처마·주막 등불, 성황당 촛불, 가마 등롱
  static readonly string[] AllowedGlow={"Ore_Vein","Ore_Glow297","InnLantern297","InnLantern","Warm_Porch_Lantern","DoorLantern","flame","CandleLight","Candle","Palanquin","가마"};
  static readonly string[] LightShaders={"Oheangbu/Finish297/InkBeacon","Oheangbu/InkLightSource","Oheangbu/CodexInkLantern","Oheangbu/Finish297/BeaconHalo",FarGlowShader};
  // #308 먼 불빛 (SPEC-ATTRACTION-LIGHT-308 AC-L12): the far glow card is a sustained light the census must see. It is allowed only on
  // the card object FarLight308 makes (FarLight308_Glow under a 주막 등롱 / 성황당 촛불); the same material on anything else is OUTSIDE.
  const string FarGlowShader="Oheangbu/Finish297/FarGlow308",FarGlowCard="FarLight308_Glow";

  internal sealed class Op{public string path,kind,before,after;public int slot=-1;}

  internal static bool IsBeaconCore(Material m)=>m!=null&&(m.name.StartsWith("M_Beacon297_",StringComparison.Ordinal)||m.shader!=null&&m.shader.name=="Oheangbu/Finish297/InkBeacon");

  /// <summary>M_BeaconAsh308: 그을린 재 — URP Lit (the kit stone slots' shader), the stone_rough texture darkened, no emission, no
  /// light-source marker. Created once; afterwards the material owns its tuning.</summary>
  internal static Material BeaconAsh()
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(AshPath);if(m!=null)return m;
   var lit=Shader.Find("Universal Render Pipeline/Lit")??throw new PostLedger308.Refused("URP Lit shader missing");
   m=new Material(lit){name="M_BeaconAsh308"};
   var stone=AssetDatabase.LoadAssetAtPath<Material>(StoneRoughPath);
   if(stone!=null&&stone.HasProperty("_BaseMap")){m.SetTexture("_BaseMap",stone.GetTexture("_BaseMap"));var n=stone.HasProperty("_BumpMap")?stone.GetTexture("_BumpMap"):null;if(n!=null){m.SetTexture("_BumpMap",n);m.SetFloat("_BumpScale",1f);m.EnableKeyword("_NORMALMAP");}}
   m.SetColor("_BaseColor",new Color(.30f,.28f,.26f));m.SetFloat("_Smoothness",.04f);m.SetFloat("_Metallic",0f);
   m.SetColor("_EmissionColor",Color.black);m.DisableKeyword("_EMISSION");m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
   m.enableInstancing=true;
   EnsureFolder(Path.GetDirectoryName(AshPath).Replace('\\','/'));
   AssetDatabase.CreateAsset(m,AshPath);AssetDatabase.SaveAssetIfDirty(m);
   return m;
  }

  /// <summary>What extinguishing would change under one Finish297_Attraction root (no writes).</summary>
  internal static List<Op> PlanExtinguish(Transform root)
  {
   var ops=new List<Op>();if(root==null)return ops;
   foreach(Transform beacon in root)
   {
    if(!beacon.name.StartsWith("Beacon_",StringComparison.Ordinal))continue;
    foreach(var name in Glowing){var t=beacon.Find(name);if(t!=null&&t.gameObject.activeSelf)ops.Add(new Op{path=PostLedger308.PathOf(t),kind="active",before="1",after="0"});}
    foreach(var name in StoneLods)
    {
     var r=beacon.Find(name)?.GetComponent<MeshRenderer>();if(r==null)continue;var mats=r.sharedMaterials;
     for(int i=0;i<mats.Length;i++)if(IsBeaconCore(mats[i]))ops.Add(new Op{path=PostLedger308.PathOf(r.transform),kind="material",slot=i,before=AssetDatabase.GetAssetPath(mats[i]),after=AshPath});
    }
   }
   return ops;
  }

  static void ApplyOp(Scene scene,Op op,Material ash)
  {
   var t=PostLedger308.Find(scene,op.path)??throw new Exception("missing "+op.path);
   if(op.kind=="active"){t.gameObject.SetActive(op.after=="1");EditorUtility.SetDirty(t.gameObject);return;}
   var r=t.GetComponent<MeshRenderer>();var mats=r.sharedMaterials;
   mats[op.slot]=op.after==AshPath?ash:AssetDatabase.LoadAssetAtPath<Material>(op.after);r.sharedMaterials=mats;EditorUtility.SetDirty(r);
  }

  /// <summary>Builder path (CompactFinish297 attraction): extinguish a freshly built root in the active scene; no ledger (the
  /// rebuild itself is the record). Returns the report line.</summary>
  internal static string ExtinguishFresh(Transform root)
  {
   var ops=PlanExtinguish(root);if(ops.Count==0)return "D308-6 beacons: nothing glowing";
   var ash=BeaconAsh();
   foreach(var op in ops)
   {
    if(op.kind=="active"){var g=FindUnder(root,op.path);if(g!=null)g.gameObject.SetActive(false);continue;}
    var rt=FindUnder(root,op.path)?.GetComponent<MeshRenderer>();if(rt==null)continue;var mats=rt.sharedMaterials;mats[op.slot]=ash;rt.sharedMaterials=mats;EditorUtility.SetDirty(rt);
   }
   return "D308-6 beacons extinguished: "+ops.Count(o=>o.kind=="active")+" glowing objects off, "+ops.Count(o=>o.kind=="material")+" core slots -> M_BeaconAsh308";
  }
  static Transform FindUnder(Transform root,string scenePath)
  {
   string prefix=PostLedger308.PathOf(root);if(!scenePath.StartsWith(prefix,StringComparison.Ordinal))return null;
   string rest=scenePath.Substring(prefix.Length).TrimStart('/');return rest.Length==0?root:root.Find(rest);
  }

  static string Beacons(Dictionary<string,string> o,bool dry)
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();var sb=new StringBuilder("wash308-beacons"+(dry?" (dry)":"")+"\n");
   Material ash=dry?AssetDatabase.LoadAssetAtPath<Material>(AshPath):BeaconAsh();
   if(!dry)sb.AppendLine("  material "+AshPath+" ("+ash.shader.name+", emission off)");
   foreach(var path in PostLedger308.Select(o))
   {
    var scene=PostLedger308.Open(path);sb.AppendLine(" "+PostLedger308.Short(path));
    var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==AttractionRoot);
    if(root==null){sb.AppendLine("  no "+AttractionRoot+" root (nothing to do)");continue;}
    var ops=PlanExtinguish(root.transform);
    sb.AppendLine("  beacons "+root.transform.Cast<Transform>().Count(t=>t.name.StartsWith("Beacon_",StringComparison.Ordinal))+", changes "+ops.Count+" ("+ops.Count(x=>x.kind=="active")+" objects off, "+ops.Count(x=>x.kind=="material")+" core slots)");
    if(ops.Count==0){sb.AppendLine("  no-op (already dark)");continue;}
    if(dry){foreach(var op in ops)sb.AppendLine("   "+op.kind+" "+op.path+(op.slot>=0?"#"+op.slot:"")+": "+op.before+" -> "+op.after);continue;}
    string backup=PostLedger308.Backup(BackupRoot,utc,path);ledger.backups.Add(backup);
    foreach(var op in ops)
    {
     ApplyOp(scene,op,ash);
     // keep the first "before" of an object/slot (idempotent re-runs never overwrite the original)
     if(!ledger.changes.Any(c=>c.scene==path&&c.kind==op.kind&&c.path==op.path&&c.slot==op.slot))
      ledger.changes.Add(new Change{scene=path,kind=op.kind,path=op.path,slot=op.slot,before=op.before,after=op.after,utc=utc});
    }
    PostLedger308.SaveScene(scene);WriteLedger(ledger);
    sb.AppendLine("  saved; backup "+backup);
   }
   return sb.Append(" ledger "+LedgerPath).ToString();
  }

  static string BeaconsRevert(Dictionary<string,string> o)
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();var sb=new StringBuilder("wash308-beacons:revert\n");
   foreach(var path in PostLedger308.Select(o))
   {
    var mine=ledger.changes.Where(c=>c.scene==path&&(c.kind=="active"||c.kind=="material")).Reverse().ToList();
    if(mine.Count==0){sb.AppendLine(" "+PostLedger308.Short(path)+": nothing recorded");continue;}
    var scene=PostLedger308.Open(path);string backup=PostLedger308.Backup(BackupRoot,utc,path);ledger.backups.Add(backup);int done=0;
    foreach(var c in mine)
    {
     var t=PostLedger308.Find(scene,c.path);if(t==null){sb.AppendLine("  missing "+c.path+" (kept in the ledger)");continue;}
     if(c.kind=="active"){if(t.gameObject.activeSelf!=(c.after=="1")){sb.AppendLine("  changed since apply: "+c.path+" (kept)");continue;}t.gameObject.SetActive(c.before=="1");EditorUtility.SetDirty(t.gameObject);}
     else
     {
      var r=t.GetComponent<MeshRenderer>();var mats=r!=null?r.sharedMaterials:null;
      if(mats==null||c.slot>=mats.Length||AssetDatabase.GetAssetPath(mats[c.slot])!=c.after){sb.AppendLine("  changed since apply: "+c.path+"#"+c.slot+" (kept)");continue;}
      mats[c.slot]=AssetDatabase.LoadAssetAtPath<Material>(c.before);r.sharedMaterials=mats;EditorUtility.SetDirty(r);
     }
     ledger.changes.Remove(c);done++;
    }
    PostLedger308.SaveScene(scene);WriteLedger(ledger);
    sb.AppendLine(" "+PostLedger308.Short(path)+": restored "+done+"; backup "+backup);
   }
   return sb.ToString();
  }

  static bool DrawsHaloPass(Renderer r)
  {
   foreach(var m in r.sharedMaterials)
   {
    if(m==null||m.shader==null)continue;
    if(m.shader.name=="Oheangbu/Finish297/BeaconHalo")return true;
    for(int p=0;p<m.passCount;p++)if(m.shader.FindPassTagValue(p,new ShaderTagId("LightMode")).name==HaloPass)return true;
   }
   return false;
  }

  static string HaloFeature(bool dry)
  {
   PostLedger308.RequireEditable();
   var sb=new StringBuilder("wash308-halo-feature"+(dry?" (dry)":"")+"\n");int drawing=0;
   foreach(var path in PostLedger308.Scenes)
   {
    var scene=PostLedger308.Open(path);
    var live=PostLedger308.All<Renderer>(scene).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&DrawsHaloPass(r)).ToArray();
    drawing+=live.Length;sb.AppendLine("  "+PostLedger308.Short(path)+": active "+HaloPass+" renderers "+live.Length+(live.Length>0?" ("+string.Join(", ",live.Take(6).Select(r=>PostLedger308.PathOf(r.transform)))+")":""));
   }
   var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath)??throw new PostLedger308.Refused("Renderer297 missing at "+RendererPath);
   bool present=data.rendererFeatures.Any(f=>f!=null&&f.name==HaloPass);
   sb.AppendLine("  Renderer297 feature "+HaloPass+": "+(present?"present":"absent"));
   if(!present)return sb.Append("  no-op").ToString();
   if(drawing>0)return sb.Append("  refused: "+drawing+" renderer(s) still draw the "+HaloPass+" pass (run wash308-beacons first)").ToString();
   if(dry)return sb.Append("  dry: would remove the feature (Renderer297 backed up first)").ToString();
   string utc=PostLedger308.Utc();string backup=PostLedger308.Backup(BackupRoot,utc,RendererPath);
   CompactRebuildAuthoring.RemoveBeaconHaloFeature308(data);AssetDatabase.SaveAssetIfDirty(data);
   var ledger=ReadLedger();ledger.backups.Add(backup);ledger.changes.Add(new Change{scene="",kind="halo-feature",path=RendererPath,before=backup,after="removed",utc=utc});WriteLedger(ledger);
   return sb.Append("  removed; features now "+string.Join(",",data.rendererFeatures.Where(f=>f!=null).Select(f=>f.name))+"; backup "+backup).ToString();
  }

  static string HaloFeatureRevert()
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();var rec=ledger.changes.LastOrDefault(c=>c.kind=="halo-feature");
   if(rec==null||!File.Exists(rec.before))throw new PostLedger308.Refused("no recorded Renderer297 backup");
   File.Copy(rec.before,PostLedger308.Abs(RendererPath),true);
   AssetDatabase.ImportAsset(RendererPath,ImportAssetOptions.ForceUpdate);
   ledger.changes.Remove(rec);WriteLedger(ledger);
   return "Renderer297 restored from "+rec.before;
  }

  // ---------- audit (AC-W2–W4) ----------
  static string Audit(Dictionary<string,string> o)
  {
   PostLedger308.RequireEditable();
   string label=o.TryGetValue("label",out var l)&&l.Length>0?"-"+l:"";
   var summary=new StringBuilder("wash308-audit\n");
   foreach(var path in PostLedger308.Select(o))
   {
    var scene=PostLedger308.Open(path);var sb=new StringBuilder("wash308-audit "+path+" "+PostLedger308.Utc()+"\n");
    int glowing=0,coreLeft=0,ashSlots=0;
    var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==AttractionRoot);
    if(root==null)sb.AppendLine("beacons: no "+AttractionRoot+" root");
    else foreach(Transform b in root.transform)
    {
     if(!b.name.StartsWith("Beacon_",StringComparison.Ordinal))continue;
     var on=Glowing.Where(n=>b.Find(n)!=null&&b.Find(n).gameObject.activeSelf).ToArray();glowing+=on.Length;
     int lodRenderers=0,core=0,ash=0;
     foreach(var n in StoneLods){var r=b.Find(n)?.GetComponent<MeshRenderer>();if(r==null)continue;lodRenderers++;foreach(var m in r.sharedMaterials){if(IsBeaconCore(m))core++;if(m!=null&&AssetDatabase.GetAssetPath(m)==AshPath)ash++;}}
     coreLeft+=core;ashSlots+=ash;var lod=b.GetComponent<LODGroup>();
     sb.AppendLine("beacon "+b.name+" at "+b.position.ToString("F2")+" rot "+b.eulerAngles.ToString("F1")+" | stone renderers "+lodRenderers+" LODs "+(lod!=null?lod.lodCount:0)+" colliders "+b.GetComponentsInChildren<Collider>(true).Length+
      " | glowing on: "+(on.Length>0?string.Join(",",on):"none")+" | core slots "+core+" ash slots "+ash);
    }
    var all=PostLedger308.All<Renderer>(scene).Where(r=>!PostLedger308.UnderProtectedTree(r.transform)).ToArray();
    var halo=all.Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&DrawsHaloPass(r)).ToArray();
    sb.AppendLine("halo pass renderers active: "+halo.Length);
    // persistent emission census: light-source shaders or an enabled Lit emission with a non-black colour
    var census=new Dictionary<string,int>();int outside=0,beaconCat=0;
    foreach(var r in all.Where(r=>r.enabled&&r.gameObject.activeInHierarchy))
     foreach(var m in r.sharedMaterials.Where(m=>m!=null&&m.shader!=null).Distinct())
     {
      bool lightShader=LightShaders.Contains(m.shader.name);
      bool emissive=m.IsKeywordEnabled("_EMISSION")&&m.HasProperty("_EmissionColor")&&m.GetColor("_EmissionColor").maxColorComponent>0f;
      if(!lightShader&&!emissive)continue;
      bool beacon=IsBeaconCore(m)||m.shader.name=="Oheangbu/Finish297/BeaconHalo";
      bool allowed=!beacon&&(m.shader.name==FarGlowShader?r.name==FarGlowCard:AllowedGlow.Any(a=>m.name.IndexOf(a,StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf(a,StringComparison.OrdinalIgnoreCase)>=0));
      string key=(beacon?"BEACON ":allowed?"allowed ":"OUTSIDE ")+m.name+" ("+m.shader.name+")";
      census[key]=census.TryGetValue(key,out var c)?c+1:1;if(beacon)beaconCat++;else if(!allowed)outside++;
     }
    sb.AppendLine("persistent emission (renderers per material):");foreach(var kv in census.OrderBy(k=>k.Key))sb.AppendLine("  "+kv.Key+": "+kv.Value);
    var lights=PostLedger308.All<Light>(scene).Where(x=>x.enabled&&x.gameObject.activeInHierarchy&&!PostLedger308.UnderProtectedTree(x.transform)).ToArray();
    sb.AppendLine("lights: "+lights.Length+" ("+string.Join(", ",lights.GroupBy(x=>x.type+" "+Category(x)).Select(g=>g.Key+" x"+g.Count()))+")");
    string file=Path.Combine(ChecksDir,"wash308-audit-"+PostLedger308.Short(path)+label+".txt");File.WriteAllText(file,sb.ToString());
    bool w2=glowing==0&&coreLeft==0&&halo.Length==0;
    summary.AppendLine(" "+PostLedger308.Short(path)+": "+(w2?"AC-W2 ok":"AC-W2 FAIL")+" (glowing objects "+glowing+", core slots "+coreLeft+", ash slots "+ashSlots+", halo renderers "+halo.Length+") | AC-W3 beacon-category "+beaconCat+", outside allow list "+outside+" | lights "+lights.Length+" -> "+file);
   }
   return summary.Append(" AC-W4: compare two audit files (label=before / label=after): beacon positions, stone renderers, LODs, colliders").ToString();
  }

  static string Category(Light l)
  {
   if(l.type==LightType.Directional)return "sun";
   string n=PostLedger308.PathOf(l.transform);
   if(n.Contains(AttractionRoot))return "beacon";
   foreach(var a in AllowedGlow)if(n.IndexOf(a,StringComparison.OrdinalIgnoreCase)>=0)return a;
   return n.IndexOf("Lantern",StringComparison.OrdinalIgnoreCase)>=0?"lantern":n.IndexOf("Mine",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Portal",StringComparison.OrdinalIgnoreCase)>=0?"mine":"other";
  }
 }
}
