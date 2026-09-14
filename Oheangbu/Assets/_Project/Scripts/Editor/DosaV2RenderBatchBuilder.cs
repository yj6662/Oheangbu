using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class DosaV2RenderBatchBuilder
    {
        public const string CandidatePath = "Assets/_Project/Prefabs/Player/PF_DosaV2_BatchedCandidate.prefab";
        const string Folder = "Assets/_Project/Art/Characters/DosaV2/OptimizedRendering";
        [Serializable] public sealed class Report
        {
            public string status, error, backup;
            public int originalRenderers, activeRenderers, combinedSources, combinedRenderers, originalDraws, activeDraws;
            public int originalTriangles, activeTriangles, sourceVertices, combinedVertices, checkedPoseVertices;
            public int clothBefore, clothAfter, animatorsBefore, animatorsAfter;
            public float maximumPoseErrorMeters;
            public List<string> protectedRenderers = new List<string>(), batches = new List<string>();
            public string scope = "Render-only batching of opaque non-Cloth/no-blendshape world parts. Existing source FBX, all triangles, UVs, bones, hands, Cloth, near arms, brush and source-index collision bindings preserved. Vertex equality in three diagnostic poses is not RIG_PASS.";
        }
        sealed class Source
        {
            public Renderer renderer;
            public Mesh mesh;
            public Matrix4x4 toRoot;
            public Vector3[] positions, normals;
            public Vector4[] tangents;
            public Color[] colors;
            public BoneWeight[] weights;
            public Transform[] bones;
            public Matrix4x4[] bind;
            public readonly List<Vector4>[] uv = new List<Vector4>[8];
        }
        sealed class Piece { public Source source; public int subMesh; }
        sealed class Mapping { public Source source; public int sourceVertex; }
        sealed class Built { public SkinnedMeshRenderer renderer; public readonly List<Mapping> map = new List<Mapping>(); }
        static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        static int Triangles(Renderer r) => MeshOf(r) == null ? 0 : (int)Enumerable.Range(0, MeshOf(r).subMeshCount).Sum(i => (long)MeshOf(r).GetIndexCount(i) / 3);
        static int Draws(Renderer r) => MeshOf(r) == null ? 0 : Enumerable.Range(0, MeshOf(r).subMeshCount).Count(i => MeshOf(r).GetIndexCount(i) > 0);

        public static string BuildCandidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var report = new Report { status = "BUILDING" };
            var root = PrefabUtility.LoadPrefabContents(DosaV2PlayableDraftBuilder.VisualPrefabPath);
            try
            {
                if (root.GetComponentInChildren<PlayerModelRenderBatch>(true) != null) throw new InvalidOperationException("Current prefab is already batched; reuse existing derivative.");
                var world = root.transform.Find("WorldBody");
                if (world == null) throw new InvalidOperationException("WorldBody missing.");
                var originals = world.GetComponentsInChildren<Renderer>(true);
                report.originalRenderers = originals.Length;
                report.originalTriangles = originals.Sum(Triangles); report.originalDraws = originals.Sum(Draws);
                report.clothBefore = root.GetComponentsInChildren<Cloth>(true).Length;
                report.animatorsBefore = root.GetComponentsInChildren<Animator>(true).Length;
                var groups = new Dictionary<Material, List<Piece>>();
                var sources = new List<Source>();
                foreach (var r in originals)
                {
                    Mesh mesh = MeshOf(r);
                    bool eligible = mesh != null && mesh.isReadable && mesh.blendShapeCount == 0 && r.GetComponent<Cloth>() == null
                        && r.GetComponentInParent<BrushBristleRig>() == null && (r is MeshRenderer || r is SkinnedMeshRenderer)
                        && r.sharedMaterials.Length == mesh.subMeshCount && r.sharedMaterials.All(m => m != null && m.renderQueue <= 2500);
                    if (!eligible) { report.protectedRenderers.Add(r.name); continue; }
                    var skin = r as SkinnedMeshRenderer;
                    var source = new Source { renderer = r, mesh = mesh, toRoot = world.worldToLocalMatrix * r.transform.localToWorldMatrix,
                        positions = mesh.vertices, normals = mesh.normals, tangents = mesh.tangents, colors = mesh.colors,
                        weights = skin != null ? mesh.boneWeights : null, bones = skin != null ? skin.bones : new[] { r.transform },
                        bind = skin != null ? mesh.bindposes : new[] { Matrix4x4.identity } };
                    if (skin != null)
                    {
                        using (var counts = mesh.GetBonesPerVertex()) if (counts.Any(c => c > 4)) throw new InvalidOperationException(r.name + ": more than four weights.");
                        if (source.weights.Length != mesh.vertexCount || source.bones.Length != source.bind.Length) throw new InvalidOperationException(r.name + ": invalid skin binding.");
                    }
                    for (int channel = 0; channel < 8; channel++) { source.uv[channel] = new List<Vector4>(); mesh.GetUVs(channel, source.uv[channel]); }
                    sources.Add(source);
                    for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetIndexCount(i) > 0)
                    {
                        var material = r.sharedMaterials[i];
                        if (!groups.TryGetValue(material, out var pieces)) groups.Add(material, pieces = new List<Piece>());
                        pieces.Add(new Piece { source = source, subMesh = i });
                    }
                }
                DevSceneKit.EnsureFolder(Folder);
                var built = new List<Built>();
                foreach (var group in groups.OrderBy(g => g.Key.name)) built.Add(BuildGroup(world, group.Key, group.Value, report));
                report.combinedSources = sources.Count; report.combinedRenderers = built.Count;
                report.sourceVertices = sources.Sum(s => s.mesh.vertexCount);
                report.combinedVertices = built.Sum(b => b.renderer.sharedMesh.vertexCount);
                ValidatePoses(world, sources, built, report);
                var batch = world.gameObject.AddComponent<PlayerModelRenderBatch>();
                batch.Configure(originals, sources.Select(s => s.renderer).ToArray(), built.Select(b => (Renderer)b.renderer).ToArray());
                report.activeRenderers = batch.ActiveRenderers.Length;
                report.activeTriangles = batch.ActiveRenderers.Sum(Triangles); report.activeDraws = batch.ActiveRenderers.Sum(Draws);
                report.clothAfter = root.GetComponentsInChildren<Cloth>(true).Length;
                report.animatorsAfter = root.GetComponentsInChildren<Animator>(true).Length;
                if (report.originalTriangles != report.activeTriangles || report.clothBefore != report.clothAfter || report.animatorsBefore != report.animatorsAfter)
                    throw new InvalidOperationException("Geometry/Cloth/Animator count changed.");
                if (PrefabUtility.SaveAsPrefabAsset(root, CandidatePath) == null) throw new IOException("Candidate prefab save failed.");
                AssetDatabase.SaveAssets(); report.status = "CANDIDATE_GEOMETRY_PASS";
            }
            catch (Exception e) { report.status = "FAILED"; report.error = e.ToString(); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory("Screenshots/PlayerDosaV2"); File.WriteAllText("Screenshots/PlayerDosaV2/render-batching-build.json", json);
            return json;
        }

        public static Renderer[] CombineReducedSources(Transform root, Renderer[] renderers, string folder, string prefix, Report report)
        {
            report.scope = "Batching validation compares reduced templates against their combined render meshes in three diagnostic poses. It does not compare against the unreduced model or certify RIG_PASS. Original source-index physics meshes and world Cloth remain separate.";
            var sources = new List<Source>(); var groups = new Dictionary<Material, List<Piece>>();
            var preserved = renderers.Where(r => MeshOf(r).blendShapeCount > 0).ToArray();
            foreach (var r in renderers.Except(preserved))
            {
                var skin = r as SkinnedMeshRenderer; var mesh = MeshOf(r);
                if (skin == null || r.GetComponent<Cloth>() != null || mesh == null || !mesh.isReadable) throw new InvalidOperationException("Reduced templates must be readable skinned meshes without Cloth.");
                var s = new Source { renderer = r, mesh = mesh, toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix,
                    positions = mesh.vertices, normals = mesh.normals, tangents = mesh.tangents, colors = mesh.colors,
                    weights = mesh.boneWeights, bones = skin.bones, bind = mesh.bindposes };
                for (int channel = 0; channel < 8; channel++) { s.uv[channel] = new List<Vector4>(); mesh.GetUVs(channel, s.uv[channel]); }
                sources.Add(s);
                for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetIndexCount(i) > 0)
                {
                    var material = r.sharedMaterials[i];
                    if (material == null || material.renderQueue > 2500) throw new InvalidOperationException("Only matching opaque materials can be batched.");
                    if (!groups.TryGetValue(material, out var pieces)) groups.Add(material, pieces = new List<Piece>());
                    pieces.Add(new Piece { source = s, subMesh = i });
                }
            }
            DevSceneKit.EnsureFolder(folder);
            var built = groups.OrderBy(g => g.Key.name).Select(g => BuildGroup(root, g.Key, g.Value, report, folder, prefix)).ToList();
            ValidatePoses(root, sources, built, report);
            foreach (var s in sources) Object.DestroyImmediate(s.renderer.gameObject);
            return built.Select(b => (Renderer)b.renderer).Concat(preserved).ToArray();
        }

        static Built BuildGroup(Transform root, Material material, List<Piece> pieces, Report report, string folder = Folder, string prefix = "SM_DosaV2_Batch_")
        {
            var result = new Built();
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var colors = new List<Color>();
            var uv = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
            var weights = new List<BoneWeight>(); var bones = new List<Transform>(); var bind = new List<Matrix4x4>(); var indices = new List<int>();
            var maps = new Dictionary<Source, Dictionary<int, int>>();
            int Bone(Transform bone, Matrix4x4 pose)
            {
                for (int i = 0; i < bones.Count; i++) if (bones[i] == bone && bind[i].Equals(pose)) return i;
                if (bone == null) throw new InvalidOperationException("Missing source bone.");
                bones.Add(bone); bind.Add(pose); return bones.Count - 1;
            }
            foreach (var piece in pieces)
            {
                var s = piece.source;
                if (!maps.TryGetValue(s, out var map)) maps.Add(s, map = new Dictionary<int, int>());
                Matrix4x4 inverse = s.toRoot.inverse, normalMatrix = inverse.transpose;
                foreach (int sourceIndex in s.mesh.GetTriangles(piece.subMesh))
                {
                    if (!map.TryGetValue(sourceIndex, out int target))
                    {
                        target = vertices.Count; map.Add(sourceIndex, target);
                        vertices.Add(s.toRoot.MultiplyPoint3x4(s.positions[sourceIndex]));
                        if (s.normals.Length != s.positions.Length) throw new InvalidOperationException(s.renderer.name + ": normals missing.");
                        normals.Add(normalMatrix.MultiplyVector(s.normals[sourceIndex]).normalized);
                        var t = s.tangents.Length == s.positions.Length ? s.tangents[sourceIndex] : new Vector4(1, 0, 0, 1);
                        Vector3 tangent = s.toRoot.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                        tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, t.w * (s.toRoot.determinant < 0 ? -1 : 1)));
                        colors.Add(s.colors.Length == s.positions.Length ? s.colors[sourceIndex] : Color.white);
                        for (int channel = 0; channel < 8; channel++) uv[channel].Add(s.uv[channel].Count == s.positions.Length ? s.uv[channel][sourceIndex] : Vector4.zero);
                        BoneWeight w;
                        if (s.weights == null) w = new BoneWeight { boneIndex0 = Bone(s.bones[0], inverse), weight0 = 1 };
                        else
                        {
                            w = s.weights[sourceIndex];
                            w.boneIndex0 = w.weight0 > 0 ? Bone(s.bones[w.boneIndex0], s.bind[w.boneIndex0] * inverse) : 0;
                            w.boneIndex1 = w.weight1 > 0 ? Bone(s.bones[w.boneIndex1], s.bind[w.boneIndex1] * inverse) : 0;
                            w.boneIndex2 = w.weight2 > 0 ? Bone(s.bones[w.boneIndex2], s.bind[w.boneIndex2] * inverse) : 0;
                            w.boneIndex3 = w.weight3 > 0 ? Bone(s.bones[w.boneIndex3], s.bind[w.boneIndex3] * inverse) : 0;
                        }
                        weights.Add(w); result.map.Add(new Mapping { source = s, sourceVertex = sourceIndex });
                    }
                    indices.Add(target);
                }
            }
            var mesh = new Mesh { name = prefix + material.name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTangents(tangents); mesh.SetColors(colors);
            for (int channel = 0; channel < 8; channel++)
            {
                int dimension = pieces.Max(p => p.source.mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel) ? p.source.mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel) : 0);
                if (dimension == 2) mesh.SetUVs(channel, uv[channel].Select(v => new Vector2(v.x, v.y)).ToList());
                else if (dimension == 3) mesh.SetUVs(channel, uv[channel].Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else if (dimension == 4) mesh.SetUVs(channel, uv[channel]);
            }
            mesh.boneWeights = weights.ToArray(); mesh.bindposes = bind.ToArray(); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            string path = folder + "/" + mesh.name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); mesh = old; } else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(prefix + material.name); go.layer = root.gameObject.layer; go.transform.SetParent(root, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.sharedMaterial = material;
            renderer.bones = bones.ToArray(); renderer.rootBone = root; renderer.quality = SkinQuality.Bone4;
            renderer.updateWhenOffscreen = true; renderer.forceMatrixRecalculationPerRender = true;
            var bounds = mesh.bounds; bounds.Expand(1f); renderer.localBounds = bounds;
            var reference = pieces[0].source.renderer;
            renderer.shadowCastingMode = reference.shadowCastingMode; renderer.receiveShadows = reference.receiveShadows;
            renderer.lightProbeUsage = reference.lightProbeUsage; renderer.reflectionProbeUsage = reference.reflectionProbeUsage;
            renderer.renderingLayerMask = reference.renderingLayerMask; renderer.motionVectorGenerationMode = reference.motionVectorGenerationMode;
            result.renderer = renderer;
            report.batches.Add(material.name + ": " + pieces.Select(p => p.source).Distinct().Count() + " source renderers, " + vertices.Count + " vertices, " + indices.Count / 3 + " triangles, " + bones.Count + " palette entries");
            return result;
        }

        static void ValidatePoses(Transform root, List<Source> sources, List<Built> built, Report report)
        {
            var nodes = sources.SelectMany(s => s.bones).Distinct().ToArray();
            var rotations = nodes.Select(t => t.localRotation).ToArray();
            var baked = new Mesh(); var combined = new Mesh();
            try
            {
                for (int pose = 0; pose < 3; pose++)
                {
                    for (int i = 0; i < nodes.Length; i++) nodes[i].localRotation = rotations[i] * (pose == 0 ? Quaternion.identity : Quaternion.AngleAxis((i % 3 - 1) * 13f * pose, pose == 1 ? Vector3.right : Vector3.forward));
                    var expected = new Dictionary<Source, Vector3[]>();
                    foreach (var s in sources)
                    {
                        Vector3[] points;
                        if (s.renderer is SkinnedMeshRenderer skin) { skin.BakeMesh(baked); points = baked.vertices; }
                        else points = s.positions;
                        Matrix4x4 matrix = root.worldToLocalMatrix * s.renderer.transform.localToWorldMatrix;
                        expected[s] = points.Select(matrix.MultiplyPoint3x4).ToArray();
                    }
                    foreach (var b in built)
                    {
                        b.renderer.BakeMesh(combined); var points = combined.vertices;
                        for (int i = 0; i < points.Length; i++)
                        {
                            var map = b.map[i];
                            float error = Vector3.Distance(points[i], expected[map.source][map.sourceVertex]);
                            if (float.IsNaN(error) || float.IsInfinity(error)) throw new InvalidOperationException("Nonfinite merged skin.");
                            report.maximumPoseErrorMeters = Mathf.Max(report.maximumPoseErrorMeters, error); report.checkedPoseVertices++;
                        }
                    }
                }
                if (report.maximumPoseErrorMeters > .00005f) throw new InvalidOperationException("Merged deformation differs: " + report.maximumPoseErrorMeters);
            }
            finally { for (int i = 0; i < nodes.Length; i++) if (nodes[i] != null) nodes[i].localRotation = rotations[i]; Object.DestroyImmediate(baked); Object.DestroyImmediate(combined); }
        }

        public static string ApplyCandidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var report = JsonUtility.FromJson<Report>(File.ReadAllText("Screenshots/PlayerDosaV2/render-batching-build.json"));
            if (report.status != "CANDIDATE_GEOMETRY_PASS") throw new InvalidOperationException("Candidate geometry gate not passed.");
            string backup = Path.GetFullPath("../Art/PlayerV2/Backups/RenderBatch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")); Directory.CreateDirectory(backup);
            File.Copy(DosaV2PlayableDraftBuilder.VisualPrefabPath, Path.Combine(backup, "PF_DosaV2_PlayableDraft.prefab"));
            var root = PrefabUtility.LoadPrefabContents(CandidatePath);
            try { if (PrefabUtility.SaveAsPrefabAsset(root, DosaV2PlayableDraftBuilder.VisualPrefabPath) == null) throw new IOException("Could not apply candidate."); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets(); report.backup = backup; report.status = "APPLIED_PENDING_PLAY";
            string json = JsonUtility.ToJson(report, true); File.WriteAllText("Screenshots/PlayerDosaV2/render-batching-apply.json", json); return json;
        }
    }
}
