using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Private compact-world elevation correction sampled in destination metres.</summary>
    public sealed class WorldMacroReliefGridSO : ScriptableObject
    {
        public int Version = 2;
        public Vector2 Origin;
        public float Spacing = 8;
        public int Width, Height;
        public float[] TargetHeights = Array.Empty<float>();
        public float[] Weights = Array.Empty<float>();
        public bool IncludesRoadGrades = true;
        public string SourceHash, MappingHash, DataHash, WeightHash, FitHash;

        public void Validate()
        {
            if (Version != 2 || Width < 2 || Height < 2 || Width > 4097 || Height > 4097 ||
                !Finite(Spacing) || Spacing <= 0 || !Finite(Origin.x) || !Finite(Origin.y) ||
                TargetHeights == null || TargetHeights.Length != checked(Width * Height) || Weights == null || Weights.Length != TargetHeights.Length)
                throw new InvalidOperationException("Invalid compact relief grid dimensions.");
            foreach (float value in TargetHeights)
                if (!Finite(value)) throw new InvalidOperationException("Nonfinite compact relief sample.");
            foreach (float value in Weights)
                if (!Finite(value) || value < 0 || value > 1) throw new InvalidOperationException("Invalid compact relief weight.");
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public float SampleHeight(float x, float z, float source)
        {
            if (TargetHeights == null || Weights == null || Width < 2 || Height < 2 || TargetHeights.Length != Width * Height || Weights.Length != TargetHeights.Length || Spacing <= 0) return source;
            double gx = ((double)x - Origin.x) / Spacing, gz = ((double)z - Origin.y) / Spacing;
            if (gx < 0 || gz < 0 || gx > Width - 1 || gz > Height - 1) return source;
            int ix = Math.Min((int)gx, Width - 2), iz = Math.Min((int)gz, Height - 2);
            float tx = (float)(gx - ix), tz = (float)(gz - iz);
            int i = iz * Width + ix;
            float target = Mathf.Lerp(Mathf.Lerp(TargetHeights[i], TargetHeights[i + 1], tx),
                Mathf.Lerp(TargetHeights[i + Width], TargetHeights[i + Width + 1], tx), tz);
            float weight = Mathf.Lerp(Mathf.Lerp(Weights[i], Weights[i + 1], tx),
                Mathf.Lerp(Weights[i + Width], Weights[i + Width + 1], tx), tz);
            return source + weight * (target - source);
        }
    }
}
