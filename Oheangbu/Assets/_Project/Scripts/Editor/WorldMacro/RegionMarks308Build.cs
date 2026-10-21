using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-127 (debate 11 A + g2, debate 14 - TEST): the enemy body-part marks. Editor queue entry:
    //   Run("asset")         RegionMarks308.asset + its PigmentTrail308 material (the pigment is the paper; no emission)
    //   Run("connect[:dry]") the visual sets outside the protected trees point at it (SpellVisualSetSO.RegionMarks308)
    //   Run("check")         priority and the two-part cap, the drawings' sizes (g2), anchors on a Humanoid body (bandit) and a
    //                        four-legged one (wolf), and the marks' life on those bodies in a preview scene (no Awake, no Play)
    //   Run("stills")        both bodies with pairs of parts, at 3 m and 15 m
    public static class RegionMarks308Build
    {
        const string Folder = "Assets/_Project/Art/Characters/Creatures308/RegionMarks308";
        const string AssetPath = Folder + "/RegionMarks308.asset", MaterialPath = Folder + "/M_RegionMark308.mat";
        const string Shader308 = "Oheangbu/PigmentTrail308";
        const string Bandit = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_bandit.prefab";
        const string Wolf = "Assets/_Project/Art/Characters/Creatures308/PF_Creature308_wolf.prefab";
        static readonly string[] Protected = { "Watershed295", "Reworld292", "MountainTrail285", "W_Demo_Compact", "03_Content" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Source => Path.Combine(Repo, "Art", "Characters308", "RegionMarks308", "v1");

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
                }
                return "RegionMarks308Build: asset | connect[:dry] | check | stills";
            }
            catch (Exception e) { return "RegionMarks308Build exception: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0].Trim(); }
        }

        // ---------------------------------------------------------------- asset

        static string MakeAsset()
        {
            var shader = Shader.Find(Shader308);
            if (shader == null) return "refused: shader missing " + Shader308;
            EnsureFolder(Folder);
            var b = new StringBuilder("RegionMarks308 asset\n");
            var defaults = ScriptableObject.CreateInstance<RegionMarks308SO>();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath); bool made = material == null;
            if (made) material = new Material(shader) { name = "M_RegionMark308" };
            material.shader = shader; material.shaderKeywords = Array.Empty<string>(); material.enableInstancing = true;
            // the pigment is the paper (rim share 1 = no ink core); ink parts darken it by their vertex colour
            material.SetColor("_PigmentColor", defaults.Paper); material.SetColor("_InkColor", defaults.Ink); material.SetFloat("_RimShare", 1f);
            material.SetFloat("_Opacity", 1f); material.SetFloat("_StreakDensity", 10f); material.SetFloat("_DryStrength", .55f); material.SetFloat("_EdgeSoftness", .15f);
            if (made) AssetDatabase.CreateAsset(material, MaterialPath); else EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            var asset = AssetDatabase.LoadAssetAtPath<RegionMarks308SO>(AssetPath); bool fresh = asset == null;
            if (fresh) { asset = defaults; defaults = null; }
            else
            {
                // the code's defaults are written again on every run
                asset.Paper = defaults.Paper; asset.Ink = defaults.Ink; asset.Priority = defaults.Priority; asset.MaxRegions = defaults.MaxRegions;
                asset.Capacity = defaults.Capacity; asset.FadeIn = defaults.FadeIn; asset.FadeOut = defaults.FadeOut; asset.ReferenceHeight = defaults.ReferenceHeight;
            }
            asset.Material = material;
            if (fresh) AssetDatabase.CreateAsset(asset, AssetPath); else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            if (defaults != null) Object.DestroyImmediate(defaults);
            b.AppendLine("  " + MaterialPath + (made ? " created" : " updated"));
            b.AppendLine("  " + AssetPath + (fresh ? " created" : " updated"));
            return b.ToString().TrimEnd();
        }

        static string Connect(bool dry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<RegionMarks308SO>(AssetPath);
            if (asset == null) return "refused: run asset first";
            var b = new StringBuilder("RegionMarks308 connect" + (dry ? ":dry" : "") + "\n");
            foreach (string guid in AssetDatabase.FindAssets("t:SpellVisualSetSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(path);
                if (set == null) continue;
                if (Protected.Any(p => path.IndexOf(p, StringComparison.Ordinal) >= 0)) { b.AppendLine("  " + path + " protected tree: left alone"); continue; }
                bool already = set.RegionMarks308 == asset;
                b.AppendLine("  " + path + (already ? " (already)" : dry ? " (dry)" : " -> RegionMarks308"));
                if (dry || already) continue;
                set.RegionMarks308 = asset; EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            }
            return b.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- check

        static string Check()
        {
            var b = new StringBuilder("RegionMarks308 check\n"); int fail = 0, warn = 0;
            void Expect(bool ok, string m) { if (ok) b.AppendLine("  PASS " + m); else { fail++; b.AppendLine("  FAIL " + m); } }
            void Warn(string m) { warn++; b.AppendLine("  WARN " + m); }
            var asset = AssetDatabase.LoadAssetAtPath<RegionMarks308SO>(AssetPath);
            if (asset == null) return "FAIL: no " + AssetPath + " (run asset)";

            // data and material
            Expect(asset.Material != null && asset.Material.shader != null && asset.Material.shader.name == Shader308, "material on " + Shader308 + " (unlit alpha blend, no emission term)");
            if (asset.Material != null)
            {
                var pigment = asset.Material.GetColor("_PigmentColor");
                Expect(pigment.grayscale <= asset.Paper.grayscale + 1e-3f && pigment.maxColorComponent <= 1f, "pigment " + Hex6(pigment) + " = the paper, LDR (the brightest a mark gets)");
                Expect(Mathf.Approximately(asset.Material.GetFloat("_RimShare"), 1f), "rim share 1: the whole width is the vertex-coloured pigment");
            }
            Color inkOut = (asset.Paper.linear * asset.InkTint).gamma;
            Expect(Mathf.Abs(inkOut.r - asset.Ink.r) < .01f && Mathf.Abs(inkOut.g - asset.Ink.g) < .01f, "ink parts come out " + Hex6(inkOut) + " (asked " + Hex6(asset.Ink) + ")");
            var expected = new[] { PresentState308.Bound, PresentState308.Exposed, PresentState308.Armour, PresentState308.Weakened, PresentState308.Slowed, PresentState308.Blind };
            Expect(asset.Priority != null && asset.Priority.SequenceEqual(expected), "priority Bound > Exposed > Armour > Weakened > Slowed > Blind");
            Expect(asset.MaxRegions == 2, "at most two parts on one enemy");
            Expect(RegionMarks308SO.RegionBits == (PresentState308.Bound | PresentState308.Slowed | PresentState308.Weakened | PresentState308.Armour | PresentState308.Exposed | PresentState308.Blind), "six readings move off the chest mark (guard, broken guard, root, brand stay)");

            // the pick
            var into = new BodyRegion308[3];
            string Picked(PresentState308 state) { int n = RegionMarks308SO.Pick(state, asset.Priority, asset.MaxRegions, into); return string.Join("+", into.Take(n)); }
            Expect(Picked(PresentState308.Bound | PresentState308.Exposed | PresentState308.Armour | PresentState308.Slowed) == "Ankles+Collar", "bound + exposed + armour + slowed -> ankles + collar (" + Picked(PresentState308.Bound | PresentState308.Exposed | PresentState308.Armour | PresentState308.Slowed) + ")");
            Expect(Picked(PresentState308.Weakened | PresentState308.Slowed | PresentState308.Blind) == "WeaponHand+Legs", "weakened + slowed + blind -> weapon hand + legs");
            Expect(Picked(PresentState308.Guarded | PresentState308.Root | PresentState308.Sin) == "", "guard, root and brand: no part");
            Expect(Picked(PresentState308.Armour) == "Chest", "armour alone -> chest");

            // the drawings at s = 1 (a 1.8 m body)
            var buffer = new InkRegionForms308.Buffer();
            Color ink = asset.InkTint;
            RegionMarkStats308 Draw(BodyRegion308 r) => InkRegionForms308.Compose(r, 1f, ink, buffer);
            var legs = Draw(BodyRegion308.Legs);
            Expect(legs.MinWidth >= .12f - 1e-4f && legs.Min.y <= .05f && legs.Max.y >= .5f, "legs: runs >= 12 cm wide (" + F(legs.MinWidth) + ") from the knee (" + F(legs.Max.y) + ") to the ground");
            var hand = Draw(BodyRegion308.WeaponHand);
            Expect(hand.DiscDiameter >= .4f - 1e-4f && hand.Min.y < -.3f, "weapon hand: a blot " + F(hand.DiscDiameter) + " m (two heads) running down " + F(-hand.Min.y) + " m");
            var chest = Draw(BodyRegion308.Chest);
            float across = (chest.Max.x - chest.Min.x) / .45f;
            Expect(chest.MinWidth >= .03f - 1e-4f && chest.PaperParts == chest.Strokes && across >= .4f && across <= .6f, "chest: paper cracks >= 3 cm (" + F(chest.MinWidth) + "), " + (across * 100f).ToString("0", Inv) + " % of a .45 m chest across");
            var collar = Draw(BodyRegion308.Collar);
            Expect(Mathf.Abs(collar.DiscDiameter - .3f) < .01f && collar.PaperParts == 1 && collar.InkParts >= 2, "collar: a paper disc " + F(collar.DiscDiameter) + " m with a dark seal and runs");
            var head = Draw(BodyRegion308.Head); var ankles = Draw(BodyRegion308.Ankles);
            Expect(head.Strokes >= 3 && head.InkParts == head.Strokes && ankles.Strokes == 4 && ankles.Max.y <= .2f, "head band and ankle rings: ink only, the rings under .2 m");
            float big = InkRegionForms308.Compose(BodyRegion308.Collar, 2f, ink, buffer).DiscDiameter;
            Expect(Mathf.Abs(big - .6f) < .01f, "drawings scale with the body (a 3.6 m body: disc " + F(big) + " m)");

            // anchors and the marks' life on real bodies, in a preview scene
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>();
            GameObject Make(string path)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (prefab == null) return null;
                var o = (GameObject)Object.Instantiate(prefab); o.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene); made.Add(o); return o;
            }
            EnemyRegionMarks308 marks = null;
            try
            {
                var bandit = Make(Bandit); var wolf = Make(Wolf);
                if (bandit == null || wolf == null) { Warn("body prefabs missing: anchors not checked"); return Done(b, fail, warn); }
                bandit.transform.position = Vector3.zero; wolf.transform.position = new Vector3(3f, 0f, 0f);
                var ab = EnemyRegionMarks308.Resolve(bandit.transform);
                b.AppendLine("  bandit: weapon " + ab.WeaponFrom + " · head " + ab.HeadFrom + " · chest " + ab.ChestFrom + " · height " + F(ab.Height) + " · feet " + F(ab.Feet));
                Expect(ab.Weapon != null && ab.WeaponFrom != "fallback" && ab.Head != null && ab.Chest != null, "bandit (Humanoid): weapon hand, head and chest found on the body");
                Expect(ab.Height > 1.3f && ab.Height < 2.2f, "bandit height " + F(ab.Height) + " m (feet to the head's top)");                var aw = EnemyRegionMarks308.Resolve(wolf.transform);
                b.AppendLine("  wolf: weapon " + aw.WeaponFrom + " · head " + aw.HeadFrom + " · chest " + aw.ChestFrom + " · height " + F(aw.Height));
                Expect(aw.Height > .4f && aw.Height < 1.4f, "wolf height " + F(aw.Height) + " m");
                Expect(aw.Head != null && aw.Weapon != null, "wolf (four legs): head found, the weapon part is its jaw or head (" + aw.WeaponFrom + ")");
                if (!aw.WeaponFrom.Contains("jaw") && !aw.WeaponFrom.Contains("Mouth")) Warn("wolf: no jaw bone or Mouth organ - the weakened blot sits on the head");

                var host = new GameObject("RegionMarks308Check") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene); made.Add(host);
                marks = new EnemyRegionMarks308(host.transform, asset, 0);
                Vector3 eye = new Vector3(0f, 1.6f, -4f);
                float t = 10f;
                marks.Watch(bandit.transform, PresentState308.Slowed | PresentState308.Weakened | PresentState308.Exposed, .5f, t);
                marks.Sample(t + .1f, eye);
                var collarMark = marks.Find(bandit.transform, BodyRegion308.Collar); var handMark = marks.Find(bandit.transform, BodyRegion308.WeaponHand);
                Expect(marks.ActiveCount == 2 && collarMark != null && handMark != null && marks.Find(bandit.transform, BodyRegion308.Legs) == null, "exposed + weakened + slowed: collar and weapon hand shown, legs folded away");
                Expect(handMark != null && handMark.Carrier == ab.Weapon, "the weapon-hand mark is carried by the hand");
                Expect(handMark != null && handMark.Opacity > 0f && handMark.Opacity < 1f, "fades in (" + F(handMark != null ? handMark.Opacity : -1f) + " at .1 s)");
                marks.Sample(t + 1f, eye);
                if (handMark != null)
                {
                    Vector3 toEye = eye - ab.Weapon.position; toEye.y = 0f;
                    float stepped = Vector3.Dot(handMark.transform.position - ab.Weapon.position, toEye.normalized);
                    Expect(stepped > .05f && handMark.Opacity >= 1f, "stands " + F(stepped) + " m toward the eye from the hand, fully shown");
                    Expect(Vector3.Dot(handMark.transform.forward, (handMark.transform.position - eye).normalized) > .9f, "faces the eye");
                }
                marks.Watch(bandit.transform, PresentState308.Slowed, .5f, t + 2f);
                Expect(collarMark != null && handMark != null && collarMark.Releasing && handMark.Releasing && marks.Find(bandit.transform, BodyRegion308.Legs) != null, "readings come off: their marks fade, the legs (now in the top two) come on");
                marks.Sample(t + 2f + asset.FadeOut + .01f, eye);
                Expect(collarMark != null && handMark != null && !collarMark.Busy && !handMark.Busy, "faded marks go back to the pool");
                marks.Watch(wolf.transform, PresentState308.Weakened | PresentState308.Blind, .5f, t + 3f);
                var jaw = marks.Find(wolf.transform, BodyRegion308.WeaponHand);
                Expect(jaw != null && jaw.Carrier == aw.Weapon && marks.Find(wolf.transform, BodyRegion308.Head) != null, "wolf: weakened on " + aw.WeaponFrom + ", blind on the head");
                wolf.SetActive(false); marks.Sample(t + 3.1f, eye);
                Expect(marks.Find(wolf.transform, BodyRegion308.Head) == null, "a body switched off takes its marks with it");
                int before = marks.ActiveCount;
                marks.Watch(bandit.transform, PresentState308.None, .5f, t + 4f); marks.Sample(t + 4f + asset.FadeOut + .01f, eye);
                Expect(marks.ActiveCount == 0 && before > 0, "no reading: nothing left on the bandit");
            }
            finally
            {
                if (marks != null) marks.Destroy();
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return Done(b, fail, warn);
        }

        static string Done(StringBuilder b, int fail, int warn) => b.AppendLine("  " + fail + " FAIL · " + warn + " WARN").ToString().TrimEnd();

        // ---------------------------------------------------------------- stills

        static string Stills()
        {
            var asset = AssetDatabase.LoadAssetAtPath<RegionMarks308SO>(AssetPath);
            if (asset == null) return "refused: run asset first";
            var b = new StringBuilder("RegionMarks308 stills\n");
            Directory.CreateDirectory(Source);
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>(); var meshes = new List<Mesh>();
            var rt = new RenderTexture(1600, 1000, 24); var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            GameObject Empty(string name) { var o = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene); made.Add(o); return o; }
            EnemyRegionMarks308 marks = null;
            try
            {
                var light = Empty("Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.color = new Color(1f, .97f, .9f); light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.hideFlags = HideFlags.HideAndDontSave; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ground, scene); made.Add(ground);
                ground.transform.localScale = Vector3.one * 6f; Object.DestroyImmediate(ground.GetComponent<Collider>());
                var groundMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave }; groundMat.SetColor("_BaseColor", new Color(.70f, .66f, .58f)); ground.GetComponent<Renderer>().sharedMaterial = groundMat;
                marks = new EnemyRegionMarks308(Empty("Marks").transform, asset, 0);
                // pairs of parts (the cap is two): bandit x3, wolf x2
                var panels = new (string Body, PresentState308 State)[]
                {
                    (Bandit, PresentState308.Slowed | PresentState308.Weakened), (Bandit, PresentState308.Armour | PresentState308.Exposed), (Bandit, PresentState308.Bound | PresentState308.Blind),
                    (Wolf, PresentState308.Slowed | PresentState308.Weakened), (Wolf, PresentState308.Armour | PresentState308.Exposed),
                };
                Vector3 eye = new Vector3(4.4f, 1.6f, -3f);
                for (int i = 0; i < panels.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(panels[i].Body); if (prefab == null) continue;
                    var body = (GameObject)Object.Instantiate(prefab); body.hideFlags = HideFlags.HideAndDontSave; UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene); made.Add(body);
                    body.transform.SetPositionAndRotation(new Vector3(i * 2.2f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
                    // the body prefabs' pivots sit at mid-body: stand the feet on the ground
                    body.transform.position += Vector3.up * -EnemyRegionMarks308.Resolve(body.transform).Feet;
                    marks.Watch(body.transform, panels[i].State, .4f, 1f);
                    foreach (var sk in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
                        // (the bake takes the skin's own scale, not its parents': the wolf's .02 lives on a parent - drawn fifty
                        // times too big when the copy had none)
                        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave }; sk.BakeMesh(baked, true); meshes.Add(baked);
                        var copy = Empty(sk.name + "_baked"); copy.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                        Vector3 own = sk.transform.localScale, all = sk.transform.lossyScale;
                        copy.transform.localScale = new Vector3(all.x / Mathf.Max(1e-5f, own.x), all.y / Mathf.Max(1e-5f, own.y), all.z / Mathf.Max(1e-5f, own.z));
                        copy.AddComponent<MeshFilter>().sharedMesh = baked; copy.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials; sk.enabled = false;
                    }
                }
                var cam = Empty("Camera").AddComponent<Camera>(); cam.scene = scene; cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.74f, .71f, .64f); cam.nearClipPlane = .05f; cam.farClipPlane = 80f; cam.fieldOfView = 60f;
                void Shot(string name, Vector3 at, Vector3 look, float fov)
                {
                    marks.Sample(5f, at);   // the marks face this camera, fully shown
                    cam.fieldOfView = fov; cam.transform.position = at; cam.transform.LookAt(look);
                    cam.Render(); var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = old;
                    string file = Path.Combine(Source, name + ".png"); File.WriteAllBytes(file, tex.EncodeToPNG()); b.AppendLine("  " + file);
                }
                Vector3 centre = new Vector3(4.4f, 1.0f, 0f);
                Shot("regions_3m", centre + new Vector3(0f, .6f, -6.5f), centre, 60f);
                Shot("regions_3m", centre + new Vector3(0f, .6f, -6.5f), centre, 60f);
                for (int i = 0; i < panels.Length; i++)
                {
                    Vector3 c = new Vector3(i * 2.2f, 1f, 0f);
                    Shot("region_" + i + "_3m", c + new Vector3(0f, .6f, -3f), c, 50f);
                }
                Shot("regions_15m", centre + new Vector3(0f, 1.6f, -15f), centre, 50f);
            }
            finally
            {
                if (marks != null) marks.Destroy();
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return b.ToString().TrimEnd();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Hex6(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static string F(float v) => v.ToString("0.###", Inv);
    }
}
