using Oheangbu.App.World.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World
{
    /// <summary>Candidate-only, text-free doorway rest. Starts only after the durable rest transaction.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldMacroInnRestPresentation : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public Transform Door;
        public Vector3 HingeWorld;
        public string PointId = "geumpyo_inn";
        public float OpenDegrees = -24f;
        public float ReturnYaw;
        public bool IsActive { get; private set; }
        public int CompletedCount { get; private set; }
        public float Elapsed { get; private set; }
        public float FadeAlpha => veil != null ? veil.color.a : 0;
        public bool CanPresent(string id) => isActiveAndEnabled && id == PointId && Door != null
            && Session != null && PlaytestUiRoot.Instance != null && !PlaytestUiRoot.Instance.IsMenuOpen;

        Image veil;
        GameplayUiGate gate;
        Vector3 doorPosition, returnFeet;
        Quaternion doorRotation, playerRotation, facingDoor;
        float returnYaw;
        bool returned, ownsGate;

        public void BeginCommittedRest(Vector3 feet, float yaw)
        {
            if (IsActive || !CanPresent(PointId)) return;
            gate = PlaytestUiRoot.Instance.Gate;
            doorPosition = Door.position; doorRotation = Door.rotation;
            returnFeet = feet; returnYaw = yaw; returned = false;
            playerRotation = Session.Walker.Body.transform.rotation;
            var target = Door.GetComponent<Renderer>().bounds.center - Session.Walker.Body.transform.position;
            target.y = 0;
            facingDoor = target.sqrMagnitude > .01f ? Quaternion.LookRotation(target) : playerRotation;
            EnsureVeil();
            Elapsed = 0; IsActive = true;
            gate.Block(); ownsGate = true;
            Session.Walker.Motor.ResetMotion();
        }

        void EnsureVeil()
        {
            if (veil != null) return;
            var root = new GameObject("InnRestTransition", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;
            var image = new GameObject("Shade", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(root.transform, false);
            var rect = (RectTransform)image.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            veil = image.GetComponent<Image>(); veil.raycastTarget = false;
            veil.color = new Color(.035f, .029f, .023f, 0);
        }

        void Update()
        {
            if (!IsActive) return;
            // Focus loss must not silently consume the transition while the player cannot see it.
            if (!Application.isFocused) return;
            Elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            float turn = Mathf.SmoothStep(0, 1, Elapsed / .55f);
            if (!returned) Session.Walker.Body.transform.rotation = Quaternion.Slerp(playerRotation, facingDoor, turn);
            float open = Mathf.SmoothStep(0, 1, (Elapsed - .3f) / .65f);
            if (Elapsed > 1.55f) open *= 1 - Mathf.SmoothStep(0, 1, (Elapsed - 1.55f) / .4f);
            var rotation = Quaternion.AngleAxis(OpenDegrees * open, Vector3.up);
            Door.SetPositionAndRotation(HingeWorld + rotation * (doorPosition - HingeWorld), rotation * doorRotation);
            float alpha = Elapsed < 1.1f ? Mathf.SmoothStep(0, 1, (Elapsed - .45f) / .65f)
                : Elapsed < 1.95f ? 1 : 1 - Mathf.SmoothStep(0, 1, (Elapsed - 1.95f) / .85f);
            veil.color = new Color(.035f, .029f, .023f, alpha);
            if (!returned && Elapsed >= 1.35f)
            {
                Session.Teleport(returnFeet, returnYaw);
                returned = true;
            }
            if (Elapsed >= 2.8f) { CompletedCount++; Finish(); }
        }

        void Finish()
        {
            if (Door != null && IsActive) Door.SetPositionAndRotation(doorPosition, doorRotation);
            if (veil != null) veil.color = new Color(.035f, .029f, .023f, 0);
            IsActive = false;
            // Menu input is held off during the transition; focus and held-key release remain gate-owned.
            if (ownsGate && gate != null) gate.ReleaseWhenNeutral();
            ownsGate = false;
        }

        void OnDisable() { Finish(); }
    }
}
