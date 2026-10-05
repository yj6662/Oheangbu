using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 hitch attribution (Play only): record the Unity Profiler for N frames, then aggregate the main thread hierarchy of those
    // frames (total ms per marker path, depth <= 6) and list the top items. Also the render thread top items.
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.ProfTop307 Run "top:<name>[:frames=180]" | status
    // Output Art/Performance/Perf307/Prof/<name>.txt. The profiler is switched off again afterwards.
    public static class ProfTop307
    {
        static string status = "idle", name = ""; static int want, first = -1; static bool hooked, wasEnabled;
        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg == "status") return status;
            if (!arg.StartsWith("top:", StringComparison.Ordinal)) return "refused: top:<name>[:frames=180] | status";
            if (!EditorApplication.isPlaying) return "refused: Play only";
            if (status == "running") return "refused: running";
            var parts = arg.Substring(4).Split(':'); name = parts[0]; want = 180;
            foreach (var p in parts.Skip(1)) { var kv = p.Split('='); if (kv.Length == 2 && kv[0] == "frames") int.TryParse(kv[1], out want); }
            wasEnabled = ProfilerDriver.enabled; ProfilerDriver.ClearAllFrames(); ProfilerDriver.enabled = true; first = -1; status = "running";
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return "profiling " + want + " frames for " + name;
        }

        static void Tick()
        {
            if (status != "running") return;
            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (first < 0) { if (lastFrame >= 0) first = lastFrame + 2; return; }
            if (lastFrame < first + want) return;
            ProfilerDriver.enabled = wasEnabled;
            try { status = "done " + Write(first, first + want); } catch (Exception e) { status = "FAIL " + e.Message; }
        }

        static string Write(int from, int to)
        {
            var sb = new StringBuilder("#307 prof-top " + name + " " + DateTime.Now.ToString("s") + " frames " + from + ".." + to + "\n");
            foreach (var thread in new[] { 0, 1 })
            {
                var totals = new Dictionary<string, double>(); var calls = new Dictionary<string, int>(); double frameTotal = 0; int frames = 0;
                for (int f = from; f < to; f++)
                {
                    using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    {
                        if (view == null || !view.valid) continue;
                        frames++; frameTotal += view.frameTimeMs;
                        var stack = new Stack<(int id, string path, int depth)>(); stack.Push((view.GetRootItemID(), "", 0));
                        var children = new List<int>();
                        while (stack.Count > 0)
                        {
                            var (id, path, depth) = stack.Pop();
                            children.Clear(); view.GetItemChildren(id, children);
                            foreach (var c in children)
                            {
                                string n = view.GetItemName(c); float ms = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
                                if (ms < .05f) continue;
                                string p = path.Length == 0 ? n : path + " > " + n;
                                totals[p] = (totals.TryGetValue(p, out var t) ? t : 0) + ms; calls[p] = (calls.TryGetValue(p, out var k) ? k : 0) + 1;
                                if (depth < 6) stack.Push((c, p, depth + 1));
                            }
                        }
                    }
                }
                sb.AppendLine((thread == 0 ? "MAIN" : "RENDER/thread1") + " frames " + frames + " mean frame " + (frames > 0 ? frameTotal / frames : 0).ToString("0.00") + " ms");
                foreach (var kv in totals.OrderByDescending(k => k.Value).Take(60))
                    sb.AppendLine("  " + (kv.Value / Math.Max(1, frames)).ToString("0.000", CultureInfo.InvariantCulture) + " ms/frame  " + kv.Key);
            }
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/Prof")); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name + ".txt"); File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }
    }
}
