using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>One layer of an ink meter (IMPLEMENTATION §5.1). The rect is the FULL (max) stroke; the shader UI/InkMeter
    /// draws body for px &lt; fill - tail, blends body -> split tail over _BlendPx, and draws nothing past the fill, so the
    /// bristles end exactly at the value. Payload per vertex: uv0 = rect 0..1, uv1 = (fill, rectW, rectH, edgeOnly).
    /// No material instances: every layer with the same material batches. Use InkMeter304 for the 4-layer composition.
    /// Fallback (material is not UI/InkMeter, e.g. before ui304-setup): the quad is cut at the fill so the value still reads
    /// as a plain bar, and edge-only layers draw nothing.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InkMeterGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] float fill = 1f;
        [Tooltip("ghost-edge layer: rim texture minus the body mask (only the paper edge)")]
        [SerializeField] bool edgeOnly;

        public float Fill
        {
            get => fill;
            set { value = Mathf.Clamp01(value); if (value == fill) return; fill = value; SetVerticesDirty(); }
        }

        public bool EdgeOnly
        {
            get => edgeOnly;
            set { if (value == edgeOnly) return; edgeOnly = value; SetVerticesDirty(); }
        }

        /// <summary>True when the current material is UI/InkMeter (it cuts at the fill itself).</summary>
        public bool UsesMeterShader { get { var m = material; return m != null && m.HasProperty("_BodyTex") && m.HasProperty("_TailPx"); } }

        protected override void OnEnable() { base.OnEnable(); PlaytestUiView.EnsureCanvasChannels(canvas); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); PlaytestUiView.EnsureCanvasChannels(canvas); }

        // the mesh depends on the material (shader cut vs quad cut)
        public override void SetMaterialDirty() { base.SetMaterialDirty(); SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0 || r.height <= 0) return;
            float u1 = 1f;
            if (!UsesMeterShader)
            {
                if (edgeOnly || fill <= 0f) return;
                u1 = fill; r.xMax = r.xMin + r.width * fill;
            }
            Color32 c = color;
            var payload = new Vector4(fill, r.width / u1, r.height, edgeOnly ? 1f : 0f);
            var v = UIVertex.simpleVert; v.color = c; v.uv1 = payload;
            v.position = new Vector3(r.xMin, r.yMin); v.uv0 = new Vector4(0, 0, 0, 0); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax); v.uv0 = new Vector4(0, 1, 0, 0); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); v.uv0 = new Vector4(u1, 1, 0, 0); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); v.uv0 = new Vector4(u1, 0, 0, 0); vh.AddVert(v);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
    }
}
