using System;
using System.Linq;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        void BuildTradeFrame(bool forge)
        {
            V.Image(V.Stretch("TradeDim", modalLayer), new Color(.04f,.04f,.035f,.46f), null, true);
            frame = V.Rect("EquipmentTradeWindow", modalLayer, 0, 0, 1320, 780);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f,.5f);
            frame.anchoredPosition = Vector2.zero;
            V.Image(V.Stretch("TradePaper", frame), Theme.Paper, null, true);
            if (Theme.PaperTexture != null)
                V.Raw(V.Stretch("TradeHanji", frame), Theme.PaperTexture, new Color(1,1,1,.23f));
            heading = subheading = null;
            V.Text(frame, "ServiceSpeaker", forge ? "목공 장인" : "장비 상인", Theme.Font, 32, Theme.Ink, 44, 30, 650, 56);
            var currency = V.Rect("TradeCurrency", frame, 1000, 38, 32, 32);
            if (Theme.Icons != null) CompactUiSymbols.Draw(currency, "통보", Theme.Icons, Theme.Seal);
            else V.Text(currency, "CoinLabel", "통보", Theme.Font, 18, Theme.Ink, -18, 0, 58, 32);
            statusText = V.Text(frame, "TradeBalance", "", Theme.Font, 22, Theme.Ink, 1040, 30, 146, 48, TextAnchor.MiddleRight);
            RefreshStatus();
            var close = V.Button(frame, "TradeClose", "×", Theme, 1220, 30, 52, 48, CloseMenu, false, true);
            close.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            close.GetComponentInChildren<Text>().rectTransform.anchorMin = Vector2.zero;
            close.GetComponentInChildren<Text>().rectTransform.anchorMax = Vector2.one;
            close.GetComponentInChildren<Text>().rectTransform.offsetMin = Vector2.zero;
            close.GetComponentInChildren<Text>().rectTransform.offsetMax = Vector2.zero;
            V.Rule(frame, Theme, 44, 104, 1232);
            contentRoot = V.Rect("TradeContent", frame, 44, 132, 1232, 604);
        }

        void BuildEquipmentService(bool forge)
        {
            if (!Session.EquipmentReady)
            {
                V.Text(contentRoot, "GearError", "장비 저장을 확인해야 한다.", Theme.Font, 24, Theme.Ink, 0, 20, 1000, 80);
                return;
            }
            var catalog = Session.Content.EquipmentCatalog;
            var state = Session.Progress.equipment;
            var choices = catalog.Items.Where(x => forge ? state.Has(x.Id) : !x.Starter).ToArray();
            if (!choices.Any(x => x.Id == gearSelection))
                gearSelection = (choices.FirstOrDefault(x => forge ? state.Equipped.Contains(x.Id) : !state.Has(x.Id)) ?? choices.FirstOrDefault())?.Id ?? "";

            var grid = V.Scroll(contentRoot, "EquipmentServiceGrid", 0, 0, 624, 444, Mathf.Ceil(choices.Length / 3f) * 148);
            for (int i = 0; i < choices.Length; i++)
            {
                var item = choices[i];
                bool selected = gearSelection == item.Id;
                var button = V.Button(grid, "ServiceGear_" + item.Id, "", Theme, i % 3 * 202, i / 3 * 148, 184, 136,
                    () => { gearSelection = item.Id; gearError = ""; OpenPage(Page); }, selected);
                GearSymbol(button.transform, item.Slot, selected ? Theme.Paper : Theme.Ink, 52, 6, 80);
                string note = forge ? "+" + state.Level(item.Id) : state.Has(item.Id) ? "보유" : item.Price.ToString();
                V.Text(button.transform, "TradeItemState", note, Theme.Font, 18, selected ? Theme.Seal : Theme.Muted,
                    12, 92, 160, 44, TextAnchor.MiddleCenter);
            }
            V.Image(V.Rect("TradeDivider", contentRoot, 646, 0, 1, 576), new Color(Theme.Ink.r,Theme.Ink.g,Theme.Ink.b,.16f));
            V.Rule(contentRoot, Theme, 0, 458, 588);
            V.Text(contentRoot, "ServiceSpeech", forge ? "손에 익은 도구부터 손봐 주지." : "해진 곳부터 살피시오.",
                Theme.Font, 22, Theme.Muted, 0, 474, 600, 64);
            if (forge)
            {
                bool done = Session.Progress.campaign.Completed.Contains("village_tool_report");
                bool found = Session.Progress.campaign.Facts.Contains("evidence:village_toolbox");
                if (!done && found)
                    V.Button(contentRoot, "DeliverToolbox", "공구함 건네기", Theme, 0, 546, 280, 48,
                        () => { Session.DeliverVillageToolbox(out gearError); OpenPage(Page); }, false, true);
                else V.Text(contentRoot, "ToolboxRequest", done ? "덕분에 다시 대패를 잡겠군." : "계곡 옆 부서진 수레에\n내 공구함을 놓고 왔네.",
                    Theme.Font, 20, Theme.Muted, 0, 542, 600, 60);
            }

            var d = catalog.Find(gearSelection);
            if (d == null) return;
            GearSymbol(contentRoot, d.Slot, Theme.Ink, 864, 0, 184);
            int level = state.Level(d.Id);
            float bonus = catalog.Bonus(state, d.Id);
            V.Text(contentRoot, "SelectedGearTitle", d.Name + (forge || state.Has(d.Id) ? "  +" + level : ""), Theme.Font, 30, Theme.Ink, 688, 210, 536, 52);
            string effect = EffectName(d.Effect) + " +" + (bonus * 100).ToString("0") + "%";
            if (forge && level < 3) effect += " → +" + ((bonus + catalog.UpgradeStep) * 100).ToString("0") + "%";
            V.Text(contentRoot, "SelectedGearEffect", effect, Theme.Font, 22, Theme.Muted, 688, 274, 536, 88);
            V.Rule(contentRoot, Theme, 688, 374, 536);
            long revision = state.Revision;
            int price = forge ? level < 3 ? catalog.UpgradeCosts[level] : 0 : d.Price;
            bool unavailable = forge ? level >= 3 : state.Has(d.Id);
            string label = unavailable ? forge ? "강화 완료" : "보유 중" : price + "통보 · " + (forge ? "강화" : "구매");
            var trade = V.Button(contentRoot, forge ? "GearUpgrade" : "GearBuy", label, Theme, 688, 402, 536, 60,
                () => GearAction(forge ? EquipmentAction.Upgrade : EquipmentAction.Buy, d.Id, d.Slot, revision, price), !unavailable, true);
            trade.interactable = !unavailable;
            trade.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            if (!string.IsNullOrEmpty(gearError))
                V.Text(contentRoot, "GearTransactionError", gearError, Theme.Font, 20, Theme.Seal, 688, 480, 536, 110);
        }
    }
}
