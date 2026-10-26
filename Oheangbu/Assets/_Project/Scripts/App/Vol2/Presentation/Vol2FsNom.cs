using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>놈 (fire, ㅗ fan, 심는다) - free showcase batch 5, concept nom_b (small round seals of fire are stamped over the fan ground like burning seal marks,
    /// their rims burning down; where the bear stepped in they detonate one after another into geysers of fire and ink racing across the fan). Not 노's breath fan,
    /// 녹's burning reeds or 논's flame tide: fire MINES that wait. The seal is the KCISA Pattern_8 round seal (fs_seal8, read only) burning on the ground.
    /// Executor facts only: the start = the seals are stamped down from the near third outward (a puff of embers each) -> the wait (each seal breathes on the
    /// heartbeat with a tongue or two licking off it and embers rising; the far edge's ink arc burns down; the last 0.4 s they sputter) -> Consumed = the
    /// detonations run as a wave from the entering bear: each seal shoots a geyser of tall flame tongues, black ink spikes and embers, leaves a scorch; every
    /// struck body burns its 2 s (the status sink's burn, mirrored) and then the flames die from the tips into ash rising (CSV 끝: 끝부터 타 재로 날림(재가 일어
    /// 위로)); Expired = the seals gutter out from the far edge into rising ash.</summary>
    public sealed class Vol2FsNom : Vol2FsSeedFan
    {
        static readonly Color Ember = new Color(1f, 0.45f, 0.12f);
        const float BurnSeconds = 2f;
        ParticleSystem _licks, _geyser, _embers, _soot, _ash;
        Mesh _sealMesh, _spikeMesh, _scorchMesh; readonly Vol2FsStrips _sealS = new Vol2FsStrips(), _spikeS = new Vol2FsStrips(), _scorchS = new Vol2FsStrips();
        readonly List<(Struck S, float At)> _burning = new List<(Struck, float)>();
        Light[] _lights;
        protected override int SpotCount => 16;
        protected override float WaveSpeed => 8f;
        protected override float FireSeconds => BurnSeconds + 1.2f;
        protected override float ExpireSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var seal = M("fs_seal8", new Color(1f, 0.38f, 0.1f, 1f), 2.2f, 0.85f); seal.NoiseAmt = 0.35f; seal.Erode = 0.25f; seal.Edge = new Color(1.6f, 0.9f, 0.3f, 1f); seal.EdgeWidth = 0.2f; seal.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Seals", Kit.Mat(seal), out _sealMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.2f, 0.45f, 0.35f, 0.7f, 1.4f, 600), root, "Licks");
            _geyser = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.6f, 1.15f)), 0.45f, 0.9f, 0.45f, 0.85f, 3.5f, 500, 2.8f), root, "Geysers");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 3.5f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 1f, Speed1 = 6f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.2f, Drag = 1f, VelOverLife = new Vector3(0f, 1.2f, 0f), Noise = 1.2f, NoiseFreq = 1.5f, Max = 900 }, root, "Embers");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.4f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.6f, Size1 = 1.3f, Radius = 0.25f, Drag = 1.5f, Tiles = 2, VelOverLife = new Vector3(0f, 1f, 0f), SizeEnd = 1.8f, Max = 160 }, root, "Soot");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.04f, Size1 = 0.1f, Radius = 0.3f, Tiles = 2, VelOverLife = new Vector3(0f, 0.9f, 0f), Noise = 0.8f, Spin = 200f, Max = 600 }, root, "Ash");
            foreach (var ps in new[] { _licks, _geyser, _embers, _soot, _ash }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkSpikes", Kit.InkSolid("fs_stroke2", InkC, new Color(1.8f, 0.55f, 0.12f, 1f), 0.14f, 0.5f, 0.3f, 1.2f), out _spikeMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Opacity = 0.8f; sc.NoiseAmt = 0.4f; sc.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "FireLight" + k, Ember, 5f);
        }

        protected override void Planted(Pod p)
        {
            foreach (var s in Spots) Vol2FsB1.Emit(_embers, s.P + Vector3.up * 0.05f, 6);
            for (int k = 0; k < 3; k++) _lights[k].transform.position = InFan(0.5f + 0.2f * k, (k - 1) * 0.6f) + Vector3.up * 0.5f;
        }

        protected override void SpotBurst(Spot s)
        {
            Vector3 c = s.P;
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_geyser, c + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(3f, 6f) + Random.insideUnitSphere * 0.6f, Random.Range(0.5f, 0.9f), Random.Range(0.45f, 0.8f));
            Vol2FsB1.Emit(_embers, c + Vector3.up * 0.3f, 40);
            if (s.K % 3 == 0) Vol2FsB1.Emit(_soot, c + Vector3.up * 0.8f, 2);
        }

        protected override void OnStruckBody(Pod p, Struck s) { float at = WaveAt(FeetOf(s)); _burning.Add((s, at < 0f ? Now(NowUs) : at)); }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _sealS.Clear(); _spikeS.Clear(); _scorchS.Clear();
            bool waiting = p != null && p.Started && !p.Ended;
            float beat = p != null ? Beat(p) : 0f, warn = p != null ? PodWarn(p, t) : 0f;
            float lightK = 0f;
            foreach (var s in Spots)
            {
                if (p == null || !p.Started) break;
                if (!s.Burst)
                {
                    float w = SpotWither(s, t);
                    float grow = Vol2Fs.Smooth(s.U * 0.3f, s.U * 0.3f + 0.2f, PodAge(p, t));
                    float a = waiting ? grow * (0.7f + 0.3f * beat) * (warn > 0f ? (Mathf.Sin(t * 37f + s.K) > 0f ? 1f : 0.5f) : 1f) : Withered(p) ? 1f - w : 0f;
                    if (a > 0.01f)
                    {
                        float sz = (0.55f + 0.15f * s.Rnd) * (1f + 0.08f * beat);
                        Vector3 r = Quaternion.AngleAxis(s.Rnd * 360f, Vector3.up) * Vector3.right, f = Vector3.Cross(r, Vector3.up);
                        _sealS.Strip(new[] { s.P + Vector3.up * 0.03f - f * sz * 0.5f, s.P + Vector3.up * 0.03f + f * sz * 0.5f }, new[] { sz, sz }, new[] { r, r }, Color.white, new[] { a, a }, s.Rnd, cam, 0f, 1f);
                        if (waiting && Time.frameCount % 7 == s.K % 7) Vol2FsB1.EmitV(_licks, s.P + Vector3.up * 0.1f, Vector3.up * 0.5f, Random.Range(0.12f, 0.22f), 0.4f);
                        if (waiting && Time.frameCount % 9 == s.K % 9) Vol2FsB1.EmitV(_embers, s.P + Vector3.up * 0.1f, Vector3.up * 0.7f, -1f, 0.9f);
                        if (Withered(p) && w > 0.05f && w < 0.6f && Time.frameCount % 4 == s.K % 4) Vol2FsB1.Emit(_ash, s.P + Vector3.up * 0.1f, 2);
                        lightK = Mathf.Max(lightK, a);
                    }
                    continue;
                }
                // detonated: an ink spike pair and a scorch
                float b = t - s.BurstAt;
                float spike = Vol2Fs.Smooth(0f, 0.08f, b) * (1f - Vol2Fs.Smooth(0.3f, 0.8f, b));
                if (spike > 0.01f)
                {
                    float hgt = (1.6f + 1.4f * s.Rnd) * Vol2Fs.EaseOutBack(Mathf.Clamp01(b / 0.12f));
                    Vector3 lean = (FlatN(s.P - Apex) * 0.25f + Vector3.right * (s.Rnd - 0.5f) * 0.3f);
                    _spikeS.Strip(new[] { s.P, s.P + (Vector3.up + lean) * hgt * 0.5f, s.P + (Vector3.up + lean * 1.4f) * hgt }, new[] { 0.32f, 0.22f, 0.02f }, null, Color.white, new[] { spike, spike, spike * 0.8f }, s.Rnd, cam);
                }
                float sc = Vol2Fs.Smooth(0f, 0.1f, b) * (1f - Vol2Fs.Smooth(2.2f, 3f, b));
                if (sc > 0.01f) _scorchS.Quad(s.P + Vector3.up * 0.02f, Vector3.right, Vector3.forward, 0.9f + 0.4f * s.Rnd, new Color(1f, 1f, 1f, sc), s.Rnd);
                lightK = Mathf.Max(lightK, 3f * (1f - Vol2Fs.Smooth(0f, 0.7f, b)));
            }
            // the struck bodies burn their 2 s, then the flames die into rising ash
            foreach (var (s, at) in _burning)
            {
                float a = t - at; if (a < 0f) continue;
                var bb = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
                if (a < BurnSeconds && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_licks, bb.center + new Vector3(Random.Range(-bb.extents.x, bb.extents.x), Random.Range(-0.2f, bb.extents.y), Random.Range(-bb.extents.z, bb.extents.z)), Vector3.up * 0.8f, Random.Range(0.3f, 0.55f), Random.Range(0.35f, 0.6f));
                if (a > BurnSeconds - 0.3f && a < BurnSeconds + 1f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, bb.center + Vector3.up * bb.extents.y * Random.Range(0.2f, 1f), 3);
                if (a < BurnSeconds) lightK = Mathf.Max(lightK, 1.2f);
            }
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = lightK * (waiting ? (0.4f + 0.8f * beat) * (1f - 0.6f * Held01) : 1f) * (0.8f + 0.2f * Mathf.Sin(t * 9f + k));
            _sealS.Write(_sealMesh); _spikeS.Write(_spikeMesh); _scorchS.Write(_scorchMesh);
        }
    }
}
