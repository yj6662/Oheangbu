using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 menu area: mouse glyphs for the controls page (조작 안내) and keycap rows (white RGB + alpha masks derived
    /// from the purchased Amanz mouse-outlined set by Art/UI304/gen/menu_assets.py; tint with Image.color). The asset lives in
    /// Assets/_Project/Resources/UI304/menu/MenuInputIcons304.asset and is created / refreshed by the editor command
    /// Oheangbu.EditorTools.WorldMacro.Menu304Setup.Execute("menu304-setup"). When it is missing the menu falls back to text
    /// keycaps (좌클릭, 마우스, 휠), so nothing breaks before the command runs. Not a singleton: PlaytestUiRoot loads it once
    /// into an instance field.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI304 Menu Input Icons", fileName = "MenuInputIcons304")]
    public sealed class MenuInputIcons304 : ScriptableObject
    {
        public const string ResourcePath = "UI304/menu/MenuInputIcons304";

        [Tooltip("좌클릭 (mouse_left.png)")] public Sprite MouseLeft;
        [Tooltip("우클릭 (mouse_right.png)")] public Sprite MouseRight;
        [Tooltip("휠 (mouse_middle.png)")] public Sprite MouseMiddle;
        [Tooltip("마우스 이동 / 시점 (mouse_move.png)")] public Sprite MouseMove;

        /// <summary>Glyph for a mouse token of the controls table ("좌클릭", "우클릭", "휠", "마우스"), null when unknown / missing.</summary>
        public Sprite For(string token)
        {
            switch (token)
            {
                case "좌클릭": return MouseLeft;
                case "우클릭": return MouseRight;
                case "휠": return MouseMiddle;
                case "마우스": return MouseMove;
                default: return null;
            }
        }
    }
}
