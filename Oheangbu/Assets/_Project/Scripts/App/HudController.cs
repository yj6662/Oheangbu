using Oheangbu.App.World;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App
{
    // The common HUD keeps its graybox when Skin is unassigned. The World Macro
    // playtest assigns an owned skin and still receives the same service values.
    public sealed class HudController : MonoBehaviour
    {
        [Tooltip("Optional playtest-owned visual skin. Leave null to retain the prototype HUD.")]
        public WorldMacroHudSkinProfileSO Skin;
        public bool HideText;
        public World.UI.CompactUiProfileSO Icons;
        private Image _interactionIcon;
        public bool UsesIcons => Icons != null;

        private RectTransform _hpFill, _inkFill, _reticle, _groggyFill;
        private Image _hpImage, _inkLiquid, _inkBarImage, _groggyImage;
        private RectTransform _inkReceivedMask;
        private Image _inkReceivedImage;
        private float _receivedFrom, _receivedTo, _receivedLast = -10f, _receivedBegan;
        private int _receivedLastFrame = -100000;
        public Vector2 ReceivedInkSegment => new Vector2(_receivedFrom, _receivedTo);
        private Image[] _dangerEdges;
        private GameObject _interactionRoot;
        private RectTransform _interactionRect;
        private Text _interactionText;
        private Canvas _canvas;
        private Font _font;
        private bool _ownsFont;
        private Texture2D _diskTexture;
        private Sprite _diskSprite;
        private Texture2D _bottleMaskTexture;
        private Sprite _bottleMaskSprite;
        private Vector2 _inkMotion;
        private float _inkHitImpulse, _inkTilt, _pulse;
        private float _hp01 = 1f, _ink01 = 1f, _groggy01;
#if UNITY_EDITOR
        private bool _editorDiagnosticState;
#endif
        private const float BarWidth = 260f;

        public bool IsSkinned => Skin != null;
        public bool InteractionVisible => _interactionRoot != null && _interactionRoot.activeSelf;
        public float Hp01 => _hp01;
        public float Ink01 => _ink01;
        public float Groggy01 => _groggy01;
        public bool ReticleVisible => _reticle != null && _reticle.gameObject.activeSelf;

        private void Awake()
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            if (Skin == null) BuildFallbackHud(); else BuildSkinnedHud();
            _reticle.gameObject.SetActive(false);
        }

        private void BuildFallbackHud()
        {
            _hpFill = CreateFallbackBar("HP", new Vector2(40f, 70f), new Color(.22f, .20f, .19f, .9f));
            _inkFill = CreateFallbackBar("Ink", new Vector2(40f, 40f), new Color(.10f, .09f, .09f, .9f));
            var reticle = CreateImage("Reticle", _canvas.transform, null, new Color(.95f, .93f, .88f, .8f));
            _reticle = reticle.rectTransform;
            _reticle.sizeDelta = new Vector2(46f, 46f);
            var groggy = CreateImage("GroggyFill", _reticle, null, new Color(.13f, .12f, .11f, .95f));
            _groggyFill = groggy.rectTransform;
            _groggyFill.sizeDelta = new Vector2(38f, 38f);
            _groggyFill.localScale = Vector3.zero;
        }

        private void BuildSkinnedHud()
        {
            _font = Skin.KoreanFont;
            if (_font == null) { _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 24); _ownsFont = true; }
            if(Skin.UseInkBar)
            {
                var paper=Skin.Paper;paper.a=.87f;
                var backing=CreateImage("Resources_Hanji",_canvas.transform,Skin.PromptPaper,paper);
                Anchor(backing.rectTransform,Vector2.zero,new Vector2(0,.5f),new Vector2(24,80),new Vector2(374,90));
            }
            var hpBackColor=Icons!=null?Icons.Health:Skin.Ink;hpBackColor.a=.20f;
            var hpBack=CreateImage("HP_FullStroke",_canvas.transform,Skin.HpStroke,hpBackColor);
            Anchor(hpBack.rectTransform,Vector2.zero,new Vector2(0f,.5f),Skin.HpPosition,Skin.HpSize);
            if(!Skin.UseInkBar)AddPaperContour(hpBack,.72f);
            var hp = CreateImage("HP_BrushStroke", _canvas.transform, Skin.HpStroke, Icons!=null?Icons.Health:Skin.Ink);
            _hpImage = hp;
            _hpImage.type = Image.Type.Filled;
            _hpImage.fillMethod = Image.FillMethod.Horizontal;
            _hpImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _hpFill = hp.rectTransform;
            Anchor(_hpFill, Vector2.zero, new Vector2(0f, .5f), Skin.HpPosition, Skin.HpSize);
            BuildDangerEdges();

            if (Skin.UseInkBar) BuildInkBar();
            else BuildInkBottle();

            _diskSprite = CreateInkDisk();
            BuildReticleAndInteraction();
        }

        private void BuildInkBar()
        {
            var faded = Skin.Ink; faded.a = .20f;
            var background = CreateImage("Ink_FullStroke", _canvas.transform, Skin.HpStroke, faded);
            Anchor(background.rectTransform, Vector2.zero, new Vector2(0,.5f), Skin.InkBarPosition, Skin.InkBarSize);
            _inkBarImage = CreateImage("Ink_BrushBar", _canvas.transform, Skin.HpStroke, Skin.Ink);
            _inkBarImage.type = Image.Type.Filled;
            _inkBarImage.fillMethod = Image.FillMethod.Horizontal;
            _inkBarImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _inkFill = _inkBarImage.rectTransform;
            Anchor(_inkFill, Vector2.zero, new Vector2(0,.5f), Skin.InkBarPosition, Skin.InkBarSize);
            var received = new GameObject("Ink_ReceivedSegment", typeof(RectTransform), typeof(RectMask2D));
            received.transform.SetParent(_canvas.transform, false);
            _inkReceivedMask = received.GetComponent<RectTransform>();
            Anchor(_inkReceivedMask, Vector2.zero, new Vector2(0,.5f), Skin.InkBarPosition, Skin.InkBarSize);
            _inkReceivedImage = CreateImage("Received_BrushStroke", received.transform, Skin.HpStroke, new Color(.40f,.37f,.30f,0f));
            Anchor(_inkReceivedImage.rectTransform, new Vector2(0,.5f), new Vector2(0,.5f), Vector2.zero, Skin.InkBarSize);
            received.SetActive(false);
            ResourceLabel("체력", Skin.HpPosition);
            ResourceLabel("먹", Skin.InkBarPosition);
        }

        private void ResourceLabel(string label, Vector2 position)
        {
            if(Icons!=null){
                var icon=CreateImage(label=="체력"?"HealthIcon":"InkIcon",_canvas.transform,label=="체력"?Icons.Heart:Icons.InkBottle,label=="체력"?Icons.Health:Icons.Ink);
                icon.preserveAspect=true;Anchor(icon.rectTransform,Vector2.zero,new Vector2(1,.5f),position+new Vector2(-8,0),new Vector2(32,32));return;
            }
            if(HideText)return;
            var go = new GameObject(label + "_Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_canvas.transform, false);
            var text = go.GetComponent<Text>(); text.text = label; text.font = _font; text.fontSize = 18;
            text.color = Skin.Ink; text.alignment = TextAnchor.MiddleRight; text.raycastTarget = false;
            Anchor(text.rectTransform, Vector2.zero, new Vector2(1,.5f), position + new Vector2(-10,0), new Vector2(46,28));
        }

        private void BuildInkBottle()
        {
            var bottleRoot = NewRect("InkBottle", _canvas.transform);
            Anchor(bottleRoot, Vector2.zero, new Vector2(.5f, .5f), Skin.BottlePosition, Skin.BottleSize);
            var clipRoot = new GameObject("LiquidClip", typeof(RectTransform), typeof(Image), typeof(Mask));
            clipRoot.transform.SetParent(bottleRoot, false);
            var clipRect = (RectTransform)clipRoot.transform;
            clipRect.anchorMin = clipRect.anchorMax = new Vector2(.5f, .5f);
            clipRect.pivot = new Vector2(.5f, 0f);
            clipRect.anchoredPosition = new Vector2(0f, -Skin.BottleSize.y * .44f);
            clipRect.sizeDelta = new Vector2(Skin.BottleSize.x * .78f, Skin.BottleSize.y * .54f);
            _bottleMaskSprite=CreateRoundedBottleMask();
            var maskImage=clipRoot.GetComponent<Image>();maskImage.sprite=_bottleMaskSprite;maskImage.color=Color.white;maskImage.raycastTarget=false;
            clipRoot.GetComponent<Mask>().showMaskGraphic=false;
            var glassColor=Skin.Paper;glassColor.a=.12f;
            var glass=CreateImage("GlassBody",clipRect,null,glassColor);Stretch(glass.rectTransform);
            _inkLiquid = CreateImage("InkLiquid", clipRect, null, Skin.Ink);
            _inkFill = _inkLiquid.rectTransform;
            _inkFill.anchorMin = _inkFill.anchorMax = new Vector2(.5f, 0f);
            _inkFill.pivot = new Vector2(.5f, 0f);
            _inkFill.anchoredPosition = Vector2.zero;
            _inkFill.sizeDelta = clipRect.sizeDelta;
            var bottle = CreateImage("BottleFrame", bottleRoot, Skin.InkBottle, Skin.Ink);
            bottle.preserveAspect = true;
            Stretch(bottle.rectTransform);
            AddPaperContour(bottle,.74f);

        }

        private void BuildReticleAndInteraction()
        {
            var reticleRoot = NewRect("Reticle", _canvas.transform);
            reticleRoot.sizeDelta = Skin.ReticleSize;
            _reticle = reticleRoot;
            _groggyImage = CreateImage("GroggyInkFill", reticleRoot, _diskSprite, Skin.Ink);
            _groggyImage.type = Image.Type.Filled;
            _groggyImage.fillMethod = Image.FillMethod.Vertical;
            _groggyImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            _groggyFill = _groggyImage.rectTransform;
            _groggyFill.sizeDelta = Skin.ReticleSize * .68f;
            var ring = CreateImage("OneStrokeRing", reticleRoot, Skin.LockRing, Skin.Ink);
            ring.preserveAspect = true;
            Stretch(ring.rectTransform);
            AddPaperContour(ring,.78f);

            _interactionRoot = NewRect("InteractionPrompt", _canvas.transform).gameObject;
            var interactionRect = (RectTransform)_interactionRoot.transform;
            _interactionRect = interactionRect;
            Anchor(interactionRect, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 62f), Skin.PromptSize);
            var paperTint = Skin.Paper; paperTint.a *= .78f;
            var paper = CreateImage("PromptPaper", interactionRect, Skin.PromptPaper, paperTint);
            Stretch(paper.rectTransform);
            var textGo = new GameObject("PromptText", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(interactionRect, false);
            _interactionText = textGo.GetComponent<Text>();
            _interactionText.font = _font;
            _interactionText.fontSize = 24;
            _interactionText.alignment = TextAnchor.MiddleCenter;
            _interactionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _interactionText.verticalOverflow = VerticalWrapMode.Truncate;
            _interactionText.color = Skin.Ink;
            _interactionText.raycastTarget = false;
            Stretch(_interactionText.rectTransform, new Vector2(42f, 18f));
            if(Icons!=null){
                _interactionText.enabled=false;
                _interactionIcon=CreateImage("InteractionIcon",interactionRect,Icons.Hand,Skin.Ink);
                _interactionIcon.preserveAspect=true;Stretch(_interactionIcon.rectTransform,new Vector2(14,14));
                interactionRect.sizeDelta=new Vector2(72,72);
            }
            _interactionRoot.SetActive(false);
        }
        public void SetInteractionIcon(Sprite sprite,bool problem=false){
            if(Icons==null||_interactionIcon==null)return;
            _interactionIcon.sprite=sprite;_interactionIcon.color=problem?Icons.Health:Skin.Ink;
            _interactionRoot.SetActive(sprite!=null);
        }

        private void BuildDangerEdges()
        {
            _dangerEdges = new Image[4];
            for (int i = 0; i < _dangerEdges.Length; i++)
            {
                var edge = CreateImage("LowHp_InkEdge_" + i, _canvas.transform, Skin.HpStroke,
                    new Color(Skin.Ink.r, Skin.Ink.g, Skin.Ink.b, 0f));
                Vector2 anchor = i == 0 ? new Vector2(.5f, 1f) : i == 1 ? new Vector2(.5f, 0f)
                    : i == 2 ? new Vector2(0f, .5f) : new Vector2(1f, .5f);
                edge.rectTransform.anchorMin = edge.rectTransform.anchorMax = anchor;
                edge.rectTransform.pivot = new Vector2(.5f, .5f);
                edge.rectTransform.anchoredPosition = i == 0 ? new Vector2(0f, -18f)
                    : i == 1 ? new Vector2(0f, 18f) : i == 2 ? new Vector2(18f, 0f) : new Vector2(-18f, 0f);
                bool horizontal = i < 2;
                edge.rectTransform.sizeDelta = horizontal ? new Vector2(1440f, 74f) : new Vector2(900f, 58f);
                edge.rectTransform.localRotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
                _dangerEdges[i] = edge;
            }
        }

        private RectTransform CreateFallbackBar(string name, Vector2 position, Color color)
        {
            var back = CreateImage(name + "_Back", _canvas.transform, null, new Color(0f, 0f, 0f, .25f));
            Anchor(back.rectTransform, Vector2.zero, new Vector2(0f, .5f), position, new Vector2(BarWidth, 16f));
            var fill = CreateImage(name + "_Fill", back.transform, null, color);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, .5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(BarWidth, 0f);
            return fillRect;
        }

        public void SetHp01(float value)
        {
            value = Mathf.Clamp01(value);
            _hp01 = value;
            if (_hpImage != null) { if(Icons==null||value<_hpImage.fillAmount)_hpImage.fillAmount = value; }
            else if (_hpFill != null) _hpFill.localScale = new Vector3(value, 1f, 1f);
            if (_dangerEdges == null || Skin == null) return;
            float alpha = Mathf.InverseLerp(Skin.DangerBeginsAtHp, 0f, value) * Skin.MaximumDangerAlpha;
            for (int i = 0; i < _dangerEdges.Length; i++)
            {
                Color color = _dangerEdges[i].color;
                color.a = alpha * (i < 2 ? 1f : .78f);
                _dangerEdges[i].color = color;
            }
        }

        public void SetInk01(float value)
        {
            value = Mathf.Clamp01(value);
            if (value < _ink01 - .00001f)
            {
                // Spending begins a new receipt range; old incoming ink must not be highlighted again.
                _receivedFrom = _receivedTo = value;
                _receivedLast = -10f; _receivedLastFrame = -100000;
            }
            _ink01 = value;
            _receivedTo = Mathf.Min(_receivedTo, value);
            _receivedFrom = Mathf.Min(_receivedFrom, _receivedTo);
            if (_inkBarImage != null) { if(Icons==null||value<_inkBarImage.fillAmount)_inkBarImage.fillAmount = value; }
            else if (_inkLiquid != null)
            {
                Vector2 size = _inkFill.sizeDelta;
                size.y = ((RectTransform)_inkFill.parent).rect.height * value;
                _inkFill.sizeDelta = size;
                _inkLiquid.color = new Color(Skin.Ink.r, Skin.Ink.g, Skin.Ink.b, Mathf.Lerp(.58f, Skin.Ink.a, value));
            }
            else if (_inkFill != null) _inkFill.localScale = new Vector3(value, 1f, 1f);
        }

        public void NotifyInkGained(float actualReceived)
        {
            if (actualReceived <= 0f || _inkReceivedImage == null) return;
            float now = Time.unscaledTime;
            float from = Mathf.Max(0f, _ink01 - actualReceived);
            // Harvest pays once per gameplay frame. A long render frame must not split one held
            // extraction into repeated flashes; a real gap in both clocks starts a fresh range.
            bool continuousFrames = Time.frameCount >= _receivedLastFrame && Time.frameCount - _receivedLastFrame <= 2;
            if (now - _receivedLast > .14f && !continuousFrames) { _receivedFrom = from; _receivedBegan = now; }
            else _receivedFrom = Mathf.Min(_receivedFrom, from);
            _receivedTo = _ink01; _receivedLast = now; _receivedLastFrame = Time.frameCount;
        }

        private void UpdateInkReceived()
        {
            if (_inkReceivedImage == null) return;
            float age = Time.unscaledTime - _receivedLast;
            bool visible = age < .45f && _receivedTo > _receivedFrom;
            _inkReceivedMask.gameObject.SetActive(visible);
            if (!visible) return;
            float width = Skin.InkBarSize.x;
            _inkReceivedMask.anchoredPosition = Skin.InkBarPosition + Vector2.right * (_receivedFrom * width);
            _inkReceivedMask.sizeDelta = new Vector2((_receivedTo - _receivedFrom) * width, Skin.InkBarSize.y);
            _inkReceivedImage.rectTransform.anchoredPosition = Vector2.left * (_receivedFrom * width);
            float attack = Mathf.Clamp01((Time.unscaledTime - _receivedBegan) / .06f);
            var color = _inkReceivedImage.color;
            color.a = .85f * attack * (1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.16f,.45f,age)));
            _inkReceivedImage.color = color;
        }

        public void SetGroggy01(float value)
        {
            value = Mathf.Clamp01(value);
            _groggy01 = value;
            if (_groggyImage != null) _groggyImage.fillAmount = value;
            else if (_groggyFill != null) _groggyFill.localScale = Vector3.one * value;
        }

        public void SetInteractionText(string value)
        {
#if UNITY_EDITOR
            if (_editorDiagnosticState) return;
#endif
            ApplyInteractionText(value);
        }

        private void ApplyInteractionText(string value)
        {
            if (_interactionRoot == null || _interactionText == null) return;
            if(Icons!=null)return;
            if(HideText){_interactionRoot.SetActive(false);return;}
            string text = value ?? string.Empty;
            if (_interactionText.text != text) _interactionText.text = text;
            if (Skin != null && _interactionRect != null && text.Length > 0)
            {
                // Let a short interaction fit its text; long feedback can still wrap within the profile limit.
                float width = Mathf.Clamp(_interactionText.preferredWidth + 84f, 260f, Skin.PromptSize.x);
                _interactionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                float height = Mathf.Clamp(_interactionText.preferredHeight + 36f, 66f, Skin.PromptSize.y);
                _interactionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
            bool visible = text.Length > 0;
            if (_interactionRoot.activeSelf != visible) _interactionRoot.SetActive(visible);
        }

        public void SetInkMotion(Vector2 normalizedLocalVelocity)
        {
            _inkMotion = Vector2.ClampMagnitude(normalizedLocalVelocity, 1f);
        }

        public void KickInk(float strength = 1f)
        {
            _inkHitImpulse = Mathf.Max(_inkHitImpulse, Mathf.Clamp01(strength));
        }

        public void PulseReticle() { _pulse = 1f; }

        public void UpdateReticle(Transform target, Camera cam)
        {
#if UNITY_EDITOR
            if (_editorDiagnosticState) return;
#endif
            _pulse = Mathf.Max(0f, _pulse - Time.unscaledDeltaTime * 4f);
            bool visible = target != null && cam != null;
            if (_reticle == null) return;
            _reticle.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * 1.1f);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height)
            { _reticle.gameObject.SetActive(false); return; }
            _reticle.position = screen;
            _reticle.localScale = Vector3.one * (1f + .5f * _pulse * _pulse);
        }

