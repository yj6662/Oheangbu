using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad]public static class WorldMacroQueue
    {
        [Serializable]class Request{public string id,method,argument;}
        [Serializable]class Response{public string status,result,error;}
        static double next;
        static WorldMacroQueue(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.25;
            string dir=WorldMacroBuilder.Output,path=dir+"/command.json";if(!File.Exists(path))return;
            var r=JsonUtility.FromJson<Request>(File.ReadAllText(path));if(r==null||string.IsNullOrEmpty(r.id))return;File.Move(path,dir+"/request_"+r.id+".json");var reply=new Response();
            try{
                switch(r.method){
                    case "RecoveryStatus":reply.result=WorldMacroRecoveryStatus.Read();break;
                    case "ContentReviewOpen":reply.result=EditorApplication.ExecuteMenuItem("Tools/오행부/전체맵 콘텐츠 위치")?"Opened content location review window":"Content review menu unavailable";break;
                    case "ContentInstall":reply.result=WorldMacroContentAuthoring.Install();break;
                    case "ContentAudit":reply.result=WorldMacroContentAuthoring.Audit();break;
                    case "ContentVisit":reply.result=WorldMacroContentAuthoring.Visit(r.argument);break;
                    case "ContentRepair":reply.result=WorldMacroContentAuthoring.Repair();break;
                    case "ContentCapture":reply.result=WorldMacroContentAuthoring.Capture(r.argument);break;
                    case "ContentPlayChecks":reply.result=WorldMacroContentAuthoring.PlayChecks();break;
                    case "Build":reply.result=WorldMacroBuilder.Build();break;
                    case "Rebuild":reply.result=WorldMacroBuilder.Rebuild();break;
                    case "Reseed":
                        if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
                        if(File.ReadAllText(WorldMacroBuilder.ScenePath).Contains("Macro_PlayerCapsule"))throw new Exception("Authored macro content present; full reseed is disabled to preserve player and landmarks.");
                        Directory.CreateDirectory(dir+"/Backups");
                        File.WriteAllText(dir+"/Backups/Sheet_"+DateTime.Now.ToString("yyyyMMddHHmmss")+".json",JsonUtility.ToJson(WorldMacroBuilder.Sheet,true));
                        WorldMacroBuilder.Save(WorldMacroSeed.Create(),"WorldMacroSheet.asset");reply.result=WorldMacroBuilder.Rebuild();break;
                    case "Export":reply.result=WorldMacroBuilder.Export();break;
                    case "Audit":reply.result=WorldMacroAudit.Audit();break;
                    case "FlowAudit":reply.result=WorldMacroTraversalAudit.Audit();break;
                    case "FlowProbe":reply.result=WorldMacroTraversalAudit.Probe();break;
                    case "SkyInstall":reply.result=WorldMacroSkyAuthoring.Install();break;
                    case "SkyAudit":reply.result=WorldMacroSkyAuthoring.Audit();break;
                    case "SkyCapture":reply.result=WorldMacroSkyAuthoring.Capture(r.argument);break;
                    case "SkyLiveBegin":reply.result=WorldMacroSkyAuthoring.LiveBegin();break;
                    case "SkyLiveEnd":reply.result=WorldMacroSkyAuthoring.LiveEnd();break;
                    case "WaterInstall":reply.result=WorldMacroWaterAuthoring.Install();break;
                    case "PlayerInstall":reply.result=WorldMacroPlayerAuthoring.Install();break;
                    case "PalanquinInstall":reply.result=WorldMacroPalanquinAuthoring.Install();break;
                    case "MagicCarInstall":reply.result=MagicStoneCarAuthoring.Install();break;
                    case "MagicCarAudit":reply.result=MagicStoneCarAuthoring.Audit();break;
                    case "MagicCarSurfaceLook":reply.result=MagicStoneCarAuthoring.ApplyVehicleSurfaceLook();break;
                    case "MagicCarCapture":reply.result=MagicStoneCarReviewTools.Capture(r.argument);break;
                    case "MagicCarCamera":reply.result=MagicStoneCarReviewTools.CameraAction(r.argument);break;
                    case "MagicCarSnapshot":reply.result=MagicStoneCarReviewTools.Snapshot();break;
                    case "MagicCarCameraChecks":reply.result=MagicStoneCarRuntimeChecks.CameraChecks();break;
                    case "MagicCarDriveChecksBegin":reply.result=MagicStoneCarRuntimeChecks.BeginDriveStateChecks();break;
                    case "MagicCarDriveChecksPoll":reply.result=MagicStoneCarRuntimeChecks.Poll();break;
                    case "MagicCarDriveChecksEnd":reply.result=MagicStoneCarRuntimeChecks.End();break;
                    case "MagicCarIgnitionBegin":reply.result=MagicStoneCarIgnitionCapture.Begin();break;
                    case "MagicCarIgnitionPoll":reply.result=MagicStoneCarIgnitionCapture.Poll();break;
                    case "MagicCarIgnitionEnd":reply.result=MagicStoneCarIgnitionCapture.End();break;
                    case "DressingInstallRegion":reply.result=WorldMacroDressingAuthoring.InstallRegion(r.argument);break;
                    case "DressingPrepareAssets":reply.result=WorldMacroDressingAuthoring.PrepareAssetsOnly();break;
                    case "DressingValidate":reply.result=WorldMacroDressingAuthoring.Validate();break;
                    case "DressingGrounding":reply.result=WorldMacroDressingGrounding.Repair();break;
                    case "DressingCapture":
                        var dressingView=JsonUtility.FromJson<DressingView>(r.argument);
                        reply.result=WorldMacroDressingProbe.Capture(dressingView.label,dressingView.dressing,dressingView.position.x,dressingView.position.y,dressingView.position.z,dressingView.target.x,dressingView.target.y,dressingView.target.z);break;
                    case "DressingMeasureBegin":
                        var dressingMeasure=JsonUtility.FromJson<DressingView>(r.argument);
                        reply.result=WorldMacroDressingProbe.BeginMeasure(dressingMeasure.label,dressingMeasure.dressing,120,dressingMeasure.rebuildEachFrame);break;
                    case "DressingMeasurePoll":reply.result=WorldMacroDressingProbe.Poll();break;
                    case "DressingMeasureStop":reply.result=WorldMacroDressingProbe.Stop();break;
                    case "DressingPlan":reply.result=WorldMacroDressingReview.PreparePlan();break;
                    case "DressingCapturePlanned":
                        var dressingPlanned=JsonUtility.FromJson<DressingView>(r.argument);
                        reply.result=WorldMacroDressingReview.CapturePlanned(dressingPlanned.label,dressingPlanned.dressing);break;
                    case "DressingReview":reply.result=WorldMacroDressingReview.WriteReview();break;
                    case "PalanquinRunningGear":reply.result=WorldMacroPalanquinAuthoring.AddRunningGear();break;
                    case "PalanquinAudit":reply.result=WorldMacroPalanquinAuthoring.Audit();break;
                    case "PalanquinCapture":reply.result=WorldMacroPalanquinAuthoring.Capture(r.argument);break;
                    case "PalanquinProbeBegin":reply.result=WorldMacroPalanquinProbe.Begin();break;
                    case "PalanquinProbePoll":reply.result=WorldMacroPalanquinProbe.Poll();break;
                    case "PalanquinProbeEnd":reply.result=WorldMacroPalanquinProbe.End();break;
                    case "PalanquinBoard":reply.result=WorldMacroPalanquinProbe.TryBoard();break;
                    case "PalanquinExit":reply.result=WorldMacroPalanquinProbe.TryExit();break;
                    case "PalanquinVisit":
                        var walker=UnityEngine.Object.FindFirstObjectByType<Oheangbu.App.World.WorldMacroReviewController>();
                        if(walker==null||walker.InspectionStops==null||walker.InspectionStops.Length<5||walker.InspectionStops[4]==null)throw new Exception("Installed palanquin inspection stop required");
                        walker.VisitStop(4);if(!EditorApplication.isPlaying)WorldMacroWaterAuthoring.SaveScene();reply.result="Capsule at palanquin entry; E boards in Play";break;
                    case "LandmarkInstall":reply.result=WorldMacroLandmarkAuthoring.Install(r.argument);break;
                    case "LandmarkReposition":reply.result=WorldMacroLandmarkAuthoring.Reposition(r.argument);break;
                    case "LandmarkCapture":reply.result=WorldMacroLandmarkCapture.Capture(r.argument);break;
                    case "LandmarkVisit":reply.result=WorldMacroLandmarkAuthoring.Visit(r.argument);break;
                    case "LandmarkRefine":reply.result=WorldMacroLandmarkAuthoring.RefineSurfaces();break;
                    case "LandmarkAudit":reply.result=WorldMacroLandmarkAuthoring.Audit();break;
                    case "WaterAudit":reply.result=WorldMacroWaterAuthoring.Audit();break;
                    case "WaterDump":reply.result=WorldMacroWaterAuthoring.Dump();break;
                    case "WaterDepthProbe":reply.result=WorldMacroWaterAuthoring.DepthProbe();break;
                    case "WaterTune":reply.result=WorldMacroWaterAuthoring.Tune();break;
                    case "WaterCapture":reply.result=WorldMacroWaterAuthoring.Capture(r.argument);break;
                    case "WaterLiveBegin":reply.result=WorldMacroWaterAuthoring.LiveBegin(r.argument=="pause");break;
                    case "WaterLiveEnd":reply.result=WorldMacroWaterAuthoring.LiveEnd();break;
                    case "MotionBegin":reply.result=WorldMacroMotionProbe.Begin();break;
                    case "MotionPoll":reply.result=WorldMacroMotionProbe.Poll();break;
                    case "Capture":reply.result=WorldMacroAudit.Capture(r.argument);break;
                    case "Probe":reply.result=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path+" playing="+EditorApplication.isPlaying+" commit="+Prologue.PrologueAudit.CommitRatio();break;
                    case "Cleanup":if(EditorApplication.isPlaying)throw new Exception("Edit mode required");GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();reply.result="Released unused assets; commit="+Prologue.PrologueAudit.CommitRatio();break;
                    case "UnloadForModels":
                        if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new Exception("Saved macro scene in Edit mode required");
                        WorldMacroWaterAuthoring.SaveScene();
                        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
                        Selection.activeObject=null;GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
                        reply.result="Macro saved and temporarily unloaded for external model preparation; commit="+Prologue.PrologueAudit.CommitRatio();break;
                    case "RestoreMacro":
                        if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Clean Edit scene required");
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(WorldMacroBuilder.ScenePath);reply.result="Restored authored macro scene";break;
                    case "CloseForModels":
                        if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Saved clean Edit scene required");
                        if(SessionState.GetBool("MacroSavedWorkerCount",false))AssetDatabase.DesiredWorkerCount=SessionState.GetInt("MacroPriorWorkerCount",1);
                        File.WriteAllText(dir+"/editor_model_pause.json",JsonUtility.ToJson(new EditorPause{executable=EditorApplication.applicationPath,project=Directory.GetParent(Application.dataPath).FullName,scene=WorldMacroBuilder.ScenePath,utc=DateTime.UtcNow.ToString("o")},true));
                        double closeAt=EditorApplication.timeSinceStartup+1;EditorApplication.CallbackFunction close=null;close=()=>{if(EditorApplication.timeSinceStartup<closeAt)return;EditorApplication.update-=close;EditorApplication.Exit(0);};EditorApplication.update+=close;
                        reply.result="Saved project pause recorded; closing this editor only for Blender memory headroom";break;
                    case "SingleImportWorker":
                        if(!SessionState.GetBool("MacroSavedWorkerCount",false)){SessionState.SetInt("MacroPriorWorkerCount",AssetDatabase.DesiredWorkerCount);SessionState.SetBool("MacroSavedWorkerCount",true);}
                        AssetDatabase.DesiredWorkerCount=r.argument=="park"?0:1;AssetDatabase.ForceToDesiredWorkerCount();reply.result="Import worker target="+AssetDatabase.DesiredWorkerCount+"; idle workers released through Unity API";break;
                    case "RestoreImportWorkers":
                        if(SessionState.GetBool("MacroSavedWorkerCount",false)){AssetDatabase.DesiredWorkerCount=SessionState.GetInt("MacroPriorWorkerCount",1);SessionState.SetBool("MacroSavedWorkerCount",false);}
                        reply.result="Prior import concurrency restored";break;
                    case "Play":EditorApplication.isPlaying=true;reply.result="Starting macro inspection";break;
                    case "Stop":EditorApplication.isPlaying=false;reply.result="Stopping macro inspection";break;
                    default:throw new Exception("Unknown macro method "+r.method);
                }reply.status="COMPLETE";
            }catch(Exception e){reply.status="FAILED";reply.error=e.ToString();}
            File.WriteAllText(dir+"/response_"+r.id+".json",JsonUtility.ToJson(reply,true));
        }
        [Serializable]class EditorPause{public string executable,project,scene,utc;}
        [Serializable]class DressingView{public string label;public bool dressing,rebuildEachFrame;public Vector3 position,target;}
    }
}
