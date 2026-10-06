using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 지도 page chrome (DESIGN §7.6, map.png, 1920x1080 page px). Everything lives in mapLayer (above the menu layer);
    // the rail (지도 selected, [M] 닫기 "Close", Q / E) is PlaytestUiRoot's MapRail304 in MenuRailLayer304 above this layer.
    //   MapVeil304 (Page304): the page veil, lifted at 1960 (= full), faded with the fold on close
    //   TwiceFoldedHanji (the paper; WorldMapPresenter.cs)
    //   MapPage304 (Page304, CanvasGroup = mapControls, visible once the sheet is flat):
    //     bottom PaperMapControls: Current [R] / Whole [T] / ClearPin [X]
    //     on the paper: MapTitle (D12 vertical title slip with the D11 frame) and 북
    // D308-25 (map 5, text diet): nothing else. The left column (here line, legend rows, terrain cells), the right column
    // (places list, detail card, pointer hints) and the [L] row with its key are gone; the columns' room went to the sheet
    // (MapStyle304SO.SheetRect), and what is printed on the paper sits relative to the sheet's rect.
    // The page content is rebuilt on every open (text scale), like the other #304 pages.
    public sealed partial class WorldMapPresenter
    {
        RectTransform veilPage304, page304;
        CanvasGroup veilGroup304;
        RawImage veil304;
        InkRevealEffect veilReveal304;
        RectTransform titleSlip304, northLabel304, northLine304;
        Image titlePaper304, titleFrame304;
        TMP_Text titleText304, realmText304;
        FocusRow304 currentRow304, wholeRow304, clearPinRow304;
        InputAction keyCurrent304, keyWhole304, keyClearPin304;

        /// <summary>#304 optional hint from the caller before SetExpanded(true): true = the map replaces another open menu page
        /// (tab change), so the veil is already there and is not wiped in again. Consumed by the next open. Without it the map
        /// looks at the menu layer: a page still being torn down there means a tab change.</summary>
        public bool? OpenedFromPage304 { get; set; }

        // ------------------------------------------------------------------ build (Initialize)
        void BuildVeil304()
        {
            veilPage304 = V.Page304(FullRoot, "MapVeil304");
            veilGroup304 = veilPage304.GetComponent<CanvasGroup>();
            veil304 = V.Veil(style, veilPage304, "지도");
            veilReveal304 = veil304.GetComponent<InkRevealEffect>();
            veilGroup304.alpha = 0f; veilGroup304.interactable = veilGroup304.blocksRaycasts = false;
        }

        void BuildPageRoot304()
        {
            page304 = V.Page304(FullRoot, "MapPage304");
            mapControls = page304.GetComponent<CanvasGroup>();
            mapControls.alpha = 0f; mapControls.interactable = mapControls.blocksRaycasts = false;
        }

        void ApplyVeilFold304()
        {
            if (veilGroup304 == null) return;
            // open: the veil is there at once (wiped in by BuildPage304); close: it lifts in the last 18 % of the fold.
            // Tab change away from 지도 (the root already shows another page with its own veil + rail): the sheet folds away
            // over that page, so this veil steps aside at once instead of hiding the new page for half a second.
            var root = PlaytestUiRoot.Instance;
            bool handedOver = !targetExpanded && root != null && !string.IsNullOrEmpty(root.Page) && root.Page != "지도";
            veilGroup304.alpha = targetExpanded ? 1f : handedOver ? 0f : Mathf.SmoothStep(0, 1, Mathf.Clamp01(fold / .18f));
            veilGroup304.interactable = false; veilGroup304.blocksRaycasts = targetExpanded;
        }

        // ------------------------------------------------------------------ build (every open)
        void BuildPage304()
        {
            var s = style;
            var canvas = FullRoot.GetComponentInParent<Canvas>();
            V.EnsureCanvasChannels(canvas);
            FocusMark304.Attach(s, canvas != null ? (RectTransform)canvas.rootCanvas.transform : FullRoot);
            V.Clear(page304);
            currentRow304 = wholeRow304 = clearPinRow304 = null;

            bool firstOpen = !(OpenedFromPage304 ?? MenuPageUnderneath304());
            OpenedFromPage304 = null;

            BuildTitleSlip304();
            BuildControls304();

            // D308-25: the key line's three rows are the page's only focus targets
            if (currentRow304 != null) FocusMark304.Select(currentRow304.Button.gameObject);

            EnableInput304(true);
            if (firstOpen && veilReveal304 != null)
                UiTween304.RevealIn(veilReveal304, s, s.Motion.VeilMs, UiTween304.Token(veilReveal304)).Forget();
        }

        void EndPage304()
        {
            EnableInput304(false);
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && FullRoot != null && es.currentSelectedGameObject.transform.IsChildOf(FullRoot))
                es.SetSelectedGameObject(null);
        }

        /// <summary>PlaytestUiRoot clears its MenuLayer (V.Clear: deactivate now, destroy at the end of the frame) right before
        /// it opens 지도, so a child still there means another page was on screen (tab change: no veil wipe).</summary>
        bool MenuPageUnderneath304()
        {
            var canvasRoot = FullRoot != null && FullRoot.parent != null ? FullRoot.parent.parent : null;
            var menuLayer = canvasRoot != null ? canvasRoot.Find("MenuLayer") : null;
            return menuLayer != null && menuLayer.childCount > 0;
        }

        // D12: the title is a vertical slip on the paper's top-left (강토 지도 Serif 800 30 + realm 22), D11 frame; 북 kept.
        // #304 QA2: the words of the title are their own columns' runs with a TitleWordGap304 gap (UiText304.Vertical made
        // the space a whole 36 px line), and the D11 rim is the pause / title slips' stroke_line rim on the shared InkReveal
        // material (the slip_frame sprite at α.62 went through UI/Default: blended in linear light it printed ~α.35 grey).
        const string MapTitle304 = "강토 지도";
        const float TitleWordGap304 = 14f, SlipW304 = 92f, SlipPad304 = 18f;
        const float RimInset304 = 7f, RimOver304 = 2f, RimLineH304 = 6f, RimAlpha304 = .6f;   // = PlaytestUiRoot.Flow304SlipRim
        const float NorthLineDx304 = 18.5f, NorthLineDy304 = 30f;   // the ink stroke under 북, from the mark's top-left (map.png)
        readonly List<TMP_Text> titleWords304 = new List<TMP_Text>();
        Image[] titleRim304;
        bool titleRimBrushes304;

        void BuildTitleSlip304()
        {
            var s = style;
            // D308-25: what is printed on the paper sits relative to the sheet (MapStyle304SO.SheetRect), not at page constants
            Rect sheet = Sheet304;
            float northX = sheet.xMax - mapStyle.NorthOffset.x, northY = sheet.y + mapStyle.NorthOffset.y;
            titleSlip304 = V.Rect("MapTitle", page304, sheet.x + mapStyle.TitleSlipOffset.x, sheet.y + mapStyle.TitleSlipOffset.y, SlipW304, 230);
            titlePaper304 = V.SpriteImage(titleSlip304, "Slip", s.Sprites.SheetSlip, Color.white, 0, 0, SlipW304, 230);
            if (titlePaper304.sprite == null) titlePaper304.color = s.Sheet;
            BuildSlipRim304();
            titleWords304.Clear();
            string[] words = MapTitle304.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
                titleWords304.Add(V.Label(s, titleSlip304, i == 0 ? "Title" : "Title" + (i + 1), UiText304.Vertical(words[i]),
                    UiType304.Title30, s.Ink, 14, SlipPad304, 36, 0, TextAlignmentOptions.Top));
            titleText304 = titleWords304.Count > 0 ? titleWords304[0] : null;
            realmText304 = V.Label(s, titleSlip304, "Realm", "", UiType304.Serif700_22, s.Ash, 54, SlipPad304, 26, 0, TextAlignmentOptions.Top);
            var north = V.Label(s, page304, "North", "북", UiType304.Serif900_22, s.Ink, northX, northY, 40, 0, TextAlignmentOptions.Top);
            var under = s.TmpMaterial(north.font, TmpPreset304.Ink_UnderPaper); if (under != null) north.fontSharedMaterial = under;
            northLabel304 = north.rectTransform;
            northLine304 = V.Rect("NorthLine", page304, northX + NorthLineDx304, northY + NorthLineDy304, 3, 26);
            V.Image(northLine304, s.Ink);
            RefreshTitle304(displayedZoneLabel);
        }

        /// <summary>D11 제첨 테: stroke_line brushes (InkReveal, linear-light alpha compensated) 7 px inside, crossing 2 px at the
        /// corners, α.6, as on the pause and title slips. Without the line sprite: the slip_frame sprite, else four 1.5 px
        /// lines, both on the same InkReveal material.</summary>
        void BuildSlipRim304()
        {
            var s = style;
            titleRim304 = null; titleFrame304 = null; titleRimBrushes304 = false;
            if (V.StrokeSprite(s, StrokeClass304.Line) != null)
            {
                titleRimBrushes304 = true;
                titleRim304 = new[]
                {
                    V.Brush(s, titleSlip304, "FrameTop", StrokeClass304.Line, s.Ink, 0, 0, SlipW304, RimLineH304, RimAlpha304),
                    V.Brush(s, titleSlip304, "FrameBottom", StrokeClass304.Line, s.Ink, 0, 0, SlipW304, RimLineH304, RimAlpha304, 0f, true),
                    V.Brush(s, titleSlip304, "FrameLeft", StrokeClass304.Line, s.Ink, 0, 0, 230, RimLineH304, RimAlpha304, 90f),
                    V.Brush(s, titleSlip304, "FrameRight", StrokeClass304.Line, s.Ink, 0, 0, 230, RimLineH304, RimAlpha304, -90f),
                };
                return;
            }
            if (mapStyle.SlipFrame != null)
            {
                titleFrame304 = V.SpriteImage(titleSlip304, "Frame", mapStyle.SlipFrame, UiStyle304SO.A(s.Ink, .62f), 0, 0, SlipW304, 230);
                InkRevealEffect.On(titleFrame304, s, InkRevealMode.Wipe, 1f);
                return;
            }
            var ink = UiStyle304SO.A(s.Ink, RimAlpha304);
            titleRim304 = new[]
            {
                V.Image(V.Rect("FrameTop", titleSlip304, 0, 0, 1, 1.5f), ink), V.Image(V.Rect("FrameBottom", titleSlip304, 0, 0, 1, 1.5f), ink),
                V.Image(V.Rect("FrameLeft", titleSlip304, 0, 0, 1.5f, 1), ink), V.Image(V.Rect("FrameRight", titleSlip304, 0, 0, 1.5f, 1), ink),
            };
            foreach (var line in titleRim304) InkRevealEffect.On(line, s, InkRevealMode.Wipe, 1f);
        }

        void LayoutSlipRim304(float w, float h)
        {
            if (titleFrame304 != null) { SizeSliced(titleFrame304, w, h); return; }
            if (titleRim304 == null || titleRim304.Length < 4) return;
            float x0 = RimInset304, x1 = w - RimInset304, y0 = RimInset304, y1 = h - RimInset304;
            float across = x1 - x0 + 2f * RimOver304, down = y1 - y0 + 2f * RimOver304;
            if (titleRimBrushes304)
            {
                // V.Brush rects: centre pivot, length along the stroke (the side strokes are turned 90°)
                PlaceStroke304(titleRim304[0], (x0 + x1) * .5f, y0, across);
                PlaceStroke304(titleRim304[1], (x0 + x1) * .5f, y1, across);
                PlaceStroke304(titleRim304[2], x0, (y0 + y1) * .5f, down);
                PlaceStroke304(titleRim304[3], x1, (y0 + y1) * .5f, down);
                return;
            }
            // four plain 1.5 px lines (top-left pivot)
            const float t = 1.5f;
            SetRect304(titleRim304[0], x0 - RimOver304, y0 - t * .5f, across, t);
            SetRect304(titleRim304[1], x0 - RimOver304, y1 - t * .5f, across, t);
            SetRect304(titleRim304[2], x0 - t * .5f, y0 - RimOver304, t, down);
            SetRect304(titleRim304[3], x1 - t * .5f, y0 - RimOver304, t, down);
        }

        static void PlaceStroke304(Image stroke, float cx, float cy, float length)
        {
            var r = stroke.rectTransform;
            r.sizeDelta = new Vector2(length, RimLineH304);
            r.anchoredPosition = new Vector2(cx, -cy);
        }

        static void SetRect304(Image image, float x, float y, float w, float h)
        {
            var r = image.rectTransform; r.sizeDelta = new Vector2(w, h); V.Place(r, x, y);
        }

        void RefreshTitle304(string zoneLabel)
        {
            if (titleSlip304 == null || realmText304 == null) return;
            string realm = string.IsNullOrEmpty(zoneLabel) || zoneLabel == "강토" ? "" : zoneLabel;
            realmText304.text = UiText304.Vertical(realm);
            // the title words stack down the left column with a TitleWordGap304 gap between them
            float y = SlipPad304, titleBottom = SlipPad304;
            for (int i = 0; i < titleWords304.Count; i++)
            {
                var word = titleWords304[i]; if (word == null) continue;
                if (i > 0) y += TitleWordGap304;
                Vector2 wp = word.GetPreferredValues(word.text);
                word.rectTransform.sizeDelta = new Vector2(36, Mathf.Ceil(wp.y));
                V.Place(word.rectTransform, 14, y);
                y += Mathf.Ceil(wp.y); titleBottom = y;
            }
            float titleH = titleBottom - SlipPad304;
            Vector2 rp = realm.Length > 0 ? realmText304.GetPreferredValues(realmText304.text) : Vector2.zero;
            realmText304.rectTransform.sizeDelta = new Vector2(26, Mathf.Ceil(rp.y));
            float body = Mathf.Max(titleH, rp.y);
            float h = Mathf.Ceil(SlipPad304 + body + SlipPad304);
            titleSlip304.sizeDelta = new Vector2(SlipW304, h);
            // the realm column hangs to the bottom of the title column (the pause slip's 권역 -> 지명 order)
            V.Place(realmText304.rectTransform, 54, SlipPad304 + Mathf.Max(0f, titleH - rp.y));
            SizeSliced(titlePaper304, SlipW304, h);
            LayoutSlipRim304(SlipW304, h);
        }

        static void SizeSliced(Image image, float w, float h)
        {
            if (image == null) return;
            var r = image.rectTransform; r.sizeDelta = new Vector2(w, h);
        }

        /// <summary>D308-25: the slip's realm line names where you stand (a realm, or the cave). The sheet prints the realm names
        /// too, so by default (MapStyle304SO.SlipRealm) the line shows only while the sheet does not show that same name - the
        /// word never stands twice on the paper. The slip keeps the size it was measured at with the line (RefreshTitle304), so
        /// it does not twitch when the line comes and goes. Called after the realm names were placed.</summary>
        void RefreshRealmLine304(bool interior)
        {
            if (realmText304 == null) return;
            bool show = true;
            if (mapStyle.SlipRealm == MapSlipRealm304.InteriorOnly) show = interior;
            else if (mapStyle.SlipRealm == MapSlipRealm304.WhenNotOnSheet && !interior && !string.IsNullOrEmpty(displayedZoneLabel))
                for (int i = 0; i < regionLabels.Count && show; i++)
                    show = regionLabels[i] == null || !regionLabels[i].gameObject.activeSelf || regionLabels[i].text != displayedZoneLabel;
            if (realmText304.gameObject.activeSelf != show) realmText304.gameObject.SetActive(show);
        }

        // ------------------------------------------------------------------ controls row (y972, centred under the paper)
        void BuildControls304()
        {
            var controls = V.Rect("PaperMapControls", page304, 0, 972, UiPageFit304.Width, 48);
            currentRow304 = ControlRow304(controls, "Current", "R", "현재 위치", FocusCurrent);
            wholeRow304 = ControlRow304(controls, "Whole", "T", "전체 보기", ShowWholeWorld);
            clearPinRow304 = ControlRow304(controls, "ClearPin", "X", "표식 지우기", ClearPin);
            LayoutControls304();
        }

        FocusRow304 ControlRow304(RectTransform parent, string name, string key, string label, Action action)
        {
            var s = style;
            float kw = V.KeycapWidth(s, null, key, true);
            var row = V.FocusRow(s, parent, name, label, 0, 0, 10, 48, () => action(), new FocusRowSpec304
            {
                Role = UiType304.Label22, LabelX = kw + 10f, Key = key, KeyMode = FocusKeyMode304.Filled, KeySmall = true, KeyX = 0f,
                UnderlayH = 76, UnderlayX = -52f, DabSize = new Vector2(28, 22), DabGap = kw + 22f, SoundTheme = theme,
            });
            return row;
        }

        void LayoutControls304()
        {
            const float gap = 34f;
            var rows = new[] { currentRow304, wholeRow304, clearPinRow304 };
            float total = 0f; int count = 0;
            foreach (var r in rows)
            {
                if (r == null) continue;
                float w = r.Label.rectTransform.anchoredPosition.x + r.Label.rectTransform.sizeDelta.x;
                r.Rect.sizeDelta = new Vector2(w, r.Rect.sizeDelta.y);
                total += w; count++;
            }
            total += gap * Mathf.Max(0, count - 1);
            float x = UiPageFit304.Width * .5f - total * .5f;
            foreach (var r in rows)
            {
                if (r == null) continue;
                V.Place(r.Rect, x, 0f);
                x += r.Rect.sizeDelta.x + gap;
            }
        }

        // ------------------------------------------------------------------ keys (R / T / X; D308-25: L went with the legend;
        // Q / E and LB / RB belong to PlaytestUiRoot's rail). Code-built actions, enabled only while the map page is open.
        void BuildInput304()
        {
            keyCurrent304 = new InputAction("Map304Current", InputActionType.Button, "<Keyboard>/r");
            keyWhole304 = new InputAction("Map304Whole", InputActionType.Button, "<Keyboard>/t");
            keyClearPin304 = new InputAction("Map304ClearPin", InputActionType.Button, "<Keyboard>/x");
        }

        IEnumerable<InputAction> Input304()
        {
            yield return keyCurrent304; yield return keyWhole304; yield return keyClearPin304;
        }

        void EnableInput304(bool on)
        {
            foreach (var a in Input304())
            {
                if (a == null) continue;
                if (on && !a.enabled) a.Enable(); else if (!on && a.enabled) a.Disable();
            }
        }

        void TickInput304()
        {
            if (keyCurrent304 == null || !keyCurrent304.enabled || !targetExpanded || fold < .999f) return;
            if (keyCurrent304.WasPressedThisFrame()) FocusCurrent();
            if (keyWhole304.WasPressedThisFrame()) ShowWholeWorld();
            if (keyClearPin304.WasPressedThisFrame() && session.Progress?.ui?.pin != null && session.Progress.ui.pin.active) ClearPin();
        }

        void DisposeInput304()
        {
            foreach (var a in Input304()) a?.Dispose();
            keyCurrent304 = keyWhole304 = keyClearPin304 = null;
        }
    }
}
