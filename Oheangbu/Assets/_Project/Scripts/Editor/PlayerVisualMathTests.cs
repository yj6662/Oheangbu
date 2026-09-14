using System;
using Oheangbu.Presentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>임시 오브젝트만 만드는 수학 회귀 검사. 저장된 씬과 프리팹에는 쓰지 않는다.</summary>
    public static class PlayerVisualMathTests
    {
        public static string Run()
        {
            int solves = 0, projections = 0;
            float worstJointError = 0f, worstPixels = 0f;
            var root = new GameObject("PlayerVisualMathTests_Temporary");
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var upper = new GameObject("Upper").transform; upper.SetParent(root.transform, false);
                var forearm = new GameObject("Forearm").transform; forearm.SetParent(upper, false);
                var hand = new GameObject("Hand").transform; hand.SetParent(forearm, false);
                Vector3[] targets = { new Vector3(.2f,.12f,.24f), new Vector3(-.3f,.1f,.2f),
                    new Vector3(.05f,-.35f,.12f), new Vector3(0f,0f,.51f) };
                foreach (float yaw in new[] { 0f, 65f, 173f })
                {
                    root.transform.SetPositionAndRotation(new Vector3(2f,3f,4f), Quaternion.Euler(11f, yaw, -8f));
                    foreach (Vector3 local in targets)
                    {
                        upper.localRotation = forearm.localRotation = hand.localRotation = Quaternion.identity;
                        forearm.localPosition = Vector3.forward * .28f;
                        hand.localPosition = Vector3.forward * .26f;
                        Vector3 target = root.transform.TransformPoint(local);
                        PlayerVisualIK.Solve(upper, forearm, hand, target, root.transform.TransformPoint(new Vector3(0f,.5f,0f)), root.transform.rotation, 1f);
                        float error = Vector3.Distance(hand.position, target);
                        worstJointError = Mathf.Max(worstJointError, error);
                        Require(error < .0002f, "reachable IK endpoint differs from target");
                        Require(Mathf.Abs(Vector3.Distance(upper.position, forearm.position) - .28f) < .0001f, "upper arm stretched");
                        Require(Mathf.Abs(Vector3.Distance(forearm.position, hand.position) - .26f) < .0001f, "forearm stretched");
                        Require(root.transform.position == new Vector3(2f,3f,4f), "solver moved gameplay root");
                        solves++;
                    }
                }
                var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.nearClipPlane = .03f;
                camera.pixelRect = new Rect(0f,0f,1920f,1080f);
                foreach (float aspect in new[] { 4f/3f, 16f/9f, 21f/9f })
                foreach (float fov in new[] { 52f, 60f })
                foreach (float pitch in new[] { -80f, -35f, 0f, 60f, 80f })
                {
                    camera.aspect = aspect; camera.fieldOfView = fov;
                    camera.transform.SetPositionAndRotation(new Vector3(3f,1.75f,2f), Quaternion.Euler(pitch, 31f, 0f));
                    Vector3 shoulder = camera.transform.TransformPoint(new Vector3(.18f,-.12f,-.03f));
                    for (int x = 0; x <= 4; x++)
                    for (int y = 0; y <= 4; y++)
                    {
                        Vector3 screen = camera.ViewportToScreenPoint(new Vector3(x / 4f, y / 4f, 0f));
                        Ray ray = camera.ScreenPointToRay(screen);
                        bool reachable = PlayerVisualIK.FindTip(ray, shoulder, .47f, .28f, .18f, 1.1f, .55f, out Vector3 tip);
                        Require(reachable, "supported viewport ray is outside near arm envelope");
                        Require(Vector3.Distance(tip, shoulder) <= .75f, "tip outside arm and fixed brush reach");
                        float pixels = Vector2.Distance(camera.WorldToScreenPoint(tip), screen);
                        worstPixels = Mathf.Max(worstPixels, pixels);
                        Require(pixels < .02f, "ray depth correction changed screen XY");
                        projections++;
                    }
                }
                bool impossible = PlayerVisualIK.FindTip(new Ray(Vector3.zero, Vector3.forward), new Vector3(10f,0f,0f),
                    .5f,.28f,.18f,1.1f,.55f,out _);
                Require(!impossible, "impossible reach must report failure instead of promising exact contact");
                return $"PASS PlayerVisualMath: IK={solves}, projected rays={projections}, max endpoint={worstJointError:F7}m, max projection={worstPixels:F5}px; impossible reach detected; fixed limb lengths/root preserved.";
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("PlayerVisualMath: " + message);
        }
    }
}
