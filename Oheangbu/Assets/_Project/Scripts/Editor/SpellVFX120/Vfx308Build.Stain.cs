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
    // D308-104 (e) SPEC-SPELL-VFX-OVERHAUL-308 11.37 6절: a range-ㄱ letter (곡 녹 목 속 옥) no longer leaves a pool at every hit;
    // the game lays one small stain under each struck enemy (Vfx120Profile.AreaStainPrefab, CombatLoopWiring.Stain308).
    // The prefab is the old Linger alone, flat on the ground; the game sets its size, life and alpha from the profile.
    public static partial class Vfx308Build
    {
        static string StainPath(string key) => Root2 + "/Prefabs/PF_V308b_stain_" + key + ".prefab";

        // contract (KtpContactEffect): a root with a non-looping ParticleSystem, particles only
        static GameObject StainBody(Elem e, Dictionary<string, GameObject> cache)
        {
            string path = StainPath(e.Key);
            if (cache.TryGetValue(path, out var done)) return done;
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            var ps = Layer(null, new L { Name = Path.GetFileNameWithoutExtension(path), Mat = Mk(e.Key, "stain", "ink_pool", C(e.Ink, 1f), 1f, 0f), World = false, Mode = 1, Burst = 1, Max = 1,
                Life = Vector2.one * 2.5f, Size = Vector2.one * 2f, NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 15.5f,
                Colour = G((0f, C(mark, 1f)), (.7f, C(mark, .9f)), (1f, C(mark, 0f))), Grow = Curve(0f, .4f, .1f, 1f, 1f, 1.1f) });
            var root = ps.gameObject;
            try
            {
                root.layer = AfterFogLayer;
                return cache[path] = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // the stain step alone: rebuilds the range-ㄱ hit without its pool and sets the stain on the attack copies; other
        // range letters get no stain. Touches no texture (the stain reads the existing ink_pool sheet).
        static string BuildStain()
        {
            if (!V2) throw new ArgumentException("the stain exists in the v2 look only");
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            Touched.Clear(); seed = 31700;
            var elems = new Dictionary<string, Elem>(); var hits = new Dictionary<string, GameObject>(); var stains = new Dictionary<string, GameObject>();
            var sb = new StringBuilder(); int set = 0, cleared = 0, missing = 0;
            foreach (var a in Attack)
            {
                if (a.Shape == Form.Giyeok || a.Shape == Form.Single || Spells.Any(x => x.Glyph == a.Glyph)) continue;
                if (!File.Exists(AttackProfilePath(a))) { missing++; sb.AppendLine(a.Glyph + " no attack copy (run build first)"); continue; }
                var profile = Load<Vfx120Profile>(AttackProfilePath(a));
                if (FinalOf(a.Glyph) == FinalK)
                {
                    if (!elems.TryGetValue(a.Elem, out var e)) elems[a.Elem] = e = Element(kit, a.Elem);
                    profile.AreaContactPrefab = FinalHit(e, FinalK, hits);
                    profile.AreaStainPrefab = StainBody(e, stains);
                    set++; sb.AppendLine(a.Glyph + " stain " + Path.GetFileNameWithoutExtension(StainPath(e.Key)));
                }
                else
                {
                    if (profile.AreaStainPrefab == null) continue;
                    profile.AreaStainPrefab = null; cleared++;
                }
                EditorUtility.SetDirty(profile); Touched.Add(profile);
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            sb.AppendLine("stain: " + set + " range-ㄱ letters set, " + cleared + " others cleared, " + missing + " missing, " + stains.Count + " stain prefabs");
            return sb.ToString();
        }
    }
}
