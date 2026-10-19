using System;
using System.IO;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.65, the moment a field spell stands up. 국 (lift) and 뭄 (earth bank) keep their own bodies
    // (FieldSpellService / MumBridgeService, gate-checked), so each gets one short ink picture at the feet when the cast commits,
    // played through the intercept seam (ISpellBurstPresenter308.Burst -> profile.Burst308Prefab):
    //   국  at the lift base: a flat ink wash spreading and four dry wood strokes rising round its rim, shrinking away
    //   뭄  at the player's feet: a flat ink wash, three clods heaving a little and one ink clump settling on it
    // The live set still points both letters at the old catalogue prefabs, so the profile written is that prefab's
    // Vfx120Effect.Profile (SpellVFX120/Profiles/<id>.asset) - only its Burst308Prefab field. One shot, local, no glow,
    // all gone inside 1 s, nothing left behind. Sizes and timings are TEST.
    //   fieldbirth     - build the two prefabs and point the two profiles at them
    public static partial class Vfx308Build
    {
        static readonly (string id, string glyph, string key, string kind)[] FieldBirths =
        {
            ("020_AD6D", "국", "ga", "lift"),
            ("070_BB44", "뭄", "ma", "bank"),
        };

        internal static string FieldBirthProfilePath(string id) => "Assets/_Project/Art/SpellVFX120/Profiles/" + id + ".asset";

        static string BuildFieldBirth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build fieldbirth: Edit mode only");
            V2 = true; Touched.Clear(); seed = 49000;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_pool", "ink_splat" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var (id, glyph, key, kind) in FieldBirths)
            {
                var e = Element(kit, key); kit.Ink = e.Ink;
                string path = BuffHitPath(kind, key);
                var body = kind == "lift" ? LiftBody(e, kit, path) : BankBody(e, kit, path);
                var prefab = SaveWrap(body, path);
                string profilePath = FieldBirthProfilePath(id);
                var profile = File.Exists(profilePath) ? Load<Vfx120Profile>(profilePath) : null;
                if (profile == null) { sb.AppendLine(glyph + ": " + profilePath + " missing - " + prefab.name + " built, not assigned"); continue; }
                profile.Burst308Prefab = prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(glyph + " " + kind + " -> " + path + " on " + profilePath);
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        // 국: the lift takes root - a flat ink wash under the base (a little off the ground) and four dry wood strokes rising
        // from its rim, thinning as they go; the lift rises away and the picture stays on the ground until it is gone
        static GameObject LiftBody(Elem e, Kit kit, string path)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            Layer(root.transform, new L { Name = "Root", Mat = e.Pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.85f, .95f), Size = new Vector2(1.9f, 2.2f),
                Shape = ParticleSystemShapeType.Sphere, Radius = .01f, ShapePos = new Vector3(0f, .05f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(mark, .8f)), (.55f, C(mark, .6f)), (1f, C(mark, 0f))), Grow = Curve(0f, .5f, .2f, 1f, 1f, 1.05f) });
            Layer(root.transform, new L { Name = "Grain", Mat = e.Solid, Mesh = kit.Splinter, World = false, Burst = 4, BurstTime = .05f, Max = 4, Life = new Vector2(.45f, .6f),
                Size = new Vector2(.7f, .9f), Size3D = new Vector3(.3f, .3f, 1.5f), Shape = ParticleSystemShapeType.Circle, Radius = .85f, Thickness = 0f,
                ShapeRot = new Vector3(90f, 0f, 0f), ShapePos = new Vector3(0f, .05f, 0f), RiseY = 1.8f, AlongVelocity = true,
                Grow = Curve(0f, .4f, .2f, 1f, 1f, 0f) });
            OneShot(root);
            return root;
        }

        // 뭄: the ground answers - a flat ink wash at the feet, three clods heaving a little out of it and one ink clump
        // settling in the middle; round about the feet on every side, since the presenter turns it to the eye
        static GameObject BankBody(Elem e, Kit kit, string path)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            Layer(root.transform, new L { Name = "Bed", Mat = e.Pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.85f, .95f), Size = new Vector2(1.6f, 1.9f),
                Shape = ParticleSystemShapeType.Sphere, Radius = .01f, ShapePos = new Vector3(0f, .05f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(mark, .8f)), (.55f, C(mark, .6f)), (1f, C(mark, 0f))), Grow = Curve(0f, .55f, .2f, 1f, 1f, 1.04f) });
            Layer(root.transform, new L { Name = "Heave", Mat = e.Solid, Mesh = kit.Clod, World = false, Burst = 3, BurstTime = .04f, Max = 3, Life = new Vector2(.5f, .65f),
                Size = new Vector2(.22f, .32f), Shape = ParticleSystemShapeType.Circle, Radius = .6f, Thickness = 0f, ShapeRot = new Vector3(90f, 0f, 0f),
                ShapePos = new Vector3(0f, .05f, 0f), RiseY = .6f, Tumble = true, Grow = Curve(0f, .3f, .15f, 1f, .6f, 1f, 1f, 0f) });
            Layer(root.transform, new L { Name = "Settle", Mat = e.Pool, World = false, Mode = 1, Burst = 1, BurstTime = .06f, Max = 1, Life = new Vector2(.55f, .65f), Size = new Vector2(.7f, .85f),
                Shape = ParticleSystemShapeType.Sphere, Radius = .01f, ShapePos = new Vector3(0f, .07f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 9f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(e.Ink, .7f)), (.5f, C(e.Ink, .5f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .45f, .2f, 1f, 1f, 1f) });
            OneShot(root);
            return root;
        }
    }
}
