using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  static Mesh PartMesh279(Sheet.Part part){
   string path=A278+"/Meshes/Part279_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(part.Mesh))+"_"+part.Submesh+".asset";
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(mesh==null){mesh=UnityEngine.Object.Instantiate(part.Mesh);var triangles=part.Mesh.GetTriangles(part.Submesh);mesh.subMeshCount=1;mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}return mesh;
  }
  static bool Ground279(float x,float z,out RaycastHit hit){
   var hits=Physics.RaycastAll(new Vector3(x,450,z),Vector3.down,700).Where(h=>h.collider.name.StartsWith("Cliff_")||h.collider.name=="Trail").OrderByDescending(h=>h.point.y).ToArray();
   hit=hits.Length>0?hits[0]:default;return hits.Length>0;
  }
  static void Plant279(Sheet.Prototype prototype,Transform parent,Vector3 position,float scale,float yaw,Dictionary<Material,Material> cache){
   var tree=new GameObject(prototype.Id+"_ledge");tree.transform.SetParent(parent,false);tree.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));tree.transform.localScale=Vector3.one*scale;
   var levels=new List<LOD>();
   for(int l=0;l<Mathf.Min(3,prototype.Lods.Length);l++){
    var go=new GameObject("LOD"+l);go.transform.SetParent(tree.transform,false);var rr=new List<Renderer>();
    foreach(var part in prototype.Lods[l].Parts){var child=MeshObject278("Canopy_and_bark",PartMesh279(part),CloneProp278(part.Material,cache),go.transform,false);child.transform.localPosition=part.Local.GetColumn(3);child.transform.localRotation=part.Local.rotation;child.transform.localScale=part.Local.lossyScale;rr.Add(child.GetComponent<Renderer>());}
    levels.Add(new LOD(l==0?.065f:l==1?.018f:.003f,rr.ToArray()));
   }
   var lod=tree.AddComponent<LODGroup>();lod.SetLODs(levels.ToArray());lod.RecalculateBounds();
  }
  static void DressAscent279(Transform root,Sheet sheet,Dictionary<Material,Material> cache){
   var trees=new GameObject("Ledge_groves_279");trees.transform.SetParent(root,false);
   var pine=sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_PinusDensiflora_Spring_2");var oldPine=sheet.Prototypes.Single(p=>p.Id=="Meshy_Pinus");var shrub=sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_UlmusDavidiana_Summer_2");
   var rng=new System.Random(279);float R(float lo,float hi)=>Mathf.Lerp(lo,hi,(float)rng.NextDouble());
   int treeCount=0;
   foreach(float anchor in new[]{12f,39,68,112,146,188,227,268,306}){
    for(int j=0;j<11;j++){
     float z=anchor+R(-10,10);var p=RouteAt278(z);float offset=j<6?R(-15,-6.8f):R(17,51);
     if(!Ground279(p.x+offset,z,out var h)||h.normal.y<.38f)continue;
     bool meshy=j==3;Plant279(meshy?oldPine:pine,trees.transform,h.point-Vector3.up*.12f,meshy?R(.7f,.95f):R(1.25f,2.15f),R(0,360),cache);treeCount++;
    }
   }
   // Low woody clumps occupy sheltered benches, not a row on every road edge.
   for(int i=0;i<132;i++){float z=R(5,328);var p=RouteAt278(z);float off=i%3==0?R(8,26):R(-9,-4.3f);if(Ground279(p.x+off,z,out var h)&&h.normal.y>.48f)Plant279(shrub,trees.transform,h.point-Vector3.up*.04f,R(.10f,.25f),R(0,360),cache);}
   var fern=sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_Deparia_1");var grass=sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_Grass");
   int coverCount=0;
   for(int cell=0;cell<12;cell++){
    var packet=new GameObject("Groundcover_packet_"+cell);packet.transform.SetParent(root,false);var levels=new List<LOD>();var samples=new List<(Sheet.Prototype species,Matrix4x4 matrix)>();
    for(int j=0;j<110;j++){
     float z=cell*27.5f+R(0,27.5f);var p=RouteAt278(z);float off=j%2==0?R(-7.5f,-3.55f):R(3.65f,8.5f);
     if(!Ground279(p.x+off,z,out var h)||h.normal.y<.48f)continue;
     var species=j%3==0?fern:grass;float scale=species==fern?R(.65f,1.7f):R(.45f,1.0f);
     samples.Add((species,Matrix4x4.TRS(h.point-Vector3.up*.03f,Quaternion.Euler(0,R(0,360),0),Vector3.one*scale)));coverCount++;
    }
    for(int level=0;level<2;level++){
     var batches=new Dictionary<Material,List<CombineInstance>>();
     // Camera-facing source cards cannot be merged into one world-space packet:
     // their vertex shader would rotate the entire cluster around the packet origin.
     // The distant level instead keeps half of the real, rooted plant meshes.
     int sampleIndex=0;
     foreach(var sample in samples){if(level>0&&(sampleIndex++%2)!=0)continue;foreach(var part in sample.species.Lods[0].Parts){var source=CloneProp278(part.Material,cache);if(!batches.TryGetValue(source,out var instances)){instances=new List<CombineInstance>();batches[source]=instances;}instances.Add(new CombineInstance{mesh=PartMesh279(part),transform=sample.matrix*part.Local,subMeshIndex=0});}}
     var renderers=new List<Renderer>();int batch=0;
     foreach(var entry in batches){var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(entry.Value.ToArray(),true,true);mesh.RecalculateBounds();mesh=ArtMesh(mesh,A278+"/Meshes/Cover279_"+cell+"_"+level+"_"+batch+".asset");
      var mat=new Material(entry.Key);mat.SetFloat("_WindAmplitude",0);mat.SetFloat("_FadeOutStart",95);mat.SetFloat("_FadeOutEnd",140);mat=SurveyMaterial(mat,A278+"/Materials/Cover279_"+cell+"_"+level+"_"+batch+".mat");
      var go=MeshObject278("Fern_grass_LOD"+level,mesh,mat,packet.transform,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;renderers.Add(go.GetComponent<Renderer>());batch++;
     }
     levels.Add(new LOD(level==0?.12f:.012f,renderers.ToArray()));
    }
    var group=packet.AddComponent<LODGroup>();group.SetLODs(levels.ToArray());group.RecalculateBounds();
   }
   System.IO.Directory.CreateDirectory("../Art/World/Compact/Rebuild/Ascent278");System.IO.File.WriteAllText("../Art/World/Compact/Rebuild/Ascent278/dressing.txt",$"Pines={treeCount}; cover plants={coverCount}; 12 spatial packets, 2 cover LODs; no individual cover colliders; source-colour materials retained");
  }
 }
}
