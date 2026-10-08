using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 roots (TEST): the editor's check of the root collar + sink. Read only: no asset write, no scene write, no dialog, no static
    // state. The meshes, the gradient field and TreeRoots308.asset are made OFFLINE (Tools/Unity/Stage308_roots/_Tools/roots_build.py).
    //   Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.TreeRoots308Check Run "<command>"
    //   check          what the editor imported is what was built (mesh counts, bounds, field size, sheet names, prototype rows) and the
    //                  open scene's renderers use it                                   -> head `roots ok N/N` | `roots FAILED n/N`
    //   sample[:n]     n trees on a slope (default 40; a SAMPLE, never a census): the ground under the trunk by Physics.Raycast,
    //                  trunk bottom and collar foot against it, before and after the shift       -> head `roots sample ...`
    //   off | on       every CompactRebuildArtRenderer of the open scenes draws without / with the profile (memory only, for the
    //                  before / after stills; a reload or `on` restores it)
    // Report: <repo>/Art/World/Compact/Rebuild/Roots308/checks-roots.txt (and sample-roots.txt).
    public static class TreeRoots308Check
    {
        const string AssetPath = "Assets/_Project/Resources/Roots308/TreeRoots308.asset";
        const float SlopeMinDegrees = 15f, SlopeMaxDegrees = 32f, RayUp = 60f, RayLength = 200f, AboveGroundTolerance = .05f;

        sealed class Sheet
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Total;
            public void Add(bool ok, string label) { Total++; if (!ok) Failed++; Lines.Add((ok ? "ok      " : "FAILED  ") + label); }
            public void Info(string label) => Lines.Add("INFO    " + label);
        }

        static string F(float v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);
        static string Folder()
        {
            string folder = Path.Combine(Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName, "Art", "World", "Compact", "Rebuild", "Roots308");
            Directory.CreateDirectory(folder); return folder;
        }
        static CompactRebuildArtRenderer[] Renderers() => Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                if (command == "check" || command == "") return Check();
                if (command == "off" || command == "on") return Switch(command == "on");
                if (command == "sample" || command.StartsWith("sample:", StringComparison.Ordinal))
                {
                    int n = 40; if (command.Length > 7 && !int.TryParse(command.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return "refused: sample:<n>";
                    return Sample(Mathf.Clamp(n, 1, 400));
                }
                return "refused: unknown command '" + command + "' (check | sample[:n] | off | on)";
            }
            catch (Exception e) { return "roots FAILED: " + e.GetType().Name + " " + e.Message; }
        }

        static string Switch(bool on)
        {
            var all = Renderers(); foreach (var r in all) { r.Roots308Mode = on ? -1 : 0; r.Invalidate(); }
            SceneView.RepaintAll();
            return "roots " + (on ? "on" : "off") + ": " + all.Length + " renderer(s), memory only";
        }

        static string Check()
        {
            var sheet = new Sheet();
            var profile = AssetDatabase.LoadAssetAtPath<TreeRoots308SO>(AssetPath);
            sheet.Add(profile != null, "profile " + AssetPath);
            if (profile == null) return Finish(sheet, "checks-roots.txt", "roots");
            var loaded = Resources.Load<TreeRoots308SO>(TreeRoots308SO.ResourcePath);
            sheet.Add(loaded == profile, "Resources.Load(" + TreeRoots308SO.ResourcePath + ") is that asset");
            sheet.Info("version " + profile.Version + " " + profile.Decision + ", On " + profile.On + ", follow " + F(profile.FollowTrunk) + " (max " + F(profile.FollowMax) + "), sink base " + F(profile.SinkBase)
                + " gain " + F(profile.SlopeGain) + " max " + F(profile.SinkMax) + ", tan cap " + F(profile.TanCap));
            long added = 0;
            foreach (var s in profile.Meshes)
            {
                string name = s.Family + " LOD " + s.Lod;
                if (s.Source == null || s.Rooted == null) { sheet.Add(false, name + ": source or rooted mesh missing"); continue; }
                long before = 0, after = 0; for (int i = 0; i < s.Source.subMeshCount; i++) before += s.Source.GetIndexCount(i) / 3; for (int i = 0; i < s.Rooted.subMeshCount; i++) after += s.Rooted.GetIndexCount(i) / 3;
                added += after - before;
                sheet.Add(s.Rooted.subMeshCount == s.Source.subMeshCount && after - before == s.AddedTriangles,
                    name + ": " + s.Rooted.name + " triangles " + before + " -> " + after + " (+" + (after - before) + ", built +" + s.AddedTriangles + "), submeshes " + s.Rooted.subMeshCount + ", vertices +" + (s.Rooted.vertexCount - s.Source.vertexCount));
                float drop = s.Source.bounds.min.y - s.Rooted.bounds.min.y;
                sheet.Add(drop > .55f && drop < 1.1f && s.Rooted.bounds.max.y <= s.Source.bounds.max.y + 1e-4f, name + ": foot " + F(drop) + " m below the source mesh's lowest vertex, top unchanged");
                sheet.Add(s.Rooted.isReadable == s.Source.isReadable && s.Rooted.indexFormat == s.Source.indexFormat, name + ": readable / index format as the source");
            }
            sheet.Info("collar triangles over all twin meshes +" + added);
            sheet.Add(profile.Field != null && profile.HasField, "gradient field " + profile.FieldWidth + " x " + profile.FieldHeight + " nodes, " + (profile.Field != null ? profile.Field.dataSize : 0) + " B, baked from height " + (profile.FieldHeightSha256 ?? "").PadRight(12).Substring(0, 12));
            var all = Renderers(); int used = 0;
            foreach (string name in profile.Sheets)
            {
                var r = all.FirstOrDefault(x => x.Sheet != null && x.Sheet.name == name);
                if (r == null) { sheet.Info("no active renderer draws sheet " + name + " in the open scene(s)"); continue; }
                used++;
                var trees = r.Sheet.Prototypes.Where(p => p.Category == WorldMacroDressingSheetSO.Kind.Tree).ToArray();
                var without = trees.Where(p => profile.Find(p.Id) == null).Select(p => p.Id).ToArray();
                sheet.Add(without.Length == 0, name + ": " + trees.Length + " tree prototype(s), without a profile row: " + (without.Length == 0 ? "none" : string.Join(", ", without)));
                int twins = 0, parts = 0;
                foreach (var p in trees) for (int l = 0; l < p.Lods.Length; l++) foreach (var part in p.Lods[l].Parts) { if (part.Mesh == null) continue; parts++; if (profile.Rooted(part.Mesh) != part.Mesh) twins++; }
                sheet.Add(twins > 0, name + ": " + twins + " of " + parts + " tree parts have a rooted twin (card LODs and leaf-only meshes have none)");
                int count = r.Sheet.FixedPlacements.Count(fp => profile.Find(fp.PrototypeId) != null); float deepest = 0, highest = 0; int moved = 0;
                foreach (var fp in r.Sheet.FixedPlacements)
                {
                    var e = profile.Find(fp.PrototypeId); if (e == null) continue;
                    float dy = profile.Shift(e, fp.Position, fp.Euler, fp.Scale, out _, out _); if (dy != 0) moved++; deepest = Mathf.Min(deepest, dy); highest = Mathf.Max(highest, dy);
                }
                sheet.Add(moved == count && deepest >= -(profile.FollowMax + profile.SinkMax * 2f), name + ": " + moved + " of " + count + " trees shift, dy " + F(deepest) + " .. " + F(highest) + " m");
                sheet.Info(name + ": renderer '" + r.name + "' mode " + r.Roots308Mode + ", last Prepare rooted parts " + r.RootedParts308 + ", shifted " + r.ShiftedTrees308 + ", deepest " + F(r.DeepestShift308));
            }
            sheet.Add(used > 0, used + " of " + profile.Sheets.Length + " listed sheet(s) drawn in the open scene(s)");
            foreach (var r in all) if (r.Sheet != null && !profile.Applies(r.Sheet)) sheet.Info("untouched sheet: " + r.Sheet.name + " (renderer '" + r.name + "')");
            return Finish(sheet, "checks-roots.txt", "roots");
        }

        static string Sample(int n)
        {
            var profile = AssetDatabase.LoadAssetAtPath<TreeRoots308SO>(AssetPath); if (profile == null) return "refused: no " + AssetPath;
            var sheet = new Sheet(); var rows = new List<(WorldMacroDressingSheetSO.FixedPlacement fp, TreeRoots308SO.Entry e, float deg)>();
            foreach (var r in Renderers())
            {
                if (r.Sheet == null || !profile.Applies(r.Sheet)) continue;
                foreach (var fp in r.Sheet.FixedPlacements)
                {
                    var e = profile.Find(fp.PrototypeId); if (e == null) continue;
                    var off = Quaternion.Euler(fp.Euler) * new Vector3(e.TrunkOffset.x * fp.Scale, 0, e.TrunkOffset.y * fp.Scale);
                    float deg = Mathf.Atan(profile.Gradient(fp.Position.x + off.x, fp.Position.z + off.z).magnitude) * Mathf.Rad2Deg;
                    if (deg >= SlopeMinDegrees && deg <= SlopeMaxDegrees) rows.Add((fp, e, deg));
                }
            }
            if (rows.Count == 0) return "refused: no listed sheet is drawn in the open scene(s), or no tree stands on a " + SlopeMinDegrees + " - " + SlopeMaxDegrees + " degree slope";
            int step = Mathf.Max(1, rows.Count / n), floatBefore = 0, floatAfter = 0, footOut = 0, noGround = 0, taken = 0; float worstBefore = 0, worstAfter = float.NegativeInfinity, worstFoot = float.NegativeInfinity;
            for (int i = step / 2; i < rows.Count && taken < n; i += step)
            {
                var (fp, e, deg) = rows[i]; taken++;
                var off = Quaternion.Euler(fp.Euler) * new Vector3(e.TrunkOffset.x * fp.Scale, 0, e.TrunkOffset.y * fp.Scale);
                var g = profile.Gradient(fp.Position.x + off.x, fp.Position.z + off.z); var down = g.sqrMagnitude > 1e-8f ? -g.normalized : Vector2.zero;
                float reach = e.RootRadius * fp.Scale; var trunk = fp.Position + off; var edge = trunk + new Vector3(down.x, 0, down.y) * reach;
                if (!Ground(trunk, out float gt, out string ct) || !Ground(edge, out float ge, out _)) { noGround++; sheet.Info(fp.Id + ": no ground under the trunk or the root edge"); continue; }
                float dy = profile.Shift(e, fp.Position, fp.Euler, fp.Scale, out float follow, out float sink);
                float before = fp.Position.y - ge, after = fp.Position.y + dy - ge, foot = fp.Position.y + dy - e.Depth * fp.Scale - ge;
                if (before > AboveGroundTolerance) floatBefore++; if (after > AboveGroundTolerance) floatAfter++; if (foot > 0) footOut++;
                worstBefore = Mathf.Max(worstBefore, before); worstAfter = Mathf.Max(worstAfter, after); worstFoot = Mathf.Max(worstFoot, foot);
                sheet.Info(fp.Id + " " + fp.PrototypeId + " slope " + F(deg, "0.0") + " scale " + F(fp.Scale) + ": trunk bottom " + F(fp.Position.y - gt) + " m over the ground under it (" + ct + "), root edge " + F(before) + " -> " + F(after)
                    + " (dy " + F(dy) + " = follow " + F(follow) + " - sink " + F(sink) + "), collar foot " + F(foot));
            }
            sheet.Add(footOut == 0, "collar foot in the ground at the downhill root edge: " + (taken - noGround - footOut) + " of " + (taken - noGround) + " (worst " + F(worstFoot) + " m)");
            string head = "roots sample " + taken + " of " + rows.Count + " trees on " + SlopeMinDegrees + " - " + SlopeMaxDegrees + " deg: root edge > " + F(AboveGroundTolerance) + " m above ground before " + floatBefore + " (worst " + F(worstBefore)
                + " m), after " + floatAfter + " (worst " + F(worstAfter) + " m), collar foot above ground " + footOut + ", no ground " + noGround;
            File.WriteAllText(Path.Combine(Folder(), "sample-roots.txt"), head + "\n" + string.Join("\n", sheet.Lines) + "\n", new UTF8Encoding(false));
            return head;
        }

        // First non-trigger hit straight down. Edit Mode has the terrain and scene colliders; the Play-time tree stand-ins are not there.
        static bool Ground(Vector3 at, out float y, out string collider)
        {
            y = 0; collider = "";
            if (!Physics.Raycast(new Vector3(at.x, at.y + RayUp, at.z), Vector3.down, out var hit, RayLength, ~0, QueryTriggerInteraction.Ignore)) return false;
            y = hit.point.y; collider = hit.collider != null ? hit.collider.name : ""; return true;
        }

        static string Finish(Sheet sheet, string file, string word)
        {
            string head = sheet.Failed == 0 ? word + " ok " + sheet.Total + "/" + sheet.Total : word + " FAILED " + sheet.Failed + "/" + sheet.Total;
            File.WriteAllText(Path.Combine(Folder(), file), head + "\n" + string.Join("\n", sheet.Lines) + "\n", new UTF8Encoding(false));
            return head;
        }
    }
}
