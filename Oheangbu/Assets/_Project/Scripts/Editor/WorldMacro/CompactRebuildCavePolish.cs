using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string CavePolishMap()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(SceneManager.GetActiveScene().path!=receipt.scene)throw new Exception("Candidate required");
   string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');
   SurveyTexture("terrain.png",folder+"/Cartography.png");SurveyTexture("region.png",folder+"/PaintedTerrain_Cheongrim.png");SurveyTexture("explored.png",folder+"/ExploredTerrain.png",8192);
   AssetDatabase.SaveAssets();return "Exterior map relief refreshed from final cave cover; discovery and cave plan retained";
  }
  static string CavePolishFinish()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   var scene=SceneManager.GetActiveScene();var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));if(scene.path!=receipt.scene)throw new Exception("Candidate required");
   var cover=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>()).Single(r=>r.name=="Cave_ExteriorCover237");
   cover.sharedMaterial.SetFloat("_Cull",0);cover.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;EditorUtility.SetDirty(cover.sharedMaterial);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   return "Host surface is two sided for close cameras intersecting the hillside; geometry and navigation unchanged";
  }
  static string CavePolishAudit()
  {
   var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
   var wall=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Natural_Cave_Interior");
   var cover=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Cave_ExteriorCover237");
   var layout=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
   var lines=new List<string>();void Check(bool ok,string label)=>lines.Add((ok?"PASS ":"FAIL ")+label);
   int samples=0,covered=0;bool before=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   try
   {
    foreach(var path in new[]{layout.main,layout.branch,layout.recess})for(int i=1;i<path.Length;i++)
    {
     int count=Mathf.CeilToInt(Vector3.Distance(path[i-1],path[i])/3);
     for(int j=0;j<=count;j++)
     {
      var p=Vector3.Lerp(path[i-1],path[i],j/(float)count)+Vector3.up*1.7f;samples++;
      bool ceiling=wall.Raycast(new Ray(p,Vector3.up),out var roof,20);
      bool exterior=cover.Raycast(new Ray(p+Vector3.up*100,Vector3.down),out var outer,100)&&outer.point.y>p.y+1;
      if(ceiling&&exterior)covered++;else lines.Add("DETAIL exposed authored path="+p+" interior roof="+ceiling+" outer cover="+exterior);
     }
    }
   }
   finally{Physics.queriesHitBackfaces=before;}
   Check(samples==covered,"authored galleries have interior ceiling and exterior cover "+covered+"/"+samples);
   Check(wall.GetComponent<Renderer>().sharedMaterial.GetFloat("_Cull")==0,"cave color/depth/shadow passes use two-sided material");
   Check(mine.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Solid_Mountain_Portal_")).All(t=>!t.gameObject.activeInHierarchy),"legacy portal surfaces retired from active candidate");
   var all=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).ToArray();
   var mats=all.Where(r=>r.gameObject.activeInHierarchy&&r.enabled).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
   Check(mats.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"active renderer materials and shaders resolve");
   Check(roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"no missing scene scripts");
   Check(wall.sharedMesh==wall.GetComponent<MeshFilter>().sharedMesh&&cover.sharedMesh==cover.GetComponent<MeshFilter>().sharedMesh,"visible wall and mountain match collision mesh");
   var report=string.Join("\n",lines);File.WriteAllText(Output+"/CavePolish237/audit.txt",report);return report;
  }

  static string CavePolishBuild()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   var scene=SceneManager.GetActiveScene();if(scene.path!=receipt.scene)throw new Exception("Open candidate first");
   string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/CavePolish237";DevSceneKit.EnsureFolder(folder);
   var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine").transform;
   var existing=mine.Find("Cave_ExteriorCover237");if(existing!=null)Object.DestroyImmediate(existing.gameObject);
   var terrain=roots.SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(false)).Where(c=>c.name.StartsWith("Terrain_")).ToArray();
   var wall=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Natural_Cave_Interior");
   var layout=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
   var mouth=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CavePolish237/geometry.json"));
   layout=mouth;
   var record=mouth.meshes.Single(m=>m.name=="Natural_Cave_Interior");
   var lining=new Mesh{name="Cave_MouthContinuous237",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};lining.vertices=record.vertices.Select(v=>wall.transform.InverseTransformPoint(mouth.origin+v)).ToArray();lining.triangles=record.triangles;lining.RecalculateNormals();lining.uv=lining.vertices.Select(v=>new Vector2(v.x,v.z)*.2f).ToArray();lining.RecalculateBounds();lining.RecalculateTangents();lining=ArtMesh(lining,folder+"/ContinuousInterior.asset");
   wall.sharedMesh=lining;wall.GetComponent<MeshFilter>().sharedMesh=lining;
   foreach(var t in mine.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Solid_Mountain_Portal_")||t.name=="Mountain_Rock_Transition"||t.name=="Natural_Mine_Broad_Apron"))t.gameObject.SetActive(false);
   const int nx=126,nz=116;const float x0=3370,z0=1700,step=2;
   var points=new Vector3[nx*nz];var raised=new bool[points.Length];
   bool previousBackfaces=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   try
   {
    for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
    {
     float wx=x0+x*step,wz=z0+z*step;float ground=float.NegativeInfinity;
     var ray=new Ray(new Vector3(wx,600,wz),Vector3.down);
     foreach(var c in terrain)if(c.bounds.min.x<=wx&&c.bounds.max.x>=wx&&c.bounds.min.z<=wz&&c.bounds.max.z>=wz&&c.Raycast(ray,out var hit,800))ground=Mathf.Max(ground,hit.point.y);
     if(float.IsNegativeInfinity(ground))throw new Exception("Missing host terrain at "+wx+","+wz);
     float dx=(wx-3521)/83,dz=(wz-1797)/75;
     float inward=Vector2.Dot(new Vector2(wx-3435,wz-1857),new Vector2(.728f,-.686f));
     float lip=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-12,35,inward));
     float hill=135.2f+43*Mathf.Exp(-(dx*dx+dz*dz)*1.25f)*lip;
     float edge=Mathf.Min(Mathf.Min(x,nx-1-x),Mathf.Min(z,nz-1-z))*step;
     float height=Mathf.Lerp(ground,Mathf.Max(ground,hill),Mathf.SmoothStep(0,1,Mathf.Clamp01(edge/18)));
     if(inward>0&&wall.Raycast(ray,out var roof,800))height=Mathf.Max(height,roof.point.y+3.5f);
     int at=x+z*nx;raised[at]=height>ground+.025f;points[at]=new Vector3(wx,height+.015f,wz);
    }
   }
   finally{Physics.queriesHitBackfaces=previousBackfaces;}
   // Spread the rock roof into a bank instead of an abrupt thin cliff at the mesh boundary.
   var raw=(Vector3[])points.Clone();
   for(int z=8;z<nz-8;z++)for(int x=8;x<nx-8;x++)
   {
    int i=x+z*nx;float height=raw[i].y;
    for(int dz=-7;dz<=7;dz++)for(int dx=-7;dx<=7;dx++)
      if(raised[i+dx+dz*nx])height=Mathf.Max(height,raw[i+dx+dz*nx].y-Mathf.Sqrt(dx*dx+dz*dz)*step*.85f);
    if(height>points[i].y+.025f)raised[i]=true;points[i].y=height;
   }
   var vertices=new List<Vector3>();var triangles=new List<int>();
   float HostHeight(Vector3 p)
   {float fx=Mathf.Clamp((p.x-x0)/step,0,nx-1.001f),fz=Mathf.Clamp((p.z-z0)/step,0,nz-1.001f);int x=(int)fx,z=(int)fz,i=x+z*nx;return Mathf.Lerp(Mathf.Lerp(points[i].y,points[i+1].y,fx-x),Mathf.Lerp(points[i+nx].y,points[i+nx+1].y,fx-x),fz-z);}
   // Intersect the extended lining with the host mountain: no detached tunnel lip outside the slope.
   var lv=new List<Vector3>();var lt=new List<int>();var originalVertices=lining.vertices;var originalIndices=lining.triangles;
   for(int t=0;t<originalIndices.Length;t+=3)
   {
    var input=new[]{wall.transform.TransformPoint(originalVertices[originalIndices[t]]),wall.transform.TransformPoint(originalVertices[originalIndices[t+1]]),wall.transform.TransformPoint(originalVertices[originalIndices[t+2]])};var poly=new List<Vector3>();
    for(int j=0;j<3;j++){var a=input[j];var b=input[(j+1)%3];float va=HostHeight(a)-a.y+.03f,vb=HostHeight(b)-b.y+.03f;if(va>=0)poly.Add(a);if((va>=0)!=(vb>=0))poly.Add(Vector3.Lerp(a,b,va/(va-vb)));}
    for(int j=2;j<poly.Count;j++){int n=lv.Count;lv.Add(wall.transform.InverseTransformPoint(poly[0]));lv.Add(wall.transform.InverseTransformPoint(poly[j-1]));lv.Add(wall.transform.InverseTransformPoint(poly[j]));lt.Add(n);lt.Add(n+1);lt.Add(n+2);}
   }
   lining.Clear();lining.SetVertices(lv);lining.SetTriangles(lt,0);lining.RecalculateNormals();lining.uv=lv.Select(v=>new Vector2(v.x,v.z)*.2f).ToArray();lining.RecalculateBounds();lining.RecalculateTangents();EditorUtility.SetDirty(lining);wall.sharedMesh=null;wall.sharedMesh=lining;
   void Emit(Vector3 a,Vector3 b,Vector3 c,int depth=0)
   {
    // A steep mouth face can cross the tunnel even when all three corners are outside it.
    if(depth<7&&Mathf.Max(a.z,Mathf.Max(b.z,c.z))>1825&&Mathf.Min(a.x,Mathf.Min(b.x,c.x))<3460&&Mathf.Min(a.y,Mathf.Min(b.y,c.y))<layout.origin.y+12&&Mathf.Max(a.y,Mathf.Max(b.y,c.y))>layout.origin.y&&Mathf.Max((a-b).sqrMagnitude,Mathf.Max((b-c).sqrMagnitude,(c-a).sqrMagnitude))>1f)
    {var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;Emit(a,ab,ca,depth+1);Emit(ab,b,bc,depth+1);Emit(ca,bc,c,depth+1);Emit(ab,bc,ca,depth+1);return;}
    // Cut the real opening out of the solid cover. Never fill the authored air volume.
    var input=new[]{a,b,c};var poly=new List<Vector3>();
    for(int j=0;j<3;j++){var v=input[j];var w=input[(j+1)%3];float va=CaveAir(v,layout)-.15f,wa=CaveAir(w,layout)-.15f;if(va>=0)poly.Add(v);if((va>=0)!=(wa>=0))poly.Add(Vector3.Lerp(v,w,va/(va-wa)));}
    for(int j=2;j<poly.Count;j++){int n=vertices.Count;vertices.Add(poly[0]);vertices.Add(poly[j-1]);vertices.Add(poly[j]);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);}
   }
   for(int z=0;z<nz-1;z++)for(int x=0;x<nx-1;x++)
   {
    int a=x+z*nx,b=a+1,c=a+nx,d=c+1;if(!raised[a]&&!raised[b]&&!raised[c]&&!raised[d])continue;
    Emit(points[a],points[c],points[b]);Emit(points[b],points[c],points[d]);
   }
   Vector3 SurfaceNormal(Vector3 p)
   {
    int x=Mathf.Clamp(Mathf.RoundToInt((p.x-x0)/step),1,nx-2),z=Mathf.Clamp(Mathf.RoundToInt((p.z-z0)/step),1,nz-2),i=x+z*nx;
    return new Vector3(points[i-1].y-points[i+1].y,step*2,points[i-nx].y-points[i+nx].y).normalized;
   }
   var mesh=new Mesh{name="Cave_ExteriorCover237",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.uv=vertices.Select(v=>new Vector2(v.x,v.z)*.1f).ToArray();mesh.normals=vertices.Select(SurfaceNormal).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();mesh=ArtMesh(mesh,folder+"/ExteriorCover.asset");
   var cover=new GameObject("Cave_ExteriorCover237");cover.transform.SetParent(mine,false);cover.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);cover.transform.localScale=Vector3.one;
   cover.AddComponent<MeshFilter>().sharedMesh=mesh;cover.AddComponent<MeshCollider>().sharedMesh=mesh;
   var material=new Material(terrain[0].GetComponent<Renderer>().sharedMaterial){name="MineExterior237"};material.SetFloat("_Cull",0);material=SurveyMaterial(material,folder+"/Exterior.mat");var coverRenderer=cover.AddComponent<MeshRenderer>();coverRenderer.sharedMaterial=material;coverRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;cover.isStatic=true;
   var rock=wall.GetComponent<Renderer>().sharedMaterial;rock.SetFloat("_Cull",0);EditorUtility.SetDirty(rock);
   var outward=wall.transform.Find("Cave_OuterCollision237");if(outward!=null)Object.DestroyImmediate(outward.gameObject);
   var solid=Object.Instantiate(wall.sharedMesh);solid.name="Cave_OuterCollision237";var indices=solid.triangles;
   for(int i=0;i<indices.Length;i+=3){int t=indices[i];indices[i]=indices[i+2];indices[i+2]=t;}solid.triangles=indices;
   solid=ArtMesh(solid,folder+"/OuterCollision.asset");var boundary=new GameObject("Cave_OuterCollision237");boundary.transform.SetParent(wall.transform,false);boundary.AddComponent<MeshCollider>().sharedMesh=solid;
   foreach(var r in mine.GetComponentsInChildren<Renderer>().Where(r=>r.sharedMaterial!=null&&r.sharedMaterial.shader.name=="Oheangbu/Compact/CaveRock"))r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;
   Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   ExportFinalHeights(scene);
   string result="Exterior host mountain added from current terrain and authored cave ceiling; triangles="+triangles.Count/3+"; cave air opening retained; back faces visible and two-sided shadows enabled; same content/save slot.";
   File.WriteAllText(Output+"/CavePolish237/build.txt",result);return result;
  }

  static string CavePolishSurvey()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   var scene=SceneManager.GetSceneByPath(receipt.scene);bool opened=!scene.IsValid()||!scene.isLoaded;
   if(opened)scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
   try
   {
    var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
    var lines=new List<string>{"scene="+scene.path+" dirty="+scene.isDirty};
    foreach(var r in mine.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.Contains("Cave")||r.name.Contains("Mountain")||r.name.Contains("Approach")||r.name.Contains("Apron")))
     lines.Add(r.name+" active="+r.gameObject.activeInHierarchy+" enabled="+r.enabled+" bounds="+r.bounds+" material="+string.Join(";",r.sharedMaterials.Where(m=>m!=null).Select(m=>m.name+" shader="+m.shader.name+" cull="+(m.HasProperty("_Cull")?m.GetFloat("_Cull"):-1))));
    Directory.CreateDirectory(Output+"/CavePolish237");
    File.WriteAllText(Output+"/CavePolish237/survey.txt",string.Join("\n",lines));return string.Join("\n",lines);
   }
   finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
  }

  static string CavePolishCapture(string label)
  {
   var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();
   var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   var cameraObject=new GameObject("Temporary_CavePolishCamera");var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;
   var data=session.Walker.ViewCamera.GetComponent<UniversalAdditionalCameraData>();if(data!=null)EditorUtility.CopySerialized(data,camera.GetUniversalAdditionalCameraData());
   var poses=new[]{(new Vector3(3398,143,1886),new Vector3(3442,139,1845)),(new Vector3(3500,213,1870),new Vector3(3500,137,1804)),(new Vector3(3630,185,1790),new Vector3(3538,139,1790)),(new Vector3(3464,137.5f,1830),new Vector3(3440,138,1854))};
   var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
   try
   {
    camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=65;
    for(int i=0;i<poses.Length;i++)
    {
     camera.transform.SetPositionAndRotation(poses[i].Item1,Quaternion.LookRotation(poses[i].Item2-poses[i].Item1));camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
     File.WriteAllBytes(Output+"/CavePolish237/"+label+"_"+i+".png",image.EncodeToPNG());
    }
    return "Captured four candidate cave inspection viewpoints: "+label;
   }
   finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(cameraObject);}
  }
 }
}
