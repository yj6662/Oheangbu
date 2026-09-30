using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #304 FINAL (붓의 몸짓) design tokens. Values come from Art/UI304/design/FINAL/DESIGN.md + IMPLEMENTATION.md §1/§6.
    // Every enum below is serialized as int in UiStyle304.asset: APPEND new members only, never reorder.

    /// <summary>Design font roles (DESIGN §3.1). ui304-setup maps them to the SDF assets in Art/UI/UI304/FontAssets:
    /// Serif600/700 -> NotoSerifKR-600 SDF, Serif800 -> NotoSerifKR-800 SDF, Serif900 -> NotoSerifKR-900 SDF (static instances,
    /// they carry the hanja), Prose -> NanumMyeongjo SDF (fallback NotoSerifKR-800 for hanja, then NotoSansKR-Bold),
    /// Sans400 -> NotoSansKR-Regular SDF, Sans700 -> NotoSansKR-Bold SDF; Serif fallback = NotoSansKR-Regular.
    /// When the Noto Serif assets are missing (or "ui304-setup:nanum") Serif600/700 -> NanumMyeongjoBold and
    /// Serif800/900 -> NanumMyeongjoExtraBold with fallback NotoSansKR-Bold. Read the live mapping with ui304-report.</summary>
    public enum UiFont304 { Serif600 = 0, Serif700 = 1, Serif800 = 2, Serif900 = 3, Prose = 4, Sans400 = 5, Sans700 = 6 }

    /// <summary>TMP material presets (IMPLEMENTATION §3.3). Paper and Ink resolve to the font asset's own material
    /// (colour is always TMP_Text.color, so they batch together); the other four are material assets per font.</summary>
    public enum TmpPreset304 { Paper = 0, Ink = 1, Ink_UnderPaper = 2, Paper_UnderInk = 3, Rubbing = 4, Rubbing_Lite = 5 }

    /// <summary>Every text role used by the 12 mockups (DESIGN §3.2 scale, one id per size actually used).</summary>
    public enum UiType304
    {
        Lockup150 = 0, Glyph240 = 1, Region72 = 2, CellGlyph64 = 3, Speaker60 = 4, ChipGlyph56 = 5, ToastGlyph50 = 6, Region48 = 7,
        Heading44 = 8, MapRegion44 = 9, BearingMajor24 = 10,
        Display50 = 11, Headline44 = 12, Figure40 = 13, Figure36 = 14, Title36 = 15, Title34 = 16, Title32 = 17, Title30 = 18, Title26 = 19,
        Title24 = 20, MapTitle24 = 21,
        Serif700_28 = 22, Serif700_24 = 23, Serif700_22 = 24, MapLabel21 = 25, Serif700_20 = 26, BearingMinor20 = 27, BearingLabel20 = 28,
        Label28 = 29, Label27 = 30, Label26 = 31, Label24 = 32, Label22 = 33,
        Prose28 = 34, Body24 = 35, ValueBold24 = 36, Body22 = 37, Meta20 = 38, MetaBold20 = 39,
        Keycap18 = 40, KeycapSm16 = 41, OrderNo16 = 42,
        Title28 = 43, Serif900_28 = 44, Serif900_22 = 45, Serif800_20 = 46,
    }

    /// <summary>Brush-stroke sprite classes (IMPLEMENTATION §2). WetS/WetM/Band are 9-sliced underlays (V.Stroke);
    /// the others are Simple stretched sprites (V.Brush). Auto = pick WetS/WetM/Band from the rect height
    /// (UiStyle304SO.StrokeFor: heights 118~124 are ambiguous and resolve to WetS; pass WetM explicitly for options rows).</summary>
    public enum StrokeClass304 { WetS = 0, WetM = 1, Band = 2, Dry = 3, Line = 4, Hair = 5, HairRim = 6, Swell = 7, Short = 8, Spine = 9, Sweep = 10, Auto = 99 }

    /// <summary>UI/InkReveal direction (IMPLEMENTATION §5.2): 0 번짐 bleed, 1 좌→우 wipe (stroke direction), 2 가장자리→안.</summary>
    public enum InkRevealMode { Bleed = 0, Wipe = 1, Edges = 2 }

    /// <summary>Screen corner / edge a HUD element is anchored to (DESIGN §7: the HUD is corner-anchored, the mockup px are
    /// converted to offsets from that point by PlaytestUiView.RectAnchored / PlaceAnchored).</summary>
    public enum UiAnchor304 { TopLeft = 0, TopCentre = 1, TopRight = 2, MiddleLeft = 3, Centre = 4, MiddleRight = 5, BottomLeft = 6, BottomCentre = 7, BottomRight = 8 }

    /// <summary>How V.FocusRow shows its keycap (DESIGN §5.8: filled = the screen's main action, hollow = back-out / shortcut).</summary>
    public enum FocusKeyMode304
    {
        [Tooltip("primary button: filled while unfocused, hollow once the row is focused (pause 계속하기 + Esc, confirm 확인 + Enter)")]
        FilledUntilFocused = 0,
        [Tooltip("shortcut: always hollow (pause list [I] [M]); the rim flips to ink while the row sits on its paper underlay")]
        Hollow = 1,
        [Tooltip("always filled (dialogue [Esc] on the last choice)")]
        Filled = 2,
        [Tooltip("filled, visible only while focused (dialogue [F] on the focused choice)")]
        FilledWhenFocused = 3,
    }

    /// <summary>One TMP role: font + size + CSS line-height + tracking + material preset.</summary>
    [Serializable]
    public sealed class TypeRole
    {
        public UiType304 Id;
        public UiFont304 Family;
        [Tooltip("Filled by ui304-setup from UiStyle304SO.Fonts; null = resolve through the style at runtime.")]
        public TMP_FontAsset Font;
        [Tooltip("px at the 1920x1080 reference")] public float Size = 24;
        [Tooltip("CSS line-height multiple (1.25 = 125 %). Converted to TMP lineSpacing per font by TmpLineSpacing().")]
        public float LineHeight = 1.25f;
        [Tooltip("Letter spacing in em/100 (TMP characterSpacing units). -1 = -1 %, 12 = .12em.")]
        public float Tracking;
        public TmpPreset304 Preset;
        [Tooltip("Preset material for Font, filled by ui304-setup. null = Font.material (Paper / Ink presets).")]
        public Material Material;

        public TypeRole() { }
        public TypeRole(UiType304 id, UiFont304 family, float size, float lineHeight, float tracking = 0, TmpPreset304 preset = TmpPreset304.Paper)
        { Id = id; Family = family; Size = size; LineHeight = lineHeight; Tracking = tracking; Preset = preset; }

        /// <summary>TMP lineSpacing (em/100) that makes the baseline pitch equal LineHeight x Size for this font.</summary>
        public float TmpLineSpacing(TMP_FontAsset font)
        {
            if (font == null || font.faceInfo.pointSize <= 0) return 0;
            float natural = font.faceInfo.lineHeight / font.faceInfo.pointSize;
            return (LineHeight - natural) * 100f;
        }

        /// <summary>CSS half-leading above the first line: add to a mockup "top" to get the TMP rect top
        /// (TMP puts the first ascender at the rect top, CSS centres the glyph box inside the line box).</summary>
        public float CssTopOffset(TMP_FontAsset font)
        {
            if (font == null || font.faceInfo.pointSize <= 0) return 0;
            float content = (font.faceInfo.ascentLine - font.faceInfo.descentLine) / font.faceInfo.pointSize * Size;
            return (LineHeight * Size - content) * .5f;
        }

        public bool IsPlainPreset => Preset == TmpPreset304.Paper || Preset == TmpPreset304.Ink;
    }

    [Serializable] public sealed class FontFamily304 { public UiFont304 Family; public TMP_FontAsset Font; public FontFamily304() { } public FontFamily304(UiFont304 f) { Family = f; } }

    [Serializable] public sealed class TmpMaterial304 { public TMP_FontAsset Font; public TmpPreset304 Preset; public Material Material; }

    /// <summary>Motion tokens (DESIGN §6, IMPLEMENTATION §6). Durations are ms, curves are the cubic-beziers as weighted
    /// AnimationCurves. THE authoritative place for every duration (meter lag, toast hold, arrival hold included).</summary>
    [Serializable]
    public sealed class MotionTokens304
    {
        [Tooltip("ease.stroke cubic-bezier(.22,1,.36,1): 획 긋기, 등장")] public AnimationCurve Stroke = UiEase304.Bezier(.22f, 1f, .36f, 1f);
        [Tooltip("ease.lift cubic-bezier(.4,0,.2,1): 사라짐, 붓 들기")] public AnimationCurve Lift = UiEase304.Bezier(.4f, 0f, .2f, 1f);
        [Tooltip("ease.press cubic-bezier(.3,0,.6,1): 누름")] public AnimationCurve Press = UiEase304.Bezier(.3f, 0f, .6f, 1f);
        public int TickMs = 90;             // 누름, 되돌림
        public int DabMs = 140;             // 방점 pop .6 -> 1.08 -> 1
        public int DabSlideMs = 160;        // 방점이 새 초점으로 미끄러짐
        public int HoverMs = 120;           // 올림 번짐 α 0 -> .16
        public int StrokeMs = 240;          // 받침 획 와이프 (s, m)
        public int StrokeBandMs = 320;      // 받침 획 와이프 (band)
        public int UnderlayOutMs = 120;     // 이전 받침이 붓 드는 방향으로 사라짐
        public int VeilMs = 220;            // 장막 긋기
        public int RevealMs = 280;          // 내용 번짐 등장
        public int RevealGapMs = 40;        // 그룹 간격
        public int SwellMs = 220;           // 탭 부풂 미끄러짐 (+ 탭 글자 scale)
        public int ToastInMs = 240, ToastHoldMs = 3600, ToastHoldPickupMs = 5000, ToastHoldErrorMs = 8000, ToastOutMs = 400;
        public int ArrivalInMs = 280, ArrivalHoldMs = 2400, ArrivalOutMs = 400;
        public int MeterLagHoldMs = 450, MeterLagDrainMs = 350;
        public int EnsoPopMs = 180;
        public int PromptInMs = 200, PromptOutMs = 120;
        public int HoldPressMs = 600;       // 길게 누르기: 동사구 밑 한지 선이 차오름
        public int MenuEnterBudgetMs = 520; // 장막 -> 레일 -> 내용 -> 방점
        public int RestInMs = 900, RestOutMs = 1200, DeathInMs = 700, DeathOutMs = 1400;
        [Tooltip("움직임 줄이기: every wipe / bleed becomes an alpha fade of this length; swell + dab jump.")]
        public int ReducedMs = 120;
        public float DabPopFrom = .6f, DabPopPeak = 1.08f, SwellSlideScaleX = 1.3f, EnsoPopScale = 1.12f;

        /// <summary>Seconds for a token, honouring reduced motion (every animated wipe becomes ReducedMs).</summary>
        public float Sec(int ms, bool reduced = false) => (reduced ? Mathf.Min(ms, ReducedMs) : ms) / 1000f;
    }

    /// <summary>HUD meters (DESIGN §5.9 / §7.1). Group origin is the top-left of the tilted bundle in mockup px; place the
    /// group with PlaytestUiView.RectAnchored(..., Anchor) so it stays in the bottom-left corner on any aspect.
    /// Lag timing lives in Motion (MeterLagHoldMs / MeterLagDrainMs).</summary>
    [Serializable]
    public sealed class MeterSpec304
    {
        public UiAnchor304 Anchor = UiAnchor304.BottomLeft;
        public Vector2 Origin = new Vector2(64, 900);
        public Vector2 GroupSize = new Vector2(620, 110);
        [Tooltip("rotation of the bundle about its left-middle (CSS transform-origin 0 50%)")] public float TiltDeg = -1.2f;
        public Vector2 HpOffset = new Vector2(60, 0), HpSize = new Vector2(500, 42);
        public Vector2 InkOffset = new Vector2(60, 56), InkSize = new Vector2(420, 30);
        public Vector2 BottleOffset = new Vector2(0, 34), BottleSize = new Vector2(44, 72);
        public Vector2 LoadingSize = new Vector2(640, 24);
        [Tooltip("tail length in px; 0 = rect height (TailPx = h)")] public float TailPx = 0;
        public float BlendPx = 16;
        [Tooltip("meter_body.png aspect 1200/64: body u = px / max(W, H * BodyAspect)")] public float BodyAspect = 1200f / 64f;
        public float GhostInk = .27f, GhostEdge = .3f, RimAlpha = .72f, LagAlpha = .34f;
        [Tooltip("max grows -> sizeDelta.x = base + PxPerMaxPercent x increase %")] public float PxPerMaxPercent = 5f;
    }

    /// <summary>방위 먹선 (DESIGN §5.14). Mockup px; anchored TopCentre (x 960 = the canvas centre on any aspect).</summary>
    [Serializable]
    public sealed class BearingSpec304
    {
        public UiAnchor304 Anchor = UiAnchor304.TopCentre;
        public Rect Line = new Rect(632, 70, 656, 22);
        public float CenterX = 960;
        [Tooltip("640 px over 180 deg (≈3.56)")] public float PxPerDegree = 640f / 180f;
        public float HalfRangeDeg = 90;
        public float MinorTickDeg = 15, MinorTickPx = 8, MajorTickDeg = 45, MajorTickPx = 14, TickWidth = 3, TickCenterY = 81;
        public Rect You = new Rect(958, 62, 4, 38);
        public float CardinalTop = 34, IntercardinalTop = 38, LabelWidth = 80;
        public float MarkerTop = 92, MarkerSize = 32;
        public Vector2 ObjectiveDabSize = new Vector2(26, 20); public float ObjectiveDabTop = 96, ObjectiveDabRot = -8;
        public float ObjectiveLabelDeg = 5, ObjectiveLabelTop = 120, ObjectiveLabelWidth = 240;
        [Tooltip("Inverse polarity when the top 12 % mean luminance is below this")] public float InverseLuma = .25f;
        public float HysteresisLow = .22f, HysteresisHigh = .28f, SampleInterval = .5f, TopBand = .12f;
        public int SampleDownscale = 16;
        public float RimAlpha = .95f;
    }

    /// <summary>알림 (DESIGN §5.10 / §7.1). Mockup px, anchored TopRight (x - 1920 from the right edge), so the stroke tail
    /// always leaves the screen. Hold times live in Motion (ToastHoldMs / ToastHoldPickupMs / ToastHoldErrorMs); use
    /// UiStyle304SO.ToastSeconds(kind). Horizontal layout is a RULE (Layout), not fixed x: glyph count varies 0..3 (+N).</summary>
    [Serializable]
    public sealed class ToastSpec304
    {
        public UiAnchor304 Anchor = UiAnchor304.TopRight;
        public Vector2 Origin = new Vector2(1296, 146);
        public float Height = 110;
        public int MaxVisible = 2;
        public float Pitch = 124;
        public int MaxGlyphs = 3;
        public float UnderlayAlpha = .92f, ErrorUnderlayAlpha = .94f;
        [Tooltip("first fragment glyph (Serif900 50, Rubbing_Lite); glyphs are GlyphGap apart (V.GlyphRow)")] public Vector2 GlyphPos = new Vector2(1346, 170);
        public float GlyphGap = 14;
        [Tooltip("divider x = glyph right + DividerGap (mockup: 1460 + 22 = 1482)")] public float DividerGap = 22;
        public float DividerY = 172, DividerH = 62, DividerAlpha = .3f;
        [Tooltip("title x = divider x + TitleGap (1482 + 24 = 1506)")] public float TitleGap = 24;
        [Tooltip("source x = title x + SourceDx (1507)")] public float SourceDx = 1;
        public float TitleY = 166, SourceY = 204;
        [Tooltip("title x of a toast without glyphs (info, realm, vehicle, service, wake)")] public float NoGlyphTitleX = 1360;
        [Tooltip("text ends inside x1760; only the stroke tail leaves the screen")] public float ContentRightMax = 1760;
        [Tooltip("error: cinnabar vertical dry stroke (rotated 90 about its centre) left of the title")] public Rect ErrorEdge = new Rect(1286, 196, 70, 12);
        public float ErrorEdgeRot = 90, ErrorEdgeAlpha = .95f;
        public float ErrorTitleX = 1360;

        /// <summary>Horizontal layout rule. hasGlyphs: glyphRight = right edge of the glyph row (+N label included), mockup px.
        /// Without glyphs dividerX is NaN (no divider) and the title starts at NoGlyphTitleX (error: ErrorTitleX).</summary>
        public void Layout(bool hasGlyphs, float glyphRight, bool error, out float dividerX, out float titleX, out float sourceX)
        {
            if (hasGlyphs) { dividerX = glyphRight + DividerGap; titleX = dividerX + TitleGap; }
            else { dividerX = float.NaN; titleX = error ? ErrorTitleX : NoGlyphTitleX; }
            sourceX = titleX + SourceDx;
        }

        /// <summary>px the whole toast (underlay included) must move LEFT so the text still ends inside ContentRightMax.</summary>
        public float Overflow(float contentRight) => Mathf.Max(0f, contentRight - ContentRightMax);
    }

    /// <summary>도착 카드 (DESIGN §5.11). Mockup px, anchored TopLeft. Hold time = Motion.ArrivalHoldMs.</summary>
    [Serializable]
    public sealed class ArrivalSpec304
    {
        public UiAnchor304 Anchor = UiAnchor304.TopLeft;
        public Rect Spine = new Rect(52, 150, 150, 500);
        public float SpineAlpha = .9f;
        public Vector2 RegionPos = new Vector2(80, 196); public float RegionWidth = 90;
        public Vector2 PlacePos = new Vector2(113, 360); public float PlaceWidth = 24, PlaceLineHeight = 1.14f;
        [Tooltip("names longer than this fall back to horizontal Serif700_22 under the spine")] public int MaxVerticalChars = 6;
    }

    /// <summary>상호작용 프롬프트 (DESIGN §5.8 / §7.1): ink wet_s α.93 from (736,836) h100, filled key (800,864), verb Label27
    /// paper (860,866). Anchored BottomCentre. Width = content + tail (V.Stroke rule). In / out = Motion.PromptInMs / PromptOutMs.</summary>
    [Serializable]
    public sealed class PromptSpec304
    {
        public UiAnchor304 Anchor = UiAnchor304.BottomCentre;
        public Vector2 Origin = new Vector2(736, 836);
        public float Height = 100, UnderlayAlpha = .93f;
        public Vector2 KeyPos = new Vector2(800, 864), LabelPos = new Vector2(860, 866);
        public UiType304 LabelRole = UiType304.Label27;
        [Tooltip("gap between the label's right edge and the stroke's content right")] public float ContentPad = 24;
    }

    /// <summary>락온 일원상 (DESIGN §5.14): cinnabar enso 80 + paper enso_rim behind, groggy disc 38 ink α.6 filled bottom-up.</summary>
    [Serializable]
    public sealed class LockOnSpec304
    {
        public float EnsoSize = 80, GroggySize = 38, GroggyAlpha = .6f;
        [Tooltip("world offset above the target pivot (target + up x ChestHeight)")] public float ChestHeight = 1.1f;
    }

    /// <summary>#306 먹 원상 미니맵 (SPEC-PLAYTEST-306 #1, D306; TEST). Mockup px, anchored BottomRight: the enso's centre and
    /// diameter; the map disc inside it is Window of the diameter. Ranges are the window's half extent in metres. The window
    /// ink is re-printed only after ReprintMetres of travel (or a map change), so InkPadMetres must stay above it.
    /// Serialized on HudController.Minimap (no UiStyle304 asset field yet).</summary>
    [Serializable]
    public sealed class MinimapSpec304
    {
        public UiAnchor304 Anchor = UiAnchor304.BottomRight;
        public Vector2 Centre = new Vector2(1740, 900);
        public float Diameter = 232;
        [Tooltip("map disc diameter as a share of the enso (inside the brush ring)")] public float Window = .88f;
        [Tooltip("half extent outside (m)")] public float OutsideMetres = 120;
        [Tooltip("half extent in caves and interiors (m): the walked cave plan")] public float CaveMetres = 40;
        [Tooltip("travel (m) before the window ink is printed again")] public float ReprintMetres = 15;
        [Tooltip("ink printed beyond the window (m); > ReprintMetres so the window never leaves the print")] public float InkPadMetres = 22;
        public float ArrowSize = 26, MarkerSize = 20, PinSize = 16, PinStroke = 5;
        [Tooltip("marks closer than this to the window edge are left off (px)")] public float MarkerRimInset = 10;
        [Tooltip("north tick at the window edge (ink stroke_short, w x h px)")] public Vector2 NorthTick = new Vector2(4, 14);
        public float RimAlpha = .95f, MarkRimAlpha = .95f;
        public int FadeMs = 200;
        [Tooltip("marker refresh (IMapMarkerSource304.CollectMarkers) and settings poll interval (s)")] public float RefreshSeconds = .25f;
    }

    /// <summary>#306 enemy health (SPEC-PLAYTEST-306 #8, D306 부칙 of D304; TEST): the lock-on stroke under the enso and the boss
    /// bar. Ink value + paper trailing chip on the InkMeter304 layers; no cinnabar, no element colour on either (ART-UI).
    /// Mockup px; the boss bar is anchored BottomCentre with its name above it, left-aligned (Elden Ring layout).
    /// Serialized on HudController.EnemyBars.</summary>
    [Serializable]
    public sealed class EnemyBarSpec304
    {
        public Vector2 TargetSize = new Vector2(64, 5);
        [Tooltip("gap between the enso's bottom edge and the stroke (px)")] public float TargetGap = 6;
        [Tooltip("stroke alpha at rest and right after a hit (darker), hold and fade back (ms)")] public float TargetIdleAlpha = .72f, TargetHitAlpha = 1f;
        public int TargetHitHoldMs = 700, TargetHitFadeMs = 500;
        public UiAnchor304 BossAnchor = UiAnchor304.BottomCentre;
        [Tooltip("bar rect (mockup px, top-left): 720 x 8 centred on x960, clear of the prompt (<= y960) and the title-safe bottom 64")]
        public Rect BossBar = new Rect(600, 1004, 720, 8);
        [Tooltip("Label24 = Noto Serif KR 600")] public UiType304 BossNameRole = UiType304.Label24;
        [Tooltip("name bottom to bar top (px)")] public float BossNameGap = 6;
        public int BossInMs = 400, BossOutMs = 600;
        [Tooltip("the emptied bar stays this long after the boss dies before it fades (ms)")] public int BossDeadHoldMs = 900;
        [Tooltip("trailing chip (lost share, holds / drains with Motion.MeterLag*): paper at this alpha")] public float ChipAlpha = .5f;
    }

    /// <summary>건반 (DESIGN §5.8, common.css .key).</summary>
    [Serializable]
    public sealed class KeycapSpec304
    {
        public float Height = 40, SmallHeight = 34, MinWidth = 40, SmallMinWidth = 34, PadX = 10, SmallPadX = 8;
        [Tooltip("text sits above the lip (css padding-bottom 4)")] public float TextLift = 4;
        public float RimPx = 2, HollowLipPx = 4, HollowLipAlphaVeil = .28f, HollowLipAlphaPaper = .2f;
        [Tooltip("keycap to label gap inside a hint")] public float LabelGap = 10;
        public float KeyGap = 8;
    }

    /// <summary>탭 레일 (DESIGN §5.3, common.css .rail). THE rail tokens (UiStyle304SO.TabGap / RailY / RailLine forward here).</summary>
    [Serializable]
    public sealed class RailSpec304
    {
        public Rect Line = new Rect(64, 106, 1792, 16);
        public float LineAlpha = .45f;
        public float Left = 64, TabsBottom = 106, TabPadBottom = 14, TabGap = 44, KeyBottomMargin = 12, CloseGap = 40;
        public float SwellH = 36, SwellLeft = 26, SwellRight = 30, SwellBelow = 16, SwellAlpha = .95f;
        public UiType304 SelectedRole = UiType304.Title34, UnselectedRole = UiType304.Label24;
        public float HitHeight = 64;
    }

    /// <summary>Page -> where the veil's brush lifts (IMPLEMENTATION §1.1 VeilLiftX) + the optional 먹 위의 먹 sweep.</summary>
    [Serializable]
    public sealed class VeilLift304
    {
        [Tooltip("PlaytestUiRoot page id (일시정지, 옵션, 소지품, 술식 도감, 지도, 장비 상점 ...)")] public string Page;
        public float LiftX = 1960;
        public bool Sweep;
        [Tooltip("stroke_sweep 1700x620 top-left x, y and alpha (rotated -8 deg about its centre)")] public Vector3 SweepXYA;
        public VeilLift304() { }
        public VeilLift304(string page, float lift) { Page = page; LiftX = lift; }
        public VeilLift304(string page, float lift, Vector3 sweep) { Page = page; LiftX = lift; Sweep = true; SweepXYA = sweep; }
    }

    /// <summary>Slice data of one stroke class (IMPLEMENTATION §2 table).</summary>
    [Serializable]
    public sealed class StrokeSprite304
    {
        public StrokeClass304 Class;
        public Sprite Sprite;
        public float NativeW, NativeH;
        [Tooltip("9-slice left / right border px (top / bottom are 0)")] public float BorderL, BorderR;
        [Tooltip("height band this class is meant for (Auto selection)")] public float MinH, MaxH;
        public bool Sliced;
        public StrokeSprite304() { }
        public StrokeSprite304(StrokeClass304 c, float w, float h, float l, float r, float minH, float maxH, bool sliced)
        { Class = c; NativeW = w; NativeH = h; BorderL = l; BorderR = r; MinH = minH; MaxH = maxH; Sliced = sliced; }
    }

    /// <summary>Every texture of Art/UI304/design/FINAL/assets that Unity uses (+ the two round marks). Filled by ui304-setup.</summary>
    [Serializable]
    public sealed class UiSprites304
    {
        [Header("veil + strokes")]
        [Tooltip("Alpha8 3200x1080, RawImage")] public Texture2D VeilWash;
        public Sprite WetS, WetM, Band, Dry, Line, Hair, HairRim, Swell, Short, Dab, Spine, Sweep;
        [Header("rings, marks")]
        public Sprite Enso, EnsoRim, Arrow, Blot, DashBox, RampBottom;
        [Tooltip("disc.png 64², anti-aliased white + alpha: 획순 번호 원 24 (Cinnabar), 남긴 통보 점 18 (Ink over a 22 Sheet disc), 목적 고리 채움 α.07")]
        public Sprite Disc;
        [Tooltip("ring_dashed.png 160², 16 dashes (15:9), white + alpha: 들은 목적 권역 고리 (Cinnabar; list / legend CinnabarLift / Cinnabar)")]
        public Sprite RingDashed;
        [Header("paper (fixed colour)")]
        public Sprite SheetCodex, SheetMap, SheetStrip, SheetSlip, TileChip, SealBu, Keycap;
        [Header("오행 형상 도장")]
        public Sprite ElemWood, ElemFire, ElemEarth, ElemMetal, ElemWater;
        [Header("먹병")]
        public Sprite BottleGlass, BottleLiquid, BottleRim;
        [Header("shader / TMP inputs (Texture2D)")]
        public Texture2D MeterBody, MeterTail, MeterBodyRim, MeterTailRim, WashTile, RubbingMask, Ribbon;

        /// <summary>오행 도장 by element index 0..4 = 木 火 土 金 水.</summary>
        public Sprite Element(int index)
        {
            switch (index) { case 0: return ElemWood; case 1: return ElemFire; case 2: return ElemEarth; case 3: return ElemMetal; case 4: return ElemWater; default: return null; }
        }
    }

    /// <summary>Material bundle: the two UI shaders + TMP presets per font.</summary>
    [Serializable]
    public sealed class UiMaterials304
    {
        [Tooltip("UI/InkReveal, shared by every underlay / veil / toast (reveal comes per vertex through uv1)")] public Material InkReveal;
        [Tooltip("UI/InkMeter body: _BodyTex meter_body, _TailTex meter_tail")] public Material InkMeterBody;
        [Tooltip("UI/InkMeter rim: _BodyTex meter_body_rim, _TailTex meter_tail_rim, cut = body (ghost edge)")] public Material InkMeterRim;
        public List<TmpMaterial304> Tmp = new List<TmpMaterial304>();
    }

    [Serializable] public sealed class JamoRule304 { public string Jamo; public string Rule; public JamoRule304() { } public JamoRule304(string j, string r) { Jamo = j; Rule = r; } }

    [Serializable] public sealed class OptionValueHelp304 { public string Value; public string Meaning; public OptionValueHelp304() { } public OptionValueHelp304(string v, string m) { Value = v; Meaning = m; } }

    /// <summary>설정 설명 칸 (DESIGN §5.5): every value of the focused option with its meaning + optional warning.</summary>
    [Serializable]
    public sealed class OptionHelp304
    {
        [Tooltip("option label exactly as the Options page shows it (해상도, 화면 모드 ...)")] public string Option;
        public string Summary;
        public List<OptionValueHelp304> Values = new List<OptionValueHelp304>();
        [Tooltip("warning line drawn with the cinnabar vertical dry stroke; empty = none")] public string Warning;
        public OptionHelp304() { }
        public OptionHelp304(string option, string summary, string warning, params OptionValueHelp304[] values)
        { Option = option; Summary = summary; Warning = warning; Values = new List<OptionValueHelp304>(values); }
    }

    /// <summary>cubic-bezier -> AnimationCurve. Unity weighted tangents are exact 2D cubic Béziers, so the curve is the CSS easing.</summary>
    public static class UiEase304
    {
        public static AnimationCurve Bezier(float x1, float y1, float x2, float y2)
        {
            x1 = Mathf.Clamp(x1, 1e-4f, 1f); x2 = Mathf.Clamp(x2, 0f, 1f - 1e-4f);
            var k0 = new Keyframe(0f, 0f, 0f, y1 / x1, 0f, x1) { weightedMode = WeightedMode.Out };
            var k1 = new Keyframe(1f, 1f, (1f - y2) / (1f - x2), 0f, 1f - x2, 0f) { weightedMode = WeightedMode.In };
            return new AnimationCurve(k0, k1) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        /// <summary>Evaluates a token curve; a missing / empty curve is linear.</summary>
        public static float Eval(AnimationCurve curve, float t)
        {
            t = Mathf.Clamp01(t);
            return curve == null || curve.length < 2 ? t : curve.Evaluate(t);
        }
    }
}
