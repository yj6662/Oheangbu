using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // [SPEC-WORLD-REALM-FLORA-308 3.6] New assets -> prefabs the sheet tool can turn into prototypes (made[]).
 //   intake-plan | intake      every FBX under Assets/_Project/Art/World/Flora308/CC0 (three meshes <name>_LOD0..2, origin at the base centre,
 //                             written by the Blender export of 2026-10-11) with <set>_albedo.png / <set>_normal.png next to it
 // For each: importer settings (no materials, no collider, not readable), one WorldMacroVegetation material per texture set, and a prefab
 // with a LODGroup. Rocks, props and shrubs get TWO levels (the renderer draws a non-tree kind with a near and a far level only);
 // trees get three. The list of made[] entries goes to Tools/Unity/Stage308_flora/Out/made_cc0.json for the planner.
 // Writes only under Assets/_Project/Art/World/Flora308 (SaveAssetIfDirty / importer.SaveAndReimport), no scene.
 public static partial class CompactRebuildAuthoring
 {
  const string Cc0308F=A308F+"/CC0";
  [Serializable] sealed class MadeList308F{public List<Made308F> made=new List<Made308F>();}

  static Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind IntakeKind308F(string name)
  {
   if(name.StartsWith("fern_")||name.StartsWith("wild_rooibos")||name.StartsWith("dry_branches"))return Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Shrub;
   if(name.StartsWith("rock_")||name.StartsWith("boulder_"))return Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Rock;
   if(name.Contains("snag")||name.Contains("_tree_standing"))return Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree;
   return Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Prop;   // logs, stumps, roots
  }

  static string Intake308F(bool dry)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   if(!AssetDatabase.IsValidFolder(Cc0308F))throw new Refuse305("no folder "+Cc0308F);
   var shader=Shader.Find("Oheangbu/WorldMacroVegetation")??throw new Refuse305("shader Oheangbu/WorldMacroVegetation not found");
   var fbx=AssetDatabase.FindAssets("t:Model",new[]{Cc0308F}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p).ToArray();
   var albedo=AssetDatabase.FindAssets("t:Texture2D",new[]{Cc0308F}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith("_albedo.png",StringComparison.Ordinal)).ToArray();
   var sets=albedo.ToDictionary(p=>Path.GetFileName(p).Replace("_albedo.png",""),p=>p);
   var sb=new StringBuilder("Flora308 intake"+(dry?" plan":"")+" | models "+fbx.Length+" | texture sets "+sets.Count+"\n");var list=new MadeList308F();int problems=0;
   if(!dry){DevSceneKit.EnsureFolder(A308F+"/Materials");DevSceneKit.EnsureFolder(A308F+"/Prefabs");}
   foreach(string path in fbx)
   {
    string name=Path.GetFileNameWithoutExtension(path);var kind=IntakeKind308F(name);
    string set=sets.Keys.Where(k=>name==k||name.StartsWith(k+"_",StringComparison.Ordinal)).OrderByDescending(k=>k.Length).FirstOrDefault();
    if(set==null){sb.AppendLine("  PROBLEM "+name+": no <set>_albedo.png matches");problems++;continue;}
    if(dry){sb.AppendLine("  "+kind.ToString().PadRight(6)+name.PadRight(32)+" set "+set);continue;}

    if(AssetImporter.GetAtPath(path) is ModelImporter mi&&(mi.materialImportMode!=ModelImporterMaterialImportMode.None||mi.addCollider||mi.isReadable||mi.importNormals!=ModelImporterNormals.Calculate||mi.meshCompression!=ModelImporterMeshCompression.Medium))
    {
     mi.materialImportMode=ModelImporterMaterialImportMode.None;mi.addCollider=false;mi.isReadable=false;mi.importNormals=ModelImporterNormals.Calculate;mi.normalSmoothingAngle=60;
     mi.meshCompression=ModelImporterMeshCompression.Medium;mi.importAnimation=false;mi.importCameras=false;mi.importLights=false;mi.SaveAndReimport();
    }
    string normalPath=sets[set].Replace("_albedo.png","_normal.png");bool hasNormal=File.Exists(normalPath);
    var ti=AssetImporter.GetAtPath(sets[set]) as TextureImporter;bool clipped=ti!=null&&ti.DoesSourceTextureHaveAlpha();
    if(ti!=null&&(ti.maxTextureSize!=1024||ti.alphaIsTransparency!=clipped||ti.isReadable||(clipped&&!ti.mipMapsPreserveCoverage)))
    {ti.maxTextureSize=1024;ti.sRGBTexture=true;ti.alphaIsTransparency=clipped;ti.isReadable=false;ti.mipmapEnabled=true;ti.mipMapsPreserveCoverage=clipped;ti.alphaTestReferenceValue=.3f;ti.SaveAndReimport();}
    if(hasNormal&&AssetImporter.GetAtPath(normalPath) is TextureImporter ni&&(ni.textureType!=TextureImporterType.NormalMap||ni.maxTextureSize!=1024))
    {ni.textureType=TextureImporterType.NormalMap;ni.maxTextureSize=1024;ni.isReadable=false;ni.mipmapEnabled=true;ni.SaveAndReimport();}

    var meshes=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToDictionary(m=>m.name,m=>m);
    // Blender renames a copy to "<name>_LOD0.001" when the source object already carries that name
    Mesh Level(int i)=>meshes.TryGetValue(name+"_LOD"+i,out var m)?m:meshes.Where(kv=>kv.Key.StartsWith(name+"_LOD"+i+".",StringComparison.Ordinal)).Select(kv=>kv.Value).FirstOrDefault();
    var l0=Level(0);var l1=Level(1);var l2=Level(2);
    if(l0==null||l2==null){sb.AppendLine("  PROBLEM "+name+": meshes "+string.Join(",",meshes.Keys)+" (want "+name+"_LOD0..2)");problems++;continue;}
    var size=l0.bounds.size;
    string matPath=A308F+"/Materials/CC0_"+set+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);bool create=mat==null;
    if(create)mat=new Material(shader);
    mat.shader=shader;mat.enableInstancing=true;mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(sets[set]));
    mat.SetTexture("_BumpMap",hasNormal?AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath):null);mat.SetFloat("_BumpScale",hasNormal?1:0);
    mat.SetFloat("_AlphaClip",clipped?1:0);mat.SetFloat("_Cutoff",.3f);mat.SetFloat("_Cull",clipped?0:2);mat.SetColor("_BaseColor",Color.white);
    mat.SetFloat("_Saturation",.4f);mat.SetFloat("_AmbientFloor",.36f);mat.SetFloat("_LightResponse",.65f);mat.SetFloat("_Height",Mathf.Max(.3f,size.y));
    mat.SetFloat("_WindAmplitude",clipped?.03f:0);mat.SetFloat("_WindSpeed",.85f);mat.SetFloat("_Billboard",0);
    // the renderer overrides the fade per batch (Range): these only matter when a material is drawn outside it
    mat.SetFloat("_FadeInStart",-1);mat.SetFloat("_FadeInEnd",0);mat.SetFloat("_FadeOutStart",680);mat.SetFloat("_FadeOutEnd",700);
    if(create)AssetDatabase.CreateAsset(mat,matPath);else EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);

    var levels=kind==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree?new[]{l0,l1??l2,l2}:new[]{l0,l2};
    var root=new GameObject(name);var lods=new List<LOD>();
    try
    {
     for(int i=0;i<levels.Length;i++)
     {
      var child=new GameObject(name+"_L"+i);child.transform.SetParent(root.transform,false);
      child.AddComponent<MeshFilter>().sharedMesh=levels[i];var r=child.AddComponent<MeshRenderer>();
      r.sharedMaterials=Enumerable.Repeat(mat,Mathf.Max(1,levels[i].subMeshCount)).ToArray();
      lods.Add(new LOD(i==0?.25f:i==levels.Length-1?.01f:.08f,new Renderer[]{r}));
     }
     var group=root.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();
     string prefabPath=A308F+"/Prefabs/"+name+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
     list.made.Add(new Made308F{id="CC0_"+name,prefab=prefabPath,category=kind.ToString(),radius=Mathf.Max(.15f,Mathf.Max(size.x,size.z)*.5f)});
     sb.AppendLine("  "+kind.ToString().PadRight(6)+name.PadRight(32)+" tris "+(l0.triangles.Length/3)+" / "+(l2.triangles.Length/3)+" | size "+F305(size.x)+" x "+F305(size.y)+" x "+F305(size.z)+(clipped?" | clipped":"")+" | set "+set);
    }
    finally{Object.DestroyImmediate(root);}
   }
   if(dry)return sb+"intake plan: "+fbx.Length+" models, problems "+problems;
   string outDir=Path.Combine(Stage308F,"Out");Directory.CreateDirectory(outDir);File.WriteAllText(Path.Combine(outDir,"made_cc0.json"),JsonUtility.ToJson(list,true));
   return sb+"intake: "+list.made.Count+" prefabs under "+A308F+"/Prefabs, problems "+problems+" | made[] list "+Path.Combine(outDir,"made_cc0.json");
  }
 }
}
