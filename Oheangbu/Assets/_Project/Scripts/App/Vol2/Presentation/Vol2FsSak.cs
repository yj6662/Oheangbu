using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>삭 (metal, ㅏ path, 남긴다) - free showcase batch 3, concept sak_a (a cluster of long polished steel blades bursts up out of the ground at the bear's
    /// feet like a steel thornbush - a bloom of swords, points up and out, white-gold glints, a humming light at its heart; a thin bright line hangs from the brush).
    /// Unlike 사's thrown sword / 소's blade fan / 수's saw it is a spinning steel STAR that flies and a bloom that STAYS. Beats, executor facts only: charge (light
    /// lines converge) -> the star flies to the predicted first body on a razor streak -> the cut (X flash, sparks) -> the residue material appears = the bloom tears
    /// up blade by blade and stands humming; any other metal residue within 3 m is joined to it by a taut bright blade-line (CSV: 3 m 안 금 재료끼리 날선으로 이어진다 -
    /// read from the registry); a body drawing from it = a ringing flash -> the last 0.4 s = the blades shiver -> natural expiry (CSV: 쨍하게 끊기며 파편이 옆으로 튐) =
    /// they snap with a ring and the shards fly SIDEWAYS.</summary>
    public sealed class Vol2FsSak : Vol2FsLeave
    {
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _lines, _sparks, _shards, _glints, _flash, _smoke; Light _chargeLight, _heart;
        Transform _star; readonly List<Transform> _starBlades = new List<Transform>(); Material _steel, _steelHot;
        Mesh _streakMesh, _xMesh, _linkMesh, _inkMesh; readonly Vol2FsStrips _streakS = new Vol2FsStrips(), _xS = new Vol2FsStrips(), _linkS = new Vol2FsStrips(), _inkS = new Vol2FsStrips();
        sealed class Blade { public Transform T; public Vector3 Dir; public float Len, At, Rnd; }
        readonly List<Blade> _blades = new List<Blade>();
        Vector3 _base, _heartPos; bool _built, _snapped; float _cutAt = -1f; Vector3 _cutAtPos;
        protected override float LobHeight => 0.15f;
        protected override float ExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.82f, 0.86f, 0.95f), 0.5f, 0.82f, new Color(0.5f, 0.56f, 0.68f), "fs_steel");      // r3: cooler (r2 pink from the red ground)      // r2: less self-light (r1 blades read as white paper)
            _steelHot = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.95f, 0.93f, 0.88f), 0.6f, 0.85f, new Color(0.95f, 0.85f, 0.6f), "fs_steel");
            var bladeMesh = Kit.SharedMesh("blade", () => Vol2FsB1.Blade());
            _star = new GameObject("Star").transform; _star.SetParent(root, false); _star.position = Tip();
            for (int k = 0; k < 5; k++)
            {
                var r = Vol2FsB1.Prop(_star, "StarBlade" + k, bladeMesh, _steelHot, false);
                r.transform.localRotation = Quaternion.AngleAxis(k * 72f, Vector3.forward) * Quaternion.LookRotation(Vector3.up, Vector3.back);
                r.transform.localScale = Vector3.one * 0.01f; _starBlades.Add(r.transform);
            }
            var line = M("fs_streak", White, 4f, 1f); line.NoiseAmt = 0.2f;
            _lines = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(line), Rate = 60f, Loop = true, Life0 = 0.12f, Life1 = 0.2f, Speed0 = -9f, Speed1 = -14f, Size0 = 0.025f, Size1 = 0.045f, Radius = 1.2f, Stretch = true, StretchVel = 0.03f, World = false, Max = 60 }, _star, "Lines");
            _chargeLight = Vol2Fs.PointLight(_star, "ChargeLight", White, 3.5f);
            _lines.Play(false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 5f, Speed1 = 15f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.15f, Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 900 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 1f, Speed1 = 2f, Size0 = 0.12f, Size1 = 0.3f, Radius = 0.1f, Gravity = 1.3f, Drag = 0.7f, Spin = 900f, Max = 160 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.1f, Life1 = 0.2f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.18f, Size1 = 0.35f, Radius = 0.01f, Max = 120, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.4f, Size1 = 1.8f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 8, SortFudge = -30 }, root, "Flash");
            var smoke = Vol2FsB3.Smoke(0.45f);
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.6f, Size1 = 1.1f, Radius = 0.3f, Tiles = 2, VelOverLife = new Vector3(0f, 0.6f, 0f), SizeEnd = 1.8f, Max = 60, SortFudge = 10 }, root, "InkSmoke");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash, _smoke }) ps.Play(false);
            var streak = M("fs_streak", White, 5f, 1f); streak.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "Streak", Kit.Mat(streak), out _streakMesh);
            Vol2Fs.MeshObject(root, "XFlash", Kit.Mat(streak), out _xMesh);
            var link = M("fs_streak", Gold, 4f, 1f); link.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "BladeLines", Kit.Mat(link), out _linkMesh);
            var ink = M("fs_ink", Ink, 1f, 0f); ink.Soft = 0.001f; ink.NoiseAmt = 0.45f; ink.Erode = 0.3f; ink.Opacity = 0.85f; ink.Edge = new Color(0.9f, 0.85f, 0.7f, 1f); ink.EdgeWidth = 0.05f;
            Vol2Fs.MeshObject(root, "InkSplat", Kit.Mat(ink), out _inkMesh);
            _heart = Vol2Fs.PointLight(root, "Heart", new Color(1f, 0.92f, 0.75f), 5f);
        }

        Vector3 Hold() => Tip() + Fwd * 0.3f + Vector3.up * 0.1f;
        static Quaternion Look(Vector3 d) => Quaternion.LookRotation(d, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);

        protected override void OnCharge(float t, float k)
        {
            _star.position = Hold();
            _star.rotation = Quaternion.LookRotation(Fwd, Vector3.up) * Quaternion.AngleAxis(t * 720f, Vector3.forward);
            float s = Vol2Fs.Smooth(0.1f, 1f, k);
            foreach (var b in _starBlades) b.localScale = new Vector3(1.6f, 1.6f, 0.5f) * Mathf.Max(0.01f, s);      // r2: smaller (r1: a paper windmill)
            _chargeLight.intensity = 3.5f * k;
        }

        protected override void OnLaunch(long nowUs) { _lines.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; From = _star.position; }

        protected override void OnFly(float t, float f, float prev)
        {
            _star.position = Line(f);
            _star.rotation = Quaternion.LookRotation(FlatN(HitPoint - From), Vector3.up) * Quaternion.AngleAxis(t * 1800f, Vector3.forward);
            DrawStreak(f, 1f);
        }

        void DrawStreak(float f, float life)
        {
            _streakS.Clear();
            if (life > 0.01f && f > 0.01f)
            {
                var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                for (int k = 0; k < 8; k++) { float u = k / 7f * f; pts[k] = Line(u); w[k] = 0.05f + 0.06f * u; al[k] = life; }
                _streakS.Strip(pts, w, null, Color.white, al, 0.2f, CamPos, 0f, 1f);
            }
            _streakS.Write(_streakMesh);
        }

        protected override void OnHit(long nowUs)
        {
            foreach (var b in _starBlades) b.gameObject.SetActive(false);
            Vector3 at = HasTarget ? HitPoint : AimPoint;
            _cutAt = 0f; _cutAtPos = at;
            Vol2FsB1.Emit(_flash, at, 1); Vol2FsB1.Emit(_sparks, at, 200); Vol2FsB1.Emit(_glints, at, 8);
            _heart.transform.position = at; _heart.intensity = 9f;
        }

        protected override void OnResidue() { Build(); }

        void Build()
        {
            if (_built) return;
            _built = true;
            var root = Root.transform;
            _base = ResCentre; _base.y = Ground;
            _heartPos = _base + Vector3.up * 0.45f;
            var bladeMesh = Kit.SharedMesh("blade", () => Vol2FsB1.Blade());
            int seed = Seed + 51;
            Vector3 toCam = FlatN(CamPos - _base);
            for (int k = 0; k < 32; k++)
            {
                // directions on the upper hemisphere, biased up, a few leaning to the sides - the bloom; none straight at the camera
                float a = (k / 32f + 0.04f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f;
                float up = Mathf.Lerp(0.6f, 2.2f, Vol2Fs.Hash(seed, k + 10));
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Vector3.Dot(o, toCam) > 0.7f) o = Vector3.Cross(Vector3.up, o) * (k % 2 == 0 ? 1f : -1f);
                Vector3 dir = (o + Vector3.up * up).normalized;
                float len = Mathf.Lerp(1.2f, 3.0f, Vol2Fs.Hash(seed, k + 20)) * (up > 1.2f ? 1.1f : 0.8f);
                var r = Vol2FsB1.Prop(root, "Bloom" + k, bladeMesh, k % 4 == 0 ? _steelHot : _steel);
                r.transform.position = _base + o * 0.12f + Vector3.up * 0.05f;
                r.transform.rotation = Look(dir) * Quaternion.AngleAxis(Vol2Fs.Hash(seed, k + 30) * 180f, Vector3.forward);
                r.transform.localScale = new Vector3(2.6f, 2.6f, 0.001f);
                _blades.Add(new Blade { T = r.transform, Dir = dir, Len = len, At = 0.01f * k, Rnd = Vol2Fs.Hash(seed, k + 40) });
            }
            Vol2FsB1.Emit(_sparks, _heartPos, 160); Vol2FsB1.Emit(_flash, _heartPos, 1); Vol2FsB1.Emit(_smoke, _base + Vector3.up * 0.2f, 8);
        }

        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_flash, CentreNow(s), 1); Vol2FsB1.Emit(_sparks, CentreNow(s), 90); }
        protected override void OnChanged(string what) { if (what == "extended" || what == "thickened") Vol2FsB1.Emit(_glints, _heartPos + Vector3.up * 0.6f, 6); }

        protected override void OnResidueEnd()
        {
            if (_snapped) return;
            _snapped = true;
            // natural end: a ringing snap, every blade breaks and the shards fly SIDEWAYS (the camera's left / right); other ends: a dull break, shards drop
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(_base - CamPos));
            Vol2FsB1.Emit(_flash, _heartPos, NaturalEnd ? 2 : 1);
            foreach (var b in _blades)
            {
                Vector3 mid = b.T.position + b.Dir * b.Len * 0.5f;
                int n = NaturalEnd ? 5 : 2;
                for (int j = 0; j < n; j++)
                {
                    Vector3 v = NaturalEnd ? side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(5f, 11f) + Vector3.up * Random.Range(0.5f, 3f) : Random.insideUnitSphere * 1.5f;
                    Vol2FsB1.EmitV(_shards, mid + b.Dir * b.Len * (j / (float)n - 0.4f), v, Random.Range(0.12f, 0.28f), -1f);
                }
                if (NaturalEnd) Vol2FsB1.EmitV(_sparks, mid, side * Random.Range(-12f, 12f) + Vector3.up * 2f, -1f, -1f);
            }
            if (NaturalEnd) Vol2FsB1.Emit(_sparks, _heartPos, 160);
            if (ResWhy == Vol2MaterialEnd.Cleansed) Vol2FsB1.Emit(_smoke, _heartPos, 8);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            DrawStreak(1f, 1f - Vol2Fs.Smooth(0.08f, 0.6f, h));
            // the X of white light opening on the body
            _xS.Clear();
            float xl = 1f - Vol2Fs.Smooth(0.05f, 0.35f, h);
            if (xl > 0.01f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, FlatN(_cutAtPos - cam)); float s = 1.4f * Vol2Fs.EaseOutBack(h / 0.08f);
                foreach (var d in new[] { (side + Vector3.up).normalized, (side - Vector3.up).normalized })
                    _xS.Strip(new[] { _cutAtPos - d * s, _cutAtPos, _cutAtPos + d * s }, new[] { 0.02f, 0.16f, 0.02f }, null, Color.white, new[] { xl, xl, xl }, 0.3f, cam, 0f, 1f);
            }
            _xS.Write(_xMesh);
            float ra = ResAge(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            if (!_built) { _heart.intensity = Mathf.Max(0f, 9f * (1f - h / 0.4f)); return; }
            // the bloom tears up blade by blade; it shivers in the warning; it is gone the moment it snaps
            foreach (var b in _blades)
            {
                float g = ra < 0f ? 0f : Vol2Fs.EaseOutBack((ra - b.At) / 0.16f);
                bool on = !_snapped;
                b.T.gameObject.SetActive(on && g > 0.01f);
                if (!on) continue;
                Quaternion shiver = Quaternion.identity;
                if (warn > 0f) shiver = Quaternion.Euler((Mathf.PerlinNoise(t * 47f, b.Rnd * 9f) - 0.5f) * 9f * warn, (Mathf.PerlinNoise(b.Rnd * 7f, t * 43f) - 0.5f) * 9f * warn, 0f);
                if (pulse > 0f) shiver *= Quaternion.Euler(Mathf.Sin(t * 90f + b.Rnd * 10f) * 4f * pulse, 0f, 0f);
                b.T.rotation = Look(shiver * b.Dir) * Quaternion.AngleAxis(b.Rnd * 180f, Vector3.forward);
                b.T.localScale = new Vector3(2.6f, 2.6f, Mathf.Max(0.001f, b.Len * g));
                // glints run up the edges now and then (they are the residue "humming")
                if (g >= 1f && Random.value < 0.012f + 0.04f * warn) Vol2FsB1.Emit(_glints, b.T.position + b.Dir * b.Len * Random.Range(0.4f, 1f), 1);
            }
            float hum = _snapped ? 0f : Mathf.Clamp01(ra / 0.3f) * (0.8f + 0.2f * Mathf.Sin(t * 13f));
            _heart.transform.position = _heartPos;
            _heart.intensity = Mathf.Max(h < 0.4f ? 9f * (1f - h / 0.4f) : 0f, 4.5f * hum * (1f + 1.2f * pulse) * (warn > 0f ? 0.6f + 0.4f * Mathf.Sin(t * 60f) : 1f), _snapped && ex < 0.2f ? 12f * (1f - ex / 0.2f) : 0f);
            if (!_snapped && ra > 0.2f && Time.frameCount % 20 == 0) Vol2FsB1.Emit(_sparks, _heartPos + Random.insideUnitSphere * 0.3f, 2);
            // blade-lines to every other metal residue within 3 m (the registry's materials - presentation of the rule's link, not the link itself)
            _linkS.Clear();
            var co = Inst.Model.Coordinator;
            if (co != null && !_snapped && ra > 0.2f)
                foreach (var m in co.Materials.Alive)
                {
                    if (m == Res || m.Body != Body.Metal || m.Kind != Vol2MaterialKind.Residue || m.Footprint == null || m.Attached) continue;
                    Vector3 c = m.Footprint.Centre; c.y = Ground + 0.6f;
                    Vector3 a = _heartPos + Vector3.up * 0.15f; Vector3 d = c - a; d.y = 0f;
                    if (d.magnitude > 3f || d.magnitude < 0.2f) continue;
                    float l = 0.8f + 0.2f * Mathf.Sin(t * 30f + m.Id.Value);
                    _linkS.Strip(new[] { a, Vector3.Lerp(a, c, 0.5f) + Vector3.up * 0.04f * Mathf.Sin(t * 50f), c }, new[] { 0.035f, 0.05f, 0.035f }, null, Color.white, new[] { l, l, l }, 0.6f, cam, 0f, 1f);
                }
            _linkS.Write(_linkMesh);
            // ink splatter under the bloom
            _inkS.Clear();
            float il = Vol2Fs.Smooth(0f, 0.2f, Mathf.Max(0f, ra)) * (1f - Vol2Fs.Smooth(0.3f, 1f, ex));
            if (il > 0.01f && ra >= 0f) _inkS.Quad(new Vector3(_base.x, Ground + 0.02f, _base.z), Right, Fwd, 2.6f, new Color(1f, 1f, 1f, il), 0.63f);
            _inkS.Write(_inkMesh);
        }
    }
}
