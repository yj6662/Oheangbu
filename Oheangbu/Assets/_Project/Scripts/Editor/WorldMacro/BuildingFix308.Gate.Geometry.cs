using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 gate track — measurement and mesh builders of BuildingFix308.Gate (G2 door frame, G3 arch contact faces, G4
    // stalk mesh). Everything is measured from the meshes that are in the scene; the numbers come from BuildingAudit308/gate308.json.
    // Gate frame: x = across the opening, y = up, z = through the wall; the leaves swing toward +z (SouthGateDoorPresentation:
    // left hinge -openDegrees, right hinge +openDegrees about y), so the fixed frame sits on the -z face.
    public static partial class BuildingFix308
    {
        // ------------------------------------------------------------------ projected grid of the opening (gate x / y)

        sealed class GateGrid308
        {
            public readonly float x0, y0, cell; public readonly int nx, ny;
            public GateGrid308(float halfWidth, float floor, float top, float cell)
            {
                x0 = -halfWidth; y0 = floor; this.cell = cell;
                nx = Mathf.Max(1, Mathf.CeilToInt(2f * halfWidth / cell)); ny = Mathf.Max(1, Mathf.CeilToInt((top - floor) / cell));
            }
            public bool[] New() => new bool[nx * ny];
            public float X(int i) => x0 + (i + .5f) * cell;
            public float Y(int j) => y0 + (j + .5f) * cell;
            public int Row(float y) => Mathf.Clamp(Mathf.FloorToInt((y - y0) / cell), 0, ny - 1);
            public int Col(float x) => Mathf.Clamp(Mathf.FloorToInt((x - x0) / cell), 0, nx - 1);

            // cells whose centre lies inside the triangle projected on x / y (edge-on triangles cover nothing)
            public void Raster(bool[] g, Vector3 a, Vector3 b, Vector3 c)
            {
                float d = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(d) < 1e-9f) return;
                float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                int i0 = Mathf.Max(0, Mathf.FloorToInt((minX - x0) / cell - .5f)), i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((maxX - x0) / cell - .5f));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((minY - y0) / cell - .5f)), j1 = Mathf.Min(ny - 1, Mathf.CeilToInt((maxY - y0) / cell - .5f));
                float inv = 1f / d;
                for (int j = j0; j <= j1; j++)
                {
                    float y = Y(j);
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = X(i);
                        float u = ((x - a.x) * (c.y - a.y) - (y - a.y) * (c.x - a.x)) * inv;
                        float w = ((b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x)) * inv;
                        if (u >= -1e-5f && w >= -1e-5f && u + w <= 1f + 1e-5f) g[j * nx + i] = true;
                    }
                }
            }

            public void Raster(bool[] g, Vector3[] v, int[] t)
            {
                for (int i = 0; i + 2 < t.Length; i += 3) Raster(g, v[t[i]], v[t[i + 1]], v[t[i + 2]]);
            }

            // the region of free cells connected to (x, y): the opening. The floor row may be open; any other border reached = not enclosed
            public bool[] Flood(bool[] blocked, float x, float y, out bool escaped, out int count)
            {
                escaped = false; count = 0;
                int si = Col(x), sj = Row(y);
                if (blocked[sj * nx + si]) return null;
                var open = New(); var queue = new Queue<int>();
                open[sj * nx + si] = true; queue.Enqueue(sj * nx + si);
                while (queue.Count > 0)
                {
                    int k = queue.Dequeue(); count++;
                    int i = k % nx, j = k / nx;
                    if (i == 0 || i == nx - 1 || j == ny - 1) escaped = true;
                    if (i > 0) Visit(k - 1); if (i < nx - 1) Visit(k + 1); if (j > 0) Visit(k - nx); if (j < ny - 1) Visit(k + nx);
                    void Visit(int n) { if (!blocked[n] && !open[n]) { open[n] = true; queue.Enqueue(n); } }
                }
                return open;
            }

            // widest |x| of the opening in the rows whose centre lies in [ya, yb] (cell edge)
            public float MaxAbsX(bool[] open, float ya, float yb)
            {
                float best = 0;
                for (int j = 0; j < ny; j++)
                {
                    float y = Y(j); if (y < ya || y > yb) continue;
                    for (int i = 0; i < nx; i++) if (open[j * nx + i]) best = Mathf.Max(best, Mathf.Abs(X(i)) + cell * .5f);
                }
                return best;
            }
        }

        // z0 = outer face (shared by the whole frame), z1 = back face: per part, just clear of what the leaves sweep in its own footprint
        internal sealed class GateRect308 { public string part = ""; public float x0, x1, y0, y1, z0, z1, sweep = float.PositiveInfinity; }
        sealed class GateLeaf308 { public Transform hinge; public float sign; public Quaternion closed; public readonly List<(Vector3[] v, int[] t)> parts = new List<(Vector3[] v, int[] t)>(); }

        internal sealed class GateFrameDesign308
        {
            public string id = "", error = "", materialPath = "";
            public int open, missBefore, missUpper, missLeft, missRight, missAfter, missChild = -1, layer;
            public float cell, leafL, leafR, upperY0, upperY1, sideTop, openDegrees, sweepMin, zFront, uvScale, archZMin, archZMax, childClear = float.PositiveInfinity;
            public readonly List<GateRect308> rects = new List<GateRect308>();
            public Bounds Bounds()
            {
                var b = new Bounds(); bool first = true;
                foreach (var r in rects)
                    foreach (var p in new[] { new Vector3(r.x0, r.y0, r.z0), new Vector3(r.x1, r.y1, r.z1) })
                        if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                return b;
            }
            public string Text()
            {
                string F(float v) => BuildingAudit308.F(v);
                return string.Join("; ", rects.Select(r => r.part + " x[" + F(r.x0) + ", " + F(r.x1) + "] y[" + F(r.y0) + ", " + F(r.y1) + "] z[" + F(r.z0) + ", " + F(r.z1) + "]"));
            }
        }

        // local-to-ancestor matrix from the local TRS chain (no world round trip: the scenes sit 2-3 km from the origin)
        static Matrix4x4 GateToAncestor(Transform t, Transform ancestor)
        {
            var m = Matrix4x4.identity;
            for (var x = t; x != null && x != ancestor; x = x.parent) m = Matrix4x4.TRS(x.localPosition, x.localRotation, x.localScale) * m;
            return m;
        }

        static Transform GateArchOf(GateCtx308 ctx, GateSite308 site)
        {
            var architecture = string.IsNullOrEmpty(site.architecture) ? null : BuildingAudit308.Resolve(ctx.scene, site.architecture);
            if (architecture == null) return null;
            foreach (var t in architecture.GetComponentsInChildren<Transform>(true)) if (t.name == site.arch) return t;
            return null;
        }

        // keeps the part of a convex polygon on one side of an axis plane (Sutherland-Hodgman on 3D points, so z is interpolated)
        static void GateClip(List<Vector3> src, List<Vector3> dst, int axis, float value, bool keepGreater)
        {
            dst.Clear();
            for (int i = 0; i < src.Count; i++)
            {
                Vector3 a = src[i], b = src[(i + 1) % src.Count];
                bool ia = keepGreater ? a[axis] >= value : a[axis] <= value, ib = keepGreater ? b[axis] >= value : b[axis] <= value;
                if (ia) dst.Add(a);
                if (ia != ib) dst.Add(a + (b - a) * ((value - a[axis]) / (b[axis] - a[axis])));
            }
        }

        // lowest z the leaves reach inside one footprint (expanded by margin) over the whole swing; +inf when nothing does.
        // Each triangle is clipped to the footprint first, so a hinge-side vertex far outside it does not drag the depth of
        // another part along (the header sits right in front of the leaf tops, the posts in front of the hinge edges).
        static float GateSweepMin(List<GateLeaf308> leaves, GateRect308 rect, float margin, float openDegrees, float step)
        {
            float zmin = float.PositiveInfinity;
            float x0 = rect.x0 - margin, x1 = rect.x1 + margin, y0 = rect.y0 - margin, y1 = rect.y1 + margin;
            var poly = new List<Vector3>(12); var tmp = new List<Vector3>(12);
            foreach (var leaf in leaves)
                for (float angle = 0; angle <= openDegrees + 1e-3f; angle += step)
                {
                    var m = Matrix4x4.TRS(leaf.hinge.localPosition, leaf.closed * Quaternion.Euler(0, leaf.sign * angle, 0), leaf.hinge.localScale);
                    foreach (var (v, t) in leaf.parts)
                    {
                        var w = new Vector3[v.Length];
                        for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            Vector3 p = w[t[i]], q = w[t[i + 1]], r = w[t[i + 2]];
                            if (Mathf.Max(p.x, Mathf.Max(q.x, r.x)) < x0 || Mathf.Min(p.x, Mathf.Min(q.x, r.x)) > x1 || Mathf.Max(p.y, Mathf.Max(q.y, r.y)) < y0 || Mathf.Min(p.y, Mathf.Min(q.y, r.y)) > y1) continue;
                            if (Mathf.Min(p.z, Mathf.Min(q.z, r.z)) >= zmin) continue;
                            poly.Clear(); poly.Add(p); poly.Add(q); poly.Add(r);
                            GateClip(poly, tmp, 0, x0, true); GateClip(tmp, poly, 0, x1, false); GateClip(poly, tmp, 1, y0, true); GateClip(tmp, poly, 1, y1, false);
                            foreach (var c in poly) if (c.z < zmin) zmin = c.z;
                        }
                    }
                }
            return zmin;
        }

        // the door's own material (largest area of the leaf) and its texel density (uv units per metre)
        static void GateLeafMaterial(Transform hinge, out Material material, out float uvScale)
        {
            material = null; uvScale = .25f; float best = 0;
            foreach (var mf in hinge.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>(); if (mesh == null || mr == null) continue;
                var mats = mr.sharedMaterials; var v = mesh.vertices; var uv = mesh.uv; var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null) continue;
                    var t = mesh.GetTriangles(s); double area = 0, uvArea = 0;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * .5f;
                        if (uv != null && uv.Length == v.Length) { Vector2 a = uv[t[i]], b = uv[t[i + 1]], c = uv[t[i + 2]]; uvArea += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * .5f; }
                    }
                    if (area <= best) continue;
                    best = (float)area; material = mats[s];
                    if (uvArea > 1e-9 && area > 1e-9) uvScale = Mathf.Sqrt((float)(uvArea / area));
                }
            }
        }

        static List<GateRect308> GateRectsOfMesh(Vector3[] v, int[] t)
        {
            // the front / back faces of the frame boxes: one rect per face pair, z0 / z1 = the two face depths
            var rects = new List<GateRect308>();
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a); if (n.magnitude < 1e-9f || Mathf.Abs(n.normalized.z) < .99f) continue;
                var r = new GateRect308 { x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), y0 = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), y1 = Mathf.Max(a.y, Mathf.Max(b.y, c.y)), z0 = a.z, z1 = a.z };
                var same = rects.FirstOrDefault(o => Mathf.Abs(o.x0 - r.x0) < 1e-4f && Mathf.Abs(o.x1 - r.x1) < 1e-4f && Mathf.Abs(o.y0 - r.y0) < 1e-4f && Mathf.Abs(o.y1 - r.y1) < 1e-4f);
                if (same == null) rects.Add(r); else { same.z0 = Mathf.Min(same.z0, a.z); same.z1 = Mathf.Max(same.z1, a.z); }
            }
            return rects;
        }

        // ------------------------------------------------------------------ G2 measurement of one gate

        static GateFrameDesign308 GateMeasure(GateCtx308 ctx, GateSite308 site, Transform gate)
        {
            var f = ctx.g.frame;
            var d = new GateFrameDesign308 { id = site.id, cell = f.cell, openDegrees = f.openDegrees };
            var arch = GateArchOf(ctx, site);
            if (arch == null) { d.error = "no " + site.arch + " under " + site.architecture; return d; }

            // hinges, closed pose and swing (the door component is the authority when present)
            Transform left = gate.Find(f.leftHinge), right = gate.Find(f.rightHinge);
            Quaternion leftClosed = left != null ? left.localRotation : Quaternion.identity, rightClosed = right != null ? right.localRotation : Quaternion.identity;
            var door = gate.GetComponent<Oheangbu.App.Demo.SouthGateDoorPresentation>();
            if (door != null)
            {
                var so = new SerializedObject(door);
                if (so.FindProperty("_leftLeaf")?.objectReferenceValue is Transform lt) left = lt;
                if (so.FindProperty("_rightLeaf")?.objectReferenceValue is Transform rt) right = rt;
                var lc = so.FindProperty("_leftClosed"); var rc = so.FindProperty("_rightClosed"); var od = so.FindProperty("_openDegrees");
                if (lc != null) leftClosed = lc.quaternionValue;
                if (rc != null) rightClosed = rc.quaternionValue;
                if (od != null) d.openDegrees = od.floatValue;
            }
            if (left == null || right == null) { d.error = "hinges " + f.leftHinge + " / " + f.rightHinge + " missing"; return d; }
            if (left.parent != gate || right.parent != gate) { d.error = "the hinges are not direct children of the gate"; return d; }
            if (Quaternion.Angle(left.localRotation, leftClosed) > .01f || Quaternion.Angle(right.localRotation, rightClosed) > .01f) { d.error = "the saved leaves are not in the closed pose"; return d; }
            if (d.openDegrees <= 0) { d.error = "open angle is 0"; return d; }

            var toGate = gate.worldToLocalMatrix;
            var grid = new GateGrid308(f.halfWidth, f.floor, f.top, f.cell);
            bool[] masonry = grid.New(), leafMask = grid.New(), fill = grid.New();

            // masonry = the arch shells projected through the wall
            d.archZMin = float.PositiveInfinity; d.archZMax = float.NegativeInfinity; int archMeshes = 0;
            foreach (var mf in arch.GetComponentsInChildren<MeshFilter>(false))
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (mf.sharedMesh == null || mr == null || !mr.enabled) continue;
                var m = toGate * mf.transform.localToWorldMatrix; var v = mf.sharedMesh.vertices;
                for (int i = 0; i < v.Length; i++) { v[i] = m.MultiplyPoint3x4(v[i]); d.archZMin = Mathf.Min(d.archZMin, v[i].z); d.archZMax = Mathf.Max(d.archZMax, v[i].z); }
                grid.Raster(masonry, v, mf.sharedMesh.triangles); archMeshes++;
            }
            if (archMeshes == 0) { d.error = site.arch + " has no rendered mesh"; return d; }
            var open = grid.Flood(masonry, 0f, f.seedY, out bool escaped, out d.open);
            if (open == null) { d.error = "the seed (0, " + BuildingAudit308.F(f.seedY) + ") is inside the masonry projection"; return d; }
            if (escaped) { d.error = "the arch opening is not enclosed by " + site.arch + " within x ±" + BuildingAudit308.F(f.halfWidth) + ", y " + BuildingAudit308.F(f.top); return d; }

            // leaves (closed pose) in hinge space for the swing, in gate space for the coverage
            var leaves = new List<GateLeaf308>();
            foreach (var hinge in new[] { left, right })
            {
                var leaf = new GateLeaf308 { hinge = hinge, sign = hinge == left ? -1f : 1f, closed = hinge == left ? leftClosed : rightClosed };
                var hingeToGate = Matrix4x4.TRS(hinge.localPosition, hinge.localRotation, hinge.localScale);
                foreach (var mf in hinge.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var toHinge = GateToAncestor(mf.transform, hinge); var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                    for (int i = 0; i < v.Length; i++) v[i] = toHinge.MultiplyPoint3x4(v[i]);
                    leaf.parts.Add((v, t));
                    var g = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++) g[i] = hingeToGate.MultiplyPoint3x4(v[i]);
                    grid.Raster(leafMask, g, t);
                }
                if (leaf.parts.Count == 0) { d.error = "hinge " + hinge.name + " carries no leaf mesh"; return d; }
                leaves.Add(leaf);
            }

            // fixed infill above the leaves
            var holder = gate.Find(ctx.g.names.holder);
            d.layer = gate.gameObject.layer;
            if (holder != null)
                foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    d.layer = mf.gameObject.layer;
                    var m = GateToAncestor(mf.transform, gate); var v = mf.sharedMesh.vertices;
                    for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                    grid.Raster(fill, v, mf.sharedMesh.triangles);
                }

            // the frame that is already there (verify / second run)
            bool[] childMask = null; List<GateRect308> childRects = null;
            var child = gate.Find(f.name);
            if (child != null)
            {
                childMask = grid.New(); childRects = new List<GateRect308>(); d.missChild = 0;
                foreach (var mf in child.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var m = GateToAncestor(mf.transform, gate); var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                    for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                    grid.Raster(childMask, v, t);
                    childRects.AddRange(GateRectsOfMesh(v, t));
                }
                foreach (var cr in childRects)
                {
                    cr.sweep = GateSweepMin(leaves, cr, f.sweepMargin, d.openDegrees, f.sweepStep);
                    if (!float.IsInfinity(cr.sweep)) d.childClear = Mathf.Min(d.childClear, cr.sweep - cr.z1);
                }
            }

            // gap cells: open, not covered by a leaf or the infill; split at the leaf edges of the seed row
            int js = grid.Row(f.seedY), iL = -1, iR = -1;
            for (int i = 0; i < grid.nx; i++) if (open[js * grid.nx + i] && leafMask[js * grid.nx + i]) { if (iL < 0) iL = i; iR = i; }
            if (iL < 0) { d.error = "the leaves do not cover the opening at y " + BuildingAudit308.F(f.seedY); return d; }
            float half = f.cell * .5f;
            d.leafL = grid.X(iL) - half; d.leafR = grid.X(iR) + half;
            float upY0 = float.PositiveInfinity, upY1 = float.NegativeInfinity, leftX1 = float.NegativeInfinity, rightX0 = float.PositiveInfinity, sideY1 = float.NegativeInfinity;
            for (int j = 0; j < grid.ny; j++)
                for (int i = 0; i < grid.nx; i++)
                {
                    int k = j * grid.nx + i;
                    if (!open[k] || leafMask[k] || fill[k]) continue;
                    d.missBefore++;
                    if (childMask != null && !childMask[k]) d.missChild++;
                    float x = grid.X(i), y = grid.Y(j);
                    if (x < d.leafL) { d.missLeft++; leftX1 = Mathf.Max(leftX1, x + half); sideY1 = Mathf.Max(sideY1, y + half); }
                    else if (x > d.leafR) { d.missRight++; rightX0 = Mathf.Min(rightX0, x - half); sideY1 = Mathf.Max(sideY1, y + half); }
                    else { d.missUpper++; upY0 = Mathf.Min(upY0, y - half); upY1 = Mathf.Max(upY1, y + half); }
                }
            d.upperY0 = upY0; d.upperY1 = upY1; d.sideTop = sideY1;

            GateLeafMaterial(left, out var material, out d.uvScale);
            if (!string.IsNullOrEmpty(f.material)) material = AssetDatabase.LoadAssetAtPath<Material>(f.material);
            d.materialPath = material != null ? AssetDatabase.GetAssetPath(material) : "";
            if (d.missBefore == 0) return d;

            // header over the top gap, posts over the jamb gaps; ends run into the masonry (embed), edges overlap leaf and infill
            float postTop;
            if (d.missUpper > 0)
            {
                float hy0 = upY0 - f.overlapY, hy1 = upY1 + f.overlapTop;
                if (hy1 - hy0 > f.maxHeader) { d.error = "the gap between the leaves and the infill spans y " + BuildingAudit308.F(upY0) + ".." + BuildingAudit308.F(upY1) + " (header > " + BuildingAudit308.F(f.maxHeader) + " m): not the lintel gap this step repairs"; return d; }
                float hw = grid.MaxAbsX(open, hy0, hy1);
                d.rects.Add(new GateRect308 { part = "header", x0 = -(hw + f.embed), x1 = hw + f.embed, y0 = hy0, y1 = hy1 });
                postTop = hy0;
            }
            else postTop = sideY1 + f.overlapTop;
            float hwPost = grid.MaxAbsX(open, f.floor, postTop);
            if (d.missLeft > 0) d.rects.Add(new GateRect308 { part = "post-left", x0 = -(hwPost + f.embed), x1 = leftX1 + f.overlapX, y0 = -f.sink, y1 = postTop });
            if (d.missRight > 0) d.rects.Add(new GateRect308 { part = "post-right", x0 = rightX0 - f.overlapX, x1 = hwPost + f.embed, y0 = -f.sink, y1 = postTop });

            for (int j = 0; j < grid.ny; j++)
                for (int i = 0; i < grid.nx; i++)
                {
                    int k = j * grid.nx + i;
                    if (!open[k] || leafMask[k] || fill[k]) continue;
                    float x = grid.X(i), y = grid.Y(j);
                    if (!d.rects.Any(r => x >= r.x0 && x <= r.x1 && y >= r.y0 && y <= r.y1)) d.missAfter++;
                }

            // depth, per part: the back face stops just outside what the leaves sweep inside that part's own footprint, so the
            // header closes on the leaf tops (a plate a hand's width in front of them shows sky to anyone who looks up at the
            // slit from near the gate); all parts share the outer face.
            d.sweepMin = float.PositiveInfinity;
            foreach (var r in d.rects) { r.sweep = GateSweepMin(leaves, r, f.sweepMargin, d.openDegrees, f.sweepStep); d.sweepMin = Mathf.Min(d.sweepMin, r.sweep); }
            if (float.IsInfinity(d.sweepMin)) { d.error = "no leaf geometry meets the frame footprint (cannot place the frame depth)"; return d; }
            float backMin = float.PositiveInfinity, backMax = float.NegativeInfinity;
            foreach (var r in d.rects)
            {
                if (float.IsInfinity(r.sweep)) r.sweep = d.sweepMin;
                r.z1 = r.sweep - f.clearance; backMin = Mathf.Min(backMin, r.z1); backMax = Mathf.Max(backMax, r.z1);
            }
            d.zFront = backMin - f.thickness;
            foreach (var r in d.rects) r.z0 = d.zFront;
            if (d.zFront <= d.archZMin || backMax >= d.archZMax) { d.error = "the frame depth z[" + BuildingAudit308.F(d.zFront) + ", " + BuildingAudit308.F(backMax) + "] leaves the masonry z[" + BuildingAudit308.F(d.archZMin) + ", " + BuildingAudit308.F(d.archZMax) + "]"; return d; }
            return d;
        }

        static bool GateSameBounds(Bounds a, Bounds b, float tolerance) => Vector3.Distance(a.min, b.min) <= tolerance && Vector3.Distance(a.max, b.max) <= tolerance;

        static StepPlan308 GatePlanFrame(GateCtx308 ctx)
        {
            var f = ctx.g.frame;
            var sp = new StepPlan308 { step = "G2", target = f.name };
            string folder = f.meshFolder.TrimEnd('/'); int gates = 0;
            foreach (var site in ctx.g.gates)
            {
                var gate = BuildingAudit308.Resolve(ctx.scene, site.gate);
                if (gate == null) { sp.notes.Add(site.id + ": gate not in this scene"); continue; }
                gates++;
                string key = BuildingAudit308.KeyOf(gate);
                var d = GateMeasure(ctx, site, gate); ctx.frames[key] = d;
                if (d.error.Length > 0) { sp.status = "blocked"; sp.notes.Add(site.id + ": " + d.error); continue; }
                string path = folder + "/" + f.name + "_" + site.id + ".asset";
                string gap = d.missBefore + " gap cells (" + BuildingAudit308.F(d.missBefore * d.cell * d.cell, "F2") + " m²: top " + d.missUpper + (d.missUpper > 0 ? " y " + BuildingAudit308.F(d.upperY0, "F2") + ".." + BuildingAudit308.F(d.upperY1, "F2") : "") + ", left jamb " + d.missLeft + ", right jamb " + d.missRight + ")";
                var child = gate.Find(f.name);
                if (child != null)
                {
                    var cm = child.GetComponent<MeshFilter>();
                    var c = new Change308 { step = "G2", key = key, kind = "frame", after = MeshState(cm), at = gate.position, state = "already", note = d.missChild + " gap cells with the frame" };
                    if (cm == null || cm.sharedMesh == null || AssetDatabase.GetAssetPath(cm.sharedMesh) != path) { c.state = "mismatch"; sp.notes.Add(site.id + ": " + f.name + " exists with another mesh (" + c.after + ")"); }
                    else if (d.missChild != 0) { c.state = "mismatch"; sp.notes.Add(site.id + ": " + f.name + " is in place but " + d.missChild + " gap cells remain"); }
                    else sp.notes.Add(site.id + ": " + f.name + " in place, 0 gap cells (without it " + d.missBefore + ")");
                    sp.changes.Add(c);
                    continue;
                }
                if (d.missBefore == 0) { sp.notes.Add(site.id + ": no gap between leaves, infill and arch"); continue; }
                var make = new Change308 { step = "G2", key = key, kind = "frame", after = path + "#" + Path.GetFileNameWithoutExtension(path), at = gate.position, state = "apply", note = d.materialPath + "|" + d.layer };
                if (d.missAfter != 0) { make.state = "blocked"; sp.notes.Add(site.id + ": the frame would leave " + d.missAfter + " gap cells"); }
                else if (string.IsNullOrEmpty(d.materialPath)) { make.state = "blocked"; sp.notes.Add(site.id + ": the door material is not an asset (set frame.material)"); }
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    if (!GateSameBounds(existing.bounds, d.Bounds(), f.matchTolerance)) { make.state = "mismatch"; sp.notes.Add(site.id + ": " + path + " exists with other bounds than this scene measures (" + existing.bounds.min + ".." + existing.bounds.max + " vs " + d.Bounds().min + ".." + d.Bounds().max + ")"); }
                }
                else if (make.state == "apply") sp.changes.Add(new Change308 { step = "G2", key = key, kind = "create-mesh", after = path, at = gate.position, state = "apply", note = "frame:" + site.id });
                sp.changes.Add(make);
                sp.notes.Add(site.id + ": " + gap + " -> 0 with " + d.Text() + "; leaf swing 0.." + BuildingAudit308.F(d.openDegrees, "F0") + "° reaches z " + string.Join(" / ", d.rects.Select(r => BuildingAudit308.F(r.sweep))) + " in the footprints (clearance " + BuildingAudit308.F(f.clearance) + ")");
            }
            if (gates == 0) sp.status = "absent";
            sp.detail = f.name + " (header + jamb posts on the outer face, render only)";
            return sp;
        }

        // ------------------------------------------------------------------ G2 mesh

        static void GateQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float uvScale)
        {
            int k = v.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                v.Add(p); n.Add(normal);
                uv.Add((Mathf.Abs(normal.z) > .5f ? new Vector2(p.x, p.y) : Mathf.Abs(normal.x) > .5f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.z)) * uvScale);
            }
            // Unity front face: Cross(b - a, c - a) points along the normal
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0) { t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3); }
            else { t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2); }
        }

        static Mesh GateFrameMesh(GateFrameDesign308 d)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            foreach (var r in d.rects)
            {
                Vector3 lo = new Vector3(r.x0, r.y0, r.z0), hi = new Vector3(r.x1, r.y1, r.z1);
                Vector3 P(int ix, int iy, int iz) => new Vector3(ix == 0 ? lo.x : hi.x, iy == 0 ? lo.y : hi.y, iz == 0 ? lo.z : hi.z);
                GateQuad(v, n, uv, t, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), Vector3.back, d.uvScale);
                GateQuad(v, n, uv, t, P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), P(0, 0, 1), Vector3.forward, d.uvScale);
                GateQuad(v, n, uv, t, P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), P(0, 0, 0), Vector3.left, d.uvScale);
                GateQuad(v, n, uv, t, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), Vector3.right, d.uvScale);
                GateQuad(v, n, uv, t, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), Vector3.up, d.uvScale);
                GateQuad(v, n, uv, t, P(0, 0, 1), P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), Vector3.down, d.uvScale);
            }
            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ G3 arch contact faces

        internal sealed class GateArchFix308 { public string error = ""; public int pushed, positions, moved; public float area, residual; public Vector3[] vertices = Array.Empty<Vector3>(); }
        sealed class GateSoup308 { public Vector3[] a, b, c, n; public float[] area; }

        static GateSoup308 GateSoup(Vector3[] v, int[] t, Matrix4x4 m)
        {
            int count = t.Length / 3;
            var s = new GateSoup308 { a = new Vector3[count], b = new Vector3[count], c = new Vector3[count], n = new Vector3[count], area = new float[count] };
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
            for (int j = 0; j < count; j++)
            {
                s.a[j] = w[t[j * 3]]; s.b[j] = w[t[j * 3 + 1]]; s.c[j] = w[t[j * 3 + 2]];
                var n = Vector3.Cross(s.b[j] - s.a[j], s.c[j] - s.a[j]); float l = n.magnitude;
                s.area[j] = l * .5f; s.n[j] = l > 1e-12f ? n / l : Vector3.zero;
            }
            return s;
        }

        static bool GateInTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            int ax = Mathf.Abs(n.x) > Mathf.Abs(n.y) ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2) : (Mathf.Abs(n.y) > Mathf.Abs(n.z) ? 1 : 2);
            Vector2 P(Vector3 q) => ax == 0 ? new Vector2(q.y, q.z) : ax == 1 ? new Vector2(q.x, q.z) : new Vector2(q.x, q.y);
            Vector2 p2 = P(p), a2 = P(a), b2 = P(b), c2 = P(c);
            float d = (b2.x - a2.x) * (c2.y - a2.y) - (b2.y - a2.y) * (c2.x - a2.x);
            if (Mathf.Abs(d) < 1e-12f) return false;
            float u = ((p2.x - a2.x) * (c2.y - a2.y) - (p2.y - a2.y) * (c2.x - a2.x)) / d;
            float w = ((b2.x - a2.x) * (p2.y - a2.y) - (b2.y - a2.y) * (p2.x - a2.x)) / d;
            return u >= -1e-4f && w >= -1e-4f && u + w <= 1f + 1e-4f;
        }

        // the audit's C4 pairing with tolerances instead of buckets (a superset of it): same plane within distance, parallel or
        // anti-parallel within normalTol, the smaller triangle's centroid inside the larger one. Returns the paired area.
        static float GatePairs(GateSoup308 r, GateSoup308 o, float distance, float normalTol, HashSet<int> paired)
        {
            float total = 0;
            for (int j = 0; j < o.area.Length; j++)
            {
                if (o.area[j] * 2f < 1e-5f) continue;
                for (int i = 0; i < r.area.Length; i++)
                {
                    if (r.area[i] * 2f < 1e-5f) continue;
                    if (Mathf.Abs(Vector3.Dot(r.n[i], o.n[j])) < 1f - normalTol) continue;
                    if (Mathf.Abs(Vector3.Dot(o.a[j] - r.a[i], r.n[i])) > distance || Mathf.Abs(Vector3.Dot(o.b[j] - r.a[i], r.n[i])) > distance || Mathf.Abs(Vector3.Dot(o.c[j] - r.a[i], r.n[i])) > distance) continue;
                    bool small = r.area[i] <= o.area[j];
                    bool hit = small ? GateInTri((r.a[i] + r.b[i] + r.c[i]) / 3f, o.a[j], o.b[j], o.c[j], o.n[j]) : GateInTri((o.a[j] + o.b[j] + o.c[j]) / 3f, r.a[i], r.b[i], r.c[i], r.n[i]);
                    if (!hit) continue;
                    total += small ? r.area[i] : o.area[j];
                    paired?.Add(j);
                }
            }
            return total;
        }

        // Faces of `other` that lie in a face plane of `reference` are pushed arch.offset along their own normal: a pier bottom goes
        // below the ground plane, a parapet underside sinks into the deck it stands on, an end plate stands proud of the block.
        // Every vertex at a pushed position moves with it (welded by position), so the shell stays closed. Metres are taken in
        // the common parent's frame from the exact local TRS (no world round trip).
        static GateArchFix308 GateArchOffset(Transform reference, Transform other, Mesh otherMesh, GateArch308 a)
        {
            var fix = new GateArchFix308();
            var rf = reference.GetComponent<MeshFilter>();
            if (rf == null || rf.sharedMesh == null) { fix.error = a.reference + " has no mesh"; return fix; }
            if (reference.parent == null || reference.parent != other.parent) { fix.error = a.reference + " and " + other.name + " do not share a parent"; return fix; }
            var scale = Matrix4x4.Scale(reference.parent.lossyScale);
            var mr = scale * Matrix4x4.TRS(reference.localPosition, reference.localRotation, reference.localScale);
            var mo = scale * Matrix4x4.TRS(other.localPosition, other.localRotation, other.localScale);
            var rs = GateSoup(rf.sharedMesh.vertices, rf.sharedMesh.triangles, mr);
            var ov = otherMesh.vertices; var ot = otherMesh.triangles;
            var os = GateSoup(ov, ot, mo);
            var paired = new HashSet<int>();
            fix.area = GatePairs(rs, os, a.distance, a.normalTol, paired);
            fix.pushed = paired.Count; fix.vertices = ov;
            if (paired.Count == 0) return fix;
            var metres = new Vector3[ov.Length];
            for (int i = 0; i < ov.Length; i++) metres[i] = mo.MultiplyPoint3x4(ov[i]);
            (int, int, int) Key(Vector3 p) => (Mathf.RoundToInt(p.x / a.weld), Mathf.RoundToInt(p.y / a.weld), Mathf.RoundToInt(p.z / a.weld));
            var offsets = new Dictionary<(int, int, int), List<Vector3>>();
            foreach (int j in paired)
                for (int k = 0; k < 3; k++)
                {
                    var key = Key(metres[ot[j * 3 + k]]);
                    if (!offsets.TryGetValue(key, out var list)) offsets[key] = list = new List<Vector3>();
                    if (!list.Any(m => Vector3.Dot(m, os.n[j]) > .999f)) list.Add(os.n[j]);
                }
            fix.positions = offsets.Count;
            var back = mo.inverse; var moved = new Vector3[ov.Length];
            for (int i = 0; i < ov.Length; i++)
            {
                if (!offsets.TryGetValue(Key(metres[i]), out var list)) { moved[i] = ov[i]; continue; }
                var sum = Vector3.zero; foreach (var m in list) sum += m;
                moved[i] = back.MultiplyPoint3x4(metres[i] + sum * a.offset); fix.moved++;
            }
            fix.vertices = moved;
            fix.residual = GatePairs(rs, GateSoup(moved, ot, mo), a.distance, a.normalTol, null);
            return fix;
        }

        static bool GateSameVertices(Mesh mesh, Vector3[] expected, float tolerance) => mesh.vertexCount == expected.Length && GateSameVertices(mesh.vertices, expected, tolerance);
        static bool GateSameVertices(Vector3[] v, Vector3[] expected, float tolerance)
        {
            if (v.Length != expected.Length) return false;
            for (int i = 0; i < v.Length; i++) if (Vector3.Distance(v[i], expected[i]) > tolerance) return false;
            return true;
        }

        static StepPlan308 GatePlanArch(GateCtx308 ctx)
        {
            var a = ctx.g.arch;
            var sp = new StepPlan308 { step = "G3", target = a.reference + " x " + string.Join(", ", a.others) };
            string folder = a.meshFolder.TrimEnd('/'); var planned = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase); int arches = 0;
            float limit = ctx.cfg.thresholds.c4CoplanarArea;
            foreach (var site in ctx.g.gates.Concat(a.extra))
            {
                var arch = GateArchOf(ctx, site);
                var reference = arch != null ? arch.Find(a.reference) : null;
                if (reference == null) { sp.notes.Add(site.id + ": no " + site.arch + "/" + a.reference + " in this scene"); continue; }
                arches++;
                foreach (var name in a.others)
                {
                    var t = arch.Find(name); var mf = t != null ? t.GetComponent<MeshFilter>() : null;
                    if (mf == null || mf.sharedMesh == null) { sp.notes.Add(site.id + "/" + name + ": absent"); continue; }
                    string key = BuildingAudit308.KeyOf(t); var src = mf.sharedMesh; string srcPath = AssetDatabase.GetAssetPath(src);
                    var fix = GateArchOffset(reference, t, src, a);
                    if (fix.error.Length > 0) { sp.status = "blocked"; sp.notes.Add(site.id + "/" + name + ": " + fix.error); continue; }
                    if (srcPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var done = new Change308 { step = "G3", key = key, kind = "mesh", after = MeshState(mf), at = t.position, state = "already", note = BuildingAudit308.F(fix.area, "F2") + " m² coplanar with " + a.reference };
                        if (fix.area > limit) { done.state = "mismatch"; sp.notes.Add(site.id + "/" + name + ": the offset mesh is in place but " + BuildingAudit308.F(fix.area, "F2") + " m² is still coplanar"); }
                        sp.changes.Add(done);
                        continue;
                    }
                    if (fix.pushed == 0) { sp.notes.Add(site.id + "/" + name + ": no face coplanar with " + a.reference); continue; }
                    string path = folder + "/" + src.name + a.suffix + ".asset";
                    var swap = new Change308 { step = "G3", key = key, kind = "mesh", before = MeshState(mf), after = path + "#" + Path.GetFileNameWithoutExtension(path), at = t.position, state = "apply", note = fix.pushed + " faces / " + fix.moved + " vertices pushed " + BuildingAudit308.F(a.offset) + " m; coplanar " + BuildingAudit308.F(fix.area, "F2") + " -> " + BuildingAudit308.F(fix.residual, "F2") + " m²" };
                    if (fix.residual > limit) { swap.state = "blocked"; sp.notes.Add(site.id + "/" + name + ": " + BuildingAudit308.F(fix.residual, "F2") + " m² would stay coplanar after the push"); }
                    if (string.IsNullOrEmpty(srcPath)) { swap.state = "blocked"; sp.notes.Add(site.id + "/" + name + ": the mesh is not an asset (cannot be restored by a revert)"); }
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing != null)
                    {
                        if (!GateSameVertices(existing, fix.vertices, 1e-4f)) { swap.state = "mismatch"; sp.notes.Add(site.id + "/" + name + ": " + path + " exists but differs from this scene's result"); }
                    }
                    else if (planned.TryGetValue(path, out var first))
                    {
                        // one asset per source mesh name: another gate shares it only when its own result is the same mesh
                        if (!GateSameVertices(first, fix.vertices, 1e-4f)) { swap.state = "blocked"; sp.notes.Add(site.id + "/" + name + ": " + path + " is planned from another arch with a different result (another source mesh of the same name, or another scale)"); }
                    }
                    else if (swap.state == "apply") { planned[path] = fix.vertices; sp.changes.Add(new Change308 { step = "G3", key = key, kind = "create-mesh", after = path, at = t.position, state = "apply", note = "arch:" + srcPath + "#" + src.name }); }
                    sp.changes.Add(swap);
                    sp.notes.Add(site.id + "/" + name + " (" + src.name + "): " + swap.note);
                }
            }
            if (arches == 0) sp.status = "absent";
            sp.detail = "arch shells: coplanar contact faces pushed " + BuildingAudit308.F(a.offset) + " m (MeshFilter only, colliders keep the source mesh)";
            return sp;
        }

        // ------------------------------------------------------------------ mesh copies

        static void GateCopyUvs(Mesh src, Mesh dst, int[] order)
        {
            for (int ch = 0; ch < 8; ch++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
                if (!src.HasVertexAttribute(attribute)) continue;
                int dim = src.GetVertexAttributeDimension(attribute);
                if (dim <= 2) { var uv = new List<Vector2>(); src.GetUVs(ch, uv); if (uv.Count == src.vertexCount) dst.SetUVs(ch, order == null ? uv : order.Select(i => uv[i]).ToList()); }
                else if (dim == 3) { var uv = new List<Vector3>(); src.GetUVs(ch, uv); if (uv.Count == src.vertexCount) dst.SetUVs(ch, order == null ? uv : order.Select(i => uv[i]).ToList()); }
                else { var uv = new List<Vector4>(); src.GetUVs(ch, uv); if (uv.Count == src.vertexCount) dst.SetUVs(ch, order == null ? uv : order.Select(i => uv[i]).ToList()); }
            }
        }

        // the source mesh with other vertex positions (same triangles, normals, tangents, colours and uv sets)
        static Mesh GateCopyMesh(Mesh src, Vector3[] vertices)
        {
            if (vertices.Length != src.vertexCount) throw new InvalidOperationException("vertex count differs from " + src.name);
            var mesh = new Mesh { indexFormat = src.indexFormat };
            mesh.SetVertices(vertices);
            var n = src.normals; if (n != null && n.Length == vertices.Length) mesh.SetNormals(n);
            var tg = src.tangents; if (tg != null && tg.Length == vertices.Length) mesh.SetTangents(tg);
            var col = src.colors32; if (col != null && col.Length == vertices.Length) mesh.SetColors(col);
            GateCopyUvs(src, mesh, null);
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);
            mesh.RecalculateBounds();
            return mesh;
        }

        // one submesh of the source as its own mesh (only the vertices that submesh uses)
        static Mesh GateSubmeshMesh(Mesh src, int keep)
        {
            var tris = src.GetTriangles(keep);
            var map = new Dictionary<int, int>(); var order = new List<int>(); var remapped = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                if (!map.TryGetValue(tris[i], out int k)) { k = order.Count; map[tris[i]] = k; order.Add(tris[i]); }
                remapped[i] = k;
            }
            var pick = order.ToArray(); int count = src.vertexCount;
            var v = src.vertices; var mesh = new Mesh { indexFormat = pick.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pick.Select(i => v[i]).ToArray());
            var n = src.normals; if (n != null && n.Length == count) mesh.SetNormals(pick.Select(i => n[i]).ToArray());
            var tg = src.tangents; if (tg != null && tg.Length == count) mesh.SetTangents(pick.Select(i => tg[i]).ToArray());
            var col = src.colors32; if (col != null && col.Length == count) mesh.SetColors(pick.Select(i => col[i]).ToArray());
            GateCopyUvs(src, mesh, pick);
            mesh.SetTriangles(remapped, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
