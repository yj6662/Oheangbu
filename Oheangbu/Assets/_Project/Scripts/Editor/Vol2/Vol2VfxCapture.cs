using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.EditorTools.WorldMacro;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 9a (D308-150 V5): the engine capture of the vol.2 looks - stills and frame sequences of P0 - P5 x 5 bodies in the spell lab, seen through the plan's SHOULDER camera
    /// (pivot 1.7 m, local offset (0.7, 0.55, -2.8), FOV 60, pitch 0 / 15 degrees) on a synthetic red-earth (Lab 35,30,25) and green (Lab 25,-22,15) ground. Queue (editor idle, no Play yet):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxCommands Run "vfx9a:play"                 open the lab in Play with a Vol2 scope and NO lab run
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxCommands Run "vfx9a:run:P0"              capture P0 (also: P0,P1 | all)   [:bodies=Wood,Fire] [:back=red] [:video=0]
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxCommands Run "vfx9a:status"
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxCommands Run "vfx9a:stop"                abort the queue (Play stays)
    /// Frames go to Art/SpellVFX308/Vol2Vfx/&lt;out&gt;/frames/&lt;job&gt;/ (out= option, default capture; f_NNNNN.jpg 1280x720 every game frame for a video job, key_*.png 1920x1080 near 0.08 s and 0.30 s,
    /// index.csv with the SCOPE clock age of each frame); the media tools (free_showcase_media308.py, cast_camera_media308.py, legacy_bridge/*) turn them into mp4s. Time.captureFramerate = 60
    /// for the capture: one game frame is exactly 1/60 s. LAB-SIMULATED and marked in index.csv: the clock jumps over the long waits, the swing's touch, the rapid casts of burst=.
    /// (The command prefix vfx9a: is kept for the callers; the slice 9a recipes themselves were removed on 2026-10-26.)</summary>
    public static class Vol2VfxCommands
    {
        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("vfx9a:", StringComparison.Ordinal)) c = c.Substring(6);
            try
            {
                if (c == "play") return Play();
                if (c == "play-legacy") return PlayLegacy();
                if (c == "probe") return Vol2VfxCapture.Probe();
                if (c == "status") return Vol2VfxCapture.Status();
                if (c == "stop") return Vol2VfxCapture.Stop("stop command");
                if (c.StartsWith("run:", StringComparison.Ordinal)) return Vol2VfxCapture.Enqueue(c.Substring(4));
                if (c == "build") return Vol2VfxBuild.Run("all");
                return "REFUSED vfx9a: play | run:<templates|all>[:bodies=..][:back=red,green][:video=0|1][:variant=..] | status | stop | build";
            }
            catch (Exception e) { return "REFUSED vfx9a:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>The legacy bridge comparison: the lab in Play WITHOUT a vol.2 request = the Legacy308 route the main game plays (SpellResolver -> the old 120 effects). Capture with legacy=1.</summary>
        static string PlayLegacy()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED vfx9a:play-legacy: Play is running or starting";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vfx9a:play-legacy: editor busy (compile / import)";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "REFUSED vfx9a:play-legacy: unsaved changes in " + SceneManager.GetSceneAt(i).path;
            if (File.Exists(Vol2LabRequestFile.RequestPath)) return "REFUSED vfx9a:play-legacy: a vol.2 lab request is pending (" + Vol2LabRequestFile.RequestPath + ")";
            string started = SpellLab308.Run("play");
            return started.StartsWith("play:", StringComparison.Ordinal) ? "PLAY-REQUESTED (Legacy308 route, the main game's) " + started : "REFUSED vfx9a:play-legacy: " + started;
        }

        static string Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED vfx9a:play: Play is running or starting";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vfx9a:play: editor busy (compile / import)";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "REFUSED vfx9a:play: unsaved changes in " + SceneManager.GetSceneAt(i).path;
            if (!File.Exists(Path.GetFullPath(Vol2LabRequestFile.LabScenePath))) return "REFUSED vfx9a:play: no lab scene (SpellLab308 build)";
            var book = Vol2RulebookLoader.Load(null, null);
            if (!book.Ok) return "REFUSED vfx9a:play: vol.2 data unavailable: " + book.Error;
            Directory.CreateDirectory(Vol2LabRequestFile.Folder);
            var request = new Vol2LabRequest { mode = "Vol2", run = false, runId = "vfx9a_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), commit = "vfx9a", issuedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), scene = Vol2LabRequestFile.LabScenePath };
            File.WriteAllText(Vol2LabRequestFile.RequestPath, JsonUtility.ToJson(request, true), new UTF8Encoding(false));
            string started = SpellLab308.Run("play");
            if (!started.StartsWith("play:", StringComparison.Ordinal))
            {
                try { File.Delete(Vol2LabRequestFile.RequestPath); } catch (Exception) { }
                return "REFUSED vfx9a:play: " + started;
            }
            return "PLAY-REQUESTED (Vol2 scope, no lab run) " + started;
        }
    }

    public static class Vol2VfxCapture
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int VideoW = 1280, VideoH = 720, KeyW = 1920, KeyH = 1080;
        static readonly string[] Rows = { "가나마사아", "각낙막삭악", "간난만산안", "감남맘삼암", "갓낫맛삿앗", "강낭망상앙" };
        static readonly Body[] Bodies = { Body.Wood, Body.Fire, Body.Earth, Body.Metal, Body.Water };
        static readonly (float L, float a, float b) RedEarth = (35f, 30f, 25f), GreenForest = (25f, -22f, 15f);

        /// <summary>The output folder under Art/SpellVFX308/Vol2Vfx (run option out=free_showcase/batch1 ...; default "capture").</summary>
        static string _out = "capture";
        // calibration (coordinator 2026-10-25): look=main puts the main world's look profile on the lab's LookDriver308 for the run (runtime only, restored at the end);
        // realm=auto makes the look step at a point inside the realm of the soil (적로 for red, 청림 for green) before every render instead of at the lab's own position;
        // template AJ = the approved 앞잡 one stroke (RiposteStroke308Effect) fired at the first body
        static bool _mainLook; static string _realmMode = "lab";
        // pitch= the video camera pitch (the in-game shoulder camera is pitch 0 by default; the old captures used 15)
        // (2026-10-26: the options of the removed slice 9a / NB20 / stroke20 recipes - p0=9a|nb20, rules=1, dist=, the nb20 template group - are gone)
        static float _videoPitch = 15f; static bool _legacy; static int _legacyFrames; static Vol2WiringCaster _legacyCaster;
        // D308-150 V9 (legacy bridge): legacy=2 = the Legacy308 route WITH the old glyph bodies. The old route presents a glyph's body through the stroke adapter
        // (BrushStrokeFeedAdapter.SpawnPattern), which needs the drawn letter's strokes - a lab letter raised on the channel has none, so legacy=1 showed the swing,
        // the brush-tip ink and the wards but no projectile / area / guard body. legacy=2 (LAB-ONLY, editor capture) feeds the REAL SpawnPattern at the moment
        // the old route plans the cast (CastPlanned at the apex; CastAccepted for a parry) with one synthetic stroke at the adapter's drawing-surface centre and
        // the pending hit context the wiring computed (target, flight, area plan). apex=0|1 sets the vol.2 TE-S3-6 launch switch for the run (restored at the end).
        static bool _legacyFeed, _wardBand; static bool? _apex; static bool _apexSaved; static Vol2ShapeTuning _apexTuning; static int _fed, _fedMissing;
        static readonly List<EnemyVitals> LegacyTargets = new List<EnemyVitals>(); static LockOn LegacyLock;
        // step 5a (task A, the double-spawn guard): stroke=1 hands the REAL stroke adapter one drawn stroke before the letter and commits it after (its private input
        // handlers OnStrokeStarted / OnStrokePointAdded / OnStrokeEnded / OnCommitted, as DrawingProjectionRegression drives them), so the adapter's own commit path runs
        // exactly as with a drawn letter; every game frame the glyph bodies on screen are counted by who made them (the adapter's "SpellPattern" / the bridge's
        // "Vol2Legacy_*"). LAB-ONLY. lasting=0|1 = the step 5a lasting variants of the bridge stage for this run (Vol2LegacyLastingSet; restored at the end).
        static bool _strokeFeed; static bool? _lasting; static bool? _lastingSaved; static readonly HashSet<int> AdapterBodies = new HashSet<int>(), BridgeBodies = new HashSet<int>(), Unravels = new HashSet<int>();

        /// <summary>D308-150 V9: the end of a job of the bridge comparison: every old body still running its own life (Vfx120Effect destroys itself only when its life is over)
        /// and the dummies the old route was given are taken away, so nothing of one glyph shows in the next one's frames. LAB-ONLY.</summary>
        static void ClearOldBodies(Ctx c)
        {
            int n = 0;
            foreach (var fx in Object.FindObjectsByType<Oheangbu.App.SpellVFX120.Vfx120Effect>(FindObjectsSortMode.None)) if (fx != null && fx.gameObject.scene.IsValid()) { Object.Destroy(fx.gameObject); n++; }
            c?.Presenter?.Legacy?.Sweep();
            if (LegacyTargets.Count > 0 && c != null && c.Wiring != null)
            {
                var list = typeof(CombatLoopWiring).GetField("_targets", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(c.Wiring) as List<EnemyVitals>;
                if (list != null) foreach (var v in LegacyTargets) list.Remove(v);
            }
            LegacyTargets.Clear();
            if (LegacyLock != null) { typeof(LockOn).GetField("_locked", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(LegacyLock, false); typeof(LockOn).GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(LegacyLock, null); LegacyLock = null; }
            if (n > 0) _note = "cleared " + n + " old bodies";
        }
        static Action<CastPlan> _onLegacyPlanned; static Action<SpellCast, Vector3, Vector3> _onLegacyAccepted;
        static Oheangbu.App.World.LookDriver308 _lookDriver; static Oheangbu.Data.World.LookProfile308 _lookSaved; static bool _lookSwapped;
        static Vector3? _realmEye;
        static Oheangbu.App.World.RiposteStroke308Effect _rip; static Oheangbu.App.World.RiposteStroke308Effect.Slot _ripSlot; static int _ripFrames;
        const string MainLookProfile = "Assets/_Project/Art/World/Look308/Data/LookProfile308_PBZInkWash.asset";
        const string RiposteProfile = "Assets/_Project/Art/SpellVFX308/RiposteStroke/RiposteStroke308Profile.asset";
        public static string OutRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX308/Vol2Vfx/" + _out));
        static readonly Dictionary<string, string> RowOf = new Dictionary<string, string>
        {
            { "A0", "거너머서어" }, { "F0", "고노모소오" }, { "R0", "구누무수우" },
            // D308-150 V9 step 4: the batchim rows of ㅓ ㅗ ㅜ (ㅏ's are Rows)
            { "A1", "걱넉먹석억" }, { "A2", "건넌먼선언" }, { "A3", "검넘멈섬엄" }, { "A4", "것넛멋섯엇" }, { "A5", "겅넝멍성엉" },
            { "F1", "곡녹목속옥" }, { "F2", "곤논몬손온" }, { "F3", "곰놈몸솜옴" }, { "F4", "곳놋못솟옷" }, { "F5", "공농몽송옹" },
            { "R1", "국눅묵숙욱" }, { "R2", "군눈문순운" }, { "R3", "굼눔뭄숨움" }, { "R4", "굿눗뭇숫웃" }, { "R5", "궁눙뭉숭웅" },
        };
        /// <summary>Step 4: a batchim template (P1 - R5) recorded as the legacy bridge plays it.</summary>
        static bool Batchim(string t) => t.Length == 2 && "PAFR".IndexOf(t[0]) >= 0 && t[1] >= '1' && t[1] <= '5';
        static int _noLogFrames, _describe; static float _pitch2;

        sealed class Step { public double At; public string Name; public Func<Ctx, bool> Do; public bool Done; }

        sealed class Job
        {
            public string Template, Variant = "main", Back = "red"; public Body Body; public char Letter; public bool Video, Keys;
            public double MaxAge = 1.2, MinAge = 0.4;
            /// <summary>Step 4 F2: the bodies are placed from the fan's own spec at setup (in it / outside its edges within the spread's reach).</summary>
            public bool FanNeighbours;
            /// <summary>batch 4 (spread=1, 눈): stage wood materials before the cast (LAB-SIMULATED).</summary>
            public bool PlantWood;
            public readonly List<Step> Steps = new List<Step>();
            public List<(float ahead, float side)> Dummies = new List<(float, float)>();
            public string Name => Template + "_" + Variant + "_" + Body + "_" + Back;
        }

        sealed class Ctx
        {
            public Vol2VfxPresenter Presenter; public Vol2ShapeRuntime Shapes; public Vol2CastCoordinator Coordinator; public CombatLoopWiring Wiring; public Vol2Services Services;
            public Vol2CastLog Log; public Vol2CastDecision Decision; public Vector3 Origin, Forward;
            public readonly List<GameObject> Dummies = new List<GameObject>();
            public GameObject Backdrop; public Material BackdropMat, DummyMat, GroundOriginal; public Renderer GroundRenderer;
            public readonly List<GameObject> HiddenLab = new List<GameObject>();
        }

        enum St { Idle, WaitPresenter, Setup, Settle, Record, Teardown }

        static readonly Queue<Job> Queue = new Queue<Job>();
        static Job _job; static Ctx _ctx; static St _st = St.Idle;
        static int _lastFrame = -1, _settle, _frames, _keys, _total, _done;
        static double _guardUntil;
        static StreamWriter _index; static string _folder;
        static Camera _cam; static RenderTexture _rtVideo, _rtKey; static Texture2D _texVideo, _texKey;
        static readonly List<string> Problems = new List<string>(), Finished = new List<string>();
        static bool _running; static string _runId = "";
        static Action<Vol2CastDecision> _onDecided; static Action<Vol2CastLog> _onBegan;
        static Vol2CastDecision _lastDecision; static Vol2CastLog _beganLog;
        static int _savedCaptureRate;
        static float _lastJumpAge = -1f; static string _note = "";

        public static string Status()
        {
            return "running=" + _running + " state=" + _st + " queued=" + Queue.Count + " done=" + _done + "/" + _total + (_job != null ? " job=" + _job.Name : "") + " frames=" + _frames
                + (Problems.Count > 0 ? "\nproblems:\n" + string.Join("\n", Problems.Take(12)) : "") + "\nout=" + OutRoot;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // queue
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        public static string Enqueue(string spec)
        {
            if (!EditorApplication.isPlaying) return "REFUSED vfx9a:run: not in Play (vfx9a:play first)";
            if (_running) return "REFUSED vfx9a:run: a capture is running: " + Status();
            string[] parts = spec.Split(':');
            var templates = new List<string>();
            foreach (string t in parts[0].Split(','))
            {
                if (t == "all") templates.AddRange(new[] { "P0", "P1", "P2", "P3", "P4", "P5" });
                else if (t == "batchim") foreach (char f in "PAFR") for (int v = 1; v <= 5; v++) templates.Add(f + v.ToString());
                else templates.Add(t.Trim());
            }
            _out = "capture"; _mainLook = false; _realmMode = "lab"; MeasuredGround.Clear(); _videoPitch = 15f; _legacy = false;
            _legacyFeed = false; _apex = null; _fed = 0; _fedMissing = 0; _wardBand = false; _pitch2 = 0f; _strokeFeed = false; _lasting = null; _walkIn = false; _endWith = Vol2MaterialEnd.None; _spreadB4 = false; AdapterBodies.Clear(); BridgeBodies.Clear();
            _burstCount = 0; _burstEvery = 0.65;
            var bodies = Bodies.ToList(); var backs = new List<string> { "red", "green" }; bool videoOnRed = true, videoAll = false; string variantOnly = null;
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) continue;
                string k = parts[i].Substring(0, eq), v = parts[i].Substring(eq + 1);
                if (k == "bodies") bodies = v.Split(',').Select(x => (Body)Enum.Parse(typeof(Body), x.Trim(), true)).ToList();
                else if (k == "back") backs = v.Split(',').Select(x => x.Trim()).ToList();
                else if (k == "video") { videoOnRed = v == "1" || v == "2"; videoAll = v == "2"; }
                else if (k == "variant") variantOnly = v;
                else if (k == "out") _out = v.Trim().Trim('/').Replace("..", "");
                else if (k == "look") _mainLook = v.Trim() == "main";
                else if (k == "realm") _realmMode = v.Trim();
                else if (k == "pitch") _videoPitch = float.Parse(v, Inv);
                else if (k == "pitch2") _pitch2 = float.Parse(v, Inv);       // step 4: a second, higher view of every video frame (g_NNNNN.jpg) to read the state on the ground
                else if (k == "legacy") { _legacy = v == "1" || v == "2"; _legacyFeed = v == "2"; }
                else if (k == "apex") _apex = v.Trim() == "1";
                else if (k == "wardband") _wardBand = v.Trim() == "1";
                else if (k == "stroke") _strokeFeed = v.Trim() == "1";
                else if (k == "lasting") _lasting = v.Trim() == "1";
                else if (k == "walkin") _walkIn = v.Trim() == "1";          // batch 3 (opt-in): a body walks onto the 남긴다 residue at 1.6 s (P1 / F1 / R1)
                else if (k == "endwith") _endWith = (Vol2MaterialEnd)Enum.Parse(typeof(Vol2MaterialEnd), v.Trim(), true);   // batch 3 (opt-in): end the residue for this reason instead of the expiry jump
                else if (k == "spread") _spreadB4 = v.Trim() == "1";       // batch 4 (opt-in): the 번진다 rows P2 / A2 / R2 with a third neighbour / 넌's second swing / 눈's staged wood (see Step4Job)
                else if (k == "enemy") EnemyPrefab = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_" + v + ".prefab";
                else if (k == "burst") { var nb = v.Split('/'); _burstCount = int.Parse(nb[0], Inv); if (nb.Length > 1) _burstEvery = double.Parse(nb[1], Inv); }
            }
            foreach (string t in templates)
            {
                if (!(t.Length == 2 && t[0] == 'P' && t[1] >= '0' && t[1] <= '5') && !RowOf.ContainsKey(t) && t != "AJ") return "REFUSED vfx9a:run: template " + t + " (P0 - P5, A0 - A5, F0 - F5, R0 - R5, AJ)";
                var variants = new[] { "main" };
                foreach (string variant in variants)
                {
                    if (variantOnly != null && variant != variantOnly) continue;
                    foreach (string back in backs)
                        foreach (var body in bodies)
                        {
                            if (t == "AJ" && body != bodies[0]) continue;      // the 앞잡 stroke has no body variant: one job per ground
                            var job = MakeJob(t, variant, body, back, videoAll || (videoOnRed && back == backs[0]));
                            Queue.Enqueue(job);
                        }
                }
            }
            _total = Queue.Count; _done = 0; Problems.Clear(); Finished.Clear();
            _runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(OutRoot);
            _running = true; _st = St.WaitPresenter; _lastFrame = -1; _settle = 0;
            _savedCaptureRate = Time.captureFramerate;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += OnReload;
            return "queued " + _total + " jobs | " + OutRoot;
        }

        public static string Stop(string why)
        {
            if (!_running) return "capture: not running";
            Finish(why);
            return "capture stopped (" + why + ")";
        }

        static void OnPlayMode(PlayModeStateChange s) { if (s == PlayModeStateChange.ExitingPlayMode) Finish("Play ended"); }
        static void OnReload() { Finish("script reload"); }

        static Job MakeJob(string template, string variant, Body body, string back, bool video)
        {
            int verb = template[0] == 'P' ? template[1] - '0' : 0;
            var j = new Job { Template = template, Variant = variant, Body = body, Back = back, Video = video, Keys = true };
            j.Letter = template == "AJ" ? '\0' : RowOf.TryGetValue(template, out var row) ? row[(int)body] : Rows[verb][(int)body];
            j.Dummies.Add((4.6f, 0f));
            switch (template)
            {
                case "AJ": j.MaxAge = 1.3; j.MinAge = 1.25; break;          // the approved 앞잡 one stroke: reveal .05 - .20, dry from .60, gone 1.20 (profile defaults)
                // the bodies of each frame (the ages of P0 / A0 / F0 / R0 are set below for the bridge, the batchim ones by Step4Job or legacy=1|2)
                case "A0": j.Dummies[0] = (1.6f, 0.25f); break;              // the bare parry: a body 1.6 m ahead
                case "F0": j.Dummies[0] = (3.4f, -0.3f); j.Dummies.Add((4.3f, 1.25f)); break;
                case "R0": j.Dummies[0] = (1.85f, 0.75f); break;
                case "P0": j.Dummies.Add((8f, 0f)); break;
                case "P2": j.Dummies.Add((6.2f, 1.7f)); j.Dummies.Add((5.8f, -1.9f)); j.Dummies.Add((3.6f, 2.3f)); break;   // the spread's neighbours for legacy=1|2 (Step4Job places its own)
                default: j.MaxAge = 1.5; j.MinAge = 1.2; break;
            }
            if (Batchim(template) && !_legacy) { Step4Job(j); if (_lasting.HasValue) j.Variant = _lasting.Value ? "lasting1" : "lasting0"; }   // step 5a: old / new in their own folders
            if (_legacy) { j.Steps.Clear(); j.MaxAge = template == "A0" ? 1.2 : Batchim(template) ? 4.5 : 1.6; j.MinAge = j.MaxAge; j.Variant = _legacyFeed ? "old" : "legacy"; }
            // D308-150 V9: the old visuals live longer than the vol.2 ink looks (windup + flight + impact): both sides of the legacy bridge comparison record the same full
            // length (no early stop), with no lab-simulated parry (the old route only shows a parry's success on a real enemy impact)
            if (template == "P0" || template == "A0" || template == "F0" || template == "R0")
            {
                j.Steps.Clear(); j.MaxAge = template == "A0" ? 1.6 : 2.2; j.MinAge = j.MaxAge;
                if (!_legacy) j.Variant = _apex == true ? "bridge_apex" : "bridge";
            }
            // D308-150 V13 (cast camera, rapid casts): burst=N/every raises the job's letter N - 1 more times, every `every` s after the first commit (LAB-SIMULATED:
            // the same letter on the channel, no drawing), and records the whole run plus BurstTail s (no early stop when the first cast retires)
            if (_burstCount > 1)
            {
                char letter = j.Letter;
                for (int k = 1; k < _burstCount; k++)
                {
                    int nth = k + 1;
                    j.Steps.Add(new Step { At = k * _burstEvery, Name = "LAB-SIMULATED: rapid cast " + nth + "/" + _burstCount + " (" + letter + " again)", Do = c => { RaiseLetter(c, letter); return true; } });
                }
                j.MaxAge = j.MinAge = (_burstCount - 1) * _burstEvery + BurstTail;
                j.Variant += "_burst" + _burstCount;
            }
            return j;
        }

        static int _burstCount; static double _burstEvery = 0.65; const double BurstTail = 1.8;
        // batch 3 free showcase (2026-10-26, opt-in, defaults unchanged): walkin=1 / endwith=<Vol2MaterialEnd> for the 남긴다 rows P1 / F1 / R1 (see Step4Job)
        static bool _walkIn; static Vol2MaterialEnd _endWith = Vol2MaterialEnd.None;

        /// <summary>LAB-SIMULATED (endwith=): ends the cast's live residue through the registry for the given reason (a stand-in for a cleanse / a reaction / a
        /// replace that the capture has no other way to stage), so the look's exit for that reason is on the video.</summary>
        static bool EndResidueWith(Ctx c, Vol2MaterialEnd why)
        {
            if (c.Log == null) return false;
            if (c.Log.Materials.Count == 0) return false;          // not there yet (try again next frame)
            foreach (var m in c.Log.Materials) if (m.Alive) c.Coordinator.Materials.TryEnd(m.Id, why, c.Services.Clock.NowUs);
            return true;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // step 4 (D308-150 V9): the batchim templates as the legacy bridge plays them - the cast AND the lasting verb state long enough to read it
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>The scenario of one batchim template: real bear bodies, the steps that make the verb's state appear, work and end. LAB-SIMULATED steps are named so
        /// (a swing's touch, the player's lost HP, a body walking in, the caster stepping on the band); the long waits are JUMPs of the scope clock that stop
        /// 1.4 s before the executor's scheduled end, so the expiry warning (1.0 s before) and the end reason are on the video.</summary>
        static void Step4Job(Job j)
        {
            j.Variant = "bridge4"; j.Steps.Clear(); j.Dummies.Clear();
            char f = j.Template[0]; int v = j.Template[1] - '0';
            const double Lead = 1.4;
            void Step(double at, string name, Func<Ctx, bool> act) => j.Steps.Add(new Step { At = at, Name = name, Do = act });
            void JumpMat(double at) => Step(at, "JUMP to " + Lead + " s before the state's scheduled end (LAB-SIMULATED long wait)", c => JumpBeforeExpiry(c, Lead));
            void JumpStrip(double at) => Step(at, "JUMP to " + Lead + " s before the strip ends (LAB-SIMULATED 3 s wait)", c => JumpBeforeStripEnd(c, Lead));
            void JumpHost(double at) => Step(at, "JUMP to " + Lead + " s before the attachment ends (LAB-SIMULATED 6 s wait)", c => JumpBeforeEmitterEnd(c, Lead));
            void SwingAt(double at) => Step(at, "LAB-SIMULATED: the swing's touch (the caster's brush sweeps the body)", c => { SimSwing(c); return true; });
            // batch 3 (opt-in): a body walking onto the 남긴다 residue / the residue ended for another reason than its expiry
            void Leave1Options((float ahead, float side) from, (float ahead, float side) to)
            {
                if (v != 1) return;
                if (_walkIn) { int idx = j.Dummies.Count; j.Dummies.Add(from); Step(1.6, "LAB-SIMULATED: a body walks onto the residue", c => MoveDummy(c, idx, to.ahead, to.side)); }
                if (_endWith != Vol2MaterialEnd.None)
                {
                    j.Steps.RemoveAll(s => s.Name.StartsWith("JUMP", StringComparison.Ordinal));
                    var why = _endWith;
                    Step(3.0, "LAB-SIMULATED: the residue ends " + why + " (registry TryEnd)", c => EndResidueWith(c, why));
                    j.MaxAge = 6; j.Variant += "_" + why.ToString().ToLowerInvariant();
                }
            }
            switch (f)
            {
                case 'P':
                    j.Dummies.Add((4.6f, 0f));
                    switch (v)
                    {
                        case 1: j.MaxAge = 14; j.MinAge = 2.5; JumpMat(3.0); break;                                                        // residue 10 s (각: the cage first)
                        case 2: j.Dummies.Add((6.2f, 1.7f)); j.Dummies.Add((5.8f, -1.9f)); j.MaxAge = 8; j.MinAge = 1.5; JumpMat(1.3); break;   // marks 3 s on the neighbours
                        case 3:
                            j.MaxAge = 4; j.MinAge = 2.0;
                            char follow = Rows[0][(int)j.Body];
                            Step(1.4, "LAB-SIMULATED: a direct hit of the same body works the seed (" + follow + ")", c => { RaiseLetter(c, follow); return true; });
                            break;
                        case 4: j.MaxAge = 8; j.MinAge = 1.5; JumpStrip(1.2); break;
                        default: j.Dummies.Add((8.5f, 0.4f)); j.MaxAge = 3.2; j.MinAge = 2.0; break;                                    // out to 12 m and back
                    }
                    Leave1Options((6.5f, 2.6f), (4.7f, 0.85f));
                    break;
                case 'A':
                    j.Dummies.Add((1.2f, 0.3f));
                    switch (v)
                    {
                        case 1: j.MaxAge = 9; j.MinAge = 2.0; SwingAt(0.35); JumpHost(1.6); break;
                        case 2: j.Dummies.Add((2.9f, 1.9f)); j.Dummies.Add((3.3f, -1.6f)); j.MaxAge = 9; j.MinAge = 2.0; SwingAt(0.35); JumpMat(1.5); break;
                        case 3:
                            j.Dummies.Add((1.6f, -0.7f)); j.MaxAge = 4.5; j.MinAge = 2.4;
                            Step(1.3, "LAB-SIMULATED: the player really loses 6 HP (PlayerVitals.TakeAttackDamage from the first body)", c => { HurtPlayer(c); return true; });
                            break;
                        case 4: j.MaxAge = 9; j.MinAge = 2.0; SwingAt(0.35); if (j.Letter == '섯') SwingAt(0.9); JumpStrip(1.3); break;
                        default: j.Dummies[0] = (1.6f, 0f); j.MaxAge = 9; j.MinAge = 2.0; JumpHost(2.2); break;                            // the orbit (r 1.6 m) meets the body
                    }
                    if (j.Letter == '걱') { j.Steps.Clear(); j.MaxAge = 4.0; j.MinAge = 4.0; }       // the recovery (the defence kit's): recorded without a cast log
                    break;
                case 'F':
                    j.Dummies.Add((3.4f, -0.3f));
                    switch (v)
                    {
                        case 1: j.Dummies.Add((4.3f, 1.25f)); j.MaxAge = 14; j.MinAge = 2.5; JumpMat(3.0); break;
                        case 2: j.MaxAge = 9; j.MinAge = 1.5; j.FanNeighbours = true; JumpMat(1.3); break;
                        case 3:
                            j.Dummies.Add((7.6f, 0.4f)); j.MaxAge = 5; j.MinAge = 2.6;
                            Step(1.3, "LAB-SIMULATED: a body walks into the fan (an entry: the seed works)", c => MoveDummy(c, 1, 4.1f, 0.4f));
                            if (j.Letter == '몸') { j.MaxAge = 9; JumpMat(2.6); }
                            break;
                        case 4: j.Dummies.Add((4.3f, 1.25f)); j.MaxAge = 8; j.MinAge = 1.5; JumpStrip(1.2); break;
                        default: j.Dummies.Add((5.6f, 0.5f)); j.MaxAge = 3.2; j.MinAge = 2.0; break;
                    }
                    Leave1Options((2.4f, 3.2f), (2.6f, 0.55f));
                    break;
                default:
                    j.Dummies.Add((1.9f, 0.5f));
                    switch (v)
                    {
                        case 1: j.MaxAge = 14; j.MinAge = 2.5; JumpMat(3.0); break;
                        case 2:
                            j.Dummies.Add((4.4f, 1.2f)); j.Dummies.Add((5.3f, 2.6f)); j.MaxAge = 9; j.MinAge = 2.0;
                            Step(1.0, "LAB-SIMULATED: a body crosses onto the band", c => MoveDummy(c, 1, 2.0f, 1.0f));
                            JumpMat(1.8);
                            break;
                        case 3:
                            j.Dummies.Add((4.4f, 1.2f)); j.MaxAge = 5; j.MinAge = 2.6;
                            if (j.Letter == '뭄') { Step(1.3, "LAB-SIMULATED: the caster steps onto the band", c => CasterOnBand(c)); j.MaxAge = 9; JumpMat(2.6); }
                            else Step(1.3, "LAB-SIMULATED: a body crosses onto the band (the seed works)", c => MoveDummy(c, 1, 2.0f, 1.0f));
                            break;
                        case 4:
                            j.Dummies.Add((4.4f, 1.2f)); j.MaxAge = 14; j.MinAge = 2.0;
                            Step(1.0, "LAB-SIMULATED: a body crosses onto the band (stripped)", c => MoveDummy(c, 1, 2.0f, 1.0f));
                            JumpStrip(1.6);
                            Step(4.6, "JUMP to " + Lead + " s before the 10 s ring line ends (LAB-SIMULATED long wait)", c => JumpBeforeEmitterEnd(c, Lead));
                            break;
                        default: j.Dummies.Add((3.7f, 0.9f)); j.MaxAge = 3.2; j.MinAge = 2.0; break;                                // the ring goes 2 -> 4 m and back
                    }
                    Leave1Options((0.6f, -3.4f), (0.5f, -1.95f));
                    break;
            }
            // batch 4 free showcase (2026-10-26, opt-in spread=1, defaults unchanged): the 번진다 rows laid out so every spread is seen
            if (_spreadB4 && v == 2)
            {
                if (f == 'P') j.Dummies.Add((7.2f, -0.4f));                 // a third neighbour of the struck body (2.6 m): 간 shares 1.0 three ways, 난 gives each 0.5
                else if (f == 'A' && j.Letter == '넌') SwingAt(0.9);        // 넌: a second swing (the shared 1.5 budget; the effect-target cap decides what it may still reach)
                else if (f == 'R')
                {
                    // the struck body stands on the band from the launch; two neighbours farther out within 3 m of it (2.7 m each), in front of the camera
                    j.Steps.RemoveAll(s => s.Name.StartsWith("LAB-SIMULATED: a body crosses", StringComparison.Ordinal));
                    j.Dummies.Clear(); j.Dummies.Add((1.9f, 0.5f)); j.Dummies.Add((4.3f, 1.8f)); j.Dummies.Add((3.9f, -1.3f));
                    if (j.Letter == '눈') j.PlantWood = true;
                }
                j.Variant += "_b4";
            }
        }

        /// <summary>batch 4 (spread=1, 눈 only, LAB-SIMULATED): wood materials staged before the cast as the fixture S8 plants them - one on the band ahead-left, one under
        /// the right-hand bear 1.9 m from it, one 2 m farther out (the chain), and one enemy patch on the band; the left-hand bear's ground has no wood (fire follows
        /// wood, not bodies). Ended at teardown when still alive.</summary>
        static void PlantWood(Ctx c)
        {
            var mats = c.Coordinator.Materials; long now = c.Services.Clock.NowUs; long life = Vol2Time.FromSeconds(6.0);
            Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
            Vector3 At(float ahead, float side) => c.Origin + c.Forward * ahead + right * side;
            void Plant(long cast, Body body, long faction, float ahead, float side, float radius, string tag)
            {
                var m = mats.Create(new CastId(cast), '가', body, Vol2MaterialKind.Residue, 0.9m, life, Vol2Footprint.Disc(At(ahead, side), radius), faction == Vol2Faction.Enemy ? 2 : 1, faction, true, now, 0, 0, tag);
                if (m != null) Planted.Add(m);
            }
            Plant(98001, Body.Wood, Vol2Faction.Player, 2.0f, -0.9f, 0.7f, "lab-wood");
            Plant(98002, Body.Wood, Vol2Faction.World, 3.9f, -1.3f, 0.7f, "lab-wood");
            Plant(98003, Body.Wood, Vol2Faction.Player, 5.6f, -2.4f, 0.7f, "lab-wood");
            Plant(98011, Body.Water, Vol2Faction.Enemy, 1.2f, 1.6f, 0.7f, "lab-enemy-patch");
            Finished.Add("  " + _job.Name + ": LAB-SIMULATED wood staged: " + Planted.Count + " materials (3 wood, 1 enemy patch)");
        }
        static readonly List<Vol2Material> Planted = new List<Vol2Material>();
        static bool _spreadB4;

        static bool MoveDummy(Ctx c, int index, float ahead, float side)
        {
            if (index >= c.Dummies.Count || c.Dummies[index] == null) return true;
            Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
            c.Dummies[index].transform.position = c.Origin + c.Forward * ahead + right * side;
            Physics.SyncTransforms();
            return true;
        }

        static void SimSwing(Ctx c)
        {
            long swing = c.Services.Ids.NextSwing().Value;
            Vector3 sleeve = c.Origin + Vector3.up * 1.1f, tip = sleeve + c.Forward * c.Coordinator.Tuning.BrushReach;
            for (int k = 0; k < 3; k++) c.Shapes.Swings.Raise(new Vol2SwingEvent(swing, sleeve, tip, c.Services.Clock.NowUs, k));
        }

        static void HurtPlayer(Ctx c)
        {
            try
            {
                // as the lab's 심는다 row does (Vol2SeedLab.Wire): the scene's player vitals, bound to the seed hub with the wiring's defence kit, then a real hit
                var player = Object.FindFirstObjectByType<PlayerVitals>() ?? c.Wiring.Vol2Player;
                var from = c.Dummies.Count > 0 && c.Dummies[0] != null ? c.Dummies[0].GetComponentInChildren<EnemyVitals>(true) : null;
                if (player == null) { Problems.Add(_job.Name + ": no player vitals (the 심는다 ㅓ seed cannot work)"); return; }
                if (c.Wiring.Vol2Defence != null) Vol2SeedWiring.Bind(c.Coordinator, c.Wiring.Vol2Defence, player);
                var took = player.TakeAttackDamage(6f, IncomingDamageKind.Melee, from);
                Finished.Add("  " + _job.Name + ": player hit took=" + took + " hp01=" + player.Hp01.ToString("0.00", Inv));
            }
            catch (Exception e) { Problems.Add(_job.Name + " hurt: " + e.Message); }
        }

        static bool CasterOnBand(Ctx c)
        {
            var hub = Vol2SeedExecutor.HubOf(c.Coordinator);
            if (hub == null) { Problems.Add(_job.Name + ": no seed hub (뭄)"); return true; }
            Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
            Vector3 onBand = c.Log != null ? c.Log.Spec.Origin + right * c.Log.Spec.RingRadius : c.Origin + right * 2f;
            hub.CasterPositionOverride = () => onBand;
            _casterOverride = hub;
            return true;
        }
        static Vol2SeedHub _casterOverride;

        static bool JumpBeforeEmitterEnd(Ctx c, double before)
        {
            long end = c.Log != null && c.Log.Emitter is Vol2AttachmentHost h ? h.EndUs : c.Log != null && c.Log.Emitter is Vol2TimedEmitter te ? te.EndUs : 0;
            if (end <= 0) return true;
            Jump(c, end - Vol2Time.FromSeconds(before));
            return true;
        }

        /// <summary>F2: two bodies outside the fan, within the spread's 3 m of its edge (placed from the fan's own spec numbers), one inside it.</summary>
        static void FanNeighbours(Ctx c, Job j)
        {
            var spec = Vol2ShapeSpec.Create(Frame.Fan, c.Coordinator.Numbers, c.Origin, c.Forward, c.Coordinator.Tuning);
            float half = spec.FanHalfAngleRad * Mathf.Rad2Deg, depth = spec.FanDepth;
            Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
            (float ahead, float side) Local(float angleDeg, float dist) { Vector3 p = Quaternion.AngleAxis(angleDeg, Vector3.up) * c.Forward * dist; return (Vector3.Dot(p, c.Forward), Vector3.Dot(p, right)); }
            j.Dummies.Clear();
            j.Dummies.Add(Local(-half * 0.3f, depth * 0.75f));                  // in the fan (struck)
            j.Dummies.Add(Local(half + 22f, depth * 0.7f));                     // right of the edge
            j.Dummies.Add(Local(-half - 20f, depth * 0.55f));                   // left of the edge
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // steps
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static void Jump(Ctx c, long targetUs)
        {
            long now = c.Services.Clock.NowUs;
            if (targetUs <= now) return;
            double seconds = (targetUs - now) / 1000000.0;
            c.Services.Skew.Seconds += seconds;
            _note = "JUMP +" + seconds.ToString("0.00", Inv) + "s";
        }

        static bool JumpBeforeExpiry(Ctx c, double before)
        {
            long soonest = long.MaxValue;
            foreach (var m in c.Log.Materials) if (m.Alive) soonest = Math.Min(soonest, m.ExpiresUs);
            if (soonest == long.MaxValue) return false;      // the material is not there yet (a peak's cage): try again next frame
            Jump(c, soonest - Vol2Time.FromSeconds(before));
            return true;
        }

        static bool JumpBeforeStripEnd(Ctx c, double before)
        {
            if (!c.Coordinator.Verbs.TryGet(Verb.Split, out var ex) || !(ex is Vol2SplitExecutor split)) return true;
            long until = 0;
            foreach (var s in split.Strips.Entries) if (s.Cast == c.Log.Cast && s.UntilUs > until) until = s.UntilUs;
            if (until <= 0) return false;
            Jump(c, until - Vol2Time.FromSeconds(before));
            return true;
        }

        static void RaiseLetter(Ctx c, char letter)
        {
            try { c.Wiring.LetterChannel.Raise(new DrawnLetter(letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); }
            catch (Exception e) { Problems.Add("follow-up letter " + letter + ": " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // the state machine (one step per game frame)
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static void Tick()
        {
            if (!_running) return;
            try
            {
                if (!EditorApplication.isPlaying) { Finish("Play ended"); return; }
                if (EditorApplication.isPaused) return;
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                switch (_st)
                {
                    case St.WaitPresenter: WaitPresenter(); break;
                    case St.Idle: NextJob(); break;
                    case St.Setup: Setup(); break;
                    case St.Settle: if (--_settle <= 0) CastNow(); break;
                    case St.Record: Record(); break;
                    case St.Teardown: Teardown(); break;
                }
            }
            catch (Exception e) { Problems.Add("EXCEPTION " + e.GetType().Name + " " + e.Message + " @ " + e.StackTrace.Split('\n').FirstOrDefault()); Finish("exception"); }
        }

        static void WaitPresenter()
        {
            if (_legacy)
            {
                var w = Object.FindFirstObjectByType<CombatLoopWiring>();
                if (w == null || w.LetterChannel == null || w.SummonPlayer == null) { if (++_settle > 900) { Problems.Add("legacy: no wiring / player after 900 frames"); Finish("no wiring"); } return; }
                if (Object.FindFirstObjectByType<Vol2VfxPresenter>() != null) Problems.Add("legacy=1 but a vol.2 presenter exists: this Play is a Vol2 scope, not the main game's route (use vfx9a:play-legacy)");
                _settle = 0;
                _ctx = new Ctx { Wiring = w };
                _legacyCaster = new Vol2WiringCaster(w);
                if (Camera.main == null) { Problems.Add("no main camera"); Finish("no camera"); return; }
                MakeCamera(Camera.main);
                Time.captureFramerate = 60;
                _lookDriver = Object.FindFirstObjectByType<Oheangbu.App.World.LookDriver308>();
                if (_mainLook)
                {
                    var prof = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.LookProfile308>(MainLookProfile);
                    if (_lookDriver != null && prof != null) { _lookSaved = _lookDriver.Profile; _lookDriver.Profile = prof; _lookSwapped = true; Finished.Add("look=main: LookDriver308 profile -> " + prof.name + " (runtime only)"); }
                }
                Finished.Add("legacy=1: the Legacy308 route (the main game's SpellResolver and the old 120 effects), letters raised on the same channel");
                if (_legacyFeed)
                {
                    w.CastPlanned += _onLegacyPlanned = plan => { if (plan != null) FeedOld(w, plan.Cast.Letter, plan, false); };
                    w.CastAccepted += _onLegacyAccepted = (cast, o, f) => { if (cast.Kind == Oheangbu.Spellcraft.SpellKind.Parry) FeedOld(w, cast.Letter, null, true); };
                    Finished.Add("legacy=2: LAB-ONLY body feed - the real BrushStrokeFeedAdapter.SpawnPattern is called at the old route's plan (apex) / parry accept with one synthetic stroke at the adapter's drawing-surface centre (a lab letter has no drawn strokes)");
                }
                _st = St.Idle;
                return;
            }
            var p = Object.FindFirstObjectByType<Vol2VfxPresenter>();
            var wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
            if (p == null)
            {
                // the scope attached none (the runtime of this build has no attach line): bind one next to the debug presenter
                var dbg = Object.FindFirstObjectByType<Vol2DebugPresenter>();
                if (dbg != null && dbg.Coordinator != null) p = Vol2VfxPresenter.Attach(dbg.Coordinator, dbg.transform.parent, null, dbg);
            }
            if (p == null || wiring == null || wiring.LetterChannel == null) { if (++_settle > 900) { Problems.Add("no presenter / wiring after 900 frames"); Finish("no presenter"); } return; }
            _settle = 0;
            var shapes = p.Coordinator.Services.Shapes;
            _ctx = new Ctx { Presenter = p, Shapes = shapes, Coordinator = p.Coordinator, Wiring = wiring, Services = p.Coordinator.Services };
            var main = Camera.main;
            if (main == null) { Problems.Add("no main camera"); Finish("no camera"); return; }
            MakeCamera(main);
            _ctx.Services.Route.Adapter.Decided += _onDecided = d => _lastDecision = d;
            _ctx.Coordinator.CastBegan += _onBegan = l => { if (_beganLog == null) _beganLog = l; };
            Time.captureFramerate = 60;
            // D308-150 V9: apex=0|1 = the TE-S3-6 launch switch for this run (the lab scope's own value is put back by Finish)
            _apexTuning = p.Coordinator.Tuning; _apexSaved = _apexTuning.LaunchAtApex;
            if (_apex.HasValue) _apexTuning.LaunchAtApex = _apex.Value;
            if (p.Legacy != null) p.Legacy.WardOnRingBand = _wardBand;
            // step 5a: lasting=0|1 = the bridge stage's lasting variants for this run (Finish puts the stage's own override back)
            if (p.Legacy != null) { _lastingSaved = p.Legacy.LastingOverride; if (_lasting.HasValue) p.Legacy.LastingOverride = _lasting; Finished.Add("step 5a lasting variants = " + p.Legacy.LastingVariants + (_lasting.HasValue ? " (lasting=" + (_lasting.Value ? 1 : 0) + ")" : " (the set's switch)")); }
            Finished.Add("TE-S3-6 LaunchAtApex = " + _apexTuning.LaunchAtApex + " (scope default " + _apexSaved + ", path " + _apexTuning.PathApexSeconds.ToString("0.00", Inv) + " s, fan " + _apexTuning.FanApexSeconds.ToString("0.00", Inv) + " s)"
                + " | bridge stage " + (p.Legacy != null ? "bound" : "MISSING"));
            _lookDriver = Object.FindFirstObjectByType<Oheangbu.App.World.LookDriver308>();
            if (_mainLook)
            {
                var prof = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.LookProfile308>(MainLookProfile);
                if (_lookDriver == null || prof == null) Problems.Add("look=main: " + (_lookDriver == null ? "no LookDriver308 in the lab" : "no " + MainLookProfile));
                else { _lookSaved = _lookDriver.Profile; _lookDriver.Profile = prof; _lookSwapped = true; Finished.Add("look=main: LookDriver308 profile " + (_lookSaved != null ? _lookSaved.name : "none") + " -> " + prof.name + " (runtime only)"); }
            }
            _st = St.Idle;
        }

        /// <summary>A point inside the first geography region of the realm (its polygon's centroid) - where the look steps for realm=auto.</summary>
        static Vector3? RealmPoint(string realmName)
        {
            if (_lookDriver == null) return null;
            var geo = typeof(Oheangbu.App.World.LookDriver308).GetField("_geography", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_lookDriver) as Oheangbu.Data.World.WorldMacroSheetSO;
            if (geo == null || geo.Regions == null) return null;
            foreach (var rg in geo.Regions)
            {
                if (rg == null || rg.Realm.ToString() != realmName || rg.Polygon == null || rg.Polygon.Length == 0) continue;
                Vector2 sum = Vector2.zero; foreach (var v in rg.Polygon) sum += v;
                sum /= rg.Polygon.Length;
                return new Vector3(sum.x, 60f, sum.y);
            }
            return null;
        }

        /// <summary>What the vol.2 capture renders with (the copied main camera of the lab): pipeline asset, renderer, its features, volumes, the look driver, light, sky, quality.</summary>
        public static string Probe()
        {
            var sb = new StringBuilder();
            const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            int q = QualitySettings.GetQualityLevel();
            sb.AppendLine("playing=" + EditorApplication.isPlaying + " scene=" + SceneManager.GetActiveScene().path);
            sb.AppendLine("quality " + q + " '" + QualitySettings.names[q] + "' | QualitySettings.renderPipeline=" + (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : "null") + " | Graphics default=" + (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name : "null") + " | current=" + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "null"));
            var pipe = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var list = pipe != null ? pipe.GetType().GetField("m_RendererDataList", Any)?.GetValue(pipe) as Array : null;
            int defIdx = pipe != null && pipe.GetType().GetField("m_DefaultRendererIndex", Any)?.GetValue(pipe) is int di ? di : -1;
            if (list != null) for (int i = 0; i < list.Length; i++) { var d = list.GetValue(i) as ScriptableObject; sb.AppendLine("  renderer[" + i + "] = " + (d != null ? d.name + " (" + AssetDatabase.GetAssetPath(d) + ")" : "null") + (i == defIdx ? "  <- default" : "")); }
            var main = Camera.main;
            var dataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (main != null && dataType != null)
            {
                var data = main.GetComponent(dataType);
                int idx = data != null && dataType.GetField("m_RendererIndex", Any)?.GetValue(data) is int ri ? ri : -99;
                object post = dataType.GetProperty("renderPostProcessing")?.GetValue(data), aa = dataType.GetProperty("antialiasing")?.GetValue(data), mask = dataType.GetProperty("volumeLayerMask")?.GetValue(data);
                var renderer = dataType.GetProperty("scriptableRenderer")?.GetValue(data);
                sb.AppendLine("Camera.main '" + main.name + "' rendererIndex=" + idx + " -> " + (renderer != null ? renderer.GetType().Name : "?") + " | post=" + post + " aa=" + aa + " volumeMask=" + mask + " | fov=" + main.fieldOfView + " far=" + main.farClipPlane + " | the vol.2 capture camera copies this camera and its URP data (stack cleared)");
                int use = idx < 0 ? defIdx : idx;
                var rd = list != null && use >= 0 && use < list.Length ? list.GetValue(use) as ScriptableObject : null;
                if (rd != null)
                {
                    sb.AppendLine("  used renderer data: " + rd.name);
                    if (rd.GetType().GetProperty("rendererFeatures")?.GetValue(rd) is System.Collections.IList feats)
                        foreach (var f in feats) { var so = f as ScriptableObject; bool act = so != null && so.GetType().GetProperty("isActive")?.GetValue(so) is bool b && b; sb.AppendLine("    feature " + (so != null ? so.name + " [" + so.GetType().Name + "] active=" + act : "null")); }
                }
            }
            var volType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            if (volType != null)
                foreach (var v in Object.FindObjectsByType(volType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var b = v as Behaviour; var prof = volType.GetProperty("sharedProfile")?.GetValue(v) as ScriptableObject;
                    string comps = "";
                    if (prof != null && prof.GetType().GetField("components")?.GetValue(prof) is System.Collections.IList cs) foreach (var c in cs) { var so = c as ScriptableObject; if (so != null) comps += so.GetType().Name + (so.GetType().GetField("active")?.GetValue(so) is bool a && !a ? "(off)" : "") + " "; }
                    sb.AppendLine("Volume '" + b.name + "' enabled=" + (b.isActiveAndEnabled) + " global=" + volType.GetField("isGlobal")?.GetValue(v) + " priority=" + volType.GetProperty("priority")?.GetValue(v) + " weight=" + volType.GetField("weight")?.GetValue(v) + " profile=" + (prof != null ? prof.name : "null") + " | " + comps);
                }
            var ld = Object.FindFirstObjectByType<Oheangbu.App.World.LookDriver308>();
            if (ld != null) _lookDriver = ld;
            if (ld != null)
            {
                string w = ""; foreach (Oheangbu.Data.World.RealmId r in Enum.GetValues(typeof(Oheangbu.Data.World.RealmId))) w += r + "=" + ld.RealmWeight(r).ToString("0.00", Inv) + " ";
                sb.AppendLine("LookDriver308 '" + ld.name + "' profile=" + (ld.Profile != null ? ld.Profile.name : "null") + " live=" + ld.Live + " realm weights at the main camera: " + w + "| main camera at " + (main != null ? main.transform.position.ToString("F0") : "-"));
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) sb.AppendLine("Directional '" + l.name + "' enabled=" + l.isActiveAndEnabled + " intensity=" + l.intensity.ToString("0.###", Inv) + " colour=" + l.color + " rot=" + l.transform.eulerAngles.ToString("F0") + " shadows=" + l.shadows + (RenderSettings.sun == l ? " (RenderSettings.sun)" : ""));
            sb.AppendLine("RenderSettings fog=" + RenderSettings.fog + " mode=" + RenderSettings.fogMode + " colour=" + RenderSettings.fogColor + " density=" + RenderSettings.fogDensity + " | ambient " + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor + " eq=" + RenderSettings.ambientEquatorColor + " ground=" + RenderSettings.ambientGroundColor + " intensity=" + RenderSettings.ambientIntensity + " | skybox=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name + " (" + RenderSettings.skybox.shader.name + ")" : "none"));
            sb.AppendLine("Realm points: Jeokro=" + RealmPoint("Jeokro") + " Cheongrim=" + RealmPoint("Cheongrim"));
            return sb.ToString();
        }

        static void MakeCamera(Camera main)
        {
            DestroyCamera();
            var go = new GameObject("Vol2Vfx_Cam") { hideFlags = HideFlags.HideAndDontSave };
            _cam = go.AddComponent<Camera>();
            _cam.CopyFrom(main);
            _cam.enabled = false;
            // the URP settings of the game camera (post-processing on, like the game), no stack - by name, so this assembly needs no URP reference
            var dataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (dataType != null)
            {
                var src = main.GetComponent(dataType); var dst = go.GetComponent(dataType) ?? go.AddComponent(dataType);
                if (src != null) EditorUtility.CopySerialized(src, dst);
                var stack = dataType.GetProperty("cameraStack")?.GetValue(dst) as System.Collections.IList;
                stack?.Clear();
            }
            _rtVideo = new RenderTexture(VideoW, VideoH, 24); _rtKey = new RenderTexture(KeyW, KeyH, 24);
            _texVideo = new Texture2D(VideoW, VideoH, TextureFormat.RGB24, false); _texKey = new Texture2D(KeyW, KeyH, TextureFormat.RGB24, false);
        }

        static void DestroyCamera()
        {
            if (_cam != null) Object.DestroyImmediate(_cam.gameObject);
            foreach (var rt in new[] { _rtVideo, _rtKey }) if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            foreach (var tx in new[] { _texVideo, _texKey }) if (tx != null) Object.DestroyImmediate(tx);
            _cam = null; _rtVideo = _rtKey = null; _texVideo = _texKey = null;
        }

        static void NextJob()
        {
            if (Queue.Count == 0) { Finish("all jobs done"); return; }
            _job = Queue.Dequeue();
            _st = St.Setup;
        }

        static Color FromLab(float L, float a, float b)
        {
            // CIE Lab (D65) -> sRGB
            float fy = (L + 16f) / 116f, fx = fy + a / 500f, fz = fy - b / 200f;
            Func<float, float> inv = t => t * t * t > 0.008856f ? t * t * t : (t - 16f / 116f) / 7.787f;
            float X = 0.95047f * inv(fx), Y = inv(fy), Z = 1.08883f * inv(fz);
            float r = 3.2406f * X - 1.5372f * Y - 0.4986f * Z, g = -0.9689f * X + 1.8758f * Y + 0.0415f * Z, bl = 0.0557f * X - 0.2040f * Y + 1.0570f * Z;
            Func<float, float> gam = u => u <= 0.0031308f ? 12.92f * u : 1.055f * Mathf.Pow(Mathf.Max(u, 0f), 1f / 2.4f) - 0.055f;
            return new Color(Mathf.Clamp01(gam(r)), Mathf.Clamp01(gam(g)), Mathf.Clamp01(gam(bl)), 1f);
        }

        const string JeokroSoil = "Assets/_Project/Art/World/Reworld292/Highlands293/Sections/Materials/Jeokro_Soil.mat", CheongrimSoil = "Assets/_Project/Art/World/Reworld292/Highlands293/Sections/Materials/Cheongrim_Soil.mat";
        static string EnemyPrefab = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_forest_bear.prefab";
        static readonly HashSet<string> MeasuredGround = new HashSet<string>();

        static (float L, float a, float b) LabOf(Color c)
        {
            Func<float, float> lin = u => u <= 0.04045f ? u / 12.92f : Mathf.Pow((u + 0.055f) / 1.055f, 2.4f);
            float r = lin(c.r), g = lin(c.g), bl = lin(c.b);
            float X = (0.4124f * r + 0.3576f * g + 0.1805f * bl) / 0.95047f, Y = 0.2126f * r + 0.7152f * g + 0.0722f * bl, Z = (0.0193f * r + 0.1192f * g + 0.9505f * bl) / 1.08883f;
            Func<float, float> f = t => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
            return (116f * f(Y) - 16f, 500f * (f(X) - f(Y)), 200f * (f(Y) - f(Z)));
        }

        /// <summary>Measures (not forces) the Lab colour of the RENDERED real ground (after light, fog and the ink post of the shoulder camera) in a patch right of the caster, for the report.</summary>
        static void MeasureGround(Ctx c, string back)
        {
            Vector3 origin = c.Origin; float yaw = Quaternion.LookRotation(c.Forward, Vector3.up).eulerAngles.y;
            Shoot(origin, yaw, 15f, _rtVideo, _texVideo, Path.Combine(OutRoot, "ground_" + back + ".jpg"), false);
            var px = _texVideo.GetPixels((int)(VideoW * 0.60f), (int)(VideoH * 0.18f), (int)(VideoW * 0.10f), (int)(VideoH * 0.12f));
            float r = 0, g = 0, b = 0; foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
            var lab = LabOf(new Color(r / px.Length, g / px.Length, b / px.Length));
            MeasuredGround.Add(back);
            Finished.Add("ground " + back + " (" + (back == "red" ? JeokroSoil : CheongrimSoil) + "): rendered patch Lab(" + lab.L.ToString("0.0", Inv) + "," + lab.a.ToString("0.0", Inv) + "," + lab.b.ToString("0.0", Inv) + ") [plan: " + (back == "red" ? "35,30,25" : "25,-22,15") + "]");
        }
        /// <summary>legacy=1: a full ink pool before every letter (the old route spends it; a dry pool would misfire the later jobs). The pool is private to the wiring: reflection, editor capture only.</summary>
        static void LegacyRefill(Ctx c)
        {
            try
            {
                var pool = typeof(CombatLoopWiring).GetField("_ink", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(c.Wiring);
                pool?.GetType().GetMethod("Restore", new[] { typeof(float) })?.Invoke(pool, new object[] { 1f });
            }
            catch (Exception e) { Problems.Add("legacy refill: " + e.Message); }
        }

        /// <summary>legacy=2 (LAB-ONLY): the old route's body, through the REAL stroke adapter. At the old route's plan (CastPlanned, raised inside the apex dispatch after
        /// SwingInk308 pinned the brush tip's stem) or its parry accept, the adapter's pending hit context is set to what the wiring pushes in the game (target + flight for a
        /// single shot, the area plan for an area cast, a guard for a parry; config guard clock) and its private SpawnPattern runs with one synthetic stroke at the centre of
        /// its drawing surface - what a drawn letter would hand it. The synthetic stroke is destroyed at once (no letter is shown).</summary>
        static void FeedOld(CombatLoopWiring w, char letter, CastPlan plan, bool parry)
        {
            if (!_running || _job == null || (_st != St.Record && _st != St.Settle) || letter != _job.Letter) return;      // a parry is accepted inside the raise itself (Settle -> CastNow)
            try
            {
                var adapter = w.BrushAdapter308; var set = w.VisualSet308; var cam = Camera.main;
                if (adapter == null || set == null || cam == null || !set.TryGet(letter, out var visual) || visual.FxPrefab == null)
                { _fedMissing++; Problems.Add("legacy=2: no adapter / visual set / camera / body for " + letter); return; }
                const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
                void Set(string field, object value)
                {
                    var fi = typeof(BrushStrokeFeedAdapter).GetField(field, Hidden);
                    if (fi == null) throw new MissingFieldException("BrushStrokeFeedAdapter." + field);
                    fi.SetValue(adapter, value);
                }
                Transform target = null; float duration = 0f; AreaImpactPlan area = plan != null ? plan.Area : null;
                if (plan != null && plan.Hits.Count > 0 && plan.Hits[0].Target != null)
                {
                    if (area == null) { target = plan.Hits[0].Target.transform; duration = Mathf.Max(0f, plan.Hits[0].ImpactTime - Time.time); }
                    else if (area.Shape != AreaShape.Cone) target = plan.Hits[0].Target.transform;     // the wiring aims a circle / path / volley body at its target, a cone at nothing
                }
                Set("_pendingFxPrefab", visual.FxPrefab); Set("_pendingFxScaleMul", visual.ScaleMul > 0f ? visual.ScaleMul : 1f); Set("_pendingFxArcHeight", Mathf.Max(0f, visual.ArcHeight));
                Set("_pendingAttack", !parry); Set("_pendingParry", parry); Set("_pendingSummon", false); Set("_pendingSummonExternal", false); Set("_pendingFieldExternal", false);
                Set("_pendingAttackTarget", target); Set("_pendingAttackDuration", duration); Set("_pendingAreaPlan", area);
                Set("_pendingGuardDuration", 0f); Set("_pendingGuardWindow", 0f);
                Vector3 centre = cam.transform.position + cam.transform.forward * adapter.EffectiveSurfaceDistance(cam);
                var go = new GameObject("Vol2VfxOldLetter") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var stroke = go.AddComponent<BrushStrokeRenderer>();
                    stroke.Data.Add(new BrushStrokePoint(centre - cam.transform.right * 0.2f, 0.02f, 1f, 0f));
                    stroke.Data.Add(new BrushStrokePoint(centre + cam.transform.right * 0.2f, 0.02f, 1f, 0f));
                    var groupType = typeof(BrushStrokeFeedAdapter).GetNestedType("FadingGroup", BindingFlags.NonPublic);
                    object group = Activator.CreateInstance(groupType, true);
                    ((System.Collections.IList)groupType.GetField("Strokes").GetValue(group)).Add(stroke);
                    groupType.GetField("FlashColor").SetValue(group, Color.white);
                    typeof(BrushStrokeFeedAdapter).GetMethod("SpawnPattern", Hidden).Invoke(adapter, new[] { group });
                    _fed++; _note = "OLD BODY FED (" + letter + (parry ? " guard" : area != null ? " " + area.Shape : " single") + ")";
                }
                finally { Object.Destroy(go); }
            }
            catch (Exception e) { _fedMissing++; Problems.Add("legacy=2 feed " + letter + ": " + (e.InnerException ?? e).Message); }
        }

        /// <summary>legacy=1: the same ground, enemy, distances and camera as the vol.2 jobs, around the player of the Legacy308 lab (no vol.2 registry: the old route finds the enemy itself).</summary>
        static void SetupLegacy(Ctx c, Job j)
        {
            foreach (var v in Object.FindObjectsByType<EnemyVitals>(FindObjectsSortMode.None))
                if (v.gameObject.activeSelf && !v.name.StartsWith("Vol2VfxDummy", StringComparison.Ordinal)) { c.HiddenLab.Add(v.gameObject); v.gameObject.SetActive(false); }
            c.Origin = _legacyCaster.Position; c.Forward = Vol2ShapeGeometry.FlatUnit(_legacyCaster.AimDirection);
            _realmEye = _realmMode == "auto" ? RealmPoint(j.Back == "red" ? "Jeokro" : "Cheongrim") : (Vector3?)null;
            Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
            string soil = j.Back == "red" ? JeokroSoil : CheongrimSoil;
            var soilMat = AssetDatabase.LoadAssetAtPath<Material>(soil);
            c.Backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            c.Backdrop.name = "Vol2VfxGround"; Object.DestroyImmediate(c.Backdrop.GetComponent<Collider>());
            c.Backdrop.transform.SetPositionAndRotation(c.Origin + Vector3.up * 0.012f + c.Forward * 30f, Quaternion.Euler(90f, 0f, 0f));
            c.Backdrop.transform.localScale = new Vector3(240f, 240f, 1f);
            if (soilMat != null) c.Backdrop.GetComponent<MeshRenderer>().sharedMaterial = soilMat;
            var posts = GameObject.Find("Lab_DistancePosts");
            if (posts != null && posts.activeSelf) { posts.SetActive(false); c.HiddenLab.Add(posts); }
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            int n = 0;
            foreach (var d in j.Dummies)
            {
                if (enemyPrefab == null) { Problems.Add("enemy prefab not found: " + EnemyPrefab); break; }
                Vector3 at = c.Origin + c.Forward * d.ahead + right * d.side;
                var holder = new GameObject("Vol2VfxHolder"); holder.SetActive(false);
                var go = Object.Instantiate(enemyPrefab, holder.transform);
                go.name = "Vol2VfxDummy_" + (char)('A' + n++);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && !(mb is EnemyVitals) && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("Oheangbu", StringComparison.Ordinal)) mb.enabled = false;
                foreach (var agent in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
                go.transform.SetParent(null, false);
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-c.Forward, Vector3.up));
                Object.Destroy(holder);
                go.SetActive(true);
                c.Dummies.Add(go);
            }
            Physics.SyncTransforms();
            // legacy=2: the dummies are enemies of THIS encounter for the old route too (its lock-on / free aim / scheduled hits read the wiring's own target list, filled at
            // Awake from the scene - a dummy made later is not in it, so the old shots of legacy=1 flew to the free-aim point in the air). LAB-ONLY, taken out at teardown.
            if (_legacyFeed)
            {
                var list = typeof(CombatLoopWiring).GetField("_targets", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(c.Wiring) as List<EnemyVitals>;
                if (list == null) Problems.Add("legacy=2: the wiring's target list was not found (the old route has no target)");
                else foreach (var d in c.Dummies) { var v = d.GetComponentInChildren<EnemyVitals>(true); if (v != null && !list.Contains(v)) { list.Add(v); LegacyTargets.Add(v); } }
                // the old main game's duel: the caster is locked on the first dummy (LockOn private state set here, LAB-ONLY; the free aim reads the lab's game camera, which
                // does not look at the capture's dummies). Unlocked again at teardown.
                var lockOn = typeof(CombatLoopWiring).GetField("_lockOn", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(c.Wiring) as LockOn;
                var first = c.Dummies.Count > 0 ? c.Dummies[0].GetComponentInChildren<EnemyVitals>(true) : null;
                if (lockOn == null || first == null) Problems.Add("legacy=2: no LockOn on the wiring (the old shots aim by free aim)");
                else { typeof(LockOn).GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lockOn, first); typeof(LockOn).GetField("_locked", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lockOn, true); LegacyLock = lockOn; }
            }
            c.Log = null; _note = "";
            _folder = Path.Combine(OutRoot, "frames", j.Name);
            if (Directory.Exists(_folder)) foreach (var f in Directory.GetFiles(_folder)) File.Delete(f);
            Directory.CreateDirectory(_folder);
            _index = new StreamWriter(Path.Combine(_folder, "index.csv"), false, new UTF8Encoding(false));
            _index.WriteLine("frame,unityFrame,clockUs,ageSec,video,key,note,template,variant,body,back,letter,parts,version,verts,views");
            _frames = 0; _keys = 0;
            _guardUntil = EditorApplication.timeSinceStartup + 120.0;
            _settle = 20; _st = St.Settle;          // the old route's previous effects die out before the next letter
        }

        static void Setup()
        {
            var c = _ctx; var j = _job;
            if (_legacy) { SetupLegacy(c, j); return; }
            c.Shapes.PoolInk?.Refill();
            c.Wiring.Vol2Defence?.ResetForRow();
            c.Coordinator.AbortAll();
            c.Presenter.ClearAll();
            c.Coordinator.Reactions.Enabled = false;                  // slice 9a draws the verbs, not the reactions
            if (c.Shapes.Presenter != null) c.Shapes.Presenter.Enabled = false;
            var reg = c.Shapes.Registry; reg.ExtrasOnly = true; reg.ClearExtras();
            // the lab's own dummies stand at fixed spots: out of the frame for the capture
            foreach (var v in Object.FindObjectsByType<EnemyVitals>(FindObjectsSortMode.None))
                if (v.gameObject.activeSelf && !v.name.StartsWith("Vol2VfxDummy", StringComparison.Ordinal)) { c.HiddenLab.Add(v.gameObject); v.gameObject.SetActive(false); }
            c.Origin = c.Shapes.Caster.Position; c.Forward = Vol2ShapeGeometry.FlatUnit(c.Shapes.Caster.AimDirection);
            _realmEye = _realmMode == "auto" ? RealmPoint(j.Back == "red" ? "Jeokro" : "Cheongrim") : (Vector3?)null;
            if (_realmMode == "auto" && _realmEye == null) Problems.Add("realm=auto: no geography region for " + j.Back);
            Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
            if (j.FanNeighbours) FanNeighbours(c, j);
            // the REAL ground look: the realm's own soil material (적로 / 청림) on a flat ground quad, and a real enemy prefab as the target (never saved into a scene; destroyed at the end of the job)
            string soil = j.Back == "red" ? JeokroSoil : CheongrimSoil;
            var soilMat = AssetDatabase.LoadAssetAtPath<Material>(soil);
            if (soilMat == null) { Problems.Add("ground material not found: " + soil + " (a flat colour stands in)"); soilMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = j.Back == "red" ? new Color(0.45f, 0.2f, 0.15f) : new Color(0.1f, 0.25f, 0.12f) }; c.BackdropMat = soilMat; }
            c.Backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            c.Backdrop.name = "Vol2VfxGround"; Object.DestroyImmediate(c.Backdrop.GetComponent<Collider>());
            c.Backdrop.transform.SetPositionAndRotation(c.Origin + Vector3.up * 0.012f + c.Forward * 30f, Quaternion.Euler(90f, 0f, 0f));
            c.Backdrop.transform.localScale = new Vector3(240f, 240f, 1f);
            c.Backdrop.GetComponent<MeshRenderer>().sharedMaterial = soilMat;
            var posts = GameObject.Find("Lab_DistancePosts");
            if (posts != null && posts.activeSelf) { posts.SetActive(false); c.HiddenLab.Add(posts); }
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            if (enemyPrefab == null) Problems.Add("enemy prefab not found: " + EnemyPrefab + " (a capsule stands in)");
            c.DummyMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.38f, 0.37f, 0.35f, 1f), name = "Vol2VfxDummy" };
            int n = 0;
            foreach (var d in j.Dummies)
            {
                Vector3 at = c.Origin + c.Forward * d.ahead + right * d.side;
                GameObject go; EnemyVitals vitals;
                if (enemyPrefab != null)
                {
                    // instantiated under an inactive parent so the enemy's own brain never wakes; the code of the game that is not the body is switched off
                    var holder = new GameObject("Vol2VfxHolder"); holder.SetActive(false);
                    go = Object.Instantiate(enemyPrefab, holder.transform);
                    go.name = "Vol2VfxDummy_" + (char)('A' + n++);
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                        if (mb != null && !(mb is EnemyVitals) && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("Oheangbu", StringComparison.Ordinal)) mb.enabled = false;
                    foreach (var agent in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
                    go.transform.SetParent(null, false);
                    go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-c.Forward, Vector3.up));
                    Object.Destroy(holder);
                    go.SetActive(true);
                    vitals = go.GetComponentInChildren<EnemyVitals>(true) ?? go.AddComponent<EnemyVitals>();
                }
                else
                {
                    go = new GameObject("Vol2VfxDummy_" + (char)('A' + n++));
                    go.transform.position = at;
                    var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.4f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f);
                    vitals = go.AddComponent<EnemyVitals>();
                    var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Object.DestroyImmediate(vis.GetComponent<Collider>());
                    vis.transform.SetParent(go.transform, false); vis.transform.localPosition = new Vector3(0f, 0.9f, 0f); vis.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                    vis.GetComponent<MeshRenderer>().sharedMaterial = c.DummyMat;
                }
                reg.AddExtra(vitals);
                c.Dummies.Add(go);
            }
            Physics.SyncTransforms();
            if (j.PlantWood) PlantWood(c);
            if (!MeasuredGround.Contains(j.Back)) MeasureGround(c, j.Back);
            c.Log = null; _beganLog = null; _lastDecision = null; _note = ""; _lastJumpAge = -1f; _describe = 0;
            foreach (var s in _job.Steps) s.Done = false;
            _folder = Path.Combine(OutRoot, "frames", _job.Name);
            if (Directory.Exists(_folder)) foreach (var f in Directory.GetFiles(_folder)) File.Delete(f);
            Directory.CreateDirectory(_folder);
            _index = new StreamWriter(Path.Combine(_folder, "index.csv"), false, new UTF8Encoding(false));
            _index.WriteLine("frame,unityFrame,clockUs,ageSec,video,key,note,template,variant,body,back,letter,parts,detail");
            _frames = 0; _keys = 0;
            _guardUntil = EditorApplication.timeSinceStartup + 120.0;
            _settle = 5; _st = St.Settle;
        }

        static void CastNow()
        {
            var c = _ctx;
            if (_legacy) { LegacyRefill(c); if (_strokeFeed) FeedStroke(c); RaiseLetter(c, _job.Letter); if (_strokeFeed) CommitStroke(c); c.Log = null; _legacyFrames = 0; _st = St.Record; return; }
            c.Shapes.PoolInk?.Refill();
            c.Wiring.Vol2Defence?.ResetForRow();
            _beganLog = null; _lastDecision = null;
            if (_job.Template == "AJ") { BeginRiposte(c); return; }
            if (_strokeFeed) FeedStroke(c);
            RaiseLetter(c, _job.Letter);
            if (_strokeFeed) CommitStroke(c);
            c.Log = _beganLog;
            // step 4: 걱 is the recovery (the defence kit runs it, no vol.2 cast log): its old look comes from the kit's own records - recorded on the frame count
            if (c.Log == null && _lastDecision != null && _lastDecision.Accepted && _lastDecision.IsRecovery) { _noLogFrames = 0; _note = "recovery (the defence kit's, no cast log)"; _st = St.Record; return; }
            if (c.Log == null) { Problems.Add(_job.Name + ": the letter " + _job.Letter + " started no cast (" + (_lastDecision != null ? "accepted=" + _lastDecision.Accepted + " reason=" + _lastDecision.Reason : "no decision") + ")"); _st = St.Teardown; return; }
            _st = St.Record;
        }

        /// <summary>Template AJ: the approved 앞잡 one stroke (D308-148) fired at the first body exactly as Riposte301 aims it (torso = the collider centre, eye = the shoulder camera,
        /// brush tip of the scene's great brush), driven frame by frame on 1/60 s; its own colour-drain volume runs as in the game.</summary>
        static void BeginRiposte(Ctx c)
        {
            if (_rip == null)
            {
                var profile = AssetDatabase.LoadAssetAtPath<Oheangbu.App.World.RiposteStroke308Profile>(RiposteProfile);
                _rip = new Oheangbu.App.World.RiposteStroke308Effect();
                if (profile == null || !_rip.Warm(null, profile, _lookDriver != null ? _lookDriver.gameObject.layer : 0)) { Problems.Add("AJ: the riposte profile " + RiposteProfile + " is not ready"); _rip = null; _st = St.Teardown; return; }
            }
            var bear = c.Dummies.Count > 0 ? c.Dummies[0] : null;
            if (bear == null) { Problems.Add("AJ: no target"); _st = St.Teardown; return; }
            var col = bear.GetComponentInChildren<Collider>();
            var vit = bear.GetComponentInChildren<EnemyVitals>(true);
            Vector3 torso = col != null && col.enabled && col.bounds.size.y > .3f ? col.bounds.center : bear.transform.position + Vector3.up * 0.9f;
            float yaw = Quaternion.LookRotation(c.Forward, Vector3.up).eulerAngles.y;
            Quaternion rot = Quaternion.Euler(15f, yaw, 0f);
            Vector3 eye = c.Origin + Vector3.up * 1.7f + rot * new Vector3(0.7f, 0.55f, -2.8f);
            var tip = c.Presenter.Model.BrushTip?.Invoke();
            var aim = new Oheangbu.App.World.RiposteStroke308Aim { Caster = c.Origin, Eye = eye, Torso = torso, TorsoRadius = vit != null ? vit.BodyRadius : 0.5f, BrushTip = tip ?? default, HasBrushTip = tip.HasValue };
            _ripSlot = _rip.Begin(aim, bear.transform, col);
            _ripFrames = 0;
            if (_ripSlot == null) { Problems.Add("AJ: Begin refused"); _st = St.Teardown; return; }
            _st = St.Record;
        }

        static void RecordRiposte()
        {
            var c = _ctx; var j = _job;
            _rip.Tick(1f / 60f); _ripFrames++;
            if (_ripSlot.ImpactReady) { _ripSlot.ImpactReady = false; _rip.Splash(_ripSlot); _note = "pierce (specks)"; }
            double age = _ripFrames / 60.0;
            bool keyWindow = (age >= 0.065 && age <= 0.10) || (age >= 0.24 && age <= 0.26) || (age >= 0.69 && age <= 0.71);
            SaveFrame(c, age, 0, j.Video, keyWindow);
            _note = "";
            if (age >= j.MaxAge || EditorApplication.timeSinceStartup > _guardUntil) _st = St.Teardown;
        }

        /// <summary>stroke=1: one drawn stroke into the real adapter (its private input handlers; the projection happens in its LateUpdate, as for a drawn letter).</summary>
        static void FeedStroke(Ctx c)
        {
            var a = c.Wiring != null ? c.Wiring.BrushAdapter308 : null;
            if (a == null) { Problems.Add(_job.Name + ": stroke=1 but no BrushStrokeFeedAdapter on the wiring"); return; }
            try
            {
                AdapterCall(a, "OnStrokeStarted");
                for (int i = 0; i < 6; i++) AdapterCall(a, "OnStrokePointAdded", new Vector2(Screen.width * (0.42f + 0.03f * i), Screen.height * (0.56f - 0.02f * i)));
                AdapterCall(a, "OnStrokeEnded");
            }
            catch (Exception e) { Problems.Add(_job.Name + " stroke feed: " + (e.InnerException ?? e).Message); }
        }

        /// <summary>stroke=1: the drawing input's commit (recognised = true) right after the letter, in the same frame, as the input raises it.</summary>
        static void CommitStroke(Ctx c)
        {
            var a = c.Wiring != null ? c.Wiring.BrushAdapter308 : null;
            if (a == null) return;
            try { AdapterCall(a, "OnCommitted", true); }
            catch (Exception e) { Problems.Add(_job.Name + " stroke commit: " + (e.InnerException ?? e).Message); }
        }

        static void AdapterCall(BrushStrokeFeedAdapter a, string method, params object[] args)
        {
            var m = typeof(BrushStrokeFeedAdapter).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) throw new MissingMethodException("BrushStrokeFeedAdapter." + method);
            m.Invoke(a, args.Length == 0 ? null : args);
        }

        /// <summary>stroke=1: every glyph body on screen this frame, by who made it (the stroke adapter names its pattern "SpellPattern"; the bridge stage "Vol2Legacy_*").</summary>
        static void CountBodies()
        {
            foreach (var fx in Object.FindObjectsByType<SpellSequenceEffect>(FindObjectsSortMode.None))
            {
                if (fx == null || !fx.gameObject.scene.IsValid()) continue;
                var root = fx.transform.root.gameObject;
                if (root.name == "SpellPattern") AdapterBodies.Add(root.GetInstanceID());
                // the bridge's cast body is "Vol2Legacy_<one glyph>_<kind>" (its lasting parts are "Vol2Legacy_<role>_<prefab>")
                else if (root.name.Length > 13 && root.name.StartsWith("Vol2Legacy_", StringComparison.Ordinal) && root.name[12] == '_' && fx.transform == root.transform) BridgeBodies.Add(root.GetInstanceID());
            }
            foreach (var u in Object.FindObjectsByType<InkUnravel308>(FindObjectsSortMode.None)) if (u != null) Unravels.Add(u.GetInstanceID());   // the drawn letter coming apart into the brush
        }

        static string BodyCount()
        {
            string s = " | stroke check: adapter bodies " + AdapterBodies.Count + ", bridge bodies " + BridgeBodies.Count + ", letter unravels " + Unravels.Count;
            AdapterBodies.Clear(); BridgeBodies.Clear(); Unravels.Clear();
            return s;
        }

        static void Record()
        {
            var c = _ctx; var j = _job;
            if (_strokeFeed) CountBodies();
            if (_legacy)
            {
                _legacyFrames++;
                double la = _legacyFrames / 60.0;
                bool kw = j.Keys && ((la >= 0.065 && la <= 0.10) || (la >= 0.165 && la <= 0.20) || (la >= 0.285 && la <= 0.315));
                if (j.Video || kw) SaveFrame(c, la, 0, j.Video, kw);
                _note = "";
                if (la >= j.MaxAge || EditorApplication.timeSinceStartup > _guardUntil) _st = St.Teardown;
                return;
            }
            if (j.Template == "AJ") { RecordRiposte(); return; }
            if (c.Log == null)
            {
                _noLogFrames++;
                double ra = _noLogFrames / 60.0;
                if (j.Video) SaveFrame(c, ra, c.Services.Clock.NowUs, true, false);
                _note = "";
                if (ra >= j.MaxAge || EditorApplication.timeSinceStartup > _guardUntil) _st = St.Teardown;
                return;
            }
            // one picture per game frame, after the presenter stepped on the scope clock (Vol2VfxPresenter.LastStepUs)
            long stepUs = c.Presenter.LastStepUs;
            double age = (stepUs - c.Log.StartUs) / 1000000.0;
            foreach (var s in j.Steps)
            {
                if (s.Done || age < s.At) continue;
                if (s.Do(c)) { s.Done = true; _note = s.Name; }
            }
            bool keyWindow = j.Keys && ((age >= 0.065 && age <= 0.10) || (age >= 0.165 && age <= 0.20) || (age >= 0.285 && age <= 0.315));
            if (Batchim(j.Template) && c.Presenter.Legacy != null && (_describe == 0 && age >= 1.0 || _describe == 1 && age >= 2.2)) { _describe++; Finished.Add("  parts at " + age.ToString("0.00", Inv) + " s: " + c.Presenter.Legacy.Describe()); }
            bool lateKey = j.Keys && j.Template == "P1" && j.Body == Body.Wood && age >= 1.70 && age <= 1.74;       // 각 cages its body for 1.5 s: the residue stands after that
            if (j.Video || keyWindow || lateKey) SaveFrame(c, age, stepUs, j.Video, keyWindow || lateKey);
            _note = "";
            bool retired = !c.Presenter.Model.TryGet(c.Log.Cast, out _);
            if (age >= j.MaxAge || (retired && age >= j.MinAge) || EditorApplication.timeSinceStartup > _guardUntil) { _st = St.Teardown; return; }
        }

        static void SaveFrame(Ctx c, double age, long stepUs, bool video, bool key)
        {
            Vector3 origin = c.Log != null ? c.Log.Spec.Origin : c.Origin, fwd = c.Log != null ? c.Log.Spec.Forward : c.Forward;
            float yaw = Quaternion.LookRotation(fwd, Vector3.up).eulerAngles.y;
            if (video) { Shoot(origin, yaw, _videoPitch, _rtVideo, _texVideo, Path.Combine(_folder, "f_" + _frames.ToString("D5", Inv) + ".jpg"), false); }
            if (video && _pitch2 > 0f) Shoot2(origin, yaw, Path.Combine(_folder, "g_" + _frames.ToString("D5", Inv) + ".jpg"));
            if (key)
            {
                string tag = _job.Template == "AJ" ? (age < 0.13 ? "a008" : age < 0.4 ? "r025" : "r070") : age < 0.13 ? "a008" : age < 0.24 ? "b018" : age > 1.0 ? "x172" : "b030";
                string stamp = age.ToString("0.000", Inv);
                Shoot(origin, yaw, 15f, _rtKey, _texKey, Path.Combine(_folder, "key_" + tag + "_p15_" + stamp + ".png"), true);
                Shoot(origin, yaw, 0f, _rtKey, _texKey, Path.Combine(_folder, "key_" + tag + "_p00_" + stamp + ".png"), true);
                _keys++;
            }
            _index.WriteLine(string.Join(",", _frames.ToString(Inv), Time.frameCount.ToString(Inv), stepUs.ToString(Inv), age.ToString("0.0000", Inv), video ? "1" : "0", key ? "1" : "0", _note.Replace(',', ';'),
                _job.Template, _job.Variant, _job.Body, _job.Back, _job.Letter, PartInfo(c)));
            _frames++;
        }

        /// <summary>The plan's shoulder camera: pivot 1.7 m above the cast point, local offset (0.7, 0.55, -2.8) turned with the aim (yaw) and the pitch about the pivot, FOV 60.</summary>
        static void Shoot(Vector3 origin, float yawDegrees, float pitchDegrees, RenderTexture rt, Texture2D tex, string file, bool png)
        {
            Quaternion rot = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            Vector3 pivot = origin + Vector3.up * 1.7f;
            _cam.transform.SetPositionAndRotation(pivot + rot * new Vector3(0.7f, 0.55f, -2.8f), rot);
            _cam.fieldOfView = 60f;
            _cam.targetTexture = rt;
            // realm=auto: the look steps at the realm's own point right before this render (the driver steps only for Camera.main; its globals and scene state persist into this render)
            if (_realmEye.HasValue && _lookDriver != null) _lookDriver.Refresh(_realmEye.Value);
            _cam.Render();
            var was = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            }
            finally { RenderTexture.active = was; }
            File.WriteAllBytes(file, png ? tex.EncodeToPNG() : tex.EncodeToJPG(90));
        }

        /// <summary>Step 4 reading view (NOT the game camera): the same shoulder rig pulled back x2 and pitched down by pitch2, so a state lying on the ground round the caster or
        /// a body can be read. The game camera (pitch 0) stays the main picture.</summary>
        static void Shoot2(Vector3 origin, float yawDegrees, string file)
        {
            Quaternion rot = Quaternion.Euler(_pitch2, yawDegrees, 0f);
            Vector3 pivot = origin + Vector3.up * 1.7f;
            _cam.transform.SetPositionAndRotation(pivot + rot * new Vector3(0.7f, 0.55f, -2.8f) * 2f, rot);
            _cam.fieldOfView = 60f;
            _cam.targetTexture = _rtVideo;
            if (_realmEye.HasValue && _lookDriver != null) _lookDriver.Refresh(_realmEye.Value);
            _cam.Render();
            var was = RenderTexture.active;
            try { RenderTexture.active = _rtVideo; _texVideo.ReadPixels(new Rect(0, 0, _rtVideo.width, _rtVideo.height), 0, 0, false); }
            finally { RenderTexture.active = was; }
            File.WriteAllBytes(file, _texVideo.EncodeToJPG(88));
        }

        static string PartInfo(Ctx c)
        {
            if (_legacy) return "-,legacy";
            if (c.Log == null) return "-,riposte reveal " + (_ripSlot != null ? _ripSlot.RevealU.ToString("0.00", Inv) + " dry " + _ripSlot.DryNow.ToString("0.00", Inv) : "- dry -") + " weight " + (_rip != null ? _rip.LookWeight.ToString("0.00", Inv) : "-");
            if (!c.Presenter.Model.TryGet(c.Log.Cast, out var inst)) return "0,retired";
            return inst.Parts.Count + ",live";
        }

        static void Teardown()
        {
            var c = _ctx; var j = _job;
            if (_legacy)
            {
                try { _index?.Dispose(); } catch (Exception) { }
                _index = null;
                Finished.Add(j.Name + " | legacy letter " + j.Letter + " | frames " + _frames + " | keys " + _keys + (_legacyFeed ? " | old bodies fed " + _fed + " missing " + _fedMissing : "") + (_strokeFeed ? BodyCount() : ""));
                _fed = 0; _fedMissing = 0;
                ClearOldBodies(c);
                foreach (var d in c.Dummies) if (d != null) Object.Destroy(d);
                c.Dummies.Clear();
                if (c.Backdrop != null) Object.Destroy(c.Backdrop);
                if (c.BackdropMat != null) Object.Destroy(c.BackdropMat);
                if (c.DummyMat != null) Object.Destroy(c.DummyMat);
                foreach (var go in c.HiddenLab) if (go != null) go.SetActive(true);
                c.HiddenLab.Clear();
                _done++; _st = St.Idle;
                return;
            }
            try { _index?.Dispose(); } catch (Exception) { }
            _index = null;
            string summary = j.Name + " | letter " + j.Letter + " | frames " + _frames + " | keys " + _keys + (c.Log != null ? " | cast " + c.Log.Status + " contacts " + c.Log.Contacts.Count + " materials " + c.Log.Materials.Count : " | NO CAST");
            // batch 4 (spread=1): every contact (kind:target:amount) and every material's end, to check each glyph's spread against the executor's facts
            if (_spreadB4 && c.Log != null)
            {
                summary += " | contacts " + string.Join(" ", c.Log.Contacts.Select(x => x.Kind + ":" + x.TargetName + ":" + x.Amount.ToString("0.###", Inv) + (x.BudgetAccepted ? "" : "(refused " + x.BudgetReason + ")")));
                summary += " | materials " + string.Join(" ", c.Log.Materials.Select(m => m.Kind + ":" + m.Initial.ToString("0.###", Inv) + ":" + (m.Alive ? "alive" : m.End.ToString())));
                if (c.Log.Burned.Count > 0) summary += " | burned " + string.Join(" ", c.Log.Burned);
            }
            foreach (var m in Planted) if (m != null && m.Alive) c.Coordinator.Materials.TryEnd(m.Id, Vol2MaterialEnd.Aborted, c.Services.Clock.NowUs);
            Planted.Clear();
            Finished.Add(summary + (j.Template == "AJ" && _ripSlot != null ? " | riposte pierced=" + _ripSlot.HasImpact + " at " + _ripSlot.ImpactT.ToString("0.000", Inv) + " s" : "") + (_strokeFeed ? BodyCount() : ""));
            _rip?.CancelAll(); _ripSlot = null;
            var stage = c.Presenter.Legacy;
            string bridge = stage != null ? " | bridge begun " + stage.Begun + " missing " + stage.Missing + " swings " + stage.SwingsShown + " tip-ink " + stage.InkShown + " stems " + stage.StemsShown
                + " last body " + stage.LastPrefab + " live-before-reset " + stage.LiveCount : "";
            if (stage != null) bridge += " | parts laid " + stage.PartsLaid + " missing " + stage.PartsMissing + " closed " + stage.PartsClosed + " live " + stage.LivePartCount + " | pulses " + stage.PulsesShown + " missing " + stage.PulsesMissing
                + " | last part " + stage.LastPart + " | last pulse " + stage.LastPulse + (stage.RecoveriesShown > 0 ? " | recoveries " + stage.RecoveriesShown : "")
                + " | variants " + stage.PartsVaried + " skipped " + stage.PartsSkipped;
            try { var rec = c.Wiring.Vol2Defence != null ? c.Wiring.Vol2Defence.Recovery : null; if (rec != null && rec.Active) rec.Cancel(); } catch (Exception) { }
            if (_casterOverride != null) { _casterOverride.CasterPositionOverride = null; _casterOverride = null; }
            c.Coordinator.AbortAll(); c.Presenter.Model.Tick(c.Services.Clock.NowUs); c.Presenter.ClearAll();
            if (stage != null) bridge += " live-after-reset " + stage.LiveCount + " parts-after-reset " + stage.LivePartCount + " profile copies " + stage.CopiesMade + "/" + stage.CopiesFreed + " freed";
            if (bridge.Length > 0) Finished[Finished.Count - 1] += bridge;
            ClearOldBodies(c);
            foreach (var d in c.Dummies) { if (d != null) { c.Shapes.Registry.RemoveExtra(d.GetComponentInChildren<EnemyVitals>(true)); Object.Destroy(d); } }
            c.Dummies.Clear();
            if (c.Backdrop != null) Object.Destroy(c.Backdrop);
            if (c.GroundRenderer != null) c.GroundRenderer.sharedMaterial = c.GroundOriginal;
            c.GroundRenderer = null;
            if (c.BackdropMat != null) Object.Destroy(c.BackdropMat);
            if (c.DummyMat != null) Object.Destroy(c.DummyMat);
            foreach (var go in c.HiddenLab) if (go != null) go.SetActive(true);
            c.HiddenLab.Clear();
            c.Shapes.Registry.ExtrasOnly = false;
            _done++;
            _st = St.Idle;
        }

        static void Finish(string why)
        {
            if (!_running) return;
            _running = false;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
            try { _index?.Dispose(); } catch (Exception) { }
            _index = null;
            if (_ctx != null)
            {
                try
                {
                    if (_onDecided != null && _ctx.Services != null) _ctx.Services.Route.Adapter.Decided -= _onDecided;
                    if (_onBegan != null && _ctx.Coordinator != null) _ctx.Coordinator.CastBegan -= _onBegan;
                    if (_legacy)
                    {
                        ClearOldBodies(_ctx);
                        foreach (var d in _ctx.Dummies) if (d != null) Object.DestroyImmediate(d);
                        if (_ctx.Backdrop != null) Object.DestroyImmediate(_ctx.Backdrop);
                        foreach (var go in _ctx.HiddenLab) if (go != null) go.SetActive(true);
                        throw new OperationCanceledException("legacy: no vol.2 scope to restore");
                    }
                    foreach (var d in _ctx.Dummies) if (d != null) Object.DestroyImmediate(d);
                    if (_ctx.Backdrop != null) Object.DestroyImmediate(_ctx.Backdrop);
                    if (_ctx.GroundRenderer != null) _ctx.GroundRenderer.sharedMaterial = _ctx.GroundOriginal;
                    foreach (var go in _ctx.HiddenLab) if (go != null) go.SetActive(true);
                    _ctx.Shapes.Registry.ExtrasOnly = false; _ctx.Shapes.Registry.ClearExtras();
                    _ctx.Coordinator.Reactions.Enabled = true;
                    if (_ctx.Shapes.Presenter != null) _ctx.Shapes.Presenter.Enabled = true;
                    _ctx.Coordinator.AbortAll();
                }
                catch (Exception) { }
            }
            _ctx = null; _onDecided = null; _onBegan = null;
            Time.captureFramerate = _savedCaptureRate;
            // the calibration options are runtime only: put back what was changed
            try { if (_rip != null) _rip.Dispose(!EditorApplication.isPlaying); } catch (Exception) { }
            _rip = null; _ripSlot = null;
            if (_lookSwapped && _lookDriver != null) _lookDriver.Profile = _lookSaved;
            _lookSwapped = false; _lookSaved = null; _realmEye = null;
            // D308-150 V9: the legacy feed listeners leave, the launch switch goes back to what the scope had
            try
            {
                var lw = Object.FindFirstObjectByType<CombatLoopWiring>();
                if (lw != null && _onLegacyPlanned != null) lw.CastPlanned -= _onLegacyPlanned;
                if (lw != null && _onLegacyAccepted != null) lw.CastAccepted -= _onLegacyAccepted;
                if (_apexTuning != null) _apexTuning.LaunchAtApex = _apexSaved;
                var pres2 = Object.FindFirstObjectByType<Vol2VfxPresenter>();
                if (pres2 != null && pres2.Legacy != null && _lasting.HasValue) pres2.Legacy.LastingOverride = _lastingSaved;
            }
            catch (Exception) { }
            _onLegacyPlanned = null; _onLegacyAccepted = null; _apexTuning = null;
            DestroyCamera();
            Queue.Clear(); _job = null; _st = St.Idle;
            try
            {
                string text = "run " + _runId + " | " + why + " | " + _done + "/" + _total + " jobs\n" + string.Join("\n", Finished) + (Problems.Count > 0 ? "\nPROBLEMS\n" + string.Join("\n", Problems) : "");
                File.WriteAllText(Path.Combine(OutRoot, "capture_" + _runId + ".txt"), text, new UTF8Encoding(false));
            }
            catch (Exception) { }
            Debug.Log("[Vol2Vfx] capture finished: " + why + " (" + _done + "/" + _total + ")");
        }
    }
}
