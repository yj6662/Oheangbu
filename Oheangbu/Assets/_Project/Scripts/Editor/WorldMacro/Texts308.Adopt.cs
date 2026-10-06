using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Oheangbu.App.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 ledger recovery (Adopt308.cs): Texts308 "scene-adopt:<scene>".
 // Without the scene ledger a changed prop / body makes scene-apply refuse ("InteractionVisuals[id] exists and was not added by
 // this tool": the proof is a live visual-add row) and scene-revert finds no ledger. scene-adopt writes the rows scene-apply would
 // have written on a scene that held none of this tool's objects: create (the root, every holder / body that stands under it)
 // and visual-add (the entry of that id) - only when the tool's own dry pass (ScenePlan, the one scene-dry prints) has nothing to
 // write and nothing stands under the root that the data does not name. Re-seat and note rows of earlier passes are not written:
 // they were about objects a create row removes. The campaign / content ledgers hold before-forms of assets (stage fields,
 // a keeper's rows): those are original unknown and are NOT adopted - their apply opens a new ledger by itself.
 // "Nothing to write" is not enough by itself: the dry pass writes no row for a body it could not compare either (no ground under
 // its point: "WARN ...; skipped"). An object is adopted only when the pass printed its own "ok <root>/<id> at ..." line, and a
 // WARN that ends "; skipped" / "; left as copied" (a body or a child the pass did not compare) refuses the whole command.
 public static partial class Texts308
 {
  // the adopted mark holds while a live row still sits in the write an adopt made (Adopt308.UnknownBackup)
  static string AdoptedLive(Ledger l)=>l!=null&&Adopt308.Is(l.adopted)&&l.writes.Any(w=>w!=null&&w.rows!=null&&Adopt308.UnknownBackup(w.backup)&&w.rows.Any(r=>r!=null&&!r.reverted))?l.adopted:null;
  static string AdoptNote(string sceneArg)
  {
   try{var cfg=Load();return Adopt308.RevertNote(AdoptedLive(ReadLedger("scene_"+TargetOf(cfg,sceneArg).Alias)));}
   catch(Refuse){return "";}   // the revert itself says what is wrong
  }

  static string SceneAdopt(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);string key="scene_"+t.Alias,file=LedgerFile(key);
   string head="Texts308 scene-adopt "+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n";
   if(File.Exists(file))
   {
    var old=ReadLedger(key);int live=Live(old).Count();
    if(old!=null&&Adopt308.Is(old.adopted))return head+"  "+Adopt308.Already(file,old.adopted,live);
    throw new Refuse(Adopt308.Taken(file,live));
   }
   var k=Begin(cfg,t,true);   // the dry pass of scene-dry: opens the scene, changes nothing
   ScenePlan(k);              // a refusal of the plan (an InteractionVisuals entry of another writer, a missing prefab) is the answer
   if(k.Rows.Count>0)throw new Refuse(Adopt308.NotEqual(t.Scene,k.Rows.Select(r=>"scene-dry would ["+r.op+"]: "+r.detail).ToList(),4));
   // a body / a child the pass left uncompared (no ground under it, a child gone from the clone): no row, and not "equal" either
   var blind=k.Lines.Where(l=>l.StartsWith("WARN ",StringComparison.Ordinal)&&(l.EndsWith("; skipped",StringComparison.Ordinal)||l.EndsWith("; left as copied",StringComparison.Ordinal))).Select(l=>l.Substring(5)).ToList();
   if(blind.Count>0)throw new Refuse(Adopt308.NotCompared(t.Scene,blind,4));
   var root=FindRoot(k.Scene,cfg.RootName);
   if(root==null)return head+"  nothing to adopt: "+cfg.RootName+" is not in "+t.Scene+" (scene-apply opens its own ledger). Nothing written";
   var rows=new List<Row>();var mine=new HashSet<Transform>();var bad=new List<string>();
   var visuals=k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>();
   // an element the plan handles in this scene (its source object resolves, its point is in the content) and that stands under the root
   void Take(string op,string id,string sourceKey,string what)
   {
    if(Resolved(k.Scene,sourceKey)==null||Point(k.Content,id)==null)return;
    var obj=root.Find(id);if(obj==null)return;
    mine.Add(obj);
    // only an object the dry pass compared and found in place: its own line "ok <root>/<id> at ..." (ScenePlan)
    if(!k.Lines.Any(l=>l.StartsWith("ok "+cfg.RootName+"/"+id+" at ",StringComparison.Ordinal))){bad.Add(cfg.RootName+"/"+id+" stands under the root, but scene-dry did not compare its pose with the data (no line 'ok "+cfg.RootName+"/"+id+" at ...')");return;}
    if(rows.Count==0)rows.Add(new Row{op=op,kind="create",key=BuildingAudit308.KeyOf(root),detail="create root "+cfg.RootName+" (adopted)"});
    rows.Add(new Row{op=op,kind="create",id=id,key=BuildingAudit308.KeyOf(obj),detail="create "+cfg.RootName+"/"+id+" (adopted: "+what+" "+sourceKey+", stands at "+V(obj.position)+")"});
    // the entry of this id: the pass found it with the renderer set the data gives ("ok InteractionVisuals[id]"; anything else is
    // a row or a refusal above). A person's set is the clone's own renderers (under the root); a board's are the board's - an
    // object outside the root, so "all under the root" (RoadInn308's rule) cannot hold there: the id is this tool's own point
    var vis=Array.Find(visuals,v=>v!=null&&v.Id==id);
    if(vis!=null)rows.Add(new Row{op=op,kind="visual-add",id=id,detail="InteractionVisuals["+id+"] += "+(vis.Renderers??Array.Empty<Renderer>()).Count(x=>x!=null)+" renderer(s) (adopted)"});
   }
   foreach(var b in Arr(cfg.J,"boards"))Take(Str(b,"op"),Str(b,"id"),Str(b,"object_key"),"the holder of board");
   foreach(var n in Arr(cfg.J,"people"))Take(Str(n,"op"),Str(n,"id"),Str(n,"source_key"),"a clone of");
   foreach(Transform c in root)if(!mine.Contains(c))bad.Add(cfg.RootName+"/"+c.name+" stands under the root and no board / person of the data builds it in this scene");
   if(bad.Count>0)throw new Refuse(Adopt308.NotEqual(t.Scene,bad,6));
   if(rows.Count==0)return head+"  nothing to adopt: "+cfg.RootName+" holds no object of the data in "+t.Scene+". Nothing written";
   if(k.Scene.isDirty)throw new Refuse("the scene is dirty after a read-only pass (bug): reload it. Nothing written");
   string sha=Harness303.Sha(Harness303.Abs(t.Scene)),utc=Adopt308.Utc();
   var ledger=new Ledger{group=cfg.Group,target=t.Scene,created=DateTime.UtcNow.ToString("O"),adopted=Adopt308.Stamp(utc,sha)};
   ledger.writes.Add(new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Adopt308.NoBackup,shaBefore="",shaAfter=sha,dataSha=cfg.Sha,rows=rows});
   WriteLedger(key,ledger);
   return head+"  scene-dry has nothing to write (WARN "+k.Warns+"): "+mine.Count+" object(s) under "+cfg.RootName+"\n"
    +"  wrote "+rows.Count+" row(s) ("+rows.Count(r=>r.kind=="create")+" create, "+rows.Count(r=>r.kind=="visual-add")+" visual-add); "+ledger.adopted+"\n"
    +"  "+Adopt308.Unknown+": the scene bytes and sha before the first apply, re-seat / note rows of earlier passes (scene-revert removes these objects and their InteractionVisuals entries and restores nothing)\n"
    +"  ledger "+file+" group "+cfg.Group+" (the scene was not changed)";
  }
 }
}
