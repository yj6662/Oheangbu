using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        RectTransform confirmationRoot;
        TMP_Text confirmationBody;
        bool displayConfirmation;
        TMP_Text loadingText;

        // ------------------------------------------------------------------ title (#304 title.png, DESIGN §7.7)
        // TitleCard keeps the 274 geometry (102,74,488,904) that LateUpdate re-anchors to the left middle; at 1080 its top lands
        // at y 88, so the children below use mockup px minus (102, 88) and bleed out of the card where the mockup does.
        void BuildTitle(bool updateCursor=true)
        {
            V.Clear(baseLayer);V.Clear(modalLayer);Page="";
            var s=V.Style(Theme);
            V.EnsureCanvasChannels(canvasRect!=null?canvasRect.GetComponentInParent<Canvas>():null);
            FocusMark304.Attach(s,canvasRect);
            if(Application.isPlaying)UiText304.Prewarm(s);   // 五行符 in the slip

            // world: the illustration covers the canvas (EnvelopeParent); pivot x .62 = CSS object-position 62% 50%
            var art=LobbyIllustration!=null?LobbyIllustration:Theme.TitleBackdrop;
            var background=V.Raw(V.Stretch("LobbyIllustration",baseLayer),art,Color.white);
            if(art!=null)
            {
                if(LobbyIllustration!=null)background.rectTransform.pivot=new Vector2(.62f,.5f);
                var fit=background.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio=art.width/(float)art.height;
            }
            // MenuMist (lobby274 check): the left veil wash under the spine, Veil α.3 -> 0 over the left ~900 px
            var mist=V.Stretch("MenuMist",baseLayer).gameObject.AddComponent<LobbyAtmosphere274>();
            mist.color=UiStyle304SO.A(s.Veil,.3f);mist.raycastTarget=false;mist.FadeEnd=900f/1920f;mist.Linear=true;
            // #304 QA2: on the shared InkReveal material (fully drawn) so the wash gets the linear-space ink alpha remap like every
            // other veil; on UI/Default the α.3 wash composited in linear light and the left edge read ~25 levels lighter than
            // title.png (x 10..80: 162-175 vs 138-148)
            InkRevealEffect.On(mist,s,InkRevealMode.Wipe,1f);

            var title=V.Rect("TitleCard",baseLayer,102,74,488,904);titleCard=title;
            var group=title.gameObject.AddComponent<CanvasGroup>();
            // a page opened from the title (설정 / 조작) hides the card under its veil; a confirm only suspends it (confirm.png)
            title.gameObject.AddComponent<FlowTitleGate304>().Bind(group,()=>Page.Length==0&&confirmationRoot==null&&!Busy,
                ()=>Page.Length>0,s.Motion.Sec(s.Motion.VeilMs,UiTween304.ReducedMotion));
            float X(float mockupX)=>mockupX-Flow304CardX;
            float Y(float mockupY)=>mockupY-Flow304CardY;

            // 기둥: one ink stroke_spine (14,-110) 660x1640 α.93, bleeding past the card and the screen edge
            V.Brush(s,title,"Spine",StrokeClass304.Spine,s.Ink,X(14),Y(-110),660,1640,.93f);
            // 락업: 오 / 행 / 부 Serif900 150 vertical, Rubbing_Lite, centred in 170 px at (126,96). Name kept from 274 ("Title")
            var lockRole=s.Role(UiType304.Lockup150);
            var lockup=V.Label(s,title,"Title",UiText304.Vertical("오행부"),lockRole,s.Paper,X(126),0f,170f,0f,TextAlignmentOptions.Top);
            V.Place(lockup.rectTransform,X(126),Y(96)+lockRole.CssTopOffset(lockup.font));
            // 五行符 쪽지 84x236 with the D11 제첨 rim. No seal (removed at the user's request 2026-09-30): title.png stamps the seal
            // 92 at (326,112) over the slip's top, so the seal + slip unit started level with the lockup. Without the seal the
            // slip at 190 hung ~66 px below 오 with an empty corner above it; #304 QA2 lifts it to 116 so its torn paper edge
            // (~8 px inside the rect) starts on 오's glyph top (y 124 at 1080). 五行符 keeps its 24 px inset.
            const float slipTop=116f,hanjaInset=214f-190f;
            var slipSprite=s.Sprites.SheetSlip;
            var slip=V.SpriteImage(title,"TitleSlip",slipSprite,slipSprite!=null?Color.white:s.Sheet,X(330),Y(slipTop),84,236);
            Flow304SlipRim(s,slip.rectTransform,84,236);
            var slipRole=Flow304SlipRole;
            var hanja=V.Label(s,slip.rectTransform,"Hanja",UiText304.Vertical("五行符"),slipRole,s.Ink,0f,0f,84f,0f,TextAlignmentOptions.Top);
            V.Place(hanja.rectTransform,0f,hanjaInset+slipRole.CssTopOffset(hanja.font));

            // 메뉴 (112,610) 430 wide: first row 66, then 60 (title.png); exactly five LobbyMenuButton274 (lobby274 check)
            var info=WorldMacroSaveSlot.Inspect(Application.persistentDataPath,ActiveSlotName);
            bool canContinue=info.Status==WorldMacroSaveSlotStatus.Primary||info.Status==WorldMacroSaveSlotStatus.Backup||info.Status==WorldMacroSaveSlotStatus.Temporary;
            string why=info.Status==WorldMacroSaveSlotStatus.Invalid?"저장 손상":"저장 없음";
            LobbyMenuButton274 Row(string name,string label,int i,UnityEngine.Events.UnityAction action,string reason=null)
            {
                float y=Y(610)+(i==0?0f:66f+(i-1)*60f);
                return LobbyMenuButton274.Create(s,title,name,label,Theme,X(112),y,430,i==0?66:60,action,"Enter",reason);
            }
            var resume=Row("Continue","이어하기",0,()=>StartCoroutine(LoadPlay()),canContinue?null:why);resume.interactable=canContinue;
            var newGame=Row("NewGame","새 게임",1,()=>
            {
                if(info.Status==WorldMacroSaveSlotStatus.New)StartNew();
                else Confirm("새 게임을 시작할까요?","기존 진행은 백업으로 보관된다.",StartNew);
            });
            Row("Options","설정",2,()=>{OpenPage("옵션");if(Page=="옵션")Flow304TitleReturn="Options";});
            Row("Controls","조작",3,()=>{OpenPage("조작 안내");if(Page=="조작 안내")Flow304TitleReturn="Controls";});
            Row("Quit","종료",4,()=>Confirm("게임을 종료할까요?","현재 진행을 저장합니다.",QuitApplication));
            Flow304TitleSaveLine(s,title,info,canContinue);

            if(updateCursor){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            ApplyTextScale();
            // first build of a title scene bleeds in; returning from 설정 / 조작 does not
            int scene=SceneManager.GetActiveScene().handle;
            if(Application.isPlaying&&scene!=Flow304TitleScene)UiTween304.BleedIn(group,s,0,UiTween304.Token(group)).Forget();
            Flow304TitleScene=scene;
            // initial selection (IMPLEMENTATION §7.11): the row that opened the page we came back from, else 이어하기 / 새 게임
            GameObject first=null;
            if(!string.IsNullOrEmpty(Flow304TitleReturn))
            {
                var back=title.Find(Flow304TitleReturn);var sel=back!=null?back.GetComponent<Selectable>():null;
                if(sel!=null&&sel.interactable)first=back.gameObject;
                Flow304TitleReturn=null;
            }
            if(first==null)first=(canContinue?resume:newGame).gameObject;
            FocusMark304.Select(first);
        }
        void StartNew()
        {
            if(!IsTitle||Busy)return;
            if(!WorldMacroSaveSlot.TryArchiveForNew(Application.persistentDataPath,ActiveSlotName,out _,out var error))
            {LastError=error;Menu304ShowNotice(error,UiNoticeKind304.Error,8f);return;}
            StartCoroutine(LoadPlay());
        }
        IEnumerator LoadPlay()
        {
            if(Busy)yield break;
            if(LoadingProfile!=null){yield return LoadWithRegionScreen();yield break;}
            Busy=true;Gate.Block();DismissConfirmation();Settings.Revert();
            ShowLoading("여정 준비");
            Time.timeScale=1;
            var load=SceneManager.LoadSceneAsync(PlaySceneName,LoadSceneMode.Single);
            if(load==null){Busy=false;LastError="플레이 장면을 불러올 수 없다.";BuildTitle();Menu304ShowNotice(LastError,UiNoticeKind304.Error,8f);yield break;}
            while(!load.isDone){Flow304LoadingProgress(load.progress/.9f);yield return null;}
            Busy=false;bound=false;currentSceneHandle=-1;
        }
        IEnumerator ReturnTitle(bool leaveBlockedSession=false)
        {
            if(Busy)yield break;
            if(Session!=null&&Session.SaveBlocked&&!leaveBlockedSession)
            {
                Confirm("저장하지 않고 로비로 돌아갈까요?","손상된 저장 원본은 그대로 보관된다. 이번 임시 플레이의 변경 내용은 저장되지 않는다.",()=>StartCoroutine(ReturnTitle(true)));
                yield break;
            }
            if(Session!=null&&!Session.SaveBlocked&&!Session.SaveNow(out var error))
            {LastError=error;Menu304ShowNotice("저장하지 못했다.\n"+error,UiNoticeKind304.Error,8f);yield break;}
            // Do not release the existing pause/menu until Unity has accepted the load.
            // A missing title scene must leave a usable ending/menu with an error and retry.
            Busy=true;Settings.Revert();
            AsyncOperation load=null;string loadError=null;
            try{load=SceneManager.LoadSceneAsync(TitleSceneName,LoadSceneMode.Single);}
            catch(Exception exception){loadError=exception.Message;}
            if(load==null)
            {
                Busy=false;LastError="로비를 불러올 수 없다."+(string.IsNullOrEmpty(loadError)?"":"\n"+loadError);
                Menu304ShowNotice(LastError,UiNoticeKind304.Error,8f);yield break;
            }
            UnhookSession();SetHud(true);ShowLoading(leaveBlockedSession?"원본 보존 · 로비로":"여정 기록됨");
            if(Map!=null){Destroy(Map.gameObject);Map=null;}
            Pause.End();Gate.Block();Time.timeScale=1;
            while(!load.isDone){Flow304LoadingProgress(load.progress/.9f);yield return null;}
            Busy=false;bound=false;currentSceneHandle=-1;
        }

        // ------------------------------------------------------------------ journey overlay (IMPLEMENTATION §7.12 last item)
        /// <summary>Menu-canvas loading overlay (new game / continue without a region profile, return to the lobby): the paper
        /// card is replaced by an opaque 먹장막 with 부인 64 (drawn only while UiStyle304SO.ShowSeals is on), the label (Title36 =
        /// Serif800 36) and the thin progress line, one layout for every label (IMPLEMENTATION §7.12). Harness name kept: ShowLoading(string).</summary>
        void ShowLoading(string label){Flow304ShowLoading(label);}

        // ------------------------------------------------------------------ confirm (#304 confirm.png, DESIGN §5.15)
        /// <summary>Uniform veil α.66 over the canvas, sheet_strip 1100x320 (24 px above centre) with 부인 40 + Title36 ink + Body22
        /// ash, [돌아가기] (default focus: ink underlay, paper label, 방점, hollow Esc) and [확인] (ink label, filled Enter, dry
        /// under-stroke). The page behind is suspended (FocusMark304.Suspend) and gets its selection back on dismiss.
        /// Names kept: Confirmation/Shade, Confirmation/Dialog, Dialog/Accept, Dialog/Cancel, Dialog/Title, Dialog/Body.</summary>
        void Confirm(string title,string message,Action accept)
        {
            DismissConfirmation();
            var s=V.Style(Theme);
            V.EnsureCanvasChannels(canvasRect.GetComponentInParent<Canvas>());
            FocusMark304.Attach(s,canvasRect);
            var es=EventSystem.current;
            Flow304ConfirmReturn=es!=null?es.currentSelectedGameObject:null;
            Flow304Suspend();
            confirmationRoot=V.Stretch("Confirmation",canvasRect);
            var shade=V.Dim(s,confirmationRoot);   // "Shade": Veil α.66, raycast on
            // #304 QA2: V.Dim is a plain UI/Default image, so α.66 composited in linear light and the page behind read at
            // sRGB-effective ~.42 instead of confirm.png's .66 (after2/confirm.png: 소지품 label 143 vs 89). On the shared InkReveal
            // material (Edges, fully drawn = no noise holes) it gets the foundation's ink alpha remap like the other veils.
            InkRevealEffect.On(shade,s,InkRevealMode.Edges,1f);
            var dialog=V.Rect("Dialog",confirmationRoot,0,0,1100,320);
            dialog.anchorMin=dialog.anchorMax=dialog.pivot=new Vector2(.5f,.5f);dialog.anchoredPosition=new Vector2(0,24);
            dialog.gameObject.AddComponent<UiPageFit304>();   // same min(1, W/1920, H/1080) as the pages behind it
            var strip=s.Sprites.SheetStrip;
            V.SpriteImage(dialog,"Sheet",strip,strip!=null?Color.white:s.Sheet,0,0,1100,320,0,1,true);
            // 부인 40 hangs in the left margin before the title (null while UiStyle304SO.ShowSeals is off). The text column (title,
            // body) sits on the [돌아가기] 방점 column at 114 either way, so a missing seal leaves no gap inside the layout.
            V.Seal(s,dialog,52,48,40);
            var titleRole=s.Role(UiType304.Title36);
            var t=V.Label(s,dialog,"Title",title,titleRole,s.Ink,114,0f);
            V.Place(t.rectTransform,114,42+titleRole.CssTopOffset(t.font));
            var bodyRole=s.Role(UiType304.Body22);
            confirmationBody=V.Label(s,dialog,"Body",message??"",bodyRole,s.Ash,116,0f,900f,0f,TextAlignmentOptions.TopLeft,true);
            V.Place(confirmationBody.rectTransform,116,98+bodyRole.CssTopOffset(confirmationBody.font));
            var cancel=V.FocusRow(s,dialog,"Cancel","돌아가기",76,184,520,96,Flow304CancelPressed,new FocusRowSpec304
            {
                OnPaper=true,Role=UiType304.Title30,LabelX=86,UnderlayH=96,UnderlayX=0,
                Key="Esc",KeyMode=FocusKeyMode304.Hollow,KeySmall=false,KeyX=274,SoundTheme=Theme,
            });
            var acceptRow=V.FocusRow(s,dialog,"Accept","확인",756,184,300,96,()=>{DismissConfirmation();accept?.Invoke();},new FocusRowSpec304
            {
                OnPaper=true,Role=UiType304.Title30,LabelX=14,UnderlayH=96,
                Key="Enter",KeySmall=false,KeyX=90,UnderStroke=true,SoundTheme=Theme,
            });
            Flow304ConfirmAccept=acceptRow.Button;
            // the two rows only lead to each other (nothing behind the veil is reachable)
            cancel.Button.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnRight=acceptRow.Button};
            acceptRow.Button.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=cancel.Button};
            Flow304CountdownShown=-1;
            ApplyTextScale();
            FocusMark304.Select(cancel.Button.gameObject);   // the safe side (DESIGN §5.2)
        }
        void DismissConfirmation()
        {
            if(confirmationRoot!=null)
            {
                confirmationRoot.gameObject.SetActive(false);Destroy(confirmationRoot.gameObject);confirmationRoot=null;
                confirmationBody=null;Flow304ConfirmAccept=null;
                Flow304Resume(true);
            }
            if(displayConfirmation){displayConfirmation=false;Settings.Revert();}
        }
        void ShowDisplayConfirmation()
        {
            // Confirm's normal dismissal would revert the preview; clear the flag before explicitly accepting it.
            Confirm("이 화면 설정을 사용할까요?","",()=>{Settings.Confirm();OpenPage("옵션");});
            displayConfirmation=true;
            var accept=confirmationRoot.Find("Dialog/Accept").GetComponent<Button>();
            accept.onClick.RemoveAllListeners();accept.onClick.AddListener(()=>
            {displayConfirmation=false;Settings.Confirm();DismissConfirmation();OpenPage("옵션");});
            if(Settings.IsPreviewing)Flow304ShowCountdown();   // the body is never blank for a frame
        }
        void UpdateDisplayConfirmation()
        {
            if(!displayConfirmation)return;
            if(!Settings.IsPreviewing){displayConfirmation=false;DismissConfirmation();if(Page=="옵션")OpenPage("옵션");return;}
            Flow304ShowCountdown();
        }
        void QuitApplication()
        {
            if(Session!=null&&Session.SaveBlocked)
            {
                Confirm("저장하지 않고 종료할까요?","손상된 저장 원본은 그대로 보관된다. 이번 임시 플레이의 변경 내용은 저장되지 않는다.",ExitProcess);
                return;
            }
            if(Session!=null&&!Session.SaveNow(out var error)){Menu304ShowNotice("저장하지 못했다.\n"+error,UiNoticeKind304.Error,8f);return;}
            ExitProcess();
        }
        void ExitProcess()
        {
            Settings.Revert();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
    }
}
