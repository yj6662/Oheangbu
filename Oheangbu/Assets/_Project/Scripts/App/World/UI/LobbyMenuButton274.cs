using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>Title menu row (#304 title.png, DESIGN §7.7): a paper label on the title's ink spine, hit area = the whole row.
    /// Focus (keyboard / pad selection, or mouse-over which SELECTS the row) = label Serif600 28 -> Serif800 34, the 선택 부풂
    /// (stroke_swell) under the label, a hollow [Enter] after it, and the canvas' single 방점 (FocusMark304 reads DabAnchor).
    /// The swell and the key live under the child "FocusFrame" whose CanvasGroup alpha is the focus target (lobby274 check:
    /// 0 at rest, 1 on hover / selection, 0 once deselected or disabled). Disabled = the row's OWN interactable flag: Off label +
    /// a reason meta under it ("저장 없음"); a row whose page is suspended by a confirm dialog (CanvasGroup) keeps its default look.
    /// Sounds: ui_focus on select, ui_select on press / submit (one cue per hover because hover selects).</summary>
    public sealed class LobbyMenuButton274 : Button, IFocusVisual304
    {
        const float LabelX = 40f, DabGap = 16f, KeyGap = 22f, SwellX = 18f, SwellH = 26f, SwellOverhang = 12f;
        static readonly Vector2 DabSize = new Vector2(30f, 23f);   // title.png dab 30x23 (menu rows are a little smaller than 34x26)

        [SerializeField] CanvasGroup frame;
        [SerializeField] TMP_Text label;
        [SerializeField] TMP_Text reason;
        [SerializeField] Image swell;
        [SerializeField] RectTransform dabAnchor;
        [SerializeField] UiStyle304SO style;
        [SerializeField] PlaytestUiThemeSO theme;
        [SerializeField] Color labelNormal = Color.white, labelDisabled = Color.gray;
        [SerializeField] TMP_FontAsset normalFont, focusFont;
        [SerializeField] Material normalMaterial, focusMaterial;
        [SerializeField] float normalBase = 28f, focusBase = 34f;
        [SerializeField] Vector2 labelPosition;
        [SerializeField] float swellAlpha = .95f;
        float frameTarget;
        bool lit, built;
        readonly UiTweenSlot304 swellSlot = new UiTweenSlot304();

        public RectTransform DabAnchor => dabAnchor;
        public TMP_Text Label => label;
        public TMP_Text Reason => reason;
        public CanvasGroup FocusFrame => frame;
        public Image SwellImage => swell;
        /// <summary>Drawn in the focused look (hover / selection / press, own interactable flag on).</summary>
        public bool Lit => lit;

        /// <summary>274 signature (theme only): resolves the #304 style from the theme.</summary>
        public static LobbyMenuButton274 Create(Transform parent,string name,string text,PlaytestUiThemeSO theme,
            float x,float y,float width,float height,UnityAction action)
            => Create(V.Style(theme),parent,name,text,theme,x,y,width,height,action);

        /// <summary>Row at (x, y) w x h in parent px (title card coordinates). key = the hollow keycap shown while focused;
        /// disabledReason = meta shown under the label while the row's own interactable flag is off.</summary>
        public static LobbyMenuButton274 Create(UiStyle304SO s,Transform parent,string name,string text,PlaytestUiThemeSO theme,
            float x,float y,float width,float height,UnityAction action,string key="Enter",string disabledReason=null)
        {
            s=s!=null?s:UiStyle304SO.Fallback;
            var rect=V.Rect(name,parent,x,y,width,Mathf.Max(height,s.MinHit));
            float h=rect.sizeDelta.y;
            var hit=V.Image(rect,new Color(0f,0f,0f,0f),null,true);
            hit.canvasRenderer.cullTransparentMesh=true;   // raycasts, draws nothing (keeps the InkReveal batch)
            var button=rect.gameObject.AddComponent<LobbyMenuButton274>();
            button.transition=Transition.None;button.targetGraphic=hit;
            button.style=s;button.theme=theme;
            button.labelNormal=s.Paper;button.labelDisabled=s.Off;

            // label: measured in both looks so the rect never clips the focused 34 px
            var normalRole=s.Role(UiType304.Label28);var focusRole=s.Role(UiType304.Title34);
            var frameRect=V.Stretch("FocusFrame",rect);
            var t=V.Label(s,rect,"Label",text,normalRole,s.Paper,LabelX,0f,0f,0f,TextAlignmentOptions.Left);
            button.label=t;button.normalFont=t.font;button.normalMaterial=t.fontSharedMaterial;
            button.normalBase=normalRole.Size;button.focusBase=focusRole.Size;
            button.focusFont=s.FontOf(focusRole);
            if(button.focusFont==null)button.focusFont=t.font;
            button.focusMaterial=s.MaterialOf(focusRole,button.focusFont);
            if(button.focusMaterial==null&&button.focusFont!=null)button.focusMaterial=button.focusFont.material;
            float factor=UiText304.TextScale;
            Vector2 normalSize=t.rectTransform.sizeDelta;
            Vector2 focusSize=button.Measure(text,button.focusFont,button.focusMaterial,button.focusBase*factor);
            Vector2 ls=Vector2.Max(normalSize,focusSize);
            t.rectTransform.sizeDelta=ls;

            // disabled reason (stacked under the label, Meta20 Mist: states.png 비활성)
            TMP_Text why=null;float blockH=normalSize.y;
            if(!string.IsNullOrEmpty(disabledReason))
            {
                why=V.Label(s,rect,"Reason",disabledReason,UiType304.Meta20,s.Mist,LabelX,0f);
                blockH=normalSize.y+why.rectTransform.sizeDelta.y;
                why.gameObject.SetActive(false);
            }
            button.reason=why;
            float mid=h*.5f;
            button.labelPosition=new Vector2(LabelX,-(mid-ls.y*.5f));
            t.rectTransform.anchoredPosition=button.labelPosition;

            // FocusFrame (behind the label): swell under the label + hollow key after the focused label
            frameRect.SetAsFirstSibling();
            button.frame=frameRect.gameObject.AddComponent<CanvasGroup>();
            button.frame.alpha=0;button.frame.interactable=false;button.frame.blocksRaycasts=false;
            float right=LabelX+focusSize.x;
            if(!string.IsNullOrEmpty(key))
            {
                var cap=V.Keycap(s,frameRect,key,false,false,true,right+KeyGap,mid-s.Keycap.SmallHeight*.5f);
                right=cap.anchoredPosition.x+cap.sizeDelta.x;
            }
            // title.png: the swell runs 12 px past the key (key right 368, swell right 380); was +6 (after_lobby/title.png ~371)
            button.swell=V.Swell(s,frameRect,SwellX,mid+17f,Mathf.Max(60f,right+SwellOverhang-SwellX),SwellH,s.Paper,button.swellAlpha);
            button.swell.transform.SetAsFirstSibling();
            var fx=button.swell.GetComponent<InkRevealEffect>();if(fx!=null)fx.Reveal=0f;

            // the canvas 방점 sits DabGap left of the label, a little above its middle (title.png dab (-6,18) 30x23)
            button.dabAnchor=V.DabAnchor(rect,LabelX-DabGap-DabSize.x,mid-DabSize.y*.5f-3f,DabSize.x,DabSize.y,s);

            if(why!=null)
            {
                // the reason is laid out now; it only shows while disabled (Apply keeps the label block centred either way)
                why.rectTransform.anchoredPosition=new Vector2(LabelX,-(mid-blockH*.5f+normalSize.y));
            }
            button.onClick.AddListener(action);
            button.built=true;
            button.DoStateTransition(button.currentSelectionState,true);
            return button;
        }

        Vector2 Measure(string text,TMP_FontAsset font,Material material,float size)
        {
            if(label==null)return Vector2.zero;
            var font0=label.font;var material0=label.fontSharedMaterial;float size0=label.fontSize;
            if(font!=null)label.font=font;
            if(material!=null)label.fontSharedMaterial=material;
            label.fontSize=size;
            Vector2 p=label.GetPreferredValues(text??"");
            label.font=font0;if(material0!=null)label.fontSharedMaterial=material0;label.fontSize=size0;
            return new Vector2(Mathf.Ceil(p.x),Mathf.Ceil(p.y));
        }

        // ------------------------------------------------------------------ state
        protected override void DoStateTransition(SelectionState state,bool instant)
        {
            // own flag only: a CanvasGroup-suspended row (confirm dialog open) reports Disabled but keeps its default look
            bool disabled=!interactable;
            bool active=!disabled&&(state==SelectionState.Highlighted||state==SelectionState.Selected||state==SelectionState.Pressed);
            frameTarget=active?1:0;
            bool now=instant||!Application.isPlaying;
            if(frame!=null&&now)frame.alpha=frameTarget;
            Apply(active,disabled,state==SelectionState.Pressed&&!disabled,now);
        }

        void Apply(bool active,bool disabled,bool pressed,bool instant)
        {
            if(!built||label==null)return;
            label.color=disabled?labelDisabled:labelNormal;
            var font=active?focusFont:normalFont;var material=active?focusMaterial:normalMaterial;
            if(font!=null&&label.font!=font)label.font=font;
            if(material!=null&&label.fontSharedMaterial!=material)label.fontSharedMaterial=material;
            float size=TargetSize(active);
            if(!Mathf.Approximately(label.fontSize,size))label.fontSize=size;
            float drop=pressed?(style!=null?style.PressDrop:2f):0f;
            var block=LabelBlockOffset(disabled);
            label.rectTransform.anchoredPosition=labelPosition+new Vector2(0f,block-drop);
            if(reason!=null&&reason.gameObject.activeSelf!=disabled)reason.gameObject.SetActive(disabled);
            if(swell!=null)
            {
                // 누름 = the swell's tone x .86 (DESIGN §5.0), alpha untouched (the stroke tween owns it)
                float tone=pressed?(style!=null?style.PressTone:.86f):1f;var p=labelNormal;var c=swell.color;
                var want=new Color(p.r*tone,p.g*tone,p.b*tone,c.a);
                if(c!=want)swell.color=want;
            }
            if(active==lit&&!instant)return;
            lit=active;
            var fx=swell!=null?swell.GetComponent<InkRevealEffect>():null;
            if(fx==null)return;
            if(instant||style==null){swellSlot.Cancel();fx.Reveal=active?1f:0f;UiTween304.SetAlpha(swell,swellAlpha);return;}
            var ct=swellSlot.Restart(this);
            if(active)UiTween304.StrokeIn(fx,style,false,ct,swellAlpha,true).Forget();
            else UiTween304.StrokeOut(fx,style,ct,swellAlpha).Forget();
        }

        // while disabled the label moves up so label + reason stay centred in the row
        float LabelBlockOffset(bool disabled)
        {
            if(!disabled||reason==null)return 0f;
            return reason.rectTransform.sizeDelta.y*.5f;
        }

        // focused 34 px keeps its ratio to the normal 28 px under the text-scale setting (28 x scale -> 34 x scale)
        float TargetSize(bool active)
        {
            float k=UiText304.TextScale;
            return (active?focusBase:normalBase)*k;
        }

        void Update()
        {
            if(frame==null)return;
            float ms=style!=null?Mathf.Max(1,style.Motion.HoverMs):120f;
            frame.alpha=Mathf.MoveTowards(frame.alpha,frameTarget,Time.unscaledDeltaTime*1000f/ms);
        }

        void LateUpdate()
        {
            // PlaytestUiRoot.ApplyTextScale resets every <= 28 px label to its base size; keep the focused size on a lit row
            if(built&&label!=null)
            {
                float size=TargetSize(lit);
                if(!Mathf.Approximately(label.fontSize,size))label.fontSize=size;
            }
        }

        protected override void OnDisable(){swellSlot.Cancel();base.OnDisable();}
        protected override void OnDestroy(){swellSlot.Cancel();base.OnDestroy();}

        // ------------------------------------------------------------------ IFocusVisual304 (FocusMark304 drives the 방점)
        public void SetFocused(bool focused,bool instant){DoStateTransition(currentSelectionState,instant);}

        // ------------------------------------------------------------------ pointer / keys / sound
        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            // DESIGN §5.0: mouse-over moves the focus (there is never a second focus mark)
            if(Application.isPlaying&&IsInteractable()&&EventSystem.current!=null)FocusMark304.Select(gameObject);
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            Cue("ui_focus",.22f);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if(eventData==null||eventData.button==PointerEventData.InputButton.Left)Cue("ui_select",.3f);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            Cue("ui_select",.3f);
            base.OnSubmit(eventData);
        }

        void Cue(string id,float gain)
        {
            if(!Application.isPlaying||!IsInteractable()||theme==null||theme.SoundPalette==null)return;
            PlaytestUiRoot.Instance?.PlayNamedSound(id,gain);
        }
    }
}
