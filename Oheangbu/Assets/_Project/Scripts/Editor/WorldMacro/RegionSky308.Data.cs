using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class RegionSky308
 {
  // ART-SKY seed real values (SPEC-REGION-SKY-308 시드 표, sRGB, TEST). Cloud = 구름 씻김; CloudDensity = min(덮임, .5) (clamp kept).
  // RealmInk (WorldLocationCatalog) is never used for the sky (ART-COLOR). Step-2 columns (구름 먹, 성격) come from the seeds JSON
  // through data:apply:step=2 (RegionSky308.Sky2.cs).
  internal static readonly (RealmId realm,Color horizon,Color zenith,Color cloud,float cover,Color fog)[] Seeds308={
   (RealmId.Cheongrim,  new Color(.82f,.86f,.79f),new Color(.50f,.60f,.55f),new Color(.60f,.66f,.60f),.55f,new Color(.70f,.76f,.70f)),
   (RealmId.Jeokro,     new Color(.88f,.77f,.64f),new Color(.60f,.55f,.52f),new Color(.62f,.54f,.48f),.50f,new Color(.80f,.72f,.64f)),
   (RealmId.Cheolong,   new Color(.83f,.87f,.90f),new Color(.54f,.62f,.71f),new Color(.74f,.78f,.82f),.30f,new Color(.72f,.77f,.82f)),
   (RealmId.Hyeongang,  new Color(.80f,.85f,.86f),new Color(.42f,.49f,.55f),new Color(.60f,.66f,.70f),.62f,new Color(.68f,.75f,.78f)),
   (RealmId.Hwanggyeong,new Color(.92f,.88f,.76f),new Color(.72f,.71f,.62f),new Color(.84f,.80f,.70f),.22f,new Color(.80f,.78f,.70f))};
  // TEST numbers of the Spec (혼합·스냅·앵커·강조)
  internal const float Blend308=350f,Response308=2.5f,Snap308=60f,Capital308=.025f;
  internal const float SkyFogFollow308=.6f,SkyInkTone308=.35f;

  static RegionalInkSkyProfile.Entry[] SeedEntries()=>Seeds308.Select(s=>new RegionalInkSkyProfile.Entry{Realm=s.realm,Horizon=s.horizon,Zenith=s.zenith,Cloud=s.cloud,CloudDensity=Mathf.Min(s.cover,.5f)}).ToArray();

  /// <summary>Sub-region accents A1–A3 with their reference points baked from the catalog centres (AC-S16 checks them).</summary>
  internal static RegionalInkSkyProfile.Accent[] Accents308(WorldLocationCatalog catalog)
  {
   Vector2 P(string id)
   {
    var e=catalog.Entries?.FirstOrDefault(x=>x!=null&&x.Id==id)??throw new PostLedger308.Refused("catalog id '"+id+"' missing in "+AssetDatabase.GetAssetPath(catalog));
    return new Vector2(e.Centre.x,e.Centre.z);
   }
   // A1 금표의 길 -> 물든 심부 -> 성역: arc-length share of the middle point for the two-value columns (+0 -> +.08, +0 -> +.06)
   var a1=new[]{P("geumpyo_inn"),P("deep_forest"),P("sanctuary")};
   float s1=Vector2.Distance(a1[0],a1[1]),total=s1+Vector2.Distance(a1[1],a1[2]),mid=total>0?s1/total:.5f;
   var hwFog=Seeds308.First(s=>s.realm==RealmId.Hwanggyeong).fog;
   return new[]{
    new RegionalInkSkyProfile.Accent{Id="A1_geumpyo_to_deep",Kind=RegionalInkSkyProfile.AccentKind.Gradient,Realm=RealmId.Cheongrim,ScaleByRealmWeight=true,
     PointIds=new[]{"geumpyo_inn","deep_forest","sanctuary"},Points=a1,Brightness=new[]{1.06f,.90f,.86f},CoverAdd=new[]{0f,.08f*mid,.08f},MistAdd=new[]{0f,.06f*mid,.06f},
     HorizonPull=new float[0],SideWidth=250f,SideFalloff=120f},
    new RegionalInkSkyProfile.Accent{Id="A2_sanggyeong_to_south_gate",Kind=RegionalInkSkyProfile.AccentKind.Gradient,Realm=RealmId.Hwanggyeong,ScaleByRealmWeight=true,
     PointIds=new[]{"inspection_two","south_gate"},Points=new[]{P("inspection_two"),P("south_gate")},Brightness=new float[0],CoverAdd=new float[0],MistAdd=new float[0],
     HorizonPull=new[]{0f,.15f},PullColor=hwFog,SideWidth=250f,SideFalloff=120f},
    // A3: the Spec's figures (황경 실효 .66, 적로 .28 at S08) are a .5 pull not scaled by the 적로 weight -> ScaleByRealmWeight false
    new RegionalInkSkyProfile.Accent{Id="A3_inspection_one",Kind=RegionalInkSkyProfile.AccentKind.Override,Realm=RealmId.Jeokro,ScaleByRealmWeight=false,
     PointIds=new[]{"inspection_one"},Points=new[]{P("inspection_one")},TowardRealm=RealmId.Hwanggyeong,Pull=.5f,Radius=220f,Falloff=120f}};
  }

  static string DataApply(string step,Dictionary<string,string> o)
  {
   if(step=="2")return DataApply2(o);
   if(step!="1b")throw new PostLedger308.Refused("unknown step '"+step+"' (1b | 2)");
   PostLedger308.RequireEditable();
   var catalog=LoadGuid<WorldLocationCatalog>(CatalogGuid)??throw new PostLedger308.Refused("catalog "+CatalogGuid+" missing");
   var accents=Accents308(catalog);
   var ledger=ReadJson<DataLedger>(DataLedgerPath);bool first=string.IsNullOrEmpty(ledger.utc);
   var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath);
   if(first){ledger.utc=PostLedger308.Utc();ledger.profileCreated=profile==null;ledger.profileSnapshot=profile!=null?EditorJsonUtility.ToJson(profile):"";}
   var sb=new StringBuilder("data:apply step 1b\n");
   if(profile==null)
   {
    EnsureFolder(Path.GetDirectoryName(ProfilePath).Replace('\\','/'));
    profile=ScriptableObject.CreateInstance<RegionalInkSkyProfile>();
    var p297=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(Profile297Path);if(p297!=null)profile.DefaultRealm=p297.DefaultRealm;
    AssetDatabase.CreateAsset(profile,ProfilePath);sb.AppendLine("  created "+ProfilePath+" (RegionalSky297 untouched)");
   }
   string before=EditorJsonUtility.ToJson(profile);
   profile.BlendDistance=Blend308;profile.ResponseSeconds=Response308;profile.SnapDistance=Snap308;
   profile.CapitalAtmosphere=Capital308;profile.CapitalRealm=RealmId.Hwanggyeong;
   profile.Regions=SeedEntries();profile.Accents=accents;
   if(EditorJsonUtility.ToJson(profile)!=before){EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);sb.AppendLine("  profile written: blend "+Blend308+" m, response "+Response308+" s, snap "+Snap308+" m, capital "+Capital308+", "+profile.Regions.Length+" seeds, "+accents.Length+" accents");}
   else sb.AppendLine("  profile already at the #308 values (no-op)");
   foreach(var a in accents)sb.AppendLine("    "+a.Id+" "+a.Kind+" "+string.Join(" -> ",a.PointIds.Select((id,i)=>id+"("+a.Points[i].x.ToString("F0")+","+a.Points[i].y.ToString("F0")+")")));
   foreach(var (mat,prop,value) in new[]{(FogMatPath,"_SkyFogFollow",SkyFogFollow308),(InkMatPath,"_SkyInkTone",SkyInkTone308)})
   {
    var m=AssetDatabase.LoadAssetAtPath<Material>(mat);
    if(m==null){sb.AppendLine("  refused: material missing "+mat);continue;}
    if(!m.HasProperty(prop)){sb.AppendLine("  refused: field missing "+prop+" on "+m.shader.name+" (deploy the #308 shader first, then re-run data:apply)");continue;}
    float now=m.GetFloat(prop);
    var rec=ledger.materials.FirstOrDefault(x=>x.material==mat&&x.property==prop&&StepOf(x)=="1b");
    if(rec==null){rec=new MatValue{material=mat,property=prop,before=now,existed=true,step="1b"};ledger.materials.Add(rec);}
    rec.after=value;
    if(Mathf.Abs(now-value)>1e-6f){m.SetFloat(prop,value);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);sb.AppendLine("  "+Path.GetFileName(mat)+" "+prop+" "+PostLedger308.F(now)+" -> "+PostLedger308.F(value));}
    else sb.AppendLine("  "+Path.GetFileName(mat)+" "+prop+" already "+PostLedger308.F(value));
   }
   WriteJson(DataLedgerPath,ledger);
   return sb.Append("  ledger "+DataLedgerPath).ToString();
  }

  static string DataRevert()
  {
   PostLedger308.RequireEditable();
   if(!File.Exists(DataLedgerPath))throw new PostLedger308.Refused("no data ledger at "+DataLedgerPath);
   var scene=ReadJson<SceneLedger>(SceneLedgerPath);
   if(scene.items.Any(i=>i.item=="L4"))throw new PostLedger308.Refused("scene ledger still points drivers at RealmInkSky308 (L4): run ledger:revert:step=2 first");
   if(scene.items.Any(i=>i.item=="L2"))throw new PostLedger308.Refused("scene ledger still points drivers at RegionalSky308 (L2): run ledger:revert:step=1b first");
   var ledger=ReadJson<DataLedger>(DataLedgerPath);var sb=new StringBuilder("data:revert\n");
   // newest first: a step-2 record restores the 1b value, then the 1b record the original one
   for(int i=ledger.materials.Count-1;i>=0;i--)
   {
    var rec=ledger.materials[i];
    if(ledger.materialCreated&&rec.material==Sky2MatPath)continue;   // deleted below
    RestoreMaterialValue(rec,out string line);sb.AppendLine("  "+line);
   }
   if(ledger.materialCreated&&AssetDatabase.LoadAssetAtPath<Material>(Sky2MatPath)!=null){AssetDatabase.DeleteAsset(Sky2MatPath);sb.AppendLine("  deleted "+Sky2MatPath+" (created by data:apply:step=2)");}
   var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath);
   if(profile!=null)
   {
    if(ledger.profileCreated){AssetDatabase.DeleteAsset(ProfilePath);sb.AppendLine("  deleted "+ProfilePath+" (created by data:apply)");}
    else if(!string.IsNullOrEmpty(ledger.profileSnapshot)){EditorJsonUtility.FromJsonOverwrite(ledger.profileSnapshot,profile);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);sb.AppendLine("  profile restored from snapshot");}
   }
   File.Delete(DataLedgerPath);
   return sb.Append("  ledger removed").ToString();
  }

  static void EnsureFolder(string folder)
  {
   if(AssetDatabase.IsValidFolder(folder))return;
   string parent=Path.GetDirectoryName(folder).Replace('\\','/');EnsureFolder(parent);
   AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
  }
 }
}
