using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.Prologue
{
 public static class PrologueSceneRefinement
 {
  public static string Apply()
  {
   if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=PrologueBuilder.ScenePath)throw new Exception("Production edit scene required");
   PrologueBuilder.Layout();int moved=0,removed=0;
   var valley=GameObject.Find("Valley");var mesh=valley.GetComponent<MeshFilter>().sharedMesh;
   // CopySerialized can leave the old native vertex buffer attached to a Mesh.
   // Reupload the serialized geometry explicitly without replacing the asset GUID.
   var rebuilt=PrologueBuilder.BuildGround();var vertices=rebuilt.vertices;var normals=rebuilt.normals;var uv=rebuilt.uv;var triangles=rebuilt.triangles;Object.DestroyImmediate(rebuilt);
   mesh.Clear();mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);
   valley.GetComponent<MeshCollider>().sharedMesh=null;valley.GetComponent<MeshCollider>().sharedMesh=mesh;
   var root=GameObject.Find(CodexRockKit.RootName);
   if(root!=null)foreach(Transform t in root.transform){
    if(t.name.StartsWith("Mine_"))continue;
    Vector3 p=t.position;float size=t.localScale.x;
    p.y=PrologueBuilder.Height(p.x,p.z)-size*.11f;t.position=p;moved++;
   }
   Physics.SyncTransforms();
   if(root!=null)foreach(Transform t in root.transform){
    if(t.name.StartsWith("Mine_"))continue;
    bool obstruct=t.GetComponentsInChildren<Renderer>().Any(r=>PrologueBuilder.Main.Concat(PrologueBuilder.Branch).Any(p=>r.bounds.Intersects(new Bounds(new Vector3(p.x,PrologueBuilder.Height(p.x,p.z)+1,p.z),new Vector3(5,2,5)))));
    if(obstruct){t.gameObject.SetActive(false);removed++;}
   }
   var ground=AssetDatabase.LoadAssetAtPath<Material>(PrologueBuilder.Folder+"/Ground.mat");
   var terrainShader=Shader.Find("Oheangbu/PrologueTerrain");if(terrainShader!=null)ground.shader=terrainShader;
   ground.SetFloat("_BrushStrength",.1f);ground.SetFloat("_StrokeStrength",.12f);ground.SetFloat("_NoiseStrength",.065f);ground.SetFloat("_WashStrength",.4f);EditorUtility.SetDirty(ground);
   if(GameObject.Find("Prologue_South_Ridges")==null){
    var originals=CodexMountainLOD.Originals();var far=new GameObject("Prologue_South_Ridges");
    for(int i=0;i<Mathf.Min(3,originals.Length);i++){
     var source=originals[i];var copy=Object.Instantiate(source.gameObject,far.transform);copy.name="Southern_Ridge_"+i;
     var r=copy.GetComponent<MeshRenderer>();Vector3 center=new Vector3(-210+i*185,source.bounds.size.y*.38f,-220-Mathf.Abs(i-1)*70);
     copy.transform.position+=center-r.bounds.center;
     foreach(var collider in copy.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
    }
   }
   // Sparse clusters beside bends, copied from the approved C2 pine kit.
   if(GameObject.Find("Prologue_Trail_Pines")==null){
    var source=GameObject.Find("03_Windswept_Pines");var candidates=source!=null?source.transform.Cast<Transform>().Where(t=>t.GetComponentInChildren<MeshRenderer>()!=null).ToArray():Array.Empty<Transform>();
    var group=new GameObject("Prologue_Trail_Pines");
    if(candidates.Length>0)for(int i=15;i<PrologueBuilder.Main.Length-12;i+=13){
     var p=PrologueBuilder.Main[i];var tangent=(PrologueBuilder.Main[i+1]-p).normalized;var side=Vector3.Cross(Vector3.up,tangent)*(i%2==0?8:-9);
     for(int j=0;j<2;j++){Vector3 q=p+side+new Vector3(j*2.5f,0,j*2);q.y=PrologueBuilder.Height(q.x,q.z);var tree=Object.Instantiate(candidates[(i+j)%candidates.Length].gameObject,group.transform);tree.SetActive(true);tree.transform.position=q;tree.transform.Rotate(0,(i*17+j*63)%360,0);}
    }
   }
   var nav=Object.FindFirstObjectByType<NavMeshSurface>();nav.BuildNavMesh();var path=PrologueBuilder.Folder+"/Navigation_Refined.asset";
   var savedNav=AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path);
   if(savedNav==null){savedNav=Object.Instantiate(nav.navMeshData);AssetDatabase.CreateAsset(savedNav,path);}
   else {EditorUtility.CopySerialized(nav.navMeshData,savedNav);EditorUtility.SetDirty(savedNav);}
   nav.RemoveData();nav.navMeshData=savedNav;nav.AddData();
   var session=Object.FindFirstObjectByType<Oheangbu.App.Prologue.PrologueSession>();
   var preview=session.Content.Points.First(p=>p.Id=="GukPreview");preview.Position=new Vector3(-111,PrologueBuilder.Height(-111,121),121);EditorUtility.SetDirty(session.Content);
   var timber=AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder+"/Materials/Timber.mat");
   foreach(string name in new[]{"Broken_Mine_Beam","Lost_Worker_Satchel","Preview_High_Shelf","Preview_Shelf_Support"}){var go=GameObject.Find(name);if(go!=null)go.GetComponent<Renderer>().sharedMaterial=timber;}
   if(GameObject.Find("Prologue_Shallow_Stream")==null){
    var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/SpellVFX120/AreaFive/PolyOneWave.mat");
    if(source!=null){
     var material=new Material(source){name="Prologue Shallow Stream"};material.SetColor("_Shallow_Water_Color",new Color(.19f,.24f,.23f,.6f));material.SetColor("_Deep_Water_Color",new Color(.10f,.13f,.12f,.65f));material.SetFloat("_Amplitude_Wave",0);material.SetFloat("_Max_Depth",.3f);material.SetFloat("_Refration_Intensity",.008f);
     string materialPath=PrologueBuilder.Folder+"/Stream.mat";var old=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(old!=null){old.CopyPropertiesFromMaterial(material);Object.DestroyImmediate(material);material=old;}else AssetDatabase.CreateAsset(material,materialPath);
     var stream=new GameObject("Prologue_Shallow_Stream");var points=PrologueBuilder.Branch.Skip(12).Take(31).Reverse().Select(p=>p+Vector3.right*2.8f).ToArray();
     var v=new Vector3[points.Length*2];var texcoords=new Vector2[v.Length];var indices=new List<int>();
     for(int i=0;i<points.Length;i++){float width=.6f*Mathf.Sin(Mathf.PI*i/(points.Length-1));for(int side=0;side<2;side++){var p=points[i]+Vector3.right*(side==0?-width:width);p.y=PrologueBuilder.Height(p.x,p.z)+.012f;v[i*2+side]=p;texcoords[i*2+side]=new Vector2(side,i*.3f);}if(i>0){int k=i*2;indices.AddRange(new[]{k-2,k,k-1,k-1,k,k+1});}}
     for(int i=0;i<indices.Count;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}
     var waterMesh=new Mesh{name="Prologue shallow stream"};waterMesh.vertices=v;waterMesh.uv=texcoords;waterMesh.triangles=indices.ToArray();waterMesh.RecalculateNormals();waterMesh.RecalculateBounds();AssetDatabase.CreateAsset(waterMesh,PrologueBuilder.Folder+"/StreamMesh.asset");
     stream.AddComponent<MeshFilter>().sharedMesh=waterMesh;stream.AddComponent<MeshRenderer>().sharedMaterial=material;stream.AddComponent<Oheangbu.App.Prologue.PrologueStreamTime>();
     var cameraData=Camera.main.GetUniversalAdditionalCameraData();cameraData.requiresDepthTexture=true;cameraData.requiresColorTexture=true;
     var sheet=AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.AreaSheetSO>(PrologueBuilder.Folder+"/AreaSheet.asset");PrologueBuilder.Set(sheet,"streams",new[]{new Oheangbu.Data.World.AreaSheetSO.StreamSpec{id="ShallowRewardStream",knots=points,width=1.2f}});
    }
   }
   System.IO.File.WriteAllText(PrologueBuilder.Output+"/layout.json",JsonUtility.ToJson(session.Content,true));
   var floating=new List<string>();var surface=valley.GetComponent<MeshCollider>();
   if(root!=null)foreach(Transform t in root.transform){if(!t.gameObject.activeSelf||t.name.StartsWith("Mine_"))continue;var r=t.GetComponentInChildren<Renderer>();if(r==null)continue;var p=r.bounds.center;if(surface.Raycast(new Ray(p+Vector3.up*150,Vector3.down),out var hit,400)&&r.bounds.min.y-hit.point.y>1)floating.Add(t.name+" base="+r.bounds.min.y+" actual="+hit.point.y+" analytic="+PrologueBuilder.Height(p.x,p.z)+" pivot="+t.position);}
   EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Regrounded "+moved+"; cleared "+removed+"; Valley transform="+valley.transform.position+" scale="+valley.transform.lossyScale+" Floating:\n"+string.Join("\n",floating);
  }
 }
}
