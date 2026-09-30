using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #306 §2-3 host of the one dialogue surface (DialogueView304). Session.DialogueRequested -> Show (bound per scene in BindScene,
    // dropped in UnhookSession: this root is the lobby's DontDestroyOnLoad instance). The conversation lives on the "상세" page:
    // opening it from nothing = Pause.Begin + save + HUD / prompt off, once per conversation; page turns, follow-ups and the menu
    // rebuild in place; Trade / Upgrade / Maintain switch Page to the existing window (no second Begin) and that window's
    // FinishClose brings the menu back instead of resuming. Any other close ends it: the band fades out (PromptOutMs), Pause.End,
    // HUD on, then the showing request's Closed. The old Present -> DetailRequested -> ShowDetail band still works beside it.
    public sealed partial class PlaytestUiRoot : IDialogueHost304
    {
        [Tooltip("#306 §2-3 dialogue placement (TEST); colours / roles / motion come from UiStyle304SO")]
        public DialogueViewSpec304 Dialogue304Spec=new DialogueViewSpec304();
        DialogueView304 dialogue304;
        /// <summary>THE dialogue surface (IDialogueSurface306). The session raises requests through DialogueRequested.</summary>
        public DialogueView304 Dialogue304=>dialogue304??=new DialogueView304(this);
        bool Dialogue304Open=>dialogue304!=null&&dialogue304.IsOpen;

        void OnDialogueRequested306(DialogueRequest306 r)
        {
            if(r==null)return;
            Dialogue304Heal();
            // refused (another page, loading, rest, demo ending, nothing to show): the conversation never opened; end it at once so
            // the session / NPC actor never waits on a DialogueEnded that would not come
            if(!Dialogue304.Show(r))Dialogue304Ended(r.Closed);
        }

        void Dialogue304Build(bool fresh)
        {
            var s=V.Style(Theme);
            V.EnsureCanvasChannels(canvas);FocusMark304.Attach(s,canvasRect);UiText304.Prewarm(s);
            frame=null;menu304Rail=null;menu304Veil=null;menu304LiftX=float.NaN;
            menu304StoryChoices=null;menu304StoryText=null;menu304StoryDone=null;
            var r=dialogue304.Current;
            detailTitle=r!=null?r.Speaker??"":"";detailBody=r!=null&&r.Lines!=null?string.Join("\n",r.Lines):"";
            var page=dialogue304.BuildPage(modalLayer,fresh);
            menu304Page=page;contentRoot=page;
            Menu304LegacyText("상세");
        }

        /// <summary>Conversation end on this side: forgets it (fade = the band leaves on the canvas instead of being cleared) and
        /// returns the Closed to run once the page is gone. null when no conversation is open.</summary>
        Action Dialogue304Detach(bool fade)
        {
            if(!Dialogue304Open)return null;
            var band=dialogue304.Root;var ended=dialogue304.Detach();
            if(fade)Dialogue304FadeOut(band);
            return ended;
        }
        /// <summary>A flow that clears the page without FinishClose (journey overlay, title, bind) left the conversation open with
        /// no surface: end it (Closed once) so the next request opens from nothing (Pause.Begin) instead of rebuilding in place.</summary>
        void Dialogue304Heal(){if(Dialogue304Open&&Page.Length==0&&!closingMap)Dialogue304Ended(Dialogue304Detach(false));}
        static void Dialogue304Ended(Action ended){if(ended==null)return;try{ended();}catch(Exception e){Debug.LogException(e);}}

        /// <summary>BleedIn's reverse: the band moves out of modalLayer (V.Clear would cut it), loses raycasts and selection, fades
        /// over PromptOutMs with ease.lift and is destroyed. Named "DialogueFade306" so harnesses never take it for an open band.</summary>
        void Dialogue304FadeOut(RectTransform band)
        {
            if(band==null||canvasRect==null||!band.gameObject.activeInHierarchy)return;
            var es=EventSystem.current;
            if(es!=null&&es.currentSelectedGameObject!=null&&es.currentSelectedGameObject.transform.IsChildOf(band))es.SetSelectedGameObject(null);
            band.name="DialogueFade306";band.SetParent(canvasRect,false);
            var group=band.GetComponent<CanvasGroup>();
            if(group==null){Destroy(band.gameObject);return;}
            group.interactable=false;group.blocksRaycasts=false;
            Dialogue304FadeAsync(band.gameObject,group).Forget();
        }
        async UniTaskVoid Dialogue304FadeAsync(GameObject go,CanvasGroup group)
        {
            var s=V.Style(Theme);
            await UiTween304.Alpha(group,0f,s.Motion.Sec(s.Motion.PromptOutMs,UiTween304.ReducedMotion),s.Motion.Lift,UiTween304.Token(group));
            if(go!=null)Destroy(go);
        }

        /// <summary>Window page of a service row. Trade / Upgrade by the station id used today (OpenEquipmentService):
        /// village_shop = 장비 상점, village_artisan = 장비 강화, another id falls back to the kind. Maintain = 정비. OpenPage keeps
        /// its own gates (NearEquipmentService / AtDemoShop). null = the row opens no window.</summary>
        public static string Dialogue304ServicePage(DialogueService306 service)
        {
            if(service==null)return null;
            switch(service.Kind)
            {
                case DialogueServiceKind306.Trade:
                case DialogueServiceKind306.Upgrade:
                    return service.Id=="village_shop"?"장비 상점":service.Id=="village_artisan"?"장비 강화":service.Kind==DialogueServiceKind306.Trade?"장비 상점":"장비 강화";
                case DialogueServiceKind306.Maintain:return "정비";
                default:return null;
            }
        }

        // ------------------------------------------------------------------ IDialogueHost304
        UiStyle304SO IDialogueHost304.DialogueStyle=>V.Style(Theme);
        PlaytestUiThemeSO IDialogueHost304.DialogueTheme=>Theme;
        DialogueViewSpec304 IDialogueHost304.DialogueSpec=>Dialogue304Spec??=new DialogueViewSpec304();
        bool IDialogueHost304.DialogueServiceShowing=>Page.Length>0&&Page!="상세";

        bool IDialogueHost304.DialogueOpen()
        {
            if(!bound||IsTitle||Session==null||(Page.Length>0&&Page!="상세"))return false;
            OpenPage("상세");   // from nothing: Pause.Begin + SaveNow + HUD off (OpenPage's own gates: busy, map fold, confirm, rest, ending)
            return Page=="상세";
        }

        void IDialogueHost304.DialogueRebuild()
        {
            if(!bound||IsTitle)return;
            bool fresh=Page!="상세";
            if(fresh)
            {
                // back from a service window (or a follow-up while it showed): the window page goes, the pause stays
                if(Map!=null&&Page=="지도")Map.SetExpanded(false);
                closingMap=false;Page="상세";settingsBuilt=false;Menu304ClearRailLayer();Menu304ResetPageState();SetHud(false);
            }
            V.Clear(modalLayer);Dialogue304Build(fresh);Menu304Shown("상세");ApplyTextScale();
        }

        void IDialogueHost304.DialogueClose(){CloseMenu();}

        bool IDialogueHost304.DialogueOpenService(DialogueService306 service)
        {
            string page=Dialogue304ServicePage(service);if(page==null)return false;
            if(page!="정비"){gearSelection="";gearError="";}
            OpenPage(page);return Page==page;
        }

        void IDialogueHost304.DialogueSound(string cue,float gain){PlayNamedSound(cue,gain);}
    }
}
