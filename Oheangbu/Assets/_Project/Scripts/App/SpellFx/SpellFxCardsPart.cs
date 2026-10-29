using System;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 routes A and B: cards that hold a picture of the spell as the capture camera sees it - a rendered frame sequence (A, Blender / Cycles) or one painted
    /// still from the concept (B, animated by the card shader). Each card fills the frustum of a virtual camera at the shoulder-camera pose in the caster's frame
    /// (eye = origin + right 0.7 + up 2.25 - forward 2.8, vertical FOV 60) at its own distance: the far card sits behind the bodies, the near card in front of them, and the
    /// world depth sorts them. <see cref="pitch"/> is the virtual camera's look-down angle the picture was made with (0 for the Cycles render, ~17 for the concept).
    /// The look only: placed once at the cast's begin, never moved by a contact.</summary>
    public sealed class SpellFxCardsPart : MonoBehaviour, ISpellFxPart
    {
        [Serializable]
        public sealed class Card
        {
            public string Name = "Far";
            [Tooltip("Route B: one painted still (animated by the shader).")] public Texture2D Still;
            [Tooltip("Route A: the rendered frames (slice = frame).")] public Texture2DArray Sequence;
            [Tooltip("Route A r2: the render's view depth per frame (r = depth / DepthRange); with it the card tests each pixel against the scene and draws at ZTest Always.")] public Texture2DArray DepthSequence;
            public float DepthRange = 12f;
            [Min(1f)] public float Fps = 30f;
            [Tooltip("Metres from the virtual eye along its view.")] public float Distance = 5f;
            [Tooltip("The still's waterline in uv y (0 = bottom): it grows up and down away from it.")] [Range(0f, 1f)] public float RiseLine = 0.55f;
        }

        [SerializeField] Card[] cards = Array.Empty<Card>();
        [SerializeField] Shader shader;
        [SerializeField] Vector3 eyeOffset = new Vector3(0.7f, 2.25f, -2.8f);
        [SerializeField] float pitch = 0f;
        [SerializeField] float fovY = 60f;
        [SerializeField] float aspect = 16f / 9f;
        [Header("Still (route B) timing")]
        [SerializeField] float riseSeconds = 0.6f;
        [SerializeField] float dissolveFrom = 1.4f, dissolveTo = 2.2f;
        [SerializeField] float flow = 1f;
        [Tooltip("Route B r2: how far the painted ring turns along its ellipse (radians, eases in), and the ellipse in uv (centre xy, radii zw).")]
        [SerializeField] float swirl = 0.55f;
        [SerializeField] Vector4 swirlEllipse = new Vector4(0.54f, 0.45f, 0.45f, 0.28f);

        Material[] _mats;
        static Mesh _quad;
        static readonly int IdMain = Shader.PropertyToID("_MainTex"), IdSeq = Shader.PropertyToID("_Seq"), IdUseSeq = Shader.PropertyToID("_UseSeq"), IdFrame = Shader.PropertyToID("_Frame"),
            IdFrames = Shader.PropertyToID("_Frames"), IdAlpha = Shader.PropertyToID("_Alpha"), IdRise = Shader.PropertyToID("_Rise"), IdRiseLine = Shader.PropertyToID("_RiseLine"),
            IdFlow = Shader.PropertyToID("_Flow"), IdAge = Shader.PropertyToID("_Age"), IdDissolve = Shader.PropertyToID("_Dissolve"),
            IdDepthSeq = Shader.PropertyToID("_DepthSeq"), IdUseDepth = Shader.PropertyToID("_UseDepth"), IdDepthRange = Shader.PropertyToID("_DepthRange"), IdZTest = Shader.PropertyToID("_ZTest"),
            IdSwirl = Shader.PropertyToID("_Swirl"), IdSwirlC = Shader.PropertyToID("_SwirlC");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _quad = null;

        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "SpellFxCardQuad", hideFlags = HideFlags.DontSave };
            _quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) });
            _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            _quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            _quad.RecalculateBounds();
            return _quad;
        }

        public void OnBegin(SpellFxCast cast)
        {
            var sh = shader != null ? shader : Shader.Find("Oheangbu/SpellFx/Card");
            if (sh == null) { Debug.LogWarning("SpellFxCardsPart: shader Oheangbu/SpellFx/Card missing"); return; }
            Vector3 fwd = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward.normalized : Vector3.forward, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 eye = cast.Origin + right * eyeOffset.x + Vector3.up * eyeOffset.y + fwd * eyeOffset.z;
            Quaternion view = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
            _mats = new Material[cards.Length];
            for (int k = 0; k < cards.Length; k++)
            {
                var c = cards[k];
                var go = new GameObject("Card_" + c.Name);
                go.transform.SetParent(transform, false);
                float h = 2f * c.Distance * Mathf.Tan(fovY * 0.5f * Mathf.Deg2Rad);
                // the quad faces the eye: its +z points away from the camera, so look along the view
                go.transform.SetPositionAndRotation(eye + view * Vector3.forward * c.Distance, view);
                go.transform.localScale = new Vector3(h * aspect, h, 1f);
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                var m = new Material(sh) { name = "SpellFxCard_" + c.Name, hideFlags = HideFlags.DontSave };
                if (c.Sequence != null) { m.SetTexture(IdSeq, c.Sequence); m.SetFloat(IdUseSeq, 1f); m.SetFloat(IdFrames, c.Sequence.depth); }
                else if (c.Still != null) { m.SetTexture(IdMain, c.Still); m.SetVector(IdSwirlC, swirlEllipse); }
                if (c.DepthSequence != null) { m.SetTexture(IdDepthSeq, c.DepthSequence); m.SetFloat(IdUseDepth, 1f); m.SetFloat(IdDepthRange, c.DepthRange); m.SetFloat(IdZTest, (float)UnityEngine.Rendering.CompareFunction.Always); }
                m.SetFloat(IdRiseLine, c.RiseLine);
                m.SetFloat(IdRise, c.Sequence != null ? 1f : 0.02f);
                m.SetFloat(IdAlpha, c.Sequence != null ? 1f : 0f);
                mr.sharedMaterial = m;
                _mats[k] = m;
            }
            VfxAfterFog308.Route(transform);
        }

        public void OnTick(SpellFxCast cast, float age)
        {
            if (_mats == null) return;
            for (int k = 0; k < _mats.Length; k++)
            {
                var m = _mats[k]; var c = cards[k]; if (m == null) continue;
                m.SetFloat(IdAge, age);
                if (c.Sequence != null)
                {
                    float f = Mathf.Max(0f, age * c.Fps);
                    m.SetFloat(IdFrame, Mathf.Min(f, c.Sequence.depth - 1));
                    m.SetFloat(IdAlpha, f > c.Sequence.depth + 2f ? 0f : 1f);
                }
                else
                {
                    m.SetFloat(IdRise, Ease(Mathf.Clamp01(age / riseSeconds)));
                    m.SetFloat(IdAlpha, Mathf.Clamp01(age / 0.08f));
                    m.SetFloat(IdFlow, flow * (0.5f + age));
                    m.SetFloat(IdDissolve, Mathf.SmoothStep(0f, 1.05f, Mathf.InverseLerp(dissolveFrom, dissolveTo, age)));
                    m.SetFloat(IdSwirl, swirl * (1f - Mathf.Exp(-age / 0.7f)));
                }
            }
        }

        static float Ease(float t) { const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }

        void OnDestroy()
        {
            if (_mats == null) return;
            foreach (var m in _mats) if (m != null) Destroy(m);
        }
    }
}
