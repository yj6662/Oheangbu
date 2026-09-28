using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Explicit persistent installation of the prepared compact painting study.</summary>
    public static class InkPaintingPersistentApplication
    {
        const string Folder = "Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision";
        const string Persistent = Folder + "/Persistent";
        const string SkyField = "_regionalSkyProfile";
        static string Output => WorldMacroCompactAuthoring.Output + "/InkLandscape/InkPaintingStudy/FlowRevision/PersistentApplication";

        [Serializable] sealed class Stamp { public string path, sha256; }
        [Serializable] sealed class Mapping { public string source, derived; public long sourceLocalId; }
        [Serializable] sealed class Receipt
        {
            public string utc, status, scene, backup, sceneBeforeSha256, sceneAfterSha256, error;
            public bool cleanSceneRequired = true, sourcesUnchanged, populationUnchanged, supplementaryPlacementUnchanged, structureUnchanged, sceneSaved;
            public int rendererBindings, sheetBindings, skyBindings, sheetParts, supplementaryParts;
            public Stamp[] sources;
            public Mapping[] materials, sheets, skies;
            public string[] createdAssets, rollbackErrors;
        }
        sealed class RendererBinding { public Renderer target; public Material[] original; }
        sealed class SheetBinding { public WorldMacroDressingRenderer target; public WorldMacroDressingSheetSO original; }
        sealed class PartBinding { public EarlyRegionFoliage.Part target; public Material original; }
        sealed class SkyBinding { public WorldLookDriver target; public RegionalInkSkyProfile original; }

        /// <summary>Call only after restoring the temporary preview and preserving any user edits.</summary>
        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                !scene.IsValid() || !scene.isLoaded || scene.path != WorldMacroCompactAuthoring.TargetScene)
                throw new InvalidOperationException("Stable compact Edit scene required for persistent painting application.");
            if (scene.isDirty) throw new InvalidOperationException("The compact scene has unsaved changes. Preserve or restore those changes before applying; this helper never saves an initially dirty scene.");
            if (InkPaintingFoliagePreview.IsActive) throw new InvalidOperationException("Restore the temporary painting preview before persistent application.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; application stopped.");

            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
            var dressing = roots.SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true)).ToArray();
            var supplements = roots.SelectMany(g => g.GetComponentsInChildren<EarlyRegionFoliage>(true)).ToArray();
            var drivers = roots.SelectMany(g => g.GetComponentsInChildren<WorldLookDriver>(true)).ToArray();
            var allMaterials = renderers.SelectMany(r => r.sharedMaterials)
                .Concat(dressing.Where(d => d.Sheet != null).SelectMany(d => SheetParts(d.Sheet)).Select(p => p.Material))
                .Concat(supplements.SelectMany(AdditionalParts).Select(p => p.Material)).Where(m => m != null).Distinct().ToArray();
            var sourceMaterials = allMaterials.Where(IsSource).ToArray();
            if (sourceMaterials.Length == 0) return VerifyAlreadyApplied(scene, dressing, drivers, allMaterials);
            if (allMaterials.Any(IsStudy)) throw new InvalidOperationException("Mixed temporary/study bindings found. Restore the full preview before applying.");
            if (dressing.Length == 0 || dressing.Any(d => d.Sheet == null) || drivers.Length == 0)
                throw new InvalidOperationException("Expected authored dressing and sky driver references are missing.");

            // Resolve every source and prepared destination before the first write.
            var replacements = new Dictionary<Material, Material>();
            foreach (var source in sourceMaterials)
            {
                RequireCleanPersistent(source);
                string shaderName = StudyShaderName(source.shader.name);
                string path = IsFoliage(source) ? Folder + "/FoliageMaterials/" + AssetKey(source) + ".mat" :
                    Folder + "/Materials/" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)) + ".mat";
                var derived = AssetDatabase.LoadAssetAtPath<Material>(path);
                RequireCleanPersistent(derived);
                if (derived.shader == null || derived.shader.name != shaderName || !derived.shader.isSupported || ShaderUtil.ShaderHasError(derived.shader) ||
                    derived.passCount != source.passCount || derived.renderQueue != source.renderQueue || derived.enableInstancing != source.enableInstancing ||
                    (IsFoliage(source) && (!derived.HasProperty("_PaintedFoliage") || derived.GetFloat("_PaintedFoliage") < .5f)))
                    throw new InvalidOperationException("Prepared derivative failed shader/pass/queue/instancing checks: " + path);
                replacements.Add(source, derived);
            }
            var sourceSheets = dressing.Select(d => d.Sheet).Distinct().ToArray();
            foreach (var sheet in sourceSheets) RequireCleanPersistent(sheet);
            var skies = drivers.Select(d => new SkyBinding { target = d, original = ReadSky(d) }).ToArray();
            foreach (var sky in skies) RequireCleanPersistent(sky.original);
            var protectedObjects = sourceMaterials.Cast<Object>().Concat(sourceSheets).Concat(skies.Select(s => (Object)s.original))
                .Concat(sourceSheets.Where(s => s.Geography != null).Select(s => (Object)s.Geography)).ToList();
            foreach (var driver in drivers)
            {
                var serialized = new SerializedObject(driver);
                foreach (string name in new[] { "_palette", "_inkSkyProfile", "_skyGeography", "_skyboxMaterial" })
                {
                    var reference = serialized.FindProperty(name)?.objectReferenceValue;
                    if (reference != null) { RequireCleanPersistent(reference); protectedObjects.Add(reference); }
                }
            }
            string[] sourcePaths = protectedObjects.Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var stamps = sourcePaths.Select(p => new Stamp { path = p, sha256 = FileDigest(p) }).ToArray();
            string geometry = Structure(scene), supplementary = SupplementSignature(supplements);
            string run = Output + "/" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(run);
            string backup = run + "/before.unity";
            File.Copy(Absolute(scene.path), backup, false);
            if (File.Exists(Absolute(scene.path + ".meta"))) File.Copy(Absolute(scene.path + ".meta"), backup + ".meta", false);
            var receipt = new Receipt { utc = DateTime.UtcNow.ToString("O"), status = "PREPARED", scene = scene.path, backup = backup,
                sceneBeforeSha256 = FileDigest(scene.path), sources = stamps,
                materials = replacements.Select(p => Map(p.Key, p.Value)).ToArray() };
            WriteReceipt(run + "/application.json", receipt);

            var changedRenderers = new List<RendererBinding>(); var changedSheets = new List<SheetBinding>(); var changedParts = new List<PartBinding>();
            var created = new List<string>(); var derivedSheets = new Dictionary<WorldMacroDressingSheetSO, WorldMacroDressingSheetSO>();
            var derivedSkies = new Dictionary<RegionalInkSkyProfile, RegionalInkSkyProfile>();
            bool sceneTouched = false;
            try
            {
                EnsureFolder(Persistent);
                foreach (var source in sourceSheets)
                {
                    string path = Persistent + "/Dressing_" + AssetKey(source) + ".asset";
                    var clone = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(path);
                    if (clone == null)
                    {
                        if (File.Exists(Absolute(path))) throw new IOException("An incompatible asset already occupies the owned dressing path: " + path);
                        clone = Object.Instantiate(source); clone.name = source.name; clone.hideFlags = HideFlags.None;
                        try
                        {
                            AssertDeepParts(source, clone);
                            foreach (var part in SheetParts(clone)) part.Material = Replace(part.Material, replacements);
                            VerifySheet(source, clone, replacements);
                            AssetDatabase.CreateAsset(clone, path); created.Add(path); AssetDatabase.SaveAssetIfDirty(clone);
                        }
                        catch { if (!AssetDatabase.Contains(clone)) Object.DestroyImmediate(clone); throw; }
                    }
                    else { RequireCleanPersistent(clone); VerifySheet(source, clone, replacements); }
                    derivedSheets.Add(source, clone);
                }
                foreach (var source in skies.Select(s => s.original).Distinct())
                {
                    string path = Persistent + "/RegionalSky_" + AssetKey(source) + ".asset";
                    var expected = Object.Instantiate(source); expected.name = source.name; expected.hideFlags = HideFlags.None;
                    try
                    {
                        if (ReferenceEquals(source.Regions, expected.Regions) || expected.Regions.Any(e => source.Regions.Contains(e)))
                            throw new InvalidOperationException("Regional sky did not deep-clone its entries.");
                        foreach (var entry in expected.Regions)
                        {
                            entry.Horizon = new Color(.98f, .93f, .83f, 1);
                            entry.Zenith = new Color(.86f, .82f, .74f, 1) + (entry.Zenith - new Color(.69f, .71f, .68f, 1)) * .2f;
                            entry.Cloud = new Color(.75f, .72f, .66f, 1); entry.CloudDensity *= .6f;
                        }
                        var clone = AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(path);
                        if (clone == null) { if (File.Exists(Absolute(path))) throw new IOException("An incompatible asset already occupies the owned sky path: " + path); clone = expected; AssetDatabase.CreateAsset(clone, path); created.Add(path); AssetDatabase.SaveAssetIfDirty(clone); expected = null; }
                        else { RequireCleanPersistent(clone); if (JsonUtility.ToJson(clone) != JsonUtility.ToJson(expected)) throw new InvalidOperationException("Existing owned sky differs from the current preview: " + path); }
                        derivedSkies.Add(source, clone);
                    }
                    finally { if (expected != null) Object.DestroyImmediate(expected); }
                }
                receipt.sheets = derivedSheets.Select(p => Map(p.Key, p.Value)).ToArray();
                receipt.skies = derivedSkies.Select(p => Map(p.Key, p.Value)).ToArray();
                if (!SourcesUnchanged(stamps)) throw new InvalidOperationException("Source files changed while creating derivatives.");
                if (scene.isDirty) throw new InvalidOperationException("Scene became dirty while preparing assets; no scene bindings were changed.");
                sceneTouched = true;
                foreach (var renderer in renderers)
                {
                    var originals = renderer.sharedMaterials;
                    if (!originals.Any(m => m != null && replacements.ContainsKey(m))) continue;
                    changedRenderers.Add(new RendererBinding { target = renderer, original = originals });
                    renderer.sharedMaterials = originals.Select(m => Replace(m, replacements)).ToArray(); Dirty(renderer);
                }
                foreach (var renderer in dressing)
                {
                    changedSheets.Add(new SheetBinding { target = renderer, original = renderer.Sheet });
                    renderer.Sheet = derivedSheets[renderer.Sheet]; Dirty(renderer);
                }
                var seen = new HashSet<EarlyRegionFoliage.Part>();
                foreach (var foliage in supplements)
                {
                    bool changed = false;
                    foreach (var part in AdditionalParts(foliage))
                    {
                        if (!seen.Add(part)) continue;
                        var next = Replace(part.Material, replacements); if (next == part.Material) continue;
                        changedParts.Add(new PartBinding { target = part, original = part.Material }); part.Material = next; changed = true;
                    }
                    if (changed) Dirty(foliage);
                }
                foreach (var sky in skies) WriteSky(sky.target, derivedSkies[sky.original]);
                receipt.rendererBindings = changedRenderers.Count; receipt.sheetBindings = changedSheets.Count; receipt.skyBindings = skies.Length;
                receipt.sheetParts = derivedSheets.Values.Sum(s => SheetParts(s).Count(p => replacements.Values.Contains(p.Material)));
                receipt.supplementaryParts = changedParts.Count;
                foreach (var pair in derivedSheets) VerifySheet(pair.Key, pair.Value, replacements);
                receipt.populationUnchanged = true;
                receipt.supplementaryPlacementUnchanged = supplementary == SupplementSignature(supplements);
                receipt.structureUnchanged = geometry == Structure(scene);
                receipt.sourcesUnchanged = SourcesUnchanged(stamps);
                if (!receipt.supplementaryPlacementUnchanged || !receipt.structureUnchanged || !receipt.sourcesUnchanged)
                    throw new InvalidOperationException("Pre-save source/placement/geometry preservation check failed.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the explicitly targeted compact scene.");
                receipt.sceneSaved = true;
                if (scene.isDirty || changedRenderers.Any(b => !b.target.sharedMaterials.SequenceEqual(b.original.Select(m => Replace(m, replacements)))) ||
                    changedSheets.Any(b => b.target.Sheet != derivedSheets[b.original]) || changedParts.Any(b => b.target.Material != Replace(b.original, replacements)) ||
                    skies.Any(s => ReadSky(s.target) != derivedSkies[s.original]) || !SourcesUnchanged(stamps))
                    throw new InvalidOperationException("Post-save binding/source validation failed.");
                foreach (var renderer in dressing) renderer.ResetCache();
                RefreshSky(skies.Select(s => s.target));
                receipt.status = "PASS_PERSISTENT_APPLICATION"; receipt.sceneAfterSha256 = FileDigest(scene.path); receipt.createdAssets = created.ToArray();
                WriteReceipt(run + "/application.json", receipt); WriteReceipt(Output + "/application.json", receipt);
                EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
                return JsonUtility.ToJson(receipt, true);
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                void Attempt(Action action) { try { action(); } catch (Exception failure) { failures.Add(failure.Message); } }
                foreach (var binding in changedRenderers) Attempt(() => { binding.target.sharedMaterials = binding.original; Dirty(binding.target); });
                foreach (var binding in changedParts) Attempt(() => binding.target.Material = binding.original);
                foreach (var binding in changedSheets) Attempt(() => { binding.target.Sheet = binding.original; Dirty(binding.target); binding.target.ResetCache(); });
                if (sceneTouched) foreach (var sky in skies) Attempt(() => WriteSky(sky.target, sky.original));
                if (sceneTouched) Attempt(() => RefreshSky(drivers));
                // The input scene was clean and its exact bytes were preserved before mutation.
                // Restore those bytes on a failed save without reloading/closing any open scene.
                if (sceneTouched && FileDigest(scene.path) != receipt.sceneBeforeSha256) Attempt(() => File.Copy(backup, Absolute(scene.path), true));
                if (failures.Count == 0) foreach (string path in created) Attempt(() => { if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Could not remove failed owned derivative: " + path); });
                receipt.status = failures.Count == 0 ? "FAILED_ROLLED_BACK" : "FAILED_ROLLBACK_INCOMPLETE"; receipt.error = error.ToString(); receipt.rollbackErrors = failures.ToArray();
                receipt.sourcesUnchanged = SourcesUnchanged(stamps); receipt.sceneAfterSha256 = FileDigest(scene.path); receipt.createdAssets = created.ToArray();
                WriteReceipt(run + "/application.json", receipt);
                throw new InvalidOperationException("Persistent application failed; original bindings restored where possible. The scene may remain dirty for review; no dirty state was silently saved. Receipt: " + run + "/application.json", error);
            }
        }

        static string VerifyAlreadyApplied(Scene scene, WorldMacroDressingRenderer[] dressing, WorldLookDriver[] drivers, Material[] materials)
        {
            string path = Output + "/application.json";
            if (!File.Exists(path)) throw new InvalidOperationException("No source bindings or completed persistent application receipt found.");
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(path));
            if (receipt.status != "PASS_PERSISTENT_APPLICATION" || receipt.scene != scene.path || !SourcesUnchanged(receipt.sources) ||
                dressing.Length != receipt.sheetBindings || drivers.Length != receipt.skyBindings ||
                dressing.Any(d => d.Sheet == null || !receipt.sheets.Any(m => m.derived == AssetDatabase.GetAssetPath(d.Sheet))) ||
                drivers.Any(d => !receipt.skies.Any(m => m.derived == AssetDatabase.GetAssetPath(ReadSky(d)))) ||
                receipt.materials.Any(p => !materials.Any(m => AssetDatabase.GetAssetPath(m) == p.derived)) ||
                materials.Where(IsStudy).Any(m => !receipt.materials.Any(p => p.derived == AssetDatabase.GetAssetPath(m))))
                throw new InvalidOperationException("Existing persistent bindings/source hashes differ from the receipt; no automatic overwrite performed.");
            var replacements = receipt.materials.ToDictionary(m => LoadMaterial(m.source, m.sourceLocalId), m => AssetDatabase.LoadAssetAtPath<Material>(m.derived));
            foreach (var mapping in receipt.sheets) VerifySheet(AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(mapping.source), AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(mapping.derived), replacements);
            return "Already persistently applied; source hashes and dressing population match. " + path;
        }
        static void VerifySheet(WorldMacroDressingSheetSO source, WorldMacroDressingSheetSO derived, Dictionary<Material, Material> replacements)
        {
            if (source == null || derived == null || source == derived) throw new InvalidOperationException("Independent source and derivative dressing sheets required.");
            AssertDeepParts(source, derived);
            var original = SheetParts(source).ToArray(); var actual = SheetParts(derived).ToArray();
            if (original.Length != actual.Length || actual.Where((p, i) => p.Material != Replace(original[i].Material, replacements)).Any())
                throw new InvalidOperationException("A dressing LOD material was omitted or unexpectedly changed.");
            var changed = actual.Select(p => p.Material).ToArray();
            try
            {
                // Compare the complete serialized population, including seeds, habitats, all
                // heights, fixed placements, density/retention, LOD, wind and part transforms.
                // Temporarily normalize only this owned derivative's material references.
                for (int i = 0; i < actual.Length; i++) actual[i].Material = original[i].Material;
                if (JsonUtility.ToJson(source) != JsonUtility.ToJson(derived)) throw new InvalidOperationException("Dressing data differs beyond material references.");
            }
            finally { for (int i = 0; i < actual.Length; i++) actual[i].Material = changed[i]; }
        }
        static void AssertDeepParts(WorldMacroDressingSheetSO source, WorldMacroDressingSheetSO clone)
        {
            var originals = new HashSet<WorldMacroDressingSheetSO.Part>(SheetParts(source));
            if (ReferenceEquals(source.Prototypes, clone.Prototypes) || SheetParts(clone).Any(originals.Contains))
                throw new InvalidOperationException("Dressing parts were not deep-cloned; source mutation prevented.");
        }
        static RegionalInkSkyProfile ReadSky(WorldLookDriver driver) => new SerializedObject(driver).FindProperty(SkyField)?.objectReferenceValue as RegionalInkSkyProfile;
        static void WriteSky(WorldLookDriver driver, RegionalInkSkyProfile sky)
        {
            var serialized = new SerializedObject(driver); var property = serialized.FindProperty(SkyField);
            if (property == null) throw new InvalidOperationException("Regional sky serialized field missing.");
            property.objectReferenceValue = sky; serialized.ApplyModifiedPropertiesWithoutUndo(); Dirty(driver);
        }
        static void RefreshSky(IEnumerable<WorldLookDriver> drivers)
        {
            var camera = Camera.main; var position = camera != null ? camera.transform.position : Vector3.zero;
            foreach (var driver in drivers) driver.PreviewRegionalSky(position);
        }
        static void Dirty(Object target) { EditorUtility.SetDirty(target); if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target); }
        static bool IsFoliage(Material material) => material.shader.name == "Oheangbu/CompactNaturalVegetation" || material.shader.name == "Oheangbu/EarlyRegionFoliageCard";
        static bool IsSource(Material material) => material != null && material.shader != null && material.HasProperty("_CIEnabled") && material.GetFloat("_CIEnabled") > .5f && StudyShaderName(material.shader.name) != null;
        static bool IsStudy(Material material) => material != null && material.shader != null && material.shader.name.StartsWith("Oheangbu/Study/InkPainting", StringComparison.Ordinal);
        static string StudyShaderName(string source)
        {
            switch (source) { case "Oheangbu/CompactNaturalGround": return "Oheangbu/Study/InkPaintingGround";
                case "Oheangbu/WorldMacroTerrain": return "Oheangbu/Study/InkPaintingTerrain";
                case "Oheangbu/CompactNaturalVegetation": return "Oheangbu/Study/InkPaintingVegetation";
                case "Oheangbu/EarlyRegionFoliageCard": return "Oheangbu/Study/InkPaintingFoliageCard"; default: return null; }
        }
        static Material Replace(Material source, Dictionary<Material, Material> mapping) => source != null && mapping.TryGetValue(source, out var derived) ? derived : source;
        static IEnumerable<WorldMacroDressingSheetSO.Part> SheetParts(WorldMacroDressingSheetSO sheet) => (sheet.Prototypes ?? Array.Empty<WorldMacroDressingSheetSO.Prototype>()).Where(p => p != null).SelectMany(p => p.Lods ?? Array.Empty<WorldMacroDressingSheetSO.Level>()).Where(l => l != null).SelectMany(l => l.Parts ?? Array.Empty<WorldMacroDressingSheetSO.Part>()).Where(p => p != null);
        static IEnumerable<EarlyRegionFoliage.Part> AdditionalParts(EarlyRegionFoliage foliage) => (foliage.Packets ?? Array.Empty<EarlyRegionFoliage.Packet>()).Where(p => p != null).SelectMany(p => (p.Near ?? Array.Empty<EarlyRegionFoliage.Part>()).Concat(p.Far ?? Array.Empty<EarlyRegionFoliage.Part>())).Where(p => p != null);
        static void RequireCleanPersistent(Object asset)
        {
            if (asset == null || !AssetDatabase.Contains(asset) || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)) || (asset.hideFlags & HideFlags.DontSave) != 0)
                throw new InvalidOperationException("A persistent prepared asset is required; transient previews cannot be installed.");
            if (EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Unsaved source/derived asset must be preserved first: " + AssetDatabase.GetAssetPath(asset));
        }
        static string AssetKey(Object asset) { if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)) throw new InvalidOperationException("Persistent asset identity missing: " + asset.name); return guid + "_" + localId; }
        static Mapping Map(Object source, Object derived) { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long localId); return new Mapping { source = AssetDatabase.GetAssetPath(source), derived = AssetDatabase.GetAssetPath(derived), sourceLocalId = localId }; }
        static Material LoadMaterial(string path, long localId) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Single(m => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string _, out long id); return id == localId; });
        static string Absolute(string path) => Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
        static string FileDigest(string path) { using (var hash = SHA256.Create()) using (var file = File.OpenRead(Absolute(path))) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        static bool SourcesUnchanged(Stamp[] stamps) => stamps != null && stamps.All(s => File.Exists(Absolute(s.path)) && FileDigest(s.path) == s.sha256);
        static string TextDigest(string value) { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static string PathOf(Transform t) => (t.parent == null ? "" : PathOf(t.parent) + "/") + t.GetSiblingIndex() + ":" + t.name;
        static string MeshIdentity(Mesh mesh) => mesh == null ? "null" : AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) ? guid + "_" + localId : "transient:" + mesh.GetInstanceID() + ":" + mesh.vertexCount;
        static string Structure(Scene scene) => TextDigest(string.Join("\n", scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => (t.gameObject.hideFlags & HideFlags.DontSave) == 0).Select(t => PathOf(t) + "|" + t.gameObject.activeSelf + "|" + t.gameObject.layer + "|" + t.localPosition.ToString("R") + "|" + t.localRotation.ToString("R") + "|" + t.localScale.ToString("R") + "|" + string.Join(",", t.GetComponents<MeshFilter>().Select(f => MeshIdentity(f.sharedMesh))) + "|" + string.Join(",", t.GetComponents<Collider>().Select(c => c.GetType().Name + EditorJsonUtility.ToJson(c)))).OrderBy(s => s, StringComparer.Ordinal)));
        static string SupplementSignature(EarlyRegionFoliage[] foliage)
        {
            // Only material references may differ: packet bounds/counts, mesh/submesh, every
            // near/far matrix and their order are covered by this serialized comparison.
            return TextDigest(string.Join("\n", foliage.OrderBy(f => PathOf(f.transform), StringComparer.Ordinal).Select(f => PathOf(f.transform) + System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(f), "\\\"Material\\\"\\s*:\\s*\\{\\s*\\\"instanceID\\\"\\s*:\\s*-?\\d+\\s*\\}", "\"Material\":null"))));
        }
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split)); if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1)))) throw new IOException("Could not create owned asset folder: " + path); }
        static void WriteReceipt(string path, Receipt receipt) { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true)); if (File.Exists(path)) File.Delete(path); File.Move(temporary, path); }
    }
}
