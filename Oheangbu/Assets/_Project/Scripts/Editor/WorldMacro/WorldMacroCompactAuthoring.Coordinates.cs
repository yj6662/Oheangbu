using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.EditorTools.WorldCompact;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        static Vector2 Map2(Vector2 v){var p=Compression.Map(new Vector3(v.x,0,v.y));return new Vector2(p.x,p.z);}
        static Vector3[] MapPolyline(Vector3[] source,float spacing=16)
        {
            if(source==null||source.Length==0)return Array.Empty<Vector3>();var result=new List<Vector3>();
            for(int i=1;i<source.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(new Vector2(source[i-1].x,source[i-1].z),new Vector2(source[i].x,source[i].z))/spacing));for(int j=0;j<n;j++)result.Add(Compression.Map(Vector3.Lerp(source[i-1],source[i],j/(float)n)));}result.Add(Compression.Map(source.Last()));return result.ToArray();
        }
        static Vector2[] MapPolygon(Vector2[] source)
        {if(source.Length==0)return source;return MapPolyline(source.Concat(new[]{source[0]}).Select(v=>new Vector3(v.x,0,v.y)).ToArray()).SkipLast(1).Select(v=>new Vector2(v.x,v.z)).ToArray();}
        static string Coordinates()
        {
            RequireCompact();var p=ReadProgress();if(p.phase!="GEOMETRY_COMPLETE")throw new InvalidOperationException("Finish geometry before coordinate remap.");
            var coverage=new List<WorldCompactCoordinateRemapper.CoordinateCoverage>();
            var map=Compression;
            var assetList=p.assets.ToList();
            foreach(var source in new[]{WorldMacroBuilder.Folder+"/Landmarks/Landmarks.asset",WorldMacroBuilder.Folder+"/Playtest/VisualCorridor/VisualCorridor.asset"})
            {
                if(assetList.Any(a=>a.source==source)||AssetDatabase.LoadMainAssetAtPath(source)==null)continue;
                string target=Folder+"/Data/"+assetList.Count.ToString("D2")+"_"+Path.GetFileName(source);
                if(AssetDatabase.LoadMainAssetAtPath(target)==null)AssetDatabase.CreateAsset(Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(source)),target);
                assetList.Add(new AssetPair{source=source,target=target,hash=Hash(source)});
            }
            p.assets=assetList.ToArray();
            foreach(var pair in p.assets)
            {
                var obj=AssetDatabase.LoadMainAssetAtPath(pair.target);
                coverage.AddRange(WorldCompactCoordinateRemapper.InspectCoordinateCoverage(obj,true));
                p.changedFields.AddRange(WorldCompactCoordinateRemapper.RemapCoordinates(obj,map.Map,Map2).Select(f=>pair.target+":"+f));
                if(obj is WorldMacroSheetSO geo)
                {
                    geo.BoundsMin=Map2(geo.BoundsMin);geo.BoundsMax=Map2(geo.BoundsMax);geo.Outline=MapPolygon(geo.Outline);
                    foreach(var r in geo.Ridges)r.Points=MapPolyline(r.Points);
                    foreach(var r in geo.Rivers)r.Points=MapPolyline(r.Points);
                    foreach(var r in geo.Routes)r.Points=MapPolyline(r.Points);
                    foreach(var b in geo.Basins){var min=Map2(b.Center-b.Radius);var max=Map2(b.Center+b.Radius);b.Center=Map2(b.Center);b.Radius=(max-min)*.5f;}
                    foreach(var s in geo.Sites)s.Position=map.Map(s.Position);
                    foreach(var r in geo.Regions)r.Polygon=MapPolygon(r.Polygon);
                    geo.CompressionSource=AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(Folder+"/SourceGeographySnapshot.asset");geo.Compression=map;
                }
                if(obj is WorldMacroPlaytestSO content){content.SaveSlot="world-demo-compact-v1";content.TerrainRevision="compact-v1";}
                if(obj is WorldMacroDressingSheetSO dress){dress.Cells=Array.Empty<WorldMacroDressingSheetSO.Cell>();dress.CompletedRegions=Array.Empty<string>();dress.SourceFingerprint="COMPACT_REBAKE_PENDING";}
                if(obj is WorldMapBakedDataSO data)data.Revision="compact-v1";
                EditorUtility.SetDirty(obj);
            }
            var components=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c!=null).ToArray();
            foreach(var c in components)
            {
                coverage.AddRange(WorldCompactCoordinateRemapper.InspectCoordinateCoverage(c,true));
                p.changedFields.AddRange(WorldCompactCoordinateRemapper.RemapCoordinates(c,map.Map,Map2).Select(f=>Hierarchy(c.transform)+":"+f));
                if(c is PlaytestUiRoot ui)
                {var so=new SerializedObject(ui);var play=so.FindProperty("PlaySceneName");if(play!=null)play.stringValue="W_Demo_Compact";var title=so.FindProperty("TitleSceneName");if(title!=null)title.stringValue="W_Demo_Compact_Title";so.ApplyModifiedPropertiesWithoutUndo();}
                EditorUtility.SetDirty(c);
            }
            File.WriteAllText(Path.Combine(Output,"coordinate_coverage.json"),JsonUtility.ToJson(new Coverage{fields=coverage.ToArray()},true));
            p.phase="COORDINATES_COMPLETE";AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return WriteProgress(p);
        }
        [Serializable] sealed class Coverage {public WorldCompactCoordinateRemapper.CoordinateCoverage[] fields;}
        [Serializable] sealed class Binding {public string componentId,field,target;}
        [Serializable] sealed class Bindings {public Binding[] bindings;}
        static string RepairSceneReferences()
        {
            RequireCompact();var progress=ReadProgress();if(Hash(SourceScene)!=progress.sourceHash)throw new InvalidOperationException("Source scene changed; do not use stale binding manifest.");
            var source=JsonUtility.FromJson<Bindings>(File.ReadAllText(Path.Combine(Output,"scene_reference_bindings.json")));
            var comps=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c!=null).ToDictionary(c=>GlobalObjectId.GetGlobalObjectIdSlow(c).targetObjectId.ToString(),c=>c);
            foreach(var b in source.bindings)
            {
                if(!comps.TryGetValue(b.componentId,out var component))throw new InvalidOperationException("Missing original component ID "+b.componentId);
                var asset=AssetDatabase.LoadMainAssetAtPath(b.target);if(asset==null||!EditorUtility.IsPersistent(asset))throw new InvalidOperationException("Missing persisted compact asset "+b.target);
                var so=new SerializedObject(component);var field=so.FindProperty(b.field);if(field==null||field.propertyType!=SerializedPropertyType.ObjectReference)throw new InvalidOperationException("Binding schema changed "+b.field);
                field.objectReferenceValue=asset;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(component);
            }
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "Restored "+source.bindings.Length+" exact source component bindings to persisted compact assets; original scene unchanged.";
        }
    }
}
