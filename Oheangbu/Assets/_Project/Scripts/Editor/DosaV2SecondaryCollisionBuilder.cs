using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    /// <summary>Author on a disposable/rest V2 model or validation prefab before production animation.
    /// The caller owns prefab saving. This helper never modifies models, skeletons, profiles or C2.</summary>
    public static class DosaV2SecondaryCollisionBuilder
    {
        public static string Bind(GameObject world)
        {
            var report = new JObject { ["status"] = "WAIT", ["rigGate"] = "NOT_GRANTED",
                ["scope"] = "Complete source triangles of rigid ornaments and chains from every present LOD, including mixed-weight boundary corners. Rigid geometry is authored from actual rest BakeMesh in pivot coordinates; chains retain exact original full skin streams. Runtime does not shrink supports, BakeMesh, touch physics layers, change source models or promote C2. No collision quality pass before actual direct-pose/impulse replay.",
                ["parts"] = new JArray() };
            PlayerSecondaryCollisionRig collision = null;
            try
            {
                Need(world != null, "World representation is required.");
                var rig = world.GetComponent<PlayerSecondaryMotionRig>(); var body = world.GetComponent<PlayerClothBodyProxyRig>();
                Need(rig != null && body != null, "World secondary rig and actual posed body fitter must already be authored.");
                Need(world.GetComponentsInChildren<Animator>(true).All(a => a.runtimeAnimatorController == null), "Fresh static V2 source required: no production controller.");
                var ornaments = Read<RigidOrnamentBinding[]>(rig, "_ornaments"); var chains = Read<SecondaryBoneChainBinding[]>(rig, "_chains");
                Need(DosaV2SecondaryBuilder.IsCompleteRigidLayout(world, ornaments, out string layoutError), layoutError);
                Need(chains.Length == 5, "All five world chains must be present; no missing part may be silently skipped.");
                var baseCapsules = world.GetComponentsInChildren<CapsuleCollider>(true).Where(c => c.name.StartsWith("DosaV2_ClothProxy_", StringComparison.Ordinal)
                    && (c.name.Contains("BodyLining_") || c.name.Contains("ArmLining_") || c.name.Contains("LegLining_"))).OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
                Need(baseCapsules.Length == 27 && baseCapsules.All(c => PlayerSecondaryMotionRig.ValidateProxy(c, out _)), "All 27 source-fitted body capsules required; not the 16-capsule Cloth-selected subset.");
                var headCapsules = world.GetComponentsInChildren<CapsuleCollider>(true)
                    .Where(c => c.name.StartsWith(DosaV2HeadNeckCollisionBuilder.Prefix, StringComparison.Ordinal)).ToArray();
                Need(headCapsules.Length == 6 && new HashSet<string>(headCapsules.Select(c => c.name), StringComparer.Ordinal)
                    .SetEquals(DosaV2HeadNeckCollisionBuilder.ReviewedNames)
                    && headCapsules.All(c => PlayerSecondaryMotionRig.ValidateProxy(c, out _)), "All six explicitly reviewed Head-parented proxies required.");
                var shoulderCapsules = world.GetComponentsInChildren<CapsuleCollider>(true)
                    .Where(c => c.name.StartsWith(DosaV2ShoulderCollisionBuilder.Prefix, StringComparison.Ordinal)).ToArray();
                Need(shoulderCapsules.Length == 2 && new HashSet<string>(shoulderCapsules.Select(c => c.name), StringComparer.Ordinal)
                    .SetEquals(DosaV2ShoulderCollisionBuilder.ReviewedNames)
                    && shoulderCapsules.All(c => PlayerSecondaryMotionRig.ValidateProxy(c, out _)), "Both complete-surface shoulder proxies required.");
                var capsules = baseCapsules.Concat(headCapsules).Concat(shoulderCapsules).OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
                report["baseBodyCapsules"] = new JArray(baseCapsules.Select(c => c.name));
                report["additionalHeadNeckCapsules"] = new JArray(headCapsules.Select(c => c.name));
                report["additionalShoulderCapsules"] = new JArray(shoulderCapsules.Select(c => c.name));
                var renderers = world.GetComponentsInChildren<Renderer>(true); var parts = new List<SecondaryCollisionPart>();
                foreach (var ornament in ornaments) parts.Add(AuthorPart(ornament.Name, true, new[] { ornament.Pivot }, renderers, (JArray)report["parts"]));
                foreach (var chain in chains) parts.Add(AuthorPart(chain.Name, false, chain.Bones, renderers, (JArray)report["parts"]));
                collision = world.GetComponent<PlayerSecondaryCollisionRig>();
                if (collision == null) collision = world.AddComponent<PlayerSecondaryCollisionRig>();
                Need(collision.Configure(world.transform, body, capsules, parts.ToArray()), collision.LastError);
                Need(rig.ConfigureCollision(collision), rig.LastBindingError);
                EditorUtility.SetDirty(collision); EditorUtility.SetDirty(rig);
                report["status"] = "BOUND_AWAITING_ACTUAL_COLLISION_REPLAY";
                report["bodyCapsules"] = new JArray(capsules.Select(c => c.name));
                report["sourceTriangles"] = parts.Sum(p => p.Surfaces.Sum(s => s.Triangles.Length / 3));
                report["sourceVertices"] = parts.Sum(p => p.Surfaces.Sum(s => s.Positions.Length));
                report["rigidParts"] = ornaments.Length; report["chains"] = chains.Length;
                report["runtimeBakeMesh"] = false; report["sourceTriangleOmissions"] = 0;
            }
            catch (Exception e) { report["error"] = e.GetBaseException().Message; report["status"] = "WAIT"; }
            return report.ToString();
        }

        private static SecondaryCollisionPart AuthorPart(string name, bool rigid, Transform[] driven, Renderer[] renderers, JArray output)
        {
            Need(driven != null && driven.Length > 0 && driven.All(b => b != null), name + ": missing driven bone.");
            var owned = new HashSet<Transform>(driven); var surfaces = new List<SecondaryCollisionSurface>(); var rows = new JArray();
            foreach (var renderer in renderers)
            {
                var skin = renderer as SkinnedMeshRenderer; Mesh mesh = skin != null ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || renderer.GetComponent<Cloth>() != null) continue;
                Transform[] bones = skin != null ? skin.bones : Array.Empty<Transform>();
                // Palette presence alone does not mean this object uses that bone.
                if (skin == null && (!rigid || renderer.transform.parent != driven[0])) continue;
                if (skin != null && !bones.Any(b => b != null && owned.Contains(b))) continue;
                Need(mesh.isReadable, name + ": readable exact source geometry is required: " + renderer.name);
                Vector3[] positions = mesh.vertices; int[] sourceTriangles = mesh.triangles;
                var selected = new HashSet<int>(); byte[] counts = Array.Empty<byte>(); BoneWeight1[] weights = Array.Empty<BoneWeight1>();
                int[] starts = Array.Empty<int>();
                if (skin == null) selected.UnionWith(Enumerable.Range(0, positions.Length));
                else
                {
                    using (var stream = mesh.GetBonesPerVertex()) counts = stream.ToArray();
                    using (var stream = mesh.GetAllBoneWeights()) weights = stream.ToArray();
                    Need(counts.Length == positions.Length, "Complete source vertex weights required."); starts = new int[positions.Length]; int offset = 0;
                    for (int v = 0; v < positions.Length; v++)
                    {
                        starts[v] = offset; float ownerWeight = 0, otherWeight = 0, sum = 0;
                        Need(counts[v] <= 4 && offset + counts[v] <= weights.Length, "Source exceeds original four-weight skin contract.");
                        for (int w = 0; w < counts[v]; w++)
                        {
                            var influence = weights[offset++];
                            Need(float.IsFinite(influence.weight) && influence.weight >= 0 && influence.boneIndex >= 0 && influence.boneIndex < bones.Length && bones[influence.boneIndex] != null, "Malformed original skin stream.");
                            sum += influence.weight;
                            if (owned.Contains(bones[influence.boneIndex])) ownerWeight += influence.weight; else otherWeight += influence.weight;
                        }
                        if (ownerWeight <= 0) continue;
                        Need(Mathf.Abs(sum - 1f) <= .0001f, "Source weights do not sum to one.");
                        if (rigid) Need(otherWeight == 0f && Mathf.Abs(ownerWeight - 1f) <= .0001f, name + ": rigid part has deforming mixed-body weights.");
                        selected.Add(v);
                    }
                    Need(offset == weights.Length, "Trailing source weight data.");
                }
                if (selected.Count == 0) continue;
                Need(mesh.blendShapeCount == 0, name + ": shape-changing ornament/chain requires separately authored support; do not ignore blend shapes.");
                var retainedTriangles = new List<int>(); var retainedVertices = new HashSet<int>(); int mixedTriangles = 0;
                for (int t = 0; t < sourceTriangles.Length; t += 3)
                {
                    int included = 0; for (int corner = 0; corner < 3; corner++) if (selected.Contains(sourceTriangles[t + corner])) included++;
                    if (included == 0) continue;
                    if (included != 3) mixedTriangles++;
                    Need(!rigid || included == 3, name + ": triangle crosses a rigid/non-rigid boundary; cannot certify rigid support by dropping it.");
                    retainedTriangles.Add(t / 3); for (int corner = 0; corner < 3; corner++) retainedVertices.Add(sourceTriangles[t + corner]);
                }
                Need(retainedTriangles.Count > 0, name + ": no retained source triangles.");
                var originalVertices = retainedVertices.OrderBy(v => v).ToArray(); var remap = new Dictionary<int, int>();
                for (int v = 0; v < originalVertices.Length; v++) remap.Add(originalVertices[v], v);
                var surface = new SecondaryCollisionSurface { Rigid = rigid, SourceRenderer = renderer, SourceMesh = mesh,
                    OriginalVertexIndices = originalVertices, OriginalTriangleIndices = retainedTriangles.ToArray(),
                    Positions = new Vector3[originalVertices.Length], Triangles = new int[retainedTriangles.Count * 3] };
                for (int t = 0; t < retainedTriangles.Count; t++) for (int corner = 0; corner < 3; corner++)
                    surface.Triangles[t * 3 + corner] = remap[sourceTriangles[retainedTriangles[t] * 3 + corner]];
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId); surface.SourceGuid = guid; surface.SourceLocalId = localId;
                string assetPath = AssetDatabase.GetAssetPath(mesh), fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                Need(File.Exists(fullPath), name + ": source artifact is missing."); surface.SourceFileSha256 = FileHash(fullPath);
                surface.SourceGeometrySha256 = PlayerSecondaryCollisionGeometry.SourceMeshHash(mesh);
                float maximumRigidSkinDifference = 0;
                if (rigid)
                {
                    Mesh bake = null;
                    try
                    {
                        Vector3[] actual = positions;
                        if (skin != null) { bake = new Mesh(); skin.BakeMesh(bake, false); actual = bake.vertices; Need(actual.Length == positions.Length, "Actual rest BakeMesh changed source vertex indexing."); }
                        Matrix4x4 toPivot = driven[0].worldToLocalMatrix * renderer.localToWorldMatrix;
                        for (int v = 0; v < originalVertices.Length; v++)
                        {
                            int original = originalVertices[v]; surface.Positions[v] = toPivot.MultiplyPoint3x4(actual[original]);
                            if (skin != null)
                            {
                                Vector3 independent = Vector3.zero;
                                for (int w = starts[original]; w < starts[original] + counts[original]; w++)
                                {
                                    var influence = weights[w];
                                    independent += (driven[0].worldToLocalMatrix * bones[influence.boneIndex].localToWorldMatrix * mesh.bindposes[influence.boneIndex]).MultiplyPoint3x4(positions[original]) * influence.weight;
                                }
                                maximumRigidSkinDifference = Mathf.Max(maximumRigidSkinDifference, Vector3.Distance(independent, surface.Positions[v]));
                            }
                        }
                    }
                    finally { if (bake != null) UnityEngine.Object.DestroyImmediate(bake); }
                    Need(maximumRigidSkinDifference <= .0001f, name + ": actual rest BakeMesh differs from independent full-weight rigid binding.");
                }
                else
                {
                    surface.Bones = bones; surface.Bindposes = mesh.bindposes;
                    surface.WeightStarts = new int[originalVertices.Length]; surface.WeightCounts = new byte[originalVertices.Length];
                    var serialized = new List<SecondaryCollisionWeight>();
                    for (int v = 0; v < originalVertices.Length; v++)
                    {
                        int original = originalVertices[v]; surface.Positions[v] = positions[original];
                        surface.WeightStarts[v] = serialized.Count; surface.WeightCounts[v] = counts[original];
                        for (int i = starts[original]; i < starts[original] + counts[original]; i++)
                            serialized.Add(new SecondaryCollisionWeight { Bone = weights[i].boneIndex, Weight = weights[i].weight });
                    }
                    surface.Weights = serialized.ToArray();
                }
                surface.GeometrySha256 = GeometryHash(surface); surfaces.Add(surface);
                rows.Add(new JObject { ["renderer"] = renderer.name, ["sourceAsset"] = assetPath, ["sourceGuid"] = guid, ["sourceLocalId"] = localId,
                    ["sourceFileSha256"] = surface.SourceFileSha256, ["geometrySha256"] = surface.GeometrySha256,
                    ["sourceGeometrySha256"] = surface.SourceGeometrySha256,
                    ["originalVertices"] = mesh.vertexCount, ["retainedVertices"] = surface.Positions.Length,
                    ["retainedTriangles"] = retainedTriangles.Count, ["mixedBoundaryTrianglesRetained"] = mixedTriangles,
                    ["maximumIndependentRigidSkinDifferenceMeters"] = maximumRigidSkinDifference,
                    ["coverage"] = "All incident original filled triangles, exact source positions/full weights, no radial support approximation or omitted boundary corners." });
            }
            Need(surfaces.Count > 0, name + ": no actual render surface found; body-free decoration cannot be assumed.");
            var part = new SecondaryCollisionPart { Name = name, Rigid = rigid, DrivenBones = driven, Surfaces = surfaces.ToArray() };
            output.Add(new JObject { ["name"] = name, ["rigid"] = rigid, ["drivenBones"] = new JArray(driven.Select(b => b.name)), ["surfaces"] = rows });
            return part;
        }
        private static string GeometryHash(SecondaryCollisionSurface surface)
        {
            using (var bytes = new MemoryStream())
            using (var write = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                write.Write(surface.Rigid); write.Write(surface.Positions.Length);
                foreach (var p in surface.Positions) { write.Write(p.x); write.Write(p.y); write.Write(p.z); }
                foreach (int i in surface.Triangles) write.Write(i);
                foreach (int i in surface.OriginalVertexIndices) write.Write(i);
                foreach (int i in surface.OriginalTriangleIndices) write.Write(i);
                foreach (var bone in surface.Bones) write.Write(bone != null ? bone.name : "<missing>");
                foreach (var matrix in surface.Bindposes) for (int i = 0; i < 16; i++) write.Write(matrix[i]);
                foreach (int start in surface.WeightStarts) write.Write(start);
                foreach (byte count in surface.WeightCounts) write.Write(count);
                foreach (var w in surface.Weights) { write.Write(w.Bone); write.Write(w.Weight); }
                write.Flush(); bytes.Position = 0; using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes));
            }
        }
        private static T Read<T>(object owner, string name)
        { var field = owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance); Need(field != null, "Serialized secondary binding field unavailable: " + name); return (T)field.GetValue(owner); }
        private static string FileHash(string path) { using (var file = File.OpenRead(path)) using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(file)); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
