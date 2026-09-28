using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.App.World.Vehicle;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad] public static class WorldMacroActRuntimeChecks
    {
        const string Key="ActsTerrain.PlayChecks",UiKey="PlaytestUiReviewSuffix",Missing="__unset__";
        [Serializable] sealed class Stamp { public string path,hash; }
        [Serializable] sealed class Run
        {
            public string status,suffix,priorSuffix,priorUi,savePath,sceneHash;
            public bool active,stopping;public int phase,lastFrame=-1;public double began,deadline,phaseTime;
            public Vector3 safe,water;public List<string> checks=new List<string>(),failures=new List<string>();public List<Stamp> saves=new List<Stamp>();
            public string scope="Actual Unity Play lifecycle with isolated save; diagnostic API placement/ticking and environment death. No native input or automatic walking, combat/escort completion or performance certification.";
        }
        static Run run;
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Demo/ActsTerrain/runtime_checks.json"));
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static string Hash(string p){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Call(WorldMacroPlaytestSession s,string name)=>typeof(WorldMacroPlaytestSession).GetMethod(name,Private).Invoke(s,null);
        static void Field(WorldMacroPlaytestSession s,string name,object value)=>typeof(WorldMacroPlaytestSession).GetField(name,Private).SetValue(s,value);
        static WorldMacroActRuntimeChecks()
        {
            var json=SessionState.GetString(Key,"");if(json.Length>0)run=JsonUtility.FromJson<Run>(json);
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;
            Application.logMessageReceived+=Log;
        }
        static void Log(string text,string stack,LogType type)
        {if(run?.active==true&&(type==LogType.Exception||type==LogType.Error)&&run.failures.Count<30){run.failures.Add(type+": "+text);Persist();}}
        static void Check(bool value,string text){(value?run.checks:run.failures).Add(text);Persist();}
        static string Persist(){var json=JsonUtility.ToJson(run,true);SessionState.SetString(Key,json);Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,json);return json;}
        public static string Status()=>run==null?"No play checks":Persist();
        public static string Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene||SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Saved compact scene in Edit required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            var s=Session;string suffix="_acts_api_"+Guid.NewGuid().ToString("N");
            run=new Run{active=true,status="ENTERING_PLAY",suffix=suffix,priorSuffix=s.TestSaveSuffix,priorUi=SessionState.GetString(UiKey,Missing),savePath=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+suffix+".json"),sceneHash=Hash(WorldMacroCompactAuthoring.TargetScene),deadline=EditorApplication.timeSinceStartup+120};
            foreach(var p in Directory.GetFiles(Application.persistentDataPath,"*.json*"))run.saves.Add(new Stamp{path=p,hash=Hash(p)});
            s.TestSaveSuffix=suffix;SessionState.SetString(UiKey,suffix);Persist();EditorApplication.EnterPlaymode();return Persist();
        }
        static void Changed(PlayModeStateChange state)
        {
            if(run==null||!run.active&&!run.stopping)return;
            if(state==PlayModeStateChange.EnteredPlayMode){run.began=EditorApplication.timeSinceStartup;run.status="RUNNING";Persist();}
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                if(Session!=null)Session.TestSaveSuffix=run.priorSuffix;
                if(run.priorUi==Missing)SessionState.EraseString(UiKey);else SessionState.SetString(UiKey,run.priorUi);
                Check(Hash(WorldMacroCompactAuthoring.TargetScene)==run.sceneHash,"Saved compact scene unchanged by diagnostic Play");
                Check(run.saves.All(s=>File.Exists(s.path)&&Hash(s.path)==s.hash),"All pre-existing save files unchanged");
                run.active=run.stopping=false;run.status=run.failures.Count==0&&run.phase>=5?"PASS":"FAIL_OR_INCOMPLETE";Persist();
            }
        }
        static void Stop(){run.active=false;run.stopping=true;Persist();EditorApplication.ExitPlaymode();}
        static void Tick()
        {
            if(run?.active!=true)return;
            if(EditorApplication.timeSinceStartup>run.deadline){run.failures.Add("Timed out");Stop();return;}
            if(!EditorApplication.isPlaying||Time.frameCount==run.lastFrame)return;run.lastFrame=Time.frameCount;
            try
            {
                if(Prologue.PrologueAudit.CommitRatio()>=.85f){run.failures.Add("System commit >=85%, stopped before further tests");Stop();return;}
                var s=Session;if(s==null||s.Progress==null||EditorApplication.timeSinceStartup-run.began<3)return;
                if(run.phase==0)
                {
                    Check(s.TestSaveSuffix==run.suffix,"Actual session uses isolated UUID save namespace");
                    Check(WorldMacroProgress.Valid(s.Progress)&&s.Traversal!=null,"Session, v7 progress and actual water query initialized");
                    Object.FindFirstObjectByType<PlaytestUiRoot>()?.CloseMenu();
                    s.CombatActive=false;s.Cull();s.enabled=false;s.Walker.Motor.enabled=false;
                    run.safe=s.LastSafeFeet;s.Progress.ledger.currency=257;s.Progress.campaign.EncounterEvidence.Add("demo_logging_01");s.Progress.renUsed=false;
                    var t=s.Traversal.Water.OrderBy(t=>Vector3.SqrMagnitude((t.A+t.B+t.C)/3-run.safe)).First();run.water=(t.A+t.B+t.C)/3;
                    s.Teleport(run.water-Vector3.up*.3f,0);Field(s,"lastSafe",run.safe);Call(s,"TickTraversal");
                    Check(!s.IsDrowning&&s.Walker.Motor.TerrainMovementScale<1&&s.Walker.Motor.TerrainMovementScale>.65f,"Actual water: 0.3m foot immersion slows movement without death");
                    s.Teleport(run.water+Vector3.up*.2f,0);Field(s,"lastSafe",run.safe);Call(s,"TickTraversal");
                    Check(!s.IsDrowning&&s.CurrentImmersion==0,"Foot position above actual water is not drowning");
                    s.Teleport(run.water-Vector3.up*.7f,0);Field(s,"lastSafe",run.safe);Call(s,"TickTraversal");
                    Check(s.IsDrowning&&s.Walker.Motor.EnvironmentalInputBlocked,"0.7m foot immersion starts timed drowning and gates input");
                    run.phase=1;Persist();return;
                }
                if(run.phase==1)
                {
                    Call(s,"TickTraversal");if(s.IsDrowning)return;
                    var v=s.Walker.Body.GetComponent<PlayerVitals>();
                    Check(v.Hp01==1&&s.Progress.ledger.currency==0&&s.Progress.ledger.dropCurrency==257,"Drowning reaches existing death path and restores HP with one currency drop");
                    Check(Vector3.Distance(s.Progress.ledger.dropPosition,run.safe)<.01f&&!s.Progress.renUsed,"Drop remains on prior dry ground; Ren is not consumed");
                    Check(!s.Walker.Motor.EnvironmentalInputBlocked&&Vector3.Distance(s.Walker.Body.transform.position,s.Progress.ledger.position)<.1f,"Checkpoint pose and input restored after drowning");
                    Check(s.Progress.campaign.EncounterEvidence.Contains("demo_logging_01"),"Persistent logging evidence survives environment recovery");
                    s.Progress.ledger.currency=19;Field(s,"lastSafe",run.safe);v.ApplyFatalFall();run.phase=2;Persist();return;
                }
                if(run.phase==2)
                {
                    Call(s,"TickTraversal");
                    Check(s.Progress.ledger.currency==0&&s.Progress.ledger.dropCurrency==19,"Second environmental death replaces unrecovered drop instead of duplicating it");
                    Check(s.SaveNow(out var error),"Recovered session saves successfully: "+error);
                    var loaded=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(run.savePath));
                    Check(WorldMacroProgress.Valid(loaded)&&loaded.version==7&&loaded.ledger.dropCurrency==19&&loaded.campaign.EncounterEvidence.Contains("demo_logging_01"),"Saved v7 reload retains recovery and encounter evidence");
                    var summon=s.DemoEscortSummon;var seat=s.DemoEscortSeat;
                    if(summon==null||seat==null||!summon.TryFindPlacement(run.safe,Vector3.forward,out var placement,out var reason))throw new InvalidOperationException("Vehicle diagnostic placement unavailable");
                    typeof(WorldMacroPalanquinSummon).GetMethod("CommitPlacement",Private).Invoke(summon,new object[]{placement});
                    var car=seat.Vehicle;car.Body.isKinematic=true;
                    Vector3 entry=default;bool found=false;
                    foreach(var socket in seat.ExitSockets)if(s.TrySafeFeet(socket.position,out entry)){found=true;break;}
                    if(!found)throw new InvalidOperationException("No safe vehicle entry socket");
                    s.Teleport(entry,0);s.Walker.Motor.enabled=true;run.phaseTime=EditorApplication.timeSinceStartup;run.phase=3;Persist();return;
                }
                if(run.phase==3)
                {
                    if(EditorApplication.timeSinceStartup-run.phaseTime<1)return;
                    var seat=s.DemoEscortSeat;
                    if(!seat.TryBoard())
                    {
                        if(EditorApplication.timeSinceStartup-run.phaseTime<3)return;
                        throw new InvalidOperationException("Vehicle boarding: "+seat.LastInteraction+"; CanBoard="+s.Walker.CanBoard);
                    }
                    Check(seat.Occupied&&s.Walker.Seated&&!s.Walker.Body.enabled,"Actual TryBoard transfers input/camera ownership");
                    var car=seat.Vehicle;float bottom=car.Hull.bounds.min.y-car.transform.position.y;
                    var position=new Vector3(run.water.x,run.water.y-.7f-bottom,run.water.z);
                    car.Body.position=position;car.transform.position=position;Physics.SyncTransforms();Field(s,"lastSafe",run.safe);Field(s,"vehicleHeightKnown",false);
                    s.Progress.ledger.currency=23;Call(s,"TickTraversal");
                    Check(s.IsDrowning,"Occupied vehicle hull immersion starts environment recovery");
                    run.phaseTime=EditorApplication.timeSinceStartup;run.phase=4;Persist();return;
                }
                if(run.phase==4)
                {
                    Call(s,"TickTraversal");if(s.IsDrowning)return;
                    Check(!s.DemoEscortSeat.Occupied&&!s.Walker.Seated&&s.Walker.Body.enabled&&s.DemoEscortSummon.IsRecalled,"Vehicle drowning releases ownership, restores walker, recalls one vehicle");
                    Check(s.Progress.ledger.dropCurrency==23&&Vector3.Distance(s.Progress.ledger.dropPosition,run.safe)<.01f,"Vehicle environment death preserves safe dry drop location");
                    run.phase=5;Stop();
                }
            }
            catch(Exception e){run.failures.Add(e.ToString());Stop();}
        }
    }
}
