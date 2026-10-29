using UnityEngine;
using UnityEngine.VFX;

namespace Oheangbu.App.SpellFx
{
    /// <summary>The default part for a VFX Graph prefab (D308-153). It drives the child <see cref="VisualEffect"/>s from the cast:
    ///   exposed properties (all optional - a name the graph does not expose is skipped, no error):
    ///     Origin (Vector3), Forward (Vector3), Radius (float), Age (float, seconds since the launch, every step), HitPos (Vector3, set before OnHit);
    ///   events: OnLaunch (the launch frame), OnHit (position attribute filled when the graph reads it), OnEnd (the cast's end).
    /// Graph contract: start the spawning from OnLaunch (or leave the default OnPlay wired: with Hold Until Launch the component stays disabled until the launch, so
    /// either way nothing shows before the shape leaves). Nothing here is read by a rule; it only presents.</summary>
    public sealed class SpellFxVfxGraphPart : MonoBehaviour, ISpellFxPart
    {
        [Tooltip("Empty = every VisualEffect under this object.")]
        [SerializeField] VisualEffect[] effects;
        [Tooltip("Keep the VisualEffect components disabled until the launch frame.")]
        [SerializeField] bool holdUntilLaunch = true;
        [SerializeField] float radiusScale = 1f;
        [SerializeField] string originProperty = "Origin", forwardProperty = "Forward", radiusProperty = "Radius", ageProperty = "Age", hitPosProperty = "HitPos";
        [SerializeField] string launchEvent = "OnLaunch", hitEvent = "OnHit", endEvent = "OnEnd";

        int _origin, _forward, _radius, _age, _hitPos, _position;
        bool _ready;
        /// <summary>Events sent so far (bookkeeping).</summary>
        public int Sent { get; private set; }

        void Ready()
        {
            if (_ready) return;
            _ready = true;
            // property ids cannot be taken in a constructor / field initialiser: here, on the first call
            _origin = Shader.PropertyToID(originProperty); _forward = Shader.PropertyToID(forwardProperty); _radius = Shader.PropertyToID(radiusProperty);
            _age = Shader.PropertyToID(ageProperty); _hitPos = Shader.PropertyToID(hitPosProperty); _position = Shader.PropertyToID("position");
            if (effects == null || effects.Length == 0) effects = GetComponentsInChildren<VisualEffect>(true);
        }

        static bool Playable(VisualEffect vfx) => vfx != null && vfx.visualEffectAsset != null;

        public void OnBegin(SpellFxCast cast)
        {
            Ready();
            if (!holdUntilLaunch) return;
            foreach (var vfx in effects) if (vfx != null) vfx.enabled = false;
        }

        public void OnLaunch(SpellFxCast cast)
        {
            Ready();
            foreach (var vfx in effects)
            {
                if (vfx == null) continue;
                if (holdUntilLaunch) vfx.enabled = true;
                if (!Playable(vfx)) continue;
                Set3(vfx, _origin, cast.Origin); Set3(vfx, _forward, cast.Forward); SetF(vfx, _radius, cast.Radius * radiusScale); SetF(vfx, _age, 0f);
                Send(vfx, launchEvent, null);
            }
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            Ready();
            foreach (var vfx in effects) if (Playable(vfx)) SetF(vfx, _age, ageSeconds);
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Ready();
            foreach (var vfx in effects)
            {
                if (!Playable(vfx)) continue;
                Set3(vfx, _hitPos, hit.Position);
                Send(vfx, hitEvent, hit.Position);
            }
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            Ready();
            if (reason == SpellFxEndReason.Aborted) return;          // the director removes the prefab at once
            foreach (var vfx in effects) if (Playable(vfx)) Send(vfx, endEvent, null);
        }

        void Set3(VisualEffect vfx, int id, Vector3 v) { if (vfx.HasVector3(id)) vfx.SetVector3(id, v); }
        void SetF(VisualEffect vfx, int id, float f) { if (vfx.HasFloat(id)) vfx.SetFloat(id, f); }

        // one event attribute per effect, made on the first hit and reused (a hit is rare; the attribute is native memory, freed with the part)
        readonly System.Collections.Generic.Dictionary<VisualEffect, VFXEventAttribute> _attributes = new System.Collections.Generic.Dictionary<VisualEffect, VFXEventAttribute>();

        void Send(VisualEffect vfx, string eventName, Vector3? position)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            VFXEventAttribute attribute = null;
            if (position.HasValue)
            {
                if (!_attributes.TryGetValue(vfx, out attribute)) _attributes[vfx] = attribute = vfx.CreateVFXEventAttribute();
                if (attribute != null && attribute.HasVector3(_position)) attribute.SetVector3(_position, position.Value); else attribute = null;
            }
            if (attribute != null) vfx.SendEvent(eventName, attribute); else vfx.SendEvent(eventName);
            Sent++;
        }

        void OnDestroy()
        {
            foreach (var a in _attributes.Values) if (a != null) a.Dispose();
            _attributes.Clear();
        }
    }
}