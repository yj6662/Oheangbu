using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 terrain pass (SPEC-WORLD-FINISH-297): Tools/Art/surface297.py writes the staged height field (#295 + compound pads +
 // cave portal notch). Here it becomes the candidate's FinalSurface and only the terrain tiles touched by an op are
 // re-sampled into private #297 meshes (the #295 meshes stay untouched). Vegetation standing on a pad/notch is removed,
 // plants near it are re-seated on the new ground. Originals are recorded once; surface-revert restores them.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class EditBox297{public float[] min,max;}
  [Serializable] class EditOp297{public string type,source;public int cells;public EditBox297 bounds;}
  [Serializable] class Edits297{public EditOp297[] perOp;}
  [Serializable] class SurfaceOriginal297{public string height;public string[] filters;public string[] meshes;public string sheet;}

  static string Surface297(bool revert)
  {
   RequireClean292();var session=Session292();var layout=session.MountainLayout;var scene=SceneManager.GetActiveScene();
   string file=K297+"/surface-original.json";var root=GameObject.Find("Reworld292_Terrain");if(root==null)throw new Exception("Candidate terrain root missing");
   var manifest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();var art=manifest.Art;
   var allFilters=root.GetComponentsInChildren<MeshFilter>(true);
   if(revert)
   {
    var o=JsonUtility.FromJson<SurfaceOriginal297>(File.ReadAllText(file));
    layout.FinalSurface=AssetDatabase.LoadAssetAtPath<TextAsset>(o.height);EditorUtility.SetDirty(layout);
    for(int i=0;i<o.filters.Length;i++){var f=allFilters.FirstOrDefault(x=>HierarchyPath(x.transform)==o.filters[i]);if(f!=null)f.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(o.meshes[i]);}
    foreach(Transform tile in root.transform){var c=tile.GetComponent<MeshCollider>();if(c==null)continue;c.sharedMesh=null;c.sharedMesh=tile.GetComponentsInChildren<MeshFilter>(true).First(x=>x.name=="LOD0").sharedMesh;}
    if(!string.IsNullOrEmpty(o.sheet)){JsonUtility.FromJsonOverwrite(o.sheet,art);EditorUtility.SetDirty(art);}
    Physics.SyncTransforms();Save292();return "Terrain restored to the recorded #296 surface";
   }
   var edits=JsonUtility.FromJson<Edits297>(File.ReadAllText(K297+"/Stage/Surface/edits.json"));
   var boxes=edits.perOp.Where(o=>o.bounds!=null&&o.bounds.min!=null&&o.bounds.min.Length==2).Select(o=>Rect.MinMaxRect(o.bounds.min[0]-8,o.bounds.min[1]-8,o.bounds.max[0]+8,o.bounds.max[1]+8)).ToArray();
   var cores=edits.perOp.Where(o=>o.bounds!=null&&o.bounds.min!=null&&o.bounds.min.Length==2).Select(o=>Rect.MinMaxRect(o.bounds.min[0]-1,o.bounds.min[1]-1,o.bounds.max[0]+1,o.bounds.max[1]+1)).ToArray();
   var tiles=root.transform.Cast<Transform>().Where(t=>boxes.Any(b=>b.Overlaps(new Rect(t.position.x,t.position.z,500,500)))).ToArray();
   if(!File.Exists(file))
   {
    var fs=tiles.SelectMany(t=>t.GetComponentsInChildren<MeshFilter>(true)).ToArray();
    File.WriteAllText(file,JsonUtility.ToJson(new SurfaceOriginal297{height=AssetDatabase.GetAssetPath(layout.FinalSurface),filters=fs.Select(f=>HierarchyPath(f.transform)).ToArray(),
     meshes=fs.Select(f=>AssetDatabase.GetAssetPath(f.sharedMesh)).ToArray(),sheet=JsonUtility.ToJson(art)},true));
   }
   var original=JsonUtility.FromJson<SurfaceOriginal297>(File.ReadAllText(file));
   var oldField=new CompactWorldSurface(layout);
   DevSceneKit.EnsureFolder(A297+"/Surface");string hp=A297+"/Surface/height.bytes";File.Copy(K297+"/Stage/Surface/height.bytes",hp,true);AssetDatabase.ImportAsset(hp,ImportAssetOptions.ForceUpdate);
   layout.FinalSurface=AssetDatabase.LoadAssetAtPath<TextAsset>(hp);EditorUtility.SetDirty(layout);
   var field=new CompactWorldSurface(layout);int changed=0;DevSceneKit.EnsureFolder(A297+"/Meshes/Terrain");
   foreach(var tile in tiles)
   {
    foreach(var f in tile.GetComponentsInChildren<MeshFilter>(true))
    {
     // always resample the recorded #295 mesh (a re-run must not inherit a later pass's edits, e.g. the cave portal hole)
     int oi=Array.IndexOf(original.filters,HierarchyPath(f.transform));var source=oi>=0?AssetDatabase.LoadAssetAtPath<Mesh>(original.meshes[oi]):null;
     string id=tile.name+"_"+f.name;var mesh=Object.Instantiate(source!=null?source:f.sharedMesh);mesh.name=id+"_297";var v=mesh.vertices;var n=mesh.normals;
     for(int i=0;i<v.Length;i++){float x=tile.position.x+v[i].x,z=tile.position.z+v[i].z;if(!boxes.Any(b=>b.Contains(new Vector2(x,z))))continue;v[i].y=field.Sample(x,z);n[i]=field.Normal(x,z);changed++;}
     mesh.vertices=v;mesh.normals=n;mesh.RecalculateBounds();mesh.RecalculateTangents();
     f.sharedMesh=ArtMesh(mesh,A297+"/Meshes/Terrain/"+id+".asset");
    }
    var col=tile.GetComponent<MeshCollider>();if(col!=null){col.sharedMesh=null;col.sharedMesh=tile.GetComponentsInChildren<MeshFilter>(true).First(x=>x.name=="LOD0").sharedMesh;}
    tile.GetComponent<LODGroup>()?.RecalculateBounds();
   }
   // vegetation: remove plants standing on a pad/notch core, re-seat the rest near the edits on the new ground
   int removed=0,moved=0;var keep=new List<WorldMacroDressingSheetSO.FixedPlacement>();
   foreach(var p in art.FixedPlacements)
   {
    var xz=new Vector2(p.Position.x,p.Position.z);
    if(cores.Any(b=>b.Contains(xz))&&art.Prototypes.First(t=>t.Id==p.PrototypeId).Category!=WorldMacroDressingSheetSO.Kind.Prop){removed++;continue;}
    if(boxes.Any(b=>b.Contains(xz))){float before=oldField.Sample(xz.x,xz.y),after=field.Sample(xz.x,xz.y);if(Mathf.Abs(after-before)>.01f){p.Position.y+=after-before;moved++;}}
    keep.Add(p);
   }
   art.FixedPlacements=keep.ToArray();EditorUtility.SetDirty(art);
   foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)))r.enabled=false;
   foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)))r.enabled=true;
   Physics.SyncTransforms();Save292();
   string text="terrain tiles="+tiles.Length+" resampled vertices="+changed+" ops="+edits.perOp.Length+"; vegetation removed="+removed+" reseated="+moved+"; FinalSurface="+hp;
   File.WriteAllText(K297+"/surface.txt",text);return text;
  }
 }
}
