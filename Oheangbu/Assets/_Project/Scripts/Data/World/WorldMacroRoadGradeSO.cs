using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Compact connector beds with source-relative hillside displacement.</summary>
    public sealed class WorldMacroRoadGradeSO : ScriptableObject
    {
        public const int CurrentVersion = 4;
        [Serializable] public sealed class Line
        {
            public string Id;
            public float Width;
            public Vector3[] Points;
            public float[] OriginalY;
        }
        public int Version = CurrentVersion;
        public Line[] Lines = Array.Empty<Line>();
        public Rect[] Protected = Array.Empty<Rect>();
        public float Shoulder = 50;
        public float RoadbedPadding = .8f;
        public float RoadbedTransition = 6;
        public WorldMacroReliefGridSO Relief;

        struct Segment { public Vector3 a, b; public float originalA, originalB, width; }
        [NonSerialized] Dictionary<long, List<Segment>> index;
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        void OnEnable() => InvalidateCache();
        public void InvalidateCache() => index = null;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Validate()
        {
            if (Version != CurrentVersion || Lines == null || Protected == null ||
                !Finite(Shoulder) || Shoulder <= 0 || !Finite(RoadbedTransition) || RoadbedTransition <= 0 ||
                !Finite(RoadbedPadding) || RoadbedPadding < 0)
                throw new InvalidOperationException("Invalid compact grade profile settings; rebuild with grade-reset.");
            if (Relief != null) Relief.Validate();
            foreach (var line in Lines)
            {
                if (line == null || line.Points == null || line.OriginalY == null ||
                    line.Points.Length != line.OriginalY.Length || line.Points.Length < 2 || !Finite(line.Width) || line.Width <= 0)
                    throw new InvalidOperationException("Every grade line requires matching fitted points and original Y samples: " + line?.Id);
                for (int i = 0; i < line.Points.Length; i++)
                    if (!Finite(line.Points[i].x) || !Finite(line.Points[i].y) || !Finite(line.Points[i].z) || !Finite(line.OriginalY[i]))
                        throw new InvalidOperationException("Nonfinite compact grade sample: " + line.Id + "/" + i);
            }
        }

        void Ensure()
        {
            if (index != null) return;
            Validate();
            var rebuilt = new Dictionary<long, List<Segment>>();
            foreach (var line in Lines) for (int i = 1; i < line.Points.Length; i++)
            {
                var a = line.Points[i - 1]; var b = line.Points[i];
                float r = line.Width * .5f + RoadbedPadding + Mathf.Max(Shoulder, RoadbedTransition);
                if (new Vector2(a.x - b.x, a.z - b.z).sqrMagnitude <= 1e-12f) continue;
                var s = new Segment { a = a, b = b, originalA = line.OriginalY[i - 1], originalB = line.OriginalY[i], width = line.Width };
                for (int z = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - r) / 64); z <= Mathf.FloorToInt((Mathf.Max(a.z, b.z) + r) / 64); z++)
                    for (int x = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r) / 64); x <= Mathf.FloorToInt((Mathf.Max(a.x, b.x) + r) / 64); x++)
                    {
                        long key = Key(x, z);
                        if (!rebuilt.TryGetValue(key, out var list)) { list = new List<Segment>(); rebuilt.Add(key, list); }
                        list.Add(s);
                    }
            }
            index = rebuilt;
        }

        public bool IsProtected(float x, float z)
        {
            foreach (var rect in Protected)
                if (x >= rect.xMin && x <= rect.xMax && z >= rect.yMin && z <= rect.yMax) return true;
            return false;
        }

        static float Falloff(float distance, float radius)
        {
            float t = Math.Max(0, Math.Min(1, distance / radius));
            return 1 - t * t * (3 - 2 * t);
        }

        /// <summary>Source must be the immutable ungraded terrain height, never a previous sculpt result.</summary>
        public static float BlendHeight(float source, float originalRouteY, float fittedRouteY,
            float distanceBeyondBed, float shoulder = 50, float bedTransition = 6)
        {
            float distance = Math.Max(0, distanceBeyondBed);
            if (distance == 0) return fittedRouteY;
            // A broad shoulder translates the existing hillside by the route's change in height.
            // Absolute flattening is limited to the actual roadbed and its short transition.
            float displacedSource = source + (fittedRouteY - originalRouteY) * Falloff(distance, shoulder);
            float bedWeight = Falloff(distance, bedTransition);
            return displacedSource + (fittedRouteY - displacedSource) * bedWeight;
        }

        public float Height(float x, float z, float source)
        {
            if (IsProtected(x, z)) return source;
            Ensure();
            if (Relief != null) source = Relief.SampleHeight(x, z, source);
            if (!index.TryGetValue(Key(Mathf.FloorToInt(x / 64), Mathf.FloorToInt(z / 64)), out var list)) return source;
            float nearest = float.MaxValue, fitted = source, original = source, width = 0;
            bool selectedBed = false;
            foreach (var s in list)
            {
                // Dense boundary samples can be millimetres apart; the terrain helper's
                // 0.001 squared-length threshold would incorrectly collapse those edges.
                double dx = (double)s.b.x - s.a.x, dz = (double)s.b.z - s.a.z;
                double lengthSquared = dx * dx + dz * dz;
                float t = (float)Math.Max(0, Math.Min(1, (((double)x - s.a.x) * dx + ((double)z - s.a.z) * dz) / lengthSquared));
                double ex = x - (s.a.x + dx * t), ez = z - (s.a.z + dz * t);
                float distance = (float)Math.Sqrt(ex * ex + ez * ez);
                // A nearby narrow footpath shoulder must not cut into an actual wider
                // carriage bed. Shared full beds use the wider authored surface; only
                // outside all beds do we choose the nearest centerline's shoulder.
                bool insideBed = distance <= s.width * .5f + RoadbedPadding;
                if (selectedBed && !insideBed) continue;
                if (insideBed && selectedBed)
                {
                    if (s.width < width - .0001f) continue;
                    if (Mathf.Abs(s.width - width) <= .0001f && distance >= nearest) continue;
                }
                else if (!insideBed && distance >= nearest) continue;
                nearest = distance; fitted = Mathf.Lerp(s.a.y, s.b.y, t);
                original = Mathf.Lerp(s.originalA, s.originalB, t); width = s.width;
                selectedBed = insideBed;
            }
            float beyondBed = Mathf.Max(0, nearest - width * .5f - RoadbedPadding);
            if (beyondBed >= Mathf.Max(Shoulder, RoadbedTransition)) return source;
            // Protected regions return their exact source height above. Do not feather the
            // roadbed itself toward source Y: that would invalidate the fitted route grade.
            // The reviewed global field already solves the road elevation into its
            // surrounding hillsides. Do not apply the old road delta a second time.
            if (Relief != null && Relief.IncludesRoadGrades)
                return BlendHeight(source, fitted, fitted, beyondBed, Shoulder, RoadbedTransition);
            return BlendHeight(source, original, fitted, beyondBed, Shoulder, RoadbedTransition);
        }
    }
}
