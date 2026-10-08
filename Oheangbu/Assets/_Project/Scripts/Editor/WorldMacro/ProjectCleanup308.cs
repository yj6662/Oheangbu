using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 project cleanup, one switch per removal. Queue-safe (no dialog).
    //   odin   2026-10-08 user: "프로젝트에 odin 없애자. 쓰지도 않잖아 지금" - no script, asmdef or asset referenced Sirenix.
    //          Removes Assets/Plugins/Sirenix and the ODIN_* scripting defines of every build target. A copy of the folder is
    //          taken outside the repository first (by the caller), because the package is a paid asset.
    public static class ProjectCleanup308
    {
        public static string Run(string command)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only";
            if ((command ?? "").Trim() != "odin") return "REFUSED odin";
            int targets = 0, removed = 0;
            foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL, NamedBuildTarget.Server })
            {
                string before;
                try { before = PlayerSettings.GetScriptingDefineSymbols(target); } catch (Exception) { continue; }
                var kept = before.Split(';').Where(s => s.Length > 0 && !s.StartsWith("ODIN_", StringComparison.Ordinal)).ToArray();
                string after = string.Join(";", kept);
                if (after != before) { PlayerSettings.SetScriptingDefineSymbols(target, after); targets++; removed += before.Split(';').Length - kept.Length; }
            }
            bool folder = AssetDatabase.IsValidFolder("Assets/Plugins/Sirenix");
            bool deleted = folder && AssetDatabase.DeleteAsset("Assets/Plugins/Sirenix");
            return "odin: folder " + (folder ? (deleted ? "deleted" : "NOT deleted") : "absent") + ", defines removed " + removed + " on " + targets + " build target(s)";
        }
    }
}
