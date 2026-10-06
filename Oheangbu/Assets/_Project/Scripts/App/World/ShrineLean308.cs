using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>
    /// #308 guide 2 (D308-32 answer 2): a rest shrine may say the way with a thing of its own world. The paper strips of its
    /// 금줄 and the smoke of its candles lean toward the next place (a world position that comes from data), with a flutter and a
    /// slow swell so that the lean is wind, never a still arrow. Transform sway only: no light, no emission, no text, no marker,
    /// no camera, no HUD. Every number is set by the editor tool (Guide2_308) from guide2_308.json rows[].lean - the defaults are
    /// zero, so a component nobody configured does nothing. A shrine without this component leans nowhere (off by default).
    /// No static state (domain reload is off: nothing to reset), no singleton, no allocation per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShrineLean308 : MonoBehaviour
    {
        [SerializeField] private bool _hasNext;
        [SerializeField] private Vector3 _next;
        [SerializeField] private float _leanDeg;
        [SerializeField] private float _flutterDeg;
        [SerializeField] private float _flutterHz;
        [SerializeField] private float _gustHz;
        [SerializeField] private float _gustShare;
        [SerializeField] private float _smokeLeanDeg;
        [SerializeField] private float _smokeSwayM;
        [SerializeField] private Transform[] _strips = new Transform[0];
        [SerializeField] private Transform[] _puffs = new Transform[0];
        [SerializeField] private float[] _puffRise = new float[0];

        private Quaternion[] _stripRest;
        private Vector3[] _puffRest;
        private float _riseMax;

        public bool HasNext => _hasNext;
        public Vector3 Next => _next;
        public float LeanDeg => _leanDeg;
        public float FlutterDeg => _flutterDeg;
        public float FlutterHz => _flutterHz;
        public float GustHz => _gustHz;
        public float GustShare => _gustShare;
        public float SmokeLeanDeg => _smokeLeanDeg;
        public float SmokeSwayM => _smokeSwayM;
        public int StripCount => _strips != null ? _strips.Length : 0;
        public int PuffCount => _puffs != null ? _puffs.Length : 0;

        /// <summary>Set by the editor tool from the data row (never by hand: the data file is the place for the numbers).</summary>
        public void Configure(bool hasNext, Vector3 next, float leanDeg, float flutterDeg, float flutterHz, float gustHz, float gustShare,
            float smokeLeanDeg, float smokeSwayM, Transform[] strips, Transform[] puffs, float[] puffRise)
        {
            _hasNext = hasNext;
            _next = next;
            _leanDeg = leanDeg;
            _flutterDeg = flutterDeg;
            _flutterHz = flutterHz;
            _gustHz = gustHz;
            _gustShare = gustShare;
            _smokeLeanDeg = smokeLeanDeg;
            _smokeSwayM = smokeSwayM;
            _strips = strips ?? new Transform[0];
            _puffs = puffs ?? new Transform[0];
            _puffRise = puffRise ?? new float[0];
        }

        /// <summary>The direction a strip hangs: straight down tilted by <paramref name="leanDeg"/> toward <paramref name="toward"/>
        /// (horizontal, unit) and by <paramref name="sideDeg"/> across it. Pure: the editor check and a test can call it.</summary>
        public static Vector3 Hang(Vector3 toward, float leanDeg, float sideDeg)
        {
            Vector3 side = new Vector3(toward.z, 0f, -toward.x);
            float a = leanDeg * Mathf.Deg2Rad, b = sideDeg * Mathf.Deg2Rad;
            Vector3 v = Vector3.down * Mathf.Cos(a) + toward * Mathf.Sin(a) + side * Mathf.Sin(b);
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.down;
        }

        /// <summary>Horizontal unit vector from <paramref name="from"/> to <paramref name="next"/>; zero when they coincide.</summary>
        public static Vector3 Toward(Vector3 from, Vector3 next)
        {
            Vector3 d = next - from;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
        }

        private void OnEnable()
        {
            int n = _strips != null ? _strips.Length : 0;
            _stripRest = new Quaternion[n];
            for (int i = 0; i < n; i++) _stripRest[i] = _strips[i] != null ? _strips[i].localRotation : Quaternion.identity;
            int m = _puffs != null ? _puffs.Length : 0;
            _puffRest = new Vector3[m];
            _riseMax = 0f;
            for (int i = 0; i < m; i++)
            {
                _puffRest[i] = _puffs[i] != null ? _puffs[i].localPosition : Vector3.zero;
                if (_puffRise != null && i < _puffRise.Length) _riseMax = Mathf.Max(_riseMax, _puffRise[i]);
            }
        }

        private void OnDisable()
        {
            if (_stripRest != null && _strips != null)
                for (int i = 0; i < _strips.Length && i < _stripRest.Length; i++)
                    if (_strips[i] != null) _strips[i].localRotation = _stripRest[i];
            if (_puffRest != null && _puffs != null)
                for (int i = 0; i < _puffs.Length && i < _puffRest.Length; i++)
                    if (_puffs[i] != null) _puffs[i].localPosition = _puffRest[i];
        }

        private void LateUpdate()
        {
            if (_stripRest == null || _puffRest == null) return;
            Vector3 toward = _hasNext ? Toward(transform.position, _next) : Vector3.zero;
            bool aimed = toward != Vector3.zero;
            if (!aimed) toward = transform.forward;                      // no next place: the strips only stir, across the rope
            float t = Time.time;
            float swell = 1f - _gustShare * (0.5f - 0.5f * Mathf.Sin(t * _gustHz * 6.2831853f));
            float lean = aimed ? _leanDeg * swell : 0f;
            Quaternion parent = transform.rotation;
            for (int i = 0; i < _strips.Length && i < _stripRest.Length; i++)
            {
                Transform s = _strips[i];
                if (s == null) continue;
                float phase = i * 1.7f;
                float a = lean + _flutterDeg * swell * Mathf.Sin(t * _flutterHz * 6.2831853f + phase);
                float b = _flutterDeg * 0.4f * Mathf.Sin(t * _flutterHz * 4.1f + phase * 2.3f);
                s.rotation = Quaternion.FromToRotation(Vector3.down, Hang(toward, a, b)) * (parent * _stripRest[i]);
            }
            if (_puffs.Length == 0) return;
            Vector3 side = new Vector3(toward.z, 0f, -toward.x);
            float tan = aimed ? Mathf.Tan(Mathf.Clamp(_smokeLeanDeg * swell, 0f, 80f) * Mathf.Deg2Rad) : 0f;
            for (int i = 0; i < _puffs.Length && i < _puffRest.Length; i++)
            {
                Transform p = _puffs[i];
                if (p == null) continue;
                float rise = _puffRise != null && i < _puffRise.Length ? _puffRise[i] : 0f;
                float k = _riseMax > 1e-4f ? rise / _riseMax : 0f;
                Vector3 rest = transform.TransformPoint(_puffRest[i]);
                p.position = rest + toward * (tan * rise) + side * (_smokeSwayM * k * Mathf.Sin(t * _flutterHz * 6.2831853f * 0.7f + i * 0.9f));
            }
        }
    }
}
