using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    // Isolated illustration props, NOT runtime environment interactions. Create only
    // for a PreviewControlled effect and Dispose when that review instance ends.
    // Parent must be the same coordinate frame used by EnvironmentMotion.Context.
    public sealed class Vfx120EnvironmentFixture : IDisposable
    {
        public GameObject Root { get; private set; }
        public string Glyph { get; private set; }
        private Vector3 _surface, _normal, _right, _up, _cutStart, _cutEnd;
        private Vector3[] _path;
        private bool _pairs;
        private Transform[] _pieces;
        private Renderer[] _renderers;
        private Vector3[] _restPositions, _restScales;
        private Material _material;
        private MaterialPropertyBlock _block;
        private Color _baseColor;
        private float _size;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static Vfx120EnvironmentFixture Create(string glyph, Transform parent,
            Vector3 localSurfacePoint, Vector3 localSurfaceNormal, bool previewControlled,
            float size = 1)
        {
            if (!previewControlled || parent == null || (glyph != "눈" && glyph != "숫" && glyph != "웅")) return null;
            if (!Finite(localSurfacePoint) || !Finite(localSurfaceNormal) || localSurfaceNormal.sqrMagnitude < .000001f
                || float.IsNaN(size) || float.IsInfinity(size)) return null;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;
            var fixture = new Vfx120EnvironmentFixture
            {
                Glyph = glyph, _surface = localSurfacePoint, _normal = localSurfaceNormal.normalized,
                _size = Mathf.Clamp(size, .35f, 2), _material = new Material(shader), _block = new MaterialPropertyBlock()
            };
            Vfx120EnvironmentMotion.Basis(fixture._normal, out fixture._right, out fixture._up);
            fixture.Root = new GameObject("VFX_REVIEW_ONLY_Environment_" + glyph);
            fixture.Root.transform.SetParent(parent, false);
            fixture._material.name = "VFX_REVIEW_ONLY_EnvironmentMaterial_" + glyph;
            fixture._baseColor = glyph == "눈" ? new Color(.16f, .13f, .085f)
                : glyph == "숫" ? new Color(.32f, .31f, .28f) : new Color(.055f, .043f, .065f);
            fixture._material.SetColor(BaseColorId, fixture._baseColor);
            fixture._material.SetColor(ColorId, fixture._baseColor);
            if (fixture._material.HasProperty("_Smoothness")) fixture._material.SetFloat("_Smoothness", .08f);
            int count = glyph == "눈" ? 10 : glyph == "숫" ? 2 : 7;
            fixture._pieces = new Transform[count]; fixture._renderers = new Renderer[count];
            fixture._restPositions = new Vector3[count]; fixture._restScales = new Vector3[count];
            if (glyph == "눈") fixture.BuildVines();
            else if (glyph == "숫") fixture.BuildRock();
            else fixture.BuildPollution();
            return fixture;
        }

        // References point into arrays allocated once by this fixture. The motion
        // sampler treats them as read-only; this call performs no copying/allocation.
        public void FillContext(ref Vfx120EnvironmentMotion.Context context)
        {
            context.HasSurface = Root != null;
            context.SurfacePoint = _surface; context.SurfaceNormal = _normal;
            context.HasCut = Glyph == "숫"; context.CutStart = _cutStart; context.CutEnd = _cutEnd;
            context.PathPoints = _path; context.PathIsSegmentPairs = _pairs;
            // Do not enable either review flag or manufacture actual Hit/Release here.
        }

        public void Sample(in Vfx120EnvironmentMotion.Context context)
        {
            if (Root == null || !context.PreviewControlled || !Finite(context.Age)) return;
            float hit = Vfx120EnvironmentMotion.GetHitTime(context);
            float release = Vfx120EnvironmentMotion.GetReleaseTime(context);
            for (int i = 0; i < _pieces.Length; i++)
            {
                Transform piece = _pieces[i];
                if (piece == null) continue;
                Vector3 position = _restPositions[i], scale = _restScales[i];
                Color color = _baseColor;
                if (Glyph == "눈")
                {
                    float charred = hit < 0 ? 0 : Smooth(context.Age - hit - i * .032f, .40f);
                    color = Color.Lerp(_baseColor, new Color(.035f, .028f, .021f), charred);
                    float gone = release < 0 ? 0 : Smooth(context.Age - release - i * .018f, .42f);
                    float side = (i & 1) == 0 ? -1 : 1;
                    position += _right * side * gone * .22f * _size - _up * gone * gone * .13f * _size;
                    scale *= 1 - gone;
                }
                else if (Glyph == "숫")
                {
                    // These are our two review blocks, never the passed target mesh.
                    // Opening a gap illustrates an externally confirmed cut; no collider
                    // or real terrain is changed, even when a real cue is supplied.
                    float open = release < 0 ? 0 : Smooth(context.Age - release, .50f);
                    position += _right * (i == 0 ? -1 : 1) * .64f * _size * open;
                }
                else
                {
                    float clean = release < 0 ? 0 : Smooth(context.Age - release - i * .035f, .42f);
                    scale *= 1 - clean;
                }
                piece.localPosition = position; piece.localScale = scale;
                _renderers[i].enabled = scale.sqrMagnitude > .000001f;
                _block.Clear(); _block.SetColor(BaseColorId, color); _block.SetColor(ColorId, color);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        private void BuildVines()
        {
            _pairs = true; _path = new Vector3[20];
            var a = new[] { new Vector2(-.43f,-.78f), new Vector2(.43f,-.78f),
                new Vector2(-.64f,-.52f), new Vector2(.64f,-.52f), new Vector2(-.72f,0),
                new Vector2(-.43f,.2f), new Vector2(.43f,-.15f), new Vector2(0,-.15f),
                new Vector2(-.1f,-.7f), new Vector2(-.6f,.58f) };
            var b = new[] { new Vector2(-.40f,.78f), new Vector2(.40f,.78f),
                new Vector2(.60f,.55f), new Vector2(-.60f,.55f), new Vector2(.72f,.08f),
                new Vector2(-.75f,.58f), new Vector2(.75f,.2f), new Vector2(-.35f,.52f),
                new Vector2(.30f,.4f), new Vector2(.60f,.65f) };
            for (int i = 0; i < 10; i++)
            {
                Vector3 start = Plane(a[i]), end = Plane(b[i]);
                _path[i * 2] = start; _path[i * 2 + 1] = end;
                Vector3 segment = end - start;
                AddPiece(i, PrimitiveType.Cylinder, (start + end) * .5f,
                    Quaternion.FromToRotation(Vector3.up, segment.normalized), new Vector3(.065f * _size, segment.magnitude * .5f, .065f * _size));
            }
        }

        private void BuildRock()
        {
            _cutStart = _surface - _up * .80f * _size;
            _cutEnd = _surface + _up * .80f * _size;
            Quaternion rotation = Quaternion.LookRotation(_normal, _up);
            // Front faces coincide with the supplied surface and share one vertical seam.
            for (int i = 0; i < 2; i++)
                AddPiece(i, PrimitiveType.Cube, _surface + _right * (i == 0 ? -.31f : .31f) * _size - _normal * .275f * _size,
                    rotation, new Vector3(.62f, 1.60f, .55f) * _size);
        }

        private void BuildPollution()
        {
            _path = new Vector3[8];
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.PI * 2 / 7;
                _path[i] = _surface + (_right * Mathf.Cos(a) * .74f + _up * Mathf.Sin(a) * .57f) * _size;
                Vector3 position = _surface + (_right * Mathf.Cos(a) * .29f + _up * Mathf.Sin(a) * .23f) * _size + _normal * .010f;
                AddPiece(i, PrimitiveType.Sphere, position, Quaternion.FromToRotation(Vector3.up, _normal),
                    new Vector3(.72f, .025f, .60f) * _size);
            }
            _path[7] = _path[0];
        }

        private Vector3 Plane(Vector2 p) => _surface + (_right * p.x + _up * p.y) * _size;
        private void AddPiece(int index, PrimitiveType primitive, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject piece = GameObject.CreatePrimitive(primitive);
            piece.name = "ReviewProxy_" + Glyph + "_" + index;
            // Disable immediately before deferred destruction so not even a single
            // physics step can use a fixture collider created by CreatePrimitive.
            Collider collider = piece.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; DestroyOwned(collider); }
            piece.transform.SetParent(Root.transform, false);
            piece.transform.localPosition = position; piece.transform.localRotation = rotation; piece.transform.localScale = scale;
            Renderer renderer = piece.GetComponent<Renderer>();
            renderer.sharedMaterial = _material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            _pieces[index] = piece.transform; _renderers[index] = renderer;
            _restPositions[index] = position; _restScales[index] = scale;
        }

        public void Dispose()
        {
            // Only objects created above are destroyed. Parent, terrain and targets
            // were never retained as owned objects and cannot be removed here.
            if (Root != null) DestroyOwned(Root);
            if (_material != null) DestroyOwned(_material);
            Root = null; _material = null;
        }
        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        private static float Smooth(float age, float span) { float t = Mathf.Clamp01(age / Mathf.Max(.001f, span)); return t * t * (3 - 2 * t); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
