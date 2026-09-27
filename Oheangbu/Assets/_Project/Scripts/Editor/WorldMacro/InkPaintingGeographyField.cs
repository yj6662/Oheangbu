using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Bakes paint guidance from the current visible surface; never changes its geometry.</summary>
    public static class InkPaintingGeographyField
    {
        [Serializable] public sealed class Receipt
        {
            public string scene, utc, channels, output;
            public int width, height, matchedRenderers, rasterizedMeshes, triangles, coveredPixels, invalidPixels;
            public Vector4 worldRect;
            public Vector3 sourceBoundsMin, sourceBoundsMax;
            public float minHeight, maxHeight, maxRelativeRelief, coverage, mountainCoverage;
            public float neighborhoodMetres = 320, broadNeighborhoodMetres = 640, flowSmoothingMetres = 32;
            public float mountainReliefLow = 20, mountainReliefHigh = 100, reliefNormalizationMetres = 256;
            public int[] mountainHistogram = new int[10];
            public string[] materials;
        }

        public static Receipt LastReceipt { get; private set; }
        public static Rect CompactBounds => new Rect(-2000, -3000, 4000, 6000);
        public static Vector4 ShaderRect(Rect rect) => new Vector4(rect.xMin, rect.yMin, rect.width, rect.height);

        /// <summary>
        /// R = relative-relief mountain coverage, GB = downhill XZ direction * .5 + .5,
        /// A = relative relief / 256 m. UV = (worldXZ - rect.xy) / rect.zw.
        /// Invalid samples are (0,.5,.5,0). Runtime can reject UV outside [0,1].
        /// Null output returns an owned transient texture; otherwise output must be a derived .asset path.
        /// The compact 4 x 6 km domain is deliberate: distant backdrop bounds cannot dilute resolution.
        /// </summary>
        public static Texture2D Bake(Material[] sources, string outputAssetPath, out Vector4 worldRect,
            int width = 512, int height = 768, Rect? bounds = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake the geography field in Edit mode.");
            if (sources == null || sources.Length == 0 || sources.Any(m => m == null)) throw new ArgumentException("Explicit source surface materials are required.");
            if (width < 16 || height < 16 || width > 2048 || height > 3072) throw new ArgumentOutOfRangeException("Field dimensions");
            if (!string.IsNullOrEmpty(outputAssetPath) && (!outputAssetPath.StartsWith("Assets/", StringComparison.Ordinal) || !outputAssetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("The derived field output must be an Assets/... .asset path.");
            var domain = bounds ?? CompactBounds;
            if (domain.width <= 0 || domain.height <= 0) throw new ArgumentException("Positive field bounds are required.");
            worldRect = ShaderRect(domain);
            var scene = SceneManager.GetActiveScene();
            var selected = new HashSet<Material>(sources);
            var receipt = new Receipt { scene = scene.path, utc = DateTime.UtcNow.ToString("o"), width = width, height = height,
                worldRect = worldRect, output = outputAssetPath, channels = "R=mountain coverage; GB=downhill XZ*0.5+0.5; A=relative relief/256m; invalid=(0,.5,.5,0)",
                materials = sources.Distinct().Select(m => AssetDatabase.GetAssetPath(m) + " | " + m.shader.name).ToArray() };
            LastReceipt = null;
            int count = checked(width * height);
            var heights = Enumerable.Repeat(float.NegativeInfinity, count).ToArray();
            bool hasSourceBounds = false;
            Bounds sourceBounds = default;
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var materials = renderer.sharedMaterials;
                if (!materials.Any(m => m != null && selected.Contains(m))) continue;
                receipt.matchedRenderers++;
                if (!hasSourceBounds) { sourceBounds = renderer.bounds; hasSourceBounds = true; }
                else sourceBounds.Encapsulate(renderer.bounds);
                var b = renderer.bounds;
                if (b.max.x < domain.xMin || b.min.x > domain.xMax || b.max.z < domain.yMin || b.min.z > domain.yMax) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Surface MeshFilter missing: " + renderer.name);
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable) throw new InvalidOperationException("Surface mesh is not CPU-readable: " + AssetDatabase.GetAssetPath(mesh));
                var vertices = mesh.vertices;
                var matrix = renderer.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                receipt.rasterizedMeshes++;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    // Unity repeats the final submesh when extra material slots are present.
                    bool selectedSubmesh = sub < materials.Length && selected.Contains(materials[sub]);
                    if (sub == mesh.subMeshCount - 1 && materials.Length > mesh.subMeshCount)
                        selectedSubmesh |= materials.Skip(mesh.subMeshCount).Any(m => selected.Contains(m));
                    if (!selectedSubmesh) continue;
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("Surface submesh is not triangular: " + mesh.name);
                    var indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        receipt.triangles++;
                        Rasterize(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], domain, width, height, heights);
                    }
                }
            }
            if (!hasSourceBounds || receipt.triangles == 0) throw new InvalidOperationException("No selected active surface triangles intersect the field domain.");
            receipt.sourceBoundsMin = sourceBounds.min; receipt.sourceBoundsMax = sourceBounds.max;
            float dx = domain.width / width, dz = domain.height / height;
            var valid = new bool[count];
            receipt.minHeight = float.PositiveInfinity; receipt.maxHeight = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                valid[i] = !float.IsNegativeInfinity(heights[i]);
                if (!valid[i]) continue;
                receipt.coveredPixels++;
                receipt.minHeight = Mathf.Min(receipt.minHeight, heights[i]); receipt.maxHeight = Mathf.Max(receipt.maxHeight, heights[i]);
            }
            if (receipt.coveredPixels == 0) throw new InvalidOperationException("The selected triangles produced no surface samples.");
            receipt.invalidPixels = count - receipt.coveredPixels;
            receipt.coverage = receipt.coveredPixels / (float)count;
            var smooth = SmoothValid(heights, valid, width, height, Mathf.CeilToInt(receipt.flowSmoothingMetres / dx), Mathf.CeilToInt(receipt.flowSmoothingMetres / dz));
            var localMin = Minimum(heights, width, height, Mathf.CeilToInt(receipt.neighborhoodMetres / dx), Mathf.CeilToInt(receipt.neighborhoodMetres / dz));
            var broadMin = Minimum(heights, width, height, Mathf.CeilToInt(receipt.broadNeighborhoodMetres / dx), Mathf.CeilToInt(receipt.broadNeighborhoodMetres / dz));
            var pixels = new Color32[count];
            int mountains = 0;
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                if (!valid[i]) { pixels[i] = new Color32(0, 128, 128, 0); continue; }
                float relief = Mathf.Max(heights[i] - localMin[i], (heights[i] - broadMin[i]) * .65f);
                float mountain = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(receipt.mountainReliefLow, receipt.mountainReliefHigh, relief));
                receipt.maxRelativeRelief = Mathf.Max(receipt.maxRelativeRelief, relief);
                receipt.mountainHistogram[Mathf.Min(9, (int)(mountain * 10))]++;
                if (mountain >= .5f) mountains++;
                int left = x > 0 && valid[i - 1] ? i - 1 : i, right = x + 1 < width && valid[i + 1] ? i + 1 : i;
                int down = z > 0 && valid[i - width] ? i - width : i, up = z + 1 < height && valid[i + width] ? i + width : i;
                var flow = new Vector2((smooth[left] - smooth[right]) / ((right == left ? 1 : right - left) * dx),
                    (smooth[down] - smooth[up]) / ((up == down ? 1 : (up - down) / width) * dz));
                if (flow.sqrMagnitude > .000001f) flow.Normalize(); else flow = Vector2.zero;
                pixels[i] = new Color(mountain, flow.x * .5f + .5f, flow.y * .5f + .5f, Mathf.Clamp01(relief / receipt.reliefNormalizationMetres));
            }
            receipt.mountainCoverage = mountains / (float)receipt.coveredPixels;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true) { name = "InkPaintingGeographyField", wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear, anisoLevel = 1 };
                texture.SetPixels32(pixels); texture.Apply(true, false);
                if (!string.IsNullOrEmpty(outputAssetPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputAssetPath));
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    var existing = AssetDatabase.LoadMainAssetAtPath(outputAssetPath);
                    if (existing != null && !(existing is Texture2D)) throw new InvalidOperationException("Output path is occupied by a non-texture asset.");
                    if (existing == null) AssetDatabase.CreateAsset(texture, outputAssetPath);
                    else { EditorUtility.CopySerialized(texture, existing); Object.DestroyImmediate(texture); texture = (Texture2D)existing; EditorUtility.SetDirty(texture); }
                    AssetDatabase.SaveAssetIfDirty(texture);
                    File.WriteAllText(Path.ChangeExtension(outputAssetPath, ".receipt.json"), JsonUtility.ToJson(receipt, true));
                }
                LastReceipt = receipt;
                return texture;
            }
            catch { if (texture != null && !EditorUtility.IsPersistent(texture)) Object.DestroyImmediate(texture); throw; }
        }

        static void Rasterize(Vector3 a, Vector3 b, Vector3 c, Rect rect, int width, int height, float[] heights)
        {
            float ax = (a.x - rect.xMin) * width / rect.width - .5f, az = (a.z - rect.yMin) * height / rect.height - .5f;
            float bx = (b.x - rect.xMin) * width / rect.width - .5f, bz = (b.z - rect.yMin) * height / rect.height - .5f;
            float cx = (c.x - rect.xMin) * width / rect.width - .5f, cz = (c.z - rect.yMin) * height / rect.height - .5f;
            float determinant = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (Mathf.Abs(determinant) < .000001f) return;
            int minX = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(ax, Mathf.Min(bx, cx)))), maxX = Mathf.Min(width - 1, Mathf.FloorToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
            int minZ = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(az, Mathf.Min(bz, cz)))), maxZ = Mathf.Min(height - 1, Mathf.FloorToInt(Mathf.Max(az, Mathf.Max(bz, cz))));
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                float u = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / determinant;
                float v = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / determinant;
                float w = 1 - u - v;
                if (u < -.00001f || v < -.00001f || w < -.00001f) continue;
                float y = a.y * u + b.y * v + c.y * w;
                int i = z * width + x;
                if (y > heights[i]) heights[i] = y; // Highest visible surface wins over cave floors.
            }
        }

        // Count-weighted integral blur: no artificial zero-height slopes at holes or coastline.
        static float[] SmoothValid(float[] input, bool[] valid, int width, int height, int rx, int rz)
        {
            int stride = width + 1;
            var sums = new double[(width + 1) * (height + 1)]; var counts = new int[sums.Length];
            for (int z = 0; z < height; z++)
            {
                double row = 0; int rowCount = 0;
                for (int x = 0; x < width; x++)
                {
                    int i = z * width + x, p = (z + 1) * stride + x + 1;
                    if (valid[i]) { row += input[i]; rowCount++; }
                    sums[p] = sums[p - stride] + row; counts[p] = counts[p - stride] + rowCount;
                }
            }
            var result = new float[input.Length];
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int x0 = Mathf.Max(0, x - rx), x1 = Mathf.Min(width, x + rx + 1), z0 = Mathf.Max(0, z - rz), z1 = Mathf.Min(height, z + rz + 1);
                int a = z0 * stride + x0, b = z0 * stride + x1, c = z1 * stride + x0, d = z1 * stride + x1;
                int n = counts[d] - counts[b] - counts[c] + counts[a];
                result[z * width + x] = n > 0 ? (float)((sums[d] - sums[b] - sums[c] + sums[a]) / n) : 0;
            }
            return result;
        }

        // Separable sliding minimum is O(pixels); invalid cells never create a false low valley.
        static float[] Minimum(float[] input, int width, int height, int rx, int rz)
        {
            var temp = new float[input.Length]; var result = new float[input.Length];
            var queue = new int[Mathf.Max(width, height)];
            for (int z = 0; z < height; z++) MinimumLine(input, temp, z * width, 1, width, rx, queue);
            for (int x = 0; x < width; x++) MinimumLine(temp, result, x, width, height, rz, queue);
            return result;
        }
        static void MinimumLine(float[] input, float[] output, int offset, int stride, int length, int radius, int[] queue)
        {
            int head = 0, tail = 0, added = -1;
            for (int x = 0; x < length; x++)
            {
                int right = Mathf.Min(length - 1, x + radius);
                while (added < right)
                {
                    int k = ++added; float value = input[offset + k * stride];
                    if (float.IsNegativeInfinity(value)) continue;
                    while (tail > head && input[offset + queue[tail - 1] * stride] >= value) tail--;
                    queue[tail++] = k;
                }
                while (head < tail && queue[head] < x - radius) head++;
                output[offset + x * stride] = head < tail ? input[offset + queue[head] * stride] : float.PositiveInfinity;
            }
        }
    }
}
