using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-88 / Spec 11.31: VFX Graph bodies. A spell body prefab may carry a VisualEffect child; the area seam plays it when the
    // emit window opens and stops it when it closes (Vfx120Effect.AreaRemake). Values go into the component's property sheet as
    // overrides of the graph's exposed properties, so the graph asset itself is shared and untouched.
    public static partial class Vfx308Build
    {
        // the package sample (VFXGraph Additions, Unity Companion License), imported as Package Manager would place it
        const string SampleSmoke = "Assets/Samples/Visual Effect Graph/17.3.0/VFXGraph Additions/VFX/Smoke.vfx";

        static VisualEffect GraphHost(Transform parent, string name, string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            if (asset == null) { Debug.LogWarning("[Vfx308Build] VFX Graph asset missing: " + assetPath + " (import the VFX Graph Additions sample)"); return null; }
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var v = go.AddComponent<VisualEffect>();
            v.visualEffectAsset = asset;
            v.initialEventName = "";                 // nothing at instantiate (= cast start); the seam sends Play at the emit window
            return v;
        }

        // property sheet override: find or add the entry named `name` in m_PropertySheet.<field>.m_Array and mark it overridden
        static SerializedProperty Sheet(SerializedObject so, string field, string name)
        {
            var arr = so.FindProperty("m_PropertySheet." + field + ".m_Array");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("m_Name").stringValue == name) { e.FindPropertyRelative("m_Overridden").boolValue = true; return e.FindPropertyRelative("m_Value"); }
            }
            arr.arraySize++;
            var n = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            n.FindPropertyRelative("m_Name").stringValue = name; n.FindPropertyRelative("m_Overridden").boolValue = true;
            return n.FindPropertyRelative("m_Value");
        }

        static void SetF(SerializedObject so, string name, float x) => Sheet(so, "m_Float", name).floatValue = x;
        static void SetV2(SerializedObject so, string name, Vector2 x) => Sheet(so, "m_Vector2f", name).vector2Value = x;
        static void SetV3(SerializedObject so, string name, Vector3 x) => Sheet(so, "m_Vector3f", name).vector3Value = x;
        static void SetCurve(SerializedObject so, string name, AnimationCurve x) => Sheet(so, "m_AnimationCurve", name).animationCurveValue = x;
        static void SetGrad(SerializedObject so, string name, Gradient x) => Sheet(so, "m_Gradient", name).gradientValue = x;
        static void SetB(SerializedObject so, string name, bool x) => Sheet(so, "m_Bool", name).boolValue = x;
        static void SetObj(SerializedObject so, string name, Object x) => Sheet(so, "m_NamedObject", name).objectReferenceValue = x;

        // 노 smoke (11.31, TEST spike, the sample graph untouched): many small torn soot wisps instead of a few large cards. Origin =
        // the caster (the seam places the host there, facing the fan), thrown forward, slowed by drag so they stop near the reach,
        // lifted a little; ink-grey to soot-dark, see-through, no glow (alpha blend, the colour never above the ink ceiling).
        static bool ConeSmokeGraph(Transform root, Elem e, Atk a)
        {
            // 11.32: the own graph (concepts C + D: turbulence, flat fan, ground plane, 600) when built, else the package sample
            bool own = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(NaSmokeGraph) != null;
            var v = GraphHost(root, "SmokeVfx", own ? NaSmokeGraph : SampleSmoke);
            if (v == null) return false;
            var so = new SerializedObject(v);
            // pq_vfxg2: the wash-based soot read pink (straw-coloured hay cloud) - now soot-dark from the start, the pigment only a
            // hint in the first tenth, sinking to ink (the user's reference = dark torn soot)
            Color soot = Color.Lerp(e.Dark, e.Ink, own ? .6f : .35f), ink = Color.Lerp(e.Dark, e.Ink, own ? .9f : .75f);   // pq_vfxg7: .35 read red-brown haze
            if (own) { var ash = new Color(.16f, .15f, .13f); soot = Color.Lerp(soot, ash, .5f); ink = Color.Lerp(ink, ash, .5f); }   // pq_vfxg8: still brown over the paving -> toward concept C's grey-black soot (TEST)
            var g = new Gradient();
            // 11.32 concept C: a dull, unlit red-brown heart at birth (pigment toward ink, never light) smothered into soot by 0.12
            Color heart = Color.Lerp(e.Pig, e.Ink, .72f);   // pq_vfxg4: .45 read pink across the whole wave
            if (own) g.SetKeys(new[] { new GradientColorKey(heart, 0f), new GradientColorKey(soot, .03f), new GradientColorKey(ink, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, .14f), new GradientAlphaKey(.9f, .3f), new GradientAlphaKey(.75f, .6f), new GradientAlphaKey(0f, 1f) });
            // 11.33 (fire D): the fire tongues own the first ~0.25 s, the soot rises out of them after (alpha 0 until .14 of the life)
            else g.SetKeys(new[] { new GradientColorKey(Color.Lerp(soot, e.Pig, .2f), 0f), new GradientColorKey(soot, .15f), new GradientColorKey(ink, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.7f, .06f), new GradientAlphaKey(.45f, .5f), new GradientAlphaKey(0f, 1f) });
            // pq_vfxg (first Play): the fan's emit window is ~0.3 s, so 150/s gave ~40 soft round puffs that merged into one
            // brown ball. Now the burst fills the graph's capacity (128) inside the window, each quad small and torn (the soot
            // wisp cels as a colour tone), thrown wider so the fan reads as many wisps.
            var wisp = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_soot_wisp_vfx.png");
            if (wisp != null) { SetObj(so, "Main Texture", wisp); SetV2(so, "Flipbook Size", new Vector2(4f, 4f)); SetB(so, "Use Flipbook", true); }
            else Debug.LogWarning("[Vfx308Build] T_V308b_soot_wisp_vfx missing (python Tools/Art/SpellVFX308/smoke308.py soot_wisp_vfx), the sample smoke texture stays");
            float life = 1.6f * a.Slow;
            if (own)
            {
                // concept C (user 2026-10-16): the knot at the brush tip drops onto the ground plane and runs low along it to the
                // target inside ~0.4 s as a narrow heavy streak, then spreads around the target and tears up. Drag sets the reach
                // (v / drag ~ 14 m limit, ~4.5 m at 0.4 s, ~8.5 m at 1 s); the fan block in the graph gives the narrow spread.
                // Initial Position y is set per cast by the seam (the brush tip's height above the ground point).
                SetF(so, "Spawn Rate", 1600f * a.Dense);
                SetV3(so, "Initial Velocity_vector", new Vector3(0f, -.4f, 13f));   // pq_vfxg5: 6.5 stopped 2 m out; pq_vfxg7: 8 + drag .4 hung by the caster
                SetF(so, "Initial Velocity Divergence", .3f);
                SetF(so, "Drag", .9f);
                SetV3(so, "Global Force_vector", new Vector3(0f, -1.6f, 0f));      // pq_vfxg7: -1.2 a head-high cloud; pq_vfxg8: -3 pressed the turbulence flat
                SetV3(so, "Initial Position_position", new Vector3(0f, 1.1f, 0f));
                SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .35f * a.Big), new Keyframe(.35f, .9f * a.Big), new Keyframe(1f, 1.8f * a.Big)));   // knee-high streak, tatters widen at the end
            }
            else
            {
                SetF(so, "Spawn Rate", 420f * a.Dense);
                SetV3(so, "Initial Velocity_vector", new Vector3(0f, .5f, 7.5f));
                SetF(so, "Initial Velocity Divergence", 1.4f);   // pq_vfxg2: 2.4 swelled into a ball; the fan stays a forward throw
                SetF(so, "Drag", 1.4f);
                SetV3(so, "Global Force_vector", new Vector3(0f, .9f, 0f));
                SetV3(so, "Initial Position_position", Vector3.zero);
                SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .35f * a.Big), new Keyframe(.4f, .9f * a.Big), new Keyframe(1f, 1.4f * a.Big)));
            }
            SetF(so, "LifeTime", life);
            SetGrad(so, "Color Over Life", g);
            SetF(so, "Soft Particle Fade Distance", .8f);
            SetV2(so, "Camera Fade Min/Max Distance", new Vector2(.45f, 1.4f));
            SetF(so, "Flipbook Framerate", 16f / life);   // the 16 cels once over the life (torn -> thinned)
            SetV3(so, "Bounds_center", new Vector3(0f, 1.5f, 4.5f));
            SetV3(so, "Bounds_size", new Vector3(12f, 4f, 12f));
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        // 11.33 fire concept D "붓 획이 불붙음" (user 2026-10-16, steps 2-3): a second host on the same graph, property sheet only.
        // Red-pigment flame tongues (fire308.py: CC0 SmallFlame shape, vermilion core -> red-brown body -> soot dry-brush edges,
        // nothing brighter than the pigment, no emission) pour forward and down from the brush tip, run along the ground plane and
        // pile up as a low wall around the reach (v / drag ~ 6.4 m, the 6 m dummy). Upright cels (angle 0); the particle colour
        // only darkens them toward soot late in the life. Fire element only (na); built only when the own graph exists.
        static bool ConeFireGraph(Transform root, Elem e, Atk a)
        {
            if (e.Key != "na" || AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(NaSmokeGraph) == null) return false;
            // 11.34 fire8: the user picked fire7's C (soot-mixed tongues, fire308.py tongue_soot) at its 140/s
            var tongue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_fire_tongue_soot_vfx.png");
            if (tongue == null) { Debug.LogWarning("[Vfx308Build] T_V308b_fire_tongue_soot_vfx missing (python Tools/Art/SpellVFX308/fire308.py tongue_soot)"); return false; }
            var v = GraphHost(root, "FireVfx", NaSmokeGraph);
            if (v == null) return false;
            var so = new SerializedObject(v);
            SetObj(so, "Main Texture", tongue); SetV2(so, "Flipbook Size", new Vector2(8f, 8f)); SetB(so, "Use Flipbook", true);   // fire5: square cels (fire308.py re-pack)
            float life = 1.4f * a.Slow;
            // fire2 (lab_fire1 side stills: the -3 down force landed the tongues by ~3 m and 600/s x 1.6 m merged them into a flat
            // carpet): fast and strongly dragged so they settle near v / drag ~ 6.4 m within ~0.45 s, almost no down force, sparser,
            // growing late = the low wall at the reach
            // fire4 (lab_fire3 side stills: on the ground but a .3-.5 m carpet - the graph's collision plane holds the quad CENTRE on
            // the ground, so half of each camera-facing tongue is buried and the .5 m soft fade eats the rest): poured slightly down
            // from higher up, then a buoyant lift (terminal ~.9 m/s against drag 2.2) raises the centres as they grow, so the tongues
            // stand up at the reach (centre ~.8 m at .45 s, ~1.3 m at the end); soft fade .2
            // fire6 (lab_fire5 side stills: upright tongues at last, but a narrow 1.5 m block standing in FRONT of the dummy, ~.7 m
            // tall, gone to brown by shot 5; concept D frame 3 is a wide bloom around the target): the divergence is a per-axis
            // +-m/s spread, so 2.2 settles the tongues over ~+-1 m around the reach instead of +-.1 m; bigger late size; alpha held
            // full to .7 of life
            SetF(so, "Spawn Rate", 140f * a.Dense);
            // fire18 (user 2026-10-16 "시전자 쪽에서 작게 시작된 불씨가 시전 지역으로 가서 번지는듯한"): the tongues no longer pour from the
            // brush tip - the ember carries the fire there - but rise where it lands (2.6 m out, .5 m up, SeamTiming below) and run
            // on to the reach: 9.5 / drag 2.2 ~ 4.3 m -> settling near 6.9 m (the 17th: 16 from the tip, near 7.3 m)
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, -1f, 9.5f));
            SetF(so, "Initial Velocity Divergence", 2.2f);
            SetF(so, "Drag", 2.2f);
            SetV3(so, "Global Force_vector", new Vector3(0f, 2f, 0f));
            SetV3(so, "Initial Position_position", new Vector3(0f, .9f, 0f));
            SetV2(so, "Angle Min/Max", Vector2.zero);
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .6f * a.Big), new Keyframe(.35f, 1.5f * a.Big), new Keyframe(1f, 2.6f * a.Big)));
            var soot = new Color(.165f, .15f, .135f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, .6f), new GradientColorKey(soot, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .05f), new GradientAlphaKey(1f, .7f), new GradientAlphaKey(0f, 1f) });
            SetGrad(so, "Color Over Life", g);
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .2f);
            SetV2(so, "Camera Fade Min/Max Distance", new Vector2(.45f, 1.4f));
            SetF(so, "Flipbook Framerate", 30f);   // the source flicker's own pace; 60 cels = 2 s, so a tongue never loops in its life
            SetV3(so, "Bounds_center", new Vector3(0f, 1.5f, 4.5f));
            SetV3(so, "Bounds_size", new Vector3(12f, 4f, 12f));
            so.ApplyModifiedPropertiesWithoutUndo();
            SeamTiming(v, -.12f, 2.6f, .5f);   // fire18: on with the near arc, from where the ember lands
            return true;
        }

        // 11.34 fire8 (concept D frame 3 re-read, user 2026-10-16 "과녁 둘레 고리 ... 여러 시트를 이용하고"): three more hosts, each
        // its own graph and sheet. Particles are born only in the seam's ~0.3 s emit window, so the late parts (the ring after
        // the tongues arrive, the column after the ring) are held back by size 0 at the start of their lives. All TEST values.
        static (VisualEffect v, SerializedObject so) RoleHost(Transform root, Elem e, string name, string graph, string sheet, Vector2 cels)
        {
            if (e.Key != "na" || AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(graph) == null) return (null, null);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_fire_" + sheet + "_vfx.png");
            if (tex == null) { Debug.LogWarning("[Vfx308Build] T_V308b_fire_" + sheet + "_vfx missing (python Tools/Art/SpellVFX308/fire308.py " + sheet + ")"); return (null, null); }
            var v = GraphHost(root, name, graph);
            if (v == null) return (null, null);
            var so = new SerializedObject(v);
            SetObj(so, "Main Texture", tex); SetV2(so, "Flipbook Size", cels); SetB(so, "Use Flipbook", true);
            SetV3(so, "Initial Position_position", Vector3.zero);   // the graph's own shape block overwrites the position anyway
            SetV2(so, "Camera Fade Min/Max Distance", new Vector2(.45f, 1.4f));
            return (v, so);
        }
        static Gradient FadeGrad(Color mid, Color end, float inA, float holdA, float peak)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(mid, .6f), new GradientColorKey(end, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inA), new GradientAlphaKey(peak, holdA), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // ⓐ the ring: born on a band round the target, leaning out. fire16 (user 2026-10-16 "네가 자유롭게 만든다면 ... 그렇게
        // 진행해줘"): the painted tongue sheet (fire308.py brush: 4 tongues of brushed ribbons, each a 16-cel life - rises as ink,
        // the red soaks up, sways, parts, dries to soot). The graph stands each tongue on its root and gives it one of the 4 for
        // its whole life; here: shown from RingHide of the life (the poured fire has reached the target), the 15 visible cels
        // once over the rest, size up at once and growing a little, barely drifting
        static bool RingGraph(Transform root, Elem e, Atk a)
        {
            var (v, so) = RoleHost(root, e, "RingVfx", NaRingGraph, "brush", new Vector2(8f, 8f));
            if (v == null) return false;
            float life = 1.8f * a.Slow;
            SetF(so, "Spawn Rate", 160f * a.Dense);   // ~48 tongues in the ~.3 s emit window, round a 1.4-2.3 m band
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, .12f, 0f));   // with the graph's .06 outward push: the lean, not a climb
            SetF(so, "Initial Velocity Divergence", .02f);
            SetF(so, "Drag", .9f);
            SetV3(so, "Global Force_vector", Vector3.zero);
            SetV2(so, "Angle Min/Max", Vector2.zero);   // drawn along the velocity: any roll breaks the lean
            // zero tangents: a step from nothing to full in .02 of the life (no overshoot), then a slow swell
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(RingHide, 0f), new Keyframe(RingHide + .02f, 1.5f * a.Big), new Keyframe(1f, 1.9f * a.Big)));
            // the sheet carries the colour (ink -> red -> soot): the particle only fades it in and out, a little dimmer at the end
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, new Color(.5f, .47f, .44f), RingHide + .01f, .85f, 1f));
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .15f);
            SetF(so, "Flipbook Framerate", RingCels / ((1f - RingHide) * life));   // the variant's last cel lands on the end of the life
            SetV3(so, "Bounds_center", new Vector3(0f, 1.2f, 6f));
            SetV3(so, "Bounds_size", new Vector3(9f, 4f, 9f));
            so.ApplyModifiedPropertiesWithoutUndo();
            v.GetComponent<VFXRenderer>().sortingOrder = 1;   // over the wash and the pool
            return true;
        }

        // ⓓ the soot wash (puff sheet): soft red-soot puffs filling the disc behind and between the tongues once they stand, so the
        // tongues read against a dark ground (frame 3's maroon-black field) instead of the paving
        static bool WashGraph(Transform root, Elem e, Atk a)
        {
            var (v, so) = RoleHost(root, e, "WashVfx", NaWashGraph, "puff", new Vector2(8f, 8f));
            if (v == null) return false;
            float life = 2.4f * a.Slow;
            SetF(so, "Spawn Rate", 80f * a.Dense);
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, .25f, 0f));
            SetF(so, "Initial Velocity Divergence", .1f);
            SetF(so, "Drag", .5f);
            SetV3(so, "Global Force_vector", Vector3.zero);
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.2f, 0f), new Keyframe(.35f, 1.4f * a.Big), new Keyframe(1f, 2.2f * a.Big)));
            // pigment-tinted soot, see-through: a ground under the tongues, never a wall in front of them (TEST)
            var tint = new Color(.8f, .55f, .5f);
            SetGrad(so, "Color Over Life", FadeGrad(tint, Color.white * .85f, .3f, .6f, .5f));
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .5f);
            SetF(so, "Flipbook Framerate", 63f / life);   // the puff sheet's 64 cels once (dark young -> ash)
            SetV3(so, "Bounds_center", new Vector3(0f, 1.6f, 6.4f));
            SetV3(so, "Bounds_size", new Vector3(8f, 5f, 8f));
            so.ApplyModifiedPropertiesWithoutUndo();
            v.GetComponent<VFXRenderer>().sortingOrder = 0;
            return true;
        }

        // ⓔ the ground pool (pool sheet, fire308.py: one 16-cel life - the strokes painted on round the target as ink, the red
        // soaking up, held, dried to soot and lifted in patches): two big blots laid flat, turned at random, outliving the fire
        static bool PoolGraph(Transform root, Elem e, Atk a)
        {
            var (v, so) = RoleHost(root, e, "PoolVfx", NaPoolGraph, "pool", new Vector2(4f, 4f));
            if (v == null) return false;
            float life = 3.2f * a.Slow;
            SetF(so, "Spawn Rate", 7f);   // ~2 blots in the ~.3 s emit window; more turns the strokes into mush
            SetV3(so, "Initial Velocity_vector", Vector3.zero);
            SetF(so, "Initial Velocity Divergence", 0f);
            SetF(so, "Drag", 0f);
            SetV3(so, "Global Force_vector", Vector3.zero);
            SetV2(so, "Angle Min/Max", new Vector2(0f, 360f));
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, 6.5f * a.Big), new Keyframe(1f, 6.5f * a.Big)));   // the sheet paints its own growth
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .04f, .85f, 1f));
            SetF(so, "LifeTime", life);
            SetF(so, "Flipbook Framerate", 15f / life);
            SetV3(so, "Bounds_center", new Vector3(0f, .5f, 6f));
            SetV3(so, "Bounds_size", new Vector3(8f, 2f, 8f));
            so.ApplyModifiedPropertiesWithoutUndo();
            v.GetComponent<VFXRenderer>().sortingOrder = -1;
            return true;
        }

        // fire17: the band sheet is 3072 x 3520 (8 x 8 cels of 384 x 440) - the default import would shrink it to 2048 and round
        // it to a power of two, resampling the cel grid; the sim leaves black RGB under zero alpha, so alpha-is-transparency
        // bleeds the colour out before the mips (else the tongue edges darken with distance, as the leaf atlas did)
        static void FireSheetImport(string sheet, int max)
        {
            string path = "Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_fire_" + sheet + "_vfx.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
            if (imp.maxTextureSize == max && imp.npotScale == TextureImporterNPOTScale.None && imp.sRGBTexture && imp.alphaIsTransparency
                && imp.wrapMode == TextureWrapMode.Clamp && imp.mipmapEnabled) return;
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
            imp.npotScale = TextureImporterNPOTScale.None; imp.maxTextureSize = max; imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
        }

        // ⓕ the fire bank (fire17): ~28 quads of the baked fluid band round the target, born in the ~.3 s emit window and standing
        // still; each plays its variant's 16 cels once, cel 0 at birth to cel 15 at death
        static bool BankGraph(Transform root, Elem e, Atk a)
        {
            if (e.Key == "na") FireSheetImport("band", 4096);
            var (v, so) = RoleHost(root, e, "BankVfx", NaBankGraph, "band", new Vector2(8f, 8f));
            if (v == null) return false;
            float life = 1.8f * a.Slow;
            SetF(so, "Spawn Rate", 95f * BankWiden * a.Dense);   // ~28 in the emit window (the lab composite's count); 11.69 x the longer band
            SetV3(so, "Initial Velocity_vector", Vector3.zero);
            SetF(so, "Initial Velocity Divergence", 0f);
            SetF(so, "Drag", 0f);
            SetV3(so, "Global Force_vector", Vector3.zero);
            SetV2(so, "Angle Min/Max", Vector2.zero);   // upright: the sim's heat rises up the cel
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, 3.85f * a.Big), new Keyframe(1f, 3.85f * a.Big)));   // the sheet paints its own growth
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .03f, .9f, 1f));
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .3f);
            SetF(so, "Flipbook Framerate", (BankCels - 1f) / life);   // never reaches the next variant's first cel
            SetV3(so, "Bounds_center", new Vector3(0f, 2.6f, BankCentre));   // 11.69 the moved, wider ring
            SetV3(so, "Bounds_size", new Vector3(12f, 5.8f, 12f));
            so.ApplyModifiedPropertiesWithoutUndo();
            v.GetComponent<VFXRenderer>().sortingOrder = 1;
            return true;
        }

        // fire18 (user 2026-10-16 "시전자 쪽에서 작게 시작된 불씨가 시전 지역으로 가서 번지는듯한 느낌으로"): per-host timing for the
        // area seam (Vfx308SeamTiming, display only): lead = seconds the host's emit window opens earlier (+) or later (-) than the
        // shared one; forward / height = where its Initial Position goes (height < 0 = the brush tip)
        static void SeamTiming(VisualEffect v, float lead, float forward = 0f, float height = -1f)
        {
            var t = v.gameObject.AddComponent<Oheangbu.App.SpellVFX120.Vfx308SeamTiming>();
            t.Lead = lead; t.Forward = forward; t.Height = height;
        }

        // ⓕ' the bank as five arcs (fire18): the 17th bank, cut so the near side catches first where the ember lands, then the
        // sides, then the far side (BankArcs: phi, seeds, lead). 19/s each = the 17th's 95/s; each arc keeps the 17th's life,
        // size, fade and pace, so once all five are up the fire is the 17th's
        static void BankArcGraphs(Transform root, Elem e, Atk a)
        {
            if (e.Key == "na") FireSheetImport("band", 4096);
            foreach (var arc in BankArcs)
            {
                var (v, so) = RoleHost(root, e, arc.name, arc.path, "band", new Vector2(8f, 8f));
                if (v == null) continue;
                float life = 1.8f * a.Slow;
                SetF(so, "Spawn Rate", 19f * BankWiden * a.Dense);   // 11.69: the 17th's density along the longer band
                SetV3(so, "Initial Velocity_vector", Vector3.zero);
                SetF(so, "Initial Velocity Divergence", 0f);
                SetF(so, "Drag", 0f);
                SetV3(so, "Global Force_vector", Vector3.zero);
                SetV2(so, "Angle Min/Max", Vector2.zero);
                SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, 3.85f * a.Big), new Keyframe(1f, 3.85f * a.Big)));
                SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .03f, .9f, 1f));
                SetF(so, "LifeTime", life);
                SetF(so, "Soft Particle Fade Distance", .3f);
                SetF(so, "Flipbook Framerate", (BankCels - 1f) / life);
                SetV3(so, "Bounds_center", new Vector3(0f, 2.6f, BankCentre));   // 11.69 the moved, wider ring
                SetV3(so, "Bounds_size", new Vector3(12f, 5.8f, 12f));
                so.ApplyModifiedPropertiesWithoutUndo();
                v.GetComponent<VFXRenderer>().sortingOrder = 1;
                SeamTiming(v, arc.lead);
            }
        }

        // ⓗ the ember (fire18): four small soot-edged tongues (tongue_soot) thrown once from the brush tip at the very start of the
        // cast (lead +.28: its window is open at t=0; the spawner is one burst at Play, rate 0, so only the opening matters),
        // falling on a short arc onto the ring's near side (~3.3 m)
        // as it catches (.40). Shrinks away as it lands - no trail, nothing left on the ground (the road mark stays removed)
        static bool EmberGraph(Transform root, Elem e, Atk a)
        {
            var (v, so) = RoleHost(root, e, "EmberVfx", NaEmberGraph, "tongue_soot", new Vector2(8f, 8f));
            if (v == null) return false;
            const float life = .43f;   // the flight time to the near band: y lift -> ground with vy -1 and g 9.8
            SetF(so, "Spawn Rate", 0f);   // only the graph's one burst at Play
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, -1f, 6.25f));
            SetF(so, "Initial Velocity Divergence", .35f);
            SetF(so, "Drag", 0f);
            SetV3(so, "Global Force_vector", new Vector3(0f, -9.8f, 0f));
            SetV2(so, "Angle Min/Max", new Vector2(-15f, 15f));
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .25f), new Keyframe(.7f, .75f), new Keyframe(.88f, .9f), new Keyframe(1f, .2f)));
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .06f, .85f, 1f));
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .1f);
            SetV2(so, "Camera Fade Min/Max Distance", new Vector2(.45f, 1.4f));     // D308-109 audit: nothing inside .45 m of the camera (was .3 - .9)
            SetF(so, "Flipbook Framerate", 30f);
            SetV3(so, "Bounds_center", new Vector3(0f, 1f, 2f));
            SetV3(so, "Bounds_size", new Vector3(3f, 3f, 5f));
            so.ApplyModifiedPropertiesWithoutUndo();
            v.GetComponent<VFXRenderer>().sortingOrder = 2;
            SeamTiming(v, .28f, .6f);
            return true;
        }

        // 11.37 (g) D308-104 size caps (TEST, 시안 대기): the ring band is .32 r but never wider than 1.0 m, the stain / mark
        // radius never past r + 1.8 m. For the non-attack builder rows (② graph host: Band Half Width / Stain Size per r) -
        // those rows are not built yet, so nothing calls these but deploy308-unit D35. Paths take no mark; the fan keeps its
        // judgment 40 deg x 10 m. The 노 ring (11.69: r 3.7, band 1.0 = cap, stain mark 5.5 = r + 1.8) sits on both caps.
        public const float AreaBandRelative308 = .32f, AreaBandCap308 = 1f, AreaMarkMargin308 = 1.8f;
        public static float AreaBand308(float r) => Mathf.Min(AreaBandRelative308 * r, AreaBandCap308);
        public static float AreaMark308(float r, float want) => Mathf.Min(want, r + AreaMarkMargin308);

        // ⓖ the stain (fire17): one splash stain laid flat, 10.8 m across (the sheet covers +-5.4 m, its band at r 2.55 under the
        // bank's 2.6 - so not scaled by Big, the bank's ring is fixed in its graph), spreading in over the first .12 of its life
        // and outliving the bank. fire19 (user 2026-10-16 "바닥에 먹자국만 지금의 5퍼센트 수준으로 ... 경계가 너무 뚜렷해서
        // 어색해", "자국도 불 따라 늦게"): peak alpha .05, the stainwipe sheet (stain_wipe19.py, 4x4, near -> far, last cel =
        // the whole stain) run by the graph's texIndex curve, yaw only +-18 so the near side stays toward the caster.
        // fire20 (user 2026-10-16 "10–15 %로 조금 진하게"): peak alpha .05 -> .12 (TEST)
        static bool StainGraph(Transform root, Elem e, Atk a)
        {
            if (e.Key == "na") FireSheetImport("stainwipe", 2048);
            var (v, so) = RoleHost(root, e, "StainVfx", NaStainGraph, "stainwipe", new Vector2(4f, 4f));
            if (v == null) return false;
            float life = 3.4f * a.Slow;
            SetF(so, "Spawn Rate", 30f);   // capacity 1 in the graph
            SetV3(so, "Initial Velocity_vector", Vector3.zero);
            SetF(so, "Initial Velocity Divergence", 0f);
            SetF(so, "Drag", 0f);
            SetV3(so, "Global Force_vector", Vector3.zero);
            SetV2(so, "Angle Min/Max", new Vector2(-18f, 18f));
            // 11.69: grown with the moved ring, but the mark stays inside r + 1.8 m (D308-104: 4.87 m x 1.13 = 5.5 m)
            float stain = 10.8f * Mathf.Min(BankWiden, AreaMark308(BankRadius, 4.87f * BankWiden) / 4.87f);
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, stain * 7.5f / 10.8f), new Keyframe(.12f, stain), new Keyframe(1f, stain)));
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .06f, .75f, .12f));
            SetF(so, "LifeTime", life);
            SetF(so, "Flipbook Framerate", 0f);
            SetV3(so, "Bounds_center", new Vector3(0f, .2f, BankCentre));
            SetV3(so, "Bounds_size", new Vector3(13.5f, 1f, 13.5f));
            so.ApplyModifiedPropertiesWithoutUndo();
            v.GetComponent<VFXRenderer>().sortingOrder = -1;
            SeamTiming(v, -.12f);   // fire18: spreads in with the near arc, after the ember lands
            return true;
        }

        // ⓑ the scorched road: dark dragged brush streaks with wet red in their heads, laid along the cast from the caster to the
        // target. fire11 (user 2026-10-16 "길 자국만 더 손봄" after lab_fire10: the scorch sheet's round grey blots sank into the
        // ground and, spun 0-360, never ran along the road): drag sheet 4x4, one streak variant per particle, yaw only +-12
        static bool ScorchGraph(Transform root, Elem e, Atk a)
        {
            var (v, so) = RoleHost(root, e, "ScorchVfx", NaScorchGraph, "drag", new Vector2(4f, 4f));
            if (v == null) return false;
            float life = 3.5f * a.Slow;
            SetF(so, "Spawn Rate", 160f * a.Dense);   // fire14: the streaks are a third as wide (sheet alpha .17 -> .06), so more of them
            SetV3(so, "Initial Velocity_vector", Vector3.zero);
            SetF(so, "Initial Velocity Divergence", 0f);
            SetF(so, "Drag", 0f);
            SetV3(so, "Global Force_vector", Vector3.zero);
            SetV2(so, "Angle Min/Max", new Vector2(-12f, 12f));
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .9f * a.Big), new Keyframe(.15f, 1.1f * a.Big), new Keyframe(1f, 1.2f * a.Big)));
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .04f, .8f, .95f));
            SetF(so, "LifeTime", life);
            SetF(so, "Flipbook Framerate", 0f);   // the cel picked at birth stays: each streak is its own drawing, not a sequence
            SetV3(so, "Bounds_center", new Vector3(0f, .5f, 3.8f));   // the streaks reach ~3.6 m along the road past each end
            SetV3(so, "Bounds_size", new Vector3(5f, 2f, 13f));
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        // ⓒ the soot column (puff sheet, fire9: soft round soot puffs, dark young -> ash; the DiscSmoke "column" cels were narrow and
        // flat-bottomed and read as drifting rectangles in fire8): rising behind the target's right shoulder
        static bool ColumnGraph(Transform root, Elem e, Atk a)
        {
            var (v, so) = RoleHost(root, e, "ColumnVfx", NaColumnGraph, "puff", new Vector2(8f, 8f));
            if (v == null) return false;
            float life = 2.2f * a.Slow;
            SetF(so, "Spawn Rate", 18f * a.Dense);   // fire10: a wisp, not fire9's ball (50 puffs up to 3.2 m)
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, 1.6f, 0f));
            SetF(so, "Initial Velocity Divergence", .15f);
            SetF(so, "Drag", .3f);
            SetV3(so, "Global Force_vector", new Vector3(0f, .4f, 0f));
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.3f, 0f), new Keyframe(.4f, .6f * a.Big), new Keyframe(1f, 1.6f * a.Big)));
            SetGrad(so, "Color Over Life", FadeGrad(Color.white, Color.white, .35f, .6f, .55f));
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .4f);
            SetF(so, "Flipbook Framerate", 64f / life);
            SetV3(so, "Bounds_center", new Vector3(1.2f, 4.5f, 7.8f));
            SetV3(so, "Bounds_size", new Vector3(5f, 8f, 5f));
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
