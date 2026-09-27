using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string ArrivalOutput262=Output+"/Arrival262";
  public static string Arrival262(string command)
  {
   var scene=FrontageScene249();var s=VillageSession();var roots=scene.GetRootGameObjects();
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   Directory.CreateDirectory(ArrivalOutput262);
   if(command=="checks")return CheckArrival262();
   if(command=="capture")return CaptureArrival262();
   if(command!="build")throw new ArgumentException(command);
   if(scene.isDirty)throw new Exception("Clean candidate required");
   string backup=ArrivalOutput262+"/Before/"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");Directory.CreateDirectory(backup);
   var paths=new[]{scene.path,AssetDatabase.GetAssetPath(ui.MapData)};
   for(int i=0;i<paths.Length;i++)foreach(var suffix in new[]{"",".meta"})if(File.Exists(paths[i]+suffix))File.Copy(paths[i]+suffix,backup+"/"+i+"_"+Path.GetFileName(paths[i])+suffix,true);
   File.WriteAllLines(backup+"/paths.txt",paths);
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Arrival262";Directory.CreateDirectory(folder);
   var catalog=AssetDatabase.LoadAssetAtPath<WorldLocationCatalog>(folder+"/Locations.asset");if(catalog==null){catalog=ScriptableObject.CreateInstance<WorldLocationCatalog>();AssetDatabase.CreateAsset(catalog,folder+"/Locations.asset");}
   var entries=new List<WorldLocationCatalog.Entry>();
   void Region(string id,string name,float x,float z,float width,float depth)=>entries.Add(new WorldLocationCatalog.Entry{Id=id,Name=name,Priority=0,Polygon=new[]{new Vector2(x,z),new Vector2(x+width,z),new Vector2(x+width,z+depth),new Vector2(x,z+depth)}});
   Region("realm_cheongrim","청림",2550,1550,1450,2550);Region("realm_hwanggyeong","황경",1450,1550,1100,2550);
   Region("realm_jeokro","적로",0,0,4000,1550);Region("realm_cheolong","철옹",0,1550,1450,2550);Region("realm_hyeongang","현강",0,4100,4000,1900);
   var map=ui.MapData;var markers=map.Markers.ToList();var ground=FinalSurface(scene);
   void AddMarker(WorldLocationCatalog.Entry entry,WorldMapMarkerKind kind)
   {
    var marker=markers.FirstOrDefault(m=>m.Id==entry.MarkerId);if(marker==null){marker=new WorldMapMarkerSpec{Id=entry.MarkerId};markers.Add(marker);}
    marker.Label=entry.Name;marker.WorldXZ=new Vector2(entry.Centre.x,entry.Centre.z);marker.Kind=kind;marker.RequiresArrival=true;marker.InitiallyDiscovered=false;
   }
   void Place(string id,string name,float radius,int priority=10,WorldMapMarkerKind kind=WorldMapMarkerKind.Place)
   {
    var place=manifest.Layout.Places.Single(p=>p.Id==id);var foot=ground(place.XZ.x,place.XZ.y).point;
    var entry=new WorldLocationCatalog.Entry{Id=id,Name=name,MarkerId=id,Centre=foot,Radius=radius,MinimumY=foot.y-22,MaximumY=foot.y+22,Priority=priority};entries.Add(entry);AddMarker(entry,kind);
   }
   Place("geumpyo_inn","금표 주막",38,10,WorldMapMarkerKind.Rest);Place("relay","길목 역참",42);
   Place("village","청림 벌목마을",92,10,WorldMapMarkerKind.Settlement);
   Place("logging","버려진 벌목장",65);Place("herb_path","약초꾼 길",32);Place("deep_forest","물든 심부",90);
   Place("sanctuary","청룡 성역",85);Place("high_cache","능선 위 흔적",12,20);
   Place("village_toolbox","버려진 공구터",14,20);Place("village_cut_trace","검게 물든 그루터기",16,20);
   Place("inspection_one","고개 검문",35);Place("inspection_two","하천 검문",35);
   Place("capital_delivery","성저 객주",42,10,WorldMapMarkerKind.Settlement);Place("south_gate","황경 남문",50,10,WorldMapMarkerKind.Gate);
   var mine=map.Zones.Single(z=>z.Id=="mine_interior");
   var cave=new WorldLocationCatalog.Entry{Id="mine_interior",Name="폐광",MarkerId="mine",Polygon=mine.Polygon,MinimumY=s.Content.StartFeet.y-1,MaximumY=s.Content.StartFeet.y+2.8f,Priority=40,Centre=s.Content.StartFeet};entries.Add(cave);
   var caveMarker=markers.Single(m=>m.Id=="mine");caveMarker.RequiresArrival=true;caveMarker.InitiallyDiscovered=false;
   // The mine is one arrival area. Clue interactions retain their independent durable facts.
   catalog.Entries=entries.ToArray();catalog.SettleSeconds=.65f;catalog.ExitMargin=5;catalog.RepeatSeconds=30;
   var presenter=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).SingleOrDefault();
   if(presenter==null)presenter=new GameObject("LocationArrivals262").AddComponent<WorldLocationArrival>();
   presenter.Catalog=catalog;presenter.Session=s;presenter.Theme=ui.Theme;map.Markers=markers.ToArray();
   foreach(var obj in new Object[]{catalog,presenter,map})EditorUtility.SetDirty(obj);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(ArrivalOutput262+"/catalog.json",JsonUtility.ToJson(catalog,true));File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));
   string report="Candidate arrival volumes="+entries.Count+" (5 realms; authored places; one whole-mine arrival). Two-line names only; no input gate, travel or rewards. Existing discoveries retained. New landmark markers require actual arrival.";
   File.WriteAllText(ArrivalOutput262+"/build.txt",report);return report;
  }
  static string CheckArrival262()
  {
   var lines=new List<string>();void C(bool ok,string title){lines.Add((ok?"PASS ":"FAIL ")+title);if(!ok){File.WriteAllLines(ArrivalOutput262+"/checks.txt",lines);throw new Exception(title);}}
   var scene=FrontageScene249();var presenters=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).ToArray();C(presenters.Length==1,"single candidate presenter");
   var presenter=presenters[0];var catalog=presenter.Catalog;
   C(catalog!=null&&presenter.Session==VillageSession()&&presenter.Theme.Font!=null,"live session/catalog/bundled font bindings");
   C(catalog.Entries.Select(e=>e.Id).Distinct().Count()==catalog.Entries.Length&&catalog.Entries.All(e=>!string.IsNullOrWhiteSpace(e.Name)),"unique IDs and display names");
   C(catalog.Entries.Count(e=>e.Priority==0)==5,"five compass realms");
   bool built263=scene.GetRootGameObjects().Any(g=>g.name==Root263);
   C(built263?catalog.Entries.Count(e=>e.Id=="root_cave"||e.Id=="old_tree")==2:!catalog.Entries.Any(e=>e.Id=="root_cave"||e.Id=="old_tree"),"root cave and Sinmok labels require their authored scene content");
   var inn=catalog.Entries.Single(e=>e.Id=="geumpyo_inn");var tracker=new WorldLocationTracker(catalog);
   tracker.Step(inn.Centre,0);C(tracker.Current==null,"brief crossing does not announce");tracker.Step(inn.Centre,.7f);C(tracker.Current==inn,"local place outranks realm");
   C(tracker.TakeAnnouncement(.7f,false)==null&&tracker.TakeAnnouncement(.8f,true)==inn,"suppressed notice deferred until allowed");C(tracker.TakeAnnouncement(1,true)==null,"continuous stay does not repeat");
   var outside=inn.Centre+Vector3.right*(inn.Radius+2);tracker.Step(outside,2);tracker.Step(outside,3);C(tracker.Current==inn,"edge jitter retained inside exit margin");
   outside=inn.Centre+Vector3.right*(inn.Radius+20);tracker.Step(outside,4);tracker.Step(outside,5);C(tracker.Current!=inn,"genuine exit releases local volume");
   tracker.Step(inn.Centre,6);tracker.Step(inn.Centre,7);C(tracker.TakeAnnouncement(7,true)==null,"quick recross suppressed");
   tracker.Step(outside,35);tracker.Step(outside,36);tracker.Step(inn.Centre,37);tracker.Step(inn.Centre,38);C(tracker.TakeAnnouncement(38,true)==inn,"later revisit displays again");
   var stale=new WorldLocationTracker(catalog);stale.Step(inn.Centre,0);stale.Step(inn.Centre,1);stale.TakeAnnouncement(1,false);stale.Step(outside,2);stale.Step(outside,3);C(stale.TakeAnnouncement(3,true)?.Id!="geumpyo_inn","old delayed location discarded after departure");
   var cave=catalog.Entries.Single(e=>e.Id=="mine_interior");C(cave.Contains(VillageSession().Content.StartFeet)&&!cave.Contains(VillageSession().Content.StartFeet+Vector3.up*4),"cave rejects roof/surface above its vertical bounds");
   C(WorldLocationTracker.Opacity(0)==0&&WorldLocationTracker.Opacity(1)==1&&WorldLocationTracker.Opacity(5)==0,"transient fade lifecycle");
   var state=WorldMacroProgress.CreateNew("arrival-fixture",Vector3.zero,0);state.ledger.currency=123;state.campaign.Facts.Add("old-evidence");state.ui.discoveredMarkers.Add("old-marker");
   C(WorldMacroPlaytestSession.PrepareLocationVisit(state,inn,out var proposed),"first arrival proposes durable visit");
   C(!state.campaign.Facts.Contains("location:"+inn.Id)&&proposed.campaign.Facts.Contains("location:"+inn.Id)&&proposed.ui.discoveredMarkers.Contains(inn.MarkerId),"fact/marker added together without mutating accepted state");
   C(proposed.ledger.currency==123&&proposed.campaign.Completed.Count==0&&proposed.campaign.Facts.Contains("old-evidence")&&proposed.ui.discoveredMarkers.Contains("old-marker"),"no reward or quest completion; prior records retained");
   C(!WorldMacroPlaytestSession.PrepareLocationVisit(proposed,inn,out _),"repeat arrival is idempotent");
   string dir=ArrivalOutput262+"/FixtureSaves/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(dir);string path=dir+"/visit.json";
   var store=new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid);store.Save(state);bool rejected=false;
   using(var locked=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){try{store.Save(proposed);}catch(IOException){rejected=true;}}
   C(rejected&&!JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(path)).campaign.Facts.Contains("location:"+inn.Id),"atomic file failure retains old discovery");
   store.Save(proposed);var loaded=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(path));C(WorldMacroProgress.Valid(loaded)&&loaded.campaign.Facts.Count(f=>f=="location:"+inn.Id)==1&&loaded.ui.discoveredMarkers.Contains(inn.MarkerId),"retry/reload retains one visit and marker");
   var map=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   C(catalog.Entries.Where(e=>!string.IsNullOrEmpty(e.MarkerId)).All(e=>map.Markers.Count(m=>m.Id==e.MarkerId&&m.RequiresArrival&&!m.InitiallyDiscovered)==1),"arrival markers uniquely bound, proximity discovery disabled");
   C(VillageSession().Content.SaveSlot=="world-demo-compact-cave-v4"&&WorldMacroProgress.CurrentVersion==8,"save slot/version unchanged");
   File.WriteAllLines(ArrivalOutput262+"/checks.txt",lines);return string.Join("\n",lines);
  }
  static string CaptureArrival262()
  {
   var scene=FrontageScene249();var s=VillageSession();var arrival=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Single();
   var cameraObject=new GameObject("ArrivalReviewCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(s.Walker.ViewCamera);camera.enabled=false;
   EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;var active=RenderTexture.active;
   var panel=new GameObject("ArrivalReviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)){hideFlags=HideFlags.HideAndDontSave};
   var canvas=panel.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.7f;canvas.sortingOrder=55;
   var scaler=panel.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
   var label=WorldLocationArrival.CreateName(panel.transform,arrival.Theme.Font);var detail=WorldLocationArrival.CreateDetail(label,arrival.Theme.Font);var ground=FinalSurface(scene);
   try
   {
    art.Observer=camera;
    for(int i=0;i<3;i++)
    {
     int w=i==2?1280:1920,h=i==2?720:1080;var rt=new RenderTexture(w,h,24);var tex=new Texture2D(w,h,TextureFormat.RGB24,false);
     try
     {
      WorldLocationArrival.SetNames(arrival.Catalog,arrival.Catalog.Entries.Single(e=>e.Id==(i==0?"realm_cheongrim":i==1?"geumpyo_inn":"mine_interior")),label,detail);
      var foot=i==1?ground(3110,2250).point:ground(3118,2628).point;
      var eye=ground(foot.x,foot.z-15).point+Vector3.up*2;
      camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(new Vector3(0,-.02f,1)));
      if(i==2){var entrance=s.Content.StartFeet+Vector3.up*1.7f;camera.transform.SetPositionAndRotation(entrance,Quaternion.LookRotation(Vector3.left));}
      camera.targetTexture=rt;camera.aspect=w/(float)h;camera.fieldOfView=62;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(ArrivalOutput262+"/name-"+i+".png",tex.EncodeToPNG());
     }
     finally{camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
    }
   }
   finally{art.Observer=observer;Object.DestroyImmediate(panel);Object.DestroyImmediate(cameraObject);}
   return "Actual name-only widget captured offscreen: two 1080p and one 720p layout previews. Injected example names, not live arrival playback.";
  }
 }
}
