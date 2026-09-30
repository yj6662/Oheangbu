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

        private void OnEnable() { Activate(); }
        private void Start() { Activate(); }

        private void Activate()
        {
            if (_presenting || Hud == null || !Hud.IsSkinned || Session == null || Body == null) return;
            _vitals = Body.GetComponent<PlayerVitals>();
            if (_vitals != null) _vitals.Damaged += OnDamaged;
            Session.SetCanvasHudPresenterActive(true);
            _presenting = true;
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
            _vitals = null;
            _presenting = false;
        }
    }
}
