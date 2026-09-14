using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>TEST vehicle values, independent of the shared player and world travel rules.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World/Macro Palanquin TEST", fileName = "MacroPalanquinProfile")]
    public sealed class WorldMacroPalanquinProfileSO : ScriptableObject
    {
        [Header("Physics root: +Z front, +Y up, metres, scale 1")]
        [Min(100)] public float Mass = 780;
        public Vector3 CentreOfMass = new Vector3(0, .55f, 0);
        public Vector3 HullCentre = new Vector3(0, 1.02f, 0);
        public Vector3 HullSize = new Vector3(1.6f, 1.2f, 2.8f);
        [Min(0)] public float LinearDamping = .06f;
        [Min(0)] public float AngularDamping = .8f;
        [Min(1)] public int SolverIterations = 12;
        [Min(1)] public int SolverVelocityIterations = 4;

        [Header("Four separate WheelColliders; axle along local X")]
        [Min(.1f)] public float WheelRadius = .48f;
        [Min(1)] public float WheelMass = 25;
        [Min(.01f)] public float SuspensionDistance = .28f;
        [Min(100)] public float SuspensionSpring = 26000;
        [Min(0)] public float SuspensionDamper = 3400;
        [Range(0, 1)] public float SuspensionTarget = .5f;
        [Min(0)] public float ForceApplicationHeight = .15f;
        [Min(0)] public float WheelDamping = .6f;
        [Min(.1f)] public float ForwardFriction = 1.1f;
        [Min(.1f)] public float SidewaysFriction = 1.35f;
        [Min(0)] public float AntiRollForce = 2200;

        [Header("Drive; torque is total for all driven wheels")]
        [Min(1)] public float ForwardSpeed = 14;
        [Min(.1f)] public float ReverseSpeed = 4;
        [Min(0)] public float MotorTorque = 2100;
        [Range(0, 1)] public float FrontDriveShare = .5f;
        [Min(0)] public float BrakeTorquePerWheel = 2800;
        [Min(0)] public float ParkingBrakeTorquePerWheel = 4000;
        [Min(.01f)] public float DirectionChangeSpeed = .45f;
        [Min(.1f)] public float ThrottleResponse = 2;
        [Min(.1f)] public float BrakeResponse = 5;
        [Range(1, 60)] public float LowSpeedSteerAngle = 32;
        [Range(1, 45)] public float HighSpeedSteerAngle = 10;
        [Min(1)] public float SteerDegreesPerSecond = 80;

        [Header("Visual body motion; does not move any collider")]
        [Min(.01f)] public float BodyResponse = .22f;
        [Min(0)] public float PitchDegreesPerAcceleration = .5f;
        [Min(0)] public float RollDegreesPerAcceleration = .65f;
        [Min(0)] public float MaximumPitch = 4;
        [Min(0)] public float MaximumRoll = 5;
        [Min(0)] public float MaximumVisualHeave = .035f;
        [Min(0)] public float MaximumSampleAcceleration = 15;

        [Header("Boarding and safe exit")]
        [Min(.5f)] public float BoardingDistance = 2.7f;
        [Min(0)] public float BoardingMaximumSpeed = .8f;
        [Min(0)] public float ExitMaximumSpeed = .8f;
        [Min(1)] public float ExitSideDistance = 2.1f;
        [Min(1)] public float ExitEndDistance = 2.8f;
        [Min(.1f)] public float ExitGroundProbeUp = 2;
        [Min(.1f)] public float ExitGroundProbeDown = 3;
        [Min(0)] public float ExitClearance = .025f;
        public LayerMask EnvironmentMask = Physics.DefaultRaycastLayers;

        [Header("Seated and external camera; RMB look / V switch")]
        [Min(.01f)] public float CameraResponse = .12f;
        [Range(0, 1)] public float SeatTiltFollow = .3f;
        [Min(.01f)] public float MouseSensitivity = .12f;
        [Range(5, 170)] public float SeatYawLimit = 85;
        public Vector2 PitchLimits = new Vector2(-45, 55);
        [Min(1)] public float ExternalDistance = 6;
        [Min(0)] public float ExternalPivotHeight = .6f;
        public float ExternalShoulderOffset;
        [Min(.01f)] public float CameraCollisionRadius = .2f;
        [Min(.05f)] public float CameraMinimumDistance = .45f;
    }
}
