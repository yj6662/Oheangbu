using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.41: one flat ink stamp that lives a moment and goes - the faint dot a ground miss leaves
    // and the parry ring. No asset: the two shapes are drawn once into small textures and share one Sprites/Default material
    // per stamp (the colour moves from ink toward the pigment, so each stamp owns its instance). Nothing flies, nothing glows.
    public sealed class InkStamp308 : MonoBehaviour
    {
        public enum Shape { Dot, Ring }

        static Texture2D _dot, _ring;
        static Mesh _quad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _dot = null; _ring = null; _quad = null; }

        Material _material;
        Color _ink, _pigment;
        float _alpha, _life, _soak, _grow, _born, _diameter;

        // soak: how far the colour goes from ink toward the pigment over the life (0 = stays ink); grow: end size / start size
        public static InkStamp308 Spawn(Shape shape, Vector3 position, Quaternion rotation, float diameter,
            Color ink, Color pigment, float alpha, float life, float soak = 0f, float grow = 1f)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || life <= 0f || diameter <= 0f) return null;
            var go = new GameObject(shape == Shape.Ring ? "InkStamp308_Ring" : "InkStamp308_Dot");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = Vector3.one * diameter;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var stamp = go.AddComponent<InkStamp308>();
            stamp._material = new Material(shader) { mainTexture = shape == Shape.Ring ? Ring() : Dot() };
            renderer.sharedMaterial = stamp._material;
            stamp._ink = ink; stamp._pigment = pigment; stamp._alpha = Mathf.Clamp01(alpha);
            stamp._diameter = diameter; stamp._life = life; stamp._soak = Mathf.Clamp01(soak); stamp._grow = Mathf.Max(.1f, grow);
            stamp._born = Time.time;
            stamp.Apply(0f);
            return stamp;
        }

        void Update()
        {
            float t = (Time.time - _born) / _life;
            if (t >= 1f) { Destroy(gameObject); return; }
            Apply(t);
        }

        void Apply(float t)
        {
            if (_material == null) return;
            // in within the first tenth, held, then out over the last half; the soak follows the whole life
            float a = Mathf.Clamp01(t / .1f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1f, t)));
            var c = Color.Lerp(_ink, _pigment, _soak * Mathf.SmoothStep(0f, 1f, t));
            c.a = _alpha * a;
            _material.color = c;
            // the size eases out to grow x (the ink spreading, fast then settling)
            if (Mathf.Abs(_grow - 1f) > .001f) transform.localScale = Vector3.one * (_diameter * Mathf.Lerp(1f, _grow, 1f - (1f - t) * (1f - t)));
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "InkStamp308_Quad", hideFlags = HideFlags.DontSave };
            _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(-.5f, .5f, 0f), new Vector3(.5f, .5f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            // both faces: a ground dot is seen from above, a parry ring from either side
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };
            _quad.RecalculateBounds();
            return _quad;
        }

        static Texture2D Dot() => _dot != null ? _dot : (_dot = Draw("InkStamp308_Dot", false));
        static Texture2D Ring() => _ring != null ? _ring : (_ring = Draw("InkStamp308_Ring", true));

        // white with the shape in alpha (the material colour tints it); a ragged ink edge from a few low sine waves, no texture noise
        static Texture2D Draw(string name, bool ring)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) / n * 2f - 1f, v = (y + .5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
                float edge = .06f * Mathf.Sin(ang * 3f + .7f) + .04f * Mathf.Sin(ang * 7f + 2.1f) + .025f * Mathf.Sin(ang * 13f + 4.4f);
                float a;
                if (ring)
                {
                    // a brush ring: heavier on one side (where the brush came down), thinning round the other
                    float mid = .7f + edge * .6f, half = .1f + .06f * (.5f + .5f * Mathf.Cos(ang - .9f));
                    a = 1f - Mathf.SmoothStep(half * .55f, half, Mathf.Abs(r - mid));
                }
                else
                {
                    // a soft wet dot: dense core, the edge fading out with a ragged rim
                    float outer = .82f + edge;
                    a = (1f - Mathf.SmoothStep(outer - .3f, outer, r)) * (.75f + .25f * (1f - r));
                }
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
