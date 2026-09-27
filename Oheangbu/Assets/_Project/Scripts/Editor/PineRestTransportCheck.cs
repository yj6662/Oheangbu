using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static partial class PineRestGameBuilder
    {
        const string TransportReport = "../Art/World/PineRest/escort_load_checks.txt";
        static readonly List<string> transportChecks = new List<string>();
        static double transportAt;
        static int transportStep;
        static Keyboard transportKeyboard, priorTransportKeyboard;
        static Key transportKey;
        static Vector3 transportStart, transportSavedPosition, transportExitPosition;
        static bool[] transportVisuals;
        static void TransportInput()
        {
            if (transportKeyboard != null && InputState.currentUpdateType != InputUpdateType.BeforeRender)
                InputSystem.QueueStateEvent(transportKeyboard, transportKey == Key.None ? new KeyboardState() : new KeyboardState(transportKey));
        }
        [Serializable] sealed class TransportPose {public Vector3 position;}
        static string EscortLoadReloadCheck()
        {
            var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
            bool ok=s.Progress.escort.Stage==DemoEscortStage.Escorting&&(s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Waiting||s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Following)&&!s.JourneySeated&&s.EscortCompanion.parent!=s.EscortPassengerSocket&&s.EscortCargo.parent!=s.EscortCargoSocket&&Vector3.Distance(s.Player.position,s.EscortCompanion.position)<8&&Vector3.Distance(s.Player.position,s.JourneySeat.Vehicle.transform.position)<8&&Vector3.Distance(s.JourneySeat.Vehicle.transform.position,JsonUtility.FromJson<TransportPose>(SessionState.GetString("JourneyTransportSavedPose","{}")).position)<.3f;
            var result=(ok?"PASS ":"FAIL ")+"new Play restores parked vehicle, player and both companions together on foot";File.WriteAllText("../Art/World/PineRest/escort_load_reload.txt",result);return result;
        }
        static string TransportCheck()
        {
            var s = Object.FindFirstObjectByType<PrologueSession>();
            if (!EditorApplication.isPlaying || s?.JourneySeat == null || !s.TestSaveSuffix.StartsWith("-audit-") || s.HasGuk) throw new Exception("Fresh isolated Journey required");
            transportChecks.Clear(); transportStep = 0; transportAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += TransportTick; File.WriteAllText(TransportReport, "RUNNING"); return "Journey boarding and physical drive audit started";
        }
        static void TransportTick()
        {
            if (EditorApplication.timeSinceStartup < transportAt) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play stopped");
                var s = Object.FindFirstObjectByType<PrologueSession>(); var seat = s.JourneySeat; var vehicle = seat.Vehicle; var walker = seat.CombatWalker;
                void Check(bool pass, string label) => transportChecks.Add((pass ? "PASS " : "FAIL ") + label);
                if (transportStep == 0)
                {
                    Check(!seat.BoardingAllowed(), "boarding locked before Cheongryong and W1");
                    CommissionCheck(); transportStep++; transportAt = EditorApplication.timeSinceStartup + .5; return;
                }
                if (transportStep == 1)
                {
                    s.Encounters.Single(a => a.Id == "cheongryong").GetComponent<EnemyVitals>().TakeDamage(100000);
                    Check(!seat.BoardingAllowed(), "boss alone does not satisfy W1 boarding gate");
                    var point = s.Content.Points.Single(p => p.Id == "wangso_w1"); s.Teleport(point.Position + Vector3.up, 0); s.Interact(point.Id);
                    var ground = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c => c.name == "Valley");
                    var entry = vehicle.transform.position + vehicle.transform.right * 2.1f;
                    if (!ground.Raycast(new Ray(entry + Vector3.up * 20, Vector3.down), out var hit, 40)) throw new Exception("Boarding support absent");
                    s.Teleport(hit.point + Vector3.up * (walker.Body.height * .5f - walker.Body.center.y + .12f), 270);
                    transportStep++; transportAt = EditorApplication.timeSinceStartup + 1; return;
                }
                if (transportStep == 2)
                {
                    Check(vehicle.IsConfigured && vehicle.GroundedWheelCount >= 3, "configured rigid vehicle supported by wheels: " + vehicle.GroundedWheelCount);
                    transportVisuals = walker.Visuals.Select(r => r != null && r.enabled).ToArray();
                    s.Save(); transportSavedPosition = s.Progress.position;
                    var npcPosition=s.EscortCompanion.position;var cargoPosition=s.EscortCargo.position;
                    s.EscortCargo.gameObject.SetActive(false);Check(!seat.TryBoard(),"missing cargo rejects boarding");s.EscortCargo.gameObject.SetActive(true);
                    var storeField=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var original=storeField.GetValue(s);
                    var blocker=Path.GetFullPath("../Art/World/PineRest/load_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit only");
                    try{storeField.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));bool failed=seat.TryBoard();Check(!failed&&!s.JourneySeated&&walker.Body.enabled&&s.Progress.escort.Stage==DemoEscortStage.Contracted&&!s.Progress.completed.Contains("escort_start")&&Vector3.Distance(npcPosition,s.EscortCompanion.position)<.001f&&Vector3.Distance(cargoPosition,s.EscortCargo.position)<.001f,"failed departure save restores player, cargo and NPC with no published departure");}
                    finally{storeField.SetValue(s,original);File.Delete(blocker);}
                    bool boarded = seat.TryBoard();
                    Check(s.Progress.escort.Stage==DemoEscortStage.Escorting&&s.Progress.completed.Contains("escort_start")&&s.EscortCompanion.IsChildOf(s.EscortPassengerSocket)&&s.EscortCargo.IsChildOf(s.EscortCargoSocket),"loaded seat ownership commits real escort departure");
                    Check(boarded && s.JourneySeated && walker.Seated && !walker.Body.enabled && !walker.Drawing.enabled && !walker.CameraRig.enabled, "boarding transfers input and camera ownership: " + seat.LastInteraction);
                    if (!boarded) throw new Exception("Board failed; body=" + walker.Body.transform.position + " seat=" + seat.SeatSocket.position + " can=" + seat.CanBoard + " speed=" + vehicle.Speed);
                    transportStart = vehicle.transform.position;
                    priorTransportKeyboard = Keyboard.current; transportKeyboard = InputSystem.AddDevice<Keyboard>("JourneyVehicleAudit"); transportKeyboard.MakeCurrent();
                    transportKey = Key.W; InputSystem.onBeforeUpdate += TransportInput;
                    transportStep++; transportAt = EditorApplication.timeSinceStartup + 2; return;
                }
                if (transportStep == 3)
                {
                    Check(Vector3.Distance(transportStart, vehicle.transform.position) > .25f && vehicle.Speed > .1f, "virtual W drives real rigid vehicle: metres=" + Vector3.Distance(transportStart, vehicle.transform.position).ToString("F2") + " speed=" + vehicle.Speed.ToString("F2"));
                    Check(walker.Visuals.All(r=>r==null||!r.enabled),"player visual remains hidden across seated animation updates");
                    transportKey = Key.Space; transportStep++; transportAt = EditorApplication.timeSinceStartup + 2; return;
                }
                if (transportStep == 4)
                {
                    Check(vehicle.Speed <= vehicle.Profile.ExitMaximumSpeed, "brake reaches safe exit speed: " + vehicle.Speed.ToString("F3"));
                    var menu = s.GetComponent<ProloguePauseMenu>(); menu.SetOpen(true);
                    Check(seat.RuntimeState != null && seat.RuntimeState.InputBlocked && !seat.TryExit(), "pause blocks driver interaction");
                    s.Save(); Check(s.Progress.hasVehicle&&Vector3.Distance(s.Progress.vehiclePosition,vehicle.Body.position)<.1f&&Vector3.Distance(s.Progress.position,vehicle.transform.position)<6, "seated save pairs current vehicle and safe nearby player exit");
                    menu.SetOpen(false); transportKey = Key.None;
                    transportStep++; transportAt = EditorApplication.timeSinceStartup + .5; return;
                }
                if (transportStep == 5)
                {
                    var storeField=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var original=storeField.GetValue(s);
                    var blocker=Path.GetFullPath("../Art/World/PineRest/exit_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit only");
                    try{storeField.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));Check(seat.TryExit()&&!walker.Seated&&walker.Body.enabled&&walker.Drawing.enabled&&walker.CameraRig.enabled,"safe exit restores walking camera and controls");Check(s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding&&s.EscortCompanion.IsChildOf(s.EscortPassengerSocket),"failed disembark save retains passenger until retry");}
                    finally{storeField.SetValue(s,original);File.Delete(blocker);}
                    Check(walker.Visuals.Select((r, i) => r == null || r.enabled == transportVisuals[i]).All(v => v), "exit restores prior renderer visibility exactly");
                    Check(s.Progress.escort.Stage == DemoEscortStage.Escorting, "driving does not fabricate inspection or delivery");
                    transportExitPosition=s.Player.position;s.Teleport(transportExitPosition+Vector3.back*12,0);
                    transportStep=50; transportAt = EditorApplication.timeSinceStartup + .7; return;
                }
                if(transportStep==50){Check((s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Waiting||s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Following)&&!s.EscortCompanion.IsChildOf(vehicle.transform)&&Vector3.Distance(s.Player.position,vehicle.transform.position)>8,"exit retry unloads companions beside vehicle even after player moves away (audit teleport)");s.Teleport(transportExitPosition+Vector3.up*.1f,270);transportStep=6;transportAt=EditorApplication.timeSinceStartup+.5;return;}
                if(transportStep==6){
                Check((s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Waiting||s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Following)&&!s.EscortCompanion.IsChildOf(vehicle.transform)&&!s.EscortCargo.IsChildOf(vehicle.transform),"restored store retries exit and safely unloads both actors");
                Check(walker.Body.isGrounded && !s.JourneySeated, "exited player capsule settles on ground");
                if(s.EscortFollowProfile!=null){
                    var storeField=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var original=storeField.GetValue(s);var carryParent=s.EscortCargo.parent;var blocker=Path.GetFullPath("../Art/World/PineRest/reboard_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit only");
                    bool eligible=seat.CanBoard&&s.EscortCarryAttached;
                    try{storeField.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));bool boarded=seat.TryBoard();Check(eligible&&!boarded&&s.EscortCarryAttached&&s.EscortCargo.parent==carryParent&&s.EscortCargo.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"failed reboard restores carrying parent and disabled cargo collisions");}
                    finally{storeField.SetValue(s,original);File.Delete(blocker);}
                }
                bool reboarded = seat.TryBoard(); Check(reboarded, "nearby grounded player can reboard");
                if(!reboarded)throw new Exception("Cannot test moving death without boarding");
                transportStart=vehicle.transform.position;transportKey=Key.W;transportStep=60;transportAt=EditorApplication.timeSinceStartup+2;return;
                }
                if(transportStep==60){transportKey=Key.Space;transportStep=61;transportAt=EditorApplication.timeSinceStartup+1.5;return;}
                if(transportStep==61){
                bool safe=seat.TryGetSafeExit(out var deathExit);
                Check(safe&&vehicle.GroundedWheelCount>=3&&Vector3.Distance(vehicle.transform.position,transportStart)>3,"death test vehicle actually moved beyond previous boarding position");
                int currency=s.Progress.currency;
                s.Player.GetComponent<PlayerVitals>().ApplyFatalFall();
                Check(!s.JourneySeated && walker.Body.enabled && walker.CameraRig.enabled && Vector3.Distance(s.Player.position,s.Progress.checkpointPosition)<.05f,"seated death releases camera and returns to committed checkpoint");
                Check(safe&&Vector3.Distance(s.Progress.dropPosition,deathExit)<.1f&&s.Progress.dropCurrency==currency&&currency>0&&s.Progress.currency==0,"seated death drops all currency at current supported exit instead of old boarding position");
                Check(s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Waiting&&s.EscortCompanion.parent!=s.EscortPassengerSocket&&Vector3.Distance(s.EscortCompanion.position,s.EscortDepartureAnchor.position)<8,"death commits waiting companion and parked vehicle recovery");
                var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");ground.Raycast(new Ray(vehicle.transform.position+Vector3.right*2.1f+Vector3.up*20,Vector3.down),out var hit,40);s.Teleport(hit.point+Vector3.up*1.1f,270);
                transportStep=7;transportAt=EditorApplication.timeSinceStartup+1;return;
                }
                if(transportStep==7){bool finalBoard=seat.TryBoard();Check(finalBoard&&s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding,"recovered escort boards again before reload drive");transportStart=vehicle.transform.position;transportKey=Key.W;transportStep++;transportAt=EditorApplication.timeSinceStartup+1;return;}
                if(transportStep==8){transportKey=Key.Space;transportStep++;transportAt=EditorApplication.timeSinceStartup+1.5;return;}
                s.Save();SessionState.SetString("JourneyTransportSavedPose",JsonUtility.ToJson(new TransportPose{position=vehicle.transform.position}));Check(s.JourneySeated&&Vector3.Distance(vehicle.transform.position,transportStart)>.5f&&Vector3.Distance(s.Progress.vehiclePosition,vehicle.transform.position)<.1f,"persist moved vehicle while riding for new-Play recovery test");
                FinishTransportCheck();
            }
            catch (Exception e) { transportChecks.Add("FAIL " + e); FinishTransportCheck(); }
        }
        static void FinishTransportCheck()
        {
            EditorApplication.update -= TransportTick; InputSystem.onBeforeUpdate -= TransportInput;
            if (transportKeyboard != null) { InputSystem.RemoveDevice(transportKeyboard); transportKeyboard = null; }
            if (priorTransportKeyboard != null && priorTransportKeyboard.added) priorTransportKeyboard.MakeCurrent();
            transportChecks.Add("Direct boarding/exit with virtual W/Space and real physics; not manual road traversal, inspection/delivery or visual approval.");
            File.WriteAllText(TransportReport, string.Join("\n", transportChecks));
        }
    }
}
