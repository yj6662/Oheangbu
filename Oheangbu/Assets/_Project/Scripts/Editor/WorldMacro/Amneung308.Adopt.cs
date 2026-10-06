using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 ledger recovery (Adopt308.cs): Amneung308 "adopt:<scene>".
 // Without its ledgers the band is a dead end: apply refuses ("a root object named Amneung308 already exists without an applied
 // ledger"), revert and rocks-revert find no ledger, rocks-apply asks for an applied base ledger, and verify judges the rocks
 // against the BASE set while the look rocks stand (LookActive reads the rocks ledger). adopt:<scene> writes
 //   ledger_amneung308_<scene>.json         the base ledger apply would have written (rock, core, stone, pad, site, root rows)
 //   ledger_amneung308_rocks_<scene>.json   when the holder carries the look rocks: the rocks ledger rocks-apply would have written
 // only when (1) the holder carries exactly the base set or exactly the look set, (2) the identity lines of the tool's own verify
 // pass (Judge: one root; rocks / cores / stone / pads / site as the data builds them; no light, glow, text or script) and (3)
 // nothing stands under the root that apply does not build. Lines of verify that measure the ground, the NavMesh, the trees or
 // the 국 place count are printed and do not decide: a ledger is about what was built, not about the ground under it.
 // Not observed, so written as such: the 25 base rocks while the look rocks stand (their rows carry the key apply gave them and
 // "not observed"; rocks-revert rebuilds them from amneung308.json - the look data pins that file's sha, checked here), the scene
 // bytes before the first apply (sceneBackup = "original unknown"), the trees veg-apply cleared (BuildingFix308's own ledger).
 // Two files: the rocks ledger is written FIRST, the base ledger second. A call cut between them leaves the rocks ledger alone
 // (verify then still judges the look rocks: LookActive) - and the next adopt says "half an adopt", never "변경 없음".
 public static partial class Amneung308
 {
  const string NotObserved="not observed";
  // the adopted mark (status) and the revert note hold while that ledger is the applied one (apply / rocks-apply write a new ledger)
  static string AdoptedLive(Ledger l)=>l!=null&&l.state=="applied"&&Adopt308.Is(l.adopted)?l.adopted:null;

  static string Adopt(string scenePath)
  {
   PostLedger308.RequireEditable();var d=Load();string alias=PostLedger308.Short(scenePath);
   string baseFile=LedgerFile(alias),lookFile=LookLedgerFile(alias);var b0=ReadLedger(alias);var l0=ReadLookLedger(alias);
   string head="Amneung308 adopt "+scenePath+"\n  data sha "+Short(d.Sha)+" version "+d.version+", group "+d.group+"\n";
   bool bA=b0!=null&&Adopt308.Is(b0.adopted),lA=l0!=null&&Adopt308.Is(l0.adopted),baseOnly=bA&&l0==null;
   string already=head+"  "+Adopt308.Already(baseFile,bA?b0.adopted:"",bA?b0.rows.Count:0)+(l0!=null?" + "+lookFile+" ("+l0.rows.Count+" row(s))":"");
   if(File.Exists(baseFile)||File.Exists(lookFile))
   {
    // one file of a two-file adopt is never "변경 없음"
    if(lA&&b0==null)throw new Refuse("half an adopt: the rocks ledger "+lookFile+" is an adopted one and the base ledger "+baseFile+" is not there (a call was cut between the two files, or the file was moved aside) - move the rocks ledger aside and run adopt:"+alias+" again. Nothing written");
    if(bA&&lA)return already;
    if(!baseOnly)throw new Refuse(Adopt308.Taken(b0!=null&&!bA?baseFile:l0!=null&&!lA?lookFile:File.Exists(baseFile)?baseFile:lookFile,(b0!=null?b0.rows.Count:0)+(l0!=null?l0.rows.Count:0)));
    // the adopted base ledger alone: "변경 없음" - unless the look rocks stand in the holder (seen below, in the opened scene)
   }
   var scene=PostLedger308.Open(scenePath);int rootCount;var rootGo=RootOf(scene,d.root,out rootCount);
   if(rootCount==0)return baseOnly?already:head+"  nothing to adopt: "+d.root+" is not in "+scenePath+" (apply builds it and opens its own ledger). Nothing written";
   if(rootCount>1)throw new Refuse(rootCount+" root objects named "+d.root+": which one is this tool's cannot be told. Nothing written");
   var root=rootGo.transform;if(PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+d.root+" is a protected tree name");
   Transform rocksT=root.Find("Rocks"),coresT=root.Find("Core"),stoneT=root.Find("Stone");
   if(rocksT==null||coresT==null||stoneT==null)throw new Refuse(d.root+" does not hold the three groups apply builds (Rocks / Core / Stone). Nothing written");
   // which rock set stands in the holder: the base set of amneung308.json, or the look set of amneung308_look.json
   var names=new HashSet<string>();foreach(Transform t in rocksT)names.Add(t.name);
   bool isBase=rocksT.childCount==d.rocks.Length&&d.rocks.All(r=>names.Contains(r.id));LookD look=null;
   if(!isBase)
   {
    look=LoadLook();
    bool isLook=look.root==d.root&&root.Find(look.holder)==rocksT&&rocksT.childCount==look.rocks.Length&&look.rocks.All(r=>names.Contains(r.id));
    if(!isLook)throw new Refuse(d.root+"/Rocks holds "+rocksT.childCount+" object(s): neither the "+d.rocks.Length+" base rocks nor the "+look.rocks.Length+" look rocks of the present data. Nothing written");
    if(!string.Equals(d.Sha,look.@base.sha256,StringComparison.OrdinalIgnoreCase)||d.rocks.Length!=look.@base.rocks)
     throw new Refuse("the look rocks stand, but the deployed amneung308.json (sha "+Short(d.Sha)+", "+d.rocks.Length+" rocks) is not the base the look data was derived from ("+Short(look.@base.sha256)+", "+look.@base.rocks+" rocks): the base rocks a rocks-revert would rebuild cannot be named. Nothing written");
   }
   if(baseOnly)
   {
    if(look!=null)throw new Refuse("half an adopt: the base ledger "+baseFile+" is an adopted one, the look rocks stand in "+d.root+"/"+look.holder+" and the rocks ledger "+lookFile+" is not there (moved aside, or a call was cut) - move the base ledger aside and run adopt:"+alias+" again. Nothing written");
    return already;
   }
   var k=Judge(scenePath,alias,d,scene,look!=null);   // the tool's own verify lines; the rock set is the one that stands
   var fails=k.Text.ToString().Split('\n').Where(x=>x.StartsWith("FAIL ",StringComparison.Ordinal)).Select(x=>x.TrimEnd()).ToList();
   if(k.IdFail>0)throw new Refuse(scenePath+" is not what the present data builds: "+k.IdFail+" identity line(s) of verify FAIL (of "+k.Fail+" FAIL)\n  "+string.Join("\n  ",fails)+"\n  no ledger is written for a scene the data does not describe. Nothing written");
   // nothing else under the root: an object apply does not build is not a row of its ledger
   var extra=new List<string>();var stoneNames=new HashSet<string>(d.stone.parts.Select(x=>x.name).Concat(d.stone.pads.Select(x=>x.name)));
   if(d.stone.site.enabled)stoneNames.Add(d.stone.site.name);
   foreach(Transform g in root)if(g!=rocksT&&g!=coresT&&g!=stoneT)extra.Add(d.root+"/"+g.name);
   foreach(Transform t in coresT)if(!d.cores.Any(c=>c.id==t.name)||coresT.Find(t.name)!=t)extra.Add(d.root+"/Core/"+t.name);
   foreach(Transform t in stoneT)if(!stoneNames.Contains(t.name)||stoneT.Find(t.name)!=t)extra.Add(d.root+"/Stone/"+t.name);
   if(extra.Count>0)throw new Refuse(Adopt308.NotEqual(scenePath,extra.Select(x=>x+" stands under the root and apply does not build it").ToList(),6));
   if(scene.isDirty)throw new Refuse("the scene is dirty after a read-only verify (bug): reload it. Nothing written");
   string sha=Harness303.Sha(Harness303.Abs(scenePath)),utc=Adopt308.Utc(),stamp=Adopt308.Stamp(utc,sha),when=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",IC);
   var bl=new Ledger{scene=scenePath,state="applied",utc=when,dataSha=d.Sha,dataVersion=d.version,root=d.root,sceneBackup=Adopt308.NoBackup,sceneShaBefore="",sceneShaAfter=sha,adopted=stamp};
   bl.notes.Add(stamp);
   void Log(Ledger l,string group,string kind,Transform t)=>l.rows.Add(new Row{group=group,kind=kind,key=BuildingAudit308.KeyOf(t),before="",after=Trs(t)});
   string rocksKey=BuildingAudit308.KeyOf(rocksT);
   if(look==null)foreach(var r in d.rocks)Log(bl,d.group,"rock",rocksT.Find(r.id));
   else
   {
    // the base rocks are not in the scene: the key is the one apply gave (ids are unique under one holder), the pose is the data's
    foreach(var r in d.rocks)bl.rows.Add(new Row{group=d.group,kind="rock",key=rocksKey+"/"+r.id,before="",after=NotObserved+" (the look rocks stand in the holder); the data puts it at "+Harness303.V(new Vector3(r.x,r.y,r.z))+" yaw "+F(r.yaw,"F1")});
    bl.notes.Add("the "+d.rocks.Length+" rock rows were not observed: the look rocks stand in "+rocksKey+" (rocks ledger "+Path.GetFileName(lookFile)+")");
   }
   foreach(var c in d.cores)Log(bl,d.group,"core",coresT.Find(c.id));
   foreach(var p in d.stone.parts){var t=stoneT.Find(p.name);if(t!=null)Log(bl,d.group,"stone",t);else bl.notes.Add("SKIP stone part "+p.name+": not in this scene (seen at adopt)");}
   foreach(var p in d.stone.pads){var t=stoneT.Find(p.name);if(t!=null)Log(bl,d.group,"pad",t);else bl.notes.Add("SKIP pad "+p.name+": not in this scene (seen at adopt)");}
   if(d.stone.site.enabled){var t=stoneT.Find(d.stone.site.name);if(t!=null)Log(bl,d.group,"site",t);else bl.notes.Add("SKIP site "+d.stone.site.name+": not in this scene (seen at adopt)");}
   Log(bl,d.group,"root",root);
   foreach(var f in fails)bl.notes.Add("verify at adopt (not an identity line): "+f);
   Ledger ll=null;
   if(look!=null)
   {
    ll=new Ledger{kind="amneung308-rocks",scene=scenePath,state="applied",utc=when,dataSha=look.Sha,dataVersion=look.version,root=d.root+"/"+look.holder,sceneBackup=Adopt308.NoBackup,sceneShaBefore="",sceneShaAfter=sha,adopted=stamp};
    ll.notes.Add("base data "+d.Sha);ll.notes.Add("frozen digest (Core + Stone) "+FrozenDigest(root));ll.notes.Add(stamp);
    ll.notes.Add("rock-removed rows: "+Adopt308.Unknown+" as observed poses - rocks-revert rebuilds the "+d.rocks.Length+" base rocks from amneung308.json "+Short(d.Sha)+" (= base.sha256 of the look data)");
    foreach(var r in d.rocks)ll.rows.Add(new Row{group=look.group,kind="rock-removed",key=rocksKey+"/"+r.id,before=Adopt308.Unknown+" (not observed)",after=""});
    foreach(var r in look.rocks){var t=rocksT.Find(r.id);ll.rows.Add(new Row{group=look.group,kind="rock",key=BuildingAudit308.KeyOf(t),before="",after=Trs(t)+"|"+r.mesh});}
   }
   // the rocks ledger first: if the second write fails, verify still judges the rocks that stand (LookActive reads this file)
   if(ll!=null)WriteLookLedger(alias,ll);
   try{WriteLedger(alias,bl);}
   catch(Exception e)when(ll!=null){throw new Refuse("the rocks ledger "+lookFile+" was written, the base ledger was not ("+e.Message+"): move the rocks ledger aside and run adopt:"+alias+" again");}
   var sb=new StringBuilder(head);
   sb.AppendLine("  verify: PASS "+k.Pass+" / FAIL "+k.Fail+" / INFO "+k.Info+" - identity lines FAIL 0 ("+(look!=null?"the "+look.rocks.Length+" look rocks":"the "+d.rocks.Length+" base rocks")+" stand in the holder)");
   foreach(var f in fails)sb.AppendLine("  note (not an identity line, written into the ledger notes): "+f);
   sb.AppendLine("  wrote the base ledger: "+bl.rows.Count+" row(s) ("+string.Join(", ",bl.rows.GroupBy(r=>r.kind).Select(g=>g.Key+" "+g.Count()))+")"+(look!=null?" - the rock rows are 'not observed'":"")+"; "+stamp);
   if(ll!=null)sb.AppendLine("  wrote the rocks ledger: "+ll.rows.Count+" row(s) ("+string.Join(", ",ll.rows.GroupBy(r=>r.kind).Select(g=>g.Key+" "+g.Count()))+"), base data "+Short(d.Sha)+", look data "+Short(look.Sha));
   sb.AppendLine("  "+Adopt308.Unknown+": the scene bytes and sha before the first apply (revert removes the root, rocks-revert swaps the rock sets; neither restores anything else); the trees veg-apply cleared are BuildingFix308's ledger, not this one");
   return sb.Append("  ledger "+(ll!=null?lookFile+" + ":"")+baseFile+" (the scene was not changed)").ToString();
  }
 }
}