#if UNITY_EDITOR
        public void SetEditorDiagnosticState(float hp01, float ink01, float groggy01, string interaction, Vector2 screenPoint)
        {
            _editorDiagnosticState = true;
            SetHp01(hp01); SetInk01(ink01); SetGroggy01(groggy01);
            ApplyInteractionText(interaction);
            _pulse = 0f;
            if (_reticle != null)
            {
                _reticle.gameObject.SetActive(true);
                _reticle.position = screenPoint;
                _reticle.localScale = Vector3.one;
            }
        }

        public void ClearEditorDiagnosticState(float hp01, float ink01, float groggy01, string interaction, bool reticleVisible, Vector3 reticlePosition)
        {
            _editorDiagnosticState = false;
            SetHp01(hp01); SetInk01(ink01); SetGroggy01(groggy01);
            ApplyInteractionText(interaction);
            if (_reticle != null)
            {
                _reticle.gameObject.SetActive(reticleVisible);
                _reticle.position = reticlePosition;
                _reticle.localScale = Vector3.one;
            }
        }
#endif

        private void LateUpdate()
        {
            if(Icons!=null){
                if(_hpImage!=null)_hpImage.fillAmount=Mathf.MoveTowards(_hpImage.fillAmount,_hp01,Time.deltaTime*.48f);
                if(_inkBarImage!=null)_inkBarImage.fillAmount=Mathf.MoveTowards(_inkBarImage.fillAmount,_ink01,Time.deltaTime*.48f);
            }
            UpdateInkReceived();
            if (_inkLiquid == null) return;
            _inkHitImpulse = Mathf.MoveTowards(_inkHitImpulse, 0f, Time.unscaledDeltaTime * 2.8f);
            float target = -_inkMotion.x * 5f + Mathf.Sin(Time.unscaledTime * 16f) * _inkHitImpulse * 7f;
            _inkTilt = Mathf.Lerp(_inkTilt, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            _inkFill.localRotation = Quaternion.Euler(0f, 0f, _inkTilt);
            _inkFill.anchoredPosition = new Vector2(_inkMotion.x * 2f, Mathf.Abs(_inkMotion.y) * 1.5f + _inkHitImpulse * 3f);
        }

        private Sprite CreateInkDisk()
        {
            const int size = 64;
            _diskTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            { name = "HUD_ProceduralInkDisk", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            var ink = new Color32(255, 255, 255, 255);
            var clear = new Color32(255, 255, 255, 0);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - (size - 1) * .5f, dy = y - (size - 1) * .5f;
                float radius = 29f + Mathf.Sin(x * 1.71f + y * .37f) * .7f;
                pixels[y * size + x] = dx * dx + dy * dy <= radius * radius ? ink : clear;
            }
            _diskTexture.SetPixels32(pixels);
            _diskTexture.Apply(false, true);
            var sprite = Sprite.Create(_diskTexture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), size);
            sprite.name = "HUD_ProceduralInkDisk";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private Sprite CreateRoundedBottleMask()
        {
            const int width=64,height=64;const float radius=10f;
            _bottleMaskTexture=new Texture2D(width,height,TextureFormat.RGBA32,false,true)
            {name="HUD_ProceduralBottleInterior",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,
                hideFlags=HideFlags.HideAndDontSave};
            var pixels=new Color32[width*height];var opaque=new Color32(255,255,255,255);var clear=new Color32(255,255,255,0);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                float cx=Mathf.Clamp(x,radius,width-1-radius),cy=Mathf.Clamp(y,radius,height-1-radius);
                float dx=x-cx,dy=y-cy;pixels[y*width+x]=dx*dx+dy*dy<=radius*radius?opaque:clear;
            }
            _bottleMaskTexture.SetPixels32(pixels);_bottleMaskTexture.Apply(false,true);
            var sprite=Sprite.Create(_bottleMaskTexture,new Rect(0f,0f,width,height),new Vector2(.5f,.5f),width);
            sprite.name="HUD_ProceduralBottleInterior";sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;
        }

        private void AddPaperContour(Image image,float alpha)
        {
            var outline=image.gameObject.AddComponent<Outline>();var color=Skin.Paper;color.a=alpha;
            outline.effectColor=color;outline.effectDistance=new Vector2(1.25f,-1.25f);outline.useGraphicAlpha=true;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, Vector2 inset = default)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
        }

        private void OnDestroy()
        {
            if (_diskSprite != null) Destroy(_diskSprite);
            if (_diskTexture != null) Destroy(_diskTexture);
            if (_bottleMaskSprite != null) Destroy(_bottleMaskSprite);
            if (_bottleMaskTexture != null) Destroy(_bottleMaskTexture);
            if (_font != null && _ownsFont) Destroy(_font);
        }
    }
}
