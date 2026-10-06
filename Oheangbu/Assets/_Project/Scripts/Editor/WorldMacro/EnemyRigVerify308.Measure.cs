using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 SPEC-ENEMY-RIG-VERIFY-308 M1-M10. Same definitions as Tools/Blender/EnemyRig308/rig308.py so the two sides can be compared;
    // side 0 = the monster's own left, 1 = its own right. The fixture host sits at the origin, unrotated: world = actor frame
    // (x right, y up, z forward).
    public static partial class EnemyRigVerify308
    {
        static readonly string[] SideName = { "Left", "Right" };

        sealed class Geo
        {
            public Vector3[] bind; public int[] tris; public BoneWeight[] bw; public float[] area0; public Transform[] bones;
            public readonly Transform[] shoulder = new Transform[2], upper = new Transform[2], lower = new Transform[2], hand = new Transform[2], foot = new Transform[2];
            public Transform hips, chest;
            public readonly int[][] test = new int[2][], handVerts = new int[2][], footVerts = new int[2][], armTris = new int[2][], elbowRing = new int[2][], shoulderRing = new int[2][];
            public readonly bool[][] testUpper = new bool[2][];
            public int[] bodyVerts, bodyTris;
            public readonly Vector3[] bendAxisLocal = new Vector3[2], upperAxisLocal = new Vector3[2], lowerAxisLocal = new Vector3[2];
            public readonly Quaternion[] upperLocalBind = new Quaternion[2], lowerLocalBind = new Quaternion[2];
            public readonly float[] elbowArea0 = new float[2], shoulderArea0 = new float[2];
        }

        static Geo BuildGeo(Fixture f)
        {
            var g = new Geo(); var mesh = f.skin.sharedMesh; g.bind = mesh.vertices; g.tris = mesh.triangles; g.bw = mesh.boneWeights; g.bones = f.skin.bones;
            if (g.bw.Length != g.bind.Length) throw new InvalidOperationException("skin has no bone weights");
            var a = f.animator;
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b) ?? throw new InvalidOperationException("humanoid bone missing: " + b);
            int Idx(Transform t) { int i = Array.IndexOf(g.bones, t); if (i < 0) throw new InvalidOperationException("bone not in the skin: " + t.name); return i; }
            var bindMat = mesh.bindposes.Select(m => m.inverse).ToArray();   // bone -> mesh space at bind
            Vector3 Pos(Transform t) => bindMat[Idx(t)].GetColumn(3);
            Quaternion Rot(Transform t) => bindMat[Idx(t)].rotation;
            g.hips = B(HumanBodyBones.Hips); g.chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            var chestBones = new[] { a.GetBoneTransform(HumanBodyBones.UpperChest), a.GetBoneTransform(HumanBodyBones.Chest) }.Where(t => t != null && Array.IndexOf(g.bones, t) >= 0).Select(t => Array.IndexOf(g.bones, t)).ToArray();
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                g.shoulder[s] = a.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
                g.upper[s] = B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm); g.lower[s] = B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                g.hand[s] = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); g.foot[s] = B(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            }
            int n = g.bind.Length;
            float W(int v, int b) { var w = g.bw[v]; return (w.boneIndex0 == b ? w.weight0 : 0) + (w.boneIndex1 == b ? w.weight1 : 0) + (w.boneIndex2 == b ? w.weight2 : 0) + (w.boneIndex3 == b ? w.weight3 : 0); }
            var chain = new float[2][]; var chain4 = new float[2][]; var armAll = new float[n];
            for (int s = 0; s < 2; s++)
            {
                int iu = Idx(g.upper[s]), il = Idx(g.lower[s]), ih = Idx(g.hand[s]), ish = g.shoulder[s] != null ? Array.IndexOf(g.bones, g.shoulder[s]) : -1;
                var toes = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes); int ifoot = Idx(g.foot[s]), itoe = toes != null ? Array.IndexOf(g.bones, toes) : -1;
                chain[s] = new float[n]; chain4[s] = new float[n];
                Vector3 u0 = Pos(g.upper[s]), e0 = Pos(g.lower[s]), h0 = Pos(g.hand[s]); Vector3 ua = (e0 - u0).normalized, fa = (h0 - e0).normalized; float upperLength = (e0 - u0).magnitude;
                var test = new List<int>(); var isUpper = new List<bool>(); var hands = new List<int>(); var feet = new List<int>(); var elbow = new List<int>(); var shoulderRing = new List<int>();
                Vector3 elbowNormal = (ua + fa).normalized, shoulderCentre = u0 + ua * .06f;
                for (int v = 0; v < n; v++)
                {
                    float wu = W(v, iu), wl = W(v, il), wh = W(v, ih), ws = ish >= 0 ? W(v, ish) : 0;
                    chain[s][v] = wu + wl + wh; chain4[s][v] = wu + wl + wh + ws; armAll[v] += wu + wl + wh;
                    bool foreHand = wl + wh >= .9f;
                    bool upper = g.bw[v].boneIndex0 == iu && chain[s][v] >= .9f && Vector3.Dot(g.bind[v] - u0, ua) > .5f * upperLength;
                    if (foreHand || upper) { test.Add(v); isUpper.Add(!foreHand); }
                    if (wh >= .5f) hands.Add(v);
                    if (W(v, ifoot) + (itoe >= 0 ? W(v, itoe) : 0) >= .5f) feet.Add(v);
                    if (chain[s][v] > .6f && Ring(g.bind[v], e0, elbowNormal)) elbow.Add(v);
                    if (chain4[s][v] + chestBones.Sum(c => W(v, c)) > .6f && Ring(g.bind[v], shoulderCentre, ua)) shoulderRing.Add(v);
                }
                g.test[s] = test.ToArray(); g.testUpper[s] = isUpper.ToArray(); g.handVerts[s] = hands.ToArray(); g.footVerts[s] = feet.ToArray();
                g.elbowRing[s] = elbow.ToArray(); g.shoulderRing[s] = shoulderRing.ToArray();
                g.elbowArea0[s] = HullArea(g.bind, g.elbowRing[s], e0, elbowNormal); g.shoulderArea0[s] = HullArea(g.bind, g.shoulderRing[s], shoulderCentre, ua);
                var bend = Vector3.Cross(ua, fa); if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(ua, Vector3.forward);
                g.bendAxisLocal[s] = Quaternion.Inverse(Rot(g.upper[s])) * bend.normalized;
                g.upperAxisLocal[s] = Quaternion.Inverse(Rot(g.upper[s])) * ua; g.lowerAxisLocal[s] = Quaternion.Inverse(Rot(g.lower[s])) * fa;
                g.upperLocalBind[s] = g.shoulder[s] != null && Array.IndexOf(g.bones, g.shoulder[s]) >= 0 ? Quaternion.Inverse(Rot(g.shoulder[s])) * Rot(g.upper[s]) : Quaternion.identity;
                g.lowerLocalBind[s] = Quaternion.Inverse(Rot(g.upper[s])) * Rot(g.lower[s]);
                if (g.test[s].Length == 0 || g.footVerts[s].Length < 15) throw new InvalidOperationException("vertex sets empty on side " + SideName[s] + " (arm test " + g.test[s].Length + ", foot " + g.footVerts[s].Length + ")");
            }
            int triCount = g.tris.Length / 3; g.area0 = new float[triCount];
            var body = new List<int>(); var armTris = new[] { new List<int>(), new List<int>() };
            for (int t = 0; t < triCount; t++)
            {
                int i0 = g.tris[t * 3], i1 = g.tris[t * 3 + 1], i2 = g.tris[t * 3 + 2];
                g.area0[t] = Vector3.Cross(g.bind[i1] - g.bind[i0], g.bind[i2] - g.bind[i0]).magnitude * .5f;
                if (armAll[i0] < .05f && armAll[i1] < .05f && armAll[i2] < .05f) { body.Add(i0); body.Add(i1); body.Add(i2); }
                for (int s = 0; s < 2; s++) if (chain4[s][i0] > .5f || chain4[s][i1] > .5f || chain4[s][i2] > .5f) armTris[s].Add(t);
            }
            for (int s = 0; s < 2; s++) g.armTris[s] = armTris[s].ToArray();
            var map = new Dictionary<int, int>(); var verts = new List<int>(); g.bodyTris = new int[body.Count];
            for (int i = 0; i < body.Count; i++) { if (!map.TryGetValue(body[i], out int k)) { k = verts.Count; map[body[i]] = k; verts.Add(body[i]); } g.bodyTris[i] = k; }
            g.bodyVerts = verts.ToArray();
            if (g.bodyTris.Length < 300) throw new InvalidOperationException("body mesh too small for the penetration test (" + g.bodyTris.Length / 3 + " triangles)");
            return g;
        }

        static bool Ring(Vector3 p, Vector3 centre, Vector3 normal)
        {
            Vector3 rel = p - centre; float s = Vector3.Dot(rel, normal);
            return Mathf.Abs(s) < .025f && (rel - normal * s).magnitude < .30f;
        }

        // Area of the convex hull of a vertex ring projected on the plane through `centre` (cross-section of a limb).
        static float HullArea(Vector3[] points, int[] ring, Vector3 centre, Vector3 normal)
        {
            if (ring == null || ring.Length < 4) return 0;
            normal.Normalize(); Vector3 axis = Mathf.Abs(normal.x) < .9f ? Vector3.right : Vector3.up; Vector3 u = Vector3.Cross(normal, axis).normalized, v = Vector3.Cross(normal, u);
            var pts = ring.Select(i => { var rel = points[i] - centre; return new Vector2(Vector3.Dot(rel, u), Vector3.Dot(rel, v)); }).OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var lo = new List<Vector2>(); foreach (var p in pts) { while (lo.Count >= 2 && Cross(lo[lo.Count - 2], lo[lo.Count - 1], p) <= 0) lo.RemoveAt(lo.Count - 1); lo.Add(p); }
            var up = new List<Vector2>(); for (int i = pts.Count - 1; i >= 0; i--) { var p = pts[i]; while (up.Count >= 2 && Cross(up[up.Count - 2], up[up.Count - 1], p) <= 0) up.RemoveAt(up.Count - 1); up.Add(p); }
            lo.RemoveAt(lo.Count - 1); up.RemoveAt(up.Count - 1); lo.AddRange(up);
            float sum = 0; for (int i = 0; i < lo.Count; i++) { var p = lo[i]; var q = lo[(i + 1) % lo.Count]; sum += p.x * q.y - q.x * p.y; }
            return Mathf.Abs(sum) * .5f;
        }

        // rev 2 (SPEC 개정 2, 5-a): arm-in-body test for OPEN, multi-part meshes. The ray-parity test it replaces needs one closed shell;
        // these bodies have 940 - 1713 open edges and 21 - 128 loose parts, and its self-check failed for two of the three monsters.
        // Two votes per arm vertex (review F5: the winding number alone misreads a few percent of the points right at the surface):
        //   W        generalised winding number of the point against the body triangles > pen.windingInside (no ray)
        //   N        the nearest body triangle within pen.maxDepth is seen from behind
        //   W and N  = penetration, depth = distance to that triangle (the Blender side's measure)
        //   W only   = UNDETERMINED (inside by the winding, the nearest face seen from its front: between two layers)
        //   N only   = UNDETERMINED, but only within pen.behindOnlyMaxCm of that face and inside a cone of pen.behindConeDeg around its
        //              inward normal: without the two bounds a point 20 cm beyond the RIM of an open sheet (arm hole, skirt hem, hair
        //              card) counts as "behind" it - hundreds of such votes per monster offline
        //   neither  = outside; clearance = the distance to the nearest body triangle
        // An undetermined vertex is kept with its depth and is never read as "no penetration": a picture decides.
        // Far triangles are summed per grid cell as one dipole (area-normal at the area-weighted centre), near cells exactly. Same
        // arithmetic as Tools/Blender/EnemyAvatar308/pen308.py, which checks it against the exact sum offline. No collider, no physics
        // setting, nothing in any scene: the class keeps its old name and members so the run-clip tool compiles against either version.
        sealed class PenWorld : IDisposable
        {
            readonly Geo geo; readonly PenSettings set; readonly int triCount; readonly int[] tri;   // body triangles as indices into the skin's vertices
            readonly Vector3[] a, b, c, areaNormal;
            int[] cellStart = Array.Empty<int>(), cellTris = Array.Empty<int>(); Vector3[] cellCentre = Array.Empty<Vector3>(), cellNormal = Array.Empty<Vector3>(); float[] cellRadius = Array.Empty<float>();
            float sign = 1f;
            public bool Trusted { get; private set; }
            public string SelfTest { get; private set; } = "";
            public int Triangles => triCount;
            // near-surface self-test (review F5), as numbers: samples, then % of the points behind (penetration / undetermined / missed)
            // and of the points in front (clear / undetermined / false). "missed" is the share this measure cannot see.
            readonly float[] near = new float[7];
            public JObject NearJson() => new JObject { ["samples"] = (int)near[0], ["offsetCm"] = set.nearOffsetCm, ["behindPenetrationPct"] = Math.Round(near[1], 1), ["behindUndeterminedPct"] = Math.Round(near[2], 1),
                ["behindMissedPct"] = Math.Round(near[3], 1), ["frontClearPct"] = Math.Round(near[4], 1), ["frontUndeterminedPct"] = Math.Round(near[5], 1), ["frontFalsePct"] = Math.Round(near[6], 1),
                ["missedLimitPct"] = set.nearMissedMaxPct, ["falseLimitPct"] = set.nearFalseMaxPct };
            public PenWorld(Fixture f, Geo g, PenSettings settings)
            {
                geo = g; set = settings;
                // body = every triangle none of whose vertices is bound to an arm (upper arm + forearm + hand, either side) by
                // pen.bodyArmWeightMax or more. The shoulder caps stay in: with the old 0.05 cut the chest joints of two monsters read
                // 0.36 - 0.48 (the arm holes were too wide), with 0.3 every trunk joint reads 0.77 or more (pen308.py, offline).
                var arm = new int[6];
                for (int s = 0; s < 2; s++) { arm[s * 3] = Array.IndexOf(g.bones, g.upper[s]); arm[s * 3 + 1] = Array.IndexOf(g.bones, g.lower[s]); arm[s * 3 + 2] = Array.IndexOf(g.bones, g.hand[s]); }
                float Bound(int v)
                {
                    var w = g.bw[v];
                    return (Array.IndexOf(arm, w.boneIndex0) >= 0 ? w.weight0 : 0) + (Array.IndexOf(arm, w.boneIndex1) >= 0 ? w.weight1 : 0) + (Array.IndexOf(arm, w.boneIndex2) >= 0 ? w.weight2 : 0) + (Array.IndexOf(arm, w.boneIndex3) >= 0 ? w.weight3 : 0);
                }
                var list = new List<int>(); float max = set.bodyArmWeightMax;
                for (int t = 0; t + 2 < g.tris.Length; t += 3)
                    if (Bound(g.tris[t]) < max && Bound(g.tris[t + 1]) < max && Bound(g.tris[t + 2]) < max) { list.Add(g.tris[t]); list.Add(g.tris[t + 1]); list.Add(g.tris[t + 2]); }
                tri = list.ToArray(); triCount = tri.Length / 3;
                if (triCount < 100) throw new InvalidOperationException("body surface too small for the penetration test (" + triCount + " triangles)");
                a = new Vector3[triCount]; b = new Vector3[triCount]; c = new Vector3[triCount]; areaNormal = new Vector3[triCount];
            }
            public void Update(Vector3[] world)
            {
                var centroid = new Vector3[triCount]; var area = new float[triCount]; Vector3 lo = Vector3.positiveInfinity;
                for (int t = 0; t < triCount; t++)
                {
                    a[t] = world[tri[t * 3]]; b[t] = world[tri[t * 3 + 1]]; c[t] = world[tri[t * 3 + 2]];
                    areaNormal[t] = Vector3.Cross(b[t] - a[t], c[t] - a[t]) * .5f; area[t] = areaNormal[t].magnitude; centroid[t] = (a[t] + b[t] + c[t]) / 3f;
                    lo = Vector3.Min(lo, Vector3.Min(a[t], Vector3.Min(b[t], c[t])));
                }
                float cell = Mathf.Max(.01f, set.cell); var key = new long[triCount]; var order = new int[triCount];
                for (int t = 0; t < triCount; t++)
                {
                    long x = (long)Mathf.Floor((centroid[t].x - lo.x) / cell), y = (long)Mathf.Floor((centroid[t].y - lo.y) / cell), z = (long)Mathf.Floor((centroid[t].z - lo.z) / cell);
                    key[t] = (x << 40) | (y << 20) | z; order[t] = t;
                }
                Array.Sort(key, order);
                var starts = new List<int>(); for (int i = 0; i < triCount; i++) if (i == 0 || key[i] != key[i - 1]) starts.Add(i);
                int cells = starts.Count; starts.Add(triCount); cellStart = starts.ToArray(); cellTris = order;
                cellCentre = new Vector3[cells]; cellNormal = new Vector3[cells]; cellRadius = new float[cells];
                for (int k = 0; k < cells; k++)
                {
                    Vector3 n = Vector3.zero, weighted = Vector3.zero, plain = Vector3.zero; float total = 0;
                    for (int i = cellStart[k]; i < cellStart[k + 1]; i++) { int t = cellTris[i]; n += areaNormal[t]; weighted += centroid[t] * area[t]; plain += centroid[t]; total += area[t]; }
                    Vector3 centre = total > 1e-12f ? weighted / total : plain / (cellStart[k + 1] - cellStart[k]); float radius = 0;
                    for (int i = cellStart[k]; i < cellStart[k + 1]; i++) { int t = cellTris[i]; radius = Mathf.Max(radius, Mathf.Max((a[t] - centre).magnitude, Mathf.Max((b[t] - centre).magnitude, (c[t] - centre).magnitude))); }
                    cellCentre[k] = centre; cellNormal[k] = n; cellRadius[k] = radius;
                }
            }
            public float Winding(Vector3 p, bool exact = false)
            {
                double sum = 0; float beta = set.beta;
                for (int k = 0; k < cellCentre.Length; k++)
                {
                    Vector3 r = cellCentre[k] - p; float d = r.magnitude;
                    if (!exact && d > beta * cellRadius[k]) { sum += Vector3.Dot(cellNormal[k], r) / ((double)d * d * d); continue; }
                    for (int i = cellStart[k]; i < cellStart[k + 1]; i++)
                    {
                        int t = cellTris[i]; Vector3 pa = a[t] - p, pb = b[t] - p, pc = c[t] - p; float la = pa.magnitude, lb = pb.magnitude, lc = pc.magnitude;
                        double det = Vector3.Dot(pa, Vector3.Cross(pb, pc)), den = (double)la * lb * lc + (double)Vector3.Dot(pa, pb) * lc + (double)Vector3.Dot(pb, pc) * la + (double)Vector3.Dot(pc, pa) * lb;
                        sum += 2.0 * Math.Atan2(det, den);
                    }
                }
                return sign * (float)(sum / (4.0 * Math.PI));
            }
            // closest point on a triangle (Ericson)
            static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 ab = b - a, ac = c - a, ap = p - a; float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0 && d2 <= 0) return a;
                Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0 && d4 <= d3) return b;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
                Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0 && d5 <= d6) return c;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
                float va = d3 * d6 - d5 * d4;
                if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                float denominator = 1f / (va + vb + vc);
                return a + ab * (vb * denominator) + ac * (vc * denominator);
            }
            // Distance to the nearest body triangle, searched no farther than `cap` (+infinity when nothing is that near). `cosine` = the
            // cosine between that triangle's outward normal and the direction from its closest point to p: -1 straight behind the face,
            // +1 straight in front, near 0 in its plane (beyond a rim). The cell whose bound is nearest is searched first, so `best` is
            // small before the pass over every cell.
            public float Nearest(Vector3 p, float cap, out float cosine)
            {
                float best = cap, side = 0; bool found = false; int first = -1; float firstBound = float.PositiveInfinity;
                for (int k = 0; k < cellCentre.Length; k++) { float bound = (cellCentre[k] - p).magnitude - cellRadius[k]; if (bound < firstBound) { firstBound = bound; first = k; } }
                void Scan(int k)
                {
                    if ((cellCentre[k] - p).magnitude - cellRadius[k] >= best) return;
                    for (int i = cellStart[k]; i < cellStart[k + 1]; i++)
                    {
                        int t = cellTris[i]; Vector3 offset = p - Closest(p, a[t], b[t], c[t]); float d = offset.magnitude;
                        if (d < best) { best = d; found = true; float area = areaNormal[t].magnitude; side = d > 1e-9f && area > 1e-12f ? sign * Vector3.Dot(offset, areaNormal[t]) / (d * area) : 0; }
                    }
                }
                if (first >= 0) Scan(first);
                for (int k = 0; k < cellCentre.Length; k++) if (k != first) Scan(k);
                cosine = side; return found ? best : float.PositiveInfinity;
            }
            public float Nearest(Vector3 p, float cap) => Nearest(p, cap, out _);
            public float Nearest(Vector3 p) => Nearest(p, set.maxDepth, out _);
            // the dipole sum is off by up to 0.02 (offline check): a point that close to the threshold is summed exactly before it is judged
            public bool Inside(Vector3 p)
            {
                float w = Winding(p); if (Mathf.Abs(w - set.windingInside) < set.exactBand) w = Winding(p, true);
                return w > set.windingInside;
            }
            // 0 = outside (or touching, or nothing within pen.maxDepth) | 1 = penetration (both votes) | 2 = undetermined (one vote)
            public int Classify(Vector3 p, out float depth)
            {
                bool inside = Inside(p); depth = Nearest(p, set.maxDepth, out float cosine);
                if (float.IsInfinity(depth) || depth < set.minDepth) return 0;
                if (inside) return cosine < 0 ? 1 : 2;
                return cosine <= -Mathf.Cos(set.behindConeDeg * Mathf.Deg2Rad) && depth <= set.behindOnlyMaxCm * .01f ? 2 : 0;
            }
            bool RayHitsAny(Vector3 origin, Vector3 direction)
            {
                for (int t = 0; t < triCount; t++)
                {
                    Vector3 e1 = b[t] - a[t], e2 = c[t] - a[t], h = Vector3.Cross(direction, e2); float det = Vector3.Dot(e1, h); if (Mathf.Abs(det) < 1e-14f) continue;
                    float f = 1f / det; Vector3 s = origin - a[t]; float u = f * Vector3.Dot(s, h); if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(s, e1); float v = f * Vector3.Dot(direction, q); if (v < 0 || u + v > 1) continue;
                    if (f * Vector3.Dot(e2, q) > 1e-5f) return true;
                }
                return false;
            }
            // review F5: the three self-test points are far from any surface. This one is at it: outermost body triangles of the height
            // band the hands pass (a ray along the normal meets no other body triangle), a point pen.nearOffsetCm behind each and one in
            // front. Both votes "outside" for a point behind = missed; both "inside" for a point in front = false. The shares are
            // printed in every measure; over the data limits the monster's M5 reads "not measured".
            string NearSurfaceTest()
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                for (int t = 0; t < triCount; t++) { lo = Mathf.Min(lo, Mathf.Min(a[t].y, Mathf.Min(b[t].y, c[t].y))); hi = Mathf.Max(hi, Mathf.Max(a[t].y, Mathf.Max(b[t].y, c[t].y))); }
                var candidates = new List<int>();
                for (int t = 0; t < triCount; t++)
                {
                    float rel = ((a[t].y + b[t].y + c[t].y) / 3f - lo) / Mathf.Max(1e-6f, hi - lo);
                    if (rel > set.nearBandLow && rel < set.nearBandHigh && areaNormal[t].magnitude > 1e-6f) candidates.Add(t);
                }
                int want = Mathf.Max(1, set.nearSamples), stride = Mathf.Max(1, candidates.Count / (3 * want)), n = 0; float off = set.nearOffsetCm * .01f;
                int behindPen = 0, behindUnsure = 0, behindMissed = 0, frontClear = 0, frontUnsure = 0, frontFalse = 0;
                for (int i = 0; i < candidates.Count && n < want; i += stride)
                {
                    int t = candidates[i]; Vector3 normal = areaNormal[t] * (sign / areaNormal[t].magnitude), centre = (a[t] + b[t] + c[t]) / 3f;   // not .normalized: it returns zero under 1e-5
                    if (RayHitsAny(centre + normal * 1e-4f, normal)) continue;
                    n++; int kb = Classify(centre - normal * off, out _), kf = Classify(centre + normal * off, out _);
                    if (kb == 1) behindPen++; else if (kb == 2) behindUnsure++; else behindMissed++;
                    if (kf == 0) frontClear++; else if (kf == 2) frontUnsure++; else frontFalse++;
                }
                Array.Clear(near, 0, near.Length);
                if (n == 0) { Trusted = false; return "near surface: no outermost triangle found in the hand band - OUTSIDE / INSIDE readings unchecked"; }
                float missed = 100f * behindMissed / n, wrong = 100f * frontFalse / n; if (missed > set.nearMissedMaxPct || wrong > set.nearFalseMaxPct) Trusted = false;
                near[0] = n; near[1] = 100f * behindPen / n; near[2] = 100f * behindUnsure / n; near[3] = missed; near[4] = 100f * frontClear / n; near[5] = 100f * frontUnsure / n; near[6] = wrong;
                return "near surface (" + n + " outermost triangles of the hand band, " + F(set.nearOffsetCm) + " cm off): points behind read penetration " + F1(100f * behindPen / n) + " % / undetermined " + F1(100f * behindUnsure / n) +
                    " % / missed " + F1(missed) + " % (limit " + F1(set.nearMissedMaxPct) + ")" + (missed > set.nearMissedMaxPct ? " = reads OUTSIDE too often" : "") +
                    ", points in front read clear " + F1(100f * frontClear / n) + " % / undetermined " + F1(100f * frontUnsure / n) + " % / penetration " + F1(wrong) + " % (limit " + F1(set.nearFalseMaxPct) + ")" +
                    (wrong > set.nearFalseMaxPct ? " = reads INSIDE too often" : "");
            }
            // Three known points: two joints deep in the trunk must read inside, a point a metre beside the hips outside. The mesh's
            // triangle order decides the sign of the raw sum: when both joints read clearly negative the sign is turned once, and said so.
            // Then the near-surface test. The capital words OUTSIDE / INSIDE appear only when a test failed (the run-clip tool reads them).
            public void Check(Vector3 insideA, Vector3 insideB, Vector3 outside)
            {
                sign = 1f; float wa = Winding(insideA), wb = Winding(insideB); bool turned = false;
                if (wa < -set.windingInside && wb < -set.windingInside) { sign = -1f; wa = -wa; wb = -wb; turned = true; }
                float wo = Winding(outside);
                Trusted = wa > set.windingInside && wb > set.windingInside && Mathf.Abs(wo) < set.windingOutsideMax;
                SelfTest = "winding: hips joint " + F(wa) + (wa > set.windingInside ? " inside" : " OUTSIDE") + ", chest joint " + F(wb) + (wb > set.windingInside ? " inside" : " OUTSIDE") +
                    ", " + F(set.besideMetres) + " m beside the hips " + F(wo) + (Mathf.Abs(wo) < set.windingOutsideMax ? " outside" : " INSIDE") + (turned ? " (triangle order: sign turned)" : "");
                if (Trusted) SelfTest += " | " + NearSurfaceTest();
            }
            // The run-clip tool's two-point call: the hips joint, and its far point replaced by the standard third point when a chest is known.
            public void Check(Vector3 inside, Vector3 outside) => Check(inside, geo.chest != null ? geo.chest.position : inside, inside + Vector3.right * set.besideMetres);
            public void Dispose() { }
        }

        sealed class Pose
        {
            public float t;
            public readonly float[] abduction = new float[2], swing = new float[2], elbow = new float[2], flex = new float[2], twistUpper = new float[2], twistFore = new float[2];
            public readonly float[] penFore = new float[2], penUpper = new float[2], clearHand = { float.PositiveInfinity, float.PositiveInfinity }, soleMin = new float[2];
            public readonly float[] unsureFore = new float[2], unsureUpper = new float[2];   // rev 2 (review F5): deepest vertex the two votes disagree on
            public readonly float[] sectionElbow = new float[2], sectionShoulder = new float[2];
            public readonly int[] triStretch = new int[2], triCollapse = new int[2];
            public readonly Vector3[] wrist = new Vector3[2]; public Vector3 chest;
            public Vector3[] verts; public readonly int[][] low = new int[2][]; public Quaternion[] local; public int outsideCull; public float lowestVertex;
            public float cullReach;   // rev 3: the farthest vertex of this sample by the M7 measure (metres), so that a radius can be read against it
        }

        static float Twist(Quaternion delta, Vector3 axis)
        {
            float deg = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis.normalized), delta.w) * Mathf.Rad2Deg;
            return Mathf.Repeat(deg + 180f, 360f) - 180f;
        }

        static Pose Capture(Fixture f, Geo g, PenWorld pen, Config cfg, float t, bool penetration, FolkloreCullingBounds298 culling)
        {
            var p = new Pose { t = t }; var V = CompactFolklore298.PhysicalSkinVertices298(f.skin); p.verts = V;
            p.local = g.bones.Select(b => b != null ? b.localRotation : Quaternion.identity).ToArray(); p.chest = g.chest.position;
            float lowest = float.PositiveInfinity; for (int i = 0; i < V.Length; i++) lowest = Mathf.Min(lowest, V[i].y); p.lowestVertex = lowest;
            if (penetration) pen.Update(V);
            for (int s = 0; s < 2; s++)
            {
                Vector3 U = g.upper[s].position, E = g.lower[s].position, H = g.hand[s].position; Vector3 ua = (E - U).normalized, fa = (H - E).normalized; float outward = s == 0 ? -1f : 1f;
                p.abduction[s] = Mathf.Atan2(ua.x * outward, -ua.y) * Mathf.Rad2Deg; p.swing[s] = Mathf.Atan2(ua.z, -ua.y) * Mathf.Rad2Deg;
                p.elbow[s] = Vector3.Angle(ua, fa);
                p.flex[s] = Mathf.Atan2(Vector3.Dot(Vector3.Cross(ua, fa), g.upper[s].rotation * g.bendAxisLocal[s]), Vector3.Dot(ua, fa)) * Mathf.Rad2Deg;
                if (g.shoulder[s] != null) p.twistUpper[s] = Twist(Quaternion.Inverse(g.upperLocalBind[s]) * (Quaternion.Inverse(g.shoulder[s].rotation) * g.upper[s].rotation), g.upperAxisLocal[s]);
                p.twistFore[s] = Twist(Quaternion.Inverse(g.lowerLocalBind[s]) * (Quaternion.Inverse(g.upper[s].rotation) * g.lower[s].rotation), g.lowerAxisLocal[s]);
                p.wrist[s] = H;
                foreach (int tri in g.armTris[s])
                {
                    if (g.area0[tri] <= 2e-5f) continue;
                    int i0 = g.tris[tri * 3], i1 = g.tris[tri * 3 + 1], i2 = g.tris[tri * 3 + 2];
                    float ratio = Vector3.Cross(V[i1] - V[i0], V[i2] - V[i0]).magnitude * .5f / g.area0[tri];
                    if (ratio > cfg.limits.triStretchRatio) p.triStretch[s]++; else if (ratio < cfg.limits.triCollapseRatio) p.triCollapse[s]++;
                }
                p.sectionElbow[s] = g.elbowArea0[s] > 0 ? HullArea(V, g.elbowRing[s], E, (ua + fa).normalized) / g.elbowArea0[s] : 1f;
                p.sectionShoulder[s] = g.shoulderArea0[s] > 0 ? HullArea(V, g.shoulderRing[s], U + ua * .06f, ua) / g.shoulderArea0[s] : 1f;
                var feet = g.footVerts[s]; var order = feet.OrderBy(i => V[i].y).Take(cfg.lowestSoleVertices).ToArray(); p.low[s] = order; p.soleMin[s] = V[order[0]].y;
                if (!penetration || !pen.Trusted) continue;
                for (int k = 0; k < g.test[s].Length; k++)
                {
                    int kind = pen.Classify(V[g.test[s][k]], out float depth); if (kind == 0) continue;
                    bool upper = g.testUpper[s][k];
                    if (kind == 1) { if (upper) p.penUpper[s] = Mathf.Max(p.penUpper[s], depth); else p.penFore[s] = Mathf.Max(p.penFore[s], depth); }
                    else { if (upper) p.unsureUpper[s] = Mathf.Max(p.unsureUpper[s], depth); else p.unsureFore[s] = Mathf.Max(p.unsureFore[s], depth); }
                }
                if (p.penFore[s] <= 0) foreach (int v in g.handVerts[s]) p.clearHand[s] = Mathf.Min(p.clearHand[s], pen.Nearest(V[v], cfg.pen.clearCap));
            }
            if (culling != null && culling.Entries != null)
                foreach (var entry in culling.Entries)
                {
                    if (entry == null || entry.Skin != f.skin) continue;
                    float radius = entry.LocalBounds.extents.x; var anchor = f.skin.rootBone != null ? f.skin.rootBone : f.skin.transform;
                    for (int i = 0; i < V.Length; i++)
                    {
                        float reach = Mathf.Max(anchor.InverseTransformPoint(V[i]).magnitude, f.skin.transform.InverseTransformPoint(V[i]).magnitude);
                        if (reach > p.cullReach) p.cullReach = reach;
                        if (reach > radius) p.outsideCull++;
                    }
                }
            return p;
        }

        sealed class Gait
        {
            public bool valid; public float speed, stride, seconds, soleCycleMin;
            public readonly int[] stanceFrames = new int[2]; public readonly float[] stancePct = new float[2], stanceSpeed = new float[2], soleMin = new float[2], soleMax = new float[2], soleMean = new float[2], slideInClip = new float[2];
            public readonly float[] slopeUp = { float.PositiveInfinity, float.NegativeInfinity }, slopeDown = { float.PositiveInfinity, float.NegativeInfinity };
            public float stanceSeconds;
        }

        // Stance from the speed plateau of the lowest sole vertices (the walk shuffles: height does not separate stance from swing),
        // one stance per cycle = the longest cyclic run. Same rule as rig308.stance_masks.
        static Gait MeasureGait(List<Pose> poses, Config cfg, float frameRate)
        {
            var gait = new Gait(); int n = poses.Count - 1; if (n < 8) return gait;
            float dt = 1f / frameRate; gait.seconds = n * dt; double sum = 0; int count = 0; int stanceTotal = 0; gait.soleCycleMin = float.PositiveInfinity; float tan = Mathf.Tan(cfg.limits.slopeDeg * Mathf.Deg2Rad);
            for (int s = 0; s < 2; s++)
            {
                var back = new float[n]; var height = new float[n]; var forward = new float[n];
                for (int k = 0; k < n; k++)
                {
                    Vector3 a = Vector3.zero, b = Vector3.zero; var low = poses[k].low[s];
                    foreach (int v in low) { a += poses[k].verts[v]; b += poses[k + 1].verts[v]; }
                    a /= low.Length; b /= low.Length; back[k] = -(b.z - a.z) / dt; height[k] = poses[k].soleMin[s]; forward[k] = a.z;
                    gait.soleCycleMin = Mathf.Min(gait.soleCycleMin, height[k]);
                }
                var sorted = back.OrderBy(x => x).ToArray(); float rank = .9f * (n - 1); int lo = Mathf.FloorToInt(rank); float top = Mathf.Lerp(sorted[lo], sorted[Mathf.Min(n - 1, lo + 1)], rank - lo);
                if (top <= 0) return gait;
                var high = back.Where(x => x > .5f * top).OrderBy(x => x).ToArray(); if (high.Length == 0) return gait;
                float plateau = high.Length % 2 == 1 ? high[high.Length / 2] : .5f * (high[high.Length / 2 - 1] + high[high.Length / 2]);
                var hit = back.Select(x => Mathf.Abs(x - plateau) < cfg.stancePlateauBand * Mathf.Abs(plateau)).ToArray(); var mask = new bool[n];
                if (hit.All(x => x)) for (int k = 0; k < n; k++) mask[k] = true;
                else
                {
                    int bestLength = 0, bestStart = 0;
                    for (int start = 0; start < n; start++)
                    {
                        if (!hit[start] || hit[(start + n - 1) % n]) continue;
                        int length = 0; while (hit[(start + length) % n]) length++;
                        if (length > bestLength) { bestLength = length; bestStart = start; }
                    }
                    for (int k = 0; k < bestLength; k++) mask[(bestStart + k) % n] = true;
                }
                int frames = mask.Count(x => x); if (frames == 0) return gait;
                float mean = 0, min = float.PositiveInfinity, max = float.NegativeInfinity, soleSum = 0;
                for (int k = 0; k < n; k++) if (mask[k]) { mean += back[k]; min = Mathf.Min(min, height[k]); max = Mathf.Max(max, height[k]); soleSum += height[k]; sum += back[k]; count++; }
                mean /= frames; float slide = 0; for (int k = 0; k < n; k++) if (mask[k]) slide += Mathf.Abs(back[k] - mean) * dt;
                // 10 degree slope without foot placement: the actor stays upright, the ground under a foot at forward offset z is z * tan
                for (int k = 0; k < n; k++) if (mask[k])
                {
                    float up = height[k] - forward[k] * tan, down = height[k] + forward[k] * tan;
                    gait.slopeUp[0] = Mathf.Min(gait.slopeUp[0], up); gait.slopeUp[1] = Mathf.Max(gait.slopeUp[1], up); gait.slopeDown[0] = Mathf.Min(gait.slopeDown[0], down); gait.slopeDown[1] = Mathf.Max(gait.slopeDown[1], down);
                }
                gait.stanceFrames[s] = frames; gait.stancePct[s] = 100f * frames / n; gait.stanceSpeed[s] = mean; gait.soleMin[s] = min; gait.soleMax[s] = max; gait.soleMean[s] = soleSum / frames; gait.slideInClip[s] = slide; stanceTotal += frames;
            }
            gait.speed = count > 0 ? (float)(sum / count) : 0; gait.stride = gait.speed * gait.seconds; gait.stanceSeconds = stanceTotal * .5f * dt; gait.valid = gait.speed > .05f;
            return gait;
        }

        sealed class SetResult
        {
            public string key = "", role = "", clip = "", relax = ""; public List<Pose> poses = new List<Pose>(); public Gait gait; public float frameRate, length; public bool penMeasured;
            public float Max(Func<Pose, float> f) => poses.Max(f); public float Min(Func<Pose, float> f) => poses.Min(f); public float Mean(Func<Pose, float> f) => poses.Average(f);
        }

        static SetResult RunSet(Fixture f, Geo g, PenWorld pen, Config cfg, FolkloreCullingBounds298 culling, string key, string role, AnimationClip clip, string relax, AnimationClip walkForDetection, EnemyArmRelaxProfile308 profile, bool penetration)
        {
            var set = new SetResult { key = key, role = role, clip = clip.name, relax = relax, frameRate = clip.frameRate, length = clip.length, penMeasured = penetration && pen.Trusted };
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
            // rev 2: on a long clip the (costly) winding test runs on every pen.longClipEvery-th sample; every other measure on all
            int penEvery = frames + 1 > cfg.pen.longClipSamples ? Mathf.Max(1, cfg.pen.longClipEvery) : 1;
            for (int i = 0; i <= frames; i++)
            {
                float t = Mathf.Min(i / clip.frameRate, clip.length);
                Sample(f, clip, t); Relax(f, relax, walkForDetection, profile, role == "idle");
                set.poses.Add(Capture(f, g, pen, cfg, t, penetration && i % penEvery == 0, culling));
            }
            if (role == "walk") set.gait = MeasureGait(set.poses, cfg, clip.frameRate);
            // only the walk needs vertices of neighbouring samples; drop the rest (12k vectors per sample)
            if (role != "walk") foreach (var p in set.poses) p.verts = null;
            return set;
        }

        sealed class ActorInfo { public string id = "", name = "", clipKind = "", relax = ""; public float speed, walkMetresPerSecond, deathVisualSeconds, scale; }

        static IEnumerable<(EnemyRigMotion298 rig, Monster monster, bool fixedClip)> Targets(Scene scene, Config cfg)
        {
            foreach (var rig in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnemyRigMotion298>(true)))
            {
                if (rig == null || rig.Animator == null || rig.Walk == null || ProtectedTree(rig.transform)) continue;
                string path = AssetDatabase.GetAssetPath(rig.Walk);
                foreach (var m in cfg.monsters)
                    if (path == m.walkSource) yield return (rig, m, false);
                    else if (!string.IsNullOrEmpty(m.walkFixed) && path == m.walkFixed) yield return (rig, m, true);
            }
        }

        static string RelaxLabel(EnemyRigMotion298 rig) =>
            rig.RelaxProfile != null ? "profile " + rig.RelaxProfile.name : rig.ArmRelax < 0 && rig.ElbowRelax < 0 ? "auto " + F(rig.ArmRelaxAmount) + "/" + F(rig.ElbowRelaxAmount) : "explicit " + F(rig.ArmRelaxAmount) + "/" + F(rig.ElbowRelaxAmount);

        static JArray Pair(Func<int, float> f, float scale = 1f) => new JArray(Math.Round(f(0) * scale, 3), Math.Round(f(1) * scale, 3));

        static string Measure(Config cfg, string only)
        {
            var monsters = Pick(cfg, only, out string why); if (monsters == null) return "REFUSED " + why;
            var manifest = CompactFolklore298.ReadManifest(); Directory.CreateDirectory(Out);
            var active = SceneManager.GetActiveScene(); bool ledgerScene = Ledger.Contains(active.path);
            var md = new StringBuilder("# 적 리그 측정 (편집기) — EnemyRigVerify308 measure\n\n");
            md.AppendLine("- 시각(UTC): " + DateTime.UtcNow.ToString("O") + " · 품질 단계: " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " · 정점당 본: " + QualitySettings.skinWeights);
            md.AppendLine("- 자세: 프리팹 사본(미리보기 씬) + PlayableGraph 표본 + `EnemyRigMotion298.ApplyArmRelax`(Play와 같은 함수). 씬 · 에셋 저장 0.");
            md.AppendLine("- 배우 값: " + (ledgerScene ? "열린 씬 `" + active.path + "`에서 읽음" : "열린 씬이 원장 씬이 아니라 읽지 않음 — 미끄러짐은 가정값 " + F(cfg.assumeWalkMetresPerSecond) + " 기준") + "\n");
            var lines = new List<string>();
            foreach (var m in monsters)
            {
                var row = manifest.rows.FirstOrDefault(r => r.id == m.id); if (row == null) { lines.Add(m.id + ": REFUSED no manifest row"); continue; }
                lines.Add(MeasureOne(cfg, m, row, ledgerScene ? active : default, md));
            }
            // rev 2: a measure taken under candidate A is kept beside the live one (measure_A.md / measure_<id>_A.json), never over it
            string state = string.Join("+", monsters.Select(x => AvatarState(cfg, x, out _)).Distinct()), tag = state == "live" ? "" : "_" + state.Replace('+', '-');
            File.WriteAllText(Path.Combine(Out, (only == "" ? "measure" : "measure_" + only.Replace('+', '_')) + tag + ".md"), md.ToString(), new UTF8Encoding(false));
            return "measure -> " + Out + "\n" + string.Join("\n", lines);
        }

        static string MeasureOne(Config cfg, Monster m, CompactFolklore298.ModelRow row, Scene actorScene, StringBuilder md)
        {
            var lim = cfg.limits;
            var origWalk = OnlyClip(m.walkSource) ?? throw new InvalidOperationException(m.id + ": walk source clip not found (or not exactly one take) in " + m.walkSource);
            var fixedWalk = FixedClip(m); var profile = Profile(m.relaxProfile); var profileB = Profile(m.relaxProfileB); var runClip = RunClip(m);
            string avatarNow = AvatarState(cfg, m, out string avatarNote), suffix = avatarNow == "live" ? "" : "_" + avatarNow;
            // review F3: every clip file keeps its own copy of the avatar's reference rows; a stale copy = that clip is converted with the other pose
            var staleRows = StaleClipRows(m, cfg.avatar.rowTolerance, out int rowFiles);
            var clips = new Dictionary<string, AnimationClip> { { "idle", CompactFolklore298.Clip(row, "idle") }, { "attack", CompactFolklore298.Clip(row, "attack") }, { "hit", CompactFolklore298.Clip(row, "hit") },
                { "stun", CompactFolklore298.Clip(row, "stun") }, { "death", ClipIn(m.motionSource, m.deathTake) ?? CompactFolklore298.Clip(row, "death") } };
            var sets = new List<SetResult>(); string selfTest; int testVerts, bodyTris; bool penTrusted; JObject nearSurface;
            using (var f = new Fixture(m))
            {
                Sample(f, clips["idle"], 0); var g = BuildGeo(f); var culling = f.visual.GetComponent<FolkloreCullingBounds298>();
                testVerts = g.test[0].Length + g.test[1].Length; bodyTris = g.bodyTris.Length / 3;
                using (var pen = new PenWorld(f, g, cfg.pen))
                {
                    pen.Update(CompactFolklore298.PhysicalSkinVertices298(f.skin)); pen.Check(g.hips.position, g.chest.position, g.hips.position + Vector3.right * cfg.pen.besideMetres); selfTest = pen.SelfTest; bodyTris = pen.Triangles; penTrusted = pen.Trusted; nearSurface = pen.NearJson();
                    sets.Add(RunSet(f, g, pen, cfg, culling, "idle/off", "idle", clips["idle"], "off", origWalk, null, true));
                    sets.Add(RunSet(f, g, pen, cfg, culling, "idle/legacy", "idle", clips["idle"], "legacy", origWalk, null, true));
                    if (profile != null) sets.Add(RunSet(f, g, pen, cfg, culling, "idle/profile", "idle", clips["idle"], "profile", origWalk, profile, true));
                    sets.Add(RunSet(f, g, pen, cfg, culling, "walk/off", "walk", origWalk, "off", origWalk, null, true));
                    sets.Add(RunSet(f, g, pen, cfg, culling, "walk/legacy", "walk", origWalk, "legacy", origWalk, null, true));
                    if (fixedWalk != null)
                    {
                        sets.Add(RunSet(f, g, pen, cfg, culling, "walkfix/off", "walk", fixedWalk, "off", fixedWalk, null, true));
                        if (profile != null) sets.Add(RunSet(f, g, pen, cfg, culling, "walkfix/profile", "walk", fixedWalk, "profile", fixedWalk, profile, true));
                        // rev 2 candidate B: the repaired walk with walk relax as data (its own profile asset)
                        if (profileB != null) { sets.Add(RunSet(f, g, pen, cfg, culling, "walkfix/B", "walk", fixedWalk, "B", fixedWalk, profileB, true)); sets.Add(RunSet(f, g, pen, cfg, culling, "idle/B", "idle", clips["idle"], "B", origWalk, profileB, true)); }
                    }
                    foreach (var role in new[] { "attack", "hit", "stun", "death" }) sets.Add(RunSet(f, g, pen, cfg, culling, role + "/off", role, clips[role], "off", origWalk, null, true));
                    // rev 2 (review F4): the run clip of the run-clip stage on the avatar that is imported now, relax off (RunArm / RunElbow are 0 / 0 there)
                    if (runClip != null) sets.Add(RunSet(f, g, pen, cfg, culling, "run/off", "run", runClip, "off", origWalk, null, true));
                }
            }
            // ---- actors of the open ledger scene (read only)
            var actors = new List<ActorInfo>();
            if (actorScene.IsValid())
                foreach (var (rig, monster, fixedClip) in Targets(actorScene, cfg).Where(x => x.monster.id == m.id))
                {
                    var enc = rig.GetComponent<PrologueEncounter>();
                    actors.Add(new ActorInfo { id = enc != null ? enc.Id : "", name = rig.name, clipKind = fixedClip ? "walk_fix308" : "walking_man", relax = RelaxLabel(rig), speed = enc != null ? enc.Speed : 0,
                        walkMetresPerSecond = rig.WalkMetresPerSecond, deathVisualSeconds = enc != null ? enc.DeathVisualSeconds : 0, scale = rig.Animator.transform.lossyScale.y });
                }
            // ---- report
            SetResult Get(string key) => sets.FirstOrDefault(s => s.key == key);
            var json = new JObject { ["schema"] = "308.enemyrig.unity.1", ["id"] = m.id, ["utc"] = DateTime.UtcNow.ToString("O"), ["quality"] = QualitySettings.names[QualitySettings.GetQualityLevel()],
                ["skinWeights"] = QualitySettings.skinWeights.ToString(), ["penetrationSelfTest"] = selfTest, ["penetrationTrusted"] = penTrusted, ["penetrationNearSurface"] = nearSurface, ["armTestVertices"] = testVerts, ["bodyTriangles"] = bodyTris,
                ["avatar"] = avatarNow, ["avatarNote"] = avatarNote, ["clipRowFiles"] = rowFiles, ["clipRowsStale"] = new JArray(staleRows.ToArray()),
                ["runClip"] = runClip != null ? "measured (run/off)" : string.IsNullOrEmpty(m.runClip) ? "none" : "NOT IMPORTED" };
            if (ElbowFloor(RigAvatar(AvatarRows(cfg, m, out _)?.rig), out float floorLeft, out float floorRight)) json["avatarElbowFloorDeg"] = new JArray(Math.Round(floorLeft, 2), Math.Round(floorRight, 2));
            var jsets = new JObject(); var fails = new List<string>();
            void Limit(bool ok, string text) { if (!ok) fails.Add(text); }
            foreach (var s in sets)
            {
                var o = new JObject { ["clip"] = s.clip, ["relax"] = s.relax, ["samples"] = s.poses.Count, ["seconds"] = Math.Round(s.length, 4), ["penetrationMeasured"] = s.penMeasured,
                    ["abductionMeanDeg"] = Pair(i => s.Mean(p => p.abduction[i])), ["abductionMinDeg"] = Pair(i => s.Min(p => p.abduction[i])), ["abductionMaxDeg"] = Pair(i => s.Max(p => p.abduction[i])),
                    ["swingRangeDeg"] = Pair(i => s.Max(p => p.swing[i]) - s.Min(p => p.swing[i])), ["wristForeAftCm"] = Pair(i => s.Max(p => p.wrist[i].z - p.chest.z) - s.Min(p => p.wrist[i].z - p.chest.z), 100f),
                    ["elbowMinDeg"] = Pair(i => s.Min(p => p.elbow[i])), ["elbowMaxDeg"] = Pair(i => s.Max(p => p.elbow[i])), ["elbowSignedMinDeg"] = Pair(i => s.Min(p => p.flex[i])),
                    ["hyperextensionSamples"] = new JArray(s.poses.Count(p => p.flex[0] < 0), s.poses.Count(p => p.flex[1] < 0)),
                    ["twistUpperDeg"] = new JArray(Pair(i => s.Min(p => p.twistUpper[i])), Pair(i => s.Max(p => p.twistUpper[i]))), ["twistForeDeg"] = new JArray(Pair(i => s.Min(p => p.twistFore[i])), Pair(i => s.Max(p => p.twistFore[i]))),
                    ["penForeHandMaxCm"] = Pair(i => s.Max(p => p.penFore[i]), 100f), ["penSleeveMaxCm"] = Pair(i => s.Max(p => p.penUpper[i]), 100f),
                    // rev 2 (review F5): vertices the two votes disagree on - not a penetration, not "none": a picture decides
                    ["penUnsureForeHandMaxCm"] = Pair(i => s.Max(p => p.unsureFore[i]), 100f), ["penUnsureSleeveMaxCm"] = Pair(i => s.Max(p => p.unsureUpper[i]), 100f),
                    ["penUnsureForeHandSamples"] = new JArray(s.poses.Count(p => p.unsureFore[0] > 0), s.poses.Count(p => p.unsureFore[1] > 0)),
                    ["penForeHandSamples"] = new JArray(s.poses.Count(p => p.penFore[0] > 0), s.poses.Count(p => p.penFore[1] > 0)),
                    ["handClearanceCm"] = Pair(i => { float c = s.Min(p => p.clearHand[i]); return float.IsInfinity(c) ? -1f : c; }, 100f),
                    ["triStretchMax"] = new JArray(s.poses.Max(p => p.triStretch[0]), s.poses.Max(p => p.triStretch[1])), ["triCollapseMax"] = new JArray(s.poses.Max(p => p.triCollapse[0]), s.poses.Max(p => p.triCollapse[1])),
                    ["sectionElbowMin"] = Pair(i => s.Min(p => p.sectionElbow[i])), ["sectionShoulderMin"] = Pair(i => s.Min(p => p.sectionShoulder[i])),
                    ["outsideCullingVertices"] = s.poses.Sum(p => p.outsideCull), ["lowestVertexCm"] = new JArray(Math.Round(s.Min(p => p.lowestVertex) * 100, 2), Math.Round(s.poses[0].lowestVertex * 100, 2)) };
                // rev 3 (SPEC 개정 3): the farthest vertex by the M7 measure, and the per-frame lowest vertex as the statistics level-* works with
                o["cullingReachM"] = Math.Round(s.Max(p => p.cullReach), 4); o["lowestVertexStatsCm"] = StatsJson(LowestOf(s));
                // first-frame pop (M9): rotation between sample 0 and 1 against the median of the next ten steps of that bone
                if (s.poses.Count > 3)
                {
                    float worst = 0; int bone = 0; int bones = s.poses[0].local.Length; int over = 0;
                    for (int b = 0; b < bones; b++) { float a = Quaternion.Angle(s.poses[0].local[b], s.poses[1].local[b]); if (a > lim.popDeg) over++; if (a > worst) { worst = a; bone = b; } }
                    var later = Enumerable.Range(1, Mathf.Min(10, s.poses.Count - 2)).Select(i => Quaternion.Angle(s.poses[i].local[bone], s.poses[i + 1].local[bone])).OrderBy(x => x).ToArray();
                    o["firstStepMaxDeg"] = Math.Round(worst, 2); o["firstStepBonesOverLimit"] = over; o["laterMedianDeg"] = Math.Round(later[later.Length / 2], 2);
                }
                if (s.gait != null && s.gait.valid)
                {
                    var gt = s.gait;
                    o["gait"] = new JObject { ["stanceSpeed"] = Math.Round(gt.speed, 4), ["stride"] = Math.Round(gt.stride, 4), ["cycleSeconds"] = Math.Round(gt.seconds, 4), ["stanceSeconds"] = Math.Round(gt.stanceSeconds, 4),
                        ["stancePct"] = Pair(i => gt.stancePct[i]), ["stanceSpeedPerFoot"] = Pair(i => gt.stanceSpeed[i]), ["soleStanceMinCm"] = Pair(i => gt.soleMin[i], 100f), ["soleStanceMaxCm"] = Pair(i => gt.soleMax[i], 100f),
                        ["soleStanceMeanCm"] = Pair(i => gt.soleMean[i], 100f), ["soleCycleMinCm"] = Math.Round(gt.soleCycleMin * 100, 2), ["slideInClipCm"] = Pair(i => gt.slideInClip[i], 100f),
                        ["slopeUphillGapCm"] = new JArray(Math.Round(gt.slopeUp[0] * 100, 1), Math.Round(gt.slopeUp[1] * 100, 1)), ["slopeDownhillGapCm"] = new JArray(Math.Round(gt.slopeDown[0] * 100, 1), Math.Round(gt.slopeDown[1] * 100, 1)) };
                }
                jsets[s.key] = o;
            }
            json["sets"] = jsets;
            // rev 2 (review F5): the first Spec's Blender BVH numbers (Blender pose) beside the new ones. A cell that was several cm there and reads 0
            // here is not "none" before a picture confirms it; the decision table (decide_enemyrig2.py) lists those cells.
            JObject bvh = null;
            try { string bvhFile = string.IsNullOrEmpty(m.blenderMeasure) ? "" : Path.Combine(Repo, m.blenderMeasure); if (bvhFile != "" && File.Exists(bvhFile)) bvh = JObject.Parse(File.ReadAllText(bvhFile))["clips"] as JObject; }
            catch (Exception) { bvh = null; }
            JArray Bvh(string role, string field) { var pen = bvh?[role]?["pen"]; return pen == null ? null : new JArray(Math.Round(pen["Left"]?[field]?.Value<float>() ?? 0f, 2), Math.Round(pen["Right"]?[field]?.Value<float>() ?? 0f, 2)); }
            foreach (string role in new[] { "idle", "walk", "attack", "hit", "stun", "death" })
            {
                var set = jsets[role + "/off"] as JObject; var fore = Bvh(role, "fore_hand_max_cm");
                if (set != null && fore != null) { set["blenderBvhForeHandMaxCm"] = fore; set["blenderBvhSleeveMaxCm"] = Bvh(role, "upper_max_cm"); }
            }
            // M3: Unity humanoid (relax off) against the Blender source angles, frame by frame
            JObject M3(string setKey, string file, string node)
            {
                var s = Get(setKey); string path = string.IsNullOrEmpty(file) ? "" : Path.Combine(Repo, file);
                if (s == null || path == "" || !File.Exists(path)) return null;
                var reference = JObject.Parse(File.ReadAllText(path)); JToken r = reference; foreach (var part in node.Split('.')) r = r?[part];
                if (r == null) return null;
                var result = new JObject(); float worst = 0;
                foreach (var (name, pick) in new (string, Func<Pose, int, float>)[] { ("abduction", (p, i) => p.abduction[i]), ("swing", (p, i) => p.swing[i]), ("elbow", (p, i) => p.elbow[i]) })
                {
                    var pair = new JArray();
                    for (int side = 0; side < 2; side++)
                    {
                        var values = (r[SideName[side]]?[name] as JArray)?.Select(x => x.Value<float>()).ToArray(); float max = 0;
                        if (values != null) for (int k = 0; k < Mathf.Min(values.Length, s.poses.Count); k++) max = Mathf.Max(max, Mathf.Abs(Mathf.DeltaAngle(values[k], pick(s.poses[k], side))));
                        pair.Add(Math.Round(max, 2)); worst = Mathf.Max(worst, max);
                    }
                    result[name + "MaxDeltaDeg"] = pair;
                }
                result["worstDeg"] = Math.Round(worst, 2); return result;
            }
            var m3Source = M3("walk/off", m.blenderMeasure, "ref.walk"); var m3Fixed = M3("walkfix/off", m.blenderFix, "ref_after");
            if (m3Source != null) json["m3_source"] = m3Source; if (m3Fixed != null) json["m3_repaired"] = m3Fixed;
            float blenderSpeed = 0, blenderFixedSpeed = 0;
            try { if (File.Exists(Path.Combine(Repo, m.blenderFix))) { var bf = JObject.Parse(File.ReadAllText(Path.Combine(Repo, m.blenderFix))); blenderSpeed = bf["before"]?["gait"]?["stance_speed_m_s"]?.Value<float>() ?? 0; blenderFixedSpeed = bf["after"]?["gait"]?["stance_speed_m_s"]?.Value<float>() ?? 0; } }
            catch (Exception e) { json["blenderReferenceError"] = e.Message; }
            // rev 2 (review F7): the Blender stance speeds the M1 limit is taken against, kept so that two avatar states can be compared offline
            json["blenderStanceSpeed"] = new JObject { ["source"] = Math.Round(blenderSpeed, 4), ["repaired"] = Math.Round(blenderFixedSpeed, 4) };
            var jactors = new JArray();
            foreach (var actor in actors)
            {
                var walkSet = actor.clipKind == "walk_fix308" ? Get("walkfix/off") : Get("walk/off"); float v = walkSet?.gait != null && walkSet.gait.valid ? walkSet.gait.speed * actor.scale : 0;
                float ratio = actor.walkMetresPerSecond > 0 && v > 0 ? Mathf.Abs(1 - v / actor.walkMetresPerSecond) : -1; float cycle = walkSet != null ? walkSet.gait.seconds : 0;
                jactors.Add(new JObject { ["id"] = actor.id, ["object"] = actor.name, ["clip"] = actor.clipKind, ["relax"] = actor.relax, ["speed"] = Math.Round(actor.speed, 3), ["walkMetresPerSecond"] = Math.Round(actor.walkMetresPerSecond, 4),
                    ["stanceSpeedOnActor"] = Math.Round(v, 4), ["slideRatio"] = Math.Round(ratio, 4), ["slidePerStepCm"] = v > 0 ? Math.Round(Mathf.Abs(actor.walkMetresPerSecond - v) * walkSet.gait.stanceSeconds * 100, 2) : -1,
                    ["stepsPerMinute"] = cycle > 0 && actor.walkMetresPerSecond > 0 ? Math.Round(120.0 * (actor.speed / actor.walkMetresPerSecond) / cycle, 1) : -1, ["deathVisualSeconds"] = Math.Round(actor.deathVisualSeconds, 4) });
                if (ratio >= 0)
                {
                    float perStep = Mathf.Abs(actor.walkMetresPerSecond - v) * walkSet.gait.stanceSeconds * 100;
                    Limit(ratio <= lim.slideRatio && perStep <= lim.slidePerStepCm, "M2 " + actor.id + " 미끄러짐 " + F1(ratio * 100) + " % · 한 걸음 " + F1(perStep) + " cm (한계 " + F1(lim.slideRatio * 100) + " % · " + F1(lim.slidePerStepCm) + " cm)");
                }
            }
            json["actors"] = jactors;
            // ---- limits (TEST) on what the game shows now and on the repaired pair when it is present
            void CheckSet(string label, SetResult walk, SetResult idle)
            {
                if (walk == null || idle == null) return;
                for (int i = 0; i < 2; i++)
                {
                    if (walk.penMeasured)
                    {
                        Limit(walk.Max(p => p.penFore[i]) * 100 <= lim.penForeHandCm && idle.Max(p => p.penFore[i]) * 100 <= lim.penForeHandCm, "M5 " + label + " " + SideName[i] + " 손 · 아래팔 관통 걷기 " + F1(walk.Max(p => p.penFore[i]) * 100) + " / 대기 " + F1(idle.Max(p => p.penFore[i]) * 100) + " cm");
                        Limit(walk.Max(p => p.penUpper[i]) * 100 <= lim.penUpperCm, "M5 " + label + " " + SideName[i] + " 소매 관통 걷기 " + F1(walk.Max(p => p.penUpper[i]) * 100) + " cm > " + F1(lim.penUpperCm));
                        // rev 2 (review F5): a vertex the two votes disagree on is a miss of its own kind, never a silent 0
                        float unsureFore = Mathf.Max(walk.Max(p => p.unsureFore[i]), idle.Max(p => p.unsureFore[i])) * 100, unsureSleeve = walk.Max(p => p.unsureUpper[i]) * 100;
                        Limit(unsureFore <= lim.penForeHandCm && unsureSleeve <= lim.penUpperCm, "M5 " + label + " " + SideName[i] + " 미판정(두 기준이 갈림 — 그림으로 확인) 손 · 아래팔 " + F1(unsureFore) + " cm · 소매 " + F1(unsureSleeve) + " cm");
                    }
                    Limit(walk.Mean(p => p.abduction[i]) <= lim.walkAbductionMeanDeg, "M4 " + label + " " + SideName[i] + " 걷기 벌림 평균 " + F1(walk.Mean(p => p.abduction[i])) + "° > " + F1(lim.walkAbductionMeanDeg) + "°");
                    Limit(walk.poses.All(p => p.flex[i] >= 0) && idle.poses.All(p => p.flex[i] >= 0), "M6 " + label + " " + SideName[i] + " 팔꿈치 과신전");
                    Limit(walk.Min(p => p.sectionElbow[i]) >= lim.sectionMinRatio && idle.Min(p => p.sectionElbow[i]) >= lim.sectionMinRatio, "M6 " + label + " " + SideName[i] + " 팔꿈치 단면 " + F(Mathf.Min(walk.Min(p => p.sectionElbow[i]), idle.Min(p => p.sectionElbow[i]))) + " < " + F(lim.sectionMinRatio));
                }
                float elbowDiff = Mathf.Abs((walk.Max(p => p.elbow[0]) - walk.Min(p => p.elbow[0])) - (walk.Max(p => p.elbow[1]) - walk.Min(p => p.elbow[1])));
                Limit(elbowDiff <= lim.elbowRangeDiffDeg, "M10 " + label + " 팔꿈치 범위 좌우 차 " + F1(elbowDiff) + "° > " + F1(lim.elbowRangeDiffDeg) + "°");
                Limit(Mathf.Abs(walk.Mean(p => p.abduction[0]) - walk.Mean(p => p.abduction[1])) <= lim.abductionDiffDeg, "M10 " + label + " 벌림 평균 좌우 차 " + F1(Mathf.Abs(walk.Mean(p => p.abduction[0]) - walk.Mean(p => p.abduction[1]))) + "°");
                if (walk.gait != null && walk.gait.valid)
                {
                    Limit(walk.gait.soleCycleMin * 100 >= lim.soleMinCm, "M8 " + label + " 발바닥 최저 " + F1(walk.gait.soleCycleMin * 100) + " cm < " + F1(lim.soleMinCm));
                    Limit(Mathf.Max(walk.gait.soleMean[0], walk.gait.soleMean[1]) * 100 <= lim.stanceSoleMeanMaxCm, "M8 " + label + " 디딤 발바닥 평균 " + F1(Mathf.Max(walk.gait.soleMean[0], walk.gait.soleMean[1]) * 100) + " cm > " + F1(lim.stanceSoleMeanMaxCm));
                    Limit(Mathf.Abs(walk.gait.stancePct[0] - walk.gait.stancePct[1]) <= lim.stancePctDiff, "M10 " + label + " 디딤 비율 좌우 차 " + F1(Mathf.Abs(walk.gait.stancePct[0] - walk.gait.stancePct[1])) + " %p");
                    Limit(Mathf.Abs(walk.gait.soleMin[0] - walk.gait.soleMin[1]) * 100 <= lim.soleDiffCm, "M10 " + label + " 디딤 발바닥 좌우 차 " + F1(Mathf.Abs(walk.gait.soleMin[0] - walk.gait.soleMin[1]) * 100) + " cm");
                }
            }
            CheckSet("지금", Get("walk/legacy"), Get("idle/legacy"));
            if (Get("walkfix/profile") != null) CheckSet("수리 뒤", Get("walkfix/profile"), Get("idle/profile"));
            var walkOff = Get("walk/off"); var fixOff = Get("walkfix/off");
            if (walkOff?.gait != null && walkOff.gait.valid && blenderSpeed > 0) Limit(Mathf.Abs(walkOff.gait.speed / blenderSpeed - 1) <= lim.gaitVsBlenderRatio, "M1 원본 걷기 발 속도 Unity " + F(walkOff.gait.speed) + " vs Blender " + F(blenderSpeed));
            if (fixOff?.gait != null && fixOff.gait.valid && blenderFixedSpeed > 0) Limit(Mathf.Abs(fixOff.gait.speed / blenderFixedSpeed - 1) <= lim.gaitVsBlenderRatio, "M1 수리 걷기 발 속도 Unity " + F(fixOff.gait.speed) + " vs Blender " + F(blenderFixedSpeed));
            if (m3Source != null) Limit(m3Source["worstDeg"].Value<float>() <= lim.humanoidArmDeltaDeg, "M3 원본 걷기 Humanoid − Blender 최대 " + m3Source["worstDeg"] + "° > " + F1(lim.humanoidArmDeltaDeg) + "°");
            if (m3Fixed != null) Limit(m3Fixed["worstDeg"].Value<float>() <= lim.humanoidArmDeltaDeg, "M3 수리 걷기 Humanoid − Blender 최대 " + m3Fixed["worstDeg"] + "° > " + F1(lim.humanoidArmDeltaDeg) + "°");
            if (fixOff != null && walkOff != null) for (int i = 0; i < 2; i++)
            {
                float keep = (fixOff.Max(p => p.wrist[i].z - p.chest.z) - fixOff.Min(p => p.wrist[i].z - p.chest.z)) / Mathf.Max(.001f, walkOff.Max(p => p.wrist[i].z - p.chest.z) - walkOff.Min(p => p.wrist[i].z - p.chest.z));
                Limit(keep >= lim.swingKeepRatio, "M4 수리 걷기 " + SideName[i] + " 손목 흔들림 " + F1(keep * 100) + " % < " + F1(lim.swingKeepRatio * 100) + " %");
            }
            foreach (var s in sets) Limit(s.poses.Sum(p => p.outsideCull) == 0, "M7 " + s.key + " 컬링 구 밖 정점 " + s.poses.Sum(p => p.outsideCull));
            var death = Get("death/off"); float pop = death != null && death.poses.Count > 1 ? Enumerable.Range(0, death.poses[0].local.Length).Max(b => Quaternion.Angle(death.poses[0].local[b], death.poses[1].local[b])) : 0;
            Limit(pop <= lim.popDeg, "M9 사망 첫 프레임 " + F1(pop) + "° > " + F1(lim.popDeg) + "°");
            json["limitMisses"] = new JArray(fails.ToArray());
            // ---- rev 2 (5-c): the GAME pose against the DESIGN target, per state the player could be shown. M3 above stays the alarm
            // for "Unity is not Blender"; this block is what a candidate is judged by. Limits are the existing ones, none loosened.
            // The penetration verdict is split in three (review F2): the walk, the idle, and the idle CLIP ITSELF with the relax off.
            // An idle miss that the source clip already carries is the clip's, not the candidate's: it still counts (met stays n / 3),
            // but it is shown as what it is so that one cell of the source idle does not pass for "the candidate failed".
            var design = new JObject(); var designRows = new List<string>(); var idleSource = Get("idle/off");
            float Cm(SetResult s, Func<Pose, float> f) => s != null ? s.Max(f) * 100 : 0;
            string stateSuffix = avatarNow == "live" ? "" : " · 아바타 " + avatarNow;
            void Judge(string label, string key, SetResult walk, SetResult idle)
            {
                if (walk == null) return;
                float spread = Mathf.Max(walk.Mean(p => p.abduction[0]), walk.Mean(p => p.abduction[1]));
                float rangeDiff = Mathf.Abs((walk.Max(p => p.elbow[0]) - walk.Min(p => p.elbow[0])) - (walk.Max(p => p.elbow[1]) - walk.Min(p => p.elbow[1])));
                float spreadDiff = Mathf.Abs(walk.Mean(p => p.abduction[0]) - walk.Mean(p => p.abduction[1]));
                bool spreadOk = spread <= lim.walkAbductionMeanDeg, symmetric = rangeDiff <= lim.elbowRangeDiffDeg && spreadDiff <= lim.abductionDiffDeg;
                bool measured = walk.penMeasured && (idle == null || idle.penMeasured);
                float foreWalk = Cm(walk, p => Mathf.Max(p.penFore[0], p.penFore[1])), sleeve = Cm(walk, p => Mathf.Max(p.penUpper[0], p.penUpper[1]));
                float unsureWalk = Cm(walk, p => Mathf.Max(p.unsureFore[0], p.unsureFore[1])), unsureSleeve = Cm(walk, p => Mathf.Max(p.unsureUpper[0], p.unsureUpper[1]));
                float foreIdle = Cm(idle, p => Mathf.Max(p.penFore[0], p.penFore[1])), unsureIdle = Cm(idle, p => Mathf.Max(p.unsureFore[0], p.unsureFore[1]));
                float sourcePen = Cm(idleSource, p => Mathf.Max(p.penFore[0], p.penFore[1])), sourceUnsure = Cm(idleSource, p => Mathf.Max(p.unsureFore[0], p.unsureFore[1]));
                bool walkClean = measured && foreWalk <= lim.penForeHandCm && sleeve <= lim.penUpperCm && unsureWalk <= lim.penForeHandCm && unsureSleeve <= lim.penUpperCm;
                bool idleClean = measured && foreIdle <= lim.penForeHandCm && unsureIdle <= lim.penForeHandCm, clean = walkClean && idleClean;
                bool onlyIdle = measured && spreadOk && symmetric && walkClean && !idleClean, idleIsSource = onlyIdle && (sourcePen > lim.penForeHandCm || sourceUnsure > lim.penForeHandCm);
                float swing = Mathf.Min(walk.Max(p => p.swing[0]) - walk.Min(p => p.swing[0]), walk.Max(p => p.swing[1]) - walk.Min(p => p.swing[1]));
                int met = (spreadOk ? 1 : 0) + (symmetric ? 1 : 0) + (clean ? 1 : 0);
                design[key] = new JObject { ["spreadMaxSideDeg"] = Math.Round(spread, 2), ["spreadOk"] = spreadOk, ["elbowRangeDiffDeg"] = Math.Round(rangeDiff, 2), ["spreadDiffDeg"] = Math.Round(spreadDiff, 2), ["symmetric"] = symmetric,
                    ["penetrationMeasured"] = measured, ["foreHandCm"] = Math.Round(Mathf.Max(foreWalk, foreIdle), 2), ["sleeveCm"] = Math.Round(sleeve, 2), ["clean"] = clean,
                    ["walkForeHandCm"] = Math.Round(foreWalk, 2), ["walkUnsureForeHandCm"] = Math.Round(unsureWalk, 2), ["walkUnsureSleeveCm"] = Math.Round(unsureSleeve, 2), ["walkClean"] = walkClean,
                    ["walkPenetrationFree"] = measured && foreWalk <= lim.penForeHandCm && sleeve <= lim.penUpperCm,   // no CONFIRMED penetration; an undetermined vertex still keeps walkClean false
                    ["idleForeHandCm"] = Math.Round(foreIdle, 2), ["idleUnsureForeHandCm"] = Math.Round(unsureIdle, 2), ["idleClean"] = idleClean, ["idlePenetrationFree"] = measured && foreIdle <= lim.penForeHandCm,
                    ["idleSourceForeHandCm"] = Math.Round(sourcePen, 2), ["idleSourceUnsureForeHandCm"] = Math.Round(sourceUnsure, 2), ["onlyIdleMisses"] = onlyIdle, ["idleMissIsTheSourceClip"] = idleIsSource,
                    ["armSwingMinSideDeg"] = Math.Round(swing, 1), ["met"] = met };
                string Unsure(float a, float b) => a > 0 || b > 0 ? " (미판정 " + F1(a) + (b >= 0 ? " · " + F1(b) : "") + ")" : "";
                designRows.Add("| " + label + stateSuffix + " | " + F1(spread) + "° " + (spreadOk ? "○" : "✕") + " | 범위 차 " + F1(rangeDiff) + "° · 벌림 차 " + F1(spreadDiff) + "° " + (symmetric ? "○" : "✕") + " | " +
                    (measured ? "손 " + F1(foreWalk) + " · 소매 " + F1(sleeve) + " cm" + Unsure(unsureWalk, unsureSleeve) + " " + (walkClean ? "○" : "✕") : "미측정 ✕") + " | " +
                    (measured ? "손 " + F1(foreIdle) + " cm" + Unsure(unsureIdle, -1) + " " + (idleClean ? "○" : "✕") : "미측정 ✕") + " | " +
                    (idleSource != null && idleSource.penMeasured ? "손 " + F1(sourcePen) + " cm" + Unsure(sourceUnsure, -1) : "미측정") + " | " + F1(swing) + "° | " + met + " / 3" +
                    (onlyIdle ? (idleIsSource ? " — 대기 원본 클립의 관통만 남음" : " — 대기 관통만 남음") : "") + " |");
            }
            Judge("지금(원본 걷기 + 자동 보정)", "now", Get("walk/legacy"), Get("idle/legacy"));
            Judge("수리 걷기 + 프로필(걷기 보정 0)", "repaired", Get("walkfix/profile"), Get("idle/profile"));
            Judge("후보 B(수리 걷기 + 걷기 보정 자료)", "candidateB", Get("walkfix/B"), Get("idle/B"));
            json["design"] = design;
            // ---- rev 2 (5-d): idle <-> walk foot-height step = lowest vertex of the idle (mean over the clip) against the stance sole of each walk
            var idleOff = Get("idle/off"); float idleLow = idleOff != null ? idleOff.Mean(p => p.lowestVertex) * 100 : float.NaN;
            float StepTo(SetResult walk) => walk?.gait != null && walk.gait.valid && !float.IsNaN(idleLow) ? idleLow - Mathf.Min(walk.gait.soleMean[0], walk.gait.soleMean[1]) * 100 : float.NaN;
            float stepNow = StepTo(Get("walk/off")), stepFixed = StepTo(Get("walkfix/off"));
            json["idleWalkStep"] = new JObject { ["idleLowestVertexMeanCm"] = float.IsNaN(idleLow) ? null : (JToken)Math.Round(idleLow, 2), ["stepToSourceWalkCm"] = float.IsNaN(stepNow) ? null : (JToken)Math.Round(stepNow, 2),
                ["stepToRepairedWalkCm"] = float.IsNaN(stepFixed) ? null : (JToken)Math.Round(stepFixed, 2), ["closedBy"] = "level-apply (rev 3): the root height offset of every listed take (importer value); see 'level' below" };
            // rev 3: what the gate reads - the prefab's culling radius this measure counted against, and every listed take's planted height
            var cullingJson = CullingJson(cfg, m, sets); var levelJson = LevelJson(cfg, Get); json["culling"] = cullingJson; json["level"] = levelJson;
            File.WriteAllText(Path.Combine(Out, "measure_" + m.id + suffix + ".json"), json.ToString(), new UTF8Encoding(false));
            // ---- Korean table
            string P2(SetResult s, Func<Pose, int, float> pick, Func<SetResult, Func<Pose, float>, float> fold, float scale = 1) => s == null ? "-" : F1(fold(s, p => pick(p, 0)) * scale) + " / " + F1(fold(s, p => pick(p, 1)) * scale);
            float MaxOf(SetResult s, Func<Pose, float> f) => s.Max(f); float MeanOf(SetResult s, Func<Pose, float> f) => s.Mean(f);
            md.AppendLine("## " + m.displayName + " (`" + m.id + "`)\n");
            md.AppendLine("아바타: **" + (avatarNow == "live" ? "지금 것" : avatarNow == "A" ? "후보 A(T자 기준 자세)" : avatarNow) + "** (" + avatarNote + ")\n");
            md.AppendLine("클립 파일의 기준 자세 사본: " + (staleRows.Count == 0 ? rowFiles + "개 모두 리그와 같음" : "**낡음(STALE)** " + string.Join("; ", staleRows) + " → `avatar-sync:" + m.id + "` 뒤 다시 잴 것 (그 클립의 수치는 다른 기준 자세로 변환된 것이다)") + "\n");
            md.AppendLine("관통 검사 자가 확인: " + selfTest + (penTrusted ? "" : " → **M5 미측정**") + " · 팔 검사 정점 " + testVerts + " · 몸 삼각형 " + bodyTris + "\n");
            md.AppendLine("관통 표 읽는 법: 괄호 안 \"미판정\" = 두 기준(감김수 · 가장 가까운 면의 뒤쪽)이 갈린 정점의 깊이. 관통도 아니고 \"없음\"도 아니다 — 그림으로 확인한다. 몸의 가장 바깥 면 바로 뒤 " +
                F1(cfg.pen.nearOffsetCm) + " cm 점을 두 기준이 모두 놓치는 비율이 위 자가 확인 줄의 missed 값이다(다른 면에 2 mm 안으로 붙어 있거나 두 겹 사이에 낀 점 — 이 측정이 못 보는 몫). \"관통 0\"은 그 몫만큼 덜 확실하다.\n");
            md.AppendLine("| 항목 | 지금(원본 걷기 + 자동 보정) | 원본 걷기 보정 끔 | 수리 걷기 + 프로필 | 한계 |"); md.AppendLine("|---|---|---|---|---|");
            var now = Get("walk/legacy"); var after = Get("walkfix/profile") ?? Get("walkfix/off");
            md.AppendLine("| M4 걷기 벌림 평균(좌 / 우) | " + P2(now, (p, i) => p.abduction[i], MeanOf) + "° | " + P2(walkOff, (p, i) => p.abduction[i], MeanOf) + "° | " + P2(after, (p, i) => p.abduction[i], MeanOf) + "° | ≤ " + F1(lim.walkAbductionMeanDeg) + "° |");
            md.AppendLine("| M10 걷기 팔꿈치 최대 굽힘 | " + P2(now, (p, i) => p.elbow[i], MaxOf) + "° | " + P2(walkOff, (p, i) => p.elbow[i], MaxOf) + "° | " + P2(after, (p, i) => p.elbow[i], MaxOf) + "° | 범위 좌우 차 ≤ " + F1(lim.elbowRangeDiffDeg) + "° |");
            // rev 2 (review F5): penetration cells show the undetermined depth beside the confirmed one, and "미측정" instead of a 0 that is not a value
            string Pen2(SetResult s, Func<Pose, int, float> pen, Func<Pose, int, float> unsure) => s == null ? "-" : !s.penMeasured ? "미측정" :
                P2(s, pen, MaxOf, 100) + " cm" + (s.Max(p => Mathf.Max(unsure(p, 0), unsure(p, 1))) > 0 ? " (미판정 " + P2(s, unsure, MaxOf, 100) + ")" : "");
            md.AppendLine("| M5 걷기 손 · 아래팔 관통 | " + Pen2(now, (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + Pen2(walkOff, (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + Pen2(after, (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + F1(lim.penForeHandCm) + " cm |");
            md.AppendLine("| M5 걷기 소매 관통 | " + Pen2(now, (p, i) => p.penUpper[i], (p, i) => p.unsureUpper[i]) + " | " + Pen2(walkOff, (p, i) => p.penUpper[i], (p, i) => p.unsureUpper[i]) + " | " + Pen2(after, (p, i) => p.penUpper[i], (p, i) => p.unsureUpper[i]) + " | ≤ " + F1(lim.penUpperCm) + " cm |");
            md.AppendLine("| M5 대기 손 · 아래팔 관통 | " + Pen2(Get("idle/legacy"), (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + Pen2(Get("idle/off"), (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + Pen2(Get("idle/profile"), (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + F1(lim.penForeHandCm) + " cm |");
            string G(SetResult s, Func<Gait, string> f) => s?.gait != null && s.gait.valid ? f(s.gait) : "-";
            md.AppendLine("| M1 디딘 발 속도 · 보폭 | " + G(walkOff, x => F(x.speed) + " m/s · " + F(x.stride) + " m") + " | (같음) | " + G(fixOff, x => F(x.speed) + " m/s · " + F(x.stride) + " m") + " | Blender " + F(blenderSpeed) + " / " + F(blenderFixedSpeed) + " ±" + F1(lim.gaitVsBlenderRatio * 100) + " % |");
            md.AppendLine("| M8 디딤 발바닥 높이(좌 / 우) | " + G(walkOff, x => F1(x.soleMin[0] * 100) + "…" + F1(x.soleMax[0] * 100) + " / " + F1(x.soleMin[1] * 100) + "…" + F1(x.soleMax[1] * 100) + " cm") + " | (같음) | " +
                G(fixOff, x => F1(x.soleMin[0] * 100) + "…" + F1(x.soleMax[0] * 100) + " / " + F1(x.soleMin[1] * 100) + "…" + F1(x.soleMax[1] * 100) + " cm") + " | 최저 ≥ " + F1(lim.soleMinCm) + " · 디딤 평균 ≤ " + F1(lim.stanceSoleMeanMaxCm) + " cm |");
            md.AppendLine("| M8 10° 비탈 발 틈(오르막) | " + G(walkOff, x => F1(x.slopeUp[0] * 100) + "…" + F1(x.slopeUp[1] * 100) + " cm") + " | (같음) | " + G(fixOff, x => F1(x.slopeUp[0] * 100) + "…" + F1(x.slopeUp[1] * 100) + " cm") + " | 기록만 |");
            md.AppendLine("| M3 Humanoid − Blender 팔 각도 | " + (m3Source != null ? m3Source["worstDeg"] + "°" : "기준 없음") + " | | " + (m3Fixed != null ? m3Fixed["worstDeg"] + "°" : "기준 없음") + " | ≤ " + F1(lim.humanoidArmDeltaDeg) + "° |");
            md.AppendLine("| M9 사망 첫 프레임 | " + F1(pop) + "° | | | ≤ " + F1(lim.popDeg) + "° |");
            md.AppendLine("| M6 공격 팔꿈치 단면 최소 | " + P2(Get("attack/off"), (p, i) => p.sectionElbow[i], (s, f) => s.Min(f)) + " | | | 기록만(A4) |");
            md.AppendLine("| 스턴 · 피격 손 관통 | " + Pen2(Get("stun/off"), (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " · " + Pen2(Get("hit/off"), (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | | | 기록만(A5 · A6) |\n");
            // rev 2 (review F4 · F7): every clip on THIS avatar, relax off - the table candidate A is compared by, clip by clip (live file against the _A file)
            md.AppendLine("클립별 팔과 몸(보정 끔, 이 아바타):\n");
            md.AppendLine("| 클립 | 팔꿈치 범위(좌 / 우) | 벌림 평균 | 손 · 아래팔 관통 | 소매 관통 | 1차 Blender BVH 값(손 · 소매, Blender 자세) | 컬링 구 밖 정점 | 가장 낮은 정점 |"); md.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (string key in new[] { "idle/off", "walk/off", "walkfix/off", "run/off", "attack/off", "hit/off", "stun/off", "death/off" })
            {
                var s = Get(key); if (s == null) continue;
                var bvhFore = jsets[key]?["blenderBvhForeHandMaxCm"] as JArray; var bvhSleeve = jsets[key]?["blenderBvhSleeveMaxCm"] as JArray;
                string bvhCell = bvhFore == null || bvhSleeve == null ? "-" : F1(bvhFore[0].Value<float>()) + " / " + F1(bvhFore[1].Value<float>()) + " · " + F1(bvhSleeve[0].Value<float>()) + " / " + F1(bvhSleeve[1].Value<float>()) + " cm";
                md.AppendLine("| " + key + " | " + F1(s.Min(p => p.elbow[0])) + "–" + F1(s.Max(p => p.elbow[0])) + " / " + F1(s.Min(p => p.elbow[1])) + "–" + F1(s.Max(p => p.elbow[1])) + "° | " + P2(s, (p, i) => p.abduction[i], MeanOf) + "° | " +
                    Pen2(s, (p, i) => p.penFore[i], (p, i) => p.unsureFore[i]) + " | " + Pen2(s, (p, i) => p.penUpper[i], (p, i) => p.unsureUpper[i]) + " | " + bvhCell + " | " + s.poses.Sum(p => p.outsideCull) + " | " + F1(s.Min(p => p.lowestVertex) * 100) + " cm |");
            }
            md.AppendLine("\n\"1차 Blender BVH 값\"은 1차 Spec이 Blender에서 다른 방법으로 잰 값이다(게임 자세가 아니라 FBX의 자세). 그 값이 몇 cm인데 여기 관통이 0인 칸은 그림으로 확인하기 전에는 \"없음\"이 아니다.\n");
            md.AppendLine(CullingLine(cullingJson)); md.AppendLine(LevelLine(levelJson));   // rev 3
            if (runClip == null && !string.IsNullOrEmpty(m.runClip)) md.AppendLine("달리기 클립이 임포트되어 있지 않다(달리기 스테이지 미배포) — 이 종의 후보 A는 달리기를 그 상태에서 재고 찍기 전에는 **잠정**이다.\n");
            if (actors.Count > 0)
            {
                md.AppendLine("| 배우 | 클립 | 보정 | Speed | WMPS | 미끄러짐 | 분당 걸음 |"); md.AppendLine("|---|---|---|---|---|---|---|");
                foreach (JObject a in jactors) md.AppendLine("| " + a["id"] + " | " + a["clip"] + " | " + a["relax"] + " | " + a["speed"] + " | " + a["walkMetresPerSecond"] + " | " + (a["slideRatio"].Value<float>() < 0 ? "-" : F1(a["slideRatio"].Value<float>() * 100) + " % · " + a["slidePerStepCm"] + " cm/걸음") + " | " + a["stepsPerMinute"] + " |");
                md.AppendLine();
            }
            if (designRows.Count > 0)
            {
                md.AppendLine("게임 자세 대 설계 목표 (플레이어가 보는 자세 — 벌림 ≤ " + F1(lim.walkAbductionMeanDeg) + "° · 좌우 대칭 · 관통 없음):\n");
                md.AppendLine("| 상태 | 걷기 벌림(큰 쪽) | 팔꿈치 · 벌림 좌우 | 걷기 관통 | 대기 관통 | 대기 원본 클립(보정 끔) | 팔 흔들림(작은 쪽) | 충족 |"); md.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var line in designRows) md.AppendLine(line);
                md.AppendLine("\n\"대기 원본 클립\" 칸 = 대기 클립을 보정 없이 이 아바타에 올렸을 때의 손 관통. 대기 관통이 이 값에서 온 것이면 후보의 탓이 아니라 클립의 것이다(충족 수에는 그대로 센다).\n");
            }
            if (!float.IsNaN(idleLow)) md.AppendLine("대기 ↔ 걷기 발 높이 차: 대기 가장 낮은 정점 평균 " + F1(idleLow) + " cm → 지금 걷기 디딤 발바닥과 " + (float.IsNaN(stepNow) ? "-" : F1(stepNow)) + " cm · 수리 걷기와 " +
                (float.IsNaN(stepFixed) ? "-" : F1(stepFixed)) + " cm (닫는 값 = 테이크별 루트 높이 오프셋, `level-dry` — 개정 3)\n");
            md.AppendLine(fails.Count == 0 ? "한계 밖 0건.\n" : "한계 밖 " + fails.Count + "건:\n" + string.Join("\n", fails.Select(x => "- " + x)) + "\n");
            return m.id + ": " + sets.Count + " sets, " + sets.Sum(s => s.poses.Count) + " samples | walk stance speed " + G(walkOff, x => F(x.speed)) + (fixOff != null ? " -> repaired " + G(fixOff, x => F(x.speed)) : " (repaired clip not imported)") +
                " | pen self-test: " + selfTest + (staleRows.Count > 0 ? " | CLIP ROWS STALE: " + string.Join("; ", staleRows) : "") + (runClip == null && !string.IsNullOrEmpty(m.runClip) ? " | run clip not imported" : "") + " | actors " + actors.Count + " | limit misses " + fails.Count + (fails.Count > 0 ? " (" + string.Join("; ", fails.Take(4)) + (fails.Count > 4 ? "; ..." : "") + ")" : "");
        }
    }
}
