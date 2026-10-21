using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-127 step 2 (TEST): the sword form's weapon heads on the great brush. Editor queue entry:
    //   Run("import")       the Blender heads (Tools/Blender/WeaponHeads308/build_heads308.py -> Art/Characters308/WeaponHeads308/v1)
    //                       copied in with their build report
    //   Run("materials")    matte ink body + one pigment per element (the brush's own shader) + the hidden bristle material
    //   Run("prefabs")      one head prefab per sword glyph (GreatBrushWeapon308 on the root)
    //   Run("connect[:dry]") the cue profile of each sword glyph points at its head (Vfx120Profile.WeaponHead308Prefab);
    //                       profiles inside the protected trees are never written
    //   Run("check")        prefabs, attach / mirror / detach on a brush model copy in a preview scene, the build report bounds
    //   Run("stills")       the five glyphs' brushes with their heads, rendered in a preview scene (no focus, no Play)
    // Glyph -> head (D308-127): wood 것 spear, fire 넛 two-handed sword until the bow (step 5), earth 멋 axe, metal 섯
    // two-handed sword, water 엇 flail (straight along the shaft until the chain of step 4).
    public static class WeaponHeads308Build
    {
        const string Folder = "Assets/_Project/Art/Characters/GreatBrush308/WeaponHeads308";
        const string Model = Folder + "/Models/SM_WeaponHeads308.fbx";
        const string Report = Folder + "/Models/build-report.json";
        const string BrushModel = "Assets/_Project/Art/Characters/GreatBrush308/Models/SM_GreatBrush308.fbx";
        const string BrushShader = "Oheangbu/WorldMacroTexturedSurface", HiddenShader = "Oheangbu/WeaponBristleHidden308";
        const string InkPath = Folder + "/Materials/M_WeaponHead308_Ink.mat", HiddenPath = Folder + "/Materials/M_WeaponHead308_BristleHidden.mat";
        static readonly string[] Protected = { "Watershed295", "Reworld292", "MountainTrail285", "W_Demo_Compact", "03_Content" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // grid cell (SpellGrammar308.LetterAt), element key, head object in the model, pigment (sRGB, the element palette)
        static readonly (int Cell, string Key, string Head, string Hex)[] Glyphs =
        {
            (11, "wood", "Head_Spear", "426E58"), (35, "fire", "Head_Sword", "BE543B"), (59, "earth", "Head_Axe", "977343"),
            (83, "metal", "Head_Sword", "A6B0AD"), (107, "water", "Head_Flail", "2C465A"),
        };

        // debate 13 (TEST): the swing afterimage. Colour per element (ArtAudio LOCKED 오행 강조색 번안, starting values to be judged
        // in the film), ink core, the share of the width that is pigment rim; the swing clip per head and the two points of its
        // ribbon in the head's own space (Unity: +Y along the shaft, the axe blade on -X). A line head (halfWidth > 0) widens one
        // point toward the camera: the spear thrusts along its own axis and sweeps no band.
        // colours (debate 13, merged): rim pigment per element, core = ink except water (the core itself is 현색 so the rim can be
        // a lighter 청묵 that does not vanish into black); metal no brighter than the paper (Fable: a rim brighter than the ground
        // reads as light) and a narrower rim
        const string TrailShader = "Oheangbu/PigmentTrail308", TrailInk = "1E1C1A";
        static readonly Dictionary<string, (string Rim, string Core, float RimShare)> TrailHex = new Dictionary<string, (string, string, float)>
        {
            // still round 2: rim .6 left the grey core the larger read - .7 (the core a third of the width)
            { "wood", ("368B75", TrailInk, .7f) }, { "fire", ("BE543B", TrailInk, .7f) }, { "earth", ("B68A3D", TrailInk, .7f) },
            // metal: Fable's rim <= 35 % read as a grey ink stroke in still round 2 - .5, the colour still no brighter than the paper
            { "metal", ("A6B0AD", TrailInk, .5f) }, { "water", ("4F6A84", "2C465A", .7f) },
        };
        // still round 1 (10-22): opacity .9 and a long dry tail left the stroke faint
        const float TrailOpacity = 1f, TrailDry = .55f, TrailEmit = .15f, TrailFade = .3f;
        // still round 1: a band between two points of the head lies in the swing's own surface and is seen nearly edge-on from
        // the shoulder camera (a thin line). Every head draws the stroke along its striking point instead, its width turned
        // toward the camera (the pigment shader, not the gas one; the head's point, not the hidden bristle tip).
        static readonly Dictionary<string, (string State, Vector3 From, Vector3 To, float HalfWidth, string Under)> Strikes =
            new Dictionary<string, (string, Vector3, Vector3, float, string)>
        {
            { "Head_Spear", ("SwingThrow", Vector3.zero, new Vector3(0f, 2.0f, 0f), .1f, null) },              // Fable: StrikeFar belongs to the far circle
            { "Head_Sword", ("SwingSweep", Vector3.zero, new Vector3(0f, 1.1f, 0f), .2f, null) },
            { "Head_Axe", ("SwingSlam", Vector3.zero, new Vector3(-.31f, .56f, 0f), .22f, null) },
            { "Head_Flail", ("SwingArc", Vector3.zero, new Vector3(0f, .70f, 0f), .16f, "Head_Flail_Swing") },
        };
        // the clip starts this long before its apex (the wind-up is not shown: contact <= 2 frames after the click)
        const float StrikeToApex = .03f, StrikeLock = .3f;
        static string TrailPath(string key) => Folder + "/Materials/M_WeaponTrail308_" + key + ".mat";

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Source => Path.Combine(Repo, "Art", "Characters308", "WeaponHeads308", "v1");
        static string PigmentPath(string key) => Folder + "/Materials/M_WeaponHead308_Pigment_" + key + ".mat";
        static string PrefabPath(string key, string head) => Folder + "/Prefabs/PF_WeaponHead308_" + key + "_" + head.Replace("Head_", "").ToLowerInvariant() + ".prefab";

        public static string Run(string command)
        {
            try
            {
                string c = (command ?? "").Trim();
                if (EditorApplication.isPlayingOrWillChangePlaymode && c != "check") return "refused: edit mode only";
                switch (c)
                {
                    case "import": return Import();
                    case "materials": return Materials();
                    case "prefabs": return Prefabs();
                    case "connect": return Connect(false);
                    case "connect:dry": return Connect(true);
                    case "check": return Check();
                    case "stills": return Stills("");
                    case "stills:grow": return Stills("grow");
                    case "stills:dry": return Stills("dry");
                    case "rig": return Rig();
                    case "trails": return Trails();
                }
                return "WeaponHeads308Build: import | materials | prefabs | connect[:dry] | check | stills | rig | trails";
            }
            catch (Exception e) { return "WeaponHeads308Build exception: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0].Trim(); }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------------------------------------------------------------- import

        static string Import()
        {
            string fbx = Path.Combine(Source, "SM_WeaponHeads308.fbx"), report = Path.Combine(Source, "build-report.json");
            if (!File.Exists(fbx) || !File.Exists(report)) return "refused: run Tools/Blender/WeaponHeads308/build_heads308.py first (" + Source + ")";
            EnsureFolder(Folder + "/Models");
            File.Copy(fbx, Path.Combine(Repo, "Oheangbu", Model), true);
            File.Copy(report, Path.Combine(Repo, "Oheangbu", Report), true);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(Report, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(Model) is ModelImporter importer)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
                importer.isReadable = false; importer.globalScale = 1f; importer.useFileScale = true; importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (model == null) return "FAIL: the model did not import " + Model;
            var b = new StringBuilder("WeaponHeads308 import\n  " + Model + "\n");
            foreach (string head in Glyphs.Select(g => g.Head).Distinct())
            {
                var t = Find(model.transform, head);
                var bounds = t != null ? BoundsIn(model.transform, t) : default;
                b.AppendLine("  " + head + (t == null ? " MISSING" : " bounds y " + F(bounds.min.y) + ".." + F(bounds.max.y) + " x " + F(bounds.min.x) + ".." + F(bounds.max.x) + " z " + F(bounds.min.z) + ".." + F(bounds.max.z)));
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- materials

        static string Materials()
        {
            Shader brush = Shader.Find(BrushShader), hidden = Shader.Find(HiddenShader);
            if (brush == null || hidden == null) return "refused: shader missing (" + (brush == null ? BrushShader : HiddenShader) + ")";
            EnsureFolder(Folder + "/Materials");
            var b = new StringBuilder("WeaponHeads308 materials\n");
            Material Make(string path, Shader shader, Action<Material> set)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path); bool made = material == null;
                if (made) material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                material.shader = shader; material.shaderKeywords = Array.Empty<string>(); material.renderQueue = -1; material.enableInstancing = true;
                set(material);
                if (made) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                b.AppendLine("  " + Path.GetFileName(path) + (made ? " created" : " updated"));
                return material;
            }
            // matte like the brush (its own shader and its numbers): the ink is darker than the bamboo, the pigment is a dull earth colour
            void Matte(Material m, Color c) { m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", null); m.SetFloat("_Saturation", .78f); m.SetFloat("_AmbientFloor", .48f); m.SetFloat("_LightResponse", .75f); m.SetFloat("_Cull", 2f); }
            Make(InkPath, brush, m => Matte(m, new Color(.07f, .065f, .06f)));
            foreach (var g in Glyphs.GroupBy(x => x.Key).Select(x => x.First()))
                Make(PigmentPath(g.Key), brush, m => Matte(m, Hex(g.Hex)));
            Make(HiddenPath, hidden, m => { });
            var trail = Shader.Find(TrailShader);
            if (trail == null) return b.AppendLine("  refused: shader " + TrailShader + " not found").ToString().TrimEnd();
            foreach (var pair in TrailHex)
                Make(TrailPath(pair.Key), trail, m =>
                {
                    m.SetColor("_PigmentColor", Hex(pair.Value.Rim)); m.SetColor("_InkColor", Hex(pair.Value.Core)); m.SetFloat("_RimShare", pair.Value.RimShare);
                    m.SetFloat("_Opacity", TrailOpacity); m.SetFloat("_StreakDensity", 14f); m.SetFloat("_DryStrength", TrailDry); m.SetFloat("_EdgeSoftness", .12f);
                });
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- prefabs

        static string Prefabs()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var ink = AssetDatabase.LoadAssetAtPath<Material>(InkPath); var hidden = AssetDatabase.LoadAssetAtPath<Material>(HiddenPath);
            if (model == null || ink == null || hidden == null) return "refused: run import and materials first";
            EnsureFolder(Folder + "/Prefabs");
            var b = new StringBuilder("WeaponHeads308 prefabs\n");
            foreach (var g in Glyphs)
            {
                var pigment = AssetDatabase.LoadAssetAtPath<Material>(PigmentPath(g.Key));
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(model); copy.hideFlags = HideFlags.HideAndDontSave;
                var root = new GameObject("PF_WeaponHead308_" + g.Key) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); copy.transform.localScale = Vector3.one;
                    var source = Find(copy.transform, g.Head) ?? throw new Exception(g.Head + " not in " + Model);
                    var head = Object.Instantiate(source.gameObject, source.position, source.rotation, root.transform);
                    head.name = g.Head;
                    foreach (var renderer in head.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = new[] { ink, pigment };
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; renderer.receiveShadows = true;
                        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    }
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;
                    var weapon = root.AddComponent<GreatBrushWeapon308>(); weapon.Configure(hidden, 0f);
                    // debate 13: the strike's swing and the afterimage's two points (children of the head, so they ride its pose)
                    var strike = Strikes[g.Head];
                    weapon.ConfigureStrike(strike.State, StrikeToApex, StrikeLock);
                    Transform under = strike.Under != null ? Find(root.transform, strike.Under) ?? root.transform : root.transform;
                    Transform Point(string name, Vector3 at)
                    {
                        var p = new GameObject(name).transform; p.SetParent(under, true); p.position = root.transform.TransformPoint(at); p.rotation = root.transform.rotation; return p;
                    }
                    var to = Point("TrailTo", strike.To);
                    var from = strike.HalfWidth > 0f ? null : Point("TrailFrom", strike.From);
                    root.AddComponent<WeaponTrail308>().Configure(from, to, strike.HalfWidth, AssetDatabase.LoadAssetAtPath<Material>(TrailPath(g.Key)), TrailEmit, TrailFade);
                    var bounds = BoundsIn(root.transform, root.transform);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(g.Key, g.Head));
                    b.AppendLine("  " + SpellGrammar308.LetterAt(g.Cell) + " " + g.Key + " -> " + PrefabPath(g.Key, g.Head) + " · y " + F(bounds.min.y) + ".." + F(bounds.max.y) + " · x " + F(bounds.min.x) + ".." + F(bounds.max.x));
                }
                finally { Object.DestroyImmediate(root); Object.DestroyImmediate(copy); }
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- connect

        static string Connect(bool dry)
        {
            var b = new StringBuilder("WeaponHeads308 connect" + (dry ? ":dry" : "") + "\n");
            var done = new HashSet<Vfx120Profile>();
            // a profile a protected tree's visual set points at is read by the protected scenes: never written, wherever it lives
            var readByProtected = new HashSet<Vfx120Profile>();
            foreach (string guid in AssetDatabase.FindAssets("t:SpellVisualSetSO"))
            {
                string setPath = AssetDatabase.GUIDToAssetPath(guid);
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(setPath);
                if (set == null || !IsProtected(setPath)) continue;
                foreach (var g in Glyphs)
                    if (set.TryGet(SpellGrammar308.LetterAt(g.Cell), out var visual) && visual.FxPrefab != null)
                    {
                        var fx = visual.FxPrefab.GetComponentInChildren<Vfx120Effect>(true);
                        if (fx != null && fx.Profile != null) readByProtected.Add(fx.Profile);
                    }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:SpellVisualSetSO"))
            {
                string setPath = AssetDatabase.GUIDToAssetPath(guid);
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(setPath);
                if (set == null) continue;
                b.AppendLine("  set " + setPath + (IsProtected(setPath) ? " (protected tree: its profiles are only read)" : ""));
                foreach (var g in Glyphs)
                {
                    char letter = SpellGrammar308.LetterAt(g.Cell);
                    if (!set.TryGet(letter, out var visual) || visual.FxPrefab == null) { b.AppendLine("    " + letter + " no body"); continue; }
                    var fx = visual.FxPrefab.GetComponentInChildren<Vfx120Effect>(true);
                    var profile = fx != null ? fx.Profile : null;
                    string profilePath = profile != null ? AssetDatabase.GetAssetPath(profile) : "";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(g.Key, g.Head));
                    if (profile == null) { b.AppendLine("    " + letter + " no Vfx120 profile"); continue; }
                    if (IsProtected(profilePath) || readByProtected.Contains(profile)) { b.AppendLine("    " + letter + " " + profilePath + " read by a protected scene: left alone"); continue; }
                    bool already = profile.WeaponHead308Prefab == prefab;
                    b.AppendLine("    " + letter + " " + profilePath + " -> " + (prefab != null ? prefab.name : "MISSING prefab") + (already ? " (already)" : dry ? " (dry)" : ""));
                    if (dry || prefab == null || already || !done.Add(profile)) continue;
                    profile.WeaponHead308Prefab = prefab; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                }
            }
            return b.ToString().TrimEnd();
        }

        static bool IsProtected(string path) => Protected.Any(p => path.IndexOf(p, StringComparison.Ordinal) >= 0);

        // ---------------------------------------------------------------- check

        static string Check()
        {
            var b = new StringBuilder("WeaponHeads308 check\n"); int fail = 0, warn = 0;
            void Line(bool ok, string text, bool warning = false) { if (!ok) { if (warning) warn++; else fail++; } b.AppendLine("  " + (ok ? "ok   " : warning ? "WARN " : "FAIL ") + text); }
            var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            var ink = AssetDatabase.LoadAssetAtPath<Material>(InkPath); var hidden = AssetDatabase.LoadAssetAtPath<Material>(HiddenPath);
            Line(brushModel != null, "brush model " + BrushModel);
            Line(hidden != null && hidden.shader != null && hidden.shader.name == HiddenShader && hidden.shader.passCount == 1, "hidden bristle material: " + HiddenShader + ", one pass (no shadow / depth pass)");
            if (brushModel == null || hidden == null) return Done(b, fail, warn);

            // the build report: what the Blender script measured
            var report = File.Exists(Path.Combine(Repo, "Oheangbu", Report)) ? File.ReadAllText(Path.Combine(Repo, "Oheangbu", Report)) : null;
            Line(report != null, "build report " + Report);
            float Num(string head, string key)
            {
                if (report == null) return float.NaN;
                int at = report.IndexOf("\"" + head + "\"", StringComparison.Ordinal); if (at < 0) return float.NaN;
                int k = report.IndexOf("\"" + key + "\"", at, StringComparison.Ordinal); if (k < 0) return float.NaN;
                int colon = report.IndexOf(':', k) + 1, end = report.IndexOfAny(new[] { ',', '}' }, colon);
                return float.TryParse(report.Substring(colon, end - colon).Trim(), NumberStyles.Float, Inv, out float v) ? v : float.NaN;
            }
            const float ShaftDiameter = .034f, GripToFerrule = .57f, ButtToFerrule = 1.17f, GripAhead = .4f, GripFromButt = .6f;
            foreach (string head in Glyphs.Select(g => g.Head).Distinct())
                Line(Num(head, "pigmentShare") <= .15f && Num(head, "pigmentShare") >= .12f, head + " pigment " + F(Num(head, "pigmentShare") * 100f) + " % of the head's area (12-15 %: debate 13 fills the D308-127 cap)");
            Line(2f * Num("Head_Spear", "maxX") <= 3f * ShaftDiameter, "spear leaf width " + F(2f * Num("Head_Spear", "maxX")) + " m <= 3 x shaft (" + F(3f * ShaftDiameter) + ")");
            Line(Num("Head_Axe", "maxX") >= 8f * ShaftDiameter, "axe blade reach from the shaft " + F(Num("Head_Axe", "maxX")) + " m >= 8 x shaft (" + F(8f * ShaftDiameter) + ")");
            Line(Num("Head_Sword", "widthX") <= .18f, "two-handed sword blade width " + F(Num("Head_Sword", "widthX")) + " m <= .18");
            float spearAll = ButtToFerrule + Num("Head_Spear", "length"), swordAll = ButtToFerrule + Num("Head_Sword", "length");
            Line(Mathf.Abs(swordAll / spearAll - .75f) <= .05f, "sword / spear overall " + F(swordAll) + " / " + F(spearAll) + " = " + F(swordAll / spearAll) + " (a2: ~.75)");
            var reach = Reaches();
            foreach (var g in Glyphs.Where(x => x.Key != "fire"))
            {
                float tip = GripAhead + GripToFerrule + Num(g.Head, "length");
                float need = reach.TryGetValue(g.Cell, out float r) ? r - .6f : float.NaN;
                Line(tip >= need, SpellGrammar308.LetterAt(g.Cell) + " " + g.Head + " body -> tip ~" + F(tip) + " m >= reach - .6 = " + F(need) + " (Sonnet; the swing and the lunge make up the rest)", g.Head == "Head_Flail");
            }

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var g in Glyphs)
                {
                    string tag = SpellGrammar308.LetterAt(g.Cell) + " " + g.Key;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(g.Key, g.Head));
                    var pigment = AssetDatabase.LoadAssetAtPath<Material>(PigmentPath(g.Key));
                    var component = prefab != null ? prefab.GetComponent<GreatBrushWeapon308>() : null;
                    Line(component != null && component.HiddenBristle == hidden, tag + " prefab carries GreatBrushWeapon308 with the hidden bristle material");
                    if (component == null) continue;
                    var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                    Line(renderers.Length > 0 && renderers.All(r => r.sharedMaterials.Length == 2 && r.sharedMaterials[0] == ink && r.sharedMaterials[1] == pigment), tag + " head renderers: ink + its element's pigment");

                    var brush = (GameObject)Object.Instantiate(brushModel); brush.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(brush, scene);
                    var head = (GameObject)Object.Instantiate(prefab); head.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(head, scene);
                    try
                    {
                        brush.transform.SetPositionAndRotation(new Vector3(3, 1, 2), Quaternion.Euler(20f, 35f, -50f));
                        Transform grip = Find(brush.transform, "GripSocket"), ferrule = Find(brush.transform, "Bristle_01"), butt = Find(brush.transform, "ShaftEndSocket");
                        var bristleRenderer = brush.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0));
                        var before = bristleRenderer != null ? bristleRenderer.sharedMaterials : Array.Empty<Material>();
                        var weapon = head.GetComponent<GreatBrushWeapon308>();
                        bool attached = weapon.Attach(brush.transform);
                        Vector3 axis = (ferrule.position - butt.position).normalized;
                        Line(attached && head.transform.parent == grip && (head.transform.position - ferrule.position).magnitude < .001f && Vector3.Dot(head.transform.up, axis) > .999f,
                            tag + " attached under GripSocket at the ferrule (" + F((head.transform.position - ferrule.position).magnitude * 1000f) + " mm) along the shaft (dot " + F(Vector3.Dot(head.transform.up, axis)) + ")");
                        Line(Vector3.Dot(grip.up, axis) > .999f, tag + " GripSocket +Y runs along the shaft toward the tip");
                        int slot = weapon.BristleSlot;
                        Line(bristleRenderer != null && weapon.BrushRenderer == bristleRenderer && slot >= 0 && bristleRenderer.sharedMaterials[slot] == hidden && before[slot] != hidden &&
                            bristleRenderer.sharedMaterials.Where((m, i) => i != slot).SequenceEqual(before.Where((m, i) => i != slot)), tag + " only the bristle slot (" + slot + ") draws the hidden material");
                        Line(head.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == bristleRenderer.gameObject.layer), tag + " head on the brush renderer's layer " + bristleRenderer.gameObject.layer);
                        var mirror = typeof(GreatBrushWeapon308).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        bristleRenderer.enabled = false; mirror.Invoke(weapon, null);
                        bool off = head.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled);
                        bristleRenderer.enabled = true; bristleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mirror.Invoke(weapon, null);
                        bool on = head.GetComponentsInChildren<Renderer>(true).All(r => r.enabled && r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off);
                        Line(off && on, tag + " the head follows its brush renderer (enabled, shadows): the close-up brush is hidden that way");
                        // debate 13: the strike's swing state and the afterimage (emits only after a strike, gone EmitSeconds + FadeSeconds later)
                        var trail = head.GetComponent<WeaponTrail308>();
                        var swingProfile = Resources.Load<GreatBrush308Profile>(GreatBrush308Profile.ResourcesPath);
                        Line(trail != null && trail.Material != null && trail.Material.shader != null && trail.Material.shader.name == TrailShader && trail.Lifetime <= .45f + 1e-4f,
                            tag + " afterimage: " + TrailShader + ", whole life " + (trail != null ? F(trail.Lifetime) : "-") + " s (<= .45: Codex .30 / Fable .35-.5)");
                        Line(swingProfile != null && swingProfile.States.Any(s => s.State == weapon.StrikeState && s.ClipSeconds > 0f) && weapon.StrikeToApex <= 2f / 60f + 1e-4f,
                            tag + " strike swing " + weapon.StrikeState + " · contact " + F(weapon.StrikeToApex * 1000f) + " ms after the click (<= 2 frames)");
                        if (trail != null)
                        {
                            float t0 = Time.time;
                            trail.Step(t0, null); bool idle = trail.Samples == 0;
                            weapon.Strike();
                            for (int k = 0; k < 6; k++) { brush.transform.Rotate(0f, 12f, 0f, Space.World); trail.Step(t0 + .02f * k, null); }
                            int during = trail.Samples;
                            trail.Step(t0 + trail.Lifetime + .01f, null);
                            Line(idle && during >= 2 && trail.Samples == 0, tag + " afterimage: none before the strike, " + during + " samples while it swings, none " + F(trail.Lifetime) + " s after it");
                        }
                        // step 3: grows out of the ferrule (criterion 1: <= .5 s), dries over the last 3 s, sinks back (criterion 8: <= .5 s)
                        float c0 = Time.time, life = 10f;
                        weapon.SetLife(c0, life);
                        float atStart = weapon.Grown; weapon.Step(c0 + weapon.GrowSeconds * .5f); float half = weapon.Grown;
                        weapon.Step(c0 + .5f); float grown = weapon.Grown;
                        Line(atStart <= .05f && half > atStart && half < 1f && grown >= .9f && weapon.GrowSeconds <= .5f,
                            tag + " the head grows out of the ferrule: " + F(atStart) + " -> " + F(half) + " -> " + F(grown) + " (>= .9 by .5 s)");
                        weapon.Step(c0 + life - weapon.DrySeconds - .01f); float wet = weapon.Dryness;
                        weapon.Step(c0 + life - weapon.DrySeconds * .5f); float halfDry = weapon.Dryness;
                        var block = new MaterialPropertyBlock(); var probe = head.GetComponentsInChildren<Renderer>(true)[0];
                        probe.GetPropertyBlock(block, 0); var tinted = block.GetColor("_BaseColor");
                        weapon.Step(c0 + life); float dried = weapon.Dryness;
                        Line(wet == 0f && halfDry > 0f && halfDry < 1f && dried >= .999f && weapon.DrySeconds >= 2.99f && tinted.r > ink.GetColor("_BaseColor").r,
                            tag + " dries over the last " + F(weapon.DrySeconds) + " s of the form: " + F(wet) + " -> " + F(halfDry) + " -> " + F(dried) + " (the ink goes pale)");
                        weapon.Detach(false);
                        bool stillOn = head != null && bristleRenderer.sharedMaterials[slot] == hidden;
                        weapon.Step(Time.time + weapon.RetractSeconds * .5f); float sinking = head != null ? weapon.Grown : -1f;
                        weapon.Step(Time.time + weapon.RetractSeconds + .01f);
                        var back = bristleRenderer.sharedMaterials;
                        Line(stillOn && sinking > 0f && sinking < 1f && head == null && back.Zip(before, (x, y) => ReferenceEquals(x, y)).All(x => x) && weapon.RetractSeconds <= .5f,
                            tag + " the form's end: the head sinks back (" + F(sinking) + " at half time) and is gone with the bristles back after " + F(weapon.RetractSeconds) + " s (<= .5)");
                        // a head attached again over one still sinking: the old one ends first, the bristles stay hidden under the new one
                        var first = (GameObject)Object.Instantiate(prefab); first.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(first, scene);
                        var firstHead = first.GetComponent<GreatBrushWeapon308>(); firstHead.Attach(brush.transform); firstHead.Detach(false);
                        var second = (GameObject)Object.Instantiate(prefab); second.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(second, scene);
                        second.GetComponent<GreatBrushWeapon308>().Attach(brush.transform);
                        bool firstGone = first == null, hiddenUnderSecond = bristleRenderer.sharedMaterials[slot] == hidden;
                        second.GetComponent<GreatBrushWeapon308>().Detach(true);
                        Line(firstGone && hiddenUnderSecond && ReferenceEquals(bristleRenderer.sharedMaterials[slot], before[slot]),
                            tag + " cast again while the old head sinks: the old one ends at once, the new one hides the bristles and gives them back");
                        head = (GameObject)Object.Instantiate(prefab); head.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(head, scene);
                        weapon = head.GetComponent<GreatBrushWeapon308>(); weapon.Attach(brush.transform);
                        weapon.Detach();
                        var after = bristleRenderer.sharedMaterials;
                        Line(head == null && after.Length == before.Length && after.Zip(before, (x, y) => ReferenceEquals(x, y)).All(x => x), tag + " Detach: the head is gone and the brush has its very own materials back");
                        // a head destroyed by anyone else still gives the bristles back
                        var again = (GameObject)Object.Instantiate(prefab); again.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(again, scene);
                        again.GetComponent<GreatBrushWeapon308>().Attach(brush.transform);
                        bool swapped = bristleRenderer.sharedMaterials[slot] == hidden;
                        Object.DestroyImmediate(again);
                        Line(swapped && ReferenceEquals(bristleRenderer.sharedMaterials[slot], before[slot]), tag + " a head destroyed without Detach (scene unload) restores the bristles too");
                    }
                    finally { if (head != null) Object.DestroyImmediate(head); Object.DestroyImmediate(brush); }
                }
                // the presenter's lookup: the main scene's visual set (the Architecture296 copy) gives each sword glyph its head;
                // a protected tree's set gives none (its profiles are the shared catalogue ones and are never written)
                foreach (string guid in AssetDatabase.FindAssets("t:SpellVisualSetSO"))
                {
                    string setPath = AssetDatabase.GUIDToAssetPath(guid);
                    var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(setPath);
                    var found = Glyphs.Where(g => SpellWeaponHeads308.PrefabFor(set, SpellGrammar308.LetterAt(g.Cell)) == AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(g.Key, g.Head))).Select(g => SpellGrammar308.LetterAt(g.Cell)).ToArray();
                    bool main = setPath.IndexOf("Architecture296", StringComparison.Ordinal) >= 0, guarded = IsProtected(setPath);
                    if (main) Line(found.Length == Glyphs.Length, "main scene visual set: the five sword glyphs find their heads (" + new string(found) + ")");
                    else if (guarded) Line(found.Length == 0 && Glyphs.All(g => SpellWeaponHeads308.PrefabFor(set, SpellGrammar308.LetterAt(g.Cell)) == null), "protected set " + Path.GetFileName(setPath) + ": no head (nothing it reads was written)");
                    else b.AppendLine("  note " + Path.GetFileName(setPath) + ": heads for " + (found.Length > 0 ? new string(found) : "none"));
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return Done(b, fail, warn);
        }

        static string Done(StringBuilder b, int fail, int warn) => b.Insert(0, "weaponheads308: " + (fail == 0 ? "COMPLETE" : "FAILED") + ", " + fail + " failed, " + warn + " warnings\n").ToString().TrimEnd();

        // sword.reach of the five rows, read from the live rule sheet (the check does not keep its own numbers)
        static Dictionary<int, float> Reaches()
        {
            var result = new Dictionary<int, float>();
            string path = Path.Combine(Repo, "Oheangbu", "Assets", "_Project", "Data", "Spells", "Rules308_WP14.csv");
            if (!File.Exists(path)) return result;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var cells = line.Split(','); if (cells.Length < 2) continue;
                var g = Glyphs.FirstOrDefault(x => SpellGrammar308.LetterAt(x.Cell).ToString() == cells[0]);
                if (g.Head == null) continue;
                foreach (string pair in cells[cells.Length - 1].Split(';'))
                    if (pair.StartsWith("sword.reach=", StringComparison.Ordinal) && float.TryParse(pair.Substring(12), NumberStyles.Float, Inv, out float v)) result[g.Cell] = v;
            }
            return result;
        }

        // ---------------------------------------------------------------- rig (read only)

        // What a head would find on the player rigs of the open scenes: the two brushes, their grip and ferrule, the bristle slot.
        static string Rig()
        {
            var b = new StringBuilder("WeaponHeads308 rig (read only)\n");
            var rigs = Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (rigs.Length == 0) return b.Append("  no WorldMacroPlayerGestureRig in the open scenes").ToString();
            foreach (var rig in rigs)
            {
                b.AppendLine("  rig " + rig.gameObject.scene.name + "/" + rig.name);
                foreach (var (label, root) in new[] { ("world", rig.WorldBrushRoot308), ("near", rig.NearBrushRoot308) })
                {
                    if (root == null) { b.AppendLine("    " + label + ": no brush root"); continue; }
                    Transform grip = Find(root, GreatBrushWeapon308.GripName), ferrule = Find(root, GreatBrushWeapon308.FerruleName);
                    var bristle = root.GetComponentsInChildren<Renderer>(true).Select(r => (r, slot: Array.FindIndex(r.sharedMaterials, m => m != null && m.name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0))).FirstOrDefault(x => x.slot >= 0);
                    b.AppendLine("    " + label + ": " + root.name + " · grip " + (grip != null) + " · ferrule " + (ferrule != null) +
                        (grip != null && ferrule != null ? " (" + F(grip.InverseTransformPoint(ferrule.position).y) + " m along the grip's +Y)" : "") +
                        " · bristle slot " + (bristle.r != null ? bristle.r.name + "[" + bristle.slot + "] " + bristle.r.sharedMaterials[bristle.slot].name + " layer " + LayerMask.LayerToName(bristle.r.gameObject.layer) + " enabled " + bristle.r.enabled : "NONE"));
                }
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- stills

        static string Stills(string state)
        {
            var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (brushModel == null) return "refused: no brush model";
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>(); var bakedMeshes = new List<Mesh>();
            GameObject Make(GameObject source) { var o = (GameObject)Object.Instantiate(source); o.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(o, scene); made.Add(o); return o; }
            var b = new StringBuilder("WeaponHeads308 stills\n");
            string folder = Path.Combine(Source, "unity"); Directory.CreateDirectory(folder);
            var rt = new RenderTexture(1600, 1000, 24); var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                var lightObject = new GameObject("WeaponHeads308Light") { hideFlags = HideFlags.HideAndDontSave }; SceneManager308.Move(lightObject, scene); made.Add(lightObject);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.color = new Color(1f, .97f, .9f);
                lightObject.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                // the line-up: the plain brush, then the five glyphs. The brush wears the project's brush materials (what the
                // installed brush wears), and is baked to a plain mesh for the still (a skinned mesh is not drawn by a camera
                // that renders a preview scene).
                GameObject Brush()
                {
                    var brush = Make(brushModel);
                    foreach (var r in brush.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = r.sharedMaterials.Select(m =>
                        {
                            string name = m != null ? new[] { "Bamboo", "Brass", "Bristle", "Cord" }.FirstOrDefault(n => m.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) : null;
                            var project = name != null ? AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Characters/GreatBrush308/Materials/M_GreatBrush308_" + name + ".mat") : null;
                            return project != null ? project : m;
                        }).ToArray();
                    return brush;
                }
                var columns = new List<(string Label, GameObject Brush)> { ("brush", Brush()) };
                foreach (var g in Glyphs)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(g.Key, g.Head)); if (prefab == null) continue;
                    var brush = Brush(); var head = Make(prefab);
                    var weapon = head.GetComponent<GreatBrushWeapon308>();
                    if (!weapon.Attach(brush.transform)) b.AppendLine("  " + g.Key + " did not attach");
                    // step 3 states: half grown out of the ferrule / fully dried (the form's last moment)
                    if (state == "grow") { weapon.SetLife(Time.time, 10f); weapon.Step(Time.time + weapon.GrowSeconds * .5f); }
                    else if (state == "dry") { weapon.SetLife(Time.time - 20f, 10f); weapon.Step(Time.time); }
                    columns.Add((SpellGrammar308.LetterAt(g.Cell) + "_" + g.Key, brush));
                }
                for (int i = 0; i < columns.Count; i++)
                {
                    var t = columns[i].Brush.transform;
                    t.SetPositionAndRotation(new Vector3(i * .8f, 0f, 0f), Quaternion.identity);
                    var grip = Find(t, "GripSocket"); if (grip != null) t.position += new Vector3(i * .8f, .65f, 0f) - grip.position;   // grips on one line, 0.65 m up
                    foreach (var sk in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave }; sk.BakeMesh(baked, true);
                        var copy = new GameObject(sk.name + "_baked") { hideFlags = HideFlags.HideAndDontSave }; SceneManager308.Move(copy, scene); made.Add(copy);
                        copy.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                        copy.AddComponent<MeshFilter>().sharedMesh = baked; copy.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
                        sk.enabled = false; bakedMeshes.Add(baked);
                    }
                }
                var camObject = new GameObject("WeaponHeads308Camera") { hideFlags = HideFlags.HideAndDontSave }; SceneManager308.Move(camObject, scene); made.Add(camObject);
                var cam = camObject.AddComponent<Camera>(); cam.scene = scene; cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.74f, .71f, .64f); cam.nearClipPlane = .05f; cam.farClipPlane = 30f;
                void Shot(string name, Vector3 at, Vector3 look, bool ortho, float size)
                {
                    cam.orthographic = ortho; cam.orthographicSize = size; cam.fieldOfView = 32f;
                    cam.transform.position = at; cam.transform.LookAt(look);
                    cam.Render(); var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = old;
                    string file = Path.Combine(folder, name + ".png"); File.WriteAllBytes(file, tex.EncodeToPNG()); b.AppendLine("  " + file);
                }
                Vector3 centre = new Vector3((columns.Count - 1) * .4f, 1.6f, 0f);
                Shot("lineup" + (state.Length > 0 ? "_" + state : "") + "_front", centre + new Vector3(0f, 0f, -8f), centre, true, 1.75f);
                Shot("lineup" + (state.Length > 0 ? "_" + state : "") + "_three_quarter", centre + new Vector3(2.6f, .6f, -6.5f), centre, false, 0f);
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                foreach (var m in bakedMeshes) if (m != null) Object.DestroyImmediate(m);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- trails (stills of one afterimage per glyph)

        // Each glyph's brush and head on a paper-coloured ground, moved through a made-up strike (no clip, no Play: the brush
        // is turned about its grip by hand) while its WeaponTrail308 is stepped with the same clock, then rendered from behind
        // the caster's right shoulder. Judges colour, width and fade only - the real motion is the film's.
        static string Trails()
        {
            var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (brushModel == null) return "refused: no brush model";
            var b = new StringBuilder("WeaponHeads308 trails\n");
            string folder = Path.Combine(Source, "unity"); Directory.CreateDirectory(folder);
            var rt = new RenderTexture(1280, 800, 24); var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            foreach (var g in Glyphs)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                var made = new List<GameObject>(); var meshes = new List<Mesh>();
                GameObject Make(GameObject source) { var o = (GameObject)Object.Instantiate(source); o.hideFlags = HideFlags.HideAndDontSave; SceneManager308.Move(o, scene); made.Add(o); return o; }
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(g.Key, g.Head)); if (prefab == null) { b.AppendLine("  " + g.Key + " no prefab"); continue; }
                    var brush = Make(brushModel);
                    foreach (var r in brush.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = r.sharedMaterials.Select(m =>
                        {
                            string name = m != null ? new[] { "Bamboo", "Brass", "Bristle", "Cord" }.FirstOrDefault(n => m.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) : null;
                            var project = name != null ? AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Characters/GreatBrush308/Materials/M_GreatBrush308_" + name + ".mat") : null;
                            return project != null ? project : m;
                        }).ToArray();
                    var head = Make(prefab); var weapon = head.GetComponent<GreatBrushWeapon308>();
                    weapon.Attach(brush.transform);
                    var trail = head.GetComponent<WeaponTrail308>();
                    var grip = Find(brush.transform, "GripSocket");
                    var lightObject = new GameObject("Light") { hideFlags = HideFlags.HideAndDontSave }; SceneManager308.Move(lightObject, scene); made.Add(lightObject);
                    var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.color = new Color(1f, .97f, .9f); lightObject.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                    var camObject = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave }; SceneManager308.Move(camObject, scene); made.Add(camObject);
                    var cam = camObject.AddComponent<Camera>(); cam.scene = scene; cam.targetTexture = rt; cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(.74f, .71f, .64f); cam.nearClipPlane = .05f; cam.farClipPlane = 40f; cam.fieldOfView = 50f;
                    // the grip at chest height; the strike's pose at a moment u (0..1) - the shaft's direction and the grip's place
                    Vector3 chest = new Vector3(0f, 1.25f, 0f), Tilt = new Vector3(-.35f, 1f, .3f).normalized;
                    (Quaternion, Vector3) Pose(float u)
                    {
                        switch (g.Head)
                        {
                            case "Head_Spear": return (Quaternion.Euler(90f, 0f, 0f), chest + Vector3.forward * Mathf.Lerp(-.4f, .9f, u));   // shaft level, forward thrust
                            case "Head_Axe": return (Quaternion.Euler(Mathf.Lerp(-20f, 120f, u), 0f, 0f), chest);                         // raised behind -> chopped down in front
                            case "Head_Flail": return (Quaternion.AngleAxis(Mathf.Lerp(-80f, 70f, u), Tilt) * Quaternion.Euler(80f, 0f, 0f), chest);
                            default: return (Quaternion.AngleAxis(Mathf.Lerp(-60f, 55f, u), Tilt) * Quaternion.Euler(78f, 0f, 0f), chest);       // diagonal sweep right-high -> left-low
                        }
                    }
                    void Place(float u)
                    {
                        var (rotation, at) = Pose(u);
                        // turn the brush so its grip's +Y (the shaft) points along `rotation * up`, grip on `at`
                        brush.transform.rotation = rotation * Quaternion.Inverse(Quaternion.Inverse(brush.transform.rotation) * grip.rotation);
                        brush.transform.position += at - grip.position;
                    }
                    // the camera stands where it will shoot before the stroke is sampled: a line head turns its width toward it
                    // behind the right shoulder; the spear's thrust runs into that view, so it is seen from its right side
                    cam.transform.position = chest + (g.Head == "Head_Spear" ? new Vector3(3.2f, .8f, .4f) : new Vector3(.9f, .7f, -3.2f));
                    cam.transform.LookAt(chest + Vector3.forward * (g.Head == "Head_Spear" ? 1.6f : .8f) + Vector3.up * .3f);
                    float t0 = Time.time; const int Steps = 9;
                    Place(0f); weapon.Strike();
                    for (int k = 0; k <= Steps; k++) { Place(k / (float)Steps); trail.Step(t0 + TrailEmit * k / Steps, cam); }
                    trail.Step(t0 + TrailEmit + .02f, cam);   // just after the last sample: the freshest stroke the eye sees
                    foreach (var sk in brush.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave }; sk.BakeMesh(baked, true); meshes.Add(baked);
                        var copy = new GameObject(sk.name + "_baked") { hideFlags = HideFlags.HideAndDontSave }; SceneManager308.Move(copy, scene); made.Add(copy);
                        copy.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                        copy.AddComponent<MeshFilter>().sharedMesh = baked; copy.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; sk.enabled = false;
                    }
                    var holder = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "WeaponTrail308");
                    var trailBounds = holder != null && holder.GetComponent<MeshFilter>() != null ? holder.GetComponent<MeshFilter>().sharedMesh.bounds : new Bounds(chest, Vector3.one);
                    cam.transform.LookAt(trailBounds.center);
                    cam.Render(); var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = old;
                    string file = Path.Combine(folder, "trail_" + g.Key + ".png"); File.WriteAllBytes(file, tex.EncodeToPNG());
                    b.AppendLine("  " + SpellGrammar308.LetterAt(g.Cell) + " " + g.Key + " " + g.Head + " samples " + trail.Samples + " · band " + F(trailBounds.size.magnitude) + " m · " + file);
                    if (holder != null) made.Add(holder);
                }
                finally
                {
                    foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                    foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- helpers

        static class SceneManager308
        {
            public static void Move(GameObject o, UnityEngine.SceneManagement.Scene scene) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene);
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { var found = Find(child, name); if (found != null) return found; }
            return null;
        }

        // the bounds of what is under `of`, in `space`'s coordinates (mesh bounds, no renderer needed)
        static Bounds BoundsIn(Transform space, Transform of)
        {
            bool any = false; var bounds = new Bounds();
            foreach (var filter in of.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var mb = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
        static string F(float v) => v.ToString("0.###", Inv);
    }
}
