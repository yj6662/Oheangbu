using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 map 6 (D308-30, SPEC-MAP-OVERHAUL-308 4c): the realm sheets of the unfolded map - "실제 지형과 각 강토를 나눈 선이
    /// 보이는 형태, 색으로 강토 구분". Two small textures and the look numbers, all written by the offline bake
    /// Tools/Unity/Stage308_map6/_Tools/map6_realm.py from Data/map6_realm.json (nothing is typed here or in the shader):
    ///   Border  256 x 384 RGBA32 - per COLOUR CLASS of the realm graph, the signed distance to that class's borders
    ///           (code 128 = on a border, (code - 128) / 127 x RangeMetres metres, + inside); the paper shader draws the line at a
    ///           fixed screen width from the least |distance| of the four channels.
    ///   Ground  512 x 768 RGBA32 - RGB the realm's wash colour (the approved five, SPEC-ART-INK-LOOK 4.1; two neighbours run
    ///           into each other over +-BlendMetres), A the relief ink of the height field (0 = flat or lit, 1 = a steep shaded slope).
    /// Both: no mips, linear import, bilinear, clamp. Not part of the map bundle (MapNotation308SO): the bundle's bake is not run.
    /// Found through MapStyle304SO.Realm308. The unfolded sheet only: the HUD minimap never reads it (its shader variant does not
    /// compile the block). On = 0, no asset, or one baked for another map = the sheet of map 5, pixel for pixel.
    /// No light anywhere: every term only takes paper away or lays a wash (ART-INK "빛은 전구가 아니라 먹이다").</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI308 Map Realm Sheets", fileName = "MapRealm308")]
    public sealed class MapRealm308SO : ScriptableObject
    {
        [Serializable]
        public sealed class Realm
        {
            public string Id, Name, Element;
            public Color Wash = Color.gray;
            [Tooltip("corners of the realm's polygon in the location catalogue at bake time")] public int Corners;
            [Tooltip("0..3 = R, G, B, A of the border sheet: the colour class of this realm")] public int BorderChannel;
        }

        [Serializable] public sealed class Input { public string Role, Path, Sha256; }

        public int Version = 1;
        public string Decision = "";
        [Tooltip("the map these sheets were baked for (its bounds, its location catalogue)")] public WorldMapBakedDataSO BakedFor;

        [Header("border sheet")]
        public Texture2D Border;
        public string BorderSha256 = "";
        public int BorderWidth, BorderHeight;
        [Tooltip("metres at code 0 / 255 of the border sheet")] public float RangeMetres = 64f;

        [Header("ground sheet")]
        public Texture2D Ground;
        public string GroundSha256 = "";
        public int GroundWidth, GroundHeight;
        [Tooltip("two realm washes run into each other over +- this many metres (baked)")] public float BlendMetres = 36f;

        [Header("look (sRGB composite of the paper shader; the unfolded sheet only)")]
        [Tooltip("THE switch: 0 = the sheet of map 5 (unwalked land = the flat wash, no realm colour, no border)"), Range(0f, 1f)] public float On = 1f;
        [Tooltip("unwalked land: share of the old unwalked wash kept over the paper (1 = the flat grey of map 5)"), Range(0f, 1f)] public float Veil = .3f;
        [Tooltip("unwalked land: share of the walked land's terrain ink (shore, tree dabs, ripples, rock) and slope wash"), Range(0f, 1f)] public float FaintTerrain = .55f;
        [Tooltip("unwalked land: paper the relief takes away at full ink"), Range(0f, 1f)] public float ReliefInk = .42f;
        [Tooltip("realm wash on unwalked land (lerp toward the realm colour)"), Range(0f, 1f)] public float WashUnwalked = .2f;
        [Tooltip("realm wash on walked land"), Range(0f, 1f)] public float WashWalked = .1f;
        [Tooltip("realm border: ink alpha over unwalked land"), Range(0f, 1f)] public float LineInkUnwalked = .6f;
        [Tooltip("realm border: ink alpha over walked land"), Range(0f, 1f)] public float LineInkWalked = .4f;
        [Tooltip("realm border: full width in page px (the same on screen at every zoom)")] public float LinePx = 1.8f;
        [Tooltip("realm border: how much of the dry-brush gaps shows (0 = a whole line)"), Range(0f, 1f)] public float LineBreak = .6f;
        [Tooltip("realm border: lattice of the value noise that makes the gaps, metres (world anchored)")] public float LineBreakMetres = 70f;
        [Tooltip("soft ink wash round the border: width in page px each side (never wider than .95 x RangeMetres on screen)")] public float BleedPx = 7f;
        [Tooltip("soft ink wash round the border: ink alpha at the line"), Range(0f, 1f)] public float BleedInk = .16f;

        [Header("record (bake)")]
        public Realm[] Realms = Array.Empty<Realm>();
        public Input[] Inputs = Array.Empty<Input>();

        public bool IsUsableFor(WorldMapBakedDataSO data) => data != null && BakedFor == data && Border != null && Ground != null && RangeMetres > 0f;
    }
}
