using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 3 (design s5b_design_v1 sections 6 and 14): the frame a contact is judged against. ONE spec per cast, shared by the
    /// judgement now and by the visuals later (the presenter reads the same numbers, never its own). Every geometry number comes from
    /// NumbersV3 (space.*); the few numbers the data does not hold are in <see cref="Vol2ShapeTuning"/> and listed as temporary exceptions.</summary>
    public enum Vol2ShapeKind { Path, Attachment, Fan, Ring }

    /// <summary>TEMPORARY EXCEPTIONS of slice 3 (Exit Blocking): numbers the executors need that NumbersV3 does not have. None is balance of
    /// a spell effect; each is a technical stand-in to be replaced by a data key (or removed) before the vol.2 runtime leaves the lab.
    /// TE-S3-1 PathSpeed, TE-S3-2 BrushReach / SwingRadius, TE-S3-3 DotTickUs, TE-S3-4 PushMeters / PushSeconds / Push* (D308-158), TE-S3-5 ChestHeight.</summary>
    public sealed class Vol2ShapeTuning
    {
        /// <summary>TE-S3-1: how fast the narrow path travels (m/s). The data has only the path length (12 m); 36 m/s covers it in 1/3 s.</summary>
        public float PathSpeed = 36f;
        /// <summary>TE-S3-2: the swing contact volume of the attachment frame: a capsule from the sleeve to the brush tip, this long ...</summary>
        public float BrushReach = 1.6f;
        /// <summary>... and this thick (radius). The data only says "brush shaft and sleeve" (design section 6).</summary>
        public float SwingRadius = 0.45f;
        /// <summary>TE-S3-3: tick interval of a lasting damage (microseconds). The data has the burn total (body.fire.utility) and length (body.fire.duration), not the cadence.</summary>
        public long DotTickUs = 500000;
        /// <summary>TE-S3-4: the earth push (밀침). The data has its value (body.earth.utility) but no distance. D308-158 (SPEC-VOL2-EARTH-PUSH-308, TEST): the user chose a REAL push of
        /// 0.2 - 0.3 m, so the default is 0.25 m over 0.12 s (it was 1.0 m / 0.25 s while it only had to be seen). <see cref="ApplyNumbers"/> reads the numbers rows first.</summary>
        public float PushMeters = 1.0f;      // D308-158 correction: the push already existed at 1.0 m - the user chose "a real push", not a shorter one
        public float PushSeconds = 0.25f;
        /// <summary>D308-158: the share of <see cref="PushMeters"/> a boss gets (EnemyVitals.IsBoss via the coordinator's BossTest). 0 = a boss is not moved (the 군 sentence: "보스는 밀리지 않음").</summary>
        public float PushBossFactor = 0f;
        /// <summary>D308-158: a body whose flat radius is at or above this (m) is "large" and gets <see cref="PushLargeFactor"/> of the distance. The enemy data has no mass, so the body's own radius stands in.</summary>
        public float PushLargeRadius = 1.0f;
        public float PushLargeFactor = 0.5f;
        /// <summary>D308-158 guard (Vol2PushGuard): the ground is probed every PushProbeStep (m) along the push; a step that finds no ground within +/- PushStepHeight of the last one, or a
        /// slope steeper than PushSlopeNormalY (the normal's up part), ends the push there. A wall ray at PushWallHeight above the feet ends it in front of the wall.</summary>
        public float PushProbeStep = 0.125f, PushStepHeight = 0.35f, PushSlopeNormalY = 0.5f, PushWallHeight = 0.5f;

        /// <summary>The numbers keys that override the TE defaults above when the loaded numbers have the row (a data change then needs no code change; the NumbersV3 contract is frozen with
        /// the E0 golden, so there are no such rows today - the same arrangement as the ward life, TE-W1). Used by the coordinator once, at its construction.</summary>
        public const string PushMetersKey = "body.earth.pushMeters", PushSecondsKey = "body.earth.pushSeconds", PushBossFactorKey = "body.earth.pushBossFactor",
            PushLargeRadiusKey = "body.earth.pushLargeRadius", PushLargeFactorKey = "body.earth.pushLargeFactor";

        public void ApplyNumbers(NumbersV3Dto numbers)
        {
            if (numbers == null) return;
            foreach (var row in numbers.Rows)
            {
                if (!row.Value.HasValue) continue;
                float v = (float)row.Value.Value;
                switch (row.Key)
                {
                    case PushMetersKey: PushMeters = Math.Max(0f, v); break;
                    case PushSecondsKey: if (v > 0f) PushSeconds = v; break;
                    case PushBossFactorKey: PushBossFactor = Math.Max(0f, v); break;
                    case PushLargeRadiusKey: if (v > 0f) PushLargeRadius = v; break;
                    case PushLargeFactorKey: PushLargeFactor = Math.Max(0f, v); break;
                }
            }
        }
        /// <summary>TE-S3-5: height above the caster's feet of the path's wall check ray.</summary>
        public float ChestHeight = 1.1f;
        /// <summary>TE-S3-6 (D308-150 V9, TEST switch - the user decides on return): launch a 한 번 path / fan at the swing's apex instead of at the commit, like the old route
        /// (SPEC-SPELL-SWING-308: single 0.28 s, area 0.53 s in the live swing profile). OFF by default; the spell lab scope turns it on (Vol2ShapeRuntime.CreateForScope).
        /// Only the emitter's start moves: the shape, the budget and the damage are the same and the damage still lands at the shape's actual contact.</summary>
        public bool LaunchAtApex;
        /// <summary>TE-S3-6: seconds from the commit to the apex for a 한 번 path (the old single shot) and a 한 번 fan (the old area cast). Filled from the swing profile.</summary>
        public float PathApexSeconds = 0.28f, FanApexSeconds = 0.53f;

        /// <summary>TE-S3-6: the launch delay of a cast of this frame and verb (0 = launch at the commit).</summary>
        public long LaunchDelayUs(Frame frame, Verb verb)
        {
            if (!LaunchAtApex || verb != Verb.Once) return 0;
            float s = frame == Frame.Path ? PathApexSeconds : frame == Frame.Fan ? FanApexSeconds : 0f;
            return s > 0f ? (long)Math.Round(s * 1000000.0) : 0;
        }
    }

    /// <summary>Frame geometry. Horizontal (x, z) plane only: the old area judgement ignores height too ([TEST], AreaGeometry); a body that
    /// jumps is judged by its position on the plane, so a ring or a fan cannot be hopped over. Every number below is measured on the plane.
    /// MEASURING POINTS (design section 14, asserted by name in Vol2ShapeFixture):
    ///  - Path: <see cref="PathWidth"/> is the FULL width of the swept disc's centre line (half-width = PathWidth / 2 each side of the axis),
    ///    <see cref="PathLength"/> is measured from the caster along the aim axis.
    ///  - Fan: a sector with its apex at the caster, radius <see cref="FanDepth"/>; <see cref="FanWidth"/> is the straight chord between the two
    ///    far arc ends (measured at the far edge), so the half-angle is asin((FanWidth / 2) / FanDepth).
    ///  - Ring: a flat annulus centred on the cast position; <see cref="RingRadius"/> is the CENTRE LINE of the band, so the band spans
    ///    RingRadius +/- RingThickness / 2; the inside is empty.</summary>
    public sealed class Vol2ShapeSpec
    {
        public Vol2ShapeKind Kind { get; }
        public Vector3 Origin { get; }
        /// <summary>Flat unit vector of the aim.</summary>
        public Vector3 Forward { get; }
        public float PathWidth { get; }
        public float PathLength { get; }
        public float FanWidth { get; }
        public float FanDepth { get; }
        public float FanHalfAngleRad { get; }
        public float RingRadius { get; }
        public float RingThickness { get; }
        public float RingInner => Mathf.Max(0f, RingRadius - RingThickness * 0.5f);
        public float RingOuter => RingRadius + RingThickness * 0.5f;
        public float BrushReach { get; }
        public float SwingRadius { get; }
        public float PathSpeed { get; }
        public string FanWidthMeasuredAt => "far chord";
        public string RingRadiusMeasuredAt => "band centre line";

        Vol2ShapeSpec(Vol2ShapeKind kind, Vector3 origin, Vector3 forward, NumbersV3Dto n, Vol2ShapeTuning t)
        {
            Kind = kind; Origin = origin; Forward = Vol2ShapeGeometry.FlatUnit(forward);
            PathWidth = (float)n.Get("space.path.width"); PathLength = (float)n.Get("space.path.length");
            FanWidth = (float)n.Get("space.fan.width"); FanDepth = (float)n.Get("space.fan.depth");
            FanHalfAngleRad = Mathf.Asin(Mathf.Clamp01(FanWidth * 0.5f / FanDepth));
            RingRadius = (float)n.Get("space.ring.radius"); RingThickness = (float)n.Get("space.ring.thickness");
            BrushReach = t.BrushReach; SwingRadius = t.SwingRadius; PathSpeed = t.PathSpeed;
        }

        public static Vol2ShapeSpec Create(Frame frame, NumbersV3Dto numbers, Vector3 origin, Vector3 forward, Vol2ShapeTuning tuning)
        {
            if (numbers == null || !numbers.IsValidated) throw new ArgumentException("validated numbers required");
            var kind = frame == Frame.Path ? Vol2ShapeKind.Path : frame == Frame.Attachment ? Vol2ShapeKind.Attachment : frame == Frame.Fan ? Vol2ShapeKind.Fan : Vol2ShapeKind.Ring;
            return new Vol2ShapeSpec(kind, origin, forward, numbers, tuning ?? new Vol2ShapeTuning());
        }
    }

    /// <summary>The exact tests. Pure (Vector3 in, number out), no Physics, so an edit-mode fixture and the Play emitters run the same code.
    /// A body is a disc (position, radius) on the plane.</summary>
    public static class Vol2ShapeGeometry
    {
        public static Vector3 FlatUnit(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        static Vector2 P(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>The narrow path as a disc of radius <paramref name="halfWidth"/> swept along the axis from <paramref name="fromAlong"/> to
        /// <paramref name="toAlong"/> (metres from <paramref name="start"/>), against a body disc. Exact for the swept stadium, so a fast
        /// step cannot skip a body (no tunnelling). entryAlong = the axis distance at which the head first touches the body, clamped to
        /// the step; lateral = the body centre's distance from the axis.</summary>
        public static bool PathStep(Vector3 start, Vector3 axis, float fromAlong, float toAlong, float halfWidth, Vector3 bodyPos, float bodyRadius, out float entryAlong, out float lateral)
        {
            entryAlong = 0f; lateral = 0f;
            Vector2 s = P(start), d = P(FlatUnit(axis)), c = P(bodyPos) - s;
            float t = Vector2.Dot(c, d);
            lateral = Mathf.Abs(c.x * d.y - c.y * d.x);
            float reach = halfWidth + bodyRadius;
            if (lateral > reach) return false;
            float h = Mathf.Sqrt(Mathf.Max(0f, reach * reach - lateral * lateral));
            float entry = Mathf.Max(t - h, fromAlong);
            if (t + h < fromAlong || entry > toAlong) return false;
            entryAlong = entry; return true;
        }

        /// <summary>A body disc against the sector (apex, forward, half-angle, depth). Exact: inside the infinite wedge -> depth + radius;
        /// outside it -> within radius of either edge segment (this also covers the apex).</summary>
        public static bool FanHit(Vector3 apex, Vector3 forward, float halfAngleRad, float depth, Vector3 bodyPos, float bodyRadius)
        {
            Vector2 a = P(apex), f = P(FlatUnit(forward)), c = P(bodyPos) - a;
            float dist = c.magnitude;
            if (dist > depth + bodyRadius) return false;
            float along = Vector2.Dot(c, f), lat = Mathf.Abs(c.x * f.y - c.y * f.x);
            bool inWedge = dist < 1e-6f || Mathf.Atan2(lat, along) <= halfAngleRad;
            if (inWedge) return true;
            Vector2 left = Rotate(f, halfAngleRad), right = Rotate(f, -halfAngleRad);
            return SegmentDistance(c, Vector2.zero, left * depth) <= bodyRadius || SegmentDistance(c, Vector2.zero, right * depth) <= bodyRadius;
        }

        static Vector2 Rotate(Vector2 v, float rad)
        {
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
        }

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float len2 = ab.sqrMagnitude;
            float u = len2 < 1e-10f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * u)).magnitude;
        }

        /// <summary>A body disc against the flat annulus [inner, outer] around the centre: the hollow is empty. A body fully inside the
        /// hole (distance + radius below inner) or fully outside (distance - radius above outer) is not touched.</summary>
        public static bool RingHit(Vector3 centre, float inner, float outer, Vector3 bodyPos, float bodyRadius)
        {
            float d = (P(bodyPos) - P(centre)).magnitude;
            return d + bodyRadius >= inner && Mathf.Max(0f, d - bodyRadius) <= outer;
        }

        /// <summary>The body's PATH from <paramref name="from"/> to <paramref name="to"/> (one step) against the annulus. The distance to the
        /// centre varies continuously along the step, so the body's discs cover every radius between the closest and the farthest
        /// approach: the step crosses the band iff that radial span overlaps it. A jump or a dash over the band is caught the same way
        /// a walk is (nothing here looks at feet or height).</summary>
        public static bool RingCrossed(Vector3 centre, float inner, float outer, Vector3 from, Vector3 to, float bodyRadius)
        {
            Vector2 c = P(centre), a = P(from), b = P(to);
            float dmin = SegmentDistance(c, a, b), dmax = Mathf.Max((a - c).magnitude, (b - c).magnitude);
            return dmax + bodyRadius >= inner && Mathf.Max(0f, dmin - bodyRadius) <= outer;
        }

        /// <summary>The swing volume: a capsule from the sleeve to the brush tip (plane projection) against a body disc.</summary>
        public static bool SwingHit(Vector3 sleeve, Vector3 tip, float swingRadius, Vector3 bodyPos, float bodyRadius)
            => SegmentDistance(P(bodyPos), P(sleeve), P(tip)) <= swingRadius + bodyRadius;
    }
}
