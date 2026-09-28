using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        string optionsTab="화면";
        Text settingsErrorText;
        UserSettingsData displayDraft;
        void BuildOptions()
        {
            subheading.text="";
            displayDraft=Settings.Current;
            string[] tabs={"화면","소리","조작","접근성"};
            for(int i=0;i<tabs.Length;i++)
            {string tab=tabs[i];V.Button(contentRoot,"OptionTab_"+tab,tab,Theme,i*195,0,176,48,()=>{optionsTab=tab;OpenPage("옵션");},optionsTab==tab);}
            if(optionsTab=="화면")BuildDisplayOptions();
            else if(optionsTab=="소리")BuildAudioOptions();
            else if(optionsTab=="조작")BuildInputOptions();
            else BuildAccessibilityOptions();
            V.Button(contentRoot,"ResetSettings","기본값 복원",Theme,0,532,270,52,()=>Confirm("설정을 기본값으로 바꿀까요?","화면 설정은 적용 후 다시 확인.",()=>{Settings.Reset();ShowDisplayConfirmation();}));
            settingsErrorText=V.Text(contentRoot,"SettingsError",Settings.SaveError??"",Theme.Font,18,Theme.Seal,304,540,808,48);
            settingsBuilt=true;
        }
        void BuildDisplayOptions()
        {
            var sizes=Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height))
                .Where(v=>v.x>=1280&&v.y>=720).Distinct().OrderBy(v=>v.x).ThenBy(v=>v.y).ToList();
            var current=new Vector2Int(displayDraft.ScreenWidth,displayDraft.ScreenHeight);
            if(!sizes.Contains(current))sizes.Add(current);
            if(sizes.Count==0)sizes.Add(new Vector2Int(1920,1080));
            OptionCycle("해상도",90,()=>displayDraft.ScreenWidth+" × "+displayDraft.ScreenHeight,()=>
            {int index=sizes.IndexOf(new Vector2Int(displayDraft.ScreenWidth,displayDraft.ScreenHeight));var v=sizes[(index+1)%sizes.Count];displayDraft.ScreenWidth=v.x;displayDraft.ScreenHeight=v.y;});
            var modes=new[]{FullScreenMode.FullScreenWindow,FullScreenMode.Windowed,FullScreenMode.ExclusiveFullScreen};
            OptionCycle("화면 모드",163,()=>displayDraft.WindowMode==FullScreenMode.Windowed?"창 모드":displayDraft.WindowMode==FullScreenMode.ExclusiveFullScreen?"전체 화면":"테두리 없는 전체 화면",()=>
            {displayDraft.WindowMode=modes[(Array.IndexOf(modes,displayDraft.WindowMode)+1)%modes.Length];});
            OptionCycle("품질",236,()=>QualitySettings.names[Mathf.Clamp(displayDraft.QualityLevel,0,QualitySettings.names.Length-1)],()=>displayDraft.QualityLevel=(displayDraft.QualityLevel+1)%Mathf.Max(1,QualitySettings.names.Length));
            OptionCycle("수직동기화",309,()=>displayDraft.VSyncCount==0?"끄기":"켜기",()=>displayDraft.VSyncCount=displayDraft.VSyncCount==0?1:0);
            var rates=new[]{30,60,90,120,144,165,240,-1};
            OptionCycle("프레임 제한",382,()=>displayDraft.TargetFrameRate<0?"제한 없음":displayDraft.TargetFrameRate+" fps",()=>displayDraft.TargetFrameRate=rates[(Array.IndexOf(rates,displayDraft.TargetFrameRate)+1)%rates.Length]);
            V.Button(contentRoot,"ApplyDisplay","화면 설정 적용",Theme,720,464,392,52,()=>{Settings.Preview(displayDraft);ShowDisplayConfirmation();},true);
            V.Text(contentRoot,"VSyncHint","수직동기화가 켜지면 화면 주사율을 우선한다.",Theme.Font,18,Theme.Muted,0,470,680,43);
        }
        void OptionCycle(string label,float y,Func<string> value,Action next)
        {
            V.Text(contentRoot,"Name_"+label,label,Theme.Font,24,Theme.Ink,0,y+11,350,45);
            Button b=null;
            void Display(){
                var text=b.GetComponentInChildren<Text>(true);string current=value();
                if(Theme.Icons==null){text.text=current+"   ›";return;}
                if(label=="해상도"||label=="프레임 제한"){text.text=current.Replace(" fps","").Replace("제한 없음","∞");return;}
                text.enabled=false;var old=b.transform.Find("ControlSymbol");if(old!=null){old.gameObject.SetActive(false);Destroy(old.gameObject);}
                CompactUiSymbols.Draw(b.transform,label=="품질"?"quality:"+displayDraft.QualityLevel:current,Theme.Icons,Theme.Ink);
            }
            b=V.Button(contentRoot,"Value_"+label,value(),Theme,425,y,687,56,()=>{next();Display();});Display();
        }
        void LiveSetting(Action<UserSettingsData> change)
        {var data=Settings.Current;change(data);Settings.Apply(data);}
        void VolumeRow(string label,float y,float initial,Action<UserSettingsData,float> change,float min=0,float max=1,string suffix="%")
        {
            V.Text(contentRoot,"Name_"+label,label,Theme.Font,24,Theme.Ink,0,y,360,48);
            var readout=V.Text(contentRoot,"Value_"+label,"",Theme.Font,23,Theme.Seal,1000,y,110,46,TextAnchor.MiddleRight);
            Action<float> show=value=>readout.text=(suffix=="%"?Mathf.RoundToInt(value*100).ToString():value.ToString("0.00"))+suffix;
            show(initial);
            V.Slider(contentRoot,"Slider_"+label,410,y+3,550,min,max,initial,v=>{show(v);LiveSetting(s=>change(s,v));},Theme);
            V.Rule(contentRoot,Theme,0,y+74,1112);
        }
        void BuildAudioOptions()
        {
            var value=Settings.Current;
            VolumeRow("전체 음량",106,value.MasterVolume,(s,v)=>s.MasterVolume=v);
            VolumeRow("게임 효과음",215,value.GameplayVolume,(s,v)=>s.GameplayVolume=v);
            VolumeRow("UI 효과음",324,value.UiVolume,(s,v)=>s.UiVolume=v);
            V.Button(contentRoot,"Listen","효과음 들어보기",Theme,722,447,390,54,()=>PlayUi(Theme.ConfirmSound,.7f));
        }
        void BuildInputOptions()
        {
            VolumeRow("마우스 감도",106,Settings.Current.LookSensitivity,(s,v)=>s.LookSensitivity=v,.25f,3f,"×");
            OptionCycle("시점 Y축 반전",238,()=>Settings.Current.InvertLookY?"켜기":"끄기",()=>LiveSetting(s=>s.InvertLookY=!s.InvertLookY));
            V.Text(contentRoot,"InputHint","작도 좌표·필세는 감도의 영향을 받지 않는다.",Theme.Font,22,Theme.Muted,0,340,1060,104);
            V.Button(contentRoot,"ControlsLink","전체 조작 안내",Theme,722,447,390,54,()=>OpenPage("조작 안내"));
        }
        void BuildAccessibilityOptions()
        {
            VolumeRow("UI 크기",92,Settings.Current.UiScale,(s,v)=>s.UiScale=v,.8f,1.2f);
            VolumeRow("본문 크기",188,Settings.Current.TextScale,(s,v)=>s.TextScale=v,.9f,1.25f);
            OptionCycle("미니맵",300,()=>Settings.Current.ShowMinimap?"표시":"숨기기",()=>LiveSetting(s=>s.ShowMinimap=!s.ShowMinimap));
            OptionCycle("지도 펼침 동작 줄이기",388,()=>Settings.Current.ReducedMotion?"켜기":"끄기",()=>LiveSetting(s=>s.ReducedMotion=!s.ReducedMotion));
            V.Text(contentRoot,"ReducedHint","동작 줄이기를 켜면 접힘 대신 짧은 전환으로 지도를 엽니다.",Theme.Font,18,Theme.Muted,0,468,1112,45);
        }
    }
}
