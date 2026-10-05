using System;
using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>Four-wheel TEST physics and rigid visual parts. No input, route AI, or gameplay rules.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class WorldMacroPalanquinController : MonoBehaviour
    {
        [Serializable]
        public sealed class WheelBinding
        {
            public WheelCollider Collider;
            [Tooltip("Separate visual; its axle is X. Never use the WheelCollider transform itself.")]
            public Transform Visual;
            public Vector3 VisualRotationOffset;
        }

        public WorldMacroPalanquinProfileSO Profile;
        public Rigidbody Body;
        public BoxCollider Hull;
        [Tooltip("Direct child of the physics root. Contains rigid body parts and SeatSocket, but no colliders or wheels.")]
        public Transform BodyVisualRoot;
        [Tooltip("Exactly four: front left, front right, rear left, rear right. +Z is front.")]
        public WheelBinding[] Wheels = new WheelBinding[4];
        [Tooltip("Optional rigid X-axis visual shafts outside body sway. Scale/length is authored once, never stretched in play.")]
        public Transform FrontAxleVisual, RearAxleVisual;

        public bool DriverPresent { get; private set; }
        public bool IsConfigured { get; private set; }
        public int GroundedWheelCount { get; private set; }
        public float Speed => Body == null ? 0 : Body.linearVelocity.magnitude;
        public float ForwardSpeed => Body == null ? 0 : Vector3.Dot(Body.linearVelocity, transform.forward);
        public string ConfigurationIssue { get; private set; }
        public float RequestedThrottle => throttleInput;
        public float AppliedThrottle => throttle;
        public float AppliedMotorTorque { get; private set; }
        public bool Braking => brakeInput || brake > .1f;
        readonly WheelHit[] contacts = new WheelHit[4];
        readonly bool[] grounded = new bool[4];
        readonly float[] travel = new float[4];
        float throttleInput, steerInput, throttle, brake, steering, wheelbase, track;
        bool brakeInput;
        Vector3 visualRestPosition, visualOffsetVelocity, visualEuler, visualEulerVelocity;
        Vector3 priorVelocity, localAcceleration;
        Quaternion visualRestRotation;

        void Awake() { ApplyConfiguration(); }
        void OnEnable() { if (Body != null) priorVelocity = Body.linearVelocity; }
        void OnDisable() { SetDriverPresent(false); ApplyStoppedTorques(); RestoreVisual(); }

        public bool ApplyConfiguration()
        {
            RestoreVisual();
            Body = Body != null ? Body : GetComponent<Rigidbody>();
            Hull = Hull != null ? Hull : GetComponent<BoxCollider>();
            IsConfigured = ValidateConfiguration(out string issue); ConfigurationIssue = issue;
            if (!IsConfigured) return false;
            Body.mass = Mathf.Max(100, Profile.Mass); Body.centerOfMass = Profile.CentreOfMass;
            Body.useGravity = true; Body.isKinematic = false;
            Body.linearDamping = Mathf.Max(0, Profile.LinearDamping); Body.angularDamping = Mathf.Max(0, Profile.AngularDamping);
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.solverIterations = Mathf.Max(1, Profile.SolverIterations);
            Body.solverVelocityIterations = Mathf.Max(1, Profile.SolverVelocityIterations);
            Hull.center = Profile.HullCentre; Hull.size = new Vector3(Mathf.Max(.1f, Profile.HullSize.x), Mathf.Max(.1f, Profile.HullSize.y), Mathf.Max(.1f, Profile.HullSize.z));
            Hull.isTrigger = false;
            foreach (var binding in Wheels)
            {
                var wheel = binding.Collider;
                wheel.radius = Mathf.Max(.1f, Profile.WheelRadius); wheel.mass = Mathf.Max(1, Profile.WheelMass);
                wheel.suspensionDistance = Mathf.Max(.01f, Profile.SuspensionDistance);
                wheel.forceAppPointDistance = Mathf.Max(0, Profile.ForceApplicationHeight);
                wheel.wheelDampingRate = Mathf.Max(0, Profile.WheelDamping);
                wheel.suspensionSpring = new JointSpring { spring = Mathf.Max(100, Profile.SuspensionSpring), damper = Mathf.Max(0, Profile.SuspensionDamper), targetPosition = Mathf.Clamp01(Profile.SuspensionTarget) };
                var forward = wheel.forwardFriction; forward.stiffness = Mathf.Max(.1f, Profile.ForwardFriction); wheel.forwardFriction = forward;
                var sideways = wheel.sidewaysFriction; sideways.stiffness = Mathf.Max(.1f, Profile.SidewaysFriction); wheel.sidewaysFriction = sideways;
                wheel.ConfigureVehicleSubsteps(5, 5, 3);
            }
            Vector3 fl = transform.InverseTransformPoint(Wheels[0].Collider.transform.position), fr = transform.InverseTransformPoint(Wheels[1].Collider.transform.position);
            Vector3 rl = transform.InverseTransformPoint(Wheels[2].Collider.transform.position), rr = transform.InverseTransformPoint(Wheels[3].Collider.transform.position);
            wheelbase = ((fl.z + fr.z) - (rl.z + rr.z)) * .5f;
            track = (Mathf.Abs(fr.x - fl.x) + Mathf.Abs(rr.x - rl.x)) * .5f;
            visualRestPosition = BodyVisualRoot.localPosition; visualRestRotation = BodyVisualRoot.localRotation;
            priorVelocity = Body.linearVelocity; localAcceleration = visualEuler = visualEulerVelocity = visualOffsetVelocity = Vector3.zero;
            ApplyStoppedTorques(); return true;
        }

        public bool ValidateConfiguration(out string issue)
        {
            issue = null;
            if (Profile == null || Body == null || Hull == null || BodyVisualRoot == null) issue = "Assign profile, root Rigidbody/BoxCollider and body visual root.";
            else if (Body.transform != transform || Hull.transform != transform || BodyVisualRoot.parent != transform) issue = "Physics root must own the body/hull and directly parent the visual body.";
            else if ((transform.lossyScale - Vector3.one).sqrMagnitude > .0001f) issue = "Vehicle physics root and ancestors must have unit scale.";
            else if (BodyVisualRoot.GetComponentInChildren<Collider>(true) != null) issue = "Visual body must not contain colliders; sway is presentation only.";
            else if (Wheels == null || Wheels.Length != 4) issue = "Four bindings required: FL, FR, RL, RR.";
            else for (int i = 0; i < 4; i++)
            {
                var binding = Wheels[i];
                if (binding == null || binding.Collider == null || binding.Visual == null) { issue = "Wheel binding missing at index " + i; break; }
                var wheel = binding.Collider;
                if ((wheel.attachedRigidbody != Body && (gameObject.activeInHierarchy || wheel.GetComponentInParent<Rigidbody>(true) != Body)) || !wheel.transform.IsChildOf(transform) || wheel.transform.IsChildOf(BodyVisualRoot)) { issue = "Each wheel collider must belong to this physics body, outside visual sway."; break; }
                if ((wheel.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f || Quaternion.Angle(wheel.transform.rotation, transform.rotation) > .1f) { issue = "WheelCollider axes must match the unit-scale vehicle root."; break; }
                if (!binding.Visual.IsChildOf(transform) || binding.Visual.IsChildOf(BodyVisualRoot) || binding.Visual == wheel.transform || wheel.transform.IsChildOf(binding.Visual) || binding.Visual.GetComponentInChildren<Collider>(true) != null) { issue = "Wheel visuals must be separate rigid parts outside body sway and wheel colliders."; break; }
                for (int j = 0; j < i; j++) if (Wheels[j].Collider == wheel || Wheels[j].Visual == binding.Visual) { issue = "Duplicate wheel binding."; break; }
                if (issue != null) break;
            }
            if (issue != null) return false;
            if (!ValidAxleVisual(FrontAxleVisual) || !ValidAxleVisual(RearAxleVisual) || (FrontAxleVisual != null && FrontAxleVisual == RearAxleVisual))
            { issue = "Axle visuals must be distinct collider-free children outside body sway."; return false; }
            var a = transform.InverseTransformPoint(Wheels[0].Collider.transform.position); var b = transform.InverseTransformPoint(Wheels[1].Collider.transform.position);
            var c = transform.InverseTransformPoint(Wheels[2].Collider.transform.position); var d = transform.InverseTransformPoint(Wheels[3].Collider.transform.position);
            if (a.x >= b.x || c.x >= d.x || Mathf.Min(a.z, b.z) <= Mathf.Max(c.z, d.z) + .2f) issue = "Wheel positions must follow FL, FR, RL, RR with front along +Z.";
            return issue == null;
        }
        bool ValidAxleVisual(Transform axle) => axle == null || (axle != transform && axle.IsChildOf(transform) && !axle.IsChildOf(BodyVisualRoot) && axle.GetComponentInChildren<Collider>(true) == null);

        public void SetDriverPresent(bool present)
        {
            DriverPresent = present && IsConfigured && isActiveAndEnabled;
            if (!DriverPresent) { throttleInput = steerInput = throttle = 0; brakeInput = true; brake = 1; }
        }
        public void SetDriverInput(float acceleration, float turn, bool braking)
        {
            throttleInput = DriverPresent ? Mathf.Clamp(acceleration, -1, 1) : 0;
            steerInput = DriverPresent ? Mathf.Clamp(turn, -1, 1) : 0; brakeInput = braking || !DriverPresent;
        }
        /// <summary>Immediately removes queued driver motion while retaining seat ownership.</summary>
        public void StopDriverInputForUi()
        {
            throttleInput = steerInput = throttle = steering = 0f;
            brakeInput = true;
            brake = 1f;
            if (!IsConfigured) return;
            ApplySteering();
            ApplyStoppedTorques();
        }
        public bool GetWheelContact(int index, out WheelHit hit)
        { hit = default; if (index < 0 || index >= 4) return false; hit = contacts[index]; return grounded[index]; }

        public void ResetMountainTraversalPose(){hasMountainPose=false;}
        Vector3 lastAllowedMountainPose; Quaternion lastAllowedMountainRotation; bool hasMountainPose;
        void FixedUpdate()
        {
            if (!IsConfigured) return;
            float footprint=Hull.bounds.extents.magnitude;
            if(!CompactMountainAccess.VehicleAllowed(gameObject.scene,Body.position,footprint)||hasMountainPose&&!CompactMountainAccess.VehicleSegmentAllowed(gameObject.scene,lastAllowedMountainPose,Body.position,footprint))
            {
                StopDriverInputForUi();Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;
                if(hasMountainPose){Body.position=lastAllowedMountainPose;Body.rotation=lastAllowedMountainRotation;}
                return;
            }
            lastAllowedMountainPose=Body.position;lastAllowedMountainRotation=Body.rotation;hasMountainPose=true;
            var approaching=Body.position+Body.linearVelocity*Mathf.Max(.4f,Speed/8f);
            if(!CompactMountainAccess.VehicleSegmentAllowed(gameObject.scene,Body.position,approaching,footprint))StopDriverInputForUi();
            float dt = Time.fixedDeltaTime, forwardSpeed = ForwardSpeed;
            throttle = Mathf.MoveTowards(throttle, throttleInput, Mathf.Max(.1f, Profile.ThrottleResponse) * dt);
            bool changingDirection = Mathf.Abs(forwardSpeed) > Profile.DirectionChangeSpeed && throttleInput * forwardSpeed < -.05f;
            float desiredBrake = brakeInput || changingDirection ? 1 : 0;
            brake = Mathf.MoveTowards(brake, desiredBrake, Mathf.Max(.1f, Profile.BrakeResponse) * dt);
            float speedLimit = throttle >= 0 ? Mathf.Max(1, Profile.ForwardSpeed) : Mathf.Max(.1f, Profile.ReverseSpeed);
            float overspeed = Mathf.Clamp01((Mathf.Abs(forwardSpeed) - speedLimit) / Mathf.Max(1, speedLimit * .2f));
            float torqueFade = 1 - Mathf.InverseLerp(speedLimit * .85f, speedLimit, Mathf.Abs(forwardSpeed));
            float torque = DriverPresent && !changingDirection && !brakeInput ? throttle * Mathf.Max(0, Profile.MotorTorque) * torqueFade * (1 - brake) : 0;
            AppliedMotorTorque=torque;
            float steerLimit = Mathf.Lerp(Profile.LowSpeedSteerAngle, Profile.HighSpeedSteerAngle, Mathf.Clamp01(Speed / Mathf.Max(1, Profile.ForwardSpeed)));
            steering = Mathf.MoveTowards(steering, steerInput * steerLimit, Mathf.Max(1, Profile.SteerDegreesPerSecond) * dt);
            ApplySteering(); GroundedWheelCount = 0;
            for (int i = 0; i < 4; i++)
            {
                var wheel = Wheels[i].Collider;
                float share = i < 2 ? Mathf.Clamp01(Profile.FrontDriveShare) : 1 - Mathf.Clamp01(Profile.FrontDriveShare);
                wheel.motorTorque = torque * share * .5f;
                wheel.brakeTorque = DriverPresent ? Mathf.Max(brake, overspeed) * Profile.BrakeTorquePerWheel : Profile.ParkingBrakeTorquePerWheel;
                grounded[i] = wheel.GetGroundHit(out contacts[i]); travel[i] = 1;
                if (!grounded[i]) continue;
                GroundedWheelCount++;
                travel[i] = Mathf.Clamp01((-wheel.transform.InverseTransformPoint(contacts[i].point).y - wheel.radius) / wheel.suspensionDistance);
            }
            if (Vector3.Dot(transform.up, Vector3.up) > .25f) { StabilizeAxle(0, 1); StabilizeAxle(2, 3); }
            localAcceleration = transform.InverseTransformDirection((Body.linearVelocity - priorVelocity) / Mathf.Max(.0001f, dt));
            localAcceleration = Vector3.ClampMagnitude(localAcceleration, Mathf.Max(0, Profile.MaximumSampleAcceleration));
            priorVelocity = Body.linearVelocity;
        }

        void ApplySteering()
        {
            float left = steering, right = steering;
            if (Mathf.Abs(steering) > .1f && wheelbase > .2f && track > .2f)
            {
                float radius = wheelbase / Mathf.Tan(Mathf.Abs(steering) * Mathf.Deg2Rad);
                float inner = Mathf.Atan(wheelbase / Mathf.Max(.1f, radius - track * .5f)) * Mathf.Rad2Deg;
                float outer = Mathf.Atan(wheelbase / (radius + track * .5f)) * Mathf.Rad2Deg;
                left = steering > 0 ? outer : -inner; right = steering > 0 ? inner : -outer;
            }
            Wheels[0].Collider.steerAngle = left; Wheels[1].Collider.steerAngle = right;
            Wheels[2].Collider.steerAngle = Wheels[3].Collider.steerAngle = 0;
        }
        void StabilizeAxle(int left, int right)
        {
            float force = (travel[left] - travel[right]) * Mathf.Max(0, Profile.AntiRollForce);
            if (grounded[left]) Body.AddForceAtPosition(-transform.up * force, Wheels[left].Collider.transform.position);
            if (grounded[right]) Body.AddForceAtPosition(transform.up * force, Wheels[right].Collider.transform.position);
        }
        void LateUpdate()
        {
            if (!IsConfigured) return;
            foreach (var binding in Wheels)
            {
                binding.Collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
                binding.Visual.SetPositionAndRotation(position, rotation * Quaternion.Euler(binding.VisualRotationOffset));
            }
            PoseAxle(FrontAxleVisual, 0, 1); PoseAxle(RearAxleVisual, 2, 3);
            if (Time.deltaTime <= 0) return;
            float pitch = GroundedWheelCount > 0 ? Mathf.Clamp(-localAcceleration.z * Profile.PitchDegreesPerAcceleration, -Profile.MaximumPitch, Profile.MaximumPitch) : 0;
            float roll = GroundedWheelCount > 0 ? Mathf.Clamp(localAcceleration.x * Profile.RollDegreesPerAcceleration, -Profile.MaximumRoll, Profile.MaximumRoll) : 0;
            float heave = GroundedWheelCount > 0 ? Mathf.Clamp(((travel[0] + travel[1] + travel[2] + travel[3]) * .25f - .5f) * 2, -1, 1) * Profile.MaximumVisualHeave : 0;
            visualEuler = Vector3.SmoothDamp(visualEuler, new Vector3(pitch, 0, roll), ref visualEulerVelocity, Mathf.Max(.01f, Profile.BodyResponse), Mathf.Infinity, Time.deltaTime);
            // #308 juice C-4: while a settle runs the damper keeps its own state and the closed form is added after it (ApplyVisualSettle308)
            if (settleActive308) ApplyVisualSettle308(heave);
            else BodyVisualRoot.localPosition = Vector3.SmoothDamp(BodyVisualRoot.localPosition, visualRestPosition + Vector3.up * heave, ref visualOffsetVelocity, Mathf.Max(.01f, Profile.BodyResponse), Mathf.Infinity, Time.deltaTime);
            BodyVisualRoot.localRotation = visualRestRotation * Quaternion.Euler(visualEuler);
        }
        // #308 player juice C-4 (SPEC-ANIM-JUICE-308) [TEST]: a presentation-only vertical settle of the visual body when the summoned car
        // is revealed (and a small lift when a recall starts). The visual root has no collider and is outside the wheels; the Rigidbody,
        // the hull and the suspension are not touched. LateUpdate normally damps the visual root with the transform itself as the
        // damper's state, so an offset written from outside would become damper state. While a settle runs the damper state is
        // kept in settleDamper308 instead and the closed form (PlayerJuice308Curves) is added after it: the motion follows the form
        // and, once its length has passed, the transform is exactly the damper's state again. The numbers come from the caller
        // (PlayerJuice308ProfileSO.Arrival). No static field.
        bool settleActive308, settleLift308;
        float settleTime308, settleAmount308, settleFall308, settleHz308, settleDamping308, settleLength308;
        Vector3 settleDamper308;
        public bool VisualSettleActive308 => settleActive308;
        /// <summary>#308 juice: the vertical offset added to the visual body this frame (m; 0 when no settle runs).</summary>
        public float VisualSettleOffset308 { get; private set; }
        public int VisualSettles308 { get; private set; }
        /// <summary>#308 juice C-4: the visual body sinks by dropMeters over fallSeconds, then settles back (hz, damping) and is exactly
        /// at rest fallSeconds + settleSeconds later.</summary>
        public void BeginVisualSettle308(float dropMeters, float fallSeconds, float hz, float damping, float settleSeconds)
        { StartVisualSettle308(false, dropMeters, fallSeconds, hz, damping, settleSeconds); }
        /// <summary>#308 juice C-4 recall: the visual body lifts by liftMeters and is back at rest `seconds` later.</summary>
        public void BeginVisualLift308(float liftMeters, float seconds)
        { StartVisualSettle308(true, liftMeters, 0f, 0f, 0f, seconds); }
        void StartVisualSettle308(bool lift, float amount, float fall, float hz, float damping, float length)
        {
            if (!IsConfigured || BodyVisualRoot == null || !(amount > 0f) || !(length > 0f)) return;
            if (!settleActive308) settleDamper308 = BodyVisualRoot.localPosition;   // until now the transform was the damper's state
            settleActive308 = true; settleLift308 = lift; settleTime308 = 0f; VisualSettles308++;
            settleAmount308 = amount; settleFall308 = Mathf.Max(0f, fall); settleHz308 = hz; settleDamping308 = damping; settleLength308 = length;
        }
        void ApplyVisualSettle308(float heave)
        {
            settleDamper308 = Vector3.SmoothDamp(settleDamper308, visualRestPosition + Vector3.up * heave, ref visualOffsetVelocity, Mathf.Max(.01f, Profile.BodyResponse), Mathf.Infinity, Time.deltaTime);
            settleTime308 += Time.deltaTime;
            float offset = settleLift308
                ? Oheangbu.Presentation.PlayerJuice308Curves.CarLift(settleTime308, settleAmount308, settleLength308)
                : Oheangbu.Presentation.PlayerJuice308Curves.CarSettle(settleTime308, settleAmount308, settleFall308, settleHz308, settleDamping308, settleLength308);
            if (settleTime308 >= (settleLift308 ? settleLength308 : settleFall308 + settleLength308)) { offset = 0f; settleActive308 = false; }
            VisualSettleOffset308 = offset;
            BodyVisualRoot.localPosition = offset != 0f ? settleDamper308 + Vector3.up * offset : settleDamper308;
        }
        void PoseAxle(Transform shaft, int left, int right)
        {
            if (shaft == null) return;
            Vector3 a = Wheels[left].Visual.position, b = Wheels[right].Visual.position, delta = b - a;
            if (delta.sqrMagnitude < .0001f) return;
            shaft.SetPositionAndRotation((a + b) * .5f, transform.rotation * Quaternion.FromToRotation(Vector3.right, transform.InverseTransformDirection(delta).normalized));
            // Different suspension heights tilt the fixed-length shaft; its ends remain inside the hubs.
        }
        void ApplyStoppedTorques()
        {
            AppliedMotorTorque=0;
            if (Wheels == null) return;
            foreach (var binding in Wheels) if (binding?.Collider != null) { binding.Collider.motorTorque = 0; binding.Collider.brakeTorque = Profile != null ? Profile.ParkingBrakeTorquePerWheel : 4000; }
        }
        void RestoreVisual()
        { settleActive308 = false; VisualSettleOffset308 = 0f; if (IsConfigured && BodyVisualRoot != null) { BodyVisualRoot.localPosition = visualRestPosition; BodyVisualRoot.localRotation = visualRestRotation; } }
    }
}
