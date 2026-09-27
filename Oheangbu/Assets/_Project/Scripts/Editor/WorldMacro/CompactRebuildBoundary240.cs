using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  static string Boundary240(string command) {
   var scene=SceneManager.GetActiveScene();var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(scene.path!=receipt.scene)throw new Exception("Latest candidate required");
   var roots=scene.GetRootGameObjects();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   string dir=Output+"/Boundary240";Directory.CreateDirectory(dir);
   if(command=="survey") {
    var lines=new List<string>{"play="+EditorApplication.isPlaying};
    var door=session.Content.Points.Single(p=>p.Id=="geumpyo_inn").Position;
    foreach(var r in roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).Where(r=>Vector3.Distance(r.bounds.center,door)<25||r.name=="Continuous_Approach_Soil"||r.name.StartsWith("Terrain_"))) {
     lines.Add(HierarchyPath(r.transform)+" enabled="+r.enabled+" bounds="+r.bounds+" mesh="+AssetDatabase.GetAssetPath((r.GetComponent<MeshFilter>()!=null?r.GetComponent<MeshFilter>().sharedMesh:null)));
     foreach(var m in r.sharedMaterials.Where(m=>m!=null))lines.Add("  "+AssetDatabase.GetAssetPath(m)+" shader="+m.shader.name+" detail="+(m.HasProperty("_RebuildDetailStrength")?m.GetFloat("_RebuildDetailStrength"):-1));
    }
    foreach(var c in roots.SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>Vector3.Distance(c.bounds.center,door)<12))lines.Add("COLLIDER "+HierarchyPath(c.transform)+" "+c.GetType().Name+" "+c.bounds);
    string result=string.Join("\n",lines);File.WriteAllText(dir+(EditorApplication.isPlaying?"/survey_play.txt":"/survey_edit.txt"),result);return "Survey written "+lines.Count+" lines";
   }
   if(command.StartsWith("capture:")) {
    var go=new GameObject("Temporary_Boundary240");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;camera.useOcclusionCulling=false;EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
    var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).ToArray();var observers=art.Select(a=>a.Observer).ToArray();foreach(var a in art)a.Observer=camera;
    var eyes=new[]{new Vector3(3398,140,1886),new Vector3(3410,137.8f,1880),new Vector3(3109,52.6f,2251),new Vector3(3108.7f,52.7f,2256)};
    var targets=new[]{new Vector3(3435,137,1857),new Vector3(3390,133,1900),new Vector3(3109,51.5f,2259),new Vector3(3108.7f,51,2259)};
    var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
    try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=65;for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(dir+"/"+command.Substring(8)+"_"+i+".png",image.EncodeToPNG());}}
    finally{for(int i=0;i<art.Length;i++)art[i].Observer=observers[i];camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}return "Four boundary captures";
   }
   if(command=="build")return BuildBoundary240(scene,receipt);
   if(command=="audit")return AuditBoundary240(scene);
   throw new ArgumentException(command);
  }
  static string BuildBoundary240(Scene scene,MigrationReceipt receipt) {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Boundary240";DevSceneKit.EnsureFolder(folder);
   var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");var inn=roots.Single(g=>g.name=="geumpyo_inn");
   var h1=inn.GetComponentsInChildren<Renderer>().Single(r=>r.name=="h1_house_ground");
   // This mesh contains an atlas with different wood, stone and earth islands.
   // Restore its original sampling rather than projecting that atlas across the building.
   var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/h1_house_ground_Muted.mat");
   var porch=new Material(source);if(porch.HasProperty("_WashStrength"))porch.SetFloat("_WashStrength",0);
   h1.sharedMaterial=SurveyMaterial(porch,folder+"/Porch.mat");
   var ground=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).First(r=>r.name.StartsWith("Terrain_")).sharedMaterial;
   var approach=mine.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Continuous_Approach_Soil");
   approach.GetComponent<Renderer>().sharedMaterial=ground;
   var old=roots.SingleOrDefault(g=>g.name=="Boundary240_Dressing");if(old!=null)Object.DestroyImmediate(old);roots=scene.GetRootGameObjects();
   var root=new GameObject("Boundary240_Dressing");var sample=FinalSurface(scene);Physics.SyncTransforms();
   // Sink only the old mouth's low, exterior tails beneath the final host ground.
   // Gallery roof, inner walls and walkable deck are retained.
   var wall=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Natural_Cave_Interior");
   var lining=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Mesh>(Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/CavePolish237/ContinuousInterior.asset"));
   var wallVertices=lining.vertices;int embedded=0;
   for(int i=0;i<wallVertices.Length;i++){var p=wall.transform.TransformPoint(wallVertices[i]);
    float inward=Vector3.Dot(p-new Vector3(3435,135.88f,1857),new Vector3(.728f,0,-.686f));
    if(inward>=-12||p.y>136)continue;float y=sample(p.x,p.z).point.y;
    if(p.y>y-.18f&&p.y<y+.6f){p.y=y-.18f;wallVertices[i]=wall.transform.InverseTransformPoint(p);embedded++;}}
   lining.vertices=wallVertices;lining.RecalculateNormals();lining.RecalculateBounds();lining.RecalculateTangents();lining=ArtMesh(lining,folder+"/EmbeddedLining.asset");wall.sharedMesh=lining;wall.GetComponent<MeshFilter>().sharedMesh=lining;
   var outer=wall.transform.Find("Cave_OuterCollision237").GetComponent<MeshCollider>();var reversed=Object.Instantiate(lining);var reverse=reversed.triangles;
   for(int i=0;i<reverse.Length;i+=3){int a=reverse[i];reverse[i]=reverse[i+2];reverse[i+2]=a;}reversed.triangles=reverse;reversed.RecalculateNormals();outer.sharedMesh=ArtMesh(reversed,folder+"/EmbeddedOuter.asset");
   // Count welded boundary edges: the imported approach has split triangle vertices.
   var mesh=approach.sharedMesh;var local=mesh.vertices;var world=local.Select(approach.transform.TransformPoint).ToArray();
   var keys=new Dictionary<Vector3Int,int>();var welded=new List<Vector3>();var ids=new int[world.Length];
   for(int i=0;i<world.Length;i++){var p=world[i];var key=Vector3Int.RoundToInt(p*1000);if(!keys.TryGetValue(key,out int id)){id=welded.Count;keys.Add(key,id);welded.Add(p);}ids[i]=id;}
   var edges=new Dictionary<(int,int),int>();var triangles=mesh.triangles;
   for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++){int a=ids[triangles[i+j]],b=ids[triangles[i+(j+1)%3]];var key=a<b?(a,b):(b,a);edges.TryGetValue(key,out int count);edges[key]=count+1;}
   var vs=new List<Vector3>();var ts=new List<int>();int joins=0;
   foreach(var edge in edges.Where(e=>e.Value==1)) {
    var a=welded[edge.Key.Item1];var b=welded[edge.Key.Item2];var mid=(a+b)*.5f;
    if(Vector3.Dot(mid-new Vector3(3435,135.88f,1857),new Vector3(.728f,0,-.686f))>-3)continue;
    var delta=b-a;delta.y=0;if(delta.magnitude<.05f)continue;
    var side=new Vector3(-delta.z,0,delta.x).normalized;
    // Determine the exposed side from the existing approach collider.
    var collider=approach.GetComponent<MeshCollider>();
    if(collider.Raycast(new Ray(mid+side*.3f+Vector3.up*15,Vector3.down),out var inside,30))side=-side;
    var aa=a+side*2.4f;var bb=b+side*2.4f;aa.y=sample(aa.x,aa.z).point.y-.025f;bb.y=sample(bb.x,bb.z).point.y-.025f;
    if(aa.y>a.y+.05f||bb.y>b.y+.05f||Mathf.Max(a.y-aa.y,b.y-bb.y)>1.8f)continue;
    int n=vs.Count;vs.Add(a);vs.Add(b);vs.Add(bb);vs.Add(aa);
    if(Vector3.Cross(b-a,bb-a).y>0){ts.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}else ts.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});joins++;
   }
   var skirt=new Mesh{name="ApproachBank240",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};skirt.SetVertices(vs);skirt.SetTriangles(ts,0);skirt.RecalculateNormals();skirt.RecalculateBounds();skirt=ArtMesh(skirt,folder+"/ApproachBank.asset");
   var bank=new GameObject("Approach_Bank240");bank.transform.SetParent(root.transform,false);bank.AddComponent<MeshFilter>().sharedMesh=skirt;bank.AddComponent<MeshRenderer>().sharedMaterial=ground;bank.AddComponent<MeshCollider>().sharedMesh=skirt;
   // Small broken rock at the path shoulders, below capsule step height. No hidden walls.
   var rockMaterial=new Material(AssetDatabase.LoadAssetAtPath<Material>(Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Surface239/Rock.mat"));
   rockMaterial.SetColor("_BaseColor",new Color(.38f,.37f,.34f));rockMaterial.SetFloat("_Ambient",.42f);rockMaterial.SetFloat("_Height",.012f);rockMaterial.SetFloat("_Scale",.6f);
   rockMaterial=SurveyMaterial(rockMaterial,folder+"/LooseRock.mat");
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   // Reproject just the changed cave-host area; retain IDs, scale and all other placements.
   var sheet=Object.Instantiate(art.Sheet);sheet.name="Boundary240Placements";int grounded=0;
   foreach(var placed in sheet.FixedPlacements){var p=placed.Position;if(p.x<3370||p.x>3616||p.z<1700||p.z>1930)continue;
    var proto=sheet.Prototypes.Single(t=>t.Id==placed.PrototypeId);if(proto.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Prop)continue;
    float y=sample(p.x,p.z).point.y-(proto.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Rock?proto.Size.y*placed.Scale*.17f:0);
    if(Mathf.Abs(y-p.y)<.025f)continue;placed.Position=new Vector3(p.x,y,p.z);var collision=art.transform.Find(placed.Id);if(collision!=null)collision.position=placed.Position;grounded++;}
   art.Sheet=SavePrivate(sheet,folder+"/Placements.asset");art.Invalidate();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();manifest.Art=art.Sheet;EditorUtility.SetDirty(manifest);
   File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(art.Sheet,true));
   var prototype=art.Sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_Rock_L");
   var random=new System.Random(240);int stones=0;
   for(int i=0;i<56;i++) {
    float t=(float)random.NextDouble();var center=Vector3.Lerp(new Vector3(3430,0,1863),new Vector3(3390,0,1901),t);var side=new Vector3(.69f,0,.72f)*(i%2==0?1:-1);
    var p=center+side*Mathf.Lerp(3.8f,7.5f,(float)random.NextDouble());var hit=sample(p.x,p.z);if(hit.normal.y<.7f)continue;
    if(approach.GetComponent<MeshCollider>().Raycast(new Ray(hit.point+Vector3.up*8,Vector3.down),out var deck,16)&&deck.point.y>hit.point.y)hit=deck;
    float scale=Mathf.Lerp(.06f,.19f,(float)random.NextDouble());p=hit.point-Vector3.up*prototype.Size.y*scale*.23f;
    var go=new GameObject("ShoulderRock_"+i);go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,(float)random.NextDouble()*360,0));go.transform.localScale=Vector3.one*scale;
    foreach(var part in prototype.Lods[0].Parts){var piece=new GameObject("Rock");piece.transform.SetParent(go.transform,false);piece.transform.localPosition=part.Local.GetColumn(3);piece.transform.localRotation=part.Local.rotation;piece.transform.localScale=part.Local.lossyScale;
     piece.AddComponent<MeshFilter>().sharedMesh=part.Mesh;var renderer=piece.AddComponent<MeshRenderer>();renderer.sharedMaterials=Enumerable.Repeat(rockMaterial,part.Mesh.subMeshCount).ToArray();}
    var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.015f,go.GetComponentsInChildren<Renderer>())});lod.RecalculateBounds();stones++;
   }
   AssetDatabase.SaveAssets();Physics.SyncTransforms();EditorSceneManager.SaveScene(scene);
   string result="Restored original inn atlas; approach uses neighbouring soil; "+joins+" bank joins, "+embedded+" embedded wall-tail vertices, "+grounded+" replanted placements, "+stones+" grounded shoulder rocks; content/door/checkpoint unchanged";
   File.WriteAllText(Output+"/Boundary240/build.txt",result);return result;
  }
  static string AuditBoundary240(Scene scene) {
   var roots=scene.GetRootGameObjects();var lines=new List<string>();void Check(bool ok,string label)=>lines.Add((ok?"PASS ":"FAIL ")+label);
   var dressing=roots.Single(g=>g.name=="Boundary240_Dressing");var bank=dressing.GetComponentsInChildren<MeshCollider>().Single();
   Check(bank.sharedMesh==bank.GetComponent<MeshFilter>().sharedMesh,"visible bank equals collision mesh");
   Check(dressing.GetComponentsInChildren<Collider>().Length==1,"small shoulder rocks do not create capsule obstacles");
   var inn=roots.Single(g=>g.name=="geumpyo_inn");var porch=inn.GetComponentsInChildren<Renderer>().Single(r=>r.name=="h1_house_ground");
   Check(AssetDatabase.GetAssetPath(porch.sharedMaterial).EndsWith("/Boundary240/Porch.mat")&&porch.sharedMaterial.shader.name=="Oheangbu/DesaturatedAssetLit","inn uses original atlas UV shader");
   var materials=dressing.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Append(porch.sharedMaterial).Distinct();
   Check(materials.All(m=>m!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"changed materials compile");
   var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();Check(session.Content.SaveSlot=="world-demo-compact-cave-v4","candidate slot retained");
   string report=string.Join("\n",lines);File.WriteAllText(Output+"/Boundary240/audit.txt",report);return report;
  }

 }
}
