using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>난 (fire, ㅏ path, 번진다 극치) - free showcase batch 4, concept nan_a (fire birds: the fire bolt bursts on the first bear and from the blast great
    /// birds of flame and burning ink burst out and dive each onto one neighbour, every one as big and bright as the first blast; each neighbour erupts in an
    /// equal column of fire; scorched marks glow at their feet). The peak's rule is drawn as SAMENESS: every bird and every eruption is the same size, because the
    /// executor pays every spread body the same fixed 0.5 (no thinning, three at most). Beats, all executor facts: charge -> an ink-ember bolt to the predicted
    /// first body -> the blast -> every "Spread" contact = one bird rising from the blast and diving onto that neighbour, in the executor's order -> its Mark (3 s) =
    /// a burning scorch at the feet + the body burning; warning = the flames gutter -> natural expiry (CSV: 끝부터 타 재로 날림) = the fire burns down into rising ash.</summary>
    public sealed class Vol2FsNan : Vol2FsSpread
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        static readonly Color FireRim = new Color(2.6f, 0.9f, 0.15f, 1f);
        ParticleSystem _charge, _trail, _fire, _tall, _embers, _smoke, _ash, _drops; Light _chargeLight, _boltLight, _blastLight;
        Mesh _wingMesh, _tailMesh, _burstMesh, _scorchMesh;
        readonly Vol2FsStrips _wings = new Vol2FsStrips(), _tails = new Vol2FsStrips(), _burstS = new Vol2FsStrips(), _scorch = new Vol2FsStrips();
        readonly List<(List<Vol2FsB1.Stroke> S, Vector3 At, float T)> _bursts = new List<(List<Vol2FsB1.Stroke>, Vector3, float)>();
        Vector3 _blast;

        sealed class Bird { public Light L; public Vector3 P0, P1, P2, Pos, Dir; public float Phase; }

        protected override float LobHeight => 0.35f;
        protected override float HopSpeed => 7f;
        protected override float HopMin => 0.6f;
        protected override float HopMax => 0.85f;
        protected override float HopStagger => 0.13f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.5f)), Rate = 70f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.12f, Size1 = 0.28f, Radius = 0.6f, World = false, Tiles = 2, Max = 90 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _trail = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(2f, 0.5f)), Rate = 0f, RatePerMeter = 26f, Loop = true, Life0 = 0.2f, Life1 = 0.4f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.25f, Size1 = 0.5f, Radius = 0.1f, Tiles = 2,
                VelOverLife = new Vector3(0f, 1f, 0f), SizeEnd = 0.3f, Max = 200 }, root, "Trail");
            _boltLight = Vol2Fs.PointLight(_trail.transform, "BoltLight", Orange, 5f);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.6f, 0.35f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.3f, Size1 = 0.65f, Radius = 0.12f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.35f, Max = 5000 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.35f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 1.5f, Speed1 = 3.5f, Size0 = 0.9f, Size1 = 1.5f, Radius = 0.3f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3.4f, 0f), Noise = 1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 600 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 3f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.25f, Gravity = -0.25f,
                Noise = 1.4f, NoiseFreq = 1.6f, Stretch = true, StretchVel = 0.015f, Max = 1500 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.5f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.9f, Size1 = 1.5f, Radius = 0.3f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.1f, 0f), Max = 300, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.04f, Size1 = 0.12f, Radius = 0.35f, VelOverLife = new Vector3(0f, 1.6f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 1500 }, root, "Ash");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(1.8f, 0.6f, 0.1f, 1f); drop.EdgeWidth = 0.18f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.26f, Radius = 0.25f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 500 }, root, "InkDrops");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash, _drops }) ps.Play(false);
            // the birds' wings: solid ink feathers with a burning rim; their tails: flame ribbons
            Vol2Fs.MeshObject(root, "Wings", Kit.InkSolid("fs_stroke2", Ink, FireRim, 0.2f, 0.45f, 0.3f, 1.4f), out _wingMesh);
            var tail = M("fs_streak", Color.white, 3f, 0.85f); tail.Ramp = "fs_ramp_fire"; tail.RampScale = 1.3f; tail.NoiseAmt = 0.5f;
            Vol2Fs.MeshObject(root, "Tails", Kit.Mat(tail), out _tailMesh);
            Vol2Fs.MeshObject(root, "InkBursts", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.8f, 0.12f, 1f), 0.12f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.4f; sc.Erode = 0.3f; sc.Opacity = 0.9f; sc.Edge = new Color(2.4f, 0.7f, 0.1f, 1f); sc.EdgeWidth = 0.16f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            _blastLight = Vol2Fs.PointLight(root, "BlastLight", Orange, 9f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 4f * k; }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            _trail.transform.position = From; _trail.Play(false); _boltLight.intensity = 5f;
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f); _trail.transform.position = p;
            Vol2FsB1.Emit(_fire, p, 3); Vol2FsB1.Emit(_embers, p, 2); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drops, p, 1);
        }

        protected override void OnHit(long nowUs)
        {
            _trail.Stop(true, ParticleSystemStopBehavior.StopEmitting); _boltLight.intensity = 0f;
            _blast = HasTarget ? HitPoint : AimPoint;
            Erupt(_blast, G(FeetNow()), 1f);
        }

        /// <summary>One blast - the first one and every bird's landing are the SAME (the peak: 0.5 each, no thinning).</summary>
        void Erupt(Vector3 at, Vector3 feet, float k)
        {
            Vector3 fwd = FlatN(at - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
            _bursts.Add((Vol2FsB1.Burst(Seed + _bursts.Count * 17, 9, side, fwd, 1.0f, 2.1f, 0.18f, 0.36f, 0.85f, 0.25f), at, -1f));
            Vol2FsB1.Emit(_fire, at, Mathf.RoundToInt(70 * k)); Vol2FsB1.Emit(_tall, feet + Vector3.up * 0.3f, Mathf.RoundToInt(12 * k)); Vol2FsB1.Emit(_embers, at, Mathf.RoundToInt(110 * k));
            Vol2FsB1.Emit(_drops, at, Mathf.RoundToInt(50 * k)); Vol2FsB1.Emit(_smoke, at + Vector3.up * 0.6f, 5);
        }

        protected override void OnHop(Hop h)
        {
            var b = new Bird { L = Vol2Fs.PointLight(Root.transform, "BirdLight", Orange, 5f), Phase = Vol2Fs.Hash(Seed, (int)h.TargetId) * 6f };
            // rise out of the blast, arc high, dive onto the neighbour (a quadratic curve)
            b.P0 = (_blast.sqrMagnitude > 0f ? _blast : h.FromP + Vector3.up * 1f) + Vector3.up * 0.4f;
            b.P2 = CentreNow(h);
            Vector3 mid = Vector3.Lerp(b.P0, b.P2, 0.45f); Vector3 away = FlatN(b.P2 - b.P0);
            b.P1 = mid + Vector3.up * (2.6f + 0.4f * h.Order) - away * 0.3f;
            b.Pos = b.P0; b.Dir = (b.P1 - b.P0).normalized;
            h.Data = b;
        }

        protected override void OnLand(Hop h)
        {
            if (h.Data is Bird b) b.L.intensity = 0f;
            Erupt(CentreNow(h), G(FeetOf(h)), 1f);      // the same blast as the first (the peak's sameness)
        }

        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float u) { float v = 1f - u; return v * v * a + 2f * v * u * b + u * u * c; }

        void DrawBird(Bird b, float size, float flap, float life, Vector3 cam, float rnd)
        {
            Vector3 dir = b.Dir.sqrMagnitude > 1e-6f ? b.Dir.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir); if (right.sqrMagnitude < 1e-4f) right = Vector3.right; right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);
            var pts = new Vector3[5]; var w = new float[5]; var al = new float[5];
            for (int s = -1; s <= 1; s += 2)
            {
                float lift = Mathf.Sin(flap) * 0.9f;
                Vector3 arm = (right * s * Mathf.Cos(lift) + up * Mathf.Sin(lift)).normalized;
                Vector3 shoulder = b.Pos + right * s * 0.12f * size;
                // six feathers fanned along the arm, longer toward the wingtip, swept back
                for (int f = 0; f < 6; f++)
                {
                    float u = (f + 1) / 6f;
                    Vector3 root = shoulder + arm * (0.15f + 0.85f * u) * 0.9f * size;
                    Vector3 fd = (arm * (0.3f + 0.7f * u) - dir * (0.9f - 0.4f * u) + up * 0.05f).normalized;
                    float len = (0.45f + 0.75f * u) * size;
                    for (int k = 0; k < 5; k++)
                    {
                        float v = k / 4f;
                        pts[k] = root + fd * len * v + up * Mathf.Sin(v * 2.5f) * 0.06f * size * s;
                        w[k] = (0.2f + 0.09f * u) * size * (1f - 0.75f * v) * (0.7f + 0.3f * Mathf.Sin(v * Mathf.PI));
                        al[k] = life * (1f - 0.35f * v);
                    }
                    _wings.Strip(pts, w, null, Color.white, al, rnd + f * 0.07f + (s > 0 ? 0.5f : 0f), cam, 0f, 1f);
                }
            }
            // the head / neck: a short thick stroke forward
            for (int k = 0; k < 5; k++) { float v = k / 4f; pts[k] = b.Pos + dir * (-0.1f + 0.55f * v) * size + up * 0.06f * size; w[k] = 0.3f * size * (1f - 0.6f * v); al[k] = life; }
            _wings.Strip(pts, w, null, Color.white, al, rnd + 0.3f, cam, 0f, 1f);
            // the tail: three flame ribbons trailing behind
            for (int tl = -1; tl <= 1; tl++)
            {
                for (int k = 0; k < 5; k++) { float v = k / 4f; pts[k] = b.Pos - dir * (0.1f + 1.3f * v) * size + right * tl * 0.25f * v * size + up * Mathf.Sin(v * 3f + flap) * 0.12f * size; w[k] = 0.32f * size * (1f - 0.6f * v); al[k] = life * (1f - 0.6f * v); }
                _tails.Strip(pts, w, null, Color.white, al, rnd + tl * 0.11f, cam, 0f, 1f);
            }
        }

        protected override void OnMark(Mark m) { }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vector3 c = m.Centre + Vector3.up * 0.5f;
            if (Natural(m)) Vol2FsB1.Emit(_smoke, c + Vector3.up * 0.5f, 3);
            else { Vol2FsB1.Emit(_embers, c, 120); Vol2FsB1.Emit(_smoke, c, 4); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _blastLight.transform.position = _blast;
            _blastLight.intensity = h < 0.05f ? 14f * h / 0.05f : 14f * Mathf.Exp(-(h - 0.05f) * 3.5f);
            // the ink bursts (the first blast and every landing, all alike)
            _burstS.Clear();
            for (int i = 0; i < _bursts.Count; i++)
            {
                var bs = _bursts[i]; if (bs.T < 0f) { bs.T = t; _bursts[i] = bs; }
                Vol2FsB1.DrawBurst(_burstS, bs.S, bs.At, t - bs.T, cam, 0.5f, 0.1f);
            }
            _burstS.Write(_burstMesh);
            // the birds
            _wings.Clear(); _tails.Clear();
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Bird b)) continue;
                float k = HopK(hop, t);
                if (t < hop.Start || k >= 1f) { if (k >= 1f) b.L.intensity = 0f; continue; }
                b.P2 = CentreNow(hop);
                float u = k * k * (3f - 2f * k) * 0.25f + k * 0.75f;
                Vector3 p = Bez(b.P0, b.P1, b.P2, u), p2 = Bez(b.P0, b.P1, b.P2, Mathf.Min(1f, u + 0.03f));
                b.Dir = (p2 - p).sqrMagnitude > 1e-6f ? (p2 - p).normalized : b.Dir; b.Pos = p;
                float size = 1.7f;                                                  // every bird the same: the peak's fixed share
                float flap = (t - hop.Start) * 15f + b.Phase;
                float grow = Vol2Fs.Smooth(0f, 0.12f, t - hop.Start);
                DrawBird(b, size * (0.4f + 0.6f * grow), flap, grow, cam, 0.2f + hop.Order * 0.13f);
                Vol2FsB1.EmitV(_fire, p + Random.insideUnitSphere * 0.15f, -b.Dir * 1.5f + Vector3.up * 0.5f, 0.55f, 0.35f);
                Vol2FsB1.Emit(_embers, p, 3); if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drops, p, 1);
                b.L.transform.position = p; b.L.intensity = 6f;
            }
            _wings.Write(_wingMesh); _tails.Write(_tailMesh);
            // the marks: a burning scorch at the feet, the body burning; the warning gutters the flames; natural expiry burns down into ash
            _scorch.Clear();
            float firstBurn = 2f;
            foreach (var s in Direct) if (t - s.At < firstBurn && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.45f + 0.3f * Random.value, -1f);
            foreach (var m in MarkList)
            {
                float life = MarkLife(m, t, 0.2f), warn = MarkWarn(m, t), ex = MarkExit(m, t), age = MarkAge(m, t);
                if (age < 0f) continue;
                if (life > 0.01f) _scorch.Quad(m.Centre + Vector3.up * 0.025f, Right, Fwd, 2.4f * ShareScale(m.Share), new Color(1f, 1f, 1f, Mathf.Min(1f, life + 0.3f * (1f - ex))), 0.4f + m.Id % 5 * 0.1f);
                if (m.EndAt < 0f)
                {
                    if (warn > 0.01f && Random.value < warn * 0.7f) continue;      // the flames gutter before the expiry
                    Vector3 c = m.Hop != null ? CentreNow(m.Hop) : m.Centre + Vector3.up * 0.6f;
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, c + Random.insideUnitSphere * 0.4f, Vector3.up * 1.4f, 0.5f + 0.35f * Random.value, -1f);
                    if (Time.frameCount % 7 == 0) Vol2FsB1.EmitV(_tall, m.Centre + Random.insideUnitSphere * 0.3f + Vector3.up * 0.2f, Vector3.up * 1.4f, 0.8f * (1f - 0.6f * warn), -1f);
                    if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.3f, 2);
                }
                else if (Natural(m) && ex < 1f && Time.frameCount % 2 == 0) { Vol2FsB1.Emit(_ash, (m.Hop != null ? CentreNow(m.Hop) : m.Centre) + Random.insideUnitSphere * 0.4f, 4); Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.4f, 1); }
            }
            if (h < 0.4f) _scorch.Quad(G(_blast) + Vector3.up * 0.025f, Right, Fwd, 2.6f * Vol2Fs.Smooth(0f, 0.15f, h), new Color(1f, 1f, 1f, 1f - Vol2Fs.Smooth(0.2f, 0.4f, h) * 0f), 0.11f);
            else { bool any = false; foreach (var m in MarkList) if (MarkLife(m, t) > 0.01f) any = true; float sl = any ? 1f : 1f - Vol2Fs.Smooth(0f, 0.8f, h - 2.5f); if (sl > 0.01f) _scorch.Quad(G(_blast) + Vector3.up * 0.025f, Right, Fwd, 2.6f, new Color(1f, 1f, 1f, sl), 0.11f); }
            _scorch.Write(_scorchMesh);
        }
    }
}
