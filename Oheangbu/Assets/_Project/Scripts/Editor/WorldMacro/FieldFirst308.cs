using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // 필드 첫 1시간 (SPEC-FIELD-FIRST-HOUR-308 B1 / B2, D308-117, TEST). Queue-safe: refusals are "refused: ..." strings, never a dialog.
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.FieldFirst308 Run "<command>"
 //   field308-docs[:dry|:revert]   WorldContent_Main.Documents <- field117.json documents[] (rows replaced by id, others kept)
 //   field308-start[:dry|:revert]  campaign InitialCompletedIds minus campaign_start.remove_initial. Refused until the trigger point
 //        (village_commission, the 덕대 of Texts308 XF1) is a Conversation point of WorldContent_Main, is the stage's TriggerId and
 //        is no commission giver: without it a new game could never complete the stage.
 //   field308-check                the writes against the data and the runtime wiring rules (read only; C2 rows included)
 //   field308-c2[:dry|:revert]     C2 sub-NPC commissions + their evidence texts (FieldFirst308.C2.cs)
 //   field308-cloud[:dry|:revert]  D308-118: cloud_band{} of the data -> _CloudBand308 / _CloudShape308 / _CloudColor308 of the live fog
 //        material (RealmFog297); revert puts the before-values back while the material still holds what this tool wrote
 //   field308-sight                A4 sightline audit, read only: per leg (beat -> next beat) and guide: distance, angle off the
 //        heading, in the front view, the line of sight by colliders (hard) and by renderer bounds (soft, foliage / props), the
 //        first blockers named. Report file Art/Playtest306/Backups/FieldFirst308/sight-<utc>.txt. Open the realms first.
 //   field308-heights              read only: the highest collider surface on a 25 m grid of the open scenes, as 500 m cells
 //        (max per cell, rows north first) + world percentiles - where the 운해 band of RealmFog297 should sit (D308-118)
 // Data: Art/World/Compact/Rebuild/FieldFirst308/field117.json (built by Tools/Art/FieldFirst308/field117_build.py from
 // DECISIONS D308-117 F2; the code holds no line). Ledger + asset backups: Art/Playtest306/Backups/FieldFirst308/. Edit mode only,
 // no AssetDatabase.SaveAssets (the target asset is saved alone).
 public static partial class FieldFirst308
 {
  sealed class Refused:Exception{public Refused(string m):base(m){}}
  [Serializable] sealed class DocsWrap{public WorldMacroPlaytestSO.DocumentSpec[] items=Array.Empty<WorldMacroPlaytestSO.DocumentSpec>();}
  [Serializable] sealed class IdsWrap{public string[] items=Array.Empty<string>();}
  [Serializable] sealed class Ledger{public string docsBefore,docsAfter,initialBefore,initialAfter,cloudBefore,cloudAfter,c2Before,c2After;public List<string> backups=new List<string>();}

  static string DataFile=>PostLedger308.RepoPath("Art/World/Compact/Rebuild/FieldFirst308/field117.json");
  static string BackupRoot=>PostLedger308.RepoPath("Art/Playtest306/Backups/FieldFirst308");
  static string LedgerPath=>Path.Combine(BackupRoot,"ledger.json");
  // JsonUtility writes a null string as "": read "" back as null, or a revert (x=null) makes the next apply skip its "before"
  // and the next revert parse "" (2026-10-20: cloudBefore / initialBefore were lost that way)
  static Ledger ReadLedger()
  {
   var l=File.Exists(LedgerPath)?JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath))??new Ledger():new Ledger();
   foreach(var f in typeof(Ledger).GetFields())if(f.FieldType==typeof(string)&&(string)f.GetValue(l)=="")f.SetValue(l,null);
   return l;
  }
  static void WriteLedger(Ledger l){Directory.CreateDirectory(BackupRoot);File.WriteAllText(LedgerPath,JsonUtility.ToJson(l,true));}

  public static string Run(string command)
  {
   command=(command??"").Trim();
   try
   {
    var parts=command.Split(':');var o=PostLedger308.Options(parts.Skip(1));bool dry=o.ContainsKey("dry"),revert=o.ContainsKey("revert");
    switch(parts[0])
    {
     case "field308-docs":return revert?DocsRevert():Docs(dry);
     case "field308-start":return revert?StartRevert():Start(dry);
     case "field308-check":return Check();
     case "field308-heights":return Heights();
     case "field308-sight":return Sight();
     case "field308-ends":return Ends();
     case "field308-nav":return Nav(parts.Length>1?parts[1]:"");
     case "field308-flat":return Flat(parts.Length>1?parts[1].Split(','):new string[0]);
     case "field308-cloud":return revert?CloudRevert():Cloud(dry);
     case "field308-c2":return revert?C2Revert():C2(dry);
    }
   }
   catch(Refused r){return "refused: "+r.Message;}
   catch(PostLedger308.Refused r){return "refused: "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "refused: unknown command '"+command+"' (field308-docs[:dry|:revert] | field308-start[:dry|:revert] | field308-check)";
  }

  // ---------------------------------------------------------------- data
  static JObject Data()
  {
   if(!File.Exists(DataFile))throw new Refused("data missing: "+DataFile+" (python Tools/Art/FieldFirst308/field117_build.py)");
   try{return JObject.Parse(File.ReadAllText(DataFile));}catch(Exception e){throw new Refused("data does not parse: "+e.Message);}
  }
  static T Load<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new Refused("asset missing: "+path);
  static string S(JToken o,string key)=>o?[key]?.Value<string>()??throw new Refused("field117.json: "+key+" missing at "+o?.Path);

  static WorldMacroPlaytestSO.DocumentSpec[] DataDocs(JObject j)
  {
   var list=new List<WorldMacroPlaytestSO.DocumentSpec>();
   foreach(var d in (j["documents"] as JArray)??new JArray())
   {
    var spec=new WorldMacroPlaytestSO.DocumentSpec
    {
     Id=S(d,"id"),Title=S(d,"title"),GrantOnStage=S(d,"grant_on_stage"),State="TEST",
     Pages=((d["pages"] as JArray)??new JArray()).Select(x=>x.Value<string>()).ToArray(),
     Notes=((d["notes"] as JArray)??new JArray()).Select(n=>new WorldMacroPlaytestSO.DocumentNote{AfterStage=S(n,"after_stage"),Text=S(n,"text")}).ToArray(),
    };
    if(!spec.IsConfigured)throw new Refused("document "+spec.Id+" is not configured (id prefix "+WorldMacroPlaytestSO.DocumentSpec.ItemPrefix+", title, pages)");
    if(spec.Notes.Length>3)throw new Refused("document "+spec.Id+": more than 3 notes (SPEC B2)");
    list.Add(spec);
   }
   if(list.Count==0)throw new Refused("field117.json: documents[] is empty");
   return list.ToArray();
  }

  static string DocsJson(WorldMacroPlaytestSO.DocumentSpec[] d)=>JsonUtility.ToJson(new DocsWrap{items=d??Array.Empty<WorldMacroPlaytestSO.DocumentSpec>()});
  static string IdsJson(string[] ids)=>JsonUtility.ToJson(new IdsWrap{items=ids??Array.Empty<string>()});

  // ---------------------------------------------------------------- docs
  static string Docs(bool dry)
  {
   PostLedger308.RequireEditable();
   var j=Data();var content=Load<WorldMacroPlaytestSO>(S(j,"content"));var campaign=Load<DemoCampaignProfile>(S(j,"campaign"));
   var want=DataDocs(j);
   foreach(var d in want)
   {
    if(campaign.FindStage(d.GrantOnStage)==null)throw new Refused("document "+d.Id+": grant stage "+d.GrantOnStage+" is not in the campaign");
    foreach(var n in d.Notes)if(campaign.FindStage(n.AfterStage)==null)throw new Refused("document "+d.Id+": note stage "+n.AfterStage+" is not in the campaign");
   }
   var now=content.Documents??Array.Empty<WorldMacroPlaytestSO.DocumentSpec>();
   var next=now.Where(x=>x!=null&&!want.Any(w=>w.Id==x.Id)).Concat(want).ToArray();
   string before=DocsJson(now),after=DocsJson(next);
   var sb=new StringBuilder("field308-docs"+(dry?" (dry)":"")+"\n content "+AssetDatabase.GetAssetPath(content)+"\n");
   foreach(var d in want)sb.AppendLine("  "+d.Id+" \""+d.Title+"\" pages "+d.Pages.Length+" notes "+d.Notes.Length+" grant on "+d.GrantOnStage);
   if(before==after)return sb.Append(" no-op (already written)").ToString();
   if(dry)return sb.Append(" would write "+next.Length+" document rows (was "+now.Length+")").ToString();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,utc,AssetDatabase.GetAssetPath(content)));
   if(ledger.docsBefore==null)ledger.docsBefore=before;
   content.Documents=next;EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   ledger.docsAfter=DocsJson(content.Documents);WriteLedger(ledger);
   return sb.Append(" wrote "+next.Length+" rows; ledger "+LedgerPath).ToString();
  }

  static string DocsRevert()
  {
   PostLedger308.RequireEditable();
   var j=Data();var content=Load<WorldMacroPlaytestSO>(S(j,"content"));var ledger=ReadLedger();
   if(ledger.docsBefore==null)return "field308-docs:revert nothing recorded";
   if(DocsJson(content.Documents)!=ledger.docsAfter)return "refused: Documents changed since field308-docs (kept; compare by hand with the backup in "+BackupRoot+")";
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(content)));
   content.Documents=JsonUtility.FromJson<DocsWrap>(ledger.docsBefore).items??Array.Empty<WorldMacroPlaytestSO.DocumentSpec>();
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   ledger.docsBefore=ledger.docsAfter=null;WriteLedger(ledger);
   return "field308-docs:revert restored "+content.Documents.Length+" rows";
  }

  // ---------------------------------------------------------------- start
  /// <summary>Why the stage could not be completed by a new game after the removal (null = wired).</summary>
  static string StartWiring(JObject j,WorldMacroPlaytestSO content,DemoCampaignProfile campaign)
  {
   var cs=j["campaign_start"]??throw new Refused("field117.json: campaign_start missing");
   string stageId=S(cs,"stage"),pointId=S(cs,"trigger_point");
   var stage=campaign.FindStage(stageId);if(stage==null)return "stage "+stageId+" is not in the campaign";
   if(stage.Event!=DemoEventKind.Interaction||stage.TriggerId!=pointId)return "stage "+stageId+" is "+stage.Event+" on '"+stage.TriggerId+"', not Interaction on "+pointId;
   if(!stage.Implemented)return "stage "+stageId+" is not Implemented";
   var point=Array.Find(content.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id==pointId);
   if(point==null)return "point "+pointId+" is not in "+AssetDatabase.GetAssetPath(content)+" (Texts308 content-apply:main first)";
   if(point.Kind!=PrologueInteractionKind.Conversation)return "point "+pointId+" is "+point.Kind+", not Conversation";
   if((content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).Any(c=>c!=null&&c.GiverId==pointId))return "point "+pointId+" is a commission giver (TryHandleCommission would take the talk first)";
   if((stage.PrerequisiteIds??Array.Empty<string>()).Length>0||(stage.RequiredFacts??Array.Empty<string>()).Length>0)return "stage "+stageId+" has prerequisites / facts: a new game could not complete it on the first talk";
   return null;
  }

  static string Start(bool dry)
  {
   PostLedger308.RequireEditable();
   var j=Data();var content=Load<WorldMacroPlaytestSO>(S(j,"content"));var campaign=Load<DemoCampaignProfile>(S(j,"campaign"));
   string why=StartWiring(j,content,campaign);if(why!=null)throw new Refused(why);
   var remove=((j["campaign_start"]["remove_initial"] as JArray)??new JArray()).Select(x=>x.Value<string>()).ToArray();
   var now=campaign.InitialCompletedIds??Array.Empty<string>();var next=now.Where(id=>!remove.Contains(id)).ToArray();
   var sb=new StringBuilder("field308-start"+(dry?" (dry)":"")+"\n campaign "+AssetDatabase.GetAssetPath(campaign)+"\n InitialCompletedIds ["+string.Join(", ",now)+"] -> ["+string.Join(", ",next)+"]\n");
   if(next.Length==now.Length)return sb.Append(" no-op").ToString();
   if(dry)return sb.Append(" (existing saves keep their completed stages; only a new game changes)").ToString();
   var ledger=ReadLedger();
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(campaign)));
   if(ledger.initialBefore==null)ledger.initialBefore=IdsJson(now);
   campaign.InitialCompletedIds=next;EditorUtility.SetDirty(campaign);AssetDatabase.SaveAssetIfDirty(campaign);
   ledger.initialAfter=IdsJson(campaign.InitialCompletedIds);WriteLedger(ledger);
   return sb.Append(" wrote; ledger "+LedgerPath).ToString();
  }

  static string StartRevert()
  {
   PostLedger308.RequireEditable();
   var j=Data();var campaign=Load<DemoCampaignProfile>(S(j,"campaign"));var ledger=ReadLedger();
   if(ledger.initialBefore==null)return "field308-start:revert nothing recorded";
   if(IdsJson(campaign.InitialCompletedIds)!=ledger.initialAfter)return "refused: InitialCompletedIds changed since field308-start (kept)";
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(campaign)));
   campaign.InitialCompletedIds=JsonUtility.FromJson<IdsWrap>(ledger.initialBefore).items??Array.Empty<string>();
   EditorUtility.SetDirty(campaign);AssetDatabase.SaveAssetIfDirty(campaign);
   ledger.initialBefore=ledger.initialAfter=null;WriteLedger(ledger);
   return "field308-start:revert InitialCompletedIds = ["+string.Join(", ",campaign.InitialCompletedIds)+"]";
  }

  // ---------------------------------------------------------------- check
  static string Check()
  {
   var j=Data();var content=Load<WorldMacroPlaytestSO>(S(j,"content"));var campaign=Load<DemoCampaignProfile>(S(j,"campaign"));
   var lines=new List<string>();int fail=0;
   void C(bool ok,string what){lines.Add((ok?"PASS ":"FAIL ")+what);if(!ok)fail++;}
   var want=DataDocs(j);var have=content.Documents??Array.Empty<WorldMacroPlaytestSO.DocumentSpec>();
   foreach(var d in want)
   {
    var h=content.FindDocument(d.Id);
    C(h!=null&&DocsJson(new[]{h})==DocsJson(new[]{d}),"B2 document "+d.Id+" in content equals field117.json");
    C(campaign.FindStage(d.GrantOnStage)!=null,"B2 "+d.Id+" grant stage "+d.GrantOnStage+" exists");
   }
   C(have.All(d=>d==null||d.IsConfigured),"B2 every content document is configured (prefix "+WorldMacroPlaytestSO.DocumentSpec.ItemPrefix+")");
   C(!WorldMacroCollectionCatalogHasLetter(),"B2 no 석경 item id carries the document prefix");
   string why=StartWiring(j,content,campaign);
   C(why==null,"B1 commission wiring"+(why!=null?": "+why:" (Conversation point, TriggerId, not a giver, no prerequisite)"));
   var remove=((j["campaign_start"]["remove_initial"] as JArray)??new JArray()).Select(x=>x.Value<string>()).ToArray();
   C(!(campaign.InitialCompletedIds??Array.Empty<string>()).Any(remove.Contains),"B1 a new game no longer starts with ["+string.Join(", ",remove)+"]");
   C2Check(j,content,C);
   return "field308-check "+(fail==0?"PASS":"FAIL "+fail)+"\n"+string.Join("\n",lines);
  }

  static bool WorldMacroCollectionCatalogHasLetter()=>Oheangbu.App.World.UI.WorldMacroCollectionCatalog.AllFragments.Any(f=>WorldMacroPlaytestSO.DocumentSpec.IsDocumentItem(f.ItemId));

  static string Heights()
  {
   const float W=4000f,D=6000f,step=25f,cell=500f;int cols=(int)(W/cell),rows=(int)(D/cell);
   var max=new float[cols,rows];for(int x=0;x<cols;x++)for(int z=0;z<rows;z++)max[x,z]=float.NegativeInfinity;
   var all=new List<float>();Physics.SyncTransforms();
   for(float x=step*.5f;x<W;x+=step)for(float z=step*.5f;z<D;z+=step)
   {
    float top=float.NegativeInfinity;
    foreach(var h in Physics.RaycastAll(new Vector3(x,3000f,z),Vector3.down,6000f,~0,QueryTriggerInteraction.Ignore))
     if(!(h.collider is CharacterController)&&h.point.y>top)top=h.point.y;
    if(float.IsNegativeInfinity(top))continue;
    all.Add(top);int cx=(int)(x/cell),cz=(int)(z/cell);if(top>max[cx,cz])max[cx,cz]=top;
   }
   var sb=new StringBuilder("field308-heights ("+all.Count+" columns hit, open scenes "+UnityEngine.SceneManagement.SceneManager.sceneCount+")\n");
   if(all.Count==0)return sb.Append("  nothing hit").ToString();
   all.Sort();string P(float q)=>all[Mathf.Clamp((int)(q*(all.Count-1)),0,all.Count-1)].ToString("F0");
   sb.AppendLine("  percentiles p10 "+P(.1f)+" p50 "+P(.5f)+" p75 "+P(.75f)+" p90 "+P(.9f)+" p97 "+P(.97f)+" max "+P(1f));
   sb.AppendLine("  max per 500 m cell, z rows north (5500) to south (0), x columns 0 .. 3500:");
   for(int z=rows-1;z>=0;z--){sb.Append("  z"+(z*cell).ToString("0000")+" ");for(int x=0;x<cols;x++)sb.Append(float.IsNegativeInfinity(max[x,z])?"    -":max[x,z].ToString("F0").PadLeft(5));sb.AppendLine();}
   return sb.ToString().TrimEnd();
  }

  static readonly string[] CloudProps={"_CloudBand308","_CloudShape308","_CloudColor308"};
  static string Inv(float f)=>f.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
  static string CloudJson(Material m)=>string.Join(";",CloudProps.Select(p=>{var v=m.GetVector(p);return Inv(v.x)+","+Inv(v.y)+","+Inv(v.z)+","+Inv(v.w);}));
  static Vector4 V4(JToken o,string key)
  {
   var a=o?[key] as JArray;if(a==null||a.Count!=4)throw new Refused("field117.json: cloud_band."+key+" needs 4 numbers");
   return new Vector4(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>(),a[3].Value<float>());
  }
  static string Cloud(bool dry)
  {
   PostLedger308.RequireEditable();
   var c=Data()["cloud_band"]??throw new Refused("field117.json: cloud_band missing (python Tools/Art/FieldFirst308/field117_build.py)");
   var m=Load<Material>(S(c,"material"));
   foreach(var p in CloudProps)if(!m.HasProperty(p))throw new Refused(m.name+" has no "+p+" (RealmFog297 without the D308-118 band: reimport the shader)");
   var want=new[]{V4(c,"band"),V4(c,"shape"),V4(c,"colour")};
   if(want[0].y<1||want[0].z<0||want[0].z>.05f||want[1].x<10||want[1].y<0||want[1].y>1||want[2].w<0||want[2].w>1)throw new Refused("cloud_band values out of range (half-width >= 1, density 0 .. .05, scale >= 10, coverage 0 .. 1, alpha 0 .. 1)");
   if(Mathf.Max(want[2].x,Mathf.Max(want[2].y,want[2].z))>1)throw new Refused("cloud_band colour must be LDR (<= 1): pigment, not light (ART-INK)");
   string before=CloudJson(m);
   var sb=new StringBuilder("field308-cloud"+(dry?" (dry)":"")+"\n material "+AssetDatabase.GetAssetPath(m)+"\n");
   for(int i=0;i<3;i++)sb.AppendLine(" "+CloudProps[i]+" "+m.GetVector(CloudProps[i])+" -> "+want[i]);
   if(dry)return sb.ToString().TrimEnd();
   var ledger=ReadLedger();
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(m)));
   if(ledger.cloudBefore==null)ledger.cloudBefore=before;
   for(int i=0;i<3;i++)m.SetVector(CloudProps[i],want[i]);
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
   ledger.cloudAfter=CloudJson(m);WriteLedger(ledger);
   return sb.Append(" wrote; ledger "+LedgerPath).ToString();
  }
  static string CloudRevert()
  {
   PostLedger308.RequireEditable();
   var c=Data()["cloud_band"]??throw new Refused("field117.json: cloud_band missing");
   var m=Load<Material>(S(c,"material"));var ledger=ReadLedger();
   if(ledger.cloudBefore==null)return "field308-cloud:revert nothing recorded";
   if(CloudJson(m)!=ledger.cloudAfter)return "refused: the cloud values changed since field308-cloud (kept; the backup is in "+BackupRoot+")";
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(m)));
   var parts=ledger.cloudBefore.Split(';');
   for(int i=0;i<3;i++){var f=parts[i].Split(',').Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();m.SetVector(CloudProps[i],new Vector4(f[0],f[1],f[2],f[3]));}
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
   ledger.cloudBefore=ledger.cloudAfter=null;WriteLedger(ledger);
   return "field308-cloud:revert "+CloudJson(m);
  }

  static float GroundY(float x,float z)
  {
   float best=float.NegativeInfinity;
   foreach(var h in Physics.RaycastAll(new Vector3(x,3000f,z),Vector3.down,6000f,~0,QueryTriggerInteraction.Ignore))
    if(!(h.collider is CharacterController)&&h.normal.y>.5f&&h.point.y>best)best=h.point.y;
   if(float.IsNegativeInfinity(best))throw new Refused("no ground at ("+x.ToString("F0")+", "+z.ToString("F0")+") - open the realms (RealmStream308 open-all)");
   return best;
  }
  static string Kind(Transform t)
  {
   for(var p=t;p!=null;p=p.parent)
   {
    string n=p.name;
    if(n.Contains("Terrain")||n.Contains("Surface")||n.Contains("Cliff")||n.Contains("Rugged"))return "지형";
    if(n.Contains("Tree")||n.Contains("tree")||n.Contains("Pine")||n.Contains("Flora")||n.Contains("Foliage")||n.Contains("Bush")||n.Contains("Veg"))return "식생";
    if(n.Contains("House")||n.Contains("Building")||n.Contains("Realm_")||n.Contains("Wall"))return "건물";
   }
   return "소품";
  }
  sealed class SightTry{public float step,hardAt=float.PositiveInfinity;public Vector3 eye;public string hard="열림",soft="열림";public int softCount;public bool Open=>float.IsPositiveInfinity(hardAt)&&softCount==0;}
  static string Sight()
  {
   var c=Data()["sightline"]??throw new Refused("field117.json: sightline missing (python Tools/Art/FieldFirst308/field117_build.py)");
   float eyeUp=c["eye_up"].Value<float>(),half=c["front_half_deg"].Value<float>(),gap=c["end_gap_m"].Value<float>();
   float softMin=c["soft_min_size_m"].Value<float>(),softMax=c["soft_max_size_m"].Value<float>();
   var steps=((c["eye_steps_m"] as JArray)??new JArray{0}).Select(x=>x.Value<float>()).ToArray();
   float roadSnap=c["road_snap_m"]?.Value<float>()??30f;
   var content=Load<WorldMacroPlaytestSO>(S(Data(),"content"));
   var roads=new[]{content.MainPath,content.BranchPath}.Where(r=>r!=null&&r.Length>1).ToArray();
   Physics.SyncTransforms();
   // soft blockers: single objects (foliage, props) whose bounds are softMin .. softMax; merged / terrain-sized meshes are
   // left to the colliders (their box would block everything)
   var soft=new List<Renderer>();
   for(int si=0;si<UnityEngine.SceneManagement.SceneManager.sceneCount;si++)
   {
    var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);if(!sc.isLoaded)continue;
    foreach(var g in sc.GetRootGameObjects())foreach(var r in g.GetComponentsInChildren<Renderer>(false))
    {
     if(!r.enabled||r is ParticleSystemRenderer||r is TrailRenderer||r is LineRenderer)continue;
     float size=r.bounds.size.magnitude;if(size>=softMin&&size<=softMax)soft.Add(r);
    }
   }
   // sheet vegetation (Flora308 and the other dressing sheets) is drawn instanced by CompactRebuildArtRenderer: no Renderer, no
   // collider, so the scan above never saw it (2026-10-20: legs the audit called open were solid canopy in the stills). Tree / shrub
   // rows become soft boxes with the renderer's own footprint (Prepare: Size x Scale, radius max(x,z) x .75); the lower 30 % of the
   // height is left out - the eye sees between trunks under the crowns.
   var sheetSoft=new List<(Bounds b,string name)>();var sheetsSeen=new HashSet<WorldMacroDressingSheetSO>();
   foreach(var art in UnityEngine.Object.FindObjectsByType<Oheangbu.App.World.CompactRebuildArtRenderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
   {
    var sheet=art.Sheet;if(!art.enabled||sheet==null||!sheetsSeen.Add(sheet))continue;
    var protos=sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
    foreach(var f in sheet.FixedPlacements)
    {
     if(f==null||f.PrototypeId==null||!protos.TryGetValue(f.PrototypeId,out var p))continue;
     if(p.Category!=WorldMacroDressingSheetSO.Kind.Tree&&p.Category!=WorldMacroDressingSheetSO.Kind.Shrub)continue;
     var size=p.Size*f.Scale;float radius=Mathf.Max(size.x,size.z)*.75f;float h=Mathf.Max(size.y,.2f);
     var b=new Bounds(f.Position+Vector3.up*h*.65f,new Vector3(radius*2,h*.7f,radius*2));
     float mag=new Vector3(radius*2,h,radius*2).magnitude;if(mag<softMin||mag>softMax)continue;
     sheetSoft.Add((b,"식생(시트) "+f.PrototypeId));
    }
   }
   var sb=new StringBuilder("field308-sight (open scenes "+UnityEngine.SceneManagement.SceneManager.sceneCount+", soft renderers "+soft.Count+" + sheet trees / shrubs "+sheetSoft.Count+" of "+softMin.ToString("F0")+" - "+softMax.ToString("F0")+" m, eye steps "+string.Join("/",steps.Select(x=>x.ToString("F0")))+" m)\n");
   sb.AppendLine(" leg | guide | dist m | angle° | front | result (first open eye step, else the last step's blockers)");
   int legs=0,legsOpen=0;
   foreach(var leg in (c["legs"] as JArray)??new JArray())
   {
    legs++;var e=leg["eye"] as JArray;var to=leg["toward"] as JArray;
    float ex=e[0].Value<float>(),ez=e[1].Value<float>();
    var heading=new Vector2(to[0].Value<float>()-ex,to[1].Value<float>()-ez).normalized;
    // the player walks the road: when both the beat and the next beat lie on one content path (within road_snap_m), the eye
    // steps are arc lengths along that path toward the next beat; else straight along the heading
    Func<float,Vector2> walk=step=>new Vector2(ex+heading.x*step,ez+heading.y*step);string how="직선";
    {
     float best=float.PositiveInfinity;
     foreach(var road in roads)
     {
      // a path passes a beat more than once (the loop back by the inn): pair the visits of the two beats with the shortest
      // stretch of road between them
      var v0=Visits(road,new Vector2(ex,ez),roadSnap);var v1=Visits(road,new Vector2(to[0].Value<float>(),to[1].Value<float>()),roadSnap);
      foreach(var a0 in v0)foreach(var b0 in v1)
      {
       float span=Mathf.Abs(b0-a0);if(span>=best)continue;
       best=span;var r=road;float a=a0,b=b0;walk=step=>At(r,a+Mathf.Sign(b-a)*Mathf.Min(step,Mathf.Abs(b-a)));
      how="길 "+(road==content.MainPath?"Main":"Branch")+" "+Mathf.Abs(b-a).ToString("F0")+" m";
      }
     }
    }
    sb.AppendLine(" "+S(leg,"id")+" 눈 이동 = "+how);
    bool any=false;
    foreach(var g in (leg["guides"] as JArray)??new JArray())
    {
     float gx=g["x"].Value<float>(),gz=g["z"].Value<float>();
     float gy=g["y"]!=null?g["y"].Value<float>():GroundY(gx,gz)+g["up"].Value<float>();
     var target=new Vector3(gx,gy,gz);
     var first=new Vector3(ex,GroundY(ex,ez)+eyeUp,ez);var d0=target-first;
     float angle=Vector2.SignedAngle(heading,new Vector2(d0.x,d0.z));bool front=Mathf.Abs(angle)<=half;
     SightTry last=null,open=null;
     foreach(float step in steps)
     {
      var w=walk(step);float sx=w.x,sz=w.y;var eye=new Vector3(sx,GroundY(sx,sz)+eyeUp,sz);
      var d=target-eye;float dist=d.magnitude;var dir=d/dist;float reach=Mathf.Max(0,dist-gap);
      var t=new SightTry{step=step,eye=eye};
      foreach(var h in Physics.RaycastAll(eye,dir,reach,~0,QueryTriggerInteraction.Ignore))
       if(!(h.collider is CharacterController)&&h.distance>.5f&&h.distance<t.hardAt){t.hardAt=h.distance;t.hard=Kind(h.collider.transform)+" "+h.distance.ToString("F0")+" m "+h.collider.name;}
      var ray=new Ray(eye,dir);var hits=new List<(float t,Renderer r)>();
      foreach(var r in soft){if(r.bounds.Contains(eye)||r.bounds.Contains(target))continue;if(r.bounds.IntersectRay(ray,out float hit)&&hit>.5f&&hit<Mathf.Min(reach,t.hardAt))hits.Add((hit,r));}
      var named=hits.Select(x=>(x.t,label:Kind(x.r.transform)+" "+x.t.ToString("F0")+" m "+x.r.name)).ToList();
      foreach(var s in sheetSoft){if(s.b.Contains(eye)||s.b.Contains(target))continue;if(s.b.IntersectRay(ray,out float hit)&&hit>.5f&&hit<Mathf.Min(reach,t.hardAt))named.Add((hit,s.name+" "+hit.ToString("F0")+" m"));}
      t.softCount=named.Count;
      if(named.Count>0)t.soft=named.Count+"개, 첫 "+string.Join(" / ",named.OrderBy(x=>x.t).Take(2).Select(x=>x.label));
      last=t;if(t.Open){open=t;break;}
     }
     if(open!=null&&front)any=true;
     // the eye of the first open step (xz) and the guide point, so a still can be shot from where the guide first shows
     string result=open!=null?"열림 (+"+open.step.ToString("F0")+" m)"+(front?"  [O]":"")+" eye ("+open.eye.x.ToString("F1")+", "+open.eye.z.ToString("F1")+") guide ("+gx.ToString("F1")+", "+gy.ToString("F1")+", "+gz.ToString("F1")+")":"막힘 — 단단 "+last.hard+" | 무른 "+last.soft+" | last eye ("+last.eye.x.ToString("F1")+", "+last.eye.z.ToString("F1")+")";
     sb.AppendLine(" "+S(leg,"id")+" | "+S(g,"name")+" | "+d0.magnitude.ToString("F0")+" | "+angle.ToString("F0")+" | "+(front?"앞":"옆/뒤")+" | "+result);
    }
    if(any)legsOpen++;
    sb.AppendLine(" "+S(leg,"id")+" => "+(any?"길잡이 보임 O":"막힘 X"));
   }
   sb.Insert(0,"legs open "+legsOpen+" / "+legs+"\n");
   string text=sb.ToString().TrimEnd();
   Directory.CreateDirectory(BackupRoot);File.WriteAllText(Path.Combine(BackupRoot,"sight-"+PostLedger308.Utc()+".txt"),text);
   return text;
  }
  // arc lengths where a polyline passes within snap of q: one (the closest) per run of near segments
  static List<float> Visits(Vector3[] path,Vector2 q,float snap)
  {
   var list=new List<float>();float runBest=float.PositiveInfinity,runArc=0,arc=0;bool inRun=false;
   for(int i=0;i+1<path.Length;i++)
   {
    var a=new Vector2(path[i].x,path[i].z);var b=new Vector2(path[i+1].x,path[i+1].z);var ab=b-a;float len=ab.magnitude;
    float t=len<1e-4f?0:Mathf.Clamp01(Vector2.Dot(q-a,ab)/(len*len));float d=Vector2.Distance(q,a+ab*t);
    if(d<=snap){if(!inRun){inRun=true;runBest=float.PositiveInfinity;}if(d<runBest){runBest=d;runArc=arc+t*len;}}
    else if(inRun){inRun=false;list.Add(runArc);}
    arc+=len;
   }
   if(inRun)list.Add(runArc);
   return list;
  }
  // nearest point of a polyline (xz): distance and arc length there
  static (float dist,float arc) Project(Vector3[] path,Vector2 q)
  {
   float best=float.PositiveInfinity,arcBest=0,arc=0;
   for(int i=0;i+1<path.Length;i++)
   {
    var a=new Vector2(path[i].x,path[i].z);var b=new Vector2(path[i+1].x,path[i+1].z);var ab=b-a;float len=ab.magnitude;
    float t=len<1e-4f?0:Mathf.Clamp01(Vector2.Dot(q-a,ab)/(len*len));float d=Vector2.Distance(q,a+ab*t);
    if(d<best){best=d;arcBest=arc+t*len;}
    arc+=len;
   }
   return (best,arcBest);
  }
  static Vector2 At(Vector3[] path,float s)
  {
   float arc=0;
   for(int i=0;i+1<path.Length;i++)
   {
    var a=new Vector2(path[i].x,path[i].z);var b=new Vector2(path[i+1].x,path[i+1].z);float len=Vector2.Distance(a,b);
    if(arc+len>=s)return len<1e-4f?a:Vector2.Lerp(a,b,(s-arc)/len);
    arc+=len;
   }
   var e=path[path.Length-1];return new Vector2(e.x,e.z);
  }
 }
}
