using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>#308 먼 불빛 (SPEC-ATTRACTION-LIGHT-308, TEST): sits on the far glow card of one man-made attraction light and keeps
    /// the card in step with its lamp — the card is drawn only while the source Light is lit, so a lamp that gameplay puts out
    /// never leaves a glow behind. Everything visual is in the card's material (baked from <see cref="AttractionLightProfileSO"/>)
    /// and its shader; this component holds no number and no shared state (no static field, so nothing to reset with domain
    /// reload off), allocates nothing, and costs one bool compare per light per frame. Self-contained per object: no scope
    /// registration, no singleton, no lookup. Never placed on enemies or contamination (AC-L7).</summary>
    [DisallowMultipleComponent]
    public sealed class AttractionLight308 : MonoBehaviour
    {
        [SerializeField] private AttractionLightProfileSO _profile;
        [SerializeField] private AttractionLightProfileSO.Kind _kind;
        [Tooltip("The lamp this card belongs to. Empty = always lit.")]
        [SerializeField] private Light _source;
        [SerializeField] private Renderer _card;

        private bool _shown = true;

        public AttractionLightProfileSO Profile => _profile;
        public AttractionLightProfileSO.Kind Kind => _kind;
        public Light Source => _source;
        public Renderer Card => _card;

        /// <summary>Editor authoring (FarLight308): the only writer of these references.</summary>
        public void Bind(AttractionLightProfileSO profile, AttractionLightProfileSO.Kind kind, Light source, Renderer card)
        {
            _profile = profile; _kind = kind; _source = source; _card = card;
        }

        private bool Lit => _source == null || (_source.isActiveAndEnabled && _source.intensity > 0f);

        private void OnEnable()
        {
            if (_card == null) return;
            _shown = Lit;
            _card.enabled = _shown;
        }

        private void LateUpdate()
        {
            if (_card == null) return;
            bool lit = Lit;
            if (lit == _shown) return;
            _shown = lit;
            _card.enabled = lit;
        }
    }
}
