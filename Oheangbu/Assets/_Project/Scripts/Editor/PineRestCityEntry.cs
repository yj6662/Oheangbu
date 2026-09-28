using System;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools
{
 public static partial class PineRestGameBuilder
 {
  static string CityEntry()
  {
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   var session=Object.FindFirstObjectByType<PrologueSession>();
   var road=GameObject.Find("Capital road ground").GetComponent<MeshCollider>();
   if(!road.Raycast(new Ray(new Vector3(270,150,-880),Vector3.down),out var hit,300))throw new Exception("Gate ground absent");
   float level=hit.point.y, start=road.bounds.min.z+.1f, end=-1120;
   if(start<end+80)throw new Exception("Unexpected existing road bounds");
   var old=GameObject.Find("Journey City Entry");if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject("Journey City Entry").transform;
   Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/"+name+".mat");
   GameObject Box(string name,Vector3 center,Vector3 size,Material mat,bool collision=true)
   {
    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root);go.transform.position=center;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
   }
   var mesh=new Mesh{name="Journey city entry ground"};
   int nx=190,nz=Mathf.CeilToInt((start-end)/2);var vertices=new Vector3[(nx+1)*(nz+1)];var triangles=new int[nx*nz*6];int t=0;
   for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
   {
    float px=80+x*2,pz=Mathf.Lerp(start,end,(float)z/nz);
    float channel=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(5,9,Mathf.Abs(pz+990)));
    vertices[z*(nx+1)+x]=new Vector3(px,level-channel*1.5f,pz);
    if(x==nx||z==nz)continue;int a=z*(nx+1)+x,b=a+nx+1;
    triangles[t++]=a;triangles[t++]=a+1;triangles[t++]=b;triangles[t++]=a+1;triangles[t++]=b+1;triangles[t++]=b;
   }
   mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
   string meshPath=Folder+"/CityEntryGround.asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
   if(saved==null){AssetDatabase.CreateAsset(mesh,meshPath);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
   var floor=new GameObject("City entry ground",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));floor.transform.SetParent(root);floor.GetComponent<MeshFilter>().sharedMesh=saved;floor.GetComponent<MeshCollider>().sharedMesh=saved;var groundPath=Folder+"/CityGround.mat";var groundMat=AssetDatabase.LoadAssetAtPath<Material>(groundPath);
   if(groundMat==null){groundMat=new Material(Mat("Path"));AssetDatabase.CreateAsset(groundMat,groundPath);}
   groundMat.SetFloat("_NoiseStrength",.025f);groundMat.SetFloat("_StrokeStrength",.025f);groundMat.SetFloat("_BrushStrength",.025f);EditorUtility.SetDirty(groundMat);floor.GetComponent<MeshRenderer>().sharedMaterial=groundMat;
   var water=Box("Open city stream",new Vector3(270,level-1.15f,-990),new Vector3(378,.025f,10),AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/WorldMacro/MacroWater.mat"),false);
   water.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   Box("Main stone bridge",new Vector3(270,level-.2f,-990),new Vector3(10,.4f,20),Mat("PaleStone"));
   for(int side=-1;side<=1;side+=2){Box("Bridge parapet",new Vector3(270+side*4.8f,level+.45f,-990),new Vector3(.4f,.9f,20),Mat("Granite"));}
   Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
   void Building(string name,string prefab,Vector3 feet,float height,float yaw)
   {
    var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab),root);go.name=name;go.transform.rotation=Quaternion.Euler(0,yaw,0);var b=BoundsOf(go);go.transform.localScale*=height/b.size.y;b=BoundsOf(go);go.transform.position+=feet-new Vector3(b.center.x,b.min.y,b.center.z);
    if(go.GetComponentsInChildren<Collider>().Length==0){b=BoundsOf(go);var c=go.AddComponent<BoxCollider>();c.center=go.transform.InverseTransformPoint(b.center);c.size=new Vector3(b.size.x/go.transform.lossyScale.x,b.size.y/go.transform.lossyScale.y,b.size.z/go.transform.lossyScale.z);}
   }
   string house="Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab";
   for(int i=0;i<2;i++)
   {
    Building("Western market house "+i,"Assets/HwaseongHaenggung/Prefabs/SM_Bokgunyeong.prefab",new Vector3(247,level,-924-i*34),6.5f,90);
    Building("Eastern market house "+i,"Assets/HwaseongHaenggung/Prefabs/SM_Namgunyeong.prefab",new Vector3(293,level,-928-i*34),6,270);
    Building("Western courtyard "+i,"Assets/HwaseongHaenggung/Prefabs/SM_Boknaedang.prefab",new Vector3(211,level,-926-i*36),8,180);
    Building("Eastern courtyard "+i,"Assets/HwaseongHaenggung/Prefabs/SM_Seoricheong.prefab",new Vector3(330,level,-931-i*36),7,0);
   }
   Building("City rest pavilion",house,new Vector3(300,level,-1048),6,270);
   Building("Palace distant hall","Assets/HwaseongHaenggung/Prefabs/SM_Bongsudang.prefab",new Vector3(270,level,-1110),13,180);
   Building("Closed palace approach","Assets/HwaseongHaenggung/Prefabs/SM_D_Gate_2.prefab",new Vector3(270,level,-1088),7,180);
   // Palace access remains physically closed until the later return sequence is authored.
   Box("Palace approach barrier",new Vector3(270,level+3,-1088),new Vector3(14,6,1),Mat("Timber"));
   foreach(float x in new[]{80f,460f})Box("District side retaining wall",new Vector3(x,level+4,(start+end)*.5f),new Vector3(3,8,start-end),Mat("Granite"));
   Box("District northern wall",new Vector3(270,level+4,end),new Vector3(380,8,3),Mat("Granite"));
   var points=session.Content.Points.Where(p=>p.Id!="CityEntryRest").ToList();
   points.Add(new PrologueContentSO.Point{Id="CityEntryRest",Kind=PrologueInteractionKind.Rest,Position=new Vector3(287,level,-1040),Radius=3,RequiredDefeated=new[]{"south_gate_general"}});
   session.Content.Points=points.ToArray();Box("City rest seat",new Vector3(289,level+.3f,-1040),new Vector3(1.6f,.6f,.7f),Mat("Timber"));EditorUtility.SetDirty(session.Content);
   Physics.SyncTransforms();var surface=Object.FindFirstObjectByType<NavMeshSurface>();var previous=surface.navMeshData;
   var min=surface.center-surface.size*.5f;var max=surface.center+surface.size*.5f;min=Vector3.Min(min,new Vector3(75,level-5,end-5));max=Vector3.Max(max,new Vector3(465,level+30,start));surface.center=(min+max)*.5f;surface.size=max-min;
   surface.BuildNavMesh();var next=surface.navMeshData;if(next==null)throw new Exception("City NavMesh failed");surface.RemoveData();EditorUtility.CopySerialized(next,previous);surface.navMeshData=previous;surface.AddData();EditorUtility.SetDirty(previous);Object.DestroyImmediate(next);
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   string result="City street, stream bridge, gated rest and closed palace approach authored; ground from "+start+" to "+end+"; level="+level;
   File.WriteAllText("../Art/World/PineRest/city_entry_author.txt",result);return result;
  }
  static string CityEntryView(){var s=RoadRestSession();var p=s.Content.Points.Single(x=>x.Id=="CityEntryRest");s.Teleport(new Vector3(270,p.Position.y+1.1f,-970),180);return "At city stream approach; audit viewpoint";}
 }
}
