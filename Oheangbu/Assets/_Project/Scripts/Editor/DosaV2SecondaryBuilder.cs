using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// Static V2 authoring integration only. It never evaluates animation, publishes a prefab/scene,
    /// edits a source mesh, or fabricates missing cloth constraints. Call on fresh rest-pose instances.
    /// </summary>
    public static class DosaV2SecondaryBuilder
    {
        public const string WorldProfilePath = "Assets/_Project/Data/PlayerV2/SecondaryMotion_DosaV2_World.asset";
        public const string NearProfilePath = "Assets/_Project/Data/PlayerV2/SecondaryMotion_DosaV2_Near.asset";
        private const float PositionTolerance = .00001f;
        private const float WeightTolerance = .00001f;
        private const string ProxyPrefix = "DosaV2_ClothProxy_";
        private static readonly string[] LegacyWorldSurfaces =
        {
            "DosaV2_Robe_Front_L", "DosaV2_Robe_Front_R", "DosaV2_Robe_Back_L", "DosaV2_Robe_Back_R",
            "DosaV2_Robe_Side_L", "DosaV2_Robe_Side_R", "DosaV2_SleeveOuter_L", "DosaV2_SleeveOuter_R"
        };
        private static readonly string[] CombinedWorldSurfaces = { "DosaV2_Robe_Combined", "DosaV2_SleeveOuter_L", "DosaV2_SleeveOuter_R" };
        public static bool TryResolveClothLayout(IEnumerable<string> rendererNames, out string[] surfaces, out string layout, out string error)
        {
            var names = rendererNames.Select(Normalize).ToArray();
            var cloth = names.Where(n => n.StartsWith("DosaV2_Robe_", StringComparison.Ordinal) || n == "DosaV2_SleeveOuter_L" || n == "DosaV2_SleeveOuter_R").OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (cloth.SequenceEqual(CombinedWorldSurfaces.OrderBy(n => n, StringComparer.Ordinal))) { surfaces = (string[])CombinedWorldSurfaces.Clone(); layout = "COMBINED_COAT_3"; error = null; return true; }
            if (cloth.SequenceEqual(LegacyWorldSurfaces.OrderBy(n => n, StringComparer.Ordinal))) { surfaces = (string[])LegacyWorldSurfaces.Clone(); layout = "LEGACY_SPLIT_COAT_8"; error = null; return true; }
            surfaces = Array.Empty<string>(); layout = "WAIT"; error = "Expected the complete combined-coat/2-sleeve layout, or the explicitly legacy eight-surface set. Mixed, missing, duplicate or unknown Cloth roles require review."; return false;
        }
        // Exact authored roles from backpack-build-report.json and pack-assembly-report.json.
        // Unknown additions wait for review; fixed backpanel/brush bundle are never spring driven.
        private static readonly Dictionary<string, string> PackRigidRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Bottle_Black_L", "heavy_bottle" }, { "Bottle_Black_M", "heavy_bottle" }, { "Bottle_Black_R", "heavy_bottle" },
            { "Bottle_Brown", "heavy_bottle" }, { "Bottle_Gold_Low", "heavy_bottle" },
            { "Tube_Long_L", "heavy_tube" }, { "Tube_Long_R", "heavy_tube" }, { "Tube_Outer_R", "heavy_tube" },
            { "Tube_Low_L", "heavy_tube" }, { "Tube_Low_M", "heavy_tube" }, { "Pendant", "restrained_pendant" }
        };
        private static readonly Dictionary<string, string> WaistRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "J_WaistPouch_R", "heavy_pouch" }, { "J_WaistPouchBack_L", "heavy_pouch" },
            { "J_WaistVial_L", "heavy_bottle" }, { "J_WaistVialBack_L", "heavy_bottle" },
            { "J_WaistTubeBack_R", "heavy_tube" }, { "J_WaistTube_C", "heavy_tube" },
            { "J_WaistUpperScroll_L", "heavy_tube" }, { "J_WaistScroll_L", "heavy_tube" }, { "J_WaistTubeSide_L", "heavy_tube" }
        };
        // The split candidate retains the original unused bone for bind-pose provenance.
        // Its three new parent anchors follow the belt; only their child bones swing.
        public static bool TryResolveWaistRoles(IEnumerable<string> allBoneNames,
            out Dictionary<string, string> roles, out string[] passive, out string error)
        {
            var actual = new HashSet<string>(allBoneNames.Where(n => n.StartsWith("J_Waist", StringComparison.Ordinal)), StringComparer.Ordinal);
            roles = new Dictionary<string, string>(WaistRoles, StringComparer.Ordinal); passive = Array.Empty<string>(); error = null;
            if (actual.SetEquals(roles.Keys)) return true;
            roles.Remove("J_WaistTubeSide_L");
            roles.Add("J_WaistSideBottle_L", "heavy_bottle");
            roles.Add("J_WaistSideUpperCase_L", "heavy_tube");
            roles.Add("J_WaistSideFlatCase_L", "heavy_tube");
            passive = new[] { "J_WaistTubeSide_L", "J_WaistSideBottleAnchor_L", "J_WaistSideUpperCaseAnchor_L", "J_WaistSideFlatCaseAnchor_L" };
            if (actual.SetEquals(roles.Keys.Concat(passive))) return true;
            error = "Waist hierarchy must match the original nine pivots or the complete reviewed split layout (11 pivots, three surface anchors and one retired bone).";
            return false;
        }

        public static bool IsCompleteRigidLayout(GameObject world, RigidOrnamentBinding[] ornaments, out string error)
        {
            error = null;
            if (world == null || ornaments == null || !TryResolveWaistRoles(world.GetComponentsInChildren<Transform>(true).Select(t => t.name), out var roles, out var passive, out error)) return false;
            var waist = ornaments.Where(o => o != null && o.Pivot != null && o.Pivot.name.StartsWith("J_Waist", StringComparison.Ordinal)).Select(o => o.Pivot.name).ToArray();
            bool complete = ornaments.Length == roles.Count + PackRigidRoles.Count && waist.Length == roles.Count && new HashSet<string>(waist).SetEquals(roles.Keys);
            if (passive.Length > 0)
            {
                var attachments = world.GetComponent<PlayerSkinnedSurfaceAttachmentRig>();
                complete &= attachments != null && attachments.TryBindSerialized() && attachments.Owns(world.transform);
            }
            if (!complete) error = "Incomplete explicit ornament layout or missing split-waist surface attachments.";
            return complete;
        }

        [Serializable] public sealed class ParticleMappingReport
        {
            public int sourceVertices, particles, pinnedSourceVertices, freeSourceVertices, pinnedParticles, freeParticles, duplicateMatches;
            public float maximumDistance, maximumPositionError;
            public string error;
        }
        [Serializable] private sealed class ClothReport
        {
            public string renderer, mesh, sourcePath, colorFormat;
            public string mappingSpace = "V2 measured Root-aligned representation space: representation.worldToLocal * renderer.localToWorld * BakeMesh(useScale=false). Root/representation alignment is required. Probe: cloth-particle-space-probe.json; source color indices are unchanged.";
            public Vector3 bakedVertex0, nativeParticle0;
            public ParticleMappingReport mapping;
        }
        [Serializable] private sealed class ProxyReport
        {
            public string name, anchorBone, endpointBone, startBone, endBone;
            public string updateMode = "RIGID_BONE_ANCHOR";
            public Vector3 midpoint;
            public float radiusMeters, heightMeters;
            public string sourceRenderer, sourcePath, sourceSha256, sourceMetaSha256;
            public Vector3 startRepresentation, endRepresentation, startBlender, endBlender;
            public Vector3 startUnityBoneLocal, endUnityBoneLocal;
            public int fitVertices;
        }
        [Serializable] private sealed class CoverageReport
        {
            public string sourceRenderer, sourcePath;
            public int checkedVertices, uncoveredVertices;
            public float maxOutsideMeters, maxInsideDepthMeters;
        }
        [Serializable] private sealed class PinnedOverlap
        { public string surface, proxy; public int vertex; public float depthMeters; public Vector3 positionRepresentation; }
        [Serializable] private sealed class ProxyExchange
        {
            public string status = "MEASURED_REST_ANATOMY_PENDING_PHYSICS";
            public string space = "Blender rig-local rest meters. RIGID_BONE_ANCHOR entries follow one stated bone. SKINNED_TRIANGLE_FIT entries are rest snapshots only; runtime recomputes barycentric arm fragments and fitted capsules from full source skin. Never pose these snapshots with one bone.";
            public ExchangeCapsule[] capsules;
        }
        [Serializable] private sealed class ExchangeCapsule
        {
            public string name, kind = "body", anchorBone, sourcePath, sourceSha256, sourceMetaSha256;
            public string updateMode;
            public float[] startBlender, endBlender;
            public float radiusMeters;
        }
        [Serializable] private sealed class BindingReport
        {
            public string status = "WAIT", reason, clothLayout;
            public string worldProfile = WorldProfilePath, nearProfile = NearProfilePath;
            public string note = "Static serialized bindings only; no production animation, RigPass, Cloth stability result, scene or shared-prefab promotion. Existing profile tuning is preserved.";
            public bool mutated, cameraReferenceAssigned;
            public int worldOrnaments, worldChains, nearChains;
            public List<ClothReport> cloth = new List<ClothReport>();
            public List<ProxyReport> proxies = new List<ProxyReport>();
            public List<string> notes = new List<string>();
            public List<SecondaryReport> secondary = new List<SecondaryReport>();
            public List<CoverageReport> anatomyCoverage = new List<CoverageReport>();
            public List<PinnedOverlap> worstInitialPinnedOverlaps = new List<PinnedOverlap>();
            public int checkedPinnedVertices, initialPinnedProxyIntersections;
            public float maxInitialPinnedPenetrationMeters;
            public TorsoSurfaceFitReport torsoSurfaceFit;
            public string collisionCapacityStatus = "PENDING_ACTUAL_SELECTION";
            public DosaV2HeadNeckCollisionBuilder.BindingReport headNeckCollision;
            public DosaV2ShoulderCollisionBuilder.Report shoulderCollision;
            public int collisionCandidateCount, measuredNativeCapsuleLimit = PlayerClothCollisionBudgetRig.NativeCapsuleLimit;
            public PlayerClothCollisionBudgetRig.SurfaceSelection[] collisionSelections = Array.Empty<PlayerClothCollisionBudgetRig.SurfaceSelection>();
            public string proxyFitStatus = "PENDING_ACTUAL_BIND", brushProxyStatus = "PENDING_EXPLICIT_WORLD_BRUSH";
            public string anatomyCoverageScope = "BodyLining: original triangle surfaces are partitioned by exact plane clipping; every fragment is enclosed by a convex fitted capsule, with area conservation and fragment-point coverage recorded. Arm/leg coverage remains all actual vertices. Proxy outward error is sampled against actual closed BodyLining triangles and is not a certified whole-proxy bound. Pins never enter fitting. Dynamic body/cloth/self-intersection approval remains pending.";
        }
        [Serializable] private sealed class SecondaryReport
        {
            public string name, role, kind, renderer;
            public string[] bones;
            public Vector3 localLever;
            public int geometryVertices;
            public SecondarySpringSettings settings;
        }
        private sealed class PendingAuthoring : Exception { public PendingAuthoring(string reason) : base(reason) { } }
        private sealed class Prepared
        {
            public GameObject Root;
            public Animator Animator;
            public SkinnedMeshRenderer[] Renderers;
            public Dictionary<string, Transform> Bones;
            public readonly List<RigidOrnamentBinding> Ornaments = new List<RigidOrnamentBinding>();
            public readonly List<SecondaryBoneChainBinding> Chains = new List<SecondaryBoneChainBinding>();
        }
        private sealed class CapsulePlan
        {
            public string Name;
            public Transform Start, End;
            public float Radius;
            public Vector3 WorldStart, WorldEnd;
            public string SourceRenderer, SourcePath;
            public int FitVertices;
        }

        [Serializable] private sealed class SpaceTransform
        {
            public string name;
            public Vector3 position, localPosition, lossyScale;
            public Quaternion rotation, localRotation;
            public Matrix4x4 localToWorld, worldToLocal;
        }
        [Serializable] private sealed class SpaceCandidate
        {
            public string name;
            public bool allParticlesAndSourceVerticesMatched;
            public Vector3 transformedVertex0;
            public ParticleMappingReport mapping;
        }
        [Serializable] private sealed class SpaceCase
        {
            public string name, renderer, sourceMesh;
            public SpaceTransform modelRoot, rendererTransform, parentTransform, rootBoneTransform;
            public Vector3[] bakedSamples, nativeSamples;
            public List<SpaceCandidate> candidates = new List<SpaceCandidate>();
        }
        [Serializable] private sealed class SpaceProbeReport
        {
            public string status = "WAIT", reason;
            public string note = "Fresh native Cloth snapshots in two root poses. Candidate names describe explicit transforms; this probe does not select a conversion, change constraints, or certify simulation.";
            public bool temporarySceneClosed;
            public List<SpaceCase> cases = new List<SpaceCase>();
        }

        /// <summary>Read-only asset probe. Root executes in Edit Mode before choosing a native particle coordinate contract.</summary>
        public static string ProbeParticleSpace()
        {
            var report = new SpaceProbeReport();
            UnityEngine.SceneManagement.Scene preview = default;
            try
            {
                Require(!Application.isPlaying, "The particle-space probe requires Edit Mode.");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaV2_Rigged.fbx");
                Require(asset != null, "The imported V2 world FBX is required.");
                preview = EditorSceneManager.NewPreviewScene();
                for (int pose = 0; pose < 2; pose++)
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                    try
                    {
                        root.transform.SetPositionAndRotation(pose == 0 ? Vector3.zero : new Vector3(3f, 1f, 5f),
                            pose == 0 ? Quaternion.identity : Quaternion.Euler(23f, 37f, -17f));
                        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        Require(TryResolveClothLayout(renderers.Select(r => r.name), out string[] roleNames, out _, out string layoutError), layoutError);
                        var renderer = renderers.Single(r => Normalize(r.name) == roleNames[0]);
                        Require(renderer.GetComponent<Cloth>() == null, "Probe needs an FBX without pre-existing Cloth.");
                        Vector3[] bakedVertices;
                        var baked = new Mesh();
                        try { renderer.BakeMesh(baked, false); bakedVertices = baked.vertices; }
                        finally { Object.DestroyImmediate(baked); }
                        Require(bakedVertices.Length == renderer.sharedMesh.vertexCount, "BakeMesh/source index count differs.");
                        var cloth = renderer.gameObject.AddComponent<Cloth>();
                        cloth.enabled = false;
                        Vector3[] native = cloth.vertices;
                        var sample = new SpaceCase
                        {
                            name = pose == 0 ? "identity_root" : "translated_rotated_root", renderer = renderer.name,
                            sourceMesh = AssetDatabase.GetAssetPath(renderer.sharedMesh),
                            modelRoot = Describe(root.transform), rendererTransform = Describe(renderer.transform),
                            parentTransform = Describe(renderer.transform.parent), rootBoneTransform = Describe(renderer.rootBone),
                            bakedSamples = bakedVertices.Take(8).ToArray(), nativeSamples = native.Take(8).ToArray()
                        };
                        report.cases.Add(sample);
                        Color[] colors = GetAuthoredClothValues(renderer.sharedMesh, roleNames.Length == 8);
                        AddCandidate("bakedLocal", p => p);
                        AddCandidate("rendererWorldRotation", p => renderer.transform.rotation * p);
                        AddCandidate("rendererWorldLinear", p => renderer.localToWorldMatrix.MultiplyVector(p));
                        AddCandidate("rendererWorldPoint", p => renderer.localToWorldMatrix.MultiplyPoint3x4(p));
                        AddCandidate("rendererRelativeToModelPoint", p => root.transform.worldToLocalMatrix.MultiplyPoint3x4(renderer.localToWorldMatrix.MultiplyPoint3x4(p)));
                        AddCandidate("rendererRelativeToModelRotation", p => Quaternion.Inverse(root.transform.rotation) * renderer.transform.rotation * p);
                        AddCandidate("rendererWorldPointMinusModelPosition", p => renderer.localToWorldMatrix.MultiplyPoint3x4(p) - root.transform.position);
                        if (renderer.transform.parent != null)
                        {
                            Transform parent = renderer.transform.parent;
                            AddCandidate("parentWorldRotation", p => parent.rotation * p);
                            AddCandidate("parentWorldLinear", p => parent.localToWorldMatrix.MultiplyVector(p));
                            AddCandidate("parentWorldPoint", p => parent.localToWorldMatrix.MultiplyPoint3x4(p));
                        }
                        if (renderer.rootBone != null)
                        {
                            Transform bone = renderer.rootBone;
                            AddCandidate("rootBoneWorldRotation", p => bone.rotation * p);
                            AddCandidate("rootBoneWorldLinear", p => bone.localToWorldMatrix.MultiplyVector(p));
                            AddCandidate("rootBoneWorldPoint", p => bone.localToWorldMatrix.MultiplyPoint3x4(p));
                            AddCandidate("rendererRelativeToRootBonePoint", p => bone.worldToLocalMatrix.MultiplyPoint3x4(renderer.localToWorldMatrix.MultiplyPoint3x4(p)));
                            AddCandidate("rendererRelativeToRootBoneRotation", p => Quaternion.Inverse(bone.rotation) * renderer.transform.rotation * p);
                            AddCandidate("rendererWorldPointMinusRootBonePosition", p => renderer.localToWorldMatrix.MultiplyPoint3x4(p) - bone.position);
                        }
                        void AddCandidate(string name, Func<Vector3, Vector3> transform)
                        {
                            Vector3[] candidate = bakedVertices.Select(transform).ToArray();
                            bool matched = TryMapAuthoredCoefficients(candidate, colors, native, out _, out var mapping);
                            sample.candidates.Add(new SpaceCandidate { name = name, allParticlesAndSourceVerticesMatched = matched,
                                transformedVertex0 = candidate.Length == 0 ? Vector3.zero : candidate[0], mapping = mapping });
                        }
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                report.status = "MEASURED";
            }
            catch (PendingAuthoring e) { report.reason = e.Message; }
            catch (Exception e) { report.status = "FAIL"; report.reason = e.ToString(); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                report.temporarySceneClosed = !preview.IsValid();
            }
            string json = JsonUtility.ToJson(report, true);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/cloth-particle-space-probe.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, json);
            return json;
        }

        /// <summary>
        /// Measured for the V2 FBX by fresh native Cloth snapshots at identity and translated/rotated roots.
        /// This is not a universal Cloth.vertices space assumption: the imported Root must remain aligned
        /// with its explicitly supplied representation. A different hierarchy requires a new space probe.
        /// </summary>
        public static bool TryGetParticleFrame(SkinnedMeshRenderer renderer, Transform representation,
            out Matrix4x4 bakedLocalToParticle, out string error)
        {
            bakedLocalToParticle = Matrix4x4.identity; error = null;
            if (renderer == null || representation == null || renderer.rootBone == null ||
                !renderer.transform.IsChildOf(representation) || !renderer.rootBone.IsChildOf(representation))
            { error = "Explicit V2 representation and descendant renderer/Root are required for the measured particle-space contract."; return false; }
            Matrix4x4 relative = representation.worldToLocalMatrix * renderer.rootBone.localToWorldMatrix;
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
                if (!Finite(relative[row, col]) || Mathf.Abs(relative[row, col] - (row == col ? 1f : 0f)) > .0001f)
                { error = renderer.name + ": Root is no longer aligned with the representation; rerun the native particle-space probe before binding/measuring."; return false; }
            bakedLocalToParticle = representation.worldToLocalMatrix * renderer.localToWorldMatrix;
            return true;
        }

        private static SpaceTransform Describe(Transform transform) => transform == null ? null : new SpaceTransform
        {
            name = transform.name, position = transform.position, localPosition = transform.localPosition,
            rotation = transform.rotation, localRotation = transform.localRotation, lossyScale = transform.lossyScale,
            localToWorld = transform.localToWorldMatrix, worldToLocal = transform.worldToLocalMatrix
        };

        /// <summary>
        /// Both representations and an explicit near-view camera are required. A camera outside the saved
        /// prefab hierarchy must be injected again by the runtime owner; Camera.main is never guessed here.
        /// Any incomplete authoring returns WAIT and removes only components/objects created by this call.
        /// </summary>
        public static string Bind(GameObject world, GameObject near, Camera nearCamera)
            => Bind(world, near, nearCamera, null);

        public static string Bind(GameObject world, GameObject near, Camera nearCamera, GameObject worldBrush, bool bindNearSleeveDynamics = true)
        {
            var report = new BindingReport();
            var createdObjects = new List<Object>();
            var temporaryProfiles = new List<PlayerSecondaryMotionProfileSO>();
            var createdAssets = new List<string>();
            bool committed = false;
            try
            {
                Require(!Application.isPlaying, "Bind requires Edit Mode and fresh static review instances.");
                Require(world != null && near != null && world != near, "Both distinct authored representations are required.");
                Require(!world.transform.IsChildOf(near.transform) && !near.transform.IsChildOf(world.transform), "World and near must be separate representation roots.");
                Require(nearCamera != null, "An explicit near-view Camera is required; no Camera.main fallback is used.");
                var w = Prepare(world); var n = Prepare(near);
                Require(TryResolveClothLayout(w.Renderers.Select(r => r.name), out string[] clothNames, out string layout, out string layoutError), layoutError);
                report.clothLayout = layout;
                var surfaces = clothNames.Select(name => Surface(w, name)).ToArray();
                foreach (var renderer in surfaces) InspectAuthoredMesh(renderer, layout == "LEGACY_SPLIT_COAT_8");
                foreach (string side in new[] { "L", "R" })
                {
                    var sleeve = Surface(n, "DosaV2_SleeveOuter_" + side);
                    Require(sleeve.GetComponent<Cloth>() == null, sleeve.name + ": near sleeves must use dedicated bones, not Cloth.");
                    if (bindNearSleeveDynamics) AddChain(n, "J_SleeveHem_" + side, sleeve);
                    AddChain(w, "J_HatTassel_" + side, null);
                    var hat = w.Chains[w.Chains.Count - 1]; SetRole(hat, "light_hat");
                    report.secondary.Add(DescribeSecondary(hat, "light_hat", null, 0));
                }
                Require(TryResolveWaistRoles(w.Bones.Keys, out var waistRoles, out var passiveWaist, out string waistError), waistError);
                foreach (string name in passiveWaist)
                    Require(WeightedPoints(w, w.Bones[name], false, null).Count == 0 && RigidPoints(w, w.Bones[name], null).Count == 0,
                        name + ": surface-driver/retired bones must own no source geometry; none may be silently skipped.");
                var waist = w.Bones.Where(pair => waistRoles.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                foreach (var entry in waist)
                {
                    var binding = Ornament(w, entry.Value, out int vertices); string role = waistRoles[entry.Key]; SetRole(binding, role); w.Ornaments.Add(binding);
                    report.secondary.Add(new SecondaryReport { name = binding.Name, role = role, kind = "rigid_skinned_waist", bones = new[] { entry.Value.name },
                        localLever = binding.LocalCenterOfMass, geometryVertices = vertices, settings = binding.Settings });
                }
                AddBackpack(w, report);
                if (!bindNearSleeveDynamics)
                    report.notes.Add("PLAYABLE DRAFT: the rebuilt near sleeves use their authored Humanoid skin only. They contain no weights for the old sleeve-hem chain, so no phantom chain is bound. Extra near-sleeve secondary deformation awaits authoring; world native Cloth and other authored chains remain active.");
                report.notes.Add("Named heavy bottle/pouch/tube and lighter pendant/cord/hat overrides are initial restrained tuning, not measured collision/stability PASS. Levers use actual authored geometry; pivots/rest transforms are preserved.");
                var capsulePlans = PrepareCapsules(w, surfaces, report);
                PlayerSecondaryMotionProfileSO worldProfile = Profile(WorldProfilePath, temporaryProfiles);
                PlayerSecondaryMotionProfileSO nearProfile = Profile(NearProfilePath, temporaryProfiles);

                // Read the native particle order only after the complete static preflight above.
                // All added Cloth components are removed on WAIT; source meshes/colors are never changed.
                var bindings = new List<PlayerClothBinding>();
                foreach (var renderer in surfaces)
                {
                    Vector3[] bakedVertices;
                    var baked = new Mesh { name = "DosaV2ClothRestMapping_Temporary" };
                    try
                    {
                        renderer.BakeMesh(baked, false);
                        bakedVertices = baked.vertices;
                        Require(bakedVertices.Length == renderer.sharedMesh.vertexCount,
                            renderer.name + ": pre-Cloth BakeMesh vertex count differs from source color indices.");
                    }
                    finally { Object.DestroyImmediate(baked); }
                    Require(TryGetParticleFrame(renderer, world.transform, out Matrix4x4 restToParticle, out string frameError), frameError);
                    Vector3[] particleRest = bakedVertices.Select(restToParticle.MultiplyPoint3x4).ToArray();
                    var cloth = renderer.gameObject.AddComponent<Cloth>(); createdObjects.Add(cloth);
                    cloth.enabled = false;
                    Vector3[] particles = cloth.vertices;
                    Require(particles.Length == cloth.coefficients.Length, renderer.name + ": native Cloth vertices/coefficient counts differ; wait for native initialization.");
                    bool mapped = TryMapAuthoredCoefficients(particleRest, GetAuthoredClothValues(renderer.sharedMesh, layout == "LEGACY_SPLIT_COAT_8"),
                        particles, out ClothSkinningCoefficient[] coefficients, out ParticleMappingReport mapping);
                    report.cloth.Add(new ClothReport { renderer = renderer.name, mesh = renderer.sharedMesh.name,
                        sourcePath = AssetDatabase.GetAssetPath(renderer.sharedMesh), mapping = mapping,
                        colorFormat = renderer.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord3) ? "UV3.x Float32 ClothMobility_Meters" : "EXPLICIT_LEGACY_VERTEX_COLOR_FALLBACK",
                        bakedVertex0 = bakedVertices.Length > 0 ? bakedVertices[0] : Vector3.zero,
                        nativeParticle0 = particles.Length > 0 ? particles[0] : Vector3.zero });
                    Require(mapped, renderer.name + ": " + mapping.error);
                    bindings.Add(new PlayerClothBinding { Name = renderer.name, Cloth = cloth, Coefficients = coefficients });
                }
                var capsules = new List<CapsuleCollider>();
                foreach (CapsulePlan plan in capsulePlans) capsules.Add(CreateCapsule(plan, world.transform, createdObjects, report));
                string headPlanPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../../Art/PlayerV2/Staging/Code/HeadNeckCollision/head-neck-capsules.json"));
                bool headBound = DosaV2HeadNeckCollisionBuilder.TryBind(world.transform, w.Animator, headPlanPath,
                    out CapsuleCollider[] headCapsules, out var headReport);
                report.headNeckCollision = headReport;
                Require(headBound, "Actual imported head/neck capsule authoring: " + headReport.error);
                foreach (var proxy in headCapsules) createdObjects.Add(proxy.gameObject);
                Require(new HashSet<string>(headCapsules.Select(c => c.name), StringComparer.Ordinal)
                    .SetEquals(DosaV2HeadNeckCollisionBuilder.ReviewedNames), "Reviewed six head/neck shapes must match by exact name.");
                capsules.AddRange(headCapsules);
                var bodyBindings = new List<PlayerClothBodyProxyRig.Binding>();
                foreach(string armName in new[] { "DosaV2_ArmLining_Left", "DosaV2_ArmLining_Right" })
                {
                    var binding = PlayerClothBodyProxyRig.AuthorRestBinding(Surface(w,armName),world.transform,out _);
                    for(int region=0;region<binding.regions.Length;region++)
                        binding.regions[region].capsule=capsules.Single(c=>c.name==ProxyPrefix+armName.Replace("DosaV2_", "")+"_Posed_"+region);
                    bodyBindings.Add(binding);
                }
                bool shouldersBound = DosaV2ShoulderCollisionBuilder.TryCreateBindings(world.transform,
                    out var shoulderBindings, out var shoulderCapsules, out var shoulderReport);
                report.shoulderCollision = shoulderReport;
                Require(shouldersBound, "Actual shoulder source binding: " + shoulderReport.error);
                foreach (var proxy in shoulderCapsules) createdObjects.Add(proxy.gameObject);
                bodyBindings.AddRange(shoulderBindings);
                capsules.AddRange(shoulderCapsules);
                var bodyProxies=world.AddComponent<PlayerClothBodyProxyRig>();createdObjects.Add(bodyProxies);
                Require(bodyProxies.Configure(world.transform,bodyBindings.ToArray(),out string bodyError),"Actual skinned arm proxy binding: "+bodyError);
                if (worldBrush != null)
                {
                    var brushProxies = world.AddComponent<PlayerClothBrushProxyRig>(); createdObjects.Add(brushProxies);
                    Require(brushProxies.Configure(world.transform, worldBrush, out string brushError), "World brush proxy binding: " + brushError);
                    foreach (var proxy in brushProxies.Capsules) createdObjects.Add(proxy.gameObject);
                    capsules.AddRange(brushProxies.Capsules); report.brushProxyStatus = "BOUND_ACTUAL_GEOMETRY_PENDING_COLLISION_REVIEW";
                }
                // Unity 6000.3.9f1's actual capacity probe responds to only the first16 capsules
                // (32 sphere slots), even though its serialized getter retains a longer list.
                // Preserve every geometry candidate; per-surface selection exposes any omission.
                var collisionBudget = world.AddComponent<PlayerClothCollisionBudgetRig>(); createdObjects.Add(collisionBudget);
                Require(collisionBudget.Configure(world.transform, bindings.Select(b => b.Cloth).ToArray(), capsules.ToArray(), out string capacityError),
                    "Collision candidate selection: " + capacityError);
                foreach (var binding in bindings) binding.Capsules = collisionBudget.GetAssignedCapsules(binding.Cloth);
                report.collisionCapacityStatus = collisionBudget.Status;
                report.collisionCandidateCount = collisionBudget.CandidateCount;
                report.collisionSelections = collisionBudget.SnapshotSelections();

                var worldRig = world.AddComponent<PlayerSecondaryMotionRig>(); createdObjects.Add(worldRig);
                var nearRig = near.AddComponent<PlayerSecondaryMotionRig>(); createdObjects.Add(nearRig);
                Require(worldRig.Configure(worldProfile, world.transform, w.Ornaments.ToArray(), w.Chains.ToArray(), bindings.ToArray()),
                    "World Configure rejected authored bindings: " + worldRig.LastBindingError);
                Require(nearRig.Configure(nearProfile, near.transform, Array.Empty<RigidOrnamentBinding>(), n.Chains.ToArray(),
                    Array.Empty<PlayerClothBinding>(), nearCamera), "Near Configure rejected authored bindings: " + nearRig.LastBindingError);
                collisionBudget.ReapplyAssignments();

                PersistProfile(worldProfile, WorldProfilePath, temporaryProfiles, createdAssets);
                PersistProfile(nearProfile, NearProfilePath, temporaryProfiles, createdAssets);
                foreach (Object obj in createdObjects)
                {
                    if (obj == null) continue;
                    EditorUtility.SetDirty(obj);
                    if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                }
                report.worldOrnaments = w.Ornaments.Count; report.worldChains = w.Chains.Count; report.nearChains = n.Chains.Count;
                report.cameraReferenceAssigned = true; report.mutated = true; report.status = "BOUND_STATIC";
                report.notes.Add("World Cloth layout: " + layout + " (" + surfaces.Length + " authored surfaces). Near: two sleeve-hem chains, zero Cloth; world hat/waist/pack roles retain their independent bindings.");
                report.notes.Add("Red is maxDistance in meters without scaling/clamping; exact zero remains pinned. Green/blue are ignored; collisionSphereDistance is explicitly zero.");
                report.notes.Add("Imported color precision is reported per mesh. Observed 8-bit import may quantize authored 0.28m to 0.278431m; bindings preserve the measured imported value and exact-zero pins rather than inventing missing precision.");
                report.notes.Add("Anatomy proxy radii enclose their assigned real inner-mesh vertices. Cloth pins never choose/shrink them. Source coverage and initial fixed-pin intersections are measured separately; no physical stability PASS is implied.");
                report.notes.Add("Each native Cloth receives at most16 capsules, measured on Unity6000.3.9f1. Complete source-weighted target points plus exact UV3 mobility select relevant candidates; analytic full containment alone can remove redundancy. WAIT_CAPACITY and omitted candidates remain explicit even when nearest16 permit an incomplete diagnostic solve. Static BOUND does not imply collision coverage or RIG_PASS.");
                report.notes.Add("Proxy manifest uses rig-local Blender coordinates(-unity.x,-unity.z,unity.y). Rigid torso/leg candidates retain bone anchors. Posed arm candidates use full current source skin and serialized clipped-triangle barycentric corners; their manifest coordinates are rest snapshots only. No proxy Rigidbody or game-collision participation is permitted.");
                if (!nearCamera.transform.IsChildOf(near.transform.root)) report.notes.Add("The supplied camera is outside the representation hierarchy. Rebind it explicitly after prefab instantiation; scene-object references do not persist in prefab assets.");
                committed = true;
                string proxyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/anatomy-proxy-fit.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)); File.WriteAllText(proxyPath, JsonUtility.ToJson(report, true));
                var exchange = new ProxyExchange { capsules = report.proxies.Select(p => new ExchangeCapsule { name = p.name, anchorBone = p.anchorBone, updateMode=p.updateMode,
                    startBlender = new[] { p.startBlender.x, p.startBlender.y, p.startBlender.z }, endBlender = new[] { p.endBlender.x, p.endBlender.y, p.endBlender.z },
                    radiusMeters = p.radiusMeters, sourcePath = p.sourcePath, sourceSha256 = p.sourceSha256, sourceMetaSha256 = p.sourceMetaSha256 }).ToArray() };
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(proxyPath), "anatomy-capsules-blender.json"), JsonUtility.ToJson(exchange, true));
            }
            catch (PendingAuthoring e) { report.status = "WAIT"; report.reason = e.Message; }
            catch (Exception e) { report.status = "FAIL"; report.reason = e.GetType().Name + ": " + e.Message; }
            finally
            {
                if (!committed)
                {
                    // Reverse destruction releases runtime snapshots before deleting their native Cloth/proxies.
                    for (int i = createdObjects.Count - 1; i >= 0; i--) if (createdObjects[i] != null) Object.DestroyImmediate(createdObjects[i]);
                    foreach (string path in createdAssets) AssetDatabase.DeleteAsset(path);
                    report.mutated = false;
                }
                foreach (var profile in temporaryProfiles) if (profile != null && !EditorUtility.IsPersistent(profile)) Object.DestroyImmediate(profile);
            }
            return JsonUtility.ToJson(report, true);
        }

        private static Prepared Prepare(GameObject root)
        {
            Require(!EditorUtility.IsPersistent(root) && root.scene.IsValid(), root.name + ": use an instantiated static review root, not a source asset.");
            Require(!root.scene.path.EndsWith("/C2_CodexWorld.unity", StringComparison.OrdinalIgnoreCase), "This static builder does not modify canonical C2.");
            Require(root.GetComponentInChildren<PlayerSecondaryMotionRig>(true) == null, root.name + ": existing secondary bindings are preserved; bind a fresh instance.");
            Require(root.GetComponentInChildren<Cloth>(true) == null, root.name + ": existing Cloth is preserved; bind a fresh authored instance.");
            Require(root.GetComponentsInChildren<Rigidbody>(true).Length == 0 && root.GetComponentInParent<Rigidbody>() == null,
                root.name + ": Rigidbody ownership would invalidate representation-only collision proxies.");
            var animators = root.GetComponentsInChildren<Animator>(true);
            Require(animators.Length == 1 && animators[0].avatar != null && animators[0].avatar.isValid && animators[0].avatar.isHuman,
                root.name + ": one valid static Humanoid Animator is required.");
            Require(animators[0].runtimeAnimatorController == null, root.name + ": no production AnimatorController may be bound before RigPass.");
            var result = new Prepared { Root = root, Animator = animators[0], Renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                Bones = new Dictionary<string, Transform>(StringComparer.Ordinal) };
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                Uniform(t);
                string name = t.name.Contains(":") ? t.name.Substring(t.name.LastIndexOf(':') + 1) : t.name;
                if (!name.StartsWith("J_", StringComparison.Ordinal) && !name.StartsWith("PackAux_", StringComparison.Ordinal) && name != "PackRoot") continue;
                Require(!result.Bones.ContainsKey(name), root.name + ": duplicate authored bone name " + name);
                result.Bones.Add(name, t);
            }
            Require(!root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith(ProxyPrefix, StringComparison.Ordinal)),
                root.name + ": existing secondary proxy objects are preserved; use a fresh instance.");
            foreach (var renderer in result.Renderers)
            {
                Require(renderer.sharedMesh != null && renderer.sharedMesh.isReadable, renderer.name + ": readable authored mesh required.");
                Mesh mesh = renderer.sharedMesh;
                Require(mesh.bindposes.Length == renderer.bones.Length && renderer.bones.All(b => b != null), renderer.name + ": skin bone/bindpose references differ.");
                for (int i = 0; i < renderer.bones.Length; i++)
                {
                    Matrix4x4 relative = renderer.transform.worldToLocalMatrix * renderer.bones[i].localToWorldMatrix * mesh.bindposes[i];
                    float error = 0f;
                    for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) error = Mathf.Max(error, Mathf.Abs(relative[row, col] - (row == col ? 1f : 0f)));
                    Require(error <= .001f, renderer.name + ": current bones are not in authored bind rest; particle mapping must not use a posed mesh.");
                }
                for (int i = 0; i < mesh.blendShapeCount; i++) Require(renderer.GetBlendShapeWeight(i) == 0f, renderer.name + ": reset blend shapes to authored rest before binding.");
            }
            return result;
        }

        private static SkinnedMeshRenderer Surface(Prepared prepared, string name)
        {
            var matches = prepared.Renderers.Where(r => r.name == name || r.sharedMesh != null && r.sharedMesh.name == name).ToArray();
            Require(matches.Length == 1, prepared.Root.name + ": expected exactly one authored surface " + name + ", found " + matches.Length);
            return matches[0];
        }

        private static void InspectAuthoredMesh(SkinnedMeshRenderer renderer, bool allowLegacyColors)
        {
            Mesh mesh = renderer.sharedMesh;
            var colors = GetAuthoredClothValues(mesh, allowLegacyColors);
            Require(colors.All(c => Finite(c.r) && c.r >= 0f && c.r <= 1f), renderer.name + ": red maxDistance must be finite meters in [0, 1]; values are never clamped.");
            Require(colors.Any(c => c.r == 0f) && colors.Any(c => c.r > 0f), renderer.name + ": both exact-zero seam pins and authored free vertices are required.");
        }

        /// <summary>V2 mobility is the exact authored float in UV channel 3.x. UV0/materials are untouched.
        /// UNorm8 colors turn small positive mobility into zero; they are accepted only for an explicitly selected legacy layout.</summary>
        public static Color[] GetAuthoredClothValues(Mesh mesh, bool allowLegacyColors = false)
        {
            Require(mesh != null && mesh.isReadable, "Readable source mesh is required for authored Cloth mobility.");
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord3))
            {
                Require(mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord3) == VertexAttributeFormat.Float32,
                    mesh.name + ": ClothMobility_Meters must remain Float32; compressed/quantized values cannot define pins.");
                var values = new List<Vector2>(); mesh.GetUVs(3, values);
                return DecodeClothMobility(values.ToArray(), mesh.vertexCount);
            }
            Require(allowLegacyColors && mesh.HasVertexAttribute(VertexAttribute.Color) && mesh.colors.Length == mesh.vertexCount,
                mesh.name + ": exact authored ClothMobility_Meters UV3 is missing; V2 does not fall back to quantized colors.");
            return mesh.colors;
        }
        public static Color[] DecodeClothMobility(Vector2[] values, int vertexCount)
        {
            if (values == null || values.Length != vertexCount || vertexCount <= 0 ||
                values.Any(v => !Finite(v.x) || !Finite(v.y) || v.x < 0f || v.x > 1f))
                throw new ArgumentException("Complete finite UV3 mobility in meters [0,1] is required.");
            return values.Select(v => new Color(v.x, 0f, 0f, 1f)).ToArray();
        }

        private static RigidOrnamentBinding Ornament(Prepared prepared, Transform pivot, out int vertexCount, Renderer only = null)
        {
            var points = RigidPoints(prepared, pivot, only); vertexCount = points.Count;
            Require(points.Count > 0, pivot.name + ": dedicated ornament bone has no direct-parent rigid or 100%-weighted geometry.");
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in points) center += pivot.InverseTransformPoint(point);
            center /= points.Count;
            Require(Finite(center) && center.sqrMagnitude >= .000001f, pivot.name + ": authored attachment pivot needs a nonzero measured center-of-mass lever.");
            return new RigidOrnamentBinding { Name = pivot.name, Pivot = pivot, LocalCenterOfMass = center };
        }

        private static void AddBackpack(Prepared prepared, BindingReport report)
        {
            var packRenderers = prepared.Root.GetComponentsInChildren<Renderer>(true)
                .Where(r => Normalize(r.name).StartsWith("DosaPackV2_", StringComparison.Ordinal)).ToArray();
            bool hasBones = prepared.Bones.Keys.Any(name => name == "PackRoot" || name.StartsWith("PackAux_", StringComparison.Ordinal));
            if (!hasBones && packRenderers.Length == 0)
            { report.notes.Add("This source has no backpack authoring. No backpack bindings were inferred; import the assembled source before backpack diagnostics."); return; }
            string[] cords = { "Center", "Right", "Left" };
            var expectedMeshes = PackRigidRoles.Keys.Select(name => "DosaPackV2_" + name)
                .Concat(cords.Select(side => "DosaPackV2_Cord_" + side)).Concat(new[] { "DosaPackV2_Backpanel", "DosaPackV2_BrushBundle" }).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Require(packRenderers.Select(r => Normalize(r.name)).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expectedMeshes),
                "The backpack must have exactly the 16 reviewed renderers; missing, duplicate or unknown parts require authoring review.");
            Require(prepared.Bones.TryGetValue("PackRoot", out Transform packRoot), "Reviewed backpack PackRoot is missing.");
            Require(packRoot.parent != null && Normalize(packRoot.parent.name) == "Spine02", "PackRoot must keep its authored Spine02 attachment.");
            var expectedBones = new[] { "PackRoot", "PackAux_BrushBundle" }.Concat(PackRigidRoles.Keys.Select(name => "PackAux_" + name))
                .Concat(cords.SelectMany(side => Enumerable.Range(1, 3).Select(i => "PackAux_Cord_" + side + "_" + i.ToString("00")))).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Require(prepared.Bones.Keys.Where(name => name == "PackRoot" || name.StartsWith("PackAux_", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expectedBones),
                "Backpack bone names differ from the reviewed fixed/rigid/three-bone cord manifest.");
            Renderer RendererFor(string suffix) => packRenderers.Single(r => Normalize(r.name) == "DosaPackV2_" + suffix);
            foreach (var fixedPart in new[] { ("Backpanel", "PackRoot"), ("BrushBundle", "PackAux_BrushBundle") })
            {
                Transform pivot = prepared.Bones[fixedPart.Item2]; Renderer renderer = RendererFor(fixedPart.Item1);
                if (pivot != packRoot) Require(pivot.parent == packRoot, pivot.name + ": fixed brush bundle must retain direct PackRoot parent.");
                var points = RigidPoints(prepared, pivot, renderer);
                Require(points.Count > 0, renderer.name + ": fixed authored geometry is absent.");
                report.secondary.Add(new SecondaryReport { name = renderer.name, role = "fixed", kind = "fixed_not_driven", renderer = renderer.name,
                    bones = new[] { pivot.name }, geometryVertices = points.Count });
            }
            foreach (var entry in PackRigidRoles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Transform pivot = prepared.Bones["PackAux_" + entry.Key]; Renderer renderer = RendererFor(entry.Key);
                Require(pivot.parent == packRoot, pivot.name + ": rigid accessory pivot must retain direct PackRoot parent.");
                var binding = Ornament(prepared, pivot, out int vertices, renderer); SetRole(binding, entry.Value); prepared.Ornaments.Add(binding);
                report.secondary.Add(new SecondaryReport { name = binding.Name, role = entry.Value,
                    kind = renderer is SkinnedMeshRenderer ? "whole_mesh_rigid_skin" : "direct_bone_parent_mesh", renderer = renderer.name,
                    bones = new[] { pivot.name }, localLever = binding.LocalCenterOfMass, geometryVertices = vertices, settings = binding.Settings });
            }
            foreach (string side in cords)
            {
                string prefix = "PackAux_Cord_" + side;
                var bones = Enumerable.Range(1, 3).Select(i => prepared.Bones[prefix + "_" + i.ToString("00")]).ToArray();
                Require(bones[0].parent == packRoot && bones[1].parent == bones[0] && bones[2].parent == bones[1], prefix + ": authored direct three-bone hierarchy must be preserved.");
                var renderer = RendererFor("Cord_" + side) as SkinnedMeshRenderer;
                Require(renderer != null, prefix + ": cord must be an authored SkinnedMeshRenderer, not an inferred rigid mesh chain.");
                ValidateCordWeights(renderer, bones);
                foreach (Transform bone in bones) Require(WeightedPoints(prepared, bone, false, renderer).Count > 0, bone.name + ": cord bone has no actual weighted vertices.");
                var terminalPoints = WeightedPoints(prepared, bones[2], false, renderer);
                Vector3 tip = terminalPoints.Select(point => bones[2].InverseTransformPoint(point)).OrderByDescending(point => point.sqrMagnitude).First();
                Require(Finite(tip) && tip.sqrMagnitude >= .000001f, prefix + ": actual terminal geometry has no valid lever.");
                var binding = new SecondaryBoneChainBinding { Name = prefix, Bones = bones, LastBoneTipLocal = tip }; SetRole(binding, "light_cord");
                prepared.Chains.Add(binding); report.secondary.Add(DescribeSecondary(binding, "light_cord", renderer, renderer.sharedMesh.vertexCount));
            }
            report.notes.Add("Backpack adds 11 rigid ornament pivots and three 3-bone cord chains. PackRoot/Backpanel and BrushBundle are audited fixed and are not registered for secondary motion. Geometry levers are vertex centroids/terminal extents, not inferred physical mass measurements; collision review remains pending.");
        }

        private static List<Vector3> RigidPoints(Prepared prepared, Transform pivot, Renderer only)
        {
            var result = new List<Vector3>();
            if (only == null || only is SkinnedMeshRenderer)
            {
                result.AddRange(WeightedPoints(prepared, pivot, true, only as SkinnedMeshRenderer));
                if (only is SkinnedMeshRenderer skin)
                    Require(result.Count == skin.sharedMesh.vertexCount, skin.name + ": the entire rigid part must have 100% single-pivot weights; zero/unrelated vertices are not ignored.");
            }
            foreach (var renderer in prepared.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (only != null && renderer != only) continue;
                if (only == null && renderer.transform.parent != pivot) continue;
                Require(renderer.transform.parent == pivot, renderer.name + ": rigid MeshRenderer must be directly parented to its exact authored pivot.");
                var filter = renderer.GetComponent<MeshFilter>();
                Require(filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable, renderer.name + ": readable rigid mesh geometry is required.");
                Vector3[] vertices = filter.sharedMesh.vertices;
                Require(vertices.Length > 0 && vertices.All(Finite), renderer.name + ": rigid geometry contains empty/nonfinite vertices.");
                result.AddRange(vertices.Select(renderer.transform.TransformPoint));
            }
            Require(only == null || only is MeshRenderer || only is SkinnedMeshRenderer, "Only explicit MeshRenderer or wholly rigid SkinnedMeshRenderer authoring is supported.");
            Require(result.All(Finite), pivot.name + ": rigid world geometry is nonfinite.");
            return result;
        }

        private static void ValidateCordWeights(SkinnedMeshRenderer renderer, Transform[] bones)
        {
            var allowed = new HashSet<Transform>(bones); Mesh mesh = renderer.sharedMesh;
            using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
            {
                Require(counts.Length == mesh.vertexCount, renderer.name + ": full cord weight stream is missing."); int offset = 0;
                for (int vertex = 0; vertex < counts.Length; vertex++)
                {
                    float total = 0f;
                    for (int i = 0; i < counts[vertex]; i++)
                    {
                        Require(offset < weights.Length, renderer.name + ": malformed cord weight stream."); var weight = weights[offset++];
                        Require(Finite(weight.weight) && weight.weight >= 0f && weight.boneIndex >= 0 && weight.boneIndex < renderer.bones.Length, renderer.name + ": invalid cord influence.");
                        if (weight.weight > 0f) Require(allowed.Contains(renderer.bones[weight.boneIndex]), renderer.name + ": cord is weighted outside its reviewed three-bone chain.");
                        total += weight.weight;
                    }
                    Require(Mathf.Abs(total - 1f) <= WeightTolerance, renderer.name + ": cord vertex weights must sum to 1 without rewriting.");
                }
                Require(offset == weights.Length, renderer.name + ": cord weight stream has trailing entries.");
            }
        }

        /// <summary>Initial explicit review roles. Unknown names fail instead of inheriting a generic heavy/light guess.</summary>
        public static bool TryGetRoleSettings(string role, out SecondarySpringSettings settings)
        {
            settings = null;
            switch (role)
            {
                case "heavy_bottle": settings = Spring(2.2f, 1.05f, 12f, .6f, .25f, .18f, .08f); break;
                case "heavy_tube": settings = Spring(2.4f, 1f, 14f, .65f, .3f, .2f, .08f); break;
                case "heavy_pouch": settings = Spring(2f, 1.1f, 12f, .7f, .2f, .15f, .05f); break;
                case "restrained_pendant": settings = Spring(2.6f, .95f, 20f, .8f, .35f, .25f, .12f); break;
                case "light_cord": settings = Spring(3f, .85f, 24f, 1f, .4f, .3f, .4f); break;
                case "light_hat": settings = Spring(3.2f, .9f, 24f, 1f, .35f, .28f, .35f); break;
                default: return false;
            }
            return true;
        }
        private static SecondarySpringSettings Spring(float frequency, float damping, float swing, float gravity, float translation, float rotation, float wind)
            => new SecondarySpringSettings { FrequencyHz = frequency, DampingRatio = damping, MaximumSwingDegrees = swing, GravityScale = gravity, TranslationInertia = translation, RotationInertia = rotation, WindScale = wind };
        private static void SetRole(RigidOrnamentBinding binding, string role)
        { Require(TryGetRoleSettings(role, out var settings), "Unknown ornament role: " + role); binding.OverrideSettings = true; binding.Settings = settings; }
        private static void SetRole(SecondaryBoneChainBinding binding, string role)
        { Require(TryGetRoleSettings(role, out var settings), "Unknown chain role: " + role); binding.OverrideSettings = true; binding.Settings = settings; }
        private static SecondaryReport DescribeSecondary(SecondaryBoneChainBinding binding, string role, Renderer renderer, int vertices)
            => new SecondaryReport { name = binding.Name, role = role, kind = "three_bone_chain", renderer = renderer != null ? renderer.name : null,
                bones = binding.Bones.Select(bone => bone.name).ToArray(), localLever = binding.LastBoneTipLocal, geometryVertices = vertices, settings = binding.Settings };
        private static string Normalize(string name) => name.Contains(":") ? name.Substring(name.LastIndexOf(':') + 1) : name;

        private static void AddChain(Prepared prepared, string prefix, SkinnedMeshRenderer requiredSurface)
        {
            var bones = new Transform[3];
            for (int i = 0; i < bones.Length; i++)
                Require(prepared.Bones.TryGetValue(prefix + "_" + i, out bones[i]), prepared.Root.name + ": missing authored chain bone " + prefix + "_" + i);
            for (int i = 1; i < bones.Length; i++) Require(bones[i].parent == bones[i - 1], prefix + ": chain must have direct parent-child topology.");
            var used = new HashSet<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) { var bone = prepared.Animator.GetBoneTransform((HumanBodyBones)i); if (bone != null) used.Add(bone); }
            Require(bones.All(b => !used.Contains(b)), prefix + ": Humanoid animation/IK bones cannot be secondary chain nodes.");
            foreach (Transform bone in bones)
                Require(WeightedPoints(prepared, bone, false, requiredSurface).Count > 0, bone.name + ": no authored skin influence; do not fabricate sleeve/hat weights.");
            var terminalPoints = WeightedPoints(prepared, bones[2], false, requiredSurface);
            Vector3 tip = terminalPoints.Select(p => bones[2].InverseTransformPoint(p)).OrderByDescending(p => p.sqrMagnitude).First();
            Require(Finite(tip) && tip.sqrMagnitude >= .000001f, prefix + ": terminal geometry provides no nonzero authored chain lever.");
            prepared.Chains.Add(new SecondaryBoneChainBinding { Name = prefix, Bones = bones, LastBoneTipLocal = tip });
        }

        private static List<Vector3> WeightedPoints(Prepared prepared, Transform pivot, bool rigid, SkinnedMeshRenderer only)
        {
            var result = new List<Vector3>();
            foreach (var renderer in prepared.Renderers)
            {
                if (only != null && renderer != only) continue;
                int boneIndex = Array.IndexOf(renderer.bones, pivot);
                if (boneIndex < 0) continue;
                Mesh mesh = renderer.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                using (var counts = mesh.GetBonesPerVertex())
                using (var weights = mesh.GetAllBoneWeights())
                {
                    Require(counts.Length == vertices.Length, renderer.name + ": full bone-weight stream is missing.");
                    int offset = 0;
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        float selected = 0f, other = 0f;
                        for (int influence = 0; influence < counts[vertex]; influence++)
                        {
                            Require(offset < weights.Length, renderer.name + ": malformed full weight stream.");
                            var weight = weights[offset++];
                            Require(Finite(weight.weight) && weight.weight >= 0f && weight.boneIndex >= 0 && weight.boneIndex < renderer.bones.Length,
                                renderer.name + ": invalid weight/bone index.");
                            if (weight.boneIndex == boneIndex) selected += weight.weight; else other += weight.weight;
                        }
                        if (selected <= 0f) continue;
                        if (rigid) Require(Mathf.Abs(selected - 1f) <= WeightTolerance && other <= WeightTolerance,
                            pivot.name + ": ornament vertices must be 100% assigned to their dedicated pivot; mixed weights are not rewritten.");
                        result.Add(renderer.transform.TransformPoint(vertices[vertex]));
                    }
                    Require(offset == weights.Length, renderer.name + ": bone-weight stream has unmatched entries.");
                }
            }
            return result;
        }

        [Serializable] public sealed class AnatomyCapsuleFit
        { public Vector3 start, end; public float radius; public int vertices; }

        /// <summary>Fits all supplied inner-body points; cloth anchors are deliberately not an input.</summary>
        public static AnatomyCapsuleFit[] FitAnatomySection(Vector3[] points, Vector3 axisStart, Vector3 axisEnd, Vector3 lateral, int columns)
        {
            if (points == null || points.Length < columns * 3 || points.Any(p => !Finite(p)) || !Finite(axisStart) || !Finite(axisEnd)
                || (columns != 1 && columns != 3) || (axisEnd - axisStart).sqrMagnitude < .000001f) throw new ArgumentException("Anatomy fitting requires finite real geometry and an explicit valid segment.");
            Vector3 axis = (axisEnd - axisStart).normalized;
            lateral -= axis * Vector3.Dot(lateral, axis);
            if (lateral.sqrMagnitude < .000001f) lateral = Vector3.Cross(axis, Math.Abs(axis.y) < .9f ? Vector3.up : Vector3.right);
            lateral.Normalize();
            float[] x = points.Select(p => Vector3.Dot(p - axisStart, lateral)).ToArray();
            float minimum = x.Min(), maximum = x.Max(), middle = (minimum + maximum) * .5f;
            float[] centers = columns == 1 ? new[] { middle } : new[] { (minimum + middle) * .5f, middle, (maximum + middle) * .5f };
            var assignments = new int[points.Length];
            for (int iteration = 0; iteration < 8; iteration++)
            {
                for (int i = 0; i < x.Length; i++) { int best = 0; for (int c = 1; c < columns; c++) if (Math.Abs(x[i] - centers[c]) < Math.Abs(x[i] - centers[best])) best = c; assignments[i] = best; }
                for (int c = 0; c < columns; c++) { var selected = Enumerable.Range(0, x.Length).Where(i => assignments[i] == c).ToArray(); if (selected.Length == 0) throw new ArgumentException("The requested anatomy columns have no distinct source geometry."); centers[c] = selected.Average(i => x[i]); }
            }
            var result = new List<AnatomyCapsuleFit>();
            for (int c = 0; c < columns; c++)
            {
                Vector3[] selected = Enumerable.Range(0, points.Length).Where(i => assignments[i] == c).Select(i => points[i]).ToArray();
                Vector3 center = Vector3.zero; foreach (Vector3 point in selected) center += point; center /= selected.Length;
                Vector3 basePoint = axisStart + (center - axisStart) - axis * Vector3.Dot(center - axisStart, axis);
                float low = selected.Min(p => Vector3.Dot(p - basePoint, axis)), high = selected.Max(p => Vector3.Dot(p - basePoint, axis));
                Vector3 a = basePoint + axis * low, b = basePoint + axis * high;
                float radius = selected.Max(p => DistanceToSegment(p, a, b));
                if (!Finite(radius) || radius <= .00001f) throw new ArgumentException("Inner-body geometry does not define a positive capsule radius.");
                result.Add(new AnatomyCapsuleFit { start = a, end = b, radius = radius, vertices = selected.Length });
            }
            return result.ToArray();
        }

        [Serializable] public sealed class TorsoSurfaceFitReport
        {
            public string method = "Geometry-only adaptive clipped-surface capsule partition; no pin input, mesh edits, radius shrink or surface deletion.";
            public string outwardScope = "64 actual capsule surface samples per candidate, nearest original BodyLining triangle and its outward normal. Sample maximum is not a certified continuous Hausdorff bound.";
            public int sourceVertices, sourceTriangles, clippedTriangles, capsuleCount, outwardSamples;
            public float sourceArea, clippedArea, areaDifference, maximumFragmentOutsideMeters, maximumSampledOutwardMeters;
            public bool allTriangleFragmentsCovered;
        }
        private struct TorsoTriangle
        {
            public Vector3 A, B, C;
            public TorsoTriangle(Vector3 a, Vector3 b, Vector3 c) { A = a; B = b; C = c; }
            public float Area => Vector3.Cross(B - A, C - A).magnitude * .5f;
        }
        private sealed class TorsoPatch
        {
            public List<TorsoTriangle> Triangles;
            public AnatomyCapsuleFit Fit;
            public double Cost;
            public float MaxOutward;
            public bool SplitComputed;
            public TorsoPatch Left, Right;
            public double SplitGain;
        }

        /// <summary>All original triangle surfaces are preserved by plane clipping and enclosed fragment-wise.
        /// Capsule count is bounded by the native budget. This computes a candidate, never collision approval.</summary>
        public static AnatomyCapsuleFit[] FitTorsoSurface(Vector3[] vertices, int[] indices, int maximumCapsules, out TorsoSurfaceFitReport report)
        {
            if (vertices == null || indices == null || vertices.Length < 4 || indices.Length < 12 || indices.Length % 3 != 0 ||
                vertices.Any(v => !Finite(v)) || indices.Any(i => i < 0 || i >= vertices.Length) || maximumCapsules < 1 || maximumCapsules > 17)
                throw new ArgumentException("Finite actual torso triangle geometry and a 1..17 native capsule budget are required.");
            var original = new List<TorsoTriangle>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                var triangle = new TorsoTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                if (triangle.Area <= 1e-12f) throw new ArgumentException("Degenerate source torso triangle requires authoring review.");
                original.Add(triangle);
            }
            // A signed nearest-face surface error assumes consistently outward, closed source geometry.
            // FBX UV seams may duplicate render vertices. Weld only this topology audit's
            // index view within 1 micrometer; fitting retains every original position/triangle.
            var representatives = new List<Vector3>(); var topologyIndex = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int match = representatives.FindIndex(p => (p - vertices[i]).sqrMagnitude <= 1e-12f);
                if (match < 0) { match = representatives.Count; representatives.Add(vertices[i]); }
                topologyIndex[i] = match;
            }
            var edgeCounts = new Dictionary<(int, int), (int count, int orientation)>();
            for (int i = 0; i < indices.Length; i += 3) for (int e = 0; e < 3; e++)
            {
                int a = topologyIndex[indices[i + e]], b = topologyIndex[indices[i + (e + 1) % 3]]; var key = (Math.Min(a, b), Math.Max(a, b));
                edgeCounts.TryGetValue(key, out var value);
                edgeCounts[key] = (value.count + 1, value.orientation + (a < b ? 1 : -1));
            }
            if (edgeCounts.Values.Any(v => v.count != 2 || v.orientation != 0))
                throw new ArgumentException("BodyLining must be a consistently wound closed indexed surface before proxy fitting.");
            double signedVolume = original.Sum(t => (double)Vector3.Dot(t.A, Vector3.Cross(t.B, t.C)) / 6d);
            if (signedVolume <= 0d) throw new ArgumentException("BodyLining winding must face outward.");
            var patches = new List<TorsoPatch> { FitPatch(original, original) };
            while (patches.Count < maximumCapsules)
            {
                TorsoPatch chosen = null;
                foreach (var patch in patches)
                {
                    PrepareSplit(patch, original);
                    if (patch.Left != null && (chosen == null || patch.SplitGain > chosen.SplitGain)) chosen = patch;
                }
                if (chosen == null) break;
                int index = patches.IndexOf(chosen); patches.RemoveAt(index);
                patches.Insert(index, chosen.Right); patches.Insert(index, chosen.Left);
            }
            report = new TorsoSurfaceFitReport { sourceVertices = vertices.Length, sourceTriangles = original.Count,
                sourceArea = original.Sum(t => t.Area), clippedArea = patches.Sum(p => p.Triangles.Sum(t => t.Area)),
                clippedTriangles = patches.Sum(p => p.Triangles.Count), capsuleCount = patches.Count,
                maximumSampledOutwardMeters = patches.Max(p => p.MaxOutward), outwardSamples = patches.Count * 64 };
            report.areaDifference = Math.Abs(report.clippedArea - report.sourceArea);
            foreach (var patch in patches) foreach (var triangle in patch.Triangles)
                foreach (var point in new[] { triangle.A, triangle.B, triangle.C })
                    report.maximumFragmentOutsideMeters = Math.Max(report.maximumFragmentOutsideMeters, DistanceToSegment(point, patch.Fit.start, patch.Fit.end) - patch.Fit.radius);
            report.allTriangleFragmentsCovered = report.maximumFragmentOutsideMeters <= .00001f && report.areaDifference <= Math.Max(1e-6f, report.sourceArea * .00001f);
            if (!report.allTriangleFragmentsCovered) throw new ArgumentException("Torso clipping/convex capsule enclosure did not conserve and cover the complete original surface.");
            return patches.Select(p => p.Fit).ToArray();
        }
        private static void PrepareSplit(TorsoPatch patch, List<TorsoTriangle> original)
        {
            if (patch.SplitComputed) return;
            patch.SplitComputed = true;
            var points = PatchPoints(patch.Triangles);
            for (int axis = 0; axis < 3; axis++)
            {
                float min = points.Min(p => p[axis]), max = points.Max(p => p[axis]);
                if (max - min < .001f) continue;
                float plane = (min + max) * .5f;
                var left = ClipPatch(patch.Triangles, axis, plane, true);
                var right = ClipPatch(patch.Triangles, axis, plane, false);
                if (left.Count == 0 || right.Count == 0) continue;
                var a = FitPatch(left, original); var b = FitPatch(right, original);
                double gain = patch.Cost - a.Cost - b.Cost;
                if (patch.Left == null || gain > patch.SplitGain) { patch.Left = a; patch.Right = b; patch.SplitGain = gain; }
            }
        }
        private static List<TorsoTriangle> ClipPatch(List<TorsoTriangle> triangles, int axis, float plane, bool lower)
        {
            var result = new List<TorsoTriangle>();
            foreach (var triangle in triangles)
            {
                var input = new[] { triangle.A, triangle.B, triangle.C }; var polygon = new List<Vector3>(4);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 a = input[i], b = input[(i + 1) % 3];
                    bool insideA = lower ? a[axis] <= plane : a[axis] >= plane;
                    bool insideB = lower ? b[axis] <= plane : b[axis] >= plane;
                    if (insideA) polygon.Add(a);
                    if (insideA != insideB) polygon.Add(a + (b - a) * ((plane - a[axis]) / (b[axis] - a[axis])));
                }
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    var piece = new TorsoTriangle(polygon[0], polygon[i], polygon[i + 1]);
                    if (piece.Area > 1e-12f) result.Add(piece);
                }
            }
            return result;
        }
        private static Vector3[] PatchPoints(List<TorsoTriangle> triangles)
        { return triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct().ToArray(); }
        private static TorsoPatch FitPatch(List<TorsoTriangle> triangles, List<TorsoTriangle> original)
        {
            Vector3[] points = PatchPoints(triangles); TorsoPatch best = null;
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                Vector2[] cross = points.Select(p => new Vector2(p[u], p[v])).ToArray();
                EnclosingCircle(cross, out Vector2 center, out float minimumRadius);
                AnatomyCapsuleFit candidate = null; double minimumVolume = double.MaxValue;
                foreach (float factor in new[] { 1f, 1.03f, 1.08f, 1.15f, 1.3f })
                {
                    float radius = Math.Max(.000001f, minimumRadius * factor + .0000005f);
                    float start = float.MaxValue, end = float.MinValue;
                    for (int i = 0; i < points.Length; i++)
                    {
                        float reach = (float)Math.Sqrt(Math.Max(0d, (double)radius * radius - (cross[i] - center).sqrMagnitude));
                        start = Math.Min(start, points[i][axis] + reach); end = Math.Max(end, points[i][axis] - reach);
                    }
                    if (start > end) start = end = (start + end) * .5f;
                    double volume = Math.PI * radius * radius * (end - start) + 4d / 3d * Math.PI * radius * radius * radius;
                    if (volume >= minimumVolume) continue;
                    minimumVolume = volume; Vector3 a = Vector3.zero, b = Vector3.zero;
                    a[axis] = start; b[axis] = end; a[u] = b[u] = center.x; a[v] = b[v] = center.y;
                    candidate = new AnatomyCapsuleFit { start = a, end = b, radius = radius, vertices = points.Length };
                }
                double cost = SampleOutward(candidate, axis, original, out float maxOutward);
                if (best == null || cost < best.Cost) best = new TorsoPatch { Triangles = triangles, Fit = candidate, Cost = cost, MaxOutward = maxOutward };
            }
            return best;
        }
        private static void EnclosingCircle(Vector2[] source, out Vector2 center, out float radius)
        {
            var points = (Vector2[])source.Clone(); var random = new System.Random(73);
            for (int i = points.Length - 1; i > 0; i--) { int j = random.Next(i + 1); var swap = points[i]; points[i] = points[j]; points[j] = swap; }
            Vector2 c = points[0]; float r = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i]; if ((a - c).sqrMagnitude <= r * r + 1e-12f) continue;
                c = a; r = 0f;
                for (int j = 0; j < i; j++)
                {
                    Vector2 b = points[j]; if ((b - c).sqrMagnitude <= r * r + 1e-12f) continue;
                    c = (a + b) * .5f; r = Vector2.Distance(a, b) * .5f;
                    for (int k = 0; k < j; k++)
                    {
                        Vector2 d = points[k]; if ((d - c).sqrMagnitude <= r * r + 1e-12f) continue;
                        Vector2 ab = b - a, ad = d - a;
                        double determinant = 2d * ((double)ab.x * ad.y - (double)ab.y * ad.x);
                        if (Math.Abs(determinant) < 1e-14d) continue;
                        c = a + new Vector2((float)((ad.y * (double)ab.sqrMagnitude - ab.y * (double)ad.sqrMagnitude) / determinant),
                            (float)((ab.x * (double)ad.sqrMagnitude - ad.x * (double)ab.sqrMagnitude) / determinant));
                        r = Vector2.Distance(a, c);
                    }
                }
            }
            // Explicit full-point enclosure also guards nearly collinear floating-point circle cases.
            center = c; radius = points.Max(p => Vector2.Distance(p, c));
        }
        private static double SampleOutward(AnatomyCapsuleFit fit, int axis, List<TorsoTriangle> original, out float maximum)
        {
            int u = (axis + 1) % 3, v = (axis + 2) % 3; float radius = fit.radius;
            double sum = 0d; float max = 0f; int count = 0;
            void Sample(Vector3 point)
            {
                float nearest = float.MaxValue, signed = 0f;
                foreach (var triangle in original)
                {
                    Vector3 closest = ClosestTrianglePoint(point, triangle.A, triangle.B, triangle.C);
                    float distance = (point - closest).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    signed = Vector3.Dot(point - closest, Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A));
                }
                float outside = signed >= 0f ? (float)Math.Sqrt(nearest) : 0f;
                max = Math.Max(max, outside); sum += outside * outside; count++;
            }
            for (int ring = -2; ring <= 2; ring++) for (int sector = 0; sector < 8; sector++)
            {
                float s = ring * .5f; double angle = sector * Math.PI / 4d;
                Vector3 point = s < 0f ? fit.start : fit.end;
                point[axis] += s * radius;
                point[u] += (float)(Math.Sqrt(Math.Max(0d, 1d - s * s)) * radius * Math.Cos(angle));
                point[v] += (float)(Math.Sqrt(Math.Max(0d, 1d - s * s)) * radius * Math.Sin(angle)); Sample(point);
            }
            for (int ring = 1; ring <= 3; ring++) for (int sector = 0; sector < 8; sector++)
            {
                Vector3 point = Vector3.Lerp(fit.start, fit.end, ring * .25f); double angle = sector * Math.PI / 4d;
                point[u] += (float)(radius * Math.Cos(angle)); point[v] += (float)(radius * Math.Sin(angle)); Sample(point);
            }
            maximum = max;
            double area = 2d * Math.PI * radius * Vector3.Distance(fit.start, fit.end) + 4d * Math.PI * radius * radius;
            return area * (sum / count + .05d * max * max);
        }
        public static Vector3 ClosestTrianglePoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = 1f / (va + vb + vc); return a + ab * (vb * denominator) + ac * (vc * denominator);
        }

        private static List<CapsulePlan> PrepareCapsules(Prepared prepared, SkinnedMeshRenderer[] clothSurfaces, BindingReport report)
        {
            Transform Bone(HumanBodyBones bone)
            {
                Transform result = prepared.Animator.GetBoneTransform(bone);
                Require(result != null, prepared.Root.name + ": missing proxy anchor " + bone);
                return result;
            }
            var result = new List<CapsulePlan>();
            var sources = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
            void Source(string meshName, HumanBodyBones[] anchors, int columns)
            {
                var renderer = Surface(prepared, meshName); Vector3[] points = BakeWorld(renderer); sources.Add(renderer, points);
                Transform[] bones = anchors.Select(Bone).ToArray(); var selected = new List<Vector3>[bones.Length - 1];
                for (int i = 0; i < selected.Length; i++) selected[i] = new List<Vector3>();
                foreach (Vector3 point in points)
                {
                    int nearest = 0; for (int i = 1; i < selected.Length; i++)
                        if (DistanceToSegment(point, bones[i].position, bones[i + 1].position) < DistanceToSegment(point, bones[nearest].position, bones[nearest + 1].position)) nearest = i;
                    selected[nearest].Add(point);
                }
                for (int i = 0; i < selected.Length; i++)
                {
                    Require(selected[i].Count >= columns * 3, meshName + ": anatomical segment has insufficient real geometry; do not infer a radius.");
                    var fits = FitAnatomySection(selected[i].ToArray(), bones[i].position, bones[i + 1].position, prepared.Root.transform.right, columns);
                    for (int c = 0; c < fits.Length; c++) result.Add(new CapsulePlan { Name = ProxyPrefix + meshName.Replace("DosaV2_", "") + "_" + i + "_" + c,
                        Start = bones[i], End = bones[i + 1], WorldStart = fits[c].start, WorldEnd = fits[c].end, Radius = fits[c].radius,
                        SourceRenderer = renderer.name, SourcePath = AssetDatabase.GetAssetPath(renderer.sharedMesh), FitVertices = fits[c].vertices });
                }
            }
            var body = Surface(prepared, "DosaV2_BodyLining"); var bodyPoints = BakeWorld(body); sources.Add(body, bodyPoints);
            Vector3[] localBody = bodyPoints.Select(prepared.Root.transform.InverseTransformPoint).ToArray();
            var torso = FitTorsoSurface(localBody, body.sharedMesh.triangles, 17, out var torsoReport);
            report.torsoSurfaceFit = torsoReport;
            Transform[] torsoBones = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck }.Select(Bone).ToArray();
            float bodyScale = prepared.Root.transform.lossyScale.x;
            for (int i = 0; i < torso.Length; i++)
            {
                Vector3 a = prepared.Root.transform.TransformPoint(torso[i].start), b = prepared.Root.transform.TransformPoint(torso[i].end), midpoint = (a + b) * .5f;
                int nearest = 0; for (int k = 1; k + 1 < torsoBones.Length; k++)
                    if (DistanceToSegment(midpoint, torsoBones[k].position, torsoBones[k + 1].position) < DistanceToSegment(midpoint, torsoBones[nearest].position, torsoBones[nearest + 1].position)) nearest = k;
                result.Add(new CapsulePlan { Name = ProxyPrefix + "BodyLining_Surface_" + i, Start = torsoBones[nearest], End = torsoBones[nearest + 1],
                    WorldStart = a, WorldEnd = b, Radius = torso[i].radius * bodyScale, SourceRenderer = body.name, SourcePath = AssetDatabase.GetAssetPath(body.sharedMesh), FitVertices = torso[i].vertices });
            }
            foreach(string armName in new[] { "DosaV2_ArmLining_Left", "DosaV2_ArmLining_Right" })
            {
                var renderer=Surface(prepared,armName);sources.Add(renderer,BakeWorld(renderer));
                PlayerClothBodyProxyRig.AuthorRestBinding(renderer,prepared.Root.transform,out var fitted);
                for(int region=0;region<fitted.Length;region++)
                    result.Add(new CapsulePlan {Name=ProxyPrefix+armName.Replace("DosaV2_", "")+"_Posed_"+region,
                        Start=prepared.Root.transform,End=prepared.Root.transform,
                        WorldStart=prepared.Root.transform.TransformPoint(fitted[region].start),WorldEnd=prepared.Root.transform.TransformPoint(fitted[region].end),
                        Radius=fitted[region].radius*bodyScale,SourceRenderer=renderer.name,SourcePath=AssetDatabase.GetAssetPath(renderer.sharedMesh),FitVertices=fitted[region].corners});
            }
            Source("DosaV2_LegLining_Left", new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, 1);
            Source("DosaV2_LegLining_Right", new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }, 1);
            foreach (var source in sources)
            {
                var coverage = new CoverageReport { sourceRenderer = source.Key.name, sourcePath = AssetDatabase.GetAssetPath(source.Key.sharedMesh), checkedVertices = source.Value.Length };
                foreach (Vector3 point in source.Value)
                {
                    float signedDistance = result.Min(p => DistanceToSegment(point, p.WorldStart, p.WorldEnd) - p.Radius);
                    if (signedDistance > .00001f) coverage.uncoveredVertices++;
                    coverage.maxOutsideMeters = Mathf.Max(coverage.maxOutsideMeters, signedDistance);
                    coverage.maxInsideDepthMeters = Mathf.Max(coverage.maxInsideDepthMeters, -signedDistance);
                }
                report.anatomyCoverage.Add(coverage);
            }
            // This independent overlap diagnostic never feeds the fit above.
            foreach (var renderer in clothSurfaces)
            {
                Vector3[] points = BakeWorld(renderer); Color[] colors = GetAuthoredClothValues(renderer.sharedMesh, clothSurfaces.Length == 8);
                for (int v = 0; v < points.Length; v++) if (colors[v].r == 0f)
                {
                    report.checkedPinnedVertices++;
                    foreach (var proxy in result)
                    {
                        float depth = proxy.Radius - DistanceToSegment(points[v], proxy.WorldStart, proxy.WorldEnd);
                        if (depth <= .00001f) continue;
                        report.initialPinnedProxyIntersections++; report.maxInitialPinnedPenetrationMeters = Mathf.Max(report.maxInitialPinnedPenetrationMeters, depth);
                        report.worstInitialPinnedOverlaps.Add(new PinnedOverlap { surface = renderer.name, proxy = proxy.Name, vertex = v,
                            depthMeters = depth, positionRepresentation = prepared.Root.transform.InverseTransformPoint(points[v]) });
                    }
                }
            }
            report.worstInitialPinnedOverlaps = report.worstInitialPinnedOverlaps.OrderByDescending(p => p.depthMeters).Take(32).ToList();
            report.proxyFitStatus = "MEASURED_REST_INNER_VERTEX_COVERAGE_PENDING_PHYSICS";
            return result;
        }

        private static Vector3[] BakeWorld(SkinnedMeshRenderer renderer)
        {
            var baked = new Mesh();
            try { renderer.BakeMesh(baked, false); Require(baked.vertexCount == renderer.sharedMesh.vertexCount, renderer.name + ": actual BakeMesh lost source indices.");
                var points = baked.vertices.Select(renderer.transform.TransformPoint).ToArray(); Require(points.All(Finite), renderer.name + ": nonfinite anatomy geometry."); return points; }
            finally { Object.DestroyImmediate(baked); }
        }
        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        { Vector3 delta = b - a; float t = delta.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude) : 0f; return Vector3.Distance(point, a + delta * t); }

        private static CapsuleCollider CreateCapsule(CapsulePlan plan, Transform representation, List<Object> created, BindingReport report)
        {
            var obj = new GameObject(plan.Name) { layer = 2 }; created.Add(obj);
            Vector3 delta = plan.WorldEnd - plan.WorldStart;
            obj.transform.SetParent(plan.Start, false);
            obj.transform.SetPositionAndRotation((plan.WorldStart + plan.WorldEnd) * .5f, delta.sqrMagnitude > 1e-12f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity);
            float scale = obj.transform.lossyScale.x;
            var capsule = obj.AddComponent<CapsuleCollider>();
            capsule.direction = 1; capsule.center = Vector3.zero;
            capsule.radius = plan.Radius / scale; capsule.height = (delta.magnitude + 2f * plan.Radius) / scale;
            capsule.isTrigger = true; capsule.excludeLayers = -1; capsule.includeLayers = 0;
            Require(PlayerSecondaryMotionRig.ValidateProxy(capsule, out string error), plan.Name + ": " + error);
            Vector3 localA = representation.InverseTransformPoint(plan.WorldStart), localB = representation.InverseTransformPoint(plan.WorldEnd);
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", plan.SourcePath));
            report.proxies.Add(new ProxyReport { name = plan.Name, anchorBone = Normalize(plan.Start.name), endpointBone = Normalize(plan.End.name),
                updateMode=plan.Name.Contains("_Posed_") ? "SKINNED_TRIANGLE_FIT" : "RIGID_BONE_ANCHOR",
                startBone = Normalize(plan.Start.name), endBone = Normalize(plan.Start.name), startUnityBoneLocal = plan.Start.InverseTransformPoint(plan.WorldStart), endUnityBoneLocal = plan.Start.InverseTransformPoint(plan.WorldEnd),
                midpoint = obj.transform.position, radiusMeters = plan.Radius, heightMeters = delta.magnitude + 2f * plan.Radius,
                startRepresentation = localA, endRepresentation = localB, startBlender = new Vector3(-localA.x, -localA.z, localA.y), endBlender = new Vector3(-localB.x, -localB.z, localB.y),
                sourceRenderer = plan.SourceRenderer, sourcePath = plan.SourcePath, fitVertices = plan.FitVertices,
                sourceSha256 = HashFile(fullPath), sourceMetaSha256 = File.Exists(fullPath + ".meta") ? HashFile(fullPath + ".meta") : null });
            return capsule;
        }
        private static string HashFile(string path) { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return string.Concat(sha.ComputeHash(file).Select(v => v.ToString("x2"))); }

        private static PlayerSecondaryMotionProfileSO Profile(string path, List<PlayerSecondaryMotionProfileSO> temporary)
        {
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            Require(existing == null || existing is PlayerSecondaryMotionProfileSO, path + ": an unrelated asset occupies the intended profile path.");
            if (existing is PlayerSecondaryMotionProfileSO profile) return profile;
            profile = ScriptableObject.CreateInstance<PlayerSecondaryMotionProfileSO>();
            profile.name = Path.GetFileNameWithoutExtension(path);
            // Keep the serializable profile's review defaults; do not silently overwrite later manual tuning.
            temporary.Add(profile); return profile;
        }

        private static void PersistProfile(PlayerSecondaryMotionProfileSO profile, string path,
            List<PlayerSecondaryMotionProfileSO> temporary, List<string> createdAssets)
        {
            if (!temporary.Contains(profile)) return;
            Require(AssetDatabase.LoadMainAssetAtPath(path) == null, path + ": a profile was created concurrently; preserve it and retry with a fresh instance.");
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateAsset(profile, path); createdAssets.Add(path);
            AssetDatabase.SaveAssetIfDirty(profile);
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>
        /// Maps authored colors through native welded/reordered Cloth particles. Source order is never assumed.
        /// https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Cloth-vertices.html
        /// Pure data helper; no Unity objects, meshes or coefficients are modified on either success or failure.
        /// </summary>
        public static bool TryMapAuthoredCoefficients(Vector3[] restPositions, Color[] colors, Vector3[] clothParticles,
            out ClothSkinningCoefficient[] coefficients, out ParticleMappingReport report)
        {
            coefficients = Array.Empty<ClothSkinningCoefficient>(); report = new ParticleMappingReport();
            if (restPositions == null || restPositions.Length == 0 || colors == null || colors.Length != restPositions.Length)
            { report.error = "Missing per-vertex authored rest positions/colors."; return false; }
            if (clothParticles == null || clothParticles.Length == 0)
            { report.error = "Native Cloth particle positions are not initialized."; return false; }
            report.sourceVertices = restPositions.Length; report.particles = clothParticles.Length;
            var cells = new Dictionary<Cell, List<int>>();
            for (int i = 0; i < restPositions.Length; i++)
            {
                float distance = colors[i].r;
                if (!Finite(restPositions[i]) || !Finite(distance) || distance < 0f || distance > 1f)
                { report.error = "Nonfinite rest position or maxDistance outside authored [0, 1] meters."; return false; }
                if (distance == 0f) report.pinnedSourceVertices++; else report.freeSourceVertices++;
                report.maximumDistance = Math.Max(report.maximumDistance, distance);
                Cell cell = Cell.At(restPositions[i]);
                if (!cells.TryGetValue(cell, out List<int> indices)) cells.Add(cell, indices = new List<int>());
                indices.Add(i);
            }
            if (report.pinnedSourceVertices == 0 || report.freeSourceVertices == 0)
            { report.error = "Authored exact-zero seam pins and free vertices are both required."; return false; }
            var mapped = new ClothSkinningCoefficient[clothParticles.Length];
            var covered = new bool[restPositions.Length];
            float toleranceSquared = PositionTolerance * PositionTolerance;
            for (int particle = 0; particle < clothParticles.Length; particle++)
            {
                Vector3 point = clothParticles[particle];
                if (!Finite(point)) { report.error = "Nonfinite native particle position."; return false; }
                Cell center = Cell.At(point); int matches = 0; float value = 0f, nearestError = float.MaxValue;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue(new Cell(center.X + x, center.Y + y, center.Z + z), out List<int> candidates)) continue;
                    foreach (int index in candidates)
                    {
                        Vector3 offset = restPositions[index] - point; float distanceSquared = offset.sqrMagnitude;
                        if (distanceSquared > toleranceSquared) continue;
                        if (matches > 0 && colors[index].r != value)
                        { report.error = "Ambiguous welded/rest-position match has conflicting red constraints at particle " + particle + "; colors were not averaged or replaced."; return false; }
                        value = colors[index].r; matches++; covered[index] = true; nearestError = Math.Min(nearestError, distanceSquared);
                    }
                }
                if (matches == 0) { report.error = "No authored rest-position match for native particle " + particle + "; verify rest pose/space and importer output."; return false; }
                if (matches > 1) report.duplicateMatches += matches - 1;
                report.maximumPositionError = Math.Max(report.maximumPositionError, (float)Math.Sqrt(nearestError));
                mapped[particle] = new ClothSkinningCoefficient { maxDistance = value, collisionSphereDistance = 0f };
                if (value == 0f) report.pinnedParticles++; else report.freeParticles++;
            }
            if (covered.Any(value => !value)) { report.error = "Some authored vertices do not map to any native particle; unused or mismatched geometry must be inspected."; return false; }
            if (report.pinnedParticles == 0 || report.freeParticles == 0) { report.error = "Native mapping lost authored seam pins or free particles."; return false; }
            coefficients = mapped; return true;
        }

        private readonly struct Cell : IEquatable<Cell>
        {
            public readonly long X, Y, Z;
            public Cell(long x, long y, long z) { X = x; Y = y; Z = z; }
            public static Cell At(Vector3 p) => new Cell((long)Math.Floor(p.x / PositionTolerance), (long)Math.Floor(p.y / PositionTolerance), (long)Math.Floor(p.z / PositionTolerance));
            public bool Equals(Cell other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is Cell other && Equals(other);
            public override int GetHashCode() { unchecked { return X.GetHashCode() * 73856093 ^ Y.GetHashCode() * 19349663 ^ Z.GetHashCode() * 83492791; } }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void Uniform(Transform transform)
        {
            Vector3 scale = transform.lossyScale;
            Require(Finite(scale) && scale.x > 0f && Mathf.Abs(scale.x - scale.y) <= .0001f && Mathf.Abs(scale.x - scale.z) <= .0001f,
                transform.name + ": positive uniform world scale is required; apply authoring scale before binding.");
        }
        private static void Require(bool condition, string reason) { if (!condition) throw new PendingAuthoring(reason); }
    }
}
