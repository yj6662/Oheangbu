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
            var v = GraphHost(root, "SmokeVfx", SampleSmoke);
            if (v == null) return false;
            var so = new SerializedObject(v);
            // pq_vfxg2: the wash-based soot read pink (straw-coloured hay cloud) - now soot-dark from the start, the pigment only a
            // hint in the first tenth, sinking to ink (the user's reference = dark torn soot)
            Color soot = Color.Lerp(e.Dark, e.Ink, .35f), ink = Color.Lerp(e.Dark, e.Ink, .75f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(soot, e.Pig, .2f), 0f), new GradientColorKey(soot, .15f), new GradientColorKey(ink, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.7f, .06f), new GradientAlphaKey(.45f, .5f), new GradientAlphaKey(0f, 1f) });
            // pq_vfxg (first Play): the fan's emit window is ~0.3 s, so 150/s gave ~40 soft round puffs that merged into one
            // brown ball. Now the burst fills the graph's capacity (128) inside the window, each quad small and torn (the soot
            // wisp cels as a colour tone), thrown wider so the fan reads as many wisps.
            var wisp = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/SpellVFX308/V2/Textures/T_V308b_soot_wisp_vfx.png");
            if (wisp != null) { SetObj(so, "Main Texture", wisp); SetV2(so, "Flipbook Size", new Vector2(4f, 4f)); SetB(so, "Use Flipbook", true); }
            else Debug.LogWarning("[Vfx308Build] T_V308b_soot_wisp_vfx missing (python Tools/Art/SpellVFX308/smoke308.py soot_wisp_vfx), the sample smoke texture stays");
            SetF(so, "Spawn Rate", 420f * a.Dense);
            SetF(so, "LifeTime", 1.6f * a.Slow);
            SetV3(so, "Initial Velocity_vector", new Vector3(0f, .5f, 7.5f));
            SetF(so, "Initial Velocity Divergence", 1.4f);   // pq_vfxg2: 2.4 swelled into a ball; the fan stays a forward throw
            SetF(so, "Drag", 1.4f);
            SetV3(so, "Global Force_vector", new Vector3(0f, .9f, 0f));
            SetV3(so, "Initial Position_position", Vector3.zero);
            SetCurve(so, "Size Over Life", new AnimationCurve(new Keyframe(0f, .35f * a.Big), new Keyframe(.4f, .9f * a.Big), new Keyframe(1f, 1.4f * a.Big)));
            SetGrad(so, "Color Over Life", g);
            SetF(so, "Soft Particle Fade Distance", .8f);
            SetV2(so, "Camera Fade Min/Max Distance", new Vector2(.45f, 1.4f));
            SetF(so, "Flipbook Framerate", 16f / (1.6f * a.Slow));   // the 16 cels once over the life (torn -> thinned)
            SetV3(so, "Bounds_center", new Vector3(0f, 1.5f, 4f));
            SetV3(so, "Bounds_size", new Vector3(10f, 4f, 10f));
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
