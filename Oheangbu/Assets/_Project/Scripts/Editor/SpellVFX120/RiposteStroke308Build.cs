using System;
using System.IO;
using System.Text;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-148 앞잡 = 일필휘지 한 획 (step 5): imports the stroke's textures, makes its two materials and its profile, and points the
    // riposte's Resources profile at it. Run through the editor queue (the editor is needed: texture import, shader, assets):
    //   Oheangbu.EditorTools.SpellVFX120.RiposteStroke308Build Run "build"   (safe to repeat: nothing is duplicated, tuned values are kept)
    //   Oheangbu.EditorTools.SpellVFX120.RiposteStroke308Build Run "check"   (read only: what exists, what the shader says)
    // Needs, already in place: Assets/_Project/Shaders/SpellVFX308/RiposteInk308.shader and the three textures of
    // Tools/Art/riposte308_stroke_textures.py in Assets/_Project/Art/SpellVFX308/RiposteStroke/.
    // Creates / writes ONLY:
    //   Art/SpellVFX308/RiposteStroke/M_RiposteInk308.mat       (Oheangbu/SpellVFX308/RiposteInk + bristle and crack textures)
    //   Art/SpellVFX308/RiposteStroke/M_RiposteFlake308.mat     (Oheangbu/SpellVFX308/InkFlipbook + the flake atlas)
    //   Art/SpellVFX308/RiposteStroke/RiposteStroke308Profile.asset   (RiposteStroke308Profile; on a repeat only its two material references)
    //   import settings of the three PNGs (linear data, no sRGB)
    //   Assets/Resources/Riposte301Profile.asset: the field Stroke (GUID and every other field untouched)
    // It never touches the old glyph / ring / orb / splash materials, Riposte301Build's LEGACY look, any scene, LookDriver308 or a protected tree.
    public static class RiposteStroke308Build
    {
        const string Folder = "Assets/_Project/Art/SpellVFX308/RiposteStroke/";
        const string ShaderPath = "Assets/_Project/Shaders/SpellVFX308/RiposteInk308.shader";
        const string ProfilePath = Folder + "RiposteStroke308Profile.asset";
        const string RipostePath = "Assets/Resources/Riposte301Profile.asset";
        const string Bristle = "RipStroke308_Bristle.png", Crack = "RipStroke308_Crack.png", Flakes = "RipStroke308_Flakes.png";

        public static string Run(string command)
        {
            switch ((command ?? "").Trim())
            {
                case "build": return Build();
                case "check": return Check();
                default: throw new ArgumentException("riposte-stroke308-build: build | check");
            }
        }

        static string Build()
        {
            foreach (var name in new[] { Bristle, Crack, Flakes })
                if (!File.Exists(Folder + name)) throw new FileNotFoundException("missing " + Folder + name + " - run Tools/Art/riposte308_stroke_textures.py and copy the PNGs there");
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var ink = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (ink == null) throw new InvalidOperationException("shader not found: " + ShaderPath);
            string problems = ShaderProblems(ink);
            if (problems.Length > 0) throw new InvalidOperationException("RiposteInk308 does not compile: " + problems);
            var flipbook = Shader.Find("Oheangbu/SpellVFX308/InkFlipbook");
            if (flipbook == null) throw new InvalidOperationException("shader Oheangbu/SpellVFX308/InkFlipbook not found");

            var bristle = Import(Bristle, TextureWrapMode.Repeat, TextureWrapMode.Clamp, true);
            var crack = Import(Crack, TextureWrapMode.Repeat, TextureWrapMode.Repeat, true);
            var flakes = Import(Flakes, TextureWrapMode.Clamp, TextureWrapMode.Clamp, true);

            var strokeMat = Mat("M_RiposteInk308", ink);
            strokeMat.SetTexture("_Bristle", bristle); strokeMat.SetTexture("_Crack", crack);
            strokeMat.SetColor("_BaseColor", new Color(.055f, .05f, .047f, 1f)); strokeMat.SetColor("_DryColor", new Color(.2f, .185f, .17f, 1f));
            strokeMat.SetVector("_Tiling", new Vector4(3f, 1f, 0f, 0f)); strokeMat.SetVector("_CrackTiling", new Vector4(9f, 1.6f, 0f, 0f));
            strokeMat.SetFloat("_RevealS", 1f); strokeMat.SetFloat("_Dry", 0f); strokeMat.SetFloat("_HeadRag", .05f); strokeMat.SetFloat("_TailDry", .55f);
            strokeMat.SetFloat("_NearClip", .12f); strokeMat.SetFloat("_NearFade", .3f);
            strokeMat.enableInstancing = false;
            EditorUtility.SetDirty(strokeMat);

            var flakeMat = Mat("M_RiposteFlake308", flipbook);
            flakeMat.SetTexture("_MainTex", flakes);
            flakeMat.SetColor("_Ink", new Color(.075f, .065f, .058f, .35f));
            flakeMat.SetFloat("_CoreLo", .1f); flakeMat.SetFloat("_CoreHi", .4f); flakeMat.SetFloat("_Soft", .09f); flakeMat.SetFloat("_ErodeSteps", 0f);
            flakeMat.SetFloat("_Heat", 0f); flakeMat.SetFloat("_Glow", 0f);            // matte ink: no heat, no glow, ever
            flakeMat.SetFloat("_NearClip", .12f); flakeMat.SetFloat("_NearFade", .3f);
            EditorUtility.SetDirty(flakeMat);

            var profile = AssetDatabase.LoadAssetAtPath<RiposteStroke308Profile>(ProfilePath);
            bool created = profile == null;
            if (created) { profile = ScriptableObject.CreateInstance<RiposteStroke308Profile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            profile.StrokeMaterial = strokeMat; profile.FlakeMaterial = flakeMat;       // a repeat keeps every tuned number
            EditorUtility.SetDirty(profile);

            var riposte = AssetDatabase.LoadAssetAtPath<Riposte301Profile>(RipostePath);
            if (riposte == null) throw new InvalidOperationException("missing " + RipostePath + " (it is not created here: its GUID is the loader's contract)");
            riposte.Stroke = profile;
            EditorUtility.SetDirty(riposte);

            foreach (UnityEngine.Object o in new UnityEngine.Object[] { strokeMat, flakeMat, profile, riposte }) AssetDatabase.SaveAssetIfDirty(o);   // not SaveAssets: other sessions' dirty assets stay theirs
            return (created ? "created " : "updated ") + ProfilePath + "; materials " + AssetDatabase.GetAssetPath(strokeMat) + ", " + AssetDatabase.GetAssetPath(flakeMat)
                + "; " + RipostePath + " Stroke -> profile";
        }

        static string Check()
        {
            var b = new StringBuilder();
            foreach (var name in new[] { Bristle, Crack, Flakes }) b.Append(name).Append(File.Exists(Folder + name) ? " ok; " : " MISSING; ");
            var ink = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            b.Append("shader ").Append(ink == null ? "MISSING" : ShaderProblems(ink).Length == 0 ? "ok" : "ERRORS " + ShaderProblems(ink)).Append("; ");
            var profile = AssetDatabase.LoadAssetAtPath<RiposteStroke308Profile>(ProfilePath);
            b.Append("profile ").Append(profile == null ? "MISSING" : profile.Ready ? "ready" : "no materials").Append("; ");
            var riposte = AssetDatabase.LoadAssetAtPath<Riposte301Profile>(RipostePath);
            b.Append("Riposte301Profile.Stroke ").Append(riposte == null ? "(no asset)" : riposte.Stroke == null ? "EMPTY" : "set");
            return b.ToString();
        }

        static string ShaderProblems(Shader shader)
        {
            var text = new StringBuilder();
            if (!shader.isSupported) text.Append("not supported; ");
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) text.Append(m.message).Append(" (line ").Append(m.line).Append("); ");
            return text.ToString();
        }

        // data textures: linear, uncompressed (the thin gaps must survive), mips on (no shimmer), the cells of the flake sheet have a clear margin
        static Texture2D Import(string name, TextureWrapMode u, TextureWrapMode v, bool mips)
        {
            string path = Folder + name;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("no importer for " + path);
            bool dirty = importer.sRGBTexture || importer.alphaIsTransparency || importer.mipmapEnabled != mips || importer.wrapModeU != u || importer.wrapModeV != v
                || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 2;
            if (dirty)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false; importer.alphaIsTransparency = false;
                importer.mipmapEnabled = mips; importer.wrapModeU = u; importer.wrapModeV = v;
                importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 2;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Shader shader)
        {
            string path = Folder + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
        }
    }
}
