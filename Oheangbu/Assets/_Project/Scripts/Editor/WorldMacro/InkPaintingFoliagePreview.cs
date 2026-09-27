using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Parent owns the preview lifetime. No scene, source material or source sheet is saved.
    // Prepare in Edit mode, Begin, allow an Editor update, capture/warm up, then End in finally.
    public static class InkPaintingFoliagePreview
    {
        const string MeshSource = "Oheangbu/CompactNaturalVegetation";
        const string CardSource = "Oheangbu/EarlyRegionFoliageCard";
        const string MeshStudy = "Oheangbu/Study/InkPaintingVegetation";
        const string CardStudy = "Oheangbu/Study/InkPaintingFoliageCard";
        static Preview active;
        public static bool IsActive => active != null;

        sealed class RendererBinding { public Renderer Target; public Material[] Original; }
        sealed class SheetBinding { public WorldMacroDressingRenderer Target; public WorldMacroDressingSheetSO Original; }
        sealed class PartBinding { public EarlyRegionFoliage.Part Target; public Material Original; }
        sealed class Preview
        {
            public string Folder;
            public Scene Scene;
            public bool WasDirty;
            public int MaterialCount, SheetParts;
            public SourceFile[] Sources;
            public readonly List<RendererBinding> Renderers = new List<RendererBinding>();
            public readonly List<SheetBinding> Sheets = new List<SheetBinding>();
            public readonly List<PartBinding> Parts = new List<PartBinding>();
            public readonly List<WorldMacroDressingSheetSO> Clones = new List<WorldMacroDressingSheetSO>();
        }
        sealed class Inventory
        {
            public Scene Scene;
            public Renderer[] Renderers;
            public WorldMacroDressingRenderer[] Dressing;
            public EarlyRegionFoliage[] Additional;
            public Material[] Materials;
        }
        [Serializable] sealed class SourceFile { public string path, sha256; }
        [Serializable] sealed class Receipt
        {
            public string utc, phase, status, scene;
            public int materials, rendererBindings, sheetBindings, clonedSheets, sheetParts, additionalParts;
            public bool wasDirty, isDirty, sourceFilesUnchanged;
            public SourceFile[] sources;
            public string[] errors;
        }

        public static string Prepare(string folder)
        {
            if (active != null) throw new InvalidOperationException("End the foliage preview before preparing materials");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Prepare foliage derivatives in Edit mode");
            folder = NormalizeFolder(folder);
            var inventory = Gather();
            if (inventory.Materials.Length == 0) throw new InvalidOperationException("No active compact foliage source materials found");
            // Resolve all shaders and asset identities before writing the first derivative.
            var shaders = inventory.Materials.ToDictionary(m => m, m => StudyShader(m));
            var paths = inventory.Materials.ToDictionary(m => m, m => MaterialPath(folder, m));
            var sources = SnapshotSources(inventory);
            EnsureFolder(folder + "/FoliageMaterials");
            foreach (var source in inventory.Materials)
            {
                Material copy = null;
                try
                {
                    copy = new Material(source) { name = source.name + "_InkPaintingFoliage", shader = shaders[source] };
                    if (!copy.HasProperty("_PaintedFoliage"))
                        throw new InvalidOperationException("Study shader lacks _PaintedFoliage: " + copy.shader.name);
                    copy.SetFloat("_PaintedFoliage", 1);
                    var existing = AssetDatabase.LoadAssetAtPath<Material>(paths[source]);
                    if (existing == null)
                    {
                        AssetDatabase.CreateAsset(copy, paths[source]);
                        existing = copy;
                        copy = null;
                    }
                    else
                    {
                        EditorUtility.CopySerialized(copy, existing);
                        EditorUtility.SetDirty(existing);
                    }
                    // Save only this owned derivative; never SaveAssets/SaveScene globally.
                    AssetDatabase.SaveAssetIfDirty(existing);
                }
                finally { if (copy != null) Object.DestroyImmediate(copy); }
            }
            bool unchanged = SourcesUnchanged(sources);
            WriteReceipt(folder, "foliage_prepare.json", new Receipt
            {
                utc = DateTime.UtcNow.ToString("o"), phase = "prepare", status = unchanged ? "PASS" : "FAIL",
                scene = inventory.Scene.path, materials = inventory.Materials.Length,
                rendererBindings = inventory.Renderers.Count(r => r.sharedMaterials.Any(IsSource)),
                sheetBindings = inventory.Dressing.Count(d => d.Sheet != null),
                wasDirty = inventory.Scene.isDirty, isDirty = inventory.Scene.isDirty,
                sourceFilesUnchanged = unchanged, sources = sources, errors = Array.Empty<string>()
            });
            if (!unchanged) throw new InvalidOperationException("A source file changed while preparing foliage derivatives; see foliage_prepare.json");
            return "Prepared " + inventory.Materials.Length + " foliage materials; sources unchanged, scene bindings untouched";
        }

        public static string Begin(string folder)
        {
            folder = NormalizeFolder(folder);
            if (active != null)
            {
                if (active.Folder != folder || active.Scene != SceneManager.GetActiveScene())
                    throw new InvalidOperationException("Another foliage preview is already active");
                return "Already active; " + Counts(active);
            }
            var inventory = Gather();
            if (inventory.Materials.Length == 0) throw new InvalidOperationException("No compact foliage sources found");
            var replacements = new Dictionary<Material, Material>();
            foreach (var source in inventory.Materials)
            {
                var replacement = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, source));
                var shader = StudyShader(source);
                if (replacement == null || replacement.shader != shader || !replacement.HasProperty("_PaintedFoliage") || replacement.GetFloat("_PaintedFoliage") < .5f)
                    throw new InvalidOperationException("Prepare the foliage derivative first: " + source.name);
                replacements.Add(source, replacement);
            }
            var work = new Preview
            {
                Folder = folder, Scene = inventory.Scene, WasDirty = inventory.Scene.isDirty,
                MaterialCount = replacements.Count, Sources = SnapshotSources(inventory)
            };
            active = work;
            Subscribe();
            try
            {
                foreach (var renderer in inventory.Renderers)
                {
                    var original = renderer.sharedMaterials;
                    if (!original.Any(m => m != null && replacements.ContainsKey(m))) continue;
                    work.Renderers.Add(new RendererBinding { Target = renderer, Original = original });
                    renderer.sharedMaterials = original.Select(m => Replace(m, replacements)).ToArray();
                }
                var clones = new Dictionary<WorldMacroDressingSheetSO, WorldMacroDressingSheetSO>();
                foreach (var dressing in inventory.Dressing)
                {
                    var original = dressing.Sheet;
                    if (original == null || !SheetParts(original).Any(p => p.Material != null && replacements.ContainsKey(p.Material))) continue;
                    if (!clones.TryGetValue(original, out var clone))
                    {
                        clone = Object.Instantiate(original);
                        clone.name = original.name + "_TransientInkFoliage";
                        clone.hideFlags = HideFlags.HideAndDontSave;
                        work.Clones.Add(clone);
                        // Instantiate must deep-copy the serialized managed hierarchy. Check before
                        // replacing any Part.Material, so a shallow copy can never modify the source.
                        var sourceParts = new HashSet<WorldMacroDressingSheetSO.Part>(SheetParts(original));
                        var cloneParts = SheetParts(clone).ToArray();
                        if (ReferenceEquals(clone.Prototypes, original.Prototypes) || cloneParts.Any(sourceParts.Contains))
                            throw new InvalidOperationException("Foliage sheet did not deep-clone its prototype parts");
                        foreach (var part in cloneParts)
                        {
                            var next = Replace(part.Material, replacements);
                            if (next == part.Material) continue;
                            part.Material = next;
                            work.SheetParts++;
                        }
                        clones.Add(original, clone);
                    }
                    work.Sheets.Add(new SheetBinding { Target = dressing, Original = original });
                    dressing.Sheet = clone;
                }
                var seen = new HashSet<EarlyRegionFoliage.Part>();
                foreach (var foliage in inventory.Additional)
                foreach (var part in AdditionalParts(foliage))
                {
                    if (!seen.Add(part)) continue;
                    var next = Replace(part.Material, replacements);
                    if (next == part.Material) continue;
                    work.Parts.Add(new PartBinding { Target = part, Original = part.Material });
                    part.Material = next;
                }
                foreach (var binding in work.Sheets) binding.Target.ResetCache();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                return "Foliage preview bound; " + Counts(work) + ". Allow an Editor update and warm caches before capture/performance sampling";
            }
            catch
            {
                Debug.LogWarning(End());
                throw;
            }
        }

        public static string End()
        {
            if (active == null) return "Foliage preview already restored";
            var work = active;
            active = null;
            Unsubscribe();
            var errors = new List<string>();
            // Restore every independent reference even if another target was destroyed/failed.
            foreach (var binding in work.Renderers)
                Attempt(() => { if (binding.Target != null) binding.Target.sharedMaterials = binding.Original; }, errors);
            foreach (var binding in work.Parts)
                Attempt(() => binding.Target.Material = binding.Original, errors);
            foreach (var binding in work.Sheets)
                Attempt(() => { if (binding.Target != null) binding.Target.Sheet = binding.Original; }, errors);
            foreach (var binding in work.Sheets)
                Attempt(() => { if (binding.Target != null) binding.Target.ResetCache(); }, errors);
            foreach (var clone in work.Clones)
                Attempt(() => { if (clone != null) Object.DestroyImmediate(clone); }, errors);
            foreach (var binding in work.Renderers)
                if (binding.Target != null && !binding.Target.sharedMaterials.SequenceEqual(binding.Original)) errors.Add("Renderer binding restoration mismatch: " + binding.Target.name);
            foreach (var binding in work.Sheets)
                if (binding.Target != null && binding.Target.Sheet != binding.Original) errors.Add("Sheet restoration mismatch: " + binding.Target.name);
            foreach (var binding in work.Parts)
                if (binding.Target.Material != binding.Original) errors.Add("Additional foliage part restoration mismatch");
            bool unchanged = false;
            Attempt(() => unchanged = SourcesUnchanged(work.Sources), errors);
            if (!unchanged) errors.Add("A source scene/material/sheet file changed during the preview");
            Attempt(() => WriteReceipt(work.Folder, "foliage_restore.json", new Receipt
            {
                utc = DateTime.UtcNow.ToString("o"), phase = "restore", status = errors.Count == 0 ? "PASS" : "FAIL",
                scene = work.Scene.path, materials = work.MaterialCount, rendererBindings = work.Renderers.Count,
                sheetBindings = work.Sheets.Count, clonedSheets = work.Clones.Count, sheetParts = work.SheetParts,
                additionalParts = work.Parts.Count, wasDirty = work.WasDirty,
                isDirty = work.Scene.IsValid() && work.Scene.isDirty, sourceFilesUnchanged = unchanged,
                sources = work.Sources, errors = errors.ToArray()
            }), errors);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            return (errors.Count == 0 ? "PASS restored; " : "FAIL restoration: " + string.Join("; ", errors) + "; ") + Counts(work);
        }

        static Inventory Gather()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != WorldMacroCompactAuthoring.TargetScene)
                throw new InvalidOperationException("The active compact scene is required");
            var roots = scene.GetRootGameObjects();
            var result = new Inventory
            {
                Scene = scene,
                Renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray(),
                Dressing = roots.SelectMany(r => r.GetComponentsInChildren<WorldMacroDressingRenderer>(true)).ToArray(),
                Additional = roots.SelectMany(r => r.GetComponentsInChildren<EarlyRegionFoliage>(true)).ToArray()
            };
            result.Materials = result.Renderers.SelectMany(r => r.sharedMaterials)
                .Concat(result.Dressing.Where(d => d.Sheet != null).SelectMany(d => SheetParts(d.Sheet)).Select(p => p.Material))
                .Concat(result.Additional.SelectMany(AdditionalParts).Select(p => p.Material))
                .Where(IsSource).Distinct().OrderBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal).ToArray();
            return result;
        }
        static bool IsSource(Material material) => material != null && material.shader != null &&
            (material.shader.name == MeshSource || material.shader.name == CardSource) &&
            material.HasProperty("_CIEnabled") && material.GetFloat("_CIEnabled") > .5f;
        static Shader StudyShader(Material source)
        {
            var name = source.shader.name == MeshSource ? MeshStudy : CardStudy;
            var shader = Shader.Find(name);
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Missing/unsupported/invalid foliage study shader: " + name);
            return shader;
        }
        static Material Replace(Material material, Dictionary<Material, Material> replacements) =>
            material != null && replacements.TryGetValue(material, out var replacement) ? replacement : material;
        static IEnumerable<WorldMacroDressingSheetSO.Part> SheetParts(WorldMacroDressingSheetSO sheet) =>
            (sheet.Prototypes ?? Array.Empty<WorldMacroDressingSheetSO.Prototype>()).Where(p => p != null)
            .SelectMany(p => p.Lods ?? Array.Empty<WorldMacroDressingSheetSO.Level>()).Where(l => l != null)
            .SelectMany(l => l.Parts ?? Array.Empty<WorldMacroDressingSheetSO.Part>()).Where(p => p != null);
        static IEnumerable<EarlyRegionFoliage.Part> AdditionalParts(EarlyRegionFoliage foliage) =>
            (foliage.Packets ?? Array.Empty<EarlyRegionFoliage.Packet>()).Where(p => p != null)
            .SelectMany(p => (p.Near ?? Array.Empty<EarlyRegionFoliage.Part>()).Concat(p.Far ?? Array.Empty<EarlyRegionFoliage.Part>())).Where(p => p != null);
        static string MaterialPath(string folder, Material source)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId) || string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("A persistent source material is required: " + source.name);
            return folder + "/FoliageMaterials/" + guid + "_" + localId + ".mat";
        }
        static SourceFile[] SnapshotSources(Inventory inventory) =>
            inventory.Materials.Select(AssetDatabase.GetAssetPath)
            .Concat(inventory.Dressing.Where(d => d.Sheet != null).Select(d => AssetDatabase.GetAssetPath(d.Sheet)))
            .Append(inventory.Scene.path).Where(p => !string.IsNullOrEmpty(p)).Distinct()
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => new SourceFile { path = p, sha256 = Digest(p) }).ToArray();
        static bool SourcesUnchanged(SourceFile[] sources) => sources.All(s => File.Exists(Absolute(s.path)) && Digest(s.path) == s.sha256);
        static string Digest(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(Absolute(path)))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string Absolute(string path) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
        static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An Assets folder is required", nameof(folder));
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Split('/').Any(s => s == ".." || s == "." || s.Length == 0))
                throw new ArgumentException("Use a project-relative folder inside Assets", nameof(folder));
            return folder;
        }
        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            if (split < 0) throw new ArgumentException("Invalid asset folder: " + folder);
            EnsureFolder(folder.Substring(0, split));
            AssetDatabase.CreateFolder(folder.Substring(0, split), folder.Substring(split + 1));
        }
        static void WriteReceipt(string folder, string name, Receipt receipt) => File.WriteAllText(Absolute(folder + "/" + name), JsonUtility.ToJson(receipt, true));
        static string Counts(Preview p) => p.MaterialCount + " materials, " + p.Renderers.Count + " renderers, " + p.Sheets.Count + " sheet bindings/" + p.Clones.Count + " clones, " + p.SheetParts + " sheet parts, " + p.Parts.Count + " near/far parts";
        static void Attempt(Action action, List<string> errors) { try { action(); } catch (Exception error) { errors.Add(error.GetType().Name + ": " + error.Message); } }
        static void RestoreForEditor()
        {
            try { string result = End(); if (result.StartsWith("FAIL", StringComparison.Ordinal)) Debug.LogError(result); }
            catch (Exception error) { Debug.LogException(error); }
        }
        static void BeforeSceneSave(Scene scene, string path) { if (active != null && scene == active.Scene) RestoreForEditor(); }
        static void BeforeSceneClose(Scene scene, bool removingScene) { if (active != null && scene == active.Scene) RestoreForEditor(); }
        static void BeforePlayChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode) RestoreForEditor();
        }
        static void Subscribe()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RestoreForEditor;
            EditorApplication.quitting += RestoreForEditor;
            EditorApplication.playModeStateChanged += BeforePlayChange;
            EditorSceneManager.sceneSaving += BeforeSceneSave;
            EditorSceneManager.sceneClosing += BeforeSceneClose;
        }
        static void Unsubscribe()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RestoreForEditor;
            EditorApplication.quitting -= RestoreForEditor;
            EditorApplication.playModeStateChanged -= BeforePlayChange;
            EditorSceneManager.sceneSaving -= BeforeSceneSave;
            EditorSceneManager.sceneClosing -= BeforeSceneClose;
        }
    }
}
