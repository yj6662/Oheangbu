using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #304 foundation setup (IMPLEMENTATION §1-§3): texture import settings, UiStyle304.asset, the two UI shader materials,
    // TMP material presets + fallback tables, the notice channel, and Style304 on the live themes / HUD skin.
    // Queue: Oheangbu.EditorTools.WorldMacro.UiOverhaul304 Execute "ui304-setup[:keep-tokens][:nanum]" | "ui304-report".
    //   keep-tokens = keep token values edited in the asset (references are always refreshed); nanum = map the Serif families
    //   to Nanum Myeongjo even when the Noto Serif KR SDF assets exist (the pre-2026-09-30 mapping).
    // Idempotent (importers are only re-imported when a setting differs). Edit Mode only. Never opens a dialog.
    public static partial class UiOverhaul304
    {
        const string Ui304Root = "Assets/_Project/Art/UI/UI304";
        const string Ui304Textures = Ui304Root + "/Textures";
        const string Ui304Shaders = Ui304Root + "/Shaders";
        const string Ui304Materials = Ui304Root + "/Materials";
        const string Ui304Presets = FontAssetFolder + "/Presets";
        const string Ui304StylePath = Ui304Root + "/UiStyle304.asset";
        const string Ui304ChannelPath = Ui304Root + "/UiNoticeChannel.asset";
        const string Ui304InkRevealMat = Ui304Materials + "/InkReveal.mat";
        const string Ui304MeterBodyMat = Ui304Materials + "/InkMeter_Body.mat";
        const string Ui304MeterRimMat = Ui304Materials + "/InkMeter_Rim.mat";
        const string Ui304ArchitectureData = "Assets/_Project/Art/World/Architecture296/Data";
        const string Ui304MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        const string Ui304ProjectRoot = "Assets/_Project";
        // the live chain first (the lobby theme survives into gameplay via DontDestroyOnLoad), then every other theme / skin asset
        static readonly string[] Ui304ThemePaths = { "Assets/_Project/Audio/Compact255/Theme.asset", "Assets/_Project/Audio/Compact255/TitleTheme.asset" };

        enum TexKind { Sprite, Texture, Alpha8 }

        sealed class TexSpec
        {
            public string Name; public TexKind Kind; public Vector4 Border; public TextureWrapMode Wrap; public bool FixedColour, Crisp, Mips;
            public TexSpec(string name, TexKind kind, Vector4 border = default, TextureWrapMode wrap = TextureWrapMode.Clamp, bool fixedColour = false, bool crisp = false, bool mips = false)
            { Name = name; Kind = kind; Border = border; Wrap = wrap; FixedColour = fixedColour; Crisp = crisp; Mips = mips; }
        }

        static Vector4 Ui304LR(float l, float r) => new Vector4(l, 0, r, 0);   // TextureImporter border order is (L, B, R, T)
        static Vector4 Ui304All(float b) => new Vector4(b, b, b, b);

        // IMPLEMENTATION §2 table. Masks = white RGB + alpha, uncompressed. Fixed-colour paper = BC7 when block aligned.
        static readonly TexSpec[] Ui304TextureTable =
        {
            new TexSpec("veil_wash", TexKind.Alpha8),
            new TexSpec("stroke_wet_s", TexKind.Sprite, Ui304LR(90, 300)),
            new TexSpec("stroke_wet_m", TexKind.Sprite, Ui304LR(140, 400)),
            new TexSpec("stroke_band", TexKind.Sprite, Ui304LR(200, 520)),
            new TexSpec("stroke_dry", TexKind.Sprite, Ui304LR(80, 700)),
            new TexSpec("stroke_line", TexKind.Sprite),
            new TexSpec("stroke_hair", TexKind.Sprite),
            new TexSpec("stroke_hair_rim", TexKind.Sprite),
            new TexSpec("stroke_swell", TexKind.Sprite),
            new TexSpec("stroke_short", TexKind.Sprite),
            new TexSpec("dab", TexKind.Sprite),
            new TexSpec("meter_body", TexKind.Texture),
            new TexSpec("meter_tail", TexKind.Texture),
            new TexSpec("meter_body_rim", TexKind.Texture),
            new TexSpec("meter_tail_rim", TexKind.Texture),
            new TexSpec("enso", TexKind.Sprite),
            new TexSpec("enso_rim", TexKind.Sprite),
            new TexSpec("stroke_spine", TexKind.Sprite),
            new TexSpec("stroke_sweep", TexKind.Sprite),
            new TexSpec("wash_tile", TexKind.Texture, default, TextureWrapMode.Repeat, mips: true),   // InkReveal noise: minified on small screens
            new TexSpec("sheet_codex", TexKind.Sprite, Ui304All(52), TextureWrapMode.Clamp, true),
            new TexSpec("sheet_map", TexKind.Sprite, default, TextureWrapMode.Clamp, true),
            new TexSpec("sheet_strip", TexKind.Sprite, Ui304All(28), TextureWrapMode.Clamp, true),
            new TexSpec("sheet_slip", TexKind.Sprite, Ui304All(18), TextureWrapMode.Clamp, true),
            new TexSpec("tile_chip", TexKind.Sprite, Ui304All(14), TextureWrapMode.Clamp, true),
            new TexSpec("seal_bu", TexKind.Sprite, default, TextureWrapMode.Clamp, true),
            new TexSpec("elem_wood", TexKind.Sprite),
            new TexSpec("elem_fire", TexKind.Sprite),
            new TexSpec("elem_earth", TexKind.Sprite),
            new TexSpec("elem_metal", TexKind.Sprite),
            new TexSpec("elem_water", TexKind.Sprite),
            new TexSpec("keycap", TexKind.Sprite, Ui304All(12), TextureWrapMode.Clamp, true, true),
            new TexSpec("blot", TexKind.Sprite),
            new TexSpec("arrow", TexKind.Sprite),
            new TexSpec("dash_box", TexKind.Sprite, Ui304All(12)),
            new TexSpec("bottle_glass", TexKind.Sprite),
            new TexSpec("bottle_liquid", TexKind.Sprite),
            new TexSpec("bottle_rim", TexKind.Sprite),
            new TexSpec("rubbing_mask", TexKind.Texture, default, TextureWrapMode.Repeat),
            new TexSpec("ribbon", TexKind.Texture),
            new TexSpec("ramp_bottom", TexKind.Sprite),
            // foundation additions (Art/UI304/make_marks304.py): drawn 18~140 px from 64 / 160 px masks -> mips
            new TexSpec("disc", TexKind.Sprite, mips: true),
            new TexSpec("ring_dashed", TexKind.Sprite, mips: true),
        };

        // #304 screen textures: each area folder under Textures/ may hold an import304.json manifest
        // {"textures":[{"file":"hud/foo.png","kind":"Sprite|Texture|Alpha8","border":[l,b,r,t],"wrap":"Clamp|Repeat","fixedColour":false,"crisp":false,"mips":false}]}
        // "file" is relative to the Textures folder. Screen engineers add manifests instead of editing the table above.
        [Serializable] sealed class Ui304ManifestEntry { public string file; public string kind = "Sprite"; public float[] border; public string wrap = "Clamp"; public bool fixedColour, crisp, mips; }
        [Serializable] sealed class Ui304Manifest { public Ui304ManifestEntry[] textures; }

        static List<TexSpec> Ui304ManifestSpecs(List<string> log)
        {
            var specs = new List<TexSpec>();
            if (!Directory.Exists(Ui304Textures)) return specs;
            foreach (var path in Directory.GetFiles(Ui304Textures, "import304.json", SearchOption.AllDirectories))
            {
                Ui304Manifest m;
                try { m = JsonUtility.FromJson<Ui304Manifest>(File.ReadAllText(path)); }
                catch (Exception e) { log?.Add("MANIFEST unreadable " + path + ": " + e.Message); continue; }
                if (m?.textures == null) continue;
                foreach (var e in m.textures)
                {
                    if (string.IsNullOrEmpty(e.file)) continue;
                    string name = e.file.Replace('\\', '/'); if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    var kind = e.kind == "Texture" ? TexKind.Texture : e.kind == "Alpha8" ? TexKind.Alpha8 : TexKind.Sprite;
                    var b = e.border != null && e.border.Length == 4 ? new Vector4(e.border[0], e.border[1], e.border[2], e.border[3]) : default;
                    specs.Add(new TexSpec(name, kind, b, e.wrap == "Repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, e.fixedColour, e.crisp, e.mips));
                }
                log?.Add("MANIFEST " + path.Replace('\\', '/') + " textures=" + m.textures.Length);
            }
            return specs;
        }

        static string Ui304ImportScreens()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "ui304-import is Edit Mode only.");
            var log = new List<string>(); var specs = Ui304ManifestSpecs(log);
            foreach (var t in specs)
            {
                string path = Ui304Textures + "/" + t.Name + ".png";
                if (File.Exists(path) && AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            int changed = 0, missing = 0;
            AssetDatabase.StartAssetEditing();
            try { foreach (var t in specs) { string r = Ui304ConfigureTexture(t, out bool c); if (c) changed++; if (r != null) { missing++; log.Add(r); } } }
            finally { AssetDatabase.StopAssetEditing(); }
            log.Add("SCREEN_TEXTURES " + (specs.Count - missing) + "/" + specs.Count + " reimported=" + changed);
            return string.Join("\n", log);
        }

        static string SetupMore(string a)
        {
            if (a == "ui304-report") return Ui304Report();
            if (a == "ui304-import") return Ui304ImportScreens();
            if (a == null || !(a == "ui304-setup" || a.StartsWith("ui304-setup:", StringComparison.Ordinal))) return null;
            var flags = new HashSet<string>(a.Split(':').Skip(1));
            foreach (var f in flags) Need(f == "keep-tokens" || f == "nanum", "ui304-setup: unknown option '" + f + "' (keep-tokens, nanum).");
            return Ui304Setup(!flags.Contains("keep-tokens"), flags.Contains("nanum"));
        }

        // ================================================================== setup
        static string Ui304Setup(bool resetTokens, bool nanumSerif = false)
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "ui304-setup is Edit Mode only.");
            Need(!EditorApplication.isCompiling, "Scripts are compiling; call ui304-setup again when done.");
            var log = new List<string>();
            foreach (var folder in new[] { Ui304Root, Ui304Textures, Ui304Shaders, Ui304Materials, FontAssetFolder, Ui304Presets }) Ui304Folder(folder);

            // 1. textures: import files copied in from design/FINAL/assets that the editor has not seen yet, then batch the re-imports
            foreach (var t in Ui304TextureTable)
            {
                string path = Ui304Textures + "/" + t.Name + ".png";
                if (File.Exists(path) && AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            foreach (var n in new[] { "InkReveal", "InkMeter" })
            {
                string path = Ui304Shaders + "/" + n + ".shader";
                if (File.Exists(path) && AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            int changed = 0, missing = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var t in Ui304TextureTable)
                {
                    string r = Ui304ConfigureTexture(t, out bool c);
                    if (c) changed++;
                    if (r != null) { missing++; log.Add(r); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            log.Add("TEXTURES " + (Ui304TextureTable.Length - missing) + "/" + Ui304TextureTable.Length + " reimported=" + changed);
            log.Add(Ui304ImportScreens());

            // 2. style asset (tokens = UiStyle304SO field initializers = DESIGN.md values)
            var style = AssetDatabase.LoadAssetAtPath<UiStyle304SO>(Ui304StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<UiStyle304SO>(); style.name = "UiStyle304";
                AssetDatabase.CreateAsset(style, Ui304StylePath); log.Add("STYLE created " + Ui304StylePath);
            }
            else if (resetTokens)
            {
                var fresh = ScriptableObject.CreateInstance<UiStyle304SO>();
                EditorUtility.CopySerialized(fresh, style); Object.DestroyImmediate(fresh); style.name = "UiStyle304";
                log.Add("STYLE tokens reset to the design values");
            }
            else log.Add("STYLE kept tokens");

            // 3. fonts + fallback tables. DESIGN §3.1: hanja only from Noto (Nanum has none). With the static Noto Serif KR
            //    instances: Serif* -> NotoSansKR-Regular, Nanum (Prose) -> NotoSerifKR-800 (hanja) + NotoSansKR-Bold.
            //    Without them (or :nanum): Serif* = Nanum Bold / ExtraBold, every Nanum -> NotoSansKR-Bold.
            var fonts = Ui304Fonts(nanumSerif, out bool notoSerif);
            log.Add("FONTMAP " + (notoSerif ? "noto-serif" : "nanum") + " " + string.Join(", ", fonts.Select(kv => kv.Key + "=" + (kv.Value != null ? kv.Value.name : "MISSING"))));
            var sansBold = Ui304Font("NotoSansKR-Bold"); var sansRegular = Ui304Font("NotoSansKR-Regular"); var serif800 = Ui304Font("NotoSerifKR-800");
            foreach (var name in new[] { "NanumMyeongjo", "NanumMyeongjoBold", "NanumMyeongjoExtraBold" })
            {
                var fa = Ui304Font(name);
                if (fa == null) { log.Add("FONT missing " + name + " SDF"); continue; }
                Ui304Fallback(fa, notoSerif ? new[] { serif800, sansBold } : new[] { sansBold }, log);
            }
            if (notoSerif)
                foreach (var name in new[] { "NotoSerifKR-600", "NotoSerifKR-800", "NotoSerifKR-900" })
                { var fa = Ui304Font(name); if (fa != null) Ui304Fallback(fa, new[] { sansRegular }, log); }

            // 4. shader materials
            var reveal = Ui304Shader("InkReveal", log); var meter = Ui304Shader("InkMeter", log);
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Ui304Textures + "/" + n + ".png");
            Material revealMat = null, bodyMat = null, rimMat = null;
            if (reveal != null)
            {
                revealMat = Ui304Material(Ui304InkRevealMat, reveal, log);
                revealMat.SetTexture("_NoiseTex", Tex("wash_tile")); revealMat.SetFloat("_Edge", .08f); revealMat.SetFloat("_NoiseAmt", .3f);
                revealMat.SetFloat("_NoiseTile", 640f); revealMat.SetFloat("_DirMode", 1f); revealMat.SetFloat("_Reveal", 1f);
                EditorUtility.SetDirty(revealMat);
            }
            if (meter != null)
            {
                bodyMat = Ui304Material(Ui304MeterBodyMat, meter, log);
                bodyMat.SetTexture("_BodyTex", Tex("meter_body")); bodyMat.SetTexture("_TailTex", Tex("meter_tail"));
                bodyMat.SetTexture("_CutBodyTex", null); bodyMat.SetTexture("_CutTailTex", null);
                rimMat = Ui304Material(Ui304MeterRimMat, meter, log);
                rimMat.SetTexture("_BodyTex", Tex("meter_body_rim")); rimMat.SetTexture("_TailTex", Tex("meter_tail_rim"));
                rimMat.SetTexture("_CutBodyTex", Tex("meter_body")); rimMat.SetTexture("_CutTailTex", Tex("meter_tail"));
                foreach (var m in new[] { bodyMat, rimMat })
                {
                    m.SetFloat("_TailPx", style.Meter.TailPx); m.SetFloat("_BlendPx", style.Meter.BlendPx);
                    m.SetFloat("_BodyAspect", style.Meter.BodyAspect); m.SetFloat("_Mode", 0f); m.SetFloat("_Fill", 1f);
                    EditorUtility.SetDirty(m);
                }
            }

            // 5. TMP presets (Paper / Ink = the font's own material; the rest are assets per font)
            var presets = new List<TmpMaterial304>();
            var rubbing = Tex("rubbing_mask");
            var serifs = new HashSet<UiFont304> { UiFont304.Serif600, UiFont304.Serif700, UiFont304.Serif800, UiFont304.Serif900, UiFont304.Prose };
            foreach (var font in fonts.Values.Distinct())
            {
                if (font == null || font.material == null) continue;
                bool serif = fonts.Any(kv => kv.Value == font && serifs.Contains(kv.Key));
                foreach (var preset in new[] { TmpPreset304.Ink_UnderPaper, TmpPreset304.Paper_UnderInk, TmpPreset304.Rubbing, TmpPreset304.Rubbing_Lite })
                {
                    if (!serif && (preset == TmpPreset304.Rubbing || preset == TmpPreset304.Rubbing_Lite)) continue;
                    var m = Ui304Preset(font, preset, style, rubbing, log);
                    if (m != null) presets.Add(new TmpMaterial304 { Font = font, Preset = preset, Material = m });
                }
            }

            // 6. fill the style's references
            Ui304FillStyle(style, fonts, presets, revealMat, bodyMat, rimMat);
            EditorUtility.SetDirty(style);

            // 7. notice channel
            var channel = AssetDatabase.LoadAssetAtPath<UiNoticeChannelSO>(Ui304ChannelPath);
            if (channel == null)
            {
                channel = ScriptableObject.CreateInstance<UiNoticeChannelSO>(); channel.name = "UiNoticeChannel";
                AssetDatabase.CreateAsset(channel, Ui304ChannelPath); log.Add("CHANNEL created " + Ui304ChannelPath);
            }
            if (style.Notices != channel) { style.Notices = channel; EditorUtility.SetDirty(style); }

            // 8. every theme + HUD skin in the project (a reference only: no existing screen reads Style304 yet), so a scene on an
            //    older theme never falls back to the asset-less UiStyle304SO.Fallback once the screens are rewritten
            foreach (var theme in Ui304Themes())
                if (theme.Style304 != style) { theme.Style304 = style; EditorUtility.SetDirty(theme); log.Add("THEME " + AssetDatabase.GetAssetPath(theme) + " <- Style304"); }
            foreach (var skin in Ui304Skins(log))
                if (skin.Style304 != style) { skin.Style304 = style; EditorUtility.SetDirty(skin); log.Add("HUDSKIN " + AssetDatabase.GetAssetPath(skin) + " <- Style304"); }

            AssetDatabase.SaveAssets();
            return "UI304_SETUP OK\n" + string.Join("\n", log) + "\n" + Ui304Report();
        }

        static void Ui304Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) Ui304Folder(parent);
            if (Directory.Exists(path))
            {
                // written to disk outside the editor (shaders / textures): import it instead of creating "<name> 1"
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
                if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Ui304ConfigureTexture(TexSpec t, out bool changed)
        {
            changed = false;
            string path = Ui304Textures + "/" + t.Name + ".png";
            if (!File.Exists(path)) return "MISSING " + path;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return "NOT_IMPORTED " + path + " (refresh the AssetDatabase and run again)";
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            string before = Ui304Signature(importer);
            var s = new TextureImporterSettings(); importer.ReadTextureSettings(s);
            switch (t.Kind)
            {
                case TexKind.Sprite:
                    s.textureType = TextureImporterType.Sprite; s.spriteMode = (int)SpriteImportMode.Single; s.spritePixelsPerUnit = 100;
                    s.spriteAlignment = (int)SpriteAlignment.Center; s.spritePivot = new Vector2(.5f, .5f); s.spriteMeshType = SpriteMeshType.FullRect;
                    s.spriteBorder = t.Border; s.spriteGenerateFallbackPhysicsShape = false;
                    s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = true;
                    break;
                case TexKind.Texture:
                    s.textureType = TextureImporterType.Default; s.textureShape = TextureImporterShape.Texture2D;
                    s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = true;
                    break;
                case TexKind.Alpha8:
                    s.textureType = TextureImporterType.SingleChannel; s.singleChannelComponent = TextureImporterSingleChannelComponent.Alpha;
                    s.textureShape = TextureImporterShape.Texture2D; s.alphaSource = TextureImporterAlphaSource.FromGrayScale; s.alphaIsTransparency = false;
                    break;
            }
            s.mipmapEnabled = t.Mips; s.wrapMode = t.Wrap; s.filterMode = FilterMode.Bilinear; s.sRGBTexture = true;
            s.npotScale = TextureImporterNPOTScale.None; s.readable = false; s.streamingMipmaps = false;
            importer.SetTextureSettings(s);
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, Mathf.NextPowerOfTwo(Mathf.Max(w, h)));
            bool compress = t.FixedColour && !t.Crisp && w % 4 == 0 && h % 4 == 0;
            importer.textureCompression = compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            if (Ui304Signature(importer) != before) { importer.SaveAndReimport(); changed = true; }
            return null;
        }

        static string Ui304Signature(TextureImporter i)
        {
            var s = new TextureImporterSettings(); i.ReadTextureSettings(s);
            return string.Join("|", s.textureType, s.spriteMode, s.spriteBorder, s.spritePixelsPerUnit, s.spriteMeshType, s.spriteAlignment, s.mipmapEnabled,
                s.wrapMode, s.filterMode, s.alphaSource, s.alphaIsTransparency, s.singleChannelComponent, s.npotScale, s.sRGBTexture, s.readable,
                i.maxTextureSize, i.textureCompression);
        }

        static Shader Ui304Shader(string name, List<string> log)
        {
            string path = Ui304Shaders + "/" + name + ".shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null && File.Exists(path)) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); shader = AssetDatabase.LoadAssetAtPath<Shader>(path); }
            if (shader == null) { log.Add("SHADER missing " + path); return null; }
            if (ShaderUtil.ShaderHasError(shader)) log.Add("SHADER ERROR " + path + " (see Console; materials are still created)");
            return shader;
        }

        static Material Ui304Material(string path, Shader shader, List<string> log)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path); log.Add("MATERIAL created " + path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        static Material Ui304Preset(TMP_FontAsset font, TmpPreset304 preset, UiStyle304SO style, Texture2D rubbing, List<string> log)
        {
            string path = Ui304Presets + "/" + font.name + " " + preset + ".mat";
            var source = font.material;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = m == null;
            if (created) m = new Material(source);
            else { m.shader = source.shader; m.CopyPropertiesFromMaterial(source); }
            m.name = Path.GetFileNameWithoutExtension(path);
            foreach (var k in new[] { "UNDERLAY_ON", "UNDERLAY_INNER", "OUTLINE_ON", "GLOW_ON", "BEVEL_ON" }) m.DisableKeyword(k);
            bool rub = preset == TmpPreset304.Rubbing || preset == TmpPreset304.Rubbing_Lite;
            if (rub)
            {
                var full = Shader.Find("TextMeshPro/Distance Field");
                if (full != null) m.shader = full; else log.Add("PRESET " + m.name + ": TextMeshPro/Distance Field not found, face texture unsupported");
                m.SetColor("_FaceColor", Color.white);
                if (m.HasProperty("_FaceTex"))
                {
                    m.SetTexture("_FaceTex", rubbing);
                    // IMPLEMENTATION §3.3: tiling = glyph width / 360 (240 px glyph) and / 900 (150 px lockup)
                    float tile = preset == TmpPreset304.Rubbing ? 240f / 360f : 150f / 900f;
                    m.SetTextureScale("_FaceTex", new Vector2(tile, tile));
                }
            }
            else
            {
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", preset == TmpPreset304.Ink_UnderPaper ? style.Sheet : style.Ink);
                m.SetFloat("_UnderlayOffsetX", 0f); m.SetFloat("_UnderlayOffsetY", 0f);
                m.SetFloat("_UnderlayDilate", .45f); m.SetFloat("_UnderlaySoftness", .6f);
            }
            ShaderUtilities.UpdateShaderRatios(m);
            if (created) { AssetDatabase.CreateAsset(m, path); log.Add("PRESET created " + path); }
            else EditorUtility.SetDirty(m);
            return m;
        }

        static TMP_FontAsset Ui304Font(string name) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetFolder + "/" + name + " SDF.asset");

        // Serif600 / Serif700 -> NotoSerifKR-600 (no static 700; 600 keeps the 600 -> 800 focus step visible), 800 -> 800, 900 -> 900.
        static Dictionary<UiFont304, TMP_FontAsset> Ui304Fonts(bool nanumSerif, out bool notoSerif)
        {
            var s600 = Ui304Font("NotoSerifKR-600"); var s800 = Ui304Font("NotoSerifKR-800"); var s900 = Ui304Font("NotoSerifKR-900");
            notoSerif = !nanumSerif && s600 != null && s800 != null && s900 != null;
            var map = new Dictionary<UiFont304, TMP_FontAsset>
            {
                { UiFont304.Prose, Ui304Font("NanumMyeongjo") }, { UiFont304.Sans400, Ui304Font("NotoSansKR-Regular") }, { UiFont304.Sans700, Ui304Font("NotoSansKR-Bold") },
            };
            if (notoSerif) { map[UiFont304.Serif600] = s600; map[UiFont304.Serif700] = s600; map[UiFont304.Serif800] = s800; map[UiFont304.Serif900] = s900; }
            else
            {
                var bold = Ui304Font("NanumMyeongjoBold"); var extra = Ui304Font("NanumMyeongjoExtraBold");
                map[UiFont304.Serif600] = bold; map[UiFont304.Serif700] = bold; map[UiFont304.Serif800] = extra; map[UiFont304.Serif900] = extra;
            }
            return map;
        }

        static void Ui304Fallback(TMP_FontAsset font, TMP_FontAsset[] chain, List<string> log)
        {
            var want = chain.Where(f => f != null && f != font).Distinct().ToList();
            if (want.Count == 0) return;
            var table = font.fallbackFontAssetTable;
            if (table != null && table.Count == want.Count && table.SequenceEqual(want)) return;
            font.fallbackFontAssetTable = want; EditorUtility.SetDirty(font);
            log.Add("FALLBACK " + font.name + " -> " + string.Join(", ", want.Select(f => f.name)));
        }

        static void Ui304FillStyle(UiStyle304SO style, Dictionary<UiFont304, TMP_FontAsset> fonts, List<TmpMaterial304> presets,
            Material reveal, Material body, Material rim)
        {
            string P(string n) => Ui304Textures + "/" + n + ".png";
            Sprite Sp(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(P(n));
            Texture2D Tx(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(P(n));

            // fonts (append families missing from a kept asset)
            foreach (UiFont304 f in Enum.GetValues(typeof(UiFont304)))
                if (!style.Fonts.Any(x => x != null && x.Family == f)) style.Fonts.Add(new FontFamily304(f));
            foreach (var entry in style.Fonts) if (entry != null && fonts.TryGetValue(entry.Family, out var fa)) entry.Font = fa;

            // roles (append ids missing from a kept asset), resolve font + preset material
            foreach (var d in UiStyle304SO.DefaultRoles()) if (!style.Type.Any(r => r != null && r.Id == d.Id)) style.Type.Add(d);
            foreach (var role in style.Type)
            {
                if (role == null) continue;
                role.Font = fonts.TryGetValue(role.Family, out var fa) ? fa : null;
                role.Material = role.IsPlainPreset || role.Font == null ? null
                    : presets.FirstOrDefault(p => p.Font == role.Font && p.Preset == role.Preset)?.Material;
            }

            var b = style.Sprites;
            b.VeilWash = Tx("veil_wash");
            b.WetS = Sp("stroke_wet_s"); b.WetM = Sp("stroke_wet_m"); b.Band = Sp("stroke_band"); b.Dry = Sp("stroke_dry");
            b.Line = Sp("stroke_line"); b.Hair = Sp("stroke_hair"); b.HairRim = Sp("stroke_hair_rim"); b.Swell = Sp("stroke_swell");
            b.Short = Sp("stroke_short"); b.Dab = Sp("dab"); b.Spine = Sp("stroke_spine"); b.Sweep = Sp("stroke_sweep");
            b.Enso = Sp("enso"); b.EnsoRim = Sp("enso_rim"); b.Arrow = Sp("arrow"); b.Blot = Sp("blot"); b.DashBox = Sp("dash_box"); b.RampBottom = Sp("ramp_bottom");
            b.Disc = Sp("disc"); b.RingDashed = Sp("ring_dashed");
            b.SheetCodex = Sp("sheet_codex"); b.SheetMap = Sp("sheet_map"); b.SheetStrip = Sp("sheet_strip"); b.SheetSlip = Sp("sheet_slip");
            b.TileChip = Sp("tile_chip"); b.SealBu = Sp("seal_bu"); b.Keycap = Sp("keycap");
            b.ElemWood = Sp("elem_wood"); b.ElemFire = Sp("elem_fire"); b.ElemEarth = Sp("elem_earth"); b.ElemMetal = Sp("elem_metal"); b.ElemWater = Sp("elem_water");
            b.BottleGlass = Sp("bottle_glass"); b.BottleLiquid = Sp("bottle_liquid"); b.BottleRim = Sp("bottle_rim");
            b.MeterBody = Tx("meter_body"); b.MeterTail = Tx("meter_tail"); b.MeterBodyRim = Tx("meter_body_rim"); b.MeterTailRim = Tx("meter_tail_rim");
            b.WashTile = Tx("wash_tile"); b.RubbingMask = Tx("rubbing_mask"); b.Ribbon = Tx("ribbon");

            // stroke classes: sprite + native size from the imported texture
            foreach (var d in UiStyle304SO.DefaultStrokes()) if (!style.Strokes.Any(s => s != null && s.Class == d.Class)) style.Strokes.Add(d);
            var byClass = new Dictionary<StrokeClass304, string>
            {
                { StrokeClass304.WetS, "stroke_wet_s" }, { StrokeClass304.WetM, "stroke_wet_m" }, { StrokeClass304.Band, "stroke_band" },
                { StrokeClass304.Dry, "stroke_dry" }, { StrokeClass304.Line, "stroke_line" }, { StrokeClass304.Hair, "stroke_hair" },
                { StrokeClass304.HairRim, "stroke_hair_rim" }, { StrokeClass304.Swell, "stroke_swell" }, { StrokeClass304.Short, "stroke_short" },
                { StrokeClass304.Spine, "stroke_spine" }, { StrokeClass304.Sweep, "stroke_sweep" },
            };
            foreach (var s in style.Strokes)
            {
                if (s == null || !byClass.TryGetValue(s.Class, out var n)) continue;
                s.Sprite = Sp(n);
                var tex = Tx(n); if (tex != null) { s.NativeW = tex.width; s.NativeH = tex.height; }
                if (s.Sprite != null && s.Sliced) { s.BorderL = s.Sprite.border.x; s.BorderR = s.Sprite.border.z; }
            }

            style.Materials.InkReveal = reveal; style.Materials.InkMeterBody = body; style.Materials.InkMeterRim = rim;
            style.Materials.Tmp = presets;
        }

        // Protected worlds (protect_architecture296.py hashes every file in these trees): never write to them.
        static readonly string[] Ui304Protected = { "Assets/_Project/Art/World/Watershed295/", "Assets/_Project/Art/World/Reworld292/", "Assets/_Project/Art/World/MountainTrail285/" };
        static bool Ui304IsProtected(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return Ui304Protected.Any(p => path.StartsWith(p, StringComparison.Ordinal));
        }

        // every PlaytestUiThemeSO: the live Compact255 pair first, then Architecture296, then the rest of the project
        static IEnumerable<PlaytestUiThemeSO> Ui304Themes()
        {
            var seen = new HashSet<PlaytestUiThemeSO>();
            foreach (var p in Ui304ThemePaths) { var t = AssetDatabase.LoadAssetAtPath<PlaytestUiThemeSO>(p); if (t != null && seen.Add(t)) yield return t; }
            var folders = new List<string>();
            if (AssetDatabase.IsValidFolder(Ui304ArchitectureData)) folders.Add(Ui304ArchitectureData);
            folders.Add(Ui304ProjectRoot);
            foreach (var folder in folders)
                foreach (var guid in AssetDatabase.FindAssets("t:PlaytestUiThemeSO", new[] { folder }))
                { var t = AssetDatabase.LoadAssetAtPath<PlaytestUiThemeSO>(AssetDatabase.GUIDToAssetPath(guid)); if (t != null && !Ui304IsProtected(t) && seen.Add(t)) yield return t; }
        }

        // every WorldMacroHudSkinProfileSO; the one W_Demo_Main's HudController references (scene read as text) comes first
        static List<WorldMacroHudSkinProfileSO> Ui304Skins(List<string> log)
        {
            var guids = AssetDatabase.FindAssets("t:WorldMacroHudSkinProfileSO", new[] { Ui304ProjectRoot });
            string scene = File.Exists(Ui304MainScene) ? File.ReadAllText(Ui304MainScene) : "";
            int Rank(string g) => scene.Contains(g) ? 0 : AssetDatabase.GUIDToAssetPath(g).StartsWith(Ui304ArchitectureData, StringComparison.Ordinal) ? 1 : 2;
            if (log != null && guids.Length > 0 && !guids.Any(g => scene.Contains(g))) log.Add("HUDSKIN none referenced by " + Ui304MainScene);
            var result = new List<WorldMacroHudSkinProfileSO>();
            foreach (var g in guids.OrderBy(Rank)) { var s = AssetDatabase.LoadAssetAtPath<WorldMacroHudSkinProfileSO>(AssetDatabase.GUIDToAssetPath(g)); if (s != null && !Ui304IsProtected(s)) result.Add(s); }
            return result;
        }

        // ================================================================== report
        static string Ui304Report()
        {
            var sb = new StringBuilder("UI304_REPORT\n");
            var style = AssetDatabase.LoadAssetAtPath<UiStyle304SO>(Ui304StylePath);
            if (style == null) { sb.Append("STYLE missing ").Append(Ui304StylePath).Append(" (run ui304-setup)\n"); }
            else
            {
                sb.Append("STYLE ").Append(Ui304StylePath).Append(" roles=").Append(style.Type.Count)
                  .Append(" rolesWithoutFont=").Append(style.Type.Count(r => r != null && r.Font == null))
                  .Append(" presetRolesWithoutMaterial=").Append(style.Type.Count(r => r != null && !r.IsPlainPreset && r.Material == null))
                  .Append(" veilLifts=").Append(style.VeilLiftX.Count).Append(" strokeRules=").Append(style.StrokeRules.Count)
                  .Append(" optionHelp=").Append(style.OptionHelp.Count).Append('\n');
                sb.Append("TOKENS Ink=#").Append(ColorUtility.ToHtmlStringRGB(style.Ink)).Append(" Paper=#").Append(ColorUtility.ToHtmlStringRGB(style.Paper))
                  .Append(" Cinnabar=#").Append(ColorUtility.ToHtmlStringRGB(style.Cinnabar)).Append(" StrokeMs=").Append(style.Motion.StrokeMs)
                  .Append(" Meter HP=").Append(style.Meter.HpSize).Append(" Toast=").Append(style.Toast.Origin).Append('\n');
                var missingSprites = typeof(UiSprites304).GetFields().Where(f => typeof(Object).IsAssignableFrom(f.FieldType) && (f.GetValue(style.Sprites) as Object) == null).Select(f => f.Name).ToArray();
                sb.Append("SPRITES missing=").Append(missingSprites.Length == 0 ? "none" : string.Join(",", missingSprites)).Append('\n');
                var strokes = style.Strokes.Where(s => s != null).Select(s => s.Class + (s.Sprite == null ? "(no sprite)" : "") + ":" + s.NativeW + "x" + s.NativeH + (s.Sliced ? " L" + s.BorderL + " R" + s.BorderR : ""));
                sb.Append("STROKES ").Append(string.Join("; ", strokes)).Append('\n');
                var mats = style.Materials;
                sb.Append("MATERIALS InkReveal=").Append(Ui304MatState(mats.InkReveal)).Append(" InkMeterBody=").Append(Ui304MatState(mats.InkMeterBody))
                  .Append(" InkMeterRim=").Append(Ui304MatState(mats.InkMeterRim)).Append(" tmpPresets=").Append(mats.Tmp.Count(p => p != null && p.Material != null)).Append('\n');
                sb.Append("FONTS ").Append(string.Join("; ", style.Fonts.Where(f => f != null).Select(f => f.Family + "=" + (f.Font != null ? f.Font.name + " fb[" + string.Join(",", (f.Font.fallbackFontAssetTable ?? new List<TMP_FontAsset>()).Where(x => x != null).Select(x => x.name)) + "]" : "none")))).Append('\n');
            }
            int ok = 0; var bad = new List<string>();
            foreach (var t in Ui304TextureTable)
            {
                string path = Ui304Textures + "/" + t.Name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { bad.Add(t.Name + "(not imported)"); continue; }
                var want = t.Kind == TexKind.Sprite ? TextureImporterType.Sprite : t.Kind == TexKind.Alpha8 ? TextureImporterType.SingleChannel : TextureImporterType.Default;
                bool good = importer.textureType == want && importer.mipmapEnabled == t.Mips && importer.wrapMode == t.Wrap && importer.npotScale == TextureImporterNPOTScale.None
                    && (t.Kind != TexKind.Sprite || importer.spriteBorder == t.Border);
                if (t.Kind == TexKind.Alpha8) { var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path); good &= tex != null && tex.format == TextureFormat.Alpha8; }
                if (good) ok++; else bad.Add(t.Name);
            }
            sb.Append("IMPORT ok=").Append(ok).Append('/').Append(Ui304TextureTable.Length).Append(bad.Count > 0 ? " differs=" + string.Join(",", bad) : "").Append('\n');
            sb.Append("CHANNEL ").Append(AssetDatabase.LoadAssetAtPath<UiNoticeChannelSO>(Ui304ChannelPath) != null ? Ui304ChannelPath : "missing")
              .Append(" style.Notices=").Append(style != null && style.Notices != null ? "set" : "none").Append('\n');
            var unassigned = new List<string>();
            foreach (var theme in Ui304Themes())
            {
                sb.Append("THEME ").Append(AssetDatabase.GetAssetPath(theme)).Append(" Style304=").Append(theme.Style304 != null ? (theme.Style304 == style ? "UiStyle304" : theme.Style304.name + " (other)") : "none").Append('\n');
                if (theme.Style304 == null) unassigned.Add(AssetDatabase.GetAssetPath(theme));
            }
            foreach (var skin in Ui304Skins(null))
            {
                sb.Append("HUDSKIN ").Append(AssetDatabase.GetAssetPath(skin)).Append(" Style304=").Append(skin.Style304 != null ? (skin.Style304 == style ? "UiStyle304" : skin.Style304.name + " (other)") : "none").Append('\n');
                if (skin.Style304 == null) unassigned.Add(AssetDatabase.GetAssetPath(skin));
            }
            sb.Append("UNASSIGNED ").Append(unassigned.Count == 0 ? "none" : unassigned.Count + ": " + string.Join(", ", unassigned)).Append('\n');
            return sb.ToString().TrimEnd();
        }

        static string Ui304MatState(Material m)
        {
            if (m == null) return "none";
            return m.shader == null ? "no-shader" : m.shader.name + (ShaderUtil.ShaderHasError(m.shader) ? "(ERROR)" : "");
        }
    }
}
