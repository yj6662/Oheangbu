using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        const string Folder=WorldMacroCompactAuthoring.Folder+"/ActsTerrain";
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Demo/ActsTerrain"));
        static WorldMacroPlaytestSession Session=>Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsSortMode.None).Single(s=>s.gameObject.scene==SceneManager.GetActiveScene());
        [Serializable] sealed class Report {public string status,scope;public List<string> checks=new List<string>();public List<string> failures=new List<string>();}
        static void Require()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)
                throw new InvalidOperationException("Saved compact scene in Edit mode required");
        }
        static T Copy<T>(T source,string name)where T:ScriptableObject
        {
            string path=Folder+"/"+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<T>(path);
            if(asset!=null)return asset;
            if(source==null)throw new InvalidOperationException(name+" source missing");
            if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),path))throw new IOException(path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
        static string Write(string file,object value)
        {Directory.CreateDirectory(Output);string json=JsonUtility.ToJson(value,true);File.WriteAllText(Path.Combine(Output,file),json);return json;}
        public static string Execute(string command)
        {
            if(command=="play-checks")return WorldMacroActRuntimeChecks.Start();
            if(command=="play-status")return WorldMacroActRuntimeChecks.Status();
            Require();Directory.CreateDirectory(Output);
            if(command.StartsWith("capture:"))return CaptureTerrainView(command.Substring(8));
            if(command=="map-sync")return SyncActMap();
            if(command=="shortcut-check")return CheckShortcut();
            if(command=="shortcut-reuse")return ReuseShortcutSurface();
            if(command=="shutdown-saved")
            {
                AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveOpenScenes())throw new IOException("Scene save failed");
                double when=EditorApplication.timeSinceStartup+2;
                EditorApplication.CallbackFunction close=null;close=()=>{if(EditorApplication.timeSinceStartup<when)return;EditorApplication.update-=close;EditorApplication.Exit(0);};EditorApplication.update+=close;
                return "Saved compact candidate and assets; clean editor exit scheduled";
            }
            if(command=="release-memory"){EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();return "Released unreferenced Unity assets and managed garbage";}
            if(command.StartsWith("ground-local:"))
            {
                var values=command.Substring(13).Split(',').Select(v=>float.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();var p=new Vector3(values[0],values[1],values[2]);
                var hits=Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,8,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance);
                return "Safe="+Session.TrySafeFeet(p,out var safe)+" at "+safe+"\n"+string.Join("\n",hits.Select(h=>h.collider.name+" y="+h.point.y+" normal="+h.normal));
            }
            if(command=="prepare")return Prepare();
            if(command=="separate-npc")return SeparateNpc();
            if(command=="dress-checkpoints")return DressCheckpoints();
            if(command=="survey")return Survey();
            if(command=="repair-checkpoints")return RepairCheckpoints();
            if(command=="terrain-audit")return TerrainAudit();
            if(command=="repair-escort-staging")return RepairEscortStaging();
            if(command=="repair-cave-route")return RepairCaveRoute();
            if(command=="repair-cave-floor")return RepairCaveFloor();
            if(command=="repair-seam-ground")return WorldMacroCompactAuthoring.RepairActSeamGround();
            if(command=="repair-road-edge")return WorldMacroCompactAuthoring.RefineActRoadEdge();
            if(command=="shortcut")return Shortcut();
            if(command=="checks")return Checks();
            if(command=="nav-prepare")return NavigationPrepare();
            if(command=="nav-step")return NavigationStep();
            if(command=="nav-relay-obstacle")return NavigationRelayObstacle();
            if(command=="nav-restart")return NavigationRestart();
            if(command=="nav-repair-links")return RepairNavigationLinks();
            if(command=="nav-repair-wide")return RepairNavigationLinks(true);
            if(command=="nav-audit")return NavigationAudit();
            throw new ArgumentException("acts:prepare/survey/checks");
        }
        static string Prepare()
        {
            var session=Session;var content=session.Content;
            if(!AssetDatabase.GetAssetPath(content).StartsWith(WorldMacroCompactAuthoring.Folder+"/"))throw new InvalidOperationException("Content not isolated");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            content.Campaign=Copy(content.Campaign,"Campaign_TEST");content.Economy=Copy(content.Economy,"Economy_TEST");
            var campaign=content.Campaign;
            campaign.EnvironmentalGuidance=true;
            string[] titles={"액트 1 · 폐광의 흔적","액트 2 · 물든 숲","액트 3 · 봉인된 화물","액트 4 · 적로","액트 5 · 철옹","액트 6 · 현강","액트 7 · 황경 귀환","액트 8 · 잔월회","액트 9 · 현신"};
            string[] journey={"관청 → 폐광 조사 → 관청 보고 → 금표 주막","정담·왕소 → 벌목장 → 금극목 → 청룡","호송 → 검문 두 곳 → 객주 인도 → 남문 개방","적로·주작","철옹·백호","현강·현무","황경 귀환·몰락한 황룡","잔월회·사찰 공방","현신·인공 황룡·결말"};
            string[] rewards={"보고 100통보 · 첫 정비 선택","80+40+160통보 · ㄱ·국·仁 · 선택 재탐험 80","검문 60+60·인도220·장수120통보","ㄴ·눈·禮 · 덩굴 장벽 제거","ㅅ·숫·義 · 바위 통로 개방","ㅇ·웅·智 · 오염 정화","ㅁ·뭄·信 · 이동 확장","기존 능력 종합 활용","최종 대결·선택 결산"};
            campaign.Acts=Enumerable.Range(0,9).Select(i=>new DemoCampaignProfile.Act{Id="act_"+(i+1),Title=titles[i],Journey=journey[i],RewardSummary=rewards[i],Reserved=i>=3}).ToArray();
            var points=content.Points.ToList();if(content.Opening!=null)points.Add(content.Opening.Commission);
            string[] areas={"관청","폐광","관청","금표 주막","역참","객주 분소","벌목장","물든 숲","청룡의 터","높은 석대","상경 출발지","첫 검문","두 번째 검문","성저 객주","황경 남문","황경 남문"};
            string[] hints={
                "폐광의 폭파 소식을 확인하러 왔다. 마을 관청 마당의 아전에게 의뢰를 묻자.",
                "폭파 원인을 확인해야 한다. 산 아래 갱도 입구로 가서 안쪽의 부서진 암벽과 광석을 조사하자. 큰길에서는 G로 자동차를 부를 수 있다.",
                "확인한 물증을 보고해야 한다. 지나온 마을 관청으로 돌아가 마당의 아전에게 사실만 전하자.",
                "숲길 사정을 알아보자. 산길 끝 초가와 등불이 모인 금표 주막에서 쉬며 통보로 첫 정비를 선택할 수 있다.",
                "숲의 통행을 확인하려면 길을 아는 사람이 필요하다. 주막 너머 큰길의 역참에서 정담을 만난다.",
                "상경 화물 의뢰가 남았다. 역참에서 큰길을 따라 남쪽으로 내려가면 객주 분소의 왕소가 봉인 화물과 숲길 사정을 안다.",
                "화물을 옮기려면 숲의 위협부터 해결해야 한다. 잘린 나무와 쌓인 목재가 보이는 벌목장으로 가서 위협을 정리하고 흔적을 조사하자.",
                "베어도 자라는 덩굴을 끊을 방법을 확인하자. 벌목 흔적이 이어지는 심부에서 금 술식으로 생장을 끊어 보자.",
                "과성장의 중심을 잠재워야 한다. 심부 끝 청룡의 터에 들어가 청룡을 격파하자.",
                "국을 써 볼 높은 석대가 벌목장 쉼터 옆에 있다. 받침을 올려 보따리를 살펴볼 수 있다. 이 탐험은 상경에 필수가 아니다.",
                "이제 상경할 수 있다. 큰길 남쪽 객주 분소의 왕소에게 돌아가 화물과 함께 자동차에 탑승해 출발한다. 높은 석대는 나중에 돌아와도 된다.",
                "봉인을 유지한 채 황경으로 간다. 큰길의 첫 검문에서 왕소·화물과 함께 정차하고 검문관에게 보증하자.",
                "화물을 계속 호위하자. 가도와 교량을 따라 다음 검문까지 가서 정차하고 확인을 받자.",
                "검문을 마쳤다. 가까운 쉼터에서 왕소가 중개하는 정비를 이용할 수 있다. 황경 성저 객주 본점에 화물을 인도하자.",
                "호송을 마쳤으니 남문을 열 차례다. 성저에서 보이는 큰 성문 앞으로 가서 남문 장수와 싸우자.",
                "남문 장수가 쓰러졌다. 열린 황경 남문에서 이번 여정의 마무리를 확인하자."};
            for(int i=0;i<campaign.Stages.Length;i++)
            {
                var s=campaign.Stages[i];s.ActId=i<4?"act_1":i<10?"act_2":"act_3";
                s.Optional=s.Id=="guk_return";s.PrerequisiteIds=s.Optional?new[]{"cheongryong"}:Array.Empty<string>();
                s.DestinationId="area_"+s.Id;s.DestinationLabel=areas[i];s.DestinationRadius=i==8?70:45;s.TravelHint=hints[i];
                var point=points.FirstOrDefault(p=>p.Id==s.TriggerId);
                var actor=content.Encounters.FirstOrDefault(e=>e.Id==s.TriggerId||e.ContentId==s.TriggerId);
                s.Destination=point!=null?point.Position:actor!=null?actor.Feet:i==15?campaign.Stages[14].Destination:content.StartFeet;
                if(s.Id=="office_report")s.TongboReward=100;
                if(s.Id=="guk_return")s.TongboReward=80;
                if(s.Id=="checkpoint_one"||s.Id=="checkpoint_two")s.TongboReward=60;
                if(s.Id=="delivery")s.TongboReward=220;
                // Follow-up destination is stated at the previous successful dialogue, never as an ongoing HUD.
                if(s.Event==DemoEventKind.Interaction&&i+1<hints.Length&&!s.Dialogue.Contains(hints[i+1]))s.Dialogue+="\n\n"+hints[i+1];
                if(point!=null&&s.TongboReward>0&&point.Currency!=0)throw new InvalidOperationException("Duplicate reward owner: "+point.Id);
            }
            foreach(var cp in content.Checkpoints)if(cp.Id=="road_rest_2"){cp.Shop=true;cp.ShopRequiredStageId="checkpoint_two";cp.Label="상경길 쉼터 · 왕소의 간이 정비";}
            var rest=content.Points.FirstOrDefault(p=>p.Id=="road_rest_2");if(rest!=null)rest.Text="가도 옆 쉼터다. 왕소가 중개하는 간이 정비에서 받은 통보로 장비와 체력·먹을 보강할 수 있다.";
            var terrain=session.Traversal!=null?session.Traversal:session.gameObject.AddComponent<WorldTerrainQuery>();
            string rulesPath=Folder+"/Traversal_TEST.asset";var rules=AssetDatabase.LoadAssetAtPath<WorldTraversalTestProfile>(rulesPath);
            if(rules==null){rules=ScriptableObject.CreateInstance<WorldTraversalTestProfile>();AssetDatabase.CreateAsset(rules,rulesPath);}
            terrain.Rules=rules;
            var tris=new List<WorldTerrainQuery.WaterTriangle>();
            var water=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true))
                .Where(m=>m.name.StartsWith("Water_")&&m.transform.parent!=null&&m.transform.parent.name=="02_Drainage_WaterSurface").ToArray();
            if(water.Length!=6)throw new InvalidOperationException("Expected 6 actual authored water meshes, got "+water.Length);
            foreach(var m in water)
            {
                var vs=m.sharedMesh.vertices;var ts=m.sharedMesh.triangles;
                for(int j=0;j<ts.Length;j+=3)tris.Add(new WorldTerrainQuery.WaterTriangle{A=m.transform.TransformPoint(vs[ts[j]]),B=m.transform.TransformPoint(vs[ts[j+1]]),C=m.transform.TransformPoint(vs[ts[j+2]])});
            }
            terrain.Water=tris.ToArray();terrain.Reindex();session.Traversal=terrain;
            EditorUtility.SetDirty(campaign);EditorUtility.SetDirty(content);EditorUtility.SetDirty(terrain);EditorUtility.SetDirty(session);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);
            return Write("preparation.json",new Report{status="PREPARED_NOT_PLAY_VERIFIED",scope="Private compact campaign/economy, acts 1–9 outline, optional Guk and terrain rules. Existing activation flags preserved until route checks.",checks=new List<string>{"Mandatory rewards "+campaign.Stages.Where(s=>!s.Optional).Sum(s=>s.TongboReward),"Escort rewards 340","Water triangles "+tris.Count,"Shared source profiles unchanged"}});
        }
        [Serializable] sealed class Point {public string id;public Vector3 position;public string info;}
        [Serializable] sealed class SurveyReport {public string status="READ_ONLY";public Point[] points,water;}
        static string Survey()
        {
            var s=Session;var points=s.Content.Points.Select(p=>new Point{id=p.Id,position=p.Position,info=p.Kind+" currency="+p.Currency}).ToList();
            foreach(var cp in s.Content.Checkpoints)points.Add(new Point{id="checkpoint:"+cp.Id,position=cp.Feet,info="shop="+cp.Shop});
            var cave=GameObject.Find("Playtest_NaturalCave")?.transform;
            if(cave!=null)
            {
                var soil=cave.Find("Continuous_Approach_Soil")?.GetComponent<MeshCollider>();
                foreach(var n in new[]{new Vector3(-54,0,5),new Vector3(-43,0,5),new Vector3(-26,0,-1),new Vector3(-9,0,-15),new Vector3(0,0,-26),new Vector3(8,0,-38)})
                {
                    var p=cave.TransformPoint(n);var ray=new Ray(p+Vector3.up*10,Vector3.down);
                    bool found=soil!=null&&soil.Raycast(ray,out var h,40);if(found){soil.Raycast(ray,out var hit,40);p=hit.point;}
                    int index=Enumerable.Range(0,s.Content.MainPath.Length).OrderBy(i=>Vector3.Distance(s.Content.MainPath[i],p)).First();
                    points.Add(new Point{id="cave_node_"+n,position=p,info="soil="+found+" nearest MainPath="+index+" distance="+Vector3.Distance(s.Content.MainPath[index],p)});
                }
            }
            var guk=Object.FindFirstObjectByType<DemoGukRevisitSite>();if(guk!=null)foreach(var t in new[]{guk.LiftPad,guk.UpperSurface,guk.DescentExit})if(t!=null)points.Add(new Point{id=t.name,position=t.position});
            var meshes=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(m=>m.name.StartsWith("Water_")&&m.transform.parent?.name=="02_Drainage_WaterSurface");
            return Write("survey.json",new SurveyReport{points=points.ToArray(),water=meshes.Select(m=>new Point{id=m.name,position=m.GetComponent<Renderer>().bounds.center,info="mesh="+AssetDatabase.GetAssetPath(m.sharedMesh)+" vertices="+m.sharedMesh.vertexCount}).ToArray()});
        }
    }
}
