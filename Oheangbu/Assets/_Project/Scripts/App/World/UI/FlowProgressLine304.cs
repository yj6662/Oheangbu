using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 thin progress line (REFERENCE_BOARD D20; replaces the 640x24 loading InkMeter and the rotating spinner,
    /// DESIGN §8 bans spinners): one stroke_hair run about 5 px thick over `width` - a faint Track (the whole run), a bright
    /// child "Fill" cut exactly at the value (Image Filled Horizontal from the left, so the brush taper is cut, not squashed) and
    /// two short brush ticks at both ends. Used by the region loading screen (WorldLoadingScreen270) and the menu-canvas journey
    /// overlay (PlaytestUiRoot.ShowLoading). The value only grows unless SetValue(v, false). No loop, no tween: the fill moves
    /// with the real progress. Without a style sprite (UiStyle304SO.Fallback) the line is plain 4 px bars cut by width.</summary>
    [DisallowMultipleComponent]
    public sealed class FlowProgressLine304 : MonoBehaviour
    {
        /// <summary>stroke_hair's core is ~6 of its 36 rows, so a 28 px rect shows a ~4.7 px line (D20: 4~6 px).</summary>
        public const float RectHeight = 28f;
        const float TickLength = 16f, TickRect = 8f, PlainThickness = 4f;

        [SerializeField] Image track, fill;
        [SerializeField] RectTransform tickStart, tickEnd;
        [SerializeField, Range(0f, 1f)] float value;
        [SerializeField] float width;

        /// <summary>Current value 0..1 (monotonic under SetValue(v)).</summary>
        public float Value => value;
        /// <summary>The child "Fill": fillAmount == Value (Filled mode) or width == Value x line width (plain fallback).</summary>
        public Image Fill => fill;
        public Image Track => track;
        public RectTransform Rect => (RectTransform)transform;
        public float Width => width;
        /// <summary>Right edge of the line in parent px (the end tick).</summary>
        public float Right => Rect.anchoredPosition.x + width;

        /// <summary>Line from parent px x over `width`, its core centred on centreY. color: Paper on ink / veil.</summary>
        public static FlowProgressLine304 Create(UiStyle304SO s, Transform parent, string name, float x, float centreY, float width, Color color,
            float trackAlpha = .28f, float fillAlpha = .95f, float tickAlpha = .7f)
        {
            s = s != null ? s : UiStyle304SO.Fallback;
            var root = V.Rect(name, parent, x, centreY - RectHeight * .5f, width, RectHeight);
            var line = root.gameObject.AddComponent<FlowProgressLine304>();
            line.width = width;
            bool sprites = V.StrokeSprite(s, StrokeClass304.Hair) != null;
            if (sprites)
            {
                line.track = V.Brush(s, root, "Track", StrokeClass304.Hair, color, 0f, 0f, width, RectHeight, trackAlpha);
                line.fill = V.Brush(s, root, "Fill", StrokeClass304.Hair, color, 0f, 0f, width, RectHeight, fillAlpha);
                line.fill.type = Image.Type.Filled; line.fill.fillMethod = Image.FillMethod.Horizontal;
                line.fill.fillOrigin = (int)Image.OriginHorizontal.Left; line.fill.fillAmount = 0f;
            }
            else
            {
                float y = RectHeight * .5f - PlainThickness * .5f;
                line.track = V.Image(V.Rect("Track", root, 0f, y, width, PlainThickness), UiStyle304SO.A(color, trackAlpha));
                line.fill = V.Image(V.Rect("Fill", root, 0f, y, 0f, PlainThickness), UiStyle304SO.A(color, fillAlpha));
            }
            line.tickStart = Tick(s, root, "TickStart", 0f, color, tickAlpha, sprites);
            line.tickEnd = Tick(s, root, "TickEnd", width, color, tickAlpha, sprites);
            line.Apply();
            return line;
        }

        // short vertical brush tick (stroke_line rotated 90, ~2 px wide) centred on x
        static RectTransform Tick(UiStyle304SO s, RectTransform root, string name, float x, Color color, float alpha, bool sprites)
        {
            float cy = RectHeight * .5f;
            if (sprites)
                return V.Brush(s, root, name, StrokeClass304.Line, color, x - TickLength * .5f, cy - TickRect * .5f, TickLength, TickRect, alpha, 90f).rectTransform;
            var r = V.Rect(name, root, x - 1f, cy - 7f, 2f, 14f); V.Image(r, UiStyle304SO.A(color, alpha)); return r;
        }

        /// <summary>Sets the value (0..1). monotonic = never moves back (loading progress).</summary>
        public void SetValue(float v, bool monotonic = true)
        {
            v = Mathf.Clamp01(v);
            if (monotonic && v < value) v = value;
            value = v; Apply();   // Image.fillAmount / sizeDelta only dirty the mesh when they change
        }

        void Apply()
        {
            if (fill == null) return;
            if (fill.type == Image.Type.Filled && fill.sprite != null) fill.fillAmount = value;
            else { var size = new Vector2(width * value, fill.rectTransform.sizeDelta.y); if (fill.rectTransform.sizeDelta != size) fill.rectTransform.sizeDelta = size; }
        }
    }
}
