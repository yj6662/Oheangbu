using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>넛 (fire, ㅓ attachment, 가른다) - free showcase batch 6, concept neot_a (the brush becomes a long blade of white-hot flame with a burning ink edge;
    /// the swing leaves a huge molten crescent, the sill sliced in two with glowing molten faces, the bear's armour flung off in burning shards, its seam
    /// burning). Executor facts only: the attachment's 6 s = the flame sword on the brush (a white-hot core sliver past the bristles, a wider orange flame sheath
    /// round it, a dark ink back edge, tongues licking off it and embers), every swing contact = an orange crescent (the base) with fire tongues and sparks
    /// thrown along it, the struck body's cut flares (burning ink drops), the strip book opens the seam, the body action burns it 2 s -> ash rising (CSV 끝).
    /// The attachment's end = the sword gutters from the tip down into embers and smoke.</summary>
    public sealed class Vol2FsNeot : Vol2FsCutWear
    {
        static readonly Color Hot = new Color(1f, 0.5f, 0.15f);
        Mesh _coreMesh, _sheathMesh, _edgeMesh, _heatMesh; readonly Vol2FsStrips _core = new Vol2FsStrips(), _sheath = new Vol2FsStrips(), _edge = new Vol2FsStrips(), _heat = new Vol2FsStrips();
        ParticleSystem _flame, _ember, _smoke;
        Light _glow;
        float _endAt = -1f;
        protected override Color SlashTint => new Color(1f, 0.55f, 0.2f);
        protected override Color CutGlow => new Color(1.9f, 0.75f, 0.2f);
        protected override float SlashWidth => 0.42f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var core = M("fs_streak", new Color(1f, 0.82f, 0.55f), 3.4f, 1f); core.NoiseAmt = 0.15f; core.Soft = 0.02f; core.Erode = 0.08f; core.Scroll = new Vector2(-1.5f, 0f);
            Vol2Fs.MeshObject(root, "FlameCore", Kit.Mat(core), out _coreMesh);
            var sh = Vol2FsB3.Fire(1.6f, 0.9f); sh.Scroll = new Vector2(-1.2f, 0f);
            Vol2Fs.MeshObject(root, "FlameSheath", Kit.Mat(sh), out _sheathMesh);
            var heat = M("fs_streak", new Color(1f, 0.42f, 0.1f), 3f, 1f); heat.NoiseAmt = 0.3f; heat.Soft = 0.05f; heat.Erode = 0.15f; heat.Scroll = new Vector2(-1.8f, 0f);
            Vol2Fs.MeshObject(root, "FlameHeat", Kit.Mat(heat), out _heatMesh);      // r2: the incandescent body of the blade (r1: a few small flames on the shaft only)
            Vol2Fs.MeshObject(root, "InkEdge", Kit.InkSolid("fs_stroke2", InkC, new Color(1.6f, 0.5f, 0.12f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _edgeMesh);
            _flame = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.9f)), Life0 = 0.22f, Life1 = 0.5f, Speed0 = 0.4f, Speed1 = 1.4f, Size0 = 0.14f, Size1 = 0.3f, Gravity = -0.8f, Noise = 0.8f, NoiseFreq = 1.5f, SizeStart = 0.5f, SizePeak = 1f, SizeEnd = 0.3f, Max = 900 }, root, "Flame");
            _ember = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.6f, 0.25f), 4.5f, 1f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.3f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.6f, Max = 600 }, root, "Ember");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.4f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.25f, Size1 = 0.6f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.6f, Gravity = -0.15f, Drag = 1f, Max = 150 }, root, "Smoke");
            foreach (var ps in new[] { _flame, _ember, _smoke }) ps.Play(false);
            _glow = Vol2Fs.PointLight(root, "SwordGlow", Hot, 4f);
        }

        protected override void OnLineEnd()
        {
            _endAt = Now(NowUs);
            for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_smoke, Shaft(Random.Range(0.8f, 1.9f)), Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f, -1f, -1f);
            Vol2FsB1.Emit(_ember, Shaft(1.3f), 60);
        }

        /// <summary>A sword-shaped strip along the shaft from u0 to the point (u1), widest at <paramref name="waist"/>, camera-facing.</summary>
        void Sword(Vol2FsStrips s, float u0, float u1, float width, float waist, float life, float rnd, float wave, float t, float shift = 0f)
        {
            int n = 14; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            Vector3 side = ShaftSide;
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                pts[k] = Shaft(u) + side * (wave * Mathf.Sin(v * 9f - t * 18f) * v + shift * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI));
                float prof = v < waist ? Mathf.Lerp(0.55f, 1f, v / waist) : Mathf.Lerp(1f, 0.02f, Mathf.Pow((v - waist) / (1f - waist), 1.2f));
                w[k] = width * prof; al[k] = life;
            }
            s.Strip(pts, w, null, Color.white, al, rnd, CamPos, 0f, 1.5f);
        }

        protected override void Blade(float t, float life, float warn)
        {
            _core.Clear(); _sheath.Clear(); _edge.Clear(); _heat.Clear();
            float gutter = _endAt < 0f ? 0f : Vol2Fs.Smooth(_endAt, _endAt + 0.5f, t);
            if (life > 0.01f)
            {
                // r2: a thick incandescent blade 1.5 m past the bristles (orange-white core, an orange heat body, a dark ink back edge, big tongues)
                float brush = Mathf.Max(0.5f, (Tip() - Grip()).magnitude);
                float full = 1f + 1.55f / brush;
                float reach = Mathf.Lerp(1.0f, full, Vol2Fs.Smooth(0f, 1f, life)) * (1f - 0.45f * gutter);
                Sword(_edge, 0.6f, reach + 0.04f, 0.5f, 0.28f, life * 0.95f, 0.21f, 0.015f, t, -0.11f);
                Sword(_sheath, 0.62f, reach + 0.14f, 0.82f + 0.08f * Mathf.Sin(t * 23f), 0.3f, life * 0.8f, 0.43f, 0.035f, t);
                Sword(_heat, 0.64f, reach + 0.06f, 0.5f, 0.3f, life, 0.57f, 0.02f, t, 0.03f);
                Sword(_core, 0.68f, reach, 0.2f, 0.34f, life, 0.67f, 0.0f, t, 0.04f);
                if (Time.frameCount % 2 == 0)
                    for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_flame, Shaft(Random.Range(0.75f, reach)) + ShaftSide * Random.Range(-0.2f, 0.2f), Vector3.up * Random.Range(0.6f, 1.6f) - ShaftAxis * 0.5f, Random.Range(0.3f, 0.58f), -1f);
                if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_ember, Shaft(Random.Range(0.8f, reach)), Vector3.up * 0.9f + Random.insideUnitSphere * 0.5f, -1f, -1f);
                _glow.transform.position = Shaft((0.7f + reach) * 0.5f); _glow.intensity = Mathf.Max(_glow.intensity * 0.92f, life * (3.5f + 1.5f * warn + 0.6f * Mathf.Sin(t * 31f)));
            }
            else _glow.intensity = 0f;
            _core.Write(_coreMesh); _sheath.Write(_sheathMesh); _edge.Write(_edgeMesh); _heat.Write(_heatMesh);
        }

        protected override void OnSlash(Slash s, Struck by)
        {
            if (s.Arc)
            {
                for (int k = 0; k < 26; k++)
                {
                    float u = k / 25f; Vector3 p = ArcPoint(s, u), d = (ArcPoint(s, Mathf.Min(1f, u + 0.05f)) - p).normalized;
                    Vol2FsB1.EmitV(_flame, p, d * 2f + Vector3.up * 1.2f, Random.Range(0.22f, 0.42f), -1f);
                    if (k % 2 == 0) Vol2FsB1.EmitV(PSparks, p, (d + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 9f), -1f, -1f);
                }
                return;
            }
            Vector3 c = (s.A + s.B) * 0.5f;
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_flame, Vector3.Lerp(s.A, s.B, Random.value), Vector3.up * Random.Range(0.8f, 2f) + Random.insideUnitSphere * 0.5f, Random.Range(0.25f, 0.45f), -1f);
            Vol2FsB1.Emit(_ember, c, 60); Vol2FsB1.Emit(_smoke, c, 6);
            _glow.transform.position = c; _glow.intensity = 8f;
        }

        protected override void Live(float t, float h) { }
    }
}
