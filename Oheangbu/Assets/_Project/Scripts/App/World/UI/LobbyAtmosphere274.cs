using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    // Broad mist wash: no card boundary underneath the free-standing menu text.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LobbyAtmosphere274 : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            for(int i=0;i<=24;i++)
            {
                float x=i/24f;float fade=1-Mathf.SmoothStep(0,1,x/.48f);
                var c=color;c.a*=fade;
                vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x),r.yMin),c,Vector2.zero);
                vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x),r.yMax),c,Vector2.one);
                if(i==0)continue;int k=i*2;vh.AddTriangle(k-2,k-1,k);vh.AddTriangle(k,k-1,k+1);
            }
        }
    }
}
