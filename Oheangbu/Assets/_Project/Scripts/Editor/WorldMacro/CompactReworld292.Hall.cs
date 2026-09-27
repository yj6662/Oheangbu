using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class HallMaterials292{public string[] paths;}
  static string ExportHall292()
  {
   var hall=GameObject.Find("Reworld292_Sansa").transform.GetChild(0).Find("KoreanHall_SourceAdaptation");
   string folder=O292+"/HallLOD";Directory.CreateDirectory(folder);var mats=new List<Material>();int count=0,offset=1;
   using(var writer=new StreamWriter(folder+"/hall.obj",false,new UTF8Encoding(false)))
   {
    writer.WriteLine("mtllib hall.mtl");
    foreach(var f in hall.GetComponentsInChildren<MeshFilter>())
    {
     var mesh=f.sharedMesh;if(mesh==null)continue;var matrix=hall.worldToLocalMatrix*f.transform.localToWorldMatrix;var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var material=f.GetComponent<Renderer>().sharedMaterials;
     writer.WriteLine("o part_"+count++);
     string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
     foreach(var v in vertices){var p=matrix.MultiplyPoint3x4(v);writer.WriteLine("v "+F(p.x)+" "+F(p.y)+" "+F(p.z));}
     for(int i=0;i<vertices.Length;i++){var p=uv.Length>i?uv[i]:Vector2.zero;writer.WriteLine("vt "+F(p.x)+" "+F(p.y));}
     for(int i=0;i<vertices.Length;i++){var n=normals.Length>i?matrix.MultiplyVector(normals[i]).normalized:Vector3.up;writer.WriteLine("vn "+F(n.x)+" "+F(n.y)+" "+F(n.z));}
     for(int sub=0;sub<mesh.subMeshCount;sub++)
     {var mat=material[Mathf.Min(sub,material.Length-1)];int id=mats.IndexOf(mat);if(id<0){id=mats.Count;mats.Add(mat);}writer.WriteLine("usemtl m_"+id);var t=mesh.GetTriangles(sub);
      for(int i=0;i<t.Length;i+=3){string V(int k){int n=t[k]+offset;return n+"/"+n+"/"+n;}writer.WriteLine("f "+V(i)+" "+V(i+1)+" "+V(i+2));}}
     offset+=vertices.Length;
    }
   }
   File.WriteAllText(folder+"/hall.mtl",string.Join("\n",mats.Select((m,i)=>"newmtl m_"+i+"\nKd 1 1 1\n")));
   File.WriteAllText(folder+"/materials.json",JsonUtility.ToJson(new HallMaterials292{paths=mats.Select(AssetDatabase.GetAssetPath).ToArray()},true));
   return "Exported original-proportion Korean hall "+count+" mesh parts for offline LOD reduction";
  }
  static string HallLod292()
  {
   string path=A292+"/Architecture/HallLOD.obj";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Exception("Blender LOD required");
   var materials=JsonUtility.FromJson<HallMaterials292>(File.ReadAllText(O292+"/HallLOD/materials.json")).paths.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
   foreach(Transform temple in GameObject.Find("Reworld292_Sansa").transform)
   {
    var hall=temple.Find("KoreanHall_SourceAdaptation");var previous=temple.Find("KoreanHall_Distant");if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
    var lod=(GameObject)PrefabUtility.InstantiatePrefab(prefab,temple.gameObject.scene);lod.name="KoreanHall_Distant";lod.transform.SetParent(temple,false);lod.transform.SetPositionAndRotation(hall.position,hall.rotation);lod.transform.localScale=hall.localScale;
    foreach(var r in lod.GetComponentsInChildren<Renderer>())r.sharedMaterials=r.sharedMaterials.Select(m=>{string id=m.name.Split(' ')[0].Replace("m_","");return int.TryParse(id,out int index)&&index<materials.Length?materials[index]:materials[0];}).ToArray();
    var group=hall.GetComponent<LODGroup>();if(group==null)group=hall.gameObject.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.14f,hall.GetComponentsInChildren<Renderer>()),new LOD(.004f,lod.GetComponentsInChildren<Renderer>())});group.RecalculateBounds();
   }
   Save292();return "Five halls share one reduced Blender LOD asset; transition/play validation pending";
  }
 }
}
