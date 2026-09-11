using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    [InitializeOnLoad]
    public static class Vfx120Queue
    {
        [Serializable] class Command { public string id, method, request; }
        [Serializable] class Response { public string id, status, result, error; }
        static double next;
        static Vfx120Queue() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .25;
            string path = Path.Combine(Vfx120Editor.Output, "command.json");
            if (!File.Exists(path)) return;
            Command command;
            try { command = JsonUtility.FromJson<Command>(File.ReadAllText(path)); } catch (IOException) { return; }
            if (command == null || string.IsNullOrEmpty(command.id)) return;
            var response = new Response { id = command.id };
            File.Move(path, Path.Combine(Vfx120Editor.Output, "request_" + command.id + ".json"));
            try
            {
                switch (command.method)
                {
                    case "Probe": response.result = "Unity=" + Application.unityVersion + "; playing=" + EditorApplication.isPlaying + "; scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; break;
                    case "ReviewMemoryCleanup":
                        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop review before cleanup");
                        GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
                        response.result="Unused review assets released; scene not saved or changed";break;
                    case "ReviewDrainWorkers":
                        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop review before draining idle import workers");
                        int priorWorkers=AssetDatabase.DesiredWorkerCount;
                        try{AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();}
                        finally{AssetDatabase.DesiredWorkerCount=priorWorkers;}
                        response.result="Idle import workers drained; desired count restored to "+priorWorkers;break;
                    case "Refresh": AssetDatabase.Refresh(); response.result = "Refreshed"; break;
                    case "FixedWardBuild": response.result=FixedWardBuild.Build(command.request);break;
                    case "WoodDeerBuild": response.result=WoodDeerBuild.Build();break;
                    case "FireHaetaeBuild": response.result=FireHaetaeBuild.Build();break;
                    case "MetalTigerBuild": response.result=MetalTigerBuild.Build();break;
                    case "StoneJangseungBuild": response.result=StoneJangseungBuild.Build();break;
                case "StoneJangseungAudit": response.result=StoneJangseungReview.Audit();break;
                case "StoneJangseungCapture": response.result=StoneJangseungReview.Start();break;
                case "StoneJangseungCaptureExternal": response.result=StoneJangseungReview.Start(true);break;
                case "DokkaebiClubBuild": response.result=DokkaebiClubBuild.Build();break;
                    case "DokkaebiClubAudit": response.result=DokkaebiClubReview.Audit();break;
                    case "DokkaebiClubCapture": response.result=DokkaebiClubReview.Start();break;
                    case "DokkaebiClubCaptureExternal": response.result=DokkaebiClubReview.Start(true);break;
                    case "WaterTurtleBuild": response.result=WaterTurtleBuild.Build();break;
                    case "WaterTurtleAudit": response.result=WaterTurtleReview.Audit();break;
                    case "WaterTurtleCapture": response.result=WaterTurtleReview.Start();break;
                    case "WaterTurtleCaptureExternal": response.result=WaterTurtleReview.Start(true);break;
                    case "StoneDokkaebiBuild": response.result=StoneDokkaebiBuild.Build();break;
                    case "StoneDokkaebiAudit": response.result=StoneDokkaebiReview.Audit();break;
                    case "StoneDokkaebiCapture": response.result=StoneDokkaebiReview.Start();break;
                    case "StoneDokkaebiCaptureExternal": response.result=StoneDokkaebiReview.Start(true);break;
                    case "MetalTigerAudit": response.result=MetalTigerReview.Audit();break;
                    case "MetalTigerCapture": response.result=MetalTigerReview.Start();break;
                    case "MetalTigerCaptureExternal": response.result=MetalTigerReview.Start(true);break;
                    case "FireHaetaeAudit": response.result=FireHaetaeReview.Audit();break;
                    case "FireHaetaeCapture": response.result=FireHaetaeReview.Start();break;
                    case "FireHaetaeCaptureExternal": response.result=FireHaetaeReview.Start(true);break;
                    case "WoodDeerAudit": response.result=WoodDeerReview.Audit();break;
                    case "WoodDeerCapture": response.result=WoodDeerReview.Start();break;
                    case "FixedWardAudit": response.result=FixedWardReview.Audit(command.request);break;
                    case "FixedWardCapture": response.result=FixedWardReview.Start(command.request);break;
                    case "AreaFiveBuild": response.result = KtpAreaFiveBuild.Build(command.request); break;
                    case "AreaNaturalBuild": response.result=KtpAreaNaturalBuild.Build(command.request);break;
                    case "AreaNaturalCapture": response.result=KtpAreaNaturalCapture.Start(command.request);break;
                    case "AreaNaturalAudit": response.result=KtpAreaNaturalBuild.Audit();break;
                    case "AreaBranchBuild": response.result=KtpAreaBranchBuild.Build(command.request);break;
                    case "AreaBranchCapture": response.result=KtpAreaBranchCapture.Start(command.request);break;
                    case "AreaBranchAudit": response.result=KtpAreaBranchBuild.Audit();break;
                    case "AreaRiftBuild": response.result = KtpAreaRiftBuild.Build(command.request); break;
                    case "AreaRiftCapture": response.result = KtpAreaRiftCapture.Start(command.request); break;
                    case "AreaRiftAudit": response.result = KtpAreaRiftAudit.Start(); break;
                    case "AreaRiftRegression": response.result = KtpAreaRiftRegression.Start(); break;
                    case "AreaFlowBuild": response.result = KtpAreaFlowBuild.Build(command.request); break;
                    case "AreaFlowCapture": response.result = KtpAreaFlowCapture.Start(command.request); break;
                    case "AreaFlowAudit": response.result = KtpAreaFlowAudit.Start(); break;
                    case "AreaFlowRegression": response.result = KtpAreaFlowRegression.Start(); break;
                    case "AreaWideBuild": response.result = KtpAreaWideBuild.Build(command.request); break;
                    case "AreaWideCapture": response.result = KtpAreaWideCapture.Start(command.request); break;
                    case "AreaWideAudit": response.result = KtpAreaWideAudit.Start(); break;
                    case "AreaWideRegression": response.result = KtpAreaWideRegression.Start(); break;
                    case "AreaFiveCapture": response.result = KtpAreaFiveCapture.Start(command.request); break;
                    case "AreaFiveAudit": response.result = KtpAreaFiveAudit.Start(); break;
                    case "AreaBambooSurface": response.result = KtpAreaFiveBuild.RepairBambooSurface(); break;
                    case "AreaRegressionAudit": response.result = KtpAreaRegressionAudit.Start(); break;
                    case "BasicSixBuild": response.result = KtpBasicSixBuild.Build(command.request); break;
                    case "BasicSixCapture": response.result = KtpBasicSixCapture.Start(command.request); break;
                    case "BasicSixAudit": response.result = KtpBasicSixAudit.Start(); break;
                    case "RectShieldBuild": response.result = KtpRectShieldBuild.Build(command.request); break;
                    case "OffsetGuardBuild": response.result = KtpOffsetGuardBuild.Build(command.request); break;
                    case "QuickCastBuild": response.result = KtpQuickCastBuild.Build(command.request); break;
                    case "EmphasisBuild": response.result = KtpEmphasisBuild.Build(command.request); break;
                    case "EmphasisCapture": response.result = KtpEmphasisCapture.Start(command.request); break;
                    case "EmphasisAudit": response.result = KtpEmphasisAudit.Start(); break;
                    case "OriginalBuild": response.result = KtpOriginalBuild.Build(command.request); break;
                    case "OriginalReview": response.result = KtpOriginalReview.Capture(command.request); break;
                    case "OriginalPlayAudit": response.result = KtpOriginalPlayAudit.Start(); break;
                    case "ContactBuild": response.result = KtpContactBuild.Build(); break;
                    case "ContactAudit": response.result = KtpContactAudit.Start(); break;
                    case "ParticleColorProbe": response.result = Vfx120ParticleColorProbe.Run(); break;
                    case "RibbonProbe": response.result = Vfx120ParticleColorProbe.RibbonProbe(); break;
                    case "RibbonCrossRender": response.result = Vfx120ParticleColorProbe.RibbonCrossRender(); break;
                    case "RepairMeshBuffers": response.result = Vfx120Editor.RepairMeshBuffers(); break;
                    case "ImportGuardianParts": response.result = Vfx120Editor.ImportGuardianParts(); break;
                    case "GuardianAudit": response.result = Vfx120GuardianAudit.Run(); break;
                    case "NativeMeshCopyProbe": response.result = Vfx120Editor.NativeMeshCopyProbe(); break;
                    case "PlayerInputSingle": response.result = Vfx120PlayerInputAudit.StartSingle(command.request); break;
                    case "PlayerInputAudit": response.result = Vfx120PlayerInputAudit.Start(); break;
                    case "PlayerInputBatch1": response.result = Vfx120PlayerInputAudit.StartBatch(1); break;
                    case "PlayerInputBatch2": response.result = Vfx120PlayerInputAudit.StartBatch(2); break;
                    case "PlayerInputBatch3": response.result = Vfx120PlayerInputAudit.StartBatch(3); break;
                    case "PlayerInputPoll": response.result = Vfx120PlayerInputAudit.Poll(); break;
                    case "PlayerInputCancel": response.result = Vfx120PlayerInputAudit.Cancel(); break;
                    case "Build": response.result = Vfx120Editor.Build(); break;
                    case "Audit": response.result = Vfx120Editor.AuditCatalog(); break;
                    case "Review": response.result = Vfx120Editor.Review(); break;
                    case "Calibrate": response.result = Vfx120Editor.Calibrate(); break;
                    case "ExportMeshes": response.result = Vfx120Editor.ExportMeshes(); break;
                    case "Connect": response.result = Vfx120Editor.Connect(); break;
                    case "AdapterAudit": response.result = Vfx120AdapterAudit.Run(); break;
                    case "MotionAudit": response.result = Vfx120MotionAudit.Run(); break;
                    case "NativeParticleAudit": response.result = Vfx120MotionAudit.RunNativeParticles(); break;
                    case "AreaAudit": response.result = Vfx120AreaAudit.Run(); break;
                    case "RuntimeAudit": response.result = Vfx120RuntimeAudit.Start(); break;
                    case "RuntimePoll": response.result = Vfx120RuntimeAudit.Poll(); break;
                    case "RuntimeCancel": response.result = Vfx120RuntimeAudit.Cancel(); break;
                    case "GameplayAudit": response.result = Vfx120GameplayAudit.Start(); break;
                    case "GameplayCapture": response.result = Vfx120GameplayAudit.Start(0, true); break;
                    case "GameplayDiagnosticCapture": response.result = Vfx120GameplayAudit.Start(0, true, true); break;
                    case "ContrastAudit": response.result = Vfx120ContrastAudit.Run(); break;
                    case "GameplayPoll": response.result = Vfx120GameplayAudit.Poll(); break;
                    case "GameplayCancel": response.result = Vfx120GameplayAudit.Cancel(); break;
                    case "OpenWorld": response.result = OpenSavedScene("Assets/_Project/Scenes/Dev/C2_CodexWorld.unity"); break;
                    case "OpenVfxReview": response.result = OpenSavedScene("Assets/_Project/Art/SpellVFX120/Scenes/SpellVFX120_Review.unity"); break;
                    case "OpenRange": response.result = OpenSavedScene("Assets/_Project/Scenes/Dev/C1_SpellRange.unity"); break;
                    case "RenderAudit": response.result = Vfx120RenderAudit.Run(); break;
                    case "Capture": response.result = Vfx120Capture.Start(command.request); break;
                    case "CapturePoll": response.result = Vfx120Capture.Poll(); break;
                    case "CaptureCancel": response.result = Vfx120Capture.Cancel(); break;
                    case "TraditionalCapture": response.result = Vfx120TraditionalCapture.Start(command.request); break;
                    case "TraditionalPoll": response.result = Vfx120TraditionalCapture.Poll(); break;
                    case "TraditionalBuild": response.result = Vfx120TraditionalBuilder.Build(); break;
                    case "TraditionalAudit": response.result = Vfx120TraditionalAudit.Run(); break;
                    case "MeshyBuild": response.result = Vfx120MeshyBuilder.Build(); break;
                    case "TraditionalCatalogBuild": response.result = Vfx120TraditionalCatalogBuilder.Build(); break;
                    case "TraditionalCatalogAudit": response.result = Vfx120TraditionalAudit.RunCatalog(); break;
                    case "TraditionalPlayAudit": response.result = Vfx120TraditionalPlayAudit.Start(); break;
                    case "NieunPlayAudit": response.result=Vfx120TraditionalPlayAudit.StartNieun(); break;
                    case "SinglePlayAudit": response.result = Vfx120TraditionalPlayAudit.StartSingle(command.request); break;
                    case "ElementWashPlayAudit": response.result = Vfx120TraditionalPlayAudit.StartElementWash(); break;
                    case "ElementWashAudit": response.result = Vfx120ElementWashAudit.Run(); break;
                    case "ElementWashBuild": response.result = Vfx120ElementWashBuilder.Build(); break;
                    case "BotanicalBuild": response.result = Vfx120BotanicalBuilder.Build(); break;
                    case "BotanicalAudit": response.result = Vfx120BotanicalAudit.Run(); break;
                    case "BotanicalGroundProbe": response.result = Vfx120BotanicalBuilder.GroundProbe(); break;
                    case "BotanicalPlayAudit": response.result = Vfx120TraditionalPlayAudit.StartBotanical(); break;
                    case "StoneBodyBuild": response.result = Vfx120StoneBodyBuilder.Build(); break;
                    case "WaterWaveTuning": response.result = Vfx120WaterWaveTuning.Build(); break;
                    case "SonDetailBuild": response.result = Vfx120MetalDetailBuilder.Build(); break;
                    case "ChimeDetailBuild": response.result = Vfx120MetalDetailBuilder.BuildChime(); break;
                    case "FocusArcsDetailBuild": response.result = Vfx120MetalDetailBuilder.BuildFocusArcs(); break;
                    case "FrostDetailBuild": response.result = Vfx120FrostDetailBuilder.Build(); break;
                    case "SpeedFeatherBuild": response.result = Vfx120MetalDetailBuilder.BuildSpeedFeathers(); break;
                    case "FireBoltBuild": response.result=Vfx120FireBoltBuilder.Build(); break;
                    case "AttachedFlameBuild": response.result=Vfx120AttachedFlameBuilder.Build(); break;
                    case "HeavyFlameBuild": response.result=Vfx120HeavyFlameBuilder.Build(); break;
                    case "PiercingFlameBuild": response.result=Vfx120PiercingFlameBuilder.Build(); break;
                    case "FireGuardBuild": response.result=Vfx120FireGuardBuilder.Build(); break;
                    case "FireResolveBuild": response.result=Vfx120FireResolveBuilder.Build(); break;
                    case "FireSwordBuild": response.result=Vfx120FireSwordBuilder.Build(); break;
                    case "FireCompanionsBuild": response.result=Vfx120FireCompanionsBuilder.Build(); break;
                    case "FireJetBuild": response.result=Vfx120FireJetBuilder.Build(); break;
                    case "FireHealingBuild": response.result=Vfx120FireHealingBuilder.Build(); break;
                    case "FireBackblastBuild": response.result=Vfx120FireBackblastBuilder.Build(); break;
                    case "FireSummonBuild": response.result=Vfx120FireSummonBuilder.Build(); break;
                    case "FireMeltBuild": response.result=Vfx120FireMeltBuilder.Build(); break;
                    case "FireSteamBuild": response.result=Vfx120FireSteamBuilder.Build(); break;
                    case "FireWardBuild": response.result=Vfx120FireWardBuilder.Build(); break;
                    case "FireBurnBuild": response.result=Vfx120FireBurnBuilder.Build(); break;
                    case "FireAuraBuild": response.result=Vfx120FireAuraBuilder.Build(); break;
                    case "FireEmpowerBuild": response.result=Vfx120FireEmpowerBuilder.Build(); break;
                    case "EmberChargeBuild": response.result=Vfx120EmberChargeBuilder.Build(); break;
                    case "FlameCrescentBuild": response.result=Vfx120FlameCrescentBuilder.Build(); break;
                    case "BambooBoltBuild": response.result=Vfx120BambooBoltBuilder.Build(); break;
                    case "SeedTransferBuild": response.result=Vfx120SeedTransferBuilder.Build(); break;
                    case "SeedPodBuild": response.result=Vfx120SeedPodBuilder.Build(); break;
                    case "LeafCutBuild": response.result=Vfx120LeafCutBuilder.Build(); break;
                    case "WetRootBuild": response.result=Vfx120WetRootBuilder.Build(); break;
                    case "RootLiftBuild": response.result=Vfx120RootLiftBuilder.Build(); break;
                    case "StakeFieldBuild": response.result=Vfx120StakeFieldBuilder.Build(); break;
                    case "WoodLiftBuild": response.result=Vfx120WoodLiftBuilder.Build(); break;
                    case "VineFieldBuild": response.result=Vfx120VineFieldBuilder.Build(); break;
                    case "BambooSpikeBuild": response.result=Vfx120BambooSpikeBuilder.Build(); break;
                    case "CompanionSeedBuild": response.result=Vfx120CompanionSeedBuilder.Build(); break;
                    case "WoodSwordBuild": response.result=Vfx120WoodSwordBuilder.Build(); break;
                    case "BloomBuild": response.result=Vfx120BloomBuilder.Build(); break;
                    case "RegrowthBuild": response.result = Vfx120RegrowthBuilder.Build(); break;
                    case "WoodWardBuild": response.result = Vfx120WoodWardBuilder.Build(); break;
                    case "BambooGuardBuild": response.result = Vfx120BambooGuardBuilder.Build(); break;
                    case "InterceptionBuild": response.result = Vfx120InterceptionBuilder.Build(); break;
                    case "TraditionalPlayPoll": response.result = Vfx120TraditionalPlayAudit.Poll(); break;
                    case "TraditionalPlayCancel": response.result = Vfx120TraditionalPlayAudit.Cancel(); break;
                    case "Play": EditorApplication.isPlaying = true; response.result = "PLAY_REQUESTED"; break;
                    case "Stop": EditorApplication.isPlaying = false; response.result = "STOP_REQUESTED"; break;
                    default: throw new InvalidOperationException("Unknown scoped VFX command");
                }
                response.status = "COMPLETE";
            }
            catch (Exception e) { response.status = "FAILED"; response.error = e.ToString(); }
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "response_" + command.id + ".json"), JsonUtility.ToJson(response, true));
        }

        static string OpenSavedScene(string path)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before opening a saved scene");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Unsaved scene changes; saved scene was not replaced");
            if (!File.Exists(path)) throw new FileNotFoundException("Scene is unavailable", path);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
            return path;
        }
    }
}
