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
    // D308-126 적용 1 (debate 10, TEST): the 20 domains (결계 - the player's own circle, "safe to stand in") get a rim that rises
    // instead of a flat floor only (user 2026-10-21 "장판 계열 vfx가 바닥에만 붙어있는 게 아니라, 위로 일렁거리는 효과"). One prefab
    // per element, built at 1 m radius; the presenter scales it by the domain's radius and Vfx308RingDensity keeps the count per
    // metre of rim. Grammar: a thin pigment rim on the floor, 12-16 thin ink strands standing on it at 3 m (uneven, each grows
    // 1.4-2 s, sways and unravels), a few motes rising off the rim. Nothing rises within the circle (the player stands there).
    // Heights stay under a figure 8 m away's knee: dense < .5 m, tops <= 1.1 m (earth .8). No glow, no floor glyph.
    //   목 - wood strokes stood up (teal ink sprouts) · 화 - soot strands, dark red only at the root, ash motes
    //   토 - earth dust, lowest and slowest · 금 - metal streaks that shoot up in .3 s and stop, iron dust · 수 - indigo strands, drops that jump
    //   domain - build the five and the 20 _308h copies (Zone308Prefab = the element's rim; connect:attack lists them)
    public static partial class Vfx308Build
    {
        static readonly (string Glyph, string Id, string Key)[] Domains =
        {
            ("군", "021_AD70", "ga"), ("굼", "022_AD7C", "ga"), ("굿", "023_AD7F", "ga"), ("궁", "024_AD81", "ga"),
            ("눅", "044_B205", "na"), ("눔", "046_B214", "na"), ("눗", "047_B217", "na"), ("눙", "048_B219", "na"),
            ("묵", "068_BB35", "ma"), ("문", "069_BB38", "ma"), ("뭇", "071_BB47", "ma"), ("뭉", "072_BB49", "ma"),
            ("숙", "092_C219", "sa"), ("순", "093_C21C", "sa"), ("숨", "094_C228", "sa"), ("숭", "096_C22D", "sa"),
            ("욱", "116_C6B1", "a"), ("운", "117_C6B4", "a"), ("움", "118_C6C0", "a"), ("웃", "119_C6C3", "a"),
        };
        static string DomainPath(string key) => Root2 + "/Prefabs/PF_V308d_" + key + ".prefab";

        static string BuildDomain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build domain: Edit mode only");
            V2 = true; Touched.Clear(); seed = 52600;
            foreach (var d in new[] { "/Prefabs", "/Materials", "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_strand", "ink_pool", "ink_splat", "wood_stroke", "earth_dust", "metal_streak", "ink_drops" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            var rims = new System.Collections.Generic.Dictionary<string, GameObject>();
            foreach (string key in Domains.Select(d => d.Key).Distinct())
            {
                var e = Element(kit, key); kit.Ink = e.Ink;
                rims[key] = DomainBody(key, e, out float dry);
                sb.AppendLine(key + " rim -> " + DomainPath(key) + " (dry " + dry.ToString("0.00") + " s)");
            }
            foreach (var d in Domains)
            {
                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + d.Id + ".asset", srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + d.Id + ".prefab";
                if (!File.Exists(CueProfilePath(d.Id)) && !AssetDatabase.CopyAsset(srcProfile, CueProfilePath(d.Id))) throw new Exception("copy failed " + srcProfile);
                var src = Load<Vfx120Profile>(srcProfile);
                var profile = Load<Vfx120Profile>(CueProfilePath(d.Id));
                profile.NativeCastPrefab = null; profile.NativeImpactPrefab = null; profile.NativeBodyPrefab = null; profile.BotanicalPrefab = null;
                profile.WardKind = Vfx120WardKind.None; profile.Aura308ByWardRadius = false; profile.AreaRemake = Vfx120AreaRemake.None;
                profile.KtpContactBrightness = 1f; profile.Duration = 3f;
                profile.Aura308Prefab = null; profile.Bolt300Prefab = null;
                profile.Zone308Prefab = rims[d.Key];
                // D308-125 적용 6 writes the stone-stop picture to the source profile (눗 숭), so it is carried over on every run
                profile.Burst308Prefab = src.Burst308Prefab; profile.Return308Prefab = src.Return308Prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                if (!File.Exists(CueEffectPath(d.Id)) && !AssetDatabase.CopyAsset(srcEffect, CueEffectPath(d.Id))) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(CueEffectPath(d.Id));
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + CueEffectPath(d.Id));
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, CueEffectPath(d.Id));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(d.Glyph + " -> " + CueEffectPath(d.Id) + " (rim " + d.Key + ")");
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static GameObject DomainBody(string key, Elem e, out float dryFade)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(DomainPath(key)));
            try
            {
                var t = root.transform;
                string m = "M_V308d_" + key + "_";
                Color dark = Color.Lerp(e.Pig, e.Ink, .8f), rimInk = Color.Lerp(e.Pig, e.Ink, .45f);
                // per element: the strand sheet, foot and top tones, mote tone, foot height (m), top height (m), the rise's grow curve
                string sheet = "ink_strand"; Color foot = dark, top = Color.Lerp(e.Pig, e.Ink, .65f), mote = dark;
                float footH = .5f, topH = .6f, pivot = .5f, moteRise = .3f, moteGravity = 0f;
                var grow = Curve(0f, .55f, .4f, 1f, 1f, 1f);
                switch (key)
                {
                    case "ga": sheet = "wood_stroke"; pivot = .45f; foot = Color.Lerp(e.Pig, e.Ink, .7f); top = Color.Lerp(e.Pig, e.Ink, .5f); grow = Curve(0f, .2f, .4f, 1f, 1f, .9f); break;
                    case "na":   // soot body, dark red root; vermilion only on the floor rim; ash motes
                        foot = Color.Lerp(Hex("#2A2622"), Hex("#6E2A1F"), .35f); top = Hex("#3E362C"); mote = Hex("#867C6B"); rimInk = Hex("#B8432F"); break;
                    case "ma": sheet = "earth_dust"; pivot = .45f; footH = .45f; topH = .35f; moteRise = .15f; grow = Curve(0f, .4f, .6f, 1f, 1f, 1.05f); break;   // lowest, slowest (top .8 m)
                    case "sa": sheet = "metal_streak"; pivot = .45f; topH = .4f; grow = Curve(0f, .15f, .18f, 1f, 1f, 1f); mote = Color.Lerp(e.Ink, Color.black, .3f); break;   // up in .3 s, then still
                    case "a": foot = Color.Lerp(e.Pig, e.Ink, .85f); top = Color.Lerp(e.Pig, e.Ink, .55f); moteRise = .6f; moteGravity = .3f; break;   // indigo; drops jump and fall
                }
                var pool = Bleed(m + "pool", "ink_pool", C(e.Ink, .85f), 0f, .85f, 0f, 0f);
                var rim = SoakVariant(Bleed(m + "rim", "ink_pool", C(rimInk, .85f), 0f, .8f, 0f, 0f));
                var strand = Bleed(m + "strand", sheet, C(foot, .9f), 0f, .8f, 0f, .3f);
                strand.SetFloat("_NearClip", .6f); strand.SetFloat("_NearFade", 1.6f);   // a player crossing the rim is not blinded
                var drops = Bleed(m + "mote", "ink_drops", C(mote, .9f), 0f, .9f, 0f, .1f);

                // ink first: one dark pour, then a faint stain under the rim (Hierarchy: both widen with the circle)
                Layer(t, new L { Name = "Pour", Mat = pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(1.4f, 1.6f), Size = new Vector2(2f, 2.3f),
                    NoShape = true, ShapePos = new Vector3(0f, .02f, 0f), Blend = true, FrameFrom = 3f, FrameTo = 12f, Colour = InkFade(.5f), Grow = Curve(0f, .55f, .35f, 1f, 1f, 1.1f) });
                Stain(t, pool, Color.Lerp(rimInk, e.Ink, .4f), .1f);
                // the rim line on the floor: Shape scaling, so the stroke stays .3 m wide at any radius (counts per metre: Vfx308RingDensity)
                Density(Layer(t, new L { Name = "Rim", Mat = rim, World = false, Mode = 1, Rate = 4f, Max = 8, Life = new Vector2(2f, 3f), Size = new Vector2(.28f, .36f),
                    Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .03f, 0f), ShapeScaling = true,
                    Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 6f, Colour = G((0f, C(rimInk, 0f)), (.25f, C(rimInk, .7f)), (.75f, C(rimInk, .55f)), (1f, C(rimInk, 0f))) }), 0f);
                // the strands: a dense foot under .5 m and a faint top above it, both standing on the rim, swaying slowly
                Density(Layer(t, new L { Name = "PostFoot", Mat = strand, World = false, Mode = 2, Rate = 2.6f, Max = 5, Life = new Vector2(1.4f, 2f), Size = new Vector2(footH / 1.7f, footH / 1.7f * 1.15f),
                    Size3D = new Vector3(1f, 1.7f, 1f), Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapeScaling = true,
                    Pivot = new Vector3(0f, pivot, 0f), SpinRandom = false, RotDeg = new Vector2(-8f, 8f), Noise = .1f, NoiseFreq = .3f, Blend = true,
                    Colour = G((0f, C(foot, 0f)), (.15f, C(foot, .7f)), (.7f, C(foot, .6f)), (1f, C(foot, 0f))), Grow = grow }), 1.2f);
                Density(Layer(t, new L { Name = "PostTop", Mat = strand, World = false, Mode = 2, Rate = 1.8f, Max = 4, Life = new Vector2(1.4f, 2f), Size = new Vector2(topH / 1.7f, topH / 1.7f * 1.1f),
                    Size3D = new Vector3(1f, 1.7f, 1f), Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapeScaling = true,
                    Pivot = new Vector3(0f, pivot + footH * .9f / topH, 0f), SpinRandom = false, RotDeg = new Vector2(-10f, 10f), Noise = .14f, NoiseFreq = .35f, Blend = true,
                    Colour = G((0f, C(top, 0f)), (.3f, C(top, .28f)), (.7f, C(top, .18f)), (1f, C(top, 0f))), Grow = grow }), 1.2f);
                // a few motes off the rim band (world space: a following circle leaves them briefly behind)
                Density(Layer(t, new L { Name = "Motes", Mat = drops, World = true, Mode = 0, Rate = 1f, Max = 4, Life = new Vector2(3f, 4f), Size = new Vector2(.04f, .08f),
                    Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = .25f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapeScaling = true, RiseY = moteRise, Gravity = moteGravity,
                    RandomCell = true, FrameFrom = 0f, FrameTo = 16f, Colour = G((0f, C(mote, 0f)), (.15f, C(mote, .7f)), (.7f, C(mote, .4f)), (1f, C(mote, 0f))) }), 0f);
                // a broken domain: one ink splat, once (Break is not part of the body)
                var brk = new GameObject("Break").transform; brk.SetParent(t, false);
                Layer(brk, new L { Name = "Splat", Mat = e.Pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.6f, .7f), Size = new Vector2(1.8f, 2.1f),
                    NoShape = true, ShapePos = new Vector3(0f, .035f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 12f, Colour = G((0f, C(e.Ink, .85f)), (.4f, C(dark, .6f)), (1f, C(dark, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1.1f) });

                dryFade = 1.2f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) if (ps.transform.parent == t) dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 0f; body.LateBefore = 2f; body.LateAlpha = .5f; body.DryFade = dryFade;   // pales 2 s before the end (a warning), not at 3 s
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, DomainPath(key));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // the rate and cap above are per metre of rim; the presenter multiplies them by the radius (and drops the strands on a small circle)
        static ParticleSystem Density(ParticleSystem ps, float offBelow)
        {
            var d = ps.gameObject.AddComponent<Vfx308RingDensity>(); d.OffBelow = offBelow;
            return ps;
        }
    }
}
