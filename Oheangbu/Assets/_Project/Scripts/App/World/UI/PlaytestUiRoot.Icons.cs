using UnityEngine;
using UnityEngine.UI;
namespace Oheangbu.App.World.UI {
 public sealed partial class PlaytestUiRoot {
 // #304 save-failure mark (IMPLEMENTATION §7.2): the 40px UnsavedProgress icon is gone. The first failure of a journey save or
 // a settings save raises ONE error toast (저장하지 못했다); the pause page's save line shows the failed state (D10). Name kept
 // because RefreshStatus calls it every .25 s. The icon-only detail (BuildIconDetail) and the icon-mode label hiding
 // (ApplyIconChrome) are removed: the StoryBand304 page and the text labels serve both themes (Theme.Icons = support only).
 const string Menu304SaveHint="저장 위치를 확인하고 다시 시도해 주세요";
 bool menu304SaveFailed,menu304SettingsSaveFailed;
 void RefreshSaveFailureIcon(){
  bool failed=!string.IsNullOrEmpty(Session!=null?Session.SaveError:null);
  if(failed&&!menu304SaveFailed)Menu304Notify(new UiNotice304(UiNoticeKind304.Error,"저장하지 못했다",Menu304SaveHint));
  menu304SaveFailed=failed;
  bool settingsFailed=!string.IsNullOrEmpty(Settings!=null?Settings.SaveError:null);
  if(settingsFailed&&!menu304SettingsSaveFailed)Menu304Notify(new UiNotice304(UiNoticeKind304.Error,"설정을 저장하지 못했다",Menu304SaveHint));
  menu304SettingsSaveFailed=settingsFailed;
 }
 }
 // Small brush-based control symbols extend the imported pictograms without introducing a font icon dependency.
 public static class CompactUiSymbols {
 public static void Draw(Transform parent,string meaning,CompactUiProfileSO icons,Color tint){
 var root=PlaytestUiView.Rect("ControlSymbol",parent,0,0,40,40);root.anchorMin=root.anchorMax=root.pivot=new Vector2(.5f,.5f);root.anchoredPosition=Vector2.zero;
 Sprite sprite=(meaning.Contains("주막")||meaning.Contains("정비"))?icons.Inn:meaning.Contains("저장")?icons.Save:
 meaning.Contains("ReturnTitle")||meaning.Contains("로비")?icons.Exit:
 meaning.Contains("Cancel")||meaning.Contains("Close")||meaning.Contains("Done")||meaning.Contains("Resume")||meaning.Contains("확인")||meaning.Contains("Apply")?icons.Resume:
 meaning.Contains("지도")||meaning.Contains("Map")||meaning.Contains("범례")?icons.Mountain:
 meaning.Contains("술식")?icons.Save:
 meaning.Contains("조작")||meaning.Contains("Controls")?icons.Hand:null;
 if(sprite!=null){var image=PlaytestUiView.Image(root,tint,sprite);image.preserveAspect=true;if(meaning.Contains("Cancel"))root.localRotation=Quaternion.Euler(0,0,180);return;}
 void Stroke(float x1,float y1,float x2,float y2,float width=3){
 var d=new Vector2(x2-x1,y2-y1);var line=PlaytestUiView.Rect("Brush",root,0,0,d.magnitude,width);line.anchorMin=line.anchorMax=line.pivot=new Vector2(.5f,.5f);line.anchoredPosition=new Vector2((x1+x2)/2,(y1+y2)/2);line.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);
 PlaytestUiView.Image(line,tint,PlaytestUiRoot.Instance?.Theme?.BrushStroke);}
 if(meaning.Contains("소지품")){Stroke(-13,-14,13,-14);Stroke(-13,-14,-16,4);Stroke(-16,4,-9,11);Stroke(-9,11,9,11);Stroke(9,11,16,4);Stroke(16,4,13,-14);Stroke(-9,11,-5,17);Stroke(-5,17,5,17);Stroke(5,17,9,11);}
 else if(meaning.Contains("통보")){for(int i=0;i<12;i++){float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;Stroke(Mathf.Cos(a)*14,Mathf.Sin(a)*14,Mathf.Cos(b)*14,Mathf.Sin(b)*14);}Stroke(-3,-3,3,-3);Stroke(3,-3,3,3);Stroke(3,3,-3,3);Stroke(-3,3,-3,-3);}
 else if(meaning=="켜기"||meaning=="표시"){Stroke(-13,0,-3,-10,4);Stroke(-3,-10,15,13,4);}
 else if(meaning=="끄기"||meaning=="숨기기"){Stroke(-12,-12,12,12);Stroke(-12,12,12,-12);}
 else if(meaning.StartsWith("quality:")){int level=int.Parse(meaning.Substring(8))+1;var original=tint;for(int i=0;i<5;i++){tint=original;tint.a*=i<level?1f:.16f;Stroke(-14+i*7,-13,-14+i*7,-5+i*5,4);}tint=original;}
 else if(meaning.Contains("일시정지")){Stroke(-7,-14,-7,14,5);Stroke(7,-14,7,14,5);}
 else if(meaning.Contains("차패")||meaning.Contains("자동차")||meaning.Contains("가마")){Stroke(-17,-8,17,-8);Stroke(-17,-8,-12,5);Stroke(-12,5,12,5);Stroke(12,5,17,-8);Stroke(-10,5,-7,14);Stroke(-7,14,6,14);Stroke(6,14,12,5);Stroke(-10,-11,-10,-16,6);Stroke(10,-11,10,-16,6);}
 else if(meaning.Contains("음")||meaning.Contains("소리")||meaning.Contains("Listen")){Stroke(-17,-6,-8,-6);Stroke(-17,6,-8,6);Stroke(-17,-6,-17,6);Stroke(-8,6,4,14);Stroke(4,14,4,-14);Stroke(4,-14,-8,-6);Stroke(12,-10,17,0);Stroke(17,0,12,10);}
 else if(meaning.Contains("해상도")){Stroke(-17,4,-17,14);Stroke(-17,14,-7,14);Stroke(7,14,17,14);Stroke(17,14,17,4);Stroke(-17,-4,-17,-14);Stroke(-17,-14,-7,-14);Stroke(7,-14,17,-14);Stroke(17,-14,17,-4);Stroke(-7,-5,7,5);}
 else if(meaning.Contains("품질")){for(int i=0;i<3;i++){float x=-12+i*12;Stroke(x-4,1,x,7);Stroke(x,7,x+4,1);Stroke(x+4,1,x,-5);Stroke(x,-5,x-4,1);}}
 else if(meaning.Contains("수직")||meaning.Contains("반전")){Stroke(-7,-13,-7,13);Stroke(-7,13,-13,5);Stroke(-7,13,-1,5);Stroke(8,13,8,-13);Stroke(8,-13,2,-5);Stroke(8,-13,14,-5);}
 else if(meaning.Contains("프레임")||meaning.Contains("Reset")||meaning.Contains("복원")){for(int i=0;i<10;i++){float a=(i+1)*Mathf.PI/6,b=(i+2)*Mathf.PI/6;Stroke(Mathf.Cos(a)*15,Mathf.Sin(a)*15,Mathf.Cos(b)*15,Mathf.Sin(b)*15);}Stroke(0,0,0,10);Stroke(0,0,8,-3);Stroke(14,0,19,4);Stroke(14,0,19,-4);}
 else if(meaning.Contains("마우스")||meaning.Contains("시점")){Stroke(-10,-13,10,-13);Stroke(-10,-13,-12,6);Stroke(-12,6,-7,14);Stroke(-7,14,7,14);Stroke(7,14,12,6);Stroke(12,6,10,-13);Stroke(0,14,0,1);Stroke(-12,1,12,1);}
 else if(meaning.Contains("접근성")){Stroke(-2,7,2,15,7);Stroke(0,5,0,-4);Stroke(-14,2,14,2);Stroke(0,-4,-10,-16);Stroke(0,-4,10,-16);}
 else if(meaning.Contains("화면")||meaning.Contains("해상도")||meaning.Contains("품질")||meaning.Contains("창 모드")){Stroke(-18,-11,18,-11);Stroke(18,-11,18,13);Stroke(18,13,-18,13);Stroke(-18,13,-18,-11);Stroke(0,-11,0,-17);Stroke(-9,-17,9,-17);}
 else if(meaning.Contains("글씨")||meaning.Contains("갈무리")){Stroke(-12,-14,12,16,6);Stroke(-14,-17,-7,-15);}
 else if(meaning.Contains("걷기")||meaning.Contains("달리기")||meaning.Contains("점프")||meaning.Contains("회피")||meaning.Contains("착석")){Stroke(-2,7,2,15,7);Stroke(0,6,-3,-4,4);Stroke(-3,-4,-12,-16);Stroke(-3,-4,10,-12);Stroke(-1,3,-14,0);Stroke(-1,3,12,7);}
 else if(meaning.Contains("현재")){Stroke(-15,0,15,0);Stroke(0,-15,0,15);}
 else if(meaning.Contains("지우기")||meaning.Contains("취소")){Stroke(-12,-12,12,12);Stroke(-12,12,12,-12);}
 else {for(int i=0;i<8;i++){float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;Stroke(Mathf.Cos(a)*8,Mathf.Sin(a)*8,Mathf.Cos(a)*16,Mathf.Sin(a)*16,4);Stroke(Mathf.Cos(a)*8,Mathf.Sin(a)*8,Mathf.Cos(b)*8,Mathf.Sin(b)*8,3);}}
 }
 }
}
