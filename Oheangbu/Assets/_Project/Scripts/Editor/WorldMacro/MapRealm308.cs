using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 map 6 (D308-30, SPEC-MAP-OVERHAUL-308 4c): the editor's check of the realm sheets. Read only: no asset write, no scene,
    // no dialog, no static state. The sheets and MapRealm308.asset are made OFFLINE (Tools/Unity/Stage308_map6/_Tools/map6_realm.py)
    // and copied in; this proves what the editor imported is what was baked and that it still tells the game's realm division.
    //   Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.MapRealm308 Run "check"     (or "status")
    //   -> Art/UI308/Map/checks-realm.txt, head `realm ok N/N` | `realm FAILED n/N`.
    // The rule that replaces "which realm NAMES stand on the sheet": the COLOUR and the LINE on the sheet are the catalogue's realms -
    // at every probe point the border sheet says "inside" for the class of the realm Locations.RealmAt names, the ground sheet
    // carries that realm's wash colour away from a border, and every catalogue border lies on the sheet's zero line.
    public static class MapRealm308
    {
        const string AssetPath = "Assets/_Project/Resources/UI308/Map/MapRealm308.asset";
        const string StylePath = "Assets/_Project/Resources/UI304/map/MapStyle304.asset";
        const string ShaderName = "Oheangbu/UI/TwiceFoldedHanji";
        const float ProbeStepMetres = 40f, EdgeStepMetres = 20f;
        static readonly string[] ShaderProperties = { "_RealmBorder308", "_RealmGround308", "_R308A", "_R308B", "_R308C", "_R308D" };

        sealed class Sheet
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Total;
            public void Add(bool ok, string label) { Total++; if (!ok) Failed++; Lines.Add((ok ? "ok      " : "FAILED  ") + label); }
            public void Info(string label) => Lines.Add("INFO    " + label);
        }

        static string F(float v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);

        static string Sha(string file)
        {
            if (!File.Exists(file)) return "";
            using (var sha = SHA256.Create()) using (var s = File.OpenRead(file))
                return string.Concat(sha.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        static string Abs(string repoRelative) => Path.Combine(Harness303.RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c != "check" && c != "status") return "REFUSED MapRealm308: check | status";
            var sheet = new Sheet();
            var asset = AssetDatabase.LoadAssetAtPath<MapRealm308SO>(AssetPath);
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            sheet.Add(asset != null, "asset " + AssetPath + (asset != null ? " (" + asset.Decision + ", version " + asset.Version + ")" : " does not load as MapRealm308SO"));
            sheet.Add(style != null, "style " + StylePath);
            if (asset == null || style == null) return Finish(sheet);
            sheet.Add(style.Realm308 == asset, "MapStyle304SO.Realm308 is this asset");
            sheet.Add(!style.RegionNamesOnSheet, "D308-30 answer 3: MapStyle304SO.RegionNamesOnSheet is off (no realm name on the sheet; the slip's realm line stands)");
            sheet.Add(style.Notation308 != null && asset.BakedFor != null && style.Notation308.BakedFor == asset.BakedFor,
                "baked for the map of the bundle: " + (asset.BakedFor != null ? asset.BakedFor.name : "NONE"));
            sheet.Info("look: on " + F(asset.On) + ", veil " + F(asset.Veil) + ", faint terrain " + F(asset.FaintTerrain) + ", relief ink " + F(asset.ReliefInk) + ", wash " + F(asset.WashUnwalked) + " / " + F(asset.WashWalked)
                + ", border ink " + F(asset.LineInkUnwalked) + " / " + F(asset.LineInkWalked) + " " + F(asset.LinePx) + " px, gaps " + F(asset.LineBreak) + " @ " + F(asset.LineBreakMetres) + " m, bleed " + F(asset.BleedPx) + " px " + F(asset.BleedInk));
            if (c == "status") return Finish(sheet);

            Texture2D border = null, ground = null;
            try
            {
                border = Texture(sheet, "border", asset.Border, asset.BorderWidth, asset.BorderHeight, asset.BorderSha256);
                ground = Texture(sheet, "ground", asset.Ground, asset.GroundWidth, asset.GroundHeight, asset.GroundSha256);
                // inputs: what the bake read is what is on disk now
                int stale = 0; var notes = new List<string>();
                foreach (var input in asset.Inputs ?? Array.Empty<MapRealm308SO.Input>())
                {
                    string now = input != null ? Sha(Abs(input.Path ?? "")) : "";
                    if (input == null || now.Length == 0 || now != input.Sha256) { stale++; notes.Add((input != null ? input.Role : "?") + (now.Length == 0 ? " missing" : " changed")); }
                }
                sheet.Add(stale == 0 && asset.Inputs != null && asset.Inputs.Length >= 5, "bake inputs unchanged on disk: " + (asset.Inputs != null ? asset.Inputs.Length : 0) + " files"
                    + (stale > 0 ? " - " + string.Join(", ", notes) + " (bake again: map6_realm.py)" : ""));
                // the realm table = the catalogue the map asks
                var catalogue = asset.BakedFor != null ? asset.BakedFor.Locations : null;
                var entries = catalogue != null ? catalogue.Entries.Where(e => e != null && e.Priority == 0 && e.Polygon != null && e.Polygon.Length >= 3).ToArray() : Array.Empty<WorldLocationCatalog.Entry>();
                var byId = (asset.Realms ?? Array.Empty<MapRealm308SO.Realm>()).Where(r => r != null && !string.IsNullOrEmpty(r.Id)).ToDictionary(r => r.Id, r => r);
                bool table = entries.Length == 5 && byId.Count == 5 && entries.All(e => byId.TryGetValue(e.Id, out var r) && r.Name == e.Name && r.Corners == e.Polygon.Length && r.BorderChannel >= 0 && r.BorderChannel <= 3);
                sheet.Add(table, "realm table = the location catalogue's Priority 0 entries: " + string.Join(" / ", entries.Select(e => e.Name + (byId.TryGetValue(e.Id, out var r) ? " " + r.Element + " #" + ColorUtility.ToHtmlStringRGB(r.Wash) + " ch " + "RGBA"[Mathf.Clamp(r.BorderChannel, 0, 3)] : " MISSING"))));
                if (table && border != null && ground != null && asset.BakedFor != null) Probe(sheet, asset, catalogue, entries, byId, border, ground);
                else sheet.Add(false, "probe not run (no table / no readable sheet)");
            }
            catch (Exception e) { sheet.Add(false, "the check threw: " + e.GetType().Name + " " + e.Message); }
            finally
            {
                if (border != null) Object.DestroyImmediate(border);
                if (ground != null) Object.DestroyImmediate(ground);
            }
            // the shader the sheet draws with
            var shader = Shader.Find(ShaderName);
            var missing = shader != null ? ShaderProperties.Where(p => shader.FindPropertyIndex(p) < 0).ToArray() : ShaderProperties;
            sheet.Add(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader) && missing.Length == 0,
                "shader " + ShaderName + (shader == null ? " not found" : ": supported " + shader.isSupported + ", compile error " + ShaderUtil.ShaderHasError(shader) + (missing.Length > 0 ? ", missing " + string.Join(", ", missing) : ", the six map 6 properties are there")));
            sheet.Info("not in this check: the sheet's pixels (captures: RUN_ORDER_map6.md), the minimap's pixels (must be unchanged), Play");
            return Finish(sheet);
        }

        /// <summary>Importer and file of one sheet; returns a readable copy decoded from the PNG (the imported texture is not readable).</summary>
        static Texture2D Texture(Sheet sheet, string name, Texture2D texture, int width, int height, string sha)
        {
            if (texture == null) { sheet.Add(false, name + " sheet: no texture"); return null; }
            string path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            bool import = importer != null && !importer.sRGBTexture && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.npotScale == TextureImporterNPOTScale.None;
            sheet.Add(texture.width == width && texture.height == height && texture.format == TextureFormat.RGBA32 && texture.mipmapCount == 1 && texture.filterMode == FilterMode.Bilinear
                    && texture.wrapMode == TextureWrapMode.Clamp && import,
                name + " sheet " + path + ": " + texture.width + " x " + texture.height + " " + texture.format + ", mips " + texture.mipmapCount + ", " + texture.filterMode + ", " + texture.wrapMode
                + ", importer linear " + (importer != null && !importer.sRGBTexture) + " uncompressed " + (importer != null && importer.textureCompression == TextureImporterCompression.Uncompressed)
                + " (want " + width + " x " + height + " RGBA32, 1 mip, Bilinear, Clamp, linear, uncompressed, NPOT kept) = " + (texture.width * texture.height * 4) + " B");
            string file = Path.Combine(Harness303.RepoRoot, "Oheangbu", path.Replace('/', Path.DirectorySeparatorChar));
            string now = Sha(file);
            sheet.Add(now.Length > 0 && now == sha, name + " sheet file sha256 " + (now.Length >= 12 ? now.Substring(0, 12) : "MISSING") + " = the bake's " + (sha != null && sha.Length >= 12 ? sha.Substring(0, 12) : "NONE"));
            if (now.Length == 0) return null;
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!copy.LoadImage(File.ReadAllBytes(file), false)) { Object.DestroyImmediate(copy); sheet.Add(false, name + " sheet: the PNG does not decode"); return null; }
            return copy;
        }

        static void Probe(Sheet sheet, MapRealm308SO asset, WorldLocationCatalog catalogue, WorldLocationCatalog.Entry[] entries, Dictionary<string, MapRealm308SO.Realm> byId, Texture2D border, Texture2D ground)
        {
            Vector2 min = asset.BakedFor.BoundsMin, size = asset.BakedFor.BoundsMax - asset.BakedFor.BoundsMin;
            float range = Mathf.Max(1f, asset.RangeMetres), texel = Mathf.Max(size.x / border.width, size.y / border.height);
            int probes = 0, noRealm = 0, wrongSide = 0, wrongWash = 0, washProbes = 0; float worstSide = float.MaxValue, worstWash = 0f;
            for (float z = min.y + ProbeStepMetres * .5f; z < min.y + size.y; z += ProbeStepMetres)
                for (float x = min.x + ProbeStepMetres * .5f; x < min.x + size.x; x += ProbeStepMetres)
                {
                    probes++;
                    var realm = catalogue.RealmAt(new Vector3(x, 0f, z));
                    if (realm == null || !byId.TryGetValue(realm.Id, out var row)) { noRealm++; continue; }
                    float u = (x - min.x) / size.x, v = (z - min.y) / size.y;
                    Color b = border.GetPixelBilinear(u, v); Color g = ground.GetPixelBilinear(u, v);
                    // signed distance of the realm's own class, metres (+ inside): never clearly outside the class the catalogue puts the point in
                    float own = (b[row.BorderChannel] * 255f - 128f) / 127f * range;
                    worstSide = Mathf.Min(worstSide, own);
                    if (own < -texel) wrongSide++;
                    // away from every border (two blend widths) the ground carries the realm's wash colour
                    float least = Mathf.Min(Mathf.Min(Mathf.Abs(b.r * 255f - 128f), Mathf.Abs(b.g * 255f - 128f)), Mathf.Min(Mathf.Abs(b.b * 255f - 128f), Mathf.Abs(b.a * 255f - 128f))) / 127f * range;
                    if (least >= Mathf.Min(range * .98f, asset.BlendMetres * 1.5f))
                    {
                        washProbes++;
                        float d = Mathf.Max(Mathf.Abs(g.r - row.Wash.r), Mathf.Max(Mathf.Abs(g.g - row.Wash.g), Mathf.Abs(g.b - row.Wash.b)));
                        worstWash = Mathf.Max(worstWash, d);
                        if (d > 3f / 255f) wrongWash++;
                    }
                }
            sheet.Add(noRealm == 0, "every probe point (" + probes + ", every " + F(ProbeStepMetres) + " m) lies in a realm of the catalogue" + (noRealm > 0 ? ": " + noRealm + " in none" : ""));
            sheet.Add(wrongSide == 0, "border sheet: at every probe the catalogue's realm is on the INSIDE of its own colour class (worst " + F(worstSide, "0.#") + " m; a point within one texel, " + F(texel, "0.#") + " m, of a border may read either side)"
                + (wrongSide > 0 ? ": " + wrongSide + " on the wrong side" : ""));
            sheet.Add(wrongWash == 0 && washProbes > probes / 2, "ground sheet: away from a border the colour is the realm's wash (" + washProbes + " probes, worst channel difference " + F(worstWash * 255f, "0.#") + " / 255)"
                + (wrongWash > 0 ? ": " + wrongWash + " off by more than 3 / 255" : ""));
            // every border of the catalogue (an edge two realms share) lies on the sheet's zero line
            int edgePoints = 0, offLine = 0; float worstEdge = 0f;
            for (int i = 0; i < entries.Length; i++)
                for (int k = 0; k < entries[i].Polygon.Length; k++)
                {
                    Vector2 a = entries[i].Polygon[k], bb = entries[i].Polygon[(k + 1) % entries[i].Polygon.Length];
                    if (OnOutline(a, bb, min, size)) continue;
                    int n = Mathf.Max(2, Mathf.CeilToInt((bb - a).magnitude / EdgeStepMetres));
                    for (int s = 1; s < n; s++)
                    {
                        Vector2 p = Vector2.Lerp(a, bb, s / (float)n);
                        Color b = border.GetPixelBilinear((p.x - min.x) / size.x, (p.y - min.y) / size.y);
                        float least = Mathf.Min(Mathf.Min(Mathf.Abs(b.r * 255f - 128f), Mathf.Abs(b.g * 255f - 128f)), Mathf.Min(Mathf.Abs(b.b * 255f - 128f), Mathf.Abs(b.a * 255f - 128f))) / 127f * range;
                        edgePoints++; worstEdge = Mathf.Max(worstEdge, least);
                        if (least > 2f) offLine++;
                    }
                }
            sheet.Add(edgePoints > 100 && offLine == 0, "the drawn line follows the game's division: " + edgePoints + " points on the catalogue's realm borders (every " + F(EdgeStepMetres) + " m) are within 2 m of the sheet's zero line (worst " + F(worstEdge, "0.##") + " m)"
                + (offLine > 0 ? ": " + offLine + " farther" : ""));
        }

        static bool OnOutline(Vector2 a, Vector2 b, Vector2 min, Vector2 size)
        {
            const float eps = .01f;
            return (Mathf.Abs(a.x - min.x) < eps && Mathf.Abs(b.x - min.x) < eps) || (Mathf.Abs(a.x - min.x - size.x) < eps && Mathf.Abs(b.x - min.x - size.x) < eps)
                || (Mathf.Abs(a.y - min.y) < eps && Mathf.Abs(b.y - min.y) < eps) || (Mathf.Abs(a.y - min.y - size.y) < eps && Mathf.Abs(b.y - min.y - size.y) < eps);
        }

        static string Finish(Sheet sheet)
        {
            string head = "realm " + (sheet.Failed == 0 ? "ok" : "FAILED") + " " + (sheet.Total - sheet.Failed) + "/" + sheet.Total;
            string folder = Path.Combine(Harness303.RepoRoot, "Art", "UI308", "Map");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "checks-realm.txt"), head + "\n" + string.Join("\n", sheet.Lines) + "\n", new UTF8Encoding(false));
            return head + "\n" + string.Join("\n", sheet.Lines);
        }
    }
}
