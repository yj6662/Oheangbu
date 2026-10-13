using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // the baked grass seed field (CompactGrassField266) keeps the height of every seed: after stage n8 the seeds on changed ground follow it
    // by (n8 - n7), and the seeds on a prepared platform, pond, street or wall bench (Out/veg_mask.bytes) are taken out.
    //   grass:plan | grass:apply | grass:revert     backup = a byte copy of the asset (+ .meta); revert copies it back when the asset is still the file apply wrote
    public static partial class Hanyang308
    {
        const string GrassAsset = "Assets/_Project/Art/World/Finish297/Dressing/Grass297_4b81d642058447d47a9dd4c0d8dcbe89_Grass_db3fd9ac771e32a44bf7c3c946eef56e_MountainGrass.asset";
        const string SurfaceDir = "Assets/_Project/Art/World/CliffBoundary308/Surface";
        static string GrassLedgerFile => Path.Combine(StageDir, "Ledger", "grass.json");
        [Serializable] sealed class GrassLedger { public string stamp = "", backup = "", shaAfter = ""; public int moved, removed, cells; }

        static float[] FieldOf(string stage)
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(SurfaceDir + "/height_" + stage + ".bytes"); if (ta == null) throw new Refuse("no " + SurfaceDir + "/height_" + stage + ".bytes");
            var b = ta.bytes; var f = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, f, 0, f.Length * 4); if (f.Length != 1001 * 1501) throw new Refuse("field size " + f.Length); return f;
        }
        // the tile triangles: each 4 m quad = (00, 10, 01) and (11, 01, 10)
        static float SampleField(float[] f, float x, float z)
        {
            const int W = 1001, H = 1501; float u = Mathf.Clamp(x / 4f, 0, W - 1.001f), w = Mathf.Clamp(z / 4f, 0, H - 1.001f); int j = (int)u, i = (int)w; float fu = u - j, fw = w - i;
            float h00 = f[i * W + j], h10 = f[i * W + j + 1], h01 = f[(i + 1) * W + j], h11 = f[(i + 1) * W + j + 1];
            return fu + fw <= 1f ? h00 + (h10 - h00) * fu + (h01 - h00) * fw : h11 + (h01 - h11) * (1 - fu) + (h10 - h11) * (1 - fw);
        }
        static string ShaFile(string path) { using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }

        static string Grass(string verb)
        {
            string abs = Abs(GrassAsset);
            if (verb == "revert")
            {
                if (!File.Exists(GrassLedgerFile)) return "Hanyang308 grass:revert - no ledger";
                var led = JsonUtility.FromJson<GrassLedger>(File.ReadAllText(GrassLedgerFile)); if (ShaFile(abs) != led.shaAfter) throw new Refuse("the grass field is not the file grass:apply wrote (another tool changed it since)");
                File.Copy(Path.Combine(led.backup, Path.GetFileName(abs)), abs, true); AssetDatabase.ImportAsset(GrassAsset, ImportAssetOptions.ForceUpdate); File.Move(GrassLedgerFile, Path.Combine(led.backup, "grass.reverted.json"));
                return "Hanyang308 grass:revert - the field is the backup again (" + led.backup + ")";
            }
            if (verb != "plan" && verb != "apply") throw new Refuse("use grass:plan | grass:apply | grass:revert");
            if (verb == "apply" && File.Exists(GrassLedgerFile)) throw new Refuse("already applied (grass:revert first)");
            var field = AssetDatabase.LoadAssetAtPath<CompactGrassField266>(GrassAsset); if (field == null) throw new Refuse("no " + GrassAsset);
            string maskFile = Path.Combine(OutDir, "veg_mask.bytes"); if (!File.Exists(maskFile)) throw new Refuse("no " + maskFile);
            var mask = File.ReadAllBytes(maskFile); var from = FieldOf("n7"); var to = FieldOf("n8"); const int W = 1001;
            int moved = 0, removed = 0, cells = 0, total = 0; float maxMove = 0; bool write = verb == "apply"; string backup = "";
            if (write) { backup = BackupFiles("grass", abs, abs + ".meta"); }
            foreach (var cell in field.Cells)
            {
                if (cell == null || cell.Seeds == null || cell.Seeds.Length == 0) continue; total += cell.Seeds.Length;
                var keep = new List<CompactGrassField266.Seed>(cell.Seeds.Length); bool changed = false;
                foreach (var s in cell.Seeds)
                {
                    int j = Mathf.RoundToInt(s.Position.x / 4f), i = Mathf.RoundToInt(s.Position.z / 4f);
                    if (i >= 0 && j >= 0 && i < 1501 && j < W && mask[i * W + j] != 0) { removed++; changed = true; continue; }
                    float d = SampleField(to, s.Position.x, s.Position.z) - SampleField(from, s.Position.x, s.Position.z);
                    if (Mathf.Abs(d) > 0.02f) { var t = s; t.Position.y += d; keep.Add(t); moved++; changed = true; if (Mathf.Abs(d) > maxMove) maxMove = Mathf.Abs(d); }
                    else keep.Add(s);
                }
                if (!changed) continue; cells++;
                if (!write) continue;
                cell.Seeds = keep.ToArray();
                if (keep.Count > 0)
                {
                    float y0 = keep.Min(k => k.Position.y), y1 = keep.Max(k => k.Position.y); var b = cell.Bounds; float pad = 1.5f;
                    b.SetMinMax(new Vector3(b.min.x, y0 - pad, b.min.z), new Vector3(b.max.x, y1 + pad, b.max.z)); cell.Bounds = b;
                }
            }
            string said = "Hanyang308 grass:" + verb + " seeds " + total + " | follow the ground " + moved + " (max " + F(maxMove) + " m) | taken off platforms / streets " + removed + " | cells touched " + cells;
            if (!write) return said + " | plan only";
            field.Count = field.Cells.Where(c => c != null && c.Seeds != null).Sum(c => c.Seeds.Length);
            EditorUtility.SetDirty(field); AssetDatabase.SaveAssetIfDirty(field);
            var ledger = new GrassLedger { stamp = Stamp(), backup = backup, shaAfter = ShaFile(abs), moved = moved, removed = removed, cells = cells };
            Directory.CreateDirectory(Path.GetDirectoryName(GrassLedgerFile)); File.WriteAllText(GrassLedgerFile, JsonUtility.ToJson(ledger, true));
            return said + " | applied, backup " + backup;
        }
    }
}
