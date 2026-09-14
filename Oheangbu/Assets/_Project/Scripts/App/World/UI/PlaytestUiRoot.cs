using System;
using System.Collections;
using Oheangbu.Core;
using Oheangbu.Data.World;
using Oheangbu.App.World.Vehicle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>Only installed in the playtest/title scenes. Owns the one menu stack across scene loads.</summary>
    public sealed partial class PlaytestUiRoot : MonoBehaviour
    {
        public const string TitleScene="W_Playtest_Title";
        public const string PlayScene="W_WorldMacro_Playtest";
        public static PlaytestUiRoot Instance {get;private set;}
        public PlaytestUiThemeSO Theme;
        public WorldMacroSheetSO WorldSheet;
        public WorldMapBakedDataSO MapData;
        public WorldMacroPlaytestSO Content;
        public InputActionAsset InputActions;
        public GameplayRuntimeStateSO RuntimeState;
        public UserSettingsService Settings {get;private set;}
        public PauseCoordinator Pause {get;private set;}
        public GameplayUiGate Gate {get;private set;}
        public WorldMapPresenter Map {get;private set;}
        public WorldMacroPlaytestSession Session {get;private set;}
        public string Page {get;private set;}="";
        public bool Busy {get;private set;}
        public bool IsTitle => SceneManager.GetActiveScene().name==TitleScene;
        public string ActiveSlotName => Content.SaveSlot + DiagnosticSuffix;
        public static string DiagnosticSuffix
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetString("PlaytestUiReviewSuffix", "");
#else
                const string prefix="--ui-test-slot=";
                foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith(prefix,StringComparison.Ordinal))
                {
                    string suffix=arg.Substring(prefix.Length);
                    if(suffix.Length<=64&&System.Text.RegularExpressions.Regex.IsMatch(suffix,"^_[a-zA-Z0-9_-]+$"))return suffix;
                }
                return "";
