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
    // SPEC-SPELL-VFX-OVERHAUL-308 11.64, the moment a buff does its work. Buffs of one element share the aura round the brush
    // (11.22 / D308-111), so 먼 (reflect), 멈 (slow back) and 넘 (poise) each get one short ink picture where the effect lands,
    // played through the intercept seam (ISpellBurstPresenter308.Burst -> profile.Burst308Prefab):
    //   먼  at the attacker, 1 m up: one slanted ink slash answering the blow, a clump and two clods held in it
    //   멈  at the attacker's feet: a flat ink wash clinging round them and three strokes falling down onto them
    //   넘  at the player's feet: a flat ink ring drawing in tight and a faint wash inside it
    // One shot per layer (no loop), start speed 0 except the falling strokes, all gone inside 1 s; no glow, no drops, nothing
    // left behind. Local space. Sizes and timings are TEST.
    //   buffhit        - build the three prefabs and point the three _308h profiles at them
    public static partial class Vfx308Build
    {
        static readonly (string id, string glyph, string key, string kind)[] BuffHits =
        {
            ("057_BA3C", "먼", "ma", "reflect"),
            ("058_BA48", "멈", "ma", "slow"),
            ("034_B118", "넘", "na", "poise"),
        };

        static string BuffHitPath(string kind, string key) => Root2 + "/Prefabs/PF_V308n_" + kind + "_" + key + ".prefab";

        static string BuildBuffHit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build buffhit: Edit mode only");
            V2 = true; Touched.Clear(); seed = 48000;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_throw", "ink_pool", "ink_splat", "speed_line", "water_ring" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var elems = new System.Collections.Generic.Dictionary<string, Elem>();
            var sb = new StringBuilder();
            foreach (var (id, glyph, key, kind) in BuffHits)
            {
                if (!elems.TryGetValue(key, out var e)) { elems[key] = e = Element(kit, key); kit.Ink = e.Ink; }
                string path = BuffHitPath(kind, key);
                GameObject body;
                switch (kind)
                {
                    case "reflect": body = ReflectBody(e, path); break;
                    case "slow": body = SlowBody(e, path); break;
                    default: body = PoiseBody(e, path); break;
                }
                var prefab = SaveWrap(body, path);
                string profilePath = CueProfilePath(id);
                if (!File.Exists(profilePath)) { sb.AppendLine(glyph + ": " + profilePath + " missing (run cue first) - " + prefab.name + " built, not assigned"); continue; }
                var profile = Load<Vfx120Profile>(profilePath);
                profile.Burst308Prefab = prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(glyph + " " + kind + " -> " + path + " on " + profilePath);
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static void OneShot(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.playOnAwake = true; main.loop = false; }
        }

        // 먼: the blow comes back - one slanted ink slash across the attacker (the presenter turns it to face the eye), a clump
        // spreading behind it and two earth clods held still in it, shrinking away
        static GameObject ReflectBody(Elem e, string path)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            Color c = Color.Lerp(e.Pig, e.Ink, .6f);
            var slash = SoakVariant(Bleed("M_V308n_reflect_" + e.Key, "ink_throw", C(e.Ink, .92f), 0f, .85f));
            Layer(root.transform, new L { Name = "Retort", Mat = slash, World = false, Burst = 1, Max = 1, Life = new Vector2(.45f, .55f), Size = new Vector2(1.4f, 1.7f),
                NoShape = true, RotDeg = new Vector2(30f, 50f), Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(c, 0f)), (.12f, C(c, .92f)), (.6f, C(c, .55f)), (1f, C(c, 0f))), Grow = Curve(0f, .6f, .25f, 1f, 1f, 1.1f) });
            var clump = Layer(root.transform, new L { Name = "Clump", Mat = e.Pool, World = false, Burst = 1, BurstTime = .04f, Max = 1, Life = new Vector2(.7f, .8f), Size = new Vector2(.7f, .85f),
                NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 9f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(e.Ink, .85f)), (.5f, C(e.Ink, .7f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .45f, .15f, 1f, 1f, 1.05f) });
            clump.GetComponent<ParticleSystemRenderer>().sortingFudge = 40f;   // behind the slash
            Layer(root.transform, new L { Name = "Clods", Mat = e.Solid, Mesh = e.Spike, World = false, Burst = 2, BurstTime = .03f, Max = 2, Life = new Vector2(.55f, .7f),
                Size = new Vector2(.18f, .26f), Shape = ParticleSystemShapeType.Sphere, Radius = .2f, Thickness = 1f, Tumble = true,
                Grow = Curve(0f, .3f, .1f, 1f, .55f, 1f, 1f, 0f) });
            OneShot(root);
            return root;
        }

        // 멈: the attacker is held back - a flat ink wash clinging round its feet (a little off the ground so it is not cut by
        // it) and three dry strokes falling down onto it, weight coming down; nothing stays on the ground after
        static GameObject SlowBody(Elem e, string path)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            var stroke = Mk(e.Key, "stroke", "speed_line", C(e.Ink, 1f));
            Layer(root.transform, new L { Name = "Cling", Mat = e.Pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.85f, .95f), Size = new Vector2(1.6f, 1.9f),
                Shape = ParticleSystemShapeType.Sphere, Radius = .01f, ShapePos = new Vector3(0f, .06f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(mark, .8f)), (.55f, C(mark, .6f)), (1f, C(mark, 0f))), Grow = Curve(0f, .55f, .2f, 1f, 1f, 1.04f) });
            Layer(root.transform, new L { Name = "Weight", Mat = stroke, World = false, Mode = 2, Burst = 3, BurstTime = .03f, Max = 3, Life = new Vector2(.6f, .75f), Size = new Vector2(1.1f, 1.4f),
                Shape = ParticleSystemShapeType.Circle, Radius = .35f, Thickness = 0f, ShapeRot = new Vector3(90f, 0f, 0f), ShapePos = new Vector3(0f, 1.6f, 0f),
                RotDeg = new Vector2(84f, 96f), RiseY = -2.2f, FrameFrom = 0f, FrameTo = .99f, RandomCell = true,
                Colour = Melt(mark, .5f), Grow = Curve(0f, .4f, .15f, 1f, 1f, .8f) });
            OneShot(root);
            return root;
        }

        // 넘: the player keeps the stroke - a flat ink ring at the feet drawing in tight, a faint wash inside it; only seen when
        // looking down (the feet are well past .45 m from the eye)
        static GameObject PoiseBody(Elem e, string path)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            var ring = Mk(e.Key, "ring", "water_ring", C(e.Ink, 1f));
            Layer(root.transform, new L { Name = "Hold", Mat = ring, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.6f, .7f), Size = Vector2.one * 1.8f,
                Shape = ParticleSystemShapeType.Sphere, Radius = .01f, ShapePos = new Vector3(0f, .05f, 0f), FrameFrom = 4f, FrameTo = 4.5f, RandomCell = true,
                Colour = Melt(mark, .5f), Grow = Curve(0f, 1.25f, .35f, .9f, 1f, .85f) });
            Layer(root.transform, new L { Name = "Settle", Mat = e.Pool, World = false, Mode = 1, Burst = 1, BurstTime = .05f, Max = 1, Life = new Vector2(.55f, .65f), Size = new Vector2(.9f, 1.05f),
                Shape = ParticleSystemShapeType.Sphere, Radius = .01f, ShapePos = new Vector3(0f, .04f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 9f,
                Colour = G((0f, C(e.Ink, .45f)), (.5f, C(e.Ink, .3f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1f) });
            OneShot(root);
            return root;
        }
    }
}
