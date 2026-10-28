using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 route C (in-engine, real time) for 우: the breaking-wave whirlpool ring as a live mesh - three walls (outer / main / inner) that sweep up round the
    /// caster, chase each other with the flow, curl inward and wash down - drawn with Oheangbu/SpellFx/Water; ink ribbons ride the faces; spray and glassy beads are
    /// Shuriken particles thrown along the flow. Same wave law as the route-A Cycles render (Tools/Art/SpellVFX308/restart/u_ring_cycles.py) so the two compare fairly.
    /// The look only: laid around the cast origin at the begin, never moved by a contact.</summary>
    public sealed class SpellFxRingWavePart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Shader waterShader, inkShader;
        [SerializeField] float radius = 2.2f;
        [SerializeField] float lifeSeconds = 2.6f;

        const int NU = 160, NV = 18;
        const float Curl0 = 0.16f, Curl1 = 0.55f, Wash0 = 1.05f, Wash1 = 1.7f, Fade0 = 1.6f, Fade1 = 2.4f;
        // radial offset, height scale, lobe phase, flow speed, rise lag, lobes
        static readonly float[,] Layers = { { 0.8f, 0.75f, 0.5f, 0.8f, 0.07f, 6f }, { 0f, 1f, 0f, 1f, 0f, 5f }, { -0.65f, 0.45f, 0.27f, 1.35f, 0.04f, 4f } };
        static readonly Vector2[] Prof = { new Vector2(1.10f, 0f), new Vector2(0.75f, .18f), new Vector2(0.42f, .48f), new Vector2(0.18f, .80f), new Vector2(0.02f, .97f),
                                           new Vector2(-0.10f, 1f), new Vector2(-0.32f, .93f), new Vector2(-0.48f, .76f), new Vector2(-0.52f, .55f) };

        Vector3 _o, _f, _r; float _spin = 1f;
        Mesh _mesh, _inkMesh; Material _water, _ink;
        ParticleSystem _spray, _beads;
        readonly List<Vector3> _v = new List<Vector3>(); readonly List<Vector3> _n = new List<Vector3>(); readonly List<Color> _c = new List<Color>(); readonly List<Vector2> _uv = new List<Vector2>(); readonly List<int> _t = new List<int>();
        struct Rib { public float Th, Span, VA, VB, W, At, Rnd; public int L; }
        readonly List<Rib> _ribs = new List<Rib>();
        float _lastAge = -1f;

        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static float Frac(float x) => x - Mathf.Floor(x);
        static float EaseBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        float Phase(float t) => _spin * (2.4f * t + (8f - 2.4f) * 0.9f * (1f - Mathf.Exp(-t / 0.9f)));
        float Omega(float t) => _spin * (2.4f + 5.6f * Mathf.Exp(-t / 0.9f));
        static float Back(float th) => Sm(0.55f, 1f, Mathf.Abs(th) / Mathf.PI);
        Vector3 Dir(float th) => _f * Mathf.Cos(th) + _r * Mathf.Sin(th);
        static float HMax(float th, int L) => Mathf.Lerp(2.4f, 1.5f, Sm(0.15f, 0.6f, Mathf.Abs(th) / Mathf.PI)) * (1f - 0.95f * Back(th)) * Layers[L, 1];      // r3: the near arc stays low

        float Height(float th, float t, int L)
        {
            float ph = th - Phase(t) * Layers[L, 3];
            float s = Frac(_spin * ph * Layers[L, 5] / (2f * Mathf.PI) + Layers[L, 2]);
            float lobe = s < 0.78f ? Sm(0f, 0.78f, s) : 1f - Sm(0.78f, 1f, s);
            float bump = 0.5f + 0.42f * lobe + 0.1f * Mathf.Sin(7f * ph + 3f * L) + 0.06f * Mathf.Sin(13f * ph + 5f * t);
            float lag = Frac(_spin * th / (2f * Mathf.PI)) * 0.22f + Layers[L, 4];
            return Mathf.Max(0f, HMax(th, L) * bump * EaseBack((t - lag) / 0.32f));
        }

        static Vector2 ProfAt(float v)
        {
            float f = Mathf.Clamp01(v) * (Prof.Length - 1); int i = Mathf.Min(Prof.Length - 2, (int)f); float u = f - i;
            Vector2 p0 = Prof[Mathf.Max(0, i - 1)], p1 = Prof[i], p2 = Prof[i + 1], p3 = Prof[Mathf.Min(Prof.Length - 1, i + 2)];
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        Vector3 Surface(float th, float v, float t, int L, out float h)
        {
            h = Height(th, t, L);
            float curl = Sm(Curl0, Curl1, t), wash = Sm(Wash0, Wash1, t);
            float ks = 0.45f + 0.35f * Mathf.Max(h, 0.4f);
            Vector2 p = ProfAt(v);
            float lip = Sm(0.5f, 0.75f, v); Vector2 up = new Vector2(-0.02f, 0.97f + (v - 0.62f) * 0.55f);
            p = Vector2.Lerp(p, Vector2.Lerp(p, up, lip), 1f - curl);
            float dr = p.x * ks, y = p.y * h * (1f - 0.9f * wash);
            dr -= wash * 1.3f * Sm(0.35f, 1f, v); dr += wash * 0.35f * (1f - Sm(0f, 0.3f, v));
            float lean = _spin * ((0.18f + 0.3f * curl) * Mathf.Min(1f, h / 1.6f) * Sm(0.4f, 1f, v) + 0.6f * wash * Sm(0.3f, 1f, v));
            Vector3 q = _o + Dir(th + lean) * Mathf.Max(0.15f, radius + Layers[L, 0] + dr); q.y = _o.y + Mathf.Max(0.02f, y);
            return q;
        }

        public void OnBegin(SpellFxCast cast)
        {
            _o = cast.Origin; _f = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            _spin = (cast.CastId & 1) == 0 ? 1f : -1f;
            var ws = waterShader != null ? waterShader : Shader.Find("Oheangbu/SpellFx/Water");
            var ks = inkShader != null ? inkShader : Shader.Find("Universal Render Pipeline/Unlit");
            _water = new Material(ws) { hideFlags = HideFlags.DontSave };
            _ink = new Material(ks) { hideFlags = HideFlags.DontSave }; if (_ink.HasProperty("_BaseColor")) _ink.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.025f));
            _mesh = MakeObject("Wave", _water); _inkMesh = MakeObject("Ink", _ink);
            _spray = MakeParticles("Spray", new Color(0.95f, 0.97f, 0.98f, 0.95f), 0.012f, 0.035f, 4000);
            _beads = MakeParticles("Beads", new Color(0.88f, 0.94f, 0.95f, 0.7f), 0.035f, 0.07f, 600);
            var rnd = new System.Random((int)(cast.CastId * 7919 + 31));
            for (int k = 0; k < 24; k++)
                _ribs.Add(new Rib { Th = (k + 0.5f) / 24f * 2f * Mathf.PI - Mathf.PI, Span = 1f + 0.8f * (float)rnd.NextDouble(), VA = 0.5f + 0.15f * (float)rnd.NextDouble(),
                    VB = 0.8f + 0.15f * (float)rnd.NextDouble(), W = 0.07f + 0.07f * (float)rnd.NextDouble(), At = Curl0 + 0.15f * (float)rnd.NextDouble(), Rnd = (float)rnd.NextDouble(), L = rnd.NextDouble() < 0.3 ? 2 : 1 });
            VfxAfterFog308.Route(transform);
        }

        Mesh MakeObject(string name, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave }; mesh.MarkDynamic(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mesh;
        }

        static Texture2D _dot;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => _dot = null;
        static Texture2D Dot()
        {
            if (_dot != null) return _dot;
            const int S = 64; _dot = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "SpellFxDot", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) / 0.18f), hi = Mathf.Clamp01(1f - new Vector2(dx + 0.3f, dy - 0.35f).magnitude / 0.22f);
                float a = Mathf.Clamp01((0.95f - r) * 14f) * Mathf.Clamp01(0.45f + 0.5f * rim + hi);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
            _dot.SetPixels(px); _dot.Apply(true, true);
            return _dot;
        }

        ParticleSystem MakeParticles(string name, Color col, float s0, float s1, int max)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = 5f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(s0, s1); main.startColor = col;
            main.gravityModifier = 0.55f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col2 = ps.colorOverLifetime; col2.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col2.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { hideFlags = HideFlags.DontSave };
            m.SetTexture("_BaseMap", Dot()); m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.004f; r.lengthScale = 1f;      // r2: shorter streaks (r1 read as white flakes on the ground)
            go.SetActive(true); ps.Play();
            return ps;
        }

        public void OnTick(SpellFxCast cast, float age)
        {
            float t = age;
            float dt = _lastAge < 0f ? 0f : Mathf.Clamp(t - _lastAge, 0f, 0.1f); _lastAge = t;
            BuildWave(t);
            BuildInk(t);
            if (dt > 0f) EmitSpray(t, dt);
            if (_water != null) _water.SetFloat("_Age", t);
        }

        void BuildWave(float t)
        {
            _v.Clear(); _n.Clear(); _c.Clear(); _uv.Clear(); _t.Clear();
            float life = 1f - Sm(Fade0, Fade1, t), wash = Sm(Wash0, Wash1, t);
            if (life > 0.01f)
                for (int L = 0; L < 3; L++)
                {
                    int b = _v.Count;
                    float uShift = Phase(t) * Layers[L, 3] / (2f * Mathf.PI) * 10f;
                    for (int iu = 0; iu < NU; iu++)
                    {
                        float th = (iu / (NU - 1f) * 2f - 1f) * Mathf.PI;
                        for (int iv = 0; iv < NV; iv++)
                        {
                            float v = iv / (NV - 1f);
                            var p = Surface(th, v, t, L, out float h);
                            _v.Add(p);      // r2: the mesh objects sit at the world origin (r1 subtracted the root's position too - the ring was drawn ~3.7 km away)
                            float crest = Mathf.Max(0f, 1f - Mathf.Abs(v - 0.82f) / 0.2f);
                            float foam = h > 0.05f ? Mathf.Min(1f, 0.85f * crest + 0.25f * Sm(0.6f, 1f, v) + 0.6f * wash) : 0f;
                            float vis = (h > 0.03f ? 1f : 0f) * life * Sm(0f, 0.1f, v) * (0.75f + 0.25f * (1f - Back(th)));
                            _c.Add(new Color(foam, 0f, 0f, vis));
                            _uv.Add(new Vector2(iu / (NU - 1f) * 10f - uShift + 3.7f * L, v));
                        }
                    }
                    for (int iu = 0; iu < NU - 1; iu++)
                        for (int iv = 0; iv < NV - 1; iv++)
                        {
                            int a = b + iu * NV + iv, a1 = a + 1, c = a + NV, d = c + 1;
                            if (_c[a].a + _c[a1].a + _c[c].a + _c[d].a <= 0.001f) continue;
                            _t.Add(a); _t.Add(c); _t.Add(a1); _t.Add(a1); _t.Add(c); _t.Add(d);
                        }
                }
            _mesh.Clear(); _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_t, 0); _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
        }

        void BuildInk(float t)
        {
            _v.Clear(); _t.Clear(); _uv.Clear();
            float dry = Sm(Wash0 - 0.1f, Wash0 + 0.5f, t);
            if (dry < 0.98f)
                foreach (var r in _ribs)
                {
                    float g = Mathf.Clamp01((t - r.At) / 0.35f); if (g < 0.02f) continue;
                    const int n = 22; int prev = -1;
                    for (int j = 0; j < n; j++)
                    {
                        float u = j / (n - 1f) * g;
                        float th = r.Th + 0.85f * Phase(t) * Layers[r.L, 3] + _spin * r.Span * (u - g);
                        float v = r.VA + (r.VB - r.VA) * Mathf.Pow(u, 1.6f) + 0.05f * Mathf.Sin(u * 6.9f + r.Rnd * 6f);
                        var p = Surface(th, v, t, r.L, out float h);
                        if (h < 0.2f || Back(th) > 0.6f) { prev = -1; continue; }
                        var pv = Surface(th, Mathf.Min(1f, v + 0.04f), t, r.L, out _); var pt = Surface(th + 0.02f, v, t, r.L, out _);
                        Vector3 nrm = Vector3.Cross(pt - p, pv - p).normalized, side = (pv - p).normalized;
                        float wd = r.W * (0.25f + 0.75f * Mathf.Sin(Mathf.Clamp01(u / Mathf.Max(0.01f, g)) * Mathf.PI)) * (1f - 0.7f * dry);
                        Vector3 c = p + nrm * 0.03f;
                        int k0 = _v.Count; _v.Add(c - side * wd); _v.Add(c + side * wd); _uv.Add(new Vector2(u, 0)); _uv.Add(new Vector2(u, 1));
                        if (prev >= 0) { _t.Add(prev); _t.Add(k0); _t.Add(prev + 1); _t.Add(prev + 1); _t.Add(k0); _t.Add(k0 + 1); }
                        prev = k0;
                    }
                }
            _inkMesh.Clear(); _inkMesh.SetVertices(_v); _inkMesh.SetUVs(0, _uv); _inkMesh.SetTriangles(_t, 0); _inkMesh.RecalculateBounds();
        }

        void EmitSpray(float t, float dt)
        {
            if (t < 0.06f || t > Wash1) return;
            int n = Mathf.RoundToInt(3000f * dt * (1f - 0.6f * Sm(Wash0, Wash1, t)));
            var ep = new ParticleSystem.EmitParams();
            for (int k = 0; k < n; k++)
            {
                int L = Random.value < 0.5f ? 1 : (Random.value < 0.5f ? 0 : 2); float th = Random.Range(-Mathf.PI, Mathf.PI);
                var c = Surface(th, 0.75f, t, L, out float h);
                if (h < 0.35f * Layers[L, 1] || Back(th) > 0.6f) continue;
                Vector3 outw = Dir(th), tang = Vector3.Cross(Vector3.up, outw);
                float flow = Omega(t) * (radius + Layers[L, 0]);
                bool bead = Random.value < 0.1f;
                ep.position = c + new Vector3(Random.Range(-.1f, .1f), Random.Range(-.05f, .1f), Random.Range(-.1f, .1f));
                ep.velocity = Vector3.up * Random.Range(1.5f, 4.5f) * (0.5f + Mathf.Min(1f, h / 2f)) + outw * Random.Range(-1.2f, 1.8f) + tang * (flow * Random.Range(0.3f, 0.6f) + Random.Range(-0.6f, 0.6f));
                ep.startLifetime = Random.Range(0.4f, 1.0f);
                ep.startSize = bead ? Random.Range(0.025f, 0.05f) : Random.Range(0.008f, 0.022f);
                (bead ? _beads : _spray).Emit(ep, 1);
            }
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh); if (_inkMesh != null) Destroy(_inkMesh);
            if (_water != null) Destroy(_water); if (_ink != null) Destroy(_ink);
        }
    }
}
