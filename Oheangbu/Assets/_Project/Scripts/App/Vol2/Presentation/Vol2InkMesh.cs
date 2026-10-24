using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>How one stroke is laid out: camera-facing around its own axis (a line in the air), or flat on the ground.</summary>
    public enum Vol2StrokeLayout { Billboard = 0, Flat = 1 }

    /// <summary>What a stroke does with the instance uniforms (the shader's role code).</summary>
    public enum Vol2StrokeRole { Normal = 0, FollowHead = 1, RigidOffset = 2, Drift = 3 }

    /// <summary>Everything the shader needs to know about ONE stroke, as numbers. Times are seconds after the instance started (the scope clock, converted by the recipe);
    /// the shader reads them against the instance's _Age, so the look is a pure function of the executor's timestamps and nothing here runs a timer.</summary>
    public struct Vol2StrokeAttr
    {
        /// <summary>Full width (metres).</summary>
        public float Width;
        public float Appear;
        /// <summary>Metres per second the stroke is written (the pen speed). 1e5 = at once. Ignored for FollowHead (the head decides).</summary>
        public float Speed;
        /// <summary>Seconds (after the instance started) the stroke's end begins; >= 1e5 = it has no end yet.</summary>
        public float End;
        public float EndSeconds;
        public Vol2EndCode EndCode;
        /// <summary>Expiry warning window before <see cref="End"/> (0 = none): wear on the stroke end.</summary>
        public float Warn;
        public float Seed;
        /// <summary>0 solid .. 1 fully hollow (my strokes: a dry rim and a hollow bundle, plan E).</summary>
        public float Hollow;
        public float Density;
        public Vol2StrokeLayout Layout;
        public Vol2StrokeRole Role;
        public bool TaperTip, TaperBase;
        /// <summary>A fleck of burnt ink: drawn neutral grey whatever the body hue (fire's ash).</summary>
        public bool Ash;
        /// <summary>Drift velocity (m/s) over <see cref="DriftSeconds"/>, eased out (linear drag): the flecks.</summary>
        public Vector3 Drift;
        public float DriftSeconds;

        public static Vol2StrokeAttr Default => new Vol2StrokeAttr { Width = 0.12f, Speed = 1e5f, End = 1e6f, EndSeconds = 0.3f, EndCode = Vol2EndCode.Expire, Hollow = 1f, Density = 1f };
        public const float Forever = 1e6f;
    }

    /// <summary>Collects strokes and writes ONE mesh (one draw call per instance). Per vertex: position = a point of the stroke's centre line (world), normal = the line's direction there,
    /// tangent = fleck drift, uv0 = (arc length, across -1..1, total length, half width), uv1 = (appear, speed, end, end seconds), uv2 = (end mode, warn seconds, seed, hollow),
    /// uv3 = (layout, density, role, flags). The shader (Oheangbu/SpellVFX308/Vol2Ink) widens the line to a ribbon facing the camera or lying on the ground.</summary>
    public sealed class Vol2InkWriter
    {
        readonly List<Vector3> _pos = new List<Vector3>(512), _nrm = new List<Vector3>(512);
        readonly List<Vector4> _tan = new List<Vector4>(512), _uv0 = new List<Vector4>(512), _uv1 = new List<Vector4>(512), _uv2 = new List<Vector4>(512), _uv3 = new List<Vector4>(512);
        readonly List<int> _idx = new List<int>(1536);
        readonly List<float> _arc = new List<float>(64);
        public int Strokes { get; private set; }
        /// <summary>A fixture sets this to read back what was written (the attributes of every stroke).</summary>
        public List<Vol2StrokeAttr> Record;
        public int Vertices => _pos.Count;
        public int Triangles => _idx.Count / 3;

        public void Clear()
        {
            _pos.Clear(); _nrm.Clear(); _tan.Clear(); _uv0.Clear(); _uv1.Clear(); _uv2.Clear(); _uv3.Clear(); _idx.Clear(); Strokes = 0;
        }

        /// <summary>Adds a stroke along <paramref name="pts"/> (at least 2 points; points closer than a millimetre are skipped).</summary>
        public void Stroke(IList<Vector3> pts, in Vol2StrokeAttr a)
        {
            int n = pts.Count;
            if (n < 2) return;
            _arc.Clear();
            float total = 0f; _arc.Add(0f);
            for (int i = 1; i < n; i++) { total += Vector3.Distance(pts[i - 1], pts[i]); _arc.Add(total); }
            if (total < 0.001f) return;
            Record?.Add(a);
            int baseIndex = _pos.Count;
            float flags = (a.TaperTip ? 1f : 0f) + (a.TaperBase ? 2f : 0f) + (a.Ash ? 4f : 0f);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1];
                if (d.sqrMagnitude < 1e-10f) d = Vector3.forward;
                d.Normalize();
                for (int side = 0; side < 2; side++)
                {
                    _pos.Add(pts[i]); _nrm.Add(d);
                    _tan.Add(new Vector4(a.Drift.x, a.Drift.y, a.Drift.z, a.DriftSeconds));
                    _uv0.Add(new Vector4(_arc[i], side == 0 ? -1f : 1f, total, a.Width * 0.5f));
                    _uv1.Add(new Vector4(a.Appear, a.Speed, a.End, Mathf.Max(0.001f, a.EndSeconds)));
                    _uv2.Add(new Vector4((float)(int)a.EndCode, a.Warn, a.Seed, a.Hollow));
                    _uv3.Add(new Vector4((float)(int)a.Layout, a.Density, (float)(int)a.Role, flags));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                int v = baseIndex + i * 2;
                _idx.Add(v); _idx.Add(v + 2); _idx.Add(v + 1);
                _idx.Add(v + 1); _idx.Add(v + 2); _idx.Add(v + 3);
            }
            Strokes++;
        }

        /// <summary>Writes the collected strokes into <paramref name="mesh"/> (cleared first). Bounds are a big box around <paramref name="centre"/>: the shader moves vertices, so the CPU bounds
        /// of the points would cull a stroke that drifts or follows an offset.</summary>
        public void Apply(Mesh mesh, Vector3 centre, float radius)
        {
            mesh.Clear();
            if (_pos.Count == 0) return;
            mesh.indexFormat = _pos.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_pos); mesh.SetNormals(_nrm); mesh.SetTangents(_tan);
            mesh.SetUVs(0, _uv0); mesh.SetUVs(1, _uv1); mesh.SetUVs(2, _uv2); mesh.SetUVs(3, _uv3);
            mesh.SetTriangles(_idx, 0, false);
            mesh.bounds = new Bounds(centre, Vector3.one * (radius * 2f));
        }
    }

    /// <summary>Small curve helpers of the recipes. Pure; no Unity objects.</summary>
    public static class Vol2Curves
    {
        public static void Line(List<Vector3> into, Vector3 from, Vector3 to, int segments)
        {
            into.Clear();
            for (int i = 0; i <= segments; i++) into.Add(Vector3.Lerp(from, to, i / (float)segments));
        }

        /// <summary>A rising stroke from <paramref name="root"/>: straight up with a gentle lean/bend (bend = sideways offset of the top, metres).</summary>
        public static void Rise(List<Vector3> into, Vector3 root, Vector3 lean, float height, int segments)
        {
            into.Clear();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                into.Add(root + Vector3.up * (height * t) + lean * (t * t));
            }
        }

        /// <summary>Bends the points from index <paramref name="from"/> on by <paramref name="degrees"/> in total, progressively, around <paramref name="axis"/> (a hook / curl at a stroke end).</summary>
        public static void Curl(List<Vector3> pts, int from, float degrees, Vector3 axis)
        {
            int n = pts.Count;
            if (from < 1 || from >= n) return;
            int steps = n - from;
            Vector3 pivot = pts[from - 1];
            Vector3 dir = (pts[from] - pivot);
            float seg = dir.magnitude; if (seg < 1e-5f) return;
            dir /= seg;
            Vector3 cur = pivot;
            for (int i = 0; i < steps; i++)
            {
                float turn = degrees * ((i + 1f) / steps) / steps;
                dir = Quaternion.AngleAxis(turn, axis) * dir;
                cur += dir * seg;
                pts[from + i] = cur;
            }
        }

        public static void Circle(List<Vector3> into, Vector3 centre, Vector3 u, Vector3 v, float radius, int segments)
        {
            into.Clear();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                into.Add(centre + u * (Mathf.Cos(a) * radius) + v * (Mathf.Sin(a) * radius));
            }
        }

        /// <summary>A deterministic hash in 0..1 (no System.Random, so a rebuild reproduces the same strokes).</summary>
        public static float Hash(int a, int b = 0)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
