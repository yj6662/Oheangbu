using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static T SavePrivate<T>(T value,string path) where T:ScriptableObject
  {var existing=AssetDatabase.LoadAssetAtPath<T>(path);if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,existing);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;}
  static string WireSlice()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Canonical Compact edit required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   var original=SceneManager.GetActiveScene();var candidate=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
   var sources=original.GetRootGameObjects();var roots=new List<GameObject>();var references=new Dictionary<Object,Object>();var cleared=new List<string>();
   string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');
   try
   {
    var manifest=candidate.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();var content=manifest.Content;
    var oldPeople=candidate.GetRootGameObjects().SingleOrDefault(g=>g.name=="Rebuild_InnPeople");if(oldPeople!=null)Object.DestroyImmediate(oldPeople);
    var people=new GameObject("Rebuild_InnPeople");SceneManager.MoveGameObjectToScene(people,candidate);
    var sourcePoints=sources.SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)).ToArray();
    foreach(string id in new[]{"logger","herbalist"})
    {
     var source=sourcePoints.Single(p=>p.Id==id);var clone=Object.Instantiate(source.gameObject,people.transform);clone.name=id;clone.SetActive(true);
     clone.transform.position=content.Points.Single(p=>p.Id==id).Position;
    }
    content.Checkpoints=new[]{
     new WorldMacroPlaytestSO.CheckpointSpec{Id="mine_start",Label="폐광",Feet=content.StartFeet,Yaw=content.StartYaw},
     new WorldMacroPlaytestSO.CheckpointSpec{Id="geumpyo_inn",Label="금표 주막",Feet=content.InnCheckpointFeet,Yaw=0,Shop=true}};
    string[] names={"WorldMacro_Playtest","Macro_CombatPlayerRig","Playtest_UI","WorldMacro_MagicPalanquin_TEST"};
    foreach(var previous in candidate.GetRootGameObjects().Where(g=>names.Contains(g.name)).ToArray())Object.DestroyImmediate(previous);
    foreach(var name in names)
    {
     var source=sources.Single(g=>g.name==name);var clone=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(clone,candidate);clone.name=name;roots.Add(clone);
     var before=source.GetComponentsInChildren<Transform>(true);var after=clone.GetComponentsInChildren<Transform>(true);if(before.Length!=after.Length)throw new Exception("Clone shape differs "+name);
     for(int i=0;i<before.Length;i++)
     {
      references[before[i].gameObject]=after[i].gameObject;
      var a=before[i].GetComponents<Component>();var b=after[i].GetComponents<Component>();if(a.Length!=b.Length)throw new Exception("Component shape differs");
      for(int j=0;j<a.Length;j++)if(a[j]!=null)references[a[j]]=b[j];
     }
    }
    var sourceSession=sources.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
    references[sourceSession.Content]=content;
    var terrain=candidate.GetRootGameObjects().Single(g=>g.name=="Compact_Rebuild_Terrain");Physics.SyncTransforms();
    content.MainPath=content.MainPath.Select(p=>{
     var ground=Physics.RaycastAll(new Vector3(p.x,500,p.z),Vector3.down,700,1,QueryTriggerInteraction.Ignore)
      .Where(h=>h.collider.transform.IsChildOf(terrain.transform)).OrderByDescending(h=>h.point.y).ToArray();
     if(ground.Length==0)throw new Exception("Road sample has no candidate terrain: "+p);return ground[0].point;
    }).ToArray();
    var sheet=ScriptableObject.CreateInstance<WorldMacroSheetSO>();sheet.BoundsMin=Vector2.zero;sheet.BoundsMax=manifest.Layout.Extent;sheet.CompressionSource=null;sheet.Compression=null;
    sheet.Outline=new[]{Vector2.zero,new Vector2(sheet.BoundsMax.x,0),sheet.BoundsMax,new Vector2(0,sheet.BoundsMax.y)};
    sheet.Routes=new[]{new WorldMacroSheetSO.RouteSpec{Id="mine_inn",From="mine",To="geumpyo_inn",Points=content.MainPath,Width=3,Carriage=false}};
    sheet=SavePrivate(sheet,folder+"/Geography.asset");
    var sourceUi=sources.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();references[sourceUi.WorldSheet]=sheet;
    var map=ScriptableObject.CreateInstance<WorldMapBakedDataSO>();map.Revision=manifest.Generation;map.BoundsMin=sheet.BoundsMin;map.BoundsMax=sheet.BoundsMax;map.Outline=sheet.Outline;
    map.Lines=new[]{new WorldMapLineSpec{Id="mine_inn",Kind=WorldMapLineKind.Trail,Points=content.MainPath.Select(p=>new Vector2(p.x,p.z)).ToArray()}};
    map.Markers=new[]{new WorldMapMarkerSpec{Id="mine",Label="폐광",Kind=WorldMapMarkerKind.Place,WorldXZ=new Vector2(content.StartFeet.x,content.StartFeet.z),InitiallyDiscovered=true},new WorldMapMarkerSpec{Id="geumpyo_inn",Label="금표 주막",Kind=WorldMapMarkerKind.Rest,WorldXZ=new Vector2(content.InnCheckpointFeet.x,content.InnCheckpointFeet.z),CompletionId="geumpyo_inn"}};
    var texture=new Texture2D(200,300,TextureFormat.RGB24,false);var paper=new Color(.91f,.90f,.85f);var ink=new Color(.22f,.28f,.25f);
    for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
    {
     var p=new Vector3((x+.5f)/texture.width*sheet.BoundsMax.x,500,(y+.5f)/texture.height*sheet.BoundsMax.y);
     var hits=Physics.RaycastAll(p,Vector3.down,700,1,QueryTriggerInteraction.Ignore).Where(h=>h.collider.transform.IsChildOf(terrain.transform)).ToArray();float height=hits.Length==0?0:hits.Max(h=>h.point.y);
     texture.SetPixel(x,y,Color.Lerp(paper,ink,Mathf.Clamp01(height/320)));
    }
    texture.Apply();string imagePath=folder+"/Map.png";File.WriteAllBytes(imagePath,texture.EncodeToPNG());Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(imagePath);map.BaseMap=AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
    map=SavePrivate(map,folder+"/Map.asset");references[sourceUi.MapData]=map;
    if(sourceSession.RuntimeState!=null)references[sourceSession.RuntimeState]=SavePrivate(Object.Instantiate(sourceSession.RuntimeState),folder+"/RuntimeState.asset");
    foreach(var component in roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c!=null&&!(c is Transform)))
    {
     var so=new SerializedObject(component);var p=so.GetIterator();bool enter=true;
     while(p.Next(enter))
     {
      enter=true;if(p.propertyType!=SerializedPropertyType.ObjectReference||p.objectReferenceValue==null)continue;
      var value=p.objectReferenceValue;if(references.TryGetValue(value,out var replacement))p.objectReferenceValue=replacement;
      else if(value is Component c&&c.gameObject.scene==original||value is GameObject go&&go.scene==original)
      {cleared.Add(component.GetType().Name+"."+p.propertyPath);p.objectReferenceValue=null;}
     }
     so.ApplyModifiedPropertiesWithoutUndo();
    }
    var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();session.Content=content;session.TestSaveSuffix="";session.PreviewSheetBoundsMin=sheet.BoundsMin;session.PreviewSheetBoundsMax=sheet.BoundsMax;
    session.PreviewPoints=people.GetComponentsInChildren<WorldMacroContentPoint>(true);session.PreviewSheet=SavePrivate(ScriptableObject.CreateInstance<WorldMacroContentSheetSO>(),folder+"/Preview.asset");session.DemoSouthGateGeneral=null;
    session.Actors=session.GetComponentsInChildren<PrologueEncounter>(true).Where(a=>content.Encounters.Any(e=>e.Id==a.Id)).ToArray();
    foreach(var actor in session.Actors)
    {var spec=content.Encounters.Single(e=>e.Id==actor.Id);actor.transform.position=spec.Feet;actor.PatrolPoints=spec.Patrol;actor.Player=session.Walker.Body.transform;actor.Session=null;}
    foreach(Transform child in session.transform)if(child.GetComponent<PrologueEncounter>()==null)child.gameObject.SetActive(false);
    var query=session.GetComponent<WorldTerrainQuery>();query.Water=Array.Empty<WorldTerrainQuery.WaterTriangle>();
    var player=session.Walker.Body;player.enabled=false;player.transform.SetPositionAndRotation(content.StartFeet,Quaternion.Euler(0,content.StartYaw,0));player.enabled=true;
    var vehicle=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPalanquinController>(true)).Single();vehicle.transform.position=content.InnCheckpointFeet+new Vector3(12,1,12);
    if(Physics.Raycast(vehicle.transform.position+Vector3.up*100,Vector3.down,out var vehicleGround,200,1,QueryTriggerInteraction.Ignore))vehicle.transform.position=vehicleGround.point+Vector3.up*.25f;
    var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();ui.Content=content;ui.WorldSheet=sheet;ui.MapData=map;ui.PlaySceneName=Path.GetFileNameWithoutExtension(receipt.scene);
    SceneManager.SetActiveScene(candidate);
    var look=candidate.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Oheangbu.App.WorldLookDriver>(true)).Single();
    var lookData=new SerializedObject(look);
    lookData.FindProperty("_skyCamera").objectReferenceValue=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
    lookData.FindProperty("_skyGeography").objectReferenceValue=sheet;
    string skyPath=folder+"/Sky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
    if(sky==null){sky=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/PineRestGame/Sky.mat"));AssetDatabase.CreateAsset(sky,skyPath);}
    lookData.FindProperty("_useSkybox").boolValue=true;lookData.FindProperty("_skyboxMaterial").objectReferenceValue=sky;
    // The old regional coordinates do not apply to the rebuilt layout. Regional variants follow when realms are authored.
    lookData.FindProperty("_regionalSkyProfile").objectReferenceValue=null;
    lookData.ApplyModifiedPropertiesWithoutUndo();
    var review=candidate.GetRootGameObjects().SingleOrDefault(g=>g.name=="Migration_Review_Camera");if(review!=null)review.SetActive(false);
    if(session.Actors.Length!=content.Encounters.Length||session.Walker.Body.gameObject.scene!=candidate||!map.IsUsable)throw new Exception("Runtime ownership checks failed");
    foreach(var component in roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c!=null))
    {var so=new SerializedObject(component);var p=so.GetIterator();while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue is Component c&&c.gameObject.scene==original)throw new Exception("Leaked source reference "+component.GetType().Name+"."+p.propertyPath);}
    EditorUtility.SetDirty(content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(candidate);
    string report="Runtime roots=4; actors="+session.Actors.Length+"; inn NPC visuals=2; explicit checkpoints=2; private map/geography/runtime state; scene ownership passed. Cleared external references by component: "+string.Join(", ",cleared.GroupBy(x=>x.Split('.')[0]).Select(g=>g.Key+"="+g.Count()))+". Actual Play verification: see slice_runtime_checks.txt.";
    File.WriteAllText(Output+"/runtime_wiring.txt",report);return report;
   }
   finally{EditorSceneManager.CloseScene(candidate,true);SceneManager.SetActiveScene(original);}
  }
  [MenuItem("Oheangbu/Compact Rebuild/Open playable mine and inn candidate")]
  static void OpenPlayableSlice()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before changing scene");
   var current=SceneManager.GetActiveScene();
   if(current.isDirty){
    string backup="Assets/_Project/Scenes/Recovery/BeforeCompactSlice_"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".unity";
    if(!EditorSceneManager.SaveScene(current,backup,true))throw new Exception("Scene recovery failed");
   }
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));EditorSceneManager.OpenScene(receipt.scene);
  }
 }
}
