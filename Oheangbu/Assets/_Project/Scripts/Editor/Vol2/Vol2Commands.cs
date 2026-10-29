using System;
using System.IO;
using System.Linq;
using Oheangbu.Vol2.Data;
using Oheangbu.Vol2.Import;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-a (D308-149): editor commands of the vol.2 data layer. Queue (editor must be idle, no Play, no compile):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2Commands Run "vol2:import"
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2Commands Run "vol2:check"
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2Commands Run "vol2:status"
    /// vol2:import reads the two CSV copies under Data/Spells/Vol2, imports them through Vol2TableV11Importer against the frozen
    /// manifest (Golden/import_manifest_v11.json, computed independently in Python), replays the golden fixtures, and only then writes
    /// NumbersV3SO + Vol2TableSO. Any refusal returns "REFUSED ..." as text, writes nothing and opens no dialog (a modal would stop the
    /// queue). vol2:check re-hashes the CSVs and replays the golden through the loaded assets (Vol2S5aChecks). Neither command touches
    /// the live 120-glyph tables, a scene, or any gameplay class.</summary>
    public static class Vol2Commands
    {
        [Serializable] sealed class ImportReport
        {
            public string status, dtoHash, numbersSha256, tableSha256, numbersAsset, tableAsset;
            public int numbers, spells, goldenPass, goldenFail;
            public bool createdNumbers, createdTable;
        }
        [Serializable] sealed class StatusReport
        {
            public bool numbersCsv, tableCsv, numbersAsset, tableAsset, goldenFixtures, editorIdle;
            public string numbersSha256, tableSha256, assetDtoHash, frozenDtoHash;
            public bool csvMatchesAssets, assetHashMatchesFrozen;
        }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("vol2:", StringComparison.Ordinal)) c = c.Substring(5);
            if (c != "import" && c != "check" && c != "status") return "REFUSED vol2: commands are vol2:import | vol2:check | vol2:status";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2:" + c + ": editor busy (play / compile / import)";
            try
            {
                if (c == "check") return Vol2S5aChecks.Run("all");
                return c == "import" ? Import() : Status();
            }
            catch (Exception e) { return "REFUSED vol2:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        static string Import()
        {
            string numbersPath = Vol2Paths.Full(Vol2Paths.NumbersCsv), tablePath = Vol2Paths.Full(Vol2Paths.TableCsv);
            foreach (string path in new[] { numbersPath, tablePath, Vol2Paths.Golden("import_manifest_v11.json"), Vol2Paths.Golden("source.json"), Vol2Paths.Golden("events.json"), Vol2Paths.Golden("balance.json") })
                if (!File.Exists(path)) return "REFUSED vol2:import: missing file " + path;
            byte[] numbers = File.ReadAllBytes(numbersPath), table = File.ReadAllBytes(tablePath);
            var trusted = Vol2Golden.ReadTrustedManifest(File.ReadAllText(Vol2Paths.Golden("import_manifest_v11.json")));

            var result = Vol2TableV11Importer.Import(numbers, table, trusted, trusted.DtoHash);
            if (!result.CanCast) return "REFUSED vol2:import: " + result.Errors.Count + " error(s)\n" + string.Join("\n", result.Errors.Take(20).Select(e => e.ToString()));

            var golden = Vol2Golden.Run(result.Numbers, result.Spells, File.ReadAllText(Vol2Paths.Golden("source.json")), File.ReadAllText(Vol2Paths.Golden("events.json")), File.ReadAllText(Vol2Paths.Golden("balance.json")));
            if (golden.Fail != 0) return "REFUSED vol2:import: golden " + golden.Fail + " failure(s); first: " + golden.FirstFailure;

            // Make sure the editor knows the folder and both CSVs before assets are created beside them.
            AssetDatabase.ImportAsset(Vol2Paths.DataFolder, ImportAssetOptions.ImportRecursive);
            if (!AssetDatabase.IsValidFolder(Vol2Paths.DataFolder)) return "REFUSED vol2:import: " + Vol2Paths.DataFolder + " is not known to the AssetDatabase yet (run assets-refresh, then retry)";
            string nSha = Vol2ImportManifestDto.Sha256(numbers), tSha = Vol2ImportManifestDto.Sha256(table);
            var report = new ImportReport { numbersAsset = Vol2Paths.NumbersAsset, tableAsset = Vol2Paths.TableAsset };

            var numbersSo = LoadOrCreate<NumbersV3SO>(Vol2Paths.NumbersAsset, out report.createdNumbers);
            numbersSo.sourceFile = Path.GetFileName(Vol2Paths.NumbersCsv); numbersSo.sourceSha256 = nSha; numbersSo.rows = Vol2Snapshot.FromNumbers(result.Numbers);
            var tableSo = LoadOrCreate<Vol2TableSO>(Vol2Paths.TableAsset, out report.createdTable);
            tableSo.tableVersion = Vol2Paths.TableVersion; tableSo.sourceFile = Path.GetFileName(Vol2Paths.TableCsv); tableSo.sourceSha256 = tSha;
            tableSo.spells = Vol2Snapshot.FromSpells(result.Spells); tableSo.manifest = Vol2Snapshot.FromManifest(result.Manifest);
            EditorUtility.SetDirty(numbersSo); EditorUtility.SetDirty(tableSo);
            // SaveAssetIfDirty, not SaveAssets: another session's dirty assets must not be flushed by this command.
            AssetDatabase.SaveAssetIfDirty(numbersSo); AssetDatabase.SaveAssetIfDirty(tableSo);

            // Prove what was written: the assets must load through the frozen manifest exactly like any later consumer.
            var back = Vol2DataLoader.Load(AssetDatabase.LoadAssetAtPath<NumbersV3SO>(Vol2Paths.NumbersAsset), AssetDatabase.LoadAssetAtPath<Vol2TableSO>(Vol2Paths.TableAsset), trusted);
            if (!back.Ok) return "REFUSED vol2:import: written assets do not load back (" + back.Error + ")";
            report.status = "IMPORTED"; report.dtoHash = result.Manifest.DtoHash; report.numbersSha256 = nSha; report.tableSha256 = tSha;
            report.numbers = back.Numbers.Rows.Count; report.spells = back.Spells.Count; report.goldenPass = golden.Pass; report.goldenFail = golden.Fail;
            return JsonUtility.ToJson(report, true);
        }

        static string Status()
        {
            var s = new StatusReport { editorIdle = true };
            string numbersPath = Vol2Paths.Full(Vol2Paths.NumbersCsv), tablePath = Vol2Paths.Full(Vol2Paths.TableCsv);
            s.numbersCsv = File.Exists(numbersPath); s.tableCsv = File.Exists(tablePath);
            s.goldenFixtures = File.Exists(Vol2Paths.Golden("import_manifest_v11.json")) && File.Exists(Vol2Paths.Golden("source.json"));
            var numbersSo = AssetDatabase.LoadAssetAtPath<NumbersV3SO>(Vol2Paths.NumbersAsset); var tableSo = AssetDatabase.LoadAssetAtPath<Vol2TableSO>(Vol2Paths.TableAsset);
            s.numbersAsset = numbersSo != null; s.tableAsset = tableSo != null;
            if (s.numbersCsv) s.numbersSha256 = Vol2ImportManifestDto.Sha256(File.ReadAllBytes(numbersPath));
            if (s.tableCsv) s.tableSha256 = Vol2ImportManifestDto.Sha256(File.ReadAllBytes(tablePath));
            if (s.goldenFixtures) s.frozenDtoHash = Vol2Golden.ReadTrustedManifest(File.ReadAllText(Vol2Paths.Golden("import_manifest_v11.json"))).DtoHash;
            if (s.tableAsset && tableSo.manifest != null) s.assetDtoHash = tableSo.manifest.dtoHash;
            s.csvMatchesAssets = s.numbersAsset && s.tableAsset && s.numbersSha256 == numbersSo.sourceSha256 && s.tableSha256 == tableSo.sourceSha256;
            s.assetHashMatchesFrozen = s.assetDtoHash != null && s.assetDtoHash == s.frozenDtoHash;
            return JsonUtility.ToJson(s, true);
        }

        static T LoadOrCreate<T>(string assetPath, out bool created) where T : ScriptableObject
        {
            var so = AssetDatabase.LoadAssetAtPath<T>(assetPath); created = so == null;
            if (so != null) return so;
            so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, assetPath);
            return so;
        }
    }
}
