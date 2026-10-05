using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 소지품 screen checks (SPEC-UI-EQUIPMENT-308). Queue: Oheangbu.EditorTools.WorldMacro.EquipmentChecks308 Execute
    /// "static" | "edit" | "play" | "dab". Never opens a dialog; a refusal is a returned string. Each run writes
    /// Art/UI308/Equipment/checks308_&lt;mode&gt;.txt and ends with "RESULT ok n / FAIL n" (status words stay IMPLEMENTED / VALIDATED
    /// in reports; these lines are raw check rows).
    ///   static (Edit or Play): slot table 6 + 5 + 5, no mutable static on the #308 members, wording rules, the data asset.
    ///   edit   (AC-E9): the screen's wording functions against arithmetic done here, on catalogs / rules built in memory and
    ///          on the project's own catalog and economy assets.
    ///   play   (AC-E10, E11, E13..E16, E24; synchronous): needs an unpaused Play session on a DIAGNOSTIC save slot
    ///          (PlaytestMenuReview "test-slot:_equip308" before entering Play); it equips and unequips for real.
    ///   dab    : FocusMark304.VisibleCount and the selection, read one or more frames after a page change (AC-E12).
    /// Not covered here: E12 over time, E17 / E18 (PlaytestMenuReview layout-check / layout-large), E19 / E22 (captures),
    /// E20 empty states, E21 cost, E23 regression, sound cues (CompactSoundChecks255).</summary>
    public static class EquipmentChecks308
    {
        sealed class Sheet
        {
            public readonly List<string> Lines = new List<string>(); public int Ok, Fail;
            public void Check(bool ok, string text) { Lines.Add((ok ? "ok   " : "FAIL ") + text); if (ok) Ok++; else Fail++; }
            public void Info(string text) { Lines.Add("INFO " + text); }
        }

        static string Output(string mode) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/UI308/Equipment/checks308_" + mode + ".txt"));

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "dab") return Dab();
            if (a != "static" && a != "edit" && a != "play") throw new ArgumentException("Expected static, edit, play or dab");
            var sheet = new Sheet(); string refused = null;
            try
            {
                if (a == "static") Static(sheet);
                else if (a == "edit") refused = Edit(sheet);
                else refused = Play(sheet);
            }
            catch (Exception e) { sheet.Check(false, "exception: " + e.GetType().Name + " " + e.Message); }
            if (refused != null) return refused;
            sheet.Lines.Add("RESULT " + a + " ok " + sheet.Ok + " / FAIL " + sheet.Fail);
            string text = string.Join("\n", sheet.Lines);
            Directory.CreateDirectory(Path.GetDirectoryName(Output(a)));
            File.WriteAllText(Output(a), text + "\n");
            return text;
        }

        static string Dab()
        {
            var es = EventSystem.current; var sel = es != null ? es.currentSelectedGameObject : null;
            var ui = PlaytestUiRoot.Instance;
            return "DAB visible=" + FocusMark304.VisibleCount + " active=" + FocusMark304.ActiveCount + " selected=" + (sel != null ? sel.name : "<none>")
                + " page=" + (ui != null ? ui.Page : "<no root>");
        }

        // ------------------------------------------------------------------ static
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        static void Static(Sheet sheet)
        {
            // AC-E1: the slot table
            var table = typeof(PlaytestUiRoot).GetField("Equip308Slots", BindingFlags.NonPublic | BindingFlags.Static);
            var slots = table != null ? table.GetValue(null) as Array : null;
            sheet.Check(slots != null && slots.Length == 16, "AC-E1 slot table has 16 entries (" + (slots != null ? slots.Length : -1) + ")");
            if (slots != null)
            {
                var kinds = new Dictionary<string, int>();
                foreach (var d in slots) { string k = d.GetType().GetField("Kind").GetValue(d).ToString(); kinds[k] = kinds.TryGetValue(k, out int n) ? n + 1 : 1; }
                kinds.TryGetValue("Gear", out int gear); kinds.TryGetValue("Stone", out int stone); kinds.TryGetValue("Virtue", out int virtue);
                sheet.Check(gear == 6 && stone == 5 && virtue == 5, "AC-E1 장비 6 + 오행 마석 5 + 오덕 5 (" + gear + " / " + stone + " / " + virtue + ")");
                sheet.Check(gear == Enum.GetValues(typeof(EquipmentSlot)).Length, "AC-E1 every EquipmentSlot has exactly one 칸");
            }
            sheet.Check(WorldMacroProgress.CurrentVersion == 8, "AC-E1 save version still 8 (" + WorldMacroProgress.CurrentVersion + ")");

            // AC-E3: no mutable static state on the #308 members / types
            var bad = new List<string>();
            foreach (var f in typeof(PlaytestUiRoot).GetFields(Any))
                if (f.IsStatic && !f.IsLiteral && !f.IsInitOnly && (f.Name.StartsWith("Equip308", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("E308", StringComparison.Ordinal))) bad.Add("PlaytestUiRoot." + f.Name);
            foreach (var type in new[] { typeof(EquipmentScreen308SO), typeof(EquipmentText308), typeof(EquipLegendClick308), typeof(EquipmentKeys308), typeof(PlayerStatus308) })
                foreach (var f in type.GetFields(Any))
                    if (f.IsStatic && !f.IsLiteral && !f.IsInitOnly) bad.Add(type.Name + "." + f.Name);
            sheet.Check(bad.Count == 0, "AC-E3 mutable static fields on #308 members: " + (bad.Count == 0 ? "none" : string.Join(", ", bad)));

            // AC-E4: wording rules on what the text functions produce
            var samples = Samples();
            var instruction = samples.Where(HarnessUiRules304.IsInstructionStyle).ToList();
            sheet.Check(instruction.Count == 0, "AC-E4 instruction style in generated wording: " + (instruction.Count == 0 ? "none" : string.Join(" | ", instruction)));
            var dash = samples.Where(t => t.IndexOf('—') >= 0).ToList();
            sheet.Check(dash.Count == 0, "AC-E4 em-dash in generated wording: " + (dash.Count == 0 ? "none" : string.Join(" | ", dash)));

            // data asset (the screen draws without it; apply has not run = INFO)
            var data = Resources.Load<EquipmentScreen308SO>(EquipmentScreen308SO.ResourcePath);
            if (data == null) sheet.Info("EquipmentScreen308 asset not found (EquipmentSetup308 apply has not run; fallbacks draw)");
            else
            {
                var sprites = new[] { data.ItemFrame, data.MarkEffect, data.MarkCompare, data.MarkUpgrade, data.MarkCandidates, data.MarkBody, data.MarkPower, data.MarkTier, data.MarkLearned };
                sheet.Check(sprites.All(x => x != null), "data asset binds 9 sprites (" + sprites.Count(x => x != null) + ")");
                sheet.Check(data.InkDisplayScale > 0f, "InkDisplayScale > 0 (" + data.InkDisplayScale + ")");
            }
        }

        static List<string> Samples()
        {
            var list = new List<string>();
            var catalog = ScriptableObject.CreateInstance<EquipmentCatalogSO>();
            try
            {
                var state = EquipmentState.Initial(catalog);
                foreach (var d in catalog.Items)
                {
                    if (!state.Has(d.Id)) state.Owned.Add(new OwnedEquipment { Id = d.Id, Level = 1 });
                    list.Add(EquipmentText308.GearName(d, state)); list.Add(EquipmentText308.GearEffect(catalog, state, d)); list.Add(EquipmentText308.GearParts(catalog, state, d));
                    list.Add(EquipmentText308.GearState(state, d, true)); list.Add(EquipmentText308.Compare(catalog, state, d));
                    list.Add(EquipmentText308.CompareAgainst(catalog, state, d, "붓"));
                    if (EquipmentText308.NextUpgrade(catalog, state, d, out string step, out int cost)) { list.Add(step); list.Add(EquipmentText308.UpgradeWhere("목공 장인", cost)); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
            for (int i = 0; i < 5; i++)
            {
                var e = (Element)i;
                list.Add(EquipmentText308.StoneName(e)); list.Add(EquipmentText308.StoneEffect(e, .2f)); list.Add(EquipmentText308.StoneNext(3, .3f)); list.Add(EquipmentText308.Power(1.26f));
            }
            list.Add(EquipmentText308.Coins(1240)); list.Add(EquipmentText308.InkRegen(.05f, 100f)); list.Add(EquipmentText308.Fraction(8, 20)); list.Add(EquipmentText308.StoneLevel(2));
            return list;
        }

        // ------------------------------------------------------------------ edit (AC-E9)
        static string Pct(double v) => Math.Round(v * 100.0, MidpointRounding.AwayFromZero).ToString("0");

        static string Edit(Sheet sheet)
        {
            var catalogs = new List<(string name, EquipmentCatalogSO catalog, bool temporary)> { ("memory default", ScriptableObject.CreateInstance<EquipmentCatalogSO>(), true) };
            foreach (var guid in AssetDatabase.FindAssets("t:EquipmentCatalogSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var asset = AssetDatabase.LoadAssetAtPath<EquipmentCatalogSO>(path);
                if (asset != null && asset.Valid()) catalogs.Add((path, asset, false));
            }
            try
            {
                foreach (var (name, catalog, _) in catalogs)
                {
                    int rows = 0, wrong = 0; string first = null;
                    int max = catalog.UpgradeCosts.Length;
                    foreach (var d in catalog.Items)
                        for (int level = 0; level <= max; level++)
                        {
                            // the item owned at `level`, the slot wearing the starter of that slot (or nothing)
                            var state = EquipmentState.Initial(catalog);
                            var owned = state.Owned.Find(x => x.Id == d.Id);
                            if (owned == null) state.Owned.Add(new OwnedEquipment { Id = d.Id, Level = level }); else owned.Level = level;
                            double total = (double)d.Bonus + level * (double)catalog.UpgradeStep;
                            string effect = EquipmentText308.EffectName(d.Effect) + " +" + Pct(total) + "%";
                            string parts = "기본 +" + Pct(d.Bonus) + "% · 강화 +" + Pct(level * (double)catalog.UpgradeStep) + "%";
                            string wornId = state.Equipped[(int)d.Slot];
                            double before = string.IsNullOrEmpty(wornId) ? 0.0 : (double)catalog.Find(wornId).Bonus + state.Level(wornId) * (double)catalog.UpgradeStep;
                            double delta = total - before;
                            string compare = "교체 " + (delta >= -1e-9 ? "+" : "") + Pct(Math.Abs(delta) < 1e-9 ? 0.0 : delta) + "%p";
                            bool more = level < max;
                            bool gotNext = EquipmentText308.NextUpgrade(catalog, state, d, out string step, out int cost);
                            bool ok = EquipmentText308.GearEffect(catalog, state, d) == effect && EquipmentText308.GearParts(catalog, state, d) == parts
                                && EquipmentText308.Compare(catalog, state, d) == compare && gotNext == more
                                && (!more || (step == "+" + (level + 1) + " 강화" && cost == catalog.UpgradeCosts[level]));
                            rows++; if (!ok) { wrong++; first = first ?? d.Id + " +" + level + ": " + EquipmentText308.GearEffect(catalog, state, d) + " | " + EquipmentText308.GearParts(catalog, state, d) + " | " + EquipmentText308.Compare(catalog, state, d) + " (expected " + effect + " | " + parts + " | " + compare + ")"; }
                        }
                    sheet.Check(wrong == 0, "AC-E9 gear wording = data, " + name + ": " + rows + " rows, " + wrong + " wrong" + (first != null ? " (" + first + ")" : ""));
                }
            }
            finally { foreach (var c in catalogs) if (c.temporary) UnityEngine.Object.DestroyImmediate(c.catalog); }

            var ruleSets = new List<(string name, DemoEconomyRules rules)> { ("approved defaults", DemoEconomyRules.ApprovedDefaults) };
            foreach (var guid in AssetDatabase.FindAssets("t:DemoEconomyProfileSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var asset = AssetDatabase.LoadAssetAtPath<DemoEconomyProfileSO>(path);
                if (asset == null) continue;
                try { ruleSets.Add((path, asset.CreateRules())); } catch (Exception e) { sheet.Info("economy profile " + path + " has no valid rules: " + e.Message); }
            }
            foreach (var (name, rules) in ruleSets)
            {
                int rows = 0, wrong = 0;
                for (int t = 0; t < 5; t++)
                {
                    var track = (DemoUpgradeTrack)t; var e = (Element)t; int max = rules.MaximumLevel(track);
                    for (int level = 0; level <= max; level++)
                    {
                        string effect = "목화토금수".Substring(t, 1) + " 속성 위력 +" + Pct(rules.TotalBonus(track, level)) + "%";
                        bool ok = EquipmentText308.StoneEffect(e, rules.TotalBonus(track, level)) == effect && EquipmentText308.StoneLevel(level) == level + "단";
                        if (level < max)
                            ok &= EquipmentText308.StoneNext(level + 1, rules.TotalBonus(track, level + 1)) == (level + 1) + "단 +" + Pct(rules.TotalBonus(track, level + 1)) + "%"
                               && EquipmentText308.Coins(rules.CostForNextLevel(track, level)) == "조선통보 " + rules.CostForNextLevel(track, level).ToString("N0");
                        rows++; if (!ok) wrong++;
                    }
                }
                sheet.Check(wrong == 0, "AC-E9 마석 wording = rules, " + name + ": " + rows + " rows, " + wrong + " wrong");
            }
            return null;
        }

        // ------------------------------------------------------------------ play
        static string Text(Component root, string path)
        {
            var t = root != null ? root.transform.Find(path) : null; var label = t != null ? t.GetComponent<TMP_Text>() : null;
            return label != null ? label.text : null;
        }
        static Selectable Named(PlaytestUiRoot ui, string name) => ui.GetComponentsInChildren<Selectable>().FirstOrDefault(x => x.name == name);
        static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        static void Select(Component c) { EventSystem.current.SetSelectedGameObject(c.gameObject); Canvas.ForceUpdateCanvases(); }
        static string Snapshot(WorldMacroPlaytestSession s)
            => JsonUtility.ToJson(s.Progress.equipment) + "|" + JsonUtility.ToJson(s.Progress.economy) + "|" + JsonUtility.ToJson(s.Progress.ui) + "|" + s.Progress.ledger.currency;

        static string Play(Sheet sheet)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return "REFUSED play needs an unpaused Play session";
            var ui = PlaytestUiRoot.Instance; var s = ui != null ? ui.Session : null;
            if (ui == null || s == null || s.Progress == null || ui.IsTitle) return "REFUSED no gameplay session (enter the play scene first)";
            if (string.IsNullOrEmpty(PlaytestUiRoot.DiagnosticSuffix)) return "REFUSED no diagnostic save slot (PlaytestMenuReview test-slot:_equip308 before Play): this check equips and unequips";
            if (EventSystem.current == null) return "REFUSED no EventSystem";
            if (ui.Page.Length > 0) ui.CloseMenu();
            if (ui.Page.Length > 0) return "REFUSED the menu did not close (page " + ui.Page + ")";

            string before = Snapshot(s);
            ui.OpenPage("소지품"); Canvas.ForceUpdateCanvases();
            sheet.Check(ui.Page == "소지품", "AC-E11 OpenPage opens 소지품");
            // AC-E10 pause contract while open
            sheet.Check(ui.Pause.Depth == 1, "AC-E10 Pause.Depth == 1 (" + ui.Pause.Depth + ")");
            sheet.Check(Time.timeScale == 0f, "AC-E10 Time.timeScale == 0 (" + Time.timeScale + ")");
            sheet.Check(ui.Gate.InputBlocked, "AC-E10 Gate.InputBlocked");
            var hud = UnityEngine.Object.FindFirstObjectByType<HudController>();
            sheet.Check(hud == null || hud.GetComponentsInChildren<Canvas>(true).All(c => !c.enabled), "AC-E10 HUD canvases off");
            sheet.Check(ui.RuntimeStatesAligned(out string mismatch), "AC-E10 RuntimeStatesAligned (" + mismatch + ")");

            // AC-E1 at runtime + the rows that exist
            bool gear = s.EquipmentEnabled, stones = s.DemoEconomy != null;
            var rows = new List<List<string>>();
            if (gear) { rows.Add(new List<string> { "Equipped_Brush", "Equipped_Accessory" }); rows.Add(new List<string> { "Equipped_Head", "Equipped_Body", "Equipped_Hands", "Equipped_Feet" }); }
            if (stones) rows.Add(Enumerable.Range(0, 5).Select(i => "Stone_" + (DemoUpgradeTrack)i).ToList());
            rows.Add(Enumerable.Range(0, 5).Select(i => "VirtueSlot_" + i).ToList());
            int expectedCells = rows.Sum(r => r.Count);
            int cells = ui.GetComponentsInChildren<ContentCell304>().Count(c => c.name.StartsWith("Equipped_") || c.name.StartsWith("Stone_") || c.name.StartsWith("VirtueSlot_"));
            sheet.Check(cells == expectedCells && cells <= 16, "AC-E1 칸 " + cells + " (expected " + expectedCells + ": gear " + gear + ", stones " + stones + ")");
            sheet.Check(!gear || ui.GetComponentsInChildren<EquipmentDropSlot>().Length == 6, "six equipment drop targets");
            sheet.Check(Selected != null && (!gear || Selected.name.StartsWith("Equipped_") || Selected.name.EndsWith("Tab")), "AC-E12 first focus is a slot (" + (Selected != null ? Selected.name : "none") + ")");

            // AC-E13 navigation table: 4 directions per cell against §6
            int moves = 0, wrongMoves = 0; string firstWrong = null;
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Count; c++)
                {
                    var cell = Named(ui, rows[r][c]); if (cell == null) { wrongMoves += 4; moves += 4; firstWrong = firstWrong ?? rows[r][c] + " missing"; continue; }
                    Select(cell);
                    string left = c > 0 ? rows[r][c - 1] : null;
                    string right = c < rows[r].Count - 1 ? rows[r][c + 1] : MiddleFirst(ui);
                    string up = r > 0 ? rows[r - 1][Mathf.Min(c, rows[r - 1].Count - 1)] : (Named(ui, "EquipmentTab") != null ? "EquipmentTab" : null);
                    string down = r < rows.Count - 1 ? rows[r + 1][Mathf.Min(c, rows[r + 1].Count - 1)] : null;
                    var got = new[] { cell.FindSelectableOnLeft(), cell.FindSelectableOnRight(), cell.FindSelectableOnUp(), cell.FindSelectableOnDown() };
                    var want = new[] { left, right, up, down }; var dir = new[] { "left", "right", "up", "down" };
                    for (int k = 0; k < 4; k++)
                    {
                        moves++; string g = got[k] != null ? got[k].name : null;
                        if (g != want[k]) { wrongMoves++; firstWrong = firstWrong ?? rows[r][c] + " " + dir[k] + " = " + (g ?? "none") + ", expected " + (want[k] ?? "none"); }
                    }
                }
            sheet.Check(wrongMoves == 0, "AC-E13 navigation " + moves + " moves, " + wrongMoves + " wrong" + (firstWrong != null ? " (" + firstWrong + ")" : ""));

            // AC-E15 status column = game values
            var status = ui.GetComponentsInChildren<RectTransform>().FirstOrDefault(x => x.name == "EquipStatus308");
            sheet.Check(status != null, "EquipStatus308 exists");
            if (status != null)
            {
                var vitals = s.Walker.Body.GetComponent<PlayerVitals>();
                string hp = Mathf.RoundToInt(vitals.Hp01 * vitals.MaxHp) + " / " + Mathf.RoundToInt(vitals.MaxHp);
                sheet.Check(Text(status, "Status_hp/Value") == hp, "AC-E15 체력 " + Text(status, "Status_hp/Value") + " == " + hp);
                var ink = typeof(WorldMacroPlaytestSession).GetField("ink", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(s) as InkPool;
                var data = Resources.Load<EquipmentScreen308SO>(EquipmentScreen308SO.ResourcePath); float scale = EquipmentScreen308SO.InkScale(data);
                if (ink != null)
                {
                    string want = Mathf.RoundToInt(ink.Value * ink.CapacityMultiplier * scale) + " / " + Mathf.RoundToInt(ink.CapacityMultiplier * scale);
                    sheet.Check(Text(status, "Status_ink/Value") == want, "AC-E15 먹 " + Text(status, "Status_ink/Value") + " == " + want);
                }
                var damage = s.Walker.Wiring.PlayerDamageScale;
                if (damage != null)
                    for (int i = 0; i < 5; i++)
                    {
                        string want = EquipmentText308.Power(damage((Element)i)), got = Text(status, "Status_power_" + (DemoUpgradeTrack)i + "/Value");
                        sheet.Check(got == want, "AC-E15 위력 " + (DemoUpgradeTrack)i + " " + got + " == " + want);
                    }
                else sheet.Info("PlayerDamageScale not bound (no economy): the 속성 위력 group must be absent = " + (status.Find("Status_power_Wood") == null));
                var currency = ui.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t => t.name == "Value" && t.transform.parent != null && t.transform.parent.name == "Currency");
                sheet.Check(currency != null && currency.text == s.Progress.ledger.currency.ToString("N0"), "AC-E15 조선통보 " + (currency != null ? currency.text : "none"));
                var banned = new[] { "레벨", "지구력", "무게", "방어" };
                var hits = status.GetComponentsInChildren<TMP_Text>().Where(t => banned.Any(b => t.text.Contains(b))).Select(t => t.text).ToList();
                sheet.Check(hits.Count == 0, "AC-E15 no 레벨 / 지구력 / 무게 / 방어 row" + (hits.Count > 0 ? ": " + string.Join(", ", hits) : ""));
            }

            // AC-E16 links (no purchase path here)
            if (stones)
            {
                Select(Named(ui, "Stone_Wood"));
                var link = ui.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "LinkService");
                sheet.Check(link != null && link.interactable == s.AtDemoShop, "AC-E16 정비 link interactable == AtDemoShop (" + s.AtDemoShop + ")");
            }
            Select(Named(ui, "VirtueSlot_0"));
            var chapae = ui.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "LinkChapae");
            sheet.Check(chapae != null && chapae.interactable, "AC-E16 차패 link live");
            if (chapae != null) { chapae.onClick.Invoke(); Canvas.ForceUpdateCanvases(); sheet.Check(ui.Page == "차패", "AC-E16 오덕 → 차패 (" + ui.Page + ")"); ui.OpenPage("소지품"); Canvas.ForceUpdateCanvases(); }

            // AC-E11 Esc: list row → slot (page and depth stay); slot → the menu closes
            string candidateSlot = null;
            if (gear && s.EquipmentReady)
            {
                foreach (var name in rows.Take(2).SelectMany(x => x))
                {
                    var cell = Named(ui, name); if (cell == null) continue;
                    ((Button)cell).onClick.Invoke(); Canvas.ForceUpdateCanvases();
                    if (Selected != null && Selected.name.StartsWith("Gear_")) { candidateSlot = name; break; }
                }
                sheet.Check(candidateSlot != null, "Enter on a 장비 칸 moves the focus into 바꿔 낄 것 (" + (candidateSlot ?? "no slot with a list") + ")");
                if (candidateSlot != null)
                {
                    ui.Back(); Canvas.ForceUpdateCanvases();
                    sheet.Check(ui.Page == "소지품" && ui.Pause.Depth == 1 && Selected != null && Selected.name == candidateSlot, "AC-E11 Esc on a list row = back to " + candidateSlot + " (page " + ui.Page + ", selected " + (Selected != null ? Selected.name : "none") + ")");
                }
            }
            // AC-E24 / E16: opening, selecting everything and following links wrote nothing
            sheet.Check(Snapshot(s) == before, "AC-E24 equipment / economy / ui / 조선통보 unchanged by opening and selecting");

            // AC-E14 equip / unequip through the list, the 빼기 path and a rejected request
            if (candidateSlot != null)
            {
                var slot = (EquipmentSlot)Enum.Parse(typeof(EquipmentSlot), candidateSlot.Substring("Equipped_".Length));
                string original = s.Progress.equipment.Equipped[(int)slot]; long revision = s.Progress.equipment.Revision;
                ((Button)Named(ui, candidateSlot)).onClick.Invoke(); Canvas.ForceUpdateCanvases();
                var row = Selected != null ? Selected.GetComponent<Button>() : null; string id = row != null ? row.name.Substring("Gear_".Length) : null;
                if (row != null)
                {
                    bool wasWorn = original == id;
                    row.onClick.Invoke(); Canvas.ForceUpdateCanvases();
                    string now = s.Progress.equipment.Equipped[(int)slot];
                    sheet.Check((wasWorn ? string.IsNullOrEmpty(now) : now == id) && s.Progress.equipment.Revision == revision + 1, "AC-E14 list row " + (wasWorn ? "unequips" : "equips") + " " + id + " (revision " + revision + " → " + s.Progress.equipment.Revision + ")");
                    sheet.Check(ui.Page == "소지품" && Selected != null && Selected.name == candidateSlot, "AC-E12 focus returns to " + candidateSlot + " after the rebuild (" + (Selected != null ? Selected.name : "none") + ")");
                    // a stale revision is rejected, nothing is saved, the reason is shown
                    string gearBefore = JsonUtility.ToJson(s.Progress.equipment);
                    typeof(PlaytestUiRoot).GetMethod("GearAction", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, new object[] { EquipmentAction.Equip, id, slot, revision - 5, -1 });
                    Canvas.ForceUpdateCanvases();
                    sheet.Check(gearBefore == JsonUtility.ToJson(s.Progress.equipment) && ui.GetComponentsInChildren<TMP_Text>().Any(t => t.name == "GearTransactionError" && t.text.Length > 0), "AC-E14 stale request rejected: save unchanged, GearTransactionError shown");
                    // 빼기 (the [X] key's action) on a worn slot, then put the original back
                    if (!string.IsNullOrEmpty(s.Progress.equipment.Equipped[(int)slot]))
                    {
                        Select(Named(ui, candidateSlot));
                        typeof(PlaytestUiRoot).GetMethod("Equip308Remove", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, new object[] { true });
                        Canvas.ForceUpdateCanvases();
                        sheet.Check(string.IsNullOrEmpty(s.Progress.equipment.Equipped[(int)slot]), "AC-E14 빼기 takes off the worn item");
                    }
                    if (s.Progress.equipment.Equipped[(int)slot] != original && !string.IsNullOrEmpty(original))
                    {
                        typeof(PlaytestUiRoot).GetMethod("GearAction", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, new object[] { EquipmentAction.Equip, original, slot, s.Progress.equipment.Revision, -1 });
                        Canvas.ForceUpdateCanvases();
                    }
                    else if (string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(s.Progress.equipment.Equipped[(int)slot]))
                    {
                        typeof(PlaytestUiRoot).GetMethod("GearAction", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, new object[] { EquipmentAction.Unequip, s.Progress.equipment.Equipped[(int)slot], slot, s.Progress.equipment.Revision, -1 });
                        Canvas.ForceUpdateCanvases();
                    }
                    sheet.Check((s.Progress.equipment.Equipped[(int)slot] ?? "") == (original ?? ""), "AC-E14 the slot wears what it wore before the check (" + original + ")");
                }
            }

            // AC-E11 / E10: Esc on a slot closes; depth back to 0
            var any = Named(ui, rows[0][0]); if (any != null) Select(any);
            ui.Back(); Canvas.ForceUpdateCanvases();
            sheet.Check(ui.Page.Length == 0, "AC-E11 Esc on a slot closes the menu (page '" + ui.Page + "')");
            sheet.Check(ui.Pause.Depth == 0, "AC-E10 Pause.Depth == 0 after closing (" + ui.Pause.Depth + ")");
            sheet.Info("not covered here: AC-E10 two neutral frames before input returns, AC-E12 dab count (run `dab` a frame later), I key / rail entry (same OpenPage)");
            return null;
        }

        /// <summary>What a row's last cell reaches to the right: the first 바꿔 낄 것 row, else the live link row, else nothing.</summary>
        static string MiddleFirst(PlaytestUiRoot ui)
        {
            var detail = ui.GetComponentsInChildren<RectTransform>().FirstOrDefault(x => x.name == "GearDetail304");
            if (detail == null) return null;
            var list = detail.GetComponentsInChildren<Selectable>().Where(x => x.interactable && x.navigation.mode != Navigation.Mode.None).ToList();
            var row = list.FirstOrDefault(x => x.name.StartsWith("Gear_")) ?? list.FirstOrDefault(x => x.name == "LinkService" || x.name == "LinkChapae");
            return row != null ? row.name : null;
        }
    }
}
