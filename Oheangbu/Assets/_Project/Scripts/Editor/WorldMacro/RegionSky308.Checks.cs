using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 checks (SPEC-REGION-SKY-308 AC). Calculation and scene-data checks only; capture ACs (S4–S7, S11–S13, S20), the Play part
 // of AC-S1/S10, the perf ACs (S18/S19) and the human PASS items are separate steps (capture/perf tools, user).
 public static partial class RegionSky308
 {
  static readonly string[] LayoutPaths={
   "Assets/_Project/Art/World/Architecture296/Data/211377a1cf4052f47905e2d4c976f7c4_566363b0e71d6e5499a8f6599e450127_782f53e1c87540e449a7951bb733ff7a_WorldLayout.asset",
   "Assets/_Project/Art/Characters/Folklore298/Data/WorldLayout298.asset",
   "Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset"};
  static readonly Vector2 SpecCentroid=new Vector2(1871f,3021f);   // Spec 계산값 (황경 면적 무게중심)
  static readonly string[] RecognitionFiles={"DrawingInputController","RecognitionPipeline","JamoMatcher","HangulComposer","JamoTemplateLibrarySO"};
  const string SkyProfileGuid="722c3e076dfc69744b8873be50b1f2c9";

  // ---------- colour (sRGB D65 -> CIELAB) ----------
  internal static Vector3 Lab(Color c)
  {
   float Lin(float v)=>v<=.04045f?v/12.92f:Mathf.Pow((v+.055f)/1.055f,2.4f);
   float r=Lin(c.r),g=Lin(c.g),b=Lin(c.b);
   float x=(.4124f*r+.3576f*g+.1805f*b)/.95047f,y=.2126f*r+.7152f*g+.0722f*b,z=(.0193f*r+.1192f*g+.9505f*b)/1.08883f;
   float F(float t)=>t>.008856f?Mathf.Pow(t,1f/3f):7.787f*t+16f/116f;
   float fx=F(x),fy=F(y),fz=F(z);return new Vector3(116f*fy-16f,500f*(fx-fy),200f*(fy-fz));
  }
  internal static float Chroma(Vector3 lab)=>Mathf.Sqrt(lab.y*lab.y+lab.z*lab.z);

  static RegionalInkSkyProfile ProfileOrRefuse()=>AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath)??throw new PostLedger308.Refused("RegionalSky308 missing: run data:apply first");
  static WorldMacroSheetSO Geography()=>LoadGuid<WorldMacroSheetSO>(GeographyGuid)??throw new PostLedger308.Refused("geography sheet "+GeographyGuid+" missing");

  // ---------- geometry ----------
  static string Norm(string id){id=(id??"").ToLowerInvariant();return id.StartsWith("realm_",StringComparison.Ordinal)?id.Substring(6):id;}
  static float PolygonDelta(Vector2[] a,Vector2[] b)
  {
   if(a==null||b==null||a.Length!=b.Length||a.Length==0)return float.PositiveInfinity;
   float best=float.PositiveInfinity;int n=a.Length;
   for(int dir=0;dir<2;dir++)for(int shift=0;shift<n;shift++)
   {
    float m=0;for(int i=0;i<n&&m<best;i++){int j=dir==0?(i+shift)%n:((shift-i)%n+n)%n;m=Mathf.Max(m,Vector2.Distance(a[i],b[j]));}
    best=Mathf.Min(best,m);
   }
   return best;
  }

  static string GeometryCheck(out bool ok)
  {
   bool good=true;var sheet=Geography();var catalog=LoadGuid<WorldLocationCatalog>(CatalogGuid)??throw new PostLedger308.Refused("catalog missing");
   var sb=new StringBuilder("geometry-check: canon = "+AssetDatabase.GetAssetPath(sheet)+" (WorldMacroSheetSO.Regions)\n");
   var canon=new Dictionary<string,Vector2[]>();
   foreach(var r in sheet.Regions??new WorldMacroSheetSO.RegionSpec[0])if(r!=null){string k=Norm(r.Realm.ToString());if(canon.ContainsKey(k))sb.AppendLine("  note: canon has more than one polygon for "+k+" (first used)");else canon[k]=r.Polygon;}
   void Compare(string source,IEnumerable<(string id,Vector2[] poly)> copies)
   {
    var map=copies.Where(c=>c.poly!=null&&c.poly.Length>=3).GroupBy(c=>Norm(c.id)).ToDictionary(g=>g.Key,g=>g.First().poly);
    foreach(var kv in canon)
    {
     if(!map.TryGetValue(kv.Key,out var poly)){sb.AppendLine("  FAIL "+source+" "+kv.Key+": missing");good=false;continue;}
     float d=PolygonDelta(kv.Value,poly);bool pass=d<=.01f;good&=pass;
     sb.AppendLine("  "+(pass?"ok  ":"FAIL")+" "+source+" "+kv.Key+": "+poly.Length+" vertices, max delta "+(float.IsInfinity(d)?"(count differs: "+kv.Value.Length+" vs "+poly.Length+")":d.ToString("0.###",CultureInfo.InvariantCulture)+" m"));
    }
   }
   Compare("catalog",(catalog.Entries??new WorldLocationCatalog.Entry[0]).Where(e=>e!=null&&e.Priority==0).Select(e=>(e.Id,e.Polygon)));
   foreach(var lp in LayoutPaths)
   {
    var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(lp);
    if(layout==null){sb.AppendLine("  FAIL layout missing "+lp);good=false;continue;}
    Compare(Path.GetFileName(lp).Length>40?"layout296":Path.GetFileNameWithoutExtension(lp),(layout.Realms??new CompactWorldLayoutSO.RealmArea[0]).Where(a=>a!=null).Select(a=>(a.Id,a.Polygon)));
   }
   // accent reference points = catalog centres
   var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath);
   if(profile?.Accents!=null)
    foreach(var a in profile.Accents.Where(x=>x!=null))
     for(int i=0;i<(a.PointIds?.Length??0);i++)
     {
      var e=catalog.Entries.FirstOrDefault(x=>x!=null&&x.Id==a.PointIds[i]);
      float d=e==null||a.Points==null||i>=a.Points.Length?float.PositiveInfinity:Vector2.Distance(a.Points[i],new Vector2(e.Centre.x,e.Centre.z));
      bool pass=d<=.01f;good&=pass;sb.AppendLine("  "+(pass?"ok  ":"FAIL")+" accent "+a.Id+" point "+a.PointIds[i]+" vs catalog centre: "+(float.IsInfinity(d)?"missing":d.ToString("0.###",CultureInfo.InvariantCulture)+" m"));
     }
   ok=good;return sb.Append(good?"  PASS":"  FAIL").ToString();
  }

  // ---------- weights ----------
  static string Weights()
  {
   var profile=ProfileOrRefuse();var sheet=Geography();var file=RegionSky308Capture.Stations();
   var stepper=new RegionalSkyStepper();var raw=new float[Mathf.Max(8,sheet.Regions.Length)];
   var sb=new StringBuilder("weights (B "+profile.BlendDistance+" m; after accents; calculation)\n");
   var realms=(RealmId[])Enum.GetValues(typeof(RealmId));
   RegionalInkSkyProfile.Centroid(sheet,profile.CapitalRealm,out var centroid);
   sb.AppendLine("  capital centroid "+centroid.ToString("F0")+" (Spec "+SpecCentroid.ToString("F0")+")");
   foreach(var s in file.stations)
   {
    var p=new Vector3(s.x,0,s.z);var state=stepper.Evaluate(profile,sheet,p);var w=stepper.Weights;
    profile.GetWeights(sheet,p,raw);
    string Row(float[] ws)=>string.Join(" ",realms.Select(r=>r.ToString().Substring(0,4)+" "+RegionalInkSkyProfile.RealmWeight(sheet,ws,r).ToString("0.00",CultureInfo.InvariantCulture)));
    float az=RegionalInkSkyProfile.AzimuthTo(p,centroid)*Mathf.Rad2Deg;
    sb.AppendLine("  "+s.id+" "+s.name+": "+Row(w)+" | raw "+Row(raw)+" | capital "+state.Capital.ToString("0.0000",CultureInfo.InvariantCulture)+" bearing "+az.ToString("F1")+" deg | zenith L* "+Lab(state.Zenith).x.ToString("F1")+" density "+state.CloudDensity.ToString("0.00",CultureInfo.InvariantCulture)+Shape2Row(state));
   }
   return sb.ToString();
  }

  /// <summary>Step-2 values of a state (expected cloud character for AC-S20), empty without step-2 data.</summary>
  static string Shape2Row(RegionalInkSkyProfile.SkyState s)
  {
   if(!(s.Shape>0f))return "";
   string F(float v)=>v.ToString("0.00",CultureInfo.InvariantCulture);
   return " | step2 cover "+F(s.Coverage)+" band "+s.BandYaw.ToString("F0",CultureInfo.InvariantCulture)+" deg storm "+s.StormYaw.ToString("F0",CultureInfo.InvariantCulture)+
    " deg stretch "+F(s.Stretch)+" mist "+F(s.Mist)+" ink "+Hex(s.CloudInk)+" fog "+Hex(s.Fog)+(s.Shape<.999f?" (shape share "+F(s.Shape)+")":"");
  }

  // ---------- hashes (AC-S15 / S17 baseline) ----------
  [Serializable] sealed class HashEntry{public string path,sha;}
  [Serializable] sealed class HashFile{public string utc;public List<HashEntry> files=new List<HashEntry>();}
  static string[] HashTargets()
  {
   var l=new List<string>{ProfilePath,FogMatPath,InkMatPath,AssetDatabase.GUIDToAssetPath(SkyProfileGuid),Profile297Path,"Assets/_Project/Shaders/InkCloudSky.shader","Assets/_Project/Art/Teaser300/InkWashSky300.shader"};
   // step 2: the RealmInkSky308 material (the driver only writes its transient copy) once data:apply:step=2 created it
   if(AssetDatabase.LoadAssetAtPath<Material>(Sky2MatPath)!=null)l.Add(Sky2MatPath);
   l.AddRange(AssetDatabase.FindAssets("WorldRealmAtmosphere t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith("/WorldRealmAtmosphere.cs",StringComparison.Ordinal)));
   // AC-S17 "템플릿 해시 그대로": template assets are git-ignored (#307 repo policy), so git status cannot see them — hash them here
   l.AddRange(AssetDatabase.FindAssets("t:JamoTemplateLibrarySO").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p,StringComparer.Ordinal));
   return l.Where(p=>!string.IsNullOrEmpty(p)).Distinct().ToArray();
  }
  static string Sha(string assetPath){string abs=PostLedger308.Abs(assetPath);if(!File.Exists(abs))return "missing";using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(abs))).Replace("-","").ToLowerInvariant();}
  static string HashPath=>Path.Combine(OutDir,"hash-baseline.json");
  static string RecordHashes()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new PostLedger308.Refused("record the baseline in Edit mode");
   var f=new HashFile{utc=PostLedger308.Utc()};foreach(var p in HashTargets())f.files.Add(new HashEntry{path=p,sha=Sha(p)});
   WriteJson(HashPath,f);return "hash baseline: "+f.files.Count+" files -> "+HashPath;
  }

  // ---------- checks ----------
  static string Checks(Dictionary<string,string> o)
  {
   var sb=new StringBuilder("RegionSky308 checks "+PostLedger308.Utc()+"\n");int fail=0;
   void Line(bool pass,string text){if(!pass)fail++;sb.AppendLine((pass?"  ok   ":"  FAIL ")+text);}
   void Note(string text)=>sb.AppendLine("  --   "+text);
   var profile=ProfileOrRefuse();var sheet=Geography();var file=RegionSky308Capture.Stations();

   // AC-S1 (scene data): count 1, disabled, #263 invariants, ledger values (L2/L3) in every selected scene
   bool scenes=!(o.TryGetValue("scene",out var sc)&&sc=="none");
   if(scenes)
   {
    PostLedger308.RequireEditable();
    var ledger=ReadJson<SceneLedger>(SceneLedgerPath);
    foreach(var path in PostLedger308.Select(o))
    {
     var scene=PostLedger308.Open(path);string tag="AC-S1 "+PostLedger308.Short(path)+": ";
     var atms=PostLedger308.All<WorldRealmAtmosphere>(scene).ToArray();
     Line(atms.Length==1,tag+"WorldRealmAtmosphere count "+atms.Length);
     if(atms.Length==1)
     {
      var a=atms[0];Line(!a.enabled,tag+"m_Enabled "+(a.enabled?1:0));
      Line(a.Catalog!=null&&AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a.Catalog))==CatalogGuid,tag+"Catalog = "+CatalogGuid);
      Line(a.TintStrength<=.06f,tag+"TintStrength "+a.TintStrength.ToString("0.###",CultureInfo.InvariantCulture)+" <= .06");
      var snap=ledger.items.FirstOrDefault(x=>x.scene==path&&x.item=="L1.snapshot");
      if(snap!=null)Line(snap.before==Snapshot263(a),tag+"Catalog/View/SkySource/TintStrength unchanged since the ledger snapshot");
      else Note(tag+"no L1 snapshot yet (ledger:apply:step=1a not run)");
     }
     var drivers=PostLedger308.All<WorldLookDriver>(scene).ToArray();
     if(drivers.Length==1)
     {
      var so=new SerializedObject(drivers[0]);
      foreach(var rec in ledger.items.Where(x=>x.scene==path&&(x.item=="L2"||x.item=="L4"||x.item.StartsWith("L3",StringComparison.Ordinal))))
      {
       var prop=so.FindProperty(rec.property);string cur=prop==null?"(field missing)":prop.isArray&&prop.propertyType!=SerializedPropertyType.String?Paths(prop):PostLedger308.AssetRef(prop.objectReferenceValue);
       Line(cur==rec.after,"ledger "+PostLedger308.Short(path)+" "+rec.item+" "+rec.property+" = "+(cur.Length>0?cur:"null"));
      }
     }
     else Line(false,tag+"WorldLookDriver count "+drivers.Length);
    }
    Note("AC-S1 Play part (no active Skybox on the player camera, RenderSettings.skybox = driver transient): Play capture step");
   }

   // AC-S2 weights
   var stepper=new RegionalSkyStepper();
   foreach(var s in file.stations.Where(x=>x.min>=0))
   {
    stepper.Evaluate(profile,sheet,new Vector3(s.x,0,s.z));
    if(!Enum.TryParse<RealmId>(s.realm,out var realm)){Line(false,"AC-S2 "+s.id+" bad realm "+s.realm);continue;}
    float w=RegionalInkSkyProfile.RealmWeight(sheet,stepper.Weights,realm);
    Line(w>=s.min,"AC-S2 "+s.id+" "+s.name+" "+realm+" "+w.ToString("0.00",CultureInfo.InvariantCulture)+" >= "+s.min.ToString("0.00",CultureInfo.InvariantCulture));
   }

   // AC-S3 seeds (data)
   var labs=profile.Regions.Where(e=>e!=null).Select(e=>(e.Realm,h:Lab(e.Horizon),z:Lab(e.Zenith))).ToArray();
   foreach(var l in labs)
   {
    Line(l.h.x>=80f,"AC-S3 "+l.Realm+" horizon L* "+l.h.x.ToString("F1")+" >= 80");
    Line(Chroma(l.h)<=22f&&Chroma(l.z)<=22f,"AC-S3 "+l.Realm+" C* horizon "+Chroma(l.h).ToString("F1")+" zenith "+Chroma(l.z).ToString("F1")+" <= 22");
   }
   if(labs.Length>1)
   {
    float span=labs.Max(l=>l.z.x)-labs.Min(l=>l.z.x);Line(span>=15f,"AC-S3 zenith L* span "+span.ToString("F1")+" >= 15");
    float minDe=float.PositiveInfinity;string pair="";
    for(int i=0;i<labs.Length;i++)for(int j=i+1;j<labs.Length;j++){float de=Vector3.Distance(labs[i].z,labs[j].z);if(de<minDe){minDe=de;pair=labs[i].Realm+"-"+labs[j].Realm;}}
    Line(minDe>=12f,"AC-S3 zenith pair dE76 min "+minDe.ToString("F1")+" ("+pair+") >= 12");
   }
   // step-2 data (only once data:apply:step=2 ran): every realm carries it, 담채 ink (L* >= 12, C* <= 22, darker than its wash),
   // the material on a shader without errors. The capture part (AC-S20 cover +-.10, band +-15 deg, motion) is the capture step.
   if(profile.Regions.Any(e=>e!=null&&e.HasShape))
   {
    Line(profile.HasShapeData,"step2 every realm carries step-2 data ("+profile.Regions.Count(e=>e!=null&&e.HasShape)+"/"+profile.Regions.Length+")");
    foreach(var e in profile.Regions.Where(x=>x!=null&&x.HasShape))
    {
     var ink=Lab(e.CloudInk);var wash=Lab(e.Cloud);
     Line(ink.x>=12f&&Chroma(ink)<=22f&&ink.x<wash.x,"step2 "+e.Realm+" ink "+Hex(e.CloudInk)+" L* "+ink.x.ToString("F1")+" C* "+Chroma(ink).ToString("F1")+" < wash L* "+wash.x.ToString("F1")+" (담채, no pure black)");
     Line(e.Coverage>=0f&&e.Coverage<=1f&&e.Softness>=.005f&&e.MistHeight>=.01f,"step2 "+e.Realm+" cover "+e.Coverage.ToString("0.00",CultureInfo.InvariantCulture)+" soft "+e.Softness.ToString("0.000",CultureInfo.InvariantCulture)+" mist "+e.MistHeight.ToString("0.00",CultureInfo.InvariantCulture)+" in range");
     if(e.FogTint.a>0f){var fog=Lab(e.FogTint);Line(Chroma(fog)<=22f,"step2 "+e.Realm+" fog tint "+Hex(e.FogTint)+" C* "+Chroma(fog).ToString("F1")+" <= 22");}
    }
    var mat=AssetDatabase.LoadAssetAtPath<Material>(Sky2MatPath);
    string why=null;var shader=mat!=null?Sky2Shader(out why):null;
    Line(mat!=null&&shader!=null&&mat.shader==shader,"step2 material "+Sky2MatPath+(mat==null?" missing":shader==null?": "+why:" on \""+Sky2ShaderName+"\" (no errors)"));
    Note("AC-S19 perf307 ab \"sky308:1\" tier=pc and AC-S20 (capture: cover +-.10, band +-15 deg, motion over 10 s): separate steps");
   }

   int realmInk=0;foreach(var name in new[]{"WorldLookDriver","RegionalInkSkyProfile","RegionalSkyStepper"})
    foreach(var p in AssetDatabase.FindAssets(name+" t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).Where(p=>Path.GetFileNameWithoutExtension(p)==name))
     realmInk+=File.ReadAllLines(PostLedger308.Abs(p)).Count(x=>x.Contains("RealmInk(")||x.Contains(".RealmInk"));
   Line(realmInk==0,"AC-S3 RealmInk references on the sky path: "+realmInk);

   // AC-S8 snap (stepper simulation with the profile; the driver runs the same instance type)
   {
    var st=new RegionalSkyStepper();var a=Station(file,"S02");var b=Station(file,"S11");
    st.Step(profile,sheet,a,1/60f,false,false);
    st.Step(profile,sheet,a+new Vector3(30,0,0),1/60f,false,false);bool smallSnap=st.Snapped;
    st.Step(profile,sheet,b,1/60f,false,false);
    var t=st.Target;var s=st.State;
    float diff=Mathf.Max(MaxDiff(t.Horizon,s.Horizon),Mathf.Max(MaxDiff(t.Zenith,s.Zenith),MaxDiff(t.Cloud,s.Cloud)));
    Line(profile.SnapDistance>0&&st.Snapped&&diff<1f/512f,"AC-S8 move S02 -> S11 ("+Vector3.Distance(a,b).ToString("F0")+" m > "+profile.SnapDistance+"): snapped, channel diff "+diff.ToString("0.######",CultureInfo.InvariantCulture));
    Line(!smallSnap,"AC-S8 30 m move is exponential (no snap)");
   }

   // AC-S9 capital anchor (static part)
   Line(profile.CapitalAtmosphere>=.02f&&profile.CapitalAtmosphere<=.03f,"AC-S9 CapitalAtmosphere "+profile.CapitalAtmosphere.ToString("0.###",CultureInfo.InvariantCulture)+" in [.02, .03]");
   bool hasC=RegionSky308Capture.CapitalCentroid(sheet,out var independent);
   RegionalInkSkyProfile.Centroid(sheet,profile.CapitalRealm,out var centroid);
   Line(hasC&&Vector2.Distance(centroid,independent)<.5f,"AC-S9 centroid "+centroid.ToString("F0")+" (independent "+independent.ToString("F0")+", Spec "+SpecCentroid.ToString("F0")+")");
   float worstBearing=0;
   foreach(var s in file.stations)
   {
    var st=new RegionalSkyStepper();var p=new Vector3(s.x,0,s.z);st.Step(profile,sheet,p,0,false,true);
    float expect=Mathf.Atan2(independent.x-s.x,independent.y-s.z)*Mathf.Rad2Deg,got=st.CapitalAzimuth*Mathf.Rad2Deg;
    worstBearing=Mathf.Max(worstBearing,Mathf.Abs(Mathf.DeltaAngle(expect,got)));
    float hw=RegionalInkSkyProfile.RealmWeight(sheet,st.Weights,profile.CapitalRealm);
    if(Mathf.Abs(st.State.Capital-profile.CapitalAtmosphere*(1-Mathf.Clamp01(hw)))>1e-5f)Line(false,"AC-S9 "+s.id+" strength "+st.State.Capital+" != CapitalAtmosphere x (1 - "+hw+")");
   }
   Line(worstBearing<=1f,"AC-S9 bearing vs centroid, worst "+worstBearing.ToString("0.###",CultureInfo.InvariantCulture)+" deg <= 1");
   {
    var st=new RegionalSkyStepper();st.Step(profile,sheet,new Vector3(centroid.x,0,centroid.y),0,false,true);
    float hw=RegionalInkSkyProfile.RealmWeight(sheet,st.Weights,profile.CapitalRealm);
    Line(st.State.Capital<=profile.CapitalAtmosphere*(1-hw)+1e-6f&&(hw<.999f||st.State.Capital<1e-4f),"AC-S9 at the centroid: capital weight "+hw.ToString("0.0000",CultureInfo.InvariantCulture)+", strength "+st.State.Capital.ToString("0.00000",CultureInfo.InvariantCulture));
   }
   Note("AC-S9 pose a/b horizon band difference and visibility: capture + human");

   // AC-S10 interior hold (stepper simulation: 10 s walk at 1.5 m/s inside, then out)
   {
    var st=new RegionalSkyStepper();var start=Station(file,"S00");
    st.Step(profile,sheet,start,1/60f,true,false);var held=st.State;bool unchanged=true;int changes=0;
    for(int f=1;f<=600;f++){if(st.Step(profile,sheet,start+new Vector3(f*.025f,0,f*.01f),1/60f,true,false))changes++;unchanged&=Same(held,st.State);}
    Line(unchanged&&changes==0,"AC-S10 10 s inside: state changes "+changes+", identical "+unchanged);
    var outside=start+new Vector3(20,0,10);st.Step(profile,sheet,outside,1/60f,false,false);
    Line(!st.Snapped&&!st.Holding,"AC-S10 leaving (< snap distance): exponential response resumes");
    Note("AC-S10 RenderSettings.ambientProbe and the driver's SkyWriteCount in sealed cells: Play probe (driver.SkyWriteCount / RegionalSkySealed)");
   }

   // AC-S13 / S14: capture and render tools
   Note("AC-S13 HDR sky max / Bloom threshold: capture step (not automated here)");
   Note("AC-S14: RegionSky308Capture render-ref:pre308 (old code) vs render-ref:post308 (new code, RegionalSky297, material defaults) -> render-compare");

   // AC-S15 hashes against the baseline (hashes:record before Play / before the change)
   if(File.Exists(HashPath))
   {
    var baseFile=JsonUtility.FromJson<HashFile>(File.ReadAllText(HashPath));
    foreach(var h in baseFile.files)Line(Sha(h.path)==h.sha,(h.path.EndsWith(".asset",StringComparison.Ordinal)&&AssetDatabase.GetMainAssetTypeAtPath(h.path)?.Name=="JamoTemplateLibrarySO"?"AC-S17 template ":"AC-S15 ")+h.path+" unchanged since "+baseFile.utc);
   }
   else Note("AC-S15: no hash baseline (run hashes:record first)");

   // AC-S16 geometry
   string geo=GeometryCheck(out bool geoOk);Line(geoOk,"AC-S16 geometry-check (details below)");

   // AC-S17 recognition files: git diff against HEAD
   var recog=RecognitionFiles.SelectMany(n=>AssetDatabase.FindAssets(n+" t:Object").Select(AssetDatabase.GUIDToAssetPath).Where(p=>Path.GetFileNameWithoutExtension(p)==n&&(p.EndsWith(".cs",StringComparison.Ordinal)||p.EndsWith(".asset",StringComparison.Ordinal)))).Distinct().ToArray();
   string git=Git("status --porcelain -- "+string.Join(" ",recog.Select(p=>"\"Oheangbu/"+p+"\"")));
   if(git==null)Note("AC-S17 git not available: diff the recognition files manually ("+recog.Length+" files)");
   else Line(git.Trim().Length==0,"AC-S17 recognition files ("+recog.Length+") git status clean"+(git.Trim().Length>0?": "+git.Trim():""));
   Note("AC-S17 HUD element count / sun angle-intensity-colour: unchanged by construction (no HUD or sun code touched) — capture review");

   sb.AppendLine(geo);
   sb.Insert(0,(fail==0?"PASS":"FAIL "+fail)+" — ");
   File.WriteAllText(Path.Combine(OutDir,"checks.txt"),sb.ToString());
   return sb.ToString();
  }

  static Vector3 Station(RegionSky308Capture.StationFile f,string id){var s=f.stations.FirstOrDefault(x=>x.id==id)??throw new PostLedger308.Refused("station "+id+" missing");return new Vector3(s.x,0,s.z);}
  static float MaxDiff(Color a,Color b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Max(Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)));
  static bool Same(RegionalInkSkyProfile.SkyState a,RegionalInkSkyProfile.SkyState b)=>a.Horizon==b.Horizon&&a.Zenith==b.Zenith&&a.Cloud==b.Cloud&&a.CloudDensity==b.CloudDensity&&a.Capital==b.Capital&&a.Mist==b.Mist&&
   a.Fog==b.Fog&&a.CloudInk==b.CloudInk&&a.Coverage==b.Coverage&&a.Band==b.Band&&a.Storm==b.Storm&&a.Shape==b.Shape;

  static string Git(string args)
  {
   try
   {
    var psi=new System.Diagnostics.ProcessStartInfo("git",args){WorkingDirectory=PostLedger308.RepoPath(""),RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
    using(var p=System.Diagnostics.Process.Start(psi)){string output=p.StandardOutput.ReadToEnd();p.WaitForExit(20000);return p.ExitCode==0?output:null;}
   }
   catch{return null;}
  }
 }
}
