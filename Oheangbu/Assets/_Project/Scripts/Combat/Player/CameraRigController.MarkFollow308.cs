using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 player juice, 3rd revision (SPEC-ANIM-JUICE-308 section 4d, D308-24 answer 23) [TEST]: marks that sit on a world
    // point - the lock-on reticle with its groggy disc, the target health stroke under it - follow the camera reaction.
    //   · The reaction is on the camera only while the frame is drawn (CameraRigController.RenderOffset308.cs). A HUD mark is
    //     placed in LateUpdate from the camera as it is then, so during the cast kick the target moved on screen and the mark
    //     did not: 0.9 degrees is 17 px at 1080p in the 52-degree close-up.
    //   · The owner of the offset answers one question here: by how many pixels will this world point be drawn away from
    //     where the un-offset camera puts it? The formula is the one OnRenderBegin308 applies - local rotation x
    //     Euler(nod, 0, roll), field of view + kick. The HUD adds the answer to the position it computes today. With no offset
    //     it is told "nothing to add" and does not touch its own value, so the mark is where it always was, bit for bit.
    //   · The juice pushes this frame's offset late in LateUpdate (order 1000), after the HUD placed its marks (order 0). The
    //     owner announces every change of the offset (RenderOffsetChanged308); the HUD places its mark again on that call.
    //   · Whether marks follow is data: the pusher sets RenderOffsetMarksFollow308 from its profile (Camera.MarksFollow).
    //     Off, no offset, the draw flag, or another camera: TryGetMarkShift308 answers false.
    //   · Small numbers only (the camera position is subtracted first), no allocation, no static field. No game rule calls
    //     this: the lock-on, the aim, the pull and the drawing input keep reading the un-offset camera.
    public sealed partial class CameraRigController
    {
        private bool _marksFollow308;

        /// <summary>World-anchored HUD marks follow the offset. Set by whoever pushes the offset, from its data. Default false.</summary>
        public bool RenderOffsetMarksFollow308 { get => _marksFollow308; set => _marksFollow308 = value; }
        /// <summary>The offset for the coming render changed (raised inside SetRenderOffset308 and when the rig lets go of it).</summary>
        public event Action RenderOffsetChanged308;
        /// <summary>Diagnostics: listeners of RenderOffsetChanged308 that threw (must stay 0). The offset itself is not affected.</summary>
        public int RenderOffsetListenerFaults308 { get; private set; }

        // A listener is HUD code. Its fault must not cut the camera owner short (the caller may be the drawing input's event).
        private void AnnounceRenderOffset308()
        {
            Action listeners = RenderOffsetChanged308;
            if (listeners == null) return;
            try { listeners(); }
            catch (Exception e) { RenderOffsetListenerFaults308++; Debug.LogException(e, this); }
        }

        /// <summary>Pixels (x right, y up) by which the coming render draws `world` away from where this camera, un-offset, puts
        /// it. False = nothing to add: no offset, marks do not follow, the draw flag is on, or `camera` is not this rig's camera.</summary>
        public bool TryGetMarkShift308(Camera camera, Vector3 world, out Vector2 shiftPixels)
        {
            shiftPixels = default;
            Vector3 o = _renderOffset308;
            if (!_marksFollow308 || (o.x == 0f && o.y == 0f && o.z == 0f) || _drawing) return false;
            if (camera == null || _camera == null || camera.transform != _camera) return false;
            return MarkShift308(camera, world, o, out shiftPixels);
        }

        /// <summary>The same question for any camera and any offset (x nod, y roll, z field of view; degrees). Pure.</summary>
        public static bool MarkShift308(Camera camera, Vector3 world, Vector3 offset, out Vector2 shiftPixels)
        {
            shiftPixels = default;
            Transform view = camera.transform;
            // the point in the camera's own frame (x right, y up, z ahead), then in the frame of the camera as it will be drawn:
            // OnRenderBegin308 sets localRotation = saved x Euler(nod, 0, roll), so the world rotation gains the same factor
            Vector3 seen = Quaternion.Inverse(view.rotation) * (world - view.position);
            Vector3 kicked = Quaternion.Inverse(Quaternion.Euler(offset.x, 0f, offset.y)) * seen;
            Matrix4x4 p = camera.projectionMatrix;
            // a wider field of view shrinks the picture about its centre: the two focal terms scale by tan(half) / tan(half')
            float zoom = 1f;
            if (offset.z != 0f && !camera.orthographic)
            {
                float fov = camera.fieldOfView;
                zoom = Mathf.Tan(fov * .5f * Mathf.Deg2Rad) / Mathf.Tan((fov + offset.z) * .5f * Mathf.Deg2Rad);
            }
            if (!MarkNdc308(p, seen, 1f, out float ax, out float ay) || !MarkNdc308(p, kicked, zoom, out float bx, out float by)) return false;
            Rect rect = camera.pixelRect;
            shiftPixels = new Vector2((bx - ax) * .5f * rect.width, (by - ay) * .5f * rect.height);
            return true;
        }

        // clip = P x (x, y, -z, 1); zoom scales the two focal terms (m00, m11) only - a lens shift (m02, m12) is not a focal term
        private static bool MarkNdc308(Matrix4x4 p, Vector3 v, float zoom, out float nx, out float ny)
        {
            float z = -v.z;
            float w = p.m30 * v.x + p.m31 * v.y + p.m32 * z + p.m33;
            if (Mathf.Abs(w) <= 1e-7f) { nx = 0f; ny = 0f; return false; }   // on the camera plane: no screen place
            nx = (p.m00 * zoom * v.x + p.m01 * v.y + p.m02 * z + p.m03) / w;
            ny = (p.m10 * v.x + p.m11 * zoom * v.y + p.m12 * z + p.m13) / w;
            return true;
        }
    }
}
