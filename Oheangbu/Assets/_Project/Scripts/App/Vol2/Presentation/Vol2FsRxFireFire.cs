using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>화>화 비화 - free showcase batch 8, concept ff_a (flare one layer): the fire shape dives into the enemy's burning patch; a ring of embers bursts at
    /// the joining point and the flames leap one full step higher - a second, wider crown of dark-cored saw-toothed tongues (to ~2 m) over the old bed, black
    /// ink-soot curling off their tips, the bed itself wider and higher. It stays the enemy's fire (soot-dark cores, no white heat). It burns for the thickened
    /// patch's renewed life, sinking a little toward its end; Expired = the crown dies down into embers and ash; any other end = it is snuffed at once.</summary>
    public sealed class Vol2FsRxFireFire : Vol2FsReact
    {
        ParticleSystem _crown, _tips, _soot, _embers, _ash;
        Light _glow; Vol2FsRxSameFx _fx; bool _out; float _acc, _accTip;
        const float CrownRate = 260f, TipRate = 75f, SootRate = 18f;      // r2: x1.8 (r1 ~1 m against the concept's 3 - 4 m)

        protected override void OnBuild()
        {
            _fx = new Vol2FsRxSameFx(Kit, Root.transform, AccA, 7f);
            var lick = Vol2FsPolish.Lick(1.25f, 0.55f, 1.0f);
            var crown = Vol2FsPolish.Licks(Kit.Mat(lick), 0.85f, 1.6f, 0.6f, 1.05f, 2.4f, 900, 2.6f);
            crown.Loop = true; crown.Rate = 0f; crown.Radius = Radius * 2.1f; crown.ShapeScale = new Vector3(1f, 0.06f, 1f);
            _crown = Vol2Fs.Build(crown, Root.transform, "SameCrown");
            // the tall saw tips: fewer, taller, faster tongues
            var tips = Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.45f, 0.6f, 1.1f)), 0.6f, 1.05f, 0.5f, 0.85f, 4.6f, 400, 3.3f);
            tips.Loop = true; tips.Rate = 0f; tips.Radius = Radius * 1.5f; tips.ShapeScale = new Vector3(1f, 0.06f, 1f);
            _tips = Vol2Fs.Build(tips, Root.transform, "SameTips");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.6f)), Loop = true, Rate = 0f, Life0 = 1.2f, Life1 = 2f, Speed0 = 0.3f, Speed1 = 0.8f, Size0 = 0.9f, Size1 = 1.8f, Radius = Radius * 1.7f, ShapeScale = new Vector3(1f, 0.1f, 1f), VelOverLife = new Vector3(0f, 2.2f, 0f), Noise = 0.7f, NoiseFreq = 0.6f, SizeStart = 0.5f, SizePeak = 1f, SizeEnd = 1.8f, Max = 120 }, Root.transform, "SameSoot");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.5f, 0.15f), 4f, 1f)), Loop = true, Rate = 0f, Life0 = 0.6f, Life1 = 1.5f, Speed0 = 0.5f, Speed1 = 2.2f, Size0 = 0.04f, Size1 = 0.08f, Radius = Radius * 2f, ShapeScale = new Vector3(1f, 0.1f, 1f), Gravity = -0.45f, Drag = 0.6f, Noise = 1f, NoiseFreq = 1.5f, Max = 900 }, Root.transform, "SameEmbers");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 1.1f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.25f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 300 }, Root.transform, "SameAsh");
            foreach (var ps in new[] { _crown, _tips, _soot, _embers, _ash }) { ps.transform.position = G(P) + Vector3.up * 0.08f; ps.Play(false); }
            _glow = Vol2Fs.PointLight(Root.transform, "SameFireGlow", new Color(1f, 0.45f, 0.15f), 10f);
            _glow.transform.position = P + Vector3.up * 1.0f;
            // the merge: a ring of embers bursts out of the joining point, the bed flares
            Vector3 at = P + Vector3.up * 0.3f - Dir * Radius * 0.5f;
            for (int k = 0; k < 300; k++) { float a = k / 300f * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); Vol2FsB1.EmitV(_embers, at, o * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.6f, 3.4f), -1f, -1f); }
            // r2: the flare - a sheaf of tall tongues leaps out of the bed at the merge (bigger than the acting fireball)
            for (int k = 0; k < 40; k++) { Vector2 o = Random.insideUnitCircle * Radius * 1.6f; Vol2FsB1.EmitV(_tips, G(P + new Vector3(o.x, 0f, o.y)) + Vector3.up * 0.1f, Vector3.up * Random.Range(3f, 6f), Random.Range(1.0f, 1.6f), Random.Range(0.6f, 0.9f)); }
            HitStop(0f, 0.06f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float pop = Vol2FsRxSameFx.Pop(t, 0.2f), exit = Vol2FsRxSameFx.Exit(t, ContactEndAt, ContactWhy, 0.9f), near = Vol2FsRxSameFx.Near(t, ContactExpireRel, 2.5f);
            // the crown: up in 0.2 s, then burning, sinking toward the end of the renewed life
            float k = Mathf.Clamp01(pop) * (1f - 0.45f * near) * (1f - exit);
            // r3: the crown is placed tongue by tongue on three rings (r2's disc emitter read side-on as a flat rectangular curtain): heights vary round each
            // ring (gaps where it is low), the inner ring tallest, a few fixed spots throw the tallest licks, soot-dark cores at the bases
            float boost = 1f + 0.6f * (1f - Vol2Fs.Smooth(0.2f, 0.7f, t));
            _acc += CrownRate * k * boost * Mathf.Max(0f, Time.deltaTime);
            while (_acc >= 1f)
            {
                _acc -= 1f;
                int ring = Random.value < 0.3f ? 0 : Random.value < 0.6f ? 1 : 2;
                float a = Random.value * Mathf.PI * 2f, rr = Radius * (ring == 0 ? 0.55f : ring == 1 ? 1.25f : 1.95f) * Random.Range(0.85f, 1.15f);
                float hf = Mathf.PerlinNoise(a * 1.4f + ring * 3.1f, t * 0.6f + Seed * 0.01f);
                if (hf < 0.32f && ring > 0) continue;                                   // uneven: low gaps in the outer rings
                float h = (ring == 0 ? 1.35f : ring == 1 ? 1.0f : 0.7f) * (0.45f + 1.1f * hf);
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vol2FsB1.EmitV(_crown, G(P + o * rr) + Vector3.up * 0.05f, Vector3.up * (1.2f + 1.6f * h) + o * 0.3f, 0.55f + 0.6f * h, 0.5f + 0.35f * h);
                if (Random.value < 0.12f) Vol2FsB1.EmitV(_soot, G(P + o * rr * 0.9f) + Vector3.up * 0.3f, Vector3.up * 0.8f, 0.6f + 0.5f * h, -1f);
            }
            _accTip += TipRate * k * (1f + 0.25f * Mathf.Sin(t * 11f)) * Mathf.Max(0f, Time.deltaTime);
            while (_accTip >= 1f)
            {
                _accTip -= 1f;
                int spot = Random.Range(0, 5);
                float a = (Vol2Fs.Hash(Seed, spot) + 0.03f * Random.Range(-1f, 1f)) * Mathf.PI * 2f, rr = Radius * (0.3f + 1.3f * Vol2Fs.Hash(Seed, spot + 9));
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vol2FsB1.EmitV(_tips, G(P + o * rr) + Vector3.up * 0.1f, Vector3.up * Random.Range(3.5f, 5.5f), Random.Range(0.7f, 1.1f), -1f);
            }
            var e3 = _soot.emission; e3.rateOverTime = exit > 0f ? SootRate * 1.8f * (1f - exit) : SootRate * 0.5f * k;
            var e4 = _embers.emission; e4.rateOverTime = 30f * k + (exit > 0f ? 120f * (1f - exit) : 0f);
            if (Patch != null)
            {
                Patch.Pose((1f + 0.6f * pop) * (1f - 0.8f * exit), 0.08f * exit, 1f + 0.5f * pop);
                if (exit > 0f) Patch.StopParticles();
            }
            if (exit > 0f && !_out)
            {
                _out = true;
                bool expired = ContactWhy == Vol2MaterialEnd.Expired;
                for (int i = 0; i < (expired ? 90 : 30); i++) Vol2FsB1.EmitV(_ash, G(P) + Vector3.up * Random.Range(0.3f, 2.2f) + Random.insideUnitSphere * Radius * 2f, Vector3.up * Random.Range(0.4f, 1.4f) + Random.insideUnitSphere * 0.5f, -1f, -1f);
            }
            if (_glow != null) _glow.intensity = (4.5f + 1f * Mathf.PerlinNoise(t * 7f, 0.3f)) * k + 0.8f * Mathf.Clamp01(pop) * (1f - exit) * (1f - k);
            _fx.Draw(t, P, Dir, Ground, Radius, Radius * 2.4f, cam, 2.6f);
        }
    }
}
