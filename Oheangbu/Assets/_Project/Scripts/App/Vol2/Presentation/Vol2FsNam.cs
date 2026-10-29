using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>남 (fire, ㅏ path, 심는다) - free showcase batch 5, concept nam_b (a small round seal of fire pressed on the flank of every bear the path passed -
    /// a red seal stamp burning in the air, its rim burning down like incense; on the first bear the seal bursts into a blossom of flame and black-ink petals
    /// while the seals on the farther bears still burn down). The seal is the KCISA Pattern_25 fret seal (fs_seal25, read only) in fire. Executor facts only:
    /// "Plant" = the seal is stamped (a puff of embers) -> the wait (the seal breathes on its heartbeat, small tongues lick off it, the ink dial = the burning
    /// rim) -> Consumed = the blossom (petals of flame and ink, tongues all over the body, embers, soot) and the body burns its 2 s (the burn is the status
    /// sink's; mirrored), then burns out from the tips into ash rising (CSV 끝: 끝부터 타 재로 날림(재가 일어 위로)); Expired = the seal gutters into ash.</summary>
    public sealed class Vol2FsNam : Vol2FsSeedPath
    {
        static readonly Color Ember = new Color(1f, 0.45f, 0.12f);
        const float BurnSeconds = 2f;
        ParticleSystem _licks, _embers, _soot, _ash, _blast;
        Material _seal;
        Mesh _petalMesh, _sealMesh; readonly Vol2FsStrips _petalS = new Vol2FsStrips(), _sealS = new Vol2FsStrips();
        protected override float MarkLift => 0.25f;
        protected override float DialR => 0.48f;
        protected override float FireSeconds => BurnSeconds + 1.2f;
        protected override float ExpireSeconds => 1.2f;

        sealed class Look { public Light Glow; public List<Vol2FsB1.Stroke> Petals; public Vector3 At; }

        protected override void OnSetup()
        {
            var root = Root.transform;
            var seal = M("fs_seal25", new Color(1f, 0.28f, 0.06f, 1f), 2.0f, 0.8f); seal.NoiseAmt = 0.35f; seal.Erode = 0.25f; seal.Edge = new Color(1.6f, 0.9f, 0.3f, 1f); seal.EdgeWidth = 0.2f; seal.Soft = 0.02f;
            seal.Scroll = new Vector2(0f, -0.3f);
            _seal = Kit.Mat(seal);
            Vol2Fs.MeshObject(root, "Seals", _seal, out _sealMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.25f, 0.55f, 0.35f, 0.7f, 1.6f, 500), root, "Licks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 3.5f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.25f, Drag = 1f, VelOverLife = new Vector3(0f, 1.2f, 0f), Noise = 1.2f, NoiseFreq = 1.5f, Max = 700 }, root, "Embers");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.4f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.6f, Size1 = 1.4f, Radius = 0.3f, Drag = 1.5f, Tiles = 2, VelOverLife = new Vector3(0f, 0.8f, 0f), SizeEnd = 1.8f, Max = 120 }, root, "Soot");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.04f, Size1 = 0.1f, Radius = 0.35f, Tiles = 2, VelOverLife = new Vector3(0f, 0.9f, 0f), Noise = 0.8f, Spin = 200f, Max = 500 }, root, "Ash");
            _blast = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.6f, 1.15f)), 0.5f, 1.1f, 0.4f, 0.8f, 2.4f, 200), root, "BlastLicks");
            foreach (var ps in new[] { _licks, _embers, _soot, _ash, _blast }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Petals", Kit.InkSolid("fs_stroke2", InkC, new Color(1.8f, 0.55f, 0.12f, 1f), 0.16f, 0.5f, 0.3f, 1.2f), out _petalMesh);
        }

        Look L(Pod p) => p.Data as Look;
        protected override Vector3 DialAt(Pod p) => SealAt(p);
        /// <summary>The seal on the flank facing the camera (a little above the middle of the body).</summary>
        Vector3 SealAt(Pod p) { var b = HostNow(p); Vector3 toCam = FlatN(CamPos - b.center); return b.center + toCam * (Mathf.Min(b.extents.x, b.extents.z) * 0.9f) + Vector3.up * b.extents.y * 0.55f; }

        protected override void Planted(Pod p)
        {
            var l = new Look(); p.Data = l;
            l.Glow = Vol2Fs.PointLight(Root.transform, "SealGlow" + p.Index, Ember, 2.5f);
            Vector3 at = SealAt(p);
            Vol2FsB1.Emit(_embers, at, 40); Vol2FsB1.Emit(_licks, at, 4);
        }

        protected override void OnFire(Pod p)
        {
            var l = L(p); if (l == null) return;
            Vector3 c = SealAt(p); l.At = c;
            Vector3 side = Vector3.Cross(Vector3.up, (CamPos - c).normalized).normalized;
            l.Petals = Vol2FsB1.Burst(Seed + p.Index * 5, 18, side, -FlatN(CamPos - c), 1.6f, 3.2f, 0.35f, 0.7f, 0.75f, 0.3f);
            var b = HostNow(p);
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_blast, b.center + Random.insideUnitSphere * 0.8f, Random.onUnitSphere * 3f + Vector3.up * 1.8f, Random.Range(0.7f, 1.4f), Random.Range(0.4f, 0.8f));
            Vol2FsB1.Emit(_embers, c, 300); Vol2FsB1.Emit(_soot, c, 8);
            l.Glow.intensity = 6f;
        }

        protected override void OnPodEnd(Pod p)
        {
            if (Worked(p)) return;
            Vector3 at = SealAt(p);
            Vol2FsB1.Emit(_ash, at, Withered(p) ? 60 : 15); Vol2FsB1.Emit(_embers, at, 20);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _sealS.Clear(); _petalS.Clear();
            foreach (var p in Pods)
            {
                var l = L(p); if (l == null) continue;
                if (!Worked(p))
                {
                    float life = PodLife(p, t, 0.25f), beat = Beat(p), warn = PodWarn(p, t);
                    if (life <= 0.01f) { l.Glow.intensity = 0f; continue; }
                    Vector3 at = SealAt(p), view = (cam - at).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, view).normalized, up = Vector3.Cross(view, right);
                    float size = (0.62f + 0.05f * beat + 0.08f * warn) * Mathf.Lerp(1.4f, 1f, Vol2Fs.Smooth(0f, 0.2f, PodAge(p, t)));
                    // a quad seal facing the camera, its life fading its erosion (the shader eats it as it dies)
                    float a = life * (0.75f + 0.25f * beat) * (warn > 0f ? (Mathf.Sin(t * 38f) > 0f ? 1f : 0.55f) : 1f);
                    var pts = new[] { at - up * size * 0.5f, at + up * size * 0.5f };
                    _sealS.Strip(pts, new[] { size, size }, new[] { right, right }, Color.white, new[] { a, a }, 0.3f + p.Index * 0.2f, cam, 0f, 1f);
                    l.Glow.transform.position = at + view * 0.2f; l.Glow.intensity = life * (0.8f + 1.4f * beat + warn) * (1f - 0.6f * Held01);
                    if (!p.Ended && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_licks, at + right * Random.Range(-0.3f, 0.3f) + up * 0.25f, Vector3.up * 0.6f, Random.Range(0.12f, 0.22f), 0.35f);
                    if (!p.Ended && Time.frameCount % 4 == 0) Vol2FsB1.EmitV(_embers, at + Random.insideUnitSphere * 0.25f, Vector3.up * 0.8f, -1f, 0.9f);
                    continue;
                }
                float s = SinceFire(p, t);
                if (l.Petals != null) Vol2FsB1.DrawBurst(_petalS, l.Petals, l.At, s, cam, 0.25f, 0.09f);
                l.Glow.intensity = Mathf.Max(0f, 6f * (1f - Vol2Fs.Smooth(0f, 0.6f, s))) + (s < BurnSeconds ? 1.2f : 0f);
                var b = HostNow(p);
                // the body burns its 2 s, then the flames die from the tips into rising ash
                if (s < BurnSeconds && Time.frameCount % 2 == 0)
                    Vol2FsB1.EmitV(_licks, b.center + new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-0.2f, b.extents.y), Random.Range(-b.extents.z, b.extents.z)), Vector3.up * 0.8f, Random.Range(0.3f, 0.55f), Random.Range(0.35f, 0.6f));
                if (s > BurnSeconds - 0.3f && s < BurnSeconds + 1f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, b.center + Vector3.up * b.extents.y * Random.Range(0.2f, 1f), 3);
                if (s < BurnSeconds && Time.frameCount % 9 == 0) Vol2FsB1.Emit(_soot, b.max - Vector3.up * 0.1f, 1);
            }
            _sealS.Write(_sealMesh); _petalS.Write(_petalMesh);
        }
    }
}
