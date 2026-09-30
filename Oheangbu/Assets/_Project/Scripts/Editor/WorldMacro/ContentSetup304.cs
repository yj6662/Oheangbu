using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#304 content area setup. Queue: Oheangbu.EditorTools.WorldMacro.ContentSetup304 Execute
    /// "content304-setup" | "content304-report". Edit Mode only, idempotent, never opens a dialog.
    ///   1. imports Textures/content/*.png with the foundation manifest route (UiOverhaul304 "ui304-import" reads
    ///      Textures/content/import304.json: Sprite, gwanggwak Sliced 24, mips on the small marks);
    ///   2. creates / refreshes Assets/_Project/Resources/UI304/content/ContentArt304.asset (the one Resources asset the content
    ///      screens load: ContentArt304.Load) and binds the five sprites;
    ///   3. imports Docs/오행부_작도어휘_v0_1.csv (the only source of 글자 효과) into ContentArt304.Vocab (DTO rows + SHA-1 of the
    ///      source). Run it again whenever the CSV changes; the codex never reads the CSV at runtime.</summary>
    public static class ContentSetup304
    {
        const string TextureFolder = "Assets/_Project/Art/UI/UI304/Textures/content";
        const string ResourcesRoot = "Assets/_Project/Resources";
        const string AssetFolder = ResourcesRoot + "/UI304/content";
        const string AssetPath = AssetFolder + "/ContentArt304.asset";
        const string CsvFromRepo = "Docs/오행부_작도어휘_v0_1.csv";

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "content304-setup") return Setup();
            if (a == "content304-report") return Report();
            throw new ArgumentException("Expected content304-setup or content304-report");
        }

        static string Setup()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "content304-setup is Edit Mode only.");
            Need(!EditorApplication.isCompiling, "Scripts are compiling; call content304-setup again when done.");
            var log = new List<string>();
            log.Add(UiOverhaul304.Execute("ui304-import"));

            Folder(ResourcesRoot); Folder(ResourcesRoot + "/UI304"); Folder(AssetFolder);
            var art = AssetDatabase.LoadAssetAtPath<ContentArt304>(AssetPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<ContentArt304>(); art.name = "ContentArt304";
                AssetDatabase.CreateAsset(art, AssetPath); log.Add("ASSET created " + AssetPath);
            }
            art.Coin = SpriteAt("coin"); art.MukDeung = SpriteAt("mukdeung"); art.GwangGwak = SpriteAt("gwanggwak");
            art.Flick = SpriteAt("flick"); art.SealFrame = SpriteAt("seal_frame");
            log.Add("SPRITES coin=" + Has(art.Coin) + " mukdeung=" + Has(art.MukDeung) + " gwanggwak=" + Has(art.GwangGwak)
                + " flick=" + Has(art.Flick) + " seal_frame=" + Has(art.SealFrame));

            string csv = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", CsvFromRepo));
            Need(File.Exists(csv), "Missing " + csv);
            byte[] bytes = File.ReadAllBytes(csv);
            var rows = ContentVocab304.Parse(Encoding.UTF8.GetString(bytes));
            Need(rows.Count > 0, "No rows parsed from " + csv);
            art.Vocab = rows; art.VocabSource = CsvFromRepo;
            using (var sha = SHA1.Create()) art.VocabHash = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
            EditorUtility.SetDirty(art); AssetDatabase.SaveAssets();
            log.Add("VOCAB rows=" + rows.Count + " finals=" + rows.Count(r => r.HasFinal) + " blank=" + rows.Count(r => r.Blank) + " sha1=" + art.VocabHash);
            log.Add(Report());
            return string.Join("\n", log);
        }

        static string Report()
        {
            var art = AssetDatabase.LoadAssetAtPath<ContentArt304>(AssetPath);
            if (art == null) return "CONTENT304 missing " + AssetPath + " (run content304-setup)";
            var lines = new List<string>
            {
                "CONTENT304 asset=" + AssetPath,
                "SPRITES coin=" + Has(art.Coin) + " mukdeung=" + Has(art.MukDeung) + " gwanggwak=" + Has(art.GwangGwak) + " flick=" + Has(art.Flick) + " seal_frame=" + Has(art.SealFrame),
                "VOCAB rows=" + (art.Vocab != null ? art.Vocab.Count : 0) + " source=" + art.VocabSource + " sha1=" + art.VocabHash,
            };
            string csv = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", CsvFromRepo));
            if (File.Exists(csv))
                using (var sha = SHA1.Create())
                {
                    string now = string.Concat(sha.ComputeHash(File.ReadAllBytes(csv)).Select(b => b.ToString("x2")));
                    lines.Add(now == art.VocabHash ? "VOCAB up to date" : "VOCAB STALE (CSV changed: run content304-setup)");
                }
            // the learnable catalog must find its CSV rows (row header 분류 / 프레임, 효과)
            var missing = WorldMacroCollectionCatalog.AllSpells.Where(s => art.Row(s.Letter) == null).Select(s => s.Letter).ToArray();
            lines.Add("CATALOG rows missing=" + (missing.Length == 0 ? "none" : string.Join(" ", missing)));
            return string.Join("\n", lines);
        }

        static Sprite SpriteAt(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/" + name + ".png");
        static string Has(UnityEngine.Object o) => o != null ? "ok" : "MISSING";

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
