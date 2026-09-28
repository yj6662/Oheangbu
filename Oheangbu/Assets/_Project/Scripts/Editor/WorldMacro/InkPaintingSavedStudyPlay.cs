using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Play-only material cost A/B on the same SAVED geometry/population/sky. Never saves assets or scenes.</summary>
    [InitializeOnLoad]
    public static class InkPaintingSavedStudyPlay
    {
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string Study = "Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision/";
        const string LookReceipt = "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/PersistentApplication/20260917T001106470Z_bad21529/application.json";
        const string NavReceipt = "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/NavigationUpdate/progress.json";
        const string Scope = "MATERIAL_COST_ONLY: same saved geometry, deformation, population, LOD, lighting and sky; before=receipt original205 materials, after=current saved205 study materials. Not a pre-geometry or vegetation-count comparison.";
        static string Workspace => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string Full(string path) => Path.IsPathRooted(path) ? path : Path.Combine(path.StartsWith("Assets/", StringComparison.Ordinal) ? Path.GetDirectoryName(Application.dataPath) : Workspace, path);
        static string Output => Full("Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/SavedMaterialPerformance");
        [Serializable] sealed class NavGate { public bool installed = false, sceneSaved = false; public string status = null; }
        [Serializable] sealed class FileStamp { public string path = null, sha256 = null; }
        [Serializable] sealed class MaterialRow { public string source = null, derived = null; public long sourceLocalId = 0; }
        [Serializable] sealed class LookGate
        {
            public string status = null, scene = null; public bool sceneSaved = false, sourcesUnchanged = false, populationUnchanged = false, supplementaryPlacementUnchanged = false, structureUnchanged = false;
            public MaterialRow[] materials = null; public FileStamp[] sources = null;
        }
        sealed class RendererBinding { public Renderer target; public Material[] originals; }
        sealed class SheetBinding { public WorldMacroDressingRenderer target; public WorldMacroDressingSheetSO original, clone; }
        sealed class PartBinding { public EarlyRegionFoliage.Part part; public Material original; }
        sealed class Work
        {
            public double expires; public string id, sceneHash;
            public Dictionary<Material, Material> mapping;
            public readonly List<RendererBinding> renderers = new List<RendererBinding>();
            public readonly List<SheetBinding> sheets = new List<SheetBinding>();
            public readonly List<PartBinding> parts = new List<PartBinding>();
            public readonly List<WorldMacroDressingSheetSO> clones = new List<WorldMacroDressingSheetSO>();
            public readonly Dictionary<WorldMacroDressingSheetSO, string> originalSheets = new Dictionary<WorldMacroDressingSheetSO, string>();
            public readonly Dictionary<string, string> files = new Dictionary<string, string>();
            public int sheetParts, packetMatrices;
        }
        [Serializable] sealed class Receipt
        {
            public string utc, status, scope, sceneHash, error;
            public int mappingCount, renderers, sheetBindings, clonedSheets, sheetParts, additionalParts, packetMatrices;
            public bool sourceFilesUnchanged, originalSheetDataUnchanged, bindingsRestored;
        }
        static Work active;
        public static bool IsActive => active != null;
        public static bool HasInstalledNavigationGeneration
        {
            get
            {
                string path = Full(NavReceipt); if (!File.Exists(path)) return false;
                var gate = JsonUtility.FromJson<NavGate>(File.ReadAllText(path));
                // Selection only. RequireCurrentBake performs the authoritative hashes,
                // geometry generation,20surface and audit checks after this selection.
                return gate != null && gate.installed && gate.sceneSaved && gate.status == "INSTALLED_SAVED_EDIT_PATHS_VERIFIED";
            }
        }
        static InkPaintingSavedStudyPlay()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RestoreForEvent;
            EditorApplication.quitting += RestoreForEvent;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) RestoreForEvent(); };
            EditorSceneManager.sceneSaving += (scene, path) => RestoreForEvent();
            EditorSceneManager.sceneClosing += (scene, removing) => RestoreForEvent();
            EditorApplication.update += () => { if (active != null && (!Application.isPlaying || EditorApplication.timeSinceStartup > active.expires)) RestoreForEvent(); };
        }
        static T[] Components<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static IEnumerable<WorldMacroDressingSheetSO.Part> Parts(WorldMacroDressingSheetSO sheet) =>
            (sheet.Prototypes ?? Array.Empty<WorldMacroDressingSheetSO.Prototype>()).Where(p => p != null)
            .SelectMany(p => p.Lods ?? Array.Empty<WorldMacroDressingSheetSO.Level>()).Where(l => l != null)
            .SelectMany(l => l.Parts ?? Array.Empty<WorldMacroDressingSheetSO.Part>()).Where(p => p != null);
        static IEnumerable<EarlyRegionFoliage.Part> Parts(EarlyRegionFoliage value) =>
            (value.Packets ?? Array.Empty<EarlyRegionFoliage.Packet>()).Where(p => p != null)
            .SelectMany(p => (p.Near ?? Array.Empty<EarlyRegionFoliage.Part>()).Concat(p.Far ?? Array.Empty<EarlyRegionFoliage.Part>())).Where(p => p != null);
        static Material[] BoundMaterials() => Components<Renderer>().SelectMany(r => r.sharedMaterials)
            .Concat(Components<WorldMacroDressingRenderer>().Where(r => r.Sheet != null).SelectMany(r => Parts(r.Sheet)).Select(p => p.Material))
            .Concat(Components<EarlyRegionFoliage>().SelectMany(Parts).Select(p => p.Material)).Where(m => m != null).Distinct().ToArray();
        static void RequirePlay()
        {
            var sessions = Components<WorldMacroPlaytestSession>();
            if (!Application.isPlaying || EditorApplication.isCompiling || SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != ScenePath ||
                sessions.Length != 1 || string.IsNullOrEmpty(sessions[0].TestSaveSuffix))
                throw new InvalidOperationException("Saved compact scene in isolated Play is required; this helper never enters Play or changes a save suffix.");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("Scene reload must be enabled to discard runtime state on Play exit.");
            if (InkPaintingFoliagePreview.IsActive) throw new InvalidOperationException("End the older material preview before the saved-scene comparison.");
        }
        static string Hash(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(Full(path)); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static Dictionary<Material, Material> Mapping()
        {
            var gate = JsonUtility.FromJson<LookGate>(File.ReadAllText(Full(LookReceipt)));
            if (gate == null || gate.status != "PASS_PERSISTENT_APPLICATION" || gate.scene != ScenePath || !gate.sceneSaved || !gate.sourcesUnchanged || !gate.populationUnchanged ||
                !gate.supplementaryPlacementUnchanged || !gate.structureUnchanged || gate.materials == null || gate.materials.Length != 205 || gate.sources == null)
                throw new InvalidOperationException("The reviewed205-material saved-look receipt is required.");
            var sourceHashes = gate.sources.ToDictionary(s => s.path, s => s.sha256);
            var result = new Dictionary<Material, Material>(); var originals = new HashSet<Material>();
            foreach (var row in gate.materials)
            {
                if (!sourceHashes.TryGetValue(row.source, out var expected) || Hash(row.source) != expected || !row.derived.StartsWith(Study, StringComparison.Ordinal))
                    throw new InvalidOperationException("Original material changed or derived path escaped saved study: " + row.source);
                var source = AssetDatabase.LoadAllAssetsAtPath(row.source).OfType<Material>().SingleOrDefault(m =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string guid, out long id) && id == row.sourceLocalId);
                var derived = AssetDatabase.LoadAssetAtPath<Material>(row.derived);
                if (source == null || derived == null || source == derived || derived.shader == null || !derived.shader.name.StartsWith("Oheangbu/Study/InkPainting", StringComparison.Ordinal) || !originals.Add(source))
                    throw new InvalidOperationException("Invalid or duplicate persistent material mapping: " + row.source);
                result.Add(derived, source);
            }
            return result;
        }
        static void RequireSavedBindings(Dictionary<Material, Material> map)
        {
            var bound = BoundMaterials(); var original = new HashSet<Material>(map.Values);
            if (bound.Any(original.Contains) || map.Keys.Any(m => !bound.Contains(m)) || bound.Any(m => m.shader != null && m.shader.name.StartsWith("Oheangbu/Study/InkPainting", StringComparison.Ordinal) && !map.ContainsKey(m)))
                throw new InvalidOperationException("Expected all205 saved study materials and zero receipt-original bindings; scene is not the reviewed saved look.");
        }
        static Material Replace(Material material, Dictionary<Material, Material> map) => material != null && map.TryGetValue(material, out var source) ? source : material;
        public static string BeginOriginal()
        {
            RequirePlay(); if (active != null) throw new InvalidOperationException("Original-material comparison already active; restore before another run.");
            var map = Mapping(); RequireSavedBindings(map);
            var work = new Work { mapping = map, id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"), sceneHash = Hash(ScenePath), expires = EditorApplication.timeSinceStartup + 180 };
            foreach (var material in map.Keys.Concat(map.Values))
            {
                string path = AssetDatabase.GetAssetPath(material); work.files[path] = Hash(path); if (File.Exists(Full(path + ".meta"))) work.files[path + ".meta"] = Hash(path + ".meta");
            }
            work.files[ScenePath] = work.sceneHash; work.files[LookReceipt] = Hash(LookReceipt);
            active = work;
            try
            {
                foreach (var renderer in Components<Renderer>())
                {
                    var prior = renderer.sharedMaterials; if (!prior.Any(m => m != null && map.ContainsKey(m))) continue;
                    work.renderers.Add(new RendererBinding { target = renderer, originals = prior });
                    renderer.sharedMaterials = prior.Select(m => Replace(m, map)).ToArray();
                }
                var clones = new Dictionary<WorldMacroDressingSheetSO, WorldMacroDressingSheetSO>();
                foreach (var renderer in Components<WorldMacroDressingRenderer>())
                {
                    var original = renderer.Sheet; if (original == null || !Parts(original).Any(p => p.Material != null && map.ContainsKey(p.Material))) continue;
                    if (!clones.TryGetValue(original, out var clone))
                    {
                        work.originalSheets[original] = JsonUtility.ToJson(original);
                        string path = AssetDatabase.GetAssetPath(original); if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Expected saved dressing sheet."); work.files[path] = Hash(path);
                        clone = Object.Instantiate(original); work.clones.Add(clone); clone.hideFlags = HideFlags.HideAndDontSave;
                        var sourceParts = new HashSet<WorldMacroDressingSheetSO.Part>(Parts(original)); var cloneParts = Parts(clone).ToArray();
                        if (ReferenceEquals(clone.Prototypes, original.Prototypes) || cloneParts.Any(sourceParts.Contains)) throw new InvalidOperationException("Dressing sheet did not deep-clone serialized parts.");
                        foreach (var part in cloneParts) { var next = Replace(part.Material, map); if (next == part.Material) continue; part.Material = next; work.sheetParts++; }
                        clones.Add(original, clone);
                    }
                    work.sheets.Add(new SheetBinding { target = renderer, original = original, clone = clone }); renderer.Sheet = clone; renderer.ResetCache();
                }
                foreach (var part in Components<EarlyRegionFoliage>().SelectMany(Parts).Distinct())
                {
                    var next = Replace(part.Material, map); if (next == part.Material) continue;
                    work.parts.Add(new PartBinding { part = part, original = part.Material }); work.packetMatrices += part.Matrices?.Length ?? 0; part.Material = next;
                }
                if (BoundMaterials().Any(map.ContainsKey)) throw new InvalidOperationException("A saved study binding survived the original-material swap.");
                VerifyFiles(work); VerifySheets(work); Persist(work, "BEFORE_ORIGINAL_BOUND", false, "");
                return Scope + " Original materials bound; warm benchmark caches with the unchanged existing49-second camera run.";
            }
            catch { Restore(); throw; }
        }
        public static string AfterSaved()
        {
            RequirePlay(); string restored = Restore(); var map = Mapping(); RequireSavedBindings(map);
            return Scope + " Saved study materials active. " + restored;
        }
        public static string Restore()
        {
            var work = active; if (work == null) return "No temporary original-material bindings.";
            var errors = new List<string>();
            void Try(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex.Message); } }
            foreach (var b in work.renderers) Try(() => { if (b.target != null) { b.target.sharedMaterials = b.originals; if (!b.target.sharedMaterials.SequenceEqual(b.originals)) throw new InvalidOperationException("Renderer restore mismatch"); } });
            foreach (var b in work.parts) Try(() => { b.part.Material = b.original; });
            foreach (var b in work.sheets) Try(() => { if (b.target != null) { b.target.Sheet = b.original; b.target.ResetCache(); if (b.target.Sheet != b.original) throw new InvalidOperationException("Sheet restore mismatch"); } });
            Try(() => VerifyFiles(work)); Try(() => VerifySheets(work));
            // Retain references on failure so an explicit Restore/event can retry.
            if (errors.Count > 0) { Persist(work, "RESTORE_FAILED_RETRY_RETAINED", false, string.Join(" | ", errors)); throw new InvalidOperationException(string.Join(" | ", errors)); }
            foreach (var clone in work.clones) if (clone != null) Object.DestroyImmediate(clone);
            active = null; Persist(work, "AFTER_SAVED_RESTORED", true, "");
            return "Saved renderer/sheet/near-far bindings restored; sources and scene file unchanged.";
        }
        static void VerifyFiles(Work work) { foreach (var pair in work.files) if (Hash(pair.Key) != pair.Value) throw new InvalidOperationException("File changed during material-only comparison: " + pair.Key); }
        static void VerifySheets(Work work) { foreach (var pair in work.originalSheets) if (pair.Key != null && JsonUtility.ToJson(pair.Key) != pair.Value) throw new InvalidOperationException("Original dressing sheet data changed."); }
        static void RestoreForEvent() { if (active == null) return; try { Restore(); } catch (Exception ex) { Debug.LogException(ex); } }
        static void Persist(Work work, string status, bool restored, string error)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, work.id + "_" + status + ".json"), JsonUtility.ToJson(new Receipt {
                utc = DateTime.UtcNow.ToString("o"), status = status, scope = Scope, sceneHash = work.sceneHash, error = error, mappingCount = work.mapping.Count,
                renderers = work.renderers.Count, sheetBindings = work.sheets.Count, clonedSheets = work.clones.Count, sheetParts = work.sheetParts,
                additionalParts = work.parts.Count, packetMatrices = work.packetMatrices, sourceFilesUnchanged = error == "", originalSheetDataUnchanged = error == "", bindingsRestored = restored
            }, true));
        }
    }
}
