using System.Collections.Generic;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Data.Spell;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // SPEC-SPELL-DEPLOY-308 section 9 (D308-10b) [TEST]: binds the HUD whitelist items to the impact frame state.
    //   UI/InkMeter and UI/InkReveal items (HP stroke, ink stroke, prompt underlay, groggy disc) react in their shaders through
    //   the globals (ImpactHud308.hlsl) - nothing to bind.
    //   Default-material images (ink bottle rim / liquid / glass, enso, enso rim) get HudImpactReact308.
    //   The prompt text (TMP) swaps its Normal / Inverse material pair for the inversion frame only.
    // No HUD item is added (the whitelist stays at four) and no fill value is read or written here.
    // The HUD is built in HudController.Awake, which may run after the layer boots: binding is retried until it succeeds.
    public sealed class HudImpactBinder308 : MonoBehaviour
    {
        private HudController _hud;
        private ImpactFrameDirector308 _director;
        private SpellDeploy308ProfileSO _profile;
        private readonly List<HudImpactReact308> _effects = new List<HudImpactReact308>(8);
        private TMP_Text _label;
        private Material _labelNormal, _labelInverse;
        private Color _labelColor, _labelInk;
        private bool _bound, _labelSwapped;
        private int _serial = -1, _retry;

        public bool Bound => _bound;
        public int EffectCount => _effects.Count;
        public bool LabelSwapped => _labelSwapped;
        public int Pushes { get; private set; }

        public void Configure(HudController hud, ImpactFrameDirector308 director, SpellDeploy308ProfileSO profile)
        {
            _hud = hud; _director = director; _profile = profile; _bound = false; _serial = -1;
        }

        private bool TryBind()
        {
            if (_hud == null || _hud.Canvas == null) return false;
            var canvas = _hud.Canvas.transform;
            var bottle = canvas.Find("Meters304/InkBottle");
            if (bottle == null && _hud.LockOn304 == null) return false;   // the prototype (unskinned) HUD has nothing to react
            _effects.Clear();
            if (bottle != null) foreach (Transform child in bottle) Attach(child.GetComponent<Image>());
            var lockOn = _hud.LockOn304;
            if (lockOn != null)
            {
                Attach(lockOn.Enso);
                var rim = lockOn.transform.Find("EnsoRim");
                if (rim != null) Attach(rim.GetComponent<Image>());
            }
            var prompt = _hud.Prompt304;
            _label = prompt != null ? prompt.Label : null;
            if (_label != null && _hud.Skin != null)
            {
                var style = V.Style(_hud.Skin);
                _labelInverse = style != null ? style.TmpMaterial(_label.font, TmpPreset304.Ink_UnderPaper) : null;
                _labelInk = style != null ? style.Ink : Color.black;
            }
            return true;
        }

        private void Attach(Image image)
        {
            if (image == null) return;
            var effect = image.GetComponent<HudImpactReact308>();
            if (effect == null) effect = image.gameObject.AddComponent<HudImpactReact308>();
            _effects.Add(effect);
        }

        private void Update()
        {
            if (_director == null) return;
            if (!_bound)
            {
                if ((_retry++ & 15) != 0) return;
                _bound = TryBind();
                if (!_bound) return;
            }
            if (_director.StateSerial == _serial) return;
            _serial = _director.StateSerial;
            Push();
        }

        private void Push()
        {
            Vector4 impact = _director.ImpactGlobal, hud = _director.HudGlobal;
            bool on = impact.z > 0f && (hud.x > 0f || hud.y > 0f || hud.w > 0f);
            Vector2 screen = new Vector2(impact.x * Screen.width, impact.y * Screen.height);
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i] != null) _effects[i].SetState(on, screen, hud.x, hud.y, hud.z, hud.w, impact.z);
            SwapLabel(on && hud.w > .5f);
            Pushes++;
        }

        // the prompt text: ink on the (shader-swapped) paper underlay for the inversion frame, then back
        private void SwapLabel(bool swap)
        {
            if (_label == null || swap == _labelSwapped) return;
            if (swap)
            {
                _labelNormal = _label.fontSharedMaterial; _labelColor = _label.color;
                if (_labelInverse != null) _label.fontSharedMaterial = _labelInverse;
                _label.color = new Color(_labelInk.r, _labelInk.g, _labelInk.b, _labelColor.a);
            }
            else
            {
                if (_labelNormal != null) _label.fontSharedMaterial = _labelNormal;
                _label.color = _labelColor;
            }
            _labelSwapped = swap;
        }

        private void OnDisable()
        {
            SwapLabel(false);
            for (int i = 0; i < _effects.Count; i++) if (_effects[i] != null) _effects[i].SetState(false, default, 0f, 0f, 0f, 0f, 0f);
            _serial = -1;
        }
    }
}
