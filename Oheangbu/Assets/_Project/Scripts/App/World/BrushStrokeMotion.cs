using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>Read-only presentation of stroke size, travel and direction. Never changes drawing input.</summary>
    public struct BrushStrokeMotion
    {
        public float ArmShare { get; private set; }
        public Vector2 Sweep { get; private set; }
        public bool StartedStroke { get; private set; }
        public bool Contact { get; private set; }
        Vector2 origin, previous, low, high;
        bool initialized;

        public void BeginStroke() { Contact = false; }

        public void Step(Vector2 pointer, bool drawing, bool contact, float dt, float smallSpan, float largeSpan)
        {
            StartedStroke = contact && !Contact;
            if (!drawing) { Reset(); return; }
            if (!initialized) ArmShare = .15f;
            if (!initialized || StartedStroke) { origin = low = high = pointer; }
            Vector2 velocity = initialized && dt > .00001f ? (pointer - previous) / dt : Vector2.zero;
            float response = 1f - Mathf.Exp(-12f * Mathf.Max(0, dt));
            Sweep = Vector2.Lerp(Sweep, Vector2.ClampMagnitude(velocity, 1f), response);
            if (contact) { low = Vector2.Min(low, pointer); high = Vector2.Max(high, pointer); }
            float span = (high - low).magnitude;
            float large = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(smallSpan, Mathf.Max(smallSpan + .01f, largeSpan), span));
            float downward = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(smallSpan, largeSpan, origin.y - pointer.y));
            float travel = Mathf.Clamp01(Sweep.magnitude * 2);
            float target = contact ? Mathf.Lerp(.12f, .95f, Mathf.Max(large, downward)) : Mathf.Lerp(.55f, 1f, travel);
            ArmShare = Mathf.Lerp(ArmShare, target, response);
            Contact = contact; previous = pointer; initialized = true;
        }

        public void Reset()
        {
            initialized = Contact = StartedStroke = false; ArmShare = .15f; Sweep = Vector2.zero;
        }
    }
}
