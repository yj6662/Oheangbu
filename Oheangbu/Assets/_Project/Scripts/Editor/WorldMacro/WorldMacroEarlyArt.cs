using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroEarlyArt
    {
        static string Output=>WorldMacroPlaytestAuthoring.Output+"/EarlyArt";
        const string Root="Playtest_EarlyArt";
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static WorldMacroDressingRenderer Dressing=>Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
        public static string Execute(string command)
        {
            if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)throw new Exception("Playtest Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("System commit >=85%; stopped");
            Directory.CreateDirectory(Output);
            if(command=="inspect")return Inspect();
            if(command=="apply")return Apply();
            if(command=="validate")return Validate();
            if(command=="refine")return Refine();
            if(command=="ground")return GroundFoliage();
            if(command=="materials")return FoliageMaterials();
            if(command=="card-size")return CardSize();
            if(command=="card-test"){var f=Object.FindFirstObjectByType<EarlyRegionFoliage>();f.DiagnosticFar=true;try{return Capture("diagnostic:mine_exit");}finally{f.DiagnosticFar=false;}}
            if(command=="ridge-fill"){
                var root=GameObject.Find(Root);Object.DestroyImmediate(root.GetComponent<EarlyRegionFoliage>());
                BuildFoliage(root.transform);Refine();GroundFoliage();FoliageMaterials();CardSize();return Validate();
            }
            if(command=="details")return string.Join("\n",Object.FindFirstObjectByType<EarlyRegionFoliage>().Packets.Where(p=>p.Distance>400&&p.Count>0).Take(4).Select(p=>"near="+p.Near[0].Matrices[0]+" far="+p.Far[0].Matrices[0]+" material="+p.Far[0].Material.name+" tex="+p.Far[0].Material.GetTexture("_BaseMap")+" shader="+p.Far[0].Material.shader.name));
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            throw new ArgumentException(command);
        }
        static string Inspect()
        {
            File.WriteAllText(Output+"/content_before.json",EditorJsonUtility.ToJson(Session.Content,true));
            var lines=new List<string>{"dressing="+AssetDatabase.GetAssetPath(Dressing.Sheet)};
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            lines.Add("cave="+cave.position+" yaw="+cave.eulerAngles);
            foreach(var t in cave.GetComponentsInChildren<Transform>())lines.Add("cavepart="+t.name+" pos="+t.position);
            foreach(var p in Dressing.Sheet.Prototypes)lines.Add("proto="+p.Id+" category="+p.Category+" realm="+p.Realm+" size="+p.Size+" lods="+p.Lods.Length+" src="+p.SourcePath);
            File.WriteAllLines(Output+"/survey.txt",lines);
            return Output+"/survey.txt";
        }
        static string Capture(string id)
        {
            var bits=id.Split(':');string phase=bits[0],view=bits[1];
            string source=view=="mine_inside"||view.StartsWith("inn_")?"AssetReuse_"+view:"VisualCorridor_"+view;
            var info=JsonUtility.FromJson<WorldMacroDressingProbe.Result>(File.ReadAllText(WorldMacroBuilder.Output+"/Dressing/"+source+".json"));
            var p=info.position;var target=p+Quaternion.Euler(info.euler)*Vector3.forward*10;
            Dressing.ResetCache();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            do { Dressing.PrepareView(p,4096,()=>{if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("Capture memory guard");}); }
            while(Dressing.PendingChunks>0&&timer.Elapsed.TotalSeconds<8);
            File.WriteAllText(Output+"/"+phase+"_"+view+"_loading.txt","Bounded 8-second Editor preparation, not fully settled. Pending="+Dressing.PendingChunks+"; resident="+Dressing.ResidentInstances+". Runtime performance not certified.");
            string image;float budget=Dressing.PacketBudgetMilliseconds;
            var root=GameObject.Find(Root);bool active=root!=null&&root.activeSelf;
            try { if(phase=="before"&&root!=null)root.SetActive(false);Dressing.PacketBudgetMilliseconds=1000; image=WorldMacroDressingProbe.Capture("EarlyArt_"+phase+"_"+view,true,p.x,p.y,p.z,target.x,target.y,target.z); }
            finally { Dressing.PacketBudgetMilliseconds=budget;if(root!=null)root.SetActive(active); }
            File.Copy(image,Output+"/"+phase+"_"+view+".png",true);
            var foliage=Object.FindFirstObjectByType<EarlyRegionFoliage>();
            if(foliage!=null)File.WriteAllText(Output+"/"+phase+"_"+view+"_cost.txt","New foliage draw calls="+foliage.DrawCalls+"; instances="+foliage.Instances+"; submission ms="+foliage.SubmissionMilliseconds+". Single Editor still, not CPU/GPU frame time.");
            return Output+"/"+phase+"_"+view+".png";
        }
    }
}
