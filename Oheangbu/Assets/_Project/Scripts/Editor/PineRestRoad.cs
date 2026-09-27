using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static readonly Vector3[] RoadControls={new Vector3(20,0,43),new Vector3(80,0,43),new Vector3(130,1,20),new Vector3(170,4,-65),new Vector3(160,6,-190),new Vector3(120,9,-320),new Vector3(160,8,-470),new Vector3(240,4,-620),new Vector3(270,2,-800)};
  static Vector3[] RoadSamples(float baseHeight){
   var points=new List<Vector3>();
   for(int n=0;n<RoadControls.Length-1;n++){
    var a=RoadControls[Mathf.Max(0,n-1)];var b=RoadControls[n];var c=RoadControls[n+1];var d=RoadControls[Mathf.Min(RoadControls.Length-1,n+2)];
    int count=Mathf.CeilToInt(Vector3.Distance(b,c)/2);
    for(int i=0;i<count;i++){float t=(float)i/count;var p=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);p.y+=baseHeight;points.Add(p);}
   }
   points.Add(RoadControls.Last()+Vector3.up*baseHeight);return points.ToArray();
  }
  static void RoadNearest(Vector3 p,Vector3[] line,out float distance,out float height){
   float best=float.MaxValue; height=0;
   for(int i=1;i<line.Length;i++){var a=line[i-1];var delta=line[i]-a;delta.y=0;var flat=p-a;flat.y=0;float t=Mathf.Clamp01(Vector3.Dot(flat,delta)/Mathf.Max(.001f,delta.sqrMagnitude));var q=a+delta*t;float sq=(new Vector2(p.x-q.x,p.z-q.z)).sqrMagnitude;if(sq<best){best=sq;height=Mathf.Lerp(a.y,line[i].y,t);}}
   distance=Mathf.Sqrt(best);
  }
  static string RoadAuthor(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   if(GameObject.Find("Journey Capital Road")!=null)return "Existing road retained; use audit rather than destructive regeneration";
   var session=Object.FindFirstObjectByType<PrologueSession>();var valley=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
   if(!valley.Raycast(new Ray(new Vector3(20,100,43),Vector3.down),out var entry,200))throw new Exception("Road junction ground absent");
   var line=RoadSamples(entry.point.y);var root=new GameObject("Journey Capital Road").transform;
   var copy=Object.Instantiate(valley.sharedMesh);copy.name="Journey junction valley";var vertices=copy.vertices;
   for(int i=0;i<vertices.Length;i++){var p=valley.transform.TransformPoint(vertices[i]);if(p.x<20)continue;RoadNearest(p,line,out float dist,out float h);float weight=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(8,32,dist)))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(20,38,p.x));p.y=Mathf.Lerp(p.y,h,weight);vertices[i]=valley.transform.InverseTransformPoint(p);}
   copy.vertices=vertices;copy.RecalculateNormals();copy.RecalculateBounds();AssetDatabase.CreateAsset(copy,Folder+"/RoadJunction.asset");valley.GetComponent<MeshFilter>().sharedMesh=copy;valley.sharedMesh=copy;Physics.SyncTransforms();
   const int columns=95,rows=280;var v=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[v.Length];var triangles=new int[columns*rows*6];
   for(int z=0;z<=rows;z++)for(int x=0;x<=columns;x++){
    var p=new Vector3(80+x*4,0,-900+z*4);RoadNearest(p,line,out float dist,out float h);
    float shoulder=Mathf.SmoothStep(0,1,Mathf.InverseLerp(7,55,dist));p.y=h+shoulder*(3+17*Mathf.PerlinNoise(p.x*.009f,p.z*.006f+17));
    if(p.x<=100&&p.z>=0&&p.z<=220&&valley.Raycast(new Ray(new Vector3(79.99f,150,p.z),Vector3.down),out var edge,300))p.y=Mathf.Lerp(edge.point.y,p.y,Mathf.SmoothStep(0,1,(p.x-80)/20));
    int index=z*(columns+1)+x;v[index]=p;uv[index]=new Vector2(p.x*.08f,p.z*.08f);
   }
   int k=0;for(int z=0;z<rows;z++)for(int x=0;x<columns;x++){int a=z*(columns+1)+x,b=a+1,c=a+columns+1,d=c+1;triangles[k++]=a;triangles[k++]=c;triangles[k++]=b;triangles[k++]=b;triangles[k++]=c;triangles[k++]=d;}
   var mesh=new Mesh{name="Journey southbound ground",indexFormat=IndexFormat.UInt32,vertices=v,uv=uv,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/CapitalRoadGround.asset");
   var road=new GameObject("Capital road ground",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));road.transform.SetParent(root);road.GetComponent<MeshFilter>().sharedMesh=mesh;road.GetComponent<MeshCollider>().sharedMesh=mesh;
   var material=new Material(valley.GetComponent<Renderer>().sharedMaterial);material.name="Journey road ground";if(material.HasProperty("_GroundPath"))material.SetFloat("_GroundPath",0);AssetDatabase.CreateAsset(material,Folder+"/CapitalRoadGround.mat");road.GetComponent<Renderer>().sharedMaterial=material;
   Physics.SyncTransforms();var ground=road.GetComponent<MeshCollider>();
   Vector3 Ground(Vector3 p){if(!ground.Raycast(new Ray(p+Vector3.up*150,Vector3.down),out var hit,300))throw new Exception("Unsupported road site "+p);return hit.point;}
   var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/Timber.mat");
   void Box(string name,Transform parent,Vector3 position,Vector3 size){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=wood;}
   var stops=new List<JourneyEscortStop>();var content=session.Content.Points.ToList();
   for(int n=0;n<3;n++){
    int control=n==0?4:n==1?7:8;var center=Ground(RoadControls[control]);var tangent=(RoadControls[control]-RoadControls[control-1]).normalized;tangent.y=0;tangent.Normalize();var across=Vector3.Cross(Vector3.up,tangent);
    var site=new GameObject(n<2?"Road inspection "+(n+1):"Capital merchant depot").transform;site.SetParent(root);site.position=center;site.rotation=Quaternion.LookRotation(tangent);
    var position=Ground(center-across*5-tangent*8);
    var npc=Object.Instantiate(GameObject.Find("Jeongdam appearance"),site);npc.name=n<2?"Inspection officer provisional":"Depot receiver provisional";npc.transform.position=position;npc.transform.rotation=Quaternion.LookRotation(across);
    var stop=npc.AddComponent<JourneyEscortStop>();stop.Session=session;stop.PointId=n==0?"checkpoint_1":n==1?"checkpoint_2":"cargo_delivery";stop.ClearedStage=n==0?DemoEscortStage.FirstInspectionCleared:n==1?DemoEscortStage.SecondInspectionCleared:DemoEscortStage.Delivered;stops.Add(stop);
    if(n<2){
     var pivot=new GameObject("Inspection barrier hinge").transform;pivot.SetParent(site,false);pivot.localPosition=new Vector3(-7,1.3f,4);Box("Closed road beam",pivot,new Vector3(7,0,0),new Vector3(14,.18f,.22f));stop.Barrier=pivot;
     Box("Barrier post",site,new Vector3(-7,.9f,4),new Vector3(.3f,1.8f,.3f));Box("Barrier post",site,new Vector3(7,.9f,4),new Vector3(.3f,1.8f,.3f));
    }
    var houseFeet=Ground(center-across*16);Place(n<2?"Inspection shelter":"Capital receiving house","Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab",site,houseFeet,4.5f);
    content.Add(new PrologueContentSO.Point{Id=stop.PointId,Kind=PrologueInteractionKind.Conversation,Position=position,Radius=3,Currency=n==2?220:60,RequiredCompleted=new[]{n==0?"escort_start":n==1?"checkpoint_1":"checkpoint_2"}});
   }
   session.EscortStops=stops.ToArray();session.Content.Points=content.ToArray();
   var surface=Object.FindFirstObjectByType<NavMeshSurface>();var previous=surface.navMeshData;surface.center=new Vector3(175,25,-335);surface.size=new Vector3(520,140,1130);surface.BuildNavMesh();var next=surface.navMeshData;if(next==null)throw new Exception("Road navigation absent");surface.RemoveData();EditorUtility.CopySerialized(next,previous);surface.navMeshData=previous;surface.AddData();EditorUtility.SetDirty(previous);Object.DestroyImmediate(next);
   EditorUtility.SetDirty(session);EditorUtility.SetDirty(session.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   return RoadAudit();
  }
  static string RoadRewards(){
   if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
   var s=Object.FindFirstObjectByType<PrologueSession>();if(s==null||s.gameObject.scene.path!=Scene)throw new Exception("Journey required");
   var source=AssetDatabase.LoadAssetAtPath<Oheangbu.Data.Demo.DemoCampaignProfile>("Assets/_Project/Art/World/WorldCompact/ActsTerrain/Campaign_TEST.asset");
   var report=new StringBuilder();foreach(var id in new[]{"checkpoint_1","checkpoint_2","cargo_delivery"}){var point=s.Content.Points.Single(x=>x.Id==id);point.Currency=source.Stages.Single(x=>x.TriggerId==id).TongboReward;report.AppendLine(id+"="+point.Currency);}
   EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();return report.ToString();
  }
  static string RoadFinish(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   var root=GameObject.Find("Journey Capital Road").transform;var valley=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
   foreach(string name in new[]{"Talus_3_0","Bedrock_Shoulder_3","Jointed_Block_3"}){var rock=GameObject.Find(name);if(rock!=null&&rock.transform.position.z<50){var p=rock.transform.position;p.z+=18;if(valley.Raycast(new Ray(p+Vector3.up*100,Vector3.down),out var h,200))p.y=h.point.y;rock.transform.position=p;}}
   var ground=root.Find("Capital road ground").GetComponent<MeshCollider>();
   foreach(var stop in Object.FindFirstObjectByType<PrologueSession>().EscortStops){
    var site=stop.transform.parent;string name=stop.PointId=="cargo_delivery"?"Capital receiving house":"Inspection shelter";
    var old=site.Find(name);if(old!=null&&old.GetComponentsInChildren<MeshFilter>().Any(f=>AssetDatabase.GetAssetPath(f.sharedMesh).Contains("WoodenBox")))Object.DestroyImmediate(old.gameObject);
    var p=site.position-site.right*16;if(ground.Raycast(new Ray(p+Vector3.up*150,Vector3.down),out var h,300))p=h.point;
    Place(name,"Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab",site,p,4.5f);
    foreach(var filter in site.Find(name).GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh!=null&&filter.GetComponent<Collider>()==null)filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
    if(stop.GetComponent<CapsuleCollider>()==null){var body=stop.gameObject.AddComponent<CapsuleCollider>();body.center=Vector3.up*.875f;body.height=1.75f;body.radius=.3f;}
    if(stop.Barrier!=null&&stop.Barrier.GetComponent<NavMeshObstacle>()==null){var obstacle=stop.Barrier.gameObject.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=new Vector3(7,0,0);obstacle.size=new Vector3(14,.3f,.4f);obstacle.carving=true;}
   }
   Physics.SyncTransforms();var surface=Object.FindFirstObjectByType<NavMeshSurface>();var previous=surface.navMeshData;surface.BuildNavMesh();var next=surface.navMeshData;surface.RemoveData();EditorUtility.CopySerialized(next,previous);surface.navMeshData=previous;surface.AddData();EditorUtility.SetDirty(previous);Object.DestroyImmediate(next);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return RoadAudit();
  }
  static string RoadAudit(){
   var report=new StringBuilder();var valley=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");valley.Raycast(new Ray(new Vector3(20,100,43),Vector3.down),out var hit,200);var line=RoadSamples(hit.point.y);float length=0,maxSlope=0;int missing=0,blocked=0;
   for(int i=1;i<line.Length;i++){
    length+=Vector3.Distance(line[i],line[i-1]);if(!Physics.Raycast(line[i]+Vector3.up*30,Vector3.down,out var ground,60,1,QueryTriggerInteraction.Ignore)){missing++;continue;}maxSlope=Mathf.Max(maxSlope,Vector3.Angle(ground.normal,Vector3.up));if(Vector3.Angle(ground.normal,Vector3.up)>15)report.AppendLine("steep "+line[i]+" collider="+ground.collider.name+" hit="+ground.point);
    if(Physics.OverlapBox(ground.point+Vector3.up*1.6f,new Vector3(1.15f,.9f,1.7f),Quaternion.LookRotation(line[i]-line[i-1]),1,QueryTriggerInteraction.Ignore).Any(c=>c.name!="Valley"&&c.name!="Capital road ground"&&c.name!="Closed road beam")){blocked++;report.AppendLine("blocked "+line[i]+" by "+string.Join(",",Physics.OverlapBox(ground.point+Vector3.up*1.6f,new Vector3(1.15f,.9f,1.7f),Quaternion.LookRotation(line[i]-line[i-1]),1,QueryTriggerInteraction.Ignore).Select(c=>c.name)));}
   }
   report.AppendLine("Road length="+length+"m; center support misses="+missing+"; maximum sampled slope="+maxSlope+"; obstructed vehicle samples="+blocked);
   var s=Object.FindFirstObjectByType<PrologueSession>();foreach(var stop in s.EscortStops){var path=new NavMeshPath();bool valid=NavMesh.SamplePosition(line[0],out var a,4,NavMesh.AllAreas)&&NavMesh.SamplePosition(stop.transform.position,out var b,4,NavMesh.AllAreas)&&NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;report.AppendLine(stop.PointId+" navigation="+valid+" position="+stop.transform.position);}
   report.AppendLine("Static geometry/path audit only; actual full vehicle traverse, narrative presentation and final art remain unverified.");File.WriteAllText("../Art/World/PineRest/road_audit.txt",report.ToString());return report.ToString();
  }
 }
}
