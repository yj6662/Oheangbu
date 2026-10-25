using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 9a (D308-150 V5): writes the vol.2 VFX assets from the code defaults (Vol2VfxDefaults) - 24 templates, 5 body looks, the shared ink material and the catalogue under
    /// Resources, so the runtime loads them by path. Queue (editor idle, no Play, no compile):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxBuild Run "all"      create what is missing (existing assets are kept: they are the data now)
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxBuild Run "reset"    overwrite every asset with the code defaults
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxBuild Run "status"   what is there
    /// Refuses (as text, never a dialog) when the editor is busy or the shader has not been imported yet. Saves only the assets it touched (SaveAssetIfDirty): another session's dirty assets are not flushed.</summary>
    public static class Vol2VfxBuild
    {
        public const string Root = "Assets/_Project/Art/SpellVFX308/Vol2Vfx";
        public const string ResourcesFolder = Root + "/Resources/Vol2Vfx";
        public const string TemplateFolder = Root + "/Templates", BodyFolder = Root + "/Bodies";
        public const string MaterialPath = Root + "/M_Vol2Ink.mat";
        public const string BristleTexture = "Assets/_Project/Art/SpellVFX308/RiposteStroke/RipStroke308_Bristle.png";
        public const string CrackTexture = "Assets/_Project/Art/SpellVFX308/RiposteStroke/RipStroke308_Crack.png";
        public const string CataloguePath = ResourcesFolder + "/Vol2VfxCatalogue.asset";
        public const string SheetTexture = Root + "/NB20/NB20_Sheet.png", SheetTimeTexture = Root + "/NB20/NB20_SheetTime.png";

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("vfx9a:build:", StringComparison.Ordinal)) c = c.Substring(12);
            if (c != "all" && c != "reset" && c != "status") return "REFUSED vol2vfx-build: all | reset | status";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2vfx-build: editor busy (play / compile / import)";
            try { return c == "status" ? Status() : Build(c == "reset"); }
            catch (Exception e) { return "REFUSED vol2vfx-build: " + e.GetType().Name + " " + e.Message; }
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')), name = path.Substring(path.LastIndexOf('/') + 1);
            Folder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static T Make<T>(string path, T defaults, bool reset, List<string> log) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(defaults, path); log.Add("created " + path);
                return defaults;
            }
            if (reset) { EditorUtility.CopySerialized(defaults, existing); EditorUtility.SetDirty(existing); log.Add("reset " + path); }
            UnityEngine.Object.DestroyImmediate(defaults);
            return existing;
        }

        static string Build(bool reset)
        {
            var shader = Shader.Find(Vol2VfxCatalogue.ShaderName);
            if (shader == null) return "REFUSED vol2vfx-build: shader " + Vol2VfxCatalogue.ShaderName + " is not imported yet (refresh first)";
            var log = new List<string>();
            Folder(TemplateFolder); Folder(BodyFolder); Folder(ResourcesFolder);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) { mat = new Material(shader) { name = "M_Vol2Ink" }; AssetDatabase.CreateAsset(mat, MaterialPath); log.Add("created " + MaterialPath); }
            mat.shader = shader;
            mat.renderQueue = 2450;
            mat.SetFloat("_MinPx", 56f);
            mat.DisableKeyword("_EMISSION");
            // the riposte one-stroke dry-brush bristle mask (R = ink coverage, tiles along the stroke): the stroke texture of every vol.2 stroke
            var bristle = AssetDatabase.LoadAssetAtPath<Texture2D>(BristleTexture);
            if (bristle != null) mat.SetTexture("_Bristle", bristle); else log.Add("WARNING: no bristle texture at " + BristleTexture);
            // stroke20 (D308-150 V7): the 앞잡's crack field drives the brush strokes' drying (the same texture asset the riposte material uses; its importer is not touched)
            var crack = AssetDatabase.LoadAssetAtPath<Texture2D>(CrackTexture);
            if (crack != null && mat.HasProperty("_Crack")) { if (mat.GetTexture("_Crack") != crack) { mat.SetTexture("_Crack", crack); log.Add("material _Crack = " + CrackTexture); } }
            else log.Add("WARNING: no crack texture at " + CrackTexture + " or no _Crack on the shader (refresh first)");
            // stroke20 look numbers (TEST, judged by the user by eye): the 앞잡 ink and dry grey-black, the streak / flake sizes in metres, the ragged head, the faint body pigment
            foreach (var (prop, value) in new[] { ("_BrushTile", 0.45f), ("_CrackTile", 0.80f), ("_BrushRag", 0.07f), ("_BrushTailDry", 0.55f), ("_BrushTint", 0.32f), ("_BrushPigment", 0.7f) })
                if (mat.HasProperty(prop) && !Mathf.Approximately(mat.GetFloat(prop), value)) { mat.SetFloat(prop, value); log.Add("material " + prop + " = " + value); }
            foreach (var (prop, value) in new[] { ("_BrushInk", new Color(.055f, .05f, .047f, 1f)), ("_BrushDry", new Color(.2f, .185f, .17f, 1f)) })
                if (mat.HasProperty(prop) && mat.GetColor(prop) != value) { mat.SetColor(prop, value); log.Add("material " + prop + " = " + value); }
            // NB20 (D308-150 V6): the authored ink atlas pair (Blender + After Effects, packed by Tools/Art/SpellVFX308/nb20/nb20_pack.py): data, not colour - linear, mips that keep the
            // cutout coverage, BC7 (high quality) to keep the two 4096 maps at ~21 MB each
            foreach (var (prop, path) in new[] { ("_Sheet", SheetTexture), ("_SheetTime", SheetTimeTexture) })
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { log.Add("WARNING: no NB20 atlas at " + path + " (run nb20_pack.py, then refresh)"); continue; }
                bool dirty = ti.sRGBTexture || !ti.mipmapEnabled || ti.alphaIsTransparency || ti.textureCompression != TextureImporterCompression.CompressedHQ || ti.maxTextureSize < 4096
                    || (prop == "_Sheet" && (!ti.mipMapsPreserveCoverage || Mathf.Abs(ti.alphaTestReferenceValue - 0.5f) > 0.01f)) || ti.wrapMode != TextureWrapMode.Clamp || ti.npotScale != TextureImporterNPOTScale.None;
                if (dirty)
                {
                    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaIsTransparency = false; ti.mipmapEnabled = true; ti.maxTextureSize = 4096;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.npotScale = TextureImporterNPOTScale.None;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    if (prop == "_Sheet") { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; }
                    ti.SaveAndReimport(); log.Add("imported " + path + " (linear, mips" + (prop == "_Sheet" ? " preserving coverage" : "") + ", BC7)");
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null && mat.GetTexture(prop) != tex) { mat.SetTexture(prop, tex); log.Add("material " + prop + " = " + path); }
            }
            // NB20 look numbers (pixels on screen): the paper rim of the flying head / parry, the broken dry pigment edge of every element
            if (mat.HasProperty("_RimPx")) mat.SetFloat("_RimPx", 3f);
            if (mat.HasProperty("_EdgePx")) mat.SetFloat("_EdgePx", 1.75f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);

            var templates = new List<Vol2VfxTemplate>();
            foreach (var id in Vol2TemplateId.All())
            {
                var t = Make(TemplateFolder + "/Template_" + id.Name + ".asset", Vol2VfxDefaults.NewTemplate(id), reset, log);
                // NB20: a template the code now fills (A0 / F0 / R0) but whose asset was written empty by slice 9a takes the new defaults; P0 keeps its numbers, only its summary moves
                var code = Vol2VfxDefaults.RecipeOf(id);
                if (t.recipe != code)
                {
                    var fresh = Vol2VfxDefaults.NewTemplate(id); EditorUtility.CopySerialized(fresh, t); UnityEngine.Object.DestroyImmediate(fresh);
                    EditorUtility.SetDirty(t); log.Add("filled " + id.Name + " -> " + code);
                }
                templates.Add(t);
            }
            var bodies = new List<Vol2BodyLook>();
            foreach (Body b in Enum.GetValues(typeof(Body)))
                bodies.Add(Make(BodyFolder + "/BodyLook_" + b + ".asset", Vol2VfxDefaults.NewLook(b), reset, log));

            var cat = AssetDatabase.LoadAssetAtPath<Vol2VfxCatalogue>(CataloguePath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<Vol2VfxCatalogue>(); cat.name = "Vol2VfxCatalogue"; AssetDatabase.CreateAsset(cat, CataloguePath); log.Add("created " + CataloguePath); }
            cat.templates = templates.ToArray(); cat.bodies = bodies.ToArray(); cat.inkMaterial = mat;
            EditorUtility.SetDirty(cat);

            foreach (var t in templates) AssetDatabase.SaveAssetIfDirty(t);
            foreach (var b in bodies) AssetDatabase.SaveAssetIfDirty(b);
            AssetDatabase.SaveAssetIfDirty(mat); AssetDatabase.SaveAssetIfDirty(cat);
            bool ok = cat.Complete(out string problem);
            return (ok ? "OK" : "INCOMPLETE " + problem) + " | " + templates.Count + " templates, " + bodies.Count + " bodies, material " + MaterialPath + " | " + log.Count + " changes" + (log.Count > 0 ? "\n" + string.Join("\n", log) : "");
        }

        static string Status()
        {
            var cat = AssetDatabase.LoadAssetAtPath<Vol2VfxCatalogue>(CataloguePath);
            var shader = Shader.Find(Vol2VfxCatalogue.ShaderName);
            if (cat == null) return "no catalogue yet | shader " + (shader != null ? "imported" : "NOT imported");
            bool ok = cat.Complete(out string problem);
            int filled = 0; foreach (var t in cat.templates) if (t != null && t.filled) filled++;
            return (ok ? "complete" : "INCOMPLETE " + problem) + " | templates " + cat.templates.Length + " (filled " + filled + ") bodies " + cat.bodies.Length + " | shader " + (shader != null ? "imported" : "NOT imported");
        }
    }
}
