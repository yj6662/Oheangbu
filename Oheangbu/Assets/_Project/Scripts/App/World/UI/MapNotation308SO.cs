using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>Brush-strip classes of map308_strokes.bytes (SPEC-MAP-OVERHAUL-308 §7). The number is also the draw order.</summary>
    public enum MapStrokeClass308 { None = 0, Stream = 1, Ridge = 2, InnerCliff = 3, OuterRange = 4, Wall = 5, SiteLine = 6, Trail = 7, Road = 8, Highway = 9, Bridge = 10, Tick = 11 }

    /// <summary>How one stroke class (and, for ridges, one rank) is drawn. Per zoom band Z0..Z3 (x, y, z, w): the ink width in
    /// reference px (1080 page), the atlas row and the metres of stroke per atlas repeat.</summary>
    [Serializable]
    public sealed class MapStrokeStyle308
    {
        [Tooltip("MapStrokeClass308 number (1..11)")] public int Class;
        [Tooltip("ridge rank 1..4 (4 = a rank 3 piece of a ridge under 220 m: drawn like rank 3, hidden from the region band on); 0 = every rank of the class")] public int Rank;
        public string Name = "";
        [Tooltip("row of map308_stroke_atlas.png per band, 0 = top (8 rows x 64 px)")] public Vector4 AtlasRow;
        [Tooltip("metres of stroke per atlas repeat at the reference scale of each band (the notation's tileMetresByBand; a record: the " +
                 "strips take the repeat from the row's UAspect and the live scale, so the brush grain keeps its px size while zooming)")]
        public Vector4 TileMetres = new Vector4(48f, 48f, 48f, 48f);
        [Tooltip("ink width in reference px per band (0 = not drawn in that band); it maps onto the ink span of the row")] public Vector4 WidthPx = new Vector4(2f, 2f, 2f, 2f);
        [Tooltip("paper band under the ink, px on each side (roads). 0 = none")] public float KnockoutPx;
        [Tooltip("ink alpha of the class (sRGB composite)")] public float InkAlpha = .85f;
        [Tooltip("ends marked in the data (flags bit0 / bit1) taper to this share of the width (1 = square ends)")] public float CapScale = .35f;
        [Tooltip("water courses: drawn only while the physical width of the stroke (2 x halfWidthM) is under this many px (wider water is the " +
                 "shore line of the terrain picture). 0 = always")] public float PhysicalWidthBelowPx;

        public static float At(Vector4 v, int band) => band <= 0 ? v.x : band == 1 ? v.y : band == 2 ? v.z : v.w;
        public float Width(int band) => At(WidthPx, band);
    }

    /// <summary>One row of the stroke atlas (map308_notation.json strokeAtlas.rows).</summary>
    [Serializable]
    public sealed class MapAtlasRow308
    {
        public int Row;
        public string Name = "";
        [Tooltip("texel row (from the top of the row, 0..64) that sits on the baked centre line: 32 = symmetric, saw band 48, hachure 46")] public float AxisV = 32f;
        [Tooltip("the row carries a paper band in R (roads)")] public bool Knockout;
        [Tooltip("texture stretch along the stroke: one repeat (1024 texels) = 1024 x (width px / ink span) x UAspect px. 0 = the row is " +
                 "stretched once over the whole stroke (bridge)")] public float UAspect = 1f;
        public MapAtlasRow308() { }
        public MapAtlasRow308(int row, string name, float axisV, bool knockout, float uAspect) { Row = row; Name = name; AxisV = axisV; Knockout = knockout; UAspect = uAspect; }
    }

    [Serializable] public sealed class MapGlyph308 { public string Id = ""; [Tooltip("atlas cell, row x 8 + column, (0,0) = top left")] public int Cell; public string NameKo = ""; }

    /// <summary>A key (marker id, marker kind name, a word of the label, or a well-known key) and the glyph id it draws.</summary>
    [Serializable] public sealed class MapGlyphLink308 { public string Key = ""; public string Glyph = ""; public MapGlyphLink308() { } public MapGlyphLink308(string key, string glyph) { Key = key; Glyph = glyph; } }

    /// <summary>A stroke state (stroke table u16 state = index + 1): the stroke shows only while the state is open.</summary>
    [Serializable] public sealed class MapState308 { public string Id = ""; [Tooltip("shortcut = campaign.DiscoveredShortcuts, fact = campaign.Facts")] public string Kind = "shortcut"; public string Key = ""; }

    public enum MapLegendKind308 { Stroke = 0, Forest = 1, Water = 2, Glyph = 3 }

    [Serializable]
    public sealed class MapLegendCell308
    {
        public MapLegendKind308 Kind;
        public int StrokeClass, Rank;
        [Tooltip("a stroke class drawn under the sample first (the bridge lies on a great road); 0 = none")] public int UnderClass;
        public string Glyph = "";
        public string Name = "";
        public MapLegendCell308() { }
        public MapLegendCell308(MapLegendKind308 kind, int cls, int rank, string name, int under = 0) { Kind = kind; StrokeClass = cls; Rank = rank; Name = name; UnderClass = under; }
    }

    [Serializable] public sealed class MapFileHash308 { public string Path = "", Sha256 = ""; public long Bytes; }

    /// <summary>#308 map 3c: when a baked marker WITHOUT an arrival event (WorldMapMarkerSpec.RequiresArrival false) first shows.
    /// The names are the words of map308_notation.json "markerReveal".</summary>
    public enum MapMarkerReveal308
    {
        /// <summary>Its own discovery cell is walked (the only rule before map 3c; about 90 m away, the paper still unwalked there).</summary>
        FirstContact = 0,
        /// <summary>... and the paper draws the ground round its anchor as walked land (MapGround308).</summary>
        GroundDrawn = 1,
        /// <summary>Never by walking: only the place's arrival event adds it (the location catalogue must name the marker).</summary>
        OnArrival = 2
    }

    /// <summary>#308 map 3c: marker id -> reveal rule. RadiusMetres 0 = the notation's GroundRadiusMetres.</summary>
    [Serializable] public sealed class MapMarkerRevealRow308 { public string MarkerId = ""; public MapMarkerReveal308 Reveal; public float RadiusMetres; }

    /// <summary>#308 map notation (SPEC-MAP-OVERHAUL-308, D308-14 / 14b / 15): the baked bundle both maps read (terrain picture,
    /// patterns, brush strips, stroke atlas, icon atlases, reveal regions, frames) and every number of the notation. One source:
    /// the HUD minimap and the unfolded sheet take the same asset, so they cannot disagree. Created and filled by the editor
    /// command MapOverhaul308 ("import") from map308_notation.json; found at run time through MapStyle304SO.Notation308 (one
    /// asset reference, no static cache). BakedFor names the map data the bundle was baked for: any other map (the protected
    /// W_Demo_Compact) keeps the pre-#308 path. Field initialisers ARE the Spec's TEST values; the importer overwrites the
    /// ones the JSON carries. Not a singleton, no run-time state: nothing here is written while the game runs.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI308 Map Notation", fileName = "MapNotation308")]
    public sealed class MapNotation308SO : ScriptableObject
    {
        public const int ContractVersion = 1;
        // well-known keys of KindGlyphs (besides the WorldMapMarkerKind names)
        public const string KeyPlayer = "Player", KeyCoin = "Coin", KeyPin = "Pin", KeyNorth = "North", KeyCheckpoint = "Checkpoint",
            KeyWakeRing = "WakeRing", KeyObjective = "Objective";

        [Header("bundle (MapOverhaul308 import)")]
        [Tooltip("contract version of map308_notation.json (must equal ContractVersion)")] public int Version;
        [Tooltip("height stage the bundle was baked from: base | 1a | ...")] public string Stage = "";
        [Tooltip("the map data this bundle was baked for; another map keeps the old path")] public WorldMapBakedDataSO BakedFor;
        public Texture2D Terrain, Pattern, StrokeAtlas, IconsL, IconsS;
        public TextAsset Strokes, RevealRegions, NotationJson;
        public Shader StrokeShader, IconShader;
        public List<MapFileHash308> Inputs = new List<MapFileHash308>(), Outputs = new List<MapFileHash308>();

        [Header("frames (D308-15): theme kit cells Frame.Mini / Frame.MapBoard / Piece.North / Plate.Icon / Inlay.LineThin (theme308_atlas)")]
        [Tooltip("frame_mini: tiled 9-slice, 48 + 12 n px on both axes (312 x 228 = the 294 x 210 window + 9 px)")] public Sprite FrameMini;
        [Tooltip("frame_board (tiled 9-slice, body = lacquer under it), piece_north, plate_lacquer (sliced), line_najeon_thin (tiled along x)")]
        public Sprite FrameBoard;
        public Sprite NorthPiece, PlateIcon, InlayLine;
        [Tooltip("optional lattice tile for an extra band on the board (the kit's frame_board already carries its band: leave empty)")] public Sprite LatticeBand;
        [Tooltip("minimap frame width (px) outside the map window")] public float MiniFrameWidth = 9f;
        [Tooltip("north piece: centre distance outside the window edge (px) = frame width - 7 + half the piece height (the kit cell's bottom sits 7 px " +
                 "inside the frame's outer edge). The import sets it from the kit cell (16 x 18 px: 11); 10 is the value for a 16 px piece without the kit")]
        public float MiniNorthOffset = 10f;
        [Tooltip("lacquer board under the sheet: x, y, w, h in page px (1920 x 1080, top-left). 910 high = 82 + 9 x 92 (the kit's rule (80 + 9 n) x (82 + 9 m)): " +
                 "the frame's bottom band (the outer 19 px) lies under the control row (keycaps y 979-1013), not behind it")]
        public Rect BoardRect = new Rect(542f, 126f, 836f, 910f);
        [Tooltip("board edge band (px) that carries the inlay line and the lattice band")] public float BoardBand = 18f;
        [Range(0f, 1f)] public float LatticeAlpha = .14f;
        [Tooltip("frame tint (the temporary sprites are painted in their final colours: white)")] public Color FrameTint = Color.white;
        [Tooltip("nacre tint for the north piece when it is drawn from the icon atlas, the inlay lines and legend glyphs (LDR, max channel <= .82)")]
        public Color Nacre = new Color32(0xBF, 0xBE, 0xB6, 0xFF);
        [Tooltip("black lacquer (legend plates without a sprite)")] public Color Lacquer = new Color32(0x11, 0x0F, 0x0D, 0xFF);

        [Header("zoom bands (metres per reference px): Z0 <= x < Z1 <= y < Z2 <= z < Z3")]
        public Vector3 ZoomBands = new Vector3(.6f, 1.8f, 5f);

        [Header("values (sRGB; paper / ink / cinnabar default to the UiStyle304 tokens)")]
        public Color Paper = new Color32(0xDF, 0xDB, 0xD0, 0xFF);
        public Color Unknown = new Color32(0x8F, 0x8B, 0x81, 0xFF);
        public Color Ink = new Color32(0x14, 0x14, 0x13, 0xFF);
        public Color Cinnabar = new Color32(0xB8, 0x39, 0x2B, 0xFF);
        [Tooltip("ink rim of the walked land: ink alpha over the unwalked wash, and width (px) just inside the crisp edge")] public float EdgeRimAlpha = .45f;
        public float EdgeRimPx = 2f;
        [Tooltip("crisp walked edge (MapFog308_Edge): the contour of the lattice-corner field at x + y x noise. x > 0 keeps the edge inside the walked cells; x + y < 1")]
        public Vector2 EdgeThreshold = new Vector2(.14f, .42f);
        [Tooltip("crisp walked edge: lattice cells of the two noise octaves per 32 m discovery cell")] public Vector2 EdgeNoiseCells = new Vector2(1.6f, 3.7f);
        [Tooltip("grain share: walked paper fibre (minimap), unwalked wash")] public Vector2 Grain = new Vector2(0f, .03f);
        [Tooltip("ink alpha -> linear-space remap exponent (dark ink composited as in sRGB)")] public float InkGamma = 2.2f;

        [Header("terrain (map308_terrain: R slope wash, G forest density, B water distance, A elevation)")]
        [Tooltip("slope wash at R = 1 (45 deg and steeper)")] public float SlopeWash = .18f;
        [Tooltip("elevation wash: replaces the slope wash in the whole-world band (Z3); it rises between these heights (m)")] public float ElevationWash = .14f;
        public Vector2 ElevationRangeMetres = new Vector2(112f, 320f);
        [Tooltip("height at A = 1 (m)")] public float ElevationMaxMetres = 400f;
        [Tooltip("forest: no dots under this density (G); a dot cluster appears where pattern.r passes 1 - density: ink = saturate((pattern.r - (1 - density)) x gain)")]
        public float ForestShowFrom = .05f;
        public float ForestGain = 4f;
        [Tooltip("forest dot ink: bands Z0..Z2, and the whole-world band")] [Range(0f, 1f)] public float ForestInk = .55f;
        [Range(0f, 1f)] public float ForestInkWorld = .40f;
        [Tooltip("rock strokes rise from (RockFrom - RockSoftness) to 1 of the slope wash (R); none in the whole-world band")] public float RockFrom = .96f;
        public float RockSoftness = .08f;
        [Range(0f, 1f)] public float RockInk = .45f;
        [Tooltip("water distance field range (m): B = 128 + clamp(d / range, -1, 1) x 127, water inside is the large value")] public float WaterRangeMetres = 32f;
        [Tooltip("shore line width (px) and ink")] public float ShorePx = 1.25f;
        [Range(0f, 1f)] public float ShoreInk = .78f;
        [Tooltip("wave marks start this far inside the shore and fade in over the feather (m): wide water only, a stream stays plain")] public float RippleFromMetres = 9f;
        public float RippleFeatherMetres = 5f;
        [Tooltip("ripple ink: bands Z0..Z2, and the whole-world band")] [Range(0f, 1f)] public float RippleInk = .5f;
        [Range(0f, 1f)] public float RippleInkWorld = .22f;
        [Tooltip("metres of world per repeat of map308_pattern for the bands Z0..Z3")] public Vector4 PatternMetres = new Vector4(70f, 292f, 496f, 704f);
        [Tooltip("cave plan by lightness: wall from x to y, floor from y to z, wall ink w (both maps)")] public Vector4 CaveBands = new Vector4(.44f, .56f, .64f, .85f);

        [Header("brush strips")]
        public List<MapStrokeStyle308> StrokeStyles = DefaultStrokes();
        [Tooltip("stroke atlas layout: rows, row height px, margin px above and below the content, ink span (top, bottom texel of the row)")]
        public int AtlasRows = 8;
        public float AtlasRowPx = 64f, AtlasRowPad = 6f;
        [Tooltip("the class width (px) x pressure maps onto this span of the row; the centre line sits on the AxisV of the row")]
        public Vector2 AtlasInkSpan = new Vector2(12f, 52f);
        public List<MapAtlasRow308> AtlasRowSpecs = DefaultAtlasRows();
        [Tooltip("paper under every road first, every ink after (no notches at junctions) in the bands Z0..Z2; the whole-world band and a " +
                 "mesh over the vertex ceiling draw band and ink as one layer per stroke")] public bool PaperPassFirst = true;
        [Tooltip("alpha of the paper band under a road (1 = the Spec's knock-out; lower it if the band reads as a tape on the sheet)")]
        [Range(0f, 1f)] public float PaperBandAlpha = 1f;
        [Tooltip("ridge width share for rank 1 / 2 / 3 when the table has one entry for the class (x, y, z)")] public Vector3 RankWidth = new Vector3(1f, .62f, .31f);
        [Tooltip("capped ends taper over this many stroke widths")] public float CapLength = 2.5f;
        [Tooltip("minimap: strips are loaded for the window grown by this margin (m); a new upload happens only when the window leaves the loaded bins")]
        public float MiniBinMargin = 40f;
        [Tooltip("vertex ceiling of one strip graphic (uGUI limit 65,000)")] public int MaxStripVertices = 64000;
        public List<MapState308> States = new List<MapState308>();

        [Header("icons")]
        public List<MapGlyph308> Glyphs = new List<MapGlyph308>();
        [Tooltip("marker id -> glyph id")] public List<MapGlyphLink308> MarkerGlyphs = new List<MapGlyphLink308>();
        [Tooltip("marker kind (WorldMapMarkerKind name) or well-known key (Player, Coin, Pin, North, Checkpoint, WakeRing, Objective) -> glyph id; " +
                 "a key without a row falls back to the glyph of the same name in lower case")]
        public List<MapGlyphLink308> KindGlyphs = new List<MapGlyphLink308>();
        [Tooltip("place-name priority by glyph id, first = kept first when names collide; glyphs not listed come after")]
        public List<string> LabelPriority = new List<string> { "inn", "village", "gaekju", "gate", "peak" };
        [Tooltip("the wake ring (same symbol + outer ring) diameter / symbol size")] public float WakeRingScale = 1.5f;
        [Tooltip("fallback for a marker the table does not list: a word of its label -> glyph id (the pre-#308 guess, last resort)")]
        public List<MapGlyphLink308> WordGlyphs = new List<MapGlyphLink308>();
        [Tooltip("minimap sizes (px): place icon, player mark, pin, north piece, clear margin inside the window edge")]
        public float MiniIconPx = 22f;
        public float MiniPlayerPx = 28f, MiniPinPx = 18f, MiniCoinPx = 20f, MiniNorthPx = 16f, MiniEdgePx = 12f;
        [Tooltip("icons drawn at or under this size use the S atlas (32 px cells), larger ones the L atlas (64 px cells). 28 = every minimap " +
                 "icon (22 / 20 / 18 / 28) stays on the S atlas (one batch) and the sheet's 30-48 px marks take L (S would be magnified at 1440p)")]
        public float SmallAtlasMaxPx = 28f;
        [Range(0f, 1f)] public float IconRimAlpha = .95f;
        [Tooltip("minimap paper alpha inside the frame (walked and unwalked alike) and the ink darkening towards the frame (0 = none)")]
        [Range(0f, 1f)] public float MiniPaperAlpha = 1f;
        [Range(0f, 1f)] public float MiniEdgeInk;

        [Header("labels (unfolded sheet)")]
        [Tooltip("hanji plate under a place name: alpha, padding (x, y px), ink line alpha")] public float PlateAlpha = .92f;
        public Vector2 PlatePad = new Vector2(7f, 3f);
        [Range(0f, 1f)] public float PlateLineAlpha = .6f;
        [Tooltip("gap (px) between an icon and its label")] public float LabelGap = 5f;

        [Header("legend (unfolded sheet)")]
        [Tooltip("marker rows pitch (px); the #304 rows were 90")] public float LegendRowPitch = 72f;
        public string LegendTerrainCaption = "지형과 길";
        public List<MapLegendCell308> Legend = DefaultLegend();

        [Header("reveal (question 5: a cliff-top field is not revealed from its foot)")]
        [Tooltip("true = a 32 m cell with a region number 1..254 is revealed only while the player stands in that region")] public bool RegionGatedReveal = true;

        [Header("marker reveal (#308 map 3c, notation markerReveal): a baked marker WITHOUT an arrival event")]
        [Tooltip("rule of a marker the table does not list (FirstContact = its own cell is walked: the rule before map 3c)")]
        public MapMarkerReveal308 MarkerRevealDefault = MapMarkerReveal308.FirstContact;
        [Tooltip("marker id -> rule (GroundDrawn: the long wall gate and the two inns of map 3). A row for an arrival marker has no effect")]
        public List<MapMarkerRevealRow308> MarkerReveal = new List<MapMarkerRevealRow308>();
        [Tooltip("GroundDrawn: metres round the marker's anchor the paper must draw as walked land. 19 = the minimap glyph at any turn of the " +
                 "follow view plus one px: (MiniIconPx 22 x .7071 + 1) px x 240 m / 210 px. The px beyond the glyph pays the anti-aliasing " +
                 "(half a screen px at 720p)")]
        public float GroundRadiusMetres = 19f;
        [Tooltip("GroundDrawn: spacing (m) of the points read inside that disc")] public float GroundStepMetres = .5f;
        [Tooltip("GroundDrawn: least `land` of MapFog308_Edge (cells) at every point = the deepest the crisp edge can lie inside the walked cells " +
                 "(MapFog308.cginc FROM + SPAN, + SG + SWMAX + BITE when DEEP is 1) + a margin for the gap between the points read (.04), at most .98. " +
                 "Baked from the cginc's #define lines")]
        public float GroundNeedLand = .66f;
        [Tooltip("GroundDrawn: KCOVER of MapFog308.cginc (land = min(depth, KCOVER x cover))")] public float GroundKCover = 1.5f;

        // ------------------------------------------------------------------ lookups (lists are short; nothing is cached)
        /// <summary>The bundle can draw this map: same contract version, baked for it, and every file the shaders read is there.</summary>
        public bool IsUsableFor(WorldMapBakedDataSO data)
            => data != null && Version == ContractVersion && BakedFor == data && Terrain != null && Pattern != null && StrokeAtlas != null && Strokes != null
               && StrokeShader != null && StrokeShader.isSupported;

        public bool HasIcons => IconShader != null && IconShader.isSupported && (IconsS != null || IconsL != null);

        /// <summary>Zoom band 0..3 for a scale in metres per reference px.</summary>
        public int Band(float metresPerPx) => metresPerPx <= ZoomBands.x ? 0 : metresPerPx <= ZoomBands.y ? 1 : metresPerPx <= ZoomBands.z ? 2 : 3;

        public float PatternPeriod(int band) => Mathf.Max(1f, band <= 0 ? PatternMetres.x : band == 1 ? PatternMetres.y : band == 2 ? PatternMetres.z : PatternMetres.w);

        /// <summary>_M308Edge / _S308Edge: (threshold from, threshold span, noise cells 1, noise cells 2) of the crisp walked edge.
        /// `from` is kept above 0 (the edge then never reaches an unwalked cell, #214) and from + span under 1.</summary>
        public Vector4 EdgeShape
        {
            get
            {
                float from = Mathf.Clamp(EdgeThreshold.x, .02f, .9f);
                return new Vector4(from, Mathf.Clamp(EdgeThreshold.y, 0f, .98f - from), Mathf.Max(.1f, EdgeNoiseCells.x), Mathf.Max(.1f, EdgeNoiseCells.y));
            }
        }

        /// <summary>The style of a class / rank: the exact (class, rank) entry, else the class entry with rank 0. scale = the
        /// rank's width share when the class entry is shared by the ranks (1 otherwise).</summary>
        public MapStrokeStyle308 Stroke(int cls, int rank, out float scale)
        {
            scale = 1f;
            MapStrokeStyle308 any = null;
            if (StrokeStyles != null)
                for (int i = 0; i < StrokeStyles.Count; i++)
                {
                    var s = StrokeStyles[i];
                    if (s == null || s.Class != cls) continue;
                    if (s.Rank == rank) return s;
                    if (s.Rank == 0) any = s;
                }
            if (any != null && rank > 0) scale = rank == 1 ? RankWidth.x : rank == 2 ? RankWidth.y : RankWidth.z;
            return any;
        }

        public bool TryCell(string glyphId, out int cell)
        {
            cell = -1;
            if (string.IsNullOrEmpty(glyphId) || Glyphs == null) return false;
            for (int i = 0; i < Glyphs.Count; i++)
                if (Glyphs[i] != null && Glyphs[i].Id == glyphId) { cell = Glyphs[i].Cell; return cell >= 0 && cell < 64; }
            return false;
        }

        static string Linked(List<MapGlyphLink308> list, string key)
        {
            if (list == null || string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Key == key) return list[i].Glyph;
            return null;
        }

        /// <summary>Cell of a well-known key or a marker kind name: the KindGlyphs row, else the glyph named like the key
        /// (Player -> player, WakeRing -> wake_ring, Coin -> the Drop kind).</summary>
        public bool TryKeyCell(string key, out int cell)
        {
            if (TryCell(Linked(KindGlyphs, key), out cell)) return true;
            if (string.IsNullOrEmpty(key)) return false;
            if (key == KeyWakeRing) return TryCell("wake_ring", out cell);
            if (key == KeyCoin && TryCell(Linked(KindGlyphs, WorldMapMarkerKind.Drop.ToString()), out cell)) return true;
            return TryCell(key.ToLowerInvariant(), out cell);
        }

        /// <summary>Place-name priority of an atlas cell: the index of its glyph in LabelPriority, else LabelPriority.Count.</summary>
        public int LabelRank(int cell)
        {
            int last = LabelPriority != null ? LabelPriority.Count : 0;
            if (cell < 0 || Glyphs == null || LabelPriority == null) return last;
            for (int i = 0; i < Glyphs.Count; i++)
                if (Glyphs[i] != null && Glyphs[i].Cell == cell)
                {
                    int at = LabelPriority.IndexOf(Glyphs[i].Id);
                    return at >= 0 ? at : last;
                }
            return last;
        }

        public MapAtlasRow308 AtlasRow(int row)
        {
            if (AtlasRowSpecs != null) for (int i = 0; i < AtlasRowSpecs.Count; i++) if (AtlasRowSpecs[i] != null && AtlasRowSpecs[i].Row == row) return AtlasRowSpecs[i];
            return null;
        }

        /// <summary>Cell of a baked marker: by id, then by kind, then by a word of its label, then the Place kind.</summary>
        public bool TryMarkerCell(string markerId, WorldMapMarkerKind kind, string label, out int cell)
        {
            if (TryCell(Linked(MarkerGlyphs, markerId), out cell)) return true;
            if (kind != WorldMapMarkerKind.Place && TryKeyCell(kind.ToString(), out cell)) return true;
            if (!string.IsNullOrEmpty(label) && WordGlyphs != null)
                for (int i = 0; i < WordGlyphs.Count; i++)
                {
                    var w = WordGlyphs[i];
                    if (w != null && !string.IsNullOrEmpty(w.Key) && label.Contains(w.Key) && TryCell(w.Glyph, out cell)) return true;
                }
            return TryKeyCell(WorldMapMarkerKind.Place.ToString(), out cell);
        }

        /// <summary>#308 map 3c: the reveal rule of a baked marker and, for GroundDrawn, the radius (m) round its anchor.</summary>
        public MapMarkerReveal308 RevealOf(string markerId, out float radiusMetres)
        {
            radiusMetres = GroundRadiusMetres;
            if (MarkerReveal != null && !string.IsNullOrEmpty(markerId))
                for (int i = 0; i < MarkerReveal.Count; i++)
                {
                    var row = MarkerReveal[i];
                    if (row == null || row.MarkerId != markerId) continue;
                    if (row.RadiusMetres > 0f) radiusMetres = row.RadiusMetres;
                    return row.Reveal;
                }
            return MarkerRevealDefault;
        }

        public Texture2D IconAtlas(float displayPx)
        {
            if (displayPx <= SmallAtlasMaxPx) return IconsS != null ? IconsS : IconsL;
            return IconsL != null ? IconsL : IconsS;
        }

        // ------------------------------------------------------------------ defaults = Tools/Art/cartography308.py CLASSES / ATLAS_ROWS (px for Z0, Z1, Z2, Z3)
        static float ReferenceMetresPerPx(int band) => band <= 0 ? .27f : band == 1 ? 1.14f : band == 2 ? 2.66f : 7.9f;
        // texture stretch along U per atlas row; 0 = stretched once over the stroke
        static float RowUAspect(int row) { switch (row) { case 0: case 1: return 1.5f; case 2: case 4: return 2f; case 7: return 0f; default: return 1f; } }

        // the rule of the bake: one repeat (1024 texels) at the reference scale of the band, the ink span (40 texels) across the width
        static float Tile(int row, float width, int band) => width > 0f ? 1024f * (width / 40f) * RowUAspect(row) * ReferenceMetresPerPx(band) : 0f;

        static MapStrokeStyle308 S(MapStrokeClass308 cls, int rank, string name, int r0, int r1, int r2, int r3, float z0, float z1, float z2, float z3,
            float knock, float ink, float cap, float physicalBelow = 0f)
            => new MapStrokeStyle308 { Class = (int)cls, Rank = rank, Name = name, AtlasRow = new Vector4(r0, r1, r2, r3),
                TileMetres = new Vector4(Tile(r0, z0, 0), Tile(r1, z1, 1), Tile(r2, z2, 2), Tile(r3, z3, 3)), WidthPx = new Vector4(z0, z1, z2, z3),
                KnockoutPx = knock, InkAlpha = ink, CapScale = cap, PhysicalWidthBelowPx = physicalBelow };

        public static List<MapStrokeStyle308> DefaultStrokes() => new List<MapStrokeStyle308>
        {
            S(MapStrokeClass308.Stream, 0, "물줄기", 0, 0, 0, 0, 0f, 0f, 1.6f, 1.6f, 0f, .80f, .35f, 3.2f),
            S(MapStrokeClass308.Ridge, 1, "산줄기", 3, 3, 3, 3, 11f, 9f, 7f, 5f, 0f, .88f, .35f),
            S(MapStrokeClass308.Ridge, 2, "산줄기", 3, 3, 3, 3, 7.5f, 6f, 4f, 2.5f, 0f, .78f, .35f),
            S(MapStrokeClass308.Ridge, 3, "산줄기", 3, 3, 3, 3, 3.6f, 3f, 2f, 0f, 0f, .56f, .35f),
            S(MapStrokeClass308.Ridge, 4, "산줄기", 3, 3, 3, 3, 3.6f, 3f, 0f, 0f, 0f, .56f, .35f),
            S(MapStrokeClass308.InnerCliff, 0, "벼랑", 5, 5, 5, 4, 10f, 8.5f, 6f, 1.6f, 0f, .88f, .35f),
            S(MapStrokeClass308.OuterRange, 0, "끝 연봉", 3, 3, 3, 3, 12f, 10f, 8f, 5f, 0f, .92f, .35f),
            S(MapStrokeClass308.Wall, 0, "성벽", 6, 6, 6, 6, 6f, 5f, 4f, 3f, 0f, .90f, .35f),
            S(MapStrokeClass308.SiteLine, 0, "터 선", 4, 4, 4, 4, 2f, 1.5f, 1.5f, 0f, 0f, .75f, .35f),
            S(MapStrokeClass308.Trail, 0, "샛길", 2, 2, 1, 1, 2.8f, 2.2f, 1.8f, 1.3f, 1.5f, .84f, .35f),   // dry row at Z0 / Z1, the medium brush under 2 px
            S(MapStrokeClass308.Road, 0, "길", 1, 1, 1, 1, 4f, 3.2f, 2.6f, 1.8f, 1.5f, .86f, .35f),
            S(MapStrokeClass308.Highway, 0, "큰길", 0, 0, 0, 0, 5.6f, 4.5f, 3.6f, 2.4f, 1.5f, .92f, .35f),
            S(MapStrokeClass308.Bridge, 0, "다리", 7, 7, 7, 7, 12f, 10f, 8f, 0f, 0f, .90f, .35f),
            S(MapStrokeClass308.Tick, 0, "거리 눈금", 4, 4, 4, 4, 0f, 0f, 1.3f, 0f, 0f, .85f, .35f),
        };

        public static List<MapAtlasRow308> DefaultAtlasRows() => new List<MapAtlasRow308>
        {
            new MapAtlasRow308(0, "wet", 32f, true, RowUAspect(0)), new MapAtlasRow308(1, "medium", 32f, true, RowUAspect(1)),
            new MapAtlasRow308(2, "dry", 32f, true, RowUAspect(2)), new MapAtlasRow308(3, "teeth", 48f, false, RowUAspect(3)),
            new MapAtlasRow308(4, "dryline", 32f, false, RowUAspect(4)), new MapAtlasRow308(5, "hachure", 46f, false, RowUAspect(5)),
            new MapAtlasRow308(6, "battlement", 32f, false, RowUAspect(6)), new MapAtlasRow308(7, "bridge", 32f, false, RowUAspect(7)),
        };

        public static List<MapLegendCell308> DefaultLegend() => new List<MapLegendCell308>
        {
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.Ridge, 1, "산줄기"),
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.InnerCliff, 0, "벼랑"),
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.Wall, 0, "성벽"),
            new MapLegendCell308(MapLegendKind308.Water, 0, 0, "물"),
            new MapLegendCell308(MapLegendKind308.Forest, 0, 0, "숲"),
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.Highway, 0, "큰길"),
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.Road, 0, "길"),
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.Trail, 0, "샛길"),
            new MapLegendCell308(MapLegendKind308.Stroke, (int)MapStrokeClass308.Bridge, 0, "다리", (int)MapStrokeClass308.Highway),
        };
    }
}
