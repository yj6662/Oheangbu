using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-86 (SPEC-SPELL-VFX-OVERHAUL-308 11.24): a single's flying body is not a modelled thing (thorn / coal / clod / blade /
    // bead) but ink that keeps forming: overlapping bleed cels born and spreading in a few cels each (Mass), one small dark
    // heart (Core), and ink breath dropped behind it (Shed). Mass and Core sit under Travel/Spin, so the runtime hides them at
    // the hit as it hid the meshes; Shed is world space under Travel and dries where it fell. The element is in the grain, the
    // motion and the pigment, not in an object's outline. No light (ART-INK).
    public static partial class Vfx308Build
    {
        static Transform InkMass(Transform travel, string key, Color ink, Color tone, Color dark, float size, string massSheet = "ink_splat",
            float rate = 28f, float spin = 70f, float life = .3f, float shed = 34f)
        {
            var root = SpinRoot(travel);
            var lives = new Vector2(life * .66f, life);
            Layer(root, new L { Name = "Mass", Mat = Bleed("M_V308b_" + key + "_body_mass", massSheet, C(ink, .95f), 0f, .95f), World = false, Rate = rate, Max = Mathf.CeilToInt(rate * life) + 4,
                Life = lives, Size = new Vector2(size * .75f, size * 1.05f), Radius = .08f * size, Speed = new Vector2(0f, .15f), Spin = spin, Blend = true, FrameFrom = 0f, FrameTo = 9f,
                Colour = G((0f, C(dark, 0f)), (.12f, C(dark, 1f)), (.55f, C(tone, .9f)), (1f, C(tone, 0f))), Grow = Curve(0f, .55f, .4f, 1f, 1f, 1.15f) });
            Layer(root, new L { Name = "Core", Mat = Bleed("M_V308b_" + key + "_body_core", "ink_drops", C(ink, 1f)), World = false, Rate = 40f, Max = 8, Life = new Vector2(.1f, .16f),
                Size = new Vector2(size * .35f, size * .5f), Radius = .03f, RandomCell = true, Blend = true, Colour = G((0f, C(dark, 1f)), (.6f, C(dark, .9f)), (1f, C(dark, 0f))) });
            if (shed > 0f)
                Layer(travel, new L { Name = "Shed", Mat = Bleed("M_V308b_" + key + "_body_shed", "ink_wisp", C(ink, .8f), 0f, .8f), Rate = shed, Max = Mathf.CeilToInt(shed * .55f) + 4,
                    Life = new Vector2(.35f, .55f), Size = new Vector2(size * .5f, size * .8f), Radius = .12f * size, Speed = new Vector2(.1f, .4f), RandomStart = true, FrameTo = 8f, Blend = true,
                    Colour = Fade(Color.Lerp(dark, tone, .4f), .2f, dark), Grow = Curve(0f, .7f, 1f, 1.8f) });
            return root;
        }
    }
}
