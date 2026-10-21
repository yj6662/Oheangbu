using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App
{
    // #308 D308-125 적용 5 (debate decision, TEST): burning ground. A ground eruption whose profile carries a Patch leaves an
    // ember patch where it burst, hit or miss (a parried burst leaves none, a cancelled one never bursts). The player standing
    // in it takes the patch's ticks (dodge frames pass through them) and, after a dwell, a scorch that keeps burning after
    // they step out (dodge does not put it out). 운's ring washes the scorch off and keeps it from catching; the patch still
    // burns. 온's wave wipes patches (EnemyGroundPatch308 is ISpellPurgeable308). The rule is EmberExposure308 (pure); this
    // file only finds the patch under the feet and hands HP its ticks (PlayerVitals.TakeLingeringDamage308: no Damaged, no
    // hit reaction, no letter break). Enemies are never burnt by it.
    public sealed partial class CombatLoopWiring
    {
        private readonly List<EnemyGroundPatch308> _patches308 = new List<EnemyGroundPatch308>();
        private readonly EmberExposure308 _ember308 = new EmberExposure308();
        private readonly Dictionary<EnemyController, Action<EnemyAttackImpact>> _patchHooks308 = new Dictionary<EnemyController, Action<EnemyAttackImpact>>();
        private readonly RaycastHit[] _patchHits308 = new RaycastHit[8];
        private bool _emberSpared308;
        private Func<string, GameObject> _patchHost308;   // edit-mode checks hand in their own maker (born hidden in the preview scene)

        // Read-only re-broadcasts for the presentation (smoke on the hands, the ember sound, the 운 splash). No rule hangs on them.
        public event Action<EnemyGroundPatch308> PatchLaid308;
        public event Action ScorchStarted308, ScorchWashed308, ScorchEnded308;

        public IReadOnlyList<EnemyGroundPatch308> Patches308 => _patches308;   // checks read this
        public EmberExposure308 Ember308 => _ember308;

        private void HookPatches308()
        {
            foreach (var controller in _controllers)
            {
                if (controller == null || _patchHooks308.ContainsKey(controller)) continue;
                var owner = controller.GetComponent<EnemyVitals>();
                Action<EnemyAttackImpact> hook = impact => OnEruptionImpact308(controller, owner, impact);
                controller.AttackImpactResolved += hook;
                _patchHooks308.Add(controller, hook);
            }
        }

        private void UnhookPatches308()
        {
            foreach (var pair in _patchHooks308)
                if (pair.Key != null) pair.Key.AttackImpactResolved -= pair.Value;
            _patchHooks308.Clear();
        }

        private void OnEruptionImpact308(EnemyController controller, EnemyVitals owner, EnemyAttackImpact impact)
        {
            var profile = controller != null ? controller.AttackProfile : null;
            if (profile == null || profile.Delivery != EnemyAttackDelivery.GroundEruption || profile.Patch == null) return;
            if (impact.Outcome == ParryOutcome.Success) return;
            LayPatch308(profile.Patch, owner, impact.Point, Time.time);
        }

        /// <summary>Lays one patch at the burst point, oldest of the same owner's dropped past MaxPerOwner.</summary>
        private EnemyGroundPatch308 LayPatch308(EnemyGroundPatchSO profile, EnemyVitals owner, Vector3 point, float now)
        {
            if (profile == null || !profile.TryValidate(out _) || !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return null;
            Scene scene = _playerVitals != null ? _playerVitals.gameObject.scene : gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return null;

            int mine = 0, oldest = -1;
            for (int i = 0; i < _patches308.Count; i++)
            {
                var p = _patches308[i];
                if (p == null || p.Owner != owner) continue;
                mine++;
                if (oldest < 0 || p.Born < _patches308[oldest].Born) oldest = i;
            }
            if (mine >= profile.MaxPerOwner && oldest >= 0) DropPatch308(oldest);

            GameObject host;
            if (_patchHost308 != null) host = _patchHost308("EmberPatch308");
            else
            {
                host = new GameObject("EmberPatch308");
                if (!Application.isPlaying) host.hideFlags = HideFlags.HideAndDontSave;
            }
            if (host == null) return null;
            if (host.scene != scene) SceneManager.MoveGameObjectToScene(host, scene);
            if (host.scene != scene)
            {
                if (Application.isPlaying) Destroy(host); else DestroyImmediate(host);
                return null;
            }
            var patch = host.AddComponent<EnemyGroundPatch308>();
            patch.Configure(profile, owner, point, PatchNormal308(scene, point, owner), now);
            _patches308.Add(patch);
            PatchLaid308?.Invoke(patch);
            return patch;
        }

        // The ground's tilt under the burst (the burst point itself came from the enemy's own ground search). Bodies, moving
        // things and steep faces are not ground; nothing found = flat.
        private Vector3 PatchNormal308(Scene scene, Vector3 point, EnemyVitals owner)
        {
            int count = scene.GetPhysicsScene().Raycast(point + Vector3.up * 1.5f, Vector3.down, _patchHits308, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            Vector3 normal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                var hit = _patchHits308[i];
                if (hit.collider is CharacterController || hit.rigidbody != null || hit.normal.y < .5f) continue;
                if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance; normal = hit.normal;
            }
            return normal;
        }

        // Every frame after the spells: drop what is spent, then burn the feet in the first live patch they stand in (patches
        // never stack: one tick a clock, whichever patch). The clock comes in so the edit-mode checks can drive it.
        private void TickPatches308(float now)
        {
            for (int i = _patches308.Count - 1; i >= 0; i--)
            {
                var p = _patches308[i];
                if (p == null) { _patches308.RemoveAt(i); continue; }
                // the owner's life was reset (a rest) while the patch burned: it goes with the old life. A dead owner's patch stays.
                bool reset = p.Owner != null && p.Owner.isActiveAndEnabled && p.Owner.IsAlive && p.Owner.LifeRevision != p.OwnerLife;
                if (!p.Live(now) || reset) DropPatch308(i);
            }
            if (_playerVitals == null || _playerVitals.Hp01 <= 0f) return;

            Vector3 feet = _playerTransform != null ? _playerTransform.position : _playerVitals.transform.position;
            EnemyGroundPatch308 standing = null;
            foreach (var p in _patches308)
                if (p.Live(now) && p.Contains(feet)) { standing = p; break; }
            var step = _ember308.Advance(now, standing != null, standing != null ? standing.Profile.Numbers : default, _playerDomain308.Warded(now));
            if (step.Washed) ScorchWashed308?.Invoke();
            if (step.Ended) ScorchEnded308?.Invoke();
            if (step.Started) ScorchStarted308?.Invoke();

            _emberSpared308 = false;
            if (step.PatchDamage > 0f) _playerVitals.TakeLingeringDamage308(step.PatchDamage, true);
            // a spare on the patch tick puts the scorch out (OnSpared308): its tick of this frame does not land either
            if (step.ScorchDamage > 0f && !_emberSpared308 && _playerVitals.Hp01 > 0f) _playerVitals.TakeLingeringDamage308(step.ScorchDamage, false);
        }

        private void OnSpared308()
        {
            _emberSpared308 = true;
            if (_ember308.EndScorch()) ScorchEnded308?.Invoke();
        }

        private void DropPatch308(int index)
        {
            var p = _patches308[index];
            _patches308.RemoveAt(index);
            if (p == null) return;
            if (Application.isPlaying) Destroy(p.gameObject); else DestroyImmediate(p.gameObject);
        }

        // Death, disabling, a rest: nothing keeps burning.
        private void ClearPatches308()
        {
            for (int i = _patches308.Count - 1; i >= 0; i--) DropPatch308(i);
            _ember308.Clear();
        }
    }
}
