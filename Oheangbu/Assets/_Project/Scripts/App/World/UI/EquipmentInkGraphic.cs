using UnityEngine;
using UnityEngine.UI;
namespace Oheangbu.App.World.UI {
 // Candidate artwork is optional; legacy scenes retain the vector fallback.
 [RequireComponent(typeof(CanvasRenderer))]
 public sealed class EquipmentInkGraphic:MaskableGraphic {
  public int Symbol=-1;
  public Sprite Artwork;
  public override Texture mainTexture=>Artwork!=null?Artwork.texture:base.mainTexture;
  protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();
   if(Artwork!=null){
    var r=GetPixelAdjustedRect();var uv=UnityEngine.Sprites.DataUtility.GetOuterUV(Artwork);
    float aspect=Artwork.rect.width/Artwork.rect.height;
    var size=r.size;if(size.x/size.y>aspect)size.x=size.y*aspect;else size.y=size.x/aspect;
    var lo=r.center-size*.5f;var hi=r.center+size*.5f;
    vh.AddVert(new Vector3(lo.x,lo.y),color,new Vector2(uv.x,uv.y));
    vh.AddVert(new Vector3(lo.x,hi.y),color,new Vector2(uv.x,uv.w));
    vh.AddVert(new Vector3(hi.x,hi.y),color,new Vector2(uv.z,uv.w));
    vh.AddVert(new Vector3(hi.x,lo.y),color,new Vector2(uv.z,uv.y));
    vh.AddTriangle(0,1,2);vh.AddTriangle(2,3,0);return;
   }
   void Shape(params Vector2[] p){int start=vh.currentVertCount;var rect=rectTransform.rect;foreach(var v in p)vh.AddVert(new Vector3(rect.xMin+v.x*rect.width,rect.yMin+v.y*rect.height),color,Vector2.zero);for(int i=1;i<p.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);}
   void Stroke(float x,float y,float a,float b,float w){var d=new Vector2(a-x,b-y).normalized;var n=new Vector2(-d.y,d.x)*w;Shape(new Vector2(x,y)+n,new Vector2(a,b)+n,new Vector2(a,b)-n,new Vector2(x,y)-n);}
   void Disc(float x,float y,float rx,float ry){var p=new Vector2[24];for(int i=0;i<24;i++){float a=i*Mathf.PI/12;float grain=1+.022f*Mathf.Sin(i*7.1f);p[i]=new Vector2(x+Mathf.Cos(a)*rx*grain,y+Mathf.Sin(a)*ry*grain);}Shape(p);}
   if(Symbol<0){
    Disc(.5f,.87f,.09f,.074f);Shape(new Vector2(.47f,.97f),new Vector2(.55f,.965f),new Vector2(.57f,.91f),new Vector2(.44f,.91f));
    Shape(new Vector2(.42f,.81f),new Vector2(.31f,.74f),new Vector2(.36f,.42f),new Vector2(.31f,.18f),new Vector2(.49f,.13f),new Vector2(.69f,.19f),new Vector2(.64f,.44f),new Vector2(.69f,.75f),new Vector2(.57f,.81f));
    Shape(new Vector2(.32f,.75f),new Vector2(.20f,.62f),new Vector2(.10f,.40f),new Vector2(.24f,.35f),new Vector2(.40f,.67f));
    Shape(new Vector2(.66f,.76f),new Vector2(.82f,.60f),new Vector2(.89f,.39f),new Vector2(.75f,.36f),new Vector2(.61f,.66f));
    Stroke(.42f,.20f,.39f,.04f,.043f);Stroke(.57f,.20f,.61f,.04f,.043f);Stroke(.79f,.47f,.91f,.17f,.008f);
    return;
   }
   switch(Symbol){
    case 0:Stroke(.27f,.22f,.72f,.85f,.036f);Shape(new Vector2(.17f,.08f),new Vector2(.24f,.35f),new Vector2(.36f,.25f));break;
    case 1:Shape(new Vector2(.15f,.30f),new Vector2(.85f,.30f),new Vector2(.7f,.63f),new Vector2(.59f,.73f),new Vector2(.41f,.73f),new Vector2(.28f,.62f));break;
    case 2:Shape(new Vector2(.2f,.78f),new Vector2(.42f,.83f),new Vector2(.5f,.68f),new Vector2(.58f,.83f),new Vector2(.8f,.78f),new Vector2(.91f,.48f),new Vector2(.7f,.41f),new Vector2(.68f,.17f),new Vector2(.3f,.17f),new Vector2(.3f,.41f),new Vector2(.09f,.48f));break;
    case 3:Shape(new Vector2(.30f,.19f),new Vector2(.65f,.19f),new Vector2(.76f,.60f),new Vector2(.63f,.81f),new Vector2(.37f,.8f),new Vector2(.34f,.60f),new Vector2(.18f,.52f));break;
    case 4:Shape(new Vector2(.35f,.81f),new Vector2(.65f,.81f),new Vector2(.63f,.34f),new Vector2(.81f,.26f),new Vector2(.85f,.15f),new Vector2(.2f,.15f),new Vector2(.28f,.32f));break;
    default:for(int i=0;i<10;i++){float a=i*Mathf.PI/5;Disc(.5f+Mathf.Cos(a)*.27f,.5f+Mathf.Sin(a)*.3f,.048f,.048f);}break;
   }
  }
 }
}
