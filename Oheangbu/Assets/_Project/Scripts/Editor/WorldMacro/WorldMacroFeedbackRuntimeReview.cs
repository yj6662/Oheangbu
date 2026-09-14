using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Bounded service test: recognized event injection, never raw handwriting or user audio approval.
    [InitializeOnLoad]
    public static class WorldMacroFeedbackRuntimeReview
    {
        [Serializable] class Check { public string name,status,detail; }
        [Serializable] class Report
        {
            public string utc,status,scope="Live Playtest services with a temporary target and injected recognized letters; simulated focus permission for audio. Not manual input, device output, listening or visual approval.";
            public List<Check> checks=new List<Check>();
            public string before,after;
            public bool cleanup;
        }
        static string PathOut=>System.IO.Path.GetFullPath("../Art/UIAudio/PlaytestFeedback/runtime_audio.json");
        static WorldMacroPlaytestSession session;
        static WorldMacroPlaytestAudio audio;
        static CombatLoopWiring wiring;
        static InkPool ink;
        static GroggyMeter groggy;
        static PlayerVitals vitals;
        static ParryJudge judge;
        static List<EnemyVitals> targets;
        static EnemyVitals[] oldTargets;
        static GameObject targetObject;
        static EnemyVitals target;
        static bool active,oldOcclusion,oldFocus,oldApplicationPaused,oldMotorEnabled,oldDrawingEnabled,oldCombatActive,oldAudioEnabled;
        static float oldHp,oldInk,oldScale,targetHpBefore;
        static int oldGroggyCount,phase,frame,hitBefore,castBefore;
        static double next,deadline,targetCastStartedAt,targetHitDeadline;
        static Report report;
        static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static WorldMacroFeedbackRuntimeReview(){AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(active)Finish("ABORTED","Assembly reload");};}
        static T Read<T>(object o,string name)=>(T)o.GetType().GetField(name,Flags).GetValue(o);
        static void Write(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
        static void Invoke(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
        static int Count(string name)=>Read<int>(audio,name);
        static AudioSource[] Voices()=>audio.GetComponentsInChildren<AudioSource>(true);
        static void CheckIt(string name,bool passed,string detail)
        {report.checks.Add(new Check{name=name,status=passed?"PASS":"FAIL",detail=detail});}
        public static string Execute(string action)
        {
            if(action=="poll")return active?"RUNNING phase="+phase:File.Exists(PathOut)?File.ReadAllText(PathOut):"NOT_RUN";
            if(action!="begin")throw new ArgumentException("Feedback begin|poll");
            if(active||!EditorApplication.isPlaying||EditorApplication.isPaused)throw new InvalidOperationException("Unpaused Play mode required");
            session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();
            if(session==null||audio==null)throw new InvalidOperationException("Installed Playtest required");
            if(session.Progress==null)throw new InvalidOperationException("Playtest session is not ready");
            if(string.IsNullOrWhiteSpace(session.TestSaveSuffix))throw new InvalidOperationException("Isolated TestSaveSuffix required before Play mode");
            if(session.Walker==null||session.Walker.Wiring==null||session.Walker.Body==null||session.Walker.Motor==null||session.Walker.Drawing==null)
                throw new InvalidOperationException("Complete walker input and combat wiring required");
            if(session.Walker.Seated||session.Walker.Drawing.InDrawMode)throw new InvalidOperationException("Begin from idle walking state, outside draw mode");
            var lockOn=session.Walker.Motor.GetComponent<LockOn>();
            if(lockOn!=null&&lockOn.IsLocked)throw new InvalidOperationException("Clear lock-on before controlled feedback review");
            if(!audio.isActiveAndEnabled||Read<bool>(audio,"_applicationPaused"))throw new InvalidOperationException("Active, unpaused playtest audio required");
            wiring=session.Walker.Wiring;vitals=session.Walker.Body.GetComponent<PlayerVitals>();
            ink=Read<InkPool>(wiring,"_ink");groggy=Read<GroggyMeter>(wiring,"_groggy");judge=Read<ParryJudge>(wiring,"_judge");
            if(vitals==null||ink==null||groggy==null||judge==null)throw new InvalidOperationException("Live combat services required");
            var pending=(System.Collections.IList)Read<object>(wiring,"_pendingCasts");
            if(pending.Count!=0)throw new InvalidOperationException("Pending casts must be empty before controlled feedback review; found "+pending.Count);
            if(Read<bool>(judge,"_hasGuard"))throw new InvalidOperationException("Clear the live parry guard before controlled feedback review");
            targets=Read<List<EnemyVitals>>(wiring,"_targets");oldTargets=targets.ToArray();
            oldHp=vitals.Hp01;oldInk=ink.Value;oldScale=Time.timeScale;
            oldGroggyCount=Read<int>(groggy,"_count");
            oldOcclusion=Read<bool>(wiring,"_environmentOcclusion");oldFocus=Read<bool>(audio,"_focused");oldApplicationPaused=Read<bool>(audio,"_applicationPaused");
            oldMotorEnabled=session.Walker.Motor.enabled;oldDrawingEnabled=session.Walker.Drawing.enabled;
            oldCombatActive=session.CombatActive;oldAudioEnabled=audio.enabled;
            report=new Report{utc=DateTime.UtcNow.ToString("o"),status="RUNNING",before=audio.RuntimeCounters};
            active=true;phase=0;frame=-1;deadline=EditorApplication.timeSinceStartup+25;
            try
            {
                Time.timeScale=1;Invoke(audio,"OnApplicationFocus",true);
                session.Walker.Motor.enabled=false;session.Walker.Drawing.enabled=false;
                session.CombatActive=false;session.Cull();
                targets.Clear();Write(wiring,"_environmentOcclusion",false);
                targetObject=new GameObject("Temporary_Feedback_Service_Target"){hideFlags=HideFlags.HideAndDontSave};
                var camera=Camera.main;
                if(camera==null)throw new InvalidOperationException("One active MainCamera required for free-aim target placement");
                targetObject.transform.position=camera.transform.position+camera.transform.forward*4f;
                target=targetObject.AddComponent<EnemyVitals>();
                Write(target,"_config",Read<CombatConfigSO>(wiring,"_config"));target.Restore();
                CheckIt("twelve_preallocated_voices",Voices().Length==12&&audio.VoiceCount==12,"No per-hit GameObjects are created by audio.");
                int baseline=Count("_acceptedCastEvents"),plays=Count("_cuePlays");
                ink.Restore(0);Cast('가',Jamo.Giyeok,Jamo.A);
                CheckIt("no_ink_rejected_cast_is_silent",Count("_acceptedCastEvents")==baseline&&Count("_cuePlays")==plays,"Recognized letter delivered with zero ink; accepted event and cue remain unchanged.");
                next=EditorApplication.timeSinceStartup+.12;EditorApplication.update+=Tick;
                return "RUNNING controlled service QA; isolated save slot enforced; player movement/drawing and encounter combat suspended.";
            }
            catch(Exception e){Finish("ERROR",e.ToString());throw;}
        }
        static void Cast(char glyph,Jamo initial,Jamo vowel)
        {Read<DrawnLetterEventChannelSO>(wiring,"_letterDrawn").Raise(new DrawnLetter(glyph,initial,vowel,null,.8f,.75f,1.2f,1.4f,2));}
        static void Tick()
        {
            if(!active||frame==Time.frameCount)return;frame=Time.frameCount;
            try
            {
                if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>deadline){Finish("ERROR","Play ended/deadline");return;}
                if(EditorApplication.timeSinceStartup<next)return;
                if(phase<5)
                {
                    char[] glyphs={'가','나','마','사','아'};Jamo[] initials={Jamo.Giyeok,Jamo.Nieun,Jamo.Mieum,Jamo.Siot,Jamo.Ieung};
                    string[] clips={"cast_wood","cast_fire","cast_earth","cast_metal","cast_water"};
                    ink.Restore(1);int c=Count("_acceptedCastEvents"),p=Count("_cuePlays"),h=Count("_enemyHitEvents");
                    Cast(glyphs[phase],initials[phase],Jamo.A);
                    CheckIt("accepted_"+glyphs[phase],Count("_acceptedCastEvents")==c+1&&Count("_cuePlays")==p+1&&Read<string>(audio,"_lastCue")==clips[phase],audio.RuntimeCounters);
                    CheckIt("air_cast_no_hit_"+glyphs[phase],Count("_enemyHitEvents")==h,"Empty target list; no resolved hit signal.");
                    phase++;next=EditorApplication.timeSinceStartup+.15;return;
                }
                switch(phase)
                {
                    case 5:
                        targets.Add(target);ink.Restore(1);hitBefore=Count("_enemyHitEvents");castBefore=Count("_acceptedCastEvents");
                        targetHpBefore=target.Hp01;targetCastStartedAt=EditorApplication.timeSinceStartup;targetHitDeadline=targetCastStartedAt+4;
                        Cast('가',Jamo.Giyeok,Jamo.A);phase++;next=EditorApplication.timeSinceStartup+.05;break;
                    case 6:
                        int hitDelta=Count("_enemyHitEvents")-hitBefore,castDelta=Count("_acceptedCastEvents")-castBefore;
                        bool targetDamaged=target!=null&&target.Hp01<targetHpBefore;
                        if(hitDelta==0&&!targetDamaged&&EditorApplication.timeSinceStartup<targetHitDeadline)
                        {next=EditorApplication.timeSinceStartup+.05;break;}
                        string hitDetail="hitDelta="+hitDelta+" castDelta="+castDelta+" hp="+targetHpBefore.ToString("F3")+"->"+(target!=null?target.Hp01.ToString("F3"):"destroyed")+" elapsed="+(EditorApplication.timeSinceStartup-targetCastStartedAt).ToString("F3")+"s";
                        CheckIt("actual_scheduled_target_hit_once",hitDelta==1&&castDelta==1&&targetDamaged,hitDetail);
                        int p=Count("_playerHitEvents");vitals.TakeDamage(1);
                        CheckIt("real_player_damage_event",Count("_playerHitEvents")==p+1,"1 damage, restored in cleanup.");
                        var hud=Object.FindFirstObjectByType<HudController>();
                        vitals.Restore(.43f);CheckIt("hp_restore_refresh",hud!=null&&Mathf.Abs(hud.Hp01-.43f)<.001f,"Restore broadcasts HpChanged without Damaged.");
                        int parry=Count("_parryEvents");judge.RaiseGuard(Element.Water,Time.time);
                        var result=judge.ResolveImpact(Element.Fire,Time.time,session.Walker.Body.transform.position+Vector3.forward);
                        CheckIt("real_parry_success_event",result==ParryOutcome.Success&&Count("_parryEvents")==parry+1,result.ToString());
                        phase++;next=EditorApplication.timeSinceStartup+.15;break;
                    case 7:
                        int drops=Count("_cooldownDrops");
                        // Direct presentation signal stress; distinct from actual damage-path verification above.
                        for(int i=0;i<40;i++)Invoke(audio,"OnEnemyHit",target.transform.position,Element.Wood);
                        CheckIt("repeated_contact_throttled",Count("_cooldownDrops")>=drops+39&&Voices().Length==12,"40 direct presentation signals in one tick; pool stays fixed.");
                        Invoke(audio,"OnApplicationPause",true);
                        CheckIt("pause_clears_all_sources",Voices().All(s=>!s.isPlaying&&s.clip==null),"Application-pause callback simulated.");
                        int plays=Count("_cuePlays");Invoke(audio,"OnEnemyHit",target.transform.position,Element.Wood);
                        CheckIt("pause_rejects_new_cues",Count("_cuePlays")==plays,"Paused presentation does not queue later audio.");
                        Invoke(audio,"OnApplicationPause",false);
                        audio.enabled=false;
                        CheckIt("disable_clears_sources",Voices().All(s=>!s.isPlaying&&s.clip==null),"Source component disabled; listeners removed.");
                        audio.enabled=true;Invoke(audio,"OnApplicationFocus",true);
                        int before=Count("_acceptedCastEvents");targets.Clear();ink.Restore(1);Cast('나',Jamo.Nieun,Jamo.A);
                        CheckIt("reenable_no_duplicate_subscription",Count("_acceptedCastEvents")==before+1&&Voices().Length==12,"One accepted event after re-enable.");
                        Finish(report.checks.All(c=>c.status=="PASS")?"PASS_CONTROLLED_SERVICE_TESTS":"FINDINGS","Complete");break;
                }
            }
            catch(Exception e){Finish("ERROR",e.ToString());}
        }
        static void Finish(string status,string detail)
        {
            if(!active)return;active=false;EditorApplication.update-=Tick;
            report.after=audio!=null?audio.RuntimeCounters:"destroyed";
            if(status=="ERROR"||status=="ABORTED")CheckIt("completion",false,detail);
            var cleanupErrors=new List<string>();
            CleanupStep(()=>
            {
                var pending=(System.Collections.IList)Read<object>(wiring,"_pendingCasts");
                for(int i=pending.Count-1;i>=0;i--)
                {
                    object item=pending[i];var field=item.GetType().GetField("Target",Flags);
                    if(field!=null&&(EnemyVitals)field.GetValue(item)==target)pending.RemoveAt(i);
                }
                if(pending.Count!=0)throw new InvalidOperationException("Preserved "+pending.Count+" unexpected external pending cast(s)");
            },"pending casts",cleanupErrors);
            CleanupStep(()=>{targets.Clear();targets.AddRange(oldTargets);},"target list",cleanupErrors);
            CleanupStep(()=>Write(wiring,"_environmentOcclusion",oldOcclusion),"occlusion",cleanupErrors);
            CleanupStep(()=>judge.ClearGuard(),"parry guard",cleanupErrors);
            CleanupStep(()=>{Write(groggy,"_count",oldGroggyCount);Invoke(wiring,"RefreshHud");},"groggy",cleanupErrors);
            CleanupStep(()=>vitals.Restore(oldHp),"player hp",cleanupErrors);
            CleanupStep(()=>ink.Restore(oldInk),"ink",cleanupErrors);
            CleanupStep(()=>Time.timeScale=oldScale,"time scale",cleanupErrors);
            CleanupStep(()=>{session.CombatActive=oldCombatActive;session.Cull();},"encounter combat",cleanupErrors);
            CleanupStep(()=>{session.Walker.Drawing.enabled=oldDrawingEnabled;session.Walker.Motor.enabled=oldMotorEnabled;},"player input",cleanupErrors);
            CleanupStep(()=>{audio.enabled=oldAudioEnabled;Invoke(audio,"StopAll");Invoke(audio,"OnApplicationPause",oldApplicationPaused);Invoke(audio,"OnApplicationFocus",oldFocus);},"audio",cleanupErrors);
            CleanupStep(()=>{if(targetObject!=null)Object.DestroyImmediate(targetObject);},"temporary target",cleanupErrors);
            report.cleanup=cleanupErrors.Count==0;
            if(!report.cleanup)CheckIt("cleanup",false,string.Join(" | ",cleanupErrors));
            if(status=="ERROR"||status=="ABORTED")report.status=status;
            else report.status=report.cleanup&&report.checks.All(c=>c.status=="PASS")?"PASS_CONTROLLED_SERVICE_TESTS":"FINDINGS";
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOut));File.WriteAllText(PathOut,JsonUtility.ToJson(report,true));
        }
        static void CleanupStep(Action action,string label,List<string> errors)
        {try{action();}catch(Exception e){errors.Add(label+": "+e.Message);}}
    }
}
