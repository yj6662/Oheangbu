using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // #308 D308-127 follow-up (TEST, debate 13): the swing afterimage of a weapon form, in the element's colour. Presentation
    // only: it reads two points of the weapon head and writes a ribbon mesh in world space; recognition, power and judgement
    // never see it. Ink and pigment, not light (PigmentTrail308: alpha blend, no emission).
    //   ribbon   between two points of the head (blade base -> tip, the axe's edge, the flail's rod ends) sampled every frame
    //            while a strike emits: the band the striking part swept.
    //   line     a weapon that strikes along its own axis (the spear's thrust) sweeps no band: one point, widened toward the
    //            camera (halfWidth either side, across the motion).
    //   timing   a strike resets the record (no streak from the pose before), samples for EmitSeconds, each sample lives
    //            FadeSeconds: the whole afterimage is gone EmitSeconds + FadeSeconds after the click.
    [ExecuteAlways]
    [DefaultExecutionOrder(1150)]   // after the gesture rig (1000) placed the brush and the head for the frame
    public sealed class WeaponTrail308 : MonoBehaviour
    {
        const int Capacity = 64;

        [SerializeField] private Transform _from, _to;               // ribbon: the two points; line: _to only
        [SerializeField] private float _halfWidth;                    // > 0 = line mode
        [SerializeField] private Material _material;
        [SerializeField, Min(.01f)] private float _emitSeconds = .12f;
        [SerializeField, Min(.01f)] private float _fadeSeconds = .18f;

        private readonly Vector3[] _a = new Vector3[Capacity], _b = new Vector3[Capacity];
        private readonly float[] _born = new float[Capacity];
        private int _count;
        private float _emitUntil = float.NegativeInfinity;
        private Vector3 _lastTip; private bool _hasTip;
        private float _firstBorn;
        private Renderer _shownBy;
        private GameObject _holder;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Vector3[] _vertices = new Vector3[Capacity * 2];
        private Vector2[] _uv = new Vector2[Capacity * 2];
        private Color[] _colors = new Color[Capacity * 2];
        private int[] _indices = new int[(Capacity - 1) * 6];

        public bool Emitting => Time.time < _emitUntil;
        public int Samples => _count;
        public Material Material => _material;
        public float Lifetime => _emitSeconds + _fadeSeconds;

        // The editor build fills these in (the head prefab is made by a tool).
        public void Configure(Transform from, Transform to, float halfWidth, Material material, float emitSeconds, float fadeSeconds)
        {
            _from = from; _to = to; _halfWidth = halfWidth; _material = material; _emitSeconds = emitSeconds; _fadeSeconds = fadeSeconds;
        }

        // The head shows only when its brush renderer does: the afterimage follows that renderer too (null = always).
        public void ShownBy(Renderer renderer) => _shownBy = renderer;

        // One strike: the record starts fresh at this frame.
        public void Strike()
        {
            _count = 0; _hasTip = false;
            _emitUntil = Time.time + _emitSeconds;
        }

        private void OnDisable() { _count = 0; if (_renderer != null) _renderer.enabled = false; }

        private void OnDestroy()
        {
            if (_holder != null) { if (Application.isPlaying) Destroy(_holder); else DestroyImmediate(_holder); }
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
        }

        private void LateUpdate() => Step(Time.time, Camera.main);

        // Advance the afterimage to `now` (checks call this with their own clock and camera).
        public void Step(float now, Camera view)
        {
            if (_to == null || _material == null) return;
            bool shown = _shownBy == null || (_shownBy.enabled && _shownBy.gameObject.activeInHierarchy);
            // forget what has dried
            int keep = 0;
            for (int i = 0; i < _count; i++)
                if (now - _born[i] < _fadeSeconds) { _a[keep] = _a[i]; _b[keep] = _b[i]; _born[keep] = _born[i]; keep++; }
            _count = keep;
            if (now < _emitUntil && shown) Sample(now, view);
            Draw(now, shown);
        }

        private void Sample(float now, Camera view)
        {
            Vector3 tip = _to.position;
            if (_halfWidth > 0f || _from == null)
            {
                // line: across the motion and the line of sight. The first point waits for the second (no motion yet: a
                // guessed direction twisted the stroke's start into an X), then both go in with that frame's width.
                if (!_hasTip) { _lastTip = tip; _hasTip = true; _firstBorn = now; return; }
                Vector3 motion = tip - _lastTip;
                if (motion.sqrMagnitude < 1e-10f) return;
                Vector3 look = view != null ? tip - view.transform.position : Vector3.forward;
                Vector3 side = Vector3.Cross(motion, look);
                if (side.sqrMagnitude < 1e-10f) side = Vector3.Cross(motion, Vector3.up);
                if (side.sqrMagnitude < 1e-10f) side = Vector3.right;
                side = side.normalized * Mathf.Max(_halfWidth, .005f);
                if (_count == 0) Add(_lastTip - side, _lastTip + side, _firstBorn);
                Add(tip - side, tip + side, now);
                _lastTip = tip;
            }
            else Add(_from.position, tip, now);
        }

        private void Add(Vector3 a, Vector3 b, float born)
        {
            if (_count == Capacity) { System.Array.Copy(_a, 1, _a, 0, Capacity - 1); System.Array.Copy(_b, 1, _b, 0, Capacity - 1); System.Array.Copy(_born, 1, _born, 0, Capacity - 1); _count--; }
            _a[_count] = a; _b[_count] = b; _born[_count] = born; _count++;
        }

        private void Draw(float now, bool shown)
        {
            if (_count < 2 || !shown)
            {
                if (_renderer != null) _renderer.enabled = false;
                return;
            }
            if (_holder == null)
            {
                _holder = new GameObject("WeaponTrail308") { hideFlags = HideFlags.DontSave };   // world space: not parented, no scale
                if (gameObject.scene.IsValid() && _holder.scene != gameObject.scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_holder, gameObject.scene);
                _holder.layer = gameObject.layer;
                _mesh = new Mesh { name = "WeaponTrail308", hideFlags = HideFlags.DontSave }; _mesh.MarkDynamic();
                _holder.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _holder.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = _material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = LightProbeUsage.Off; _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            _holder.layer = gameObject.layer;
            _renderer.enabled = true;
            // newest sample = the head of the stroke (u 0), oldest = its tail (u 1); alpha falls with age
            for (int i = 0; i < _count; i++)
            {
                int k = _count - 1 - i;
                float u = _count > 1 ? i / (float)(_count - 1) : 0f;
                float life = Mathf.Clamp01(1f - (now - _born[k]) / _fadeSeconds);
                _vertices[i * 2] = _a[k]; _vertices[i * 2 + 1] = _b[k];
                _uv[i * 2] = new Vector2(u, 0f); _uv[i * 2 + 1] = new Vector2(u, 1f);
                var c = new Color(1f, 1f, 1f, life);
                _colors[i * 2] = c; _colors[i * 2 + 1] = c;
            }
            int n = 0;
            for (int i = 0; i < _count - 1; i++)
            {
                int v = i * 2;
                _indices[n++] = v; _indices[n++] = v + 1; _indices[n++] = v + 2;
                _indices[n++] = v + 1; _indices[n++] = v + 3; _indices[n++] = v + 2;
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices, 0, _count * 2);
            _mesh.SetUVs(0, _uv, 0, _count * 2);
            _mesh.SetColors(_colors, 0, _count * 2);
            _mesh.SetTriangles(_indices, 0, n, 0);
            _mesh.RecalculateBounds();
        }
    }
}
