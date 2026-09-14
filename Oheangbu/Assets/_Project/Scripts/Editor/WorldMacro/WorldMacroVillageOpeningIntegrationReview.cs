using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Explicit, isolated Play-only API integration probe. Never drives or synthesizes input.
    [InitializeOnLoad]
    public static class WorldMacroVillageOpeningIntegrationReview
    {
        [Serializable] sealed class CheckResult {public string name,status,detail;}
        [Serializable] sealed class Report
        {
            public string status,slot,phase,scope="Diagnostic API calls and bounded test-only teleports, not native player input or an automatic walkthrough. Only an explicit isolated test save is written.";
            public List<CheckResult> checks=new List<CheckResult>();public List<string> failures=new List<string>();
            public bool cleanup;public Vector3 originalPlayer,originalCar,summonedCar;
        }
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/OpeningSequence/village_integration.json"));
        static bool active;static int phase;static double readyAt,deadline;
        static WorldMacroPlaytestSession session;static PlaytestUiRoot ui;static WorldMacroPalanquinSummon caller;
        static WorldMacroPalanquinSeat seat;static WorldMacroPalanquinController car;static WorldMacroPalanquinProfileSO profile;
        static Vector3 oldFeet,oldCar,oldVelocity,oldAngular;static Quaternion oldCarRotation;static float oldYaw;static bool oldRun;static string oldPage;
        static int carId,callsBefore,currencyBefore;static Report report;
        static WorldMacroVillageOpeningIntegrationReview(){AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(active)Finish("ABORTED_RELOAD");};}
        public static string Execute(string action)
        {
            if(action=="poll")return active?"RUNNING "+report.phase:File.Exists(Output)?File.ReadAllText(Output):"NOT_RUN";
            if(action=="abort"){if(active){report.failures.Add("Explicit abort");Finish("ABORTED");}return "Stopped";}
            if(action!="begin")throw new ArgumentException("village integration: begin | poll | abort");
            if(active||!EditorApplication.isPlaying||EditorApplication.isPaused)throw new InvalidOperationException("Idle Play mode required");
            session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();ui=PlaytestUiRoot.Instance;
            if(session==null||session.Progress==null||ui==null||ui.Pause==null||session.Walker==null)throw new InvalidOperationException("Session/UI not ready");
            string suffix=session.TestSaveSuffix;
            if(string.IsNullOrWhiteSpace(suffix)||suffix.Length<4||suffix.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-'))
                throw new InvalidOperationException("Explicit isolated alphanumeric TestSaveSuffix required; production save never tested");
            if(!session.OpeningJourneyActive||session.OpeningCommissionReceived||session.Content.Opening==null)
                throw new InvalidOperationException("Fresh village-opening diagnostic slot required; do not erase an existing save to satisfy this probe");
            caller=session.GetComponent<WorldMacroPalanquinSummon>();
            if(caller==null||caller.Vehicle==null||caller.Seat==null)throw new InvalidOperationException("Caller not wired");
            car=caller.Vehicle;seat=caller.Seat;
            if(seat.Occupied||session.Walker.Seated||car.DriverPresent||car.Speed>.15f)throw new InvalidOperationException("Start while unseated and car parked");
            if(ui.Busy||ui.Settings.IsPreviewing)throw new InvalidOperationException("UI must not be loading or confirming settings");
            oldFeet=session.Walker.Body.transform.position;oldYaw=session.Walker.Body.transform.eulerAngles.y;
            oldCar=car.transform.position;oldCarRotation=car.transform.rotation;oldVelocity=car.Body.linearVelocity;oldAngular=car.Body.angularVelocity;
            profile=car.Profile;carId=car.GetInstanceID();callsBefore=caller.SuccessfulCalls;currencyBefore=session.Progress.ledger.currency;
            oldRun=Application.runInBackground;oldPage=ui.Page;Application.runInBackground=true;
            report=new Report{status="RUNNING",slot=session.Content.SaveSlot+suffix,originalPlayer=oldFeet,originalCar=oldCar};
            active=true;phase=0;deadline=EditorApplication.timeSinceStartup+45;readyAt=EditorApplication.timeSinceStartup+.15;
            ui.OpenPage("차패");report.phase="pre-commission UI";EditorApplication.update+=Tick;return "RUNNING isolated village/car API integration; about 5-15 seconds";
        }
        static void Check(bool okay,string name,string detail)
        {
            report.checks.Add(new CheckResult{name=name,status=okay?"PASS":"FAIL",detail=detail});
            if(!okay)throw new InvalidOperationException(name+": "+detail);
        }
        static Button CallButton()=>ui.GetComponentsInChildren<Button>(true).FirstOrDefault(x=>x.name=="SummonPalanquin");
        static void Next(int value,string label,double wait=.15){phase=value;report.phase=label;readyAt=EditorApplication.timeSinceStartup+wait;}
        static bool InputReady=>!ui.Pause.IsPaused&&!ui.Pause.Gate.InputBlocked&&Time.timeScale>.0001f;
        static bool CarSame(Vector3 p,Quaternion q)=>Vector3.Distance(car.transform.position,p)<.001f&&Quaternion.Angle(car.transform.rotation,q)<.01f;
        static void Tick()
        {
            if(!active)return;
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play mode ended");
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Integration timed out; neutral controls and Unity focus may be required");
                if(EditorApplication.timeSinceStartup<readyAt)return;
                switch(phase)
                {
                    case 0:
                        var locked=CallButton();Check(ui.Page=="차패"&&locked!=null&&!locked.interactable,"commission gate in actual UI","button locked before commission");
                        var p=car.transform.position;var q=car.transform.rotation;
                        locked.onClick.Invoke();Check(CarSame(p,q)&&caller.SuccessfulCalls==callsBefore,"locked callback does not move car","actual UI delegate invoked; input interactability alone not relied on");
                        Check(!session.TryRecordOpeningVehicleSummoned(out _)&&!session.OpeningVehicleSummoned,"pre-commission vehicle marker denied","actual Session API");
                        ui.CloseMenu();Next(1,"approach commission");break;
                    case 1:
                        if(!InputReady)return;
                        bool positioned=false;var point=session.Content.Opening.Commission;
                        for(int i=0;i<12;i++)
                        {
                            float angle=i*Mathf.PI/6;var candidate=point.Position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Mathf.Min(1.65f,point.Radius*.7f);
                            if(!session.TrySafeFeet(candidate,out var feet))continue;
                            session.Teleport(feet,Quaternion.LookRotation(point.Position-feet).eulerAngles.y);Physics.SyncTransforms();
                            if(session.CanInteract(WorldMacroOpeningProfileSO.CommissionId)){positioned=true;break;}
                        }
                        Check(positioned,"actual commission access","test-only safe teleport; no walking/input synthesis");
                        bool before=session.OpeningCommissionReceived;
                        Check(session.Interact(WorldMacroOpeningProfileSO.CommissionId)&&!before&&session.OpeningCommissionReceived,"commission interaction changes stage","actual Session.Interact(village_commission)");
                        Check(session.Progress.ledger.currency==currencyBefore,"commission does not mint currency","reward remains zero");
                        ui.OpenPage("차패");Next(2,"vehicle menu failures");break;
                    case 2:
                        Check(ui.Page=="차패"&&CallButton()!=null&&CallButton().interactable,"commission unlocks actual call button","same UI page");
                        p=car.transform.position;q=car.transform.rotation;car.Body.linearVelocity=Vector3.forward;
                        bool moving=caller.TrySummonFromMenu(ui.Pause,out var movingReason);car.Body.linearVelocity=Vector3.zero;
                        Check(!moving&&CarSame(p,q),"moving car denied without relocation",movingReason);
                        car.SetDriverPresent(true);bool driver=caller.TrySummonFromMenu(ui.Pause,out var driverReason);car.SetDriverPresent(false);
                        Check(!driver&&CarSame(p,q),"driver ownership denied without relocation",driverReason);
                        // Full actor grounding needs one resumed physics step after the diagnostic teleport.
                        ui.CloseMenu();Next(3,"settle player before call",.25);break;
                    case 3:
                        if(!InputReady||!session.Walker.Motor.IsLocomotionGrounded)return;
                        ui.OpenPage("차패");Next(4,"actual call button");break;
                    case 4:
                        var heading=session.Walker.Body.transform.forward;CallButton().onClick.Invoke();
                        Check(caller.SuccessfulCalls==callsBefore+1,"actual call button relocates existing car",caller.LastResult);
                        Check(Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsSortMode.None).Length==1&&car.GetInstanceID()==carId,"single original instance","no duplicate vehicle");
                        Check(car.Profile==profile,"drive profile preserved","same object reference");
                        Check(Vector3.Dot(Vector3.ProjectOnPlane(car.transform.forward,Vector3.up).normalized,Vector3.ProjectOnPlane(heading,Vector3.up).normalized)>=-.001f,"road-aligned forward facing","does not face backwards relative to caller");
                        Check(session.TryRecordOpeningVehicleSummoned(out var error)&&session.OpeningVehicleSummoned&&session.SaveNow(out error),"vehicle stage saved",error??"actual marker and SaveNow succeeded");
                        string testSave=Path.Combine(Application.persistentDataPath,session.Content.SaveSlot+session.TestSaveSuffix+".json");
                        var disk=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(testSave));
                        Check(disk.ledger.completed.Contains(WorldMacroOpeningProfileSO.CommissionId)&&disk.ledger.completed.Contains(WorldMacroPlaytestSession.OpeningVehicleSummonedId),"diagnostic save readback","commission and vehicle markers persisted to isolated slot; no app relaunch claimed");
                        report.summonedCar=car.transform.position;
                        ui.OpenPage("차패");p=car.transform.position;q=car.transform.rotation;
                        bool duplicate=caller.TrySummonFromMenu(ui.Pause,out var duplicateReason);
                        Check(!duplicate&&CarSame(p,q)&&caller.SuccessfulCalls==callsBefore+1,"immediate duplicate call denied",duplicateReason);
                        ui.CloseMenu();Next(5,"approach actual seat",.25);break;
                    case 5:
                        if(!InputReady)return;
                        var candidateList=new List<Vector3>();if(seat.ExitSockets!=null)candidateList.AddRange(seat.ExitSockets.Where(x=>x!=null).Select(x=>x.position));
                        for(int side=-1;side<=1;side+=2)for(int j=-1;j<=1;j++)candidateList.Add(car.transform.position+car.transform.right*car.Profile.ExitSideDistance*side+car.transform.forward*j*.7f);
                        positioned=false;
                        foreach(var candidate in candidateList)
                        {
                            if(!session.TrySafeFeet(candidate,out var feet))continue;
                            session.Teleport(feet,car.transform.eulerAngles.y);Physics.SyncTransforms();
                            if(Vector3.Distance(session.Walker.Body.bounds.center,seat.SeatSocket.position)<=car.Profile.BoardingDistance){positioned=true;break;}
                        }
                        Check(positioned,"safe actual boarding approach","diagnostic teleport near existing door sockets");Next(6,"board and deny occupied call",.25);break;
                    case 6:
                        if(!InputReady||!session.Walker.Motor.IsLocomotionGrounded)return;
                        Check(seat.TryBoard()&&seat.Occupied,"actual seat boarding",seat.LastInteraction);
                        ui.OpenPage("차패");p=car.transform.position;q=car.transform.rotation;
                        bool occupied=caller.TrySummonFromMenu(ui.Pause,out var occupiedReason);
                        Check(!occupied&&CarSame(p,q)&&seat.Occupied,"occupied car cannot be recalled",occupiedReason);
                        ui.CloseMenu();Next(7,"exit and cleanup",.2);break;
                    case 7:
                        if(!InputReady)return;
                        Check(seat.TryExit()&&!seat.Occupied&&!session.Walker.Seated,"actual seat exit restores walking",seat.LastInteraction);
                        Finish("PASS_API_INTEGRATION");break;
                }
            }
            catch(Exception e){report.failures.Add(e.ToString());Finish("FINDINGS");}
        }
        static void Finish(string status)
        {
            if(!active)return;active=false;EditorApplication.update-=Tick;
            try
            {
                // These restoration writes are allowed only because begin refused production slots.
                if(session!=null&&!string.IsNullOrWhiteSpace(session.TestSaveSuffix))
                {
                    if(ui!=null&&ui.Page.Length>0)ui.CloseMenu();
                    if(seat!=null&&seat.Occupied){bool prior=seat.enabled;seat.enabled=false;seat.enabled=prior;}
                    if(car!=null){car.SetDriverPresent(false);car.StopDriverInputForUi();car.Body.position=oldCar;car.Body.rotation=oldCarRotation;car.transform.SetPositionAndRotation(oldCar,oldCarRotation);car.Body.linearVelocity=oldVelocity;car.Body.angularVelocity=oldAngular;}
                    session.Teleport(oldFeet,oldYaw);Physics.SyncTransforms();
                    if(!session.SaveNow(out var error))report.failures.Add("Restore-position diagnostic save: "+error);
                    report.cleanup=car!=null&&CarSame(oldCar,oldCarRotation)&&Vector3.Distance(session.Walker.Body.transform.position,oldFeet)<.001f;
                    // Keep the diagnostic menu closed; re-opening the stale introduction would be misleading.
                }
                Application.runInBackground=oldRun;
            }
            catch(Exception e){report.failures.Add("Cleanup: "+e);report.cleanup=false;}
            report.status=report.failures.Count==0&&report.cleanup?status:"FINDINGS";report.phase="finished";
            Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,JsonUtility.ToJson(report,true));
        }
    }
}
