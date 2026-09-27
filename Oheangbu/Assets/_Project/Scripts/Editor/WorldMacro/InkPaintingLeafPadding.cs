using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // One verified black-matte source only. No shader, scene or original importer edits.
    // Prepare performs import/readback validation. SetEnabled changes only matching
    // derived BaseMap references; Restore can run after a domain reload using receipt.
    public static class InkPaintingLeafPadding
    {
        const string SourceGuid = "e11609022153bd349938ba23fdbd13f5";
        const string SourcePath = "Assets/_Project/Art/World/WorldMacro/Dressing/Textures/abc00000000001471862224526166097_Color_1024.png";
        const string StudyFolder = "Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision";
        const string Folder = StudyFolder + "/LeafPadding";
        const string TargetPath = Folder + "/e11609022153bd349938ba23fdbd13f5_Padded.png";
        const string ReceiptPath = Folder + "/prepare.json";
        const string ReadbackShaderName = "Hidden/Oheangbu/Study/LeafPaddingMipReadback";
        const int ExpectedMaterials = 205, ExpectedMatches = 9;

        [Serializable] public sealed class MipCheck
        {
            public int mip, pixels, alphaMismatches;
            public int transparentRgbChanges, opaqueRgbChanges;
            public float maxAlphaDifference, maxRgbDifference;
        }
        [Serializable] public sealed class Receipt
        {
            public string status, utc, sourcePath, sourceGuid, sourceHash, sourceMetaHash;
            public string targetPath, targetGuid, targetHash, message;
            public int materials, matches, rawWidth, rawHeight, rawTransparent, rawOpaque, rawPartial;
            public string[] materialPaths;
            public MipCheck[] mips;
            public bool sourceUnchanged, rawCopyIdentical, importedAlphaIdentical;
        }

        public static string Inspect()
        {
            RequireEdit();
            var all = Materials(); var matches = Matching(all, SourceGuid);
            var receipt = NewReceipt(all.Length, matches);
            InspectRaw(receipt);
            receipt.status = "READ_ONLY_SOURCE_INSPECTED";
            return JsonUtility.ToJson(receipt, true);
        }

        public static string Prepare()
        {
            RequireEdit();
            RequireFloatReadback();
            if (QualitySettings.globalTextureMipmapLimit != 0)
                throw new InvalidOperationException("All-mip comparison requires globalTextureMipmapLimit=0; helper does not change quality settings.");
            var all = Materials();
            var existingGuid = AssetDatabase.AssetPathToGUID(TargetPath);
            var matches = all.Where(m => TextureGuid(m) == SourceGuid || (!string.IsNullOrEmpty(existingGuid) && TextureGuid(m) == existingGuid)).ToArray();
            ValidateMatches(matches);
            var receipt = NewReceipt(all.Length, matches);
            var materialHashes = all.ToDictionary(AssetDatabase.GetAssetPath, m => Hash(AssetDatabase.GetAssetPath(m)));
            InspectRaw(receipt);
            EnsureFolder(Folder);
            try
            {
                if (!File.Exists(Full(TargetPath)))
                {
                    if (!AssetDatabase.CopyAsset(SourcePath, TargetPath)) throw new IOException("CopyAsset failed for the owned padded derivative.");
                }
                if (Hash(TargetPath) != receipt.sourceHash) throw new InvalidOperationException("Existing derivative PNG differs from the original bytes; refusing overwrite.");
                AssetDatabase.ImportAsset(TargetPath, ImportAssetOptions.ForceSynchronousImport);
                var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(SourcePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TargetPath);
                if (sourceImporter == null || importer == null) throw new InvalidOperationException("Expected PNG TextureImporter instances.");
                // CopyAsset preserves the complete source importer/platform configuration.
                // This is the sole importer property changed, on the new GUID only.
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                VerifyImportSettings(sourceImporter, importer);
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourcePath);
                var padded = AssetDatabase.LoadAssetAtPath<Texture2D>(TargetPath);
                receipt.targetGuid = AssetDatabase.AssetPathToGUID(TargetPath);
                if (receipt.targetGuid == SourceGuid || string.IsNullOrEmpty(receipt.targetGuid)) throw new InvalidOperationException("Derivative must have a distinct valid GUID.");
                receipt.targetHash = Hash(TargetPath);
                receipt.rawCopyIdentical = receipt.targetHash == receipt.sourceHash;
                receipt.mips = CompareMips(source, padded);
                receipt.importedAlphaIdentical = receipt.mips.All(m => m.alphaMismatches == 0);
                receipt.sourceUnchanged = SourceUnchanged(receipt);
                if (!receipt.sourceUnchanged || !receipt.rawCopyIdentical) throw new InvalidOperationException("Source/copy preservation check failed.");
                if (!receipt.importedAlphaIdentical) throw new InvalidOperationException("Imported alpha differs; no material bindings were changed. See mip checks.");
                if (!receipt.mips.Any(m => m.transparentRgbChanges > 0)) throw new InvalidOperationException("No transparent RGB difference detected; padding benefit is unverified.");
                if (materialHashes.Any(p => Hash(p.Key) != p.Value)) throw new InvalidOperationException("A material changed during preparation.");
                receipt.status = "PREPARED_ALPHA_IDENTICAL_NOT_BOUND";
                receipt.message = "Original PNG/meta unchanged, copied PNG bytes identical, all imported mip alphas identical. Actual canopy comparison remains required.";
                Write(ReceiptPath, receipt);
                return JsonUtility.ToJson(receipt, true);
            }
            catch (Exception ex)
            {
                receipt.status = "FAIL_NOT_BOUND"; receipt.message = ex.Message;
                receipt.sourceUnchanged = SourceUnchanged(receipt);
                Write(Folder + "/prepare_failed.json", receipt);
                throw;
            }
        }

        public static string Restore() => SetEnabled(false);

        public static string SetEnabled(bool enabled)
        {
            RequireEdit();
            if (!File.Exists(Full(ReceiptPath))) throw new InvalidOperationException("Prepare the derivative before changing references.");
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(Full(ReceiptPath)));
            if (receipt == null || receipt.status != "PREPARED_ALPHA_IDENTICAL_NOT_BOUND" || !receipt.importedAlphaIdentical)
                throw new InvalidOperationException("Prepare receipt is not a verified alpha-identical derivative.");
            if (!SourceUnchanged(receipt) || Hash(TargetPath) != receipt.targetHash || AssetDatabase.AssetPathToGUID(TargetPath) != receipt.targetGuid)
                throw new InvalidOperationException("Source or derivative identity changed after preparation.");
            var all = Materials();
            var matches = receipt.materialPaths.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            ValidateMatches(matches);
            if (matches.Any(m => TextureGuid(m) != SourceGuid && TextureGuid(m) != receipt.targetGuid))
                throw new InvalidOperationException("A selected BaseMap changed independently; refusing to overwrite it.");
            if (all.Where(m => TextureGuid(m) == SourceGuid || TextureGuid(m) == receipt.targetGuid).Any(m => !matches.Contains(m)))
                throw new InvalidOperationException("Additional matching references appeared after preparation; inspect the changed scope.");
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourcePath);
            var padded = AssetDatabase.LoadAssetAtPath<Texture2D>(TargetPath);
            if (enabled)
            {
                VerifyImportSettings((TextureImporter)AssetImporter.GetAtPath(SourcePath), (TextureImporter)AssetImporter.GetAtPath(TargetPath));
                if (CompareMips(source, padded).Any(m => m.alphaMismatches != 0)) throw new InvalidOperationException("Fresh GPU alpha verification failed; references were not changed.");
            }
            var originals = matches.Select(m => m.GetTexture("_BaseMap")).ToArray();
            var states = matches.Select(MaterialState).ToArray();
            var untouched = all.Except(matches).ToDictionary(AssetDatabase.GetAssetPath, m => Hash(AssetDatabase.GetAssetPath(m)));
            try
            {
                for (int i = 0; i < matches.Length; i++)
                {
                    matches[i].SetTexture("_BaseMap", enabled ? padded : source);
                    if (MaterialState(matches[i]) != states[i]) throw new InvalidOperationException("A property other than BaseMap changed.");
                    EditorUtility.SetDirty(matches[i]); AssetDatabase.SaveAssetIfDirty(matches[i]);
                }
                if (untouched.Any(p => Hash(p.Key) != p.Value) || !SourceUnchanged(receipt)) throw new InvalidOperationException("Unrelated materials or original PNG/meta changed.");
                string message = (enabled ? "Enabled" : "Restored") + " padded BaseMap on " + matches.Length + "/" + all.Length + " derived materials; alpha/other settings preserved. No scene saved.";
                File.WriteAllText(Full(Folder + "/binding_status.txt"), DateTime.UtcNow.ToString("o") + "\n" + message);
                return message;
            }
            catch (Exception failure)
            {
                var errors = new List<string> { failure.Message };
                for (int i = 0; i < matches.Length; i++)
                    try { matches[i].SetTexture("_BaseMap", originals[i]); EditorUtility.SetDirty(matches[i]); AssetDatabase.SaveAssetIfDirty(matches[i]); }
                    catch (Exception ex) { errors.Add("Restore " + AssetDatabase.GetAssetPath(matches[i]) + ": " + ex.Message); }
                throw new InvalidOperationException("Binding failed; rollback attempted for every selected material: " + string.Join(" | ", errors), failure);
            }
        }

        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Use the padding comparison in stable Edit mode.");
            if (InkPaintingFoliagePreview.IsActive)
                throw new InvalidOperationException("End the foliage preview before importing or switching padding references.");
            if (AssetDatabase.AssetPathToGUID(SourcePath) != SourceGuid) throw new InvalidOperationException("The known source GUID/path no longer matches.");
        }
        static Material[] Materials()
        {
            var paths = AssetDatabase.FindAssets("t:Material", new[] { StudyFolder + "/Materials", StudyFolder + "/FoliageMaterials" })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (paths.Length != ExpectedMaterials) throw new InvalidOperationException("Expected205 current study materials, found " + paths.Length);
            return paths.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
        }
        static Material[] Matching(Material[] all, string guid) { var result = all.Where(m => TextureGuid(m) == guid).ToArray(); ValidateMatches(result); return result; }
        static string TextureGuid(Material m) => m != null && m.HasProperty("_BaseMap") ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap"))) : "";
        static void ValidateMatches(Material[] materials)
        {
            if (materials.Length != ExpectedMatches || materials.Any(m => m == null || !AssetDatabase.GetAssetPath(m).StartsWith(StudyFolder + "/FoliageMaterials/", StringComparison.Ordinal)
                || (m.shader.name != "Oheangbu/Study/InkPaintingVegetation" && m.shader.name != "Oheangbu/Study/InkPaintingFoliageCard")
                || !m.HasProperty("_AlphaClip") || m.GetFloat("_AlphaClip") < .5f))
                throw new InvalidOperationException("Expected exactly9 owned alpha-clipped foliage derivatives for the known texture.");
        }
        static Receipt NewReceipt(int count, Material[] matches) => new Receipt { utc = DateTime.UtcNow.ToString("o"), sourcePath = SourcePath, sourceGuid = SourceGuid,
            sourceHash = Hash(SourcePath), sourceMetaHash = Hash(SourcePath + ".meta"), targetPath = TargetPath, materials = count, matches = matches.Length,
            materialPaths = matches.Select(AssetDatabase.GetAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray() };
        static bool SourceUnchanged(Receipt r) => Hash(SourcePath) == r.sourceHash && Hash(SourcePath + ".meta") == r.sourceMetaHash;
        static void InspectRaw(Receipt r)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(Full(SourcePath)), false)) throw new IOException("PNG decode failed.");
                r.rawWidth = decoded.width; r.rawHeight = decoded.height;
                foreach (var p in decoded.GetPixels32())
                {
                    if (p.a == 0) { r.rawTransparent++; if (p.r != 0 || p.g != 0 || p.b != 0) throw new InvalidOperationException("Source is no longer the verified zero-RGB transparent matte."); }
                    else if (p.a == 255) r.rawOpaque++; else r.rawPartial++;
                }
                // This straight-RGBA source also contains original edge alpha195..254.
                // Preserve those values; byte-identical PNG and every-mip GPU alpha
                // comparisons below verify preservation without assuming binary alpha.
                if (r.rawTransparent == 0 || r.rawOpaque == 0) throw new InvalidOperationException("Source lacks the verified transparent matte or opaque leaf pixels.");
            }
            finally { Object.DestroyImmediate(decoded); }
        }
        static void VerifyImportSettings(TextureImporter source, TextureImporter target)
        {
            if (source == null || target == null || source.alphaIsTransparency || !target.alphaIsTransparency) throw new InvalidOperationException("Unexpected original/padded alphaIsTransparency settings.");
            var a = new TextureImporterSettings(); var b = new TextureImporterSettings(); source.ReadTextureSettings(a); target.ReadTextureSettings(b); a.alphaIsTransparency = true;
            if (!TextureImporterSettings.Equal(a, b)) throw new InvalidOperationException("Texture settings changed beyond alphaIsTransparency.");
            if (source.textureCompression != target.textureCompression || source.compressionQuality != target.compressionQuality || source.crunchedCompression != target.crunchedCompression)
                throw new InvalidOperationException("Compression settings differ.");
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "Android", "iPhone", "iOS", "WebGL" })
                if (JsonUtility.ToJson(source.GetPlatformTextureSettings(platform)) == "{}" || JsonUtility.ToJson(source.GetPlatformTextureSettings(platform)) != JsonUtility.ToJson(target.GetPlatformTextureSettings(platform)))
                    throw new InvalidOperationException("Platform settings differ: " + platform);
        }
        static MipCheck[] CompareMips(Texture2D source, Texture2D padded)
        {
            RequireFloatReadback();
            if (QualitySettings.globalTextureMipmapLimit != 0) throw new InvalidOperationException("Full mip GPU verification requires globalTextureMipmapLimit=0.");
            if (source == null || padded == null || source.width != padded.width || source.height != padded.height || source.mipmapCount != padded.mipmapCount)
                throw new InvalidOperationException("Imported texture dimensions/mip counts differ.");
            var checks = new List<MipCheck>();
            for (int mip = 0; mip < source.mipmapCount; mip++)
            {
                var a = ReadMip(source, mip); var b = ReadMip(padded, mip);
                if (a.Length != b.Length) throw new InvalidOperationException("GPU mip lengths differ.");
                if (mip == 0 && (!a.Any(p => p.a == 0) || !a.Any(p => p.a == 1)))
                    throw new InvalidOperationException("Readback did not retain known source transparent/opaque alpha; refusing a false equal result.");
                var check = new MipCheck { mip = mip, pixels = a.Length };
                for (int i = 0; i < a.Length; i++)
                {
                    if (float.IsNaN(a[i].a) || float.IsNaN(b[i].a) || a[i].a < 0 || a[i].a > 1 || b[i].a < 0 || b[i].a > 1)
                        throw new InvalidOperationException("Invalid GPU alpha readback at mip " + mip);
                    float alpha = Math.Abs(a[i].a - b[i].a); if (alpha != 0) check.alphaMismatches++;
                    check.maxAlphaDifference = Math.Max(check.maxAlphaDifference, alpha);
                    float rgb = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                    check.maxRgbDifference = Math.Max(check.maxRgbDifference, rgb);
                    if (rgb > 0) { if (a[i].a == 0) check.transparentRgbChanges++; if (a[i].a == 1) check.opaqueRgbChanges++; }
                }
                checks.Add(check);
            }
            return checks.ToArray();
        }
        static Color[] ReadMip(Texture2D texture, int mip)
        {
            // Some backends reject compressed Texture2D -> RGBAFloat async conversion.
            // Load each exact texel/mip into a same-size float RT first. No sampler,
            // filtering, alpha blending or source importer mutation is involved.
            int width = Math.Max(1, texture.width >> mip), height = Math.Max(1, texture.height >> mip);
            var previous = RenderTexture.active;
            RenderTexture target = null; Texture2D cpu = null; Material material = null;
            try
            {
                var shader = Shader.Find(ReadbackShaderName);
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Import the supported staging LeafPaddingMipReadback.shader before preparing textures.");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetFloat("_ReadbackMip", mip);
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.filterMode = FilterMode.Point;
                Graphics.Blit(texture, target, material, 0);
                RenderTexture.active = target;
                cpu = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
                cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return cpu.GetPixels(0);
            }
            finally
            {
                RenderTexture.active = previous;
                if (cpu != null) Object.DestroyImmediate(cpu);
                if (material != null) Object.DestroyImmediate(material);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }
        static void RequireFloatReadback()
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                throw new InvalidOperationException("Float render/readback is unsupported; alpha validation cannot be weakened to an8-bit comparison.");
            var shader = Shader.Find(ReadbackShaderName);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Import staging LeafPaddingMipReadback.shader before preparing textures.");
        }
        static string MaterialState(Material material)
        {
            var parts = new List<string> { AssetDatabase.GetAssetPath(material.shader), material.renderQueue.ToString(), material.enableInstancing.ToString(), material.doubleSidedGI.ToString(), material.globalIlluminationFlags.ToString() };
            for (int i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                string name = material.shader.GetPropertyName(i); var type = material.shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Texture)
                {
                    if (name != "_BaseMap") parts.Add(name + ":" + AssetDatabase.GetAssetPath(material.GetTexture(name)));
                    parts.Add(name + ":ST=" + material.GetTextureScale(name).ToString("R") + material.GetTextureOffset(name).ToString("R"));
                }
                else if (type == ShaderPropertyType.Color) parts.Add(name + ":" + material.GetColor(name).ToString("R"));
                else if (type == ShaderPropertyType.Vector) parts.Add(name + ":" + material.GetVector(name).ToString("R"));
                else if (type == ShaderPropertyType.Int) parts.Add(name + ":" + material.GetInteger(name).ToString(System.Globalization.CultureInfo.InvariantCulture));
                else parts.Add(name + ":" + material.GetFloat(name).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            parts.AddRange(material.enabledKeywords.Select(k => k.name).OrderBy(n => n, StringComparer.Ordinal));
            return string.Join("|", parts);
        }
        static string Full(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(Full(path))) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static void Write(string path, Receipt r) => File.WriteAllText(Full(path), JsonUtility.ToJson(r, true));
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    }
}
