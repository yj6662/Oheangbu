using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet308F=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 // [SPEC-WORLD-REALM-FLORA-308] Vegetation of one realm as its own dressing sheet and its own renderer (the #305 pattern): the shared
 // sheets (Architecture296/Data/*) are read, never written here. Rows that have to leave a shared sheet go through the
 // BuildingFix308.VegRows ledger (remove-plan / remove-apply), so "Veg replay" brings the removals back after a #296 re-bake.
 //   status | protos | plan:<Realm> | build:<Realm> | clear:<Realm> | check:<Realm> | remove-plan:<Realm> | remove-apply:<Realm> | remove-revert:<Realm>
 // Input (written by Tools/Unity/Stage308_flora/_Tools/flora_plan.py): Tools/Unity/Stage308_flora/Out/flora_<Realm>.json
 //   { "version", "realm", "prototypes": ["<id in a scene sheet>"], "made": [{ "id", "prefab", "category", "radius" }],
 //     "candidates": [{ "Id", "ClusterId", "PrototypeId", "kind", "Position":[x,y,z], "Euler":[..], "Scale" }],
 //     "remove": [{ "sheet", "id", "proto", "pos":[x,y,z] }] }
 // Run in W_Demo_Main, clean, in Edit mode; open the realm scenes first (RealmStream308 open-all) so that built ground counts.
 // Saves the sheet of that realm (SaveAssetIfDirty) and the play scene (SaveScene). Queue-safe: no dialog, refusals come back as text.
 public static partial class CompactRebuildAuthoring
 {
  const string Main308F="Assets/_Project/Scenes/World/W_Demo_Main.unity",A308F="Assets/_Project/Art/World/Flora308",Root308F="Flora308";
  // TEST numbers (Spec 3.1): trunk to trunk, shrub to trunk, content points, built ground above the terrain, rows per sheet
  const float TreeClear308F=2.5f,ShrubClear308F=1f,ContentClear308F=6f,BuiltOver308F=.6f;const int Cap308F=60000;
  static readonly string[] Realms308F={"Cheongrim","Hwanggyeong","Cheolong","Jeokro","Hyeongang"};
  static string Stage308F=>Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Tools","Unity","Stage308_flora"));

  [Serializable] sealed class Made308F{public string id="",prefab="",category="Tree";public float radius=.3f;}
  [Serializable] sealed class Remove308F{public string sheet="",id="",proto="";public float[] pos=Array.Empty<float>();}
  [Serializable] sealed class File308F{public string version="",realm="";public string[] prototypes=Array.Empty<string>();public Made308F[] made=Array.Empty<Made308F>();
   public Cand305[] candidates=Array.Empty<Cand305>();public Remove308F[] remove=Array.Empty<Remove308F>();}

  public static string Flora308(string command)
  {
   try
   {
    string[] a=(command??"").Split(new[]{':'},2);string realm=a.Length>1?a[1].Trim():"";
    if(a[0]=="status")return Status308F();
    if(a[0]=="protos")return Protos308F();
    if(!Realms308F.Contains(realm))return "REFUSED realm '"+realm+"' (one of "+string.Join(", ",Realms308F)+")";
    try{Cliff305Now=LoadCliff305();}catch(Exception){Cliff305Now=new Cliff305();}
    switch(a[0])
    {
     case "plan":return Build308F(realm,true);
     case "build":return Build308F(realm,false);
     case "clear":return Clear308F(realm);
     case "check":return Check308F(realm);
     case "remove-plan":return BuildingFix308.VegRowsPlan("flora308-"+realm,Asks308F(Read308F(realm)),.05f);
     case "remove-apply":return BuildingFix308.VegRowsApply("flora308-"+realm,Asks308F(Read308F(realm)),.05f);
     case "remove-revert":return BuildingFix308.VegRowsRevert("flora308-"+realm,.05f);
    }
   }
   catch(Refuse305 r){return "REFUSED "+r.Message;}
   return "ERROR unknown Flora308 command '"+command+"' (status | protos | plan:<Realm> | build:<Realm> | clear:<Realm> | check:<Realm> | remove-plan:<Realm> | remove-apply:<Realm> | remove-revert:<Realm>)";
  }

  static File308F Read308F(string realm)
  {
   string file=Path.Combine(Stage308F,"Out","flora_"+realm+".json");
   if(!File.Exists(file))throw new Refuse305("no input "+file+" (run Tools/Unity/Stage308_flora/_Tools/flora_plan.py "+realm+")");
   var f=JsonUtility.FromJson<File308F>(File.ReadAllText(file).TrimStart('﻿'));
   if(f==null||f.realm!=realm)throw new Refuse305(file+" is for realm '"+(f?.realm??"?")+"', not "+realm);
   return f;
  }
  static List<BuildingFix308.VegRowAsk308> Asks308F(File308F f)=>(f.remove??Array.Empty<Remove308F>()).Where(r=>r!=null&&r.pos!=null&&r.pos.Length>=3)
   .Select(r=>new BuildingFix308.VegRowAsk308{sheet=r.sheet,id=r.id,proto=r.proto,at=new Vector3(r.pos[0],r.pos[1],r.pos[2])}).ToList();

  static Scene Scene308F(bool write)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   var s=SceneManager.GetActiveScene();if(s.path!=Main308F)throw new Refuse305("active scene is "+s.path+", not "+Main308F);
   if(write)for(int i=0;i<SceneManager.sceneCount;i++){var o=SceneManager.GetSceneAt(i);if(o.isDirty)throw new Refuse305("scene "+o.path+" has unsaved changes - nothing written");}
   return s;
  }
  static CompactRebuildArtRenderer[] Arts308F(Scene scene)=>Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
   .Where(x=>x.Sheet!=null&&x.gameObject.scene==scene).ToArray();
  static string SheetPath308F(string realm)=>A308F+"/Flora308_"+realm+".asset";

  static string Status308F()
  {
   var scene=SceneManager.GetActiveScene();var sb=new StringBuilder("Flora308 status | scene "+scene.name+"\n");
   foreach(var x in Arts308F(scene).OrderBy(x=>x.Sheet.name))
   {
    var cat=x.Sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First().Category);
    var by=x.Sheet.FixedPlacements.Where(f=>f!=null&&f.PrototypeId!=null&&cat.ContainsKey(f.PrototypeId)).GroupBy(f=>cat[f.PrototypeId]).OrderBy(g=>g.Key).Select(g=>g.Key+" "+g.Count());
    sb.AppendLine("  "+x.gameObject.name.PadRight(28)+" "+x.Sheet.name+" | rows "+x.Sheet.FixedPlacements.Length+" ("+string.Join(", ",by)+") | observer "+(x.Observer!=null?"set":"MISSING"));
   }
   foreach(string r in Realms308F)sb.AppendLine("  input "+r.PadRight(12)+(File.Exists(Path.Combine(Stage308F,"Out","flora_"+r+".json"))?"present":"-")+" | sheet "+(AssetDatabase.LoadAssetAtPath<Sheet308F>(SheetPath308F(r))!=null?"present":"-"));
   return sb.ToString().TrimEnd();
  }

  /// <summary>Every prototype the scene's sheets hold: what the planner may name in "prototypes".</summary>
  static string Protos308F()
  {
   var scene=SceneManager.GetActiveScene();var sb=new StringBuilder("Flora308 protos | scene "+scene.name+"\n");var seen=new HashSet<string>();
   foreach(var x in Arts308F(scene).OrderByDescending(x=>x.Sheet.FixedPlacements.Length))
   {
    var use=x.Sheet.FixedPlacements.Where(f=>f!=null&&f.PrototypeId!=null).GroupBy(f=>f.PrototypeId).ToDictionary(g=>g.Key,g=>g.Count());
    foreach(var p in x.Sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).OrderBy(p=>p.Category).ThenBy(p=>p.Id))
    {
     if(!seen.Add(p.Id))continue;long tris=0;var l0=p.Lods!=null&&p.Lods.Length>0?p.Lods[0]:null;
     if(l0!=null)foreach(var part in l0.Parts)if(part.Mesh!=null&&part.Submesh<part.Mesh.subMeshCount)tris+=part.Mesh.GetIndexCount(part.Submesh)/3;
     sb.AppendLine("  "+p.Category.ToString().PadRight(6)+" "+p.Id.PadRight(52)+" lods "+(p.Lods?.Length??0)+" tris0 "+tris.ToString().PadLeft(7)+" r "+F305(p.Radius)+" used "+(use.TryGetValue(p.Id,out int n)?n:0).ToString().PadLeft(6)+" | "+x.Sheet.name+" | "+p.SourcePath);
    }
   }
   return sb.ToString().TrimEnd();
  }

  /// <summary>A prototype from a prefab (new assets): the levels of its first LODGroup, or all its renderers as one level.</summary>
  static Sheet308F.Prototype Made308FProto(Made308F m,RealmId realm,List<string> problems)
  {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(m.prefab);if(prefab==null){problems.Add("made "+m.id+": no prefab at "+m.prefab);return null;}
   if(!Enum.TryParse(m.category,true,out Sheet308F.Kind kind)){problems.Add("made "+m.id+": category "+m.category);return null;}
   var toRoot=prefab.transform.worldToLocalMatrix;var levels=new List<Sheet308F.Level>();var b=new Bounds();bool has=false;
   Sheet308F.Level Level(IEnumerable<Renderer> rs)
   {
    var parts=new List<Sheet308F.Part>();
    foreach(var r in rs.OfType<MeshRenderer>())
    {
     var f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null)continue;var mats=r.sharedMaterials;
     for(int s=0;s<f.sharedMesh.subMeshCount&&s<mats.Length;s++)
     {
      if(mats[s]==null)continue;
      if(mats[s].IsKeywordEnabled("_EMISSION")){problems.Add("made "+m.id+": material "+mats[s].name+" has _EMISSION");continue;}
      parts.Add(new Sheet308F.Part{Mesh=f.sharedMesh,Submesh=s,Material=mats[s],Local=toRoot*r.transform.localToWorldMatrix});
     }
     if(levels.Count==0){var wb=r.bounds;if(!has){b=wb;has=true;}else b.Encapsulate(wb);}
    }
    return new Sheet308F.Level{Parts=parts.ToArray()};
   }
   var group=prefab.GetComponentInChildren<LODGroup>(true);
   if(group!=null)foreach(var lod in group.GetLODs()){var l=Level(lod.renderers.Where(r=>r!=null));if(l.Parts.Length>0)levels.Add(l);if(levels.Count==3)break;}
   else{var l=Level(prefab.GetComponentsInChildren<MeshRenderer>(true));if(l.Parts.Length>0)levels.Add(l);}
   if(levels.Count==0){problems.Add("made "+m.id+": no drawable part in "+m.prefab);return null;}
   return new Sheet308F.Prototype{Id=m.id,SourcePath=m.prefab,SourceHash=AssetDatabase.AssetPathToGUID(m.prefab),Realm=realm,Category=kind,Radius=m.radius,Size=b.size,Lods=levels.ToArray()};
  }

  static string Build308F(string realm,bool dry)
  {
   var scene=Scene308F(!dry);var input=Read308F(realm);var session=Session292();var content=session.Content??throw new Refuse305("session has no content");
   var arts=Arts308F(scene);string sheetPath=SheetPath308F(realm);
   var own=AssetDatabase.LoadAssetAtPath<Sheet308F>(sheetPath);
   var others=arts.Where(x=>x.Sheet!=own).ToArray();
   var main=others.FirstOrDefault(x=>x.gameObject.name=="Rebuild_EnvironmentArt"||(x.transform.parent!=null&&x.transform.parent.name=="Rebuild_EnvironmentArt"))
            ??others.OrderByDescending(x=>x.Sheet.FixedPlacements.Length).FirstOrDefault()??throw new Refuse305("no art renderer with a sheet in the scene");
   if(main.Observer==null)throw new Refuse305("art renderer "+main.gameObject.name+" has no Observer; the new renderer would draw nothing in Play");
   if(!Enum.TryParse(realm,true,out RealmId realmId))realmId=default;
   int realmScenes=0;for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).name.StartsWith("Realm_"))realmScenes++;

   // prototypes: by Id from the scene's sheets, then the ones made from prefabs
   var problems=new List<string>();var protos=new List<Sheet308F.Prototype>();
   var wanted=input.prototypes.Concat(input.candidates.Where(c=>c!=null).Select(c=>c.PrototypeId)).Where(x=>!string.IsNullOrEmpty(x)).Distinct().ToList();
   var made=(input.made??Array.Empty<Made308F>()).Where(m=>m!=null&&m.id!="").ToDictionary(m=>m.id,m=>m);
   foreach(string id in wanted)
   {
    if(made.TryGetValue(id,out var m)){var p=Made308FProto(m,realmId,problems);if(p!=null)protos.Add(p);continue;}
    var src=others.Select(x=>x.Sheet.Prototypes.FirstOrDefault(p=>p!=null&&p.Id==id)).FirstOrDefault(p=>p!=null);
    if(src==null)problems.Add("prototype "+id+" is in no sheet of the scene and not in made[]");else protos.Add(Proto305(src));
   }
   var kinds=protos.ToDictionary(p=>p.Id,p=>p.Category);

   // what is already there (minus the rows this input removes), content points, preserved areas
   var leaving=new HashSet<string>((input.remove??Array.Empty<Remove308F>()).Where(r=>r!=null).Select(r=>r.sheet+"|"+r.id));
   var trees=new Grid305(8f);
   foreach(var x in others)
   {
    string path=AssetDatabase.GetAssetPath(x.Sheet);var cat=x.Sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First().Category);
    foreach(var f in x.Sheet.FixedPlacements)if(f!=null&&f.PrototypeId!=null&&cat.TryGetValue(f.PrototypeId,out var c)&&c==Sheet308F.Kind.Tree&&!leaving.Contains(path+"|"+f.Id))trees.Add(f.Position);
   }
   var areas=others.SelectMany(x=>x.Sheet.PreservedAreas??Array.Empty<Sheet308F.PreserveArea>()).Where(x=>x!=null&&x.ExcludeProcedural).ToArray();
   var reach=areas.Select(x=>x.HalfSize.magnitude+(x.TypedClearance?Mathf.Max(x.Padding.x,Mathf.Max(x.Padding.y,x.Padding.w)):2f)+.5f).ToArray();
   var reserved=new Grid305(16f);
   foreach(var p in content.Points??Array.Empty<PrologueContentSO.Point>())if(p!=null)reserved.Add(p.Position);
   foreach(var e in content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>())if(e!=null)reserved.Add(e.Feet);
   foreach(var c in content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())if(c!=null)reserved.Add(c.Feet);
   var skip=Skip305(session);Physics.SyncTransforms();

   var dropped=new SortedDictionary<string,int>();void Drop(string why){dropped[why]=dropped.TryGetValue(why,out int v)?v+1:1;}
   var kept=new List<Sheet308F.FixedPlacement>();var ids=new HashSet<string>();var keptBy=new SortedDictionary<string,int>();var mine=new Grid305(8f);var dy=new List<float>();
   foreach(var c in input.candidates)
   {
    if(c==null||c.Position==null||c.Position.Length<3)continue;
    if(string.IsNullOrEmpty(c.PrototypeId)||!kinds.TryGetValue(c.PrototypeId,out var kind)){Drop("prototype missing");continue;}
    if(string.IsNullOrEmpty(c.Id)||!ids.Add(c.Id)){Drop("duplicate id");continue;}
    var xz=new Vector2(c.Position[0],c.Position[2]);bool woody=kind==Sheet308F.Kind.Tree||kind==Sheet308F.Kind.Shrub;
    if(kind==Sheet308F.Kind.Tree&&(trees.Any(xz,TreeClear308F)||mine.Any(xz,TreeClear308F))){Drop("tree within "+TreeClear308F+" m of a tree");continue;}
    if(kind==Sheet308F.Kind.Shrub&&trees.Any(xz,ShrubClear308F)){Drop("shrub within "+ShrubClear308F+" m of a trunk");continue;}
    if(reserved.Any(xz,ContentClear308F)){Drop("content / encounter / checkpoint "+ContentClear308F+" m");continue;}
    if(!Ground305(xz.x,xz.y,null,skip,out float gy,out bool terrain,out float top,out Collider topBy)){Drop("no ground");continue;}
    if(!terrain){Drop("no terrain under it");continue;}
    if(top>gy+BuiltOver308F&&topBy!=null&&!Terrain305(topBy)){Drop("something built stands there ("+topBy.transform.root.name+")");continue;}
    var pos=new Vector3(xz.x,gy-(woody?.05f:.02f),xz.y);bool preserved=false;
    for(int i=0;i<areas.Length&&!preserved;i++)if((new Vector2(areas[i].Centre.x,areas[i].Centre.z)-xz).magnitude<=reach[i]&&Sheet308F.Excludes(areas[i],pos,kind))preserved=true;
    if(preserved){Drop("preserved area");continue;}
    if(kept.Count>=Cap308F){Drop("cap "+Cap308F);continue;}
    dy.Add(pos.y-c.Position[1]);if(kind==Sheet308F.Kind.Tree)mine.Add(pos);
    kept.Add(new Sheet308F.FixedPlacement{Id=c.Id,ClusterId=c.ClusterId,PrototypeId=c.PrototypeId,Position=pos,Euler=c.Euler!=null&&c.Euler.Length>=3?new Vector3(c.Euler[0],c.Euler[1],c.Euler[2]):Vector3.zero,Scale=c.Scale>0?c.Scale:1,Preserve=true});
    keptBy[kind.ToString()]=keptBy.TryGetValue(kind.ToString(),out int kn)?kn+1:1;
   }

   var sb=new StringBuilder("Flora308 "+(dry?"plan":"build")+" "+realm+" | input version "+input.version+" | candidates "+input.candidates.Length+" | realm scenes open "+realmScenes+(realmScenes<5?" (WARNING: built ground of closed realm scenes is not seen)":"")+"\n");
   sb.AppendLine("  prototypes "+protos.Count+" ("+string.Join(", ",protos.GroupBy(p=>p.Category).Select(g=>g.Key+" "+g.Count()))+")"+(problems.Count>0?" | PROBLEMS "+string.Join("; ",problems):""));
   sb.AppendLine("  kept "+kept.Count+" ("+string.Join(", ",keptBy.Select(k=>k.Key+" "+k.Value))+") | ground minus offline "+Stats305(dy));
   foreach(var d in dropped)sb.AppendLine("  dropped "+d.Value.ToString().PadLeft(6)+"  "+d.Key);
   sb.AppendLine("  rows of other sheets this input removes "+leaving.Count+" (remove-plan / remove-apply, not done by build)");
   if(dry)return sb.ToString().TrimEnd();
   if(problems.Count>0)return "REFUSED "+string.Join("; ",problems)+"\n"+sb;

   string backup=Backup305(Main308F);string sheetBackup="";
   DevSceneKit.EnsureFolder(A308F);
   if(own==null){own=ScriptableObject.CreateInstance<Sheet308F>();own.name="Flora308_"+realm;AssetDatabase.CreateAsset(own,sheetPath);}
   else{string dir=Path.Combine(Stage308F,"Backup");Directory.CreateDirectory(dir);sheetBackup=Path.Combine(dir,"Flora308_"+realm+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".asset");File.Copy(Path.GetFullPath(sheetPath),sheetBackup,true);}
   var from=main.Sheet;
   own.Prototypes=protos.ToArray();own.FixedPlacements=kept.ToArray();
   own.TreeNear=from.TreeNear;own.TreeMiddle=from.TreeMiddle;own.TreeDistance=from.TreeDistance;own.ForestDistance=from.ForestDistance;
   own.ShrubDistance=from.ShrubDistance;own.GrassDistance=from.GrassDistance;own.GrassMeshDistance=from.GrassMeshDistance;own.RegionBlend=from.RegionBlend;
   own.PreservedAreas=Array.Empty<Sheet308F.PreserveArea>();own.Cells=Array.Empty<Sheet308F.Cell>();own.StoryClusters=Array.Empty<Sheet308F.StoryCluster>();
   EditorUtility.SetDirty(own);AssetDatabase.SaveAssetIfDirty(own);

   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root308F);
   if(root==null){root=new GameObject(Root308F);SceneManager.MoveGameObjectToScene(root,scene);}
   var host=root.transform.Find(realm+"_Renderer");
   if(host==null){host=new GameObject(realm+"_Renderer").transform;host.SetParent(root.transform,false);}
   var art=host.GetComponent<CompactRebuildArtRenderer>();if(art==null)art=host.gameObject.AddComponent<CompactRebuildArtRenderer>();
   art.Sheet=own;art.Observer=main.Observer;art.Contacts=main.Contacts;art.Invalidate();
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))return "ERROR SaveScene returned false for "+Main308F+" (sheet saved; backup "+backup+")";
   return sb+"  saved "+sheetPath+" and "+Main308F+" | scene backup "+backup+(sheetBackup!=""?" | sheet backup "+sheetBackup:"");
  }

  static string Clear308F(string realm)
  {
   var scene=Scene308F(true);var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root308F);var host=root!=null?root.transform.Find(realm+"_Renderer"):null;
   if(host==null)return "Flora308 clear "+realm+": no renderer in the scene (nothing changed; the sheet asset stays)";
   string backup=Backup305(Main308F);Object.DestroyImmediate(host.gameObject);if(root.transform.childCount==0)Object.DestroyImmediate(root);
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Flora308 clear "+realm+": renderer taken out, scene saved (sheet asset "+SheetPath308F(realm)+" left on disk) | backup "+backup;
  }

  static string Check308F(string realm)
  {
   var scene=Scene308F(false);var input=Read308F(realm);var sheet=AssetDatabase.LoadAssetAtPath<Sheet308F>(SheetPath308F(realm));
   var sb=new StringBuilder("Flora308 check "+realm+"\n");int pass=0,fail=0;
   void Line(bool ok,string id,string text){if(ok)pass++;else fail++;sb.AppendLine((ok?"PASS ":"FAIL ")+id+" "+text);}
   if(sheet==null){Line(false,"AC-F1","no sheet "+SheetPath308F(realm));return sb+"checks, "+pass+" pass "+fail+" fail";}
   var art=Arts308F(scene).FirstOrDefault(x=>x.Sheet==sheet);
   var protoIds=new HashSet<string>(sheet.Prototypes.Where(p=>p!=null).Select(p=>p.Id));
   int noLod=sheet.Prototypes.Count(p=>p==null||p.Lods==null||p.Lods.Length==0||p.Lods.Any(l=>l==null||l.Parts==null||l.Parts.Length==0||l.Parts.Any(x=>x.Mesh==null||x.Material==null)));
   int glow=sheet.Prototypes.Where(p=>p?.Lods!=null).SelectMany(p=>p.Lods).Where(l=>l?.Parts!=null).SelectMany(l=>l.Parts).Count(x=>x.Material!=null&&x.Material.IsKeywordEnabled("_EMISSION"));
   int orphan=sheet.FixedPlacements.Count(f=>f==null||!protoIds.Contains(f.PrototypeId));
   int dup=sheet.FixedPlacements.Length-sheet.FixedPlacements.Where(f=>f!=null).Select(f=>f.Id).Distinct().Count();
   var asked=new HashSet<string>(input.candidates.Where(c=>c!=null).Select(c=>c.Id));int foreign=sheet.FixedPlacements.Count(f=>f!=null&&!asked.Contains(f.Id));
   Line(sheet.FixedPlacements.Length>0&&orphan==0&&dup==0&&foreign==0,"AC-F1a","rows "+sheet.FixedPlacements.Length+" of "+input.candidates.Length+" candidates | without prototype "+orphan+" | duplicate ids "+dup+" | not in the input "+foreign);
   Line(noLod==0&&glow==0,"AC-F1b","prototypes "+sheet.Prototypes.Length+" | without a complete level "+noLod+" | parts with _EMISSION "+glow);
   Line(art!=null&&art.Observer!=null&&art.isActiveAndEnabled,"AC-F1c","renderer in the scene with an observer: "+(art==null?"none":art.Observer==null?"no observer":"yes"));

   // seat: every 23rd row against the ground now (terrain first, like build)
   var session=Session292();var skip=Skip305(session);Physics.SyncTransforms();int sampled=0,off=0,built=0;float worst=0;string worstId="";
   var content=session.Content;var reserved=new Grid305(16f);
   if(content!=null){foreach(var p in content.Points??Array.Empty<PrologueContentSO.Point>())if(p!=null)reserved.Add(p.Position);foreach(var e in content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>())if(e!=null)reserved.Add(e.Feet);}
   int nearContent=0;
   for(int i=0;i<sheet.FixedPlacements.Length;i++)
   {
    var f=sheet.FixedPlacements[i];if(f==null)continue;if(reserved.Any(new Vector2(f.Position.x,f.Position.z),ContentClear308F-.01f))nearContent++;
    if(i%23!=0)continue;
    if(!Ground305(f.Position.x,f.Position.z,null,skip,out float gy,out bool terrain,out float top,out Collider topBy)||!terrain){off++;sampled++;continue;}
    sampled++;float d=Mathf.Abs(f.Position.y-gy);if(d>.25f)off++;if(d>worst){worst=d;worstId=f.Id;}
    if(top>gy+BuiltOver308F&&topBy!=null&&!Terrain305(topBy))built++;
   }
   Line(nearContent==0,"AC-F2","rows within "+ContentClear308F+" m of a content point or encounter "+nearContent);
   Line(off==0&&built==0,"AC-F3","sampled "+sampled+" rows | off the ground by more than 0.25 m "+off+" | under something built "+built+" | largest "+F305(worst)+" m ("+worstId+")");
   var cat=sheet.Prototypes.Where(p=>p!=null).ToDictionary(p=>p.Id,p=>p.Category);
   sb.AppendLine("  by kind: "+string.Join(", ",sheet.FixedPlacements.Where(f=>f!=null&&cat.ContainsKey(f.PrototypeId)).GroupBy(f=>cat[f.PrototypeId]).OrderBy(g=>g.Key).Select(g=>g.Key+" "+g.Count())));
   sb.AppendLine("  by cluster: "+string.Join(", ",sheet.FixedPlacements.Where(f=>f!=null).GroupBy(f=>(f.ClusterId??"").Split('#')[0]).OrderByDescending(g=>g.Count()).Take(14).Select(g=>g.Key+" "+g.Count())));
   return sb+"checks, "+pass+" pass "+fail+" fail";
  }
 }
}
