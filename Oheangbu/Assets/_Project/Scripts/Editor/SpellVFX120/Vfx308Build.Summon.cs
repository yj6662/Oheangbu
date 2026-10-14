using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.13 (D308-83b, user 2026-10-14 "소환수도 전체적으로 이런형태로" + a picture): the ink body
    // of a summon. A black ink silhouette whose outline bleeds, ink smoke streaming off it, one pale part (the deer's
    // antlers) and one core of element pigment in the chest that only lights for a moment (D308-83c).
    // First prototype: the wood deer (곰). The existing static model is reused - only its materials change - so the
    // prefab keeps the parts WoodDeerVfx asks for (the four Foot_ anchors, a renderer named *Roots*). Not connected.
    //
    //   summon                         build PF_V308c_summon_deer (+ materials)
    //   summon:pose:deer:<age>[:glow]  edit-mode pose at <age> seconds of the summon clock (glow = the core lifted);
    //                                  optional :<px,py,pz>:<fx,fy,fz> = where it stands and faces
    //   summon:pose:clear
    public static partial class Vfx308Build
    {
        const string DeerSource = "Assets/_Project/Art/SpellVFX120/WoodDeer/PF_WoodDeer_Static.prefab";
        const string DeerProfile = "Assets/_Project/Art/SpellVFX120/Profiles/016_ACF0.asset";
        const string SummonPoseName = "__Vfx308SummonPose";
        static string SummonPrefabPath(string key) => Root2 + "/Prefabs/PF_V308c_summon_" + key + ".prefab";

        static Material InkBody(string name, Color core, float accentFrom, Vector3 corePos, float coreRadius, float life)
        {
            string path = Root2 + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Oheangbu/SpellVFX308/InkBody") ?? throw new Exception("InkBody shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_Core", core); m.SetFloat("_AccentFrom", accentFrom); m.SetVector("_CorePos", corePos); m.SetFloat("_CoreRadius", coreRadius);
            m.SetFloat("_Life", life); m.SetFloat("_CoreGlow", 0f);
            // 2nd pass (user: liquid, not solid): no eaten outline (that reads dry), a running surface and a wet sheen instead
            m.SetFloat("_Edge", 0f); m.SetFloat("_GrainScale", 6.5f); m.SetFloat("_WashAmount", .12f); m.SetFloat("_Wobble", .01f);
            m.SetFloat("_Flow", .035f); m.SetFloat("_Sag", .04f); m.SetFloat("_Gloss", .65f);     // the mesh already hangs: the shader only keeps it moving
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        static Bounds WorldBounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static string BuildSummon()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build summon: Edit mode only");
            V2 = true; Touched.Clear(); seed = 31000;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            AssetDatabase.Refresh();
            foreach (var sheet in new[] { "ink_wisp", "ink_drops", "ink_pool" }) Sheet2(sheet);
            var profile = Load<Vfx120Profile>(DeerProfile);
            var source = Load<GameObject>(DeerSource);
            var root = new GameObject("PF_V308c_summon_deer");
            var model = (GameObject)Object.Instantiate(source, root.transform); model.name = source.name;
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            var body = model.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name.Contains("Body"));
            var all = WorldBounds(model);
            var bb = body.bounds;
            // the chest: on the body's midline, a little forward of the middle, a little above half the body's height
            Vector3 chestWorld = new Vector3(bb.center.x, bb.min.y + bb.size.y * .5f, bb.center.z + bb.size.z * .16f);
            Vector3 chest = body.transform.InverseTransformPoint(chestWorld);
            // the element's pigment, a little cleaner than the wash colour (LDR; the shader caps the momentary lift)
            Color.RGBToHSV(profile.Pigment, out float hue, out float sat, out float val);
            Color core = Color.HSVToRGB(hue, Mathf.Min(1f, sat * 1.25f), Mathf.Min(.42f, val * 1.05f));
            float accentFrom = (bb.min.y + bb.size.y * .8f - all.min.y) / Mathf.Max(.1f, all.size.y);
            var ink = InkBody("M_V308c_summon_deer_body", core, accentFrom, chest, bb.size.y * .11f, WoodDeerVfx.ReviewLife);
            var sb = new StringBuilder();
            // 2nd pass (user: a mass of liquid, not a solid): the body mesh itself is the melted one (Tools/Blender/SpellVFX308/
            // inkbody308.py - remeshed into one skin, undersides hanging, drops on threads). It is fitted to where the modelled
            // body stood (same height, same feet), and the modelled body is switched off - its object stays for the seam.
            var melted = Model("inkdeer");
            var liquid = new GameObject("InkBody"); liquid.transform.SetParent(model.transform, false);
            liquid.AddComponent<MeshFilter>().sharedMesh = melted;
            var liquidRenderer = liquid.AddComponent<MeshRenderer>(); liquidRenderer.sharedMaterial = ink;
            liquid.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lb = liquidRenderer.bounds;
            if ((lb.size.x > lb.size.z) != (bb.size.x > bb.size.z)) { liquid.transform.rotation = Quaternion.Euler(0f, 90f, 0f); lb = liquidRenderer.bounds; }
            float fit = bb.size.y / Mathf.Max(.01f, lb.size.y);
            liquid.transform.localScale *= fit; lb = liquidRenderer.bounds;
            liquid.transform.position += new Vector3(bb.center.x - lb.center.x, bb.max.y - lb.max.y, bb.center.z - lb.center.z);     // the antlers and the back stay where they were; the hang goes below
            ink.SetVector("_CorePos", liquid.transform.InverseTransformPoint(chestWorld)); EditorUtility.SetDirty(ink);     // the core is given in the drawn mesh's own space
            sb.AppendLine("  InkBody <- " + melted.name + " (" + melted.triangles.Length / 3 + " tris), fit x" + fit.ToString("0.000", CultureInfo.InvariantCulture) + ", yaw " + liquid.transform.eulerAngles.y.ToString("0", CultureInfo.InvariantCulture));
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == liquidRenderer) { }
                else if (r.name.Contains("Body")) r.enabled = false;
                else if (r.name.Contains("Leaves")) r.enabled = false;           // no leaves: one pale part only (the antlers)
                else if (r.name.Contains("Roots")) r.enabled = false;            // no bark roots: the ink pool is the ground it stands on
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sb.AppendLine("  " + r.name + " -> " + (r.enabled ? r.sharedMaterial.name : "off"));
            }
            // ink smoke off the whole body: it leaves backward and up, the root of each strand at the body
            var wisp = Bleed("M_V308c_summon_ink_wisp", "ink_wisp", C(SwingDark, .95f), 0f, .95f, 0f, .06f);
            var wisps = Layer(root.transform, new L { Name = "Wisps", Mat = wisp, Mode = 3, Rate = 7f, Max = 16, Life = new Vector2(1.1f, 1.8f), Size = new Vector2(1.0f, 1.6f), Speed = new Vector2(.05f, .2f), SpinRandom = false, Blend = true,
                RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Colour = G((0f, C(SwingDark, 0f)), (.2f, C(SwingDark, .9f)), (.6f, C(SwingDark, .7f)), (1f, C(SwingDark, 0f))), Grow = Curve(0f, .6f, 1f, 1.5f) });
            // a box over the trunk and neck (a mesh-renderer shape darkened the whole view in the first stills - particles far from the body)
            var shape = wisps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            // a stretched strand trails BEHIND its particle (toward the front here, the particle moves back): start a strand's length behind the body so its root reaches the body
            shape.position = root.transform.InverseTransformPoint(new Vector3(bb.center.x, bb.min.y + bb.size.y * .6f, bb.center.z - 1.0f)); shape.scale = new Vector3(bb.size.x * .35f, bb.size.y * .45f, bb.size.z * .55f);
            var vel = wisps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-.15f, .15f); vel.y = new ParticleSystem.MinMaxCurve(.3f, .8f); vel.z = new ParticleSystem.MinMaxCurve(-1.7f, -.8f);
            var main = wisps.main; main.startDelay = .9f;                         // once the body has risen
            wisps.GetComponent<ParticleSystemRenderer>().flip = new Vector3(1f, 0f, 0f);     // the sheet's root is on its right: put it at the body
            // a drop now and then from the belly
            var drips = Layer(root.transform, new L { Name = "Drips", Mat = Bleed("M_V308c_summon_ink_drops", "ink_drops", C(SwingDark, .9f), 0f, .9f), Rate = 9f, Max = 14, Life = new Vector2(.55f, .9f), Size = new Vector2(.2f, .46f), Gravity = 1.9f, SpinRandom = false, RandomCell = true, Blend = true,
                Colour = InkFade(.5f) });
            var ds = drips.shape; ds.enabled = true; ds.shapeType = ParticleSystemShapeType.Box; ds.position = root.transform.InverseTransformPoint(new Vector3(bb.center.x, bb.min.y + bb.size.y * .42f, bb.center.z)); ds.scale = new Vector3(bb.size.x * .4f, .05f, bb.size.z * .45f);
            var dm = drips.main; dm.startDelay = 1.1f;
            // the ink it rises out of
            Layer(root.transform, new L { Name = "Pool", Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = new Vector2(1.9f, 1.9f), Size = new Vector2(3.4f, 3.4f), NoShape = true, Blend = true,
                ShapePos = new Vector3(0f, .04f, 0f), FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFade(.6f), Grow = Curve(0f, .4f, .2f, 1f, 1f, 1.1f) });
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.layer = AfterFogLayer;     // the ink is drawn after the realm fog; the body stays a world object
            // it stands in its own ink for as long as it is there (the first pool spreads, this one stays)
            var puddle = Layer(root.transform, new L { Name = "Puddle", Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = new Vector2(3.3f, 3.3f), Size = new Vector2(3.0f, 3.0f), NoShape = true, Blend = true,
                ShapePos = new Vector3(0f, .05f, 0f), FrameFrom = 7f, FrameTo = 10f, Colour = G((0f, C(SwingDark, 0f)), (.12f, C(SwingDark, 1f)), (.8f, C(SwingDark, 1f)), (1f, C(SwingDark, 0f))) });
            var pm = puddle.main; pm.startDelay = 1.2f;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.layer = AfterFogLayer;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SummonPrefabPath("deer"));
            Object.DestroyImmediate(root);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return "summon deer -> " + SummonPrefabPath("deer") + "\n" + sb + "  height " + all.size.y.ToString("0.00", CultureInfo.InvariantCulture) + " m, accent from " + accentFrom.ToString("0.00", CultureInfo.InvariantCulture) +
                ", core " + ColorUtility.ToHtmlStringRGB(core) + " r " + (bb.size.y * .16f).ToString("0.00", CultureInfo.InvariantCulture) + " m, particles <= " + prefab.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.main.maxParticles) + "\n  (not connected: the game still summons the bark deer)";
        }

        static void ClearSummonPose()
        {
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x != null && x.name == SummonPoseName && !EditorUtility.IsPersistent(x)).ToArray())
                Object.DestroyImmediate(g);
        }

        static string Summon(string[] a)
        {
            if (a.Length == 1) return BuildSummon();
            if (a[1] != "pose") throw new ArgumentException("summon | summon:pose:deer:<age>[:glow] | summon:pose:clear");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build summon pose: Edit mode only");
            ClearSummonPose();
            if (a.Length > 2 && a[2] == "clear") return "summon pose cleared";
            if (a.Length < 4) throw new ArgumentException("summon:pose:deer:<age>[:glow][:px,py,pz:fx,fy,fz]");
            float age = float.Parse(a[3], CultureInfo.InvariantCulture);
            bool glow = a.Contains("glow");
            var places = a.Skip(4).Where(x => x.Contains(",")).ToArray();
            Vector3 feet = places.Length > 0 ? Vec(places[0]) : new Vector3(3340f, 100f, 2692f);
            feet.y = Ground(feet);
            Vector3 forward = places.Length > 1 ? Vec(places[1]) : Vector3.right;
            forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;
            var prefab = Load<GameObject>(SummonPrefabPath(a[2]));
            var root = Object.Instantiate(prefab, feet, Quaternion.LookRotation(forward, Vector3.up)); root.name = SummonPoseName;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var bounds = WorldBounds(root);
            var block = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetFloat("_Age", age); block.SetFloat("_Ground", feet.y); block.SetFloat("_Height", bounds.max.y - feet.y); block.SetFloat("_CoreGlow", glow ? 1f : 0f);
                r.SetPropertyBlock(block);
            }
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            const float step = 1f / 60f;
            for (float t = 0f; t + step <= age + .00001f; t += step) foreach (var ps in systems) ps.Simulate(step, false, false, false);
            SceneView.RepaintAll();
            return "summon pose " + a[2] + " at " + age.ToString("0.00", CultureInfo.InvariantCulture) + " s" + (glow ? " (core lifted)" : "") + ", feet " + feet + ", height " + (bounds.max.y - feet.y).ToString("0.00", CultureInfo.InvariantCulture) +
                ", particles " + systems.Sum(p => p.particleCount);
        }
    }
}
