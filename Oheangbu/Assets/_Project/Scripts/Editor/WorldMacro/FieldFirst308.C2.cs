using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // SPEC-FIELD-FIRST-HOUR-308 C2 (D308-117 F3 - F6, TEST): the four sub-NPC commissions of 청림.
 //   field308-c2[:dry|:revert]  WorldContent_Main.Commissions <- field117.json c2.commissions[] (rows replaced by id, others kept)
 //        and the LockedText (before the request) / Text (the pick-up) of each c2.evidence[] point. Refused while a giver is not a
 //        Conversation point of the content with no talk rows (Texts308 content-apply:main first - XF3 - XF6), is a campaign
 //        trigger or another commission's giver; while an evidence point is missing / not Evidence / another commission's
 //        evidence; while a target is not a content encounter; while a reward is outside c2.reward_band. Revert puts the rows and
 //        texts of the ledger back while the content still holds what this tool wrote.
 // The giver's name, first words, place and body are Texts308's (texts308.json XF3 - XF6, field117_texts.py); the request lines
 // live here in the content, not in the code (field117_build.py copies them from DECISIONS D308-117 by position).
 public static partial class FieldFirst308
 {
  [Serializable] sealed class EvidenceText{public string id="",locked="",text="";}
  [Serializable] sealed class C2Wrap{public WorldMacroPlaytestSO.CommissionSpec[] items=Array.Empty<WorldMacroPlaytestSO.CommissionSpec>();public EvidenceText[] evidence=Array.Empty<EvidenceText>();}

  static JToken C2Data(JObject j)=>j["c2"]??throw new Refused("field117.json: c2 missing (python Tools/Art/FieldFirst308/field117_build.py)");
  static string[] Lines(JToken o,string key)=>((o?[key] as JArray)??new JArray()).Select(x=>x.Value<string>()).ToArray();

  static WorldMacroPlaytestSO.CommissionSpec[] DataCommissions(JToken c2)
  {
   var list=new List<WorldMacroPlaytestSO.CommissionSpec>();
   foreach(var c in (c2["commissions"] as JArray)??new JArray())
    list.Add(new WorldMacroPlaytestSO.CommissionSpec
    {
     Id=S(c,"id"),GiverId=S(c,"giver"),EvidenceId=c["evidence"]?.Value<string>()??"",TargetEncounterId=c["target"]?.Value<string>()??"",
     OfferText=S(c,"offer_text"),WaitingText=S(c,"waiting_text"),ReportText=S(c,"report_text"),CompletedText=S(c,"completed_text"),
     Reward=c["reward"]?.Value<int>()??-1,State="TEST",Speaker=S(c,"speaker"),Title="",
     OfferLines=Lines(c,"offer_lines"),WaitingLines=Lines(c,"waiting_lines"),ReportLines=Lines(c,"report_lines"),CompletedLines=Lines(c,"completed_lines"),
    });
   if(list.Count==0)throw new Refused("field117.json: c2.commissions[] is empty");
   return list.ToArray();
  }
  static EvidenceText[] DataEvidence(JToken c2)=>((c2["evidence"] as JArray)??new JArray()).Select(e=>new EvidenceText{id=S(e,"id"),locked=S(e,"locked"),text=S(e,"text")}).ToArray();

  static PrologueContentSO.Point PointOf(WorldMacroPlaytestSO content,string id)=>Array.Find(content.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id==id);

  /// <summary>Why the commissions could not be taken / satisfied in a game (empty = wired).</summary>
  static List<string> C2Wiring(JObject j,WorldMacroPlaytestSO content,WorldMacroPlaytestSO.CommissionSpec[] want,EvidenceText[] ev)
  {
   var bad=new List<string>();var c2=C2Data(j);
   var band=(c2["reward_band"] as JArray)??throw new Refused("field117.json: c2.reward_band missing");
   int lo=band[0].Value<int>(),hi=band[1].Value<int>();
   var ours=new HashSet<string>(want.Select(w=>w.Id));
   var others=(content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).Where(x=>x!=null&&!ours.Contains(x.Id)).ToArray();
   string trigger=j["campaign_start"]?["trigger_point"]?.Value<string>();
   foreach(var w in want)
   {
    if(!w.IsConfigured)bad.Add(w.Id+" is not configured (id, giver, evidence or target, reward >= 0)");
    if(w.Reward<lo||w.Reward>hi)bad.Add(w.Id+": reward "+w.Reward+" is outside "+lo+" - "+hi);
    if(w.OfferLines.Length==0||w.WaitingLines.Length==0||w.ReportLines.Length==0||w.CompletedLines.Length==0)bad.Add(w.Id+": a line set is empty");
    var g=PointOf(content,w.GiverId);
    if(g==null)bad.Add(w.Id+": giver point "+w.GiverId+" is not in the content (Texts308 content-apply:main first)");
    else
    {
     if(g.Kind!=PrologueInteractionKind.Conversation)bad.Add(w.Id+": giver "+w.GiverId+" is "+g.Kind+", not Conversation");
     if((g.Services??Array.Empty<PrologueContentSO.PointService306>()).Length>0)bad.Add(w.Id+": giver "+w.GiverId+" carries "+g.Services.Length+" talk / service row(s) (they would stand under the request)");
    }
    if(w.GiverId==trigger)bad.Add(w.Id+": giver "+w.GiverId+" is the campaign trigger point (the stage could not complete)");
    if(others.Any(o=>o.GiverId==w.GiverId)||want.Count(o=>o.GiverId==w.GiverId)>1)bad.Add(w.Id+": giver "+w.GiverId+" already gives another commission");
    if(!string.IsNullOrEmpty(w.EvidenceId))
    {
     var e=PointOf(content,w.EvidenceId);
     if(e==null)bad.Add(w.Id+": evidence point "+w.EvidenceId+" is not in the content");
     else if(e.Kind!=PrologueInteractionKind.Evidence)bad.Add(w.Id+": evidence "+w.EvidenceId+" is "+e.Kind+", not Evidence");
     if(others.Any(o=>o.EvidenceId==w.EvidenceId)||want.Count(o=>o.EvidenceId==w.EvidenceId)>1)bad.Add(w.Id+": evidence "+w.EvidenceId+" belongs to another commission too");
     if(!ev.Any(x=>x.id==w.EvidenceId))bad.Add(w.Id+": evidence "+w.EvidenceId+" has no c2.evidence[] texts");
    }
    if(!string.IsNullOrEmpty(w.TargetEncounterId)&&!(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Any(x=>x!=null&&x.Id==w.TargetEncounterId))
     bad.Add(w.Id+": target "+w.TargetEncounterId+" is not a content encounter");
   }
   foreach(var e in ev)if(!want.Any(w=>w.EvidenceId==e.id))bad.Add("c2.evidence "+e.id+" belongs to no c2 commission");
   return bad;
  }

  // the state this tool owns: our commission rows (content order) + the texts of our evidence points
  static string C2Json(WorldMacroPlaytestSO content,ICollection<string> ids,IEnumerable<string> evidenceIds)=>JsonUtility.ToJson(new C2Wrap
  {
   items=(content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).Where(x=>x!=null&&ids.Contains(x.Id)).ToArray(),
   evidence=evidenceIds.Select(id=>{var p=PointOf(content,id);return new EvidenceText{id=id,locked=p?.LockedText??"",text=p?.Text??""};}).ToArray(),
  });

  static string C2(bool dry)
  {
   PostLedger308.RequireEditable();
   var j=Data();var content=Load<WorldMacroPlaytestSO>(S(j,"content"));var c2=C2Data(j);
   var want=DataCommissions(c2);var ev=DataEvidence(c2);
   var bad=C2Wiring(j,content,want,ev);if(bad.Count>0)throw new Refused("C2 wiring: "+string.Join("; ",bad)+". Nothing written");
   var ids=want.Select(w=>w.Id).ToList();var evIds=ev.Select(e=>e.id).ToArray();
   var now=content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>();
   var next=now.Where(x=>x!=null).Select(x=>want.FirstOrDefault(w=>w.Id==x.Id)??x).Concat(want.Where(w=>!now.Any(x=>x!=null&&x.Id==w.Id))).ToArray();
   string before=C2Json(content,ids,evIds);
   var sb=new StringBuilder("field308-c2"+(dry?" (dry)":"")+"\n content "+AssetDatabase.GetAssetPath(content)+"\n");
   foreach(var w in want)sb.AppendLine("  "+w.Id+" "+w.Speaker+" @ "+w.GiverId+" -> "+(string.IsNullOrEmpty(w.EvidenceId)?"target "+w.TargetEncounterId:"evidence "+w.EvidenceId)+", reward "+w.Reward+(now.Any(x=>x!=null&&x.Id==w.Id)?" (replaces)":" (new)"));
   foreach(var e in ev){var p=PointOf(content,e.id);sb.AppendLine("  "+e.id+" locked \""+p.LockedText+"\" -> \""+e.locked+"\" | text \""+p.Text+"\" -> \""+e.text+"\"");}
   string after=JsonUtility.ToJson(new C2Wrap{items=next.Where(x=>ids.Contains(x.Id)).ToArray(),evidence=ev});
   if(before==after)return sb.Append(" no-op (already written)").ToString();
   if(dry)return sb.Append(" would write "+next.Length+" commission rows (was "+now.Length+") and "+ev.Length+" evidence texts").ToString();
   var ledger=ReadLedger();
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(content)));
   if(ledger.c2Before==null)ledger.c2Before=before;
   content.Commissions=next;
   foreach(var e in ev){var p=PointOf(content,e.id);p.LockedText=e.locked;p.Text=e.text;}
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   ledger.c2After=C2Json(content,ids,evIds);WriteLedger(ledger);
   return sb.Append(" wrote; ledger "+LedgerPath).ToString();
  }

  static string C2Revert()
  {
   PostLedger308.RequireEditable();
   var j=Data();var content=Load<WorldMacroPlaytestSO>(S(j,"content"));var c2=C2Data(j);var ledger=ReadLedger();
   if(ledger.c2Before==null)return "field308-c2:revert nothing recorded";
   var ids=DataCommissions(c2).Select(w=>w.Id).ToList();var evIds=DataEvidence(c2).Select(e=>e.id).ToArray();
   if(C2Json(content,ids,evIds)!=ledger.c2After)return "refused: the C2 rows / evidence texts changed since field308-c2 (kept; compare by hand with the backup in "+BackupRoot+")";
   var was=JsonUtility.FromJson<C2Wrap>(ledger.c2Before);
   ledger.backups.Add(PostLedger308.Backup(BackupRoot,PostLedger308.Utc(),AssetDatabase.GetAssetPath(content)));
   content.Commissions=(content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).Where(x=>x!=null&&!ids.Contains(x.Id)).Concat(was.items??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).ToArray();
   foreach(var e in was.evidence??Array.Empty<EvidenceText>()){var p=PointOf(content,e.id);if(p!=null){p.LockedText=e.locked;p.Text=e.text;}}
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   ledger.c2Before=ledger.c2After=null;WriteLedger(ledger);
   return "field308-c2:revert "+content.Commissions.Length+" commission rows, "+(was.evidence?.Length??0)+" evidence texts restored";
  }

  // read-only rows for field308-check
  static void C2Check(JObject j,WorldMacroPlaytestSO content,Action<bool,string> C)
  {
   var c2=j["c2"];if(c2==null){C(false,"C2 field117.json has no c2 (rebuild the data)");return;}
   var want=DataCommissions(c2);var ev=DataEvidence(c2);
   foreach(var w in want)
   {
    var h=Array.Find(content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>(),x=>x!=null&&x.Id==w.Id);
    C(h!=null&&JsonUtility.ToJson(h)==JsonUtility.ToJson(w),"C2 commission "+w.Id+" ("+w.Speaker+") in content equals field117.json");
   }
   foreach(var e in ev)
   {
    var p=PointOf(content,e.id);
    C(p!=null&&p.LockedText==e.locked&&p.Text==e.text,"C2 evidence "+e.id+" LockedText / Text equal field117.json");
   }
   var bad=C2Wiring(j,content,want,ev);
   C(bad.Count==0,"C2 wiring"+(bad.Count>0?": "+string.Join("; ",bad):" (Conversation givers without talk rows, Evidence points, encounters, rewards in band)"));
  }
 }
}
