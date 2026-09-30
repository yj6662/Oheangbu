using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Oheangbu.Data.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 장비 상점 / 장비 강화 (DESIGN §7.9, IMPLEMENTATION §7.15): a stand-alone page with no rail - full veil lifting
    /// at x1400, the speaker plate in the dialogue grammar top-left (이름 Serif900 60 + 종류 meta + 말 한 줄), 조선통보 on the right,
    /// the 소지품 chip grid with the price under each chip, and the same detail column shifted left (primary 구매 / 강화 + Enter).
    /// Names kept for the harnesses: EquipmentTradeWindow, ServiceSpeaker, ServiceSpeech, TradeClose ([Esc] 닫기 row),
    /// ServiceGear_&lt;id&gt;, TradeItemState, SelectedGearTitle, SelectedGearEffect, GearBuy, GearUpgrade, GearTransactionError,
    /// DeliverToolbox, ToolboxRequest, TradeBalance.</summary>
    public sealed partial class PlaytestUiRoot
    {
        const float Content304TradeGridX = 96, Content304TradeGridY = 290;

        void BuildTradeFrame(bool forge)
        {
            var s = Content304Style;
            V.EnsureCanvasChannels(canvas);
            FocusMark304.Attach(s, canvasRect);
            // V.Clear leaves the previous window inactive until the end of the frame: a rebuild (purchase, selection) keeps the
            // veil still, a fresh open wipes it in (DESIGN §6 메뉴 등장 순서).
            bool rebuilt = modalLayer.Find("EquipmentTradeWindow") != null;
            var page = V.Page304(modalLayer, "EquipmentTradeWindow");
            var veil = V.Veil(s, page, forge ? "장비 강화" : "장비 상점");
            if (!rebuilt)
            {
                var fx = veil.GetComponent<InkRevealEffect>();
                if (fx != null) UiTween304.RevealIn(fx, s, s.Motion.VeilMs, UiTween304.Token(fx)).Forget();
            }

            // speaker plate (dialogue grammar, DESIGN §5.12): name 60 + kind meta
            var speaker = V.Label(s, page, "ServiceSpeaker", forge ? Content304Artisan : Content304Merchant, UiType304.Speaker60, s.Paper, 96, 64);
            float nx = 96 + speaker.rectTransform.sizeDelta.x + 32;
            V.Label(s, page, "ServiceKind", forge ? "장비 강화" : "장비 상점", UiType304.Meta20, s.Mist, nx, 76);

            // 조선통보 + [Esc] 닫기 (the only way out besides Esc / Back)
            var balance = Content304Currency(page, "TradeCurrency", 1300, 70, Session != null ? Session.Progress.ledger.currency : 0);
            var value = balance.Find("Value"); if (value != null) value.name = "TradeBalance";
            var close = V.FocusRow(s, page, "TradeClose", "닫기", 1150, 124, 170, 56, CloseMenu, new FocusRowSpec304
            {
                Role = UiType304.Label24, LabelX = 40, Key = "Esc", KeyMode = FocusKeyMode304.Hollow, SoundTheme = Theme,
            });
            Content304Note(close.Button);
            contentRoot = V.Rect("TradeContent", page, 0, 0, 1920, 1080);
        }

        void BuildEquipmentService(bool forge)
        {
            var s = Content304Style;
            content304GearCells.Clear(); content304GearChips.Clear();
            if (!Session.EquipmentReady)
            {
                V.Label(s, contentRoot, "GearError", "장비 저장을 확인해야 한다.", UiType304.Body24, s.CinnabarLift, 96, 290);
                return;
            }
            var catalog = Session.Content.EquipmentCatalog;
            var state = Session.Progress.equipment;
            var art = Session.Content.EquipmentUiArt;
            var choices = catalog.Items.Where(x => forge ? state.Has(x.Id) : !x.Starter).ToArray();
            if (!choices.Any(x => x.Id == gearSelection))
                gearSelection = (choices.FirstOrDefault(x => forge ? state.Equipped.Contains(x.Id) : !state.Has(x.Id)) ?? choices.FirstOrDefault())?.Id ?? "";

            // one line of speech under the plate (Prose 28), and the artisan's toolbox errand
            V.Label(s, contentRoot, "ServiceSpeech", forge ? "손에 익은 도구부터 손봐 주지." : "해진 곳부터 살피시오.", UiType304.Prose28, s.Paper, 100, 162);
            if (forge)
            {
                bool done = Session.Progress.campaign.Completed.Contains("village_tool_report");
                bool found = Session.Progress.campaign.Facts.Contains("evidence:village_toolbox");
                if (!done && found)
                {
                    var deliver = V.FocusRow(s, contentRoot, "DeliverToolbox", "공구함 건네기", 60, 206, 420, 64,
                        () => { Session.DeliverVillageToolbox(out gearError); OpenPage(Page); }, new FocusRowSpec304 { Role = UiType304.Label26, LabelX = 40, SoundTheme = Theme });
                    Content304Note(deliver.Button);
                }
                else V.Label(s, contentRoot, "ToolboxRequest", done ? "덕분에 다시 대패를 잡겠군." : "계곡 옆 부서진 수레에 내 공구함을 놓고 왔네.",
                    UiType304.Prose28, s.Mist, 100, 212);
            }

            // chip grid (소지품 grammar): chip 104, name +114, price / level meta +144 (TradeItemState)
            int cols = 4, rows = Mathf.Max(1, Mathf.CeilToInt(choices.Length / (float)cols));
            RectTransform grid = rows > 3
                ? V.Scroll(contentRoot, "EquipmentServiceGrid", Content304TradeGridX, Content304TradeGridY, cols * Content304PitchX + 18, 3 * Content304PitchY, rows * Content304PitchY)
                : V.Rect("EquipmentServiceGrid", contentRoot, Content304TradeGridX, Content304TradeGridY, cols * Content304PitchX, rows * Content304PitchY);
            for (int i = 0; i < choices.Length; i++)
            {
                var item = choices[i]; string id = item.Id;
                float gx = i % cols * Content304PitchX, gy = i / cols * Content304PitchY;
                var cell = Content304Cell(grid, "ServiceGear_" + id, gx, gy, 128, 172, new Rect(0, 0, Content304ChipSize, Content304ChipSize), new Rect(-33, 121, 26, 20),
                    () => { Content304PickGear(Content304GearCells("ServiceGear_" + id), id, null); Content304FocusDetailPrimary(); },
                    c => Content304PickGear(c, id, null), false);
                cell.Id = id;
                var chip = Content304Chip(cell.transform, "Chip", 0, 0, Content304ChipSize, false);
                Content304GearArt(cell.transform, item.Slot, art, 8, 8, 88, Color.white);
                var name = V.Label(s, cell.transform, "Name", forge ? Content304GearName(item, state) : item.Name, UiType304.Label22, s.Paper, 0, 114);
                string note = forge ? (state.Level(id) >= catalog.UpgradeCosts.Length ? "강화 완료" : "+" + state.Level(id) + " 강화")
                                    : state.Has(id) ? "보유" : "조선통보 " + item.Price.ToString("N0");
                V.Label(s, cell.transform, "TradeItemState", note, UiType304.Meta20, s.Mist, 0, 144);
                cell.BindLabel(name, s.Paper, s.Paper);
                content304GearCells.Add(cell); content304GearChips[cell] = chip;
            }

            Content304Rule(contentRoot, "TradeDivider", 720, 250, 740);
            content304Detail = V.Rect("TradeDetail304", contentRoot, 0, 0, 1920, 1080);
            if (content304GearCells.All(c => c.name != content304Kept)) content304Kept = "ServiceGear_" + gearSelection;
            Content304RefreshGear();
            var fallback = Content304FindSelectable(contentRoot, "ServiceGear_" + gearSelection) ?? content304GearCells.FirstOrDefault()?.gameObject;
            Content304Restore(contentRoot, fallback);
        }
    }
}
