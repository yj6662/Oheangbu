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
    // SPEC-SPELL-VFX-OVERHAUL-308 11.58, the intercept burst (상 needle, 송 volley). Where an enemy projectile was shot down,
    // the ink freezes and cracks in place: a dark ink clump spreads in about .1 s, six short metal blades stand still in it and
    // shrink away, and one thin metal streak crosses it. One shot per layer (no loop), all at start speed 0 (nothing flies
    // off), gone in under 1 s; no glow, no droplets, nothing left on the ground. Local space; the presenter faces it to the eye.
    //   burst          - build the one metal prefab and point both _308b profiles at it
    public static partial class Vfx308Build
    {
        static readonly string[] BurstIds = { "078_C0C1", "090_C1A1" };
        static string BurstPath => Root2 + "/Prefabs/PF_V308i_sa.prefab";

        static string BuildBurst()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build burst: Edit mode only");
            V2 = true; Touched.Clear(); seed = 47000;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_splat", "metal_streak" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var e = Element(kit, "sa"); kit.Ink = e.Ink;
            var prefab = SaveWrap(BurstBody(e), BurstPath);
            var sb = new StringBuilder();
            foreach (string id in BurstIds)
            {
                var a = Attack.First(x => x.Effect == id);
                if (!File.Exists(AttackProfilePath(a))) { sb.AppendLine(a.Glyph + ": the attack copy is not built (run build first) - burst " + prefab.name + " built, not assigned"); continue; }
                var profile = Load<Vfx120Profile>(AttackProfilePath(a));
                profile.Burst308Prefab = prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(a.Glyph + " burst -> " + BurstPath + " on " + AttackProfilePath(a));
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static GameObject BurstBody(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(BurstPath));
            Color c = Color.Lerp(e.Pig, e.Ink, .7f);
            // the clump: one ink splat, full within .1 s, held, then thinning out
            Layer(root.transform, new L { Name = "Clump", Mat = e.Pool, World = false, Burst = 1, Max = 1, Life = new Vector2(.75f, .85f), Size = new Vector2(.6f, .72f),
                NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 9f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(e.Ink, .9f)), (.5f, C(e.Ink, .8f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .45f, .12f, 1f, 1f, 1.06f) });
            // the frozen blades: short metal shards standing still in the clump, cracking away (shrinking) in the last half
            Layer(root.transform, new L { Name = "Shards", Mat = e.Solid, Mesh = e.Spike, World = false, Burst = 6, BurstTime = .03f, Max = 7, Life = new Vector2(.6f, .85f),
                Size = new Vector2(.16f, .26f), Shape = ParticleSystemShapeType.Sphere, Radius = .14f, Thickness = 1f, Tumble = true,
                Grow = Curve(0f, .3f, .1f, 1f, .55f, 1f, 1f, 0f) });
            // the crack: one thin metal streak across the clump, quick
            Layer(root.transform, new L { Name = "Crack", Mat = e.Burst, World = false, Burst = 1, BurstTime = .05f, Max = 1, Life = new Vector2(.35f, .45f), Size = new Vector2(.75f, .9f),
                NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 10f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(c, 0f)), (.15f, C(c, .85f)), (.6f, C(c, .5f)), (1f, C(c, 0f))) });
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.playOnAwake = true; main.loop = false; }
            return root;
        }
    }
}
