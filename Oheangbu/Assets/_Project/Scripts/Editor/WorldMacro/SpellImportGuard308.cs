using System;
using System.Collections.Generic;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 importer guard (SPEC-SPELL-120-308 section 6, 2026-10-04). The spell importer never writes a protected
    /// asset: a spell book or a summon profile that is one is listed as "SKIPPED (protected)" by dry, apply, verify and
    /// revert, and is not a target. Such a book keeps resolving from its own _entries, exactly as before #308.
    /// Protected = inside a protected tree, or a protected file name, or (second pass, same day) on a protected path: the
    /// original spell book and the summon profiles under Art/Demo/Summons, which the protected scene W_Demo_Compact.unity
    /// reads. What stays a target: the Architecture296 copy of the book (the one W_Demo_Main reads) and the summon profiles
    /// of the other unprotected folders.
    /// Plain string logic on asset paths, no Unity type: Tools/SpellVFX120/spell120_guard308.py compiles this one file and
    /// runs it outside the editor, and carries the same lists in python (the two are compared there).</summary>
    public static class SpellImportGuard308
    {
        // A path segment (a folder or a file name) that starts with one of these names is inside a protected tree.
        public static readonly string[] ProtectedTrees = { "Watershed295", "Reworld292", "MountainTrail285" };
        // Protected by file name, wherever the file sits.
        public static readonly string[] ProtectedFiles = { "W_Demo_Compact.unity", "03_Content.asset" };
        // Protected by asset path, letter case ignored: an entry that ends with '/' is a folder (everything under it), any
        // other entry is that one file. Neither sits in a protected tree; the protected scene W_Demo_Compact.unity reads both.
        public static readonly string[] ProtectedPaths =
        {
            "Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset",
            "Assets/_Project/Art/Demo/Summons/",
        };
        public const string SkipLabel = "SKIPPED (protected)";

        public static bool Protected(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return true;   // a target without a path is never written
            string[] segments = assetPath.Replace('\\', '/').Split('/');
            foreach (string segment in segments)
                foreach (string tree in ProtectedTrees)
                    if (segment.StartsWith(tree, StringComparison.OrdinalIgnoreCase)) return true;
            string file = segments[segments.Length - 1];
            foreach (string name in ProtectedFiles)
                if (string.Equals(file, name, StringComparison.OrdinalIgnoreCase)) return true;
            return OnProtectedPath(segments);
        }

        // ProtectedPaths against the path without its empty, "." and ".." segments. The path is judged from every folder
        // boundary on: the same asset named from the repository root or from a drive is protected too (never the reverse).
        static bool OnProtectedPath(string[] segments)
        {
            var kept = new List<string>();
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..") { if (kept.Count > 0) kept.RemoveAt(kept.Count - 1); continue; }
                kept.Add(segment);
            }
            string path = "/" + string.Join("/", kept) + "/";
            foreach (string entry in ProtectedPaths)
            {
                bool folder = entry.EndsWith("/", StringComparison.Ordinal);
                if (folder ? path.IndexOf("/" + entry, StringComparison.OrdinalIgnoreCase) >= 0
                           : path.EndsWith("/" + entry + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // The candidates an importer may write, in their own order. The others are added to skipped (when a list is given).
        public static List<string> Writable(IEnumerable<string> candidates, List<string> skipped)
        {
            var open = new List<string>();
            if (candidates == null) return open;
            foreach (string path in candidates)
            {
                if (!Protected(path)) open.Add(path);
                else if (skipped != null) skipped.Add(path);
            }
            return open;
        }

        // The statement in front of every write of the importer: a protected path that reached a write is a defect, not a skip.
        public static void Demand(string assetPath)
        {
            if (Protected(assetPath)) throw new InvalidOperationException("refused: '" + assetPath + "' is protected (a protected tree, file or path); the #308 importer never writes it");
        }
    }
}
