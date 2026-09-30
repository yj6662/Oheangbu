using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 설정 (options.png, DESIGN §5.5 / §7.8). Page coordinates = the 1920x1080 mockup (contentRoot, top-left origin).
    // Category tabs OptionTab_<화면|소리|조작|접근성> (harness options-tab:*) at x64 pitch 70, divider x292, value rows named
    // Value_<label> at y = 180 + 86i (name x392, ‹ value › box x640~1010), focus = wet_m h118 from x322 (MenuOptionsPanel304
    // keeps values / 적용 전 / disabled reasons / the 설명 칸 at x1346). Display settings still go through displayDraft ->
    // ApplyDisplay (Settings.Preview + the 15 s confirmation in Flow); the other tabs apply live exactly as before.
    public sealed partial class PlaytestUiRoot
    {
        string optionsTab="화면";
        TMP_Text settingsErrorText;
        UserSettingsData displayDraft;
        MenuOptionsPanel304 menu304Options;
        static readonly string[] Menu304OptionTabs={"화면","소리","조작","접근성"};

        void BuildOptions()
        {
            var s=V.Style(Theme);
            displayDraft=Settings.Current;
            var tab=Menu304CategoryTabs(s,"OptionTab_",Menu304OptionTabs,optionsTab,t=>{optionsTab=t;OpenPage("옵션");});
            var help=V.Rect("OptionHelp",contentRoot,1346,180,440,760);
            menu304Options=contentRoot.gameObject.AddComponent<MenuOptionsPanel304>();
            menu304Options.Init(s,help);
            menu304Options.Above=tab;
            if(optionsTab=="화면")BuildDisplayOptions(s);
            else if(optionsTab=="소리")BuildAudioOptions(s);
            else if(optionsTab=="조작")BuildInputOptions(s);
            else BuildAccessibilityOptions(s);
            var reset=Menu304SecondaryRow(s,"ResetSettings","기본값 복원",optionsTab=="화면"?780:352,630,
                ()=>Confirm("설정을 기본값으로 바꿀까요?","화면 설정은 적용 후 다시 확인.",()=>{Settings.Reset();ShowDisplayConfirmation();}));
            if(menu304Options.Below==null)menu304Options.Below=reset;
            menu304Options.Refresh();
            // initial selection = the first live row (IMPLEMENTATION §4.2 설정 = 첫 행)
            foreach(var r in menu304Options.Rows)if(r.Focus!=null&&r.Focus.Button!=null&&r.Focus.Button.interactable){menu304DefaultSelection=r.Focus.Button.gameObject;break;}
            settingsErrorText=V.Label(s,contentRoot,"SettingsError",Settings.SaveError??"",UiType304.MetaBold20,s.CinnabarLift,392,730,900,0,TextAlignmentOptions.TopLeft,true);
            settingsBuilt=true;
        }

        // ------------------------------------------------------------------ tabs
        void BuildDisplayOptions(UiStyle304SO s)
        {
            var sizes=Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height))
                .Where(v=>v.x>=1280&&v.y>=720).Distinct().OrderBy(v=>v.x).ThenBy(v=>v.y).ToList();
            var current=new Vector2Int(displayDraft.ScreenWidth,displayDraft.ScreenHeight);
            if(!sizes.Contains(current))sizes.Add(current);
            if(sizes.Count==0)sizes.Add(new Vector2Int(1920,1080));
            var modes=new[]{FullScreenMode.FullScreenWindow,FullScreenMode.Windowed,FullScreenMode.ExclusiveFullScreen};
            var rates=new[]{30,60,90,120,144,165,240,-1};
            int qualityCount=Mathf.Max(1,QualitySettings.names.Length);
            OptionCycle(s,"해상도",0,()=>displayDraft.ScreenWidth+" × "+displayDraft.ScreenHeight,dir=>
            {int index=Mathf.Max(0,sizes.IndexOf(new Vector2Int(displayDraft.ScreenWidth,displayDraft.ScreenHeight)));var v=sizes[Menu304Wrap(index+dir,sizes.Count)];displayDraft.ScreenWidth=v.x;displayDraft.ScreenHeight=v.y;},
                ()=>displayDraft.ScreenWidth!=Settings.Current.ScreenWidth||displayDraft.ScreenHeight!=Settings.Current.ScreenHeight);
            OptionCycle(s,"화면 모드",1,()=>Menu304WindowModeName(displayDraft.WindowMode),dir=>
            {displayDraft.WindowMode=modes[Menu304Wrap(Array.IndexOf(modes,displayDraft.WindowMode)+dir,modes.Length)];},
                ()=>displayDraft.WindowMode!=Settings.Current.WindowMode);
            OptionCycle(s,"품질",2,()=>QualitySettings.names.Length>0?QualitySettings.names[Mathf.Clamp(displayDraft.QualityLevel,0,QualitySettings.names.Length-1)]:"기본",
                dir=>displayDraft.QualityLevel=Menu304Wrap(displayDraft.QualityLevel+dir,qualityCount),
                ()=>displayDraft.QualityLevel!=Settings.Current.QualityLevel);
            OptionCycle(s,"수직동기화",3,()=>displayDraft.VSyncCount==0?"끄기":"켜기",dir=>displayDraft.VSyncCount=displayDraft.VSyncCount==0?1:0,
                ()=>(displayDraft.VSyncCount==0)!=(Settings.Current.VSyncCount==0));
            OptionCycle(s,"프레임 제한",4,()=>displayDraft.TargetFrameRate<0?"제한 없음":displayDraft.TargetFrameRate+" fps",
                dir=>{int i=Array.IndexOf(rates,displayDraft.TargetFrameRate);displayDraft.TargetFrameRate=rates[Menu304Wrap((i<0?rates.Length-1:i)+dir,rates.Length)];},
                ()=>displayDraft.TargetFrameRate!=Settings.Current.TargetFrameRate,
                ()=>displayDraft.VSyncCount>0?"수직동기화 사용 중":null);
            // primary: 화면 설정 적용 32px + filled Enter (640,644) + dry under-stroke (382,690)
            var apply=V.FocusRow(s,contentRoot,"ApplyDisplay","화면 설정 적용",352,630,380,64,()=>{Settings.Preview(displayDraft);ShowDisplayConfirmation();},new FocusRowSpec304
            {
                Role=UiType304.Title32,LabelX=40,LabelY=14,Underlay=StrokeClass304.WetM,UnderlayH=110,
                Key="Enter",KeySmall=false,KeyX=288,KeyY=14,UnderStroke=true,UnderStrokeW=330,SoundTheme=Theme,
            });
            menu304Options.Below=apply.Button;
            // 지금 쓰는 화면 (392,776): what the screen really runs now, not the draft
            V.Label(s,contentRoot,"CurrentDisplay","지금 쓰는 화면",UiType304.Meta20,s.Mist,392,776);
            string[,] now={{"해상도",Screen.width+" × "+Screen.height},{"화면 모드",Menu304WindowModeName(Screen.fullScreenMode)},{"수직동기화",QualitySettings.vSyncCount==0?"끄기":"켜기"}};
            for(int i=0;i<now.GetLength(0);i++)
            {
                V.Label(s,contentRoot,"CurrentName_"+i,now[i,0],UiType304.Meta20,s.Mist,392,806+32*i);
                V.Label(s,contentRoot,"CurrentValue_"+i,now[i,1],UiType304.Meta20,s.Paper,540,806+32*i);
            }
        }
        void BuildAudioOptions(UiStyle304SO s)
        {
            OptionStepper(s,"전체 음량",0,()=>Settings.Current.MasterVolume,(d,v)=>d.MasterVolume=v,0,1,.05f,"%");
            OptionStepper(s,"게임 효과음",1,()=>Settings.Current.GameplayVolume,(d,v)=>d.GameplayVolume=v,0,1,.05f,"%");
            OptionStepper(s,"UI 효과음",2,()=>Settings.Current.UiVolume,(d,v)=>d.UiVolume=v,0,1,.05f,"%");
            var listen=Menu304SecondaryRow(s,"Listen","효과음 들어보기",352,630-86*2,()=>PlayUi(Theme.ConfirmSound,.7f));
            menu304Options.Below=listen;
        }
        void BuildInputOptions(UiStyle304SO s)
        {
            OptionStepper(s,"마우스 감도",0,()=>Settings.Current.LookSensitivity,(d,v)=>d.LookSensitivity=v,.25f,3f,.05f,"×");
            OptionCycle(s,"시점 Y축 반전",1,()=>Settings.Current.InvertLookY?"켜기":"끄기",dir=>LiveSetting(d=>d.InvertLookY=!d.InvertLookY));
            var link=Menu304SecondaryRow(s,"ControlsLink","전체 조작 안내",352,630-86*2,()=>OpenPage("조작 안내"));
            menu304Options.Below=link;
        }
        void BuildAccessibilityOptions(UiStyle304SO s)
        {
            OptionStepper(s,"UI 크기",0,()=>Settings.Current.UiScale,(d,v)=>d.UiScale=v,.8f,1.2f,.05f,"%");
            OptionStepper(s,"본문 크기",1,()=>Settings.Current.TextScale,(d,v)=>d.TextScale=v,.9f,1.25f,.05f,"%");
            // #306: 미니맵 (HudMinimap304: north up / turns with the view / hidden = ShowMinimap + MinimapFollowView) and 방위선
            // (ShowBearingLine) are separate settings
            OptionCycle(s,"미니맵",2,()=>MinimapModes[MinimapMode(Settings.Current)],dir=>LiveSetting(d=>SetMinimapMode(d,Menu304Wrap(MinimapMode(d)+dir,MinimapModes.Length))));
            OptionCycle(s,"방위선",3,()=>Settings.Current.ShowBearingLine?"표시":"숨기기",dir=>LiveSetting(d=>d.ShowBearingLine=!d.ShowBearingLine));
            OptionCycle(s,"지도 펼침 동작 줄이기",4,()=>Settings.Current.ReducedMotion?"켜기":"끄기",dir=>LiveSetting(d=>d.ReducedMotion=!d.ReducedMotion));
        }
        static readonly string[] MinimapModes={"북쪽 고정","시점 따라","숨기기"};
        static int MinimapMode(UserSettingsData d)=>!d.ShowMinimap?2:d.MinimapFollowView?1:0;
        static void SetMinimapMode(UserSettingsData d,int mode){d.ShowMinimap=mode!=2;if(mode!=2)d.MinimapFollowView=mode==1;}

        // ------------------------------------------------------------------ rows
        /// <summary>One 설정 row: FocusRow Value_&lt;label&gt; (name Label26 x392, hit rect 86 tall), ‹ value › in the box x640~1010,
        /// 적용 전 under the name (only while pending), disabled reason in the value box. Enter / click = next value, ‹ › and
        /// left / right = previous / next (MenuOptionStepper304).</summary>
        MenuOptionsPanel304.Row OptionCycle(UiStyle304SO s,string label,int index,Func<string> value,Action<int> step,Func<bool> pending=null,Func<string> blocked=null)
        {
            float v=180+86*index;                                 // value box top (mockup)
            MenuOptionsPanel304.Row entry=null;
            var row=V.FocusRow(s,contentRoot,"Value_"+label,label,322,v-12,720,86,()=>menu304Options.StepRow(entry,1),new FocusRowSpec304
            {
                Role=UiType304.Label26,LabelX=70,LabelY=18,DabDy=8,
                Underlay=StrokeClass304.WetM,UnderlayH=118,UnderlayX=0,UnderlayY=-14,ContentRight=694,
                // 적용 전: 주사 on the focused paper underlay, 주사 밝음 on the veil (common.css --cin-lift: "적용 전, 저장 실패")
                Meta="적용 전",MetaRole=UiType304.MetaBold20,MetaColor=s.CinnabarLift,MetaFocusColor=s.Cinnabar,MetaBelow=true,MetaX=72,MetaY=56,
                SoundTheme=Theme,
            });
            if(row.Meta!=null)row.Meta.gameObject.SetActive(false);
            var prev=V.Label(s,row.Rect,"Prev","‹",UiType304.Title30,s.Paper,318,20);
            var next=V.Label(s,row.Rect,"Next","›",UiType304.Title30,s.Paper,0,20);
            V.Place(next.rectTransform,688-next.rectTransform.sizeDelta.x,20);
            var val=V.Label(s,row.Rect,"Value",value(),UiType304.Body24,s.Paper,336,19,334,0,TextAlignmentOptions.Top);
            var reason=V.Label(s,row.Rect,"Reason","",UiType304.Meta20,s.Mist,336,26,334,0,TextAlignmentOptions.Top);
            reason.gameObject.SetActive(false);
            foreach(var g in new Graphic[]{prev,next,val})row.Visual.AddTint(g,s.Paper,s.Ink);
            Menu304ArrowHit(row.Rect,"PrevHit",306,12,()=>menu304Options.StepRow(entry,-1));
            Menu304ArrowHit(row.Rect,"NextHit",650,12,()=>menu304Options.StepRow(entry,1));
            entry=menu304Options.Add(new MenuOptionsPanel304.Row{Label=label,Focus=row,Value=val,Prev=prev,Next=next,Reason=reason,Read=value,Step=step,Pending=pending,Blocked=blocked});
            return entry;
        }
        /// <summary>Numeric setting as a stepper row (was a slider): ‹ 85% ›, step `step`, applied live like before.</summary>
        void OptionStepper(UiStyle304SO s,string label,int index,Func<float> read,Action<UserSettingsData,float> write,float min,float max,float step,string suffix)
        {
            string Show(){float v=read();return suffix=="%"?Mathf.RoundToInt(v*100)+"%":v.ToString("0.00")+suffix;}
            OptionCycle(s,label,index,Show,dir=>
            {
                float v=Mathf.Clamp(Mathf.Round((read()+dir*step)/step)*step,min,max);
                if(!Mathf.Approximately(v,read()))LiveSetting(d=>write(d,v));
                if(Theme.SoundPalette!=null)PlayNamedSound("settings_tick",.2f);
            });
        }
        void Menu304ArrowHit(RectTransform row,string name,float x,float y,UnityAction click)
        {
            var hit=V.Rect(name,row,x,y,44,52);
            var image=V.Image(hit,new Color(0,0,0,0),null,true);image.canvasRenderer.cullTransparentMesh=true;
            var b=hit.gameObject.AddComponent<Button>();b.transition=Selectable.Transition.None;b.targetGraphic=image;
            b.navigation=new Navigation{mode=Navigation.Mode.None};   // clicking an arrow must not steal the row's focus
            b.onClick.AddListener(click);
        }
        /// <summary>Secondary action (기본값 복원, 효과음 들어보기, 전체 조작 안내): Label26 paper, focus = wet_s + dab.</summary>
        Button Menu304SecondaryRow(UiStyle304SO s,string name,string label,float x,float y,UnityAction click)
        {
            var row=V.FocusRow(s,contentRoot,name,label,x,y,300,64,click,new FocusRowSpec304{Role=UiType304.Label26,LabelX=40,LabelY=20,SoundTheme=Theme});
            return row.Button;
        }
        /// <summary>Vertical category tabs (설정 분류, 조작 분류): x64, y = 176 + 70i; selected = Title32 paper + swell (44,210),
        /// others Label26 mist; focus = wet_s + dab + ink label. Returns the selected tab's button.</summary>
        Button Menu304CategoryTabs(UiStyle304SO s,string prefix,IReadOnlyList<string> tabs,string selected,Action<string> choose)
        {
            Button chosen=null;
            for(int i=0;i<tabs.Count;i++)
            {
                string tab=tabs[i];bool sel=tab==selected;float y=176+70*i;
                if(sel)
                {
                    var probe=V.Label(s,contentRoot,"SwellProbe",tab,UiType304.Title32,s.Paper,0,0);
                    float w=probe.rectTransform.sizeDelta.x;probe.gameObject.SetActive(false);Destroy(probe.gameObject);
                    V.Swell(s,contentRoot,44,y+34,w+96,32);
                }
                var row=V.FocusRow(s,contentRoot,prefix+tab,tab,24,y-6,250,58,()=>choose(tab),new FocusRowSpec304
                {
                    Role=sel?UiType304.Title32:UiType304.Label26,LabelX=40,LabelY=4,   // unselected sat 4 px low at 8 (options.png)
                    LabelColor=sel?s.Paper:s.Mist,SoundTheme=Theme,
                });
                if(sel)chosen=row.Button;
            }
            // on the shared InkReveal material (fully revealed) so the α.14 hairline gets the same linear-space alpha remap as the
            // strokes: plain UI/Default drew it at x292 twice as bright as options.png (93 vs 48 on the veil, after2/options.png)
            var rule=V.Image(V.Rect("CategoryRule",contentRoot,292,176,1,800),UiStyle304SO.A(s.Paper,.14f));
            InkRevealEffect.On(rule,s,InkRevealMode.Bleed,1f);
            return chosen;
        }
        static int Menu304Wrap(int i,int n)=>n<=0?0:((i%n)+n)%n;
        static string Menu304WindowModeName(FullScreenMode mode)=>mode==FullScreenMode.Windowed?"창 모드":mode==FullScreenMode.ExclusiveFullScreen?"전체 화면":mode==FullScreenMode.MaximizedWindow?"최대화 창":"테두리 없는 전체 화면";
        void LiveSetting(Action<UserSettingsData> change)
        {var data=Settings.Current;change(data);Settings.Apply(data);}
    }
}