#endif
            }
        }
        public bool IsMenuOpen => IsTitle || Page.Length>0 || closingMap || Busy;
        public string LastError {get;private set;}="";
        Canvas canvas;CanvasScaler scaler;RectTransform canvasRect,baseLayer,modalLayer,mapLayer,contentRoot,frame,noticeRoot,titleCard;
        Text heading,subheading,noticeText,statusText;WorldMacroUiAudioVoices uiAudio;
        HudController hud;Canvas[] hudCanvases;InputActionMap menuActions;InputAction pauseKey,inventoryKey,mapKey;
        bool bound,closingMap,settingsBuilt;float noticeUntil,nextStatus;int currentSceneHandle=-1;
        string selectedItem="",selectedSpell="";string detailTitle,detailBody;
        static readonly string[] GameplayPages={"일시정지","소지품","술식 도감","차패","지도","옵션","조작 안내"};
        static readonly string[] TitlePages={"옵션","조작 안내"};
        UserSettingsData settingsSnapshot;

        void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}
            Instance=this;DontDestroyOnLoad(gameObject);
            if(Theme==null||Theme.Font==null){Debug.LogError("Playtest UI requires its bundled theme/font.");enabled=false;return;}
            ApplyDiagnosticSuffix();
            Gate=GetComponent<GameplayUiGate>()??gameObject.AddComponent<GameplayUiGate>();Gate.State=RuntimeState;Gate.Initialize();
            Pause=GetComponent<PauseCoordinator>()??gameObject.AddComponent<PauseCoordinator>();Pause.Gate=Gate;Pause.Initialize();
            Settings=GetComponent<UserSettingsService>()??gameObject.AddComponent<UserSettingsService>();Settings.RuntimeState=RuntimeState;Settings.AudioMix=Theme.AudioMix;Settings.Initialize();
            var go=new GameObject("Playtest_MenuCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=80;
            scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;canvasRect=(RectTransform)go.transform;
            baseLayer=V.Stretch("TitleBackground",canvasRect);modalLayer=V.Stretch("MenuLayer",canvasRect);mapLayer=V.Stretch("MapLayer",canvasRect);
            noticeRoot=V.Rect("Notice",canvasRect,0,0,730,120);noticeRoot.anchorMin=noticeRoot.anchorMax=new Vector2(.5f,1);noticeRoot.pivot=new Vector2(.5f,1);noticeRoot.anchoredPosition=new Vector2(0,-48);
            V.Image(V.Stretch("NoticePaper",noticeRoot),Theme.Paper,Theme.PromptPaper);
            noticeText=V.Text(noticeRoot,"Text","",Theme.Font,23,Theme.Ink,40,12,650,96,TextAnchor.MiddleCenter);noticeRoot.gameObject.SetActive(false);
            uiAudio=GetComponent<WorldMacroUiAudioVoices>()??gameObject.AddComponent<WorldMacroUiAudioVoices>();
            uiAudio.ApplySettings(Theme.AudioMix,Settings.Current.MasterVolume,Settings.Current.GameplayVolume,Settings.Current.UiVolume);
            EnsureEventSystem();
            menuActions=InputActions!=null?InputActions.FindActionMap("Menus",false):null;
            if(menuActions!=null){pauseKey=menuActions.FindAction("Pause",false);inventoryKey=menuActions.FindAction("Inventory",false);mapKey=menuActions.FindAction("Map",false);menuActions.Enable();}
            SceneManager.sceneLoaded+=OnSceneLoaded;Settings.Changed+=SettingsChanged;
        }
        void Start(){BindScene();}
        void OnSceneLoaded(Scene scene,LoadSceneMode mode){ApplyDiagnosticSuffix();bound=false;currentSceneHandle=-1;}
        static void ApplyDiagnosticSuffix()
        {
            string suffix=DiagnosticSuffix;if(string.IsNullOrEmpty(suffix))return;
            var session=FindFirstObjectByType<WorldMacroPlaytestSession>();if(session!=null)session.TestSaveSuffix=suffix;
        }
        void EnsureEventSystem()
        {
            var systems=FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            EventSystem system=null;
            foreach(var candidate in systems)
            {
                if(candidate.transform.IsChildOf(transform)){system=candidate;break;}
            }
            if(system==null)
            {
                var go=new GameObject("Playtest_EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));go.transform.SetParent(transform,false);
                system=go.GetComponent<EventSystem>();go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            foreach(var candidate in systems)if(candidate!=system)candidate.gameObject.SetActive(false);
        }
        void BindScene()
        {
            if(Busy)return;
            var scene=SceneManager.GetActiveScene();
            if(currentSceneHandle==scene.handle&&bound)return;
            UnhookSession();
            if(Map!=null){Destroy(Map.gameObject);Map=null;}
            V.Clear(baseLayer);V.Clear(modalLayer);V.Clear(mapLayer);Page="";closingMap=false;
            Session=FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(!IsTitle&&(Session==null||Session.Progress==null))return;
            currentSceneHandle=scene.handle;bound=true;
            EnsureEventSystem();
            if(IsTitle)
            {
                Gate.Block();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;BuildTitle();
            }
            else
            {
                Pause.Drawing=Session.Walker.Drawing;
                Pause.PalanquinSeat=FindFirstObjectByType<WorldMacroPalanquinSeat>();
                Pause.PalanquinController=FindFirstObjectByType<WorldMacroPalanquinController>();
                Pause.Initialize();
                Session.DetailRequested+=ShowDetail;Session.CollectionChanged+=OnCollected;
                hud=FindFirstObjectByType<HudController>();hudCanvases=hud!=null?hud.GetComponentsInChildren<Canvas>(true):null;
                var mapGo=new GameObject("PaperWorldMap");mapGo.transform.SetParent(mapLayer,false);Map=mapGo.AddComponent<WorldMapPresenter>();
                Map.Initialize(mapLayer,Session,WorldSheet,new WorldMapUiDependencies{BakedData=MapData,Font=Theme.Font,PaperTexture=Theme.PaperTexture,Ink=Theme.Ink,Paper=Theme.Paper,Muted=Theme.Muted,Seal=Theme.Seal,ReducedMotion=Settings.Current.ReducedMotion});
                Map.CloseRequested+=CloseMenu;
                Map.FoldRustle+=volume=>PlayUi(Theme.PaperSound,volume);
                Gate.ReleaseWhenNeutral();Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
                if(!TryShowOpeningIntroduction())ShowNotice("[M] 지도    [I] 소지품    [Esc] 메뉴",4);
            }
            SettingsChanged();
        }
        void UnhookSession()
        {
            if(Session==null)return;Session.DetailRequested-=ShowDetail;Session.CollectionChanged-=OnCollected;
        }
        void Update()
        {
            if(!bound||currentSceneHandle!=SceneManager.GetActiveScene().handle)BindScene();
            if(noticeRoot!=null&&noticeRoot.gameObject.activeSelf&&Time.unscaledTime>=noticeUntil)noticeRoot.gameObject.SetActive(false);
            if(!bound||Busy)return;
            if(closingMap&&Map!=null&&!Map.Folding){closingMap=false;FinishClose();}
            if(Map!=null)
            {
                Map.SetVisible(Page=="지도"||closingMap||(settingsSnapshot.ShowMinimap&&Page.Length==0));
                Map.ReducedMotion=settingsSnapshot.ReducedMotion;
            }
            if(pauseKey!=null&&pauseKey.WasPressedThisFrame())Back();
            else if(confirmationRoot==null&&!IsTitle&&inventoryKey!=null&&inventoryKey.WasPressedThisFrame())
            {if(Page=="소지품")CloseMenu();else OpenPage("소지품");}
            else if(confirmationRoot==null&&!IsTitle&&mapKey!=null&&mapKey.WasPressedThisFrame())
            {if(Page=="지도")CloseMenu();else OpenPage("지도");}
            if(Time.unscaledTime>=nextStatus){nextStatus=Time.unscaledTime+.25f;RefreshStatus();}
            UpdateDisplayConfirmation();
        }
        public void OpenPage(string page)
        {
            // Reject removed/unknown routes before changing pause, save, or the current page.
            if(!IsSupportedPage(page,IsTitle))return;
            if(Busy||closingMap||confirmationRoot!=null||(!IsTitle&&Session==null))return;
            if(!IsTitle&&Page.Length==0)
            {
                Pause.Begin();
                if(!Session.SaveNow(out var error)&&!string.IsNullOrEmpty(error))LastError=error;
            }
            if(Map!=null&&Page=="지도"&&page!="지도")Map.SetExpanded(false);
            Page=page;SetHud(false);V.Clear(modalLayer);settingsBuilt=false;
            if(page=="지도")
            {
                if(Map==null)return;
                Map.SetVisible(true);Map.ShowWholeWorld();Map.SetExpanded(true);PlayUi(Theme.PaperSound,.45f);return;
            }
            BuildFrame(page);
            switch(page)
            {
                case "일시정지": BuildPause();break;
                case "소지품": BuildInventory();break;
                case "술식 도감": BuildCodex();break;
                case "차패": BuildChapae();break;
                case "옵션": BuildOptions();break;
                case "조작 안내": BuildControls();break;
                case "상세": BuildDetail();break;
            }
            ApplyTextScale();PlayUi(Theme.ConfirmSound,.40f);
        }
        public static bool IsSupportedPage(string page,bool titleScene)
        {
            return Array.IndexOf(titleScene?TitlePages:GameplayPages,page)>=0||(!titleScene&&page=="상세");
        }
        public void CloseMenu()
        {
            if(Busy)return;
            if(Settings.IsPreviewing){Settings.Revert();}
            if(Page=="지도"&&Map!=null)
            {Map.SetExpanded(false);closingMap=true;PlayUi(Theme.PaperSound,.30f);return;}
            FinishClose();
        }
        void FinishClose()
        {
            V.Clear(modalLayer);Page="";settingsBuilt=false;
            if(IsTitle){BuildTitle();return;}
            Pause.End();SetHud(true);PlayUi(Theme.BackSound,.35f);
        }
        public void Back()
        {
            if(Busy||closingMap)return;
            if(confirmationRoot!=null){DismissConfirmation();return;}
            if(IsTitle){if(Page.Length>0)CloseMenu();return;}
            if(Page.Length>0)CloseMenu();else OpenPage("일시정지");
        }
        void SetHud(bool show){if(hudCanvases!=null)foreach(var c in hudCanvases)if(c!=null)c.enabled=show;}
        void ShowDetail(string title,string body){detailTitle=title;detailBody=body;OpenPage("상세");}
        void OnCollected(WorldMacroCollectionNotice notice)
        {
            ShowNotice(notice.Title+"\n"+notice.Message,5);PlayUi(Theme.ConfirmSound,.65f);
            if(Page=="소지품"||Page=="술식 도감")OpenPage(Page);
        }
        public void ShowNotice(string text,float seconds=4)
        {noticeText.text=text;noticeRoot.gameObject.SetActive(true);noticeUntil=Time.unscaledTime+seconds;noticeRoot.SetAsLastSibling();}
        void PlayUi(AudioClip clip,float gain)
        {
            if(clip==null||uiAudio==null)return;
            uiAudio.ApplySettings(Theme.AudioMix,Settings.Current.MasterVolume,Settings.Current.GameplayVolume,Settings.Current.UiVolume);
            uiAudio.Play(clip,gain);
        }
        void SettingsChanged()
        {
            settingsSnapshot=Settings.Current;
            uiAudio?.ApplySettings(Theme.AudioMix,settingsSnapshot.MasterVolume,settingsSnapshot.GameplayVolume,settingsSnapshot.UiVolume);
            if(scaler!=null)scaler.referenceResolution=new Vector2(1920,1080)/Mathf.Clamp(settingsSnapshot.UiScale,.8f,1.3f);
            ApplyTextScale();
        }
        void LateUpdate()
        {
            if(canvasRect==null)return;
            Vector2 size=canvasRect.rect.size;
            if(frame!=null)frame.localScale=Vector3.one*Mathf.Min(1f,(size.x-64)/1560f,(size.y-64)/900f);
            if(titleCard!=null)
            {
                titleCard.localScale=Vector3.one*Mathf.Min(1f,(size.y-70)/904f);
                titleCard.anchorMin=titleCard.anchorMax=new Vector2(0,.5f);titleCard.pivot=new Vector2(0,.5f);
                titleCard.anchoredPosition=new Vector2(Mathf.Min(102,size.x*.07f),0);
            }
        }
        void ApplyTextScale()
        {
            // Each page owns an immutable base size so repeated previews never compound scaling.
            float scale=Settings.Current.TextScale;
            foreach(var text in GetComponentsInChildren<Text>(true))
            {
                var baseSize=text.GetComponent<UiTextBaseSize>();if(baseSize==null){baseSize=text.gameObject.AddComponent<UiTextBaseSize>();baseSize.Size=text.fontSize;}
                text.fontSize=Mathf.RoundToInt(baseSize.Size*(baseSize.Size<=28?scale:1));
            }
        }
        void RefreshStatus()
        {
            if(settingsErrorText!=null)settingsErrorText.text=Settings.SaveError??"";
            if(statusText==null||Session==null)return;
            statusText.text=!string.IsNullOrEmpty(Session.SaveError)?Session.SaveError:
                "조선통보 "+Session.Progress.ledger.currency.ToString("N0")+"  ·  "+Session.CheckpointDisplayName+"에서 재시작";
        }
        void BuildFrame(string title)
        {
            V.Image(V.Stretch("Dim",modalLayer),new Color(.04f,.04f,.035f,.68f),null,true);
            frame=V.Rect("Folio",modalLayer,0,0,1560,900);frame.anchorMin=frame.anchorMax=frame.pivot=new Vector2(.5f,.5f);frame.anchoredPosition=Vector2.zero;
            V.Image(V.Stretch("Backing",frame),Theme.Paper,null,true);
            if(Theme.PaperTexture!=null)V.Raw(V.Stretch("Hanji",frame),Theme.PaperTexture,new Color(1,1,1,.23f));
            V.Text(frame,"Brand","五 行 符",Theme.Font,24,Theme.Seal,44,42,230,46);
            V.Rule(frame,Theme,44,98,214);
            string[] pages=IsTitle?TitlePages:GameplayPages;
            for(int i=0;i<pages.Length;i++)
            {string label=pages[i];V.Button(frame,"Tab_"+label,label=="차패"?"오행부":label,Theme,44,135+i*66,214,54,()=>OpenPage(label),label==title);}
            V.Button(frame,"Close",IsTitle?"로비로":"여정으로",Theme,44,802,214,54,CloseMenu);
            V.Image(V.Rect("Divider",frame,292,42,1,814),new Color(.2f,.18f,.14f,.20f));
            heading=V.Text(frame,"PageHeading",title=="상세"?detailTitle:title=="차패"?"오행부":title,Theme.Font,46,Theme.Ink,338,42,1100,70);
            subheading=V.Text(frame,"Subheading","",Theme.Font,20,Theme.Muted,340,117,1060,40);
            V.Rule(frame,Theme,338,170,1168);
            contentRoot=V.Rect("PageContent",frame,338,198,1168,600);
            statusText=V.Text(frame,"Status","",Theme.Font,18,Theme.Muted,338,826,1050,34);RefreshStatus();
        }
        void BuildPause()
        {
            subheading.text="잠시 여정을 멈춥니다.";
            V.Text(contentRoot,"RestTitle","먹을 고르고, 길을 살피다",Theme.Font,36,Theme.Ink,0,32,1050,64);
            V.Text(contentRoot,"PauseCopy","소지품과 술식 도감을 살피거나\n지도를 펼쳐 지나온 길을 확인할 수 있습니다.",Theme.Font,25,Theme.Muted,0,116,970,112);
            V.Button(contentRoot,"Resume","여정 계속하기",Theme,0,290,450,64,CloseMenu,true);
            V.Button(contentRoot,"ReturnTitle","저장하고 로비로",Theme,0,378,450,60,()=>Confirm("로비로 돌아갈까요?","현재 진행을 저장한 뒤 로비로 돌아갑니다.",()=>StartCoroutine(ReturnTitle())));
        }
        void BuildControls()
        {
            subheading.text="키보드와 마우스";
            string[,] rows={{"W · A · S · D","걷기 / 탑승 중 가속·조향"},{"Ctrl + 이동","달리기"},{"Space","지상 점프 / 탑승 중 제동"},{"C / X","웅크리기 전환 / 바닥 착석·일어나기"},{"마우스","시점 이동"},{"Q + 마우스 좌클릭","글씨 그리기 · Q를 놓으면 시전"},{"왼쪽 Shift / Tab","회피 (웅크림 중 구르기) / 대상 락온"},{"마우스 좌클릭 유지","비작도 상태에서 먹 갈무리"},{"F","조사 · 대화 · 석경 파편 획득"},{"E / V","마법가마 탑승·하차 / 탑승 시점 전환"},{"G","오행부로 자동차 호출 · 하차 후 30m 자동 회수"},{"M / I / Esc","지도 / 소지품 / 일시정지·뒤로"}};
            var content=V.Scroll(contentRoot,"ControlScroll",0,0,1130,600,rows.GetLength(0)*69);
            for(int i=0;i<rows.GetLength(0);i++){V.Text(content,"Key",rows[i,0],Theme.Font,22,Theme.Seal,0,i*69,455,54);V.Text(content,"Action",rows[i,1],Theme.Font,23,Theme.Ink,475,i*69,620,54);V.Rule(content,Theme,0,i*69+59,1080);}
        }
        void BuildDetail()
        {
            subheading.text=Session!=null&&!string.IsNullOrEmpty(Session.SaveError)?"내용을 읽고 있습니다. 저장 상태를 확인해 주세요.":"내용을 확인한 뒤 여정을 계속하세요.";
            var content=V.Scroll(contentRoot,"DetailScroll",0,0,1100,470,550);
            V.Text(content,"Body",detailBody,Theme.Font,30,Theme.Ink,12,30,1030,500);
            V.Button(contentRoot,"Done","확인",Theme,0,510,330,56,CloseMenu,true);
        }
        void OnDestroy()
        {
            if(Instance!=this)return;
            UnhookSession();SceneManager.sceneLoaded-=OnSceneLoaded;
            if(Settings!=null)Settings.Changed-=SettingsChanged;
            menuActions?.Disable();if(Pause!=null)Pause.End();Instance=null;
        }
    }
    public sealed class UiTextBaseSize:MonoBehaviour{public int Size;}
}
