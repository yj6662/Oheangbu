using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Optional world-XZ height delta. Does not replace authored habitat/height/slope inputs.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World/Macro Surface Deformation")]
    public sealed class WorldMacroSurfaceDeformationSO : ScriptableObject
    {
        public Rect WorldXZ;
        public int ResolutionX, ResolutionZ;
        // Grid nodes include both rectangle edges; index is z * ResolutionX + x.
        public float[] HeightOffsets = Array.Empty<float>();

        [Serializable]
        public sealed class TriangleSurfaceData
        {
            public Rect WorldXZ;
            public float BinSize;
            public int BinsX, BinsZ, MaximumTrianglesPerBin;
            // Consecutive triples of world XZ + scalar Y. This SO stores delta Y;
            // WorldMacroFinalSurfaceSO reuses the index with absolute world Y.
            public Vector3[] Vertices = Array.Empty<Vector3>();
            // Optional original world Y, one per vertex. Its presence changes delta queries
            // to highest-final minus highest-original, including protected zero-delta faces.
            public float[] SourceHeights = Array.Empty<float>();
            public float WorldSpaceEdgeTolerance;
            // Only source-aware DELTA fields can omit bins where every face is unchanged.
            // Absolute-height fields retain all faces, including zero-height surfaces.
            public bool DeltaActiveBinsOnly;
            public int ActiveDeformationBins;
            // Prefix offsets: bin i uses TriangleIndices[BinOffsets[i]..BinOffsets[i+1]).
            public int[] BinOffsets = Array.Empty<int>();
            public int[] TriangleIndices = Array.Empty<int>();
        }
        public bool UseTriangleSampling;
        public TriangleSurfaceData TriangleSurface;

        public float SampleDelta(float x, float z) => UseTriangleSampling
            ? SampleTriangleDelta(TriangleSurface, x, z)
            : SampleDelta(WorldXZ, ResolutionX, ResolutionZ, HeightOffsets, x, z);

        /// <summary>Author once; takes ownership of vertices/sourceHeights without copying. Do not mutate afterwards.</summary>
        public void SetTriangles(Vector3[] triangleVertices, float binSize = 16, float[] sourceHeights = null)
        {
            var data = BuildTriangleSurface(triangleVertices, binSize, sourceHeights != null, sourceHeights);
            TriangleSurface = data;
            UseTriangleSampling = true;
        }

        /// <summary>Build once. Legacy delta mode expects a heightfield; source-aware mode resolves overlapping terrain.</summary>
        public static TriangleSurfaceData BuildTriangleSurface(Vector3[] vertices, float binSize = 16, bool includeZeroHeightTriangles = false, float[] sourceHeights = null)
        {
            if (vertices == null || vertices.Length % 3 != 0) throw new ArgumentException("Consecutive triangle vertex triples required.", nameof(vertices));
            if (!Finite(binSize) || binSize <= 0) throw new ArgumentOutOfRangeException(nameof(binSize));
            if (sourceHeights != null)
            {
                if (sourceHeights.Length != vertices.Length) throw new ArgumentException("Source heights must match every triangle vertex.", nameof(sourceHeights));
                foreach (float y in sourceHeights) if (!Finite(y)) throw new ArgumentException("Source heights must be finite.", nameof(sourceHeights));
                includeZeroHeightTriangles = true;
            }
            var data = new TriangleSurfaceData { Vertices = vertices, BinSize = binSize,
                SourceHeights = sourceHeights ?? Array.Empty<float>(), WorldSpaceEdgeTolerance = includeZeroHeightTriangles ? .0001f : 0 };
            if (vertices.Length == 0) return data;
            float minX = vertices[0].x, maxX = minX, minZ = vertices[0].z, maxZ = minZ;
            foreach (var p in vertices)
            {
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new ArgumentException("Triangle vertices must be finite.", nameof(vertices));
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z);
            }
            double width = (double)maxX - minX, depth = (double)maxZ - minZ;
            if (width <= 0 || depth <= 0) return data;
            double nx = Math.Ceiling(width / binSize), nz = Math.Ceiling(depth / binSize);
            const int maximumBins = 1048576, maximumReferences = 32000000, maximumPerBin = 1024;
            if (nx > maximumBins || nz > maximumBins || nx * nz > maximumBins || width > float.MaxValue || depth > float.MaxValue)
                throw new ArgumentException(FormattableString.Invariant($"Triangle field exceeds the packed-bin size limit: reason=total_bins; requested={nx}x{nz}={nx*nz}; maximumBins={maximumBins}; binSize={binSize:R}; worldXZ=({minX:R},{minZ:R})..({maxX:R},{maxZ:R}); triangles={vertices.Length/3}; includeZero={includeZeroHeightTriangles}; sourceAware={sourceHeights!=null}."), nameof(vertices));
            data.WorldXZ = new Rect(minX, minZ, (float)width, (float)depth);
            data.BinsX = (int)nx; data.BinsZ = (int)nz;
            int bins = data.BinsX * data.BinsZ;
            bool[] activeBins = null;
            if (sourceHeights != null)
            {
                data.DeltaActiveBinsOnly = true;
                activeBins = new bool[bins];
                for (int triangle = 0; triangle < vertices.Length / 3; triangle++)
                {
                    // false excludes only all-zero/degenerate delta faces. TriangleBins
                    // uses the same expanded XZ footprint as both packing passes below.
                    if (!TriangleBins(data, triangle, false, out int x0, out int z0, out int x1, out int z1)) continue;
                    for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
                    {
                        int bin = iz * data.BinsX + ix;
                        if (!activeBins[bin]) { activeBins[bin] = true; data.ActiveDeformationBins++; }
                    }
                }
            }
            var counts = new int[bins]; long total = 0;
            for (int triangle = 0; triangle < vertices.Length / 3; triangle++)
            {
                if (!TriangleBins(data, triangle, includeZeroHeightTriangles, out int x0, out int z0, out int x1, out int z1)) continue;
                for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
                {
                    int bin = iz * data.BinsX + ix;
                    // Keep EVERY surface in an active bin, especially zero-delta top
                    // faces. Outside those bins max(final)-max(original) is exactly zero.
                    if (activeBins != null && !activeBins[bin]) continue;
                    int count = ++counts[bin];
                    if (count > maximumPerBin)
                        throw TriangleIndexLimit(data, triangle, ix, iz, count, total, "bin_occupancy", includeZeroHeightTriangles, sourceHeights != null);
                    if (++total > maximumReferences)
                        throw TriangleIndexLimit(data, triangle, ix, iz, count, total, "total_references", includeZeroHeightTriangles, sourceHeights != null);
                    data.MaximumTrianglesPerBin = Math.Max(data.MaximumTrianglesPerBin, count);
                }
            }
            data.BinOffsets = new int[bins + 1];
            for (int bin = 0; bin < bins; bin++) data.BinOffsets[bin + 1] = data.BinOffsets[bin] + counts[bin];
            data.TriangleIndices = new int[(int)total];
            Array.Clear(counts, 0, counts.Length);
            for (int triangle = 0; triangle < vertices.Length / 3; triangle++)
            {
                if (!TriangleBins(data, triangle, includeZeroHeightTriangles, out int x0, out int z0, out int x1, out int z1)) continue;
                for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
                {
                    int bin = iz * data.BinsX + ix;
                    if (activeBins != null && !activeBins[bin]) continue;
                    data.TriangleIndices[data.BinOffsets[bin] + counts[bin]++] = triangle;
                }
            }
            return data;
        }

        static ArgumentException TriangleIndexLimit(TriangleSurfaceData data, int triangle, int ix, int iz,
            int count, long total, string reason, bool includeZero, bool sourceAware)
        {
            int first=triangle*3; var a=data.Vertices[first]; var b=data.Vertices[first+1]; var c=data.Vertices[first+2];
            double x0=(double)data.WorldXZ.xMin+ix*data.BinSize,z0=(double)data.WorldXZ.yMin+iz*data.BinSize;
            float minX=Math.Min(a.x,Math.Min(b.x,c.x)),maxX=Math.Max(a.x,Math.Max(b.x,c.x));
            float minZ=Math.Min(a.z,Math.Min(b.z,c.z)),maxZ=Math.Max(a.z,Math.Max(b.z,c.z));
            return new ArgumentException(FormattableString.Invariant($"Triangle field exceeds the bounded sampling/index budget: reason={reason}; binSize={data.BinSize:R}; bin=({ix},{iz}); binWorldXZ=({x0:R},{z0:R})..({x0+data.BinSize:R},{z0+data.BinSize:R}); occupancy={count}/1024; packedReferences={total}/32000000; bins={data.BinsX}x{data.BinsZ}; triangle={triangle}/{data.Vertices.Length/3}; triangleWorldXZ=({minX:R},{minZ:R})..({maxX:R},{maxZ:R}); triangleY=({a.y:R},{b.y:R},{c.y:R}); includeZero={includeZero}; sourceAware={sourceAware}."), "vertices");
        }

        /// <summary>No allocation or grid fallback. Outside indexed nonzero triangles returns zero.</summary>
        public static float SampleTriangleDelta(TriangleSurfaceData data, float x, float z)
        {
            return TrySampleTriangleValue(data, x, z, out float delta) ? delta : 0;
        }

        /// <summary>Allocation-free scalar query. Source-aware deltas report max(final Y)-max(original Y).</summary>
        public static bool TrySampleTriangleValue(TriangleSurfaceData data, float x, float z, out float value, bool highestValue = false)
        {
            bool found = TrySampleTriangle(data, x, z, out var sample, highestValue);
            value = found ? sample.Value : 0;
            return found;
        }

        [Serializable]
        public struct TriangleSample
        {
            public float Value, BeforeHeight, AfterHeight, MaximumEdgeDistance;
            public int BeforeTriangleIndex, AfterTriangleIndex;
            public Vector3 BeforeBarycentric, AfterBarycentric;
            public bool SourceAware, UsedEdgeTolerance;
        }

        public struct TriangleWeights
        {
            public double A, B, C;
            public float OutsideDistance;
            public bool UsedEdgeTolerance;
        }

        /// <summary>Shared double barycentrics. Edge fallback is closest-triangle world distance, not an area ratio.</summary>
        public static bool TryTriangleWeights(Vector3 a, Vector3 b, Vector3 c, float x, float z,
            out TriangleWeights weights, float worldSpaceTolerance = .0001f)
        {
            weights = new TriangleWeights();
            if (!Finite(x) || !Finite(z) || !Finite(worldSpaceTolerance) || worldSpaceTolerance < 0 ||
                !Finite(a.x) || !Finite(a.z) || !Finite(b.x) || !Finite(b.z) || !Finite(c.x) || !Finite(c.z)) return false;
            double determinant = Determinant(a, b, c);
            if (Math.Abs(determinant) <= 1e-12) return false;
            double u = (((double)b.z - c.z) * ((double)x - c.x) + ((double)c.x - b.x) * ((double)z - c.z)) / determinant;
            double v = (((double)c.z - a.z) * ((double)x - c.x) + ((double)a.x - c.x) * ((double)z - c.z)) / determinant;
            double w = 1 - u - v;
            bool edge = u < 0 || v < 0 || w < 0;
            double edgeDistance = 0;
            if (worldSpaceTolerance > 0)
            {
                if (edge)
                {
                    // Project onto the closest edge, including vertices. Independent
                    // signed-edge tolerances would allow a farther diagonal corner gap.
                    double best = EdgeDistanceSquared(a, b, x, z, out double ab);
                    u = 1 - ab; v = ab; w = 0;
                    double next = EdgeDistanceSquared(b, c, x, z, out double bc);
                    if (next < best) { best = next; u = 0; v = 1 - bc; w = bc; }
                    next = EdgeDistanceSquared(c, a, x, z, out double ca);
                    if (next < best) { best = next; u = ca; v = 0; w = 1 - ca; }
                    edgeDistance = Math.Sqrt(best);
                    if (edgeDistance > Math.Min(.001f, worldSpaceTolerance)) return false;
                }
            }
            else if (u < -1e-7 || v < -1e-7 || w < -1e-7) return false; // Legacy relative tolerance.
            u = Math.Max(0, u); v = Math.Max(0, v); w = Math.Max(0, w);
            double sum = u + v + w;
            weights = new TriangleWeights { A = u / sum, B = v / sum, C = w / sum,
                UsedEdgeTolerance = edge, OutsideDistance = (float)edgeDistance };
            return true;
        }

        /// <summary>Pure winner diagnostics. Exact hits take precedence; <=0.0001m edge fallback is explicit.</summary>
        public static bool TrySampleTriangle(TriangleSurfaceData data, float x, float z, out TriangleSample sample, bool highestValue = false)
        {
            sample = new TriangleSample { BeforeTriangleIndex = -1, AfterTriangleIndex = -1 };
            if (data == null || !Finite(x) || !Finite(z) || !Finite(data.BinSize) || data.BinSize <= 0 ||
                data.BinsX <= 0 || data.BinsZ <= 0 || data.BinOffsets == null || data.TriangleIndices == null || data.Vertices == null ||
                (long)data.BinsX * data.BinsZ + 1 != data.BinOffsets.Length) return false;
            float epsilon = Finite(data.WorldSpaceEdgeTolerance) ? Math.Max(0, Math.Min(.0001f, data.WorldSpaceEdgeTolerance)) : 0;
            if ((double)x < (double)data.WorldXZ.xMin - epsilon || (double)z < (double)data.WorldXZ.yMin - epsilon ||
                (double)x > (double)data.WorldXZ.xMax + epsilon || (double)z > (double)data.WorldXZ.yMax + epsilon) return false;
            bool sourceAware = !highestValue && data.SourceHeights != null && data.SourceHeights.Length > 0;
            if (sourceAware && data.SourceHeights.Length != data.Vertices.Length) return false;
            int ix = Math.Min(data.BinsX - 1, Math.Max(0, (int)(((double)x - data.WorldXZ.xMin) / data.BinSize)));
            int iz = Math.Min(data.BinsZ - 1, Math.Max(0, (int)(((double)z - data.WorldXZ.yMin) / data.BinSize)));
            int bin = iz * data.BinsX + ix, start = data.BinOffsets[bin], end = data.BinOffsets[bin + 1];
            if (start < 0 || end < start || end > data.TriangleIndices.Length || end - start > 1024) return false;
            bool found = false, exact = false;
            double bestBefore = double.NegativeInfinity, bestAfter = double.NegativeInfinity;
            for (int at = start; at < end; at++)
            {
                int triangle = data.TriangleIndices[at];
                if (triangle < 0 || triangle >= data.Vertices.Length / 3) continue;
                int first = triangle * 3;
                var a = data.Vertices[first]; var b = data.Vertices[first + 1]; var c = data.Vertices[first + 2];
                if (!TryTriangleWeights(a, b, c, x, z, out var weights, epsilon)) continue;
                bool edge = weights.UsedEdgeTolerance;
                if (epsilon > 0 && edge && exact) continue;
                double u = weights.A, v = weights.B, w = weights.C, edgeDistance = weights.OutsideDistance;
                double value = u * a.y + v * b.y + w * c.y;
                if (!Finite((float)value)) continue;
                double before = sourceAware ? u * data.SourceHeights[first] + v * data.SourceHeights[first + 1] + w * data.SourceHeights[first + 2] : 0;
                double after = sourceAware ? before + value : value;
                if (!Finite((float)before) || !Finite((float)after)) continue;
                if (epsilon > 0 && !edge && !exact)
                {
                    // A true containing face outranks all nearby edge fallbacks.
                    found = false; bestBefore = bestAfter = double.NegativeInfinity;
                    sample = new TriangleSample { BeforeTriangleIndex = -1, AfterTriangleIndex = -1 };
                    exact = true;
                }
                var barycentric = new Vector3((float)u, (float)v, (float)w);
                if (!found || before > bestBefore)
                {
                    bestBefore = before; sample.BeforeTriangleIndex = triangle; sample.BeforeBarycentric = barycentric;
                }
                if (!found || after > bestAfter)
                {
                    bestAfter = after; sample.AfterTriangleIndex = triangle; sample.AfterBarycentric = barycentric;
                }
                found = true;
                sample.SourceAware = sourceAware; sample.UsedEdgeTolerance = epsilon > 0 && !exact;
                sample.MaximumEdgeDistance = Math.Max(sample.MaximumEdgeDistance, (float)edgeDistance);
                sample.BeforeHeight = (float)bestBefore; sample.AfterHeight = (float)bestAfter;
                sample.Value = (float)(sourceAware ? bestAfter - bestBefore : bestAfter);
                if (!highestValue && !sourceAware) return true;
            }
            return found;
        }

        static double EdgeDistanceSquared(Vector3 a, Vector3 b, float x, float z, out double t)
        {
            double dx = (double)b.x - a.x, dz = (double)b.z - a.z;
            double length = dx * dx + dz * dz;
            t = length > 0 ? Math.Max(0, Math.Min(1, (((double)x - a.x) * dx + ((double)z - a.z) * dz) / length)) : 0;
            double ex = (double)a.x + dx * t - x, ez = (double)a.z + dz * t - z;
            return ex * ex + ez * ez;
        }

        static double Determinant(Vector3 a, Vector3 b, Vector3 c) =>
            ((double)b.z - c.z) * ((double)a.x - c.x) + ((double)c.x - b.x) * ((double)a.z - c.z);

        static bool TriangleBins(TriangleSurfaceData data, int triangle, bool includeZeroHeightTriangles, out int x0, out int z0, out int x1, out int z1)
        {
            int first = triangle * 3; var a = data.Vertices[first]; var b = data.Vertices[first + 1]; var c = data.Vertices[first + 2];
            x0 = z0 = x1 = z1 = 0;
            // Delta mode may omit zero triangles. Absolute-height mode must preserve them as hits.
            if (!includeZeroHeightTriangles && a.y == 0 && b.y == 0 && c.y == 0 || Math.Abs(Determinant(a, b, c)) <= 1e-12) return false;
            double minX = Math.Min(a.x, Math.Min(b.x, c.x)), maxX = Math.Max(a.x, Math.Max(b.x, c.x));
            double minZ = Math.Min(a.z, Math.Min(b.z, c.z)), maxZ = Math.Max(a.z, Math.Max(b.z, c.z));
            double epsilon = data.WorldSpaceEdgeTolerance;
            x0 = Math.Min(data.BinsX - 1, Math.Max(0, (int)((minX - data.WorldXZ.xMin - epsilon) / data.BinSize)));
            x1 = Math.Min(data.BinsX - 1, Math.Max(0, (int)((maxX - data.WorldXZ.xMin + epsilon) / data.BinSize)));
            z0 = Math.Min(data.BinsZ - 1, Math.Max(0, (int)((minZ - data.WorldXZ.yMin - epsilon) / data.BinSize)));
            z1 = Math.Min(data.BinsZ - 1, Math.Max(0, (int)((maxZ - data.WorldXZ.yMin + epsilon) / data.BinSize)));
            return true;
        }

        /// <summary>Pure sampler shared by editor authoring and renderer placement. Outside/invalid data returns zero.</summary>
        public static float SampleDelta(Rect rect, int width, int height, float[] offsets, float x, float z)
        {
            if (!ValidGrid(rect, width, height, offsets) || !Finite(x) || !Finite(z) ||
                x < rect.xMin || z < rect.yMin || x > rect.xMax || z > rect.yMax) return 0;
            double u = ((double)x - rect.xMin) / rect.width * (width - 1);
            double v = ((double)z - rect.yMin) / rect.height * (height - 1);
            int ix = Math.Min(width - 2, Math.Max(0, (int)Math.Floor(u)));
            int iz = Math.Min(height - 2, Math.Max(0, (int)Math.Floor(v)));
            float tx = (float)Math.Min(1, Math.Max(0, u - ix));
            float tz = (float)Math.Min(1, Math.Max(0, v - iz));
            int at = iz * width + ix;
            float a = offsets[at], b = offsets[at + 1], c = offsets[at + width], d = offsets[at + width + 1];
            if (!Finite(a) || !Finite(b) || !Finite(c) || !Finite(d)) return 0;
            // Convex weights avoid overflow when valid finite endpoints span a large range.
            double lower = (1.0 - tx) * a + tx * b, upper = (1.0 - tx) * c + tx * d;
            return (float)((1.0 - tz) * lower + tz * upper);
        }

        /// <summary>Conservative global range, cached by the renderer at ResetCache, never scanned per candidate.</summary>
        public Vector2 GetDeltaRange()
        {
            float minimum = 0, maximum = 0; // Include undeformed/outside points and invalid-sample fallback.
            if (UseTriangleSampling)
            {
                if (TriangleSurface?.Vertices != null)
                    foreach (var point in TriangleSurface.Vertices) if (Finite(point.y)) { minimum = Math.Min(minimum, point.y); maximum = Math.Max(maximum, point.y); }
            }
            else if (ValidGrid(WorldXZ, ResolutionX, ResolutionZ, HeightOffsets))
                foreach (float value in HeightOffsets) if (Finite(value)) { minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); }
            return new Vector2(minimum, maximum);
        }

        /// <summary>Translate only world Y; preserve every rotation/scale/shear/XZ matrix element exactly.</summary>
        public static void TranslateHeight(ref Vector3 position, ref Matrix4x4 matrix, float delta)
        {
            if (delta == 0 || !Finite(delta)) return;
            position.y += delta;
            matrix.m13 += delta;
        }

        static bool ValidGrid(Rect rect, int width, int height, float[] offsets) =>
            width >= 2 && height >= 2 && offsets != null && (long)width * height == offsets.Length &&
            Finite(rect.xMin) && Finite(rect.yMin) && Finite(rect.xMax) && Finite(rect.yMax) &&
            rect.width > 0 && rect.height > 0;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
