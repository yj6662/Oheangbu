using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 phase 1b: the long wall and the pass gate on the user-fixed FOOT lines (SPEC-WORLD-CLIFF-BOUNDARY-308 section 6, AC-B11 /
    // B14 / B15 / B17 / B20 / B22; DECISIONS D308-3c, D308-9d, D308-9e). A reversible ledger: every scene write has a dry form (plan),
    // a byte backup, a ledger with before-values and hashes, a second run that changes nothing, and a revert. Refusals are STRINGS
    // (never a dialog): Play, compiling, a dirty scene, a scene outside the three targets, a protected path, a preview root that is
    // up, a stale layout, a scene that is not on the stage-1b terrain, a missing save-relocation file. AssetDatabase.SaveAssets is
    // never called (SaveAssetIfDirty on the assets made here). Nothing glows, lights, prompts or shows on the HUD.
    // Every number lives in the offline layout Art/World/Compact/Rebuild/CliffBoundary308/wall_1b/jangseong308.json
    // (python Tools/Art/jangseong308.py build); this class only seats that layout on the physics ground of the open scene.
    // The forest seam shells are never touched here: the terrain package's Enclosure305 v305.4b removes them after `check` says yes.
    // Queue: Oheangbu.EditorTools.WorldMacro.Jangseong308 Run "<command>"        (<scene> = arch296 | folk298 | main)
    //   status                       layout, inputs (stale or not), ledgers, what stands in the active scene
    //   plan:<scene>                 dry: seat on the physics ground, counts, what apply would write - nothing written
    //   apply:<scene>                build root Jangseong308 (Wall/West, Wall/East, Gate) + WorldSealProfile308, save the scene, ledger
    //   check:<scene>[:withshells][:max=N]   read-only: heights, grade, gaps, capsule probes, gate closed / open, deck access, A1, budget,
    //                                and the verdict line "seam shells may be removed: yes / no" -> Out/cb308-wall-check-<scene>.json + .txt
    //   revert:<scene>               remove the root, restore the session's SealProfile link, save the scene (assets are kept)
    //   preview:on[:allowdirty] / preview:off      the same build in memory under WallLook308_Preview (DontSave, nothing saved, no collider)
    public static partial class Jangseong308
    {
        internal const string LayoutFile = "Art/World/Compact/Rebuild/CliffBoundary308/wall_1b/jangseong308.json";
        internal const string BeyondFile = "Art/World/Compact/Rebuild/CliffBoundary308/plan/beyond308_1b.json";
        const string PreviewWall = "WallLook308_Preview", PreviewCliff = "CliffLook308_Preview", ForestRoot = "Enclosure305";
        const string MeshSub = "/Meshes/Wall", MaterialSub = "/Materials/Wall", ProfileSub = "/Data/WorldSealProfile308.asset";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                var opt = PostLedger308.Options(a.Skip(2));
                string target = a.Length > 1 ? a[1] : "";
                switch (a[0])
                {
                    case "status": return Status();
                    case "plan": return Apply(Need(target, a[0]), true);
                    case "apply": return Apply(Need(target, a[0]), false);
                    case "check": return Check(Need(target, a[0]), opt);
                    case "revert": return Revert(Need(target, a[0]));
                    case "preview":
                        if (target == "on") return PreviewOn(PostLedger308.Options(a.Skip(2)));
                        if (target == "off") return WallLook308.Run("preview:off");
                        throw new PostLedger308.Refused("use preview:on[:allowdirty] or preview:off");
                    default: throw new PostLedger308.Refused("unknown command '" + command + "' (status | plan:<scene> | apply:<scene> | check:<scene> | revert:<scene> | preview:on | preview:off)");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { Debug.LogException(e); return "error: " + e.GetType().Name + ": " + e.Message + " (see the console; the ledger shows how far the command got)"; }
        }

        static string Need(string scene, string verb)
        {
            if (string.IsNullOrEmpty(scene)) throw new PostLedger308.Refused("use " + verb + ":<scene> (arch296 | folk298 | main)");
            return scene;
        }

        // ---------------------------------------------------------------- layout (JsonUtility: flat arrays, unknown keys ignored)

        [Serializable] internal sealed class InputRec { public string path, sha256; }
        [Serializable] internal sealed class SeatRule { public float gradeCap, floatTolerance, hiddenMargin; public int sinkPasses, footSamplesAcross, samplesAlong, samplesAcross, chunkMaxModules; }
        [Serializable] internal sealed class Limits { public float topOverEaMin, eaStripFrom, eaStripTo, eaStripStep, endFillMin, edgeClear, floorTolerance, a1Radius, a1SideMin, a1Limit, deckRadius, agwiX, agwiZ, agwiMin; }
        [Serializable] internal sealed class Chunk { public string name, wing; public string[] modules; }
        [Serializable] internal sealed class Module
        {
            public string id, wing; public int leg; public bool gateSpan, rockA, rockB, buried, hidden, aboveDeck, aboveTop;
            public float[] a, b, centre, late; public float yaw, length, scale, halfDepth, baseA, baseB, grade, a1, late16, topOverEa;
        }
        [Serializable] internal sealed class JointRow { public string wing; public float[] natural, built; public bool[] rock; }
        [Serializable] internal sealed class Bastion { public string id, wing, kind, reason, anchor; public float[] at, late; public float yaw, baseY, groundFront; public bool hidden, frontBuried; public int index; }
        [Serializable] internal sealed class Piece { public string prefab, tag; public float x, y, z, yaw, sx, sy, sz; }
        [Serializable] internal sealed class BoxFill { public string tag, materialFrom; public float cx, cy, cz, sx, sy, sz; }
        [Serializable] internal sealed class Part { public string name; public float y0, y1; public float[] b, t; }
        [Serializable] internal sealed class BastionKit { public Piece[] pieces; public BoxFill[] boxes; public Part[] colliderParts; public float deckY; public float[] deckRect; }
        [Serializable] internal sealed class BoxCol { public string name; public float[] center, size; }
        [Serializable] internal sealed class Leaves { public string leftPrefab, rightPrefab, swing; public float hingeX, hingeZ, openDegrees, duration, clearHalfWidthOpen; public float[] meshOffset; public BoxCol collider; }
        [Serializable] internal sealed class Gate
        {
            public float[] a, b, centre, late; public float floorY, yaw, halfWidth, top, scale, doorPlaneZ, passageHalfWidth, doorBottom, doorBury, leafBottomY;
            public string requiredFact, doorTag; public Piece[] pieces; public BoxFill[] boxes; public string[] skipTags, noShadowTags; public Leaves leaves; public BoxCol[] colliders; public BoxCol blocker, obstacle;
        }
        [Serializable] internal sealed class WallKit { public string prefab, parapetPrefab; public float[] colliderProfileZY, capZ; public float colliderSink; }
        [Serializable] internal sealed class MaterialRule { public string shader; public string[] floatNames; public float[] floatValues; public float bumpFallback; }
        [Serializable] internal sealed class RockSlot { public string module, prefab, path; public float x, z, yaw, scale, bury; }
        [Serializable] internal sealed class Rocks { public bool build; public RockSlot[] slots; }
        [Serializable] internal sealed class ProbePoint { public string id, kind; public float x, z; }
        [Serializable] internal sealed class Probes { public ProbePoint[] points; public float every, leakPast, roadHalfWidth, shellJointRadius; public float[] hillBack, gateLanes; }
        [Serializable] internal sealed class Budget { public int triangles, batches, colliders; public float meshMemoryMB; }
        [Serializable] internal sealed class CountsIn { public Budget budget; }
        [Serializable] internal sealed class Layout
        {
            public string version, root, stage, stageHeight, packPrefabDir, wallPrefab, parapetPrefab; public InputRec[] inputs;
            public float moduleLength, moduleHeight, moduleHalfDepth, parapetY, parapetZ, parapetTop; public int vertexStride;
            public SeatRule seat; public Limits limits; public Chunk[] chunks; public Module[] modules; public JointRow[] joints; public Bastion[] bastions; public BastionKit bastionKit;
            public Gate gate; public WallKit wallKit; public MaterialRule materials; public Rocks rocks; public Probes probes; public string[] replaces; public CountsIn counts;
            [NonSerialized] public string Sha256;
        }

        internal static Layout LoadLayout()
        {
            string abs = PostLedger308.RepoPath(LayoutFile);
            if (!File.Exists(abs)) throw new PostLedger308.Refused("no layout " + LayoutFile + " (run python Tools/Art/jangseong308.py build)");
            var bytes = File.ReadAllBytes(abs);
            var l = JsonUtility.FromJson<Layout>(Encoding.UTF8.GetString(bytes));
            if (l == null || l.modules == null || l.modules.Length == 0 || l.joints == null || l.gate == null || l.gate.pieces == null || l.gate.leaves == null || l.gate.leaves.collider == null || l.gate.blocker == null
                || l.gate.obstacle == null || l.gate.colliders == null || l.wallKit == null || l.wallKit.colliderProfileZY == null || l.wallKit.colliderProfileZY.Length < 6 || l.bastionKit == null || l.bastionKit.colliderParts == null
                || l.bastionKit.colliderParts.Length == 0 || l.materials == null || l.seat == null || l.limits == null || l.probes == null || l.chunks == null || l.counts == null || l.counts.budget == null || l.moduleLength <= 0f)
                throw new PostLedger308.Refused("layout " + LayoutFile + " is incomplete");
            if (l.bastions == null) l.bastions = new Bastion[0];
            if (l.rocks == null) l.rocks = new Rocks();
            if (l.rocks.slots == null) l.rocks.slots = new RockSlot[0];
            if (l.replaces == null) l.replaces = new string[0];
            if (l.inputs == null) l.inputs = new InputRec[0];
            if (l.root != "Jangseong308") throw new PostLedger308.Refused("layout root '" + l.root + "' is not Jangseong308 (the content keep-out list names that root)");
            if (l.gate.requiredFact != WorldMacroPlaytestSession.SouthGateOpenedId) throw new PostLedger308.Refused("layout fact '" + l.gate.requiredFact + "' is not WorldMacroPlaytestSession.SouthGateOpenedId");
            if (l.seat.footSamplesAcross < 2 || l.seat.samplesAlong < 1 || l.seat.samplesAcross < 2 || l.seat.chunkMaxModules < 1) throw new PostLedger308.Refused("layout seat sample counts / chunkMaxModules are too small");
            foreach (var m in l.modules)
                if (m.a == null || m.b == null || m.late == null || m.a.Length < 2 || m.b.Length < 2 || m.late.Length < 2 || m.length <= 0f) throw new PostLedger308.Refused("layout module " + m.id + " is incomplete");
            foreach (string wing in l.modules.Select(m => m.wing).Distinct())
            {
                var row = l.joints.FirstOrDefault(j => j.wing == wing); int n = l.modules.Count(m => m.wing == wing);
                if (row == null || row.natural == null || row.rock == null || row.built == null || row.natural.Length != n + 1 || row.rock.Length != n + 1) throw new PostLedger308.Refused("layout joints of wing " + wing + " do not match its " + n + " modules");
            }
            l.Sha256 = CliffCore308.Sha(bytes);
            return l;
        }

        /// <summary>Layout inputs whose file on disk no longer has the recorded hash (the layout must be rebuilt before a scene write).</summary>
        internal static List<string> StaleInputs(Layout l)
        {
            var stale = new List<string>();
            foreach (var i in l.inputs)
            {
                string now = CliffCore308.ShaFile(PostLedger308.RepoPath(i.path));
                if (now != i.sha256) stale.Add(i.path + (now == "" ? " (missing)" : ""));
            }
            return stale;
        }

        static string StageHeightSha(Layout l)
        {
            var hit = l.inputs.FirstOrDefault(i => i.path == l.stageHeight);
            return hit != null ? hit.sha256 : "";
        }

        // ---------------------------------------------------------------- ledger

        [Serializable] internal sealed class AssetRec { public string path, sha256; }
        [Serializable] internal sealed class Counts { public int renderers, triangles, batches, shadowCasters, colliders, modules, modulesBuilt, bastions, rocks; public float meshMemoryMB; }
        [Serializable] internal sealed class WallLedger
        {
            public string format = "cb308.wall.1", scene, key, state = "none", stage = "", utc = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public string layoutSha256 = "", buildKey = "", heightSha256 = "", beyondSha256 = "", profile = "", sealProfileBefore = "", sealProfileBeforeGuid = "";
            public float gateFloorY, seatWorstDelta; public Counts counts = new Counts(); public AssetRec[] assets = new AssetRec[0]; public string[] history = new string[0];
        }

        static string LedgerFile(CliffCore308.Config cfg, string key) => CliffCore308.OutFile(cfg, "cb308-wall-" + key + ".json");

        internal static WallLedger ReadLedger(CliffCore308.Config cfg, string scenePath)
        {
            string key = CliffCore308.SceneKey(cfg, scenePath), file = LedgerFile(cfg, key);
            if (!File.Exists(file)) return new WallLedger { scene = scenePath, key = key };
            var l = JsonUtility.FromJson<WallLedger>(File.ReadAllText(file, Encoding.UTF8));
            if (l == null || l.scene != scenePath) throw new PostLedger308.Refused("ledger " + file + " does not belong to " + scenePath);
            if (l.assets == null) l.assets = new AssetRec[0]; if (l.history == null) l.history = new string[0]; if (l.counts == null) l.counts = new Counts();
            return l;
        }

        static void WriteLedger(CliffCore308.Config cfg, WallLedger l, string note)
        {
            l.utc = PostLedger308.Utc();
            l.history = l.history.Concat(new[] { l.utc + " " + note }).ToArray();
            CliffCore308.WriteText(LedgerFile(cfg, l.key), JsonUtility.ToJson(l, true));
        }

        // ---------------------------------------------------------------- guards

        static void RequireNoPreview()
        {
            var up = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && (g.name == PreviewWall || g.name == PreviewCliff)).Select(g => g.name).Distinct().ToArray();
            if (up.Length > 0) throw new PostLedger308.Refused("a preview root is up (" + string.Join(", ", up) + "): run WallLook308 preview:off, then CliffLook308 preview:off, before a scene write");
        }

        static Scene OpenForRead(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var active = SceneManager.GetActiveScene();
            if (active.path == path && SceneManager.sceneCount == 1) return active;   // a dirty active target scene can still be measured
            PostLedger308.RequireEditable(); RequireNoPreview();
            return PostLedger308.Open(path);
        }

        static GameObject[] Roots(Scene scene, Layout lay) => scene.GetRootGameObjects().Where(g => g.name == lay.root && CliffCore308.Saved(g)).ToArray();

        /// <summary>The marker child of a built root: "Build|key=...|layout=...|utc=...".</summary>
        static string Marker(GameObject root, string key)
        {
            foreach (Transform c in root.transform)
                if (c.name.StartsWith("Build|", StringComparison.Ordinal))
                    foreach (string part in c.name.Split('|'))
                        if (part.StartsWith(key + "=", StringComparison.Ordinal)) return part.Substring(key.Length + 1);
            return "";
        }

        static Vector2 V2(float[] a) => new Vector2(a[0], a[1]);
        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);
        static string F(float v, string format = "F2") => v.ToString(format, Inv);

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("Jangseong308 status | active scene " + scene.path + " dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying);
            Layout lay = null;
            try { lay = LoadLayout(); } catch (PostLedger308.Refused r) { sb.AppendLine("layout: " + r.Message); }
            var cfg = CliffCore308.LoadConfig();
            if (lay != null)
            {
                var stale = StaleInputs(lay);
                sb.AppendLine("layout " + LayoutFile + " sha " + lay.Sha256.Substring(0, 12) + " (" + lay.version + ", stage " + lay.stage + "): " + lay.modules.Length + " modules (" + lay.modules.Count(m => !m.hidden) + " built offline), "
                    + lay.bastions.Length + " bastions, gate floor " + F(lay.gate.floorY) + ", rock slots " + lay.rocks.slots.Length + " (build=" + lay.rocks.build + ")");
                sb.AppendLine("layout inputs: " + (stale.Count == 0 ? "all " + lay.inputs.Length + " match the files on disk" : "STALE - rebuild the layout (python Tools/Art/jangseong308.py build): " + string.Join(", ", stale)));
                sb.AppendLine("stage height " + lay.stageHeight + " sha " + Short(StageHeightSha(lay)));
            }
            string beyond = PostLedger308.RepoPath(BeyondFile);
            sb.AppendLine("save relocation " + BeyondFile + ": " + (File.Exists(beyond) ? "sha " + Short(CliffCore308.ShaFile(beyond)) : "MISSING (apply refuses; terrain package output)"));
            var profile = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.WorldSealProfileSO>(cfg.assetRoot + ProfileSub);
            sb.AppendLine("profile " + cfg.assetRoot + ProfileSub + ": " + (profile == null ? "absent" : "rings " + profile.BeyondRings.Length + ", EA rests " + profile.EaRestIds.Length + ", fact " + profile.RequiredFact));
            sb.AppendLine("scenes:");
            foreach (var sc in cfg.scenes)
            {
                var l = ReadLedger(cfg, sc.path); var cb = CliffBoundary308.ReadLedger(cfg, sc.path);
                sb.AppendLine("  " + sc.key + ": wall ledger " + l.state + (l.buildKey != "" ? " build " + l.buildKey : "") + (l.utc != "" ? " " + l.utc : "") + " | terrain ledger " + cb.state + (cb.stage != "" ? " stage " + cb.stage : "")
                    + (l.state == "applied" ? " | renderers " + l.counts.renderers + ", triangles " + l.counts.triangles + ", batches " + l.counts.batches + ", shadow casters " + l.counts.shadowCasters + ", colliders " + l.counts.colliders : ""));
            }
            if (lay != null && CliffCore308.SceneKey(cfg, scene.path) != null)
            {
                var roots = Roots(scene, lay);
                sb.AppendLine("active scene: " + (roots.Length == 0 ? "no " + lay.root + " root" : roots.Length + " " + lay.root + " root(s), build " + Marker(roots[0], "key") + ", layout " + Marker(roots[0], "layout")
                    + ", colliders " + roots[0].GetComponentsInChildren<Collider>(true).Length + ", renderers " + roots[0].GetComponentsInChildren<Renderer>(true).Length));
            }
            var previews = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && (g.name == PreviewWall || g.name == PreviewCliff)).Select(g => g.name).ToArray();
            sb.Append("previews up: " + (previews.Length == 0 ? "none" : string.Join(", ", previews)) + " | quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ")");
            return sb.ToString();
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length));

        // ---------------------------------------------------------------- seat on the physics ground (pure measurement)

        internal sealed class Seated
        {
            public Module M; public Vector2 A, B, Late; public float BaseA, BaseB, NaturalA, NaturalB, FloatMax, SunkMax; public bool Hidden; public float[] SampleU, SampleGround;
            public Vector2 Centre => (A + B) * .5f;
            public float BaseCentre => (BaseA + BaseB) * .5f;
            public float Grade => (BaseB - BaseA) / M.length;
        }
        internal sealed class SeatedBastion { public Bastion B; public float BaseY; public bool Hidden; }
        internal sealed class Plan
        {
            public readonly List<Seated> Modules = new List<Seated>(); public readonly List<SeatedBastion> Bastions = new List<SeatedBastion>(); public readonly List<string> Notes = new List<string>();
            public float GateFloor, DoorLow, DoorHigh, WorstDelta; public string WorstAt = "", Key = "";
            public Seated Of(string id) => Modules.First(s => s.M.id == id);
        }

        static float FootMin(CliffCore308.Ground ground, Vector2 q, Vector2 late, float halfDepth, int n)
        {
            float low = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var s = q + late * ((i - (n - 1) * .5f) * (2f * halfDepth / (n - 1)));
                if (CliffCore308.TerrainY(ground, s.x, s.y, out float y)) low = Mathf.Min(low, y);
            }
            return float.IsInfinity(low) ? float.NaN : low;
        }

        /// <summary>The offline seat rule on the terrain colliders: joint = lowest ground under the wall thickness (a joint the layout
        /// marks as standing in cliff rock keeps the layout height = the ground under the rock), lowered to the grade cap, then lowered
        /// until no footprint sample floats. Gate floor = lowest ground on the door plane - doorBury - doorBottom.</summary>
        internal static Plan Compute(Layout lay, CliffCore308.Ground ground)
        {
            Physics.SyncTransforms();
            var plan = new Plan(); var st = lay.seat; float hd = lay.moduleHalfDepth, cap = st.gradeCap;
            foreach (string wing in lay.modules.Select(m => m.wing).Distinct())
            {
                var mods = lay.modules.Where(m => m.wing == wing).ToArray(); var row = lay.joints.First(j => j.wing == wing); int n = mods.Length;
                var seated = mods.Select(m => new Seated { M = m, A = V2(m.a), B = V2(m.b), Late = V2(m.late).normalized }).ToArray();
                var nat = new float[n + 1];
                for (int j = 0; j <= n; j++)
                {
                    if (row.rock[j]) { nat[j] = row.natural[j]; continue; }
                    float h = float.PositiveInfinity;
                    if (j < n) h = Mathf.Min(h, FootMin(ground, seated[j].A, seated[j].Late, hd, st.footSamplesAcross));
                    if (j > 0) h = Mathf.Min(h, FootMin(ground, seated[j - 1].B, seated[j - 1].Late, hd, st.footSamplesAcross));
                    if (float.IsNaN(h) || float.IsInfinity(h)) throw new PostLedger308.Refused("wing " + wing + " joint " + j + " has no terrain collider under it");
                    nat[j] = h;
                }
                foreach (var s in seated)
                {
                    int count = st.samplesAlong * st.samplesAcross; s.SampleU = new float[count]; s.SampleGround = new float[count];
                    for (int i = 0; i < st.samplesAlong; i++)
                        for (int c = 0; c < st.samplesAcross; c++)
                        {
                            float u = (i + .5f) / st.samplesAlong; var q = Vector2.Lerp(s.A, s.B, u) + s.Late * ((c - (st.samplesAcross - 1) * .5f) * (2f * hd / (st.samplesAcross - 1)));
                            s.SampleU[i * st.samplesAcross + c] = u; s.SampleGround[i * st.samplesAcross + c] = CliffCore308.TerrainY(ground, q.x, q.y, out float gy) ? gy : float.NaN;
                        }
                }
                var built = (float[])nat.Clone();
                void Cap()
                {
                    for (int i = 1; i < built.Length; i++) built[i] = Mathf.Min(built[i], built[i - 1] + cap * mods[i - 1].length);
                    for (int i = built.Length - 2; i >= 0; i--) built[i] = Mathf.Min(built[i], built[i + 1] + cap * mods[i].length);
                }
                float Float(int i)
                {
                    float worst = 0f; var s = seated[i];
                    for (int k = 0; k < s.SampleU.Length; k++) if (!float.IsNaN(s.SampleGround[k])) worst = Mathf.Max(worst, Mathf.Lerp(built[i], built[i + 1], s.SampleU[k]) - s.SampleGround[k]);
                    return worst;
                }
                Cap();
                for (int pass = 0; pass < st.sinkPasses; pass++)
                {
                    bool moved = false;
                    for (int i = 0; i < n; i++) { float f = Float(i); if (f > st.floatTolerance) { built[i] -= f; built[i + 1] -= f; moved = true; } }
                    Cap();
                    if (!moved) break;
                }
                for (int i = 0; i < n; i++)
                {
                    var s = seated[i]; s.NaturalA = nat[i]; s.NaturalB = nat[i + 1]; s.BaseA = built[i]; s.BaseB = built[i + 1]; s.Hidden = true; int counted = 0;
                    for (int k = 0; k < s.SampleU.Length; k++)
                    {
                        if (float.IsNaN(s.SampleGround[k])) { s.Hidden = false; continue; }
                        float line = Mathf.Lerp(s.BaseA, s.BaseB, s.SampleU[k]), gap = line - s.SampleGround[k]; counted++;
                        s.FloatMax = Mathf.Max(s.FloatMax, gap);
                        if (-gap < lay.parapetTop) s.SunkMax = Mathf.Max(s.SunkMax, -gap);       // ground above the wall top = rock, not a sunk foot
                        if (s.SampleGround[k] - (line + lay.parapetTop) < st.hiddenMargin) s.Hidden = false;
                    }
                    if (counted == 0) s.Hidden = false;
                    float delta = Mathf.Max(Mathf.Abs(s.BaseA - row.built[i]), Mathf.Abs(s.BaseB - row.built[i + 1]));
                    if (delta > plan.WorstDelta) { plan.WorstDelta = delta; plan.WorstAt = s.M.id; }
                    plan.Modules.Add(s);
                }
                foreach (var b in lay.bastions.Where(x => x.wing == wing))
                {
                    if (b.index < 0 || b.index > n || (b.anchor == "centre" && b.index >= n)) throw new PostLedger308.Refused("bastion " + b.id + " names joint / module " + b.index + " outside wing " + wing);
                    float y = b.anchor == "centre" ? (built[b.index] + built[b.index + 1]) * .5f : built[b.index];
                    var rect = lay.bastionKit.colliderParts[0].b; var late = V2(b.late).normalized; var right = new Vector2(late.y, -late.x); bool hidden = true;
                    for (int ix = 0; ix < 7 && hidden; ix++)
                        for (int iz = 0; iz < 7 && hidden; iz++)
                        {
                            var q = V2(b.at) + right * Mathf.Lerp(rect[0], rect[2], ix / 6f) + late * Mathf.Lerp(rect[1], rect[3], iz / 6f);
                            if (!CliffCore308.TerrainY(ground, q.x, q.y, out float gy) || gy - (y + lay.parapetTop) < st.hiddenMargin) hidden = false;
                        }
                    plan.Bastions.Add(new SeatedBastion { B = b, BaseY = y, Hidden = hidden });
                }
            }
            // gate floor: the leaf bottom ends doorBury below the lowest ground it stands over (the road is never edited)
            var g = lay.gate; var centre = V2(g.centre); var lateG = V2(g.late).normalized; var rightG = new Vector2(lateG.y, -lateG.x);
            plan.DoorLow = float.PositiveInfinity; plan.DoorHigh = float.NegativeInfinity;
            for (float x = -g.passageHalfWidth; x <= g.passageHalfWidth + .001f; x += .5f)
            {
                var q = centre + rightG * x + lateG * g.doorPlaneZ;
                if (!CliffCore308.TerrainY(ground, q.x, q.y, out float gy)) throw new PostLedger308.Refused("no terrain collider under the gate's door plane");
                plan.DoorLow = Mathf.Min(plan.DoorLow, gy); plan.DoorHigh = Mathf.Max(plan.DoorHigh, gy);
            }
            plan.GateFloor = plan.DoorLow - g.doorBury - g.doorBottom;
            var key = new StringBuilder(lay.Sha256);
            foreach (var s in plan.Modules) key.Append('|').Append(s.M.id).Append(F(s.BaseA, "F3")).Append(',').Append(F(s.BaseB, "F3")).Append(s.Hidden ? 'h' : 'b');
            foreach (var b in plan.Bastions) key.Append('|').Append(b.B.id).Append(F(b.BaseY, "F3")).Append(b.Hidden ? 'h' : 'b');
            key.Append("|gate").Append(F(plan.GateFloor, "F3"));
            plan.Key = CliffCore308.Sha(Encoding.UTF8.GetBytes(key.ToString())).Substring(0, 12);
            return plan;
        }

        static string PlanText(Layout lay, Plan plan)
        {
            var sb = new StringBuilder();
            foreach (var group in plan.Modules.GroupBy(s => s.M.wing))
            {
                var all = group.ToList(); var built = all.Where(s => !s.Hidden).ToList();
                sb.AppendLine("wing " + group.Key + ": " + all.Count + " modules, " + built.Count + " built, " + (all.Count - built.Count) + " hidden in the rock (layout " + all.Count(s => s.M.hidden) + " hidden) | |grade| max "
                    + F(all.Max(s => Mathf.Abs(s.Grade)), "F3") + " (cap " + F(lay.seat.gradeCap) + ") | gap under a base max " + F(built.Count > 0 ? built.Max(s => s.FloatMax) : 0f) + " m | base sunk max " + F(built.Count > 0 ? built.Max(s => s.SunkMax) : 0f) + " m");
            }
            sb.AppendLine("seat vs the offline layout: worst joint difference " + F(plan.WorstDelta) + " m" + (plan.WorstAt != "" ? " at " + plan.WorstAt : "") + " (the layout is measured on the 4 m field, the seat on the tile colliders)");
            sb.AppendLine("bastions: " + plan.Bastions.Count + ", built " + plan.Bastions.Count(b => !b.Hidden) + " - " + string.Join("; ", plan.Bastions.Select(b => b.B.id + " " + b.B.reason + " base " + F(b.BaseY) + (b.Hidden ? " (inside the rock, not built)" : ""))));
            float df = plan.GateFloor - lay.gate.floorY;
            sb.Append("gate floor " + F(plan.GateFloor) + " (door-plane ground " + F(plan.DoorLow) + ".." + F(plan.DoorHigh) + "; layout " + F(lay.gate.floorY) + ", difference " + df.ToString("+0.00;-0.00", Inv) + " m, tolerance " + F(lay.limits.floorTolerance)
                + ") | leaf bottoms in the ground " + F(plan.DoorLow - (plan.GateFloor + lay.gate.doorBottom)) + ".." + F(plan.DoorHigh - (plan.GateFloor + lay.gate.doorBottom)) + " m | build key " + plan.Key);
            return sb.ToString();
        }
    }
}
