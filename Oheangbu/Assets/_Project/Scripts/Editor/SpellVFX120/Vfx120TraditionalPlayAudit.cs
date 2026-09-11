using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.SpellVFX120;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Real Update/native PS/Destroy test in the saved C2 scene. This never calls
    // Effect.Sample, motif.Sample, Simulate, Update, recognition or damage itself.
    [InitializeOnLoad]
    public static class Vfx120TraditionalPlayAudit
    {
        private const string ScenePath = "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity";
        private const string StateKey = "Oheangbu.VFX120.TraditionalPlayAudit.v1";
        private static readonly string[] Glyphs = { "가", "곰", "구", "놈", "솜" };
        [Serializable] private sealed class Report
        {
            public string[] glyphs = Glyphs;
            public string status, stage, output, startedUtc, unityVersion, appMvid, hardware;
            public string scope = "Five sequential catalog prefab instances in real C2 Play. Begin receives a diagnostic impact clock; no recognizer, player DI, real cast/hit/damage, 30/60/120 sweep or art PASS.";
            public string timingScope = "Time.unscaledDeltaTime and optional Main Thread marker describe the whole C2/Editor frame including this observer, other gameplay and Editor overhead; not isolated component CPU, GPU time or a frame-rate target certification.";
            public string sceneHashBefore, sceneHashAfter, camerasBefore, camerasAfter, outputWriteError, cpuMarker = "UNAVAILABLE";
            public string art = "UNVERIFIED", gameplayCasting = "UNVERIFIED", targetFrameRates = "UNVERIFIED";
            public long startedTicks;
            public int completed, failed, errors, shaderErrors, savedVsync, savedFps;
            public float savedTimeScale;
            public bool savedBackground, sceneUnchanged, sourceUnchanged, restored, cleanupObserved;
            public List<Row> rows = new List<Row>();
            public List<SourceRow> sources = new List<SourceRow>();
            public List<string> messages = new List<string>();
        }
        [Serializable] private sealed class Row
        {
            public string glyph, status = "RUNNING", nativeDiagnostic, modelDiagnostic;
            public float life, suppliedImpactClock, spawnTime, lastAliveAge, destroyedAt = -1, lateBy,
                maxEffectAge, maxMotifAge, maxModelAge, maxScaledDelta;
            public int frames, castPeak, impactPeak, fieldPeak, bodyPeak, nativeSystemsPeak, capacityPeak, modelVisibleFrames, modelRendererCount;
            public bool expectsModel, expectsBody, expectsBotanical, configured, budgets = true, shaderFlags = true, fixedPose = true,
                noActiveCollider = true, noFallback = true, sourceReferences = true, rootGone, childrenGone, lifetime;
            public bool expectsCast, expectsImpact, expectsField;
            public bool expectsFrostCords, syntheticFrostHitIssued;
            public int frostEnabledFrames;
            public bool expectsWoodLift,woodLiftPlan,woodLiftReleased;
            public float woodLiftMaxHeight;
            public int woodLiftVisibleFrames;
            public bool expectsStakeField;
            public int stakeContacts,stakeMaxFeedback,stakeVisibleFrames;
            public bool expectsRootLift,rootLiftWithdrawn;
            public int rootLiftVisibleFrames;
            public float rootLiftMaxRise;
            public bool expectsWetRoot,wetContact;
            public int wetVisibleFrames,wetMaxPaths;
            public bool expectsLeafCut,leafCutIssued,leafExecution;
            public int leafCutVisibleFrames;
            public bool expectsSeedPod,podAttached,podDetonated;
            public int podVisibleFrames;
            public bool expectsSeedTransfer,transferRequested,transferHit;
            public int transferVisibleFrames;
            public bool expectsBambooBolt,boltContact;
            public bool expectsFireBolt,fireImpact;
            public int fireParticlePeak;
            public float firePatternPeak;
            public float fireSplitPeak,firePenetrationPeak,fireGuardPulsePeak;
            public int firePelletPeak,firePelletLaunches,firePelletContacts;
            public int fireGuardParticlePeak,fireAuraParticlePeak,fireAuraContacts;
            public float fireAuraMarkPeak,fireHealingPeak;
            public bool steamOnlySeen;
            public float fireBurnProgress;
            public int boltVisibleFrames;
            public float boltMaxTipError;
            public bool expectsVineField,vineReleased,vineMeshGone;
            public int vineContacts,vineMaxActiveContacts,vineVisibleFrames;
            public float vineMaxSpread;
            public bool expectsBambooSpikes,spikesAllRoseTogether=true;
            public int spikesVisibleFrames,spikesMaxRaised;
            public float spikesFirstFullAt=-1,spikesPlanDelay;
            public bool expectsCompanionSeeds,companionSeparateHitSeen;
            public int companionConfirmedMask,companionOpenMask,companionVisibleFrames;
            public bool expectsWoodSword,swordContactIssued,swordTraceCleared;
            public int swordVisibleFrames,swordMaxTracePoints;
            public float swordGripError;
            public bool expectsBloom, syntheticBloom, bloomRepeatRejected, bloomMeshesGone;
            public int bloomVisibleFrames;
            public float bloomMaxOpening;
            public bool expectsRegrowth;
            public int regrowthTicks, regrowthOpenedMask, regrowthVisibleFrames;
            public bool expectsWoodWard;
            public int wardVisibleFrames, wardHitSignals, wardTiltMask;
            public float wardMaxTilt;
            public bool expectsBambooGuard, syntheticBambooParry, guardMeshesGone;
            public int guardVisibleFrames;
            public float guardMaxBend;
            public bool expectsInterceptions;
            public int confirmedInterceptions, interceptionEnabledFrames;
            public double wholeSceneFrameMeanMs, wholeSceneFrameP95Ms, wholeSceneObservedFps, mainThreadMeanMs, mainThreadMaxMs;
            public int mainThreadSamples, gc0Collections;
            public float minimumTimeScale = float.PositiveInfinity, maximumTimeScale;
        }
        [Serializable] private sealed class SourceRow
        {
            public string path, diskBefore, diskAfter, dependencyBefore, dependencyAfter, memoryBefore, memoryAfter;
            public bool unchanged;
        }
        [Serializable] private sealed class Progress
        {
            public string status, stage, glyph, output, outputWriteError;
            public int completed, failed, errors;
            public double wallSeconds;
            public bool restored;
        }
        private struct Pose
        {
            public Transform transform; public Vector3 position, scale; public Quaternion rotation;
        }
        private sealed class MotifTrack
        {
            public Vfx120TraditionalMotif motif; public ParticleSystem[] systems;
        }
        private sealed class Tracked
        {
            public GameObject root; public Vfx120Effect effect; public Row row;
            public Mesh[] ownedGuardMeshes;
            public Transform swordGrip,companionPrimary;
            public Vfx120InterceptionMotion.Target[] interceptionPlans;
            public readonly List<GameObject> children = new List<GameObject>();
            public readonly HashSet<int> childIds = new HashSet<int>();
            public readonly List<MotifTrack> motifs = new List<MotifTrack>();
            public readonly List<double> deltas = new List<double>(1024);
            public Pose[] poses; public MeshFilter[] filters; public Mesh[] sourceMeshes;
            public MeshRenderer[] renderers; public Material[][] sourceMaterials;
            public int gc0; public double cpuSum;
        }
        private static Report _report;
        private static Vfx120Catalog _catalog;
        private static Tracked _current;
        private static readonly List<GameObject> Owned = new List<GameObject>();
        private static readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        private static ProfilerRecorder _cpu;
        private static int _next, _lastFrame = -1, _settleUntil;
        private static double _lastSave;
        private static string Output => Path.Combine(Vfx120Editor.Output, "traditional_play_audit.json");
        private static bool Active => _report != null && _report.stage != "FINISHED";

        static Vfx120TraditionalPlayAudit()
        {
            string saved = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(saved)) _report = JsonUtility.FromJson<Report>(saved);
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Application.logMessageReceived += Log;
            if (Active && _report.stage == "WAIT_EXIT" && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += CompleteExit;
            else if (Active && _report.stage != "WAIT_ENTER" && _report.stage != "WAIT_EXIT")
                EditorApplication.delayCall += () => Abort("Unexpected domain reload lost live references; no resumed PASS.");
        }

        public static string StartNieun() => StartRun(false,false,null,true);
        public static string Start() => StartRun(false);
        public static string StartElementWash() => StartRun(true);
        public static string StartBotanical() => StartRun(false, true);
        public static string StartSingle(string glyph)
        {
            if (string.IsNullOrEmpty(glyph) || glyph.Length != 1 || glyph[0] < 0xAC00 || glyph[0] > 0xD7A3)
                throw new ArgumentException("One catalog Hangul glyph required.");
            return StartRun(false, false, glyph);
        }
        private static string StartRun(bool elementWash, bool botanical = false, string singleGlyph = null, bool nieun = false)
        {
            if (Active) return Poll();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return "REJECTED: requires stopped Edit mode with compilation complete.";
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                return "REJECTED: Scene Reload must be enabled; this audit does not change project settings.";
            if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != ScenePath || SceneManager.GetActiveScene().isDirty)
                return "REJECTED: only the saved, clean C2 scene must be open; no scene is opened/saved by this tool.";
            _report = new Report { status = "RUNNING", stage = "WAIT_ENTER", output = Output,
                startedTicks = DateTime.UtcNow.Ticks, startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion, hardware = SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName,
                sceneHashBefore = FileHash(Absolute(ScenePath)), camerasBefore = CameraHash(), savedVsync = QualitySettings.vSyncCount,
                savedFps = Application.targetFrameRate, savedTimeScale = Time.timeScale, savedBackground = Application.runInBackground };
            if (elementWash)
            {
                _report.glyphs = new[] { "노", "모" };
                _report.output = Path.Combine(Vfx120Editor.Output, "element_wash_play_audit.json");
                _report.scope = "Two sequential catalog particle bodies in actual C2 Play with diagnostic spatial plans. No manual Sample/Simulate; no recognizer, actual damage, art or target frame-rate PASS.";
            }
            if (botanical)
            {
                _report.glyphs = new[] { "각", "공", "검" };
                _report.output = Path.Combine(Vfx120Editor.Output, "botanical_play_audit.json");
                _report.scope = "Three sequential curated botanical bodies in real C2 Update/Destroy with diagnostic target/Path plans. Shared mesh/material references, visibility and lifetime only; no actual recognition, hit, plant contact/art or frame-rate PASS. Fixed-pose check applies to the owner root, not intentionally emerging/swaying plants.";
            }
            if (singleGlyph != null)
            {
                _report.glyphs = new[] { singleGlyph };
                _report.output = Path.Combine(Vfx120Editor.Output, "single_play_" + ((int)singleGlyph[0]).ToString("X4") + ".json");
                _report.scope = "One selected catalog prefab in real C2 Update/Destroy. Technical playback, native particle budgets and cleanup only. Visual review belongs to the user; no actual cast/stack/damage or target FPS claim.";
                _report.art = "AWAITING_USER_REVIEW";
            }
            if(nieun){_report.glyphs=new[]{"나","낙","난","남","낫","낭","너","넉","넌","넘","넛","넝","노","녹","논","놈","놋","농","누","눈"};_report.output=Path.Combine(Vfx120Editor.Output,"nieun_final_play_audit.json");_report.scope="Twenty assigned nieun effects sequentially in latest runtime; synthetic presentation signals, no art/gameplay/performance approval.";}
            Owned.Clear(); Assets.Clear(); _current = null; _next = 0; _lastFrame = -1;
            Save(); EditorApplication.isPlaying = true;
            return Poll();
        }
        public static string Poll() => _report == null ? "{\"status\":\"NOT_STARTED\"}" : JsonUtility.ToJson(new Progress
        {
            status = _report.status, stage = _report.stage, glyph = _current?.row.glyph, output = _report.output,
            completed = _report.completed, failed = _report.failed, errors = _report.errors,
            wallSeconds = (DateTime.UtcNow.Ticks - _report.startedTicks) / (double)TimeSpan.TicksPerSecond,
            restored = _report.restored, outputWriteError = _report.outputWriteError
        }, true);
        public static string Cancel() { if (Active) Abort("Cancelled by caller; unfinished rows remain unverified."); return Poll(); }

        private static void PlayState(PlayModeStateChange state)
        {
            if (!Active) return;
            if (state == PlayModeStateChange.EnteredPlayMode && _report.stage == "WAIT_ENTER") InitializePlay();
            else if (state == PlayModeStateChange.EnteredPlayMode && _report.stage == "WAIT_EXIT") EditorApplication.isPlaying = false;
            else if (state == PlayModeStateChange.ExitingPlayMode && _report.stage != "WAIT_EXIT")
                Abort("Play stopped before the audit finished.");
            else if (state == PlayModeStateChange.EnteredEditMode && _report.stage == "WAIT_EXIT") CompleteExit();
        }
        private static void InitializePlay()
        {
            try
            {
                RequireC2();
                _catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
                Need(_catalog != null, "Catalog missing.");
                var paths = new HashSet<string>();
                foreach (string glyph in _report.glyphs)
                {
                    var entries = _catalog.Entries.Where(e => e.Glyph == glyph).ToArray();
                    Need(entries.Length == 1 && entries[0].Profile != null && entries[0].Prefab != null, "Unique actual prefab/profile required: " + glyph);
                    var entry = entries[0]; Need(entry.Profile.Glyph == glyph, "Catalog/profile glyph mismatch.");
                    Remember(entry.Profile, paths); Remember(entry.Prefab, paths);
                }
                _report.appMvid = typeof(Vfx120Effect).Module.ModuleVersionId.ToString();
                Application.runInBackground = true;
                _report.messages.Add("Only runInBackground is temporarily enabled. Existing timeScale, vSync and FPS cap are retained during measurement; no input/camera/scene objects are edited.");
                try
                {
                    _cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
                    _report.cpuMarker = _cpu.Valid ? "Main Thread [" + _cpu.UnitType + "] whole Editor/scene, not isolated VFX" : "UNAVAILABLE";
                }
                catch (Exception e) { _report.messages.Add("Optional Main Thread marker unavailable: " + e.Message); }
                _settleUntil = Time.frameCount + 3; _report.stage = "SETTLE"; Save();
            }
            catch (Exception e) { Abort(e.GetBaseException().Message); }
        }
        private static void Tick()
        {
            if (!Active) return;
            if (_report.stage == "WAIT_EXIT")
            { if (!EditorApplication.isPlayingOrWillChangePlaymode) CompleteExit(); return; }
            // The first measured C2 reload took ~75 s before any case could advance.
            // Include that editor startup overhead without shortening a case's real Life.
            if ((DateTime.UtcNow.Ticks - _report.startedTicks) / (double)TimeSpan.TicksPerSecond > 180)
            { Abort("180-second wall timeout including scene reload; paused/slowed or incomplete cases are not a PASS."); return; }
            if (_report.stage == "WAIT_ENTER")
            { if (EditorApplication.isPlaying && !EditorApplication.isCompiling) InitializePlay(); return; }
            if (!EditorApplication.isPlaying) { Abort("Play state changed unexpectedly."); return; }
            if (EditorApplication.isPaused || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            try
            {
                RequireC2();
                if (_report.stage == "SETTLE" && Time.frameCount >= _settleUntil) SpawnNext();
                else if (_report.stage == "CASE") Observe();
                if (EditorApplication.timeSinceStartup - _lastSave >= 1) Save(false);
            }
            catch (Exception e) { Abort(e.GetBaseException().Message); }
        }

        private static void SpawnNext()
        {
            if (_next >= _report.glyphs.Length) { Finish(); return; }
            string glyph = _report.glyphs[_next++]; var entry = _catalog.Entries.First(e => e.Glyph == glyph);
            var go = Object.Instantiate(entry.Prefab);
            go.name = "KTP_PlayAudit_" + glyph;
            go.SetActive(true); // Only this copy; asset/prefab activation remains unchanged.
            Owned.Add(go); SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var effect = go.GetComponent<Vfx120Effect>(); Need(effect != null, "Actual prefab lacks Vfx120Effect: " + glyph);
            var row = new Row { glyph = glyph, spawnTime = Time.time, expectsModel = glyph == "곰" || glyph == "놈" || glyph == "솜",
                expectsBody = (glyph == "노" && !Vfx120Effect.IsFireJet(entry.Profile)) || glyph == "모", expectsBotanical = glyph == "각" || glyph == "공" || glyph == "검",
                expectsCast = entry.Profile.NativeCastPrefab != null, expectsImpact = entry.Profile.NativeImpactPrefab != null,
                expectsField = entry.Profile.NativeFieldPrefab != null,
                minimumTimeScale = Time.timeScale, maximumTimeScale = Time.timeScale };
            _report.rows.Add(row);
            _current = new Tracked { root = go, effect = effect, row = row, gc0 = GC.CollectionCount(0) };
            effect.PreviewControlled = false; effect.DemonstrationCues = false;
            if (entry.Profile.NativeImpactPrefab != null || glyph == "가" || Vfx120Effect.IsFireJet(entry.Profile))
            { row.suppliedImpactClock = .6f; effect.SetImpactClock(.6f); }
            if (row.expectsBody || row.expectsBotanical || Vfx120Effect.IsBambooSpikes(entry.Profile) || Vfx120Effect.IsVineField(entry.Profile) || Vfx120Effect.IsRootLift(entry.Profile) || Vfx120Effect.IsStakeField(entry.Profile)) effect.SetAreaPlan(Vfx120Review.CreateDemonstrationAreaPlan(entry.Profile));
            effect.Begin(new Vector3(0, 1, 0), null, new Vector3(0, 1, 4), Color.white);
            if (Vfx120InterceptionMotion.IsPrepared(entry.Profile))
            {
                row.expectsInterceptions = true;
                _current.interceptionPlans = Vfx120InterceptionReviewFixture.CreatePlans(effect.TargetGroundWorldY - effect.transform.position.y);
                for (int i = 0; i < _current.interceptionPlans.Length; i++) _current.interceptionPlans[i].HitConfirmed = false;
                effect.SetInterceptionTargets(_current.interceptionPlans);
                _report.messages.Add("송: six synthetic local interception plans are supplied; individual presentation hits will be confirmed by this observer at their arrival times. No enemy collision/freeze gameplay is tested.");
            }
            if (Vfx120Effect.IsBambooGuard(effect.Profile))
            {
                row.expectsBambooGuard = true;
                Need(effect.BambooGuardConfigured, "Bamboo guard body missing");
                _current.renderers = effect.BambooGuardInstance.GetComponentsInChildren<MeshRenderer>(true);
                _current.ownedGuardMeshes = effect.BambooGuardInstance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                Vfx120Effect.SelectBambooGuard(effect);
                _report.messages.Add("거: a synthetic successful-parry presentation callback is supplied at age 0.36; no enemy collision or reward computation is invoked.");
            }
            if(Vfx120Effect.IsWoodLift(effect.Profile))
            {
                row.expectsWoodLift=true;Need(effect.WoodLiftConfigured,"Wood lift missing");
                row.woodLiftPlan=effect.ConfigureWoodLift(new Vector3(0,effect.WoodLiftGroundY,0),2.1f);
                _current.renderers=effect.WoodLiftInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsStakeField(effect.Profile))
            {
                row.expectsStakeField=true;Need(effect.StakeFieldConfigured,"Stake field missing");
                _current.renderers=effect.StakeFieldInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsFireJet(effect.Profile))Need(effect.FireAuraConfigured,"Fire jet missing");
            if(Vfx120Effect.IsFireBolt(effect.Profile))
            { row.expectsFireBolt=true;Need(effect.FireBoltConfigured,"Fire particles missing"); }
            if(Vfx120Effect.IsRootLift(effect.Profile))
            {
                row.expectsRootLift=true;Need(effect.RootLiftConfigured,"Root lift missing");
                _current.renderers=effect.RootLiftInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsWetRoot(effect.Profile))
            {
                row.expectsWetRoot=true;Need(effect.WetRootConfigured,"Wet root missing");
                _current.renderers=effect.WetRootInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsLeafCut(effect.Profile))
            {
                row.expectsLeafCut=true;Need(effect.LeafCutConfigured,"Leaf cut missing");
                _current.renderers=effect.LeafCutInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsSeedPod(effect.Profile))
            {
                row.expectsSeedPod=true;Need(effect.SeedPodConfigured,"Seed pod missing");
                _current.renderers=effect.SeedPodInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsSeedTransfer(effect.Profile))
            {
                row.expectsSeedTransfer=true;Need(effect.SeedTransferConfigured,"Seed transfer missing");
                _current.renderers=effect.SeedTransferInstance.GetComponentsInChildren<MeshRenderer>(true);
            }
            if(Vfx120Effect.IsBambooBolt(effect.Profile))
            {
                row.expectsBambooBolt=true;Need(effect.BambooBoltConfigured,"Bamboo bolt missing");
                _current.renderers=effect.BambooBoltInstance.GetComponentsInChildren<MeshRenderer>(true);
                foreach(var renderer in _current.renderers)foreach(var material in renderer.sharedMaterials)row.shaderFlags &= material!=null&&material.shader!=null&&material.shader.isSupported;
            }
            if(Vfx120Effect.IsVineField(effect.Profile))
            {
                row.expectsVineField=true;Need(effect.VineFieldConfigured,"Vine field missing");
                _current.renderers=effect.VineFieldInstance.GetComponentsInChildren<MeshRenderer>(true);
                _current.ownedGuardMeshes=new[]{effect.VineFieldOwnedMesh};
                foreach(var renderer in _current.renderers)foreach(var material in renderer.sharedMaterials)row.shaderFlags &= material!=null&&material.shader!=null&&material.shader.isSupported;
                _report.messages.Add("곡: diagnostic Circle plus confirmed foot points at 1.6/2.45s and release at 3.5s. Actual enemy detection, movement slow and zone gameplay are not connected.");
            }
            if(Vfx120Effect.IsBambooSpikes(effect.Profile))
            {
                row.expectsBambooSpikes=true;Need(effect.BambooSpikesConfigured,"Bamboo field missing");
                row.spikesPlanDelay=effect.BambooSpikesDelay;
                _current.renderers=effect.BambooSpikesInstance.GetComponentsInChildren<MeshRenderer>(true);
                foreach(var renderer in _current.renderers)foreach(var material in renderer.sharedMaterials)row.shaderFlags &= material!=null&&material.shader!=null&&material.shader.isSupported;
                _report.messages.Add("고: supplied diagnostic Circle plan uses the same reader as existing combat. Complete rise and native impact follow its Delay, not the generic audit's 0.6s clock. No actual cast/damage is exercised.");
            }
            if(Vfx120Effect.IsCompanionSeeds(effect.Profile))
            {
                row.expectsCompanionSeeds=true;Need(effect.CompanionSeedsConfigured,"Companion seeds missing");
                _current.renderers=effect.CompanionSeedsInstance.GetComponentsInChildren<MeshRenderer>(true);
                _current.companionPrimary=new GameObject("Audit_CompanionPrimary").transform;_current.companionPrimary.SetParent(go.transform,false);
                _current.companionPrimary.SetPositionAndRotation(Vfx120CompanionSeedReviewFixture.PrimaryPosition(0),Quaternion.identity);
                Need(effect.SetCompanionProjectile(_current.companionPrimary,.4f,1.45f),"Primary plan rejected");
                foreach(var renderer in _current.renderers)foreach(var material in renderer.sharedMaterials)row.shaderFlags &= material!=null&&material.shader!=null&&material.shader.isSupported;
                _report.messages.Add("겅: audit-owned primary projectile pose plus separate seed contacts at 1.45s/1.61s; no real projectile collisions, buff or damage are exercised.");
            }
            if (Vfx120Effect.IsWoodSword(effect.Profile))
            {
                row.expectsWoodSword=true;Need(effect.WoodSwordConfigured,"Wood sword body missing");
                _current.renderers=effect.WoodSwordInstance.GetComponentsInChildren<MeshRenderer>(true);
                _current.swordGrip=new GameObject("Audit_SwordGrip").transform;_current.swordGrip.SetParent(go.transform,false);
                Vfx120WoodSwordReviewFixture.GripPose(0,out var gripPosition,out var gripRotation);_current.swordGrip.SetPositionAndRotation(gripPosition,gripRotation);effect.SetWoodSwordGrip(_current.swordGrip);
                foreach(var renderer in _current.renderers)foreach(var material in renderer.sharedMaterials)row.shaderFlags &= material!=null&&material.shader!=null&&material.shader.isSupported;
                _report.messages.Add("것: an audit-owned grip trajectory, swing flag and contact at 1.15s; actual player sword mode, brush replacement and damage are not exercised.");
            }
            if (Vfx120Effect.IsBloom(effect.Profile))
            {
                row.expectsBloom = true;
                Need(effect.BloomConfigured, "Bloom body missing");
                _current.renderers = effect.BloomInstance.GetComponentsInChildren<MeshRenderer>(true);
                _current.ownedGuardMeshes = effect.BloomInstance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                foreach(var renderer in _current.renderers) foreach(var material in renderer.sharedMaterials)
                    row.shaderFlags &= material != null && material.shader != null && material.shader.isSupported;
                _report.messages.Add("건: one synthetic resolved-healing presentation signal at 0.28s, plus a rejected duplicate. No Health/buff gameplay is invoked.");
            }
            if (Vfx120Effect.IsRegrowth(effect.Profile))
            {
                row.expectsRegrowth = true;
                Need(effect.RegrowthConfigured, "Regrowth body missing");
                _current.renderers = effect.RegrowthInstance.GetComponentsInChildren<MeshRenderer>(true);
                foreach(var renderer in _current.renderers) foreach(var material in renderer.sharedMaterials)
                    row.shaderFlags &= material != null && material.shader != null && material.shader.isSupported;
                _report.messages.Add("걱: six synthetic regrowth callbacks; no healing or Health mutation is invoked.");
            }
            if (Vfx120Effect.IsWoodWard(effect.Profile))
            {
                row.expectsWoodWard = true;
                Need(effect.WoodWardConfigured, "Wood ward body missing");
                _current.renderers = effect.WoodWardInstance.GetComponentsInChildren<MeshRenderer>(true);
                foreach(var renderer in _current.renderers) foreach(var material in renderer.sharedMaterials)
                    row.shaderFlags &= material != null && material.shader != null && material.shader.isSupported;
                _report.messages.Add("구: two synthetic support hits (indices 1 and 4) at ages 0.95 and 1.6; no collision, mitigation or parry rewards are exercised.");
            }
            row.life = effect.Life;
            Need(effect.Begun && row.life > 0 && row.life <= 12, "Invalid/unbounded source lifetime.");
            row.configured = effect.Profile == entry.Profile && (!row.expectsCast || effect.NativeCast != null)
                && (!row.expectsField || effect.NativeFieldConfigured)
                && (!row.expectsBotanical || effect.BotanicalConfigured) && (!row.expectsBody || effect.NativeBodyConfigured);
            if (row.expectsBody) row.configured &= effect.NativeBody != null && effect.PartCount == 0 && effect.AccentCount == 0;
            row.nativeDiagnostic = effect.NativeDiagnostic; row.modelDiagnostic = effect.MeshyDiagnostic;
            var poses = new List<Transform> { go.transform };
            if (row.expectsBotanical)
            {
                row.configured &= effect.BotanicalInstance != null && effect.PartCount == 0 && effect.AccentCount == 0;
                Need(row.configured, "Botanical configure failed: " + effect.BotanicalDiagnostic);
                _current.filters = effect.BotanicalInstance.GetComponentsInChildren<MeshFilter>(true);
                _current.renderers = effect.BotanicalInstance.GetComponentsInChildren<MeshRenderer>(true);
                _current.sourceMeshes = entry.Profile.BotanicalPrefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                _current.sourceMaterials = entry.Profile.BotanicalPrefab.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterials).ToArray();
                row.modelRendererCount = _current.renderers.Length;
                row.sourceReferences &= _current.filters.Length == _current.sourceMeshes.Length && _current.renderers.Length == _current.sourceMaterials.Length;
                foreach (var renderer in _current.renderers) foreach (var material in renderer.sharedMaterials)
                    row.shaderFlags &= material != null && material.shader.isSupported && material.shader.name == "Oheangbu/VFX120/BotanicalSurface";
            }
            if (row.expectsModel)
            {
                var model = effect.MeshyModel;
                row.configured &= entry.Profile.SummonPrefab != null && effect.MeshyConfigured && model != null
                    && effect.PartCount == 0 && effect.AccentCount == 0;
                if (model != null)
                {
                    row.modelRendererCount = model.RendererCount;
                    row.configured &= row.modelRendererCount == 1 && model.SourceAnimatorCount == 0 && model.MaterialCloneCount == 0;
                    poses.AddRange(model.GetComponentsInChildren<Transform>(true));
                    _current.filters = model.GetComponentsInChildren<MeshFilter>(true);
                    _current.renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                    _current.sourceMeshes = entry.Profile.SummonPrefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                    _current.sourceMaterials = entry.Profile.SummonPrefab.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterials).ToArray();
                    row.sourceReferences &= _current.filters.Length == _current.sourceMeshes.Length && _current.renderers.Length == _current.sourceMaterials.Length;
                }
            }
            if (glyph == "구") row.configured &= (effect.NativeReplacesProcedural || effect.WoodWardConfigured) && effect.PartCount == 0 && effect.AccentCount == 0;
            if (row.expectsModel || row.expectsBody || row.expectsBotanical || glyph == "구") row.configured &= !go.GetComponentsInChildren<LineRenderer>(true).Any()
                && !go.GetComponentsInChildren<Vfx120Atmosphere>(true).Any();
            _current.poses = poses.Select(t => new Pose { transform = t, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            Discover(); _report.stage = "CASE"; Save();
        }

        private static void Discover()
        {
            if (_current.root == null) return;
            var effect = _current.effect;
            bool added = false;
            foreach (var motif in new[] { effect.NativeCast, effect.NativeImpact, effect.NativeField, effect.NativeBody })
            {
                if (motif == null || _current.motifs.Any(m => m.motif == motif)) continue;
                var systems = motif.GetComponentsInChildren<ParticleSystem>(true);
                _current.motifs.Add(new MotifTrack { motif = motif, systems = systems }); added = true;
                foreach (var renderer in motif.GetComponentsInChildren<Renderer>(true)) foreach (var material in renderer.sharedMaterials)
                    _current.row.shaderFlags &= material != null && material.shader != null && material.shader.isSupported
                        && material.shader.name != "Hidden/InternalErrorShader";
            }
            if (!added && _current.children.Count > 0) return;
            foreach (var t in _current.root.GetComponentsInChildren<Transform>(true))
                if (_current.childIds.Add(t.gameObject.GetInstanceID())) _current.children.Add(t.gameObject);
        }
        private static void Observe()
        {
            var t = _current; var row = t.row;
            float elapsed = Time.time - row.spawnTime;
            row.frames++; row.maxScaledDelta = Mathf.Max(row.maxScaledDelta, Time.deltaTime);
            row.minimumTimeScale = Mathf.Min(row.minimumTimeScale, Time.timeScale); row.maximumTimeScale = Mathf.Max(row.maximumTimeScale, Time.timeScale);
            t.deltas.Add(Time.unscaledDeltaTime * 1000.0);
            if (_cpu.Valid && _cpu.Count > 0 && _cpu.UnitType.ToString().Contains("Nanosecond"))
            { double ms = _cpu.LastValue / 1e6; t.cpuSum += ms; row.mainThreadSamples++; row.mainThreadMaxMs = Math.Max(row.mainThreadMaxMs, ms); }
            if (t.root != null)
            {
                if(row.expectsWoodLift)
                {
                    t.effect.SetWoodLiftHeight(Vfx120WoodLiftReviewFixture.Height(t.effect.Age));
                    if(t.effect.Age>=3.5f&&!row.woodLiftReleased)row.woodLiftReleased=t.effect.ReleaseWoodLift();
                    row.woodLiftMaxHeight=Mathf.Max(row.woodLiftMaxHeight,t.effect.WoodLiftHeight);
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.woodLiftVisibleFrames++;
                }
                if(row.expectsStakeField)
                {
                    if(t.effect.Age>=(row.stakeContacts==0?1.4f:2.5f)&&row.stakeContacts<2)if(t.effect.SignalStakeContact(Vfx120StakeFieldReviewFixture.Contact(row.stakeContacts)))row.stakeContacts++;
                    row.stakeMaxFeedback=Mathf.Max(row.stakeMaxFeedback,t.effect.StakeActiveFeedback);
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.stakeVisibleFrames++;
                }
                if(row.expectsRootLift)
                {
                    row.rootLiftMaxRise=Mathf.Max(row.rootLiftMaxRise,t.effect.RootLiftRise);
                    if(t.effect.Age>t.effect.RootLiftDelay+.59f&&t.effect.RootLiftRise<.001f)row.rootLiftWithdrawn=true;
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.rootLiftVisibleFrames++;
                }
                if(row.expectsWetRoot)
                {
                    if(t.effect.Age>=.6f&&!row.wetContact)row.wetContact=t.effect.SignalWetRootContact(null,Vfx120WetRootReviewFixture.Paths(),Vector3.back);
                    row.wetMaxPaths=Mathf.Max(row.wetMaxPaths,t.effect.WetRootVisiblePaths);
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.wetVisibleFrames++;
                }
                if(row.expectsLeafCut)
                {
                    if(t.effect.Age>=.6f&&!row.leafCutIssued)row.leafCutIssued=t.effect.SignalLeafCut(Vfx120LeafCutReviewFixture.Contact,Vector3.back,true);
                    row.leafExecution |= t.effect.LeafCutExecution;
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.leafCutVisibleFrames++;
                }
                if(row.expectsSeedPod)
                {
                    if(t.effect.Age>=.55f&&!row.podAttached)row.podAttached=t.effect.AttachSeedPod(null,Vfx120SeedPodReviewFixture.Contact,Vector3.back);
                    if(t.effect.Age>=1.65f&&!row.podDetonated)row.podDetonated=t.effect.DetonateSeedPod();
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.podVisibleFrames++;
                }
                if(row.expectsSeedTransfer)
                {
                    if(t.effect.Age>=1.3f&&!row.transferRequested)row.transferRequested=t.effect.RequestSeedTransfer(null,Vfx120SeedTransferReviewFixture.Recipient);
                    if(t.effect.Age>=1.85f&&!row.transferHit)row.transferHit=t.effect.ConfirmSeedTransferHit(Vfx120SeedTransferReviewFixture.Recipient);
                    if(t.renderers.Any(r=>r!=null&&r.enabled))row.transferVisibleFrames++;
                }
                if(Vfx120Effect.IsFireGuard(t.effect.Profile))
                { if(t.effect.Age>=.36f&&t.effect.ParryAt<0)t.effect.Signal(Vfx120Effect.Cue.Parry);row.fireGuardParticlePeak=Mathf.Max(row.fireGuardParticlePeak,t.effect.FireGuardParticles);row.fireGuardPulsePeak=Mathf.Max(row.fireGuardPulsePeak,t.effect.FireGuardPulse); }
                if(Vfx120Effect.IsFireAura(t.effect.Profile))
                { if(Vfx120Effect.IsFireBurn(t.effect.Profile)){t.effect.BeginFireBurnPath(t.effect.transform.position,t.effect.transform.position+Vector3.forward*3);row.fireBurnProgress=Mathf.Max(row.fireBurnProgress,t.effect.FireBurnProgress);}else if(Vfx120Effect.IsFireJet(t.effect.Profile)){if(Vfx120Effect.IsFireBackblast(t.effect.Profile)&&t.effect.Age>=1.2f&&!t.effect.FireBackblastEnded)t.effect.EndFireBackblast(t.effect.transform.position-Vector3.forward*.8f);if(Vfx120Effect.IsFireHealing(t.effect.Profile)){if(t.effect.Age>=.8f)t.effect.ConfirmFireHealingHit(t.effect.transform,t.effect.transform.position+Vector3.forward*2,t.effect.transform.position+new Vector3(0,-.8f,2));row.fireHealingPeak=Mathf.Max(row.fireHealingPeak,t.effect.FireHealingOpacity);}}else if(Vfx120Effect.IsFireSword(t.effect.Profile)){t.effect.SetFireSwordGrip(t.effect.transform.position,Quaternion.Euler(0,0,Mathf.Lerp(35,-80,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.5f,1.1f,t.effect.Age)))));if(t.effect.Age>=.85f&&t.effect.FireAuraContacts==0)t.effect.SignalFireAuraHit(t.effect.transform.position+Vector3.right);}else if(Vfx120Effect.IsFireEmpower(t.effect.Profile)){if(t.effect.Age>=1.2f&&!t.effect.FireEmpowerConsumed)t.effect.ConsumeFireEmpower();if(t.effect.Age>=1.4f&&t.effect.FireAuraContacts==0)t.effect.ConfirmFireEmpowerHit(t.effect.transform.position+Vector3.forward*1.5f);}else if(t.effect.FireAuraContacts<3&&t.effect.Age>=.6f+t.effect.FireAuraContacts*.7f)t.effect.SignalFireAuraHit(t.effect.transform.position+Vector3.right);row.fireAuraParticlePeak=Mathf.Max(row.fireAuraParticlePeak,t.effect.FireAuraParticles);row.fireAuraContacts=t.effect.FireAuraContacts;row.fireAuraMarkPeak=Mathf.Max(row.fireAuraMarkPeak,t.effect.FireAuraMarkOpacity); }
                if(Vfx120Effect.IsFireCompanions(t.effect.Profile)){for(int i=0;i<3;i++){if(t.effect.Age>=.6f+i*.3f)t.effect.LaunchFirePellet(i,t.effect.transform.position+new Vector3(i-1,0,2));if(t.effect.Age>=1.05f+i*.3f)t.effect.ConfirmFirePelletHit(i,t.effect.transform.position+new Vector3(i-1,0,2));}row.firePelletPeak=Mathf.Max(row.firePelletPeak,t.effect.FirePelletParticles);row.firePelletLaunches=t.effect.FirePelletLaunches;row.firePelletContacts=t.effect.FirePelletContacts;}
                if(Vfx120Effect.IsFireSteam(t.effect.Profile)&&t.effect.Age>2.5f&&t.effect.Age<t.effect.Life-.8f)row.steamOnlySeen|=t.effect.FireSteamParticles>0&&t.effect.FireSteamFlameParticles==0;
                if(row.expectsFireBolt)
                { if(t.effect.IsEmberCharge&&t.effect.Age>=1.8f&&t.effect.EmberChargeDetonatedAt<0)t.effect.DetonateEmberCharge();row.fireParticlePeak=Mathf.Max(row.fireParticlePeak,t.effect.FireBoltParticles);row.fireImpact|=t.effect.FireBoltImpacted;row.firePatternPeak=Mathf.Max(row.firePatternPeak,t.effect.FireHitPatternOpacity);row.fireSplitPeak=Mathf.Max(row.fireSplitPeak,t.effect.FireSplitSeparation);row.firePenetrationPeak=Mathf.Max(row.firePenetrationPeak,t.effect.FirePenetrationDistance); }
                if(row.expectsBambooBolt)
                {
                    var expected=Vector3.Lerp(t.effect.ReceivedOrigin,t.effect.ReceivedFallback,Mathf.Clamp01(t.effect.Age/t.effect.BambooBoltFlight));
                    row.boltMaxTipError=Mathf.Max(row.boltMaxTipError,Vector3.Distance(expected,t.effect.BambooBoltTip));
                    row.boltContact |= t.effect.BambooBoltContactSeen;
                    if(t.renderers.Any(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy))row.boltVisibleFrames++;
                }
                if(row.expectsVineField)
                {
                    float age=t.effect.Age;
                    if(row.vineContacts<2&&age>=1.6f+row.vineContacts*.85f)
                        if(t.effect.SignalVineFootContact(Vfx120VineFieldReviewFixture.Foot(row.vineContacts,t.effect.TargetGroundWorldY)))row.vineContacts++;
                    if(age>=3.5f&&!row.vineReleased)row.vineReleased=t.effect.ReleaseVineField();
                    row.vineMaxSpread=Mathf.Max(row.vineMaxSpread,t.effect.VineFieldSpread);
                    row.vineMaxActiveContacts=Mathf.Max(row.vineMaxActiveContacts,t.effect.VineFieldActiveContacts);
                    if(t.renderers.Any(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy))row.vineVisibleFrames++;
                    row.noFallback &= t.effect.VineFieldConfigured;
                }
                if(row.expectsBambooSpikes)
                {
                    int raised=t.effect.BambooSpikesRaised;row.spikesMaxRaised=Mathf.Max(row.spikesMaxRaised,raised);
                    row.spikesAllRoseTogether &= raised==0||raised==17;
                    if(raised==17&&row.spikesFirstFullAt<0)row.spikesFirstFullAt=t.effect.Age;
                    if(t.renderers.Any(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy))row.spikesVisibleFrames++;
                    row.noFallback &= t.effect.BambooSpikesConfigured;
                }
                if(row.expectsCompanionSeeds)
                {
                    float age=t.effect.Age;t.companionPrimary.position=Vfx120CompanionSeedReviewFixture.PrimaryPosition(age);
                    for(int i=0;i<2;i++)if(age>=1.45f+i*.16f&&(row.companionConfirmedMask&(1<<i))==0)
                        if(t.effect.ConfirmCompanionHit(i,Vfx120CompanionSeedReviewFixture.ContactPosition(i),Vector3.back))row.companionConfirmedMask|=1<<i;
                    if(t.effect.CompanionHitMask==1)row.companionSeparateHitSeen=true;
                    row.companionOpenMask|=t.effect.CompanionOpenMask;
                    if(t.renderers.Any(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy))row.companionVisibleFrames++;
                }
                if(row.expectsWoodSword)
                {
                    float age=t.effect.Age;
                    if(age>.03f)row.swordGripError=Mathf.Max(row.swordGripError,Vector3.Distance(t.effect.WoodSwordInstance.transform.position,t.swordGrip.position));
                    Vfx120WoodSwordReviewFixture.GripPose(age,out var gp,out var gq);t.swordGrip.SetPositionAndRotation(gp,gq);t.effect.SetWoodSwordSwing(Vfx120WoodSwordReviewFixture.SwingAt(age));
                    if(!row.swordContactIssued&&age>=1.15f)row.swordContactIssued=t.effect.SignalWoodSwordContact(gp+gq*new Vector3(.03f,.85f,0));
                    row.swordMaxTracePoints=Mathf.Max(row.swordMaxTracePoints,t.effect.WoodSwordTracePoints);
                    if(age>1.8f&&t.effect.WoodSwordTracePoints==0)row.swordTraceCleared=true;
                    if(t.renderers.Any(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy))row.swordVisibleFrames++;
                }
                if (row.expectsBloom)
                {
                    if(!row.syntheticBloom && t.effect.Age >= .28f)
                    {
                        row.syntheticBloom = t.effect.SignalBloom();
                        row.bloomRepeatRejected = !t.effect.SignalBloom();
                    }
                    row.bloomMaxOpening = Mathf.Max(row.bloomMaxOpening,t.effect.BloomOpened);
                    if(t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.bloomVisibleFrames++;
                }
                if (row.expectsRegrowth)
                {
                    while(row.regrowthTicks < 6 && t.effect.Age >= .45f + row.regrowthTicks*.45f)
                    {
                        if(!t.effect.SignalRegrowthTick()) break;
                        row.regrowthTicks++;
                    }
                    row.regrowthOpenedMask |= t.effect.RegrowthOpenedMask;
                    if(t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.regrowthVisibleFrames++;
                }
                if (row.expectsWoodWard)
                {
                    if(row.wardHitSignals == 0 && t.effect.Age >= .95f && t.effect.SignalWoodWardHit(1)) row.wardHitSignals++;
                    if(row.wardHitSignals == 1 && t.effect.Age >= 1.6f && t.effect.SignalWoodWardHit(4)) row.wardHitSignals++;
                    row.wardMaxTilt = Mathf.Max(row.wardMaxTilt,t.effect.WoodWardMaxTilt);
                    if(t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.wardVisibleFrames++;
                    if(t.effect.WoodWardInstance != null) for(int i=0;i<6;i++)
                    {
                        var part=t.effect.WoodWardInstance.transform.GetChild(i);
                        if(Quaternion.Angle(part.localRotation,Quaternion.Euler(0,30+i*60,0))>.05f) row.wardTiltMask |= 1<<i;
                    }
                }
                if (row.expectsBambooGuard)
                {
                    if (!row.syntheticBambooParry && t.effect.Age >= .36f)
                        row.syntheticBambooParry = Vfx120Effect.TrySignalBambooParry(t.effect.transform.TransformPoint(new Vector3(.25f,.2f,.83f)));
                    row.guardMaxBend = Mathf.Max(row.guardMaxBend, t.effect.BambooGuardBend);
                    if(t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.guardVisibleFrames++;
                }
                if (row.expectsInterceptions)
                {
                    for (int i = 0; i < t.interceptionPlans.Length; i++)
                        if (!t.interceptionPlans[i].HitConfirmed && t.effect.Age >= t.interceptionPlans[i].ArrivalAt)
                        {
                            bool confirmed = t.effect.ConfirmInterception(i, t.interceptionPlans[i].Point, t.effect.Age);
                            if (confirmed) { t.interceptionPlans[i].HitConfirmed = true; row.confirmedInterceptions++; }
                        }
                    if (t.renderers == null) t.renderers = t.root.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(r => r.name.StartsWith("Flecks_", StringComparison.Ordinal)).ToArray();
                    if (t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.interceptionEnabledFrames++;
                }
                if (Vfx120FrostCordMotion.IsPrepared(t.effect.Profile))
                {
                    row.expectsFrostCords = true;
                    if (t.renderers == null)
                        t.renderers = t.root.GetComponentsInChildren<MeshRenderer>(true)
                            .Where(r => r.name.StartsWith("Flecks_", StringComparison.Ordinal)).ToArray();
                    if (!row.syntheticFrostHitIssued && t.effect.Age >= t.effect.Profile.Flight)
                    {
                        t.effect.Signal(Vfx120Effect.Cue.Hit); row.syntheticFrostHitIssued = true;
                        _report.messages.Add("상: audit supplied a synthetic presentation Hit cue; no enemy projectile collision/freeze gameplay was exercised.");
                    }
                    if (t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.frostEnabledFrames++;
                }
                row.lastAliveAge = elapsed; row.maxEffectAge = Mathf.Max(row.maxEffectAge, t.effect.Age);
                Discover();
                row.nativeDiagnostic = t.effect.NativeDiagnostic; row.modelDiagnostic = t.effect.MeshyDiagnostic;
                row.noFallback &= !ContainsFallback(row.nativeDiagnostic) && !ContainsFallback(row.modelDiagnostic);
                int liveSystems = 0, capacity = 0;
                foreach (var track in t.motifs)
                {
                    var motif = track.motif; if (motif == null) continue;
                    int count = 0, allocated = 0, particles = 0;
                    foreach (var ps in track.systems) if (ps != null)
                    { count++; allocated += ps.main.maxParticles; particles += ps.particleCount; row.budgets &= ps.particleCount <= ps.main.maxParticles; }
                    liveSystems += count; capacity += allocated;
                    row.budgets &= motif.SourceSystemCount <= 32 && motif.ParticleBudget <= 400 && allocated <= 400 && particles <= allocated;
                    if (motif.InstanceCount > 0) row.budgets &= allocated == motif.ParticleBudget && count == motif.SourceSystemCount;
                    row.maxMotifAge = Mathf.Max(row.maxMotifAge, motif.Age);
                    if (motif == t.effect.NativeBody)
                    {
                        row.bodyPeak = Math.Max(row.bodyPeak, particles);
                        row.budgets &= count <= 12 && allocated <= 240;
                    }
                    else if (motif.MotifRole == Vfx120TraditionalMotif.Role.Cast) row.castPeak = Math.Max(row.castPeak, particles);
                    else if (motif.MotifRole == Vfx120TraditionalMotif.Role.Impact) row.impactPeak = Math.Max(row.impactPeak, particles);
                    else row.fieldPeak = Math.Max(row.fieldPeak, particles);
                }
                row.nativeSystemsPeak = Math.Max(row.nativeSystemsPeak, liveSystems); row.capacityPeak = Math.Max(row.capacityPeak, capacity);
                foreach (var collider in t.root.GetComponentsInChildren<Collider>(true)) row.noActiveCollider &= !collider.enabled || !collider.gameObject.activeInHierarchy;
                foreach (var pose in t.poses) if (pose.transform != null)
                    row.fixedPose &= (pose.transform.localPosition - pose.position).sqrMagnitude <= 1e-10f
                        && (pose.transform.localScale - pose.scale).sqrMagnitude <= 1e-10f
                        && Quaternion.Angle(pose.transform.localRotation, pose.rotation) <= .001f;
                if (row.expectsBotanical)
                {
                    row.noFallback &= t.effect.BotanicalConfigured && t.effect.BotanicalInstance != null;
                    row.modelDiagnostic = t.effect.BotanicalDiagnostic;
                    row.maxModelAge = Mathf.Max(row.maxModelAge, t.effect.Age);
                }
                if ((row.expectsModel && t.effect.MeshyModel != null) || row.expectsBotanical)
                {
                    var model = t.effect.MeshyModel;
                    if (model != null) row.maxModelAge = Mathf.Max(row.maxModelAge, model.Age);
                    if (t.renderers != null && t.renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy)) row.modelVisibleFrames++;
                    for (int i = 0; t.filters != null && i < t.filters.Length; i++)
                        if (t.filters[i] != null) row.sourceReferences &= i < t.sourceMeshes.Length && t.filters[i].sharedMesh == t.sourceMeshes[i];
                    for (int i = 0; t.renderers != null && i < t.renderers.Length; i++)
                        if (t.renderers[i] != null) row.sourceReferences &= i < t.sourceMaterials.Length && t.renderers[i].sharedMaterials.SequenceEqual(t.sourceMaterials[i]);
                }
            }
            else if (row.destroyedAt < 0) row.destroyedAt = elapsed;
            if (elapsed < row.life + 1f) return;
            row.rootGone = t.root == null; row.childrenGone = t.children.All(c => c == null);
            if(row.expectsVineField)row.vineMeshGone=t.ownedGuardMeshes!=null&&t.ownedGuardMeshes.All(m=>m==null);
            if(row.expectsBloom) row.bloomMeshesGone = t.ownedGuardMeshes != null && t.ownedGuardMeshes.All(m => m == null);
            if(row.expectsBambooGuard) row.guardMeshesGone = t.ownedGuardMeshes != null && t.ownedGuardMeshes.All(m => m == null);
            row.lateBy = row.destroyedAt < 0 ? elapsed - row.life : Mathf.Max(0, row.destroyedAt - row.life);
            float tolerance = Mathf.Max(.1f, 3 * row.maxScaledDelta);
            row.lifetime = row.rootGone && row.childrenGone && row.destroyedAt >= row.life - tolerance
                && row.lastAliveAge >= row.life - tolerance && row.lateBy <= tolerance;
            row.wholeSceneFrameMeanMs = t.deltas.Count == 0 ? 0 : t.deltas.Average();
            t.deltas.Sort(); row.wholeSceneFrameP95Ms = t.deltas.Count == 0 ? 0 : t.deltas[Math.Min(t.deltas.Count - 1, (int)Math.Ceiling(t.deltas.Count * .95) - 1)];
            row.wholeSceneObservedFps = row.wholeSceneFrameMeanMs > 0 ? 1000 / row.wholeSceneFrameMeanMs : 0;
            row.mainThreadMeanMs = row.mainThreadSamples > 0 ? t.cpuSum / row.mainThreadSamples : 0;
            row.gc0Collections = GC.CollectionCount(0) - t.gc0;
            bool visibleRoles = (!row.expectsCast || row.castPeak > 0) && (!row.expectsImpact || row.impactPeak > 0)
                && (!row.expectsField || row.fieldPeak > 0) && (!row.expectsBody || row.bodyPeak > 0)
                && (!row.expectsBotanical || row.modelVisibleFrames > 0);
            row.status = row.configured && row.noFallback && row.budgets && row.shaderFlags && row.fixedPose && row.noActiveCollider
                && row.sourceReferences && row.lifetime && row.maxEffectAge > 0 && row.maxMotifAge > 0 && visibleRoles
                && (!row.expectsFrostCords || row.syntheticFrostHitIssued && row.frostEnabledFrames > 0)
                && (!row.expectsInterceptions || row.confirmedInterceptions == Vfx120InterceptionMotion.MaxTargets && row.interceptionEnabledFrames > 0)
                && (!row.expectsWoodLift || row.woodLiftPlan && row.woodLiftReleased && row.woodLiftMaxHeight>2.09f && row.woodLiftVisibleFrames>0)
                && (!row.expectsStakeField || row.stakeContacts==2 && row.stakeMaxFeedback==2 && row.stakeVisibleFrames>0)
                && (!row.expectsRootLift || row.rootLiftMaxRise>.99f && row.rootLiftWithdrawn && row.rootLiftVisibleFrames>0)
                && (!row.expectsWetRoot || row.wetContact && row.wetMaxPaths==3 && row.wetVisibleFrames>0)
                && (!row.expectsLeafCut || row.leafCutIssued && row.leafExecution && row.leafCutVisibleFrames>0)
                && (!row.expectsSeedPod || row.podAttached && row.podDetonated && row.podVisibleFrames>0)
                && (!row.expectsSeedTransfer || row.transferRequested && row.transferHit && row.transferVisibleFrames>0)
                && (!row.expectsFireBolt || row.fireImpact && row.fireParticlePeak>0 && row.fireParticlePeak<=(row.glyph=="난"?528:row.glyph=="남"?464:row.glyph=="낫"?592:432) && row.firePatternPeak>.9f)
                && (row.glyph!="녹"||row.fireHealingPeak>.8f)
                && (row.glyph!="눈"||row.fireBurnProgress>=1)
                && (row.glyph!="농"||row.steamOnlySeen)
                && (row.glyph!="넝"||row.firePelletPeak>0&&row.firePelletPeak<=336&&row.firePelletLaunches==3&&row.firePelletContacts==3)
                && (row.glyph!="낫"||row.fireSplitPeak>1.3f)
                && (row.glyph!="낭"||row.firePenetrationPeak>1.4f)
                && (row.glyph!="너"||row.fireGuardParticlePeak>0&&row.fireGuardParticlePeak<=432&&row.fireGuardPulsePeak>.7f)
                && ((row.glyph!="넉"&&row.glyph!="넘"&&row.glyph!="놈"&&row.glyph!="놋"&&row.glyph!="농"&&row.glyph!="누")||row.fireAuraParticlePeak>0&&row.fireAuraParticlePeak<=432&&row.fireAuraContacts==3&&row.fireAuraMarkPeak>.8f)
                && ((row.glyph!="넌"&&row.glyph!="넛"&&row.glyph!="노"&&row.glyph!="녹"&&row.glyph!="논"&&row.glyph!="눈")||row.fireAuraParticlePeak>0&&row.fireAuraParticlePeak<=432&&row.fireAuraContacts==1&&row.fireAuraMarkPeak>.8f)
                && (!row.expectsBambooBolt || row.boltContact && row.boltVisibleFrames>0 && row.boltMaxTipError<.002f)
                && (!row.expectsVineField || row.vineMaxSpread>.99f && row.vineContacts==2 && row.vineMaxActiveContacts>0 && row.vineReleased && row.vineVisibleFrames>0 && row.vineMeshGone)
                && (!row.expectsBambooSpikes || row.spikesMaxRaised==17 && row.spikesAllRoseTogether && row.spikesVisibleFrames>0 && Mathf.Abs(row.spikesFirstFullAt-row.spikesPlanDelay)<Mathf.Max(.03f,3*row.maxScaledDelta))
                && (!row.expectsCompanionSeeds || row.companionConfirmedMask == 3 && row.companionSeparateHitSeen && row.companionOpenMask == 3 && row.companionVisibleFrames > 0)
                && (!row.expectsWoodSword || row.swordContactIssued && row.swordMaxTracePoints > 1 && row.swordMaxTracePoints <= 32 && row.swordTraceCleared && row.swordVisibleFrames > 0 && row.swordGripError < .002f)
                && (!row.expectsBloom || row.syntheticBloom && row.bloomRepeatRejected && row.bloomMaxOpening > .99f && row.bloomVisibleFrames > 0 && row.bloomMeshesGone)
                && (!row.expectsRegrowth || row.regrowthTicks == 6 && row.regrowthOpenedMask == 63 && row.regrowthVisibleFrames > 0)
                && (!row.expectsWoodWard || row.wardHitSignals == 2 && row.wardTiltMask == 18 && row.wardMaxTilt > .1f && row.wardVisibleFrames > 0)
                && (!row.expectsBambooGuard || row.syntheticBambooParry && row.guardMaxBend > .01f && row.guardVisibleFrames > 0 && row.guardMeshesGone)
                && (!row.expectsModel || row.modelVisibleFrames > 0 && row.maxModelAge > 0)
                ? "PASS_REAL_UPDATE_LIFETIME_ONLY" : "FAIL";
            _report.completed++; if (row.status == "FAIL") _report.failed++;
            if (t.root != null) Object.Destroy(t.root); foreach (var child in t.children) if (child != null) Object.Destroy(child);
            _current = null; _settleUntil = Time.frameCount + 2; _report.stage = "SETTLE"; Save();
        }

        private static void Finish()
        {
            _report.cleanupObserved = Owned.All(go => go == null);
            CheckSources();
            _report.status = _report.completed == _report.glyphs.Length && _report.failed == 0 && _report.errors == 0
                && _report.sourceUnchanged && _report.cleanupObserved ? "PASS_REAL_UPDATE_LIFETIME_ONLY" : "COMPLETED_WITH_FINDINGS";
            Exit();
        }
        private static void Abort(string message)
        {
            if (!Active || _report.stage == "WAIT_EXIT") return;
            _report.status = "ABORTED"; _report.messages.Add(message);
            if (_current != null && _current.row.status == "RUNNING") _current.row.status = "UNVERIFIED_ABORTED";
            foreach (var go in Owned) if (go != null) { go.SetActive(false); Object.Destroy(go); }
            if (_current != null) foreach (var child in _current.children) if (child != null) Object.Destroy(child);
            // Requesting Destroy is not recorded as observed cleanup.
            _report.cleanupObserved = false;
            try { if (Assets.Count > 0) CheckSources(); }
            catch (Exception error) { _report.sourceUnchanged = false; _report.messages.Add("Source check interrupted: " + error.Message); }
            finally { Exit(); }
        }
        private static void Exit()
        {
            if (_cpu.Valid) _cpu.Dispose();
            RestoreSettings(); _report.stage = "WAIT_EXIT"; Save();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) CompleteExit();
        }
        private static void CompleteExit()
        {
            if (!Active || _report.stage != "WAIT_EXIT" || EditorApplication.isPlayingOrWillChangePlaymode) return;
            RestoreSettings();
            _report.sceneHashAfter = FileHash(Absolute(ScenePath)); _report.camerasAfter = CameraHash();
            _report.sceneUnchanged = _report.sceneHashBefore == _report.sceneHashAfter;
            _report.restored = SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == ScenePath
                && !SceneManager.GetActiveScene().isDirty && _report.sceneUnchanged && _report.camerasBefore == _report.camerasAfter
                && QualitySettings.vSyncCount == _report.savedVsync && Application.targetFrameRate == _report.savedFps
                && Mathf.Approximately(Time.timeScale, _report.savedTimeScale) && Application.runInBackground == _report.savedBackground;
            if (!_report.restored && _report.status != "ABORTED") _report.status = "COMPLETED_WITH_FINDINGS";
            _report.stage = "FINISHED"; Save();
        }
        private static void RestoreSettings()
        {
            if (_report == null) return;
            Application.runInBackground = _report.savedBackground; Time.timeScale = _report.savedTimeScale;
            QualitySettings.vSyncCount = _report.savedVsync; Application.targetFrameRate = _report.savedFps;
        }
        private static void BeforeReload()
        {
            if (!Active) return;
            if (_report.stage == "WAIT_ENTER" || _report.stage == "WAIT_EXIT") Save();
            else Abort("Script/domain reload interrupted live observation; no continuation inferred.");
        }
        private static void Log(string message, string trace, LogType type)
        {
            if (!Active || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            _report.errors++; if (message.IndexOf("shader", StringComparison.OrdinalIgnoreCase) >= 0) _report.shaderErrors++;
            if (_report.messages.Count < 24) _report.messages.Add(type + ": " + message);
        }
        private static void Save(bool file = true)
        {
            if (_report == null) return;
            string json = JsonUtility.ToJson(_report, true); SessionState.SetString(StateKey, json); _lastSave = EditorApplication.timeSinceStartup;
            if (file)
            {
                try
                {
                    Directory.CreateDirectory(Vfx120Editor.Output);
                    File.WriteAllText(_report.output, json, new UTF8Encoding(false));
                    _report.outputWriteError = null;
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    // A locked report must never prevent Play exit or settings recovery.
                    _report.outputWriteError = error.GetType().Name + ": " + error.Message;
                    SessionState.SetString(StateKey, JsonUtility.ToJson(_report));
                }
            }
        }
        private static void Remember(Object asset, HashSet<string> paths)
        {
            foreach (string path in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset), true))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".prefab" && ext != ".asset" && ext != ".mat" && ext != ".fbx" && ext != ".glb" && ext != ".gltf") continue;
                if (!paths.Add(path)) continue;
                Assets[path] = AssetDatabase.LoadMainAssetAtPath(path);
                _report.sources.Add(new SourceRow { path = path, diskBefore = FileHash(Absolute(path)),
                    dependencyBefore = AssetDatabase.GetAssetDependencyHash(path).ToString(), memoryBefore = MemoryHash(path) });
            }
        }
        private static void CheckSources()
        {
            _report.sourceUnchanged = _report.sources.Count > 0;
            foreach (var source in _report.sources)
            {
                source.diskAfter = FileHash(Absolute(source.path)); source.dependencyAfter = AssetDatabase.GetAssetDependencyHash(source.path).ToString();
                source.memoryAfter = MemoryHash(source.path);
                source.unchanged = source.diskBefore == source.diskAfter && source.dependencyBefore == source.dependencyAfter && source.memoryBefore == source.memoryAfter;
                _report.sourceUnchanged &= source.unchanged;
            }
        }
        private static string MemoryHash(string path)
        {
            var b = new StringBuilder();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null) continue;
                b.Append(EditorUtility.IsDirty(asset)).Append(EditorJsonUtility.ToJson(asset));
                if (asset is GameObject go) foreach (var component in go.GetComponentsInChildren<Component>(true))
                    if (component != null) b.Append(EditorJsonUtility.ToJson(component));
            }
            return Hash(Encoding.UTF8.GetBytes(b.ToString()));
        }
        private static string CameraHash()
        {
            var b = new StringBuilder();
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.path == ScenePath).OrderBy(c => PathOf(c.transform)))
                b.Append(PathOf(camera.transform)).Append(JsonUtility.ToJson(new CameraState(camera)));
            return Hash(Encoding.UTF8.GetBytes(b.ToString()));
        }
        [Serializable] private struct CameraState
        {
            public Vector3 position, scale; public Quaternion rotation; public float fov, near, far, orthoSize; public bool enabled, orthographic;
            public Rect rect; public int cullingMask;
            public CameraState(Camera c) { position = c.transform.localPosition; scale = c.transform.localScale; rotation = c.transform.localRotation;
                fov = c.fieldOfView; near = c.nearClipPlane; far = c.farClipPlane; orthoSize = c.orthographicSize;
                enabled = c.enabled; orthographic = c.orthographic; rect = c.rect; cullingMask = c.cullingMask; }
        }
        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        private static bool ContainsFallback(string s) => !string.IsNullOrEmpty(s) && s.IndexOf("fallback", StringComparison.OrdinalIgnoreCase) >= 0;
        private static void RequireC2() => Need(SceneManager.GetActiveScene().path == ScenePath, "Active scene changed; no scene is opened/saved by this audit.");
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static string FileHash(string path) { if (!File.Exists(path)) return "MISSING"; using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return Hex(sha.ComputeHash(file)); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
