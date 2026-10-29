using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 우 (2026-10-29, the user: "투사체가 먹이 되어 사라지는 효과도" -> "겉모습만 (판정 없음)"): a LOOK-ONLY enemy shot for the lab shoots
    /// (SpellFxShot shots=N). It waits <see cref="Delay"/> seconds, flies straight at its speed, and is nothing to any rule: no collider, no damage, not an
    /// IVol2Projectile. A presentation part (the 우 dome) that sees it cross its membrane calls <see cref="Dissolve"/>: the shot turns to ink - its body goes,
    /// a burst of ink drops and a small ink cloud spread from the contact and fall.
    /// The live list is static; it is cleared on play start (domain reload is off in this project).</summary>
    public sealed class SpellFxLabShot : MonoBehaviour
    {
        public static readonly List<SpellFxLabShot> Active = new List<SpellFxLabShot>(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active.Clear(); _inkTex = null; _inkMat = null; }

        public Vector3 Velocity;
        public float Delay;
        public bool Flying => _alive && _t >= Delay;
        public Vector3 Position => transform.position;
        public Vector3 PreviousPosition { get; private set; }

        static Texture2D _inkTex;
        static Material _inkMat;
        bool _alive = true;
        float _t;
        GameObject _body;
        TrailRenderer _trail;

        /// <summary>A shot at <paramref name="from"/>, flying toward <paramref name="to"/> at <paramref name="speed"/> after <paramref name="delay"/> seconds.</summary>
        public static SpellFxLabShot Create(Vector3 from, Vector3 to, float speed, float delay)
        {
            var go = new GameObject("SpellFxLabShot");
            go.transform.position = from;
            var s = go.AddComponent<SpellFxLabShot>();
            s.Velocity = (to - from).normalized * speed; s.Delay = delay;
            s.Build();
            return s;
        }

        void Build()
        {
            // the body: a small dark orb with a dull red heart (an enemy's ink-and-ember shot), and a short trail
            _body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _body.name = "Body"; Destroy(_body.GetComponent<Collider>());
            _body.transform.SetParent(transform, false); _body.transform.localScale = Vector3.one * 0.32f;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh); m.color = new Color(0.16f, 0.04f, 0.04f);
            _body.GetComponent<MeshRenderer>().sharedMaterial = m;
            // an ember heart showing through the ink (reads at shot distance)
            var heart = GameObject.CreatePrimitive(PrimitiveType.Sphere); heart.name = "Heart"; Destroy(heart.GetComponent<Collider>());
            heart.transform.SetParent(_body.transform, false); heart.transform.localScale = Vector3.one * 0.55f; heart.transform.localPosition = new Vector3(0f, 0f, -0.25f);
            var hm = new Material(sh); hm.color = new Color(1.0f, 0.35f, 0.12f); heart.GetComponent<MeshRenderer>().sharedMaterial = hm;
            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.18f; _trail.minVertexDistance = 0.05f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.24f), new Keyframe(1f, 0f)); _trail.time = 0.25f;
            _trail.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? sh);
            _trail.startColor = new Color(0.08f, 0.05f, 0.05f, 0.75f); _trail.endColor = new Color(0.08f, 0.05f, 0.05f, 0f);
            _trail.emitting = false;
            _body.SetActive(false);
            int layer = VfxAfterFog308.Layer; if (layer >= 0) { gameObject.layer = layer; _body.layer = layer; }
            PreviousPosition = transform.position;
        }

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() { Active.Remove(this); }

        void Update()
        {
            if (!_alive) return;
            _t += Time.deltaTime;
            PreviousPosition = transform.position;
            if (_t < Delay) return;
            if (!_body.activeSelf) { _body.SetActive(true); _trail.emitting = true; }
            transform.position += Velocity * Time.deltaTime;
            if (_t > Delay + 6f) Destroy(gameObject);
        }

        /// <summary>The shot met the membrane at <paramref name="point"/> (outward <paramref name="normal"/>): its body goes, ink bursts out from there and falls.</summary>
        public void Dissolve(Vector3 point, Vector3 normal)
        {
            if (!_alive) return;
            _alive = false;
            Active.Remove(this);
            transform.position = point;
            if (_body != null) _body.SetActive(false);
            if (_trail != null) _trail.emitting = false;
            Burst(transform, point, normal);
            Destroy(gameObject, 2.2f);
        }

        /// <summary>D308-156: the same ink burst for a REAL shot a ward took (the shot itself is the game's; this only draws its end at <paramref name="point"/>).
        /// Builds a short-lived holder that removes itself.</summary>
        public static void InkBurst(Vector3 point, Vector3 normal)
        {
            if (!Application.isPlaying) return;           // particles need Play; an edit-mode fixture only counts the call
            var go = new GameObject("SpellFxInkBurst");
            go.transform.position = point;
            int layer = VfxAfterFog308.Layer; if (layer >= 0) go.layer = layer;
            Burst(go.transform, point, normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up);
            Destroy(go, 2.2f);
        }

        static void Burst(Transform parent, Vector3 point, Vector3 normal)
        {
            var mat = InkMaterial();
            // drops: thrown back off the membrane and sideways, falling
            var drops = MakeSystem(parent, "InkDrops", point, mat);
            var main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.03f, 0.03f, 0.035f, 0.95f), new Color(0.12f, 0.1f, 0.1f, 0.9f));
            main.gravityModifier = 1.1f;
            var shape = drops.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.05f;
            shape.rotation = Quaternion.LookRotation(normal).eulerAngles;
            var em = drops.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var sol = drops.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.3f)));
            var col = drops.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            drops.Play();
            // the cloud: the shot's own ink spreading where it stopped, like ink let into water
            var cloud = MakeSystem(parent, "InkCloud", point, mat);
            var cm = cloud.main;
            cm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            cm.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            cm.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            cm.startColor = new Color(0.05f, 0.05f, 0.06f, 0.55f);
            cm.gravityModifier = 0.15f;
            var cs = cloud.shape; cs.shapeType = ParticleSystemShapeType.Sphere; cs.radius = 0.08f;
            var ce = cloud.emission; ce.rateOverTime = 0f; ce.SetBursts(new[] { new ParticleSystem.Burst(0f, 7) });
            var csol = cloud.sizeOverLifetime; csol.enabled = true; csol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.0f)));
            var ccol = cloud.colorOverLifetime; ccol.enabled = true;
            var cg = new Gradient(); cg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            ccol.color = cg;
            cloud.Play();
        }

        static ParticleSystem MakeSystem(Transform parent, string name, Vector3 at, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = at;
            int layer = VfxAfterFog308.Layer; if (layer >= 0) go.layer = layer;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.duration = 1f;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        /// <summary>A soft, ragged ink blot made once (no asset needed: this runs in Play from code).</summary>
        static Material InkMaterial()
        {
            if (_inkMat != null) return _inkMat;
            const int S = 64;
            _inkTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SpellFxInkBlot" };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float ang = Mathf.Atan2(dy, dx), rag = 0.78f + 0.12f * Mathf.Sin(ang * 5f) + 0.07f * Mathf.Sin(ang * 11f + 1.3f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / rag;
                    float a = Mathf.Clamp01((1f - r) * 4f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            _inkTex.SetPixels32(px); _inkTex.Apply(false);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _inkMat = new Material(sh) { name = "SpellFxInkBlot", mainTexture = _inkTex };
            if (_inkMat.HasProperty("_BaseMap")) _inkMat.SetTexture("_BaseMap", _inkTex);
            // transparent alpha blend on the URP particle shader
            if (_inkMat.HasProperty("_Surface")) { _inkMat.SetFloat("_Surface", 1f); _inkMat.SetFloat("_Blend", 0f); }
            _inkMat.SetOverrideTag("RenderType", "Transparent");
            _inkMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _inkMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); _inkMat.SetInt("_ZWrite", 0);
            _inkMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _inkMat.renderQueue = 3000;
            return _inkMat;
        }

        void OnDestroy() { Active.Remove(this); }
    }
}
