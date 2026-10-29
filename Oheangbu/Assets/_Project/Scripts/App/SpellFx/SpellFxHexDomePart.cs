using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 우 route H (2026-10-29, the user: "반투명 홀로그램 육각형 막에 문양이 박혀있는 모습" -> "돔 + 다양한 문양"; concept kcisa/concepts/hx_1.png):
    /// the hexagon-cell dome (mesh from u_h_build.py via SpellFxSetupH, shader Oheangbu/SpellFx/HexDome). One renderer; this part only places and scales it and drives the
    /// material's clock: _Age from the launch, _Fade over the last fadeSeconds of its own life, _HitPos / _HitAge for the ripple of the latest hit.
    /// The dome's centre is pushed centreAhead metres away from the camera so the camera (about 2.8 m behind the caster) stays outside it. The look only.</summary>
    public sealed class SpellFxHexDomePart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Mesh dome;
        [SerializeField] Material material;
        [Tooltip("Round 9: the dome's mark on the ground (Oheangbu/SpellFx/HexGround) - a flat ring built here; none = no mark.")]
        [SerializeField] Material groundMaterial;
        [SerializeField] float radius = 2.2f;
        [Tooltip("Height scale on top of the mesh's own 0.85: the dome must stand taller than the camera (about 2.1 m) or the camera looks down through its crown at the caster (round 2).")]
        [SerializeField] float heightScale = 1.1f;
        [SerializeField] float centreAhead = 1.5f;
        [SerializeField] float life = 2.4f;
        [SerializeField] float fadeSeconds = 0.6f;
        [SerializeField] float flashNotBefore = 0.5f;

        static readonly int AgeId = Shader.PropertyToID("_Age"), FadeId = Shader.PropertyToID("_Fade"), HitPosId = Shader.PropertyToID("_HitPos"), HitAgeId = Shader.PropertyToID("_HitAge");

        GameObject _go, _ground;
        Material _mat, _groundMat;
        Mesh _groundMesh;
        bool _launched;
        float _age, _hitAt = -1f;

        void Build(SpellFxCast cast)
        {
            if (_go != null || dome == null || material == null) return;
            Vector3 origin = cast != null ? cast.Origin : transform.position;
            Vector3 back = cast != null ? cast.Forward : transform.forward;
            var cam = Camera.main;
            if (cam != null) { Vector3 d = origin - cam.transform.position; d.y = 0f; if (d.sqrMagnitude > 0.01f) back = d; }
            back.y = 0f; if (back.sqrMagnitude < 1e-4f) back = Vector3.forward;
            back.Normalize();
            _go = new GameObject("HexDome");
            _go.transform.SetParent(transform, false);
            _go.transform.SetPositionAndRotation(origin + back * centreAhead, Quaternion.LookRotation(back, Vector3.up));
            _go.transform.localScale = new Vector3(radius, radius * heightScale, radius);
            int layerId = VfxAfterFog308.Layer;
            if (layerId >= 0) _go.layer = layerId;
            _go.AddComponent<MeshFilter>().sharedMesh = dome;
            var r = _go.AddComponent<MeshRenderer>();
            _mat = new Material(material);
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            _mat.SetFloat(AgeId, -1f); _mat.SetFloat(FadeId, 0f); _mat.SetFloat(HitAgeId, -1f);
            _go.SetActive(false);
            if (groundMaterial != null)
            {
                // a flat ring from 0.6 to 1.1 dome radii, its band uv.x = 0 inside .. 1 outside; the foot (radius 1) sits at uv.x 0.8 (the shader's _Foot)
                _ground = new GameObject("HexDomeGround");
                _ground.transform.SetParent(transform, false);
                _ground.transform.SetPositionAndRotation(_go.transform.position + Vector3.up * 0.01f, Quaternion.identity);
                if (layerId >= 0) _ground.layer = layerId;
                _groundMesh = Ring(radius * 0.6f, radius * 1.1f, 96);
                _ground.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
                var gr = _ground.AddComponent<MeshRenderer>();
                _groundMat = new Material(groundMaterial);
                gr.sharedMaterial = _groundMat; gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gr.receiveShadows = false;
                _groundMat.SetFloat(AgeId, -1f); _groundMat.SetFloat(FadeId, 0f);
                _ground.SetActive(false);
            }
        }

        static Mesh Ring(float r0, float r1, int seg)
        {
            var m = new Mesh { name = "HexDomeGroundRing" };
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * r0, 0f, s * r0); v[i * 2 + 1] = new Vector3(c * r1, 0f, s * r1);
                uv[i * 2] = new Vector2(0f, i / (float)seg); uv[i * 2 + 1] = new Vector2(1f, i / (float)seg);
                if (i < seg) { int k = i * 6, b = i * 2; tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 2; tri[k + 3] = b + 1; tri[k + 4] = b + 3; tri[k + 5] = b + 2; }
            }
            m.vertices = v; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            return m;
        }

        public void OnBegin(SpellFxCast cast) { Build(cast); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            if (_go != null) _go.SetActive(true);
            if (_ground != null) _ground.SetActive(true);
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _mat == null) return;
            _mat.SetFloat(AgeId, ageSeconds);
            _mat.SetFloat(FadeId, Mathf.Clamp01((ageSeconds - (life - fadeSeconds)) / fadeSeconds));
            _mat.SetFloat(HitAgeId, _hitAt >= 0f ? ageSeconds - _hitAt : -1f);
            if (_groundMat != null) { _groundMat.SetFloat(AgeId, ageSeconds); _groundMat.SetFloat(FadeId, Mathf.Clamp01((ageSeconds - (life - fadeSeconds)) / fadeSeconds)); }
            if (ageSeconds > life + 0.1f && _ground != null && _ground.activeSelf) _ground.SetActive(false);
            if (ageSeconds > life + 0.1f && _go != null && _go.activeSelf) _go.SetActive(false);
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (_mat == null) return;
            _hitAt = Mathf.Max(_age, flashNotBefore);         // a contact at the launch flashes once the dome stands (round 5: it fell inside the reveal)
            _mat.SetVector(HitPosId, hit.Position);
        }

        /// <summary>Nothing: the executor ends a ring cast at once; the dome keeps its own life (fadeSeconds at the end).</summary>
        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason) { }

        void OnDestroy() { if (_mat != null) Destroy(_mat); if (_groundMat != null) Destroy(_groundMat); if (_groundMesh != null) Destroy(_groundMesh); }
    }
}
