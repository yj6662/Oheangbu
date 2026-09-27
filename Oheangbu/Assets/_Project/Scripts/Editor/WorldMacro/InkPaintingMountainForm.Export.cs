using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainForm
    {
        [Serializable] public sealed class ExportMeshRow
        {
            public string hierarchy, objectId, sourcePath, sourceGuid, derivedPath;
            public long sourceLocalId;
            public string sourceMeshBeforeSha256, sourceMeshAfterSha256, preparedMeshSha256, exportedMeshSha256;
            public bool collider, context, changed;
            public int vertices, triangles;
            public Matrix4x4 localToWorld;
        }
        [Serializable] public sealed class ExportStaticRootRow
        {
            public string hierarchy, objectId, status, reason;
            public Vector3 originalWorld, plannedWorld, originalLocal, plannedLocal;
            public float delta;
            public bool hasPreparedBinding, applied;
        }
        [Serializable] public sealed class ExportFileProof
        {
            public string path, beforeSha256, afterSha256;
            public bool unchanged;
        }
        // Linear row-major bin intervals, [first, first + count). No rectangular
        // coverage claim is made for the gaps between these intervals.
        [Serializable] public struct ExportBinRange { public int first, count; }
        [Serializable] public sealed class ExportSurfaceReceipt
        {
            public string semantics, deltaPath, absolutePatchPath, preparedDeltaSha256, exportedDeltaSha256, absolutePatchSha256;
            public Rect indexedExtent;
            public float binSize, edgeTolerance, comparisonTolerance, maximumAbsoluteSampleError;
            public int binsX, binsZ, coveredBins, activeDeformationBins, triangles, references, maximumCandidates, checkedSamples;
            public bool indexCopiedWithoutRebuild, fullWorldCoverage, holesUseExistingFallback, deltaAddedToAbsoluteHeight;
            public ExportBinRange[] coveredBinRanges;
        }
        [Serializable] public sealed class MountainExportReceipt
        {
            public int schemaVersion = 1;
            public string generation, utc, status, error, unityVersion, scene, assetFolder, outputFolder;
            public string beforeReceiptPath, completeReceiptPath, failedReceiptPath, preparationReceiptJson, preparationReceiptSha256;
            public string preservationScope;
            public bool sourceFilesUnchanged, sourceMeshesUnchanged, preparedMeshesUnchanged, sourceBindingsUnchanged, sceneClean;
            public bool sceneSaved, sceneBindingsApplied, navigationBaked, globallyEnabled;
            public ExportMeshRow[] meshes;
            public ExportStaticRootRow[] staticRoots;
            public ExportFileProof[] sourceFiles;
            public ExportSurfaceReceipt surface;
            public string[] createdAssets, createdFolders, verificationLimitations;
        }
        public static MountainExportReceipt LastExportReceipt { get; private set; }

        /// <summary>Read-only fingerprint shared with installation/navigation audits.</summary>
        public static string ExportedMeshFingerprint(Mesh mesh) => ExportMeshHash(mesh);

        /// <summary>
        /// Export a verified preparation as independent assets. Does not install,
        /// bind, save any scene, bake navigation, or mutate the preparation. On
        /// failure the unique generation is quarantined with its failed receipt.
        /// </summary>
        public static string ExportPrepared(string assetFolder, string outputFolder)
        {
            Guard();
            var s = prepared ?? throw new InvalidOperationException("Prepare and verify a mountain form before export.");
            ExportGuard(s);
            ExportValidateState(s);
            string assetBase = ExportNormalizeAssetFolder(assetFolder);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workspace = Path.GetFullPath(Path.Combine(projectRoot, ".."));
            string outputBase = ExportResolveOutput(outputFolder, workspace, Application.dataPath);
            string generation = "MountainForm_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var receipt = new MountainExportReceipt {
                generation = generation, utc = DateTime.UtcNow.ToString("O"), status = "before_export",
                unityVersion = Application.unityVersion, scene = s.scene.path,
                assetFolder = assetBase + "/" + generation, outputFolder = Path.Combine(outputBase, generation),
                preparationReceiptJson = JsonUtility.ToJson(s.receipt),
                preservationScope = "All prepared source meshes in memory; their source files and meta files; geography asset; active scene file; prepared mesh hashes and scene mesh/collider/static-root bindings. No project-wide preservation claim.",
                verificationLimitations = new[] {
                    "Assets only. No scene installation, scene save, navigation bake, traversal verification, or global enable.",
                    "The absolute surface is an exact final-height PATCH within indexed active bins, not a complete world height map. A missing triangle returns false and requires existing terrain fallback.",
                    "Static-root new positions are installation instructions only; original scene transforms remain unchanged.",
                    "Preparation support/protection checks are preserved verbatim. Export additionally checks cloning and absolute-height semantics; it does not replace visual QA or native physics/traversal validation."
                }
            };
            receipt.preparationReceiptSha256 = ExportStringHash(receipt.preparationReceiptJson);
            receipt.beforeReceiptPath = Path.Combine(receipt.outputFolder, "before_export.json");
            receipt.completeReceiptPath = Path.Combine(receipt.outputFolder, "complete_export.json");
            receipt.failedReceiptPath = Path.Combine(receipt.outputFolder, "FAILED_QUARANTINED.json");
            if (AssetDatabase.IsValidFolder(receipt.assetFolder) || Directory.Exists(Path.Combine(projectRoot, receipt.assetFolder)) || Directory.Exists(receipt.outputFolder))
                throw new InvalidOperationException("Unique export generation already exists.");
            var createdAssets = new List<string>();
            var createdFolders = new List<string>();
            var looseObjects = new List<Object>();
            LastExportReceipt = receipt;
            try
            {
                ExportCommitGuard();
                receipt.meshes = ExportPlanMeshes(s, receipt.assetFolder);
                receipt.staticRoots = ExportPlanStaticRoots(s);
                receipt.sourceFiles = ExportCaptureFiles(s, projectRoot, receipt.meshes);
                var data = s.foliageField.TriangleSurface;
                ExportValidateSurface(data);
                var ranges = ExportCoveredRanges(data);
                receipt.surface = new ExportSurfaceReceipt {
                    semantics = "Delta: max(originalY + deltaY) - max(originalY). Absolute patch: max(finalY), never original terrain height plus delta. Shared validated active-bin index, all overlapping zero/nonzero faces retained inside each covered bin.",
                    deltaPath = receipt.assetFolder + "/FoliageSurfaceDelta.asset",
                    absolutePatchPath = receipt.assetFolder + "/FinalTerrainAbsolutePatch.asset",
                    preparedDeltaSha256 = ExportSurfaceHash(data), indexedExtent = data.WorldXZ,
                    binSize = data.BinSize, edgeTolerance = data.WorldSpaceEdgeTolerance,
                    binsX = data.BinsX, binsZ = data.BinsZ, coveredBins = ranges.Sum(r => r.count),
                    activeDeformationBins = data.ActiveDeformationBins, triangles = data.Vertices.Length / 3,
                    references = data.TriangleIndices.Length, maximumCandidates = data.MaximumTrianglesPerBin,
                    coveredBinRanges = ranges, indexCopiedWithoutRebuild = true,
                    holesUseExistingFallback = true, comparisonTolerance = Math.Min(.001f, s.settings.SupportTolerance)
                };
                Directory.CreateDirectory(receipt.outputFolder);
                ExportWriteReceipt(receipt.beforeReceiptPath, receipt);

                ExportCreateFolders(receipt.assetFolder + "/Meshes", createdFolders);
                for (int i = 0; i < s.sources.Count; i++)
                {
                    var source = s.sources[i]; var row = receipt.meshes[i];
                    if (!row.changed) continue;
                    ExportCommitGuard();
                    var clone = Object.Instantiate(source.derived); // NEVER make the prepared object persistent.
                    looseObjects.Add(clone); clone.hideFlags = HideFlags.None;
                    clone.name = Path.GetFileNameWithoutExtension(row.derivedPath);
                    AssetDatabase.CreateAsset(clone, row.derivedPath); createdAssets.Add(row.derivedPath);
                    row.exportedMeshSha256 = ExportMeshHash(clone);
                    if (row.exportedMeshSha256 != row.preparedMeshSha256)
                        throw new InvalidOperationException("Exported mesh differs from its prepared derivative: " + row.hierarchy);
                }
                ExportCommitGuard();
                var delta = Object.Instantiate(s.foliageField);
                looseObjects.Add(delta); delta.hideFlags = HideFlags.None; delta.name = "FoliageSurfaceDelta";
                delta.HeightOffsets = (float[])s.foliageField.HeightOffsets.Clone();
                delta.TriangleSurface = ExportCopySurface(data, false);
                AssetDatabase.CreateAsset(delta, receipt.surface.deltaPath); createdAssets.Add(receipt.surface.deltaPath);
                receipt.surface.exportedDeltaSha256 = ExportSurfaceHash(delta.TriangleSurface);
                if (receipt.surface.exportedDeltaSha256 != receipt.surface.preparedDeltaSha256)
                    throw new InvalidOperationException("Delta surface changed while cloning.");

                ExportCommitGuard();
                var absolute = ScriptableObject.CreateInstance<WorldMacroFinalSurfaceSO>();
                looseObjects.Add(absolute); absolute.name = "FinalTerrainAbsolutePatch";
                absolute.AbsoluteHeightTriangles = ExportCopySurface(data, true);
                ExportCheckAbsoluteSamples(data, absolute.AbsoluteHeightTriangles, receipt.surface);
                AssetDatabase.CreateAsset(absolute, receipt.surface.absolutePatchPath); createdAssets.Add(receipt.surface.absolutePatchPath);
                receipt.surface.absolutePatchSha256 = ExportSurfaceHash(absolute.AbsoluteHeightTriangles);

                ExportVerifyPreservation(s, receipt, projectRoot);
                if (!receipt.sourceFilesUnchanged || !receipt.sourceMeshesUnchanged || !receipt.preparedMeshesUnchanged ||
                    !receipt.sourceBindingsUnchanged || !receipt.sceneClean)
                    throw new InvalidOperationException("Source preservation verification failed; exported generation is quarantined.");
                receipt.status = "complete_assets_only_not_installed";
                receipt.createdAssets = createdAssets.ToArray(); receipt.createdFolders = createdFolders.ToArray();
                ExportWriteReceipt(receipt.completeReceiptPath, receipt);
                return JsonUtility.ToJson(receipt, true);
            }
            catch (Exception exception)
            {
                receipt.status = "FAILED_QUARANTINED"; receipt.error = exception.ToString();
                receipt.createdAssets = createdAssets.ToArray(); receipt.createdFolders = createdFolders.ToArray();
                try { ExportVerifyPreservation(s, receipt, projectRoot); }
                catch (Exception verifyError) { receipt.error += "\nPreservation verification: " + verifyError; }
                // Only this call's unique receipt directory is written. Persistent
                // assets remain quarantined; there is no recursive deletion or rollback
                // that might touch source assets or another generation.
                if (Directory.Exists(receipt.outputFolder)) ExportWriteReceipt(receipt.failedReceiptPath, receipt);
                throw;
            }
            finally
            {
                foreach (var owned in looseObjects)
                    if (owned != null && !EditorUtility.IsPersistent(owned)) Object.DestroyImmediate(owned);
            }
        }

        static void ExportGuard(State s)
        {
            if (s.toe != null) throw new InvalidOperationException("Toe is preview-only; its separate algorithm/settings have not been approved for an export receipt.");
            if (s.applied || InkPaintingFoliagePreview.IsActive)
                throw new InvalidOperationException("End mountain and foliage previews before exporting.");
            // The look-study preview currently has no public active flag. Fail closed
            // if its private guard changes, rather than exporting during a preview.
            foreach (string name in new[] { "previewRenderers", "formOriginalFields", "temporarySky" })
            {
                var field = typeof(CompactInkPaintingStudy).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null || field.GetValue(null) != null)
                    throw new InvalidOperationException("Look preview must be fully restored before export: " + name);
            }
            if (!s.scene.IsValid() || !s.scene.isLoaded || SceneManager.GetActiveScene() != s.scene ||
                !s.scene.path.EndsWith("/W_Demo_Compact.unity", StringComparison.Ordinal))
                throw new InvalidOperationException("The prepared compact scene must remain active.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Export requires clean loaded scenes; save or restore intentional changes yourself first.");
            var r = s.receipt;
            if (r == null || !r.protectedVerticesExact || !r.protectedFacesExact || !r.sourceXZAndTopologyPreserved ||
                !r.foliageSupportWithinTolerance || r.supportMissing != 0 || r.supportSamples <= 0 ||
                float.IsNaN(r.maxFoliageSupportError) || r.maxFoliageSupportError > s.settings.SupportTolerance)
                throw new InvalidOperationException("The prepared protection and foliage-support checks must pass before export.");
            if (s.foliageField == null || !s.foliageField.UseTriangleSampling || EditorUtility.IsPersistent(s.foliageField))
                throw new InvalidOperationException("A transient verified source-aware delta field is required.");
        }

        static void ExportValidateState(State s)
        {
            foreach (var source in s.sources)
            {
                if (source.original == null || source.filter == null || source.filter.sharedMesh != source.original ||
                    source.filter.transform.localToWorldMatrix != source.matrix ||
                    (source.collider != null && source.collider.sharedMesh != source.original))
                    throw new InvalidOperationException("A prepared source mesh or transform binding has changed.");
                if (!EditorUtility.IsPersistent(source.original) || EditorUtility.IsDirty(source.original))
                    throw new InvalidOperationException("Source meshes must be persistent and clean: " + source.row.path);
                if (source.derived != null && EditorUtility.IsPersistent(source.derived))
                    throw new InvalidOperationException("A prepared derivative is unexpectedly persistent.");
                var vertices = source.original.vertices;
                if (vertices.Length != source.world.Length || source.original.subMeshCount != source.indices.Length)
                    throw new InvalidOperationException("Prepared source topology is stale.");
                for (int i = 0; i < vertices.Length; i++)
                    if (!source.matrix.MultiplyPoint3x4(vertices[i]).Equals(source.world[i]))
                        throw new InvalidOperationException("Prepared source vertex positions are stale: " + source.row.path);
                for (int sub = 0; sub < source.indices.Length; sub++)
                    if (!source.original.GetIndices(sub).SequenceEqual(source.indices[sub]))
                        throw new InvalidOperationException("Prepared source indices are stale: " + source.row.path);
            }
            foreach (var binding in s.staticPlants)
                if (binding.target == null || !binding.target.position.Equals(binding.worldPosition) ||
                    !binding.target.localPosition.Equals(binding.localPosition))
                    throw new InvalidOperationException("A prepared static vegetation root has moved.");
        }

        static ExportMeshRow[] ExportPlanMeshes(State s, string folder)
        {
            var rows = new ExportMeshRow[s.sources.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                ExportCommitGuard();
                var source = s.sources[i];
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source.original, out string guid, out long localId))
                    throw new InvalidOperationException("Cannot identify a source mesh asset.");
                rows[i] = new ExportMeshRow {
                    hierarchy = Hierarchy(source.filter.transform), objectId = GlobalObjectId.GetGlobalObjectIdSlow(source.filter).ToString(),
                    sourcePath = AssetDatabase.GetAssetPath(source.original), sourceGuid = guid, sourceLocalId = localId,
                    sourceMeshBeforeSha256 = ExportMeshHash(source.original),
                    preparedMeshSha256 = source.derived == null ? null : ExportMeshHash(source.derived),
                    derivedPath = source.derived == null ? null : folder + "/Meshes/" + i.ToString("D4") + "_" + guid.Substring(0, 8) + ".asset",
                    changed = source.derived != null, collider = source.collider != null, context = source.row.context,
                    vertices = source.row.vertices, triangles = source.row.triangles, localToWorld = source.matrix
                };
            }
            return rows;
        }

        static ExportStaticRootRow[] ExportPlanStaticRoots(State s)
        {
            return s.staticAudit.Select(row => {
                var binding = s.staticPlants.FirstOrDefault(b => ReferenceEquals(b.row, row));
                var result = new ExportStaticRootRow {
                    hierarchy = row.path, status = row.status, reason = row.reason, delta = row.delta,
                    originalWorld = row.originalPosition, plannedWorld = row.originalPosition, hasPreparedBinding = binding != null
                };
                if (binding != null)
                {
                    result.objectId = GlobalObjectId.GetGlobalObjectIdSlow(binding.target).ToString();
                    result.originalWorld = binding.worldPosition; result.originalLocal = binding.localPosition;
                    result.plannedWorld = binding.worldPosition + Vector3.up * row.delta;
                    result.plannedLocal = binding.target.parent == null ? result.plannedWorld : binding.target.parent.InverseTransformPoint(result.plannedWorld);
                }
                return result;
            }).ToArray();
        }

        static ExportFileProof[] ExportCaptureFiles(State s, string projectRoot, ExportMeshRow[] meshes)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in meshes.Select(row => row.sourcePath).Concat(new[] { s.scene.path, s.settings.Geography }))
            {
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("A source asset has no persistent path.");
                paths.Add(path); if (File.Exists(Path.Combine(projectRoot, path + ".meta"))) paths.Add(path + ".meta");
            }
            return paths.OrderBy(p => p, StringComparer.Ordinal).Select(path => new ExportFileProof {
                path = path, beforeSha256 = ExportFileHash(Path.Combine(projectRoot, path))
            }).ToArray();
        }

        static void ExportVerifyPreservation(State s, MountainExportReceipt r, string projectRoot)
        {
            r.sourceFilesUnchanged = r.sourceFiles != null;
            if (r.sourceFiles != null) foreach (var file in r.sourceFiles)
            {
                file.afterSha256 = ExportFileHash(Path.Combine(projectRoot, file.path));
                file.unchanged = file.beforeSha256 == file.afterSha256; r.sourceFilesUnchanged &= file.unchanged;
            }
            r.sourceMeshesUnchanged = r.preparedMeshesUnchanged = r.meshes != null && r.meshes.Length == s.sources.Count;
            if (r.meshes != null && r.meshes.Length == s.sources.Count) for (int i = 0; i < r.meshes.Length; i++)
            {
                var row = r.meshes[i]; var source = s.sources[i];
                row.sourceMeshAfterSha256 = ExportMeshHash(source.original);
                r.sourceMeshesUnchanged &= row.sourceMeshBeforeSha256 == row.sourceMeshAfterSha256;
                if (row.changed) r.preparedMeshesUnchanged &= source.derived != null && row.preparedMeshSha256 == ExportMeshHash(source.derived);
            }
            r.sourceBindingsUnchanged = false;
            ExportValidateState(s); r.sourceBindingsUnchanged = true;
            r.sceneClean = true;
            for (int i = 0; i < SceneManager.sceneCount; i++) r.sceneClean &= !SceneManager.GetSceneAt(i).isDirty;
            if (r.surface != null && ExportSurfaceHash(s.foliageField.TriangleSurface) != r.surface.preparedDeltaSha256)
                throw new InvalidOperationException("The prepared delta field changed during export.");
        }

        // Pure helpers are exercised by the staged offline harness. All arrays are
        // copied: exporting cannot alias or change the prepared field's packed data.
        internal static WorldMacroSurfaceDeformationSO.TriangleSurfaceData ExportCopySurface(
            WorldMacroSurfaceDeformationSO.TriangleSurfaceData source, bool absolute)
        {
            ExportValidateSurface(source);
            var vertices = (Vector3[])source.Vertices.Clone();
            if (absolute) for (int i = 0; i < vertices.Length; i++) vertices[i].y = source.SourceHeights[i] + vertices[i].y;
            return new WorldMacroSurfaceDeformationSO.TriangleSurfaceData {
                WorldXZ = source.WorldXZ, BinSize = source.BinSize, BinsX = source.BinsX, BinsZ = source.BinsZ,
                MaximumTrianglesPerBin = source.MaximumTrianglesPerBin, WorldSpaceEdgeTolerance = source.WorldSpaceEdgeTolerance,
                Vertices = vertices, SourceHeights = absolute ? Array.Empty<float>() : (float[])source.SourceHeights.Clone(),
                BinOffsets = (int[])source.BinOffsets.Clone(), TriangleIndices = (int[])source.TriangleIndices.Clone(),
                // Absolute mode is a sparse patch, not a delta field. Its coverage
                // provenance is recorded separately in ExportSurfaceReceipt.
                DeltaActiveBinsOnly = !absolute && source.DeltaActiveBinsOnly,
                ActiveDeformationBins = absolute ? 0 : source.ActiveDeformationBins
            };
        }

        internal static void ExportValidateSurface(WorldMacroSurfaceDeformationSO.TriangleSurfaceData data)
        {
            if (data == null || data.Vertices == null || data.Vertices.Length == 0 || data.Vertices.Length % 3 != 0 ||
                data.SourceHeights == null || data.SourceHeights.Length != data.Vertices.Length || !data.DeltaActiveBinsOnly ||
                !ExportFinite(data.BinSize) || data.BinSize <= 0 || !ExportFinite(data.WorldSpaceEdgeTolerance) ||
                data.WorldSpaceEdgeTolerance < 0 || data.WorldSpaceEdgeTolerance > .0001f ||
                !ExportFinite(data.WorldXZ.x) || !ExportFinite(data.WorldXZ.y) || !ExportFinite(data.WorldXZ.width) || !ExportFinite(data.WorldXZ.height) ||
                data.WorldXZ.width <= 0 || data.WorldXZ.height <= 0 ||
                data.BinsX <= 0 || data.BinsZ <= 0 || (long)data.BinsX * data.BinsZ > 1048576 ||
                data.BinOffsets == null || data.BinOffsets.Length != (long)data.BinsX * data.BinsZ + 1 ||
                data.TriangleIndices == null || data.TriangleIndices.Length > 32000000 ||
                data.BinOffsets[0] != 0 || data.BinOffsets[data.BinOffsets.Length - 1] != data.TriangleIndices.Length)
                throw new ArgumentException("A verified source-aware active-bin triangle field is required.");
            int maximum = 0, occupied = 0;
            for (int i = 0; i < data.BinOffsets.Length - 1; i++)
            {
                int count = data.BinOffsets[i + 1] - data.BinOffsets[i];
                if (count < 0 || count > 1024) throw new ArgumentException("Packed triangle index exceeds its verified query budget.");
                if (count > 0) occupied++;
                maximum = Math.Max(maximum, count);
            }
            if (maximum != data.MaximumTrianglesPerBin || occupied != data.ActiveDeformationBins)
                throw new ArgumentException("Packed triangle coverage metadata disagrees with the actual index.");
            int triangles = data.Vertices.Length / 3;
            foreach (int index in data.TriangleIndices)
                if (index < 0 || index >= triangles) throw new ArgumentException("Packed triangle index is out of bounds.");
            for (int i = 0; i < data.Vertices.Length; i++)
                if (!ExportFinite(data.Vertices[i].x) || !ExportFinite(data.Vertices[i].y) || !ExportFinite(data.Vertices[i].z) ||
                    !ExportFinite(data.SourceHeights[i]) || !ExportFinite(data.SourceHeights[i] + data.Vertices[i].y))
                    throw new ArgumentException("Triangle surface contains a non-finite coordinate.");
        }

        internal static ExportBinRange[] ExportCoveredRanges(WorldMacroSurfaceDeformationSO.TriangleSurfaceData data)
        {
            var ranges = new List<ExportBinRange>();
            int first = -1;
            for (int i = 0; i < data.BinOffsets.Length - 1; i++)
            {
                bool covered = data.BinOffsets[i + 1] > data.BinOffsets[i];
                if (covered && first < 0) first = i;
                if (!covered && first >= 0) { ranges.Add(new ExportBinRange { first = first, count = i - first }); first = -1; }
            }
            if (first >= 0) ranges.Add(new ExportBinRange { first = first, count = data.BinOffsets.Length - 1 - first });
            return ranges.ToArray();
        }

        static void ExportCheckAbsoluteSamples(WorldMacroSurfaceDeformationSO.TriangleSurfaceData delta,
            WorldMacroSurfaceDeformationSO.TriangleSurfaceData absolute, ExportSurfaceReceipt receipt)
        {
            // Deterministic spread over the packed active references, with repeated
            // triangles deduplicated. Full support validation happened in Prepare.
            var seen = new HashSet<int>();
            int stride = Math.Max(1, delta.TriangleIndices.Length / 2048);
            for (int index = 0; index < delta.TriangleIndices.Length; index += stride)
            {
                int first = delta.TriangleIndices[index] * 3;
                if (!seen.Add(first)) continue;
                var a = delta.Vertices[first]; var b = delta.Vertices[first + 1]; var c = delta.Vertices[first + 2];
                float x = (float)(((double)a.x + b.x + c.x) / 3), z = (float)(((double)a.z + b.z + c.z) / 3);
                if (!WorldMacroSurfaceDeformationSO.TrySampleTriangle(delta, x, z, out var sample)) continue;
                if (!WorldMacroFinalSurfaceSO.TrySample(absolute, x, z, out float finalY))
                    throw new InvalidOperationException("An absolute patch sample lost delta-index coverage.");
                receipt.checkedSamples++;
                receipt.maximumAbsoluteSampleError = Math.Max(receipt.maximumAbsoluteSampleError, Math.Abs(finalY - sample.AfterHeight));
            }
            if (receipt.checkedSamples == 0 || receipt.maximumAbsoluteSampleError > receipt.comparisonTolerance)
                throw new InvalidOperationException("Absolute patch samples disagree with the final source-aware surface.");
        }

        internal static string ExportNormalizeAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An Assets subfolder is required.");
            string normalized = folder.Replace('\\', '/').TrimEnd('/');
            string[] parts = normalized.Split('/');
            if (parts.Length < 2 || parts[0] != "Assets" || parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new ArgumentException("Asset folder must be a plain project-relative Assets subfolder without traversal.");
            return normalized;
        }
        internal static string ExportResolveOutput(string folder, string workspace, string assets)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("An output folder is required.");
            string full = Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(workspace, folder));
            if (!ExportInside(full, workspace) || ExportInside(full, assets))
                throw new ArgumentException("Receipts must be inside this workspace and outside the Unity Assets directory.");
            return full;
        }
        static bool ExportInside(string path, string root) => string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        static bool ExportFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void ExportCommitGuard()
        {
            if (Prologue.PrologueAudit.CommitRatio() >= .85f)
                throw new InvalidOperationException("System commit is at least 85%; export allocation stopped.");
        }
        static void ExportCreateFolders(string path, List<string> created)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            ExportCreateFolders(parent, created);
            string guid = AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
            if (string.IsNullOrEmpty(guid) || AssetDatabase.GUIDToAssetPath(guid) != path)
                throw new IOException("Could not create exact owned export folder: " + path);
            created.Add(path);
        }
        static void ExportWriteReceipt(string path, MountainExportReceipt receipt) => File.WriteAllText(path, JsonUtility.ToJson(receipt, true), new UTF8Encoding(false));
        static string ExportStringHash(string value) { using (var sha = SHA256.Create()) return ExportHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static string ExportFileHash(string path) { using (var input = File.OpenRead(path)) using (var sha = SHA256.Create()) return ExportHex(sha.ComputeHash(input)); }
        static string ExportHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        static string ExportHash(Action<BinaryWriter> write)
        {
            using (var sha = SHA256.Create())
            {
                using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                { write(writer); writer.Flush(); stream.FlushFinalBlock(); return ExportHex(sha.Hash); }
            }
        }
        static void ExportVector(BinaryWriter writer, Vector3 value) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
        static void ExportBounds(BinaryWriter writer, Bounds value) { ExportVector(writer, value.center); ExportVector(writer, value.size); }
        static void ExportNativeBytes(BinaryWriter writer, NativeArray<byte> bytes)
        {
            writer.Write(bytes.Length);
            var buffer = new byte[Math.Min(65536, bytes.Length)];
            for (int offset = 0; offset < bytes.Length; offset += buffer.Length)
            {
                int count = Math.Min(buffer.Length, bytes.Length - offset);
                NativeArray<byte>.Copy(bytes, offset, buffer, 0, count); writer.Write(buffer, 0, count);
            }
        }
        static string ExportMeshHash(Mesh mesh)
        {
            if (mesh == null || mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0)
                throw new InvalidOperationException("Export hashing requires unskinned terrain meshes without blend shapes.");
            return ExportHash(writer => {
                writer.Write(mesh.vertexCount); writer.Write((int)mesh.indexFormat); ExportBounds(writer, mesh.bounds);
                var attributes = mesh.GetVertexAttributes(); writer.Write(attributes.Length);
                foreach (var attribute in attributes) { writer.Write((int)attribute.attribute); writer.Write((int)attribute.format); writer.Write(attribute.dimension); writer.Write(attribute.stream); }
                using (var view = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var data = view[0]; writer.Write(data.vertexBufferCount);
                    for (int stream = 0; stream < data.vertexBufferCount; stream++) ExportNativeBytes(writer, data.GetVertexData<byte>(stream));
                    ExportNativeBytes(writer, data.GetIndexData<byte>());
                    writer.Write(data.subMeshCount);
                    for (int i = 0; i < data.subMeshCount; i++)
                    {
                        var sub = data.GetSubMesh(i); writer.Write((int)sub.topology); writer.Write(sub.indexStart); writer.Write(sub.indexCount);
                        writer.Write(sub.baseVertex); writer.Write(sub.firstVertex); writer.Write(sub.vertexCount); ExportBounds(writer, sub.bounds);
                    }
                }
            });
        }
        internal static string ExportSurfaceHash(WorldMacroSurfaceDeformationSO.TriangleSurfaceData data) => ExportHash(writer => {
            writer.Write(data.WorldXZ.x); writer.Write(data.WorldXZ.y); writer.Write(data.WorldXZ.width); writer.Write(data.WorldXZ.height);
            writer.Write(data.BinSize); writer.Write(data.BinsX); writer.Write(data.BinsZ); writer.Write(data.MaximumTrianglesPerBin);
            writer.Write(data.WorldSpaceEdgeTolerance); writer.Write(data.DeltaActiveBinsOnly); writer.Write(data.ActiveDeformationBins);
            writer.Write(data.Vertices.Length); foreach (var vertex in data.Vertices) ExportVector(writer, vertex);
            writer.Write(data.SourceHeights.Length); foreach (float y in data.SourceHeights) writer.Write(y);
            writer.Write(data.BinOffsets.Length); foreach (int value in data.BinOffsets) writer.Write(value);
            writer.Write(data.TriangleIndices.Length); foreach (int value in data.TriangleIndices) writer.Write(value);
        });
    }
}
