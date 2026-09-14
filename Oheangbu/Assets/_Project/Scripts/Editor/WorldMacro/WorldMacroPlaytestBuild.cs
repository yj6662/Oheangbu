using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlaytestAuthoring
    {
        static void Set(Object o,string field,object value)=>Prologue.PrologueBuilder.Set(o,field,value);
        static T SaveAsset<T>(T asset,string name)where T:Object
        {
            string path=Folder+"/"+name;var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old!=null){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);return old;}
            AssetDatabase.CreateAsset(asset,path);return asset;
        }
        static string Build()
        {
            if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Playtest edit scene required");
            if(Object.FindFirstObjectByType<WorldMacroPlaytestSession>()!=null)throw new Exception("Already built; use targeted repairs");
            if(!File.Exists(Output+"/route.json"))throw new Exception("Ground route must be authored first");
            DevSceneKit.EnsureFolder(Folder);var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var preview=Object.FindFirstObjectByType<WorldMacroContentPreview>();var sheet=Object.Instantiate(preview.Sheet);sheet.name="Playtest Content Positions";
            var points=preview.Points;preview.enabled=false;
            var review=Object.FindFirstObjectByType<WorldMacroReviewController>();var reviewCamera=review.GetComponent<Camera>();
            review.enabled=false;reviewCamera.enabled=false;reviewCamera.tag="Untagged";
            var audio=review.GetComponent<AudioListener>();if(audio!=null)audio.enabled=false;
            review.WalkBody.gameObject.SetActive(false);
            var root=new GameObject("WorldMacro_Playtest");
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var data=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();data.TestRules=AssetDatabase.LoadAssetAtPath<PrologueContentSO>(Prologue.PrologueBuilder.Folder+"/Content.asset");
            data.StartFeet=Ground(cave.TransformPoint(new Vector3(0,0,12)))+Vector3.up*.1f;data.StartYaw=cave.eulerAngles.y+180;
            var main=JsonUtility.FromJson<Points>(File.ReadAllText(Output+"/route.json")).points.ToList();
            // Turn only this scene's solid inn mass into a walkable shell, retaining roof and footprint.
            var inn=GameObject.Find("Inn").transform;var walls=inn.Find("Massing_Walls");
            Vector3 size=walls.localScale,centre=walls.position;float floor=centre.y-size.y*.5f;
            var material=walls.GetComponent<Renderer>().sharedMaterial;
            Object.DestroyImmediate(walls.gameObject);
            void Box(string name,Vector3 position,Vector3 scale){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(inn,true);g.transform.position=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;}
            Box("Playtest_Inn_Back",centre+Vector3.forward*(size.z*.5f-.15f),new Vector3(size.x,size.y,.3f));
            foreach(float side in new[]{-1f,1f}){
                Box("Playtest_Inn_Side",centre+Vector3.right*side*(size.x*.5f-.15f),new Vector3(.3f,size.y,size.z));
                Box("Playtest_Inn_DoorSide",centre+new Vector3(side*(size.x*.25f+.5f),0,-size.z*.5f+.15f),new Vector3(size.x*.5f-1,size.y,.3f));
            }
            Box("Playtest_Inn_Lintel",new Vector3(centre.x,floor+3.75f,centre.z-size.z*.5f+.15f),new Vector3(2,2.5f,.3f));
            Box("Playtest_Inn_Floor",new Vector3(centre.x,floor-.12f,centre.z),new Vector3(size.x,.24f,size.z));
            var door=new Vector3(centre.x,floor+.12f,centre.z-size.z*.5f-1.5f);
            data.InnCheckpointFeet=new Vector3(centre.x,floor+.12f,centre.z-1.5f);
            Physics.SyncTransforms();
            main.AddRange(FindPath(main.Last(),door).Skip(1));main.Add(data.InnCheckpointFeet);data.MainPath=main.ToArray();
            // A short optional spur beside the grounded connector; no required letter or main-route gate.
            var junction=main[Mathf.Min(65,main.Count-1)];Vector3 branchEnd=default;bool branchFound=false;
            foreach(var delta in new[]{Vector3.right*18,Vector3.left*18,Vector3.forward*18,Vector3.back*18})
                if(Surface(junction+delta,out branchEnd)&&Segment(junction,branchEnd,.5f)){branchFound=true;break;}
            if(!branchFound)throw new Exception("No clear optional spur near connector");
            data.BranchPath=new[]{junction,branchEnd};
            var entries=new List<PrologueContentSO.Point>();
            void Point(string id,PrologueInteractionKind kind,Vector3 position,string prompt,string text,int reward=0){
                entries.Add(new PrologueContentSO.Point{Id=id,Kind=kind,Position=position,Prompt=prompt,Text=text,Currency=reward});
                var point=points.FirstOrDefault(p=>p.Id==id);var entry=sheet.Entries.FirstOrDefault(e=>e.Id==id);
                if(point!=null)point.transform.position=position;
                if(entry!=null){entry.Position=position;entry.Text=text;}
            }
            Point("mine_inquiry",PrologueInteractionKind.Evidence,Ground(cave.TransformPoint(new Vector3(2,0,8)))+Vector3.up*.05f,"폭파 흔적 조사",data.TestRules.Points.First(p=>p.Id=="BlastEvidence").Text);
            Point("geumpyo_inn",PrologueInteractionKind.Rest,data.InnCheckpointFeet+Vector3.right*1.3f,"금표 주막에서 쉬기","잠시 숨을 고른다.");
            Point("logger",PrologueInteractionKind.Conversation,new Vector3(centre.x-3,floor+.05f,centre.z-1),"벌목꾼과 이야기",data.TestRules.Points.First(p=>p.Id=="Logger").Text);
            Point("herbalist",PrologueInteractionKind.Conversation,new Vector3(centre.x+3,floor+.05f,centre.z-1),"약초꾼과 이야기",data.TestRules.Points.First(p=>p.Id=="Herbalist").Text);
            var reward=data.TestRules.Points.First(p=>p.Id=="WorkerSatchel");Point("worker_satchel",PrologueInteractionKind.Currency,branchEnd,"작업자의 소지품 살피기",reward.Text,reward.Currency);
            var satchel=GameObject.CreatePrimitive(PrimitiveType.Cube);satchel.name="Playtest_WorkerSatchel";satchel.transform.SetParent(root.transform);satchel.transform.position=branchEnd+Vector3.up*.16f;satchel.transform.localScale=new Vector3(.5f,.3f,.4f);satchel.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(satchel.GetComponent<Collider>());
            data.Points=entries.ToArray();
            var spawnSpecs=new List<WorldMacroPlaytestSO.Encounter>();
            for(int i=0;i<3;i++){
                var foot=Ground(cave.TransformPoint(new Vector3(i==1?1.5f:0,0,i==0?-10:i==1?-29:-44)))+Vector3.up*.06f;
                spawnSpecs.Add(new WorldMacroPlaytestSO.Encounter{Id=i<2?"mine_beast/"+i:"mine_fire/0",ContentId=i<2?"mine_beast":"mine_fire",Feet=foot,Patrol=new[]{foot,foot+cave.right*1.5f},Ranged=i==2});
            }
            data.Encounters=spawnSpecs.ToArray();
            foreach(string id in new[]{"mine_beast","mine_fire"}){
                var point=points.First(p=>p.Id==id);point.CombatConnected=true;point.Visual.gameObject.SetActive(false);
                var spec=spawnSpecs.First(e=>e.ContentId==id);point.transform.position=spec.Feet;sheet.Entries.First(e=>e.Id==id).Position=spec.Feet;
            }
            sheet=SaveAsset(sheet,"ContentPositions.asset");preview.Sheet=sheet;
            data=SaveAsset(data,"Playtest.asset");
            var rig=DevSceneKit.InstantiateRig(scene,Vector3.zero,0);PrefabUtility.UnpackPrefabInstance(rig,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);rig.name="Macro_CombatPlayerRig";
            var motor=rig.GetComponentInChildren<PlayerMotor>();var player=motor.transform;player.SetPositionAndRotation(data.StartFeet,Quaternion.Euler(0,data.StartYaw,0));
            var body=player.GetComponent<CharacterController>();body.height=1.75f;body.radius=.28f;body.center=Vector3.up*.875f;body.skinWidth=.03f;body.stepOffset=.3f;body.slopeLimit=45;
            player.Find("CameraPivot").localPosition=Vector3.up*1.55f;
            var visual=player.Find("Body");visual.localPosition=Vector3.up*.875f;visual.localScale=new Vector3(.56f,.875f,.56f);
            var walker=root.AddComponent<WorldMacroCombatWalker>();walker.Motor=motor;walker.Body=body;walker.CameraRig=player.GetComponent<CameraRigController>();walker.Drawing=rig.GetComponentInChildren<DrawingInputController>();walker.ViewCamera=rig.GetComponentInChildren<Camera>();walker.Wiring=rig.GetComponentInChildren<CombatLoopWiring>();walker.Visuals=new[]{visual.GetComponent<Renderer>()};
            walker.ViewCamera.farClipPlane=reviewCamera.farClipPlane;walker.ViewCamera.nearClipPlane=.08f;walker.ViewCamera.clearFlags=reviewCamera.clearFlags;
            var look=Object.FindFirstObjectByType<WorldLookDriver>();if(look!=null)Set(look,"_skyCamera",walker.ViewCamera);
            foreach(var seat in Object.FindObjectsByType<WorldMacroPalanquinSeat>(FindObjectsSortMode.None)){seat.CombatWalker=walker;seat.ViewCamera=walker.ViewCamera;PrefabUtility.RecordPrefabInstancePropertyModifications(seat);EditorUtility.SetDirty(seat);}
            var session=root.AddComponent<WorldMacroPlaytestSession>();session.Content=data;session.Walker=walker;session.PreviewPoints=points;session.PreviewSheet=sheet;session.PreviewSheetBoundsMin=WorldMacroBuilder.Sheet.BoundsMin;session.PreviewSheetBoundsMax=WorldMacroBuilder.Sheet.BoundsMax;
            var config=AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);var actors=new List<PrologueEncounter>();
            foreach(var spec in data.Encounters){
                var enemy=DevSceneKit.CreateEnemy(spec.Id,spec.Feet+Vector3.up*.875f,config,Element.Fire,player,player.GetComponent<PlayerVitals>(),true);enemy.transform.SetParent(root.transform);enemy.layer=2;
                var mesh=GameObject.CreatePrimitive(PrimitiveType.Capsule);mesh.name="Enemy_TEMP_Visual";mesh.transform.SetParent(enemy.transform,false);mesh.transform.localScale=new Vector3(.56f,.875f,.56f);mesh.layer=2;Object.DestroyImmediate(mesh.GetComponent<Collider>());
                mesh.GetComponent<Renderer>().sharedMaterial=DevSceneKit.StoneMaterial();
                Object.DestroyImmediate(enemy.GetComponent<MeshRenderer>());Object.DestroyImmediate(enemy.GetComponent<MeshFilter>());var capsule=enemy.GetComponent<CapsuleCollider>();capsule.radius=.28f;capsule.height=1.75f;
                var ec=enemy.GetComponent<EnemyController>();Set(ec,"_renderer",mesh.GetComponent<Renderer>());Set(ec,"_attackMode",spec.Ranged?EnemyController.AttackMode.RangedOnly:EnemyController.AttackMode.MeleeOnly);Set(ec,"_environmentOcclusion",true);ec.AttackEnabled=false;
                var agent=enemy.AddComponent<NavMeshAgent>();agent.radius=.28f;agent.height=1.75f;agent.baseOffset=.875f;agent.angularSpeed=180;agent.acceleration=8;
                var actor=enemy.AddComponent<PrologueEncounter>();actor.Id=spec.Id;actor.Player=player;actor.PatrolPoints=spec.Patrol.Select(p=>p+Vector3.up*.875f).ToArray();actor.Ranged=spec.Ranged;actor.DetectionRange=spec.Detection;actor.Leash=spec.Leash;actor.Speed=spec.Speed;actors.Add(actor);
            }
            session.Actors=actors.ToArray();DevSceneKit.WireRigSeam(null,null,actors.Select(a=>a.GetComponent<EnemyVitals>()).ToArray());Set(walker.Wiring,"_environmentOcclusion",true);Set(rig.GetComponentInChildren<CombatLifetimeScope>(),"_macroPlaytest",session);
            var navHost=new GameObject("Playtest_Local_Navigation");navHost.transform.position=cave.position-cave.forward*38;var nav=navHost.AddComponent<NavMeshSurface>();
            nav.collectObjects=CollectObjects.Volume;nav.center=Vector3.zero;nav.size=new Vector3(140,60,160);nav.layerMask=1;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.overrideVoxelSize=true;nav.voxelSize=.18f;nav.overrideTileSize=true;nav.tileSize=128;
            nav.BuildNavMesh();if(nav.navMeshData==null)throw new Exception("Local navigation bake failed");nav.RemoveData();nav.navMeshData=SaveAsset(Object.Instantiate(nav.navMeshData),"Navigation.asset");nav.AddData();
            foreach(var actor in actors)if(!NavMesh.SamplePosition(actor.transform.position-Vector3.up*.875f,out var hit,2,NavMesh.AllAreas))throw new Exception("Enemy off NavMesh "+actor.Id);else actor.transform.position=hit.position+Vector3.up*.875f;
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            var buildScenes=EditorBuildSettings.scenes.Where(x=>x.path!=ScenePath).ToList();buildScenes.Add(new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=buildScenes.ToArray();
            RefineRoute();RepairBranch();
            File.WriteAllText(Output+"/connected_content.json",JsonUtility.ToJson(data,true));
            return "Playtest scene saved; 3 enemies, investigation, optional reward, 2 NPCs, inn rest; local navigation only.";
        }
        static string RefineRoute()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
            var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var main=s.Content.MainPath;var refined=new List<Vector3>{s.Content.StartFeet};
            for(int i=1;i<main.Length;i++){
                int count=Mathf.CeilToInt(Vector3.Distance(main[i-1],main[i])/.5f);
                for(int n=0;n<=count;n++){
                    Vector3 expected=Vector3.Lerp(main[i-1],main[i],n/(float)Mathf.Max(1,count));Vector3 feet;
                    if(i<=54)feet=Ground(expected)+Vector3.up*.12f;
                    else if(!Surface(expected,out feet)){
                        bool found=false;
                        foreach(float radius in new[]{.5f,1f,2f,4f}){
                            foreach(var delta in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
                                if(Surface(expected+delta*radius,out feet)&&Segment(refined.Last(),feet,.25f)){found=true;break;}
                            if(found)break;
                        }
                        if(!found)throw new Exception("Refine route obstruction segment="+i+" position="+expected+" hits="+string.Join(",",Physics.RaycastAll(new Vector3(expected.x,2200,expected.z),Vector3.down,4400,1).Select(h=>h.collider.name+":"+h.normal.y)));
                    }
                    refined.Add(feet);
                }
            }
            s.Content.MainPath=refined.ToArray();EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();
            File.WriteAllText(Output+"/connected_content.json",JsonUtility.ToJson(s.Content,true));return "Grounded route at <=0.5m stations: "+refined.Count;
        }
        static string RepairPresentation()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            foreach(var point in s.Content.Points){var entry=s.PreviewSheet.Entries.FirstOrDefault(e=>e.Id==point.Id);if(entry!=null){entry.Position=point.Position;entry.Text=point.Text;}}
            EditorUtility.SetDirty(s.PreviewSheet);
            foreach(var actor in s.Actors)foreach(var r in actor.GetComponentsInChildren<Renderer>())r.sharedMaterial=DevSceneKit.StoneMaterial();
            foreach(var seat in Object.FindObjectsByType<WorldMacroPalanquinSeat>(FindObjectsSortMode.None)){seat.CombatWalker=s.Walker;seat.ViewCamera=s.Walker.ViewCamera;PrefabUtility.RecordPrefabInstancePropertyModifications(seat);EditorUtility.SetDirty(seat);}
            var buildScenes=EditorBuildSettings.scenes.Where(x=>x.path!=ScenePath).ToList();buildScenes.Add(new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=buildScenes.ToArray();
            s.TestSaveSuffix="";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Enemy telegraph-compatible TEST material and clean user save suffix saved";
        }
        static string RepairBranch()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var main=s.Content.MainPath;var junction=main.First(p=>Vector3.Distance(p,s.Content.StartFeet)>120);
            Vector3 end=default;bool found=false;
            foreach(var offset in new[]{Vector3.right*18,Vector3.left*18,Vector3.forward*18,Vector3.back*18}){
                if(!Surface(junction+offset,out end)||main.Any(p=>Vector3.Distance(p,end)<12)||!Segment(junction,end,.25f))continue;found=true;break;
            }
            if(!found)throw new Exception("No distinct branch outside main route");
            s.Content.BranchPath=new[]{junction,end};s.Content.Points.First(p=>p.Id=="worker_satchel").Position=end;
            GameObject.Find("Playtest_WorkerSatchel").transform.position=end+Vector3.up*.16f;
            s.Content.TerrainRevision="macro-first-section-2";EditorUtility.SetDirty(s.Content);EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            File.WriteAllText(Output+"/connected_content.json",JsonUtility.ToJson(s.Content,true));return "Optional 18m spur moved at least 12m off main walking route: "+end;
        }
    }
}
