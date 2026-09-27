using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Oheangbu.Data.World
{
    [Serializable]
    public struct WorldMacroCompressionInterval
    {
        public double Min;
        public double Max;
        public WorldMacroCompressionInterval(double min, double max) { Min = min; Max = max; }
    }

    /// <summary>
    /// Analytic, serializable segment. Its derivative interpolates with smoothstep;
    /// its position is the exact integral, not an interpolated lookup table.
    /// </summary>
    [Serializable]
    public struct WorldMacroCompressionSegment
    {
        [SerializeField] private double sourceMin, sourceMax, targetMin;
        [SerializeField] private double derivativeMin, derivativeMax;
        public double SourceMin => sourceMin;
        public double SourceMax => sourceMax;
        public double TargetMin => targetMin;
        public double TargetMax => targetMin + (sourceMax - sourceMin) * (derivativeMin + derivativeMax) * .5;
        public double DerivativeMin => derivativeMin;
        public double DerivativeMax => derivativeMax;

        internal WorldMacroCompressionSegment(double min, double max, double mappedMin, double d0, double d1)
        {
            sourceMin = min; sourceMax = max; targetMin = mappedMin;
            derivativeMin = d0; derivativeMax = d1;
        }

        public double Forward(double value)
        {
            double length = sourceMax - sourceMin;
            double t = (value - sourceMin) / length;
            return targetMin + length * (derivativeMin * t +
                (derivativeMax - derivativeMin) * t * t * t * (1.0 - .5 * t));
        }

        public double Derivative(double value)
        {
            double t = (value - sourceMin) / (sourceMax - sourceMin);
            return derivativeMin + (derivativeMax - derivativeMin) * t * t * (3.0 - 2.0 * t);
        }

        internal double Inverse(double target)
        {
            if (derivativeMin == derivativeMax)
                return sourceMin + (target - targetMin) / derivativeMin;

            // Bracketed inversion cannot jump out of this monotonic segment.
            double low = sourceMin, high = sourceMax;
            for (int i = 0; i < 60; i++)
            {
                double middle = low + (high - low) * .5;
                if (Forward(middle) < target) low = middle;
                else high = middle;
            }
            return low + (high - low) * .5;
        }
    }

    /// <summary>
    /// Strictly increasing compression along one axis. Protected intervals have
    /// derivative 1. Every unprotected gap has a common positive base derivative,
    /// with fixed-width C2 position shoulders next to protected intervals.
    /// Outside source bounds the endpoint tangent is extended, never clamped.
    /// </summary>
    [Serializable]
    public sealed class WorldMacroCompressionAxis
    {
        public const int CurrentVersion = 1;
        [SerializeField] private int version;
        [SerializeField] private double sourceMin, sourceMax, targetMin, targetMax;
        [SerializeField] private double shoulderWidth, baseDerivative, minimumDerivative;
        [SerializeField] private WorldMacroCompressionInterval[] protectedIntervals;
        [SerializeField] private WorldMacroCompressionSegment[] segments;

        public int Version => version;
        public double SourceMin => sourceMin;
        public double SourceMax => sourceMax;
        public double TargetMin => targetMin;
        public double TargetMax => targetMax;
        public double ShoulderWidth => shoulderWidth;
        public double BaseDerivative => baseDerivative;
        public double MinimumDerivative => minimumDerivative;
        public IReadOnlyList<WorldMacroCompressionInterval> ProtectedIntervals => Array.AsReadOnly(protectedIntervals);
        public IReadOnlyList<WorldMacroCompressionSegment> Segments => Array.AsReadOnly(segments);

        private WorldMacroCompressionAxis() { }

        public static bool TryBuild(double sourceMin, double sourceMax, double targetMin, double targetMax,
            IReadOnlyList<WorldMacroCompressionInterval> protectedIntervals, double shoulderWidth,
            double minimumDerivative, out WorldMacroCompressionAxis axis, out string reason)
        {
            axis = null;
            reason = null;
            if (!Finite(sourceMin) || !Finite(sourceMax) || !Finite(targetMin) || !Finite(targetMax) ||
                sourceMax <= sourceMin || targetMax <= targetMin ||
                !Finite(sourceMax - sourceMin) || !Finite(targetMax - targetMin))
                return Fail("Source and target bounds must be finite, increasing intervals.", out reason);
            if (!Finite(shoulderWidth) || shoulderWidth <= 0.0)
                return Fail("Shoulder width must be finite and strictly positive.", out reason);
            if (!Finite(minimumDerivative) || minimumDerivative <= 0.0 || minimumDerivative > 1.0)
                return Fail("Minimum derivative must be finite and in (0, 1].", out reason);

            double sourceLength = sourceMax - sourceMin;
            double targetLength = targetMax - targetMin;
            if (targetLength > sourceLength)
                return Fail("This compression map does not expand the total source span.", out reason);

            var merged = new List<WorldMacroCompressionInterval>();
            if (protectedIntervals != null)
            {
                for (int i = 0; i < protectedIntervals.Count; i++)
                {
                    WorldMacroCompressionInterval item = protectedIntervals[i];
                    if (!Finite(item.Min) || !Finite(item.Max) || item.Max <= item.Min ||
                        item.Min < sourceMin || item.Max > sourceMax)
                        return Fail("Protected interval " + i + " must be finite, have positive length, and lie inside source bounds.", out reason);
                    merged.Add(item);
                }
            }
            merged.Sort((a, b) => a.Min.CompareTo(b.Min));
            for (int i = 1; i < merged.Count;)
            {
                if (merged[i].Min <= merged[i - 1].Max)
                {
                    merged[i - 1] = new WorldMacroCompressionInterval(merged[i - 1].Min,
                        Math.Max(merged[i - 1].Max, merged[i].Max));
                    merged.RemoveAt(i);
                }
                else i++;
            }

            // Temporary derivatives are weights in [0,1]. Their integrated area
            // is the irreducible span required by protected cores and shoulders.
            var weights = new List<WorldMacroCompressionSegment>();
            double cursor = sourceMin;
            for (int i = 0; i <= merged.Count; i++)
            {
                double gapEnd = i < merged.Count ? merged[i].Min : sourceMax;
                if (gapEnd > cursor)
                {
                    bool leftProtected = i > 0;
                    bool rightProtected = i < merged.Count;
                    double needed = ((leftProtected ? 1 : 0) + (rightProtected ? 1 : 0)) * shoulderWidth;
                    if (gapEnd - cursor < needed)
                        return Fail(string.Format(CultureInfo.InvariantCulture,
                            "Gap [{0:R}, {1:R}] is only {2:R} m; minimum shoulders require {3:R} m. Merge nearby protection intervals explicitly or choose a smaller shoulder width.",
                            cursor, gapEnd, gapEnd - cursor, needed), out reason);
                    if (leftProtected)
                    {
                        Add(weights, cursor, cursor + shoulderWidth, 1.0, 0.0);
                        cursor += shoulderWidth;
                    }
                    double plateauEnd = gapEnd - (rightProtected ? shoulderWidth : 0.0);
                    Add(weights, cursor, plateauEnd, 0.0, 0.0);
                    if (rightProtected) Add(weights, plateauEnd, gapEnd, 0.0, 1.0);
                }
                if (i < merged.Count)
                {
                    Add(weights, merged[i].Min, merged[i].Max, 1.0, 1.0);
                    cursor = merged[i].Max;
                }
            }

            double protectedLength = 0.0, fixedArea = 0.0;
            foreach (WorldMacroCompressionInterval item in merged) protectedLength += item.Max - item.Min;
            foreach (WorldMacroCompressionSegment item in weights)
                fixedArea += (item.SourceMax - item.SourceMin) * (item.DerivativeMin + item.DerivativeMax) * .5;
            double freeArea = sourceLength - fixedArea;
            double requiredLength = fixedArea + minimumDerivative * freeArea;
            if (targetLength < requiredLength)
                return Fail(string.Format(CultureInfo.InvariantCulture,
                    "Target span {0:R} m is infeasible: protected length {1:R} m plus integrated minimum shoulders {2:R} m and positive derivative floor {3:R} require at least {4:R} m. No map was created.",
                    targetLength, protectedLength, fixedArea - protectedLength, minimumDerivative, requiredLength), out reason);

            double baseScale = freeArea == 0.0 ? 1.0 : (targetLength - fixedArea) / freeArea;
            if (!Finite(baseScale) || baseScale <= 0.0 || baseScale > 1.0)
                return Fail("Cannot construct a finite, strictly positive compression derivative from these bounds.", out reason);
            var mapped = new WorldMacroCompressionSegment[weights.Count];
            double mappedCursor = targetMin;
            for (int i = 0; i < weights.Count; i++)
            {
                WorldMacroCompressionSegment item = weights[i];
                mapped[i] = new WorldMacroCompressionSegment(item.SourceMin, item.SourceMax, mappedCursor,
                    baseScale + (1.0 - baseScale) * item.DerivativeMin,
                    baseScale + (1.0 - baseScale) * item.DerivativeMax);
                mappedCursor = mapped[i].TargetMax;
            }
            axis = new WorldMacroCompressionAxis
            {
                version = CurrentVersion, sourceMin = sourceMin, sourceMax = sourceMax,
                targetMin = targetMin, targetMax = targetMax, shoulderWidth = shoulderWidth,
                baseDerivative = baseScale, minimumDerivative = minimumDerivative,
                protectedIntervals = merged.ToArray(), segments = mapped
            };
            return true;
        }

        public double Forward(double value)
        {
            RequireQuery(value);
            if (value <= sourceMin) return targetMin + (value - sourceMin) * segments[0].DerivativeMin;
            if (value >= sourceMax) return targetMax + (value - sourceMax) * segments[segments.Length - 1].DerivativeMax;
            return segments[FindSource(value)].Forward(value);
        }

        public double Inverse(double value)
        {
            RequireQuery(value);
            if (value <= targetMin) return sourceMin + (value - targetMin) / segments[0].DerivativeMin;
            if (value >= targetMax) return sourceMax + (value - targetMax) / segments[segments.Length - 1].DerivativeMax;
            int low = 0, high = segments.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (value < segments[middle].TargetMax) high = middle;
                else low = middle + 1;
            }
            return segments[low].Inverse(value);
        }

        public double Derivative(double value)
        {
            RequireQuery(value);
            if (value <= sourceMin) return segments[0].DerivativeMin;
            if (value >= sourceMax) return segments[segments.Length - 1].DerivativeMax;
            return segments[FindSource(value)].Derivative(value);
        }

        private int FindSource(double value)
        {
            int low = 0, high = segments.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (value < segments[middle].SourceMax) high = middle;
                else low = middle + 1;
            }
            return low;
        }

        private void RequireQuery(double value)
        {
            if (version != CurrentVersion || segments == null || segments.Length == 0)
                throw new InvalidOperationException("Compression axis is uninitialized or has an unsupported version.");
            if (!Finite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Coordinate must be finite.");
        }

        private static void Add(List<WorldMacroCompressionSegment> list, double min, double max, double d0, double d1)
        {
            if (max > min) list.Add(new WorldMacroCompressionSegment(min, max, 0.0, d0, d1));
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }

    /// <summary>
    /// Separable X/Z map. A protected rectangle must contribute its X and Z
    /// projections to the corresponding axis inputs. Then its entire interior
    /// undergoes translation only. Y is always copied unchanged. Axis projection
    /// may conservatively protect unrelated terrain; infeasible layouts fail.
    /// </summary>
    [Serializable]
    public sealed class WorldMacroCompressionMap
    {
        public const int CurrentVersion = 1;
        [SerializeField] private int version;
        [SerializeField] private Bounds sourceBounds, targetBounds;
        [SerializeField] private WorldMacroCompressionAxis xAxis, zAxis;
        public int Version => version;
        public Bounds SourceBounds => sourceBounds;
        public Bounds TargetBounds => targetBounds;
        public WorldMacroCompressionAxis XAxis => xAxis;
        public WorldMacroCompressionAxis ZAxis => zAxis;

        private WorldMacroCompressionMap() { }

        public static bool TryBuild(Bounds sourceBounds, Vector2 targetMinXZ, Vector2 targetMaxXZ,
            IReadOnlyList<WorldMacroCompressionInterval> protectedX,
            IReadOnlyList<WorldMacroCompressionInterval> protectedZ, double shoulderWidth,
            double minimumDerivative, out WorldMacroCompressionMap map, out string reason)
        {
            map = null;
            Vector3 sourceMin = sourceBounds.min, sourceMax = sourceBounds.max;
            if (!WorldMacroCompressionAxis.Finite(sourceMin.y) || !WorldMacroCompressionAxis.Finite(sourceMax.y) || sourceMax.y < sourceMin.y)
            {
                reason = "Source Y bounds must be finite and nonnegative in size.";
                return false;
            }
            if (!WorldMacroCompressionAxis.TryBuild(sourceMin.x, sourceMax.x, targetMinXZ.x, targetMaxXZ.x,
                protectedX, shoulderWidth, minimumDerivative, out WorldMacroCompressionAxis x, out reason))
            { reason = "X axis: " + reason; return false; }
            if (!WorldMacroCompressionAxis.TryBuild(sourceMin.z, sourceMax.z, targetMinXZ.y, targetMaxXZ.y,
                protectedZ, shoulderWidth, minimumDerivative, out WorldMacroCompressionAxis z, out reason))
            { reason = "Z axis: " + reason; return false; }
            var target = new Bounds();
            target.SetMinMax(new Vector3(targetMinXZ.x, sourceMin.y, targetMinXZ.y),
                new Vector3(targetMaxXZ.x, sourceMax.y, targetMaxXZ.y));
            map = new WorldMacroCompressionMap
            {
                version = CurrentVersion, sourceBounds = sourceBounds, targetBounds = target, xAxis = x, zAxis = z
            };
            return true;
        }

        public Vector3 Map(Vector3 value)
        {
            RequireReady(value);
            return new Vector3((float)xAxis.Forward(value.x), value.y, (float)zAxis.Forward(value.z));
        }

        public Vector3 Inverse(Vector3 value)
        {
            RequireReady(value);
            return new Vector3((float)xAxis.Inverse(value.x), value.y, (float)zAxis.Inverse(value.z));
        }

        private void RequireReady(Vector3 value)
        {
            if (version != CurrentVersion || xAxis == null || zAxis == null)
                throw new InvalidOperationException("Compression map is uninitialized or has an unsupported version.");
            if (!WorldMacroCompressionAxis.Finite(value.y))
                throw new ArgumentOutOfRangeException(nameof(value), "Coordinate must be finite.");
        }
    }

    [CreateAssetMenu(menuName = "Oheangbu/World/Macro Compression Map", fileName = "WorldMacroCompressionMap")]
    public sealed class WorldMacroCompressionMapSO : ScriptableObject
    {
        public WorldMacroCompressionMap Mapping;
        public Vector3 Map(Vector3 point) => RequireMapping().Map(point);
        public Vector3 Inverse(Vector3 point) => RequireMapping().Inverse(point);
        private WorldMacroCompressionMap RequireMapping() => Mapping ??
            throw new InvalidOperationException("Build and assign a compression mapping before using this asset.");
    }
}
