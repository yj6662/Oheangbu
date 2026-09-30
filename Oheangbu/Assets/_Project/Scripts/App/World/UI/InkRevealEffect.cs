using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>Feeds UI/InkReveal through the mesh, so every underlay / veil / toast shares ONE material and still batches
    /// (IMPLEMENTATION §5.2). Writes per vertex uv1 = (reveal, rectU, rectV, (mode + 1) * 1024 + aspect).
    /// reveal 0 = hidden, 1 = fully drawn. Needs TexCoord1 on the canvas (added automatically, see PlaytestUiView.EnsureCanvasChannels);
    /// without it the shader falls back to the material _Reveal (1 = visible), so a missing channel never hides UI.</summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class InkRevealEffect : BaseMeshEffect
    {
        [SerializeField, Range(0f, 1f)] float reveal = 1f;
        [SerializeField] InkRevealMode mode = InkRevealMode.Wipe;

        public float Reveal
        {
            get => reveal;
            set { value = Mathf.Clamp01(value); if (value == reveal) return; reveal = value; Dirty(); }
        }

        public InkRevealMode Mode
        {
            get => mode;
            set { if (value == mode) return; mode = value; Dirty(); }
        }

        public Graphic Target => graphic;

        /// <summary>Adds (or returns) the effect on a graphic, assigning the shared InkReveal material when the graphic has none.</summary>
        public static InkRevealEffect On(Graphic target, UiStyle304SO style, InkRevealMode mode, float reveal = 1f)
        {
            if (target == null) return null;
            var fx = target.GetComponent<InkRevealEffect>();
            if (fx == null) fx = target.gameObject.AddComponent<InkRevealEffect>();
            if (style != null && style.Materials.InkReveal != null && (target.material == null || target.material == target.defaultMaterial))
                target.material = style.Materials.InkReveal;
            fx.mode = mode; fx.reveal = Mathf.Clamp01(reveal); fx.Dirty();
            return fx;
        }

        protected override void OnEnable() { base.OnEnable(); if (graphic != null) PlaytestUiView.EnsureCanvasChannels(graphic.canvas); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); if (graphic != null) PlaytestUiView.EnsureCanvasChannels(graphic.canvas); }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh == null || graphic == null) return;
            Rect rect = graphic.rectTransform.rect;
            float w = Mathf.Max(rect.width, 1e-3f), h = Mathf.Max(rect.height, 1e-3f);
            float aspect = Mathf.Clamp(w / h, 1e-3f, 1000f);
            float code = ((int)mode + 1) * 1024f + aspect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.uv1 = new Vector4(reveal, (v.position.x - rect.xMin) / w, (v.position.y - rect.yMin) / h, code);
                vh.SetUIVertex(v, i);
            }
        }

        void Dirty() { if (graphic != null) graphic.SetVerticesDirty(); }
    }
}
