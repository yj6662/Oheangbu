using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        const string CompactTerrainShader = "Oheangbu/WorldMacroTerrain";
        const int CompactMaskWidth = 2048, CompactMaskHeight = 3072;
        const float CompactMainPathWidth = 4.1f, CompactPathFeather = 4f;
        [Serializable] sealed class MaterialInputHash { public string path, before, after; }
        [Serializable] sealed class CompactMaterialRecord
        {
            public string source, target, sourceHash, targetHash, shader;
            public Vector4 oldRect, newRect;
            public float oldWashStart, oldWashEnd, newWashStart, newWashEnd;
            public string realmSource, realmTarget;
            public int realmWidth, realmHeight;
            public bool otherPropertiesPreserved;
        }
        [Serializable] sealed class CompactMaterialsReport
        {
            public string status, scene, generation, mappingHash, roadMask, roadMaskHash;
            public string scope = "Compact-owned terrain/context material copies only; inverse-mapped realm pigment and full-width R8 road pigment. No source material/texture or global exposure changes; no visual approval implied.";
            public int materials, rendererSlots, reboundSlots, routes, routeSegments, mainPathSegments, paintSamples, paintedPixels;
            public float commitRatio;
            public Vector4 targetRect;
            public bool originalInputsUnchanged;
            public CompactMaterialRecord[] records;
            public MaterialInputHash[] inputs;
        }
        static string MaterialsReportPath => Path.Combine(Output, "materials_report.json");
        static bool CompactTerrainMaterial(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != CompactTerrainShader) return false;
            string path = AssetDatabase.GetAssetPath(material);
            return path.StartsWith("Assets/_Project/", StringComparison.Ordinal) && !path.StartsWith(Folder + "/", StringComparison.Ordinal);
        }
        static void MaterialMemoryGuard()
        {
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; compact material generation paused before further texture work.");
        }
        static string PersistMaterials(CompactMaterialsReport report)
        {
            Directory.CreateDirectory(Output);
            string json = JsonUtility.ToJson(report, true), temporary = MaterialsReportPath + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(MaterialsReportPath)) File.Replace(temporary, MaterialsReportPath, null);
            else File.Move(temporary, MaterialsReportPath);
            return json;
        }
        static void MaterialAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            MaterialAssetFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static void RequireMaterialOutput(string path)
        {
            if (!path.StartsWith(Folder + "/Materials/", StringComparison.Ordinal) && !path.StartsWith(Folder + "/Textures/", StringComparison.Ordinal))
                throw new InvalidOperationException("Material output must be in the compact Materials/Textures folders.");
        }

        static string Materials()
        {
            RequireCompact(); MaterialMemoryGuard();
            var scene = SceneManager.GetActiveScene();
            var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            var sessions = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
            if (sessions.Length != 1 || sessions[0].Content == null || !AssetDatabase.GetAssetPath(sessions[0].Content).StartsWith(Folder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Compact scene content must be rebound before material generation.");
            var progress = ReadProgress();
            var geographies = progress.assets.Select(a => AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(a.target)).Where(a => a != null && a.Compression == Compression).ToArray();
            if (geographies.Length != 1) throw new InvalidOperationException("Expected one remapped compact geography sheet.");
            var geo = geographies[0]; var target = Compression.Mapping.TargetBounds;
            if ((geo.BoundsMin - new Vector2(target.min.x, target.min.z)).sqrMagnitude > .001f ||
                (geo.BoundsMax - new Vector2(target.max.x, target.max.z)).sqrMagnitude > .001f)
                throw new InvalidOperationException("Compact geography bounds do not match the compression map.");
            var targetRect = new Vector4(target.min.x, target.min.z, 1f / target.size.x, 1f / target.size.z);
            CompactMaterialsReport report;
            if (File.Exists(MaterialsReportPath))
            {
                report = JsonUtility.FromJson<CompactMaterialsReport>(File.ReadAllText(MaterialsReportPath));
                if (report == null || report.records == null || report.inputs == null || report.scene != TargetScene || report.mappingHash != Hash(Folder + "/Compression.asset"))
                    throw new InvalidOperationException("Material report belongs to a different or incomplete compact generation.");
                if (report.inputs.Any(i => !File.Exists(i.path) || Hash(i.path) != i.before)) throw new InvalidOperationException("An original material/texture input changed after the generation snapshot.");
            }
            else
            {
                var originals = renderers.SelectMany(r => r.sharedMaterials).Where(CompactTerrainMaterial).Distinct().OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
                if (originals.Length == 0) throw new InvalidOperationException("No owned macro-terrain materials found on compact scene renderers.");
                report = new CompactMaterialsReport { status = "PREPARING", scene = TargetScene, generation = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"), mappingHash = Hash(Folder + "/Compression.asset"), targetRect = targetRect };
                var inputPaths = new HashSet<string>(StringComparer.Ordinal) { SourceScene };
                var records = new List<CompactMaterialRecord>();
                foreach (var source in originals)
                {
                    if (!source.HasProperty("_GroundPathRect") || !source.HasProperty("_GroundPathMask") || !source.HasProperty("_RealmPigment")) throw new InvalidOperationException("Macro terrain shader mapping properties are missing.");
                    string sourcePath = AssetDatabase.GetAssetPath(source), guid = AssetDatabase.AssetPathToGUID(sourcePath);
                    var realm = source.GetTexture("_RealmPigment") as Texture2D;
                    if (realm == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(realm))) throw new InvalidOperationException("Expected an authored RealmPigment texture on " + sourcePath);
                    Vector4 oldRect = source.GetVector("_GroundPathRect");
                    if (oldRect.z <= 0 || oldRect.w <= 0) throw new InvalidOperationException("Original material has invalid world-mask dimensions: " + sourcePath);
                    inputPaths.Add(sourcePath); inputPaths.Add(AssetDatabase.GetAssetPath(realm));
                    string oldRoad = AssetDatabase.GetAssetPath(source.GetTexture("_GroundPathMask")); if (!string.IsNullOrEmpty(oldRoad)) inputPaths.Add(oldRoad);
                    records.Add(new CompactMaterialRecord { source = sourcePath, sourceHash = Hash(sourcePath), shader = source.shader.name,
                        target = Folder + "/Materials/" + report.generation + "/" + guid + ".mat", oldRect = oldRect, newRect = targetRect,
                        oldWashStart = source.GetFloat("_WashStart"), oldWashEnd = source.GetFloat("_WashEnd"),
                        newWashStart = source.GetFloat("_WashStart") * .5f, newWashEnd = source.GetFloat("_WashEnd") * .5f,
                        realmSource = AssetDatabase.GetAssetPath(realm), realmTarget = Folder + "/Textures/" + report.generation + "/" + guid + "_RealmPigment.asset",
                        realmWidth = realm.width, realmHeight = realm.height });
                }
                report.records = records.ToArray(); report.inputs = inputPaths.OrderBy(s => s, StringComparer.Ordinal).Select(path => new MaterialInputHash { path = path, before = Hash(path) }).ToArray();
                report.roadMask = Folder + "/Textures/" + report.generation + "/RoadPigment.asset";
                PersistMaterials(report);
            }
            MaterialMemoryGuard(); report.commitRatio = Prologue.PrologueAudit.CommitRatio();
            report.routeSegments = report.mainPathSegments = report.paintSamples = report.paintedPixels = 0;
            var roadMask = SaveCompactTexture(BuildCompactRoadPigment(geo, sessions[0].Content, report), report.roadMask);
            var replacements = new Dictionary<Material, Material>();
            var realmCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (var record in report.records)
            {
                MaterialMemoryGuard(); RequireMaterialOutput(record.target); RequireMaterialOutput(record.realmTarget);
                var source = AssetDatabase.LoadAssetAtPath<Material>(record.source);
                if (!CompactTerrainMaterial(source)) throw new InvalidOperationException("Original material no longer uses the owned macro terrain shader: " + record.source);
                string key = record.realmSource + "|" + record.oldRect.ToString("R", CultureInfo.InvariantCulture);
                if (!realmCache.TryGetValue(key, out var pigment))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(record.realmSource);
                    pigment = SaveCompactTexture(WarpCompactRealmPigment(texture, record.oldRect, targetRect), record.realmTarget);
                    realmCache.Add(key, pigment);
                }
                record.realmTarget = AssetDatabase.GetAssetPath(pigment);
                var clone = AssetDatabase.LoadAssetAtPath<Material>(record.target);
                bool fresh = clone == null;
                if (fresh) clone = new Material(source); else EditorUtility.CopySerialized(source, clone);
                clone.name = source.name + "_Compact";
                clone.SetVector("_GroundPathRect", targetRect); clone.SetTexture("_GroundPathMask", roadMask); clone.SetTexture("_RealmPigment", pigment);
                clone.SetFloat("_WashStart", record.newWashStart); clone.SetFloat("_WashEnd", record.newWashEnd);
                record.otherPropertiesPreserved = MaterialOtherPropertiesMatch(source, clone);
                if (!record.otherPropertiesPreserved) { if (fresh) Object.DestroyImmediate(clone); throw new InvalidOperationException("A nonmapping terrain property changed on " + record.source); }
                if (fresh) { MaterialAssetFolder(Path.GetDirectoryName(record.target).Replace('\\', '/')); AssetDatabase.CreateAsset(clone, record.target); }
                EditorUtility.SetDirty(clone); AssetDatabase.SaveAssetIfDirty(clone); replacements.Add(source, clone); record.targetHash = Hash(record.target);
            }
            report.rendererSlots = 0;report.reboundSlots=0;
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials; bool changed = false;
                for (int i = 0; i < materials.Length; i++) if (materials[i] != null && replacements.TryGetValue(materials[i], out var replacement))
                { materials[i] = replacement; report.reboundSlots++; changed = true; }
                if (changed) { renderer.sharedMaterials = materials; EditorUtility.SetDirty(renderer); }
            }
            var boundMaterials=new HashSet<Material>(replacements.Values);
            report.rendererSlots=renderers.Sum(r=>r.sharedMaterials.Count(boundMaterials.Contains));
            foreach (var input in report.inputs) input.after = Hash(input.path);
            report.originalInputsUnchanged = report.inputs.All(i => i.before == i.after);
            if (!report.originalInputsUnchanged) { report.status = "FINDINGS_SOURCE_HASH_CHANGED"; PersistMaterials(report); throw new InvalidOperationException("An original material/texture/source-scene hash changed during compact material generation."); }
            report.materials = report.records.Length; report.roadMaskHash = Hash(report.roadMask); report.status = "MATERIALS_COMPLETE";
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save compact material bindings.");
            return PersistMaterials(report);
        }

        static Texture2D SaveCompactTexture(Texture2D texture, string path)
        {
            RequireMaterialOutput(path); MaterialAssetFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (saved == null) { AssetDatabase.CreateAsset(texture, path); saved = texture; }
            else { EditorUtility.CopySerialized(texture, saved); Object.DestroyImmediate(texture); EditorUtility.SetDirty(saved); }
            AssetDatabase.SaveAssetIfDirty(saved); return saved;
        }
        static Texture2D BuildCompactRoadPigment(WorldMacroSheetSO geo, WorldMacroPlaytestSO content, CompactMaterialsReport report)
        {
            int width = CompactMaskWidth, height = CompactMaskHeight; var pixels = new byte[width * height];
            float dx = (geo.BoundsMax.x - geo.BoundsMin.x) / (width - 1), dz = (geo.BoundsMax.y - geo.BoundsMin.y) / (height - 1);
            void Paint(Vector3[] points, float physicalWidth, bool main)
            {
                if (points == null || points.Length < 2 || physicalWidth <= 0) return;
                float half = physicalWidth * .5f, reach = half + CompactPathFeather;
                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 a = points[i - 1], b = points[i];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) / Mathf.Min(dx, dz)));
                    if (main) report.mainPathSegments++; else report.routeSegments++;
                    for (int j = 0; j <= steps; j++)
                    {
                        Vector3 point = Vector3.Lerp(a, b, j / (float)steps); report.paintSamples++;
                        int minX = Mathf.Max(0, Mathf.FloorToInt((point.x - reach - geo.BoundsMin.x) / dx)), maxX = Mathf.Min(width - 1, Mathf.CeilToInt((point.x + reach - geo.BoundsMin.x) / dx));
                        int minZ = Mathf.Max(0, Mathf.FloorToInt((point.z - reach - geo.BoundsMin.y) / dz)), maxZ = Mathf.Min(height - 1, Mathf.CeilToInt((point.z + reach - geo.BoundsMin.y) / dz));
                        for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
                        {
                            float offsetX = geo.BoundsMin.x + x * dx - point.x, offsetZ = geo.BoundsMin.y + z * dz - point.z;
                            float distance = Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
                            byte value = (byte)(255f * (1f - Mathf.SmoothStep(0, 1, (distance - half) / CompactPathFeather)));
                            int index = z * width + x; if (value > pixels[index]) pixels[index] = value;
                        }
                    }
                }
            }
            report.routes = geo.Routes?.Length ?? 0;
            foreach (var route in geo.Routes ?? Array.Empty<WorldMacroSheetSO.RouteSpec>()) { MaterialMemoryGuard(); Paint(route.Points, route.Width, false); }
            Paint(content.MainPath, CompactMainPathWidth, true);
            report.paintedPixels = pixels.Count(value => value > 0);
            var texture = new Texture2D(width, height, TextureFormat.R8, false, true) { name = "Compact full-width road pigment", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixelData(pixels, 0); texture.Apply(false, false); return texture;
        }
        static Texture2D WarpCompactRealmPigment(Texture2D source, Vector4 oldRect, Vector4 targetRect)
        {
            if (source == null) throw new InvalidOperationException("Source realm pigment texture is missing.");
            Texture2D readable = source, temporary = null;
            if (!source.isReadable)
            {
                // Read back through a temporary linear target; never alter the source importer/readability.
                var previous = RenderTexture.active;
                var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                try { Graphics.Blit(source, target); RenderTexture.active = target; temporary = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true); temporary.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); temporary.Apply(false, false); readable = temporary; }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
            }
            try
            {
                int width = source.width, height = source.height; var pixels = new Color[width * height];
                var u = new float[width]; var v = new float[height]; var mapping = Compression.Mapping;
                for (int x = 0; x < width; x++) u[x] = ((float)mapping.XAxis.Inverse(targetRect.x + x / (float)(width - 1) / targetRect.z) - oldRect.x) * oldRect.z;
                for (int z = 0; z < height; z++) v[z] = ((float)mapping.ZAxis.Inverse(targetRect.y + z / (float)(height - 1) / targetRect.w) - oldRect.y) * oldRect.w;
                for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) pixels[z * width + x] = readable.GetPixelBilinear(Mathf.Clamp01(u[x]), Mathf.Clamp01(v[z]));
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = source.name + "_Compact", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                result.SetPixels(pixels); result.Apply(false, false); return result;
            }
            finally { if (temporary != null) Object.DestroyImmediate(temporary); }
        }
        static bool MaterialOtherPropertiesMatch(Material source, Material clone)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "_GroundPathRect", "_GroundPathMask", "_RealmPigment", "_WashStart", "_WashEnd" };
            for (int i = 0; i < source.shader.GetPropertyCount(); i++)
            {
                string property = source.shader.GetPropertyName(i); if (allowed.Contains(property)) continue;
                switch (source.shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: if (source.GetColor(property) != clone.GetColor(property)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: if (source.GetVector(property) != clone.GetVector(property)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: if (source.GetFloat(property) != clone.GetFloat(property)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int: if (source.GetInteger(property) != clone.GetInteger(property)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        if (source.GetTexture(property) != clone.GetTexture(property) || source.GetTextureOffset(property) != clone.GetTextureOffset(property) || source.GetTextureScale(property) != clone.GetTextureScale(property)) return false;
                        break;
                }
            }
            return source.renderQueue == clone.renderQueue && source.enableInstancing == clone.enableInstancing && source.doubleSidedGI == clone.doubleSidedGI && source.globalIlluminationFlags == clone.globalIlluminationFlags && source.shaderKeywords.OrderBy(s => s).SequenceEqual(clone.shaderKeywords.OrderBy(s => s));
        }
    }
}
