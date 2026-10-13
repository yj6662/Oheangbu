using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // plants that stand on a prepared platform, pond, street or wall bench (Out/veg_mask.bytes, uint8 1501 x 1001, written by
    // Tools/Art/hanyang308_terrain.py) leave their sheets through the BuildingFix308.VegRows ledger, tag "hanyang308".
    //   veg:plan | veg:apply | veg:revert
    public static partial class Hanyang308
    {
        const string VegTag = "hanyang308";
        [Serializable] sealed class VegCfg { public string[] sheets = Array.Empty<string>(); }

        static string Veg(string verb)
        {
            if (verb == "revert") return BuildingFix308.VegRowsRevert(VegTag, .05f);
            if (verb != "plan" && verb != "apply") throw new Refuse("use veg:plan | veg:apply | veg:revert");
            string maskFile = Path.Combine(OutDir, "veg_mask.bytes"), cfgFile = Path.Combine(StageDir, "veg308.json");
            if (!File.Exists(maskFile) || !File.Exists(cfgFile)) throw new Refuse("no " + maskFile + " or " + cfgFile);
            var mask = File.ReadAllBytes(maskFile); const int W = 1001, H = 1501; if (mask.Length != W * H) throw new Refuse("mask size " + mask.Length);
            var cfg = JsonUtility.FromJson<VegCfg>(File.ReadAllText(cfgFile)); var asks = new List<BuildingFix308.VegRowAsk308>(); var per = new List<string>();
            foreach (string path in cfg.sheets)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.WorldMacroDressingSheetSO>(path); if (sheet == null || sheet.FixedPlacements == null) { per.Add(Path.GetFileName(path) + " MISSING"); continue; }
                int n = 0;
                foreach (var fp in sheet.FixedPlacements)
                {
                    if (fp == null) continue; int j = Mathf.RoundToInt(fp.Position.x / 4f), i = Mathf.RoundToInt(fp.Position.z / 4f); if (i < 0 || j < 0 || i >= H || j >= W || mask[i * W + j] == 0) continue;
                    asks.Add(new BuildingFix308.VegRowAsk308 { sheet = path, id = fp.Id ?? "", proto = fp.PrototypeId ?? "", at = fp.Position }); n++;
                }
                per.Add(Path.GetFileName(path) + " " + n + " of " + sheet.FixedPlacements.Length);
            }
            string head = "Hanyang308 veg:" + verb + " rows " + asks.Count + " [" + string.Join(" | ", per) + "]\n";
            if (asks.Count == 0) return head + "nothing to remove";
            return head + (verb == "apply" ? BuildingFix308.VegRowsApply(VegTag, asks, .05f) : BuildingFix308.VegRowsPlan(VegTag, asks, .05f));
        }
    }
}
