using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    public static class C2PerformanceBuilder
    {
        public static string BuildBaseline() => Build("Baseline");
        public static string BuildOptimized() => Build("Optimized");
        public static string BuildCpuFinal() => Build("CpuFinal");
        static string Build(string label)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            string directory = Path.GetFullPath("../Builds/C2Performance/" + label); Directory.CreateDirectory(directory);
            bool stats = PlayerSettings.enableFrameTimingStats;
            try
            {
                PlayerSettings.enableFrameTimingStats = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity" }, locationPathName = directory + "/C2.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                string result = report.summary.result + " " + report.summary.totalSize + " bytes, " + report.summary.totalTime + ", errors=" + report.summary.totalErrors;
                File.WriteAllText(directory + "/build.txt", result); return result;
            }
            finally { PlayerSettings.enableFrameTimingStats = stats; AssetDatabase.SaveAssets(); }
        }
    }
}
