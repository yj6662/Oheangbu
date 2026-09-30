using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 §2-3 (SPEC-PLAYTEST-306 #3, PLAN §5 ⑦): move leading "X: " speaker prefixes into the Speaker fields of the Main content
 // (Scenes/World/Main/WorldContent_Main.asset: Points[].Text/Lines, Commissions[] texts/lines) and strip them from the Main campaign it
 // references (Stage.Dialogue, Testimony.Text). The campaign has no speaker field: its speaker lands on the Main point of the same
 // trigger id. Words are otherwise unchanged. Conversation points still without a speaker get the name the code already used
 // (Escort.cs 왕소, the trade window plates, 정담's own line), else the prompt's name (the runtime fallback made explicit). The campaign asset is shared with W_Demo_Compact (03_Content.asset, never touched):
 // there only the prefix disappears and the speaker falls back to the prompt name. Queue-safe: refusals are strings, never a dialog.
 //   dry     table: asset | point id | field | speaker | source | first 20 chars after the move (nothing written)
 //   apply   backup both files once under Art/Playtest306/Backups/Dialogue306 (the oldest copy is kept), then write and save
 //   revert  copy the backups back and reimport (backups stay; they hold the state before the first apply / services write)
 //   revert:force  the same without the generation guard below
 //   generation guard (generations.txt: asset, backup|write|revert|recreate, SHA1, time, GUID): main-recreate deletes and re-clones
 //           WorldContent_Main (new GUID, logged as 'recreate' by CompactFinish297 MainCreate297), which starts a new generation. The next
 //           apply / services moves that file's older backup to superseded-<time>/ (never deleted) and backs the new clone up; revert refuses
 //           a backup of an older generation, and refuses while the file was changed since the last logged step (an inspector edit would be
 //           lost). A backup taken before the log existed counts as current until a recreate / GUID change is seen. revert:force overrides.
 //   services-dry:<point id>=<Kind[@Target],...>[;...] | services:<same>   talk menu rows on Main points (PointService306; Kind = Talk
 //           Trade Upgrade Rest Maintain, Target optional, an empty list clears), e.g. services:geumpyo_innkeeper=Rest@geumpyo_inn,Maintain
 public static class Dialogue306Migration
 {
  const string ContentPath="Assets/_Project/Scenes/World/Main/WorldContent_Main.asset",CampaignDir="Assets/_Project/Art/World/Architecture296/Data/";
  static readonly string[] Protected={"03_Content.asset","Watershed295","Reworld292","MountainTrail285","W_Demo_Compact.unity"};
  static readonly string[][] Known={new[]{"wangso_w1","왕소"},new[]{"jeongdam_j1","정담"},new[]{"village_shop","장비 상인"},new[]{"village_artisan","목공 장인"}};
  sealed class Row{public string Asset,Id,Field,Speaker,Source,Preview;public Action Write;}

  public static string Run(string command)
  {
   string raw=(command??"").Trim();int colon=raw.IndexOf(':');
   string c=(colon<0?raw:raw.Substring(0,colon)).Trim().ToLowerInvariant(),arg=colon<0?"":raw.Substring(colon+1).Trim();
   if(c!="dry"&&c!="apply"&&c!="revert"&&c!="services-dry"&&c!="services"||c=="revert"&&arg.Length>0&&arg!="force")return "REFUSED unknown command '"+raw+"' (dry | apply | revert | revert:force | services-dry:<id>=<rows> | services:<id>=<rows>)";
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
   if(EditorApplication.isCompiling||EditorApplication.isUpdating)return "REFUSED the editor is compiling or importing";
   var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(ContentPath);if(content==null)return "REFUSED no WorldMacroPlaytestSO at "+ContentPath;
   var campaign=content.Campaign;string campaignPath=campaign!=null?AssetDatabase.GetAssetPath(campaign):"";
   if(campaign!=null&&!campaignPath.StartsWith(CampaignDir,StringComparison.Ordinal))return "REFUSED the campaign is outside "+CampaignDir+": "+campaignPath;
   foreach(var path in new[]{ContentPath,campaignPath})foreach(var p in Protected)if(path.Contains(p))return "REFUSED protected path "+path;
   string backups=BackupDir();
   var assets=campaign!=null?new[]{ContentPath,campaignPath}:new[]{ContentPath};
   if(c=="revert")return Revert(backups,assets,arg=="force");
   if(c=="services"||c=="services-dry")return Services(content,arg,c=="services",backups);
   var rows=new List<Row>();Plan(content,campaign,rows);
   var table=Table(rows,campaignPath);
   if(c=="dry")return "DRY (nothing written)\n"+table;
   if(EditorUtility.IsDirty(content)||campaign!=null&&EditorUtility.IsDirty(campaign))return "REFUSED unsaved changes on the content or campaign asset - save or discard them first";
   int writes=rows.Count(r=>r.Write!=null);if(writes==0)return "NOTHING TO APPLY\n"+table;
   var kept=Backup(backups,assets);
   foreach(var r in rows)r.Write?.Invoke();
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   if(campaign!=null){EditorUtility.SetDirty(campaign);AssetDatabase.SaveAssetIfDirty(campaign);}
   Log(backups,assets,"write");
   return "APPLIED "+writes+" changes; backups in "+backups+": "+string.Join(", ",kept)+"\n"+table;
  }

  static string Full(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
  static string BackupDir()=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Playtest306/Backups/Dialogue306"));
  // the oldest copy of a generation wins: a second apply (or services after apply) keeps the pre-migration file; a re-created file
  // (main-recreate: logged 'recreate' or a new GUID) is a new generation: its old backup moves to superseded-<time>/ (never deleted)
  static List<string> Backup(string backups,string[] assets)
  {
   Directory.CreateDirectory(backups);var kept=new List<string>();string superseded=null;
   foreach(var a in assets)
   {
    string name=Path.GetFileName(a),to=Path.Combine(backups,name);
    if(File.Exists(to))
    {
     var entries=Logged(backups,a);int at=BackupEntry(entries,Sha1(to));string why=Recreated(entries,at,AssetDatabase.AssetPathToGUID(a));
     if(why==null){kept.Add(name+(at<0?" (kept older backup, taken before the generation log)":" (kept older backup)"));continue;}
     superseded??=Path.Combine(backups,"superseded-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(superseded);
     File.Move(to,Path.Combine(superseded,name));kept.Add(name+" ("+why+": new generation, older backup moved to "+Path.GetFileName(superseded)+")");
    }
    else kept.Add(name);
    File.Copy(Full(a),to);Log(backups,new[]{a},"backup");
   }
   var manifest=Path.Combine(backups,"manifest.txt");var listed=File.Exists(manifest)?File.ReadAllLines(manifest).ToList():new List<string>();
   foreach(var a in assets)if(!listed.Contains(a))listed.Add(a);File.WriteAllLines(manifest,listed);
   return kept;
  }
  static string Services(WorldMacroPlaytestSO content,string arg,bool apply,string backups)
  {
   if(string.IsNullOrWhiteSpace(arg))return "REFUSED services needs <point id>=<Kind[@Target],...>[;...]";
   var points=content.Points??Array.Empty<PrologueContentSO.Point>();var plan=new List<(PrologueContentSO.Point point,PrologueContentSO.PointService306[] rows)>();var sb=new StringBuilder();
   foreach(var entry in arg.Split(';'))
   {
    if(string.IsNullOrWhiteSpace(entry))continue;int eq=entry.IndexOf('=');if(eq<=0)return "REFUSED '"+entry+"' is not <point id>=<rows>";
    string id=entry.Substring(0,eq).Trim();var point=Array.Find(points,p=>p!=null&&p.Id==id);
    if(point==null)return "REFUSED no point "+id+" in "+ContentPath;
    if(point.Kind!=PrologueInteractionKind.Conversation)return "REFUSED "+id+" is "+point.Kind+", not Conversation";
    var rows=new List<PrologueContentSO.PointService306>();
    foreach(var item in entry.Substring(eq+1).Split(','))
    {
     if(string.IsNullOrWhiteSpace(item))continue;var parts=item.Split('@');
     if(!Enum.TryParse(parts[0].Trim(),true,out PrologueContentSO.PointServiceKind306 kind))return "REFUSED unknown row kind '"+parts[0].Trim()+"' (Talk Trade Upgrade Rest Maintain)";
     if(kind==PrologueContentSO.PointServiceKind306.Talk)return "REFUSED Talk rows need authored lines: set them in the inspector";
     string target=parts.Length>1?parts[1].Trim():"";
     if(kind==PrologueContentSO.PointServiceKind306.Rest&&target.Length>0&&!Array.Exists(points,p=>p!=null&&p.Id==target&&p.Kind==PrologueInteractionKind.Rest))return "REFUSED no Rest point "+target;
     rows.Add(new PrologueContentSO.PointService306{Kind=kind,Target=target});
    }
    plan.Add((point,rows.ToArray()));
    string Show(PrologueContentSO.PointService306[] r)=>r==null||r.Length==0?"(none)":string.Join(", ",r.Select(x=>x.Kind+(string.IsNullOrEmpty(x.Target)?"":"@"+x.Target)));
    sb.Append(id).Append(" | ").Append(Show(point.Services)).Append(" -> ").Append(Show(rows.ToArray())).Append('\n');
   }
   if(!apply)return "DRY (nothing written)\npoint id | rows before -> after\n"+sb;
   if(EditorUtility.IsDirty(content))return "REFUSED unsaved changes on "+ContentPath+" - save or discard them first";
   var kept=Backup(backups,new[]{ContentPath});
   foreach(var (point,rows) in plan)point.Services=rows;
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   Log(backups,new[]{ContentPath},"write");
   return "APPLIED services on "+plan.Count+" points; backups in "+backups+": "+string.Join(", ",kept)+"\npoint id | rows before -> after\n"+sb;
  }
  static string Revert(string backups,string[] assets,bool force)
  {
   // services writes back up the content only: restore whichever backups exist
   var present=assets.Where(a=>File.Exists(Path.Combine(backups,Path.GetFileName(a)))).ToArray();
   if(present.Length==0)return "REFUSED no backup in "+backups+" (nothing was applied?)";
   if(!force)
   {
    // every file is checked before any is copied: a stale backup never lands over a newer generation
    var stale=new List<string>();
    foreach(var a in present)
    {
     string backupHash=Sha1(Path.Combine(backups,Path.GetFileName(a)));var entries=Logged(backups,a);int at=BackupEntry(entries,backupHash);
     if(at<0&&entries.Any(e=>e.kind=="backup")){stale.Add(a+" (the backup file is not the one the log recorded)");continue;}
     string why=Recreated(entries,at,AssetDatabase.AssetPathToGUID(a));if(why!=null){stale.Add(a+" ("+why+" after its backup: an older generation)");continue;}
     if(entries.Count==0)continue;   // backup from before the log, no recreate seen since: the pre-log behaviour
     var last=entries[entries.Count-1];string now=Sha1(Full(a));
     if(now!=last.hash&&now!=backupHash)stale.Add(a+" (changed since the last logged "+last.kind+": the edit would be lost)");
    }
    if(stale.Count>0)return "REFUSED revert would copy an older generation over "+string.Join(", ",stale)+"; check the backups in "+backups+" and use revert:force to restore them anyway";
   }
   foreach(var a in present){File.Copy(Path.Combine(backups,Path.GetFileName(a)),Full(a),true);AssetDatabase.ImportAsset(a,ImportAssetOptions.ForceUpdate);}
   Log(backups,present,"revert");
   return "REVERTED "+string.Join(", ",present)+" from "+backups+(force?" (forced)":"")+" (backups kept)";
  }
  // generation log: one line per backup / write / revert / recreate (asset path, kind, SHA1 of the file on disk afterwards, time, GUID)
  static string Sha1(string file){using var sha=SHA1.Create();using var s=File.OpenRead(file);return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","");}
  static void Log(string backups,string[] assets,string kind)
  {
   Directory.CreateDirectory(backups);
   File.AppendAllLines(Path.Combine(backups,"generations.txt"),assets.Select(a=>a+"\t"+kind+"\t"+Sha1(Full(a))+"\t"+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\t"+AssetDatabase.AssetPathToGUID(a)));
  }
  static List<(string kind,string hash,string guid)> Logged(string backups,string asset)
  {
   var log=Path.Combine(backups,"generations.txt");var list=new List<(string kind,string hash,string guid)>();if(!File.Exists(log))return list;
   foreach(var line in File.ReadAllLines(log)){var f=line.Split('\t');if(f.Length>=3&&f[0]==asset)list.Add((f[1],f[2],f.Length>=5?f[4]:""));}
   return list;
  }
  // the last 'backup' entry matching the backup file, -1 = none (a backup from before the log)
  static int BackupEntry(List<(string kind,string hash,string guid)> entries,string backupHash){for(int i=entries.Count-1;i>=0;i--)if(entries[i].kind=="backup"&&entries[i].hash==backupHash)return i;return -1;}
  // null = same generation as the backup at entries[at] (-1: a pre-log backup, every entry is later); else why not: a logged recreate
  // after it, or a GUID other than the current one from the backup on (DeleteAsset + CreateAsset gives a new GUID even when unlogged)
  static string Recreated(List<(string kind,string hash,string guid)> entries,int at,string guidNow)
  {
   for(int i=Math.Max(at,0);i<entries.Count;i++)
   {
    if(i>at&&entries[i].kind=="recreate")return "re-created by main-recreate";
    if(entries[i].guid.Length>0&&!string.IsNullOrEmpty(guidNow)&&entries[i].guid!=guidNow)return "re-created (GUID changed)";
   }
   return null;
  }
  // CompactFinish297 MainCreate297 calls this after re-cloning a Main asset: the next apply / services backs the new generation up and
  // revert refuses the older one. Nothing is logged while the migration has never written (no backup folder).
  internal static void NoteRecreated306(string assetPath)
  {
   string backups=BackupDir();if(!Directory.Exists(backups)||!File.Exists(Full(assetPath)))return;
   Log(backups,new[]{assetPath},"recreate");
  }

  // ---------- planning (rows carry the write; dry never runs them) ----------
  static void Plan(WorldMacroPlaytestSO content,DemoCampaignProfile campaign,List<Row> rows)
  {
   var points=(content.Points??Array.Empty<PrologueContentSO.Point>()).Where(p=>p!=null&&!string.IsNullOrEmpty(p.Id)).ToArray();
   var planned=new Dictionary<string,string>();   // point id -> speaker after this run
   foreach(var p in points)planned[p.Id]=(p.Speaker??"").Trim();
   // 1) point texts and lines
   foreach(var p in points)
   {
    var point=p;
    Prefix(rows,"content",p.Id,"Text",p.Text,planned,p.Id,v=>point.Text=v,s=>point.Speaker=s);
    for(int i=0;i<(p.Lines?.Length??0);i++){int k=i;Prefix(rows,"content",p.Id,"Lines["+i+"]",p.Lines[i],planned,p.Id,v=>point.Lines[k]=v,s=>point.Speaker=s);}
   }
   // 2) commissions: the speaker belongs to the commission (the giver point keeps its own)
   foreach(var q in content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>())
   {
    if(q==null||string.IsNullOrEmpty(q.Id))continue;var spec=q;string key="commission:"+q.Id;planned[key]=(q.Speaker??"").Trim();
    Prefix(rows,"content",q.Id,"OfferText",q.OfferText,planned,key,v=>spec.OfferText=v,s=>spec.Speaker=s);
    Prefix(rows,"content",q.Id,"WaitingText",q.WaitingText,planned,key,v=>spec.WaitingText=v,s=>spec.Speaker=s);
    Prefix(rows,"content",q.Id,"ReportText",q.ReportText,planned,key,v=>spec.ReportText=v,s=>spec.Speaker=s);
    Prefix(rows,"content",q.Id,"CompletedText",q.CompletedText,planned,key,v=>spec.CompletedText=v,s=>spec.Speaker=s);
    foreach(var (name,lines) in new[]{("OfferLines",q.OfferLines),("WaitingLines",q.WaitingLines),("ReportLines",q.ReportLines),("CompletedLines",q.CompletedLines),("AcceptedLines",q.AcceptedLines),("DeclinedLines",q.DeclinedLines)})
     for(int i=0;i<(lines?.Length??0);i++){int k=i;var arr=lines;Prefix(rows,"content",q.Id,name+"["+i+"]",arr[i],planned,key,v=>arr[k]=v,s=>spec.Speaker=s);}
    if(planned[key].Length==0)rows.Add(new Row{Asset="content",Id=q.Id,Field="Speaker",Speaker="",Source="MISSING",Preview=Preview(q.OfferText)});
   }
   // 3) campaign texts: the speaker lands on the Main point of the trigger id
   if(campaign!=null)
   {
    foreach(var s in campaign.Stages??Array.Empty<DemoCampaignProfile.Stage>())
    {
     if(s==null)continue;var stage=s;var point=Array.Find(points,p=>p.Id==s.TriggerId);
     Prefix(rows,"campaign",s.TriggerId,"Stage "+s.Id+".Dialogue",s.Dialogue,planned,point?.Id,v=>stage.Dialogue=v,point!=null?sp=>point.Speaker=sp:(Action<string>)null);
    }
    var testimonies=campaign.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>();
    for(int i=0;i<testimonies.Length;i++)
    {
     var t=testimonies[i];if(t==null)continue;var point=Array.Find(points,p=>p.Id==t.TriggerId);
     Prefix(rows,"campaign",t.TriggerId,"Testimony["+i+"].Text",t.Text,planned,point?.Id,v=>t.Text=v,point!=null?sp=>point.Speaker=sp:(Action<string>)null);
    }
   }
   // 4) conversation points still without a speaker: the names the code used, else the prompt's name (the runtime fallback), else reported
   foreach(var p in points)
   {
    if(p.Kind!=PrologueInteractionKind.Conversation||planned[p.Id].Length>0)continue;
    var known=Array.Find(Known,k=>k[0]==p.Id);var point=p;string fromPrompt=WorldMacroPlaytestSession.PromptName306(p.Prompt);
    if(known!=null||fromPrompt.Length>0){string name=known!=null?known[1]:fromPrompt;planned[p.Id]=name;
     rows.Add(new Row{Asset="content",Id=p.Id,Field="Speaker",Speaker=name,Source=known!=null?"id":"prompt",Preview=Preview(p.Text),Write=()=>point.Speaker=name});}
    else if(!(content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).Any(q=>q!=null&&q.GiverId==p.Id))
     rows.Add(new Row{Asset="content",Id=p.Id,Field="Speaker",Speaker="",Source="MISSING",Preview=Preview(p.Text)});
   }
  }
  // one text: a prefix naming the planned speaker (or the first one when none is planned) is moved; another name is a conflict and stays
  static void Prefix(List<Row> rows,string asset,string id,string field,string text,Dictionary<string,string> planned,string key,Action<string> setText,Action<string> setSpeaker)
  {
   if(!WorldMacroPlaytestSession.SplitSpeaker306(text,out var name,out var body))return;
   if(key==null||setSpeaker==null){rows.Add(new Row{Asset=asset,Id=id,Field=field,Speaker=name,Source="NO-POINT (kept)",Preview=Preview(text)});return;}
   string current=planned.TryGetValue(key,out var s)?s:"";
   if(current.Length>0&&current!=name){rows.Add(new Row{Asset=asset,Id=id,Field=field,Speaker=current,Source="CONFLICT '"+name+"' (kept)",Preview=Preview(text)});return;}
   bool set=current.Length==0;planned[key]=name;
   rows.Add(new Row{Asset=asset,Id=id,Field=field,Speaker=name,Source=set?"prefix -> Speaker":"prefix (Speaker already)",Preview=Preview(body),
    Write=()=>{setText(body);if(set)setSpeaker(name);}});
  }
  static string Preview(string text){string t=(text??"").Replace("\r","").Replace('\n',' ');return t.Length>20?t.Substring(0,20):t;}
  static string Table(List<Row> rows,string campaignPath)
  {
   var sb=new StringBuilder();
   sb.Append("content = ").Append(ContentPath).Append("\ncampaign = ").Append(string.IsNullOrEmpty(campaignPath)?"(none)":campaignPath).Append(" (shared with W_Demo_Compact)\n");
   sb.Append("asset | point id | field | speaker | source | first 20 chars\n");
   foreach(var r in rows)sb.Append(r.Asset).Append(" | ").Append(r.Id).Append(" | ").Append(r.Field).Append(" | ").Append(r.Speaker).Append(" | ").Append(r.Source).Append(" | ").Append(r.Preview).Append('\n');
   sb.Append(rows.Count(r=>r.Write!=null)).Append(" writes, ").Append(rows.Count(r=>r.Source=="MISSING")).Append(" without speaker, ")
     .Append(rows.Count(r=>r.Source.StartsWith("CONFLICT",StringComparison.Ordinal)||r.Source.StartsWith("NO-POINT",StringComparison.Ordinal))).Append(" kept prefixes");
   return sb.ToString();
  }
 }
}
