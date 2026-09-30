using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>The 4-layer ink meter of DESIGN §5.9 / IMPLEMENTATION §5.1, all InkMeterGraphic on one rect (the MAX length):
    ///   GhostEdge  paper α.3, rim material, edge only, fill 1   (한지 가장자리 of the 담묵 ghost)
    ///   Ghost      ink α.27, body material, fill 1              (the full stroke = maximum)
    ///   Lag        cinnabar α.34, body material, fill = lag    (HP only: lost share holds 450 ms, drains 350 ms)
    ///   ValueRim   paper α.72, rim material, fill = value       (한지 테)
    ///   Value      value colour, body material, fill = value
    /// SetValue changes the value at once; only the lag follows late. SetMaxLength grows the stroke (base + 5 px per max %).</summary>
    public sealed class InkMeter304 : MonoBehaviour
    {
        public InkMeterGraphic GhostEdge, Ghost, Lag, ValueRim, Value;
        [SerializeField] UiStyle304SO style;
        [SerializeField, Range(0, 1)] float value = 1f;
        readonly UiTweenSlot304 lagSlot = new UiTweenSlot304();

        public float Value01 => value;
        public float Lag01 => Lag != null ? Lag.Fill : value;
        public RectTransform Rect => (RectTransform)transform;

        /// <summary>Builds the meter at (x, y) (top-left, parent coordinates) with the full length w and height h.</summary>
        public static InkMeter304 Create(UiStyle304SO s, Transform parent, string name, float x, float y, float w, float h, Color valueColor, bool withLag)
        {
            s = s != null ? s : UiStyle304SO.Fallback;
            var root = PlaytestUiView.Rect(name, parent, x, y, w, h);
            var meter = root.gameObject.AddComponent<InkMeter304>();
            meter.style = s;
            var m = s.Meter;
            meter.GhostEdge = Layer(root, "GhostEdge", s.Materials.InkMeterRim, UiStyle304SO.A(s.Paper, m.GhostEdge), 1f, true);
            meter.Ghost = Layer(root, "Ghost", s.Materials.InkMeterBody, UiStyle304SO.A(s.Ink, m.GhostInk), 1f, false);
            if (withLag) meter.Lag = Layer(root, "Lag", s.Materials.InkMeterBody, UiStyle304SO.A(s.Cinnabar, m.LagAlpha), 1f, false);
            meter.ValueRim = Layer(root, "ValueRim", s.Materials.InkMeterRim, UiStyle304SO.A(s.Paper, m.RimAlpha), 1f, false);
            meter.Value = Layer(root, "Value", s.Materials.InkMeterBody, valueColor, 1f, false);
            return meter;
        }

        static InkMeterGraphic Layer(RectTransform root, string name, Material material, Color color, float fill, bool edge)
        {
            var rect = PlaytestUiView.Stretch(name, root);
            var g = rect.gameObject.AddComponent<InkMeterGraphic>();
            g.raycastTarget = false; g.color = color; g.Fill = fill; g.EdgeOnly = edge;
            if (material != null) g.material = material;
            return g;
        }

        /// <summary>Value 0..1. Drops leave the lag behind (hold MeterLagHoldMs, drain MeterLagDrainMs); rises move the lag with the value.</summary>
        public void SetValue(float v01, bool animateLag = true)
        {
            v01 = Mathf.Clamp01(v01);
            value = v01;
            if (Value != null) Value.Fill = v01;
            if (ValueRim != null) ValueRim.Fill = v01;
            if (Lag == null) return;
            // Lag.Fill >= value at rest. A drop keeps the lag where it is (hits accumulate), holds, then drains to the value.
            if (!animateLag || !isActiveAndEnabled || v01 >= Lag.Fill) { lagSlot.Cancel(); Lag.Fill = v01; return; }
            DrainLag(lagSlot.Restart(this)).Forget();
        }

        async UniTaskVoid DrainLag(System.Threading.CancellationToken ct)
        {
            var s = style != null ? style : UiStyle304SO.Fallback;
            if (await UiTween304.Delay(s.Motion.MeterLagHoldMs / 1000f, ct)) return;
            if (Lag == null) return;
            await UiTween304.Fill(Lag, value, s.Motion.MeterLagDrainMs / 1000f, s.Motion.Lift, ct);
        }

        /// <summary>Stroke length in px (e.g. base 500 + PxPerMaxPercent x max-HP increase %).</summary>
        public void SetMaxLength(float width) { var r = Rect; r.sizeDelta = new Vector2(width, r.sizeDelta.y); }

        public void SetValueColor(Color c) { if (Value != null) Value.color = c; }

        void OnDisable() { lagSlot.Cancel(); if (Lag != null) Lag.Fill = value; }
    }
}
