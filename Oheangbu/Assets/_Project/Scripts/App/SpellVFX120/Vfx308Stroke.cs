using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // DECISIONS D308-125 적용 6 (숭): the metal stroke from the point where the circle took a shot to the shooter's chest. It does
    // not fly: the head is drawn from the start to the end in DrawSeconds (inside 3 frames at 60 fps, so it lands with the hit
    // tint), then the tail runs after it into the body while the line thins out, and at LifeSeconds the object goes (Play only;
    // in edit mode Draw lays the whole line and nothing moves). Presentation only; no light (ART-INK).
    [RequireComponent(typeof(LineRenderer))]
    public sealed class Vfx308Stroke : MonoBehaviour
    {
        [Min(.01f)] public float DrawSeconds = .05f;
        [Min(.02f)] public float LifeSeconds = .3f;

        LineRenderer _line;
        Vector3 _from, _to;
        float _width, _age;
        bool _drawn;

        public void Draw(Vector3 from, Vector3 to)
        {
            if (_line == null) { _line = GetComponent<LineRenderer>(); _width = _line.widthMultiplier; }
            _from = from; _to = to; _age = 0f; _drawn = true;
            _line.useWorldSpace = true; _line.positionCount = 2;
            Set(Application.isPlaying ? 0f : 1f, 0f, 1f);
        }

        void Update()
        {
            if (!_drawn) return;
            _age += Time.deltaTime;
            if (_age >= LifeSeconds) { Destroy(gameObject); return; }
            float head = Mathf.Clamp01(_age / DrawSeconds);
            float fade = Mathf.Clamp01((_age - DrawSeconds) / Mathf.Max(.01f, LifeSeconds - DrawSeconds));
            Set(head, fade * fade, 1f - fade);
        }

        void Set(float head, float tail, float thick)
        {
            _line.SetPosition(0, Vector3.LerpUnclamped(_from, _to, tail));
            _line.SetPosition(1, Vector3.LerpUnclamped(_from, _to, Mathf.Max(head, tail)));
            _line.widthMultiplier = _width * thick;
        }
    }
}
