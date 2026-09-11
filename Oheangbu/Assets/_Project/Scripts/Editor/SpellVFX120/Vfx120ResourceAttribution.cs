using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Call once AFTER CaptureEnd/resource and CPU counters have been recorded.
    // This allocates managed report data. It is not a runtime performance sample.
    public static class Vfx120ResourceAttribution
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Serializable] public sealed class Resource
        {
            public int id, width, height;
            public string name, type, hideFlags, assetPath;
            public bool persistent;
        }
        [Serializable] public sealed class MaterialEvidence
        {
            public int requestedId;
            public string status, selection, shader, attribution = "UNATTRIBUTED", limitation;
            public Resource material, mainTexture;
            public List<FontEvidence> fontReferences = new List<FontEvidence>();
            public List<CacheMatch> cacheReferences = new List<CacheMatch>();
            public List<Consumer> consumers = new List<Consumer>();
        }
        [Serializable] public sealed class FontEvidence
        {
            public Resource font, sourceFont, editorSourceFont, material;
            public string family, style, populationMode, isEditorFont, internalDynamicOS;
            public bool directMaterialReference;
            public List<int> matchingAtlasIndices = new List<int>();
            public List<Resource> atlases = new List<Resource>();
            public List<string> unavailableFields = new List<string>();
        }
        [Serializable] public sealed class CacheEvidence
        {
            public string type, field, assembly, status, initializationEvidence, reason;
            public int entries, matchingEntries;
        }
        [Serializable] public sealed class CacheMatch
        {
            public string cacheType, field, key, referencedField;
            public int materialId, sourceMaterialId, textureId, referenceCount = -1;
        }
        [Serializable] public sealed class Consumer
        {
            public Resource component;
            public string scene, hierarchy, reference;
            public bool activeInHierarchy;
        }
        [Serializable] public sealed class Report
        {
            public string status = "UNVERIFIED", capturedUtc, unityVersion, output, selectionMode;
            public string scope = "Post-measurement, read-only ownership snapshot. Explicit IDs are looked up among already loaded materials. " +
                "Without IDs, nonpersistent materials whose name contains Atlas AND whose shader looks like a font shader are candidates only. " +
                "Names do not prove ownership or justify excluding resource growth. No leak/art/performance PASS is produced.";
            public string limitations = "Only already loaded assemblies/resources are inspected. Font asset data uses fields, never dynamic font/material/atlas getters. " +
                "Renderer.sharedMaterials and existing TMP component fields identify some consumers, not all native/UI Toolkit owners. " +
                "An empty result is not proof of no reference. Static cache reads require an existing callback or a side-effect-free initializer check. " +
                "A dictionary-only initializer may create empty managed cache storage, but cannot create the matching Material. " +
                "No resource factory, font request, cache cleanup, GC or UnloadUnusedAssets is called. Run only after timing/resource counters stop.";
            public bool isPlaying, resourceSetUnchangedByInspection, leakPass = false;
            public int frame, loadedMaterialCount, textCoreFontCount, tmpFontCount, rendererCount, tmpComponentCount;
            public double elapsedMilliseconds;
            public List<MaterialEvidence> materials = new List<MaterialEvidence>();
            public List<CacheEvidence> caches = new List<CacheEvidence>();
            public List<Resource> resourcesAppearedDuringInspection = new List<Resource>();
            public List<int> resourceIdsDisappearedDuringInspection = new List<int>();
            public List<string> errors = new List<string>();
        }

        public static string Inspect(int[] materialIds = null)
        {
            var timer = Stopwatch.StartNew();
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                isPlaying = Application.isPlaying, frame = Time.frameCount,
                output = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "Art/SpellVFX120/resource_attribution.json"),
                selectionMode = materialIds != null && materialIds.Length > 0 ? "EXPLICIT_IDS" : "NAME_AND_SHADER_CANDIDATES_ONLY" };
            var before = Snapshot();
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Inspect only after compilation/import and after measured counters have stopped");
                var loaded = Resources.FindObjectsOfTypeAll<Material>();
                report.loadedMaterialCount = loaded.Length;
                var materials = new Dictionary<int, Material>();
                foreach (var material in loaded) if (material != null) materials[material.GetInstanceID()] = material;
                var ids = new HashSet<int>();
                if (materialIds != null && materialIds.Length > 0) foreach (int id in materialIds) ids.Add(id);
                else foreach (var material in loaded)
                    if (material != null && !EditorUtility.IsPersistent(material) && Contains(material.name, "Atlas")
                        && IsFontShader(material.shader != null ? material.shader.name : null)) ids.Add(material.GetInstanceID());
                foreach (int id in ids)
                {
                    var row = new MaterialEvidence { requestedId = id, selection = report.selectionMode };
                    report.materials.Add(row);
                    if (!materials.TryGetValue(id, out var material)) { row.status = "NOT_LOADED_AT_INSPECTION"; continue; }
                    row.material = Describe(material); row.shader = material.shader != null ? material.shader.name : null;
                    row.mainTexture = Describe(material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null);
                    row.status = "READ";
                }
                InspectFonts("UnityEngine.TextCore.Text.FontAsset", report, false);
                InspectFonts("TMPro.TMP_FontAsset", report, true);
                InspectCache("UnityEngine.TextCore.Text.MaterialManager", "s_FallbackMaterials", report);
                InspectCache("TMPro.TMP_MaterialManager", "m_fallbackMaterials", report);
                InspectConsumers(report);
                foreach (var row in report.materials)
                {
                    if (row.material == null) continue;
                    bool editorAtlas = false, atlas = false, direct = false, cache = false;
                    foreach (var font in row.fontReferences)
                    {
                        direct |= font.directMaterialReference;
                        atlas |= font.matchingAtlasIndices.Count > 0;
                        editorAtlas |= font.matchingAtlasIndices.Count > 0 && font.isEditorFont == "True";
                    }
                    foreach (var match in row.cacheReferences) cache |= match.referencedField == "fallbackMaterial";
                    row.attribution = cache && editorAtlas ? "EDITOR_FONT_ATLAS_AND_FALLBACK_CACHE_ID_MATCH"
                        : cache && atlas ? "FONT_ATLAS_AND_FALLBACK_CACHE_ID_MATCH"
                        : cache ? "FALLBACK_CACHE_MATERIAL_ID_MATCH"
                        : direct ? "FONT_ASSET_DIRECT_MATERIAL_ID_MATCH"
                        : atlas ? "FONT_ATLAS_TEXTURE_ID_MATCH_ONLY"
                        : row.consumers.Count > 0 ? "CONSUMER_REFERENCE_ONLY" : "UNATTRIBUTED";
                    row.limitation = "Ownership describes current references, not allocation trigger, expected cache growth, cleanup policy or leak absence.";
                }
                report.status = report.errors.Count == 0 ? "READ_ONLY_SNAPSHOT_NO_LEAK_VERDICT" : "PARTIAL_READ_ONLY_SNAPSHOT";
            }
            catch (Exception error) { report.status = "PARTIAL_OR_UNVERIFIED"; report.errors.Add(error.ToString()); }
            var after = Snapshot();
            foreach (var pair in after) if (!before.ContainsKey(pair.Key)) report.resourcesAppearedDuringInspection.Add(Describe(pair.Value));
            foreach (int id in before.Keys) if (!after.ContainsKey(id)) report.resourceIdsDisappearedDuringInspection.Add(id);
            report.resourceSetUnchangedByInspection = report.resourcesAppearedDuringInspection.Count == 0 && report.resourceIdsDisappearedDuringInspection.Count == 0;
            if (!report.resourceSetUnchangedByInspection) report.status = "RESOURCE_SET_CHANGED_DURING_INSPECTION_ATTRIBUTION_UNVERIFIED";
            report.elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(report.output)); File.WriteAllText(report.output, json);
            return json;
        }

        static void InspectFonts(string name, Report report, bool tmp)
        {
            var type = LoadedType(name);
            if (type == null) return;
            var fonts = Resources.FindObjectsOfTypeAll(type);
            if (tmp) report.tmpFontCount = fonts.Length; else report.textCoreFontCount = fonts.Length;
            foreach (var font in fonts)
            {
                if (font == null) continue;
                try
                {
                    var data = new FontEvidence { font = Describe(font) };
                    var material = Read(font, "m_Material", data.unavailableFields) as Material;
                    data.material = Describe(material);
                    var atlasArray = Read(font, "m_AtlasTextures", data.unavailableFields) as Array;
                    if (atlasArray != null) foreach (var value in atlasArray) data.atlases.Add(Describe(value as Object));
                    data.sourceFont = Describe(Read(font, "m_SourceFontFile", data.unavailableFields) as Object);
                    data.editorSourceFont = Describe(Read(font, "m_SourceFontFile_EditorRef", data.unavailableFields) as Object);
                    data.populationMode = Scalar(Read(font, "m_AtlasPopulationMode", data.unavailableFields));
                    data.isEditorFont = Scalar(Read(font, "IsEditorFont", data.unavailableFields));
                    data.internalDynamicOS = Scalar(Read(font, "InternalDynamicOS", data.unavailableFields));
                    var face = Read(font, "m_FaceInfo", data.unavailableFields);
                    data.family = Scalar(Read(face, "m_FamilyName", data.unavailableFields));
                    data.style = Scalar(Read(face, "m_StyleName", data.unavailableFields));
                    foreach (var row in report.materials)
                    {
                        if (row.material == null) continue;
                        bool direct = material != null && material.GetInstanceID() == row.requestedId;
                        var indices = new List<int>();
                        for (int i = 0; i < data.atlases.Count; i++)
                            if (data.atlases[i] != null && row.mainTexture != null && data.atlases[i].id == row.mainTexture.id) indices.Add(i);
                        if (!direct && indices.Count == 0) continue;
                        row.fontReferences.Add(new FontEvidence { font = data.font, sourceFont = data.sourceFont,
                            editorSourceFont = data.editorSourceFont, material = data.material, family = data.family, style = data.style,
                            populationMode = data.populationMode, isEditorFont = data.isEditorFont, internalDynamicOS = data.internalDynamicOS,
                            directMaterialReference = direct, matchingAtlasIndices = indices, atlases = data.atlases, unavailableFields = data.unavailableFields });
                    }
                }
                catch (Exception error) { report.errors.Add(name + " font ID " + font.GetInstanceID() + ": " + error.Message); }
            }
        }

        static void InspectCache(string name, string fieldName, Report report)
        {
            var cache = new CacheEvidence { type = name, field = fieldName, status = "UNVERIFIED" }; report.caches.Add(cache);
            var type = LoadedType(name);
            if (type == null) { cache.reason = "Type is not in an already loaded assembly"; return; }
            cache.assembly = type.Assembly.FullName;
            var field = type.GetField(fieldName, StaticFields);
            if (field == null) { cache.reason = "Expected static field unavailable in this Unity/package version"; return; }
            try
            {
                if (name == "TMPro.TMP_MaterialManager")
                {
                    if (!HasExistingCanvasCallback(type))
                    { cache.reason = "No existing OnPreRender Canvas callback proves TMP cache initialization; static read skipped to avoid subscribing it"; return; }
                    cache.initializationEvidence = "EXISTING_CANVAS_ONPRERENDER_CALLBACK";
                }
                else
                {
                    if (!DictionaryOnlyInitializer(type, out var reason))
                    { cache.reason = "Static initialization safety not established; read skipped: " + reason; return; }
                    cache.initializationEvidence = "INITIALIZER_ONLY_EMPTY_MANAGED_DICTIONARIES_OR_NO_INITIALIZER";
                }
                if (!(field.GetValue(null) is IDictionary dictionary))
                { cache.reason = "Existing field is null or does not implement IDictionary"; return; }
                cache.entries = dictionary.Count;
                foreach (DictionaryEntry entry in dictionary)
                {
                    var value = entry.Value;
                    var fallback = value as Material ?? Read(value, "fallbackMaterial") as Material;
                    var source = Read(value, "sourceMaterial") as Material ?? Read(value, "baseMaterial") as Material;
                    long key = entry.Key is long longKey ? longKey : 0;
                    int sourceId = source != null ? source.GetInstanceID() : unchecked((int)(key >> 32));
                    int textureId = unchecked((int)(uint)key);
                    int count = Read(value, "count") is int references ? references : -1;
                    foreach (var row in report.materials)
                    {
                        if (row.material == null) continue;
                        bool fallbackMatch = fallback != null && fallback.GetInstanceID() == row.requestedId;
                        bool sourceMatch = sourceId == row.requestedId;
                        if (!fallbackMatch && !sourceMatch) continue;
                        cache.matchingEntries++;
                        row.cacheReferences.Add(new CacheMatch { cacheType = name, field = fieldName,
                            key = Scalar(entry.Key), referencedField = fallbackMatch ? "fallbackMaterial" : "sourceMaterial",
                            materialId = fallback != null ? fallback.GetInstanceID() : 0, sourceMaterialId = sourceId,
                            textureId = textureId, referenceCount = count });
                    }
                }
                cache.status = "READ_EXISTING_REFERENCES";
            }
            catch (Exception error) { cache.reason = error.ToString(); report.errors.Add(name + ": " + error.Message); }
        }

        static bool HasExistingCanvasCallback(Type owner)
        {
            // Reading an event of an uninitialized type could itself initialize that type.
            if (typeof(Canvas).TypeInitializer != null) return false;
            var field = typeof(Canvas).GetField("willRenderCanvases", StaticFields);
            if (field == null || !(field.GetValue(null) is Delegate callbacks)) return false;
            foreach (var callback in callbacks.GetInvocationList())
                if (callback.Method.DeclaringType == owner && callback.Method.Name == "OnPreRender") return true;
            return false;
        }

        static bool DictionaryOnlyInitializer(Type type, out string reason)
        {
            // Do not execute the initializer. Inspect its IL. A first static read may initialize
            // empty managed dictionaries; such an initializer cannot allocate a Unity resource,
            // subscribe callbacks or fabricate a match to an already loaded Material ID.
            reason = null; var initializer = type.TypeInitializer;
            if (initializer == null) return true;
            var bytes = initializer.GetMethodBody()?.GetILAsByteArray();
            if (bytes == null) { reason = "Initializer IL unavailable"; return false; }
            for (int i = 0; i < bytes.Length;)
            {
                byte op = bytes[i++];
                if (op == 0x00 || op == 0x2a) continue; // nop, ret
                if ((op != 0x73 && op != 0x80) || i + 4 > bytes.Length)
                { reason = "Initializer contains an operation beyond empty Dictionary construction/static assignment"; return false; }
                int token = BitConverter.ToInt32(bytes, i); i += 4;
                if (op == 0x73)
                {
                    var constructor = initializer.Module.ResolveMethod(token) as ConstructorInfo;
                    var owner = constructor?.DeclaringType;
                    if (constructor == null || constructor.GetParameters().Length != 0 || owner == null || !owner.IsGenericType
                        || owner.GetGenericTypeDefinition() != typeof(Dictionary<,>))
                    { reason = "Initializer constructs a type other than an empty managed Dictionary"; return false; }
                }
                else if (initializer.Module.ResolveField(token).DeclaringType != type)
                { reason = "Initializer writes another type's static field"; return false; }
            }
            return true;
        }

        static void InspectConsumers(Report report)
        {
            var renderers = Resources.FindObjectsOfTypeAll<Renderer>(); report.rendererCount = renderers.Length;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                try
                {
                    var shared = renderer.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++) AddConsumer(renderer, shared[i], "Renderer.sharedMaterials[" + i + "]", report);
                }
                catch (Exception error) { report.errors.Add("Renderer " + renderer.GetInstanceID() + ": " + error.Message); }
            }
            foreach (string name in new[] { "TMPro.TMP_Text", "TMPro.TMP_SubMesh", "TMPro.TMP_SubMeshUI" })
            {
                var type = LoadedType(name); if (type == null) continue;
                foreach (var value in Resources.FindObjectsOfTypeAll(type))
                {
                    if (!(value is Component component)) continue; report.tmpComponentCount++;
                    foreach (string field in new[] { "m_sharedMaterial", "m_sharedMaterials", "m_fontSharedMaterial", "m_fontSharedMaterials", "m_fallbackMaterial" })
                    {
                        var material = Read(component, field);
                        if (material is Material single) AddConsumer(component, single, "existing field " + field, report);
                        else if (material is Material[] array)
                            for (int i = 0; i < array.Length; i++) AddConsumer(component, array[i], "existing field " + field + "[" + i + "]", report);
                    }
                }
            }
        }
        static void AddConsumer(Component component, Material material, string reference, Report report)
        {
            if (material == null) return;
            foreach (var row in report.materials)
                if (row.requestedId == material.GetInstanceID())
                    row.consumers.Add(new Consumer { component = Describe(component), reference = reference,
                        scene = component.gameObject.scene.path, hierarchy = Hierarchy(component.transform), activeInHierarchy = component.gameObject.activeInHierarchy });
        }
        static string Hierarchy(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }
        static object Read(object instance, string name, List<string> unavailable = null)
        {
            if (instance == null) { unavailable?.Add(name + " (owner unavailable)"); return null; }
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceFields);
                if (field != null) return field.GetValue(instance);
            }
            unavailable?.Add(name); return null;
        }
        static Type LoadedType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            { var type = assembly.GetType(name, false); if (type != null) return type; }
            return null;
        }
        static string Scalar(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            return type.IsPrimitive || type.IsEnum || value is string || value is decimal ? value.ToString() : null;
        }
        static bool Contains(string value, string fragment) => value != null && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        static bool IsFontShader(string name) => Contains(name, "TextMeshPro") || Contains(name, "TextCore") || Contains(name, "Distance Field")
            || Contains(name, "GUI/Text") || Contains(name, "GUIFont");
        static Resource Describe(Object value)
        {
            if (value == null) return null;
            var result = new Resource { id = value.GetInstanceID(), name = value.name, type = value.GetType().FullName,
                hideFlags = value.hideFlags.ToString(), persistent = EditorUtility.IsPersistent(value), assetPath = AssetDatabase.GetAssetPath(value) };
            if (value is Texture texture) { result.width = texture.width; result.height = texture.height; }
            return result;
        }
        static Dictionary<int, Object> Snapshot()
        {
            var result = new Dictionary<int, Object>();
            foreach (var type in new[] { typeof(Material), typeof(Texture), typeof(Mesh), typeof(Font) })
                foreach (var value in Resources.FindObjectsOfTypeAll(type)) if (value != null) result[value.GetInstanceID()] = value;
            foreach (string name in new[] { "UnityEngine.TextCore.Text.FontAsset", "TMPro.TMP_FontAsset" })
            {
                var type = LoadedType(name); if (type == null) continue;
                foreach (var value in Resources.FindObjectsOfTypeAll(type)) if (value != null) result[value.GetInstanceID()] = value;
            }
            return result;
        }
    }
}
