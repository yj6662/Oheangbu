using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Vector3 ApproachSide296(List<Vector3> points,int node)
  {
   var before=node>0?points[node]-points[node-1]:points[1]-points[0];var after=node<points.Count-1?points[node+1]-points[node]:before;before.y=0;after.y=0;
   if(before.sqrMagnitude<.000001f)before=after;if(after.sqrMagnitude<.000001f)after=before;before.Normalize();after.Normalize();
   var a=new Vector3(before.z,0,-before.x);var b=new Vector3(after.z,0,-after.x);var miter=(a+b).normalized;if(miter.sqrMagnitude<.1f)miter=b;
   float half=Mathf.Lerp(1.4f,4,Mathf.Clamp01(node/18f))*.5f;return miter*(half/Mathf.Max(.5f,Vector3.Dot(miter,b)));
  }
  static void AddJoinedApproachTread296(VenueBatch296 batch,List<Vector3> points,int end,string realm,List<Mesh> temporary)
  {
   // Adjacent source slabs share the exact same cross section. Rotated rectangles used to
   // cover two or three lower treads on the inside of a bend, exceeding the player's0.3m step.
   var source=ModuleVenue296(Jeju296+"Floors/Floor_stone_2.prefab");var a=points[end-1];var b=points[end];var sa=ApproachSide296(points,end-1);var sb=ApproachSide296(points,end);float top=Mathf.Max(a.y,b.y);
   foreach(var part in source.Parts)
   {
    var input=part.Meshes[0];var vertices=input.vertices;
    for(int i=0;i<vertices.Length;i++)
    {
     var old=vertices[i];float u=Mathf.InverseLerp(source.Bounds.min.x,source.Bounds.max.x,old.x),t=Mathf.InverseLerp(source.Bounds.min.z,source.Bounds.max.z,old.z),height=Mathf.InverseLerp(source.Bounds.min.y,source.Bounds.max.y,old.y);
     var left=Vector3.Lerp(a-sa,b-sb,t);var right=Vector3.Lerp(a+sa,b+sb,t);vertices[i]=Vector3.Lerp(left,right,u);vertices[i].y=top-.20f+height*.20f;
    }
    var mesh=new Mesh{name="296_shared_join_source_stone",indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.uv=input.uv;mesh.triangles=input.triangles;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();temporary.Add(mesh);
    batch.AddRaw(mesh,RegionalStoneVenue296(part.Material,realm),true);
   }
   batch.Sources.Add(source.Path);
  }
  public static string RefreshApproachTreads296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Approach tread refresh requires the saved296 Edit candidate.");
   var sheet=Sheet296();var layout=Session292().MountainLayout;var field=new CompactWorldSurface(layout);var placements=JsonUtility.FromJson<VenuePlacementLedger296>(File.ReadAllText(O296+"/venue-placements.json"));var report=new List<string>();
   foreach(var arena in sheet.Arenas.Where(a=>a.Realm!="cheongrim"))
   {
    var root=Root296(arena.SceneRoot).transform;var old=Root296("Architecture296_Venues/Approach_"+arena.Id);if(old==null)throw new Exception("Missing existing approach "+arena.Id);Object.DestroyImmediate(old);
    var plan=new VenuePlan296{Id=arena.Id,Realm=arena.Realm,Place=arena.PlaceId,Clear=arena.ClearSize,Yaw=arena.Yaw,Centre=arena.Centre,Height=arena.ClearHeight,Interior=arena.Interior};var prior=arena.Approach;
    arena.Approach=ApproachVenue296(plan,root,field,arena.Entrance,report);if(Vector3.Distance(prior[0],arena.Approach[0])>.001f||Vector3.Distance(prior[prior.Length-1],arena.Approach[arena.Approach.Length-1])>.001f)throw new Exception("Tread repair unexpectedly moved an endpoint: "+arena.Id);
    placements.Venues.First(v=>v.Id==arena.Id).Approach=arena.Approach;UpdateVenuePlace296(plan,arena,layout);report.Add(arena.Id+" original endpoints and max grade0.4 retained; bends smoothed before sampling; each visible source-stone tread shares miter boundaries across full width; render and collider use the same mesh.");
   }
   EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(layout);File.WriteAllText(O296+"/venue-placements.json",JsonUtility.ToJson(placements,true));File.WriteAllText(O296+"/architecture.json",JsonUtility.ToJson(sheet,true));Physics.SyncTransforms();Save292();File.WriteAllLines(O296+"/approach-treads-refresh.txt",report);return string.Join("\n",report);
  }
 }
}
