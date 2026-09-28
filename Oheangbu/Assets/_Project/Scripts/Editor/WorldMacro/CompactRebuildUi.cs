using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEngine.AI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 static string ConfigureWorldInteraction(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');
 var skin=AssetDatabase.LoadAssetAtPath<WorldMacroHudSkinProfileSO>(folder+"/HUDSkin.asset");
 skin.InteractionLetterOffset=.10f;skin.InteractionLetterHeight=.22f;skin.InteractionLetterColor=new Color(.97f,.95f,.89f,1);
 EditorUtility.SetDirty(skin);AssetDatabase.SaveAssets();return "Candidate World Space F settings saved";
 }
 static string UiInventory(){
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
 try{var mine=scene.GetRootGameObjects().Single(g=>g.name=="mine");
 var text=string.Join("\n",mine.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.name+" pos="+m.transform.position+" bounds="+m.GetComponent<Renderer>()?.bounds));
 var content=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single().Content;
 text+="\nSTART="+content.StartFeet+" yaw="+content.StartYaw+"\n"+string.Join("\n",content.Points.Select(p=>p.Id+" "+p.Position));
 File.WriteAllText(Output+"/ui_cave_inventory.txt",text);return text;
 }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
 }
 static bool BelowGalleryRoof(MeshCollider floor,Vector3 point){
 if(floor==null)return false;
 // Include crown/trunk clearance around the roofed gallery, not just roots directly over its floor mesh.
 var bounds=floor.bounds;bounds.Expand(new Vector3(24,0,24));
 return point.x>=bounds.min.x&&point.x<=bounds.max.x&&point.z>=bounds.min.z&&point.z<=bounds.max.z&&point.y<floor.bounds.center.y+24;
 }
 static string UiBuild(){
 if(File.Exists(Output+"/CaveV4/build.txt"))throw new Exception("V4 cave owns layout; use cave-layout-build. Legacy UI/cave v3 builder is retired.");
 if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Canonical edit required");
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
 string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');
 try{
 var roots=scene.GetRootGameObjects();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();var content=manifest.Content;
 var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
 session.IconPresentation=true;
 var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
 var hud=roots.SelectMany(g=>g.GetComponentsInChildren<Oheangbu.App.HudController>(true)).Single();
 Sprite Icon(string name){string path=Folder+"/UIIcons/"+name+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);}
 var icons=ScriptableObject.CreateInstance<CompactUiProfileSO>();icons.Heart=Icon("heart");icons.Inn=Icon("inn");icons.Mountain=Icon("mountain");icons.Cave=Icon("cave");icons.Hand=Icon("hand");icons.InkBottle=hud.Skin.InkBottle;
 Sprite Menu(string n){string target=Folder+"/UIIcons/"+n+".png";if(!File.Exists(target))AssetDatabase.CopyAsset("Assets/_Project/Art/World/PineRestGame/MenuIcons/"+n+".png",target);return Icon(n);}
 icons.Resume=Menu("resume");icons.Exit=Menu("exit");icons.Save=Menu("save");icons=SavePrivate(icons,folder+"/UIIcons.asset");
 var theme=Object.Instantiate(ui.Theme);theme.Icons=icons;ui.Theme=SavePrivate(theme,folder+"/UITheme.asset");
 var skin=Object.Instantiate(hud.Skin);skin.InteractionLetterOffset=.10f;skin.Ink=icons.Ink;hud.Skin=SavePrivate(skin,folder+"/HUDSkin.asset");hud.Icons=icons;
 foreach(var call in roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPalanquinSummon>(true))){call.NaturalPresentation=true;call.Soundscape=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestAudio>(true)).FirstOrDefault();}
 var mine=roots.Single(g=>g.name=="mine");var floor=mine.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name=="Natural_Cave_Floor");
 var removed=manifest.Art.FixedPlacements.Where(p=>manifest.Art.Prototypes.Single(t=>t.Id==p.PrototypeId).Category!=WorldMacroDressingSheetSO.Kind.Prop&&BelowGalleryRoof(floor,p.Position)).Select(p=>p.Id).ToHashSet();
 if(removed.Count>0){manifest.Art.FixedPlacements=manifest.Art.FixedPlacements.Where(p=>!removed.Contains(p.Id)).ToArray();
 var artRoot=roots.Single(g=>g.name=="Rebuild_EnvironmentArt");foreach(Transform child in artRoot.transform.Cast<Transform>().ToArray())if(removed.Contains(child.name))Object.DestroyImmediate(child.gameObject);
 EditorUtility.SetDirty(manifest.Art);File.AppendAllText(Output+"/ui_cave_art_exclusion.txt",removed.Count+" outdoor placements inside the roofed gallery removed from private placement ledger and colliders.\n"+string.Join("\n",removed));}
 Vector3 Floor(float x,float z){if(!floor.Raycast(new Ray(new Vector3(x,145,z),Vector3.down),out var hit,20))throw new Exception("No cave floor at "+x+","+z);return hit.point+Vector3.up*.12f;}
 var outcrop=mine.transform.Find("Cave_Intro_Outcrop");if(outcrop!=null)Object.DestroyImmediate(outcrop.gameObject);
 var rock=manifest.Art.Prototypes.First(p=>p.Category==WorldMacroDressingSheetSO.Kind.Rock).Lods[0].Parts[0];
 var baffle=new GameObject("Cave_Intro_Outcrop");baffle.transform.SetParent(mine.transform,false);
 baffle.AddComponent<MeshFilter>().sharedMesh=rock.Mesh;var renderer=baffle.AddComponent<MeshRenderer>();renderer.sharedMaterial=mine.GetComponentsInChildren<MeshRenderer>().First(m=>m.name=="Natural_Cave_Interior").sharedMaterial;
 var bounds=rock.Mesh.bounds;var scale=new Vector3(7/bounds.size.x,7/bounds.size.y,6/bounds.size.z);baffle.transform.localScale=scale;
 baffle.transform.position=new Vector3(3533,135.76f,1797)+Vector3.up*3.5f-Vector3.Scale(bounds.center,scale);
 baffle.AddComponent<MeshCollider>().sharedMesh=rock.Mesh;
 content.StartFeet=Floor(3543,1791);content.StartYaw=287;content.SaveSlot="world-demo-compact-cave-v3";
 var checkpoint=content.Checkpoints.Single(c=>c.Id=="mine_start");checkpoint.Feet=content.StartFeet;checkpoint.Yaw=content.StartYaw;
 // Move the satchel and its interaction together, from the misplaced exterior branch to the worked inner gallery.
 var satchel=content.Points.Single(p=>p.Id=="worker_satchel");var next=Floor(3491,1812);var delta=next-satchel.Position;
 var bag=mine.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Worker_Straw_Bag");
 if(bag==null)throw new Exception("Satchel visual missing");bag.position+=delta;satchel.Position=next;
 var pointVisual=mine.GetComponentsInChildren<WorldMacroContentPoint>(true).FirstOrDefault(c=>c.Id=="worker_satchel");if(pointVisual!=null&&!pointVisual.transform.IsChildOf(bag))pointVisual.transform.position+=delta;
 satchel.Kind=PrologueInteractionKind.Evidence;
 satchel.Text="밑바닥이 터진 짚 보따리. 탄 심지 한 가닥이 옷감에 엉겼다.\n\n접힌 쪽지에는 금표 주막의 외상값이 적혀 있다.";
 // Stationary idle start faces the worked wall. The route back out traverses the full existing curved gallery.
 var body=session.Walker.Body;body.enabled=false;body.transform.SetPositionAndRotation(content.StartFeet,Quaternion.Euler(0,content.StartYaw,0));body.enabled=true;
 var map=ui.MapData;map.IllustratedMap=map.BaseMap;map.Revision=manifest.Generation+"-cave-v3";
 map.Markers=map.Markers.Where(m=>m.Kind!=WorldMapMarkerKind.Mountain).Concat(manifest.Layout.Ridges.SelectMany(r=>r.Spine.Select((p,i)=>new WorldMapMarkerSpec{Id="mountain_"+r.Id+"_"+i,Kind=WorldMapMarkerKind.Mountain,WorldXZ=p,InitiallyDiscovered=true}))).ToArray();
 var mineMarker=map.Markers.Single(m=>m.Id=="mine");mineMarker.WorldXZ=new Vector2(3438,1835);mineMarker.InitiallyDiscovered=false;
 map.Zones=new[]{new WorldMapZoneSpec{Id="mine_interior",Label="폐광",MinimumY=130,MaximumY=150,
 Polygon=new[]{new Vector2(3435,1760),new Vector2(3560,1760),new Vector2(3560,1855),new Vector2(3435,1855)},
 DetailPath=new[]{new Vector2(3543,1791),new Vector2(3520,1800),new Vector2(3491,1812),new Vector2(3463,1814),new Vector2(3441,1831)},
 DetailLines=new[]{new WorldMapLineSpec{Id="gallery_wash",Kind=WorldMapLineKind.DetailFill,PixelWidth=28,
 Points=new[]{new Vector2(3543,1791),new Vector2(3520,1800),new Vector2(3491,1812),new Vector2(3463,1814),new Vector2(3441,1831)}}}}};
 foreach(var line in map.Lines)line.PixelWidth=3;
 icons.name="UIIcons";ui.Theme.name="UITheme";hud.Skin.name="HUDSkin";map.name="Map";
 EditorUtility.SetDirty(content);EditorUtility.SetDirty(map);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
 Physics.SyncTransforms();var origin=content.StartFeet+Vector3.up*1.6f;var direction=Quaternion.Euler(0,content.StartYaw,0)*Vector3.forward;
 bool wall=Physics.RaycastAll(origin,direction,40,~0,QueryTriggerInteraction.Ignore).Any(h=>h.collider.transform.IsChildOf(mine.transform));
 string result="UI profile assigned; new save="+content.SaveSlot+"; start="+content.StartFeet+"; facing cave wall="+wall+"; satchel="+satchel.Position+"; outdoor terrain preserved.";
 File.WriteAllText(Output+"/ui_build.txt",result);return result;
 }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
 }
 }
}
