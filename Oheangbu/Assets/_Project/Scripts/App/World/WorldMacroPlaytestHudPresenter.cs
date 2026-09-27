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

        private void LateUpdate()
        {
            if (!_presenting) { Activate(); if (!_presenting) return; }
            if (Hud.UsesIcons)
            {
                Hud.SetInteractionIcon(null);
                Hud.SetInteractionText(null);
                UpdateWorldInteraction();
            }
            else
            {
                _outline.Hide();
                if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
                Hud.SetInteractionText(Session.CurrentHudText);
            }
            Vector3 local = Body.transform.InverseTransformDirection(Body.velocity);
            Hud.SetInkMotion(new Vector2(local.x, local.z) / Mathf.Max(.1f, MovementReferenceSpeed));
        }

        private void UpdateWorldInteraction()
        {
            var camera = Session.Walker.ViewCamera;
            if (camera == null || !Session.TryGetFocusedInteractionBounds(out var bounds))
            {
                _outline.Hide();
                if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
                return;
            }
            var point = new Vector3(bounds.center.x,bounds.max.y+Hud.Skin.InteractionLetterOffset+Hud.Skin.InteractionLetterHeight*.5f,bounds.center.z);
            _outline.Show(Session.FocusedRenderers,Hud.Skin);
            var viewport = camera.WorldToViewportPoint(point);
            bool visible = FocusLineOfSight(camera,bounds) && viewport.z > camera.nearClipPlane && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
            if (!visible)
            {
                if (_interactionCanvas != null) _interactionCanvas.gameObject.SetActive(false);
                return;
            }
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
