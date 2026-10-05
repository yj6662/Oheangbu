using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>
    /// #308 D308-8 (SPEC-VEHICLE-UX-308 §6) [TEST]: 탑승 중 플레이어 몸이 좌석에 앉아 보인다(숨기지 않음). 표현 전용.
    ///   · 몸: WorldMacroCombatWalker.SetSeatedBodyVisible308 — Suspend가 기록한 원래 켜짐만 되돌린다. 그림자 모드는 탑승 중 On(내릴 때 원복).
    ///   · 자리: 차 BodyVisualRoot 아래 런타임 앵커 RideHipAnchor308(없으면 SeatSocket + 데이터 오프셋으로 만든다, 저장 안 함)에
    ///     매 LateUpdate 외형 루트를 돌려 앉힌다 — 골반 뼈가 앵커에 오도록 루트를 옮긴다(차 흔들림을 함께 탄다).
    ///   · 자세: 플레이어 컨트롤러에 Ride308 레이어(Vehicle308 ride-layer-apply가 저작)가 있으면 앉은 클립(SeatedClip 칸)을 쓰고,
    ///     없으면 절차적 자세만 쓴다. 둘 다 그 위에 발→차 바닥, 손→앞 손잡이 2뼈 IK와 머리 수평 보정을 얹는다(데이터 끄기 가능).
    ///     손이 닿지 않는 클립이면 팔 IK 전에 척추·가슴을 손잡이 쪽으로 숙인다(측정한 만큼만, 상한 GripReachLean°).
    ///   · 1인칭 좌석 시점(V)에서는 머리 뼈만 거의 0으로 줄인다(눈 카메라가 머리 안을 보지 않게).
    /// 내릴 때(또는 좌석이 이벤트 없이 풀릴 때 — 폴링) 루트 로컬 자세·머리 크기·레이어 무게·그림자 모드를 그대로 되돌린다.
    /// WorldMacroPalanquinSeat의 RuntimeState·입력·카메라 계약은 읽기만 한다. 정적 가변 필드 없음.
    /// </summary>
    [DefaultExecutionOrder(1100), DisallowMultipleComponent]
    public sealed class VehicleRider308 : MonoBehaviour
    {
        public const string AnchorName = "RideHipAnchor308";

        public WorldMacroPalanquinSeat Seat;
        public WorldMacroCombatWalker Walker;
        public VehicleUx308ProfileSO Profile;

        public bool Riding { get; private set; }
        public bool UsingClip { get; private set; }
        public int RideLayerIndex => rideLayer;
        public string LastIssue { get; private set; } = "";
        public int Rides { get; private set; }
        public Transform HipAnchor => hipAnchor;
        public Animator Animator => animator;
        public Transform AppearanceRoot => root;
        public bool HeadHidden => headHidden;
        // read-only pose diagnostics of the last LateUpdate (Vehicle308Checks)
        public float HipError { get; private set; }
        public float LeftKneeBend { get; private set; }
        public float RightKneeBend { get; private set; }
        public float LeftThighPitch { get; private set; }
        public float RightThighPitch { get; private set; }
        public float LeftFootError { get; private set; }
        public float RightFootError { get; private set; }
        public float LeftHandError { get; private set; }
        public float RightHandError { get; private set; }
        /// <summary>Forward lean (degrees, spine + chest) taken this frame so the shoulders reach the grip (0 = none needed).</summary>
        public float GripLean { get; private set; }
        public int VisibleRenderers { get; private set; }
        public int ShadowOnRenderers { get; private set; }

        WorldMacroPlayerAppearance appearance;
        Animator animator;
        Transform root, hipAnchor, hips, spine, chest, head, lUpLeg, lLeg, lFoot, rUpLeg, rLeg, rFoot, lArm, lFore, lHand, rArm, rFore, rHand;
        Vector3 rootLocalPosition, headScale, headForwardLocal;
        Quaternion rootLocalRotation;
        bool rootSaved, headHidden, headKnown, speedParameterPresent, attempted;
        int rideLayer = -1, rideState, speedParameter;
        WorldMacroPalanquinSeat subscribed;
        readonly List<Renderer> shadowRenderers = new List<Renderer>();
        readonly List<ShadowCastingMode> shadowModes = new List<ShadowCastingMode>();

        public void Bind(WorldMacroPalanquinSeat seat, WorldMacroCombatWalker walker, VehicleUx308ProfileSO profile)
        {
            if (Riding) EndRide();
            Unsubscribe();
            Seat = seat; Walker = walker; Profile = profile;
            if (isActiveAndEnabled) Subscribe();
        }
        public void Unbind() { if (Riding) EndRide(); Unsubscribe(); }

        void OnEnable() { Subscribe(); }
        void OnDisable() { if (Riding) EndRide(); Unsubscribe(); }
        void OnDestroy() { if (hipAnchor != null && hipAnchor.name == AnchorName && hipAnchor.gameObject.hideFlags == HideFlags.DontSave) Destroy(hipAnchor.gameObject); }
        void Subscribe() { if (Seat != null && subscribed != Seat) { Unsubscribe(); Seat.Boarded += OnBoarded; Seat.Exited += OnExited; subscribed = Seat; } }
        void Unsubscribe() { if (subscribed != null) { subscribed.Boarded -= OnBoarded; subscribed.Exited -= OnExited; subscribed = null; } }
        // Boarded / Exited fire inside Update (the seat's E path or the session's F path), before the Animator: the seated layer weight
        // is set or cleared on that same frame, so no frame shows the seated pose at the exit spot (LateUpdate polling stays as the fallback).
        void OnBoarded() { attempted = false; if (ShouldRide && !Riding) BeginRide(); }
        void OnExited() { if (Riding) EndRide(); }

        bool ShouldRide => Profile != null && Profile.ShowRider && Seat != null && Walker != null && Walker.Body != null &&
            Seat.Occupied && Walker.Seated && Seat.CombatWalker == Walker && Seat.Vehicle != null && Seat.Vehicle.BodyVisualRoot != null && Seat.SeatSocket != null;

        void Update()
        {
            if (!Riding || animator == null || rideLayer < 0) return;
            animator.SetLayerWeight(rideLayer, 1f);
            if (speedParameterPresent) animator.SetFloat(speedParameter, Profile.SeatedClipSpeed);
        }

        void LateUpdate()
        {
            bool should = ShouldRide;
            if (!should) attempted = false;
            if (should && !Riding && !attempted) BeginRide();
            else if (!should && Riding) { EndRide(); return; }
            if (!Riding) return;
            if (root == null || hips == null || hipAnchor == null) { EndRide(); return; }
            Pose();
        }

        void BeginRide()
        {
            attempted = true; LastIssue = "";
            appearance = Walker.Body.GetComponentInChildren<WorldMacroPlayerAppearance>(true);
            animator = appearance != null ? (appearance.Animator != null ? appearance.Animator : appearance.GetComponent<Animator>()) : null;
            if (animator == null || !animator.isHuman) { LastIssue = "no humanoid player appearance under the walker body"; return; }
            root = animator.transform;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) { LastIssue = "humanoid has no hips"; return; }
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == spine) chest = null;
            lUpLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); lLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg); lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rUpLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg); rLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg); rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            lArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); lFore = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm); lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm); rFore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm); rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            rootLocalPosition = root.localPosition; rootLocalRotation = root.localRotation; rootSaved = true;
            // the model's facing from its hips (standing pose of the boarding frame), so the head can be levelled with no axis guess
            headKnown = false;
            if (head != null && lUpLeg != null && rUpLeg != null)
            {
                Vector3 right = Vector3.ProjectOnPlane(rUpLeg.position - lUpLeg.position, Vector3.up);
                if (right.sqrMagnitude > 1e-6f) { headForwardLocal = Quaternion.Inverse(head.rotation) * Vector3.Cross(right.normalized, Vector3.up); headKnown = true; }
                headScale = head.localScale;
            }
            hipAnchor = EnsureAnchor();
            Walker.SetSeatedBodyVisible308(true);
            shadowRenderers.Clear(); shadowModes.Clear();
            if (Walker.Visuals != null)
                foreach (var r in Walker.Visuals)
                {
                    if (r == null || !r.enabled) continue;
                    shadowRenderers.Add(r); shadowModes.Add(r.shadowCastingMode);
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
            rideLayer = Profile.RideLayer != null ? animator.GetLayerIndex(Profile.RideLayer) : -1;
            // #308 review: accept the full path hash ("Ride308.Seated") as well as the short name hash
            rideState = Animator.StringToHash(Profile.RideState ?? "");
            if (rideLayer >= 0 && !animator.HasState(rideLayer, rideState))
            {
                int full = Animator.StringToHash((Profile.RideLayer ?? "") + "." + (Profile.RideState ?? ""));
                if (animator.HasState(rideLayer, full)) rideState = full;
            }
            UsingClip = rideLayer >= 0 && animator.HasState(rideLayer, rideState);
            if (!UsingClip) rideLayer = -1;
            speedParameterPresent = false;
            if (UsingClip)
            {
                speedParameter = Animator.StringToHash(Profile.RideSpeedParameter ?? "");
                foreach (var p in animator.parameters) if (p.nameHash == speedParameter && p.type == AnimatorControllerParameterType.Float) { speedParameterPresent = true; break; }
                if (speedParameterPresent) animator.SetFloat(speedParameter, Profile.SeatedClipSpeed);
                animator.Play(rideState, rideLayer, Mathf.Clamp01(Profile.SeatedClipTime));
                animator.SetLayerWeight(rideLayer, 1f);
            }
            Riding = true; Rides++;
        }

        Transform EnsureAnchor()
        {
            var body = Seat.Vehicle.BodyVisualRoot;
            var t = body.Find(AnchorName);
            if (t != null && t.gameObject.hideFlags != HideFlags.DontSave) return t;   // authored by hand: used as is
            if (t == null) { var go = new GameObject(AnchorName) { hideFlags = HideFlags.DontSave }; t = go.transform; t.SetParent(body, false); }
            // runtime anchor: placed from the data offset at every boarding (a tuned profile value takes effect on the next ride)
            t.localPosition = body.InverseTransformPoint(Seat.SeatSocket.position + body.rotation * Profile.HipOffsetFromSeatSocket);
            t.localRotation = Quaternion.identity;
            return t;
        }

        void EndRide()
        {
            if (animator != null && rideLayer >= 0) animator.SetLayerWeight(rideLayer, 0f);
            if (head != null && headHidden) head.localScale = headScale;
            headHidden = false;
            if (root != null && rootSaved) { root.localPosition = rootLocalPosition; root.localRotation = rootLocalRotation; }
            rootSaved = false;
            for (int i = 0; i < shadowRenderers.Count; i++) if (shadowRenderers[i] != null) shadowRenderers[i].shadowCastingMode = shadowModes[i];
            shadowRenderers.Clear(); shadowModes.Clear();
            if (Walker != null && Walker.Seated && Walker.SeatedBodyVisible308) Walker.SetSeatedBodyVisible308(false);
            Riding = false; UsingClip = false; rideLayer = -1;
        }

        void Pose()
        {
            var body = Seat.Vehicle.BodyVisualRoot;
            Quaternion car = body.rotation;
            float facing = appearance != null && appearance.Profile != null ? appearance.Profile.FacingYaw : 0f;
            root.rotation = car * Quaternion.Euler(0f, facing, 0f);
            Vector3 anchor = hipAnchor.position;
            root.position += anchor - hips.position;
            HipError = Vector3.Distance(hips.position, anchor);
            Vector3 up = car * Vector3.up;
            if (Profile.FeetToFloor)
            {
                LeftFootError = Limb(lUpLeg, lLeg, lFoot, Target(anchor, car, Profile.FootOffsetFromHip, -1f), lUpLeg != null ? lUpLeg.position + car * new Vector3(-.1f, .4f, 1f) : anchor);
                RightFootError = Limb(rUpLeg, rLeg, rFoot, Target(anchor, car, Profile.FootOffsetFromHip, 1f), rUpLeg != null ? rUpLeg.position + car * new Vector3(.1f, .4f, 1f) : anchor);
            }
            GripLean = 0f;
            if (Profile.HandsToGrip)
            {
                Vector3 leftGrip = Target(anchor, car, Profile.HandOffsetFromHip, -1f), rightGrip = Target(anchor, car, Profile.HandOffsetFromHip, 1f);
                // #308 review (AC-V6 hands .118/.123 m short): the seated clip keeps the shoulders out of arm's reach of the grip,
                // so the two-bone arm stopped short. The torso leans toward the grip first (bounded by data), then the arms solve.
                GripLean = LeanToReach(leftGrip, rightGrip);
                LeftHandError = Limb(lArm, lFore, lHand, leftGrip, lArm != null ? lArm.position + car * new Vector3(-.45f, -.55f, -.25f) : anchor);
                RightHandError = Limb(rArm, rFore, rHand, rightGrip, rArm != null ? rArm.position + car * new Vector3(.45f, -.55f, -.25f) : anchor);
            }
            if (head != null)
            {
                if (headKnown && Profile.HeadLevel > 0f)
                {
                    Vector3 current = head.rotation * headForwardLocal;
                    Vector3 wanted = car * (Quaternion.Euler(Profile.HeadPitchDown, 0f, 0f) * Vector3.forward);
                    head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, wanted), Profile.HeadLevel) * head.rotation;
                }
                bool hide = Profile.HideHeadInSeatView && Seat.SeatedView;
                if (hide && !headHidden) { head.localScale = headScale * .001f; headHidden = true; }
                else if (!hide && headHidden) { head.localScale = headScale; headHidden = false; }
            }
            LeftKneeBend = Bend(lUpLeg, lLeg, lFoot); RightKneeBend = Bend(rUpLeg, rLeg, rFoot);
            LeftThighPitch = Pitch(lUpLeg, lLeg, up); RightThighPitch = Pitch(rUpLeg, rLeg, up);
            int visible = 0, shadowed = 0;
            if (Walker.Visuals != null) foreach (var r in Walker.Visuals) if (r != null && r.enabled) { visible++; if (r.shadowCastingMode == ShadowCastingMode.On) shadowed++; }
            VisibleRenderers = visible; ShadowOnRenderers = shadowed;
        }

        // Leans spine (+ chest) toward the grip until both upper-arm roots are within GripReachShare x arm length of their grip target,
        // at most GripReachLean degrees. The lean is measured, not assumed: an already reachable grip leans 0°. The Animator rewrites
        // the spine every frame (the seated clip), so nothing accumulates and nothing needs restoring on exit. Returns the degrees.
        float LeanToReach(Vector3 leftGrip, Vector3 rightGrip)
        {
            float max = Profile.GripReachLean;
            if (max <= 0f || spine == null || lArm == null || lFore == null || lHand == null || rArm == null || rFore == null || rHand == null) return 0f;
            float share = Mathf.Clamp(Profile.GripReachShare, .8f, 1f);
            float leftReach = (Vector3.Distance(lArm.position, lFore.position) + Vector3.Distance(lFore.position, lHand.position)) * share;
            float rightReach = (Vector3.Distance(rArm.position, rFore.position) + Vector3.Distance(rFore.position, rHand.position)) * share;
            float total = 0f;
            for (int i = 0; i < 8 && total < max - .01f; i++)
            {
                float excess = Mathf.Max(Vector3.Distance(lArm.position, leftGrip) - leftReach, Vector3.Distance(rArm.position, rightGrip) - rightReach);
                if (excess <= .002f) break;
                Vector3 pivot = spine.position;
                Vector3 from = (lArm.position + rArm.position) * .5f - pivot, to = (leftGrip + rightGrip) * .5f - pivot;
                Vector3 axis = Vector3.Cross(from, to);
                float radius = from.magnitude;
                if (axis.sqrMagnitude < 1e-8f || radius < .05f) break;
                axis.Normalize();
                // arc length ≈ the missing reach; never past pointing the shoulders straight at the grip
                float step = Mathf.Min(Mathf.Min(excess / radius * Mathf.Rad2Deg, max - total), Vector3.Angle(from, to));
                if (step <= .01f) break;
                if (chest != null)
                {
                    spine.rotation = Quaternion.AngleAxis(step * .5f, axis) * spine.rotation;
                    chest.rotation = Quaternion.AngleAxis(step * .5f, axis) * chest.rotation;
                }
                else spine.rotation = Quaternion.AngleAxis(step, axis) * spine.rotation;
                total += step;
            }
            return total;
        }

        static Vector3 Target(Vector3 hip, Quaternion car, Vector3 offset, float side) => hip + car * new Vector3(offset.x * side, offset.y, offset.z);

        // Two-bone analytic IK: a (upper) -> b (lower) -> c (end). The middle joint bends toward `pole`. Returns the end error (m).
        static float Limb(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            if (a == null || b == null || c == null) return -1f;
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float la = Vector3.Distance(pa, pb), lb = Vector3.Distance(pb, pc);
            Vector3 toTarget = target - pa; float d = toTarget.magnitude;
            if (la < 1e-4f || lb < 1e-4f || d < 1e-4f) return Vector3.Distance(pc, target);
            Vector3 dir = toTarget / d;
            d = Mathf.Clamp(d, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            Vector3 bendSide = Vector3.ProjectOnPlane(pole - pa, dir);
            if (bendSide.sqrMagnitude < 1e-6f) bendSide = Vector3.ProjectOnPlane(pb - pa, dir);
            if (bendSide.sqrMagnitude < 1e-6f) return Vector3.Distance(pc, target);
            // rotating dir about (dir x bendSide) by a positive angle moves it toward bendSide
            Vector3 axis = Vector3.Cross(dir, bendSide.normalized).normalized;
            Vector3 joint = pa + (Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, axis) * dir) * la;
            a.rotation = Quaternion.FromToRotation(pb - pa, joint - pa) * a.rotation;
            pb = b.position; pc = c.position;
            b.rotation = Quaternion.FromToRotation(pc - pb, pa + dir * d - pb) * b.rotation;
            return Vector3.Distance(c.position, target);
        }

        static float Bend(Transform a, Transform b, Transform c) => a == null || b == null || c == null ? -1f : Vector3.Angle(b.position - a.position, c.position - b.position);
        static float Pitch(Transform a, Transform b, Vector3 up) => a == null || b == null ? 0f : Mathf.Asin(Mathf.Clamp(Vector3.Dot((b.position - a.position).normalized, up), -1f, 1f)) * Mathf.Rad2Deg;
    }
}
