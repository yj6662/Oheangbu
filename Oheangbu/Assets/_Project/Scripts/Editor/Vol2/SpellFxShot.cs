using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellFx;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.EditorTools.WorldMacro;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153 (SPEC-SPELL-FX-RESTART-308 section 4): the new one-cast capture of the restart plumbing. Queue (editor idle, then Play):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxShotCommands Run "shot:play"       the spell lab in Play with a Vol2 scope (no lab run)
    ///   ... "shot:run:&lt;letter&gt;:&lt;variant[,variant..]&gt;[:ahead=4.6][:side=0][:frames=180][:back=red|green|none][:look=main][:pitch=15][:swing=1][:stay=1][:shots=N (look-only lab shots flying in, 0 - 8)]
    ///       [:extra=N (0 - 4 more bodies)][:view=shoulder|side][:fake=1][:walk=&lt;start s&gt;[,&lt;m/s&gt;][,toward|away|left|right|&lt;deg&gt;][,&lt;max m&gt;]]"
    ///   ... "shot:status"      ... "shot:stop"
    /// A shot = the real letter raised on the real channel in the lab, seen through a copy of the main camera at the shoulder pose (pivot 1.7 m above the caster, local offset
    /// (0.7, 0.55, -2.8), FOV 60), Time.captureFramerate = 60 (one game frame = 1/60 s). Frames go to Art/SpellVFX308/Restart/shots/&lt;letter&gt;_&lt;variant&gt;/f_00000.jpg
    /// (1280x720) + index.csv (frame,clockUs,ageSec,launchAgeSec,hits,players,note; ages on the SCOPE clock, from the commit / from the launch; then the body columns
    /// contacts,bearX,bearY,bearZ,bearAlong,bearRight,bearDist,bearWalked,bearPose,bearsAlongRight - see BearCols) + summary.txt. A comma list of
    /// variants is shot one after the other in the same Play. Afterwards everything is put back and Play is left (stay=1 keeps it). Never opens a dialog, never saves a scene,
    /// never calls the old Vol2VfxCapture.
    /// walk= (2026-10-30, PLAN-308-MEMBRANE section 5.5, lane F): the lab bodies stand still (brain, agent and every game script are off); with walk= every body walks from
    /// the cast age &lt;start s&gt; (the ageSec column, scope clock from the commit) - toward the caster by default, 1.2 m/s, at most 3 m, never closer than 1 m to the caster -
    /// by moving its transform (what the rules read: Vol2EnemyTarget.Position / Previous, refreshed every Coordinator.Tick). Lab only, no rule is touched. The prefab's own
    /// EnemyRigMotion298 is switched on for such a job (Idle, then Walk by the travel speed; Run above its threshold) - without it the body would slide.</summary>
    public static class SpellFxShotCommands
    {
        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("shot:", StringComparison.Ordinal)) c = c.Substring(5);
            try
            {
                if (c == "play") return SpellFxShot.Play();
                if (c == "status") return SpellFxShot.Status();
                if (c == "stop") return SpellFxShot.Stop("stop command");
                if (c.StartsWith("run:", StringComparison.Ordinal)) return SpellFxShot.Enqueue(c.Substring(4));
                return "REFUSED shot: play | run:<letter>:<variant[,variant]>[:ahead=4.6][:side=0][:frames=180][:back=red|green|none][:look=main][:pitch=15][:swing=1][:stay=1][:extra=N][:view=side][:fake=1][:walk=<start s>[,<m/s>][,toward|away|left|right|<deg>][,<max m>]] | status | stop";
            }
            catch (Exception e) { return "REFUSED shot:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }
    }

    public static class SpellFxShot
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int VideoW = 1280, VideoH = 720;
        // the same ground / enemy / look the old captures used (copied: private constants of Vol2VfxCapture)
        const string JeokroSoil = "Assets/_Project/Art/World/Reworld292/Highlands293/Sections/Materials/Jeokro_Soil.mat", CheongrimSoil = "Assets/_Project/Art/World/Reworld292/Highlands293/Sections/Materials/Cheongrim_Soil.mat";
        const string EnemyPrefab = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_forest_bear.prefab";
        const string MainLookProfile = "Assets/_Project/Art/World/Look308/Data/LookProfile308_PBZInkWash.asset";
        const string DummyName = "SpellFxShotDummy";

        sealed class Job
        {
            public char Letter; public string Variant = "A";
            public float Ahead = 4.6f, Side = 0f, Pitch = 15f, Side3;
            public int Frames = 180, Shots, Extra; public bool Fake;
            public string Back = "red";
            public bool MainLook, Swing, Stay;
            // walk= (lane F): the bodies walk from cast age WalkAt (s) at WalkSpeed (m/s) along WalkDir, at most WalkMax metres each
            public bool Walk; public float WalkAt, WalkSpeed = WalkSpeedDefault, WalkMax = WalkMaxDefault, WalkDeg; public WalkDir Dir = WalkDir.Toward;
            public string Name => Letter + "_" + Variant;
            /// <summary>One copy per variant (a field added above is carried along without touching Enqueue).</summary>
            public Job ForVariant(string variant) { var c = (Job)MemberwiseClone(); c.Variant = variant; return c; }
        }

        enum WalkDir { Toward, Away, Left, Right, Degrees }

        // walk= constants: the default speed, the hard cap of one walk, and how close to the caster a walking body may come (centre to centre, flat)
        const float WalkSpeedDefault = 1.2f, WalkMaxDefault = 3f, WalkStopNear = 1.0f;

        /// <summary>One lab body and (when walk= is on) its walk. Start and WalkedVec let the summary tell what the script moved from what the rules moved (the earth push).</summary>
        sealed class Walker
        {
            public GameObject Go; public EnemyVitals Vitals; public Oheangbu.App.World.EnemyRigMotion298 Rig;
            public Vector3 Start, WalkedVec, Dir;
            public float Walked; public bool Began, Done;
        }

        enum St { Idle, WaitScope, Next, Setup, Settle, Record, Teardown }

        static readonly Queue<Job> Queue = new Queue<Job>();
        static readonly List<string> Finished = new List<string>(), Problems = new List<string>();
        static bool _running, _stay;
        static St _st = St.Idle;
        static Job _job;
        static int _lastFrame = -1, _settle, _frames, _done, _total, _hits;
        static string _runId = "", _folder = "", _lastSummary = "";
        static double _guardUntil;
        static StreamWriter _index;
        static int _savedCaptureRate;
        static readonly Dictionary<char, string> SavedVariants = new Dictionary<char, string>();
        static bool _savedKeepSwing;

        // scope pieces found at WaitScope
        static SpellFxDirector _director; static Vol2ShapeRuntime _shapes; static Vol2CastCoordinator _coord; static CombatLoopWiring _wiring;
        // staging of the current job
        static Camera _cam; static RenderTexture _rt; static Texture2D _tex;
        static readonly List<GameObject> Hidden = new List<GameObject>(), Dummies = new List<GameObject>(), LabShots = new List<GameObject>();
        static GameObject _ground; static Material _groundFallback;
        static bool _extrasOnlyBefore, _presenterWasEnabled; static bool _stagedScope;
        static Oheangbu.App.World.LookDriver308 _lookDriver; static Oheangbu.Data.World.LookProfile308 _lookSaved; static bool _lookSwapped;
        static Vol2CastLog _beganLog; static Action<Vol2CastLog> _onBegan; static Action<Vol2CastDecision> _onDecided; static Vol2CastDecision _decision;
        static int _spawnedBefore; static long _launchUs;
        // the bodies of the current job (always filled, walk= or not: the index.csv body columns also show the earth push), and the cast frame they are measured in
        static readonly List<Walker> Walkers = new List<Walker>();
        static Vector3 _origin, _fwd = Vector3.forward, _right = Vector3.right;
        static long _fakeStartUs;
        static double _firstContactAge, _firstPaidAge; static float _firstContactDist, _firstPaidDist; static string _firstContactNote = "";
        static int _notesSeen;
        static readonly List<string> NoteLog = new List<string>(), PoseOrder = new List<string>();

        public static string OutRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX308/Restart/shots"));

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // commands
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>The spell lab in Play with a Vol2 scope and no lab run (the request file + SpellLab308 play, as vfx9a:play did; copied here so this tool stands alone).</summary>
        public static string Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED shot:play: Play is running or starting";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED shot:play: editor busy (compile / import)";
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) return "REFUSED shot:play: unsaved changes in " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path;
            if (!File.Exists(Path.GetFullPath(Vol2LabRequestFile.LabScenePath))) return "REFUSED shot:play: no lab scene (SpellLab308 build)";
            var book = Vol2RulebookLoader.Load(null, null);
            if (!book.Ok) return "REFUSED shot:play: vol.2 data unavailable: " + book.Error;
            Directory.CreateDirectory(Vol2LabRequestFile.Folder);
            var request = new Vol2LabRequest { mode = "Vol2", run = false, runId = "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), commit = "spellfx-shot", issuedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), scene = Vol2LabRequestFile.LabScenePath };
            File.WriteAllText(Vol2LabRequestFile.RequestPath, JsonUtility.ToJson(request, true), new UTF8Encoding(false));
            string started = SpellLab308.Run("play");
            if (!started.StartsWith("play:", StringComparison.Ordinal))
            {
                try { File.Delete(Vol2LabRequestFile.RequestPath); } catch (Exception) { }
                return "REFUSED shot:play: " + started;
            }
            return "PLAY-REQUESTED (Vol2 scope, no lab run) " + started;
        }

        public static string Enqueue(string spec)
        {
            if (_running) return "REFUSED shot:run: a shot run is active (" + Status() + ")";
            if (!EditorApplication.isPlaying) return "REFUSED shot:run: not in Play (shot:play first, wait until shot:status says playing)";
            var t = (spec ?? "").Split(':');
            if (t.Length < 2 || t[0].Length != 1 || t[1].Length == 0) return "REFUSED shot:run: shot:run:<one letter>:<variant[,variant]>[:key=value...]";
            var proto = new Job { Letter = t[0][0] };
            for (int i = 2; i < t.Length; i++)
            {
                int eq = t[i].IndexOf('=');
                if (eq <= 0) return "REFUSED shot:run: option '" + t[i] + "' is not key=value";
                string k = t[i].Substring(0, eq).Trim().ToLowerInvariant(), v = t[i].Substring(eq + 1).Trim();
                switch (k)
                {
                    case "ahead": if (!float.TryParse(v, NumberStyles.Float, Inv, out proto.Ahead)) return "REFUSED shot:run: ahead=" + v; break;
                    case "side": if (!float.TryParse(v, NumberStyles.Float, Inv, out proto.Side)) return "REFUSED shot:run: side=" + v; break;
                    case "pitch": if (!float.TryParse(v, NumberStyles.Float, Inv, out proto.Pitch)) return "REFUSED shot:run: pitch=" + v; break;
                    case "frames": if (!int.TryParse(v, NumberStyles.Integer, Inv, out proto.Frames) || proto.Frames < 1 || proto.Frames > 1200) return "REFUSED shot:run: frames=" + v + " (1 - 1200)"; break;
                    case "back": if (v != "red" && v != "green" && v != "none") return "REFUSED shot:run: back=" + v + " (red | green | none)"; proto.Back = v; break;
                    case "look": if (v != "main" && v != "lab") return "REFUSED shot:run: look=" + v + " (main | lab)"; proto.MainLook = v == "main"; break;
                    case "swing": proto.Swing = v == "1"; break;
                    case "stay": proto.Stay = v == "1"; break;
                    case "view": if (v != "side" && v != "shoulder") return "REFUSED shot:run: view=" + v + " (shoulder | side)"; proto.Side3 = v == "side" ? 7f : 0f; break;
                    case "fake": proto.Fake = v == "1"; break;                    case "extra": if (!int.TryParse(v, NumberStyles.Integer, Inv, out proto.Extra) || proto.Extra < 0 || proto.Extra > 4) return "REFUSED shot:run: extra=" + v + " (0 - 4)"; break;
                    case "shots": if (!int.TryParse(v, NumberStyles.Integer, Inv, out proto.Shots) || proto.Shots < 0 || proto.Shots > 8) return "REFUSED shot:run: shots=" + v + " (0 - 8)"; break;
                    case "walk": { string bad = ParseWalk(v, proto); if (bad != null) return "REFUSED shot:run: walk=" + v + " (" + bad + ")"; break; }
                    default: return "REFUSED shot:run: unknown option " + k;
                }
            }
            Queue.Clear(); Finished.Clear(); Problems.Clear(); SavedVariants.Clear();
            foreach (string variant in t[1].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
                Queue.Enqueue(proto.ForVariant(variant));
            _total = Queue.Count; _done = 0; _stay = proto.Stay;
            _runId = DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
            Directory.CreateDirectory(OutRoot);
            _running = true; _st = St.WaitScope; _lastFrame = -1; _settle = 0;
            _savedCaptureRate = Time.captureFramerate; _savedKeepSwing = SpellFxSilencer.KeepBrushSwing;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += OnReload;
            return "queued " + _total + " shot(s) " + proto.Letter + ":" + t[1] + (proto.Walk ? " | walk from age " + proto.WalkAt.ToString("0.##", Inv) + " s at " + proto.WalkSpeed.ToString("0.##", Inv) + " m/s " + proto.Dir + (proto.Dir == WalkDir.Degrees ? " " + proto.WalkDeg.ToString("0.#", Inv) : "") + " max " + proto.WalkMax.ToString("0.##", Inv) + " m" : "") + " | " + OutRoot;
        }

        /// <summary>walk=&lt;start s&gt;[,&lt;m/s 0.1 - 4&gt;][,toward|away|left|right|&lt;degrees&gt;][,&lt;max metres 0.1 - 3&gt;]. Returns null when fine, else what is wrong.
        /// toward / away: along the line to / from the caster (each body its own); left / right: the caster's own sides; a number: a heading in degrees from the caster's forward
        /// (0 = along the cast, 90 = to the caster's right, 180 = back toward the caster for a body that stands ahead). Empty slots keep their default.</summary>
        static string ParseWalk(string v, Job job)
        {
            var w = v.Split(',');
            if (w.Length > 4) return "at most four values";
            if (!float.TryParse(w[0].Trim(), NumberStyles.Float, Inv, out float at) || at < 0f || at > 120f) return "start second 0 - 120";
            float speed = WalkSpeedDefault, max = WalkMaxDefault, deg = 0f; var dir = WalkDir.Toward;
            if (w.Length > 1 && w[1].Trim().Length > 0 && (!float.TryParse(w[1].Trim(), NumberStyles.Float, Inv, out speed) || speed < 0.1f || speed > 4f)) return "speed 0.1 - 4 m/s";
            if (w.Length > 2 && w[2].Trim().Length > 0)
            {
                string d = w[2].Trim().ToLowerInvariant();
                if (d == "toward" || d == "in") dir = WalkDir.Toward;
                else if (d == "away" || d == "out") dir = WalkDir.Away;
                else if (d == "left") dir = WalkDir.Left;
                else if (d == "right") dir = WalkDir.Right;
                else if (float.TryParse(d, NumberStyles.Float, Inv, out deg)) dir = WalkDir.Degrees;
                else return "direction toward | away | left | right | degrees";
            }
            if (w.Length > 3 && w[3].Trim().Length > 0 && (!float.TryParse(w[3].Trim(), NumberStyles.Float, Inv, out max) || max < 0.1f || max > WalkMaxDefault)) return "max distance 0.1 - 3 m";
            job.Walk = true; job.WalkAt = at; job.WalkSpeed = speed; job.Dir = dir; job.WalkDeg = deg; job.WalkMax = max;
            return null;
        }

        public static string Status()
        {
            string s = "running=" + _running + " state=" + _st + " playing=" + EditorApplication.isPlaying + " jobs " + _done + "/" + _total + (_job != null && _running ? " now " + _job.Name + " frame " + _frames + "/" + _job.Frames : "");
            if (_lastSummary.Length > 0) s += "\nlast run: " + _lastSummary;
            if (Problems.Count > 0) s += "\nPROBLEMS\n" + string.Join("\n", Problems);
            return s;
        }

        public static string Stop(string why)
        {
            if (!_running) return "shot: not running";
            Finish(why);
            return "shot stopped (" + why + ")";
        }

        static void OnPlayMode(PlayModeStateChange s) { if (s == PlayModeStateChange.ExitingPlayMode) Finish("Play ended"); }
        static void OnReload() { Finish("script reload"); }

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
                    case St.WaitScope: WaitScope(); break;
                    case St.Next: NextJob(); break;
                    case St.Setup: Setup(); break;
                    case St.Settle: if (--_settle <= 0) CastNow(); break;
                    case St.Record: Record(); break;
                    case St.Teardown: Teardown(); break;
                }
            }
            catch (Exception e) { Problems.Add("EXCEPTION " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); Finish("exception"); }
        }

        static void WaitScope()
        {
            _director = Object.FindFirstObjectByType<SpellFxDirector>();
            _wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
            if (_director == null || _director.Coordinator == null || _wiring == null || _wiring.LetterChannel == null || _wiring.SummonPlayer == null || Camera.main == null)
            {
                if (++_settle > 900) { Problems.Add("no SpellFxDirector / wiring / main camera after 900 frames (is this Play the lab with a Vol2 scope, and does the build contain the director?)"); Finish("no scope"); }
                return;
            }
            _coord = _director.Coordinator; _shapes = _coord.Services.Shapes;
            if (_shapes == null) { Problems.Add("the scope has no Vol2ShapeRuntime"); Finish("no runtime"); return; }
            _settle = 0;
            MakeCamera(Camera.main);
            Time.captureFramerate = 60;
            _lookDriver = Object.FindFirstObjectByType<Oheangbu.App.World.LookDriver308>();
            _st = St.Next;
        }

        static void NextJob()
        {
            if (Queue.Count == 0) { Finish("all shots done"); return; }
            _job = Queue.Dequeue();
            _st = St.Setup;
        }

        static void Setup()
        {
            var j = _job;
            _shapes.PoolInk?.Refill();
            _wiring.Vol2Defence?.ResetForRow();
            _coord.AbortAll();
            _director.ClearAll();
            _shapes.Vfx?.ClearAll();          // the old looks of the previous shot (a variant with no prefab shows the old look) must not linger into these frames
            // only the shot's body is a target; the debug lines stay off (they would draw over the look), both put back at teardown
            var reg = _shapes.Registry;
            _extrasOnlyBefore = reg.ExtrasOnly; reg.ExtrasOnly = true; reg.ClearExtras();
            _presenterWasEnabled = _shapes.Presenter != null && _shapes.Presenter.Enabled;
            if (_shapes.Presenter != null) _shapes.Presenter.Enabled = false;
            _stagedScope = true;
            Hidden.Clear();
            foreach (var v in Object.FindObjectsByType<EnemyVitals>(FindObjectsSortMode.None))
                if (v.gameObject.activeSelf && !v.name.StartsWith(DummyName, StringComparison.Ordinal)) { Hidden.Add(v.gameObject); v.gameObject.SetActive(false); }
            var posts = GameObject.Find("Lab_DistancePosts");
            if (posts != null && posts.activeSelf) { posts.SetActive(false); Hidden.Add(posts); }
            Vector3 origin = _shapes.Caster.Position, fwd = FlatUnit(_shapes.Caster.AimDirection);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            _origin = origin; _fwd = fwd; _right = right;
            Walkers.Clear(); NoteLog.Clear(); PoseOrder.Clear(); _notesSeen = 0;
            _firstContactAge = _firstPaidAge = -1.0; _firstContactDist = _firstPaidDist = 0f; _firstContactNote = "";
            // the real ground look (the realm's own soil material on a flat quad) and a real enemy prefab as the body; never saved into a scene, destroyed at teardown
            if (j.Back != "none")
            {
                string soil = j.Back == "red" ? JeokroSoil : CheongrimSoil;
                var soilMat = AssetDatabase.LoadAssetAtPath<Material>(soil);
                if (soilMat == null)
                {
                    Problems.Add("ground material not found: " + soil + " (a flat colour stands in)");
                    soilMat = _groundFallback = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = j.Back == "red" ? new Color(0.45f, 0.2f, 0.15f) : new Color(0.1f, 0.25f, 0.12f) };
                }
                _ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _ground.name = "SpellFxShotGround"; Object.DestroyImmediate(_ground.GetComponent<Collider>());
                _ground.transform.SetPositionAndRotation(origin + Vector3.up * 0.012f + fwd * 30f, Quaternion.Euler(90f, 0f, 0f));
                _ground.transform.localScale = new Vector3(240f, 240f, 1f);
                _ground.GetComponent<MeshRenderer>().sharedMaterial = soilMat;
            }
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            Vector3 main = origin + fwd * j.Ahead + right * j.Side;
            // extra=N (2026-10-30, 간 · 번진다): N more bodies round the first, about 2.6 m off to the sides and a little behind (inside the spread's 3 m)
            for (int e = 0; e <= j.Extra; e++)
            {
            Vector3 at = e == 0 ? main : main + right * ((e % 2 == 1 ? 1f : -1f) * 2.6f * ((e + 1) / 2)) + fwd * (0.9f * ((e + 1) / 2));
            GameObject go; EnemyVitals vitals;
            if (enemyPrefab != null)
            {
                // built under an inactive parent so the enemy's own brain never wakes; every game script but EnemyVitals is switched off, the agent too
                var holder = new GameObject("SpellFxShotHolder"); holder.SetActive(false);
                go = Object.Instantiate(enemyPrefab, holder.transform);
                go.name = DummyName;
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && !(mb is EnemyVitals) && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("Oheangbu", StringComparison.Ordinal)) mb.enabled = false;
                foreach (var agent in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
                go.transform.SetParent(null, false);
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-fwd, Vector3.up));
                Object.Destroy(holder);
                go.SetActive(true);
                vitals = go.GetComponentInChildren<EnemyVitals>(true) ?? go.AddComponent<EnemyVitals>();
            }
            else
            {
                Problems.Add("enemy prefab not found: " + EnemyPrefab + " (a capsule stands in)");
                go = new GameObject(DummyName);
                go.transform.position = at;
                var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.4f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f);
                vitals = go.AddComponent<EnemyVitals>();
            }
            reg.AddExtra(vitals);
            Dummies.Add(go);
            // the body is also a Walker: its start is kept for the index.csv body columns / summary; walk= switches the prefab's rig on so the walk is not a slide
            Walkers.Add(new Walker { Go = go, Vitals = vitals, Start = go.transform.position, Rig = j.Walk && enemyPrefab != null ? EnableRig(go, e == 0) : null });
            }
            Physics.SyncTransforms();
            // look=main: the main world's look profile on the lab's LookDriver308 for this run (runtime only, put back at teardown)
            if (j.MainLook && !_lookSwapped)
            {
                var prof = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.LookProfile308>(MainLookProfile);
                if (_lookDriver == null || prof == null) Problems.Add("look=main: " + (_lookDriver == null ? "no LookDriver308 in the lab" : "no " + MainLookProfile));
                else { _lookSaved = _lookDriver.Profile; _lookDriver.Profile = prof; _lookSwapped = true; }
            }
            SpellFxSilencer.KeepBrushSwing = j.Swing;
            _folder = Path.Combine(OutRoot, j.Name);
            if (Directory.Exists(_folder)) foreach (var f in Directory.GetFiles(_folder)) File.Delete(f);
            Directory.CreateDirectory(_folder);
            _index = new StreamWriter(Path.Combine(_folder, "index.csv"), false, new UTF8Encoding(false));
            _index.WriteLine("frame,clockUs,ageSec,launchAgeSec,hits,players,note," + BearHeader);
            _frames = 0; _hits = 0; _beganLog = null; _decision = null; _launchUs = 0;
            _guardUntil = EditorApplication.timeSinceStartup + 60.0 + j.Frames * 0.5;
            _settle = 5; _st = St.Settle;          // the previous shot's last frames die out before the letter
        }

        static void CastNow()
        {
            var j = _job;
            _shapes.PoolInk?.Refill();
            _wiring.Vol2Defence?.ResetForRow();
            if (!SavedVariants.ContainsKey(j.Letter)) SavedVariants[j.Letter] = SpellFxSwitch.Get(j.Letter);
            SpellFxSwitch.Set(j.Letter, j.Variant);
            _spawnedBefore = _director.Spawned;
            _coord.CastBegan += _onBegan = l => { if (_beganLog == null) _beganLog = l; };
            _coord.Services.Route.Adapter.Decided += _onDecided = d => _decision = d;
            // fake=1: the letter is never raised (the old heal look answered the letter even with no cast) - the prefab alone
            if (j.Fake) { if (StartFake(j)) { _st = St.Record; return; } Problems.Add(j.Name + ": fake=1 but no catalogue prefab"); _st = St.Teardown; return; }
            try { _wiring.LetterChannel.Raise(new DrawnLetter(j.Letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); }
            catch (Exception e) { Problems.Add(j.Name + ": raising the letter: " + e.Message); }
            if (_beganLog == null && j.Fake && StartFake(j)) { _st = St.Record; return; }
            if (_beganLog == null)
            {
                Problems.Add(j.Name + ": the letter started no cast (" + (_decision != null ? "accepted=" + _decision.Accepted + " reason=" + _decision.Reason : "no decision") + ")");
                _st = St.Teardown; return;
            }
            _launchUs = Math.Max(_beganLog.StartUs, _beganLog.Emitter != null ? _beganLog.Emitter.StartUs : _beganLog.StartUs);
            // shots=N (2026-10-29, look only): N lab shots fly in from the enemy side at the caster's chest, one every 0.3 s from 0.35 s after the cast - nothing
            // to any rule (SpellFxLabShot); a presentation part may turn them to ink
            for (int k = 0; k < j.Shots; k++)
            {
                Vector3 o = _shapes.Caster.Position, f = FlatUnit(_beganLog.Spec.Forward), r = Vector3.Cross(Vector3.up, f);
                float ang = (j.Shots == 1 ? 0f : Mathf.Lerp(-38f, 38f, k / (float)(j.Shots - 1))) * Mathf.Deg2Rad;
                Vector3 dir = f * Mathf.Cos(ang) + r * Mathf.Sin(ang);
                Vector3 from = o + dir * 7.5f + Vector3.up * (1.1f + 0.35f * (k % 2)), to = o + Vector3.up * 1.15f;
                LabShots.Add(Oheangbu.App.SpellFx.SpellFxLabShot.Create(from, to, 8.5f, 0.35f + 0.3f * k).gameObject);
            }
            if (_director.Spawned == _spawnedBefore) Problems.Add(j.Name + ": no SpellFx prefab was built (no catalogue asset, or no entry for " + j.Letter + "/" + j.Variant + " with a SpellFxPlayer): the OLD look is what these frames show");
            _st = St.Record;
        }

        // fake=1 (2026-10-30, 걱: a self-heal the lab's rules refuse to start - 급소창 / 교전 conditions): the LOOK ONLY - the catalogue prefab is built and
        // driven here on the scope clock with a made-up cast at the caster (no rule ran; the frames say so in their index note "FAKE")
        static GameObject _fakeGo; static SpellFxPlayer _fakePlayer; static Vector3 _fakeOrigin, _fakeFwd;

        static bool StartFake(Job j)
        {
            var cat = SpellFxCatalog.Load();
            var prefab = cat != null ? cat.Find(j.Letter, j.Variant) : null;
            if (prefab == null) return false;
            long now = _coord.Services.Clock.NowUs;
            _fakeStartUs = now;
            _fakeOrigin = _shapes.Caster.Position; _fakeFwd = FlatUnit(_shapes.Caster.AimDirection);
            var cast = new SpellFxCast { CastId = -7, Letter = j.Letter, Variant = j.Variant, Origin = _fakeOrigin, Forward = _fakeFwd, Right = Vector3.Cross(Vector3.up, _fakeFwd), StartUs = now, LaunchUs = now + 300000, Clock = _coord.Services.Clock };
            _fakeGo = Object.Instantiate(prefab);
            _fakePlayer = _fakeGo.GetComponent<SpellFxPlayer>();
            if (_fakePlayer == null) { Object.Destroy(_fakeGo); _fakeGo = null; return false; }
            _fakePlayer.Begin(cast);
            _launchUs = cast.LaunchUs;
            Problems.Add(j.Name + ": FAKE - the rules started no cast; the prefab is driven alone (look only)");
            return true;
        }

        static void Record()
        {
            var j = _job;
            long now = _coord.Services.Clock.NowUs;
            if (_fakePlayer != null)
            {
                _fakePlayer.Tick(now);
                StepWalkers((now - _fakeStartUs) / 1000000.0);
                Shoot(_fakeOrigin, Quaternion.LookRotation(_fakeFwd, Vector3.up).eulerAngles.y, j.Pitch, Path.Combine(_folder, "f_" + _frames.ToString("D5", Inv) + ".jpg"));
                _index.WriteLine(string.Join(",", _frames.ToString(Inv), now.ToString(Inv), "0", ((now - _launchUs) / 1000000.0).ToString("0.0000", Inv), "0", "1", "FAKE") + "," + string.Join(",", BearCols()));
                _frames++;
                if (_frames >= j.Frames || EditorApplication.timeSinceStartup > _guardUntil) _st = St.Teardown;
                return;
            }
            var player = _director.PlayerOf(_beganLog.Cast.Value);
            double age = (now - _beganLog.StartUs) / 1000000.0, launchAge = (now - _launchUs) / 1000000.0;
            StepWalkers(age);      // the bodies move first: this frame's picture, the rules' next Coordinator.Tick and the row below all see the new place
            _hits = _beganLog.Contacts.Count(c => c.BudgetAccepted);
            WatchRules(age);
            string note = player != null ? "fx " + player.name : "no fx";
            Shoot(_beganLog.Spec.Origin, Quaternion.LookRotation(_beganLog.Spec.Forward, Vector3.up).eulerAngles.y, j.Pitch, Path.Combine(_folder, "f_" + _frames.ToString("D5", Inv) + ".jpg"));
            _index.WriteLine(string.Join(",", _frames.ToString(Inv), now.ToString(Inv), age.ToString("0.0000", Inv), launchAge.ToString("0.0000", Inv), _hits.ToString(Inv), _director.ActiveCount.ToString(Inv), note.Replace(',', ';')) + "," + string.Join(",", BearCols()));
            _frames++;
            if (_frames >= j.Frames || EditorApplication.timeSinceStartup > _guardUntil) _st = St.Teardown;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // the bodies: walk= and the index.csv body columns
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        const string BearHeader = "contacts,bearX,bearY,bearZ,bearAlong,bearRight,bearDist,bearWalked,bearPose,bearsAlongRight";

        static string F(float v) => v.ToString("0.000", Inv);
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The prefab's own EnemyRigMotion298 on for a walk= job (Setup switched every game script off): Idle while the body stands, Walk by the travel speed
        /// (the rig reads the transform's travel per LateUpdate), Run above its threshold. Null with a note when the body has none / it is not filled in: then it slides.</summary>
        static Oheangbu.App.World.EnemyRigMotion298 EnableRig(GameObject go, bool report)
        {
            try
            {
                var rig = go.GetComponentInChildren<Oheangbu.App.World.EnemyRigMotion298>(true);
                if (rig == null) { if (report) Problems.Add("NOTE walk=: the body has no EnemyRigMotion298 - it slides without a walk animation"); return null; }
                if (!rig.IsConfigured) { if (report) Problems.Add("NOTE walk=: its EnemyRigMotion298 is not filled in (a clip or reference missing) - it slides without a walk animation"); return null; }
                rig.enabled = true;
                return rig;
            }
            catch (Exception e) { if (report) Problems.Add("NOTE walk=: switching the rig on failed (" + e.GetType().Name + " " + e.Message + ") - it slides"); return null; }
        }

        static Vector3 WalkHeading(Job j, Vector3 pos)
        {
            Vector3 toCaster = Flat(_origin - pos);
            bool have = toCaster.sqrMagnitude > 1e-6f;
            switch (j.Dir)
            {
                case WalkDir.Toward: return have ? toCaster.normalized : Vector3.zero;
                case WalkDir.Away: return have ? -toCaster.normalized : Vector3.zero;
                case WalkDir.Left: return -_right;
                case WalkDir.Right: return _right;
                default: return Quaternion.AngleAxis(j.WalkDeg, Vector3.up) * _fwd;
            }
        }

        /// <summary>walk=: from cast age WalkAt every body walks by moving its root transform a little each game frame (added to whatever else moved it - the earth push is the
        /// same kind of transform step - never set from a stored path). The rules read exactly this transform (Vol2EnemyTarget.Position / Previous on every Coordinator.Tick).
        /// Stops at the job's max distance, when the next step would end closer than WalkStopNear to the caster, or when the body dies. The heading is fixed when its walk begins.</summary>
        static void StepWalkers(double age)
        {
            var j = _job;
            if (j != null && j.Walk && age >= j.WalkAt)
            {
                float dt = Time.deltaTime; if (!(dt > 0f) || dt > 0.1f) dt = 1f / 60f;
                bool moved = false;
                foreach (var w in Walkers)
                {
                    if (w.Go == null || w.Done) continue;
                    if (w.Vitals != null && !w.Vitals.IsAlive) { w.Done = true; continue; }
                    Transform tr = w.Go.transform;
                    Vector3 pos = tr.position;
                    if (!w.Began)
                    {
                        w.Began = true;
                        w.Dir = WalkHeading(j, pos);
                        if (w.Dir.sqrMagnitude < 1e-6f) { w.Done = true; continue; }
                    }
                    float step = Mathf.Min(j.WalkSpeed * dt, j.WalkMax - w.Walked);
                    if (step <= 1e-5f) { w.Done = true; continue; }
                    Vector3 next = pos + w.Dir * step;
                    float dNow = Flat(pos - _origin).magnitude, dNext = Flat(next - _origin).magnitude;
                    if (dNext < WalkStopNear && dNext < dNow) { w.Done = true; continue; }
                    tr.position = next;
                    tr.rotation = Quaternion.RotateTowards(tr.rotation, Quaternion.LookRotation(w.Dir, Vector3.up), 540f * dt);
                    w.Walked += step; w.WalkedVec += w.Dir * step; moved = true;
                    if (w.Walked >= j.WalkMax - 1e-4f) w.Done = true;
                }
                if (moved) Physics.SyncTransforms();      // colliders follow now (the hold-cell surface code and any physics query read them)
            }
            var w0 = Walkers.Count > 0 ? Walkers[0] : null;
            if (w0 != null && w0.Rig != null && w0.Rig.enabled) { string p = w0.Rig.CurrentPose; if (!string.IsNullOrEmpty(p) && !PoseOrder.Contains(p)) PoseOrder.Add(p); }
        }

        /// <summary>What the rules did, as the first body saw it: the cast age and the first body's distance to the caster at the first contact (any, then the first paid one), and every
        /// new line of the cast's Notes (Activate / ActivateRefused ... with the age) - the numbers that say the rules judged the walk.</summary>
        static void WatchRules(double age)
        {
            var log = _beganLog;
            if (log == null) return;
            float dist0 = Walkers.Count > 0 && Walkers[0].Go != null ? Flat(Walkers[0].Go.transform.position - _origin).magnitude : 0f;
            if (_firstContactAge < 0.0 && log.Contacts.Count > 0)
            {
                var c = log.Contacts[0];
                _firstContactAge = age; _firstContactDist = dist0;
                _firstContactNote = c.Kind + (c.BudgetAccepted ? " paid" : " refused " + c.BudgetReason) + " target " + c.TargetName;
            }
            if (_firstPaidAge < 0.0 && _hits > 0) { _firstPaidAge = age; _firstPaidDist = dist0; }
            for (; _notesSeen < log.Notes.Count; _notesSeen++)
            {
                if (NoteLog.Count >= 12) continue;
                string n = log.Notes[_notesSeen] ?? "";
                NoteLog.Add(age.ToString("0.00", Inv) + " s " + (n.Length > 80 ? n.Substring(0, 80) : n));
            }
        }

        /// <summary>The index.csv columns after "note": contacts (all of the cast log), the first body's world position and its place in the cast frame (along = metres ahead of the
        /// caster, right = metres to the caster's right, dist = flat metres from the caster), bearWalked (metres the script walked it: the earth push is NOT in it, so
        /// bearAlong minus the start minus the walk = the push), bearPose (rig pose, "-" without one), and every body as along|right joined by ';' (the first one first).</summary>
        static string[] BearCols()
        {
            var c = new string[10];
            c[0] = (_beganLog != null ? _beganLog.Contacts.Count : 0).ToString(Inv);
            var w = Walkers.Count > 0 ? Walkers[0] : null;
            if (w == null || w.Go == null) { for (int i = 1; i < c.Length; i++) c[i] = ""; return c; }
            Vector3 p = w.Go.transform.position, d = Flat(p - _origin);
            c[1] = F(p.x); c[2] = F(p.y); c[3] = F(p.z);
            c[4] = F(Vector3.Dot(d, _fwd)); c[5] = F(Vector3.Dot(d, _right)); c[6] = F(d.magnitude); c[7] = F(w.Walked);
            c[8] = w.Rig != null && w.Rig.enabled && !string.IsNullOrEmpty(w.Rig.CurrentPose) ? w.Rig.CurrentPose : "-";
            var all = new StringBuilder();
            for (int i = 0; i < Walkers.Count; i++)
            {
                if (i > 0) all.Append(';');
                var o = Walkers[i];
                if (o.Go == null) { all.Append("gone"); continue; }
                Vector3 q = Flat(o.Go.transform.position - _origin);
                all.Append(F(Vector3.Dot(q, _fwd))).Append('|').Append(F(Vector3.Dot(q, _right)));
            }
            c[9] = all.ToString();
            return c;
        }

        /// <summary>The tail of summary.txt: where every body started and ended in the cast frame (along/right), how far the script walked it and how far something else moved it
        /// ("other" = the earth push), the rig poses seen, and the rules' answer (first contact, first paid hit, the cast's Notes with ages).</summary>
        static string BearSummary()
        {
            var j = _job;
            if (j == null || Walkers.Count == 0) return "";
            var sb = new StringBuilder(" | bodies " + Walkers.Count + " (along/right m from the caster):");
            for (int i = 0; i < Walkers.Count; i++)
            {
                var w = Walkers[i];
                if (w.Go == null) { sb.Append(" [" + i + " gone]"); continue; }
                Vector3 s = Flat(w.Start - _origin), e = Flat(w.Go.transform.position - _origin);
                Vector3 other = Flat((w.Go.transform.position - w.Start) - w.WalkedVec);
                sb.Append(" [" + i + " " + F(Vector3.Dot(s, _fwd)) + "/" + F(Vector3.Dot(s, _right)) + " -> " + F(Vector3.Dot(e, _fwd)) + "/" + F(Vector3.Dot(e, _right))
                    + " dist " + F(e.magnitude) + " walked " + F(w.Walked) + " other " + F(other.magnitude) + "]");
            }
            if (j.Walk)
                sb.Append(" | walk age>=" + j.WalkAt.ToString("0.##", Inv) + " s " + j.WalkSpeed.ToString("0.##", Inv) + " m/s " + j.Dir + (j.Dir == WalkDir.Degrees ? " " + j.WalkDeg.ToString("0.#", Inv) : "") + " max " + j.WalkMax.ToString("0.##", Inv) + " m"
                    + " | rig " + (Walkers[0].Rig != null ? "EnemyRigMotion298 poses " + string.Join(">", PoseOrder) : "none (slides)"));
            if (_beganLog != null)
            {
                sb.Append(" | rules: ");
                if (_firstContactAge >= 0.0) sb.Append("first contact age " + _firstContactAge.ToString("0.00", Inv) + " s with body 0 at " + F(_firstContactDist) + " m (" + _firstContactNote + ")");
                else sb.Append("NO contact in the shot");
                if (_firstPaidAge >= 0.0) sb.Append("; first paid hit age " + _firstPaidAge.ToString("0.00", Inv) + " s, body 0 at " + F(_firstPaidDist) + " m");
                if (NoteLog.Count > 0) sb.Append("; notes [" + string.Join(" | ", NoteLog) + "]");
            }
            return sb.ToString();
        }

        static void Teardown()
        {
            var j = _job;
            try { _index?.Dispose(); } catch (Exception) { }
            _index = null;
            if (_fakeGo != null) { Object.Destroy(_fakeGo); _fakeGo = null; _fakePlayer = null; }
            var player = _beganLog != null ? _director.PlayerOf(_beganLog.Cast.Value) : null;
            string summary = j.Name + " | frames " + _frames + (_beganLog != null ? " | cast " + _beganLog.Status + " contacts " + _beganLog.Contacts.Count + " paid " + _beganLog.PaidContacts + " materials " + _beganLog.Materials.Count : " | NO CAST")
                + " | director spawned " + _director.Spawned + " skipped " + _director.Skipped + " retired " + _director.Retired + " hits " + _director.HitsRelayed + " | " + _director.LastNote
                + (player != null ? " | player begin " + player.BeginCount + " launch " + player.LaunchCount + " ticks " + player.TickCount + " hit " + player.HitCount + " material " + player.MaterialCount + " reaction " + player.ReactionCount + " end " + player.EndCount : " | player gone");
            summary += BearSummary();
            Finished.Add(summary);
            try { File.WriteAllText(Path.Combine(_folder, "summary.txt"), summary, new UTF8Encoding(false)); } catch (Exception) { }
            RestoreStage();
            _done++;
            _st = St.Next;
        }

        /// <summary>Puts back everything Setup / CastNow changed for one job (also the way out of an aborted run).</summary>
        static void RestoreStage()
        {
            try
            {
                if (_onBegan != null && _coord != null) _coord.CastBegan -= _onBegan;
                if (_onDecided != null && _coord != null) _coord.Services.Route.Adapter.Decided -= _onDecided;
            }
            catch (Exception) { }
            _onBegan = null; _onDecided = null;
            try
            {
                if (_stagedScope)
                {
                    _coord?.AbortAll();
                    _director?.ClearAll();
                    _shapes?.Vfx?.ClearAll();
                    foreach (var d in Dummies) if (d != null) { _shapes?.Registry.RemoveExtra(d.GetComponentInChildren<EnemyVitals>(true)); Object.DestroyImmediate(d); }
                    _shapes.Registry.ExtrasOnly = _extrasOnlyBefore; _shapes.Registry.ClearExtras();
                    if (_shapes.Presenter != null) _shapes.Presenter.Enabled = _presenterWasEnabled;
                }
            }
            catch (Exception) { }
            Dummies.Clear(); Walkers.Clear();
            foreach (var s in LabShots) if (s != null) Object.DestroyImmediate(s);
            LabShots.Clear();
            _stagedScope = false;
            if (_ground != null) Object.DestroyImmediate(_ground);
            _ground = null;
            if (_groundFallback != null) Object.DestroyImmediate(_groundFallback);
            _groundFallback = null;
            foreach (var go in Hidden) if (go != null) go.SetActive(true);
            Hidden.Clear();
            if (_lookSwapped && _lookDriver != null) _lookDriver.Profile = _lookSaved;
            _lookSwapped = false; _lookSaved = null;
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
            RestoreStage();
            foreach (var kv in SavedVariants) SpellFxSwitch.Set(kv.Key, kv.Value);
            SavedVariants.Clear();
            SpellFxSilencer.KeepBrushSwing = _savedKeepSwing;
            Time.captureFramerate = _savedCaptureRate;
            DestroyCamera();
            Queue.Clear(); _job = null; _st = St.Idle; _director = null; _shapes = null; _coord = null; _wiring = null;
            _lastSummary = _runId + " | " + why + " | " + _done + "/" + _total + " shots";
            try
            {
                string text = "run " + _runId + " | " + why + " | " + _done + "/" + _total + " shots\n" + string.Join("\n", Finished) + (Problems.Count > 0 ? "\nPROBLEMS\n" + string.Join("\n", Problems) : "");
                File.WriteAllText(Path.Combine(OutRoot, "shot_" + _runId + ".txt"), text, new UTF8Encoding(false));
            }
            catch (Exception) { }
            Debug.Log("[SpellFxShot] finished: " + why + " (" + _done + "/" + _total + ")");
            // leave my Play (not when Play itself is what ended, and not with stay=1)
            if (!_stay && EditorApplication.isPlaying && why != "Play ended") EditorApplication.isPlaying = false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // camera
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static Vector3 FlatUnit(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        /// <summary>A copy of the main camera (with its URP data, no stack, found by name: this assembly has no URP reference) that renders into a render texture.</summary>
        static void MakeCamera(Camera main)
        {
            DestroyCamera();
            var go = new GameObject("SpellFxShot_Cam") { hideFlags = HideFlags.HideAndDontSave };
            _cam = go.AddComponent<Camera>();
            _cam.CopyFrom(main);
            _cam.enabled = false;
            var dataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (dataType != null)
            {
                var src = main.GetComponent(dataType); var dst = go.GetComponent(dataType) ?? go.AddComponent(dataType);
                if (src != null) EditorUtility.CopySerialized(src, dst);
                var stack = dataType.GetProperty("cameraStack")?.GetValue(dst) as System.Collections.IList;
                stack?.Clear();
            }
            _rt = new RenderTexture(VideoW, VideoH, 24);
            _tex = new Texture2D(VideoW, VideoH, TextureFormat.RGB24, false);
        }

        static void DestroyCamera()
        {
            if (_cam != null) Object.DestroyImmediate(_cam.gameObject);
            if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); }
            if (_tex != null) Object.DestroyImmediate(_tex);
            _cam = null; _rt = null; _tex = null;
        }

        /// <summary>The shoulder camera: pivot 1.7 m above the cast point, local offset (0.7, 0.55, -2.8) turned with the aim (yaw) and the pitch about the pivot, FOV 60.</summary>
        static void Shoot(Vector3 origin, float yawDegrees, float pitchDegrees, string file)
        {
            Quaternion rot = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            Vector3 pivot = origin + Vector3.up * 1.7f;
            _cam.transform.SetPositionAndRotation(pivot + rot * new Vector3(0.7f, 0.55f, -2.8f), rot);
            if (_job != null && _job.Side3 > 0f)
            {
                // view=side (2026-10-30, the judge's "side-angle debug still"): from the right, square to the path, the caster and the body both in frame
                Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
                Vector3 mid = origin + yaw * Vector3.forward * (_job.Ahead * 0.55f);
                Vector3 at = mid + yaw * Vector3.right * _job.Side3 + Vector3.up * 1.4f;
                _cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(mid + Vector3.up * 0.6f - at, Vector3.up));
            }
            _cam.fieldOfView = 60f;
            _cam.targetTexture = _rt;
            _cam.Render();
            var was = RenderTexture.active;
            try
            {
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
            }
            finally { RenderTexture.active = was; }
            File.WriteAllBytes(file, _tex.EncodeToJPG(90));
        }
    }
}
