using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 unused-asset cleanup (user 2026-09-30: "지금 안 쓰고 앞으로도 안 쓸 에셋은 조사해서 삭제"). Queue-safe, no dialogs.
    //   analyze            read-only: roots -> AssetDatabase.GetDependencies closure -> Art/Performance/Perf307/Unused/ (report.txt,
    //                      unused.txt = one asset path per line, roots.txt, scenes.txt). Code/shader files are never listed.
    //   move:<list file>   moves every listed asset (+ .meta) to C:/Users/yj666/Oheangbu_Unused307/<same relative path>, then
    //                      AssetDatabase.Refresh. Refuses protected paths, code, and anything the last analyze did not list as unused.
    // Roots: build + EditorBuildSettings scenes, every scene under Assets/_Project/Scenes, every Resources folder, ProjectSettings guid
    // references, "Assets/..." path literals and 32-hex guid literals in all .cs files, the asset catalog prefabs
    // (Screenshots/AssetCatalog/index.html = curated future placement props), the protected paths.
    public static class UnusedAssets307
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Out => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/Unused"));
        const string Quarantine = "C:/Users/yj666/Oheangbu_Unused307";
        static readonly string[] Protected = {
            "Assets/_Project/Art/World/Watershed295", "Assets/_Project/Art/World/Reworld292", "Assets/_Project/Art/World/MountainTrail285",
            "Assets/Plugins", "Assets/_Project/Scenes", "Assets/Settings", "Assets/TextMesh Pro", "Assets/Resources" };
        static readonly string[] ProtectedFiles = { "W_Demo_Compact.unity", "03_Content.asset" };
        static readonly HashSet<string> CodeExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".cs", ".asmdef", ".asmref", ".dll", ".so", ".dylib", ".bundle", ".jar", ".aar", ".shader", ".hlsl", ".cginc", ".compute",
            ".shadergraph", ".shadersubgraph", ".uss", ".uxml", ".tss", ".rsp", ".xml", ".json", ".txt", ".md", ".inputactions", ".raytrace", ".pdb", ".mdb" };

        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return "refused: Edit mode, not compiling";
            try
            {
                if (arg == "analyze") return Analyze();
                if (arg.StartsWith("move:", StringComparison.Ordinal)) return Move(arg.Substring(5));
                return "refused: analyze | move:<list file>";
            }
            catch (Exception e) { return "FAIL " + e; }
        }

        static bool IsProtected(string p) =>
            Protected.Any(x => p.Equals(x, StringComparison.OrdinalIgnoreCase) || p.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase)) ||
            ProtectedFiles.Any(f => p.EndsWith("/" + f, StringComparison.OrdinalIgnoreCase));

        static string Analyze()
        {
            Directory.CreateDirectory(Out);
            var all = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)).ToList();
            var allSet = new HashSet<string>(all, StringComparer.Ordinal);
            var roots = new Dictionary<string, string>(StringComparer.Ordinal);
            void AddRoot(string p, string why) { if (p != null && allSet.Contains(p) && !roots.ContainsKey(p)) roots[p] = why; }
            var folderRoots = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var s in EditorBuildSettings.scenes) AddRoot(s.path, "build-settings");
            foreach (var s in new[] { "Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity", "Assets/_Project/Art/UI/Loading270/W_Compact_Loading.unity", "Assets/_Project/Scenes/World/W_Demo_Main.unity" }) AddRoot(s, "build-297");
            foreach (var p in all)
            {
                if (p.Contains("/Resources/") || p.Contains("/Editor Default Resources/")) AddRoot(p, "resources");
                if (IsProtected(p)) AddRoot(p, "protected");
                if (CodeExt.Contains(Path.GetExtension(p))) AddRoot(p, "code");
            }
            // ProjectSettings guid references (render pipelines, preloaded assets, input settings, always-included shaders...)
            var guidRx = new Regex(@"guid: ([0-9a-f]{32})");
            foreach (var f in Directory.GetFiles(Path.Combine(Root, "ProjectSettings"), "*.asset"))
                foreach (Match m in guidRx.Matches(File.ReadAllText(f))) AddRoot(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value), "project-settings:" + Path.GetFileName(f));
            // code literals: "Assets/..." paths (files and folders) and bare 32-hex guids
            var pathRx = new Regex("\"(Assets/[^\"\\n\\r]+?)/?\"");
            var hexRx = new Regex("\"([0-9a-f]{32})\"");
            foreach (var cs in all.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                string text; try { text = File.ReadAllText(Path.Combine(Root, cs)); } catch { continue; }
                foreach (Match m in pathRx.Matches(text))
                {
                    string lit = m.Groups[1].Value.TrimEnd('/');
                    if (allSet.Contains(lit)) AddRoot(lit, "code-path:" + Path.GetFileName(cs));
                    else if (AssetDatabase.IsValidFolder(lit) && lit.Count(c => c == '/') >= 2 && !folderRoots.ContainsKey(lit)) folderRoots[lit] = Path.GetFileName(cs);
                }
                foreach (Match m in hexRx.Matches(text)) AddRoot(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value), "code-guid:" + Path.GetFileName(cs));
            }
            foreach (var kv in folderRoots) foreach (var p in all) if (p.StartsWith(kv.Key + "/", StringComparison.Ordinal)) AddRoot(p, "code-folder:" + kv.Key + " (" + kv.Value + ")");
            // curated catalog = future placement props
            string catalog = Path.Combine(Root, "Screenshots/AssetCatalog/index.html");
            int catalogCount = 0;
            if (File.Exists(catalog))
                foreach (Match m in new Regex("(Assets/[^\"<>]+?\\.prefab)").Matches(File.ReadAllText(catalog))) { AddRoot(m.Groups[1].Value, "catalog"); catalogCount++; }

            // closure (chunked: one huge array call is fine, but chunks keep the editor responsive and the memory flat)
            var used = new HashSet<string>(roots.Keys, StringComparer.Ordinal);
            var rootList = roots.Keys.ToList();   // scripts too: a .cs.meta may carry default references
            for (int i = 0; i < rootList.Count; i += 200)
            {
                EditorUtility.UnloadUnusedAssetsImmediate();
                foreach (var d in AssetDatabase.GetDependencies(rootList.Skip(i).Take(200).ToArray(), true)) used.Add(d);
            }
            // materials / prefabs referenced only by other unused assets are unused too; nothing else to do: GetDependencies is transitive
            var unused = all.Where(p => !used.Contains(p) && !CodeExt.Contains(Path.GetExtension(p)) && !IsProtected(p)).ToList();

            long Size(string p) { try { return new FileInfo(Path.Combine(Root, p)).Length; } catch { return 0; } }
            string Group(string p)
            {
                var parts = p.Split('/');
                int depth = parts[1] == "_Project" ? (parts.Length > 3 && (parts[2] == "Art" || parts[2] == "Data") ? 5 : 4) : 3;
                return string.Join("/", parts.Take(Math.Min(depth, parts.Length - 1)));
            }
            var groups = new Dictionary<string, long[]>(StringComparer.Ordinal);   // total, used, unused, unusedCount
            foreach (var p in all)
            {
                string g = Group(p); if (!groups.TryGetValue(g, out var v)) groups[g] = v = new long[4];
                long s = Size(p); v[0] += s;
                if (used.Contains(p) || CodeExt.Contains(Path.GetExtension(p)) || IsProtected(p)) v[1] += s; else { v[2] += s; v[3]++; }
            }
            var sb = new StringBuilder();
            long totalUnused = unused.Sum(Size), total = all.Sum(Size);
            sb.AppendLine("#307 unused-asset analyze " + DateTime.Now.ToString("s") + " | assets " + all.Count + " (" + Gb(total) + ") | roots " + roots.Count + " (catalog prefabs " + catalogCount + ", code folders " + folderRoots.Count + ") | used " + used.Count + " | unused " + unused.Count + " (" + Gb(totalUnused) + ")");
            sb.AppendLine("group\ttotal_GB\tused_GB\tunused_GB\tunused_count");
            foreach (var kv in groups.OrderByDescending(k => k.Value[2]))
                if (kv.Value[2] > 0) sb.AppendLine(kv.Key + "\t" + Gb(kv.Value[0]) + "\t" + Gb(kv.Value[1]) + "\t" + Gb(kv.Value[2]) + "\t" + kv.Value[3]);
            File.WriteAllText(Path.Combine(Out, "report.txt"), sb.ToString(), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(Out, "unused.txt"), unused.OrderBy(p => p, StringComparer.Ordinal), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(Out, "roots.txt"), roots.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Value + "\t" + k.Key), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(Out, "code-folders.txt"), folderRoots.Select(k => k.Key + "\t" + k.Value), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(Out, "scenes.txt"), all.Where(p => p.EndsWith(".unity")).Select(p => (used.Contains(p) ? (roots.TryGetValue(p, out var w) ? "ROOT " + w : "USED(dep)") : "UNUSED") + "\t" + Gb(Size(p)) + "\t" + p), new UTF8Encoding(false));
            return string.Join("\n", sb.ToString().Split('\n').Take(40)) + "\n(report " + Out + ")";
        }

        static string Move(string listFile)
        {
            string last = Path.Combine(Out, "unused.txt");
            if (!File.Exists(last)) return "refused: run analyze first";
            var allowed = new HashSet<string>(File.ReadAllLines(last), StringComparer.Ordinal);
            var list = File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Distinct().ToList();
            var refused = list.Where(p => !allowed.Contains(p) || IsProtected(p) || CodeExt.Contains(Path.GetExtension(p))).ToList();
            if (refused.Count > 0) return "refused: " + refused.Count + " path(s) not in the last unused list / protected / code, e.g. " + string.Join(", ", refused.Take(5));
            int moved = 0, missing = 0; long bytes = 0; var log = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var p in list)
                {
                    string src = Path.Combine(Root, p), dst = Path.Combine(Quarantine, p);
                    if (!File.Exists(src)) { missing++; continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst)) File.Delete(dst);   // a re-run of the same list: the quarantine copy is the same file
                    bytes += new FileInfo(src).Length;
                    File.Move(src, dst);
                    if (File.Exists(src + ".meta")) { if (File.Exists(dst + ".meta")) File.Delete(dst + ".meta"); File.Move(src + ".meta", dst + ".meta"); }
                    moved++; log.Add(p);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            File.AppendAllLines(Path.Combine(Quarantine, "moved.txt"), log.Select(p => DateTime.Now.ToString("s") + "\t" + p), new UTF8Encoding(false));
            AssetDatabase.Refresh(ImportAssetOptions.Default);
            return "moved " + moved + " asset(s) (" + Gb(bytes) + ") to " + Quarantine + "; missing " + missing + "; log " + Quarantine + "/moved.txt";
        }

        static string Gb(long b) => (b / 1073741824.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }
}
