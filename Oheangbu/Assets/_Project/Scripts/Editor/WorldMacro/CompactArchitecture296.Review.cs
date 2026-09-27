using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Views295 CameraViewsQa296()=>JsonUtility.FromJson<Views295>(File.ReadAllText(O295+"/cameras.json"));
  static string ReviewArchitecture296(string command)
  {
   RequireClean292();
   if(command=="nav")return NavigationQa296();
   if(command=="nav-diagnose"||command=="walk")return ScheduleTraversalQa296(command);
   if(command=="culling")return Culling296();
   if(command=="runtime")return "Use runtime:start for isolated actual session checks.";
   var lines=new List<string>{"Architecture296 structural/physical checks "+DateTime.UtcNow.ToString("O")};
   lines.AddRange(InteriorEntrancesQa296());
   void C(bool ok,string label)=>lines.Add((ok?"PASS ":"FAIL ")+label);
   var session=Session292();var sheet=Sheet296();var field=new CompactWorldSurface(session.MountainLayout);var query=Components295<WorldTerrainQuery>().First();
   C(session.Content.SaveSlot=="world-compact-architecture-296","isolated candidate save slot");
   C(AssetDatabase.GetAssetPath(session.Content).StartsWith(A296+"/"),"candidate content isolated");
   C(AssetDatabase.GetAssetPath(session.MountainLayout).StartsWith(A296+"/"),"candidate layout isolated");
   C(session.MountainLayout.FinalSurface.bytes.SequenceEqual(File.ReadAllBytes(A295+"/Surface/height.bytes")),"terrain height bytes identical to295 including protected paths");
   C(session.MountainLayout.Hydrology.WaterLevels.bytes.SequenceEqual(File.ReadAllBytes(A295+"/Surface/waterlevel.bytes")),"water height bytes identical to295");
   C(Components295<Transform>().All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"no missing scripts");
   C(sheet.Arenas.Length==5&&sheet.Arenas.Select(a=>a.Realm).Distinct().Count()==5,"five distinct regional arenas");
   C(sheet.Sources.Sum(s=>s.MeshyCredits)<=100,"Meshy recorded credits within100");
   var mats=Components295<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
   C(mats.All(m=>m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"active architecture/terrain shaders compile");
   var scene=SceneManager.GetActiveScene();
   foreach(var arena in sheet.Arenas)
   {
    C(Root296(arena.SceneRoot)!=null||Components295<Transform>().Any(t=>t.name==arena.SceneRoot),"arena scene binding "+arena.Id);
    C(arena.TestOnly||session.Actors.Any(a=>a.Id==arena.EncounterId),"arena actual encounter or explicit TestOnly "+arena.Id);
    C(arena.ClearSize.x>=40&&arena.ClearSize.y>=40,"arena clear dimensions "+arena.Id+"="+arena.ClearSize);
    int floorMiss=0,bodyBlock=0,ceilingMiss=0;var rotation=Quaternion.Euler(0,arena.Yaw,0);
    foreach(float x in new[]{-.4f,0,.4f})foreach(float z in new[]{-.4f,0,.4f})
    {
     var p=arena.Centre+rotation*new Vector3(arena.ClearSize.x*x,0,arena.ClearSize.y*z);
     if(!arena.TestOnly)p.y=field.Sample(p.x,p.z);
     var hits=Physics.RaycastAll(p+Vector3.up*2,Vector3.down,5,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&!(h.collider is CharacterController)&&h.collider.attachedRigidbody==null&&h.normal.y>.65f).OrderBy(h=>Mathf.Abs(h.point.y-p.y)).ToArray();
     if(hits.Length==0){floorMiss++;lines.Add("DETAIL missing floor "+arena.Id+" at "+p);continue;}var feet=hits[0].point+Vector3.up*.06f;
     if(query.Immersion(feet)>.55f)floorMiss++;
     var obstacles=Physics.OverlapCapsule(feet+Vector3.up*.31f,feet+Vector3.up*1.47f,.27f,~0,QueryTriggerInteraction.Ignore).Where(c=>c.attachedRigidbody==null&&!(c is CharacterController)&&!c.transform.IsChildOf(session.Walker.Body.transform)&&c.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()==null).ToArray();
     if(obstacles.Length>0){bodyBlock++;lines.Add("DETAIL blocked floor "+arena.Id+" at "+feet+": "+string.Join(",",obstacles.Select(c=>ScenePathVenue296(c.transform))));}
     if(arena.Interior&&!Physics.Raycast(feet+Vector3.up*1.8f,Vector3.up,arena.ClearHeight+30,~0,QueryTriggerInteraction.Ignore))ceilingMiss++;
    }
    C(floorMiss==0&&bodyBlock==0,"arena nine-point dry walkable combat floor "+arena.Id+" missing="+floorMiss+" blocked="+bodyBlock);
    if(arena.Interior)C(arena.ClearHeight>=(arena.Realm=="cheolong"?18:20)&&ceilingMiss==0,"interior height/solid roof "+arena.Id+" height="+arena.ClearHeight+" roof gaps="+ceilingMiss);
    C(arena.Approach!=null&&arena.Approach.Length>=2,"arena authored approach "+arena.Id);
    if(arena.Interior)
    {
     int attackBlocks=0,cameraBlocks=0;var body=session.Walker.Body;
     bool Architecture(Collider c)=>c.gameObject.scene==scene&&c.attachedRigidbody==null&&!(c is CharacterController)&&c.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()==null;
     // The centre and four dodge destinations remain inside the usable combat
     // floor. Probe actual collision, including roof backs and source components.
     foreach(var local in new[]{Vector3.zero,Vector3.right*8,Vector3.left*8,Vector3.forward*8,Vector3.back*8})
     {
      var feet=arena.Centre+rotation*local;var eye=feet+Vector3.up*session.Walker.EyeHeight;
      if(Physics.OverlapSphere(eye,.28f,~0,QueryTriggerInteraction.Ignore).Any(Architecture))cameraBlocks++;
      for(int direction=0;direction<8;direction++)
      {
       var forward=Quaternion.Euler(0,direction*45,0)*Vector3.forward;
       if(Physics.SphereCastAll(feet+Vector3.up*1.15f,.2f,forward,5,~0,QueryTriggerInteraction.Ignore).Any(h=>Architecture(h.collider)))attackBlocks++;
       if(Physics.SphereCastAll(eye,.28f,-forward,2.8f,~0,QueryTriggerInteraction.Ignore).Any(h=>Architecture(h.collider)))cameraBlocks++;
      }
     }
     C(attackBlocks==0&&cameraBlocks==0,"interior central combat/camera collision sweeps "+arena.Id+" attack="+attackBlocks+" camera="+cameraBlocks+" (40 attack and40 camera directions; geometry probe)");
    }
   }
   var ownMeshes=Components295<MeshFilter>().Where(f=>f.gameObject.activeInHierarchy&&f.sharedMesh!=null&&AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(A296+"/"));
   int invalid=0;long triangles=0;
   foreach(var f in ownMeshes){var mesh=f.sharedMesh;triangles+=mesh.triangles.Length/3;var bounds=mesh.bounds;bounds.Expand(.04f);if(mesh.vertices.Any(v=>!Finite295(v)||!bounds.Contains(v)))invalid++;}
   C(invalid==0,"candidate mesh bounds/finite vertices invalid="+invalid+" all authored LOD mesh triangles="+triangles);
   var lodAudit=new List<string>();int badLods=0;
   foreach(var root in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("Architecture296_",StringComparison.Ordinal)))
   foreach(var group in root.GetComponentsInChildren<LODGroup>(true))
   {
    var levels=group.GetLODs();bool valid=levels.Length>0;float previous=float.PositiveInfinity;bool found=false;var localBounds=new Bounds();
    foreach(var level in levels)
    {
     valid&=level.screenRelativeTransitionHeight>=0&&level.screenRelativeTransitionHeight<previous&&level.renderers.Length>0;
     previous=level.screenRelativeTransitionHeight;
     foreach(var renderer in level.renderers)
     {
      if(renderer==null){valid=false;continue;}var filter=renderer.GetComponent<MeshFilter>();
      if(filter==null||filter.sharedMesh==null){valid=false;continue;}
      var b=filter.sharedMesh.bounds;var matrix=group.transform.worldToLocalMatrix*renderer.localToWorldMatrix;
      foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
      {
       var p=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z)));
       if(!found){localBounds=new Bounds(p,Vector3.zero);found=true;}else localBounds.Encapsulate(p);
      }
     }
    }
    float needed=Mathf.Max(localBounds.size.x,Mathf.Max(localBounds.size.y,localBounds.size.z));
    // Unity LOD size is the largest local extent, not a bounding-sphere diameter.
    // Include every LOD so a simplified roof cannot inherit undersized bounds.
    valid&=found&&group.size+.08f>=needed&&Vector3.Distance(group.localReferencePoint,localBounds.center)<.08f;
    if(!valid)badLods++;
    lodAudit.Add((valid?"PASS ":"FAIL ")+ScenePathVenue296(group.transform)+" size="+group.size+" required="+needed+" centreError="+Vector3.Distance(group.localReferencePoint,localBounds.center)+" levels="+string.Join(",",levels.Select(l=>l.screenRelativeTransitionHeight.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
   }
   C(badLods==0,"candidate LOD references/transitions/all-level bounds groups="+lodAudit.Count+" invalid="+badLods);
   File.WriteAllLines(O296+"/lod-bounds.txt",lodAudit);
   CallPart296("CheckGates296",lines);
   lines.AddRange(RouteSupportQa296());
   File.WriteAllLines(O296+"/checks.txt",lines);return string.Join("\n",lines);
  }
  static Views295 ArchitectureViews296()
  {
   var file=O296+"/cameras.json";if(File.Exists(file))return JsonUtility.FromJson<Views295>(File.ReadAllText(file));
   var field=new CompactWorldSurface(Session292().MountainLayout);var views=new List<View295>();
   foreach(var arena in Sheet296().Arenas)
   {
    var q=Quaternion.Euler(0,arena.Yaw,0);var center=arena.Centre;
    var eye=center+q*new Vector3(95,75,-120);eye.y=Mathf.Max(eye.y,field.Sample(eye.x,eye.z)+12);
    views.Add(new View295{Id=arena.Realm+"-overview",Label=arena.Label+" 조감",Scale="overview",Eye=eye,Target=center+Vector3.up*8});
    eye=arena.Entrance+q*new Vector3(8,5,-28);eye.y=Mathf.Max(eye.y,field.Sample(eye.x,eye.z)+2);
    views.Add(new View295{Id=arena.Realm+"-entry",Label=arena.Label+" 진입",Scale="medium",Eye=eye,Target=center+Vector3.up*8});
    eye=center+q*new Vector3(-arena.ClearSize.x*.23f,1.65f,-arena.ClearSize.y*.34f);
    views.Add(new View295{Id=arena.Realm+"-eye",Label=arena.Label+(arena.Interior?" 실내":" 마당"),Scale="eye",Eye=eye,Target=center+q*new Vector3(arena.ClearSize.x*.1f,4,arena.ClearSize.y*.4f)});
   }
   var gate=Root296("CapitalSouthGate253").GetComponentInChildren<SouthGateDoorPresentation>(true).transform.position;
   views.Add(new View295{Id="south-gate",Label="황경 남문과 연속 성벽",Scale="medium",Eye=gate+new Vector3(65,45,-95),Target=gate+Vector3.up*10});
   var gateEye=gate+new Vector3(0,0,-35);gateEye.y=field.Sample(gateEye.x,gateEye.z)+1.65f;
   views.Add(new View295{Id="south-gate-eye",Label="황경 남문 접근",Scale="eye",Eye=gateEye,Target=gate+Vector3.up*7});
   if(File.Exists(O296+"/crossings.json"))foreach(var c in JsonUtility.FromJson<BridgeReceipt296>(File.ReadAllText(O296+"/crossings.json")).Bridges)
   {
    var center=(c.Start+c.End)*.5f;var d=c.End-c.Start;d.y=0;var r=Vector3.Cross(Vector3.up,d.normalized);
    var eye=center+r*65+Vector3.up*25;eye.y=Mathf.Max(eye.y,field.Sample(eye.x,eye.z)+5);
    views.Add(new View295{Id="bridge-"+c.Id,Label="교량 "+c.Id,Scale="medium",Eye=eye,Target=center});
   }
   var data=new Views295{Views=views.ToArray()};File.WriteAllText(file,JsonUtility.ToJson(data,true));return data;
  }
  static string ReframeArchitecture296()
  {
   RequireClean292();string file=O296+"/cameras.json";
   if(File.Exists(file)){Directory.CreateDirectory(O296+"/History");File.Copy(file,O296+"/History/cameras-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".json");File.Delete(file);}
   return "Recorded "+ArchitectureViews296().Views.Length+" paired camera poses from current architecture controls";
  }
  static string Capture296(string argument)
  {
   RequireClean292();var args=argument.Split(':');string stage=args[0];int index=int.Parse(args[1]);
   if(stage!="before"&&stage!="after")throw new ArgumentException("capture:before|after:index");
   var view=ArchitectureViews296().Views[index];string prior=SceneManager.GetActiveScene().path;string target=stage=="before"?Scene295:Scene296;
   string folder=O296+"/Captures/"+stage;Directory.CreateDirectory(folder);string output=folder+"/"+index.ToString("D2")+"-"+view.Id+".png";
   try
   {
    if(prior!=target)EditorSceneManager.OpenScene(target);eye293=view.Eye;target293=view.Target;output293=output;raw293=false;Capture292(6);
    File.WriteAllText(output+".json",JsonUtility.ToJson(new CaptureReceipt295{Stage=stage,Scene=target,CapturedUtc=DateTime.UtcNow.ToString("O"),View=view,
     HeightSHA256=HashArchitecture296(Session292().MountainLayout.FinalSurface.bytes),WaterSHA256=HashArchitecture296(Session292().MountainLayout.Hydrology.WaterLevels.bytes)},true));return output;
   }
   finally{eye293=null;target293=null;output293=null;raw293=false;if(prior!=target)EditorSceneManager.OpenScene(prior);}
  }
  static string CaptureAll296(string stage)
  {
   RequireClean292();if(stage!="before"&&stage!="after")throw new ArgumentException(stage);
   var views=ArchitectureViews296().Views;string prior=SceneManager.GetActiveScene().path,target=stage=="before"?Scene295:Scene296;
   string folder=O296+"/Captures/"+stage;Directory.CreateDirectory(folder);int count=0;
   try
   {
    if(prior!=target)EditorSceneManager.OpenScene(target);
    string height=HashArchitecture296(Session292().MountainLayout.FinalSurface.bytes),water=HashArchitecture296(Session292().MountainLayout.Hydrology.WaterLevels.bytes);
    for(int index=0;index<views.Length;index++)
    {
     var view=views[index];string output=folder+"/"+index.ToString("D2")+"-"+view.Id+".png";
     eye293=view.Eye;target293=view.Target;output293=output;raw293=false;Capture292(6);
     File.WriteAllText(output+".json",JsonUtility.ToJson(new CaptureReceipt295{Stage=stage,Scene=target,CapturedUtc=DateTime.UtcNow.ToString("O"),View=view,HeightSHA256=height,WaterSHA256=water},true));count++;
    }
    return "Captured "+count+" "+stage+" views with identical saved camera controls";
   }
   finally{eye293=null;target293=null;output293=null;raw293=false;if(prior!=target)EditorSceneManager.OpenScene(prior);}
  }
  static string HashArchitecture296(byte[] data){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
  static string Culling296()
  {
   var arts=Components295<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled&&a.Sheet!=null).ToArray();var observers=arts.Select(a=>a.Observer).ToArray();
   var go=new GameObject("Architecture296_CullingProbe"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.enabled=false;camera.aspect=16f/9;camera.farClipPlane=4000;
   var rt=new RenderTexture(640,360,24);camera.targetTexture=rt;int poses=0,errors=0;long matrices=0;
   try
   {
    foreach(var art in arts)art.Observer=camera;
    foreach(var view in ArchitectureViews296().Views)foreach(float offset in new[]{-.01f,0,.01f,64,80})
    {
     camera.transform.SetPositionAndRotation(view.Eye+Vector3.right*offset,Quaternion.LookRotation(view.Target-view.Eye));camera.Render();poses++;
     foreach(var art in arts){var scan=art.CompareWithFullScan(camera);matrices+=scan.Matrices;errors+=scan.Mismatches;if(scan.CulledVisible!=scan.FullVisible||art.VisibleInstances!=scan.FullVisible||art.DrawCalls!=scan.ExpectedDrawCalls||art.SubmittedTriangles!=scan.ExpectedTriangles)errors++;}
    }
   }
   finally{for(int i=0;i<arts.Length;i++)arts[i].Observer=observers[i];camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
   string report=(errors==0?"PASS ":"FAIL ")+"active sheets="+arts.Length+" poses="+poses+" matrices="+matrices+" value/order/accounting mismatches="+errors;
   File.WriteAllText(O296+"/culling.txt",report);return report;
  }
  static string PerfArchitecture296(string argument)=>PerfQa296(argument);
  sealed class GateProbeScope296:IDisposable
  {
   readonly SouthGateDoorPresentation[] gates;
   readonly bool[] prior;
   bool restored;
   public GateProbeScope296()
   {
    gates=Components295<SouthGateDoorPresentation>().Where(g=>g.isActiveAndEnabled).ToArray();
    prior=gates.Select(g=>g.IsOpenRequested).ToArray();foreach(var gate in gates)gate.SetOpened(true,true);Physics.SyncTransforms();
   }
   public void Dispose()
   {
    if(restored)return;restored=true;
    for(int i=0;i<gates.Length;i++)if(gates[i]!=null)gates[i].SetOpened(prior[i],true);Physics.SyncTransforms();
   }
  }
 }
}
