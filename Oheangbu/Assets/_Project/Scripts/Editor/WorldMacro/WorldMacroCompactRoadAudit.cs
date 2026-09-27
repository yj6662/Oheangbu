using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Saved compact geometry probes only. Never repairs a road or runs player/vehicle input.</summary>
    public static class WorldMacroCompactRoadAudit
    {
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string OriginalScene = "Assets/_Project/Scenes/World/W_Demo_Campaign.unity";
        const string Folder = "Assets/_Project/Art/World/WorldCompact";
        const float Spacing = 3f, HeightTolerance = 1f, GradeTolerance = .1f, CellSize = 64f;
        const int SamplesPerStep = 500, FailureLimit = 100;
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/World/WorldMacro/Compact"));
        static string ProgressPath => Path.Combine(Output, "road_physics_progress.json");
        static string ReportPath => Path.Combine(Output, "road_physics_report.json");
        static Cache cache;

        [Serializable] sealed class InputHash { public string path, hash; public bool original; }
        [Serializable] sealed class Origin { public string source = null, target = null; }
        [Serializable] sealed class OriginManifest { public string sourceHash = null; public Origin[] assets = Array.Empty<Origin>(); }
        [Serializable] sealed class Failure
        {
            public int sample;
            public float distance, plannedHeight, centerHeight, leftHeight, rightHeight, longitudinalDegrees, crossDegrees, sourceAuthoredDegrees, sourceHeightMismatch;
            public Vector3 position, sourcePosition;
            public string issues, category, centerSupport, leftSupport, rightSupport, waterReference;
            public bool protectedContent, sourceCorrespondence;
        }
        [Serializable] sealed class PreviousSample
        {
            public bool supported, sourceCorrespondence;
            public Vector3 actual, source;
        }
        [Serializable] sealed class RouteReport
        {
            public string id, status;
            public bool carriage;
            public float width, length, longitudinalLimit, crossSlopeLimit;
            public int expectedSamples, nextSample, failedSamples, issueCount, missingCenters, missingEdges, heightMismatches, longitudinalExceedances, crossSlopeExceedances;
            public int sourceCorrespondenceMissing, regressionCandidates, protectedAuthoredBaselineExceedances, protectedOtherFindings;
            public int waterAboveSupportReferences, belowTerrainSamples, supportedSubsurfaceSamples, bridgeSupportSamples;
            public float maximumHeightError, maximumLongitudinalDegrees, maximumCrossSlopeDegrees, maximumSourceAuthoredDegrees, maximumSourceXZCorrespondenceError;
            public PreviousSample previous;
            public List<Failure> failures = new List<Failure>();
        }
        [Serializable] sealed class Work
        {
            public string status, scene, geometrySignature, mappingHash, profilePath, error, startedUtc, updatedUtc;
            public string reliefPath, reliefDataHash, reliefWeightHash;
            public List<string> executionNotes = new List<string>();
            public string scope = "Read-only compact MeshCollider/explicit static deck probes, 3 m center/edge sampling, at most 500 samples per Step. Original authored-centerline comparison is not original physics or native traction verification. All failures, including retained protected baseline violations, remain findings.";
            public string waterScope = "Water heights/widths are geographic river references; no water collision or water-render-mesh certification.";
            public int nextRoute, routeCount, terrainColliders, bridgeColliders, caveFloorColliders, totalSamples, totalFailedSamples, totalIssues;
            public float spacingMetres = Spacing, heightToleranceMetres = HeightTolerance, gradeToleranceDegrees = GradeTolerance, commitRatio;
            public bool sourceInputsUnchanged, physicsOnlyRead = true, nativeTraversalVerified = false;
            public InputHash[] inputs;
            public RouteReport[] routes;
        }
        sealed class Support
        {
            public Collider collider;
            public Bounds bounds;
            public string path, kind;
        }
        struct SupportHit
        {
            public bool found, terrainFound;
            public Vector3 point, normal;
            public float terrainY;
            public string path, kind;
        }
        struct WaterSegment { public Vector3 a, b; public float width; public string id; }
        struct SourceSegment { public Vector3 a, b; }
        sealed class RouteData
        {
            public Vector3[] points;
            public float[] cumulative;
            public Dictionary<long, List<SourceSegment>> sourceIndex = new Dictionary<long, List<SourceSegment>>();
        }
        sealed class Cache
        {
            public WorldMacroSheetSO geo, sourceGeo;
            public WorldMacroPlaytestSO content, sourceContent;
            public WorldMacroCompressionMapSO mapping;
            public UnityEngine.Object profile;
            public Rect[] protectedContent;
            public List<Support> supports;
            public Dictionary<long, List<Support>> supportIndex;
            public Dictionary<long, List<WaterSegment>> waterIndex;
            public RouteData[] routes;
            public string signature;
        }

        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static string FilePath(string asset) => Path.IsPathRooted(asset) ? asset : Path.GetFullPath(Path.Combine(Application.dataPath, "..", asset));
        static string HashFile(string path) { using (var stream = File.OpenRead(FilePath(path))) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string HashText(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static long Key(Vector3 p) => Key(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
        static bool Inside(Bounds b, Vector3 p) => p.x >= b.min.x - .01f && p.x <= b.max.x + .01f && p.z >= b.min.z - .01f && p.z <= b.max.z + .01f;
        static void AddBounds<T>(Dictionary<long, List<T>> index, Vector3 min, Vector3 max, T value)
        {
            for (int z = Mathf.FloorToInt(min.z / CellSize); z <= Mathf.FloorToInt(max.z / CellSize); z++)
                for (int x = Mathf.FloorToInt(min.x / CellSize); x <= Mathf.FloorToInt(max.x / CellSize); x++)
                { long key = Key(x, z); if (!index.TryGetValue(key, out var list)) { list = new List<T>(); index.Add(key, list); } list.Add(value); }
        }
        static void RequireEdit(bool guard)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Compiled compact scene in Edit Mode required.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the compact sculpt before beginning or resuming its physical audit.");
            if (guard && Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; no road physics probes started.");
        }
        static string AtomicJson(string path, object value)
        {
            string tmp = path + ".tmp";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(value, true);
                    File.WriteAllText(tmp, json); if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); return json;
                }
                catch (IOException exception)
                {
                    if (value is Work work)
                    {
                        work.executionNotes ??= new List<string>();
                        work.executionNotes.Add(DateTime.UtcNow.ToString("o") + " AtomicJson attempt " + (attempt + 1) + "/3 at " + path + ": " + exception.Message);
                    }
                    if (attempt == 2) throw new IOException("Could not atomically persist " + path + " after 3 attempts; existing destination was not deleted.", exception);
                    System.Threading.Thread.Sleep(attempt == 0 ? 10 : 25);
                }
            }
            throw new InvalidOperationException("Unreachable atomic report write state.");
        }
        static bool Unchanged(Work work, bool originalsOnly = false) => work.inputs.Where(i => !originalsOnly || i.original).All(i => File.Exists(FilePath(i.path)) && HashFile(i.path) == i.hash);
        static Work Load()
        {
            if (!File.Exists(ProgressPath)) throw new InvalidOperationException("Begin the road physical audit first.");
            var work = JsonUtility.FromJson<Work>(File.ReadAllText(ProgressPath));
            if (work == null || work.scene != ScenePath || work.routes == null || work.routes.Length != 37 || work.inputs == null) throw new InvalidOperationException("Road audit progress does not match the 36 routes plus Content.MainPath inventory.");
            return work;
        }
        static string SupportKind(Collider collider, string path)
        {
            if (collider is MeshCollider && collider.name.StartsWith("Terrain_", StringComparison.Ordinal) && path.StartsWith("WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/", StringComparison.Ordinal)) return "terrain";
            if (path.StartsWith("WorldMacro_AuthoredGeography/03_SettlementAndLandmark_Massing/", StringComparison.Ordinal) &&
                (collider.name == "Bridge_Deck_TEST" || collider.name.StartsWith("Bridge_Apron_TEST_", StringComparison.Ordinal))) return "bridge";
            if (path.StartsWith("Playtest_NaturalCave/", StringComparison.Ordinal) && !path.Contains("_Previous_") &&
                (collider.name == "Natural_Cave_Floor" || collider.name == "Continuous_Approach_Soil" || collider.name == "Portal_Outer_Soil" || collider.name == "Exterior_Mine_Approach" || collider.name == "Natural_Mine_Broad_Apron")) return "cave_floor";
            return null;
        }
        static List<Support> CollectSupports()
        {
            var values = new List<Support>();
            foreach (var collider in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider.attachedRigidbody != null) continue;
                string path = Hierarchy(collider.transform), kind = SupportKind(collider, path);
                if (kind == null) continue;
                if (collider is MeshCollider mesh && (mesh.sharedMesh == null || EditorUtility.IsDirty(mesh.sharedMesh))) throw new InvalidOperationException("Supporting mesh must exist and be saved: " + path);
                values.Add(new Support { collider = collider, bounds = collider.bounds, path = path, kind = kind });
            }
            return values.OrderBy(v => v.path, StringComparer.Ordinal).ToList();
        }
        static string Signature(List<Support> supports)
        {
            var text = new StringBuilder();
            foreach (var item in supports)
            {
                text.Append(item.path).Append('|').Append(item.kind).Append('|').Append(item.bounds.center.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(item.bounds.size.ToString("R", CultureInfo.InvariantCulture)).Append('|');
                text.Append(item.collider.transform.localToWorldMatrix.ToString("R", CultureInfo.InvariantCulture));
                if (item.collider is MeshCollider mesh)
                { string path = AssetDatabase.GetAssetPath(mesh.sharedMesh); text.Append(path).Append('|').Append(AssetDatabase.GetAssetDependencyHash(path)); }
                if (item.collider is BoxCollider box) text.Append(box.center.ToString("R", CultureInfo.InvariantCulture)).Append(box.size.ToString("R", CultureInfo.InvariantCulture));
            }
            return HashText(text.ToString());
        }
        static Cache BuildCache()
        {
            Physics.SyncTransforms();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var session = roots.SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
            var summon = roots.SelectMany(g => g.GetComponentsInChildren<WorldMacroPalanquinSummon>(true)).Single();
            var result = new Cache { geo = summon.WorldSheet, content = session.Content };
            if (result.geo == null || result.content == null || !AssetDatabase.GetAssetPath(result.geo).StartsWith(Folder + "/", StringComparison.Ordinal) || !AssetDatabase.GetAssetPath(result.content).StartsWith(Folder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Compact session and summon must reference compact geography/content.");
            result.mapping = AssetDatabase.LoadAssetAtPath<WorldMacroCompressionMapSO>(Folder + "/Compression.asset"); result.sourceGeo = result.geo.CompressionSource;
            var original = JsonUtility.FromJson<OriginManifest>(File.ReadAllText(Path.Combine(Output, "progress.json")));
            string sourceContent = original.assets.Single(a => a.target == AssetDatabase.GetAssetPath(result.content)).source;
            result.sourceContent = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(sourceContent);
            if (result.mapping == null || result.mapping.Mapping == null || result.sourceGeo == null || result.sourceContent == null) throw new InvalidOperationException("Compression/source correspondence assets are missing.");
            var serialized = new SerializedObject(result.geo); var profileField = serialized.FindProperty("CompactRoadGrade");
            result.profile = profileField?.objectReferenceValue;
            if (result.profile == null) throw new InvalidOperationException("CompactRoadGrade must be bound before post-sculpt road auditing.");
            var profile = new SerializedObject(result.profile); var rectangles = profile.FindProperty("Protected");
            if (rectangles == null || !rectangles.isArray) throw new InvalidOperationException("Road grade protected-content rectangles are unavailable.");
            result.protectedContent = new Rect[rectangles.arraySize]; for (int i = 0; i < rectangles.arraySize; i++) result.protectedContent[i] = rectangles.GetArrayElementAtIndex(i).rectValue;
            if (result.geo.Routes.Length != 36) throw new InvalidOperationException("Expected all 36 authored routes, without route expansion.");
            result.supports = CollectSupports(); if (result.supports.Count(s => s.kind == "terrain") == 0) throw new InvalidOperationException("No actual Terrain_ mesh colliders found.");
            result.signature = Signature(result.supports); result.supportIndex = new Dictionary<long, List<Support>>();
            foreach (var item in result.supports) AddBounds(result.supportIndex, item.bounds.min, item.bounds.max, item);
            result.waterIndex = new Dictionary<long, List<WaterSegment>>();
            foreach (var river in result.geo.Rivers ?? Array.Empty<WorldMacroSheetSO.RiverSpec>()) for (int i = 1; i < river.Points.Length; i++)
            {
                var segment = new WaterSegment { a = river.Points[i - 1], b = river.Points[i], width = river.Width, id = river.Id };
                AddBounds(result.waterIndex, Vector3.Min(segment.a, segment.b) - new Vector3(river.Width * .5f, 0, river.Width * .5f), Vector3.Max(segment.a, segment.b) + new Vector3(river.Width * .5f, 0, river.Width * .5f), segment);
            }
            var routes = new List<RouteData>();
            foreach (var route in result.geo.Routes)
            { var sourceRoute = result.sourceGeo.Routes.Single(r => r.Id == route.Id); routes.Add(RouteDataFor(route.Points, sourceRoute.Points)); }
            routes.Add(RouteDataFor(result.content.MainPath, result.sourceContent.MainPath)); result.routes = routes.ToArray(); return result;
        }
        static RouteData RouteDataFor(Vector3[] points, Vector3[] source)
        {
            if (points == null || points.Length < 2 || source == null || source.Length < 2) throw new InvalidOperationException("Road polyline and its original correspondence require at least two points.");
            var route = new RouteData { points = points, cumulative = new float[points.Length] };
            for (int i = 1; i < points.Length; i++) route.cumulative[i] = route.cumulative[i - 1] + Flat(points[i - 1], points[i]);
            if (!(route.cumulative[route.cumulative.Length - 1] > .001f) || float.IsInfinity(route.cumulative[route.cumulative.Length - 1])) throw new InvalidOperationException("Road polyline requires finite, positive horizontal length.");
            for (int i = 1; i < source.Length; i++)
            { var segment = new SourceSegment { a = source[i - 1], b = source[i] }; AddBounds(route.sourceIndex, Vector3.Min(segment.a, segment.b) - Vector3.one * 4, Vector3.Max(segment.a, segment.b) + Vector3.one * 4, segment); }
            return route;
        }
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static Vector3 At(RouteData route, float distance)
        {
            if (distance <= 0) return route.points[0]; if (distance >= route.cumulative[route.cumulative.Length - 1]) return route.points[route.points.Length - 1];
            int index = Array.BinarySearch(route.cumulative, distance); if (index >= 0) return route.points[index]; index = ~index;
            float length = route.cumulative[index] - route.cumulative[index - 1]; return Vector3.Lerp(route.points[index - 1], route.points[index], length > .0001f ? (distance - route.cumulative[index - 1]) / length : 0);
        }
        static bool SourcePoint(RouteData route, Vector3 compact, out Vector3 original, out float error)
        {
            var expected = cache.mapping.Inverse(compact); original = default; error = float.MaxValue;
            if (!route.sourceIndex.TryGetValue(Key(expected), out var segments)) return false;
            foreach (var segment in segments)
            {
                float distance = WorldMacroTerrain.SegmentDistance(expected.x, expected.z, segment.a, segment.b, out float t);
                if (distance >= error) continue; error = distance; original = Vector3.Lerp(segment.a, segment.b, t);
            }
            return error <= 1f;
        }
        static SupportHit Probe(Vector3 planned)
        {
            var result = new SupportHit(); if (!cache.supportIndex.TryGetValue(Key(planned), out var candidates)) return result;
            float terrainError = float.PositiveInfinity, deckError = float.PositiveInfinity; SupportHit deck = default;
            foreach (var candidate in candidates)
            {
                if (!Inside(candidate.bounds, planned)) continue;
                var ray = new Ray(new Vector3(planned.x, candidate.bounds.max.y + 1f, planned.z), Vector3.down);
                if (!candidate.collider.Raycast(ray, out var hit, candidate.bounds.size.y + 2f) || hit.normal.y < .1f) continue;
                float error = Mathf.Abs(hit.point.y - planned.y);
                if (candidate.kind == "terrain")
                {
                    if (error >= terrainError) continue; terrainError = error; result.found = result.terrainFound = true; result.terrainY = hit.point.y;
                    result.point = hit.point; result.normal = hit.normal; result.path = candidate.path; result.kind = candidate.kind;
                }
                else if (hit.point.y <= planned.y + .8f && hit.point.y >= planned.y - 4f && error < deckError)
                { deckError = error; deck = new SupportHit { found = true, point = hit.point, normal = hit.normal, path = candidate.path, kind = candidate.kind }; }
            }
            if (deck.found && deckError <= terrainError + .05f) { deck.terrainFound = result.terrainFound; deck.terrainY = result.terrainY; return deck; }
            return result;
        }
        static string WaterAbove(Vector3 actual)
        {
            if (!cache.waterIndex.TryGetValue(Key(actual), out var segments)) return null;
            foreach (var segment in segments)
                if (WorldMacroTerrain.SegmentDistance(actual.x, actual.z, segment.a, segment.b, out float t) <= segment.width * .5f && Mathf.Lerp(segment.a.y, segment.b.y, t) > actual.y + .15f) return segment.id;
            return null;
        }
        static bool Protected(Vector3 position) => cache.protectedContent.Any(r => position.x >= r.xMin && position.x <= r.xMax && position.z >= r.yMin && position.z <= r.yMax);
        static float Degrees(float rise, float run) => run < .001f ? (Mathf.Abs(rise) > .01f ? 90 : 0) : Mathf.Atan(Mathf.Abs(rise) / run) * Mathf.Rad2Deg;

        public static string Begin()
        {
            RequireEdit(true); cache = BuildCache();
            var work = new Work { status = "RUNNING", scene = ScenePath, startedUtc = DateTime.UtcNow.ToString("o"), geometrySignature = cache.signature, mappingHash = HashFile(Folder + "/Compression.asset"), profilePath = AssetDatabase.GetAssetPath(cache.profile),
                terrainColliders = cache.supports.Count(s => s.kind == "terrain"), bridgeColliders = cache.supports.Count(s => s.kind == "bridge"), caveFloorColliders = cache.supports.Count(s => s.kind == "cave_floor"), routeCount = 37, commitRatio = Prologue.PrologueAudit.CommitRatio() };
            var origin = JsonUtility.FromJson<OriginManifest>(File.ReadAllText(Path.Combine(Output, "progress.json")));
            var sourcePaths = new HashSet<string>(origin.assets.Where(a => a.target == AssetDatabase.GetAssetPath(cache.geo) || a.target == AssetDatabase.GetAssetPath(cache.content)).Select(a => a.source), StringComparer.Ordinal) { OriginalScene };
            var allPaths = new HashSet<string>(sourcePaths, StringComparer.Ordinal) { AssetDatabase.GetAssetPath(cache.geo), AssetDatabase.GetAssetPath(cache.content), AssetDatabase.GetAssetPath(cache.sourceGeo), Folder + "/Compression.asset", work.profilePath };
            if (cache.profile is WorldMacroRoadGradeSO profile && profile.Relief != null)
            {
                work.reliefPath = AssetDatabase.GetAssetPath(profile.Relief);
                if (work.reliefPath != Folder + "/Relief.asset" || EditorUtility.IsDirty(profile.Relief))
                    throw new InvalidOperationException("Road audit requires the saved private relief dependency.");
                work.reliefDataHash = profile.Relief.DataHash; work.reliefWeightHash = profile.Relief.WeightHash; allPaths.Add(work.reliefPath);
            }
            work.inputs = allPaths.OrderBy(p => p, StringComparer.Ordinal).Select(path => new InputHash { path = path, hash = HashFile(path), original = sourcePaths.Contains(path) }).ToArray();
            if (work.inputs.Single(i => i.path == OriginalScene).hash != origin.sourceHash) throw new InvalidOperationException("Original source scene hash differs from the compact conversion baseline.");
            work.routes = Enumerable.Range(0, cache.routes.Length).Select(i =>
            {
                bool main = i == cache.geo.Routes.Length; var authored = main ? null : cache.geo.Routes[i]; float length = cache.routes[i].cumulative.Last();
                if (!main && (!(authored.Width > 0) || float.IsInfinity(authored.Width))) throw new InvalidOperationException("Route requires finite, positive full width: " + authored.Id);
                return new RouteReport { id = main ? "Content.MainPath" : authored.Id, carriage = !main && authored.Carriage, width = main ? 4.1f : authored.Width,
                    length = length, expectedSamples = Mathf.Max(2, Mathf.CeilToInt(length / Spacing) + 1), longitudinalLimit = !main && authored.Carriage ? 14 : 30,
                    crossSlopeLimit = !main && authored.Carriage ? 14 : 30, status = "PENDING" };
            }).ToArray();
            work.sourceInputsUnchanged = true; work.updatedUtc = DateTime.UtcNow.ToString("o"); return AtomicJson(ProgressPath, work);
        }
        public static string Step()
        {
            RequireEdit(true); var work = Load();
            if (!string.IsNullOrEmpty(work.error))
            {
                work.executionNotes ??= new List<string>(); work.executionNotes.Add(DateTime.UtcNow.ToString("o") + " Resuming after prior exception: " + work.error);
                work.error = null; AtomicJson(ProgressPath, work);
            }
            if (!Unchanged(work)) throw new InvalidOperationException("A saved source/compact route, grade or mapping input changed; Begin a fresh post-sculpt audit.");
            var supports = CollectSupports(); string signature = Signature(supports);
            if (signature != work.geometrySignature) throw new InvalidOperationException("Actual support collider geometry changed; Begin a fresh audit instead of mixing old/new samples.");
            if (work.nextRoute >= work.routes.Length)
            {
                if (work.routes.Any(r => r.nextSample != r.expectedSamples)) throw new InvalidOperationException("Completed route count has incomplete sample records.");
                try
                {
                    work.sourceInputsUnchanged = Unchanged(work, true); work.updatedUtc = DateTime.UtcNow.ToString("o");
                    work.status = work.totalFailedSamples == 0 && work.sourceInputsUnchanged ? "PASS_BOUNDED_ROAD_PHYSICS" : "FINDINGS";
                    AtomicJson(ReportPath, work); return AtomicJson(ProgressPath, work);
                }
                catch (Exception exception) { work.status = "INTERRUPTED"; work.error = exception.ToString(); AtomicJson(ProgressPath, work); throw; }
            }
            if (cache == null || cache.signature != signature) cache = BuildCache();
            var row = work.routes[work.nextRoute]; var route = cache.routes[work.nextRoute]; row.status = "RUNNING";
            int end = Math.Min(row.expectedSamples, row.nextSample + SamplesPerStep); work.commitRatio = Prologue.PrologueAudit.CommitRatio();
            try
            {
                for (int sample = row.nextSample; sample < end; sample++)
                {
                    float distance = row.length * sample / (row.expectedSamples - 1f); var planned = At(route, distance);
                    Vector3 tangent = At(route, Mathf.Min(row.length, distance + 1.5f)) - At(route, Mathf.Max(0, distance - 1.5f)); tangent.y = 0;
                    if (tangent.sqrMagnitude < .000001f) { tangent = At(route, Mathf.Min(row.length, distance + .75f)) - planned; tangent.y = 0; }
                    if (tangent.sqrMagnitude < .000001f) { tangent = planned - At(route, Mathf.Max(0, distance - .75f)); tangent.y = 0; }
                    bool validTangent = tangent.sqrMagnitude >= .000001f; tangent.Normalize();
                    Vector3 side = new Vector3(tangent.z, 0, -tangent.x) * (row.width * .5f);
                    var center = Probe(planned); var left = Probe(planned - side); var right = Probe(planned + side);
                    bool protectedContent = Protected(planned), correspondence = SourcePoint(route, planned, out var source, out float sourceError);
                    if (!correspondence) row.sourceCorrespondenceMissing++;
                    if (sourceError < float.MaxValue) row.maximumSourceXZCorrespondenceError = Mathf.Max(row.maximumSourceXZCorrespondenceError, sourceError);
                    var issues = new List<string>(); float heightError = center.found ? Mathf.Abs(center.point.y - planned.y) : 0, longitudinal = 0, cross = 0, sourceGrade = 0, sourceHeightMismatch = 0;
                    if (!validTangent) issues.Add("UNDEFINED_ROUTE_TANGENT");
                    if (!center.found) { issues.Add("MISSING_CENTER_SUPPORT"); row.missingCenters++; }
                    if (!left.found || !right.found) { issues.Add("MISSING_FULL_WIDTH_EDGE_SUPPORT"); row.missingEdges++; }
                    if (center.found && heightError > HeightTolerance) { issues.Add("CENTERLINE_HEIGHT_MISMATCH"); row.heightMismatches++; }
                    if (center.terrainFound && center.terrainY > planned.y + HeightTolerance)
                    { row.belowTerrainSamples++; if (center.found && center.kind == "cave_floor" && heightError <= HeightTolerance) row.supportedSubsurfaceSamples++; }
                    if (center.found && center.kind == "bridge") row.bridgeSupportSamples++;
                    if (center.found && row.previous != null && row.previous.supported)
                    { longitudinal = Degrees(center.point.y - row.previous.actual.y, Flat(center.point, row.previous.actual)); if (longitudinal > row.longitudinalLimit + GradeTolerance) { issues.Add("LONGITUDINAL_GRADE"); row.longitudinalExceedances++; } }
                    if (left.found && right.found)
                    { cross = Degrees(right.point.y - left.point.y, row.width); if (cross > row.crossSlopeLimit + GradeTolerance) { issues.Add("CROSS_SLOPE"); row.crossSlopeExceedances++; } }
                    if (correspondence)
                    {
                        sourceHeightMismatch = Mathf.Abs(source.y - WorldMacroTerrain.SurfaceHeight(cache.sourceGeo, source.x, source.z));
                        if (row.previous != null && row.previous.sourceCorrespondence) sourceGrade = Degrees(source.y - row.previous.source.y, Flat(source, row.previous.source));
                    }
                    string water = center.found ? WaterAbove(center.point) : null;
                    if (water != null) { issues.Add("GEOGRAPHIC_WATER_REFERENCE_ABOVE_SUPPORT"); row.waterAboveSupportReferences++; }
                    row.maximumHeightError = Mathf.Max(row.maximumHeightError, heightError); row.maximumLongitudinalDegrees = Mathf.Max(row.maximumLongitudinalDegrees, longitudinal);
                    row.maximumCrossSlopeDegrees = Mathf.Max(row.maximumCrossSlopeDegrees, cross); row.maximumSourceAuthoredDegrees = Mathf.Max(row.maximumSourceAuthoredDegrees, sourceGrade);
                    if (issues.Count > 0)
                    {
                        row.failedSamples++; row.issueCount += issues.Count;
                        bool protectedBaseline = protectedContent && correspondence && row.previous != null && Protected(row.previous.actual) && row.previous.sourceCorrespondence && sourceGrade > row.longitudinalLimit + GradeTolerance && issues.Contains("LONGITUDINAL_GRADE");
                        bool regressionCandidate = correspondence && row.previous != null && row.previous.sourceCorrespondence && sourceGrade <= row.longitudinalLimit + GradeTolerance && issues.Contains("LONGITUDINAL_GRADE");
                        string category = protectedBaseline ? "PROTECTED_AUTHORED_BASELINE_WITHOUT_SOURCE_PHYSICS" : regressionCandidate ? "COMPACT_REGRESSION_CANDIDATE_WITHOUT_SOURCE_PHYSICS" : "SOURCE_PHYSICAL_BASELINE_UNVERIFIED";
                        if (protectedBaseline) row.protectedAuthoredBaselineExceedances++; if (regressionCandidate) row.regressionCandidates++; if (protectedContent && !protectedBaseline) row.protectedOtherFindings++;
                        if (row.failures.Count < FailureLimit) row.failures.Add(new Failure { sample = sample, distance = distance, position = planned, plannedHeight = planned.y, centerHeight = center.found ? center.point.y : 0,
                            leftHeight = left.found ? left.point.y : 0, rightHeight = right.found ? right.point.y : 0, longitudinalDegrees = longitudinal, crossDegrees = cross, sourceAuthoredDegrees = sourceGrade,
                            sourceHeightMismatch = sourceHeightMismatch, sourcePosition = source, sourceCorrespondence = correspondence, protectedContent = protectedContent,
                            issues = string.Join("|", issues), category = category, centerSupport = center.path, leftSupport = left.path, rightSupport = right.path, waterReference = water });
                    }
                    row.previous = new PreviousSample { supported = center.found, actual = center.point, sourceCorrespondence = correspondence, source = source }; row.nextSample = sample + 1;
                }
                if (row.nextSample == row.expectedSamples) { row.status = row.failedSamples == 0 ? "PASS_BOUNDED_PHYSICAL_SAMPLES" : "FINDINGS"; work.nextRoute++; }
                work.totalSamples = work.routes.Sum(r => r.nextSample); work.totalFailedSamples = work.routes.Sum(r => r.failedSamples); work.totalIssues = work.routes.Sum(r => r.issueCount);
                work.updatedUtc = DateTime.UtcNow.ToString("o"); work.sourceInputsUnchanged = Unchanged(work, true);
                if (work.nextRoute == work.routes.Length)
                { work.status = work.totalFailedSamples == 0 && work.sourceInputsUnchanged ? "PASS_BOUNDED_ROAD_PHYSICS" : "FINDINGS"; AtomicJson(ReportPath, work); }
                else work.status = "RUNNING";
                return AtomicJson(ProgressPath, work);
            }
            catch (Exception exception) { work.status = "INTERRUPTED"; work.error = exception.ToString(); work.updatedUtc = DateTime.UtcNow.ToString("o"); AtomicJson(ProgressPath, work); throw; }
        }
        public static string Status()
        {
            var work = Load();
            return JsonUtility.ToJson(work, true);
        }
    }
}
