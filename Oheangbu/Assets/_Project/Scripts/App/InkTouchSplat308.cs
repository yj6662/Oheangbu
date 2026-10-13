using Oheangbu.Data.Spell;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // #308 (user request 2026-10-13): "클릭을 한 순간, 화면에 붓이 닿으며, 작은 먹물이 클릭한 지점 주변으로 퍼짐(글자 판정에 영향 x)".
    // A quad in the stroke object's frame at its first point. It lives and dies with the stroke object (child), and copies the
    // stroke material's fade / dissolve so it leaves the way the stroke leaves. Presentation only: created from a visual event,
    // reads a material, writes nothing back.
    public sealed class InkTouchSplat308 : MonoBehaviour
    {
        static readonly int BirthId = Shader.PropertyToID("_Birth"), SeedId = Shader.PropertyToID("_Seed"), SpreadId = Shader.PropertyToID("_SpreadSeconds");
        static readonly int FingersId = Shader.PropertyToID("_Fingers"), DropletsId = Shader.PropertyToID("_Droplets"), OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int FadeId = Shader.PropertyToID("_FadeMul"), DissolveId = Shader.PropertyToID("_DissolveT"), InkId = Shader.PropertyToID("_InkColor");
        static readonly int SheenId = Shader.PropertyToID("_WetSheen"), WetSecondsId = Shader.PropertyToID("_WetSeconds");

        static InkTouch308Profile profile; static bool looked; static Mesh quad;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { profile = null; looked = false; quad = null; }   // domain reload is off in this project

        Renderer stroke; Material material;

        /// <summary>At the first point of a stroke. `local` and `strokeWidth` are in the stroke object's local units.</summary>
        public static void Spawn(Transform strokeFrame, Vector3 local, float strokeWidth, Color ink, float seed)
        {
            if (strokeFrame == null || !Application.isPlaying) return;
            if (!looked) { looked = true; profile = Resources.Load<InkTouch308Profile>(InkTouch308Profile.ResourcesPath); }
            if (profile == null || !profile.IsValid) return;
            var go = new GameObject("InkTouch308") { layer = strokeFrame.gameObject.layer };
            go.transform.SetParent(strokeFrame, false);
            float size = strokeWidth * profile.SizeInStrokeWidths * (1f + (Mathf.PerlinNoise(seed, 3.7f) - .5f) * 2f * profile.SizeJitter);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, seed * 37f);
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.AddComponent<MeshRenderer>();
            var splat = go.AddComponent<InkTouchSplat308>();
            splat.material = new Material(profile.TouchShader) { name = "InkTouch308 (runtime)" };
            splat.material.SetFloat(BirthId, Time.time); splat.material.SetFloat(SeedId, seed);
            splat.material.SetFloat(SpreadId, profile.SpreadSeconds); splat.material.SetFloat(FingersId, profile.Fingers);
            splat.material.SetFloat(DropletsId, profile.Droplets); splat.material.SetFloat(OpacityId, profile.Opacity);
            splat.material.SetFloat(SheenId, profile.WetSheen); splat.material.SetFloat(WetSecondsId, profile.WetSeconds);
            splat.material.SetColor(InkId, ink);
            renderer.sharedMaterial = splat.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            splat.stroke = strokeFrame.GetComponent<Renderer>();
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "InkTouch308 quad", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }

        void LateUpdate()
        {
            if (material == null) return;
            var source = stroke != null ? stroke.sharedMaterial : null;
            if (source == null) return;
            if (source.HasProperty(FadeId)) material.SetFloat(FadeId, source.GetFloat(FadeId));
            if (source.HasProperty(DissolveId)) material.SetFloat(DissolveId, source.GetFloat(DissolveId));
        }

        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
