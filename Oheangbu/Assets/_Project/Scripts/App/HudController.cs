using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App
{
    // The common HUD keeps its graybox when Skin is unassigned. The World Macro
    // playtest assigns an owned skin and still receives the same service values.
    // #304 (DESIGN §5.8 / §5.9 / §5.14 / §7.1, IMPLEMENTATION §7.1): the skinned HUD is built from the #304 style of the skin
    // (V.Style(Skin)): InkMeter HP + 먹 with the 먹병 (BuildMeters304), the two-stage lock-on enso (HudLockOn304), the keycap +
    // verb interaction prompt (HudPrompt304, text restored in icon mode), the bearing line (HudBearingLine304, a child of
    // HUD_Canvas) and low-HP ink edges fitted to the canvas. Icons (CompactUiProfileSO) is kept only as a source of fallback
    // pictograms; it no longer hides text or recolours the meters.
    // #306 (SPEC-PLAYTEST-306 #1 / #8, D306): the 먹 원상 minimap is back beside the bearing line (HudMinimap304, BottomRight),
    // enemy health = the lock-on stroke "TargetHpStroke" 6 px under the enso and the boss bar (HudBossBar304, BottomCentre),
    // fed by SetTargetHealth / SetBossHealth (IHealthBarSource306). All stay children of HUD_Canvas (one screen Canvas).
    // #308 (SPEC-HUD-LIQUID-308, D308-11): while the liquid profile (Resources UI308/HudLiquid308) exists and is enabled, the
    // HP / ink meters and the 먹병 are replaced by "Vessels308" (HudVessels308: two liquid vessels + dodge / jump / vehicle
    // marks, its own Canvas). Values stay immediate here (Hp01 / Ink01); the vessels only decide how the surface reaches them.
    // Without the asset, or with Enabled off, BuildMeters304 builds exactly the #304 HUD.
    public sealed class HudController : MonoBehaviour
    {
        [Tooltip("Optional playtest-owned visual skin. Leave null to retain the prototype HUD.")]
        public WorldMacroHudSkinProfileSO Skin;
        public bool HideText;
        [Tooltip("#304: support pictograms only (bearing-line fallback icons). Text is shown whether or not this is set.")]
        public World.UI.CompactUiProfileSO Icons;
        public bool UsesIcons => Icons != null;
        [Tooltip("#306 먹 원상 미니맵: place, ranges (outside 120 m, cave 40 m), reprint distance (TEST, SPEC-PLAYTEST-306 #1)")]
        public MinimapSpec304 Minimap = new MinimapSpec304();
        [Tooltip("#306 enemy health: lock-on stroke 64x5 under the enso, boss bar 720x8 bottom centre (TEST, SPEC-PLAYTEST-306 #8)")]
        public EnemyBarSpec304 EnemyBars = new EnemyBarSpec304();
        [Tooltip("#308 liquid HUD data. Empty = Resources UI308/HudLiquid308 (read once per HUD build). Missing or disabled = the #304 meters.")]
        public HudLiquid308ProfileSO Liquid308;

        private RectTransform _hpFill, _inkFill, _reticle, _groggyFill;
        private float _receivedFrom, _receivedTo, _receivedLast = -10f, _receivedBegan;
        private int _receivedLastFrame = -100000;
        public Vector2 ReceivedInkSegment => new Vector2(_receivedFrom, _receivedTo);
        private Image[] _dangerEdges;
        private Canvas _canvas;
        private Vector2 _edgeCanvasSize;
        private Texture2D _diskTexture;
        private Sprite _diskSprite;
        private Vector2 _inkMotion;
        private float _inkHitImpulse, _pulse;
        private float _hp01 = 1f, _ink01 = 1f, _groggy01;
        private UiStyle304SO _style;
        private HudTokens304 _tokens;
        // #304 pieces (null on the prototype HUD)
        private InkMeter304 _hpMeter, _inkMeter;
        private Image _inkLiquid;
        private RectTransform _inkReceivedMask;
        private InkMeterGraphic _inkReceivedGraphic;
        private Image _inkCostBox;
        private float _inkCost01;
        private HudLockOn304 _lockOn;
        private HudPrompt304 _prompt;
        private HudBearingLine304 _bearing;
        private HudMinimap304 _minimap;
        private HudBossBar304 _bossBar;
        private HudHealthStroke304 _targetHp;
        private CanvasGroup _targetHpGroup;
        private bool _targetHpWanted;
        private HudVessels308 _vessels;   // #308 (null while the #304 meters are in use)
#if UNITY_EDITOR
        private bool _editorDiagnosticState;
#endif
        private bool _lockPreview;
        private const float BarWidth = 260f;

        public bool IsSkinned => Skin != null;
        public bool InteractionVisible => _prompt != null && _prompt.Visible;
        /// <summary>QA1: the prompt is logically shown AND can actually draw (HUD_Canvas enabled and active, prompt frame active).
        /// The presenter hides the world F only while this is true, so a HUD hidden by a menu or a capture tool never leaves the
        /// focused object with neither surface. InteractionVisible keeps the logical meaning the harness compares.</summary>
        public bool PromptOnScreen304 => _prompt != null && _prompt.Visible && _prompt.isActiveAndEnabled
            && _canvas != null && _canvas.enabled && _canvas.gameObject.activeInHierarchy;
        public float Hp01 => _hp01;
        public float Ink01 => _ink01;
        public float Groggy01 => _groggy01;
        public bool ReticleVisible => _reticle != null && _reticle.gameObject.activeSelf;
        // #304 read-only handles (harness / capture checks)
        public InkMeter304 HpMeter304 => _hpMeter;
        public InkMeter304 InkStroke304 => _inkMeter;
        public Vector2 InkMotion => _inkMotion;
        public HudLockOn304 LockOn304 => _lockOn;
        public HudPrompt304 Prompt304 => _prompt;
        public HudBearingLine304 Bearing304 => _bearing;
        public HudMinimap304 Minimap304 => _minimap;
        public HudBossBar304 BossBar304 => _bossBar;
        public InkMeter304 TargetHpStroke304 => _targetHp != null ? _targetHp.Meter : null;
        public bool TargetHpVisible => _targetHpGroup != null && _targetHpGroup.gameObject.activeSelf;
        public Canvas Canvas => _canvas;
        /// <summary>#308: the liquid cluster (null while the #304 meters are in use).</summary>
        public HudVessels308 Vessels308 => _vessels;

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
            _style = V.Style(Skin);
            _tokens = HudTokens304.Load();
            V.EnsureCanvasChannels(_canvas);
            BuildDangerEdges();
            // #308: vessels while the liquid profile exists and is enabled, else the #304 meters (rollback = profile.Enabled off)
            var liquid = Liquid308 != null ? Liquid308 : Resources.Load<HudLiquid308ProfileSO>(HudLiquid308ProfileSO.ResourcePath);
            if (liquid != null && liquid.Enabled) BuildVessels308(liquid); else BuildMeters304();
            _bearing = HudBearingLine304.Create(_style, _tokens, _canvas.transform, Icons);
            _minimap = HudMinimap304.Create(_style, _tokens, _canvas.transform, Minimap, Icons);
            if (_style.Sprites.Disc == null) _diskSprite = CreateInkDisk();
            _lockOn = HudLockOn304.Create(_style, _tokens, _canvas.transform, Skin.LockRing, _diskSprite);
            _reticle = (RectTransform)_lockOn.transform;
            BuildTargetHp306();
            _bossBar = HudBossBar304.Create(_style, _tokens, _canvas.transform, EnemyBars);
            _prompt = HudPrompt304.Create(_style, _tokens, _canvas.transform);
        }

        /// <summary>#306 "TargetHpStroke": a sibling of the Reticle (not a child: the enso's idle α.45, size tween and pop must not
        /// thin or scale it), centred under the enso every frame by PlaceTargetHp306. Hidden until SetTargetHealth gives it a
        /// live, non-boss target.</summary>
        private void BuildTargetHp306()
        {
            var e = EnemyBars;
            var root = V.Rect("TargetHpStroke", _canvas.transform, 0f, 0f, e.TargetSize.x, e.TargetSize.y);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            _targetHpGroup = root.gameObject.AddComponent<CanvasGroup>();
            _targetHpGroup.blocksRaycasts = false; _targetHpGroup.interactable = false; _targetHpGroup.alpha = e.TargetIdleAlpha;
            _targetHp = new HudHealthStroke304(_style, _tokens, root, "TargetHp_Stroke", 0f, 0f, e.TargetSize.x, e.TargetSize.y, e.ChipAlpha);
            root.gameObject.SetActive(false);
        }

        /// <summary>#308 (SPEC-HUD-LIQUID-308 §5.1): "Vessels308" with the HP vessel "HP_BrushStroke" (cinnabar), the ink vessel
        /// "Ink_BrushBar" (ink) and the dodge / jump / vehicle marks. Meters304, Ink_ReceivedSegment, Ink_CostPreview and InkBottle
        /// are not built. Not tilted: a liquid surface reads only when it is level.</summary>
        private void BuildVessels308(HudLiquid308ProfileSO liquid)
        {
            _vessels = HudVessels308.Create(liquid, _style, _tokens, _canvas.transform, Skin, _hp01, _ink01);
            if (_vessels == null) BuildMeters304();
        }

        /// <summary>DESIGN §5.9 + D07: one tilted bundle (BottomLeft, origin (64,900), -1.2° about its left middle) holding the
        /// cinnabar HP InkMeter with its lost-share lag (HP_BrushStroke, 500 x 30), the ink InkMeter (Ink_BrushBar, 420 x 21)
        /// and the 먹병 44 x 72 (paper rim, ink liquid Filled Vertical = the ink value, ink glass). Replaces the old hanji backing,
        /// full-stroke tracks and heart / ink icons.</summary>
        private void BuildMeters304()
        {
            var s = _style; var m = s.Meter; var k = _tokens;
            var group = V.RectAnchored("Meters304", _canvas.transform, m.Origin.x, m.Origin.y, m.GroupSize.x, m.GroupSize.y, m.Anchor, new Vector2(0f, .5f), m.TiltDeg);
            float bottleMid = m.BottleOffset.y + m.BottleSize.y * .5f;
            float inkY = bottleMid - k.InkHeight * .5f;
            float hpY = inkY - k.MeterGap - k.HpHeight;
            _hpMeter = V.Meter(s, group, "HP_BrushStroke", m.HpOffset.x, hpY, m.HpSize.x, k.HpHeight, s.Cinnabar, true);
            _inkMeter = V.Meter(s, group, "Ink_BrushBar", m.InkOffset.x, inkY, m.InkSize.x, k.InkHeight, s.Ink);
            _hpFill = _hpMeter.Rect; _inkFill = _inkMeter.Rect;
            LinearLook(_hpMeter); LinearLook(_inkMeter);
            _hpMeter.SetValue(_hp01, false); _inkMeter.SetValue(_ink01, false);

            // received ink (NotifyInkGained): the fresh share of the ink stroke flashes mist and fades (.45 s)
            var received = new GameObject("Ink_ReceivedSegment", typeof(RectTransform), typeof(RectMask2D));
            received.transform.SetParent(group, false);
            _inkReceivedMask = (RectTransform)received.transform;
            _inkReceivedMask.anchorMin = _inkReceivedMask.anchorMax = _inkReceivedMask.pivot = new Vector2(0f, 1f);
            var flash = V.Rect("Received_BrushStroke", received.transform, 0f, 0f, m.InkSize.x, k.InkHeight);
            _inkReceivedGraphic = flash.gameObject.AddComponent<InkMeterGraphic>();
            _inkReceivedGraphic.raycastTarget = false;
            if (s.Materials.InkMeterBody != null) _inkReceivedGraphic.material = s.Materials.InkMeterBody;
            _inkReceivedGraphic.color = UiStyle304SO.A(s.Mist, 0f);
            received.SetActive(false);

            // 작도 중 먹 비용 (DESIGN §5.9, states.png): a dashed box of the cost width starting at the ink value, paper; cinnabar
            // (CinnabarLift, it sits on ink) when the cost is more than the ink left. Driven by SetInkCostPreview.
            _inkCostBox = V.SpriteImage(group, "Ink_CostPreview", s.Sprites.DashBox, s.Paper, m.InkOffset.x, inkY, 1f, k.InkHeight, 0f, 2f);   // dash_box border 12 -> 6 px on the 21 px stroke
            _inkCostBox.gameObject.SetActive(false);

            var bottle = V.Rect("InkBottle", group, m.BottleOffset.x, m.BottleOffset.y, m.BottleSize.x, m.BottleSize.y);
            var b = s.Sprites;
            if (b.BottleRim != null) V.Image(V.Stretch("BottleRim", bottle), s.Paper, b.BottleRim).preserveAspect = true;
            _inkLiquid = V.Image(V.Stretch("InkLiquid", bottle), s.Ink, b.BottleLiquid);
            _inkLiquid.type = Image.Type.Filled; _inkLiquid.fillMethod = Image.FillMethod.Vertical;
            _inkLiquid.fillOrigin = (int)Image.OriginVertical.Bottom; _inkLiquid.fillAmount = _ink01;
            if (b.BottleLiquid == null) _inkLiquid.enabled = false;
            var glass = V.Image(V.Stretch("BottleGlass", bottle), s.Ink, b.BottleGlass != null ? b.BottleGlass : Skin.InkBottle);
            glass.preserveAspect = true;
        }

        /// <summary>QA1 (after/hud.png, after_states/hud_lowhp.png): in the Linear project the paper ValueRim (α.72) under the
        /// whole value showed through the value body's own alpha (~.74-.96) and was blended in linear light, so the 먹 value read as
        /// mid grey (~112 vs ~64 in hud.png) and the HP value as pink (192,120,110 vs 188,90,77); the ghost α.27 read as ~α.12.
        /// The value rim is now a rim only (rim minus the value body, the GhostEdge mode) and the ghost ink alpha is the
        /// linear-compensated HudTokens304.InkAlpha. Fallback materials (no UI/InkMeter) keep the plain layers.</summary>
        // QA2 (after2/hud.png, hud_lowhp.png): with the foundation remap in UI/InkMeter (_LinearInkGamma) the paper rims were
        // erased (light remap a^2.2: ghost edge .3 -> .07, value rim .72 -> .49 - no 한지 테, unlike hud.png / states.png "가득")
        // and the values went flat (먹 ~25 vs ~64, HP (184,56,42) with no streaks). Now: the value sits on the mockup's paper plate
        // again (MeterValueRimEdgeOnly false), the paper layers are pre-scaled to land at a^1.3 (HudTokens304.PaperAlpha), the value
        // alpha is MeterValueAlpha .92 and the cinnabar lag undoes the dark remap. Offline twin: qa2/hud_metersim.png.
        private void LinearLook(InkMeter304 meter)
        {
            if (meter == null || _tokens == null || _style == null) return;
            var m = _style.Meter;
            if (meter.ValueRim != null && meter.ValueRim.UsesMeterShader) meter.ValueRim.EdgeOnly = _tokens.MeterValueRimEdgeOnly;
            if (meter.GhostEdge != null) meter.GhostEdge.color = UiStyle304SO.A(_style.Paper, _tokens.PaperAlpha(meter.GhostEdge.material, m.GhostEdge));
            if (meter.ValueRim != null) meter.ValueRim.color = UiStyle304SO.A(_style.Paper, _tokens.PaperAlpha(meter.ValueRim.material, m.RimAlpha));
            if (meter.Ghost != null) meter.Ghost.color = UiStyle304SO.A(_style.Ink, _tokens.InkAlphaFor(meter.Ghost.material, m.GhostInk));
            if (meter.Lag != null && _tokens.LagUndoRemap) meter.Lag.color = UiStyle304SO.A(_style.Cinnabar, _tokens.UndoInkRemap(meter.Lag.material, m.LagAlpha));
            if (meter.Value != null && HudTokens304.ShaderInkGamma(meter.Value.material) > 1.0001f)
            { var c = meter.Value.color; c.a = _tokens.MeterValueAlpha; meter.Value.color = c; }
        }

        /// <summary>Kept for callers of the old icon mode. #304 shows the keycap + verb prompt instead; icons are not drawn.</summary>
        public void SetInteractionIcon(Sprite sprite, bool problem = false) { }

        /// <summary>Low-HP ink edges (DESIGN §5.9 "LowHp_InkEdge 먹 가장자리"): ramp_bottom veil washes on all four canvas edges,
        /// resized to the live canvas (the old fixed 1440 / 900 strokes left the corners of a 1920 canvas bare).
        /// QA2: on the shared UI/InkReveal material (fully drawn, reveal 1) so the foundation's linear-space ink remap gives the
        /// veil its mockup weight; as plain UI/Default images they lost the QA1 compensation (after2/hud_lowhp.png: top edge -14
        /// grey levels at HP .28, sides -6).</summary>
        private void BuildDangerEdges()
        {
            var s = _style; var k = _tokens;
            _dangerEdges = new Image[4];
            var sprite = s.Sprites.RampBottom != null ? s.Sprites.RampBottom : Skin.HpStroke;
            for (int i = 0; i < _dangerEdges.Length; i++)
            {
                var edge = CreateImage("LowHp_InkEdge_" + i, _canvas.transform, sprite, UiStyle304SO.A(s.Veil, 0f));
                InkRevealEffect.On(edge, s, InkRevealMode.Edges, 1f);
                var r = edge.rectTransform;
                r.pivot = new Vector2(.5f, .5f);
                if (i == 0) { r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.localScale = new Vector3(1f, -1f, 1f); }
                else if (i == 1) { r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(1f, 0f); }
                else if (i == 2) { r.anchorMin = r.anchorMax = new Vector2(0f, .5f); r.localRotation = Quaternion.Euler(0f, 0f, -90f); }
                else { r.anchorMin = r.anchorMax = new Vector2(1f, .5f); r.localRotation = Quaternion.Euler(0f, 0f, 90f); }
                _dangerEdges[i] = edge;
            }
            FitDangerEdges(true);
        }

        private void FitDangerEdges(bool force)
        {
            if (_dangerEdges == null || _canvas == null || _tokens == null) return;
            Vector2 size = ((RectTransform)_canvas.transform).rect.size;
            if (!force && size == _edgeCanvasSize) return;
            _edgeCanvasSize = size;
            float tb = _tokens.EdgeTopBottom, side = _tokens.EdgeSide;
            for (int i = 0; i < _dangerEdges.Length; i++)
            {
                var r = _dangerEdges[i].rectTransform;
                if (i == 0) { r.sizeDelta = new Vector2(0f, tb); r.anchoredPosition = new Vector2(0f, -tb * .5f); }
                else if (i == 1) { r.sizeDelta = new Vector2(0f, tb); r.anchoredPosition = new Vector2(0f, tb * .5f); }
                else if (i == 2) { r.sizeDelta = new Vector2(size.y, side); r.anchoredPosition = new Vector2(side * .5f, 0f); }
                else { r.sizeDelta = new Vector2(size.y, side); r.anchoredPosition = new Vector2(-side * .5f, 0f); }
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
            if (_vessels != null) _vessels.SetHp01(value);
            else if (_hpMeter != null) _hpMeter.SetValue(value);
            else if (_hpFill != null) _hpFill.localScale = new Vector3(value, 1f, 1f);
            if (_dangerEdges == null || Skin == null || _tokens == null) return;
            // QA1 (hud_lowhp at HP .28): the old ramp gave α.048 there (edge ~4 grey levels, invisible). The onset share makes
            // the ink edge read as soon as HP is below DangerBeginsAtHp. QA2: the edges are on UI/InkReveal, whose remap gives the
            // veil its sRGB (mockup) weight; InkAlphaFor only compensates a plain material (fallback style without the material).
            float alpha = _tokens.DangerAlpha(value, Skin.DangerBeginsAtHp, Skin.MaximumDangerAlpha);
            for (int i = 0; i < _dangerEdges.Length; i++)
            {
                Color color = _dangerEdges[i].color;
                color.a = alpha > 0f ? _tokens.InkAlphaFor(_dangerEdges[i].material, alpha * (i < 2 ? 1f : _tokens.EdgeSideAlpha)) : 0f;
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
            if (_vessels != null) _vessels.SetInk01(value);
            else if (_inkMeter != null)
            {
                _inkMeter.SetValue(value, false);
                if (_inkLiquid != null) _inkLiquid.fillAmount = value;
                if (_inkCost01 > 0f) SetInkCostPreview(_inkCost01);
            }
            else if (_inkFill != null) _inkFill.localScale = new Vector3(value, 1f, 1f);
        }

        /// <summary>#304 작도 중 먹 비용 (DESIGN §5.9): cost of the stroke being drawn as a share of the full ink stroke; 0 hides.
        /// Paper dashed box after the ink value; cinnabar when the ink left cannot pay it (the cast will fizzle). No caller yet.</summary>
        public void SetInkCostPreview(float cost01)
        {
            _inkCost01 = Mathf.Clamp01(cost01);
            if (_vessels != null) { _vessels.SetInkCostPreview(_inkCost01); return; }   // #308: a dashed line under the surface
            if (_inkCostBox == null || _inkMeter == null) return;
            bool on = _inkCost01 > .0001f && _inkCostBox.sprite != null;
            if (_inkCostBox.gameObject.activeSelf != on) _inkCostBox.gameObject.SetActive(on);
            if (!on) return;
            var meter = _inkMeter.Rect;
            float w = meter.sizeDelta.x, bw = Mathf.Max(8f, _inkCost01 * w);
            float x = Mathf.Min(_ink01 * w, Mathf.Max(0f, w - bw));
            var r = _inkCostBox.rectTransform;
            r.anchoredPosition = meter.anchoredPosition + Vector2.right * x;
            r.sizeDelta = new Vector2(bw, meter.sizeDelta.y);
            _inkCostBox.color = _inkCost01 > _ink01 + .0001f ? _style.CinnabarLift : _style.Paper;
        }

        public float InkCostPreview01 => _inkCost01;

        public void NotifyInkGained(float actualReceived) => NotifyInkGained(actualReceived, _ink01);

        /// <summary>#306: a received range ending at to01 (the fill right after that Gain - a chunk's flash lands later, at the
        /// absorb). Clamped to the live meter: ink spent meanwhile is never highlighted, and nothing shows if all of it is gone.</summary>
        public void NotifyInkGained(float actualReceived, float to01)
        {
            if (_vessels != null) { _vessels.NotifyInkGained(actualReceived, to01); return; }   // #308: the fresh share reads lighter
            if (actualReceived <= 0f || _inkReceivedGraphic == null) return;
            float to = Mathf.Min(Mathf.Clamp01(to01), _ink01);
            float from = Mathf.Max(0f, Mathf.Clamp01(to01) - actualReceived);
            if (to <= from) return;
            float now = Time.unscaledTime;
            // Harvest pays once per gameplay frame. A long render frame must not split one held
            // extraction into repeated flashes; a real gap in both clocks starts a fresh range.
            bool continuousFrames = Time.frameCount >= _receivedLastFrame && Time.frameCount - _receivedLastFrame <= 2;
            if (now - _receivedLast > .14f && !continuousFrames) { _receivedFrom = from; _receivedTo = to; _receivedBegan = now; }
            else { _receivedFrom = Mathf.Min(_receivedFrom, from); _receivedTo = Mathf.Max(_receivedTo, to); }
            _receivedLast = now; _receivedLastFrame = Time.frameCount;
        }

        private void UpdateInkReceived()
        {
            if (_inkReceivedGraphic == null || _inkMeter == null) return;
            float age = Time.unscaledTime - _receivedLast;
            float life = Mathf.Max(.05f, _tokens.ReceivedSeconds);
            bool visible = age < life && _receivedTo > _receivedFrom;
            if (_inkReceivedMask.gameObject.activeSelf != visible) _inkReceivedMask.gameObject.SetActive(visible);
            if (!visible) return;
            var meter = _inkMeter.Rect;
            float width = meter.sizeDelta.x, height = meter.sizeDelta.y;
            _inkReceivedMask.anchoredPosition = meter.anchoredPosition + Vector2.right * (_receivedFrom * width);
            _inkReceivedMask.sizeDelta = new Vector2((_receivedTo - _receivedFrom) * width, height);
            var flash = _inkReceivedGraphic.rectTransform;
            flash.anchoredPosition = Vector2.left * (_receivedFrom * width);
            flash.sizeDelta = new Vector2(width, height);
            _inkReceivedGraphic.Fill = _receivedTo;
            float attack = Mathf.Clamp01((Time.unscaledTime - _receivedBegan) / .06f);
            var color = _inkReceivedGraphic.color;
            color.a = _tokens.ReceivedAlpha * attack * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(life * .35f, life, age)));
            _inkReceivedGraphic.color = color;
        }

        // #306 contract (IHealthBarSource306): enemy health under the lock-on enso and the boss bar. Track C calls these from
        // CombatLoopWiring (null hides); track U draws them. No element colour on either bar.
        /// <summary>The lock-on target's stroke: hidden for null, dead or boss targets (a boss uses SetBossHealth) and while the
        /// reticle is hidden; a new target snaps value and chip (no chip carried over). Safe to call every frame.</summary>
        public void SetTargetHealth(Oheangbu.Combat.IHealthBarSource306 target)
        {
            if (_targetHp == null) return;
            target = HudHealthStroke304.Live(target);
            _targetHpWanted = target != null && target.Alive && !target.IsBoss;
            _targetHp.Bind(_targetHpWanted ? target : null);
            PlaceTargetHp306();
        }

        /// <summary>The engaged boss's bar at the bottom (null = fade out: disengaged, leash reset, player death). Safe to call every frame.</summary>
        public void SetBossHealth(Oheangbu.Combat.IHealthBarSource306 boss) { if (_bossBar != null) _bossBar.Set(boss); }

        /// <summary>The boss bar gone at once, any dead-boss hold dropped (player death, teardown). Safe to call every frame.</summary>
        public void HideBossHealthNow() { if (_bossBar != null) _bossBar.HideNow(); }

        private void PlaceTargetHp306()
        {
            if (_targetHpGroup == null) return;
            var live = HudHealthStroke304.Live(_targetHp.Source);
            bool on = _targetHpWanted && live != null && live.Alive && _reticle != null && _reticle.gameObject.activeSelf && !_lockPreview;
            var go = _targetHpGroup.gameObject;
            if (go.activeSelf != on) go.SetActive(on);
            if (!on) return;
            _targetHp.Tick();
            var e = EnemyBars;
            float half = _lockOn != null ? _lockOn.Size * .5f : _reticle.sizeDelta.y * .5f;
            go.transform.localPosition = _reticle.localPosition + Vector3.down * (half + e.TargetGap + e.TargetSize.y * .5f);
            // briefly darker after a hit (hold, then back to the resting weight)
            float since = Time.unscaledTime - _targetHp.LastHitTime, hold = e.TargetHitHoldMs / 1000f, fade = Mathf.Max(.01f, e.TargetHitFadeMs / 1000f);
            _targetHpGroup.alpha = Mathf.Lerp(e.TargetHitAlpha, e.TargetIdleAlpha, Mathf.Clamp01((since - hold) / fade));
        }

        public void SetGroggy01(float value)
        {
            value = Mathf.Clamp01(value);
            _groggy01 = value;
            if (_lockPreview) return;                             // capture preview holds its own groggy value
            if (_lockOn != null) _lockOn.SetGroggy01(value);
            else if (_groggyFill != null) _groggyFill.localScale = Vector3.one * value;
        }

        /// <summary>Capture / tour preview (Art/UI304/screens/harness.md `lock:`): shows the lock-on enso at the mockup point
        /// (1157, 592 of 1920x1080) with `groggy01`, holding it against UpdateReticle until called with on = false.
        /// Gameplay values (Groggy01) are not changed.</summary>
        public void PreviewLockOn304(bool on, float groggy01)
        {
            if (_reticle == null) return;
            _lockPreview = on;
            if (on)
            {
                _reticle.gameObject.SetActive(true);
                _reticle.position = new Vector3(Screen.width * (1157f / 1920f), Screen.height * (1f - 592f / 1080f), 0f);
                if (_lockOn != null) { _lockOn.Snap(); _lockOn.SetGroggy01(groggy01); }
                else if (_groggyFill != null) _groggyFill.localScale = Vector3.one * Mathf.Clamp01(groggy01);
            }
            else
            {
                _reticle.gameObject.SetActive(false);
                if (_lockOn != null) _lockOn.SetGroggy01(_groggy01);
                else if (_groggyFill != null) _groggyFill.localScale = Vector3.one * _groggy01;
            }
        }

        public bool LockPreview304 => _lockPreview;

        public void SetInteractionText(string value)
        {
#if UNITY_EDITOR
            if (_editorDiagnosticState) return;
#endif
            ApplyInteractionText(value);
        }

        // #304: the icon-mode early return is gone (IMPLEMENTATION §7.1) - the prompt is [keycap] + verb phrase again.
        private void ApplyInteractionText(string value)
        {
            if (_prompt == null) return;
            if (HideText) { _prompt.Hide(true); return; }
            if (string.IsNullOrEmpty(value)) _prompt.Hide();
            else _prompt.Show(value);
        }

        /// <summary>Kept for the presenter (normalized local velocity). The #304 먹병 shows the ink value only; it does not slosh
        /// (DESIGN §6: no ambient motion on HUD surfaces).</summary>
        public void SetInkMotion(Vector2 normalizedLocalVelocity)
        {
            _inkMotion = Vector2.ClampMagnitude(normalizedLocalVelocity, 1f);
        }

        public void KickInk(float strength = 1f)
        {
            _inkHitImpulse = Mathf.Max(_inkHitImpulse, Mathf.Clamp01(strength));
            if (_vessels != null) _vessels.KickInk(strength);
        }

        // #308 liquid HUD inputs (SPEC-HUD-LIQUID-308 §5.1). All are no-ops while the #304 meters are in use.
        /// <summary>D308-11b: what the player did this frame, as measured by the presenter (ActionMeter308: speed changes of the
        /// body, fast view turning, take-off, landing). The liquids answer it in proportion; a zero sample is nothing.</summary>
        public void AddLiquidAction308(in ActionSample308 sample) { if (_vessels != null) _vessels.AddAction(in sample); }
        /// <summary>D308-11b: the key glyph cells of the dodge / jump / vehicle marks, picked from the real bindings.</summary>
        public void SetKeys308(in HudKeys308 keys) { if (_vessels != null) _vessels.SetKeys(in keys); }
        /// <summary>Inputs of the dodge / jump / vehicle marks (HudActionRules308 decides how they are drawn).</summary>
        public void SetActionState308(in HudActionState308 state) { if (_vessels != null) _vessels.SetActionState(in state); }
        /// <summary>Ink below this (one spell cost) splits into dry-brush streaks.</summary>
        public void SetLowInkThreshold308(float ink01) { if (_vessels != null) _vessels.SetLowInkThreshold(ink01); }
        /// <summary>"UI 크기" (only the liquid cluster follows it) and "움직임 줄이기".</summary>
        public void SetUserSettings308(float uiScale, bool reducedMotion) { if (_vessels != null) _vessels.SetUserSettings(uiScale, reducedMotion); }

        public void PulseReticle() { _pulse = 1f; if (_lockOn != null) _lockOn.Pulse(); }

        public void UpdateReticle(Transform target, Camera cam)
        {
#if UNITY_EDITOR
            if (_editorDiagnosticState) return;
#endif
            if (_lockPreview) return;
            _pulse = Mathf.Max(0f, _pulse - Time.unscaledDeltaTime * 4f);
            bool visible = target != null && cam != null;
            if (_reticle == null) return;
            bool wasVisible = _reticle.gameObject.activeSelf;
            _reticle.gameObject.SetActive(visible);
            if (!visible) return;
            float chest = _style != null ? _style.LockOn.ChestHeight : 1.1f;
            Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * chest);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height)
            { _reticle.gameObject.SetActive(false); return; }
            _reticle.position = screen;
            if (_lockOn != null) { if (!wasVisible) _lockOn.Snap(); }
            else _reticle.localScale = Vector3.one * (1f + .5f * _pulse * _pulse);
            PlaceTargetHp306();
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
            FitDangerEdges(false);
            UpdateInkReceived();
            PlaceTargetHp306();
            if (_vessels != null) _vessels.Tick();   // #308: slosh, level, marks, impact-frame reaction
            _inkHitImpulse = Mathf.MoveTowards(_inkHitImpulse, 0f, Time.unscaledDeltaTime * 2.8f);
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

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void OnDestroy()
        {
            if (_diskSprite != null) Destroy(_diskSprite);
            if (_diskTexture != null) Destroy(_diskTexture);
        }
    }
}
