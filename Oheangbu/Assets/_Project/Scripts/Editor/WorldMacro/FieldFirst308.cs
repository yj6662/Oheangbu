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
 //   field308-check                the two writes against the data and the runtime wiring rules (read only)
 // Data: Art/World/Compact/Rebuild/FieldFirst308/field117.json (built by Tools/Art/FieldFirst308/field117_build.py from
 // DECISIONS D308-117 F2; the code holds no line). Ledger + asset backups: Art/Playtest306/Backups/FieldFirst308/. Edit mode only,
 // no AssetDatabase.SaveAssets (the target asset is saved alone).
 public static class FieldFirst308
 {
  sealed class Refused:Exception{public Refused(string m):base(m){}}
  [Serializable] sealed class DocsWrap{public WorldMacroPlaytestSO.DocumentSpec[] items=Array.Empty<WorldMacroPlaytestSO.DocumentSpec>();}
  [Serializable] sealed class IdsWrap{public string[] items=Array.Empty<string>();}
  [Serializable] sealed class Ledger{public string docsBefore,docsAfter,initialBefore,initialAfter;public List<string> backups=new List<string>();}

  static string DataFile=>PostLedger308.RepoPath("Art/World/Compact/Rebuild/FieldFirst308/field117.json");
  static string BackupRoot=>PostLedger308.RepoPath("Art/Playtest306/Backups/FieldFirst308");
  static string LedgerPath=>Path.Combine(BackupRoot,"ledger.json");
  static Ledger ReadLedger()=>File.Exists(LedgerPath)?JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath))??new Ledger():new Ledger();
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
   return "field308-check "+(fail==0?"PASS":"FAIL "+fail)+"\n"+string.Join("\n",lines);
  }

  static bool WorldMacroCollectionCatalogHasLetter()=>Oheangbu.App.World.UI.WorldMacroCollectionCatalog.AllFragments.Any(f=>WorldMacroPlaytestSO.DocumentSpec.IsDocumentItem(f.ItemId));
 }
}
