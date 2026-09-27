using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World.Dressing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Rasterize explicit world-space art anchors; bind only three properties of the existing 205 study materials.</summary>
    public static class InkPaintingFormMapBaker
    {
        const string Folder = "Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision";
        const string TextureProperty = "_PaintedFormMap", RectProperty = "_PaintedFormMapRect", EnabledProperty = "_PaintedFormAuthored";
        const float ExclusionFeather = 12;
        [Serializable] public sealed class AnchorRow { public string id, owner; public int footprintPixels, eligiblePixels; public float maximumAlpha; }
        [Serializable] public sealed class BindingRow
        {
            public string material, oldTexture; public long oldTextureLocalId; public Vector4 oldRect; public float oldEnabled;
            public bool initiallyDirty;
        }
        [Serializable] public sealed class Receipt
        {
            public string status, utc, generation, jsonPath, jsonSha256, fieldPath, receiptPath, scene, error;
            public int width, height, groundMaterials, foliageMaterials, roadLines, roadSegments, protectedRectangles, riverSegments;
            public int alphaPixels, exclusionRemovedPixels, rgbPaddingPixels, unresolvedOwnerPixels;
            public int savedInitiallyDirtyMaterials; public bool otherMaterialPropertiesUnchanged;
            public float alphaMaximum, alphaSum, elapsedMilliseconds; public Vector4 worldRect;
            public bool bound, rollbackCompleted, sceneDirtyBefore, sceneDirtyAfter;
            public string[] rollbackErrors, notes; public AnchorRow[] anchors; public BindingRow[] bindings;
        }
        sealed class Anchor
        {
            public string id, owner; public Vector2[] spine; public float[] widths; public Color rgba; public float feather;
            public Vector2[] curve; public float[] radii; public Rect bounds; public int bit; public AnchorRow row;
        }
        sealed class Binding { public Material material; public Texture oldTexture; public Vector4 oldRect; public float oldEnabled; public BindingRow row; public string liveJson, otherPayload; }
        static List<Binding> lastBindings;
        static Texture2D lastTexture;
        static Receipt lastBindingReceipt;
        public static Receipt LastReceipt { get; private set; }
        static string Workspace => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string Resolve(string p) => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(Workspace, p));
        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static float Number(JToken t) { if (t == null) throw new ArgumentException("Missing numeric anchor value"); float v = t.Value<float>(); if (!Finite(v)) throw new ArgumentException("Nonfinite anchor value"); return v; }
        static float[] Numbers(JToken t, int n) { var a = (t as JArray)?.Select(Number).ToArray(); if (a == null || a.Length != n) throw new ArgumentException("Wrong anchor array length"); return a; }

        public static string BakeAndBind(string jsonPath)
        {
            Guard();
            var scene = SceneManager.GetActiveScene(); var timer = System.Diagnostics.Stopwatch.StartNew();
            string full = Resolve(jsonPath); string json = File.ReadAllText(full); var root = JObject.Parse(json);
            var m = root["mapRect"] ?? throw new ArgumentException("mapRect missing");
            var rect = new Rect(Number(m["x"]), Number(m["z"]), Number(m["width"]), Number(m["height"]));
            if (rect != new Rect(-2000, -3000, 4000, 6000)) throw new ArgumentException("This baker requires the explicit compact 4x6km world rect");
            var resolution = Numbers(root["suggestedResolution"], 2); int width = (int)resolution[0], height = (int)resolution[1];
            if (!((width == 512 && height == 768) || (width == 1024 && height == 1536)) || resolution[0] != width || resolution[1] != height)
                throw new ArgumentException("Supported sizes:512x768 or1024x1536");
            var anchors = ParseAnchors(root); var materials = Materials();
            var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            var dressing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true))
                .Where(d => d.Sheet != null && d.Sheet.Geography != null).ToArray();
            var geographies = dressing.Select(d => d.Sheet.Geography).Distinct().ToArray();
            if (geographies.Length != 1) throw new InvalidOperationException("One current dressing geography is required");
            var geography = geographies[0]; var grade = geography.CompactRoadGrade;
            if (grade == null) throw new InvalidOperationException("Actual compact road grade missing"); grade.Validate();
            if (grade.Lines.Length == 0) throw new InvalidOperationException("No fitted route protection data");
            string gradeBefore = JsonUtility.ToJson(grade);
            var geoFields = materials.Select(v => v.GetTexture("_PaintedGeoField")).Distinct().ToArray();
            if (geoFields.Length != 1 || !(geoFields[0] is Texture2D geo) || !geo.isReadable)
                throw new InvalidOperationException("All205 materials must share one readable current geography field");
            var geoRect = materials[0].GetVector("_PaintedGeoRect");
            if (materials.Any(v => v.GetVector("_PaintedGeoRect") != geoRect) || geoRect.z <= 0 || geoRect.w <= 0)
                throw new InvalidOperationException("Inconsistent geography rect");
            var bindings = materials.Select(v => Snapshot(v)).ToList();
            string generation = "FormMap_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string output = Path.Combine(Workspace, "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/FormMaps", generation);
            var receipt = new Receipt { utc = DateTime.UtcNow.ToString("O"), status = "BAKING", generation = generation, jsonPath = full,
                jsonSha256 = Hash(File.ReadAllBytes(full)), fieldPath = Folder + "/FormMaps/" + generation + "/FormMap.asset",
                receiptPath = Path.Combine(output, "form_map_receipt.json"), scene = scene.path, sceneDirtyBefore = scene.isDirty,
                width = width, height = height, worldRect = new Vector4(rect.x, rect.y, rect.width, rect.height),
                groundMaterials = 6, foliageMaterials = 199, roadLines = grade.Lines.Length, bindings = bindings.Select(v => v.row).ToArray(),
                anchors = anchors.Select(v => v.row).ToArray(), notes = new[] {
                    "Engineering RGBA guidance field, not an image edit. Explicit world anchors; no camera/geometry/scene changes.",
                    "RGB is straight alpha-weighted art data; transparent edge RGB is padded2texels. Bilinear,linear,no mipmaps; existing shader LOD0 needs no change.",
                    "Road/river/facility alpha exclusion expanded by one pixel diagonal to protect bilinear samples; outer12m feather.",
                    "Owner eligibility is actual selected terrain XZ triangle coverage; it is not a rendered visibility test.",
                    "This receipt records a material binding, not rendered art approval or performance approval." } };
            LastReceipt = receipt; Texture2D texture = null; bool mutationStarted = false;
            try
            {
                // Preserve the current controlled look, including Quiet9/bank edits, before saving only these owned assets.
                Directory.CreateDirectory(output);
                var backups = new JArray(bindings.Select(b => new JObject { ["material"] = b.row.material, ["initiallyDirty"] = b.row.initiallyDirty, ["liveMaterialJson"] = b.liveJson }));
                File.WriteAllText(Path.Combine(output, "materials_before.json"), backups.ToString());
                foreach (var binding in bindings) if (binding.row.initiallyDirty)
                { AssetDatabase.SaveAssetIfDirty(binding.material); receipt.savedInitiallyDirtyMaterials++; }
                VerifyOtherProperties(bindings);
                int count = checked(width * height); float dx = rect.width / width, dz = rect.height / height;
                float pixelGuard = Mathf.Sqrt(dx * dx + dz * dz);
                var owners = OwnerCoverage(anchors, renderers, materials, rect, width, height);
                var exclusion = new float[count];
                foreach (var line in grade.Lines) for (int i = 1; i < line.Points.Length; i++)
                { ExcludeLine(exclusion, rect, width, height, XZ(line.Points[i - 1]), XZ(line.Points[i]), line.Width * .5f + grade.RoadbedPadding + grade.RoadbedTransition + pixelGuard); receipt.roadSegments++; }
                foreach (var river in geography.Rivers) for (int i = 1; i < river.Points.Length; i++)
                { ExcludeLine(exclusion, rect, width, height, XZ(river.Points[i - 1]), XZ(river.Points[i]), river.Width * .5f + 12 + pixelGuard); receipt.riverSegments++; }
                var facilities = grade.Protected.ToList();
                foreach (var r in renderers.Where(v => v.enabled && v.gameObject.activeInHierarchy && IsFixedFacility(Hierarchy(v.transform))))
                { var b = r.bounds; facilities.Add(new Rect(b.min.x - 2, b.min.z - 2, b.size.x + 4, b.size.z + 4)); }
                foreach (var facility in facilities) ExcludeRect(exclusion, rect, width, height, facility, pixelGuard);
                receipt.protectedRectangles = facilities.Count;
                var sums = new Color[count]; var maxAlpha = new float[count];
                foreach (var anchor in anchors)
                {
                    Range(rect, width, height, anchor.bounds, out int x0, out int z0, out int x1, out int z1);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    {
                        int at = z * width + x; Vector2 p = Pixel(rect, width, height, x, z);
                        float w = Footprint(anchor, p, pixelGuard) * anchor.rgba.a; if (w <= 0) continue;
                        anchor.row.footprintPixels++;
                        if ((owners[at] & anchor.bit) == 0) { receipt.unresolvedOwnerPixels++; continue; }
                        float u = (p.x - geoRect.x) / geoRect.z, v = (p.y - geoRect.y) / geoRect.w;
                        if (u < 0 || u > 1 || v < 0 || v > 1 || geo.GetPixelBilinear(u, v).r <= .001f) continue;
                        if (exclusion[at] >= 1) { receipt.exclusionRemovedPixels++; continue; }
                        anchor.row.eligiblePixels++; anchor.row.maximumAlpha = Mathf.Max(anchor.row.maximumAlpha, w * (1 - exclusion[at]));
                        sums[at] += new Color(anchor.rgba.r * w, anchor.rgba.g * w, anchor.rgba.b * w, w);
                        maxAlpha[at] = Mathf.Max(maxAlpha[at], w);
                    }
                }
                var pixels = new Color32[count];
                for (int i = 0; i < count; i++) if (sums[i].a > 0)
                {
                    float alpha = maxAlpha[i] * (1 - exclusion[i]);
                    pixels[i] = new Color(sums[i].r / sums[i].a, sums[i].g / sums[i].a, sums[i].b / sums[i].a, alpha);
                    if (pixels[i].a > 0) { receipt.alphaPixels++; receipt.alphaSum += pixels[i].a / 255f; receipt.alphaMaximum = Mathf.Max(receipt.alphaMaximum, pixels[i].a / 255f); }
                }
                if (receipt.alphaPixels == 0) throw new InvalidOperationException("No eligible form-map alpha remains after actual road/facility protection");
                receipt.rgbPaddingPixels = PadTransparentRgb(pixels, width, height, 2);
                if (gradeBefore != JsonUtility.ToJson(grade)) throw new InvalidOperationException("Road grade changed during bake");
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = generation, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 1 };
                texture.SetPixels32(pixels); texture.Apply(false, false);
                EnsureFolder(Path.GetDirectoryName(receipt.fieldPath).Replace('\\', '/'));
                if (File.Exists(receipt.fieldPath) || AssetDatabase.LoadMainAssetAtPath(receipt.fieldPath) != null) throw new IOException("Unique form field path collision");
                AssetDatabase.CreateAsset(texture, receipt.fieldPath); AssetDatabase.SaveAssetIfDirty(texture);
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "anchors_input.json"), json);
                receipt.status = "FIELD_CREATED_BINDING_PENDING"; Write(receipt);
                mutationStarted = true;
                foreach (var binding in bindings)
                {
                    binding.material.SetTexture(TextureProperty, texture); binding.material.SetVector(RectProperty, receipt.worldRect);
                    binding.material.SetFloat(EnabledProperty, 1); EditorUtility.SetDirty(binding.material); AssetDatabase.SaveAssetIfDirty(binding.material);
                }
                if (bindings.Any(v => v.material.GetTexture(TextureProperty) != texture || v.material.GetVector(RectProperty) != receipt.worldRect || v.material.GetFloat(EnabledProperty) != 1))
                    throw new InvalidOperationException("Incomplete205 material form binding");
                VerifyOtherProperties(bindings); receipt.otherMaterialPropertiesUnchanged = true;
                receipt.sceneDirtyAfter = scene.isDirty;
                if (receipt.sceneDirtyAfter != receipt.sceneDirtyBefore) throw new InvalidOperationException("Unexpected scene dirty-state change");
                receipt.bound = true; receipt.status = "BOUND_NOT_ART_APPROVED"; receipt.elapsedMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
                Write(receipt); lastBindings = bindings; lastTexture = texture; lastBindingReceipt = receipt;
                return JsonUtility.ToJson(receipt, true);
            }
            catch (Exception ex)
            {
                receipt.error = ex.ToString(); receipt.bound = false;
                receipt.rollbackErrors = mutationStarted ? Restore(bindings).ToArray() : Array.Empty<string>();
                receipt.rollbackCompleted = receipt.rollbackErrors.Length == 0;
                receipt.status = receipt.rollbackCompleted ? "FAILED_BINDINGS_RESTORED" : "FAILED_RESTORE_INCOMPLETE";
                receipt.sceneDirtyAfter = scene.isDirty; receipt.elapsedMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
                if (mutationStarted && !receipt.rollbackCompleted) { lastBindings = bindings; lastTexture = texture; lastBindingReceipt = receipt; }
                if (texture != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture))) Object.DestroyImmediate(texture);
                try { Directory.CreateDirectory(output); Write(receipt); } catch (Exception writeError) { Debug.LogError(writeError); }
                throw;
            }
        }

        public static string RestoreLast()
        {
            Guard(); if (lastBindings == null || lastBindingReceipt == null) throw new InvalidOperationException("No in-session form binding to restore");
            LastReceipt = lastBindingReceipt;
            if (LastReceipt.bound && lastBindings.Any(v => v.material.GetTexture(TextureProperty) != lastTexture))
                throw new InvalidOperationException("A form texture binding changed after this generation");
            var errors = Restore(lastBindings); LastReceipt.rollbackErrors = errors.ToArray(); LastReceipt.rollbackCompleted = errors.Count == 0;
            LastReceipt.bound = false; LastReceipt.status = errors.Count == 0 ? "RESTORED_PREVIOUS_FORM_BINDINGS" : "RESTORE_INCOMPLETE"; Write(LastReceipt);
            if (errors.Count == 0) { lastBindings = null; lastTexture = null; lastBindingReceipt = null; }
            return JsonUtility.ToJson(LastReceipt, true);
        }
        static List<string> Restore(IEnumerable<Binding> bindings)
        {
            var errors = new List<string>();
            foreach (var b in bindings) try { b.material.SetTexture(TextureProperty, b.oldTexture); b.material.SetVector(RectProperty, b.oldRect); b.material.SetFloat(EnabledProperty, b.oldEnabled); EditorUtility.SetDirty(b.material); AssetDatabase.SaveAssetIfDirty(b.material);
                if (OtherPayload(EditorJsonUtility.ToJson(b.material)) != b.otherPayload) throw new InvalidOperationException("Other material properties differ after restore"); }
            catch (Exception ex) { errors.Add(b.row.material + ": " + ex.Message); }
            return errors;
        }
        static void Guard()
        {
            var s = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || s.path != "Assets/_Project/Scenes/World/W_Demo_Compact.unity" || s.isDirty || InkPaintingFoliagePreview.IsActive)
                throw new InvalidOperationException("Clean compact Edit scene with temporary previews restored required");
        }
        static Material[] Materials()
        {
            var ground = AssetDatabase.FindAssets("t:Material", new[] { Folder + "/Materials" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var foliage = AssetDatabase.FindAssets("t:Material", new[] { Folder + "/FoliageMaterials" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if (ground.Length != 6 || foliage.Length != 199) throw new InvalidOperationException("Expected6 ground+199 foliage study materials");
            var result = ground.Concat(foliage).Distinct().OrderBy(v => v, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            string[] props = { TextureProperty, RectProperty, EnabledProperty, "_PaintedFormEnabled", "_PaintedGeoField", "_PaintedGeoRect", "_PaintedGeoEnabled" };
            foreach (var m in result)
                if (m == null || m.shader == null || !m.shader.name.StartsWith("Oheangbu/Study/InkPainting", StringComparison.Ordinal) ||
                    props.Any(v => !m.HasProperty(v)) || m.GetFloat("_PaintedFormEnabled") < .5f || m.GetFloat("_PaintedGeoEnabled") < .5f)
                    throw new InvalidOperationException("Missing or incompatible existing study material");
            foreach (var m in result) foreach (var p in m.GetTexturePropertyNames())
            { var texture = m.GetTexture(p); if (texture != null && !EditorUtility.IsPersistent(texture)) throw new InvalidOperationException("Transient texture must be restored before saving owned material:" + AssetDatabase.GetAssetPath(m) + "/" + p); }
            return result;
        }
        static Binding Snapshot(Material m)
        {
            var t = m.GetTexture(TextureProperty); long local = 0;
            if (t != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t, out string _, out local);
            string liveJson = EditorJsonUtility.ToJson(m);
            return new Binding { material = m, oldTexture = t, oldRect = m.GetVector(RectProperty), oldEnabled = m.GetFloat(EnabledProperty), liveJson = liveJson, otherPayload = OtherPayload(liveJson),
                row = new BindingRow { material = AssetDatabase.GetAssetPath(m), oldTexture = t == null ? "" : AssetDatabase.GetAssetPath(t), oldTextureLocalId = local,
                    oldRect = m.GetVector(RectProperty), oldEnabled = m.GetFloat(EnabledProperty), initiallyDirty = EditorUtility.IsDirty(m) } };
        }
        static void VerifyOtherProperties(IEnumerable<Binding> bindings)
        { foreach (var b in bindings) if (OtherPayload(EditorJsonUtility.ToJson(b.material)) != b.otherPayload) throw new InvalidOperationException("Non-FormMap material property changed:" + b.row.material); }
        public static string OtherPayload(string materialJson)
        {
            var root = JToken.Parse(materialJson); StripOwnedProperties(root); return root.ToString(Newtonsoft.Json.Formatting.None);
        }
        static bool Owned(string name) => name == TextureProperty || name == RectProperty || name == EnabledProperty;
        static void StripOwnedProperties(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var p in obj.Properties().ToArray())
                    if (Owned(p.Name)) p.Remove(); else StripOwnedProperties(p.Value);
            }
            else if (token is JArray array)
            {
                foreach (var child in array.ToArray())
                {
                    if (child is JObject entry && ((entry["first"]?.Type == JTokenType.String && Owned(entry.Value<string>("first"))) ||
                        (entry.Properties().Count() == 1 && Owned(entry.Properties().First().Name)))) child.Remove();
                    else StripOwnedProperties(child);
                }
            }
        }
        static Anchor[] ParseAnchors(JObject root)
        {
            var rows = root["anchors"] as JArray; if (rows == null || rows.Count < 1 || rows.Count > 10) throw new ArgumentException("Expected1..10 explicit anchors");
            var result = new List<Anchor>(); var names = new HashSet<string>(); var owners = new Dictionary<string, int>();
            foreach (JObject row in rows)
            {
                var a = new Anchor { id = row.Value<string>("id"), owner = row.Value<string>("ownerHierarchy"), feather = Number(row["edgeFeatherMetres"]) };
                if (string.IsNullOrEmpty(a.id) || !names.Add(a.id) || string.IsNullOrEmpty(a.owner) || !a.owner.StartsWith("WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/", StringComparison.Ordinal)) throw new ArgumentException("Invalid anchor identity/owner");
                if (!owners.TryGetValue(a.owner, out a.bit)) { a.bit = 1 << owners.Count; owners.Add(a.owner, a.bit); }
                float width = Number(row["widthMetres"]); if (width < 20 || width > 500 || a.feather <= 0 || a.feather >= width * .5f) throw new ArgumentException("Invalid footprint width/feather");
                var rgba = Numbers(row["rgba"], 4); if (rgba.Any(v => v < 0 || v > 1)) throw new ArgumentException("RGBA outside0..1"); a.rgba = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
                var spine = row["spineXZ"] as JArray; if (spine == null || spine.Count != 3) throw new ArgumentException("Exactly3 world-XZ spine points required");
                a.spine = spine.Select(v => { var p = Numbers(v, 2); return new Vector2(p[0], p[1]); }).ToArray();
                a.widths = Numbers(row["widthMultipliersAtSpine"], 3); if (a.widths.Any(v => v <= 0 || v > 1)) throw new ArgumentException("Invalid width taper");
                a.curve = new Vector2[25]; a.radii = new float[25];
                // Quadratic interpolant passes THROUGH the supplied middle point, rather than treating it as an off-curve Bezier handle.
                Vector2 handle = a.spine[1] * 2 - (a.spine[0] + a.spine[2]) * .5f;
                for (int i = 0; i < a.curve.Length; i++) { float t = i / 24f; a.curve[i] = a.spine[0] * ((1 - t) * (1 - t)) + handle * (2 * (1 - t) * t) + a.spine[2] * (t * t);
                    float taper = t <= .5f ? Mathf.Lerp(a.widths[0], a.widths[1], t * 2) : Mathf.Lerp(a.widths[1], a.widths[2], t * 2 - 1); a.radii[i] = width * .5f * taper; }
                float minX = a.curve.Min(v => v.x) - width * .5f, minZ = a.curve.Min(v => v.y) - width * .5f;
                a.bounds = Rect.MinMaxRect(minX, minZ, a.curve.Max(v => v.x) + width * .5f, a.curve.Max(v => v.y) + width * .5f);
                a.row = new AnchorRow { id = a.id, owner = a.owner }; result.Add(a);
            }
            return result.ToArray();
        }
        static float Footprint(Anchor a, Vector2 p, float guard)
        {
            float weight = 0;
            for (int i = 1; i < a.curve.Length; i++) { float distance = SegmentDistance(p, a.curve[i - 1], a.curve[i], out float t);
                float radius = Mathf.Lerp(a.radii[i - 1], a.radii[i], t) - guard;
                weight = Mathf.Max(weight, Smooth01((radius - distance) / a.feather)); }
            return weight;
        }
        public static float Smooth01(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b, out float t)
        { var v = b - a; t = v.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, v) / v.sqrMagnitude) : 0; return (p - a - v * t).magnitude; }
        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        static Vector2 Pixel(Rect r, int w, int h, int x, int z) => new Vector2(r.xMin + (x + .5f) * r.width / w, r.yMin + (z + .5f) * r.height / h);
        static void Range(Rect domain, int w, int h, Rect bounds, out int x0, out int z0, out int x1, out int z1)
        { x0 = Mathf.Max(0, Mathf.FloorToInt((bounds.xMin - domain.xMin) * w / domain.width)); z0 = Mathf.Max(0, Mathf.FloorToInt((bounds.yMin - domain.yMin) * h / domain.height));
          x1 = Mathf.Min(w - 1, Mathf.CeilToInt((bounds.xMax - domain.xMin) * w / domain.width)); z1 = Mathf.Min(h - 1, Mathf.CeilToInt((bounds.yMax - domain.yMin) * h / domain.height)); }
        static void ExcludeLine(float[] mask, Rect domain, int w, int h, Vector2 a, Vector2 b, float radius)
        {
            float full = radius + ExclusionFeather; var bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - full, Mathf.Min(a.y, b.y) - full, Mathf.Max(a.x, b.x) + full, Mathf.Max(a.y, b.y) + full);
            Range(domain, w, h, bounds, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { float d = SegmentDistance(Pixel(domain, w, h, x, z), a, b, out _); int i = z * w + x; mask[i] = Mathf.Max(mask[i], 1 - Smooth01((d - radius) / ExclusionFeather)); }
        }
        static void ExcludeRect(float[] mask, Rect domain, int w, int h, Rect facility, float guard)
        {
            float outer = guard + ExclusionFeather; var bounds = Rect.MinMaxRect(facility.xMin - outer, facility.yMin - outer, facility.xMax + outer, facility.yMax + outer);
            Range(domain, w, h, bounds, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { var p = Pixel(domain, w, h, x, z); float d = Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, facility.xMin, facility.xMax), Mathf.Clamp(p.y, facility.yMin, facility.yMax))); int i = z * w + x; mask[i] = Mathf.Max(mask[i], 1 - Smooth01((d - guard) / ExclusionFeather)); }
        }
        static bool IsFixedFacility(string path) => !path.Contains("_Previous_") &&
            (path.StartsWith("WorldMacro_AuthoredGeography/03_SettlementAndLandmark_Massing/", StringComparison.Ordinal) ||
             path.StartsWith("WorldMacro_Landmarks_Authored/", StringComparison.Ordinal) || path.StartsWith("Playtest_NaturalCave/", StringComparison.Ordinal) ||
             path.StartsWith("Playtest_OriginalC2Inn/", StringComparison.Ordinal) || path.StartsWith("Playtest_Village_Office/", StringComparison.Ordinal));
        static int[] OwnerCoverage(Anchor[] anchors, MeshRenderer[] renderers, Material[] materials, Rect domain, int w, int h)
        {
            var result = new int[w * h];
            foreach (var group in anchors.GroupBy(a => a.owner))
            {
                var matches = renderers.Where(r => Hierarchy(r.transform) == group.Key).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Anchor owner missing/ambiguous:" + group.Key);
                var r = matches[0]; var f = r.GetComponent<MeshFilter>();
                if (!r.enabled || !r.gameObject.activeInHierarchy || f == null || f.sharedMesh == null || !f.sharedMesh.isReadable || !r.sharedMaterials.Any(materials.Contains))
                    throw new InvalidOperationException("Anchor owner lacks active readable study terrain:" + group.Key);
                var mesh = f.sharedMesh; var vertices = mesh.vertices; var matrix = f.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("Nontriangle terrain owner");
                    var indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i += 3) RasterizeOwner(result, group.First().bit, XZ(vertices[indices[i]]), XZ(vertices[indices[i + 1]]), XZ(vertices[indices[i + 2]]), domain, w, h);
                }
            }
            return result;
        }
        static void RasterizeOwner(int[] owners, int bit, Vector2 a, Vector2 b, Vector2 c, Rect domain, int w, int h)
        {
            var ab = b - a; var ac = c - a; double det = (double)ab.x * ac.y - (double)ab.y * ac.x; if (Math.Abs(det) < 1e-12) return;
            Range(domain, w, h, Rect.MinMaxRect(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Min(a.y, Mathf.Min(b.y, c.y)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Max(a.y, Mathf.Max(b.y, c.y))), out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { var p = Pixel(domain, w, h, x, z) - a;
                double u = ((double)p.x * ac.y - (double)p.y * ac.x) / det, v = ((double)ab.x * p.y - (double)ab.y * p.x) / det;
                if (u >= -1e-7 && v >= -1e-7 && u + v <= 1 + 1e-7) owners[z * w + x] |= bit; }
        }
        public static int PadTransparentRgb(Color32[] pixels, int width, int height, int steps)
        {
            if (pixels == null || pixels.Length != checked(width * height) || steps < 0) throw new ArgumentException("Invalid padding input");
            var reached = new bool[pixels.Length]; var front = new List<int>();
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a > 0) { reached[i] = true; front.Add(i); }
            int padded = 0;
            for (int step = 0; step < steps; step++)
            {
                var next = new List<int>();
                foreach (int i in front)
                {
                    int x = i % width, z = i / width;
                    for (int oz = -1; oz <= 1; oz++) for (int ox = -1; ox <= 1; ox++)
                    { int nx = x + ox, nz = z + oz; if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue; int j = nz * width + nx;
                        if (reached[j]) continue; reached[j] = true; var c = pixels[i]; c.a = 0; pixels[j] = c; next.Add(j); padded++; }
                }
                front = next;
            }
            return padded;
        }
        static void EnsureFolder(string folder)
        { string[] parts = folder.Split('/'); string parent = parts[0]; for (int i = 1; i < parts.Length; i++) { string next = parent + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, parts[i]); parent = next; } }
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static void Write(Receipt r)
        { string tmp = r.receiptPath + ".tmp"; File.WriteAllText(tmp, JsonUtility.ToJson(r, true)); if (File.Exists(r.receiptPath)) File.Replace(tmp, r.receiptPath, null); else File.Move(tmp, r.receiptPath); }
    }
}
