using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff boundary, phase 1b: rock skins of the EA-visible cliff faces (SPEC-WORLD-CLIFF-BOUNDARY-308 design 1-2, 10; command row
    // `skins:<scene>`; AC-B2 / B10a / B15 / B17 / B21). The meshes come from Tools/Art/cliffskin308.py (KitMesh JSON per panel and LOD,
    // skins_1b/<stage>/unity.json). A skin has NO collider (the terrain face behind it is the collider), no emission, no light, one
    // material, no shadow caster. Every command answers a refusal STRING (never a dialog) in Play mode, while compiling, on a dirty or
    // unknown scene and on a protected path; assets are saved one by one (SaveAssetIfDirty), AssetDatabase.SaveAssets is never called.
    // Queue: Oheangbu.EditorTools.WorldMacro.CliffSkins308 Run "<command>"      (<scene> = arch296 | folk298 | main)
    //   status                                    manifests on disk, import ledgers, scene ledgers, what the active scene holds, preview
    //   import[:stage=<s>][:dry]                  KitMesh JSON -> mesh assets <assetDir>/<stage>/ + ONE material copy; idempotent by sha
    //   plan:<scene>[:stage=<s>]                  what apply would do (reads only)
    //   apply:<scene>[:stage=<s>]                 root CliffBoundary308 / Skins / <stretch> / <panel> (LODGroup, 3 LODs); backup, ledger; a
    //                                             second run changes nothing. Refused unless the scene's terrain ledger is applied at the
    //                                             manifest's stage and height sha (a 1b skin never lands on 1a terrain)
    //   verify:<scene>[:stage=<s>]                read-only: colliders 0, emission 0, shadow casters 0, front distance of the probes to the
    //                                             REAL terrain collider (p50 / p95 / max), piercing, top >= wall top, counts vs budget, memory
    //   revert:<scene>[:dry]                      removes Skins only; the shared root goes only when it has no other child
    //   preview:on[:stage=<s>][:stretch=a,b][:lod=mobile|0|1|2][:allowdirty][:anyheight]   in-memory look preview (nothing saved)
    //   preview:off
    // Stage default: import = stages.apply_stage of skins308_rules_1b.json; scene commands and the preview = the stage of the scene's
    // cliff terrain ledger (cb308-<scene>.json).
    public static partial class CliffSkins308
    {
        // the one path the code knows; every other path and number is data under it
        const string SkinsDir = "Art/World/Compact/Rebuild/CliffBoundary308/skins_1b";
        const string RulesFile = SkinsDir + "/skins308_rules_1b.json";
        const string ManifestFormat = "cb308.skins.unity.1";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            string verb = a[0]; bool dry = false;
            if (verb.EndsWith("-dry", StringComparison.Ordinal)) { verb = verb.Substring(0, verb.Length - 4); dry = true; }
            try
            {
                var cfg = CliffCore308.LoadConfig();
                switch (verb)
                {
                    case "status": return Status(cfg);
                    case "import":
                    {
                        var opt = PostLedger308.Options(a.Skip(1));
                        return Import(cfg, opt, dry || opt.ContainsKey("dry"));
                    }
                    case "plan": return Apply(cfg, Need(a, verb), PostLedger308.Options(a.Skip(2)), true);
                    case "apply":
                    {
                        var opt = PostLedger308.Options(a.Skip(2));
                        return Apply(cfg, Need(a, verb), opt, dry || opt.ContainsKey("dry"));
                    }
                    case "verify": return Verify(cfg, Need(a, verb), PostLedger308.Options(a.Skip(2)));
                    case "revert":
                    {
                        var opt = PostLedger308.Options(a.Skip(2));
                        return Revert(cfg, Need(a, verb), dry || opt.ContainsKey("dry"));
                    }
                    case "preview":
                        if (a.Length > 1 && a[1] == "on") return PreviewOn(cfg, PostLedger308.Options(a.Skip(2)));
                        if (a.Length > 1 && a[1] == "off") return PreviewOff();
                        throw new PostLedger308.Refused("use preview:on[:stage=<s>][:stretch=a,b][:lod=mobile|0|1|2] or preview:off");
                    default: throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { Debug.LogException(e); return "error: " + e.GetType().Name + ": " + e.Message + " (see the console; the ledgers show how far the command got)"; }
        }

        static string Need(string[] a, string verb)
        {
            if (a.Length < 2 || string.IsNullOrEmpty(a[1])) throw new PostLedger308.Refused("use " + verb + ":<scene> (arch296 | folk298 | main)");
            return a[1];
        }

        // ---------------------------------------------------------------- data (skins_1b/<stage>/unity.json, written by cliffskin308.py)

        [Serializable] sealed class LodRow { public string file = "", sha256 = ""; public int vertices, triangles, front, bedding, end, cap, indexBytes; }
        [Serializable] sealed class PanelRow
        {
            public string id = "", stretch = "", tier = ""; public int[] evalPanels = new int[0]; public float lengthM, size;
            public float[] position = new float[0], boundsMin = new float[0], boundsMax = new float[0], screen = new float[0], switchPcM = new float[0], probes = new float[0], tops = new float[0];
            public LodRow[] lods = new LodRow[0]; public int[] caps = new int[0];
        }
        [Serializable] sealed class Budget { public int lod0TrisMax, renderersMax; public long meshMemoryBytesMax; public int materialsMax, shadowCastersMax, collidersMax, bytesPerVertex, passesPerRenderer; }
        [Serializable] sealed class Totals { public int panels, lod0Triangles, lod1Triangles, lod2Triangles, lod0Vertices, lod1Vertices, lod2Vertices; public long meshMemoryBytes; public float skinLengthM; }
        [Serializable] sealed class Manifest
        {
            public string format = "", stage = "", heightSha256 = "", opsSha256 = "", rulesSha256 = "", guideSha256 = "", generatorSha256 = "", meshDir = "", assetDir = "", materialDir = "", materialName = "",
                materialSource = "", materialSourceGuid = "", slot = "", root = "", group = "", previewRoot = "", shadowCasting = "Off";
            public int layer, probeStride = 6, topStride = 5; public bool receiveShadows = true, meshReadable; public string[] staticFlags = new string[0];
            public float refLodBias = 2f, mobileLodBias = 1f, refFovDeg = 60f, clearance, maxFront, overM;
            public Budget budget = new Budget(); public Totals totals = new Totals(); public PanelRow[] panels = new PanelRow[0];
            [NonSerialized] public string Sha256, File;
            public string MaterialPath => materialDir + "/" + materialName + ".mat";
        }
        [Serializable] sealed class RulesStages { public string apply_stage = ""; }
        [Serializable] sealed class Rules { public RulesStages stages = new RulesStages(); }

        static void CheckStageId(string stage)
        {
            if (string.IsNullOrEmpty(stage) || stage.IndexOfAny(new[] { '/', '\\', '.', ' ' }) >= 0) throw new PostLedger308.Refused("bad stage id '" + stage + "'");
        }

        static string ManifestFile(string stage) => PostLedger308.RepoPath(SkinsDir + "/" + stage + "/unity.json");

        static Manifest Load(CliffCore308.Config cfg, string stage)
        {
            CheckStageId(stage);
            string file = ManifestFile(stage);
            if (!File.Exists(file)) throw new PostLedger308.Refused("no skin manifest " + file + " (python Tools/Art/cliffskin308.py --stage " + stage + ")");
            var bytes = File.ReadAllBytes(file);
            var m = JsonUtility.FromJson<Manifest>(Encoding.UTF8.GetString(bytes));
            if (m == null || m.format != ManifestFormat) throw new PostLedger308.Refused("manifest " + file + " is not format " + ManifestFormat);
            if (m.stage != stage) throw new PostLedger308.Refused("manifest " + file + " is for stage " + m.stage);
            if (m.panels == null || m.panels.Length == 0 || m.budget == null || m.totals == null) throw new PostLedger308.Refused("manifest " + file + " has no panels / budget");
            if (string.IsNullOrEmpty(m.root) || string.IsNullOrEmpty(m.group) || string.IsNullOrEmpty(m.previewRoot) || string.IsNullOrEmpty(m.materialName)) throw new PostLedger308.Refused("manifest " + file + " lacks root / group / previewRoot / materialName");
            if (m.probeStride < 6 || m.topStride < 5) throw new PostLedger308.Refused("manifest " + file + " has an unknown probe layout");
            foreach (string path in new[] { m.assetDir, m.materialDir })
                if (!path.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(path + "/")) throw new PostLedger308.Refused("manifest path outside " + cfg.assetRoot + " or protected: " + path);
            foreach (var p in m.panels)
                if (p.lods == null || p.lods.Length == 0 || p.position == null || p.position.Length != 3 || p.screen == null || p.screen.Length < p.lods.Length) throw new PostLedger308.Refused("manifest panel " + p.id + " is incomplete");
            m.Sha256 = CliffCore308.Sha(bytes); m.File = file;
            return m;
        }

        static string DefaultImportStage()
        {
            string file = PostLedger308.RepoPath(RulesFile);
            if (!File.Exists(file)) throw new PostLedger308.Refused("rules missing: " + RulesFile + " (pass :stage=<s>)");
            var r = JsonUtility.FromJson<Rules>(File.ReadAllText(file, Encoding.UTF8));
            if (r == null || r.stages == null || string.IsNullOrEmpty(r.stages.apply_stage)) throw new PostLedger308.Refused(RulesFile + " has no stages.apply_stage (pass :stage=<s>)");
            return r.stages.apply_stage;
        }

        /// <summary>Stage of a scene command: the :stage option, else the stage of the scene's cliff terrain ledger.</summary>
        static string SceneStage(CliffCore308.Config cfg, string scenePath, Dictionary<string, string> opt, out CliffBoundary308.SceneLedger terrain)
        {
            terrain = CliffBoundary308.ReadLedger(cfg, scenePath);
            string stage = opt != null && opt.TryGetValue("stage", out var s) && s.Length > 0 ? s : terrain != null ? terrain.stage : "";
            if (string.IsNullOrEmpty(stage)) throw new PostLedger308.Refused("the scene has no cliff terrain ledger stage (cb308-<scene>.json); run CliffBoundary308 tiles first or pass :stage=<s>");
            return stage;
        }

        /// <summary>A skin set belongs to ONE stage height field: the scene's terrain ledger must be applied at that stage and that sha.</summary>
        static string TerrainMismatch(CliffBoundary308.SceneLedger terrain, Manifest m)
        {
            if (terrain == null || terrain.state != "applied") return "the scene's cliff terrain ledger is not 'applied' (state " + (terrain == null ? "none" : terrain.state) + ")";
            if (terrain.stage != m.stage) return "the scene's terrain is stage " + terrain.stage + ", the skins are stage " + m.stage;
            if (terrain.heightSha256 != m.heightSha256) return "the scene's terrain height sha " + Short(terrain.heightSha256) + " is not the one the skins were generated on (" + Short(m.heightSha256) + "); re-run python Tools/Art/cliffskin308.py --stage " + m.stage;
            return null;
        }

        internal static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Length > 12 ? sha.Substring(0, 12) : sha;
        static string F(float v, string f = "0.##") => v.ToString(f, Inv);

        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
        }

        static bool Glows(Material m) => m != null && (m.IsKeywordEnabled("_EMISSION") || (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f));

        // ---------------------------------------------------------------- KitMesh JSON -> Mesh

        [Serializable] sealed class KitSub { public string m = ""; public int[] t = new int[0]; }
        [Serializable] sealed class Kit { public string name = ""; public float[] v = new float[0], n = new float[0]; public KitSub[] sub = new KitSub[0]; }

        static string MeshFile(Manifest m, LodRow l) => PostLedger308.RepoPath(m.meshDir + "/" + l.file);

        /// <summary>Position + normal only: the HighlandNatural shader reads no UV and no tangent (world-space triplanar).</summary>
        static Mesh BuildMesh(Manifest m, LodRow l, string name, HideFlags flags)
        {
            string file = MeshFile(m, l);
            if (!File.Exists(file)) throw new PostLedger308.Refused("mesh file missing: " + file);
            var bytes = File.ReadAllBytes(file);
            if (CliffCore308.Sha(bytes) != l.sha256) throw new PostLedger308.Refused("mesh file " + l.file + " does not match the manifest sha (re-run python Tools/Art/cliffskin308.py --stage " + m.stage + ")");
            var d = JsonUtility.FromJson<Kit>(Encoding.UTF8.GetString(bytes));
            int count = d == null || d.v == null ? 0 : d.v.Length / 3;
            if (count < 3 || count != l.vertices || d.n == null || d.n.Length != d.v.Length || d.sub == null || d.sub.Length != 1 || d.sub[0].t == null || d.sub[0].t.Length != l.triangles * 3)
                throw new PostLedger308.Refused("mesh file " + l.file + " does not hold " + l.vertices + " vertices / " + l.triangles + " triangles in one submesh");
            var v = new Vector3[count]; var n = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                v[i] = new Vector3(d.v[3 * i], d.v[3 * i + 1], d.v[3 * i + 2]);
                n[i] = new Vector3(d.n[3 * i], d.n[3 * i + 1], d.n[3 * i + 2]);
            }
            var mesh = new Mesh { name = name, hideFlags = flags, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.subMeshCount = 1; mesh.SetTriangles(d.sub[0].t, 0, false); mesh.RecalculateBounds();
            return mesh;
        }

        static string LodName(LodRow l) => Path.GetFileNameWithoutExtension(l.file);

        // ---------------------------------------------------------------- import

        [Serializable] sealed class ImportEntry { public string name = "", asset = "", guid = "", sourceSha256 = "", assetSha256 = ""; public int vertices, triangles; }
        [Serializable] sealed class ImportLedger
        {
            public string format = "cb308.skins.import.1", stage = "", utc = "", manifestSha256 = "", material = "", materialGuid = "", materialSource = "", materialSourceGuid = "";
            public ImportEntry[] entries = new ImportEntry[0]; public string[] history = new string[0];
        }

        static string ImportFile(CliffCore308.Config cfg, string stage) => CliffCore308.OutFile(cfg, "cb308-skins-import-" + stage + ".json");

        static ImportLedger ReadImport(CliffCore308.Config cfg, string stage)
        {
            string file = ImportFile(cfg, stage);
            if (!File.Exists(file)) return new ImportLedger { stage = stage };
            var l = JsonUtility.FromJson<ImportLedger>(File.ReadAllText(file, Encoding.UTF8));
            if (l == null || l.stage != stage) throw new PostLedger308.Refused("import ledger " + file + " does not belong to stage " + stage);
            if (l.entries == null) l.entries = new ImportEntry[0]; if (l.history == null) l.history = new string[0];
            return l;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static string Import(CliffCore308.Config cfg, Dictionary<string, string> opt, bool dry)
        {
            RequireIdle();
            string stage = opt.TryGetValue("stage", out var st) && st.Length > 0 ? st : DefaultImportStage();
            var m = Load(cfg, stage);
            var ledger = ReadImport(cfg, stage);
            var known = new Dictionary<string, ImportEntry>(); foreach (var e in ledger.entries) known[e.name] = e;

            // classify everything before the first write (source files, their shas, what is already imported)
            var todo = new List<(PanelRow p, LodRow l, string name, string asset, bool exists)>(); int same = 0; var names = new HashSet<string>();
            foreach (var p in m.panels)
                foreach (var l in p.lods)
                {
                    string name = LodName(l), asset = m.assetDir + "/" + name + ".asset", abs = PostLedger308.Abs(asset);
                    if (!names.Add(name)) throw new PostLedger308.Refused("the manifest names mesh " + name + " twice");
                    if (CliffCore308.ShaFile(MeshFile(m, l)) != l.sha256) throw new PostLedger308.Refused("mesh file " + l.file + " is missing or does not match the manifest (re-run python Tools/Art/cliffskin308.py --stage " + stage + ")");
                    bool exists = File.Exists(abs);
                    if (exists && known.TryGetValue(name, out var e) && e.asset == asset && e.sourceSha256 == l.sha256 && CliffCore308.ShaFile(abs) == e.assetSha256) { same++; continue; }
                    todo.Add((p, l, name, asset, exists));
                }
            string matPath = m.MaterialPath; bool matExists = File.Exists(PostLedger308.Abs(matPath));
            Material source = null;
            if (!matExists)
            {
                source = AssetDatabase.LoadAssetAtPath<Material>(m.materialSource);
                if (source == null) throw new PostLedger308.Refused("material source missing: " + m.materialSource);
                if (AssetDatabase.AssetPathToGUID(m.materialSource) != m.materialSourceGuid) throw new PostLedger308.Refused("material source " + m.materialSource + " has GUID " + AssetDatabase.AssetPathToGUID(m.materialSource) + ", the rules record " + m.materialSourceGuid);
                if (Glows(source)) throw new PostLedger308.Refused("material source " + m.materialSource + " has emission (ART-INK: a cliff skin never glows)");
            }
            else
            {
                var have = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (have == null) throw new PostLedger308.Refused("material file exists but does not load: " + matPath);
                if (Glows(have)) throw new PostLedger308.Refused("material " + matPath + " has emission (ART-INK: a cliff skin never glows)");
            }
            var stale = new List<string>();
            string absDir = PostLedger308.Abs(m.assetDir);
            if (Directory.Exists(absDir))
                foreach (string f in Directory.GetFiles(absDir, "*.asset")) if (!names.Contains(Path.GetFileNameWithoutExtension(f))) stale.Add(Path.GetFileName(f));
            int create = todo.Count(t => !t.exists), update = todo.Count(t => t.exists);
            string plan = "stage " + stage + " (manifest " + Short(m.Sha256) + ", height " + Short(m.heightSha256) + "): " + m.panels.Length + " panels, " + names.Count + " meshes -> create " + create + ", update " + update + ", unchanged " + same
                + "; material " + matPath + (matExists ? " present" : " to be copied from " + m.materialSource) + "; stale assets in " + m.assetDir + ": " + stale.Count + (stale.Count > 0 ? " (listed, never deleted: " + string.Join(", ", stale.Take(6)) + (stale.Count > 6 ? ", ..." : "") + ")" : "");
            if (dry) return "import dry | " + plan + " | nothing written";
            if (todo.Count == 0 && matExists && ledger.manifestSha256 == m.Sha256) return "import | " + plan + " | no change";

            EnsureFolder(m.assetDir); EnsureFolder(m.materialDir);
            foreach (var t in todo)
            {
                var mesh = BuildMesh(m, t.l, t.name, HideFlags.None);
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(t.asset); Mesh target;
                if (existing == null) { AssetDatabase.CreateAsset(mesh, t.asset); target = mesh; }
                else { EditorUtility.CopySerialized(mesh, existing); existing.name = t.name; Object.DestroyImmediate(mesh); target = existing; }
                if (!m.meshReadable)
                {
                    // the skin is never read at run time: keep only the GPU copy (the saved asset still holds every vertex)
                    var so = new SerializedObject(target); var readable = so.FindProperty("m_IsReadable");
                    if (readable != null && readable.boolValue) { readable.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                }
                EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
                known[t.name] = new ImportEntry { name = t.name, asset = t.asset, guid = AssetDatabase.AssetPathToGUID(t.asset), sourceSha256 = t.l.sha256, assetSha256 = CliffCore308.ShaFile(PostLedger308.Abs(t.asset)), vertices = t.l.vertices, triangles = t.l.triangles };
            }
            if (!matExists)
            {
                var copy = new Material(source) { name = m.materialName };
                AssetDatabase.CreateAsset(copy, matPath); AssetDatabase.SaveAssetIfDirty(copy);
            }
            ledger.stage = stage; ledger.utc = PostLedger308.Utc(); ledger.manifestSha256 = m.Sha256; ledger.material = matPath; ledger.materialGuid = AssetDatabase.AssetPathToGUID(matPath);
            ledger.materialSource = m.materialSource; ledger.materialSourceGuid = m.materialSourceGuid;
            ledger.entries = names.OrderBy(x => x, StringComparer.Ordinal).Select(x => known[x]).ToArray();
            ledger.history = ledger.history.Concat(new[] { ledger.utc + " import: created " + create + ", updated " + update + ", unchanged " + same + (matExists ? "" : ", material copied") }).ToArray();
            CliffCore308.WriteText(ImportFile(cfg, stage), JsonUtility.ToJson(ledger, true));
            return "import | " + plan + " | written; ledger " + ImportFile(cfg, stage);
        }

        /// <summary>The imported assets of a manifest, checked against the import ledger (file sha, GUID, vertex count). Refuses when anything is missing or stale.</summary>
        static Dictionary<string, Mesh> ImportedMeshes(CliffCore308.Config cfg, Manifest m, out Material material, out string importSha)
        {
            string file = ImportFile(cfg, m.stage);
            if (!File.Exists(file)) throw new PostLedger308.Refused("stage " + m.stage + " is not imported (run import:stage=" + m.stage + ")");
            importSha = CliffCore308.ShaFile(file);
            var ledger = ReadImport(cfg, m.stage);
            if (ledger.manifestSha256 != m.Sha256) throw new PostLedger308.Refused("the import ledger was written for manifest " + Short(ledger.manifestSha256) + ", the manifest on disk is " + Short(m.Sha256) + " (run import:stage=" + m.stage + ")");
            var known = new Dictionary<string, ImportEntry>(); foreach (var e in ledger.entries) known[e.name] = e;
            var meshes = new Dictionary<string, Mesh>();
            foreach (var p in m.panels)
                foreach (var l in p.lods)
                {
                    string name = LodName(l);
                    if (!known.TryGetValue(name, out var e) || e.sourceSha256 != l.sha256) throw new PostLedger308.Refused("mesh " + name + " is not imported from the current file (run import:stage=" + m.stage + ")");
                    if (!e.asset.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(e.asset)) throw new PostLedger308.Refused("unexpected mesh asset path " + e.asset);
                    if (CliffCore308.ShaFile(PostLedger308.Abs(e.asset)) != e.assetSha256) throw new PostLedger308.Refused("mesh asset " + e.asset + " changed since import (run import:stage=" + m.stage + ")");
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(e.asset);
                    if (mesh == null || AssetDatabase.AssetPathToGUID(e.asset) != e.guid) throw new PostLedger308.Refused("mesh asset " + e.asset + " does not load by its recorded GUID");
                    if (mesh.vertexCount != l.vertices) throw new PostLedger308.Refused("mesh asset " + e.asset + " has " + mesh.vertexCount + " vertices, the manifest " + l.vertices);
                    meshes[name] = mesh;
                }
            material = AssetDatabase.LoadAssetAtPath<Material>(m.MaterialPath);
            if (material == null) throw new PostLedger308.Refused("material missing: " + m.MaterialPath + " (run import:stage=" + m.stage + ")");
            if (Glows(material)) throw new PostLedger308.Refused("material " + m.MaterialPath + " has emission (ART-INK: a cliff skin never glows)");
            return meshes;
        }

        // ---------------------------------------------------------------- scene objects

        static StaticEditorFlags StaticFlags(Manifest m)
        {
            StaticEditorFlags f = 0;
            foreach (string name in m.staticFlags ?? new string[0])
            {
                if (!Enum.TryParse(name, out StaticEditorFlags one)) throw new PostLedger308.Refused("unknown static flag '" + name + "' in the manifest");
                f |= one;
            }
            return f;
        }

        static ShadowCastingMode Shadow(Manifest m)
        {
            if (!Enum.TryParse(m.shadowCasting, out ShadowCastingMode mode)) throw new PostLedger308.Refused("unknown shadowCasting '" + m.shadowCasting + "' in the manifest");
            return mode;
        }

        /// <summary>One panel: pivot object with a LODGroup and one renderer child per LOD. No collider is ever added.
        /// screenScale > 1 moves the LOD switches closer (the Mobile lodBias under the PC quality level); force >= 0 pins one LOD.</summary>
        static GameObject BuildPanel(Manifest m, PanelRow p, Mesh[] meshes, Material material, Transform parent, HideFlags flags, float screenScale, int force)
        {
            var shadow = Shadow(m); var staticFlags = StaticFlags(m);
            var go = new GameObject(p.id) { hideFlags = flags }; go.layer = m.layer;
            go.transform.SetParent(parent, false); go.transform.position = new Vector3(p.position[0], p.position[1], p.position[2]);
            var lods = new LOD[meshes.Length]; float above = 1f;
            for (int k = 0; k < meshes.Length; k++)
            {
                var c = new GameObject(p.id + "_LOD" + k) { hideFlags = flags }; c.layer = m.layer; c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                var r = c.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = shadow; r.receiveShadows = m.receiveShadows;
                r.lightProbeUsage = LightProbeUsage.BlendProbes; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                if (flags == HideFlags.None && staticFlags != 0) GameObjectUtility.SetStaticEditorFlags(c, staticFlags);
                // heights must fall from LOD to LOD; the last value is the cull height (0 = never culled)
                float h = k < meshes.Length - 1 ? Mathf.Min(p.screen[k] * screenScale, k == 0 ? .99f : above * .9f) : Mathf.Min(p.screen[k], above * .9f);
                lods[k] = new LOD(h, new Renderer[] { r }); above = h;
            }
            var group = go.AddComponent<LODGroup>(); group.fadeMode = LODFadeMode.None; group.SetLODs(lods); group.RecalculateBounds();
            if (force >= 0) group.ForceLOD(Mathf.Min(force, meshes.Length - 1));
            return go;
        }

        static GameObject[] Roots(Scene scene, string name) => scene.GetRootGameObjects().Where(g => g.name == name && CliffCore308.Saved(g)).ToArray();

        static bool Identity(Transform t) => t.localPosition == Vector3.zero && t.localRotation == Quaternion.identity && t.localScale == Vector3.one;

        /// <summary>True when root/Skins already holds exactly the manifest's panels on the imported meshes and material.</summary>
        static bool Matches(Transform skins, Manifest m, Dictionary<string, Mesh> meshes, Material material, out string why)
        {
            why = "";
            if (skins == null) { why = "no " + m.group; return false; }
            var groups = skins.GetComponentsInChildren<LODGroup>(true);
            if (groups.Length != m.panels.Length) { why = groups.Length + " panels, the manifest has " + m.panels.Length; return false; }
            var byName = new Dictionary<string, LODGroup>(); foreach (var g in groups) byName[g.name] = g;
            foreach (var p in m.panels)
            {
                if (!byName.TryGetValue(p.id, out var g)) { why = "panel " + p.id + " missing"; return false; }
                if ((g.transform.position - new Vector3(p.position[0], p.position[1], p.position[2])).sqrMagnitude > 1e-6f) { why = "panel " + p.id + " moved"; return false; }
                var lods = g.GetLODs();
                if (lods.Length != p.lods.Length) { why = "panel " + p.id + " has " + lods.Length + " LODs"; return false; }
                for (int k = 0; k < lods.Length; k++)
                {
                    var r = lods[k].renderers != null && lods[k].renderers.Length == 1 ? lods[k].renderers[0] as MeshRenderer : null;
                    var f = r != null ? r.GetComponent<MeshFilter>() : null;
                    if (f == null || f.sharedMesh != meshes[LodName(p.lods[k])] || r.sharedMaterial != material) { why = "panel " + p.id + " LOD" + k + " is not on the imported mesh / material"; return false; }
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- ledger of a scene

        [Serializable] sealed class Counts { public int renderers, triangles, batches, shadowCasters, colliders; }
        [Serializable] sealed class SkinLedger
        {
            public string format = "cb308.skins.1", scene = "", key = "", state = "none", stage = "", utc = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public Counts counts = new Counts();
            public string manifestSha256 = "", importLedgerSha256 = "", heightSha256 = "", material = "", materialGuid = "", countsNote = "";
            public int panels, meshRenderers; public string[] history = new string[0];
        }

        static string LedgerFile(CliffCore308.Config cfg, string key) => CliffCore308.OutFile(cfg, "cb308-skins-" + key + ".json");

        static SkinLedger ReadLedger(CliffCore308.Config cfg, string scenePath)
        {
            string key = CliffCore308.SceneKey(cfg, scenePath), file = LedgerFile(cfg, key);
            if (!File.Exists(file)) return new SkinLedger { scene = scenePath, key = key };
            var l = JsonUtility.FromJson<SkinLedger>(File.ReadAllText(file, Encoding.UTF8));
            if (l == null || l.scene != scenePath) throw new PostLedger308.Refused("ledger " + file + " does not belong to " + scenePath);
            if (l.history == null) l.history = new string[0]; if (l.counts == null) l.counts = new Counts();
            return l;
        }

        static void WriteLedger(CliffCore308.Config cfg, SkinLedger l, string note)
        {
            l.utc = PostLedger308.Utc(); l.history = l.history.Concat(new[] { l.utc + " " + note }).ToArray();
            CliffCore308.WriteText(LedgerFile(cfg, l.key), JsonUtility.ToJson(l, true));
        }

        // ---------------------------------------------------------------- plan / apply / revert

        static string Apply(CliffCore308.Config cfg, string token, Dictionary<string, string> opt, bool dry)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable(); RequireNoPreview();
            string stage = SceneStage(cfg, path, opt, out var terrain);
            var m = Load(cfg, stage);
            string mismatch = TerrainMismatch(terrain, m);
            if (mismatch != null) throw new PostLedger308.Refused(mismatch);
            var meshes = ImportedMeshes(cfg, m, out var material, out string importSha);
            if (m.panels.Length > m.budget.renderersMax || m.totals.lod0Triangles > m.budget.lod0TrisMax || m.totals.meshMemoryBytes > m.budget.meshMemoryBytesMax)
                throw new PostLedger308.Refused("the manifest is over its own budget (" + m.panels.Length + " panels / " + m.totals.lod0Triangles + " LOD0 triangles / " + m.totals.meshMemoryBytes + " bytes)");
            if (Shadow(m) != ShadowCastingMode.Off && m.budget.shadowCastersMax == 0) throw new PostLedger308.Refused("the manifest casts shadows but its budget allows 0 shadow casters");

            var scene = PostLedger308.Open(path);
            var ledger = ReadLedger(cfg, path);
            var roots = Roots(scene, m.root);
            if (roots.Length > 1) throw new PostLedger308.Refused(roots.Length + " roots named " + m.root + " in " + path);
            var root = roots.Length == 1 ? roots[0] : null;
            if (root != null && (!Identity(root.transform) || root.transform.parent != null)) throw new PostLedger308.Refused("root " + m.root + " is not at the identity transform");
            if (root != null && PostLedger308.UnderProtectedTree(root.transform)) throw new PostLedger308.Refused("root " + m.root + " is under a protected tree");
            var skins = root != null ? root.transform.Find(m.group) : null;
            bool matches = Matches(skins, m, meshes, material, out string why);
            int others = root != null ? root.transform.Cast<Transform>().Count(c => c.name != m.group) : 0;
            string plan = PostLedger308.Short(path) + " stage " + stage + ": " + (root == null ? "create root " + m.root : "root " + m.root + " present (" + others + " other children kept)") + ", "
                + (skins == null ? "create " : matches ? "keep " : "replace ") + m.group + " = " + m.panels.Length + " panels x " + m.panels[0].lods.Length + " LODs in " + m.panels.Select(p => p.stretch).Distinct().Count() + " stretches, material "
                + material.name + ", shadows " + m.shadowCasting + ", colliders 0" + (matches || skins == null ? "" : " (now: " + why + ")");
            if (matches && ledger.state == "applied" && ledger.manifestSha256 == m.Sha256 && ledger.importLedgerSha256 == importSha)
                return (dry ? "plan | " : "apply | ") + plan + " | no change (ledger applied " + ledger.utc + ")";
            if (dry) return "plan | " + plan + " | LOD0 " + m.totals.lod0Triangles + " triangles, mesh memory " + F(m.totals.meshMemoryBytes / 1048576f) + " MB | nothing written";

            string utc = PostLedger308.Utc(), key = CliffCore308.SceneKey(cfg, path);
            ledger.sceneShaBefore = CliffCore308.ShaFile(PostLedger308.Abs(path));
            ledger.backup = PostLedger308.Backup(PostLedger308.RepoPath(cfg.outDir + "/Backup/skins-" + key), utc, path).Replace('\\', '/');
            ledger.state = "applying"; ledger.stage = stage; ledger.manifestSha256 = m.Sha256; ledger.importLedgerSha256 = importSha; ledger.heightSha256 = m.heightSha256;
            ledger.material = m.MaterialPath; ledger.materialGuid = AssetDatabase.AssetPathToGUID(m.MaterialPath);
            WriteLedger(cfg, ledger, "apply started (backup " + ledger.backup + ")");

            if (root == null) { root = new GameObject(m.root); SceneManager.MoveGameObjectToScene(root, scene); root.layer = m.layer; }
            if (skins != null) Object.DestroyImmediate(skins.gameObject);
            var group = new GameObject(m.group); group.layer = m.layer; group.transform.SetParent(root.transform, false);
            var stretches = new Dictionary<string, Transform>();
            foreach (var p in m.panels)
            {
                if (!stretches.TryGetValue(p.stretch, out var parent))
                {
                    var s = new GameObject(p.stretch); s.layer = m.layer; s.transform.SetParent(group.transform, false); parent = s.transform; stretches[p.stretch] = parent;
                }
                BuildPanel(m, p, p.lods.Select(l => meshes[LodName(l)]).ToArray(), material, parent, HideFlags.None, 1f, -1);
            }
            PostLedger308.SaveScene(scene);

            Count(group.transform, m, out var counts, out int meshRenderers);
            ledger.sceneShaAfter = CliffCore308.ShaFile(PostLedger308.Abs(path)); ledger.state = "applied"; ledger.counts = counts; ledger.panels = m.panels.Length; ledger.meshRenderers = meshRenderers;
            ledger.countsNote = "renderers = panels (one LOD of a LODGroup is drawn at a time); triangles = LOD0 total; batches = panels x " + m.budget.passesPerRenderer + " passes (estimate, not measured)";
            WriteLedger(cfg, ledger, "applied stage " + stage + ": " + m.panels.Length + " panels, " + counts.triangles + " LOD0 triangles, colliders " + counts.colliders + ", shadow casters " + counts.shadowCasters);
            return "apply | " + plan + " | saved; scene sha " + Short(ledger.sceneShaBefore) + " -> " + Short(ledger.sceneShaAfter) + "; backup " + ledger.backup + "; ledger " + LedgerFile(cfg, key) + ". Next: verify:" + key;
        }

        static void Count(Transform skins, Manifest m, out Counts counts, out int meshRenderers)
        {
            var renderers = skins.GetComponentsInChildren<MeshRenderer>(true); meshRenderers = renderers.Length;
            counts = new Counts
            {
                renderers = skins.GetComponentsInChildren<LODGroup>(true).Length, triangles = m.totals.lod0Triangles, batches = m.panels.Length * m.budget.passesPerRenderer,
                shadowCasters = renderers.Count(r => r.shadowCastingMode != ShadowCastingMode.Off), colliders = skins.GetComponentsInChildren<Collider>(true).Length
            };
        }

        static string Revert(CliffCore308.Config cfg, string token, bool dry)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable(); RequireNoPreview();
            var ledger = ReadLedger(cfg, path);
            string rootName, groupName;
            {
                // root / group names come from the manifest of the ledger's stage (or of the terrain ledger's stage when nothing was applied)
                string stage = ledger.stage != "" ? ledger.stage : SceneStage(cfg, path, null, out _);
                var m = Load(cfg, stage); rootName = m.root; groupName = m.group;
            }
            var scene = PostLedger308.Open(path);
            var roots = Roots(scene, rootName);
            if (roots.Length > 1) throw new PostLedger308.Refused(roots.Length + " roots named " + rootName + " in " + path);
            var root = roots.Length == 1 ? roots[0] : null;
            var skins = root != null ? root.transform.Find(groupName) : null;
            if (skins == null) return "revert" + (dry ? " dry" : "") + " | " + PostLedger308.Short(path) + ": no " + rootName + "/" + groupName + " in the scene | nothing to do (ledger state " + ledger.state + ")";
            int panels = skins.GetComponentsInChildren<LODGroup>(true).Length, others = root.transform.Cast<Transform>().Count(c => c != skins);
            string plan = PostLedger308.Short(path) + ": remove " + rootName + "/" + groupName + " (" + panels + " panels); " + (others == 0 ? "the root has no other child and goes too" : "the root stays (" + others + " other children)");
            if (dry) return "revert dry | " + plan + " | nothing written";
            string utc = PostLedger308.Utc(), key = CliffCore308.SceneKey(cfg, path);
            ledger.sceneShaBefore = CliffCore308.ShaFile(PostLedger308.Abs(path));
            ledger.backup = PostLedger308.Backup(PostLedger308.RepoPath(cfg.outDir + "/Backup/skins-" + key), utc, path).Replace('\\', '/');
            Object.DestroyImmediate(skins.gameObject);
            if (root.transform.childCount == 0) Object.DestroyImmediate(root);
            PostLedger308.SaveScene(scene);
            ledger.sceneShaAfter = CliffCore308.ShaFile(PostLedger308.Abs(path)); ledger.state = "reverted"; ledger.counts = new Counts(); ledger.panels = 0; ledger.meshRenderers = 0;
            WriteLedger(cfg, ledger, "reverted: " + panels + " panels removed" + (others == 0 ? ", root removed" : ", root kept"));
            return "revert | " + plan + " | saved; scene sha " + Short(ledger.sceneShaBefore) + " -> " + Short(ledger.sceneShaAfter) + "; backup " + ledger.backup + " (mesh and material assets are kept)";
        }

        /// <summary>A scene save or switch while an in-memory preview is up would keep what the preview changed.</summary>
        static void RequireNoPreview()
        {
            var up = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && (g.hideFlags & HideFlags.DontSave) != 0
                && (g.hideFlags & HideFlags.HideInHierarchy) == 0 && g.name.EndsWith("_Preview", StringComparison.Ordinal)).Select(g => g.name).Distinct().ToArray();
            if (up.Length > 0) throw new PostLedger308.Refused("an in-memory preview is up (" + string.Join(", ", up) + "); run its preview:off first");
        }
    }
}
