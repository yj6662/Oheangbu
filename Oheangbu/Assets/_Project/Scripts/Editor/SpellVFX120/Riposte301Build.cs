using System.IO;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D301 앞잡: glyph atlas import, material and the Resources profile the walker loads.
    //   build — Art/SpellVFX120/Riposte301/{M_RiposteGlyph301.mat}, Assets/Resources/Riposte301Profile.asset
    public static class Riposte301Build
    {
        const string Folder = "Assets/_Project/Art/SpellVFX120/Riposte301/";
        public static string Run(string command)
        {
            if (command != "build") throw new System.ArgumentException("Riposte301Build: build");
            string atlas = Folder + "GlyphAtlas_Gungseo301.png";
            AssetDatabase.ImportAsset(atlas, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(atlas);
            importer.alphaIsTransparency = true; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.sRGBTexture = true;
            importer.SaveAndReimport();
            var glyph = Bolt300Build.Mat("M_RiposteGlyph301", "GlyphAtlas_Gungseo301.png", false, Folder, Folder.TrimEnd('/'));
            glyph.SetFloat("_SoftParticlesEnabled", 0f); glyph.DisableKeyword("_SOFTPARTICLES_ON"); glyph.DisableKeyword("_FADING_ON");
            EditorUtility.SetDirty(glyph);
            Directory.CreateDirectory("Assets/Resources");
            string path = "Assets/Resources/Riposte301Profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<Riposte301Profile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<Riposte301Profile>(); AssetDatabase.CreateAsset(profile, path); }
            profile.GlyphMaterial = glyph;
            // #302 magic circle and ink splash (textures from Tools/Art/riposte302_textures.py), double sided
            foreach (var tex in new[] { "MagicCircle302.png", "InkSplash302.png", "WispOrb302.png" })
            {
                AssetDatabase.ImportAsset(Folder + tex, ImportAssetOptions.ForceSynchronousImport);
                var ti = (TextureImporter)AssetImporter.GetAtPath(Folder + tex);
                ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.sRGBTexture = true; ti.SaveAndReimport();
            }
            var ring = Bolt300Build.Mat("M_RiposteRing302", "MagicCircle302.png", false, Folder, Folder.TrimEnd('/'));
            var splash = Bolt300Build.Mat("M_RiposteSplash302", "InkSplash302.png", false, Folder, Folder.TrimEnd('/'));
            var orb = Bolt300Build.Mat("M_RiposteOrb302", "WispOrb302.png", false, Folder, Folder.TrimEnd('/'));
            foreach (var m in new[] { ring, splash, glyph, orb })
            {
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                m.SetFloat("_SoftParticlesEnabled", 0f); m.DisableKeyword("_SOFTPARTICLES_ON"); m.DisableKeyword("_FADING_ON");
                EditorUtility.SetDirty(m);
            }
            profile.RingMaterial = ring; profile.SplashMaterial = splash; profile.OrbMaterial = orb;
            profile.TrailMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/SpellVFX120/Bolt300/Materials/M_B300_Trail.mat");
            profile.Layer = Bolt300Build.AfterFogLayer;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return "glyph " + AssetDatabase.GetAssetPath(glyph) + " profile " + path;
        }
    }
}
