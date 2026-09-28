using System;
using UnityEngine;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainForm
    {
        static void ResetBoundarySupport(State s)
        {
            s.boundarySupport.Clear();
            var r = s.receipt;
            r.rawBoundarySamples = r.rawBoundaryOutsideSource = r.rawBoundaryReferenceMissing = r.rawBoundaryRuntimeMissing = 0;
            r.rawBoundaryOutsideBothMissing = r.rawBoundaryMismatches = 0;
            r.maximumGeneratedProbeInset = r.maximumRawBoundaryError = 0;
        }

        // Construct in double precision, then explicitly validate the float XZ that
        // samplers actually receive. An arithmetic midpoint can round outside a
        // sloping projected edge even when its mathematical barycentrics are exact.
        static Vector3 WeightedSupportPoint(Triangle t, double a, double b, double c) =>
            new Vector3((float)(t.a.x * a + t.b.x * b + t.c.x * c),
                (float)(t.a.y * a + t.b.y * b + t.c.y * c),
                (float)(t.a.z * a + t.b.z * b + t.c.z * c));

        static float SupportCoordinateUlp(float value)
        {
            float positive = Mathf.Abs(value);
            int bits = BitConverter.SingleToInt32Bits(positive);
            return BitConverter.Int32BitsToSingle(bits + 1) - positive;
        }
        static bool StrictSourceContains(Triangle t, Vector3 point, bool interior)
        {
            if (!WorldMacroSurfaceDeformationSO.TryTriangleWeights(t.a, t.b, t.c, point.x, point.z, out var w, 0)) return false;
            return !interior || (w.A > 0 && w.B > 0 && w.C > 0);
        }
        static Vector3 InsetGeneratedSupportPoint(Triangle t, double a, double b, double c, bool edge, out float movement)
        {
            var raw = WeightedSupportPoint(t, a, b, c);
            movement = 0;
            if (!edge && StrictSourceContains(t, raw, true)) return raw;
            double cx = ((double)t.a.x + t.b.x + t.c.x) / 3, cz = ((double)t.a.z + t.b.z + t.c.z) / 3;
            double rx = (double)t.a.x * a + t.b.x * b + t.c.x * c, rz = (double)t.a.z * a + t.b.z * b + t.c.z * c;
            double toward = Math.Sqrt((cx - rx) * (cx - rx) + (cz - rz) * (cz - rz));
            double ulp = Math.Max(SupportCoordinateUlp(raw.x), SupportCoordinateUlp(raw.z));
            // Four coordinate ULPs initially; narrow/oblique triangles may require
            // a few bounded doublings. No sampler edge or height tolerance changes.
            double blend = Math.Min(.25, Math.Max(1e-12, ulp * 4 / Math.Max(1e-20, toward)));
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var point = WeightedSupportPoint(t, a + (1.0 / 3 - a) * blend,
                    b + (1.0 / 3 - b) * blend, c + (1.0 / 3 - c) * blend);
                if (StrictSourceContains(t, point, true))
                { movement = Vector2.Distance(XZ(raw), XZ(point)); return point; }
                if (blend >= 1) break;
                blend = Math.Min(1, blend * 2);
            }
            throw new InvalidOperationException("Cannot construct a representable interior support probe for source " + t.source + ", triangle " + t.triangle + ".");
        }

        static void CheckGeneratedSupport(State s, Triangle t, double a, double b, double c, bool edge)
        {
            var raw = WeightedSupportPoint(t, a, b, c);
            if (edge)
            {
                var r = s.receipt;
                r.rawBoundarySamples++;
                bool outsideSource = !StrictSourceContains(t, raw, false);
                if (outsideSource) r.rawBoundaryOutsideSource++;
                var reference = ProbeTriangles(s, raw.x, raw.z);
                bool runtimeHit = WorldMacroSurfaceDeformationSO.TrySampleTriangle(s.foliageField.TriangleSurface, raw.x, raw.z, out var runtime);
                if (!reference.hit) r.rawBoundaryReferenceMissing++;
                if (!runtimeHit) r.rawBoundaryRuntimeMissing++;
                bool outsideBothMissing = outsideSource && !reference.hit && !runtimeHit;
                if (outsideBothMissing) r.rawBoundaryOutsideBothMissing++;
                float error = reference.hit && runtimeHit ? Mathf.Abs(reference.delta - runtime.Value) : 0;
                r.maximumRawBoundaryError = Mathf.Max(r.maximumRawBoundaryError, error);
                bool realDisagreement = reference.hit != runtimeHit || error > .00001f || (!outsideSource && !reference.hit);
                if (realDisagreement)
                {
                    r.rawBoundaryMismatches++;
                    // A representable in-domain hole or sampler disagreement still
                    // enters the normal failing counts; it is never excused as rounding.
                    CheckSupport(s, raw);
                }
                if ((outsideBothMissing || realDisagreement) && s.boundarySupport.Count < 20)
                    s.boundarySupport.Add(SupportDetail(s, outsideBothMissing ? "generated_boundary_outside_source_and_support" : "raw_boundary_sampler_disagreement",
                        raw, reference, runtimeHit ? runtime.Value : 0));
            }
            var interior = InsetGeneratedSupportPoint(t, a, b, c, edge, out float movement);
            s.receipt.maximumGeneratedProbeInset = Mathf.Max(s.receipt.maximumGeneratedProbeInset, movement);
            CheckSupport(s, interior);
        }
    }
}
