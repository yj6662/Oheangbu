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
    /// Frames go to Art/SpellVFX308/Vol2Vfx/9a/frames/&lt;job&gt;/ (f_NNNNN.jpg 1280x720 every game frame for a video job, key_*.png 1920x1080 near 0.08 s and 0.30 s, index.csv with the SCOPE clock age of each
    /// frame); Tools/Art/SpellVFX308/vol2vfx9a.py turns them into the still sheets and the mp4s. Time.captureFramerate = 60 for the capture: one game frame is exactly 1/60 s.
    /// LAB-SIMULATED and marked in index.csv: the clock jumps over the long waits (10 s residue, 3 s marks / strip, 8 s seed), the follow-up direct hit that works a seed, the forced end reasons.</summary>
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

        /// <summary>stroke20 comparison: the lab in Play WITHOUT a vol.2 request = the Legacy308 route the main game plays (SpellResolver -> the old 120 effects). Capture with legacy=1.</summary>
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

        /// <summary>The output folder under Art/SpellVFX308/Vol2Vfx: 9a_r2 (slice 9a) or the NB20 rounds (run option out=nb20/r1 ...).</summary>
        static string _out = "9a_r2";
        // NB20 calibration (coordinator 2026-10-25): look=main puts the main world's look profile on the lab's LookDriver308 for the run (runtime only, restored at the end);
        // realm=auto makes the look step at a point inside the realm of the soil (적로 for red, 청림 for green) before every render instead of at the lab's own position;
        // p0=nb20 swaps the kept NB20 P0 recipe in for the run; template AJ = the approved 앞잡 one stroke (RiposteStroke308Effect) fired at the first body
        static bool _mainLook, _p0Nb20; static string _realmMode = "lab";
        // stroke20 (D308-150 V7): pitch= the video camera pitch (the in-game shoulder camera is pitch 0 by default; the old captures used 15), rules=1 renders every video frame three times
        // (with / without the VFX / without the enemy body) and writes rules.csv + m_NNNNN.png masks (R = VFX pixels, G = enemy body pixels) for the machine checks, dist=3/6/12 adds P0 jobs
        // with the body at those distances; p0=9a swaps the kept slice 9a P0 back in
        static float _videoPitch = 15f; static bool _rules, _p09a, _legacy; static int _legacyFrames; static Vol2WiringCaster _legacyCaster; static readonly List<float> Dists = new List<float>();
        // D308-150 V9 (legacy bridge): legacy=2 = the Legacy308 route WITH the old glyph bodies. The old route presents a glyph's body through the stroke adapter
        // (BrushStrokeFeedAdapter.SpawnPattern), which needs the drawn letter's strokes - a lab letter raised on the channel has none, so legacy=1 showed the swing,
        // the brush-tip ink and the wards but no projectile / area / guard body. legacy=2 (LAB-ONLY, editor capture) feeds the REAL SpawnPattern at the moment
        // the old route plans the cast (CastPlanned at the apex; CastAccepted for a parry) with one synthetic stroke at the adapter's drawing-surface centre and
        // the pending hit context the wiring computed (target, flight, area plan). apex=0|1 sets the vol.2 TE-S3-6 launch switch for the run (restored at the end).
        static bool _legacyFeed, _wardBand; static bool? _apex; static bool _apexSaved; static Vol2ShapeTuning _apexTuning; static int _fed, _fedMissing;
        static readonly List<EnemyVitals> LegacyTargets = new List<EnemyVitals>(); static LockOn LegacyLock;

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
        static StreamWriter _rulesCsv; static RenderTexture _rtB, _rtC; static Texture2D _texB, _texC, _mask;
        static Oheangbu.App.World.LookDriver308 _lookDriver; static Oheangbu.Data.World.LookProfile308 _lookSaved; static bool _lookSwapped;
        static Vector3? _realmEye;
        static Oheangbu.App.World.RiposteStroke308Effect _rip; static Oheangbu.App.World.RiposteStroke308Effect.Slot _ripSlot; static int _ripFrames;
        const string MainLookProfile = "Assets/_Project/Art/World/Look308/Data/LookProfile308_PBZInkWash.asset";
        const string RiposteProfile = "Assets/_Project/Art/SpellVFX308/RiposteStroke/RiposteStroke308Profile.asset";
        public static string OutRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX308/Vol2Vfx/" + _out));
        static readonly Dictionary<string, string> RowOf = new Dictionary<string, string> { { "A0", "거너머서어" }, { "F0", "고노모소오" }, { "R0", "구누무수우" } };

        sealed class Step { public double At; public string Name; public Func<Ctx, bool> Do; public bool Done; }

        sealed class Job
        {
            public string Template, Variant = "main", Back = "red"; public Body Body; public char Letter; public bool Video, Keys;
            public double MaxAge = 1.2, MinAge = 0.4;
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
            foreach (string t in parts[0].Split(',')) { if (t == "all") templates.AddRange(new[] { "P0", "P1", "P2", "P3", "P4", "P5" }); else if (t == "nb20") templates.AddRange(new[] { "P0", "A0", "F0", "R0" }); else templates.Add(t.Trim()); }
            _out = "9a_r2"; _mainLook = false; _p0Nb20 = false; _realmMode = "lab"; MeasuredGround.Clear(); _videoPitch = 15f; _rules = false; _p09a = false; _legacy = false; Dists.Clear();
            _legacyFeed = false; _apex = null; _fed = 0; _fedMissing = 0; _wardBand = false;
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
                else if (k == "p0") { _p0Nb20 = v.Trim() == "nb20"; _p09a = v.Trim() == "9a"; }
                else if (k == "pitch") _videoPitch = float.Parse(v, Inv);
                else if (k == "rules") _rules = v == "1";
                else if (k == "legacy") { _legacy = v == "1" || v == "2"; _legacyFeed = v == "2"; }
                else if (k == "apex") _apex = v.Trim() == "1";
                else if (k == "wardband") _wardBand = v.Trim() == "1";
                else if (k == "dist") Dists.AddRange(v.Split('/').Select(x => float.Parse(x, Inv)));
                else if (k == "p0" && v.Trim() == "9a") _p09a = true;
                else if (k == "enemy") EnemyPrefab = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_" + v + ".prefab";
            }
            foreach (string t in templates)
            {
                if (!(t.Length == 2 && t[0] == 'P' && t[1] >= '0' && t[1] <= '5') && !RowOf.ContainsKey(t) && t != "AJ") return "REFUSED vfx9a:run: template " + t + " (slice 9a draws P0 - P5, NB20 also A0 F0 R0)";
                var variants = t == "P3" ? new[] { "work", "expire" } : t == "P0" && Dists.Count > 0 ? new[] { "main" }.Concat(Dists.Select(d => "d" + d.ToString("0", Inv))).ToArray() : new[] { "main" };
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
            var j = new Job { Template = template, Variant = variant, Body = body, Back = back, Video = video, Keys = variant != "expire" };
            j.Letter = template == "AJ" ? '\0' : RowOf.TryGetValue(template, out var row) ? row[(int)body] : Rows[verb][(int)body];
            j.Dummies.Add((4.6f, 0f));
            switch (template)
            {
                case "AJ": j.MaxAge = 1.3; j.MinAge = 1.25; break;          // the approved 앞잡 one stroke: reveal .05 - .20, dry from .60, gone 1.20 (profile defaults)
                case "A0":
                    // the bare parry: a body 1.6 m ahead; LAB-SIMULATED: its attack is parried successfully 0.22 s into the window (the model is told what the combat wiring would relay)
                    j.Dummies[0] = (1.6f, 0.25f); j.MaxAge = 0.95; j.MinAge = 0.8;
                    j.Steps.Add(new Step { At = 0.22, Name = "LAB-SIMULATED: a successful parry of the body's attack (ParryResolved Success at its chest)", Do = c => { SimulateParry(c); return true; } });
                    break;
                case "F0": j.Dummies[0] = (3.4f, -0.3f); j.Dummies.Add((4.3f, 1.25f)); j.MaxAge = 0.6; j.MinAge = 0.45; break;
                case "R0": j.Dummies[0] = (1.85f, 0.75f); j.MaxAge = 0.6; j.MinAge = 0.45; break;
                case "P0":
                    if (variant.StartsWith("d", StringComparison.Ordinal)) { j.Dummies[0] = (float.Parse(variant.Substring(1), Inv), 0f); j.MaxAge = 1.0; j.MinAge = 0.75; break; }
                    j.Dummies.Add((8f, 0f)); j.MaxAge = 1.0; j.MinAge = 0.7; break;
                case "P1":
                    j.Dummies[0] = (4.6f, 0f); j.MaxAge = 14.0; j.MinAge = 1.2;
                    j.Steps.Add(new Step { At = 2.0, Name = "JUMP to 0.55 s before the residue expires (LAB-SIMULATED: 10 s wait)", Do = c => JumpBeforeExpiry(c, 0.55) });
                    break;
                case "P2":
                    j.Dummies[0] = (4.6f, 0f); j.Dummies.Add((6.2f, 1.7f)); j.Dummies.Add((5.8f, -1.9f)); j.Dummies.Add((3.6f, 2.3f)); j.MaxAge = 8.0; j.MinAge = 1.2;
                    j.Steps.Add(new Step { At = 0.9, Name = "JUMP to 0.55 s before the marks expire (LAB-SIMULATED: 3 s wait)", Do = c => JumpBeforeExpiry(c, 0.55) });
                    break;
                case "P3":
                    if (variant == "work")
                    {
                        j.MaxAge = 2.3; j.MinAge = 1.8;
                        char follow = Rows[0][(int)body];
                        j.Steps.Add(new Step { At = 1.0, Name = "LAB-SIMULATED: a direct hit of the same body works the seed (" + follow + ")", Do = c => { RaiseLetter(c, follow); return true; } });
                    }
                    else
                    {
                        j.MaxAge = 12.0; j.MinAge = 1.2;
                        j.Steps.Add(new Step { At = 1.0, Name = "JUMP to 0.55 s before the seed expires unused (LAB-SIMULATED: 8 s wait)", Do = c => JumpBeforeExpiry(c, 0.55) });
                    }
                    break;
                case "P4":
                    j.MaxAge = 8.0; j.MinAge = 1.2;
                    j.Steps.Add(new Step { At = 0.8, Name = "JUMP to 0.55 s before the strip ends (LAB-SIMULATED: 3 s wait)", Do = c => JumpBeforeStripEnd(c, 0.55) });
                    break;
                default: j.MaxAge = 1.5; j.MinAge = 1.2; break;
            }
            if (_legacy) { j.Steps.Clear(); j.MaxAge = template == "A0" ? 1.2 : 1.6; j.Variant = _legacyFeed ? "old" : "legacy"; }
            // D308-150 V9: the old visuals live longer than the vol.2 ink looks (windup + flight + impact): both sides of the legacy bridge comparison record the same full
            // length (no early stop), with no lab-simulated parry (the old route only shows a parry's success on a real enemy impact)
            if (template == "P0" || template == "A0" || template == "F0" || template == "R0")
                if (_legacyFeed || (!_p0Nb20 && !_p09a))
                {
                    j.Steps.Clear(); j.MaxAge = template == "A0" ? 1.6 : 2.2; j.MinAge = j.MaxAge;
                    if (!_legacy) j.Variant = _apex == true ? "bridge_apex" : "bridge";
                }
            return j;
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

        static void SimulateParry(Ctx c)
        {
            if (c.Dummies.Count == 0) return;
            Vector3 p = c.Dummies[0].transform.position; p.y = c.Origin.y + 1.15f;
            Vector3 to = p - c.Origin; to.y = 0f;
            p -= to.normalized * 0.55f;                     // the impact meets the brush in front of the body
            c.Presenter.Model.Parried(p, c.Services.Clock.NowUs);
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
            // calibration options (runtime only; Finish puts everything back)
            if (_p0Nb20) p.Model.Recipes.Set(new Vol2Nb20PathRecipe());
            if (_p09a) p.Model.Recipes.Set(new Vol2PathOneHitRecipe());
            // D308-150 V9: apex=0|1 = the TE-S3-6 launch switch for this run (the lab scope's own value is put back by Finish)
            _apexTuning = p.Coordinator.Tuning; _apexSaved = _apexTuning.LaunchAtApex;
            if (_apex.HasValue) _apexTuning.LaunchAtApex = _apex.Value;
            if (p.Legacy != null) p.Legacy.WardOnRingBand = _wardBand;
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
            _rtB = new RenderTexture(VideoW, VideoH, 24); _rtC = new RenderTexture(VideoW, VideoH, 24);
            _texB = new Texture2D(VideoW, VideoH, TextureFormat.RGB24, false); _texC = new Texture2D(VideoW, VideoH, TextureFormat.RGB24, false);
            _mask = new Texture2D(VideoW, VideoH, TextureFormat.RGB24, false);
        }

        static void DestroyCamera()
        {
            if (_cam != null) Object.DestroyImmediate(_cam.gameObject);
            foreach (var rt in new[] { _rtVideo, _rtKey, _rtB, _rtC }) if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            foreach (var tx in new[] { _texVideo, _texKey, _texB, _texC, _mask }) if (tx != null) Object.DestroyImmediate(tx);
            _cam = null; _rtVideo = _rtKey = _rtB = _rtC = null; _texVideo = _texKey = _texB = _texC = _mask = null;
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
            if (!MeasuredGround.Contains(j.Back)) MeasureGround(c, j.Back);
            c.Log = null; _beganLog = null; _lastDecision = null; _note = ""; _lastJumpAge = -1f;
            foreach (var s in _job.Steps) s.Done = false;
            _folder = Path.Combine(OutRoot, "frames", _job.Name);
            if (Directory.Exists(_folder)) foreach (var f in Directory.GetFiles(_folder)) File.Delete(f);
            Directory.CreateDirectory(_folder);
            _index = new StreamWriter(Path.Combine(_folder, "index.csv"), false, new UTF8Encoding(false));
            _index.WriteLine("frame,unityFrame,clockUs,ageSec,video,key,note,template,variant,body,back,letter,parts,version,verts,views");
            try { _rulesCsv?.Dispose(); } catch (Exception) { }
            _rulesCsv = null;
            if (_rules)
            {
                _rulesCsv = new StreamWriter(Path.Combine(_folder, "rules.csv"), false, new UTF8Encoding(false));
                _rulesCsv.WriteLine("frame,ageSec,vfxPx,enemyPx,occludedPx,enemyVisible,contacts,pitch");
            }
            _frames = 0; _keys = 0;
            _guardUntil = EditorApplication.timeSinceStartup + 120.0;
            _settle = 5; _st = St.Settle;
        }

        static void CastNow()
        {
            var c = _ctx;
            if (_legacy) { LegacyRefill(c); RaiseLetter(c, _job.Letter); c.Log = null; _legacyFrames = 0; _st = St.Record; return; }
            c.Shapes.PoolInk?.Refill();
            c.Wiring.Vol2Defence?.ResetForRow();
            _beganLog = null; _lastDecision = null;
            if (_job.Template == "AJ") { BeginRiposte(c); return; }
            RaiseLetter(c, _job.Letter);
            c.Log = _beganLog;
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

        static void Record()
        {
            var c = _ctx; var j = _job;
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
            // one picture per game frame, after the presenter stepped on the scope clock (Vol2VfxPresenter.LastStepUs)
            long stepUs = c.Presenter.LastStepUs;
            double age = (stepUs - c.Log.StartUs) / 1000000.0;
            foreach (var s in j.Steps)
            {
                if (s.Done || age < s.At) continue;
                if (s.Do(c)) { s.Done = true; _note = s.Name; }
            }
            bool keyWindow = j.Keys && ((age >= 0.065 && age <= 0.10) || (age >= 0.165 && age <= 0.20) || (age >= 0.285 && age <= 0.315));
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
            if (video) { Shoot(origin, yaw, _videoPitch, _rtVideo, _texVideo, Path.Combine(_folder, "f_" + _frames.ToString("D5", Inv) + ".jpg"), false); if (_rules && !_legacy) Rules(c, origin, yaw, age); }
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

        /// <summary>stroke20 rule probe: the frame just shot (_texVideo) against the same frame rendered without the VFX (B) and without the VFX and the enemy body (C, the body made
        /// shadows-only so its shadow stays): VFX pixels = A != B, enemy body pixels = B != C, occluded = enemy body pixels the VFX changed in A. Tolerance 12/255 on any channel.</summary>
        static void Rules(Ctx c, Vector3 origin, float yaw, double age)
        {
            var vfx = c.Presenter.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToList();
            var body = new List<(Renderer r, UnityEngine.Rendering.ShadowCastingMode m)>();
            foreach (var d in c.Dummies) if (d != null) foreach (var r in d.GetComponentsInChildren<Renderer>(false)) if (r.enabled) body.Add((r, r.shadowCastingMode));
            try
            {
                foreach (var r in vfx) r.enabled = false;
                Render(origin, yaw, _videoPitch, _rtB, _texB);
                foreach (var b in body) b.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                Render(origin, yaw, _videoPitch, _rtC, _texC);
            }
            finally
            {
                foreach (var r in vfx) if (r != null) r.enabled = true;
                foreach (var b in body) if (b.r != null) b.r.shadowCastingMode = b.m;
            }
            var A = _texVideo.GetPixels32(); var B = _texB.GetPixels32(); var C = _texC.GetPixels32();
            var M = new Color32[A.Length];
            int vfxPx = 0, enemyPx = 0, occ = 0; const int Tol = 12;
            for (int i = 0; i < A.Length; i++)
            {
                bool v = Mathf.Max(Mathf.Abs(A[i].r - B[i].r), Mathf.Max(Mathf.Abs(A[i].g - B[i].g), Mathf.Abs(A[i].b - B[i].b))) > Tol;
                bool e = Mathf.Max(Mathf.Abs(B[i].r - C[i].r), Mathf.Max(Mathf.Abs(B[i].g - C[i].g), Mathf.Abs(B[i].b - C[i].b))) > Tol;
                if (v) vfxPx++; if (e) enemyPx++; if (v && e) occ++;
                M[i] = new Color32(v ? (byte)255 : (byte)0, e ? (byte)255 : (byte)0, 0, 255);
            }
            _mask.SetPixels32(M); _mask.Apply(false);
            File.WriteAllBytes(Path.Combine(_folder, "m_" + _frames.ToString("D5", Inv) + ".png"), _mask.EncodeToPNG());
            int contacts = c.Log != null ? c.Log.Contacts.Count(x => x.BudgetAccepted) : (_ripSlot != null && _ripSlot.HasImpact ? 1 : 0);
            float vis = enemyPx > 0 ? 1f - occ / (float)enemyPx : 1f;
            _rulesCsv?.WriteLine(string.Join(",", _frames.ToString(Inv), age.ToString("0.0000", Inv), vfxPx.ToString(Inv), enemyPx.ToString(Inv), occ.ToString(Inv), vis.ToString("0.0000", Inv), contacts.ToString(Inv), _videoPitch.ToString("0", Inv)));
        }

        static void Render(Vector3 origin, float yawDegrees, float pitchDegrees, RenderTexture rt, Texture2D tex)
        {
            Quaternion rot = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            Vector3 pivot = origin + Vector3.up * 1.7f;
            _cam.transform.SetPositionAndRotation(pivot + rot * new Vector3(0.7f, 0.55f, -2.8f), rot);
            _cam.fieldOfView = 60f;
            _cam.targetTexture = rt;
            if (_realmEye.HasValue && _lookDriver != null) _lookDriver.Refresh(_realmEye.Value);
            _cam.Render();
            var was = RenderTexture.active;
            try { RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); tex.Apply(false); }
            finally { RenderTexture.active = was; }
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

        static string PartInfo(Ctx c)
        {
            if (_legacy) return "legacy,-,-,-";
            if (c.Log == null) return "riposte," + (_ripSlot != null ? _ripSlot.RevealU.ToString("0.00", Inv) + "," + _ripSlot.DryNow.ToString("0.00", Inv) : "-,-") + "," + (_rip != null ? _rip.LookWeight.ToString("0.00", Inv) : "-");
            if (!c.Presenter.Model.TryGet(c.Log.Cast, out var inst)) return "0,0,0," + c.Presenter.ActiveViews;
            return inst.Parts.Count + "," + inst.Version + "," + c.Presenter.LastVertices + "," + c.Presenter.ActiveViews;
        }

        static void Teardown()
        {
            var c = _ctx; var j = _job;
            if (_legacy)
            {
                try { _index?.Dispose(); } catch (Exception) { }
                _index = null;
                Finished.Add(j.Name + " | legacy letter " + j.Letter + " | frames " + _frames + " | keys " + _keys + (_legacyFeed ? " | old bodies fed " + _fed + " missing " + _fedMissing : ""));
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
            try { _rulesCsv?.Dispose(); } catch (Exception) { }
            _rulesCsv = null;
            string summary = j.Name + " | letter " + j.Letter + " | frames " + _frames + " | keys " + _keys + (c.Log != null ? " | cast " + c.Log.Status + " contacts " + c.Log.Contacts.Count + " materials " + c.Log.Materials.Count : " | NO CAST");
            Finished.Add(summary + (j.Template == "AJ" && _ripSlot != null ? " | riposte pierced=" + _ripSlot.HasImpact + " at " + _ripSlot.ImpactT.ToString("0.000", Inv) + " s" : ""));
            _rip?.CancelAll(); _ripSlot = null;
            var stage = c.Presenter.Legacy;
            string bridge = stage != null ? " | bridge begun " + stage.Begun + " missing " + stage.Missing + " swings " + stage.SwingsShown + " tip-ink " + stage.InkShown + " stems " + stage.StemsShown
                + " last body " + stage.LastPrefab + " live-before-reset " + stage.LiveCount : "";
            c.Coordinator.AbortAll(); c.Presenter.Model.Tick(c.Services.Clock.NowUs); c.Presenter.ClearAll();
            if (stage != null) bridge += " live-after-reset " + stage.LiveCount + " profile copies " + stage.CopiesMade + "/" + stage.CopiesFreed + " freed";
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
            if (_p0Nb20 || _p09a) { var pres = Object.FindFirstObjectByType<Vol2VfxPresenter>(); if (pres != null && pres.Model != null) pres.Model.Recipes.Set(new Vol2LegacyPathRecipe()); }
            // D308-150 V9: the legacy feed listeners leave, the launch switch goes back to what the scope had
            try
            {
                var lw = Object.FindFirstObjectByType<CombatLoopWiring>();
                if (lw != null && _onLegacyPlanned != null) lw.CastPlanned -= _onLegacyPlanned;
                if (lw != null && _onLegacyAccepted != null) lw.CastAccepted -= _onLegacyAccepted;
                if (_apexTuning != null) _apexTuning.LaunchAtApex = _apexSaved;
            }
            catch (Exception) { }
            _onLegacyPlanned = null; _onLegacyAccepted = null; _apexTuning = null;
            try { _rulesCsv?.Dispose(); } catch (Exception) { }
            _rulesCsv = null;
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
