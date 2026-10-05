using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.Combat
{
    // #308 player juice D (SPEC-ANIM-JUICE-308 section 4b, D308-20 answer 4) [TEST]: the one owner of the camera reaction.
    //   · The offset is a nod / roll in the camera's own frame (degrees) and a field-of-view change. Position is never moved.
    //   · It exists on the camera only while the frame is drawn: put in when the render context that draws this camera begins,
    //     and the saved values are written back when that context ends (assigned, not subtracted - nothing can accumulate).
    //     PlayerMotor yaw / pitch, the lock-on pull, this rig's pose blend, boom and FOV snap / return, aiming rays and the
    //     drawing input all run before the render and never see it. So it is layered after the pull and cannot fight it.
    //   · Why the context events and not begin / endCameraRendering: the field-of-view breath (ImpactFrameDirector308) and the
    //     view shakes (Riposte301, Guardian302Actor) save and restore on the per-camera events. Those lie inside the context,
    //     so they save "rig value + this offset" and put exactly that back; then this rig puts its own value back. They add.
    //     A second save / restore pair on the per-camera events would restore in subscription order and leave one of the two
    //     offsets on the camera.
    //   · WorldMacroPlayerGetUpCamera303 uses the same context events for a full pose override; the caller sends no offset
    //     while it is active (the get-up starts behind the death veil).
    //   · Never while the draw mode is on (D308-21): the caller checks the draw mode and the live strokes, and this rig refuses
    //     on its own flag as a second lock.
    //   · Subscribed only while an offset is set. No static field.
    public sealed partial class CameraRigController
    {
        private Vector3 _renderOffset308;   // x nod (+ = the view dips), y roll, z field of view
        private bool _renderHooked308, _renderApplied308;
        private Quaternion _renderSavedRotation308, _renderWrittenRotation308;
        private float _renderSavedFov308, _renderWrittenFov308;
        private Action<ScriptableRenderContext, List<Camera>> _renderBegin308, _renderEnd308;

        /// <summary>The offset the next render of this camera adds (x nod, y roll, z field of view; degrees).</summary>
        public Vector3 RenderOffset308 => _renderOffset308;
        /// <summary>The offset is on the camera right now (true only between the two context events).</summary>
        public bool RenderOffsetOnCamera308 => _renderApplied308;
        /// <summary>Diagnostics: renders that carried an offset / restores that found another writer's value (must stay 0) /
        /// offsets refused because the draw mode was on.</summary>
        public int RenderOffsetApplied308 { get; private set; }
        public int RenderOffsetConflicts308 { get; private set; }
        public int RenderOffsetRefusedDrawing308 { get; private set; }
        /// <summary>The largest offset that reached a render since the last reset (x nod, y roll, z field of view; absolute).</summary>
        public Vector3 RenderOffsetPeak308 { get; private set; }
        public void ResetRenderOffsetDiagnostics308()
        { RenderOffsetApplied308 = RenderOffsetConflicts308 = RenderOffsetRefusedDrawing308 = 0; RenderOffsetPeak308 = Vector3.zero; }

        /// <summary>Sets the camera reaction for the coming renders (all zero = none, and the render events are released).</summary>
        public void SetRenderOffset308(float nodDegrees, float rollDegrees, float fovDegrees)
        {
            bool any = nodDegrees != 0f || rollDegrees != 0f || fovDegrees != 0f;
            if (any && _drawing) { RenderOffsetRefusedDrawing308++; nodDegrees = rollDegrees = fovDegrees = 0f; any = false; }
            _renderOffset308 = new Vector3(nodDegrees, rollDegrees, fovDegrees);
            if (_cam == null && _camera != null) _cam = _camera.GetComponent<Camera>();   // an instance whose Awake has not run (edit-mode preview)
            if (any == _renderHooked308) return;
            if (any)
            {
                if (_renderBegin308 == null) { _renderBegin308 = OnRenderBegin308; _renderEnd308 = OnRenderEnd308; }
                RenderPipelineManager.beginContextRendering += _renderBegin308;
                RenderPipelineManager.endContextRendering += _renderEnd308;
            }
            else UnhookRender308();
            _renderHooked308 = any;
        }

        public void ClearRenderOffset308() { SetRenderOffset308(0f, 0f, 0f); }

        private void UnhookRender308()
        {
            if (_renderApplied308) RestoreRender308(false);
            if (_renderBegin308 == null) return;
            RenderPipelineManager.beginContextRendering -= _renderBegin308;
            RenderPipelineManager.endContextRendering -= _renderEnd308;
        }

        // OnDisable: nothing of the reaction may outlive the rig
        private void ReleaseRenderOffset308()
        {
            _renderOffset308 = Vector3.zero;
            if (_renderHooked308) UnhookRender308();
            _renderHooked308 = false;
        }

        private bool DrawsMyCamera308(List<Camera> cameras)
        {
            if (_cam == null || cameras == null) return false;
            for (int i = 0; i < cameras.Count; i++) if (cameras[i] == _cam) return true;
            return false;
        }

        private void OnRenderBegin308(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_cam == null || _camera == null) { _renderApplied308 = false; return; }
            // scene view, film cameras, and a context nested inside the one that draws this camera (a reflection render):
            // left alone - an offset that is on the camera stays until this camera's own context ends
            // (the same test WorldMacroPlayerGetUpCamera303 makes on these events)
            if (!DrawsMyCamera308(cameras)) return;
            // this camera's context begins while an offset is still on: that render never reached its end (an exception inside it).
            // The rig's own values go back first, and the case is counted.
            if (_renderApplied308) RestoreRender308(true);
            Vector3 o = _renderOffset308;
            if (o.x == 0f && o.y == 0f && o.z == 0f) return;
            if (_drawing) { RenderOffsetRefusedDrawing308++; return; }
            _renderSavedRotation308 = _camera.localRotation; _renderSavedFov308 = _cam.fieldOfView;
            if (o.x != 0f || o.y != 0f) _camera.localRotation = _renderSavedRotation308 * Quaternion.Euler(o.x, 0f, o.y);
            if (o.z != 0f && !_cam.orthographic) _cam.fieldOfView = _renderSavedFov308 + o.z;
            _renderWrittenRotation308 = _camera.localRotation; _renderWrittenFov308 = _cam.fieldOfView;
            _renderApplied308 = true; RenderOffsetApplied308++;
            RenderOffsetPeak308 = Vector3.Max(RenderOffsetPeak308, new Vector3(Mathf.Abs(o.x), Mathf.Abs(o.y), Mathf.Abs(o.z)));
        }

        private void OnRenderEnd308(ScriptableRenderContext context, List<Camera> cameras)
        {
            // only the end of the context that draws this camera takes the offset off (not the end of a nested one)
            if (_renderApplied308 && DrawsMyCamera308(cameras)) RestoreRender308(false);
        }

        private void RestoreRender308(bool lateBegin)
        {
            _renderApplied308 = false;
            if (_camera == null || _cam == null) return;
            // another writer changed the camera inside this bracket and did not put it back: the rig's values still go back,
            // and the case is counted (the acceptance value is 0)
            Quaternion now = _camera.localRotation, wrote = _renderWrittenRotation308;
            bool same = now.x == wrote.x && now.y == wrote.y && now.z == wrote.z && now.w == wrote.w && _cam.fieldOfView == _renderWrittenFov308;
            if (lateBegin || !same) RenderOffsetConflicts308++;
            _camera.localRotation = _renderSavedRotation308;
            _cam.fieldOfView = _renderSavedFov308;
        }
    }
}
