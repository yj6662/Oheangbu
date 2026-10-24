using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Vol2.Data;
using Oheangbu.Vol2.Import;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-a (D308-149) edit-mode checks of the vol.2 data layer, in the style of Riposte308Checks. Everything runs in memory:
    /// no asset is written, no scene is touched, no Play is needed. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2S5aChecks Run "all"      (vol2:check runs the same)
    ///   F  the two CSV copies and the two ScriptableObjects exist
    ///   H  SHA-256 of the CSV bytes now == the hashes stored in the assets and in the frozen manifest
    ///   R  the CSVs imported again give the same canonical DTO hash as the assets and the independent Python hash
    ///   L  NumbersV3SO + Vol2TableSO load through Vol2DataLoader (rebuilt DTOs hash to the frozen manifest): 414 numbers, 120 spells
    ///   G  golden replay (120 rows, rankings, efficiency, 12 budget event cases, 25 reaction cells) through the Unity-side copies
    ///   T  tampered copies of the asset data are refused by the loader (numbers, spells, manifest, swapped sources)
    ///   P  Oheangbu.Vol2.Rules / .Import reference no engine assembly; only the S5-b wiring assemblies (Spellcraft, App) reference Oheangbu.Vol2.*
    /// Not covered (S5-b and later): casting, contacts, the adapter, scenes, Play.</summary>
    public static class Vol2S5aChecks
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }
        [Serializable] sealed class Pack { public NumberRowData[] numbers; public SpellRowData[] spells; public ManifestData manifest; }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-checks: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-checks: editor busy (play / compile / import)";
            var r = new Report();
            try { Body(r); } catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static void Body(Report r)
        {
            string numbersPath = Vol2Paths.Full(Vol2Paths.NumbersCsv), tablePath = Vol2Paths.Full(Vol2Paths.TableCsv);
            var numbersSo = AssetDatabase.LoadAssetAtPath<NumbersV3SO>(Vol2Paths.NumbersAsset);
            var tableSo = AssetDatabase.LoadAssetAtPath<Vol2TableSO>(Vol2Paths.TableAsset);
            bool files = File.Exists(numbersPath) && File.Exists(tablePath), assets = numbersSo != null && tableSo != null;
            Check(r, "F1", files, "CSV copies under " + Vol2Paths.DataFolder);
            Check(r, "F2", assets, assets ? "NumbersV3SO + Vol2TableSO present" : "assets missing: run vol2:import first");
            string[] golden = { "source.json", "events.json", "balance.json", "import_manifest_v11.json" };
            bool goldenFiles = golden.All(f => File.Exists(Vol2Paths.Golden(f)));
            Check(r, "F3", goldenFiles, "golden fixtures in " + Vol2Paths.GoldenDir);
            if (!files || !assets || !goldenFiles) return;

            byte[] numbers = File.ReadAllBytes(numbersPath), table = File.ReadAllBytes(tablePath);
            string nSha = Vol2ImportManifestDto.Sha256(numbers), tSha = Vol2ImportManifestDto.Sha256(table);
            var trusted = Vol2Golden.ReadTrustedManifest(File.ReadAllText(Vol2Paths.Golden("import_manifest_v11.json")));
            Check(r, "H1", nSha == numbersSo.sourceSha256 && tSha == tableSo.sourceSha256, "CSV bytes == hashes stored in the assets (numbers " + Short(nSha) + ", table " + Short(tSha) + ")");
            Check(r, "H2", nSha == trusted.SourceHashes["numbers"] && tSha == trusted.SourceHashes["table"], "CSV bytes == frozen manifest source hashes");
            Check(r, "H3", Docs(r, "e0/numbers_v3.csv", nSha) & Docs(r, "vol2/vol2_table120_v11.csv", tSha), "CSV copies equal the Docs/Research originals (when present)");

            var import = Vol2TableV11Importer.Import(numbers, table, trusted, trusted.DtoHash);
            Check(r, "R1", import.CanCast, import.CanCast ? "CSV import accepted against the frozen manifest" : string.Join(" | ", import.Errors.Take(5)));
            if (import.CanCast)
            {
                Check(r, "R2", import.Manifest.DtoHash == trusted.DtoHash && import.Manifest.DtoHash == tableSo.manifest.dtoHash, "canonical DTO hash " + Short(import.Manifest.DtoHash) + " == frozen (independent Python) == asset");
                Check(r, "R3", import.Numbers.Rows.Count == 414 && import.Spells.Count == 120, "414 numbers / 120 spells from the CSVs");
            }

            var loaded = Vol2DataLoader.Load(numbersSo, tableSo, trusted);
            Check(r, "L1", loaded.Ok, loaded.Ok ? "assets load through the frozen manifest" : loaded.Error);
            if (!loaded.Ok) return;
            Check(r, "L2", loaded.Numbers.IsValidated && loaded.Numbers.Rows.Count == 414 && loaded.Spells.Count == 120, "validated numbers 414 / spells 120");
            Check(r, "L3", loaded.Spells.All(s => GlyphRulesV3.Build(s.Glyph, loaded.Numbers).Cost == s.Cost), "plans built from the loaded numbers agree with stored costs");
            var geok = loaded.Spells.Single(s => s.Letter == '걱');
            Check(r, "L4", geok.EffectTerms.Count == 0 && GlyphRulesV3.Build(geok.Glyph, loaded.Numbers).IsRecovery, "걱 is the heal-only exception (D308-145)");

            var g = Vol2Golden.Run(loaded.Numbers, loaded.Spells, File.ReadAllText(Vol2Paths.Golden("source.json")), File.ReadAllText(Vol2Paths.Golden("events.json")), File.ReadAllText(Vol2Paths.Golden("balance.json")));
            Check(r, "G1", g.Fail == 0, "golden PASS " + g.Pass + " / FAIL " + g.Fail + (g.Fail > 0 ? " first: " + g.FirstFailure : ""));

            // T: tampered copies (JSON round trip = deep copy of the serialized data) must never load.
            string json = JsonUtility.ToJson(new Pack { numbers = numbersSo.rows, spells = tableSo.spells, manifest = tableSo.manifest });
            void Tamper(string id, Action<Pack> edit, string ns = null, string ts = null)
            {
                var p = JsonUtility.FromJson<Pack>(json); edit(p);
                var o = Vol2Snapshot.Load(p.numbers, ns ?? numbersSo.sourceSha256, p.spells, ts ?? tableSo.sourceSha256, p.manifest, trusted);
                Check(r, id, !o.Ok && o.Numbers == null, o.Ok ? "TAMPERED DATA LOADED" : "refused: " + Trim(o.Error));
            }
            Tamper("T1", p => p.numbers.Single(x => x.key == "action.instant").value = "0.8");
            Tamper("T2", p => p.numbers.Single(x => x.key == "combat.parry.failureCost").value = "13");
            Tamper("T3", p => p.spells[3].cost = "99");
            Tamper("T4", p => p.spells[3].fields[5] += "!");
            Tamper("T5", p => p.spells[Array.FindIndex(p.spells, x => x.fields[0] == "각")].terms[1].amount = "0.8");
            Tamper("T6", p => p.manifest.dtoHash = new string('0', 64));
            Tamper("T7", p => p.spells = p.spells.Skip(1).ToArray());
            Tamper("T8", p => { }, tableSo.sourceSha256, numbersSo.sourceSha256);   // swapped source hashes
            Tamper("T9", p => p.manifest.schema = "old");

            var assembliesChecked = new[] { typeof(GlyphRulesV3).Assembly, typeof(Vol2Snapshot).Assembly };
            Check(r, "P1", assembliesChecked.All(a => !a.GetReferencedAssemblies().Any(n => n.Name.StartsWith("UnityEngine", StringComparison.Ordinal) || n.Name.StartsWith("UnityEditor", StringComparison.Ordinal))),
                "Rules and Import reference no UnityEngine / UnityEditor (noEngineReferences)");
            var users = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("Oheangbu.", StringComparison.Ordinal) && !a.GetName().Name.StartsWith("Oheangbu.Vol2.", StringComparison.Ordinal) && !a.GetName().Name.StartsWith("Oheangbu.EditorTools.Vol2", StringComparison.Ordinal))
                .Where(a => a.GetReferencedAssemblies().Any(n => n.Name.StartsWith("Oheangbu.Vol2.", StringComparison.Ordinal))).Select(a => a.GetName().Name).ToArray();
            // S5-b slice 1 wires the vol.2 route on purpose: Spellcraft (adapter, route) and App (installer, lab runner). Any other game assembly
            // (Core, Data, Combat, Drawing, BrushRender, Presentation ...) referencing Oheangbu.Vol2.* is still a layering violation.
            var allowed = new[] { "Oheangbu.Spellcraft", "Oheangbu.App" };
            var unexpected = users.Where(u => !allowed.Contains(u)).ToArray();
            Check(r, "P2", unexpected.Length == 0, unexpected.Length == 0 ? "only " + (users.Length == 0 ? "(nobody)" : string.Join(",", users)) + " reference Oheangbu.Vol2.* (whitelist: Spellcraft, App)" : "unexpected references from: " + string.Join(",", unexpected));
        }

        static bool Docs(Report r, string relative, string sha)
        {
            string path = Vol2Paths.DocsOriginal(relative);
            return !File.Exists(path) || Vol2ImportManifestDto.Sha256(File.ReadAllBytes(path)) == sha;
        }
        static string Short(string sha) => sha.Substring(0, 12);
        static string Trim(string s) => s != null && s.Length > 90 ? s.Substring(0, 90) + "..." : s;
    }
}
