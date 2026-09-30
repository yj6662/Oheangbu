using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 대화 · 상세 (IMPLEMENTATION §7.9, dialogue.png, REFERENCE_BOARD D19). Present -> DetailRequested -> ShowDetail ->
    // OpenPage("상세") (pause + save unchanged) builds "StoryBand304", a bottom-anchored 1920x1080 page without a veil:
    //  - band tier: a commission line (body matches a CommissionSpec text), a choice (Menu304PresentChoices) or a description
    //    without a speaker: ramp_bottom 460 α.6 + ink band (-150,690) h390 α.94, speaker Serif900 60 (148,744) or the title in
    //    Heading44, 의뢰 meta + 사례, Prose28 body 1000 wide with TMP Page overflow (3 lines per page, title-safe);
    //  - subtitle tier (D19): a speaker line that is not a commission: one Prose28 line on a bottom-centre ink wet_s, the
    //    speaker as a Serif700 24 prefix inside it, one line per page.
    // Speaker = the text before the first ": " when it is 12 characters or less ("수레꾼: ..."). [F] / Enter / click on "Done"
    // turns the page; on the last page it reads 확인 and closes. Esc closes (with choices: the last choice).
    // #306 §2-3: NPC speech moves to DialogueView304 on this same page / root (PlaytestUiRoot.Dialogue304.cs); this band stays for
    // Present() text (evidence, investigation, objects) and for NPC lines until the session raises DialogueRequested for them.
    public sealed partial class PlaytestUiRoot
    {
        /// <summary>One dialogue choice (label Serif700 28 -> Title30 focused, result meta, action). The last one is the Esc choice.</summary>
        public struct Menu304StoryChoice { public string Label, Result; public Action Chosen; }

        IReadOnlyList<Menu304StoryChoice> menu304StoryChoices;
        TMP_Text menu304StoryText;
        FocusRow304 menu304StoryDone;
        int menu304StoryFrame;
        bool menu304StoryFHeld;

        /// <summary>Opens the band with choices (schema step 2 of §7.9; nothing in the data calls it yet).</summary>
        public void Menu304PresentChoices(string title,string body,IReadOnlyList<Menu304StoryChoice> choices)
        {
            detailTitle=title;detailBody=body;menu304StoryChoices=choices!=null&&choices.Count>0?choices:null;
            OpenPage("상세");
        }

        /// <summary>Kept for the old text-mode route: the detail page is the same band now.</summary>
        void BuildDetail(){Menu304BuildStory();}

        void Menu304BuildStory()
        {
            var s=V.Style(Theme);
            V.EnsureCanvasChannels(canvas);FocusMark304.Attach(s,canvasRect);UiText304.Prewarm(s);
            frame=null;menu304Rail=null;menu304Veil=null;menu304LiftX=float.NaN;
            string body=(detailBody??"").Trim();
            Menu304SplitSpeaker(body,out string speaker,out string text);
            var commission=Menu304FindCommission(body);
            bool choices=menu304StoryChoices!=null&&menu304StoryChoices.Count>0;
            var page=V.Page304(modalLayer,"StoryBand304");
            page.anchorMin=page.anchorMax=page.pivot=new Vector2(.5f,0f);page.anchoredPosition=Vector2.zero;   // hugs the bottom edge
            menu304Page=page;contentRoot=page;
            Menu304LegacyText("상세");
            menu304StoryFrame=Time.frameCount;
            menu304StoryFHeld=Keyboard.current!=null&&Keyboard.current.fKey.isPressed;   // the [F] that opened it must be released first
            if(commission!=null||choices||speaker.Length==0)Menu304BuildBand(s,page,speaker,speaker.Length>0?text:body,commission,choices);
            else Menu304BuildSubtitle(s,page,speaker,text);
        }

        void Menu304BuildBand(UiStyle304SO s,RectTransform page,string speaker,string text,WorldMacroPlaytestSO.CommissionSpec commission,bool choices)
        {
            // ramp_bottom (alpha 1 at the bottom) x Veil α.6 over the lowest 460 px, wide enough for 32:9
            // on the shared InkReveal material (fully revealed) so the ramp gets the same linear-space alpha remap as the band:
            // as plain UI/Default it stayed lighter than dialogue.png at the bottom edge ((100,1040) 80 vs 63, after2/dialogue.png)
            if(s.Sprites.RampBottom!=null)
            {
                var ramp=V.SpriteImage(page,"RampBottom",s.Sprites.RampBottom,UiStyle304SO.A(s.Veil,.6f),-V.VeilLead,620,UiPageFit304.Width+2*V.VeilLead,460);
                InkRevealEffect.On(ramp,s,InkRevealMode.Bleed,1f);
            }
            // band from far left (the head slice sits off-screen on any aspect) to the body's right; continue lines keep [F] inside it
            float contentRight=choices?1160f:1330f;
            var band=V.Stroke(s,page,"Band",StrokeClass304.Band,s.Ink,-150-V.VeilLead,690,390,contentRight,.94f);
            var fx=band.GetComponent<InkRevealEffect>();
            if(fx!=null)UiTween304.StrokeIn(fx,s,true,UiTween304.Token(fx),.94f*s.Ink.a).Forget();
            var textRoot=V.Rect("BandText",page,0,0,UiPageFit304.Width,UiPageFit304.Height);
            var group=textRoot.gameObject.AddComponent<CanvasGroup>();
            float metaX=372f;
            // mockup CSS tops (dialogue.png): speaker 744, body 842. Without CssTopOffset the speaker sat 10 px low and the body
            // 7 px high (after/dialogue.png). The no-speaker title shares the speaker's top so both glyph tops land at y752.
            if(speaker.Length>0)
            {
                var name=Menu304CssLabel(s,textRoot,"Speaker",speaker,UiType304.Speaker60,s.Paper,148,744);
                metaX=Mathf.Max(372f,148f+name.rectTransform.sizeDelta.x+40f);
            }
            else if(!string.IsNullOrEmpty(detailTitle))
            {
                var head=Menu304CssLabel(s,textRoot,"Heading",detailTitle,UiType304.Heading44,s.Paper,148,744);
                metaX=Mathf.Max(372f,148f+head.rectTransform.sizeDelta.x+40f);
            }
            if(commission!=null)
            {
                // CSS tops 752 / 780 (dialogue.png) + CssTopOffset like the speaker / body (Meta20: about -1 px; 의뢰 measured 759 vs 756,
                // the other ~2 px are the browser font's metrics, not the layout)
                Menu304CssLabel(s,textRoot,"CommissionMeta","의뢰",UiType304.Meta20,s.Mist,metaX,752);
                if(commission.Reward>0&&!Session.CommissionReported(commission.Id))
                    Menu304CssLabel(s,textRoot,"CommissionReward","사례 조선통보 "+commission.Reward.ToString("N0"),UiType304.Meta20,s.Mist,metaX,780);
            }
            var body=Menu304CssLabel(s,textRoot,"Body",text,UiType304.Prose28,s.Paper,150,842,1000,true);
            float lineH=s.Role(UiType304.Prose28).LineHeight*body.fontSize;
            // 3 lines per page (dialogue.png shows 3 at (150,842); a 4th would end at y1032, below the title-safe 1016)
            body.rectTransform.sizeDelta=new Vector2(1000,Mathf.Ceil(lineH*3f)+2f);
            body.overflowMode=TextOverflowModes.Page;body.pageToDisplay=1;
            menu304StoryText=body;
            if(choices)Menu304BuildChoices(s,textRoot);
            else menu304StoryDone=Menu304DoneRow(s,textRoot,1180,962);
            UiTween304.BleedIn(group,s,1,UiTween304.Token(group)).Forget();
            Menu304StoryRefresh();
        }

        void Menu304BuildSubtitle(UiStyle304SO s,RectTransform page,string speaker,string text)
        {
            const float h=96f,y=884f;
            var root=V.Rect("Subtitle",page,0,0,UiPageFit304.Width,UiPageFit304.Height);
            var group=root.gameObject.AddComponent<CanvasGroup>();
            var name=V.Label(s,root,"Speaker",speaker,UiType304.Serif700_24,s.Mist,0,0);
            var line=V.Label(s,root,"Body",text,UiType304.Prose28,s.Paper,0,0,1000,0,TextAlignmentOptions.TopLeft,true);
            float lineH=Mathf.Ceil(s.Role(UiType304.Prose28).LineHeight*line.fontSize);
            line.overflowMode=TextOverflowModes.Page;line.pageToDisplay=1;
            line.rectTransform.sizeDelta=new Vector2(1000,lineH);
            line.ForceMeshUpdate();
            float widest=0f;var info=line.textInfo;
            for(int i=0;i<info.lineCount;i++){var li=info.lineInfo[i];widest=Mathf.Max(widest,li.lineExtents.max.x-li.lineExtents.min.x);}
            if(widest<=1f||widest>1000f)widest=1000f;
            line.rectTransform.sizeDelta=new Vector2(Mathf.Ceil(widest)+6f,lineH);
            menu304StoryText=line;
            menu304StoryDone=Menu304DoneRow(s,root,0,0);
            float nameW=name.rectTransform.sizeDelta.x,lineW=line.rectTransform.sizeDelta.x,doneW=menu304StoryDone.Rect.sizeDelta.x;
            float block=nameW+24f+lineW+28f+doneW,left=Mathf.Max(96f,960f-block*.5f),mid=y+h*.5f;
            V.Place(name.rectTransform,left,mid-name.rectTransform.sizeDelta.y*.5f);
            V.Place(line.rectTransform,left+nameW+24f,mid-lineH*.5f);
            V.Place(menu304StoryDone.Rect,left+nameW+24f+lineW+28f,mid-menu304StoryDone.Rect.sizeDelta.y*.5f);
            var under=V.Stroke(s,page,"SubtitleUnderlay",StrokeClass304.WetS,s.Ink,left-64f,y,h,left+block+12f,.93f);
            under.transform.SetSiblingIndex(root.GetSiblingIndex());
            var fx=under.GetComponent<InkRevealEffect>();
            if(fx!=null)UiTween304.RevealIn(fx,s,s.Motion.PromptInMs,UiTween304.Token(fx),.93f*s.Ink.a).Forget();
            UiTween304.BleedIn(group,s,1,UiTween304.Token(group)).Forget();
            Menu304StoryRefresh();
        }

        /// <summary>"Done" (harness name kept): filled [F] + 다음 / 확인 meta. The row's own underlay is invisible (the band or the
        /// subtitle stroke already carries it); the page's one dab sits left of the key.</summary>
        FocusRow304 Menu304DoneRow(UiStyle304SO s,RectTransform parent,float x,float y)
        {
            float keyW=V.KeycapWidth(s,null,"F",true);
            var row=V.FocusRow(s,parent,"Done","다음",x,y,keyW+10+72,48,Menu304StoryAdvance,new FocusRowSpec304
            {
                Role=UiType304.Meta20,LabelColor=s.Mist,LabelFocusColor=s.Paper,LabelX=keyW+10,
                Key="F",KeyMode=FocusKeyMode304.Filled,KeySmall=true,KeyX=0,DabGap=keyW+10+14,UnderlayAlpha=0f,SoundTheme=Theme,
            });
            FocusMark304.Select(row.Button.gameObject);
            return row;
        }

        void Menu304BuildChoices(UiStyle304SO s,RectTransform band)
        {
            GameObject first=null;
            for(int i=0;i<menu304StoryChoices.Count;i++)
            {
                int index=i;bool last=i==menu304StoryChoices.Count-1;var c=menu304StoryChoices[i];
                var row=V.FocusRow(s,band,"Choice_"+i,c.Label,1300,766+i*146,560,124,()=>Menu304Choose(index),new FocusRowSpec304
                {
                    Role=UiType304.Serif700_28,FocusRole=UiType304.Title30,LabelX=110,
                    Underlay=StrokeClass304.WetS,UnderlayH=124,UnderlayX=18,ContentRight=430,
                    BaseUnderlayH=112,BaseUnderlay=StrokeClass304.WetS,BaseUnderlayX=38,BaseContentRight=406,
                    Meta=c.Result,MetaBelow=true,MetaX=112,
                    Key=last?"Esc":null,KeyMode=FocusKeyMode304.Filled,FocusKey="F",KeyX=last?352:376,KeySmall=false,
                    DabDy=8,SoundTheme=Theme,
                });
                if(first==null)first=row.Button.gameObject;
            }
            if(first!=null)FocusMark304.Select(first);
        }

        void Menu304Choose(int index)
        {
            var list=menu304StoryChoices;if(list==null||index<0||index>=list.Count)return;
            var chosen=list[index].Chosen;menu304StoryChoices=null;
            CloseMenu();chosen?.Invoke();
        }

        void Menu304StoryAdvance()
        {
            if(menu304StoryText!=null)
            {
                menu304StoryText.ForceMeshUpdate();
                int pages=Mathf.Max(1,menu304StoryText.textInfo.pageCount);
                if(menu304StoryText.pageToDisplay<pages){menu304StoryText.pageToDisplay++;Menu304StoryRefresh();PlayNamedSound("ui_select",.3f);return;}
            }
            CloseMenu();
        }

        void Menu304StoryRefresh()
        {
            if(menu304StoryDone==null||menu304StoryDone.Label==null||menu304StoryText==null)return;
            menu304StoryText.ForceMeshUpdate();
            bool more=menu304StoryText.pageToDisplay<Mathf.Max(1,menu304StoryText.textInfo.pageCount);
            menu304StoryDone.Label.text=more?"다음":"확인";
        }

        /// <summary>Esc on the band: with choices the last choice (거절 / 떠나기) answers; true when handled.</summary>
        bool Menu304StoryBack()
        {
            if(menu304StoryChoices==null||menu304StoryChoices.Count==0)return false;
            Menu304Choose(menu304StoryChoices.Count-1);return true;
        }

        void Menu304StoryTick()
        {
            if(Dialogue304Open){dialogue304.Tick();return;}   // #306: the conversation surface owns F / pad B while it shows
            var k=Keyboard.current;if(k==null)return;
            if(menu304StoryFHeld){if(!k.fKey.isPressed)menu304StoryFHeld=false;return;}
            if(!k.fKey.wasPressedThisFrame||Time.frameCount<=menu304StoryFrame)return;
            if(menu304StoryChoices!=null)
            {
                var sel=UnityEngine.EventSystems.EventSystem.current!=null?UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject:null;
                var b=sel!=null?sel.GetComponent<Button>():null;
                if(b!=null&&b.IsInteractable()&&sel.name.StartsWith("Choice_",StringComparison.Ordinal))b.onClick.Invoke();
                return;
            }
            Menu304StoryAdvance();
        }

        /// <summary>"수레꾼: …" -> speaker "수레꾼", text "…" (first ": " within 12 characters, single line before it).</summary>
        static void Menu304SplitSpeaker(string body,out string speaker,out string text)
        {
            speaker="";text=body??"";
            if(string.IsNullOrEmpty(body))return;
            int i=body.IndexOf(": ",StringComparison.Ordinal);
            if(i<=0||i>12||body.IndexOf('\n')>=0&&body.IndexOf('\n')<i)return;
            speaker=body.Substring(0,i).Trim();text=body.Substring(i+2).TrimStart();
        }

        WorldMacroPlaytestSO.CommissionSpec Menu304FindCommission(string body)
        {
            var list=Session!=null&&Session.Content!=null?Session.Content.Commissions:null;
            if(list==null||string.IsNullOrEmpty(body))return null;
            foreach(var q in list)
            {
                if(q==null||!q.IsConfigured)continue;
                if(Menu304Same(body,q.OfferText)||Menu304Same(body,q.WaitingText)||Menu304Same(body,q.CompletedText)
                   ||(!string.IsNullOrEmpty(q.ReportText)&&body.StartsWith(q.ReportText.Trim(),StringComparison.Ordinal)))return q;
            }
            return null;
        }
        static bool Menu304Same(string a,string b)=>!string.IsNullOrEmpty(b)&&string.Equals(a.Trim(),b.Trim(),StringComparison.Ordinal);
    }
}
