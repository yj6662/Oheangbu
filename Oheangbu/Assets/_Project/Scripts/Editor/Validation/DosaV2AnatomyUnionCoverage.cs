using System;
using UnityEngine;
using Capsule = Oheangbu.Presentation.PlayerClothCollisionBudgetRig.CapsuleGeometry;

namespace Oheangbu.EditorTools
{
    // Read-only coverage of actual baked filled triangles. No fitting or source mutation.
    public static class DosaV2AnatomyUnionCoverage
    {
        public const double RoundTripToleranceMeters = .00001;
        [Serializable] public sealed class Result
        {
            public int sourceTriangles, coveredSourceTriangles, uncoveredCornerTriangles, unresolvedSourceTriangles;
            public int provenConvexFragments, subdivisions;
            public double maximumObservedOutsideMeters;
            public bool wholeSurfaceCovered;
            public string scope = "Every original filled triangle must be enclosed by one capsule or recursively partitioned into such convex-contained fragments. Different corner capsules alone are not coverage. Depth9 unresolved fragments fail certification. Radius allowance is the existing10um round-trip instrumentation tolerance, not a fitted/shrunken anatomy surface. Original float positions are promoted once; subdivision and distance arithmetic remain double. Invalid capsule/position/index data is rejected before coverage claims.";
        }
        private readonly struct Point3
        {
            public readonly double X, Y, Z;
            public Point3(Vector3 p) { X = p.x; Y = p.y; Z = p.z; }
            private Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Point3 Midpoint(Point3 a, Point3 b) => new Point3((a.X + b.X) * .5, (a.Y + b.Y) * .5, (a.Z + b.Z) * .5);
        }
        private readonly struct DoubleCapsule
        {
            public readonly double X, Y, Z, DX, DY, DZ, LengthSquared, Radius;
            public DoubleCapsule(Capsule c)
            {
                X = c.Start.x; Y = c.Start.y; Z = c.Start.z;
                DX = (double)c.End.x - c.Start.x; DY = (double)c.End.y - c.Start.y; DZ = (double)c.End.z - c.Start.z;
                LengthSquared = DX * DX + DY * DY + DZ * DZ; Radius = c.Radius;
            }
        }
        public static Result Audit(Vector3[] actualWorld, int[] originalTriangles, Capsule[] fullBody)
        {
            if (actualWorld == null || originalTriangles == null || originalTriangles.Length % 3 != 0 || fullBody == null || fullBody.Length == 0)
                throw new ArgumentException("Complete actual skin, source triangles and full body capsules required.");
            var points = new Point3[actualWorld.Length];
            for (int i = 0; i < actualWorld.Length; i++)
            {
                if (!Finite(actualWorld[i])) throw new ArgumentException("Nonfinite actual anatomy.");
                points[i] = new Point3(actualWorld[i]);
            }
            var capsules = new DoubleCapsule[fullBody.Length];
            for (int i = 0; i < fullBody.Length; i++)
            {
                var capsule = fullBody[i];
                if (!Finite(capsule.Start) || !Finite(capsule.End) || !float.IsFinite(capsule.Radius) || capsule.Radius < 0f)
                    throw new ArgumentException("Finite capsule endpoints and finite nonnegative radii are required.");
                capsules[i] = new DoubleCapsule(capsule);
            }
            foreach (int index in originalTriangles)
                if (index < 0 || index >= points.Length) throw new ArgumentException("Original triangle index outside actual source vertices.");
            var result = new Result { sourceTriangles = originalTriangles.Length / 3 };
            for (int i = 0; i < originalTriangles.Length; i += 3)
            {
                Point3 a = points[originalTriangles[i]], b = points[originalTriangles[i + 1]], c = points[originalTriangles[i + 2]];
                double outside = Math.Max(Outside(a, capsules), Math.Max(Outside(b, capsules), Outside(c, capsules)));
                result.maximumObservedOutsideMeters = Math.Max(result.maximumObservedOutsideMeters, outside);
                if (outside > RoundTripToleranceMeters) { result.uncoveredCornerTriangles++; continue; }
                if (Prove(a, b, c, capsules, 0, result)) result.coveredSourceTriangles++;
                else result.unresolvedSourceTriangles++;
            }
            result.wholeSurfaceCovered = result.coveredSourceTriangles == result.sourceTriangles && result.sourceTriangles > 0;
            return result;
        }
        private static bool Prove(Point3 a, Point3 b, Point3 c, DoubleCapsule[] capsules, int depth, Result result)
        {
            foreach (var capsule in capsules)
                if (Distance(a, capsule) <= RoundTripToleranceMeters && Distance(b, capsule) <= RoundTripToleranceMeters && Distance(c, capsule) <= RoundTripToleranceMeters)
                { result.provenConvexFragments++; return true; }
            if (depth >= 9) return false;
            Point3 ab = Point3.Midpoint(a, b), bc = Point3.Midpoint(b, c), ca = Point3.Midpoint(c, a);
            double outside = Math.Max(Outside(ab, capsules), Math.Max(Outside(bc, capsules), Outside(ca, capsules)));
            result.maximumObservedOutsideMeters = Math.Max(result.maximumObservedOutsideMeters, outside);
            if (outside > RoundTripToleranceMeters) return false;
            result.subdivisions++;
            return Prove(a, ab, ca, capsules, depth + 1, result) && Prove(ab, b, bc, capsules, depth + 1, result)
                && Prove(ca, bc, c, capsules, depth + 1, result) && Prove(ab, bc, ca, capsules, depth + 1, result);
        }
        private static double Outside(Point3 p, DoubleCapsule[] capsules)
        { double best = double.PositiveInfinity; foreach (var c in capsules) best = Math.Min(best, Distance(p, c)); return Math.Max(0, best); }
        private static double Distance(Point3 p, DoubleCapsule c)
        {
            double px = p.X - c.X, py = p.Y - c.Y, pz = p.Z - c.Z;
            double t = c.LengthSquared > 0 ? Math.Max(0, Math.Min(1, (px * c.DX + py * c.DY + pz * c.DZ) / c.LengthSquared)) : 0;
            px -= c.DX * t; py -= c.DY * t; pz -= c.DZ * t;
            return Math.Sqrt(px * px + py * py + pz * pz) - c.Radius;
        }
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
    }
}
