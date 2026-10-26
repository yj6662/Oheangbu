using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>운 (water, ㅜ ring, 번진다) - free showcase batch 4, concept un_a (outward ripples: a shallow ring of standing water circles the caster; the bear on it is
    /// drenched and from it big concentric ripples run OUTWARD over the ground like a stone dropped in a pond, rising into splashing waves as they reach the bears
    /// farther out; puddles stay at their feet). Unlike 우 (geyser columns) and 욱 (a river flowing round the ring with arches) the ring stands still - rain-pocked
    /// - and the spread is ripples travelling outward from the struck body.
    /// Beats, all executor facts: the water band for the timed emitter's life (3 s) -> the body on the band (Instant) = a splash crown, dripping -> every "Spread"
    /// contact = rings of ripple crests running from that body toward the neighbour (executor order), height by share, breaking over it in a splash wave -> its
    /// Mark (3 s) = a rippling puddle, the body dripping; warning = the ripples die down -> natural expiry (CSV 아래로 흘러내림) = the water runs down off the
    /// body into the puddle, which soaks away.</summary>
    public sealed class Vol2FsUn : Vol2FsSpread
    {
        static readonly Color Foam = new Color(0.9f, 0.95f, 0.95f), Blue = new Color(0.55f, 0.82f, 1f);
        ParticleSystem _charge, _spray, _drops, _foam, _drip, _rain; Light _chargeLight, _light;
        Material _water, _sheet;
        Mesh _bandMesh, _rippleMesh, _crestMesh, _wetMesh, _waveMesh;
        readonly Vol2FsStrips _band = new Vol2FsStrips(), _ripple = new Vol2FsStrips(), _crest = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _wave = new Vol2FsStrips();
        Struck _first;

        protected override float HopSpeed => 6f;
        protected override float HopMin => 0.4f;
        protected override float HopMax => 0.6f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _water = Vol2FsB1.Water(Kit, 1.4f, 0.35f, "fs_water");      // r2: the clearer batch 1 water (WaterDeep read icy here)
            _sheet = Vol2FsB1.Water(Kit, 1.6f, 0.35f, "fs_splash");
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Blue, 2.5f, 0.7f)), Rate = 70f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.04f, Size1 = 0.1f, Radius = 0.6f, World = false, Orbital = 3f, Max = 100 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Blue, 3f);
            _charge.transform.position = Tip(); _charge.Play(true);
            var water = M("fs_splash", Color.white, 1.5f, 0.2f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.4f; water.NoiseAmt = 0.4f; water.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 1.0f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.3f, Size1 = 0.8f, Radius = 0.2f, Gravity = 1.8f, Drag = 0.5f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 0.8f, Spin = 120f, Max = 1600 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.2f, 0.6f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.15f, Gravity = 1.9f,
                Stretch = true, StretchVel = 0.03f, Max = 2500 }, root, "Drops");
            var foam = M("fs_puff", Foam, 1.2f, 0.15f); foam.Opacity = 0.7f; foam.Erode = 0.4f; foam.NoiseAmt = 0.6f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.5f, Life1 = 1.0f, Speed0 = 0.4f, Speed1 = 1.5f, Size0 = 0.25f, Size1 = 0.6f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.2f, Drag = 1.5f, Tiles = 2, Max = 800 }, root, "Foam");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.08f, Size1 = 0.18f, Radius = 0.3f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 800 }, root, "Drip");
            _rain = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.85f, 0.95f, 1f), 1.8f, 0.6f)), Life0 = 0.25f, Life1 = 0.45f, Speed0 = 1f, Speed1 = 2.2f, Size0 = 0.025f, Size1 = 0.05f, Shape = ParticleSystemShapeType.Cone, Angle = 18f, Radius = 0.02f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 1.5f, Stretch = true, StretchVel = 0.04f, Max = 600 }, root, "Bounce");
            foreach (var ps in new[] { _spray, _drops, _foam, _drip, _rain }) ps.Play(false);
            Vol2Fs.MeshObject(root, "WaterBand", _water, out _bandMesh);
            var rip = M("fs_ring", new Color(0.85f, 0.95f, 1f), 1.5f, 0.55f); rip.NoiseAmt = 0.35f; rip.Soft = 0.001f; rip.Scroll = new Vector2(0.8f, 0f);
            Vol2Fs.MeshObject(root, "Ripples", Kit.Mat(rip), out _rippleMesh);
            Vol2Fs.MeshObject(root, "Crests", _sheet, out _crestMesh);
            Vol2Fs.MeshObject(root, "Waves", _sheet, out _waveMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.75f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Puddles", Kit.Mat(wet), out _wetMesh);
            _light = Vol2Fs.PointLight(root, "WaterLight", Blue, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            _light.transform.position = OnBand(0f, R) + Vector3.up * 1.0f;
        }

        protected override void OnHit(long nowUs)
        {
            for (int k = 0; k < 12; k++) { float ang = Mathf.Lerp(-0.9f, 0.9f, k / 11f); if (Vol2FsB4R.Front55(ang) > 0.25f) { Vol2FsB1.Emit(_spray, OnBand(ang, R), 2); Vol2FsB1.Emit(_drops, OnBand(ang, R), 6); } }
        }

        protected override void OnStruck(Struck s)
        {
            if (_first != null) return;
            _first = s;
            Vector3 c = CentreNow(s);
            Vol2FsB1.Emit(_spray, c + Vector3.up * 0.3f, 60); Vol2FsB1.Emit(_drops, c, 160); Vol2FsB1.Emit(_foam, G(FeetOf(s)), 20);
        }

        protected override void OnLand(Hop h)
        {
            Vector3 c = CentreNow(h); float k = ShareScale(h.Share);
            Vol2FsB1.Emit(_spray, c + Vector3.up * 0.2f, Mathf.RoundToInt(70 * k)); Vol2FsB1.Emit(_drops, c, Mathf.RoundToInt(180 * k)); Vol2FsB1.Emit(_foam, G(FeetOf(h)), 24);
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            if (!Natural(m)) { Vol2FsB1.Emit(_spray, m.Centre + Vector3.up * 0.4f, 20); Vol2FsB1.Emit(_foam, m.Centre, 12); }
        }

        /// <summary>An arc of a ripple crest round <paramref name="c"/> at radius r, spanning +-half radians about the direction <paramref name="dir"/>: a low standing sheet.</summary>
        void CrestArc(Vector3 c, Vector3 dir, float r, float half, float height, float life, float rnd, Vector3 cam)
        {
            if (life <= 0.01f || r < 0.05f) return;
            const int n = 12;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var side = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.Lerp(-half, half, k / (float)(n - 1));
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * dir;
                pts[k] = G(c + d * r) + Vector3.up * (height * 0.5f + 0.02f);
                w[k] = height * (0.4f + 0.6f * Mathf.Sin(k / (float)(n - 1) * Mathf.PI));
                al[k] = life * Mathf.Sin(k / (float)(n - 1) * Mathf.PI);
                side[k] = Vector3.up;
            }
            _crest.Strip(pts, w, side, Color.white, al, rnd, cam, 0f, 1f);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _band.Clear(); _ripple.Clear(); _crest.Clear(); _wet.Clear(); _wave.Clear();
            // the still water band, rain-pocked (drops bouncing), its own rings; it soaks away after the emitter's end
            float life = HostLife(t, 0.2f, 0.7f), warn = HostWarn(t);
            if (life > 0.01f)
            {
                Vol2FsB2.GroundRing(_band, Apex, F, Ground, R - 0.22f, R + 0.22f, 72, a => Vol2FsB4R.Front55(a) * life * 0.8f, 0.21f, cam, 0.025f, 0f, 5f);      // r2: a narrow band on the front only
                Vol2FsB2.GroundRing(_ripple, Apex, F, Ground, R - 0.04f, R + 0.04f, 72, a => Vol2FsB4R.Front55(a) * life * 0.6f, 0.33f, cam, 0.035f, 0f, 9f);
                if (Time.frameCount % (warn > 0.01f ? 3 : 1) == 0) { float ang = Random.Range(-0.95f, 0.95f); if (Vol2FsB4R.Front55(ang) > 0.25f) Vol2FsB1.Emit(_rain, OnBand(ang, Random.Range(Inner, Outer)) + Vector3.up * 0.03f, 2); }
            }
            _light.intensity = 2.5f * life * (1f - 0.5f * warn);
            // the drenched body on the band drips (the wet mirror, 4 s - the status sink owns it)
            if (_first != null && t - _first.At < 4f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drip, CentreNow(_first) + Random.insideUnitSphere * 0.4f, 1);
            // the ripples: three crests running out from the struck body toward each neighbour, rising as they go, breaking over it
            foreach (var hop in Hops)
            {
                float g = HopK(hop, t), k = ShareScale(hop.Share);
                Vector3 c = G(hop.FromP), b = G(FeetOf(hop)); Vector3 dir = FlatN(b - c); float dist = Vector3.Distance(c, b);
                float since = t - hop.Start;
                if (since >= 0f && since < hop.Travel + 0.6f)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        float rr = (g - r * 0.18f) * dist; if (rr <= 0.1f) continue;
                        float fade = 1f - Vol2Fs.Smooth(hop.Travel, hop.Travel + 0.5f, since + r * 0.1f);
                        float height = (0.18f + 0.6f * Mathf.Clamp01(rr / dist)) * k;      // r2: the crests are the hero          // un_a: wide crests rising as they near the neighbour
                        CrestArc(c, dir, rr, 0.85f - 0.12f * r, height, fade * (1f - 0.25f * r), 0.17f + r * 0.23f + hop.Order * 0.1f, cam);
                        if (r == 0 && g < 1f && Time.frameCount % 2 == 0) { float a2 = Random.Range(-0.6f, 0.6f); Vol2FsB1.Emit(_foam, G(c + Quaternion.AngleAxis(a2 * Mathf.Rad2Deg, Vector3.up) * dir * rr), 1); }
                        // the foot of each crest: a bright ring line on the ground
                        Vol2FsB2.GroundRing(_ripple, c, dir, Ground, rr - 0.05f, rr + 0.05f, 24, a => Mathf.Abs(a) < 0.6f ? fade * (1f - Mathf.Abs(a) / 0.6f) : 0f, 0.5f + r * 0.1f, cam, 0.03f, 0f, 2f);
                        if (r == 0 && g < 1f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drops, c + dir * rr + Vector3.up * height, 2);
                    }
                }
                // the break over the neighbour: a curling sheet of water standing up and falling over it
                float bk = t - hop.Start - hop.Travel;
                if (bk > -0.05f && bk < 0.8f)
                {
                    float rise = Vol2Fs.Smooth(-0.05f, 0.15f, bk), fall = Vol2Fs.Smooth(0.25f, 0.8f, bk);
                    float H = (1.6f * rise * (1f - 0.7f * fall)) * k;
                    Vector3 sd = Vector3.Cross(Vector3.up, dir); Vector3 baseP = b - dir * 0.5f;
                    var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                    for (int j = 0; j < 8; j++)
                    {
                        float u = j / 7f;
                        pts[j] = baseP + Vector3.up * H * u + dir * (0.9f * u * u * (0.4f + fall)) ;
                        w[j] = (1.6f * (1f - 0.4f * u)) * k; al[j] = (1f - fall) * (1f - 0.4f * u);
                    }
                    _wave.Strip(pts, w, null, Color.white, al, 0.61f + hop.Order * 0.2f, cam, 0f, 1f);
                    if (bk > 0.2f && bk < 0.5f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_spray, b + Vector3.up * 1f, 3);
                }
            }
            // the marks: a rippling puddle, the body dripping; warning = the ripples die down; natural expiry = the water runs down off the body and soaks away
            foreach (var m in MarkList)
            {
                float ml = MarkLife(m, t, 0.25f), mw = MarkWarn(m, t), ex = MarkExit(m, t), age = MarkAge(m, t);
                if (age < 0f) continue;
                float soak = m.EndAt < 0f ? 0f : Natural(m) ? Vol2Fs.Smooth(0.3f, 1f, ex) : ex;
                float pl = Vol2Fs.Smooth(0f, 0.25f, age) * (1f - soak);
                float sz = 2.4f * ShareScale(m.Share) * (1f + 0.15f * Vol2Fs.Smooth(0f, 2f, age));
                if (pl > 0.01f) { _wet.Quad(m.Centre + Vector3.up * 0.018f, Right, Fwd, sz, new Color(1f, 1f, 1f, pl), 0.23f + m.Id % 5 * 0.1f); _band.Quad(m.Centre + Vector3.up * 0.022f, Right, Fwd, sz * 0.7f, new Color(1f, 1f, 1f, pl * 0.8f), 0.41f); }
                // slow ripple rings in the puddle
                if (pl > 0.05f)
                    for (int r = 0; r < 2; r++)
                    {
                        float ph = Mathf.Repeat(age * (1f - 0.6f * mw) * 0.9f + r * 0.5f, 1f);
                        float rad = 0.2f + ph * sz * 0.35f, a = pl * (1f - ph) * (1f - 0.7f * mw);
                        Vol2FsB2.GroundRing(_ripple, m.Centre, F, Ground, rad - 0.03f, rad + 0.03f, 28, x => a, 0.7f + r * 0.13f, cam, 0.03f, 0f, 3f);
                    }
                Vector3 c = m.Hop != null ? CentreNow(m.Hop) : m.Centre + Vector3.up * 0.6f;
                bool running = m.EndAt >= 0f && Natural(m) && ex < 0.8f;
                if ((m.EndAt < 0f && Time.frameCount % 3 == 0) || (running && Time.frameCount % 1 == 0)) Vol2FsB1.Emit(_drip, c + Random.insideUnitSphere * 0.45f, running ? 3 : 1);
            }
            _band.Write(_bandMesh); _ripple.Write(_rippleMesh); _crest.Write(_crestMesh); _wet.Write(_wetMesh); _wave.Write(_waveMesh);
        }
    }
}
