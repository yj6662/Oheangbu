using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Additive authoring in the dedicated demo scene; never runs the world terrain generator.
    public static class DemoChapterTwoAuthoring
    {
        public const string Folder="Assets/_Project/Art/Demo/Chapter2";
        public const string RootName="Demo_Chapter2_RelayAndLogging";
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Demo/Chapter2"));
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static readonly string[] LoggingIds={"demo_logging_01","demo_logging_02","demo_logging_03"};
        static Vector3 Ground(float x,float z)=>WorldMacroPlaytestAuthoring.Ground(new Vector3(x,2200,z),true)+Vector3.up*.06f;
        static bool OwnsActor(string id)=>id!=null&&(id.StartsWith("demo_forest_approach_",StringComparison.Ordinal)||id.StartsWith("demo_logging_",StringComparison.Ordinal));
        static Vector3 Station=>Ground(1908,586);
        static Vector3 Logging=>Ground(1970,1360);
        [Serializable] sealed class Check {public string name,status,detail;}
        [Serializable] sealed class Report {public string status,scope;public Check[] checks;public int actors,implementedStages;public string[] assetSources;public float innToRelayMetres,innToLoggingMetres;}
        static readonly string[] Sources={"Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab","Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab","Assets/HwaseongHaenggung/Prefabs/SM_M_WoodLog.prefab","Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice.prefab","Assets/KoreanTraditionalFestival/Prefabs/SM_JolongtaegiBag.prefab"};

        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if(command.StartsWith("view:"))return View(command.Substring(5));
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            if(command=="survey")return Survey();
            if(command=="apply")return Apply();
            if(command=="audit")return Audit();
            if(command=="tests")return Tests();
            if(command=="transactions")return SaveReport("interaction_tests.json",DemoInteractionTransactionChecks.Run());
            if(command=="enemy-profiles")return SaveReport("enemy_profile_tests.json",DemoEnemyProfileChecks.Run());
            if(command=="enemy-presentation")return SaveReport("enemy_presentation_tests.json",DemoEnemyPresentationChecks.Run());
            if(command=="checkpoints")return SaveReport("checkpoint_tests.json",DemoCheckpointFoundationChecks.Run());
            if(command.StartsWith("runtime:"))return DemoChapterTwoRuntimeChecks.Execute(command.Substring(8));
            throw new ArgumentException("chapter2: survey/apply/audit/tests/checkpoints/runtime:begin|poll|stop");
        }
        static void RequireEdit()
        {
            if(EditorApplication.isPlaying||Session==null||Session.gameObject.scene.path!=DemoFoundationAuthoring.Scene)
                throw new InvalidOperationException("Dedicated demo scene in Edit mode required.");
            if(AssetDatabase.GetAssetPath(Session.Content)!=DemoFoundationAuthoring.Folder+"/Content.asset")throw new InvalidOperationException("Demo content isolation lost.");
        }
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static string SaveReport(string name,string text){File.WriteAllText(Output+"/"+name,text);return text;}
        static string View(string name)
        {
            if(!EditorApplication.isPlaying||Session==null||!Session.TestSaveSuffix.StartsWith("_chapter2_",StringComparison.Ordinal))
                throw new InvalidOperationException("Use the isolated chapter-two Play diagnostic before staging review views.");
            var ui=PlaytestUiRoot.Instance;ui.CloseMenu();
            Vector3 probe,target;
            if(name=="relay"){probe=Ground(1908,561);target=Ground(1908,584);}
            else if(name=="logging"){probe=Ground(1971,1337);target=Logging;}
            else if(name=="forest"){probe=RouteAt(.31f);target=RouteAt(.40f);}
            else throw new ArgumentException("relay/logging/forest");
            if(!Session.TrySafeFeet(probe,out var feet))throw new InvalidOperationException("View feet obstructed: "+probe);
            Vector3 direction=target-feet;direction.y=0;Session.Teleport(feet,Quaternion.LookRotation(direction).eulerAngles.y);Session.Cull();
            var window=(EditorWindow)EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));window.Focus();window.Repaint();
            return "Diagnostic teleport for actual Game View still: "+name+" at "+feet+"; not a manual walkthrough.";
        }
        static string Capture(string name)
        {
            if(!EditorApplication.isPlaying||Screen.width!=1920||Screen.height!=1080)throw new InvalidOperationException("Active 1920x1080 Game View required");
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[a-z0-9_-]+$"))throw new ArgumentException("Invalid name");
            float commit=Oheangbu.EditorTools.Prologue.PrologueAudit.CommitRatio();
            if(commit>=.85f)return SaveReport("capture_status.json","{\"status\":\"UNVERIFIED\",\"reason\":\"System commit >=85%; no new capture\"}");
            string path=Output+"/"+name+"_1920x1080.png";if(File.Exists(path))throw new IOException("Existing capture preserved");
            ScreenCapture.CaptureScreenshot(path);
            return SaveReport(name+"_capture.json","{\"status\":\"REQUESTED\",\"width\":1920,\"height\":1080,\"commitRatio\":"+commit.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"source\":\"Actual Game View; diagnostic positioning. Requires image inspection.\"}");
        }
        static void Backup()
        {
            string folder=Output+"/Backups/before-authoring";Directory.CreateDirectory(folder);
            foreach(string asset in new[]{DemoFoundationAuthoring.Scene,AssetDatabase.GetAssetPath(Session.Content),AssetDatabase.GetAssetPath(Session.Content.Campaign),DemoFoundationAuthoring.Folder+"/Map.asset"})
            {
                string target=folder+"/"+Path.GetFileName(asset);
                if(!File.Exists(target))File.Copy(asset,target);
            }
        }
        static Transform Child(Transform parent,string name)
        {
            var child=parent.Find(name);if(child!=null)return child;
            child=new GameObject(name).transform;child.SetParent(parent,false);return child;
        }
        static void Set(Object target,string field,Object value)
        {var so=new SerializedObject(target);var p=so.FindProperty(field);if(p==null)throw new InvalidOperationException(field);p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        static float RouteLength(string id)
        {var p=WorldMacroBuilder.Sheet.Routes.First(r=>r.Id==id).Points;float d=0;for(int i=1;i<p.Length;i++)d+=Vector3.Distance(p[i-1],p[i]);return d;}
        static Vector3 RouteAt(float fraction)
        {
            var p=WorldMacroBuilder.Sheet.Routes.First(r=>r.Id=="Trail_Inn_Logging").Points;
            float target=RouteLength("Trail_Inn_Logging")*fraction;
            for(int i=1;i<p.Length;i++){float d=Vector3.Distance(p[i-1],p[i]);if(target<=d){var q=Vector3.Lerp(p[i-1],p[i],target/Mathf.Max(.001f,d));return Ground(q.x,q.z);}target-=d;}
            return Ground(p.Last().x,p.Last().z);
        }
        static string Survey()
        {
            RequireEdit();Physics.SyncTransforms();
            var points=new[]{Station,Ground(1901,578),Ground(1918,578),RouteAt(.4f),Logging,Ground(1977,1327)};
            var checks=points.Select(p=>new Check{name="ground",status="PASS",detail=p.ToString("F3")}).ToArray();
            return SaveReport("survey.json",JsonUtility.ToJson(new Report{status="PASS",scope="Existing terrain raycasts and route data; not a walkthrough",checks=checks,
                assetSources=Sources,innToRelayMetres=RouteLength("Road_Inn_Post"),innToLoggingMetres=RouteLength("Trail_Inn_Logging")},true));
        }
        static string Apply()
        {
            RequireEdit();Backup();EnsureFolder(Folder);Physics.SyncTransforms();
            var existing=GameObject.Find(RootName);var root=existing!=null?existing.transform:new GameObject(RootName).transform;
            var s=Session;var content=s.Content;
            var npcs=Child(root,"NPCs");var props=Child(root,"OwnedStructures");var combat=Child(root,"Encounters");
            var massing=GameObject.Find("WorldMacro_AuthoredGeography")?.transform.Find("03_SettlementAndLandmark_Massing/PostStation");if(massing!=null)massing.gameObject.SetActive(false);
            var station=WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Relay_Hanok",Sources[0],Station,new Vector3(13,0,12),0,false);
            station.rotation=Quaternion.Euler(0,90,0);
            // Use mesh colliders for the open porch and doors, never a building-wide solid box.
            foreach(var filter in station.GetComponentsInChildren<MeshFilter>())if(filter.GetComponent<MeshCollider>()==null){var c=filter.gameObject.AddComponent<MeshCollider>();c.sharedMesh=filter.sharedMesh;}
            CloneTable(props,"Demo_Merchant_Desk",Ground(1918,580),0);
            WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_SealedCargo",Sources[1],Ground(1921,582),new Vector3(1.1f,1.2f,1),8,true);
            WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Relay_Supplies",Sources[3],Ground(1921,580),new Vector3(1.1f,1.2f,1),-15,true);
            Npc(npcs,"jeongdam_j1",Ground(1901,578),175);
            Npc(npcs,"wangso_w1",Ground(1918,578),170);
            // The old global merchant is still a future site; only its demo preview actor is suppressed.
            foreach(var old in s.PreviewPoints.Where(p=>p!=null&&(p.Id=="jeongdam_j1"||p.Id=="wangso_w1")))
                if(!old.transform.IsChildOf(root)&&old.Visual!=null)old.Visual.gameObject.SetActive(false);
            CloneTable(props,"Demo_Logging_RestTable",Ground(1977,1329),15);
            var loggingMassing=GameObject.Find("WorldMacro_AuthoredGeography")?.transform.Find("03_SettlementAndLandmark_Massing/Logging");
            if(loggingMassing!=null)loggingMassing.gameObject.SetActive(false);
            foreach(var preview in s.PreviewPoints.Where(p=>p!=null&&p.Id=="logging_dungeon"))if(preview.Visual!=null)preview.Visual.gameObject.SetActive(false);
            WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Logging_OwnedStorehouse",
                "Assets/_Project/Art/World/WorldMacro/Playtest/VisualCorridor/HouseLOD/House2_LOD1.fbx",Ground(1992,1374),new Vector3(10,0,8),185,true);
            WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Logging_Bundle",Sources[4],Ground(1979,1329),new Vector3(.7f,.6f,.6f),0,false);
            for(int i=0;i<3;i++)WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Logging_CutWood_"+i,Sources[2],Ground(1985+i*.6f,1363+i*.4f),new Vector3(1.2f,1,3.5f),35+i*7,true);
            WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Logging_EvidenceBox",Sources[1],Ground(1974,1365),new Vector3(.7f,.65f,.7f),20,true);
            WorldMacroVisualCorridorAuthoring.PlaceSource(props,"Demo_Logging_CacheBag",Sources[4],Ground(1946,1390),new Vector3(.7f,.6f,.6f),0,false);
            AddPoint(content,"jeongdam_j1",PrologueInteractionKind.Conversation,Ground(1901,578),"정담과 이야기","정담은 숲의 사정을 객주에게도 확인해 보라고 권한다.");
            AddPoint(content,"wangso_w1",PrologueInteractionKind.Conversation,Ground(1918,578),"왕소의 운송 의뢰 확인","봉인을 열지 않고 황경 본점까지 옮겨 달라는 의뢰다. 숲길을 확인한 뒤 출발을 상의하자.");
            AddPoint(content,"logging_inquiry",PrologueInteractionKind.Evidence,Ground(1972,1364),"벌목장 과성장 조사","잘린 나무를 덩굴이 다시 감싸고 있다. 금속 도구가 남은 자리만 덜 뒤덮였다.");
            AddPoint(content,"logging_rest",PrologueInteractionKind.Rest,Ground(1977,1327),"벌목장 쉼터에서 휴식","벌목꾼이 쉬던 자리에 잠시 머문다.");
            AddPoint(content,"demo_logging_cache",PrologueInteractionKind.Currency,Ground(1946,1390),"남겨진 품삯 회수","떠난 인부의 짐에서 조선통보를 찾았다.",40);
            var checkpoints=(content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).Where(p=>p.Id!="logging_rest").ToList();
            checkpoints.Add(new WorldMacroPlaytestSO.CheckpointSpec{Id="logging_rest",Label="벌목장 쉼터",Feet=Ground(1977,1325),Yaw=0,Shop=true});content.Checkpoints=checkpoints.ToArray();
            ConfigureCampaign(content.Campaign);
            var seed=s.Actors.First(a=>a!=null);var actors=s.Actors.Where(a=>a!=null&&!OwnsActor(a.Id)).ToList();
            var specs=content.Encounters.Where(e=>!OwnsActor(e.Id)).ToList();
            var groups=new[]{RouteAt(.4f),Logging,Ground(1940,1385)};
            var groupNames=new[]{"forest_approach","logging","logging_branch"};
            for(int group=0;group<3;group++)
            {
                var centre=groups[group];if(group<2)BuildNavigation(root,groupNames[group],centre);
                for(int i=0;i<3;i++)
                {
                    string id="demo_"+groupNames[group]+"_"+(i+1).ToString("00");
                    var archetype=i==0?EnemyArchetype.NeutralMelee:i==1?EnemyArchetype.FireRanged:EnemyArchetype.WoodVine;
                    var profile=Profile(archetype);
                    var guess=Ground(centre.x+(i-1)*5,centre.z+(i==1?4:-4));
                    if(!NavMesh.SamplePosition(guess,out var hit,4,NavMesh.AllAreas))throw new InvalidOperationException("No navigation at "+id);
                    Vector3 feet=hit.position;
                    var old=combat.Find(id);var actor=old!=null?old.GetComponent<PrologueEncounter>():Object.Instantiate(seed,combat);
                    actor.name=id;actor.Id=id;actor.Session=null;actor.Player=s.Walker.Body.transform;actor.transform.position=feet+Vector3.up*.875f;
                    actor.Ranged=archetype!=EnemyArchetype.NeutralMelee;actor.DetectionRange=17;actor.Leash=25;actor.Speed=profile.MovementSpeedHint;actor.PreferredDistance=profile.PreferredDistanceHint;
                    var patrolGuess=Ground(feet.x+1.5f,feet.z+1);
                    if(!NavMesh.SamplePosition(patrolGuess,out var end,3,NavMesh.AllAreas))end=hit;
                    actor.PatrolPoints=new[]{feet+Vector3.up*.875f,end.position+Vector3.up*.875f};
                    var ec=actor.GetComponent<EnemyController>();ec.Configure(profile);ec.AttackEnabled=false;
                    if(archetype==EnemyArchetype.WoodVine)
                    {
                        var fx=actor.GetComponent<Oheangbu.App.Demo.EnemyAttackPresentation>();if(fx==null)fx=actor.gameObject.AddComponent<Oheangbu.App.Demo.EnemyAttackPresentation>();
                        fx.Configure(ec,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/AreaRift/Cast_고.prefab"),
                            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/AreaFive/Body_고.prefab"),
                            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/AreaFive/AreaContact_0.prefab"));
                        if(!fx.HasAllSources)throw new InvalidOperationException("Existing wood attack VFX sources missing");
                        EditorUtility.SetDirty(fx);
                    }
                    var agent=actor.GetComponent<NavMeshAgent>();agent.enabled=false;agent.speed=actor.Speed;agent.baseOffset=.875f;
                    actors.Add(actor);specs.Add(new WorldMacroPlaytestSO.Encounter{Id=id,ContentId=groupNames[group],Feet=feet,Patrol=new[]{feet,end.position},
                        Ranged=actor.Ranged,Speed=actor.Speed,Detection=17,Leash=25,Activation=140,RespawnOnRest=true});
                    EditorUtility.SetDirty(actor);EditorUtility.SetDirty(ec);
                }
            }
            s.Actors=actors.ToArray();content.Encounters=specs.ToArray();
            var wiring=new SerializedObject(s.Walker.Wiring);var targets=wiring.FindProperty("_enemies");targets.arraySize=actors.Count;
            for(int i=0;i<actors.Count;i++)targets.GetArrayElementAtIndex(i).objectReferenceValue=actors[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
            foreach(var actor in actors)actor.GetComponent<NavMeshAgent>().enabled=false;
            s.PreviewPoints=s.PreviewPoints.Where(p=>p!=null).Concat(npcs.GetComponentsInChildren<WorldMacroContentPoint>()).Distinct().ToArray();
            UpdateMap(content);ProtectDressing(station);
            EditorUtility.SetDirty(content.Campaign);EditorUtility.SetDirty(content);EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(s.gameObject.scene);EditorSceneManager.SaveScene(s.gameObject.scene);
            return Audit();
        }
        static void CloneTable(Transform parent,string id,Vector3 position,float yaw)
        {
            var existing=parent.Find(id);if(existing!=null)return;
            var source=GameObject.Find("Playtest_Village_Office")?.transform.Find("Clerk_WorkTable");
            if(source==null)source=GameObject.Find("Playtest_OwnedAssets")?.transform.Find("Geumpyo_ThatchedInn/Rest_Low_Table");
            if(source==null)throw new InvalidOperationException("Reviewed owned table missing.");
            var table=Object.Instantiate(source,parent);table.name=id;table.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        }
        static void Npc(Transform parent,string id,Vector3 feet,float yaw)
        {
            var node=Child(parent,id);node.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw,0));
            var point=node.GetComponent<WorldMacroContentPoint>();if(point==null)point=node.gameObject.AddComponent<WorldMacroContentPoint>();point.Id=id;point.CombatConnected=true;
            if(point.Visual==null)
            {
                var source=Session.PreviewPoints.FirstOrDefault(p=>p!=null&&p.Id=="jeongdam_j1"&&p.Visual!=null);
                var capsule=source?.Visual.Find("NPC_Proxy")??source?.Visual;
                if(capsule==null)throw new InvalidOperationException("Existing NPC proxy required.");
                var visual=Object.Instantiate(capsule,node);visual.name="Replaceable_NPC_Visual";visual.localPosition=Vector3.up*.85f;visual.localRotation=Quaternion.identity;visual.gameObject.SetActive(true);
                foreach(var collider in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);point.Visual=visual;
            }
            var body=node.GetComponent<CapsuleCollider>();if(body==null)body=node.gameObject.AddComponent<CapsuleCollider>();body.height=1.7f;body.radius=.27f;body.center=Vector3.up*.85f;
        }
        static void AddPoint(WorldMacroPlaytestSO content,string id,PrologueInteractionKind kind,Vector3 pos,string prompt,string text,int currency=0)
        {
            content.Points=content.Points.Where(p=>p.Id!=id).Concat(new[]{new PrologueContentSO.Point{Id=id,Kind=kind,Position=pos,Prompt=prompt,Text=text,Currency=currency,Radius=3}}).ToArray();
        }
        static void ConfigureCampaign(DemoCampaignProfile campaign)
        {
            void Stage(string id,string objective,string prompt,string dialogue,int reward=0)
            {var s=campaign.Stages.First(p=>p.Id==id);s.Implemented=true;s.Objective=objective;s.Prompt=prompt;s.Dialogue=dialogue;s.TongboReward=reward;}
            Stage("relay","금표 주막 남쪽 역참에서 정담을 만난다 [F]","정담에게 숲길 소식 묻기","오랜만이군. 폐광을 다녀왔다지? 함께 임무를 뛰던 때와는 사정이 많이 달라졌어.\n\n벌목장 쪽에서 인부들이 내려왔네. 나무를 베어도 덩굴이 금세 덮친다더군. 여기 왕소가 짐을 맡길 사람을 찾고 있으니, 길 사정도 함께 들어 보게.");
            Stage("cargo_contract","역참 옆 객주 접수처에서 왕소의 운송 의뢰를 확인한다 [F]","왕소의 봉인 화물 의뢰 확인","봉인을 건드리지 않고 황경 본점까지 옮겨 주실 분을 찾고 있어요. 지금은 숲길이 막혀 출발할 수 없답니다.\n\n먼저 북쪽 벌목장을 살펴봐 주시겠어요? 화물은 이곳에서 보관할게요. 숲길을 확인한 뒤 출발을 상의하지요.");
            Stage("logging","주막에서 북쪽 숲길을 따라 벌목장으로 가서 위협을 정리하고 조사한다 [F]","벌목장 과성장 조사","나무를 잘라 쌓은 자리까지 새 덩굴이 파고들었다. 쇠붙이가 놓인 자리만 덜 덮여 있다. 단순히 나무가 빨리 자라는 일은 아닌 듯하다.\n\n벌목꾼의 흔적은 더 깊은 숲으로 이어진다. 금 속성 술식이 이 덩굴을 끊는 데 도움이 될 것이다.",80);
            campaign.Stages.First(p=>p.Id=="logging").RequiredDefeatedIds=LoggingIds.ToArray();
            campaign.WorkInProgressText="벌목장의 위협과 과성장을 확인했다. 숲 심부 이후 구간은 제작 중이다.";
        }
        static EnemyAttackProfileSO Profile(EnemyArchetype type)
        {
            string path=Folder+"/"+type+".asset";var profile=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path);
            if(profile==null){profile=ScriptableObject.CreateInstance<EnemyAttackProfileSO>();profile.ApplyDefaults(type);AssetDatabase.CreateAsset(profile,path);}return profile;
        }
        static void BuildNavigation(Transform parent,string id,Vector3 centre)
        {
            var go=Child(parent,"Navigation_"+id);var surface=go.GetComponent<NavMeshSurface>();if(surface==null)surface=go.gameObject.AddComponent<NavMeshSurface>();
            go.position=centre;surface.collectObjects=CollectObjects.Volume;surface.center=Vector3.zero;surface.size=id=="logging"?new Vector3(128,80,128):new Vector3(88,70,88);
            surface.layerMask=1;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.overrideVoxelSize=true;surface.voxelSize=.18f;surface.overrideTileSize=true;surface.tileSize=128;
            string path=Folder+"/Navigation_"+id+".asset";var data=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            // Local buildings/clearings can change between passes. Re-bake just this authored volume.
            surface.RemoveData();surface.navMeshData=null;
            Physics.SyncTransforms();surface.BuildNavMesh();if(surface.navMeshData==null)throw new InvalidOperationException("Local navigation failed: "+id);
            var generated=surface.navMeshData;surface.RemoveData();
            if(data==null){data=Object.Instantiate(generated);AssetDatabase.CreateAsset(data,path);}
            else{EditorUtility.CopySerialized(generated,data);EditorUtility.SetDirty(data);}
            if(!AssetDatabase.Contains(generated))Object.DestroyImmediate(generated);
            surface.RemoveData();surface.navMeshData=data;surface.AddData();EditorUtility.SetDirty(surface);
        }
        static void UpdateMap(WorldMacroPlaytestSO content)
        {
            var ui=Object.FindFirstObjectByType<PlaytestUiRoot>();if(ui?.MapData==null)throw new InvalidOperationException("Demo map missing");
            foreach(string id in new[]{"jeongdam_j1","wangso_w1","logging_rest"})
            {
                var point=content.Points.First(p=>p.Id==id);
                ui.MapData.Markers=ui.MapData.Markers.Where(m=>m.Id!=id).Concat(new[]{new WorldMapMarkerSpec{Id=id,Label=id=="jeongdam_j1"?"길목 역참":id=="wangso_w1"?"객주 접수처":"벌목장 쉼터",Kind=WorldMapMarkerKind.Settlement,WorldXZ=new Vector2(point.Position.x,point.Position.z),InitiallyDiscovered=false}}).ToArray();
            }
            EditorUtility.SetDirty(ui.MapData);
        }
        static void ProtectDressing(Transform station)
        {
            var renderer=Object.FindFirstObjectByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>();if(renderer?.Sheet==null)return;
            string path=Folder+"/Dressing.asset";var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(path);
            if(sheet==null){sheet=Object.Instantiate(renderer.Sheet);AssetDatabase.CreateAsset(sheet,path);}
            var areas=sheet.PreservedAreas.Where(a=>!a.Id.StartsWith("demo_ch2_")).ToList();
            areas.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="demo_ch2_relay",Centre=Ground(1910,582),HalfSize=new Vector2(16,11),ExcludeProcedural=true});
            areas.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="demo_ch2_logging_rest",Centre=Ground(1977,1328),HalfSize=new Vector2(4,4),ExcludeProcedural=true});
            areas.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="demo_ch2_logging_yard",Centre=Ground(1970,1360),HalfSize=new Vector2(12,14),ExcludeProcedural=true});
            areas.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="demo_ch2_logging_store",Centre=Ground(1992,1374),HalfSize=new Vector2(7,6),ExcludeProcedural=true});
            sheet.PreservedAreas=areas.ToArray();renderer.Sheet=sheet;EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(renderer);
        }
        static string Audit()
        {
            RequireEdit();Physics.SyncTransforms();var s=Session;var checks=new List<Check>();
            void C(bool ok,string name,string detail=""){checks.Add(new Check{name=name,status=ok?"PASS":"FAIL",detail=detail});}
            C(s.Content.Campaign.IsValid,"campaign schema");
            C(s.Content.Campaign.Stages.Count(p=>p.Implemented)>=7,"first seven stages authored");
            C(s.Content.Points.Select(p=>p.Id).Distinct().Count()==s.Content.Points.Length,"unique interaction IDs");
            C(s.Actors.Select(a=>a.Id).Distinct().Count()==s.Actors.Length,"unique actor IDs");
            C(LoggingIds.All(id=>s.Actors.Any(a=>a.Id==id)),"logging gate has real actors");
            C(s.Content.Checkpoints.Any(p=>p.Id=="logging_rest"&&p.Shop),"logging rest and shop data");
            C(s.Content.Encounters.Count(e=>OwnsActor(e.Id))==9,"nine chapter encounters in three groups");
            foreach(var actor in s.Actors.Where(a=>OwnsActor(a.Id)))
            {
                var agent=actor.GetComponent<NavMeshAgent>();var profile=actor.GetComponent<EnemyController>().AttackProfile;
                C(!agent.enabled,"saved agent startup order "+actor.Id);
                C(profile!=null&&profile.TryValidate(out _),"valid attack profile "+actor.Id);
                C(NavMesh.SamplePosition(actor.transform.position-Vector3.up*agent.baseOffset,out _,2,NavMesh.AllAreas),"NavMesh spawn "+actor.Id);
                var path=new NavMeshPath();bool complete=NavMesh.CalculatePath(actor.PatrolPoints[0]-Vector3.up*.875f,actor.PatrolPoints.Last()-Vector3.up*.875f,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                C(complete,"patrol path "+actor.Id);
            }
            foreach(string id in new[]{"jeongdam_j1","wangso_w1","logging_inquiry","logging_rest","demo_logging_cache"})
            {
                var p=s.Content.Points.FirstOrDefault(q=>q.Id==id);C(p!=null,"interaction "+id);
                if(p!=null)C(Mathf.Abs(Ground(p.Position.x,p.Position.z).y-p.Position.y)<.1f,"terrain contact "+id);
            }
            var report=new Report{status=checks.All(c=>c.status=="PASS")?"PASS":"FAIL",scope="Edit scene and path checks. Temporary actors; actual input, combat balance, imagery and performance unverified.",checks=checks.ToArray(),actors=s.Actors.Length,implementedStages=s.Content.Campaign.Stages.Count(p=>p.Implemented),assetSources=Sources,innToRelayMetres=RouteLength("Road_Inn_Post"),innToLoggingMetres=RouteLength("Trail_Inn_Logging")};
            return SaveReport("scene_audit.json",JsonUtility.ToJson(report,true));
        }
        static string Tests()
        {
            var profile=ScriptableObject.CreateInstance<DemoCampaignProfile>();profile.Stages=DemoFoundationAuthoring.CreateStages();ConfigureCampaign(profile);
            var checks=new List<Check>();void C(bool ok,string n)=>checks.Add(new Check{name=n,status=ok?"PASS":"FAIL"});
            try
            {
                var state=new DemoCampaignState{CampaignId=profile.CampaignId,Completed=new List<string>{"commission","mine_evidence","office_report","inn_rest","relay","cargo_contract"}};
                C(!DemoCampaignProgression.TryAdvance(profile,state,DemoEventKind.Interaction,"logging_inquiry",out _,out _,Array.Empty<string>()),"live threats prevent investigation completion");
                C(!DemoCampaignProgression.TryAdvance(profile,state,DemoEventKind.Interaction,"logging_inquiry",out _,out _,LoggingIds.Take(2).ToArray()),"partial clear prevents completion");
                C(DemoCampaignProgression.TryAdvance(profile,state,DemoEventKind.Interaction,"logging_inquiry",out var next,out int reward,LoggingIds)&&reward==80,"complete group advances with reward");
                C(state.Completed.Count==6,"proposal does not mutate source");
                C(!DemoCampaignProgression.TryAdvance(profile,next,DemoEventKind.Interaction,"logging_inquiry",out _,out _,LoggingIds),"no duplicate investigation reward");
                return SaveReport("campaign_tests.json",JsonUtility.ToJson(new Report{status=checks.All(c=>c.status=="PASS")?"PASS":"FAIL",scope="Pure campaign clear gate checks",checks=checks.ToArray()},true));
            }
            finally{Object.DestroyImmediate(profile);}
        }
    }
}
