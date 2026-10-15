using System.IO;
using Oheangbu.Data.Spell;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.25 (풀림): the drop system InkUnravel308 emits into (world space, nothing of its own -
    // the runtime places every drop and steers it into the brush tip) and the Resources profile that switches the unravel on.
    // Drops: single cels of the ink_drops sheet, stretched along their flight, a breath of the element at birth (the start
    // colour the runtime gives) and ink after two cels. No light (ART-INK).
    public static partial class Vfx308Build
    {
        const string UnravelPrefabPath = Root2 + "/Prefabs/PF_V308c_unravel.prefab";
        const string UnravelProfilePath = "Assets/_Project/Resources/Unravel308/InkUnravel308Profile.asset";

        static string BuildUnravel()
        {
            Touched.Clear();
            Directory.CreateDirectory(Root2 + "/Prefabs"); Directory.CreateDirectory(Path.GetDirectoryName(UnravelProfilePath));
            var root = new GameObject("PF_V308c_unravel");
            try
            {
                var ink = new Color(.12f, .11f, .1f, 1f);
                var ps = Layer(root.transform, new L { Name = "Drops", Mat = Bleed("M_V308c_unravel", "ink_drops", C(SwingInkColour, .95f), 0f, .95f), World = true, Rate = 0f, Max = 400,
                    Life = new Vector2(.2f, .2f), Size = new Vector2(.08f, .08f), NoShape = true, Mode = 3, RandomCell = true, Blend = true,
                    Colour = G((0f, C(Color.white, .9f)), (.18f, C(Color.white, 1f)), (.32f, C(ink, 1f)), (.85f, C(ink, .9f)), (1f, C(ink, 0f))), Grow = Curve(0f, .7f, .3f, 1f, 1f, .45f) });
                var main = ps.main; main.loop = true; main.playOnAwake = true;
                var r = ps.GetComponent<ParticleSystemRenderer>(); r.velocityScale = .045f; r.lengthScale = 1.2f;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, UnravelPrefabPath);
                var profile = AssetDatabase.LoadAssetAtPath<InkUnravel308Profile>(UnravelProfilePath);
                if (profile == null) { profile = ScriptableObject.CreateInstance<InkUnravel308Profile>(); AssetDatabase.CreateAsset(profile, UnravelProfilePath); }
                profile.ParticlePrefab = prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
                return "unravel: " + UnravelPrefabPath + " + " + UnravelProfilePath;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
