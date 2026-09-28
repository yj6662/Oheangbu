using Oheangbu.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>Local macro-only input/camera ownership. Does not replace the shared PlayerRig.</summary>
    [DefaultExecutionOrder(150), DisallowMultipleComponent]
    public sealed class WorldMacroPalanquinSeat : MonoBehaviour
    {
        public WorldMacroPalanquinController Vehicle;
        public WorldMacroReviewController ReviewController;
        public WorldMacroCombatWalker CombatWalker;
        CharacterController WalkBody => CombatWalker != null ? CombatWalker.Body : ReviewController.WalkBody;
        float EyeHeight => CombatWalker != null ? CombatWalker.EyeHeight : ReviewController.EyeHeight;
        public Camera ViewCamera;
        [Tooltip("Seated eye position, forward +Z. Usually a child of BodyVisualRoot.")]
        public Transform SeatSocket;
        [Tooltip("Optional door-side ground candidates. Must lie outside the vehicle hull and wheels.")]
        public Transform[] ExitSockets;
        public Transform ExternalLookSocket;
        public bool StartInSeatedView = true;
        public GameplayRuntimeStateSO RuntimeState;

        public System.Func<bool> BoardingAllowed;
        public System.Func<bool> ConfirmBoarding;
        public event System.Action Boarded;
        public event System.Action Exited;
        public bool Occupied { get; private set; }
        public bool SeatedView { get; private set; }
        public string LastInteraction { get; private set; }
        public string InteractionHint => Occupied ? "WASD 주행 · Space 제동 · V 시점 · E 하차" : CanBoard ? "E 가마 탑승" : string.Empty;
        public bool CanBoard => ReferencesReady && !Occupied && CompactMountainAccess.VehicleAllowed(gameObject.scene,WalkBody.transform.position) && CompactMountainAccess.VehicleAllowed(gameObject.scene,Vehicle.transform.position,Vehicle.Hull.bounds.extents.magnitude) && (BoardingAllowed == null || BoardingAllowed()) && (CombatWalker != null ? CombatWalker.CanBoard : ReviewController.enabled && ReviewController.Mode == 1 && WalkBody.enabled) && Vehicle.Speed <= Profile.BoardingMaximumSpeed &&
            Vector3.Distance(WalkBody.bounds.center, SeatSocket.position) <= Profile.BoardingDistance;

        readonly RaycastHit[] rayHits = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[32];
        Vector3 entryFeet, cameraVelocity;
        float lookYaw, lookPitch, entryYaw;
        bool reviewWasEnabled, walkVisualWasEnabled, applicationFocused = true;
        WorldMacroPalanquinProfileSO Profile => Vehicle.Profile;
        bool ReferencesReady => Vehicle != null && Vehicle.isActiveAndEnabled && Vehicle.IsConfigured && Vehicle.Profile != null &&
            (CombatWalker != null || ReviewController != null) && WalkBody != null && ViewCamera != null && SeatSocket != null &&
            (CombatWalker != null ? ViewCamera == CombatWalker.ViewCamera : ViewCamera.transform == ReviewController.transform) && SeatSocket.IsChildOf(Vehicle.transform);

        void Awake()
        {
            if (ViewCamera == null && ReviewController != null) ViewCamera = ReviewController.GetComponent<Camera>();
            SeatedView = StartInSeatedView;
        }
        void Update()
        {
            if (!ReferencesReady) { if (Occupied) ReleaseOwnership(entryFeet, entryYaw); return; }
            if (CombatWalker!=null&&(CombatWalker.Motor.EnvironmentalInputBlocked||CombatWalker.Body.GetComponent<Oheangbu.Combat.PlayerVitals>().Hp01<=0)||RuntimeState != null && RuntimeState.InputBlocked)
            {
                if (Occupied) Vehicle.StopDriverInputForUi();
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null || !applicationFocused) { if (Occupied) Vehicle.SetDriverInput(0, 0, true); return; }
            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (Occupied) TryExit(); else TryBoard();
            }
            if (!Occupied) return;
            if (keyboard.vKey.wasPressedThisFrame) ToggleView();
            float acceleration = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            float turn = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            Vehicle.SetDriverInput(acceleration, turn, keyboard.spaceKey.isPressed);
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                var delta = Mouse.current.delta.ReadValue();
                float sensitivity = RuntimeState != null ? RuntimeState.LookSensitivity : 1f;
                lookYaw += delta.x * Profile.MouseSensitivity * sensitivity;
                if (SeatedView) lookYaw = Mathf.Clamp(lookYaw, -Profile.SeatYawLimit, Profile.SeatYawLimit);
                else lookYaw = Mathf.Repeat(lookYaw + 180, 360) - 180;
                float vertical = RuntimeState != null && RuntimeState.InvertLookY ? -delta.y : delta.y;
                lookPitch = Mathf.Clamp(lookPitch - vertical * Profile.MouseSensitivity * sensitivity, Profile.PitchLimits.x, Profile.PitchLimits.y);
            }
        }

        public bool TryBoard()
        {
            if (!Application.isPlaying || !CanBoard) { LastInteraction = "보행 상태에서 가마 가까이 접근해 정차한다."; return false; }
            Vector3 eye = WalkBody.transform.position + Vector3.up * EyeHeight;
            if (Obstructed(eye, SeatSocket.position)) { LastInteraction = "가마까지의 접근이 가려져 있다."; return false; }
            if (CombatWalker != null && !CombatWalker.Motor.PrepareForBoarding()) return false;
            entryFeet = WalkBody.transform.position; entryYaw = WalkBody.transform.eulerAngles.y;
            if (CombatWalker != null) CombatWalker.Suspend();
            else {
            reviewWasEnabled = ReviewController.enabled;
            walkVisualWasEnabled = ReviewController.WalkVisual != null && ReviewController.WalkVisual.enabled;
            ReviewController.enabled = false; // Its own OnDisable releases the walking CharacterController.
            WalkBody.enabled = false;
            if (ReviewController.WalkVisual != null) ReviewController.WalkVisual.enabled = false;
            }
            Occupied = true; SeatedView = StartInSeatedView; lookYaw = lookPitch = 0; cameraVelocity = Vector3.zero;
            Vehicle.SetDriverPresent(true); Vehicle.SetDriverInput(0, 0, true);
            ApplyCamera(true);
            try { if (ConfirmBoarding != null && !ConfirmBoarding()) { ReleaseOwnership(entryFeet, entryYaw); LastInteraction = "동행과 화물을 확인하고 다시 탑승한다."; return false; } }
            catch { ReleaseOwnership(entryFeet, entryYaw); throw; }
            LastInteraction = "가마에 탑승했다."; Boarded?.Invoke(); return true;
        }

        public bool TryExit()
        {
            if (!Application.isPlaying || !Occupied || RuntimeState != null && RuntimeState.InputBlocked) return false;
            if (!ReferencesReady) { LastInteraction = "탑승 연결을 확인해야 한다."; return false; }
            if (Vehicle.Speed > Profile.ExitMaximumSpeed) { LastInteraction = "정차한 뒤 하차할 수 있다."; return false; }
            if (!TryFindExit(out Vector3 feet)) { LastInteraction = "주변에 안전하게 설 공간이 없다."; return false; }
            ReleaseOwnership(feet, Vehicle.transform.eulerAngles.y); LastInteraction = "가마에서 내렸다."; Exited?.Invoke(); return true;
        }
        public void ForceExitForRecovery(Vector3 safeFeet,float yaw)
        {if(!Occupied)return;ReleaseOwnership(safeFeet,yaw);Exited?.Invoke();}
        public bool ToggleView()
        {
            if(!Application.isPlaying||!Occupied||!ReferencesReady)return false;
            SeatedView=!SeatedView;lookYaw=Mathf.Clamp(lookYaw,-Profile.SeatYawLimit,Profile.SeatYawLimit);
            cameraVelocity=Vector3.zero;ApplyCamera(true);return true;
        }
        public bool RefreshCamera(bool snap=true)
        {if(!Application.isPlaying||!Occupied||!ReferencesReady)return false;ApplyCamera(snap);return true;}

        public bool TryGetSafeExit(out Vector3 feet)
        { feet = default; return ReferencesReady && TryFindExit(out feet); }

        bool TryFindExit(out Vector3 feet)
        {
            if (ExitSockets != null) foreach (var socket in ExitSockets)
                if (socket != null && ExitGround(socket.position, out feet)) return true;
            Vector3 origin = Vehicle.transform.position;
            Vector3 right = Vector3.ProjectOnPlane(Vehicle.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(Vehicle.transform.forward, Vector3.up).normalized;
            if (ExitGround(origin + right * Profile.ExitSideDistance, out feet) ||
                ExitGround(origin - right * Profile.ExitSideDistance, out feet) ||
                ExitGround(origin - forward * Profile.ExitEndDistance, out feet) ||
                ExitGround(origin + forward * Profile.ExitEndDistance, out feet)) return true;
            feet = default; return false;
        }

        bool ExitGround(Vector3 candidate, out Vector3 feet)
        {
            feet = default; var walk = WalkBody;
            int count = Physics.RaycastNonAlloc(candidate + Vector3.up * Profile.ExitGroundProbeUp, Vector3.down,
                rayHits, Profile.ExitGroundProbeUp + Profile.ExitGroundProbeDown, Profile.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) return false;
            float distance = float.PositiveInfinity; RaycastHit chosen = default;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (IgnoredViewCollider(hit.collider) || Vector3.Angle(hit.normal, Vector3.up) > walk.slopeLimit) continue;
                if (hit.distance < distance) { chosen = hit; distance = hit.distance; }
            }
            if (float.IsPositiveInfinity(distance)) return false;
            var traversal=CombatWalker!=null?CombatWalker.GetComponentInParent<WorldMacroPlaytestSession>()?.Traversal:null;
            if(traversal==null&&CombatWalker!=null)traversal=Object.FindFirstObjectByType<WorldTerrainQuery>();
            if(traversal!=null&&!traversal.IsPermanentDrySupport(chosen.point,chosen.collider))return false;
            feet = chosen.point + Vector3.up * (walk.skinWidth + Profile.ExitClearance + .02f + walk.height * .5f - walk.center.y);
            float radius = walk.radius + Profile.ExitClearance;
            float half = Mathf.Max(0, walk.height * .5f - walk.radius);
            Vector3 centre = feet + Quaternion.Euler(0, Vehicle.transform.eulerAngles.y, 0) * walk.center;
            int found = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * half, centre - Vector3.up * half,
                radius, overlaps, Profile.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (found == overlaps.Length) return false;
            // The vehicle itself remains an obstacle: never eject the walking capsule into a wheel or hull.
            for (int i = 0; i < found; i++) if (overlaps[i] != walk && overlaps[i] != null && overlaps[i].enabled) return false;
            return true;
        }

        bool Obstructed(Vector3 from, Vector3 to)
        {
            var delta = to - from; float length = delta.magnitude;
            if (length < .001f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / length, rayHits, length, Profile.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) return true;
            for (int i = 0; i < count; i++) if (!IgnoredViewCollider(rayHits[i].collider)) return true;
            return false;
        }
        bool IgnoredViewCollider(Collider collider) => collider == null || collider == WalkBody || collider.transform.IsChildOf(Vehicle.transform);

        void LateUpdate()
        {
            if (!Occupied || !ReferencesReady) return;
            // Keep the disabled walk body near its owner; it neither collides nor renders while seated.
            WalkBody.transform.position = SeatSocket.position - Vector3.up * EyeHeight;
            ApplyCamera(false);
        }
        void ApplyCamera(bool snap)
        {
            if (!snap && Time.deltaTime <= 0) return;
            Vector3 targetPosition; Quaternion targetRotation;
            if (SeatedView)
            {
                // The socket's authored viewing angle is not vehicle sway. Preserve it in full,
                // then attenuate only the physics/visual body's pitch and roll around that angle.
                Quaternion bodyRotation = Vehicle.BodyVisualRoot.rotation;
                Quaternion authoredSeatRotation = Quaternion.Inverse(bodyRotation) * SeatSocket.rotation;
                Quaternion levelBody = Quaternion.Euler(0, Vehicle.transform.eulerAngles.y, 0);
                float tiltFollow = RuntimeState != null && RuntimeState.ReducedMotion ? 0f : Mathf.Clamp01(Profile.SeatTiltFollow);
                var basis = Quaternion.Slerp(levelBody, bodyRotation, tiltFollow) * authoredSeatRotation;
                targetPosition = SeatSocket.position; targetRotation = basis * Quaternion.Euler(lookPitch, lookYaw, 0);
            }
            else
            {
                Vector3 focus = (ExternalLookSocket != null ? ExternalLookSocket.position : SeatSocket.position) + Vector3.up * Profile.ExternalPivotHeight;
                var orbit = Quaternion.Euler(lookPitch, Vehicle.transform.eulerAngles.y + lookYaw, 0);
                targetPosition = ClampExteriorCamera(focus, focus + orbit * new Vector3(Profile.ExternalShoulderOffset,0,-Profile.ExternalDistance));
                Vector3 lookAt=focus+orbit*Vector3.forward*(Mathf.Abs(Profile.ExternalShoulderOffset)>.001f?2:0);
                targetRotation = Quaternion.LookRotation(lookAt - targetPosition, Vector3.up);
            }
            if (snap) ViewCamera.transform.SetPositionAndRotation(targetPosition, targetRotation);
            else
            {
                Vector3 position = Vector3.SmoothDamp(ViewCamera.transform.position, targetPosition, ref cameraVelocity, Mathf.Max(.01f, Profile.CameraResponse), Mathf.Infinity, Time.deltaTime);
                if (!SeatedView)
                {
                    Vector3 focus = (ExternalLookSocket != null ? ExternalLookSocket.position : SeatSocket.position) + Vector3.up * Profile.ExternalPivotHeight;
                    position = ClampExteriorCamera(focus, position);
                }
                ViewCamera.transform.position = position;
                float amount = 1 - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, Profile.CameraResponse));
                ViewCamera.transform.rotation = Quaternion.Slerp(ViewCamera.transform.rotation, targetRotation, amount);
            }
        }

        Vector3 ClampExteriorCamera(Vector3 focus, Vector3 candidate)
        {
            Vector3 delta = candidate - focus; float distance = delta.magnitude;
            if (distance <= .001f) return focus + Vehicle.transform.forward * -Profile.CameraMinimumDistance;
            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(focus, Profile.CameraCollisionRadius, direction, rayHits, distance, Profile.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) distance = Mathf.Min(distance, Profile.CameraMinimumDistance);
            else for (int i = 0; i < count; i++) if (!IgnoredViewCollider(rayHits[i].collider)) distance = Mathf.Min(distance, Mathf.Max(.05f, rayHits[i].distance - Profile.CameraCollisionRadius));
            return focus + direction * distance;
        }

        void ReleaseOwnership(Vector3 feet, float yaw)
        {
            if (!Occupied) return;
            Occupied = false;
            if (Vehicle != null) Vehicle.SetDriverPresent(false);
            if (CombatWalker != null) { CombatWalker.Resume(feet, yaw); return; }
            if (ReviewController == null) return;
            var body = WalkBody;
            if (body != null) { body.enabled = false; body.transform.SetPositionAndRotation(feet, Quaternion.Euler(0, yaw, 0)); }
            if (ReviewController.WalkVisual != null) ReviewController.WalkVisual.enabled = walkVisualWasEnabled;
            ReviewController.enabled = reviewWasEnabled;
            ReviewController.ResumeWalkAt(feet,yaw);
        }
        void OnApplicationFocus(bool focus) { applicationFocused = focus; if (!focus && Occupied && Vehicle != null) Vehicle.SetDriverInput(0, 0, true); }
        void OnDisable()
        {
            if (!Occupied) return;
            Vector3 feet = entryFeet; float yaw = entryYaw;
            if (ReferencesReady && TryFindExit(out var exit)) { feet = exit; yaw = Vehicle.transform.eulerAngles.y; }
            ReleaseOwnership(feet, yaw);
        }
    }
}
