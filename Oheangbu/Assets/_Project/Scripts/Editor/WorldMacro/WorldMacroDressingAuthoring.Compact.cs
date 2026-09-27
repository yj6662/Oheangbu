using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;
using Sheet = Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Draft incremental rebake for already-warped compact terrain. No asset or
    /// scene saves, legacy sheet loads, palette rebuilds, or story placement.
    /// Reuses the original Snapshot.Habitat, Owner, Weights and ReserveFixed.
    /// </summary>
    public static partial class WorldMacroDressingAuthoring
    {
        [Serializable]
        public sealed class CompactBakeReport
        {
            public string stage, startedUtc, finishedUtc, fingerprint;
            public int sourceMeshes, scannedMeshes, terrainChunks, waterMeshes, waterTriangles;
            public int colliderMasks, landmarkMasks, totalCells, processedCells, publishedCells;
            public int validLatticeNodes, missingLatticeNodes, validHabitatTiles;
            public int fineHeightResolution = Sheet.FineHeightResolution;
            public float fineHeightSpacing = Sheet.FineHeightSpacing, validationSpacing = 1f;
            public long validFineHeightNodes, missingFineHeightNodes, independentHeightProbes, fineHeightRawBytes;
            public int missingGroundTiles, inaccurateHeightTiles, existingMaskTiles;
            public long physicsRays, physicsMisses;
            public float maximumMeasuredHeightError, lastStepMilliseconds, commit;
            public bool ready, published;
            public string scope = "Resampled actual Terrain_ mesh colliders to optional 2 m fine height grids, retaining legacy 16 m heights and 4 m habitat/realm policy. Fine interpolation is validated at independent 1 m intermediate probes (0.35 m default tolerance); missing support and existing solid/water/road masks remain excluded. Existing source cells are not modified. Final visual and runtime checks remain required.";
        }

        [Serializable]
        sealed class CompactInputFingerprint
        {
            public WorldMacroSheetSO Geography;
            public string GeographyJson, LandmarksJson;
            public Sheet.PreserveArea[] PreservedAreas;
            public Sheet.FixedPlacement[] FixedPlacements;
            public bool DenseVegetation;
            public float RegionBlend;
        }

        sealed class CompactMeshStamp
        {
            public MeshFilter Filter;
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public Bounds Bounds, WorldBounds;
            public MeshCollider Ground;
            public bool Terrain;
        }

        struct CompactGroundProbe
        {
            public bool Valid;
            public float Height;
        }

        sealed class CompactBakeState
        {
            public Sheet Settings;
            public WorldMacroLandmarkSheetSO Landmarks;
            public Snapshot Source;
            public Scene Scene;
            public MeshFilter[] Meshes;
            public Collider[] Colliders;
            public readonly List<CompactMeshStamp> MeshStamps = new List<CompactMeshStamp>();
            public readonly Dictionary<long, List<CompactMeshStamp>> Ground = new Dictionary<long, List<CompactMeshStamp>>();
            public readonly HashSet<long> SampledLattice = new HashSet<long>();
            public readonly HashSet<CompactMeshStamp> ValidatedGround = new HashSet<CompactMeshStamp>();
            public readonly Dictionary<long, CompactGroundProbe> ProbeCache = new Dictionary<long, CompactGroundProbe>(70000);
            public readonly List<Sheet.Cell> Cells = new List<Sheet.Cell>();
            public readonly CompactBakeReport Report = new CompactBakeReport();
            public string InputJson;
            public long InitialManagedBytes;
            public int NX, NZ, MeshIndex, RouteIndex, RoutePoint = 1, ColliderIndex, CellIndex;
            public float MaximumHeightError;
        }

        static CompactBakeState compactBake;
        static CompactBakeReport compactLastReport;
        public static CompactBakeReport CompactProgress => compactBake != null ? compactBake.Report : compactLastReport;
        public static bool CompactReadyToFinish => compactBake != null && compactBake.Report.ready;

        /// <summary>Starts a detached bake. Caller has already cloned/mapped all settings, fixed placements and landmarks.</summary>
        public static CompactBakeReport BeginCompact(Sheet settings, WorldMacroLandmarkSheetSO clonedLandmarks,
            float maximumHeightError = .35f)
        {
            if (compactBake != null) throw new InvalidOperationException("A compact bake is already active; finish it or call AbortCompact before beginning again.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Compact dressing rebake requires Edit mode.");
            if (settings == null || settings.Geography == null || clonedLandmarks == null)
                throw new ArgumentException("Pass cloned dressing settings, cloned compact geography and cloned landmarks.");
            if (AssetDatabase.GetAssetPath(settings) == SheetPath ||
                AssetDatabase.GetAssetPath(settings.Geography) == WorldMacroBuilder.SheetPath ||
                AssetDatabase.GetAssetPath(clonedLandmarks) == WorldMacroBuilder.Folder + "/Landmarks/Landmarks.asset")
                throw new InvalidOperationException("Compact rebake accepts derivative assets or transient clones only, never the original dressing/geography/landmark assets.");
            if (!CompactFinite(maximumHeightError) || maximumHeightError <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumHeightError));
            var geo = settings.Geography;
            Vector2 size = geo.BoundsMax - geo.BoundsMin;
            if (!CompactFinite(size.x) || !CompactFinite(size.y) || size.x <= 0 || size.y <= 0 || size.x > 4096 || size.y > 6144)
                throw new InvalidOperationException("This bounded helper supports compact geography up to 4096 x 6144 m (requested world: 4000 x 6000 m).");
            if (settings.CellSize != 256 || geo.Regions == null || geo.Regions.Length != 5 ||
                settings.Prototypes == null || settings.Prototypes.Length == 0 || geo.Routes == null || geo.Sites == null)
                throw new InvalidOperationException("Expected 256 m cells, five valid region polygons, a prepared palette, routes and sites.");
            if (settings.Prototypes.Any(p => p == null) || settings.FixedPlacements == null)
                throw new InvalidOperationException("Prepared palette and mapped fixed placement arrays must not contain missing data.");
            if (geo.Regions.Any(r => r == null || r.Polygon == null || r.Polygon.Length < 3 || (int)r.Realm < 0 || (int)r.Realm > 4))
                throw new InvalidOperationException("Each compact region requires a polygon and a realm index in [0,4].");

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("A loaded compact scene must be active.");
            var state = new CompactBakeState
            {
                Settings = settings, Landmarks = clonedLandmarks, Scene = scene,
                MaximumHeightError = maximumHeightError,
                NX = Mathf.CeilToInt(size.x / 256), NZ = Mathf.CeilToInt(size.y / 256),
                InitialManagedBytes = GC.GetTotalMemory(false)
            };
            CompactMemoryGuard(state);
            state.Source = new Snapshot
            {
                Geo = geo, Settings = settings,
                MaskAreas = (settings.PreservedAreas ?? Array.Empty<Sheet.PreserveArea>())
                    .Where(a => a != null && (!settings.DenseVegetation || !a.TypedClearance)).ToArray(),
                RockRadius = settings.Prototypes.Where(p => p != null && p.Category == Sheet.Kind.Rock)
                    .Select(p => new Vector2(p.Size.x, p.Size.z).magnitude * p.Scale.y * .5f).DefaultIfEmpty(3).Max(),
                TreeRadius = settings.Prototypes.Where(p => p != null && p.Category == Sheet.Kind.Tree)
                    .Select(p => p.Radius * p.Scale.y).DefaultIfEmpty(.75f).Max()
            };
            // Preserve Snapshot.Habitat's existing mask/water/road logic, but let
            // every ground-height query use the actual compact collider surface.
            state.Source.HeightOverride = (float x, float z, out float y) => CompactGround(state, x, z, out y);
            if (state.Source.RockRadius > 64 || state.Source.TreeRadius > 64)
                throw new InvalidOperationException("Palette clearance radius exceeds the bounded 64 m sampler halo.");
            state.Meshes = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                .Where(f => f.gameObject.scene == scene && f.gameObject.activeInHierarchy && f.sharedMesh != null &&
                    (f.name.StartsWith("Terrain_", StringComparison.Ordinal) || f.sharedMesh.name.StartsWith("RiverSurface_", StringComparison.Ordinal) ||
                     (f.name.StartsWith("Water_",StringComparison.Ordinal)&&f.transform.parent!=null&&f.transform.parent.name=="02_Drainage_WaterSurface")))
                .OrderBy(f => f.name, StringComparer.Ordinal).ToArray();
            state.Colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == scene && c.gameObject.activeInHierarchy).ToArray();
            state.InputJson = CompactInputJson(state);
            state.Report.stage = "SnapshotMeshes";
            state.Report.startedUtc = DateTime.UtcNow.ToString("o");
            state.Report.sourceMeshes = state.Meshes.Length;
            state.Report.totalCells = state.NX * state.NZ;
            Physics.SyncTransforms();
            compactBake = state;
            compactLastReport = state.Report;
            return state.Report;
        }

        /// <summary>
        /// Returns true only when FinishCompact can publish. Snapshot reads at
        /// most one mesh per call; bake runs 1..4 cells, with a 200 ms soft budget
        /// between cells. A cell is indivisible; no editor update callbacks added.
        /// </summary>
        public static bool StepCompact(int maxCells = 8)
        {
            if (compactBake == null) throw new InvalidOperationException("Call BeginCompact first.");
            if (maxCells <= 0) throw new ArgumentOutOfRangeException(nameof(maxCells));
            var state = compactBake;
            CompactRequireScene(state);
            CompactMemoryGuard(state);
            double started = EditorApplication.timeSinceStartup;
            try
            {
                if (state.Report.ready) return true;
                switch (state.Report.stage)
                {
                    case "SnapshotMeshes":
                        if (state.MeshIndex < state.Meshes.Length)
                        {
                            CompactReadMesh(state, state.Meshes[state.MeshIndex]);
                            state.MeshIndex++;
                            state.Report.scannedMeshes = state.MeshIndex;
                        }
                        if (state.MeshIndex == state.Meshes.Length)
                        {
                            if (state.Report.terrainChunks == 0 || state.Report.waterMeshes == 0)
                                throw new InvalidOperationException("Actual enabled compact Terrain_ colliders and final RiverSurface_ meshes are required.");
                            state.Report.stage = "SnapshotRoutes";
                        }
                        return false;
                    case "SnapshotRoutes":
                        CompactReadRoutes(state, 256);
                        return false;
                    case "SnapshotSolids":
                        CompactReadSolids(state, 256);
                        return false;
                    case "SnapshotLandmarks":
                        CompactReadLandmarks(state);
                        ReserveFixed(state.Settings, state.Source);
                        state.Report.stage = "BakeCells";
                        return false;
                    case "BakeCells":
                        int limit = Math.Min(maxCells, 4);
                        for (int i = 0; i < limit && state.CellIndex < state.Report.totalCells; i++)
                        {
                            CompactMemoryGuard(state);
                            CompactBakeCell(state, state.CellIndex % state.NX, state.CellIndex / state.NX);
                            state.CellIndex++;
                            state.Report.processedCells = state.CellIndex;
                            if ((EditorApplication.timeSinceStartup - started) * 1000 >= 200) break;
                        }
                        if (state.CellIndex == state.Report.totalCells)
                        {
                            state.Report.ready = true;
                            state.Report.stage = "ReadyToFinish";
                        }
                        return state.Report.ready;
                    default:
                        throw new InvalidOperationException("Unknown compact bake stage: " + state.Report.stage);
                }
            }
            finally { state.Report.lastStepMilliseconds = (float)((EditorApplication.timeSinceStartup - started) * 1000); }
        }

        /// <summary>Assigns only the supplied derivative settings in memory. Caller owns SetDirty/SaveAssets/renderer reset.</summary>
        public static CompactBakeReport FinishCompact()
        {
            if (compactBake == null || !compactBake.Report.ready)
                throw new InvalidOperationException("Continue StepCompact until it returns true before finishing.");
            var state = compactBake;
            CompactRequireScene(state);
            CompactMemoryGuard(state);
            if (CompactInputJson(state) != state.InputJson)
                throw new InvalidOperationException("Compact geography, landmarks or exclusion inputs changed during the bake; no cells were published.");
            foreach (var stamp in state.MeshStamps) CompactRequireMesh(stamp);
            if (state.Cells.Count == 0) throw new InvalidOperationException("No valid physical habitat was found; no cells were published.");
            string fingerprint = CompactFingerprint(state);
            Sheet.Cell[] cells = state.Cells.ToArray();
            string[] regions = state.Source.Geo.Regions.Select(r => r.Id).ToArray();
            // All validation and allocation precedes this small publication step.
            state.Settings.Cells = cells;
            state.Settings.CompletedRegions = regions;
            state.Settings.SourceFingerprint = fingerprint;
            // Publication must survive asset reload; SaveAssetIfDirty alone cannot
            // persist plain managed-array writes on a ScriptableObject.
            EditorUtility.SetDirty(state.Settings);
            state.Report.fingerprint = fingerprint;
            state.Report.publishedCells = cells.Length;
            state.Report.published = true;
            state.Report.stage = "Complete";
            state.Report.finishedUtc = DateTime.UtcNow.ToString("o");
            compactLastReport = state.Report;
            compactBake = null;
            return compactLastReport;
        }

        public static CompactBakeReport AbortCompact()
        {
            if (compactBake != null)
            {
                compactBake.Report.stage = "AbortedWithoutPublishing";
                compactBake.Report.finishedUtc = DateTime.UtcNow.ToString("o");
                compactLastReport = compactBake.Report;
                compactBake = null;
            }
            return compactLastReport;
        }

        static void CompactReadMesh(CompactBakeState state, MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Compact source mesh disappeared during snapshot.");
            var mesh = filter.sharedMesh;
            var stamp = new CompactMeshStamp
            {
                Filter = filter, Mesh = mesh, Matrix = filter.transform.localToWorldMatrix, Bounds = mesh.bounds
            };
            if (filter.name.StartsWith("Terrain_", StringComparison.Ordinal))
            {
                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null || !collider.enabled || collider.isTrigger || collider.convex || collider.sharedMesh != mesh)
                    throw new InvalidOperationException("Expected enabled non-convex Terrain_ MeshCollider using the warped render mesh: " + filter.name);
                stamp.Ground = collider;
                var bounds = collider.bounds;
                stamp.WorldBounds = bounds;
                stamp.Terrain = true;
                CompactAddRect(state.Ground, Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z), .02f, state, stamp);
                state.Report.terrainChunks++;
            }
            else
            {
                ulong indices = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
                if ((ulong)mesh.vertexCount * 32UL + indices * 4UL > 64UL * 1024 * 1024)
                    throw new InvalidOperationException("Water mesh exceeds the 64 MiB per-step extraction guard: " + filter.name);
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var colours = mesh.colors;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (colours.Length == vertices.Length && Mathf.Max(colours[a].a, colours[b].a, colours[c].a) < .04f) continue;
                    var tri = new Triangle(stamp.Matrix.MultiplyPoint3x4(vertices[a]), stamp.Matrix.MultiplyPoint3x4(vertices[b]), stamp.Matrix.MultiplyPoint3x4(vertices[c]));
                    if (Mathf.Abs(tri.D) < .00001f) continue;
                    CompactAddRect(state.Source.Waters, tri.Rect, 24, state, tri);
                    state.Report.waterTriangles++;
                    if (state.Report.waterTriangles > 1500000) throw new InvalidOperationException("Compact water snapshot exceeds 1.5 million triangle guard.");
                }
                state.Report.waterMeshes++;
            }
            state.MeshStamps.Add(stamp);
        }

        static void CompactReadRoutes(CompactBakeState state, int budget)
        {
            var routes = state.Source.Geo.Routes;
            while (state.RouteIndex < routes.Length && budget > 0)
            {
                var route = routes[state.RouteIndex];
                if (route == null || route.Points == null) throw new InvalidOperationException("A compact route has missing geometry.");
                if (state.RoutePoint >= route.Points.Length) { state.RouteIndex++; state.RoutePoint = 1; continue; }
                var a = route.Points[state.RoutePoint - 1]; var b = route.Points[state.RoutePoint];
                CompactAddRect(state.Source.Roads, Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)),
                    route.Width * .5f + 14, state, new Segment { A = a, B = b, Width = route.Width });
                state.RoutePoint++; budget--;
            }
            if (state.RouteIndex == routes.Length) state.Report.stage = "SnapshotSolids";
        }

        static void CompactReadSolids(CompactBakeState state, int budget)
        {
            while (state.ColliderIndex < state.Colliders.Length && budget-- > 0)
            {
                var collider = state.Colliders[state.ColliderIndex++];
                if (collider == null || !collider.enabled || collider.isTrigger || collider is CharacterController ||
                    collider.name.StartsWith("Terrain_", StringComparison.Ordinal) || collider.name.StartsWith("Dressing_", StringComparison.Ordinal)) continue;
                var bounds = collider.bounds;
                if (bounds.size.sqrMagnitude < .01f) continue;
                CompactAddRect(state.Source.Solids, Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z), 10, state, bounds);
                state.Report.colliderMasks++;
            }
            if (state.ColliderIndex == state.Colliders.Length) state.Report.stage = "SnapshotLandmarks";
        }

        static void CompactReadLandmarks(CompactBakeState state)
        {
            foreach (var landmark in state.Landmarks.Landmarks ?? Array.Empty<WorldMacroLandmarkSheetSO.Landmark>())
            {
                if (landmark == null || !landmark.PlacementResolved) continue;
                var size = landmark.CourtyardSize + Vector2.one * 12;
                if (landmark.Id == "Cave") size += new Vector2(12, 50);
                var rotation = Quaternion.Euler(0, landmark.Yaw, 0);
                var bounds = new Bounds(landmark.Position, Vector3.zero);
                for (int i = 0; i < 4; i++) bounds.Encapsulate(landmark.Position + rotation *
                    new Vector3((i & 1) == 0 ? -size.x * .5f : size.x * .5f, 0, (i & 2) == 0 ? -size.y * .5f : size.y * .5f));
                CompactAddRect(state.Source.Solids, Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z), 10, state, bounds);
                state.Report.landmarkMasks++;
            }
            var vehicle = GameObject.Find(WorldMacroPalanquinAuthoring.RootName);
            if (vehicle != null && vehicle.scene == state.Scene)
            {
                var bounds = new Bounds(vehicle.transform.position, new Vector3(15, 8, 18));
                CompactAddRect(state.Source.Solids, Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z), 5, state, bounds);
            }
        }

        static void CompactBakeCell(CompactBakeState state, int x, int z)
        {
            state.ProbeCache.Clear();
            state.ValidatedGround.Clear();
            var source = state.Source;
            var geo = source.Geo;
            var origin = geo.BoundsMin + new Vector2(x * 256, z * 256);
            int halo = 1 + Mathf.CeilToInt((Mathf.Max(source.RockRadius, source.TreeRadius) + 8) / 16);
            // Snapshot.Height also reads neighboring vertices at exact lattice
            // positions. Include a halo for wet-tile and trunk clearance queries.
            for (int iz = -halo; iz <= 16 + halo; iz++)
            for (int ix = -halo; ix <= 16 + halo; ix++)
            {
                int gx = x * 16 + ix, gz = z * 16 + iz;
                long key = Key(gx, gz);
                if (!state.SampledLattice.Add(key)) continue;
                float px = geo.BoundsMin.x + gx * 16, pz = geo.BoundsMin.y + gz * 16;
                if (CompactGround(state, px, pz, out float y))
                { source.Heights[key] = y; state.Report.validLatticeNodes++; }
                else state.Report.missingLatticeNodes++;
            }
            var centre = origin + Vector2.one * 128;
            var cell = new Sheet.Cell
            {
                X = x, Z = z, Owner = Owner(geo, centre), Centre = new Vector3(centre.x, 0, centre.y),
                Heights = new float[289], FineHeights = new float[Sheet.FineHeightResolution * Sheet.FineHeightResolution], Habitat = new byte[4096], RealmWeights = new byte[1445],
                MinHeight = float.PositiveInfinity, MaxHeight = float.NegativeInfinity
            };
            for (int iz = 0; iz < 17; iz++)
            for (int ix = 0; ix < 17; ix++)
            {
                int at = iz * 17 + ix;
                if (source.Heights.TryGetValue(Key(x * 16 + ix, z * 16 + iz), out float y))
                {
                    cell.Heights[at] = y;
                    cell.MinHeight = Mathf.Min(cell.MinHeight, y);
                    cell.MaxHeight = Mathf.Max(cell.MaxHeight, y);
                }
                // Missing nodes remain finite zeroes for the existing data
                // contract; habitat cannot use a triangle with a missing node.
                Weights(geo, origin + new Vector2(ix * 16, iz * 16), state.Settings.RegionBlend, cell.RealmWeights, at * 5);
            }
            // These are stored 2 m nodes. Their validity remains bake-local:
            // no habitat tile can consume a missing/interpolated-invalid node.
            var fineValid = new bool[cell.FineHeights.Length];
            for (int iz = 0; iz < Sheet.FineHeightResolution; iz++)
            for (int ix = 0; ix < Sheet.FineHeightResolution; ix++)
            {
                int at = iz * Sheet.FineHeightResolution + ix;
                if (CompactGround(state, origin.x + ix * Sheet.FineHeightSpacing, origin.y + iz * Sheet.FineHeightSpacing, out float y))
                {
                    cell.FineHeights[at] = y; fineValid[at] = true; state.Report.validFineHeightNodes++;
                    cell.MinHeight = Mathf.Min(cell.MinHeight, y); cell.MaxHeight = Mathf.Max(cell.MaxHeight, y);
                }
                else state.Report.missingFineHeightNodes++;
            }
            int valid = 0;
            for (int iz = 0; iz < 64; iz++)
            for (int ix = 0; ix < 64; ix++)
            {
                float px = origin.x + ix * 4 + 2, pz = origin.y + iz * 4 + 2;
                byte habitat = source.Habitat(px, pz);
                if (habitat == 0) { state.Report.existingMaskTiles++; continue; }
                bool missing = false, inaccurate = false;
                // A 4 m tile contains nine stored 2 m nodes and sixteen distinct
                // 1 m intermediate samples. Validate interpolation, never just the
                // same values used to populate FineHeights.
                for (int sz = -2; sz <= 2 && !missing; sz++)
                for (int sx = -2; sx <= 2; sx++)
                {
                    float sampleX = px + sx, sampleZ = pz + sz;
                    if (!CompactFineHeight(cell, fineValid, sampleX, sampleZ, origin, out float approximate))
                    { missing = true; break; }
                    if ((sx & 1) == 0 && (sz & 1) == 0) continue;
                    state.Report.independentHeightProbes++;
                    if (!CompactGround(state, sampleX, sampleZ, out float actual)) { missing = true; break; }
                    float error = Mathf.Abs(approximate - actual);
                    state.Report.maximumMeasuredHeightError = Mathf.Max(state.Report.maximumMeasuredHeightError, error);
                    if (error > state.MaximumHeightError) inaccurate = true;
                }
                if (missing) { state.Report.missingGroundTiles++; continue; }
                if (inaccurate) { state.Report.inaccurateHeightTiles++; continue; }
                cell.Habitat[iz * 64 + ix] = habitat;
                valid++;
            }
            if (valid > 0)
            {
                cell.Centre.y = (cell.MinHeight + cell.MaxHeight) * .5f;
                state.Cells.Add(cell);
                state.Report.fineHeightRawBytes += (long)cell.FineHeights.Length * sizeof(float);
                state.Report.validHabitatTiles += valid;
            }
        }

        static bool CompactFineHeight(Sheet.Cell cell, bool[] valid, float x, float z, Vector2 origin, out float y)
        {
            y = 0;
            float u = Mathf.Clamp((x - origin.x) / Sheet.FineHeightSpacing, 0, 127.99999f), v = Mathf.Clamp((z - origin.y) / Sheet.FineHeightSpacing, 0, 127.99999f);
            int ix = Mathf.FloorToInt(u), iz = Mathf.FloorToInt(v); u -= ix; v -= iz;
            int at = iz * Sheet.FineHeightResolution + ix;
            bool Supports(int index, float weight) => weight <= .000001f || valid[index];
            if (u + v <= 1)
            { if (!Supports(at, 1 - u - v) || !Supports(at + 1, u) || !Supports(at + Sheet.FineHeightResolution, v)) return false; }
            else
            { if (!Supports(at + Sheet.FineHeightResolution + 1, u + v - 1) || !Supports(at + Sheet.FineHeightResolution, 1 - u) || !Supports(at + 1, 1 - v)) return false; }
            y = Sheet.Height(cell, x, z, origin); return CompactFinite(y);
        }

        static bool CompactGround(CompactBakeState state, float x, float z, out float y)
        {
            // Exact IEEE-754 XZ keys distinguish saved 2 m nodes, independent
            // 1 m checks, and fractional wet-bank/trunk-clearance sample positions.
            // Quantizing here would turn validation into a lookup of authored nodes.
            long probeKey = Key(BitConverter.SingleToInt32Bits(x == 0 ? 0 : x), BitConverter.SingleToInt32Bits(z == 0 ? 0 : z));
            if (state.ProbeCache.TryGetValue(probeKey, out var cached)) { y = cached.Height; return cached.Valid; }
            y = float.NegativeInfinity;
            if (state.Ground.TryGetValue(CellKey(x, z, state.Source.Geo), out var candidates))
            {
                foreach (var stamp in candidates)
                {
                    var bounds = stamp.WorldBounds;
                    if (x < bounds.min.x - .01f || x > bounds.max.x + .01f || z < bounds.min.z - .01f || z > bounds.max.z + .01f) continue;
                    if (state.ValidatedGround.Add(stamp)) CompactRequireMesh(stamp);
                    var ray = new Ray(new Vector3(x, bounds.max.y + 64, z), Vector3.down);
                    state.Report.physicsRays++;
                    if (stamp.Ground.Raycast(ray, out RaycastHit hit, bounds.size.y + 128) && hit.normal.y > .025f && CompactFinite(hit.point.y))
                        y = Mathf.Max(y, hit.point.y);
                }
            }
            bool valid = CompactFinite(y);
            if (!valid) { state.Report.physicsMisses++; y = 0; }
            state.ProbeCache[probeKey] = new CompactGroundProbe { Valid = valid, Height = y };
            return valid;
        }

        static void CompactAddRect<T>(Dictionary<long, List<T>> dictionary, Rect rect, float padding, CompactBakeState state, T value)
        {
            var geo = state.Source.Geo;
            int minX = Mathf.Max(-1, Mathf.FloorToInt((rect.xMin - padding - geo.BoundsMin.x) / 256));
            int maxX = Mathf.Min(state.NX, Mathf.FloorToInt((rect.xMax + padding - geo.BoundsMin.x) / 256));
            int minZ = Mathf.Max(-1, Mathf.FloorToInt((rect.yMin - padding - geo.BoundsMin.y) / 256));
            int maxZ = Mathf.Min(state.NZ, Mathf.FloorToInt((rect.yMax + padding - geo.BoundsMin.y) / 256));
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                long key = Key(x, z);
                if (!dictionary.TryGetValue(key, out var list)) { list = new List<T>(); dictionary.Add(key, list); }
                list.Add(value);
            }
        }

        static void CompactRequireMesh(CompactMeshStamp stamp)
        {
            if (stamp.Filter == null || !stamp.Filter.gameObject.activeInHierarchy || stamp.Filter.sharedMesh != stamp.Mesh ||
                stamp.Filter.transform.localToWorldMatrix != stamp.Matrix || stamp.Mesh.bounds != stamp.Bounds ||
                (stamp.Terrain && (stamp.Ground == null || !stamp.Ground.enabled || stamp.Ground.isTrigger || stamp.Ground.convex || stamp.Ground.sharedMesh != stamp.Mesh)))
                throw new InvalidOperationException("Compact source mesh or collider changed during the bake; abort and begin again.");
        }

        static void CompactRequireScene(CompactBakeState state)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !state.Scene.isLoaded || SceneManager.GetActiveScene() != state.Scene ||
                state.Settings == null || state.Landmarks == null || state.Settings.Geography != state.Source.Geo)
                throw new InvalidOperationException("Compact bake scene or input references changed; no result was published.");
        }

        static void CompactMemoryGuard(CompactBakeState state)
        {
            float commit = Prologue.PrologueAudit.CommitRatio();
            state.Report.commit = commit;
            if (commit >= .85f || GC.GetTotalMemory(false) - state.InitialManagedBytes > 512L * 1024 * 1024)
                throw new InvalidOperationException("Compact bake paused by memory guard (system commit >=85% or managed growth >512 MiB). No assets saved; retry the step after memory recovers or AbortCompact.");
        }

        static string CompactInputJson(CompactBakeState state) => JsonUtility.ToJson(new CompactInputFingerprint
        {
            Geography = state.Settings.Geography,
            GeographyJson = JsonUtility.ToJson(state.Settings.Geography), LandmarksJson = JsonUtility.ToJson(state.Landmarks),
            PreservedAreas = state.Settings.PreservedAreas, FixedPlacements = state.Settings.FixedPlacements,
            DenseVegetation = state.Settings.DenseVegetation, RegionBlend = state.Settings.RegionBlend
        });

        static string CompactFingerprint(CompactBakeState state)
        {
            var text = new StringBuilder(state.InputJson);
            text.Append("|compact-physics-v2-fine2m-check1m|").Append(state.MaximumHeightError.ToString("R", CultureInfo.InvariantCulture));
            foreach (var stamp in state.MeshStamps)
                text.Append('|').Append(stamp.Filter.name).Append('|').Append(AssetDatabase.GetAssetPath(stamp.Mesh))
                    .Append('|').Append(stamp.Mesh.vertexCount).Append('|').Append(stamp.Matrix.ToString("F6"))
                    .Append('|').Append(stamp.Bounds.ToString("F6"));
            using (var hash = SHA256.Create())
                return "compact-physics-v2-fine2m-check1m:" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        static bool CompactFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
