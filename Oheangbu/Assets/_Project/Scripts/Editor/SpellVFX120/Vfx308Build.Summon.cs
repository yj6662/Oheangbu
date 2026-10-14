using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.13 (D308-83b, user 2026-10-14 "소환수도 전체적으로 이런형태로" + a picture): the ink body
    // of a summon. A black ink silhouette whose outline bleeds, ink smoke streaming off it, one pale part (the deer's
    // antlers) and one core of element pigment in the chest that only lights for a moment (D308-83c).
    // First prototype: the wood deer (곰). The existing static model is reused - only its materials change - so the
    // prefab keeps the parts WoodDeerVfx asks for (the four Foot_ anchors, a renderer named *Roots*). Not connected.
    //
    //   summon                         build PF_V308c_summon_deer (+ materials)
    //   summon:pose:deer:<age>[:glow]  edit-mode pose at <age> seconds of the summon clock (glow = the core lifted);
    //                                  optional :<px,py,pz>:<fx,fy,fz> = where it stands and faces
    //   summon:pose:clear
    public static partial class Vfx308Build
    {
        const string DeerSource = "Assets/_Project/Art/SpellVFX120/WoodDeer/PF_WoodDeer_Static.prefab";
        const string DeerProfile = "Assets/_Project/Art/SpellVFX120/Profiles/016_ACF0.asset";
        const string SummonPoseName = "__Vfx308SummonPose";
        static string SummonPrefabPath(string key) => Root2 + "/Prefabs/PF_V308c_summon_" + key + ".prefab";

        static Material InkBody(string name, Color core, float accentFrom, Vector3 corePos, float coreRadius, float life)
        {
            string path = Root2 + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Oheangbu/SpellVFX308/InkBody") ?? throw new Exception("InkBody shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_Core", core); m.SetFloat("_AccentFrom", accentFrom); m.SetVector("_CorePos", corePos); m.SetFloat("_CoreRadius", coreRadius);
            m.SetFloat("_Life", life); m.SetFloat("_CoreGlow", 0f);
            // 2nd pass (user: liquid, not solid): no eaten outline (that reads dry), a running surface and a wet sheen instead
            m.SetFloat("_Edge", 0f); m.SetFloat("_GrainScale", 6.5f); m.SetFloat("_WashAmount", .12f); m.SetFloat("_Wobble", .01f);
            m.SetFloat("_Flow", .035f); m.SetFloat("_Sag", .02f); m.SetFloat("_Gloss", .5f);     // the mesh already hangs: the shader only keeps it moving (3rd pass: less sag, the aura breaks the outline)
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        static Material InkStrand(string name, float strands, float thin, float haze, float opacity = .9f)
        {
            string path = Root2 + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Oheangbu/SpellVFX308/InkStrand") ?? throw new Exception("InkStrand shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_InkColor", SwingInkColour); m.SetFloat("_Opacity", opacity); m.SetFloat("_Strands", strands); m.SetFloat("_Thin", thin); m.SetFloat("_Haze", haze);
            m.SetFloat("_Flip", StrandFlip);
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        // Unity's particle trails: UV.x = 0 at the particle (the free end), 1 at the oldest point (the root at the body)
        const float StrandFlip = 1f;

        // one emitter of ink strands. The particles themselves are not drawn - only their trails: a ribbon from where each
        // left the body to where it is now. They drift backward (the summon's -Z) and up, and a noise field curls them.
        static ParticleSystem Strands(Transform root, string name, Material mat, Vector3 worldPos, Vector3 worldSize, float rate, int max, Vector2 life, Vector2 width, Vector2 back, Vector2 up, float curl = 1f)
        {
            var ps = Layer(root, new L { Name = name, Mat = mat, Rate = rate, Max = max, Life = life, Size = width, Flipbook = false, SpinRandom = false,
                Colour = G((0f, new Color(1f, 1f, 1f, 0f)), (.12f, Color.white), (.55f, new Color(1f, 1f, 1f, .85f)), (1f, new Color(1f, 1f, 1f, 0f))) });
            var main = ps.main; main.startDelay = .9f;                              // once the body has risen
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f));     // r = the strand's own seed (the shader reads it; the ink colour is the material's)
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = root.InverseTransformPoint(worldPos); shape.scale = worldSize;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-.18f, .18f); vel.y = new ParticleSystem.MinMaxCurve(up.x, up.y); vel.z = new ParticleSystem.MinMaxCurve(-back.y, -back.x);
            var noise = ps.noise; noise.enabled = true; noise.strength = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .15f, .4f, .8f, 1f, 1.5f)); noise.strengthMultiplier = curl; noise.frequency = .8f; noise.scrollSpeed = .4f; noise.octaveCount = 2; noise.damping = false;     // straight where it leaves the body, curling more the further it gets (first stills: stiff spikes) noise.quality = ParticleSystemNoiseQuality.High;
            var trails = ps.trails; trails.enabled = true; trails.mode = ParticleSystemTrailMode.PerParticle; trails.ratio = 1f; trails.lifetime = 1f;
            trails.minVertexDistance = .07f; trails.worldSpace = true; trails.dieWithParticles = true; trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true; trails.sizeAffectsLifetime = false; trails.inheritParticleColor = true; trails.generateLightingData = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .25f, .35f, 1f, 1f, .6f));     // a fine free end, widest a third in, gathered at the body
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.None; r.trailMaterial = mat;
            return ps;
        }

        static Bounds WorldBounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static string BuildSummon()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build summon: Edit mode only");
            V2 = true; Touched.Clear(); seed = 31000;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            AssetDatabase.Refresh();
            foreach (var sheet in new[] { "ink_pool" }) Sheet2(sheet);
            var profile = Load<Vfx120Profile>(DeerProfile);
            var source = Load<GameObject>(DeerSource);
            var root = new GameObject("PF_V308c_summon_deer");
            var model = (GameObject)Object.Instantiate(source, root.transform); model.name = source.name;
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            var body = model.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name.Contains("Body"));
            var all = WorldBounds(model);
            var bb = body.bounds;
            // the chest: on the body's midline, a little forward of the middle, a little above half the body's height
            Vector3 chestWorld = new Vector3(bb.center.x, bb.min.y + bb.size.y * .5f, bb.center.z + bb.size.z * .16f);
            Vector3 chest = body.transform.InverseTransformPoint(chestWorld);
            // the element's pigment, a little cleaner than the wash colour (LDR; the shader caps the momentary lift)
            Color.RGBToHSV(profile.Pigment, out float hue, out float sat, out float val);
            Color core = Color.HSVToRGB(hue, Mathf.Min(1f, sat * 1.25f), Mathf.Min(.42f, val * 1.05f));
            float accentFrom = (bb.min.y + bb.size.y * .8f - all.min.y) / Mathf.Max(.1f, all.size.y);
            var ink = InkBody("M_V308c_summon_deer_body", core, accentFrom, chest, bb.size.y * .11f, WoodDeerVfx.ReviewLife);
            var sb = new StringBuilder();
            // 2nd pass (user: a mass of liquid, not a solid): the body mesh itself is the melted one (Tools/Blender/SpellVFX308/
            // inkbody308.py - remeshed into one skin, undersides hanging, drops on threads). It is fitted to where the modelled
            // body stood (same height, same feet), and the modelled body is switched off - its object stays for the seam.
            var melted = Model("inkdeer");
            var liquid = new GameObject("InkBody"); liquid.transform.SetParent(model.transform, false);
            liquid.AddComponent<MeshFilter>().sharedMesh = melted;
            var liquidRenderer = liquid.AddComponent<MeshRenderer>(); liquidRenderer.sharedMaterial = ink;
            liquid.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lb = liquidRenderer.bounds;
            if ((lb.size.x > lb.size.z) != (bb.size.x > bb.size.z)) { liquid.transform.rotation = Quaternion.Euler(0f, 90f, 0f); lb = liquidRenderer.bounds; }
            float fit = bb.size.y / Mathf.Max(.01f, lb.size.y);
            liquid.transform.localScale *= fit; lb = liquidRenderer.bounds;
            liquid.transform.position += new Vector3(bb.center.x - lb.center.x, bb.max.y - lb.max.y, bb.center.z - lb.center.z);     // the antlers and the back stay where they were; the hang goes below
            ink.SetVector("_CorePos", liquid.transform.InverseTransformPoint(chestWorld)); EditorUtility.SetDirty(ink);     // the core is given in the drawn mesh's own space
            sb.AppendLine("  InkBody <- " + melted.name + " (" + melted.triangles.Length / 3 + " tris), fit x" + fit.ToString("0.000", CultureInfo.InvariantCulture) + ", yaw " + liquid.transform.eulerAngles.y.ToString("0", CultureInfo.InvariantCulture));
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == liquidRenderer) { }
                else if (r.name.Contains("Body")) r.enabled = false;
                else if (r.name.Contains("Leaves")) r.enabled = false;           // no leaves: one pale part only (the antlers)
                else if (r.name.Contains("Roots")) r.enabled = false;            // no bark roots: the ink pool is the ground it stands on
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sb.AppendLine("  " + r.name + " -> " + (r.enabled ? r.sharedMaterial.name : "off"));
            }
            // 3rd pass (D308-83d, user 2026-10-14: the falling drops are awkward - let the ink's energy spread out of the body;
            // reference: Pinterest pin 12525705207021860). No drops. The ink leaves the body as long hair-fine strands from
            // the mane, the back ridge, the tail and the fetlocks: they stream backward, curl and thin out. Each strand is a
            // particle trail, so its root stays where it left the body.
            var strand = InkStrand("M_V308c_summon_ink_strand", 12f, .8f, .3f);
            var breath = InkStrand("M_V308c_summon_ink_breath", 4f, .5f, .55f, .5f);
            float len = bb.size.z, tall = bb.size.y, wide = bb.size.x;
            Vector3 At(float side, float up, float along) => new Vector3(bb.center.x + side * wide, bb.min.y + up * tall, bb.center.z + along * len);
            // the back ridge and the mane: the main read
            Strands(root.transform, "Aura_Back", strand, At(0f, .6f, -.15f), new Vector3(wide * .3f, tall * .04f, len * .5f), 20f, 44, new Vector2(1.3f, 2.1f), new Vector2(.18f, .38f), new Vector2(.8f, 1.5f), new Vector2(.05f, .4f));     // heights are shares of the body bounds, which include the antlers: the back is at .6
            Strands(root.transform, "Aura_Mane", strand, At(0f, .7f, .2f), new Vector3(wide * .25f, tall * .16f, len * .12f), 12f, 28, new Vector2(1.2f, 1.9f), new Vector2(.16f, .32f), new Vector2(.7f, 1.3f), new Vector2(.1f, .45f));
            // the tail: the longest
            Strands(root.transform, "Aura_Tail", strand, At(0f, .55f, -.45f), new Vector3(wide * .25f, tall * .1f, len * .05f), 10f, 28, new Vector2(1.7f, 2.5f), new Vector2(.2f, .4f), new Vector2(.9f, 1.6f), new Vector2(-.15f, .25f));
            // the fetlocks: short, low, they lift off the ground ink
            var feet = model.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Foot_", StringComparison.Ordinal)).Select(t => t.position).ToList();
            if (feet.Count == 0) foreach (float sx in new[] { -.3f, .3f }) foreach (float sz in new[] { -.34f, .3f }) feet.Add(At(sx, 0f, sz));
            for (int i = 0; i < feet.Count; i++)
                Strands(root.transform, "Aura_Foot" + i, strand, new Vector3(feet[i].x, bb.min.y + tall * .07f, feet[i].z), new Vector3(wide * .14f, tall * .08f, len * .04f), 4f, 8, new Vector2(.6f, 1f), new Vector2(.09f, .18f), new Vector2(.5f, .9f), new Vector2(-.05f, .15f), 1.5f);
            // and a slow wide breath of thin ink round the whole trunk, so the strands sit in a haze instead of on bare air
            Strands(root.transform, "Aura_Breath", breath, At(0f, .5f, -.1f), new Vector3(wide * .45f, tall * .22f, len * .6f), 8f, 22, new Vector2(1.6f, 2.4f), new Vector2(.5f, .9f), new Vector2(.5f, 1f), new Vector2(.1f, .4f));
            sb.AppendLine("  aura: back, mane, tail, " + feet.Count + " fetlocks, breath (trails, no drops)");
            // the ink it rises out of
            Layer(root.transform, new L { Name = "Pool", Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = new Vector2(1.9f, 1.9f), Size = new Vector2(3.4f, 3.4f), NoShape = true, Blend = true,
                ShapePos = new Vector3(0f, .04f, 0f), FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFade(.6f), Grow = Curve(0f, .4f, .2f, 1f, 1f, 1.1f) });
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.layer = AfterFogLayer;     // the ink is drawn after the realm fog; the body stays a world object
            // it stands in its own ink for as long as it is there (the first pool spreads, this one stays)
            var puddle = Layer(root.transform, new L { Name = "Puddle", Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = new Vector2(3.3f, 3.3f), Size = new Vector2(3.0f, 3.0f), NoShape = true, Blend = true,
                ShapePos = new Vector3(0f, .05f, 0f), FrameFrom = 7f, FrameTo = 10f, Colour = G((0f, C(SwingDark, 0f)), (.12f, C(SwingDark, 1f)), (.8f, C(SwingDark, 1f)), (1f, C(SwingDark, 0f))) });
            var pm = puddle.main; pm.startDelay = 1.2f;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.layer = AfterFogLayer;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SummonPrefabPath("deer"));
            Object.DestroyImmediate(root);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return "summon deer -> " + SummonPrefabPath("deer") + "\n" + sb + "  height " + all.size.y.ToString("0.00", CultureInfo.InvariantCulture) + " m, accent from " + accentFrom.ToString("0.00", CultureInfo.InvariantCulture) +
                ", core " + ColorUtility.ToHtmlStringRGB(core) + " r " + (bb.size.y * .16f).ToString("0.00", CultureInfo.InvariantCulture) + " m, particles <= " + prefab.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.main.maxParticles) + "\n  (not connected: the game still summons the bark deer)";
        }

        static void ClearSummonPose()
        {
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x != null && x.name == SummonPoseName && !EditorUtility.IsPersistent(x)).ToArray())
                Object.DestroyImmediate(g);
        }

        static string Summon(string[] a)
        {
            if (a.Length == 1) return BuildSummon();
            if (a[1] != "pose") throw new ArgumentException("summon | summon:pose:deer:<age>[:glow] | summon:pose:clear");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build summon pose: Edit mode only");
            ClearSummonPose();
            if (a.Length > 2 && a[2] == "clear") return "summon pose cleared";
            if (a.Length < 4) throw new ArgumentException("summon:pose:deer:<age>[:glow][:px,py,pz:fx,fy,fz]");
            float age = float.Parse(a[3], CultureInfo.InvariantCulture);
            bool glow = a.Contains("glow");
            var places = a.Skip(4).Where(x => x.Contains(",")).ToArray();
            Vector3 feet = places.Length > 0 ? Vec(places[0]) : new Vector3(3340f, 100f, 2692f);
            feet.y = Ground(feet);
            Vector3 forward = places.Length > 1 ? Vec(places[1]) : Vector3.right;
            forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;
            var prefab = Load<GameObject>(SummonPrefabPath(a[2]));
            var root = Object.Instantiate(prefab, feet, Quaternion.LookRotation(forward, Vector3.up)); root.name = SummonPoseName;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var bounds = WorldBounds(root);
            var block = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetFloat("_Age", age); block.SetFloat("_Ground", feet.y); block.SetFloat("_Height", bounds.max.y - feet.y); block.SetFloat("_CoreGlow", glow ? 1f : 0f);
                r.SetPropertyBlock(block);
            }
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            const float step = 1f / 60f;
            for (float t = 0f; t + step <= age + .00001f; t += step) foreach (var ps in systems) ps.Simulate(step, false, false, false);
            SceneView.RepaintAll();
            return "summon pose " + a[2] + " at " + age.ToString("0.00", CultureInfo.InvariantCulture) + " s" + (glow ? " (core lifted)" : "") + ", feet " + feet + ", height " + (bounds.max.y - feet.y).ToString("0.00", CultureInfo.InvariantCulture) +
                ", particles " + systems.Sum(p => p.particleCount);
        }
    }
}
