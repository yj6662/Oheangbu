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
    ///   ... "shot:run:&lt;letter&gt;:&lt;variant[,variant..]&gt;[:ahead=4.6][:side=0][:frames=180][:back=red|green|none][:look=main][:pitch=15][:swing=1][:stay=1]"
    ///   ... "shot:status"      ... "shot:stop"
    /// A shot = the real letter raised on the real channel in the lab, seen through a copy of the main camera at the shoulder pose (pivot 1.7 m above the caster, local offset
    /// (0.7, 0.55, -2.8), FOV 60), Time.captureFramerate = 60 (one game frame = 1/60 s). Frames go to Art/SpellVFX308/Restart/shots/&lt;letter&gt;_&lt;variant&gt;/f_00000.jpg
    /// (1280x720) + index.csv (frame,clockUs,ageSec,launchAgeSec,hits,players,note; ages on the SCOPE clock, from the commit / from the launch) + summary.txt. A comma list of
    /// variants is shot one after the other in the same Play. Afterwards everything is put back and Play is left (stay=1 keeps it). Never opens a dialog, never saves a scene,
    /// never calls the old Vol2VfxCapture.</summary>
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
                return "REFUSED shot: play | run:<letter>:<variant[,variant]>[:ahead=4.6][:side=0][:frames=180][:back=red|green|none][:look=main][:pitch=15][:swing=1][:stay=1] | status | stop";
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
            public float Ahead = 4.6f, Side = 0f, Pitch = 15f;
            public int Frames = 180;
            public string Back = "red";
            public bool MainLook, Swing, Stay;
            public string Name => Letter + "_" + Variant;
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
        static readonly List<GameObject> Hidden = new List<GameObject>(), Dummies = new List<GameObject>();
        static GameObject _ground; static Material _groundFallback;
        static bool _extrasOnlyBefore, _presenterWasEnabled; static bool _stagedScope;
        static Oheangbu.App.World.LookDriver308 _lookDriver; static Oheangbu.Data.World.LookProfile308 _lookSaved; static bool _lookSwapped;
        static Vol2CastLog _beganLog; static Action<Vol2CastLog> _onBegan; static Action<Vol2CastDecision> _onDecided; static Vol2CastDecision _decision;
        static int _spawnedBefore; static long _launchUs;

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
                    default: return "REFUSED shot:run: unknown option " + k;
                }
            }
            Queue.Clear(); Finished.Clear(); Problems.Clear(); SavedVariants.Clear();
            foreach (string variant in t[1].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
                Queue.Enqueue(new Job { Letter = proto.Letter, Variant = variant, Ahead = proto.Ahead, Side = proto.Side, Pitch = proto.Pitch, Frames = proto.Frames, Back = proto.Back, MainLook = proto.MainLook, Swing = proto.Swing, Stay = proto.Stay });
            _total = Queue.Count; _done = 0; _stay = proto.Stay;
            _runId = DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
            Directory.CreateDirectory(OutRoot);
            _running = true; _st = St.WaitScope; _lastFrame = -1; _settle = 0;
            _savedCaptureRate = Time.captureFramerate; _savedKeepSwing = SpellFxSilencer.KeepBrushSwing;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += OnReload;
            return "queued " + _total + " shot(s) " + proto.Letter + ":" + t[1] + " | " + OutRoot;
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
            Vector3 at = origin + fwd * j.Ahead + right * j.Side;
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
            _index.WriteLine("frame,clockUs,ageSec,launchAgeSec,hits,players,note");
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
            try { _wiring.LetterChannel.Raise(new DrawnLetter(j.Letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); }
            catch (Exception e) { Problems.Add(j.Name + ": raising the letter: " + e.Message); }
            if (_beganLog == null)
            {
                Problems.Add(j.Name + ": the letter started no cast (" + (_decision != null ? "accepted=" + _decision.Accepted + " reason=" + _decision.Reason : "no decision") + ")");
                _st = St.Teardown; return;
            }
            _launchUs = Math.Max(_beganLog.StartUs, _beganLog.Emitter != null ? _beganLog.Emitter.StartUs : _beganLog.StartUs);
            if (_director.Spawned == _spawnedBefore) Problems.Add(j.Name + ": no SpellFx prefab was built (no catalogue asset, or no entry for " + j.Letter + "/" + j.Variant + " with a SpellFxPlayer): the OLD look is what these frames show");
            _st = St.Record;
        }

        static void Record()
        {
            var j = _job;
            long now = _coord.Services.Clock.NowUs;
            var player = _director.PlayerOf(_beganLog.Cast.Value);
            double age = (now - _beganLog.StartUs) / 1000000.0, launchAge = (now - _launchUs) / 1000000.0;
            _hits = _beganLog.Contacts.Count(c => c.BudgetAccepted);
            string note = player != null ? "fx " + player.name : "no fx";
            Shoot(_beganLog.Spec.Origin, Quaternion.LookRotation(_beganLog.Spec.Forward, Vector3.up).eulerAngles.y, j.Pitch, Path.Combine(_folder, "f_" + _frames.ToString("D5", Inv) + ".jpg"));
            _index.WriteLine(string.Join(",", _frames.ToString(Inv), now.ToString(Inv), age.ToString("0.0000", Inv), launchAge.ToString("0.0000", Inv), _hits.ToString(Inv), _director.ActiveCount.ToString(Inv), note.Replace(',', ';')));
            _frames++;
            if (_frames >= j.Frames || EditorApplication.timeSinceStartup > _guardUntil) _st = St.Teardown;
        }

        static void Teardown()
        {
            var j = _job;
            try { _index?.Dispose(); } catch (Exception) { }
            _index = null;
            var player = _beganLog != null ? _director.PlayerOf(_beganLog.Cast.Value) : null;
            string summary = j.Name + " | frames " + _frames + (_beganLog != null ? " | cast " + _beganLog.Status + " contacts " + _beganLog.Contacts.Count + " paid " + _beganLog.PaidContacts + " materials " + _beganLog.Materials.Count : " | NO CAST")
                + " | director spawned " + _director.Spawned + " skipped " + _director.Skipped + " retired " + _director.Retired + " hits " + _director.HitsRelayed + " | " + _director.LastNote
                + (player != null ? " | player begin " + player.BeginCount + " launch " + player.LaunchCount + " ticks " + player.TickCount + " hit " + player.HitCount + " material " + player.MaterialCount + " reaction " + player.ReactionCount + " end " + player.EndCount : " | player gone");
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
            Dummies.Clear();
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
