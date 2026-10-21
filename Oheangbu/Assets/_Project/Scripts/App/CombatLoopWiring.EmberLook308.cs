using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-125 5 (c) hook file: CombatLifetimeScope calls BootEmberLook308 once the container is built. The look only
    // listens (PatchLaid308 and the three scorch events) and reads (Patches308, Ember308); nothing it does reaches a rule.
    public sealed partial class CombatLoopWiring
    {
        private EmberPatchLook308 _emberLook308;
        public EmberPatchLook308 EmberLook308 => _emberLook308;

        public void BootEmberLook308()
        {
            if (!Application.isPlaying || _emberLook308 != null) return;
            GameObject host = null;
            try
            {
                host = new GameObject("EmberPatchLook308") { hideFlags = HideFlags.DontSave };
                host.transform.SetParent(transform, false);
                _emberLook308 = host.AddComponent<EmberPatchLook308>();
                _emberLook308.Boot(this, _hud, _playerTransform != null ? _playerTransform : _playerVitals != null ? _playerVitals.transform : null);
            }
            catch (System.Exception e)
            {
                // presentation only: a failure here must not stop the container build callbacks (combat boots without the look)
                Debug.LogException(e, this);
                _emberLook308 = null;
                if (host != null) Destroy(host);
            }
        }
    }
}
