using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 강토별 하늘 (SPEC-REGION-SKY-308, D308-5, TEST). Queue-safe: refusals come back as "refused: ..." strings, never a dialog.
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.RegionSky308 Run "<command>"
 //   data:apply[:step=1b]       RegionalSky308.asset (seeds, blend 350 m / 2.5 s / snap 60 m, capital .025, accents A1–A3 with
 //                              catalog-baked points), Fog297 _SkyFogFollow .6, M_InkWash297 _SkyInkTone .35; previous values ->
 //                              Art/World/RegionSky308/data-ledger.json (first apply only). Idempotent.
 //   data:apply:step=2[:seeds=<path>]   step 2 (RealmInkSky308): Entry step-2 fields (CloudInk, Coverage, Softness, Stretch, BandYaw,
 //                              StormYaw, StormGain, WetEdge, CoreInk, MistHeight, ZenithFade, FogTint) from the seeds JSON
 //                              (Art/World/RegionSky308/sky2_seeds.json, else Tools/Unity/Stage308_sky2/sky2_seeds.json), optional
 //                              material / post values; creates Art/World/RegionSky308/Materials/RealmInkSky308.mat on
 //                              "Oheangbu/Realm Ink Sky 308" when missing (refused while the shader is missing or has errors).
 //                              The profile before step 2 is kept in the data ledger (profileSnapshot2). Idempotent.
 //   data:revert[:step=2]       material values back from the ledger; the profile back to its snapshot (deleted when apply made it,
 //                              refused while a scene ledger still points a driver at it). step=2 only undoes step 2 (profile back
 //                              to the 1b snapshot, step-2 material values, the material deleted when apply made it; refused
 //                              while L4 is applied)
 //   ledger:dry|apply|revert[:step=1a|1b|2][:scene=arch296|folk298|main][:ac19=<perf307 run>]   scene ledger (RegionSky308.Ledger.cs)
 //   geometry-check             realm polygons: geography sheet (canon) vs catalog + 3 WorldLayouts, max vertex delta <= .01 m
 //   weights                    station weights (B from the profile), accents, capital strength/bearing (calculation)
 //   checks[:scene=...]         AC-S1 (scenes), S2, S3, S8 + S10 (stepper simulation), S9 (static), S15 (hash baseline), S16, S17
 //   hashes:record              baseline hashes for AC-S15/S17 (run before Play / before the change)
 //   render-ref / render-compare / stations  -> RegionSky308Capture (deployable before the runtime change)
 // Never: AssetDatabase.SaveAssets (only the target scene / asset is saved), WorldRealmAtmosphere deletion, protected trees.
 public static partial class RegionSky308
 {
  internal const string ProfilePath="Assets/_Project/Art/World/RegionSky308/Data/RegionalSky308.asset";
  internal const string Profile297Path="Assets/_Project/Art/World/Finish297/Data/RegionalSky297.asset";
  internal const string FogMatPath="Assets/_Project/Art/World/Finish297/Materials/Fog297.mat";
  internal const string InkMatPath="Assets/_Project/Art/World/Finish297/Materials/M_InkWash297.mat";
  internal const string CatalogGuid="0338695f85f7c634ea4cde19d409ce21";     // Architecture296/Data/a5309253…_Locations.asset
  internal const string GeographyGuid="fec67cf5feeb5de4b83aee94d605daac";   // Architecture296/Data/d5663117…_Geography.asset (canon)
  internal const string SealedFolder="Assets/_Project/Resources/Perf307";
  internal static string OutDir=>PostLedger308.RepoPath("Art/World/RegionSky308");
  internal static string DataLedgerPath=>Path.Combine(OutDir,"data-ledger.json");
  internal static string SceneLedgerPath=>Path.Combine(OutDir,"scene-ledger.json");
  internal static string BackupDir=>Path.Combine(OutDir,"Backups");

  public static string Run(string command)
  {
   command=(command??"").Trim();
   try
   {
    Directory.CreateDirectory(OutDir);
    var parts=command.Split(':');string head=parts[0];
    if(head=="render-ref"||head=="render-compare"||head=="stations")return RegionSky308Capture.Run(command);
    if(head=="data"&&parts.Length>1)
    {
     var o=PostLedger308.Options(parts.Skip(2));
     if(parts[1]=="apply")return DataApply(o.TryGetValue("step",out var step)?step:"1b",o);
     if(parts[1]=="revert")return o.TryGetValue("step",out var rs)&&rs.Length>0?(rs=="2"?DataRevert2():throw new PostLedger308.Refused("data:revert takes no step or step=2")):DataRevert();
    }
    if(head=="ledger"&&parts.Length>1)
    {
     var o=PostLedger308.Options(parts.Skip(2));string step=o.TryGetValue("step",out var s)?s:"";
     if(parts[1]=="dry")return Ledger("dry",step,o);
     if(parts[1]=="apply")return Ledger("apply",step,o);
     if(parts[1]=="revert")return Ledger("revert",step,o);
    }
    if(command=="geometry-check")return GeometryCheck(out _);
    if(command=="geometry-sync")return "refused: geometry-sync belongs to the D305 seam task (task 3); the canon is not moved by #308";
    if(command=="weights")return Weights();
    if(head=="checks")return Checks(PostLedger308.Options(parts.Skip(1)));
    if(command=="hashes:record")return RecordHashes();
   }
   catch(PostLedger308.Refused r){return "refused: "+r.Message;}
   catch(Exception e){return "FAILED "+e;}   // same as the neighbours (MineBoss306): never an unhandled throw into the queue
   return "refused: unknown command '"+command+"' (data:apply[:step=1b|2][:seeds=..] | data:revert[:step=2] | ledger:dry|apply|revert[:step=1a|1b|2][:scene=..][:ac19=..] | geometry-check | weights | checks | hashes:record | render-ref:<label>[:sky=..] | render-compare:<a>:<b> | stations)";
  }

  internal static T LoadGuid<T>(string guid) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

  // ---------- ledgers (JSON next to the backups, outside Assets) ----------
  // step "" (records written before step 2 existed) = 1b. Colour values use the *Color fields (isColor).
  [Serializable] internal sealed class MatValue{public string material,property;public float before,after;public bool existed;public string step="";public bool isColor;public Color beforeColor,afterColor;}
  [Serializable] internal sealed class DataLedger
  {
   public string utc;public bool profileCreated;public string profileSnapshot="";public List<MatValue> materials=new List<MatValue>();
   // step 2: profile before the first step-2 apply, whether apply created RealmInkSky308.mat, and the seeds it read
   public string utc2="";public string profileSnapshot2="";public bool materialCreated;public string seedsPath="",seedsSha="";
  }
  [Serializable] internal sealed class SceneItem{public string scene,step,item,objectPath,property,before,after,utc,note="";}
  [Serializable] internal sealed class SceneLedger{public List<SceneItem> items=new List<SceneItem>();public List<string> backups=new List<string>();}

  internal static T ReadJson<T>(string path) where T:new()=>File.Exists(path)?JsonUtility.FromJson<T>(File.ReadAllText(path))??new T():new T();
  internal static void WriteJson(string path,object value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(value,true));}
 }
}
