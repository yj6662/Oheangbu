using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-20 (SPEC-ENEMY-RIG-VERIFY-308, TEST): arm rig / walk verification and repair of the Meshy humanoids
    // (dokkaebi, agwi, changgui). Queue-safe: every refusal comes back as a string, never a dialog. Run(string):
    //   status                       config, assets, records (no scene is opened, nothing sampled)
    //   measure[:<id>]               M1-M10 per monster and clip on a prefab copy in a preview scene, with the arm relax applied by
    //                                EnemyRigMotion298.ApplyArmRelax (the method Play runs); writes Art/Characters308/EnemyRigVerify/
    //                                measure_<id>.json + measure.md. Reads the open scene's actors when it is a ledger scene. Saves nothing.
    //   stills:<id>[|k=v,...]        studio stills (front / side / back x phases) through the #298 CPU-baked still; GPU work: the operator
    //                                runs it under Tools/resource_guard.py --gpu-run. Options: clip=orig|fixed, relax=legacy|profile|off, set=walk|idle|all
    //                                rev 2: relax=B (candidate B profile) | relax=<arm>,<elbow> (explicit amounts on any clip), avatar=live|A (must
    //                                match the avatar that is imported now; A stills go to their own folder), a ground slab, a warm-up render,
    //                                set=others (attack / hit / stun / death, and the run clip when the run-clip stage's file is imported)
    //   rev 2 (SPEC 개정 2, EnemyRigVerify308.Rev2.cs):
    //   avatar-floor[:<id>]          read-only: the avatar's reference rows (live | A | other), the smallest elbow angle it can show (from its
    //                                joint frames AND by a public-API sweep of the forearm muscle), and whether every clip file's own copy of the
    //                                reference rows is current
    //   avatar-dry[:<id>] | avatar-apply:<id> | avatar-revert:<id>     candidate A: the four arm rows of the rig's avatar reference pose (data:
    //                                avatarA308.json) through the model importer, then the clips that copy that avatar are re-imported and their
    //                                copies of the rows CHECKED (a stale copy fails the command and the .meta files go back). No scene is opened or
    //                                saved; .meta backups + a record; revert restores the recorded rows
    //   sweep:<id>[|clip=fixed|orig] candidate B: spread / elbow / swing / clearance / penetration of the walk for a grid of (arm, elbow) relax
    //                                amounts (verify308.json 'sweep'), the Pareto rows and the rows inside every limit
    //   sweep:<id>|set=idle          the idle relax on the avatar that is imported now (grid 'sweep.idleArm / idleElbow'): hand penetration,
    //                                clearance, and how far the wrists leave the source idle pose; the row nearest to that pose inside the limits
    //   level-dry | level-apply | level-revert[:<id>]   rev 3: EVERY take the data lists (idle, attack, hit, stun), each by its own
    //                                measured amount onto the measured height of the monster's reference takes (level.targetRoles):
    //                                the take's root height offset (an importer value), pass by pass until it lands
    //   plan / apply ... |ops=arms,profile=B        the arms row with the candidate B profile (walk relax as data) instead of the walk-0 profile
    //   avatar-sync:<id>             re-copies the rig's avatar rows into every clip that copies it (after a stage brought new clip files)
    //   rev 3 (SPEC 개정 3, EnemyRigVerify308.Rev3.cs; D308-24: candidate A stays on, the round that applies it):
    //   bounds-dry | bounds-apply | bounds-revert[:<id>]  the culling sphere of the three prefabs = the farthest vertex of every clip by
    //                                the M7 measure + a data margin; two lines of each prefab file, backup + sha in the record
    //   apply ... |ops=speed+arms+bounds           bounds: culling bounds a scene holds itself (not a prefab instance's) follow the prefab
    //   stills ... |skin=2           the two largest bone weights (the "2 bones" quality level); the default is the four-weight CPU bake
    //   revert order of a whole round: revert-all (and the run-clip tool's) -> bounds-revert -> level-revert -> avatar-revert:<id>
    //   (one monster alone: bounds-revert:<id> -> level-revert:<id> -> avatar-revert:<id>; the other monsters' rows stay in the records)
    //   apply of the candidate A pairing (ops=arms, walk-0 profile) needs that monster's GO in gate_enemyrig3.json, computed on the
    //   A measure that is on disk (gate.applyNeedsGo; the gate is Stage308_enemyrig3/_Tools/gate_enemyrig3.py)
    //   parity                       AC-4: without a profile the new relax entry poses the arms exactly as the pre-#308 formula
    //   cleanup                      destroys leftover fixture objects of this tool (never saves, never reopens a scene)
    //   clip-check[:<id>]            repaired walk FBX: file = the gated one, humanoid, avatar copied from the rig, clip flags, length
    //   clip-fix:<id>                re-applies the importer settings of the repaired walk FBX (only needed when clip-check refuses)
    //   plan:<scene>[|k=v,...]       dry: what apply would change in one ledger scene (296 | 298 | main). Opens the scene, changes nothing.
    //   apply:<scene>[|k=v,...]      ops=speed (R1: WalkMetresPerSecond = stance-foot speed measured here), ops=arms (R3 + R4: Walk clip ->
    //                                repaired clip, RelaxProfile -> profile asset; clip=keep leaves the clip and uses the _origclip profile),
    //                                ids=a+b (only those monsters). Scene file backed up, record written, only that scene saved.
    //   apply-all[|k=v,...] | revert:<scene> | revert-all     ledger order 296 -> 298 -> main (revert: the reverse)
    //   death-dry | death-apply | death-revert                R2: firstFrame 0 -> 1 on the death take where the first-frame pop exists
    //   manifest-dry | manifest-apply[|clip=orig] | manifest-revert    R1 source of truth: walkMetresPerSecond (and, unless clip=orig,
    //                                the walk clip row) in Art/Characters/Folklore298/import-manifest.json
    // Protected trees (Watershed295*, Reworld292*, MountainTrail285*) are never touched; a dirty open scene refuses every command
    // that opens or saves a scene. Recognition, combat, AI and HUD code are not referenced: this is presentation data only.
    public static partial class EnemyRigVerify308
    {
        const string Scene296 = "Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
        const string Scene298 = "Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity";
        const string SceneMain = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        static readonly string[] Ledger = { Scene296, Scene298, SceneMain };
        static readonly string[] ProtectedRoots = { "Watershed295", "Reworld292", "MountainTrail285" };
        static readonly string[] Roles = { "idle", "walk", "attack", "hit", "stun", "death" };
        const string Marker = "EnemyRigVerify308_";

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string ProjectFile(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        static string Out => Path.Combine(Repo, "Art/Characters308/EnemyRigVerify");
        static string ConfigFile => Path.Combine(Out, "verify308.json");
        static string RecordFile(string scene) => Path.Combine(Out, "Records", "apply_" + Path.GetFileNameWithoutExtension(scene) + ".json");

        // ------------------------------------------------------------------ data (verify308.json: every number is TEST data, none in code)
        [Serializable] public sealed class Monster
        {
            public string id = "", displayName = "", walkSource = "", motionSource = "", walkFixed = "", walkFixedTake = "", walkFixedSha256 = "";
            public string relaxProfile = "", relaxProfileOrigClip = "", deathTake = "Dead", blenderMeasure = "", blenderFix = "", manifestWalkPath = "", manifestWalkName = "";
            public string relaxProfileB = "";   // rev 2: candidate B profile (walk relax as data for the repaired clip)
            public string runClip = "", runTake = "";              // rev 2 (review F4): the run-clip stage's file; measured / photographed only when it is imported
        }
        [Serializable] public sealed class Limits
        {
            public float gaitVsBlenderRatio, slideRatio, slidePerStepCm, humanoidArmDeltaDeg, walkAbductionMeanDeg, swingKeepRatio, penForeHandCm, penUpperCm;
            public float triStretchRatio, triCollapseRatio, sectionMinRatio, soleMinCm, stanceSoleMeanMaxCm, popDeg, elbowRangeDiffDeg, abductionDiffDeg, stancePctDiff, soleDiffCm, slopeDeg;
        }
        // rev 2: winding-number field (cell / beta / thresholds), the second vote and the near-surface self-test (review F5);
        // insideNeed, rayStep and rayLength belong to the retired ray-parity test. The defaults only mirror verify308.json.
        [Serializable] public sealed class PenSettings
        {
            public int insideNeed = 4, longClipSamples = 60, longClipEvery = 2, nearSamples = 400; public float maxDepth = .25f, minDepth = .002f, rayStep = .0005f, rayLength = 6f;
            public float cell = .08f, beta = 2.5f, windingInside = .5f, windingOutsideMax = .1f, clearCap = .25f, besideMetres = 1f, bodyArmWeightMax = .3f, exactBand = .05f;
            public float behindConeDeg = 60f, behindOnlyMaxCm = 5f, nearOffsetCm = 1f, nearBandLow = .3f, nearBandHigh = .7f, nearMissedMaxPct = 25f, nearFalseMaxPct = 3f;
        }
        [Serializable] public sealed class StillSettings
        {
            public float[] walkPhases = Array.Empty<float>(), idlePhases = Array.Empty<float>(), yaws = Array.Empty<float>();
            public float[] otherPhases = Array.Empty<float>();   // rev 2: set=others (attack / hit / stun / death, no relax: the game applies none there)
            public float groundSize = 2.4f, groundLineWidth = .008f, minCoverageRatio = .25f, coverageTopShare = .6f; public int warmupRenders = 1, retries = 2, coverageColourDelta = 24, coverageStride = 7;
        }
        [Serializable] public sealed class SweepSettings
        {
            public float[] arm = Array.Empty<float>(), elbow = Array.Empty<float>(), idleArm = Array.Empty<float>(), idleElbow = Array.Empty<float>(); public int idleEvery = 4; public float minClearanceCm = 1f;
        }
        [Serializable] public sealed class AvatarSettings { public string rowsFile = ""; public float floorMaxDeg = 1f, rowTolerance = .0005f, stateToleranceDeg = .05f, floorSweepMuscle = 2f; public int floorSweepSteps = 400; }
        // rev 3 (SPEC 개정 3). Every number below comes from verify308.json; the classes carry no defaults of their own.
        //   level   role = manifest clip role (or walkfix / run); stat = how the planted height is read from the per-frame lowest
        //           vertex (min | entry | exit | ends | mean | median); level = false lists a take that is never moved, with its reason.
        //           pairs = "from>to" transitions whose step is reported and limited.
        //           targetRoles = the takes whose own measured planted height is the ground of that monster (the target is the middle
        //           of those imported, + targetLowestCm); needCm = moved when further off; aimCm = the passes go on while a moved take
        //           is further off; landCm = what a moved take must reach to be accepted.
        [Serializable] public sealed class LevelTake { public string role = "", stat = "", reason = ""; public bool level; }
        [Serializable] public sealed class LevelSettings
        {
            public float targetLowestCm, needCm, aimCm, landCm, maxStepCm, dipWarnCm; public int maxPasses; public string direction = "", requireAvatar = "";
            public LevelTake[] takes = Array.Empty<LevelTake>(); public string[] pairs = Array.Empty<string>(), targetRoles = Array.Empty<string>();
        }
        [Serializable] public sealed class BoundsSettings
        {
            public float marginMetres, roundUpMetres, maxRadiusMetres, equalToleranceMetres; public bool allowShrink, requireLevel, twoBoneSkin; public string requireAvatar = "";
            public string[] roles = Array.Empty<string>();
        }
        [Serializable] public sealed class GateSettings { public string armsNeedAvatar = ""; public bool applyNeedsGo; }
        [Serializable] public sealed class Config
        {
            public string schema = "";
            public float assumeWalkMetresPerSecond = 1.5f, stancePlateauBand = .25f, deathVisualMargin = .1f;
            public int lowestSoleVertices = 15;
            public Monster[] monsters = Array.Empty<Monster>();
            public Limits limits = new Limits();
            public PenSettings pen = new PenSettings();
            public StillSettings stills = new StillSettings();
            public SweepSettings sweep = new SweepSettings();
            public AvatarSettings avatar = new AvatarSettings();
            public LevelSettings level = new LevelSettings();
            public BoundsSettings bounds = new BoundsSettings();
            public GateSettings gate = new GateSettings();
        }

        static Config ReadConfig(out string why)
        {
            why = null;
            if (!File.Exists(ConfigFile)) { why = "no config " + ConfigFile; return null; }
            var cfg = JsonUtility.FromJson<Config>(File.ReadAllText(ConfigFile));
            if (cfg?.monsters == null || cfg.monsters.Length == 0) { why = "config has no monsters: " + ConfigFile; return null; }
            if (cfg.monsters.Any(m => string.IsNullOrEmpty(m.id) || string.IsNullOrEmpty(m.walkSource) || string.IsNullOrEmpty(m.motionSource))) { why = "config monster row incomplete (id / walkSource / motionSource)"; return null; }
            if (cfg.monsters.GroupBy(m => m.id).Any(g => g.Count() != 1)) { why = "config monster ids are not unique"; return null; }
            if (cfg.limits == null || cfg.limits.popDeg <= 0 || cfg.limits.slideRatio <= 0) { why = "config limits missing"; return null; }
            return cfg;
        }

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                if (command == "cleanup") return Cleanup();
                var cfg = ReadConfig(out string why); if (cfg == null) return "REFUSED " + why;
                if (command == "status") return Status(cfg);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Play mode (EnemyRigVerify308 works in Edit mode only)";
                string head = command, tail = "";
                int bar = command.IndexOf('|'); if (bar >= 0) { head = command.Substring(0, bar); tail = command.Substring(bar + 1); }
                string verb = head, arg = "";
                int colon = head.IndexOf(':'); if (colon >= 0) { verb = head.Substring(0, colon); arg = head.Substring(colon + 1).Trim(); }
                Dictionary<string, string> options;
                try { options = Options(tail); } catch (ArgumentException e) { return "REFUSED " + e.Message; }
                switch (verb)
                {
                    case "measure": return Guarded(() => Measure(cfg, arg));
                    case "stills": return Guarded(() => Stills(cfg, arg, options));
                    case "parity": return Guarded(() => Parity(cfg));
                    case "clip-check": return ClipCheck(cfg, arg);
                    case "clip-fix": return ClipFix(cfg, arg);
                    case "plan": return SceneOp(cfg, arg, options, false);
                    case "apply": return SceneOp(cfg, arg, options, true);
                    case "apply-all": return All(cfg, options, true);
                    case "revert": return Revert(cfg, arg);
                    case "revert-all": return All(cfg, options, false);
                    case "death-dry": return Death(cfg, "dry");
                    case "death-apply": return Death(cfg, "apply");
                    case "death-revert": return Death(cfg, "revert");
                    case "manifest-dry": return ManifestOp(cfg, options, "dry");
                    case "manifest-apply": return ManifestOp(cfg, options, "apply");
                    case "manifest-revert": return ManifestOp(cfg, options, "revert");
                    // rev 2. Each of these builds a fixture or re-imports an asset: Guarded adds the "open scenes unchanged" line (review F6)
                    case "avatar-floor": return Guarded(() => AvatarFloor(cfg, arg));
                    case "avatar-dry": return Guarded(() => AvatarOp(cfg, arg, "dry"));
                    case "avatar-apply": return Guarded(() => AvatarOp(cfg, arg, "apply"));
                    case "avatar-revert": return Guarded(() => AvatarOp(cfg, arg, "revert"));
                    case "avatar-sync": return Guarded(() => AvatarOp(cfg, arg, "sync"));
                    case "sweep": return Guarded(() => SweepRelax(cfg, arg, options));
                    case "level-dry": return Guarded(() => Level(cfg, "dry"));
                    case "level-apply": return Guarded(() => Level(cfg, "apply"));
                    case "level-revert": return Guarded(() => Level(cfg, "revert", arg));   // rev 3: [:<id>] = that monster's rows alone
                    // rev 3: culling bounds of the prefabs (the scene copies go through apply ... ops=bounds)
                    case "bounds-dry": return Guarded(() => BoundsOp(cfg, "dry"));
                    case "bounds-apply": return Guarded(() => BoundsOp(cfg, "apply"));
                    case "bounds-revert": return Guarded(() => BoundsOp(cfg, "revert", arg));
                }
                return "REFUSED unknown command '" + command + "' (status | measure[:<id>] | stills:<id> | parity | cleanup | clip-check[:<id>] | clip-fix:<id> | plan:<296|298|main> | " +
                    "apply:<scene>|ops=speed+arms+bounds | apply-all | revert:<scene> | revert-all | death-dry|apply|revert | manifest-dry|apply|revert | " +
                    "avatar-floor[:<id>] | avatar-dry[:<id>] | avatar-apply:<id> | avatar-revert:<id> | avatar-sync:<id> | sweep:<id>[|set=idle] | level-dry|apply|revert[:<id>] | bounds-dry|apply|revert[:<id>])";
            }
            catch (Exception e) { return "FAILED " + e; }
        }

        // ------------------------------------------------------------------ helpers
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        static string Sha(string file) { using (var sha = SHA256.Create()) using (var s = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        static bool ProtectedTree(Transform t) { var root = t.root.name; return ProtectedRoots.Any(p => root.StartsWith(p, StringComparison.Ordinal)); }
        static string ScenePath(string key)
        {
            switch (key)
            {
                case "296": case "architecture296": return Scene296;
                case "298": case "folklore298": return Scene298;
                case "main": return SceneMain;
                default: return Ledger.Contains(key) ? key : null;
            }
        }
        static string DirtyScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isDirty) return s.path; }
            return null;
        }
        static Dictionary<string, string> Options(string text)
        {
            var map = new Dictionary<string, string>();
            string last = null;
            foreach (var part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                // rev 2: relax=<arm>,<elbow> - a bare number after an option continues that option's value
                if (eq < 0 && last != null && float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { map[last] += "," + part.Trim(); continue; }
                if (eq <= 0) throw new ArgumentException("option needs key=value: " + part);
                string key = part.Substring(0, eq).Trim();
                if (key != "ops" && key != "clip" && key != "ids" && key != "relax" && key != "set" && key != "avatar" && key != "profile" && key != "skin")
                    throw new ArgumentException("unknown option " + key + " (ops | clip | ids | relax | set | avatar | profile | skin)");
                map[key] = part.Substring(eq + 1).Trim(); last = key;
            }
            return map;
        }
        static Monster[] Pick(Config cfg, string only, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(only)) return cfg.monsters;
            var ids = only.Split('+'); var picked = cfg.monsters.Where(m => ids.Contains(m.id)).ToArray();
            if (picked.Length != ids.Length) { why = "unknown monster id in '" + only + "' (" + string.Join(" | ", cfg.monsters.Select(m => m.id)) + ")"; return null; }
            return picked;
        }
        static AnimationClip ClipIn(string assetPath, string take) =>
            AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__", StringComparison.Ordinal) && (c.name == take || c.name.EndsWith("|" + take, StringComparison.Ordinal)));
        static AnimationClip OnlyClip(string assetPath)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__", StringComparison.Ordinal)).ToArray();
            return clips.Length == 1 ? clips[0] : null;
        }
        static AnimationClip FixedClip(Monster m) => string.IsNullOrEmpty(m.walkFixed) ? null : ClipIn(m.walkFixed, m.walkFixedTake);
        static EnemyArmRelaxProfile308 Profile(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<EnemyArmRelaxProfile308>(path);

        // "Parent/Name" picks the first sibling of that name: actors are addressed by a sibling-index path and checked by name.
        static string IndexPath(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent)
                parts.Add(p.parent != null ? p.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) : Array.IndexOf(p.gameObject.scene.GetRootGameObjects(), p.gameObject).ToString(CultureInfo.InvariantCulture));
            parts.Reverse(); return string.Join("/", parts);
        }
        static string NamePath(Transform t) { var sb = new StringBuilder(t.name); for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/"); return sb.ToString(); }
        static Transform Resolve(Scene scene, string indexPath)
        {
            var parts = indexPath.Split('/'); var roots = scene.GetRootGameObjects();
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) || r < 0 || r >= roots.Length) return null;
            var t = roots[r].transform;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) || k < 0 || k >= t.childCount) return null;
                t = t.GetChild(k);
            }
            return t;
        }

        // Every command that builds a fixture ends with an "open scenes unchanged" line (root count + dirty flag per loaded scene).
        static string OpenScenes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); sb.Append(s.path).Append(':').Append(s.rootCount).Append(':').Append(s.isDirty ? "dirty" : "clean").Append(';'); }
            return sb.ToString();
        }
        static string Guarded(Func<string> body)
        {
            string before = OpenScenes(); string result;
            try { result = body(); }
            finally { Sweep(); }
            string after = OpenScenes();
            return result + (after == before ? "\nopen scenes unchanged (" + before + ")" : "\nWARN open scenes changed: before " + before + " after " + after + " (nothing was saved; run cleanup and reload the scene without saving)");
        }

        // ------------------------------------------------------------------ fixture: a prefab copy in its own preview scene
        sealed class Fixture : IDisposable
        {
            public readonly Scene scene; public readonly GameObject host, visual; public readonly Animator animator; public readonly EnemyRigMotion298 rig; public readonly SkinnedMeshRenderer skin;
            public Fixture(Monster m)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)) ?? throw new InvalidOperationException("no prefab " + CompactFolklore298.PrefabPath(m.id));
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    host = EditorUtility.CreateGameObjectWithHideFlags(Marker + m.id, HideFlags.HideAndDontSave);
                    SceneManager.MoveGameObjectToScene(host, scene);
                    if (host.scene != scene) throw new InvalidOperationException("fixture host did not enter the preview scene");
                    // parented at creation: the copy is born in the host's (preview) scene, never in the open scene
                    visual = Object.Instantiate(prefab, host.transform); visual.name = "Folklore298_Visual";
                    if (visual.scene != scene) throw new InvalidOperationException("fixture visual did not enter the preview scene");
                    foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
                    animator = visual.GetComponentInChildren<Animator>(true) ?? throw new InvalidOperationException(m.id + " prefab has no Animator");
                    if (!animator.isHuman) throw new InvalidOperationException(m.id + " prefab Animator is not humanoid");
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.fireEvents = false;
                    // the runtime component, never enabled: Edit mode calls no OnEnable on it and no graph is made. Only ApplyArmRelax is used.
                    rig = host.AddComponent<EnemyRigMotion298>(); rig.enabled = false; rig.Animator = animator;
                    skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault()
                        ?? throw new InvalidOperationException(m.id + " prefab has no skinned mesh");
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (host != null) Object.DestroyImmediate(host);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void Sample(Fixture f, AnimationClip clip, float time) => CompactFolklore298.SamplePoseGraph298(f.animator, clip, time);

        // The relax as the game applies it at full idle or full walk weight. mode: off | legacy (no profile, -1 / -1) | profile.
        static void Relax(Fixture f, string mode, AnimationClip walk, EnemyArmRelaxProfile308 profile, bool idle) => Relax(f.rig, mode, walk, profile, idle);
        // rev 2: mode "B" = the profile handed in (the caller picks the candidate B asset); "<arm>,<elbow>" = explicit amounts through the
        // component's own ArmRelax / ElbowRelax fields (no profile: the #302 hang direction), on any clip.
        static void Relax(EnemyRigMotion298 rig, string mode, AnimationClip walk, EnemyArmRelaxProfile308 profile, bool idle)
        {
            if (mode == "off") return;
            rig.Walk = walk; rig.ArmRelax = -1f; rig.ElbowRelax = -1f; rig.RelaxProfile = mode == "profile" || mode == "B" ? profile : null;
            if (ExplicitRelax(mode, out float arm, out float elbow)) { rig.ArmRelax = arm; rig.ElbowRelax = elbow; }
            rig.ApplyArmRelax(idle ? 1f : 0f, idle ? 0f : 1f);
            rig.ArmRelax = -1f; rig.ElbowRelax = -1f;
        }
        static bool ExplicitRelax(string mode, out float arm, out float elbow)
        {
            arm = elbow = 0; var parts = (mode ?? "").Split(',');
            return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out arm) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out elbow) &&
                arm >= 0 && arm <= 1 && elbow >= 0 && elbow <= 1;
        }

        static void Sweep()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.transform.parent == null && go.name.StartsWith(Marker, StringComparison.Ordinal) && !EditorUtility.IsPersistent(go)) Object.DestroyImmediate(go);
        }

        static string Cleanup()
        {
            int n = Resources.FindObjectsOfTypeAll<GameObject>().Count(go => go != null && go.transform.parent == null && go.name.StartsWith(Marker, StringComparison.Ordinal) && !EditorUtility.IsPersistent(go));
            Sweep();
            return "cleanup: destroyed " + n + " leftover fixture root(s); open scenes " + OpenScenes() + " (no scene saved or reopened)";
        }

        // ------------------------------------------------------------------ status
        static string Status(Config cfg)
        {
            var sb = new StringBuilder("EnemyRigVerify308 status (" + cfg.schema + ")\n");
            foreach (var m in cfg.monsters)
            {
                bool prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)) != null;
                var fixedClip = FixedClip(m); var profile = Profile(m.relaxProfile); var orig = Profile(m.relaxProfileOrigClip);
                sb.AppendLine("  " + m.id + ": prefab " + (prefab ? "ok" : "MISSING") + " | walk source " + (OnlyClip(m.walkSource) != null ? "ok" : "MISSING") +
                    " | repaired clip " + (fixedClip != null ? fixedClip.name + " " + F(fixedClip.length) + "s" : "not imported") +
                    " | profile " + (profile != null ? "idle " + F(profile.IdleArm) + "/" + F(profile.IdleElbow) + " walk " + F(profile.WalkArm) + "/" + F(profile.WalkElbow) : "absent") +
                    " | _origclip profile " + (orig != null ? "ok" : "absent") +
                    " | B profile " + (Profile(m.relaxProfileB) is EnemyArmRelaxProfile308 b ? "walk " + F(b.WalkArm) + "/" + F(b.WalkElbow) : "absent") + " | avatar " + AvatarState(cfg, m, out _) +
                    " | " + ClipRowsLine(cfg, m) + " | run clip " + (RunClip(m) != null ? "imported" : string.IsNullOrEmpty(m.runClip) ? "none" : "not imported"));
            }
            foreach (var scene in Ledger) sb.AppendLine("  " + scene + ": " + (File.Exists(RecordFile(scene)) ? "APPLIED (record " + RecordFile(scene) + ")" : "not applied"));
            sb.AppendLine("  death first frame: " + (File.Exists(DeathRecord) ? "APPLIED (record " + DeathRecord + ")" : "not applied"));
            sb.AppendLine("  manifest rows: " + (File.Exists(ManifestRecord) ? "APPLIED (record " + ManifestRecord + ")" : "not applied"));
            foreach (var m in cfg.monsters) sb.AppendLine("  avatar candidate A " + m.id + ": " + (File.Exists(AvatarRecord(m.id)) ? "APPLIED (record " + AvatarRecord(m.id) + ")" : "not applied"));
            sb.Append(Rev3Status(cfg));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ parity (AC-4)
        // Test-only reference: the pre-#308 EnemyRigMotion298.Relax, kept here verbatim (its hang direction .95 / .12 included) so the
        // staged runtime can be compared against it. It is never used to pose anything that is measured or shown.
        static void LegacyRelaxReference(Transform actor, Transform upper, Transform lower, Transform hand, float arm, float elbow)
        {
            Vector3 hang = (-actor.up * .95f + actor.forward * .12f).normalized;
            Vector3 dir = (lower.position - upper.position).normalized;
            upper.rotation = Quaternion.FromToRotation(dir, Vector3.Slerp(dir, hang, arm)) * upper.rotation;
            Vector3 along = (lower.position - upper.position).normalized, fore = (hand.position - lower.position).normalized;
            lower.rotation = Quaternion.FromToRotation(fore, Vector3.Slerp(fore, along, elbow)) * lower.rotation;
        }

        static string Parity(Config cfg)
        {
            var sb = new StringBuilder("parity (no profile: new entry vs pre-#308 formula; weights idle+walk = .3+.7, 1+0, 0+1)\n"); bool all = true;
            foreach (var m in cfg.monsters)
            {
                // the source walk (walking_man): the clip on which the automatic #302 amounts are non-zero, whatever the manifest says now
                var walk = OnlyClip(m.walkSource); if (walk == null) { sb.AppendLine("  " + m.id + ": walk source clip not found (skipped)"); all = false; continue; }
                using (var f = new Fixture(m))
                {
                    var bones = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
                    float worst = 0; int samples = 0;
                    foreach (var w in new[] { new Vector2(.3f, .7f), new Vector2(1, 0), new Vector2(0, 1) })
                        for (int i = 0; i < 8; i++)
                        {
                            float t = walk.length * i / 8f;
                            Sample(f, walk, t); f.rig.Walk = walk; f.rig.ArmRelax = -1; f.rig.ElbowRelax = -1; f.rig.RelaxProfile = null; f.rig.ApplyArmRelax(w.x, w.y);
                            var a = bones.Select(b => f.animator.GetBoneTransform(b).rotation).ToArray();
                            Sample(f, walk, t); var tr = bones.Select(b => f.animator.GetBoneTransform(b)).ToArray();
                            // amounts from the runtime's own getters (automatic for walking_man): no second copy of the numbers here
                            float weight = w.x + w.y, arm = f.rig.ArmRelaxAmount * weight, elbow = f.rig.ElbowRelaxAmount * weight;
                            LegacyRelaxReference(f.host.transform, tr[0], tr[1], tr[2], arm, elbow); LegacyRelaxReference(f.host.transform, tr[3], tr[4], tr[5], arm, elbow);
                            for (int k = 0; k < bones.Length; k++) worst = Mathf.Max(worst, Quaternion.Angle(a[k], tr[k].rotation));
                            samples++;
                        }
                    bool ok = worst <= 0f; all &= ok;
                    sb.AppendLine("  " + m.id + ": " + samples + " poses, max arm bone difference " + worst.ToString("0.#####", CultureInfo.InvariantCulture) + " deg " + (ok ? "(equal)" : "MISMATCH") +
                        " | automatic amounts " + F(f.rig.ArmRelaxAmount) + " / " + F(f.rig.ElbowRelaxAmount));
                }
            }
            return sb.Append(all ? "PARITY OK" : "PARITY FAILED").ToString();
        }
    }
}
