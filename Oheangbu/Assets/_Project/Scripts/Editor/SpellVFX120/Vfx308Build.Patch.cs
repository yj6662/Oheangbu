using System;
using System.IO;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-125 5 (c), the ember patch an enemy ground eruption leaves (EnemyGroundPatchSO.Look308) and the scorch it puts on
    // the player's hand (ScorchLook308). Presentation only; EmberPatchLook308 places them. Built at 1 m radius - the
    // presenter scales the patch by the rule's Radius (the pool grows with it, the tongues keep their size and only their
    // ring widens) and gives the body the rule's Life. Debate 8: the patch borrows the two painted sheets of the na fire
    // (fire308.py T_V308b_fire_brush_vfx v7, T_V308b_fire_pool_vfx v4) as they are imported - loaded, never reimported (the
    // VFX Graph roles read the same import) - on InkBleed's _Paint modes. Ink first: the pool forms black, then its red rim
    // strokes show; a ring of low ember tongues (pigment, never lit - ART-INK: pollution does not shine) that thin, shorten
    // and darken as the patch burns down, a few soot wisps. Three ways to go: it burns out (the pool crumbles, End: a grey ash
    // ring), it is washed by 온 (Break: the tongues fold away from the player, ink drops, a wet stain - no ash) or it is
    // replaced / cleared (Vfx308ZoneBody.Fade, nothing else). Five materials (the debate's budget was one: the wisps and the
    // drops keep their own sheets), at most 22 particles.
    // The scorch: a few small burn strokes crawling on the shown hand, coloured by the presenter (pigment while it burns,
    // dried ink at the end), a thread of soot rising from it and a "Wash" of drops when 온 puts it out.
    //   patch          - build both prefabs
    public static partial class Vfx308Build
    {
        static string PatchPath => Root2 + "/Prefabs/PF_V308z_ember_patch.prefab";
        static string ScorchPath => Root2 + "/Prefabs/PF_V308z_ember_scorch.prefab";

        static readonly Color EmberPig = new Color(.722f, .263f, .184f), EmberHeat = new Color(.780f, .329f, .212f),
            EmberAsh = new Color(.525f, .486f, .420f), EmberDried = new Color(.165f, .149f, .133f);

        static string BuildPatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build patch: Edit mode only");
            V2 = true; Touched.Clear(); seed = 47300;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_splat", "ink_drops", "soot_wisp" }) Sheet2(s);
            var patch = PatchBody(out float dry);
            var scorch = ScorchBody();
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return "patch -> " + AssetDatabase.GetAssetPath(patch) + " (dry " + dry.ToString("0.00") + " s), scorch -> " + AssetDatabase.GetAssetPath(scorch);
        }

        static GameObject PatchBody(out float dryFade)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(PatchPath));
            try
            {
                var t = root.transform;
                const string m = "M_V308z_ember_";
                Color dark = SwingDark;
                var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                // the brush sheet's brightest strokes brought under the debate's tongue ceiling #D65A43 (linear .672 .102 .056)
                var tongue = Paint(m + "tongue", "brush", 2, new Vector4(8f, 8f, 16f, 0f), new Color(.83f, .38f, .33f), .08f);
                var pool = Paint(m + "pool", "pool", 1, new Vector4(4f, 4f, 16f, 0f), Color.white, 0f);
                // ash: the crumbled pool greyed, its bright specks lifted to the debate's ash ceiling #867C6B (linear lum .21)
                var ash = Paint(m + "ash", "pool", 1, new Vector4(4f, 4f, 16f, 0f), new Color(4.25f, 3.6f, 2.6f), 0f, 1f);
                var wisp = Bleed(m + "wisp", "soot_wisp", C(dark, .6f), 0f, .6f, 0f, .3f);
                var drops = Bleed(m + "drops", "ink_drops", C(dark, .85f), 0f, .9f, 0f, .1f);

                // ink first: the pool forms black where the eruption burst, then its rim strokes redden (the presenter sets its
                // life to the rule's life and the crumbling tail, and its cels and tone along that life)
                var pl = Flat(Layer(t, new L { Name = "Pool", Mat = pool, World = false, Burst = 1, Max = 1, Life = new Vector2(7.2f, 7.2f), Size = new Vector2(2.5f, 2.5f),
                    NoShape = true, Mesh = quad, SpinRandom = false, Blend = true }), 5f);
                var tsa = Sheet16(pl); tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, Oheangbu.App.EmberPatchLook308.PoolFrames(6f));
                var col = pl.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Oheangbu.App.EmberPatchLook308.PoolTone(6f));

                // the burning rim and a few tongues inside it, thinner, shorter and darker as the patch burns down (the presenter
                // sets the duration to the rule's life and the rate's first catch); the tongue's painted height is .73 of the quad
                BurnDown(Tongues(Layer(t, new L { Name = "Ring", Mat = tongue, World = false, Mode = 2, Max = 14, Life = new Vector2(.7f, 1f),
                    Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapeScaling = true, Pivot = TonguePivot, SpinRandom = false,
                    Flipbook = false, Colour = TongueLife() }), 0f), 12f, new Vector2(.55f, .6f), new Vector2(.2f, .22f));
                BurnDown(Tongues(Layer(t, new L { Name = "Inner", Mat = tongue, World = false, Mode = 2, Max = 4, Life = new Vector2(.7f, 1f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .55f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapeScaling = true, Pivot = TonguePivot, SpinRandom = false,
                    Flipbook = false, Colour = TongueLife() }), 0f), 3f, new Vector2(.3f, .36f), new Vector2(.14f, .16f));
                // soot: a few slow wisps rising off the rim (the sheet is drawn lying down, so stood up a quarter turn)
                BurnDown(Layer(t, new L { Name = "Wisps", Mat = wisp, World = false, Mode = 2, Max = 3, Life = new Vector2(1.6f, 2.2f), Size = new Vector2(.35f, .5f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .8f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .3f, 0f), ShapeScaling = true,
                    SpinRandom = false, RotDeg = new Vector2(75f, 105f), RiseY = .3f, Blend = true, RandomStart = true, FrameTo = 5f,
                    Colour = G((0f, C(EmberAsh, 0f)), (.25f, C(EmberAsh, .35f)), (.7f, C(EmberAsh, .25f)), (1f, C(EmberAsh, 0f))), Grow = Curve(0f, .7f, 1f, 1.3f) }), 1.2f, Vector2.zero, Vector2.zero);

                // burnt out: the pool's last cel - its crumbled specks - laid over it in grey, the ash ring
                var end = new GameObject("End").transform; end.SetParent(t, false);
                var ashRing = Flat(Layer(end, new L { Name = "Ash", Mat = ash, World = false, Burst = 1, Max = 1, Life = new Vector2(1.6f, 1.9f), Size = new Vector2(2.5f, 2.5f),
                    NoShape = true, Mesh = quad, SpinRandom = false, Blend = true, Colour = G((0f, C(Color.white, 0f)), (.15f, C(Color.white, .85f)), (.6f, C(Color.white, .7f)), (1f, C(Color.white, 0f))) }), 2f);
                Hold(Sheet16(ashRing), 15f);

                // washed by 온: the tongues fold over away from the player (the presenter turns Break's +Z from the player to the
                // patch; the near half first, the far half a beat later), ink drops kick up, a wet stain - no ash
                var brk = new GameObject("Break").transform; brk.SetParent(t, false);
                for (int i = 0; i < 2; i++)
                {
                    var fold = Tongues(Layer(brk, new L { Name = i == 0 ? "FoldNear" : "FoldFar", Mat = tongue, World = false, Mode = 2, Burst = 4, BurstTime = i == 0 ? .012f : .15f, Max = 4,
                        Life = new Vector2(.35f, .45f), Size = new Vector2(.55f, .68f), Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, i == 0 ? 0f : 180f, 0f),
                        ShapeScaling = true, Pivot = TonguePivot, SpinRandom = false, Flipbook = false,
                        Colour = G((0f, C(Color.white, .95f)), (.5f, C(new Color(.7f, .7f, .7f), .7f)), (1f, C(dark, 0f))) }), 6f);
                    var sh = fold.shape; sh.arc = 180f;
                    var sz = fold.sizeOverLifetime; sz.enabled = true; sz.separateAxes = true;
                    sz.x = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, 1.6f)); sz.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, .15f)); sz.z = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, 1f));
                    var v = fold.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
                    v.x = new ParticleSystem.MinMaxCurve(0f); v.y = new ParticleSystem.MinMaxCurve(0f); v.z = new ParticleSystem.MinMaxCurve(.8f);
                }
                Layer(brk, new L { Name = "Drops", Mat = drops, World = false, Burst = 9, Max = 9, Life = new Vector2(.45f, .6f), Size = new Vector2(.08f, .14f), Speed = new Vector2(1f, 2.2f), Gravity = 1.4f,
                    Shape = ParticleSystemShapeType.Cone, Radius = .6f, Angle = 35f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapeScaling = true, RandomCell = true, Colour = Fade(dark, .4f) });
                // the wet stain: the held pool greyed and darkened, spreading a little as it dries
                var wet = Flat(Layer(brk, new L { Name = "Wet", Mat = ash, World = false, Burst = 1, Max = 1, Life = new Vector2(2f, 2f), Size = new Vector2(2.5f, 2.5f),
                    NoShape = true, Mesh = quad, SpinRandom = false, Blend = true, Colour = G((0f, C(new Color(.3f, .3f, .3f), .8f)), (.5f, C(new Color(.3f, .3f, .3f), .5f)), (1f, C(new Color(.3f, .3f, .3f), 0f))),
                    Grow = Curve(0f, 1f, 1f, 1.15f) }), 2f);
                Hold(Sheet16(wet), 11f);

                // the body dries over the longest of its End / Break layers (the pool's own tail is shorter: the presenter keeps it so)
                dryFade = 2f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) if (ps.name != "Pool") dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 0f; body.EndOnSplash = false; body.DryFade = dryFade;
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, PatchPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // a vertical tongue quad stands on its root: the brush sheet paints it .07 of the cel above the cel's foot
        static readonly Vector3 TonguePivot = new Vector3(0f, .43f, 0f);

        static readonly System.Collections.Generic.List<ParticleSystemVertexStream> PaintStreams = new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1XY,
            ParticleSystemVertexStream.AgePercent,   // InkBleed _Paint 2: TEXCOORD0.zw = (variant, first cel), TEXCOORD1.x = age
        };

        // D308-125 5 (c): a material on one of the painted na fire sheets (fire308.py T_V308b_fire_<sheet>_vfx.png), loaded as
        // it is imported. `tint` is linear (it may lift past 1: the ash); `soft` the depth fade, `desat` 1 greys the sheet first.
        static Material Paint(string name, string sheet, int mode, Vector4 grid, Color tint, float soft, float desat = 0f)
        {
            var gamma = new Color(Mathf.LinearToGammaSpace(tint.r), Mathf.LinearToGammaSpace(tint.g), Mathf.LinearToGammaSpace(tint.b), 1f);
            var m = Bleed(name, null, Color.black, 0f, 1f, 0f, soft, gamma);
            m.SetTexture("_MainTex", Load<Texture2D>(Root2 + "/Textures/T_V308b_fire_" + sheet + "_vfx.png"));
            m.SetFloat("_Paint", mode); m.SetVector("_Grid", grid); m.SetFloat("_Desat", desat);
            EditorUtility.SetDirty(m);
            return m;
        }

        // brush-sheet tongues: one of the four variants at random, its cels from `firstCel` on played once over the life
        // (InkBleed _Paint 2 frames them; the system's own sheet animation stays off)
        static ParticleSystem Tongues(ParticleSystem ps, float firstCel)
        {
            var cd = ps.customData; cd.enabled = true;
            cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 2);
            cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(0f, 3.999f));
            cd.SetVector(ParticleSystemCustomData.Custom1, 1, new ParticleSystem.MinMaxCurve(firstCel));
            var tsa = ps.textureSheetAnimation; tsa.enabled = false;
            ps.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(PaintStreams);
            return ps;
        }

        // a mesh quad laid flat in the patch's plane, drawn under (`fudge` > 0) the tongues and wisps; the presenter lifts it
        // off the ground and turns the whole patch about its normal
        static ParticleSystem Flat(ParticleSystem ps, float fudge)
        {
            var main = ps.main; main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(Mathf.PI * .5f); main.startRotationY = new ParticleSystem.MinMaxCurve(0f); main.startRotationZ = new ParticleSystem.MinMaxCurve(0f);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.sortingFudge = fudge; r.maxParticleSize = 10f;
            return ps;
        }

        // the pool sheet's 4 x 4 cels over the particle's life (a mesh particle: Layer leaves its sheet animation to the caller)
        static ParticleSystem.TextureSheetAnimationModule Sheet16(ParticleSystem ps)
        {
            var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = 4; tsa.numTilesY = 4; tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime; tsa.cycleCount = 1;
            return tsa;
        }

        static void Hold(ParticleSystem.TextureSheetAnimationModule tsa, float cel) { tsa.startFrame = new ParticleSystem.MinMaxCurve(cel); tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); }

        // one tongue's life: it catches from its seed, holds, darkens a little as it thins to wisps
        static Gradient TongueLife() =>
            G((0f, C(Color.white, 0f)), (.08f, C(Color.white, 1f)), (.7f, C(new Color(.85f, .85f, .85f), .9f)), (1f, C(new Color(.6f, .6f, .6f), 0f)));

        // a burning-down layer: emits over one non-looping run (the presenter sets main.duration to the rule's life and, for the
        // tongues, the first catch), its rate falling to a third, its tongues - when sizes are given - shrinking from `from` to
        // `to` and darkening (a tongue's start colour is read along the run: 5 s into 6 at most half as bright)
        static void BurnDown(ParticleSystem ps, float rate, Vector2 from, Vector2 to)
        {
            var main = ps.main; main.loop = false; main.duration = 6f;
            if (from != Vector2.zero)
            {
                main.startSize = new ParticleSystem.MinMaxCurve(1f, Curve(0f, from.x, 1f, to.x), Curve(0f, from.y, 1f, to.y));
                main.startColor = new ParticleSystem.MinMaxGradient(G((0f, Color.white), (.5f, new Color(.78f, .78f, .78f)), (.83f, new Color(.6f, .6f, .6f)), (1f, new Color(.5f, .5f, .5f))));
            }
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(rate, Oheangbu.App.EmberPatchLook308.BurnRate(6f));
        }

        static GameObject ScorchBody()
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(ScorchPath));
            try
            {
                var t = root.transform;
                const string m = "M_V308z_scorch_";
                // on the hand, a hand's breadth from the eye: nothing faded for nearness, no soft depth
                Material Near(Material x) { x.SetFloat("_NearClip", 0f); x.SetFloat("_NearFade", .01f); EditorUtility.SetDirty(x); return x; }
                var strokes = Near(Bleed(m + "strokes", "ink_splat", C(SwingDark, .9f), 0f, .9f, 0f, 0f));
                var smoke = Near(Bleed(m + "smoke", "soot_wisp", C(SwingDark, .5f), 0f, .6f, 0f, 0f));
                var drops = Near(Bleed(m + "drops", "ink_drops", C(SwingDark, .85f), 0f, .9f, 0f, 0f));
                // burn strokes crawling on the hand: white here, the presenter colours them through the start colour
                Layer(t, new L { Name = "Strokes", Mat = strokes, World = false, Rate = 3.5f, Max = 4, Life = new Vector2(1f, 1.3f), Size = new Vector2(.05f, .08f), Radius = .04f,
                    RandomCell = true, FrameFrom = 0f, FrameTo = 8f, Colour = G((0f, C(Color.white, 0f)), (.2f, C(Color.white, .9f)), (.75f, C(Color.white, .85f)), (1f, C(Color.white, 0f))) });
                // a thread of soot rising off the hand, left behind as the hand moves
                Layer(t, new L { Name = "Smoke", Mat = smoke, World = true, Rate = 3f, Max = 3, Life = new Vector2(.9f, 1.2f), Size = new Vector2(.07f, .11f), Radius = .03f,
                    SpinRandom = false, RotDeg = new Vector2(75f, 105f), RiseY = .35f, Blend = true, RandomStart = true, FrameTo = 5f,
                    Colour = G((0f, C(EmberAsh, 0f)), (.25f, C(EmberAsh, .4f)), (1f, C(EmberAsh, 0f))), Grow = Curve(0f, .7f, 1f, 1.4f) });
                var wash = new GameObject("Wash").transform; wash.SetParent(t, false);
                Layer(wash, new L { Name = "Drops", Mat = drops, World = true, Burst = 5, Max = 6, Life = new Vector2(.4f, .55f), Size = new Vector2(.03f, .05f), Speed = new Vector2(.3f, .8f), Gravity = 1.2f, Radius = .04f,
                    RandomCell = true, Colour = Fade(SwingDark, .4f) });
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, ScorchPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
