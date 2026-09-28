using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Oheangbu.App.World.Dressing;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Installs exported geometry on current compact coordinates. Never re-compresses or regenerates placements.</summary>
    public static partial class InkPaintingMountainApplication
    {
        const string CompactScene = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        [Serializable] public sealed class Settings
        {
            public string exportReceiptPath, lookApplicationReceiptPath, physicsReceiptPath, normalReceiptPath;
            public string assetFolder = "Assets/_Project/Art/World/WorldCompact/MountainApplications";
            public string outputFolder = "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/GeometryApplication";
        }
        [Serializable] public sealed class AssetMapping
        { public string source, derived, sourceJsonSha256, derivedJsonSha256; public long sourceLocalId; }
        [Serializable] public sealed class ReferenceRow
        { public string hierarchy, component, objectId, property, source, derived; public long sourceLocalId; }
        [Serializable] public sealed class FileProof { public string path, sha256; }
        [Serializable] public sealed class FoliageRow
        { public string hierarchy, beforeSha256, afterSha256; public int packets, matrices, placements; public bool sharedIndexDelta, countAndNonYExact; }
        [Serializable] public sealed class ApplicationReceipt
        {
            // Compatible with InkPaintingNavigationUpdate.GeometryApplicationReceipt.
            public int schemaVersion = 1;
            public string status, generation, scene, sceneBeforeSha256, sceneAfterSha256;
            public string exportReceiptPath, exportReceiptSha256, physicsReceiptPath, physicsReceiptSha256, normalReceiptPath, normalReceiptSha256;
            public bool sceneSaved, sourceFilesUnchanged, protectedGeometryPreserved, exactSurfaceBindingsVerified;
            public string utc, applicationId, reviewStatus, lookApplicationReceiptPath, lookReceiptSha256, receiptPath, sceneBackup, assetFolder, error;
            public string deltaPath, absolutePatchPath;
            public bool sceneInitiallyClean, sourceSceneChainVerified, selectionDataUnchanged, transformsPreserved, rendererMaterialsPreserved;
            public bool previewActive, applied, navigationRebaked, temporaryPlayGeometryVerified, mapIllustrationAligned;
            public AssetMapping[] assets; public ReferenceRow[] references; public FoliageRow[] earlyFoliage;
            public FileProof[] sourceFiles; public string[] unchangedMapArtwork, createdAssets, notes, rollbackErrors;
        }
        // These private DTOs are populated by JsonUtility from saved evidence.
#pragma warning disable CS0649
        [Serializable] sealed class LookReceipt
        {
            public string status, scene, sceneBeforeSha256, sceneAfterSha256;
            public bool sourcesUnchanged, populationUnchanged, supplementaryPlacementUnchanged, structureUnchanged, sceneSaved;
            public LookStamp[] sources;
        }
        [Serializable] sealed class LookStamp { public string path, sha256; }
        [Serializable] sealed class AuditGate
        {
            public string utc, scene, physicalAuditUtc, preparationUtc;
            public bool completed, preservationPass, normalPreservationResolved, colliderBindingsRestored;
        }
#pragma warning restore CS0649
        sealed class MeshBinding { public MeshFilter target; public MeshCollider collider; public Mesh before, after; public InkPaintingMountainForm.ExportMeshRow row; }
        sealed class ReferenceBinding { public Component target; public string path; public ScriptableObject before, after; public Accessor access; }
        sealed class EarlyBinding { public EarlyRegionFoliage target; public EarlyRegionFoliage.Packet[] before, after; public FoliageRow row; }
        sealed class PlantBinding { public Transform target; public Vector3 before, after; }
        sealed class TransformProof { public Transform target, parent; public Vector3 localPosition, localScale; public Quaternion localRotation; }
        sealed class State
        {
            public Scene scene; public Settings settings; public ApplicationReceipt receipt;
            public InkPaintingMountainForm.MountainExportReceipt export;
            public WorldMacroSurfaceDeformationSO delta; public WorldMacroFinalSurfaceSO absolute;
            public readonly List<MeshBinding> meshes = new List<MeshBinding>();
            public readonly List<ReferenceBinding> references = new List<ReferenceBinding>();
            public readonly List<EarlyBinding> early = new List<EarlyBinding>();
            public readonly List<PlantBinding> plants = new List<PlantBinding>();
            public readonly Dictionary<ScriptableObject, ScriptableObject> replacements = new Dictionary<ScriptableObject, ScriptableObject>();
            public readonly Dictionary<ScriptableObject, string> sourceJson = new Dictionary<ScriptableObject, string>();
            public readonly List<TransformProof> transforms = new List<TransformProof>();
            public readonly List<string> created = new List<string>();
            public string materialsHash; public bool bound; public double expires;
        }
        static State current;
        public static ApplicationReceipt LastReceipt { get; private set; }
        public static bool IsPreviewActive => current != null && current.bound && !current.receipt.applied;
        static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Workspace => Path.GetFullPath(Path.Combine(Project, ".."));
        static string AbsoluteAsset(string path) => Path.GetFullPath(Path.Combine(Project, path));
        static string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Workspace, path));
        static string Hierarchy(Transform transform) => transform.parent == null ? transform.name : Hierarchy(transform.parent) + "/" + transform.name;
        static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).Where(c => c != null).ToArray();

        /// <summary>Validate receipts/current baseline; create only owned derivative SO assets. Scene remains unbound and clean.</summary>
        public static string Prepare(Settings settings)
        {
            Guard(true);
            if (IsPreviewActive) throw new InvalidOperationException("Restore this geometry preview before preparing another application.");
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var s = new State { scene = SceneManager.GetActiveScene(), settings = JsonUtility.FromJson<Settings>(JsonUtility.ToJson(settings)) };
            var exportPath = Resolve(settings.exportReceiptPath);
            s.export = JsonUtility.FromJson<InkPaintingMountainForm.MountainExportReceipt>(File.ReadAllText(exportPath));
            if (s.export == null || s.export.status != "complete_assets_only_not_installed" || s.export.scene != CompactScene ||
                !s.export.sourceFilesUnchanged || !s.export.sourceMeshesUnchanged || !s.export.preparedMeshesUnchanged || !s.export.sourceBindingsUnchanged)
                throw new InvalidOperationException("A completed source-preserving mountain export is required.");
            var prep = JsonUtility.FromJson<InkPaintingMountainForm.Receipt>(s.export.preparationReceiptJson);
            if (prep == null || !prep.protectedFacesExact || !prep.protectedVerticesExact || !prep.sourceXZAndTopologyPreserved || !prep.foliageSupportWithinTolerance)
                throw new InvalidOperationException("Export preparation protection/support proof did not pass.");
            string id = s.export.generation + "_Application_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string output = Path.Combine(InkPaintingMountainForm.ExportResolveOutput(settings.outputFolder, Workspace, Application.dataPath), id);
            var r = new ApplicationReceipt {
                utc = DateTime.UtcNow.ToString("O"), status = "PREPARING_GEOMETRY_ASSETS", applicationId = id,
                generation = s.export.generation, scene = CompactScene, sceneInitiallyClean = true,
                sceneBeforeSha256 = FileHash(AbsoluteAsset(CompactScene)), exportReceiptPath = exportPath, exportReceiptSha256 = FileHash(exportPath),
                physicsReceiptPath = Resolve(settings.physicsReceiptPath), normalReceiptPath = Resolve(settings.normalReceiptPath),
                assetFolder = InkPaintingMountainForm.ExportNormalizeAssetFolder(settings.assetFolder) + "/" + id,
                receiptPath = Path.Combine(output, "application.json"), sceneBackup = Path.Combine(output, "before_geometry.unity"),
                deltaPath = s.export.surface.deltaPath, absolutePatchPath = s.export.surface.absolutePatchPath,
                reviewStatus = "GEOMETRY_REVIEW_NAVIGATION_PENDING",
                notes = new[] {
                    "Geometry installation uses current compact world coordinates directly. No compression/remapping, terrain regeneration, selection regeneration or content relocation.",
                    "Navigation is not rebaked by this helper. A saved application remains under review until navigation and final Play validation complete.",
                    "Existing map illustrations, region tiles, interior artwork, markers, lines and zones are preserved. Illustrated ridge alignment remains under review.",
                    "Temporary geometry preview is Edit-only. Play preview has not been verified; final Play validation follows saved geometry/navigation application."
                }
            };
            s.receipt = r; LastReceipt = r;
            if (Directory.Exists(output) || Directory.Exists(AbsoluteAsset(r.assetFolder)) || AssetDatabase.IsValidFolder(r.assetFolder))
                throw new InvalidOperationException("The unique geometry application generation already exists.");
            ValidateSceneChain(s);
            string physicalJson = File.ReadAllText(r.physicsReceiptPath), normalJson = File.ReadAllText(r.normalReceiptPath);
            var physics = JsonUtility.FromJson<AuditGate>(physicalJson);
            var normals = JsonUtility.FromJson<AuditGate>(normalJson);
            if (physics == null || normals == null || physics.scene != CompactScene || normals.scene != CompactScene ||
                !physics.completed || !physics.preservationPass || !physics.colliderBindingsRestored ||
                !normals.completed || !normals.normalPreservationResolved || !normals.colliderBindingsRestored ||
                normals.physicalAuditUtc != physics.utc ||
                !InkPaintingMountainForm.AuditProvenanceMatchesExport(exportPath, s.export, physicalJson, normalJson))
                throw new InvalidOperationException("Matching completed physical/normal preservation receipts are required.");
            r.physicsReceiptSha256 = FileHash(r.physicsReceiptPath); r.normalReceiptSha256 = FileHash(r.normalReceiptPath);
            s.delta = LoadClean<WorldMacroSurfaceDeformationSO>(r.deltaPath);
            s.absolute = LoadClean<WorldMacroFinalSurfaceSO>(r.absolutePatchPath);
            if (InkPaintingMountainForm.ExportSurfaceHash(s.delta.TriangleSurface) != s.export.surface.exportedDeltaSha256 ||
                InkPaintingMountainForm.ExportSurfaceHash(s.absolute.AbsoluteHeightTriangles) != s.export.surface.absolutePatchSha256)
                throw new InvalidOperationException("Exported surface payload changed.");
            var filters = Components<MeshFilter>(s.scene);
            foreach (var row in s.export.meshes)
            {
                var filter = filters.Single(f => Hierarchy(f.transform) == row.hierarchy);
                var original = LoadSourceMesh(row);
                var derived = row.changed ? LoadClean<Mesh>(row.derivedPath) : original;
                if (filter.sharedMesh != original || filter.transform.localToWorldMatrix != row.localToWorld ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(original) != row.sourceMeshBeforeSha256 ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(derived) != (row.changed ? row.exportedMeshSha256 : row.sourceMeshBeforeSha256))
                    throw new InvalidOperationException("Original/derived mesh or current transform differs from the export: " + row.hierarchy);
                var collider = filter.GetComponent<MeshCollider>();
                if (row.collider && (collider == null || collider.sharedMesh != original)) throw new InvalidOperationException("Source collider mismatch: " + row.hierarchy);
                s.meshes.Add(new MeshBinding { target = filter, collider = row.collider ? collider : null, before = original, after = derived, row = row });
            }
            foreach (var row in s.export.staticRoots.Where(row => row.hasPreparedBinding))
            {
                var target = Components<Transform>(s.scene).Single(t => Hierarchy(t) == row.hierarchy);
                if (!target.position.Equals(row.originalWorld) || !target.localPosition.Equals(row.originalLocal))
                    throw new InvalidOperationException("Static vegetation baseline moved: " + row.hierarchy);
                s.plants.Add(new PlantBinding { target = target, before = target.localPosition, after = row.plannedLocal });
            }
            foreach (var t in Components<Transform>(s.scene))
                s.transforms.Add(new TransformProof { target = t, parent = t.parent, localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale });
            s.materialsHash = MaterialSignature(s.scene);

            var allComponents = Components<Component>(s.scene);
            var graph = DiscoverGraph(allComponents);
            var geography = LoadClean<WorldMacroSheetSO>(prep.settings.Geography);
            if (geography.FinalSurface != null) throw new InvalidOperationException("A final terrain surface is already installed; do not apply twice.");
            var sheets = Components<WorldMacroDressingRenderer>(s.scene).Select(d => d.Sheet).Distinct().ToArray();
            if (sheets.Length == 0 || sheets.Any(sheet => sheet == null || sheet.Geography != geography || sheet.SurfaceDeformation != null))
                throw new InvalidOperationException("Current dressing must reference the unchanged compact geography and have no prior deformation.");
            var needed = new HashSet<ScriptableObject>(sheets.Cast<ScriptableObject>()) { geography };
            bool expanded;
            do {
                expanded = false;
                foreach (var pair in graph)
                    if (!needed.Contains(pair.Key) && pair.Value.Any(v => needed.Contains(v.value))) { needed.Add(pair.Key); expanded = true; }
            } while (expanded);
            if (needed.Any(asset => !graph.ContainsKey(asset))) throw new InvalidOperationException("Expected geography/dressing is not reachable from the scene's serialized SO graph.");
            foreach (var asset in needed) { Clean(asset); s.sourceJson.Add(asset, JsonHash(asset)); }
            r.unchangedMapArtwork = graph.Keys.OfType<WorldMapBakedDataSO>().Select(AssetDatabase.GetAssetPath).Distinct().ToArray();
            r.sourceFiles = CaptureSources(s, graph.Keys);
            Directory.CreateDirectory(output);
            File.Copy(AbsoluteAsset(CompactScene), r.sceneBackup, false);
            if (File.Exists(AbsoluteAsset(CompactScene + ".meta"))) File.Copy(AbsoluteAsset(CompactScene + ".meta"), r.sceneBackup + ".meta", false);
            Write(r);
            try
            {
                EnsureFolder(r.assetFolder);
                foreach (var source in needed.OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal))
                {
                    CommitGuard(); var clone = Object.Instantiate(source); clone.name = source.name; clone.hideFlags = HideFlags.None;
                    s.replacements.Add(source, clone);
                }
                foreach (var pair in s.replacements)
                {
                    foreach (var reference in graph[pair.Key])
                        if (s.replacements.TryGetValue(reference.value, out var mapped))
                        {
                            var sourceAccess = new Accessor(pair.Key, reference.path); var cloneAccess = new Accessor(pair.Value, reference.path);
                            if (ReferenceEquals(sourceAccess.Storage, cloneAccess.Storage)) throw new InvalidOperationException("A cloned reference container aliases its source: " + reference.path);
                            cloneAccess.Set(mapped);
                        }
                    if (pair.Value is WorldMacroSheetSO geo) geo.FinalSurface = s.absolute;
                    if (pair.Value is WorldMacroDressingSheetSO dressing) dressing.SurfaceDeformation = s.delta;
                    VerifyCloneOnlyAllowed(s, pair.Key, pair.Value, graph[pair.Key]);
                }
                var mappings = new List<AssetMapping>(); int assetIndex = 0;
                foreach (var pair in s.replacements)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pair.Key, out string guid, out long localId);
                    string path = r.assetFolder + "/" + (assetIndex++).ToString("D3") + "_" + pair.Key.GetType().Name + "_" + guid.Substring(0, 8) + ".asset";
                    AssetDatabase.CreateAsset(pair.Value, path); s.created.Add(path);
                    mappings.Add(new AssetMapping { source = AssetDatabase.GetAssetPath(pair.Key), sourceLocalId = localId, derived = path,
                        sourceJsonSha256 = s.sourceJson[pair.Key] });
                }
                // References between two new assets were assigned before both had
                // persistent IDs. Save ONLY these owned new assets after all IDs exist.
                foreach (var clone in s.replacements.Values) { EditorUtility.SetDirty(clone); AssetDatabase.SaveAssetIfDirty(clone); }
                r.assets = mappings.ToArray();
                foreach (var row in r.assets) row.derivedJsonSha256 = JsonHash(LoadClean<ScriptableObject>(row.derived));
                foreach (var component in allComponents) foreach (var reference in References(component))
                    if (s.replacements.TryGetValue(reference.value, out var mapped))
                        s.references.Add(new ReferenceBinding { target = component, path = reference.path, before = reference.value, after = mapped, access = new Accessor(component, reference.path) });
                r.references = s.references.Select(b => {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(b.before, out string _, out long localId);
                    return new ReferenceRow { hierarchy = Hierarchy(b.target.transform), component = b.target.GetType().FullName,
                        objectId = GlobalObjectId.GetGlobalObjectIdSlow(b.target).ToString(), property = b.path,
                        source = AssetDatabase.GetAssetPath(b.before), sourceLocalId = localId, derived = AssetDatabase.GetAssetPath(b.after) };
                }).ToArray();
                foreach (var early in Components<EarlyRegionFoliage>(s.scene))
                {
                    var row = new FoliageRow { hierarchy = Hierarchy(early.transform), beforeSha256 = PacketSignature(early.Packets), sharedIndexDelta = true };
                    var clone = ClonePackets(early, s.delta, row);
                    row.afterSha256 = PacketSignature(clone); row.countAndNonYExact = true;
                    s.early.Add(new EarlyBinding { target = early, before = early.Packets, after = clone, row = row });
                }
                r.earlyFoliage = s.early.Select(b => b.row).ToArray(); r.createdAssets = s.created.ToArray();
                VerifyState(s, false); r.status = "PREPARED_GEOMETRY_ASSETS_UNBOUND"; Write(r);
                current = s;
                return JsonUtility.ToJson(r, true);
            }
            catch (Exception error)
            {
                r.status = "FAILED_PREPARATION_QUARANTINED"; r.error = error.ToString(); r.createdAssets = s.created.ToArray(); Write(r);
                foreach (var clone in s.replacements.Values) if (clone != null && !EditorUtility.IsPersistent(clone)) Object.DestroyImmediate(clone);
                throw;
            }
        }

        public static string BeginPreview()
        {
            Guard(true); var s = current ?? throw new InvalidOperationException("Prepare geometry application first.");
            if (s.bound || s.receipt.applied) throw new InvalidOperationException("Application is already bound.");
            ValidateInputs(s); VerifyState(s, false);
            try
            {
                Bind(s, true); s.bound = true; VerifyState(s, true);
                s.receipt.previewActive = true; s.receipt.status = "EDIT_PREVIEW_NAVIGATION_PENDING";
                s.expires = EditorApplication.timeSinceStartup + 180;
                EditorApplication.update += PreviewTick; AssemblyReloadEvents.beforeAssemblyReload += RestoreBeforeReload;
                Write(s.receipt); return JsonUtility.ToJson(s.receipt, true);
            }
            catch { RestoreBindings(s); throw; }
        }
        public static string EndPreview()
        {
            if (!IsPreviewActive) return LastReceipt == null ? "{}" : JsonUtility.ToJson(LastReceipt, true);
            var s = current; RestoreBindings(s); VerifyState(s, false);
            s.receipt.previewActive = false; s.receipt.status = "EDIT_PREVIEW_RESTORED"; Write(s.receipt);
            return JsonUtility.ToJson(s.receipt, true);
        }

        /// <summary>Explicit persistence step; requires the still-clean original scene and a fully restored preview.</summary>
        public static string ApplyAndSave()
        {
            Guard(true); var s = current ?? throw new InvalidOperationException("Prepare geometry application first.");
            if (s.bound || s.receipt.applied) throw new InvalidOperationException("Restore preview first; a saved generation cannot be applied twice.");
            ValidateInputs(s); VerifyState(s, false); s.receipt.status = "APPLYING_GEOMETRY"; Write(s.receipt);
            bool saveAttempted = false;
            try
            {
                Bind(s, true); s.bound = true; VerifyState(s, true);
                foreach (var target in s.meshes.Select(b => (Object)b.target).Concat(s.meshes.Where(b => b.collider != null).Select(b => (Object)b.collider))
                    .Concat(s.references.Select(b => (Object)b.target)).Concat(s.early.Select(b => (Object)b.target)).Concat(s.plants.Select(b => (Object)b.target)).Distinct()) Dirty(target);
                EditorSceneManager.MarkSceneDirty(s.scene); saveAttempted = true;
                var waterClocks = Components<Oheangbu.App.World.WorldMacroWaterClock>(s.scene);
                try
                {
                    foreach (var clock in waterClocks) clock.RestoreAuthoredMaterial();
                    if (!EditorSceneManager.SaveScene(s.scene)) throw new IOException("Compact geometry scene save failed.");
                }
                finally { foreach (var clock in waterClocks) clock.Rebind(); }
                VerifyState(s, true);
                if (s.scene.isDirty) throw new InvalidOperationException("Saved geometry scene remains dirty.");
                s.receipt.sceneAfterSha256 = FileHash(AbsoluteAsset(CompactScene)); s.receipt.sceneSaved = true;
                s.receipt.applied = true; s.receipt.previewActive = false;
                s.receipt.status = "APPLIED_GEOMETRY_SAVED"; Write(s.receipt);
                return JsonUtility.ToJson(s.receipt, true);
            }
            catch (Exception error)
            {
                s.receipt.status = "FAILED_APPLICATION_REVIEW_REQUIRED"; s.receipt.error = error.ToString();
                s.receipt.applied = false;
                if (saveAttempted) s.receipt.error += "\nScene save was attempted. Inspect the saved file and before_geometry.unity backup before recovery; this helper does not silently overwrite either.";
                try { RestoreBindings(s); } catch (Exception rollback) { s.receipt.rollbackErrors = new[] { rollback.ToString() }; }
                Write(s.receipt); throw;
            }
        }

        /// <summary>Read-only confirmation after saving, also usable after an Editor domain reload.</summary>
        public static string VerifyApplied(string applicationReceiptPath)
        {
            Guard(true); var r = JsonUtility.FromJson<ApplicationReceipt>(File.ReadAllText(Resolve(applicationReceiptPath)));
            if (r == null || r.status != "APPLIED_GEOMETRY_SAVED" || !r.applied || !r.sceneSaved || r.scene != CompactScene ||
                r.sceneAfterSha256 != FileHash(AbsoluteAsset(CompactScene))) throw new InvalidOperationException("Saved geometry application receipt does not match this scene.");
            CheckFile(r.exportReceiptPath, r.exportReceiptSha256); CheckFile(r.physicsReceiptPath, r.physicsReceiptSha256); CheckFile(r.normalReceiptPath, r.normalReceiptSha256);
            foreach (var source in r.sourceFiles) CheckFile(source.path, source.sha256);
            var export = JsonUtility.FromJson<InkPaintingMountainForm.MountainExportReceipt>(File.ReadAllText(r.exportReceiptPath));
            var filters = Components<MeshFilter>(SceneManager.GetActiveScene());
            foreach (var row in export.meshes)
            {
                var filter = filters.Single(f => Hierarchy(f.transform) == row.hierarchy);
                if (filter.transform.localToWorldMatrix != row.localToWorld || AssetDatabase.GetAssetPath(filter.sharedMesh) != (row.changed ? row.derivedPath : row.sourcePath) ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(filter.sharedMesh) != (row.changed ? row.exportedMeshSha256 : row.sourceMeshBeforeSha256) ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(LoadSourceMesh(row)) != row.sourceMeshBeforeSha256 ||
                    (row.collider && filter.GetComponent<MeshCollider>()?.sharedMesh != filter.sharedMesh))
                    throw new InvalidOperationException("Saved geometry/source mismatch: " + row.hierarchy);
            }
            foreach (var row in r.references)
            {
                var component = ResolveComponent(row.objectId, row.hierarchy, row.component);
                if (AssetDatabase.GetAssetPath(new Accessor(component, row.property).Get() as Object) != row.derived)
                    throw new InvalidOperationException("Saved SO binding mismatch: " + row.hierarchy + ":" + row.property);
            }
            foreach (var row in r.assets)
                if (JsonHash(LoadClean<ScriptableObject>(row.derived)) != row.derivedJsonSha256) throw new InvalidOperationException("Derived SO data changed: " + row.derived);
            foreach (var row in r.earlyFoliage)
                if (PacketSignature(Components<EarlyRegionFoliage>(SceneManager.GetActiveScene()).Single(v => Hierarchy(v.transform) == row.hierarchy).Packets) != row.afterSha256)
                    throw new InvalidOperationException("Saved supplementary foliage changed: " + row.hierarchy);
            LastReceipt = r; return JsonUtility.ToJson(r, true);
        }

        static void ValidateSceneChain(State s)
        {
            var baseline = s.export.sourceFiles.Single(row => row.path == CompactScene).beforeSha256;
            if (baseline == s.receipt.sceneBeforeSha256) { s.receipt.sourceSceneChainVerified = true; return; }
            if (string.IsNullOrWhiteSpace(s.settings.lookApplicationReceiptPath)) throw new InvalidOperationException("Scene differs from export; a matching persistent-look receipt chain is required.");
            string path = Resolve(s.settings.lookApplicationReceiptPath);
            var look = JsonUtility.FromJson<LookReceipt>(File.ReadAllText(path));
            if (look == null || look.status != "PASS_PERSISTENT_APPLICATION" || look.scene != CompactScene || !look.sceneSaved ||
                !look.sourcesUnchanged || !look.populationUnchanged || !look.supplementaryPlacementUnchanged || !look.structureUnchanged ||
                look.sceneBeforeSha256 != baseline || look.sceneAfterSha256 != s.receipt.sceneBeforeSha256)
                throw new InvalidOperationException("The changed scene is not explained by the source-preserving material/sky application receipt.");
            foreach (var source in look.sources) CheckFile(AbsoluteAsset(source.path), source.sha256);
            s.receipt.lookApplicationReceiptPath = path; s.receipt.lookReceiptSha256 = FileHash(path); s.receipt.sourceSceneChainVerified = true;
        }
        static void ValidateInputs(State s)
        {
            if (s.scene != SceneManager.GetActiveScene() || FileHash(AbsoluteAsset(CompactScene)) != s.receipt.sceneBeforeSha256)
                throw new InvalidOperationException("Application baseline scene changed; prepare again after review.");
            CheckFile(s.receipt.exportReceiptPath, s.receipt.exportReceiptSha256);
            CheckFile(s.receipt.physicsReceiptPath, s.receipt.physicsReceiptSha256); CheckFile(s.receipt.normalReceiptPath, s.receipt.normalReceiptSha256);
            if (!string.IsNullOrEmpty(s.receipt.lookApplicationReceiptPath)) CheckFile(s.receipt.lookApplicationReceiptPath, s.receipt.lookReceiptSha256);
            foreach (var row in s.receipt.assets) if (JsonHash(LoadClean<ScriptableObject>(row.derived)) != row.derivedJsonSha256) throw new InvalidOperationException("Prepared SO changed.");
        }
        static void VerifyState(State s, bool applied)
        {
            foreach (var file in s.receipt.sourceFiles) CheckFile(file.path, file.sha256);
            foreach (var source in s.sourceJson) if (JsonHash(source.Key) != source.Value || EditorUtility.IsDirty(source.Key)) throw new InvalidOperationException("Source SO changed in memory.");
            foreach (var mesh in s.meshes)
            {
                Clean(mesh.before); Clean(mesh.after);
                if (InkPaintingMountainForm.ExportedMeshFingerprint(mesh.before) != mesh.row.sourceMeshBeforeSha256 ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(mesh.after) != (mesh.row.changed ? mesh.row.exportedMeshSha256 : mesh.row.sourceMeshBeforeSha256))
                    throw new InvalidOperationException("Source/derived mesh payload changed in memory: " + mesh.row.hierarchy);
                if (mesh.target.sharedMesh != (applied ? mesh.after : mesh.before) || mesh.target.transform.localToWorldMatrix != mesh.row.localToWorld ||
                    (mesh.collider != null && mesh.collider.sharedMesh != (applied ? mesh.after : mesh.before))) throw new InvalidOperationException("Mesh/collider binding changed: " + mesh.row.hierarchy);
            }
            foreach (var binding in s.references) if (binding.access.Get() as Object != (applied ? binding.after : binding.before)) throw new InvalidOperationException("SO binding changed: " + binding.path);
            foreach (var binding in s.early) if (PacketSignature(binding.target.Packets) != (applied ? binding.row.afterSha256 : binding.row.beforeSha256)) throw new InvalidOperationException("Supplementary foliage payload changed.");
            var currentTransforms = Components<Transform>(s.scene);
            if (currentTransforms.Length != s.transforms.Count) throw new InvalidOperationException("Scene hierarchy count changed.");
            foreach (var t in s.transforms)
            {
                var plant = s.plants.FirstOrDefault(p => p.target == t.target);
                var expected = applied && plant != null ? plant.after : t.localPosition;
                if (t.target == null || t.target.parent != t.parent || !t.target.localPosition.Equals(expected) || !t.target.localRotation.Equals(t.localRotation) || !t.target.localScale.Equals(t.localScale))
                    throw new InvalidOperationException("Unexpected scene transform change.");
            }
            if (MaterialSignature(s.scene) != s.materialsHash) throw new InvalidOperationException("Renderer material bindings changed during geometry installation.");
            if (InkPaintingMountainForm.ExportSurfaceHash(s.delta.TriangleSurface) != s.export.surface.exportedDeltaSha256 ||
                InkPaintingMountainForm.ExportSurfaceHash(s.absolute.AbsoluteHeightTriangles) != s.export.surface.absolutePatchSha256)
                throw new InvalidOperationException("Final field payload changed.");
            s.receipt.sourceFilesUnchanged = s.receipt.selectionDataUnchanged = s.receipt.transformsPreserved = s.receipt.rendererMaterialsPreserved = true;
            s.receipt.protectedGeometryPreserved = true;
            s.receipt.exactSurfaceBindingsVerified = applied && s.replacements.Values.OfType<WorldMacroSheetSO>().All(g => g.FinalSurface == s.absolute) &&
                s.replacements.Values.OfType<WorldMacroDressingSheetSO>().All(d => d.SurfaceDeformation == s.delta && d.Geography.FinalSurface == s.absolute);
        }
        static void Bind(State s, bool applied)
        {
            // Absolute assignments from the captured baseline make this idempotent.
            // No matrix/position receives an accumulated second delta.
            foreach (var mesh in s.meshes) { mesh.target.sharedMesh = applied ? mesh.after : mesh.before; if (mesh.collider != null) mesh.collider.sharedMesh = applied ? mesh.after : mesh.before; }
            foreach (var binding in s.references) binding.access.Set(applied ? binding.after : binding.before);
            foreach (var binding in s.early) binding.target.Packets = applied ? binding.after : binding.before;
            foreach (var plant in s.plants) plant.target.localPosition = applied ? plant.after : plant.before;
            foreach (var renderer in Components<WorldMacroDressingRenderer>(s.scene)) renderer.ResetCache();
            Physics.SyncTransforms(); EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
        }
        static void RestoreBindings(State s)
        {
            EditorApplication.update -= PreviewTick; AssemblyReloadEvents.beforeAssemblyReload -= RestoreBeforeReload;
            var errors = new List<string>();
            void Attempt(Action action) { try { action(); } catch (Exception e) { errors.Add(e.Message); } }
            foreach (var mesh in s.meshes) { Attempt(() => mesh.target.sharedMesh = mesh.before); if (mesh.collider != null) Attempt(() => mesh.collider.sharedMesh = mesh.before); }
            foreach (var binding in s.references) Attempt(() => binding.access.Set(binding.before));
            foreach (var binding in s.early) Attempt(() => binding.target.Packets = binding.before);
            foreach (var plant in s.plants) Attempt(() => plant.target.localPosition = plant.before);
            foreach (var renderer in Components<WorldMacroDressingRenderer>(s.scene)) Attempt(renderer.ResetCache);
            Attempt(Physics.SyncTransforms); s.bound = errors.Count != 0; s.receipt.previewActive = s.bound; s.receipt.rollbackErrors = errors.ToArray();
            if (errors.Count != 0) throw new InvalidOperationException("Geometry restoration needs review: " + string.Join("; ", errors));
        }
        static void PreviewTick()
        {
            if (!IsPreviewActive) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup >= current.expires)
                try { EndPreview(); } catch (Exception e) { Debug.LogException(e); }
        }
        static void RestoreBeforeReload() { if (IsPreviewActive) EndPreview(); }

        sealed class SOReference { public string path; public ScriptableObject value; }
        static IEnumerable<SOReference> References(Object target)
        {
            using (var serialized = new SerializedObject(target))
            {
                var property = serialized.GetIterator(); bool enter = true;
                while (property.Next(enter))
                {
                    enter = property.propertyType == SerializedPropertyType.Generic;
                    if (property.isArray && (property.arrayElementType == "float" || property.arrayElementType == "double" || property.arrayElementType == "int" ||
                        property.arrayElementType == "UInt8" || property.arrayElementType == "byte" || property.arrayElementType == "Vector3f" || property.arrayElementType == "Vector2f" ||
                        property.arrayElementType == "Matrix4x4f" || property.arrayElementType == "Quaternionf" || property.arrayElementType == "ColorRGBA")) enter = false;
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is ScriptableObject value)
                        yield return new SOReference { path = property.propertyPath, value = value };
                }
            }
        }
        static Dictionary<ScriptableObject, SOReference[]> DiscoverGraph(Component[] components)
        {
            var graph = new Dictionary<ScriptableObject, SOReference[]>();
            var queue = new Queue<ScriptableObject>(components.SelectMany(References).Select(r => r.value).Distinct());
            while (queue.Count > 0)
            {
                var item = queue.Dequeue(); if (graph.ContainsKey(item)) continue;
                if (graph.Count >= 4096) throw new InvalidOperationException("Scene SO graph exceeds the bounded authoring limit.");
                var references = References(item).ToArray(); graph.Add(item, references);
                foreach (var reference in references) if (!graph.ContainsKey(reference.value)) queue.Enqueue(reference.value);
            }
            return graph;
        }
        // Direct field access avoids dirtying the user's scene for an Edit preview.
        // Serialized property paths are accepted only when they resolve to classes
        // or arrays; nested value-type references fail before any scene mutation.
        sealed class Accessor
        {
            readonly object owner; readonly FieldInfo field; readonly IList list; readonly int index;
            public object Storage => (object)list ?? owner;
            public Accessor(object root, string path)
            {
                string[] parts = path.Replace(".Array.data[", "[").Split('.'); object value = root;
                for (int i = 0; i < parts.Length; i++)
                {
                    int bracket = parts[i].IndexOf('['); string name = bracket < 0 ? parts[i] : parts[i].Substring(0, bracket);
                    if (value == null || value.GetType().IsValueType) throw new InvalidOperationException("Unsupported serialized reference owner: " + path);
                    FieldInfo found = null;
                    for (Type type = value.GetType(); type != null && found == null; type = type.BaseType) found = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (found == null) throw new InvalidOperationException("Serialized reference field not found: " + path);
                    if (bracket >= 0)
                    {
                        var array = found.GetValue(value) as IList ?? throw new InvalidOperationException("Serialized reference is not an array/list: " + path);
                        int at = int.Parse(parts[i].Substring(bracket + 1, parts[i].Length - bracket - 2));
                        if (i == parts.Length - 1) { list = array; index = at; return; } value = array[at];
                    }
                    else { if (i == parts.Length - 1) { owner = value; field = found; return; } value = found.GetValue(value); }
                }
                throw new InvalidOperationException("Empty serialized reference path.");
            }
            public object Get() => list != null ? list[index] : field.GetValue(owner);
            public void Set(Object value) { if (list != null) list[index] = value; else field.SetValue(owner, value); }
        }
        static void VerifyCloneOnlyAllowed(State s, ScriptableObject source, ScriptableObject clone, SOReference[] references)
        {
            foreach (var reference in references) if (s.replacements.ContainsKey(reference.value)) new Accessor(clone, reference.path).Set(reference.value);
            if (clone is WorldMacroSheetSO geography) geography.FinalSurface = ((WorldMacroSheetSO)source).FinalSurface;
            if (clone is WorldMacroDressingSheetSO dressing) dressing.SurfaceDeformation = ((WorldMacroDressingSheetSO)source).SurfaceDeformation;
            if (JsonHash(clone) != s.sourceJson[source]) throw new InvalidOperationException("A derivative changed data beyond references and final surface: " + source.name);
            foreach (var reference in references) if (s.replacements.TryGetValue(reference.value, out var mapped)) new Accessor(clone, reference.path).Set(mapped);
            if (clone is WorldMacroSheetSO geo) geo.FinalSurface = s.absolute;
            if (clone is WorldMacroDressingSheetSO dress) dress.SurfaceDeformation = s.delta;
        }
        static EarlyRegionFoliage.Packet[] ClonePackets(EarlyRegionFoliage early, WorldMacroSurfaceDeformationSO delta, FoliageRow row)
        {
            if (early.Owner == null || early.Owner.Sheet == null) throw new InvalidOperationException("Supplementary foliage owner missing.");
            return (early.Packets ?? Array.Empty<EarlyRegionFoliage.Packet>()).Select(packet => {
                if (packet == null || packet.Near == null || packet.Near.Length == 0 || packet.Near[0] == null) throw new InvalidOperationException("Supplementary packet has no placement anchor.");
                var anchor = packet.Near[0];
                var locals = early.Owner.Sheet.Prototypes.SelectMany(p => p.Lods).SelectMany(l => l.Parts)
                    .Where(p => p.Mesh == anchor.Mesh && p.Submesh == anchor.Submesh).Select(p => p.Local).Distinct().ToArray();
                if (locals.Length == 0) throw new InvalidOperationException("Supplementary placement anchor Local is missing: " + anchor.Mesh.name);
                int count = packet.Count; if (anchor.Matrices.Length != count) throw new InvalidOperationException("Supplementary count differs from placement index count.");
                var offsets = new float[count]; var inverse = locals[0].inverse;
                float minimum = 0, maximum = 0;
                for (int i = 0; i < count; i++)
                {
                    var root = anchor.Matrices[i] * inverse;
                    offsets[i] = delta.SampleDelta(root.m03, root.m23);
                    // Shared billboard meshes can have different extent-only Local
                    // scales. Accept them only when EVERY candidate recovers the
                    // same horizontal origin and surface delta for this placement.
                    foreach (var local in locals.Skip(1))
                    {
                        var other = anchor.Matrices[i] * local.inverse;
                        if (other.m03 == root.m03 && other.m23 == root.m23) continue;
                        if (Mathf.Abs(other.m03-root.m03) > .0001f || Mathf.Abs(other.m23-root.m23) > .0001f ||
                            Mathf.Abs(delta.SampleDelta(other.m03,other.m23)-offsets[i]) > .00001f)
                            throw new InvalidOperationException("Supplementary anchor has distinct placement origins: " + anchor.Mesh.name + " index " + i);
                    }
                    minimum = Math.Min(minimum, offsets[i]); maximum = Math.Max(maximum, offsets[i]);
                }
                EarlyRegionFoliage.Part[] Parts(EarlyRegionFoliage.Part[] parts) => (parts ?? Array.Empty<EarlyRegionFoliage.Part>()).Select(part => {
                    if (part == null || part.Matrices == null || part.Matrices.Length != count) throw new InvalidOperationException("Near/Far placement indices differ.");
                    var matrices = ShiftMatrices(part.Matrices, offsets); row.matrices += matrices.Length;
                    return new EarlyRegionFoliage.Part { Mesh = part.Mesh, Material = part.Material, Submesh = part.Submesh, Matrices = matrices };
                }).ToArray();
                Bounds bounds = packet.Bounds; var min = bounds.min; var max = bounds.max; min.y += minimum; max.y += maximum; bounds.SetMinMax(min, max);
                row.packets++; row.placements += count;
                return new EarlyRegionFoliage.Packet { Bounds = bounds, Count = count, Distance = packet.Distance, Near = Parts(packet.Near), Far = Parts(packet.Far) };
            }).ToArray();
        }
        internal static Matrix4x4[] ShiftMatrices(Matrix4x4[] originals, float[] delta)
        {
            if (originals.Length != delta.Length) throw new ArgumentException("Placement index arrays must match.");
            var result = (Matrix4x4[])originals.Clone();
            for (int i = 0; i < result.Length; i++) if (delta[i] != 0) { var matrix = result[i]; matrix.m13 = originals[i].m13 + delta[i]; result[i] = matrix; }
            return result;
        }
        static string PacketSignature(EarlyRegionFoliage.Packet[] packets) => Digest(writer => {
            writer.Write(packets?.Length ?? -1);
            foreach (var p in packets ?? Array.Empty<EarlyRegionFoliage.Packet>())
            {
                writer.Write(p != null); if (p == null) continue;
                writer.Write(p.Count); writer.Write(p.Distance); Vector(writer, p.Bounds.center); Vector(writer, p.Bounds.size);
                foreach (var parts in new[] { p.Near, p.Far })
                {
                    writer.Write(parts?.Length ?? -1);
                    foreach (var part in parts ?? Array.Empty<EarlyRegionFoliage.Part>())
                    {
                        writer.Write(part != null); if (part == null) continue; writer.Write(AssetKey(part.Mesh)); writer.Write(AssetKey(part.Material)); writer.Write(part.Submesh);
                        writer.Write(part.Matrices?.Length ?? -1); foreach (var matrix in part.Matrices ?? Array.Empty<Matrix4x4>()) for (int n = 0; n < 16; n++) writer.Write(matrix[n]);
                    }
                }
            }
        });
        static FileProof[] CaptureSources(State s, IEnumerable<ScriptableObject> graph)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in s.export.sourceFiles)
            {
                if (row.path == CompactScene) continue; // explicitly verified by the receipt chain, never silently waived
                CheckFile(AbsoluteAsset(row.path), row.beforeSha256); files.Add(AbsoluteAsset(row.path));
            }
            foreach (string path in s.export.createdAssets)
            { files.Add(AbsoluteAsset(path)); if (File.Exists(AbsoluteAsset(path + ".meta"))) files.Add(AbsoluteAsset(path + ".meta")); }
            foreach (var asset in graph)
            {
                Clean(asset); string path = AbsoluteAsset(AssetDatabase.GetAssetPath(asset)); files.Add(path);
                if (File.Exists(path + ".meta")) files.Add(path + ".meta");
            }
            return files.OrderBy(p => p, StringComparer.Ordinal).Select(p => new FileProof { path = p, sha256 = FileHash(p) }).ToArray();
        }
        static string MaterialSignature(Scene scene) => Digest(writer => {
            foreach (var renderer in Components<Renderer>(scene))
            {
                writer.Write(Hierarchy(renderer.transform));
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || EditorUtility.IsPersistent(material)) { writer.Write(AssetKey(material)); continue; }
                    // Water clocks and existing enemy presentations have transient
                    // materials. Preserve their exact binding and serialized content
                    // in this in-session comparison instead of demanding asset GUIDs.
                    // This neither adopts nor writes those materials to any asset.
                    writer.Write("existing-transient:"); writer.Write(material.GetInstanceID());
                    writer.Write(EditorJsonUtility.ToJson(material));
                }
            }
        });
        static Mesh LoadSourceMesh(InkPaintingMountainForm.ExportMeshRow row) => AssetDatabase.LoadAllAssetsAtPath(row.sourcePath).OfType<Mesh>().Single(mesh =>
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id) && guid == row.sourceGuid && id == row.sourceLocalId);
        static Component ResolveComponent(string id, string hierarchy, string type)
        {
            if (GlobalObjectId.TryParse(id, out var gid) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) is Component component) return component;
            return Components<Component>(SceneManager.GetActiveScene()).Single(c => c.GetType().FullName == type && Hierarchy(c.transform) == hierarchy);
        }
        static T LoadClean<T>(string path) where T : Object { var value = AssetDatabase.LoadAssetAtPath<T>(path); Clean(value); return value; }
        static void Clean(Object asset)
        {
            if (asset == null || !EditorUtility.IsPersistent(asset) || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("A clean persistent asset is required: " + (asset == null ? "null" : asset.name));
        }
        static void Guard(bool clean)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != CompactScene || !scene.isLoaded)
                throw new InvalidOperationException("Stable compact Edit scene required.");
            if (clean) for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve or restore all user scene edits before geometry application; this helper never saves an initially dirty scene.");
            if (InkPaintingMountainForm.IsActive || InkPaintingFoliagePreview.IsActive) throw new InvalidOperationException("Restore existing temporary previews first.");
            foreach (string name in new[] { "previewRenderers", "formOriginalFields", "temporarySky" })
                if (typeof(CompactInkPaintingStudy).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) != null) throw new InvalidOperationException("Restore the look preview first.");
            CommitGuard();
        }
        static void CommitGuard() { if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; geometry application stopped."); }
        static void Dirty(Object target) { EditorUtility.SetDirty(target); if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target); }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return; int cut = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, cut));
            if (AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1))) != path) throw new IOException("Could not create owned application folder.");
        }
        static void CheckFile(string path, string expected) { if (FileHash(path) != expected) throw new InvalidOperationException("Source/evidence file changed: " + path); }
        static void Write(ApplicationReceipt receipt)
        {
            string temporary = receipt.receiptPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true), new UTF8Encoding(false));
            if (File.Exists(receipt.receiptPath)) File.Replace(temporary, receipt.receiptPath, null);
            else File.Move(temporary, receipt.receiptPath);
        }
        static string AssetKey(Object obj)
        {
            if (obj == null) return "null";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long id)) throw new InvalidOperationException("Unidentified persistent asset: " + obj.name);
            return guid + ":" + id;
        }
        static string FileHash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        static string JsonHash(Object value)
        {
            // JsonUtility's instance IDs are session-local. Canonical asset GUID +
            // local IDs make the receipt valid after domain/scene reload as well.
            string canonical = CanonicalJson(JsonUtility.ToJson(value), id => {
                // The serializer emits legacy instanceID integers, not EntityId.
#pragma warning disable CS0618
                var reference = EditorUtility.InstanceIDToObject(id);
#pragma warning restore CS0618
                if (reference == null) throw new InvalidOperationException("Unresolved serialized asset instance ID: " + id);
                return AssetKey(reference);
            });
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }
        internal static string CanonicalJson(string json, Func<int, string> resolve) =>
            Regex.Replace(json, "(?<!\\\\)\"instanceID\"\\s*:\\s*(-?[0-9]+)", match => {
                int id = int.Parse(match.Groups[1].Value);
                return "\"asset\":\"" + (id == 0 ? "null" : resolve(id)) + "\"";
            });
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        static void Vector(BinaryWriter writer, Vector3 value) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
        static string Digest(Action<BinaryWriter> action)
        {
            using (var sha = SHA256.Create()) using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write)) using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            { action(writer); writer.Flush(); stream.FlushFinalBlock(); return Hex(sha.Hash); }
        }
    }
}
