using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    /// <summary>Resumable, one-surface-at-a-time navigation conversion for the compact scene only.</summary>
    public static partial class WorldMacroCompactNavigation
    {
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string Folder = "Assets/_Project/Art/World/WorldCompact";
        const string MappingPath = Folder + "/Compression.asset";
        const int ExpectedSurfaces = 20, ExpectedLinks = 28, ExpectedActors = 37;
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/World/WorldMacro/Compact"));
        static string ProgressPath => Path.Combine(Output, "navigation_progress.json");
        static WorldMacroCompressionMapSO Mapping => AssetDatabase.LoadAssetAtPath<WorldMacroCompressionMapSO>(MappingPath);

        [Serializable] sealed class SurfaceRecord
        {
            public string path, sourceData, sourceHash, targetData;
            public Vector3 oldRoot, newRoot, oldCenter, oldSize, newCenter, newSize;
            public bool baked;
        }
        [Serializable] sealed class LinkRecord
        {
            public string path;
            public Vector3 oldStart, oldEnd, newStart, newEnd;
            public bool externalStart, externalEnd, enabled;
        }
        [Serializable] sealed class Progress
        {
            public string status, generation, scene, mappingHash, gradeHash, fitHash, terrainSignature, error;
            public ReliefStamp relief;
            public string scope = "Compact Edit Mode navigation conversion only. One of the original 20 surfaces per Step; no route expansion or runtime completion claim.";
            public int next;
            public float commitRatio;
            public SurfaceRecord[] surfaces;
            public LinkRecord[] links;
        }
        [Serializable] sealed class ReliefStamp { public string path, assetHash, dataHash, weightHash; }
        [Serializable] sealed class TerrainRecord { public string path, target; }
        [Serializable] sealed class TerrainGate
        {
            public string phase, fitHash, profileHash, reliefHash;
            public int profileVersion, nextTerrain, retainedTerrain;
            public string[] terrain;
            public TerrainRecord[] rebuilt;
        }
        [Serializable] sealed class ActorCheck
        {
            public string id, finding;
            public Vector3 feet, sampled;
            public int patrolCount;
            public bool positionSampled, allPatrolSampled, completePatrol;
        }
        [Serializable] sealed class LinkCheck
        {
            public string path;
            public Vector3 start, end;
            public bool startSampled, endSampled, completePath, seamMatches;
        }
        [Serializable] sealed class AuditReport
        {
            public string status, scene;
            public string scope = "Read-only Edit Mode samples of all authored actors/patrols and existing link endpoints. This does not verify native movement, combat, the full escort journey, or missing route navigation.";
            public int surfaces, links, actors;
            public bool originalDataUnchanged, allSurfaceDataIsCompact, inventoryMatches;
            public string[] findings;
            public ActorCheck[] actorChecks;
            public LinkCheck[] linkChecks;
        }

        static IEnumerable<T> SceneComponents<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static T Find<T>(string path) where T : Component
        {
            var matches = SceneComponents<T>().Where(c => Hierarchy(c.transform) == path).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one " + typeof(T).Name + " at " + path + "; found " + matches.Length);
            return matches[0];
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static string AssetFile(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        static bool OwnedData(string path) => !string.IsNullOrEmpty(path) && path.StartsWith(Folder + "/Navigation/", StringComparison.Ordinal);
        static string Persist(Progress progress)
        {
            Directory.CreateDirectory(Output);
            string json = JsonUtility.ToJson(progress, true);
            File.WriteAllText(ProgressPath, json);
            return json;
        }
        static Progress Load()
        {
            if (!File.Exists(ProgressPath)) throw new InvalidOperationException("Prepare compact navigation first.");
            var p = JsonUtility.FromJson<Progress>(File.ReadAllText(ProgressPath));
            if (p == null || p.scene != ScenePath || p.surfaces == null || p.links == null || p.surfaces.Length != ExpectedSurfaces || p.links.Length != ExpectedLinks)
                throw new InvalidOperationException("Compact navigation progress is missing or has an incompatible inventory.");
            if (p.mappingHash != Hash(AssetFile(MappingPath))) throw new InvalidOperationException("Compression mapping changed since navigation preparation; do not resume against another mapping.");
            return p;
        }
        static void RequireCompact(bool memoryGuard)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Compiled compact scene in Edit Mode required.");
            if (Mapping == null || Mapping.Mapping == null) throw new InvalidOperationException("Compact Compression.asset is missing or uninitialized.");
            // Global NavMesh queries must not see another loaded world underneath the compact one.
            if (Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(s => s.gameObject.scene.IsValid() && s.gameObject.scene != SceneManager.GetActiveScene()))
                throw new InvalidOperationException("Unload other scenes containing NavMeshSurface before compact navigation work.");
            if (memoryGuard && Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; no navigation collection or bake was started.");
        }
        static void RequireUnitTransform(Transform t)
        {
            if (Quaternion.Angle(t.rotation, Quaternion.identity) > .001f || (t.lossyScale - Vector3.one).sqrMagnitude > .000001f)
                throw new InvalidOperationException("Expected audited identity navigation rotation/scale at " + Hierarchy(t));
        }
        static bool SourceHashesMatch(Progress p) => p.surfaces.All(s => File.Exists(AssetFile(s.sourceData)) && Hash(AssetFile(s.sourceData)) == s.sourceHash);
        static string ReliefFloatHash(float[] values)
        {
            // Hash the actual payload as Float32 little endian, not only its self-reported hash.
            if (!BitConverter.IsLittleEndian) throw new InvalidOperationException("Reviewed relief binaries require little-endian float payloads.");
            using var sha = SHA256.Create(); var buffer = new byte[65536];
            int byteCount = checked(values.Length * sizeof(float));
            for (int offset = 0; offset < byteCount; offset += buffer.Length)
            {
                int count = Math.Min(buffer.Length, byteCount - offset); Buffer.BlockCopy(values, offset, buffer, 0, count);
                sha.TransformBlock(buffer, 0, count, buffer, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
        static ReliefStamp CurrentReliefStamp(WorldMacroRoadGradeSO profile, string fitHash)
        {
            var relief = profile.Relief; if (relief == null) return null;
            string path = AssetDatabase.GetAssetPath(relief);
            if (path != Folder + "/Relief.asset" || EditorUtility.IsDirty(relief) || relief.MappingHash != Hash(AssetFile(MappingPath)) ||
                relief.SourceHash != Hash(AssetFile(WorldMacroCompactAuthoring.SourceScene)) || relief.FitHash != fitHash)
                throw new InvalidOperationException("Current private relief provenance differs from source/map/fit or is unsaved.");
            relief.Validate(); string dataHash = ReliefFloatHash(relief.TargetHeights), weightHash = ReliefFloatHash(relief.Weights);
            if (dataHash != relief.DataHash || weightHash != relief.WeightHash)
                throw new InvalidOperationException("Relief sample payload differs from its reviewed target/weight hashes.");
            return new ReliefStamp { path = path, assetHash = Hash(AssetFile(path)), dataHash = dataHash, weightHash = weightHash };
        }
        static string CurrentTerrainStamp(out string gradeHash, out string fitHash, out ReliefStamp relief)
        {
            string gatePath = Path.Combine(Output, "grade_progress.json"), fitPath = Path.Combine(Output, "route_grade_fit.json");
            var gate = JsonUtility.FromJson<TerrainGate>(File.ReadAllText(gatePath));
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroRoadGradeSO>(Folder + "/RoadGrade.asset");
            if (profile == null) throw new InvalidOperationException("Complete current terrain grade before navigation bake.");
            profile.Validate(); fitHash = Hash(fitPath); gradeHash = Hash(AssetFile(Folder + "/RoadGrade.asset"));
            relief = CurrentReliefStamp(profile, fitHash);
            if (gate == null || gate.phase != "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT" || gate.profileVersion != WorldMacroRoadGradeSO.CurrentVersion ||
                gate.fitHash != fitHash || gate.profileHash != gradeHash || (gate.reliefHash ?? "") != (relief?.assetHash ?? "") ||
                gate.terrain == null || gate.nextTerrain != gate.terrain.Length || gate.retainedTerrain < 0 ||
                gate.rebuilt == null || gate.rebuilt.Length != 88 || gate.retainedTerrain + gate.terrain.Length != 88 ||
                gate.rebuilt.Select(r => r.path).Distinct(StringComparer.Ordinal).Count() != 88 ||
                gate.terrain.Any(path => !gate.rebuilt.Any(r => r.path == path)))
                throw new InvalidOperationException("Navigation requires all 88 current terrain records: retained chunks plus the completed patch, with matching grade version/fit.");
            var terrain = SceneComponents<MeshCollider>().Where(c => c.name.StartsWith("Terrain_", StringComparison.Ordinal) &&
                Hierarchy(c.transform).StartsWith("WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/", StringComparison.Ordinal)).ToArray();
            if (terrain.Length != 88) throw new InvalidOperationException("Expected 88 actual terrain colliders before navigation bake.");
            var rows = new List<string> { relief == null ? "relief:none" : "relief:" + JsonUtility.ToJson(relief) };
            foreach (var record in gate.rebuilt.OrderBy(r => r.path, StringComparer.Ordinal))
            {
                var collider = terrain.SingleOrDefault(c => Hierarchy(c.transform) == record.path);
                if (collider == null || collider.sharedMesh == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger ||
                    !record.target.StartsWith(Folder + "/Meshes/", StringComparison.Ordinal) || AssetDatabase.GetAssetPath(collider.sharedMesh) != record.target || EditorUtility.IsDirty(collider.sharedMesh))
                    throw new InvalidOperationException("Saved current terrain collider is missing or differs from its grade record: " + record.path);
                rows.Add(record.path + "|" + record.target + "|" + AssetDatabase.GetAssetDependencyHash(record.target) + "|" + collider.transform.localToWorldMatrix.ToString("R"));
            }
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows)))).Replace("-", "").ToLowerInvariant();
        }
        static void MatchBakeInputs(Progress p, string terrainSignature, string gradeHash, string fitHash, ReliefStamp relief)
        {
            if (p.terrainSignature != terrainSignature || p.gradeHash != gradeHash || p.fitHash != fitHash ||
                (p.relief?.assetHash ?? "") != (relief?.assetHash ?? "") || (p.relief?.dataHash ?? "") != (relief?.dataHash ?? "") || (p.relief?.weightHash ?? "") != (relief?.weightHash ?? ""))
                throw new InvalidOperationException("Navigation bake belongs to another terrain/grade/fit; do not reuse or continue its baked outputs.");
        }
        public static void RequireCurrentBake()
        {
            RequireCompact(false); var p = Load();
            string stamp = CurrentTerrainStamp(out string gradeHash, out string fitHash, out var relief); MatchBakeInputs(p, stamp, gradeHash, fitHash, relief);
            if (p.status != "BAKED" || p.next != ExpectedSurfaces || p.surfaces.Any(s => !s.baked) || !SourceHashesMatch(p))
                throw new InvalidOperationException("All 20 current navigation surfaces and unchanged original data are required.");
        }
        static void SaveScene()
        {
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the compact scene.");
        }

        public static string Prepare()
        {
            RequireCompact(true);
            Progress p;
            if (File.Exists(ProgressPath))
            {
                p = Load();
                if (p.status != "PREPARING") return Persist(p);
            }
            else
            {
                var surfaces = SceneComponents<NavMeshSurface>().OrderBy(s => Hierarchy(s.transform), StringComparer.Ordinal).ToArray();
                var links = SceneComponents<NavMeshLink>().OrderBy(l => Hierarchy(l.transform), StringComparer.Ordinal).ToArray();
                if (surfaces.Length != ExpectedSurfaces || links.Length != ExpectedLinks) throw new InvalidOperationException("Expected exactly 20 source surfaces and 28 links; no partial preparation applied.");
                var map = Mapping;
                p = new Progress { status = "PREPARING", generation = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"), scene = ScenePath, mappingHash = Hash(AssetFile(MappingPath)), commitRatio = Prologue.PrologueAudit.CommitRatio() };
                var records = new List<SurfaceRecord>();
                foreach (var surface in surfaces)
                {
                    RequireUnitTransform(surface.transform);
                    if (surface.collectObjects != CollectObjects.Volume) throw new InvalidOperationException("Expected volume collection at " + Hierarchy(surface.transform));
                    string source = AssetDatabase.GetAssetPath(surface.navMeshData);
                    if (surface.navMeshData == null || string.IsNullOrEmpty(source) || OwnedData(source)) throw new InvalidOperationException("Expected saved original navigation data at " + Hierarchy(surface.transform));
                    Vector3 newRoot = surface.transform.position, oldRoot = map.Inverse(newRoot);
                    Vector3 oldCenter = oldRoot + surface.center;
                    Vector3 min = map.Map(oldCenter - surface.size * .5f), max = map.Map(oldCenter + surface.size * .5f);
                    float minimumWidth = Mathf.Max(2f, surface.GetBuildSettings().agentRadius * 2f + 1f);
                    Vector3 size = max - min;
                    size.x = Mathf.Max(minimumWidth, size.x); size.z = Mathf.Max(minimumWidth, size.z);
                    records.Add(new SurfaceRecord { path = Hierarchy(surface.transform), sourceData = source, sourceHash = Hash(AssetFile(source)),
                        targetData = Folder + "/Navigation/" + p.generation + "/Surface_" + records.Count.ToString("D2") + ".asset",
                        oldRoot = oldRoot, newRoot = newRoot, oldCenter = surface.center, oldSize = surface.size, newCenter = (min + max) * .5f - newRoot, newSize = size });
                }
                p.surfaces = records.ToArray();
                p.links = links.Select(link =>
                {
                    RequireUnitTransform(link.transform);
                    Vector3 oldRoot = map.Inverse(link.transform.position);
                    return new LinkRecord { path = Hierarchy(link.transform), oldStart = link.startPoint, oldEnd = link.endPoint, enabled = link.enabled,
                        externalStart = link.startTransform != null, externalEnd = link.endTransform != null,
                        newStart = link.startTransform != null ? link.startPoint : link.transform.InverseTransformPoint(map.Map(oldRoot + link.startPoint)),
                        newEnd = link.endTransform != null ? link.endPoint : link.transform.InverseTransformPoint(map.Map(oldRoot + link.endPoint)) };
                }).ToArray();
                // Persist the original values before any scene field is changed. PREPARING can be replayed safely.
                Persist(p);
            }
            if (!SourceHashesMatch(p)) throw new InvalidOperationException("Original NavMeshData changed since the preparation snapshot.");
            foreach (var record in p.surfaces)
            {
                var surface = Find<NavMeshSurface>(record.path);
                surface.RemoveData(); surface.navMeshData = null;
                surface.center = record.newCenter; surface.size = record.newSize;
                EditorUtility.SetDirty(surface);
            }
            foreach (var record in p.links)
            {
                var link = Find<NavMeshLink>(record.path);
                // The root remapper already mapped DemoEscortNavigationSeam.StartWorld/EndWorld.
                // Endpoint transforms, when configured, were also relocated by the caller.
                link.startPoint = record.newStart; link.endPoint = record.newEnd;
                link.enabled = false;
                EditorUtility.SetDirty(link);
            }
            Physics.SyncTransforms(); SaveScene();
            p.status = "PREPARED"; p.error = null;
            return Persist(p);
        }

        public static string Step()
        {
            RequireCompact(true);
            var p = Load();
            if (p.status == "PREPARING") throw new InvalidOperationException("Finish Prepare before Step.");
            string terrainStamp = CurrentTerrainStamp(out string currentGradeHash, out string currentFitHash, out var currentRelief);
            if (string.IsNullOrEmpty(p.terrainSignature) && p.next == 0 && p.surfaces.All(s => !s.baked) && p.surfaces.All(s => !File.Exists(AssetFile(s.targetData))))
            {
                p.terrainSignature = terrainStamp; p.gradeHash = currentGradeHash; p.fitHash = currentFitHash; p.relief = currentRelief; Persist(p);
            }
            MatchBakeInputs(p, terrainStamp, currentGradeHash, currentFitHash, currentRelief);
            if (p.next >= p.surfaces.Length) return Persist(p);
            if (!SourceHashesMatch(p)) throw new InvalidOperationException("Original NavMeshData changed; no further bake was started.");
            var record = p.surfaces[p.next];
            if (!OwnedData(record.targetData)) throw new InvalidOperationException("Navigation progress does not name a compact-only output path.");
            var surface = Find<NavMeshSurface>(record.path);
            p.status = "BAKING"; p.error = null; p.commitRatio = Prologue.PrologueAudit.CommitRatio(); Persist(p);
            NavMeshData generated = null;
            try
            {
                var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(record.targetData);
                surface.RemoveData(); surface.navMeshData = null;
                surface.center = record.newCenter; surface.size = record.newSize;
                if (saved == null)
                {
                    // Passing no existing data ensures BuildNavMesh cannot modify an original asset.
                    Physics.SyncTransforms(); surface.BuildNavMesh(); generated = surface.navMeshData;
                    if (generated == null) throw new InvalidOperationException("BuildNavMesh returned no data for " + record.path);
                    surface.RemoveData(); surface.navMeshData = null;
                    EnsureAssetFolder(Path.GetDirectoryName(record.targetData).Replace('\\', '/'));
                    generated.name = surface.name + "_Compact";
                    AssetDatabase.CreateAsset(generated, record.targetData);
                    AssetDatabase.SaveAssetIfDirty(generated);
                    saved = generated;
                }
                // Recovery after an interrupted save adopts only this generation's known output path.
                if (!OwnedData(AssetDatabase.GetAssetPath(saved))) throw new InvalidOperationException("Navigation output escaped the compact asset folder.");
                surface.navMeshData = saved;
                if (surface.isActiveAndEnabled) surface.AddData();
                EditorUtility.SetDirty(surface);
                if (p.next + 1 == p.surfaces.Length)
                {
                    foreach (var linkRecord in p.links)
                    {
                        var link = Find<NavMeshLink>(linkRecord.path);
                        link.enabled = linkRecord.enabled;
                        if (link.isActiveAndEnabled) link.UpdateLink();
                        EditorUtility.SetDirty(link);
                    }
                }
                SaveScene(); record.baked = true; p.next++;
                p.status = p.next == p.surfaces.Length ? "BAKED" : "PREPARED";
                return Persist(p);
            }
            catch (Exception exception)
            {
                if (generated != null && !AssetDatabase.Contains(generated))
                {
                    surface.RemoveData(); surface.navMeshData = null; Object.DestroyImmediate(generated);
                }
                p.status = "FINDINGS"; p.error = exception.ToString(); Persist(p);
                throw;
            }
        }

        static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureAssetFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static bool Sample(Vector3 feet, NavMeshQueryFilter filter, float radius, out NavMeshHit hit) =>
            NavMesh.SamplePosition(feet, out hit, radius, filter) && Mathf.Abs(hit.position.y - feet.y) <= .8f;
        static bool Complete(Vector3 a, Vector3 b, NavMeshQueryFilter filter)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, filter, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        public static string Audit()
        {
            RequireCompact(false);
            var p = Load();
            var findings = new List<string>();
            try { string stamp = CurrentTerrainStamp(out string gradeHash, out string fitHash, out var relief); MatchBakeInputs(p, stamp, gradeHash, fitHash, relief); }
            catch (InvalidOperationException exception) { findings.Add(exception.Message); }
            var surfaces = SceneComponents<NavMeshSurface>().ToArray();
            var links = SceneComponents<NavMeshLink>().ToArray();
            var sessions = SceneComponents<WorldMacroPlaytestSession>().ToArray();
            if (sessions.Length != 1 || sessions[0].Actors == null) throw new InvalidOperationException("Expected one compact playtest session with registered actors.");
            var actors = sessions[0].Actors;
            var report = new AuditReport { scene = ScenePath, surfaces = surfaces.Length, links = links.Length, actors = actors.Length,
                originalDataUnchanged = SourceHashesMatch(p), allSurfaceDataIsCompact = surfaces.All(s => s.navMeshData != null && OwnedData(AssetDatabase.GetAssetPath(s.navMeshData))),
                inventoryMatches = surfaces.Length == ExpectedSurfaces && links.Length == ExpectedLinks && actors.Length == ExpectedActors && actors.All(a => a != null) && actors.Where(a => a != null).Select(a => a.Id).Distinct().Count() == ExpectedActors };
            if (p.next != ExpectedSurfaces || p.surfaces.Any(s => !s.baked)) findings.Add("Not all 20 existing surfaces have completed their compact bake.");
            if (!report.originalDataUnchanged) findings.Add("Original navigation asset hash changed.");
            if (!report.allSurfaceDataIsCompact) findings.Add("One or more surfaces has missing or noncompact navigation data.");
            if (!report.inventoryMatches) findings.Add("Surface/link/actor inventory differs from the baseline 20/28/37.");
            foreach (var record in p.surfaces)
            {
                var surface = surfaces.FirstOrDefault(s => Hierarchy(s.transform) == record.path);
                if (surface == null || AssetDatabase.GetAssetPath(surface.navMeshData) != record.targetData ||
                    (surface.center - record.newCenter).sqrMagnitude > .0001f || (surface.size - record.newSize).sqrMagnitude > .0001f)
                    findings.Add("Surface output or mapped volume differs from the prepared record: " + record.path);
            }
            report.actorChecks = actors.Where(a => a != null).Select(actor =>
            {
                var row = new ActorCheck { id = actor.Id, allPatrolSampled = true, completePatrol = true, patrolCount = actor.PatrolPoints?.Length ?? 0 };
                var agent = actor.GetComponent<NavMeshAgent>();
                if (agent == null) { row.finding = "NavMeshAgent missing"; row.completePatrol = false; row.allPatrolSampled = false; return row; }
                var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                row.feet = actor.transform.position - Vector3.up * agent.baseOffset;
                row.positionSampled = Sample(row.feet, filter, 2f, out var start); row.sampled = row.positionSampled ? start.position : default;
                Vector3 previous = start.position;
                if (row.patrolCount == 0) row.allPatrolSampled = row.completePatrol = false;
                foreach (var patrol in actor.PatrolPoints ?? Array.Empty<Vector3>())
                {
                    bool sampled = Sample(patrol - Vector3.up * agent.baseOffset, filter, 2f, out var hit);
                    row.allPatrolSampled &= sampled;
                    row.completePatrol &= row.positionSampled && sampled && Complete(previous, hit.position, filter);
                    if (sampled) previous = hit.position;
                }
                if (!row.positionSampled || !row.allPatrolSampled || !row.completePatrol) row.finding = "Actor or patrol sample/path failed within the 2 m / 0.8 m vertical tolerance.";
                return row;
            }).ToArray();
            report.linkChecks = links.Select(link =>
            {
                var filter = new NavMeshQueryFilter { agentTypeID = link.agentTypeID, areaMask = NavMesh.AllAreas };
                var row = new LinkCheck { path = Hierarchy(link.transform), start = link.startTransform != null ? link.startTransform.position : link.transform.TransformPoint(link.startPoint),
                    end = link.endTransform != null ? link.endTransform.position : link.transform.TransformPoint(link.endPoint) };
                row.startSampled = Sample(row.start, filter, .6f, out var start); row.endSampled = Sample(row.end, filter, .6f, out var end);
                row.completePath = row.startSampled && row.endSampled && link.isActiveAndEnabled && link.activated && Complete(start.position, end.position, filter);
                var seam = link.GetComponent<DemoEscortNavigationSeam>(); row.seamMatches = seam != null && seam.Matches(row.start, row.end);
                return row;
            }).ToArray();
            findings.AddRange(report.actorChecks.Where(a => !a.positionSampled || !a.allPatrolSampled || !a.completePatrol).Select(a => "Actor: " + a.id + ": " + a.finding));
            findings.AddRange(report.linkChecks.Where(l => !l.startSampled || !l.endSampled || !l.completePath || !l.seamMatches).Select(l => "Link: " + l.path));
            report.findings = findings.ToArray(); report.status = findings.Count == 0 ? "PASS_EDIT_NAVIGATION_AUDIT" : "FINDINGS";
            Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Output, "navigation_audit.json"), json);
            return json;
        }
    }
}
