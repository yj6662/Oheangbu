using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class NaturalCaveFinalAudit
 {
  public static string Execute(string command)
  {
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var b=s.Walker.Body;var lines=new List<string>();
   if(command=="restore-editor-anchor")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");
    bool was=b.enabled;b.enabled=false;b.transform.SetPositionAndRotation(new Vector3(3406.6193848f,195.7736816f,1129.2521973f),Quaternion.Euler(0,210,0));b.enabled=was;
    lines.Add("Editor actor anchor restored to pre-cave position. Runtime checkpoint remains "+s.Content.StartFeet);
    EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
   }
   else if(command=="aperture-rays")
   {
    var apertureRoot=GameObject.Find(WorldMacroNaturalCave.RootName).transform;var go=new GameObject("Temporary_Aperture_Rays"){hideFlags=HideFlags.HideAndDontSave};
    try{var camera=go.AddComponent<Camera>();camera.CopyFrom(Object.FindFirstObjectByType<WorldMacroReviewController>().GetComponent<Camera>());camera.enabled=false;camera.aspect=16f/9;camera.transform.SetPositionAndRotation(apertureRoot.TransformPoint(new Vector3(-6,3,-26)),Quaternion.LookRotation(apertureRoot.TransformDirection(new Vector3(-54,2,31))));
    var temps=new List<GameObject>();foreach(var f in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)){var r=f.GetComponent<Renderer>();if(r==null||!r.enabled||f.sharedMesh==null||(f.GetComponent<Collider>()!=null&&f.GetComponent<Collider>().enabled)||Vector3.Distance(r.bounds.center,camera.transform.position)>250)continue;var test=new GameObject("DiagnosticMesh_"+f.name){hideFlags=HideFlags.HideAndDontSave};test.transform.SetPositionAndRotation(f.transform.position,f.transform.rotation);test.transform.localScale=f.transform.lossyScale;test.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;temps.Add(test);}Physics.SyncTransforms();
    bool before=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;try{foreach(var uv in new[]{new Vector2(1200,595),new Vector2(1165,604),new Vector2(1216,585),new Vector2(1430,900),new Vector2(1300,655)}){lines.Add("PIXEL "+uv);foreach(var h in Physics.RaycastAll(camera.ViewportPointToRay(new Vector3(uv.x/1920,1-uv.y/1080,0)),300,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).Take(5))lines.Add(h.collider.name+" local="+apertureRoot.InverseTransformPoint(h.point)+" normal="+h.normal);}}finally{Physics.queriesHitBackfaces=before;foreach(var temp in temps)Object.DestroyImmediate(temp);}}
    finally{Object.DestroyImmediate(go);}return string.Join("\n",lines);
   }
   else if(command=="settle-approach")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");var cave=GameObject.Find(WorldMacroNaturalCave.RootName).transform;
    float Lower(Vector3 p){float center=Mathf.Lerp(5,-1,Mathf.InverseLerp(-43,-26,p.x));float lateral=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(11,22,Mathf.Abs(p.z-center)));return 1.58f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-43,-26,p.x))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-26,-14,p.x)))*lateral;}
    foreach(var f in cave.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.Contains("Apron")||f.name=="Exterior_Mine_Approach"))
    {if(f.sharedMesh.name.EndsWith("Settled"))throw new Exception("Already settled");var mesh=Object.Instantiate(f.sharedMesh);mesh.name=f.sharedMesh.name+"_Settled";var vs=mesh.vertices;for(int i=0;i<vs.Length;i++){var p=cave.InverseTransformPoint(f.transform.TransformPoint(vs[i]));p.y-=Lower(p);vs[i]=f.transform.InverseTransformPoint(cave.TransformPoint(p));}mesh.vertices=vs;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();AssetDatabase.CreateAsset(mesh,WorldMacroNaturalCave.Folder+"/Meshes/"+mesh.name+".asset");f.sharedMesh=mesh;f.GetComponent<MeshCollider>().sharedMesh=mesh;lines.Add("Settled raised legacy approach: "+f.name);}
    foreach(var n in Enumerable.Range(0,s.Content.MainPath.Length)){var p=cave.InverseTransformPoint(s.Content.MainPath[n]);if(p.x> -44&&p.x< -14&&p.z>-10&&p.z<16){p.y-=Lower(p);s.Content.MainPath[n]=cave.TransformPoint(p);}}
    Physics.SyncTransforms();EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());File.WriteAllLines(WorldMacroNaturalCave.Output+"/approach_settlement.txt",lines);return string.Join("\n",lines);
   }
   else if(command=="retire-approach-strip")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");var cave=GameObject.Find(WorldMacroNaturalCave.RootName).transform;cave.Find("Exterior_Mine_Approach").gameObject.SetActive(false);Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Narrow raised approach strip archived inactive; broad continuous soil remains visual and collision ground.";
   }
   else if(command=="settle-entry-boulders")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");var cave=GameObject.Find(WorldMacroNaturalCave.RootName).transform;
    foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.StartsWith("MineRock_")))
    {var rs=t.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)continue;var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);var ground=new List<float>();foreach(var off in new[]{Vector3.zero,Vector3.right*bounds.extents.x*.65f,Vector3.left*bounds.extents.x*.65f,Vector3.forward*bounds.extents.z*.65f,Vector3.back*bounds.extents.z*.65f}){var hits=Physics.RaycastAll(bounds.center+off+Vector3.up*8,Vector3.down,30,1).Where(h=>h.normal.y>.5f&&(h.collider.name.StartsWith("Terrain_")||h.collider.name=="Continuous_Approach_Soil"||h.collider.name.StartsWith("Solid_Mountain_Portal"))).OrderBy(h=>h.distance).ToArray();if(hits.Length>0)ground.Add(hits[0].point.y);}if(ground.Count>0){float delta=Mathf.Min(0,ground.Min()-bounds.min.y-.22f);t.position+=Vector3.up*delta;lines.Add(t.name+" terrain footprint lower="+delta);}}
    var f=cave.Find("Solid_Mountain_Portal/Solid_Mountain_Portal_Rock").GetComponent<MeshFilter>();if(!f.sharedMesh.name.Contains("NoIsland")){var v=f.sharedMesh.vertices;var old=f.sharedMesh.triangles;bool Island(int i){var p=v[i];return p.x>-22.34f&&p.x< -21.87f&&p.y>4.84f&&p.y<5.08f&&p.z>8.91f&&p.z<9.59f;}var tri=new List<int>();for(int i=0;i<old.Length;i+=3)if(!(Island(old[i])&&Island(old[i+1])&&Island(old[i+2])))tri.AddRange(new[]{old[i],old[i+1],old[i+2]});if(old.Length-tri.Count!=72)throw new Exception("Expected exactly the audited 24-triangle isolated SDF island");var mesh=Object.Instantiate(f.sharedMesh);mesh.name="Portal_Rock_NoIsland";mesh.triangles=tri.ToArray();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,WorldMacroNaturalCave.Folder+"/Meshes/Portal_Rock_NoIsland.asset");f.sharedMesh=mesh;f.GetComponent<MeshCollider>().sharedMesh=mesh;lines.Add("Removed audited isolated SDF rock component:24 triangles; original generated source preserved.");}
    Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());File.WriteAllLines(WorldMacroNaturalCave.Output+"/loose_rock_cleanup.txt",lines);return string.Join("\n",lines);
   }
   else if(command=="ground-isolate")
   {
    var cave=GameObject.Find(WorldMacroNaturalCave.RootName);var hidden=cave.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("Apron")||r.name=="Exterior_Mine_Approach"||r.name=="Portal_Outer_Soil").Where(r=>r.enabled).ToArray();
    try{foreach(var r in hidden)r.enabled=false;return WorldMacroNaturalCave.Execute("capture:outside");}finally{foreach(var r in hidden)r.enabled=true;}
   }
   else if(command=="portal-two-sided")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");var r=GameObject.Find(WorldMacroNaturalCave.RootName).transform.Find("Solid_Mountain_Portal/Solid_Mountain_Portal_Terrain").GetComponent<Renderer>();
    var m=new Material(r.sharedMaterial){name="Portal_Terrain_TwoSided"};m.SetFloat("_Cull",0);AssetDatabase.CreateAsset(m,WorldMacroNaturalCave.Folder+"/Materials/Portal_Terrain_TwoSided.mat");r.sharedMaterial=m;AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Local carved terrain uses both sides near exposed cave roof, original terrain material preserved.";
   }
   else if(command=="fit-stair-end")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");var ramp=GameObject.Find(WorldMacroOriginalInn.RootName).transform.Find("Inn_Stair_Traversal").GetComponent<MeshCollider>();
    if(ramp.sharedMesh.name.EndsWith("Aligned"))throw new Exception("Already fitted");
    var mesh=Object.Instantiate(ramp.sharedMesh);mesh.name="Inn_Stair_Aligned";var vs=mesh.vertices;float end=vs.Max(p=>p.z);for(int i=0;i<vs.Length;i++)if(vs[i].z>end-.01f)vs[i].y+=.2f;
    mesh.vertices=vs;mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,WorldMacroNaturalCave.Folder+"/Meshes/Inn_Stair_Aligned.asset");ramp.sharedMesh=mesh;Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Stair ramp endpoint raised 0.2m to clear the original stone porch lip; base and visible source steps unchanged.";
   }
   else if(command=="stair-landing")
   {
    if(EditorApplication.isPlaying)throw new Exception("Edit only");var ramp=GameObject.Find(WorldMacroOriginalInn.RootName).transform.Find("Inn_Stair_Traversal").GetComponent<MeshCollider>();var old=ramp.sharedMesh;
    var v=old.vertices;if(v.Length!=4)throw new Exception("Landing already installed");var mesh=new Mesh{name="Inn_Stair_WithLanding"};mesh.vertices=new[]{v[0],v[1],v[2],v[3],v[2]+Vector3.forward*.9f,v[3]+Vector3.forward*.9f};mesh.triangles=new[]{0,2,1,1,2,3,2,4,3,3,4,5};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,WorldMacroNaturalCave.Folder+"/Meshes/Inn_Stair_WithLanding.asset");ramp.sharedMesh=mesh;Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Continuous 0.9m porch landing added after stair collision incline.";
   }
   else if(command=="inn-check")
   {
    var inn=GameObject.Find(WorldMacroOriginalInn.RootName).transform;var ramp=inn.Find("Inn_Stair_Traversal").GetComponent<MeshCollider>();var v=ramp.sharedMesh.vertices;int ok=0,count=0;
    var left=v.OrderBy(p=>p.z).First();var right=v.OrderByDescending(p=>p.z).First();float x=(v.Min(p=>p.x)+v.Max(p=>p.x))*.5f;
    for(float z=left.z+.4f;z<=689.99f;z+=.15f){count++;bool safe=s.TrySafeFeet(new Vector3(x,right.y,z),out var f);if(safe)ok++;else lines.Add("FAIL stair point="+new Vector3(x,right.y,z)+" candidate="+f);}
    lines.Add((ok==count?"PASS ":"FAIL ")+"source stair/ramp support "+ok+"/"+count+" slope="+Mathf.Atan2(right.y-left.y,right.z-left.z)*Mathf.Rad2Deg);
    lines.Add("Ramp extent="+left+" to "+right);
    var probe=new Vector3(1998.97f,134.3f,689.21f);foreach(var h in Physics.RaycastAll(probe+Vector3.up,Vector3.down,4,1).OrderBy(h=>h.distance))lines.Add("RAY "+h.collider.name+" at="+h.point+" normal="+h.normal);
    probe.y=134.213f;foreach(var c in Physics.OverlapCapsule(probe+Vector3.up*.31f,probe+Vector3.up*1.47f,.27f,~0,QueryTriggerInteraction.Ignore))lines.Add("OVERLAP "+c.name+" layer="+c.gameObject.layer+" bounds="+c.bounds);
    foreach(var o in new[]{Vector3.right*.25f,Vector3.left*.25f,Vector3.forward*.25f,Vector3.back*.25f})lines.Add("OFFSET "+o+" supported="+Physics.Raycast(probe+o+Vector3.up*.3f,Vector3.down,out var oh,.65f,1,QueryTriggerInteraction.Ignore)+" collider="+oh.collider?.name);
    foreach(var p in s.Content.Points.Where(p=>p.Id=="geumpyo_inn"||p.Id=="logger"||p.Id=="herbalist"))lines.Add((s.TrySafeFeet(p.Position+Vector3.back*.8f,out _)?"PASS ":"FAIL ")+p.Id+" support");
    File.WriteAllLines(WorldMacroNaturalCave.Output+"/inn_final_validation.txt",lines);return string.Join("\n",lines);
   }
   else if(command!="inspect"&&command!="runtime")throw new ArgumentException(command);
   lines.Add("Play="+EditorApplication.isPlaying+" body="+b.transform.position+" expected="+s.Content.StartFeet+" saveSuffix="+s.TestSaveSuffix);
   lines.Add("Save status="+s.LoadStatus+" error="+s.SaveError+" safe="+s.TrySafeFeet(b.transform.position,out var feet)+" feet="+feet);
   foreach(var a in s.Actors){var n=a.GetComponent<NavMeshAgent>();lines.Add("Actor "+a.Id+" enabled="+n.enabled+" onNavMesh="+n.isOnNavMesh+" pos="+a.transform.position);}
   foreach(var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))lines.Add("Skin "+r.name+" bounds="+r.bounds+" rootBone="+r.rootBone?.position+" enabled="+r.enabled);
   var root=GameObject.Find(WorldMacroNaturalCave.RootName);
   foreach(var mf in root.GetComponentsInChildren<MeshFilter>())lines.Add("MESH "+mf.name+" vertices="+mf.sharedMesh.vertexCount+" tris="+mf.sharedMesh.triangles.Length/3+" bounds="+mf.sharedMesh.bounds+" collider="+(mf.GetComponent<MeshCollider>()!=null));
   lines.Add("System commit="+Prologue.PrologueAudit.CommitRatio());
   File.WriteAllLines(WorldMacroNaturalCave.Output+"/"+(EditorApplication.isPlaying?"runtime_smoke":"final_scene_audit")+".txt",lines);
   return string.Join("\n",lines);
  }
 }
}
