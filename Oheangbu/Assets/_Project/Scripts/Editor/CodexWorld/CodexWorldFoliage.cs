using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// Untextured pine foliage for SPEC-DEV-CODEX-WORLD. Small needle fans occupy real
    /// volume, leaving holes between brush-like tufts instead of forming rock-shaped plates.
    /// Render with a double-sided material. The caller owns and saves the returned mesh.
    /// </summary>
    public static class CodexWorldFoliage
    {
        public static Mesh Canopy(Vector3[] centres, Vector3[] radii, int seed)
        {
            if (centres == null) throw new ArgumentNullException(nameof(centres));
            if (radii == null || radii.Length != centres.Length)
                throw new ArgumentException("One ellipsoid radius is required per foliage cloud.", nameof(radii));

            var random = new System.Random(seed);
            var data = new NeedleMesh();
            // About 14,450 triangles for the intended 28-cloud tree. Repeated trees can
            // share this one mesh; more clouds trade tuft density for the same budget.
            int tufts = centres.Length == 0 ? 0 : Mathf.Clamp((15000 / centres.Length - 96) / 15, 3, 28);

            for (int cloud = 0; cloud < centres.Length; cloud++)
            {
                Vector3 center = centres[cloud];
                Vector3 radius = radii[cloud];
                RequireFinite(center, nameof(centres));
                RequireFinite(radius, nameof(radii));
                if (radius.x <= 0f || radius.y <= 0f || radius.z <= 0f)
                    throw new ArgumentOutOfRangeException(nameof(radii), "Foliage radii must be positive.");

                float phase = Next(random, 0f, Mathf.PI * 2f);
                float needleScale = Mathf.Clamp(Mathf.Sqrt(radius.x * radius.z) / 1.55f, 0.58f, 1.48f);
                var stems = new Vector3[7];
                for (int stem = 0; stem < stems.Length; stem++)
                {
                    float angle = phase + stem * 2.399963f;
                    float extent = Next(random, 0.39f, 0.83f);
                    stems[stem] = center + new Vector3(Mathf.Cos(angle) * radius.x * extent,
                        radius.y * Next(random, -0.52f, 0.42f), Mathf.Sin(angle) * radius.z * extent);
                    Vector3 root = center + new Vector3(0f, -radius.y * 0.46f, 0f);
                    data.Stem(root, stems[stem], 0.014f * needleScale);
                }

                // Small, separate inner knots hold the crown together. There is no continuous
                // ellipse or horizontal skin: their faceted cores stay inside the needles.
                for (int core = 0; core < 5; core++)
                {
                    float angle = phase + core * 2.399963f;
                    float extent = core == 0 ? 0f : 0.36f;
                    Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius.x * extent,
                        radius.y * Next(random, -0.35f, 0.20f), Mathf.Sin(angle) * radius.z * extent);
                    data.Knot(point, new Vector3(0.28f, 0.14f, 0.22f) * needleScale,
                        Quaternion.Euler(Next(random, -16f, 16f), Next(random, 0f, 360f), Next(random, -12f, 12f)));
                }

                for (int tuft = 0; tuft < tufts; tuft++)
                {
                    // Golden-angle placement gives broad coverage with deterministic irregular
                    // breaks; tiny clusters remain legible from both below and the side.
                    float angle = phase + tuft * 2.399963f + Next(random, -0.22f, 0.22f);
                    float radial = Mathf.Sqrt((tuft + Next(random, 0.20f, 0.95f)) / tufts);
                    float scallop = 0.80f + 0.11f * Mathf.Sin(angle * 3f + phase) + Next(random, -0.05f, 0.08f);
                    float height = (1f - radial * radial) * 0.34f + Next(random, -0.46f, 0.52f);
                    Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius.x * radial * scallop,
                        radius.y * height, Mathf.Sin(angle) * radius.z * radial * scallop);
                    float tuftScale = needleScale * Next(random, 0.75f, 1.25f);
                    float fanPhase = angle + Next(random, -0.65f, 0.65f);
                    AddNeedleTuft(data, point, fanPhase, tuftScale, random);
                }
            }
            return data.Finish("Codex_AiryPineNeedles_" + seed);
        }

        private static void AddNeedleTuft(NeedleMesh data, Vector3 center, float phase, float scale, System.Random random)
        {
            // Three intersecting fan planes prevent a camera-facing card or flat umbrella.
            // Five unequal, tapered strokes per plane make the outer edge read as pine needles.
            for (int plane = 0; plane < 3; plane++)
            {
                float azimuth = phase + plane * Mathf.PI * (2f / 3f) + Next(random, -0.19f, 0.19f);
                Vector3 outward = new Vector3(Mathf.Cos(azimuth), Next(random, -0.04f, 0.23f), Mathf.Sin(azimuth)).normalized;
                Vector3 planeNormal = Vector3.Cross(outward, Vector3.up).normalized;
                Vector3 rise = Vector3.Cross(planeNormal, outward).normalized;
                Vector3 foot = center - outward * (0.10f * scale) - Vector3.up * (0.035f * scale);
                for (int needle = 0; needle < 5; needle++)
                {
                    float spread = Mathf.Lerp(-0.46f, 1.16f, needle / 4f) + Next(random, -0.11f, 0.11f);
                    Vector3 direction = (outward * Mathf.Cos(spread) + rise * Mathf.Sin(spread)).normalized;
                    Vector3 across = Vector3.Cross(planeNormal, direction).normalized;
                    float length = Next(random, 0.26f, 0.50f) * scale;
                    float halfWidth = Next(random, 0.025f, 0.048f) * scale;
                    // A broader middle stroke supplies local opacity; tips remain very fine.
                    if (needle == 2) halfWidth *= 1.50f;
                    Vector3 basePoint = foot + outward * Next(random, -0.025f, 0.075f) * scale;
                    Vector3 tip = basePoint + direction * length + planeNormal * Next(random, -0.05f, 0.05f) * scale;
                    data.Triangle(basePoint - across * halfWidth, basePoint + across * halfWidth, tip);
                }
            }
        }

        private static void RequireFinite(Vector3 value, string name)
        {
            if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
                float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z))
                throw new ArgumentException("Foliage inputs must be finite.", name);
        }

        private static float Next(System.Random random, float min, float max)
        {
            return min + (max - min) * (float)random.NextDouble();
        }

        private sealed class NeedleMesh
        {
            private readonly List<Vector3> _vertices = new List<Vector3>(45000);
            private readonly List<int> _triangles = new List<int>(45000);
            private readonly List<Vector2> _uv = new List<Vector2>(45000);

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int index = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _uv.Add(Vector2.zero); _uv.Add(Vector2.right); _uv.Add(Vector2.up);
                _triangles.Add(index); _triangles.Add(index + 1); _triangles.Add(index + 2);
            }

            public void Stem(Vector3 a, Vector3 b, float radius)
            {
                Vector3 tangent = (b - a).normalized;
                Vector3 up = Vector3.Cross(tangent, Vector3.up);
                if (up.sqrMagnitude < 0.0001f) up = Vector3.Cross(tangent, Vector3.right);
                up.Normalize();
                Vector3 side = Vector3.Cross(tangent, up).normalized;
                var first = new Vector3[3];
                var last = new Vector3[3];
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * Mathf.PI * 2f / 3f;
                    Vector3 normal = up * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                    first[i] = a + normal * radius;
                    last[i] = b + normal * radius * 0.23f;
                }
                for (int i = 0; i < 3; i++)
                {
                    int next = (i + 1) % 3;
                    Triangle(first[i], first[next], last[next]);
                    Triangle(first[i], last[next], last[i]);
                }
                Triangle(first[2], first[1], first[0]);
                Triangle(last[0], last[1], last[2]);
            }

            public void Knot(Vector3 center, Vector3 radius, Quaternion rotation)
            {
                Vector3 top = center + rotation * new Vector3(0f, radius.y, 0f);
                Vector3 bottom = center + rotation * new Vector3(0f, -radius.y * 0.62f, 0f);
                var edge = new Vector3[4];
                edge[0] = center + rotation * new Vector3(radius.x, 0f, 0f);
                edge[1] = center + rotation * new Vector3(0f, 0f, radius.z);
                edge[2] = center + rotation * new Vector3(-radius.x * 0.86f, 0f, 0f);
                edge[3] = center + rotation * new Vector3(0f, 0f, -radius.z * 0.87f);
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    Triangle(top, edge[next], edge[i]);
                    Triangle(bottom, edge[i], edge[next]);
                }
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetUVs(0, _uv);
                var colors = new List<Color>(_vertices.Count);
                for (int i = 0; i < _vertices.Count; i++) colors.Add(Color.white);
                mesh.SetColors(colors);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
