using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Oheangbu.Combat;
using Oheangbu.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroLocomotionAuthoring
    {
        [Serializable] private sealed class MotorCheck
        {
            public string name, status, detail;
            public float measured, limit;
        }
        [Serializable] private sealed class MotorReport
        {
            public string status;
            public string scope;
            public List<MotorCheck> checks = new List<MotorCheck>();
            public bool resourcesReleased, timeRestored;
            public string[] unverified = { "Native Ctrl/Space/C/Q interactions and real-time Animator transitions", "Current-scene terrain, slope/stair contacts, art and foot skin", "Real pause menu and occupied vehicle UI integration" };
        }
        private const BindingFlags MotorFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool motorFixtureQueued;
        private static double motorFixtureQueuedAt;

        [StructLayout(LayoutKind.Sequential)]
        private struct LocomotionDiagnosticState : IInputStateTypeInfo
        {
            public FourCC format => new FourCC('L','M','Q','A');
            [InputControl(name="sprint",layout="Button")] public float sprint;
            [InputControl(name="harvest",layout="Button")] public float harvest;
            [InputControl(name="draw",layout="Button")] public float draw;
        }
        // Not a Gamepad/Keyboard subclass: existing gameplay and UI bindings cannot
        // consume these controls even while the real player's action maps are enabled.
        [InputControlLayout(stateType=typeof(LocomotionDiagnosticState))]
        public sealed class LocomotionDiagnosticButtons : InputDevice
        {
            public ButtonControl Sprint { get; private set; }
            public ButtonControl Harvest { get; private set; }
            public ButtonControl Draw { get; private set; }
            protected override void FinishSetup()
            {
                base.FinishSetup();Sprint=GetChildControl<ButtonControl>("sprint");
                Harvest=GetChildControl<ButtonControl>("harvest");Draw=GetChildControl<ButtonControl>("draw");
            }
        }

        /// <summary>Called by the root's serialized dispatcher. Isolated fixture in Edit or Play; no captures or build.</summary>
        public static string RunMotorChecks()
        {
            if(motorFixtureQueued)return "QUEUED: awaiting the next actual Dynamic Input System update; inspect unity_synthetic_motor.json.";
            if(!EditorApplication.isPlaying)
                return SaveFixtureStatus("NOT_RUN_REQUIRES_PLAY_MODE","The Input System explicitly ignores action monitors in Editor updates. Enter Play mode and request this fixture again. No held-action gate or gameplay check was bypassed.");
            motorFixtureQueued=true;motorFixtureQueuedAt=EditorApplication.timeSinceStartup;
            InputSystem.onAfterUpdate+=RunQueuedMotorFixture;EditorApplication.update+=WatchMotorFixture;
            return SaveFixtureStatus("QUEUED_PLAYER_INPUT_UPDATE","Temporary fixture will execute once inside an actual Dynamic update. No global InputSystem.Update or input-setting changes are made.");
        }
        private static string SaveFixtureStatus(string status,string detail)
        {
            var report=new MotorReport{status=status,scope=detail,resourcesReleased=true,timeRestored=true};
            Directory.CreateDirectory(ReportFolder);string json=JsonUtility.ToJson(report,true);
            File.WriteAllText(Path.Combine(ReportFolder,"unity_synthetic_motor.json"),json);return json;
        }
        private static void ClearMotorFixtureQueue()
        {motorFixtureQueued=false;InputSystem.onAfterUpdate-=RunQueuedMotorFixture;EditorApplication.update-=WatchMotorFixture;}
        private static void WatchMotorFixture()
        {
            if(!motorFixtureQueued)return;
            if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup-motorFixtureQueuedAt>20)
            {ClearMotorFixtureQueue();SaveFixtureStatus("NOT_RUN_NO_PLAYER_INPUT_UPDATE","Play mode ended or no Dynamic player input update arrived within 20 seconds. Gameplay tests were not executed.");}
        }
        private static void RunQueuedMotorFixture()
        {
            if(!motorFixtureQueued||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
            ClearMotorFixtureQueue();ExecuteMotorChecksInPlayerUpdate();
        }
        private static string ExecuteMotorChecksInPlayerUpdate()
        {
            bool playing = EditorApplication.isPlaying;
            var report = new MotorReport { scope = (playing ? "PLAY-MODE" : "EDIT-MODE")
                + " SYNTHETIC: isolated temporary CharacterController and ground at y=-1000; real PlayerMotor scaled steps and action eligibility, dedicated virtual-device state injection inside an actual Dynamic input update. No OS input, live-player movement, save, damage/reward, animation or whole-route play." };
            float priorTime = Time.timeScale;
            CursorLockMode priorLock = Cursor.lockState; bool priorVisible = Cursor.visible;
            GameObject fixture = null, floor = null;
            PlayerMotor motor = null; CombatConfigSO config = null; PlayerLocomotionProfileSO profile = null;
            GameplayRuntimeStateSO state = null; InputActionAsset actions = null;
            LocomotionDiagnosticButtons device = null; InputActionMap diagnosticMap = null;
            InputAction sprint = null, harvest = null, draw = null;bool layoutRegistered=false;
            try
            {
                Time.timeScale = 1f;
                config = ScriptableObject.CreateInstance<CombatConfigSO>();
                profile = ScriptableObject.CreateInstance<PlayerLocomotionProfileSO>();
                state = ScriptableObject.CreateInstance<GameplayRuntimeStateSO>(); state.SetInputBlocked(false);
                actions = Object.Instantiate(Need<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
                InputSystem.RegisterLayout<LocomotionDiagnosticButtons>();layoutRegistered=true;
                device = InputSystem.AddDevice<LocomotionDiagnosticButtons>("LocomotionDiagnosticOnly");
                actions.devices = new InputDevice[] { device };
                diagnosticMap=new InputActionMap("LocomotionDiagnosticOnly");diagnosticMap.devices=new InputDevice[]{device};
                sprint = diagnosticMap.AddAction("DiagnosticSprint", InputActionType.Button, "<LocomotionDiagnosticButtons>/sprint");
                harvest = diagnosticMap.AddAction("DiagnosticHarvest", InputActionType.Button, "<LocomotionDiagnosticButtons>/harvest");
                draw = diagnosticMap.AddAction("DiagnosticDraw", InputActionType.Button, "<LocomotionDiagnosticButtons>/draw");
                diagnosticMap.Enable();
                floor = new GameObject("__LocomotionDiagnosticGround") { hideFlags = HideFlags.HideAndDontSave };
                floor.transform.position = new Vector3(0f, -1000.25f, 0f);
                floor.AddComponent<BoxCollider>().size = new Vector3(100f, .5f, 100f);
                fixture = new GameObject("__LocomotionDiagnosticPlayer") { hideFlags = HideFlags.HideAndDontSave };
                fixture.SetActive(false);
                fixture.transform.position = new Vector3(0f, -1000f, 0f);
                var body = fixture.AddComponent<CharacterController>();
                body.height = 1.75f; body.radius = .28f; body.center = Vector3.up * .875f; body.skinWidth = .03f; body.stepOffset = .3f; body.slopeLimit = 45f;
                var pivot = new GameObject("DiagnosticEye").transform; pivot.SetParent(fixture.transform, false); pivot.localPosition = Vector3.up * 1.5f;
                var dodge = fixture.AddComponent<DodgeAction>();
                var dodgeData = new SerializedObject(dodge); dodgeData.FindProperty("_config").objectReferenceValue = config; dodgeData.ApplyModifiedPropertiesWithoutUndo();
                motor = fixture.AddComponent<PlayerMotor>();
                Set(motor, "_actions", actions); Set(motor, "_config", config); Set(motor, "_cameraPivot", pivot); Set(motor, "_dodge", dodge);
                motor.RuntimeState = state; Set(motor, "_locomotion", profile);
                fixture.SetActive(true);
                if (!playing) { Call(motor, "Awake"); Call(motor, "OnEnable"); }
                motor.ConfigureLocomotion(profile);
                Set(motor, "_sprintAction", sprint); Set(motor, "_harvestAction", harvest);Set(motor,"_locomotionDrawAction",draw);
                Physics.SyncTransforms();

                Action<Vector2, float> step = (input, dt) => Call(motor, "StepLocomotion", input, dt);
                Action<int, float> settle = (frames, dt) => { for (int i = 0; i < frames; i++) step(Vector2.zero, dt); };
                Action reset = () =>
                {
                    SendDiagnosticButtons(device, false, false); state.SetInputBlocked(false); Set(motor, "_drawing", false);
                    body.enabled = false; fixture.transform.position = new Vector3(0f, -1000f, 0f); body.enabled = true;
                    motor.ResetMotion(); Physics.SyncTransforms(); settle(8, 1f / 60f);
                };
                SendDiagnosticButtons(device, true, true);
                Add(report, "virtual held action precondition", InputState.currentUpdateType==InputUpdateType.Dynamic
                    && sprint.enabled && harvest.enabled && sprint.controls.Count == 1 && harvest.controls.Count == 1
                    && sprint.controls[0].device==device && harvest.controls[0].device==device && sprint.IsPressed() && harvest.IsPressed(),
                    sprint.ReadValue<float>() + harvest.ReadValue<float>(), 2f,
                    "Temporary action enabled/controls: " + sprint.enabled + "/" + sprint.controls.Count + ", " + harvest.enabled + "/" + harvest.controls.Count
                    + "; update="+InputState.currentUpdateType+"; device values=" + device.Sprint.ReadValue() + "/" + device.Harvest.ReadValue()
                    + "; action phases="+sprint.phase+"/"+harvest.phase+"; dedicated device="+device.deviceId
                    + ". Both actual virtual controls and action state must read held before eligibility tests.");
                if (!sprint.IsPressed() || !harvest.IsPressed()) throw new InvalidOperationException(
                    "Virtual held actions did not consume the Dynamic device state; gameplay checks must not run with invalid fixture input.");
                reset();
                Add(report, "initial ground acquired", motor.IsLocomotionGrounded && motor.CanBeginDrawing, body.bounds.min.y + 1000f, .05f);
                SendDiagnosticButtons(device,false,true,true);step(Vector2.zero,1f/60f);
                Add(report,"same-frame Q+LMB press cannot start a chunk pull before drawing state",draw.IsPressed()&&harvest.IsPressed()&&!motor.IsDrawing&&!CanHarvest(motor),CanHarvest(motor)?1f:0f,0f,
                    "D306 click harvest: the motor starts a pull only on the LMB press frame when CanHarvestNow holds; evaluated with a held draw action while _drawing remains false. Native Q/LMB ordering remains unverified.");
                reset();
                foreach (int fps in new[] { 30, 60, 120 }) foreach (float scale in new[] { 1f, .2f })
                {
                    reset(); float dt = scale / fps, start = fixture.transform.position.y, peak = 0f; int serial = motor.JumpSerial;
                    Call(motor, "OnJump", default(InputAction.CallbackContext));
                    Add(report, "jump starts " + fps + "fps scale " + scale, motor.JumpSerial == serial + 1 && !motor.CanBeginDrawing, motor.VerticalVelocity, PlayerMotor.JumpSpeed(.75f, config.Gravity));
                    int airborneSerial = motor.JumpSerial;
                    Call(motor, "OnJump", default(InputAction.CallbackContext)); Call(motor, "OnDodge", default(InputAction.CallbackContext));
                    Add(report, "air jump/dodge/draw rejected " + fps + "/" + scale, motor.JumpSerial == airborneSerial && !dodge.IsDashing && !motor.CanBeginDrawing, motor.JumpSerial - airborneSerial, 0f);
                    SendDiagnosticButtons(device, false, true);
                    for (int i = 0; i < fps * 12 && (i == 0 || motor.IsAirborne); i++)
                    { step(Vector2.zero, dt); peak = Mathf.Max(peak, fixture.transform.position.y - start); }
                    Add(report, "real CharacterController apex " + fps + "/" + scale, Mathf.Abs(peak - .75f) < .02f, peak, .75f);
                    Add(report, "landing acquired " + fps + "/" + scale, motor.IsLocomotionGrounded, fixture.transform.position.y - start, .03f);
                    Add(report, "LMB held across landing cannot start a pull " + fps + "/" + scale, !CanHarvest(motor), CanHarvest(motor) ? 1f : 0f, 0f);
                    SendDiagnosticButtons(device, false, false); step(Vector2.zero, dt);
                    Add(report, "released LMB re-arms the next press " + fps + "/" + scale, CanHarvest(motor), CanHarvest(motor) ? 1f : 0f, 1f);
                }

                reset(); Set(motor, "_drawing", true); int priorSerial = motor.JumpSerial;
                Call(motor, "OnJump", default(InputAction.CallbackContext));
                Add(report, "jump rejected during drawing", motor.JumpSerial == priorSerial, motor.JumpSerial - priorSerial, 0f);
                Set(motor, "_drawing", false);
                // D306: a held LMB is no longer a harvest; only an active pull (HarvestAction.IsExtracting) blocks the jump
                SendDiagnosticButtons(device, false, true); Call(motor, "OnJump", default(InputAction.CallbackContext));
                Add(report, "held LMB without a pull does not block jump", motor.JumpSerial == priorSerial + 1, motor.JumpSerial - priorSerial, 1f);
                SendDiagnosticButtons(device, false, false); reset();
                for (int i = 0; i < 120; i++) step(Vector2.up, 1f / 60f);
                Add(report, "actual walk speed", Mathf.Abs(motor.ActualLocalVelocity.z - 2.2f) < .04f, motor.ActualLocalVelocity.z, 2.2f);
                SendDiagnosticButtons(device, true, false);
                for (int i = 0; i < 120; i++) step(Vector2.up, 1f / 60f);
                Add(report, "held sprint actual speed", motor.IsSprinting && Mathf.Abs(motor.ActualLocalVelocity.z - 5.5f) < .04f, motor.ActualLocalVelocity.z, 5.5f);
                for (int i = 0; i < 90; i++) step(Vector2.one, 1f / 60f);
                float diagonal = new Vector2(motor.ActualLocalVelocity.x, motor.ActualLocalVelocity.z).magnitude;
                Add(report, "diagonal sprint normalized", Mathf.Abs(diagonal - 5.5f) < .05f, diagonal, 5.5f);

                state.SetInputBlocked(true); Call(motor, "Update");
                Vector3 stopped = fixture.transform.position; state.SetInputBlocked(false); SendDiagnosticButtons(device, false, false); step(Vector2.zero, 1f / 60f);
                Add(report, "menu release has no stale planar drift", Vector2.Distance(new Vector2(stopped.x, stopped.z), new Vector2(fixture.transform.position.x, fixture.transform.position.z)) < .001f, motor.ActualLocalVelocity.magnitude, 0f);
                // #300 run toggle: a press latches running without holding; a second press, stopping or crouching ends it
                reset(); profile.SprintToggle = true;
                Call(motor, "OnSprint", default(InputAction.CallbackContext));
                for (int i = 0; i < 120; i++) step(Vector2.up, 1f / 60f);
                Add(report, "toggle run latched without holding", motor.SprintLatched && motor.IsSprinting && Mathf.Abs(motor.ActualLocalVelocity.z - 5.5f) < .04f, motor.ActualLocalVelocity.z, 5.5f);
                SendDiagnosticButtons(device, true, false);
                for (int i = 0; i < 30; i++) step(Vector2.up, 1f / 60f);
                SendDiagnosticButtons(device, false, false);
                Add(report, "toggle mode ignores the held key", motor.SprintLatched && Mathf.Abs(motor.ActualLocalVelocity.z - 5.5f) < .04f, motor.ActualLocalVelocity.z, 5.5f);
                Call(motor, "OnSprint", default(InputAction.CallbackContext));
                for (int i = 0; i < 120; i++) step(Vector2.up, 1f / 60f);
                Add(report, "second press returns to walk", !motor.SprintLatched && !motor.IsSprinting && Mathf.Abs(motor.ActualLocalVelocity.z - 2.2f) < .04f, motor.ActualLocalVelocity.z, 2.2f);
                Call(motor, "OnSprint", default(InputAction.CallbackContext));
                for (int i = 0; i < 60; i++) step(Vector2.up, 1f / 60f);
                for (int i = 0; i < 10; i++) step(Vector2.zero, 1f / 60f);
                for (int i = 0; i < 120; i++) step(Vector2.up, 1f / 60f);
                Add(report, "brief stop (.17 s) keeps the latched run", motor.SprintLatched && Mathf.Abs(motor.ActualLocalVelocity.z - 5.5f) < .04f, motor.ActualLocalVelocity.z, 5.5f);
                for (int i = 0; i < 30; i++) step(Vector2.zero, 1f / 60f);
                for (int i = 0; i < 120; i++) step(Vector2.up, 1f / 60f);
                Add(report, "stopping (.5 s) ends the latched run", !motor.SprintLatched && Mathf.Abs(motor.ActualLocalVelocity.z - 2.2f) < .04f, motor.ActualLocalVelocity.z, 2.2f, "SprintToggleStopSeconds=" + profile.SprintToggleStopSeconds);
                Call(motor, "OnSprint", default(InputAction.CallbackContext)); step(Vector2.up, 1f / 60f);
                Call(motor, "OnCrouch", default(InputAction.CallbackContext)); settle(30, 1f / 60f);
                Add(report, "crouch ends the latched run", motor.IsCrouching && !motor.SprintLatched, motor.SprintLatched ? 1f : 0f, 0f);
                profile.SprintToggle = false;
                reset(); Call(motor, "OnSit", default(InputAction.CallbackContext)); settle(45, 1f / 60f);
                Add(report, "stationary sit complete", motor.IsSitting && motor.Posture01 > .999f && !motor.CanBeginDrawing && Mathf.Abs(body.height - profile.SittingHeight) < .001f, motor.Posture01, 1f);
                float feetBefore = fixture.transform.position.y; float postureBefore = motor.Posture01;
                state.SetInputBlocked(true); Time.timeScale = 0f; Call(motor, "Update");
                Call(motor, "OnSit", default(InputAction.CallbackContext)); Call(motor, "OnJump", default(InputAction.CallbackContext));
                Add(report, "pause preserves sit without accepting action", Mathf.Abs(motor.Posture01 - postureBefore) < .00001f && motor.SitRequested, motor.Posture01, postureBefore);
                Time.timeScale = 1f; state.SetInputBlocked(false);
                SendDiagnosticButtons(device, false, false); step(Vector2.zero, 1f / 60f);
                SendDiagnosticButtons(device, false, true); step(Vector2.zero, 1f / 60f);
                for (int i = 0; i < 40; i++) step(Vector2.up, 1f / 60f);
                Add(report, "movement gets up preserving foot height", !motor.IsSitting && Mathf.Abs(body.height - 1.75f) < .001f && Mathf.Abs(fixture.transform.position.y - feetBefore) < .03f, fixture.transform.position.y - feetBefore, .03f);
                Add(report, "LMB held through seated getup cannot start a pull", !CanHarvest(motor), CanHarvest(motor) ? 1f : 0f, 0f);
                SendDiagnosticButtons(device, false, false); step(Vector2.zero, 1f / 60f);
                Add(report, "release after getup re-arms the next press", CanHarvest(motor), CanHarvest(motor) ? 1f : 0f, 1f);
                settle(30, 1f / 60f); Call(motor, "OnSit", default(InputAction.CallbackContext)); settle(45, 1f / 60f);
                Call(motor, "OnLocomotionDamage", 1f); settle(40, 1f / 60f);
                Add(report, "synthetic damage notification cancels sit", !motor.IsSitting, motor.Posture01, 0f, "Only the presentation/motor notification is invoked; no HP, reward or save is changed.");
                foreach(int crouchFps in new[]{30,60,120})
                {
                    reset();float dt=1f/crouchFps,feet=fixture.transform.position.y;
                    Call(motor,"OnCrouch",default(InputAction.CallbackContext));settle(crouchFps/2,dt);
                    Add(report,"crouch capsule/feet "+crouchFps,motor.IsCrouching&&Mathf.Abs(body.height-1.25f)<.001f&&Mathf.Abs(fixture.transform.position.y-feet)<.02f,body.height,1.25f,"feetDelta="+(fixture.transform.position.y-feet)+"; crouch="+motor.IsCrouching+"; center="+body.center);
                    SendDiagnosticButtons(device,true,false);for(int i=0;i<crouchFps;i++)step(Vector2.up,dt);
                    Add(report,"crouch speed overrides held sprint "+crouchFps,!motor.IsSprinting&&Mathf.Abs(motor.ActualLocalVelocity.z-1.2f)<.04f,motor.ActualLocalVelocity.z,1.2f);
                    int serial=motor.JumpSerial;Call(motor,"OnJump",default(InputAction.CallbackContext));Call(motor,"OnDodge",default(InputAction.CallbackContext));
                    Add(report,"crouch rejects jump/dodge "+crouchFps,motor.JumpSerial==serial&&!motor.IsDodging,motor.JumpSerial-serial,0);
                    SendDiagnosticButtons(device,false,false);step(Vector2.zero,dt);
                    Add(report,"crouch permits draw/harvest "+crouchFps,motor.CanBeginDrawing&&CanHarvest(motor),motor.CanBeginDrawing?1:0,1);
                    Set(motor,"_drawing",true);Call(motor,"OnCrouch",default(InputAction.CallbackContext));Add(report,"C ignored during draw "+crouchFps,motor.IsCrouching,motor.Crouch01,1);Set(motor,"_drawing",false);
                    // D306: held LMB without a pull no longer blocks C (stand request accepted), then crouch again for the ceiling case
                    SendDiagnosticButtons(device,false,true);Call(motor,"OnCrouch",default(InputAction.CallbackContext));Add(report,"C with held LMB and no pull toggles stand "+crouchFps,CrouchTarget(motor)<.5f,CrouchTarget(motor),0);
                    Call(motor,"OnCrouch",default(InputAction.CallbackContext));SendDiagnosticButtons(device,false,false);
                    var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    try
                    {
                        ceiling.transform.position=fixture.transform.position+Vector3.up*1.5f;ceiling.transform.localScale=new Vector3(3,.1f,3);Physics.SyncTransforms();
                        Call(motor,"OnCrouch",default(InputAction.CallbackContext));settle(crouchFps/2,dt);
                        Add(report,"low ceiling blocks stand/boarding "+crouchFps,motor.IsCrouching&&!motor.CanStandForBoarding,body.height,1.25f);
                    }
                    finally{Object.DestroyImmediate(ceiling);Physics.SyncTransforms();}
                    state.SetInputBlocked(true);Call(motor,"OnCrouch",default(InputAction.CallbackContext));Add(report,"menu preserves crouch "+crouchFps,motor.IsCrouching,motor.Crouch01,1);state.SetInputBlocked(false);
                    Add(report,"boarding explicitly restores stand "+crouchFps,motor.PrepareForBoarding()&&!motor.IsCrouching&&Mathf.Abs(body.height-1.75f)<.001f,body.height,1.75f);
                }
                motor.ConfigureLocomotion(null);
                Add(report, "unconfigured motor leaves legacy entry gate available", !motor.HasLocomotion && motor.CanBeginDrawing && Mathf.Abs(body.height - 1.75f) < .001f, body.height, 1.75f);
                report.status = report.checks.TrueForAll(c => c.status == "PASS") ? "SYNTHETIC_MOTOR_PASS_NATIVE_UNVERIFIED" : "FAILED";
            }
            catch (Exception exception)
            { report.status = "FAILED_EXCEPTION"; report.checks.Add(new MotorCheck { name = "exception", status = "FAIL", detail = exception.ToString() }); }
            finally
            {
                if (motor != null) { if (playing) fixture.SetActive(false); else Call(motor, "OnDisable"); }
                diagnosticMap?.Dispose();
                if (device != null && device.added) InputSystem.RemoveDevice(device);
                if(layoutRegistered)InputSystem.RemoveLayout(nameof(LocomotionDiagnosticButtons));
                if (fixture != null) Object.DestroyImmediate(fixture); if (floor != null) Object.DestroyImmediate(floor);
                if (actions != null) Object.DestroyImmediate(actions); if (config != null) Object.DestroyImmediate(config);
                if (profile != null) Object.DestroyImmediate(profile); if (state != null) Object.DestroyImmediate(state);
                Physics.SyncTransforms(); Time.timeScale = priorTime; Cursor.lockState = priorLock; Cursor.visible = priorVisible;
                report.resourcesReleased = fixture == null && floor == null && (device == null || !device.added);
                report.timeRestored = Time.timeScale == priorTime;
            }
            Directory.CreateDirectory(ReportFolder); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(ReportFolder, "unity_synthetic_motor.json"), json); return json;
        }

        private static void SendDiagnosticButtons(LocomotionDiagnosticButtons device, bool sprint, bool harvest,bool draw=false)
        {
            if(InputState.currentUpdateType!=InputUpdateType.Dynamic)throw new InvalidOperationException("Diagnostic input requires the real Dynamic update context.");
            var value = new LocomotionDiagnosticState{sprint=sprint?1f:0f,harvest=harvest?1f:0f,draw=draw?1f:0f};
            // InputState.Change's updateType selects a write buffer; it does not change
            // the current update context. Editor-context action monitors ignore changes.
            // This method is therefore called only by the queued actual-player-update fixture.
            InputState.Change(device, value, InputUpdateType.Dynamic);
        }
        // D306 click harvest: CanHarvestNow = an LMB press on this frame may start one chunk pull (holding never repeats it)
        private static bool CanHarvest(PlayerMotor motor) => (bool)typeof(PlayerMotor).GetProperty("CanHarvestNow", MotorFlags).GetValue(motor);
        private static float CrouchTarget(PlayerMotor motor) => (float)typeof(PlayerMotor).GetField("_crouchTarget", MotorFlags).GetValue(motor);
        private static void Set(PlayerMotor motor, string field, object value) => typeof(PlayerMotor).GetField(field, MotorFlags).SetValue(motor, value);
        private static void Call(PlayerMotor motor, string method, params object[] arguments) => typeof(PlayerMotor).GetMethod(method, MotorFlags).Invoke(motor, arguments);
        private static void Add(MotorReport report, string name, bool pass, float measured, float limit, string detail = null)
        { report.checks.Add(new MotorCheck { name = name, status = pass ? "PASS" : "FAIL", measured = measured, limit = limit, detail = detail }); }
    }
}
