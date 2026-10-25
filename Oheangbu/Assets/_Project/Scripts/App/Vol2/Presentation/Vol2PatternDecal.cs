using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>D308-150 V9 step 5a (TEST, presentation only): a KCISA traditional pattern (Assets/KoreanTraditionalPattern_Effect, read only - the pack is not edited) laid as
    /// ritual language on a vol.2 lasting state, as DECISIONS #135 / #138 / #189 allow (pattern layered, texture reused; the effect grammar stays the ink parts'). Its material
    /// is a vol.2-owned SeedLeafMark copy (pattern alpha, the body palette's dark tint, matte, no emission, alpha blend). The mesh is made here on the instance:
    ///  - Disc: a flat quad on the ground (a ring pattern lies on the ring band; the part's scale is the ring's radius);
    ///  - Fan: a flat sector from the apex (+Z = the fan's forward), the round pattern mapped by planar UV so the sector CLIPS it - the fan's own edges;
    ///  - Plate: an upright quad (a seal standing above a body, turned to the eye by the stage);
    ///  - Band: an open cylinder round a limb of the humanoid it is parented under (the pattern frieze wound round the sleeve), laid on the bones every frame.
    /// The look over time is this instance's MaterialPropertyBlock (_Alpha): it inks in over <see cref="InSeconds"/>, pales on Dim, fades on Fade. No light, no glow.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class Vol2PatternDecal : MonoBehaviour
    {
        public enum Kind { Disc = 0, Fan = 1, Plate = 2, Band = 3 }

        public Kind Shape = Kind.Disc;
        [Tooltip("Disc / Plate: half size (local m). Fan: depth = 1 (the stage scales); Band: the cylinder's radius (m).")]
        public float Size = 1f;
        [Tooltip("Fan: half angle (degrees).")]
        public float HalfAngle = 30f;
        [Tooltip("Band: height of the frieze along the limb (m) and how many times it repeats round it.")]
        public float BandHeight = .14f, BandTiles = 2f;
        public HumanBodyBones From = HumanBodyBones.RightUpperArm, To = HumanBodyBones.RightLowerArm;
        [Tooltip("Band: where on the limb (fraction from -> to).")]
        public float At = .45f;
        public float Alpha = .9f, InSeconds = .35f;
        [Tooltip("Lifted this far off the ground (m) so it does not fight the soil.")]
        public float Lift = .03f;

        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        MaterialPropertyBlock _mpb;
        MeshRenderer _r; Mesh _mesh;
        float _born, _fadeFrom = -1f, _fadeSeconds = 1f, _dim = 1f;
        Transform _a, _b, _root; float _nextBind;

        /// <summary>Checks: the frieze is laid on a limb this frame (Band).</summary>
        public bool OnLimb { get; private set; }

        void OnEnable()
        {
            _r = GetComponent<MeshRenderer>();
            _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _r.receiveShadows = false;
            if (_mesh == null) Rebuild();
            _born = Time.time;
            Apply(0f);
        }

        /// <summary>Make the mesh for the current shape numbers (the stage calls it after setting a fan's half angle).</summary>
        public void Rebuild()
        {
            if (_mesh == null) { _mesh = new Mesh { name = "Vol2PatternDecal" }; _mesh.hideFlags = HideFlags.DontSave; GetComponent<MeshFilter>().sharedMesh = _mesh; }
            _mesh.Clear();
            switch (Shape)
            {
                case Kind.Fan: FanMesh(); break;
                case Kind.Band: BandMesh(); break;
                case Kind.Plate: Quad(false); break;
                default: Quad(true); break;
            }
            _mesh.RecalculateBounds();
        }

        void Quad(bool flat)
        {
            float s = Size;
            var v = flat
                ? new[] { new Vector3(-s, Lift, -s), new Vector3(s, Lift, -s), new Vector3(s, Lift, s), new Vector3(-s, Lift, s) }
                : new[] { new Vector3(-s, -s, 0f), new Vector3(s, -s, 0f), new Vector3(s, s, 0f), new Vector3(-s, s, 0f) };
            _mesh.vertices = v;
            _mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _mesh.normals = new[] { flat ? Vector3.up : Vector3.back, flat ? Vector3.up : Vector3.back, flat ? Vector3.up : Vector3.back, flat ? Vector3.up : Vector3.back };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        }

        // the sector of radius 1 from the apex: the round pattern is centred on the apex with radius 1 (planar UV), so the sector shows the slice of it the fan covers
        void FanMesh()
        {
            int seg = Mathf.Clamp(Mathf.CeilToInt(HalfAngle * 2f / 4f), 4, 48);
            var v = new Vector3[seg + 2]; var uv = new Vector2[seg + 2]; var n = new Vector3[seg + 2]; var t = new int[seg * 3];
            v[0] = new Vector3(0f, Lift, 0f); uv[0] = new Vector2(.5f, .5f); n[0] = Vector3.up;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(-HalfAngle, HalfAngle, i / (float)seg);
                var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v[i + 1] = p + Vector3.up * Lift; uv[i + 1] = new Vector2(.5f + p.x * .5f, .5f + p.z * .5f); n[i + 1] = Vector3.up;
            }
            for (int i = 0; i < seg; i++) { t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = i + 2; }
            _mesh.vertices = v; _mesh.uv = uv; _mesh.normals = n; _mesh.triangles = t;
        }

        // an open cylinder along +Z (the limb), radius Size, height BandHeight, the frieze repeated BandTiles times round it
        void BandMesh()
        {
            const int seg = 24;
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var n = new Vector3[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i * 2] = d * Size + Vector3.forward * (-BandHeight * .5f); v[i * 2 + 1] = d * Size + Vector3.forward * (BandHeight * .5f);
                float u = i / (float)seg * BandTiles;
                uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f); n[i * 2] = n[i * 2 + 1] = d;
            }
            for (int i = 0; i < seg; i++) { int k = i * 6, a = i * 2; t[k] = a; t[k + 1] = a + 1; t[k + 2] = a + 2; t[k + 3] = a + 1; t[k + 4] = a + 3; t[k + 5] = a + 2; }
            _mesh.vertices = v; _mesh.uv = uv; _mesh.normals = n; _mesh.triangles = t;
        }

        /// <summary>The expiry warning: it pales (once).</summary>
        public void Dim(float k) { _dim = Mathf.Clamp01(k); }

        /// <summary>The state ended: the pattern fades out over <paramref name="seconds"/> (the part it sits on dries meanwhile).</summary>
        public void Fade(float seconds) { if (_fadeFrom < 0f) { _fadeFrom = Time.time; _fadeSeconds = Mathf.Max(.05f, seconds); } }

        void LateUpdate()
        {
            if (Shape == Kind.Band) FollowLimb();
            float a = Alpha * _dim * Mathf.Clamp01((Time.time - _born) / Mathf.Max(.01f, InSeconds));
            if (_fadeFrom >= 0f) a *= 1f - Mathf.Clamp01((Time.time - _fadeFrom) / _fadeSeconds);
            Apply(a);
        }

        void Apply(float a)
        {
            if (_r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _r.GetPropertyBlock(_mpb); _mpb.SetFloat(AlphaId, a); _r.SetPropertyBlock(_mpb);
        }

        void FollowLimb()
        {
            if ((_a == null || _b == null || _root != transform.root) && Time.time >= _nextBind)
            {
                _nextBind = Time.time + .5f; _root = transform.root; _a = _b = null;
                Vector3 near = transform.parent != null ? transform.parent.position : transform.position; float best = float.MaxValue;
                foreach (var anim in _root.GetComponentsInChildren<Animator>(false))
                {
                    if (anim == null || !anim.isActiveAndEnabled || !anim.isHuman || anim.avatar == null) continue;
                    bool drawn = false;
                    foreach (var sk in anim.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (sk.enabled) { drawn = true; break; }
                    if (!drawn) continue;
                    var a = anim.GetBoneTransform(From); var b = anim.GetBoneTransform(To);
                    if (a == null || b == null) continue;
                    float d = (anim.transform.position - near).sqrMagnitude;
                    if (d < best) { best = d; _a = a; _b = b; }
                }
            }
            OnLimb = _a != null && _b != null;
            _r.enabled = OnLimb;
            if (!OnLimb) return;
            Vector3 p = _a.position, axis = _b.position - p;
            if (axis.sqrMagnitude < 1e-4f) return;
            transform.SetPositionAndRotation(p + axis * At, Quaternion.LookRotation(axis.normalized));
            var parent = transform.parent;
            transform.localScale = Vector3.one / Mathf.Max(1e-4f, parent != null ? parent.lossyScale.x : 1f);
        }

        void OnDestroy() { if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); } }
    }
}
