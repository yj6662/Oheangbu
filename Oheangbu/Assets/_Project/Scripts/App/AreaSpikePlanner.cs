using System;
using UnityEngine;
namespace Oheangbu.App
{
    /// <summary>One cast owns one placement and timing plan; presentation never rerolls it.</summary>
    public static class AreaSpikePlanner
    {
        public static void Fill(AreaImpactPlan plan,int seed)
        {
            plan.Spikes.Clear();plan.CreatedAt=Time.time;
            var random=new System.Random(seed);int[] order=new int[17];float[] gaps=new float[16];float sum=0;
            for(int i=0;i<17;i++)order[i]=i;
            for(int i=16;i>0;i--){int j=random.Next(i+1);int t=order[i];order[i]=order[j];order[j]=t;}
            for(int i=0;i<16;i++){gaps[i]=.35f+(float)random.NextDouble();sum+=gaps[i];}
            for(int i=0;i<17;i++)
            {
                float a=i*2.39996323f;float radius=Mathf.Sqrt((i+.35f)/17f)*plan.Radius*.82f;
                plan.Spikes.Add(new PlannedSpike{Point=plan.Point+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius});
            }
            float time=plan.Delay;
            for(int rank=0;rank<17;rank++){plan.Spikes[order[rank]].RiseAt=time;if(rank<16)time+=.6f*gaps[rank]/sum;}
        }
        public static float NearestRise(AreaImpactPlan plan,Vector3 target)
        {
            float best=float.PositiveInfinity,at=plan.Delay;
            foreach(var spike in plan.Spikes){var d=spike.Point-target;d.y=0;if(d.sqrMagnitude<best){best=d.sqrMagnitude;at=spike.RiseAt;}}
            return at;
        }
    }
}
