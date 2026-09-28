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
 // #297 grass re-seat: ground-grass seeds are baked world positions (CompactGrassField266). The #297 terrain pass (pads,
 // notches) lowered ground under the fortress/palace/cliff path, leaving seeds floating. Each renderer's field is copied
 // once to Finish297/Dressing (the #295 sources stay untouched); seeds are dropped on the final #297 surface, and seeds
 // inside an op's changed-cell bounds (+1 m: paved courts, cut pads, portal notch) are removed. Original references are
 // recorded for grass-revert. Queue: CompactRebuildAuthoring Grass297 apply|revert
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class GrassRecord297{public string[] renderers=new string[0];public string[] fields=new string[0];}
  public static string Grass297(string arg)
  {
   string file=K297+"/Grass/original.json";var scene=SceneManager.GetActiveScene();var s=Session292();
   var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>(true)).Where(g=>g.Field!=null).ToArray();
   if(arg=="revert")
   {
    var o=JsonUtility.FromJson<GrassRecord297>(File.ReadAllText(file));var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    for(int i=0;i<o.renderers.Length;i++){var t=all.FirstOrDefault(x=>HierarchyPath(x)==o.renderers[i]);var r=t!=null?t.GetComponent<CompactGrassRenderer266>():null;if(r!=null){r.Field=AssetDatabase.LoadAssetAtPath<CompactGrassField266>(o.fields[i]);r.Invalidate();EditorUtility.SetDirty(r);}}
    Save292();return "grass restored ("+o.renderers.Length+" renderers)";
   }
   if(arg!="apply")throw new ArgumentException(arg);
   RequireClean292();
   var rec=File.Exists(file)?JsonUtility.FromJson<GrassRecord297>(File.ReadAllText(file)):new GrassRecord297();
   var edits=JsonUtility.FromJson<Edits297>(File.ReadAllText(K297+"/Stage/Surface/edits.json"));
   var cores=edits.perOp.Where(o=>o.bounds!=null&&o.bounds.min!=null&&o.bounds.min.Length==2).Select(o=>Rect.MinMaxRect(o.bounds.min[0]-1,o.bounds.min[1]-1,o.bounds.max[0]+1,o.bounds.max[1]+1)).ToArray();
   var field=new CompactWorldSurface(s.MountainLayout);DevSceneKit.EnsureFolder(A297+"/Dressing");
   int kept=0,removed=0,moved=0;var lines=new List<string>();
   foreach(var g in renderers)
   {
    string rp=HierarchyPath(g.transform);int k=Array.IndexOf(rec.renderers,rp);
    if(k<0){rec.renderers=rec.renderers.Append(rp).ToArray();rec.fields=rec.fields.Append(AssetDatabase.GetAssetPath(g.Field)).ToArray();k=rec.renderers.Length-1;}
    var source=AssetDatabase.LoadAssetAtPath<CompactGrassField266>(rec.fields[k]);if(source==null){lines.Add("skip "+rp+": source missing");continue;}
    string path=A297+"/Dressing/Grass297_"+source.name+".asset";var copy=AssetDatabase.LoadAssetAtPath<CompactGrassField266>(path);
    if(copy==null){copy=ScriptableObject.CreateInstance<CompactGrassField266>();AssetDatabase.CreateAsset(copy,path);}
    EditorUtility.CopySerialized(source,copy);copy.name=Path.GetFileNameWithoutExtension(path);copy.Count=0;
    foreach(var cell in copy.Cells)
    {
     if(cell==null)continue;var seeds=new List<CompactGrassField266.Seed>();
     foreach(var seed in cell.Seeds)
     {
      var p=seed.Position;var xz=new Vector2(p.x,p.z);
      if(cores.Any(b=>b.Contains(xz))){removed++;continue;}
      float y=field.Sample(p.x,p.z);if(Mathf.Abs(y-p.y)>.02f)moved++;var n=field.Normal(p.x,p.z);
      seeds.Add(new CompactGrassField266.Seed{Position=new Vector3(p.x,y,p.z),NormalXZ=new Vector2(n.x,n.z)});
     }
     cell.Seeds=seeds.ToArray();copy.Count+=seeds.Count;
     if(seeds.Count>0){var bounds=new Bounds(seeds[0].Position,Vector3.one*2);foreach(var seed in seeds)bounds.Encapsulate(seed.Position+Vector3.up);bounds.Expand(2);cell.Bounds=bounds;}
    }
    kept+=copy.Count;EditorUtility.SetDirty(copy);g.Field=copy;g.Invalidate();EditorUtility.SetDirty(g);lines.Add(rp+" -> "+path+" seeds="+copy.Count);
   }
   Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,JsonUtility.ToJson(rec,true));
   AssetDatabase.SaveAssets();Save292();
   return "grass re-seated on the #297 surface: kept "+kept+", moved "+moved+", removed inside "+cores.Length+" op areas "+removed+"\n"+string.Join("\n",lines);
  }
 }
}
