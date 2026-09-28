using UnityEngine;
using UnityEngine.UI;
namespace Oheangbu.App.World.UI
{
    public sealed class WorldMapKnownAreaGraphic:MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var rect=rectTransform.rect;Vector2 half=rect.size*.5f;const int n=48;
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;
                Vector2 u=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),v=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                Vector2 inner=new Vector2(Mathf.Max(0,half.x-1.4f),Mathf.Max(0,half.y-1.4f));
                int k=vh.currentVertCount;
                vh.AddVert(rect.center+Vector2.Scale(u,half),color,Vector2.zero);vh.AddVert(rect.center+Vector2.Scale(v,half),color,Vector2.zero);
                vh.AddVert(rect.center+Vector2.Scale(v,inner),color,Vector2.zero);vh.AddVert(rect.center+Vector2.Scale(u,inner),color,Vector2.zero);
                vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
            }
        }
    }
}
