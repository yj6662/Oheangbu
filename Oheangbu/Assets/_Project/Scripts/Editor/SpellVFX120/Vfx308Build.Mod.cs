using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.63: the eight letters whose effect carries a modifier (CSV, LOCKED attack rows) draw it at
    // the hit - 강 pierce, 낭 guardbreak (WP-15, D308-113), 놋 맛 shred, 몬 산 brand, 만 blast, 곤 lift (난 has none). One more "Mod_" layer group next to the
    // final's marker: a single's bolt copy PF_V308b_<key>_<final>_<mod>, a range letter's hit PF_V308b_hit_<key>_<final>_<mod>.
    // Ink, local, no light, gone inside .7 s (D308-109); sizes are TEST.
    public static partial class Vfx308Build
    {
        static string ModOf(string glyph)
        {
            switch (glyph)
            {
                case "강": return "pierce";
                case "낭": return "guardbreak";   // WP-15 (D308-113): 낭 left the pierce pair
                case "놋": case "맛": return "shred";
                case "몬": case "산": return "brand";
                case "만": return "blast";
                case "곤": return "lift";
                default: return "";
            }
        }

        static string ModBoltPath(string key, int final, string mod) => Root2 + "/Prefabs/PF_V308b_" + key + "_" + FinalName(final) + "_" + mod + ".prefab";
        static string ModHitPath(string key, int final, string mod) => Root2 + "/Prefabs/PF_V308b_hit_" + key + "_" + FinalName(final) + "_" + mod + ".prefab";

        // a single's bolt: the final's bolt, plus the modifier under Contact when the letter has one
        static GameObject ModBolt(Elem e, Spell spell, string glyph, Dictionary<string, GameObject> cache)
        {
            int final = FinalOf(glyph);
            var source = FinalBolt(e, spell, final, cache);
            string mod = ModOf(glyph);
            if (source == null || mod == "" || final == FinalNone) return source;
            string path = ModBoltPath(e.Key, final, mod);
            if (cache.TryGetValue(path, out var done)) return done;
            var root = Object.Instantiate(source); root.name = Path.GetFileNameWithoutExtension(path);
            try
            {
                var contact = root.transform.Find("Contact") ?? throw new Exception(source.name + " has no Contact");
                Accent(e, mod, contact, 1f);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                return cache[path] = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // a range letter's per-hit prefab: the final's hit, plus the modifier when the letter has one
        static GameObject ModHit(Elem e, string glyph, Dictionary<string, GameObject> cache)
        {
            int final = FinalOf(glyph);
            string mod = ModOf(glyph);
            if (mod == "" || final == FinalNone) return FinalHit(e, final, cache);
            string path = ModHitPath(e.Key, final, mod);
            if (cache.TryGetValue(path, out var done)) return done;
            var root = HitBody(e); root.name = Path.GetFileNameWithoutExtension(path);
            try
            {
                Marker(e, final, null, root.transform, null, .65f);
                Accent(e, mod, root.transform, .65f);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                return cache[path] = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // contact: the bolt's Contact (placed at the hit, local +Z back toward the caster) or a hit root; the materials are the
        // final markers' own (bleed2 / stroke / ring), so no new material
        static void Accent(Elem e, string mod, Transform contact, float k)
        {
            string key = e.Key;
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            var bleed = Mk(key, "bleed2", "ink_splat", C(e.Ink, .95f), .95f);
            var stroke = Mk(key, "stroke", "speed_line", C(e.Ink, 1f));
            var ring = Mk(key, "ring", "water_ring", C(e.Ink, 1f));
            switch (mod)
            {
                case "pierce":   // ink driven out behind the hit: five stretched strokes in a cone along the flight (local -Z) + a ring closing on the point
                {
                    var ps = Top(Layer(contact, new L { Name = "Mod_Pierce", Mat = bleed, World = false, Mode = 3, Burst = 5, BurstTime = .03f, Max = 5, Life = new Vector2(.26f, .32f),
                        Size = new Vector2(.22f, .32f) * k, Speed = new Vector2(7f, 9f) * k, Shape = ParticleSystemShapeType.Cone, Angle = 28f, Radius = .02f, ShapeRot = new Vector3(0f, 180f, 0f),
                        Drag = 18f * k, DragLinear = true, SpinRandom = false, FrameFrom = 2f, FrameTo = 4.99f, RandomCell = true, Colour = Melt(mark, .5f) }));
                    ps.GetComponent<ParticleSystemRenderer>().lengthScale = 5f;
                    Top(Layer(contact, new L { Name = "Mod_PierceHole", Mat = ring, World = false, Burst = 1, BurstTime = .02f, Max = 1, Life = new Vector2(.35f, .35f), Size = Vector2.one * 1.6f * k,
                        NoShape = true, SpinRandom = true, FrameFrom = 4f, FrameTo = 4.5f, RandomCell = true, Colour = Fade(mark, .4f), Grow = Curve(0f, 1.4f, 1f, .5f) }));
                    break;
                }
                case "guardbreak":   // a raised guard broken (낭, WP-15): a ring closes on the hit, snaps, and its pieces are thrown out flat across the caster's view
                {
                    Top(Layer(contact, new L { Name = "Mod_GuardRing", Mat = ring, World = false, Burst = 1, BurstTime = .02f, Max = 1, Life = new Vector2(.32f, .32f), Size = Vector2.one * 1.9f * k,
                        NoShape = true, SpinRandom = true, FrameFrom = 4f, FrameTo = 4.5f, RandomCell = true, Colour = Fade(mark, .45f), Grow = Curve(0f, 1f, .35f, .7f, 1f, 1.6f) }));
                    var ps = Top(Layer(contact, new L { Name = "Mod_GuardShards", Mat = stroke, World = false, Mode = 3, Burst = 6, BurstTime = .11f, Max = 6, Life = new Vector2(.3f, .4f),
                        Size = new Vector2(.3f, .45f) * k, Speed = new Vector2(5f, 7f) * k, Shape = ParticleSystemShapeType.Circle, Radius = .35f * k, Thickness = 0f,
                        Drag = 14f * k, DragLinear = true, SpinRandom = false, FrameFrom = 0f, FrameTo = .99f, RandomCell = true, Colour = Melt(mark, .5f) }));
                    ps.GetComponent<ParticleSystemRenderer>().lengthScale = 3f;
                    break;
                }
                case "shred":    // dry strokes lying on a shell round the hit, pushed off a little as they dry: armour coming apart
                    Top(Layer(contact, new L { Name = "Mod_Shred", Mat = stroke, World = false, Burst = 7, BurstTime = .08f, Max = 7, Life = new Vector2(.5f, .6f), Size = new Vector2(.7f, 1.1f) * k,
                        Speed = new Vector2(.4f, .8f) * k, Shape = ParticleSystemShapeType.Sphere, Radius = .75f * k, Thickness = 0f, Drag = 1.2f, DragLinear = true,
                        FrameFrom = 0f, FrameTo = .99f, RandomCell = true, Colour = Melt(mark, .4f), Grow = Curve(0f, .6f, .15f, 1f, 1f, 1f) }));
                    break;
                case "brand":    // a seal pressed in at .2 s and its rim closing round it, drawn over the other hit layers
                {
                    var seal = Layer(contact, new L { Name = "Mod_Brand", Mat = bleed, World = false, Burst = 1, BurstTime = .2f, Max = 1, Life = new Vector2(.55f, .55f), Size = Vector2.one * 1.5f * k,
                        NoShape = true, SpinRandom = true, FrameFrom = 3f, FrameTo = 3.5f, RandomCell = true, Colour = Melt(mark, .6f), Grow = Curve(0f, 1.35f, .25f, 1f, 1f, 1f) });
                    seal.GetComponent<ParticleSystemRenderer>().sortingFudge = -80f;
                    Top(Layer(contact, new L { Name = "Mod_BrandRim", Mat = ring, World = false, Burst = 1, BurstTime = .2f, Max = 1, Life = new Vector2(.5f, .5f), Size = Vector2.one * 2.4f * k,
                        NoShape = true, SpinRandom = true, FrameFrom = 6f, FrameTo = 6.5f, RandomCell = true, Colour = Melt(mark, .5f), Grow = Curve(0f, 1.4f, .3f, 1f, 1f, 1f) }));
                    break;
                }
                case "blast":    // a flat ink wash and a ring spreading level with the hit, about 4 m across (r 2 m)
                    Top(Layer(contact, new L { Name = "Mod_Blast", Mat = bleed, World = false, Mode = 1, Burst = 1, BurstTime = .05f, Max = 1, Life = new Vector2(.5f, .5f), Size = Vector2.one * 3.4f * k,
                        NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 12f, Colour = Melt(mark, .45f), Grow = Curve(0f, .5f, 1f, 1.1f) }));
                    Top(Layer(contact, new L { Name = "Mod_BlastRing", Mat = ring, World = false, Mode = 1, Burst = 1, BurstTime = .05f, Max = 1, Life = new Vector2(.5f, .5f), Size = Vector2.one * 3.6f * k,
                        NoShape = true, FrameFrom = 0f, FrameTo = 15f, Colour = Fade(mark, .35f), Grow = Curve(0f, .3f, 1f, 1.1f) }));
                    break;
                case "lift":     // two upright dry strokes rising off the hit
                    foreach (var (name, at) in new[] { ("Mod_LiftA", .05f), ("Mod_LiftB", .12f) })
                        Top(Layer(contact, new L { Name = name, Mat = stroke, World = false, Mode = 2, Burst = 1, BurstTime = at, Max = 1, Life = new Vector2(.35f, .35f), Size = Vector2.one * 2.6f * k,
                            Shape = ParticleSystemShapeType.Circle, Radius = .5f * k, ShapeRot = new Vector3(90f, 0f, 0f), RotDeg = new Vector2(84f, 96f), RiseY = 4f,
                            FrameFrom = 0f, FrameTo = .99f, RandomCell = true, Colour = Melt(mark, .5f), Grow = Curve(0f, .4f, .15f, 1f, 1f, 1f) }));
                    break;
            }
        }

        // the modifier step alone: the eight letters' attack copies get their modifier prefab; nothing else changes
        static string BuildMod()
        {
            if (!V2) throw new ArgumentException("the modifier pictures exist in the v2 look only");
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            Touched.Clear(); seed = 31900;
            var elems = new Dictionary<string, Elem>(); var bolts = new Dictionary<string, GameObject>(); var hits = new Dictionary<string, GameObject>();
            var sb = new StringBuilder(); int set = 0, missing = 0;
            foreach (var a in Attack)
            {
                string mod = ModOf(a.Glyph);
                if (mod == "" || a.Shape == Form.Giyeok || Spells.Any(x => x.Glyph == a.Glyph)) continue;
                if (!File.Exists(AttackProfilePath(a))) { missing++; sb.AppendLine(a.Glyph + " no attack copy (run build first)"); continue; }
                if (!elems.TryGetValue(a.Elem, out var e)) elems[a.Elem] = e = Element(kit, a.Elem);
                var profile = Load<Vfx120Profile>(AttackProfilePath(a));
                GameObject made;
                if (a.Shape == Form.Single) made = profile.Bolt300Prefab = ModBolt(e, Spells.First(x => x.Key == a.Elem), a.Glyph, bolts);
                else made = profile.AreaContactPrefab = ModHit(e, a.Glyph, hits);
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                set++; sb.AppendLine(a.Glyph + " " + mod + " " + (made != null ? made.name : "none"));
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            sb.AppendLine("mod: " + set + " letters set, " + missing + " missing");
            return sb.ToString();
        }
    }
}
