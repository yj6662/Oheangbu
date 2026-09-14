using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Adds shared source-derived LODs only to the existing capital House_2 placements.</summary>
    public static class WorldMacroVisualCorridorLod
    {
        public const string SourcePath = "Assets/House_2/house2 .fbx";
        public const string Lod1Path = WorldMacroVisualCorridorAuthoring.Folder + "/HouseLOD/House2_LOD1.fbx";
        public const string Lod2Path = WorldMacroVisualCorridorAuthoring.Folder + "/HouseLOD/House2_LOD2.fbx";
        private const string CapitalGroup = "03_CapitalExterior";
        private const string OwnedNodePrefix = "House2_SourceDerived_";
        private const int ExpectedHouses = 40;
        private const int ExpectedSourceTriangles = 86400;
        private const int ExpectedMaterialSlots = 12;

        [Serializable]
        private sealed class Report
        {
            public string utc;
            public string status;
            public string scope = "Existing 40 capital House_2 placements only. LOD0, transforms, colliders, source FBX, provider materials, common placement code, and capital authoring code remain unchanged.";
            public string source = SourcePath;
            public string lod1 = Lod1Path;
            public string lod2 = Lod2Path;
            public int houses;
            public int lodGroups;
            public int sourceTriangles;
            public int lod1Triangles;
            public int lod2Triangles;
            public int materialBatches;
            public int sharedLod1Meshes;
            public int sharedLod2Meshes;
            public int lod1RendererInstances;
            public int lod2RendererInstances;
            public float maximumBoundsSizeError;
            public float maximumBoundsCenterError;
            public bool lod0Preserved;
            public bool thresholdsExact;
            public bool meshesShared;
            public bool materialsPreserved;
            public string[] failures = Array.Empty<string>();
        }

        private sealed class BakedLevel
        {
            public Mesh[] Meshes;
            public Material[] Materials;
            public Bounds Bounds;
            public int Triangles;
        }

        private static string Output => WorldMacroVisualCorridorAuthoring.Output + "/HouseLOD";
        private static string MeshFolder => WorldMacroVisualCorridorAuthoring.Folder + "/Meshes/HouseLOD";

        public static string Execute(string argument)
        {
            switch ((argument ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "optimize": return OptimizeHouseLods();
                case "validate": return ValidateHouseLods();
                default: throw new ArgumentException("Expected optimize or validate.");
            }
        }

        public static string OptimizeHouseLods()
        {
            RequireScene();
            EnsureFolder(MeshFolder);
            Directory.CreateDirectory(Output);
            GameObject source = Need<GameObject>(SourcePath);
            var sourceInstance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (sourceInstance == null) throw new InvalidOperationException("Could not instantiate House_2 source.");
            BakedLevel lod1;
            BakedLevel lod2;
            Bounds sourceBounds;
            Dictionary<string, Material> sourceMaterials;
            try
            {
                sourceInstance.hideFlags = HideFlags.HideAndDontSave;
                sourceInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                sourceInstance.transform.localScale = Vector3.one;
                Renderer[] sourceRenderers = sourceInstance.GetComponentsInChildren<Renderer>(true);
                int sourceTriangles = Triangles(sourceRenderers);
                if (sourceTriangles != ExpectedSourceTriangles)
                    throw new InvalidOperationException("House_2 source changed: expected 86400 triangles, got " + sourceTriangles + ".");
                sourceBounds = WorldMacroVisualCorridorAuthoring.SourceBounds(sourceRenderers);
                sourceMaterials = SourceMaterialMap(sourceRenderers);
                if (sourceMaterials.Count != ExpectedMaterialSlots)
                    throw new InvalidOperationException("House_2 material slots changed: expected 12, got " + sourceMaterials.Count + ".");
                lod1 = BakeSharedLevel(Lod1Path, 1, sourceMaterials);
                lod2 = BakeSharedLevel(Lod2Path, 2, sourceMaterials);
            }
            finally
            {
                Object.DestroyImmediate(sourceInstance);
            }

            if (lod1.Triangles <= 0 || lod1.Triangles > 24000)
                throw new InvalidOperationException("House LOD1 must be within 1..24000 triangles; got " + lod1.Triangles + ".");
            if (lod2.Triangles <= 0 || lod2.Triangles > 8000)
                throw new InvalidOperationException("House LOD2 must be within 1..8000 triangles; got " + lod2.Triangles + ".");
            if (lod1.Meshes.Length != ExpectedMaterialSlots || lod2.Meshes.Length != ExpectedMaterialSlots)
                throw new InvalidOperationException("Source-derived LODs must retain all 12 source material batches.");
            ValidateBounds(sourceBounds, lod1.Bounds, "LOD1");
            ValidateBounds(sourceBounds, lod2.Bounds, "LOD2");

            LODGroup[] houses = FindHouses();
            if (houses.Length != ExpectedHouses)
                throw new InvalidOperationException("Expected exactly 40 capital House_2 instances by their 86400-triangle LOD0 signature; found " + houses.Length + ".");
            var originalLod0 = houses.ToDictionary(group => group, group => group.GetLODs()[0].renderers.ToArray());
            foreach (LODGroup group in houses)
            {
                RemoveOwnedNodes(group.transform);
                Renderer[] lod0 = originalLod0[group];
                if (lod0.Length == 0) throw new InvalidOperationException("House has an empty LOD0: " + PathOf(group.transform));
                Transform reference = lod0[0].transform;
                Renderer[] level1 = CreateLevel(group.transform, 1, lod1, reference);
                Renderer[] level2 = CreateLevel(group.transform, 2, lod2, reference);
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;
                group.SetLODs(new[] { new LOD(.14f, lod0), new LOD(.055f, level1), new LOD(.006f, level2) });
                group.RecalculateBounds();
                EditorUtility.SetDirty(group);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return ValidateHouseLods();
        }

        public static string ValidateHouseLods()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var failures = new List<string>();
            LODGroup[] houses = FindHouses();
            if (houses.Length != ExpectedHouses) failures.Add("Capital House_2 count expected 40, got " + houses.Length + ".");
            Mesh[] expected1 = LoadMeshes(1);
            Mesh[] expected2 = LoadMeshes(2);
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("o"),
                houses = houses.Length,
                lodGroups = houses.Length,
                sourceTriangles = ExpectedSourceTriangles,
                sharedLod1Meshes = expected1.Length,
                sharedLod2Meshes = expected2.Length
            };
            var seen1 = new HashSet<Mesh>();
            var seen2 = new HashSet<Mesh>();
            bool lod0Preserved = true, thresholds = true, materials = true;
            foreach (LODGroup group in houses)
            {
                LOD[] levels = group.GetLODs();
                if (levels.Length != 3)
                {
                    failures.Add(PathOf(group.transform) + " has " + levels.Length + " LOD levels.");
                    continue;
                }
                int sourceTris = Triangles(levels[0].renderers);
                int lod1Tris = Triangles(levels[1].renderers);
                int lod2Tris = Triangles(levels[2].renderers);
                lod0Preserved &= sourceTris == ExpectedSourceTriangles && levels[0].renderers.All(renderer => renderer != null
                    && !renderer.name.StartsWith(OwnedNodePrefix, StringComparison.Ordinal));
                thresholds &= Mathf.Abs(levels[0].screenRelativeTransitionHeight - .14f) < .0001f
                    && Mathf.Abs(levels[1].screenRelativeTransitionHeight - .055f) < .0001f
                    && Mathf.Abs(levels[2].screenRelativeTransitionHeight - .006f) < .0001f;
                if (lod1Tris <= 0 || lod1Tris > 24000) failures.Add(PathOf(group.transform) + " LOD1 triangles=" + lod1Tris);
                if (lod2Tris <= 0 || lod2Tris > 8000) failures.Add(PathOf(group.transform) + " LOD2 triangles=" + lod2Tris);
                if (report.lod1Triangles == 0) report.lod1Triangles = lod1Tris;
                if (report.lod2Triangles == 0) report.lod2Triangles = lod2Tris;
                report.lod1RendererInstances += levels[1].renderers.Length;
                report.lod2RendererInstances += levels[2].renderers.Length;
                foreach (Renderer renderer in levels[1].renderers)
                {
                    Mesh mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                    if (mesh != null) seen1.Add(mesh);
                }
                foreach (Renderer renderer in levels[2].renderers)
                {
                    Mesh mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                    if (mesh != null) seen2.Add(mesh);
                }
                materials &= levels.Skip(1).SelectMany(level => level.renderers).Where(renderer => renderer != null)
                    .All(renderer => renderer.sharedMaterials.All(material => material != null
                        && AssetDatabase.GetAssetPath(material).StartsWith(WorldMacroVisualCorridorAuthoring.Folder + "/Materials/", StringComparison.Ordinal)));
            }
            report.lod0Preserved = lod0Preserved;
            report.thresholdsExact = thresholds;
            report.meshesShared = expected1.Length > 0 && expected2.Length > 0
                && expected1.Length == ExpectedMaterialSlots && expected2.Length == ExpectedMaterialSlots
                && seen1.SetEquals(expected1) && seen2.SetEquals(expected2);
            report.materialsPreserved = materials;
            report.materialBatches = expected1.Length;
            if (!report.lod0Preserved) failures.Add("At least one original 86400-triangle LOD0 renderer set was replaced or altered.");
            if (!report.thresholdsExact) failures.Add("LOD thresholds must be exactly 0.14 / 0.055 / 0.006.");
            if (!report.meshesShared) failures.Add("Capital houses do not share the owned LOD mesh assets.");
            if (!report.materialsPreserved) failures.Add("LOD material batches are not using owned corridor materials derived from the source slots.");

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            GameObject lod1Asset = AssetDatabase.LoadAssetAtPath<GameObject>(Lod1Path);
            GameObject lod2Asset = AssetDatabase.LoadAssetAtPath<GameObject>(Lod2Path);
            if (source == null || lod1Asset == null || lod2Asset == null) failures.Add("Source or source-derived FBX asset is missing.");
            else
            {
                Bounds sourceBounds = AssetBounds(source);
                Bounds bounds1 = AssetBounds(lod1Asset);
                Bounds bounds2 = AssetBounds(lod2Asset);
                report.maximumBoundsSizeError = Mathf.Max(SizeError(sourceBounds, bounds1), SizeError(sourceBounds, bounds2));
                report.maximumBoundsCenterError = Mathf.Max(CenterError(sourceBounds, bounds1), CenterError(sourceBounds, bounds2));
                if (report.maximumBoundsSizeError > .02f || report.maximumBoundsCenterError > .02f)
                    failures.Add("Source-derived LOD bounds differ from House_2 by more than 2%.");
            }
            report.failures = failures.Distinct().ToArray();
            report.status = report.failures.Length == 0 ? "HOUSE_LODS_TECHNICAL_PASS" : "HOUSE_LODS_FAILED";
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Output + "/house_lod_report.json", json);
            return json;
        }

        private static BakedLevel BakeSharedLevel(string assetPath, int level, Dictionary<string, Material> sourceMaterials)
        {
            GameObject asset = Need<GameObject>(assetPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (instance == null) throw new InvalidOperationException("Could not instantiate " + assetPath);
            try
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                var batches = new Dictionary<Material, List<CombineInstance>>();
                foreach (Renderer renderer in renderers)
                {
                    Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        if (submesh >= renderer.sharedMaterials.Length || renderer.sharedMaterials[submesh] == null)
                            throw new InvalidOperationException(assetPath + " has a missing material slot.");
                        string key = CleanMaterialName(renderer.sharedMaterials[submesh].name);
                        if (!sourceMaterials.TryGetValue(key, out Material sourceMaterial))
                            throw new InvalidOperationException(assetPath + " has an unmapped source material slot: " + key);
                        if (!batches.TryGetValue(sourceMaterial, out List<CombineInstance> list))
                        {
                            list = new List<CombineInstance>();
                            batches.Add(sourceMaterial, list);
                        }
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = submesh, transform = renderer.localToWorldMatrix });
                    }
                }
                var meshes = new List<Mesh>();
                var materials = new List<Material>();
                int index = 0;
                foreach (var batch in batches.OrderBy(pair => AssetDatabase.GetAssetPath(pair.Key), StringComparer.Ordinal))
                {
                    var combined = new Mesh { name = "House2_SourceDerived_LOD" + level + "_M" + index, indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(batch.Value.ToArray(), true, true);
                    combined.RecalculateBounds();
                    if (combined.vertexCount > 0 && (combined.uv == null || combined.uv.Length != combined.vertexCount))
                        throw new InvalidOperationException(assetPath + " lost UVs in material batch " + index + ".");
                    string path = MeshFolder + "/House2_LOD" + level + "_M" + index++ + ".asset";
                    Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (saved == null)
                    {
                        AssetDatabase.CreateAsset(combined, path);
                        saved = combined;
                    }
                    else
                    {
                        EditorUtility.CopySerialized(combined, saved);
                        Object.DestroyImmediate(combined);
                        EditorUtility.SetDirty(saved);
                    }
                    meshes.Add(saved);
                    materials.Add(WorldMacroVisualCorridorAuthoring.Surface(batch.Key));
                }
                if (meshes.Count == 0) throw new InvalidOperationException(assetPath + " produced no material batches.");
                return new BakedLevel
                {
                    Meshes = meshes.ToArray(),
                    Materials = materials.ToArray(),
                    Bounds = BoundsOf(meshes),
                    Triangles = meshes.Sum(Triangles)
                };
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Renderer[] CreateLevel(Transform holder, int level, BakedLevel baked, Transform reference)
        {
            var renderers = new Renderer[baked.Meshes.Length];
            for (int i = 0; i < baked.Meshes.Length; i++)
            {
                var node = new GameObject(OwnedNodePrefix + "LOD" + level + "_M" + i).transform;
                node.SetParent(holder, false);
                node.localPosition = reference.localPosition;
                node.localRotation = reference.localRotation;
                node.localScale = reference.localScale;
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = baked.Meshes[i];
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = baked.Materials[i];
                node.gameObject.isStatic = true;
                renderers[i] = renderer;
            }
            return renderers;
        }

        private static LODGroup[] FindHouses()
        {
            GameObject root = GameObject.Find(WorldMacroVisualCorridorAuthoring.RootName);
            Transform capital = root != null ? root.transform.Find(CapitalGroup) : null;
            if (capital == null) throw new InvalidOperationException("Capital visual corridor group is missing.");
            return capital.GetComponentsInChildren<LODGroup>(true).Where(group =>
            {
                LOD[] levels = group.GetLODs();
                return levels.Length > 0 && Triangles(levels[0].renderers) == ExpectedSourceTriangles;
            }).OrderBy(group => PathOf(group.transform), StringComparer.Ordinal).ToArray();
        }

        private static Dictionary<string, Material> SourceMaterialMap(Renderer[] renderers)
        {
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            var importer = AssetImporter.GetAtPath(SourcePath) as ModelImporter;
            if (importer != null)
            {
                foreach (var pair in importer.GetExternalObjectMap())
                    if (pair.Key.type == typeof(Material) && pair.Value is Material mapped)
                        result[CleanMaterialName(pair.Key.name)] = mapped;
            }
            if (result.Count == 0)
                foreach (Material material in renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null))
                    result[CleanMaterialName(material.name)] = material;
            return result;
        }

        private static string CleanMaterialName(string name)
        {
            string clean = name.Replace(" (Instance)", string.Empty);
            return Regex.Replace(clean, @"\.\d{3}$", string.Empty);
        }

        private static void RemoveOwnedNodes(Transform holder)
        {
            foreach (Transform child in holder.Cast<Transform>().Where(child => child.name.StartsWith(OwnedNodePrefix, StringComparison.Ordinal)).ToArray())
                Object.DestroyImmediate(child.gameObject);
        }

        private static void ValidateBounds(Bounds source, Bounds candidate, string label)
        {
            float size = SizeError(source, candidate);
            float center = CenterError(source, candidate);
            if (size > .02f || center > .02f)
                throw new InvalidOperationException(label + " bounds do not match House_2 axes/envelope: sizeError="
                    + size.ToString("P2") + "; centerError=" + center.ToString("P2") + ".");
        }

        private static Bounds AssetBounds(GameObject asset)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (instance == null) throw new InvalidOperationException("Could not measure " + AssetDatabase.GetAssetPath(asset));
            try
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                return WorldMacroVisualCorridorAuthoring.SourceBounds(instance.GetComponentsInChildren<Renderer>(true));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static float SizeError(Bounds source, Bounds candidate)
        {
            return Mathf.Max(Mathf.Abs(candidate.size.x / Mathf.Max(.001f, source.size.x) - 1f),
                Mathf.Abs(candidate.size.y / Mathf.Max(.001f, source.size.y) - 1f),
                Mathf.Abs(candidate.size.z / Mathf.Max(.001f, source.size.z) - 1f));
        }

        private static float CenterError(Bounds source, Bounds candidate)
        {
            return Vector3.Distance(source.center, candidate.center) / Mathf.Max(.001f, source.size.magnitude);
        }

        private static Bounds BoundsOf(IEnumerable<Mesh> meshes)
        {
            bool has = false;
            Bounds bounds = default;
            foreach (Mesh mesh in meshes)
            {
                if (!has) { bounds = mesh.bounds; has = true; }
                else bounds.Encapsulate(mesh.bounds);
            }
            if (!has) throw new InvalidOperationException("No owned LOD mesh bounds.");
            return bounds;
        }

        private static Mesh[] LoadMeshes(int level)
        {
            var meshes = new List<Mesh>();
            for (int index = 0; index < ExpectedMaterialSlots; index++)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/House2_LOD" + level + "_M" + index + ".asset");
                if (mesh != null) meshes.Add(mesh);
            }
            return meshes.ToArray();
        }

        private static int Triangles(IEnumerable<Renderer> renderers)
        {
            return renderers.Where(renderer => renderer != null).Sum(renderer =>
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                return mesh == null ? 0 : Triangles(mesh);
            });
        }

        private static int Triangles(Mesh mesh)
        {
            int count = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetTopology(submesh) == MeshTopology.Triangles) count += (int)mesh.GetIndexCount(submesh) / 3;
            return count;
        }

        private static T Need<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("Required asset has not imported: " + path);
            return asset;
        }

        private static void RequireScene()
        {
            if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest in Edit mode.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f)
                throw new InvalidOperationException("House LOD authoring stopped: system commit >=85%.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("Invalid asset folder " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string PathOf(Transform transform)
        {
            var names = new Stack<string>();
            for (Transform cursor = transform; cursor != null; cursor = cursor.parent) names.Push(cursor.name);
            return string.Join("/", names);
        }
    }
}
