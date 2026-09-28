using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 static string FocusMapBuild(bool apply){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
 string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');Directory.CreateDirectory(Output+"/Cartography");
 try{
 var roots=scene.GetRootGameObjects();var all=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
 var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
 var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
 var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
 var skin=AssetDatabase.LoadAssetAtPath<WorldMacroHudSkinProfileSO>(folder+"/HUDSkin.asset");
 var text=new List<string>();var bindings=new List<WorldMacroPlaytestSession.InteractionVisual>();
 foreach(var point in session.Content.Points){
 string name=point.Id=="geumpyo_inn"?"Inn_Rest_Door":point.Id=="worker_satchel"?"Worker_Straw_Bag":point.Id=="mine_tool_marks"?"Broken_Work_Crate":point.Id=="mine_inquiry"?"Mine_Investigation_Clue":null;
 var target=all.FirstOrDefault(t=>t.name==(name??point.Id));
 var renderers=target!=null?target.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&r.bounds.size.magnitude<12).ToArray():Array.Empty<Renderer>();
 if(renderers.Length==0){var preview=session.PreviewPoints.FirstOrDefault(p=>p!=null&&p.Id==point.Id);
 if(preview!=null)renderers=preview.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&r.bounds.size.magnitude<8).ToArray();}
 if(renderers.Length==0){var nearby=all.Select(t=>t.GetComponent<MeshRenderer>()).Where(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy&&r.bounds.size.magnitude<3&&Vector3.Distance(r.bounds.center,point.Position)<1.5f).OrderBy(r=>Vector3.Distance(r.bounds.center,point.Position)).FirstOrDefault();
 if(nearby!=null)renderers=new Renderer[]{nearby};}
 renderers=renderers.Where(r=>r.bounds.size.sqrMagnitude>.01f).ToArray();
 bindings.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=point.Id,Renderers=renderers});
 text.Add(point.Id+" "+point.Position+" => "+string.Join(";",renderers.Select(r=>r.name+" "+r.bounds)));
 }
 File.WriteAllText(Output+"/Cartography/placements.json",JsonUtility.ToJson(manifest.Art,true));
 File.WriteAllText(Output+"/Cartography/focus_bindings.txt",string.Join("\n",text));
 if(!apply){
 File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(ui.MapData,true));
 ExportFinalHeights(scene);
 return "Exported terrain/map ledger and focus candidates: "+string.Join("\n",text);
 }
 // A combined static mesh includes neighboring buildings and loses the target-local geometry.
 foreach(var binding in bindings)foreach(var r in binding.Renderers)GameObjectUtility.SetStaticEditorFlags(r.gameObject,GameObjectUtility.GetStaticEditorFlags(r.gameObject)&~StaticEditorFlags.BatchingStatic);
 session.InteractionVisuals=bindings.ToArray();skin.InteractionLetterOffset=.10f;skin.InteractionLetterHeight=.22f;skin.InteractionOutlinePixels=2.4f;skin.InteractionOutlineColor=new Color(.90f,.79f,.48f,1);
 string image=folder+"/Cartography.png";File.Copy(Output+"/Cartography/terrain.png",image,true);AssetDatabase.ImportAsset(image);
 var importer=(TextureImporter)AssetImporter.GetAtPath(image);importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.sRGBTexture=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
 ui.MapData.IllustratedMap=AssetDatabase.LoadAssetAtPath<Texture2D>(image);ui.MapData.RegionTiles=Array.Empty<WorldMapRegionTile>();ui.MapData.Revision="compact-cave-v4-cartography";
 string tilePath=folder+"/PaintedTerrain_Cheongrim.png";File.Copy(Output+"/Cartography/region.png",tilePath,true);AssetDatabase.ImportAsset(tilePath);
 var ti=(TextureImporter)AssetImporter.GetAtPath(tilePath);ti.npotScale=TextureImporterNPOTScale.None;ti.maxTextureSize=4096;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.mipmapEnabled=true;ti.sRGBTexture=true;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
 ui.MapData.RegionTiles=new[]{new WorldMapRegionTile{Id="cheongrim-worked",Texture=AssetDatabase.LoadAssetAtPath<Texture2D>(tilePath),WorldUv=new Rect(.625f,.2f,.375f,1900f/6000)}};
 ui.MapData.PaintedRelief=true;
 EditorUtility.SetDirty(skin);EditorUtility.SetDirty(ui.MapData);EditorUtility.SetDirty(session);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
 return "Saved private focus bindings, bounds prompt and illustrated cartography; "+bindings.Count+" targets";
 }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
 }
 }
}
