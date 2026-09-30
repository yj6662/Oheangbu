using System;
using Oheangbu.App.Demo;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool condition,string label)
    { if(!condition)throw new Exception(label);checks++; }
    static Vector3 U(float x,float y,float z)=>new Vector3(-x,z,-y);
    static int Main()
    {
        // Actual source joint geometry; the supplied gaps are from the recorded real terrain diagnostic.
        var hips=new[]{U(-.105f,-.005f,1.10f),U(.105f,-.050f,1.10f),U(.105f,.595f,1.19f),U(.325f,.480f,1.19f)};
        var knees=new[]{U(-.10f,-.018f,.58f),U(.100f,-.066f,.58f),U(.183f,.838f,.635f),U(.389f,.748f,.635f)};
        var ankles=new[]{U(-.109f,-.108f,.050f),U(.099f,-.151f,.050f),U(.154f,.835f,.047f),U(.382f,.740f,.047f)};
        var gaps=new[]{.04538f,.04037f,.01262f,.01027f};
        float drop=.04538f-.005f;
        for(int i=0;i<4;i++)
        {
            float upper=Vector3.Distance(hips[i],knees[i]),lower=Vector3.Distance(knees[i],ankles[i]);
            Vector3 hip=hips[i]+Vector3.down*drop,ankle=ankles[i]+Vector3.down*(gaps[i]-.005f);
            Vector3 hint=Vector3.ProjectOnPlane(knees[i]-hips[i],ankles[i]-hips[i]);
            Check(DemoWoodDeerFootPlacement.TryTwoBoneKnee(hip,ankle,hint,upper,lower,out var knee),"Recorded terrain target reachable: "+i);
            Check(Math.Abs(Vector3.Distance(hip,knee)-upper)<.00001,"Upper segment unchanged: "+i);
            Check(Math.Abs(Vector3.Distance(knee,ankle)-lower)<.00001,"Lower segment unchanged: "+i);
            Check(Vector3.Dot(Vector3.ProjectOnPlane(knee-hip,ankle-hip),hint)>0,"Knee bend side preserved: "+i);
            Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(hip,hip+Vector3.down*(upper+lower+.03f),hint,upper,lower,out _),"Unreachable target rejected: "+i);
        }
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(Vector3.zero,Vector3.zero,Vector3.right,1,1,out _),"Collapsed target rejected");
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(Vector3.zero,Vector3.up*.4f,Vector3.right,1,.4f,out _),"Too-close unequal-chain target rejected");
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(Vector3.zero,Vector3.up,Vector3.up,1,1,out _),"Degenerate knee plane rejected");
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(new Vector3(float.NaN,0,0),Vector3.up,Vector3.right,1,1,out _),"NaN rejected");
        Console.WriteLine("PASS "+checks+" analytic IK checks; Unity physics/skinning integration not exercised.");return 0;
    }
}
