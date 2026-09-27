using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        RectTransform confirmationRoot;
        Text confirmationBody;
        bool displayConfirmation;
        Text loadingText;

        void BuildTitle(bool updateCursor=true)
        {
            V.Clear(baseLayer);V.Clear(modalLayer);Page="";
            var background=V.Raw(V.Stretch("LobbyIllustration",baseLayer),LobbyIllustration!=null?LobbyIllustration:Theme.TitleBackdrop,Color.white);
            if(LobbyIllustration!=null)
            {
                var fit=background.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio=LobbyIllustration.width/(float)LobbyIllustration.height;
            }
            V.Image(V.Stretch("BackdropShade",baseLayer),new Color(.12f,.10f,.075f,LobbyIllustration!=null?.035f:.20f));
            if(LobbyIllustration!=null)
            {
                var mist=V.Stretch("MenuMist",baseLayer).gameObject.AddComponent<LobbyAtmosphere274>();
                mist.color=new Color(Theme.Paper.r,Theme.Paper.g,Theme.Paper.b,.65f);mist.raycastTarget=false;
            }
            var title=V.Rect("TitleCard",baseLayer,102,74,488,904);titleCard=title;
            if(LobbyIllustration==null)
            {
                V.Image(V.Stretch("Paper",title),new Color(Theme.Paper.r,Theme.Paper.g,Theme.Paper.b,.94f));
                V.Raw(V.Stretch("Fiber",title),Theme.PaperTexture,new Color(1,1,1,.18f));
            }
            V.Text(title,"Title","오행부",Theme.Font,84,Theme.Ink,34,126,390,122);
            if(LobbyIllustration==null)V.Rule(title,Theme,42,286,386);
            var info=WorldMacroSaveSlot.Inspect(Application.persistentDataPath,ActiveSlotName);
            bool canContinue=info.Status==WorldMacroSaveSlotStatus.Primary||info.Status==WorldMacroSaveSlotStatus.Backup||info.Status==WorldMacroSaveSlotStatus.Temporary;
            // 건조한 사물·상태 (사용자 확정 2026-09-19): 저장 슬롯의 상태를 명사로. 문장형 여정 서술 제거.
            string firstJourney=Content!=null&&Content.Opening!=null&&Content.Opening.Enabled?"새 여정 · 마을 관청":"새 여정 · 폐광";
            string saved=canContinue?"저장된 게임":info.Status==WorldMacroSaveSlotStatus.Invalid?"저장 손상 · 원본은 보관됨":"저장 없음";
            if(info.Status==WorldMacroSaveSlotStatus.Temporary)saved="중단된 저장 · 이어하기로 복구";
            if(info.Status==WorldMacroSaveSlotStatus.Backup)saved="백업 저장 · 이어하기로 복구";
            V.Text(title,"SaveStatus",saved,Theme.Font,20,info.Status==WorldMacroSaveSlotStatus.Invalid?Theme.Seal:Theme.Muted,44,318,384,104);
            Button Menu(string name,string label,float y,UnityEngine.Events.UnityAction action,bool primary=false)
            {
                if(LobbyIllustration==null)return V.Button(title,name,label,Theme,42,y,386,primary?60:54,action,primary,true);
                return LobbyMenuButton274.Create(title,name,label,Theme,42,y,300,54,action);
            }
            var resume=Menu("Continue","이어하기",454,()=>StartCoroutine(LoadPlay()),true);resume.interactable=canContinue;
            Menu("NewGame","새 게임",530,()=>
            {
                if(info.Status==WorldMacroSaveSlotStatus.New)StartNew();
                else Confirm("새 게임을 시작할까요?","기존 진행은 백업으로 보관된다.",StartNew);
            });
            Menu("Options","설정",598,()=>OpenPage("옵션"));
            Menu("Controls","조작",666,()=>OpenPage("조작 안내"));
            Menu("Quit","종료",734,()=>Confirm("게임을 종료할까요?","현재 진행을 저장합니다.",QuitApplication));
            if(updateCursor){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            ApplyTextScale();
        }
        void StartNew()
        {
            if(!IsTitle||Busy)return;
            if(!WorldMacroSaveSlot.TryArchiveForNew(Application.persistentDataPath,ActiveSlotName,out _,out var error))
            {LastError=error;ShowNotice(error,8);return;}
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
            if(load==null){Busy=false;LastError="플레이 장면을 불러올 수 없다.";BuildTitle();ShowNotice(LastError,8);yield break;}
            while(!load.isDone){if(loadingText!=null)loadingText.text="여정 준비  "+Mathf.RoundToInt(Mathf.Clamp01(load.progress/.9f)*100)+"%";yield return null;}
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
            {LastError=error;ShowNotice("저장하지 못했다.\n"+error,8);yield break;}
            // Do not release the existing pause/menu until Unity has accepted the load.
            // A missing title scene must leave a usable ending/menu with an error and retry.
            Busy=true;Settings.Revert();
            AsyncOperation load=null;string loadError=null;
            try{load=SceneManager.LoadSceneAsync(TitleSceneName,LoadSceneMode.Single);}
            catch(Exception exception){loadError=exception.Message;}
            if(load==null)
            {
                Busy=false;LastError="로비를 불러올 수 없다."+(string.IsNullOrEmpty(loadError)?"":"\n"+loadError);
                ShowNotice(LastError,8);yield break;
            }
            UnhookSession();SetHud(true);ShowLoading(leaveBlockedSession?"원본 보존 · 로비로":"여정 기록됨");
            if(Map!=null){Destroy(Map.gameObject);Map=null;}
            Pause.End();Gate.Block();Time.timeScale=1;
            while(!load.isDone)yield return null;
            Busy=false;bound=false;currentSceneHandle=-1;
        }
        void ShowLoading(string label)
        {
            V.Clear(modalLayer);Page="";
            V.Image(V.Stretch("LoadingPaper",modalLayer),Theme.Paper,null,true);
            V.Raw(V.Stretch("LoadingFibers",modalLayer),Theme.PaperTexture,new Color(1,1,1,.18f));
            var center=V.Rect("Center",modalLayer,0,0,900,320);center.anchorMin=center.anchorMax=center.pivot=new Vector2(.5f,.5f);center.anchoredPosition=Vector2.zero;
            V.Text(center,"Title","오행부",Theme.Font,68,Theme.Ink,0,25,900,110,TextAnchor.MiddleCenter);
            V.Rule(center,Theme,170,173,560);
            loadingText=V.Text(center,"Progress",label,Theme.Font,24,Theme.Muted,0,226,900,60,TextAnchor.MiddleCenter);
        }
        void Confirm(string title,string message,Action accept)
        {
            DismissConfirmation();
            confirmationRoot=V.Stretch("Confirmation",canvasRect);V.Image(V.Stretch("Shade",confirmationRoot),new Color(0,0,0,.66f),null,true);
            var dialog=V.Rect("Dialog",confirmationRoot,0,0,810,386);dialog.anchorMin=dialog.anchorMax=dialog.pivot=new Vector2(.5f,.5f);dialog.anchoredPosition=Vector2.zero;
            V.Image(V.Stretch("Paper",dialog),Theme.Paper,null,true);
            V.Raw(V.Stretch("Fiber",dialog),Theme.PaperTexture,new Color(1,1,1,.17f));
            V.Text(dialog,"Title",title,Theme.Font,32,Theme.Ink,44,34,710,58);
            confirmationBody=V.Text(dialog,"Body",message,Theme.Font,24,Theme.Muted,44,111,710,122);
            if(Theme.Icons!=null&&!IsTitle&&Page!="일시정지"){
                dialog.Find("Title").GetComponent<Text>().enabled=false;confirmationBody.enabled=false;
                var symbol=V.Rect("ConfirmationSymbol",dialog,320,82,150,130);CompactUiSymbols.Draw(symbol,title,Theme.Icons,Theme.Ink);
            }
            V.Button(dialog,"Cancel","돌아가기",Theme,44,282,330,56,DismissConfirmation,false,IsTitle||Page=="일시정지");
            V.Button(dialog,"Accept","확인",Theme,412,282,352,56,()=>{DismissConfirmation();accept?.Invoke();},true,IsTitle||Page=="일시정지");
            ApplyTextScale();
        }
        void DismissConfirmation()
        {
            if(confirmationRoot!=null){confirmationRoot.gameObject.SetActive(false);Destroy(confirmationRoot.gameObject);confirmationRoot=null;}
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
        }
        void UpdateDisplayConfirmation()
        {
            if(!displayConfirmation)return;
            if(!Settings.IsPreviewing){displayConfirmation=false;DismissConfirmation();if(Page=="옵션")OpenPage("옵션");return;}
            if(Theme.Icons!=null&&confirmationBody!=null)confirmationBody.enabled=true;
            if(confirmationBody!=null)confirmationBody.text=Theme.Icons!=null?Mathf.CeilToInt(Settings.PreviewSecondsRemaining).ToString():Mathf.CeilToInt(Settings.PreviewSecondsRemaining)+"초 뒤 이전 화면 설정으로 돌아갑니다.";
        }
        void QuitApplication()
        {
            if(Session!=null&&Session.SaveBlocked)
            {
                Confirm("저장하지 않고 종료할까요?","손상된 저장 원본은 그대로 보관된다. 이번 임시 플레이의 변경 내용은 저장되지 않는다.",ExitProcess);
                return;
            }
            if(Session!=null&&!Session.SaveNow(out var error)){ShowNotice("저장하지 못했다.\n"+error,8);return;}
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
