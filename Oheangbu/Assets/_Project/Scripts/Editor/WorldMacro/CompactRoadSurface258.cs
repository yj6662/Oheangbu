using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // Only terrain-following open ribbons use this helper; never closed rocks/buildings.
  static int UpwardRibbon258(Mesh mesh)
  {
   var vertices=mesh.vertices;var triangles=mesh.triangles;int changed=0;
   for(int i=0;i<triangles.Length;i+=3)
   {
    var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
    if(Vector3.Cross(b-a,c-a).y>=-1e-7f)continue;
    int swap=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=swap;changed++;
   }
   if(changed>0){mesh.triangles=triangles;mesh.RecalculateNormals();}
   return changed;
  }
  public static string RoadSurface258()
  {
   var scene=FrontageScene249();string folder=Path.GetDirectoryName(scene.path).Replace('\\','/');
   var paths=AssetDatabase.FindAssets("t:Mesh",new[]{folder+"/Village245",folder+"/Progression251"})
    .Select(AssetDatabase.GUIDToAssetPath).Where(p=>Path.GetFileName(p).StartsWith("Village_")||Path.GetFileName(p).StartsWith("Yard_")||Path.GetFileName(p).EndsWith("251.asset")).ToArray();
   var colliders=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(true)).ToArray();
   var report=new List<string>();int total=0;
   Directory.CreateDirectory(ContinuationOutput258+"/RoadBackup");
   if(!File.Exists(Path.Combine(Path.GetDirectoryName(ContinuationOutput258),"Frontage249/surface258-before_court.png")))FrontageCapture249("surface258-before");
   foreach(var path in paths)
   {
    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null)continue;
    if(colliders.Any(c=>c.sharedMesh==mesh))throw new Exception("Expected visual-only ribbon; collider references "+path);
    string backup=ContinuationOutput258+"/RoadBackup/"+Path.GetFileName(path);
    if(!File.Exists(backup)){File.Copy(path,backup);File.Copy(path+".meta",backup+".meta");}
    var before=mesh.vertices;int count=UpwardRibbon258(mesh);total+=count;
    if(!before.SequenceEqual(mesh.vertices))throw new Exception("Ribbon vertices changed");
    if(count>0)EditorUtility.SetDirty(mesh);
    report.Add("PASS "+Path.GetFileName(path)+": reversed="+count+"; vertices/UV/world placement/colliders unchanged");
   }
   foreach(var filter in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)))
   {
    if(filter.sharedMesh==null||!paths.Contains(AssetDatabase.GetAssetPath(filter.sharedMesh)))continue;
    var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null)continue;
    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;EditorUtility.SetDirty(renderer);
    report.Add("PASS visual road receives shadows without casting a raised ribbon edge: "+filter.name);
   }
   AssetDatabase.SaveAssets();
   UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
   UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
   FrontageCapture249("surface258-after");
   report.Add("Total downward triangles corrected="+total);
   File.WriteAllText(ContinuationOutput258+"/road-surface-fix.txt",string.Join("\n",report));
   return string.Join("\n",report);
  }
 }
}
