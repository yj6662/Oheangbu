using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Oheangbu.App.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 ledger recovery (Adopt308.cs): RoadInn308 "scene-adopt:<scene>".
 // scene-apply works from the scene, so it runs without a ledger - but the ledger it then opens holds only what THAT call changed
 // (a re-seat row): scene-revert would put a pose back and leave the lantern standing. scene-adopt writes the rows scene-apply
 // would have written on a scene without this tool's objects: create (root, holder, lantern) and visual-add (when the entry of
 // the rest id names only renderers under the root) - only when the tool's own dry pass (ScenePlan) has nothing to write and
 // nothing else stands under the root / the holder. Re-seat and note rows of earlier passes are not written (they were about the
 // lantern a create row removes). The content and seal ledgers hold before-forms of assets: original unknown, NOT adopted.
 // Compiles against RoadInn308.Apply.cs as it is and as world bundle 4 stages it (the names used here are in both).
 public static partial class RoadInn308
 {
  // the adopted mark (status) and the revert note hold while a live row still sits in the write an adopt made
  // (Adopt308.UnknownBackup): after a revert of those rows and a real apply the ledger is a plain one again
  static string AdoptedLive(Ledger l)=>l!=null&&Adopt308.Is(l.adopted)&&l.writes.Any(w=>w!=null&&w.rows!=null&&Adopt308.UnknownBackup(w.backup)&&w.rows.Any(r=>r!=null&&!r.reverted))?l.adopted:null;
  static string AdoptNote(string sceneArg)
  {
   try{var cfg=Load();return Adopt308.RevertNote(AdoptedLive(ReadLedger("scene_"+Key(TargetOf(cfg,sceneArg)))));}
   catch(Refuse){return "";}   // the revert itself says what is wrong
  }

  static string SceneAdopt(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);string key="scene_"+Key(t),file=LedgerFile(key);
   string head="RoadInn308 scene-adopt "+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+"\n";
   if(File.Exists(file))
   {
    var old=ReadLedger(key);int live=Live(old).Count();
    if(old!=null&&Adopt308.Is(old.adopted))return head+"  "+Adopt308.Already(file,old.adopted,live);
    throw new Refuse(Adopt308.Taken(file,live));
   }
   var k=Begin(cfg,t,true);   // the dry pass of dry:<scene>: opens the scene, changes nothing
   string skipWhy=SkipReason(cfg,k.Scene,k.Content);
   if(skipWhy!=null)return head+"  nothing to adopt: the inn is not in this scene ("+skipWhy+"). Nothing written";
   var root=Root(k.Scene,cfg.RootName);
   if(root==null)return head+"  nothing to adopt: "+cfg.RootName+" is not in "+t.Scene+" (scene-apply opens its own ledger). Nothing written";
   ScenePlan(k);
   if(k.Rows.Count>0)throw new Refuse(Adopt308.NotEqual(t.Scene,k.Rows.Select(r=>"scene-dry would: "+r.detail).ToList(),4));
   string id=cfg.RestId,name=Str(cfg.Lantern,"name");var holder=root.Find(id);var lantern=holder!=null?holder.Find(name):null;var bad=new List<string>();
   if(holder==null||lantern==null)bad.Add(cfg.RootName+"/"+id+"/"+name+" is not in the scene");
   foreach(Transform c in root)if(c!=holder)bad.Add(cfg.RootName+"/"+c.name+" stands under the root and the data does not build it");
   if(holder!=null)foreach(Transform c in holder)if(c!=lantern)bad.Add(cfg.RootName+"/"+id+"/"+c.name+" stands under the holder and the data does not build it");
   if(bad.Count>0)throw new Refuse(Adopt308.NotEqual(t.Scene,bad,6));
   var rows=new List<Row>
   {
    new Row{op="XI3",kind="create",key=BuildingAudit308.KeyOf(root),detail="create root "+cfg.RootName+" (adopted)"},
    new Row{op="XI3",kind="create",key=BuildingAudit308.KeyOf(holder),detail="create "+cfg.RootName+"/"+id+" (adopted: stands at "+V(holder.position)+")"},
    new Row{op="XI3",kind="create",key=BuildingAudit308.KeyOf(lantern),detail="create "+cfg.RootName+"/"+id+"/"+name+" (adopted: stands at "+V(lantern.position)+")"},
   };
   // the entry of the rest id is this tool's only when every renderer it names stands under the root (the rule Undo uses)
   var vis=Array.Find(k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>(),v=>v!=null&&v.Id==id);
   bool ours=vis!=null&&(vis.Renderers??Array.Empty<Renderer>()).All(x=>x==null||Own(cfg,x.transform));
   if(ours)rows.Add(new Row{op="XI3",kind="visual-add",id=id,detail="InteractionVisuals["+id+"] += "+(vis.Renderers??Array.Empty<Renderer>()).Count(x=>x!=null)+" renderer(s) (adopted)"});
   if(k.Scene.isDirty)throw new Refuse("the scene is dirty after a read-only pass (bug): reload it. Nothing written");
   string sha=Harness303.Sha(Harness303.Abs(t.Scene)),utc=Adopt308.Utc();
   var ledger=new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O"),adopted=Adopt308.Stamp(utc,sha)};
   ledger.writes.Add(new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Adopt308.NoBackup,shaBefore="",shaAfter=sha,dataSha=cfg.Sha,rows=rows});
   WriteLedger(key,ledger);
   return head+"  scene-dry has nothing to write (WARN "+k.Warns+"): the lantern stands at "+V(lantern.position)+"\n"
    +"  wrote "+rows.Count+" row(s) (3 create, "+(ours?"1":"0")+" visual-add"+(vis!=null&&!ours?" - the InteractionVisuals entry names renderers outside the root: another writer's, not adopted":"")+"); "+ledger.adopted+"\n"
    +"  "+Adopt308.Unknown+": the scene bytes and sha before the first apply, re-seat / note rows of earlier passes (scene-revert removes the lantern, its holder and its InteractionVisuals entry and restores nothing)\n"
    +"  ledger "+file+" (the scene was not changed)";
  }
 }
}
