using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 recovery (SPEC-MAP-OVERHAUL-308 Temporary Exceptions "lost bake inputs"; Docs/Handoff/Recovery308, gap M-3).
    // On 2026-10-06 Art/World was deleted and came back only in part: files the map bundles recorded as bake inputs are gone and
    // their generators cannot run. MapOverhaul308 compares every recorded input with the file on disk - this is that comparison,
    // with one more answer:
    //   on disk, the recorded sha                                           fine
    //   on disk, another sha                                                REFUSED, always ("input changed since the bake")
    //   not on disk, a row of WaiverFile names the path WITH the bundle's sha   TRUSTED on the bundle's own record, one line each
    //   not on disk, anything else                                          REFUSED ("input missing"), as before
    // The list is data (beside the bake tools, tracked). A row waives one path with one sha: a bundle baked from other bytes of that
    // path is not waived, and the all-zero sha of a requires.* ledger that was not applied at bake time can never be a row. Only a
    // file of the tree that was lost (LostTree) can be a row: an input anywhere else was never lost and is always compared. A list
    // that does not read whole waives nothing and is itself a problem.
    // Pure on purpose - no Unity type, no static state: Tools/Unity/Stage308_recover_mapin/Offline/rule_test.py compiles this file
    // alone and runs the cases. Exit of the exception: the lost files stand again with their recorded sha, or the bundles are baked
    // again from a rebuilt chain, and the list has no row.
    public static class MapBundleInputs308
    {
        public const string WaiverFile = "Tools/Art/map308_lost_inputs.json";
        const string WaiverId = "map308_lost_inputs";
        const string LostTree = "Art/World/";   // the deleted tree every lost bake input lived in

        public sealed class Verdict
        {
            public readonly List<string> Problems = new List<string>(), Trusted = new List<string>(), Notes = new List<string>();
        }

        public sealed class Waiver
        {
            public readonly Dictionary<string, string> Sha = new Dictionary<string, string>(StringComparer.Ordinal);   // repo path -> sha256
            public string LostOn = "?", Problem = "";
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, Math.Min(10, sha.Length));
        static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>sha256 of a file, upper-case hex (the form Harness303.Sha prints).</summary>
        public static string Sha(string file)
        {
            using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        /// <summary>The lost-input list of a repo root. No file = no row (every absent input is refused, as before the list existed).</summary>
        public static Waiver ReadWaiver(string repoRoot)
        {
            string file = Path.Combine(repoRoot, WaiverFile.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(file) ? ParseWaiver(File.ReadAllText(file, Encoding.UTF8)) : new Waiver();
        }

        /// <summary>The list's text -> rows. The rows are read as `{"path": "...", "sha256": "..."` (that order, the form the stage's
        /// make_data.py writes) and must number "count": a row in another form is not read, the count then differs and NO row is taken.</summary>
        public static Waiver ParseWaiver(string text)
        {
            var w = new Waiver(); text = text ?? "";
            var count = Regex.Match(text, "\"count\"\\s*:\\s*([0-9]+)"); var lost = Regex.Match(text, "\"lostOn\"\\s*:\\s*\"([^\"\\\\]*)\"");
            if (lost.Success && lost.Groups[1].Value.Length > 0) w.LostOn = lost.Groups[1].Value;
            if (!Regex.IsMatch(text, "\"id\"\\s*:\\s*\"" + WaiverId + "\"")) w.Problem = "its id is not \"" + WaiverId + "\"";
            else if (!count.Success) w.Problem = "it has no \"count\"";
            else
            {
                int n = 0;
                foreach (Match m in Regex.Matches(text, "\\{\\s*\"path\"\\s*:\\s*\"([^\"\\\\]*)\"\\s*,\\s*\"sha256\"\\s*:\\s*\"([^\"\\\\]*)\""))
                {
                    string path = m.Groups[1].Value, sha = m.Groups[2].Value; n++;
                    bool pathOk = path.Length > 0 && path[0] != '/' && path.IndexOf(':') < 0 && Array.TrueForAll(path.Split('/'), part => part.Length > 0 && part != "." && part != "..");
                    bool shaOk = sha.Length == 64 && sha.Trim('0').Length > 0 && Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$");
                    if (!pathOk) w.Problem = "row " + n + ": '" + path + "' is not a repo path";
                    else if (!path.StartsWith(LostTree, StringComparison.Ordinal)) w.Problem = "row " + n + ": " + path + " is not under " + LostTree + " (only a file of the lost tree can be a row)";
                    else if (!shaOk) w.Problem = "row " + n + " (" + path + "): its sha256 is not 64 hex digits of a real file";
                    else if (w.Sha.ContainsKey(path)) w.Problem = "row " + n + ": " + path + " is listed twice";
                    else { w.Sha[path] = sha; continue; }
                    break;
                }
                if (w.Problem.Length == 0 && n != int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture))
                    w.Problem = "it says count " + count.Groups[1].Value + " but " + n + " row(s) read as {\"path\", \"sha256\"}";
            }
            if (w.Problem.Length > 0) w.Sha.Clear();
            return w;
        }

        /// <summary>Every recorded input of a bundle against the files under repoRoot. Problems = why the bundle must not be imported;
        /// Trusted = one line per absent input taken on the bundle's record; Notes = rows of the list that are no longer needed.</summary>
        public static Verdict Classify(string repoRoot, IEnumerable<(string role, string path, string sha)> inputs, string stage)
        {
            var v = new Verdict(); var w = ReadWaiver(repoRoot);
            if (w.Problem.Length > 0) v.Problems.Add("lost-input list " + WaiverFile + " is not usable (" + w.Problem + "): no absent input is taken on record");
            foreach (var i in inputs)
            {
                if (string.IsNullOrEmpty(i.path) || string.IsNullOrEmpty(i.sha)) { v.Problems.Add("inputs[] entry without a path or sha (" + i.role + ")"); continue; }
                string abs = Path.Combine(repoRoot, i.path.Replace('/', Path.DirectorySeparatorChar));
                bool listed = w.Sha.TryGetValue(i.path, out string lost);
                if (!File.Exists(abs))
                {
                    if (listed && Same(lost, i.sha) && !Directory.Exists(abs))
                        v.Trusted.Add("TRUSTED absent input (" + i.role + "): " + i.path + " - not on disk since " + w.LostOn + ", taken on the bundle's record (sha " + Short(i.sha) + ", " + WaiverFile + ")");
                    else
                        v.Problems.Add("input missing (" + i.role + "): " + i.path + (Directory.Exists(abs) ? " - a folder stands at that path"
                            : listed ? " - the lost-input list names this path with sha " + Short(lost) + ", this bundle recorded " + Short(i.sha) + ": it was not baked from the lost file" : ""));
                    continue;
                }
                string now = Sha(abs);
                // name WHAT changed: which input (its role in the bake), the recorded and the present sha, when the file was written
                if (!Same(now, i.sha)) v.Problems.Add("input changed since the bake (" + i.role + "): " + i.path + " - sha " + Short(i.sha) + " -> " + Short(now) + ", written "
                    + File.GetLastWriteTime(abs).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " - bake again (map308_bake.py --stage " + (string.IsNullOrEmpty(stage) ? "base" : stage) + ")"
                    + (listed ? " [a row of the lost-input list: the file is back with other bytes - the list never excuses a file that is on disk]" : ""));
                else if (listed) v.Notes.Add("NOTE lost-input list: " + i.path + " is on disk again with the recorded sha - take its row out of " + WaiverFile);
            }
            return v;
        }
    }
}
