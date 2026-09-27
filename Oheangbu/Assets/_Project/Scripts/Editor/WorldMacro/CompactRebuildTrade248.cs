using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        static readonly List<string> tradeChecks248 = new List<string>();
        public static string TradeCheck248(string command)
        {
            var s = VillageSession();
            if (!EditorApplication.isPlaying || !s.TestSaveSuffix.StartsWith("_compact_slice_"))
                throw new Exception("Private diagnostic slot required");
            var ui = PlaytestUiRoot.Instance;
            void Check(bool value,string label)
            {
                Directory.CreateDirectory(Output + "/Trade248");
                tradeChecks248.Add((value ? "PASS " : "FAIL ") + label);
                File.WriteAllText(Output + "/Trade248/runtime.txt", string.Join("\n",tradeChecks248));
                if (!value) throw new Exception(label);
            }
            Button Button(string name) => ui.GetComponentsInChildren<Button>().Single(x=>x.name==name);
            void Shape()
            {
                Canvas.ForceUpdateCanvases();
                Check(ui.GetComponentsInChildren<RectTransform>().Any(x=>x.name=="EquipmentTradeWindow"),"dedicated trade window exists");
                Check(!ui.GetComponentsInChildren<Transform>().Any(x=>x.name=="Folio"||x.name.StartsWith("Tab_")),"trade has no pause folio or menu navigation");
                var close=Button("TradeClose");
                Check(close.GetComponentInChildren<Text>().rectTransform.rect.width>40,"close symbol has a visible hit target");
                Check(ui.GetComponentsInChildren<Text>().Where(x=>x.name=="TradeItemState").All(x=>x.preferredHeight<=x.rectTransform.rect.height+.1f),"item prices and levels fit after text scaling");
                var speech=ui.GetComponentsInChildren<Text>().Single(x=>x.name=="ServiceSpeech");
                Check(speech.preferredHeight<=speech.rectTransform.rect.height+.1f,"complete merchant sentence fits its bounds");
            }
            if(command=="prepare")
            {
                tradeChecks248.Clear();
                Check(ApproachVillage("village_shop")&&s.Interact("village_shop"),"merchant interaction opens trade");
                Shape();
                Check(Button("GearBuy").interactable,"new trade selects a purchasable item");
                string before=JsonUtility.ToJson(s.Progress.equipment);int coins=s.Progress.ledger.currency;
                Button("GearBuy").onClick.Invoke();
                Check(JsonUtility.ToJson(s.Progress.equipment)==before&&s.Progress.ledger.currency==coins,"insufficient funds through buy button preserves equipment and coins");
                Check(ui.GetComponentsInChildren<Text>().Any(x=>x.name=="GearTransactionError"&&x.text.Contains("부족")),"transaction rejection is visible within trade");
                s.Progress.ledger.currency=1000;Check(s.SaveNow(out _),"private test funds saved");
                ui.CloseMenu();ui.Gate.ReleaseImmediately();
                Check(ApproachVillage("village_shop")&&s.Interact("village_shop"),"merchant can reopen trade");
            }
            else if(command=="purchase")
            {
                var buy=Button("GearBuy");buy.onClick.Invoke();
                Check(s.Progress.equipment.Has("pine_brush")&&s.Progress.ledger.currency==880,"buy button commits one brush for 120");
                Check(!Button("GearBuy").interactable,"owned item disables purchase");
                int coins=s.Progress.ledger.currency;buy.onClick.Invoke();
                Check(s.Progress.ledger.currency==coins&&s.Progress.equipment.Owned.Count==3,"old callback cannot buy twice");
                Button("TradeClose").onClick.Invoke();
                Check(ui.Page==""&&Time.timeScale>0,"close button returns to world and resumes time");
                Check(ApproachVillage("village_artisan")&&s.Interact("village_artisan"),"artisan interaction opens dedicated forge");
                Shape();Button("ServiceGear_pine_brush").onClick.Invoke();Button("GearUpgrade").onClick.Invoke();
                Check(s.Progress.equipment.Level("pine_brush")==1&&s.Progress.ledger.currency==840,"forge button commits first upgrade for 40");
                Check(ui.GetComponentsInChildren<Text>().Any(x=>x.name=="SelectedGearEffect"&&x.text.Contains("6%")&&x.text.Contains("8%")),"forge preview refreshes after saved upgrade");
            }
            else if(command=="finish")
            {
                Button("GearUpgrade").onClick.Invoke();Button("GearUpgrade").onClick.Invoke();
                Check(s.Progress.equipment.Level("pine_brush")==3&&s.Progress.ledger.currency==620,"forge charges 80 then 140 to reach +3");
                Check(!Button("GearUpgrade").interactable,"maximum upgrade disables forge action");
                ui.Back();Check(ui.Page==""&&Time.timeScale>0,"Back returns from forge to world");
                ui.OpenPage("소지품");Check(ui.GetComponentsInChildren<Transform>().Any(x=>x.name=="Folio"),"inventory still uses its menu folio");
                ui.CloseMenu();ui.Gate.ReleaseImmediately();
                s.Teleport(s.Content.StartFeet,s.Content.StartYaw);ui.OpenPage("장비 상점");
                Check(ui.Page=="","remote trade route remains rejected");
            }
            else throw new ArgumentException(command);
            return string.Join("\n",tradeChecks248);
        }
    }
}
