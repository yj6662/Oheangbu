using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 D308-8 (SPEC-VEHICLE-UX-308) [TEST 전부]: 마석 자동차 UX 수치 — G 토글 거리, F 프롬프트 낱말, 먹 소환·회수 연출 시간과 모양,
    // 호출 획(D308-8c: 붓으로 허공에 긋는 획의 시간·경로·먹 자취·소환 자리로 흐름),
    // 탑승 자세(앉은 클립 칸 + 좌석 기준 오프셋). 런타임은 세션 필드 → Resources "Vehicle308/VehicleUx308Profile" 순서로 찾는다.
    // 연출은 무발광·LDR이다(ART-INK "빛은 전구가 아니라 먹이다"): 색 필드는 모두 먹·한지 범위이고 HDR을 받지 않는다.
    [CreateAssetMenu(menuName = "Oheangbu/World/Vehicle UX 308 TEST", fileName = "VehicleUx308Profile")]
    public sealed class VehicleUx308ProfileSO : ScriptableObject
    {
        public const string ResourcesPath = "Vehicle308/VehicleUx308Profile";
        public const string BossFieldsResourcesPath = "Vehicle308/VehicleBossFields308";

        [Header("G toggle [TEST]")]
        [Tooltip("G recalls the car when it is active within this flat distance of the player; farther (or recalled) G calls it to the front.")]
        [Min(1f)] public float RecallToggleDistance = 20f;

        [Header("F interaction [TEST]")]
        [Tooltip("Prompt word after [F] while the parked car can be boarded (HUD interaction prompt).")]
        public string BoardPrompt = "탑승";
        [Tooltip("Prompt word after [F] while seated and stopped.")]
        public string ExitPrompt = "하차";
        [Tooltip("Same-target cooldown id used with the session's #306 fresh-press rule.")]
        public string InteractTarget = "vehicle308";

        [Header("Ink summon / recall [TEST]")]
        [Tooltip("Oheangbu/VehicleInkShell308 material (transparent, no emission). Empty = particles only.")]
        public Material ShellMaterial;
        [Tooltip("Ink dust particle material (M_InkDust, Oheangbu/InkSpray). Empty = shell only.")]
        public Material InkParticleMaterial;
        public Color InkColour = new Color(.095f, .088f, .080f, 1f);
        public Color EdgeColour = new Color(.035f, .032f, .030f, 1f);
        [Tooltip("Summon: ink rises from the ground and gathers into the car's shape (s).")]
        [Min(.05f)] public float GatherSeconds = .45f;
        [Tooltip("Summon: full ink silhouette held while the car appears beneath it (s).")]
        [Min(0f)] public float HoldSeconds = .08f;
        [Tooltip("Summon: the ink recedes (역번짐) and reveals the car (s).")]
        [Min(.05f)] public float RecedeSeconds = .55f;
        [Tooltip("Recall: ink spreads over the car (s).")]
        [Min(.05f)] public float CoverSeconds = .40f;
        [Tooltip("Recall: the ink silhouette scatters from the top down (s).")]
        [Min(.05f)] public float ScatterSeconds = .60f;
        [Tooltip("World noise frequency of the ink edge (1/m).")]
        [Min(.1f)] public float NoiseScale = 3.5f;
        [Tooltip("Share of noise in the rise/fall order (0 = clean line, 1 = pure noise).")]
        [Range(0f, 1f)] public float NoiseShare = .35f;
        [Range(.005f, .3f)] public float EdgeWidth = .08f;
        [Tooltip("Shell pushed out along normals (m) so it covers the car surface.")]
        [Range(0f, .1f)] public float Inflate = .02f;
        [Min(0)] public int GatherParticles = 36;
        [Min(0)] public int ScatterParticles = 48;
        public Vector2 ParticleSize = new Vector2(.30f, .75f);
        [Tooltip("Radial speed toward the car while it gathers (m/s).")]
        [Min(0f)] public float GatherInward = 2.4f;
        [Tooltip("Radial speed away from the car while it scatters (m/s).")]
        [Min(0f)] public float ScatterOutward = 1.5f;
        [Min(0f)] public float ScatterRise = .6f;
        [Range(0f, 1f)] public float ParticleAlpha = .55f;

        // #308 D308-8c: G = 손에 든 붓으로 허공에 짧은 호출 획(패 들어 보이기 1.8 s 대체). 표현 전용 — 인식기·필세 입력이 아니다.
        [Header("Call stroke D308-8c [TEST]")]
        [Tooltip("G summon / recall / declined call = a brush stroke in the air (false = the legacy pendant gesture).")]
        public bool CallStroke = true;
        [Tooltip("Whole stroke gesture: raise, stroke, lower (s). The action gate is locked for exactly this long.")]
        [Range(.6f, 1.6f)] public float CallStrokeSeconds = 1.0f;
        [Tooltip("Share of the gesture at which the raised hand + brush reach the stroke start.")]
        [Range(.1f, .6f)] public float StrokeRaiseEnd = .30f;
        [Tooltip("Share of the gesture at which the brush leaves the air = the call moment (car placed / recall starts / ink flows).")]
        [Range(.3f, .9f)] public float StrokeContactEnd = .62f;
        [Tooltip("How the action gate opens when the stroke ends or is cancelled (damage). true = the SPEC-VEHICLE-CALL rule: only after every held gameplay key is released (a move / dodge key held through the stroke keeps the player locked past it). false = at once: the lock is exactly the stroke and held movement resumes (death and an unfocused window still wait for neutral). User decision pending.")]
        public bool StrokeGateWaitsForNeutral = true;
        [Tooltip("Contact time 0..1 -> path 0..1. Steep middle = a decisive stroke.")]
        public AnimationCurve StrokeEase = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(.5f, .5f, 2.2f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("Stroke start on the world drawing plane (centred: x right, y up; ±1 = the gesture profile's WorldDrawingHalfSize, beyond allowed). Upper right keeps it visible over the right shoulder.")]
        public Vector2 StrokeFrom = new Vector2(.40f, 1.50f);
        [Tooltip("Stroke end on the world drawing plane.")]
        public Vector2 StrokeTo = new Vector2(1.50f, .70f);
        [Tooltip("Bulge as a share of the stroke length (+ = left of the travel direction).")]
        [Range(-.5f, .5f)] public float StrokeArc = .12f;
        [Tooltip("Forward push of the tip toward the far end of the stroke (m) — the stroke thrusts toward the summon side.")]
        [Range(0f, .4f)] public float StrokeReach = .12f;
        [Tooltip("Torso share while the stroke is held (0..1).")]
        [Range(0f, 1f)] public float StrokeBodyParticipation = .35f;
        [Tooltip("While the stroke runs the visual body turns to face the call direction (controller forward) at this speed (deg/s). Standing look-around can leave it up to 90° off; 360 turns that in .25 s, inside the raise. 0 = no turn.")]
        [Range(0f, 1440f)] public float StrokeBodyTurnSpeed = 360f;
        [Tooltip("Recall draws the same stroke backwards (it starts extended and withdraws toward the body).")]
        public bool RecallReversed = true;
        [Tooltip("Ink stroke material for the trail and the flow (Oheangbu/InkStroke, e.g. M_ParryCounterStroke306). Empty = a copy of the Oheangbu/InkStroke shader. Emission add is forced to 0.")]
        public Material CallStrokeMaterial;
        [Tooltip("Trail width at the stroke start (m).")]
        [Range(.01f, .2f)] public float StrokeWidth = .07f;
        [Tooltip("Trail width at the stroke end, share of StrokeWidth (the brush lifts off).")]
        [Range(.1f, 1f)] public float StrokeEndWidthScale = .45f;
        [Tooltip("Ink density at the stroke end (1 = wet; lower = dry brush).")]
        [Range(.2f, 1f)] public float StrokeEndInk = .55f;
        [Tooltip("Trail points kept (recorded from the real brush tip while it touches the air).")]
        [Range(8, 64)] public int TrailMaxPoints = 40;
        [Tooltip("Minimum tip travel between trail points (m).")]
        [Range(.002f, .05f)] public float TrailMinSpacing = .01f;
        [Tooltip("The trail dries away after the call moment (s).")]
        [Min(.05f)] public float TrailMeltSeconds = .45f;
        [Tooltip("The ink flies from the stroke end to the car spot (s).")]
        [Min(.05f)] public float FlowSeconds = .30f;
        [Tooltip("Share of FlowSeconds after which the car's ink gather (summon) / cover (recall) begins — the hand-over.")]
        [Range(0f, 1f)] public float GatherAfterFlow = .65f;
        [Range(.01f, .2f)] public float FlowWidth = .08f;
        [Tooltip("Flow width at the car, share of FlowWidth.")]
        [Range(.1f, 1.5f)] public float FlowEndWidthScale = .60f;
        [Tooltip("Visible length of the flying ink, share of the flight line.")]
        [Range(.1f, 1f)] public float FlowTailLength = .55f;
        [Range(-.5f, .5f)] public float FlowArc = .10f;
        [Tooltip("The flow's tail dries into the car after it lands (s).")]
        [Min(.05f)] public float FlowMeltSeconds = .25f;

        [Header("Visible rider [TEST]")]
        public bool ShowRider = true;
        [Tooltip("Seated humanoid clip slot (now N306_WritingSeated, frozen; later a Mixamo 'Sitting Idle' / 'Driving'). Used through the controller layer below, authored by Vehicle308 ride-layer-apply.")]
        public AnimationClip SeatedClip;
        public string RideLayer = "Ride308";
        public string RideState = "Seated";
        public string RideSpeedParameter = "Ride308Speed";
        [Tooltip("Normalized clip time the seated state starts at.")]
        [Range(0f, 1f)] public float SeatedClipTime = .2f;
        [Tooltip("Seated state speed (0 = frozen frame; 1 = a real looping sitting idle).")]
        [Min(0f)] public float SeatedClipSpeed = 0f;
        [Tooltip("Hips position from the car's SeatSocket, in the car body's axes (BodyVisualRoot rotation, metres, +Z front; the socket's own viewing pitch is ignored). Overridden by a hand-authored child 'RideHipAnchor308' under the car body when present.")]
        public Vector3 HipOffsetFromSeatSocket = new Vector3(0f, -.66f, -.12f);
        public bool FeetToFloor = true;
        [Tooltip("Foot target from the hips (car-body axes; x mirrored per side).")]
        public Vector3 FootOffsetFromHip = new Vector3(.11f, -.48f, .42f);
        public bool HandsToGrip = true;
        [Tooltip("Hand target from the hips (car-body axes; x mirrored per side).")]
        public Vector3 HandOffsetFromHip = new Vector3(.20f, .30f, .42f);
        [Tooltip("Largest forward lean (degrees, spine + chest, toward the grip) the rider takes so both shoulders come within arm's reach of the grip targets. The seated clip's shoulders can sit too far back for the two-bone arm (hands then stopped ~.12 m short); 0 = no lean.")]
        [Range(0f, 45f)] public float GripReachLean = 35f;
        [Tooltip("Share of the arm length (upper + fore arm) used when leaning to reach the grip: below 1 keeps the elbows a little bent.")]
        [Range(.8f, 1f)] public float GripReachShare = .95f;
        [Tooltip("How much the head is turned to look along the car (0 = clip head).")]
        [Range(0f, 1f)] public float HeadLevel = .8f;
        [Tooltip("Degrees the levelled head looks down.")]
        [Range(-20f, 30f)] public float HeadPitchDown = 6f;
        [Tooltip("First-person seated view (V): the head bone is scaled to ~0 so the eye camera does not see inside it.")]
        public bool HideHeadInSeatView = true;
    }
}
