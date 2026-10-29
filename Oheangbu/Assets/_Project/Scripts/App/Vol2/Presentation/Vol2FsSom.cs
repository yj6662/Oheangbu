using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>솜 (metal, ㅗ fan, 심는다) - free showcase batch 5, concept som_a (tiny steel caltrops and needle seeds glint all over the fan; where the bear stepped
    /// in, a field of thin curved blades springs up out of the ground in a wave across the fan, slicing both bears, sparks spraying). Not 소's flying knives,
    /// 속's stakes with taut lines or 손's metal dust: a hidden blade field that WAITS. Executor facts only: the start = caltrops scatter onto the fan with a
    /// glint each -> the wait (a glint runs over them on each heartbeat; the far edge's ink arc burns down; the last 0.4 s they rattle) -> Consumed = blades
    /// spring up as a wave from the entering bear (1.2 - 2.4 m curved steel, leaning out, a spark spurt at each root), every struck body gets a white-gold slash
    /// and a spark fountain with a hit-stop, and 0.55 s after it rose each blade snaps and its shards fly sideways (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐);
    /// Expired = the caltrops snap where they lie from the far edge, tiny shards sideways.</summary>
    public sealed class Vol2FsSom : Vol2FsSeedFan
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        const float StandSeconds = 0.55f;
        ParticleSystem _caltrops, _sparks, _shards, _glints;
        Material _steel;
        Mesh _slashMesh; readonly Vol2FsStrips _slashS = new Vol2FsStrips();
        readonly List<(Transform T, Vector3 Root, Quaternion Rot, float H, float At, bool Snapped)> _blades = new List<(Transform, Vector3, Quaternion, float, float, bool)>();
        readonly List<(Vector3 C, float At)> _slashes = new List<(Vector3, float)>();
        Light _glint;
        protected override int SpotCount => 26;
        protected override float WaveSpeed => 11f;
        protected override float FireSeconds => StandSeconds + 1f;
        protected override float ExpireSeconds => 0.9f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.05f, 0.06f, 0.08f));
            var shard = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5));
            _caltrops = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = shard, Life0 = 99f, Life1 = 99f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.26f, Size1 = 0.4f, Gravity = 0f, Max = 120 }, root, "Caltrops");      // r3: bigger (r2: invisible from the shoulder)
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.08f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1200 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = shard, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Radius = 0.08f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 500 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.white, 4f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.2f, Size1 = 0.35f, Max = 120 }, root, "Glints");
            foreach (var ps in new[] { _caltrops, _sparks, _shards, _glints }) ps.Play(false);
            var x = M("fs_streak", Gold, 4f, 1f); x.NoiseAmt = 0.15f; x.Soft = 0.02f; x.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Slashes", Kit.Mat(x), out _slashMesh);
            _glint = Vol2Fs.PointLight(root, "Glint", Gold, 5f);
        }

        protected override void Planted(Pod p)
        {
            // the caltrops: mesh particles that never move (their life is longer than the seed's; cleared at the burst / the end)
            foreach (var s in Spots)
            {
                var e = new ParticleSystem.EmitParams { position = s.P + Vector3.up * 0.04f, velocity = Vector3.zero, startLifetime = 60f, rotation3D = new Vector3(s.Rnd * 40f, s.Rnd * 360f, 20f), applyShapeToPosition = false };
                _caltrops.Emit(e, 1);
                Vol2FsB1.EmitV(_glints, s.P + Vector3.up * 0.08f, Vector3.zero, 0.3f, 0.12f);
            }
            _glint.transform.position = InFan(0.65f, 0f) + Vector3.up * 0.5f;
        }

        void ClearCaltrops() => _caltrops.Clear(false);

        protected override void Fired(Pod p) { ClearCaltrops(); }

        protected override void SpotBurst(Spot s)
        {
            var mesh = Kit.SharedMesh("somBlade", () => Vol2FsB1.Blade());
            for (int k = 0; k < 2; k++)
            {
                var r = Vol2FsB1.Prop(Root.transform, "Blade" + s.K + "_" + k, mesh, _steel, false);
                Vector3 outw = FlatN(s.P - Apex), side = Vector3.Cross(Vector3.up, outw);
                // a blade: up, leaning out and sideways, its flat facing the camera's side
                Vector3 up = (Vector3.up * 1.6f + outw * (0.4f + 0.4f * s.Rnd) + side * (k == 0 ? 0.35f : -0.35f)).normalized;
                var rot = Quaternion.LookRotation(up, side);
                float h = 1.2f + 1.2f * Vol2Fs.Hash(Seed, s.K * 3 + k);
                r.transform.SetPositionAndRotation(s.P, rot); r.transform.localScale = new Vector3(2.6f, 1f, 0.01f);
                _blades.Add((r.transform, s.P, rot, h, s.BurstAt + k * 0.03f, false));
            }
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_sparks, s.P + Vector3.up * 0.05f, (Vector3.up * 2f + Random.insideUnitSphere).normalized * Random.Range(3f, 8f), -1f, -1f);
        }

        protected override void OnStruckBody(Pod p, Struck s)
        {
            float at = WaveAt(FeetOf(s)); _slashes.Add((CentreNow(s), (at < 0f ? Now(NowUs) : at) + 0.05f));
        }

        void Snap(Vector3 at, Vector3 side, int n, int sparks)
        {
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_shards, at + Random.insideUnitSphere * 0.15f, side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
            for (int k = 0; k < sparks; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.6f).normalized * Random.Range(4f, 10f), -1f, -1f);
        }

        protected override void OnPodEnd(Pod p) { if (!Worked(p) && !Withered(p)) ClearCaltrops(); }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The; _slashS.Clear();
            bool waiting = p != null && p.Started && !p.Ended;
            float beat = p != null ? Beat(p) : 0f, warn = p != null ? PodWarn(p, t) : 0f;
            if (waiting)
            {
                // a glint runs over the field on each beat; a rattle at the warning
                float ph = p.Phase - Mathf.Floor(p.Phase);
                if (ph < 0.3f && Time.frameCount % 2 == 0 && Spots.Count > 0) { var s = Spots[(int)(ph / 0.3f * Spots.Count) % Spots.Count]; Vol2FsB1.EmitV(_glints, s.P + Vector3.up * 0.08f, Vector3.zero, 0.35f, 0.1f); }
                if (warn > 0f && Time.frameCount % 2 == 0 && Spots.Count > 0) { var s = Spots[Random.Range(0, Spots.Count)]; Vol2FsB1.EmitV(_sparks, s.P + Vector3.up * 0.05f, Vector3.up * 1.5f + Random.insideUnitSphere, -1f, 0.2f); }
                _glint.intensity = PodLife(p, t) * (0.3f + 1.2f * beat + warn) * (1f - 0.6f * Held01);
            }
            else _glint.intensity = 0f;
            if (p != null && Withered(p))
            {
                // the caltrops snap from the far edge
                foreach (var s in Spots) if (!s.Burst && SpotWither(s, t) > 0.3f) { s.Burst = true; Snap(s.P + Vector3.up * 0.05f, Vector3.Cross(Vector3.up, FlatN(s.P - Apex)), 2, 6); }
                if (PodExit(p, t) > 0.6f) ClearCaltrops();
            }
            float flash = 0f;
            for (int i = 0; i < _blades.Count; i++)
            {
                var b = _blades[i]; float a = t - b.At;
                if (a < 0f) { b.T.gameObject.SetActive(false); continue; }
                float rise = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.1f));
                b.T.gameObject.SetActive(!b.Snapped);
                b.T.localScale = new Vector3(2.6f, 1f, Mathf.Max(0.01f, b.H * rise));
                if (!b.Snapped && a >= StandSeconds)
                {
                    Vector3 tip = b.Root + b.Rot * Vector3.forward * b.H * 0.6f;
                    Snap(tip, Vector3.Cross(Vector3.up, FlatN(b.Root - Apex)), 4, 8);
                    _blades[i] = (b.T, b.Root, b.Rot, b.H, b.At, true); b.T.gameObject.SetActive(false);
                }
                if (a < 0.15f) flash = Mathf.Max(flash, 1f);
            }
            foreach (var (c, at) in _slashes)
            {
                float a = t - at; if (a < 0f || a > 0.6f) continue;
                if (a < Time.deltaTime * 1.01f) { HitStop(t, 0.07f); for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_sparks, c, (Random.insideUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(5f, 13f), -1f, -1f); }
                float life = 1f - Vol2Fs.Smooth(0.1f, 0.6f, a), open = Vol2Fs.Smooth(0f, 0.05f, a);
                Vector3 view = (cam - c).normalized, right = Vector3.Cross(Vector3.up, view).normalized, up = Vector3.Cross(view, right);
                Vector3 dir = (right + up * 0.5f).normalized;
                _slashS.Strip(new[] { c - dir * 1.2f * open, c, c + dir * 1.2f * open }, new[] { 0.02f, 0.14f, 0.02f }, null, Color.white, new[] { life, life, life }, 0.3f, cam);
                flash = Mathf.Max(flash, 2f * life);
            }
            if (!waiting && flash > 0f) { _glint.intensity = 3f * flash; }
            _slashS.Write(_slashMesh);
        }
    }
}
