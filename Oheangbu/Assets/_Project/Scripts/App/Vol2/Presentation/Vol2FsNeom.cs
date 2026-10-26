using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>넘 (fire, ㅓ attachment, 심는다) - free showcase batch 5, concept neom_a (smouldering embers seeded along the caster's sleeve hem and brush shaft,
    /// glowing in a slow pulse; when the caster is struck they blast outward as a ring of fire racing over the ground, flame streaks radiating from the feet,
    /// scorching every bear that close). Unlike 너 (a fire wheel that blooms a peony on a parry) and 넉 (a sleeve that burns for 6 s) it is a dormant charge
    /// that does nothing until the caster bleeds. Executor facts only: the start = embers catch along the shaft and the sleeve -> the wait (each ember breathes
    /// on the heartbeat, thin smoke threads, the ink dial and the 2 m ground arc burn down; the last 0.4 s the embers spit sparks) -> Consumed (the player
    /// really lost HP) = the nova: a flame front runs out to the reach (tall tongues on the front, low fire behind it), streaks of flame and burning ink fly
    /// out flat, embers and soot, each struck bear (OnStrike) bursts into flame and burns its 2 s (the status sink's; mirrored), then the fire dies from its
    /// tips into ash rising (CSV 끝: 끝부터 타 재로 날림(재가 일어 위로)); Expired = the embers gutter out and their ash rises off the sleeve.</summary>
    public sealed class Vol2FsNeom : Vol2FsSeedWear
    {
        static readonly Color Ember = new Color(1f, 0.45f, 0.12f);
        const float BurnSeconds = 2f, FrontSeconds = 0.28f;
        ParticleSystem _coals, _licks, _front, _streaks, _embers, _soot, _ash;
        Light _glow, _blast;
        Mesh _inkMesh, _scorchMesh; readonly Vol2FsStrips _inkS = new Vol2FsStrips(), _scorchS = new Vol2FsStrips(); List<Vol2FsB1.Stroke> _ink;
        readonly List<Struck> _burning = new List<Struck>();
        protected override float SeedU => 0.45f;
        protected override float FireSeconds => BurnSeconds + 1.2f;
        protected override float ExpireSeconds => 1.3f;

        /// <summary>The ember points: along the shaft and round the sleeve hem (fixed by the seed).</summary>
        Vector3 EmberAt(int k)
        {
            if (k < 7) return Shaft(0.1f + 0.12f * k) + Random.insideUnitSphere * 0.015f;
            float a = (k - 7) * 1.3f; return Sleeve() + Quaternion.AngleAxis(a * Mathf.Rad2Deg, ShaftAxis) * Vector3.Cross(ShaftAxis, Vector3.up).normalized * 0.09f;
        }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _coals = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 3.5f, 1f)), Life0 = 0.1f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.08f, Size1 = 0.12f, Max = 200 }, root, "Coals");
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.08f, 0.16f, 0.25f, 0.45f, 1.2f, 300), root, "Licks");
            _front = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.6f, 1.15f)), 0.45f, 0.95f, 0.35f, 0.7f, 2.2f, 700), root, "Front");
            var streak = M("fs_streak", new Color(1f, 0.55f, 0.2f, 1f), 2.6f, 0.9f); streak.NoiseAmt = 0.3f; streak.Ramp = "fs_ramp_fire"; streak.RampScale = 1.2f;
            _streaks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), NoFlip = true, Life0 = 0.25f, Life1 = 0.5f, Speed0 = 9f, Speed1 = 16f, Size0 = 0.12f, Size1 = 0.25f, Drag = 2.2f, Stretch = true, StretchVel = 0.07f, StretchLen = 2.5f, Max = 300 }, root, "Streaks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 3.5f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.25f, Drag = 1f, VelOverLife = new Vector3(0f, 1.2f, 0f), Noise = 1.2f, NoiseFreq = 1.5f, Max = 900 }, root, "Embers");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.4f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.5f, Size1 = 1.2f, Radius = 0.3f, Drag = 1.5f, Tiles = 2, VelOverLife = new Vector3(0f, 0.8f, 0f), SizeEnd = 1.8f, Max = 160 }, root, "Soot");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.04f, Size1 = 0.1f, Radius = 0.3f, Tiles = 2, VelOverLife = new Vector3(0f, 0.9f, 0f), Noise = 0.8f, Spin = 200f, Max = 600 }, root, "Ash");
            foreach (var ps in new[] { _coals, _licks, _front, _streaks, _embers, _soot, _ash }) ps.Play(false);
            _glow = Vol2Fs.PointLight(root, "EmberGlow", Ember, 2.5f);
            _blast = Vol2Fs.PointLight(root, "Blast", Ember, 7f);
            Vol2Fs.MeshObject(root, "InkFlames", Kit.InkSolid("fs_stroke2", InkC, new Color(1.8f, 0.55f, 0.12f, 1f), 0.16f, 0.5f, 0.3f, 1.2f), out _inkMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Opacity = 0.75f; sc.NoiseAmt = 0.4f; sc.Soft = 0.02f; sc.Edge = new Color(1.4f, 0.4f, 0.08f, 1f); sc.EdgeWidth = 0.18f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
        }

        protected override void OnPlant(Pod p) { for (int k = 0; k < 12; k++) Vol2FsB1.Emit(_embers, EmberAt(k), 3); }

        protected override void Fired(Pod p)
        {
            Vector3 feet = FireFeet, c = feet + Vector3.up * 0.9f;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - c));
            _ink = Vol2FsB1.Burst(Seed + 17, 14, side, -FlatN(CamPos - c), 1.4f, 2.8f, 0.22f, 0.45f, 0.25f, 0.1f);
            for (int k = 0; k < 120; k++)
            {
                float a = k / 120f * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vol2FsB1.EmitV(_streaks, feet + d * 0.3f + Vector3.up * Random.Range(0.1f, 0.9f), (d + Vector3.up * Random.Range(-0.05f, 0.25f)) * Random.Range(9f, 16f), -1f, -1f);
            }
            Vol2FsB1.Emit(_embers, c, 400); Vol2FsB1.Emit(_soot, c, 10);
            _blast.transform.position = c; _blast.intensity = 8f; _glow.intensity = 0f;
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (s.World) return;
            _burning.Add(s);
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_front, b.center + Random.insideUnitSphere * 0.5f, Random.onUnitSphere * 1.5f + Vector3.up * 2f, Random.Range(0.5f, 1.1f), Random.Range(0.4f, 0.8f));
            Vol2FsB1.Emit(_embers, b.center, 120);
        }

        protected override void OnPodEnd(Pod p)
        {
            if (Worked(p)) return;
            for (int k = 0; k < 12; k++) Vol2FsB1.Emit(_ash, EmberAt(k), Withered(p) ? 6 : 2);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _inkS.Clear(); _scorchS.Clear();
            if (p != null && !Worked(p))
            {
                float life = PodLife(p, t, 0.4f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                if (p.Started && !p.Ended)
                {
                    for (int k = 0; k < 12; k++)
                    {
                        float br = 0.6f + 0.4f * Mathf.Sin(t * 3f + k * 1.7f);
                        if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_coals, EmberAt(k), Vector3.zero, (0.05f + 0.06f * beat + 0.04f * warn) * life * br, 0.14f);
                        if ((Time.frameCount + k) % 11 == 0) Vol2FsB1.EmitV(_licks, EmberAt(k), Vector3.up * 0.4f, 0.1f * life, 0.35f);
                        if (warn > 0f && (Time.frameCount + k) % 4 == 0) Vol2FsB1.EmitV(_embers, EmberAt(k), Random.onUnitSphere * 2f, -1f, 0.5f);
                    }
                    if (Time.frameCount % 7 == 0) Vol2FsB1.Emit(_soot, SeedPoint() + Vector3.up * 0.1f, 1);
                    _glow.transform.position = SeedPoint(); _glow.intensity = life * (0.5f + 1.6f * beat + warn) * (1f - 0.6f * Held01);
                }
                else if (Withered(p) && ex < 0.6f) { if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, EmberAt(Random.Range(0, 12)), 2); _glow.intensity = 1f - ex * 1.6f; }
                else _glow.intensity = 0f;
                _inkS.Write(_inkMesh); _scorchS.Write(_scorchMesh);
                return;
            }
            if (p == null) { _inkS.Write(_inkMesh); _scorchS.Write(_scorchMesh); return; }
            float a = SinceFire(p, t);
            Vector3 feet = FireFeet;
            if (_ink != null) Vol2FsB1.DrawBurst(_inkS, _ink, feet + Vector3.up * 0.5f, a, cam, 0.15f, 0.08f);
            // the flame front: out to the reach in FrontSeconds, tall tongues on the front (lower right behind the caster), a low fire left behind it
            if (a < FrontSeconds + 0.1f)
            {
                float r = Mathf.Lerp(0.4f, Reach * 1.05f, Expand(a, FrontSeconds));
                for (int k = 0; k < 10; k++)
                {
                    float ang = Random.Range(-Mathf.PI, Mathf.PI), front = Front(ang);
                    Vector3 d = Dir(ang);
                    Vol2FsB1.EmitV(_front, feet + d * r + Vector3.up * 0.15f, d * 1.5f + Vector3.up * 1.4f, Random.Range(0.5f, 1f) * Mathf.Lerp(0.35f, 1f, front), Random.Range(0.3f, 0.55f));
                }
            }
            if (a < 1.1f && Time.frameCount % 2 == 0)
                for (int k = 0; k < 3; k++) { float ang = Random.Range(-Mathf.PI, Mathf.PI); Vol2FsB1.EmitV(_licks, feet + Dir(ang) * Random.Range(0.4f, Reach) + Vector3.up * 0.05f, Vector3.up * 0.6f, Random.Range(0.15f, 0.3f) * (1f - a / 1.1f), 0.4f); }
            float sc = Vol2Fs.Smooth(0f, FrontSeconds, a) * (1f - Vol2Fs.Smooth(BurnSeconds, BurnSeconds + 1f, a));
            if (sc > 0.01f) Vol2FsB2.GroundRing(_scorchS, feet, F, Ground, 0.3f, Mathf.Lerp(0.4f, Reach, Expand(a, FrontSeconds)), 48, ang => sc * (0.4f + 0.6f * Front(ang)), 0.61f, cam, 0.025f, 0f, 3f);
            _blast.intensity = 8f * (1f - Vol2Fs.Smooth(0f, 0.5f, a)) + (a < BurnSeconds && _burning.Count > 0 ? 1.2f : 0f);
            // the struck bodies burn their 2 s, then the flames die from the tips into rising ash
            foreach (var s in _burning)
            {
                float ba = t - s.At; var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
                if (ba < BurnSeconds && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_licks, b.center + new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-0.2f, b.extents.y), Random.Range(-b.extents.z, b.extents.z)), Vector3.up * 0.8f, Random.Range(0.3f, 0.55f), Random.Range(0.35f, 0.6f));
                if (ba > BurnSeconds - 0.3f && ba < BurnSeconds + 1f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, b.center + Vector3.up * b.extents.y * Random.Range(0.2f, 1f), 3);
                if (ba < BurnSeconds && Time.frameCount % 9 == 0) Vol2FsB1.Emit(_soot, b.max - Vector3.up * 0.1f, 1);
            }
            _inkS.Write(_inkMesh); _scorchS.Write(_scorchMesh);
        }
    }
}
