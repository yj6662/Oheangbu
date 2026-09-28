using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainForm
    {
        [Serializable] public sealed class ExportAuditProof
        {
            public int schemaVersion = 1;
            public string status, exportReceiptPath, exportReceiptSha256, generation;
            public string exportedPreparationUtc, auditedPreparationUtc, settingsSha256, deterministicPayloadSha256;
            public string physicalAuditSha256, normalAuditSha256, physicalAuditUtc, normalAuditUtc;
            public string sceneBeforeSha256, sceneAfterSha256;
            public int matchedSourceMeshes, matchedDerivedMeshes, matchedStaticRoots;
            public bool allMeshesMatched, transformsMatched, settingsMatched, fieldsMatched, staticRootsMatched;
            public bool checkedBeforeAudit, checkedAfterAudit, previewRestored, sceneUnchanged;
        }
        [Serializable] public sealed class ExportBoundAuditReceipt
        {
            public string status, error;
            public ExportAuditProof exportAuditProof;
            public string physicalJson, normalJson;
        }
        [Serializable] sealed class ExportProofEnvelope { public ExportAuditProof exportAuditProof; }
        public static ExportBoundAuditReceipt LastExportBoundAudit { get; private set; }

        /// <summary>
        /// Verify the CURRENT prepared geometry against an existing complete export,
        /// then audit exactly that state. Owns a scoped preview and always restores it.
        /// No artifact or asset is written. Save physicalJson/normalJson from the result;
        /// serializing LastPhysicalAudit alone would omit the attached provenance.
        /// </summary>
        public static string AuditAgainstExport(string exportReceiptPath)
        {
            Guard(); var s = prepared ?? throw new InvalidOperationException("Prepare the deterministic form first.");
            if (s.applied || EditorApplication.isCompiling || EditorApplication.isUpdating || s.scene != SceneManager.GetActiveScene() || s.scene.isDirty)
                throw new InvalidOperationException("A clean, stable, unbound prepared Edit scene is required.");
            if (InkPaintingFoliagePreview.IsActive) throw new InvalidOperationException("Restore temporary foliage preview before the export-bound audit.");
            ExportCommitGuard();
            string path = ExportAuditResolve(exportReceiptPath);
            var export = JsonUtility.FromJson<MountainExportReceipt>(File.ReadAllText(path));
            var original = RequireExportAuditReceipt(export);
            var proof = new ExportAuditProof {
                status = "RUNNING", exportReceiptPath = path, exportReceiptSha256 = ExportFileHash(path), generation = export.generation,
                exportedPreparationUtc = original.utc, auditedPreparationUtc = s.receipt.utc,
                settingsSha256 = ExportStringHash(JsonUtility.ToJson(original.settings)),
                deterministicPayloadSha256 = ExportAuditPayload(export, JsonUtility.ToJson(original.settings)),
                sceneBeforeSha256 = ExportFileHash(ExportAuditAsset(s.scene.path))
            };
            var result = new ExportBoundAuditReceipt { status = "RUNNING", exportAuditProof = proof };
            LastExportBoundAudit = result;
            bool began = false;
            try
            {
                VerifyPreparedExportIdentity(s, export, proof);
                proof.checkedBeforeAudit = true;
                BeginPreview(); began = true;
                // Recheck after binding so the actual colliders used by both audits
                // are the same derivatives whose raw buffers matched the export.
                VerifyPreparedExportIdentity(s, export, proof);
                AuditPhysicalSupport();
                if (LastPhysicalAudit == null || !LastPhysicalAudit.completed || !LastPhysicalAudit.preservationPass || !LastPhysicalAudit.colliderBindingsRestored)
                    throw new InvalidOperationException("Export-bound physical preservation audit did not pass.");
                AuditPhysicalNormals(JsonUtility.ToJson(LastPhysicalAudit));
                if (LastNormalAudit == null || !LastNormalAudit.completed || !LastNormalAudit.normalPreservationResolved || !LastNormalAudit.colliderBindingsRestored ||
                    LastNormalAudit.preparationUtc != s.receipt.utc || LastNormalAudit.physicalAuditUtc != LastPhysicalAudit.utc)
                    throw new InvalidOperationException("Export-bound normal preservation audit did not pass or has a different audit parent.");
                VerifyPreparedExportIdentity(s, export, proof);
                proof.checkedAfterAudit = true;
                proof.physicalAuditUtc = LastPhysicalAudit.utc; proof.normalAuditUtc = LastNormalAudit.utc;
                proof.physicalAuditSha256 = ExportStringHash(JsonUtility.ToJson(LastPhysicalAudit));
                proof.normalAuditSha256 = ExportStringHash(JsonUtility.ToJson(LastNormalAudit));
            }
            catch (Exception error) { result.status = "FAILED_EXPORT_BOUND_AUDIT"; result.error = error.ToString(); throw; }
            finally
            {
                if (began || s.applied)
                {
                    try { EndPreview(); proof.previewRestored = !s.applied; }
                    catch (Exception restore) { result.status = "FAILED_EXPORT_BOUND_RESTORE"; result.error += "\n" + restore; throw; }
                }
            }
            // No PASS proof is emitted until restoration, payload identity and the
            // disk/dirty-state checks all finish. Original timestamps remain intact.
            try
            {
                VerifyPreparedExportIdentity(s, export, proof);
                if (ExportFileHash(path) != proof.exportReceiptSha256) throw new InvalidOperationException("Export receipt changed during audit.");
                proof.sceneAfterSha256 = ExportFileHash(ExportAuditAsset(s.scene.path));
                proof.sceneUnchanged = proof.sceneBeforeSha256 == proof.sceneAfterSha256 && !s.scene.isDirty;
                if (!proof.previewRestored || !proof.sceneUnchanged) throw new InvalidOperationException("Export-bound audit did not preserve the clean scene.");
                proof.status = "PASS_EXACT_EXPORTED_PAYLOAD"; result.status = "PASS_EXPORT_BOUND_AUDITS";
                result.physicalJson = AttachExportAuditProof(JsonUtility.ToJson(LastPhysicalAudit, true), proof);
                result.normalJson = AttachExportAuditProof(JsonUtility.ToJson(LastNormalAudit, true), proof);
                return JsonUtility.ToJson(result, true);
            }
            catch (Exception error) { proof.status = "FAILED_FINAL_PROVENANCE_CHECK"; result.status = "FAILED_EXPORT_BOUND_AUDIT"; result.error = error.ToString(); throw; }
        }

        /// <summary>
        /// Shared installer/Nav gate. Legacy same-preparation receipts retain their
        /// existing timestamp requirement. Different preparation times require both
        /// audits' exact same export-bound proof and unchanged canonical audit payloads.
        /// </summary>
        public static bool AuditProvenanceMatchesExport(string exportReceiptPath, MountainExportReceipt export, string physicalJson, string normalJson)
            => EvaluateAuditProvenance(exportReceiptPath, export, physicalJson, normalJson).pass;

        [Serializable] public sealed class AuditProvenanceDiagnostic
        {
            public bool pass;
            public string predicate, expected, actual, exception;
            public string physicalCanonicalSha256, normalCanonicalSha256, settingsCanonicalSha256, payloadSha256;
        }

        /// <summary>Read-only file diagnostic using the exact same predicates as the installer/Nav gate.</summary>
        public static string DiagnoseAuditProvenance(string exportReceiptPath, string physicalReceiptPath, string normalReceiptPath)
        {
            try
            {
                return JsonUtility.ToJson(EvaluateAuditProvenance(exportReceiptPath,
                    JsonUtility.FromJson<MountainExportReceipt>(File.ReadAllText(ExportAuditResolve(exportReceiptPath))),
                    File.ReadAllText(ExportAuditResolve(physicalReceiptPath)), File.ReadAllText(ExportAuditResolve(normalReceiptPath))), true);
            }
            catch (Exception error)
            {
                return JsonUtility.ToJson(new AuditProvenanceDiagnostic { predicate = "input_files", exception = error.ToString() }, true);
            }
        }

        static AuditProvenanceDiagnostic EvaluateAuditProvenance(string exportReceiptPath, MountainExportReceipt export, string physicalJson, string normalJson)
        {
            var diagnostic = new AuditProvenanceDiagnostic();
            AuditProvenanceDiagnostic Fail(string predicate, string expected = "true", string actual = "false")
            {
                diagnostic.predicate = predicate; diagnostic.expected = expected; diagnostic.actual = actual; return diagnostic;
            }
            try
            {
                diagnostic.predicate = "complete_export_receipt";
                var preparation = RequireExportAuditReceipt(export);
                var physical = JsonUtility.FromJson<PhysicalAuditReceipt>(physicalJson);
                var normal = JsonUtility.FromJson<NormalAuditReceipt>(normalJson);
                if (physical == null || normal == null) return Fail("audit_receipts_present");
                if (physical.scene != export.scene) return Fail("physical.scene", export.scene, physical.scene);
                if (normal.scene != export.scene) return Fail("normal.scene", export.scene, normal.scene);
                if (!physical.completed) return Fail("physical.completed");
                if (!physical.preservationPass) return Fail("physical.preservationPass");
                if (!physical.colliderBindingsRestored) return Fail("physical.colliderBindingsRestored");
                if (!normal.completed) return Fail("normal.completed");
                if (!normal.normalPreservationResolved) return Fail("normal.normalPreservationResolved");
                if (!normal.colliderBindingsRestored) return Fail("normal.colliderBindingsRestored");
                if (normal.physicalAuditUtc != physical.utc) return Fail("normal.physicalAuditUtc", physical.utc, normal.physicalAuditUtc);
                var a = JsonUtility.FromJson<ExportProofEnvelope>(physicalJson)?.exportAuditProof;
                var b = JsonUtility.FromJson<ExportProofEnvelope>(normalJson)?.exportAuditProof;
                if (a == null && b == null)
                {
                    if (normal.preparationUtc != preparation.utc) return Fail("legacy.preparationUtc", preparation.utc, normal.preparationUtc);
                    diagnostic.pass = true; diagnostic.predicate = "PASS_LEGACY_SAME_PREPARATION"; return diagnostic;
                }
                if (a == null || b == null) return Fail("both_export_proofs_present");
                if (JsonUtility.ToJson(a) != JsonUtility.ToJson(b)) return Fail("proofs_identical", JsonUtility.ToJson(a), JsonUtility.ToJson(b));
                string path = ExportAuditResolve(exportReceiptPath);
                string settingsJson = JsonUtility.ToJson(preparation.settings);
                // JsonUtility can change a float's text after reading its own JSON.
                // Bind the original emitted tokens instead: only insignificant JSON
                // whitespace and the separately verified top-level proof are removed.
                diagnostic.physicalCanonicalSha256 = ExportStringHash(CanonicalExportAuditJson(physicalJson));
                diagnostic.normalCanonicalSha256 = ExportStringHash(CanonicalExportAuditJson(normalJson));
                diagnostic.settingsCanonicalSha256 = ExportStringHash(settingsJson);
                diagnostic.payloadSha256 = ExportAuditPayload(export, settingsJson);
                if (a.schemaVersion != 1) return Fail("proof.schemaVersion", "1", a.schemaVersion.ToString());
                if (a.status != "PASS_EXACT_EXPORTED_PAYLOAD") return Fail("proof.status", "PASS_EXACT_EXPORTED_PAYLOAD", a.status);
                if (!string.Equals(ExportAuditResolve(a.exportReceiptPath), path, StringComparison.OrdinalIgnoreCase)) return Fail("proof.exportReceiptPath", path, ExportAuditResolve(a.exportReceiptPath));
                string exportHash = ExportFileHash(path);
                if (a.exportReceiptSha256 != exportHash) return Fail("proof.exportReceiptSha256", a.exportReceiptSha256, exportHash);
                if (a.generation != export.generation) return Fail("proof.generation", export.generation, a.generation);
                if (a.exportedPreparationUtc != preparation.utc) return Fail("proof.exportedPreparationUtc", preparation.utc, a.exportedPreparationUtc);
                if (a.auditedPreparationUtc != normal.preparationUtc) return Fail("proof.auditedPreparationUtc", normal.preparationUtc, a.auditedPreparationUtc);
                if (a.physicalAuditUtc != physical.utc) return Fail("proof.physicalAuditUtc", physical.utc, a.physicalAuditUtc);
                if (a.normalAuditUtc != normal.utc) return Fail("proof.normalAuditUtc", normal.utc, a.normalAuditUtc);
                if (a.physicalAuditSha256 != diagnostic.physicalCanonicalSha256) return Fail("proof.physicalAuditSha256", a.physicalAuditSha256, diagnostic.physicalCanonicalSha256);
                if (a.normalAuditSha256 != diagnostic.normalCanonicalSha256) return Fail("proof.normalAuditSha256", a.normalAuditSha256, diagnostic.normalCanonicalSha256);
                string normalSettingsJson = JsonUtility.ToJson(normal.previewSettings);
                if (normalSettingsJson != settingsJson) return Fail("normal.previewSettings", settingsJson, normalSettingsJson);
                if (a.settingsSha256 != diagnostic.settingsCanonicalSha256) return Fail("proof.settingsSha256", a.settingsSha256, diagnostic.settingsCanonicalSha256);
                if (a.deterministicPayloadSha256 != diagnostic.payloadSha256) return Fail("proof.deterministicPayloadSha256", a.deterministicPayloadSha256, diagnostic.payloadSha256);
                if (a.matchedSourceMeshes != export.meshes.Length) return Fail("proof.matchedSourceMeshes", export.meshes.Length.ToString(), a.matchedSourceMeshes.ToString());
                if (a.matchedDerivedMeshes != export.meshes.Count(row => row.changed)) return Fail("proof.matchedDerivedMeshes", export.meshes.Count(row => row.changed).ToString(), a.matchedDerivedMeshes.ToString());
                if (a.matchedStaticRoots != export.staticRoots.Count(row => row.hasPreparedBinding)) return Fail("proof.matchedStaticRoots", export.staticRoots.Count(row => row.hasPreparedBinding).ToString(), a.matchedStaticRoots.ToString());
                if (!a.allMeshesMatched) return Fail("proof.allMeshesMatched");
                if (!a.transformsMatched) return Fail("proof.transformsMatched");
                if (!a.settingsMatched) return Fail("proof.settingsMatched");
                if (!a.fieldsMatched) return Fail("proof.fieldsMatched");
                if (!a.staticRootsMatched) return Fail("proof.staticRootsMatched");
                if (!a.checkedBeforeAudit) return Fail("proof.checkedBeforeAudit");
                if (!a.checkedAfterAudit) return Fail("proof.checkedAfterAudit");
                if (!a.previewRestored) return Fail("proof.previewRestored");
                if (!a.sceneUnchanged) return Fail("proof.sceneUnchanged");
                if (string.IsNullOrEmpty(a.sceneBeforeSha256)) return Fail("proof.sceneBeforeSha256_present");
                if (a.sceneBeforeSha256 != a.sceneAfterSha256) return Fail("proof.sceneAfterSha256", a.sceneBeforeSha256, a.sceneAfterSha256);
                // Ensure the object supplied by the caller really is the current
                // hashed receipt, not a different in-memory manifest with that path.
                var disk = JsonUtility.FromJson<MountainExportReceipt>(File.ReadAllText(path));
                if (disk == null) return Fail("disk_export_present");
                if (disk.generation != export.generation) return Fail("disk.generation", export.generation, disk.generation);
                string diskPayload = ExportAuditPayload(disk, settingsJson);
                if (diskPayload != a.deterministicPayloadSha256) return Fail("disk.deterministicPayloadSha256", a.deterministicPayloadSha256, diskPayload);
                diagnostic.pass = true; diagnostic.predicate = "PASS_EXACT_EXPORTED_PAYLOAD"; return diagnostic;
            }
            catch (Exception error) { diagnostic.exception = error.ToString(); return diagnostic; }
        }

        static Receipt RequireExportAuditReceipt(MountainExportReceipt export)
        {
            if (export == null || export.status != "complete_assets_only_not_installed" || !export.sourceFilesUnchanged || !export.sourceMeshesUnchanged ||
                !export.preparedMeshesUnchanged || !export.sourceBindingsUnchanged || export.meshes == null || export.surface == null || export.staticRoots == null ||
                export.preparationReceiptSha256 != ExportStringHash(export.preparationReceiptJson)) throw new InvalidOperationException("Complete unchanged export proof is required.");
            var preparation = JsonUtility.FromJson<Receipt>(export.preparationReceiptJson);
            if (preparation == null || preparation.settings == null || !preparation.protectedVerticesExact || !preparation.protectedFacesExact ||
                !preparation.sourceXZAndTopologyPreserved || !preparation.foliageSupportWithinTolerance) throw new InvalidOperationException("Export protection/support proof is incomplete.");
            return preparation;
        }
        static void VerifyPreparedExportIdentity(State s, MountainExportReceipt export, ExportAuditProof proof)
        {
            var old = RequireExportAuditReceipt(export);
            if (s.scene.path != export.scene || s.receipt == null || s.receipt.utc != proof.auditedPreparationUtc ||
                !s.receipt.protectedVerticesExact || !s.receipt.protectedFacesExact || !s.receipt.sourceXZAndTopologyPreserved || !s.receipt.foliageSupportWithinTolerance ||
                JsonUtility.ToJson(s.settings) != JsonUtility.ToJson(old.settings)) throw new InvalidOperationException("Prepared settings/protection do not exactly match the exported form.");
            if (s.sources.Count != export.meshes.Length || export.meshes.Select(row => row.hierarchy).Distinct().Count() != export.meshes.Length)
                throw new InvalidOperationException("Prepared/export mesh inventories differ.");
            foreach (var row in export.meshes)
            {
                var source = s.sources.Single(v => Hierarchy(v.filter.transform) == row.hierarchy);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source.original, out string guid, out long id);
                if (guid != row.sourceGuid || id != row.sourceLocalId || AssetDatabase.GetAssetPath(source.original) != row.sourcePath ||
                    EditorUtility.IsDirty(source.original) || (source.derived != null && EditorUtility.IsPersistent(source.derived)) ||
                    (source.derived != null) != row.changed || (source.collider != null) != row.collider || source.row.context != row.context ||
                    source.row.vertices != row.vertices || source.row.triangles != row.triangles ||
                    !ExportAuditMatrixEqual(source.matrix, row.localToWorld) || !ExportAuditMatrixEqual(source.filter.transform.localToWorldMatrix, row.localToWorld))
                    throw new InvalidOperationException("Prepared source identity/transform differs: " + row.hierarchy);
                string originalHash = ExportMeshHash(source.original);
                if (originalHash != row.sourceMeshBeforeSha256 || originalHash != row.sourceMeshAfterSha256)
                    throw new InvalidOperationException("Prepared original mesh payload differs: " + row.hierarchy);
                if (row.changed)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Mesh>(row.derivedPath);
                    if (asset == null || EditorUtility.IsDirty(asset) || row.preparedMeshSha256 != row.exportedMeshSha256 ||
                        ExportMeshHash(source.derived) != row.exportedMeshSha256 || ExportMeshHash(asset) != row.exportedMeshSha256)
                        throw new InvalidOperationException("Prepared/exported derived mesh payload differs: " + row.hierarchy);
                }
                var expected = s.applied && source.derived != null ? source.derived : source.original;
                if (source.filter.sharedMesh != expected || (source.collider != null && source.collider.sharedMesh != expected))
                    throw new InvalidOperationException("Actual audit render/collider binding differs: " + row.hierarchy);
            }
            var delta = AssetDatabase.LoadAssetAtPath<WorldMacroSurfaceDeformationSO>(export.surface.deltaPath);
            var absolute = AssetDatabase.LoadAssetAtPath<WorldMacroFinalSurfaceSO>(export.surface.absolutePatchPath);
            if (delta == null || absolute == null || EditorUtility.IsDirty(delta) || EditorUtility.IsDirty(absolute) ||
                ExportSurfaceHash(s.foliageField.TriangleSurface) != export.surface.preparedDeltaSha256 ||
                export.surface.preparedDeltaSha256 != export.surface.exportedDeltaSha256 ||
                ExportSurfaceHash(delta.TriangleSurface) != export.surface.exportedDeltaSha256 ||
                ExportSurfaceHash(absolute.AbsoluteHeightTriangles) != export.surface.absolutePatchSha256)
                throw new InvalidOperationException("Prepared/exported exact delta or absolute patch differs.");
            var plants = export.staticRoots.Where(row => row.hasPreparedBinding).ToArray();
            if (s.staticPlants.Count != plants.Length) throw new InvalidOperationException("Prepared static-root inventory differs from export.");
            foreach (var row in plants)
            {
                var plant = s.staticPlants.Single(v => Hierarchy(v.target) == row.hierarchy);
                var planned = plant.worldPosition + Vector3.up * plant.row.delta;
                if (!plant.localPosition.Equals(row.originalLocal) || !plant.worldPosition.Equals(row.originalWorld) || plant.row.delta != row.delta || !planned.Equals(row.plannedWorld) ||
                    !plant.target.position.Equals(s.applied ? row.plannedWorld : row.originalWorld))
                    throw new InvalidOperationException("Prepared static-root payload differs: " + row.hierarchy);
            }
            // The saved material application can intentionally change the scene file.
            // All original export assets/meta remain immutable; scene preservation is
            // measured against the current file before and after these scoped audits.
            foreach (var file in export.sourceFiles)
                if (file.path != export.scene && ExportFileHash(ExportAuditAsset(file.path)) != file.beforeSha256)
                    throw new InvalidOperationException("Export source asset changed: " + file.path);
            proof.matchedSourceMeshes = s.sources.Count; proof.matchedDerivedMeshes = s.sources.Count(v => v.derived != null); proof.matchedStaticRoots = s.staticPlants.Count;
            proof.allMeshesMatched = proof.transformsMatched = proof.settingsMatched = proof.fieldsMatched = proof.staticRootsMatched = true;
        }
        internal static bool ExportAuditMatrixEqual(Matrix4x4 left, Matrix4x4 right)
        {
            for (int i = 0; i < 16; i++) if (BitConverter.SingleToInt32Bits(left[i]) != BitConverter.SingleToInt32Bits(right[i])) return false;
            return true;
        }
        // Binary float payloads avoid tolerance-based transform equivalence. UTC is
        // deliberately outside this digest; exact content proves deterministic identity.
        internal static string ExportAuditPayload(MountainExportReceipt export, string settingsJson) => ExportHash(writer => {
            writer.Write("ExportAuditPayload/v1"); writer.Write(export.generation); writer.Write(export.scene); writer.Write(settingsJson);
            writer.Write(export.meshes.Length);
            foreach (var row in export.meshes.OrderBy(row => row.hierarchy, StringComparer.Ordinal))
            {
                writer.Write(row.hierarchy); writer.Write(row.sourcePath); writer.Write(row.sourceGuid); writer.Write(row.sourceLocalId);
                writer.Write(row.sourceMeshBeforeSha256); writer.Write(row.sourceMeshAfterSha256); writer.Write(row.changed); writer.Write(row.collider); writer.Write(row.context);
                writer.Write(row.vertices); writer.Write(row.triangles);
                writer.Write(row.derivedPath ?? ""); writer.Write(row.preparedMeshSha256 ?? ""); writer.Write(row.exportedMeshSha256 ?? "");
                for (int i = 0; i < 16; i++) writer.Write(row.localToWorld[i]);
            }
            writer.Write(export.surface.preparedDeltaSha256); writer.Write(export.surface.exportedDeltaSha256); writer.Write(export.surface.absolutePatchSha256);
            var roots = export.staticRoots.Where(row => row.hasPreparedBinding).OrderBy(row => row.hierarchy, StringComparer.Ordinal).ToArray(); writer.Write(roots.Length);
            foreach (var root in roots)
            {
                writer.Write(root.hierarchy); writer.Write(root.delta); ExportVector(writer, root.originalWorld); ExportVector(writer, root.plannedWorld);
                ExportVector(writer, root.originalLocal); ExportVector(writer, root.plannedLocal);
            }
        });
        internal static string AttachExportAuditProof(string auditJson, ExportAuditProof proof)
        {
            string audit = auditJson.Trim(), envelope = JsonUtility.ToJson(new ExportProofEnvelope { exportAuditProof = proof });
            if (!audit.StartsWith("{", StringComparison.Ordinal) || !audit.EndsWith("}", StringComparison.Ordinal)) throw new ArgumentException("An object audit JSON is required.");
            return audit.Substring(0, audit.Length - 1) + ",\n" + envelope.Substring(1);
        }
        internal static string CanonicalExportAuditJson(string json) => new ExportAuditTokenReader(json).Read();

        // Strict structural JSON reader, not a numeric/object round trip. Duplicate
        // decoded keys are rejected at every depth (including escaped aliases).
        // Unknown proof fields cannot hide inside the excluded proof object.
        sealed class ExportAuditTokenReader
        {
            readonly string text;
            readonly HashSet<string> proofFields = new HashSet<string>(typeof(ExportAuditProof).GetFields().Select(f => f.Name), StringComparer.Ordinal);
            int position, proofs;
            public ExportAuditTokenReader(string text) { this.text = text ?? throw new ArgumentNullException(nameof(text)); }
            public string Read()
            {
                Space(); string result = Object(0, false); Space();
                if (position != text.Length || proofs != 1) throw Invalid("Exactly one top-level exportAuditProof and no trailing tokens are required");
                return result;
            }
            FormatException Invalid(string reason) => new FormatException(reason + " at JSON offset " + position);
            void Space() { while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\r' || text[position] == '\n')) position++; }
            bool Take(char c) { Space(); if (position < text.Length && text[position] == c) { position++; return true; } return false; }
            void Need(char c) { if (!Take(c)) throw Invalid("Expected " + c); }
            string Object(int depth, bool proof)
            {
                if (depth > 128) throw Invalid("JSON nesting exceeds 128");
                Need('{'); var output = new StringBuilder("{"); var keys = new HashSet<string>(StringComparer.Ordinal); bool first = true;
                if (!Take('}'))
                {
                    do
                    {
                        Space(); int start = position; string key = String(); string rawKey = text.Substring(start, position - start);
                        if (!keys.Add(key)) throw Invalid("Duplicate object key: " + key);
                        if (proof && !proofFields.Contains(key)) throw Invalid("Unknown export proof field: " + key);
                        Need(':'); bool skip = depth == 0 && key == "exportAuditProof";
                        string value = skip ? Object(depth + 1, true) : Value(depth + 1);
                        if (skip) proofs++;
                        else { if (!first) output.Append(','); first = false; output.Append(rawKey).Append(':').Append(value); }
                    } while (Take(','));
                    Need('}');
                }
                return output.Append('}').ToString();
            }
            string Value(int depth)
            {
                if (depth > 128) throw Invalid("JSON nesting exceeds 128");
                Space(); if (position == text.Length) throw Invalid("Missing value");
                int start = position; char c = text[position];
                if (c == '{') return Object(depth, false);
                if (c == '[')
                {
                    position++; var output = new StringBuilder("["); bool first = true;
                    if (!Take(']')) { do { if (!first) output.Append(','); first = false; output.Append(Value(depth + 1)); } while (Take(',')); Need(']'); }
                    return output.Append(']').ToString();
                }
                if (c == '"') String();
                else if (c == 't') Literal("true");
                else if (c == 'f') Literal("false");
                else if (c == 'n') Literal("null");
                else Number();
                return text.Substring(start, position - start);
            }
            void Literal(string value)
            {
                if (position + value.Length > text.Length || string.CompareOrdinal(text, position, value, 0, value.Length) != 0) throw Invalid("Invalid literal");
                position += value.Length;
            }
            bool Digit() => position < text.Length && text[position] >= '0' && text[position] <= '9';
            void Digits() { if (!Digit()) throw Invalid("Expected digit"); while (Digit()) position++; }
            void Number()
            {
                if (position < text.Length && text[position] == '-') position++;
                if (position < text.Length && text[position] == '0') position++;
                else Digits();
                if (position < text.Length && text[position] == '.') { position++; Digits(); }
                if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
                {
                    position++; if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++; Digits();
                }
            }
            string String()
            {
                Need('"'); var decoded = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++]; if (c == '"') return decoded.ToString();
                    if (c < 32) throw Invalid("Unescaped control character");
                    if (c != '\\') { decoded.Append(c); continue; }
                    if (position == text.Length) throw Invalid("Incomplete escape");
                    c = text[position++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': decoded.Append(c); break;
                        case 'b': decoded.Append('\b'); break; case 'f': decoded.Append('\f'); break;
                        case 'n': decoded.Append('\n'); break; case 'r': decoded.Append('\r'); break; case 't': decoded.Append('\t'); break;
                        case 'u':
                            int unicode = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                if (position == text.Length) throw Invalid("Incomplete unicode escape");
                                char hex = text[position++]; int n = hex >= '0' && hex <= '9' ? hex - '0' : hex >= 'a' && hex <= 'f' ? hex - 'a' + 10 : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                                if (n < 0) throw Invalid("Invalid unicode escape"); unicode = unicode * 16 + n;
                            }
                            decoded.Append((char)unicode); break;
                        default: throw Invalid("Invalid escape");
                    }
                }
                throw Invalid("Unterminated string");
            }
        }
        static string ExportAuditResolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, "../..", path));
        static string ExportAuditAsset(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, "..", path));
    }
}
