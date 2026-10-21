using Oheangbu.Presentation;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-127 debate 16 (TEST): the horn bow the fire form makes of the whole great brush. Presentation only: rules never
    // read it; the arrow's judgement is SwordFormEffect308's clock (the flying arrow only keeps the same time).
    //   parts   Bow_Grip, Bow_UpperIn -> Bow_UpperOut -> Bow_NockTop, Bow_LowerIn -> Bow_LowerOut -> Bow_NockBottom, ArrowRest308
    //           (build_heads308.py). Each limb bends as two rigid segments turned about their own roots, so the bend is shared
    //           along the limb instead of one hinge (Codex).
    //   body    after the gesture rig (order 1000, which the animator and the rig's pose save rewrite every frame): the left
    //           arm reaches along the body's facing and holds the bow upright; the right hand comes up to the string while
    //           drawing and pulls it to the cheek. Weight = how far the head has grown, so the stance eases in and out with it.
    //   string  three points (top nock, the pull point, bottom nock). The nocked arrow is revealed at the start of the draw and
    //           is ArrowLength once there: it never stretches with the pull (Codex).
    //   shot    Shoot flies a BowArrow308 to the target over the rule's flight time; the nocked arrow is gone at once.
    [DefaultExecutionOrder(1050)]
    public sealed class HornBow308 : MonoBehaviour
    {
        public const string RestName = "ArrowRest308", StringName = "Bow_String", NockedName = "Bow_Nocked", NockedHeadName = "Bow_NockedHead";

        [SerializeField] private Material _arrowInk, _arrowHead;
        [Tooltip("deg each limb segment turns at full draw (inner, outer)")]
        [SerializeField, Min(0f)] private float _bendIn = 10f, _bendOut = 7f;
        [Tooltip("m the string comes back at full draw when there is no body to pull it")]
        [SerializeField, Min(0f)] private float _pull = .62f;
        [SerializeField, Min(.1f)] private float _arrowLength = 1.2f;
        [Tooltip("m: the nocked arrow and the flying one (>= .08 m reads 3 px at 15 m, D308-127)")]
        [SerializeField, Min(.005f)] private float _nockedWidth = .05f, _flightWidth = .08f;
        [SerializeField, Min(0f)] private float _tail = .6f, _headLength = .12f;
        [SerializeField, Min(.001f)] private float _stringWidth = .007f;
        [Tooltip("deg the bow leans, top away from the face")]
        [SerializeField, Range(0f, 30f)] private float _cant = 8f;

        private Transform _upperIn, _upperOut, _lowerIn, _lowerOut, _nockTop, _nockBottom, _rest, _grip;
        private Quaternion _upperInRest, _upperOutRest, _lowerInRest, _lowerOutRest;
        private float _signUpper = 1f, _signLower = 1f;
        private Vector3 _upLocal, _archerLocal;
        private LineRenderer _string, _nocked, _nockedHead;
        private WorldMacroPlayerGestureRig _rig;
        private GreatBrushWeapon308 _head;
        private bool _bound;
        private float _drawn, _right;

        public float Drawn => _drawn;
        public bool Bound => _bound;
        public Transform Rest => _rest;
        public Transform NockTop => _nockTop;
        public Transform NockBottom => _nockBottom;
        public LineRenderer String => _string;
        public LineRenderer Nocked => _nocked;
        public BowArrow308 LastArrow { get; private set; }
        /// <summary>How much of the nocked arrow shows now (m; 0 = none).</summary>
        public float NockedShown { get; private set; }
        public float ArrowLength => _arrowLength;
        public float FlightWidth => _flightWidth;

        // The editor build: the two arrow materials and the three lines (made once into the prefab, so they belong to the head's renderers).
        public void Configure(Material arrowInk, Material arrowHead)
        {
            _arrowInk = arrowInk; _arrowHead = arrowHead;
            _string = EnsureLine(StringName, _arrowInk, 3, _stringWidth);
            _nocked = EnsureLine(NockedName, _arrowInk, 2, _nockedWidth);
            _nockedHead = EnsureLine(NockedHeadName, _arrowHead, 2, _nockedWidth);
        }

        // The rig whose body draws the bow (null = no body: the bow stays where the head put it and pulls its own string).
        public bool Bind(WorldMacroPlayerGestureRig rig)
        {
            _rig = rig; _head = GetComponent<GreatBrushWeapon308>();
            _grip = Find(transform, "Bow_Grip"); _rest = Find(transform, RestName);
            _upperIn = Find(transform, "Bow_UpperIn"); _upperOut = Find(transform, "Bow_UpperOut");
            _lowerIn = Find(transform, "Bow_LowerIn"); _lowerOut = Find(transform, "Bow_LowerOut");
            _nockTop = Find(transform, "Bow_NockTop"); _nockBottom = Find(transform, "Bow_NockBottom");
            _string = Find(transform, StringName)?.GetComponent<LineRenderer>(); _nocked = Find(transform, NockedName)?.GetComponent<LineRenderer>();
            _nockedHead = Find(transform, NockedHeadName)?.GetComponent<LineRenderer>();
            _bound = _grip != null && _upperIn != null && _upperOut != null && _lowerIn != null && _lowerOut != null && _nockTop != null && _nockBottom != null;
            if (!_bound) return false;
            _upperInRest = _upperIn.localRotation; _upperOutRest = _upperOut.localRotation; _lowerInRest = _lowerIn.localRotation; _lowerOutRest = _lowerOut.localRotation;
            // the bow's own axes in this root's space, from the parts (the FBX turn of the model never matters)
            Vector3 top = transform.InverseTransformPoint(_nockTop.position), bottom = transform.InverseTransformPoint(_nockBottom.position);
            Vector3 grip = transform.InverseTransformPoint(_grip.position);
            _upLocal = (top - bottom).normalized;
            _archerLocal = Vector3.ProjectOnPlane((top + bottom) * .5f - grip, _upLocal).normalized;
            // which way each limb turns so its nock comes toward the archer
            Vector3 width = transform.TransformDirection(Vector3.Cross(_upLocal, _archerLocal)), archer = transform.TransformDirection(_archerLocal);
            _signUpper = Toward(_upperIn.position, _nockTop.position, width, archer);
            _signLower = Toward(_lowerIn.position, _nockBottom.position, width, archer);
            Apply();
            return true;
        }

        // The draw from the rule's Tick cue: 0..1, negative = the draw ended without an arrow (fizzle, cancel).
        public void Draw(float drawn01) => _drawn = drawn01 > 0f ? Mathf.Clamp01(drawn01) : 0f;

        // One arrow leaves (the rule's Secondary cue): to the target (null = no target, it flies to point and scatters) in flight seconds.
        public BowArrow308 Shoot(Transform target, Vector3 point, float flight, float now = float.NaN)
        {
            Vector3 from = _rest != null ? _rest.position : transform.position;
            _drawn = 0f;
            if (_nocked != null) _nocked.enabled = false;
            if (_nockedHead != null) _nockedHead.enabled = false;
            LastArrow = BowArrow308.Launch(from, target, point, flight, _arrowInk, _arrowHead, _flightWidth, _arrowLength, _headLength, _tail, gameObject.layer, now);
            return LastArrow;
        }

        private void LateUpdate() => Apply();

        // The pose, the bend, the string and the nocked arrow for this frame (checks call it themselves).
        public void Apply()
        {
            if (!_bound || this == null) return;
            float weight = _head != null ? Mathf.Clamp01(_head.Grown) : 1f;
            float dt = Application.isPlaying ? Time.deltaTime : 1f;
            _right = Mathf.MoveTowards(_right, _drawn > 0f ? 1f : 0f, dt * 8f);
            Bend(0f);
            Transform rh = null;
            if (_rig != null && weight > 0f && _rig.WorldArms308(out var root, out var ru, out var rf, out rh, out var lu, out var lf, out var lh))
            {
                Vector3 up = root.up, aim = Vector3.ProjectOnPlane(root.forward, up).normalized, right = Vector3.Cross(up, aim);
                float reach = Vector3.Distance(lu.position, lf.position) + Vector3.Distance(lf.position, lh.position);
                PlayerVisualIK.Solve(lu, lf, lh, lu.position + aim * reach * .92f + up * .03f, lu.position - right * .25f - up * .3f, lh.rotation, weight);
                // the bow in the left fist: upright, leaning a little outward, its back toward the aim
                Vector3 bowUp = Quaternion.AngleAxis(_cant, aim) * up;
                Quaternion turn = Quaternion.LookRotation(-aim, bowUp) * Quaternion.Inverse(Quaternion.LookRotation(_archerLocal, _upLocal));
                transform.rotation = Quaternion.Slerp(transform.rotation, turn, weight);
                transform.position += (lh.position + aim * .015f - _grip.position) * weight;
                Bend(_drawn);
                if (_right > 0f)
                {
                    Vector3 rest = (_nockTop.position + _nockBottom.position) * .5f;
                    Vector3 cheek = ru.position + up * .16f + aim * .06f - right * .05f;
                    PlayerVisualIK.Solve(ru, rf, rh, Vector3.Lerp(rest, cheek, Mathf.SmoothStep(0f, 1f, _drawn)), ru.position + right * .3f - up * .05f - aim * .25f,
                        rh.rotation, weight * _right);
                }
            }
            else Bend(_drawn);
            Lines(rh);
        }

        // Both segments of both limbs turn toward the archer by the draw.
        private void Bend(float drawn)
        {
            _upperIn.localRotation = _upperInRest; _upperOut.localRotation = _upperOutRest; _lowerIn.localRotation = _lowerInRest; _lowerOut.localRotation = _lowerOutRest;
            if (drawn <= 0f) return;
            float k = Mathf.SmoothStep(0f, 1f, drawn);
            Vector3 width = transform.TransformDirection(Vector3.Cross(_upLocal, _archerLocal));
            _upperIn.Rotate(width, _signUpper * _bendIn * k, Space.World); _upperOut.Rotate(width, _signUpper * _bendOut * k, Space.World);
            _lowerIn.Rotate(width, _signLower * _bendIn * k, Space.World); _lowerOut.Rotate(width, _signLower * _bendOut * k, Space.World);
        }

        private void Lines(Transform rightHand)
        {
            Vector3 top = _nockTop.position, bottom = _nockBottom.position, middle = (top + bottom) * .5f;
            Vector3 archer = transform.TransformDirection(_archerLocal);
            Vector3 pull = rightHand != null && _right > 0f ? Vector3.Lerp(middle, rightHand.position, _right) : middle + archer * _pull * Mathf.SmoothStep(0f, 1f, _drawn);
            float dry = _head != null ? _head.Dryness : 0f;
            Color tint = new Color(1f, 1f, 1f, 1f - .5f * dry);   // the dried string thins out but stays readable to the end (Codex)
            if (_string != null)
            {
                _string.useWorldSpace = true; _string.positionCount = 3;
                _string.SetPosition(0, top); _string.SetPosition(1, pull); _string.SetPosition(2, bottom);
                _string.startColor = _string.endColor = tint;
            }
            bool nocked = _drawn > 0f;
            Vector3 rest = _rest != null ? _rest.position : transform.position;
            Vector3 along = (rest - pull).sqrMagnitude > 1e-6f ? (rest - pull).normalized : -archer;
            float shown = _arrowLength * Mathf.Clamp01(_drawn / .25f);   // revealed over the first quarter of the draw, then fixed
            NockedShown = nocked ? shown : 0f;
            Vector3 tip = pull + along * shown, neck = pull + along * Mathf.Max(0f, shown - _headLength);
            if (_nocked != null) { _nocked.enabled = nocked; _nocked.useWorldSpace = true; _nocked.positionCount = 2; _nocked.SetPosition(0, neck); _nocked.SetPosition(1, pull); _nocked.startColor = _nocked.endColor = tint; }
            if (_nockedHead != null) { _nockedHead.enabled = nocked && shown >= _headLength; _nockedHead.useWorldSpace = true; _nockedHead.positionCount = 2; _nockedHead.SetPosition(0, tip); _nockedHead.SetPosition(1, neck); }
        }

        private static float Toward(Vector3 pivot, Vector3 nock, Vector3 axis, Vector3 archer)
        {
            Vector3 v = nock - pivot;
            return Vector3.Dot(Quaternion.AngleAxis(1f, axis) * v - v, archer) >= 0f ? 1f : -1f;
        }

        private LineRenderer EnsureLine(string name, Material material, int points, float width)
        {
            var found = Find(transform, name);
            var line = found != null ? found.GetComponent<LineRenderer>() : null;
            if (line == null) { var go = new GameObject(name); go.transform.SetParent(transform, false); line = go.AddComponent<LineRenderer>(); }
            BowArrow308.Setup(line, material, points, width);
            line.enabled = name == StringName;
            return line;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
