using System;
using System.IO;
using Oheangbu.App.World.Dressing;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroDressingRenderCostReview
    {
        // Read only: does not generate, render, reset caches, change camera or start Play.
        public static string Execute(string label)
        {
            if(string.IsNullOrEmpty(label)||label.IndexOfAny(Path.GetInvalidFileNameChars())>=0||label.Contains(".."))
                throw new ArgumentException("A simple report label is required.");
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            if(renderer==null)throw new InvalidOperationException("No existing dressing renderer.");
            string json=renderer.RenderCostJson();
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/Vegetation/Performance"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"_"+label+"_submissions.json"),json);
            return json;
        }
    }
}
