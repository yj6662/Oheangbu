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
        public const string TitleScene="W_Demo_Compact_Title";
        public const string PlayScene="W_Demo_Compact";
        public string TitleSceneName=TitleScene;
        public string PlaySceneName=PlayScene;
        public static PlaytestUiRoot Instance {get;private set;}
        public PlaytestUiThemeSO Theme;
        public Texture2D LobbyIllustration;
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
        public bool IsTitle => SceneManager.GetActiveScene().name==TitleSceneName;
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
        /// <summary>#306 §2-2: frame of the last modal close (PauseCoordinator resume). Interaction polling must ignore the F that
        /// closed a dialogue: ModalClosedRecently = this frame or the next.</summary>
        public static int LastModalCloseFrame => PauseCoordinator.LastResumeFrame;
        public static bool ModalClosedRecently => PauseCoordinator.ResumedRecently;
        /// <summary>#306 smoke: root, gate, session, drawing, motor and seat share one GameplayRuntimeStateSO.</summary>
        public bool RuntimeStatesAligned(out string mismatch)
        {
            if(Gate==null||Pause==null){mismatch="gate/pause missing";return false;}
            if(Gate.State!=RuntimeState){mismatch="gate";return false;}
            if(!IsTitle&&bound&&Pause.Session==null){mismatch="session unbound";return false;}   // a gameplay bind must hand the session over
            return Pause.StatesAligned(out mismatch);
        }
        public string LastError {get;private set;}="";
        // #304: contentRoot = the page content root in 1920x1080 page px with a TOP-LEFT origin (V.Rect coordinates = the mockups);
        // frame is only set by legacy windows (trade) that still size themselves; heading / subheading / statusText are hidden
        // legacy Text kept so older partials compile (writes are harmless, nothing shows them).
        Canvas canvas;CanvasScaler scaler;RectTransform canvasRect,baseLayer,modalLayer,mapLayer,contentRoot,frame,noticeRoot,titleCard;
        Text heading,subheading,noticeText,statusText;WorldMacroUiAudioVoices uiAudio;
        HudController hud;Canvas[] hudCanvases;InputActionMap menuActions;InputAction pauseKey,inventoryKey,mapKey;
        bool bound,closingMap,settingsBuilt;float nextStatus;int currentSceneHandle=-1;
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
            V.EnsureCanvasChannels(canvas);   // #304: InkReveal / InkMeter read their payload from TexCoord1
            baseLayer=V.Stretch("TitleBackground",canvasRect);modalLayer=V.Stretch("MenuLayer",canvasRect);mapLayer=V.Stretch("MapLayer",canvasRect);
            menu304RailLayer=V.Stretch("MenuRailLayer304",canvasRect);   // the 지도 page's rail sits above the map
            // #304: "Notice" stays as the harness root; it now hosts the toast stack (MenuToastStack304) and a hidden legacy Text
            // that mirrors the last ShowNotice text for older checks. Nothing on it draws paper any more.
            noticeRoot=V.Stretch("Notice",canvasRect);
            noticeText=V.Text(noticeRoot,"Text","",Theme.Font,23,Theme.Ink,0,0,10,10,TextAnchor.MiddleCenter);noticeText.enabled=false;
            menu304Toasts=noticeRoot.gameObject.AddComponent<MenuToastStack304>();
            menu304Toasts.Bind(V.Style(Theme),Menu304ToastsWait,Menu304Drawing);
            uiAudio=GetComponent<WorldMacroUiAudioVoices>()??gameObject.AddComponent<WorldMacroUiAudioVoices>();
            uiAudio.ApplySettings(Theme.AudioMix,Settings.Current.MasterVolume,Settings.Current.GameplayVolume,Settings.Current.UiVolume);
            EnsureEventSystem();
            menuActions=InputActions!=null?InputActions.FindActionMap("Menus",false):null;
            if(menuActions!=null){pauseKey=menuActions.FindAction("Pause",false);inventoryKey=menuActions.FindAction("Inventory",false);mapKey=menuActions.FindAction("Map",false);menuActions.Enable();}
            SceneManager.sceneLoaded+=OnSceneLoaded;Settings.Changed+=SettingsChanged;
        }
        void Start(){BindScene();}
        // #307 (user 2026-10-01 "새 게임 시작해도 이어하기로 들어가고"): while this root loads the play scene from the title (Busy), the
        // session must use exactly the slot the title inspected and archived (ActiveSlotName), even when that suffix is empty: a stale
        // TestSaveSuffix on the scene's session (an editor harness value restored by Play-exit) made 새 게임 archive one file and the
        // session resume another.
        void OnSceneLoaded(Scene scene,LoadSceneMode mode){ApplyDiagnosticSuffix(Busy);bound=false;currentSceneHandle=-1;}
