using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string ProgressionRoot251="CheongrimProgression251";
  public static string ProgressionCodeVersion251()=>"251.3: filtered rebuild roots and canonical main path source";
  [Serializable] public sealed class ProgressionRoute251 { public string id,fromId,toId;public Vector2[] points;public float length; }
  [Serializable] public sealed class ProgressionPlan251 { public ProgressionRoute251[] routes; }
  public static string ProgressionBuild251()
  {
   var scene=FrontageScene249();var s=VillageSession();var roots=scene.GetRootGameObjects();
   if(scene.isDirty)throw new Exception("Save unrelated edits before authoring progression");
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   var layout=manifest.Layout;var ground=FinalSurface(scene);Vector3 G(Vector2 p)=>ground(p.x,p.y).point;
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(ProgressionOutput251+"/route-plan.json"));
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Progression251";
   Directory.CreateDirectory(folder);AssetDatabase.Refresh();
   T Asset<T>(string name,Func<T> make) where T:Object{string p=folder+"/"+name+".asset";var value=AssetDatabase.LoadAssetAtPath<T>(p);if(value==null){value=make();AssetDatabase.CreateAsset(value,p);}return value;}
   var old=roots.SingleOrDefault(g=>g.name==ProgressionRoot251);if(old!=null)Object.DestroyImmediate(old);
   roots=roots.Where(g=>g!=null).ToArray();
   var root=new GameObject(ProgressionRoot251);
   bool OwnedActor(string id)=>id==WorldMacroPlaytestSession.CheongryongId||id=="demo_growth_lesson";
   var actors=s.Actors.Where(a=>a!=null&&!OwnedActor(a.Id)).ToList();
   var points=s.Content.Points.Where(p=>p.Id!=DemoGukRevisitSite.Id&&p.Id!=DemoGukRevisitSite.PreviewId&&p.Id!=DemoGrowthLessonLink.LessonId&&p.Id!="sanctuary_rest251").ToList();
   var visuals=s.InteractionVisuals.Where(p=>points.Any(q=>q.Id==p.Id)).ToList();
   var encounters=s.Content.Encounters.Where(e=>!OwnedActor(e.Id)).ToList();
   var source=EditorSceneManager.OpenScene(Scene,OpenSceneMode.Additive);
   DemoGukRevisitSite site;
   try
   {
    foreach(var original in source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).Where(a=>OwnedActor(a.Id)))
    {
     var a=Object.Instantiate(original,root.transform);a.name=a.Id;
     bool boss=a.Id==WorldMacroPlaytestSession.CheongryongId;var place=layout.Places.Single(p=>p.Id==(boss?"sanctuary":"logging"));
     var feet=G(place.XZ);var agent=a.GetComponent<NavMeshAgent>();agent.enabled=false;
     a.transform.position=feet+Vector3.up*agent.baseOffset;a.PatrolPoints=new[]{a.transform.position};a.Session=null;a.Player=s.Walker.Body.transform;
     a.Leash=boss?30:14;a.DetectionRange=boss?22:12;
     var lesson=a.GetComponent<DemoGrowthLessonLink>();if(lesson!=null)lesson.Session=s;
     actors.Add(a);encounters.Add(new WorldMacroPlaytestSO.Encounter{Id=a.Id,ContentId=boss?a.Id:DemoGrowthLessonLink.LessonId,Feet=feet,Patrol=new[]{feet},Ranged=a.Ranged,Speed=a.Speed,Detection=a.DetectionRange,Leash=a.Leash,Activation=120,RespawnOnRest=!boss});
     place.HasSourceBinding=true;place.SceneRoots=new[]{ProgressionRoot251+"/"+a.Id};place.EncounterIds=new[]{a.Id};
    }
    var originalSite=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DemoGukRevisitSite>(true)).Single();
    site=Object.Instantiate(originalSite,root.transform);site.name="GukRevisit251";
   }
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   // Register in the same content collection used by startup, culling, damage and save proof.
   if(actors.Count(a=>OwnedActor(a.Id))!=2)throw new Exception("Expected existing boss and growth lesson source");
   s.Actors=actors.ToArray();s.Content.Encounters=encounters.ToArray();
   s.DemoWoodLiftProfile=AssetDatabase.LoadAssetAtPath<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/020_AD6D.asset");
   var wiring=new SerializedObject(s.Walker.Wiring);var enemies=wiring.FindProperty("_enemies");enemies.arraySize=actors.Count;
   for(int i=0;i<actors.Count;i++)enemies.GetArrayElementAtIndex(i).objectReferenceValue=actors[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
   var soil=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).First(r=>r.name=="Natural_Cave_Floor").sharedMaterial;
   var road=Asset("FootpathSoil",()=>Object.Instantiate(soil));
   var paths=new Dictionary<string,Vector3[]>();
   foreach(var route in plan.routes)
   {
    var path=new List<Vector3>();for(int i=1;i<route.points.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(route.points[i-1],route.points[i])/2));for(int j=0;j<n;j++)path.Add(G(Vector2.Lerp(route.points[i-1],route.points[i],j/(float)n)));}path.Add(G(route.points.Last()));paths[route.id]=path.ToArray();
    var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();float distance=0;
    for(int i=0;i<path.Count;i++)
    {
     if(i>0)distance+=Vector3.Distance(path[i],path[i-1]);var d=path[Math.Min(i+1,path.Count-1)]-path[Math.Max(0,i-1)];d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up);
     for(int k=0;k<5;k++){var p=path[i]+side*((k/4f-.5f)*3.2f);p=ground(p.x,p.z).point+Vector3.up*.025f;vertices.Add(p);uv.Add(new Vector2(k*.8f,distance));}
     if(i>0)for(int k=0;k<4;k++){int v=i*5+k;triangles.AddRange(new[]{v-5,v,v+1,v-5,v+1,v-4});}
    }
    var mesh=Asset(route.id,()=>new Mesh());mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);UpwardRibbon258(mesh);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject(route.id,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=road;go.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    var oldRoutes=layout.Routes.Where(r=>r.Id!=route.id&&!(r.From==route.fromId&&r.To==route.toId)).ToList();oldRoutes.Add(new CompactWorldLayoutSO.Route{Id=route.id,From=route.fromId,To=route.toId,Role=CompactRouteRole.Main,Width=3.2f,Bends=route.points.Skip(1).Take(route.points.Length-2).ToArray()});layout.Routes=oldRoutes.ToArray();
   }
   // The lift destination is visible from the logging approach. Its pad and reward
   // use one authored site; the old walk-up ramp is removed from this private clone.
   var cache=layout.Places.Single(p=>p.Id=="high_cache");cache.XZ=new Vector2(3329,2675);
   var pad=G(cache.XZ);float floor=pad.y;
   for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)floor=Mathf.Max(floor,ground(pad.x+x*1.6f,pad.z+z*1.6f).point.y);
   pad.y=floor+.07f;Vector3 shift=pad-site.LiftPad.position;site.transform.position+=shift;
   var ramp=site.transform.Find("ExistingStone_Descent");if(ramp!=null)Object.DestroyImmediate(ramp.gameObject);
   site.DescentExit.position=ground(pad.x-3,pad.z).point;
   var stone=site.GetComponentsInChildren<MeshRenderer>(true).First().sharedMaterial;
   var apron=GameObject.CreatePrimitive(PrimitiveType.Cube);apron.name="LiftApron251";apron.transform.SetParent(site.transform);apron.transform.position=pad-Vector3.up*.18f;apron.transform.localScale=new Vector3(3.4f,.36f,3.4f);apron.GetComponent<Renderer>().sharedMaterial=stone;
   void Point(string id,PrologueInteractionKind kind,Vector3 position,Renderer[] rr,string text,float radius=2)
   {points.Add(new PrologueContentSO.Point{Id=id,Kind=kind,Position=position,Radius=radius,Prompt="",Text=text});visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=rr});}
   var reward=site.transform.Find("ExistingRewardBag");
   Point(DemoGukRevisitSite.Id,PrologueInteractionKind.Evidence,site.UpperSurface.position+Vector3.up*.06f,reward.GetComponentsInChildren<Renderer>(true),"나무 틈에 감춘 보따리다.",1.25f);
   Point(DemoGukRevisitSite.PreviewId,PrologueInteractionKind.Preview,pad,new[]{apron.GetComponent<Renderer>()},"낡은 밧줄 끝이 바위 위에서 끊겼다.");
   var learner=actors.Single(a=>a.Id=="demo_growth_lesson");Point(DemoGrowthLessonLink.LessonId,PrologueInteractionKind.Evidence,learner.transform.position-Vector3.up*.875f,learner.GetComponentsInChildren<Renderer>(true),"잘린 자리가 다시 부푼다. 박힌 쇠 주위만 말라 있다.",3);
   var logging=layout.Places.Single(p=>p.Id=="logging");logging.InteractionId=DemoGrowthLessonLink.LessonId;logging.InteractionIds=new[]{DemoGrowthLessonLink.LessonId};
   cache.HasSourceBinding=true;cache.SceneRoots=new[]{ProgressionRoot251+"/GukRevisit251"};cache.InteractionIds=new[]{DemoGukRevisitSite.Id,DemoGukRevisitSite.PreviewId};
   // A small stone rest marker at the arena approach, deliberately clear of combat.
   var restFeet=G(new Vector2(3204,3492));var rest=GameObject.CreatePrimitive(PrimitiveType.Cube);rest.name="sanctuary_rest251";rest.transform.SetParent(root.transform);rest.transform.position=restFeet+Vector3.up*.6f;rest.transform.localScale=new Vector3(.65f,1.2f,.45f);rest.GetComponent<Renderer>().sharedMaterial=stone;
   Point(rest.name,PrologueInteractionKind.Rest,restFeet,new[]{rest.GetComponent<Renderer>()},"");
   var checkpoint=ground(restFeet.x-2,restFeet.z-2).point;
   s.Content.Checkpoints=s.Content.Checkpoints.Where(c=>c.Id!=rest.name).Concat(new[]{new WorldMacroPlaytestSO.CheckpointSpec{Id=rest.name,Label="성역 앞 돌무더기",Feet=checkpoint,Yaw=0,Shop=false}}).ToArray();
   var sanctuary=layout.Places.Single(p=>p.Id=="sanctuary");sanctuary.CheckpointIds=new[]{rest.name};sanctuary.InteractionIds=new[]{rest.name};
   sanctuary.SceneRoots=new[]{ProgressionRoot251+"/cheongryong",ProgressionRoot251+"/sanctuary_rest251"};
   s.Content.Points=points.ToArray();s.InteractionVisuals=visuals.ToArray();
   string mainSource=folder+"/OriginalMainPath251.json";
   if(!File.Exists(mainSource))File.WriteAllText(mainSource,JsonUtility.ToJson(new VillageLedger245{main=s.Content.MainPath}));
   s.Content.MainPath=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(mainSource)).main.Concat(paths.Values.SelectMany(p=>p.Skip(1))).ToArray();
   // Clear only vegetation in the new path/combat footprints, with a private placement sheet.
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();var sheet=Asset("Placements",()=>Object.Instantiate(art.Sheet));
   bool Clear(Vector3 p)=>paths.Values.Any(path=>FlatPathDistance(p,path)<3.3f)||Vector2.Distance(new Vector2(p.x,p.z),sanctuary.XZ)<30||Vector3.Distance(p,pad)<12||Vector3.Distance(p,restFeet)<5||Vector3.Distance(p,learner.transform.position)<7;
   var originalPlants=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Village245/Placements.asset");
   if(originalPlants==null)throw new Exception("Expected pre-progression placement source");
   sheet.FixedPlacements=originalPlants.FixedPlacements.Where(p=>!Clear(p.Position)).ToArray();art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();
   var kept=new HashSet<string>(sheet.FixedPlacements.Select(p=>p.Id));foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name.StartsWith("grove_")||t.name.StartsWith("outcrop_")||t.name.StartsWith("path_meshy_")||t.name.StartsWith("inn_meshy_")).ToArray())if(t!=null&&!kept.Contains(t.name)&&Clear(t.position))Object.DestroyImmediate(t.gameObject);
   var map=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   map.Lines=map.Lines.Where(l=>!paths.ContainsKey(l.Id)).Concat(paths.Select(pair=>new WorldMapLineSpec{Id=pair.Key,Kind=WorldMapLineKind.Trail,Points=pair.Value.Select(p=>new Vector2(p.x,p.z)).ToArray()})).ToArray();
   foreach(var stage in s.Content.Campaign.Stages){var p=points.FirstOrDefault(x=>x.Id==stage.TriggerId);var e=encounters.FirstOrDefault(x=>x.Id==stage.TriggerId);if(p!=null)stage.Destination=p.Position;else if(e!=null)stage.Destination=e.Feet;}
   foreach(var obj in new Object[]{s,s.Content,s.Content.Campaign,layout,manifest,art,sheet,map})EditorUtility.SetDirty(obj);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));File.WriteAllText(ProgressionOutput251+"/content.json",JsonUtility.ToJson(s.Content,true));
   File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(sheet,true));File.WriteAllText(Output+"/Cartography/placements.json",JsonUtility.ToJson(sheet,true));
   File.WriteAllText(ProgressionOutput251+"/build.txt","Boss + growth lesson + Guk revisit + three terrain-following routes authored. Navigation and runtime validation pending.");
   return File.ReadAllText(ProgressionOutput251+"/build.txt");
  }
 }
}
