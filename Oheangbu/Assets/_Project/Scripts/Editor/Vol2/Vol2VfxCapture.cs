using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
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
                if (c == "status") return Vol2VfxCapture.Status();
                if (c == "stop") return Vol2VfxCapture.Stop("stop command");
                if (c.StartsWith("run:", StringComparison.Ordinal)) return Vol2VfxCapture.Enqueue(c.Substring(4));
                if (c == "build") return Vol2VfxBuild.Run("all");
                return "REFUSED vfx9a: play | run:<templates|all>[:bodies=..][:back=red,green][:video=0|1][:variant=..] | status | stop | build";
            }
            catch (Exception e) { return "REFUSED vfx9a:" + c + ": " + e.GetType().Name + " " + e.Message; }
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

        public static string OutRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX308/Vol2Vfx/9a_r2"));

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
            foreach (string t in parts[0].Split(',')) { if (t == "all") templates.AddRange(new[] { "P0", "P1", "P2", "P3", "P4", "P5" }); else templates.Add(t.Trim()); }
            var bodies = Bodies.ToList(); var backs = new List<string> { "red", "green" }; bool videoOnRed = true; string variantOnly = null;
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) continue;
                string k = parts[i].Substring(0, eq), v = parts[i].Substring(eq + 1);
                if (k == "bodies") bodies = v.Split(',').Select(x => (Body)Enum.Parse(typeof(Body), x.Trim(), true)).ToList();
                else if (k == "back") backs = v.Split(',').Select(x => x.Trim()).ToList();
                else if (k == "video") videoOnRed = v == "1";
                else if (k == "variant") variantOnly = v;
                else if (k == "enemy") EnemyPrefab = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_" + v + ".prefab";
            }
            foreach (string t in templates)
            {
                if (!(t.Length == 2 && t[0] == 'P' && t[1] >= '0' && t[1] <= '5')) return "REFUSED vfx9a:run: template " + t + " (slice 9a draws P0 - P5)";
                var variants = t == "P3" ? new[] { "work", "expire" } : new[] { "main" };
                foreach (string variant in variants)
                {
                    if (variantOnly != null && variant != variantOnly) continue;
                    foreach (string back in backs)
                        foreach (var body in bodies)
                        {
                            var job = MakeJob(t, variant, body, back, videoOnRed && back == backs[0]);
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
            int verb = template[1] - '0';
            var j = new Job { Template = template, Variant = variant, Body = body, Back = back, Video = video, Keys = variant != "expire" };
            j.Letter = Rows[verb][(int)body];
            j.Dummies.Add((4.6f, 0f));
            switch (template)
            {
                case "P0": j.Dummies.Add((8f, 0f)); j.MaxAge = 1.0; j.MinAge = 0.7; break;
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
            _st = St.Idle;
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
            if (_texVideo != null) Object.DestroyImmediate(_texVideo);
            if (_texKey != null) Object.DestroyImmediate(_texKey);
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
        static void Setup()
        {
            var c = _ctx; var j = _job;
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
            _frames = 0; _keys = 0;
            _guardUntil = EditorApplication.timeSinceStartup + 120.0;
            _settle = 5; _st = St.Settle;
        }

        static void CastNow()
        {
            var c = _ctx;
            c.Shapes.PoolInk?.Refill();
            c.Wiring.Vol2Defence?.ResetForRow();
            _beganLog = null; _lastDecision = null;
            RaiseLetter(c, _job.Letter);
            c.Log = _beganLog;
            if (c.Log == null) { Problems.Add(_job.Name + ": the letter " + _job.Letter + " started no cast (" + (_lastDecision != null ? "accepted=" + _lastDecision.Accepted + " reason=" + _lastDecision.Reason : "no decision") + ")"); _st = St.Teardown; return; }
            _st = St.Record;
        }

        static void Record()
        {
            var c = _ctx; var j = _job;
            // one picture per game frame, after the presenter stepped on the scope clock (Vol2VfxPresenter.LastStepUs)
            long stepUs = c.Presenter.LastStepUs;
            double age = (stepUs - c.Log.StartUs) / 1000000.0;
            foreach (var s in j.Steps)
            {
                if (s.Done || age < s.At) continue;
                if (s.Do(c)) { s.Done = true; _note = s.Name; }
            }
            bool keyWindow = j.Keys && ((age >= 0.065 && age <= 0.10) || (age >= 0.285 && age <= 0.315));
            bool lateKey = j.Keys && j.Template == "P1" && j.Body == Body.Wood && age >= 1.70 && age <= 1.74;       // 각 cages its body for 1.5 s: the residue stands after that
            if (j.Video || keyWindow || lateKey) SaveFrame(c, age, stepUs, j.Video, keyWindow || lateKey);
            _note = "";
            bool retired = !c.Presenter.Model.TryGet(c.Log.Cast, out _);
            if (age >= j.MaxAge || (retired && age >= j.MinAge) || EditorApplication.timeSinceStartup > _guardUntil) { _st = St.Teardown; return; }
        }

        static void SaveFrame(Ctx c, double age, long stepUs, bool video, bool key)
        {
            var spec = c.Log.Spec;
            Vector3 origin = spec.Origin, fwd = spec.Forward;
            float yaw = Quaternion.LookRotation(fwd, Vector3.up).eulerAngles.y;
            if (video) { Shoot(origin, yaw, 15f, _rtVideo, _texVideo, Path.Combine(_folder, "f_" + _frames.ToString("D5", Inv) + ".jpg"), false); }
            if (key)
            {
                string tag = age < 0.2 ? "a008" : age > 1.0 ? "x172" : "b030";
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
            if (!c.Presenter.Model.TryGet(c.Log.Cast, out var inst)) return "0,0,0," + c.Presenter.ActiveViews;
            return inst.Parts.Count + "," + inst.Version + "," + c.Presenter.LastVertices + "," + c.Presenter.ActiveViews;
        }

        static void Teardown()
        {
            var c = _ctx; var j = _job;
            try { _index?.Dispose(); } catch (Exception) { }
            _index = null;
            string summary = j.Name + " | letter " + j.Letter + " | frames " + _frames + " | keys " + _keys + (c.Log != null ? " | cast " + c.Log.Status + " contacts " + c.Log.Contacts.Count + " materials " + c.Log.Materials.Count : " | NO CAST");
            Finished.Add(summary);
            c.Coordinator.AbortAll(); c.Presenter.ClearAll();
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
                    if (_onDecided != null) _ctx.Services.Route.Adapter.Decided -= _onDecided;
                    if (_onBegan != null) _ctx.Coordinator.CastBegan -= _onBegan;
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
