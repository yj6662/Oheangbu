using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 realm look (SPEC-WORLD-REALM-LOOK-308, TEST): ground, steep rock faces, grass and foliage take a per-realm wash from ONE source.
    // Numbers: Tools/Unity/Stage308_realmlook/realmlook308.json (no colour lives in C# or in a shader).
    //   inspect     what draws the ground / grass / vegetation of the open scenes, and with which shaders                 (read only)
    //   palette     json -> palette asset (+ map import settings) -> shader globals. No scene or material is written.
    //   apply       palette + the ground material's _RealmLook308 + the grass materials' role + the scene driver object (scene saved)
    //   on | off    palette Strength 1 | 0 (asset only; off = the picture before this stage)
    //   check       PASS / FAIL lines
    //   revert      material values from the ledger, driver object removed (scene saved). The asset, the map and the shader code stay.
    // Queue-safe: never opens a dialog, refusals are returned as text.
    public static class RealmLook308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string GroundProperty = "_RealmLook308", RoleProperty = "_RealmLookRole308", MainScene = "W_Demo_Main", TerrainRoot = "Reworld292_Terrain";
        static readonly string[] ProtectedRoots = { "Watershed295", "Reworld292", "MountainTrail285" };
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Stage => Path.Combine(Repo, "Tools", "Unity", "Stage308_realmlook");
        static string LedgerFile => Path.Combine(Stage, "Ledger", "realmlook308.json");

        [Serializable] sealed class RealmCfg { public string id = ""; public float[] ground, rock, grass, foliage; public float groundValue = 1, rockValue = 1, grassValue = 1, foliageValue = 1, amount = 1; }
        [Serializable] sealed class Cfg
        {
            public string version = "", asset = "", map = "", groundMaterial = "", driverObject = "RealmLook308";
            public float strength = 1, groundMaterialStrength = 1;
            public float[] rect, amounts, slope, strata, face, lift, joints;
            public MatCfg[] materialFloats = Array.Empty<MatCfg>();
            public RealmCfg[] realms = Array.Empty<RealmCfg>();
        }
        [Serializable] sealed class MatCfg { public string path = "", property = "", why = ""; public float value; }
        [Serializable] sealed class Was { public string path = "", property = ""; public float before; }
        [Serializable] sealed class Ledger { public List<Was> materials = new List<Was>(); public bool driverCreated; public string applied = ""; }

        static Cfg Read()
        {
            var cfg = JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(Stage, "realmlook308.json")));
            if (cfg == null || cfg.realms == null || cfg.realms.Length != 5) throw new Exception("realmlook308.json needs five realms");
            foreach (var a in new[] { cfg.rect, cfg.amounts, cfg.slope, cfg.strata, cfg.face, cfg.lift, cfg.joints }) if (a == null || a.Length != 4) throw new Exception("rect / amounts / slope / strata / face / lift / joints need four numbers each");
            foreach (var r in cfg.realms) foreach (var c in new[] { r.ground, r.rock, r.grass, r.foliage }) if (c == null || c.Length != 3) throw new Exception("realm " + r.id + ": every colour needs three numbers (sRGB 0-1)");
            return cfg;
        }
        static Ledger ReadLedger() => File.Exists(LedgerFile) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile)) : new Ledger();
        static void WriteLedger(Ledger l) { Directory.CreateDirectory(Path.GetDirectoryName(LedgerFile)); File.WriteAllText(LedgerFile, JsonUtility.ToJson(l, true)); }
        static bool Protected(string path) => !string.IsNullOrEmpty(path) && ProtectedRoots.Any(r => path.Contains("/" + r + "/") || path.Contains("/" + r + "_"));
        static Vector4 V4(float[] a) => new Vector4(a[0], a[1], a[2], a[3]);
        static Color Col(float[] a) => new Color(a[0], a[1], a[2], 1f);
        static string F(float v) => v.ToString("0.###", Inv);

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "inspect") return Inspect();
                if (c == "check") return Check();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED the editor is compiling or importing";
                if (c == "palette") return Palette(Read(), out _);
                if (c == "on" || c == "off") return Switch(c == "on");
                if (c == "apply") return Apply();
                if (c == "revert") return Revert();
                return "REFUSED unknown command '" + c + "' (inspect | palette | apply | on | off | check | revert)";
            }
            catch (Exception e) { return "FAILED " + e.GetType().Name + ": " + e.Message; }
        }

        // ---------------------------------------------------------------- read only

        static string Inspect()
        {
            var sb = new StringBuilder();
            sb.AppendLine("RealmLook308 inspect: quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + ", active scene " + SceneManager.GetActiveScene().path + ", open scenes " + SceneManager.sceneCount
                + ", global _OhRealmLook " + F(Shader.GetGlobalFloat("_OhRealmLook")));
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var terrain = renderers.Where(r => Under(r.transform, TerrainRoot)).ToArray();
            sb.AppendLine("terrain root " + TerrainRoot + ": renderers " + terrain.Length);
            foreach (var g in terrain.SelectMany(r => r.sharedMaterials).Where(m => m != null).GroupBy(m => m))
                sb.AppendLine("  ground material x" + g.Count() + " " + AssetDatabase.GetAssetPath(g.Key) + " [" + g.Key.shader.name + "] has " + GroundProperty + " " + g.Key.HasProperty(GroundProperty));
            var groundMats = new HashSet<Material>(terrain.SelectMany(r => r.sharedMaterials).Where(m => m != null));
            foreach (var g in renderers.Where(r => !Under(r.transform, TerrainRoot) && r.sharedMaterials.Any(m => m != null && groundMats.Contains(m))).GroupBy(r => r.gameObject.scene.name + ":" + r.transform.root.name).OrderByDescending(g => g.Count()).Take(12))
                sb.AppendLine("  same ground material outside the terrain root: " + g.Key + " x" + g.Count());
            foreach (var art in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(a => a.name))
            {
                if (art.Sheet == null) { sb.AppendLine("art " + art.name + ": no sheet"); continue; }
                var parts = art.Sheet.Prototypes.SelectMany(p => p.Lods.SelectMany(l => l.Parts).Select(x => (Kind: p.Category.ToString(), x.Material))).Where(x => x.Material != null).ToArray();
                sb.AppendLine("art " + art.name + " (" + (art.isActiveAndEnabled ? "on" : "off") + ") sheet " + AssetDatabase.GetAssetPath(art.Sheet) + ": prototypes " + art.Sheet.Prototypes.Length + ", materials " + parts.Select(x => x.Material).Distinct().Count());
                foreach (var g in parts.GroupBy(x => x.Material.shader.name + " / " + x.Kind).OrderBy(g => g.Key))
                {
                    var mats = g.Select(x => x.Material).Distinct().ToArray();
                    sb.AppendLine("    " + g.Key + ": materials " + mats.Length + ", alpha clip " + mats.Count(m => m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f)
                        + ", protected path " + mats.Count(m => Protected(AssetDatabase.GetAssetPath(m))) + ", role property " + mats.Count(m => m.HasProperty(RoleProperty)));
                }
            }
            foreach (var grass in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var m = grass.Field != null ? grass.Field.Material : null;
                sb.AppendLine("grass " + grass.name + " (" + (grass.isActiveAndEnabled ? "on" : "off") + ") field " + (grass.Field != null ? AssetDatabase.GetAssetPath(grass.Field) + " seeds " + grass.Field.Count + " gust " + grass.Field.Gust : "none")
                    + " | material " + (m != null ? AssetDatabase.GetAssetPath(m) + " [" + m.shader.name + "] alpha clip " + (m.HasProperty("_AlphaClip") ? F(m.GetFloat("_AlphaClip")) : "-") + " base " + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-") + " saturation "
                        + (m.HasProperty("_Saturation") ? F(m.GetFloat("_Saturation")) : "-") + " far tone " + (m.HasProperty("_BladeFarTone307") ? m.GetVector("_BladeFarTone307").ToString() : "-") : "none"));
            }
            sb.AppendLine("dressing renderers " + Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            foreach (var d in Object.FindObjectsByType<RealmLookDriver308>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine("driver " + d.gameObject.scene.name + ":" + d.name + " palette " + (d.Palette != null ? AssetDatabase.GetAssetPath(d.Palette) : "none") + " live " + d.Live);
            // other shaders that draw many renderers of the open scenes (rock meshes next to the terrain are the next thing to match)
            foreach (var g in renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy).SelectMany(r => r.sharedMaterials).Where(m => m != null).GroupBy(m => m.shader.name).OrderByDescending(g => g.Count()).Take(14))
                sb.AppendLine("shader " + g.Key + ": renderer slots " + g.Count() + ", materials " + g.Distinct().Count());
            return sb.ToString();
        }

        static bool Under(Transform t, string root) { for (var p = t; p != null; p = p.parent) if (p.name == root) return true; return false; }

        // ---------------------------------------------------------------- palette asset

        static RealmLookPaletteSO Asset(Cfg cfg, bool create)
        {
            var so = AssetDatabase.LoadAssetAtPath<RealmLookPaletteSO>(cfg.asset);
            if (so != null || !create) return so;
            if (!cfg.asset.StartsWith("Assets/_Project/Art/World/RealmLook308/", StringComparison.Ordinal)) throw new Exception("the palette asset must live under Assets/_Project/Art/World/RealmLook308/");
            Directory.CreateDirectory(Path.Combine(Repo, "Oheangbu", Path.GetDirectoryName(cfg.asset)));
            AssetDatabase.Refresh();
            so = ScriptableObject.CreateInstance<RealmLookPaletteSO>();
            AssetDatabase.CreateAsset(so, cfg.asset);
            return so;
        }

        static Texture2D Map(Cfg cfg, out string note)
        {
            note = "";
            var importer = AssetImporter.GetAtPath(cfg.map) as TextureImporter;
            if (importer == null) throw new Exception("no realm map at " + cfg.map + " (python Tools/Unity/Stage308_realmlook/_Tools/realmmap308.py)");
            bool ok = !importer.sRGBTexture && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.filterMode == FilterMode.Bilinear && importer.textureCompression == TextureImporterCompression.Uncompressed
                && importer.npotScale == TextureImporterNPOTScale.None && !importer.alphaIsTransparency && importer.alphaSource == TextureImporterAlphaSource.FromInput && importer.maxTextureSize >= 2048;
            if (!ok)
            {
                importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None; importer.alphaIsTransparency = false; importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.maxTextureSize = 2048;
                importer.SaveAndReimport(); note = " (map import settings written)";
            }
            var map = AssetDatabase.LoadAssetAtPath<Texture2D>(cfg.map);
            if (map == null) throw new Exception("the realm map did not import: " + cfg.map);
            return map;
        }

        static string Palette(Cfg cfg, out RealmLookPaletteSO so)
        {
            var map = Map(cfg, out string note);
            so = Asset(cfg, true);
            so.Map = map; so.Rect = V4(cfg.rect); so.Strength = Mathf.Clamp01(cfg.strength); so.Amounts = V4(cfg.amounts); so.Slope = V4(cfg.slope); so.Strata = V4(cfg.strata); so.Face = V4(cfg.face); so.Lift = V4(cfg.lift); so.Joints = V4(cfg.joints);
            so.Realms = cfg.realms.Select(r => new RealmLookPaletteSO.Realm
            {
                Id = (RealmId)Enum.Parse(typeof(RealmId), r.id), Ground = Col(r.ground), Rock = Col(r.rock), Grass = Col(r.grass), Foliage = Col(r.foliage),
                GroundValue = r.groundValue, RockValue = r.rockValue, GrassValue = r.grassValue, FoliageValue = r.foliageValue, Amount = r.amount,
            }).ToArray();
            EditorUtility.SetDirty(so); AssetDatabase.SaveAssetIfDirty(so);
            int live = Push();
            return "RealmLook308 palette: " + cfg.asset + " <- realmlook308.json " + cfg.version + ", strength " + F(so.Strength) + ", map " + map.width + "x" + map.height + note + ", drivers refreshed " + live;
        }

        static int Push()
        {
            int n = 0;
            foreach (var d in Object.FindObjectsByType<RealmLookDriver308>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) { d.Apply(); n++; }
            SceneView.RepaintAll();
            return n;
        }

        static string Switch(bool on)
        {
            var cfg = Read(); var so = Asset(cfg, false);
            if (so == null) return "REFUSED no palette asset yet (run palette)";
            so.Strength = on ? Mathf.Clamp01(cfg.strength) : 0f; EditorUtility.SetDirty(so); AssetDatabase.SaveAssetIfDirty(so);
            return "RealmLook308 " + (on ? "on" : "off") + ": strength " + F(so.Strength) + ", drivers refreshed " + Push() + (on ? "" : " (the json still says " + F(cfg.strength) + "; `palette` or `on` brings it back)");
        }

        // ---------------------------------------------------------------- scene + materials

        static Scene RequireMain()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != MainScene) throw new Exception("open " + MainScene + " first (active scene is " + scene.name + ")");
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isDirty) throw new Exception("scene " + s.path + " has unsaved changes"); }
            return scene;
        }

        static string Backup(string stamp, string assetPath)
        {
            string dir = Path.Combine(Stage, "Backup", stamp); Directory.CreateDirectory(dir);
            string to = Path.Combine(dir, Path.GetFileName(assetPath));
            if (!File.Exists(to)) File.Copy(Path.Combine(Repo, "Oheangbu", assetPath), to);
            return to;
        }

        static bool SetFloat(Material m, string property, float value, Ledger led, string stamp, List<string> log)
        {
            string path = AssetDatabase.GetAssetPath(m);
            if (!m.HasProperty(property)) { log.Add("skip " + path + ": shader " + m.shader.name + " has no " + property); return false; }
            if (Protected(path)) { log.Add("skip " + path + ": protected path"); return false; }
            float now = m.GetFloat(property);
            if (Mathf.Approximately(now, value)) return false;
            if (!led.materials.Any(w => w.path == path && w.property == property)) led.materials.Add(new Was { path = path, property = property, before = now });
            Backup(stamp, path);
            m.SetFloat(property, value); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            log.Add(path + " " + property + " " + F(now) + " -> " + F(value));
            return true;
        }

        static string Apply()
        {
            var scene = RequireMain(); var cfg = Read(); var led = ReadLedger(); var log = new List<string>();
            string stamp = DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_apply";
            string palette = Palette(cfg, out var so);
            var ground = AssetDatabase.LoadAssetAtPath<Material>(cfg.groundMaterial);
            if (ground == null) throw new Exception("no ground material at " + cfg.groundMaterial);
            SetFloat(ground, GroundProperty, cfg.groundMaterialStrength, led, stamp, log);
            foreach (var m in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(g => g.gameObject.scene == scene && g.Field != null && g.Field.Material != null).Select(g => g.Field.Material).Distinct())
                SetFloat(m, RoleProperty, 2f, led, stamp, log);
            foreach (var f in cfg.materialFloats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(f.path);
                if (m == null) { log.Add("skip " + f.path + ": no material"); continue; }
                SetFloat(m, f.property, f.value, led, stamp, log);
            }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == cfg.driverObject);
            var driver = root != null ? root.GetComponent<RealmLookDriver308>() : null;
            bool sceneChange = driver == null || driver.Palette != so;
            if (sceneChange)
            {
                Backup(stamp, scene.path);
                if (root == null) { root = new GameObject(cfg.driverObject); SceneManager.MoveGameObjectToScene(root, scene); led.driverCreated = true; }
                if (driver == null) driver = root.AddComponent<RealmLookDriver308>();
                driver.Palette = so; EditorUtility.SetDirty(driver); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the scene did not save");
                log.Add("scene " + scene.path + ": driver object '" + cfg.driverObject + "' " + (led.driverCreated ? "created" : "updated") + ", saved");
            }
            led.applied = stamp; WriteLedger(led);
            Push();
            return "RealmLook308 apply: " + palette + "\n  " + (log.Count > 0 ? string.Join("\n  ", log) : "no material or scene change") + "\n  backup " + (log.Count > 0 ? "Tools/Unity/Stage308_realmlook/Backup/" + stamp : "not needed") + " | scene dirty " + scene.isDirty + " | driver live " + driver.Live;
        }

        static string Revert()
        {
            var scene = RequireMain(); var cfg = Read(); var led = ReadLedger(); var log = new List<string>();
            string stamp = DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_revert";
            foreach (var w in led.materials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(w.path);
                if (m == null || !m.HasProperty(w.property)) { log.Add("skip " + w.path + " " + w.property); continue; }
                Backup(stamp, w.path); m.SetFloat(w.property, w.before); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); log.Add(w.path + " " + w.property + " -> " + F(w.before));
            }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == cfg.driverObject);
            if (root != null && root.GetComponent<RealmLookDriver308>() != null)
            {
                Backup(stamp, scene.path);
                if (root.transform.childCount == 0 && root.GetComponents<Component>().Length == 2) Object.DestroyImmediate(root); else Object.DestroyImmediate(root.GetComponent<RealmLookDriver308>());
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the scene did not save");
                log.Add("driver removed from " + scene.path + ", saved");
            }
            Shader.SetGlobalFloat("_OhRealmLook", 0f);
            WriteLedger(new Ledger());
            return "RealmLook308 revert: " + (log.Count > 0 ? string.Join(" | ", log) : "nothing to put back") + " | the palette asset, the map and the shader code stay (inert without a driver)";
        }

        // ---------------------------------------------------------------- checks

        static string Check()
        {
            var lines = new List<string>(); int pass = 0, fail = 0;
            void Line(bool ok, string what) { lines.Add((ok ? "PASS " : "FAIL ") + what); if (ok) pass++; else fail++; }
            var cfg = Read(); var so = Asset(cfg, false);
            Line(so != null, "palette asset " + cfg.asset);
            if (so != null)
            {
                Line(so.Map != null && so.Realms.Length == 5 && so.Realms.Select(r => r.Id).Distinct().Count() == 5, "palette has the map and five distinct realms");
                var importer = AssetImporter.GetAtPath(cfg.map) as TextureImporter;
                Line(importer != null && !importer.sRGBTexture && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.textureCompression == TextureImporterCompression.Uncompressed, "realm map is linear, clamped, uncompressed, no mips");
                bool tone = so.Realms.All(r => new[] { r.Ground, r.Rock, r.Grass, r.Foliage }.All(c => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) <= .98f && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) >= .06f));
                Line(tone, "no palette colour is pure white or pure black (ART-COLOR)");
                bool same = Mathf.Approximately(so.Strength, Mathf.Clamp01(cfg.strength)) && so.Amounts == V4(cfg.amounts) && so.Slope == V4(cfg.slope) && so.Strata == V4(cfg.strata) && so.Face == V4(cfg.face) && so.Lift == V4(cfg.lift) && so.Joints == V4(cfg.joints)
                    && cfg.realms.All(r => { var x = so.Realms.FirstOrDefault(y => y.Id.ToString() == r.id); return x.Ground == Col(r.ground) && x.Rock == Col(r.rock) && x.Grass == Col(r.grass) && x.Foliage == Col(r.foliage)
                        && Mathf.Approximately(x.GroundValue, r.groundValue) && Mathf.Approximately(x.RockValue, r.rockValue) && Mathf.Approximately(x.GrassValue, r.grassValue) && Mathf.Approximately(x.FoliageValue, r.foliageValue); });
                Line(same, "palette asset equals realmlook308.json " + cfg.version + (same ? "" : " (run palette; `off` also shows here)"));
            }
            var scene = SceneManager.GetActiveScene();
            var drivers = Object.FindObjectsByType<RealmLookDriver308>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Line(drivers.Length == 1 && drivers[0].gameObject.scene == scene && drivers[0].Palette == so && so != null, "one driver in " + scene.name + " with the palette (found " + drivers.Length + ")");
            float global = Shader.GetGlobalFloat("_OhRealmLook");
            Line(so != null && Mathf.Approximately(global, so.Strength) && (drivers.Length == 0 || drivers[0].Live == so.Strength > 0f), "global _OhRealmLook " + F(global) + " = palette strength");
            var ground = AssetDatabase.LoadAssetAtPath<Material>(cfg.groundMaterial);
            Line(ground != null && ground.HasProperty(GroundProperty) && Mathf.Approximately(ground.GetFloat(GroundProperty), cfg.groundMaterialStrength), "ground material " + GroundProperty + " = " + F(cfg.groundMaterialStrength));
            var terrainMats = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => Under(r.transform, TerrainRoot)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            Line(terrainMats.Length == 1 && terrainMats[0] == ground, "every terrain tile draws with that one material (found " + terrainMats.Length + ")");
            var grassMats = Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(g => g.gameObject.scene == scene && g.Field != null && g.Field.Material != null).Select(g => g.Field.Material).Distinct().ToArray();
            Line(grassMats.Length > 0 && grassMats.All(m => m.HasProperty(RoleProperty) && Mathf.Approximately(m.GetFloat(RoleProperty), 2f)), "grass field materials have the grass role (" + grassMats.Length + ")");
            foreach (var f in cfg.materialFloats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(f.path);
                Line(m != null && m.HasProperty(f.property) && Mathf.Approximately(m.GetFloat(f.property), f.value), Path.GetFileName(f.path) + " " + f.property + " = " + F(f.value) + " (" + f.why + ")");
            }
            foreach (string name in new[] { "Oheangbu/Architecture296/KoreanInkGround", "Oheangbu/WorldMacroVegetation", "Oheangbu/CompactNaturalVegetation" })
            {
                var shader = Shader.Find(name);
                Line(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "shader " + name + " compiles (messages " + (shader != null ? ShaderUtil.GetShaderMessageCount(shader) : -1) + ")");
                if (shader != null) foreach (var msg in ShaderUtil.GetShaderMessages(shader).Take(4)) lines.Add("     " + msg.severity + " " + msg.platform + ": " + msg.message + " @ " + msg.file + ":" + msg.line);
            }
            Line(!scene.isDirty, "scene " + scene.name + " is not dirty");
            lines.Add("RealmLook308 checks, " + pass + " PASS, " + fail + " FAIL");
            return string.Join("\n", lines);
        }
    }
}
