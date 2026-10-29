using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>악 (water, ㅏ path, 남긴다) - free showcase batch 3, concept ak_a (where the water hit the bear a STANDING FOUNTAIN stays: a column of clear water
    /// rising ~2 m and branching at the top like coral, falling back in sheets into a swirling pool on the red ground, ink curling inside) + ak_b's whirlpool at its foot.
    /// Unlike 아's orb / 오's wave / 어's disc / 우's geyser ring it is ONE standing branching fountain. Beats, executor facts only: charge (drops swirl into the tip)
    /// -> a pressurised jet runs to the predicted first body -> the splash (crown, spray, foam; the body drips for the 4 s wetness - a mirror) -> the residue material
    /// appears = the fountain rises out of a whirlpool and keeps flowing for the material's life; a body drawing from it = a surge -> the last 0.4 s = the column
    /// falters -> natural expiry (CSV: 아래로 흘러내림) = it collapses and runs DOWN into the pool, which spreads and soaks away.</summary>
    public sealed class Vol2FsAk : Vol2FsLeave
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.95f, 0.95f);
        ParticleSystem _charge, _spray, _drops, _foam, _mist, _drip, _jetDrops; Light _light, _chargeLight;
        Mesh _jetMesh, _colMesh, _branchMesh, _poolMesh, _wetMesh, _inkMesh, _crownMesh;
        readonly Vol2FsStrips _jet = new Vol2FsStrips(), _cols = new Vol2FsStrips(), _branches = new Vol2FsStrips(), _pool = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _crown = new Vol2FsStrips();
        struct Br { public float Ang, Up, Len, W, Rnd, H0; }
        readonly List<Br> _br = new List<Br>();
        Vector3 _base; bool _built, _exitFx; float _poolSpin;
        protected override float LobHeight => 0.12f;
        protected override float ExpireSeconds => 1.5f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var water = M("fs_splash", Color.white, 1.6f, 0.3f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.5f; water.NoiseAmt = 0.4f; water.Soft = 0.1f;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 60f, Loop = true, Life0 = 0.18f, Life1 = 0.28f, Speed0 = -2f, Speed1 = -3f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.6f, World = false, Tiles = 2, Orbital = 6f, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", new Color(0.6f, 0.85f, 1f), 3f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.3f, Size1 = 0.8f, Radius = 0.2f, Gravity = 1.6f, Drag = 0.5f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 0.8f, Spin = 120f, Max = 1600 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.2f, 0.6f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.1f, Gravity = 1.8f,
                Stretch = true, StretchVel = 0.03f, Max = 2500 }, root, "Drops");
            _jetDrops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.4f, 0.6f)), Rate = 0f, RatePerMeter = 30f, Loop = true, Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.1f, Gravity = 1.5f, Max = 300 }, root, "JetDrops");
            var foam = M("fs_puff", Foam, 1.3f, 0.2f); foam.Opacity = 0.7f; foam.Erode = 0.4f; foam.NoiseAmt = 0.6f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 0.3f, Speed1 = 1.5f, Size0 = 0.25f, Size1 = 0.6f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Drag = 1.5f, Tiles = 2, Max = 700 }, root, "Foam");
            var mist = M("fs_puff", new Color(0.8f, 0.88f, 0.9f, 1f), 1.1f, 0.2f); mist.Opacity = 0.3f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.8f, Size1 = 1.5f, Radius = 0.3f, Drag = 1.5f, Tiles = 2, Max = 120, SortFudge = 8 }, root, "Mist");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 0f, Loop = true, Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.5f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 300 }, root, "Drip");
            foreach (var ps in new[] { _spray, _drops, _foam, _mist, _drip }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Jet", Vol2FsB1.Water(Kit, 2f, 0.4f, "fs_streak"), out _jetMesh);
            var flow = M("fs_water", Color.white, 1.8f, 0.38f); flow.Ramp = "fs_ramp_water"; flow.RampScale = 1.6f; flow.NoiseAmt = 0.45f; flow.NoiseScale = 1.4f; flow.Erode = 0.22f; flow.Soft = 0.15f; flow.Scroll = new Vector2(-1.6f, 0f); flow.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; flow.EdgeWidth = 0.12f;
            Vol2Fs.MeshObject(root, "Column", Kit.Mat(flow), out _colMesh);
            var fall = flow; fall.Scroll = new Vector2(1.2f, 0f); fall.Tex = "fs_splash";
            Vol2Fs.MeshObject(root, "Branches", Kit.Mat(fall), out _branchMesh);
            Vol2Fs.MeshObject(root, "Crown", Vol2FsB1.Water(Kit, 1.7f, 0.35f, "fs_splash"), out _crownMesh);
            var pool = M("fs_swirl", Color.white, 1.05f, 0.2f); pool.Ramp = "fs_ramp_water"; pool.RampScale = 1.2f; pool.NoiseAmt = 0.45f; pool.Soft = 0.02f; pool.Erode = 0.3f; pool.Opacity = 0.7f;      // r2: softer (r1: a bright cyan plate)
            Vol2Fs.MeshObject(root, "Pool", Kit.Mat(pool), out _poolMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.75f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            Vol2Fs.MeshObject(root, "InkCurls", Kit.InkSolid("fs_stroke2", Ink, new Color(0.5f, 0.9f, 1f, 1f) * 0.8f, 0.05f, 0.45f, 0.26f, 1.4f), out _inkMesh);
            _light = Vol2Fs.PointLight(root, "WaterLight", new Color(0.6f, 0.85f, 1f), 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; _jetDrops.transform.position = From; _jetDrops.Play(false); }

        void DrawJet(float f, float life)
        {
            _jet.Clear();
            if (life > 0.01f && f > 0.01f)
            {
                var pts = new Vector3[10]; var w = new float[10]; var al = new float[10];
                for (int k = 0; k < 10; k++) { float u = k / 9f * f; pts[k] = Line(u) + Vector3.up * 0.04f * Mathf.Sin(u * 20f + Time.time * 30f); w[k] = 0.16f + 0.18f * (u / Mathf.Max(0.01f, f)); al[k] = life; }
                _jet.Strip(pts, w, null, Color.white, al, 0.4f, CamPos, 0f, 3f);
            }
            _jet.Write(_jetMesh);
        }

        protected override void OnFly(float t, float f, float prev) { _jetDrops.transform.position = Line(f); DrawJet(f, 1f); }

        protected override void OnHit(long nowUs)
        {
            _jetDrops.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Vector3 at = HasTarget ? HitPoint : AimPoint;
            Vol2FsB1.Emit(_spray, at, 60); Vol2FsB1.Emit(_drops, at, 200); Vol2FsB1.Emit(_foam, at, 30); Vol2FsB1.Emit(_mist, at, 3);
            _light.transform.position = at; _light.intensity = 6f;
        }

        protected override void OnResidue() { Build(); }

        void Build()
        {
            if (_built) return;
            _built = true;
            _base = ResCentre; _base.y = Ground;
            int seed = Seed + 61;
            for (int k = 0; k < 7; k++)
            {
                float ang = (k / 7f + 0.06f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f;
                _br.Add(new Br { Ang = ang, Up = Mathf.Lerp(0.6f, 1.3f, Vol2Fs.Hash(seed, k + 10)), Len = Mathf.Lerp(1.0f, 1.7f, Vol2Fs.Hash(seed, k + 20)), W = Mathf.Lerp(0.34f, 0.55f, Vol2Fs.Hash(seed, k + 30)),
                    Rnd = Vol2Fs.Hash(seed, k + 40), H0 = Mathf.Lerp(1.7f, 2.4f, Vol2Fs.Hash(seed, k + 50)) });
            }
            Vol2FsB1.Emit(_spray, _base + Vector3.up * 0.3f, 40); Vol2FsB1.Emit(_foam, _base, 20);
            _drip.transform.position = BodyNow();
        }

        protected override void OnStruck(Struck s) { _drip.transform.position = CentreNow(s); }
        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_spray, CentreNow(s), 30); Vol2FsB1.Emit(_drops, CentreNow(s), 80); }
        protected override void OnChanged(string what) { if (what == "extended" || what == "thickened") Vol2FsB1.Emit(_spray, _base + Vector3.up * 2f, 30); }
        protected override void OnResidueEnd()
        {
            if (NaturalEnd || ResWhy == Vol2MaterialEnd.Consumed) Vol2FsB1.Emit(_spray, _base + Vector3.up * 1.6f, 50);
            else Vol2FsB1.Emit(_mist, _base + Vector3.up * 1f, 8);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            DrawJet(1f, 1f - Vol2Fs.Smooth(0.05f, 0.4f, h));
            // the struck body drips for the 4 s of its wetness (the body action's - a mirror)
            bool drip = false;
            foreach (var s in Hits) if (t - s.At < 4f) { drip = true; _drip.transform.position = CentreNow(s); }
            var em = _drip.emission; em.rateOverTime = drip ? 40f : 0f;
            float ra = ResAge(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            _cols.Clear(); _branches.Clear(); _crown.Clear(); _ink.Clear(); _pool.Clear(); _wet.Clear();
            if (_built && ra >= 0f)
            {
                float rise = Vol2Fs.EaseOutBack(ra / 0.45f);
                float falter = warn > 0f ? 1f - warn * (0.35f + 0.3f * Mathf.Sin(t * 25f)) : 1f;
                float collapse = NaturalEnd ? Vol2Fs.Smooth(0f, 0.7f, ex) : Vol2Fs.Smooth(0f, 0.4f, ex);
                float hMul = rise * falter * (1f - collapse) * (1f + 0.25f * pulse);
                float H = 2.7f * hMul;
                Vector3 side = Vector3.Cross(Vector3.up, FlatN(_base - cam));
                // the column: three overlapping water sheets twisting round each other, flowing UP
                if (H > 0.05f)
                    for (int j = 0; j < 3; j++)
                    {
                        var pts = new Vector3[9]; var w = new float[9]; var al = new float[9];
                        for (int k = 0; k < 9; k++)
                        {
                            float u = k / 8f;
                            pts[k] = _base + Vector3.up * H * u + side * 0.14f * Mathf.Sin(u * 5f + t * 4f + j * 2.1f) + FlatN(_base - cam) * 0.1f * Mathf.Cos(u * 4f + t * 3f + j);
                            w[k] = (0.8f - 0.2f * j) * (1f - 0.4f * u) * (0.85f + 0.15f * Mathf.Sin(t * 9f + u * 7f));
                            al[k] = 0.95f * (1f - 0.2f * u);
                        }
                        _cols.Strip(pts, w, null, Color.white, al, 0.2f + 0.3f * j, cam, 0f, 2.5f);
                    }
                // the coral branches at the top, arching out and falling back as sheets
                foreach (var b in _br)
                {
                    float g = Mathf.Clamp01((ra - 0.2f) / 0.35f) * (1f - collapse) * falter;
                    if (g <= 0.02f) continue;
                    Vector3 o = Quaternion.AngleAxis(b.Ang * Mathf.Rad2Deg, Vector3.up) * Vector3.forward;
                    Vector3 s0 = _base + Vector3.up * b.H0 * hMul;
                    var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                    for (int k = 0; k < 8; k++)
                    {
                        float u = k / 7f * g;
                        pts[k] = s0 + o * b.Len * u + Vector3.up * (b.Up * u * 1.2f - 2.2f * u * u * (0.6f + 0.4f * b.Rnd)) + Vector3.Cross(Vector3.up, o) * 0.1f * Mathf.Sin(t * 5f + u * 6f + b.Rnd * 9f);
                        w[k] = b.W * (1f - 0.4f * u) * (1f + 0.3f * pulse);
                        al[k] = 0.9f * (1f - 0.35f * u);
                    }
                    _branches.Strip(pts, w, null, Color.white, al, b.Rnd, cam, 0f, 1.6f);
                    // drops falling off the branch ends
                    if (Time.frameCount % 3 == 0 && ResEndAt < 0f) Vol2FsB1.EmitV(_drops, pts[7], Vector3.down * 0.5f + o * 0.3f, -1f, -1f);
                }
                // a crown of spray at the top + ink curling up inside the column
                if (H > 0.3f) { Vol2FsB3.Plume(_crown, _base + Vector3.up * (H - 0.2f), Vector3.up, side, 0.6f, 1.1f * (1f + 0.3f * pulse), 0.2f, t * 3f, 0.8f, 0.71f, cam, 6, 0.3f); }
                if (H > 0.3f)
                    for (int j = 0; j < 2; j++)
                    {
                        float ph = t * 1.6f + j * 3.1f;
                        var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                        for (int k = 0; k < 8; k++) { float u = k / 7f; pts[k] = _base + Vector3.up * (0.2f + H * 0.8f * u) + (side * Mathf.Sin(u * 7f + ph) + FlatN(_base - cam) * Mathf.Cos(u * 7f + ph) * 0.3f) * 0.16f; w[k] = 0.07f * (1f - 0.5f * u); al[k] = 0.8f; }
                        _ink.Strip(pts, w, null, Color.white, al, 0.3f + 0.4f * j, cam, 0f, 1f);
                    }
                // the pool (a whirlpool turning at its foot) and the wet ground; on the natural end the water runs down and the pool spreads, then soaks away
                _poolSpin += Time.deltaTime * (1.6f + 1.4f * pulse);
                float pr = 1.3f * Vol2Fs.Smooth(0f, 0.4f, ra) * (1f + 0.5f * (NaturalEnd ? Vol2Fs.Smooth(0f, 0.6f, ex) : 0f));
                float pl = 1f - Vol2Fs.Smooth(NaturalEnd ? 0.55f : 0.2f, 1f, ex);
                if (pl > 0.01f)
                {
                    Vector3 r1 = new Vector3(Mathf.Cos(_poolSpin), 0f, Mathf.Sin(_poolSpin)), f1 = Vector3.Cross(r1, Vector3.up);
                    _pool.Quad(new Vector3(_base.x, Ground + 0.03f, _base.z), r1, f1, 2f * pr, new Color(1f, 1f, 1f, pl), 0.41f);
                }
                float wl = Vol2Fs.Smooth(0f, 0.4f, ra) * (1f - Vol2Fs.Smooth(0.7f, 1f, ex));
                if (wl > 0.01f) _wet.Quad(new Vector3(_base.x, Ground + 0.02f, _base.z), Right, Fwd, 2f * pr * 1.6f, new Color(1f, 1f, 1f, wl), 0.27f);
                if (ResEndAt < 0f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_foam, _base + Random.insideUnitSphere * 0.5f, 1);
                if (Exiting && !_exitFx) { _exitFx = true; if (NaturalEnd) Vol2FsB1.Emit(_foam, _base, 40); }
                if (Exiting && NaturalEnd && ex < 0.6f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_spray, _base + Vector3.up * H * Random.value, Vector3.down * 2f + Random.insideUnitSphere, -1f, -1f);
                _light.transform.position = _base + Vector3.up * 1.2f;
                _light.intensity = Mathf.Max(h < 0.4f ? 6f * (1f - h / 0.4f) : 0f, 2.6f * hMul);
            }
            else _light.intensity = Mathf.Max(0f, 6f * (1f - h / 0.4f));
            _cols.Write(_colMesh); _branches.Write(_branchMesh); _crown.Write(_crownMesh); _ink.Write(_inkMesh); _pool.Write(_poolMesh); _wet.Write(_wetMesh);
        }
    }
}
