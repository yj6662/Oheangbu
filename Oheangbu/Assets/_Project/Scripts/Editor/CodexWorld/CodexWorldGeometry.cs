using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// Deterministic, asset-independent geometry for the canonical Codex world scene.
    /// Artistic dimensions are prototype authoring choices; this does not change realm data.
    /// Callers own the returned meshes and decide where to save them.
    /// </summary>
    public static class CodexWorldGeometry
    {
        private static readonly float[] RouteZ = { 0f, 35f, 70f, 120f, 165f, 220f };
        private static readonly float[] RouteX = { 0f, -6f, 7f, 20f, -6f, 6f };
        private static readonly float[] RouteY = { 0f, 0.5f, 2.2f, 5.5f, 9f, 13f };

        /// <summary>Mine mouth (0,0), sweeping path to the inn (20,120), then an upper pass.</summary>
        public static float PathX(float z)
        {
            return RouteValue(RouteX, z);
        }

        /// <summary>
        /// Ground height shared by the actual mesh, props, cameras and collision audit.
        /// The inner eight-metre corridor has a small crossfall; hills begin outside it.
        /// Origin is zero, and the inn anchor at (20,120) is exactly 5.5 metres high.
        /// </summary>
        public static float Height(float x, float z)
        {
            float offset = x - PathX(z);
            float distance = Mathf.Abs(offset);
            float shoulder = Mathf.Max(0f, distance - 4f);
            float mountain = Mathf.Pow(shoulder, 1.12f) * 0.105f;
            float broad = Mathf.PerlinNoise(x * 0.025f + 36.8f, z * 0.019f + 8.1f);
            float grain = Mathf.PerlinNoise(x * 0.075f + 11.2f, z * 0.067f + 17.7f);
            float bank = mountain * (0.45f + broad * 1.4f);
            float slopeDetail = Mathf.SmoothStep(0f, 1f, shoulder / 9f) * (grain - 0.45f) * 1.1f;
            return RouteValue(RouteY, z) + Mathf.Min(distance, 4f) * 0.025f + bank + slopeDetail;
        }

        /// <summary>
        /// Weathered closed granite volume, nominal radius one. Coherent displacement gives
        /// broad shoulders and eroded lobes; shared vertices give continuous surface normals.
        /// Increasing resolution samples the same shape instead of adding random sharp facets.
        /// </summary>
        public static Mesh Rock(int seed, int rings = 24, int sides = 40)
        {
            rings = Mathf.Clamp(rings, 3, 64);
            sides = Mathf.Clamp(sides, 5, 64);
            var random = new System.Random(seed);
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            float phase = Next(random, -0.3f, 0.3f);
            float leanX = Next(random, -0.16f, 0.16f);
            float leanZ = Next(random, -0.14f, 0.14f);
            float noiseSeed = Next(random, 1f, 80f);
            vertices.Add(new Vector3(-leanX, -0.95f, -leanZ));
            uv.Add(new Vector2(0.5f, 0f));
            for (int r = 1; r < rings; r++)
            {
                float t = r / (float)rings;
                float elevation = Mathf.Lerp(-0.95f, 1f, t);
                // Broad shoulders remain asymmetric, without a per-vertex random zigzag.
                float bulge = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.64f);
                float ringTwist = phase + Mathf.Sin(t * Mathf.PI * 2f + noiseSeed) * 0.055f;
                for (int s = 0; s < sides; s++)
                {
                    float angle = s * Mathf.PI * 2f / sides + ringTwist;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    var domain = new Vector3(cos * bulge, elevation, sin * bulge);
                    float erosion = SurfaceNoise(domain * 1.7f, noiseSeed) - 0.5f;
                    float broadLobe = Mathf.Sin(angle * 3f + noiseSeed + t * 1.6f) * 0.06f;
                    float radial = bulge * (1f + erosion * 0.32f + broadLobe);
                    float y = elevation + erosion * bulge * 0.10f;
                    // A soft superellipse preserves a granite block's shoulders, rather
                    // than smoothing the rock into a perfect spherical pebble.
                    float x = Mathf.Sign(cos) * Mathf.Pow(Mathf.Abs(cos), 0.88f) * radial;
                    float z = Mathf.Sign(sin) * Mathf.Pow(Mathf.Abs(sin), 0.88f) * radial;
                    vertices.Add(new Vector3(x + elevation * leanX, y, z + elevation * leanZ));
                    uv.Add(new Vector2(s / (float)sides, t));
                }
            }
            int top = vertices.Count;
            vertices.Add(new Vector3(leanX * 0.8f, 1f, leanZ * 0.8f));
            uv.Add(new Vector2(0.5f, 1f));
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                Triangle(triangles, vertices, 0, 1 + s, 1 + next, Vector3.down);
                for (int r = 0; r < rings - 2; r++)
                {
                    int a = 1 + r * sides + s;
                    int b = 1 + r * sides + next;
                    int c = b + sides;
                    int d = a + sides;
                    Vector3 facing = (vertices[a] + vertices[b] + vertices[c] + vertices[d]) * 0.25f;
                    Quad(triangles, vertices, a, b, c, d, facing);
                }
                int last = 1 + (rings - 2) * sides;
                Triangle(triangles, vertices, last + s, last + next, top, Vector3.up);
            }
            return Finish("Codex_Granite_" + seed, vertices, uv, triangles);
        }

        /// <summary>A continuous valley floor, x -width/2..width/2 and z 0..length.</summary>
        public static Mesh Ground(int seed, float width, float length, int columns, int rows)
        {
            RequirePositive(width, nameof(width));
            RequirePositive(length, nameof(length));
            columns = Mathf.Clamp(columns, 2, 1024);
            rows = Mathf.Clamp(rows, 2, 2048);
            var vertices = new List<Vector3>((columns + 1) * (rows + 1));
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(columns * rows * 6);
            for (int r = 0; r <= rows; r++)
            {
                float z = length * r / rows;
                for (int c = 0; c <= columns; c++)
                {
                    float x = width * (c / (float)columns - 0.5f);
                    vertices.Add(new Vector3(x, Height(x, z), z));
                    uv.Add(new Vector2(x * 0.08f, z * 0.08f));
                }
            }
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int a = r * (columns + 1) + c;
                int b = a + 1;
                int d = a + columns + 1;
                int e = d + 1;
                // Alternate diagonals without changing the shared analytic height function.
                if (((r + c + seed) & 1) == 0)
                    Quad(triangles, vertices, a, b, e, d, Vector3.up);
                else
                {
                    Triangle(triangles, vertices, a, b, d, Vector3.up);
                    Triangle(triangles, vertices, b, e, d, Vector3.up);
                }
            }
            return Finish("Codex_ValleyFloor_" + seed, vertices, uv, triangles);
        }

        /// <summary>A conforming, gently uneven path overlay. Five lateral strips follow the floor.</summary>
        public static Mesh PathRibbon(float startZ, float endZ, float width, int segments, float lift = 0.045f)
        {
            RequirePositive(width, nameof(width));
            if (endZ <= startZ) throw new ArgumentException("endZ must exceed startZ.");
            segments = Mathf.Clamp(segments, 2, 4096);
            const int strips = 6;
            var vertices = new List<Vector3>((segments + 1) * (strips + 1));
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(segments * strips * 6);
            for (int i = 0; i <= segments; i++)
            {
                float z = Mathf.Lerp(startZ, endZ, i / (float)segments);
                float x = PathX(z);
                Vector3 tangent = new Vector3(PathX(z + 0.15f) - PathX(z - 0.15f), 0f, 0.3f).normalized;
                Vector3 across = new Vector3(tangent.z, 0f, -tangent.x);
                float edge = 1f + (Mathf.PerlinNoise(z * 0.15f, 4.71f) - 0.5f) * 0.14f;
                for (int j = 0; j <= strips; j++)
                {
                    float u = j / (float)strips;
                    Vector3 p = new Vector3(x, 0f, z) + across * ((u - 0.5f) * width * edge);
                    p.y = Height(p.x, p.z) + lift;
                    vertices.Add(p);
                    uv.Add(new Vector2(u, (z - startZ) * 0.1f));
                }
            }
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < strips; j++)
            {
                int a = i * (strips + 1) + j;
                Quad(triangles, vertices, a, a + 1, a + strips + 2, a + strips + 1, Vector3.up);
            }
            return Finish("Codex_WornMountainPath", vertices, uv, triangles);
        }

        /// <summary>
        /// A volumetric range: warped watershed ridges, branching spurs and uneven saddles.
        /// All scales sample one continuous height field; extra subdivisions do not add random facets.
        /// Positions and seeds retain the canonical composition. The buried perimeter closes the mesh.
        /// </summary>
        public static Mesh Ridge(int seed, float width, float height, int segments = 256, int depthSegments = 128)
        {
            RequirePositive(width, nameof(width));
            RequirePositive(height, nameof(height));
            segments = Mathf.Clamp(segments, 16, 1024);
            depthSegments = Mathf.Clamp(depthSegments, 16, 256);
            var random = new System.Random(seed);
            float offset = Next(random, 12f, 190f);
            int peakCount = random.Next(3, 5);
            var peaks = new Vector3[peakCount];
            for (int p = 0; p < peakCount; p++)
                peaks[p] = new Vector3((p + Next(random, .15f, .85f)) / peakCount,
                    Next(random, .53f, 1f), Next(random, .14f, .23f));
            float depth = width * .24f;
            int stride = depthSegments + 1;
            var vertices = new List<Vector3>((segments + 1) * stride);
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(segments * (depthSegments + 1) * 6);
            for (int i = 0; i <= segments; i++)
            for (int j = 0; j <= depthSegments; j++)
            {
                float t = i / (float)segments;
                float u = j / (float)depthSegments * 2f - 1f;
                float bend = (Mathf.PerlinNoise(t * 5.7f + offset, offset) - .5f) * .54f;
                float cross = (u - bend) / (u < bend ? 1f + bend : 1f - bend);
                float crest = .12f;
                // Broad mass distribution, with connected saddles instead of a row of cones.
                for (int p = 0; p < peaks.Length; p++)
                {
                    float d = (t - peaks[p].x) / peaks[p].z;
                    crest = SmoothMaximum(crest, peaks[p].y * Mathf.Exp(-.5f * d * d), .12f);
                }
                float shoulder = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(cross * Mathf.PI * .5f)), cross < 0 ? 1.5f : 2.1f);
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / .20f);
                // Warp the drainage domain at a broader scale than the tributaries.
                // Ridged multifractals put small relief on large spurs, not evenly spaced grooves.
                float nx = t * 7f, nz = u * 2.4f;
                float wx = nx + (Mathf.PerlinNoise(nx * .65f + offset, nz * .65f + offset) - .5f) * 1.15f;
                float wz = nz + (Mathf.PerlinNoise(nx * .8f + offset + 19f, nz * .8f + offset + 33f) - .5f) * .75f;
                float ridges = 0f, weight = 1f, amplitude = .55f, frequency = 1f;
                for (int octave = 0; octave < 6; octave++)
                {
                    float n = Mathf.PerlinNoise(wx * frequency + offset, wz * frequency + offset * .71f);
                    float ridge = 1f - Mathf.Abs(2f * n - 1f);
                    ridge = ridge * ridge * weight;
                    ridges += ridge * amplitude;
                    weight = Mathf.Clamp01(ridge * 1.8f);
                    frequency *= 2.08f;
                    amplitude *= .48f;
                }
                float envelope = crest * shoulder;
                float relief = (ridges - .43f) * .32f * shoulder;
                float y = height * (-.18f + edge * (envelope * .95f + relief + .18f * shoulder));
                vertices.Add(new Vector3((t - .5f) * width, y, u * depth));
                uv.Add(new Vector2(t, (u + 1f) * .5f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * stride;
                for (int j = 0; j < depthSegments; j++)
                    Quad(triangles, vertices, a + j, a + j + stride, a + j + stride + 1, a + j + 1, Vector3.up);
                Quad(triangles, vertices, a, a + depthSegments, a + stride + depthSegments, a + stride, Vector3.down);
            }
            // The four height-field edges share the buried base plane, so the underside
            // closes directly without a vertical skirt or coincident side triangles.
            var mesh = Finish("Codex_MountainRidge_" + seed, vertices, uv, triangles);
            // Height-field gradients avoid triangulation-direction shading. Bottom-edge
            // vertices are buried; interior lighting has no contribution from underside faces.
            var normals = new Vector3[vertices.Count];
            var colors = new Color[vertices.Count];
            for (int i = 0; i <= segments; i++)
            for (int j = 0; j <= depthSegments; j++)
            {
                int k = i * stride + j;
                Vector3 dx = vertices[Mathf.Min(i + 1, segments) * stride + j] - vertices[Mathf.Max(i - 1, 0) * stride + j];
                Vector3 dz = vertices[i * stride + Mathf.Min(j + 1, depthSegments)] - vertices[i * stride + Mathf.Max(j - 1, 0)];
                normals[k] = Vector3.Cross(dz, dx).normalized;
                float neighbours = (vertices[Mathf.Min(i + 1, segments) * stride + j].y
                    + vertices[Mathf.Max(i - 1, 0) * stride + j].y
                    + vertices[i * stride + Mathf.Min(j + 1, depthSegments)].y
                    + vertices[i * stride + Mathf.Max(j - 1, 0)].y) * .25f;
                float cavity = Mathf.Clamp01((neighbours - vertices[k].y) / (width / segments) * .8f);
                colors[k] = new Color(1f - cavity * .6f, 1f, 1f, 1f);
            }
            mesh.normals = normals;
            mesh.colors = colors;
            return mesh;
        }

        /// <summary>Tapered closed tube with transported frames, for crooked pines, exposed roots and beams.</summary>
        public static Mesh Branch(Vector3[] points, float[] radii, int sides = 7)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("At least two branch points are required.");
            if (radii == null || radii.Length != points.Length) throw new ArgumentException("One radius per branch point is required.");
            sides = Mathf.Clamp(sides, 3, 64);
            for (int i = 0; i < points.Length; i++)
            {
                if (radii[i] < 0f || float.IsNaN(radii[i]) || float.IsInfinity(radii[i]))
                    throw new ArgumentException("Branch radii must be finite and nonnegative.");
                if (i > 0 && (points[i] - points[i - 1]).sqrMagnitude < 0.000001f)
                    throw new ArgumentException("Consecutive branch points must differ.");
            }
            var vertices = new List<Vector3>(points.Length * sides + 2);
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>((points.Length - 1) * sides * 6 + sides * 6);
            var tangents = new Vector3[points.Length];
            Vector3 firstTangent = (points[1] - points[0]).normalized;
            Vector3 basis = Mathf.Abs(Vector3.Dot(firstTangent, Vector3.up)) < 0.92f ? Vector3.up : Vector3.right;
            Vector3 previousNormal = Vector3.Cross(firstTangent, basis).normalized;
            Vector3 previousTangent = firstTangent;
            float distance = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent;
                if (i == 0) tangent = firstTangent;
                else if (i == points.Length - 1) tangent = (points[i] - points[i - 1]).normalized;
                else
                {
                    tangent = (points[i + 1] - points[i]).normalized + (points[i] - points[i - 1]).normalized;
                    tangent = tangent.sqrMagnitude > 0.00001f ? tangent.normalized : previousTangent;
                }
                tangents[i] = tangent;
                Vector3 normal = Quaternion.FromToRotation(previousTangent, tangent) * previousNormal;
                normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
                if (i > 0) distance += Vector3.Distance(points[i], points[i - 1]);
                // A tiny terminal ring avoids zero-area triangles when the requested tip radius is zero.
                float radius = Mathf.Max(0.001f, radii[i]);
                for (int s = 0; s < sides; s++)
                {
                    float angle = s * Mathf.PI * 2f / sides;
                    vertices.Add(points[i] + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius);
                    uv.Add(new Vector2(s / (float)sides, distance));
                }
                previousNormal = normal;
                previousTangent = tangent;
            }
            for (int i = 0; i < points.Length - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * sides + s;
                int b = i * sides + (s + 1) % sides;
                Vector3 facing = (vertices[a] + vertices[b]) * 0.5f - points[i];
                Quad(triangles, vertices, a, b, b + sides, a + sides, facing);
            }
            int bottom = vertices.Count;
            vertices.Add(points[0]); uv.Add(Vector2.zero);
            int top = vertices.Count;
            vertices.Add(points[points.Length - 1]); uv.Add(Vector2.one);
            int lastRing = (points.Length - 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                Triangle(triangles, vertices, bottom, s, next, -tangents[0]);
                Triangle(triangles, vertices, top, lastRing + s, lastRing + next, tangents[tangents.Length - 1]);
            }
            return Finish("Codex_TwistedBranch", vertices, uv, triangles);
        }

        /// <summary>
        /// Irregular inward-facing horseshoe mine walls and roof, open at both ends and at the floor.
        /// Mouth is at z=0, tunnel extends down -Z; floor edges remain y=0 for a separate floor mesh.
        /// </summary>
        public static Mesh Tunnel(int seed, float length = 34f, float width = 7f, float height = 6f)
        {
            RequirePositive(length, nameof(length));
            RequirePositive(width, nameof(width));
            RequirePositive(height, nameof(height));
            int rings = Mathf.Max(4, Mathf.CeilToInt(length / 2f));
            const int archSegments = 14;
            const int profileCount = archSegments + 3;
            var random = new System.Random(seed);
            float noiseSeed = Next(random, 0f, 90f);
            var vertices = new List<Vector3>((rings + 1) * profileCount);
            var uv = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(rings * (profileCount - 1) * 6);
            for (int r = 0; r <= rings; r++)
            {
                float z = Mathf.Lerp(-length, 0f, r / (float)rings);
                float bend = Mathf.Sin(z * 0.08f) * width * 0.045f;
                for (int p = 0; p < profileCount; p++)
                {
                    float x, y;
                    if (p == 0) { x = -width * 0.5f; y = 0f; }
                    else if (p == profileCount - 1) { x = width * 0.5f; y = 0f; }
                    else
                    {
                        float theta = Mathf.PI * (1f - (p - 1) / (float)archSegments);
                        float irregular = (Mathf.PerlinNoise(p * 0.73f + noiseSeed, r * 0.46f + 0.83f) - 0.5f) * 0.13f;
                        x = Mathf.Cos(theta) * width * 0.5f * (1f + irregular);
                        y = height * (0.27f + Mathf.Sin(theta) * 0.73f) * (1f + irregular * 0.6f);
                    }
                    vertices.Add(new Vector3(x + bend, y, z));
                    uv.Add(new Vector2(p / (float)(profileCount - 1) * 3f, z * 0.15f));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int p = 0; p < profileCount - 1; p++)
            {
                int a = r * profileCount + p;
                int b = a + 1;
                int c = b + profileCount;
                int d = a + profileCount;
                Vector3 center = (vertices[a] + vertices[b] + vertices[c] + vertices[d]) * 0.25f;
                Vector3 facing = new Vector3(-center.x, height * 0.28f - center.y, 0f);
                Quad(triangles, vertices, a, b, c, d, facing);
            }
            return Finish("Codex_MineInterior_" + seed, vertices, uv, triangles);
        }

        private static float SurfaceNoise(Vector3 p, float seed)
        {
            return (Mathf.PerlinNoise(p.x + seed, p.y + seed * 0.71f)
                + Mathf.PerlinNoise(p.y + seed * 0.31f, p.z + seed)
                + Mathf.PerlinNoise(p.z + seed * 0.47f, p.x + seed * 0.83f)) / 3f;
        }

        private static float SmoothMaximum(float a, float b, float width)
        {
            float h = Mathf.Max(width - Mathf.Abs(a - b), 0f) / width;
            return Mathf.Max(a, b) + h * h * width * 0.25f;
        }

        private static float RouteValue(float[] values, float z)
        {
            if (z <= RouteZ[0]) return values[0];
            for (int i = 0; i < RouteZ.Length - 1; i++)
                if (z <= RouteZ[i + 1])
                {
                    float t = Mathf.InverseLerp(RouteZ[i], RouteZ[i + 1], z);
                    return Mathf.Lerp(values[i], values[i + 1], t * t * (3f - 2f * t));
                }
            return values[values.Length - 1];
        }

        private static void RequirePositive(float value, string name)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "Dimension must be positive and finite.");
        }

        private static float Next(System.Random random, float min, float max)
        {
            return min + (max - min) * (float)random.NextDouble();
        }

        private static void Quad(List<int> triangles, List<Vector3> vertices, int a, int b, int c, int d, Vector3 facing)
        {
            Triangle(triangles, vertices, a, b, c, facing);
            Triangle(triangles, vertices, a, c, d, facing);
        }

        private static void Triangle(List<int> triangles, List<Vector3> vertices, int a, int b, int c, Vector3 facing)
        {
            bool flip = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), facing) < 0f;
            triangles.Add(a);
            triangles.Add(flip ? c : b);
            triangles.Add(flip ? b : c);
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<Vector2> uv, List<int> triangles, bool flat = false)
        {
            if (flat)
            {
                var facetedVertices = new List<Vector3>(triangles.Count);
                var facetedUv = new List<Vector2>(triangles.Count);
                for (int i = 0; i < triangles.Count; i++)
                {
                    int source = triangles[i];
                    facetedVertices.Add(vertices[source]);
                    facetedUv.Add(uv[source]);
                    triangles[i] = i;
                }
                vertices = facetedVertices;
                uv = facetedUv;
            }
            var mesh = new Mesh { name = name };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
