using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Batch 4 ring (R2 군 눈 문 순 운) helpers. r2: the cast camera sits INSIDE the 2 m ring, so a band drawn round the sides swept across the frame's bottom
    /// corners in r1 - the R2 bands are drawn over the front ~±55° only (gone by ±70°).</summary>
    public static class Vol2FsB4R
    {
        /// <summary>1 within ±55° of the ring's forward, 0 from ±70° (radians in).</summary>
        public static float Front55(float ang) => 1f - Vol2Fs.Smooth(100f, 135f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)));      // 2026-10-27 polish: ±100° (the ring reads round; was ±55°)
        /// <summary>The front arc's half span in radians (70°).</summary>
        public const float Span = 70f * Mathf.Deg2Rad;
    }
}
