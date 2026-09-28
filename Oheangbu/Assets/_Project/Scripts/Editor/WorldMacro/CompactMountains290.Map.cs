using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Map290(bool apply)
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Candidate only");
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var ui=Object.FindFirstObjectByType<PlaytestUiRoot>();var art=Object.FindFirstObjectByType<CompactRebuildArtRenderer>();
   string output=O290+"/Cartography";Directory.CreateDirectory(output);
   if(!apply)
   {
    var colliders=GameObject.Find("Compact_Mountains_290").GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Terrain_")).ToArray();
    var data=colliders.ToDictionary(c=>c, c=>AssetDatabase.LoadAssetAtPath<TerrainData>(A290+"/"+c.name.Substring(8)+"/Height.asset"));
    var detail=GameObject.Find("Cheongrim_AssetPass_290")?.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).ToArray()??Array.Empty<Collider>();
    var ground=FinalSurface(scene);int fallback=0;
    using(var writer=new BinaryWriter(File.Create(output+"/heights.f32")))for(int z=0;z<2400;z++)for(int x=0;x<1600;x++)
    {
     float wx=x*2.5f,wz=z*2.5f;var mountain=colliders.FirstOrDefault(c=>wx>=c.bounds.min.x&&wx<=c.bounds.max.x&&wz>=c.bounds.min.z&&wz<=c.bounds.max.z);
     float y;if(mountain==null)y=ground(wx,wz).point.y;
     else if(mountain.Raycast(new Ray(new Vector3(wx,1800,wz),Vector3.down),out var hit,2200))y=hit.point.y;
     else{var b=mountain.bounds;y=data[mountain].GetInterpolatedHeight((wx-b.min.x)/b.size.x,(wz-b.min.z)/b.size.z)-200;fallback++;}
     foreach(var c in detail){var b=c.bounds;if(wx<b.min.x||wx>b.max.x||wz<b.min.z||wz>b.max.z)continue;if(c.Raycast(new Ray(new Vector3(wx,1800,wz),Vector3.down),out var cap,2200))y=Mathf.Max(y,cap.point.y);}
     if(!float.IsFinite(y))throw new Exception("Invalid map elevation");writer.Write(y);
    }
    File.WriteAllText(output+"/heights.json","{\"width\":1600,\"height\":2400,\"cellMetres\":2.5,\"source\":\"current candidate collider surface; authored Terrain height only in lift cutouts\",\"cutoutSamples\":"+fallback+"}");
    File.WriteAllText(output+"/placements.json",JsonUtility.ToJson(art.Sheet,true));File.WriteAllText(output+"/catalog.json",JsonUtility.ToJson(Object.FindFirstObjectByType<WorldLocationArrival>().Catalog,true));
    File.WriteAllText(O290+"/layout.json",JsonUtility.ToJson(s.MountainLayout,true));return "Exported candidate physical terrain for isolated cartography; shaft fallbacks="+fallback;
   }
   Texture2D Texture(string name)
   {
    string path=A290+"/Maps/"+name+".png";DevSceneKit.EnsureFolder(A290+"/Maps");File.Copy(output+"/"+name+".png",path,true);AssetDatabase.ImportAsset(path);
    var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=8192;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   }
   var map=ui.MapData;if(!AssetDatabase.GetAssetPath(map).StartsWith(A290+"/"))throw new Exception("Refuse to edit shared map");
   map.IllustratedMap=Texture("terrain");map.ExploredMap=Texture("explored");
   map.RegionTiles=new[]{new WorldMapRegionTile{Id="mountains290-cheongrim",Texture=Texture("region"),WorldUv=new Rect(.625f,.2f,.375f,2600f/6000)}};
   map.Lines=map.Lines.Where(v=>!v.Id.StartsWith("mountain_")).Concat(s.MountainLayout.Mountains.SelectMany(m=>new[]{
    new WorldMapLineSpec{Id=m.Id+"_main",Kind=WorldMapLineKind.Trail,Points=m.MainPath.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1.4f},
    new WorldMapLineSpec{Id=m.Id+"_hidden",Kind=WorldMapLineKind.Trail,Points=m.TemplePath.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1.1f},
    new WorldMapLineSpec{Id=m.Id+"_return",Kind=WorldMapLineKind.Trail,Points=m.ReturnPath.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1.1f}})).ToArray();
   map.PaintedRelief=true;map.Revision="mountains-290-v1";EditorUtility.SetDirty(map);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Installed private candidate atlas/contours/road geometry; existing discovery and cave drawings retained";
  }
 }
}
