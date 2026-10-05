using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World
{
    /// <summary>Playtest-only read-only adapter for interaction text and ink-bottle motion.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldMacroPlaytestHudPresenter : MonoBehaviour
    {
        public HudController Hud;
        public WorldMacroPlaytestSession Session;
        public CharacterController Body;
        [Min(.1f)] public float MovementReferenceSpeed = 4.5f;

        private PlayerVitals _vitals;
        private bool _presenting;
        private Canvas _interactionCanvas;
        private Text _interactionLetter;
        private Font _ownedFont;
        private Material _letterMaterial;
        readonly RaycastHit[] _focusHits=new RaycastHit[32];
        readonly WorldInteractionOutline _outline=new WorldInteractionOutline();
        public int OutlineCount => _outline.ActiveCount;
        // #308 liquid HUD (SPEC-HUD-LIQUID-308 §5.1): what the player did (for the slosh, D308-11b), the three action marks and
        // their key glyphs, user settings. Instance state only; the vehicle and the bindings are read a few times per second,
        // everything else is a handful of property reads.
        private UI.ActionMeter308 _motion308;
        private UI.HudActionState308 _action308;
        private float _vehiclePoll308;
        private UI.UserSettingsService _settings308;
        private UI.HudKeys308 _keys308;
        private string _dodgePath308, _jumpPath308, _vehiclePath308;
        private bool _keysRead308, _seated308;
        private UnityEngine.InputSystem.InputAction _vehicleInput308;

        private void OnEnable() { Activate(); }
        private void Start() { Activate(); }

        private void Activate()
        {
            if (_presenting || Hud == null || !Hud.IsSkinned || Session == null || Body == null) return;
            _vitals = Body.GetComponent<PlayerVitals>();
            if (_vitals != null) _vitals.Damaged += OnDamaged;
            Session.SetCanvasHudPresenterActive(true);
            _presenting = true;
            Activate308();
        }

        // #304 (IMPLEMENTATION §7.1): the HUD prompt shows the session text in every theme (the icon-mode branch that nulled it is
        // gone). One text surface still holds: Present() already routes long text to the detail page, so CurrentHudText is the
        // prompt. The focused object keeps its outline; the world-space F letter only appears while the HUD prompt is hidden.
        // The death wake line is routed to the notice channel by WorldMacroPlayerDeath303 and so is kept off the prompt.
        // QA1 (after/prompt.png, item 3): read-only state of the last LateUpdate for `hud304-prompt` (editor) and captures.
        public enum LetterState304 { NotRun, NoCamera, NoFocus, HiddenByPrompt, LineOfSight, Offscreen, Shown }
        public LetterState304 LastLetterState304 { get; private set; }
        public string LastPromptText304 { get; private set; }
        public int LastPresentFrame304 { get; private set; } = -1;

        private void LateUpdate()
        {
            if (!_presenting) { Activate(); if (!_presenting) return; }
            string text = PromptText(Session);
            Hud.SetInteractionText(text);
            LastPromptText304 = text; LastPresentFrame304 = Time.frameCount;
            // the world F stays hidden only while the HUD prompt can really draw (a HUD canvas turned off by a menu or a capture
            // tool must not leave a focused object with no surface at all)
            UpdateWorldInteraction(!Hud.PromptOnScreen304);
            Vector3 local = Body.transform.InverseTransformDirection(Body.velocity);
            Hud.SetInkMotion(new Vector2(local.x, local.z) / Mathf.Max(.1f, MovementReferenceSpeed));
            Present308();
        }

        // ---------- #308 liquid HUD ----------
        private void Activate308()
        {
            _motion308.Clear(); _vehiclePoll308 = 0f; _action308 = default;
            _keys308 = UI.HudKeys308.Empty; _keysRead308 = false; _seated308 = false; _vehicleInput308 = null;
            _dodgePath308 = _jumpPath308 = _vehiclePath308 = null;
            if (Hud.Vessels308 == null) return;
            var motor = Session.Walker != null ? Session.Walker.Motor : null;
            if (motor != null && motor.Config != null) Hud.SetLowInkThreshold308(motor.Config.SpellInkCost);
            // the vehicle call has no Input System action today (the summon polls a key): if the map ever gets one, it is found here
            if (motor != null) _vehicleInput308 = UI.HudKeyBinding308.Find(motor.InputActions, motor.InputMapName, Hud.Vessels308.Profile.Marks.VehicleAction);
            BindSettings308();
        }

        private void BindSettings308()
        {
            var root = UI.PlaytestUiRoot.Instance;
            if (_settings308 != null || root == null || root.Settings == null) return;
            _settings308 = root.Settings;
            _settings308.Changed += OnSettings308;
            OnSettings308();
        }

        private void OnSettings308()
        {
            if (Hud == null || _settings308 == null) return;
            var current = _settings308.Current;   // a copy: read when the settings change, never per frame
            Hud.SetUserSettings308(current.UiScale, current.ReducedMotion);
        }

        private void Deactivate308()
        {
            if (_settings308 != null) _settings308.Changed -= OnSettings308;
            _settings308 = null; _vehicleInput308 = null;
        }

        private void Present308()
        {
            var vessels = Hud.Vessels308;
            if (vessels == null) return;
            if (_settings308 == null) BindSettings308();
            var profile = vessels.Profile;
            var walker = Session.Walker;
            var motor = walker != null ? walker.Motor : null;
            // D308-11b: the liquid answers what the PLAYER did, as measured - the displacement of the body over the motor's own
            // time step and the motor's jump / landing serials. No camera is read, on foot or riding (a camera moves with nobody
            // doing anything: rig sway, lock-on pull, collision, the draw-mode offset, a free look from the seat). A body that
            // stands still gives exactly zero. While riding, the seat carries the body with the car, so the car's own starts,
            // stops and swerves come in the same way; the heading is then the CAR's (the body's own yaw is frozen in the seat,
            // and the seat view turns with the mouse: read as the heading, a look round at speed sloshed the liquid).
            bool seated = walker != null && walker.Seated;
            var body = Body.transform;
            var heading = body;
            if (seated)
            {
                var seat = Session.VehicleSeat308;
                if (seat != null && seat.Occupied && seat.Vehicle != null) heading = seat.Vehicle.transform;
            }
            // boarding / leaving moves the body, and the seat places it in ITS LateUpdate (order 150, after this one): the frame
            // the flag flips is not measured at all, so the next frame is the new baseline whichever of the two ran first
            if (seated != _seated308) { _seated308 = seated; _motion308.Clear(); }
            else
            {
                var sample = _motion308.Sample(body.position, heading.right, heading.eulerAngles.y, Time.deltaTime,
                    motor != null ? motor.JumpSerial : 0, motor != null ? motor.LandingSerial : 0, motor != null ? motor.JumpLaunchSpeed : 0f, profile.Motion);
                Hud.AddLiquidAction308(in sample);
            }
            if (motor != null)
            {
                // the same predicates the input handlers use (PlayerMotor.DodgeGateOpen / JumpGateOpen)
                _action308.Seated = walker.Seated;
                _action308.HasDodge = motor.HasDodge;
                _action308.DodgeGateOpen = motor.DodgeGateOpen();
                _action308.Dodging = motor.IsDodging;
                _action308.DodgeCooldown01 = motor.DodgeCooldown01;
                _action308.HasJump = motor.HasJump;
                _action308.JumpGateOpen = motor.JumpGateOpen();
                _action308.Airborne = motor.IsAirborne;
            }
            _vehiclePoll308 -= Time.unscaledDeltaTime;
            if (_vehiclePoll308 <= 0f)
            {
                Session.ReadVehicleHud308(out _action308.Vehicle, out _action308.VehicleOutGlyph, out _action308.VehicleBusy01);
                ReadKeys308(profile, motor);
                // boss-field polygons are not tested every frame; a running call stroke is followed every frame
                _vehiclePoll308 = _action308.Vehicle == UI.VehicleHud308.Busy ? 0f : Mathf.Max(.02f, profile.Marks.VehiclePollSeconds);
            }
            Hud.SetActionState308(in _action308);
        }

        // D308-11b key glyphs (SPEC §3.5): the key each mark shows is the one REALLY bound - the control path of the binding of
        // the action the motor subscribes to (overrides included; the binding of the device used last). The vehicle call is not
        // an action yet, so its path is data (Marks.VehicleKeyPath) until the map has one. The paths are compared as strings
        // (no allocation); a cell is looked up again only when a path changed.
        private void ReadKeys308(UI.HudLiquid308ProfileSO profile, PlayerMotor motor)
        {
            var marks = profile.Marks;
            if (!marks.ShowKeys) return;
            bool pad = UI.HudKeyBinding308.GamepadUsedLast();
            string dodge = motor != null ? UI.HudKeyBinding308.Path(motor.DodgeInput, pad) : null;
            string jump = motor != null ? UI.HudKeyBinding308.Path(motor.JumpInput, pad) : null;
            string vehicle = _vehicleInput308 != null ? UI.HudKeyBinding308.Path(_vehicleInput308, pad) : marks.VehicleKeyPath;
            if (_keysRead308 && dodge == _dodgePath308 && jump == _jumpPath308 && vehicle == _vehiclePath308) return;
            _keysRead308 = true; _dodgePath308 = dodge; _jumpPath308 = jump; _vehiclePath308 = vehicle;
            _keys308.Dodge = UI.HudKeyGlyph308.Cell(dodge, marks.KeyGlyphs);
            _keys308.Jump = UI.HudKeyGlyph308.Cell(jump, marks.KeyGlyphs);
            _keys308.Vehicle = UI.HudKeyGlyph308.Cell(vehicle, marks.KeyGlyphs);
            Hud.SetKeys308(in _keys308);
        }

        /// <summary>#304: the text the HUD prompt shows for the session. The death wake line (WorldMacroPlayerDeath303 routes it
        /// to the notice channel) and the session lines PlaytestUiRoot forwards to the toast stack (Menu304RoutesToToast) are left
        /// out while they are the session's LastFeedback, so one line never shows as a toast and on the prompt at once. A save
        /// error keeps the prompt (session priority, unchanged). Harnesses compare the prompt's visibility with this.</summary>
        public static string PromptText(WorldMacroPlaytestSession session)
        {
            if (session == null) return null;
            string text = session.CurrentHudText;
            if (string.IsNullOrEmpty(text)) return text;
            var death = session.DeathPresentation;
            if (death != null && death.IsRoutedWakeLine(text)) return null;
            if (text == session.LastFeedback && text != session.SaveError && UI.PlaytestUiRoot.Menu304RoutesToToast(text)) return null;
            return text;
        }

        private void UpdateWorldInteraction(bool showLetter)
        {
            var camera = Session.Walker.ViewCamera;
            if (camera == null || !Session.TryGetFocusedInteractionBounds(out var bounds))
            {
                LastLetterState304 = camera == null ? LetterState304.NoCamera : LetterState304.NoFocus;
                _outline.Hide();
                if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
                return;
            }
            var point = new Vector3(bounds.center.x,bounds.max.y+Hud.Skin.InteractionLetterOffset+Hud.Skin.InteractionLetterHeight*.5f,bounds.center.z);
            _outline.Show(Session.FocusedRenderers,Hud.Skin);
            if (!showLetter)
            {
                LastLetterState304 = LetterState304.HiddenByPrompt;
                if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
                return;
            }
            var viewport = camera.WorldToViewportPoint(point);
            bool sight = FocusLineOfSight(camera,bounds);
            bool visible = sight && viewport.z > camera.nearClipPlane && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
            if (!visible)
            {
                LastLetterState304 = sight ? LetterState304.Offscreen : LetterState304.LineOfSight;
                if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
                return;
            }
            LastLetterState304 = LetterState304.Shown;
            if (_interactionCanvas == null)
            {
                var root = new GameObject("WorldInteractionPrompt", typeof(RectTransform), typeof(Canvas));
                root.transform.SetParent(transform, false);
                _interactionCanvas = root.GetComponent<Canvas>();
                _interactionCanvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(80, 80);
                var letter = new GameObject("F", typeof(RectTransform), typeof(Text));
                letter.transform.SetParent(root.transform, false);
                _interactionLetter = letter.GetComponent<Text>();
                _interactionLetter.font = Hud.Skin.KoreanFont;
                if (_interactionLetter.font == null)
                {
                    _ownedFont = Font.CreateDynamicFontFromOSFont("Arial", 64);
                    _interactionLetter.font = _ownedFont;
                }
                _interactionLetter.text = "F";
                var shader=Resources.Load<Shader>("Interaction/WorldLetter");
                if(shader!=null){_letterMaterial=new Material(shader){hideFlags=HideFlags.DontSave};_interactionLetter.material=_letterMaterial;}
                var shadow=letter.AddComponent<Shadow>();shadow.effectColor=new Color(.10f,.10f,.08f,.85f);shadow.effectDistance=new Vector2(1,-1);
                _interactionLetter.fontSize = 64;
                _interactionLetter.horizontalOverflow = HorizontalWrapMode.Overflow;
                _interactionLetter.verticalOverflow = VerticalWrapMode.Overflow;
                _interactionLetter.alignment = TextAnchor.MiddleCenter;
                _interactionLetter.raycastTarget = false;
                _interactionLetter.rectTransform.sizeDelta = rect.sizeDelta;
            }
            _interactionCanvas.worldCamera = camera;
            _interactionCanvas.transform.SetPositionAndRotation(point, camera.transform.rotation);
            _interactionCanvas.transform.localScale = Vector3.one * (Hud.Skin.InteractionLetterHeight / 64f);
            _interactionLetter.color = Hud.Skin.InteractionLetterColor;
            _interactionCanvas.gameObject.SetActive(true);
        }

        private bool FocusLineOfSight(Camera camera,Bounds bounds)
        {
            Vector3 delta=bounds.center-camera.transform.position;
            var visibleTarget=bounds;visibleTarget.Expand(.12f);
            int count=Physics.RaycastNonAlloc(camera.transform.position,delta.normalized,_focusHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++){
                var t=_focusHits[i].transform;
                if(t.root==Body.transform.root)continue;
                bool target=visibleTarget.Contains(_focusHits[i].point);
                foreach(var r in Session.FocusedRenderers)if(r!=null&&(t==r.transform||t.IsChildOf(r.transform))){target=true;break;}
                if(!target)return false;
            }
            return count<_focusHits.Length;
        }

        private void OnDamaged(float amount)
        {
            Hud.KickInk(.25f + Mathf.Clamp01(amount / 25f) * .75f);
        }

        private void OnDisable() { Deactivate(); }
        private void OnDestroy()
        {
            Deactivate();
            _outline.Dispose();
            if (_ownedFont != null) Destroy(_ownedFont);
            if (_letterMaterial != null) Destroy(_letterMaterial);
            if (_interactionCanvas != null) Destroy(_interactionCanvas.gameObject);
        }

        private void Deactivate()
        {
            _outline.Hide();
            if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
            if (!_presenting) return;
            if (_vitals != null) _vitals.Damaged -= OnDamaged;
            if (Hud != null)
            {
                Hud.SetInteractionText(null);
                Hud.SetInkMotion(Vector2.zero);
            }
            if (Session != null) Session.SetCanvasHudPresenterActive(false);
            Deactivate308();
            _vitals = null;
            _presenting = false;
        }
    }
}
