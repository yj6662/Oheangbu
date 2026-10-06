using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 ledger recovery (Adopt308.cs): Places308 "scene-adopt:<scene>" and "assets-adopt".
 // Without a ledger scene-apply answers "변경 없음" while the data is the same, refuses "scene-revert first" when a row changed, and
 // scene-revert finds no row: a dead end. scene-adopt writes ledger_places308_scene_<alias>.json = the rows scene-apply would have
 // written (create-root, create-place, create) for the objects that stand under the root - only when every enabled row of the
 // data stands there as Build / Same say (the comparison plan and check use) and nothing else stands under the root.
 // assets-adopt does the same for the mesh assets: a "mesh" row for an asset whose geometry equals what its json builds. Folder
 // rows are not adopted (which folders the first assets-apply made is original unknown): assets-revert then leaves the folders.
 public static partial class Places308
 {
  // the adopted mark (status) and the revert note hold while a live row still sits in the write an adopt made
  // (Adopt308.UnknownBackup): after a revert of those rows and a real apply the ledger is a plain one again
  static string AdoptedLive(Ledger l)=>l!=null&&Adopt308.Is(l.adopted)&&l.writes.Any(w=>w!=null&&w.rows!=null&&Adopt308.UnknownBackup(w.backup)&&w.rows.Any(r=>r!=null&&!r.reverted))?l.adopted:null;
  static string AdoptNote(string arg)
  {
   try{var cfg=Load();return Adopt308.RevertNote(AdoptedLive(ReadLedger("scene_"+TargetOf(cfg,arg).Alias)));}
   catch(Refuse){return "";}   // the revert itself says what is wrong
  }

  static string SceneAdopt(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);string key="scene_"+t.Alias,file=LedgerFile(key);
   string head="Places308 scene-adopt "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n";
   if(File.Exists(file))
   {
    var old=ReadLedger(key);int live=Live(old).Count();
    if(old!=null&&Adopt308.Is(old.adopted))return head+"  "+Adopt308.Already(file,old.adopted,live);
    throw new Refuse(Adopt308.Taken(file,live));
   }
   var scene=Open(t);Physics.SyncTransforms();
   int roots=scene.GetRootGameObjects().Count(g=>g.name==cfg.Root&&!Preview(g.transform));
   if(roots==0)return head+"  nothing to adopt: "+cfg.Root+" is not in "+scene.path+" (scene-apply opens its own ledger). Nothing written";
   if(roots>1)throw new Refuse(roots+" roots named "+cfg.Root+" in "+scene.path+": which one is this tool's cannot be told. Nothing written");
   var root=Root(cfg,scene);if(PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   var rows=new List<Row>{new Row{kind="create-root",key=BuildingAudit308.KeyOf(root),detail="root"}};
   var bad=new List<string>();var holders=new HashSet<Transform>();var mine=new HashSet<Transform>();int solid=0;
   foreach(var place in EnabledPlaces(cfg))foreach(var r in Arr(place,"rows"))
   {
    var it=Build(cfg,place,r);var have=root.Find(it.Place+"/"+it.Id);
    if(have==null){bad.Add(it.Path(cfg)+" is not in the scene");continue;}
    if(!Same(cfg,have,it)){bad.Add(it.Path(cfg)+" stands with another pose / mesh / colliders than the data gives");continue;}
    if(holders.Add(have.parent))rows.Add(new Row{kind="create-place",key=BuildingAudit308.KeyOf(have.parent),detail="place "+it.Place});
    mine.Add(have);if(it.Solid)solid++;
    rows.Add(new Row{kind="create",key=BuildingAudit308.KeyOf(have),detail=it.Id+" at "+V(have.position)+(it.Solid?" solid x"+Boxes(it).Length:"")});
   }
   // nothing else may stand under the root: an object the data does not name is not a row scene-apply would have written
   foreach(Transform h in root)
   {
    if(!holders.Contains(h)){bad.Add(cfg.Root+"/"+h.name+" stands under the root and no enabled place of the data names it");continue;}
    foreach(Transform o in h)if(!mine.Contains(o))bad.Add(cfg.Root+"/"+h.name+"/"+o.name+" stands under the root and no row of the data names it");
   }
   if(bad.Count>0)throw new Refuse(Adopt308.NotEqual(scene.path,bad,6));
   if(scene.isDirty)throw new Refuse("the scene is dirty after a read-only comparison (bug): reload it. Nothing written");
   string sha=Harness303.Sha(Harness303.Abs(t.Scene)),utc=Adopt308.Utc();
   var ledger=new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O"),adopted=Adopt308.Stamp(utc,sha)};
   ledger.writes.Add(new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Adopt308.NoBackup,shaBefore="",shaAfter=sha,dataSha=cfg.Sha,rows=rows});
   WriteLedger(key,ledger);
   return head+"  the scene equals the data: "+mine.Count+" object(s) in "+holders.Count+" place(s), "+solid+" with colliders\n"
    +"  wrote "+rows.Count+" row(s) (1 create-root, "+holders.Count+" create-place, "+mine.Count+" create); "+ledger.adopted+"\n"
    +"  "+Adopt308.Unknown+": the scene bytes and the scene sha before the first apply (scene-revert removes these objects and restores nothing)\n"
    +"  ledger "+file+" (the scene was not changed)";
  }

  static string AssetsAdopt()
  {
   var cfg=Load();string file=LedgerFile("assets");string head="Places308 assets-adopt (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n";
   if(File.Exists(file))
   {
    var old=ReadLedger("assets");int live=Live(old).Count();
    if(old!=null&&Adopt308.Is(old.adopted))return head+"  "+Adopt308.Already(file,old.adopted,live);
    throw new Refuse(Adopt308.Taken(file,live));
   }
   var rows=new List<Row>();var bad=new List<string>();float tol=cfg.R("mesh_bounds_tol_m");
   foreach(var m in Arr(cfg.J,"meshes"))
   {
    string name=Str(m,"name"),path=MeshAsset(cfg,name),json=JsonState(cfg,m);
    if(json!=null){bad.Add("mesh "+name+": "+json);continue;}
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have==null){bad.Add(path+" is missing");continue;}
    if(!MeshMatches(cfg,have,m)){bad.Add(path+" is not the mesh the data describes");continue;}
    var built=BuildMesh(cfg,m);string diff;try{diff=Adopt308.MeshDiff(have,built,tol);}finally{Object.DestroyImmediate(built);}
    if(diff!=null){bad.Add(path+" is not what its json builds ("+diff+")");continue;}
    rows.Add(new Row{kind="mesh",key=path,detail=name+" json "+Short(Str(m,"sha256"))});
   }
   if(bad.Count>0)throw new Refuse(Adopt308.NotEqual(cfg.AssetDir,bad,6));
   string utc=Adopt308.Utc();
   var ledger=new Ledger{group=Opt(cfg.J,"group")??"",target=cfg.AssetDir,created=DateTime.UtcNow.ToString("O"),adopted=Adopt308.AssetStamp(utc,rows.Count+" mesh asset(s) equal to what their json builds")};
   ledger.writes.Add(new Write{utc=DateTime.UtcNow.ToString("O"),target=cfg.AssetDir,backup=Adopt308.Unknown+" (no folder row is adopted: which folders the first assets-apply made is not known)",dataSha=cfg.Sha,rows=rows});
   WriteLedger("assets",ledger);
   return head+"  every mesh asset equals what its json builds: "+rows.Count+" of "+Arr(cfg.J,"meshes").Count()+" (vertices / normals / uv within "+F(tol,"F4")+", triangles equal)\n"
    +"  wrote "+rows.Count+" mesh row(s); "+ledger.adopted+"\n"
    +"  "+Adopt308.Unknown+": the folders the first assets-apply made (assets-revert takes the mesh assets out and leaves the folders)\n"
    +"  ledger "+file+" (no asset was changed)";
  }
 }
}
