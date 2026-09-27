using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainApplication
    {
        // Trial source view never saves a scene or changes navigation/materials.
        // The separate explicit export command may create owned candidate assets; it never installs them.
#pragma warning disable CS0649
        [Serializable] sealed class SpurAsset { public string guid; public long fileID; }
        [Serializable] sealed class SpurBounds { public Vector3 m_Center, m_Extent; }
        [Serializable] sealed class SpurPart { public SpurAsset Mesh, Material; public int Submesh; public Matrix4x4[] Matrices; }
        [Serializable] sealed class SpurPacket { public SpurBounds Bounds; public SpurPart[] Near, Far; public float Distance; public int Count; }
        [Serializable] sealed class SpurEarly { public string hierarchy, beforeSha256, afterSha256; public SpurPacket[] packets; }
        [Serializable] sealed class SpurNavigation { public string generation, geometryGeneration, sceneAfterSha256; public bool installed, sceneSaved; public int next; }
        [Serializable] sealed class SpurManifest
        {
            public int schemaVersion;
            public string applicationPath, applicationSha256, exportPath, exportSha256, navigationPath, navigationSha256;
            public string sceneSha256, backupPath, backupSha256, generation;
            public string variant;
            public InkPaintingMountainForm.Settings settings; public SpurEarly[] early;
            public InkPaintingMountainForm.ToeSettings toe;
        }
#pragma warning restore CS0649
        [Serializable] public sealed class SouthernSpurTrialReceipt
        {
            public string status, utc, manifestPath, manifestSha256, installedGeneration, output, error;
            public string variant;
            public string installedSceneSha256, preparationJson, exportReceiptPath, capturePath;
            public bool sourceMeshesFromReceipt, sourceSOsFromReceipt, exactOriginalEarlyMatrices, sourceStaticRootsFromReceipt;
            public bool sceneFileUnchanged, navigationUnchanged, installedBindingsRestored, materialBindingsUnchanged;
            public int verticesDifferentFromInstalled; public float maximumWorldYDifferenceFromInstalled;
            public string[] restoreErrors;
            public string scope = "Temporary authored-ridge mountain trial rebuilt from receipt-original geometry and exact pre-install placement matrices. Existing saved materials and art geography field retained. Current NavMesh is stale during trial; no Play or traversal claim. No persistent install.";
        }
        sealed class SpurMesh { public MeshFilter filter; public MeshCollider collider; public Mesh installed, original; public Matrix4x4 matrix; }
        sealed class SpurReference { public Accessor access; public Object installed, original; }
        sealed class SpurEarlyBinding { public EarlyRegionFoliage target; public EarlyRegionFoliage.Packet[] installed, original; public string installedHash; }
        sealed class SpurPlant { public Transform target; public Vector3 installed, original; }
        sealed class SpurState
        {
            public SpurManifest manifest; public SouthernSpurTrialReceipt receipt; public Scene scene;
            public string materials; public double expires; public bool restoring;
            public readonly List<SpurMesh> meshes = new List<SpurMesh>();
            public readonly List<SpurReference> references = new List<SpurReference>();
            public readonly List<SpurEarlyBinding> early = new List<SpurEarlyBinding>();
            public readonly List<SpurPlant> plants = new List<SpurPlant>();
        }
        static SpurState spurTrial;
        public static bool IsSouthernSpurTrialActive => spurTrial != null;
        public static SouthernSpurTrialReceipt LastSouthernSpurTrial { get; private set; }

        public static string BeginSouthernSpurTrial(string manifestPath)
        {
            Guard(true);
            if (spurTrial != null) throw new InvalidOperationException("Restore the existing southern-spur source view first.");
            string path = Resolve(manifestPath);
            var manifest = JsonUtility.FromJson<SpurManifest>(File.ReadAllText(path));
            if (manifest == null || manifest.schemaVersion != 1) throw new InvalidOperationException("Invalid staged source-view manifest.");
            CheckFile(manifest.applicationPath, manifest.applicationSha256); CheckFile(manifest.exportPath, manifest.exportSha256);
            CheckFile(manifest.navigationPath, manifest.navigationSha256); CheckFile(manifest.backupPath, manifest.backupSha256);
            CheckFile(AbsoluteAsset(CompactScene), manifest.sceneSha256);
            var application = JsonUtility.FromJson<ApplicationReceipt>(File.ReadAllText(manifest.applicationPath));
            var export = JsonUtility.FromJson<InkPaintingMountainForm.MountainExportReceipt>(File.ReadAllText(manifest.exportPath));
            var nav = JsonUtility.FromJson<SpurNavigation>(File.ReadAllText(manifest.navigationPath));
            if (nav == null || !nav.installed || !nav.sceneSaved || nav.next != 20 || string.IsNullOrEmpty(nav.generation) ||
                nav.geometryGeneration != manifest.generation || nav.sceneAfterSha256 != manifest.sceneSha256)
                throw new InvalidOperationException("Installed navigation/current scene provenance differs.");
            if (application.status != "APPLIED_GEOMETRY_SAVED" || !application.applied || !application.sceneSaved ||
                application.generation != manifest.generation || export.generation != manifest.generation ||
                application.exportReceiptSha256 != manifest.exportSha256 || application.sceneBeforeSha256 != manifest.backupSha256 ||
                export.status != "complete_assets_only_not_installed" || !export.sourceMeshesUnchanged || !export.sourceBindingsUnchanged)
                throw new InvalidOperationException("Source application/export/backup provenance mismatch.");
            var originalSettings = JsonUtility.FromJson<InkPaintingMountainForm.Receipt>(export.preparationReceiptJson).settings;
            ValidateSpurSettings(originalSettings, manifest.settings, manifest.variant);
            if (manifest.variant == "southern-toe-v3") InkPaintingMountainForm.ValidateToeSettings(manifest.toe);
            else if (manifest.toe != null) throw new InvalidOperationException("Unexpected toe option on a ridge-control trial.");
            var s = new SpurState { manifest = manifest, scene = SceneManager.GetActiveScene(), materials = SpurMaterialBindings(SceneManager.GetActiveScene()) };
            string output = Path.Combine(Workspace, "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/SouthernSpurTrial", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0,8));
            s.receipt = new SouthernSpurTrialReceipt { status = "VALIDATING_SOURCE_VIEW", utc = DateTime.UtcNow.ToString("O"),
                manifestPath = path, manifestSha256 = FileHash(path), installedGeneration = manifest.generation, variant = manifest.variant ?? "three-controls-v1",
                installedSceneSha256 = manifest.sceneSha256, output = output };
            LastSouthernSpurTrial = s.receipt;
            foreach (var row in export.meshes)
            {
                var filter = Components<MeshFilter>(s.scene).Single(f => Hierarchy(f.transform) == row.hierarchy);
                var original = LoadSourceMesh(row); var installed = row.changed ? LoadClean<Mesh>(row.derivedPath) : original;
                var collider = filter.GetComponent<MeshCollider>();
                if (filter.sharedMesh != installed || filter.transform.localToWorldMatrix != row.localToWorld ||
                    (row.collider && (collider == null || collider.sharedMesh != installed)) ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(original) != row.sourceMeshBeforeSha256 ||
                    InkPaintingMountainForm.ExportedMeshFingerprint(installed) != (row.changed ? row.exportedMeshSha256 : row.sourceMeshBeforeSha256))
                    throw new InvalidOperationException("Installed/source mesh differs from receipt: " + row.hierarchy);
                s.meshes.Add(new SpurMesh { filter = filter, collider = row.collider ? collider : null, original = original, installed = installed, matrix = row.localToWorld });
            }
            s.receipt.sourceMeshesFromReceipt = true;
            foreach (var mapping in application.assets)
            {
                var before = LoadClean<ScriptableObject>(mapping.source); var after = LoadClean<ScriptableObject>(mapping.derived);
                if (JsonHash(before) != mapping.sourceJsonSha256 || JsonHash(after) != mapping.derivedJsonSha256)
                    throw new InvalidOperationException("Original/installed SO payload changed: " + mapping.source);
            }
            foreach (var row in application.references)
            {
                var component = ResolveComponent(row.objectId, row.hierarchy, row.component); var access = new Accessor(component, row.property);
                var before = LoadClean<ScriptableObject>(row.source); var after = LoadClean<ScriptableObject>(row.derived);
                if (access.Get() as Object != after || AssetKey(before) != AssetDatabase.AssetPathToGUID(row.source) + ":" + row.sourceLocalId)
                    throw new InvalidOperationException("SO reference identity changed: " + row.hierarchy + "/" + row.property);
                s.references.Add(new SpurReference { access = access, original = before, installed = after });
            }
            s.receipt.sourceSOsFromReceipt = true;
            var expectedEarly = application.earlyFoliage.Select(v=>v.hierarchy).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            var manifestEarly = (manifest.early??Array.Empty<SpurEarly>()).Select(v=>v.hierarchy).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            if (manifestEarly.Distinct(StringComparer.Ordinal).Count()!=manifestEarly.Length || !manifestEarly.SequenceEqual(expectedEarly))
                throw new InvalidOperationException("Original foliage inventory must match every receipt hierarchy exactly once.");
            foreach (var row in manifest.early)
            {
                var proof = application.earlyFoliage.Single(v => v.hierarchy == row.hierarchy);
                var target = Components<EarlyRegionFoliage>(s.scene).Single(v => Hierarchy(v.transform) == row.hierarchy);
                var packets = DecodeSpurPackets(row.packets);
                if (row.beforeSha256 != proof.beforeSha256 || row.afterSha256 != proof.afterSha256 ||
                    PacketSignature(packets) != proof.beforeSha256 || PacketSignature(target.Packets) != proof.afterSha256)
                    throw new InvalidOperationException("Exact before/after foliage matrix proof differs: " + row.hierarchy);
                s.early.Add(new SpurEarlyBinding { target = target, original = packets, installed = target.Packets, installedHash = proof.afterSha256 });
            }
            if (s.early.Count != application.earlyFoliage.Length) throw new InvalidOperationException("Incomplete original foliage inventory.");
            s.receipt.exactOriginalEarlyMatrices = true;
            foreach (var row in export.staticRoots.Where(v => v.hasPreparedBinding))
            {
                var target = Components<Transform>(s.scene).Single(v => Hierarchy(v) == row.hierarchy);
                if (!target.localPosition.Equals(row.plannedLocal) || !target.position.Equals(row.plannedWorld))
                    throw new InvalidOperationException("Installed static root differs: " + row.hierarchy);
                s.plants.Add(new SpurPlant { target = target, original = row.originalLocal, installed = target.localPosition });
            }
            s.receipt.sourceStaticRootsFromReceipt = true;
            Directory.CreateDirectory(output);
            File.Copy(Path.Combine(Workspace,"Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/views.json"), Path.Combine(output,"views.json"));
            spurTrial = s; SubscribeSpur(); s.expires = EditorApplication.timeSinceStartup + 240;
            try
            {
                foreach (var m in s.meshes) { m.filter.sharedMesh = m.original; if (m.collider != null) m.collider.sharedMesh = m.original; }
                foreach (var r in s.references) r.access.Set(r.original);
                foreach (var p in s.plants) p.target.localPosition = p.original;
                foreach (var e in s.early) e.target.Packets = e.original;
                foreach (var renderer in Components<WorldMacroDressingRenderer>(s.scene)) renderer.ResetCache();
                Physics.SyncTransforms();
                s.receipt.preparationJson = manifest.variant == "southern-toe-v3"
                    ? InkPaintingMountainForm.PrepareToe(manifest.settings, manifest.toe)
                    : InkPaintingMountainForm.Prepare(manifest.settings);
                var prepared = InkPaintingMountainForm.LastReceipt;
                if (!prepared.protectedFacesExact || !prepared.foliageSupportWithinTolerance || prepared.supportMissing != 0)
                    throw new InvalidOperationException("Source-based trial failed protected geometry/exact foliage support checks.");
                File.WriteAllText(Path.Combine(output,"preparation.json"), s.receipt.preparationJson);
                InkPaintingMountainForm.BeginPreview();
                foreach (var m in s.meshes)
                {
                    var previous=m.installed.vertices; var trial=m.filter.sharedMesh.vertices;
                    if (previous.Length!=trial.Length) throw new InvalidOperationException("Trial topology differs from installed geometry.");
                    for(int i=0;i<trial.Length;i++) if (!previous[i].Equals(trial[i]))
                    { s.receipt.verticesDifferentFromInstalled++; s.receipt.maximumWorldYDifferenceFromInstalled=Mathf.Max(s.receipt.maximumWorldYDifferenceFromInstalled,Mathf.Abs(m.matrix.MultiplyVector(trial[i]-previous[i]).y)); }
                }
                s.receipt.status = "SOURCE_BASED_PREVIEW_ACTIVE_NOT_SAVED"; WriteSpur(s);
                return JsonUtility.ToJson(s.receipt,true);
            }
            catch (Exception error)
            {
                s.receipt.error = error.ToString();
                try { RestoreSouthernSpurTrial(); } catch (Exception restore) { throw new AggregateException(error,restore); }
                throw;
            }
        }

        public static string CaptureSouthernSpurTrial(string view)
        {
            var s = RequireSpur(); s.expires = EditorApplication.timeSinceStartup + 240;
            try { var result = CompactInkLandscapeAuthoring.CaptureReviewAt("trial:" + view,s.receipt.output); s.receipt.capturePath = result; WriteSpur(s); return result; }
            catch { RestoreSouthernSpurTrial(); throw; }
        }
        public static string AuditSouthernSpurTrial()
        {
            var s = RequireSpur();
            try
            {
                string physics = InkPaintingMountainForm.AuditPhysicalSupport();
                File.WriteAllText(Path.Combine(s.receipt.output,"physics.json"),physics);
                string normals = InkPaintingMountainForm.AuditPhysicalNormals(physics);
                File.WriteAllText(Path.Combine(s.receipt.output,"normals.json"),normals);
                if (!InkPaintingMountainForm.LastPhysicalAudit.preservationPass || !InkPaintingMountainForm.LastNormalAudit.normalPreservationResolved)
                    throw new InvalidOperationException("Trial preservation audit failed; inspect its unchanged failure record.");
                s.receipt.status = "PREVIEW_AUDITED_NOT_ART_APPROVED"; WriteSpur(s);
            }
            finally { RestoreSouthernSpurTrial(); }
            return JsonUtility.ToJson(s.receipt,true);
        }
        public static string ExportSouthernSpurTrial()
        {
            var s = RequireSpur();
            try
            {
                if (s.manifest.variant == "southern-toe-v3") throw new InvalidOperationException("Toe trial supports preview, physics and restore only; export is blocked.");
                InkPaintingMountainForm.EndPreview();
                string result = InkPaintingMountainForm.ExportPrepared("Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision/MountainForms", Path.Combine(s.receipt.output,"Export"));
                s.receipt.exportReceiptPath = InkPaintingMountainForm.LastExportReceipt.completeReceiptPath;
                InkPaintingMountainForm.AuditAgainstExport(s.receipt.exportReceiptPath);
                var audit = InkPaintingMountainForm.LastExportBoundAudit;
                File.WriteAllText(Path.Combine(s.receipt.output,"export_physics.json"),audit.physicalJson);
                File.WriteAllText(Path.Combine(s.receipt.output,"export_normals.json"),audit.normalJson);
                s.receipt.status = "EXPORTED_SOURCE_BASED_CANDIDATE_NOT_INSTALLED"; WriteSpur(s); return result;
            }
            finally { RestoreSouthernSpurTrial(); }
        }
        public static string RestoreSouthernSpurTrial()
        {
            var s = spurTrial; if (s == null) return "Southern-spur trial already restored.";
            if (s.restoring) throw new InvalidOperationException("Southern-spur restoration is already running.");
            s.restoring=true;
            try
            {
            var errors = new List<string>();
            void Attempt(Action action) { try { action(); } catch (Exception e) { errors.Add(e.ToString()); } }
            Attempt(() => InkPaintingMountainForm.EndPreview());
            if (InkPaintingMountainForm.IsActive) { s.receipt.restoreErrors = errors.ToArray(); WriteSpur(s); throw new InvalidOperationException("Inner terrain restoration failed; references retained for retry."); }
            foreach (var m in s.meshes) { Attempt(() => m.filter.sharedMesh = m.installed); if (m.collider != null) Attempt(() => m.collider.sharedMesh = m.installed); }
            foreach (var r in s.references) Attempt(() => r.access.Set(r.installed));
            foreach (var p in s.plants) Attempt(() => p.target.localPosition = p.installed);
            foreach (var e in s.early) Attempt(() => e.target.Packets = e.installed);
            foreach (var renderer in Components<WorldMacroDressingRenderer>(s.scene)) Attempt(renderer.ResetCache);
            Attempt(Physics.SyncTransforms);
            Attempt(() => {
                if (s.meshes.Any(m => m.filter.sharedMesh != m.installed || m.filter.transform.localToWorldMatrix != m.matrix || (m.collider != null && m.collider.sharedMesh != m.installed)) ||
                    s.references.Any(r => r.access.Get() as Object != r.installed) || s.plants.Any(p => p.target.localPosition != p.installed) ||
                    s.early.Any(e => e.target.Packets != e.installed || PacketSignature(e.target.Packets) != e.installedHash)) throw new InvalidOperationException("Installed binding restoration mismatch.");
                s.receipt.installedBindingsRestored = true;
                s.receipt.sceneFileUnchanged = FileHash(AbsoluteAsset(CompactScene)) == s.manifest.sceneSha256 && !s.scene.isDirty;
                s.receipt.navigationUnchanged = FileHash(s.manifest.navigationPath) == s.manifest.navigationSha256;
                s.receipt.materialBindingsUnchanged = SpurMaterialBindings(s.scene) == s.materials;
                if (!s.receipt.sceneFileUnchanged || !s.receipt.navigationUnchanged || !s.receipt.materialBindingsUnchanged) throw new InvalidOperationException("Saved scene, navigation or material bindings changed during trial.");
            });
            s.receipt.restoreErrors = errors.ToArray();
            if (errors.Count > 0) { s.receipt.status = "RESTORE_FAILED_RETRY_AVAILABLE"; WriteSpur(s); throw new InvalidOperationException(string.Join("\n",errors)); }
            s.receipt.status += "_INSTALLED_STATE_RESTORED"; WriteSpur(s);
            InkPaintingMountainForm.Discard(); UnsubscribeSpur(); spurTrial = null;
            return JsonUtility.ToJson(s.receipt,true);
            }
            finally { s.restoring=false; }
        }

        static SpurState RequireSpur()
        {
            var s = spurTrial ?? throw new InvalidOperationException("Begin a source-based trial first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene() != s.scene || !InkPaintingMountainForm.IsActive)
                throw new InvalidOperationException("Active Edit-only mountain trial required; no Play on stale navigation.");
            return s;
        }
        static void ValidateSpurSettings(InkPaintingMountainForm.Settings original, InkPaintingMountainForm.Settings candidate, string variant)
        {
            if (variant == "southern-toe-v3")
            {
                if (original == null || candidate == null || original.Controls.Length != 12 || JsonUtility.ToJson(original) != JsonUtility.ToJson(candidate))
                    throw new InvalidOperationException("Toe must use exactly the receipt-original 12 controls/settings; prior trial controls cannot accumulate.");
                return;
            }
            bool shoulder = variant == "broad-authored-shoulder-v2";
            if (!shoulder && !string.IsNullOrEmpty(variant) && variant != "three-controls-v1") throw new InvalidOperationException("Unknown staged mountain candidate.");
            int extra = shoulder ? 2 : 3;
            if (original == null || candidate == null || candidate.Controls == null || candidate.Controls.Length != original.Controls.Length + extra)
                throw new InvalidOperationException("Unexpected number of additional ridge controls.");
            var clone = JsonUtility.FromJson<InkPaintingMountainForm.Settings>(JsonUtility.ToJson(candidate)); clone.Controls = original.Controls;
            if (JsonUtility.ToJson(clone) != JsonUtility.ToJson(original)) throw new InvalidOperationException("Existing settings/protection/delta limit must remain identical.");
            for (int i=0;i<original.Controls.Length;i++) if (JsonUtility.ToJson(original.Controls[i]) != JsonUtility.ToJson(candidate.Controls[i])) throw new InvalidOperationException("Existing ridge controls changed.");
            var expected = shoulder
                ? new[] {new Vector4(.45f,78,240,260),new Vector4(.835f,-22,180,230)}
                : new[] {new Vector4(.83f,55,95,150),new Vector4(.91f,-25,80,135),new Vector4(.98f,45,95,150)};
            for (int i=0;i<extra;i++)
            {
                var c=candidate.Controls[original.Controls.Length+i]; var x=expected[i];
                string ridge=shoulder && i==0 ? "CapitalEasternFoothills" : "EasternWestSpur";
                float amplitude=c.gain*(c.saddle?-candidate.SaddleAmplitude:candidate.PeakAmplitude);
                if (c.ridge!=ridge || Mathf.Abs(c.along-x.x)>.000001f || Mathf.Abs(amplitude-x.y)>.0001f || c.alongRadius!=x.z || c.crossRadius!=x.w)
                    throw new InvalidOperationException("Unexpected southern-spur candidate.");
            }
        }
        static EarlyRegionFoliage.Packet[] DecodeSpurPackets(SpurPacket[] packets) => packets.Select(p => new EarlyRegionFoliage.Packet {
            Bounds=new Bounds(p.Bounds.m_Center,p.Bounds.m_Extent*2), Count=p.Count, Distance=p.Distance,
            Near=DecodeSpurParts(p.Near), Far=DecodeSpurParts(p.Far) }).ToArray();
        static EarlyRegionFoliage.Part[] DecodeSpurParts(SpurPart[] parts) => parts.Select(p => new EarlyRegionFoliage.Part {
            Mesh=SpurLoad<Mesh>(p.Mesh), Material=SpurLoad<Material>(p.Material), Submesh=p.Submesh, Matrices=p.Matrices }).ToArray();
        static T SpurLoad<T>(SpurAsset reference) where T:Object
        {
            if (reference == null || reference.fileID==0) return null;
            var asset=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(reference.guid)).OfType<T>().Single(v => AssetKey(v)==reference.guid+":"+reference.fileID); Clean(asset);return asset;
        }
        static string SpurMaterialBindings(Scene scene) => Digest(writer => {
            foreach (var renderer in Components<Renderer>(scene))
            {
                writer.Write(Hierarchy(renderer.transform));
                foreach(var material in renderer.sharedMaterials)
                    writer.Write(material == null || EditorUtility.IsPersistent(material) ? AssetKey(material) : "existing-transient:"+material.GetInstanceID());
            }
        });
        static void WriteSpur(SpurState s) => File.WriteAllText(Path.Combine(s.receipt.output,"trial_receipt.json"),JsonUtility.ToJson(s.receipt,true));
        static void SubscribeSpur() { EditorApplication.update+=TickSpur; AssemblyReloadEvents.beforeAssemblyReload+=BeforeSpurReload; EditorApplication.quitting+=BeforeSpurReload; EditorSceneManager.sceneSaving+=BeforeSpurSave; EditorSceneManager.sceneClosing+=BeforeSpurClose; EditorApplication.playModeStateChanged+=BeforeSpurPlay; }
        static void UnsubscribeSpur() { EditorApplication.update-=TickSpur; AssemblyReloadEvents.beforeAssemblyReload-=BeforeSpurReload; EditorApplication.quitting-=BeforeSpurReload; EditorSceneManager.sceneSaving-=BeforeSpurSave; EditorSceneManager.sceneClosing-=BeforeSpurClose; EditorApplication.playModeStateChanged-=BeforeSpurPlay; }
        static void TickSpur() { if (spurTrial!=null && EditorApplication.timeSinceStartup>spurTrial.expires) RestoreSouthernSpurTrial(); }
        static void BeforeSpurReload() => RestoreSouthernSpurTrial();
        static void BeforeSpurSave(Scene scene,string path) { if (spurTrial!=null) RestoreSouthernSpurTrial(); }
        static void BeforeSpurClose(Scene scene,bool removing) { if (spurTrial!=null) RestoreSouthernSpurTrial(); }
        static void BeforeSpurPlay(PlayModeStateChange change) { if (change==PlayModeStateChange.ExitingEditMode && spurTrial!=null) { EditorApplication.isPlaying=false; RestoreSouthernSpurTrial(); } }
    }
}
