using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public const string Scene="Assets/_Project/Scenes/World/W_Demo_Compact.unity";
  public const string Folder="Assets/_Project/Art/World/WorldCompact/Rebuild";
  const string Output="../Art/World/Compact/Rebuild";
  [Serializable] class DependencyList {public string[] assets;}
  public static string Run(string command)
  {
   Directory.CreateDirectory(Output);
   if(command.StartsWith("terrain244:"))return Terrain243(command.Substring("terrain244:".Length),true);
   if(command.StartsWith("terrain243:"))return Terrain243(command.Substring("terrain243:".Length));
   if(command.StartsWith("rest242:"))return CompactRebuildRest242.Run(command.Substring("rest242:".Length));
   if(command.StartsWith("mouth241:"))return Mouth241(command.Substring("mouth241:".Length));
   if(command.StartsWith("boundary240:"))return Boundary240(command.Substring("boundary240:".Length));
   if(command.StartsWith("surface239:"))return Surface239(command.Substring("surface239:".Length));
   if(command.StartsWith("atmosphere238:"))return Atmosphere238(command.Substring("atmosphere238:".Length));
   if(command=="open-test")return OpenTest();
   if(command=="cave-polish-survey")return CavePolishSurvey();
   if(command=="cave-polish-build")return CavePolishBuild();
   if(command=="cave-polish-audit")return CavePolishAudit();
   if(command=="cave-polish-map")return CavePolishMap();
   if(command=="cave-polish-finish")return CavePolishFinish();
   if(command.StartsWith("cave-polish-capture:"))return CavePolishCapture(command.Substring("cave-polish-capture:".Length));
   if(command=="continuation-survey")return ContinuationSurvey(false);
   if(command=="gesture-build")return ContinuationSurvey(true);
   if(command=="cave-support-fix")return FixGallerySupports();
   if(command=="gesture-review")return WorldMacroPlayerReRigAuthoring.ReviewArticulatedStrokes(false);
   if(command=="gesture-regression")return WorldMacroPlayerReRigAuthoring.CheckCompactStrokeRegression();
   if(command=="gesture-capture")return WorldMacroPlayerReRigAuthoring.ReviewArticulatedStrokes(true);
   if(command=="cave-explore-build")return CaveExploreBuild();
   if(command=="survey-models")return SurveyModels();
   if(command=="logger-rig-import")return ImportLoggerRig();
   if(command=="logger-rig-capture")return CaptureLoggerRig();
   if(command=="survey-build")return SurveyBuild();
   if(command=="survey-inventory")return SurveyInventory();
   if(command=="focus-map-export")return FocusMapBuild(false);
   if(command=="focus-map-build")return FocusMapBuild(true);
   if(command=="cave-layout-build")return CaveLayoutBuild();
   if(command=="ui-f-configure")return ConfigureWorldInteraction();
   if(command=="ui-build")return UiBuild();
   if(command=="ui-inventory")return UiInventory();
   if(command=="open")return Open();
   if(command=="campaign")return Campaign();
   if(command=="checks")return Checks();
   if(command=="layout")return Layout();
   if(command=="terrain")return Terrain();
   if(command=="bindings")return Bindings();
   if(command=="bind-slice")return BindSlice();
   if(command=="migrate-slice")return MigrateSlice();
   if(command=="nav-slice")return NavSlice();
   if(command=="cave-walk")return WalkSlice(true);
   if(command=="walk-slice")return WalkSlice();
   if(command=="wire-slice")return WireSlice();
   if(command=="art-inventory")return ArtInventory();
   if(command=="art-build")return ArtBuild();
   if(command=="art-audit")return ArtAudit();
   if(command=="art-motion")return ArtMotion();
   if(command=="art-measure")return ArtMeasure();
   if(command.StartsWith("art-view:"))return ArtView(int.Parse(command.Substring(9)));
   if(command.StartsWith("art-capture:"))return ArtCapture(int.Parse(command.Substring(12)));
   if(command.StartsWith("ui-"))return UiRuntime(command);
   if(command.StartsWith("slice-"))return CompactRebuildSliceChecks.Run(command);
   if(command=="play"||command=="runtime"||command=="stop")return CompactRebuildRuntimeChecks.Run(command);
   throw new ArgumentException(command);
  }
  [MenuItem("Oheangbu/Compact Rebuild/Open canonical scene")]
  static void OpenMenu()=>Debug.Log(Open());
  [MenuItem("Oheangbu/Compact Rebuild/Open latest test scene")]
  static void OpenTestMenu()=>Debug.Log(OpenTest());
  static string OpenTest()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play first");
   if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Save the current scene before opening the test scene");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   EditorSceneManager.OpenScene(receipt.scene);
   return "Latest test scene opened: "+receipt.scene+"; existing save settings retained";
  }
  static string Open()
  {
   if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
   var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   if(active.isDirty)
   {
    string recovery="Assets/_Project/Scenes/Recovery";
    if(!AssetDatabase.IsValidFolder(recovery))AssetDatabase.CreateFolder("Assets/_Project/Scenes","Recovery");
    if(!EditorSceneManager.SaveScene(active,recovery+"/PreCompactRebuild_"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".unity",true))throw new Exception("Unsaved scene recovery failed");
   }
   EditorSceneManager.OpenScene(Scene);
   var dependencies=AssetDatabase.GetDependencies(new[]{Scene,"Assets/_Project/Scenes/World/W_Demo_Compact_Title.unity"},true).OrderBy(x=>x).ToArray();
   File.WriteAllText(Output+"/dependencies.json",JsonUtility.ToJson(new DependencyList{assets=dependencies},true));
   return "Canonical Compact opened; dependency inventory="+dependencies.Length+"; no terrain regeneration";
  }
  static string Campaign()
  {
   if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Compact edit required");
   var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>("Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/_Project/Art/World/WorldCompact","Rebuild");
   string path=Folder+"/Campaign.asset";
   var profile=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(path);
   if(profile==null){profile=Object.Instantiate(content.Campaign);AssetDatabase.CreateAsset(profile,path);}
   profile.CampaignId="compact-rebuild-v2";profile.UseExplicitPrerequisites=true;profile.EnvironmentalGuidance=true;
   profile.InitialCompletedIds=new[]{"commission"};
   foreach(var stage in profile.Stages){stage.PrerequisiteIds=Array.Empty<string>();stage.RequiredFacts=Array.Empty<string>();stage.GrantedFacts=Array.Empty<string>();}
   DemoCampaignProfile.Stage S(string id)=>profile.Stages.Single(s=>s.Id==id);
   void Depends(string id,params string[] prerequisites)=>S(id).PrerequisiteIds=prerequisites;
   S("commission").GrantedFacts=new[]{"case:mine_commissioned"};
   S("mine_evidence").GrantedFacts=new[]{"evidence:mine_blast"};
   S("office_report").RequiredFacts=new[]{"case:mine_commissioned","evidence:mine_blast"};S("office_report").Optional=true;
   S("inn_rest").Optional=true;
   S("relay").GrantedFacts=new[]{"met:jeongdam"};
   S("cargo_contract").RequiredFacts=new[]{"met:jeongdam"};
   S("logging").RequiredDefeatedIds=Array.Empty<string>();S("logging").GrantedFacts=new[]{"evidence:logging_growth"};S("logging").Optional=true;
   S("deep_forest").Optional=true;S("deep_forest").GrantedFacts=new[]{"learned:metal_counters_growth"};
   S("cheongryong").GrantedFacts=new[]{"defeated:cheongryong","ability:guk","virtue:ren"};
   Depends("guk_return","cheongryong");Depends("escort","cargo_contract","cheongryong");
   Depends("checkpoint_one","escort");Depends("checkpoint_two","checkpoint_one");Depends("delivery","checkpoint_two");Depends("south_gate","delivery");Depends("ending","south_gate");
   var lines=new System.Collections.Generic.Dictionary<string,string>{
    {"commission","폭파가 난 광산이다. 먼저 안에 남은 흔적을 살핀다."},
    {"mine_evidence","채굴 자국 사이에 검게 탄 틈이 남았다. 잘린 심지는 광석 먼지에 묻혀 있다."},
    {"office_report","화약 흔적이라… 장부에는 그런 작업이 없었소. 이 심지는 내가 맡겠소."},
    {"inn_rest","처마 아래 바람이 잦아든다."},
    {"relay","오랜만이군. 벌목꾼들이 숲에서 내려왔네. 베어 놓은 나무가 다시 자란다더군."},
    {"cargo_contract","도성의 객주까지 함께 가겠소? 봉인은 인도할 때까지 그대로 두시오."},
    {"logging","잘린 나이테를 새 뿌리가 비집고 나왔다. 박힌 쇠도끼 둘레만 비어 있다."},
    {"deep_forest","쇠가 닿은 자리에 성장이 멎는다."},
    {"cheongryong","오래된 석경이 드러났다."},
    {"guk_return","아래에서는 보이지 않던 길이 이어진다."},
    {"escort","왕소가 봉인 화물 곁에 자리를 잡는다."},
    {"checkpoint_one","차패를 확인했소. 화물 봉인은 훼손하지 마시오."},
    {"checkpoint_two","이 짐을 보증한 도사로 기록하겠소."},
    {"delivery","봉인이 온전하군. 여기서부터는 우리가 맡겠소."},
    {"south_gate","낯선 장수가 길을 비킨다."},
    {"ending","황경 남문이 열린다."}};
   foreach(var stage in profile.Stages){if(lines.TryGetValue(stage.Id,out var text))stage.Dialogue=text;stage.TravelHint="";}
   profile.Testimonies=new[]{
    new DemoCampaignProfile.Testimony{TriggerId="jeongdam_j1",RequiredFacts=new[]{"evidence:logging_growth"},Text="그 도끼 자국을 봤나. 벌목꾼들은 나무가 쇠를 피한다고 했네. 나는 그저 겁먹은 소리인 줄 알았지."},
    new DemoCampaignProfile.Testimony{TriggerId="jeongdam_j1",RequiredFacts=new[]{"defeated:cheongryong"},Text="숲에서 소리가 끊겼더군. 자네가 그 안까지 갔나? 돌아오지 않은 벌목꾼들이 아직 마음에 걸리네."},
    new DemoCampaignProfile.Testimony{TriggerId="wangso_w1",RequiredFacts=new[]{"met:jeongdam"},Text=lines["cargo_contract"]}};
   if(!profile.IsValid)throw new Exception("Invalid explicit campaign");
   if(content.Opening!=null&&!content.Points.Any(p=>p.Id==WorldMacroOpeningProfileSO.CommissionId))content.Points=content.Points.Concat(new[]{JsonUtility.FromJson<PrologueContentSO.Point>(JsonUtility.ToJson(content.Opening.Commission))}).ToArray();
   content.Opening=null;content.Campaign=profile;content.SaveSlot="world-demo-compact-rebuild-v2";content.TerrainRevision="compact-rebuild-v2";
   foreach(var point in content.Points)
   {
    var stage=profile.Stages.FirstOrDefault(s=>s.Event==DemoEventKind.Interaction&&s.TriggerId==point.Id);
    if(stage==null)continue;
    point.Text=stage.Dialogue;
    if(stage.Id=="logging"||stage.Id=="mine_evidence"){point.RequiredDefeated=Array.Empty<string>();point.RequiredCompleted=Array.Empty<string>();}
    if(stage.Id=="cargo_contract")point.Text="성 안으로 들일 짐이 있소. 길목 역참의 정담과는 아는 사이요?";
   }
   EditorUtility.SetDirty(profile);EditorUtility.SetDirty(content);AssetDatabase.SaveAssets();
   return "Compact explicit campaign connected; separate save; original geometry retained until layout rebuild";
  }
 }
}
