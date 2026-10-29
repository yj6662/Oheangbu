using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 D308-125 5 (c): soot in the empty head of the HP vessel while the player is scorched. One quad, the HP
    /// vessel's own (same rect, same margin as InkVesselGraphic308), drawn just before it: the liquid covers the soot below its
    /// surface and the glass wash lies over the rest. Its material is its own copy of the profile's Smoke308 (UI/VesselSmoke308)
    /// with the vessel's cell, quad, level and wall numbers; the share of soot (_Smoke) and its top (_Top) are material values
    /// only, so the canvas never rebuilds for them. ART-INK: dark value, no light. D308-11b: no clock of its own - it moves only
    /// with the scorch (HudVessels308.SetScorch01).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VesselSmoke308 : MaskableGraphic
    {
        static readonly int SmokeId = Shader.PropertyToID("_Smoke"), TopId = Shader.PropertyToID("_Top");
        static readonly string[] Copied = { "_CellVessel", "_QuadRef", "_Level", "_Glass2" };

        Texture atlas;
        float outset;
        Material own;
        float smoke = -1f, top = -1f;

        public float Smoke => Mathf.Max(0f, smoke);
        public float Top => top;
        public Material Own => own;
        public override Texture mainTexture => atlas != null ? atlas : s_WhiteTexture;

        /// <summary>False = no soot material: the graphic draws nothing.</summary>
        public bool Configure(Material smokeMaterial, Material vessel, Texture atlasTexture, float outsetShare)
        {
            atlas = atlasTexture; outset = Mathf.Max(0f, outsetShare);
            if (smokeMaterial == null) { enabled = false; return false; }
            own = new Material(smokeMaterial) { name = smokeMaterial.name + " (HUD)", hideFlags = HideFlags.DontSave };
            if (vessel != null)
            {
                foreach (var n in Copied) if (vessel.HasProperty(n) && own.HasProperty(n)) own.SetVector(n, vessel.GetVector(n));
                if (vessel.HasProperty("_LinearInkGamma") && own.HasProperty("_LinearInkGamma")) own.SetFloat("_LinearInkGamma", vessel.GetFloat("_LinearInkGamma"));
            }
            material = own;
            Set(0f, 1f);
            SetVerticesDirty();
            return true;
        }

        /// <summary>share of soot 0..1, top 0..1 (1 = the neck top, 0 = the floor). Writes only when a value changes.</summary>
        public void Set(float share, float topShare)
        {
            if (own == null) return;
            share = Mathf.Clamp01(share); topShare = Mathf.Clamp01(topShare);
            if (share == smoke && topShare == top) return;
            smoke = share; top = topShare;
            own.SetFloat(SmokeId, share); own.SetFloat(TopId, topShare);
            // a stencil mask above draws a copy of the material: it gets the same values
            var drawn = canvasRenderer.materialCount > 0 ? canvasRenderer.GetMaterial() : null;
            if (drawn != null && drawn != own) { drawn.SetFloat(SmokeId, share); drawn.SetFloat(TopId, topShare); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f || own == null) return;
            float by = -outset * r.height;
            var q = new Rect(r.xMin + by, r.yMin + by, r.width - 2f * by, r.height - 2f * by);
            var v = UIVertex.simpleVert;
            v.color = color;
            v.position = new Vector3(q.xMin, q.yMin); v.uv0 = new Vector4(0f, 0f, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(q.xMin, q.yMax); v.uv0 = new Vector4(0f, 1f, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(q.xMax, q.yMax); v.uv0 = new Vector4(1f, 1f, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(q.xMax, q.yMin); v.uv0 = new Vector4(1f, 0f, 0f, 0f); vh.AddVert(v);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (own != null) { if (Application.isPlaying) Destroy(own); else DestroyImmediate(own); }
            own = null;
        }
    }
}
