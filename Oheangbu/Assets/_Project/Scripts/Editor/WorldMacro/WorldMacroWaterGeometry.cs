using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Data.World;
using RiverSpec = Oheangbu.Data.World.WorldMacroSheetSO.RiverSpec;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Water-only authoring. Samples existing Terrain_ MeshColliders without changing geography.
    /// Call ClearCache before a build batch if terrain colliders or river authoring changed.
    /// UV0 is transverse/longitudinal metres; red is displacement, green is terrain depth / 8m,
    /// alpha is end/junction fade. Baked depth does not represent moving occluders.
    /// </summary>
    public static class WorldMacroWaterGeometry
    {
        const float RowSpacing = 8f, BankStep = 4f, EndFade = 25f, JoinLength = 60f, JoinFade = 8f;
        const int CrossCells = 8;
        static readonly RaycastHit[] Hits = new RaycastHit[64];
        static readonly Dictionary<string, Channel> Channels = new Dictionary<string, Channel>();
        static readonly Dictionary<string, BuildReport> Results = new Dictionary<string, BuildReport>();
        static readonly HashSet<string> Building = new HashSet<string>();
        static WorldMacroSheetSO cachedSheet;

        [Serializable]
        public sealed class BuildReport
        {
            public string riverId;
            public int rows, crossCells, vertices, triangles, clippedTransparentTriangles;
            public int terrainRayQueries, rayBufferOverflows, missingTerrainQueries;
            public int invalidCenterRows, dryCenterRows, bankSearchCaps, bankTerrainMissing;
            public int correctedJoinRows, verticesInsideParent;
            public int parentClippedTriangles, junctionCutVertices, removedJunctionSlivers, correctedJunctionWinding;
            public int bakedDepthMissingVertices;
            public float sourceWidthM, minWidthM, maxWidthM, maxRowSpacingM;
            public float maximumJoinCorrectionM, maxBankBracketM, lengthM;
            public float minBakedDepthM, maxBakedDepthM;
            public string scope = "Water-only mesh over existing terrain colliders. Vertex green stores clamped 0-8m static terrain depth; moving occluders are not represented. Navigation, source geography and physics are unchanged.";
        }

        sealed class Row
        {
            public Vector3 position, side;
            public float chainage, left, right;
            public bool valid;
        }

        sealed class Channel
        {
            public RiverSpec river;
            public Row[] rows;
            public float length;
            public Channel parent;
            public BuildReport report;
            public List<CutTriangle> footprint;
        }

        struct WaterVertex
        {
            public Vector3 position;
            public Vector2 uv;
            public Color colour;
            public static WaterVertex Lerp(WaterVertex a, WaterVertex b, float t) => new WaterVertex {
                position = Vector3.Lerp(a.position, b.position, t), uv = Vector2.Lerp(a.uv, b.uv, t),
                colour = Color.Lerp(a.colour, b.colour, t)
            };
        }

        struct CutTriangle
        {
            public Vector3 a, b, c;
            public float minX, maxX, minZ, maxZ, orientation;
            public CutTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                this.a = a; this.b = b; this.c = c;
                minX = Mathf.Min(a.x, b.x, c.x); maxX = Mathf.Max(a.x, b.x, c.x);
                minZ = Mathf.Min(a.z, b.z, c.z); maxZ = Mathf.Max(a.z, b.z, c.z);
                orientation = Mathf.Sign(Cross(b - a, c - a));
            }
            public bool Overlaps(float x0, float x1, float z0, float z1) =>
                maxX >= x0 && minX <= x1 && maxZ >= z0 && minZ <= z1;
        }

        public static BuildReport LastReport { get; private set; }
        public static IReadOnlyDictionary<string, BuildReport> Reports => Results;

        public static void ClearCache()
        {
            Channels.Clear(); Results.Clear(); Building.Clear(); cachedSheet = null; LastReport = null;
        }

        public static Mesh Build(WorldMacroSheetSO sheet, RiverSpec river)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (river == null) throw new ArgumentNullException(nameof(river));
            if (cachedSheet != sheet) { ClearCache(); cachedSheet = sheet; }
            var channel = GetChannel(sheet, river);
            var report = channel.report;
            report.verticesInsideParent = report.clippedTransparentTriangles = 0;
            report.parentClippedTriangles = report.junctionCutVertices = 0;
            report.removedJunctionSlivers = report.correctedJunctionWinding = 0;
            report.bakedDepthMissingVertices = 0;
            report.minBakedDepthM = float.MaxValue; report.maxBakedDepthM = 0;
            int columns = CrossCells + 1, count = channel.rows.Length * columns;
            var vertices = new Vector3[count]; var uv = new Vector2[count]; var colours = new Color[count];
            var triangles = new List<int>(Mathf.Max(0, channel.rows.Length - 1) * CrossCells * 6);

            for (int rowIndex = 0; rowIndex < channel.rows.Length; rowIndex++)
            {
                var row = channel.rows[rowIndex];
                float sourceFade = Smooth(0, EndFade, row.chainage);
                float endFade = channel.parent == null ? Smooth(0, EndFade, channel.length - row.chainage) : 1f;
                for (int column = 0; column < columns; column++)
                {
                    int index = rowIndex * columns + column;
                    float t = column / (float)CrossCells;
                    var position = row.position + row.side * Mathf.Lerp(-row.left, row.right, t);
                    float alpha = row.valid ? sourceFade * endFade : 0f;
                    if (channel.parent != null && ParentSample(channel.parent, position, out float outside, out float parentY))
                    {
                        // Blend into the actual widened parent's water surface before hiding overlap.
                        // A row can follow the parent for longer than the nominal final 60 metres.
                        if (outside < JoinFade)
                        {
                            position.y = Mathf.Lerp(position.y, parentY, 1f - Smooth(0, JoinFade, outside));
                            alpha *= Smooth(0, JoinFade, outside);
                            if (outside <= 0) report.verticesInsideParent++;
                        }
                    }
                    vertices[index] = position;
                    // A bank can move abruptly at a confluence; keep texture phase tied to the centreline.
                    uv[index] = new Vector2(Mathf.Lerp(-row.left, row.right, t), row.chainage);
                    float bankMask = Smooth(0, .24f, Mathf.Min(t, 1f - t));
                    float depth = 0;
                    if (TerrainHeight(position, report, out float terrainY))
                    {
                        depth = Mathf.Clamp(position.y - terrainY, 0f, 8f);
                        report.minBakedDepthM = Mathf.Min(report.minBakedDepthM, depth);
                        report.maxBakedDepthM = Mathf.Max(report.maxBakedDepthM, depth);
                    }
                    else report.bakedDepthMissingVertices++;
                    colours[index] = new Color(bankMask * alpha, depth / 8f, 0, alpha);
                }
            }

            var finalVertices = new List<Vector3>(vertices); var finalUv = new List<Vector2>(uv);
            var finalColours = new List<Color>(colours);
            for (int rowIndex = 0; rowIndex + 1 < channel.rows.Length; rowIndex++)
            {
                if (!channel.rows[rowIndex].valid || !channel.rows[rowIndex + 1].valid) continue;
                for (int column = 0; column < CrossCells; column++)
                {
                    int a = rowIndex * columns + column, b = a + 1, c = a + columns, d = c + 1;
                    Add(a, c, b); Add(b, c, d);
                }
            }
            void Add(int a, int b, int c)
            {
                if (colours[a].a <= .00001f && colours[b].a <= .00001f && colours[c].a <= .00001f)
                { report.clippedTransparentTriangles++; return; }
                if (channel.parent != null)
                {
                    float x0 = Mathf.Min(vertices[a].x, vertices[b].x, vertices[c].x), x1 = Mathf.Max(vertices[a].x, vertices[b].x, vertices[c].x);
                    float z0 = Mathf.Min(vertices[a].z, vertices[b].z, vertices[c].z), z1 = Mathf.Max(vertices[a].z, vertices[b].z, vertices[c].z);
                    List<List<WaterVertex>> pieces = null;
                    bool changed = false;
                    foreach (var cut in channel.parent.footprint)
                    {
                        if (!cut.Overlaps(x0, x1, z0, z1)) continue;
                        if (pieces == null) pieces = new List<List<WaterVertex>> { new List<WaterVertex> {
                            new WaterVertex { position = vertices[a], uv = uv[a], colour = colours[a] },
                            new WaterVertex { position = vertices[b], uv = uv[b], colour = colours[b] },
                            new WaterVertex { position = vertices[c], uv = uv[c], colour = colours[c] }
                        } };
                        var next = new List<List<WaterVertex>>();
                        foreach (var piece in pieces) changed |= SubtractTriangle(piece, cut, next);
                        pieces = next;
                        if (pieces.Count == 0) break;
                    }
                    if (changed)
                    {
                        report.parentClippedTriangles++;
                        foreach (var piece in pieces)
                        {
                            if (piece.Count < 3) continue;
                            int offset = finalVertices.Count;
                            foreach (var value in piece)
                            {
                                var position = value.position;
                                var colour = value.colour;
                                if (ParentSample(channel.parent, position, out float gap, out float parentY) && gap <= .002f)
                                { position.y = parentY; colour.a = 0; colour.r = 0; }
                                if (TerrainHeight(position, report, out float ground))
                                {
                                    colour.g = Mathf.Clamp01((position.y - ground) / 8f);
                                    report.minBakedDepthM = Mathf.Min(report.minBakedDepthM, colour.g * 8f);
                                    report.maxBakedDepthM = Mathf.Max(report.maxBakedDepthM, colour.g * 8f);
                                }
                                else { colour.g = 0; report.bakedDepthMissingVertices++; }
                                finalVertices.Add(position); finalUv.Add(value.uv); finalColours.Add(colour);
                                report.junctionCutVertices++;
                            }
                            for (int i = 1; i + 1 < piece.Count; i++)
                            {
                                int bIndex = offset + i, cIndex = offset + i + 1;
                                float upArea = Vector3.Cross(finalVertices[bIndex] - finalVertices[offset],
                                    finalVertices[cIndex] - finalVertices[offset]).y;
                                // Clip intersections at kilometre coordinates can collapse after float rounding.
                                if (Mathf.Abs(upArea) < .001f) { report.removedJunctionSlivers++; continue; }
                                if (upArea < 0) { int swap = bIndex; bIndex = cIndex; cIndex = swap; report.correctedJunctionWinding++; }
                                triangles.Add(offset); triangles.Add(bIndex); triangles.Add(cIndex);
                            }
                        }
                        return;
                    }
                }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            var mesh = new Mesh { name = "Water_" + river.Id, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(finalVertices); mesh.SetUVs(0, finalUv); mesh.SetColors(finalColours); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            report.vertices = finalVertices.Count; report.triangles = triangles.Count / 3;
            if (report.minBakedDepthM == float.MaxValue) report.minBakedDepthM = 0;
            Results[river.Id] = report; LastReport = report;
            return mesh;
        }

        static Channel GetChannel(WorldMacroSheetSO sheet, RiverSpec river)
        {
            if (string.IsNullOrEmpty(river.Id)) throw new ArgumentException("Water river Id is required.");
            if (Channels.TryGetValue(river.Id, out var cached)) return cached;
            if (!Building.Add(river.Id)) throw new ArgumentException("Cyclic river ParentId: " + river.Id);
            try
            {
                if (river.Points == null || river.Points.Length < 2 || !Finite(river.Width) || river.Width <= 0)
                    throw new ArgumentException("River requires finite points and positive width: " + river.Id);
                var dense = new List<Vector3>();
                for (int i = 0; i + 1 < river.Points.Length; i++)
                {
                    Vector3 a = river.Points[i], b = river.Points[i + 1];
                    if (!Finite(a) || !Finite(b)) throw new ArgumentException("Non-finite water point: " + river.Id);
                    float length = Vector3.Distance(a, b);
                    if (length < .001f) continue;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(length / RowSpacing));
                    for (int k = 0; k < steps; k++) dense.Add(Vector3.Lerp(a, b, k / (float)steps));
                }
                dense.Add(river.Points[river.Points.Length - 1]);
                if (dense.Count < 2) throw new ArgumentException("Degenerate water centreline: " + river.Id);
                var report = new BuildReport { riverId = river.Id, sourceWidthM = river.Width, crossCells = CrossCells, minWidthM = float.MaxValue };
                var channel = new Channel { river = river, rows = new Row[dense.Count], report = report };
                if (!string.IsNullOrEmpty(river.ParentId))
                {
                    RiverSpec parent = Array.Find(sheet.Rivers, item => item != null && item.Id == river.ParentId);
                    if (parent == null) throw new ArgumentException("Missing parent river: " + river.ParentId);
                    channel.parent = GetChannel(sheet, parent);
                }
                float distance = 0;
                for (int i = 0; i < dense.Count; i++)
                {
                    if (i > 0)
                    {
                        float segment = Vector3.Distance(dense[i - 1], dense[i]);
                        distance += segment; report.maxRowSpacingM = Mathf.Max(report.maxRowSpacingM, segment);
                    }
                    var direction = dense[Mathf.Min(dense.Count - 1, i + 1)] - dense[Mathf.Max(0, i - 1)];
                    direction.y = 0;
                    if (direction.sqrMagnitude < .000001f) throw new ArgumentException("Vertical or reversing water segment: " + river.Id);
                    direction.Normalize();
                    channel.rows[i] = new Row { position = dense[i], side = new Vector3(direction.z, 0, -direction.x), chainage = distance };
                }
                channel.length = report.lengthM = distance; report.rows = dense.Count;
                foreach (var row in channel.rows)
                {
                    if (channel.parent != null && distance - row.chainage < JoinLength &&
                        ParentSample(channel.parent, row.position, out _, out float parentY))
                    {
                        float originalY = row.position.y;
                        row.position.y = Mathf.Lerp(originalY, parentY, 1f - Smooth(0, JoinLength, distance - row.chainage));
                        report.maximumJoinCorrectionM = Mathf.Max(report.maximumJoinCorrectionM, Mathf.Abs(originalY - row.position.y));
                        report.correctedJoinRows++;
                    }
                    if (!TerrainHeight(row.position, report, out float centerY))
                    { report.invalidCenterRows++; continue; }
                    if (centerY >= row.position.y - .01f)
                    { report.dryCenterRows++; continue; }
                    row.valid = true;
                    row.left = FindBank(row, -1, river.Width, report);
                    row.right = FindBank(row, 1, river.Width, report);
                    float width = row.left + row.right;
                    report.minWidthM = Mathf.Min(report.minWidthM, width); report.maxWidthM = Mathf.Max(report.maxWidthM, width);
                }
                if (report.minWidthM == float.MaxValue) report.minWidthM = 0;
                channel.footprint = new List<CutTriangle>();
                for (int i = 0; i + 1 < channel.rows.Length; i++)
                {
                    var a = channel.rows[i]; var b = channel.rows[i + 1];
                    if (!a.valid || !b.valid) continue;
                    Vector3 al = a.position - a.side * a.left, ar = a.position + a.side * a.right;
                    Vector3 bl = b.position - b.side * b.left, br = b.position + b.side * b.right;
                    channel.footprint.Add(new CutTriangle(al, bl, ar));
                    channel.footprint.Add(new CutTriangle(ar, bl, br));
                }
                Channels.Add(river.Id, channel);
                return channel;
            }
            finally { Building.Remove(river.Id); }
        }

        static float FindBank(Row row, float sign, float sourceWidth, BuildReport report)
        {
            // Limit reach at tributary intersections; never fill an entire low basin with a single row.
            float maximum = Mathf.Max(36f, sourceWidth * 1.5f), wet = 0f;
            for (float distance = Mathf.Min(BankStep, maximum); ; distance = Mathf.Min(distance + BankStep, maximum))
            {
                if (!TerrainHeight(row.position + row.side * (sign * distance), report, out float ground))
                { report.bankTerrainMissing++; return Mathf.Max(.05f, wet); }
                if (ground >= row.position.y)
                {
                    float dry = distance;
                    for (int i = 0; i < 8; i++)
                    {
                        float mid = (wet + dry) * .5f;
                        if (!TerrainHeight(row.position + row.side * (sign * mid), report, out ground))
                        { report.bankTerrainMissing++; break; }
                        if (ground >= row.position.y) dry = mid; else wet = mid;
                    }
                    report.maxBankBracketM = Mathf.Max(report.maxBankBracketM, dry - wet);
                    return (wet + dry) * .5f;
                }
                wet = distance;
                if (distance >= maximum) { report.bankSearchCaps++; return maximum; }
            }
        }

        static bool TerrainHeight(Vector3 point, BuildReport report, out float height)
        {
            report.terrainRayQueries++;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 4096f, Vector3.down, Hits, 8192f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= Hits.Length) report.rayBufferOverflows++;
            height = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (collider is MeshCollider && collider.name.StartsWith("Terrain_", StringComparison.Ordinal) && Hits[i].normal.y > 0)
                    height = Mathf.Max(height, Hits[i].point.y);
            }
            if (Finite(height)) return true;
            report.missingTerrainQueries++; return false;
        }

        static bool ParentSample(Channel parent, Vector3 position, out float outside, out float waterY)
        {
            outside = float.PositiveInfinity; waterY = position.y;
            float nearest = float.PositiveInfinity; int closest = -1; float fraction = 0;
            for (int i = 0; i + 1 < parent.rows.Length; i++)
            {
                var a = parent.rows[i]; var b = parent.rows[i + 1];
                if (!a.valid || !b.valid) continue;
                float d = WorldMacroTerrain.SegmentDistance(position.x, position.z, a.position, b.position, out float t);
                if (d < nearest) { nearest = d; closest = i; fraction = t; }
            }
            if (closest < 0) return false;
            var first = parent.rows[closest]; var last = parent.rows[closest + 1];
            var center = Vector3.Lerp(first.position, last.position, fraction);
            var side = Vector3.Lerp(first.side, last.side, fraction).normalized;
            float lateral = Vector3.Dot(position - center, side);
            float width = lateral < 0 ? Mathf.Lerp(first.left, last.left, fraction) : Mathf.Lerp(first.right, last.right, fraction);
            outside = nearest - width; waterY = center.y;
            // Reject end-cap extension: nearest segment projection alone would create a radial parent pool.
            if (closest == 0 && fraction <= 0 && Vector3.Dot(position - first.position, last.position - first.position) < 0)
                outside = Mathf.Max(outside, Vector3.Distance(position, first.position));
            if (closest == parent.rows.Length - 2 && fraction >= 1 && Vector3.Dot(position - last.position, last.position - first.position) > 0)
                outside = Mathf.Max(outside, Vector3.Distance(position, last.position));
            float edgeDistance = float.PositiveInfinity;
            for (int i = 0; i < parent.rows.Length - 1; i++)
            {
                var a = parent.rows[i]; var b = parent.rows[i + 1];
                if (!a.valid || !b.valid) continue;
                Vector3 al = a.position - a.side * a.left, ar = a.position + a.side * a.right;
                Vector3 bl = b.position - b.side * b.left, br = b.position + b.side * b.right;
                if (position.x < Mathf.Min(Mathf.Min(al.x, ar.x), Mathf.Min(bl.x, br.x)) - JoinFade ||
                    position.x > Mathf.Max(Mathf.Max(al.x, ar.x), Mathf.Max(bl.x, br.x)) + JoinFade ||
                    position.z < Mathf.Min(Mathf.Min(al.z, ar.z), Mathf.Min(bl.z, br.z)) - JoinFade ||
                    position.z > Mathf.Max(Mathf.Max(al.z, ar.z), Mathf.Max(bl.z, br.z)) + JoinFade) continue;
                if (TriangleHeight(position, al, bl, ar, out float y) || TriangleHeight(position, ar, bl, br, out y))
                { waterY = y; outside = Mathf.Min(outside, 0f); return true; }
                edgeDistance = Mathf.Min(edgeDistance,
                    WorldMacroTerrain.SegmentDistance(position.x, position.z, al, bl, out _),
                    WorldMacroTerrain.SegmentDistance(position.x, position.z, ar, br, out _));
                if (i == 0) edgeDistance = Mathf.Min(edgeDistance, WorldMacroTerrain.SegmentDistance(position.x, position.z, al, ar, out _));
                if (i == parent.rows.Length - 2) edgeDistance = Mathf.Min(edgeDistance, WorldMacroTerrain.SegmentDistance(position.x, position.z, bl, br, out _));
            }
            // Only hide vertices inside an actual parent quad, not inside an approximate curved radius.
            outside = Finite(edgeDistance) ? Mathf.Max(.00001f, edgeDistance) : Mathf.Max(JoinFade, outside);
            return true;
        }

        // Difference of a convex polygon and one actual parent footprint triangle.
        // Alpha at vertices alone cannot remove overlap inside crossing child triangles.
        static bool SubtractTriangle(List<WaterVertex> polygon, CutTriangle cut, List<List<WaterVertex>> output)
        {
            var remaining = polygon;
            var outsidePieces = new List<List<WaterVertex>>();
            Vector3[] points = { cut.a, cut.b, cut.c };
            for (int edgeIndex = 0; edgeIndex < 3 && remaining.Count > 0; edgeIndex++)
            {
                Vector3 start = points[edgeIndex], end = points[(edgeIndex + 1) % 3], edge = end - start;
                float edgeLength = Mathf.Max(.0001f, new Vector2(edge.x, edge.z).magnitude);
                var inside = new List<WaterVertex>(); var outside = new List<WaterVertex>();
                WaterVertex previous = remaining[remaining.Count - 1];
                float previousDistance = cut.orientation * Cross(edge, previous.position - start) / edgeLength;
                foreach (var current in remaining)
                {
                    float currentDistance = cut.orientation * Cross(edge, current.position - start) / edgeLength;
                    bool previousInside = previousDistance >= 0, currentInside = currentDistance >= 0;
                    if (previousInside != currentInside)
                    {
                        var intersection = WaterVertex.Lerp(previous, current, previousDistance / (previousDistance - currentDistance));
                        inside.Add(intersection); outside.Add(intersection);
                    }
                    if (currentInside) inside.Add(current); else outside.Add(current);
                    previous = current; previousDistance = currentDistance;
                }
                if (outside.Count >= 3) outsidePieces.Add(outside);
                remaining = inside;
            }
            float removedArea = 0;
            for (int i = 1; i + 1 < remaining.Count; i++)
                removedArea += Mathf.Abs(Cross(remaining[i].position - remaining[0].position, remaining[i + 1].position - remaining[0].position));
            if (removedArea <= .00001f) { output.Add(polygon); return false; }
            output.AddRange(outsidePieces); return true;
        }

        static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;

        static bool TriangleHeight(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            float bx = b.x - a.x, bz = b.z - a.z, cx = c.x - a.x, cz = c.z - a.z;
            float denominator = bx * cz - bz * cx; y = 0;
            if (Mathf.Abs(denominator) < .00001f) return false;
            float px = p.x - a.x, pz = p.z - a.z;
            float u = (px * cz - pz * cx) / denominator, v = (bx * pz - bz * px) / denominator;
            if (u < -.00001f || v < -.00001f || u + v > 1.00001f) return false;
            y = a.y + (b.y - a.y) * u + (c.y - a.y) * v; return true;
        }

        static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, value));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
