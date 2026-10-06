using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 one HUD element of the liquid cluster (SPEC-HUD-LIQUID-308 §2.6): a single quad drawn by UI/InkVessel308 -
    /// a vessel with its liquid, or an action mark. The state travels per vertex (no material instances, so two vessels and
    /// three marks batch on one material + one atlas): uv0 = quad 0..1, uv1 / uv2 / uv3 = the payload documented in the shader.
    /// The colour is Graphic.color (cinnabar / ink): harness colour checks read it like any other graphic.
    /// D308-11b: a mark also draws the key that triggers it - a SECOND quad of this same graphic (shader code 6: a lacquer
    /// keycap with one key symbol of the atlas), so the key glyph is part of its mark: no GameObject, no text object, no
    /// extra draw call. It is there only while SetKey gave it a cell.
    /// The mesh is rebuilt only when the payload really changed (SetPayload / SetKey compare), so a resting liquid costs nothing.
    /// Fallback (material is not UI/InkVessel308, e.g. before hud308-setup): a plain quad cut at the level (vessel) or a plain
    /// square (mark, dimmed while dry; no key glyph), so the values still read.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InkVesselGraphic308 : MaskableGraphic
    {
        /// <summary>Shader element code of the key glyph quad (UI/InkVessel308 TEXCOORD3.x).</summary>
        public const float KeyCode = 6f;
        static readonly int QuadRefId = Shader.PropertyToID("_QuadRef");

        [SerializeField] Texture atlas;
        [Tooltip("the quad is larger than the rect by this share of its height on every side (paper rim room; = the atlas cell's margin)")]
        [SerializeField] float outset;
        [Tooltip("fallback only: rect-height share of the floor and of the full line")]
        [SerializeField] Vector2 fallbackSpan = new Vector2(.04f, .85f);
        [SerializeField] Vector4 liquid, marks, extra;
        [Tooltip("key glyph quad: centre from the rect centre (x right, y DOWN) and its size, all as shares of the rect height")]
        [SerializeField] Vector3 keyPlace;
        [Tooltip("key glyph: cell of the atlas key block (< 0 = no key quad), wetness 0..1 of its mark")]
        [SerializeField] float keyCell = -1f, keyWet = 1f;

        /// <summary>Mesh rebuilds since creation (AC-H4.4: stays put while the liquid rests).</summary>
        public int RebuildCount { get; private set; }
        public Vector4 Liquid => liquid;
        public Vector4 Marks => marks;
        public Vector4 Extra => extra;
        /// <summary>Cell of the key glyph this mark shows (-1 = none).</summary>
        public int KeyCell => keyCell < 0f ? -1 : Mathf.RoundToInt(keyCell);
        public float KeyWet => keyWet;

        /// <summary>True when the current material is UI/InkVessel308.</summary>
        public bool UsesVesselShader { get { var m = material; return m != null && m.HasProperty(QuadRefId); } }

        public override Texture mainTexture => atlas != null && UsesVesselShader ? atlas : s_WhiteTexture;

        public void Configure(Texture atlasTexture, float outsetShare, Vector2 fallbackFloorFull)
        {
            atlas = atlasTexture; outset = Mathf.Max(0f, outsetShare); fallbackSpan = fallbackFloorFull;
            SetMaterialDirty(); SetVerticesDirty();
        }

        /// <summary>Where this mark's key glyph sits: centre offset from the rect centre (design px, y down) and the keycap size
        /// (design px), for a rect that is `rectPx` design px high.</summary>
        public void ConfigureKey(Vector2 offsetPx, float sizePx, float rectPx)
        {
            float k = rectPx > 0f ? 1f / rectPx : 0f;
            keyPlace = new Vector3(offsetPx.x * k, offsetPx.y * k, Mathf.Max(0f, sizePx) * k);
            SetVerticesDirty();
        }

        /// <summary>Hands over the per-vertex state; dirties the mesh only when something changed.</summary>
        public void SetPayload(in Vector4 liquidState, in Vector4 markState, in Vector4 extraState)
        {
            if (liquidState == liquid && markState == marks && extraState == extra) return;
            liquid = liquidState; marks = markState; extra = extraState;
            SetVerticesDirty();
        }

        /// <summary>The key glyph of this mark: cell of the atlas key block (-1 = none) and how wet its mark is (a dry mark
        /// dims its key). Dirties the mesh only when something changed.</summary>
        public void SetKey(int cell, float wet)
        {
            float c = cell < 0 ? -1f : cell;
            if (c == keyCell && wet == keyWet) return;
            keyCell = c; keyWet = wet;
            SetVerticesDirty();
        }

        /// <summary>TexCoord1..3 on the canvas and its root: the shader reads the payload from them.</summary>
        public static void EnsureChannels(Canvas canvas)
        {
            if (canvas == null) return;
            const AdditionalCanvasShaderChannels need = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3;
            if ((canvas.additionalShaderChannels & need) != need) canvas.additionalShaderChannels |= need;
            var root = canvas.rootCanvas;
            if (root != null && root != canvas && (root.additionalShaderChannels & need) != need) root.additionalShaderChannels |= need;
        }

        protected override void OnEnable() { base.OnEnable(); EnsureChannels(canvas); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); EnsureChannels(canvas); }

        // the mesh depends on the material (shader quad vs fallback quad)
        public override void SetMaterialDirty() { base.SetMaterialDirty(); SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            RebuildCount++;
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            Color32 c = color;
            bool mark = extra.x > 1.5f;
            var v = UIVertex.simpleVert;
            if (!UsesVesselShader)
            {
                if (mark) { c.a = (byte)(c.a * Mathf.Lerp(.3f, 1f, Mathf.Clamp01(liquid.x))); r = Inset(r, r.width * .3f); }
                else
                {
                    float level = Mathf.Clamp01(liquid.x);
                    if (level <= 0f) return;
                    float bottom = r.yMin + r.height * fallbackSpan.x, top = r.yMin + r.height * Mathf.Lerp(fallbackSpan.x, fallbackSpan.y, level);
                    r = Inset(r, r.width * .12f); r.yMin = bottom; r.yMax = top;
                }
                v.color = c;
                Quad(vh, v, r, 0);
                return;
            }
            Rect q = outset > 0f ? Inset(r, -outset * r.height) : r;   // vessels and marks: room for the paper rim outside the rect
            v.color = c; v.uv1 = liquid; v.uv2 = marks; v.uv3 = extra;
            Quad(vh, v, q, 0);
            if (mark && keyCell >= 0f && keyPlace.z > 0f)
            {
                // the key glyph of this mark: its own quad over the lid's lower edge (drawn after it), same material, same atlas
                float size = keyPlace.z * r.height;
                var centre = new Vector2(r.center.x + keyPlace.x * r.height, r.center.y - keyPlace.y * r.height);
                var k = new Rect(centre.x - size * .5f, centre.y - size * .5f, size, size);
                v.uv1 = new Vector4(keyWet, 0f, 0f, 0f); v.uv2 = Vector4.zero; v.uv3 = new Vector4(KeyCode, 0f, keyCell, 0f);
                Quad(vh, v, k, 4);
            }
        }

        static Rect Inset(Rect r, float by) => new Rect(r.xMin + by, r.yMin + by, r.width - 2f * by, r.height - 2f * by);

        static void Quad(VertexHelper vh, UIVertex v, Rect r, int first)
        {
            v.position = new Vector3(r.xMin, r.yMin); v.uv0 = new Vector4(0f, 0f, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax); v.uv0 = new Vector4(0f, 1f, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); v.uv0 = new Vector4(1f, 1f, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); v.uv0 = new Vector4(1f, 0f, 0f, 0f); vh.AddVert(v);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
