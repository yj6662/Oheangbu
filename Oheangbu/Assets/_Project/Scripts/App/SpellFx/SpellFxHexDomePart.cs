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
        [Tooltip("The ring lies this far above the cast origin (round 10: at 1 cm it sank under the lab floor).")]
        [SerializeField] float groundLift = 0.04f;
        [SerializeField] float radius = 2.2f;
        [Tooltip("Height scale on top of the mesh's own 0.85: the dome must stand taller than the camera (about 2.1 m) or the camera looks down through its crown at the caster (round 2).")]
        [SerializeField] float heightScale = 1.1f;
        [SerializeField] float centreAhead = 1.5f;
        [SerializeField] float life = 2.4f;
        [SerializeField] float fadeSeconds = 0.6f;
        [SerializeField] float flashNotBefore = 0.5f;
        [Tooltip("D308-156: when the cast is a ward, stand where the ward really is - centred on the cast point, foot on the band's outer edge, life = the ward's own life " +
                 "(the fields radius / centreAhead / life above are then not used). Off = the round-2 camera placement (look only).")]
        [SerializeField] bool followWard = true;
        [Tooltip("2026-10-30, the five ㅜ wards: what a taken shot turns into (the ward's own body).")]
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Water;

        static readonly int AgeId = Shader.PropertyToID("_Age"), FadeId = Shader.PropertyToID("_Fade"), HitPosId = Shader.PropertyToID("_HitPos"), HitAgeId = Shader.PropertyToID("_HitAge"), HitCellId = Shader.PropertyToID("_HitCell"), ImpPosId = Shader.PropertyToID("_ImpPos"), ImpAgeId = Shader.PropertyToID("_ImpAge");
        const float MeshFlat = 0.85f;                       // the dome mesh's own height / radius (u_h_build.py FLAT)

        // look-only impacts of lab shots (SpellFxLabShot) on the membrane: up to four at once, oldest replaced
        readonly Vector4[] _impPos = new Vector4[4];
        readonly float[] _impAt = { -10f, -10f, -10f, -10f };
        int _impNext;

        GameObject _go, _ground;
        Material _mat, _groundMat;
        Mesh _groundMesh;
        bool _launched;
        float _age, _hitAt = -1f;
        float _radius, _life;
        bool _ward;

        void Build(SpellFxCast cast)
        {
            if (_go != null || dome == null || material == null) return;
            Vector3 origin = cast != null ? cast.Origin : transform.position;
            Vector3 back = cast != null ? cast.Forward : transform.forward;
            var cam = Camera.main;
            if (cam != null) { Vector3 d = origin - cam.transform.position; d.y = 0f; if (d.sqrMagnitude > 0.01f) back = d; }
            back.y = 0f; if (back.sqrMagnitude < 1e-4f) back = Vector3.forward;
            back.Normalize();
            // D308-156: a ward's dome IS the ward - its centre, its outer band edge (where shots are taken) and its life; otherwise the look-only placement
            _ward = followWard && cast != null && cast.HasWard;
            _radius = _ward ? (cast.Spec != null ? cast.Spec.RingOuter : cast.Radius) : radius;
            _life = _ward ? cast.WardSeconds + fadeSeconds * 0.5f : life;
            Vector3 centre = _ward ? origin : origin + back * centreAhead;
            _go = new GameObject("HexDome");
            _go.transform.SetParent(transform, false);
            _go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(back, Vector3.up));
            _go.transform.localScale = new Vector3(_radius, _radius * heightScale, _radius);
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
                _ground.transform.SetPositionAndRotation(_go.transform.position + Vector3.up * groundLift, Quaternion.identity);
                if (layerId >= 0) _ground.layer = layerId;
                _groundMesh = Ring(_radius * 0.6f, _radius * 1.1f, 96);
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
            _mat.SetFloat(FadeId, Mathf.Clamp01((ageSeconds - (_life - fadeSeconds)) / fadeSeconds));
            _mat.SetFloat(HitAgeId, _hitAt >= 0f ? ageSeconds - _hitAt : -1f);
            if (_groundMat != null) { _groundMat.SetFloat(AgeId, ageSeconds); _groundMat.SetFloat(FadeId, Mathf.Clamp01((ageSeconds - (_life - fadeSeconds)) / fadeSeconds)); }
            if (ageSeconds > _life + 0.1f && _ground != null && _ground.activeSelf) _ground.SetActive(false);
            if (ageSeconds > _life + 0.1f && _go != null && _go.activeSelf) _go.SetActive(false);
            Shots(ageSeconds);
            if (_mat != null) { _mat.SetVectorArray(ImpPosId, _impPos); _mat.SetVector(ImpAgeId, new Vector4(Age(0, ageSeconds), Age(1, ageSeconds), Age(2, ageSeconds), Age(3, ageSeconds))); }
        }

        /// <summary>D308-156: the ward took a real enemy shot at <paramref name="point"/> (the executor's own point on the band): the shot's end is drawn as ink there and
        /// leaves an ink blot that runs down the membrane - the same look the lab shots have, now driven by the rule's event.</summary>
        public void OnIntercept(SpellFxCast cast, Vector3 point)
        {
            Build(cast);
            Vector3 centre = _go != null ? _go.transform.position : (cast != null ? cast.Origin : transform.position);
            if (_go != null)
            {
                // the rule's point is on the band (a vertical wall); the membrane is a dome: draw it where the shot's line from the centre meets the shell
                var t = _go.transform;
                Vector3 u = Unit(t.InverseTransformPoint(point));
                if (u.sqrMagnitude > 1e-6f) { u.Normalize(); if (u.y < 0f) u.y = 0f; u.Normalize(); point = t.TransformPoint(new Vector3(u.x, u.y * MeshFlat, u.z)); }
            }
            Vector3 normal = point - centre; if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            SpellFxLabShot.InkBurst(point, normal.normalized, burstBody);
            Blot(point, _age);
        }

        /// <summary>D308-156: the ward stopped a melee hit: the cell of the membrane nearest the blow flashes (the same flash a body's contact gives), at once.</summary>
        public void OnBlock(SpellFxCast cast, Vector3 point)
        {
            Build(cast);
            if (_mat == null) return;
            _hitAt = Mathf.Max(_age, 0f);
            _mat.SetVector(HitPosId, point);
            _mat.SetVector(HitCellId, NearestCell(point));
        }

        void Blot(Vector3 point, float age)
        {
            _impPos[_impNext] = new Vector4(point.x, point.y, point.z, 1f); _impAt[_impNext] = age;
            _impNext = (_impNext + 1) % _impPos.Length;
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            if (_mat == null) return;
            _hitAt = Mathf.Max(_age, flashNotBefore);         // a contact at the launch flashes once the dome stands (round 5: it fell inside the reveal)
            _mat.SetVector(HitPosId, hit.Position);
            _mat.SetVector(HitCellId, NearestCell(hit.Position));
        }

        /// <summary>Nothing: the executor ends a ring cast at once; the dome keeps its own life (fadeSeconds at the end).</summary>
        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason) { }

        /// <summary>2026-10-29 (the user: "투사체가 먹이 되어 사라지는 효과도" -> look only): a lab shot that crosses the membrane while the dome stands is turned to ink
        /// there (<see cref="SpellFxLabShot.Dissolve"/>) and leaves an ink blot that runs down the glass (the shader's _ImpPos / _ImpAge). No rule sees any of this.</summary>
        void Shots(float age)
        {
            if (_go == null || !_go.activeSelf || _mat == null) return;
            bool standing = age > 0.25f && age < _life - fadeSeconds * 0.5f;
            var t = _go.transform;
            for (int i = SpellFxLabShot.Active.Count - 1; i >= 0; i--)
            {
                var s = SpellFxLabShot.Active[i];
                if (s == null || !s.Flying || !standing) continue;
                Vector3 a = Unit(t.InverseTransformPoint(s.PreviousPosition)), b = Unit(t.InverseTransformPoint(s.Position));
                if (b.magnitude > 1f || a.magnitude <= 1f) continue;              // crosses the shell from outside this frame
                float k = (a.magnitude - 1f) / Mathf.Max(a.magnitude - b.magnitude, 1e-4f);
                Vector3 hitU = Vector3.Lerp(a, b, k).normalized;
                Vector3 local = new Vector3(hitU.x, hitU.y * MeshFlat, hitU.z);
                Vector3 point = t.TransformPoint(local), normal = (point - t.position).normalized;
                s.Dissolve(point, normal, burstBody);
                Blot(point, age);
            }
        }

        float Age(int i, float now) => _impPos[i].w > 0.5f ? now - _impAt[i] : -1f;

        /// <summary>A local dome point in unit-sphere space (the mesh is a unit dome squashed to <see cref="MeshFlat"/> in y).</summary>
        static Vector3 Unit(Vector3 local) => new Vector3(local.x, local.y / MeshFlat, local.z);

        /// <summary>Round 17b (Fable r17: no cell centre lay within the shader's fixed reach of the contact, so no cell answered): the centre of the ONE cell
        /// nearest the contact - the wall point in the body's direction at 0.45 m (the shader's own contact) - read from the mesh's cell centres (uv3).</summary>
        Vector4 NearestCell(Vector3 body)
        {
            if (_go == null || dome == null) return new Vector4(0f, -1000f, 0f, 0f);
            var t = _go.transform;
            Vector3 c = t.position, d = body - c; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) d = t.forward;
            Vector3 contact = c + Vector3.up * 0.45f + d.normalized * (_radius > 0f ? _radius : radius) * 0.93f;
            var centres = new System.Collections.Generic.List<Vector3>();
            dome.GetUVs(3, centres);
            float best = float.MaxValue; Vector3 bestW = contact;
            foreach (var oc in centres)
            {
                Vector3 w = t.TransformPoint(oc);
                float dd = (w - contact).sqrMagnitude;
                if (dd < best) { best = dd; bestW = w; }
            }
            return new Vector4(bestW.x, bestW.y, bestW.z, 1f);
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); if (_groundMat != null) Destroy(_groundMat); if (_groundMesh != null) Destroy(_groundMesh); }
    }
}
