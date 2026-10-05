using System;
using UnityEngine;
namespace Oheangbu.App
{
    /// <summary>One cast owns one placement and timing plan; presentation never rerolls it.</summary>
    public static class AreaSpikePlanner
    {
        // A cast without row data (old callers) keeps the pre-#308 numbers.
        public static void Fill(AreaImpactPlan plan,int seed)=>Fill(plan,seed,AreaSpikeSpec.Legacy);
        // #308 WP-00: count, fill, window and gaps come from the row (spike.*); the arithmetic lives in AreaSpikeRule308 (pure).
        public static void Fill(AreaImpactPlan plan,int seed,AreaSpikeSpec spec)
        {
            plan.Spikes.Clear();plan.CreatedAt=Time.time;
            var points=new Vector3[spec.Count];var rise=new float[spec.Count];
            AreaSpikeRule308.Plan(seed,spec,plan.Point,plan.Radius,plan.Delay,points,rise);
            for(int i=0;i<spec.Count;i++)plan.Spikes.Add(new PlannedSpike{Point=points[i],RiseAt=rise[i]});
        }
        public static float NearestRise(AreaImpactPlan plan,Vector3 target)
        {
            float best=float.PositiveInfinity,at=plan.Delay;
            foreach(var spike in plan.Spikes){var d=spike.Point-target;d.y=0;if(d.sqrMagnitude<best){best=d.sqrMagnitude;at=spike.RiseAt;}}
            return at;
        }
    }
}
