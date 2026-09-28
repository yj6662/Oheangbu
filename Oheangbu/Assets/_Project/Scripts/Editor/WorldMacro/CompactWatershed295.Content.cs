using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class Crossing295
  {public string Id,RouteId,Kind;public Vector3 Start,End;public float DeckHeight,RouteWidth,Span,WaterHeight;public Vector3[] Points;}
  [Serializable] sealed class Crossings295{public Crossing295[] Crossings=Array.Empty<Crossing295>();}
  [Serializable] sealed class Pose295{public string Path;public Vector3 Position;}
  [Serializable] sealed class Poses295{public Pose295[] Items;}
  static Transform Find295(string path)
  {
   var split=path.Split('/');var root=Root295(split[0]);if(root==null)return null;
   var t=root.transform;for(int i=1;i<split.Length&&t!=null;i++)t=t.Find(split[i]);return t;
  }
  static void Content295(CompactWorldSurface oldField,CompactWorldSurface field,List<string> report)
  {
   var session=Session292();var layout=session.MountainLayout;var content=session.Content;var roots=session.gameObject.scene.GetRootGameObjects();
   var beforeLayout=ScriptableObject.CreateInstance<CompactWorldLayoutSO>();JsonUtility.FromJsonOverwrite(File.ReadAllText(O295+"/layout-before.json"),beforeLayout);
   var beforeContent=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();JsonUtility.FromJsonOverwrite(File.ReadAllText(O295+"/content-before.json"),beforeContent);
   var query=roots.SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true)).First();
   Vector3 Map(Vector3 p,bool dry=false)
   {
    var xz=new Vector2(p.x,p.z);var place=beforeLayout.Places.Where(q=>!q.Id.StartsWith("mountain_")).OrderBy(q=>(q.XZ-xz).sqrMagnitude).FirstOrDefault();
    Vector2 next=xz;
    if(place!=null&&Vector2.Distance(place.XZ,xz)<Mathf.Max(38,place.GroundRadius+12))
    {var after=layout.Places.FirstOrDefault(q=>q.Id==place.Id);if(after!=null)next+=after.XZ-place.XZ;}
    float offset=p.y-oldField.Sample(p.x,p.z);var result=new Vector3(next.x,field.Sample(next.x,next.y)+offset,next.y);
    if(!dry||!query.TryWaterHeight(result,out float water)||water<=result.y+.05f)return result;
    for(float radius=8;radius<=640;radius+=8)for(int k=0;k<32;k++)
    {
     float a=k*Mathf.PI/16;var at=next+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
     if(at.x<8||at.x>3992||at.y<8||at.y>5992||field.Normal(at.x,at.y).y<.82f)continue;
     var candidate=field.Point(at);if(query.TryWaterHeight(candidate,out float y)&&candidate.y<y+.25f)continue;
     return candidate+Vector3.up*Mathf.Max(0,offset);
    }
    throw new Exception("No dry placement found for content at "+p);
   }
   // Baseline world transforms make repeated builds independent of previous relocation.
   var posePath=O295+"/assembly-before.json";Poses295 poses;
   if(File.Exists(posePath))poses=JsonUtility.FromJson<Poses295>(File.ReadAllText(posePath));
   else
   {
    var items=new List<Pose295>();
    foreach(string line in File.ReadAllLines(O292+"/relocated-roots.txt"))
    {string path=line.Split('|')[0].Trim();var t=Find295(path);if(t!=null)items.Add(new Pose295{Path=path,Position=t.position});}
    poses=new Poses295{Items=items.ToArray()};File.WriteAllText(posePath,JsonUtility.ToJson(poses,true));
   }
   int moved=0;
   foreach(var pose in poses.Items)
   {
    var t=Find295(pose.Path);if(t==null)continue;
    bool interactive=t.GetComponentsInChildren<WorldMacroContentPoint>(true).Length>0;
    var position=Map(pose.Position,interactive);if(Vector3.Distance(position,pose.Position)>.01f)moved++;
    t.position=position;
   }
   foreach(var p in content.Points){var old=beforeContent.Points.FirstOrDefault(q=>q.Id==p.Id);if(old!=null)p.Position=Map(old.Position,true);}
   foreach(var e in content.Encounters)
   {
    var old=beforeContent.Encounters.FirstOrDefault(q=>q.Id==e.Id);if(old==null)continue;e.Feet=Map(old.Feet,true);
    if(old.Patrol!=null)e.Patrol=old.Patrol.Select(p=>Map(p,true)).ToArray();
   }
   foreach(var c in content.Checkpoints){var old=beforeContent.Checkpoints.FirstOrDefault(q=>q.Id==c.Id);if(old!=null)c.Feet=Map(old.Feet,true);}
   content.StartFeet=Map(beforeContent.StartFeet,true);content.InnCheckpointFeet=Map(beforeContent.InnCheckpointFeet,true);
   content.MainPath=beforeContent.MainPath.Select(p=>Map(p)).ToArray();content.BranchPath=beforeContent.BranchPath.Select(p=>Map(p)).ToArray();
   content.SaveSlot="world-compact-watershed-295";content.TerrainRevision="watershed-295";EditorUtility.SetDirty(content);
   foreach(var marker in roots.SelectMany(r=>r.GetComponentsInChildren<WorldMacroContentPoint>(true)))
   {
    var p=content.Points.FirstOrDefault(q=>q.Id==marker.Id);if(p==null)continue;
    // Move the authored interaction assembly with its target; keep NPC/building and prompt together.
    var t=marker.transform;var delta=p.Position-t.position;
    if(delta.sqrMagnitude>.0001f)t.position+=delta;
   }
   foreach(var actor in session.Actors)
   {
    if(actor==null)continue;var encounter=content.Encounters.FirstOrDefault(e=>e.Id==actor.Id);if(encounter==null)continue;
    var agent=actor.GetComponent<UnityEngine.AI.NavMeshAgent>();var offset=Vector3.up*(agent!=null?agent.baseOffset:0);
    actor.transform.position=encounter.Feet+offset;actor.PatrolPoints=encounter.Patrol.Select(p=>p+offset).ToArray();EditorUtility.SetDirty(actor);
   }
   foreach(var catalog in roots.SelectMany(r=>r.GetComponentsInChildren<WorldLocationArrival>(true)).Select(a=>a.Catalog).Where(a=>a!=null).Distinct())
   {
    string filename=Path.GetFileName(AssetDatabase.GetAssetPath(catalog));
    var original=filename.Length>32?AssetDatabase.LoadAssetAtPath<WorldLocationCatalog>(AssetDatabase.GUIDToAssetPath(filename.Substring(0,32))):null;
    if(original==null)throw new Exception("Immutable arrival catalog source missing");EditorUtility.CopySerialized(original,catalog);
    foreach(var entry in catalog.Entries.Where(e=>e.Priority>0))
    {
     var before=entry.Centre;entry.Centre=Map(before,true);var delta=entry.Centre-before;
     if(entry.MinimumY>-900)entry.MinimumY+=delta.y;if(entry.MaximumY<1900)entry.MaximumY+=delta.y;
     if(entry.Polygon!=null)entry.Polygon=entry.Polygon.Select(p=>p+new Vector2(delta.x,delta.z)).ToArray();
     if(entry.FloorPath!=null)entry.FloorPath=entry.FloorPath.Select(p=>Map(p)).ToArray();
    }
    EditorUtility.SetDirty(catalog);
   }
   var routes=JsonUtility.FromJson<Routes292>(File.ReadAllText(G295+"/routes.json"));
   foreach(var ui in roots.SelectMany(r=>r.GetComponentsInChildren<PlaytestUiRoot>(true)))
   {
    var map=ui.MapData;if(map==null)continue;map.IllustratedMap=Texture295("cartography.png",false);map.ExploredMap=map.IllustratedMap;map.RegionTiles=Array.Empty<WorldMapRegionTile>();map.Revision="watershed-295";
    map.Lines=routes.routes.Select(r=>new WorldMapLineSpec{Id=r.id,Kind=WorldMapLineKind.Trail,Points=r.points.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1}).ToArray();
    foreach(var marker in map.Markers){var place=layout.Places.FirstOrDefault(p=>p.Id==marker.Id);if(place!=null)marker.WorldXZ=place.XZ;}EditorUtility.SetDirty(map);
   }
   Bridges295(field,report);KaesongRuins295(field,layout.Hydrology,report);
   var profile=Asset295("Data/MumBridgeProfile.asset",()=>ScriptableObject.CreateInstance<MumBridgeProfileSO>());
   profile.MaximumBankEmbedApproach=6f;
   profile.DeckMaterial=AssetDatabase.LoadAssetAtPath<Material>(A295+"/Materials/BridgeStone.mat");
   var ink=Asset295("Materials/BridgePreviewInk.mat",()=>new Material(Shader.Find("Universal Render Pipeline/Unlit")));ink.SetColor("_BaseColor",new Color(.065f,.10f,.095f));EditorUtility.SetDirty(ink);profile.PreviewMaterial=ink;
   session.MumBridgeProfile=profile;EditorUtility.SetDirty(profile);EditorUtility.SetDirty(session);
   Object.DestroyImmediate(beforeLayout);Object.DestroyImmediate(beforeContent);report.Add("Baseline assemblies relocated="+moved+"; private interactions/checkpoints moved to dry support; illustrated map refreshed.");
  }
 }
}
