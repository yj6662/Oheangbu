// PURE308
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Numbers of the staggered spike field (SPEC-SPELL-120-308 WP-00): moved from code constants to the row parameters
    // spike.count / spike.fill / spike.window / spike.gap / spike.inner. Legacy = the constants they replace; it is only used
    // for a cast without a row (a book that has no imported table yet, an old caller handing the wiring a bare cast).
    public readonly struct AreaSpikeSpec
    {
        public readonly int Count;        // spikes per cast
        public readonly float Fill;       // outermost spike radius as a share of the judgement radius
        public readonly float Window;     // seconds between the first and the last spike
        public readonly float Gap;        // smallest gap weight (each gap = Gap + a random 0..1)
        public readonly float Inner;      // sunflower offset of the innermost spike

        public AreaSpikeSpec(int count, float fill, float window, float gap, float inner)
        { Count = count < 1 ? 1 : count; Fill = fill; Window = window; Gap = gap; Inner = inner; }

        public static AreaSpikeSpec Legacy => new AreaSpikeSpec(17, .82f, .6f, .35f, .35f);

        public static AreaSpikeSpec From(SpellRow row)
        {
            var legacy = Legacy;
            if (row == null) return legacy;
            return new AreaSpikeSpec((int)row.F("spike.count", legacy.Count), row.F("spike.fill", legacy.Fill),
                row.F("spike.window", legacy.Window), row.F("spike.gap", legacy.Gap), row.F("spike.inner", legacy.Inner));
        }
    }

    // One cast owns one placement and timing plan (the pre-#308 AreaSpikePlanner arithmetic, same operations in the same
    // order, with the constants as parameters). Pure: no Unity object, no clock.
    public static class AreaSpikeRule308
    {
        const float GoldenAngle = 2.39996323f; // radians: sunflower packing, geometry not balance

        // points[i] = spike position, riseAt[i] = seconds after the cast at which spike i is fully out. Both arrays hold spec.Count items.
        public static void Plan(int seed, in AreaSpikeSpec spec, Vector3 center, float radius, float delay, Vector3[] points, float[] riseAt)
        {
            int count = spec.Count;
            var random = new System.Random(seed); int[] order = new int[count]; float[] gaps = new float[count - 1]; float sum = 0;
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = count - 1; i > 0; i--) { int j = random.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            for (int i = 0; i < count - 1; i++) { gaps[i] = spec.Gap + (float)random.NextDouble(); sum += gaps[i]; }
            for (int i = 0; i < count; i++)
            {
                float a = i * GoldenAngle; float ring = Mathf.Sqrt((i + spec.Inner) / count) * radius * spec.Fill;
                points[i] = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ring;
            }
            float time = delay;
            for (int rank = 0; rank < count; rank++) { riseAt[order[rank]] = time; if (rank < count - 1) time += spec.Window * gaps[rank] / sum; }
        }
    }
}
