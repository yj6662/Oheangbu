using System;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public enum CheongryongTrailAdvance { Invalid, Stationary, Moved, Teleport }

    /// <summary>Fixed-capacity world-space arc-length history. Does not touch scene objects.</summary>
    public sealed class CheongryongArcLengthHistory
    {
        private readonly Vector3[] _points;
        private readonly float _spacing, _maximumLength, _teleportDistance;
        private int _newest, _count;
        private Vector3 _live;
        public int Count => _count;
        public int Capacity => _points.Length;
        public Vector3 Head => _live;
        public float StoredLength
        {
            get { float result = 0; for (int i = 1; i < _count; i++) result += Vector3.Distance(At(i - 1), At(i)); return result; }
        }

        public CheongryongArcLengthHistory(int capacity = 512, float spacing = .035f, float maximumLength = 24f, float teleportDistance = 2.5f)
        {
            if (capacity < 2 || capacity > 2048 || !Finite(spacing) || spacing <= 0 ||
                !Finite(maximumLength) || maximumLength <= spacing || !Finite(teleportDistance) || teleportDistance <= spacing)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Finite positive distances and capacity 2..2048 required.");
            _points = new Vector3[capacity]; _spacing = spacing; _maximumLength = maximumLength; _teleportDistance = teleportDistance;
        }

        public bool Reset(Vector3[] headToTail)
        {
            if (headToTail == null || headToTail.Length == 0) return false;
            for (int i = 0; i < headToTail.Length; i++) if (!Finite(headToTail[i])) return false;
            _count = 0; _newest = 0;
            int last = Mathf.Min(headToTail.Length, Capacity) - 1;
            for (int i = last; i >= 0; i--)
                if (_count == 0 || (headToTail[i] - At(0)).sqrMagnitude > 1e-12f) Push(headToTail[i]);
            _live = headToTail[0]; Trim(); return true;
        }

        public CheongryongTrailAdvance Advance(Vector3 position)
        {
            if (!Finite(position) || _count == 0) return CheongryongTrailAdvance.Invalid;
            if (Vector3.Distance(_live, position) > _teleportDistance) return CheongryongTrailAdvance.Teleport;
            bool moved = (position - _live).sqrMagnitude > 1e-12f; _live = position;
            if (Vector3.Distance(At(0), position) >= _spacing) { Push(position); Trim(); }
            return moved ? CheongryongTrailAdvance.Moved : CheongryongTrailAdvance.Stationary;
        }

        public Vector3 Sample(float distance, Vector3 fallbackDirection)
        {
            if (_count == 0 || !Finite(distance)) return _count == 0 ? Vector3.zero : _live;
            float remaining = Mathf.Max(0, distance); Vector3 previous = _live;
            Vector3 direction = SafeDirection(fallbackDirection, Vector3.back);
            for (int i = 0; i < _count; i++)
            {
                Vector3 point = At(i), delta = point - previous; float length = delta.magnitude;
                if (length > 1e-6f)
                {
                    direction = delta / length;
                    if (remaining <= length) return previous + direction * remaining;
                    remaining -= length;
                }
                previous = point;
            }
            return previous + direction * remaining;
        }

        // Projection uses Euclidean bind lengths even where polyline arc distances
        // produce shorter chords. It deliberately does not model self-collision.
        public void Solve(Vector3 anchor, float[] lengths, Vector3[] fallbackDirections, Vector3[] output)
        {
            if (!Finite(anchor) || lengths == null || fallbackDirections == null || output == null ||
                fallbackDirections.Length < lengths.Length || output.Length < lengths.Length + 1)
                throw new ArgumentException("Valid anchor and matching preallocated arrays required.");
            for (int i = 0; i < lengths.Length; i++)
                if (!Finite(lengths[i]) || lengths[i] <= 0) throw new ArgumentException("Segment lengths must be finite and positive.");
            output[0] = anchor; float arc = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                arc += lengths[i]; Vector3 target = Sample(arc, fallbackDirections[i]);
                Vector3 direction = SafeDirection(target - output[i], fallbackDirections[i]);
                output[i + 1] = output[i] + direction * lengths[i];
            }
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
        {
            if (Finite(direction) && direction.sqrMagnitude > 1e-12f) return direction.normalized;
            return Finite(fallback) && fallback.sqrMagnitude > 1e-12f ? fallback.normalized : Vector3.back;
        }
        private Vector3 At(int age) => _points[(_newest + age) % Capacity];
        private void Push(Vector3 point) { _newest = (_newest + Capacity - 1) % Capacity; _points[_newest] = point; _count = Mathf.Min(_count + 1, Capacity); }
        private void Trim()
        {
            float length = StoredLength;
            while (_count > 2 && length > _maximumLength)
            { length -= Vector3.Distance(At(_count - 2), At(_count - 1)); _count--; }
            if (_count == 2 && length > _maximumLength)
                _points[(_newest + 1) % Capacity] = At(0) + SafeDirection(At(1) - At(0), Vector3.back) * _maximumLength;
        }
    }

    /// <summary>
    /// Presentation only. Configure on the imported bind pose before Animator runs.
    /// Head remains Animator-owned; paw descendants inherit their body's solved pose.
    /// No physics, collision, terrain IK, root movement, or damage is performed here.
    /// Uniform model scale is supported; animated/nonuniform ancestor scale is not.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class CheongryongBodyFollow : MonoBehaviour
    {
        [SerializeField] private Transform _head;
        [SerializeField] private Transform[] _orderedBody;
        [SerializeField] private Transform _tail;
        [SerializeField, Min(.001f)] private float _sampleSpacing = .035f;
        [SerializeField, Min(.1f)] private float _teleportDistance = 2.5f;
        [SerializeField, Range(64, 2048)] private int _historyCapacity = 512;
        private Transform _reference;
        private Vector3 _headAnchorLocal;
        private Vector3[] _bindPoints, _fallback, _solved, _seed;
        private Quaternion[] _bindRotations;
        private float[] _lengths;
        private CheongryongArcLengthHistory _history;
        private bool _needsReset;
        public bool IsConfigured { get; private set; }
        public int HistoryCount => _history != null ? _history.Count : 0;
        public float MaximumSegmentLengthError { get; private set; }
        public int TeleportResetCount { get; private set; }

        private void Awake() { if (!IsConfigured) TryAutoConfigure(); }
        private void OnEnable() { _needsReset = true; }
        private void OnDisable() { _needsReset = true; }

        public bool TryAutoConfigure()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            Transform head = _head, tail = _tail;
            var body = new Transform[24];
            foreach (Transform item in all)
            {
                if (item.name == "Head") head = item;
                else if (item.name == "TailTip") tail = item;
                else if (item.name.StartsWith("Body_", StringComparison.Ordinal) &&
                    int.TryParse(item.name.Substring(5), out int index) && index >= 1 && index <= 24) body[index - 1] = item;
            }
            return Configure(head, _orderedBody != null && _orderedBody.Length >= 2 ? _orderedBody : body, tail);
        }

        public bool Configure(Transform head, Transform[] orderedBody, Transform tail)
        {
            IsConfigured = false; _history = null; MaximumSegmentLengthError = 0;
            if (head == null || head.parent == null || orderedBody == null || orderedBody.Length < 2 || orderedBody.Length > 128) return false;
            for (int i = 0; i < orderedBody.Length; i++)
                if (orderedBody[i] == null || orderedBody[i].parent != (i == 0 ? head : orderedBody[i - 1])) return false;
            if (tail != null && !tail.IsChildOf(orderedBody[orderedBody.Length - 1])) return false;
            _head = head; _tail = tail; _orderedBody = (Transform[])orderedBody.Clone(); _reference = head.parent;
            int count = _orderedBody.Length;
            _bindPoints = new Vector3[count + 1]; _bindRotations = new Quaternion[count];
            _fallback = new Vector3[count]; _lengths = new float[count]; _solved = new Vector3[count + 1]; _seed = new Vector3[count + 1];
            _headAnchorLocal = head.InverseTransformPoint(_orderedBody[0].position);
            for (int i = 0; i < count; i++)
            {
                _bindPoints[i] = _reference.InverseTransformPoint(_orderedBody[i].position);
                _bindRotations[i] = Quaternion.Inverse(_reference.rotation) * _orderedBody[i].rotation;
            }
            _bindPoints[count] = tail != null ? _reference.InverseTransformPoint(tail.position) :
                _bindPoints[count - 1] * 2 - _bindPoints[count - 2];
            float total = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 delta = _reference.TransformVector(_bindPoints[i + 1] - _bindPoints[i]);
                if (!CheongryongArcLengthHistory.Finite(delta) || delta.magnitude < 1e-5f) return false;
                total += delta.magnitude;
            }
            float spacing = CheongryongArcLengthHistory.Finite(_sampleSpacing) ? Mathf.Max(.001f, _sampleSpacing) : .035f;
            float teleport = CheongryongArcLengthHistory.Finite(_teleportDistance) ? Mathf.Max(spacing * 2, _teleportDistance) : 2.5f;
            // Ensure a full body length remains available even at configured capacity.
            int capacity = Mathf.Clamp(_historyCapacity, 64, 2048);
            spacing = Mathf.Max(spacing, total * 1.5f / (capacity - 2));
            _history = new CheongryongArcLengthHistory(capacity, spacing, Mathf.Max(total * 2, spacing * 2), Mathf.Max(teleport, spacing * 2));
            IsConfigured = true; ResetPoseHistory(); return true;
        }

        public void ResetPoseHistory()
        {
            if (!IsConfigured || _head == null || _reference == null) return;
            Vector3 anchor = _head.TransformPoint(_headAnchorLocal);
            Vector3 correction = anchor - _reference.TransformPoint(_bindPoints[0]);
            for (int i = 0; i < _seed.Length; i++) _seed[i] = _reference.TransformPoint(_bindPoints[i]) + correction;
            _history.Reset(_seed); MaximumSegmentLengthError = 0; _needsReset = false;
        }

        private void LateUpdate() { EvaluateFollow(); }

        // Explicit evaluation also supports isolated Editor checks without entering Play mode.
        public void EvaluateFollow()
        {
            if (!IsConfigured || _head == null || _reference == null) return;
            for (int i = 0; i < _orderedBody.Length; i++) if (_orderedBody[i] == null) { IsConfigured = false; return; }
            if (_needsReset) ResetPoseHistory();
            Vector3 anchor = _head.TransformPoint(_headAnchorLocal);
            CheongryongTrailAdvance step = _history.Advance(anchor);
            if (step == CheongryongTrailAdvance.Invalid) return;
            if (step == CheongryongTrailAdvance.Teleport) { TeleportResetCount++; ResetPoseHistory(); }
            for (int i = 0; i < _lengths.Length; i++)
            {
                _fallback[i] = _reference.TransformVector(_bindPoints[i + 1] - _bindPoints[i]);
                _lengths[i] = _fallback[i].magnitude;
                if (!CheongryongArcLengthHistory.Finite(_lengths[i]) || _lengths[i] < 1e-5f) return;
            }
            _history.Solve(anchor, _lengths, _fallback, _solved);
            // Solve every target first, then write world transforms in parent order.
            // Descendants receive one inherited transform and then their own absolute target.
            for (int i = 0; i < _orderedBody.Length; i++)
            {
                Quaternion bindWorld = _reference.rotation * _bindRotations[i];
                Quaternion alignment = Quaternion.FromToRotation(_fallback[i], _solved[i + 1] - _solved[i]);
                _orderedBody[i].SetPositionAndRotation(_solved[i], alignment * bindWorld);
            }
            if (_tail != null) _tail.position = _solved[_orderedBody.Length];
            MaximumSegmentLengthError = 0;
            for (int i = 0; i < _orderedBody.Length; i++)
            {
                Vector3 end = i + 1 < _orderedBody.Length ? _orderedBody[i + 1].position : (_tail != null ? _tail.position : _solved[i + 1]);
                MaximumSegmentLengthError = Mathf.Max(MaximumSegmentLengthError, Mathf.Abs(Vector3.Distance(_orderedBody[i].position, end) - _lengths[i]));
            }
        }
    }
}
