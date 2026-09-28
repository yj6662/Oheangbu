using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Bounded, reversible form study of the CURRENT compact terrain. No source mesh,
    /// geography, scene or foliage asset is saved. The original vertex XZ, indices, UVs
    /// and collider topology survive; only world Y changes. NOT a world regeneration.
    /// Prepare -> BeginPreview -> rebuild transient paint field / warm up / capture ->
    /// EndPreview in finally. End before ending a containing material/foliage preview.
    /// </summary>
    public static partial class InkPaintingMountainForm
    {
        const string GeographyPath = "Assets/_Project/Art/World/WorldCompact/Data/01_WorldMacroSheet.asset";
        const string TerrainRoot = "WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/";
        const string ContextRoot = "WorldMacro_AuthoredGeography/BackgroundContext_RenderOnly_NoGameplay_NoColliders/";
        const float SpatialBinSize = 64, SupportBinSize = 8, SupportEdgeTolerance = .0001f;

        [Serializable] public sealed class RidgeControl
        {
            public string ridge;
            [Range(0, 1)] public float along;
            public bool saddle;
            public float gain = 1, alongRadius = 160, crossRadius = 200;
        }
        [Serializable] public sealed class Settings
        {
            public Rect Patch = new Rect(200, 240, 900, 1110);
            public float Feather = 160, GridSpacing = 8, FoliageGridSpacing = 2, FoliageTriangleBinSize = 16;
            public float DeltaLimit = 35, PeakAmplitude = 28, SaddleAmplitude = 35;
            public float NearBlur = 40, BroadBlur = 90, Smoothing = .85f;
            public float ProtectionFeather = 48, RiverPadding = 12, SupportTolerance = .1f;
            public bool ReprojectStaticVegetation = true;
            public string Geography = GeographyPath;
            // Deliberate, editable proposed forms ON the existing authored crest paths.
            // They are not claimed to be existing peaks or random synthetic mountains.
            public RidgeControl[] Controls = {
                new RidgeControl { ridge="CheongrimOuterFoothillBranch", along=.20f, alongRadius=180, crossRadius=210 },
                new RidgeControl { ridge="CheongrimOuterFoothillBranch", along=.48f, saddle=true, alongRadius=130, crossRadius=235 },
                new RidgeControl { ridge="CheongrimOuterFoothillBranch", along=.77f, gain=.8f, alongRadius=150, crossRadius=170 },
                new RidgeControl { ridge="EasternWestSpur", along=.29f, alongRadius=230, crossRadius=270 },
                new RidgeControl { ridge="EasternWestSpur", along=.49f, saddle=true, alongRadius=150, crossRadius=260 },
                new RidgeControl { ridge="EasternWestSpur", along=.69f, gain=.75f, alongRadius=200, crossRadius=240 }
            };
        }
        [Serializable] public sealed class MeshRow
        {
            public string path, source;
            public int vertices, triangles, changedVertices, protectedVertices, protectedTriangles;
            public bool collider, context;
            public float maxDelta, protectedMaxDelta, maxSlopeBefore, maxSlopeAfter;
            public int slopesAbove55Before, slopesAbove55After;
        }
        [Serializable] public sealed class ControlRow
        { public string ridge; public bool saddle, surfaceSampled; public float alongMetres, amplitude, alongRadius, crossRadius; public Vector3 actualSurfaceAnchor; }
        [Serializable] public sealed class StaticPlantRow
        { public string path, status, reason; public int renderers, colliders; public Vector3 originalPosition; public float delta; }
        [Serializable] public sealed class StageTiming
        { public string stage, status; public double milliseconds; }
        [Serializable] public sealed class SupportHit
        {
            public string path; public int source, submesh, triangle;
            public Vector3 a, b, c, delta, barycentric;
            public double originalHeight, finalHeight;
            public bool selectedBefore, selectedAfter, runtimeSelectedBefore, runtimeSelectedAfter, accepted, edgeTolerance;
        }
        [Serializable] public sealed class SupportIssue
        {
            public string kind; public Vector3 position;
            public float referenceBefore, referenceAfter, referenceDelta, fieldDelta, absoluteError;
            public float legacyFirstNonzeroDelta, originalWinnerDelta, originalHeightSpread;
            public int hits; public SupportHit[] candidates;
            public bool runtimeHit, runtimeUsedEdgeTolerance; public int runtimeBeforeTriangle, runtimeAfterTriangle;
        }
        [Serializable] public sealed class Receipt
        {
            public string utc, scene, phase, foliageSupportMode;
            public Settings settings;
            public ToeReceipt toe;
            public Rect fieldRect;
            public int gridX, gridZ, protectedRectangles, roadSegments, riverSegments, sourceMeshes, changedMeshes;
            public int changedVertices, protectedVertices, protectedTriangles, foliageSheets, earlyPackets, earlyMatrices;
            public int supportSamples, supportMissing, fieldInvalidNodes, staticVegetationProtected;
            public int supportTriangles, supportColliderMeshes, visualOnlyContextMeshesExcluded;
            public int naturalStaticRoots, movedStaticRoots, ambiguousStaticRenderers;
            public int protectionBins, protectionReferences, protectionMaxCandidates, crestBins, crestReferences;
            public int supportTriangleReferences, supportMaxTrianglesPerBin, runtimeSupportReferences, runtimeSupportMaxTrianglesPerBin;
            public int overlapSupportSamples, changedTopSurfaceSamples;
            public int referenceSupportMissing, runtimeSupportMissing;
            public int rawBoundarySamples, rawBoundaryOutsideSource, rawBoundaryReferenceMissing, rawBoundaryRuntimeMissing, rawBoundaryOutsideBothMissing, rawBoundaryMismatches;
            public float maximumGeneratedProbeInset, maximumRawBoundaryError;
            public float legacyFirstNonzeroMaxError, originalWinnerMaxError;
            public int supportEdgeFallbackSamples; public float maximumSupportEdgeDistance, actualRuntimeSupportBinSize;
            public double preparationMilliseconds;
            public float maxDelta, protectedMaxDelta, maxFoliageSupportError;
            public bool protectedVerticesExact, protectedFacesExact, sourceXZAndTopologyPreserved, foliageSupportWithinTolerance;
            public bool navigationTraversalVerified, persistentHeightCachesUpdated, wasDirty, isDirty;
            public MeshRow[] meshes; public ControlRow[] controls; public StaticPlantRow[] staticPlants; public StageTiming[] timings; public string[] findings;
            public SupportIssue[] worstSupportErrors, missingSupportDetails, overlapSupportDetails, legacySupportDifferences;
            public SupportIssue[] rawBoundaryDetails;
        }
        sealed class Source
        {
            public MeshFilter filter; public MeshCollider collider; public Mesh original, derived;
            public Matrix4x4 matrix; public Vector3[] world; public int[][] indices;
            public MeshRow row;
            public readonly HashSet<int> protectedVertices = new HashSet<int>();
        }
        struct Triangle { public Vector3 a, b, c; public Vector3 delta; public int source, submesh, triangle; }
        struct SurfaceProbe
        {
            public bool hit; public int hits, beforeTriangle, afterTriangle;
            public double before, after, minimumBefore;
            public float delta, legacyDelta, originalWinnerDelta;
            public bool usedEdgeTolerance; public float maximumEdgeDistance;
        }
        struct Segment { public Vector2 a, b; public float radius; }
        sealed class Crest
        {
            public string id; public Vector3[] points; public float[] arc; public float length;
            public ControlRow[] controls; public readonly Dictionary<long, List<int>> bins = new Dictionary<long, List<int>>();
        }
        sealed class ProtectionBucket
        { public readonly List<Rect> rectangles = new List<Rect>(); public readonly List<Segment> segments = new List<Segment>(); }
        sealed class SheetBinding { public WorldMacroDressingRenderer target; public WorldMacroDressingSheetSO original, clone; }
        sealed class EarlyBinding { public EarlyRegionFoliage target; public EarlyRegionFoliage.Packet[] original; }
        sealed class StaticBinding
        { public Transform target; public Vector3 localPosition, worldPosition; public Renderer[] renderers; public StaticPlantRow row; }
        sealed class State
        {
            public Scene scene; public Settings settings; public Rect domain; public ToeSettings toe;
            public int nx, nz; public float dx, dz; public float[] heights, offsets; public bool[] valid, protectedNodes;
            public WorldMacroSurfaceDeformationSO meshField, foliageField;
            public readonly List<Source> sources = new List<Source>();
            public readonly List<Rect> rectangles = new List<Rect>();
            public readonly List<Segment> roads = new List<Segment>(), rivers = new List<Segment>();
            public readonly List<Crest> crests = new List<Crest>();
            public readonly List<Triangle> triangles = new List<Triangle>();
            public readonly Dictionary<long, List<int>> triangleBins = new Dictionary<long, List<int>>();
            public readonly List<SheetBinding> sheets = new List<SheetBinding>();
            public readonly List<EarlyBinding> early = new List<EarlyBinding>();
            public readonly List<StaticBinding> staticPlants = new List<StaticBinding>();
            public readonly List<StaticPlantRow> staticAudit = new List<StaticPlantRow>();
            public readonly HashSet<Renderer> movableRenderers = new HashSet<Renderer>();
            public readonly List<StageTiming> timings = new List<StageTiming>();
            public readonly List<SupportIssue> worstSupport = new List<SupportIssue>(), missingSupport = new List<SupportIssue>(), overlapSupport = new List<SupportIssue>(), legacySupport = new List<SupportIssue>();
            public readonly List<SupportIssue> boundarySupport = new List<SupportIssue>();
            public Receipt receipt; public bool applied;
        }
        static State prepared;
        public static Receipt LastReceipt { get; private set; }
        public static bool IsActive => prepared != null && prepared.applied;
        public static WorldMacroSurfaceDeformationSO Deformation => prepared?.foliageField;
        public static string ReceiptJson => LastReceipt == null ? "{}" : JsonUtility.ToJson(LastReceipt, true);

        /// <summary>Creates only owned, unsaved derivatives; does not bind them to the scene.</summary>
        public static string Prepare(Settings settings = null) => PrepareInternal(settings, null);

        static string PrepareInternal(Settings settings, ToeSettings toe)
        {
            Guard();
            if (IsActive) throw new InvalidOperationException("End the current mountain preview before Prepare.");
            Discard();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || !scene.path.EndsWith("/W_Demo_Compact.unity", StringComparison.Ordinal))
                throw new InvalidOperationException("The active compact Edit scene is required.");
            var s = settings == null ? new Settings() : JsonUtility.FromJson<Settings>(JsonUtility.ToJson(settings));
            if (s.Patch.width <= 0 || s.Patch.height <= 0 || s.Feather < 32 || s.GridSpacing < 2 || s.GridSpacing > 16 ||
                s.FoliageGridSpacing < 1 || s.FoliageGridSpacing > 4 || s.FoliageTriangleBinSize < 4 || s.FoliageTriangleBinSize > 64 || s.DeltaLimit <= 0 || s.DeltaLimit > 120 ||
                s.ProtectionFeather < 16 || s.NearBlur < 0 || s.BroadBlur < s.NearBlur || s.SupportTolerance <= 0)
                throw new ArgumentException("Invalid bounded form settings.");
            var state = new State { scene = scene, settings = s, toe = toe, domain = Expand(s.Patch, s.Feather + s.BroadBlur + 16) };
            state.nx = Mathf.CeilToInt(state.domain.width / s.GridSpacing) + 1;
            state.nz = Mathf.CeilToInt(state.domain.height / s.GridSpacing) + 1;
            if ((long)state.nx * state.nz > 1000000) throw new InvalidOperationException("Prototype height grid exceeds one million nodes.");
            state.dx = state.domain.width / (state.nx - 1); state.dz = state.domain.height / (state.nz - 1);
            state.receipt = new Receipt { utc = DateTime.UtcNow.ToString("o"), scene = scene.path, phase = "preparing",
                settings = s, fieldRect = state.domain, gridX = state.nx, gridZ = state.nz, wasDirty = scene.isDirty };
            prepared = state; LastReceipt = state.receipt;
            var preparationTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var geography = AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(s.Geography);
                if (geography == null || geography.CompactRoadGrade == null) throw new InvalidOperationException("Mapped compact geography/road-grade protection is required.");
                Timed(state, "gather_sources", () => GatherSources(state));
                Timed(state, "classify_static_vegetation", () => ClassifyStaticVegetation(state));
                Timed(state, "gather_protection", () => GatherProtection(state, geography));
                Timed(state, "raster_original_surface", () => BuildHeightGrid(state));
                Timed(state, "protect_footprints_and_triangles", () => BuildProtection(state));
                Timed(state, "prepare_authored_crests", () => BuildCrests(state, geography));
                Timed(state, "build_height_offsets", () => BuildOffsets(state));
                Timed(state, "clone_deformed_meshes", () => BuildMeshes(state));
                Timed(state, "index_collider_triangles", () => BuildTriangleIndex(state));
                Timed(state, "prepare_static_root_offsets", () => PrepareStaticOffsets(state));
                Timed(state, "pack_runtime_exact_support", () => BuildFoliageField(state));
                Timed(state, "verify_exact_support", () => VerifySupport(state));
                state.receipt.meshes = state.sources.Select(v => v.row).ToArray();
                state.receipt.staticPlants = state.staticAudit.ToArray();
                state.receipt.sourceMeshes = state.sources.Count;
                state.receipt.changedMeshes = state.sources.Count(v => v.derived != null);
                state.receipt.changedVertices = state.sources.Sum(v => v.row.changedVertices);
                state.receipt.protectedVertices = state.sources.Sum(v => v.row.protectedVertices);
                state.receipt.protectedTriangles = state.sources.Sum(v => v.row.protectedTriangles);
                state.receipt.protectedMaxDelta = state.sources.Max(v => v.row.protectedMaxDelta);
                state.receipt.maxDelta = state.sources.Max(v => v.row.maxDelta);
                state.receipt.protectedVerticesExact = state.receipt.protectedMaxDelta == 0;
                state.receipt.protectedFacesExact = state.receipt.protectedVerticesExact;
                state.receipt.sourceXZAndTopologyPreserved = true;
                state.receipt.foliageSupportWithinTolerance = state.receipt.supportMissing == 0 && state.receipt.maxFoliageSupportError <= s.SupportTolerance;
                state.receipt.phase = "prepared_unsaved_prototype";
                state.receipt.isDirty = scene.isDirty;
                state.receipt.findings = new[] {
                    "Existing terrain/context mesh derivatives only; original source assets and all XZ/index/UV data retained.",
                    "Protected footprints freeze every intersecting source triangle, plus a soft exterior transition. Roads/buildings/caves/water/POIs remain fixed; proven natural static assemblies can follow terrain in world Y only.",
                    "Proposed peaks/saddles follow the named existing crest polylines; their amplitudes and radii are explicit authoring controls, not random mountains.",
                    "Foliage selection heights/slope/habitat remain original. Only final instance Y is offset using exact collider-terrain triangle delta; render-only Context is excluded from support queries.",
                    "Static assemblies require known Tree/Shrub/Grass prototype mesh provenance, a pure natural-renderer hierarchy, no gameplay/rigidbody components, and an actual terrain support hit. Ambiguous roots remain frozen with reasons recorded.",
                    "No native traffic/collision traversal or NavMesh/link audit has been performed. Persistent height/geography caches are NOT updated by this preview.",
                    "Rebake the transient ink geography field from the currently bound derived meshes after BeginPreview. EndPreview restores mesh/collider/sheet/packet references.",
                    "Generated interior support probes and raw boundary probes are distinct: raw reference misses=" + state.receipt.rawBoundaryReferenceMissing + ", runtime misses=" + state.receipt.rawBoundaryRuntimeMissing + ", both missing outside source=" + state.receipt.rawBoundaryOutsideBothMissing + ". Raw boundary details remain available for actual before/after collider audit; sampler and height tolerances are unchanged.",
                    state.receipt.foliageSupportWithinTolerance ? "Generated interior and in-domain raw boundary foliage support meets tolerance; recorded raw outside-source boundary misses are NOT claimed resolved. Full runtime population/traversal validation remains required." : "Foliage field support exceeds tolerance or is unresolved. Prototype visual review only; do not persist or claim grounding validation."
                };
                if (!state.receipt.protectedVerticesExact || state.receipt.changedMeshes == 0)
                    throw new InvalidOperationException("Protection invariant failed or the protected patch has no changed mesh vertices.");
                state.receipt.preparationMilliseconds = preparationTimer.Elapsed.TotalMilliseconds;
                return ReceiptJson;
            }
            catch { state.receipt.phase = "prepare_failed"; state.receipt.preparationMilliseconds = preparationTimer.Elapsed.TotalMilliseconds; Discard(); throw; }
        }

        static void Timed(State s, string name, Action action)
        {
            var row = new StageTiming { stage = name, status = "running" }; s.timings.Add(row);
            s.receipt.timings = s.timings.ToArray(); s.receipt.phase = "preparing:" + name;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try { action(); row.status = "complete"; }
            catch { row.status = "failed"; throw; }
            finally { row.milliseconds = timer.Elapsed.TotalMilliseconds; }
        }

        public static string BeginPreview()
        {
            Guard(); var s = prepared ?? throw new InvalidOperationException("Prepare a form study first.");
            if (s.applied) throw new InvalidOperationException("Mountain preview is already active.");
            if (SceneManager.GetActiveScene() != s.scene) throw new InvalidOperationException("The prepared scene is no longer active.");
            foreach (var source in s.sources)
                if (source.filter == null || source.filter.sharedMesh != source.original || source.filter.transform.localToWorldMatrix != source.matrix ||
                    (source.collider != null && source.collider.sharedMesh != source.original))
                    throw new InvalidOperationException("Prepared mesh/transform/collider changed: " + source.row.path);
            foreach (var plant in s.staticPlants)
                if (plant.target == null || plant.target.localPosition != plant.localPosition || plant.target.position != plant.worldPosition)
                    throw new InvalidOperationException("Prepared static assembly moved: " + plant.row.path);
            s.applied = true; Subscribe();
            try
            {
                s.receipt.earlyPackets = s.receipt.earlyMatrices = s.receipt.foliageSheets = 0;
                foreach (var source in s.sources.Where(v => v.derived != null))
                {
                    source.filter.sharedMesh = source.derived;
                    if (source.collider != null) source.collider.sharedMesh = source.derived;
                }
                foreach (var plant in s.staticPlants)
                    if (plant.row.delta != 0) plant.target.position = plant.worldPosition + Vector3.up * plant.row.delta;
                foreach (var target in s.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true)))
                {
                    if (target.Sheet == null) continue;
                    if (target.Sheet.SurfaceDeformation != null) throw new InvalidOperationException("An existing surface deformation must be composed explicitly, not silently overwritten.");
                    var clone = Object.Instantiate(target.Sheet); clone.hideFlags = HideFlags.HideAndDontSave;
                    clone.name = target.Sheet.name + "_MountainFormPreview"; clone.SurfaceDeformation = s.foliageField;
                    s.sheets.Add(new SheetBinding { target = target, original = target.Sheet, clone = clone });
                    target.Sheet = clone; target.ResetCache();
                }
                foreach (var target in s.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EarlyRegionFoliage>(true)))
                {
                    var original = target.Packets;
                    if (original == null) continue;
                    var replacement = new EarlyRegionFoliage.Packet[original.Length];
                    for (int i = 0; i < original.Length; i++)
                    {
                        var p = original[i]; if (p == null) continue;
                        Bounds bounds = p.Bounds; bounds.Expand(new Vector3(0, s.settings.DeltaLimit * 2, 0));
                        replacement[i] = new EarlyRegionFoliage.Packet { Bounds = bounds, Count = p.Count, Distance = p.Distance,
                            Near = CloneParts(s, p.Near), Far = CloneParts(s, p.Far) };
                        s.receipt.earlyPackets++;
                    }
                    s.early.Add(new EarlyBinding { target = target, original = original }); target.Packets = replacement;
                }
                Physics.SyncTransforms();
                s.receipt.foliageSheets = s.sheets.Count; s.receipt.phase = "preview_active_unsaved"; s.receipt.isDirty = s.scene.isDirty;
                EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
                return ReceiptJson;
            }
            catch { EndPreview(); throw; }
        }

        /// <summary>Restore all independent references even when another restoration throws.</summary>
        public static string EndPreview()
        {
            var s = prepared; if (s == null || !s.applied) return "Mountain preview already restored.";
            var errors = new List<string>(); Unsubscribe();
            foreach (var plant in s.staticPlants) Attempt(() => { if (plant.target != null) plant.target.localPosition = plant.localPosition; }, errors);
            foreach (var binding in s.early) Attempt(() => { if (binding.target != null) binding.target.Packets = binding.original; }, errors);
            foreach (var binding in s.sheets) Attempt(() => { if (binding.target != null) binding.target.Sheet = binding.original; }, errors);
            foreach (var source in s.sources)
            {
                Attempt(() => { if (source.filter != null) source.filter.sharedMesh = source.original; }, errors);
                Attempt(() => { if (source.collider != null) source.collider.sharedMesh = source.original; }, errors);
            }
            foreach (var binding in s.sheets) Attempt(() => { if (binding.target != null) binding.target.ResetCache(); }, errors);
            Attempt(Physics.SyncTransforms, errors);
            foreach (var source in s.sources)
                if ((source.filter != null && source.filter.sharedMesh != source.original) || (source.collider != null && source.collider.sharedMesh != source.original))
                    errors.Add("Mesh/collider binding restoration mismatch: " + source.row.path);
            foreach (var binding in s.sheets) if (binding.target != null && binding.target.Sheet != binding.original) errors.Add("Dressing sheet restoration mismatch.");
            foreach (var binding in s.early) if (binding.target != null && binding.target.Packets != binding.original) errors.Add("Supplemental packet restoration mismatch.");
            foreach (var plant in s.staticPlants) if (plant.target != null && plant.target.localPosition != plant.localPosition) errors.Add("Static assembly restoration mismatch: " + plant.row.path);
            if (errors.Count != 0)
            {
                // Keep all originals and owned clones alive for another EndPreview attempt.
                // Never destroy a derived object that could still be bound after a failure.
                s.receipt.phase = "restore_failed_retry_available"; Subscribe();
                throw new InvalidOperationException("Mountain restoration: " + string.Join("; ", errors));
            }
            foreach (var binding in s.sheets) Attempt(() => { if (binding.clone != null) Object.DestroyImmediate(binding.clone); }, errors);
            s.sheets.Clear(); s.early.Clear(); s.applied = false;
            s.receipt.phase = errors.Count == 0 ? "preview_restored" : "restored_clone_cleanup_failed"; s.receipt.isDirty = s.scene.IsValid() && s.scene.isDirty;
            EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
            if (errors.Count != 0) throw new InvalidOperationException("Mountain restoration: " + string.Join("; ", errors));
            return ReceiptJson;
        }

        public static void Discard()
        {
            if (prepared == null) return;
            if (prepared.applied) EndPreview();
            foreach (var source in prepared.sources) if (source.derived != null) Object.DestroyImmediate(source.derived);
            if (prepared.meshField != null) Object.DestroyImmediate(prepared.meshField);
            if (prepared.foliageField != null) Object.DestroyImmediate(prepared.foliageField);
            prepared = null;
        }
        public static bool TrySurface(float x, float z, out float before, out float after)
        {
            before = after = 0; if (prepared == null || !SampleTriangle(prepared, x, z, out before, out float delta)) return false;
            after = before + delta; return true;
        }

        static void GatherSources(State s)
        {
            foreach (var f in s.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)))
            {
                string path = Hierarchy(f.transform); bool terrain = path.StartsWith(TerrainRoot, StringComparison.Ordinal), context = path.StartsWith(ContextRoot, StringComparison.Ordinal);
                if (!terrain && !context) continue;
                var r = f.GetComponent<MeshRenderer>(); if (!f.gameObject.activeInHierarchy || r == null || !r.enabled || !Intersects(s.domain, r.bounds)) continue;
                var mesh = f.sharedMesh; if (mesh == null || !mesh.isReadable) throw new InvalidOperationException("Readable source mesh required: " + path);
                var collider = f.GetComponent<MeshCollider>();
                if ((terrain && (collider == null || !collider.enabled || collider.sharedMesh != mesh || collider.convex || collider.isTrigger)) || (context && collider != null))
                    throw new InvalidOperationException("Unexpected terrain/context collider contract: " + path);
                var source = new Source { filter = f, collider = collider, original = mesh, matrix = f.transform.localToWorldMatrix,
                    world = mesh.vertices, indices = new int[mesh.subMeshCount][], row = new MeshRow { path = path, source = AssetDatabase.GetAssetPath(mesh),
                        vertices = mesh.vertexCount, collider = collider != null, context = context } };
                for (int i = 0; i < source.world.Length; i++) source.world[i] = source.matrix.MultiplyPoint3x4(source.world[i]);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("Non-triangular terrain submesh.");
                    source.indices[sub] = mesh.GetTriangles(sub); source.row.triangles += source.indices[sub].Length / 3;
                }
                s.sources.Add(source);
            }
            if (s.sources.Count == 0) throw new InvalidOperationException("No actual terrain/context mesh intersects the prototype domain.");
        }

        static void GatherProtection(State s, WorldMacroSheetSO geography)
        {
            var grade = geography.CompactRoadGrade; grade.Validate();
            foreach (var rect in grade.Protected) if (rect.Overlaps(s.domain)) s.rectangles.Add(rect);
            foreach (var road in grade.Lines) AddSegments(s.roads, road.Points, road.Width * .5f + grade.RoadbedPadding + grade.RoadbedTransition, s.domain);
            foreach (var river in geography.Rivers) AddSegments(s.rivers, river.Points, river.Width * .5f + s.settings.RiverPadding, s.domain);
            foreach (var r in s.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !Intersects(s.domain, r.bounds)) continue;
                string path = Hierarchy(r.transform);
                bool explicitSupport = ExplicitSupport(path);
                bool staticVegetation = HasNaturalMaterial(r);
                if (!explicitSupport && !staticVegetation) continue;
                if (!explicitSupport && s.movableRenderers.Contains(r)) continue;
                var b = r.bounds;
                s.rectangles.Add(new Rect(b.min.x - 2, b.min.z - 2, b.size.x + 4, b.size.z + 4));
                if (staticVegetation) s.receipt.staticVegetationProtected++;
            }
            s.receipt.protectedRectangles = s.rectangles.Count; s.receipt.roadSegments = s.roads.Count; s.receipt.riverSegments = s.rivers.Count;
        }

        /// <summary>Expanded Cheongrim view study, still a bounded derivative, for explicit review.</summary>
        public static Settings CheongrimReviewSettings()
        {
            var s = new Settings { Patch = new Rect(-400, -200, 2200, 2800), Feather = 220, DeltaLimit = 90, PeakAmplitude = 72, SaddleAmplitude = 90 };
            s.Controls = s.Controls.Concat(new[] {
                new RidgeControl { ridge="Cheongrim_EasternSpine", along=.12f, alongRadius=250, crossRadius=330 },
                new RidgeControl { ridge="Cheongrim_EasternSpine", along=.32f, saddle=true, alongRadius=190, crossRadius=360 },
                new RidgeControl { ridge="Cheongrim_EasternSpine", along=.52f, alongRadius=260, crossRadius=350 },
                new RidgeControl { ridge="Cheongrim_EasternSpine", along=.72f, saddle=true, alongRadius=200, crossRadius=320 },
                new RidgeControl { ridge="Bukhan_MainDivide", along=.28f, alongRadius=250, crossRadius=330 },
                new RidgeControl { ridge="Bukhan_MainDivide", along=.43f, saddle=true, alongRadius=180, crossRadius=300 }
            }).ToArray();
            return s;
        }

        static void ClassifyStaticVegetation(State s)
        {
            if (!s.settings.ReprojectStaticVegetation) return;
            var roots = s.scene.GetRootGameObjects();
            var known = new HashSet<Mesh>();
            var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dressing in roots.SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true)))
                if (dressing.Sheet != null) foreach (var p in dressing.Sheet.Prototypes)
                {
                    if (p == null || (p.Category != WorldMacroDressingSheetSO.Kind.Tree && p.Category != WorldMacroDressingSheetSO.Kind.Shrub && p.Category != WorldMacroDressingSheetSO.Kind.Grass)) continue;
                    if (!string.IsNullOrEmpty(p.SourcePath)) sourcePaths.Add(p.SourcePath);
                    foreach (var lod in p.Lods) if (lod != null) foreach (var part in lod.Parts) if (part?.Mesh != null) known.Add(part.Mesh);
                }
            // Static wind/LOD baking created derivative meshes. The corridor's owned
            // placement -> source record -> prototype source path is stronger provenance
            // than a name substring and remains valid when Mesh references differ.
            var provenCorridor = new HashSet<string>(StringComparer.Ordinal);
            var corridor = AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>("Assets/_Project/Art/World/WorldCompact/Data/07_VisualCorridor.asset");
            if (corridor != null) foreach (var placement in corridor.Placements)
            {
                if (placement.Group != "02_MountainAndForest") continue;
                var record = corridor.Sources.FirstOrDefault(v => v.Id == placement.SourceId);
                if (record != null && sourcePaths.Contains(record.SourcePath))
                    provenCorridor.Add("Playtest_VisualCorridor/" + placement.Group + "/" + placement.Id);
            }
            var considered = new HashSet<Transform>();
            foreach (var renderer in roots.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !Intersects(s.domain, renderer.bounds) || !HasNaturalMaterial(renderer)) continue;
                if (s.movableRenderers.Contains(renderer)) continue;
                string path = Hierarchy(renderer.transform);
                if (ExplicitSupport(path)) continue;
                var lod = renderer.GetComponentInParent<LODGroup>();
                var prefab = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                Transform candidate = lod != null ? lod.transform : prefab != null ? prefab.transform : renderer.transform;
                if (!considered.Add(candidate)) continue;
                string reason = null;
                var members = candidate.GetComponentsInChildren<Renderer>(true);
                var filters = candidate.GetComponentsInChildren<MeshFilter>(true);
                bool corridorProof = provenCorridor.Contains(Hierarchy(candidate));
                string prefabPath = prefab != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefab) : null;
                bool prefabProof = prefabPath != null && sourcePaths.Contains(prefabPath);
                if (ExplicitSupport(Hierarchy(candidate))) reason = "explicit content/landmark/structure hierarchy";
                else if (members.Length == 0 || members.Any(r => !(r is MeshRenderer) || !NaturalMaterial(r))) reason = "mixed or non-natural renderer hierarchy";
                else if (filters.Length != members.Length || filters.Any(f => f.sharedMesh == null) || (!corridorProof && !prefabProof && filters.Any(f => !known.Contains(f.sharedMesh)))) reason = "not proven by Tree/Shrub/Grass mesh, source prefab, or corridor placement record";
                else if (candidate.GetComponentsInChildren<Component>(true).Any(c => c == null || !(c is Transform || c is MeshRenderer || c is MeshFilter || c is LODGroup || c is Collider))) reason = "gameplay/rigidbody/unknown component in assembly";
                else if (candidate.parent != null && candidate.parent.GetComponentsInParent<Collider>(true).Length != 0) reason = "collider outside candidate assembly";
                else if (lod == null && prefab == null && (candidate.childCount != 0 || (candidate.parent != null && candidate.parent.GetComponents<LODGroup>().Length != 0))) reason = "unproven unpacked multi-part assembly";
                else if (lod == null && prefab == null && candidate.parent != null && candidate.parent.GetComponentsInChildren<Renderer>(true).Any(r => r != renderer && Vector2.Distance(XZ(r.transform.position), XZ(candidate.position)) < 1)) reason = "coincident sibling renderer may be another part of the same assembly";
                else if (!OriginalTerrainSupport(s, candidate.position)) reason = "no actual original terrain collider support at root XZ";
                var row = new StaticPlantRow { path = Hierarchy(candidate), status = reason == null ? "natural_y_reprojection_prepared" : "ambiguous_preserved",
                    reason = reason ?? (corridorProof ? "owned corridor placement/source path -> natural prototype, pure LOD assembly" : prefabProof ? "source prefab path -> natural prototype, pure assembly" : lod != null ? "LODGroup + prototype meshes + pure natural components" : prefab != null ? "prefab root + prototype meshes + pure natural components" : "single renderer leaf + prototype mesh + no parent collider/nearby sibling"),
                    renderers = members.Length, colliders = candidate.GetComponentsInChildren<Collider>(true).Length, originalPosition = candidate.position };
                s.staticAudit.Add(row);
                if (reason != null) { s.receipt.ambiguousStaticRenderers += members.Length; continue; }
                // Never move both an ancestor and a child. A pure LOD/prefab candidate
                // owns all of its renderer/collider components as one authored plant.
                if (s.staticPlants.Any(p => candidate.IsChildOf(p.target) || p.target.IsChildOf(candidate)))
                { row.status = "ambiguous_preserved"; row.reason = "overlapping candidate hierarchy"; s.receipt.ambiguousStaticRenderers += members.Length; continue; }
                s.staticPlants.Add(new StaticBinding { target = candidate, localPosition = candidate.localPosition, worldPosition = candidate.position, renderers = members, row = row });
                foreach (var member in members) s.movableRenderers.Add(member);
            }
            s.receipt.naturalStaticRoots = s.staticPlants.Count;
        }
        static bool OriginalTerrainSupport(State s, Vector3 root)
        {
            var ray = new Ray(new Vector3(root.x, 3000, root.z), Vector3.down);
            return s.sources.Any(source => source.collider != null && source.collider.Raycast(ray, out _, 6000));
        }
        static void PrepareStaticOffsets(State s)
        {
            foreach (var plant in s.staticPlants)
            {
                if (!SampleTriangle(s, plant.worldPosition.x, plant.worldPosition.z, out _, out float delta))
                    throw new InvalidOperationException("Prepared natural static root has no exact collider triangle: " + plant.row.path);
                plant.row.delta = delta;
                if (delta != 0) s.receipt.movedStaticRoots++;
            }
        }
        static bool NaturalMaterial(Renderer r) => r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.shader != null &&
            (m.shader.name.IndexOf("Vegetation", StringComparison.OrdinalIgnoreCase) >= 0 || m.shader.name.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0));
        static bool HasNaturalMaterial(Renderer r) => r.sharedMaterials.Any(m => m != null && m.shader != null &&
            (m.shader.name.IndexOf("Vegetation", StringComparison.OrdinalIgnoreCase) >= 0 || m.shader.name.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0));
        static bool ExplicitSupport(string path) =>
            path.StartsWith("Playtest_NaturalCave/", StringComparison.Ordinal) || path.StartsWith("Playtest_OriginalC2Inn/", StringComparison.Ordinal) ||
            path.StartsWith("Playtest_Village_Office/", StringComparison.Ordinal) || path.StartsWith("WorldMacro_AuthoredGeography/03_SettlementAndLandmark_Massing/", StringComparison.Ordinal) ||
            path.StartsWith("WorldMacro_Landmarks_Authored/", StringComparison.Ordinal) || path.StartsWith("WorldMacro_Content_Layout/", StringComparison.Ordinal);
        static void AddSegments(List<Segment> output, Vector3[] points, float radius, Rect domain)
        {
            if (points == null) return;
            for (int i = 1; i < points.Length; i++)
            {
                var a = XZ(points[i - 1]); var b = XZ(points[i]);
                if (!Expand(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), radius).Overlaps(domain)) continue;
                output.Add(new Segment { a = a, b = b, radius = radius });
            }
        }
        static float ProtectionDistance(State s, Vector2 p)
        {
            float d = float.PositiveInfinity;
            foreach (var rect in s.rectangles) d = Mathf.Min(d, Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, rect.xMin, rect.xMax), Mathf.Clamp(p.y, rect.yMin, rect.yMax))));
            foreach (var line in s.roads) d = Mathf.Min(d, SegmentDistance(p, line.a, line.b, out _) - line.radius);
            foreach (var line in s.rivers) d = Mathf.Min(d, SegmentDistance(p, line.a, line.b, out _) - line.radius);
            return Mathf.Max(0, d);
        }
        static Dictionary<long, ProtectionBucket> IndexProtection(State s, float guard)
        {
            var bins = new Dictionary<long, ProtectionBucket>();
            foreach (var rect in s.rectangles)
                VisitSpatialBins(Expand(rect, guard + .01f), s.domain, key =>
                { if (!bins.TryGetValue(key, out var bucket)) bins[key] = bucket = new ProtectionBucket(); bucket.rectangles.Add(rect); });
            foreach (var line in s.roads.Concat(s.rivers))
            {
                var bounds = Rect.MinMaxRect(Mathf.Min(line.a.x, line.b.x), Mathf.Min(line.a.y, line.b.y), Mathf.Max(line.a.x, line.b.x), Mathf.Max(line.a.y, line.b.y));
                VisitSpatialBins(Expand(bounds, line.radius + guard + .01f), s.domain, key =>
                { if (!bins.TryGetValue(key, out var bucket)) bins[key] = bucket = new ProtectionBucket(); bucket.segments.Add(line); });
            }
            if (s.receipt != null)
            {
                s.receipt.protectionBins = bins.Count;
                s.receipt.protectionReferences = bins.Values.Sum(v => v.rectangles.Count + v.segments.Count);
                s.receipt.protectionMaxCandidates = bins.Count == 0 ? 0 : bins.Values.Max(v => v.rectangles.Count + v.segments.Count);
            }
            return bins;
        }
        static bool ProtectedWithin(Dictionary<long, ProtectionBucket> bins, Vector2 p, float guard)
        {
            if (!bins.TryGetValue(SpatialKey(p), out var bucket)) return false;
            // Keep the exact original distance expressions and <= threshold. The bin
            // only removes primitives whose expanded bounds cannot reach this point.
            foreach (var rect in bucket.rectangles)
                if (Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, rect.xMin, rect.xMax), Mathf.Clamp(p.y, rect.yMin, rect.yMax))) <= guard) return true;
            foreach (var line in bucket.segments)
                if (SegmentDistance(p, line.a, line.b, out _) - line.radius <= guard) return true;
            return false;
        }
        static void VisitSpatialBins(Rect bounds, Rect domain, Action<long> visit)
        {
            float minX = Mathf.Max(bounds.xMin, domain.xMin), maxX = Mathf.Min(bounds.xMax, domain.xMax);
            float minZ = Mathf.Max(bounds.yMin, domain.yMin), maxZ = Mathf.Min(bounds.yMax, domain.yMax);
            if (minX > maxX || minZ > maxZ) return;
            for (int z = Mathf.FloorToInt(minZ / SpatialBinSize); z <= Mathf.FloorToInt(maxZ / SpatialBinSize); z++)
                for (int x = Mathf.FloorToInt(minX / SpatialBinSize); x <= Mathf.FloorToInt(maxX / SpatialBinSize); x++) visit(Key(x, z));
        }
        static long SpatialKey(Vector2 p) => Key(Mathf.FloorToInt(p.x / SpatialBinSize), Mathf.FloorToInt(p.y / SpatialBinSize));
        static void BuildHeightGrid(State s)
        {
            s.heights = Enumerable.Repeat(float.NegativeInfinity, s.nx * s.nz).ToArray();
            foreach (var source in s.sources) ForTriangles(source, (a, b, c) => Raster(s.domain, s.nx, s.nz, a, b, c, (i, u, v, w) =>
            { float h = a.y * u + b.y * v + c.y * w; if (h > s.heights[i]) s.heights[i] = h; }));
            s.valid = s.heights.Select(v => !float.IsNegativeInfinity(v)).ToArray();
        }
        static void BuildProtection(State s)
        {
            int n = s.nx * s.nz; var baseMask = new bool[n]; s.protectedNodes = new bool[n];
            float guard = Mathf.Sqrt(s.dx * s.dx + s.dz * s.dz) * 1.01f;
            var bins = IndexProtection(s, guard);
            for (int z = 0; z < s.nz; z++) for (int x = 0; x < s.nx; x++)
                baseMask[z * s.nx + x] = ProtectedWithin(bins, GridPoint(s, x, z), guard);
            Array.Copy(baseMask, s.protectedNodes, n);
            int[] integral = Integral(baseMask, s.nx, s.nz);
            foreach (var source in s.sources) foreach (var indices in source.indices) for (int tri = 0; tri < indices.Length; tri += 3)
            {
                var a = source.world[indices[tri]]; var b = source.world[indices[tri + 1]]; var c = source.world[indices[tri + 2]];
                if (!TriangleRect(a, b, c).Overlaps(s.domain)) continue;
                Indices(s.domain, s.nx, s.nz, Expand(TriangleRect(a, b, c), guard), out int x0, out int z0, out int x1, out int z1);
                if (Count(integral, s.nx, x0, z0, x1, z1) == 0) continue;
                source.row.protectedTriangles++;
                source.protectedVertices.Add(indices[tri]); source.protectedVertices.Add(indices[tri + 1]); source.protectedVertices.Add(indices[tri + 2]);
                // Freeze the entire triangle AABB + one grid cell, not just vertices falling
                // inside the footprint. Every point on a protected source face stays fixed.
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) s.protectedNodes[z * s.nx + x] = true;
            }
        }
        static void BuildCrests(State s, WorldMacroSheetSO geography)
        {
            var rows = new List<ControlRow>();
            foreach (string id in s.settings.Controls.Select(v => v.ridge).Distinct())
            {
                var source = geography.Ridges.FirstOrDefault(v => v.Id == id);
                if (source == null || source.Points.Length < 2) throw new InvalidOperationException("Missing authored ridge: " + id);
                var crest = new Crest { id = id, points = source.Points, arc = new float[source.Points.Length] };
                for (int i = 1; i < crest.arc.Length; i++) crest.arc[i] = crest.arc[i - 1] + Vector2.Distance(XZ(crest.points[i - 1]), XZ(crest.points[i]));
                crest.length = crest.arc[crest.arc.Length - 1]; s.crests.Add(crest);
                foreach (var control in s.settings.Controls.Where(v => v.ridge == id))
                {
                    if (control.along < 0 || control.along > 1 || control.alongRadius < 40 || control.crossRadius < 40 || control.gain < 0)
                        throw new InvalidOperationException("Invalid ridge control: " + id);
                    float distance = control.along * crest.length; Vector3 anchor = crest.points[crest.points.Length - 1];
                    for (int i = 1; i < crest.arc.Length; i++) if (crest.arc[i] >= distance)
                    { anchor = Vector3.Lerp(crest.points[i - 1], crest.points[i], Mathf.InverseLerp(crest.arc[i - 1], crest.arc[i], distance)); break; }
                    anchor.y = SampleArray(s, s.heights, anchor.x, anchor.z, out bool valid);
                    if (!valid) anchor.y = 0;
                    rows.Add(new ControlRow { ridge = id, saddle = control.saddle, surfaceSampled = valid, alongMetres = distance,
                        amplitude = control.gain * (control.saddle ? -s.settings.SaddleAmplitude : s.settings.PeakAmplitude),
                        alongRadius = control.alongRadius, crossRadius = control.crossRadius, actualSurfaceAnchor = anchor });
                }
            }
            s.receipt.controls = rows.ToArray();
            foreach (var crest in s.crests)
            {
                crest.controls = rows.Where(v => v.ridge == crest.id).ToArray();
                IndexCrest(crest, s.domain);
            }
            s.receipt.crestBins = s.crests.Sum(v => v.bins.Count);
            s.receipt.crestReferences = s.crests.Sum(v => v.bins.Values.Sum(list => list.Count));
        }
        static void IndexCrest(Crest crest, Rect domain)
        {
            float radius = crest.controls.Max(v => v.crossRadius) + .01f;
            for (int i = 1; i < crest.points.Length; i++)
            {
                int index = i; var a = XZ(crest.points[i - 1]); var b = XZ(crest.points[i]);
                var bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                VisitSpatialBins(Expand(bounds, radius), domain, key =>
                { if (!crest.bins.TryGetValue(key, out var list)) crest.bins[key] = list = new List<int>(); list.Add(index); });
            }
        }
        static float CrestForm(Crest crest, Vector2 p, float accumulated)
        {
            if (!crest.bins.TryGetValue(SpatialKey(p), out var candidates)) return accumulated;
            float best = float.PositiveInfinity, along = 0;
            // Candidates retain original segment order, preserving nearest-segment tie
            // behaviour and the exact floating-point operations of the full scan.
            foreach (int i in candidates)
            {
                float d = SegmentDistance(p, XZ(crest.points[i - 1]), XZ(crest.points[i]), out float t);
                if (d < best) { best = d; along = Mathf.Lerp(crest.arc[i - 1], crest.arc[i], t); }
            }
            float form = accumulated;
            foreach (var control in crest.controls)
                form += control.amplitude * Bell((along - control.alongMetres) / control.alongRadius) * Bell(best / control.crossRadius);
            return form;
        }
        static void BuildOffsets(State s)
        {
            var near = Blur(s.heights, s.valid, s.nx, s.nz, Mathf.CeilToInt(s.settings.NearBlur / s.dx), Mathf.CeilToInt(s.settings.NearBlur / s.dz));
            var broad = Blur(s.heights, s.valid, s.nx, s.nz, Mathf.CeilToInt(s.settings.BroadBlur / s.dx), Mathf.CeilToInt(s.settings.BroadBlur / s.dz));
            // Smooth the hard exclusion outward; its zero core remains exact.
            var protectedFloat = s.protectedNodes.Select(v => v ? 1f : 0f).ToArray();
            var valid = Enumerable.Repeat(true, protectedFloat.Length).ToArray();
            var protection = Blur(protectedFloat, valid, s.nx, s.nz, Mathf.CeilToInt(s.settings.ProtectionFeather / s.dx), Mathf.CeilToInt(s.settings.ProtectionFeather / s.dz));
            s.offsets = new float[s.heights.Length];
            for (int z = 0; z < s.nz; z++) for (int x = 0; x < s.nx; x++)
            {
                int at = z * s.nx + x; if (!s.valid[at] || s.protectedNodes[at]) continue;
                var p = GridPoint(s, x, z); float outside = OutsideRect(s.settings.Patch, p);
                float patch = 1 - Smooth(outside / s.settings.Feather); if (patch == 0) continue;
                float form = 0;
                foreach (var crest in s.crests)
                    form = CrestForm(crest, p, form);
                float smooth = (Mathf.Lerp(near[at], broad[at], .25f) - s.heights[at]) * s.settings.Smoothing;
                float protectFade = 1 - Smooth(Mathf.Clamp01(protection[at] * 2.5f));
                float mountain = Smooth((s.heights[at] - 110) / 100);
                s.offsets[at] = Mathf.Clamp(smooth + form, -s.settings.DeltaLimit, s.settings.DeltaLimit) * patch * protectFade * mountain;
            }
            if (s.toe != null) Timed(s, "remap_local_toe", () => ApplyToeOffsets(s, protection));
            s.meshField = NewField(s.domain, s.nx, s.nz, s.offsets, "MountainForm_MeshDelta");
        }
        static void BuildMeshes(State s)
        {
            foreach (var source in s.sources)
            {
                var vertices = source.original.vertices; var normals = source.original.normals;
                var inverse = source.matrix.inverse; var normalToWorld = inverse.transpose; var normalToLocal = source.matrix.transpose;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var p = source.world[i]; float delta = s.meshField.SampleDelta(p.x, p.z);
                    bool protect = source.protectedVertices.Contains(i) || ProtectedSample(s, p.x, p.z);
                    if (protect) { source.row.protectedVertices++; source.row.protectedMaxDelta = Mathf.Max(source.row.protectedMaxDelta, Mathf.Abs(delta)); }
                    if (delta == 0) continue;
                    // Multiplying the WORLD vertical vector preserves original local XZ when
                    // transforms are identity, and the full world XZ under any invertible basis.
                    vertices[i] += inverse.MultiplyVector(Vector3.up * delta);
                    source.row.changedVertices++; source.row.maxDelta = Mathf.Max(source.row.maxDelta, Mathf.Abs(delta));
                    if (normals.Length == vertices.Length)
                    {
                        float h = 1;
                        float dx = (s.meshField.SampleDelta(p.x + h, p.z) - s.meshField.SampleDelta(p.x - h, p.z)) / (2 * h);
                        float dz = (s.meshField.SampleDelta(p.x, p.z + h) - s.meshField.SampleDelta(p.x, p.z - h)) / (2 * h);
                        var n = normalToWorld.MultiplyVector(normals[i]).normalized;
                        normals[i] = normalToLocal.MultiplyVector(new Vector3(n.x - n.y * dx, n.y, n.z - n.y * dz).normalized).normalized;
                    }
                }
                if (source.row.changedVertices > 0)
                {
                    var clone = Object.Instantiate(source.original); source.derived = clone;
                    clone.name = source.original.name + "_MountainForm_Unsaved"; clone.hideFlags = HideFlags.HideAndDontSave;
                    clone.vertices = vertices; if (normals.Length == vertices.Length) clone.normals = normals; else clone.RecalculateNormals();
                    clone.RecalculateBounds(); if (clone.uv.Length == clone.vertexCount) clone.RecalculateTangents();
                }
                ForTriangles(source, (a, b, c) =>
                {
                    float before = Slope(a, b, c);
                    a.y += s.meshField.SampleDelta(a.x, a.z); b.y += s.meshField.SampleDelta(b.x, b.z); c.y += s.meshField.SampleDelta(c.x, c.z);
                    float after = Slope(a, b, c);
                    source.row.maxSlopeBefore = Mathf.Max(source.row.maxSlopeBefore, before); source.row.maxSlopeAfter = Mathf.Max(source.row.maxSlopeAfter, after);
                    if (before > 55) source.row.slopesAbove55Before++; if (after > 55) source.row.slopesAbove55After++;
                });
            }
        }

        static void BuildTriangleIndex(State s)
        {
            s.receipt.supportColliderMeshes = s.sources.Count(source => source.collider != null);
            s.receipt.visualOnlyContextMeshesExcluded = s.sources.Count(source => source.collider == null);
            // This index is physical support, not the visual paint-height field. A tall
            // render-only Context triangle must never lift plants off a lower collider.
            foreach (var source in s.sources.Where(source => source.collider != null))
            {
                int sourceIndex = s.sources.IndexOf(source);
                var final = source.derived != null ? source.derived.vertices : null;
                var actualDelta = new float[source.world.Length];
                if (final != null) for (int i = 0; i < final.Length; i++)
                {
                    var p = source.matrix.MultiplyPoint3x4(final[i]);
                    if (Mathf.Abs(p.x - source.world[i].x) > .0001f || Mathf.Abs(p.z - source.world[i].z) > .0001f)
                        throw new InvalidOperationException("Derived collider changed world XZ: " + source.row.path);
                    actualDelta[i] = p.y - source.world[i].y;
                }
                for (int submesh = 0; submesh < source.indices.Length; submesh++) for (int i = 0; i < source.indices[submesh].Length; i += 3)
                {
                    var indices = source.indices[submesh];
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    var a = source.world[ia]; var b = source.world[ib]; var c = source.world[ic];
                    var rect = TriangleRect(a, b, c); if (!rect.Overlaps(s.domain)) continue;
                    int index = s.triangles.Count;
                    s.triangles.Add(new Triangle { a = a, b = b, c = c, delta = new Vector3(actualDelta[ia], actualDelta[ib], actualDelta[ic]),
                        source = sourceIndex, submesh = submesh, triangle = i / 3 });
                    var indexedRect = Expand(rect, SupportEdgeTolerance);
                    for (int z = Mathf.FloorToInt(indexedRect.yMin / SupportBinSize); z <= Mathf.FloorToInt(indexedRect.yMax / SupportBinSize); z++)
                        for (int x = Mathf.FloorToInt(indexedRect.xMin / SupportBinSize); x <= Mathf.FloorToInt(indexedRect.xMax / SupportBinSize); x++)
                        { long key = Key(x, z); if (!s.triangleBins.TryGetValue(key, out var list)) s.triangleBins[key] = list = new List<int>(); list.Add(index); }
                }
            }
            s.receipt.supportTriangles = s.triangles.Count;
            s.receipt.supportTriangleReferences = s.triangleBins.Values.Sum(v => v.Count);
            s.receipt.supportMaxTrianglesPerBin = s.triangleBins.Count == 0 ? 0 : s.triangleBins.Values.Max(v => v.Count);
        }
        static void BuildFoliageField(State s)
        {
            if (s.triangles.Count == 0) throw new InvalidOperationException("No collider terrain support triangles in the prototype domain.");
            var vertices = new Vector3[checked(s.triangles.Count * 3)];
            var sourceHeights = new float[vertices.Length];
            for (int i = 0; i < s.triangles.Count; i++)
            {
                var t = s.triangles[i];
                vertices[i * 3] = new Vector3(t.a.x, t.delta.x, t.a.z);
                vertices[i * 3 + 1] = new Vector3(t.b.x, t.delta.y, t.b.z);
                vertices[i * 3 + 2] = new Vector3(t.c.x, t.delta.z, t.c.z);
                sourceHeights[i * 3] = t.a.y; sourceHeights[i * 3 + 1] = t.b.y; sourceHeights[i * 3 + 2] = t.c.y;
            }
            // Exact triangle mode supersedes the former 2 m / 1 m approximation, whose
            // largest measured grounding error was .329 m in the first 35 m prototype.
            // Include zero-delta faces so protected corridors never interpolate across a gap.
            s.foliageField = NewField(s.domain, 2, 2, new float[4], "MountainForm_FoliageExactColliderTriangleDelta");
            float binSize = s.settings.FoliageTriangleBinSize;
            while (true)
            {
                try { s.foliageField.SetTriangles(vertices, binSize, sourceHeights); break; }
                catch (ArgumentException e) when (binSize > 4 && e.Message.Contains("bounded sampling/index budget"))
                { binSize = Mathf.Max(4, binSize * .5f); }
            }
            s.receipt.actualRuntimeSupportBinSize = binSize;
            s.receipt.runtimeSupportReferences = s.foliageField.TriangleSurface.TriangleIndices.Length;
            s.receipt.runtimeSupportMaxTrianglesPerBin = s.foliageField.TriangleSurface.MaximumTrianglesPerBin;
            s.receipt.fieldInvalidNodes = 0;
            s.receipt.foliageSupportMode = "max final collider Y minus max original collider Y; double barycentric, zero-delta faces included; <=0.0001m world-edge fallback separately counted";
        }
        static void VerifySupport(State s)
        {
            s.receipt.supportSamples = s.receipt.supportMissing = 0; s.receipt.maxFoliageSupportError = 0;
            s.receipt.referenceSupportMissing = s.receipt.runtimeSupportMissing = 0;
            s.receipt.overlapSupportSamples = s.receipt.changedTopSurfaceSamples = 0;
            s.receipt.supportEdgeFallbackSamples = 0; s.receipt.maximumSupportEdgeDistance = 0;
            s.receipt.legacyFirstNonzeroMaxError = s.receipt.originalWinnerMaxError = 0;
            s.worstSupport.Clear(); s.missingSupport.Clear(); s.overlapSupport.Clear(); s.legacySupport.Clear();
            ResetBoundarySupport(s);
            foreach (var t in s.triangles)
            {
                if (Mathf.Max(Mathf.Abs(t.delta.x), Mathf.Max(Mathf.Abs(t.delta.y), Mathf.Abs(t.delta.z))) < .00001f) continue;
                CheckGeneratedSupport(s, t, 1.0 / 3, 1.0 / 3, 1.0 / 3, false);
                CheckGeneratedSupport(s, t, .5, .5, 0, true);
                CheckGeneratedSupport(s, t, 0, .5, .5, true);
                CheckGeneratedSupport(s, t, .5, 0, .5, true);
            }
            s.receipt.worstSupportErrors = s.worstSupport.ToArray(); s.receipt.missingSupportDetails = s.missingSupport.ToArray();
            s.receipt.overlapSupportDetails = s.overlapSupport.ToArray(); s.receipt.legacySupportDifferences = s.legacySupport.ToArray();
            s.receipt.rawBoundaryDetails = s.boundarySupport.ToArray();
        }
        static void CheckSupport(State s, Vector3 p)
        {
            s.receipt.supportSamples++;
            var probe = ProbeTriangles(s, p.x, p.z);
            bool runtimeHit = WorldMacroSurfaceDeformationSO.TrySampleTriangle(s.foliageField.TriangleSurface, p.x, p.z, out var runtime);
            float actual = runtimeHit ? runtime.Value : 0;
            if (!probe.hit || !runtimeHit)
            {
                s.receipt.supportMissing++;
                if (!probe.hit) s.receipt.referenceSupportMissing++;
                if (!runtimeHit) s.receipt.runtimeSupportMissing++;
                if (s.missingSupport.Count < 20) s.missingSupport.Add(SupportDetail(s, !probe.hit ? "missing_reference_surface" : "missing_runtime_surface", p, probe, actual));
                return;
            }
            float error = Mathf.Abs(actual - probe.delta), legacyError = Mathf.Abs(probe.legacyDelta - probe.delta);
            if (probe.usedEdgeTolerance) s.receipt.supportEdgeFallbackSamples++;
            s.receipt.maximumSupportEdgeDistance = Mathf.Max(s.receipt.maximumSupportEdgeDistance, probe.maximumEdgeDistance);
            s.receipt.maxFoliageSupportError = Mathf.Max(s.receipt.maxFoliageSupportError, error);
            s.receipt.legacyFirstNonzeroMaxError = Mathf.Max(s.receipt.legacyFirstNonzeroMaxError, legacyError);
            s.receipt.originalWinnerMaxError = Mathf.Max(s.receipt.originalWinnerMaxError, Mathf.Abs(probe.originalWinnerDelta - probe.delta));
            if (probe.beforeTriangle != probe.afterTriangle) s.receipt.changedTopSurfaceSamples++;
            float spread = (float)(probe.before - probe.minimumBefore);
            if (spread > .001f) s.receipt.overlapSupportSamples++;
            if (error > .00001f && WorthRecording(s.worstSupport, error)) KeepWorst(s.worstSupport, SupportDetail(s, "field_vs_physical_surface", p, probe, actual));
            if (legacyError > .001f && WorthRecording(s.legacySupport, legacyError))
            { var detail = SupportDetail(s, "legacy_first_nonzero_vs_physical_surface", p, probe, actual); detail.absoluteError = legacyError; KeepWorst(s.legacySupport, detail); }
            if (spread > .001f && s.overlapSupport.Count < 20) s.overlapSupport.Add(SupportDetail(s, "overlapping_collider_surfaces", p, probe, actual));
        }
        static bool SampleTriangle(State s, float x, float z, out float height, out float delta)
        {
            var probe = ProbeTriangles(s, x, z); height = probe.hit ? (float)probe.before : float.NegativeInfinity; delta = probe.delta; return probe.hit;
        }
        static SurfaceProbe ProbeTriangles(State s, float x, float z)
        {
            var result = new SurfaceProbe { before = double.NegativeInfinity, after = double.NegativeInfinity, minimumBefore = double.PositiveInfinity, beforeTriangle = -1, afterTriangle = -1 };
            if (!s.triangleBins.TryGetValue(Key(Mathf.FloorToInt(x / SupportBinSize), Mathf.FloorToInt(z / SupportBinSize)), out var list)) return result;
            bool legacyFound = false;
            bool exact = false;
            foreach (int index in list)
            {
                var t = s.triangles[index];
                if (!WorldMacroSurfaceDeformationSO.TryTriangleWeights(t.a, t.b, t.c, x, z, out var weights, SupportEdgeTolerance)) continue;
                bool edge = weights.UsedEdgeTolerance;
                if (edge && exact) continue;
                if (!edge && !exact)
                {
                    result = new SurfaceProbe { before = double.NegativeInfinity, after = double.NegativeInfinity, minimumBefore = double.PositiveInfinity, beforeTriangle = -1, afterTriangle = -1 };
                    legacyFound = false; exact = true;
                }
                double h = t.a.y * weights.A + t.b.y * weights.B + t.c.y * weights.C;
                double delta = t.delta.x * weights.A + t.delta.y * weights.B + t.delta.z * weights.C; double after = h + delta;
                result.hits++; result.minimumBefore = Math.Min(result.minimumBefore, h);
                result.usedEdgeTolerance = !exact; result.maximumEdgeDistance = Mathf.Max(result.maximumEdgeDistance, weights.OutsideDistance);
                if (!legacyFound && (t.delta.x != 0 || t.delta.y != 0 || t.delta.z != 0)) { result.legacyDelta = (float)delta; legacyFound = true; }
                if (h > result.before) { result.before = h; result.beforeTriangle = index; result.originalWinnerDelta = (float)delta; }
                if (after > result.after) { result.after = after; result.afterTriangle = index; }
            }
            result.hit = result.hits != 0; if (result.hit) result.delta = (float)(result.after - result.before); return result;
        }
        static bool WorthRecording(List<SupportIssue> rows, float error) => rows.Count < 20 || error > rows[rows.Count - 1].absoluteError;
        static void KeepWorst(List<SupportIssue> rows, SupportIssue row)
        { rows.Add(row); rows.Sort((a, b) => b.absoluteError.CompareTo(a.absoluteError)); if (rows.Count > 20) rows.RemoveAt(20); }
        static SupportIssue SupportDetail(State s, string kind, Vector3 p, SurfaceProbe probe, float actual)
        {
            bool runtimeHit = WorldMacroSurfaceDeformationSO.TrySampleTriangle(s.foliageField.TriangleSurface, p.x, p.z, out var runtime);
            var candidates = new List<SupportHit>();
            var nearby = new HashSet<int>();
            int bx = Mathf.FloorToInt(p.x / SupportBinSize), bz = Mathf.FloorToInt(p.z / SupportBinSize);
            // The expanded diagnostic search is bounded and runs only for recorded rows.
            for (int z = bz - 1; z <= bz + 1; z++) for (int x = bx - 1; x <= bx + 1; x++)
                if (s.triangleBins.TryGetValue(Key(x, z), out var list)) foreach (int index in list) nearby.Add(index);
            foreach (int index in nearby)
            {
                var t = s.triangles[index]; bool hit = WorldMacroSurfaceDeformationSO.TryTriangleWeights(t.a, t.b, t.c, p.x, p.z, out var weights, SupportEdgeTolerance);
                var q = new Vector3((float)weights.A, (float)weights.B, (float)weights.C);
                if (!hit && probe.hit) continue;
                if (!hit) Barycentric(t.a, t.b, t.c, p.x, p.z, out q);
                if (!hit && Mathf.Min(q.x, Mathf.Min(q.y, q.z)) < -.05f) continue;
                double before = (double)t.a.y * q.x + (double)t.b.y * q.y + (double)t.c.y * q.z;
                candidates.Add(new SupportHit { path = s.sources[t.source].row.path, source = t.source, submesh = t.submesh, triangle = t.triangle,
                    a = t.a, b = t.b, c = t.c, delta = t.delta, barycentric = q, originalHeight = before, finalHeight = before + Vector3.Dot(t.delta, q),
                    selectedBefore = index == probe.beforeTriangle, selectedAfter = index == probe.afterTriangle,
                    runtimeSelectedBefore = runtimeHit && index == runtime.BeforeTriangleIndex, runtimeSelectedAfter = runtimeHit && index == runtime.AfterTriangleIndex,
                    accepted = hit && (!probe.hit || probe.usedEdgeTolerance || !weights.UsedEdgeTolerance), edgeTolerance = weights.UsedEdgeTolerance });
            }
            var chosen = candidates.OrderByDescending(v => v.selectedBefore || v.selectedAfter || v.runtimeSelectedBefore || v.runtimeSelectedAfter).ThenByDescending(v => v.accepted).ThenByDescending(v => v.finalHeight).Take(8).ToArray();
            return new SupportIssue { kind = kind, position = p, referenceBefore = probe.hit ? (float)probe.before : 0, referenceAfter = probe.hit ? (float)probe.after : 0,
                referenceDelta = probe.delta, fieldDelta = actual, absoluteError = Mathf.Abs(probe.delta - actual), legacyFirstNonzeroDelta = probe.legacyDelta,
                originalWinnerDelta = probe.originalWinnerDelta, originalHeightSpread = probe.hit ? (float)(probe.before - probe.minimumBefore) : 0, hits = probe.hits, candidates = chosen,
                runtimeHit = runtimeHit, runtimeUsedEdgeTolerance = runtime.UsedEdgeTolerance, runtimeBeforeTriangle = runtime.BeforeTriangleIndex, runtimeAfterTriangle = runtime.AfterTriangleIndex };
        }
        static EarlyRegionFoliage.Part[] CloneParts(State s, EarlyRegionFoliage.Part[] parts)
        {
            if (parts == null) return null;
            return parts.Select(p =>
            {
                if (p == null) return null;
                var matrices = (Matrix4x4[])p.Matrices.Clone();
                for (int i = 0; i < matrices.Length; i++)
                {
                    var m = matrices[i];
                    if (SampleTriangle(s, m.m03, m.m23, out _, out float delta)) m.m13 += delta;
                    matrices[i] = m; s.receipt.earlyMatrices++;
                }
                return new EarlyRegionFoliage.Part { Mesh = p.Mesh, Material = p.Material, Submesh = p.Submesh, Matrices = matrices };
            }).ToArray();
        }
        static WorldMacroSurfaceDeformationSO NewField(Rect rect, int nx, int nz, float[] offsets, string name)
        {
            var field = ScriptableObject.CreateInstance<WorldMacroSurfaceDeformationSO>();
            field.name = name; field.hideFlags = HideFlags.HideAndDontSave; field.WorldXZ = rect;
            field.ResolutionX = nx; field.ResolutionZ = nz; field.HeightOffsets = offsets; return field;
        }

        static bool ProtectedSample(State s, float x, float z)
        {
            if (!Inclusive(s.domain, x, z)) return false;
            float u = (x - s.domain.xMin) / s.dx, v = (z - s.domain.yMin) / s.dz;
            int ix = Mathf.Clamp(Mathf.FloorToInt(u), 0, s.nx - 2), iz = Mathf.Clamp(Mathf.FloorToInt(v), 0, s.nz - 2), at = iz * s.nx + ix;
            return s.protectedNodes[at] && s.protectedNodes[at + 1] && s.protectedNodes[at + s.nx] && s.protectedNodes[at + s.nx + 1];
        }
        static float SampleArray(State s, float[] a, float x, float z, out bool valid)
        {
            valid = false; if (!Inclusive(s.domain, x, z)) return 0;
            float u = (x - s.domain.xMin) / s.dx, v = (z - s.domain.yMin) / s.dz;
            int ix = Mathf.Clamp(Mathf.FloorToInt(u), 0, s.nx - 2), iz = Mathf.Clamp(Mathf.FloorToInt(v), 0, s.nz - 2), at = iz * s.nx + ix;
            valid = s.valid[at] && s.valid[at + 1] && s.valid[at + s.nx] && s.valid[at + s.nx + 1];
            return valid ? Mathf.Lerp(Mathf.Lerp(a[at], a[at + 1], u - ix), Mathf.Lerp(a[at + s.nx], a[at + s.nx + 1], u - ix), v - iz) : 0;
        }
        static void ForTriangles(Source s, Action<Vector3, Vector3, Vector3> visit)
        { foreach (var indices in s.indices) for (int i = 0; i < indices.Length; i += 3) visit(s.world[indices[i]], s.world[indices[i + 1]], s.world[indices[i + 2]]); }
        static bool Barycentric(Vector3 a, Vector3 b, Vector3 c, float x, float z, out Vector3 weights)
        {
            weights = default; float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(det) < .000001f) return false;
            float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
            float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
            weights = new Vector3(u, v, 1 - u - v); return weights.x >= -.00001f && weights.y >= -.00001f && weights.z >= -.00001f;
        }
        static void Raster(Rect rect, int nx, int nz, Vector3 a, Vector3 b, Vector3 c, Action<int, float, float, float> sample)
        {
            if (!TriangleRect(a, b, c).Overlaps(rect)) return;
            Indices(rect, nx, nz, TriangleRect(a, b, c), out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                if (Barycentric(a, b, c, rect.xMin + x * rect.width / (nx - 1), rect.yMin + z * rect.height / (nz - 1), out var q)) sample(z * nx + x, q.x, q.y, q.z);
        }
        static void Indices(Rect domain, int nx, int nz, Rect rect, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((rect.xMin - domain.xMin) * (nx - 1) / domain.width), 0, nx - 1);
            z0 = Mathf.Clamp(Mathf.FloorToInt((rect.yMin - domain.yMin) * (nz - 1) / domain.height), 0, nz - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt((rect.xMax - domain.xMin) * (nx - 1) / domain.width), 0, nx - 1);
            z1 = Mathf.Clamp(Mathf.CeilToInt((rect.yMax - domain.yMin) * (nz - 1) / domain.height), 0, nz - 1);
        }
        static int[] Integral(bool[] values, int nx, int nz)
        {
            var sum = new int[(nx + 1) * (nz + 1)];
            for (int z = 0; z < nz; z++) { int row = 0; for (int x = 0; x < nx; x++) { row += values[z * nx + x] ? 1 : 0; sum[(z + 1) * (nx + 1) + x + 1] = sum[z * (nx + 1) + x + 1] + row; } }
            return sum;
        }
        static int Count(int[] sum, int nx, int x0, int z0, int x1, int z1)
        { int stride = nx + 1; return sum[(z1 + 1) * stride + x1 + 1] - sum[z0 * stride + x1 + 1] - sum[(z1 + 1) * stride + x0] + sum[z0 * stride + x0]; }
        static float[] Blur(float[] values, bool[] valid, int nx, int nz, int rx, int rz)
        {
            int stride = nx + 1; var sums = new double[stride * (nz + 1)]; var counts = new int[sums.Length];
            for (int z = 0; z < nz; z++)
            {
                double row = 0; int n = 0;
                for (int x = 0; x < nx; x++) { int at = z * nx + x, p = (z + 1) * stride + x + 1; if (valid[at]) { row += values[at]; n++; } sums[p] = sums[p - stride] + row; counts[p] = counts[p - stride] + n; }
            }
            var result = new float[values.Length];
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int x0 = Mathf.Max(0, x - rx), x1 = Mathf.Min(nx, x + rx + 1), z0 = Mathf.Max(0, z - rz), z1 = Mathf.Min(nz, z + rz + 1);
                int a = z0 * stride + x0, b = z0 * stride + x1, c = z1 * stride + x0, d = z1 * stride + x1, n = counts[d] - counts[b] - counts[c] + counts[a];
                result[z * nx + x] = n > 0 ? (float)((sums[d] - sums[b] - sums[c] + sums[a]) / n) : 0;
            }
            return result;
        }
        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b, out float t)
        { var d = b - a; t = d.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude); return Vector2.Distance(p, a + d * t); }
        static float Slope(Vector3 a, Vector3 b, Vector3 c) => Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y))) * Mathf.Rad2Deg;
        static float Bell(float x) { if (Mathf.Abs(x) >= 1) return 0; float v = Mathf.Cos(x * Mathf.PI * .5f); return v * v; }
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        static Rect Expand(Rect r, float d) => Rect.MinMaxRect(r.xMin - d, r.yMin - d, r.xMax + d, r.yMax + d);
        static Rect TriangleRect(Vector3 a, Vector3 b, Vector3 c) => Rect.MinMaxRect(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Min(a.z, Mathf.Min(b.z, c.z)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
        static bool Intersects(Rect r, Bounds b) => b.max.x >= r.xMin && b.min.x <= r.xMax && b.max.z >= r.yMin && b.min.z <= r.yMax;
        static bool Inclusive(Rect r, float x, float z) => x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax;
        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        static Vector2 GridPoint(State s, int x, int z) => new Vector2(s.domain.xMin + x * s.dx, s.domain.yMin + z * s.dz);
        static float OutsideRect(Rect r, Vector2 p) => Mathf.Max(Mathf.Max(r.xMin - p.x, p.x - r.xMax), Mathf.Max(r.yMin - p.y, p.y - r.yMax));
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static string Hierarchy(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
        static void Guard() { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Mountain form preparation/preview is Edit-only."); }
        static void Attempt(Action action, List<string> errors) { try { action(); } catch (Exception e) { errors.Add(e.Message); } }
        static void RestoreForEditor() { try { EndPreview(); Discard(); } catch (Exception e) { Debug.LogException(e); } }
        static void BeforeSave(Scene scene, string path) { if (prepared != null && scene == prepared.scene) RestoreForEditor(); }
        static void BeforeClose(Scene scene, bool removing) { if (prepared != null && scene == prepared.scene) RestoreForEditor(); }
        static void BeforePlay(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) RestoreForEditor(); }
        static void Subscribe()
        { AssemblyReloadEvents.beforeAssemblyReload += RestoreForEditor; EditorApplication.quitting += RestoreForEditor; EditorApplication.playModeStateChanged += BeforePlay; EditorSceneManager.sceneSaving += BeforeSave; EditorSceneManager.sceneClosing += BeforeClose; }
        static void Unsubscribe()
        { AssemblyReloadEvents.beforeAssemblyReload -= RestoreForEditor; EditorApplication.quitting -= RestoreForEditor; EditorApplication.playModeStateChanged -= BeforePlay; EditorSceneManager.sceneSaving -= BeforeSave; EditorSceneManager.sceneClosing -= BeforeClose; }
    }
}
