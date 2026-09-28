using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string Output263=Output+"/Sinmok263";
  static string Folder263=>Path.GetDirectoryName(FrontageScene249().path).Replace('\\','/')+"/Sinmok263";
  static T Asset263<T>(string name,Func<T> create)where T:Object
  {Directory.CreateDirectory(Folder263);string p=Folder263+"/"+name+".asset";var a=AssetDatabase.LoadAssetAtPath<T>(p);if(a==null){a=create();AssetDatabase.CreateAsset(a,p);}return a;}
  public static string Sinmok263(string command)
  {
   Directory.CreateDirectory(Output263);
   switch(command){case "environment":return Environment263();case "map":return ImportMap263();case "world":return BuildWorld263();case "checks":return Checks263();case "combat-checks":return CombatChecks263();case "capture":return Capture263();default:throw new ArgumentException(command);}
  }
  static string Environment263()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Clean candidate required");var roots=scene.GetRootGameObjects();var s=VillageSession();
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var arrival=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Single();var cat=arrival.Catalog;var map=ui.MapData;
   foreach(var e in cat.Entries.Where(e=>e.Priority>0))e.RealmId=cat.RealmAt(e.Centre)?.Id;
   foreach(var marker in map.Markers){var e=cat.Entries.FirstOrDefault(e=>e.MarkerId==marker.Id);if(e!=null)marker.Label=cat.DisplayName(e);}
   map.Locations=cat;foreach(var z in map.Zones){var e=cat.Entries.FirstOrDefault(e=>e.Id==z.Id);if(e!=null)z.Label=cat.DisplayName(e);}
   var sky=Asset263("Sky",()=>new Material(Shader.Find("Oheangbu/Ink Cloud Sky")));
   var basis=AssetDatabase.LoadAssetAtPath<Material>(Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Sky.mat");
   if(basis!=null&&basis.HasProperty("_Horizon"))sky.CopyPropertiesFromMaterial(basis);
   sky.SetColor("_Cloud",new Color(.91f,.92f,.89f));sky.SetFloat("_CloudDensity",.78f);sky.SetFloat("_CloudScale",3.4f);sky.SetFloat("_CloudSpeed",.003f);EditorUtility.SetDirty(sky);
   var atm=arrival.GetComponent<WorldRealmAtmosphere>();if(atm==null)atm=arrival.gameObject.AddComponent<WorldRealmAtmosphere>();atm.Catalog=cat;atm.View=s.Walker.ViewCamera;atm.SkySource=sky;
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Atmosphere238/Renderer.asset");
   if(renderer==null)renderer=AssetDatabase.FindAssets("t:UniversalRendererData",new[]{Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Atmosphere238"}).Select(g=>AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(g))).Single();
   var fog=Asset263("MountainMist",()=>new Material(Shader.Find("Oheangbu/Compact/MountainMist263")));fog.SetFloat("_MistClock",-1);EditorUtility.SetDirty(fog);
   var pass=renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().Single(f=>f.name=="CompactMist238");pass.passMaterial=fog;renderer.SetDirty();EditorUtility.SetDirty(pass);EditorUtility.SetDirty(renderer);
   // Export original map images once. All revisions tint the same source, never tint a tint.
   var exports=new List<MapImage263>();Directory.CreateDirectory(Output263+"/MapSources");
   void Export(string id,Texture2D texture,Rect rect){if(texture==null)return;string file=Output263+"/MapSources/"+id+".png";if(!File.Exists(file)){var rt=RenderTexture.GetTemporary(texture.width,texture.height,0);var prior=RenderTexture.active;Graphics.Blit(texture,rt);RenderTexture.active=rt;var im=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);im.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);im.Apply();File.WriteAllBytes(file,im.EncodeToPNG());Object.DestroyImmediate(im);RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);}exports.Add(new MapImage263{id=id,rect=rect});}
   Export("base",map.BaseMap,new Rect(0,0,1,1));Export("macro",map.IllustratedMap,new Rect(0,0,1,1));Export("explored",map.ExploredMap,new Rect(0,0,1,1));
   foreach(var tile in map.RegionTiles)Export("tile_"+tile.Id,tile.Texture,tile.WorldUv);
   File.WriteAllText(Output263+"/map-sources.json",JsonUtility.ToJson(new MapExport263{images=exports.ToArray()},true));File.WriteAllText(Output263+"/catalog.json",JsonUtility.ToJson(cat,true));
   foreach(var o in new Object[]{cat,map,atm})EditorUtility.SetDirty(o);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Combined realm/site names, camera-local sky, clouds and depth-aware moving mountain mist applied; map image sources exported.";
  }
  [Serializable]class MapImage263{public string id;public Rect rect;}
  [Serializable]class MapExport263{public MapImage263[] images;}
  static string ImportMap263()
  {
   var scene=FrontageScene249();var map=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   Texture2D Read(string id){string p=Folder263+"/Map_"+id+".png";if(!File.Exists(p))throw new Exception("Missing tinted map "+p);AssetDatabase.ImportAsset(p);var importer=(TextureImporter)AssetImporter.GetAtPath(p);importer.maxTextureSize=8192;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(p);}
   map.BaseMap=Read("base");map.IllustratedMap=Read("macro");map.ExploredMap=Read("explored");foreach(var tile in map.RegionTiles)tile.Texture=Read("tile_"+tile.Id);
   EditorUtility.SetDirty(map);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));return "Private tinted terrain/detail map images connected; geography and discovery unchanged.";
  }
 }
}
