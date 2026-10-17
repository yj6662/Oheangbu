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
    // D308-109 (SPEC-SPELL-VFX-OVERHAUL-308 11.40): the 27 non-attack letters the visual set still sent to the old catalogue -
    // installs (감 남 맘 삼 암), field gates (눈 숫 웅), WP buffs (건 넌 넘 선 언 엄 먼 멈 섬), companions (겅 넝 멍 성 엉) and
    // sword forms (것 넛 멋 섯 엇). Data only: per letter a #308 copy of its Vfx120 profile and effect (_308h) with every old
    // native body / cast / impact switched off. Installs and gates fly the element's single bolt (they send a real flight);
    // buffs reuse the 11.22 buff band; companions and sword forms get a small low ink wrap at the caster - a placeholder
    // until their shots / swings have a seam. connect:attack points the set entries at the copies once built (recorded per
    // letter in visualset-before.json, so revert puts them back).
    //   cue            - build the 27
    public static partial class Vfx308Build
    {
        static readonly (string Glyph, string Id, string Key, string Kind)[] Cues =
        {
            ("감", "004_AC10", "ga", "mark"), ("남", "028_B0A8", "na", "mark"), ("맘", "052_B9D8", "ma", "mark"), ("삼", "076_C0BC", "sa", "mark"), ("암", "100_C554", "a", "mark"),
            ("눈", "045_B208", "na", "field"), ("숫", "095_C22B", "sa", "field"), ("웅", "120_C6C5", "a", "field"),
            ("건", "009_AC74", "ga", "buff"), ("넌", "033_B10C", "na", "buff"), ("넘", "034_B118", "na", "buff"), ("먼", "057_BA3C", "ma", "buff"), ("멈", "058_BA48", "ma", "buff"),
            ("선", "081_C120", "sa", "buff"), ("섬", "082_C12C", "sa", "buff"), ("언", "105_C5B8", "a", "buff"), ("엄", "106_C5C4", "a", "buff"),
            ("겅", "012_AC85", "ga", "companion"), ("넝", "036_B11D", "na", "companion"), ("멍", "060_BA4D", "ma", "companion"), ("성", "084_C131", "sa", "companion"), ("엉", "108_C5C9", "a", "companion"),
            ("것", "011_AC83", "ga", "sword"), ("넛", "035_B11B", "na", "sword"), ("멋", "059_BA4B", "ma", "sword"), ("섯", "083_C12F", "sa", "sword"), ("엇", "107_C5C7", "a", "sword"),
        };

        static string CueProfilePath(string id) => Root2 + "/Profiles/" + id + "_308h.asset";
        static string CueEffectPath(string id) => Root2 + "/Effects/" + id + "_308h.prefab";
        static string CueWrapPath(string kind, string key) => Root2 + "/Prefabs/PF_V308h_" + kind + "_" + key + ".prefab";

        static string BuildCue()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build cue: Edit mode only");
            V2 = true; Touched.Clear(); seed = 31900;
            foreach (var d in new[] { "/Prefabs", "/Materials", "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            AssetDatabase.Refresh();
            Sheet2("ink_throw"); Sheet2("ink_pool");
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var g in Cues)
            {
                var e = Element(kit, g.Key);
                var spell = Spells.First(x => x.Key == g.Key);
                GameObject bolt = null, aura = null;
                if (g.Kind == "mark" || g.Kind == "field")
                {
                    // the plain element bolt: FinalBolt does not take the ㅁ final, and a gate has none
                    bolt = File.Exists(PrefabPath(spell)) ? Load<GameObject>(PrefabPath(spell)) : null;
                    if (bolt == null) { sb.AppendLine(g.Glyph + ": the element bolt is not built (run build first)"); continue; }
                }
                else if (g.Kind == "buff")
                {
                    aura = File.Exists(AuraPrefabPath("buff", g.Key)) ? Load<GameObject>(AuraPrefabPath("buff", g.Key)) : null;
                    if (aura == null) aura = SaveWrap(BuffInk(e), AuraPrefabPath("buff", g.Key));
                }
                else
                {
                    // rebuilt on every run (cheap), once per kind and element
                    string wrap = CueWrapPath(g.Kind, g.Key);
                    if (builtWraps.Add(wrap)) aura = SaveWrap(SmallWrap(e, g.Kind), wrap);
                    else aura = Load<GameObject>(wrap);
                }

                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + g.Id + ".asset";
                if (!File.Exists(CueProfilePath(g.Id)) && !AssetDatabase.CopyAsset(srcProfile, CueProfilePath(g.Id))) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(CueProfilePath(g.Id));
                profile.NativeCastPrefab = null; profile.NativeImpactPrefab = null; profile.NativeBodyPrefab = null; profile.BotanicalPrefab = null;
                profile.WardKind = Vfx120WardKind.None; profile.Aura308ByWardRadius = false; profile.AreaRemake = Vfx120AreaRemake.None;
                profile.KtpContactBrightness = 1f; profile.Duration = 3f;
                if (bolt != null)
                {
                    profile.Aura308Prefab = null;
                    profile.Bolt300Prefab = bolt; profile.Bolt300Arc = spell.Arc; profile.Bolt300Curve = spell.Curve; profile.Bolt300Spin = spell.Spin;
                    profile.Bolt300Scale = 1f; profile.Bolt300Ease = 1f; profile.Bolt300SoakSeconds = BodySoakSeconds; profile.Bolt300NearContactMeters = NearContactMeters; profile.Bolt300TravelScale = TravelScale;
                    profile.BodyMesh = e.Bit;
                }
                else { profile.Bolt300Prefab = null; profile.Aura308Prefab = aura; }
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
                sb.AppendLine(g.Glyph + " " + g.Kind + " -> " + CueEffectPath(g.Id) + " (" + (bolt != null ? "bolt " + bolt.name : "wrap " + aura.name) + ")");
            }
            builtWraps.Clear();
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static readonly System.Collections.Generic.HashSet<string> builtWraps = new System.Collections.Generic.HashSet<string>();

        static GameObject SaveWrap(GameObject root, string path)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // companion / sword form placeholder: a few ink strokes drawn up round the hips on a .4 - .5 m circle, black first and the
        // pigment soaking in; kept low (top about 1.1 m, so .45 m clear of a 1.6 m eye) and with no ground pool. The companion
        // turns one way, the sword form the other and tighter.
        static GameObject SmallWrap(Elem e, string kind)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(CueWrapPath(kind, e.Key)));
            bool sword = kind == "sword";
            Color c = Color.Lerp(e.Pig, e.Ink, .65f);
            var band = SoakVariant(Bleed("M_V308h_" + kind + "_" + e.Key + "_band", "ink_throw", C(e.Ink, .9f), 0f, .85f));
            Layer(root.transform, new L { Name = "Wrap", Mat = band, World = false, Rate = sword ? 7f : 9f, Max = 12, Life = new Vector2(.8f, 1.05f), Size = new Vector2(.42f, .58f), RiseY = .7f,
                Shape = ParticleSystemShapeType.Circle, Radius = sword ? .4f : .5f, Thickness = 0f, ArcSpeed = sword ? -2.2f : 1.4f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .15f, 0f),
                Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(c, 0f)), (.15f, C(c, .85f)), (.7f, C(c, .55f)), (1f, C(c, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, .7f) });
            return root;
        }
    }
}
