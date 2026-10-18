using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.61, the seal the install letters (감 남 맘 삼 암) leave on the marked enemy. The presenter
    // puts this body on the enemy at the Anchor cue (parented, at the middle of its renderers, scaled by their bounds) and
    // gives Vfx308ZoneBody the mark's life: dark for 3 s, faint after, dries on Expire or a lost target, breaks on Detonate.
    // Built at 1 m. Ink first: the ink gathers onto the enemy once, then short strokes - flat and upright, like the lines of
    // a ㅁ - keep being laid round it on a slowly turning ring, black soaking into the element pigment; a faint ink haze.
    // All local space, start speed 0 (it follows the enemy, nothing flies off). No glow, no droplets, nothing on the ground.
    // Break child = an ink splat with a little smoke; End child = a few strokes that sag and melt.
    //   mark           - build the five prefabs and point the five _308h mark profiles at them
    public static partial class Vfx308Build
    {
        static string MarkPath(string key) => Root2 + "/Prefabs/PF_V308m_" + key + ".prefab";

        static string BuildMark()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build mark: Edit mode only");
            V2 = true; Touched.Clear(); seed = 47500;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_gather", "ink_throw", "ink_smoke", "ink_splat", "ink_wisp" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var g in Cues.Where(x => x.Kind == "mark"))
            {
                var e = Element(kit, g.Key); kit.Ink = e.Ink;
                var prefab = MarkBody(e, out float dry);
                if (!File.Exists(CueProfilePath(g.Id))) { sb.AppendLine(g.Glyph + ": the cue copy is not built (run cue first) - seal " + prefab.name + " built, not assigned"); continue; }
                var profile = Load<Vfx120Profile>(CueProfilePath(g.Id));
                profile.Mark308Prefab = prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(g.Glyph + " mark -> " + MarkPath(g.Key) + " (dry " + dry.ToString("0.00") + " s) on " + CueProfilePath(g.Id));
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static GameObject MarkBody(Elem e, out float dryFade)
        {
            string path = MarkPath(e.Key);
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                var t = root.transform;
                string m = "M_V308m_" + e.Key + "_";
                Color wash = Color.Lerp(e.Pig, e.Ink, .5f), dark = Color.Lerp(e.Pig, e.Ink, .8f);
                // ink first: the ink gathers onto the enemy, once
                Layer(t, new L { Name = "Gather", Mat = Bleed(m + "gather", "ink_gather", C(e.Ink, .9f), 0f, .85f, 0f, .3f), World = false, Burst = 1, Max = 1, Life = new Vector2(.45f, .5f),
                    Size = new Vector2(1.15f, 1.25f), NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 16f, Colour = InkFade(.45f), Grow = Curve(0f, 1.15f, 1f, .85f) });
                // the seal: flat strokes laid round a slowly turning ring, black soaking into the pigment
                var stroke = SoakVariant(Bleed(m + "stroke", "ink_throw", C(wash, .95f), 0f, .9f, 0f, .3f));
                Gradient hold = G((0f, C(dark, 0f)), (.15f, C(dark, .9f)), (.7f, C(dark, .85f)), (1f, C(dark, 0f)));
                Layer(t, new L { Name = "Band", Mat = stroke, World = false, Rate = 6f, Max = 12, Life = new Vector2(1.4f, 1.7f), Size = new Vector2(.5f, .62f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .55f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ArcSpeed = .35f, SpinRandom = false, RotDeg = new Vector2(80f, 100f),
                    Blend = true, RandomCell = true, FrameFrom = 6f, FrameTo = 12f, Colour = hold, Grow = Curve(0f, .85f, .2f, 1f, 1f, 1.02f) });
                // the upright lines of the ㅁ, fewer
                Layer(t, new L { Name = "Post", Mat = stroke, World = false, Rate = 2.6f, Max = 6, Life = new Vector2(1.4f, 1.7f), Size = new Vector2(.55f, .68f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .5f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ArcSpeed = .35f, SpinRandom = false, RotDeg = new Vector2(-8f, 8f),
                    Blend = true, RandomCell = true, FrameFrom = 6f, FrameTo = 12f, Colour = hold, Grow = Curve(0f, .85f, .2f, 1f, 1f, 1.02f) });
                // a faint ink haze that hangs on the enemy
                Layer(t, new L { Name = "Haze", Mat = Bleed(m + "haze", "ink_smoke", C(e.Ink, .8f), 0f, .6f, 0f, .4f), World = false, Rate = 1.4f, Max = 4, Life = new Vector2(2f, 2.4f), Size = new Vector2(.9f, 1.1f),
                    Shape = ParticleSystemShapeType.Sphere, Radius = .25f, Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 6f,
                    Colour = G((0f, C(wash, 0f)), (.3f, C(wash, .25f)), (1f, C(wash, 0f))), Grow = Curve(0f, .8f, 1f, 1.1f) });
                // a detonated seal: one ink splat and a little smoke, once
                var brk = new GameObject("Break").transform; brk.SetParent(t, false);
                Layer(brk, new L { Name = "Splat", Mat = Bleed(m + "splat", "ink_splat", C(e.Ink, .95f), 0f, .9f, 0f, .3f), World = false, Burst = 1, Max = 1, Life = new Vector2(.55f, .65f),
                    Size = new Vector2(1.5f, 1.7f), NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 14f, Colour = G((0f, C(e.Ink, .95f)), (.4f, C(dark, .7f)), (1f, C(dark, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1.1f) });
                Layer(brk, new L { Name = "Smoke", Mat = Bleed(m + "smoke", "ink_smoke", C(e.Ink, .8f), 0f, .6f, 0f, .4f), World = false, Burst = 2, Max = 2, Life = new Vector2(.8f, 1f),
                    Size = new Vector2(1.1f, 1.3f), Shape = ParticleSystemShapeType.Sphere, Radius = .2f, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                    Colour = G((0f, C(dark, .5f)), (1f, C(dark, 0f))), Grow = Curve(0f, .7f, 1f, 1.2f) });
                // a dried seal: a few strokes sag and melt
                var end = new GameObject("End").transform; end.SetParent(t, false);
                Layer(end, new L { Name = "Sag", Mat = Bleed(m + "sag", "ink_wisp", C(dark, .9f), 0f, .8f, 0f, .3f), World = false, Burst = 4, Max = 4, Life = new Vector2(.8f, 1f), Size = new Vector2(.45f, .55f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .5f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = .04f, SpinRandom = false, RotDeg = new Vector2(-10f, 10f),
                    Blend = true, FrameFrom = 4f, FrameTo = 14f, Colour = Melt(dark, .3f), Grow = Curve(0f, 1f, 1f, .9f) });

                dryFade = 1.2f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 3f; body.LateAlpha = .35f; body.DryFade = dryFade;
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
