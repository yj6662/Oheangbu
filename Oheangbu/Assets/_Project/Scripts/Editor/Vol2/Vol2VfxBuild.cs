using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 9a (D308-150 V5): writes the vol.2 VFX data assets from the code defaults (Vol2VfxDefaults) - 24 templates, 5 body looks and the catalogue under
    /// Resources, so the runtime loads them by path (2026-10-26: the ink material / NB20 atlas of the removed ink-mesh recipes are no longer built). Queue (editor idle, no Play, no compile):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxBuild Run "all"      create what is missing (existing assets are kept: they are the data now)
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxBuild Run "reset"    overwrite every asset with the code defaults
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxBuild Run "status"   what is there
    /// Refuses (as text, never a dialog) when the editor is busy. Saves only the assets it touched (SaveAssetIfDirty): another session's dirty assets are not flushed.</summary>
    public static class Vol2VfxBuild
    {
        public const string Root = "Assets/_Project/Art/SpellVFX308/Vol2Vfx";
        public const string ResourcesFolder = Root + "/Resources/Vol2Vfx";
        public const string TemplateFolder = Root + "/Templates", BodyFolder = Root + "/Bodies";
        public const string CataloguePath = ResourcesFolder + "/Vol2VfxCatalogue.asset";

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
            var log = new List<string>();
            Folder(TemplateFolder); Folder(BodyFolder); Folder(ResourcesFolder);

            var templates = new List<Vol2VfxTemplate>();
            foreach (var id in Vol2TemplateId.All())
            {
                var t = Make(TemplateFolder + "/Template_" + id.Name + ".asset", Vol2VfxDefaults.NewTemplate(id), reset, log);
                // a template the code now fills but whose asset was written empty by an older slice takes the new defaults
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
            cat.templates = templates.ToArray(); cat.bodies = bodies.ToArray();
            EditorUtility.SetDirty(cat);

            foreach (var t in templates) AssetDatabase.SaveAssetIfDirty(t);
            foreach (var b in bodies) AssetDatabase.SaveAssetIfDirty(b);
            AssetDatabase.SaveAssetIfDirty(cat);
            bool ok = cat.Complete(out string problem);
            return (ok ? "OK" : "INCOMPLETE " + problem) + " | " + templates.Count + " templates, " + bodies.Count + " bodies | " + log.Count + " changes" + (log.Count > 0 ? "\n" + string.Join("\n", log) : "");
        }

        static string Status()
        {
            var cat = AssetDatabase.LoadAssetAtPath<Vol2VfxCatalogue>(CataloguePath);
            if (cat == null) return "no catalogue yet";
            bool ok = cat.Complete(out string problem);
            int filled = 0; foreach (var t in cat.templates) if (t != null && t.filled) filled++;
            return (ok ? "complete" : "INCOMPLETE " + problem) + " | templates " + cat.templates.Length + " (filled " + filled + ") bodies " + cat.bodies.Length;
        }
    }
}
