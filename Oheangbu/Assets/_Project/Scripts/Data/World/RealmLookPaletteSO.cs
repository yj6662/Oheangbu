using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 (SPEC-WORLD-REALM-LOOK-308, TEST): one per-realm colour source for the ground, the steep rock faces, the grass and the foliage.
    // Numbers come from Tools/Unity/Stage308_realmlook/realmlook308.json (editor tool RealmLook308 writes this asset; nothing is typed here
    // by hand and no shader or C# file holds a colour). RealmLookDriver308 pushes it to shader globals; Strength 0 = today's picture.
    // Map = world-space realm weights (r Cheongrim, g Jeokro, b Cheolong, a Hyeongang, remainder Hwanggyeong) with soft borders.
    public sealed class RealmLookPaletteSO : ScriptableObject
    {
        [Serializable]
        public struct Realm
        {
            public RealmId Id;
            [Tooltip("sRGB hue of the wash; its own lightness is ignored, Value scales the surface's existing lightness")]
            public Color Ground, Rock, Grass, Foliage;
            public float GroundValue, RockValue, GrassValue, FoliageValue;
            [Tooltip("Per-realm share of the role amounts (1 = full)")]
            public float Amount;
        }

        public Texture2D Map;
        [Tooltip("World rectangle of the map: origin x, origin z, size x, size z (metres)")]
        public Vector4 Rect = new Vector4(0, 0, 4000, 6000);
        [Range(0, 1)] public float Strength;
        [Tooltip("How far each role moves to the realm wash: ground, rock, grass, foliage")]
        public Vector4 Amounts = new Vector4(.8f, .9f, .8f, .7f);
        [Tooltip("Steep faces, as 1 - normal.y: rock starts, rock full, talus band starts, ragged edge amplitude")]
        public Vector4 Slope = new Vector4(.2f, .42f, .08f, .12f);
        [Tooltip("Rock face: bed thickness (m), crease ink, vertical streak, bed-to-bed value variation")]
        public Vector4 Strata = new Vector4(3.2f, .5f, .3f, .5f);
        [Tooltip("Rock face: base value of a face (0 keeps the surface's own lightness), detail fade start m, fade end m, talus speckle")]
        public Vector4 Face = new Vector4(.3f, 250f, 800f, .25f);
        [Tooltip("Lightness added before the wash (linear), so near-black albedo still carries the hue: ground, rock, grass, foliage")]
        public Vector4 Lift = Vector4.zero;
        [Tooltip("Rock face blocks: joint spacing (m), joint ink, major bed = this many beds, broad wash blotch")]
        public Vector4 Joints = new Vector4(5f, .5f, 3f, .15f);
        public Realm[] Realms = Array.Empty<Realm>();
    }
}
