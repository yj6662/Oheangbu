using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Oheangbu.App.Demo;
using UnityEngine;

static class Checks
{
    static readonly List<string> passed = new();
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); passed.Add(name); }
    static Vector3 Point(JsonElement p,float scale) => new Vector3(p[0].GetSingle(),p[2].GetSingle(),-p[1].GetSingle())*scale;
    static void Main(string[] args)
    {
        string root=args.Length>0?args[0]:Directory.GetCurrentDirectory();
        int solved=0, rejected=0; float maxError=0;
        foreach(string id in new[]{"bulgasari","fox_spirit"})
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Art/Characters/Folklore298/RigConfigs",id+".json")));
            foreach(var leg in doc.RootElement.GetProperty("legs").EnumerateObject())
            foreach(float scale in new[]{1f,2f,4f})
            {
                var hip=Point(leg.Value.GetProperty("hip"),scale);
                var knee=Point(leg.Value.GetProperty("knee"),scale);
                var ankle=Point(leg.Value.GetProperty("ankle"),scale);
                float upper=Vector3.Distance(hip,knee),lower=Vector3.Distance(knee,ankle);
                var bend=knee-hip;
                Check(DemoWoodDeerFootPlacement.TryTwoBoneKnee(hip,ankle,bend,upper,lower,out var baseKnee)&&Vector3.Distance(knee,baseKnee)<.00003f,
                    id+"/"+leg.Name+"/"+scale+": source rest pose");
                foreach(float dx in new[]{-.01f,0,.01f})
                foreach(float dy in new[]{-.01f,0,.01f})
                foreach(float dz in new[]{-.01f,0,.01f})
                {
                    var target=ankle+new Vector3(dx,dy,dz)*scale;
                    bool ok=DemoWoodDeerFootPlacement.TryTwoBoneKnee(hip,target,bend,upper,lower,out var found);
                    float distance=Vector3.Distance(hip,target);
                    bool reachable=distance>MathF.Abs(upper-lower)+.0002f && distance<upper+lower-.0002f;
                    if(ok!=reachable)throw new Exception("Reachability disagreement");
                    if(!ok){rejected++;continue;}
                    solved++;
                    float error=MathF.Max(MathF.Abs(Vector3.Distance(hip,found)-upper),MathF.Abs(Vector3.Distance(found,target)-lower));
                    maxError=MathF.Max(maxError,error);
                    if(error>.00003f || Vector3.Dot(Vector3.ProjectOnPlane(found-hip,target-hip),Vector3.ProjectOnPlane(bend,target-hip))<=0)
                        throw new Exception("Length or bend plane changed");
                }
                var axis=(ankle-hip).normalized;
                Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(hip,hip+axis*(upper+lower+.01f),bend,upper,lower,out _),id+"/"+leg.Name+"/"+scale+": no stretch");
                Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(hip,hip,bend,upper,lower,out _),id+"/"+leg.Name+"/"+scale+": collapsed target rejected");
            }
        }
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(new Vector3(float.NaN,0,0),Vector3.down,Vector3.forward,1,1,out _),"nonfinite position rejected");
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(Vector3.zero,Vector3.down,Vector3.forward,float.NaN,1,out _),"nonfinite length rejected");
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(Vector3.zero,Vector3.down,Vector3.forward,0,1,out _),"zero length rejected");
        Check(!DemoWoodDeerFootPlacement.TryTwoBoneKnee(Vector3.zero,Vector3.down,Vector3.down,1,1,out _),"undefined bend rejected");
        Console.WriteLine(JsonSerializer.Serialize(new{scope="Actual detached App DLL public two-bone solver and real rig coordinates; no native Unity engine, component integration, imported skin or visual claim",count=passed.Count,solved,rejected,maxSegmentError=maxError,passed}));
    }
}
