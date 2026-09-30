using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class Roadside303
    {
        // Small authoring mesh builder: boxes and cylinders with per-metre UVs, merged into one mesh per prop group.
        internal sealed class MeshBuilder303
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> t = new List<int>();
            public bool Empty => v.Count == 0;

            static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            static readonly Vector3[] FaceU = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };

            // m: placement (rotation + translation, no scale); size: box extents in metres
            public void Box(Matrix4x4 m, Vector3 size)
            {
                for (int f = 0; f < 6; f++)
                {
                    Vector3 nn = FaceN[f], uu = FaceU[f], vv = Vector3.Cross(nn, uu);
                    Vector3 c = Vector3.Scale(nn, size) * .5f;
                    Vector3 hu = Vector3.Scale(uu, size) * .5f, hv = Vector3.Scale(vv, size) * .5f;
                    float su = Mathf.Abs(Vector3.Dot(uu, size)), sv = Mathf.Abs(Vector3.Dot(vv, size));
                    int b = v.Count;
                    Vector3[] corners = { c - hu - hv, c + hu - hv, c + hu + hv, c - hu + hv };
                    Vector2[] uvs = { new Vector2(0, 0), new Vector2(su, 0), new Vector2(su, sv), new Vector2(0, sv) };
                    for (int k = 0; k < 4; k++) { v.Add(m.MultiplyPoint3x4(corners[k])); n.Add(m.MultiplyVector(nn).normalized); uv.Add(uvs[k]); }
                    t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);   // clockwise seen from outside
                }
            }

            // cylinder along local Y, centred on m's origin
            public void Cylinder(Matrix4x4 m, float radius, float height, int segments)
            {
                float h = height * .5f; int b = v.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(m.MultiplyPoint3x4(dir * radius + Vector3.up * -h)); n.Add(m.MultiplyVector(dir).normalized); uv.Add(new Vector2(a * radius, 0));
                    v.Add(m.MultiplyPoint3x4(dir * radius + Vector3.up * h)); n.Add(m.MultiplyVector(dir).normalized); uv.Add(new Vector2(a * radius, height));
                }
                for (int i = 0; i < segments; i++) { int k = b + i * 2; t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k + 1); t.Add(k + 3); t.Add(k + 2); }
                foreach (float s in new[] { -1f, 1f })
                {
                    int centre = v.Count; v.Add(m.MultiplyPoint3x4(Vector3.up * h * s)); n.Add(m.MultiplyVector(Vector3.up * s).normalized); uv.Add(Vector2.zero);
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = i * Mathf.PI * 2f / segments; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        v.Add(m.MultiplyPoint3x4(dir * radius + Vector3.up * h * s)); n.Add(m.MultiplyVector(Vector3.up * s).normalized); uv.Add(new Vector2(dir.x, dir.z) * radius);
                    }
                    for (int i = 0; i < segments; i++) { if (s > 0) { t.Add(centre); t.Add(centre + i + 2); t.Add(centre + i + 1); } else { t.Add(centre); t.Add(centre + i + 1); t.Add(centre + i + 2); } }
                }
            }

            // a beam of square section w from a to b
            public void Beam(Vector3 a, Vector3 b, float w, float roll = 0)
            {
                var d = b - a; if (d.sqrMagnitude < 1e-6f) return;
                var r = Quaternion.FromToRotation(Vector3.up, d.normalized) * Quaternion.Euler(0, roll, 0);
                Box(Matrix4x4.TRS((a + b) * .5f, r, Vector3.one), new Vector3(w, d.magnitude, w));
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
