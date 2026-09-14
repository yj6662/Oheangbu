using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroVisualCorridorAuthoring
    {
        private static readonly string[] ThinCutoutTokens =
        {
            "leaf", "leaves", "foliage", "fern", "frond", "grass", "flower", "petal", "needle"
        };

        [Serializable]
        private sealed class MaterialOptimizationReport
        {
            public int ownedMaterials;
            public int refreshedMaterials;
            public int twoSidedCutouts;
            public int backfaceCulled;
            public string[] refreshedPaths = Array.Empty<string>();
        }

        private sealed class SourceMaterial
        {
            public Material Material;
            public string Guid;
            public string Key;
        }

        private sealed class OwnedMaterialBinding
        {
            public Material Owned;
            public string Path;
            public SourceMaterial Source;
        }

        /// <summary>
        /// Refreshes only corridor-owned material copies. Supplier assets are read-only, and an
        /// ambiguous legacy GUID cache is rejected before any material is changed.
        /// </summary>
        public static string OptimizeMaterials()
        {
            EnsureFolders();
            var failures = new List<string>();
            List<OwnedMaterialBinding> bindings = ResolveOwnedMaterialBindings(failures);
            if (failures.Count > 0)
                throw new InvalidOperationException("Visual corridor material preflight failed:\n" + string.Join("\n", failures));

            var report = new MaterialOptimizationReport { ownedMaterials = bindings.Count };
            var refreshedPaths = new List<string>(bindings.Count);
            foreach (OwnedMaterialBinding binding in bindings)
            {
                Material source = binding.Source.Material;
                Material owned = binding.Owned;
                RefreshSurfaceMetadata(owned, source);

                bool thinCutout = IsThinCutout(source);
                CullMode sourceCull = ReadSourceCull(source);
                // The reviewed gate roof is a one-sided tile sheet: its underside is visible from ground level.
                bool reviewedRoofSheet = binding.Source.Guid == "abc00000000003262796300251567504";
                CullMode desiredCull = reviewedRoofSheet ? CullMode.Off : sourceCull == CullMode.Off && !thinCutout ? CullMode.Back : sourceCull;
                owned.SetFloat("_Cull", (float)desiredCull);
                owned.doubleSidedGI = desiredCull == CullMode.Off && source.doubleSidedGI;
                EditorUtility.SetDirty(owned);

                report.refreshedMaterials++;
                if (desiredCull == CullMode.Off) report.twoSidedCutouts++;
                else if (desiredCull == CullMode.Back) report.backfaceCulled++;
                refreshedPaths.Add(binding.Path);
            }

            report.refreshedPaths = refreshedPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            AssetDatabase.SaveAssets();
            return JsonUtility.ToJson(report, true);
        }

        /// <summary>
        /// Read-only validation hook for ValidateGeometry. It deliberately reports drift instead
        /// of updating SourceHash, so cached geometry cannot silently claim a newer dependency.
        /// </summary>
        public static string[] ValidateCacheDrift()
        {
            var failures = new List<string>();
            var records = Sheet.Sources ?? Array.Empty<Oheangbu.Data.World.WorldMacroVisualCorridorSO.SourceRecord>();
            foreach (var sourceGroup in records.Where(row => row != null && !string.IsNullOrWhiteSpace(row.SourcePath))
                         .GroupBy(row => row.SourcePath, StringComparer.Ordinal))
            {
                string path = sourceGroup.Key;
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                {
                    failures.Add("Visual corridor source is missing: " + path);
                    continue;
                }

                string actual = AssetDatabase.GetAssetDependencyHash(path).ToString();
                string[] staleIds = sourceGroup.Where(row => !string.Equals(row.SourceHash, actual, StringComparison.Ordinal))
                    .Select(row => row.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().OrderBy(id => id).ToArray();
                if (staleIds.Length > 0)
                    failures.Add("Visual corridor cache drift for " + path + "; recorded rows: " + string.Join(",", staleIds)
                        + ". Rebuild the owned cache before accepting validation.");
            }

            // This also detects a legacy GUID-only material cache that points at multiple
            // material sub-assets in the same FBX. The robust identity is GUID + local file ID.
            ResolveOwnedMaterialBindings(failures);
            return failures.Distinct().ToArray();
        }

        private static List<OwnedMaterialBinding> ResolveOwnedMaterialBindings(List<string> failures)
        {
            List<SourceMaterial> sources = ReadSourceMaterials(failures);
            var byGuid = sources.GroupBy(source => source.Guid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var byKey = sources.ToDictionary(source => source.Key, source => source, StringComparer.Ordinal);
            var result = new List<OwnedMaterialBinding>();
            string materialFolder = Folder + "/Materials";

            foreach (string assetGuid in AssetDatabase.FindAssets("t:Material", new[] { materialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuid);
                Material owned = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (owned == null || owned.shader == null || owned.shader.name != "Oheangbu/WorldMacroTexturedSurface")
                    continue;

                string stem = Path.GetFileNameWithoutExtension(path);
                SourceMaterial source = null;
                if (!byKey.TryGetValue(stem, out source) && byGuid.TryGetValue(stem, out SourceMaterial[] legacy))
                {
                    if (legacy.Length == 1) source = legacy[0];
                    else
                        failures.Add("Ambiguous legacy material cache " + path + " maps to " + legacy.Length
                            + " source materials. Use GUID + local file ID: "
                            + string.Join(",", legacy.Select(candidate => candidate.Key).OrderBy(key => key)) + ".");
                }

                if (source == null)
                {
                    if (!byGuid.ContainsKey(stem))
                        failures.Add("Owned corridor material has no exact recorded source: " + path + ".");
                    continue;
                }

                result.Add(new OwnedMaterialBinding { Owned = owned, Path = path, Source = source });
            }

            return result;
        }

        private static List<SourceMaterial> ReadSourceMaterials(List<string> failures)
        {
            var result = new Dictionary<string, SourceMaterial>(StringComparer.Ordinal);
            var records = Sheet.Sources ?? Array.Empty<Oheangbu.Data.World.WorldMacroVisualCorridorSO.SourceRecord>();
            foreach (string sourcePath in records.Where(row => row != null && !string.IsNullOrWhiteSpace(row.SourcePath))
                         .Select(row => row.SourcePath).Distinct(StringComparer.Ordinal))
            {
                GameObject sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (sourceRoot == null) continue;
                foreach (Material material in sourceRoot.GetComponentsInChildren<Renderer>(true)
                             .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct())
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId)
                        || string.IsNullOrWhiteSpace(guid))
                    {
                        failures.Add("Recorded source uses a material without an asset identity: " + sourcePath + "/" + material.name + ".");
                        continue;
                    }

                    string key = MaterialKey(guid, localId);
                    if (!result.ContainsKey(key))
                        result.Add(key, new SourceMaterial { Material = material, Guid = guid, Key = key });
                }
            }
            return result.Values.ToList();
        }

        private static string MaterialKey(string guid, long localId)
        {
            return guid + "_" + localId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void RefreshSurfaceMetadata(Material owned, Material source)
        {
            string baseProperty = FirstTextureProperty(source, "_BaseMap", "_MainTex", "_DiffuseMap", "_Albedo");
            Texture baseTexture = baseProperty == null ? source.mainTexture : source.GetTexture(baseProperty);
            owned.SetTexture("_BaseMap", baseTexture);
            if (baseProperty != null)
            {
                owned.SetTextureScale("_BaseMap", source.GetTextureScale(baseProperty));
                owned.SetTextureOffset("_BaseMap", source.GetTextureOffset(baseProperty));
            }
            else
            {
                owned.SetTextureScale("_BaseMap", Vector2.one);
                owned.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            // Keep the authored corridor tint. Supplier colour refresh must not brighten the approved world look.

            string normalProperty = FirstTextureProperty(source, "_BumpMap", "_NormalMap");
            Texture normal = normalProperty == null ? null : source.GetTexture(normalProperty);
            owned.SetTexture("_BumpMap", normal);
            owned.SetTextureScale("_BumpMap", normalProperty == null ? Vector2.one : source.GetTextureScale(normalProperty));
            owned.SetTextureOffset("_BumpMap", normalProperty == null ? Vector2.zero : source.GetTextureOffset(normalProperty));
            float normalScale = source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale")
                : source.HasProperty("_NormalScale") ? source.GetFloat("_NormalScale") : 1f;
            owned.SetFloat("_BumpScale", normalScale);

            bool alphaClip = UsesAlphaClip(source);
            owned.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            float cutoff = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff")
                : source.HasProperty("_AlphaCutoff") ? source.GetFloat("_AlphaCutoff") : .5f;
            owned.SetFloat("_Cutoff", cutoff);
        }

        private static string FirstTextureProperty(Material material, params string[] properties)
        {
            return properties.FirstOrDefault(property => material.HasProperty(property) && material.GetTexture(property) != null);
        }

        private static CullMode ReadSourceCull(Material source)
        {
            foreach (string property in new[] { "_Cull", "_CullMode", "_CullModeForward" })
            {
                if (!source.HasProperty(property)) continue;
                int value = Mathf.RoundToInt(source.GetFloat(property));
                if (value >= (int)CullMode.Off && value <= (int)CullMode.Back) return (CullMode)value;
            }
            return CullMode.Back;
        }

        private static bool IsThinCutout(Material source)
        {
            if (!UsesAlphaClip(source)) return false;
            string identity = (source.name + " " + AssetDatabase.GetAssetPath(source)).ToLowerInvariant();
            return ThinCutoutTokens.Any(token => identity.Contains(token));
        }

        private static bool UsesAlphaClip(Material source)
        {
            if (source.IsKeywordEnabled("_ALPHATEST_ON") || source.IsKeywordEnabled("_ALPHACLIP_ON")) return true;
            if (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > .5f) return true;
            if (source.HasProperty("_AlphaTest") && source.GetFloat("_AlphaTest") > .5f) return true;
            if (source.HasProperty("_Mode") && Mathf.RoundToInt(source.GetFloat("_Mode")) == 1) return true;
            return string.Equals(source.GetTag("RenderType", false, string.Empty), "TransparentCutout", StringComparison.OrdinalIgnoreCase);
        }
    }
}
