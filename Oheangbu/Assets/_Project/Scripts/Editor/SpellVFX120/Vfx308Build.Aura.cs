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
                    profile.NativeBodyPrefab = null;   // the old bespoke bodies (fire aura ...) stay out, as for the guards
                    profile.NativeCastPrefab = null; profile.NativeImpactPrefab = null;   // 11.49 / 11.50: Aura308 hid them already - now not built at all
                    profile.KtpContactBrightness = 1f;
                    profile.Aura308FadeSeconds = kind == "ward" ? 1f : .85f;   // 11.49 / 11.50: the hold layer / the dry rim
                    profile.Aura308LateAfter = 3f; profile.Aura308LateAlpha = .45f;   // 11.54 (D308-110): dark first, faint after 3 s
                    profile.Aura308Lasting = false;
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

        // 11.50 buff = a seal at the feet (D308-97 · 98 · 99): one ink thread at the front foot, a wide faint band on a .55 m
        // ring for the whole life (one sheet ≈ .075, overlapped ≈ .15), rare ink-only strokes rising ahead-left / ahead-right of
        // the camera ("Cam" - turned to the camera's yaw at run time) and a dry rim played when the fade starts ("End")
        static GameObject BuffInk(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(AuraPrefabPath("buff", e.Key)));
            Color band = Color.Lerp(e.Pig, e.Ink, .7f), ink = SwingInkColour;
            var throwInk = Bleed("M_V308f_buff_" + e.Key + "_ink", "ink_throw", C(ink, .9f), 0f, .9f);
            var pool = Bleed("M_V308f_buff_" + e.Key + "_band", "ink_pool", C(band, 1f), 0f, 1f, 0f, 0f);
            // a disabled shape emits at the system's own origin (ShapePos is ignored): placed by the transform
            Layer(root.transform, new L { Name = "Thread", Mat = throwInk, World = false, Mode = 1, Burst = 1, BurstTime = .03f, Max = 2, Life = new Vector2(.45f, .6f), Size = new Vector2(.35f, .45f),
                NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(ink, .7f)), (.4f, C(ink, .55f)), (1f, C(ink, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1f) }).transform.localPosition = new Vector3(0f, .02f, .5f);
            Layer(root.transform, new L { Name = "Ring", Mat = pool, World = false, Mode = 1, Rate = 24f, Max = 32, Life = new Vector2(.8f, 1f), Size = new Vector2(.5f, .7f),
                Shape = ParticleSystemShapeType.Circle, Radius = .55f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .015f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 6f,
                Colour = G((0f, C(band, 0f)), (.3f, C(band, .075f)), (.7f, C(band, .06f)), (1f, C(band, 0f))), Grow = Curve(0f, .85f, 1f, 1f) });
            // 11.55 (D308-111): ink wound up the great brush - a local system laid along GripSocket -> TipSocket by AuraBrushWrap308
            // (+Z = up the shaft); the looping arc round the shaft and the drift up it draw a rising spiral, black first, the
            // element's band soaking in toward the tip. No great brush, or the brush not drawn: nothing.
            var brush = Layer(root.transform, new L { Name = "Brush", Mat = throwInk, World = false, Rate = 20f, Max = 28, Life = new Vector2(.8f, .9f), Size = new Vector2(.06f, .09f),
                Shape = ParticleSystemShapeType.Circle, Radius = .035f, Thickness = 0f, ArcSpeed = 1.5f, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(ink, 0f)), (.12f, C(ink, .8f)), (.55f, C(band, .6f)), (1f, C(band, 0f))), Grow = Curve(0f, .6f, .2f, 1f, 1f, .8f) });
            var bv = brush.velocityOverLifetime; bv.enabled = true; bv.space = ParticleSystemSimulationSpace.Local;   // AuraBrushWrap308 sets z = shaft length / life
            bv.x = new ParticleSystem.MinMaxCurve(0f); bv.y = new ParticleSystem.MinMaxCurve(0f); bv.z = new ParticleSystem.MinMaxCurve(1.2f);
            brush.gameObject.AddComponent<AuraBrushWrap308>();
            var cam = new GameObject("Cam").transform; cam.SetParent(root.transform, false);
            foreach (var (name, x) in new[] { ("StrokeL", -.6f), ("StrokeR", .6f) })
                Layer(cam, new L { Name = name, Mat = throwInk, World = false, Mode = 2, Rate = .8f, Max = 3, Life = new Vector2(.7f, .9f), Size = new Vector2(.4f, .5f), RiseY = .6f,
                    NoShape = true, Pivot = new Vector3(0f, .45f, 0f), SpinRandom = false, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                    Colour = G((0f, C(ink, 0f)), (.2f, C(ink, .6f)), (.6f, C(ink, .4f)), (1f, C(ink, 0f))), Grow = Curve(0f, .7f, .3f, 1f, 1f, 1f) }).transform.localPosition = new Vector3(x, 0f, .8f);
            var end = new GameObject("End").transform; end.SetParent(root.transform, false);
            Spread(Layer(end, new L { Name = "Rim", Mat = pool, World = false, Mode = 1, Burst = 16, Max = 16, Life = new Vector2(.7f, .8f), Size = new Vector2(.5f, .6f),
                Shape = ParticleSystemShapeType.Circle, Radius = .55f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .016f, 0f), Blend = true, FrameFrom = 6f, FrameTo = 14f,
                Colour = G((0f, C(band, .20f)), (.5f, C(band, .12f)), (1f, C(band, 0f))), Grow = Curve(0f, 1f, 1f, 1.05f) }), 0f);
            return root;
        }

        // 11.49 ward = the circle drawn (D308-97 · 98): it splits at the front and closes behind - seven arc segments on the 1 m
        // ring (×WardRadius at run time), each one burst: a dark foot membrane, a faint 2.2 m veil (dark at the foot only, so
        // fainter going up) and a ground stain dark only while it is drawn (≤ .045 from .95 s, gone at 3.5 s); then one faint
        // repeating membrane holds the ring for the life. Arc angle a = local (cos a, 0, -sin a): the front (+Z) is 270°.
        static readonly (float From, float Width, float At)[] WardSegments =
        {
            (240f, 60f, .30f),                                    // front
            (300f, 50f, .40f), (350f, 50f, .52f), (40f, 50f, .66f),   // the swung (right) arm leads
            (190f, 50f, .45f), (140f, 50f, .57f), (90f, 50f, .71f),   // the other arm follows .05 s behind and closes at the back
        };

        static GameObject WardInk(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(AuraPrefabPath("ward", e.Key)));
            Color c = e.Key == "sa" ? Color.Lerp(new Color(.651f, .690f, .678f), e.Ink, .3f) : Color.Lerp(e.Pig, e.Ink, .5f);   // 금: light grey-blue, not lost in black
            var wall = SoakVariant(Bleed("M_V308f_ward_" + e.Key + "_wall", "ink_throw", C(e.Ink, .9f), 0f, .9f));
            var stain = Bleed("M_V308f_ward_stain", "ink_pool", C(SwingInkColour, 1f), 0f, 1f, 0f, 0f);
            Color ink = SwingInkColour;
            for (int i = 0; i < WardSegments.Length; i++)
            {
                var (from, width, at) = WardSegments[i];
                var seg = new GameObject("Seg" + i).transform; seg.SetParent(root.transform, false);
                ParticleSystem Arc(L s) { s.Shape = ParticleSystemShapeType.Circle; s.Radius = 1f; s.Thickness = 0f; s.BurstTime = at; var ps = Layer(seg, s); var sh = ps.shape; sh.arc = width; Spread(ps, from); return ps; }
                Arc(new L { Name = "Foot", Mat = wall, World = false, Mode = 2, Burst = 4, Max = 4, Life = new Vector2(1f, 1.2f), Size = new Vector2(.27f, .33f), Pivot = new Vector3(0f, .45f, 0f),
                    SpinRandom = false, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                    Colour = G((0f, C(c, .3f)), (.12f, C(c, .85f)), (.6f, C(c, .7f)), (1f, C(c, 0f))), Grow = Curve(0f, .55f, .2f, 1f, 1f, 1.05f) });
                Arc(new L { Name = "Veil", Mat = wall, World = false, Mode = 2, Burst = 3, Max = 3, Life = new Vector2(1.1f, 1.3f), Size = new Vector2(.66f, .73f), Pivot = new Vector3(0f, .45f, 0f),
                    SpinRandom = false, Blend = true, FrameFrom = 0f, FrameTo = 10f,
                    Colour = G((0f, C(c, 0f)), (.2f, C(c, .05f)), (.7f, C(c, .035f)), (1f, C(c, 0f))), Grow = Curve(0f, .4f, .25f, 1f, 1f, 1.03f) });   // 11.55: the face all but clear
                float life = 3.5f - at, peak = .1f / life, low = (Mathf.Max(.95f, at + .22f) - at) / life;
                Arc(new L { Name = "Stain", Mat = stain, World = false, Mode = 1, Burst = 4, Max = 4, Life = new Vector2(life, life), Size = new Vector2(.32f, .38f), ShapePos = new Vector3(0f, .005f, 0f),
                    Blend = true, FrameFrom = 3f, FrameTo = 12f,
                    Colour = G((0f, C(ink, .3f)), (peak, C(ink, .52f)), (low, C(ink, .045f)), (1f, C(ink, 0f))), Grow = Curve(0f, .7f, peak * 2f, 1f, 1f, 1.08f) });
            }
            var hold = Layer(root.transform, new L { Name = "Hold", Mat = wall, World = false, Mode = 2, Rate = 6f, Max = 8, Life = new Vector2(.9f, 1f), Size = new Vector2(.6f, .73f), Pivot = new Vector3(0f, .45f, 0f),
                Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), SpinRandom = false, Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 8f,
                Colour = G((0f, C(c, 0f)), (.3f, C(c, .035f)), (.7f, C(c, .025f)), (1f, C(c, 0f))), Grow = Curve(0f, .9f, 1f, 1f) });   // 11.55: the face all but clear
            var hm = hold.main; hm.startDelay = 1f;   // once the drawn ring has stood
            // 11.55 (D308-111): the ward reads by its rim - a dark ink line on the ground along the ring for the life (ink first,
            // the element's colour under it), while the faces above stay all but clear; the 3 s late fade thins it with the rest
            Color rim = Color.Lerp(c, ink, .55f);
            var line = Layer(root.transform, new L { Name = "Rim", Mat = stain, World = false, Mode = 1, Rate = 36f, Max = 48, Life = new Vector2(.9f, 1.1f), Size = new Vector2(.22f, .3f),
                Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .012f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 6f,
                Colour = G((0f, C(rim, 0f)), (.25f, C(rim, .55f)), (.7f, C(rim, .45f)), (1f, C(rim, 0f))), Grow = Curve(0f, .9f, 1f, 1f) });
            var lm = line.main; lm.startDelay = .7f;   // as the drawn segments close
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
