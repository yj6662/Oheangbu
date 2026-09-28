using UnityEngine;
using UnityEngine.UI;
namespace Oheangbu.App.World.UI {
 public sealed partial class WorldMapPresenter {
 void ApplyIconStyle(){
 if(dependencies.Icons==null)return;
 foreach(var t in MiniRoot.GetComponentsInChildren<Text>(true))t.enabled=false;
 foreach(var t in FullRoot.GetComponentsInChildren<Text>(true))t.enabled=false;
 foreach(var t in regionLabels){t.enabled=true;t.raycastTarget=false;}
 var border=MiniRoot.Find("InkBorder");if(border!=null)border.gameObject.SetActive(false);
 MiniRoot.GetComponent<Image>().sprite=PlaytestUiRoot.Instance?.Theme?.PromptPaper;
 var maskImage=miniViewport.gameObject.AddComponent<Image>();maskImage.sprite=PlaytestUiRoot.Instance.Theme.PromptPaper;maskImage.raycastTarget=false;
 miniViewport.gameObject.AddComponent<Mask>().showMaskGraphic=false;
 MiniRoot.sizeDelta=new Vector2(268,272);miniViewport.anchoredPosition=new Vector2(10,-12);
 foreach(var line in new[]{miniLines,miniMajorLines,caveFootprint,caveDetail})line.BrushStyle=true;
 paperInk.BrushStyle=true;macroInk.BrushStyle=true;
 if(data.PaintedRelief){
 paperMaterial.SetFloat("_PaintedRelief",1);
 foreach(var line in new[]{miniLines,miniMajorLines,caveFootprint,caveDetail})line.PaintedRelief=true;
 paperInk.PaintedRelief=true;macroInk.PaintedRelief=true;
 if(miniPaperMaterial!=null)miniPaperMaterial.SetFloat("_PaintedRelief",1);
 // A restrained paper margin leaves the illustrated terrain readable.
 MiniRoot.GetComponent<Image>().sprite=null;
 MiniRoot.GetComponent<Image>().color=new Color(.65f,.61f,.48f,.9f);
 maskImage.sprite=null;
 MiniRoot.sizeDelta=new Vector2(268,268);miniViewport.anchoredPosition=new Vector2(5,-5);miniViewport.sizeDelta=new Vector2(258,258);
 }
 foreach(var marker in markers){SetSymbol(marker.Mini,dependencies.Icons.Marker(marker.Spec),marker.Spec.Kind==WorldMapMarkerKind.Mountain?42:30);SetSymbol(marker.Full,dependencies.Icons.Marker(marker.Spec),34);}
 miniPlayer.SetAsLastSibling();fullPlayer.SetAsLastSibling();
 SetSymbol(miniCheckpoint,dependencies.Icons.Inn,28);SetSymbol(fullCheckpoint,dependencies.Icons.Inn,32);
 foreach(var button in FullRoot.GetComponentsInChildren<Button>(true)){
 var label=button.GetComponentInChildren<Text>(true);if(label!=null){label.enabled=false;CompactUiSymbols.Draw(button.transform,button.name+" "+label.text,dependencies.Icons,dependencies.Ink);}}
 if(legend!=null){var rect=(RectTransform)legend.transform;rect.sizeDelta=new Vector2(100,270);
 Sprite[] sprites={dependencies.Icons.Mountain,dependencies.Icons.Inn,dependencies.Icons.Cave};
 for(int i=0;i<sprites.Length;i++){var icon=PlaytestUiView.Image(PlaytestUiView.Rect("Symbol",rect,22,20+i*78,56,56),dependencies.Ink,sprites[i]);icon.preserveAspect=true;}}
 }
 static void SetSymbol(RectTransform rect,Sprite sprite,float size){var image=rect.GetComponent<Image>();if(image!=null){image.sprite=sprite;image.preserveAspect=true;}rect.sizeDelta=Vector2.one*size;rect.localRotation=Quaternion.identity;}
 }
}
