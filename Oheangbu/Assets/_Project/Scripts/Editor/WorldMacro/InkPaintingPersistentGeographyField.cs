using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Refresh only the owned study materials' geography guidance after saved geometry installation.</summary>
    public static class InkPaintingPersistentGeographyField
    {
        const string MaterialsFolder = "Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision";
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string FieldProperty = "_PaintedGeoField", RectProperty = "_PaintedGeoRect";
        const string BakerSource = "Assets/_Project/Scripts/Editor/WorldMacro/InkPaintingGeographyField.cs";
        [Serializable] public sealed class Settings
        {
            public string geometryApplicationReceiptPath;
            public string assetFolder = MaterialsFolder + "/FinalGeographyFields";
            public string outputFolder = "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/FinalGeographyFields";
            public int expectedMaterials = 205;
        }
        [Serializable] public sealed class SurfaceRow
        {
            public string hierarchy, meshPath, meshKey, meshSha256;
            public Matrix4x4 localToWorld; public Bounds bounds; public string[] materialKeys;
        }
        [Serializable] public sealed class MaterialRow
        {
            public string path, key, beforeFileSha256, afterFileSha256, metaSha256, beforePayloadSha256, afterPayloadSha256, backup;
            public string beforeFieldPath, beforeFieldKey; public Vector4 beforeRect;
            public bool otherPropertiesUnchanged;
        }
        [Serializable] public sealed class FileProof { public string path, sha256; }
#pragma warning disable CS0649 // Read from the existing persistent-look receipt.
        [Serializable] sealed class LookSources { public FileProof[] sources; }
#pragma warning restore CS0649
        [Serializable] public sealed class Receipt
        {
            public int schemaVersion = 1;
            public string status, utc, generation, applicationReceiptPath, applicationReceiptSha256, sceneSha256;
            public string sourceFingerprint, bakerSha256, fieldPath, fieldFileSha256, fieldPixelSha256, receiptPath;
            public string error, navMeshDependencyBefore, navMeshDependencyAfter;
            public bool sceneUnchanged, protectedInputsUnchanged, allMaterialBindingsMatch, reused, originalSourcesPreserved;
            public bool otherMaterialPropertiesUnchanged, navigationDependenciesUnchanged;
            public Vector4 worldRect; public SurfaceRow[] surfaces; public MaterialRow[] materials;
            public FileProof[] preservedFieldAssets;
            public InkPaintingGeographyField.Receipt bake;
            public string[] rollbackErrors, notes;
        }
        sealed class MaterialState { public Material target; public Texture texture; public Vector4 rect; public MaterialRow row; }
        public static Receipt LastReceipt { get; private set; }
        static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Workspace => Path.GetFullPath(Path.Combine(Project, ".."));
        static string AssetFile(string path) => Path.GetFullPath(Path.Combine(Project, path));
        static string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Workspace, path));
        static string Hierarchy(Transform target) => target.parent == null ? target.name : Hierarchy(target.parent) + "/" + target.name;
        static T[] Components<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

        public static string Apply(Settings settings)
        {
            Guard();
            if (settings == null || settings.expectedMaterials <= 0) throw new ArgumentException("Explicit application receipt and expected material inventory required.");
            string applicationPath = Resolve(settings.geometryApplicationReceiptPath);
            // This verifies saved scene/source mesh bindings and exact surface fields.
            // Run before navigation changes that saved scene hash, as documented.
            InkPaintingMountainApplication.VerifyApplied(applicationPath);
            var application = JsonUtility.FromJson<InkPaintingMountainApplication.ApplicationReceipt>(File.ReadAllText(applicationPath));
            if (string.IsNullOrEmpty(application.generation) || application.generation.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
                throw new InvalidOperationException("Invalid geometry generation identifier.");
            var materials = AssetDatabase.FindAssets("t:Material", new[] { MaterialsFolder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m != null && m.HasProperty(FieldProperty)).OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
            if (materials.Length != settings.expectedMaterials || materials.Any(m => !m.HasProperty(RectProperty) || m.shader == null ||
                !m.shader.name.StartsWith("Oheangbu/Study/InkPainting", StringComparison.Ordinal) || !m.HasProperty("_PaintedGeoEnabled") || m.GetFloat("_PaintedGeoEnabled") < .5f))
                throw new InvalidOperationException("Owned geography-enabled study material inventory differs from the expected count/shaders.");
            foreach (var material in materials) Clean(material);
            var materialPaths = new HashSet<string>(materials.Select(m => AssetFile(AssetDatabase.GetAssetPath(m))), StringComparer.OrdinalIgnoreCase);
            if (application.sourceFiles.Any(row => materialPaths.Contains(Path.GetFullPath(row.path))))
                throw new InvalidOperationException("An application receipt protects a target material file. Do not invalidate its recorded inputs.");
            var sources = Components<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && (m.shader.name == "Oheangbu/Study/InkPaintingGround" || m.shader.name == "Oheangbu/Study/InkPaintingTerrain"))
                .Distinct().OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
            if (sources.Length == 0 || sources.Any(m => !materials.Contains(m))) throw new InvalidOperationException("Current visible terrain is not using the owned painting material set.");
            var surfaces = SnapshotSurfaces(sources);
            string bakerHash = FileHash(AssetFile(BakerSource));
            string fingerprint = TextHash(application.generation + "\n" + bakerHash + "\n512x768\n" + JsonUtility.ToJson(new SurfaceRows { rows = surfaces }));
            string folder = InkPaintingMountainForm.ExportNormalizeAssetFolder(settings.assetFolder) + "/" + application.generation;
            string output = Path.Combine(InkPaintingMountainForm.ExportResolveOutput(settings.outputFolder, Workspace, Application.dataPath), application.generation);
            string receiptPath = Path.Combine(output, "geography_field_application.json");
            string fieldPath = folder + "/Geography.asset";
            if (File.Exists(receiptPath))
            {
                var previous = JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath));
                if (previous == null || previous.status != "PASS_PERSISTENT_FINAL_GEOGRAPHY" || previous.generation != application.generation ||
                    previous.sourceFingerprint != fingerprint || previous.applicationReceiptSha256 != FileHash(applicationPath) || previous.fieldPath != fieldPath)
                    throw new InvalidOperationException("This geometry generation has a different or incomplete field receipt. Review it; no automatic rebake/overwrite.");
                VerifyExisting(previous, materials, application); previous.reused = true; LastReceipt = previous;
                return JsonUtility.ToJson(previous, true);
            }
            if (AssetDatabase.LoadMainAssetAtPath(fieldPath) != null || File.Exists(AssetFile(fieldPath)) || Directory.Exists(output))
                throw new InvalidOperationException("Generation path already exists without a completed receipt; inspect quarantine before retrying.");
            var r = new Receipt {
                utc = DateTime.UtcNow.ToString("O"), status = "PREPARING_FINAL_GEOGRAPHY", generation = application.generation,
                applicationReceiptPath = applicationPath, applicationReceiptSha256 = FileHash(applicationPath),
                sceneSha256 = FileHash(AssetFile(ScenePath)), sourceFingerprint = fingerprint, bakerSha256 = bakerHash,
                fieldPath = fieldPath, receiptPath = receiptPath, surfaces = surfaces,
                navMeshDependencyBefore = MeshDependencySignature(),
                notes = new[] {
                    "Same 512x768 compact-domain Bake implementation and current visible ground/terrain surface selection as form preview.",
                    "Only _PaintedGeoField and _PaintedGeoRect change on the existing owned study materials. Shader, keywords, pass state, queue, instancing and every other serialized property are compared.",
                    "No scene/SO/mesh binding changes or scene save. Original source material assets and prior geography field are preserved.",
                    "Geometry receipt hashes SO material identities, not properties of derived material assets. Navigation inputs concern mesh payload/dependencies; their dependency signature is verified unchanged here.",
                    "Apply this after saved geometry and before navigation scene save. A later different scene SHA needs its receipt chain reviewed; this helper does not waive the saved application guard."
                }
            };
            LastReceipt = r; Directory.CreateDirectory(output);
            var states = materials.Select((material, index) => {
                string path = AssetDatabase.GetAssetPath(material); var old = material.GetTexture(FieldProperty);
                var row = new MaterialRow { path = path, key = AssetKey(material), beforeFileSha256 = FileHash(AssetFile(path)),
                    metaSha256 = FileHash(AssetFile(path + ".meta")), beforePayloadSha256 = PayloadHash(material),
                    beforeFieldPath = old == null ? null : AssetDatabase.GetAssetPath(old), beforeFieldKey = AssetKey(old),
                    beforeRect = material.GetVector(RectProperty), backup = Path.Combine(output, "material_" + index.ToString("D3") + ".mat.backup") };
                File.Copy(AssetFile(path), row.backup, false);
                return new MaterialState { target = material, texture = old, rect = row.beforeRect, row = row };
            }).ToArray();
            r.materials = states.Select(state => state.row).ToArray(); Write(r);
            r.preservedFieldAssets = states.Where(s => s.texture != null).Select(s => AssetDatabase.GetAssetPath(s.texture)).Distinct()
                .SelectMany(path => new[] { AssetFile(path), AssetFile(path + ".meta") }).Select(path => new FileProof { path = path, sha256 = FileHash(path) }).ToArray();
            VerifySceneAndInputs(r, application); Write(r);
            var touched = new List<MaterialState>(); Texture2D field = null;
            try
            {
                CommitGuard();
                // Transient Bake avoids its non-null-output Refresh/overwrite path.
                field = InkPaintingGeographyField.Bake(sources, null, out var rectangle);
                r.worldRect = rectangle; r.bake = InkPaintingGeographyField.LastReceipt;
                if (r.bake == null || r.bake.matchedRenderers != surfaces.Length || r.bake.triangles == 0)
                    throw new InvalidOperationException("Baked source inventory differs from the captured surface proof.");
                if (SurfaceFingerprint(application.generation, bakerHash, sources) != fingerprint)
                    throw new InvalidOperationException("Current geometry changed during the geography bake.");
                r.fieldPixelSha256 = PixelHash(field);
                CommitGuard();
                EnsureFolder(folder); field.hideFlags = HideFlags.None; field.name = "FinalGeography_" + application.generation;
                AssetDatabase.CreateAsset(field, fieldPath);
                AssetDatabase.SaveAssetIfDirty(field); Clean(field);
                r.fieldFileSha256 = FileHash(AssetFile(fieldPath));
                foreach (var state in states)
                {
                    touched.Add(state);
                    state.target.SetTexture(FieldProperty, field); state.target.SetVector(RectProperty, rectangle);
                    state.row.otherPropertiesUnchanged = NormalizedPayloadHash(state.target, state.texture, state.rect) == state.row.beforePayloadSha256;
                    if (!state.row.otherPropertiesUnchanged) throw new InvalidOperationException("Another material property changed: " + state.row.path);
                    EditorUtility.SetDirty(state.target); AssetDatabase.SaveAssetIfDirty(state.target);
                    state.row.otherPropertiesUnchanged &= NormalizedPayloadHash(state.target, state.texture, state.rect) == state.row.beforePayloadSha256;
                    if (!state.row.otherPropertiesUnchanged) throw new InvalidOperationException("Another material property changed while saving: " + state.row.path);
                    state.row.afterFileSha256 = FileHash(AssetFile(state.row.path)); state.row.afterPayloadSha256 = PayloadHash(state.target);
                    CheckFile(AssetFile(state.row.path + ".meta"), state.row.metaSha256);
                }
                r.navMeshDependencyAfter = MeshDependencySignature();
                r.navigationDependenciesUnchanged = r.navMeshDependencyAfter == r.navMeshDependencyBefore;
                if (!r.navigationDependenciesUnchanged) throw new InvalidOperationException("A material edit changed a scene mesh's dependency hash; navigation input proof would be invalidated.");
                VerifySceneAndInputs(r, application);
                if (SurfaceFingerprint(application.generation, bakerHash, sources) != fingerprint) throw new InvalidOperationException("Final mesh fingerprint differs from the baked source.");
                r.allMaterialBindingsMatch = states.All(s => s.target.GetTexture(FieldProperty) == field && s.target.GetVector(RectProperty).Equals(rectangle));
                r.otherMaterialPropertiesUnchanged = states.All(s => s.row.otherPropertiesUnchanged);
                r.originalSourcesPreserved = r.protectedInputsUnchanged;
                if (!r.allMaterialBindingsMatch) throw new InvalidOperationException("A final material field/rect binding is missing.");
                r.status = "PASS_PERSISTENT_FINAL_GEOGRAPHY"; Write(r);
                return JsonUtility.ToJson(r, true);
            }
            catch (Exception error)
            {
                r.status = "FAILED_QUARANTINED"; r.error = error.ToString(); var errors = new List<string>();
                foreach (var state in touched)
                    try {
                        state.target.SetTexture(FieldProperty, state.texture); state.target.SetVector(RectProperty, state.rect);
                        if (PayloadHash(state.target) != state.row.beforePayloadSha256) throw new InvalidOperationException("Other material properties changed; inspect backup: " + state.row.backup);
                        EditorUtility.SetDirty(state.target); AssetDatabase.SaveAssetIfDirty(state.target);
                    } catch (Exception rollback) { errors.Add(state.row.path + ": " + rollback.Message); }
                r.rollbackErrors = errors.ToArray(); Write(r); throw;
            }
            finally { if (field != null && !EditorUtility.IsPersistent(field)) Object.DestroyImmediate(field); }
        }

        static void VerifyExisting(Receipt r, Material[] materials, InkPaintingMountainApplication.ApplicationReceipt application)
        {
            CheckFile(AssetFile(r.fieldPath), r.fieldFileSha256);
            var field = AssetDatabase.LoadAssetAtPath<Texture2D>(r.fieldPath); Clean(field);
            if (PixelHash(field) != r.fieldPixelSha256 || materials.Length != r.materials.Length) throw new InvalidOperationException("Existing field pixels or material inventory changed.");
            foreach (var row in r.materials)
            {
                var material = materials.Single(m => AssetDatabase.GetAssetPath(m) == row.path);
                CheckFile(AssetFile(row.path), row.afterFileSha256); CheckFile(AssetFile(row.path + ".meta"), row.metaSha256);
                if (PayloadHash(material) != row.afterPayloadSha256 || material.GetTexture(FieldProperty) != field || !material.GetVector(RectProperty).Equals(r.worldRect))
                    throw new InvalidOperationException("Existing field/material proof differs; no implicit rewrite: " + row.path);
            }
            if (MeshDependencySignature() != r.navMeshDependencyAfter) throw new InvalidOperationException("Current mesh dependency signature changed.");
            VerifySceneAndInputs(r, application);
        }
        static void VerifySceneAndInputs(Receipt r, InkPaintingMountainApplication.ApplicationReceipt application)
        {
            CheckFile(r.applicationReceiptPath, r.applicationReceiptSha256); CheckFile(AssetFile(ScenePath), r.sceneSha256);
            CheckFile(application.exportReceiptPath, application.exportReceiptSha256); CheckFile(application.physicsReceiptPath, application.physicsReceiptSha256); CheckFile(application.normalReceiptPath, application.normalReceiptSha256);
            foreach (var source in application.sourceFiles) CheckFile(source.path, source.sha256);
            foreach (var source in r.preservedFieldAssets ?? Array.Empty<FileProof>()) CheckFile(source.path, source.sha256);
            CheckFile(AssetFile(BakerSource), r.bakerSha256);
            if (!string.IsNullOrEmpty(application.lookApplicationReceiptPath))
            {
                CheckFile(application.lookApplicationReceiptPath, application.lookReceiptSha256);
                var look = JsonUtility.FromJson<LookSources>(File.ReadAllText(application.lookApplicationReceiptPath));
                if (look?.sources == null) throw new InvalidOperationException("Persistent-look original source proof is missing.");
                foreach (var source in look.sources) CheckFile(AssetFile(source.path), source.sha256);
            }
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Scene became dirty during an asset-only operation.");
            r.sceneUnchanged = r.protectedInputsUnchanged = true;
        }
        [Serializable] sealed class SurfaceRows { public SurfaceRow[] rows; }
        static string SurfaceFingerprint(string generation, string bakerHash, Material[] sources) =>
            TextHash(generation + "\n" + bakerHash + "\n512x768\n" + JsonUtility.ToJson(new SurfaceRows { rows = SnapshotSurfaces(sources) }));
        static SurfaceRow[] SnapshotSurfaces(Material[] sources)
        {
            var selected = new HashSet<Material>(sources); var hashes = new Dictionary<Mesh, string>();
            return Components<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterials.Any(selected.Contains))
                .OrderBy(r => Hierarchy(r.transform), StringComparer.Ordinal).Select(renderer => {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh; Clean(mesh);
                    if (!hashes.TryGetValue(mesh, out var hash)) { hash = InkPaintingMountainForm.ExportedMeshFingerprint(mesh); hashes.Add(mesh, hash); }
                    return new SurfaceRow { hierarchy = Hierarchy(renderer.transform), meshPath = AssetDatabase.GetAssetPath(mesh), meshKey = AssetKey(mesh), meshSha256 = hash,
                        localToWorld = renderer.localToWorldMatrix, bounds = renderer.bounds, materialKeys = renderer.sharedMaterials.Select(AssetKey).ToArray() };
                }).ToArray();
        }
        static string MeshDependencySignature() => TextHash(string.Join("\n", Components<Component>().Select(component =>
            component is MeshFilter filter ? filter.sharedMesh : component is MeshCollider collider ? collider.sharedMesh : component is SkinnedMeshRenderer skin ? skin.sharedMesh : null)
            .Where(mesh => mesh != null).Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => path + ":" + AssetDatabase.GetAssetDependencyHash(path))));
        static string NormalizedPayloadHash(Material material, Texture original, Vector4 originalRect)
        {
            var current = material.GetTexture(FieldProperty); var rect = material.GetVector(RectProperty);
            try { material.SetTexture(FieldProperty, original); material.SetVector(RectProperty, originalRect); return PayloadHash(material); }
            finally { material.SetTexture(FieldProperty, current); material.SetVector(RectProperty, rect); }
        }
        static string PayloadHash(Material material)
        {
            string text = Regex.Replace(EditorJsonUtility.ToJson(material), "(?<!\\\\)\"instanceID\"\\s*:\\s*(-?[0-9]+)", match => {
                int id = int.Parse(match.Groups[1].Value);
#pragma warning disable CS0618 // Editor JSON still serializes instanceID integers.
                var reference = id == 0 ? null : EditorUtility.InstanceIDToObject(id);
#pragma warning restore CS0618
                if (id != 0 && reference == null) throw new InvalidOperationException("Unresolved material reference.");
                return "\"asset\":\"" + AssetKey(reference) + "\"";
            });
            return TextHash(text);
        }
        static string PixelHash(Texture2D texture)
        {
            var bytes = texture.GetRawTextureData<byte>().ToArray();
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }
        static string AssetKey(Object value)
        {
            if (value == null) return "null";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id)) throw new InvalidOperationException("Persistent asset identity missing: " + value.name);
            return guid + ":" + id;
        }
        static void Clean(Object asset) { if (asset == null || !EditorUtility.IsPersistent(asset) || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Clean persistent asset required: " + (asset == null ? "null" : asset.name)); }
        static void Guard()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Clean stable saved compact Edit scene required.");
            if (InkPaintingMountainForm.IsActive || InkPaintingMountainApplication.IsPreviewActive || InkPaintingFoliagePreview.IsActive) throw new InvalidOperationException("Restore all geometry/foliage previews first.");
            foreach (string name in new[] { "previewRenderers", "formOriginalFields", "temporarySky" })
                if (typeof(CompactInkPaintingStudy).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) != null) throw new InvalidOperationException("Restore the painting preview first.");
            CommitGuard();
        }
        static void CommitGuard() { if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; field bake stopped."); }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return; int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
            if (AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1))) != path) throw new IOException("Cannot create owned field generation folder.");
        }
        static string FileHash(string path) { using (var input = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(input)); }
        static string TextHash(string text) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text))); }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        static void CheckFile(string path, string expected) { if (FileHash(path) != expected) throw new InvalidOperationException("Protected file changed: " + path); }
        static void Write(Receipt r)
        {
            string temp = r.receiptPath + ".tmp"; File.WriteAllText(temp, JsonUtility.ToJson(r, true), new UTF8Encoding(false));
            if (File.Exists(r.receiptPath)) File.Replace(temp, r.receiptPath, null); else File.Move(temp, r.receiptPath);
        }
    }
}
