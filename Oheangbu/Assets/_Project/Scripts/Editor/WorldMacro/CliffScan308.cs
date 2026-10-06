using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff boundary (SPEC-WORLD-CLIFF-BOUNDARY-308, D308-9c): the READ-ONLY half of the CliffBoundary308 ledger.
    //   CliffCore308  config + stage height field + op boxes + tile lookup + mesh re-sample kernel + capsule walker. No scene or
    //                 asset write lives here; the writing commands (CliffBoundary308.*) call into it.
    //   CliffScan308  queue diagnostics built on the core. They change nothing: no scene save, no asset, no reference; temporary
    //                 objects are HideAndDontSave and destroyed before the call returns. The only output is one JSON report under
    //                 Art/World/Compact/Rebuild/CliffBoundary308/Out/.
    // Queue: Oheangbu.EditorTools.WorldMacro.CliffScan308 Run "<command>"
    //   scan                       tiles, LODs, colliders, layout, NavMesh surfaces, sheets of the ACTIVE scene -> Out/scan-<scene>.json
    //   dry:<stage>                stage field + op boxes + tiles + in-memory re-sample with every surface assertion -> Out/scan-dry-<stage>-<scene>.json
    //   probe:<stage>[:max=N][:seg=ID]  face heights and capsule probes on the CURRENT physics ground -> Out/scan-probe-<stage>-<scene>.json
    //   probe:<stage>:overlay[...]      the same on the stage as it will stand: its re-sampled collider meshes as temporary in-memory colliders
    //   ...:walkablejump                what-if: the capsule may only jump from walkable support (the motor today jumps from any contact below)
    // Objects that carry HideFlags.DontSave (another session's in-memory preview) are ignored everywhere.
    public static class CliffScan308
    {
        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
                var cfg = CliffCore308.LoadConfig();
                var scene = SceneManager.GetActiveScene();
                switch (a[0])
                {
                    case "scan": return Scan(cfg, scene);
                    case "dry":
                        if (a.Length < 2 || a[1].Length == 0) throw new PostLedger308.Refused("use dry:<stage>");
                        return Dry(cfg, scene, a[1]);
                    case "probe":
                        if (a.Length < 2 || a[1].Length == 0) throw new PostLedger308.Refused("use probe:<stage>[:max=N][:seg=ID]");
                        return Probe(cfg, scene, a[1], PostLedger308.Options(a.Skip(2)));
                    default: throw new PostLedger308.Refused("unknown command '" + command + "' (scan | dry:<stage> | probe:<stage>)");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { return "refused: " + e.GetType().Name + ": " + e.Message; }
        }

        // ---------------------------------------------------------------- scan

        [Serializable] sealed class ScanLod { public string lod, guid, path, mesh, material; public long fileId; public bool active, readable, regular; public int vertices, indices, subMeshes, cutCells; }
        [Serializable] sealed class ScanTile { public string name; public float x, z; public bool active, colliderIsLod0, colliderEnabled, identity; public int layer; public string colliderPath; public float[] lodHeights; public ScanLod[] lods; }
        [Serializable] sealed class ScanNav { public string path, data, collect; public int layerMask; public bool active; }
        [Serializable] sealed class ScanSheet { public string renderer, sheet; public int placements; public bool enabled; }
        [Serializable] sealed class ScanFile
        {
            public string utc, scene, configSha256, terrainRoot, layout, layoutFinalSurface, layoutFinalSurfaceSha256, body, tileReferenceSha256, colliderSetSha256;
            public bool dirty; public int tiles, dontSaveRootsIgnored, otherChildren;
            public float bodyHeight, bodyRadius, bodyStep, bodySlope, bodySkin;
            public ScanTile[] tile; public ScanNav[] navMesh; public ScanSheet[] sheets; public string[] notes;
        }

        static string Scan(CliffCore308.Config cfg, Scene scene)
        {
            var notes = new List<string>();
            var root = CliffCore308.TerrainRoot(scene, cfg);
            var file = new ScanFile { utc = PostLedger308.Utc(), scene = scene.path, configSha256 = cfg.Sha256, terrainRoot = cfg.terrainRoot, dirty = scene.isDirty,
                dontSaveRootsIgnored = scene.GetRootGameObjects().Count(g => !CliffCore308.Saved(g)) };
            var tiles = new List<ScanTile>(); var refs = new StringBuilder(); int cutTiles = 0, irregular = 0, offLod0 = 0;
            for (int r = 0; r < cfg.tileRows; r++)
                for (int c = 0; c < cfg.tileCols; c++)
                {
                    var t = CliffCore308.FindTile(root, cfg, c, r, false);
                    if (t == null) { notes.Add("missing tile " + CliffCore308.TileName(cfg, c, r)); continue; }
                    var st = new ScanTile { name = t.Name, x = t.Tile.position.x, z = t.Tile.position.z, active = t.Tile.gameObject.activeInHierarchy, layer = t.Tile.gameObject.layer, identity = t.Identity,
                        colliderIsLod0 = t.ColliderIsLod0, colliderEnabled = t.Collider != null && t.Collider.enabled, colliderPath = t.Collider != null ? PostLedger308.AssetRef(t.Collider.sharedMesh) : "" };
                    var group = t.Tile.GetComponent<LODGroup>();
                    st.lodHeights = group != null ? group.GetLODs().Select(l => l.screenRelativeTransitionHeight).ToArray() : new float[0];
                    var lods = new List<ScanLod>();
                    foreach (var l in t.Lods)
                    {
                        var info = CliffCore308.Analyse(l.Mesh, l.Quads, cfg.tileSize);
                        var mr = l.Filter.GetComponent<MeshRenderer>();
                        lods.Add(new ScanLod { lod = l.Lod, guid = l.Guid, fileId = l.FileId, path = l.Path, mesh = l.Mesh != null ? l.Mesh.name : "", active = l.Filter.gameObject.activeSelf,
                            readable = l.Mesh != null && l.Mesh.isReadable, regular = info.Regular, vertices = info.Vertices, indices = info.Indices, subMeshes = info.SubMeshes, cutCells = info.CutCells.Count,
                            material = mr != null && mr.sharedMaterial != null ? PostLedger308.AssetRef(mr.sharedMaterial) : "" });
                        refs.Append(t.Name).Append('/').Append(l.Lod).Append('=').Append(l.Guid).Append(':').Append(l.FileId).Append(';');
                        if (!info.Regular) irregular++;
                        if (info.CutCells.Count > 0) { cutTiles++; notes.Add(t.Name + "/" + l.Lod + " has " + info.CutCells.Count + " cut cells (cave portal), first at world (" + (t.Tile.position.x + info.CutCells[0].x * info.Step).ToString("F0", Inv) + "," + (t.Tile.position.z + info.CutCells[0].y * info.Step).ToString("F0", Inv) + ")"); }
                    }
                    if (!t.ColliderIsLod0) offLod0++;
                    st.lods = lods.ToArray(); tiles.Add(st);
                }
            file.tile = tiles.ToArray(); file.tiles = tiles.Count; file.otherChildren = root.childCount - tiles.Count;
            file.tileReferenceSha256 = CliffCore308.Sha(Encoding.UTF8.GetBytes(refs.ToString()));
            file.colliderSetSha256 = CliffCore308.ColliderSet(cfg, root);

            var session = CliffCore308.Session(scene);
            if (session != null && session.MountainLayout != null)
            {
                file.layout = PostLedger308.AssetRef(session.MountainLayout);
                file.layoutFinalSurface = PostLedger308.AssetRef(session.MountainLayout.FinalSurface);
                if (session.MountainLayout.FinalSurface != null) file.layoutFinalSurfaceSha256 = CliffCore308.Sha(session.MountainLayout.FinalSurface.bytes);
            }
            else notes.Add("no WorldMacroPlaytestSession / MountainLayout in the scene");
            var body = session != null && session.Walker != null ? session.Walker.Body : null;
            if (body != null)
            {
                var s = body.transform.lossyScale;
                file.body = PostLedger308.PathOf(body.transform); file.bodyHeight = body.height * Mathf.Abs(s.y); file.bodyRadius = body.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                file.bodyStep = body.stepOffset; file.bodySlope = body.slopeLimit; file.bodySkin = body.skinWidth;
            }
            file.navMesh = PostLedger308.All<NavMeshSurface>(scene).Where(n => CliffCore308.Saved(n.gameObject)).Select(n => new ScanNav { path = PostLedger308.PathOf(n.transform), data = PostLedger308.AssetRef(n.navMeshData),
                collect = n.collectObjects.ToString(), layerMask = n.layerMask.value, active = n.isActiveAndEnabled }).ToArray();
            file.sheets = PostLedger308.All<CompactRebuildArtRenderer>(scene).Where(n => CliffCore308.Saved(n.gameObject)).Select(n => new ScanSheet { renderer = PostLedger308.PathOf(n.transform), sheet = PostLedger308.AssetRef(n.Sheet),
                placements = n.Sheet != null && n.Sheet.FixedPlacements != null ? n.Sheet.FixedPlacements.Length : 0, enabled = n.isActiveAndEnabled }).ToArray();
            file.notes = notes.ToArray();
            string outFile = CliffCore308.OutFile(cfg, "scan-" + PostLedger308.Short(scene.path) + ".json");
            CliffCore308.WriteText(outFile, JsonUtility.ToJson(file, true));
            return "scan " + scene.path + ": tiles " + tiles.Count + "/" + cfg.tileCols * cfg.tileRows + " (other children under " + cfg.terrainRoot + " " + file.otherChildren + "), irregular LOD meshes " + irregular + ", LOD meshes with cut cells " + cutTiles
                + ", colliders not on " + cfg.colliderLod + " " + offLod0 + ", tile reference sha " + file.tileReferenceSha256.Substring(0, 12) + ", collider set sha " + file.colliderSetSha256.Substring(0, 12) + ", layout " + file.layout + " -> " + file.layoutFinalSurface
                + " (sha " + Short(file.layoutFinalSurfaceSha256) + "), NavMesh surfaces " + file.navMesh.Length + ", sheets " + file.sheets.Length + ", DontSave roots ignored " + file.dontSaveRootsIgnored + ", scene dirty=" + scene.isDirty + " | " + outFile;
        }

        // ---------------------------------------------------------------- dry

        static string Dry(CliffCore308.Config cfg, Scene scene, string stageId)
        {
            var stage = CliffCore308.LoadStage(cfg, stageId, false);
            var build = CliffCore308.BuildSurface(cfg, scene, stage, null, null);
            string outFile = CliffCore308.OutFile(cfg, "scan-dry-" + stageId + "-" + PostLedger308.Short(scene.path) + ".json");
            build.utc = PostLedger308.Utc();
            // the scene's physics ground against BOTH fields: it stands on the base before `tiles`, on the stage after
            var root = CliffCore308.TerrainRoot(scene, cfg);
            var onBase = CliffCore308.GroundAgainst(cfg, root, stage, stage.Base, "base"); var onStage = CliffCore308.GroundAgainst(cfg, root, stage, stage.New, "stage");
            CliffCore308.WriteText(outFile, JsonUtility.ToJson(new DryFile { surface = build, groundVsBase = onBase, groundVsStage = onStage }, true));
            return "dry " + CliffCore308.Summary(build) + " | physics ground at " + onBase.samples + " changed cells: vs base field max |d| " + onBase.max.ToString("F3", Inv) + " m (over " + onBase.tolerance.ToString("0.###", Inv) + " m: " + onBase.over
                + ", no terrain " + onBase.missing + "), vs stage field over " + onStage.over + " -> the scene stands on the " + (onBase.over == 0 && onBase.missing == 0 ? "BASE" : onStage.over == 0 && onStage.missing == 0 ? "STAGE" : "NEITHER") + " field"
                + " | nothing written but " + outFile + " | scene dirty=" + scene.isDirty;
        }

        [Serializable] sealed class DryFile { public CliffCore308.SurfaceBuild surface; public CliffCore308.GroundCheck groundVsBase, groundVsStage; }

        // ---------------------------------------------------------------- probe

        static string Probe(CliffCore308.Config cfg, Scene scene, string stageId, Dictionary<string, string> opt)
        {
            var data = CliffCore308.LoadCheckData(cfg, stageId);
            int max = opt.TryGetValue("max", out var m) && int.TryParse(m, NumberStyles.Integer, Inv, out int mv) ? mv : int.MaxValue;
            opt.TryGetValue("seg", out string only);
            CliffCore308.ProbeReport report; string head = "", tag = "";
            bool whatIf = opt.ContainsKey("walkablejump");
            if (whatIf) cfg.probe.jumpNeedsWalkable = true;   // this call only: the config object is not written back
            if (opt.ContainsKey("overlay"))
            {
                // the stage as it WILL stand: its re-sampled collider meshes as temporary colliders over the untouched scene
                var stage = CliffCore308.LoadStage(cfg, stageId, false);
                using (var overlay = CliffCore308.BuildOverlay(cfg, scene, stage))
                {
                    var ground = new CliffCore308.Ground { Root = CliffCore308.TerrainRoot(scene, cfg), Overlay = overlay.Root.transform };
                    var onStage = CliffCore308.GroundAgainst(cfg, ground, stage, stage.New, "stage");
                    report = CliffCore308.ProbeLines(cfg, scene, data, max, only, overlay.Root.transform, stage);
                    head = "OVERLAY of stage " + stageId + " (" + overlay.Colliders + " temporary colliders, " + overlay.Meshes.Count + " in-memory meshes, destroyed; physics vs stage field at " + onStage.samples + " cells max |d| "
                        + onStage.max.ToString("F3", Inv) + " m, over " + onStage.over + ", no terrain " + onStage.missing + ") | "; tag = "-overlay";
                    report.notes = (report.notes ?? new string[0]).Concat(new[] { "overlay: " + head, "forest shells and vegetation are those of the scene as it is (Enclosure305 build-scene has not run on this ground)" }).ToArray();
                }
            }
            else
            {
                CliffCore308.Stage stage = null;
                try { stage = CliffCore308.LoadStage(cfg, stageId, false); } catch (PostLedger308.Refused) { }
                report = CliffCore308.ProbeLines(cfg, scene, data, max, only, null, stage);
            }
            report.utc = PostLedger308.Utc(); report.scene = scene.path;
            // lines from an older ops file or points from an older plan file: said in the report and in the answer, the verdict is labelled
            CliffCore308.Stage staleStage = null; try { staleStage = CliffCore308.LoadStage(cfg, stageId, false); } catch (PostLedger308.Refused) { }
            string stale = CliffCore308.StaleCheckData(data, staleStage);
            if (stale != null) { report.notes = (report.notes ?? new string[0]).Concat(new[] { "STALE CHECK DATA: " + stale }).ToArray(); report.verdict += " (STALE CHECK DATA)"; }
            string outFile = CliffCore308.OutFile(cfg, "scan-probe-" + stageId + "-" + PostLedger308.Short(scene.path) + tag + (whatIf ? "-walkablejump" : "") + ".json");
            CliffCore308.WriteText(outFile, JsonUtility.ToJson(report, true));
            return "probe " + head + CliffCore308.Summary(report) + (stale != null ? " | STALE CHECK DATA: " + stale : "") + " | " + outFile + " | scene dirty=" + scene.isDirty;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length));
    }

    internal static class CliffCore308
    {
        // the one path the code knows; every other path and number is data in that file
        internal const string ConfigPath = "Art/World/Compact/Rebuild/CliffBoundary308/Data/cb308.json";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- config

        [Serializable] internal sealed class LodSpec { public string name; public int quads; }
        [Serializable] internal sealed class SceneSpec { public string key, path; }
        [Serializable] internal sealed class VegSpec { public string stage; public float removeSlopeDeg; public string[] sheets; }
        [Serializable] internal sealed class Variant { public string name; public bool jump; public float guk, nominalRise; }
        [Serializable] internal sealed class ProbeSpec
        {
            public float every, push, failPast, tick; public float[] startBack; public int stallTicks, extraTicks; public bool useMotorValues, jumpNeedsWalkable;
            public float speed, gravity, jumpHeight, groundStick, groundSnap, capsuleHeight, capsuleRadius, capsuleStep, capsuleSlope, capsuleSkin, fallLimit;
            public Variant[] variants; public float endBack, endPast, endReach;
            public float[] gainOffsets; public float gainSeconds, gainLimit, gainMinSlopeDeg, gainMaxSlopeDeg, climbSeconds;
            public int groundSamples; public float groundTolerance;
            // walker / probe geometry (data, not code): headings tried at a joint or point (degrees off the line normal), how far a rise-gain
            // run is pushed, how close counts as a waypoint reached, how far above the ground a run starts, the pad of the "touching" query,
            // the number of moves a guk lift is split into
            public float[] markTurns; public float gainPush, waypointReach, startLift, touchPad; public int gukSteps;
        }
        [Serializable] internal sealed class Grammar
        {
            public float faceEvery, faceLower, faceUpper, faceWindow, faceWithin, d305Floor, noFaceMin, noFaceMax, liftMin, liftMax, flankMax, flankLength, innerMin, innerMedian, outerFirstStep;
            // the plan tools' face window (review308 / eval): upper 0..faceUpper m max - lower planLowerNear..planLowerFar m min, every planStep m
            public float planLowerNear, planLowerFar, planStep;
        }
        [Serializable] internal sealed class Config
        {
            public string version; public int width, height; public float cell;
            public string baseHeight, baseHeightSha256, stageDir, stageFile, stageReport, stageTiles, checkData, assetRoot, outDir, terrainRoot, tileNameFormat, colliderLod;
            public SceneSpec[] scenes; public float tileSize; public int tileCols, tileRows; public LodSpec[] lods;
            public float changedEps, boxBlock, boxMargin, seamTolerance, cutMargin;
            public VegSpec[] veg; public ProbeSpec probe; public Grammar grammar; public string[] knownRedRegressions, phase1b;
            [NonSerialized] public string Sha256;
        }

        internal static Config LoadConfig()
        {
            string abs = PostLedger308.RepoPath(ConfigPath);
            if (!File.Exists(abs)) throw new PostLedger308.Refused("config missing: " + ConfigPath);
            var bytes = File.ReadAllBytes(abs);
            var cfg = JsonUtility.FromJson<Config>(Encoding.UTF8.GetString(bytes));
            if (cfg == null || cfg.lods == null || cfg.lods.Length == 0 || cfg.scenes == null || cfg.scenes.Length == 0 || cfg.width < 2 || cfg.height < 2 || cfg.cell <= 0f || cfg.tileSize <= 0f
                || cfg.probe == null || cfg.probe.variants == null || cfg.grammar == null || string.IsNullOrEmpty(cfg.assetRoot) || string.IsNullOrEmpty(cfg.outDir) || string.IsNullOrEmpty(cfg.terrainRoot))
                throw new PostLedger308.Refused("config " + ConfigPath + " is incomplete");
            var pr = cfg.probe; var gr = cfg.grammar;
            if (pr.markTurns == null || pr.markTurns.Length == 0 || pr.gainPush <= 0f || pr.waypointReach <= 0f || pr.startLift <= 0f || pr.touchPad <= 0f || pr.gukSteps < 1 || pr.startBack == null || pr.startBack.Length == 0 || pr.tick <= 0f
                || gr.planStep < 1f || gr.planLowerFar < gr.planLowerNear || gr.planLowerNear < 0f)
                throw new PostLedger308.Refused("config " + ConfigPath + " lacks probe.markTurns / gainPush / waypointReach / startLift / touchPad / gukSteps or grammar.planLowerNear / planLowerFar / planStep");
            if (PostLedger308.IsProtectedPath(cfg.assetRoot + "/")) throw new PostLedger308.Refused("config assetRoot is a protected path: " + cfg.assetRoot);
            cfg.Sha256 = Sha(bytes);
            return cfg;
        }

        /// <summary>Target scene path of a token (key or path). Unknown and protected scenes are refused.</summary>
        internal static string ScenePath(Config cfg, string token)
        {
            var hit = cfg.scenes.FirstOrDefault(s => string.Equals(s.key, token, StringComparison.OrdinalIgnoreCase) || s.path == token);
            if (hit == null) throw new PostLedger308.Refused("unknown scene '" + token + "' (one of " + string.Join(", ", cfg.scenes.Select(s => s.key)) + ")");
            if (PostLedger308.IsProtectedPath(hit.path)) throw new PostLedger308.Refused("protected path " + hit.path);
            if (!PostLedger308.Scenes.Contains(hit.path)) throw new PostLedger308.Refused("not a #308 target scene: " + hit.path);
            return hit.path;
        }

        internal static string SceneKey(Config cfg, string path)
        {
            var hit = cfg.scenes.FirstOrDefault(s => s.path == path);
            return hit != null ? hit.key : null;
        }

        internal static string OutFile(Config cfg, string name)
        {
            string dir = PostLedger308.RepoPath(cfg.outDir); Directory.CreateDirectory(dir);
            return Path.Combine(dir, name).Replace('\\', '/');
        }

        internal static void WriteText(string path, string text) => File.WriteAllText(path, text.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));

        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static string ShaFile(string absolute) => File.Exists(absolute) ? Sha(File.ReadAllBytes(absolute)) : "";

        internal static bool Saved(GameObject g) => (g.hideFlags & HideFlags.DontSave) == 0;

        internal static WorldMacroPlaytestSession Session(Scene scene) => PostLedger308.All<WorldMacroPlaytestSession>(scene).FirstOrDefault(s => Saved(s.gameObject));

        // ---------------------------------------------------------------- stage height field

        [Serializable] internal sealed class BoxData { public float[] min, max; }
        // the terrain track's report next to the stage field (Tools/Art/scarp308.py); unknown keys are ignored, tiles / boxes are optional
        [Serializable] internal sealed class StageReport { public string stage, @base, base_sha256, ops, ops_sha256, ops_version, @out, out_sha256; public string[] tiles; public BoxData[] boxes; }
        // the offline evaluation that names the tiles of a stage (plan/Stage/FORMAT.md: plan/eval_v5.json tiles.list)
        [Serializable] internal sealed class TileList { public string[] list; }
        [Serializable] internal sealed class StageTiles { public string stage, height_sha256; public TileList tiles; }

        internal sealed class Stage
        {
            public string Id, HeightFile, ReportFile, HeightSha, BaseSha, OpsSha, OpsVersion, OpsPath, BoxSource, BoxSha, TileSource = "";
            public bool HasReport, Prototype;
            public byte[] Bytes; public float[] New, Base; public CompactWorldSurface Field, BaseField;
            public int ChangedCells; public float MaxRaise, MaxCut;
            public List<Rect> Boxes = new List<Rect>(); public string[] ReportTiles;
        }

        internal static string StageHeightPath(Config cfg, string id) => PostLedger308.RepoPath(cfg.stageDir + "/" + cfg.stageFile.Replace("{stage}", id));

        /// <summary>Reads plan/Stage/height_p&lt;stage&gt;.bytes (float32 LE, height rows (z / cell) x width columns (x / cell), row-major, no header) and
        /// its report, checks every recorded sha and derives the op boxes. forWrite refuses a prototype / report-less field.</summary>
        internal static Stage LoadStage(Config cfg, string id, bool forWrite)
        {
            if (id.IndexOfAny(new[] { '/', '\\', '.', ' ' }) >= 0) throw new PostLedger308.Refused("bad stage id '" + id + "'");
            var s = new Stage { Id = id, HeightFile = StageHeightPath(cfg, id), ReportFile = PostLedger308.RepoPath(cfg.stageDir + "/" + cfg.stageReport.Replace("{stage}", id)) };
            if (!File.Exists(s.HeightFile)) throw new PostLedger308.Refused("no stage height field " + s.HeightFile + " (terrain track: python Tools/Art/scarp308.py <ops> --stage " + id + ")");
            s.Bytes = File.ReadAllBytes(s.HeightFile);
            long expect = (long)cfg.width * cfg.height * 4;
            if (s.Bytes.Length != expect) throw new PostLedger308.Refused("stage field is " + s.Bytes.Length + " bytes, expected " + expect + " (float32 " + cfg.height + " x " + cfg.width + ", no header)");
            s.HeightSha = Sha(s.Bytes);
            string side = s.HeightFile + ".sha256";
            if (File.Exists(side))
            {
                string recorded = File.ReadAllText(side).Trim().Split(' ')[0].ToLowerInvariant();
                if (recorded != s.HeightSha) throw new PostLedger308.Refused("stage field sha " + s.HeightSha + " != its .sha256 sidecar " + recorded);
            }
            s.Prototype = id.IndexOf("PROTOTYPE", StringComparison.OrdinalIgnoreCase) >= 0;
            s.HasReport = File.Exists(s.ReportFile);

            string baseAbs = PostLedger308.Abs(cfg.baseHeight);
            if (!File.Exists(baseAbs)) throw new PostLedger308.Refused("base height missing: " + cfg.baseHeight);
            var baseBytes = File.ReadAllBytes(baseAbs);
            s.BaseSha = Sha(baseBytes);
            if (baseBytes.Length != expect) throw new PostLedger308.Refused("base height is " + baseBytes.Length + " bytes, expected " + expect);
            if (s.BaseSha != cfg.baseHeightSha256) throw new PostLedger308.Refused("base height sha " + s.BaseSha + " != config baseHeightSha256 " + cfg.baseHeightSha256 + " (the stage must be re-made from the base)");
            if (s.HasReport)
            {
                var rep = JsonUtility.FromJson<StageReport>(File.ReadAllText(s.ReportFile, Encoding.UTF8));
                if (!string.IsNullOrEmpty(rep.base_sha256) && rep.base_sha256 != s.BaseSha) throw new PostLedger308.Refused("report base_sha256 " + rep.base_sha256 + " != base height " + s.BaseSha);
                if (!string.IsNullOrEmpty(rep.out_sha256) && rep.out_sha256 != s.HeightSha) throw new PostLedger308.Refused("report out_sha256 " + rep.out_sha256 + " != stage field " + s.HeightSha);
                if (!string.IsNullOrEmpty(rep.stage) && rep.stage != id) throw new PostLedger308.Refused("report stage '" + rep.stage + "' != requested '" + id + "'");
                s.OpsSha = rep.ops_sha256 ?? ""; s.OpsVersion = rep.ops_version ?? ""; s.OpsPath = rep.ops ?? ""; s.ReportTiles = rep.tiles;
                if (rep.boxes != null && rep.boxes.Length > 0)
                {
                    foreach (var b in rep.boxes)
                    {
                        if (b == null || b.min == null || b.max == null || b.min.Length != 2 || b.max.Length != 2) throw new PostLedger308.Refused("report box without min/max [x,z]");
                        s.Boxes.Add(Rect.MinMaxRect(b.min[0], b.min[1], b.max[0], b.max[1]));
                    }
                    s.BoxSource = "report";
                }
            }
            if (s.ReportTiles != null && s.ReportTiles.Length > 0) s.TileSource = s.ReportFile;
            else if (!string.IsNullOrEmpty(cfg.stageTiles) && File.Exists(PostLedger308.RepoPath(cfg.stageTiles)))
            {
                // only a tile list made for THIS field counts; one for another stage or an older field is ignored (and said so)
                var named = JsonUtility.FromJson<StageTiles>(File.ReadAllText(PostLedger308.RepoPath(cfg.stageTiles), Encoding.UTF8));
                bool forThis = named != null && named.tiles != null && named.tiles.list != null && named.tiles.list.Length > 0 && named.height_sha256 == s.HeightSha && (string.IsNullOrEmpty(named.stage) || named.stage == id);
                if (forThis) { s.ReportTiles = named.tiles.list; s.TileSource = cfg.stageTiles; }
                else s.TileSource = "none (" + cfg.stageTiles + " is not for this field)";
            }
            if (forWrite)
            {
                if (s.Prototype) throw new PostLedger308.Refused("stage '" + id + "' is a prototype field (not for import, Spec 데이터·파일)");
                if (!s.HasReport || string.IsNullOrEmpty(s.OpsSha)) throw new PostLedger308.Refused("stage '" + id + "' has no report with ops_sha256 (" + s.ReportFile + "): the ledger records base sha + ops sha");
            }

            int n = cfg.width * cfg.height;
            s.New = new float[n]; s.Base = new float[n];
            Buffer.BlockCopy(s.Bytes, 0, s.New, 0, n * 4); Buffer.BlockCopy(baseBytes, 0, s.Base, 0, n * 4);
            s.Field = new CompactWorldSurface(cfg.width, cfg.height, cfg.cell, s.Bytes);
            s.BaseField = new CompactWorldSurface(cfg.width, cfg.height, cfg.cell, baseBytes);

            // changed lattice vertices and, per boxBlock, the tight box around them (the op boxes when the report names none)
            int block = Mathf.Max(1, Mathf.RoundToInt(cfg.boxBlock / cfg.cell));
            var blocks = new Dictionary<long, Vector4>();   // min i, min j, max i, max j
            for (int i = 0; i < cfg.height; i++)
                for (int j = 0; j < cfg.width; j++)
                {
                    float d = s.New[i * cfg.width + j] - s.Base[i * cfg.width + j];
                    if (Mathf.Abs(d) <= cfg.changedEps) continue;
                    s.ChangedCells++; if (d > s.MaxRaise) s.MaxRaise = d; if (-d > s.MaxCut) s.MaxCut = -d;
                    long key = ((long)(i / block) << 32) ^ (uint)(j / block);
                    if (!blocks.TryGetValue(key, out var b)) b = new Vector4(i, j, i, j);
                    else b = new Vector4(Mathf.Min(b.x, i), Mathf.Min(b.y, j), Mathf.Max(b.z, i), Mathf.Max(b.w, j));
                    blocks[key] = b;
                }
            if (s.ChangedCells == 0) throw new PostLedger308.Refused("stage field equals the base (0 changed vertices)");
            if (s.Boxes.Count == 0)
            {
                foreach (var kv in blocks.OrderBy(k => k.Key)) s.Boxes.Add(Rect.MinMaxRect(kv.Value.y * cfg.cell, kv.Value.x * cfg.cell, kv.Value.w * cfg.cell, kv.Value.z * cfg.cell));
                s.BoxSource = "derived (" + cfg.boxBlock.ToString("0.#", Inv) + " m blocks of changed vertices)";
            }
            else
            {
                // boxes named by the report must cover every changed vertex: a vertex left at its old height beside a raised one is a crack
                var index = new BoxIndex(s.Boxes, 0f); int outside = 0;
                for (int i = 0; i < cfg.height; i++)
                    for (int j = 0; j < cfg.width; j++)
                        if (Mathf.Abs(s.New[i * cfg.width + j] - s.Base[i * cfg.width + j]) > cfg.changedEps && !index.Contains(j * cfg.cell, i * cfg.cell)) outside++;
                if (outside > 0) throw new PostLedger308.Refused(outside + " changed vertices lie outside the report's op boxes");
            }
            var sb = new StringBuilder();
            foreach (var b in s.Boxes) sb.Append(b.xMin.ToString("R", Inv)).Append(',').Append(b.yMin.ToString("R", Inv)).Append(',').Append(b.xMax.ToString("R", Inv)).Append(',').Append(b.yMax.ToString("R", Inv)).Append(';');
            s.BoxSha = Sha(Encoding.UTF8.GetBytes(sb.ToString()));
            return s;
        }

        /// <summary>Height of the tile MESH surface over a field: each cell = triangles (00, 01, 10) and (10, 01, 11), as the LOD0 tiles are built.</summary>
        internal static float TriSample(float[] h, int width, int height, float cell, float x, float z)
        {
            float fx = Mathf.Clamp(x / cell, 0f, width - 1.0001f), fz = Mathf.Clamp(z / cell, 0f, height - 1.0001f);
            int j = (int)fx, i = (int)fz; fx -= j; fz -= i;
            float h00 = h[i * width + j], h10 = h[i * width + j + 1], h01 = h[(i + 1) * width + j], h11 = h[(i + 1) * width + j + 1];
            return fx + fz <= 1f ? h00 + (h10 - h00) * fx + (h01 - h00) * fz : h11 + (h01 - h11) * (1f - fx) + (h10 - h11) * (1f - fz);
        }

        /// <summary>Slope of the field in degrees (central differences over one cell, the plan's slope_deg).</summary>
        internal static float SlopeDeg(CompactWorldSurface f, float x, float z)
        {
            float gx = (f.Sample(x + f.Cell, z) - f.Sample(x - f.Cell, z)) / (2f * f.Cell), gz = (f.Sample(x, z + f.Cell) - f.Sample(x, z - f.Cell)) / (2f * f.Cell);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        /// <summary>Op boxes grown by a margin, bucketed for point queries. Borders count as inside.</summary>
        internal sealed class BoxIndex
        {
            const float Bucket = 64f;
            readonly List<Rect> grown = new List<Rect>(); readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
            public int Count => grown.Count;
            public IReadOnlyList<Rect> Grown => grown;
            public BoxIndex(List<Rect> boxes, float margin)
            {
                foreach (var b in boxes)
                {
                    var g = Rect.MinMaxRect(b.xMin - margin, b.yMin - margin, b.xMax + margin, b.yMax + margin); int id = grown.Count; grown.Add(g);
                    for (int bx = Mathf.FloorToInt(g.xMin / Bucket); bx <= Mathf.FloorToInt(g.xMax / Bucket); bx++)
                        for (int bz = Mathf.FloorToInt(g.yMin / Bucket); bz <= Mathf.FloorToInt(g.yMax / Bucket); bz++)
                        {
                            long key = ((long)bx << 32) ^ (uint)bz;
                            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                            list.Add(id);
                        }
                }
            }
            public bool Contains(float x, float z)
            {
                long key = ((long)Mathf.FloorToInt(x / Bucket) << 32) ^ (uint)Mathf.FloorToInt(z / Bucket);
                if (!buckets.TryGetValue(key, out var list)) return false;
                foreach (int id in list) { var g = grown[id]; if (x >= g.xMin && x <= g.xMax && z >= g.yMin && z <= g.yMax) return true; }
                return false;
            }
            public bool Overlaps(float x0, float z0, float x1, float z1)
            {
                foreach (var g in grown) if (g.xMin <= x1 && g.xMax >= x0 && g.yMin <= z1 && g.yMax >= z0) return true;
                return false;
            }
        }

        // ---------------------------------------------------------------- tiles

        internal sealed class LodRef { public string Lod, Guid, Path; public long FileId; public int Quads; public MeshFilter Filter; public Mesh Mesh; }
        internal sealed class TileRef
        {
            public int Col, Row; public string Name; public Transform Tile; public List<LodRef> Lods = new List<LodRef>(); public MeshCollider Collider; public bool ColliderIsLod0, Identity;
            public LodRef Lod(string name) => Lods.FirstOrDefault(l => l.Lod == name);
        }

        internal static string TileName(Config cfg, int col, int row) => string.Format(Inv, cfg.tileNameFormat, col, row);
        /// <summary>"x4_z3" (the offline tools' tile key) -> column, row.</summary>
        internal static bool ParseTileKey(string key, out int col, out int row)
        {
            col = row = -1; if (string.IsNullOrEmpty(key)) return false;
            var p = key.Split('_');
            return p.Length == 2 && p[0].StartsWith("x", StringComparison.Ordinal) && p[1].StartsWith("z", StringComparison.Ordinal)
                && int.TryParse(p[0].Substring(1), NumberStyles.Integer, Inv, out col) && int.TryParse(p[1].Substring(1), NumberStyles.Integer, Inv, out row);
        }

        internal static Transform TerrainRoot(Scene scene, Config cfg)
        {
            var roots = scene.GetRootGameObjects().Where(g => g.name == cfg.terrainRoot && Saved(g)).ToArray();
            if (roots.Length != 1) throw new PostLedger308.Refused(roots.Length + " roots named " + cfg.terrainRoot + " in " + scene.path + " (expected 1)");
            return roots[0].transform;
        }

        internal static void MeshIdentity(Mesh mesh, out string guid, out long fileId, out string path)
        {
            guid = ""; fileId = 0; path = "";
            if (mesh == null) return;
            path = AssetDatabase.GetAssetPath(mesh);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out fileId)) { guid = ""; fileId = 0; }
        }

        /// <summary>Loads the mesh a recorded reference names (GUID + fileID), whatever its path is today.</summary>
        internal static Mesh LoadMesh(string guid, long fileId)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Mesh m && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string g, out long id) && g == guid && id == fileId) return m;
            return null;
        }

        internal static TileRef FindTile(Transform root, Config cfg, int col, int row, bool strict)
        {
            string name = TileName(cfg, col, row);
            Transform tile = null; int found = 0;
            foreach (Transform c in root) if (c.name == name && Saved(c.gameObject)) { tile = c; found++; }
            if (found != 1) { if (strict) throw new PostLedger308.Refused(found + " objects named " + name + " under " + cfg.terrainRoot); return null; }
            var t = new TileRef { Col = col, Row = row, Name = name, Tile = tile, Collider = tile.GetComponent<MeshCollider>() };
            t.Identity = tile.rotation == Quaternion.identity && tile.lossyScale == Vector3.one;
            foreach (var spec in cfg.lods)
            {
                var child = tile.Find(spec.name); var filter = child != null ? child.GetComponent<MeshFilter>() : null;
                if (filter == null) { if (strict) throw new PostLedger308.Refused(name + " has no " + spec.name + " MeshFilter"); continue; }
                var l = new LodRef { Lod = spec.name, Quads = spec.quads, Filter = filter, Mesh = filter.sharedMesh };
                MeshIdentity(l.Mesh, out l.Guid, out l.FileId, out l.Path);
                if (child.localPosition != Vector3.zero || child.localRotation != Quaternion.identity || child.localScale != Vector3.one) t.Identity = false;
                t.Lods.Add(l);
            }
            var collide = t.Lod(cfg.colliderLod);
            t.ColliderIsLod0 = t.Collider != null && collide != null && t.Collider.sharedMesh == collide.Mesh;
            if (strict)
            {
                if (!t.Identity) throw new PostLedger308.Refused(name + " or one of its LOD children is rotated, scaled or offset");
                if (Mathf.Abs(tile.position.x - col * cfg.tileSize) > .001f || Mathf.Abs(tile.position.z - row * cfg.tileSize) > .001f) throw new PostLedger308.Refused(name + " is not at (" + col * cfg.tileSize + "," + row * cfg.tileSize + ")");
                if (t.Collider == null) throw new PostLedger308.Refused(name + " has no MeshCollider");
            }
            return t;
        }

        /// <summary>Tiles whose 500 m square touches a grown op box (shared edges belong to both tiles).</summary>
        internal static List<Vector2Int> TilesOf(Config cfg, BoxIndex boxes)
        {
            var list = new List<Vector2Int>();
            for (int r = 0; r < cfg.tileRows; r++)
                for (int c = 0; c < cfg.tileCols; c++)
                    if (boxes.Overlaps(c * cfg.tileSize, r * cfg.tileSize, (c + 1) * cfg.tileSize, (r + 1) * cfg.tileSize)) list.Add(new Vector2Int(c, r));
            return list;
        }

        internal sealed class GridInfo { public bool Regular; public int Vertices, Indices, SubMeshes; public float Step; public List<Vector2Int> CutCells = new List<Vector2Int>(); public int Overfull; }

        /// <summary>Topology of a tile LOD mesh: (quads + 1)^2 lattice vertices, 2 triangles per quad. Quads with fewer are cuts (cave portal).</summary>
        internal static GridInfo Analyse(Mesh mesh, int quads, float tileSize)
        {
            var g = new GridInfo { Step = tileSize / quads };
            if (mesh == null) return g;
            g.Vertices = mesh.vertexCount; g.SubMeshes = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) g.Indices += (int)mesh.GetIndexCount(s);
            g.Regular = g.Vertices == (quads + 1) * (quads + 1);
            if (!mesh.isReadable) { g.Regular = false; return g; }
            var v = mesh.vertices; var t = mesh.triangles; var count = new byte[quads * quads];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                int qx = Mathf.Clamp(Mathf.FloorToInt(c.x / g.Step), 0, quads - 1), qz = Mathf.Clamp(Mathf.FloorToInt(c.z / g.Step), 0, quads - 1);
                if (count[qz * quads + qx] < 255) count[qz * quads + qx]++;
            }
            for (int qz = 0; qz < quads; qz++)
                for (int qx = 0; qx < quads; qx++)
                {
                    if (count[qz * quads + qx] < 2) g.CutCells.Add(new Vector2Int(qx, qz));
                    else if (count[qz * quads + qx] > 2) g.Overfull++;
                }
            if (g.Overfull > 0) g.Regular = false;
            return g;
        }

        internal static string MeshContentSha(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles;
            var bytes = new byte[v.Length * 12 + t.Length * 4]; var f = new float[v.Length * 3];
            for (int i = 0; i < v.Length; i++) { f[i * 3] = v[i].x; f[i * 3 + 1] = v[i].y; f[i * 3 + 2] = v[i].z; }
            Buffer.BlockCopy(f, 0, bytes, 0, f.Length * 4); Buffer.BlockCopy(t, 0, bytes, f.Length * 4, t.Length * 4);
            return Sha(bytes);
        }

        // ---------------------------------------------------------------- surface build (in memory; a sink turns it into assets)

        [Serializable] internal sealed class MeshRecord { public string guid, path, name, fileSha256, contentSha256; public long fileId; public int vertices, indices, subMeshes; }
        [Serializable] internal sealed class LodRecord { public string lod; public MeshRecord source, mesh; public int changedVertices, outsideMismatch, cutCells; public float outsideWorst, maxY, minY; }
        [Serializable] internal sealed class TileRecord { public string tile, key; public float x, z; public LodRecord[] lods; public bool sourceFromLedger; }
        [Serializable] internal sealed class SurfaceBuild
        {
            public string format = "cb308.surface.1", utc, stage, scene, configSha256, baseSha256, opsSha256, opsVersion, heightSha256, heightSource, heightAsset, boxSource, boxSha256, tileSource;
            public int boxes, changedLatticeVertices, tiles, changedVertices, outsideMismatch; public float boxMargin, maxRaise, maxCut, outsideWorst;
            public bool written; public string[] tileKeys, notes; public TileRecord[] tile;
        }

        internal static MeshRecord Record(Mesh mesh, bool hashFile)
        {
            var r = new MeshRecord();
            if (mesh == null) return r;
            MeshIdentity(mesh, out r.guid, out r.fileId, out r.path); r.name = mesh.name; r.vertices = mesh.vertexCount; r.subMeshes = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) r.indices += (int)mesh.GetIndexCount(s);
            if (hashFile && !string.IsNullOrEmpty(r.path)) r.fileSha256 = ShaFile(PostLedger308.Abs(r.path));
            if (mesh.isReadable) r.contentSha256 = MeshContentSha(mesh);
            return r;
        }

        /// <summary>The re-sample of one stage: for every tile an op box (+ margin) touches and every LOD, a copy of the SOURCE mesh whose
        /// vertices inside the boxes take the stage field's height and normal. Topology, submeshes and UV stay; counts are asserted; a box
        /// over a cave-portal cut is refused. originals (tile path/LOD -> recorded pre-#308 mesh) overrides "what the filter references now"
        /// for a scene that already carries a #308 stage. sink == null keeps everything in memory (dry) and destroys the copies.</summary>
        internal static SurfaceBuild BuildSurface(Config cfg, Scene scene, Stage stage, Func<string, Mesh> originals, Func<TileRef, LodRef, Mesh, Mesh> sink)
        {
            var root = TerrainRoot(scene, cfg);
            var boxes = new BoxIndex(stage.Boxes, cfg.boxMargin);
            var keys = TilesOf(cfg, boxes);
            var build = new SurfaceBuild { stage = stage.Id, scene = scene.path, configSha256 = cfg.Sha256, baseSha256 = stage.BaseSha, opsSha256 = stage.OpsSha ?? "", opsVersion = stage.OpsVersion ?? "", heightSha256 = stage.HeightSha,
                heightSource = stage.HeightFile, boxSource = stage.BoxSource, boxSha256 = stage.BoxSha, boxes = stage.Boxes.Count, boxMargin = cfg.boxMargin, changedLatticeVertices = stage.ChangedCells, maxRaise = stage.MaxRaise, maxCut = stage.MaxCut,
                written = sink != null };
            var notes = new List<string>();
            build.tileKeys = keys.Select(k => "x" + k.x + "_z" + k.y).ToArray();
            if (stage.ReportTiles != null && stage.ReportTiles.Length > 0)
            {
                var named = new HashSet<string>(stage.ReportTiles); var mine = new HashSet<string>(build.tileKeys);
                if (!named.SetEquals(mine)) throw new PostLedger308.Refused("the stage data (" + stage.TileSource + ") names tiles [" + string.Join(" ", named.OrderBy(x => x)) + "] but the op boxes (+" + cfg.boxMargin.ToString("0.#", Inv) + " m) touch [" + string.Join(" ", mine.OrderBy(x => x)) + "]");
                notes.Add("tile list equals the one named by " + stage.TileSource);
            }
            else notes.Add("no tile list for this field in the stage data (" + stage.TileSource + "); the list is derived from the op boxes");
            build.tileSource = stage.TileSource;

            // pass 1: every refusal before the first copy
            var tiles = new List<TileRef>(); var sources = new Dictionary<LodRef, Mesh>(); var fromLedger = new HashSet<TileRef>();
            foreach (var k in keys)
            {
                var t = FindTile(root, cfg, k.x, k.y, true); tiles.Add(t);
                foreach (var l in t.Lods)
                {
                    Mesh source = originals != null ? originals(t.Name + "/" + l.Lod) : null;
                    if (source != null) fromLedger.Add(t); else source = l.Mesh;
                    if (source == null) throw new PostLedger308.Refused(t.Name + "/" + l.Lod + " references no mesh");
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    if (sourcePath.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal)) throw new PostLedger308.Refused(t.Name + "/" + l.Lod + " already references a #308 mesh (" + sourcePath + ") and the scene ledger records no original for it");
                    if (!source.isReadable) throw new PostLedger308.Refused(sourcePath + " is not readable");
                    var info = Analyse(source, l.Quads, cfg.tileSize);
                    if (info.Vertices != (l.Quads + 1) * (l.Quads + 1) || info.Overfull > 0) throw new PostLedger308.Refused(sourcePath + " is not a " + l.Quads + "-quad lattice (" + info.Vertices + " vertices, " + info.Overfull + " over-full quads)");
                    foreach (var cut in info.CutCells)
                    {
                        float x0 = t.Tile.position.x + cut.x * info.Step - cfg.cutMargin, z0 = t.Tile.position.z + cut.y * info.Step - cfg.cutMargin;
                        if (boxes.Overlaps(x0, z0, x0 + info.Step + 2f * cfg.cutMargin, z0 + info.Step + 2f * cfg.cutMargin))
                            throw new PostLedger308.Refused("an op box (+" + cfg.boxMargin.ToString("0.#", Inv) + " m) overlaps the cave-portal cut of " + t.Name + "/" + l.Lod + " at world (" + (x0 + cfg.cutMargin).ToString("F0", Inv) + "," + (z0 + cfg.cutMargin).ToString("F0", Inv) + ")");
                    }
                    sources[l] = source;
                }
            }

            // pass 2: copy, move, assert
            var records = new List<TileRecord>();
            foreach (var t in tiles)
            {
                var tr = new TileRecord { tile = t.Name, key = "x" + t.Col + "_z" + t.Row, x = t.Tile.position.x, z = t.Tile.position.z, sourceFromLedger = fromLedger.Contains(t) };
                var lods = new List<LodRecord>();
                foreach (var l in t.Lods)
                {
                    var source = sources[l]; var origin = l.Filter.transform.position;
                    var info = Analyse(source, l.Quads, cfg.tileSize);
                    var rec = new LodRecord { lod = l.Lod, source = Record(source, true), cutCells = info.CutCells.Count, maxY = float.NegativeInfinity, minY = float.PositiveInfinity };
                    var copy = Object.Instantiate(source); copy.name = t.Name + "_" + l.Lod + "_308" + stage.Id; copy.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        var v = copy.vertices; var n = copy.normals;
                        if (n.Length != v.Length) throw new PostLedger308.Refused(rec.source.path + " has " + n.Length + " normals for " + v.Length + " vertices");
                        for (int i = 0; i < v.Length; i++)
                        {
                            float x = origin.x + v[i].x, z = origin.z + v[i].z, y = stage.Field.Sample(x, z);
                            if (boxes.Contains(x, z))
                            {
                                v[i].y = y - origin.y; n[i] = stage.Field.Normal(x, z); rec.changedVertices++;
                            }
                            else
                            {
                                float d = Mathf.Abs(y - (origin.y + v[i].y));
                                if (d > cfg.seamTolerance) { rec.outsideMismatch++; if (d > rec.outsideWorst) rec.outsideWorst = d; }
                            }
                            if (origin.y + v[i].y > rec.maxY) rec.maxY = origin.y + v[i].y; if (origin.y + v[i].y < rec.minY) rec.minY = origin.y + v[i].y;
                        }
                        copy.vertices = v; copy.normals = n; copy.RecalculateBounds();
                        if (source.tangents != null && source.tangents.Length == v.Length) copy.RecalculateTangents();
                        // the contract: same vertex count, same submeshes, same index counts, same UV count
                        if (copy.vertexCount != source.vertexCount || copy.subMeshCount != source.subMeshCount) throw new PostLedger308.Refused(t.Name + "/" + l.Lod + ": vertex or submesh count changed");
                        for (int s = 0; s < source.subMeshCount; s++)
                            if (copy.GetIndexCount(s) != source.GetIndexCount(s) || copy.GetTopology(s) != source.GetTopology(s)) throw new PostLedger308.Refused(t.Name + "/" + l.Lod + ": submesh " + s + " changed");
                        if (copy.uv.Length != source.uv.Length) throw new PostLedger308.Refused(t.Name + "/" + l.Lod + ": UV count changed");
                        build.changedVertices += rec.changedVertices; build.outsideMismatch += rec.outsideMismatch; if (rec.outsideWorst > build.outsideWorst) build.outsideWorst = rec.outsideWorst;
                        if (sink != null)
                        {
                            copy.hideFlags = HideFlags.None;
                            var asset = sink(t, l, copy);   // takes ownership of the copy (asset or destroyed)
                            copy = null;
                            rec.mesh = Record(asset, true);
                        }
                        else { rec.mesh = Record(copy, false); rec.mesh.path = ""; rec.mesh.guid = ""; }
                    }
                    finally { if (copy != null) Object.DestroyImmediate(copy); }
                    if (rec.outsideMismatch > 0) notes.Add(t.Name + "/" + l.Lod + ": " + rec.outsideMismatch + " vertices outside the op boxes differ from the stage field by > " + cfg.seamTolerance.ToString("0.###", Inv) + " m (worst " + rec.outsideWorst.ToString("F3", Inv) + "); they keep the source height");
                    lods.Add(rec);
                }
                tr.lods = lods.ToArray(); records.Add(tr);
            }
            build.tile = records.ToArray(); build.tiles = records.Count; build.notes = notes.ToArray();
            return build;
        }

        internal static string Summary(SurfaceBuild b)
        {
            var perLod = b.tile.SelectMany(t => t.lods).GroupBy(l => l.lod).Select(g => g.Key + " " + g.Sum(l => l.changedVertices));
            return "stage " + b.stage + " in " + b.scene + ": height sha " + b.heightSha256.Substring(0, 12) + " (base " + b.baseSha256.Substring(0, 12) + ", ops " + (string.IsNullOrEmpty(b.opsSha256) ? "none" : b.opsSha256.Substring(0, 12)) + "), "
                + b.changedLatticeVertices + " changed lattice vertices (max raise " + b.maxRaise.ToString("F1", Inv) + " m, max cut " + b.maxCut.ToString("F1", Inv) + " m), op boxes " + b.boxes + " [" + b.boxSource + "] +" + b.boxMargin.ToString("0.#", Inv)
                + " m, tiles " + b.tiles + " [" + string.Join(" ", b.tileKeys) + "]" + (b.notes != null && b.notes.Any(n => n.StartsWith("tile list equals", StringComparison.Ordinal)) ? " = the offline tile list" : " (no offline tile list for this field)") + ", re-sampled vertices " + b.changedVertices + " (" + string.Join(", ", perLod) + "), counts and UV unchanged on " + b.tile.Sum(t => t.lods.Length)
                + " meshes, cut cells in touched tiles " + b.tile.Sum(t => t.lods.Sum(l => l.cutCells)) + " (none under a box), vertices outside the boxes off the field " + b.outsideMismatch
                + (b.outsideMismatch > 0 ? " (worst " + b.outsideWorst.ToString("F3", Inv) + " m)" : "") + ", tiles sourced from ledger originals " + b.tile.Count(t => t.sourceFromLedger);
        }

        // ---------------------------------------------------------------- check data (op lines exported offline, Offline/cb308_stations.py)

        [Serializable] internal sealed class Window { public string id; public float s0, s1; }
        [Serializable] internal sealed class SegmentData { public string id, cls, stage, nameKo; public bool closed; public float step, length; public float[] sx, sz, nx, nz; public string[] ends; public Window[] bays; }
        [Serializable] internal sealed class PointData { public string id, note; public float x, z; }
        [Serializable] internal sealed class WalkData { public string id, expect, note; public float[] from, way; }
        [Serializable] internal sealed class CheckData { public string version, stage, ops, opsSha256, plan, planSha256; public SegmentData[] segments; public PointData[] joints, points; public WalkData[] walks; [NonSerialized] public string Sha256, File; }

        /// <summary>Why the check data is stale, or null: its lines come from the ops file, its points / walks from the plan file
        /// (closure clusters). Either one changing after Offline/cb308_stations.py ran means probes at the wrong or at missing places.</summary>
        internal static string StaleCheckData(CheckData data, Stage stage)
        {
            var why = new List<string>();
            if (stage != null && !string.IsNullOrEmpty(stage.OpsSha) && !string.IsNullOrEmpty(data.opsSha256) && data.opsSha256 != stage.OpsSha)
                why.Add("lines made from ops " + data.opsSha256.Substring(0, Math.Min(12, data.opsSha256.Length)) + ", the stage field from ops " + stage.OpsSha.Substring(0, Math.Min(12, stage.OpsSha.Length)));
            if (string.IsNullOrEmpty(data.plan) || string.IsNullOrEmpty(data.planSha256)) why.Add("no plan sha recorded (made by an older cb308_stations.py)");
            else
            {
                string now = ShaFile(PostLedger308.RepoPath(data.plan));
                if (now != data.planSha256) why.Add("points / walks made from " + data.plan + " sha " + data.planSha256.Substring(0, Math.Min(12, data.planSha256.Length)) + ", the file is now " + (now == "" ? "missing" : now.Substring(0, 12)));
            }
            return why.Count == 0 ? null : string.Join("; ", why) + " - re-run Tools/Unity/Stage308_cliff/Offline/cb308_stations.py";
        }

        internal static CheckData LoadCheckData(Config cfg, string stageId)
        {
            string file = PostLedger308.RepoPath(cfg.checkData.Replace("{stage}", stageId));
            if (!File.Exists(file)) throw new PostLedger308.Refused("no check data " + file + " (python Tools/Unity/Stage308_cliff/Offline/cb308_stations.py <ops> --stage " + stageId + ")");
            var bytes = File.ReadAllBytes(file);
            var d = JsonUtility.FromJson<CheckData>(Encoding.UTF8.GetString(bytes));
            if (d == null || d.segments == null || d.segments.Length == 0) throw new PostLedger308.Refused("check data has no segments: " + file);
            foreach (var s in d.segments)
                if (s.sx == null || s.sx.Length < 2 || s.sz == null || s.nx == null || s.nz == null || s.sz.Length != s.sx.Length || s.nx.Length != s.sx.Length || s.nz.Length != s.sx.Length)
                    throw new PostLedger308.Refused("segment " + s.id + " has no station arrays");
            d.Sha256 = Sha(bytes); d.File = file;
            return d;
        }

        // ---------------------------------------------------------------- physics ground

        /// <summary>What counts as terrain for the rays and the capsule: the tile colliders under the terrain root and, when a stage is
        /// probed before it is applied, the in-memory overlay of its re-sampled tiles (raise-only stages sit on or above the old ground).</summary>
        internal sealed class Ground
        {
            public Transform Root, Overlay;
            public bool Is(Collider c) { var p = c.transform.parent; return p != null && (p == Root || p == Overlay); }
            public static implicit operator Ground(Transform root) => new Ground { Root = root };
        }

        internal static bool TerrainY(Ground terrain, float x, float z, out float y) => TerrainY(terrain, x, z, out y, out _);
        internal static bool TerrainY(Ground terrain, float x, float z, out float y, out Vector3 normal)
        {
            y = float.NegativeInfinity; normal = Vector3.up; bool any = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!terrain.Is(hit.collider) || hit.point.y <= y) continue;
                y = hit.point.y; normal = hit.normal; any = true;
            }
            return any;
        }

        /// <summary>The stage's re-sampled collider meshes as temporary HideAndDontSave MeshColliders (nothing saved, no scene object changed).
        /// Dispose destroys the objects and the meshes.</summary>
        internal sealed class Overlay : IDisposable
        {
            public GameObject Root; public readonly List<Mesh> Meshes = new List<Mesh>(); public SurfaceBuild Build; public int Colliders;
            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                foreach (var m in Meshes) if (m != null) Object.DestroyImmediate(m);
                Meshes.Clear(); Physics.SyncTransforms();
            }
        }

        internal static Overlay BuildOverlay(Config cfg, Scene scene, Stage stage)
        {
            if (stage.MaxCut > cfg.changedEps) throw new PostLedger308.Refused("the overlay needs a raise-only stage (max cut " + stage.MaxCut.ToString("F1", Inv) + " m): a cut lies under the real tile collider");
            var overlay = new Overlay { Root = new GameObject("CliffScan308_overlay") { hideFlags = HideFlags.HideAndDontSave } };
            try
            {
                overlay.Build = BuildSurface(cfg, scene, stage, null, (t, l, copy) =>
                {
                    copy.hideFlags = HideFlags.HideAndDontSave; overlay.Meshes.Add(copy);
                    if (l.Lod != cfg.colliderLod) return copy;
                    var go = new GameObject(t.Name) { hideFlags = HideFlags.HideAndDontSave, layer = t.Tile.gameObject.layer };
                    go.transform.SetParent(overlay.Root.transform, false); go.transform.position = l.Filter.transform.position;
                    go.AddComponent<MeshCollider>().sharedMesh = copy; overlay.Colliders++;
                    return copy;
                });
                overlay.Build.written = false;
                Physics.SyncTransforms();
                return overlay;
            }
            catch { overlay.Dispose(); throw; }
        }

        [Serializable] internal sealed class GroundCheck { public string against; public int samples, missing, over; public float median, p95, max, tolerance; public string worstAt; }

        /// <summary>Physics ground (tile colliders) against a height field at the stage's changed lattice cells: one ray inside the first
        /// triangle of each sampled cell, compared with that triangle of the field (the LOD0 collider's own surface).</summary>
        internal static GroundCheck GroundAgainst(Config cfg, Ground terrainRoot, Stage stage, float[] expect, string against)
        {
            Physics.SyncTransforms();
            var g = new GroundCheck { against = against, tolerance = cfg.probe.groundTolerance }; var deltas = new List<float>(); float worst = -1f;
            int step = Mathf.Max(1, stage.ChangedCells / Mathf.Max(1, cfg.probe.groundSamples)), seen = 0;
            for (int i = 0; i < cfg.height - 1; i++)
                for (int j = 0; j < cfg.width - 1; j++)
                {
                    if (Mathf.Abs(stage.New[i * cfg.width + j] - stage.Base[i * cfg.width + j]) <= cfg.changedEps || seen++ % step != 0) continue;
                    float x = (j + .37f) * cfg.cell, z = (i + .23f) * cfg.cell, want = TriSample(expect, cfg.width, cfg.height, cfg.cell, x, z);
                    g.samples++;
                    if (!TerrainY(terrainRoot, x, z, out float y)) { g.missing++; continue; }
                    float d = Mathf.Abs(y - want); deltas.Add(d);
                    if (d > g.tolerance) g.over++;
                    if (d > worst) { worst = d; g.worstAt = "(" + x.ToString("F1", Inv) + "," + z.ToString("F1", Inv) + ") physics " + y.ToString("F2", Inv) + " field triangle " + want.ToString("F2", Inv); }
                }
            deltas.Sort();
            if (deltas.Count > 0) { g.median = deltas[deltas.Count / 2]; g.p95 = deltas[Mathf.Min(deltas.Count - 1, (int)(deltas.Count * .95f))]; g.max = deltas[deltas.Count - 1]; }
            return g;
        }

        /// <summary>Hash of every terrain tile's collider mesh content, in tile order: equal in the three scenes before the NavMesh bake.</summary>
        internal static string ColliderSet(Config cfg, Transform root)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < cfg.tileRows; r++)
                for (int c = 0; c < cfg.tileCols; c++)
                {
                    var t = FindTile(root, cfg, c, r, false);
                    var m = t != null && t.Collider != null ? t.Collider.sharedMesh : null;
                    sb.Append(TileName(cfg, c, r)).Append('=').Append(m != null && m.isReadable ? MeshContentSha(m) : "-").Append(';');
                }
            return Sha(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        // ---------------------------------------------------------------- capsule walker (edit-mode CharacterController, the motor's rules)

        /// <summary>A hidden copy of the player's CharacterController driven like PlayerMotor.Locomotion: run speed, the config's gravity,
        /// a jump of JumpHeight whenever the motor would call itself grounded (CollisionFlags.Below with a falling velocity), and for the
        /// 국 variant a lift of `guk` metres from any walkable support before that jump. Nothing in the scene is touched or disabled.</summary>
        internal sealed class Walker : IDisposable
        {
            public readonly GameObject Go; public readonly CharacterController CC;
            public float Speed, Gravity, JumpHeight, Stick, Snap; public string Source;
            public Walker(Scene scene, Config cfg)
            {
                var p = cfg.probe;
                Go = new GameObject("CliffBoundary308_probe") { hideFlags = HideFlags.HideAndDontSave }; Go.SetActive(false);
                CC = Go.AddComponent<CharacterController>();
                Speed = p.speed; Gravity = p.gravity; JumpHeight = p.jumpHeight; Stick = p.groundStick; Snap = p.groundSnap;
                CC.height = p.capsuleHeight; CC.radius = p.capsuleRadius; CC.center = new Vector3(0f, p.capsuleHeight * .5f, 0f); CC.stepOffset = p.capsuleStep; CC.slopeLimit = p.capsuleSlope; CC.skinWidth = p.capsuleSkin;
                Source = "config fallback";
                var session = Session(scene); var body = session != null && session.Walker != null ? session.Walker.Body : null;
                if (body != null)
                {
                    var s = body.transform.lossyScale; float sy = Mathf.Abs(s.y), sr = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                    CC.height = body.height * sy; CC.radius = body.radius * sr; CC.center = Vector3.Scale(body.center, s); CC.stepOffset = body.stepOffset; CC.slopeLimit = body.slopeLimit; CC.skinWidth = body.skinWidth;
                    Source = "player body " + PostLedger308.PathOf(body.transform);
                    var motor = session.Walker.Motor;
                    if (p.useMotorValues && motor != null)
                    {
                        var profile = motor.LocomotionProfile;
                        if (profile != null) { Speed = profile.RunSpeed; JumpHeight = profile.JumpHeight; Stick = profile.GroundStickSpeed; Snap = profile.GroundSnap; Source += " + locomotion profile " + profile.name; }
                        var so = new SerializedObject(motor); var prop = so.FindProperty("_config"); var combat = prop != null ? prop.objectReferenceValue as CombatConfigSO : null;
                        if (combat != null) { Gravity = combat.Gravity; Source += " + combat config " + combat.name; }
                    }
                }
            }
            public Vector3 Feet => CC.transform.TransformPoint(CC.center) - Vector3.up * (CC.height * .5f);
            public float JumpSpeed => Mathf.Sqrt(2f * Mathf.Max(0f, JumpHeight) * Mathf.Abs(Gravity));
            public void Place(Vector3 feet)
            {
                CC.enabled = false; Go.transform.position = feet + Vector3.up * (CC.height * .5f - CC.center.y); Go.SetActive(true); CC.enabled = true; Physics.SyncTransforms();
            }
            public void Rest() { CC.enabled = false; Go.SetActive(false); }
            public Collider[] Around(Vector3 feet, float pad) =>
                Physics.OverlapCapsule(feet + Vector3.up * (CC.radius + .05f), feet + Vector3.up * (CC.height - CC.radius), CC.radius + pad, ~0, QueryTriggerInteraction.Ignore).Where(c => c != CC).ToArray();
            /// <summary>PlayerMotor.ProbeGround: a walkable support (within the slope limit) right under the feet.</summary>
            public bool Supported()
            {
                var feet = Feet; float radius = Mathf.Max(.05f, CC.radius * .85f), limit = Mathf.Cos(CC.slopeLimit * Mathf.Deg2Rad);
                foreach (var h in Physics.SphereCastAll(feet + Vector3.up * (radius + .08f), radius, Vector3.down, .08f + Snap, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider != CC && h.normal.y >= limit) return true;
                return false;
            }
            public void Dispose() { if (Go != null) Object.DestroyImmediate(Go); }
        }

        internal sealed class RunResult { public bool Ground, Reached, StartOnRaise, OnlyTerrain = true; public float StartY, MaxPast = float.NegativeInfinity, MaxGain, EndGain; public Vector3 Last; public string End = "stalled", By = "nothing", Start = ""; public int Ticks, Jumps, Lifts; }

        /// <summary>One run: feet at `from` on the terrain, pushed through the waypoints. Ends at the last waypoint, on a stall (no progress
        /// toward the waypoint for stallTicks), on a fall or at the tick budget. past = signed distance beyond the cliff line.</summary>
        internal static RunResult RunWalker(Walker w, Config cfg, Variant variant, Ground terrainRoot, Vector3 from, bool fromIsFeet, Vector2[] way, Func<Vector3, float> past, float seconds, float climbSeconds = 0f)
        {
            var p = cfg.probe; var r = new RunResult();
            Vector3 feet = from;
            if (!fromIsFeet)
            {
                if (!TerrainY(terrainRoot, from.x, from.z, out float gy)) { r.End = "no terrain at the start"; r.Last = from; return r; }
                feet = new Vector3(from.x, gy + p.startLift, from.z);
            }
            r.Ground = true; r.StartY = feet.y;
            w.Place(feet);
            try
            {
                var inside = w.Around(feet, -.02f);
                if (inside.Length > 0) r.Start = ", started inside " + string.Join(",", inside.Take(3).Select(c => c.name));
                float length = 0f; var prev = new Vector2(feet.x, feet.z); foreach (var q in way) { length += Vector2.Distance(prev, q); prev = q; }
                // a jumping capsule that keeps gaining height on a face is followed for climbSeconds (it still ends on a stall)
                int budget = seconds > 0f ? Mathf.CeilToInt(seconds / p.tick) : Mathf.Max(Mathf.CeilToInt(length / (w.Speed * p.tick)) + p.extraTicks, variant.jump ? Mathf.CeilToInt(climbSeconds / p.tick) : 0), wi = 0, stall = 0;
                float best = float.PositiveInfinity, vy = 0f, dt = p.tick; bool grounded = false;
                for (int tick = 0; tick < budget; tick++)
                {
                    r.Ticks = tick + 1;
                    var f = w.Feet; r.MaxPast = Mathf.Max(r.MaxPast, past(f)); r.MaxGain = Mathf.Max(r.MaxGain, f.y - r.StartY);
                    var d = way[wi] - new Vector2(f.x, f.z);
                    if (d.magnitude < p.waypointReach) { if (++wi >= way.Length) { r.End = "reached the push target"; r.Reached = true; break; } best = float.PositiveInfinity; stall = 0; continue; }
                    if (seconds <= 0f) { if (d.magnitude < best - .01f) { best = d.magnitude; stall = 0; } else if (++stall > p.stallTicks) break; }
                    if (f.y < r.StartY - p.fallLimit) { r.End = "fell"; break; }
                    var planar = new Vector3(d.x, 0f, d.y).normalized * w.Speed;
                    float vertical;
                    if (grounded && vy <= 0f)
                    {
                        if (variant.guk > 0f && w.Supported())
                        {
                            // a 국 pillar under the feet, then the jump off its top
                            int steps = p.gukSteps; for (int k = 0; k < steps; k++) w.CC.Move(Vector3.up * (variant.guk / steps));
                            r.Lifts++; vy = variant.jump ? w.JumpSpeed : 0f; if (variant.jump) r.Jumps++; grounded = false;
                        }
                        // the motor today lets a jump start from ANY contact below the capsule (CollisionFlags.Below); jumpNeedsWalkable is the
                        // what-if rule "only from walkable support" (data switch, default off = the motor as it is)
                        else if (variant.jump && (!p.jumpNeedsWalkable || w.Supported())) { vy = w.JumpSpeed; r.Jumps++; grounded = false; }
                    }
                    if (grounded && vy <= 0f) { vy = -w.Stick; vertical = vy * dt; }
                    else { vertical = vy * dt + .5f * w.Gravity * dt * dt; vy += w.Gravity * dt; }
                    var flags = w.CC.Move(planar * dt + Vector3.up * vertical);
                    if ((flags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;
                    grounded = (flags & CollisionFlags.Below) != 0 && vy <= 0f;
                }
                r.Last = w.Feet; r.MaxPast = Mathf.Max(r.MaxPast, past(r.Last)); r.EndGain = r.Last.y - r.StartY; r.MaxGain = Mathf.Max(r.MaxGain, r.EndGain);
                var around = w.Around(r.Last, p.touchPad);
                r.OnlyTerrain = around.All(c => terrainRoot.Is(c));
                // what is not terrain is named first: that is what stopped the capsule short of the face
                r.By = around.Length == 0 ? "nothing" : string.Join(",", around.OrderBy(c => terrainRoot.Is(c) ? 1 : 0).Take(3).Select(c => terrainRoot.Is(c) ? "terrain " + c.name : c.name + "@" + (c.transform.parent != null ? c.transform.parent.name : "-")));
                return r;
            }
            finally { w.Rest(); }
        }

        // ---------------------------------------------------------------- face heights + probes along the op lines

        [Serializable] internal sealed class FaceStats { public int stations, buried, noSurface, below4, band345to8, band8to24Outside, band8to24InBay, band24to28, flankAway, ok; public float min, p10, median, p90, max, planMin, planMedian; }
        [Serializable] internal sealed class VariantTally { public string variant; public int pass, fail, inconclusive, skipped, top, climb; public float maxGain, maxGainSeconds; }
        [Serializable] internal sealed class ProbeLine { public string segment, variant, kind, verdict, end, by; public float s, x, z, maxPast, maxGain, endGain, startY, endX, endY, endZ; public int ticks, jumps, lifts; }
        [Serializable] internal sealed class SegmentReport { public string id, cls, grammar; public float length; public FaceStats face; public VariantTally[] probes, ends, gain; public string[] faceFlags; }
        [Serializable] internal sealed class ProbeReport
        {
            public string format = "cb308.probe.1", utc, scene, stage, checkData, checkDataSha256, walker, jumpRule, verdict;
            public float capsuleHeight, capsuleRadius, capsuleStep, capsuleSlope, speed, gravity, jumpHeight;
            public int stations, runs, fails, inconclusive, leaks, open, skipped, grammarFlags;
            public SegmentReport[] segments; public ProbeLine[] lines; public string[] notes;
        }

        // JSON carries no NaN / Infinity: an empty sample set reads 0 and the station counts say so
        static float At(List<float> sorted, float f) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(f * (sorted.Count - 1)), 0, sorted.Count - 1)];
        static float Fin(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        static ProbeLine Line(string segment, Variant variant, string kind, string verdict, float s, float x, float z, RunResult run, string extra = "") =>
            new ProbeLine { segment = segment, variant = variant.name, kind = kind, verdict = verdict, s = s, x = x, z = z, maxPast = Fin(run.MaxPast), maxGain = Fin(run.MaxGain), endGain = Fin(run.EndGain), startY = Fin(run.StartY),
                end = run.End + run.Start + extra, by = run.By, ticks = run.Ticks, jumps = run.Jumps, lifts = run.Lifts, endX = Fin(run.Last.x), endY = Fin(run.Last.y), endZ = Fin(run.Last.z) };

        /// <summary>Face height at one station (Spec section 2): the largest rise inside any faceWindow across the line, samples 1 m apart from
        /// faceLower m on the lower side to faceUpper m on the upper side, the lower end of the window within faceWithin m of the line.
        /// plan = the plan tools' window (upper 0..faceUpper m max - lower planLowerNear..planLowerFar m min, every planStep m).</summary>
        internal static bool FaceHeight(Ground terrainRoot, Grammar g, float x, float z, float nx, float nz, out float strict, out float plan)
        {
            int lower = Mathf.RoundToInt(g.faceLower), upper = Mathf.RoundToInt(g.faceUpper), n = lower + upper + 1, window = Mathf.RoundToInt(g.faceWindow), first = lower - Mathf.RoundToInt(g.faceWithin);
            var h = new float[n];
            for (int k = 0; k < n; k++) { float o = k - lower; if (!TerrainY(terrainRoot, x + nx * o, z + nz * o, out h[k])) h[k] = float.NaN; }
            strict = float.NaN;
            for (int lo = Mathf.Max(0, first); lo < n; lo++)
                for (int hi = lo + 1; hi < n && hi <= lo + window; hi++)
                {
                    if (float.IsNaN(h[lo]) || float.IsNaN(h[hi])) continue;
                    float rise = h[hi] - h[lo]; if (float.IsNaN(strict) || rise > strict) strict = rise;
                }
            float up = float.NegativeInfinity, low = float.PositiveInfinity;
            int planStep = Mathf.Max(1, Mathf.RoundToInt(g.planStep)), planFar = Mathf.RoundToInt(g.planLowerFar), planNear = Mathf.RoundToInt(g.planLowerNear);
            for (int k = lower; k < n; k += planStep) if (!float.IsNaN(h[k])) up = Mathf.Max(up, h[k]);
            for (int k = Mathf.Max(0, lower - planFar); k <= lower - planNear; k += planStep) if (!float.IsNaN(h[k])) low = Mathf.Min(low, h[k]);
            plan = float.IsInfinity(up) || float.IsInfinity(low) ? float.NaN : up - low;
            return !float.IsNaN(strict);
        }

        static bool InBay(SegmentData seg, float s, float pad) => seg.bays != null && seg.bays.Any(b => s >= b.s0 - pad && s <= b.s1 + pad);

        /// <summary>True when the feet stand on ground this stage raised beyond the 국 + jump reach (the top or a ledge of a cliff).</summary>
        static bool OnRaise(Stage stage, Grammar g, Vector3 feet)
        {
            if (stage == null) return false;
            float now = stage.Field.Sample(feet.x, feet.z), was = stage.BaseField.Sample(feet.x, feet.z);
            return now - was > g.noFaceMin && feet.y > was + g.noFaceMin;
        }

        /// <summary>PASS = stopped by terrain alone, on the old ground; TOP = ended on raised ground beyond the line (on the plateau);
        /// CLIMB = ended on raised ground short of that (gaining height ON the face when the run was cut); OPEN = went beyond the line at
        /// ground level; SKIP = the start itself is on raised ground (a buried end, a plateau); INCONCLUSIVE = no start or another collider
        /// in the way. Without a stage (no raise map) a run beyond the line is OPEN.</summary>
        static string Classify(RunResult run, Config cfg, Stage stage)
        {
            if (!run.Ground) return "INCONCLUSIVE";
            if (run.StartOnRaise) return "SKIP";
            if (run.Start != "") return "INCONCLUSIVE";
            bool beyond = run.MaxPast > cfg.probe.failPast || run.Reached;
            if (OnRaise(stage, cfg.grammar, run.Last))
            {
                if (beyond) return "TOP";
                // on raised ground short of the line: a climb only when the RUN gained that height. With no gain the start already stood on
                // the face (the collider is triangulated, the raise map bilinear: inside a face cell they disagree, so StartOnRaise can miss
                // it) and the capsule only slid down - no verdict on the face. Measured 2026-10-04: point reach_2248_1552 +40, walk, gain 0.0
                return run.MaxGain > cfg.grammar.noFaceMin ? "CLIMB" : "SKIP";
            }
            if (beyond) return "OPEN";
            return run.OnlyTerrain ? "PASS" : "INCONCLUSIVE";
        }

        static void Count(VariantTally t, string verdict, RunResult run, float tick)
        {
            if (verdict == "PASS" || verdict == "BLOCKED") t.pass++; else if (verdict == "SKIP") t.skipped++; else if (verdict == "INCONCLUSIVE") t.inconclusive++;
            else
            {
                t.fail++; if (verdict == "TOP") t.top++; else if (verdict == "CLIMB") t.climb++;
                if (run.EndGain > t.maxGain) { t.maxGain = Fin(run.EndGain); t.maxGainSeconds = run.Ticks * tick; }
            }
        }

        /// <summary>Face heights every station and capsule probes (every probe.every m, every joint / open end, outer-slope gain) on the scene's
        /// physics ground as it is NOW (plus an optional in-memory overlay). Creates one hidden CharacterController and destroys it; changes
        /// nothing else. stage (optional) tells raised ground from old ground: without it nothing can be called TOP.</summary>
        internal static ProbeReport ProbeLines(Config cfg, Scene scene, CheckData data, int maxStations, string onlySegment, Transform overlay = null, Stage stage = null)
        {
            var root = new Ground { Root = TerrainRoot(scene, cfg), Overlay = overlay }; var g = cfg.grammar; var p = cfg.probe;
            Physics.SyncTransforms();
            var report = new ProbeReport { stage = data.stage, checkData = data.File, checkDataSha256 = data.Sha256 };
            var lines = new List<ProbeLine>(); var segs = new List<SegmentReport>(); var notes = new List<string>();
            if (stage == null) notes.Add("no stage field given: runs beyond a line are OPEN, none can be TOP");
            using (var w = new Walker(scene, cfg))
            {
                report.walker = w.Source; report.capsuleHeight = w.CC.height; report.capsuleRadius = w.CC.radius; report.capsuleStep = w.CC.stepOffset; report.capsuleSlope = w.CC.slopeLimit;
                report.speed = w.Speed; report.gravity = w.Gravity; report.jumpHeight = w.JumpHeight;
                report.jumpRule = p.jumpNeedsWalkable ? "motor (D308-9d): jump only from walkable support" : "motor: jump from any contact below (CollisionFlags.Below, falling)";
                RunResult Go(Variant variant, Vector3 from, bool fromIsFeet, Vector2[] way, Func<Vector3, float> past, float seconds)
                {
                    var run = RunWalker(w, cfg, variant, root, from, fromIsFeet, way, past, seconds, p.climbSeconds); report.runs++;
                    run.StartOnRaise = run.Ground && OnRaise(stage, g, new Vector3(from.x, run.StartY, from.z));
                    return run;
                }
                foreach (var seg in data.segments)
                {
                    if (!string.IsNullOrEmpty(onlySegment) && seg.id != onlySegment) continue;
                    int n = seg.sx.Length; float step = seg.step > 0f ? seg.step : seg.length / Mathf.Max(1, n - 1);
                    var sr = new SegmentReport { id = seg.id, cls = seg.cls, length = seg.length, face = new FaceStats() };
                    bool outer = seg.cls == "outer";

                    // --- face heights, every station
                    var strict = new List<float>(); var plan = new List<float>(); var flags = new List<string>();
                    for (int i = 0; i < n; i++)
                    {
                        sr.face.stations++;
                        // a station whose player side is itself raised ground (the line runs into another cliff) has no face to grade
                        float lx = seg.sx[i] - seg.nx[i] * g.faceWithin, lz = seg.sz[i] - seg.nz[i] * g.faceWithin;
                        if (stage != null && stage.Field.Sample(lx, lz) - stage.BaseField.Sample(lx, lz) > g.noFaceMin) { sr.face.buried++; continue; }
                        if (!FaceHeight(root, g, seg.sx[i], seg.sz[i], seg.nx[i], seg.nz[i], out float hs, out float hp)) { sr.face.noSurface++; continue; }
                        strict.Add(hs); if (!float.IsNaN(hp)) plan.Add(hp);
                        float s = i * step; bool bay = InBay(seg, s, 0f), beside = InBay(seg, s, g.flankLength); string flag = null;
                        if (hs < g.d305Floor) { sr.face.below4++; flag = "below the D305 floor (" + g.d305Floor.ToString("0.##", Inv) + " m)"; }
                        else if (hs < g.noFaceMax) { sr.face.band345to8++; flag = "inside the forbidden " + g.noFaceMin.ToString("0.##", Inv) + "-" + g.noFaceMax.ToString("0.##", Inv) + " m band"; }
                        else if (hs < g.liftMax) { if (bay) sr.face.band8to24InBay++; else { sr.face.band8to24Outside++; flag = "liftable " + g.liftMin.ToString("0.#", Inv) + "-" + g.liftMax.ToString("0.#", Inv) + " m face outside a declared bay window"; } }
                        else if (hs < g.flankMax) { if (beside) sr.face.band24to28++; else { sr.face.flankAway++; flag = g.liftMax.ToString("0.#", Inv) + "-" + g.flankMax.ToString("0.#", Inv) + " m face more than " + g.flankLength.ToString("0.#", Inv) + " m from a bay window"; } }
                        else sr.face.ok++;
                        if (flag != null && flags.Count < 60) flags.Add("s=" + s.ToString("F0", Inv) + " (" + seg.sx[i].ToString("F0", Inv) + "," + seg.sz[i].ToString("F0", Inv) + ") " + hs.ToString("F1", Inv) + " m: " + flag);
                    }
                    strict.Sort(); plan.Sort();
                    sr.face.min = At(strict, 0f); sr.face.p10 = At(strict, .1f); sr.face.median = At(strict, .5f); sr.face.p90 = At(strict, .9f); sr.face.max = At(strict, 1f); sr.face.planMin = At(plan, 0f); sr.face.planMedian = At(plan, .5f);
                    int bad = sr.face.below4 + sr.face.band345to8 + sr.face.band8to24Outside + sr.face.flankAway;
                    bool medianOk = outer || strict.Count == 0 || sr.face.median >= g.innerMedian;
                    sr.grammar = bad == 0 && medianOk && sr.face.noSurface == 0 ? "ok" : "FLAG: " + bad + " of " + (sr.face.stations - sr.face.buried) + " graded stations off the height grammar" + (sr.face.noSurface > 0 ? ", " + sr.face.noSurface + " without a surface" : "")
                        + (medianOk ? "" : ", median " + sr.face.median.ToString("F1", Inv) + " < " + g.innerMedian.ToString("0.#", Inv));
                    if (sr.grammar != "ok") report.grammarFlags++;
                    sr.faceFlags = flags.ToArray();

                    // --- push probes every probe.every m: inside a line every run beyond it is a hole (TOP or OPEN)
                    var tally = p.variants.ToDictionary(v => v.name, v => new VariantTally { variant = v.name });
                    int every = Mathf.Max(1, Mathf.RoundToInt(p.every / step)), done = 0;
                    for (int i = seg.closed ? 0 : Mathf.Min(1, n - 1); i < n && done < maxStations; i += every, done++)
                    {
                        float sx = seg.sx[i], sz = seg.sz[i], nx = seg.nx[i], nz = seg.nz[i];
                        report.stations++;
                        foreach (var variant in p.variants)
                        {
                            RunResult run = null;
                            foreach (float back in p.startBack)
                            {
                                run = Go(variant, new Vector3(sx - nx * back, 0f, sz - nz * back), false, new[] { new Vector2(sx + nx * p.push, sz + nz * p.push) }, f => (f.x - sx) * nx + (f.z - sz) * nz, 0f);
                                if (run.Ground && run.Start == "" && !run.StartOnRaise) break;
                            }
                            string verdict = Classify(run, cfg, stage);
                            Count(tally[variant.name], verdict, run, p.tick);
                            if (verdict == "TOP" || verdict == "CLIMB" || verdict == "OPEN") report.fails++; else if (verdict == "INCONCLUSIVE") report.inconclusive++; else if (verdict == "SKIP") report.skipped++;
                            if (verdict != "PASS" || lines.Count < 2000) lines.Add(Line(seg.id, variant, "push", verdict, i * step, sx, sz, run));
                        }
                    }
                    sr.probes = tally.Values.ToArray();

                    // --- open ends: around the end from the lower side to the upper side
                    var endTally = p.variants.ToDictionary(v => v.name, v => new VariantTally { variant = v.name });
                    for (int e = 0; e < 2 && !seg.closed; e++)
                    {
                        if (seg.ends == null || e >= seg.ends.Length || seg.ends[e] != "open") continue;
                        int i = e == 0 ? 0 : n - 1, back = Mathf.Clamp(e == 0 ? Mathf.RoundToInt(p.endBack / step) : n - 1 - Mathf.RoundToInt(p.endBack / step), 0, n - 1), inner = e == 0 ? Mathf.Min(1, n - 1) : Mathf.Max(0, n - 2);
                        var end = new Vector2(seg.sx[i], seg.sz[i]); var outward = (end - new Vector2(seg.sx[inner], seg.sz[inner])).normalized; var nb = new Vector2(seg.nx[i], seg.nz[i]);
                        var from = new Vector2(seg.sx[back], seg.sz[back]) - new Vector2(seg.nx[back], seg.nz[back]) * p.startBack[0];
                        var target = new Vector2(seg.sx[back], seg.sz[back]) + new Vector2(seg.nx[back], seg.nz[back]) * p.startBack[0];
                        var way = new[] { end - nb * p.startBack[0] + outward * p.endPast, end + nb * p.startBack[0] + outward * p.endPast, target };
                        foreach (var variant in p.variants)
                        {
                            var run = Go(variant, new Vector3(from.x, 0f, from.y), false, way, f => (f.x - seg.sx[back]) * seg.nx[back] + (f.z - seg.sz[back]) * seg.nz[back], 0f);
                            bool leak = run.Ground && Vector2.Distance(new Vector2(run.Last.x, run.Last.z), target) < p.endReach;
                            string verdict = !run.Ground ? "INCONCLUSIVE" : run.StartOnRaise ? "SKIP" : leak ? "LEAK" : "BLOCKED";
                            Count(endTally[variant.name], verdict, run, p.tick);
                            if (leak && verdict == "LEAK") report.leaks++; else if (verdict == "INCONCLUSIVE") report.inconclusive++; else if (verdict == "SKIP") report.skipped++;
                            lines.Add(Line(seg.id, variant, "end" + e, verdict, i * step, end.x, end.y, run));
                        }
                    }
                    sr.ends = endTally.Values.ToArray();

                    // --- outer class: no height gain on the rise behind the first step (AC-B9 c)
                    var gainTally = p.variants.ToDictionary(v => v.name, v => new VariantTally { variant = v.name });
                    if (outer && p.gainOffsets != null)
                    {
                        done = 0;
                        for (int i = 0; i < n && done < maxStations; i += every, done++)
                            foreach (float off in p.gainOffsets)
                            {
                                float x = seg.sx[i] + seg.nx[i] * off, z = seg.sz[i] + seg.nz[i] * off;
                                if (!TerrainY(root, x, z, out float y, out Vector3 normal)) continue;
                                float slope = Vector3.Angle(normal, Vector3.up);
                                if (slope < p.gainMinSlopeDeg || slope > p.gainMaxSlopeDeg) continue;
                                // the capsule's bottom sphere must clear the slope: lift the feet by r (1 / cos - 1) plus a gap
                                float lift = w.CC.radius * (1f / Mathf.Max(.2f, normal.y) - 1f) + p.startLift;
                                foreach (var variant in p.variants)
                                {
                                    var run = Go(variant, new Vector3(x, y + lift, z), true, new[] { new Vector2(x + seg.nx[i] * p.gainPush, z + seg.nz[i] * p.gainPush) }, f => (f.x - seg.sx[i]) * seg.nx[i] + (f.z - seg.sz[i]) * seg.nz[i], p.gainSeconds);
                                    string verdict = run.Start != "" ? "INCONCLUSIVE" : run.EndGain > p.gainLimit ? "GAIN" : "PASS";
                                    Count(gainTally[variant.name], verdict, run, p.tick);
                                    if (verdict == "GAIN") report.fails++; else if (verdict == "INCONCLUSIVE") report.inconclusive++;
                                    if (verdict != "PASS" || lines.Count < 2000) lines.Add(Line(seg.id, variant, "gain+" + off.ToString("0", Inv), verdict, i * step, x, z, run, ", slope " + slope.ToString("F0", Inv)));
                                }
                            }
                    }
                    sr.gain = gainTally.Values.ToArray();
                    segs.Add(sr);
                }

                // --- joints and declared points: three headings across the mark. TOP / CLIMB are failures here; OPEN (ground-level way past a
                //     line end or between two lines) is listed for the closure check, which knows what must be sealed.
                var marks = new List<(string id, float x, float z, string kind)>();
                if (data.joints != null) foreach (var j in data.joints) marks.Add((j.id, j.x, j.z, "joint"));
                if (data.points != null) foreach (var q in data.points) marks.Add((q.id, q.x, q.z, "point"));
                if (string.IsNullOrEmpty(onlySegment))
                    foreach (var mark in marks)
                    {
                        if (!TerrainY(root, mark.x, mark.z, out float _)) { notes.Add(mark.kind + " " + mark.id + ": no terrain"); continue; }
                        // the nearest station decides which side is the lower (player) side
                        SegmentData near = null; int ni = 0; float nd = float.MaxValue;
                        foreach (var seg in data.segments)
                            for (int i = 0; i < seg.sx.Length; i++) { float dd = (seg.sx[i] - mark.x) * (seg.sx[i] - mark.x) + (seg.sz[i] - mark.z) * (seg.sz[i] - mark.z); if (dd < nd) { nd = dd; near = seg; ni = i; } }
                        float nx = near.nx[ni], nz = near.nz[ni], sx = near.sx[ni], sz = near.sz[ni];
                        foreach (float turn in p.markTurns)
                        {
                            var dir = Quaternion.Euler(0f, turn, 0f) * new Vector3(nx, 0f, nz);
                            foreach (var variant in p.variants)
                            {
                                var run = Go(variant, new Vector3(mark.x - dir.x * p.startBack[0], 0f, mark.z - dir.z * p.startBack[0]), false, new[] { new Vector2(mark.x + dir.x * p.push, mark.z + dir.z * p.push) }, f => (f.x - sx) * nx + (f.z - sz) * nz, 0f);
                                string verdict = Classify(run, cfg, stage);
                                if (verdict == "TOP" || verdict == "CLIMB") report.fails++; else if (verdict == "OPEN") report.open++; else if (verdict == "INCONCLUSIVE") report.inconclusive++; else if (verdict == "SKIP") report.skipped++;
                                lines.Add(Line(near.id, variant, mark.kind + " " + mark.id + " " + turn.ToString("0", Inv), verdict, ni * near.step, mark.x, mark.z, run));
                            }
                        }
                    }
            }
            report.segments = segs.ToArray(); report.lines = lines.ToArray(); report.notes = notes.ToArray();
            report.verdict = report.fails > 0 || report.leaks > 0 ? "FAIL" : report.grammarFlags > 0 ? "GRAMMAR" : report.inconclusive > 0 ? "INCONCLUSIVE" : "CLEAR";
            return report;
        }

        internal static string Summary(ProbeReport r)
        {
            var sb = new StringBuilder();
            sb.Append("stage " + r.stage + ": " + r.verdict + " - fails " + r.fails + " (TOP = on the plateau, CLIMB = gaining height on a face, OPEN inside a line, GAIN on an outer rise), end leaks " + r.leaks + ", open at joints / points " + r.open + " (for the closure check), inconclusive " + r.inconclusive
                + ", skipped (start on raised ground) " + r.skipped + ", segments off the height grammar " + r.grammarFlags + " over " + r.stations + " stations / " + r.runs + " runs; capsule h "
                + r.capsuleHeight.ToString("F2", Inv) + " r " + r.capsuleRadius.ToString("F2", Inv) + " step " + r.capsuleStep.ToString("F2", Inv) + " slope " + r.capsuleSlope.ToString("F0", Inv) + ", speed " + r.speed.ToString("F1", Inv) + ", gravity "
                + r.gravity.ToString("F1", Inv) + ", jump " + r.jumpHeight.ToString("F2", Inv) + " [" + r.jumpRule + "] (" + r.walker + ")");
            foreach (var s in r.segments)
                sb.Append(" | " + s.id + " face min/p10/median/p90 " + s.face.min.ToString("F1", Inv) + "/" + s.face.p10.ToString("F1", Inv) + "/" + s.face.median.ToString("F1", Inv) + "/" + s.face.p90.ToString("F1", Inv) + " m"
                    + (s.face.buried > 0 ? " (" + s.face.buried + " buried stations not graded)" : "") + ", " + s.grammar
                    + ", push pass/fail/inconclusive " + string.Join(" ", s.probes.Select(t => t.variant + " " + t.pass + "/" + t.fail + "/" + t.inconclusive
                        + (t.fail > 0 ? " (top " + t.top + ", climb " + t.climb + ", best gain " + t.maxGain.ToString("F1", Inv) + " m in " + t.maxGainSeconds.ToString("F0", Inv) + " s)" : "")))
                    + (s.gain != null && s.gain.Any(t => t.pass + t.fail > 0) ? ", rise-gain pass/fail " + string.Join(" ", s.gain.Select(t => t.variant + " " + t.pass + "/" + t.fail + (t.fail > 0 ? " (best +" + t.maxGain.ToString("F1", Inv) + " m)" : ""))) : ""));
            return sb.ToString();
        }
    }
}
