using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Receipt-gated rebake of the CURRENT compact 20/28 navigation inventory. Never remaps volumes or endpoints.</summary>
    public static class InkPaintingNavigationUpdate
    {
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string Compact = "Assets/_Project/Art/World/WorldCompact";
        const string TerrainRoot = "WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/";
        static string Workspace => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string Output => Path.Combine(Workspace, "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/NavigationUpdate");
        static string ProgressPath => Path.Combine(Output, "progress.json");

        // Installer contract: generated only after actual geometry bindings, exact
        // height consumers, preservation checks and the clean scene save succeeded.
        [Serializable] public sealed class GeometryApplicationReceipt
        {
            public int schemaVersion = 1;
            public string status, generation, scene, sceneAfterSha256;
            public string exportReceiptPath, exportReceiptSha256, physicsReceiptPath, physicsReceiptSha256, normalReceiptPath, normalReceiptSha256;
            public bool sceneSaved, sourceFilesUnchanged, protectedGeometryPreserved, exactSurfaceBindingsVerified;
        }
        [Serializable] public sealed class FileStamp { public string path, sha256; }
        [Serializable] public sealed class MeshStamp
        {
            public string hierarchy, component, asset, guid, dependencyHash, builtinPayloadHash; public long localId;
            public Matrix4x4 matrix; public bool active; public int layer;
        }
        [Serializable] public sealed class SurfaceSettings
        {
            public string path; public Matrix4x4 matrix; public bool enabled, active;
            public int agentType, collectObjects, useGeometry, layerMask, defaultArea, tileSize;
            public Vector3 center, size; public float voxelSize, minRegionArea, agentRadius, agentHeight, agentSlope, agentClimb;
            public bool overrideTileSize, overrideVoxelSize, ignoreAgents, ignoreObstacles, buildHeightMesh;
        }
        [Serializable] public sealed class SurfaceRow
        {
            public SurfaceSettings settings; public string sourceData, sourceSha256, targetData, targetSha256;
            public bool built; public double buildMilliseconds;
        }
        [Serializable] public sealed class LinkRow
        {
            public string path, startTransform, endTransform; public Matrix4x4 matrix;
            public Vector3 startPoint, endPoint, startWorld, endWorld;
            public bool enabled, active, activated, bidirectional, autoUpdate;
            public int agentType, area; public float width, costModifier;
        }
        [Serializable] public sealed class Progress
        {
            public int schemaVersion = 1, next;
            public string status, generation, geometryGeneration, scene, applicationReceiptPath, applicationReceiptSha256;
            public string sceneBeforeSha256, sceneAfterSha256, sceneBackup, meshSignature, error, auditSha256;
            public bool originalNavigationUnchanged, settingsUnchanged, linksUnchanged, installed, sceneSaved;
            public float commitRatio; public SurfaceRow[] surfaces; public LinkRow[] links; public MeshStamp[] meshes; public FileStamp[] inputs;
            public string[] notes;
        }
        [Serializable] sealed class GradeGate
        { public string phase = null, fitHash = null, profileHash = null, reliefHash = null; public int profileVersion = 0, nextTerrain = 0, retainedTerrain = 0; public string[] terrain = null; public GradeTerrain[] rebuilt = null; }
        [Serializable] sealed class GradeTerrain { public string path = null, target = null; }
        [Serializable] sealed class StatusGate { public string status = null; public string[] failures = null; }
        [Serializable] public sealed class PathCheck
        { public string kind, id; public bool applicable, passed; public int points; public string finding; }
        [Serializable] public sealed class PathComparison
        { public PathCheck before, after; public bool newFailure; }
        [Serializable] public sealed class NavigationAudit
        {
            public string generation, status, scope, error; public bool completed, bindingsRestored, inputPreserved;
            public int surfaces, links, actors, preexistingFailures, afterFailures, newFailures, submergedBefore, submergedAfter;
            public bool nativeWalkingVerified, vehicleTraversalVerified; public PathComparison[] comparisons;
        }

        static T[] Components<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static string AssetFile(string path)
        {
            string local = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            if (path.StartsWith("Packages/", StringComparison.Ordinal) && !File.Exists(local))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                if (package == null) throw new InvalidOperationException("Unresolved package asset: " + path);
                return Path.GetFullPath(Path.Combine(package.resolvedPath, path.Substring(("Packages/" + package.name + "/").Length)));
            }
            return local;
        }
        static bool BuiltinMesh(string path) => path == "Library/unity default resources" || path == "Resources/unity_builtin_extra";
        static string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Workspace, path));
        static string Hash(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Hex(sha.ComputeHash(stream)); }
        static string TextHash(string value) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        static string Persist(object value, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string json = JsonUtility.ToJson(value, true), temp = path + ".tmp";
            File.WriteAllText(temp, json); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); return json;
        }
        static void Guard(bool memory = false)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != ScenePath || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Stable, clean, saved compact Edit scene required.");
            if (InkPaintingMountainForm.IsActive) throw new InvalidOperationException("Restore the temporary mountain preview before navigation update.");
            if (Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(v => v.gameObject.scene.IsValid() && v.gameObject.scene != scene))
                throw new InvalidOperationException("Another loaded scene contains navigation surfaces.");
            if (memory && Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; no bake started.");
        }
        static T Find<T>(string path) where T : Component => Components<T>().Single(v => Hierarchy(v.transform) == path);
        static void Saved(Object value, string path)
        { if (value == null || string.IsNullOrEmpty(path) || !File.Exists(AssetFile(path)) || EditorUtility.IsDirty(value)) throw new InvalidOperationException("Saved unchanged asset required: " + path); }

        public static string Prepare(string applicationReceiptPath)
        {
            Guard(true);
            if (File.Exists(ProgressPath)) throw new InvalidOperationException("Navigation progress already exists; resume it or explicitly archive its entire generation before preparing another.");
            string receiptPath = Resolve(applicationReceiptPath); var gate = ReadApplication(receiptPath);
            if (Hash(AssetFile(ScenePath)) != gate.sceneAfterSha256) throw new InvalidOperationException("Current saved scene differs from the geometry installation receipt.");
            VerifyGeometry(gate);
            var surfaces = Components<NavMeshSurface>().OrderBy(v => Hierarchy(v.transform), StringComparer.Ordinal).ToArray();
            var links = Components<NavMeshLink>().OrderBy(v => Hierarchy(v.transform), StringComparer.Ordinal).ToArray();
            if (surfaces.Length != 20 || links.Length != 28) throw new InvalidOperationException("Current inventory must remain exactly 20 surfaces and 28 links.");
            string generation = "InkNav_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var p = new Progress { status = "PREPARED_ASSETS_ONLY", generation = generation, geometryGeneration = gate.generation, scene = ScenePath,
                applicationReceiptPath = receiptPath, applicationReceiptSha256 = Hash(receiptPath), sceneBeforeSha256 = gate.sceneAfterSha256, sceneAfterSha256 = gate.sceneAfterSha256,
                sceneBackup = Path.Combine(Output, generation, "scene_before_navigation.unity"), commitRatio = Prologue.PrologueAudit.CommitRatio() };
            p.surfaces = surfaces.Select((v, i) => {
                if (v.collectObjects != CollectObjects.Volume || v.useGeometry != NavMeshCollectGeometry.PhysicsColliders) throw new InvalidOperationException("Expected existing volume/physics collection: " + Hierarchy(v.transform));
                string path = AssetDatabase.GetAssetPath(v.navMeshData); Saved(v.navMeshData, path);
                return new SurfaceRow { settings = Snapshot(v), sourceData = path, sourceSha256 = Hash(AssetFile(path)), targetData = Compact + "/InkLandscape/InkPaintingStudy/FlowRevision/Navigation/" + generation + "/Surface_" + i.ToString("D2") + ".asset" };
            }).ToArray();
            p.links = links.Select(Snapshot).ToArray(); p.meshes = MeshInventory(); p.meshSignature = TextHash(JsonUtility.ToJson(new MeshRows { rows = p.meshes }));
            p.inputs = CaptureInputs(gate, receiptPath, p.meshes);
            p.originalNavigationUnchanged = p.settingsUnchanged = p.linksUnchanged = true;
            p.notes = new[] { "Current compact transforms, volume centres/sizes, voxel/tile/agent settings, modifiers, 28 link settings and actor positions are retained; no inverse mapping or second compression.",
                "Step exports one independent NavMeshData, restoring existing bindings in finally. Audit temporarily binds all 20 derivatives and restores them. Only explicit Install saves new bindings after the audit passes.",
                "Historical grade/fit/relief and ActsTerrain evidence are immutable ancestry, not rewritten as if they generated the new meshes. Export/application provenance and new physics/normal receipts certify the derivative transition.",
                "Existing physical findings and navigation failures remain visible. Sampled Edit paths do not verify native movement or the full escort journey." };
            Directory.CreateDirectory(Path.GetDirectoryName(p.sceneBackup)); File.Copy(AssetFile(ScenePath), p.sceneBackup, false);
            return Persist(p, ProgressPath);
        }
        [Serializable] sealed class MeshRows { public MeshStamp[] rows; }
        static Progress Load()
        {
            var p = JsonUtility.FromJson<Progress>(File.ReadAllText(ProgressPath));
            CheckProgressShape(p); return p;
        }
        static void CheckProgressShape(Progress p)
        {
            if (p == null || p.schemaVersion != 1 || p.scene != ScenePath || p.surfaces?.Length != 20 || p.links?.Length != 28) throw new InvalidOperationException("Invalid current navigation generation.");
            if (string.IsNullOrEmpty(p.generation) || p.generation.Any(c => !char.IsLetterOrDigit(c) && c != '_') || p.next < 0 || p.next > 20 || p.inputs == null || p.inputs.Length == 0)
                throw new InvalidOperationException("Invalid generation, step or input snapshot.");
            for (int i = 0; i < p.surfaces.Length; i++)
                if (p.surfaces[i].targetData != Compact + "/InkLandscape/InkPaintingStudy/FlowRevision/Navigation/" + p.generation + "/Surface_" + i.ToString("D2") + ".asset" || p.surfaces[i].built != (i < p.next))
                    throw new InvalidOperationException("Output ownership or completed-step inventory differs from the unique generation.");
        }
        static GeometryApplicationReceipt ReadApplication(string path)
        {
            var r = JsonUtility.FromJson<GeometryApplicationReceipt>(File.ReadAllText(path));
            RequireApplicationFlags(r);
            foreach (var stamp in new[] { new FileStamp { path = r.exportReceiptPath, sha256 = r.exportReceiptSha256 }, new FileStamp { path = r.physicsReceiptPath, sha256 = r.physicsReceiptSha256 }, new FileStamp { path = r.normalReceiptPath, sha256 = r.normalReceiptSha256 } })
                if (string.IsNullOrEmpty(stamp.path) || Hash(Resolve(stamp.path)) != stamp.sha256) throw new InvalidOperationException("Referenced geometry evidence changed: " + stamp.path);
            return r;
        }
        static void RequireApplicationFlags(GeometryApplicationReceipt r)
        {
            if (r == null || r.schemaVersion != 1 || r.status != "APPLIED_GEOMETRY_SAVED" || r.scene != ScenePath || string.IsNullOrEmpty(r.generation) ||
                !r.sceneSaved || !r.sourceFilesUnchanged || !r.protectedGeometryPreserved || !r.exactSurfaceBindingsVerified)
                throw new InvalidOperationException("Completed saved geometry application receipt required; an assets-only export is insufficient.");
        }
        static void VerifyGeometry(GeometryApplicationReceipt gate)
        {
            var export = JsonUtility.FromJson<InkPaintingMountainForm.MountainExportReceipt>(File.ReadAllText(Resolve(gate.exportReceiptPath)));
            if (export?.status != "complete_assets_only_not_installed" || !export.sourceFilesUnchanged || !export.sourceMeshesUnchanged || !export.preparedMeshesUnchanged || export.scene != ScenePath || export.generation != gate.generation)
                throw new InvalidOperationException("Export generation/source proof is incomplete or differs from application.");
            if (TextHash(export.preparationReceiptJson) != export.preparationReceiptSha256) throw new InvalidOperationException("Embedded preparation receipt hash differs from export.");
            var prep = JsonUtility.FromJson<InkPaintingMountainForm.Receipt>(export.preparationReceiptJson);
            string physicalJson = File.ReadAllText(Resolve(gate.physicsReceiptPath)), normalJson = File.ReadAllText(Resolve(gate.normalReceiptPath));
            var physics = JsonUtility.FromJson<InkPaintingMountainForm.PhysicalAuditReceipt>(physicalJson);
            var normals = JsonUtility.FromJson<InkPaintingMountainForm.NormalAuditReceipt>(normalJson);
            if (prep == null || !prep.protectedVerticesExact || !prep.protectedFacesExact || !prep.sourceXZAndTopologyPreserved || !prep.foliageSupportWithinTolerance ||
                physics == null || physics.scene != ScenePath || !physics.completed || !physics.preservationPass || !physics.colliderBindingsRestored ||
                normals == null || normals.scene != ScenePath || !normals.completed || !normals.normalPreservationResolved || !normals.colliderBindingsRestored || normals.physicalAuditUtc != physics.utc ||
                !InkPaintingMountainForm.AuditProvenanceMatchesExport(Resolve(gate.exportReceiptPath), export, physicalJson, normalJson))
                throw new InvalidOperationException("Matching preparation, physical and normal preservation proof required.");
            foreach (var row in export.meshes)
            {
                var filter = Find<MeshFilter>(row.hierarchy); string expected = row.changed ? row.derivedPath : row.sourcePath;
                if (AssetDatabase.GetAssetPath(filter.sharedMesh) != expected || filter.transform.localToWorldMatrix != row.localToWorld) throw new InvalidOperationException("Applied mesh/transform mismatch: " + row.hierarchy);
                Saved(filter.sharedMesh, expected);
                if (InkPaintingMountainForm.ExportedMeshFingerprint(filter.sharedMesh) != (row.changed ? row.exportedMeshSha256 : row.sourceMeshBeforeSha256)) throw new InvalidOperationException("Applied mesh payload changed: " + row.hierarchy);
                var source = AssetDatabase.LoadAllAssetsAtPath(row.sourcePath).OfType<Mesh>().Single(v => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(v, out string guid, out long id) && guid == row.sourceGuid && id == row.sourceLocalId);
                if (InkPaintingMountainForm.ExportedMeshFingerprint(source) != row.sourceMeshBeforeSha256) throw new InvalidOperationException("Original source mesh changed: " + row.sourcePath);
                var collider = filter.GetComponent<MeshCollider>();
                if (row.collider && (collider == null || !collider.enabled || collider.sharedMesh != filter.sharedMesh)) throw new InvalidOperationException("Actual terrain collider differs from applied render mesh: " + row.hierarchy);
            }
            var grade = AssetDatabase.LoadAssetAtPath<WorldMacroRoadGradeSO>(Compact + "/RoadGrade.asset"); grade.Validate();
            string root = Path.Combine(Workspace, "Art/World/WorldMacro/Compact"); var legacy = JsonUtility.FromJson<GradeGate>(File.ReadAllText(Path.Combine(root, "grade_progress.json")));
            if (legacy.phase != "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT" || legacy.profileVersion != WorldMacroRoadGradeSO.CurrentVersion || legacy.profileHash != Hash(AssetFile(Compact + "/RoadGrade.asset")) ||
                legacy.fitHash != Hash(Path.Combine(root, "route_grade_fit.json")) || legacy.reliefHash != Hash(AssetFile(Compact + "/Relief.asset")) || legacy.rebuilt?.Length != 88 || legacy.terrain == null || legacy.nextTerrain != legacy.terrain.Length || legacy.retainedTerrain + legacy.nextTerrain != 88)
                throw new InvalidOperationException("Historical completed grade/fit/relief proof no longer matches; it is never waived by mountain application.");
            var terrain = Components<MeshCollider>().Where(v => v.name.StartsWith("Terrain_", StringComparison.Ordinal) && Hierarchy(v.transform).StartsWith(TerrainRoot, StringComparison.Ordinal)).ToArray();
            if (terrain.Length != 88 || !terrain.Select(v => Hierarchy(v.transform)).OrderBy(v => v).SequenceEqual(legacy.rebuilt.Select(v => v.path).OrderBy(v => v))) throw new InvalidOperationException("The original 88 terrain identities are not preserved.");
            // Later ActsTerrain corrections already superseded some grade paths.
            // Require their authored validation rather than silently treating the old
            // grade path list as the current mesh inventory.
            var acts = JsonUtility.FromJson<StatusGate>(File.ReadAllText(Path.Combine(Workspace, "Art/Demo/ActsTerrain/terrain_navigation_audit.json")));
            if (acts.status != "PASS" || (acts.failures != null && acts.failures.Length != 0)) throw new InvalidOperationException("Existing ActsTerrain navigation ancestry has unresolved failures.");
        }

        static SurfaceSettings Snapshot(NavMeshSurface v)
        {
            var b = v.GetBuildSettings(); return new SurfaceSettings { path = Hierarchy(v.transform), matrix = v.transform.localToWorldMatrix, enabled = v.enabled, active = v.gameObject.activeInHierarchy,
                agentType = v.agentTypeID, collectObjects = (int)v.collectObjects, useGeometry = (int)v.useGeometry, layerMask = v.layerMask.value, defaultArea = v.defaultArea,
                center = v.center, size = v.size, tileSize = v.tileSize, voxelSize = v.voxelSize, minRegionArea = v.minRegionArea, overrideTileSize = v.overrideTileSize, overrideVoxelSize = v.overrideVoxelSize,
                ignoreAgents = v.ignoreNavMeshAgent, ignoreObstacles = v.ignoreNavMeshObstacle, buildHeightMesh = v.buildHeightMesh,
                agentRadius = b.agentRadius, agentHeight = b.agentHeight, agentSlope = b.agentSlope, agentClimb = b.agentClimb };
        }
        static LinkRow Snapshot(NavMeshLink v) => new LinkRow { path = Hierarchy(v.transform), matrix = v.transform.localToWorldMatrix, enabled = v.enabled, active = v.gameObject.activeInHierarchy,
            activated = v.activated, bidirectional = v.bidirectional, autoUpdate = v.autoUpdate, agentType = v.agentTypeID, area = v.area, width = v.width, costModifier = v.costModifier,
            startPoint = v.startPoint, endPoint = v.endPoint, startTransform = v.startTransform == null ? null : Hierarchy(v.startTransform), endTransform = v.endTransform == null ? null : Hierarchy(v.endTransform),
            startWorld = v.startTransform == null ? v.transform.TransformPoint(v.startPoint) : v.startTransform.position, endWorld = v.endTransform == null ? v.transform.TransformPoint(v.endPoint) : v.endTransform.position };
        static MeshStamp[] MeshInventory()
        {
            var rows = new List<MeshStamp>();
            foreach (var c in Components<Component>())
            {
                Mesh mesh = c is MeshFilter f ? f.sharedMesh : c is MeshCollider m ? m.sharedMesh : c is SkinnedMeshRenderer s ? s.sharedMesh : null;
                if (mesh == null) continue; string path = AssetDatabase.GetAssetPath(mesh); bool builtin = BuiltinMesh(path);
                if (!builtin) Saved(mesh, path);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id);
                rows.Add(new MeshStamp { hierarchy = Hierarchy(c.transform), component = c.GetType().Name, asset = path, guid = guid, localId = id,
                    dependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString(), builtinPayloadHash = builtin ? InkPaintingMountainForm.ExportedMeshFingerprint(mesh) : null,
                    matrix = c.transform.localToWorldMatrix, active = c.gameObject.activeInHierarchy, layer = c.gameObject.layer });
            }
            return rows.OrderBy(v => v.hierarchy, StringComparer.Ordinal).ThenBy(v => v.component, StringComparer.Ordinal).ThenBy(v => v.localId).ToArray();
        }
        static FileStamp[] CaptureInputs(GeometryApplicationReceipt gate, string receipt, MeshStamp[] meshes)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { receipt, Resolve(gate.exportReceiptPath), Resolve(gate.physicsReceiptPath), Resolve(gate.normalReceiptPath) };
            files.Add(AssetFile("ProjectSettings/NavMeshAreas.asset")); files.Add(AssetFile("Packages/packages-lock.json"));
            foreach (string path in new[] { "Compression.asset", "RoadGrade.asset", "Relief.asset" }) files.Add(AssetFile(Compact + "/" + path));
            foreach (string path in new[] { "grade_progress.json", "route_grade_fit.json", "navigation_progress.json", "navigation_audit.json" }) files.Add(Path.Combine(Workspace, "Art/World/WorldMacro/Compact", path));
            foreach (string path in new[] { "terrain_navigation.json", "terrain_navigation_audit.json", "preservation.json", "road_physics_after.json", "navigation_link_repairs.json" }) files.Add(Path.Combine(Workspace, "Art/Demo/ActsTerrain", path));
            foreach (var mesh in meshes.Where(v => !BuiltinMesh(v.asset))) { files.Add(AssetFile(mesh.asset)); files.Add(AssetFile(mesh.asset + ".meta")); }
            foreach (var surface in Components<NavMeshSurface>()) files.Add(AssetFile(AssetDatabase.GetAssetPath(surface.navMeshData) + ".meta"));
            var export = JsonUtility.FromJson<InkPaintingMountainForm.MountainExportReceipt>(File.ReadAllText(Resolve(gate.exportReceiptPath)));
            foreach (var proof in export.sourceFiles)
            {
                string path = Path.IsPathRooted(proof.path) ? Path.GetFullPath(proof.path) : AssetFile(proof.path);
                if (path == AssetFile(ScenePath)) continue;
                if (!proof.unchanged || Hash(path) != proof.beforeSha256) throw new InvalidOperationException("Export original file changed: " + path);
                files.Add(path);
            }
            return files.OrderBy(v => v, StringComparer.Ordinal).Select(v => new FileStamp { path = v, sha256 = Hash(v) }).ToArray();
        }
        static void VerifyInputs(Progress p, bool outputs)
        {
            if (Hash(p.applicationReceiptPath) != p.applicationReceiptSha256 || Hash(AssetFile(ScenePath)) != p.sceneAfterSha256) throw new InvalidOperationException("Application receipt or saved scene changed outside this navigation generation.");
            foreach (var stamp in p.inputs) if (Hash(stamp.path) != stamp.sha256) throw new InvalidOperationException("Navigation input changed: " + stamp.path);
            if (TextHash(JsonUtility.ToJson(new MeshRows { rows = MeshInventory() })) != p.meshSignature) throw new InvalidOperationException("Current mesh bindings, transforms, dependencies or active states changed.");
            if (Components<NavMeshSurface>().Length != 20 || Components<NavMeshLink>().Length != 28) throw new InvalidOperationException("Navigation inventory changed.");
            foreach (var row in p.surfaces)
            {
                var v = Find<NavMeshSurface>(row.settings.path);
                Saved(v.navMeshData, AssetDatabase.GetAssetPath(v.navMeshData));
                if (JsonUtility.ToJson(Snapshot(v)) != JsonUtility.ToJson(row.settings) || Hash(AssetFile(row.sourceData)) != row.sourceSha256) throw new InvalidOperationException("Surface settings or original data changed: " + row.settings.path);
                if (AssetDatabase.GetAssetPath(v.navMeshData) != (p.installed ? row.targetData : row.sourceData)) throw new InvalidOperationException("Unexpected navigation data binding: " + row.settings.path);
                if (outputs && row.built)
                {
                    Saved(AssetDatabase.LoadAssetAtPath<NavMeshData>(row.targetData), row.targetData);
                    if (Hash(AssetFile(row.targetData)) != row.targetSha256) throw new InvalidOperationException("Generated navigation data changed: " + row.targetData);
                }
            }
            foreach (var row in p.links) if (JsonUtility.ToJson(Snapshot(Find<NavMeshLink>(row.path))) != JsonUtility.ToJson(row)) throw new InvalidOperationException("Link setting/endpoint changed: " + row.path);
        }
        static void Folder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
        static void Bind(NavMeshSurface surface, NavMeshData data) { surface.RemoveData(); surface.navMeshData = data; if (surface.isActiveAndEnabled && data != null) surface.AddData(); }
        static List<string> Restore(Progress p)
        {
            var errors = new List<string>();
            foreach (var row in p.surfaces) try { Bind(Find<NavMeshSurface>(row.settings.path), AssetDatabase.LoadAssetAtPath<NavMeshData>(p.installed ? row.targetData : row.sourceData)); } catch (Exception e) { errors.Add(e.Message); }
            foreach (var link in Components<NavMeshLink>()) if (link.isActiveAndEnabled) try { link.UpdateLink(); } catch (Exception e) { errors.Add(e.Message); }
            return errors;
        }

        public static string Step()
        {
            Guard(true); var p = Load(); VerifyInputs(p, true);
            if (p.installed || p.next == 20) return Persist(p, ProgressPath);
            var row = p.surfaces[p.next];
            if (File.Exists(AssetFile(row.targetData))) throw new InvalidOperationException("Unreceipted generated asset exists. Inspect/quarantine it before resuming; it is never adopted by path alone.");
            p.status = "BUILDING_ONE_ASSET"; p.error = null; Persist(p, ProgressPath);
            var surface = Find<NavMeshSurface>(row.settings.path); var original = surface.navMeshData; NavMeshData generated = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                try
                {
                    Bind(surface, null); Physics.SyncTransforms(); surface.BuildNavMesh(); generated = surface.navMeshData;
                    if (generated == null || generated == original || AssetDatabase.Contains(generated)) throw new InvalidOperationException("Expected a new independent NavMeshData.");
                    Bind(surface, original);
                    Folder(Path.GetDirectoryName(row.targetData).Replace('\\', '/')); generated.name = surface.name + "_InkPainting";
                    AssetDatabase.CreateAsset(generated, row.targetData); AssetDatabase.SaveAssetIfDirty(generated);
                }
                finally { Bind(surface, original); }
                Guard(); VerifyInputs(p, true);
                row.targetSha256 = Hash(AssetFile(row.targetData)); row.buildMilliseconds = watch.Elapsed.TotalMilliseconds; row.built = true; p.next++;
                p.status = p.next == 20 ? "ALL_20_ASSETS_READY_FOR_AUDIT" : "PREPARED_ASSETS_ONLY";
                return Persist(p, ProgressPath);
            }
            catch (Exception e)
            {
                try { Bind(surface, original); } catch (Exception restore) { p.error = "Restoration: " + restore; }
                if (generated != null && !AssetDatabase.Contains(generated) && surface.navMeshData != generated) Object.DestroyImmediate(generated);
                p.status = "FAILED_STEP_ORPHANS_REQUIRE_REVIEW"; p.error = (p.error ?? "") + "\n" + e; Persist(p, ProgressPath); throw;
            }
        }

        static PathCheck[] QueryPaths(out int submerged)
        {
            var session = Components<WorldMacroPlaytestSession>().Single();
            if (session.Actors == null || session.Actors.Length != 37 || session.Actors.Any(a => a == null) || session.Actors.Select(a => a.Id).Distinct().Count() != 37) throw new InvalidOperationException("Expected all 37 existing actors.");
            var rows = new List<PathCheck>();
            foreach (var actor in session.Actors)
            {
                var agent = actor.GetComponent<NavMeshAgent>(); var row = new PathCheck { kind = "actor", id = actor.Id, applicable = true, passed = agent != null };
                if (agent != null)
                {
                    var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                    bool startFound = NavSample(actor.transform.position - Vector3.up * agent.baseOffset, filter, 2, out var start);
                    row.passed &= startFound && (session.Traversal == null || !session.Traversal.IsDeep(start.position)); Vector3 previous = start.position;
                    foreach (var point in actor.PatrolPoints ?? Array.Empty<Vector3>())
                    {
                        row.points++; bool found = NavSample(point - Vector3.up * agent.baseOffset, filter, 2, out var hit);
                        row.passed &= startFound && found && CompletePath(previous, hit.position, filter) && (session.Traversal == null || !session.Traversal.IsDeep(hit.position));
                        if (found) previous = hit.position;
                    }
                }
                if (!row.passed) row.finding = "Actor/patrol sample, path or dry-ground check failed (2m horizontal / 0.8m vertical).";
                rows.Add(row);
            }
            foreach (var link in Components<NavMeshLink>())
            {
                var snapshot = Snapshot(link); var row = new PathCheck { kind = "link", id = snapshot.path, applicable = link.isActiveAndEnabled && link.activated, points = 2, passed = true };
                if (row.applicable)
                {
                    var filter = new NavMeshQueryFilter { agentTypeID = link.agentTypeID, areaMask = NavMesh.AllAreas };
                    bool a = NavSample(snapshot.startWorld, filter, .6f, out var start), b = NavSample(snapshot.endWorld, filter, .6f, out var end);
                    var seam = link.GetComponent<DemoEscortNavigationSeam>();
                    row.passed = a && b && CompletePath(start.position, end.position, filter) && seam != null && seam.Matches(snapshot.startWorld, snapshot.endWorld) &&
                        (session.Traversal == null || (!session.Traversal.IsDeep(start.position) && !session.Traversal.IsDeep(end.position)));
                    if (!row.passed) row.finding = "Enabled link endpoint/path/seam/dry-ground check failed.";
                }
                else row.finding = "Existing disabled/inactive link preserved; not a connectivity pass.";
                rows.Add(row);
            }
            submerged = 0;
            if (session.Traversal == null) throw new InvalidOperationException("Existing traversal water query required for navigation auditing.");
            var triangles = NavMesh.CalculateTriangulation();
            for (int i = 0; i < triangles.indices.Length; i += 3)
                if (session.Traversal.IsDeep((triangles.vertices[triangles.indices[i]] + triangles.vertices[triangles.indices[i + 1]] + triangles.vertices[triangles.indices[i + 2]]) / 3)) submerged++;
            return rows.OrderBy(v => v.kind, StringComparer.Ordinal).ThenBy(v => v.id, StringComparer.Ordinal).ToArray();
        }
        static bool NavSample(Vector3 p, NavMeshQueryFilter filter, float radius, out NavMeshHit hit) => NavMesh.SamplePosition(p, out hit, radius, filter) && Mathf.Abs(hit.position.y - p.y) <= .8f;
        static bool CompletePath(Vector3 a, Vector3 b, NavMeshQueryFilter filter) { var path = new NavMeshPath(); return NavMesh.CalculatePath(a, b, filter, path) && path.status == NavMeshPathStatus.PathComplete; }
        static PathComparison[] ComparePaths(PathCheck[] before, PathCheck[] after)
        {
            if (!before.Select(v => v.kind + "/" + v.id + "/" + v.applicable + "/" + v.points).SequenceEqual(after.Select(v => v.kind + "/" + v.id + "/" + v.applicable + "/" + v.points)))
                throw new InvalidOperationException("Actor/patrol/link applicability or inventory changed during audit.");
            return before.Select((v, i) => new PathComparison { before = v, after = after[i], newFailure = v.applicable && v.passed && !after[i].passed }).ToArray();
        }

        public static string Audit()
        {
            Guard(true); var p = Load(); VerifyInputs(p, true);
            if (p.next != 20 || p.surfaces.Any(v => !v.built) || p.installed) throw new InvalidOperationException("All 20 independent assets must be built, with original bindings still installed.");
            var r = new NavigationAudit { generation = p.generation, status = "RUNNING", surfaces = 20, links = 28, actors = 37,
                scope = "Actual Edit NavMesh actor/patrol and enabled-link path queries before/after all 20 new data bindings; originals restored in finally. No native walking or vehicle journey claim." };
            try
            {
                var before = QueryPaths(out r.submergedBefore); PathCheck[] after;
                try
                {
                    foreach (var row in p.surfaces) Bind(Find<NavMeshSurface>(row.settings.path), AssetDatabase.LoadAssetAtPath<NavMeshData>(row.targetData));
                    foreach (var link in Components<NavMeshLink>()) if (link.isActiveAndEnabled) link.UpdateLink();
                    after = QueryPaths(out r.submergedAfter);
                }
                finally { var errors = Restore(p); r.bindingsRestored = errors.Count == 0; if (!r.bindingsRestored) throw new InvalidOperationException("Nav restore failed: " + string.Join(" | ", errors)); }
                Guard(); VerifyInputs(p, true); r.inputPreserved = true;
                r.comparisons = ComparePaths(before, after);
                r.preexistingFailures = before.Count(v => v.applicable && !v.passed); r.afterFailures = after.Count(v => v.applicable && !v.passed); r.newFailures = r.comparisons.Count(v => v.newFailure);
                r.completed = true; r.status = r.newFailures == 0 && r.submergedAfter <= r.submergedBefore ? "PASS_PRESERVED_EDIT_PATHS" : "NEW_NAVIGATION_FINDINGS";
            }
            catch (Exception e) { r.status = "FAILED_AUDIT"; r.error = e.ToString(); }
            string path = Path.Combine(Output, p.generation, "path_audit.json"); string json = Persist(r, path);
            p.auditSha256 = Hash(path); p.status = r.status == "PASS_PRESERVED_EDIT_PATHS" ? "AUDITED_READY_FOR_INSTALL" : "AUDIT_FINDINGS"; Persist(p, ProgressPath); return json;
        }

        public static string Install()
        {
            Guard(true); var p = Load(); VerifyInputs(p, true);
            if (p.installed) return Persist(p, ProgressPath);
            string auditPath = Path.Combine(Output, p.generation, "path_audit.json"); var audit = JsonUtility.FromJson<NavigationAudit>(File.ReadAllText(auditPath));
            if (p.status != "AUDITED_READY_FOR_INSTALL" || p.next != 20 || Hash(auditPath) != p.auditSha256 || audit.generation != p.generation || !audit.completed || !audit.inputPreserved || !audit.bindingsRestored || audit.status != "PASS_PRESERVED_EDIT_PATHS")
                throw new InvalidOperationException("Matching completed derivative navigation audit required.");
            p.status = "INSTALLING"; Persist(p, ProgressPath); bool saved = false;
            try
            {
                foreach (var row in p.surfaces) { var surface = Find<NavMeshSurface>(row.settings.path); Bind(surface, AssetDatabase.LoadAssetAtPath<NavMeshData>(row.targetData)); EditorUtility.SetDirty(surface); }
                foreach (var link in Components<NavMeshLink>()) if (link.isActiveAndEnabled) link.UpdateLink();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!SaveSceneWithAuthoredWater()) throw new IOException("Could not save installed navigation bindings.");
                saved = true; p.installed = p.sceneSaved = true; p.sceneAfterSha256 = Hash(AssetFile(ScenePath));
                Guard(); VerifyInputs(p, true); p.status = "INSTALLED_SAVED_EDIT_PATHS_VERIFIED"; return Persist(p, ProgressPath);
            }
            catch (Exception e)
            {
                p.installed = false; var rollback = Restore(p);
                if (saved) try { EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); if (!SaveSceneWithAuthoredWater()) rollback.Add("Could not save restored navigation bindings."); } catch (Exception restore) { rollback.Add(restore.ToString()); }
                p.sceneSaved = false; p.status = "INSTALL_FAILED_REVIEW_BACKUP"; p.error = e + "\nRollback: " + string.Join(" | ", rollback);
                Persist(p, ProgressPath); throw;
            }
        }
        static bool SaveSceneWithAuthoredWater()
        {
            var clocks = Components<WorldMacroWaterClock>();
            try { foreach (var clock in clocks) clock.RestoreAuthoredMaterial(); return EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); }
            finally { foreach (var clock in clocks) clock.Rebind(); }
        }
        /// <summary>Receipt-aware replacement at an explicitly selected new-generation caller; the historical guard remains unchanged.</summary>
        public static void RequireCurrentBake()
        {
            Guard(); var p = Load(); VerifyInputs(p, true);
            string auditPath = Path.Combine(Output, p.generation, "path_audit.json");
            if (!p.installed || !p.sceneSaved || p.status != "INSTALLED_SAVED_EDIT_PATHS_VERIFIED" || p.next != 20 || Hash(auditPath) != p.auditSha256)
                throw new InvalidOperationException("Current geometry generation has no installed, saved and audited 20-surface navigation bake.");
        }
    }
}
