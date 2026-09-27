using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoFoundationAuthoring
    {
        public const string Folder="Assets/_Project/Art/Demo/Foundation";
        public const string Scene="Assets/_Project/Scenes/World/W_Demo_Campaign.unity";
        public const string Title="Assets/_Project/Scenes/World/W_Demo_Title.unity";
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Demo/Foundation"));
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if(command.StartsWith("summons:"))return DemoSummonAuthoring.Execute(command.Substring(8));
            if(command.StartsWith("chapter2:"))return DemoChapterTwoAuthoring.Execute(command.Substring(9));
            if(command.StartsWith("chapter3:"))return DemoChapterThreeAuthoring.Execute(command.Substring(9));
            if(command.StartsWith("runtime:"))return DemoFoundationRuntimeChecks.Execute(command.Substring(8));
            if(command=="apply")return Apply();
            if(command=="capture-size"||command=="restore-size")return (string)typeof(WorldMacroPlaytestHudAuthoring).GetMethod(command=="capture-size"?"SelectCaptureSize":"RestoreCaptureSize",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            if(command=="tests")return Tests();
            if(command=="audit")return Audit();
            if(command=="combat") {var result=DemoCombatFoundationChecks.Run();File.WriteAllText(Output+"/combat_tests.json",result);return result;}
            throw new ArgumentException("Use apply, tests or audit.");
        }
        static void EnsureFolder(string path)
        {
            var parent=Path.GetDirectoryName(path).Replace('\\','/');
            if(!AssetDatabase.IsValidFolder(parent))EnsureFolder(parent);
            if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static string Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before demo authoring.");
            if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Current scene has unsaved changes; preserve them before cloning.");
            EnsureFolder(Folder);
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)==null&&!AssetDatabase.CopyAsset(WorldMacroPlaytestAuthoring.ScenePath,Scene))
                throw new IOException("Could not preserve source scene as demo copy.");
            EditorSceneManager.OpenScene(Scene,OpenSceneMode.Single);
            var session=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session==null||session.Content==null)throw new InvalidOperationException("Playtest session missing.");
            var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(Folder+"/Content.asset");
            if(content==null){content=UnityEngine.Object.Instantiate(session.Content);content.name="Demo Content";AssetDatabase.CreateAsset(content,Folder+"/Content.asset");}
            var campaign=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(Folder+"/Campaign.asset");
            if(campaign==null){campaign=ScriptableObject.CreateInstance<DemoCampaignProfile>();campaign.Stages=CreateStages();AssetDatabase.CreateAsset(campaign,Folder+"/Campaign.asset");}
            var economy=AssetDatabase.LoadAssetAtPath<DemoEconomyProfileSO>(Folder+"/Economy.asset");
            if(economy==null){economy=ScriptableObject.CreateInstance<DemoEconomyProfileSO>();AssetDatabase.CreateAsset(economy,Folder+"/Economy.asset");}
            content.Economy=economy;content.Campaign=campaign;content.SaveSlot="demo-campaign-foundation-v1";
            session.Content=content;session.TestSaveSuffix="";
            var ui=UnityEngine.Object.FindFirstObjectByType<Oheangbu.App.World.UI.PlaytestUiRoot>();
            if(ui!=null){ConfigureUi(ui,content);EditorUtility.SetDirty(ui);}
            EditorUtility.SetDirty(content);EditorUtility.SetDirty(session);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(Title)==null&&!AssetDatabase.CopyAsset("Assets/_Project/Scenes/World/W_Playtest_Title.unity",Title))throw new IOException("Demo title clone failed");
            var titleScene=EditorSceneManager.OpenScene(Title,OpenSceneMode.Single);
            var titleUi=UnityEngine.Object.FindFirstObjectByType<Oheangbu.App.World.UI.PlaytestUiRoot>();
            if(titleUi==null)throw new InvalidOperationException("Title UI missing");
            ConfigureUi(titleUi,content);EditorUtility.SetDirty(titleUi);EditorSceneManager.MarkSceneDirty(titleScene);EditorSceneManager.SaveScene(titleScene);
            var scenes=EditorBuildSettings.scenes.ToList();
            foreach(var path in new[]{Title,Scene})if(!scenes.Any(s=>s.path==path))scenes.Add(new EditorBuildSettingsScene(path,true));
            EditorBuildSettings.scenes=scenes.ToArray();
            EditorSceneManager.OpenScene(Scene,OpenSceneMode.Single);
            return Audit();
        }
        static void ConfigureUi(Oheangbu.App.World.UI.PlaytestUiRoot ui,WorldMacroPlaytestSO content)
        {
            ui.Content=content;ui.TitleSceneName=Path.GetFileNameWithoutExtension(Title);ui.PlaySceneName=Path.GetFileNameWithoutExtension(Scene);
            var map=AssetDatabase.LoadAssetAtPath<Oheangbu.App.World.UI.WorldMapBakedDataSO>(Folder+"/Map.asset");
            if(map==null&&ui.MapData!=null){map=UnityEngine.Object.Instantiate(ui.MapData);AssetDatabase.CreateAsset(map,Folder+"/Map.asset");}
            if(map!=null&&content.Opening!=null)
            {
                var markers=map.Markers.Where(m=>m.Id!="demo.village_office").ToList();var position=content.Opening.Commission.Position;
                markers.Add(new Oheangbu.App.World.UI.WorldMapMarkerSpec{Id="demo.village_office",Label="마을 관청",Kind=Oheangbu.App.World.UI.WorldMapMarkerKind.Settlement,WorldXZ=new Vector2(position.x,position.z),InitiallyDiscovered=true});
                map.Markers=markers.ToArray();ui.MapData=map;EditorUtility.SetDirty(map);AssetDatabase.SaveAssets();
            }
        }
        static string Capture(string name)
        {
            if(!EditorApplication.isPlaying||Screen.width!=1920||Screen.height!=1080)throw new InvalidOperationException("Need active 1920x1080 Game View");
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[a-z0-9_-]+$"))throw new ArgumentException("Invalid capture name");
            float commit=Oheangbu.EditorTools.Prologue.PrologueAudit.CommitRatio();
            if(commit>=.85f)throw new InvalidOperationException("Capture stopped: system commit >=85%");
            var view=(EditorWindow)EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Focus();view.Repaint();
            var path=Output+"/"+name+"_1920x1080.png";
            if(File.Exists(path))throw new IOException("Existing capture preserved; choose new name");
            ScreenCapture.CaptureScreenshot(path);
            File.WriteAllText(Output+"/"+name+"_capture.json","{\"commitRatio\":"+commit.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"width\":1920,\"height\":1080,\"method\":\"actual Game View, diagnostic setup; image requires visual inspection\"}");
            return path;
        }
        static DemoCampaignProfile.Stage S(string id,string trigger,DemoEventKind kind,string objective,bool implemented=false,int reward=0,string prompt="",string text="")
            =>new DemoCampaignProfile.Stage{Id=id,TriggerId=trigger,Event=kind,Objective=objective,Implemented=implemented,TongboReward=reward,Prompt=prompt,Dialogue=text};
        public static DemoCampaignProfile.Stage[] CreateStages()=>new[]{
            S("commission","village_commission",DemoEventKind.Interaction,"관청 아전에게 폐광 조사 의뢰를 확인한다 [F]",true,0,"폐광 조사 의뢰 확인",
                "폐광 쪽에서 폭파 소리가 났소. 현장에 남은 흔적을 살펴보고 돌아와 주시오.\n\n[G]로 마석 자동차를 부를 수 있소. 무슨 일이었는지는 물증을 보고 판단합시다."),
            S("mine_evidence","mine_inquiry",DemoEventKind.Interaction,"[G] 자동차를 불러 폐광으로 이동하고 폭파 흔적을 조사한다",true,0,"폭파 흔적 조사",
                "암벽에 파인 폭파 흔적과 흩어진 광석을 살핀다. 채굴 자국과는 달라 보이지만, 누가 무엇을 노렸는지는 알 수 없다.\n\n관청으로 돌아가 확인한 사실을 보고하자."),
            S("office_report","village_commission",DemoEventKind.Interaction,"관청으로 돌아가 아전에게 폐광 조사 결과를 보고한다 [F]",true,60,"폐광 조사 결과 보고",
                "흔적은 확인했으나 배후를 단정할 수는 없겠구려. 수고했소. 금표 주막에서 쉬며 벌목꾼에게 숲길 사정을 물어보시오. 역참 쪽 소식도 들을 수 있을 거요."),
            S("inn_rest","geumpyo_inn",DemoEventKind.Rest,"금표 주막으로 가서 휴식한다 [F]",true),
            S("relay","jeongdam_j1",DemoEventKind.Interaction,"역참에서 정담을 만난다"),
            S("cargo_contract","wangso_w1",DemoEventKind.Interaction,"객주 분소에서 봉인 화물 의뢰를 확인한다"),
            S("logging","logging_inquiry",DemoEventKind.Interaction,"벌목장을 가로막은 과성장을 조사한다"),
            S("deep_forest","metal_growth_lesson",DemoEventKind.Interaction,"심부에서 금극목의 단서를 확인한다"),
            S("cheongryong","cheongryong",DemoEventKind.BossDefeated,"청룡을 격파한다"),
            S("guk_return","guk_high_reward",DemoEventKind.FieldUsed,"국으로 높은 장소를 다시 탐험한다"),
            S("escort","escort_start",DemoEventKind.Interaction,"왕소와 봉인 화물을 태우고 상경길로 출발한다"),
            S("checkpoint_one","checkpoint_1",DemoEventKind.Interaction,"첫 검문에서 화물을 보증한다"),
            S("checkpoint_two","checkpoint_2",DemoEventKind.Interaction,"황경 앞 검문을 통과한다"),
            S("delivery","cargo_delivery",DemoEventKind.CargoDelivered,"성저 객주 본점에 화물을 인도한다"),
            S("south_gate","south_gate_general",DemoEventKind.BossDefeated,"남문 장수를 격파한다"),
            S("ending","hwanggyeong_south_gate",DemoEventKind.GateOpened,"황경 남문이 열린다")
        };
        static string Tests()
        {
            int count=0;Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);count++;};
            var p=ScriptableObject.CreateInstance<DemoCampaignProfile>();p.Stages=CreateStages();
            try
            {
                check(p.IsValid,"profile");var state=new DemoCampaignState{CampaignId=p.CampaignId};
                check(!DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Interaction,"mine_inquiry",out _,out _),"evidence before commission");
                check(!DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Rest,"village_commission",out _,out _),"event type mismatch");
                check(DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Interaction,"village_commission",out var accepted,out _),"accept");
                check(state.Completed.Count==0,"proposal never mutates persisted state");state=accepted;
                check(!DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Interaction,"village_commission",out _,out _),"early report");
                check(DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Interaction,"mine_inquiry",out state,out _),"actual evidence");
                var disk=JsonUtility.FromJson<DemoCampaignState>(JsonUtility.ToJson(state));check(disk.IsValid()&&disk.Completed.Count==2,"reload");
                check(DemoCampaignProgression.TryAdvance(p,disk,DemoEventKind.Interaction,"village_commission",out state,out int reward)&&reward==60,"report reward");
                check(!DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Interaction,"village_commission",out _,out _),"duplicate report");
                check(DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Rest,"geumpyo_inn",out state,out _),"rest");
                check(!DemoCampaignProgression.TryAdvance(p,state,DemoEventKind.Interaction,"jeongdam_j1",out _,out _),"reserved stage cannot complete");
                for(int version=2;version<=3;version++)
                {
                    var old=WorldMacroProgress.CreateNew("old",Vector3.one,35);old.version=version;old.campaign=null;
                    old.ledger.currency=123;old.ledger.dropCurrency=12;old.ledger.completed.Add("preserved");
                    var migrated=WorldMacroProgress.MigrateToCurrent(old);
                    check(migrated!=null&&migrated.version==WorldMacroProgress.CurrentVersion&&migrated.ledger.currency==123&&
                        migrated.ledger.dropCurrency==12&&migrated.ledger.position==Vector3.one&&migrated.ledger.completed.Contains("preserved"),"old save migration "+version);
                }
                string report="{\"status\":\"PASS\",\"checks\":"+count+",\"scope\":\"campaign event progression and legacy save migration; not manual play\"}";
                File.WriteAllText(Output+"/campaign_tests.json",report);return report;
            }
            finally{UnityEngine.Object.DestroyImmediate(p);}
        }
        static string Audit()
        {
            var session=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session==null)throw new Exception("Missing session");
            var content=session.Content;
            bool isolated=session.gameObject.scene.path==Scene&&content!=null&&AssetDatabase.GetAssetPath(content)==Folder+"/Content.asset"&&content.SaveSlot=="demo-campaign-foundation-v1"&&content.Campaign!=null&&content.Campaign.IsValid;
            var ui=UnityEngine.Object.FindFirstObjectByType<Oheangbu.App.World.UI.PlaytestUiRoot>();
            bool routes=ui!=null&&ui.Content==content&&ui.TitleSceneName==Path.GetFileNameWithoutExtension(Title)&&ui.PlaySceneName==Path.GetFileNameWithoutExtension(Scene)&&
                new[]{Title,Scene}.All(path=>EditorBuildSettings.scenes.Any(s=>s.path==path&&s.enabled));
            bool map=ui!=null&&ui.MapData!=null&&AssetDatabase.GetAssetPath(ui.MapData)==Folder+"/Map.asset"&&ui.MapData.Markers.Any(m=>m.Id=="demo.village_office"&&
                Vector2.Distance(m.WorldXZ,new Vector2(content.Opening.Commission.Position.x,content.Opening.Commission.Position.z))<.01f);
            var available=content.Points.Select(p=>p.Id).Concat(new[]{content.Opening.Commission.Id}).ToArray();
            bool interactions=content.Campaign!=null&&content.Campaign.Stages.Where(s=>s.Implemented).All(s=>available.Contains(s.TriggerId));
            int missing=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            string report="{\"status\":\""+(isolated&&routes&&map&&interactions&&missing==0?"PASS":"FAIL")+"\",\"isolatedSceneAndSlot\":"+isolated.ToString().ToLowerInvariant()+",\"demoLobbyRoutes\":"+routes.ToString().ToLowerInvariant()+",\"officeMapMarker\":"+map.ToString().ToLowerInvariant()+",\"implementedInteractionIdsExist\":"+interactions.ToString().ToLowerInvariant()+",\"missingScripts\":"+missing+",\"implementedStages\":"+(content.Campaign!=null?content.Campaign.Stages.Count(s=>s.Implemented):0)+",\"reservedStages\":"+(content.Campaign!=null?content.Campaign.Stages.Count(s=>!s.Implemented):0)+"}";
            File.WriteAllText(Output+"/scene_audit.json",report);return report;
        }
    }
}
