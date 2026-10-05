using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    // SPEC-SPELL-DEPLOY-308 section 9 (D308-10b) [TEST]: the mesh path of the HUD reaction, for whitelist items drawn with the
    // default UI material (ink bottle rim / liquid / glass, lock-on enso and its rim). During an impact frame the side of the
    // element facing the impact point takes the paper value, the far side an ink shadow, and an ink copy shifted a few pixels
    // away from the impact sits underneath. During the inversion frame ink and paper values trade places.
    // Colours only: the element's own triangles are kept as they are (a Filled image keeps its fill geometry), no fill value is
    // read or written, no material is created (the #304 value correction path is untouched). No output channel exceeds .9.
    // Vertices are rebuilt only when the impact state changes (at most four times per impact); a normal frame costs nothing.
    [DisallowMultipleComponent]
    public sealed class HudImpactReact308 : BaseMeshEffect
    {
        private static readonly Color32 Paper = new Color32(229, 224, 212, 255);
        private static readonly Color32 Ink = new Color32(15, 14, 13, 255);
        private readonly List<UIVertex> _stream = new List<UIVertex>(96);
        private readonly List<UIVertex> _out = new List<UIVertex>(192);
        private bool _on;
        private Vector2 _impactScreen;
        private float _rim, _fill, _shadow, _swap, _strength;

        public int Rebuilds { get; private set; }
        public bool On => _on;

        /// <summary>impactScreen = the impact point in screen pixels (overlay canvas space).</summary>
        public void SetState(bool on, Vector2 impactScreen, float rim, float fill, float shadowPixels, float swap, float strength)
        {
            if (!on && !_on) return;
            if (on && _on && impactScreen == _impactScreen && rim == _rim && fill == _fill && shadowPixels == _shadow && swap == _swap && strength == _strength) return;
            _on = on; _impactScreen = impactScreen; _rim = rim; _fill = Mathf.Min(fill, .35f); _shadow = shadowPixels; _swap = swap; _strength = Mathf.Clamp01(strength);
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!_on || !IsActive() || vh.currentVertCount == 0) return;
            _stream.Clear(); _out.Clear();
            vh.GetUIVertexStream(_stream);
            var rect = ((RectTransform)transform).rect;
            Vector2 centre = rect.center;
            Vector3 local = transform.InverseTransformPoint(new Vector3(_impactScreen.x, _impactScreen.y, 0f));
            Vector2 toImpact = new Vector2(local.x, local.y) - centre;
            float distance = toImpact.magnitude;
            toImpact = distance > .001f ? toImpact / distance : Vector2.up;
            float half = Mathf.Max(1f, Mathf.Max(rect.width, rect.height) * .5f);

            // the ink shadow first: it is drawn under the element, shifted away from the impact
            if (_shadow > 0f)
            {
                Vector3 shift = new Vector3(-toImpact.x, -toImpact.y, 0f) * _shadow;
                for (int i = 0; i < _stream.Count; i++)
                {
                    var v = _stream[i];
                    v.position += shift;
                    v.color = new Color32(Ink.r, Ink.g, Ink.b, (byte)(v.color.a * .6f * _strength));
                    _out.Add(v);
                }
            }
            for (int i = 0; i < _stream.Count; i++)
            {
                var v = _stream[i];
                Vector2 p = new Vector2(v.position.x, v.position.y) - centre;
                float facing = Mathf.Clamp(Vector2.Dot(p, toImpact) / half, -1f, 1f);
                Color32 c = v.color;
                if (_swap > .5f) c = Swap(c);
                float lit = Mathf.Clamp01(facing) * _rim * _strength + _fill * _strength * .5f;
                float shade = Mathf.Clamp01(-facing) * _rim * _strength * .6f;
                c = Color32.Lerp(c, new Color32(Paper.r, Paper.g, Paper.b, c.a), lit);
                c = Color32.Lerp(c, new Color32(Ink.r, Ink.g, Ink.b, c.a), shade);
                v.color = c;
                _out.Add(v);
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(_out);
            Rebuilds++;
        }

        // ink and paper values trade places, the hue is kept, nothing goes above the paper value
        private static Color32 Swap(Color32 c)
        {
            float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
            float luma = r * .2126f + g * .7152f + b * .0722f;
            float scale = (.95f - luma) / Mathf.Max(luma, .04f);
            return new Color32((byte)(Mathf.Min(r * scale, .9f) * 255f), (byte)(Mathf.Min(g * scale, .9f) * 255f), (byte)(Mathf.Min(b * scale, .9f) * 255f), c.a);
        }
    }
}
