using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #299: vegetation around a moved house.
    //   preserve-find:<x>,<z>[:<radius>] — dressing sheets used by the active scene (WorldMacroDressingRenderer and
    //   CompactRebuildArtRenderer), preserved areas near the point, and fixed placements within the radius by kind
    public static partial class GroundFit299
    {
        static IEnumerable<WorldMacroDressingSheetSO> SceneSheets()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            return roots.SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true)).Select(r => r.Sheet)
                .Concat(roots.SelectMany(g => g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Select(r => r.Sheet))
                .Where(s => s != null).Distinct();
        }

        static string PreserveFind(string argument)
        {
            var a = argument.Split(':');
            var xz = a[0].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            float radius = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 20f;
            var p = new Vector3(xz[0], 0, xz[1]);
            var sb = new StringBuilder();
            foreach (var sheet in SceneSheets())
            {
                string path = AssetDatabase.GetAssetPath(sheet);
                sb.AppendLine($"sheet {path} ({new System.IO.FileInfo(System.IO.Path.GetFullPath(path)).Length / 1048576f:F0} MB): preserved {sheet.PreservedAreas.Length}, fixed placements {sheet.FixedPlacements.Count()}");
                for (int i = 0; i < sheet.PreservedAreas.Length; i++)
                {
                    var area = sheet.PreservedAreas[i];
                    var d = new Vector2(area.Centre.x - p.x, area.Centre.z - p.z).magnitude;
                    if (d > radius + Mathf.Max(area.HalfSize.x, area.HalfSize.y)) continue;
                    sb.AppendLine($"  preserved [{i}] {area.Id} centre {area.Centre.ToString("F1")} half {area.HalfSize.ToString("F1")} yaw {area.Yaw:F0} excludeProcedural {area.ExcludeProcedural} dist {d:F1}");
                }
                var kinds = new Dictionary<string, int>();
                foreach (var f in sheet.FixedPlacements)
                {
                    if (new Vector2(f.Position.x - p.x, f.Position.z - p.z).magnitude > radius) continue;
                    var proto = sheet.Prototypes.FirstOrDefault(x => x != null && x.Id == f.PrototypeId);
                    string kind = proto != null ? proto.Category.ToString() : "?";
                    kinds[kind] = kinds.TryGetValue(kind, out int c) ? c + 1 : 1;
                }
                sb.AppendLine($"  fixed placements within {radius} m: " + string.Join(", ", kinds.Select(k => k.Key + " " + k.Value)));
            }
            return sb.Length > 0 ? sb.ToString() : "no dressing sheet in the active scene";
        }
    }
}
