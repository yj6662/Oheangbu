using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #304 TextMeshPro bootstrap: Essential Resources, modern Hangul (어절) line breaking, dynamic SDF font assets for the UI fonts.
    public static partial class UiOverhaul304
    {
        const string FontFolder = "Assets/_Project/Art/UI/UI304/Fonts";
        const string FontAssetFolder = "Assets/_Project/Art/UI/UI304/FontAssets";

        static string ExecuteMore(string a)
        {
            if (a == "tmp-setup") return TmpSetup();
            if (a == "tmp-fonts") return TmpFonts();
            return SetupMore(a);   // #304 foundation: ui304-setup / ui304-report (UiOverhaul304.Setup.cs)
        }

        static string TmpSetup()
        {
            string report = "";
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                string pkg = Directory.GetDirectories(Path.GetFullPath("Library/PackageCache"), "com.unity.ugui@*").Select(d => Path.Combine(d, "Package Resources", "TMP Essential Resources.unitypackage")).FirstOrDefault(File.Exists);
                Need(pkg != null, "TMP Essential Resources.unitypackage not found in the ugui package cache.");
                AssetDatabase.ImportPackage(pkg, false); AssetDatabase.Refresh();
                report += "IMPORTED " + pkg + "; ";
            }
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            Need(settings != null, report + "TMP Settings not loadable yet (import may still be running; call tmp-setup again).");
            var so = new SerializedObject(settings);
            var hangul = so.FindProperty("m_UseModernHangulLineBreakingRules"); if (hangul != null) hangul.boolValue = true;
            var wrap = so.FindProperty("m_enableWordWrapping") ?? so.FindProperty("m_TextWrappingMode"); 
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            return report + "TMP_SETTINGS " + AssetDatabase.GetAssetPath(settings) + " modernHangul=" + (hangul != null ? hangul.boolValue.ToString() : "n/a");
        }

        static string TmpFonts()
        {
            if (!AssetDatabase.IsValidFolder(FontAssetFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/UI/UI304", "FontAssets");
            var made = new System.Collections.Generic.List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Font", new[] { FontFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                string name = Path.GetFileNameWithoutExtension(path) + " SDF"; string target = FontAssetFolder + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target) != null) { made.Add("kept " + name); continue; }
                var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                Need(fa != null, "CreateFontAsset failed for " + path);
                fa.name = name; AssetDatabase.CreateAsset(fa, target);
                fa.atlasTextures[0].name = name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
                var so = new SerializedObject(fa); var clear = so.FindProperty("m_ClearDynamicDataOnBuild"); if (clear != null) clear.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(fa); made.Add("created " + target);
            }
            AssetDatabase.SaveAssets();
            return "TMP_FONTS " + string.Join("; ", made);
        }
    }
}
