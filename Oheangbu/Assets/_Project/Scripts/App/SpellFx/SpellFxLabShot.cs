using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 우 (2026-10-29, the user: "투사체가 먹이 되어 사라지는 효과도" -> "겉모습만 (판정 없음)"): a LOOK-ONLY enemy shot for the lab shoots
    /// (SpellFxShot shots=N). It waits <see cref="Delay"/> seconds, flies straight at its speed, and is nothing to any rule: no collider, no damage, not an
    /// IVol2Projectile. A presentation part (the 우 dome) that sees it cross its membrane calls <see cref="Dissolve"/>: the shot turns to ink - its body goes,
    /// a burst of ink drops and a small ink cloud spread from the contact and fall.
    /// The live list is static; it is cleared on play start (domain reload is off in this project).</summary>
    /// <summary>What a taken shot turns into (2026-10-30, the five ㅜ wards): 수 ink/water, 목 leaves, 화 embers and ash, 토 clods and dust, 금 shards.</summary>
    public enum SpellFxBurstBody { Water, Wood, Fire, Earth, Metal, Armour }

    public sealed class SpellFxLabShot : MonoBehaviour
    {
        public static readonly List<SpellFxLabShot> Active = new List<SpellFxLabShot>(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active.Clear(); _inkTex = null; _inkMat = null; _plateTex = null; _plateMat = null; }

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
        public void Dissolve(Vector3 point, Vector3 normal) => Dissolve(point, normal, SpellFxBurstBody.Water);

        public void Dissolve(Vector3 point, Vector3 normal, SpellFxBurstBody body)
        {
            if (!_alive) return;
            _alive = false;
            Active.Remove(this);
            transform.position = point;
            if (_body != null) _body.SetActive(false);
            if (_trail != null) _trail.emitting = false;
            Burst(transform, point, normal, body);
            Destroy(gameObject, 2.2f);
        }

        /// <summary>D308-156: the same ink burst for a REAL shot a ward took (the shot itself is the game's; this only draws its end at <paramref name="point"/>).
        /// Builds a short-lived holder that removes itself.</summary>
        public static void InkBurst(Vector3 point, Vector3 normal) => InkBurst(point, normal, SpellFxBurstBody.Water);

        /// <param name="edge">Optional rim tint for the bits (Fable 낫 r1: the fire body's armour plates stayed jade - the caller passes its own body colour).</param>
        public static void InkBurst(Vector3 point, Vector3 normal, SpellFxBurstBody body, Color? edge = null)
        {
            if (!Application.isPlaying) return;           // particles need Play; an edit-mode fixture only counts the call
            var go = new GameObject("SpellFxInkBurst");
            go.transform.position = point;
            int layer = VfxAfterFog308.Layer; if (layer >= 0) go.layer = layer;
            Burst(go.transform, point, normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up, body, edge);
            Destroy(go, 2.2f);
        }

        /// <summary>The look of one body's burst: the thrown bits and the cloud they leave.</summary>
        struct BurstLook
        {
            public Color A, B, Cloud; public Vector2 Speed, Size, CloudSize; public float Gravity, CloudGravity; public int Count, CloudCount; public bool Shards;
        }

        static BurstLook LookOf(SpellFxBurstBody b)
        {
            switch (b)
            {
                case SpellFxBurstBody.Wood:   // leaves torn off, drifting down slowly; a pale green dust
                    return new BurstLook { A = new Color(0.12f, 0.25f, 0.08f, 0.95f), B = new Color(0.32f, 0.45f, 0.16f, 0.9f), Speed = new Vector2(0.6f, 1.8f), Size = new Vector2(0.07f, 0.18f), Gravity = 0.35f, Count = 32, Cloud = new Color(0.25f, 0.32f, 0.18f, 0.35f), CloudSize = new Vector2(0.3f, 0.55f), CloudGravity = 0.05f, CloudCount = 6 };
                case SpellFxBurstBody.Fire:   // embers flying up, dark ash smoke rising
                    return new BurstLook { A = new Color(1f, 0.42f, 0.08f, 1f), B = new Color(1f, 0.78f, 0.3f, 1f), Speed = new Vector2(1.0f, 3.0f), Size = new Vector2(0.03f, 0.08f), Gravity = -0.4f, Count = 46, Cloud = new Color(0.12f, 0.1f, 0.09f, 0.6f), CloudSize = new Vector2(0.35f, 0.6f), CloudGravity = -0.12f, CloudCount = 7 };
                case SpellFxBurstBody.Earth:  // clods dropping hard, ochre dust
                    return new BurstLook { A = new Color(0.28f, 0.19f, 0.11f, 1f), B = new Color(0.46f, 0.34f, 0.2f, 1f), Speed = new Vector2(0.5f, 2.0f), Size = new Vector2(0.08f, 0.22f), Gravity = 2.0f, Count = 26, Cloud = new Color(0.5f, 0.4f, 0.28f, 0.45f), CloudSize = new Vector2(0.4f, 0.7f), CloudGravity = 0.04f, CloudCount = 7 };
                case SpellFxBurstBody.Armour: // 2026-10-30 갓 (Fable 갓 r2 #1): the guard torn off - big dark ink plates with a jade cast, flung out, falling hard
                    return new BurstLook { A = new Color(0.05f, 0.07f, 0.06f, 1f), B = new Color(0.2f, 0.42f, 0.32f, 1f), Speed = new Vector2(2.2f, 4.0f), Size = new Vector2(0.16f, 0.32f), Gravity = 1.8f, Count = 9, Cloud = new Color(0.05f, 0.06f, 0.05f, 0.45f), CloudSize = new Vector2(0.3f, 0.5f), CloudGravity = 0.1f, CloudCount = 3 };
                case SpellFxBurstBody.Metal:  // bright shards flung fast, a faint grey haze
                    return new BurstLook { A = new Color(0.75f, 0.77f, 0.82f, 1f), B = new Color(0.97f, 0.97f, 1f, 1f), Speed = new Vector2(2.0f, 4.5f), Size = new Vector2(0.03f, 0.09f), Gravity = 1.4f, Count = 40, Cloud = new Color(0.6f, 0.6f, 0.65f, 0.25f), CloudSize = new Vector2(0.25f, 0.45f), CloudGravity = 0.02f, CloudCount = 4, Shards = true };
                default:                      // 수: ink drops and an ink cloud (as before)
                    return new BurstLook { A = new Color(0.03f, 0.03f, 0.035f, 0.95f), B = new Color(0.12f, 0.1f, 0.1f, 0.9f), Speed = new Vector2(0.8f, 2.6f), Size = new Vector2(0.06f, 0.2f), Gravity = 1.1f, Count = 40, Cloud = new Color(0.05f, 0.05f, 0.06f, 0.55f), CloudSize = new Vector2(0.35f, 0.65f), CloudGravity = 0.15f, CloudCount = 7 };
            }
        }

        static void Burst(Transform parent, Vector3 point, Vector3 normal, SpellFxBurstBody body, Color? edge = null)
        {
            var look = LookOf(body);
            if (edge.HasValue) { var e = edge.Value; e.a = 1f; look.B = e; }
            var mat = body == SpellFxBurstBody.Armour ? PlateMaterial() : InkMaterial();
            // drops: thrown back off the membrane and sideways, falling
            var drops = MakeSystem(parent, "InkDrops", point, mat);
            var main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(look.Speed.x, look.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(look.Size.x, look.Size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(look.A, look.B);
            main.gravityModifier = look.Gravity;
            if (look.Shards) { var r = drops.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2.5f; }
            if (body == SpellFxBurstBody.Armour) { main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); var rot = drops.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f); }
            var shape = drops.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.05f;
            shape.rotation = Quaternion.LookRotation(normal).eulerAngles;
            var em = drops.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)look.Count) });
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
            cm.startSize = new ParticleSystem.MinMaxCurve(look.CloudSize.x, look.CloudSize.y);
            cm.startColor = look.Cloud;
            cm.gravityModifier = look.CloudGravity;
            var cs = cloud.shape; cs.shapeType = ParticleSystemShapeType.Sphere; cs.radius = 0.08f;
            var ce = cloud.emission; ce.rateOverTime = 0f; ce.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)look.CloudCount) });
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

        /// <summary>갓's torn guard (Fable 갓 r4 #1: "angular plates, not round dots"): an irregular four-cornered plate, dark inside with a lighter rim
        /// (the particle colour tints it: ink inside, jade at the edge). Made once in code.</summary>
        static Material PlateMaterial()
        {
            if (_plateMat != null) return _plateMat;
            const int S = 64;
            _plateTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SpellFxArmourPlate" };
            var px = new Color32[S * S];
            Vector2[] c = { new Vector2(-0.82f, -0.55f), new Vector2(0.7f, -0.8f), new Vector2(0.85f, 0.45f), new Vector2(-0.4f, 0.78f) };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2((x + 0.5f) / S * 2f - 1f, (y + 0.5f) / S * 2f - 1f);
                    float d = float.MaxValue; bool inside = true;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector2 a = c[k], b = c[(k + 1) % 4], e = b - a, n = new Vector2(e.y, -e.x).normalized;
                        float s = Vector2.Dot(p - a, n);                // > 0 outside this edge (clockwise corners)
                        if (s > 0f) inside = false;
                        d = Mathf.Min(d, -s);
                    }
                    float alpha = inside ? Mathf.Clamp01(d * 40f) : 0f;
                    float rim = inside ? Mathf.Clamp01(1f - d * 9f) : 0f;
                    byte v = (byte)(Mathf.Lerp(0.35f, 1f, rim) * 255f);
                    px[y * S + x] = new Color32(v, v, v, (byte)(alpha * 255f));
                }
            _plateTex.SetPixels32(px); _plateTex.Apply(false);
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _plateMat = new Material(InkMaterial()) { name = "SpellFxArmourPlate", mainTexture = _plateTex };
            if (_plateMat.HasProperty("_BaseMap")) _plateMat.SetTexture("_BaseMap", _plateTex);
            return _plateMat;
        }

        static Texture2D _plateTex;
        static Material _plateMat;

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
                    // 2026-10-30 (Fable g r1-r3: "a five-point star"): no dominant lobe count - a round drop with an uneven, soft rim
                    float ang = Mathf.Atan2(dy, dx), rag = 0.84f + 0.05f * Mathf.Sin(ang * 3f + 0.4f) + 0.04f * Mathf.Sin(ang * 7f + 1.3f) + 0.025f * Mathf.Sin(ang * 13f + 2.1f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / rag;
                    float a = Mathf.Clamp01((1f - r) * 3f);
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
