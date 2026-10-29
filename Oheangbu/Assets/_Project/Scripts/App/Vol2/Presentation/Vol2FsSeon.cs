using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>선 (metal, ㅓ attachment, 번진다) - free showcase batch 4, concept seon_b (a ring blade - a chakram - spins on the brush head; the swing cuts the close bear
    /// and ring blades fly off it, curving through the air on bright silver arcs to the other bears, cutting each; crystal-sharp steel shards jut from the ground at
    /// their feet). Unlike 서 (a crescent guard on the shaft) and 석 (a glaive of blades, 남긴다) the brush carries ONE spinning ring. Beats, all executor facts: the
    /// attachment host's 6 s = the chakram spinning on the brush head (HostLife; wobbling in its last 0.4 s) + a silver arc along the real swing -> the "Instant"
    /// touch = a white cut and sparks on that body -> every "Spread" contact = a ring blade flung from that body on a curving arc to the neighbour (size by the share)
    /// -> it cuts (a bright slash line, sparks) and breaks into shards -> the Mark (3 s) = steel shards jutting from the ground round the neighbour's feet, glinting;
    /// warning = the glints flicker -> natural expiry (CSV: 쨍하게 끊기며 파편이 옆으로 튐) = a ringing flash, the shards snap and fly out sideways.</summary>
    public sealed class Vol2FsSeon : Vol2FsSpread
    {
        static readonly Color Silver = new Color(0.82f, 0.9f, 1f), Gold = new Color(1f, 0.86f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _sparks, _shards, _flash, _glints; Light _ringLight, _cutLight;
        Material _steel, _steelHot; Mesh _chakramMesh;
        Transform _ring;
        Mesh _lightMesh, _inkMesh;
        readonly Vol2FsStrips _light = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        readonly List<(Vector3 A, Vector3 B, float T)> _slashes = new List<(Vector3, Vector3, float)>();
        sealed class Flung { public Transform T; public Vector3 A, B, Side; public bool Broken; public Light L; }
        sealed class Spikes { public List<(Transform T, Vector3 Pos, Quaternion Rot, float S)> Items = new List<(Transform, Vector3, Quaternion, float)>(); public bool Snapped; }
        // r3 (Phantom Blade Zero reference, principles 2 + 5): the swing as a blade-edged crescent + two phantom afterimages in the body's accent colour
        Mesh _phantomMesh; readonly Vol2FsStrips _phantom = new Vol2FsStrips(); float _lastFast = -9f;
        protected override float HopSpeed => 9f;
        protected override float HopMin => 0.3f;
        protected override float HopMax => 0.5f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 0.7f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB4.Steel(Kit, 1.0f); _steelHot = Vol2FsB4.Steel(Kit, 1.6f, true);
            _chakramMesh = Kit.SharedMesh("b4chakram", () => Vol2FsB4A.Chakram(8, 0.55f));
            _ring = Vol2FsB1.Prop(root, "Chakram", _chakramMesh, _steel, false).transform; _ring.localScale = Vector3.one * 0.001f;
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.025f, Size1 = 0.05f, Radius = 0.1f, Gravity = 1.4f,
                Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1500 }, root, "Sparks");
            var shard = Kit.SharedMesh("b4shard", () => Vol2FsB1.Shard(7));
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = shard, Life0 = 0.5f, Life1 = 0.9f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.08f, Size1 = 0.2f, Radius = 0.1f, Gravity = 1.6f, Drag = 0.4f, Spin = 900f, Max = 200 }, root, "Shards");
            var fl = M("fs_dot", new Color(0.85f, 0.9f, 1f), 2.2f, 1f); fl.Soft = 0.2f;
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(fl), Life0 = 0.08f, Life1 = 0.12f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.9f, Size1 = 1.2f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.3f, SizeEnd = 0.6f, Max = 12, SortFudge = -10 }, root, "Flash");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 4f, 1f)), Life0 = 0.1f, Life1 = 0.18f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.08f, Size1 = 0.16f, Radius = 0.01f, Max = 200 }, root, "Glints");
            foreach (var ps in new[] { _sparks, _shards, _flash, _glints }) ps.Play(false);
            var line = M("fs_streak", new Color(0.75f, 0.85f, 1f), 2.8f, 1f); line.NoiseAmt = 0.3f; line.Erode = 0.2f;
            Vol2Fs.MeshObject(root, "SilverArcs", Kit.Mat(line), out _lightMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", Ink, new Color(0.7f, 0.8f, 1f, 1f), 0.08f, 0.5f, 0.32f, 1.6f), out _inkMesh);
            _ringLight = Vol2Fs.PointLight(root, "RingLight", Silver, 2.5f);
            _cutLight = Vol2Fs.PointLight(root, "CutLight", Silver, 4f);
            Vol2Fs.MeshObject(root, "Phantoms", Vol2FsB4A.PhantomMat(Kit, new Color(0.55f, 0.75f, 1f)), out _phantomMesh);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_glints, Tip() + Random.insideUnitSphere * 0.3f, 1); }
        protected override void OnLaunch(long nowUs) { Vol2FsB1.Emit(_flash, Tip(), 1); }
        protected override void OnHit(long nowUs) { }

        void Cut(Vector3 c, float k, Vector3 dir)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(dir));
            Vector3 s = (side + Vector3.up * 0.45f).normalized * 0.9f * k;
            _slashes.Add((c - s, c + s, -1f));
            Vol2FsB1.Emit(_sparks, c, Mathf.RoundToInt(70 * k)); Vol2FsB1.Emit(_flash, c, 1);
            // r3 (Phantom Blade Zero principle 4): a short spark SHOWER at the cut - a fan of hot sparks thrown along the slash and falling
            Vector3 along = (s / Mathf.Max(0.01f, s.magnitude));
            for (int i = 0; i < Mathf.RoundToInt(60 * k); i++)
            {
                float sgn = i % 2 == 0 ? 1f : -1f;
                Vector3 v = (along * sgn * Random.Range(0.6f, 1f) + Vector3.up * Random.Range(0.2f, 0.9f) + Random.insideUnitSphere * 0.3f).normalized * Random.Range(4f, 9f);
                Vol2FsB1.EmitV(_sparks, c + along * sgn * Random.Range(0f, 0.5f) * k, v, -1f, Random.Range(0.35f, 0.7f));
            }
            _cutLight.transform.position = c; _cutLight.intensity = 5f * k;
        }

        protected override void OnStruck(Struck s) { Cut(CentreNow(s), s.Kind == "Second" ? 0.6f : 1f, FeetOf(s) - Inst.Origin); }

        protected override void OnHop(Hop h)
        {
            var fl = new Flung { T = Vol2FsB1.Prop(Root.transform, "Flung_" + h.TargetId, _chakramMesh, _steel, false).transform, L = Vol2Fs.PointLight(Root.transform, "FlungLight", Silver, 3f) };
            fl.T.localScale = Vector3.one * 0.001f;
            fl.A = h.FromP + Vector3.up * 1.1f; fl.B = CentreNow(h);
            fl.Side = Vector3.Cross(Vector3.up, FlatN(fl.B - fl.A)) * (h.Order % 2 == 0 ? 1f : -1f);
            h.Data = fl;
        }

        Vector3 FlungPoint(Flung f, float u) => Vector3.Lerp(f.A, f.B, u) + Vector3.up * 4f * 0.9f * u * (1f - u) + f.Side * 1.1f * Mathf.Sin(u * Mathf.PI);

        protected override void OnLand(Hop h)
        {
            if (!(h.Data is Flung f)) return;
            f.Broken = true; f.L.intensity = 0f; f.T.localScale = Vector3.one * 0.001f;
            Vector3 c = CentreNow(h);
            Cut(c, ShareScale(h.Share), c - f.A);
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(c - f.A));
            for (int i = 0; i < 14; i++) Vol2FsB1.EmitV(_shards, c + Random.insideUnitSphere * 0.2f, (side * (i % 2 == 0 ? 1f : -1f) * Random.Range(4f, 8f) + Vector3.up * Random.Range(0.5f, 2.5f)), -1f, -1f);
        }

        protected override void OnMark(Mark m)
        {
            var sp = new Spikes(); var mesh = Kit.SharedMesh("b4blade", () => Vol2FsB1.Blade());
            float k = ShareScale(m.Share);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f + Vol2Fs.Hash(Seed + (int)m.Id, i) * 0.6f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = m.Centre + o * m.Radius * (1.0f + 0.5f * Vol2Fs.Hash(Seed, i + 50)) * k; p.y = Ground - 0.05f;
                Vector3 up = (Vector3.up * 1.4f + o * (0.5f + 0.4f * Vol2Fs.Hash(Seed, i + 70))).normalized;
                var r = Vol2FsB1.Prop(Root.transform, "Spike" + m.Id + "_" + i, mesh, i % 3 == 0 ? _steelHot : _steel, false);
                var rot = Quaternion.LookRotation(up, o);
                r.transform.SetPositionAndRotation(p, rot); r.transform.localScale = Vector3.one * 0.001f;
                sp.Items.Add((r.transform, p, rot, (0.55f + 0.5f * Vol2Fs.Hash(Seed + (int)m.Id, i + 90)) * k));
            }
            m.Data = sp;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is Spikes sp) || m.Why == Vol2MaterialEnd.Aborted) return;
            sp.Snapped = true;
            // ringing flash, the shards snap and fly out sideways
            Vector3 c = m.Centre + Vector3.up * 0.4f; Vector3 side = Vector3.Cross(Vector3.up, FlatN(m.Centre - Inst.Origin));
            Vol2FsB1.Emit(_flash, c, 1); Vol2FsB1.Emit(_sparks, c, 60);
            for (int i = 0; i < 16; i++) Vol2FsB1.EmitV(_shards, c + Random.insideUnitSphere * 0.4f, side * (i % 2 == 0 ? 1f : -1f) * Random.Range(4f, 9f) + Vector3.up * Random.Range(0.5f, 2f), -1f, -1f);
            _cutLight.transform.position = c; _cutLight.intensity = 4f;
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _light.Clear(); _ink.Clear(); _phantom.Clear();
            _cutLight.intensity = Mathf.MoveTowards(_cutLight.intensity, 0f, Time.deltaTime * 25f);
            // the chakram on the brush head (the host's life)
            float dress = HostLife(t, 0.2f, 0.3f), warn = HostWarn(t);
            Vector3 axis = ShaftAxis, tip = Tip() + axis * 0.12f;
            float wob = warn > 0f ? Mathf.Sin(t * 40f) * 12f * warn : 0f;
            _ring.SetPositionAndRotation(tip, Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.Euler(wob, t * 900f, 0f));
            _ring.localScale = Vector3.one * Mathf.Max(0.001f, 0.38f * dress);
            _ringLight.transform.position = tip; _ringLight.intensity = 1.8f * dress;
            if (dress > 0.05f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_glints, tip + Random.onUnitSphere * 0.38f, 1);
            if (TipSpeed() > 3f) _lastFast = t;
            if (dress > 0.05f && t - _lastFast < 0.12f) { Vol2FsB4A.BladeTrail(_light, TipPath, t, 0.2f, 0.22f, dress, 0.6f, cam); Vol2FsB4A.BladeTrail(_ink, TipPath, t, 0.18f, 0.05f, dress, 0.1f, cam); Vol2FsB4A.Phantoms(_phantom, TipPath, t, 0.22f, 0.28f, dress, cam); }
            // the slashes: a bright cut line across the body that fades in 0.35 s
            for (int i = 0; i < _slashes.Count; i++)
            {
                var s = _slashes[i]; if (s.T < 0f) { s.T = t; _slashes[i] = s; }
                float a = t - s.T, life = 1f - Vol2Fs.Smooth(0.05f, 0.35f, a); if (life <= 0f) continue;
                float g = Vol2Fs.Smooth(0f, 0.05f, a);
                Vector3 b = Vector3.Lerp(s.A, s.B, g);
                _light.Strip(new[] { s.A, Vector3.Lerp(s.A, b, 0.5f), b }, new[] { 0.03f, 0.12f, 0.03f }, null, Color.white, new[] { life, life, life }, 0.3f + i * 0.1f, cam, 0f, 1f);
            }
            // the flung ring blades: curving, spinning, a silver arc behind them
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Flung f) || t < hop.Start) continue;
                float g = HopK(hop, t); f.B = CentreNow(hop);
                if (!f.Broken)
                {
                    Vector3 p = FlungPoint(f, g), p2 = FlungPoint(f, Mathf.Min(1f, g + 0.03f));
                    Vector3 fwd = (p2 - p).sqrMagnitude > 1e-6f ? (p2 - p).normalized : FlatN(f.B - f.A);
                    f.T.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, t * 1500f, 0f) * Quaternion.Euler(0f, 0f, 20f));
                    f.T.localScale = Vector3.one * 0.55f * ShareScale(hop.Share) * Vol2Fs.Smooth(0f, 0.06f, t - hop.Start);
                    f.L.transform.position = p; f.L.intensity = 3f;
                    if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_glints, p, 1);
                }
                float after = g >= 1f ? t - hop.Start - hop.Travel : 0f;
                float life = 1f - Vol2Fs.Smooth(0.45f, 1.15f, after);      // r2: the arc stays longer (the flight A -> B / C must read)
                if (life <= 0.01f) continue;
                float tail = g < 1f ? 0f : Mathf.Min(1f, after * 0.9f);
                const int n = 14; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
                for (int k = 0; k < n; k++) { float v = k / (n - 1f), u = Mathf.Lerp(tail, g, v); pts[k] = FlungPoint(f, u); w[k] = 0.17f * (0.35f + 0.65f * v) * ShareScale(hop.Share); al[k] = life * (0.4f + 0.6f * v); }
                _light.Strip(pts, w, null, Color.white, al, 0.5f + hop.Order * 0.13f, cam, 0f, 1f);
                for (int k = 0; k < n; k++) w[k] *= 0.35f;
                _ink.Strip(pts, w, null, Color.white, al, 0.7f + hop.Order * 0.11f, cam, 0f, 1f);
            }
            // the marks: steel shards jutting round the feet, glinting; flicker in the warning; snapped away at the end
            foreach (var m in MarkList)
            {
                if (!(m.Data is Spikes sp)) continue;
                float age = MarkAge(m, t), warn2 = MarkWarn(m, t);
                for (int i = 0; i < sp.Items.Count; i++)
                {
                    var it = sp.Items[i];
                    float grow = age < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01((age - 0.02f * i) / 0.16f));
                    float s = sp.Snapped ? 0.001f : Mathf.Max(0.001f, it.S * grow);
                    it.T.SetPositionAndRotation(it.Pos, it.Rot);
                    it.T.localScale = new Vector3(s * 3f, s * 3f, s);
                    if (!sp.Snapped && age > 0.2f && Random.value < 0.04f * (1f + 3f * warn2)) Vol2FsB1.Emit(_glints, it.Pos + it.Rot * Vector3.forward * s * 0.9f, 1);
                }
            }
            _light.Write(_lightMesh); _ink.Write(_inkMesh); _phantom.Write(_phantomMesh);
        }
    }
}
