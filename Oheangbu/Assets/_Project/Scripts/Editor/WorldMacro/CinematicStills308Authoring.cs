using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.Core.Events;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 그림 시네마틱 — 저작 도구 [SPEC-CINEMATIC-STILLS-308 7.1 · 8, TE-3 · TE-6]. SO 와 그림은 git 이 추적하지 않으므로,
    // 표의 원천(CinematicStillsCatalogSO.ApplyDefaults308, 추적되는 코드)에서 자산을 만든다. 자리 그림은 여기서 코드로 그린다
    // (검토 S5: 추적되는 도구가 비추적 Art/ 입력에 기대지 않는다). 대화상자 없음 · 씬 손대지 않음 · SaveAssets 를 부르지 않는다
    // (다른 세션의 dirty 자산을 같이 저장하지 않도록 자산마다 SaveAssetIfDirty).
    //   큐: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.CinematicStills308Authoring Run "status"
    //        ... Run "apply"    카탈로그 + 채널 3 + 없는 그림의 자리 그림 + 가져오기 설정 (표를 코드 값으로 다시 쓴다)
    //        ... Run "import"   가져오기 설정과 IsPlaceholder 만 다시 맞춘다 (생성 그림을 같은 파일 이름으로 덮어쓴 뒤)
    public static class CinematicStills308Authoring
    {
        public const string Folder = "Assets/_Project/Resources/Cinematic308";
        public const string CatalogPath = Folder + "/CinematicStills308.asset";
        static readonly string[] ChannelNames = { "CinematicRequested", "CinematicStarted", "CinematicFinished" };
        // 자리 그림의 두 값(종이 · 먹). 순흑 · 순백을 쓰지 않는다(ART.md 2절).
        static readonly Color32 PlaceholderPaper = new Color32(226, 217, 206, 255), PlaceholderInk = new Color32(42, 38, 34, 255);

        public static string StillAssetPath(CinematicStillsCatalogSO.Still still) => "Assets/_Project/Resources/" + still.ResourcePath + ".png";

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c != "status" && c != "apply" && c != "import") return "REFUSED CinematicStills308Authoring: status | apply | import";
            var log = new StringBuilder();
            var catalog = AssetDatabase.LoadAssetAtPath<CinematicStillsCatalogSO>(CatalogPath);
            if (c == "status") { Status(catalog, log); return "cinematic authoring status\n" + log; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling) return "refused: the editor is compiling";
            if (c == "import" && catalog == null) return "refused: no catalogue at " + CatalogPath + " (run apply first)";
            try
            {
                if (c == "apply")
                {
                    EnsureFolder(Folder);
                    if (catalog == null) { catalog = ScriptableObject.CreateInstance<CinematicStillsCatalogSO>(); AssetDatabase.CreateAsset(catalog, CatalogPath); log.AppendLine("created " + CatalogPath); }
                    catalog.Requested = Channel(ChannelNames[0], log); catalog.Started = Channel(ChannelNames[1], log); catalog.Finished = Channel(ChannelNames[2], log);
                    catalog.ApplyDefaults308();
                    log.AppendLine("table written from code: " + catalog.Sequences.Length + " sequence(s)");
                }
                int placeholders = 0, pictures = 0;
                foreach (var sequence in catalog.Sequences)
                    for (int i = 0; i < sequence.Stills.Length; i++)
                    {
                        var still = sequence.Stills[i]; string path = StillAssetPath(still);
                        byte[] drawn = PlaceholderPng(catalog.Style.SourceWidth, catalog.Style.SourceHeight, i);
                        if (!File.Exists(path))
                        {
                            if (c != "apply") { log.AppendLine("MISSING " + path); continue; }
                            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                            File.WriteAllBytes(path, drawn); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                            log.AppendLine("placeholder drawn " + path);
                        }
                        ApplyImporter(path, catalog.Rules.MaxTextureSize, log);
                        still.IsPlaceholder = Sha(File.ReadAllBytes(path)) == Sha(drawn);
                        if (still.IsPlaceholder) placeholders++; else pictures++;
                    }
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
                log.AppendLine("stills: " + pictures + " picture(s), " + placeholders + " placeholder(s)");
                return "cinematic authoring " + c + " ok\n" + log;
            }
            catch (Exception e) { return "cinematic authoring " + c + " FAILED: " + e.GetType().Name + " " + e.Message + "\n" + log; }
        }

        static void Status(CinematicStillsCatalogSO catalog, StringBuilder log)
        {
            log.AppendLine("catalogue " + (catalog != null ? "present" : "ABSENT") + " " + CatalogPath);
            foreach (string name in ChannelNames) log.AppendLine("channel " + (File.Exists(Folder + "/" + name + ".asset") ? "present" : "ABSENT") + " " + name);
            if (catalog == null) return;
            foreach (var sequence in catalog.Sequences)
            {
                log.AppendLine("sequence " + sequence.Id + (sequence.Disabled ? " (disabled)" : "") + " " + sequence.TotalSeconds.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + " s, " + sequence.Stills.Length + " still(s)");
                foreach (var still in sequence.Stills)
                    log.AppendLine("  " + (File.Exists(StillAssetPath(still)) ? still.IsPlaceholder ? "placeholder" : "picture    " : "MISSING    ") + " " + StillAssetPath(still));
            }
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static StringEventChannelSO Channel(string name, StringBuilder log)
        {
            string path = Folder + "/" + name + ".asset";
            var channel = AssetDatabase.LoadAssetAtPath<StringEventChannelSO>(path);
            if (channel != null) return channel;
            channel = ScriptableObject.CreateInstance<StringEventChannelSO>(); AssetDatabase.CreateAsset(channel, path); log.AppendLine("created " + path);
            return channel;
        }

        // 8절: Default · sRGB · 밉맵 끔 · NPOT 그대로 · Bilinear · Clamp · 최대 2048 · 압축 High Quality · 읽기 끔 (로딩 · 로비 그림과 같다)
        static void ApplyImporter(string path, int maxSize, StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter; if (importer == null) { log.AppendLine("NO IMPORTER " + path); return; }
            bool same = importer.textureType == TextureImporterType.Default && importer.sRGBTexture && !importer.mipmapEnabled && importer.npotScale == TextureImporterNPOTScale.None &&
                importer.filterMode == FilterMode.Bilinear && importer.wrapMode == TextureWrapMode.Clamp && importer.maxTextureSize == maxSize &&
                importer.textureCompression == TextureImporterCompression.CompressedHQ && !importer.isReadable && !importer.alphaIsTransparency;
            if (same) return;
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.isReadable = false; importer.alphaIsTransparency = false;
            importer.SaveAndReimport(); log.AppendLine("importer set " + path);
        }

        public static bool ImporterMatches(string path, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer != null && importer.textureType == TextureImporterType.Default && importer.sRGBTexture && !importer.mipmapEnabled && importer.npotScale == TextureImporterNPOTScale.None &&
                importer.filterMode == FilterMode.Bilinear && importer.wrapMode == TextureWrapMode.Clamp && importer.maxTextureSize == maxSize &&
                importer.textureCompression == TextureImporterCompression.CompressedHQ && !importer.isReadable;
        }

        static string Sha(byte[] bytes) { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))); }

        /// <summary>Three code-drawn stand-ins (ink masses only, mirror-symmetric, no text): 0 = receding timber sets with a dark
        /// centre, 1 = one upright brush mass over a low dark band, 2 = a dark frame around blank paper. Deterministic.</summary>
        public static byte[] PlaceholderPng(int width, int height, int kind)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = Mathf.Abs((x + .5f) / width - .5f) * 2f, v = (y + .5f) / height;   // u: 0 centre .. 1 edge (mirror), v: 0 bottom .. 1 top
                    float w = Mathf.Abs(v - .5f) * 2f, d = Mathf.Max(u, w), ink;
                    switch (((kind % 3) + 3) % 3)
                    {
                        case 0:
                            ink = Mathf.SmoothStep(.85f, .10f, d) * .85f;
                            foreach (float ring in Rings) if (Mathf.Abs(d - ring) < .018f) ink = Mathf.Max(ink, .92f);
                            if (v < .5f && u < (.5f - v) * .9f) ink *= .45f;   // the pale wedge of light on the floor, from the viewer's side
                            break;
                        case 1:
                            float bx = u / .06f, by = (v - .52f) / .30f;
                            ink = Mathf.Clamp01(1.15f - (bx * bx + by * by)) * .95f;
                            ink = Mathf.Max(ink, Mathf.SmoothStep(.22f, .02f, v) * .8f);
                            ink = Mathf.Max(ink, .12f + .10f * d);
                            break;
                        default:
                            ink = Mathf.SmoothStep(.56f, .70f, d) * .90f + Mathf.SmoothStep(.86f, 1f, d) * .08f;
                            break;
                    }
                    pixels[y * width + x] = Color32.Lerp(PlaceholderPaper, PlaceholderInk, Mathf.Clamp01(ink));
                }
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            try { texture.SetPixels32(pixels); texture.Apply(false, false); return texture.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static readonly float[] Rings = { .88f, .62f, .42f };
    }
}
