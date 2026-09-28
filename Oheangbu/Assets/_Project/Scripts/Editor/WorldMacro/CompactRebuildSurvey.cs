using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 static string SurveyInventory(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
 try{var roots=scene.GetRootGameObjects();var s=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
 var lines=new List<string>();
 foreach(var root in roots.Where(r=>r.name.ToLowerInvariant().Contains("inn")||r.name=="mine")){
 lines.Add("ROOT "+root.name+" "+root.transform.position+" rotation="+root.transform.eulerAngles);
 foreach(var r in root.GetComponentsInChildren<Renderer>(true).Where(r=>root.name!="mine"||r.name.StartsWith("Natural_Cave")))
 lines.Add("RENDER "+HierarchyPath(r.transform)+" pos="+r.transform.position+" rot="+r.transform.eulerAngles+" bounds="+r.bounds);
 foreach(var l in root.GetComponentsInChildren<Light>(true))lines.Add("LIGHT "+HierarchyPath(l.transform)+" pos="+l.transform.position+" intensity="+l.intensity+" range="+l.range);
 }
 foreach(var a in s.Actors)lines.Add("ACTOR "+a.Id+" "+string.Join(";",a.GetComponentsInChildren<Renderer>(true).Select(r=>HierarchyPath(r.transform)+" "+r.bounds)));
 foreach(var p in s.PreviewPoints)lines.Add("NPC "+p.Id+" "+HierarchyPath(p.transform)+" "+string.Join(";",p.GetComponentsInChildren<Renderer>(true).Select(r=>r.name+" "+r.bounds)));
 File.WriteAllText(Output+"/Cartography/survey_inventory.txt",string.Join("\n",lines));return "Inventory saved; "+lines.Count+" entries";
 }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
 }

 static Texture2D SurveyTexture(string source,string path,int maxSize=4096){
 File.Copy(Output+"/Cartography/"+source,path,true);AssetDatabase.ImportAsset(path);
 var i=(TextureImporter)AssetImporter.GetAtPath(path);i.npotScale=TextureImporterNPOTScale.None;i.maxTextureSize=maxSize;i.textureCompression=TextureImporterCompression.CompressedHQ;i.mipmapEnabled=true;i.sRGBTexture=true;i.wrapMode=TextureWrapMode.Clamp;i.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 }
 static string SurveyBuild(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
 try{
 var roots=scene.GetRootGameObjects();var all=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
 var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var content=session.Content;
 ui.MapData.ExploredMap=SurveyTexture("explored.png",folder+"/ExploredTerrain.png",8192);
 ui.MapData.RegionTiles[0].Texture=SurveyTexture("region.png",folder+"/PaintedTerrain_Cheongrim.png");
 var zone=ui.MapData.Zones.Single(z=>z.Id=="mine_interior");zone.Illustration=SurveyTexture("cave.png",folder+"/CavePlan.png");zone.IllustrationWorldUv=new Rect(3420f/4000,1750f/6000,160f/4000,120f/6000);
 var originalDoor=all.Single(t=>t.name=="house_Re_Door");var door=SurveyDoor(originalDoor,folder,content.InnCheckpointFeet);var rs=door.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)throw new Exception("Inn door has no renderer");
 var bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);
 var rest=content.Points.Single(p=>p.Id=="geumpyo_inn");var from=content.InnCheckpointFeet;var toward=from-bounds.center;toward.y=0;toward.Normalize();
 // The threshold interaction sits outside the actual door, while respawn stays at the existing safe yard checkpoint.
 rest.Position=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)+toward*.6f;rest.Radius=3.6f;
 foreach(var cp in roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)).Where(cp=>cp.Id==rest.Id))cp.transform.position=rest.Position;
 var binding=session.InteractionVisuals.Single(b=>b.Id==rest.Id);binding.Renderers=rs;
 foreach(var r in rs)GameObjectUtility.SetStaticEditorFlags(r.gameObject,GameObjectUtility.GetStaticEditorFlags(r.gameObject)&~StaticEditorFlags.BatchingStatic);
 var mine=roots.Single(g=>g.name=="mine");var shader=Shader.Find("Oheangbu/Compact/CaveRock");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Cave shader error");
 foreach(var r in mine.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Natural_Cave_Interior"||r.name=="Natural_Cave_Floor")){
 bool floor=r.name.EndsWith("Floor");var mat=new Material(shader){name=floor?"CaveFloor234":"CaveRock234"};mat.SetColor("_BaseColor",floor?new Color(.38f,.35f,.29f):new Color(.29f,.31f,.30f));mat.SetFloat("_Ambient",floor?.34f:.28f);
 r.sharedMaterial=SurveyMaterial(mat,folder+"/CaveV4/"+mat.name+".mat");}
 foreach(var l in mine.GetComponentsInChildren<Light>(true)){
 if(l.name.StartsWith("Ore_Bounce")){l.enabled=false;continue;}
 if(l.transform.parent.name.StartsWith("Gallery_Lantern")){l.color=new Color(1,.72f,.42f);l.intensity=5;l.range=13;l.shadows=LightShadows.None;}
 }
 // Shallow rock shelves attach to existing walls; no new walkable geometry or collision.
 var caveLayout=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
 var rockRoot=mine.transform.Find("Cave_RockShelves234");if(rockRoot!=null)Object.DestroyImmediate(rockRoot.gameObject);rockRoot=new GameObject("Cave_RockShelves234").transform;rockRoot.SetParent(mine.transform,false);
 var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single().Art;
 var proto=art.Prototypes.First(p=>p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Rock);var part=proto.Lods[0].Parts[0];
 var wall=mine.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name=="Natural_Cave_Interior");var wallMaterial=wall.GetComponent<Renderer>().sharedMaterial;var placements=new List<string>();
 foreach(var point in caveLayout.main.Concat(caveLayout.branch).Skip(1))foreach(var direction in new[]{Vector3.left,Vector3.right,Vector3.forward,Vector3.back}){
 var ray=new Ray(point+Vector3.up*1.4f,direction);if(!wall.Raycast(ray,out var hit,5.8f)||hit.distance<2.4f)continue;
 var g=new GameObject("RockShelf_"+placements.Count);g.transform.SetParent(rockRoot,false);g.AddComponent<MeshFilter>().sharedMesh=part.Mesh;g.AddComponent<MeshRenderer>().sharedMaterial=wallMaterial;
 var b=part.Mesh.bounds;g.transform.rotation=Quaternion.Euler(11,placements.Count*137.5f,7);g.transform.localScale=Vector3.one*(1.5f/Mathf.Max(b.size.x,b.size.y,b.size.z));g.transform.position=hit.point-hit.normal*.38f-g.transform.TransformVector(b.center);placements.Add(g.name+" "+g.transform.position);
 }
 File.WriteAllText(Output+"/Cartography/cave_shelves.txt",string.Join("\n",placements));
 ui.MapData.Revision="compact-cave-v4-survey234";
 EditorUtility.SetDirty(content);EditorUtility.SetDirty(ui.MapData);EditorUtility.SetDirty(session);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
 string result="Survey atlas/cave map/private rock materials saved. Rest target="+HierarchyPath(door)+" bounds="+bounds+" interaction="+rest.Position+" yard checkpoint preserved="+content.InnCheckpointFeet;
 File.WriteAllText(Output+"/Cartography/survey_build.txt",result);return result;
 }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
 }

 static Material SurveyMaterial(Material mat,string path){var old=AssetDatabase.LoadAssetAtPath<Material>(path);if(old==null){AssetDatabase.CreateAsset(mat,path);return mat;}old.shader=mat.shader;old.CopyPropertiesFromMaterial(mat);Object.DestroyImmediate(mat);EditorUtility.SetDirty(old);return old;}
 static string SurveyModels(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
 string dir=Folder+"/Characters234";DevSceneKit.EnsureFolder(dir);var lines=new List<string>();
 foreach(var id in new[]{"logger","herbalist","innkeeper","mine_beast","mine_fire"}){
 string target=dir+"/"+id;DevSceneKit.EnsureFolder(target);string source=Output+"/Characters/"+id+"/refine/";
 foreach(string f in new[]{"model_urls_fbx.fbx","texture_urls_0_base_color.png","texture_urls_0_normal.png"}){File.Copy(source+f,target+"/"+f,true);AssetDatabase.ImportAsset(target+"/"+f);}
 var ti=(TextureImporter)AssetImporter.GetAtPath(target+"/texture_urls_0_normal.png");ti.textureType=TextureImporterType.NormalMap;ti.maxTextureSize=2048;ti.SaveAndReimport();
 var importer=(ModelImporter)AssetImporter.GetAtPath(target+"/model_urls_fbx.fbx");importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.animationType=ModelImporterAnimationType.None;importer.importAnimation=false;importer.SaveAndReimport();
 var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=id+"_mat"};mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(target+"/texture_urls_0_base_color.png"));mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(target+"/texture_urls_0_normal.png"));mat.EnableKeyword("_NORMALMAP");mat.SetFloat("_BumpScale",.45f);mat.SetFloat("_Smoothness",.12f);mat=SurveyMaterial(mat,target+"/Surface.mat");
 var root=new GameObject(id+"_GeneratedReview");try{
 var mesh=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(target+"/model_urls_fbx.fbx"),root.transform);mesh.name="Model";
 var renderers=mesh.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers){b.Encapsulate(r.bounds);r.sharedMaterial=mat;}
 float height=id=="mine_beast"?1.05f:id=="mine_fire"?.95f:id=="innkeeper"?1.62f:1.70f;float scale=height/b.size.y;mesh.transform.localScale*=scale;mesh.transform.position-=new Vector3(b.center.x,b.min.y,b.center.z)*scale;
 PrefabUtility.SaveAsPrefabAsset(root,target+"/"+id+".prefab");
 lines.Add(id+" height="+height+" renderers="+renderers.Length+" generated geometry/material prefab; rig and locomotion not assigned");
 }finally{Object.DestroyImmediate(root);}
 }
 AssetDatabase.SaveAssets();File.WriteAllText(Output+"/Characters/unity_import.txt",string.Join("\n",lines));return string.Join("\n",lines);
 }

 static Transform SurveyDoor(Transform source,string folder,Vector3 yard){
 var filter=source.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;var existing=source.Find("Inn_Rest_Door");
 if(existing!=null){
 var combined=new Mesh();combined.CombineMeshes(new[]{new CombineInstance{mesh=mesh,transform=Matrix4x4.identity},new CombineInstance{mesh=existing.GetComponent<MeshFilter>().sharedMesh,transform=Matrix4x4.identity}},true,true);mesh=combined;Object.DestroyImmediate(existing.gameObject);
 }
 var v=mesh.vertices;var tris=mesh.triangles;var world=v.Select(p=>source.TransformPoint(p)).ToArray();
 var planes=new Dictionary<string,(int axis,float coordinate,float area,Vector3 sum)>();
 for(int i=0;i<tris.Length;i+=3){var a=world[tris[i]];var b=world[tris[i+1]];var c=world[tris[i+2]];var cross=Vector3.Cross(b-a,c-a);float area=cross.magnitude*.5f;if(area<.00001f)continue;var n=cross.normalized;if(Mathf.Max(Mathf.Abs(n.x),Mathf.Abs(n.z))<.96f)continue;int axis=Mathf.Abs(n.x)>Mathf.Abs(n.z)?0:2;var center=(a+b+c)/3;float coordinate=Mathf.Round(center[axis]*10)/10;string key=axis+":"+coordinate;
 if(!planes.TryGetValue(key,out var plane))plane=(axis,coordinate,0,Vector3.zero);plane.area+=area;plane.sum+=center*area;planes[key]=plane;
 }
 var yardDirection=yard-source.GetComponent<Renderer>().bounds.center;int facingAxis=Mathf.Abs(yardDirection.x)>Mathf.Abs(yardDirection.z)?0:2;
 var chosen=planes.Values.Where(p=>p.area>.3f&&p.axis==facingAxis).OrderBy(p=>Vector3.Distance(p.sum/p.area,yard)).First();
 var selected=new List<int>();var other=new List<int>();
 for(int i=0;i<tris.Length;i+=3){float center=(world[tris[i]][chosen.axis]+world[tris[i+1]][chosen.axis]+world[tris[i+2]][chosen.axis])/3;var dest=Mathf.Abs(center-chosen.coordinate)<.13f?selected:other;dest.Add(tris[i]);dest.Add(tris[i+1]);dest.Add(tris[i+2]);}
 File.WriteAllText(Output+"/Cartography/door_planes.txt",string.Join("\n",planes.Values.Select(p=>"axis="+p.axis+" plane="+p.coordinate+" area="+p.area+" center="+(p.sum/p.area)))+"\nCHOSEN "+chosen);
 if(selected.Count<6||other.Count<6)throw new Exception("Cannot isolate inn threshold from shared doors");
 Mesh Extract(List<int> faces,string name){var result=new Mesh{name=name};var ids=faces.Distinct().ToArray();var map=ids.Select((id,i)=>(id,i)).ToDictionary(p=>p.id,p=>p.i);result.vertices=ids.Select(i=>v[i]).ToArray();result.normals=ids.Select(i=>mesh.normals[i]).ToArray();result.uv=ids.Select(i=>mesh.uv[i]).ToArray();result.triangles=faces.Select(i=>map[i]).ToArray();result.RecalculateBounds();return ArtMesh(result,folder+"/"+name+".asset");}
 var g=new GameObject("Inn_Rest_Door");g.transform.SetParent(source,false);g.AddComponent<MeshFilter>().sharedMesh=Extract(selected,"InnRestDoor234");g.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<Renderer>().sharedMaterials;
 filter.sharedMesh=Extract(other,"InnOtherDoors234");return g.transform;
 }

 }
}
