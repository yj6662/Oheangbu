using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-85 (SPEC-SPELL-VFX-OVERHAUL-308 11.23): what tells the letters of one initial apart is their FINAL consonant, drawn in
    // the element's own materials - ㄴ a second, wider bleed after the hit, ㅅ a thin sharp trail and an X of two dry strokes,
    // ㅇ ink rings left along the flight and one spreading at the hit, ㄱ a long-lived pool and short strokes stuck in the ground.
    // A single's bolt is a copy of its element bolt with the marker added (PF_V308b_<key>_<final>); a range letter gets the
    // marker in its per-hit prefab (PF_V308b_hit_<key>_<final>) - in the bodies it would be multiplied by 17 spikes / 24 shots.
    // Every single also leaves an ink stroke along its flight (the trail), so a bolt flying straight away from a shoulder camera
    // still draws its line. Presentation only; no light (ART-INK).
    public static partial class Vfx308Build
    {
        const int FinalNone = 0, FinalK = 1, FinalN = 4, FinalS = 19, FinalNg = 21;

        static int FinalOf(string glyph)
        {
            int code = glyph[0] - 0xAC00;
            return code < 0 || code > 11171 ? FinalNone : code % 28;
        }

        static string FinalName(int final) => final == FinalK ? "k" : final == FinalN ? "n" : final == FinalS ? "s" : final == FinalNg ? "ng" : "";

        static string FinalBoltPath(string key, int final) => Root2 + "/Prefabs/PF_V308b_" + key + "_" + FinalName(final) + ".prefab";
        static string FinalHitPath(string key, int final) => Root2 + "/Prefabs/PF_V308b_hit_" + key + "_" + FinalName(final) + ".prefab";

        // the stroke left along the flight: one cel of the speed_line sheet stretched over the whole trail (InkBleed reads
        // _MainTex_ST since 11.23), head at the newest point; ink, no glow, faded near the eye by the shader
        static Material TrailMat(string key, Color ink, Color dark)
        {
            var m = Bleed("M_V308b_" + key + "_trail", "speed_line", C(ink, .95f), 0f, 1f, 0f, .3f, dark);
            m.SetTextureScale("_MainTex", new Vector2(-.23f, .09f));
            m.SetTextureOffset("_MainTex", new Vector2(.24f, .83f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static void InkTrail(Transform travel, string key, Color ink, Color dark, float width = .34f, float time = .6f)
        {
            if (travel == null) return;
            var old = travel.Find("InkTrail"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("InkTrail"); go.transform.SetParent(travel, false); go.layer = AfterFogLayer;
            var t = go.AddComponent<TrailRenderer>();
            t.sharedMaterial = TrailMat(key, ink, dark); t.time = time; t.widthMultiplier = width;
            t.widthCurve = Curve(0f, .7f, .12f, 1f, .6f, .55f, 1f, 0f);
            t.colorGradient = G((0f, C(dark, 1f)), (.5f, C(dark, .85f)), (1f, C(dark, 0f)));
            t.minVertexDistance = .08f; t.numCapVertices = 2; t.numCornerVertices = 2;
            t.alignment = LineAlignment.View; t.textureMode = LineTextureMode.Stretch;
            t.shadowCastingMode = ShadowCastingMode.Off; t.receiveShadows = false; t.emitting = false; t.autodestruct = false;
        }

        // a single's bolt for one final: the element bolt with its marker (cached per element x final for one run)
        static GameObject FinalBolt(Elem e, Spell spell, int final, Dictionary<string, GameObject> cache)
        {
            var source = Load<GameObject>(PrefabPath(spell));
            if (source == null || final == FinalNone) return source;
            string path = FinalBoltPath(e.Key, final);
            if (cache.TryGetValue(path, out var done)) return done;
            var root = Object.Instantiate(source); root.name = System.IO.Path.GetFileNameWithoutExtension(path);
            try
            {
                Transform travel = root.transform.Find("Travel"), contact = root.transform.Find("Contact"), residue = root.transform.Find("Residue");
                if (travel.Find("InkTrail") == null) InkTrail(travel, e.Key, e.Ink, e.Dark);
                Marker(e, final, travel, contact, residue, 1f);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                return cache[path] = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // a range letter's per-hit prefab for one final: the element hit with the marker, a little smaller than a single's
        static GameObject FinalHit(Elem e, int final, Dictionary<string, GameObject> cache)
        {
            string path = final == FinalNone ? HitPath(e.Key) : FinalHitPath(e.Key, final);
            if (cache.TryGetValue(path, out var done)) return done;
            var root = HitBody(e);
            try
            {
                if (final != FinalNone) Marker(e, final, null, root.transform, root.transform, .65f);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                return cache[path] = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // marker materials have names of their own: B() names by sheet, and the same sheet with other values would overwrite them
        static Material Mk(string key, string name, string sheet, Color ink, float opacity = 1f, float soft = -1f) => Bleed("M_V308b_" + key + "_mark_" + name, sheet, ink, 0f, opacity, 0f, soft);

        // travel: the flying part (null for a hit) / contact: plays from the hit / residue: outlives the spell (Play only)
        // 11.24 (sw_mark: the markers drowned in the element's own hit): bigger than the hit, nearly black ink, and later -
        // they read once the element's burst has spread
        // 11.24 (sw_mark2): a marker draws over the element's own hit at the same place (lower fudge = sorted nearer)
        static ParticleSystem Top(ParticleSystem ps) { ps.GetComponent<ParticleSystemRenderer>().sortingFudge = -60f; return ps; }

        static void Marker(Elem e, int final, Transform travel, Transform contact, Transform residue, float k)
        {
            string key = e.Key;
            Color mark = Color.Lerp(e.Dark, e.Ink, .6f);
            switch (final)
            {
                case FinalN:   // the ink spreads twice: a second and a third, wider bleed after the hit
                    Top(Layer(contact, new L { Name = "Bleed2", Mat = Mk(key, "bleed2", "ink_splat", C(e.Ink, .95f), .95f), World = false, Burst = 1, BurstTime = .25f, Max = 2, Life = new Vector2(.6f, .6f),
                        Size = Vector2.one * 6f * k, NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 11f, Colour = Melt(mark, .5f), Grow = Curve(0f, .5f, 1f, 1.2f) }));
                    Top(Layer(contact, new L { Name = "Bleed3", Mat = Mk(key, "bleed2", "ink_splat", C(e.Ink, .95f), .95f), World = false, Burst = 1, BurstTime = .45f, Max = 2, Life = new Vector2(.45f, .45f),
                        Size = Vector2.one * 8.5f * k, NoShape = true, Blend = true, FrameFrom = 2f, FrameTo = 13f, Colour = Melt(mark, .4f), Grow = Curve(0f, .55f, 1f, 1.15f) }));
                    break;
                case FinalS:   // thin sharp trail, an X of two dry strokes at the hit
                    if (travel != null) { var trail = travel.GetComponentInChildren<TrailRenderer>(true); if (trail != null) { trail.widthMultiplier = .15f; trail.time = .45f; } }
                    foreach (float deg in new[] { 38f, -38f })
                        Top(Layer(contact, new L { Name = deg > 0 ? "SlashA" : "SlashB", Mat = Mk(key, "stroke", "speed_line", C(e.Ink, 1f)), World = false, Burst = 1, BurstTime = deg > 0 ? .1f : .18f, Max = 1,
                            Life = new Vector2(.7f, .7f), Size = Vector2.one * 6f * k, NoShape = true, RotDeg = new Vector2(deg, deg), FrameFrom = 0f, FrameTo = .99f, RandomCell = true,
                            Colour = Melt(mark, .6f), Grow = Curve(0f, .45f, .1f, 1f, 1f, 1.05f) }));
                    break;
                case FinalNg:  // rings left along the flight, one spreading at the hit
                    if (travel != null)
                        Top(Layer(travel, new L { Name = "Rings", Mat = Mk(key, "ring", "water_ring", C(e.Ink, 1f)), World = true, Rate = 10f, Max = 12, Life = new Vector2(.5f, .6f), Size = new Vector2(1.2f, 1.6f),
                            NoShape = true, SpinRandom = true, FrameFrom = 2f, FrameTo = 10f, Colour = Fade(mark, .35f), Grow = Curve(0f, .6f, 1f, 1.6f) }));
                    Top(Layer(contact, new L { Name = "RingOut", Mat = Mk(key, "ring", "water_ring", C(e.Ink, 1f)), World = false, Burst = 1, BurstTime = .15f, Max = 2, Life = new Vector2(.7f, .7f), Size = Vector2.one * 7f * k,
                        NoShape = true, FrameFrom = 0f, FrameTo = 15f, Colour = Fade(mark, .35f), Grow = Curve(0f, .4f, 1f, 1.3f) }));
                    break;
                case FinalK:   // short strokes stuck in the ground at the hit, a big pool that stays
                    Top(Layer(contact, new L { Name = "Stakes", Mat = Mk(key, "stroke", "speed_line", C(e.Ink, 1f)), World = false, Mode = 2, Burst = 3, BurstTime = .1f, Max = 3, Life = new Vector2(.8f, .8f),
                        Size = new Vector2(2f, 2.6f) * k, Shape = ParticleSystemShapeType.Circle, Radius = 1.1f * k, ShapeRot = new Vector3(90f, 0f, 0f), RotDeg = new Vector2(84f, 96f),
                        FrameFrom = 0f, FrameTo = .99f, RandomCell = true, Colour = Melt(mark, .65f), Grow = Curve(0f, .3f, .1f, 1f, 1f, 1f) }));
                    if (residue != null)
                        Top(Layer(residue, new L { Name = "Linger", Mat = Mk(key, "linger", "ink_pool", C(e.Ink, 1f), 1f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = Vector2.one * (travel != null ? 5f : 2.5f),
                            Size = Vector2.one * 5f * k, NoShape = true, Blend = true, FrameFrom = 0f, FrameTo = 15.5f,
                            Colour = G((0f, C(mark, 1f)), (.7f, C(mark, .9f)), (1f, C(mark, 0f))), Grow = Curve(0f, .4f, .1f, 1f, 1f, 1.1f) }));
                    break;
            }
        }
    }
}
