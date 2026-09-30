using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 menu frame (IMPLEMENTATION §7.3 / §7.4 / §7.8 조작, FOUNDATION_API §7.1). Every page is one 1920x1080 V.Page304
    // (UiPageFit304 scales it to min(1, W/1920, H/1080)) under modalLayer: 먹장막 (V.Veil, lift per page from
    // UiStyle304SO.VeilLiftX) -> MenuRail304 (all pages but 일시정지) -> "PageContent" = contentRoot, the full page in mockup px
    // with a TOP-LEFT origin, so V.Rect / V.Label / V.FocusRow take the mockup coordinates as they are. First open: veil
    // wipe (VeilMs) -> rail bleed -> content bleed -> dab pop (FocusMark304 follows the page's CanvasGroup). Tab change:
    // the rail swell slides from the previous tab, the veil lift slides to the new page's x, the content bleeds in. Same-page
    // rebuilds keep everything still and restore the selected row by name. Q / E (LB / RB) walk the rail. A confirm dialog
    // suspends the page (FocusMark304.Suspend) and the selection returns when it closes.
    public sealed partial class PlaytestUiRoot
    {
        sealed class Menu304Selection { public string Name; public bool FromRail; }

        RectTransform menu304RailLayer,menu304Page;
        MenuRail304 menu304Rail;
        RawImage menu304Veil;
        MenuToastStack304 menu304Toasts;
        MenuInputIcons304 menu304Icons;bool menu304IconsLoaded;
        GameObject menu304DefaultSelection,menu304LastPageSelection;
        string menu304FromPage="",menu304ControlsTab="이동";
        float menu304LiftX=float.NaN,menu304BuiltTextScale=1f,menu304TextScaleDirtyAt=-1f;
        bool menu304Suspended;
        // true only while OpenPage runs for a rail tab click or Q / E (LB / RB): the one case where the rail keeps the focus
        bool menu304ViaRail;
        static readonly string[] Menu304ControlTabs={"이동","작도","메뉴"};
        static readonly string[] Menu304PauseRows={"소지품","술식 도감","차패","지도","옵션","조작 안내"};

        /// <summary>The current page root (V.Page304) or null. Its CanvasGroup is what a modal suspends.</summary>
        public RectTransform Menu304PageRoot=>menu304Page;
        /// <summary>The rail of the current page (null on 일시정지, 상세, trade windows).</summary>
        public MenuRail304 Menu304Rail=>menu304Rail;

        bool Menu304ToastsWait()=>IsMenuOpen||(Session!=null&&Session.RestPresentationActive);
        bool Menu304Drawing()=>Pause!=null&&Pause.Drawing!=null&&Pause.Drawing.InDrawMode;

        void Menu304ResetPageState()
        {
            menu304FromPage="";menu304LiftX=float.NaN;menu304Rail=null;menu304Page=null;menu304Veil=null;
            menu304Suspended=false;menu304LastPageSelection=null;menu304DefaultSelection=null;menu304TextScaleDirtyAt=-1f;
        }
        void Menu304ClearRailLayer(){if(menu304RailLayer!=null)V.Clear(menu304RailLayer);}
        void Menu304BeginPage(string previous){menu304FromPage=previous??"";}
        /// <summary>Rail navigation (a Tab_&lt;page&gt; click on the rail, Q / E, LB / RB): the page opens with the focus kept on
        /// the rail when the rail held it. Every other OpenPage (pause list, shortcuts, harness, code) takes the page's default.</summary>
        void Menu304OpenFromRail(string page)
        {
            menu304ViaRail=true;
            try{OpenPage(page);}
            finally{menu304ViaRail=false;}
        }
        void Menu304Shown(string page){menu304BuiltTextScale=Settings.Current.TextScale;menu304TextScaleDirtyAt=-1f;}

        // ------------------------------------------------------------------ page scaffold
        /// <summary>#304 page scaffold (name kept for the partials that call it). title = the page id.</summary>
        void BuildFrame(string title)
        {
            var s=V.Style(Theme);
            V.EnsureCanvasChannels(canvas);
            FocusMark304.Attach(s,canvasRect);
            UiText304.Prewarm(s);
            frame=null;
            string from=menu304FromPage??"";
            bool firstOpen=from.Length==0||from=="상세"||from=="지도"||from=="장비 상점"||from=="장비 강화";
            bool samePage=from==title;
            menu304Page=V.Page304(modalLayer);
            var lift=s.VeilFor(title);
            menu304Veil=V.Veil(s,menu304Page,lift.LiftX,lift.Sweep?lift.SweepXYA:(Vector3?)null);
            bool railShown=!firstOpen&&from!="일시정지";   // the pause page has no rail: coming from it the rail appears, it does not slide
            menu304Rail=title!="일시정지"?Menu304BuildRail(s,menu304Page,title,railShown?from:null):null;
            if(menu304Rail!=null&&!firstOpen&&!railShown){var rg=menu304Rail.gameObject.AddComponent<CanvasGroup>();UiTween304.BleedIn(rg,s,0,UiTween304.Token(rg)).Forget();}
            contentRoot=V.Rect("PageContent",menu304Page,0,0,UiPageFit304.Width,UiPageFit304.Height);
            var content=contentRoot.gameObject.AddComponent<CanvasGroup>();
            Menu304LegacyText(title);
            if(firstOpen)
            {
                var fx=menu304Veil.GetComponent<InkRevealEffect>();
                if(fx!=null)UiTween304.RevealIn(fx,s,s.Motion.VeilMs,UiTween304.Token(fx)).Forget();
                if(menu304Rail!=null){var rg=menu304Rail.gameObject.AddComponent<CanvasGroup>();UiTween304.BleedIn(rg,s,1,UiTween304.Token(rg)).Forget();}
                UiTween304.BleedIn(content,s,2,UiTween304.Token(content)).Forget();
            }
            else if(!samePage)
            {
                if(!float.IsNaN(menu304LiftX)&&!Mathf.Approximately(menu304LiftX,lift.LiftX)&&!UiTween304.ReducedMotion)
                {
                    // the brush lifts somewhere else: slide the lift point instead of re-wiping the whole veil
                    var vr=menu304Veil.rectTransform;float toX=vr.anchoredPosition.x,fromX=toX+(menu304LiftX-lift.LiftX);
                    vr.anchoredPosition=new Vector2(fromX,vr.anchoredPosition.y);
                    UiTween304.Run(s.Motion.Sec(s.Motion.VeilMs),s.Motion.Stroke,e=>{if(vr!=null)vr.anchoredPosition=new Vector2(Mathf.LerpUnclamped(fromX,toX,e),vr.anchoredPosition.y);},UiTween304.Token(vr)).Forget();
                }
                UiTween304.BleedIn(content,s,0,UiTween304.Token(content)).Forget();
            }
            menu304LiftX=lift.LiftX;
        }
        MenuRail304 Menu304BuildRail(UiStyle304SO s,Transform parent,string page,string fromPage)
        {
            var pages=new List<string>(IsTitle?TitlePages:GameplayPages);
            if(!IsTitle&&Session!=null&&Session.AtDemoShop)pages.Add("정비");
            if(fromPage!=null&&!pages.Contains(fromPage))fromPage=null;
            var rail=V.MenuRail(s,parent,page,pages,page=="지도"?"M":"Esc",MenuLabel,Menu304OpenFromRail,CloseMenu,"닫기",fromPage);
            Menu304RailNewMarks(s,rail);
            return rail;
        }
        /// <summary>#304 integration, D21 (REFERENCE_BOARD): a short ink flick at the top-right of the 소지품 / 술식 도감 tab labels
        /// while that page holds something not looked at yet (content area: Content304HasUnseen). Mist on the veil; the flick sprite
        /// is ContentArt304.Flick (30x11, -18°), stroke_short before content304-setup. Re-evaluated whenever the rail is rebuilt.</summary>
        void Menu304RailNewMarks(UiStyle304SO s,MenuRail304 rail)
        {
            if(rail==null||IsTitle||Session==null||Session.Progress==null)return;
            var art=ContentArt304.Load();
            foreach(var tab in rail.Tabs)
            {
                if(tab==null||tab.Label==null||(tab.Page!="소지품"&&tab.Page!="술식 도감"))continue;
                if(!Content304HasUnseen(tab.Page))continue;
                var label=tab.Label;var lr=label.rectTransform;
                float w=Mathf.Ceil(label.GetPreferredValues(label.text).x);
                float y=Mathf.Max(0f,lr.sizeDelta.y-label.fontSize-10f);   // the label is bottom-aligned in a 1.3x box: just above the glyph top
                Image mark=art!=null&&art.Flick!=null
                    ?V.SpriteImage(lr,"NewMark",art.Flick,s.Mist,w+2f,y,30,11,-18f)
                    :V.Brush(s,lr,"NewMark",StrokeClass304.Short,s.Mist,w+2f,y,30,8,1f,-18f);
                mark.raycastTarget=false;
            }
        }
        /// <summary>The 지도 page keeps its presenter (mapLayer); only the rail (with [M] 닫기) is added, above the map.</summary>
        void Menu304BuildMapRail(string previous)
        {
            var s=V.Style(Theme);
            V.EnsureCanvasChannels(canvas);FocusMark304.Attach(s,canvasRect);
            frame=null;
            menu304Page=V.Page304(menu304RailLayer,"MapRail304");
            string from=string.IsNullOrEmpty(previous)||previous=="상세"||previous=="일시정지"?null:previous;
            menu304Rail=Menu304BuildRail(s,menu304Page,"지도",from);
            if(from==null){var rg=menu304Rail.gameObject.AddComponent<CanvasGroup>();UiTween304.BleedIn(rg,s,1,UiTween304.Token(rg)).Forget();}
            contentRoot=V.Rect("PageContent",menu304Page,0,0,UiPageFit304.Width,UiPageFit304.Height);
            Menu304LegacyText("지도");
            menu304FromPage=previous??"";menu304LiftX=float.NaN;
            var tab=menu304Rail.TabButton("지도");
            if(!Menu304SelectionLive()&&tab!=null)FocusMark304.Select(tab.gameObject);
            Menu304Shown("지도");
        }
        /// <summary>heading / subheading / statusText stay non-null (older partials write them); they are hidden legacy Text.</summary>
        void Menu304LegacyText(string title)
        {
            var holder=V.Rect("Legacy304",menu304Page!=null?menu304Page:modalLayer,0,0,10,10);
            heading=V.Text(holder,"PageHeading",title=="상세"?detailTitle:MenuLabel(title),Theme.Font,32,Theme.Ink,0,0,10,10);
            subheading=V.Text(holder,"Subheading","",Theme.Font,20,Theme.Muted,0,0,10,10);
            statusText=V.Text(holder,"Status","",Theme.Font,18,Theme.Muted,0,0,10,10);
            holder.gameObject.SetActive(false);
            RefreshStatus();
        }

        // ------------------------------------------------------------------ selection
        Menu304Selection Menu304CaptureSelection()
        {
            var es=EventSystem.current;var sel=es!=null?es.currentSelectedGameObject:null;
            if(sel==null||!sel.activeInHierarchy)return null;
            bool inMenu=sel.transform.IsChildOf(modalLayer)||(menu304RailLayer!=null&&sel.transform.IsChildOf(menu304RailLayer));
            if(!inMenu)return null;
            return new Menu304Selection{Name=sel.name,FromRail=sel.GetComponentInParent<MenuRail304>()!=null};
        }
        /// <summary>True when something on the menu canvas (page, rail, map, dialog) holds a live selection.</summary>
        bool Menu304SelectionLive()
        {
            var es=EventSystem.current;var sel=es!=null?es.currentSelectedGameObject:null;
            if(sel==null||!sel.activeInHierarchy)return false;
            return canvasRect!=null&&sel.transform.IsChildOf(canvasRect);
        }
        GameObject Menu304Find(string name,bool railOnly)
        {
            if(string.IsNullOrEmpty(name))return null;
            IEnumerable<Selectable> pool=railOnly?(menu304Rail!=null?menu304Rail.GetComponentsInChildren<Selectable>():new Selectable[0])
                :modalLayer.GetComponentsInChildren<Selectable>().Concat(menu304RailLayer!=null?menu304RailLayer.GetComponentsInChildren<Selectable>():new Selectable[0]);
            foreach(var sel in pool)if(sel!=null&&sel.name==name&&sel.IsInteractable()&&sel.navigation.mode!=Navigation.Mode.None)return sel.gameObject;
            return null;
        }
        /// <summary>Initial selection (IMPLEMENTATION §4.2: every page states it). Same-page rebuild = the same row by name. Rail
        /// navigation (Menu304OpenFromRail) with the focus on the rail = this page's rail tab. Otherwise the page's default
        /// (일시정지 Resume, 설정 first live row, 조작 category tab, or what the builder set), then the first live selectable of the
        /// content, then the rail tab. A rail tab selected on the page before (e.g. 차패, whose default is its tab) is NOT carried
        /// over by a non-rail open: that left the dab on the old tab (options.png / controls.png, QA #304-1).</summary>
        void Menu304AfterBuild(string page,string previous,Menu304Selection reselect)
        {
            Menu304Shown(page);
            GameObject pick=null;
            bool viaRail=menu304ViaRail;
            if(reselect!=null&&previous==page)pick=Menu304Find(reselect.Name,false);
            else if(viaRail&&reselect!=null&&reselect.FromRail&&menu304Rail!=null)
            {
                var railTab=menu304Rail.TabButton(page);
                if(railTab!=null&&railTab.IsInteractable())pick=railTab.gameObject;
            }
            if(pick==null&&menu304DefaultSelection!=null&&menu304DefaultSelection.activeInHierarchy)pick=menu304DefaultSelection;
            // the builder selected something itself (content cells): keep it. The old page's objects are already inactive
            // (V.Clear deactivates before Destroy), so a live selection here belongs to the new page.
            if(pick==null&&Menu304SelectionLive()){menu304DefaultSelection=null;return;}
            if(pick==null&&contentRoot!=null)
            {
                var first=contentRoot.GetComponentsInChildren<Selectable>().FirstOrDefault(x=>x.IsInteractable()&&x.navigation.mode!=Navigation.Mode.None);
                if(first!=null)pick=first.gameObject;
            }
            if(pick==null&&menu304Rail!=null){var tab=menu304Rail.TabButton(page);if(tab!=null)pick=tab.gameObject;}
            if(pick!=null)FocusMark304.Select(pick);
            menu304DefaultSelection=null;
        }

        // ------------------------------------------------------------------ per frame (Q / E, modal, story [F], text scale)
        void Menu304Tick()
        {
            Menu304TrackModal();
            if(confirmationRoot!=null||Page.Length==0||Busy)return;
            if(menu304Rail!=null)
            {
                int dir=Menu304RailInput();
                if(dir!=0){var next=menu304Rail.Neighbor(dir);if(!string.IsNullOrEmpty(next)&&next!=Page){Menu304OpenFromRail(next);return;}}
            }
            if(Page=="상세")Menu304StoryTick();
            if(Page=="옵션"&&Mathf.Abs(Settings.Current.TextScale-menu304BuiltTextScale)>.001f)
            {
                // 본문 크기 changed: widths are measured at build time, so rebuild once the value settles
                if(menu304TextScaleDirtyAt<0f)menu304TextScaleDirtyAt=Time.unscaledTime;
                bool held=Mouse.current!=null&&Mouse.current.leftButton.isPressed;
                if(!held&&Time.unscaledTime-menu304TextScaleDirtyAt>.35f){suppressNextPageSound255=true;OpenPage("옵션");}
            }
        }
        static int Menu304RailInput()
        {
            var k=Keyboard.current;var g=Gamepad.current;
            if((k!=null&&k.qKey.wasPressedThisFrame)||(g!=null&&g.leftShoulder.wasPressedThisFrame))return -1;
            if((k!=null&&k.eKey.wasPressedThisFrame)||(g!=null&&g.rightShoulder.wasPressedThisFrame))return 1;
            return 0;
        }
        void Menu304TrackModal()
        {
            var group=menu304Page!=null?menu304Page.GetComponent<CanvasGroup>():null;
            if(confirmationRoot!=null)
            {
                if(group!=null&&group.interactable){FocusMark304.Suspend(group,true);menu304Suspended=true;}
                return;
            }
            if(menu304Suspended)
            {
                menu304Suspended=false;
                if(group!=null)FocusMark304.Suspend(group,false);
                var back=menu304LastPageSelection!=null&&menu304LastPageSelection.activeInHierarchy?menu304LastPageSelection:null;
                if(back==null&&menu304Rail!=null&&menu304Rail.TabButton(Page)!=null)back=menu304Rail.TabButton(Page).gameObject;
                if(back!=null)FocusMark304.Select(back);
            }
            var es=EventSystem.current;var sel=es!=null?es.currentSelectedGameObject:null;
            if(menu304Page!=null&&sel!=null&&sel.activeInHierarchy&&sel.transform.IsChildOf(menu304Page))menu304LastPageSelection=sel;
        }

        // ------------------------------------------------------------------ 일시정지 (pause.png)
        void BuildPause()
        {
            var s=V.Style(Theme);var cr=contentRoot;
            Menu304CssLabel(s,cr,"Heading","잠시 붓을 내려놓는다",UiType304.Display50,s.Paper,96,76);
            // primary: wet_m (56,170) h150 only while focused, 계속하기 44px at (152,214), filled Esc (372,222) -> hollow when focused
            var resume=V.FocusRow(s,cr,"Resume","계속하기",56,170,720,150,CloseMenu,new FocusRowSpec304
            {
                Role=UiType304.Headline44,LabelX=96,Underlay=StrokeClass304.WetM,UnderlayH=150,UnderlayX=0,
                Key="Esc",KeySmall=false,KeyX=316,UnderStroke=true,SoundTheme=Theme,
            });
            menu304DefaultSelection=resume.Button.gameObject;
            // 길잡이 목록 (y = 349 + 58i): 이름 x112, 정보 냄새 메타 x290, 단축키 빈 건반 x742. Buttons keep the rail's harness
            // names Tab_<page> (the pause page has no rail; the list is its navigation).
            // focus underlay = content-fitted (states.png 메뉴 항목): label / meta / key right + 24, so a meta-only row no longer
            // runs its wet_s to x1010 across the scene (pause_focus_return.png). 차패 builds its meta after the row: measured here.
            for(int i=0;i<Menu304PauseRows.Length;i++)
            {
                string page=Menu304PauseRows[i];
                string key=page=="소지품"?"I":page=="지도"?"M":null;
                bool virtues=page=="차패";
                var row=V.FocusRow(s,cr,"Tab_"+page,MenuLabel(page),72,349+58*i,720,58,()=>OpenPage(page),new FocusRowSpec304
                {
                    Role=UiType304.Label28,LabelX=40,Meta=virtues?null:Menu304PauseMeta(page),MetaX=218,
                    Key=key,KeyMode=FocusKeyMode304.Hollow,KeyX=670,ContentRight=virtues?Menu304VirtueRight(s):float.NaN,SoundTheme=Theme,
                });
                if(virtues)Menu304VirtueMeta(s,row);
            }
            V.Brush(s,cr,"PauseRule",StrokeClass304.Dry,s.Paper,100,712,700,26,.32f);
            V.FocusRow(s,cr,"ReturnTitle","타이틀로",72,737,720,58,
                ()=>Confirm("타이틀로 돌아갈까요?","현재 진행을 저장합니다.",()=>StartCoroutine(ReturnTitle())),new FocusRowSpec304
            {
                Role=UiType304.Label28,LabelX=40,Meta="진행을 저장하고 로비로 돌아간다",MetaX=218,SoundTheme=Theme,
            });
            Menu304PauseCommission(s,cr);
            Menu304PauseSaveLine(s,cr);
            Menu304PauseSlip(s,cr);
        }
        string Menu304PauseMeta(string page)
        {
            var ui=Session!=null&&Session.Progress!=null?Session.Progress.ui:null;
            switch(page)
            {
                case "소지품":
                {
                    int fragments=ui!=null&&ui.items!=null?ui.items.Sum(x=>x.count):0;
                    var equipment=Session!=null&&Session.EquipmentEnabled&&Session.Progress!=null?Session.Progress.equipment:null;
                    return (equipment!=null&&equipment.Owned!=null?"장비 "+equipment.Owned.Count+"  ":"")+"석경 조각 "+fragments;
                }
                case "술식 도감":
                    return "석경에 남은 술식 "+(ui!=null&&ui.knownSpellLetters!=null?ui.knownSpellLetters.Count:0)+" / "+WorldMacroCollectionCatalog.AllSpells.Count;
                case "지도":
                {
                    Menu304Location(out string realm,out string place);
                    if(place.Length==0&&Session!=null)place=Session.CheckpointDisplayName??"";
                    return realm.Length>0&&place.Length>0?realm+" · "+place:realm.Length>0?realm:place;
                }
                case "옵션":return "화면과 소리, 조작, 접근성";
                case "조작 안내":return "키보드와 마우스";
                default:return null;
            }
        }
        /// <summary>차패 row meta: 仁禮義智信 (Serif800 20; 새긴 덕 한지, 못 새긴 덕 비활성색) + "오덕 n / 5". The glyph line swaps to
        /// ink tones while the row sits on its paper underlay.</summary>
        void Menu304VirtueMeta(UiStyle304SO s,FocusRow304 row)
        {
            Menu304VirtueText(s,out string normal,out string focused,out int owned);
            float midY=row.Rect.sizeDelta.y*.5f;
            var glyphs=V.Label(s,row.Rect,"Virtues",normal,UiType304.Serif800_20,Color.white,218,0);
            V.Place(glyphs.rectTransform,218,midY-glyphs.rectTransform.sizeDelta.y*.5f);
            var glyphsFocused=V.Label(s,row.Rect,"VirtuesFocused",focused,UiType304.Serif800_20,Color.white,218,0);
            V.Place(glyphsFocused.rectTransform,218,midY-glyphsFocused.rectTransform.sizeDelta.y*.5f);
            glyphsFocused.gameObject.SetActive(false);
            row.Visual.HideWhenFocused.Add(glyphs.gameObject);row.Visual.ShowWhenFocused.Add(glyphsFocused.gameObject);
            float x=218+glyphs.rectTransform.sizeDelta.x+14;
            var count=V.Label(s,row.Rect,"VirtueCount","오덕 "+owned+" / "+Menu304Virtues.Length,UiType304.Meta20,s.Mist,x,0);
            V.Place(count.rectTransform,x,midY-count.rectTransform.sizeDelta.y*.5f);
            row.Visual.AddTint(count,s.Mist,s.Ash);
        }
        static readonly string[] Menu304Virtues={"仁","禮","義","智","信"};
        void Menu304VirtueText(UiStyle304SO s,out string normal,out string focused,out int owned)
        {
            var known=Session!=null&&Session.Progress!=null&&Session.Progress.ui!=null?Session.Progress.ui.knownVirtues:null;
            owned=0;var n=new System.Text.StringBuilder();var f=new System.Text.StringBuilder();
            string paper=ColorUtility.ToHtmlStringRGBA(s.Paper),off=ColorUtility.ToHtmlStringRGBA(s.Off),ink=ColorUtility.ToHtmlStringRGBA(s.Ink),faint=ColorUtility.ToHtmlStringRGBA(UiStyle304SO.A(s.Ink,.35f));
            for(int i=0;i<Menu304Virtues.Length;i++)
            {
                bool has=known!=null&&known.Contains(Menu304Virtues[i]);if(has)owned++;
                string gap=i>0?" ":"";
                n.Append(gap).Append("<color=#").Append(has?paper:off).Append('>').Append(Menu304Virtues[i]).Append("</color>");
                f.Append(gap).Append("<color=#").Append(has?ink:faint).Append('>').Append(Menu304Virtues[i]).Append("</color>");
            }
            normal=n.ToString();focused=f.ToString();
        }
        /// <summary>Right edge (row px) of the 차패 meta (仁禮義智信 + 오덕 n / 5) + 24: the row's content-fitted underlay end.</summary>
        float Menu304VirtueRight(UiStyle304SO s)
        {
            Menu304VirtueText(s,out string normal,out _,out int owned);
            return 218f+Menu304TextWidth(s,normal,UiType304.Serif800_20)+14f+Menu304TextWidth(s,"오덕 "+owned+" / "+Menu304Virtues.Length,UiType304.Meta20)+24f;
        }
        float Menu304TextWidth(UiStyle304SO s,string text,UiType304 role)
        {
            var probe=V.Label(s,contentRoot!=null?contentRoot:modalLayer,"WidthProbe",text,role,Color.white,0,0);
            float w=probe.rectTransform.sizeDelta.x;
            probe.gameObject.SetActive(false);Destroy(probe.gameObject);
            return w;
        }
        /// <summary>V.Label placed by a mockup CSS top: TMP puts the first ascender at the rect top while CSS centres the glyph box
        /// in the line box, so the rect goes to cssTop + role.CssTopOffset(font) (FOUNDATION_API §4). Without it the captures
        /// measured Display50 6 px low, Speaker60 10 px low and Prose28 7 px high against pause.png / dialogue.png.</summary>
        TMP_Text Menu304CssLabel(UiStyle304SO s,Transform parent,string name,string text,UiType304 role,Color color,float x,float cssTop,float width=0f,bool wrap=false)
        {
            var t=V.Label(s,parent,name,text,role,color,x,cssTop,width,0f,TextAlignmentOptions.TopLeft,wrap);
            V.Place(t.rectTransform,x,cssTop+s.Role(role).CssTopOffset(t.font));
            return t;
        }
        /// <summary>진행 중인 의뢰 (112,832): the first accepted, unreported commission. Title = CommissionSpec.Title, else
        /// "&lt;giver&gt;의 의뢰" (giver = Speaker, else the text's "X: " prefix), line = its first waiting page (WaitingLines, else
        /// WaitingText, else OfferText) without the speaker. Hidden when there is none.</summary>
        void Menu304PauseCommission(UiStyle304SO s,RectTransform cr)
        {
            var list=Session!=null&&Session.Content!=null?Session.Content.Commissions:null;
            if(list==null)return;
            foreach(var q in list)
            {
                if(q==null||!q.IsConfigured||!Session.CommissionAccepted(q.Id)||Session.CommissionReported(q.Id))continue;
                // #306 data first (Speaker / Title / WaitingLines, which the Dialogue306 migration fills and strips the "X: " prefix
                // for); the prefix split stays only as the fallback for unmigrated content
                Menu304SplitSpeaker(q.OfferText,out string prefixed,out _);
                string giver=!string.IsNullOrWhiteSpace(q.Speaker)?q.Speaker.Trim():prefixed;
                string title=!string.IsNullOrWhiteSpace(q.Title)?q.Title.Trim():giver.Length>0?giver+"의 의뢰":"의뢰";
                string waiting=q.WaitingLines!=null&&q.WaitingLines.Length>0&&!string.IsNullOrWhiteSpace(q.WaitingLines[0])?q.WaitingLines[0]
                    :string.IsNullOrEmpty(q.WaitingText)?q.OfferText:q.WaitingText;
                Menu304SplitSpeaker(waiting,out _,out string line);
                Menu304CssLabel(s,cr,"CommissionMeta","진행 중인 의뢰",UiType304.Meta20,s.Mist,112,832);
                Menu304CssLabel(s,cr,"CommissionTitle",title,UiType304.Title28,s.Paper,112,858);
                var summary=Menu304CssLabel(s,cr,"CommissionLine",(line??"").Replace('\n',' '),UiType304.Body22,s.Mist,112,900);
                if(summary.rectTransform.sizeDelta.x>700f){summary.rectTransform.sizeDelta=new Vector2(700f,summary.rectTransform.sizeDelta.y);summary.overflowMode=TextOverflowModes.Ellipsis;}
                return;
            }
        }
        /// <summary>저장 줄 y966 (D10): "여정 기록됨" then 부인 34 whose bottom sits on the text baseline (관지인 문법). A failed
        /// save shows no seal and no 방점: a cinnabar vertical dry stroke + "저장하지 못했다" in 주사 밝음 (the error toast grammar).
        /// #304 QA2: the state is t-meta paper as in pause.png (was Serif700_22; the failure is MetaBold20, --cin-lift = 20px
        /// bold+), every part sits on the CSS top 966 + CssTopOffset (the plain tops put them 2~4 px low), and with the seals off
        /// (V.Seal = null) no seal room is reserved: the restart line keeps the list's meta column x290.</summary>
        void Menu304PauseSaveLine(UiStyle304SO s,RectTransform cr)
        {
            if(Session==null)return;
            bool failed=!string.IsNullOrEmpty(Session.SaveError);
            const float top=966f;
            var state=Menu304CssLabel(s,cr,"SaveState",failed?"저장하지 못했다":"여정 기록됨",failed?UiType304.MetaBold20:UiType304.Meta20,failed?s.CinnabarLift:s.Paper,112,top);
            var sr=state.rectTransform;
            float right=112+sr.sizeDelta.x;
            if(failed)MenuEdgeStroke304.Draw(s,cr,"SaveFailEdge",100f,-sr.anchoredPosition.y+sr.sizeDelta.y*.5f,48f,10f,.95f);
            else
            {
                float ascent=state.font!=null&&state.font.faceInfo.pointSize>0?state.font.faceInfo.ascentLine/state.font.faceInfo.pointSize*state.fontSize:state.fontSize*.88f;
                if(V.Seal(s,cr,right+10,-sr.anchoredPosition.y+ascent-32,34)!=null)right+=10+34;
            }
            float x=Mathf.Max(290f,right+24f);
            var restart=Menu304CssLabel(s,cr,"SaveRestart",(Session.CheckpointDisplayName??"")+"에서 재시작",UiType304.Meta20,s.Mist,x,top);
            x+=restart.rectTransform.sizeDelta.x+50f;
            long coins=Session.Progress!=null?Session.Progress.ledger.currency:0;
            var coinName=Menu304CssLabel(s,cr,"SaveCoinName","조선통보",UiType304.Meta20,s.Mist,x,top);
            Menu304CssLabel(s,cr,"SaveCoins",coins.ToString("N0"),UiType304.MetaBold20,s.Paper,x+coinName.rectTransform.sizeDelta.x+8,top);
        }
        /// <summary>늘어뜨린 쪽지 (1690,-30) 128x500: 권역 Region72 먹 세로, 지명 Serif700 22 재 세로, 부인 44, and the D11 제첨 테
        /// (1.5 px ink line 7 px inside the edge, α .6, crossing at the corners).
        /// #304 QA2: 권역 / 지명 sit on their CSS tops (pause.html 90 / 262 in slip px) + CssTopOffset (Region72 line-height 1.02
        /// put 청림 17 px low, after2/pause.png). With the seals off (V.Seal = null) the slip is cut to its text: the 부인's
        /// 44 px + gaps no longer leave ~130 px of blank paper under the 지명; the bottom margin under the last glyph stays the
        /// mockup's margin under the seal (38) and the slip keeps at least 1:3 (384).</summary>
        void Menu304PauseSlip(UiStyle304SO s,RectTransform page)
        {
            Menu304Location(out string realm,out string place);
            if(realm.Length==0){realm=place;place="";}
            if(realm.Length==0)return;
            const float slipW=128f,slipH=500f,regionTop=90f,placeTop=262f,sealMax=436f,gap=24f,bottomMargin=38f,minH=384f;
            var slipImage=V.SpriteImage(page,"RegionSlip",s.Sprites.SheetSlip,s.Sprites.SheetSlip!=null?Color.white:s.Sheet,1690,-30,slipW,slipH);
            var slip=slipImage.rectTransform;
            var regionRole=s.Role(UiType304.Region72);
            var region=V.Label(s,slip,"Region",UiText304.Vertical(realm),regionRole,s.Ink,22,regionTop,84,0,TextAlignmentOptions.Top);
            V.Place(region.rectTransform,22,regionTop+regionRole.CssTopOffset(region.font));
            float textBottom=-region.rectTransform.anchoredPosition.y+region.rectTransform.sizeDelta.y;
            float sealY=418f;
            if(place.Length>0)
            {
                // the 지명 column ends 24 px above the 부인, whose lowest slot is 436 (slip bottom 500): a longer name first loses
                // its word gap, then this one label shrinks to fit (kept out of the text-scale rule, which runs after the build)
                var placeRole=s.Role(UiType304.Serif700_22);
                float room=sealMax-gap-placeTop,k=Mathf.Max(1f,Settings!=null?Settings.Current.TextScale:1f);
                var local=V.Label(s,slip,"Place",UiText304.Vertical(place),placeRole,s.Ash,52,placeTop,24,0,TextAlignmentOptions.Top);
                V.Place(local.rectTransform,52,placeTop+placeRole.CssTopOffset(local.font));
                Menu304FitSlipColumn(local);   // the scaled glyph (text scale 1.25: ~30 px) must not overflow the 24 px column
                float h=local.rectTransform.sizeDelta.y;
                if(h*k>room&&place.IndexOf(' ')>=0)
                {
                    local.text=UiText304.Vertical(place.Replace(" ",""));
                    h=Mathf.Ceil(local.GetPreferredValues(local.text).y);local.rectTransform.sizeDelta=new Vector2(24f,h);Menu304FitSlipColumn(local);
                }
                if(h*k>room)
                {
                    local.gameObject.AddComponent<UiTextNoScale304>();
                    local.fontSize=Mathf.Max(14f,local.fontSize*room/h);
                    h=Mathf.Ceil(local.GetPreferredValues(local.text).y);local.rectTransform.sizeDelta=new Vector2(24f,h);Menu304FitSlipColumn(local);
                    k=1f;
                }
                sealY=Mathf.Clamp(placeTop+h*k+gap,300f,sealMax);
                textBottom=-local.rectTransform.anchoredPosition.y+h*k;
            }
            float height=slipH;
            if(V.Seal(s,slip,42,sealY,44)==null)
            {
                height=Mathf.Clamp(Mathf.Ceil(textBottom+bottomMargin),minH,slipH);
                slip.sizeDelta=new Vector2(slipW,height);
            }
            // D11 제첨 테 on the final height (drawn after the texts; the rims never overlap them)
            const float inset=7f,rim=6f,over=4f;
            V.Brush(s,slip,"RimLeft",StrokeClass304.Line,s.Ink,inset-(height+2*over)*.5f,height*.5f-rim*.5f,height+2*over,rim,.6f,90f);
            V.Brush(s,slip,"RimRight",StrokeClass304.Line,s.Ink,slipW-inset-(height+2*over)*.5f,height*.5f-rim*.5f,height+2*over,rim,.6f,90f);
            V.Brush(s,slip,"RimBottom",StrokeClass304.Line,s.Ink,inset-over,height-inset-rim*.5f,slipW-2*inset+2*over,rim,.6f);
        }
        void Menu304Location(out string realm,out string place)
        {
            realm="";place="";
            var catalog=MapData!=null?MapData.Locations:null;
            var body=Session!=null&&Session.Walker!=null?Session.Walker.Body:null;
            if(catalog==null||body==null)return;
            Vector3 p=body.transform.position;
            var r=catalog.RealmAt(p);if(r!=null)realm=r.Name??"";
            var here=catalog.Resolve(p,null);if(here!=null&&here.Priority>0)place=here.Name??"";
            place=Menu304LocalPlace(realm,place);
        }
        /// <summary>Local place without its realm prefix: the catalog names places "청림 벌목마을", and the pause meta / slip already
        /// show the realm ("청림 · 청림 벌목마을", and a 7-line vertical 지명 that ran under the 부인 on the slip, pause.png).</summary>
        static string Menu304LocalPlace(string realm,string place)
        {
            place=(place??"").Trim();
            if(string.IsNullOrEmpty(realm)||place.Length<=realm.Length+1||!place.StartsWith(realm,System.StringComparison.Ordinal))return place;
            char next=place[realm.Length];
            if(next!=' '&&next!='·')return place;   // "청림사" is its own name; only "청림 벌목마을" / "청림·…" carry a prefix
            string rest=place.Substring(realm.Length).TrimStart(' ','·');
            return rest.Length>0?rest:place;
        }

        // ------------------------------------------------------------------ 조작 안내 (DESIGN §7.9)
        void BuildControls()
        {
            var s=V.Style(Theme);var cr=contentRoot;
            // #300: the run key toggles when the active locomotion profile says so
            var motor=Session!=null&&Session.Walker!=null?Session.Walker.Motor:null;
            bool runToggle=motor!=null&&motor.SprintToggles;
            string runKey=runToggle?"Ctrl":"Ctrl + 이동",runAction=runToggle?"달리기 켜기·끄기 (멈추면 걷기)":"달리기";
            string[,] rows;
            if(menu304ControlsTab=="작도")rows=new[,]{{"Q + 마우스 좌클릭","글씨 그리기 · Q를 놓으면 시전"},{"마우스 좌클릭 유지","비작도 상태에서 먹 갈무리"}};
            else if(menu304ControlsTab=="메뉴")rows=new[,]{{"M / I / Esc","지도 / 소지품 / 일시정지·뒤로"},{"Q / E","메뉴 탭 넘기기"}};
            else rows=new[,]{{"W · A · S · D","걷기 / 탑승 중 가속·조향"},{runKey,runAction},{"Space","지상 점프 / 탑승 중 제동"},{"C / X","웅크리기 전환 / 바닥 착석·일어나기"},
                {"마우스","시점 이동"},{"왼쪽 Shift / Tab","회피 (웅크림 중 구르기) / 대상 락온"},{"F","조사, 대화, 석경 파편 획득"},{"E / V","마법가마 탑승·하차 / 탑승 시점 전환"},
                {"G","오행부로 자동차 호출 · 하차 후 30m 자동 회수"}};
            var tab=Menu304CategoryTabs(s,"ControlsTab_",Menu304ControlTabs,menu304ControlsTab,t=>{menu304ControlsTab=t;OpenPage("조작 안내");});
            menu304DefaultSelection=tab!=null?tab.gameObject:null;
            for(int i=0;i<rows.GetLength(0);i++)
            {
                float y=180+64*i;
                Menu304KeyGroup(s,cr,"Key_"+i,rows[i,0],700,y+8);
                V.Label(s,cr,"Action_"+i,rows[i,1],UiType304.Label24,s.Paper,740,y+13);
                V.Brush(s,cr,"RowRule_"+i,StrokeClass304.Dry,s.Paper,392,y+54,1060,14,.2f);
            }
        }
        /// <summary>A binding string split into keycaps (DESIGN §7.9 "건반 문자열을 V.Keycap 여러 개로"): " / " = alternatives,
        /// " + " = chord, " · " = separate keys; 마우스 좌클릭 / 우클릭 / 휠 / 마우스 = Amanz mouse glyphs (MenuInputIcons304, text
        /// keycap fallback), Korean words (이동, 유지) = meta. Right-aligned at `right`. Hollow keys: a reference table, not an action.</summary>
        RectTransform Menu304KeyGroup(UiStyle304SO s,RectTransform parent,string name,string keys,float right,float y)
        {
            var group=V.Rect(name,parent,0,y,10,s.Keycap.Height);
            float x=0f,h=s.Keycap.Height;
            foreach(var token in Menu304KeyTokens(keys))
            {
                if(token.Kind==0)
                {
                    var cap=V.Keycap(s,group,token.Text,false,false,false,x,0);x+=cap.sizeDelta.x+s.Keycap.KeyGap;
                }
                else if(token.Kind==1)
                {
                    var sprite=Menu304InputIcons()!=null?menu304Icons.For(token.Text):null;
                    if(sprite!=null){var img=V.SpriteImage(group,"Mouse_"+token.Text,sprite,s.Paper,x,0,h,h);img.preserveAspect=true;x+=h+s.Keycap.KeyGap;}
                    else {var cap=V.Keycap(s,group,token.Text,false,false,false,x,0);x+=cap.sizeDelta.x+s.Keycap.KeyGap;}
                }
                else
                {
                    var l=V.Label(s,group,token.Kind==2?"Word":"Sep",token.Text,UiType304.Meta20,s.Mist,x,0);
                    V.Place(l.rectTransform,x+(token.Kind==3?2:0),(h-l.rectTransform.sizeDelta.y)*.5f);
                    x+=l.rectTransform.sizeDelta.x+(token.Kind==3?12:s.Keycap.KeyGap);
                }
            }
            float w=Mathf.Max(1f,x-s.Keycap.KeyGap);
            group.sizeDelta=new Vector2(w,h);V.Place(group,right-w,y);
            return group;
        }
        struct Menu304KeyToken{public int Kind;public string Text;}   // 0 key, 1 mouse, 2 word, 3 separator
        static List<Menu304KeyToken> Menu304KeyTokens(string keys)
        {
            var list=new List<Menu304KeyToken>();
            if(string.IsNullOrEmpty(keys))return list;
            var alternatives=keys.Split(new[]{" / "},System.StringSplitOptions.RemoveEmptyEntries);
            for(int a=0;a<alternatives.Length;a++)
            {
                if(a>0)list.Add(new Menu304KeyToken{Kind=3,Text="/"});
                var chords=alternatives[a].Split(new[]{" + "},System.StringSplitOptions.RemoveEmptyEntries);
                for(int c=0;c<chords.Length;c++)
                {
                    if(c>0)list.Add(new Menu304KeyToken{Kind=3,Text="+"});
                    foreach(var part in chords[c].Split(new[]{" · "},System.StringSplitOptions.RemoveEmptyEntries))Menu304AddKeyToken(list,part.Trim());
                }
            }
            return list;
        }
        static void Menu304AddKeyToken(List<Menu304KeyToken> list,string part)
        {
            if(part.Length==0)return;
            if(part=="마우스"){list.Add(new Menu304KeyToken{Kind=1,Text="마우스"});return;}
            string[] words=part.Split(' ');
            int start=words[0]=="마우스"&&words.Length>1?1:0;
            string first=words[start];
            if(first=="좌클릭"||first=="우클릭"||first=="휠")
            {
                list.Add(new Menu304KeyToken{Kind=1,Text=first});
                for(int i=start+1;i<words.Length;i++)list.Add(new Menu304KeyToken{Kind=2,Text=words[i]});
                return;
            }
            bool hangulOnly=true;foreach(char ch in part)if(!(ch>='가'&&ch<='힣')&&ch!=' ')hangulOnly=false;
            list.Add(new Menu304KeyToken{Kind=hangulOnly?2:0,Text=part});
        }
        MenuInputIcons304 Menu304InputIcons()
        {
            if(!menu304IconsLoaded){menu304IconsLoaded=true;menu304Icons=Resources.Load<MenuInputIcons304>(MenuInputIcons304.ResourcePath);}
            return menu304Icons;
        }
        /// <summary>Widens a vertical slip column (base 24 px) to its glyph width and keeps it centred on the same axis.</summary>
        static void Menu304FitSlipColumn(TMP_Text t)
        {
            var r=t.rectTransform; float w=Mathf.Ceil(t.GetPreferredValues(t.text).x);
            if(w<=r.sizeDelta.x+.5f)return;
            float dx=(w-r.sizeDelta.x)*.5f; r.sizeDelta=new Vector2(w,r.sizeDelta.y); r.anchoredPosition-=new Vector2(dx,0f);
        }
    }
}
