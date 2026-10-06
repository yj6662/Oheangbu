using System;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 one row of the key table (HudLiquid308ProfileSO.Marks.KeyGlyphs): an Input System control path and the
    /// name of the atlas cell that stands for it. Data, not code: a wide key is shown as ONE symbol cell, never as a word.</summary>
    [Serializable]
    public sealed class KeyGlyphRow308
    {
        public string Path;
        public string Cell;
        public KeyGlyphRow308() { }
        public KeyGlyphRow308(string path, string cell) { Path = path; Cell = cell; }
    }

    /// <summary>#308 the key glyphs of the three marks: cell of the atlas key block for the key that triggers each action NOW
    /// (HudKeyGlyph308.Cell over the real binding's control path; -1 = no glyph). Handed to HudVessels308 by the presenter
    /// when a binding changes; never a constant of the HUD code.</summary>
    public struct HudKeys308
    {
        public int Dodge, Jump, Vehicle;
        public static HudKeys308 Empty => new HudKeys308 { Dodge = HudKeyGlyph308.None, Jump = HudKeyGlyph308.None, Vehicle = HudKeyGlyph308.None };
    }

    /// <summary>#308 key glyph of an action mark (SPEC-HUD-LIQUID-308 §3.5, D308-11b): control path of the REAL binding -> cell of
    /// the key block in hud308_atlas.png (6 x 8 cells, Tools/Art/hud308_assets.py KEY_CELLS; one cell = one key symbol).
    /// Cells 0-25 = A-Z, 26-35 = 0-9, then the drawn symbols below. Pure functions over strings: no Input System type, no
    /// Unity object, no static state - Offline/SimCheck308 runs them as they are. Nothing here knows which key an ACTION has:
    /// the path comes from the action's binding (HudKeyBinding308) or, for the vehicle call, from the profile.</summary>
    public static class HudKeyGlyph308
    {
        public const int None = -1;
        public const int Letters = 0, Digits = 26;
        public const int Shift = 36, Space = 37, Ctrl = 38, Alt = 39, Tab = 40, Enter = 41, MouseLeft = 42, MouseRight = 43, MouseMiddle = 44,
            Generic = 45;
        public const int Count = 48;
        const string Keyboard = "<Keyboard>/";

        /// <summary>The design values of the table (MarkSpec308.KeyGlyphs starts as this): control path -> cell name. Wide keys are
        /// symbols; gamepad buttons are the letter printed on the common layout. A new array on every call (data, not shared state).</summary>
        public static KeyGlyphRow308[] DefaultTable() => new[]
        {
            new KeyGlyphRow308("<Keyboard>/leftShift", nameof(Shift)), new KeyGlyphRow308("<Keyboard>/rightShift", nameof(Shift)), new KeyGlyphRow308("<Keyboard>/shift", nameof(Shift)),
            new KeyGlyphRow308("<Keyboard>/space", nameof(Space)),
            new KeyGlyphRow308("<Keyboard>/leftCtrl", nameof(Ctrl)), new KeyGlyphRow308("<Keyboard>/rightCtrl", nameof(Ctrl)), new KeyGlyphRow308("<Keyboard>/ctrl", nameof(Ctrl)),
            new KeyGlyphRow308("<Keyboard>/leftAlt", nameof(Alt)), new KeyGlyphRow308("<Keyboard>/rightAlt", nameof(Alt)), new KeyGlyphRow308("<Keyboard>/alt", nameof(Alt)),
            new KeyGlyphRow308("<Keyboard>/tab", nameof(Tab)), new KeyGlyphRow308("<Keyboard>/enter", nameof(Enter)), new KeyGlyphRow308("<Keyboard>/numpadEnter", nameof(Enter)),
            new KeyGlyphRow308("<Mouse>/leftButton", nameof(MouseLeft)), new KeyGlyphRow308("<Mouse>/rightButton", nameof(MouseRight)),
            new KeyGlyphRow308("<Mouse>/middleButton", nameof(MouseMiddle)),
            new KeyGlyphRow308("<Gamepad>/buttonSouth", "A"), new KeyGlyphRow308("<Gamepad>/buttonEast", "B"),
            new KeyGlyphRow308("<Gamepad>/buttonWest", "X"), new KeyGlyphRow308("<Gamepad>/buttonNorth", "Y"),
            new KeyGlyphRow308("<Gamepad>/leftShoulder", "L"), new KeyGlyphRow308("<Gamepad>/rightShoulder", "R"),
            new KeyGlyphRow308("<Gamepad>/leftTrigger", "L"), new KeyGlyphRow308("<Gamepad>/rightTrigger", "R"),
            new KeyGlyphRow308("<Gamepad>/leftStickPress", "L"), new KeyGlyphRow308("<Gamepad>/rightStickPress", "R"),
        };

        /// <summary>Cell index of a cell name of the table: "Shift", "Space", ... or one letter / digit ("A", "7"). -1 = unknown.</summary>
        public static int CellByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return None;
            if (name.Length == 1)
            {
                char c = name[0];
                if (c >= 'a' && c <= 'z') return Letters + (c - 'a');
                if (c >= 'A' && c <= 'Z') return Letters + (c - 'A');
                if (c >= '0' && c <= '9') return Digits + (c - '0');
                return None;
            }
            switch (name)
            {
                case nameof(Shift): return Shift;
                case nameof(Space): return Space;
                case nameof(Ctrl): return Ctrl;
                case nameof(Alt): return Alt;
                case nameof(Tab): return Tab;
                case nameof(Enter): return Enter;
                case nameof(MouseLeft): return MouseLeft;
                case nameof(MouseRight): return MouseRight;
                case nameof(MouseMiddle): return MouseMiddle;
                case nameof(Generic): return Generic;
            }
            return None;
        }

        /// <summary>The cell for a control path: a row of the table wins; a keyboard key named by one letter or one digit is
        /// that letter's cell; any other bound key is the generic keycap; no path = no glyph (-1).</summary>
        public static int Cell(string path, KeyGlyphRow308[] table)
        {
            if (string.IsNullOrEmpty(path)) return None;
            if (table != null)
                for (int i = 0; i < table.Length; i++)
                {
                    var row = table[i];
                    if (row == null || !string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                    int cell = CellByName(row.Cell);
                    return cell >= 0 ? cell : Generic;
                }
            if (path.Length == Keyboard.Length + 1 && path.StartsWith(Keyboard, StringComparison.OrdinalIgnoreCase))
            {
                int cell = CellByName(path.Substring(Keyboard.Length));
                if (cell >= 0) return cell;
            }
            return Generic;
        }

        /// <summary>Which device class a control path belongs to: 0 keyboard / mouse, 1 gamepad, 2 anything else.</summary>
        public static int DeviceClass(string path)
        {
            if (string.IsNullOrEmpty(path)) return 2;
            if (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) || path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase)) return 0;
            if (path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }
    }
}
