using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 ledger recovery (Adopt308.cs): OreOutcrop308 "scene-adopt:<scene>" and "bake-adopt".
 // scene-adopt: when an outcrop stands in the scene without a ledger, scene-apply refuses "scene-revert first" the moment the data
 // changes and scene-revert finds no row. The ledger scene-apply would have written (create-root, create) is written from the
 // opened scene - only when the root holds exactly the enabled outcrop(s) of the data, each as Same / Same2 say. A scene without
 // the root has nothing to adopt (scene-apply opens its own ledger there).
 // bake-adopt: a row per mesh asset whose geometry equals what its pack builds AND whose pack hashes to the sha the data holds -
 // what bake would have journalled. bake-revert can then take those assets out; the third try's bake reads the row's pack sha.
 // Material copies (third try) are never adopted: bake writes their rows itself. Compiles against the second-try and the
 // third-try OreOutcrop308.Vein2.cs alike (nothing of the third try is named here).
 public static partial class OreOutcrop308
 {
  // the adopted mark (status) and the revert note hold while a live row still sits in the write an adopt made
  // (Adopt308.UnknownBackup): after a revert of those rows and a real apply the ledger is a plain one again
  static string AdoptedLive(Ledger l)=>l!=null&&Adopt308.Is(l.adopted)&&l.writes.Any(w=>w!=null&&w.rows!=null&&Adopt308.UnknownBackup(w.backup)&&w.rows.Any(r=>r!=null&&!r.reverted))?l.adopted:null;
  static string AdoptNote(string arg)
  {
   try{var cfg=Load();return Adopt308.RevertNote(AdoptedLive(ReadLedger(TargetOf(cfg,arg))));}
   catch(Refuse){return "";}   // the revert itself says what is wrong
  }

  static string SceneAdopt(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);string file=LedgerFile(t);bool v2=V2(cfg);
   string head="OreOutcrop308 scene-adopt "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n";
   if(File.Exists(file))
   {
    var old=ReadLedger(t);int live=Live(old).Count();
    if(old!=null&&Adopt308.Is(old.adopted))return head+"  "+Adopt308.Already(file,old.adopted,live);
    throw new Refuse(Adopt308.Taken(file,live));
   }
   var scene=Open(t);
   int roots=scene.GetRootGameObjects().Count(g=>g.name==cfg.Root);
   if(roots==0)return head+"  nothing to adopt: "+cfg.Root+" is not in "+scene.path+" (no outcrop stands there; scene-apply opens its own ledger). Nothing written";
   if(roots>1)throw new Refuse(roots+" roots named "+cfg.Root+" in "+scene.path+": which one is this tool's cannot be told. Nothing written");
   var root=Root(cfg,scene);if(PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   var rows=new List<Row>{new Row{kind="create-root",key=BuildingAudit308.KeyOf(root),detail="root"}};var bad=new List<string>();var mine=new HashSet<Transform>();
   foreach(var o in Enabled(cfg))
   {
    string id=Str(o,"id");var have=root.Find(id);
    if(have==null){bad.Add(cfg.Root+"/"+id+" is not in the scene");continue;}
    bool same=v2?Same2(cfg,have,Build2(cfg,scene,o,null)):Same(cfg,have,Build(cfg,scene,o));
    if(!same){bad.Add(cfg.Root+"/"+id+" stands with another pose / parts than the data gives");continue;}
    mine.Add(have);rows.Add(new Row{kind="create",key=BuildingAudit308.KeyOf(have),detail=id+" at "+V(have.position)});
   }
   foreach(Transform c in root)if(!mine.Contains(c))bad.Add(cfg.Root+"/"+c.name+" stands under the root and the data does not enable it");
   if(bad.Count>0)throw new Refuse(Adopt308.NotEqual(scene.path,bad,6));
   if(scene.isDirty)throw new Refuse("the scene is dirty after a read-only comparison (bug): reload it. Nothing written");
   string sha=Harness303.Sha(Harness303.Abs(t.Scene)),utc=Adopt308.Utc();
   var ledger=new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O"),adopted=Adopt308.Stamp(utc,sha)};
   ledger.writes.Add(new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Adopt308.NoBackup,shaBefore="",shaAfter=sha,dataSha=cfg.Sha,rows=rows});
   WriteLedger(t,ledger);
   return head+"  the scene equals the data: "+mine.Count+" outcrop(s) under "+cfg.Root+"\n"
    +"  wrote "+rows.Count+" row(s) (1 create-root, "+mine.Count+" create); "+ledger.adopted+"\n"
    +"  "+Adopt308.Unknown+": the scene bytes and the scene sha before the first apply (scene-revert removes the outcrop and restores nothing)\n"
    +"  ledger "+file+" (the scene was not changed)";
  }

  static string BakeAdopt()
  {
   var cfg=Load();if(!V2(cfg))return "REFUSED bake-adopt: no enabled split_vein row in ore308.json (the first try bakes nothing)";
   var o=SplitRow(cfg);string file=AssetLedgerFile;string head="OreOutcrop308 bake-adopt (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", "+Str(o,"id")+")\n";
   if(File.Exists(file))
   {
    var old=ReadAssetLedger();var live=old.rows.Where(r=>r!=null&&!r.reverted).ToList();
    if(live.Count>0&&live.All(r=>Adopt308.Is(r.utc)))return head+"  "+Adopt308.Already(file,live[0].utc,live.Count);
    throw new Refuse(Adopt308.Taken(file,live.Count));
   }
   var rows=new List<AssetRow>();var bad=new List<string>();float tol=Num(Req(cfg.Rules,"mesh"),"bounds_tol_m");string utc=Adopt308.Utc();
   foreach(var r in Packs(o))
   {
    string abs=PackAbs(o,r.File),path=AssetOf(o,r.File);
    if(!File.Exists(abs)){bad.Add("mesh pack missing: "+abs);continue;}
    string sha=Harness303.Sha(abs);
    if(!string.Equals(sha,r.Sha,StringComparison.OrdinalIgnoreCase)){bad.Add("pack "+r.File+" is "+Short(sha)+", the data names "+Short(r.Sha));continue;}
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have==null){bad.Add(path+" is missing");continue;}
    string miss=Mismatch(cfg,have,r);if(miss!=null){bad.Add(path+" is not the mesh the data expects ("+miss+")");continue;}
    Pack p;try{p=JsonUtility.FromJson<Pack>(File.ReadAllText(abs));}catch(Exception e){bad.Add("pack "+r.File+" does not parse ("+e.Message+")");continue;}
    var built=BuildMesh(p,Path.GetFileNameWithoutExtension(path));string diff;try{diff=Adopt308.MeshDiff(have,built,tol);}finally{Object.DestroyImmediate(built);}
    if(diff!=null){bad.Add(path+" is not what pack "+r.File+" builds ("+diff+")");continue;}
    rows.Add(new AssetRow{path=path,pack=r.File,sha=r.Sha,utc=Adopt308.AssetStamp(utc,"the mesh asset (equal to what this pack builds)")});
   }
   if(bad.Count>0)throw new Refuse(Adopt308.NotEqual(Str(o,"asset_dir"),bad,6));
   WriteAssetLedger(new AssetLedger{rows=rows});
   return head+"  every mesh asset equals what its pack builds and every pack is the data's: "+rows.Count+" of "+Packs(o).Count+" (vertices / normals / uv within "+F(tol,"F4")+", triangles equal)\n"
    +"  wrote "+rows.Count+" row(s), each stamped \"adopted "+utc+" ...\" in its utc field\n"
    +"  "+Adopt308.Unknown+": rows of earlier bakes (other packs) and any material copy - not adopted\n"
    +"  ledger "+file+" (no asset was changed)";
  }
 }
}
