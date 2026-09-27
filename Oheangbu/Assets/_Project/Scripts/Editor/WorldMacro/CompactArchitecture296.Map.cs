using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void MapArchitecture296(List<string> report)
  {
   var ui=Components295<PlaytestUiRoot>().First(u=>u.gameObject.activeInHierarchy);
   var map=ui.MapData;var arrivals=Components295<WorldLocationArrival>().Where(a=>a.gameObject.activeInHierarchy).ToArray();
   var catalogs=arrivals.Select(a=>a.Catalog).Concat(new[]{map.Locations}).Where(c=>c!=null).Distinct().ToArray();
   if(!AssetDatabase.GetAssetPath(map).StartsWith(A296+"/",StringComparison.Ordinal)||catalogs.Any(c=>!AssetDatabase.GetAssetPath(c).StartsWith(A296+"/",StringComparison.Ordinal)))throw new Exception("Candidate map and arrival catalogs must be private296 assets");
   var sheet=Sheet296();var lines=map.Lines.Where(l=>!l.Id.StartsWith("architecture296_",StringComparison.Ordinal)).ToList();var markers=map.Markers.ToList();
   foreach(var a in sheet.Arenas)
   {
    string markerId=a.PlaceId;
    var marker=markers.FirstOrDefault(m=>m.Id==markerId);
    if(marker==null){marker=new WorldMapMarkerSpec{Id=markerId,Kind=WorldMapMarkerKind.Place,RequiresArrival=true,InitiallyDiscovered=false};markers.Add(marker);}
    marker.Label=a.Label;marker.WorldXZ=new Vector2(a.Centre.x,a.Centre.z);
    // A reserved venue is discoverable geography, with no completion fact or reward.
    if(a.TestOnly)marker.CompletionId="";
    var q=Quaternion.Euler(0,a.Yaw,0);var corners=new[]{new Vector3(-a.ClearSize.x*.5f,0,-a.ClearSize.y*.5f),new Vector3(a.ClearSize.x*.5f,0,-a.ClearSize.y*.5f),new Vector3(a.ClearSize.x*.5f,0,a.ClearSize.y*.5f),new Vector3(-a.ClearSize.x*.5f,0,a.ClearSize.y*.5f)}.Select(p=>a.Centre+q*p).ToArray();
    var polygon=corners.Select(p=>new Vector2(p.x,p.z)).ToArray();
    lines.Add(new WorldMapLineSpec{Id="architecture296_"+a.Id+"_outline",Kind=WorldMapLineKind.DetailOutline,Points=polygon.Concat(new[]{polygon[0]}).ToArray(),PixelWidth=1.1f});
    lines.Add(new WorldMapLineSpec{Id="architecture296_"+a.Id+"_approach",Kind=WorldMapLineKind.Trail,Points=a.Approach.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1.25f});
    foreach(var catalog in catalogs)
    {
     var entries=catalog.Entries.ToList();var entry=entries.FirstOrDefault(e=>e.Id==a.PlaceId);
     if(entry==null){entry=new WorldLocationCatalog.Entry{Id=a.PlaceId,Priority=30};entries.Add(entry);}
     entry.Name=a.Label;entry.MarkerId=markerId;entry.RealmId="realm_"+a.Realm;entry.Centre=a.Centre;
     if(a.TestOnly){entry.Polygon=polygon;entry.MinimumY=a.Centre.y-1;entry.MaximumY=a.Centre.y+Mathf.Max(3,a.ClearHeight);entry.FloorPath=Array.Empty<Vector3>();entry.Radius=Mathf.Max(a.ClearSize.x,a.ClearSize.y)*.5f;}
     catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);
    }
   }
   if(File.Exists(G296+"/routes.json"))
   {
    var routeData=JsonUtility.FromJson<Routes292>(File.ReadAllText(G296+"/routes.json"));
    foreach(var c in JsonUtility.FromJson<Crossings295>(File.ReadAllText(G296+"/crossings.json")).Crossings)
    {
     var route=routeData.routes.Single(r=>r.id==c.RouteId);var existing=lines.FirstOrDefault(l=>l.Id==c.RouteId||l.Id=="road_"+c.RouteId);
     if(existing==null){existing=new WorldMapLineSpec{Id="architecture296_bridge_"+c.RouteId,Kind=WorldMapLineKind.Road,PixelWidth=1.4f};lines.Add(existing);}
     existing.Points=route.points.Select(p=>new Vector2(p.x,p.z)).ToArray();
    }
   }
   map.Lines=lines.ToArray();map.Markers=markers.ToArray();map.Revision="architecture-296";EditorUtility.SetDirty(map);
   File.WriteAllText(O296+"/map.json",JsonUtility.ToJson(map,true));foreach(var catalog in catalogs)File.WriteAllText(O296+"/arrival-"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog))+".json",JsonUtility.ToJson(catalog,true));
   report.Add("Private map: 5 venue footprints/approaches; bridge paths match physical crossings. Arrival catalogs="+catalogs.Length+"; reserved markers have no completion fact. Existing checkpoints, discovery format and terrain illustration retained.");
  }
 }
}
