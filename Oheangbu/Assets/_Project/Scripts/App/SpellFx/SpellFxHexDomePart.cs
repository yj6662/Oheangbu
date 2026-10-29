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
        [SerializeField] float radius = 2.2f;
        [Tooltip("Height scale on top of the mesh's own 0.85: the dome must stand taller than the camera (about 2.1 m) or the camera looks down through its crown at the caster (round 2).")]
        [SerializeField] float heightScale = 1.1f;
        [SerializeField] float centreAhead = 1.5f;
        [SerializeField] float life = 2.4f;
        [SerializeField] float fadeSeconds = 0.6f;

        static readonly int AgeId = Shader.PropertyToID("_Age"), FadeId = Shader.PropertyToID("_Fade"), HitPosId = Shader.PropertyToID("_HitPos"), HitAgeId = Shader.PropertyToID("_HitAge");

        GameObject _go;
        Material _mat;
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
        }

        public void OnBegin(SpellFxCast cast) { Build(cast); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            if (_go != null) _go.SetActive(true);
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _mat == null) return;
            _mat.SetFloat(AgeId, ageSeconds);
            _mat.SetFloat(FadeId, Mathf.Clamp01((ageSeconds - (life - fadeSeconds)) / fadeSeconds));
            _mat.SetFloat(HitAgeId, _hitAt >= 0f ? ageSeconds - _hitAt : -1f);
            if (ageSeconds > life + 0.1f && _go != null && _go.activeSelf) _go.SetActive(false);
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (_mat == null) return;
            _hitAt = _age;
            _mat.SetVector(HitPosId, hit.Position);
        }

        /// <summary>Nothing: the executor ends a ring cast at once; the dome keeps its own life (fadeSeconds at the end).</summary>
        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason) { }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
