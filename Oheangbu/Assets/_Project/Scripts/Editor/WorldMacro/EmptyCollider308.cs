using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308: a MeshCollider whose mesh has no triangles makes Unity log "must have at least one non-degenerate triangle" at load,
    // which fails every Play check that counts logged errors (FolkloreRuntimeChecks298, main-start). Such a collider collides with
    // nothing, so disabling it changes no behaviour. Known case: Finish297_Hwanggyeong .../Physical_SourceFaces whose clipped
    // collider mesh (Finish297/Hwanggyeong/Clipped/Physical_SourceFaces_collider_0.asset) is empty.
    // Queue: Oheangbu.EditorTools.WorldMacro.EmptyCollider308 Run "scan" | "apply[:scene=arch296|folk298|main]" | "revert[:scene=...]"
    // Idempotent (an already disabled collider is skipped), scene backups + ledger under Art/Playtest308/EmptyCollider308/, protected trees skipped.
    public static class EmptyCollider308
    {
        [Serializable] sealed class Item { public string scene = "", path = "", mesh = ""; }
        [Serializable] sealed class Ledger { public List<Item> items = new List<Item>(); }
        static string Dir => PostLedger308.RepoPath("Art/Playtest308/EmptyCollider308");
        static string LedgerPath => Path.Combine(Dir, "ledger.json");

        public static string Run(string command)
        {
            try
            {
                var parts = (command ?? "").Trim().Split(':');
                var options = PostLedger308.Options(parts.Skip(1));
                switch (parts[0])
                {
                    case "scan": return Pass(options, false, false);
                    case "apply": return Pass(options, true, false);
                    case "revert": return Pass(options, true, true);
                    default: return "REFUSED unknown command (scan | apply[:scene=..] | revert[:scene=..])";
                }
            }
            catch (PostLedger308.Refused e) { return "refused: " + e.Message; }
            catch (Exception e) { return "FAILED " + e; }
        }

        static bool Empty(Mesh mesh)
        {
            if (mesh == null) return false;
            for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetIndexCount(i) >= 3) return false;
            return true;
        }

        static string Pass(Dictionary<string, string> options, bool write, bool revert)
        {
            PostLedger308.RequireEditable();
            var ledger = File.Exists(LedgerPath) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath)) : new Ledger();
            var sb = new StringBuilder("EmptyCollider308 " + (revert ? "revert" : write ? "apply" : "scan") + "\n");
            string utc = PostLedger308.Utc();
            foreach (var path in PostLedger308.Select(options))
            {
                var scene = PostLedger308.Open(path); int changed = 0;
                sb.AppendLine(" " + PostLedger308.Short(path));
                foreach (var c in PostLedger308.All<MeshCollider>(scene))
                {
                    if (PostLedger308.UnderProtectedTree(c.transform)) continue;
                    string p = PostLedger308.PathOf(c.transform);
                    bool recorded = ledger.items.Any(x => x.scene == path && x.path == p);
                    if (revert)
                    {
                        if (!recorded || c.enabled) continue;
                        c.enabled = true; changed++; ledger.items.RemoveAll(x => x.scene == path && x.path == p);
                        sb.AppendLine("  re-enabled " + p); continue;
                    }
                    if (!Empty(c.sharedMesh)) continue;
                    sb.AppendLine("  empty mesh " + PostLedger308.AssetRef(c.sharedMesh) + " on " + p + (c.enabled ? "" : " (already disabled)"));
                    if (!write || !c.enabled) continue;
                    c.enabled = false; changed++;
                    if (!recorded) ledger.items.Add(new Item { scene = path, path = p, mesh = PostLedger308.AssetRef(c.sharedMesh) });
                }
                if (write && changed > 0)
                {
                    sb.AppendLine("  backup " + PostLedger308.Backup(Path.Combine(Dir, "Backups"), utc, path));
                    PostLedger308.SaveScene(scene);
                    sb.AppendLine("  saved " + changed + " change(s)");
                }
                else sb.AppendLine("  " + (write ? "no-op" : "scan only"));
            }
            if (write) { Directory.CreateDirectory(Dir); File.WriteAllText(LedgerPath, JsonUtility.ToJson(ledger, true)); sb.AppendLine(" ledger " + LedgerPath); }
            return sb.ToString().TrimEnd();
        }
    }
}
