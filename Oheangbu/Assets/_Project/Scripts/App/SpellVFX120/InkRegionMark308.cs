using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    // #308 D308-127: one region drawing (InkRegionForms308) carried by a part of an enemy's body. Pooled by EnemyRegionMarks308.
    // Every frame it stands on its carrier's point (plus a lift along the world's up), steps toward the eye by its stand-off and
    // turns about the vertical to face the eye; it fades in, and out when released or when its carrier is gone. Presentation only:
    // it reads a transform's position.
    public sealed class InkRegionMark308 : MonoBehaviour
    {
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private readonly InkRegionForms308.Buffer _buffer = new InkRegionForms308.Buffer();
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private Transform _carrier;
        private float _lift, _standOff, _fadeIn = .2f, _fadeOut = .3f, _born, _gone = float.NaN, _shown = -1f;

        public bool Busy { get; private set; }
        public BodyRegion308 Region { get; private set; }
        /// <summary>The enemy the mark belongs to (its carrier may be one of its bones).</summary>
        public Transform Enemy { get; private set; }
        public Transform Carrier => _carrier;
        public bool Releasing => Busy && !float.IsNaN(_gone);
        public float Opacity => Mathf.Max(0f, _shown);
        public RegionMarkStats308 Stats { get; private set; }
        public Renderer MarkRenderer => _renderer;

        public void Prepare(Material material, int layer)
        {
            if (_mesh == null) { _mesh = new Mesh { name = "InkRegionMark308", hideFlags = HideFlags.DontSave }; _mesh.MarkDynamic(); }
            if (_renderer == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = gameObject.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = LightProbeUsage.Off; _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                _renderer.enabled = false;
            }
            _renderer.sharedMaterial = material;
            gameObject.layer = Mathf.Clamp(layer, 0, 31);
            if (_block == null) _block = new MaterialPropertyBlock();
        }

        public bool Show(BodyRegion308 region, Transform enemy, Transform carrier, float lift, float standOff, float scale, Color ink, float fadeIn, float fadeOut, float now)
        {
            if (_mesh == null || carrier == null) return false;
            Stats = InkRegionForms308.Compose(region, scale, ink, _buffer);
            if (_buffer.Vertices.Count == 0) { Hide(); return false; }
            _mesh.Clear();
            _mesh.SetVertices(_buffer.Vertices); _mesh.SetUVs(0, _buffer.Uvs); _mesh.SetColors(_buffer.Colours); _mesh.SetTriangles(_buffer.Triangles, 0, true);
            Region = region; Enemy = enemy; _carrier = carrier; _lift = lift; _standOff = standOff;
            _fadeIn = Mathf.Max(.01f, fadeIn); _fadeOut = Mathf.Max(.01f, fadeOut); _born = now; _gone = float.NaN; _shown = -1f;
            Busy = true; _renderer.enabled = true;
            return true;
        }

        /// <summary>The mark fades out (the reading came off, or another part took its place).</summary>
        public void Release(float now) { if (Busy && float.IsNaN(_gone)) _gone = now; }

        public void Sample(float now, Vector3 eye)
        {
            if (!Busy) return;
            if (_carrier == null || !_carrier.gameObject.activeInHierarchy) { Hide(); return; }
            Vector3 at = _carrier.position + Vector3.up * _lift;
            Vector3 away = at - eye; away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
            away.Normalize();
            transform.SetPositionAndRotation(at - away * _standOff, Quaternion.LookRotation(away, Vector3.up));
            float opacity = Mathf.Clamp01((now - _born) / _fadeIn);
            if (!float.IsNaN(_gone))
            {
                opacity = Mathf.Min(opacity, 1f - Mathf.Clamp01((now - _gone) / _fadeOut));
                if (now - _gone >= _fadeOut) { Hide(); return; }
            }
            if (Mathf.Abs(opacity - _shown) < .01f) return;
            _shown = opacity;
            _block.SetFloat(OpacityId, opacity);
            _renderer.SetPropertyBlock(_block);
        }

        public void Hide()
        {
            Busy = false; _carrier = null; Enemy = null; Region = BodyRegion308.None; _gone = float.NaN; _shown = -1f;
            if (_renderer != null) _renderer.enabled = false;
        }

        private void OnDestroy() => ReleaseResources();

        public void ReleaseResources()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}
