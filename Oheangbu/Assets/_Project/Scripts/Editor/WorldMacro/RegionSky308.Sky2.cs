using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 step 2 data (SPEC-REGION-SKY-308 "2 강토 모양 하늘", TEST): data:apply:step=2 / data:revert:step=2.
 //  * shader gate: "Oheangbu/Realm Ink Sky 308" (Shaders/RealmInkSky308.shader) must exist, compile without errors (the passes are
 //    compiled synchronously here) and be supported — otherwise "refused: ..." and nothing is written;
 //  * material: Art/World/RegionSky308/Materials/RealmInkSky308.mat is created from the shader (its defaults) when missing; the
 //    seeds' optional "material" values are applied to it (previous values in the data ledger);
 //  * profile: every RegionalSky308 entry gets the step-2 fields from the seeds; a value the seeds leave out takes the material's
 //    value of the same property (shader default) and is listed as such. Optional per-realm "horizon"/"zenith"/"cloud"/
 //    "cloudDensity" re-tune the step-1 colours too (reported). Optional "post" values: M_InkWash297 _SkyInkTone, Fog297 _SkyFogFollow;
 //  * the profile before the first step-2 apply is kept (profileSnapshot2) so data:revert:step=2 returns to the 1b values.
 // Seeds JSON (tolerant): {"realms":[{"realm":"Cheongrim"|"청림"|0, "CloudInk":[.28,.34,.30] | ".28/.34/.30" | "#475750" |
 //   {"r":..}, "Coverage":.55, "Softness":.., "Stretch":1.4, "BandYaw":.., "StormYaw":.., "StormGain":.., "WetEdge":.., "CoreInk":..,
 //   "MistHeight":.14, "ZenithFade":.., "FogTint":[.70,.76,.70(,a)]}, ...], "material":{"_Octaves":6, ...},
 //   "post":{"_SkyInkTone":.35}} — "realms" may also be "entries"/"seeds"/"regions", an object keyed by realm, or the root itself;
 //   keys ignore case, '_' and '-'; colours in 0..1 (or 0..255), alpha defaults to 1; // and /* */ comments are tolerated.
 public static partial class RegionSky308
 {
  internal const string Sky2ShaderName="Oheangbu/Realm Ink Sky 308";
  internal const string Sky2ShaderPath="Assets/_Project/Shaders/RealmInkSky308.shader";
  internal const string Sky2MatPath="Assets/_Project/Art/World/RegionSky308/Materials/RealmInkSky308.mat";
  internal const string SeedsRel="Art/World/RegionSky308/sky2_seeds.json";          // deployed copy (repository, outside Assets)
  internal const string SeedsStageRel="Tools/Unity/Stage308_sky2/sky2_seeds.json";  // staging copy (step-2 implementers)

  // step-2 Entry fields: canonical key, shader property (fallback source), colour
  static readonly (string key,string prop,bool color)[] Shape2={
   ("cloudink","_CloudInk",true),("coverage","_Coverage",false),("softness","_Softness",false),("stretch","_Stretch",false),
   ("bandyaw","_BandYaw",false),("stormyaw","_StormYaw",false),("stormgain","_StormGain",false),("wetedge","_WetEdge",false),
   ("coreink","_CoreInk",false),("mistheight","_MistHeight",false),("zenithfade","_ZenithFade",false)};
  // properties the shader must declare: the driver's twelve step-2 names + the step-1 state and the shared clock/anchor
  static readonly string[] Sky2Contract={"_CloudInk","_Coverage","_Softness","_Stretch","_BandYaw","_StormYaw","_StormGain","_WetEdge",
   "_CoreInk","_MistHeight","_ZenithFade","_FogTint","_Horizon","_Zenith","_Cloud","_CloudSpeed","_CapitalAzimuth","_CapitalAtmosphere"};
  // "post" values the seeds may set (material asset, property)
  static readonly (string prop,string material)[] Post2={("_SkyInkTone",InkMatPath),("_SkyFogFollow",FogMatPath)};

  // ---------- shader gate ----------
  /// <summary>The step-2 shader, or null with the reason (missing, wrong name, compile errors, unsupported).</summary>
  internal static Shader Sky2Shader(out string why)
  {
   var shader=AssetDatabase.LoadAssetAtPath<Shader>(Sky2ShaderPath);
   if(shader==null)shader=Shader.Find(Sky2ShaderName);
   if(shader==null){why="shader \""+Sky2ShaderName+"\" missing ("+Sky2ShaderPath+"): deploy RealmInkSky308.shader and refresh first";return null;}
   if(shader.name!=Sky2ShaderName){why=AssetDatabase.GetAssetPath(shader)+" declares \""+shader.name+"\" (expected \""+Sky2ShaderName+"\")";return null;}
   // the contract with WorldLookDriver: it writes step-2 values only into a sky declaring all twelve, plus the step-1 state
   var absent=Sky2Contract.Where(p=>shader.FindPropertyIndex(p)<0).ToArray();
   if(absent.Length>0){why="shader \""+Sky2ShaderName+"\" lacks "+string.Join(", ",absent)+" (WorldLookDriver writes step-2 values only when all twelve exist)";return null;}
   // compile every pass synchronously so HLSL errors surface now (import alone may not compile the variants)
   var probe=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
   try{for(int p=0;p<probe.passCount;p++)ShaderUtil.CompilePass(probe,p,true);}
   finally{UnityEngine.Object.DestroyImmediate(probe);}
   why=ShaderErrors(shader);if(why!=null)return null;
   if(!shader.isSupported){why="shader \""+Sky2ShaderName+"\" is not supported on this editor's graphics API";return null;}
   return shader;
  }

  internal static string ShaderErrors(Shader shader)
  {
   if(!ShaderUtil.ShaderHasError(shader))return null;
   var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==ShaderCompilerMessageSeverity.Error).Take(6)
    .Select(m=>m.message+(m.line>0?" (line "+m.line+")":"")).ToArray();
   return "shader \""+shader.name+"\" has compile errors: "+(errors.Length>0?string.Join(" | ",errors):"(see the console)");
  }

  // ---------- data:apply:step=2 ----------
  static string DataApply2(Dictionary<string,string> o)
  {
   PostLedger308.RequireEditable();
   var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath)??throw new PostLedger308.Refused("RegionalSky308 missing: run data:apply (step 1b) first");
   if(profile.Regions==null||profile.Regions.Length==0||profile.Regions.Any(e=>e==null))throw new PostLedger308.Refused("RegionalSky308 has no (or a null) realm entry: run data:apply (step 1b) first");
   string seedsPath=SeedsPath(o);
   string text=File.ReadAllText(seedsPath);
   var seeds=ParseSeeds(text,out var materialValues,out var postValues,out var notes);
   // every profile realm needs a seed, every seed a profile realm
   var missing=profile.Regions.Select(e=>e.Realm).Where(r=>!seeds.ContainsKey(r)).ToArray();
   if(missing.Length>0)throw new PostLedger308.Refused("seeds "+seedsPath+" have no entry for "+string.Join(", ",missing));
   var extra=seeds.Keys.Where(r=>profile.Regions.All(e=>e.Realm!=r)).ToArray();
   if(extra.Length>0)throw new PostLedger308.Refused("seeds name realm(s) RegionalSky308 does not have: "+string.Join(", ",extra));
   var shader=Sky2Shader(out string why)??throw new PostLedger308.Refused(why);
   var mat=AssetDatabase.LoadAssetAtPath<Material>(Sky2MatPath);
   if(mat!=null&&mat.shader!=shader)throw new PostLedger308.Refused(Sky2MatPath+" uses shader \""+(mat.shader!=null?mat.shader.name:"null")+"\" (expected \""+Sky2ShaderName+"\"): fix or remove it first");

   // 1. validate everything on an in-memory probe (the material as it will be: existing or shader defaults + the seeds' material
   //    values); nothing is written when a value is refused
   var problems=new List<string>();var planned=new List<(RegionalInkSkyProfile.Entry entry,Action write,string line)>();
   var probe=mat!=null?new Material(mat):new Material(shader);probe.hideFlags=HideFlags.HideAndDontSave;
   try
   {
    var scratch=new DataLedger();bool ignored=false;
    foreach(var kv in materialValues){string r=SetMaterialValue(scratch,Sky2MatPath,probe,kv.Key,kv.Value,"2",ref ignored);if(r.StartsWith("refused:",StringComparison.Ordinal))problems.Add("material "+r.Substring(9));}
    foreach(var entry in profile.Regions)planned.Add(PlanEntry(entry,seeds[entry.Realm],probe,problems));
   }
   finally{UnityEngine.Object.DestroyImmediate(probe);}
   if(problems.Count>0)throw new PostLedger308.Refused("seed values refused (nothing written):\n   "+string.Join("\n   ",problems));

   var sb=new StringBuilder("data:apply step 2 (seeds "+seedsPath+")\n");
   foreach(var n in notes)sb.AppendLine("  note: "+n);
   var ledger=ReadJson<DataLedger>(DataLedgerPath);
   if(string.IsNullOrEmpty(ledger.utc))ledger.utc=PostLedger308.Utc();
   bool first=string.IsNullOrEmpty(ledger.utc2);
   if(first){ledger.utc2=PostLedger308.Utc();ledger.profileSnapshot2=EditorJsonUtility.ToJson(profile);}
   ledger.seedsPath=seedsPath;ledger.seedsSha=Sha256(text);

   // 2. material (created from the shader defaults when missing), then the seeds' material values
   if(mat==null)
   {
    EnsureFolder(Path.GetDirectoryName(Sky2MatPath).Replace('\\','/'));
    mat=new Material(shader){name=Path.GetFileNameWithoutExtension(Sky2MatPath)};
    AssetDatabase.CreateAsset(mat,Sky2MatPath);ledger.materialCreated=true;
    sb.AppendLine("  created "+Sky2MatPath+" on \""+Sky2ShaderName+"\" (shader defaults)");
   }
   else sb.AppendLine("  material "+Sky2MatPath+" exists (shader ok)");
   bool matChanged=false;
   foreach(var kv in materialValues)sb.AppendLine("  "+SetMaterialValue(ledger,Sky2MatPath,mat,kv.Key,kv.Value,"2",ref matChanged));
   if(matChanged){EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);}

   // 3. profile entries (values validated above; material defaults read from the probe = the material as now written)
   string before=EditorJsonUtility.ToJson(profile);
   foreach(var p in planned){p.write();sb.AppendLine(p.line);}
   if(!profile.HasShapeData){EditorJsonUtility.FromJsonOverwrite(before,profile);WriteJson(DataLedgerPath,ledger);throw new PostLedger308.Refused("profile still lacks step-2 data after the write (Stretch must be > 0); profile left unchanged");}
   if(EditorJsonUtility.ToJson(profile)!=before){EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);sb.AppendLine("  profile written ("+profile.Regions.Length+" realms)");}
   else sb.AppendLine("  profile already at these step-2 values (no-op)");

   // 4. optional post values (screen passes)
   foreach(var kv in postValues)
   {
    var target=Post2.First(x=>string.Equals(x.prop,kv.Key,StringComparison.Ordinal));
    var m=AssetDatabase.LoadAssetAtPath<Material>(target.material);
    if(m==null){sb.AppendLine("  refused: material missing "+target.material);continue;}
    bool changed=false;sb.AppendLine("  "+SetMaterialValue(ledger,target.material,m,kv.Key,kv.Value,"2",ref changed));
    if(changed){EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);}
   }
   WriteJson(DataLedgerPath,ledger);
   return sb.Append("  ledger "+DataLedgerPath+(first?" (step-2 snapshot recorded)":"")).ToString();
  }

  static string SeedsPath(Dictionary<string,string> o)
  {
   if(o.TryGetValue("seeds",out var given)&&given.Length>0)
   {
    // the queue command is split on ':', so "C:/..." arrives as "C": only repository-relative paths can be passed
    if(given.Length==1&&char.IsLetter(given[0]))throw new PostLedger308.Refused("seeds=<path> must be repository-relative (the command is split on ':'), e.g. seeds=Art/World/RegionSky308/sky2_seeds.json");
    string p=Path.IsPathRooted(given)?given:PostLedger308.RepoPath(given);
    if(!File.Exists(p))throw new PostLedger308.Refused("seeds file missing: "+p);
    return Path.GetFullPath(p);
   }
   foreach(var rel in new[]{SeedsRel,SeedsStageRel}){string p=PostLedger308.RepoPath(rel);if(File.Exists(p))return p;}
   throw new PostLedger308.Refused("no seeds file: copy sky2_seeds.json to "+PostLedger308.RepoPath(SeedsRel)+" (or pass :seeds=<path>)");
  }

  static string Sha256(string text){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}

  /// <summary>Validates one realm's step-2 values (seed, else the material's value) and returns the deferred write + a report line.</summary>
  static (RegionalInkSkyProfile.Entry,Action,string) PlanEntry(RegionalInkSkyProfile.Entry entry,Dictionary<string,object> seed,Material mat,List<string> problems)
  {
   string realm=entry.Realm.ToString();var fromMaterial=new List<string>();
   var values=new Dictionary<string,float>();Color ink=default;
   foreach(var (key,prop,color) in Shape2)
   {
    bool has=seed.TryGetValue(key,out var raw);
    if(color)
    {
     if(has){if(!TryColor(raw,out ink,out string cwhy))problems.Add(realm+" "+key+": "+cwhy);}
     else if(mat.HasProperty(prop)){ink=mat.GetColor(prop);fromMaterial.Add(key);}
     else problems.Add(realm+" "+key+": missing (and the material has no "+prop+")");
     continue;
    }
    float v=float.NaN;
    if(has){if(!TryNumber(raw,out v))problems.Add(realm+" "+key+": not a number");}
    else if(mat.HasProperty(prop)){v=mat.GetFloat(prop);fromMaterial.Add(key);}
    else problems.Add(realm+" "+key+": missing (and the material has no "+prop+")");
    values[key]=v;
   }
   // ranges = the Entry attributes / InkWashSky300 property ranges (the shader's smoothsteps need softness and mist > 0)
   void Within(string key,float lo,float hi){if(values.TryGetValue(key,out var v)&&!float.IsNaN(v)&&!(v>=lo&&v<=hi))problems.Add(realm+" "+key+" "+F3(v)+" outside ["+F3(lo)+", "+F3(hi)+"]");}
   Within("coverage",0f,1f);Within("softness",.005f,.4f);Within("stretch",.1f,8f);Within("stormgain",0f,1f);Within("wetedge",0f,1f);
   Within("coreink",0f,1f);Within("mistheight",.01f,.5f);Within("zenithfade",0f,1f);Within("bandyaw",-720f,720f);Within("stormyaw",-720f,720f);
   // 담채 ink: no pure black (ART-COLOR), LDR (ART-INK); C* stays in the seed range
   if(Finite(ink))
   {
    if(!InUnit(ink))problems.Add(realm+" cloudink outside [0, 1]");
    var lab=Lab(ink);if(lab.x<12f)problems.Add(realm+" cloudink L* "+lab.x.ToString("F1")+" < 12 (no pure black, ART-COLOR)");
    if(Chroma(lab)>22f)problems.Add(realm+" cloudink C* "+Chroma(lab).ToString("F1")+" > 22 (담채)");
   }
   // fog tint: optional (absent = a 0 = the horizon)
   Color fog=default;bool hasFog=seed.TryGetValue("fogtint",out var fogRaw);
   if(hasFog)
   {
    if(!TryColor(fogRaw,out fog,out string fwhy))problems.Add(realm+" fogtint: "+fwhy);
    else if(!InUnit(fog))problems.Add(realm+" fogtint outside [0, 1]");
   }
   // optional step-1 re-tune
   var step1=new List<string>();Color hz=entry.Horizon,zn=entry.Zenith,cl=entry.Cloud;float dens=entry.CloudDensity;
   void Col(string key,ref Color target){if(!seed.TryGetValue(key,out var r))return;if(TryColor(r,out var c,out string w)){if(!InUnit(c))problems.Add(realm+" "+key+" outside [0, 1]");else{c.a=1f;target=c;step1.Add(key);}}else problems.Add(realm+" "+key+": "+w);}
   Col("horizon",ref hz);Col("zenith",ref zn);Col("cloud",ref cl);
   if(seed.TryGetValue("clouddensity",out var dr)){if(TryNumber(dr,out var dv)&&dv>=0f&&dv<=.5f){dens=dv;step1.Add("clouddensity");}else problems.Add(realm+" clouddensity must be a number in [0, .5] (the step-1 clamp)");}

   Action write=()=>
   {
    entry.CloudInk=ink;entry.Coverage=values["coverage"];entry.Softness=values["softness"];entry.Stretch=values["stretch"];
    entry.BandYaw=values["bandyaw"];entry.StormYaw=values["stormyaw"];entry.StormGain=values["stormgain"];entry.WetEdge=values["wetedge"];
    entry.CoreInk=values["coreink"];entry.MistHeight=values["mistheight"];entry.ZenithFade=values["zenithfade"];
    entry.FogTint=hasFog?fog:default;
    entry.Horizon=hz;entry.Zenith=zn;entry.Cloud=cl;entry.CloudDensity=dens;
   };
   string V(string k)=>values.TryGetValue(k,out var x)?F3(x):"?";
   string line="    "+realm+": ink "+Hex(ink)+" cover "+V("coverage")+" soft "+V("softness")+" stretch "+V("stretch")+" band "+V("bandyaw")+" storm "+V("stormyaw")+
    " gain "+V("stormgain")+" wet "+V("wetedge")+" core "+V("coreink")+" mist "+V("mistheight")+" fade "+V("zenithfade")+
    " fog "+(hasFog?Hex(fog)+" a "+F3(fog.a):"= horizon")+(fromMaterial.Count>0?" | material default: "+string.Join(",",fromMaterial):"")+
    (step1.Count>0?" | step-1 re-tuned: "+string.Join(",",step1)+" (also changes the 1b sky)":"");
   return (entry,write,line);
  }

  /// <summary>Sets one material value and records the previous one (first record per material/property/step wins).</summary>
  static string SetMaterialValue(DataLedger ledger,string matPath,Material m,string prop,object value,string step,ref bool changed)
  {
   string name=Path.GetFileName(matPath);
   int index=m.shader!=null?m.shader.FindPropertyIndex(prop):-1;
   if(index<0||!m.HasProperty(prop))return name+" "+prop+": no such property (skipped)";
   var type=m.shader.GetPropertyType(index);
   var rec=ledger.materials.FirstOrDefault(x=>x.material==matPath&&x.property==prop&&StepOf(x)==step);
   if(type==ShaderPropertyType.Color||type==ShaderPropertyType.Vector)
   {
    if(!TryColor(value,out var c,out string why))return "refused: "+name+" "+prop+": "+why;
    var now=m.GetColor(prop);
    if(rec==null){rec=new MatValue{material=matPath,property=prop,existed=true,step=step,isColor=true,beforeColor=now};ledger.materials.Add(rec);}
    rec.afterColor=c;
    if(now==c)return name+" "+prop+" already "+Hex(c);
    m.SetColor(prop,c);changed=true;return name+" "+prop+" "+Hex(now)+" -> "+Hex(c);
   }
   if(type==ShaderPropertyType.Texture)return "refused: "+name+" "+prop+" is a texture (not a seed value)";
   if(!TryNumber(value,out float v))return "refused: "+name+" "+prop+": not a number";
   bool integer=type==ShaderPropertyType.Int;
   float before=integer?m.GetInteger(prop):m.GetFloat(prop);
   if(rec==null){rec=new MatValue{material=matPath,property=prop,before=before,existed=true,step=step};ledger.materials.Add(rec);}
   rec.after=v;
   if(Mathf.Abs(before-v)<=1e-6f)return name+" "+prop+" already "+F3(v);
   if(integer)m.SetInteger(prop,Mathf.RoundToInt(v));else m.SetFloat(prop,v);
   changed=true;return name+" "+prop+" "+F3(before)+" -> "+F3(v);
  }

  static string StepOf(MatValue x)=>string.IsNullOrEmpty(x.step)?"1b":x.step;

  /// <summary>Restores one ledger record onto its material (float, integer or colour). False when the material/property is gone.</summary>
  static bool RestoreMaterialValue(MatValue rec,out string line)
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(rec.material);
   if(m==null||!m.HasProperty(rec.property)){line="skip "+rec.material+" "+rec.property+" (missing)";return false;}
   int index=m.shader!=null?m.shader.FindPropertyIndex(rec.property):-1;
   if(rec.isColor){m.SetColor(rec.property,rec.beforeColor);line=Path.GetFileName(rec.material)+" "+rec.property+" -> "+Hex(rec.beforeColor);}
   else if(index>=0&&m.shader.GetPropertyType(index)==ShaderPropertyType.Int){m.SetInteger(rec.property,Mathf.RoundToInt(rec.before));line=Path.GetFileName(rec.material)+" "+rec.property+" -> "+F3(rec.before);}
   else{m.SetFloat(rec.property,rec.before);line=Path.GetFileName(rec.material)+" "+rec.property+" -> "+F3(rec.before);}
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return true;
  }

  // ---------- data:revert:step=2 ----------
  static string DataRevert2()
  {
   PostLedger308.RequireEditable();
   if(!File.Exists(DataLedgerPath))throw new PostLedger308.Refused("no data ledger at "+DataLedgerPath);
   var scene=ReadJson<SceneLedger>(SceneLedgerPath);
   if(scene.items.Any(i=>i.item=="L4"))throw new PostLedger308.Refused("scene ledger still points drivers at RealmInkSky308 (L4): run ledger:revert:step=2 first");
   var ledger=ReadJson<DataLedger>(DataLedgerPath);
   if(string.IsNullOrEmpty(ledger.utc2))throw new PostLedger308.Refused("the data ledger has no step-2 apply");
   var sb=new StringBuilder("data:revert step 2\n");
   foreach(var rec in ledger.materials.Where(x=>StepOf(x)=="2").Reverse().ToList())
   {
    if(!(ledger.materialCreated&&rec.material==Sky2MatPath)){RestoreMaterialValue(rec,out string line);sb.AppendLine("  "+line);}
    ledger.materials.Remove(rec);
   }
   var profile=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath);
   if(profile!=null&&!string.IsNullOrEmpty(ledger.profileSnapshot2))
   {
    EditorJsonUtility.FromJsonOverwrite(ledger.profileSnapshot2,profile);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
    sb.AppendLine("  profile restored to its state before step 2 ("+ledger.utc2+")");
   }
   if(ledger.materialCreated&&AssetDatabase.LoadAssetAtPath<Material>(Sky2MatPath)!=null){AssetDatabase.DeleteAsset(Sky2MatPath);sb.AppendLine("  deleted "+Sky2MatPath+" (created by data:apply:step=2)");}
   ledger.utc2="";ledger.profileSnapshot2="";ledger.materialCreated=false;ledger.seedsPath="";ledger.seedsSha="";
   WriteJson(DataLedgerPath,ledger);
   return sb.Append("  ledger "+DataLedgerPath+" (1b records kept)").ToString();
  }

  // ---------- seeds parsing ----------
  /// <summary>Realm -> canonical key -> raw JSON value. Throws Refused on unreadable JSON or unknown realms.</summary>
  static Dictionary<RealmId,Dictionary<string,object>> ParseSeeds(string text,out Dictionary<string,object> material,out Dictionary<string,object> post,out List<string> notes)
  {
   material=new Dictionary<string,object>(StringComparer.Ordinal);post=new Dictionary<string,object>(StringComparer.Ordinal);notes=new List<string>();
   object root;
   try{root=Json308.Parse(text);}catch(FormatException e){throw new PostLedger308.Refused("seeds JSON unreadable: "+e.Message);}
   var seeds=new Dictionary<RealmId,Dictionary<string,object>>();
   object realms=null;
   if(root is List<object>)realms=root;
   else if(root is Dictionary<string,object> top)
   {
    foreach(var kv in top)
    {
     string k=SeedKey(kv.Key);
     if(k=="realms"||k=="entries"||k=="seeds"||k=="regions"){realms=kv.Value;continue;}
     if(k=="material"||k=="materialdefaults"||k=="shared"){if(kv.Value is Dictionary<string,object> md)foreach(var p in md)material[ShaderProp(p.Key)]=p.Value;else notes.Add("\"material\" is not an object (ignored)");continue;}
     if(k=="post"||k=="screen"){if(kv.Value is Dictionary<string,object> pd)foreach(var p in pd){string prop=ShaderProp(p.Key);if(Post2.Any(x=>x.prop==prop))post[prop]=p.Value;else notes.Add("post "+p.Key+" is not one of "+string.Join(", ",Post2.Select(x=>x.prop))+" (ignored)");}continue;}
     if(TryRealm(kv.Key,out var r)){AddSeed(seeds,r,kv.Value,notes);continue;}
     notes.Add("top-level \""+kv.Key+"\" ignored");
    }
   }
   if(realms is List<object> list)
    foreach(var item in list)
    {
     if(!(item is Dictionary<string,object> d)){notes.Add("a realm item is not an object (ignored)");continue;}
     object id=null;foreach(var kv in d){string k=SeedKey(kv.Key);if(k=="realm"||k=="id"||k=="name"||k=="key"){id=kv.Value;break;}}
     if(id==null||!TryRealm(id,out var r))throw new PostLedger308.Refused("seed item without a known realm ("+(id??"no realm/id/name key")+")");
     AddSeed(seeds,r,d,notes);
    }
   else if(realms is Dictionary<string,object> byRealm)
    foreach(var kv in byRealm){if(!TryRealm(kv.Key,out var r))throw new PostLedger308.Refused("unknown realm key \""+kv.Key+"\" in the seeds");AddSeed(seeds,r,kv.Value,notes);}
   else if(realms!=null)throw new PostLedger308.Refused("\"realms\" must be an array or an object keyed by realm");
   if(seeds.Count==0)throw new PostLedger308.Refused("the seeds name no realm");
   return seeds;
  }

  static void AddSeed(Dictionary<RealmId,Dictionary<string,object>> seeds,RealmId realm,object value,List<string> notes)
  {
   if(!(value is Dictionary<string,object> d))throw new PostLedger308.Refused("seed for "+realm+" is not an object");
   if(seeds.ContainsKey(realm))throw new PostLedger308.Refused("realm "+realm+" appears twice in the seeds");
   var canon=new Dictionary<string,object>(StringComparer.Ordinal);var ignored=new List<string>();
   foreach(var kv in d)
   {
    string k=Canonical(SeedKey(kv.Key));
    if(k=="realm"||k=="id"||k=="name"||k=="key")continue;
    if(Shape2.Any(s=>s.key==k)||k=="fogtint"||k=="horizon"||k=="zenith"||k=="cloud"||k=="clouddensity")canon[k]=kv.Value;
    else ignored.Add(kv.Key);
   }
   if(ignored.Count>0)notes.Add(realm+": keys ignored "+string.Join(",",ignored));
   seeds[realm]=canon;
  }

  static string SeedKey(string s){var sb=new StringBuilder();foreach(char c in s??"")if(c!='_'&&c!='-'&&c!=' '&&c!='.')sb.Append(char.ToLowerInvariant(c));return sb.ToString();}
  static string ShaderProp(string s){s=(s??"").Trim();return s.StartsWith("_",StringComparison.Ordinal)?s:"_"+s;}
  static string Canonical(string k)
  {
   switch(k)
   {
    case "ink":case "inkcore":case "cloudinkcore":case "구름먹":return "cloudink";
    case "cover":case "덮임":return "coverage";
    case "mist":case "horizonmist":case "지평안개":return "mistheight";
    case "fog":case "fogcolor":case "fogcolour":case "안개틴트":return "fogtint";
    case "wash":case "cloudwash":case "구름씻김":return "cloud";
    case "density":return "clouddensity";
    default:return k;
   }
  }

  internal static bool TryRealm(object v,out RealmId realm)
  {
   realm=default;
   if(v is double d){int k=(int)d;if(k==d&&Enum.IsDefined(typeof(RealmId),k)){realm=(RealmId)k;return true;}return false;}
   if(!(v is string raw))return false;
   string s=raw.Trim();
   (string ko,RealmId id)[] korean={("청림",RealmId.Cheongrim),("황경",RealmId.Hwanggyeong),("적로",RealmId.Jeokro),("철옹",RealmId.Cheolong),("현강",RealmId.Hyeongang)};
   foreach(var (ko,id) in korean)if(s.StartsWith(ko,StringComparison.Ordinal)){realm=id;return true;}
   string n=SeedKey(s);if(n.StartsWith("realm",StringComparison.Ordinal))n=n.Substring(5);
   foreach(RealmId r in Enum.GetValues(typeof(RealmId)))if(SeedKey(r.ToString())==n){realm=r;return true;}
   if(int.TryParse(n,NumberStyles.Integer,CultureInfo.InvariantCulture,out int i)&&Enum.IsDefined(typeof(RealmId),i)){realm=(RealmId)i;return true;}
   return false;
  }

  static bool TryNumber(object v,out float f)
  {
   f=float.NaN;
   if(v is double d){f=(float)d;return Finite(f);}
   if(v is bool b){f=b?1f:0f;return true;}
   if(v is string s&&float.TryParse(s.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out f))return Finite(f);
   return false;
  }

  static bool TryColor(object v,out Color c,out string why)
  {
   c=default;why=null;var n=new List<float>();
   if(v is List<object> l){foreach(var x in l){if(!TryNumber(x,out float f)){why="colour component is not a number";return false;}n.Add(f);}}
   else if(v is Dictionary<string,object> d)
   {
    foreach(var ch in new[]{"r","g","b","a"})
    {
     var hit=d.FirstOrDefault(kv=>SeedKey(kv.Key)==ch);
     if(hit.Key==null){if(ch=="a")break;why="colour object needs r, g, b";return false;}
     if(!TryNumber(hit.Value,out float f)){why="colour "+ch+" is not a number";return false;}n.Add(f);
    }
   }
   else if(v is string s)
   {
    s=s.Trim();
    if(s.StartsWith("#",StringComparison.Ordinal))
    {
     if(!ColorUtility.TryParseHtmlString(s,out c)){why="bad hex colour "+s;return false;}
     return true;
    }
    foreach(var part in s.Split(new[]{'/',',',' ',';'},StringSplitOptions.RemoveEmptyEntries))
    {if(!float.TryParse(part,NumberStyles.Float,CultureInfo.InvariantCulture,out float f)){why="bad colour \""+s+"\"";return false;}n.Add(f);}
   }
   else{why="colour must be [r,g,b(,a)], \"r/g/b\", \"#rrggbb\" or {r,g,b}";return false;}
   if(n.Count<3||n.Count>4){why="colour needs 3 or 4 components (got "+n.Count+")";return false;}
   if(n.Take(3).Any(x=>x>1.0001f))for(int i=0;i<n.Count;i++)if(i<3||n[i]>1.0001f)n[i]/=255f;   // 0..255 notation
   c=new Color(n[0],n[1],n[2],n.Count>3?n[3]:1f);
   if(!Finite(c)){why="colour is not finite";return false;}
   return true;
  }

  static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
  static bool Finite(Color c)=>Finite(c.r)&&Finite(c.g)&&Finite(c.b)&&Finite(c.a);
  static bool InUnit(Color c)=>c.r>=0f&&c.r<=1f&&c.g>=0f&&c.g<=1f&&c.b>=0f&&c.b<=1f&&c.a>=0f&&c.a<=1f;
  internal static string Hex(Color c)=>"#"+ColorUtility.ToHtmlStringRGB(c);
  static string F3(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);

  /// <summary>Small tolerant JSON reader (objects -> Dictionary, arrays -> List, numbers -> double). Comments and trailing commas
  /// are accepted. Editor tool only.</summary>
  static class Json308
  {
   public static object Parse(string s){int i=0;var v=Value(s,ref i);Ws(s,ref i);if(i<s.Length)throw new FormatException("trailing text at "+Where(s,i));return v;}
   static string Where(string s,int i){int line=1,col=1;for(int k=0;k<i&&k<s.Length;k++){if(s[k]=='\n'){line++;col=1;}else col++;}return "line "+line+" col "+col;}
   static void Ws(string s,ref int i)
   {
    while(i<s.Length)
    {
     char c=s[i];
     if(char.IsWhiteSpace(c)||c=='\uFEFF'){i++;continue;}
     if(c=='/'&&i+1<s.Length&&s[i+1]=='/'){while(i<s.Length&&s[i]!='\n')i++;continue;}
     if(c=='/'&&i+1<s.Length&&s[i+1]=='*'){int e=s.IndexOf("*/",i+2,StringComparison.Ordinal);i=e<0?s.Length:e+2;continue;}
     break;
    }
   }
   static object Value(string s,ref int i)
   {
    Ws(s,ref i);if(i>=s.Length)throw new FormatException("unexpected end");
    char c=s[i];
    if(c=='{')
    {
     i++;var d=new Dictionary<string,object>(StringComparer.Ordinal);
     while(true)
     {
      Ws(s,ref i);if(i<s.Length&&s[i]=='}'){i++;return d;}
      if(i>=s.Length||s[i]!='"')throw new FormatException("expected a key at "+Where(s,i));
      string k=Str(s,ref i);Ws(s,ref i);
      if(i>=s.Length||s[i]!=':')throw new FormatException("expected ':' at "+Where(s,i));
      i++;d[k]=Value(s,ref i);Ws(s,ref i);
      if(i<s.Length&&s[i]==','){i++;continue;}
      if(i<s.Length&&s[i]=='}'){i++;return d;}
      throw new FormatException("expected ',' or '}' at "+Where(s,i));
     }
    }
    if(c=='[')
    {
     i++;var l=new List<object>();
     while(true)
     {
      Ws(s,ref i);if(i<s.Length&&s[i]==']'){i++;return l;}
      l.Add(Value(s,ref i));Ws(s,ref i);
      if(i<s.Length&&s[i]==','){i++;continue;}
      if(i<s.Length&&s[i]==']'){i++;return l;}
      throw new FormatException("expected ',' or ']' at "+Where(s,i));
     }
    }
    if(c=='"')return Str(s,ref i);
    if(string.CompareOrdinal(s,i,"true",0,4)==0){i+=4;return true;}
    if(string.CompareOrdinal(s,i,"false",0,5)==0){i+=5;return false;}
    if(string.CompareOrdinal(s,i,"null",0,4)==0){i+=4;return null;}
    int start=i;while(i<s.Length&&("+-.eE".IndexOf(s[i])>=0||char.IsDigit(s[i])))i++;
    if(i==start)throw new FormatException("unexpected '"+c+"' at "+Where(s,i));
    if(!double.TryParse(s.Substring(start,i-start),NumberStyles.Float,CultureInfo.InvariantCulture,out double v))throw new FormatException("bad number at "+Where(s,start));
    return v;
   }
   static string Str(string s,ref int i)
   {
    var sb=new StringBuilder();i++;
    while(i<s.Length)
    {
     char c=s[i++];
     if(c=='"')return sb.ToString();
     if(c!='\\'){sb.Append(c);continue;}
     if(i>=s.Length)break;
     char e=s[i++];
     switch(e)
     {
      case 'n':sb.Append('\n');break;case 't':sb.Append('\t');break;case 'r':sb.Append('\r');break;case 'b':sb.Append('\b');break;case 'f':sb.Append('\f');break;
      case 'u':if(i+4<=s.Length){sb.Append((char)Convert.ToInt32(s.Substring(i,4),16));i+=4;}break;
      default:sb.Append(e);break;
     }
    }
    throw new FormatException("unterminated string");
   }
  }
 }
}