#if UNITY_EDITOR
        /// <summary>#307 validation switch (editor only, never saved): true = the old rule (an empty suffix never reaches the session).</summary>
        public static bool SlotInvariantOff307;
#endif
        static void ApplyDiagnosticSuffix(bool fromTitle=false)
        {
#if UNITY_EDITOR
            if(SlotInvariantOff307)fromTitle=false;   // Restart307 userpath:legacy only — reproduces the pre-#307 early return
#endif
            string suffix=DiagnosticSuffix;if(string.IsNullOrEmpty(suffix)&&!fromTitle)return;
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
            V.Clear(baseLayer);V.Clear(modalLayer);V.Clear(mapLayer);Menu304ClearRailLayer();Page="";closingMap=false;Menu304ResetPageState();
            Session=FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(!IsTitle&&(Session==null||Session.Progress==null))return;
            currentSceneHandle=scene.handle;bound=true;
            EnsureEventSystem();
            if(menu304Toasts!=null){menu304Toasts.Clear();menu304Toasts.Bind(V.Style(Theme),Menu304ToastsWait,Menu304Drawing);}
            if(IsTitle)
            {
                Pause.Session=null;Gate.Block();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;BuildTitle();
            }
            else
            {
                Pause.Drawing=Session.Walker.Drawing;
                Pause.PalanquinSeat=FindFirstObjectByType<WorldMacroPalanquinSeat>();
                Pause.PalanquinController=FindFirstObjectByType<WorldMacroPalanquinController>();
                Pause.Session=Session;   // #306: the session polls F against the live root's state SO, not its scene copy
                Pause.Initialize();
                Content=Session.Content;
                Session.EquipmentServiceRequested+=OpenEquipmentService;Session.DetailRequested+=ShowDetail;Session.CollectionChanged+=OnCollected;Session.InteractionResolved+=OnDemoShopInteraction;
                Session.NoticeRaised+=Menu304OnSessionNotice;   // #304: filtered into the toast channel (wake, realm, vehicle, recovery, errors)
                Session.DialogueRequested+=OnDialogueRequested306;Session.DialogueViewBound=true;   // #306 §2-3: every NPC utterance -> DialogueView304 (unhooked on rebind)
                Tutorial306Hook();   // #306 #11: mine tutorial cards
                Content304SessionBound();   // #304 integration: D21 new-content marks count from the start of this session
                hud=FindFirstObjectByType<HudController>();hudCanvases=hud!=null?hud.GetComponentsInChildren<Canvas>(true):null;
                var mapGo=new GameObject("PaperWorldMap");mapGo.transform.SetParent(mapLayer,false);Map=mapGo.AddComponent<WorldMapPresenter>();
                Map.Initialize(mapLayer,Session,WorldSheet,new WorldMapUiDependencies{Theme=Theme,Icons=Theme.Icons,BakedData=MapData,Font=Theme.Font,PaperTexture=Theme.PaperTexture,Ink=Theme.Ink,Paper=Theme.Paper,Muted=Theme.Muted,Seal=Theme.Seal,ReducedMotion=Settings.Current.ReducedMotion});
                Map.CloseRequested+=CloseMenu;
                Map.FoldRustle+=volume=>PlayUi(Theme.PaperSound,volume);
                if(LoadingInProgress)Gate.Block();
                else {Gate.ReleaseWhenNeutral();Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
                // #304: the "[M] 지도 [I] 소지품 [Esc] 메뉴" hint notice is gone (DESIGN §5.10: no instruction notices)
                if(!LoadingInProgress)TryShowOpeningIntroduction();
            }
            SettingsChanged();
        }
        void UnhookSession()
        {
            ResetDemoEnding();
            // #306: a conversation never outlives its session (the page resets); Closed runs once, never into an unloaded session
            {var ended=Dialogue304Detach(false);if(Session!=null)Dialogue304Ended(ended);}
            Tutorial306Unhook();
            if(Session==null)return;Session.EquipmentServiceRequested-=OpenEquipmentService;Session.DetailRequested-=ShowDetail;Session.CollectionChanged-=OnCollected;Session.InteractionResolved-=OnDemoShopInteraction;
            Session.NoticeRaised-=Menu304OnSessionNotice;Session.DialogueRequested-=OnDialogueRequested306;Session.DialogueViewBound=false;
        }
        void Update()
        {
            if(!bound||currentSceneHandle!=SceneManager.GetActiveScene().handle)BindScene();
            Dialogue304Heal();
            if(!bound||Busy||(Session!=null&&Session.RestPresentationActive))return;
            if(closingMap&&Map!=null&&!Map.Folding){closingMap=false;FinishClose();}
            if(TryPresentDemoEnding())
            {
                if(pauseKey!=null&&pauseKey.WasPressedThisFrame()&&confirmationRoot!=null)DismissConfirmation();
                return;
            }
            if(Map!=null)
            {
                // #304: no persistent minimap yet (#306 batch 2 restores HudMinimap304); the map shows only as the 지도 page
                Map.SetVisible(Page=="지도"||closingMap);
                Map.ReducedMotion=settingsSnapshot.ReducedMotion;
            }
            Tutorial306Tick();
            if(pauseKey!=null&&pauseKey.WasPressedThisFrame())Back();
            else if(confirmationRoot==null&&!IsTitle&&!Dialogue304Open&&inventoryKey!=null&&inventoryKey.WasPressedThisFrame())
            {if(Page=="소지품")CloseMenu();else OpenPage("소지품");}
            else if(confirmationRoot==null&&!IsTitle&&!Dialogue304Open&&mapKey!=null&&mapKey.WasPressedThisFrame())   // #306: no I / M mid-conversation
            {if(Page=="지도")CloseMenu();else OpenPage("지도");}
            else Menu304Tick();
            if(Time.unscaledTime>=nextStatus){nextStatus=Time.unscaledTime+.25f;RefreshStatus();}
            UpdateDisplayConfirmation();
        }
        public void OpenPage(string page)
        {
            if(ShowingDemoEnding||(Session!=null&&Session.RestPresentationActive))return;
            // Reject removed/unknown routes before changing pause, save, or the current page.
            if(!IsSupportedPage(page,IsTitle))return;
            if((page=="장비 상점"||page=="장비 강화")&&(Session==null||!Session.NearEquipmentService(page=="장비 상점"?"village_shop":"village_artisan")))return;
            if(page=="정비"&&(Session==null||!Session.AtDemoShop))return;
            if(Busy||closingMap||confirmationRoot!=null||(!IsTitle&&Session==null))return;
            if(!IsTitle&&Page.Length==0)
            {
                Pause.Begin();
                if(!Session.SaveNow(out var error)&&!string.IsNullOrEmpty(error))LastError=error;
            }
            if(Map!=null&&Page=="지도"&&page!="지도")Map.SetExpanded(false);
            string previous=Page;
            var reselect=Menu304CaptureSelection();
            Page=page;SetHud(false);V.Clear(modalLayer);Menu304ClearRailLayer();settingsBuilt=false;
            if(page==TutorialPage306){BuildTutorialCard306();PlayUi(Theme.PaperSound,.40f);return;}   // #306 #11 TutorialCard304
            if(page=="지도")
            {
                if(Map==null)return;
                Map.SetVisible(true);Map.ShowWholeWorld();
                // #304 integration: tab change (a menu page with its own veil was open) = no veil wipe; same rule as BuildFrame
                Map.OpenedFromPage304=previous.Length>0&&previous!="상세"&&previous!="장비 상점"&&previous!="장비 강화";
                Map.SetExpanded(true);PlayUi(Theme.PaperSound,.45f);
                Menu304BuildMapRail(previous);return;
            }
            if(page=="상세")
            {
                // #304 StoryBand304 (IMPLEMENTATION §7.9): one surface for both icon and text themes; #306 an open conversation
                // (DialogueView304) builds the same page root instead of the Present band
                if(Dialogue304Open)Dialogue304Build(true);else Menu304BuildStory();
                Menu304Shown(page);
                ApplyTextScale();if(!suppressNextPageSound255)PlayUi(Theme.ConfirmSound,.40f);suppressNextPageSound255=false;return;
            }
            if(page=="장비 상점"||page=="장비 강화")
            {
                BuildTradeFrame(page=="장비 강화");BuildEquipmentService(page=="장비 강화");
                ApplyTextScale();if(!suppressNextPageSound255)PlayUi(Theme.ConfirmSound,.40f);suppressNextPageSound255=false;return;
            }
            Menu304BeginPage(previous);
            BuildFrame(page);
            switch(page)
            {
                case "일시정지": BuildPause();break;
                case "소지품": BuildInventory();break;
                case "술식 도감": BuildCodex();break;
                case "차패": BuildChapae();break;
                case "옵션": BuildOptions();break;
                case "조작 안내": BuildControls();break;
                case "정비": BuildDemoShop();break;
            }
            ApplyTextScale();Menu304AfterBuild(page,previous,reselect);
            if(!suppressNextPageSound255)PlayUi(Theme.ConfirmSound,.40f);suppressNextPageSound255=false;
        }
        public static bool IsSupportedPage(string page,bool titleScene)
        {
            return Array.IndexOf(titleScene?TitlePages:GameplayPages,page)>=0||(!titleScene&&(page=="상세"||page=="정비"||page=="장비 상점"||page=="장비 강화"||page==TutorialPage306));
        }
        public void CloseMenu()
        {
            if(Busy||ShowingDemoEnding)return;
            if(Settings.IsPreviewing){Settings.Revert();}
            if(Page=="지도"&&Map!=null)
            {Map.SetExpanded(false);closingMap=true;Menu304ClearRailLayer();PlayUi(Theme.PaperSound,.30f);return;}
            FinishClose();
        }
        void FinishClose()
        {
            // #306: a window opened from the talk menu returns to the menu (the pause and the hidden HUD stay)
            if(Dialogue304Open&&dialogue304.InService&&!IsTitle){dialogue304.ReturnFromService();PlayUi(Theme.BackSound,.35f);return;}
            var ended=Dialogue304Detach(true);   // #306: the band fades out; Closed runs after the page is gone
            V.Clear(modalLayer);Menu304ClearRailLayer();Page="";settingsBuilt=false;Menu304ResetPageState();
            if(IsTitle){BuildTitle();Dialogue304Ended(ended);return;}
            Pause.End();SetHud(true);PlayUi(Theme.BackSound,.35f);Dialogue304Ended(ended);
            Tutorial306Ended(true);   // #306 #11: the director learns the card closed (and whether Esc was held)
        }
        public void Back()
        {
            if(Busy||closingMap)return;
            if(Tutorial306Back())return;   // #306 #11: Esc on the card = tap to close / hold to skip (Tutorial306Tick)
            if(confirmationRoot!=null){DismissConfirmation();return;}
            if(IsTitle){if(Page.Length>0)CloseMenu();return;}
            if(Page=="상세"&&Dialogue304Open){dialogue304.Close();return;}   // #306: Esc = 떠나기
            if(Page=="상세"&&Menu304StoryBack())return;
            if(Page=="소지품"&&Equipment308Back())return;   // #308: Esc in the middle column = back to the slot (the menu stays)
            if(Page.Length>0)CloseMenu();else OpenPage("일시정지");
        }
        void SetHud(bool show)
        {
            // re-read the HUD's canvases: #304 HUD parts may be (re)built after the scene bind
            if(hud!=null)hudCanvases=hud.GetComponentsInChildren<Canvas>(true);
            if(hudCanvases!=null)foreach(var c in hudCanvases)if(c!=null)c.enabled=show;
        }
        void ShowDetail(string title,string body){Dialogue304Ended(Dialogue304Detach(false));detailTitle=title;detailBody=body;menu304StoryChoices=null;OpenPage("상세");}
        void OnCollected(WorldMacroCollectionNotice notice)
        {
            // #304 사건 카드: 석경 조각을 얻었다 | 출처 = the bundle's name | 조각 글자 = the letters it opened (max 3 + "+N")
            string glyphs=notice!=null&&notice.Letters!=null?string.Concat(notice.Letters):"";
            Menu304RememberBundle(glyphs,notice!=null?notice.Title:"");
            Menu304Notify(new UiNotice304(UiNoticeKind304.Pickup,"석경 조각을 얻었다",notice!=null?notice.Title:"",glyphs));
            if(!PlayNamedSound("clue",.55f))PlayUi(Theme.ConfirmSound,.65f);
            if(Page=="소지품"||Page=="술식 도감")OpenPage(Page);
        }
        /// <summary>Legacy text notice (first line = title, the rest = source). #304: always shown (icon mode no longer mutes it),
        /// routed to the toast channel. The kind comes from the TEXT (Menu304NoticeKind), not from `seconds`: callers pass 8 s for
        /// errors and for ordinary lines alike (a plain "석경 조각을 얻었다" showed as a red error card, after/notice.png). Default =
        /// Info holding `seconds`; collection lines = Pickup (with the bundle's glyphs when known); errors = the error card.</summary>
        public void ShowNotice(string text,float seconds=4)=>Menu304ShowNotice(text,Menu304NoticeKind(text,seconds),seconds);
        /// <summary>ShowNotice with the kind stated by the caller (no text rule): e.g. Menu304ShowNotice(error, UiNoticeKind304.Error).
        /// seconds is kept for Info / Vehicle / Service; the other kinds hold for their ToastSpec time.</summary>
        public void Menu304ShowNotice(string text,UiNoticeKind304 kind,float seconds=0f)
        {
            text??="";
            if(noticeText!=null)noticeText.text=text;
            if(noticeRoot!=null){noticeRoot.gameObject.SetActive(true);noticeRoot.SetAsLastSibling();}
            Menu304SplitNotice(text,out string title,out string source);
            if(title.Length==0)return;
            string glyphs=kind==UiNoticeKind304.Pickup?Menu304PickupGlyphs(title,ref source):null;
            bool ownHold=kind==UiNoticeKind304.Info||kind==UiNoticeKind304.Vehicle||kind==UiNoticeKind304.Service;
            Menu304Notify(new UiNotice304(kind,title,source,glyphs,ownHold?seconds:0),false);
        }
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
            Tutorial306SettingsChanged();
        }
        void LateUpdate()
        {
            if(canvasRect==null)return;
            Vector2 size=canvasRect.rect.size;
            // #304 menu pages (V.Page304) scale themselves (UiPageFit304: min(1, W/1920, H/1080)); only legacy fixed windows
            // (the trade window until its rewrite) still use the old margin formula here.
            if(frame!=null)frame.localScale=Vector3.one*Mathf.Min(1f,(size.x-64)/frame.rect.width,(size.y-64)/frame.rect.height);
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
            UiText304.ApplyTextScale(this,scale);   // #304: same rule for TMP labels (base size <= 28 scales)
        }
        void RefreshStatus()
        {
            RefreshSaveFailureIcon();
            if(settingsErrorText!=null)settingsErrorText.text=Settings.SaveError??"";
            if(statusText==null||Session==null)return;
            // #307 phase 1 item 12: the status strings are rebuilt only when an input changes (same text, same assignment 4 Hz)
            int currency=Session.Progress.ledger.currency;var culture=System.Globalization.CultureInfo.CurrentCulture;
            if(statusCoins==null||currency!=statusCurrency||!ReferenceEquals(culture,statusCulture)){statusCoins=currency.ToString("N0");statusCurrency=currency;statusCulture=culture;statusLine=null;}
            string coins=statusCoins;
            // the trade window (legacy until its rewrite) shows only the balance in its small box
            if(Page=="장비 상점"||Page=="장비 강화"){statusText.text=coins;return;}
            if(!string.IsNullOrEmpty(Session.SaveError)){statusText.text=Session.SaveError;return;}
            string checkpoint=Session.CheckpointDisplayName;
            if(statusLine==null||!string.Equals(checkpoint,statusCheckpoint,System.StringComparison.Ordinal)){statusLine="조선통보 "+coins+"  ·  "+checkpoint+"에서 재시작";statusCheckpoint=checkpoint;}
            statusText.text=statusLine;
        }
        string statusCoins,statusLine,statusCheckpoint;int statusCurrency;System.Globalization.CultureInfo statusCulture;
        static string MenuLabel(string page) => page=="일시정지"?"일시정지":page=="옵션"?"설정":page=="술식 도감"?"술식":page=="조작 안내"?"조작":page;

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
