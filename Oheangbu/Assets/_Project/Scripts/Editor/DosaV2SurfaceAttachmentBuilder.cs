using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.Editor
{
    /// <summary>Authors only the three explicitly manifested auxiliary belt anchors on a fresh rest-pose world.</summary>
    public static class DosaV2SurfaceAttachmentBuilder
    {
        // Published FBX/Blender positional round-trip tolerance; not an attachment clearance threshold.
        public const float PositionIdentityToleranceMeters = .00001f;
        public const float WeightIdentityTolerance = .00001f;
        private static readonly string[] Roles = { "Bottle", "UpperCase", "FlatCase" };

        public static string Bind(GameObject world, string manifestPath = null)
            => Execute(world, null, manifestPath, true);
        public static string Bind(GameObject world, SkinnedMeshRenderer explicitBodyCore, string manifestPath)
            => Execute(world, explicitBodyCore, manifestPath, true);
        public static string Inspect(GameObject world, SkinnedMeshRenderer explicitBodyCore = null, string manifestPath = null)
            => Execute(world, explicitBodyCore, manifestPath, false);

        private static string Execute(GameObject world, SkinnedMeshRenderer explicitSource, string manifestPath, bool apply)
        {
            var report = new JObject { ["status"] = "WAIT", ["applied"] = false,
                ["scope"] = "Imported rest triangle identity and serialized authoring only; no dynamic or RIG_PASS result.",
                ["positionIdentityToleranceMeters"] = PositionIdentityToleranceMeters,
                ["weightIdentityTolerance"] = WeightIdentityTolerance };
            Mesh bake = null;
            PlayerSkinnedSurfaceAttachmentRig created = null;
            try
            {
                Need(!Application.isPlaying, "Authoring requires an isolated fresh EDIT-mode rest instance.");
                Need(world != null && !EditorUtility.IsPersistent(world), "Expected an explicit disposable world instance, not a prefab asset.");
                Need(world.transform.lossyScale.x > 0 && Mathf.Abs(world.transform.lossyScale.x - world.transform.lossyScale.y) < 1e-5f && Mathf.Abs(world.transform.lossyScale.x - world.transform.lossyScale.z) < 1e-5f,
                    "World representation scale must be positive and uniform.");
                foreach (var animator in world.GetComponentsInChildren<Animator>(true))
                    Need(animator.runtimeAnimatorController == null, "Production animation/controller must not run during rest attachment authoring.");
                // Replacing an existing serialized component needs an explicit rebuild, so a failed rebind cannot destroy a working binding.
                Need(world.GetComponent<PlayerSkinnedSurfaceAttachmentRig>() == null, "Existing attachment component: inspect/rebuild a fresh instance instead of overwriting it.");
                manifestPath = Path.GetFullPath(manifestPath ?? Path.Combine(Application.dataPath, "../../Art/PlayerV2/Inspect/WaistSideSplitd979/repair-merge-manifest.json"));
                Need(File.Exists(manifestPath), "Frozen attachment manifest missing.");
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                report["manifestPath"] = manifestPath; report["manifestSha256"] = HashFile(manifestPath);
                string candidate = RequiredString(manifest, "candidate"), original = RequiredString(manifest, "source");
                VerifyFile(candidate, RequiredString(manifest, "candidateSha256"));
                VerifyFile(original, RequiredString(manifest, "sourceSha256"));
                report["candidateSha256"] = manifest["candidateSha256"]; report["frozenSourceSha256"] = manifest["sourceSha256"];
                var records = manifest["attachments"] as JArray;
                Need(records != null && records.Count == 3, "Expected exactly the three manifested belt anchors.");
                Need(records.Select(x => (string)x["role"]).OrderBy(x => x).SequenceEqual(Roles.OrderBy(x => x)), "Unknown, missing or duplicate attachment role.");
                var sources = world.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(x => x.name == "DosaV2_BodyCore").ToArray();
                SkinnedMeshRenderer source = explicitSource;
                if (source == null)
                {
                    Need(sources.Length == 1, "BodyCore must be unique. With LOD copies, pass the explicit LOD0 BodyCore renderer.");
                    source = sources[0];
                }
                Need(source.transform.IsChildOf(world.transform) && source.name == "DosaV2_BodyCore", "Explicit source must be the world BodyCore descendant.");
                Mesh mesh = source.sharedMesh;
                Need(mesh != null && mesh.isReadable && mesh.blendShapeCount == 0 && source.GetComponent<Cloth>() == null,
                    "BodyCore must have a readable, unshaped, unsimulated source mesh.");
                int[] triangles = mesh.triangles;
                Need(triangles.Length > 0 && triangles.Length % 3 == 0, "BodyCore has no complete triangle topology.");
                var weights = ReadNamedWeights(source);
                // Bake independent BodyCore at rest, never the accessory or a circular anchor-driven renderer.
                bake = new Mesh { name = "TemporarySurfaceAttachmentRestBake", hideFlags = HideFlags.HideAndDontSave };
                source.BakeMesh(bake, false);
                Vector3[] baked = bake.vertices;
                Need(baked.Length == mesh.vertexCount, "Rest BakeMesh changed source vertex identity.");
                var modelPoints = new Vector3[baked.Length]; var worldPoints = new Vector3[baked.Length];
                Matrix4x4 toWorld = source.transform.localToWorldMatrix, toModel = world.transform.worldToLocalMatrix * toWorld;
                for (int i = 0; i < baked.Length; i++)
                {
                    modelPoints[i] = toModel.MultiplyPoint3x4(baked[i]); worldPoints[i] = toWorld.MultiplyPoint3x4(baked[i]);
                    Need(Finite(modelPoints[i]) && Finite(worldPoints[i]), "Nonfinite rest source geometry.");
                }
                string assetPath = AssetDatabase.GetAssetPath(mesh);
                var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                float importerMinBoneWeight = importer != null ? importer.minBoneWeight : 0f;
                report["importerMinBoneWeight"] = importerMinBoneWeight;
                report["weightIdentityPolicy"] = "All retained bone identities must match. A missing original influence must be below the actual importer cutoff, with TOTAL missing mass <= existing1e-5 weight tolerance; retained values must match Unity renormalization. No nearest geometry fallback.";
                string absoluteAsset = string.IsNullOrEmpty(assetPath) ? null : Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                report["sourceAssetPath"] = assetPath; report["importedAssetSha256"] = absoluteAsset != null && File.Exists(absoluteAsset) ? HashFile(absoluteAsset) : null;
                report["sourceVertexCount"] = mesh.vertexCount; report["sourceTriangleCount"] = triangles.Length / 3;
                report["importedRestGeometryAndWeightSha256"] = HashRestGeometry(modelPoints, triangles, weights);
                var bindings = new List<PlayerSkinnedSurfaceAttachmentRig.Binding>(); var rows = new JArray();
                var mappingDiagnostics = new JArray(); report["mappingDiagnostics"] = mappingDiagnostics; report["bindings"] = rows;
                foreach (var token in records)
                {
                    var record = (JObject)token;
                    Need((string)record["sourceBodyObject"] == source.name, "Manifest source object differs.");
                    Need((string)record["sourceFileSha256"] == (string)manifest["sourceSha256"], "Attachment source hash differs from the frozen manifest.");
                    Transform anchor = UniqueBone(world.transform, RequiredString(record, "anchorBone"));
                    Transform child = UniqueBone(world.transform, RequiredString(record, "rigidBone"));
                    Need(child.parent == anchor && anchor.parent != null && anchor.parent.name == "Hips", "Unexpected anchor/child parent topology.");
                    Need((anchor.localScale - Vector3.one).sqrMagnitude < 1e-10f && (child.localScale - Vector3.one).sqrMagnitude < 1e-10f, "Auxiliary rest bone scale changed.");
                    var p = record["sourceTriangleRestPositions"] as JArray;
                    var w = record["sourceBodyVertexWeights"] as JArray;
                    Need(p != null && p.Count == 3 && w != null && w.Count == 3, "Incomplete three-corner identity.");
                    var expected = p.Select(x => BlenderToUnity(ReadVector(x))).ToArray();
                    var expectedWeights = w.Select(ReadExpectedWeights).ToArray();
                    int[] match = null; int matchedTriangle = -1; int matches = 0;
                    int[] nearest = null, nearestWeighted = null; int nearestTriangle = -1, nearestWeightedTriangle = -1;
                    float nearestMax = float.PositiveInfinity, nearestWeightedMax = float.PositiveInfinity;
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int[] indices = { triangles[t], triangles[t + 1], triangles[t + 2] };
                        for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++)
                        {
                            if (a == b) continue; int c = 3 - a - b;
                            int[] ordered = { indices[a], indices[b], indices[c] };
                            bool sameWeights = true; float maximumDistance = 0;
                            for (int corner = 0; corner < 3; corner++)
                            {
                                int vi = ordered[corner];
                                Need(vi >= 0 && vi < modelPoints.Length, "Invalid imported source triangle index.");
                                maximumDistance = Mathf.Max(maximumDistance, (modelPoints[vi] - expected[corner]).magnitude);
                                sameWeights &= SameWeights(weights[vi], expectedWeights[corner], importerMinBoneWeight);
                            }
                            if (maximumDistance < nearestMax) { nearestMax = maximumDistance; nearest = ordered; nearestTriangle = t / 3; }
                            if (sameWeights && maximumDistance < nearestWeightedMax) { nearestWeightedMax = maximumDistance; nearestWeighted = ordered; nearestWeightedTriangle = t / 3; }
                            bool same = sameWeights && maximumDistance <= PositionIdentityToleranceMeters;
                            if (!same) continue;
                            matches++; match = ordered; matchedTriangle = t / 3;
                        }
                    }
                    var mapping = new JObject { ["role"] = (string)record["role"], ["originalTriangle"] = record["sourceBodyTriangle"],
                        ["expectedBlenderRestCorners"] = p.DeepClone(), ["expectedUnityModelCorners"] = new JArray(expected.Select(Vec)),
                        ["expectedFullCornerWeights"] = w.DeepClone(), ["exactCompleteTriangleMatches"] = matches,
                        ["sourceRendererLocalPosition"] = Vec(source.transform.localPosition),
                        ["sourceRendererLocalEuler"] = Vec(source.transform.localEulerAngles), ["sourceRendererLossyScale"] = Vec(source.transform.lossyScale),
                        ["worldEuler"] = Vec(world.transform.eulerAngles), ["worldLossyScale"] = Vec(world.transform.lossyScale),
                        ["nearestGeometryTriangle"] = DiagnosticTriangle(nearestTriangle, nearest, nearestMax, mesh, source, world.transform, baked, modelPoints, weights, expected),
                        ["nearestSameWeightsTriangle"] = DiagnosticTriangle(nearestWeightedTriangle, nearestWeighted, nearestWeightedMax, mesh, source, world.transform, baked, modelPoints, weights, expected),
                        ["diagnosticOnlyNoNearestFallback"] = true };
                    mappingDiagnostics.Add(mapping);
                    Need(matches == 1, matches == 0 ? "No exact rest-position and bone-weight complete triangle match for " + anchor.name + ". nearest complete geometry max=" + nearestMax.ToString("R") + "m; same-weights max=" + nearestWeightedMax.ToString("R") + "m. See mappingDiagnostics; no fallback applied." : "Ambiguous complete triangle identity for " + anchor.name + "; no closest-match fallback.");
                    Vector3 bary = ReadVector(record["sourceBarycentric"]);
                    Need(bary.x >= 0 && bary.y >= 0 && bary.z >= 0 && Mathf.Abs(bary.x + bary.y + bary.z - 1) <= 1e-5f, "Invalid source barycentric weights.");
                    Need(PlayerSkinnedSurfaceAttachmentRig.TrySurfaceFrame(worldPoints[match[0]], worldPoints[match[1]], worldPoints[match[2]], bary, out Vector3 origin, out Quaternion frame), "Collapsed imported rest attachment triangle.");
                    // Compute in the runtime's actual Unity frame convention, rather than converting Blender Euler rotations.
                    Quaternion inverse = Quaternion.Inverse(frame);
                    var binding = new PlayerSkinnedSurfaceAttachmentRig.Binding { Name = (string)record["role"], Anchor = anchor, Source = source,
                        A = match[0], B = match[1], C = match[2], Barycentric = bary,
                        PositionInFrame = inverse * (anchor.position - origin), RotationInFrame = inverse * anchor.rotation };
                    // Even an exact triangle cannot excuse a wrong initial pivot. The rest source says these anchors lie on the belt.
                    Vector3 expectedAnchor = BlenderToUnity(ReadVector(record["restSurfacePoint"]));
                    Need((world.transform.InverseTransformPoint(anchor.position) - expectedAnchor).magnitude <= PositionIdentityToleranceMeters,
                        "Imported auxiliary pivot differs from its manifested rest position: " + anchor.name);
                    foreach (int vi in match) foreach (var pair in weights[vi])
                    {
                        Transform influence = UniqueBone(world.transform, pair.Key);
                        Need(influence != anchor && !influence.IsChildOf(anchor), "Circular source influence.");
                    }
                    bindings.Add(binding);
                    rows.Add(new JObject { ["role"] = binding.Name, ["anchor"] = anchor.name, ["rigidChild"] = child.name,
                        ["originalTriangle"] = record["sourceBodyTriangle"], ["importedTriangle"] = matchedTriangle,
                        ["importedCornersInOriginalBarycentricOrder"] = new JArray(match), ["barycentric"] = Vec(bary),
                        ["maximumCornerPositionErrorMeters"] = match.Select((vi, k) => (modelPoints[vi] - expected[k]).magnitude).Max(),
                        ["allOriginalPositiveBoneNamesRetained"] = match.Select((vi, k) => weights[vi].Count == expectedWeights[k].Count).All(x => x),
                        ["boneWeightsMatchImportedNormalization"] = true,
                        ["importedWeightComparisons"] = new JArray(match.Select((vi, k) => WeightComparison(weights[vi], expectedWeights[k]))), ["positionInRuntimeFrame"] = Vec(binding.PositionInFrame),
                        ["rotationInRuntimeFrame"] = new JArray(binding.RotationInFrame.x, binding.RotationInFrame.y, binding.RotationInFrame.z, binding.RotationInFrame.w),
                        ["restBarycentricAnchorDistanceMeters"] = (anchor.position - origin).magnitude });
                }
                report["bindings"] = rows;
                if (apply)
                {
                    created = world.AddComponent<PlayerSkinnedSurfaceAttachmentRig>();
                    Need(created != null && created.Configure(bindings.ToArray()), created != null ? created.LastError : "Could not add attachment component.");
                    EditorUtility.SetDirty(created);
                    // The builder serializes authoring only. It deliberately does not run spring/attachment pose evaluation.
                    report["applied"] = true; report["status"] = "BOUND_STATIC_PENDING_PLAY"; created = null;
                }
                else report["status"] = "READY_TO_BIND_STATIC_ONLY";
            }
            catch (Exception e) { report["status"] = "WAIT"; report["error"] = e.GetBaseException().Message; }
            finally
            {
                if (created != null) UnityEngine.Object.DestroyImmediate(created);
                if (bake != null) UnityEngine.Object.DestroyImmediate(bake);
            }
            return report.ToString();
        }

        private static JToken DiagnosticTriangle(int triangle, int[] indices, float maxDistance, Mesh mesh,
            SkinnedMeshRenderer source, Transform model, Vector3[] baked, Vector3[] modelPoints,
            Dictionary<string, float>[] weights, Vector3[] expected)
        {
            if (indices == null) return JValue.CreateNull();
            var corners = new JArray(); Vector3[] bindPoints = mesh.vertices; Matrix4x4[] bindposes = mesh.bindposes; Transform[] bones = source.bones;
            for (int k = 0; k < 3; k++)
            {
                int v = indices[k]; Vector3 cpuWorld = Vector3.zero;
                foreach (var weight in weights[v])
                {
                    int bi = Array.FindIndex(bones, bone => bone != null && bone.name == weight.Key);
                    Need(bi >= 0, "Diagnostic source weight bone is missing.");
                    cpuWorld += (bones[bi].localToWorldMatrix * bindposes[bi]).MultiplyPoint3x4(bindPoints[v]) * weight.Value;
                }
                var named = new JObject(); foreach (var weight in weights[v]) named[weight.Key] = weight.Value;
                corners.Add(new JObject { ["sourceVertex"] = v, ["distanceMeters"] = (modelPoints[v] - expected[k]).magnitude,
                    ["bakedRendererLocal"] = Vec(baked[v]), ["bakedModel"] = Vec(modelPoints[v]),
                    ["sourceBindVertex"] = Vec(bindPoints[v]), ["independentCpuSkinModel"] = Vec(model.InverseTransformPoint(cpuWorld)),
                    ["allNamedWeights"] = named });
            }
            return new JObject { ["triangle"] = triangle, ["maxCornerDistanceMeters"] = maxDistance,
                ["orderedSourceCorners"] = new JArray(indices), ["corners"] = corners };
        }

        private static Dictionary<string, float>[] ReadNamedWeights(SkinnedMeshRenderer source)
        {
            Mesh m = source.sharedMesh; Transform[] bones = source.bones;
            Need(bones.Length == m.bindposes.Length, "Incomplete source skin palette.");
            var result = new Dictionary<string, float>[m.vertexCount];
            using (var counts = m.GetBonesPerVertex()) using (var weights = m.GetAllBoneWeights())
            {
                Need(counts.Length == m.vertexCount, "Incomplete bone-count stream."); int offset = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    Need(counts[i] > 0 && counts[i] <= 4, "Expected complete at-most-four source influences.");
                    var row = new Dictionary<string, float>(StringComparer.Ordinal); float sum = 0;
                    for (int j = 0; j < counts[i]; j++)
                    {
                        Need(offset < weights.Length, "Truncated weight stream."); var w = weights[offset++];
                        Need(Finite(w.weight) && w.weight >= 0 && w.boneIndex >= 0 && w.boneIndex < bones.Length && bones[w.boneIndex] != null, "Invalid source skin influence.");
                        if (w.weight == 0) continue;
                        Need(!row.ContainsKey(bones[w.boneIndex].name), "Duplicate source bone name/influence."); row.Add(bones[w.boneIndex].name, w.weight); sum += w.weight;
                    }
                    Need(Mathf.Abs(sum - 1) <= .0001f, "Unnormalized source weights."); result[i] = row;
                }
                Need(offset == weights.Length, "Trailing unmatched weight stream.");
            }
            return result;
        }
        private static Dictionary<string, float> ReadExpectedWeights(JToken token)
        {
            Need(token is JObject, "Manifest corner weights must be named values."); var result = new Dictionary<string, float>(StringComparer.Ordinal); float sum = 0;
            foreach (var p in ((JObject)token).Properties())
            { float x = (float)p.Value; Need(Finite(x) && x > 0, "Invalid original named weight."); result.Add(p.Name, x); sum += x; }
            Need(result.Count > 0 && result.Count <= 4 && Mathf.Abs(sum - 1) <= .0001f, "Invalid original full corner weight sum/count."); return result;
        }
        private static bool SameWeights(Dictionary<string, float> actual, Dictionary<string, float> expected, float importerCutoff)
        {
            // Unity's imported palette can prune a sub-micro influence, then normalize
            // the remaining original bones. This is proven by the actual binding report:
            // original Spine02=2.93475466e-7 was omitted; geometry remained within0.477um.
            // Keep the existing total weight tolerance and require every retained identity;
            // a meaningful omitted influence or an unexpected bone still fails.
            if (actual.Keys.Any(name => !expected.ContainsKey(name))) return false;
            float dropped = 0, originalSum = 0;
            foreach (var pair in expected)
            {
                originalSum += pair.Value;
                if (actual.ContainsKey(pair.Key)) continue;
                if (!(importerCutoff > 0) || pair.Value > importerCutoff) return false;
                dropped += pair.Value;
            }
            if (dropped > WeightIdentityTolerance || originalSum - dropped <= 0) return false;
            float normalization = dropped > 0 ? 1f / (originalSum - dropped) : 1f;
            foreach (var pair in actual)
                if (Mathf.Abs(pair.Value - expected[pair.Key] * normalization) > WeightIdentityTolerance) return false;
            return true;
        }
        private static JObject WeightComparison(Dictionary<string, float> actual, Dictionary<string, float> expected)
        {
            var omitted = new JObject(); float omittedMass = 0, sum = 0;
            foreach (var pair in expected)
            {
                sum += pair.Value;
                if (actual.ContainsKey(pair.Key)) continue;
                omitted[pair.Key] = pair.Value; omittedMass += pair.Value;
            }
            float normalization = omittedMass > 0 ? 1f / (sum - omittedMass) : 1f;
            return new JObject { ["omittedOriginalInfluences"] = omitted, ["omittedTotalWeight"] = omittedMass,
                ["retainedNormalizationFactor"] = normalization,
                ["maximumRetainedWeightError"] = actual.Max(pair => Mathf.Abs(pair.Value - expected[pair.Key] * normalization)) };
        }
        private static Transform UniqueBone(Transform world, string name)
        {
            var all = world.GetComponentsInChildren<Transform>(true).Where(x => x.name == name).ToArray();
            Need(all.Length == 1, "Missing/ambiguous named bone " + name + "."); return all[0];
        }
        private static string HashRestGeometry(Vector3[] points, int[] triangles, Dictionary<string, float>[] weights)
        {
            using (var memory = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(points.Length);
                    for (int i = 0; i < points.Length; i++)
                    {
                        writer.Write(points[i].x); writer.Write(points[i].y); writer.Write(points[i].z); writer.Write(weights[i].Count);
                        foreach (var p in weights[i].OrderBy(x => x.Key, StringComparer.Ordinal)) { writer.Write(p.Key); writer.Write(p.Value); }
                    }
                    writer.Write(triangles.Length); foreach (int t in triangles) writer.Write(t);
                }
                using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(memory.ToArray()));
            }
        }
        private static void VerifyFile(string path, string expected) { Need(File.Exists(path), "Missing frozen provenance file: " + path); Need(string.Equals(HashFile(path), expected, StringComparison.OrdinalIgnoreCase), "Frozen provenance hash mismatch: " + path); }
        private static string HashFile(string path) { using (var h = SHA256.Create()) using (var f = File.OpenRead(path)) return Hex(h.ComputeHash(f)); }
        private static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
        private static string RequiredString(JObject o, string key) { string x = (string)o[key]; Need(!string.IsNullOrEmpty(x), "Missing manifest " + key); return x; }
        private static Vector3 ReadVector(JToken token) { Need(token is JArray && token.Count() == 3, "Expected a three-component vector."); var p = new Vector3((float)token[0], (float)token[1], (float)token[2]); Need(Finite(p), "Nonfinite manifest vector."); return p; }
        private static Vector3 BlenderToUnity(Vector3 p) => new Vector3(-p.x, p.z, -p.y);
        private static JArray Vec(Vector3 p) => new JArray(p.x, p.y, p.z);
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
