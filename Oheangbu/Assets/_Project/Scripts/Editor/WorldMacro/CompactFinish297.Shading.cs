using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 shading pass: the world's custom shaders ignored the ambient probe (flat `max(SH, floor)` with floor .4) and SSAO.
 // Candidate-only derivatives (Finish297/Shaders) use the realm SH gradient + screen AO; only #296 private materials
 // (Architecture296/Materials/**) are switched. Original shader + values are recorded once; shading-revert restores them.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class ShadingRecord297{public string path,shader;public string[] floats;public float[] values;}
  [Serializable] class ShadingOriginal297{public ShadingRecord297[] materials;}
  // from shader -> to shader, float overrides applied after the swap
  static readonly (string from,string to,(string name,float value)[] set)[] Swaps297={
   ("Oheangbu/Reworld292/KoreanArchitecture","Oheangbu/Finish297/KoreanArchitecture",new[]{("_AmbientFloor",.07f),("_AmbientScale297",1.35f),("_InkMixNear297",.30f),("_InkMixFar297",.55f),("_AOStrength297",1f)}),
  };

  static string Shading297(bool revert)
  {
   RequireClean292();string file=K297+"/shading-original.json";var scene=SceneManager.GetActiveScene();
   if(revert)
   {
    var o=JsonUtility.FromJson<ShadingOriginal297>(File.ReadAllText(file));int n=0;
    foreach(var r in o.materials){var m=AssetDatabase.LoadAssetAtPath<Material>(r.path);if(m==null)continue;var sh=Shader.Find(r.shader);if(sh!=null)m.shader=sh;for(int i=0;i<r.floats.Length;i++)if(m.HasProperty(r.floats[i]))m.SetFloat(r.floats[i],r.values[i]);EditorUtility.SetDirty(m);n++;}
    AssetDatabase.SaveAssets();return "shading reverted on "+n+" materials";
   }
   var mats=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader!=null).Distinct()
    .Where(m=>AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Project/Art/World/Architecture296/Materials",StringComparison.Ordinal)).ToArray();
   var records=File.Exists(file)?JsonUtility.FromJson<ShadingOriginal297>(File.ReadAllText(file)).materials.ToList():new List<ShadingRecord297>();
   var known=new HashSet<string>(records.Select(r=>r.path));var report=new List<string>();
   foreach(var swap in Swaps297)
   {
    var to=Shader.Find(swap.to);if(to==null)throw new Exception("missing shader "+swap.to);int n=0;
    foreach(var m in mats.Where(m=>m.shader.name==swap.from||m.shader==to))
    {
     string path=AssetDatabase.GetAssetPath(m);
     if(!known.Contains(path)){records.Add(new ShadingRecord297{path=path,shader=m.shader.name,floats=swap.set.Select(s=>s.name).ToArray(),values=swap.set.Select(s=>m.HasProperty(s.name)?m.GetFloat(s.name):0f).ToArray()});known.Add(path);}
     if(m.shader!=to)m.shader=to;foreach(var (name,value) in swap.set)if(m.HasProperty(name))m.SetFloat(name,value);EditorUtility.SetDirty(m);n++;
    }
    report.Add(swap.from+" -> "+swap.to+": "+n+" materials");
   }
   File.WriteAllText(file,JsonUtility.ToJson(new ShadingOriginal297{materials=records.ToArray()},true));AssetDatabase.SaveAssets();
   string text=string.Join("\n",report);File.WriteAllText(K297+"/shading.txt",text);return text;
  }
 }
}
