using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 3 (2026-10-26, the user: "남긴다 19자(묶음 3)를 쇼케이스 방식으로 시작해줘."): the ㄱ book 남긴다 - P1 낙 막 삭 악 (각 = batch 0),
    // A1 넉 먹 석 억 (걱 = the defence kit's recovery: Vol2FsGeok), F1 곡 녹 목 속 옥, R1 국 눅 묵 숙 욱.
    // ONE skeleton for the four frames: charge -> launch (the emitter's StartUs) -> the strike (path: the flight to the predicted first body on the vol.2 head's clock;
    // fan / ring / attachment: at the launch) -> the STANDING RESIDUE, which lives exactly as long as the executor's material (Vol2ResidueExecutor: created / drawn /
    // extended / moved / ended, its live ExpiresUs) -> the expiry warning (the template's last 0.4 s before ExpiresUs) -> the exit of ITS end reason (natural expiry,
    // consumed by touches or a reaction, replaced, cleansed, cancelled). No timer here decides how long anything lives: the look only reads those facts on the scope
    // clock and plays its own tail after the end event. Presentation only: nothing here is read by a rule, nothing decides or delays anything.

    /// <summary>The 남긴다 skeleton of batch 3 (every frame). A glyph fills the hooks; the helpers give it the residue's facts as numbers.</summary>
    public abstract class Vol2FsLeave : Vol2FsRun
    {
        protected Frame Shape; protected int Seed;
        protected bool Launched, IsHit; protected float LaunchAt = -1f, ImpactRel = -1f, HitAt = -1f, EndRel = -1f, LastFrac;
        // path: the shot to the predicted first body
        protected Vector3 From, HitPoint, AimPoint; protected bool HasTarget; protected long TargetId; protected Transform TargetT; protected Bounds TB;
        protected virtual float LobHeight => 0.45f;
        protected Vector3 Line(float s) => Vector3.Lerp(From, HitPoint, s) + Vector3.up * 4f * LobHeight * s * (1f - s);
        // fan / ring (the spec's numbers)
        protected Vector3 Apex, F; protected float Depth, Half, R, Inner, Outer;
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        protected Vector3 OnBand(float ang, float r) { var p = Apex + Dir(ang) * r; p.y = Ground; return p; }
        /// <summary>0 in front of the caster .. 1 straight behind (the ring's near arc stays low there so it does not wall in the shoulder camera).</summary>
        protected static float Back(float ang) => Vol2Fs.Smooth(0.55f, 0.9f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) / 180f);
        /// <summary>r2: 1 on the front arc (within 40 deg of the aim) .. 0 from 115 deg round: a ring's standing pieces are lower on the sides and gone behind - with the
        /// cast camera A pulled back inside the 2 m ring the side pieces loomed over the whole frame in r1.</summary>
        protected static float Front(float ang) => 1f - Vol2Fs.Smooth(0.17f, 0.56f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) / 180f);      // r3: 30 -> 100 deg
        protected static Vector3 FlatN(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        // the bodies the executor touched (the instant strike and the residue's own touches)
        protected sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public float At; public string Kind; public object Data; }
        protected readonly List<Struck> Hits = new List<Struck>();
        protected readonly List<Struck> Touches = new List<Struck>();

        // the residue (the executor's material: the truth of its life)
        protected Vol2Material Res; protected float ResAt = -1f, ResEndAt = -1f; protected Vol2MaterialEnd ResWhy = Vol2MaterialEnd.None;
        protected Vector3 ResCentre; protected float ResRadius;
        protected float DrawnAt = -1f, ExtendedAt = -1f, ChangedAt = -1f; protected int Draws;
        protected long NowUs;

        /// <summary>What is left of the residue's pool (1 when made, drops with every draw; 0 = consumed).</summary>
        protected float Remain01 => Res == null || Res.Initial <= 0m ? 1f : Mathf.Clamp01((float)(Res.Remaining / Res.Initial));
        /// <summary>Seconds since the residue stands (its creation, never before the strike landed); -1 = not yet.</summary>
        protected float ResAge(float t) => ResAt < 0f || !IsHit ? -1f : t - Mathf.Max(ResAt, HitAt);
        /// <summary>0..1 over the template's warning window before the residue's CURRENT expiry (국 may stretch it); 0 when it is gone or not there.</summary>
        protected float Warn(long nowUs)
        {
            if (Res == null || ResEndAt >= 0f) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            float e = Inst.Rel(Res.ExpiresUs);
            return Vol2Fs.Smooth(e - w, e, Now(nowUs));
        }
        /// <summary>The residue's footprint scale now (곡 grows linear sqrt 1.5 over its life; 1 for the others).</summary>
        protected float FootScale(long nowUs) => Res != null && Res.Footprint != null ? Res.Footprint.ScaleAt(nowUs) : 1f;
        /// <summary>The look's own tail after the residue's end event, by its reason.</summary>
        protected float ExitSeconds
        {
            get
            {
                switch (ResWhy)
                {
                    case Vol2MaterialEnd.Expired: return ExpireSeconds;
                    case Vol2MaterialEnd.Consumed: return 0.7f;
                    case Vol2MaterialEnd.ChildConsumed: return 0.5f;
                    case Vol2MaterialEnd.Replaced: return 0.3f;
                    case Vol2MaterialEnd.HitCancelled: return 0.25f;
                    case Vol2MaterialEnd.Cleansed: return 0.8f;
                    default: return 0.3f;
                }
            }
        }
        protected virtual float ExpireSeconds => 1.2f;
        /// <summary>0 while the residue stands, 0..1 over its exit tail after the end event.</summary>
        protected float ExitK(float t) => ResEndAt < 0f ? 0f : Mathf.Clamp01((t - ResEndAt) / Mathf.Max(0.05f, ExitSeconds));
        protected bool Exiting => ResEndAt >= 0f;
        protected bool NaturalEnd => ResWhy == Vol2MaterialEnd.Expired;
        /// <summary>A short swell after each draw (a body touched the residue): 1 at the draw, 0 after 0.45 s.</summary>
        protected float DrawPulse(float t) => DrawnAt < 0f ? 0f : Mathf.Clamp01(1f - (t - DrawnAt) / 0.45f);

        // ---- hooks ----
        protected abstract void OnBegin();
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>Path only: in flight, f = 0..1 of the flight now.</summary>
        protected virtual void OnFly(float t, float f, float prev) { }
        /// <summary>The strike lands (path: at the impact clock; fan / ring / attachment: at the launch).</summary>
        protected abstract void OnHit(long nowUs);
        protected virtual void OnStruck(Struck s) { }
        /// <summary>A body drew from the residue (a "Residue" contact - a walk-in, a swing through it).</summary>
        protected virtual void OnTouch(Struck s) { }
        protected virtual void OnResidue() { }
        protected virtual void OnChanged(string what) { }
        protected virtual void OnResidueEnd() { }
        /// <summary>Every frame after the strike: h = seconds since it.</summary>
        protected abstract void OnLive(float t, float h);

        public override void Began(long nowUs)
        {
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            Shape = Inst.Log.Glyph.Frame;
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = FlatN(spec.Forward); if (F == Vector3.forward && spec.Forward.sqrMagnitude < 1e-6f) F = Fwd;
            Depth = Mathf.Max(1f, spec.FanDepth); Half = Mathf.Max(0.1f, spec.FanHalfAngleRad);
            R = Mathf.Max(0.5f, spec.RingRadius); Inner = spec.RingInner; Outer = Mathf.Max(spec.RingInner, spec.RingOuter);
            NowUs = nowUs;
            OnBegin();
        }

        void Launch(long nowUs)
        {
            Launched = true; LaunchAt = Now(nowUs);
            if (Shape == Frame.Path)
            {
                var cue = Vol2LegacyPathRecipe.SingleCue(Inst, new Vol2LegacyCue { Letter = Inst.Log.Glyph.Letter, Body = Inst.Log.Glyph.Body, Origin = Inst.Log.Spec.Origin, Forward = Inst.Log.Spec.Forward, PathLength = Inst.Log.Spec.PathLength }, nowUs);
                From = Tip();
                ImpactRel = Now(nowUs) + Mathf.Max(0.02f, cue.ImpactClock);
                HasTarget = cue.HasTarget; TargetId = cue.TargetId;
                if (HasTarget)
                {
                    TargetT = TargetTransform(TargetId); Watch(TargetT);
                    var tg = Target(TargetId);
                    TB = BodyBounds(TargetT, cue.TargetPoint, tg != null ? tg.Radius : 0.5f);
                    AimPoint = TB.center + Vector3.up * TB.extents.y * 0.15f;
                    Vector3 toward = From - AimPoint; toward.y = 0f;
                    HitPoint = AimPoint + toward.normalized * Mathf.Min(TB.extents.x, TB.extents.z) * 0.6f;
                }
                else { AimPoint = cue.FallbackPoint + Vector3.up * 1.0f; HitPoint = AimPoint; TB = new Bounds(cue.FallbackPoint + Vector3.up * 0.6f, Vector3.one * 1.2f); }
                Note = "launch target=" + TargetId + " impact=" + cue.ImpactClock.ToString("0.000") + "s";
            }
            OnLaunch(nowUs);
            if (Shape != Frame.Path) { IsHit = true; HitAt = LaunchAt; OnHit(nowUs); }
        }

        protected float Frac(long nowUs) => ImpactRel > LaunchRel ? Mathf.Clamp01((Now(nowUs) - LaunchRel) / (ImpactRel - LaunchRel)) : 1f;
        protected Vector3 BodyNow() => TargetT != null ? BodyBounds(TargetT, TargetT.position, 0.5f).center : TB.center;
        protected Vector3 FeetNow() => TargetT != null ? TargetT.position : new Vector3(TB.center.x, Ground, TB.center.z);
        protected Vector3 CentreNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f).center : s.B.center;
        protected Vector3 FeetOf(Struck s) => s.T != null ? s.T.position : s.Feet;
        /// <summary>A point on the brush shaft: 0 = the grip (sleeve), 1 = the tip.</summary>
        protected Vector3 Shaft(float u) => Vector3.LerpUnclamped(Grip(), Tip(), u);
        protected Vector3 ShaftAxis { get { var d = Tip() - Grip(); return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up; } }

        public override void Contact(Vol2ContactRecord rec)
        {
            if (!rec.BudgetAccepted || Root == null) return;
            var tg = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (tg == null) return;
            var s = new Struck { Id = rec.TargetId, T = tr, Feet = tg.Position, B = BodyBounds(tr, tg.Position, tg.Radius), At = Inst.Rel(rec.NowUs), Kind = rec.Kind ?? "" };
            Watch(tr);
            if (s.Kind == "Residue") { Touches.Add(s); OnTouch(s); return; }
            if (Hits.Exists(x => x.Id == rec.TargetId)) return;
            Hits.Add(s);
            OnStruck(s);
        }

        public override void MaterialCreated(Vol2Material m)
        {
            if (Res != null || m == null || m.Kind != Vol2MaterialKind.Residue || m.Cast != Inst.Cast) return;
            Res = m; ResAt = Inst.Rel(m.CreatedUs);
            var fp = m.Footprint;
            ResCentre = fp != null ? (fp.Kind == Vol2FootprintKind.Fan ? fp.Origin : fp.Origin) : Inst.Origin;
            ResRadius = fp == null ? 0.6f : fp.Kind == Vol2FootprintKind.Disc ? Mathf.Max(0.3f, fp.Radius) : fp.Kind == Vol2FootprintKind.Ring ? fp.RingOuter : fp.Kind == Vol2FootprintKind.Fan ? fp.FanDepth : 0.6f;
            if (fp != null && fp.Kind != Vol2FootprintKind.Attached) ResCentre.y = Ground;
            Note += " | residue " + (fp != null ? fp.Kind.ToString() : "?") + " at " + ResAt.ToString("0.00") + " life " + (m.LifeUs / 1e6).ToString("0.0") + "s";
            try { OnResidue(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        public override void MaterialChanged(Vol2Material m, string what)
        {
            if (m == null || m != Res) return;
            var co = Inst.Model.Coordinator;
            float t = Inst.Rel(co != null ? co.Services.Clock.NowUs : NowUs);
            ChangedAt = t;
            if (what == "drawn") { DrawnAt = t; Draws++; }
            else if (what == "extended") ExtendedAt = t;
            else if (what == "moved" && m.Footprint != null) { ResCentre = m.Footprint.Origin; ResCentre.y = Ground; }
            Note += " | " + what + " at " + t.ToString("0.00");
            OnChanged(what);
        }

        public override void MaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            if (m == null || m != Res || ResEndAt >= 0f) return;
            ResEndAt = Inst.Rel(m.EndedUs); ResWhy = why;
            Note += " | residue ended " + why + " at " + ResEndAt.ToString("0.00");
            OnResidueEnd();
        }

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            NowUs = nowUs;
            float t = Now(nowUs);
            if (!Launched)
            {
                float k = LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f;
                OnCharge(t, k);
                if (t >= LaunchRel - 1e-4f) Launch(nowUs); else return;
            }
            if (!IsHit)
            {
                float f = Frac(nowUs);
                OnFly(t, f, LastFrac);
                LastFrac = f;
                if (t >= ImpactRel) { IsHit = true; HitAt = t; OnHit(nowUs); }
                else return;
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) EndRel = t;
            OnLive(t, t - HitAt);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            if (Res != null)
            {
                if (ResEndAt < 0f) return false;                                   // the residue still stands: its end is the executor's
                float by = ResEndAt + ExitSeconds;
                return t > by + 0.3f && (!ParticlesAlive() || t > by + 2.5f);
            }
            // no residue arrived (refused by the budget, or the cast ended without one): the strike's own look, then go
            if (EndRel < 0f) return false;
            float strikeEnd = Mathf.Max(EndRel, HitAt >= 0f ? HitAt + 1.2f : EndRel);
            return t > strikeEnd + 0.6f && (!ParticlesAlive() || t > strikeEnd + 3f);
        }
    }

    /// <summary>The ㅓ + 남긴다 skeleton (A1 넉 먹 석 억): the residue is ON THE CASTER (an Attached material, 6 s): the element dresses the brush and the sleeve from
    /// the launch for exactly the material's life; every swing through a body (the executor's "Instant" / "Residue" contacts) is answered at that body. The brush
    /// tip's recent path is kept so a look can draw its swing trail (real motion only: the game's / capture's brush, nothing simulated here).</summary>
    public abstract class Vol2FsWear : Vol2FsLeave
    {
        protected readonly List<(Vector3 P, float T)> TipPath = new List<(Vector3, float)>(64);
        /// <summary>0..1: the dress standing (up in 0.18 s from the strike, held while the material lives; falls with the look's exit).</summary>
        protected float Worn(float t) => !IsHit ? 0f : Vol2Fs.Smooth(HitAt, HitAt + 0.18f, t) * (1f - ExitK(t)) * (Res == null && EndRel >= 0f ? 1f - Vol2Fs.Smooth(EndRel, EndRel + 0.3f, t) : 1f);
        /// <summary>The caster's sleeve (the grip end of the brush, pulled a little toward the shoulder).</summary>
        protected Vector3 Sleeve() => Vector3.Lerp(Grip(), Inst.Origin + Vector3.up * 1.4f + Right * 0.22f, 0.35f);

        public override void Tick(long nowUs)
        {
            if (Root != null)
            {
                float t = Now(nowUs);
                Vector3 tip = Tip();
                if (TipPath.Count == 0 || (TipPath[TipPath.Count - 1].P - tip).sqrMagnitude > 1e-6f) TipPath.Add((tip, t));
                while (TipPath.Count > 0 && t - TipPath[0].T > 0.35f) TipPath.RemoveAt(0);
            }
            base.Tick(nowUs);
        }

        /// <summary>How fast the tip moved over the last 0.1 s (m/s) - a swing reads as a trail only while the brush really moves.</summary>
        protected float TipSpeed(float t)
        {
            if (TipPath.Count < 2) return 0f;
            var a = TipPath[TipPath.Count - 1]; int i = TipPath.Count - 2;
            while (i > 0 && a.T - TipPath[i].T < 0.1f) i--;
            float dt = Mathf.Max(1e-3f, a.T - TipPath[i].T);
            return (a.P - TipPath[i].P).magnitude / dt;
        }

        /// <summary>A ribbon through the tip's path of the last <paramref name="span"/> s (width fading to the tail) - the swing's arc of the element.</summary>
        protected void SwingTrail(Vol2FsStrips s, float t, float span, float width, float life, float rnd, float lag = 0f)
        {
            if (life <= 0.01f || TipPath.Count < 3) return;
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
            foreach (var p in TipPath)
            {
                float age = t - p.T - lag; if (age > span || age < 0f) continue;
                float u = 1f - age / span;
                pts.Add(p.P); w.Add(width * (0.25f + 0.75f * u)); al.Add(life * u);
            }
            if (pts.Count >= 3 && (pts[pts.Count - 1] - pts[0]).magnitude > 0.35f) s.Strip(pts, w, null, Color.white, al, rnd, CamPos, 0f, 1f);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            if (Res != null) { if (ResEndAt < 0f) return false; float by = ResEndAt + ExitSeconds; return t > by + 0.3f && (!ParticlesAlive() || t > by + 2.5f); }
            if (EndRel < 0f) return false;
            return t > EndRel + 0.6f && (!ParticlesAlive() || t > EndRel + 3f);
        }
    }

    /// <summary>Batch 3 helpers.</summary>
    public static class Vol2FsB3
    {
        public static Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsMat.Make(tex, tint, intensity, addMix);
        public static Vol2FsMat Fire(float intensity, float addMix) { var m = M("fs_flame", Color.white, intensity, addMix); m.Ramp = "fs_ramp_fire"; m.RampScale = 1.7f; m.NoiseAmt = 0.65f; m.Erode = 0.28f; return m; }
        public static Vol2FsMat Smoke(float opacity = 0.55f) { var s = M("fs_puff", new Color(0.08f, 0.06f, 0.05f, 1f), 1f, 0f); s.Opacity = opacity; s.NoiseAmt = 0.6f; s.Erode = 0.35f; return s; }
        public static Vol2FsMat Dust(Color c, float opacity = 0.45f) { var d = M("fs_puff", c, 1f, 0f); d.Opacity = opacity; d.NoiseAmt = 0.6f; d.Erode = 0.35f; return d; }
        public static Vol2FsMat Ash() { var a = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); a.NoiseAmt = 0.3f; a.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); a.EdgeWidth = 0.25f; a.Soft = 0.05f; return a; }

        /// <summary>A hexagonal stone column (basalt), radius 0.5, height 1, pivot at the middle of its bottom face; its top slightly tilted and chipped by the seed.</summary>
        public static Mesh Hex(int seed)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            var bot = new Vector3[6]; var top = new Vector3[6];
            float tx = (Vol2Fs.Hash(seed, 1) - 0.5f) * 0.18f, tz = (Vol2Fs.Hash(seed, 2) - 0.5f) * 0.18f;
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f; float r = 0.5f * (0.92f + 0.12f * Vol2Fs.Hash(seed, k + 10));
                bot[k] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                top[k] = new Vector3(bot[k].x * 0.97f, 1f + bot[k].x * tx + bot[k].z * tz - 0.06f * Vol2Fs.Hash(seed, k + 20), bot[k].z * 0.97f);
            }
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc) { int s = v.Count; v.Add(a); v.Add(b); v.Add(c); uv.Add(ua); uv.Add(ub); uv.Add(uc); idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); }
            for (int k = 0; k < 6; k++)
            {
                int n = (k + 1) % 6; float u0 = k / 6f, u1 = (k + 1) / 6f;
                Tri(bot[k], top[k], top[n], new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1));
                Tri(bot[k], top[n], bot[n], new Vector2(u0, 0), new Vector2(u1, 1), new Vector2(u1, 0));
                Vector3 ct = new Vector3(0f, (top[0].y + top[3].y) * 0.5f, 0f);
                Tri(top[k], ct, top[n], new Vector2(top[k].x + 0.5f, top[k].z + 0.5f), new Vector2(0.5f, 0.5f), new Vector2(top[n].x + 0.5f, top[n].z + 0.5f));
            }
            var m = new Mesh { name = "FsHex" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A disc of points (sunflower spiral) of radius r round c on the ground.</summary>
        public static Vector3 Spiral(Vector3 c, float r, int k, int n, float ground)
        {
            float a = k * 2.39996323f, rr = r * Mathf.Sqrt((k + 0.5f) / n);
            return new Vector3(c.x + Mathf.Cos(a) * rr, ground, c.z + Mathf.Sin(a) * rr);
        }

        /// <summary>A point inside the fan (apex, forward, depth, half angle) at radial u (0..1) and angle v (-1..1).</summary>
        public static Vector3 InFan(Vector3 apex, Vector3 fwd, float depth, float half, float u, float v, float ground)
        {
            Vector3 d = Quaternion.AngleAxis(v * half * Mathf.Rad2Deg, Vector3.up) * fwd;
            var p = apex + d * depth * u; p.y = ground; return p;
        }

        /// <summary>A transform's lossy follow: position a renderer root at the offset of its anchor (used for the attachment dress that rides the brush).</summary>
        public static void Place(Transform t, Vector3 p, Quaternion r) { if (t != null) t.SetPositionAndRotation(p, r); }

        /// <summary>A rising quad strip ribbon (a flame tongue / water sheet / smoke plume) from b along dir with sway.</summary>
        public static void Plume(Vol2FsStrips s, Vector3 b, Vector3 up, Vector3 side, float h, float w, float sway, float phase, float life, float rnd, Vector3 cam, int n = 8, float taper = 0.6f)
        {
            if (life <= 0.01f || h <= 0.01f) return;
            var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);
                pts[k] = b + up * h * u + side * Mathf.Sin(u * 3.1f + phase) * sway * u;
                wd[k] = w * (0.45f + 0.75f * Mathf.Sin(Mathf.Min(1f, u * 1.3f + 0.12f) * Mathf.PI)) * (1f - taper * u);
                al[k] = life * (1f - 0.45f * u);
            }
            s.Strip(pts, wd, null, Color.white, al, rnd, cam, 0f, 1f);
        }
    }
}
