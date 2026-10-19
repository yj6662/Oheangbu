using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.44: the fire haetae's breath as an ink body instead of the additive KTP flame. Particles
    // only (DemoSummonFlamePresentation drives them on its manual clock), InkBleed308 only (no additive, no glow): soot that
    // leaves the mouth black and takes the fire pigment from a quarter of its life (D308-109), growing as it goes, plus a few
    // slow settling wisps. No ground mark, no flying bits. Authored for a 4.5 m reach and a .65 s spray; the game scales it
    // uniformly to the plan's range and sets the cone to the plan's half angle.
    //   haetae:flame          - build the body
    //   haetae:flame:connect  - put it on the live FireHaetae profile (recorded; also run by connect:attack)
    //   haetae:flame:revert   - put back what was there (also run by revert)
    public static partial class Vfx308Build
    {
        static string HaetaeBreathPath => Root2 + "/Prefabs/PF_V308i_haetae_breath.prefab";
        static string HaetaeBreathRecord => Path.Combine(Path.GetDirectoryName(Record), "haetae-flame-before.json");
        const float HaetaeBreathRange = 4.5f;

        [Serializable] sealed class HaetaeRecorded { public List<string> profiles = new List<string>(); public List<string> before = new List<string>(); }

        static string BuildHaetaeBreath()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build haetae:flame: Edit mode only");
            V2 = true; Touched.Clear(); seed = 44400;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            var p = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + Spells.First(x => x.Key == "na").Effect + ".asset");
            Color pig = p.Pigment, ink = p.Ink, wash = Color.Lerp(pig, ink, .45f), dark = Color.Lerp(pig, ink, .85f);
            var root = new GameObject(Path.GetFileNameWithoutExtension(HaetaeBreathPath));
            try
            {
                root.layer = AfterFogLayer;
                // Breath: the cone of soot. Speed 5 - 6.5 over .7 - .9 s without drag reaches about the authored 4.5 m.
                var breath = Layer(root.transform, new L { Name = "Breath", Mat = Bleed("M_V308i_haetae_breath", "soot_wisp", C(ink, .85f), 0f, .9f),
                    Mode = 0, Rate = 40f, Max = 40, Life = new Vector2(.7f, .9f), Size = new Vector2(.5f, .9f), Speed = new Vector2(5f, 6.5f),
                    SpinRandom = true, Spin = 40f, Blend = true, Shape = ParticleSystemShapeType.Cone, Angle = 30f, Radius = .1f,
                    FrameFrom = 0f, FrameTo = 8f, Colour = InkFirst(ink, wash, dark), Grow = Curve(0f, .45f, .5f, 1.1f, 1f, 1.6f) });
                breath.gameObject.layer = AfterFogLayer;
                // Settle: a few slow wisps rising off the far end as the soot thins (no glow, gone with the plan's end).
                var settle = Layer(root.transform, new L { Name = "Settle", Mat = Bleed("M_V308i_haetae_settle", "soot_wisp", C(ink, .7f), 0f, .6f),
                    Mode = 0, Rate = 9f, Max = 10, Life = new Vector2(.9f, 1.2f), Size = new Vector2(.8f, 1.2f), Speed = new Vector2(3f, 4f),
                    SpinRandom = true, Spin = 20f, Blend = true, RiseY = .5f, Drag = 2.5f, DragLinear = true,
                    Shape = ParticleSystemShapeType.Cone, Angle = 30f, Radius = .1f, FrameFrom = 4f, FrameTo = 12f,
                    Colour = G((0f, C(dark, 0f)), (.2f, C(Color.Lerp(wash, dark, .6f), .55f)), (1f, C(dark, 0f))), Grow = Curve(0f, .7f, 1f, 1.4f) });
                settle.gameObject.layer = AfterFogLayer;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, HaetaeBreathPath);
                foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
                return "haetae:flame -> " + AssetDatabase.GetAssetPath(saved) + " (2 systems, InkBleed308 only, authored reach " + HaetaeBreathRange + " m)";
            }
            finally { Object.DestroyImmediate(root); }
        }

        static string ConnectHaetaeBreath()
        {
            if (!File.Exists(HaetaeBreathPath)) return "haetae:flame: not built (run haetae:flame) - left as it is";
            var live = LiveBeasts().Where(x => AssetDatabase.GetAssetPath(x).EndsWith("_FireHaetae.asset", StringComparison.Ordinal)).ToList();
            if (live.Count == 0) return "haetae:flame: no live FireHaetae profile";
            if (!File.Exists(HaetaeBreathRecord))
            {
                var rec = new HaetaeRecorded();
                foreach (var p in live) { rec.profiles.Add(AssetDatabase.GetAssetPath(p)); rec.before.Add(AssetDatabase.GetAssetPath(p.FlameInk308Prefab)); }
                File.WriteAllText(HaetaeBreathRecord, JsonUtility.ToJson(rec, true));
            }
            var body = Load<GameObject>(HaetaeBreathPath);
            foreach (var p in live)
            {
                p.FlameInk308Prefab = body; p.FlameInk308AuthoredRange = HaetaeBreathRange;
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            return "haetae:flame: " + live.Count + " live FireHaetae -> " + HaetaeBreathPath;
        }

        static string RevertHaetaeBreath()
        {
            if (!File.Exists(HaetaeBreathRecord)) return "haetae:flame: nothing recorded";
            var rec = JsonUtility.FromJson<HaetaeRecorded>(File.ReadAllText(HaetaeBreathRecord));
            for (int i = 0; i < rec.profiles.Count; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(rec.profiles[i]);
                if (p == null) continue;
                p.FlameInk308Prefab = string.IsNullOrEmpty(rec.before[i]) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(rec.before[i]);
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            File.Delete(HaetaeBreathRecord);
            return "haetae:flame: restored " + rec.profiles.Count + " profiles";
        }
    }
}
