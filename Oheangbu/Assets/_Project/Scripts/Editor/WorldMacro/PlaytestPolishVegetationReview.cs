using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Explicit fixed-station review only. Does not walk routes or mutate gameplay progress.
    public static class PlaytestPolishVegetationReview
    {
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/Vegetation"));
        static WorldMacroDressingRenderer measuring;
        static WorldMacroDressingSheetSO saved;
        public static string Execute(string command)
        {
            var parts=command.Split(':');
            if(parts[0]=="poll")
            {
                string result=WorldMacroDressingProbe.Poll();
                if(!WorldMacroDressingProbe.IsRunning&&measuring!=null){measuring.Sheet=saved;measuring.ResetCache();measuring=null;saved=null;}
                return result;
            }
            if(WorldMacroDressingProbe.IsRunning)throw new InvalidOperationException("Finish current measurement first.");
            string id=parts.Length>1?parts[1]:"mine_exit";
            var views=AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>(WorldMacroVisualCorridorAuthoring.Folder+"/VisualCorridor.asset");
            var view=views.Views.First(v=>v.Id==id);
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            if(renderer==null)throw new InvalidOperationException("Existing dressing required.");
            bool before=parts.Length>2&&parts[2]=="before";
            string label="Polish_"+id+"_"+(before?"before":"after");
            if(parts[0]=="view")
            {
                var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                if(!Application.isPlaying||session==null||string.IsNullOrEmpty(session.TestSaveSuffix)||session.Walker.Seated)throw new InvalidOperationException("Isolated unseated review Play mode required.");
                if(!session.TrySafeFeet(view.Eye-Vector3.up*1.5f,out var feet))throw new InvalidOperationException("No safe ground at requested review view.");
                session.Teleport(feet,Quaternion.LookRotation(view.Target-view.Eye).eulerAngles.y);
                return "Diagnostic station: "+id+"; no traversal or completion action issued";
            }
            var original=renderer.Sheet;
            if(before)renderer.Sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(File.ReadAllText(Output+"/Baseline/source_path.txt").Trim());
            if(renderer.Sheet==null){renderer.Sheet=original;throw new InvalidOperationException("Preserved baseline sheet missing.");}
            renderer.ResetCache();
            if(parts[0]=="measure")
            {
                try{saved=original;measuring=renderer;return WorldMacroDressingProbe.BeginMeasure(label,true,180);}
                catch{renderer.Sheet=original;renderer.ResetCache();measuring=null;saved=null;throw;}
            }
            try
            {
                if(parts[0]!="capture")throw new ArgumentException("capture/view/measure:<view>:before|after, poll");
                string path=WorldMacroDressingProbe.Capture(label,true,view.Eye.x,view.Eye.y,view.Eye.z,view.Target.x,view.Target.y,view.Target.z);
                Directory.CreateDirectory(Output+"/Screenshots");
                File.Copy(path,Output+"/Screenshots/"+label+".png",true);
                File.Copy(Path.ChangeExtension(path,".json"),Output+"/Screenshots/"+label+".json",true);
                return Output+"/Screenshots/"+label+".png";
            }
            finally{renderer.Sheet=original;renderer.ResetCache();}
        }
    }
}
