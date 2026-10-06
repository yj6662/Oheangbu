using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 player juice, 3rd revision (SPEC-ANIM-JUICE-308 section 4d, D308-24 answer 23 "the mark moves with the view") [TEST].
    // The lock-on reticle is placed by one call (CombatLoopWiring.LateUpdate -> UpdateReticle); the groggy disc is its child and
    // the target health stroke is placed from it (PlaceTargetHp306). During a camera reaction the frame is drawn by a camera
    // that nods / widens for that render only, so the target moved on screen and the reticle did not (17 px at 1080p).
    //   · UpdateReticle puts the reticle where it always did and then calls FollowReticle308. That adds the pixels the camera
    //     owner reports for the reaction (CameraRigController.TryGetMarkShift308). No reaction, switch off, no rig: the owner
    //     answers false and nothing here writes the position - the reticle is bit for bit where UpdateReticle put it.
    //   · The juice pushes this frame's reaction after UpdateReticle ran (rig LateUpdate order 1000). The owner announces the
    //     change; OnFollowOffsetChanged308 places the reticle again from this frame's own numbers, and the health stroke with it.
    //   · Whether marks follow is the juice profile's switch (Camera.MarksFollow); it reaches this file through the owner only.
    //   · The visibility test of UpdateReticle (behind the camera / off screen) keeps using the un-offset position.
    //   · No new HUD element, no text, no allocation per frame (one delegate, made once), no static field. The owner is found
    //     once per camera, above the camera (the player's hierarchy); a scene without one keeps today's placement.
    public sealed partial class HudController
    {
        private CameraRigController _follow308;      // owner of the render-time offset of the camera the reticle was placed with
        private Camera _followCamera308;
        private System.Action _followChanged308;
        private Vector3 _followWorld308, _followScreen308;
        private int _followFrame308 = -1;

        /// <summary>Diagnostics (checks): the shift on the reticle right now (pixels; zero = none), placements that carried a
        /// shift, and placements done again because the reaction changed after UpdateReticle.</summary>
        public Vector2 ReticleFollowShift308 { get; private set; }
        public int ReticleFollowShifts308 { get; private set; }
        public int ReticleFollowReplaced308 { get; private set; }

        // UpdateReticle, right after the reticle was put on `screen` (the un-offset camera's answer for `world`)
        private void FollowReticle308(Camera cam, Vector3 world, Vector3 screen)
        {
            if (!ReferenceEquals(cam, _followCamera308)) BindFollow308(cam);
            _followWorld308 = world; _followScreen308 = screen; _followFrame308 = Time.frameCount;
            ApplyFollow308();
        }

        private void ApplyFollow308()
        {
            ReticleFollowShift308 = Vector2.zero;
            if (_follow308 == null || !_follow308.TryGetMarkShift308(_followCamera308, _followWorld308, out Vector2 shift)) return;
            _reticle.position = new Vector3(_followScreen308.x + shift.x, _followScreen308.y + shift.y, _followScreen308.z);
            ReticleFollowShift308 = shift; ReticleFollowShifts308++;
        }

        private void BindFollow308(Camera cam)
        {
            ReleaseFollow308();
            _followCamera308 = cam;
            _follow308 = cam != null ? cam.GetComponentInParent<CameraRigController>() : null;
            if (_follow308 == null) return;
            if (_followChanged308 == null) _followChanged308 = OnFollowOffsetChanged308;
            _follow308.RenderOffsetChanged308 += _followChanged308;
        }

        // OnDestroy, and before another camera's owner is bound
        private void ReleaseFollow308()
        {
            if (!ReferenceEquals(_follow308, null) && _followChanged308 != null) _follow308.RenderOffsetChanged308 -= _followChanged308;
            _follow308 = null; _followCamera308 = null;
        }

        // The reaction for the coming render changed after UpdateReticle ran this frame: the same reticle, the new shift.
        private void OnFollowOffsetChanged308()
        {
            if (_followFrame308 != Time.frameCount || _lockPreview || _reticle == null || !_reticle.gameObject.activeSelf) return;
#if UNITY_EDITOR
            if (_editorDiagnosticState) return;
#endif
            _reticle.position = _followScreen308;   // UpdateReticle's own value of this frame, then the shift (if any) on top
            ApplyFollow308();
            ReticleFollowReplaced308++;
            PlaceTargetHp306();
        }
    }
}
