using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Terrain243(string command,bool dynamic=false)
  {
   var scene=SceneManager.GetActiveScene();
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(scene.path!=receipt.scene)throw new Exception("Latest candidate required");
   string revision=dynamic?"Terrain244":"Terrain243";
   string dir=Output+"/"+revision;Directory.CreateDirectory(dir);
   var roots=scene.GetRootGameObjects();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   var terrain=roots.Single(g=>g.name=="Compact_Rebuild_Terrain");
   if(command.StartsWith("capture:"))return CaptureTerrain243(session,command.Substring(8),revision);
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Terrain authoring requires Edit");
   string baseFolder=Path.GetDirectoryName(receipt.scene).Replace('\\','/'),folder=baseFolder+"/"+revision;
   string sourceFolder=dynamic?baseFolder+"/Terrain243":folder;
   if(command=="build")
   {
    DevSceneKit.EnsureFolder(folder);DevSceneKit.EnsureFolder(folder+"/Source");
    File.Copy(dir+"/relief_delta.bytes",folder+"/Relief.bytes",true);AssetDatabase.ImportAsset(folder+"/Relief.bytes");
    var layout=manifest.Layout;layout.MountainRelief=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"/Relief.bytes");
    layout.MountainReliefWidth=801;layout.MountainReliefHeight=1201;layout.MountainReliefCell=5;
    layout.MountainReliefSource="Mapzen Terrain Tiles / USGS SRTM; Korea 35.08N 127.34E, 4x6km. Vertical scale "+(dynamic?"0.86":"0.48")+", valley datum fitted, authored corridors preserved. Adapted game terrain.";
    var grid=new CompactReliefGrid(layout);int changed=0;float maxDelta=0;
    foreach(var filter in terrain.GetComponentsInChildren<MeshFilter>())
    {
     string sourcePath=sourceFolder+"/Source/"+filter.name+".asset";
     if(dynamic&&AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath)==null)throw new Exception("Revision 242 source baseline missing; cannot add relief to already displaced terrain");
     if(AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath)==null)
      AssetDatabase.CreateAsset(Object.Instantiate(filter.sharedMesh),sourcePath);
     var source=AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);var mesh=Object.Instantiate(source);
     var vertices=source.vertices;var normals=source.normals;
     for(int i=0;i<vertices.Length;i++)
     {
      var world=filter.transform.TransformPoint(vertices[i]);float delta=grid.Sample(world.x,world.z);
      if(Mathf.Abs(delta)>.025f)changed++;maxDelta=Mathf.Max(maxDelta,Mathf.Abs(delta));
      world.y+=delta;vertices[i]=filter.transform.InverseTransformPoint(world);
      if(Mathf.Abs(delta)<.000001f)continue;
      var n=filter.transform.TransformDirection(normals[i]);
      if(n.y>.08f)
      {
       float dx=(grid.Sample(world.x+2.5f,world.z)-grid.Sample(world.x-2.5f,world.z))/5;
       float dz=(grid.Sample(world.x,world.z+2.5f)-grid.Sample(world.x,world.z-2.5f))/5;
       normals[i]=filter.transform.InverseTransformDirection(new Vector3(n.x/n.y-dx,1,n.z/n.y-dz).normalized);
      }
     }
     mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();
     mesh=ArtMesh(mesh,folder+"/"+filter.name+".asset");filter.sharedMesh=mesh;
     var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
    }
    Physics.SyncTransforms();
    var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
    string sourceSheet=sourceFolder+"/SourcePlacements.asset";
    if(dynamic&&AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(sourceSheet)==null)throw new Exception("Original placement baseline missing");
    if(AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(sourceSheet)==null)
     AssetDatabase.CreateAsset(Object.Instantiate(art.Sheet),sourceSheet);
    var sheet=Object.Instantiate(AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(sourceSheet));sheet.name="Placements";
    var ground=FinalSurface(scene);int grounded=0,collisions=0;
    foreach(var p in sheet.FixedPlacements)
    {
     var proto=sheet.Prototypes.Single(t=>t.Id==p.PrototypeId);
     if(proto.Category==WorldMacroDressingSheetSO.Kind.Prop)continue;
     float y=ground(p.Position.x,p.Position.z).point.y-(proto.Category==WorldMacroDressingSheetSO.Kind.Rock?proto.Size.y*p.Scale*.17f:0);
     if(Mathf.Abs(y-p.Position.y)>.025f)grounded++;
     p.Position=new Vector3(p.Position.x,y,p.Position.z);
     var c=art.transform.Find(p.Id);if(c!=null){c.position=p.Position;collisions++;}
    }
    art.Sheet=SavePrivate(sheet,folder+"/Placements.asset");art.Invalidate();manifest.Art=art.Sheet;
    BakeArtGeography(baseFolder+"/Art",ground);
    EditorUtility.SetDirty(manifest);EditorUtility.SetDirty(layout);EditorUtility.SetDirty(art);
    File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));
    File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(art.Sheet,true));
    AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    string report="Changed terrain vertices="+changed+"; max abs delta="+maxDelta+"m; regrounded placements="+grounded+"; matching colliders="+collisions+". Terrain visual/collider share private meshes; source baseline retained.\n"+ExportFinalHeights(scene);
    File.WriteAllText(dir+"/build.txt",report);return report;
   }
   if(command=="audit")
   {
    var lines=new List<string>();void Check(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
    var filters=terrain.GetComponentsInChildren<MeshFilter>();var grid=new CompactReliefGrid(manifest.Layout);
    Check(filters.Length==96&&filters.All(f=>f.sharedMesh==f.GetComponent<MeshCollider>().sharedMesh&&AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(folder+"/")),"96 private terrain meshes match visible/collision geometry");
    var edge=new Dictionary<Vector2,Vector3>();int seam=0;float worst=0;
    foreach(var f in filters)foreach(var v in f.sharedMesh.vertices)
    {
     var p=f.transform.TransformPoint(v);if(Mathf.Abs(p.x/500-Mathf.Round(p.x/500))>.00001f&&Mathf.Abs(p.z/500-Mathf.Round(p.z/500))>.00001f)continue;
     var key=new Vector2(p.x,p.z);if(edge.TryGetValue(key,out var other)){float d=Mathf.Abs(p.y-other.y);worst=Mathf.Max(worst,d);if(d>.01f)seam++;}else edge[key]=p;
    }
    Check(seam==0,"shared tile edges mismatch="+seam+" worst="+worst+"m");
    var walk=JsonUtility.FromJson<WalkReceipt>(File.ReadAllText(Output+"/art_route.json"));
    Check(walk.trail.All(p=>Mathf.Abs(grid.Sample(p.x,p.z))<.00001f),"entire recorded mine-inn corridor has zero terrain displacement");
    Check(session.Content.Points.All(p=>Mathf.Abs(grid.Sample(p.Position.x,p.Position.z))<.00001f),"live interaction anchors have zero terrain displacement");
    var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var surface=FinalSurface(scene);int bad=0,collisionBad=0;float gap=0;
    foreach(var p in art.Sheet.FixedPlacements)
    {
     var proto=art.Sheet.Prototypes.Single(t=>t.Id==p.PrototypeId);if(proto.Category==WorldMacroDressingSheetSO.Kind.Prop)continue;
     float expected=surface(p.Position.x,p.Position.z).point.y-(proto.Category==WorldMacroDressingSheetSO.Kind.Rock?proto.Size.y*p.Scale*.17f:0);
     float error=Mathf.Abs(p.Position.y-expected);gap=Mathf.Max(gap,error);if(error>.03f)bad++;
     var c=art.transform.Find(p.Id);if(c!=null&&Vector3.Distance(c.position,p.Position)>.001f)collisionBad++;
    }
    Check(bad==0&&collisionBad==0,"placement grounding failures="+bad+" collider mismatches="+collisionBad+" maximum="+gap+"m");
    Check(art.Sheet.FixedPlacements.Select(p=>p.Id).Distinct().Count()==art.Sheet.FixedPlacements.Length,"placement IDs remain unique");
    Check(session.InnRestPresentation!=null&&session.InnRestPresentation.Door!=null,"door rest wiring retained");
    Check(session.Content.SaveSlot=="world-demo-compact-cave-v4","save slot retained");
    var mats=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct();
    Check(mats.All(m=>m.shader!=null&&!ShaderUtil.ShaderHasError(m.shader)),"referenced shaders compile");
    string report=string.Join("\n",lines);File.WriteAllText(dir+"/audit.txt",report);return report;
   }
   throw new ArgumentException(command);
  }

  static string CaptureTerrain243(WorldMacroPlaytestSession session,string tag,string revision="Terrain243")
  {
   var go=new GameObject("Terrain243_ReviewCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;camera.useOcclusionCulling=false;
   EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).ToArray();
   var observers=art.Select(a=>a.Observer).ToArray();foreach(var a in art)a.Observer=camera;
   var eyes=new[]{new Vector3(3109,53,2236),new Vector3(3410,140,1880),new Vector3(3250,205,2260),new Vector3(3120,63,2220)};
   var targets=new[]{new Vector3(2990,110,2780),new Vector3(3020,85,2280),new Vector3(2820,130,2850),new Vector3(3780,180,2090)};
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=65;
    for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+revision+"/"+tag+"_"+i+".png",tex.EncodeToPNG());}}
   finally{for(int i=0;i<art.Length;i++)art[i].Observer=observers[i];camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Four matched terrain views captured: "+tag;
  }
 }
}
