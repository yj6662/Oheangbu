using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Runs only inside the existing UUID-isolated chapter-two Play harness. No native input is synthesized.
    [InitializeOnLoad]
    public static class DemoSummonPlayChecks
    {
        [Serializable] class Report
        {
            public string status="NOT_RUN", phase;
            public string scope="Actual demo Game View/runtime update with a stationary existing enemy and diagnostic resolved cast. Not native drawing input, manual combat, or a walkthrough.";
            public List<string> passed=new List<string>(), failed=new List<string>();
            public string[] unverified={"Native player input", "Uneven-ground foot IK", "Final animation quality", "Remaining summon combat", "Moving performance"};
            public float inkBefore,inkAfter,damageSnapshot,rootDamageSnapshot,flameDamageSnapshot,damageApplied,actorAge;
            public int hits,pathQueries,pathFailures,blockedSteps,rootPlanPoints;
            public string attackKind;
            public float tigerDamageSnapshot;
            public float clubDamageSnapshot,clubRange;
            public float waterDamageSnapshot,waterVisibleLength,waterSocketError;
            public bool tigerLeapObserved, tigerLeftObserved, tigerRightObserved;
            public Vector3 player,spawn;
        }
        static Quaternion initialBoneRotation;
        static Report report;
        static bool rootMode, flameMode, tigerMode, clubMode, waterMode, leapReviewHeld;
        static bool reviewCameraSaved;
        static Vector3 playerCameraLocalPosition;
        static Quaternion playerCameraLocalRotation;
        static DemoSummonCombatManager manager;
        static WorldMacroPlaytestSession session;
        static bool running, waitingExpiry;
        static double deadline, hitAt;
        static float originalTimeScale=1;
        static Transform hoof;
        static Vector3 initialHoof;
        static bool hoofChanged;
        static int initialHits,initialPathQueries,initialPathFailures,initialBlockedSteps;
        static float initialDamage;
        static string Output=>Path.Combine(DemoSummonAuthoring.Output,(waterMode ? "runtime_water_tests.json" : clubMode ? "runtime_club_tests.json" : tigerMode ? "runtime_tiger_tests.json" : flameMode ? "runtime_flame_tests.json" : rootMode ? "runtime_root_tests.json" : "runtime_tests.json"));
        static DemoSummonPlayChecks(){EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode){running=false;EditorApplication.update-=Tick;Time.timeScale=originalTimeScale;}};}
        static object Get(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
        static void Call(object o,string name,params object[] values)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,values);
        static void Check(bool value,string text){(value?report.passed:report.failed).Add(text);}
        static string Save(){Directory.CreateDirectory(DemoSummonAuthoring.Output);string json=JsonUtility.ToJson(report,true);File.WriteAllText(Output,json);return json;}
        public static string Execute(string command)
        {
            if(command=="poll")return report!=null?Save():File.Exists(Output)?File.ReadAllText(Output):"NOT_RUN";
            if(command=="view:external"||command=="view:player")
            {
                if(manager==null||manager.Active==null||session==null||Time.timeScale!=0)throw new InvalidOperationException("A paused real summon is required");
                if(command=="view:player")
                {
                    if(reviewCameraSaved)
                    {
                        var camera=session.Walker.ViewCamera;
                        camera.transform.SetLocalPositionAndRotation(playerCameraLocalPosition,playerCameraLocalRotation);
                        reviewCameraSaved=false;
                    }
                    session.Walker.CameraRig.enabled=true;
                }
                else
                {
                    if(!reviewCameraSaved)
                    {
                        var playerCamera=session.Walker.ViewCamera;
                        playerCameraLocalPosition=playerCamera.transform.localPosition;
                        playerCameraLocalRotation=playerCamera.transform.localRotation;
                        reviewCameraSaved=true;
                    }
                    session.Walker.CameraRig.enabled=false;
                    var actor=manager.Active.transform;var camera=session.Walker.ViewCamera;
                    camera.transform.position=actor.position+actor.rotation*(rootMode||flameMode||tigerMode||waterMode?new Vector3(5f,3.5f,6f):new Vector3(3.3f,2.1f,3.5f));
                    camera.transform.rotation=Quaternion.LookRotation(actor.position+(rootMode||flameMode||tigerMode||waterMode?actor.forward*2+Vector3.up*.8f:Vector3.up*1.35f)-camera.transform.position,Vector3.up);
                }
                return "Actual Game View camera adjusted for a still, actor and pose unchanged.";
            }
            if(command=="continue-strike")
            {
                if(!tigerMode||!leapReviewHeld||report==null||manager==null||manager.Active==null)throw new InvalidOperationException("No held tiger leap");
                Time.timeScale=1;waitingExpiry=false;running=true;report.phase="continue actual tiger to alternating claws";
                deadline=EditorApplication.timeSinceStartup+20;EditorApplication.update-=Tick;EditorApplication.update+=Tick;return Save();
            }
            if(command=="resume")
            {
                if(report==null||manager==null||manager.Active==null)throw new InvalidOperationException("No held summon review");
                Time.timeScale=1;waitingExpiry=true;running=true;report.phase="waiting for natural expiry";
                deadline=EditorApplication.timeSinceStartup+35;EditorApplication.update-=Tick;EditorApplication.update+=Tick;return Save();
            }
            if(command!="begin" && command!="begin-root" && command!="begin-flame" && command!="begin-tiger" && command!="begin-club" && command!="begin-water")throw new ArgumentException("play: begin/begin-root/begin-flame/begin-tiger/begin-club/begin-water/poll/resume");
            session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(!EditorApplication.isPlaying||session==null||session.Progress==null||!session.TestSaveSuffix.StartsWith("_chapter2_",StringComparison.Ordinal))
                throw new InvalidOperationException("Use the UUID-isolated chapter2 runtime harness first");
            manager=Object.FindFirstObjectByType<DemoSummonCombatManager>();
            if(manager==null)throw new InvalidOperationException("Summon manager missing");
            if(running)throw new InvalidOperationException("Existing run still active");
            rootMode=command=="begin-root";
            flameMode=command=="begin-flame";
            tigerMode=command=="begin-tiger";leapReviewHeld=false;
            clubMode=command=="begin-club";
            waterMode=command=="begin-water";
            reviewCameraSaved=false;
            report=new Report{attackKind=waterMode?"water":clubMode?"club":tigerMode?"tiger":flameMode?"flame":rootMode?"root":"profile-default",status="RUNNING",phase="seeking connected spawn near actual logging enemy"};
            PlaytestUiRoot.Instance.CloseMenu();originalTimeScale=Time.timeScale;Time.timeScale=1;
            // Freeze the test target/other AI only. Damage still uses its existing EnemyVitals and production wiring.
            session.enabled=false;
            var target=session.Actors.First(a=>a.Id=="demo_logging_01");
            foreach(var actor in session.Actors)
            {
                actor.enabled=false;actor.GetComponent<EnemyController>().enabled=false;
                var nav=actor.GetComponent<NavMeshAgent>();if(nav!=null)nav.enabled=false;
                if(actor!=target)actor.gameObject.SetActive(false);
            }
            target.gameObject.SetActive(true);target.GetComponent<EnemyVitals>().Restore();
            session.Walker.Motor.enabled=false;
            var wiring=session.Walker.Wiring;manager.Clear();
            initialHits=manager.ConfirmedHits;initialDamage=manager.ConfirmedDamage;
            initialPathQueries=manager.PathQueries;initialPathFailures=manager.PathFailures;initialBlockedSteps=manager.BlockedMovementSteps;
            var ink=(InkPool)Get(wiring,"_ink");ink.Restore();
            var cast=new SpellCast(waterMode?'옴':clubMode?'몸':tigerMode?'솜':flameMode?'놈':'곰',SpellKind.Summon,waterMode?Element.Water:clubMode?Element.Earth:tigerMode?Element.Metal:flameMode?Element.Fire:Element.Wood,20,default,1);
            bool found=false;
            for(int i=0;i<12&&!found;i++)
            {
                Vector3 direction=Quaternion.Euler(0,i*30,0)*Vector3.forward;
                Vector3 candidate=target.transform.position-direction*8-Vector3.up*.875f;
                if(!session.TrySafeFeet(candidate,out var feet))continue;
                session.Teleport(feet,Quaternion.LookRotation(direction).eulerAngles.y);
                Physics.SyncTransforms();
                found=manager.TryPrepare(cast,feet,direction,out _);
                if(found){report.player=feet;manager.CancelPrepared();}
            }
            Check(found,"Found supported, clear, NavMesh-connected placement in real logging terrain");
            if(!found){report.failed.Add(manager.LastFailure);report.status="FAIL";Time.timeScale=0;return Save();}
            report.inkBefore=ink.Value;Call(wiring,"ResolveSummon",cast);report.inkAfter=ink.Value;
            Check(manager.Active!=null,"Production ResolveSummon -> accepted event creates exactly one actor");
            if(manager.Active==null){report.failed.Add(manager.LastFailure);report.status="FAIL";return Save();}
            var config=(CombatConfigSO)Get(wiring,"_config");
            Check(Mathf.Abs((report.inkBefore-report.inkAfter)-2*config.SpellInkCost/ink.CapacityMultiplier)<.0001f,"Exactly two basic attack costs spent");
            report.spawn=manager.Active.transform.position;report.damageSnapshot=manager.DamageSnapshot;report.rootDamageSnapshot=manager.RootDamageSnapshot;report.flameDamageSnapshot=manager.FlameDamageSnapshot;
            report.tigerDamageSnapshot=manager.TigerDamageSnapshot;
            report.clubDamageSnapshot=manager.ClubDamageSnapshot;
            report.waterDamageSnapshot=manager.WaterDamageSnapshot;
            var activeProfile=manager.Profiles.Single(p=>p.Letter==(waterMode?"옴":clubMode?"몸":tigerMode?"솜":flameMode?"놈":"곰"));
            if(clubMode)report.clubRange=activeProfile.ClubRange;
            Check(Mathf.Abs(manager.DamageSnapshot-20*wiring.SummonDamageScale(cast.Element)*activeProfile.DamageMultiplier)<.0001f,"Cast brush power and current upgrade captured once");
            var presentation=manager.Active.GetComponent<DemoSummonPresentation>();
            int expectedSkins=clubMode||waterMode?activeProfile.PresentationPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length:flameMode||tigerMode?1:3;
            Check(expectedSkins>0&&presentation.SkinnedRendererCount==expectedSkins&&presentation.HasAnimationGraph,"Expected skinned mesh count and manual-clock imported animation graph active");
            hoof=manager.Active.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==(waterMode?"MouthOrigin":clubMode?"ClubTip":rootMode||flameMode?"Head":"Fore_L_Hoof"));
            if(hoof!=null){initialHoof=manager.Active.transform.InverseTransformPoint(hoof.position);initialBoneRotation=hoof.localRotation;}
            hoofChanged=false;hitAt=0;waitingExpiry=false;running=true;deadline=EditorApplication.timeSinceStartup+28;
            report.phase="actual Update formation, navigation, authored attack windup and positive damage";
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;return Save();
        }
        static void Tick()
        {
            if(!running)return;
            try
            {
                if(!EditorApplication.isPlaying||manager==null)throw new InvalidOperationException("Play ended");
                report.hits=manager.ConfirmedHits-initialHits;report.damageApplied=manager.ConfirmedDamage-initialDamage;
                report.pathQueries=manager.PathQueries-initialPathQueries;report.pathFailures=manager.PathFailures-initialPathFailures;report.blockedSteps=manager.BlockedMovementSteps-initialBlockedSteps;
                if(manager.ActiveClock!=null)report.actorAge=manager.ActiveClock.Elapsed;
                if(manager.ActiveRootPlan!=null)report.rootPlanPoints=Mathf.Max(report.rootPlanPoints,manager.ActiveRootPlan.GroundPositions.Count);
                if(tigerMode&&manager.ActiveTigerPlan!=null)
                {
                    var plan=manager.ActiveTigerPlan;var presentation=manager.Active.GetComponent<DemoSummonPresentation>();
                    report.tigerLeapObserved|=plan.Stage==TigerAttackStage.Leaping&&presentation.TigerVisualStage==TigerAttackStage.Leaping;
                    report.tigerLeftObserved|=plan.Stage==TigerAttackStage.ClawLeft&&presentation.TigerVisualStage==TigerAttackStage.ClawLeft;
                    report.tigerRightObserved|=plan.Stage==TigerAttackStage.ClawRight&&presentation.TigerVisualStage==TigerAttackStage.ClawRight;
                    if(!leapReviewHeld&&plan.Stage==TigerAttackStage.Leaping&&plan.LeapProgress>=.45f)
                    {
                        leapReviewHeld=true;Time.timeScale=0;
                        Check(report.hits==0,"Actual leap travels before either claw causes damage");
                        Check(report.tigerLeapObserved,"Production tiger plan and imported leap visual stage agree");
                        Finish("PASS_LEAP_PENDING_CLAWS","held during actual leap; continue-strike proceeds to claw contacts");return;
                    }
                }
                if(hoof!=null&&manager.Active!=null)hoofChanged|=Vector3.Distance(initialHoof,manager.Active.transform.InverseTransformPoint(hoof.position))>.005f || Quaternion.Angle(initialBoneRotation,hoof.localRotation)>1f;
                if(waitingExpiry)
                {
                    if(manager.Active==null)
                    {
                        Check(true,"Natural activity expiry removed actor without manual destruction");
                        Check(Object.FindObjectsByType<DemoSummonPresentation>(FindObjectsSortMode.None).Length==0,"No summon presentation left after expiry");
                        Finish("PASS_RUNTIME_API","natural expiry and cleanup complete; stop chapter2 harness to restore scene and suffix");
                    }
                }
                else if(report.hits>=(tigerMode?2:1))
                {
                    if(hitAt==0)hitAt=EditorApplication.timeSinceStartup;
                    if(EditorApplication.timeSinceStartup-hitAt>.18)
                    {
                        Check(true,"Actual scaled Update produced a positive summon strike");
                        if(rootMode)
                        {
                            Check(manager.ActiveAttackIsRoot && report.rootPlanPoints>1,"Actual first strike uses fixed ground root plan");
                            var roots=manager.Active.GetComponent<DemoSummonRootPresentation>();
                            Check(roots!=null && roots.VisiblePieces>0,"Reused botanical root pieces are visible at actual root impact");
                        }
                        if(flameMode)
                        {
                            Check(manager.ActiveFlamePlan!=null&&manager.ActiveFlamePlan.IsReleased,"Actual fire strike uses committed mouth cone");
                            var fire=manager.Active.GetComponent<DemoSummonFlamePresentation>();
                            Check(fire!=null&&fire.ParticleSystems==10&&fire.LiveParticles>0,"Reused KTP flame systems are visible during actual spray");
                        }
                        if(tigerMode)
                        {
                            Check(report.tigerLeapObserved&&report.tigerLeftObserved&&report.tigerRightObserved,"Actual leap and both imported claw stages were sampled");
                            Check(Mathf.Abs(report.damageApplied-2*report.tigerDamageSnapshot)<.001f,"Two actual claws each use the captured per-claw power");
                        }
                        if(clubMode)
                        {
                            Check(manager.ActiveClubPlan!=null&&manager.ActiveClubPlan.SampleTime>=manager.ActiveClubPlan.SweepAt,"Actual club strike uses committed forward sweep");
                            Check(Mathf.Abs(report.damageApplied-report.clubDamageSnapshot)<.001f,"One actual target takes one captured club power");
                        }
                        if(waterMode)
                        {
                            var plan=manager.ActiveWaterPlan;var water=manager.Active.GetComponent<DemoSummonWaterPresentation>();
                            Check(plan!=null&&plan.IsStreaming,"Actual water strike follows committed traveling water plan");
                            Check(Mathf.Abs(report.damageApplied-report.waterDamageSnapshot)<.001f,"Actual water target takes one captured power");
                            report.waterVisibleLength=water!=null?water.VisibleLength:0;
                            Check(water!=null&&water.VisibleLength>0&&water.LiveDrops>0,"Clocked liquid surface and foam are present during actual strike");
                            report.waterSocketError=hoof!=null&&plan!=null?Vector3.Distance(hoof.position,plan.Origin):float.MaxValue;
                            Check(report.waterSocketError<.025f,"Actual imported mouth remains within 2.5cm of committed jet origin");
                        }
                        Check(Object.FindObjectsByType<DemoSummonPresentation>(FindObjectsSortMode.None).Length==1&&
                            Object.FindObjectsByType<Oheangbu.App.SpellVFX120.WoodDeerVfx>(FindObjectsSortMode.None).Length==0&&
                            Object.FindObjectsByType<Oheangbu.App.SpellVFX120.FireHaetaeVfx>(FindObjectsSortMode.None).Length==0&&
                            Object.FindObjectsByType<Oheangbu.App.SpellVFX120.MetalTigerVfx>(FindObjectsSortMode.None).Length==0&&
                            Object.FindObjectsByType<Oheangbu.App.SpellVFX120.DokkaebiClubVfx>(FindObjectsSortMode.None).Length==0&&
                            Object.FindObjectsByType<Oheangbu.App.SpellVFX120.WaterTurtleVfx>(FindObjectsSortMode.None).Length==0,
                            "Exactly one combat presentation and no duplicate legacy static summon");
                        Check(hoofChanged,waterMode?"Imported mouth bone moves into planted cast pose":clubMode?"Imported club tip animates relative to actor root":rootMode||flameMode?"Imported head bone animates during planted cast":"Imported hoof bone moved relative to actor root during approach/attack");
                        Check(manager.ActiveClock!=null&&manager.ActiveClock.ActivityRemaining>0,"Actor retains its combat activity budget after first strike");
                        Time.timeScale=0;
                        Finish("PASS_STRIKE_PENDING_EXPIRY","held after actual strike for screenshot; play:resume checks expiry");
                    }
                }
                if(running&&EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Summon did not reach expected runtime state. Last failure: "+manager.LastFailure);
            }
            catch(Exception e){report.failed.Add(e.ToString());Time.timeScale=0;Finish("FAIL","failed; inspect before stop");}
        }
        static void Finish(string status,string phase)
        {running=false;EditorApplication.update-=Tick;report.phase=phase;report.status=report.failed.Count==0?status:"FAIL";Save();}
    }
}
