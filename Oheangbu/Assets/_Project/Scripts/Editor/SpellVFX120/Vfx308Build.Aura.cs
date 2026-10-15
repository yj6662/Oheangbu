using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22): buffs (걱 넉 먹 석 억) and wards (구 누 무 수 우) as #308 loop bodies
    // (Vfx120Profile.Aura308Prefab). Buff = ink drawn up round the body in a rising spiral (black first, the pigment soaking in)
    // over a dark pool at the feet. Ward = ink membranes standing on a 1 m ring (scaled by WardRadius at run time) over a ring
    // of ground ink; the ward copies set WardKind None, so the old stone / pattern ward is not built. connect:attack writes the
    // live EA profiles' PresentationPrefabs (Architecture296 copies) once built, recording them in aura-before.json; revert
    // restores them. v1: no struck ripple on the ward (the old FixedWardVfx drew it) - later.
    //   aura           - build the ten
    public static partial class Vfx308Build
    {
        static readonly (string Glyph, string Id, string Key)[] Buffs = { ("걱", "008_AC71", "ga"), ("넉", "032_B109", "na"), ("먹", "056_BA39", "ma"), ("석", "080_C11D", "sa"), ("억", "104_C5B5", "a") };
        static readonly (string Glyph, string Id, string Key)[] Wards = { ("구", "019_AD6C", "ga"), ("누", "043_B204", "na"), ("무", "067_BB34", "ma"), ("수", "091_C218", "sa"), ("우", "115_C6B0", "a") };

        static string AuraPrefabPath(string kind, string key) => Root2 + "/Prefabs/PF_V308f_" + kind + "_" + key + ".prefab";
        static string AuraProfilePath(string id) => Root2 + "/Profiles/" + id + "_308f.asset";
        static string AuraEffectPath(string id) => Root2 + "/Effects/" + id + "_308f.prefab";
        static string AuraRecord => Path.Combine(Path.GetDirectoryName(Record), "aura-before.json");

        [Serializable] sealed class AuraRecorded { public List<string> buffs = new List<string>(); public List<string> wards = new List<string>(); }

        static string BuildAura()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build aura: Edit mode only");
            V2 = true; Touched.Clear(); seed = 31700;
            foreach (var d in new[] { "/Prefabs", "/Materials", "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            AssetDatabase.Refresh();
            Sheet2("ink_throw"); Sheet2("ink_drops"); Sheet2("ink_pool");
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var (list, kind) in new[] { (Buffs, "buff"), (Wards, "ward") })
                foreach (var g in list)
                {
                    var e = Element(kit, g.Key);
                    var root = kind == "buff" ? BuffInk(e) : WardInk(e);
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, AuraPrefabPath(kind, g.Key));
                    Object.DestroyImmediate(root);

                    string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + g.Id + ".asset";
                    if (!File.Exists(AuraProfilePath(g.Id)) && !AssetDatabase.CopyAsset(srcProfile, AuraProfilePath(g.Id))) throw new Exception("copy failed " + srcProfile);
                    var profile = Load<Vfx120Profile>(AuraProfilePath(g.Id));
                    profile.Aura308Prefab = prefab;
                    if (kind == "ward") profile.WardKind = Vfx120WardKind.None;   // the old stone / pattern ward stays out
                    profile.Aura308ByWardRadius = kind == "ward";
                    profile.KtpContactBrightness = 1f;
                    EditorUtility.SetDirty(profile); Touched.Add(profile);

                    string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + g.Id + ".prefab";
                    if (!File.Exists(AuraEffectPath(g.Id)) && !AssetDatabase.CopyAsset(srcEffect, AuraEffectPath(g.Id))) throw new Exception("copy failed " + srcEffect);
                    var contents = PrefabUtility.LoadPrefabContents(AuraEffectPath(g.Id));
                    try
                    {
                        var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + AuraEffectPath(g.Id));
                        fx.Profile = profile;
                        PrefabUtility.SaveAsPrefabAsset(contents, AuraEffectPath(g.Id));
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    sb.AppendLine(g.Glyph + " " + kind + " -> " + AuraPrefabPath(kind, g.Key));
                }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        // buff: strokes drawn up round the body - emitted round a 0.45 m circle at the feet, the emitter walking the circle
        // (a spiral as they rise), black first and the pigment soaking in; a dark pool stays under the feet while it lasts
        static GameObject BuffInk(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(AuraPrefabPath("buff", e.Key)));
            Color c = Color.Lerp(e.Pig, e.Ink, .45f);
            var band = SoakVariant(Bleed("M_V308f_buff_" + e.Key + "_band", "ink_throw", C(e.Ink, .9f), 0f, .9f));
            Layer(root.transform, new L { Name = "Band", Mat = band, World = false, Rate = 16f, Max = 24, Life = new Vector2(.9f, 1.2f), Size = new Vector2(.32f, .5f), RiseY = 1.5f,
                Shape = ParticleSystemShapeType.Circle, Radius = .45f, Thickness = 0f, ArcSpeed = 1.6f, ShapeRot = new Vector3(-90f, 0f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(c, 0f)), (.15f, C(c, .95f)), (.7f, C(c, .7f)), (1f, C(c, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, .7f) });
            Layer(root.transform, new L { Name = "Pool", Mat = Bleed("M_V308f_pool", "ink_pool", C(SwingInkColour, .85f), 0f, .85f, 0f, 0f), World = false, Mode = 1, Rate = 1.2f, Max = 3, Life = new Vector2(1.8f, 2.2f),
                Size = new Vector2(1.5f, 1.9f), NoShape = true, Blend = true, ShapePos = new Vector3(0f, .03f, 0f), FrameFrom = 3f, FrameTo = 12f, Colour = InkFade(.6f), Grow = Curve(0f, .7f, .3f, 1f, 1f, 1.1f) });
            return root;
        }

        // ward: ink membranes standing on a 1 m ring (upright stroke sheets emitted round it), over a ring of ground ink
        static GameObject WardInk(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(AuraPrefabPath("ward", e.Key)));
            Color c = Color.Lerp(e.Pig, e.Ink, .5f);
            var wall = SoakVariant(Bleed("M_V308f_ward_" + e.Key + "_wall", "ink_throw", C(e.Ink, .9f), 0f, .85f));
            Layer(root.transform, new L { Name = "Wall", Mat = wall, World = false, Mode = 2, Rate = 14f, Max = 24, Life = new Vector2(1.1f, 1.4f), Size = new Vector2(.9f, 1.2f), Pivot = new Vector3(0f, .45f, 0f),
                Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), SpinRandom = false, Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(c, 0f)), (.2f, C(c, .8f)), (.7f, C(c, .6f)), (1f, C(c, 0f))), Grow = Curve(0f, .7f, .25f, 1f, 1f, 1.05f) });
            Layer(root.transform, new L { Name = "Ring", Mat = Bleed("M_V308f_pool", "ink_pool", C(SwingInkColour, .85f), 0f, .85f, 0f, 0f), World = false, Mode = 1, Rate = 10f, Max = 24, Life = new Vector2(1.6f, 2f),
                Size = new Vector2(.7f, 1f), Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .03f, 0f), Blend = true,
                FrameFrom = 3f, FrameTo = 12f, Colour = InkFade(.6f), Grow = Curve(0f, .7f, .3f, 1f, 1f, 1.1f) });
            return root;
        }

        static UnityEngine.Object LiveEa(string suffix)
        {
            const string dir = "Assets/_Project/Art/World/Architecture296/Data";
            var file = Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + suffix + ".asset").FirstOrDefault() : null;
            return file == null ? null : AssetDatabase.LoadAssetAtPath<ScriptableObject>(file.Replace('\\', '/'));
        }

        static string ConnectAura()
        {
            if (Buffs.Concat(Wards).Any(g => !File.Exists(AuraEffectPath(g.Id)))) return "aura: not built (run aura) - left as it is";
            var buff = LiveEa("EABuffs_TEST") as EABuffProfileSO; var ward = LiveEa("EAWards_TEST") as EAWardProfileSO;
            if (buff == null || ward == null) return "aura: live EA buff / ward profile not found";
            if (!File.Exists(AuraRecord))
            {
                var rec = new AuraRecorded();
                rec.buffs.AddRange(buff.PresentationPrefabs.Select(x => AssetDatabase.GetAssetPath(x)));
                rec.wards.AddRange(ward.PresentationPrefabs.Select(x => AssetDatabase.GetAssetPath(x)));
                File.WriteAllText(AuraRecord, JsonUtility.ToJson(rec, true));
            }
            Undo.RecordObjects(new Object[] { buff, ward }, "connect aura");
            buff.PresentationPrefabs = Buffs.Select(g => Load<GameObject>(AuraEffectPath(g.Id))).ToArray();   // 걱 넉 먹 석 억 (EABuffRuntime.Letters)
            ward.PresentationPrefabs = Wards.Select(g => Load<GameObject>(AuraEffectPath(g.Id))).ToArray();   // 구 누 무 수 우
            EditorUtility.SetDirty(buff); EditorUtility.SetDirty(ward); AssetDatabase.SaveAssetIfDirty(buff); AssetDatabase.SaveAssetIfDirty(ward);
            return "aura: buffs 걱 넉 먹 석 억 / wards 구 누 무 수 우 -> #308 loop bodies";
        }

        static string RevertAura()
        {
            if (!File.Exists(AuraRecord)) return "aura: nothing recorded";
            var buff = LiveEa("EABuffs_TEST") as EABuffProfileSO; var ward = LiveEa("EAWards_TEST") as EAWardProfileSO;
            if (buff == null || ward == null) return "aura: live EA buff / ward profile not found (record kept)";
            var rec = JsonUtility.FromJson<AuraRecorded>(File.ReadAllText(AuraRecord));
            GameObject At(string p) => string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(p);
            buff.PresentationPrefabs = rec.buffs.Select(At).ToArray(); ward.PresentationPrefabs = rec.wards.Select(At).ToArray();
            EditorUtility.SetDirty(buff); EditorUtility.SetDirty(ward); AssetDatabase.SaveAssetIfDirty(buff); AssetDatabase.SaveAssetIfDirty(ward);
            File.Delete(AuraRecord);
            return "aura: restored " + rec.buffs.Count + " buff + " + rec.wards.Count + " ward presentations";
        }
    }
}
