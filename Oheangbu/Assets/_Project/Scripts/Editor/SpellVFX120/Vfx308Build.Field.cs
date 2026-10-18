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
    // SPEC-SPELL-VFX-OVERHAUL-308 11.46, the field gates 눈 숫 웅. Until now they flew the plain element bolt and hit the gate
    // like an enemy. Each letter now has its own ink body (PF_V308k_field_<key>) and the profile follows the gate (Bolt300FollowTarget,
    // height 1.1 = the gate's TargetPoint), so the Contact sits on the gate for the open:
    //   눈 - a low-arc ink seed; ragged flame tongues climb a band at the gate foot, soot black first, red soaking in
    //   숫 - a high arc; one tall ink cleaver comes straight down through the gate and frays into dry wisps
    //   웅 - a low wet orb; one knee-high indigo swell rolls away from the caster across the gate, bursts only, under .9 s
    // No ground mark, no flying bits, no light past the 2-cel birth, no near-contact shrink (a 4 m gate body must keep its size).
    //   field          - build the three and point their _308h profiles at them
    public static partial class Vfx308Build
    {
        static string FieldBodyPath(string key) => Root2 + "/Prefabs/PF_V308k_field_" + key + ".prefab";

        static string BuildField()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build field: Edit mode only");
            V2 = true; Touched.Clear(); seed = 46400;
            foreach (var d in new[] { "/Prefabs", "/Materials", "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            Sheet2("ink_throw");
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var g in Cues.Where(c => c.Kind == "field"))
            {
                var e = Element(kit, g.Key); kit.Ink = e.Ink;
                var spell = Spells.First(x => x.Key == g.Key);
                var bolt = FieldBody(e);

                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + g.Id + ".asset";
                if (!File.Exists(CueProfilePath(g.Id)) && !AssetDatabase.CopyAsset(srcProfile, CueProfilePath(g.Id))) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(CueProfilePath(g.Id));
                profile.NativeCastPrefab = null; profile.NativeImpactPrefab = null; profile.NativeBodyPrefab = null; profile.BotanicalPrefab = null;
                profile.WardKind = Vfx120WardKind.None; profile.Aura308ByWardRadius = false; profile.AreaRemake = Vfx120AreaRemake.None;
                profile.KtpContactBrightness = 1f;
                profile.Bolt300Prefab = bolt; profile.Bolt300Spin = spell.Spin;
                profile.Bolt300SoakSeconds = BodySoakSeconds; profile.Bolt300TravelScale = TravelScale;
                profile.BodyMesh = e.Bit;
                TuneField(profile, g.Key);
                EditorUtility.SetDirty(profile); Touched.Add(profile);

                string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + g.Id + ".prefab";
                if (!File.Exists(CueEffectPath(g.Id)) && !AssetDatabase.CopyAsset(srcEffect, CueEffectPath(g.Id))) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(CueEffectPath(g.Id));
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + CueEffectPath(g.Id));
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, CueEffectPath(g.Id));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(g.Glyph + " field -> " + CueEffectPath(g.Id) + " (bolt " + bolt.name + ", arc " + profile.Bolt300Arc + ", ease " + profile.Bolt300Ease + ")");
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        // 11.46 §2: the gate's own flight. The hit cue is the gate's open time (눈 1.2 / 숫 .5 / 웅 2.0 s), so the arc and ease set
        // how the ink gets there in that time; the body follows the gate (it does not move) and is never shrunk near the camera.
        static void TuneField(Vfx120Profile profile, string key)
        {
            profile.Bolt300FollowTarget = true; profile.Bolt300FollowHeight = 1.1f;
            profile.Bolt300Pierce = false; profile.Bolt300Return = false; profile.Bolt300ReturnToCaster = false;
            profile.Bolt300Curve = 0f; profile.Bolt300Scale = 1f; profile.Bolt300NearContactMeters = 0f;
            profile.Aura308Prefab = null; profile.Duration = 3f;
            switch (key)
            {
                case "na": profile.Bolt300Arc = .8f; profile.Bolt300Ease = 1.3f; break;
                case "sa": profile.Bolt300Arc = 2.5f; profile.Bolt300Ease = 1f; break;
                default: profile.Bolt300Arc = .35f; profile.Bolt300Ease = 1.6f; break;
            }
        }

        // 11.46 §3. The Contact is placed at the gate's pivot + 1.1 m up and turned to the flat flight axis, so local -1.1 y is the
        // gate foot and +z runs away from the caster. All Contact layers are local; nothing outlives the 3 s effect.
        static GameObject FieldBody(Elem e)
        {
            Directory.CreateDirectory(Root2 + "/Prefabs");
            var (travel, contact, root) = Shell("PF_V308k_field_" + e.Key);
            try
            {
                switch (e.Key)
                {
                    case "na":   // the vine curtain burns from its foot: tongues on a 2.6 m band, soot rising behind them
                        InkMass(travel, "fd_na", e.Ink, e.Wash, e.Dark, .7f, rate: 22f, spin: 50f, shed: 0f);
                        Birth2(travel, "fd_na", e.Wash, .8f);
                        Layer(contact, new L { Name = "Flames", Mat = e.Flame, World = false, Mode = 2, Rate = 14f, Max = 16, Life = new Vector2(.55f, .8f), Size = new Vector2(1f, 1.5f),
                            Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(2.6f, .05f, .3f), ShapePos = new Vector3(0f, -1.1f, 0f), Pivot = new Vector3(0f, .4f, 0f), RiseY = .9f,
                            SpinRandom = false, Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Colour = InkFirst(e.Ink, e.Wash, e.Dark), Grow = Curve(0f, .5f, 1f, 1.15f) });
                        Layer(contact, new L { Name = "Soot", Mat = e.LickSoot, World = false, Mode = 2, Rate = 6f, Max = 8, Life = new Vector2(.7f, .9f), Size = new Vector2(1.1f, 1.5f),
                            Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(2.6f, .05f, .3f), ShapePos = new Vector3(0f, -.6f, 0f), RiseY = .6f,
                            SpinRandom = false, Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Colour = InkFirst(e.Ink, e.Dark, e.Dark), Grow = Curve(0f, .7f, 1f, 1.15f) });
                        break;
                    case "sa":   // one cleaver from above: a tall upright ink blade sinks through the gate, then dry wisps fray off it
                        InkMass(travel, "fd_sa", e.Ink, e.Wash, e.Dark, .8f, rate: 24f, spin: 0f, shed: 0f);
                        Birth2(travel, "fd_sa", e.Wash, .9f);
                        Layer(contact, new L { Name = "Blade", Mat = SoakVariant(Bleed("M_V308k_sa_blade", "ink_throw", C(e.Ink, .95f), 0f, .9f)), World = false, Mode = 2, Burst = 1, Max = 2,
                            Life = new Vector2(.75f, .75f), Size = new Vector2(1f, 1f), Size3D = new Vector3(.9f, 3.2f, 1f), Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(.01f, .01f, .01f),
                            ShapePos = new Vector3(0f, .9f, 0f), RiseY = -2f, SpinRandom = false, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                            Colour = InkFirst(e.Ink, e.Wash, e.Dark), Grow = Curve(0f, .85f, .2f, 1f, 1f, 1f) });
                        Layer(contact, new L { Name = "Wisps", Mat = B("sa", "ink_wisp", C(e.Ink, .8f), 0f, .8f), World = false, Mode = 2, Burst = 6, BurstTime = .45f, Max = 8,
                            Life = new Vector2(.5f, .8f), Size = new Vector2(.5f, .8f), Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(.3f, 2.6f, .3f), ShapePos = new Vector3(0f, .2f, 0f),
                            RiseY = .25f, SpinRandom = false, Blend = true, FrameFrom = 0f, FrameTo = 8f,
                            Colour = G((0f, C(e.Ink, 0f)), (.15f, C(e.Ink, .8f)), (1f, C(e.Dark, 0f))) });
                        break;
                    default:     // one washing swell: knee-high crests roll away from the caster across the blot, near side first
                        var aSpin = InkMass(travel, "fd_a", e.Ink, e.Wash, e.Dark, .8f, rate: 24f, spin: 25f, shed: 0f);
                        Layer(aSpin, new L { Name = "Orb", Mat = Bleed("M_V308b_a_water_orb_veil", "water_orb", C(Color.Lerp(e.Pig, e.Ink, .7f), .3f), 0f, .6f), World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(1.8f, 1.8f), NoShape = true, SpinRandom = false, Blend = true,
                            Cycles = 6f, Colour = G((0f, C(e.Wash, 1f)), (1f, C(e.Wash, 1f))), Grow = Curve(0f, .3f, .05f, 1f, 1f, 1f) });
                        Birth2(travel, "fd_a", e.Wash, .9f);
                        for (int i = 0; i < 3; i++)   // three staggered bursts so the near edge rises first and the swell rolls on
                            Layer(contact, new L { Name = "Swell" + i, Mat = e.Head, World = false, Mode = 2, Burst = i == 1 ? 3 : 2, BurstTime = .012f + .08f * i, Max = 4,
                                Life = new Vector2(.7f, .85f), Size = new Vector2(1.1f, 1.4f), Speed = new Vector2(1.5f, 2.5f),
                                Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(3f, .05f, .2f), ShapePos = new Vector3(0f, -1.1f, -1.2f + .25f * i), Pivot = new Vector3(0f, e.HeadPivot, 0f),
                                SpinRandom = false, Blend = true, FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFirst(e.Ink, e.Wash, e.Dark), Grow = Curve(0f, .7f, .4f, 1f, 1f, .8f) });
                        // the faint wash where it landed: flat, low, gone in .85 s (not a lasting mark)
                        Layer(contact, new L { Name = "Wash", Mat = e.Ring, World = false, Mode = 1, Burst = 1, BurstTime = .1f, Max = 2, Life = new Vector2(.85f, .85f), Size = new Vector2(3f, 3f),
                            NoShape = true, SpinRandom = true, Blend = true, FrameFrom = 0f, FrameTo = 15.5f, Pivot = new Vector3(0f, 0f, -1.05f / 3f),
                            Colour = G((0f, C(e.Dark, 0f)), (.25f, C(e.Wash, .22f)), (1f, C(e.Wash, 0f))), Grow = Curve(0f, .8f, 1f, 1.1f) });
                        break;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                Bolt308Extras(root);
                InkTrail(travel, "fd_" + e.Key, e.Ink, e.Dark, .28f);
                return PrefabUtility.SaveAsPrefabAsset(root, FieldBodyPath(e.Key));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
