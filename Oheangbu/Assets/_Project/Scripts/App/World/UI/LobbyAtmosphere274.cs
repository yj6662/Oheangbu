using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    // Broad horizontal wash with no card boundary: full colour at the left edge, gone at FadeEnd of the width.
    // #304 title (title.png): the child "MenuMist" of the title background is a low veil wash (Veil α.3 -> 0 over the
    // left ~900 px) behind the ink spine; the lobby274 check only asserts that it exists with a CanvasRenderer.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LobbyAtmosphere274 : MaskableGraphic
    {
        [Tooltip("fraction of the rect width where the wash reaches 0 (274: .48)")]
        [SerializeField, Range(.05f, 1f)] float fadeEnd = .48f;
        [Tooltip("linear fall-off (CSS linear-gradient) instead of the 274 smoothstep")]
        [SerializeField] bool linear;

        public float FadeEnd { get => fadeEnd; set { value = Mathf.Clamp(value, .05f, 1f); if (Mathf.Approximately(value, fadeEnd)) return; fadeEnd = value; SetVerticesDirty(); } }
        public bool Linear { get => linear; set { if (value == linear) return; linear = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            for(int i=0;i<=24;i++)
            {
                float x=i/24f;float k=Mathf.Clamp01(x/fadeEnd);float fade=linear?1-k:1-Mathf.SmoothStep(0,1,k);
                var c=color;c.a*=fade;
                vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x),r.yMin),c,Vector2.zero);
                vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x),r.yMax),c,Vector2.one);
                if(i==0)continue;int n=i*2;vh.AddTriangle(n-2,n-1,n);vh.AddTriangle(n,n-1,n+1);
            }
        }
    }
}
