"""Apply the explicitly approved C crouch / X sit runtime changes."""
from pathlib import Path
import json,uuid
root=Path(__file__).resolve().parents[2]
scripts=root/'Oheangbu/Assets/_Project/Scripts'
def edit(path,changes):
 p=scripts/path;s=p.read_text(encoding='utf-8-sig')
 for a,b in changes:
  assert a in s,(path,a[:100]);s=s.replace(a,b)
 p.write_text(s,encoding='utf-8')
edit(Path('Combat/Player/PlayerLocomotionProfileSO.cs'),[("public float RunSpeed = 5.5f;","public float RunSpeed = 5.5f;\n        [Min(.1f)] public float CrouchSpeed = 1.2f;\n        public float CrouchingHeight = 1.25f, CrouchingEyeHeight = 1.1f, CrouchTransitionSeconds = .22f;")])
edit(Path('Combat/Player/PlayerMotor.Locomotion.cs'),[
 ('_sitAction, _locomotionDrawAction','_sitAction, _crouchAction, _locomotionDrawAction'),
 ('_standingHeight, _posture, _sitTarget;','_standingHeight, _posture, _sitTarget, _crouch, _crouchTarget;\n        private float _actualYawSpeed;'),
 ('public float Posture01 => _posture;','public float Posture01 => _posture;\n        public bool IsCrouching => HasLocomotion && (_crouch > .001f || _crouchTarget > .5f);\n        public float Crouch01 => _crouch;\n        public float ActualYawSpeed => _actualYawSpeed;\n        public bool CanStandForBoarding => !HasLocomotion || (!IsSitting && CanStand());\n        public bool PrepareForBoarding() { if (!CanStandForBoarding) return false; _crouch = _crouchTarget = 0; ApplyPostureShape(); return true; }'),
 ('IsLocomotionGrounded && !IsSitting && !_drawing','IsLocomotionGrounded && !IsSitting && !IsCrouching && !_drawing'),
 ('_sitAction = map?.FindAction("Sit");','_sitAction = map?.FindAction("Sit"); _crouchAction = map?.FindAction("Crouch");'),
 ('if (_sitAction != null) _sitAction.performed += OnSit;','if (_sitAction != null) _sitAction.performed += OnSit;\n            if (_crouchAction != null) _crouchAction.performed += OnCrouch;'),
 ('if (_sitAction != null) _sitAction.performed -= OnSit;','if (_sitAction != null) _sitAction.performed -= OnSit;\n            if (_crouchAction != null) _crouchAction.performed -= OnCrouch;'),
 ('_sitTarget = _posture = 0f;','_sitTarget = _posture = _crouch = _crouchTarget = _actualYawSpeed = 0f;'),
 ('!IsLocomotionGrounded || IsSitting || IsDodging || _drawing','!IsLocomotionGrounded || IsSitting || IsCrouching || IsDodging || _drawing'),
 ('private void OnSit(InputAction.CallbackContext _)','private void OnCrouch(InputAction.CallbackContext _)\n        {\n            if (!ActionAllowed || !IsLocomotionGrounded || IsSitting || IsDodging || _drawing\n                || (_locomotionDrawAction != null && _locomotionDrawAction.IsPressed())\n                || (_harvest != null && _harvest.IsExtracting) || (_harvestAction != null && _harvestAction.IsPressed())) return;\n            if (_crouchTarget > .5f) { if (CanStand()) _crouchTarget = 0; }\n            else _crouchTarget = 1;\n        }\n        private void OnSit(InputAction.CallbackContext _)'),
 ('if (!ActionAllowed) return;\n            if (IsSitting)','if (!ActionAllowed || IsCrouching) return;\n            if (IsSitting)'),
 ('ApplyPostureShape();\n\n            Vector3 direction','float crouchTarget = _crouchTarget;\n            if (crouchTarget < _crouch && !CanStand()) { crouchTarget = _crouch; _crouchTarget = 1; }\n            _crouch = Mathf.MoveTowards(_crouch, crouchTarget, dt / Mathf.Max(.05f, _locomotion.CrouchTransitionSeconds));\n            ApplyPostureShape();\n\n            Vector3 direction'),
 ('float speed = IsSprinting ?','float speed = IsCrouching ? _locomotion.CrouchSpeed : IsSprinting ?'),
 ('IsLocomotionGrounded && !IsSitting && _dodge','IsLocomotionGrounded && !IsSitting && !IsCrouching && _dodge'),
 ('Mathf.Lerp(_standingHeight, Mathf.Max(_controller.radius * 2f, _locomotion.SittingHeight), _posture)','Mathf.Lerp(Mathf.Lerp(_standingHeight, Mathf.Max(_controller.radius * 2f, _locomotion.CrouchingHeight), _crouch), Mathf.Max(_controller.radius * 2f, _locomotion.SittingHeight), _posture)'),
 ('Vector3.Lerp(_standingEye, new Vector3(_standingEye.x, _locomotion.SittingEyeHeight, _standingEye.z), _posture)','Vector3.Lerp(Vector3.Lerp(_standingEye, new Vector3(_standingEye.x, _locomotion.CrouchingEyeHeight, _standingEye.z), _crouch), new Vector3(_standingEye.x, _locomotion.SittingEyeHeight, _standingEye.z), _posture)'),
 ])
edit(Path('Combat/Player/PlayerMotor.cs'),[
 ('ApplyLockOnPull();','float previousYaw = transform.eulerAngles.y;\n            ApplyLockOnPull();\n            if (HasLocomotion && Time.deltaTime > 0) _actualYawSpeed = Mathf.DeltaAngle(yawBeforeInput, transform.eulerAngles.y) / Time.deltaTime;'),
 ('// 시점 — 작도 중에는','float yawBeforeInput = transform.eulerAngles.y;\n            // 시점 — 작도 중에는'),
 ('!IsLocomotionGrounded || IsSitting))','!IsLocomotionGrounded || IsSitting || IsCrouching))')])
edit(Path('App/World/WorldMacroCombatWalker.cs'),[('&&Motor.CanBeginDrawing;','&&Motor.CanBeginDrawing&&Motor.CanStandForBoarding;')])
edit(Path('App/World/Vehicle/WorldMacroPalanquinSeat.cs'),[('entryFeet = WalkBody.transform.position; entryYaw = WalkBody.transform.eulerAngles.y;','if (CombatWalker != null && !CombatWalker.Motor.PrepareForBoarding()) return false;\n            entryFeet = WalkBody.transform.position; entryYaw = WalkBody.transform.eulerAngles.y;')])
edit(Path('App/World/UI/GameplayUiGate.cs'),[('keyboard.cKey.isPressed','keyboard.cKey.isPressed || keyboard.xKey.isPressed')])
p=root/'Oheangbu/Assets/InputSystem_Actions.inputactions';data=json.loads(p.read_text())
m=next(m for m in data['maps'] if m['name']=='Gameplay')
for b in m['bindings']:
 if b['action']=='Sit' and b['path']=='<Keyboard>/c':b['path']='<Keyboard>/x'
if not any(a['name']=='Crouch' for a in m['actions']):
 a=next(a for a in m['actions'] if a['name']=='Sit').copy();a.update(name='Crouch',id=str(uuid.uuid4()));m['actions'].append(a)
 b=next(b for b in m['bindings'] if b['action']=='Sit').copy();b.update(action='Crouch',id=str(uuid.uuid4()),path='<Keyboard>/c');m['bindings'].append(b)
p.write_text(json.dumps(data,ensure_ascii=False,indent=4),encoding='utf-8')
print('Crouch runtime and input applied; controller clips must be authored before acceptance.')
