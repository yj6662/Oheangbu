using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>LateUpdate hook of the lock-on drawing measurement (after every game script). Lives only as a HideAndDontSave object
    /// in a measured Play.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class LockDrawProbe308 : MonoBehaviour
    {
        internal static Action Late;
        void LateUpdate() { Late?.Invoke(); }
        // domain reload is off: a Play session never inherits another session's hook
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPlaySession() { Late = null; }
    }

    /// <summary>#308 lock-on drawing stability, the Play measurement (SPEC-LOCKON-DRAW-STABILITY-308 TE-2 .. TE-6).
    /// Locks a held enemy, turns the view `offset` degrees off it, holds the draw key and replays a registered-template glyph through
    /// virtual keyboard / mouse devices (the real Input System path: DrawingInputController samples, the recogniser recognises),
    /// optionally strafing. For every render of the view camera it records where sampled stroke points are on screen against the
    /// pixels they were drawn at, three times: at beginCameraRendering BEFORE the stroke adapter's render-time anchor (what the
    /// code before this stage would have drawn: the answer to hypothesis H1), AFTER it (what is drawn now: AC-1) and again at
    /// endCameraRendering; plus the camera yaw / pitch, the target's viewport position and (a proxy) the distance fog behind the points.
    /// After the commit it follows the detached glyph for its fade. Output: Art/Playtest308/LockDraw/&lt;runId&gt;/measure_&lt;mode&gt;.json.
    /// Fixtures (all listed in the report, none saved): teleport, the view offset, LockOn.Toggle, enemy AI held within 40 m,
    /// PlayerMotor.LockOnDrawModeOverride308 (not serialised; the config asset is never written), virtual input devices.
    /// The real slot is never opened: "measure" makes its own isolated store (Harness303), "run" refuses on the real slot.</summary>
    public static partial class LockDraw308
    {
        const string SuffixTag = "_c303lockdraw_";
        const double RunLimit = 180;
        const string FogMaterialPath = "Assets/_Project/Art/World/Finish297/Materials/Fog297.mat";
        const float LiveDriftLimitPx = 1f;   // acceptance AC-1. One float step of a world coordinate at the main scene's coordinates is 0.25 px at the
                                             // drawing plane; screen positions are read with PreciseScreen (LockDraw308.cs), not Camera.WorldToScreenPoint,
                                             // which alone reads up to about 0.7 px off there (check 수리 2026-10-05)

        sealed class MOptions
        {
            public string mode = "asset", move = "strafe", target = "", glyph = "가", scene = "main";
            public float dist = 3.5f, offset = 18f, seconds = 1.5f; public bool holdAi = true, fog = true;
        }

        [Serializable] sealed class MFrame
        {
            public int phase; public float t, dt, timeScale, camYaw, camPitch, fov, playerYaw, yawError, ramp, followYaw, followPitch, targetVx, targetVy;
            public bool drawing, stroking, locked, targetInFront;
            public int liveSamples; public float liveMaxPx, endMaxPx, slidePx, fogMean;
            public float preMaxPx = -1f;   // the same points BEFORE the render-time anchor of this frame (-1 = not sampled before it)
            // H10: where Camera.ScreenToWorldPoint (the adapter's mapping before the check 수리) would put the stroke frame's anchor on this
            // frame, and how far that moved since the previous frame (-1 = not sampled). Read only: nothing is placed with it.
            public float engineCornerPx = -1f, engineJumpPx = -1f;
        }

        [Serializable] sealed class MReport
        {
            public string spec = "SPEC-LOCKON-DRAW-STABILITY-308", runId = "", verdict = "", mode = "", modeSource = "", options = "", scene = "", store = "", quality = "", unity = "";
            public string disclaimer = "automated input and transform sampling; not a human judgement of feel, and not a pixel readback of the GPU frame";
            public int screenW, screenH; public float fps; public bool appFocused, gameViewFocused;
            public string configAsset = ""; public float pull; public string assetMode = ""; public float settle, resumeDelay, resumeSeconds, edgeGain, edgeMaxSpeed, edgeAcceleration, aimKeep; public Vector2 edgeMargin;
            public string targetId = ""; public float targetDistance, startYawError;
            // recognition (the same glyph in every mode must give the same bytes)
            public bool committed, recognised; public string letter = "", glyphSource = "", recognitionInputSha = ""; public int strokes, points; public string worstDistanceBits = "", averageDistanceBits = "";
            // AC-1 live strokes
            public int drawFrames, liveSampleCount, renderAnchorCalls; public float liveMaxPx, liveRmsPx, liveEndMaxPx;
            // H1: the same points before the render-time anchor (= the picture of the code before this stage). preAnchorFrames counts
            // only the frames in which the adapter's anchor demonstrably ran between the two samples.
            public int preAnchorFrames, preAnchorFramesOver; public float preAnchorMaxPx;
            // H10: the engine's inverse mapping on the frames a stroke was alive (what the adapter used before the check 수리)
            public int engineFrames, engineJumpFrames; public float engineCornerMaxPx, engineCentreMaxPx, engineJumpMaxPx, engineJumpRmsPx;
            // camera while the draw key is down, after the settle
            public int writingFrames; public float writingTurnDeg, writingPitchTravelDeg, writingMaxDps, writingRmsDps, writingStillShare, writingMaxAccelDps2, drawStartStepDps;
            // target
            public Vector2 targetViewportAtCommit; public float yawErrorAtCommit, maxAbsYawError; public int targetOffScreenFrames;
            // after the commit
            public int postFrames; public float slideAt03, slideAt06, slideAt095, resumeFirstFrameDps, resumeMaxDps;
            // the largest slide inside the window, not only its last frame (a camera reaction that comes and goes within 0.3 s shows here)
            public float slideMax03, slideMax06;
            // distance fog behind the stroke points (proxy: collider raycast, RealmFog297 range from the material)
            public bool fogMeasured; public float fogStart, fogEnd, fogMeanMin, fogMeanMax, fogPointMaxStep; public int fogSkyRays, fogRays;
            public List<string> fixtures = new List<string>(), notes = new List<string>(), cleanup = new List<string>();
            public List<MFrame> frames = new List<MFrame>();
        }

        sealed class MState
        {
            public string status = "idle", note = "", runId = "", folder = ""; public bool ownsPlay; public MOptions o = new MOptions();
            public int phase; public double began, at; public readonly List<string> lines = new List<string>(); public MReport report = new MReport();
        }

        static MState ms = new MState(); static readonly Isolation303 miso = new Isolation303(); static bool mHooked;
        // live references: all dropped in MRelease (domain reload is off on entering Play; nothing here may outlive a run)
        static WorldMacroPlaytestSession mS; static PlayerMotor mMotor; static DrawingInputController mDrawing; static BrushStrokeFeedAdapter mAdapter; static LockOn mLock;
        static Camera mCamera; static CombatConfigSO mConfig; static EnemyVitals mTarget; static GameObject mProbe;
        static readonly List<Behaviour> mHeld = new List<Behaviour>(); static readonly List<List<Vector2>> mInputs = new List<List<Vector2>>();
        static List<BrushStrokeRenderer> mLive; static IList mFading; static readonly List<BrushStrokeRenderer> mDetached = new List<BrushStrokeRenderer>();
        static readonly List<Vector2> mDetachedBase = new List<Vector2>(); static readonly List<long> mDetachedKey = new List<long>();
        static readonly Dictionary<long, float> mFogLast = new Dictionary<long, float>();
        static Vector3 mHome, mStand; static float mHomeYaw, mStandYaw, mFpsSum, mCommitAt = -1f, mDrawStartAt = -1f, mFogA, mFogB; static int mFpsCount, mAnchorBefore;
        static bool mHomeSet, mInputOn, mRecording, mResubscribed, mSubscribed, mCommitted, mBackgroundSet, mPriorBackground, mTidied, mEventsOn; static double mStoppedAt = -1, mTeleportedAt;
        static MFrame mCurrent; static double mSumSq; static int mLockTries;
        static float mPrePx = -1f; static int mPreAnchorCount = -1;   // this render's sample before the adapter's anchor, and the anchor count seen then
        static Vector2 mEngineLast; static bool mEngineHas; static double mEngineJumpSumSq;   // H10: the engine mapping's corner offset on the previous sampled frame
        static RaycastHit[] mSightHits = new RaycastHit[16];

        static double MNow => EditorApplication.timeSinceStartup;
        static void MLine(string x) => ms.lines.Add(x);

        static string MeasureStatus() => ms.status + " | " + ms.note + " | phase " + ms.phase + (ms.report.verdict.Length > 0 ? " | " + ms.report.verdict : "") + (ms.folder.Length > 0 ? " | " + ms.folder : "");
        static string MeasureReport() => ms.lines.Count == 0 ? "no run yet" : string.Join("\n", ms.lines);
        static string MeasureAbort()
        {
            if (ms.status != "running" && ms.status != "finishing") return "nothing to abort (" + ms.status + ")";
            MLine("ABORTED by command"); MFinish("aborted", "INTERRUPTED"); return "aborted";
        }

        static bool TryMOptions(IEnumerable<string> tokens, out MOptions o, out string problem)
        {
            o = new MOptions(); problem = null; bool first = true;
            foreach (string raw in tokens)
            {
                string token = raw.Trim(); if (token.Length == 0) continue;
                if (first && token.IndexOf('=') < 0)
                {
                    first = false; token = token.ToLowerInvariant();
                    if (token != "full" && token != "edge" && token != "hold" && token != "asset") { problem = "mode must be full, edge, hold or asset"; return false; }
                    o.mode = token; continue;
                }
                first = false;
                int eq = token.IndexOf('='); string key = (eq < 0 ? token : token.Substring(0, eq)).ToLowerInvariant(), value = eq < 0 ? "" : token.Substring(eq + 1).Trim();
                bool Num(out float v) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                switch (key)
                {
                    case "move": if (value != "strafe" && value != "none") { problem = "move must be strafe or none"; return false; } o.move = value; break;
                    case "dist": if (!Num(out o.dist) || o.dist < 1.5f || o.dist > 15f) { problem = "dist must be 1.5 .. 15"; return false; } break;
                    case "offset": if (!Num(out o.offset) || Mathf.Abs(o.offset) > 60f) { problem = "offset must be -60 .. 60"; return false; } break;
                    case "seconds": if (!Num(out o.seconds) || o.seconds < .3f || o.seconds > 6f) { problem = "seconds must be 0.3 .. 6"; return false; } break;
                    case "target": o.target = value; break;
                    case "glyph": if (value.Length != 1) { problem = "glyph takes one letter"; return false; } o.glyph = value; break;
                    case "ai": if (value != "hold" && value != "live") { problem = "ai must be hold or live"; return false; } o.holdAi = value == "hold"; break;
                    case "fog": if (value != "on" && value != "off") { problem = "fog must be on or off"; return false; } o.fog = value == "on"; break;
                    case "scene": o.scene = value; break;
                    default: problem = "unknown option '" + token + "'"; return false;
                }
            }
            return true;
        }

        // null = isolated; otherwise the refusal (the rule of SpellSmoke308: the real slot is never used)
        static string MIsolated(WorldMacroPlaytestSession session)
        {
            string slot = session.Content != null ? session.Content.SaveSlot : "";
            string file = session.Spell308StoreFileName ?? "";
            if (slot.Length == 0 || file.Length == 0) return "refused: the session has not opened a store yet";
            if (string.Equals(file, slot + ".json", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(session.TestSaveSuffix) || !file.StartsWith(slot + "_", StringComparison.Ordinal))
                return "refused: this session opened '" + file + "', the real slot. The measurement runs only on an isolated store (use measure:<mode> from Edit Mode). Nothing was changed.";
            return null;
        }

        static string StartMeasure(IEnumerable<string> optionTokens, bool fromEdit)
        {
            if (ms.status == "running" || ms.status == "finishing" || ms.status == "cleaning") return "refused: already " + ms.status;
            if (!TryMOptions(optionTokens, out var o, out string problem)) return "refused: " + problem;
            if (fromEdit)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: measure is Edit Mode only (inside a Play on an isolated store use run:<mode>)";
                if (!Harness303.TryTarget(o.scene, out string scenePath, out string slot)) return "refused: scene must be main or folklore298";
                var stale = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                if (stale != null && (stale.TestSaveSuffix ?? "").StartsWith(SuffixTag, StringComparison.Ordinal)) { stale.TestSaveSuffix = ""; EditorUtility.ClearDirty(stale); }
                string why = Harness303.Prepare(miso, scenePath, slot, SuffixTag);
                if (why != null) return why;
                Harness303.Apply(miso, Object.FindFirstObjectByType<WorldMacroPlaytestSession>());
            }
            else
            {
                if (!EditorApplication.isPlaying) return "refused: run needs Play Mode (from Edit Mode use measure:<mode>)";
                var live = Harness303.Session;
                if (live == null || !live.InitializationComplete) return "refused: no started WorldMacroPlaytestSession (still on the title or loading)";
                string why = MIsolated(live);
                if (why != null) return why;
            }
            MRelease();
            ms = new MState { status = "running", ownsPlay = fromEdit, o = o, began = MNow, at = MNow, runId = Harness303.UtcStamp() };
            ms.folder = Path.Combine(Root, ms.runId);
            ms.report.runId = ms.runId; ms.report.mode = o.mode;
            ms.report.options = "move=" + o.move + " dist=" + F(o.dist, "F1") + " offset=" + F(o.offset, "F0") + " seconds=" + F(o.seconds, "F1") + " ai=" + (o.holdAi ? "hold" : "live") + " glyph=" + o.glyph + " fog=" + (o.fog ? "on" : "off");
            MLine("#308 lock-on drawing measurement " + DateTime.Now.ToString("s") + " run " + ms.runId + " mode " + o.mode + (fromEdit ? " isolated suffix " + miso.suffix + " (own Play)" : " inside the running Play") + " | " + ms.report.options);
            if (!mHooked) { EditorApplication.update += MTick; EditorApplication.playModeStateChanged += MOnPlayMode; mHooked = true; }
            if (fromEdit) EditorApplication.isPlaying = true;
            return "started lockdraw308 " + ms.runId + " mode " + o.mode + (fromEdit ? ", suffix " + miso.suffix : ", in the running Play") + " | folder " + ms.folder;
        }

        static void MOnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && ms.status == "running")
            {
                MLine("FAIL Play ended while the measurement was running (phase " + ms.phase + ")");
                MRestore(); MStopRecording(); ms.report.verdict = "done INTERRUPTED"; MWrite();
                ms.status = ms.ownsPlay ? "finishing" : "done INTERRUPTED"; if (!ms.ownsPlay) MRelease();
            }
        }

        static void MTick()
        {
            if (ms.status == "finishing" && !EditorApplication.isPlayingOrWillChangePlaymode) { MCleanup(); return; }
            if (ms.status != "running") return;
            if (MNow - ms.began > RunLimit) { MLine("FAIL timeout after " + RunLimit + " s in phase " + ms.phase + " (" + ms.note + ")"); MFinish("timeout", "FAIL"); return; }
            if (!EditorApplication.isPlaying) return;
            try { MStep(); }
            catch (Exception e) { MLine("FAIL harness exception: " + e); MFinish("harness error", "FAIL"); }
        }

        static void MStep()
        {
            if (mS == null) { mS = Harness303.Session; if (mS == null) { ms.note = "waiting for the session"; return; } }
            if (!mS.InitializationComplete || UiAdapter303.LoadingInProgress == true) { ms.note = "waiting for init / loading"; ms.at = MNow; return; }
            if (ms.phase == 0)
            {
                if (ms.ownsPlay && !mTidied) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); mTidied = true; ms.at = MNow; return; }
                if (MNow - ms.at < 3) { ms.note = "settling"; return; }
                string why = MSetup();
                if (why != null) { MLine("FAIL setup: " + why); MFinish("setup failed", "FAIL"); return; }
                ms.phase = 1; ms.at = MNow; mTeleportedAt = MNow; return;
            }
            if (Time.timeScale < .01f)
            {
                if (mStoppedAt < 0) mStoppedAt = MNow;
                ms.note = "time is stopped (page '" + UiAdapter303.Page + "')";
                var ui = Oheangbu.App.World.UI.PlaytestUiRoot.Instance; if (ui != null) ui.SkipTutorial306ForHarness();
                if (MNow - mStoppedAt > 2) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); }
                if (MNow - mStoppedAt > 12) { MLine("FAIL time scale stayed 0 for 12 s (page '" + UiAdapter303.Page + "')"); MFinish("blocked", "FAIL"); }
                return;
            }
            if (mStoppedAt >= 0) { ms.at += MNow - mStoppedAt; mStoppedAt = -1; }
            switch (ms.phase)
            {
                case 1: MLockPhase(); return;
                case 2: MPreRoll(); return;
                case 3: MDrawPhase(); return;
                case 4: if (Time.unscaledTime - mCommitAt >= 1.3f) MFinish("complete", null); return;
            }
        }

        static string MSetup()
        {
            string why = MIsolated(mS);
            if (why != null) return why;
            var walker = mS.Walker;
            if (walker == null || walker.Motor == null || walker.Drawing == null || walker.Wiring == null) return "the session's walker has no motor / drawing / wiring";
            mMotor = walker.Motor; mDrawing = walker.Drawing; mLock = mMotor.GetComponent<LockOn>(); mCamera = walker.ViewCamera != null ? walker.ViewCamera : Camera.main;
            mAdapter = mDrawing.GetComponent<BrushStrokeFeedAdapter>();
            if (mAdapter == null) mAdapter = Object.FindObjectsByType<BrushStrokeFeedAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(a => a.gameObject.scene == mS.gameObject.scene);
            mConfig = Harness303.Field<CombatConfigSO>(mMotor, "_config");
            if (mLock == null || mCamera == null || mAdapter == null || mConfig == null) return "missing lock-on / camera / stroke adapter / config (" + (mLock != null) + ", " + (mCamera != null) + ", " + (mAdapter != null) + ", " + (mConfig != null) + ")";
            if (mSeated(walker)) return "the player is seated (leave the vehicle first)";
            mLive = Harness303.FieldValue(mAdapter, "_strokes") as List<BrushStrokeRenderer>; mFading = Harness303.FieldValue(mAdapter, "_fading") as IList;
            if (mLive == null || mFading == null) return "the stroke adapter's stroke lists are not readable (field names changed?)";
            var r = ms.report;
            r.scene = mS.gameObject.scene.path; r.store = mS.Spell308StoreFileName; r.unity = Application.unityVersion;
            r.quality = QualitySettings.GetQualityLevel() + ":" + QualitySettings.names[QualitySettings.GetQualityLevel()];
            r.screenW = mCamera.pixelWidth; r.screenH = mCamera.pixelHeight; r.appFocused = Application.isFocused; r.gameViewFocused = Harness303.GameViewFocused;
            r.configAsset = AssetDatabase.GetAssetPath(mConfig); r.pull = mConfig.LockOnCameraPull; r.assetMode = mConfig.LockOnDrawMode.ToString();
            r.settle = mConfig.LockOnDrawSettleSeconds; r.resumeDelay = mConfig.LockOnDrawResumeDelay; r.resumeSeconds = mConfig.LockOnDrawResumeSeconds; r.edgeMargin = mConfig.LockOnDrawEdgeMargin;
            r.edgeGain = mConfig.LockOnDrawEdgeGain; r.edgeMaxSpeed = mConfig.LockOnDrawEdgeMaxSpeed; r.edgeAcceleration = mConfig.LockOnDrawEdgeAcceleration; r.aimKeep = mConfig.LockOnDrawAimKeepDegrees;
            if (r.pull <= 0f) return "LockOnCameraPull is 0 in " + r.configAsset + ": there is no pull to measure";
            MLine("INFO store " + r.store + " | quality " + r.quality + " | screen " + r.screenW + "x" + r.screenH + " | focus app " + r.appFocused + " game view " + r.gameViewFocused + " | config " + r.configAsset + " pull " + F(r.pull, "F2") + " asset mode " + r.assetMode);
            mPriorBackground = Application.runInBackground; Application.runInBackground = true; mBackgroundSet = true;
            mHome = walker.Body.transform.position; mHomeYaw = walker.Body.transform.eulerAngles.y;
            why = MPickTarget();
            if (why != null) return why;
            if (ms.o.holdAi)
            {
                foreach (var a in mS.Actors)
                {
                    if (a == null || Vector3.Distance(a.transform.position, mTarget.transform.position) > 40f) continue;
                    var ai = a.GetComponent<EnemyController>(); if (ai != null && ai.enabled) { ai.enabled = false; mHeld.Add(ai); }
                    if (a.enabled) { a.enabled = false; mHeld.Add(a); }
                }
                r.fixtures.Add("enemy AI held within 40 m of the target: " + mHeld.Count + " component(s) (EnemyController + PrologueEncounter), switched on again at the end");
            }
            // the view starts `offset` degrees off the target: the lock-on pull has something to correct (the reported situation)
            mS.Teleport(mStand, mStandYaw - ms.o.offset); mHomeSet = true;
            r.fixtures.Add("teleport to " + Harness303.V(mStand) + " facing " + F(ms.o.offset, "F0") + " deg off the target; the player is sent back to " + Harness303.V(mHome) + " at the end");
            int modeValue = ms.o.mode == "full" ? (int)LockOnDrawMode.FullPull : ms.o.mode == "edge" ? (int)LockOnDrawMode.EdgeOnly : ms.o.mode == "hold" ? (int)LockOnDrawMode.Hold : -1;
            mMotor.LockOnDrawModeOverride308 = modeValue;
            r.modeSource = modeValue < 0 ? "config asset (" + mConfig.LockOnDrawMode + ")" : "PlayerMotor.LockOnDrawModeOverride308 (not serialised, cleared at the end)";
            r.mode = mMotor.LockOnDrawModeNow.ToString();
            if (modeValue >= 0) r.fixtures.Add("draw pull mode forced to " + r.mode + " on the motor instance (the config asset is not written)");
            if (ms.o.fog)
            {
                var fog = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
                if (fog != null && fog.HasProperty("_FogRange")) { var v = fog.GetVector("_FogRange"); mFogA = v.x; mFogB = v.y; r.fogMeasured = mFogB > mFogA; r.fogStart = mFogA; r.fogEnd = mFogB; }
                if (!r.fogMeasured) r.notes.Add("fog proxy off: " + FogMaterialPath + " has no usable _FogRange");
            }
            return null;
        }

        static bool mSeated(WorldMacroCombatWalker walker) => walker.Seated;

        static string MPickTarget()
        {
            var targets = Harness303.FieldValue(mS.Walker.Wiring, "_targets") as List<EnemyVitals>;
            if (targets == null || targets.Count == 0) return "the wiring has no enemy targets";
            var feet = mS.Walker.Body.transform.position;
            if (ms.o.target.Length > 0)
            {
                var actor = mS.Actors.FirstOrDefault(a => a != null && a.Id == ms.o.target);
                mTarget = actor != null ? actor.GetComponent<EnemyVitals>() : null;
                if (mTarget == null || !targets.Contains(mTarget)) return "target '" + ms.o.target + "' is not an enemy of this wiring";
                if (!mTarget.IsAlive || !mTarget.isActiveAndEnabled) return "target '" + ms.o.target + "' is dead or inactive in this save";
                if (!MTryStand(mTarget, out mStand, out mStandYaw)) return "no NavMesh spot " + F(ms.o.dist, "F1") + " m from target " + mTarget.name + " with a clear line of sight";
            }
            else
            {
                var boss = mS.Actors.FirstOrDefault(a => a != null && a.Id == MineTutorialProfileSO.BossId && a.gameObject.activeInHierarchy);
                foreach (var candidate in targets.Where(t => t != null && t.IsAlive && t.isActiveAndEnabled && !t.IsBoss).OrderBy(t => Harness303.Flat(t.transform.position, feet)))
                {
                    if (boss != null && Harness303.Flat(candidate.transform.position, boss.transform.position) < 60f) continue;
                    if (!MTryStand(candidate, out mStand, out mStandYaw)) continue;
                    mTarget = candidate; break;
                }
                if (mTarget == null) return "no living non-boss enemy with NavMesh ground " + F(ms.o.dist, "F1") + " m from it and a clear line of sight; name one with target=<actor id>";
            }
            var encounter = mTarget.GetComponent<Oheangbu.App.Prologue.PrologueEncounter>();
            ms.report.targetId = encounter != null ? encounter.Id : mTarget.name; ms.report.targetDistance = Harness303.Flat(mStand, mTarget.transform.position);
            MLine("INFO target " + ms.report.targetId + " at " + Harness303.V(mTarget.transform.position) + " | stand " + Harness303.V(mStand) + " (" + F(ms.report.targetDistance, "F2") + " m) yaw " + F(mStandYaw, "F0"));
            return null;
        }

        static bool MTryStand(EnemyVitals enemy, out Vector3 feet, out float yaw)
        {
            var e = enemy.transform.position;
            for (int k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0, k * 45f, 0) * enemy.transform.forward;
                if (!NavMesh.SamplePosition(e + dir * ms.o.dist, out var hit, 1.2f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - e.y) > 1.5f) continue;
                bool clear = true;
                foreach (float height in new[] { 1.5f, .9f })
                {
                    Vector3 origin = hit.position + Vector3.up * height, delta = e + Vector3.up * 1.1f - origin;
                    int count = ScenePhysicsQuery.RaycastAll(enemy.gameObject.scene, origin, delta, delta.magnitude, ~0, ref mSightHits);
                    for (int i = 0; i < count; i++)
                    {
                        var t = mSightHits[i].transform;
                        if (mSightHits[i].collider.isTrigger || t.IsChildOf(enemy.transform) || t.IsChildOf(mS.Walker.Body.transform)) continue;
                        clear = false; break;
                    }
                }
                if (!clear) continue;
                var look = Vector3.ProjectOnPlane(e - hit.position, Vector3.up);
                feet = hit.position; yaw = look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f; return true;
            }
            feet = default; yaw = 0f; return false;
        }

        // phase 1: the camera has followed the teleport; lock the target (the public selector, a fixture)
        static void MLockPhase()
        {
            ms.note = "locking";
            if (MNow - mTeleportedAt < .8) return;
            if (mLock.Target != mTarget)
            {
                if (mLockTries >= 4) { MLine("FAIL the selector did not lock the target after 4 tries (locked: " + (mLock.Target != null ? mLock.Target.name : "none") + "; the target must be on screen and inside LockOnRange)"); MFinish("no lock", "FAIL"); return; }
                if (mLock.Target != null) mLock.Toggle();
                // the selector prefers the enemy nearest the screen centre: offer it the target alone, then give it the wiring's list back
                var all = Harness303.FieldValue(mS.Walker.Wiring, "_targets") as List<EnemyVitals>;
                mLock.SetCandidates(new[] { mTarget }); mLock.Toggle();
                if (all != null) mLock.SetCandidates(all);
                mLockTries++; ms.at = MNow; return;
            }
            ms.report.fixtures.Add("LockOn.Toggle (the public selector, no Tab key) x" + mLockTries + " with the candidate list narrowed to the target for the call and put back");
            mProbe = new GameObject("LockDraw308 probe") { hideFlags = HideFlags.HideAndDontSave };
            mProbe.AddComponent<LockDrawProbe308>(); LockDrawProbe308.Late = MLate;
            // order matters: MOnBeginPre is subscribed here, before any stroke exists, so it is called BEFORE the adapter's render-time
            // anchor (the adapter subscribes on the first stroke of a letter); MOnBegin is moved behind the adapter in MLate.
            RenderPipelineManager.beginCameraRendering += MOnBeginPre;
            RenderPipelineManager.beginCameraRendering += MOnBegin; RenderPipelineManager.endCameraRendering += MOnEnd; mSubscribed = true;
            mDrawing.StrokeStarted += MOnStrokeStarted; mDrawing.StrokePointAdded += MOnStrokePoint; mDrawing.Committed += MOnCommitted; mDrawing.CommitDiagnosed += MOnDiagnosed; mEventsOn = true;
            mRecording = true; mAnchorBefore = mAdapter.RenderAnchorCount308;
            ms.phase = 2; ms.at = MNow;
        }

        // phase 2: half a second locked, not drawing: the ordinary pull is running (and the frame rate is read)
        static void MPreRoll()
        {
            ms.note = "pre-roll";
            if (MNow - ms.at < .5) return;
            float fps = mFpsCount > 0 ? mFpsSum / mFpsCount : 60f; ms.report.fps = fps;
            string why = MQueueGlyph(fps);
            if (why != null) { MLine("FAIL glyph: " + why); MFinish("no glyph", "FAIL"); return; }
            Vector3 to = mTarget.transform.position - mMotor.transform.position;
            ms.report.startYawError = Mathf.DeltaAngle(mMotor.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            ms.phase = 3; ms.at = MNow;
        }

        static string MQueueGlyph(float fps)
        {
            var library = Harness303.Field<JamoTemplateLibrarySO>(mDrawing, "_templates");
            if (library == null) return "the drawing controller has no template library";
            List<List<Vector2>> glyph; int w = mCamera.pixelWidth, h = mCamera.pixelHeight;
            string recipePath = Path.Combine(Harness303.Roadside303Folder, "CombatTest", "stroke-recipe.json");
            StrokeRecipe303 recipe = null;
            if (File.Exists(recipePath)) { try { recipe = JsonUtility.FromJson<StrokeRecipe303>(File.ReadAllText(recipePath)); } catch { recipe = null; } }
            if (recipe != null && recipe.letter == ms.o.glyph && recipe.gate != "FAIL" && !string.IsNullOrEmpty(recipe.initialTemplate))
            {
                glyph = Strokes303.Glyph(recipe, library, w, h); ms.report.glyphSource = "stroke-lab recipe " + recipe.initialTemplate + " + " + recipe.medialTemplate;
            }
            else
            {
                // no recipe for this letter: the first registered templates of its two jamo in the stroke-lab layout (recognition is recorded, not required)
                int syllable = ms.o.glyph[0] - 0xAC00;
                if (syllable < 0 || syllable >= 11172 || syllable % 28 != 0) return "glyph must be an initial + medial syllable without a final (e.g. 가)";
                string initial = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"[syllable / (21 * 28)].ToString(); int vowel = syllable / 28 % 21;
                string medial = vowel == 0 ? "ㅏ" : vowel == 4 ? "ㅓ" : vowel == 8 ? "ㅗ" : vowel == 13 ? "ㅜ" : null;
                if (medial == null) return "glyph medial must be ㅏ ㅓ ㅗ ㅜ";
                var ini = Strokes303.Templates(library, "_initials").FirstOrDefault(t => t != null && Strokes303.TemplateName(t) == initial);
                var med = Strokes303.Templates(library, "_medials").FirstOrDefault(t => t != null && Strokes303.TemplateName(t) == medial);
                if (ini == null || med == null) return "no registered template for " + initial + " / " + medial;
                bool horizontal = medial == "ㅗ" || medial == "ㅜ";
                Rect iniRect = horizontal ? new Rect(.39f, .55f, .22f, .23f) : new Rect(.29f, .34f, .21f, .34f), medRect = horizontal ? new Rect(.34f, .29f, .33f, .19f) : new Rect(.55f, .31f, .18f, .40f);
                glyph = Strokes303.Layout(Strokes303.Read(ini), iniRect, w, h, 12).Concat(Strokes303.Layout(Strokes303.Read(med), medRect, w, h, 8)).ToList();
                ms.report.glyphSource = "library templates " + ini.name + " + " + med.name + " (no stroke-lab recipe for " + ms.o.glyph + ")";
            }
            var frames = Strokes303.Frames(glyph, 1, out int points, out _);
            // stretch to the wanted length: every frame is fed `repeat` times (the 2 px filter adds no sample for a pointer that did not move)
            int repeat = Mathf.Max(1, Mathf.RoundToInt(ms.o.seconds * fps / Mathf.Max(1, frames.Count - 1)));
            var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(mMotor, "_actions"), Harness303.Field<InputActionAsset>(mDrawing, "_actions") };
            string input = VirtualInput303.Begin(assets); mInputOn = true;
            ms.report.fixtures.Add("virtual keyboard / mouse through the existing actions: " + input);
            Key strafe = ms.o.offset >= 0f ? Key.A : Key.D;   // away from the side the target is on: the bearing error grows
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i]; bool last = i == frames.Count - 1;
                if (!last && ms.o.move == "strafe") f = f.WithKeys(f.Keys.Concat(new[] { strafe }).ToArray());
                for (int k = 0; k < (last ? 1 : repeat); k++) VirtualInput303.Enqueue(f);
            }
            ms.report.notes.Add("glyph " + ms.o.glyph + ": " + points + " points in " + glyph.Count + " stroke(s), " + frames.Count + " script frames x " + repeat + " = about " + F((frames.Count - 1) * repeat / Mathf.Max(1f, fps), "F2") + " s at " + F(fps, "F0") + " fps" +
                (ms.o.move == "strafe" ? ", strafing with " + strafe : ", standing"));
            return null;
        }

        // phase 3: the script plays; wait for the commit
        static void MDrawPhase()
        {
            ms.note = "drawing (" + VirtualInput303.Pending + " frames queued)";
            if (mDrawStartAt < 0f && mDrawing.InDrawMode) mDrawStartAt = Time.unscaledTime;
            if (mDrawStartAt < 0f && MNow - ms.at > 3)
            {
                MLine("UNCONFIRMED the draw mode did not start within 3 s of the draw key (focus app " + Application.isFocused + ", game view " + Harness303.GameViewFocused + ", entry gate " + (mDrawing.EntryAllowed == null || mDrawing.EntryAllowed()) + ")");
                MFinish("no draw mode", "UNCONFIRMED"); return;
            }
            if (mCommitted) { ms.phase = 4; ms.at = MNow; return; }
            if (VirtualInput303.Pending == 0 && !mDrawing.InDrawMode && mDrawStartAt >= 0f && MNow - ms.at > ms.o.seconds + 4)
            { MLine("UNCONFIRMED the draw mode ended without a commit event (cancelled: too few points?)"); MFinish("no commit", "UNCONFIRMED"); }
        }

        // ------------------------------------------------------------------ recording
        static void MOnStrokeStarted() { mInputs.Add(new List<Vector2>()); }
        static void MOnStrokePoint(Vector2 screen) { if (mInputs.Count > 0) mInputs[mInputs.Count - 1].Add(screen); }
        static void MOnCommitted(bool success) { mCommitted = true; ms.report.committed = true; ms.report.recognised = success; mCommitAt = Time.unscaledTime; }

        // the recogniser's input, read on the commit frame before the controller clears it: the bytes of every sample position
        static void MOnDiagnosed(DrawingInputController.CommitDiagnostics d)
        {
            var r = ms.report; r.letter = d.Success ? d.Letter.ToString() : ""; r.strokes = d.StrokeCount; r.points = d.PointCount;
            r.worstDistanceBits = BitConverter.ToString(BitConverter.GetBytes(d.WorstDistance)); r.averageDistanceBits = BitConverter.ToString(BitConverter.GetBytes(d.AverageDistance));
            var strokes = Harness303.FieldValue(mDrawing, "_strokes") as List<StrokeData>;
            if (strokes == null) { r.notes.Add("recogniser input not readable (field _strokes)"); return; }
            using (var sha = SHA256.Create()) using (var stream = new MemoryStream())
            {
                foreach (var stroke in strokes)
                {
                    stream.Write(BitConverter.GetBytes(stroke.Points.Count), 0, 4);
                    foreach (var p in stroke.Points) { stream.Write(BitConverter.GetBytes(p.Position.x), 0, 4); stream.Write(BitConverter.GetBytes(p.Position.y), 0, 4); }
                }
                r.recognitionInputSha = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }

        // after every game LateUpdate: once the first stroke exists the stroke adapter has subscribed its render-time anchor,
        // so subscribe again to be called after it (the recorded pose is the one that is drawn)
        static void MLate()
        {
            if (!mRecording) return;
            if (!mResubscribed && mLive != null && mLive.Count > 0)
            {
                RenderPipelineManager.beginCameraRendering -= MOnBegin; RenderPipelineManager.beginCameraRendering += MOnBegin; mResubscribed = true;
            }
            if (ms.phase == 2 && Time.unscaledDeltaTime > 0f) { mFpsSum += 1f / Time.unscaledDeltaTime; mFpsCount++; }
        }

        static float MLiveDrift(Camera camera, bool fog, out int samples)
        {
            float max = 0f; samples = 0; float fogSum = 0f; int fogCount = 0;
            int strokes = Mathf.Min(mLive.Count, mInputs.Count);
            for (int k = 0; k < strokes; k++)
            {
                var stroke = mLive[k]; if (stroke == null) continue;
                var points = stroke.Data.Points; var inputs = mInputs[k]; int count = Mathf.Min(points.Count, inputs.Count); int step = Mathf.Max(1, count / 12);
                for (int i = 0; i < count; i += step)
                {
                    Vector3 world = stroke.transform.TransformPoint(points[i].Position);
                    float d = (float)PixelOff(camera, world, inputs[i]);   // double-precision projection (not Camera.WorldToScreenPoint)
                    if (d > max) max = d; samples++; mSumSq += (double)d * d;
                    if (fog && ms.report.fogMeasured && fogCount < 16)
                    {
                        // what the fog pass sees behind this stroke pixel: the first solid along the view ray (colliders only: a proxy)
                        Vector3 dir = (world - camera.transform.position).normalized; float share = 0f; ms.report.fogRays++;
                        if (Physics.Raycast(world, dir, out RaycastHit hit, 3000f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            float t = Mathf.Clamp01((hit.distance + Vector3.Distance(world, camera.transform.position) - mFogA) / Mathf.Max(1f, mFogB - mFogA)); share = t * t * (3f - 2f * t);
                        }
                        else ms.report.fogSkyRays++;
                        long key = (long)k * 100000 + i;
                        if (mFogLast.TryGetValue(key, out float before)) ms.report.fogPointMaxStep = Mathf.Max(ms.report.fogPointMaxStep, Mathf.Abs(share - before));
                        mFogLast[key] = share; fogSum += share; fogCount++;
                    }
                }
            }
            if (fog && fogCount > 0 && mCurrent != null) mCurrent.fogMean = fogSum / fogCount;
            return max;
        }

        // before the adapter's render-time anchor: the strokes still sit where LateUpdate put them, the camera is already the one
        // that is drawn (earlier subscribers - shakes, the field-of-view breath - have run). This is the picture without the fix.
        static void MOnBeginPre(ScriptableRenderContext context, Camera camera)
        {
            mPrePx = -1f; mPreAnchorCount = -1;
            if (!mRecording || camera != mCamera || mAdapter == null || mLive == null || mLive.Count == 0) return;
            double keep = mSumSq;
            mPrePx = MLiveDrift(camera, false, out _); mSumSq = keep;   // not part of the rms of what is drawn
            mPreAnchorCount = mAdapter.RenderAnchorCount308;
        }

        static void MOnBegin(ScriptableRenderContext context, Camera camera)
        {
            if (!mRecording || camera != mCamera || mMotor == null || mTarget == null) return;
            var f = new MFrame
            {
                phase = ms.phase, t = Time.unscaledTime, dt = Time.unscaledDeltaTime, timeScale = Time.timeScale, fov = camera.fieldOfView,
                camYaw = camera.transform.eulerAngles.y, camPitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x), playerYaw = mMotor.transform.eulerAngles.y,
                ramp = mMotor.LockDrawRamp308, followYaw = mMotor.LockDrawFollowSpeed308.x, followPitch = mMotor.LockDrawFollowSpeed308.y,
                drawing = mDrawing.InDrawMode, stroking = mDrawing.IsStroking, locked = mLock.Target == mTarget, slidePx = -1f,
            };
            Vector3 vp = camera.WorldToViewportPoint(mTarget.transform.position + Vector3.up * 1.1f);
            f.targetVx = vp.x; f.targetVy = vp.y; f.targetInFront = vp.z > 0f;
            Vector3 to = mTarget.transform.position - mMotor.transform.position;
            f.yawError = Mathf.DeltaAngle(f.playerYaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);   // what the forward-fan spells see
            mCurrent = f;
            if (mLive.Count > 0) { f.liveMaxPx = MLiveDrift(camera, true, out f.liveSamples); ms.report.liveSampleCount += f.liveSamples; ms.report.liveMaxPx = Mathf.Max(ms.report.liveMaxPx, f.liveMaxPx); ms.report.drawFrames++; }
            MEngineMapping(camera, f);
            // the pre-anchor sample counts only when the adapter's anchor ran between it and this call (the order is proven per frame, not assumed)
            if (mLive.Count > 0 && mPrePx >= 0f && mAdapter != null && mAdapter.RenderAnchorCount308 > mPreAnchorCount)
            {
                f.preMaxPx = mPrePx; ms.report.preAnchorFrames++; ms.report.preAnchorMaxPx = Mathf.Max(ms.report.preAnchorMaxPx, mPrePx);
                if (mPrePx > LiveDriftLimitPx) ms.report.preAnchorFramesOver++;
            }
            mPrePx = -1f; mPreAnchorCount = -1;
            if (mCommitted) f.slidePx = MDetachedSlide(camera);
            ms.report.frames.Add(f);
        }

        // H10 by measurement. The adapter placed the stroke frame with Camera.ScreenToWorldPoint until the check 수리 of 2026-10-05; that
        // function goes through a single-precision inverse view-projection matrix and is several px off at the main scene's coordinates,
        // differently on every frame the camera turns or moves. Here: where that function puts the frame's anchor (the screen corner at
        // the drawing depth) and the screen centre on this frame, read with PreciseScreen. Nothing is placed with it.
        static void MEngineMapping(Camera camera, MFrame f)
        {
            if (mLive == null || mLive.Count == 0 || mAdapter == null) { mEngineHas = false; return; }
            var r = ms.report; float depth = mAdapter.EffectiveSurfaceDistance(camera); Rect rect = camera.pixelRect;
            Vector2 corner = new Vector2(rect.x, rect.y), centre = rect.center;
            Vector2 offCorner = PreciseScreen(camera, camera.ScreenToWorldPoint(new Vector3(corner.x, corner.y, depth))) - corner;
            Vector2 offCentre = PreciseScreen(camera, camera.ScreenToWorldPoint(new Vector3(centre.x, centre.y, depth))) - centre;
            f.engineCornerPx = offCorner.magnitude; r.engineFrames++;
            r.engineCornerMaxPx = Mathf.Max(r.engineCornerMaxPx, f.engineCornerPx); r.engineCentreMaxPx = Mathf.Max(r.engineCentreMaxPx, offCentre.magnitude);
            if (mEngineHas)
            {
                f.engineJumpPx = (offCorner - mEngineLast).magnitude; r.engineJumpFrames++;
                r.engineJumpMaxPx = Mathf.Max(r.engineJumpMaxPx, f.engineJumpPx); mEngineJumpSumSq += (double)f.engineJumpPx * f.engineJumpPx;
            }
            mEngineLast = offCorner; mEngineHas = true;
        }

        static void MOnEnd(ScriptableRenderContext context, Camera camera)
        {
            if (!mRecording || camera != mCamera || mCurrent == null || mLive == null || mLive.Count == 0) return;
            double keep = mSumSq;
            mCurrent.endMaxPx = MLiveDrift(camera, false, out _); mSumSq = keep;   // the end sample is a cross-check, not part of the rms
            ms.report.liveEndMaxPx = Mathf.Max(ms.report.liveEndMaxPx, mCurrent.endMaxPx);
        }

        // the glyph after the commit: a world object now. How far its points move on screen from where they were on the first frame.
        static float MDetachedSlide(Camera camera)
        {
            if (mDetached.Count == 0)
            {
                if (mFading.Count == 0) return -1f;
                var group = mFading[mFading.Count - 1]; var field = group.GetType().GetField("Strokes");
                var strokes = field != null ? field.GetValue(group) as List<BrushStrokeRenderer> : null;
                if (strokes == null) return -1f;
                for (int k = 0; k < strokes.Count; k++)
                {
                    var stroke = strokes[k]; if (stroke == null) continue;
                    var points = stroke.Data.Points; int step = Mathf.Max(1, points.Count / 8);
                    for (int i = 0; i < points.Count; i += step)
                    {
                        Vector2 screen = PreciseScreen(camera, stroke.transform.TransformPoint(points[i].Position));
                        mDetached.Add(stroke); mDetachedKey.Add(i); mDetachedBase.Add(screen);
                    }
                }
                return 0f;
            }
            float max = 0f;
            for (int j = 0; j < mDetached.Count; j++)
            {
                var stroke = mDetached[j]; if (stroke == null) continue;   // the fade destroyed it
                Vector2 screen = PreciseScreen(camera, stroke.transform.TransformPoint(stroke.Data.Points[(int)mDetachedKey[j]].Position));
                max = Mathf.Max(max, Vector2.Distance(screen, mDetachedBase[j]));
            }
            return max;
        }

        static void MStopRecording()
        {
            mRecording = false;
            if (mSubscribed) { RenderPipelineManager.beginCameraRendering -= MOnBeginPre; RenderPipelineManager.beginCameraRendering -= MOnBegin; RenderPipelineManager.endCameraRendering -= MOnEnd; mSubscribed = false; }
            if (mEventsOn && mDrawing != null) { mDrawing.StrokeStarted -= MOnStrokeStarted; mDrawing.StrokePointAdded -= MOnStrokePoint; mDrawing.Committed -= MOnCommitted; mDrawing.CommitDiagnosed -= MOnDiagnosed; }
            mEventsOn = false;
            if (LockDrawProbe308.Late == (Action)MLate) LockDrawProbe308.Late = null;
            if (mProbe != null) { if (EditorApplication.isPlaying) Object.Destroy(mProbe); else Object.DestroyImmediate(mProbe); }
            mProbe = null;
        }

        // ------------------------------------------------------------------ end
        static void MRestore()
        {
            try
            {
                if (mInputOn) { string end = VirtualInput303.End(out bool ok); mInputOn = false; ms.report.fixtures.Add("virtual input ended: " + end); if (!ok) MLine("FAIL virtual input revert: " + end); }
                if (mMotor != null) mMotor.LockOnDrawModeOverride308 = -1;
                if (mLock != null && mLock.Target != null) mLock.Toggle();
                if (mHomeSet && mS != null && EditorApplication.isPlaying) mS.Teleport(mHome, mHomeYaw);
                foreach (var h in mHeld) if (h != null) h.enabled = true;
                if (mBackgroundSet) Application.runInBackground = mPriorBackground;
            }
            catch (Exception e) { MLine("INFO restore: " + e.Message); }
            mHeld.Clear(); mBackgroundSet = false; mHomeSet = false;
        }

        static void MSummarise()
        {
            var r = ms.report; var frames = r.frames;
            r.renderAnchorCalls = mAdapter != null ? mAdapter.RenderAnchorCount308 - mAnchorBefore : 0;
            r.liveRmsPx = r.liveSampleCount > 0 ? (float)Math.Sqrt(mSumSq / r.liveSampleCount) : 0f;
            r.engineJumpRmsPx = r.engineJumpFrames > 0 ? (float)Math.Sqrt(mEngineJumpSumSq / r.engineJumpFrames) : 0f;
            // the writing window: draw key down, after the settle, up to the commit
            float drawStart = -1f; foreach (var f in frames) if (f.drawing) { drawStart = f.t; break; }
            float windowFrom = drawStart + Mathf.Max(r.settle, .25f), windowTo = mCommitAt > 0f ? mCommitAt : float.MaxValue;
            MFrame prev = null, first = null; double sum = 0; int still = 0; float prevSpeedYaw = 0f; bool havePrevSpeed = false;
            float pitchMin = float.MaxValue, pitchMax = float.MinValue, fogMin = float.MaxValue, fogMax = float.MinValue;
            foreach (var f in frames)
            {
                if (f.drawing && f.dt > 0f)
                {
                    if (!f.targetInFront || f.targetVx < 0f || f.targetVx > 1f || f.targetVy < 0f || f.targetVy > 1f) r.targetOffScreenFrames++;
                    r.maxAbsYawError = Mathf.Max(r.maxAbsYawError, Mathf.Abs(f.yawError));
                }
                if (drawStart < 0f || !f.drawing || f.t < windowFrom || f.t > windowTo || f.dt <= 0f) { prev = f.drawing ? f : null; continue; }
                if (first == null) first = f;
                if (prev != null)
                {
                    float wy = Mathf.DeltaAngle(prev.camYaw, f.camYaw) / f.dt, wp = (f.camPitch - prev.camPitch) / f.dt, speed = Mathf.Sqrt(wy * wy + wp * wp);
                    r.writingMaxDps = Mathf.Max(r.writingMaxDps, speed); sum += (double)speed * speed; r.writingFrames++; if (speed < .05f) still++;
                    if (havePrevSpeed) r.writingMaxAccelDps2 = Mathf.Max(r.writingMaxAccelDps2, Mathf.Abs(wy - prevSpeedYaw) / f.dt);
                    prevSpeedYaw = wy; havePrevSpeed = true;
                }
                pitchMin = Mathf.Min(pitchMin, f.camPitch); pitchMax = Mathf.Max(pitchMax, f.camPitch);
                if (f.liveSamples > 0) { fogMin = Mathf.Min(fogMin, f.fogMean); fogMax = Mathf.Max(fogMax, f.fogMean); }
                prev = f;
            }
            if (first != null && prev != null) r.writingTurnDeg = Mathf.Abs(Mathf.DeltaAngle(first.camYaw, prev.camYaw));
            r.writingRmsDps = r.writingFrames > 0 ? (float)Math.Sqrt(sum / r.writingFrames) : 0f; r.writingStillShare = r.writingFrames > 0 ? still / (float)r.writingFrames : 0f;
            r.writingPitchTravelDeg = pitchMax >= pitchMin ? pitchMax - pitchMin : 0f;
            if (fogMax >= fogMin) { r.fogMeanMin = fogMin; r.fogMeanMax = fogMax; }
            // the frame the draw starts: how much the turn speed changes in one frame
            for (int i = 2; i < frames.Count; i++)
                if (frames[i].drawing && !frames[i - 1].drawing && frames[i].dt > 0f && frames[i - 1].dt > 0f)
                { r.drawStartStepDps = Mathf.Abs(Mathf.DeltaAngle(frames[i - 1].camYaw, frames[i].camYaw) / frames[i].dt - Mathf.DeltaAngle(frames[i - 2].camYaw, frames[i - 1].camYaw) / frames[i - 1].dt); break; }
            // the commit and after
            MFrame atCommit = frames.LastOrDefault(f => f.drawing); MFrame before = atCommit; bool firstAfter = true;
            if (atCommit != null) { r.targetViewportAtCommit = new Vector2(atCommit.targetVx, atCommit.targetVy); r.yawErrorAtCommit = atCommit.yawError; }
            foreach (var f in frames)
            {
                if (mCommitAt < 0f || f.t <= mCommitAt || f.drawing) continue;
                r.postFrames++; float since = f.t - mCommitAt;
                if (f.slidePx >= 0f)
                {
                    if (since <= .3f) { r.slideAt03 = f.slidePx; r.slideMax03 = Mathf.Max(r.slideMax03, f.slidePx); }
                    if (since <= .6f) { r.slideAt06 = f.slidePx; r.slideMax06 = Mathf.Max(r.slideMax06, f.slidePx); }
                    if (since <= .95f) r.slideAt095 = f.slidePx;
                }
                if (before != null && f.dt > 0f)
                {
                    float w = Mathf.Abs(Mathf.DeltaAngle(before.camYaw, f.camYaw)) / f.dt;
                    if (firstAfter) { r.resumeFirstFrameDps = w; firstAfter = false; }
                    r.resumeMaxDps = Mathf.Max(r.resumeMaxDps, w);
                }
                before = f;
            }
        }

        static void MFinish(string how, string forced)
        {
            if (ms.status != "running") return;
            var r = ms.report;
            if (EditorApplication.isPlaying) MRestore();
            MStopRecording();
            try { MSummarise(); } catch (Exception e) { MLine("FAIL summary: " + e.Message); forced = forced ?? "FAIL"; }
            string verdict = forced;
            if (verdict == null)
            {
                bool drew = r.committed && r.drawFrames > 5 && r.liveSampleCount > 0;
                bool fixedOnScreen = r.liveMaxPx <= LiveDriftLimitPx;
                bool modeOk = r.mode != LockOnDrawMode.Hold.ToString() || r.writingMaxDps <= .5f;
                modeOk &= r.mode != LockOnDrawMode.EdgeOnly.ToString() || r.writingMaxDps <= r.edgeMaxSpeed * 1.1f + .5f;
                verdict = !drew ? "UNCONFIRMED" : fixedOnScreen && modeOk ? "OK" : "FAIL";
                if (drew && !fixedOnScreen) MLine("FAIL AC-1 live stroke drift " + F(r.liveMaxPx, "F2") + " px > " + F(LiveDriftLimitPx, "F1") + " px: the strokes do move on screen");
                if (drew && !modeOk) MLine("FAIL the camera turned " + F(r.writingMaxDps, "F2") + " deg/s while writing in mode " + r.mode);
            }
            r.verdict = "done " + verdict;
            foreach (string f in r.fixtures) MLine("FIXTURE " + f);
            foreach (string n in r.notes) MLine("NOTE " + n);
            MLine("RECOGNITION committed " + r.committed + " recognised " + r.recognised + " letter '" + r.letter + "' strokes " + r.strokes + " points " + r.points + " input sha " + (r.recognitionInputSha.Length >= 12 ? r.recognitionInputSha.Substring(0, 12) : r.recognitionInputSha) + " (" + r.glyphSource + ")");
            MLine("LIVE drift max " + F(r.liveMaxPx, "F3") + " px rms " + F(r.liveRmsPx, "F3") + " px over " + r.liveSampleCount + " samples in " + r.drawFrames + " rendered frames (end-of-render cross-check max " + F(r.liveEndMaxPx, "F3") + " px; render-time anchor ran " + r.renderAnchorCalls + " times)");
            // H1 by measurement: where the same points were BEFORE this stage's render-time anchor (the picture of the old code)
            if (r.preAnchorFrames > 0)
                MLine("H1 before the render-time anchor " + F(r.preAnchorMaxPx, "F3") + " px -> after it " + F(r.liveMaxPx, "F3") + " px (max over " + r.preAnchorFrames + " frames in which the anchor ran between the two samples; " +
                    r.preAnchorFramesOver + " frame(s) over " + F(LiveDriftLimitPx, "F1") + " px before it): " +
                    (r.preAnchorMaxPx <= LiveDriftLimitPx ? "H1 REJECTED by measurement - the strokes did not slide on screen even without the fix; what moved was the world behind them"
                        : "H1 CONFIRMED in " + r.preAnchorFramesOver + " frame(s) - without the fix the strokes were drawn off their input pixels; look at those frames' fov / camera values in the json"));
            else if (r.drawFrames > 0)
                MLine("H1 NOT MEASURED: no frame had a sample taken before the adapter's render-time anchor (subscription order); AC-1 above still holds for what is drawn");
            // H10 by measurement: how far the glyph itself moved on screen with the mapping before the check 수리
            if (r.engineFrames > 0)
                MLine("H10 the mapping before the check 수리 (Camera.ScreenToWorldPoint) would have put the stroke frame up to " + F(r.engineCornerMaxPx, "F2") + " px off its place (screen centre " + F(r.engineCentreMaxPx, "F2") +
                    " px) on these " + r.engineFrames + " frames, moving it up to " + F(r.engineJumpMaxPx, "F2") + " px from one frame to the next (rms " + F(r.engineJumpRmsPx, "F2") + ", " + r.engineJumpFrames +
                    " frame pairs): " + (r.engineJumpMaxPx > LiveDriftLimitPx ? "the glyph itself shook on screen before the fix - a real screen-space part of the report" : "under " + F(LiveDriftLimitPx, "F1") + " px here: no visible shake from the mapping in this run") +
                    " | drawn now: LIVE drift above");
            MLine("CAMERA while writing (" + r.writingFrames + " frames): turn " + F(r.writingTurnDeg, "F2") + " deg, pitch travel " + F(r.writingPitchTravelDeg, "F2") + " deg, max " + F(r.writingMaxDps, "F2") + " deg/s, rms " + F(r.writingRmsDps, "F2") + " deg/s, still " +
                F(r.writingStillShare * 100f, "F0") + " % of frames, max turn acceleration " + F(r.writingMaxAccelDps2, "F0") + " deg/s2; speed step on the draw-start frame " + F(r.drawStartStepDps, "F2") + " deg/s");
            MLine("TARGET start bearing error " + F(r.startYawError, "F1") + " deg, at the commit viewport " + r.targetViewportAtCommit.ToString("F2") + " (bearing against the body's facing " + F(r.yawErrorAtCommit, "F1") + " deg, largest while drawing " + F(r.maxAbsYawError, "F1") + "), off screen in " + r.targetOffScreenFrames + " draw frames");
            MLine("AFTER the commit (" + r.postFrames + " frames): detached glyph moved " + F(r.slideAt03, "F1") + " / " + F(r.slideAt06, "F1") + " / " + F(r.slideAt095, "F1") + " px by 0.3 / 0.6 / 0.95 s (camera pull-back and the player's own movement included); turn speed first frame " +
                F(r.resumeFirstFrameDps, "F2") + " deg/s, max " + F(r.resumeMaxDps, "F2") + " deg/s");
            MLine("AFTER the commit, largest inside the window: " + F(r.slideMax03, "F1") + " px within 0.3 s, " + F(r.slideMax06, "F1") + " px within 0.6 s (a camera reaction that comes and goes inside 0.3 s - the #308 juice cast kick - shows here and not in the end-of-window values)");
            if (r.fogMeasured) MLine("FOG proxy (RealmFog297 " + F(r.fogStart, "F0") + " .. " + F(r.fogEnd, "F0") + " m, collider rays): mean fog share behind the stroke points " + F(r.fogMeanMin, "F2") + " .. " + F(r.fogMeanMax, "F2") +
                " while writing, largest frame-to-frame change of one point " + F(r.fogPointMaxStep, "F2") + ", sky / no hit " + r.fogSkyRays + " of " + r.fogRays + " rays");
            MLine("lockdraw308 measure: " + r.verdict + " | mode " + r.mode + " | " + how + " | " + (MNow - ms.began).ToString("F0", CultureInfo.InvariantCulture) + " s");
            if (ms.ownsPlay)
            {
                ms.status = "finishing";
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.delayCall += MCleanup;
            }
            else { MWrite(); ms.status = r.verdict; MRelease(); }
        }

        static void MCleanup()
        {
            if (ms.status != "finishing") return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += MCleanup; return; }
            ms.status = "cleaning"; bool clean = true;
            foreach (var c in Harness303.Cleanup(miso, ms.folder)) { string line = c.status + " " + c.id + " " + c.detail; MLine(line); ms.report.cleanup.Add(line); if (c.status == "FAIL") clean = false; }
            MLine("INFO after Play: quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ")");
            if (!clean) ms.report.verdict += " (cleanup FAIL)";
            MWrite();
            ms.status = ms.report.verdict; MRelease();
        }

        static void MWrite()
        {
            try
            {
                Directory.CreateDirectory(ms.folder);
                File.WriteAllText(Path.Combine(ms.folder, "measure_" + ms.o.mode + ".json"), JsonUtility.ToJson(ms.report, true), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(ms.folder, "measure_" + ms.o.mode + ".txt"), string.Join("\n", ms.lines), new UTF8Encoding(false));
            }
            catch (Exception e) { MLine("INFO report not written: " + e.Message); }
        }

        static void MRelease()
        {
            mS = null; mMotor = null; mDrawing = null; mAdapter = null; mLock = null; mCamera = null; mConfig = null; mTarget = null; mProbe = null; mLive = null; mFading = null; mCurrent = null;
            mHeld.Clear(); mInputs.Clear(); mDetached.Clear(); mDetachedBase.Clear(); mDetachedKey.Clear(); mFogLast.Clear();
            mHomeSet = mInputOn = mRecording = mResubscribed = mSubscribed = mCommitted = mBackgroundSet = mTidied = mEventsOn = false;
            mFpsSum = 0f; mFpsCount = 0; mCommitAt = -1f; mDrawStartAt = -1f; mStoppedAt = -1; mTeleportedAt = 0; mSumSq = 0; mLockTries = 0; mAnchorBefore = 0; mFogA = mFogB = 0f;
            mPrePx = -1f; mPreAnchorCount = -1; mEngineLast = default; mEngineHas = false; mEngineJumpSumSq = 0;
        }

        // ------------------------------------------------------------------ compare: three runs of the same glyph
        [Serializable] sealed class MHead
        {
            public string runId = "", verdict = "", mode = "", letter = "", recognitionInputSha = "", worstDistanceBits = "", averageDistanceBits = "", options = "";
            public bool committed, recognised; public int strokes, points, drawFrames, targetOffScreenFrames, preAnchorFrames;
            public float liveMaxPx, liveRmsPx, writingTurnDeg, writingMaxDps, writingRmsDps, writingStillShare, yawErrorAtCommit, slideAt06, resumeFirstFrameDps, fogPointMaxStep, pull, preAnchorMaxPx, slideMax03, engineJumpMaxPx, engineCornerMaxPx;
        }

        static string Compare(string folderArg)
        {
            string root = folderArg.Length > 0 ? folderArg : Root;
            if (!Directory.Exists(root)) return "refused: no folder " + root;
            // the newest measure_<mode>.json of each mode
            var newest = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(root, "measure_*.json", SearchOption.AllDirectories).OrderBy(f => File.GetLastWriteTimeUtc(f)))
            {
                string mode = Path.GetFileNameWithoutExtension(file).Substring("measure_".Length); newest[mode] = file;
            }
            if (newest.Count == 0) return "refused: no measure_*.json under " + root;
            var heads = new List<MHead>(); var sb = new StringBuilder("lockdraw308 compare (" + root + ")");
            foreach (var pair in newest.OrderBy(p => p.Key))
            {
                MHead h; try { h = JsonUtility.FromJson<MHead>(File.ReadAllText(pair.Value)); } catch (Exception e) { sb.Append("\nFAIL unreadable " + pair.Value + ": " + e.Message); continue; }
                heads.Add(h);
                sb.Append("\n" + pair.Key + " (" + h.mode + ", run " + h.runId + ", " + h.verdict + "): live drift max " + F(h.liveMaxPx, "F3") + " rms " + F(h.liveRmsPx, "F3") + " px (before the render-time anchor " +
                    (h.preAnchorFrames > 0 ? F(h.preAnchorMaxPx, "F3") + " px" : "not measured") + ") | writing turn " + F(h.writingTurnDeg, "F2") + " deg, max " + F(h.writingMaxDps, "F2") +
                    ", rms " + F(h.writingRmsDps, "F2") + " deg/s, still " + F(h.writingStillShare * 100f, "F0") + " % | bearing at commit " + F(h.yawErrorAtCommit, "F1") + " deg, off screen " + h.targetOffScreenFrames +
                    " | after commit: slide " + F(h.slideAt06, "F1") + " px at 0.6 s (largest within 0.3 s " + F(h.slideMax03, "F1") + "), first frame " + F(h.resumeFirstFrameDps, "F2") + " deg/s | fog step " + F(h.fogPointMaxStep, "F2") +
                    " | engine mapping (before the check 수리) corner max " + F(h.engineCornerMaxPx, "F2") + " px, frame-to-frame " + F(h.engineJumpMaxPx, "F2") + " px" +
                    " | letter '" + h.letter + "' points " + h.points + " sha " + (h.recognitionInputSha.Length >= 12 ? h.recognitionInputSha.Substring(0, 12) : h.recognitionInputSha));
            }
            var drew = heads.Where(h => h.committed && h.recognitionInputSha.Length > 0).ToList();
            if (drew.Count >= 2)
            {
                bool sameInput = drew.All(h => h.recognitionInputSha == drew[0].recognitionInputSha && h.points == drew[0].points && h.strokes == drew[0].strokes);
                bool sameResult = drew.All(h => h.letter == drew[0].letter && h.recognised == drew[0].recognised && h.worstDistanceBits == drew[0].worstDistanceBits && h.averageDistanceBits == drew[0].averageDistanceBits);
                bool sameOptions = drew.All(h => h.options == drew[0].options);
                sb.Append("\n" + (sameInput && sameResult ? "PASS" : "FAIL") + " recognition identity over " + drew.Count + " mode(s): input bytes " + (sameInput ? "identical" : "DIFFER") + ", result (letter, distances bit for bit) " + (sameResult ? "identical" : "DIFFERS") +
                    (sameOptions ? "" : " | NOTE the runs used different options: only runs with the same glyph, screen and options are comparable"));
            }
            else sb.Append("\nUNCONFIRMED recognition identity: fewer than two committed runs to compare");
            return sb.ToString();
        }
    }
}
