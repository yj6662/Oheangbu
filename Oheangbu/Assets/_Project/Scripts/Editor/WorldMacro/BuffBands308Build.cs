using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-127 (debate 11 A, debate 14 - TEST): the buff bands on the great brush. Editor queue entry:
    //   Run("asset")         BuffBands308.asset (the 19 glyphs -> region table, places, sizes) + one matte pigment per element
    //                        and the paper underline (the brush's own shader, no emission)
    //   Run("connect[:dry]") the visual sets outside the protected trees point at it (SpellVisualSetSO.BuffBands308)
    //   Run("check")         the table, the layout gate, the materials, and the bands' behaviour on a brush model copy in a
    //                        preview scene with a made-up buff clock (wind on, cover, fall back, unwind, form, mirror)
    //   Run("stills")        three bands on the plain brush and on a brush with a weapon head (no focus, no Play)
    public static class BuffBands308Build
    {
        const string Folder = "Assets/_Project/Art/Characters/GreatBrush308/BuffBands308";
        const string AssetPath = Folder + "/BuffBands308.asset";
        const string BrushModel = "Assets/_Project/Art/Characters/GreatBrush308/Models/SM_GreatBrush308.fbx";
        const string BrushShader = "Oheangbu/WorldMacroTexturedSurface";
        const string SpearHead = "Assets/_Project/Art/Characters/GreatBrush308/WeaponHeads308/Prefabs/PF_WeaponHead308_wood_spear.prefab";
        static readonly string[] Protected = { "Watershed295", "Reworld292", "MountainTrail285", "W_Demo_Compact", "03_Content" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // debate 11 A (Fable's sorting, checked against the CSV in debate 14): 검 is a healing tree zone, not a carried buff
        static readonly (string Letters, BandRegion308 Region)[] Table =
        {
            ("넉넌선섬겅넝멍성엉", BandRegion308.Attack), ("걱건넘먹먼멈", BandRegion308.Body), ("석억언엄", BandRegion308.Ink),
        };
        // the pigment of each element (debate 13's afterimage rims: ArtAudio 오행 강조색, judged by ΔE there); paper = UI 한지 token
        static readonly string[] PigmentHex = { "368B75", "BE543B", "B68A3D", "A6B0AD", "4F6A84" };
        const string PaperHex = "E6E2D7";
        static readonly string[] Keys = { "wood", "fire", "earth", "metal", "water" };

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Source => Path.Combine(Repo, "Art", "Characters308", "BuffBands308", "v1");

        public static string Run(string command)
        {
            try
            {
                string c = (command ?? "").Trim();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: edit mode only";
                switch (c)
                {
                    case "asset": return MakeAsset();
                    case "connect": return Connect(false);
                    case "connect:dry": return Connect(true);
                    case "check": return Check();
                    case "stills": return Stills();
                    case "rig": return Rig();
                }
                return "BuffBands308Build: asset | connect[:dry] | check | stills | rig";
            }
            catch (Exception e) { return "BuffBands308Build exception: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0].Trim(); }
        }

        // the element of a glyph is its initial: ㄱ wood, ㄴ fire, ㅁ earth, ㅅ metal, ㅇ water
        static Element ElementOf(char letter)
        {
            int initial = (letter - 0xAC00) / 588;
            return initial == 0 ? Element.Wood : initial == 2 ? Element.Fire : initial == 6 ? Element.Earth : initial == 9 ? Element.Metal : Element.Water;
        }

        // ---------------------------------------------------------------- asset

        static string MakeAsset()
        {
            var shader = Shader.Find(BrushShader);
            if (shader == null) return "refused: shader missing " + BrushShader;
            EnsureFolder(Folder + "/Materials");
            var b = new StringBuilder("BuffBands308 asset\n");
            Material Make(string name, Color colour)
            {
                string path = Folder + "/Materials/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path); bool made = material == null;
                if (made) material = new Material(shader) { name = name };
                material.shader = shader; material.shaderKeywords = Array.Empty<string>(); material.renderQueue = -1; material.enableInstancing = true;
                // matte like the brush and the weapon heads (WeaponHeads308Build.Materials): no texture, no emission
                material.SetColor("_BaseColor", colour); material.SetTexture("_BaseMap", null); material.SetFloat("_Saturation", .78f); material.SetFloat("_AmbientFloor", .48f); material.SetFloat("_LightResponse", .75f); material.SetFloat("_Cull", 2f);
                if (made) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                b.AppendLine("  " + path + (made ? " created" : " updated"));
                return material;
            }
            var asset = AssetDatabase.LoadAssetAtPath<BuffBands308SO>(AssetPath); bool fresh = asset == null;
            if (fresh) asset = ScriptableObject.CreateInstance<BuffBands308SO>();
            // the places, sizes and times are the code's defaults (BuffBands308SO): written again on every run
            var defaults = ScriptableObject.CreateInstance<BuffBands308SO>();
            asset.Plain = defaults.Plain; asset.Form = defaults.Form; asset.BandLength = defaults.BandLength; asset.UnderlineLength = defaults.UnderlineLength;
            asset.ShaftRadius = defaults.ShaftRadius; asset.BandRadius = defaults.BandRadius; asset.UnderlineRadius = defaults.UnderlineRadius; asset.Groove = defaults.Groove; asset.Turns = defaults.Turns;
            asset.HandHalf = defaults.HandHalf; asset.UpperHand = defaults.UpperHand; asset.LowerHand = defaults.LowerHand; asset.Ferrule = defaults.Ferrule; asset.Butt = defaults.Butt;
            asset.EnterSeconds = defaults.EnterSeconds; asset.ExitSeconds = defaults.ExitSeconds;
            Object.DestroyImmediate(defaults);
            asset.Entries = Table.SelectMany(t => t.Letters.Select(ch => new BuffBands308SO.Entry { Letter = ch.ToString(), Region = t.Region, Element = ElementOf(ch) })).ToArray();
            asset.Pigments = new Material[5];
            for (int i = 0; i < 5; i++) asset.Pigments[i] = Make("M_BuffBand308_" + Keys[i], Hex(PigmentHex[i]));
            asset.Paper = Make("M_BuffBand308_Paper", Hex(PaperHex));
            if (fresh) AssetDatabase.CreateAsset(asset, AssetPath); else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            b.AppendLine("  " + AssetPath + (fresh ? " created" : " updated") + " · " + asset.Entries.Length + " glyphs");
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- connect

        static string Connect(bool dry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<BuffBands308SO>(AssetPath);
            if (asset == null) return "refused: run asset first";
            var b = new StringBuilder("BuffBands308 connect" + (dry ? ":dry" : "") + "\n");
            foreach (string guid in AssetDatabase.FindAssets("t:SpellVisualSetSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(path);
                if (set == null) continue;
                if (IsProtected(path)) { b.AppendLine("  " + path + " protected tree: left alone"); continue; }
                bool already = set.BuffBands308 == asset;
                b.AppendLine("  " + path + (already ? " (already)" : dry ? " (dry)" : " -> BuffBands308"));
                if (dry || already) continue;
                set.BuffBands308 = asset; EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            }
            return b.ToString().TrimEnd();
        }

        static bool IsProtected(string path) => Protected.Any(p => path.IndexOf(p, StringComparison.Ordinal) >= 0);

        // ---------------------------------------------------------------- check

        // a made-up buff clock: letter -> (cast at, order, runs until)
        sealed class Clock : IBuffBandSource308
        {
            public readonly Dictionary<char, (float Since, int Order, float Until)> Runs = new Dictionary<char, (float, int, float)>();
            int _order;
            public void Cast(char letter, float now, float seconds) => Runs[letter] = (now, ++_order, now + seconds);
            public void End(char letter) => Runs.Remove(letter);
            public bool Started(char letter, float now, out float since, out int order)
            {
                since = 0f; order = 0;
                if (!Runs.TryGetValue(letter, out var run) || now >= run.Until) return false;
                since = run.Since; order = run.Order; return true;
            }
        }

        static string Check()
        {
            var b = new StringBuilder("BuffBands308 check\n"); int fail = 0, warn = 0;
            void Pass(string m) => b.AppendLine("  PASS " + m);
            void Fail(string m) { fail++; b.AppendLine("  FAIL " + m); }
            void Warn(string m) { warn++; b.AppendLine("  WARN " + m); }
            void Expect(bool ok, string m) { if (ok) Pass(m); else Fail(m); }

            var asset = AssetDatabase.LoadAssetAtPath<BuffBands308SO>(AssetPath);
            if (asset == null) return "FAIL: no " + AssetPath + " (run asset)";
            // the table: 19 glyphs, once each, the debate's regions, the element of the initial, no 검
            var letters = asset.Entries.Select(e => e.Letter).ToList();
            Expect(letters.Count == 19 && letters.Distinct().Count() == 19, "table: " + letters.Count + " glyphs, " + letters.Distinct().Count() + " distinct (19)");
            Expect(!letters.Contains("검"), "table: 검 (healing tree zone) has no band");
            foreach (var (row, region) in Table)
                foreach (char ch in row)
                    Expect(asset.TryGet(ch, out var e) && e.Region == region && e.Element == ElementOf(ch), "table: " + ch + " -> " + region + " " + ElementOf(ch));
            // the layout gate
            var problems = asset.LayoutProblems();
            Expect(problems.Count == 0, "layout: " + (problems.Count == 0 ? "bands >= 8 cm, pigment gaps >= a band, clear of hands, ferrule and butt (plain and form)" : string.Join("; ", problems)));
            // the materials: matte brush shader, no emission, the paper no brighter than the UI paper
            var mats = asset.Pigments.Concat(new[] { asset.Paper }).ToList();
            Expect(mats.All(m => m != null && m.shader != null && m.shader.name == BrushShader), "materials: 6, all " + BrushShader);
            Expect(mats.All(m => m == null || (!m.IsKeywordEnabled("_EMISSION") && (!m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent <= 0f))), "materials: no emission");
            if (asset.Paper != null) { var p = asset.Paper.GetColor("_BaseColor"); Expect(p.grayscale <= Hex(PaperHex).grayscale + 1e-3f, "paper underline " + Hex6(p) + " no brighter than the UI paper"); }
            for (int i = 0; i < 5; i++)
            {
                var c = asset.Pigments[i] != null ? asset.Pigments[i].GetColor("_BaseColor") : Color.magenta;
                Expect(c.maxColorComponent <= 1f && c.grayscale <= Hex(PaperHex).grayscale, Keys[i] + " pigment " + Hex6(c) + " (LDR, darker than the paper)");
            }
            // connected
            foreach (string guid in AssetDatabase.FindAssets("t:SpellVisualSetSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); if (IsProtected(path)) continue;
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(path);
                if (set != null && set.BuffBands308 != asset) Warn(path + " not connected (run connect)");
            }

            // behaviour on a brush model copy, in a preview scene
            var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (brushModel == null) { Fail("no brush model " + BrushModel); return Done(b, fail, warn); }
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>();
            GameObject Make(GameObject source) { var o = (GameObject)Object.Instantiate(source); o.hideFlags = HideFlags.HideAndDontSave; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene); made.Add(o); return o; }
            try
            {
                var brush = Make(brushModel);
                var host = new GameObject("BuffBands308Host") { hideFlags = HideFlags.HideAndDontSave }; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene); made.Add(host);
                var bands = host.AddComponent<AuraBrushBands308>();
                var clock = new Clock();
                bands.Bind(clock, asset); bands.AttachTo(brush.transform);
                Expect(bands.BrushCount == 1, "attach: one brush (grip found)");
                float t = 100f;
                bands.Step(t);
                Expect(Regions().All(r => bands.RendererOf(r) == null || !bands.RendererOf(r).enabled), "no buff: no band drawn");
                clock.Cast('선', t, 30f); bands.Step(t + .001f); bands.Step(t + asset.EnterSeconds * .5f);
                float half = bands.WoundOf(BandRegion308.Attack);
                Expect(half > .2f && half < .8f, "선 winds on: " + F(half) + " wound at half the enter time");
                bands.Step(t + asset.EnterSeconds + .01f);
                Expect(bands.LetterOf(BandRegion308.Attack) == '선' && bands.WoundOf(BandRegion308.Attack) >= 1f, "선 shows on the attack place, fully wound after " + F(asset.EnterSeconds) + " s");
                var unit = bands.UnitOf(BandRegion308.Attack); var grip = Find(brush.transform, GreatBrushWeapon308.GripName);
                Expect(unit != null && unit.parent == grip && Mathf.Abs(unit.localPosition.y - asset.Plain.Attack) < 1e-4f, "attack unit on GripSocket +Y " + F(unit != null ? unit.localPosition.y : float.NaN));
                var renderer = bands.RendererOf(BandRegion308.Attack);
                Expect(renderer != null && renderer.sharedMaterials.Length == 2 && renderer.sharedMaterials[0] == asset.PigmentOf(Element.Metal) && renderer.sharedMaterials[1] == asset.Paper, "선 = metal pigment + paper underline");
                t += 2f;
                clock.Cast('섬', t, 30f); bands.Step(t);
                Expect(bands.LetterOf(BandRegion308.Attack) == '섬' && bands.WoundOf(BandRegion308.Attack) < .1f, "섬 (newer, same place) covers 선 and winds on again");
                clock.Cast('건', t, 30f); clock.Cast('억', t, 30f);
                bands.Step(t + 1f);
                int drawn = Regions().Count(r => bands.RendererOf(r) != null && bands.RendererOf(r).enabled);
                Expect(drawn == 3 && bands.LetterOf(BandRegion308.Body) == '건' && bands.LetterOf(BandRegion308.Ink) == '억', "three places at once: 섬 · 건 · 억 (" + drawn + " drawn)");
                clock.Cast('넌', t - 1f, 30f); bands.Step(t + 1.1f);
                Expect(bands.LetterOf(BandRegion308.Attack) == '섬', "an older buff of the same place stays under the newer one");
                clock.End('섬'); bands.Step(t + 1.2f);
                Expect(bands.LetterOf(BandRegion308.Attack) == '넌', "섬 ends: the newest still running (넌) takes the place back");
                t += 3f;
                clock.Cast('섬', t, 30f); bands.Step(t + .01f); bands.Step(t + 1f);
                clock.Cast('섬', t + 1f, 30f); bands.Step(t + 1.01f);
                Expect(bands.WoundOf(BandRegion308.Attack) < .1f, "a recast of the band shown winds it on again");
                // the form: a weapon head on the grip moves every band below the hand
                var head = AssetDatabase.LoadAssetAtPath<GameObject>(SpearHead);
                if (head == null) Warn("no spear head prefab: form not checked");
                else
                {
                    var weapon = Make(head).GetComponent<GreatBrushWeapon308>();
                    weapon.Attach(brush.transform); bands.Step(t + 2f);
                    Expect(bands.Formed() && Regions().All(r => Mathf.Abs(bands.UnitOf(r).localPosition.y - asset.Form.Of(r)) < 1e-4f), "form: units at " + string.Join(" · ", Regions().Select(r => r + " " + F(bands.UnitOf(r).localPosition.y))));
                    weapon.Detach(true); bands.Step(t + 2.1f);
                    Expect(Regions().All(r => Mathf.Abs(bands.UnitOf(r).localPosition.y - asset.Plain.Of(r)) < 1e-4f), "head off: units back above the hand");
                }
                // mirror: a hidden brush hides its bands
                var body = brush.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0));
                if (body != null)
                {
                    body.enabled = false; bands.Step(t + 2.2f);
                    Expect(Regions().All(r => !bands.RendererOf(r).enabled), "brush hidden: bands hidden");
                    body.enabled = true; bands.Step(t + 2.3f);
                    Expect(Regions().All(r => bands.RendererOf(r).enabled && bands.UnitOf(r).gameObject.layer == body.gameObject.layer), "brush shown: bands shown on its layer");
                }
                else Fail("no bristle renderer on the brush model");
                // all end: unwind, then gone
                t += 10f;
                clock.Runs.Clear(); bands.Step(t); bands.Step(t + asset.ExitSeconds * .5f);
                Expect(Regions().All(r => bands.WoundOf(r) > 0f && bands.WoundOf(r) < 1f), "buffs end: bands unwind over " + F(asset.ExitSeconds) + " s");
                bands.Step(t + asset.ExitSeconds + .01f); bands.Step(t + asset.ExitSeconds + .02f);
                Expect(Regions().All(r => !bands.RendererOf(r).enabled) && Regions().All(r => bands.LetterOf(r) == default), "after the unwind nothing is drawn");
                // a fresh bind shows what already runs, fully wound (no wind on for a buff cast before)
                clock.Cast('먹', t, 30f);
                bands.Bind(clock, asset); bands.AttachTo(brush.transform); bands.Step(t + 1f);
                Expect(bands.LetterOf(BandRegion308.Body) == '먹' && bands.WoundOf(BandRegion308.Body) >= 1f, "bind while 먹 runs: shown wound at once");
                Object.DestroyImmediate(bands);
                Expect(Find(brush.transform, "BuffBand308_Body") == null, "the component destroyed takes its bands with it");
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return Done(b, fail, warn);
        }

        static IEnumerable<BandRegion308> Regions() => new[] { BandRegion308.Attack, BandRegion308.Body, BandRegion308.Ink };

        static string Done(StringBuilder b, int fail, int warn) => b.AppendLine("  " + fail + " FAIL · " + warn + " WARN").ToString().TrimEnd();

        // ---------------------------------------------------------------- stills

        static string Stills()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BuffBands308SO>(AssetPath);
            var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (asset == null || brushModel == null) return "refused: no asset or brush model";
            var b = new StringBuilder("BuffBands308 stills\n");
            Directory.CreateDirectory(Source);
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>(); var meshes = new List<Mesh>();
            var rt = new RenderTexture(1600, 1000, 24); var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            GameObject Make(GameObject source) { var o = (GameObject)Object.Instantiate(source); o.hideFlags = HideFlags.HideAndDontSave; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene); made.Add(o); return o; }
            GameObject Empty(string name) { var o = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene); made.Add(o); return o; }
            try
            {
                var light = Empty("Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.color = new Color(1f, .97f, .9f); light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
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
                var clock = new Clock(); float t = 50f;
                // one buff per place, three elements: metal attack, wood body, water ink (the darkest pigment)
                clock.Cast('선', t, 60f); clock.Cast('건', t, 60f); clock.Cast('억', t, 60f);
                var columns = new List<GameObject>();
                for (int i = 0; i < 2; i++)
                {
                    var brush = Brush();
                    if (i == 1)
                    {
                        var head = AssetDatabase.LoadAssetAtPath<GameObject>(SpearHead);
                        if (head != null) Make(head).GetComponent<GreatBrushWeapon308>().Attach(brush.transform);
                    }
                    var bands = Empty("Bands" + i).AddComponent<AuraBrushBands308>();
                    bands.Bind(clock, asset); bands.AttachTo(brush.transform); bands.Step(t + 5f);
                    var tr = brush.transform; var grip = Find(tr, GreatBrushWeapon308.GripName);
                    // the shaft upright, grips on one line 1.0 m up
                    tr.rotation = Quaternion.Inverse(Quaternion.Inverse(tr.rotation) * grip.rotation);
                    tr.position += new Vector3(i * .9f, 1.0f, 0f) - grip.position;
                    foreach (var sk in tr.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave }; sk.BakeMesh(baked, true); meshes.Add(baked);
                        var copy = Empty(sk.name + "_baked"); copy.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                        copy.AddComponent<MeshFilter>().sharedMesh = baked; copy.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; sk.enabled = false;
                    }
                    columns.Add(brush);
                }
                var cam = Empty("Camera").AddComponent<Camera>(); cam.scene = scene; cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.74f, .71f, .64f); cam.nearClipPlane = .05f; cam.farClipPlane = 40f; cam.fieldOfView = 32f;
                void Shot(string name, Vector3 at, Vector3 look, float fov)
                {
                    cam.fieldOfView = fov; cam.transform.position = at; cam.transform.LookAt(look);
                    cam.Render(); var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = old;
                    string file = Path.Combine(Source, name + ".png"); File.WriteAllBytes(file, tex.EncodeToPNG()); b.AppendLine("  " + file);
                }
                Vector3 centre = new Vector3(.45f, 1.0f, 0f);
                // twice: the first still after a scene switch can be wrong (first-still-after-editor-restart)
                Shot("bands_close", centre + new Vector3(.4f, .25f, -2.2f), centre, 32f);
                Shot("bands_close", centre + new Vector3(.4f, .25f, -2.2f), centre, 32f);
                Shot("bands_3m", centre + new Vector3(0f, .6f, -3f), centre, 50f);
                Shot("bands_8m", centre + new Vector3(0f, 1.2f, -8f), centre, 50f);
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- rig (read only)

        // Where the player's hands are along each brush's GripSocket +Y in the open scenes (the pose the scene was saved in), next to
        // the asset's hand zones: the layout gate assumes them.
        static string Rig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BuffBands308SO>(AssetPath);
            var b = new StringBuilder("BuffBands308 rig (read only)\n");
            if (asset != null) b.AppendLine("  asset zones: upper hand " + F(asset.UpperHand) + " ± " + F(asset.HandHalf) + " · lower hand " + F(asset.LowerHand) + " · neck " + F(asset.Ferrule) + " · butt " + F(asset.Butt));
            foreach (var rig in Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var animator = rig.GetComponentInParent<Animator>();
                if (animator == null || !animator.isHuman) animator = rig.transform.root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.isHuman);
                b.AppendLine("  rig " + rig.gameObject.scene.name + "/" + rig.name + (animator != null ? " · animator " + animator.name : " · no humanoid animator"));
                foreach (var (label, root) in new[] { ("world", rig.WorldBrushRoot308), ("near", rig.NearBrushRoot308) })
                {
                    var grip = root != null ? Find(root, GreatBrushWeapon308.GripName) : null;
                    if (grip == null) { b.AppendLine("    " + label + ": no grip"); continue; }
                    var line = new StringBuilder("    " + label + ":");
                    foreach (var socket in new[] { "GripSocketL", GreatBrushWeapon308.FerruleName, "ShaftEndSocket" })
                    { var t = Find(root, socket); line.Append(" " + socket + " " + (t != null ? F(grip.InverseTransformPoint(t.position).y) : "-")); }
                    if (animator != null)
                        foreach (var bone in new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.LeftMiddleProximal })
                        {
                            var t = animator.GetBoneTransform(bone); if (t == null) continue;
                            var p = grip.InverseTransformPoint(t.position);
                            line.Append(" · " + bone + " " + F(p.y) + " (off the shaft " + F(new Vector2(p.x, p.z).magnitude) + ")");
                        }
                    b.AppendLine(line.ToString());
                }
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- helpers

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { var found = Find(child, name); if (found != null) return found; }
            return null;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
        static string Hex6(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static string F(float v) => v.ToString("0.###", Inv);
    }
}
