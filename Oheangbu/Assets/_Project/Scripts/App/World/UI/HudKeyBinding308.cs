using UnityEngine.InputSystem;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 which key triggers an action RIGHT NOW (SPEC-HUD-LIQUID-308 §3.5, D308-11b): the control path of the binding
    /// the player would use, read from the Input System action itself. effectivePath is the override when one is set, so a
    /// rebinding (the project has none today) is followed without any change here. Among several bindings the one for the
    /// device used last wins (a gamepad when it reported after keyboard and mouse), else the keyboard / mouse one, else the
    /// first. The Gameplay map has no gamepad bindings today, so the keyboard key is what shows.
    /// Read-only towards the input: nothing is enabled, disabled, rebound or consumed. No static state; the per-frame path
    /// (the presenter compares the returned string with the last one) allocates nothing.</summary>
    public static class HudKeyBinding308
    {
        /// <summary>Effective control path of the binding to show for `action` (null = the action has no usable binding).</summary>
        public static string Path(InputAction action, bool gamepad)
        {
            if (action == null) return null;
            var bindings = action.bindings;
            string first = null, desk = null, pad = null;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite) continue;                 // the composite itself has no key; its parts do
                string path = b.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;
                if (first == null) first = path;
                int device = HudKeyGlyph308.DeviceClass(path);
                if (device == 0 && desk == null) desk = path;
                else if (device == 1 && pad == null) pad = path;
            }
            if (gamepad && pad != null) return pad;
            return desk ?? first;
        }

        /// <summary>The Gameplay action named `name` of the asset the motor reads, or null (the vehicle call has none today).</summary>
        public static InputAction Find(InputActionAsset asset, string map, string name)
        {
            if (asset == null || string.IsNullOrEmpty(name)) return null;
            var m = asset.FindActionMap(map);
            return m != null ? m.FindAction(name) : null;
        }

        /// <summary>A gamepad was the device that reported last (after the keyboard and the mouse).</summary>
        public static bool GamepadUsedLast()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            double t = pad.lastUpdateTime;
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            return (keyboard == null || t > keyboard.lastUpdateTime) && (mouse == null || t > mouse.lastUpdateTime);
        }
    }
}
