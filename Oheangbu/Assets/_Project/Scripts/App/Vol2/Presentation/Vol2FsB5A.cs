using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅓ 심는다 base of batch 5 (A3 검 넘 멈 섬 엄): the seed rides the caster's own brush / sleeve (the executor's ONE Attached Mark material,
    /// made at the start; nothing in the world). The base draws the two shared readings of the wait - a small ink countdown dial round the seed on the shaft
    /// and a burning-down ink arc on the ground at the executor's own reach (space.seed.attachmentRadius, 2 m; the arc stays centred on the front so it never
    /// walls the shoulder camera) - both on the live expiry (信 holds them), flaring in the last 0.4 s; the glyph draws its seed, its burst (OnFire: the
    /// player really took a hit) and its strikes (one OnStrike per enemy within the reach; none when nobody was that close).</summary>
    public abstract class Vol2FsSeedWear : Vol2FsSeed
    {
        protected Color Acc;
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        protected ParticleSystem PCharge;
        Mesh _countMesh, _dialMesh; readonly Vol2FsStrips _count = new Vol2FsStrips(), _dial = new Vol2FsStrips();
        /// <summary>Where on the shaft the seed sits (0 = the grip / sleeve, 1 = the brush tip).</summary>
        protected virtual float SeedU => 0.4f;
        protected virtual float DialR => 0.2f;
        /// <summary>The trigger's reach (the executor's number; 2 m).</summary>
        protected float Reach { get; private set; } = 2f;
        /// <summary>Where the burst was centred (the caster's feet at the hit).</summary>
        protected Vector3 FireFeet;
        protected Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);
        protected Vector3 SeedPoint() => Shaft(SeedU);
        protected Vector3 FeetNow() => G(CasterNow());
        /// <summary>The single seed of this cast (null before the start).</summary>
        protected Pod The => Pods.Count > 0 ? Pods[0] : null;

        protected abstract void OnSetup();
        protected abstract void Live(float t, float h);

        protected override void OnBegin()
        {
            Acc = Accent(Element);
            try { var co = Inst.Model.Coordinator; if (co != null) Reach = Mathf.Clamp((float)co.Numbers.Get("space.seed.attachmentRadius"), 0.5f, 6f); } catch (System.Exception) { Reach = 2f; }
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            var ink = M("fs_stroke2", InkC, 1f, 0f); ink.Edge = Acc * 2f; ink.EdgeWidth = 0.35f; ink.NoiseAmt = 0.25f; ink.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "Countdown", Kit.Mat(ink), out _countMesh);
            Vol2Fs.MeshObject(root, "Dial", Kit.Mat(ink), out _dialMesh);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, SeedPoint() + Random.insideUnitSphere * 0.3f * (1f - k), Vector3.zero, 0.05f + 0.1f * k, 0.2f);
        }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }
        protected override void OnFire(Pod p) { FireFeet = FeetNow(); Fired(p); }
        /// <summary>The seed worked (the player took a real hit): the burst round <see cref="FireFeet"/>.</summary>
        protected abstract void Fired(Pod p);

        /// <summary>0..1 of a ring burst's reach at s seconds after the trigger (out to the reach in <paramref name="dur"/> s, ease out).</summary>
        protected static float Expand(float s, float dur) { float k = Mathf.Clamp01(s / Mathf.Max(0.01f, dur)); return 1f - (1f - k) * (1f - k); }
        /// <summary>The angle (rad) of a direction round the caster against the cast's forward (0 = front).</summary>
        protected float AngleOf(Vector3 d) => Vector3.SignedAngle(F, FlatN(d), Vector3.up) * Mathf.Deg2Rad;

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _count.Clear(); _dial.Clear();
            var p = The;
            if (p != null && p.Started && !p.Ended)
            {
                float life = PodLife(p, t, 0.35f), warn = PodWarn(p, t), beat = Beat(p), rem = Remain01(p, t);
                float flick = warn > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f;
                float front = Mathf.Atan2(F.x, F.z);
                Vol2FsB5.Countdown(_count, FeetNow(), Ground, Reach, 0.07f * (1f + 0.6f * warn), rem, life * flick * (0.55f + 0.25f * beat), 0.31f, cam, front - rem * Mathf.PI, 64);
                if (DialR > 0f) Vol2FsB5.Dial(_dial, SeedPoint(), cam, DialR * (1f + 0.08f * beat), 0.025f * (1f + 0.6f * warn), rem, life * flick, 0.47f);
            }
            _count.Write(_countMesh); _dial.Write(_dialMesh);
            Live(t, h);
        }
    }
}
