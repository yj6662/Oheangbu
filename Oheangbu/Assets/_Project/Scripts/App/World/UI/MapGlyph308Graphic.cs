using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 map icon (SPEC-MAP-OVERHAUL-308 §3): one cell of the icon atlas (8 x 8 cells; R = ink drawing, G = hanji rim,
    /// B = second ink) as one quad on UI/MapIcon308. The ink takes the vertex colour (ink, cinnabar for the player mark, nacre
    /// on a lacquer plate), the rim the material's rim colour at RimAlpha (uv1.x). The second ink (only the brush mark of the
    /// current position has one: ferrule, handle, edge) is drawn over the first at SecondInkAlpha (uv1.y) in the material's
    /// ink token, or in the rim colour when the glyph sits on a dark plate (SecondInkLight, uv1.z). One quad instead of the
    /// pre-#308 rim Image + core Image, and every icon of a map shares one material and one texture (one batch). Also draws a
    /// channel of the pattern texture (legend samples of forest dots and water ripples): cell &lt; 0 = the whole texture, tiled
    /// by Tile (the second ink is off there: the pattern's B channel is another picture).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapGlyph308Graphic : MaskableGraphic
    {
        Texture atlas;
        int cell = -1;
        float rimAlpha = 1f, secondAlpha = 1f, secondLight;
        Vector2 tile = Vector2.one;

        public int Cell => cell;
        public override Texture mainTexture => atlas != null ? atlas : s_WhiteTexture;

        public static MapGlyph308Graphic Create(string name, Transform parent, Texture texture, Material iconMaterial, int atlasCell, Color ink, float rim, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(size, size);
            var g = go.AddComponent<MapGlyph308Graphic>();
            g.material = iconMaterial; g.raycastTarget = false;
            g.Set(texture, atlasCell, ink, rim);
            return g;
        }

        /// <summary>Texture, cell (row x 8 + column from the top left; &lt; 0 = whole texture), ink colour and rim alpha. The mesh
        /// is rebuilt only when one of them changes.</summary>
        public void Set(Texture texture, int atlasCell, Color ink, float rim)
        {
            bool vertices = atlasCell != cell || !Mathf.Approximately(rim, rimAlpha);
            if (texture != atlas) { atlas = texture; SetMaterialDirty(); vertices = true; }
            cell = atlasCell; rimAlpha = rim;
            if (color != ink) color = ink;          // Graphic.color dirties the vertices itself
            else if (vertices) SetVerticesDirty();
            Channels();
        }

        public void SetTile(Vector2 repeats) { if (tile != repeats) { tile = repeats; SetVerticesDirty(); } }

        /// <summary>The second ink of the cell (B channel): its alpha (0 = off) and its tone, false = the ink token (on paper),
        /// true = the rim colour (the glyph sits on a dark plate with no rim under it). Default: on, ink token.</summary>
        public void SetSecondInk(float alpha, bool light)
        {
            float tone = light ? 1f : 0f; alpha = Mathf.Clamp01(alpha);
            if (Mathf.Approximately(alpha, secondAlpha) && Mathf.Approximately(tone, secondLight)) return;
            secondAlpha = alpha; secondLight = tone; SetVerticesDirty();
        }

        protected override void OnEnable() { base.OnEnable(); Channels(); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); Channels(); }

        void Channels()
        {
            const AdditionalCanvasShaderChannels need = AdditionalCanvasShaderChannels.TexCoord1;
            Canvas owner = canvas;
            if (owner == null) return;
            if ((owner.additionalShaderChannels & need) != need) owner.additionalShaderChannels |= need;
            Canvas root = owner.rootCanvas;
            if (root != null && root != owner && (root.additionalShaderChannels & need) != need) root.additionalShaderChannels |= need;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            float u0 = 0f, v0 = 0f, u1 = tile.x, v1 = tile.y;
            if (cell >= 0)
            {
                int column = cell & 7, row = (cell >> 3) & 7;
                u0 = column / 8f; u1 = (column + 1) / 8f;
                v1 = 1f - row / 8f; v0 = 1f - (row + 1) / 8f;   // row 0 = the top row of the PNG
            }
            var v = UIVertex.simpleVert;
            v.color = color; v.uv1 = new Vector4(rimAlpha, cell >= 0 ? secondAlpha : 0f, secondLight, 0f);   // a whole texture (pattern) has no second ink
            v.position = new Vector3(r.xMin, r.yMin); v.uv0 = new Vector4(u0, v0, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax); v.uv0 = new Vector4(u0, v1, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); v.uv0 = new Vector4(u1, v1, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); v.uv0 = new Vector4(u1, v0, 0f, 0f); vh.AddVert(v);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
        }
    }
}
