using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>Playtest-only read-only adapter for interaction text and ink-bottle motion.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldMacroPlaytestHudPresenter : MonoBehaviour
    {
        public HudController Hud;
        public WorldMacroPlaytestSession Session;
        public CharacterController Body;
        [Min(.1f)] public float MovementReferenceSpeed = 4.5f;

        private PlayerVitals _vitals;
        private bool _presenting;

        private void OnEnable() { Activate(); }
        private void Start() { Activate(); }

        private void Activate()
        {
            if (_presenting || Hud == null || !Hud.IsSkinned || Session == null || Body == null) return;
            _vitals = Body.GetComponent<PlayerVitals>();
            if (_vitals != null) _vitals.Damaged += OnDamaged;
            Session.SetCanvasHudPresenterActive(true);
            _presenting = true;
        }

        private void LateUpdate()
        {
            if (!_presenting) { Activate(); if (!_presenting) return; }
            Hud.SetInteractionText(Session.CurrentHudText);
            Vector3 local = Body.transform.InverseTransformDirection(Body.velocity);
            Hud.SetInkMotion(new Vector2(local.x, local.z) / Mathf.Max(.1f, MovementReferenceSpeed));
        }

        private void OnDamaged(float amount)
        {
            Hud.KickInk(.25f + Mathf.Clamp01(amount / 25f) * .75f);
        }

        private void OnDisable() { Deactivate(); }
        private void OnDestroy() { Deactivate(); }

        private void Deactivate()
        {
            if (!_presenting) return;
            if (_vitals != null) _vitals.Damaged -= OnDamaged;
            if (Hud != null)
            {
                Hud.SetInteractionText(null);
                Hud.SetInkMotion(Vector2.zero);
            }
            if (Session != null) Session.SetCanvasHudPresenterActive(false);
            _vitals = null;
            _presenting = false;
        }
    }
}
