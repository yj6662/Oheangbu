using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static partial class PineRestGameBuilder
    {
        static string EscortFollowAuthor(bool resume=false)
        {
            var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty&&!resume)throw new Exception("Saved Journey required");
            var s=Object.FindFirstObjectByType<PrologueSession>();string profilePath=Folder+"/EscortFollow.asset";
            var profile=AssetDatabase.LoadAssetAtPath<JourneyEscortFollowProfileSO>(profilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<JourneyEscortFollowProfileSO>();AssetDatabase.CreateAsset(profile,profilePath);}s.EscortFollowProfile=profile;
            string controllerPath=Folder+"/WangsoLocomotion.controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if(controller==null)
            {
                var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/IdleStill.anim");
                var walk=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/WalkForward.anim");
                if(idle==null||walk==null)throw new Exception("Existing humanoid motion missing");
                controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);controller.AddParameter("JourneyWalking",AnimatorControllerParameterType.Bool);
                var machine=controller.layers[0].stateMachine;var still=machine.AddState("Wait");still.motion=idle;var moving=machine.AddState("Carry walk");moving.motion=walk;machine.defaultState=still;
                var go=still.AddTransition(moving);go.hasExitTime=false;go.duration=.15f;go.AddCondition(AnimatorConditionMode.If,0,"JourneyWalking");
                var stop=moving.AddTransition(still);stop.hasExitTime=false;stop.duration=.15f;stop.AddCondition(AnimatorConditionMode.IfNot,0,"JourneyWalking");
            }
            var animator=s.EscortCompanion.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
            var v=s.JourneySeat.Vehicle;var obstacle=v.GetComponent<NavMeshObstacle>();if(obstacle==null)obstacle=v.gameObject.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=v.Profile.HullCentre;var cargoBounds=s.EscortCargo.GetComponent<BoxCollider>().bounds;float clearance=Mathf.Max(s.EscortFollowProfile.ClearanceRadius,Mathf.Max(cargoBounds.extents.x,cargoBounds.extents.z));obstacle.size=v.Profile.HullSize+new Vector3(clearance*2,0,clearance*2);obstacle.carving=true;obstacle.carveOnlyStationary=true;obstacle.carvingTimeToStationary=.5f;
            EditorUtility.SetDirty(s);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Owned escort walk profile, humanoid locomotion and parked-vehicle navigation obstacle connected";
        }
        const string FollowReport="../Art/World/PineRest/escort_follow_checks.txt";
        static readonly List<string> followChecks=new List<string>();
        static int followStep;static double followAt;static Vector3 followStart,followCargoStart,followPausePosition;static float followMetres,followCargoTravel;static Vector3 followLastCargo;
        static GameObject followWall;
        static string EscortFollowCheck()
        {
            var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-")||s.Progress.escort.Stage!=DemoEscortStage.Escorting||s.JourneySeated)throw new Exception("Isolated active escort on foot required");
            followChecks.Clear();followStep=0;followAt=EditorApplication.timeSinceStartup;EditorApplication.update+=FollowTick;File.WriteAllText(FollowReport,"RUNNING");return "Escort following and cargo clearance check started";
        }
        static void FollowTick()
        {
            if(followStep==1&&EditorApplication.isPlaying){var live=Object.FindFirstObjectByType<PrologueSession>();if(live!=null&&live.EscortCargo!=null){followCargoTravel+=Vector3.Distance(followLastCargo,live.EscortCargo.position);followLastCargo=live.EscortCargo.position;}}
            if(EditorApplication.timeSinceStartup<followAt)return;
            try
            {
                if(!EditorApplication.isPlaying)throw new Exception("Play stopped");var s=Object.FindFirstObjectByType<PrologueSession>();var npc=s.EscortCompanion;
                void Check(bool pass,string label)=>followChecks.Add((pass?"PASS ":"FAIL ")+label);
                void PlayerAt(Vector3 target){var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");if(!ground.Raycast(new Ray(target+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("Audit destination unsupported");s.Teleport(hit.point+Vector3.up*1.1f,270);}
                if(followStep==0)
                {
                    Check(s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Following&&s.EscortCarryAttached,"unloaded escort rejoins and attaches sealed cargo");
                    followStart=npc.position;followCargoStart=s.EscortCargo.position;followLastCargo=followCargoStart;followCargoTravel=0;followMetres=s.EscortWalkMetres;PlayerAt(npc.position+Vector3.right*6);
                    followStep++;followAt=EditorApplication.timeSinceStartup+2;return;
                }
                if(followStep==1)
                {
                    Check(Vector3.Distance(followStart,npc.position)>2.5f&&s.EscortWalkMetres>followMetres+2.5f,"companion follows without revisiting old path start: distance="+Vector3.Distance(followStart,npc.position)+" issue="+s.EscortWalkIssue);
                    Check(s.EscortLargestStep<=s.EscortFollowProfile.MaximumStep+.001f&&s.EscortCarryAttached&&followCargoTravel>1,"bounded steps move cargo with carrier: step="+s.EscortLargestStep+" limit="+s.EscortFollowProfile.MaximumStep+" attached="+s.EscortCarryAttached+" cargo path="+followCargoTravel+" net delta="+Vector3.Distance(followCargoStart,s.EscortCargo.position));
                    var animator=npc.GetComponentInChildren<Animator>();Check(animator.runtimeAnimatorController.name=="WangsoLocomotion","dedicated humanoid walk controller assigned");
                    followPausePosition=npc.position;s.GetComponent<ProloguePauseMenu>().SetOpen(true);followStep++;followAt=EditorApplication.timeSinceStartup+.5;return;
                }
                if(followStep==2)
                {
                    Check(Vector3.Distance(followPausePosition,npc.position)<.001f,"pause freezes companion and cargo");s.GetComponent<ProloguePauseMenu>().SetOpen(false);
                    followWall=GameObject.CreatePrimitive(PrimitiveType.Cube);followWall.name="Audit escort wall";followWall.transform.position=npc.position+new Vector3(1,1.4f,0);followWall.transform.localScale=new Vector3(.2f,2.8f,5);Physics.SyncTransforms();
                    PlayerAt(npc.position+Vector3.right*6);followStep++;followAt=EditorApplication.timeSinceStartup+2;return;
                }
                if(followStep==3)
                {
                    Check(npc.position.x<followWall.transform.position.x-.55f&&s.EscortWalkIssue!=null,"unbaked physical wall blocks carrier instead of tunnelling: "+s.EscortWalkIssue);
                    Object.Destroy(followWall);followWall=null;followStart=npc.position;followStep++;followAt=EditorApplication.timeSinceStartup+2;return;
                }
                Check(npc.position.x>followStart.x+.5f,"removing obstacle permits grounded movement again: "+s.EscortWalkIssue);
                s.Save();var disk=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();
                Check(disk!=null&&disk.escort.CompanionMode==DemoEscortCompanionMode.Following&&Vector3.Distance(disk.escort.CompanionFeet,npc.position)<.1f&&Vector3.Distance(disk.escortCargoPosition,s.EscortCargo.position)<.1f,"walking actor and cargo positions persist together");
                FinishFollowCheck();
            }
            catch(Exception e){followChecks.Add("FAIL "+e);FinishFollowCheck();}
        }
        static void FinishFollowCheck()
        {
            EditorApplication.update-=FollowTick;if(followWall!=null)Object.Destroy(followWall);followWall=null;
            followChecks.Add("Player destinations use audit teleport; companion uses real runtime walking and collision checks. Not manual route completion or finished carrying animation.");File.WriteAllText(FollowReport,string.Join("\n",followChecks));
        }
    }
}
