using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>옴 (water, ㅗ fan, 심는다) - free showcase batch 5, concept om_a (beads of water with ink curling inside lie over the fan like dew, a small ripple
    /// ring under each; where the bear stepped in they burst upward into a chain of splashing jets racing across the fan, both bears drenched). Not 오's curling
    /// wave, 옥's twisting water pillars or 온's spilling flood: a dew field that WAITS. Executor facts only: the start = the beads well up from the ground from the
    /// near third outward -> the wait (each bead wobbles on the heartbeat, its ripple ring breathes; the far edge's ink arc burns down; the last 0.4 s they quiver)
    /// -> Consumed = the jets run as a wave from the entering bear: every bead shoots a 1.4 - 2.6 m jet of water threaded with ink that falls back as spray,
    /// foam rings at its foot; every struck body takes a splash crown and stays drenched for the wet's 4 s (the status sink's, mirrored with drips) while the fan
    /// ground darkens; the water runs down and soaks away (CSV 끝: 아래로 흘러내림); Expired = the beads sink and run off from the far edge, a brief wet sheen.</summary>
    public sealed class Vol2FsOm : Vol2FsSeedFan
    {
        const float WetSeconds = 4f;
        ParticleSystem _spray, _drips, _foam, _ink;
        Material _water, _jetMat;
        Mesh _jetMesh, _ringMesh, _wetMesh; readonly Vol2FsStrips _jetS = new Vol2FsStrips(), _ringS = new Vol2FsStrips(), _wetS = new Vol2FsStrips();
        readonly List<(Struck S, float At)> _wet = new List<(Struck, float)>();
        protected override int SpotCount => 14;
        protected override float WaveSpeed => 9f;
        protected override float FireSeconds => WetSeconds;
        protected override float ExpireSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _water = Vol2FsB1.Water(Kit, 1.5f, 0.35f);
            _jetMat = Vol2FsB1.Water(Kit, 1.6f, 0.4f, "fs_streak");
            var spray = M("fs_splash", new Color(0.5f, 0.78f, 0.95f, 1f), 1.15f, 0.45f);      // r3: blue-teal spray (r2: white popcorn) spray.NoiseAmt = 0.3f; spray.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.4f, Radius = 0.2f, Gravity = 1.6f, Drag = 0.6f, Tiles = 2, Spin = 120f, Max = 1200 }, root, "Spray");
            var drip = M("fs_dot", new Color(0.7f, 0.9f, 1f, 1f), 1.6f, 0.6f);
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drip), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.1f, Gravity = 1.6f, Stretch = true, StretchVel = 0.06f, Max = 500 }, root, "Drips");
            var foam = M("fs_puff", new Color(0.62f, 0.82f, 0.92f, 1f), 1.0f, 0.3f); foam.Opacity = 0.5f; foam.NoiseAmt = 0.5f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 1f, Speed1 = 2.5f, Size0 = 0.25f, Size1 = 0.55f, Shape = ParticleSystemShapeType.Circle, Radius = 0.3f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 3f, Tiles = 2, SizeEnd = 1.6f, Max = 300, SortFudge = 6 }, root, "Foam");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.NoiseAmt = 0.3f; ink.Soft = 0.15f;
            _ink = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.04f, Size1 = 0.12f, Tiles = 2, Gravity = 0.8f, Max = 400, SortFudge = -4 }, root, "Ink");
            foreach (var ps in new[] { _spray, _drips, _foam, _ink }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Jets", _jetMat, out _jetMesh);
            var ring = M("fs_ring", new Color(0.75f, 0.9f, 1f, 1f), 1.2f, 0.6f); ring.NoiseAmt = 0.25f; ring.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Ripples", Kit.Mat(ring), out _ringMesh);
            var wet = M("fs_wet", new Color(0.15f, 0.2f, 0.24f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.4f; wet.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
        }

        sealed class Bead { public Mesh M; public MeshRenderer R; }

        protected override void Planted(Pod p)
        {
            foreach (var s in Spots)
            {
                var b = new Bead(); b.R = Vol2Fs.MeshObject(Root.transform, "Bead" + s.K, _water, out b.M); s.Data = b;
                Vol2FsB1.Emit(_spray, s.P, 3);
            }
        }

        protected override void SpotBurst(Spot s)
        {
            if (s.Data is Bead b) b.M.Clear();
            Vol2FsB1.Emit(_foam, s.P + Vector3.up * 0.03f, 6);
            for (int k = 0; k < 26; k++) Vol2FsB1.EmitV(_spray, s.P + Vector3.up * 0.1f, Vector3.up * Random.Range(3f, 6.5f) + Random.insideUnitSphere * 1.2f, Random.Range(0.15f, 0.4f), -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_ink, s.P + Vector3.up * Random.Range(0.2f, 1.6f), Random.insideUnitSphere * 1.5f + Vector3.up, -1f, -1f);
        }

        protected override void OnStruckBody(Pod p, Struck s)
        {
            float at = WaveAt(FeetOf(s)); _wet.Add((s, at < 0f ? Now(NowUs) : at));
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _jetS.Clear(); _ringS.Clear(); _wetS.Clear();
            if (p == null || !p.Started) { _jetS.Write(_jetMesh); _ringS.Write(_ringMesh); _wetS.Write(_wetMesh); return; }
            bool waiting = !p.Ended;
            float beat = Beat(p), warn = PodWarn(p, t);
            foreach (var s in Spots)
            {
                var b = s.Data as Bead;
                if (!s.Burst)
                {
                    float w = SpotWither(s, t);
                    float grow = Vol2Fs.Smooth(s.U * 0.3f, s.U * 0.3f + 0.3f, PodAge(p, t));
                    float life = waiting ? grow : Withered(p) ? 1f - w : 0f;
                    if (b != null)
                    {
                        if (life > 0.01f)
                        {
                            float r = (0.16f + 0.06f * s.Rnd) * life * (1f + 0.1f * beat + 0.08f * warn);
                            float wob = 0.08f + 0.15f * warn, ph = t * (5f + 18f * warn) * (1f - Held01) + s.Rnd * 9f;
                            Vector3 c = s.P + Vector3.up * r * 0.85f * (1f - w);
                            Vol2FsB1.WaterSphere(b.M, c, r, -0.35f, cam, 0.9f * life, d => d * (1f + wob * Mathf.Sin(ph + d.x * 4f + d.z * 3f)) + Vector3.down * 0.6f * w * Mathf.Max(0f, d.y), 12, 8, 1.5f);
                            if (waiting && Time.frameCount % 11 == s.K % 11) Vol2FsB1.EmitV(_ink, c, Vector3.zero, 0.05f, 0.3f);
                        }
                        else b.M.Clear();
                    }
                    // the ripple ring under the bead, breathing on the beat
                    if (life > 0.01f)
                    {
                        float rr = 0.3f + 0.12f * beat + 0.1f * s.Rnd;
                        Vol2FsB2.GroundRing(_ringS, s.P, F, Ground, rr - 0.04f, rr + 0.04f, 16, a => life * (0.45f + 0.4f * beat), s.Rnd, cam, 0.02f, 0f, 2f);
                    }
                    if (Withered(p) && w > 0.2f && w < 0.9f) _wetS.Quad(s.P + Vector3.up * 0.015f, Vector3.right, Vector3.forward, 0.8f, new Color(1f, 1f, 1f, 0.6f * Mathf.Sin(w * Mathf.PI)), s.Rnd);
                    continue;
                }
                // the jet: up to 1.4 - 2.6 m in 0.18 s, falls back by 0.8 s
                float a = t - s.BurstAt, hgt = (1.4f + 1.2f * s.Rnd) * Vol2Fs.Smooth(0f, 0.18f, a) * (1f - Vol2Fs.Smooth(0.35f, 0.8f, a));
                if (hgt > 0.05f)
                {
                    Vector3 lean = FlatN(s.P - Apex) * 0.15f;
                    float al = 0.9f * (1f - Vol2Fs.Smooth(0.6f, 0.85f, a));
                    _jetS.Strip(new[] { s.P, s.P + (Vector3.up + lean) * hgt * 0.5f, s.P + (Vector3.up + lean * 1.5f) * hgt }, new[] { 0.42f, 0.3f, 0.18f }, null, Color.white, new[] { al, al, al * 0.7f }, s.Rnd, cam, 0f, 1.5f);
                    if (Time.frameCount % 3 == s.K % 3) Vol2FsB1.EmitV(_spray, s.P + Vector3.up * hgt, Random.insideUnitSphere * 1.5f, Random.Range(0.12f, 0.3f), -1f);
                }
            }
            // the drenched bodies and the dark wet fan
            float wetFan = 0f;
            foreach (var (s, at) in _wet)
            {
                float a = t - at; if (a < 0f) continue;
                if (a < 0.05f) { Vector3 c = CentreNow(s); for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_spray, c, (Random.insideUnitSphere + Vector3.up * 0.8f).normalized * Random.Range(2f, 6f), Random.Range(0.15f, 0.4f), -1f); }
                if (a < WetSeconds && Time.frameCount % 2 == 0) { var bb = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B; Vol2FsB1.EmitV(_drips, bb.center + new Vector3(Random.Range(-bb.extents.x, bb.extents.x), Random.Range(-0.3f, bb.extents.y), Random.Range(-bb.extents.z, bb.extents.z)), Vector3.down * 0.3f, -1f, -1f); }
                wetFan = Mathf.Max(wetFan, Vol2Fs.Smooth(0f, 0.4f, a) * (1f - Vol2Fs.Smooth(WetSeconds - 0.6f, WetSeconds, a)));
            }
            if (p.FiredAt >= 0f && wetFan <= 0f) { float a = t - p.FiredAt; wetFan = Vol2Fs.Smooth(0f, 0.5f, a) * (1f - Vol2Fs.Smooth(WetSeconds - 0.6f, WetSeconds, a)); }
            if (wetFan > 0.01f) foreach (var s in Spots) if (s.Burst) _wetS.Quad(s.P + Vector3.up * 0.015f, Vector3.right, Vector3.forward, 1.5f, new Color(1f, 1f, 1f, wetFan * Vol2Fs.Smooth(s.BurstAt, s.BurstAt + 0.3f, t)), s.Rnd);
            _jetS.Write(_jetMesh); _ringS.Write(_ringMesh); _wetS.Write(_wetMesh);
        }
    }
}
