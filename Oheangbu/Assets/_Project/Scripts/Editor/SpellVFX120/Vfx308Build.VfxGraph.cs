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
            var tongue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_fire_tongue_vfx.png");
            if (tongue == null) { Debug.LogWarning("[Vfx308Build] T_V308b_fire_tongue_vfx missing (python Tools/Art/SpellVFX308/fire308.py tongue)"); return false; }
            var v = GraphHost(root, "FireVfx", NaSmokeGraph);
            if (v == null) return false;
            var so = new SerializedObject(v);
            SetObj(so, "Main Texture", tongue); SetV2(so, "Flipbook Size", new Vector2(8f, 8f));   // fire5: square cels (fire308.py re-pack) SetB(so, "Use Flipbook", true);
            float life = 1.4f * a.Slow;
            // fire2 (lab_fire1 side stills: the -3 down force landed the tongues by ~3 m and 600/s x 1.6 m merged them into a flat
            // carpet): fast and strongly dragged so they settle near v / drag ~ 6.4 m within ~0.45 s, almost no down force, sparser,
            // growing late = the low wall at the reach
            // fire4 (lab_fire3 side stills: on the ground but a .3-.5 m carpet - the graph's collision plane holds the quad CENTRE on
            // the ground, so half of each camera-facing tongue is buried and the .5 m soft fade eats the rest): poured slightly down
            // from higher up, then a buoyant lift (terminal ~.9 m/s against drag 2.2) raises the centres as they grow, so the tongues
            // stand up at the reach (centre ~.8 m at .45 s, ~1.3 m at the end); soft fade .2
            SetF(so, "Spawn Rate", 110f * a.Dense);
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, -1f, 16f));
            SetF(so, "Initial Velocity Divergence", .18f);
            SetF(so, "Drag", 2.2f);
            SetV3(so, "Global Force_vector", new Vector3(0f, 2f, 0f));
            SetV3(so, "Initial Position_position", new Vector3(0f, .9f, 0f));
            SetV2(so, "Angle Min/Max", Vector2.zero);
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .5f * a.Big), new Keyframe(.35f, 1.1f * a.Big), new Keyframe(1f, 2f * a.Big)));
            var soot = new Color(.165f, .15f, .135f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, .45f), new GradientColorKey(soot, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .05f), new GradientAlphaKey(.9f, .55f), new GradientAlphaKey(0f, 1f) });
            SetGrad(so, "Color Over Life", g);
            SetF(so, "LifeTime", life);
            SetF(so, "Soft Particle Fade Distance", .2f);
            SetV2(so, "Camera Fade Min/Max Distance", new Vector2(.45f, 1.4f));
            SetF(so, "Flipbook Framerate", 30f);   // the source flicker's own pace; 60 cels = 2 s, so a tongue never loops in its life
            SetV3(so, "Bounds_center", new Vector3(0f, 1.5f, 4.5f));
            SetV3(so, "Bounds_size", new Vector3(12f, 4f, 12f));
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
