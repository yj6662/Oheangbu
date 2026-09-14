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

        void BuildTitle()
        {
            V.Clear(baseLayer);V.Clear(modalLayer);Page="";
            V.Raw(V.Stretch("황경",baseLayer),Theme.TitleBackdrop,Color.white);
            V.Image(V.Stretch("BackdropShade",baseLayer),new Color(.12f,.10f,.075f,.20f));
            var title=V.Rect("TitleCard",baseLayer,102,74,488,904);titleCard=title;
            V.Image(V.Stretch("Paper",title),new Color(Theme.Paper.r,Theme.Paper.g,Theme.Paper.b,.94f));
            V.Raw(V.Stretch("Fiber",title),Theme.PaperTexture,new Color(1,1,1,.18f));
            V.Text(title,"Seal","五\n行\n符",Theme.Font,25,Theme.Seal,364,56,60,155,TextAnchor.UpperCenter);
            V.Text(title,"Subtitle","먹으로 여는 길",Theme.Font,22,Theme.Muted,42,65,290,44);
            V.Text(title,"Title","오행부",Theme.Font,84,Theme.Ink,34,126,390,122);
            V.Rule(title,Theme,42,286,386);
            var info=WorldMacroSaveSlot.Inspect(Application.persistentDataPath,ActiveSlotName);
            bool canContinue=info.Status==WorldMacroSaveSlotStatus.Primary||info.Status==WorldMacroSaveSlotStatus.Backup||info.Status==WorldMacroSaveSlotStatus.Temporary;
            string firstJourney=Content!=null&&Content.Opening!=null&&Content.Opening.Enabled?"마을 관청에서 첫 의뢰를 확인합니다.":"폐광에서 황경까지, 첫 여정을 시작합니다.";
            string saved=canContinue?"남겨진 여정을 이어갑니다.":info.Status==WorldMacroSaveSlotStatus.Invalid?"저장 파일을 읽을 수 없습니다.\n새 게임을 시작하면 원본은 보관됩니다.":firstJourney;
            if(info.Status==WorldMacroSaveSlotStatus.Temporary)saved="중단된 저장을 발견했습니다.\n이어하기로 복구할 수 있습니다.";
            if(info.Status==WorldMacroSaveSlotStatus.Backup)saved="이전 백업으로 여정을 복구할 수 있습니다.";
            V.Text(title,"SaveStatus",saved,Theme.Font,20,info.Status==WorldMacroSaveSlotStatus.Invalid?Theme.Seal:Theme.Muted,44,318,384,104);
            var resume=V.Button(title,"Continue","이어하기",Theme,42,454,386,60,()=>StartCoroutine(LoadPlay()),true);resume.interactable=canContinue;
            V.Button(title,"NewGame","새 게임",Theme,42,530,386,54,()=>
            {
                if(info.Status==WorldMacroSaveSlotStatus.New)StartNew();
                else Confirm("새 여정을 시작할까요?","기존 진행은 별도 백업으로 보관하고 새 게임을 시작합니다.",StartNew);
            });
            V.Button(title,"Options","옵션",Theme,42,598,386,54,()=>OpenPage("옵션"));
            V.Button(title,"Controls","조작 안내",Theme,42,666,386,54,()=>OpenPage("조작 안내"));
            V.Button(title,"Quit","종료",Theme,42,734,386,54,()=>Confirm("게임을 종료할까요?","남겨진 여정은 다음에 이어갈 수 있습니다.",QuitApplication));
            V.Text(title,"Edition","플레이테스트  ·  2026.09.15",Theme.Font,17,Theme.Muted,44,834,382,34);
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
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
            Busy=true;Gate.Block();DismissConfirmation();Settings.Revert();
            ShowLoading("여정을 준비합니다.");
            Time.timeScale=1;
            var load=SceneManager.LoadSceneAsync(PlayScene,LoadSceneMode.Single);
            if(load==null){Busy=false;LastError="플레이 장면을 불러올 수 없습니다.";BuildTitle();ShowNotice(LastError,8);yield break;}
            while(!load.isDone){if(loadingText!=null)loadingText.text="여정을 준비합니다.  "+Mathf.RoundToInt(Mathf.Clamp01(load.progress/.9f)*100)+"%";yield return null;}
            Busy=false;bound=false;currentSceneHandle=-1;
        }
        IEnumerator ReturnTitle(bool leaveBlockedSession=false)
        {
            if(Busy)yield break;
            if(Session!=null&&Session.SaveBlocked&&!leaveBlockedSession)
            {
                Confirm("저장하지 않고 로비로 돌아갈까요?","손상된 저장 원본은 그대로 보관됩니다. 이번 임시 플레이의 변경 내용은 저장되지 않습니다.",()=>StartCoroutine(ReturnTitle(true)));
                yield break;
            }
            if(Session!=null&&!Session.SaveBlocked&&!Session.SaveNow(out var error))
            {LastError=error;ShowNotice("저장하지 못했습니다.\n"+error,8);yield break;}
            Busy=true;Settings.Revert();UnhookSession();SetHud(true);ShowLoading(leaveBlockedSession?"저장 원본을 보존하고 돌아갑니다.":"여정을 기록했습니다.");
            if(Map!=null){Destroy(Map.gameObject);Map=null;}
            Pause.End();Gate.Block();Time.timeScale=1;
            var load=SceneManager.LoadSceneAsync(TitleScene,LoadSceneMode.Single);
            if(load==null){Busy=false;ShowNotice("로비를 불러올 수 없습니다.",8);yield break;}
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
            V.Button(dialog,"Cancel","돌아가기",Theme,44,282,330,56,DismissConfirmation);
            V.Button(dialog,"Accept","확인",Theme,412,282,352,56,()=>{DismissConfirmation();accept?.Invoke();},true);
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
            if(confirmationBody!=null)confirmationBody.text=Mathf.CeilToInt(Settings.PreviewSecondsRemaining)+"초 뒤 이전 화면 설정으로 돌아갑니다.";
        }
        void QuitApplication()
        {
            if(Session!=null&&Session.SaveBlocked)
            {
                Confirm("저장하지 않고 종료할까요?","손상된 저장 원본은 그대로 보관됩니다. 이번 임시 플레이의 변경 내용은 저장되지 않습니다.",ExitProcess);
                return;
            }
            if(Session!=null&&!Session.SaveNow(out var error)){ShowNotice("저장하지 못했습니다.\n"+error,8);return;}
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
