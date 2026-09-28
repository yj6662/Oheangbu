using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string Needles(){
   if(EditorApplication.isPlaying)throw new Exception("Edit required");
   string folder=TreeFolder+"/PhotorealPine";var ti=(TextureImporter)AssetImporter.GetAtPath(folder+"/base_color.png");ti.isReadable=true;ti.SaveAndReimport();var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/base_color.png");
   var root=PrefabUtility.LoadPrefabContents(folder+"/Tree.prefab");int tufts=0;
   try{
    var existing=root.transform.Find("Fine pine needles");if(existing!=null)Object.DestroyImmediate(existing.gameObject);
    var vertices=new List<Vector3>();var triangles=new List<int>();var cells=new HashSet<Vector3Int>();var random=new System.Random(227);
    foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){
     var source=filter.sharedMesh;var points=source.vertices;var uv=source.uv;var bounds=source.bounds;float height=bounds.size.y;if(uv.Length!=points.Length)continue;
     for(int attempt=0;attempt<points.Length&&tufts<5200;attempt++){
      int index=random.Next(points.Length);var p=points[index];var color=texture.GetPixelBilinear(uv[index].x,uv[index].y);
      if(p.y<bounds.min.y+height*.36f||color.g<color.r*.99f||color.g<color.b*1.12f)continue;
      var cell=Vector3Int.FloorToInt(p/(height*.0035f));if(!cells.Add(cell))continue;
      float length=height*.019f;tufts++;
      for(int n=0;n<28;n++){
       float angle=(float)random.NextDouble()*Mathf.PI*2;var direction=new Vector3(Mathf.Cos(angle),.15f+(float)random.NextDouble()*.75f,Mathf.Sin(angle)).normalized;
       var side=Vector3.Cross(direction,Vector3.up).normalized*length*.025f;var tip=p+direction*length*(.65f+(float)random.NextDouble()*.6f);
       int v=vertices.Count;vertices.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(p-side)));vertices.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(p+side)));vertices.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(tip)));triangles.Add(v);triangles.Add(v+1);triangles.Add(v+2);
      }
     }
    }
    if(tufts==0)throw new Exception("No foliage anchors detected");
    var mesh=new Mesh{name="Pine needle sprays",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var savedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/Needles.asset");if(savedMesh==null)AssetDatabase.CreateAsset(mesh,folder+"/Needles.asset");else{EditorUtility.CopySerialized(mesh,savedMesh);Object.DestroyImmediate(mesh);mesh=savedMesh;EditorUtility.SetDirty(mesh);}
    var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Needles.mat");bool newMat=mat==null;if(newMat)mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Pine needles",enableInstancing=true};mat.SetColor("_BaseColor",new Color(.24f,.31f,.16f));mat.SetFloat("_Smoothness",.1f);mat.SetFloat("_Cull",0);if(newMat)AssetDatabase.CreateAsset(mat,folder+"/Needles.mat");EditorUtility.SetDirty(mat);
    var go=new GameObject("Fine pine needles",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
    var group=root.GetComponent<LODGroup>();if(group==null)group=root.AddComponent<LODGroup>();var all=root.GetComponentsInChildren<Renderer>();var coarse=System.Array.FindAll(all,r=>r.gameObject!=go);group.SetLODs(new[]{new LOD(.12f,all),new LOD(.008f,coarse)});group.RecalculateBounds();
    PrefabUtility.SaveAsPrefabAsset(root,folder+"/Tree.prefab");AssetDatabase.SaveAssets();
   }finally{PrefabUtility.UnloadPrefabContents(root);ti.isReadable=false;ti.SaveAndReimport();}
   return "Meshy generated branches retained; authored needle tufts="+tufts;
  }
 }
}
