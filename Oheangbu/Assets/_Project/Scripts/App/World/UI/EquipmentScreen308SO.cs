using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 theme block of the 소지품 screen (SPEC-UI-THEME-308, DECISIONS D308-15): the theme kit's sprites by their
    /// slot names (theme308_atlas.json) and its tokens (theme308_tokens.json "colours"). Every value is written by the editor
    /// command EquipmentSetup308 from the kit's own files; nothing is typed in code. This block stands in for the reference
    /// to UiTheme308SO until that asset exists (SPEC-UI-EQUIPMENT-308 Temporary Exception 11).</summary>
    [Serializable]
    public sealed class EquipTheme308
    {
        [Tooltip("kit White / Frame.Select / Plaque.Porcelain / Pip.Petal / Lattice.Bit")] public Sprite White, FrameSelect, Plaque, Petal, LatticeBit;
        [Tooltip("figure motifs: Motif.Chrys (the one najeon figure) / Sanggam.Lotus (the plaque's foot band)")] public Sprite Chrys, Lotus;
        [Tooltip("phase 2 (menu-wide): Key.Porcelain / Inlay.Line")] public Sprite Keycap, Line;
        [Tooltip("kit tokens Wood / Lacquer / InlayDark / Nacre (alpha 0 = not bound)")] public Color Wood, Lacquer, InlayDark, Nacre;

        /// <summary>The parts every themed element needs. Motif and phase 2 sprites are optional.</summary>
        public bool Bound => White != null && FrameSelect != null && Plaque != null && Petal != null && LatticeBit != null
            && Wood.a > 0f && Lacquer.a > 0f && InlayDark.a > 0f && Nacre.a > 0f;

        /// <summary>Tint that brings a baked shell sprite to `target` (Image.color multiplies): target / Nacre per channel.</summary>
        public Color Toward(Color target)
            => new Color(Mathf.Min(1f, target.r / Mathf.Max(.01f, Nacre.r)), Mathf.Min(1f, target.g / Mathf.Max(.01f, Nacre.g)), Mathf.Min(1f, target.b / Mathf.Max(.01f, Nacre.b)), target.a);
    }

    /// <summary>#308 소지품 3-column screen data (SPEC-UI-EQUIPMENT-308): its own sprites and DISPLAY-only values. Nothing here is
    /// a gameplay number. Asset: Assets/_Project/Resources/UI308/EquipmentScreen308.asset, created by the editor command
    /// Oheangbu.EditorTools.WorldMacro.EquipmentSetup308 Execute "apply". The screen works without the asset: section marks are
    /// skipped, the item frame falls back to the #304 광곽, the values below fall back to the Default* constants.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI308 Equipment Screen")]
    public sealed class EquipmentScreen308SO : ScriptableObject
    {
        public const string ResourcePath = "UI308/EquipmentScreen308";
        public const float DefaultInkDisplayScale = 100f;
        public const string DefaultRemoveKey = "<Keyboard>/x", DefaultRemovePad = "<Gamepad>/buttonWest", DefaultRemoveLabel = "X";

        [Header("sprites (Tools/Art/equip308_assets.py)")]
        [Tooltip("항목 그림 테 256, Sliced 28 (사주쌍변)")] public Sprite ItemFrame;
        [Tooltip("절 표시 64: 효과 / 견줌 / 다음 강화 / 바꿔 낄 것")] public Sprite MarkEffect, MarkCompare, MarkUpgrade, MarkCandidates;
        [Tooltip("절 표시 64: 몸과 먹 / 속성 위력 / 보강 / 익힌 것")] public Sprite MarkBody, MarkPower, MarkTier, MarkLearned;

        [Header("display only (TEST)")]
        [Tooltip("먹 눈금: the base ink capacity (1.0) is read as this many units. Not a gameplay value.")] public float InkDisplayScale = DefaultInkDisplayScale;
        [Tooltip("show the 먹 회복 row (hidden when the rate is 0)")] public bool ShowInkRegen = true;
        [Tooltip("true: Enter on a 바꿔 낄 것 row equips / unequips at once. false: it moves to a 착용 / 해제 row (GearEquip)")] public bool DirectEquipFromCandidate = true;
        [Tooltip("빼기 key binding paths (empty = no key; the candidate list still unequips)")] public string RemoveKey = DefaultRemoveKey, RemovePad = DefaultRemovePad;
        [Tooltip("keycap text of the 빼기 key")] public string RemoveKeyLabel = DefaultRemoveLabel;
        [Tooltip("no theme only: the kept-slot frame (focus moved into the middle column) uses KeptFrameOnVeil; off = ink like the #304 칸 on paper (invisible on the veil). With the theme the kept 칸 wears the nacre frame instead")] public bool KeptFrameOverride = true;
        public Color KeptFrameOnVeil = new Color32(0xA7, 0xA3, 0x98, 0xFF);

        [Header("#308 theme (SPEC-UI-THEME-308, D308-15)")]
        [Tooltip("off, or the block below not bound = the #304 look (the way back is this one switch)")] public bool ThemeOn = true;
        [Tooltip("the two figure motifs: one najeon chrysanthemum on the lacquer board of the first window, the lotus band on the plaque's foot")] public bool ThemeMotifs = true;
        [Tooltip("phase 2 (every menu page together): this page's filled keycaps become porcelain. Off until the rail and the other pages switch too")] public bool ThemePhase2;
        public EquipTheme308 Theme = new EquipTheme308();

        // null-tolerant readers (the screen draws without the asset)
        public static float InkScale(EquipmentScreen308SO d) => d != null && d.InkDisplayScale > 0f ? d.InkDisplayScale : DefaultInkDisplayScale;
        public static bool RegenShown(EquipmentScreen308SO d) => d == null || d.ShowInkRegen;
        public static bool Direct(EquipmentScreen308SO d) => d == null || d.DirectEquipFromCandidate;
        public static string RemoveBinding(EquipmentScreen308SO d) => d != null ? d.RemoveKey : DefaultRemoveKey;
        public static string RemovePadBinding(EquipmentScreen308SO d) => d != null ? d.RemovePad : DefaultRemovePad;
        public static string RemoveLabel(EquipmentScreen308SO d) => d != null && !string.IsNullOrEmpty(d.RemoveKeyLabel) ? d.RemoveKeyLabel : DefaultRemoveLabel;
        /// <summary>The theme block when the theme is on and bound, else null (the screen then draws the #304 look).</summary>
        public static EquipTheme308 Themed(EquipmentScreen308SO d) => d != null && d.ThemeOn && d.Theme != null && d.Theme.Bound ? d.Theme : null;
        public static bool Motifs(EquipmentScreen308SO d) => d != null && d.ThemeMotifs;
        public static bool Phase2(EquipmentScreen308SO d) => d != null && d.ThemePhase2 && d.Theme != null && d.Theme.Keycap != null;
    }
}
