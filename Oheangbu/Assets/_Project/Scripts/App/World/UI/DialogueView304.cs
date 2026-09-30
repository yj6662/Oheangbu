using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#306 §2-3 대화 화면 placement (1920x1080 page px, top-left origin). Colours, roles and motion come from UiStyle304SO;
    /// these are the PLAN table's numbers. All TEST.</summary>
    [Serializable]
    public sealed class DialogueViewSpec304
    {
        [Header("backing")]
        [Tooltip("ramp_bottom x Veil over the lowest px")] public float BackingHeight=380f;
        public float BackingAlpha=.6f;
        [Tooltip("D24 자막 받침 진하게 (no user setting yet): ink wet_s under the line")] public bool BoldBacking;
        public float BoldAlpha=.93f, BoldPadX=56f, BoldPadY=14f;
        [Header("subtitle")]
        public float CentreX=960f;
        [Tooltip("CSS top of the speaker name (Serif700_24 Mist)")] public float SpeakerTop=872f;
        [Tooltip("the commission header (Meta20 Mist) sits this far above the speaker")] public float HeaderGap=30f;
        [Tooltip("speaker line box -> first subtitle line")] public float LineGap=10f;
        [Tooltip("Prose28 Paper, centred")] public float LineWidth=1100f;
        [Min(1)] public int LinesPerPage=2;
        [Tooltip("last glyph -> 방점 -> small [F]")] public float KeyGap=14f, DabGap=6f;
        public Vector2 DabSize=new Vector2(28,21);
        [Header("talk menu")]
        public float MenuWidth=440f, RowHeight=60f;
        [Tooltip("menu bottom above the speaker (or header)")] public float MenuGap=26f;
        public float MenuPadX=28f, MenuPadY=16f, MenuBandAlpha=.8f;
        public float RowLabelX=72f, FocusUnderlayH=64f, KeyRight=22f;
        [Tooltip("row label when a service carries none, in DialogueServiceKind306 order")]
        public string[] KindLabels={"이야기","거래","손질","쉬기","정비","맡는다","거절한다","보고한다","떠나기"};

        public string LabelOf(DialogueService306 s)
        {
            if(s==null)return "";
            if(!string.IsNullOrEmpty(s.Label))return s.Label;
            int i=(int)s.Kind;
            return KindLabels!=null&&i>=0&&i<KindLabels.Length&&!string.IsNullOrEmpty(KindLabels[i])?KindLabels[i]:s.Kind.ToString();
        }
    }

    /// <summary>What the view needs from its owner (PlaytestUiRoot): the "상세" page, pause, HUD and the service windows.</summary>
    internal interface IDialogueHost304
    {
        UiStyle304SO DialogueStyle {get;}
        PlaytestUiThemeSO DialogueTheme {get;}
        DialogueViewSpec304 DialogueSpec {get;}
        /// <summary>Opens "상세" (Pause.Begin + save + HUD off when nothing was open); it calls BuildPage. false = refused.</summary>
        bool DialogueOpen();
        /// <summary>Rebuilds "상세" in place, from a service window too (no pause change); it calls BuildPage.</summary>
        void DialogueRebuild();
        /// <summary>Closes the page (FinishClose: Pause.End, HUD on); it calls Detach and invokes the Closed after the page is gone.</summary>
        void DialogueClose();
        /// <summary>Opens the window of a Trade / Upgrade / Maintain row; true = that window page shows now.</summary>
        bool DialogueOpenService(DialogueService306 service);
        /// <summary>A window page (not "상세") is showing: the session opened it inside Chosen.</summary>
        bool DialogueServiceShowing {get;}
        void DialogueSound(string cue,float gain);
    }

    /// <summary>#306 §2-3 THE dialogue surface (IDialogueSurface306, semantics in DialogueRequest306.cs), Elden Ring grammar: no
    /// portrait, no camera cut, HUD hidden, bottom-centre speaker + subtitle, then a small talk menu. Built as "StoryBand304"
    /// (harness name) on the "상세" page: ramp_bottom Veil α.6 over the lowest 380 px (InkReveal Bleed; + ink wet_s α.93 under
    /// the line with BoldBacking), header Meta20 Mist, speaker Serif700_24 Mist centred at top 872, the line Prose28 Paper
    /// centred in 1100 with TMP Page overflow (LinesPerPage per page, long lines split) inside the "Done" button, a 방점 and a
    /// small filled [F] after the last glyph (no 다음 / 확인 text). F / Enter / click on the line turns the page; after the last
    /// page the menu (TalkMenu304, Leave appended) or the close. Menu: W/S, arrows, pad or mouse move the focus, F / Enter /
    /// click pick, Esc / pad B = Leave. Entrances: UiTween304.BleedIn (page, new content, menu), page turns PromptInMs; the host
    /// fades the band out (the reverse) when it closes. The [F] that opened / rebuilt the surface must be released first.</summary>
    public sealed class DialogueView304 : IDialogueSurface306
    {
        enum Mode { Lines, Menu, Service }
        readonly IDialogueHost304 host;
        readonly List<DialogueService306> entries=new List<DialogueService306>();
        readonly UiTweenSlot304 turn=new UiTweenSlot304();
        DialogueRequest306 request;
        Mode mode;
        int line,focus,generation,builtFrame=-1;
        bool fHeld,contentChanged,menuShown;
        RectTransform page,done,key,dab;
        FocusRow304[] rows;
        CanvasGroup lineGroup;
        TMP_Text body;
        Image bold;
        float lineTop,lineH,bodyY;

        internal DialogueView304(IDialogueHost304 host){this.host=host;}

        public bool IsOpen=>request!=null;
        /// <summary>A Trade / Upgrade / Maintain window opened from the menu is showing (its close returns to the menu).</summary>
        public bool InService=>request!=null&&mode==Mode.Service;
        public bool MenuShown=>request!=null&&mode==Mode.Menu;
        public DialogueRequest306 Current=>request;
        /// <summary>The live "StoryBand304" root (null while closed or while a service window shows).</summary>
        public RectTransform Root=>page;

        public bool Show(DialogueRequest306 r)
        {
            if(r==null)return false;
            bool wasOpen=request!=null;
            if(Count(r.Lines)==0&&!HasServices(r))
            {
                if(!wasOpen)return false;
                request=r;generation++;Close();return true;   // an empty follow-up ends the conversation
            }
            int keep=focus;request=r;generation++;Load(r);
            if(wasOpen)focus=keep;   // a follow-up keeps the picked row focused when its menu comes back (clamped at build)
            if(wasOpen){host.DialogueRebuild();return true;}
            if(host.DialogueOpen()&&request==r)return true;
            request=null;entries.Clear();return false;
        }

        public void Close()
        {
            if(request==null)return;
            if(mode==Mode.Service)mode=Mode.Menu;   // closing from a window ends the conversation, it does not return to the menu
            host.DialogueClose();
        }

        /// <summary>Host: forget the conversation; returns the showing request's Closed (the host runs it after the page is gone).</summary>
        internal Action Detach()
        {
            var r=request;if(r==null)return null;
            request=null;generation++;mode=Mode.Lines;entries.Clear();turn.Cancel();
            page=done=key=dab=null;lineGroup=null;body=null;bold=null;rows=null;
            return r.Closed;
        }

        /// <summary>Host: the service window closed (FinishClose) -> back to the talk menu, same row focused.</summary>
        internal void ReturnFromService(){if(request==null)return;mode=Mode.Menu;host.DialogueRebuild();}

        static int Count(string[] a)=>a!=null?a.Length:0;
        static bool HasServices(DialogueRequest306 r){if(r.Services!=null)foreach(var s in r.Services)if(s!=null&&s.Kind!=DialogueServiceKind306.Leave)return true;return false;}

        void Load(DialogueRequest306 r)
        {
            entries.Clear();DialogueService306 leave=null;
            if(r.Services!=null)foreach(var s in r.Services)
            {
                if(s==null)continue;
                if(s.Kind==DialogueServiceKind306.Leave){if(leave==null)leave=s;continue;}
                entries.Add(s);
            }
            if(entries.Count>0)entries.Add(leave??new DialogueService306{Kind=DialogueServiceKind306.Leave});   // the view appends Leave
            line=0;focus=0;menuShown=false;contentChanged=true;
            mode=Count(r.Lines)>0?Mode.Lines:Mode.Menu;
        }

        string LineAt(int i){var a=request.Lines;return a!=null&&i>=0&&i<a.Length?(a[i]??"").Trim():"";}

        // ------------------------------------------------------------------ build
        /// <summary>Host (OpenPage / DialogueRebuild): builds "StoryBand304" for the current state under `parent`. fresh = the page
        /// appears (bleed the whole band); otherwise only new content / a first menu bleeds in.</summary>
        internal RectTransform BuildPage(RectTransform parent,bool fresh)
        {
            var s=host.DialogueStyle;var k=host.DialogueSpec;var r=request;
            turn.Cancel();done=key=dab=null;bold=null;body=null;lineGroup=null;rows=null;
            page=V.Page304(parent,"StoryBand304");
            page.anchorMin=page.anchorMax=page.pivot=new Vector2(.5f,0f);page.anchoredPosition=Vector2.zero;   // hugs the bottom edge
            builtFrame=Time.frameCount;
            fHeld=Keyboard.current!=null&&Keyboard.current.fKey.isPressed;   // the [F] that opened / turned it must be released first
            if(r==null)return page;
            // ramp_bottom (alpha 1 at the bottom) x Veil on the shared InkReveal material, wide enough for 32:9
            if(s.Sprites.RampBottom!=null)
            {
                var ramp=V.SpriteImage(page,"RampBottom",s.Sprites.RampBottom,UiStyle304SO.A(s.Veil,k.BackingAlpha),-V.VeilLead,UiPageFit304.Height-k.BackingHeight,UiPageFit304.Width+2*V.VeilLead,k.BackingHeight);
                InkRevealEffect.On(ramp,s,InkRevealMode.Bleed,1f);
            }
            var text=V.Rect("DialogueText",page,0,0,UiPageFit304.Width,UiPageFit304.Height);
            var textGroup=text.gameObject.AddComponent<CanvasGroup>();
            if(!string.IsNullOrEmpty(r.Header))Centred(s,text,"Header",r.Header.Trim(),UiType304.Meta20,s.Mist,k.CentreX,k.SpeakerTop-k.HeaderGap);
            var speakerRole=s.Role(UiType304.Serif700_24);float speakerSize=speakerRole.Size*UiText304.TextScale;
            if(!string.IsNullOrEmpty(r.Speaker)){var name=Centred(s,text,"Speaker",r.Speaker.Trim(),UiType304.Serif700_24,s.Mist,k.CentreX,k.SpeakerTop);speakerSize=name.fontSize;}
            lineTop=k.SpeakerTop+Mathf.Ceil(speakerRole.LineHeight*speakerSize)+k.LineGap;

            bool menu=mode==Mode.Menu;
            var lineRoot=V.Rect("Line",text,0,0,UiPageFit304.Width,UiPageFit304.Height);
            lineGroup=lineRoot.gameObject.AddComponent<CanvasGroup>();
            if(k.BoldBacking)bold=V.Brush(s,lineRoot,"SubtitleUnderlay",StrokeClass304.WetS,s.Ink,0,0,10,10,k.BoldAlpha);
            float x=k.CentreX-k.LineWidth*.5f;
            string shown=menu?LineAt(Count(r.Lines)-1):LineAt(line);
            if(!menu||shown.Length>0)
            {
                Transform holder=lineRoot;
                if(!menu)
                {
                    // "Done" (harness name) = the line itself: click / Enter / pad A turn the page; its one 방점 sits before the [F]
                    done=V.Rect("Done",lineRoot,x,lineTop,k.LineWidth,10);holder=done;
                    var hit=V.Image(done,new Color(0f,0f,0f,0f),null,true);hit.canvasRenderer.cullTransparentMesh=true;
                    var button=done.gameObject.AddComponent<Button>();button.transition=Selectable.Transition.None;button.targetGraphic=hit;
                    button.onClick.AddListener(Advance);
                }
                body=V.Label(s,holder,"Body",shown,UiType304.Prose28,s.Paper,menu?x:0,0,k.LineWidth,0,TextAlignmentOptions.Top,true);
                var role=s.Role(UiType304.Prose28);
                lineH=role.LineHeight*body.fontSize;
                float pageH=Mathf.Ceil(lineH*Mathf.Max(1,k.LinesPerPage))+2f;
                body.rectTransform.sizeDelta=new Vector2(k.LineWidth,pageH);
                body.overflowMode=TextOverflowModes.Page;body.pageToDisplay=1;
                bodyY=lineTop+role.CssTopOffset(body.font);
                if(menu)
                {
                    V.Place(body.rectTransform,x,bodyY);
                    body.ForceMeshUpdate();body.pageToDisplay=Mathf.Max(1,body.textInfo.pageCount);   // the last page stays under the menu
                }
                else
                {
                    V.Place(done,x,bodyY);done.sizeDelta=new Vector2(k.LineWidth,pageH);V.Place(body.rectTransform,0,0);
                    key=V.Keycap(s,done,"F",true,false,true);
                    var ds=k.DabSize.x>0f?k.DabSize:s.DabSize;
                    dab=V.DabAnchor(done,0,0,ds.x,ds.y,s);
                    done.gameObject.AddComponent<FocusVisual304>().Bind(s,null,null,s.Paper,s.Paper,dab);
                    FocusMark304.Select(done.gameObject);
                }
                Layout();
            }
            else if(bold!=null){UnityEngine.Object.Destroy(bold.gameObject);bold=null;}

            if(menu)
            {
                float bottom=(string.IsNullOrEmpty(r.Header)?k.SpeakerTop:k.SpeakerTop-k.HeaderGap)-k.MenuGap;
                rows=TalkMenu304.Build(s,k,text,entries,bottom,Pick,host.DialogueTheme,out var menuRoot);
                int pick=-1;
                for(int i=0;i<rows.Length&&pick<0;i++){int j=(Mathf.Clamp(focus,0,rows.Length-1)+i)%rows.Length;if(rows[j].Button.IsInteractable())pick=j;}
                if(pick>=0)FocusMark304.Select(rows[pick].Button.gameObject);
                if(!menuShown&&!fresh){var g=menuRoot.GetComponent<CanvasGroup>();UiTween304.BleedIn(g,s,contentChanged?1:0,UiTween304.Token(g)).Forget();}
                menuShown=true;
            }
            if(fresh){var g=page.GetComponent<CanvasGroup>();UiTween304.BleedIn(g,s,0,UiTween304.Token(g)).Forget();}
            else if(contentChanged)UiTween304.BleedIn(textGroup,s,0,UiTween304.Token(textGroup)).Forget();
            contentChanged=false;
            return page;
        }

        /// <summary>Label with its preferred size, centred on cx, CSS top (+ the role's half-leading like Menu304CssLabel).</summary>
        static TMP_Text Centred(UiStyle304SO s,Transform parent,string name,string text,UiType304 role,Color color,float cx,float cssTop)
        {
            var t=V.Label(s,parent,name,text,role,color,0,0);
            V.Place(t.rectTransform,Mathf.Round(cx-t.rectTransform.sizeDelta.x*.5f),cssTop+s.Role(role).CssTopOffset(t.font));
            return t;
        }

        /// <summary>After a text / page change: [F] + 방점 after the last glyph of the shown page, the bold underlay around it.</summary>
        void Layout()
        {
            if(body==null)return;
            var k=host.DialogueSpec;
            body.ForceMeshUpdate();var info=body.textInfo;
            int pages=Mathf.Max(1,info.pageCount),p=Mathf.Clamp(body.pageToDisplay,1,pages)-1;
            int first=info.pageCount>0?info.pageInfo[p].firstCharacterIndex:0,last=info.pageCount>0?info.pageInfo[p].lastCharacterIndex:info.characterCount-1;
            float minX=float.MaxValue,maxX=float.MinValue,endX=k.LineWidth*.5f,endMid=-lineH*.5f;int lo=int.MaxValue,hi=-1;
            for(int i=Mathf.Max(0,first);i<=last&&i<info.characterCount;i++)
            {
                var c=info.characterInfo[i];if(!c.isVisible)continue;
                minX=Mathf.Min(minX,c.bottomLeft.x);maxX=Mathf.Max(maxX,c.topRight.x);
                lo=Mathf.Min(lo,c.lineNumber);hi=Mathf.Max(hi,c.lineNumber);
                endX=c.topRight.x;endMid=(c.ascender+c.descender)*.5f;   // the glyph's own line box (page-local, alignment applied)
            }
            if(hi<0){minX=maxX=endX;}
            if(key!=null)
            {
                Vector2 ks=key.sizeDelta,dsz=dab!=null?dab.sizeDelta:Vector2.zero;
                float dx=endX+k.KeyGap,kx=dab!=null?dx+dsz.x+k.DabGap:dx,ky=-endMid-ks.y*.5f;
                if(dab!=null)V.Place(dab,dx,ky+(ks.y-dsz.y)*.5f);
                V.Place(key,kx,ky);maxX=Mathf.Max(maxX,kx+ks.x);
            }
            if(bold!=null)
            {
                int lines=hi>=lo?hi-lo+1:1;
                float bx=k.CentreX-k.LineWidth*.5f+minX-k.BoldPadX,bw=maxX-minX+2f*k.BoldPadX,by=bodyY-k.BoldPadY,bh=lines*lineH+2f*k.BoldPadY;
                var br=bold.rectTransform;br.sizeDelta=new Vector2(bw,bh);br.anchoredPosition=new Vector2(bx+bw*.5f,-(by+bh*.5f));
                var spec=host.DialogueStyle.Stroke(StrokeClass304.WetS);
                if(bold.type==Image.Type.Sliced)bold.pixelsPerUnitMultiplier=Mathf.Max(.01f,spec.NativeH/Mathf.Max(1f,bh));
            }
        }

        void TurnFade()
        {
            if(lineGroup==null)return;var s=host.DialogueStyle;
            lineGroup.alpha=0f;
            UiTween304.Alpha(lineGroup,1f,s.Motion.Sec(s.Motion.PromptInMs,UiTween304.ReducedMotion),s.Motion.Stroke,turn.Restart(lineGroup)).Forget();
        }

        // ------------------------------------------------------------------ input
        /// <summary>Host, every frame while "상세" shows this conversation: F turns / picks, pad B leaves (Esc arrives through Back).</summary>
        internal void Tick()
        {
            if(request==null||mode==Mode.Service)return;
            KeepSelection();
            var g=Gamepad.current;
            if(g!=null&&g.buttonEast.wasPressedThisFrame){Close();return;}
            var k=Keyboard.current;if(k==null)return;
            if(fHeld){if(!k.fKey.isPressed)fHeld=false;return;}
            if(!k.fKey.wasPressedThisFrame||Time.frameCount<=builtFrame)return;
            if(mode==Mode.Lines){Advance();return;}
            var es=EventSystem.current;var sel=es!=null?es.currentSelectedGameObject:null;
            var b=sel!=null?sel.GetComponent<Button>():null;
            if(b!=null&&b.IsInteractable()&&page!=null&&sel.transform.IsChildOf(page)&&sel.name.StartsWith("Choice_",StringComparison.Ordinal))b.onClick.Invoke();
        }

        /// <summary>The focus never drops (a click on empty page area deselects): the row the pointer / W·S / pad left last, or the
        /// line's "Done", is selected again, so F / Enter / navigation keep working. Tracks the focused row for the way back.</summary>
        void KeepSelection()
        {
            var es=EventSystem.current;if(es==null||page==null)return;
            var sel=es.currentSelectedGameObject;bool lost=sel==null||!sel.activeInHierarchy;
            if(mode==Mode.Lines){if(lost&&done!=null)FocusMark304.Select(done.gameObject);return;}
            if(rows==null||rows.Length==0)return;
            if(!lost){for(int i=0;i<rows.Length;i++)if(rows[i]!=null&&rows[i].Button!=null&&rows[i].Button.gameObject==sel){focus=i;return;}return;}
            for(int i=0;i<rows.Length;i++)
            {
                int j=(Mathf.Clamp(focus,0,rows.Length-1)+i)%rows.Length;
                if(rows[j]!=null&&rows[j].Button!=null&&rows[j].Button.IsInteractable()){FocusMark304.Select(rows[j].Button.gameObject);return;}
            }
        }

        /// <summary>Next TMP page of this line, next line, then the menu (Services) or the close. Page turns never rebuild.</summary>
        void Advance()
        {
            if(request==null||mode!=Mode.Lines)return;
            if(body!=null)
            {
                body.ForceMeshUpdate();
                if(body.pageToDisplay<Mathf.Max(1,body.textInfo.pageCount)){body.pageToDisplay++;Turned();return;}
            }
            if(line<Count(request.Lines)-1&&body!=null){line++;body.text=LineAt(line);body.pageToDisplay=1;Turned();return;}
            if(entries.Count>0){mode=Mode.Menu;host.DialogueRebuild();return;}
            Close();
        }
        void Turned(){Layout();TurnFade();Hold();host.DialogueSound("ui_select",.3f);}
        void Hold(){builtFrame=Time.frameCount;fHeld=Keyboard.current!=null&&Keyboard.current.fKey.isPressed;}

        /// <summary>A menu row: Leave closes; otherwise Chosen first (the session may log, refuse or answer with a follow-up shown in
        /// place), then Trade / Upgrade / Maintain open their window (the session may already have opened it inside Chosen), Talk
        /// stays on the menu, the rest close. A window that does not open (not beside the station) keeps the menu.</summary>
        void Pick(int index)
        {
            if(request==null||mode!=Mode.Menu||index<0||index>=entries.Count)return;
            var e=entries[index];
            if(e.Kind==DialogueServiceKind306.Leave){Close();return;}
            if(!e.Enabled)return;
            focus=index;var r=request;int gen=generation;
            try{r.Chosen?.Invoke(e);}catch(Exception x){Debug.LogException(x);}
            if(gen!=generation||request!=r)return;   // replaced in place by a follow-up, or closed
            switch(e.Kind)
            {
                case DialogueServiceKind306.Trade:
                case DialogueServiceKind306.Upgrade:
                case DialogueServiceKind306.Maintain:
                    if(host.DialogueServiceShowing||host.DialogueOpenService(e))
                    {if(request==r){mode=Mode.Service;turn.Cancel();page=done=key=dab=null;lineGroup=null;body=null;bold=null;rows=null;}return;}
                    host.DialogueSound("ui_error",.3f);Hold();return;
                case DialogueServiceKind306.Talk:Hold();return;
                default:Close();return;
            }
        }
    }
}
