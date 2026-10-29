using System;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 8: the contact tests of the reaction engine, pure (Vector3 in, bool out; horizontal plane like every vol.2 shape, so a fixture and Play run the same code).
    /// A footprint is read as a <see cref="Shape"/> at a moment (a growing fan has its depth at that moment). Disc-disc, disc-fan, disc-ring and ring-ring are exact; fan-fan and
    /// fan-ring are sampled (TE-S8-6); a swept disc (the narrow path) is exact against a disc and sampled against a fan or ring.</summary>
    public static class Vol2FootprintGeometry
    {
        public readonly struct Shape
        {
            public readonly Vol2FootprintKind Kind;
            public readonly Vector3 Origin, Forward;
            public readonly float Radius, Depth, Half, Inner, Outer;
            public Shape(Vol2FootprintKind kind, Vector3 origin, Vector3 forward, float radius, float depth, float half, float inner, float outer)
            { Kind = kind; Origin = origin; Forward = forward; Radius = radius; Depth = depth; Half = half; Inner = inner; Outer = outer; }
            public static Shape OfDisc(Vector3 c, float r) => new Shape(Vol2FootprintKind.Disc, c, Vector3.forward, r, 0f, 0f, 0f, 0f);
            public static Shape OfFan(Vector3 apex, Vector3 forward, float depth, float half) => new Shape(Vol2FootprintKind.Fan, apex, Vol2ShapeGeometry.FlatUnit(forward), 0f, depth, half, 0f, 0f);
            public static Shape OfRing(Vector3 c, float inner, float outer) => new Shape(Vol2FootprintKind.Ring, c, Vector3.forward, 0f, 0f, 0f, inner, outer);
            public static Shape Of(Vol2Footprint f, long nowUs)
            {
                switch (f.Kind)
                {
                    case Vol2FootprintKind.Disc: return OfDisc(f.Origin, f.Radius);
                    case Vol2FootprintKind.Fan: return OfFan(f.Origin, f.Forward, f.FanDepth * f.ScaleAt(nowUs), f.FanHalfAngleRad);
                    case Vol2FootprintKind.Ring: return OfRing(f.Origin, f.RingInner, f.RingOuter);
                    default: return new Shape(Vol2FootprintKind.Attached, f.Origin, Vector3.forward, 0f, 0f, 0f, 0f, 0f);
                }
            }
            public bool Extent => Kind != Vol2FootprintKind.Attached;
        }

        static Vector2 P(Vector3 v) => new Vector2(v.x, v.z);
        static float Flat(Vector3 a, Vector3 b) => (P(a) - P(b)).magnitude;

        /// <summary>Do two footprints (at one moment) overlap on the plane? A body-attached footprint has no extent: never.</summary>
        public static bool Overlaps(Vol2Footprint a, Vol2Footprint b, long nowUs)
        {
            if (a == null || b == null || a.Kind == Vol2FootprintKind.Attached || b.Kind == Vol2FootprintKind.Attached) return false;
            return Overlaps(Shape.Of(a, nowUs), Shape.Of(b, nowUs));
        }

        public static bool Overlaps(in Shape a, in Shape b)
        {
            if (!a.Extent || !b.Extent) return false;
            // cheap reject first: the bounding circles (a fan's is its depth around the apex, a ring's its outer edge)
            if (Flat(a.Origin, b.Origin) > Bound(a) + Bound(b) + 0.05f) return false;
            // order so that the "smaller" kind is first: Disc < Fan < Ring
            if (Rank(a.Kind) > Rank(b.Kind)) return Overlaps(b, a);
            switch (a.Kind)
            {
                case Vol2FootprintKind.Disc:
                    switch (b.Kind)
                    {
                        case Vol2FootprintKind.Disc: return Flat(a.Origin, b.Origin) <= a.Radius + b.Radius;
                        case Vol2FootprintKind.Fan: return Vol2ShapeGeometry.FanHit(b.Origin, b.Forward, b.Half, b.Depth, a.Origin, a.Radius);
                        default: return Vol2ShapeGeometry.RingHit(b.Origin, b.Inner, b.Outer, a.Origin, a.Radius);
                    }
                case Vol2FootprintKind.Fan:
                    if (b.Kind == Vol2FootprintKind.Fan) return FanFan(a, b);
                    return FanRing(a, b);
                default:
                    {
                        // ring - ring: some r1 in [i1, o1], r2 in [i2, o2] with |r1 - r2| <= d <= r1 + r2
                        float d = Flat(a.Origin, b.Origin);
                        return d <= a.Outer + b.Outer && d >= Mathf.Max(0f, Mathf.Max(a.Inner - b.Outer, b.Inner - a.Outer));
                    }
            }
        }

        static float Bound(in Shape s) => s.Kind == Vol2FootprintKind.Disc ? s.Radius : s.Kind == Vol2FootprintKind.Fan ? s.Depth : s.Outer;

        static int Rank(Vol2FootprintKind k) => k == Vol2FootprintKind.Disc ? 0 : k == Vol2FootprintKind.Fan ? 1 : 2;

        const float Eps = 0.02f;

        static void FanPoints(in Shape f, int depthSamples, int angleSamples, Action<Vector3> visit)
        {
            for (int di = 0; di < depthSamples; di++)
            {
                float d = f.Depth * (depthSamples == 1 ? 1f : di / (float)(depthSamples - 1));
                for (int ai = 0; ai < angleSamples; ai++)
                {
                    float t = angleSamples == 1 ? 0.5f : ai / (float)(angleSamples - 1);
                    float ang = Mathf.Lerp(-f.Half, f.Half, t);
                    visit(f.Origin + Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * f.Forward * d);
                }
            }
        }

        static bool FanFan(in Shape a, in Shape b)
        {
            bool hit = false;
            var bb = b; var aa = a;
            FanPoints(a, 7, 11, p => { if (!hit && Vol2ShapeGeometry.FanHit(bb.Origin, bb.Forward, bb.Half, bb.Depth, p, Eps)) hit = true; });
            if (hit) return true;
            FanPoints(b, 7, 11, p => { if (!hit && Vol2ShapeGeometry.FanHit(aa.Origin, aa.Forward, aa.Half, aa.Depth, p, Eps)) hit = true; });
            return hit;
        }

        static bool FanRing(in Shape f, in Shape r)
        {
            bool hit = false;
            var rr = r; var ff = f;
            FanPoints(f, 9, 13, p => { if (!hit && Vol2ShapeGeometry.RingHit(rr.Origin, rr.Inner, rr.Outer, p, Eps)) hit = true; });
            if (hit) return true;
            float mid = (r.Inner + r.Outer) * 0.5f;
            for (int i = 0; i < 48 && !hit; i++)
            {
                float a = i * Mathf.PI * 2f / 48f;
                Vector3 p = r.Origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * mid;
                if (Vol2ShapeGeometry.FanHit(ff.Origin, ff.Forward, ff.Half, ff.Depth, p, (r.Outer - r.Inner) * 0.5f)) hit = true;
            }
            return hit;
        }

        /// <summary>The narrow path of a step (a disc of radius <paramref name="halfWidth"/> swept along <paramref name="axis"/> from <paramref name="fromAlong"/> to <paramref name="toAlong"/>)
        /// against a footprint. entryAlong = how far along the axis the head first touches (clamped to the step).</summary>
        public static bool SweptDiscTouches(Vector3 start, Vector3 axis, float fromAlong, float toAlong, float halfWidth, Vol2Footprint f, long nowUs, float step, out float entryAlong)
        {
            entryAlong = 0f;
            if (f == null || f.Kind == Vol2FootprintKind.Attached) return false;
            if (f.Kind == Vol2FootprintKind.Disc) return Vol2ShapeGeometry.PathStep(start, axis, fromAlong, toAlong, halfWidth, f.Origin, f.Radius, out entryAlong, out float _);
            var shape = Shape.Of(f, nowUs);
            Vector3 dir = Vol2ShapeGeometry.FlatUnit(axis);
            float length = Mathf.Max(0f, toAlong - fromAlong);
            int n = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.02f, step)));
            for (int i = 0; i <= n; i++)
            {
                float along = fromAlong + length * (n == 0 ? 0f : i / (float)n);
                if (Overlaps(Shape.OfDisc(start + dir * along, halfWidth), shape)) { entryAlong = along; return true; }
            }
            return false;
        }

        /// <summary>Does a fan (the shape of a landing cast) touch a footprint?</summary>
        public static bool FanTouches(Vector3 apex, Vector3 forward, float halfAngleRad, float depth, Vol2Footprint f, long nowUs)
            => f != null && f.Kind != Vol2FootprintKind.Attached && Overlaps(Shape.OfFan(apex, forward, depth, halfAngleRad), Shape.Of(f, nowUs));

        /// <summary>Does a ring band touch a footprint?</summary>
        public static bool RingTouches(Vector3 centre, float inner, float outer, Vol2Footprint f, long nowUs)
            => f != null && f.Kind != Vol2FootprintKind.Attached && Overlaps(Shape.OfRing(centre, inner, outer), Shape.Of(f, nowUs));

        /// <summary>A body-attached material is touched when a hostile shape reaches the host (a disc of <paramref name="hostRadius"/> around <paramref name="host"/>).</summary>
        public static bool TouchesHost(Vol2Footprint shape, Vector3 host, float hostRadius, long nowUs)
            => shape != null && shape.Kind != Vol2FootprintKind.Attached && Overlaps(Shape.Of(shape, nowUs), Shape.OfDisc(host, hostRadius));
    }
}
