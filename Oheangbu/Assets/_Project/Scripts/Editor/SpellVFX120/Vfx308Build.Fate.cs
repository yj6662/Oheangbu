using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // DECISIONS D308-125 적용 6: where a cover or a domain stops a stone an enemy already threw (the seam's Burst(letter, point)
    // at the stop). Four short bodies, one per glyph, told apart by form; every layer one shot, local, gone inside 1 s, no glow,
    // nothing left on the ground (the presenter faces local +Z to the eye, local up = world up).
    //   막 (ma) - at the rock face: one ink splash as wide as the 1.2 m rock, two earth and two ink lumps knocked off, falling and
    //             shrinking away, a short earth-dust puff; no drips, no grey stone chips
    //   공 (ga) - on the bamboo front: a small ink contact, then split cane shards and two cane stubs flung off and falling, leaf
    //             bits drifting down, all shrinking away (contact -> break -> gone)
    //   눗 (na) - in the ring: one straight tongue (the brush sheet's upright cels only - the later ones hook) rising round the
    //             stone in .25 s, soot-dark, with a thin #B8432F dry-brush rim behind it (the only vermilion, about 1/5 of the
    //             tongue) that comes apart into soot wisps and fine ash
    //   숭 (sa) - in the ring: one blade and one diagonal metal stroke across the stone, no clump (상 / 송 are radial: a clump
    //             and six blades); blue-grey #6F7A80. Its stroke back to the shooter is Return308Prefab (a Vfx308Stroke line).
    //   fate           - build the four prefabs (and 숭's stroke) and point the four profiles at them
    public static partial class Vfx308Build
    {
        static readonly (string glyph, string key, string id, bool attack)[] Fates = { ("막", "ma", "050_B9C9", true), ("공", "ga", "018_ACF5", true), ("눗", "na", "047_B217", false), ("숭", "sa", "096_C22D", false) };
        internal static string FatePath(string key) => Root2 + "/Prefabs/PF_V308i_fate_" + key + ".prefab";
        internal static string FateProfilePath(string id) { var a = Attack.FirstOrDefault(x => x.Effect == id); return a != null && Fates.Any(f => f.id == id && f.attack) ? AttackProfilePath(a) : FieldBirthProfilePath(id); }
        internal static (string glyph, string key, string id)[] FateTable() => Fates.Select(f => (f.glyph, f.key, f.id)).ToArray();
        internal static string FateReturnPath => Root2 + "/Prefabs/PF_V308i_fate_sa_return.prefab";
        static readonly Color FateMetal = new Color(.435f, .478f, .502f);   // #6F7A80, the 상 / 송 blue-grey
        const float FateTilt = 40f;                                       // 숭: the stroke's slant on screen, degrees

        static string BuildFate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build fate: Edit mode only");
            V2 = true; Touched.Clear(); seed = 47400;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_splat", "ink_drops", "earth_dust", "ink_throw", "soot_wisp", "speed_line" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var (glyph, key, id, attack) in Fates)
            {
                var e = Element(kit, key); kit.Ink = e.Ink;
                var body = key == "ma" ? FateRockBody(e) : key == "ga" ? FateCaneBody(e) : key == "na" ? FateTongueBody(e) : FateBladeBody(e);
                OneShot(body);
                var prefab = SaveWrap(body, FatePath(key));
                string profilePath = FateProfilePath(id);
                var profile = File.Exists(profilePath) ? Load<Vfx120Profile>(profilePath) : null;
                if (profile == null) { sb.AppendLine(glyph + ": " + profilePath + " missing - " + prefab.name + " built, not assigned"); continue; }
                profile.Burst308Prefab = prefab;
                if (key == "sa") profile.Return308Prefab = SaveWrap(FateReturnStroke(e), FateReturnPath);
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(glyph + " fate -> " + FatePath(key) + (key == "sa" ? " + " + FateReturnPath : "") + " on " + profilePath);
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static GameObject FateRockBody(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(FatePath(e.Key)));
            var t = root.transform;
            Color dark = Color.Lerp(e.Pig, e.Ink, .82f), earth = Color.Lerp(e.Pig, e.Ink, .55f);
            // the splash on the rock face, about as wide as the rock is tall, so it shows round the hump
            Layer(t, new L { Name = "Splash", Mat = e.Pool, World = false, Burst = 1, Max = 1, Life = new Vector2(.75f, .85f), Size = new Vector2(1.5f, 1.75f),
                NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 9f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(e.Ink, .92f)), (.45f, C(dark, .8f)), (1f, C(dark, 0f))), Grow = Curve(0f, .4f, .12f, 1f, 1f, 1.08f) });
            // lumps knocked off the face, up and out over the hump: two of earth, two of ink, falling and shrinking to nothing
            Layer(t, new L { Name = "Clods", Mat = e.Solid, Mesh = e.Spike, World = false, Burst = 2, BurstTime = .03f, Max = 2, Life = new Vector2(.65f, .8f),
                Size = new Vector2(.34f, .45f), Speed = new Vector2(1.2f, 2.2f), Gravity = 1.2f, Shape = ParticleSystemShapeType.Cone, Radius = .3f, Angle = 35f, Thickness = 1f,
                ShapeRot = new Vector3(-60f, 0f, 0f), Tumble = true, Spin = 160f, Grow = Curve(0f, .5f, .1f, 1f, .6f, 1f, 1f, 0f) });
            Layer(t, new L { Name = "Lumps", Mat = e.Line, Mesh = e.Bit, World = false, Burst = 2, BurstTime = .05f, Max = 2, Life = new Vector2(.6f, .75f),
                Size = new Vector2(.3f, .4f), Speed = new Vector2(1f, 1.9f), Gravity = 1.2f, Shape = ParticleSystemShapeType.Cone, Radius = .3f, Angle = 35f, Thickness = 1f,
                ShapeRot = new Vector3(-60f, 0f, 0f), Tumble = true, Spin = 140f, Grow = Curve(0f, .5f, .1f, 1f, .6f, 1f, 1f, 0f) });
            // a short earth-dust puff behind the splash
            var dust = Layer(t, new L { Name = "Dust", Mat = Bleed("M_V308i_fate_ma_dust", "earth_dust", C(e.Ink, .85f), 0f, .55f, 0f, .3f), World = false, Burst = 1, BurstTime = .04f, Max = 1,
                Life = new Vector2(.6f, .7f), Size = new Vector2(1.3f, 1.5f), NoShape = true, Blend = true, FrameFrom = 2f, FrameTo = 13f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(earth, 0f)), (.15f, C(earth, .5f)), (.6f, C(earth, .3f)), (1f, C(earth, 0f))), Grow = Curve(0f, .6f, 1f, 1.15f) });
            dust.GetComponent<ParticleSystemRenderer>().sortingFudge = 40f;
            return root;
        }

        static GameObject FateCaneBody(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(FatePath(e.Key)));
            var t = root.transform;
            Color dark = Color.Lerp(e.Pig, e.Ink, .75f);
            // contact: a small dark ink nick where the stone meets the canes
            Layer(t, new L { Name = "Contact", Mat = e.Pool, World = false, Burst = 1, Max = 1, Life = new Vector2(.4f, .5f), Size = new Vector2(.7f, .85f),
                NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 8f, RotDeg = new Vector2(0f, 360f),
                Colour = G((0f, C(e.Ink, .9f)), (.4f, C(dark, .75f)), (1f, C(dark, 0f))), Grow = Curve(0f, .5f, .1f, 1f, 1f, 1.05f) });
            // break: split cane shards flung off the front, tumbling, falling, gone
            Layer(t, new L { Name = "Shards", Mat = e.StrokeSolid, Mesh = e.Bit, World = false, Burst = 5, BurstTime = .06f, Max = 5, Life = new Vector2(.55f, .75f),
                Size = new Vector2(.2f, .3f), Size3D = new Vector3(1f, 1f, 1.6f), Speed = new Vector2(2f, 3.5f), Gravity = 1.5f, Shape = ParticleSystemShapeType.Cone, Radius = .2f, Angle = 45f, Thickness = 1f,
                ShapeRot = new Vector3(-65f, 0f, 0f), Tumble = true, Spin = 300f, Grow = Curve(0f, 1f, .65f, 1f, 1f, 0f) });
            // two cane stubs, heavier and slower
            Layer(t, new L { Name = "Stubs", Mat = e.StrokeSolid, Mesh = e.Stalk, World = false, Burst = 2, BurstTime = .07f, Max = 2, Life = new Vector2(.6f, .8f),
                Size = new Vector2(.18f, .24f), Speed = new Vector2(1f, 1.8f), Gravity = 1.8f, Shape = ParticleSystemShapeType.Cone, Radius = .15f, Angle = 40f, Thickness = 1f,
                ShapeRot = new Vector3(-70f, 0f, 0f), Tumble = true, Spin = 200f, Grow = Curve(0f, 1f, .6f, 1f, 1f, 0f) });
            // leaf bits: flattened thorns drifting down, turning over
            Layer(t, new L { Name = "Leaves", Mat = e.Solid, Mesh = e.Spike, World = false, Burst = 4, BurstTime = .08f, Max = 4, Life = new Vector2(.75f, .85f),
                Size = new Vector2(.12f, .18f), Size3D = new Vector3(1f, .2f, 1.8f), Speed = new Vector2(.8f, 1.6f), Gravity = .35f, Drag = 2.5f, DragLinear = true,
                Shape = ParticleSystemShapeType.Sphere, Radius = .25f, Thickness = 1f, Tumble = true, Spin = 220f, Grow = Curve(0f, .6f, .15f, 1f, .7f, 1f, 1f, 0f) });
            return root;
        }

        static GameObject FateTongueBody(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(FatePath(e.Key)));
            var t = root.transform;
            const string m = "M_V308i_fate_na_";
            // the brush sheet read 8 cels to a variant: variant 0 then plays its first row only, the upright cels (2 - 7 here)
            var grid = new Vector4(8f, 8f, 8f, 0f);
            // linear tints: the core brought down to soot, the rim's brightest strokes to #B8432F
            var core = Paint(m + "core", "brush", 2, grid, new Color(.045f, .06f, .07f), .1f);
            var rim = Paint(m + "rim", "brush", 2, grid, new Color(.59f, .21f, .165f), .1f);
            // the same tongue twice (variant, cels, life and size all fixed): the rim 12 % larger and drawn behind, so only its edge
            // shows round the dark core; the root .55 m under the stone, the painted height (.73 of the quad) reaching past it
            ParticleSystem Tongue(string name, Material mat, float size, Gradient colour, float fudge)
            {
                var ps = Tongues(Layer(t, new L { Name = name, Mat = mat, World = false, Mode = 2, Burst = 1, Max = 1, Life = new Vector2(.6f, .6f), Size = new Vector2(size, size),
                    Shape = ParticleSystemShapeType.Sphere, Radius = .001f, ShapePos = new Vector3(0f, -.55f, 0f), Pivot = TonguePivot, SpinRandom = false, Flipbook = false,
                    Colour = colour, Grow = Curve(0f, .25f, .42f, 1f, 1f, 1f) }), 2f);
                var cd = ps.customData; cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(.5f));   // variant 0, the straightest
                ps.GetComponent<ParticleSystemRenderer>().sortingFudge = fudge;
                return ps;
            }
            Tongue("Rim", rim, 1.1f * 1.12f, G((0f, C(Color.white, 0f)), (.08f, C(Color.white, .9f)), (.4f, C(Color.white, .85f)), (.75f, C(new Color(.6f, .6f, .6f), 0f)), (1f, C(new Color(.6f, .6f, .6f), 0f))), 30f);
            Tongue("Core", core, 1.1f, G((0f, C(Color.white, 0f)), (.08f, C(Color.white, .95f)), (.6f, C(Color.white, .9f)), (1f, C(Color.white, 0f))), 0f);
            // the rim coming apart: soot wisps rising off the stone (the sheet is drawn lying down, so stood up a quarter turn)
            Layer(t, new L { Name = "Soot", Mat = Bleed(m + "soot", "soot_wisp", C(SwingDark, .6f), 0f, .6f, 0f, .3f), World = false, Mode = 2, Burst = 3, BurstTime = .3f, Max = 3,
                Life = new Vector2(.45f, .6f), Size = new Vector2(.35f, .5f), Shape = ParticleSystemShapeType.Sphere, Radius = .18f, Thickness = 1f, ShapePos = new Vector3(0f, .1f, 0f),
                SpinRandom = false, RotDeg = new Vector2(75f, 105f), RiseY = .5f, Blend = true, RandomStart = true, FrameTo = 5f,
                Colour = G((0f, C(EmberAsh, 0f)), (.25f, C(EmberAsh, .4f)), (.7f, C(EmberAsh, .25f)), (1f, C(EmberAsh, 0f))), Grow = Curve(0f, .7f, 1f, 1.3f) });
            // and fine ash, hanging, barely falling
            Layer(t, new L { Name = "Ash", Mat = Bleed(m + "ash", "ink_drops", C(SwingDark, .2f), 0f, .8f, 0f, .1f), World = false, Burst = 10, BurstTime = .28f, Max = 10,
                Life = new Vector2(.5f, .65f), Size = new Vector2(.025f, .05f), Speed = new Vector2(.2f, .6f), Gravity = .1f, Drag = 1f, DragLinear = true,
                Shape = ParticleSystemShapeType.Sphere, Radius = .25f, Thickness = 1f, RandomCell = true,
                Colour = G((0f, C(EmberAsh, 0f)), (.15f, C(EmberAsh, .8f)), (.7f, C(EmberAsh, .6f)), (1f, C(EmberAsh, 0f))) });
            return root;
        }

        static GameObject FateBladeBody(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(FatePath(e.Key)));
            var t = root.transform;
            // the stroke: one thin metal slash across the stone, drawn in its first cels and gone by .45 s
            // (the sheet's body takes the vertex colour, its heart and dried rim the darker metal of _Ink)
            Layer(t, new L { Name = "Slash", Mat = Bleed("M_V308i_fate_sa_slash", "ink_throw", C(Color.Lerp(FateMetal, e.Ink, .55f), .6f), 0f, .9f), World = false, Burst = 1, Max = 1, Life = new Vector2(.4f, .45f),
                Size = new Vector2(1.5f, 1.6f), Size3D = new Vector3(1f, .45f, 1f), NoShape = true, RotDeg = new Vector2(FateTilt, FateTilt), Blend = true, FrameFrom = 0f, FrameTo = 10f,
                Colour = G((0f, C(FateMetal, 0f)), (.1f, C(FateMetal, .95f)), (.55f, C(FateMetal, .6f)), (1f, C(FateMetal, 0f))), Grow = Curve(0f, .85f, .2f, 1f, 1f, 1.04f) });
            // the blade: one, laid along the stroke (its length is the mesh's z), there and gone
            var blade = Layer(t, new L { Name = "Blade", Mat = e.Solid, Mesh = e.Spike, World = false, Burst = 1, BurstTime = .02f, Max = 1, Life = new Vector2(.3f, .34f),
                Size = new Vector2(.3f, .32f), Size3D = new Vector3(1f, 1f, 1.8f), NoShape = true, Grow = Curve(0f, .2f, .15f, 1f, .6f, 1f, 1f, 0f) });
            // a billboard turned +a reads, from the eye (which looks down local -z), as the line through (-cos a, sin a) of local x, y
            float a = FateTilt * Mathf.Deg2Rad;
            var q = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f), Vector3.forward).eulerAngles * Mathf.Deg2Rad;
            var main = blade.main; main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(q.x); main.startRotationY = new ParticleSystem.MinMaxCurve(q.y); main.startRotationZ = new ParticleSystem.MinMaxCurve(q.z);
            return root;
        }

        // 숭, the stroke back to the shooter (Vfx308Stroke, ISpellBurstPresenter308.Return): one speed_line cel stretched over the
        // line, its head (the cel's .24 end, as on the bolt trail) at the shooter; a hairline at the stone thickening into the
        // body, blue-grey with the darker metal in its heart; drawn in .05 s, gone by .3 s
        static GameObject FateReturnStroke(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(FateReturnPath));
            var m = Bleed("M_V308i_fate_sa_return", "speed_line", C(Color.Lerp(FateMetal, e.Ink, .55f), .7f), 0f, 1f, 0f, .3f);
            m.SetTextureScale("_MainTex", new Vector2(.23f, .09f));
            m.SetTextureOffset("_MainTex", new Vector2(.01f, .83f));
            EditorUtility.SetDirty(m);
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = m; line.useWorldSpace = true; line.positionCount = 2; line.SetPositions(new[] { Vector3.zero, Vector3.zero });
            line.widthMultiplier = .14f; line.widthCurve = Curve(0f, 0f, .4f, .55f, .88f, 1f, 1f, .7f);
            line.colorGradient = G((0f, C(FateMetal, 0f)), (.25f, C(FateMetal, .85f)), (1f, C(FateMetal, 1f)));
            line.alignment = LineAlignment.View; line.textureMode = LineTextureMode.Stretch; line.numCapVertices = 0; line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            var stroke = root.AddComponent<Vfx308Stroke>(); stroke.DrawSeconds = .05f; stroke.LifeSeconds = .3f;
            return root;
        }
    }
}
