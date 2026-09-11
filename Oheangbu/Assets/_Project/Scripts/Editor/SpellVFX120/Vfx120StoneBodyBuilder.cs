using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.SpellVFX120
{
    /// <summary>Copies two audited static stone LODs; never changes source assets, scenes or gameplay clocks.</summary>
    public static class Vfx120StoneBodyBuilder
    {
        public const string Output = "Assets/_Project/Art/SpellVFX120/StoneBodies";
        const string Pack = "Assets/SeyeonjeongPavilion/";
        const string Surface = "Oheangbu/VFX120/BotanicalSurface";
        static readonly Color Pigment = new Color(.75f, .72f, .66f, 1);

        [Serializable] sealed class FileEvidence { public string path, sha256Before, sha256After; }
        [Serializable] sealed class SourceEvidence
        {
            public string glyph, prefab, child, model, modelGuid, meshOutput, materialSource, materialOutput;
            public string baseMap, normalMap, originalAxes, normalization;
            public long meshLocalId;
            public int vertices, triangles, submeshes, sourceBoneWeights, sourceBindposes;
            public int sourceUv0Count, sourceNormalCount, sourceTangentCount, derivativeTangentCount;
            public int[] uvDimensions;
            public Bounds originalMeshBounds, prefabBounds, rotatedBounds, canonicalBounds, authoredBounds;
            public Matrix4x4 meshToRoot, normalizationMatrix;
            public Vector3 canonicalEuler, authoredScale;
            public bool sourceAoCopied, nativeVertexBufferPresent, derivativeTangentsRebuilt;
        }
        [Serializable] sealed class ProfileEvidence
        {
            public string glyph, path, sha256Before, sha256After, jsonBefore, jsonAfter;
            public string meshBefore, meshAfter, materialBefore, materialAfter;
        }
        [Serializable] sealed class Report
        {
            public string status = "BUILT_STRUCTURAL_CHECKS_ONLY_VISUAL_RUNTIME_UNVERIFIED";
            public string generatedUtc, geometryPolicy, runtimeLimit, materialLimit;
            public bool sourceFilesUnchanged;
            public SourceEvidence[] sources;
            public ProfileEvidence[] profiles;
            public FileEvidence[] sourceFiles;
        }
        sealed class Source
        {
            public Mesh mesh;
            public Material material;
            public Matrix4x4 transform;
            public SourceEvidence evidence;
        }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stone body build requires Edit Mode.");
            var shader = Shader.Find(Surface);
            if (shader == null || !shader.isSupported || ShaderUtil.GetShaderMessages(shader).Any(m => m.severity.ToString() == "Error"))
                throw new InvalidOperationException("Supported BotanicalSurface without shader errors is required.");
            var properties = Enumerable.Range(0, ShaderUtil.GetPropertyCount(shader)).Select(i => ShaderUtil.GetPropertyName(shader, i)).ToArray();
            foreach (var required in new[] { "_BaseMap", "_BumpMap", "_BumpScale", "_BaseColor", "_Tint", "_TintStrength", "_Saturation", "_AlphaClip", "_Cutoff", "_Visibility", "_Formation", "_GroundY" })
                if (!properties.Contains(required)) throw new InvalidOperationException("Missing shader property: " + required);
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            if (catalog == null) throw new InvalidOperationException("VFX120 catalog missing.");
            var profiles = new[] { "막", "뭄" }.Select(g => catalog.Entries.Single(e => e.Glyph == g && e.Profile != null && e.Profile.Glyph == g).Profile).ToArray();
            var profileRows = profiles.Select(p => new ProfileEvidence {
                glyph = p.Glyph, path = AssetDatabase.GetAssetPath(p), jsonBefore = JsonUtility.ToJson(p),
                sha256Before = Hash(AssetDatabase.GetAssetPath(p)), meshBefore = AssetDatabase.GetAssetPath(p.BodyMesh),
                materialBefore = AssetDatabase.GetAssetPath(p.BodyMaterial)
            }).ToArray();
            var hashes = new Dictionary<string, FileEvidence>(StringComparer.Ordinal);
            // Preflight BOTH source LODs, topology, maps, vertex channels and output types before dirtying any asset.
            var sources = new[] {
                ReadSource("막", "SM_Rock_K", 2, 1006, Quaternion.identity, new Vector3(1.45f, 1.85f, .85f), hashes),
                ReadSource("뭄", "SM_StoneBridge_1", 3, 3375, Quaternion.Euler(0, -90, 0), new Vector3(1.65f, .65f, 4.7f), hashes)
            };
            foreach (var s in sources) { Owned<Mesh>(s.evidence.meshOutput); Owned<Material>(s.evidence.materialOutput); }
            foreach (string folder in new[] { Output, Output + "/Meshes", Output + "/Materials" }) EnsureFolder(folder);
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                var mesh = UpsertMesh(source);
                var material = UpsertMaterial(source, shader);
                source.evidence.canonicalBounds = mesh.bounds;
                source.evidence.authoredBounds = BoundsOf(mesh.vertices.Select(v => Vector3.Scale(v, source.evidence.authoredScale)));
                source.evidence.nativeVertexBufferPresent = mesh.vertexBufferCount > 0;
                profiles[i].BodyMesh = mesh;
                profiles[i].BodyMaterial = material;
                profiles[i].Pigment = Pigment;
                profiles[i].Count = 1;
                profiles[i].Size = 1; // Canonical unit bounds: PartScale alone describes the full authored dimensions.
                profiles[i].PartScale = source.evidence.authoredScale;
                profiles[i].NativeScale = .32f;
                profiles[i].NativeImpactScale = .45f;
                EditorUtility.SetDirty(profiles[i]);
                AssetDatabase.SaveAssetIfDirty(profiles[i]);
                profileRows[i].jsonAfter = JsonUtility.ToJson(profiles[i]);
                profileRows[i].sha256After = Hash(profileRows[i].path);
                profileRows[i].meshAfter = AssetDatabase.GetAssetPath(mesh);
                profileRows[i].materialAfter = AssetDatabase.GetAssetPath(material);
            }
            foreach (var file in hashes.Values)
            {
                file.sha256After = Hash(file.path);
                if (file.sha256Before != file.sha256After) throw new InvalidOperationException("Source changed during build: " + file.path);
            }
            var report = new Report {
                generatedUtc = DateTime.UtcNow.ToString("o"), sourceFilesUnchanged = true,
                geometryPolicy = "Exactly one static, single-submesh LOD per profile. Original index order, UV channels, normals and colors retained. Source tangents and handedness are transformed when present; when source tangent count is zero, tangents are explicitly recalculated only on the owned derivative using its existing UVs, normals and indices, and recorded per source. Mesh native setters rebuild buffers. Imported prefab transforms are used once; no extra FBX axis or unit conversion. Each rotated bounds axis is centered and independently normalized to [-.5,+.5]. Bridge source +X maps to canonical +Z. Count=1 and Size=1; authored dimensions are PartScale. Source proportions therefore change deliberately, without changing vertex/triangle counts.",
                runtimeLimit = "Editor build only; no scene, collider, skeleton, motion, combat timing or field gate is created. Placement, ground contact, bridge formation/reveal, lifetime and visual quality remain UNVERIFIED. Ground anchor and _Formation are supplied by runtime. One whole bridge retains its deck, supports and original UV atlas; it is not copied eight times.",
                materialLimit = "Owned shared BotanicalSurface materials retain source base/normal maps and base UV transform. TintStrength=0, Saturation=.6, AlphaClip=0, profile Pigment=(.75,.72,.66). Unresolved vendor AO is deliberately omitted. No source material or importer mutation.",
                sources = sources.Select(s => s.evidence).ToArray(), profiles = profileRows,
                sourceFiles = hashes.Values.OrderBy(h => h.path, StringComparer.Ordinal).ToArray()
            };
            Directory.CreateDirectory(Vfx120Editor.Output);
            string reportPath = Path.Combine(Vfx120Editor.Output, "stone_body_build.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            return "BUILT: 막 1x1006 tris, 1.45x1.85x.85m; 뭄 1x3375 tris, 1.65x.65x4.7m. Visual/runtime UNVERIFIED. " + reportPath;
        }

        static Source ReadSource(string glyph, string name, int lod, int expectedTriangles, Quaternion rotation,
            Vector3 authoredScale, Dictionary<string, FileEvidence> hashes)
        {
            string prefabPath = Pack + "Prefabs/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException(prefabPath);
            if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException("Static source required: " + name);
            var filter = prefab.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name + "_LOD" + lod);
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (mesh == null || renderer == null || !mesh.isReadable || mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0 || mesh.boneWeights.Length != 0)
                throw new InvalidOperationException("Readable unskinned source required: " + name);
            if (mesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial == null)
                throw new InvalidOperationException("Exactly one submesh/material is required; no output written: " + name);
            if (mesh.GetTopology(0) != MeshTopology.Triangles || mesh.GetIndexCount(0) != expectedTriangles * 3)
                throw new InvalidOperationException("Source differs from audited triangle LOD: " + name);
            int uvCount = mesh.uv.Length, normalCount = mesh.normals.Length, tangentCount = mesh.tangents.Length;
            if (uvCount != mesh.vertexCount || normalCount != mesh.vertexCount || (tangentCount != 0 && tangentCount != mesh.vertexCount))
                throw new InvalidOperationException("Original channel mismatch: " + name + " vertices=" + mesh.vertexCount +
                    ", UV0=" + uvCount + ", normals=" + normalCount + ", tangents=" + tangentCount +
                    ". Complete UV0 and normals are required; tangents may be complete or absent (zero), never partial.");
            var material = renderer.sharedMaterial;
            var baseMap = SourceTexture(material, "_BaseMap");
            var normalMap = SourceTexture(material, "_BumpMap");
            // BotanicalSurface samples both maps through the base UV transform.
            if (material.GetTextureScale("_BaseMap") != material.GetTextureScale("_BumpMap") || material.GetTextureOffset("_BaseMap") != material.GetTextureOffset("_BumpMap"))
                throw new InvalidOperationException("Source normal/base UV transforms differ: " + name);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var rotated = Matrix4x4.Rotate(rotation) * toRoot;
            var rotatedBounds = BoundsOf(mesh.vertices.Select(rotated.MultiplyPoint3x4));
            Vector3 size = rotatedBounds.size;
            if (!Finite(size) || Mathf.Min(size.x, Mathf.Min(size.y, size.z)) <= 1e-5f || toRoot.determinant <= 0)
                throw new InvalidOperationException("Invalid/reflected source transform or dimensions: " + name);
            Matrix4x4 matrix = Matrix4x4.Scale(new Vector3(1 / size.x, 1 / size.y, 1 / size.z)) * Matrix4x4.Translate(-rotatedBounds.center) * rotated;
            if (mesh.vertices.Any(v => !Finite(matrix.MultiplyPoint3x4(v)))) throw new InvalidOperationException("Nonfinite source vertices.");
            var normalized = BoundsOf(mesh.vertices.Select(matrix.MultiplyPoint3x4));
            if (normalized.center.magnitude > 1e-4f || (normalized.size - Vector3.one).magnitude > 1e-4f)
                throw new InvalidOperationException("Source does not normalize to a centered unit box.");
            string modelPath = AssetDatabase.GetAssetPath(mesh);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId);
            foreach (string path in new[] { prefabPath, modelPath, AssetDatabase.GetAssetPath(material), AssetDatabase.GetAssetPath(baseMap), AssetDatabase.GetAssetPath(normalMap) }) Track(path, hashes);
            var dims = Enumerable.Range(0, 8).Select(i => mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + i)) ? mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + i)) : 0).ToArray();
            return new Source { mesh = mesh, material = material, transform = matrix,
                evidence = new SourceEvidence {
                    glyph = glyph, prefab = prefabPath, child = filter.name, model = modelPath, modelGuid = guid, meshLocalId = localId,
                    meshOutput = Output + "/Meshes/" + name + "_LOD" + lod + "_Unit.asset",
                    materialSource = AssetDatabase.GetAssetPath(material), materialOutput = Output + "/Materials/M_" + name + ".mat",
                    baseMap = AssetDatabase.GetAssetPath(baseMap), normalMap = AssetDatabase.GetAssetPath(normalMap),
                    vertices = mesh.vertexCount, triangles = expectedTriangles, submeshes = 1, sourceBoneWeights = 0, sourceBindposes = 0,
                    sourceUv0Count = uvCount, sourceNormalCount = normalCount, sourceTangentCount = tangentCount,
                    derivativeTangentsRebuilt = tangentCount == 0,
                    uvDimensions = dims, originalMeshBounds = mesh.bounds, prefabBounds = BoundsOf(mesh.vertices.Select(toRoot.MultiplyPoint3x4)),
                    rotatedBounds = rotatedBounds, canonicalBounds = normalized, meshToRoot = toRoot, normalizationMatrix = matrix,
                    canonicalEuler = rotation.eulerAngles, authoredScale = authoredScale, sourceAoCopied = false,
                    originalAxes = glyph == "뭄" ? "Imported bridge long axis X, height Y, cross-deck width Z; near-bottom source pivot. Rotation -90Y maps +X to +Z." : "Imported rock height Y, broad irregular faces in XZ; original pivot near centre, below-pivot stone retained.",
                    normalization = "S(1/rotatedBounds.size) * T(-rotatedBounds.center) * R(canonicalEuler) * meshToRoot; unit dimensions in X/Y/Z, centre zero. Nonuniform normalization uses inverse-transpose normals."
                }
            };
        }

        static Mesh UpsertMesh(Source source)
        {
            var mesh = Owned<Mesh>(source.evidence.meshOutput);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear(false); mesh.name = Path.GetFileNameWithoutExtension(source.evidence.meshOutput); mesh.indexFormat = source.mesh.indexFormat;
            mesh.vertices = source.mesh.vertices.Select(source.transform.MultiplyPoint3x4).ToArray();
            var normalMatrix = source.transform.inverse.transpose;
            var normals = source.mesh.normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
            mesh.normals = normals;
            var tangents = source.mesh.tangents;
            for (int i = 0; i < tangents.Length; i++)
            {
                var v = source.transform.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z));
                v = (v - normals[i] * Vector3.Dot(normals[i], v)).normalized;
                tangents[i] = new Vector4(v.x, v.y, v.z, tangents[i].w);
            }
            if (tangents.Length != 0) mesh.tangents = tangents;
            if (source.mesh.colors.Length == source.mesh.vertexCount) mesh.colors = source.mesh.colors;
            for (int i = 0; i < 8; i++)
            {
                int dimension = source.evidence.uvDimensions[i];
                if (dimension == 0) continue;
                if (dimension == 2) { var uv = new List<Vector2>(); source.mesh.GetUVs(i, uv); mesh.SetUVs(i, uv); }
                else if (dimension == 3) { var uv = new List<Vector3>(); source.mesh.GetUVs(i, uv); mesh.SetUVs(i, uv); }
                else { var uv = new List<Vector4>(); source.mesh.GetUVs(i, uv); mesh.SetUVs(i, uv); }
            }
            mesh.subMeshCount = 1; mesh.SetIndices(source.mesh.GetIndices(0), MeshTopology.Triangles, 0, false);
            if (source.evidence.derivativeTangentsRebuilt) mesh.RecalculateTangents();
            var finalTangents = mesh.tangents;
            if (finalTangents.Length != mesh.vertexCount || finalTangents.Any(t => !Finite(new Vector3(t.x, t.y, t.z)) || float.IsNaN(t.w) || float.IsInfinity(t.w)))
                throw new InvalidOperationException("Derivative tangents incomplete or nonfinite: " + source.evidence.child +
                    ", vertices=" + mesh.vertexCount + ", tangents=" + finalTangents.Length);
            source.evidence.derivativeTangentCount = finalTangents.Length;
            mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            if (mesh.vertexBufferCount == 0 || mesh.GetIndexCount(0) != source.mesh.GetIndexCount(0))
                throw new InvalidOperationException("Derivative mesh buffer/topology invalid.");
            if (create) AssetDatabase.CreateAsset(mesh, source.evidence.meshOutput);
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        static Material UpsertMaterial(Source source, Shader shader)
        {
            var material = Owned<Material>(source.evidence.materialOutput);
            bool create = material == null;
            if (create) material = new Material(shader); else material.shader = shader;
            material.name = Path.GetFileNameWithoutExtension(source.evidence.materialOutput);
            material.SetTexture("_BaseMap", SourceTexture(source.material, "_BaseMap"));
            material.SetTexture("_BumpMap", SourceTexture(source.material, "_BumpMap"));
            foreach (string map in new[] { "_BaseMap", "_BumpMap" })
            {
                material.SetTextureScale(map, source.material.GetTextureScale(map));
                material.SetTextureOffset(map, source.material.GetTextureOffset(map));
            }
            material.SetFloat("_BumpScale", .7f); material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Tint", Pigment); material.SetFloat("_TintStrength", 0); material.SetFloat("_Saturation", .6f);
            material.SetFloat("_AlphaClip", 0); material.SetFloat("_Cutoff", .4f); material.SetFloat("_Visibility", 1);
            material.SetFloat("_Formation", 1); material.SetFloat("_GroundY", -10000);
            material.enableInstancing = true; material.renderQueue = -1;
            if (create) AssetDatabase.CreateAsset(material, source.evidence.materialOutput);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static Texture SourceTexture(Material mat, string property)
        {
            if (!mat.HasProperty(property) || mat.GetTexture(property) == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat.GetTexture(property))))
                throw new InvalidOperationException("Source texture unresolved: " + mat.name + " " + property);
            return mat.GetTexture(property);
        }
        static T Owned<T>(string path) where T : UnityEngine.Object
        {
            if (!path.StartsWith(Output + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Output outside owned folder.");
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && !(existing is T)) throw new InvalidOperationException("Existing output type mismatch: " + path);
            return existing as T;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
        static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
        static Bounds BoundsOf(IEnumerable<Vector3> points)
        {
            using (var e = points.GetEnumerator())
            {
                if (!e.MoveNext()) throw new InvalidOperationException("Empty source mesh.");
                var b = new Bounds(e.Current, Vector3.zero); while (e.MoveNext()) b.Encapsulate(e.Current); return b;
            }
        }
        static void Track(string path, Dictionary<string, FileEvidence> files)
        {
            foreach (string p in new[] { path, path + ".meta" })
                if (!files.ContainsKey(p)) files.Add(p, new FileEvidence { path = p, sha256Before = Hash(p) });
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(Path.GetFullPath(path)))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
