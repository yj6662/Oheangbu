using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 5 (2026-10-27, the user's order of 10-26: "...그다음 글자별 폴리싱, 그다음 묶음 5로 가자."): the ㅁ book 심는다 -
    // P3 감 남 맘 삼 암 · A3 검 넘 멈 섬 엄 · F3 곰 놈 몸 솜 옴 · R3 굼 눔 뭄 숨 움 (peaks 맘 멈 몸 뭄 = earth).
    // ONE skeleton for the four frames, read only from Vol2SeedExecutor's facts on the scope clock:
    //   charge -> launch (the emitter's StartUs) -> path: the flight to the predicted first body (SingleCue), the "Plant" contacts put a seed on each body the head
    //   passes (the first three; a fourth is refused TargetCap and drawn as a seed that fizzles); attachment / fan / ring: the seed material is made at the start
    //   -> the WAIT: each seed is the executor's Mark material (tag seed / giant) from its creation to its end; its LIVE ExpiresUs is read every tick (信: while a letter
    //   is drawn the executor holds the waiting seeds' expiry - the look's countdown holds with it, and Held01 is the hub's own IsDrawing), the template's last
    //   expiryWarnSeconds before it is the warning
    //   -> the TRIGGER: the material ends Consumed (the executor ends it just before it notifies the "Activation" contacts on the same tick): OnFire, then one
    //   OnStrike per Activation contact (the struck bodies; 뭄's contact has no body - the caster stepped on the band; an ㅓ seed may work with nobody within 2 m)
    //   -> the other exits by the end reason: Expired (the body's own end clause), HitCancelled (the host died), Replaced (몸: a newer giant), Aborted
    //   -> the AFTER material of the peaks: 몸's rubble fan (Residue giant-rubble, 10 s) and 뭄's hardened ground (Residue hardground, 8 s), from its creation to its end.
    // Nothing here decides a target, a trigger or a lifetime. Presentation only: nothing here is read by a rule.

    /// <summary>The 심는다 skeleton of batch 5 (every frame). A glyph fills the hooks; the helpers give it the seeds' facts as numbers.</summary>
    public abstract class Vol2FsSeed : Vol2FsRun
    {
        protected Frame Shape; protected int Seed; protected char Letter; protected Body Element;
        protected bool Launched, IsHit; protected float LaunchAt = -1f, ImpactRel = -1f, HitAt = -1f, EndRel = -1f, LastFrac;
        // path: the shot to the predicted first body
        protected Vector3 From, HitPoint, AimPoint; protected bool HasTarget; protected long TargetId; protected Transform TargetT; protected Bounds TB;
        protected virtual float LobHeight => 0.45f;
        protected Vector3 Line(float s) => Vector3.Lerp(From, HitPoint, s) + Vector3.up * 4f * LobHeight * s * (1f - s);
        // fan / ring (the spec's numbers)
        protected Vector3 Apex, F; protected float Depth, Half, R, Inner, Outer;
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        protected Vector3 OnBand(float ang, float r) { var p = Apex + Dir(ang) * r; p.y = Ground; return p; }
        /// <summary>1 on the front arc .. 0 straight behind (the shoulder camera stands inside the ring: the short arc behind the caster stays low).</summary>
        protected static float Front(float ang) => 1f - Vol2Fs.Smooth(95f, 165f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)));
        protected static Vector3 FlatN(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        protected Vector3 G(Vector3 p) { p.y = Ground; return p; }
        protected long NowUs;
        /// <summary>The scope's seed hub (the executor's own facts and the 信 drawing state); null outside a scope.</summary>
        protected Vol2SeedHub Hub;

        // ---- a body the executor touched ----
        public sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public float At; public string Kind; public bool World; public object Data; }

        // ---- the seeds (the executor's Mark materials tagged seed / giant) ----
        public sealed class Pod
        {
            public Vol2Material M; public long Id; public int Index; public bool Giant;
            /// <summary>Where it waits: the host's feet (path), the caster (attachment), the fan's apex / the ring's centre (fan / ring), on the ground.</summary>
            public Vector3 Centre; public float Radius;
            /// <summary>Path: the body it was planted on (the "Plant" contact).</summary>
            public long HostId; public Transform HostT; public Bounds HostB; public Vector3 HostFeet;
            /// <summary>The material's creation (rel), when the look shows it planted, its end event, the trigger (Consumed) time.</summary>
            public float Born = -1f, PlantAt = -1f, EndAt = -1f, FiredAt = -1f;
            public Vol2MaterialEnd Why = Vol2MaterialEnd.None;
            public bool Started, Ended;
            public readonly List<Struck> Struck = new List<Struck>();
            /// <summary>The heartbeat phase (cycles), integrated each tick: faster as the seed runs out, frozen while 信 holds it.</summary>
            public float Phase;
            public object Data;
        }
        protected readonly List<Pod> Pods = new List<Pod>();
        readonly Dictionary<long, Pod> _podOf = new Dictionary<long, Pod>();
        /// <summary>Path: the bodies the head passed but the rules refused a seed (TargetCap: the fourth and later).</summary>
        protected readonly List<Struck> Refused = new List<Struck>();

        // ---- the after material of the peaks (몸's rubble, 뭄's ground) ----
        protected Vol2Material After; protected float AfterAt = -1f, AfterEndAt = -1f; protected Vol2MaterialEnd AfterWhy = Vol2MaterialEnd.None;
        protected Vector3 AfterCentre;

        // ---- the brush tip's recent path (attachment: the swing of the planting) ----
        protected readonly List<(Vector3 P, float T)> TipPath = new List<(Vector3, float)>(64);
        float _held, _lastT = -1f;

        // ---- hooks ----
        protected abstract void OnBegin();
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>Path only: in flight to the predicted first body, f = 0..1 of the flight.</summary>
        protected virtual void OnFly(float t, float f, float prev) { }
        /// <summary>The cast lands (path: at the impact clock on the first body; fan / ring / attachment: at the launch).</summary>
        protected abstract void OnHit(long nowUs);
        /// <summary>A seed is seen planted (at <see cref="Pod.PlantAt"/>).</summary>
        protected abstract void OnPlant(Pod p);
        /// <summary>A body the path passed was refused a seed (the effect-target cap).</summary>
        protected virtual void OnRefused(Struck s) { }
        /// <summary>The seed worked (its material ended Consumed): the burst at the seed. The struck bodies follow as <see cref="OnStrike"/>.</summary>
        protected abstract void OnFire(Pod p);
        /// <summary>One body the working seed struck (an "Activation" contact). 뭄: World = true, no body (the caster stepped onto the band).</summary>
        protected virtual void OnStrike(Pod p, Struck s) { }
        /// <summary>Any end of a seed, after <see cref="OnFire"/> when it worked (Expired / HitCancelled / Replaced / Aborted otherwise).</summary>
        protected virtual void OnPodEnd(Pod p) { }
        protected virtual void OnAfter() { }
        protected virtual void OnAfterEnd() { }
        /// <summary>Every frame after the landing: h = seconds since it.</summary>
        protected abstract void OnLive(float t, float h);

        public override void Began(long nowUs)
        {
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            Shape = Inst.Log.Glyph.Frame; Letter = Inst.Log.Glyph.Letter; Element = Inst.Log.Glyph.Body;
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = FlatN(spec.Forward); if (spec.Forward.sqrMagnitude < 1e-6f) F = Fwd;
            Depth = Mathf.Max(1f, spec.FanDepth); Half = Mathf.Max(0.1f, spec.FanHalfAngleRad);
            R = Mathf.Max(0.5f, spec.RingRadius); Inner = spec.RingInner; Outer = Mathf.Max(spec.RingInner, spec.RingOuter);
            NowUs = nowUs;
            Hub = Vol2SeedExecutor.HubOf(Inst.Model.Coordinator);
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
        protected Vector3 CentreNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f).center : s.B.center;
        protected Vector3 FeetOf(Struck s) => s.T != null ? s.T.position : s.Feet;
        /// <summary>The seed's host body now (path), its feet, its top (where a mark rides above the fur).</summary>
        protected Bounds HostNow(Pod p) => p.HostT != null ? BodyBounds(p.HostT, p.HostT.position, 0.5f) : p.HostB;
        protected Vector3 HostFeet(Pod p) => p.HostT != null ? p.HostT.position : p.HostFeet;
        protected Vector3 HostTop(Pod p) { var b = HostNow(p); return new Vector3(b.center.x, b.max.y, b.center.z); }
        /// <summary>The caster now (the hub's own position: the ㅓ seed rides it, the ㅓ radius and the 뭄 band are measured from it).</summary>
        protected Vector3 CasterNow() => Hub != null ? Hub.CasterPosition : Inst.Origin;
        /// <summary>A point on the brush shaft: 0 = the grip (sleeve), 1 = the tip.</summary>
        protected Vector3 Shaft(float u) => Vector3.LerpUnclamped(Grip(), Tip(), u);
        protected Vector3 ShaftAxis { get { var d = Tip() - Grip(); return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up; } }
        protected Vector3 Sleeve() => Vector3.Lerp(Grip(), CasterNow() + Vector3.up * 1.4f + Right * 0.22f, 0.35f);

        // ------------------------------------------------------------------------------------------------ the executor's facts

        public override void Contact(Vol2ContactRecord rec)
        {
            if (rec == null || Root == null) return;
            string kind = rec.Kind ?? "";
            float at = Inst.Rel(rec.NowUs);
            if (kind == "Plant")
            {
                var tg = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
                Watch(tr);
                if (!rec.BudgetAccepted)
                {
                    var r = new Struck { Id = rec.TargetId, T = tr, Feet = tg != null ? tg.Position : Apex + F * rec.Along, B = BodyBounds(tr, tg != null ? tg.Position : Apex + F * rec.Along, tg != null ? tg.Radius : 0.5f), At = at, Kind = "Refused" };
                    Refused.Add(r); Note += " | refused " + rec.TargetId + " " + rec.BudgetReason;
                    try { OnRefused(r); } catch (System.Exception e) { Debug.LogException(e); }
                    return;
                }
                if (!_podOf.TryGetValue(rec.MaterialId, out var p)) return;
                p.HostId = rec.TargetId; p.HostT = tr; p.HostFeet = tg != null ? tg.Position : p.Centre; p.HostB = BodyBounds(tr, p.HostFeet, tg != null ? tg.Radius : 0.5f);
                // the first body's seed is seen when the visual shot lands on it; the farther ones when the head passes them (their contact times)
                p.PlantAt = rec.TargetId == TargetId && ImpactRel > 0f ? Mathf.Max(at, ImpactRel) : Mathf.Max(at, ImpactRel > 0f ? ImpactRel : at);
                Note += " | plant " + rec.TargetId + " @" + at.ToString("0.00");
                return;
            }
            if (kind != "Activation" || !rec.BudgetAccepted) return;
            if (!_podOf.TryGetValue(rec.MaterialId, out var pod)) return;
            var target = Target(rec.TargetId); var t = TargetTransform(rec.TargetId);
            Watch(t);
            var s = target != null
                ? new Struck { Id = rec.TargetId, T = t, Feet = target.Position, B = BodyBounds(t, target.Position, target.Radius), At = at, Kind = kind }
                : new Struck { Id = rec.TargetId, World = true, Feet = G(CasterNow()), B = new Bounds(CasterNow() + Vector3.up * 0.9f, Vector3.one), At = at, Kind = kind };
            if (pod.FiredAt < 0f) Fire(pod, at);                 // (the executor ends the seed Consumed before it notifies the contact - this is only a guard)
            pod.Struck.Add(s);
            Note += " | strike " + (s.World ? "world" : rec.TargetId.ToString()) + " @" + at.ToString("0.00");
            try { OnStrike(pod, s); } catch (System.Exception e) { Debug.LogException(e); }
        }

        public override void MaterialCreated(Vol2Material m)
        {
            if (m == null || m.Cast != Inst.Cast || Root == null) return;
            float t = Inst.Rel(m.CreatedUs);
            if (m.Kind == Vol2MaterialKind.Mark && (m.Tag == Vol2SeedExecutor.TagSeed || m.Tag == Vol2SeedExecutor.TagGiant))
            {
                if (_podOf.ContainsKey(m.Id.Value)) return;
                var fp = m.Footprint;
                var p = new Pod { M = m, Id = m.Id.Value, Index = Pods.Count, Giant = m.Tag == Vol2SeedExecutor.TagGiant, Born = t };
                p.Centre = fp == null ? G(Apex) : fp.Kind == Vol2FootprintKind.Attached ? CasterNow() : G(fp.Origin);
                p.Radius = fp == null ? 0.6f : fp.Kind == Vol2FootprintKind.Disc ? Mathf.Max(0.3f, fp.Radius) : fp.Kind == Vol2FootprintKind.Ring ? fp.RingOuter : fp.Kind == Vol2FootprintKind.Fan ? fp.FanDepth : 0.6f;
                // fan / ring / attachment: seen from the launch (the shape is down then); path: from its Plant contact
                p.PlantAt = Shape == Frame.Path ? -1f : Mathf.Max(t, LaunchRel);
                Pods.Add(p); _podOf[p.Id] = p;
                Note += " | seed " + p.Id + (p.Giant ? " giant" : "") + " " + (fp != null ? fp.Kind.ToString() : "?") + " life " + (m.LifeUs / 1e6).ToString("0.0") + "s";
                return;
            }
            if (m.Kind == Vol2MaterialKind.Residue && (m.Tag == Vol2SeedExecutor.TagRubble || m.Tag == Vol2SeedExecutor.TagGround) && After == null)
            {
                After = m; AfterAt = t;
                var fp = m.Footprint;
                AfterCentre = fp != null ? G(fp.Origin) : G(Apex);
                Note += " | after " + m.Tag + " life " + (m.LifeUs / 1e6).ToString("0.0") + "s";
                try { OnAfter(); } catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        public override void MaterialChanged(Vol2Material m, string what)
        {
            if (m == null || !_podOf.TryGetValue(m.Id.Value, out var p)) return;
            if (what == "moved" && m.Footprint != null && m.Footprint.Kind != Vol2FootprintKind.Attached) p.Centre = G(m.Footprint.Origin);
        }

        public override void MaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            if (m == null || Root == null) return;
            if (m == After) { EndAfter(why, Inst.Rel(m.EndedUs)); return; }
            if (_podOf.TryGetValue(m.Id.Value, out var p)) EndPod(p, why, Inst.Rel(m.EndedUs));
        }

        void Fire(Pod p, float at)
        {
            if (p.FiredAt >= 0f) return;
            if (!p.Started) StartPod(p);
            p.FiredAt = at;
            Note += " | fired " + p.Id + " @" + at.ToString("0.00");
            HitStop(Mathf.Max(at, Now(NowUs)));
            try { OnFire(p); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void EndPod(Pod p, Vol2MaterialEnd why, float at)
        {
            if (p.Ended) return;
            if (!p.Started) StartPod(p);
            p.Ended = true; p.EndAt = at; p.Why = why;
            if (why == Vol2MaterialEnd.Consumed) Fire(p, at);
            Note += " | seed " + p.Id + " ended " + why + " at " + at.ToString("0.00");
            try { OnPodEnd(p); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void EndAfter(Vol2MaterialEnd why, float at)
        {
            if (AfterEndAt >= 0f) return;
            AfterEndAt = at; AfterWhy = why;
            Note += " | after ended " + why + " at " + at.ToString("0.00");
            try { OnAfterEnd(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void StartPod(Pod p)
        {
            if (p.Started) return;
            p.Started = true;
            if (p.PlantAt < 0f) p.PlantAt = Mathf.Max(p.Born, Now(NowUs));
            try { OnPlant(p); } catch (System.Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------------------------------------ the helpers a glyph draws with

        /// <summary>Seconds a seed has been seen planted (-1 = not yet).</summary>
        protected float PodAge(Pod p, float t) => !p.Started || t < p.PlantAt ? -1f : t - p.PlantAt;
        /// <summary>The seed's whole wait as the executor has it NOW (creation -> live expiry; 信 pushes the expiry out while a letter is drawn).</summary>
        protected float WaitSpan(Pod p) => p.M == null ? 8f : Mathf.Max(0.1f, Inst.Rel(p.M.ExpiresUs) - p.Born);
        /// <summary>1 at the planting .. 0 at the live expiry: the countdown (held while 信 holds the seed; frozen at its end value once it ended).</summary>
        protected float Remain01(Pod p, float t)
        {
            if (p.M == null) return 1f;
            float e = Inst.Rel(p.M.ExpiresUs), at = p.Ended ? p.EndAt : t;
            return Mathf.Clamp01((e - at) / Mathf.Max(0.1f, e - p.Born));
        }
        /// <summary>0..1 over the template's warning window before the seed's live expiry (the 7.6 - 8.0 s of an 8 s seed); 0 once it ended.</summary>
        protected float PodWarn(Pod p, float t)
        {
            if (p.Ended || p.M == null) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            float e = Inst.Rel(p.M.ExpiresUs);
            return Vol2Fs.Smooth(e - w, e, t);
        }
        /// <summary>The look's own tail after a seed's end event, by its reason (a worked seed: its burst; natural expiry: the body's own end clause).</summary>
        protected virtual float PodExitSeconds(Vol2MaterialEnd why)
        {
            switch (why)
            {
                case Vol2MaterialEnd.Expired: return ExpireSeconds;
                case Vol2MaterialEnd.Consumed: return FireSeconds;
                case Vol2MaterialEnd.HitCancelled: return 0.35f;
                case Vol2MaterialEnd.Replaced: return 0.6f;
                case Vol2MaterialEnd.Cleansed: return 0.7f;
                case Vol2MaterialEnd.Aborted: return 0.05f;
                default: return 0.3f;
            }
        }
        protected virtual float ExpireSeconds => 1.0f;
        protected virtual float FireSeconds => 1.4f;
        /// <summary>0 while the seed waits, 0..1 over its exit tail after the end event.</summary>
        protected float PodExit(Pod p, float t) => !p.Ended ? 0f : Mathf.Clamp01((t - p.EndAt) / Mathf.Max(0.05f, PodExitSeconds(p.Why)));
        /// <summary>The dormant seed standing: 0..1 grown in <paramref name="grow"/> s from its planting, and gone with its end (a worked seed is spent at once:
        /// <paramref name="spend"/> s; any other end over its exit tail).</summary>
        protected float PodLife(Pod p, float t, float grow = 0.25f, float spend = 0.08f)
        {
            float a = PodAge(p, t); if (a < 0f) return 0f;
            float g = Vol2Fs.Smooth(0f, grow, a);
            if (!p.Ended) return g;
            if (p.Why == Vol2MaterialEnd.Consumed) return g * (1f - Mathf.Clamp01((t - p.EndAt) / Mathf.Max(0.01f, spend)));
            return g * (1f - PodExit(p, t));
        }
        protected bool Worked(Pod p) => p.FiredAt >= 0f;
        protected bool Withered(Pod p) => p.Ended && p.Why == Vol2MaterialEnd.Expired;
        /// <summary>Seconds since the seed worked (-1 = not worked).</summary>
        protected float SinceFire(Pod p, float t) => p.FiredAt < 0f ? -1f : t - p.FiredAt;
        /// <summary>The heartbeat of a waiting seed: a short sharp beat (0..1) each cycle; the cycle quickens 0.8 -> 3.4 Hz as the wait runs out, holds while 信 holds it.</summary>
        protected static float Beat(Pod p) { float x = p.Phase - Mathf.Floor(p.Phase); return Mathf.Exp(-x * 9f) + 0.45f * Mathf.Exp(-Mathf.Abs(x - 0.22f) * 14f); }
        /// <summary>0..1: 信 is holding this cast's waiting seeds (the hub's IsDrawing, smoothed; LAB: seedhold=1).</summary>
        protected float Held01 => _held;
        /// <summary>Did the executor record this fact for the cast (Exempt / Press / Collapse / Ground / NoTarget ...)?</summary>
        protected bool HasFact(string kind)
        {
            if (Hub == null) return false;
            foreach (var r in Hub.RecordsOf(Inst.Cast)) if (r.Kind == kind) return true;
            return false;
        }

        /// <summary>The after material standing: 0..1 grown in <paramref name="grow"/> s, gone over <paramref name="tail"/> s after its end.</summary>
        protected float AfterLife(float t, float grow = 0.3f, float tail = 1.2f)
        {
            if (AfterAt < 0f) return 0f;
            float a = Vol2Fs.Smooth(AfterAt, AfterAt + grow, t);
            return AfterEndAt < 0f ? a : a * (1f - Mathf.Clamp01((t - AfterEndAt) / Mathf.Max(0.05f, tail)));
        }
        protected float AfterWarn(float t)
        {
            if (After == null || AfterEndAt >= 0f) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            float e = Inst.Rel(After.ExpiresUs);
            return Vol2Fs.Smooth(e - w, e, t);
        }
        protected float AfterExit(float t, float tail = 1.2f) => AfterEndAt < 0f ? 0f : Mathf.Clamp01((t - AfterEndAt) / Mathf.Max(0.05f, tail));

        /// <summary>How fast the tip moved over the last 0.1 s (m/s).</summary>
        protected float TipSpeed()
        {
            if (TipPath.Count < 2) return 0f;
            var a = TipPath[TipPath.Count - 1]; int i = TipPath.Count - 2;
            while (i > 0 && a.T - TipPath[i].T < 0.1f) i--;
            float dt = Mathf.Max(1e-3f, a.T - TipPath[i].T);
            return (a.P - TipPath[i].P).magnitude / dt;
        }

        // ------------------------------------------------------------------------------------------------ the clock

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            NowUs = nowUs;
            float t = Now(nowUs);
            float dt = _lastT < 0f ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f);      // a JUMP of the lab clock is not a heartbeat
            _lastT = t;
            {
                Vector3 tip = Tip();
                if (TipPath.Count == 0 || (TipPath[TipPath.Count - 1].P - tip).sqrMagnitude > 1e-6f) TipPath.Add((tip, t));
                while (TipPath.Count > 0 && t - TipPath[0].T > 0.4f) TipPath.RemoveAt(0);
            }
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
                if (t >= ImpactRel) { IsHit = true; HitAt = t; OnHit(nowUs); HitStop(t); }
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) EndRel = t;
            // 信: the hub's own drawing state while any seed of this cast still waits
            bool waiting = false; foreach (var p in Pods) if (p.Started && !p.Ended) { waiting = true; break; }
            bool hold = waiting && Hub != null && Hub.IsDrawing;
            _held = Mathf.MoveTowards(_held, hold ? 1f : 0f, dt * 6f);
            foreach (var p in Pods)
            {
                if (!p.Started && p.PlantAt >= 0f && t >= p.PlantAt && (Shape != Frame.Path || IsHit)) StartPod(p);
                // a seed that ended without this instance hearing it: the executor's end, read back
                if (!p.Ended && p.M != null && !p.M.Alive) EndPod(p, p.M.End, Inst.Rel(p.M.EndedUs));
                if (p.Started && !p.Ended) p.Phase += dt * Mathf.Lerp(0.8f, 3.4f, 1f - Remain01(p, t)) * (1f - _held);
                if (p.Giant || Shape != Frame.Path) continue;
                if (p.HostT == null && p.HostId != 0) p.HostT = TargetTransform(p.HostId);
            }
            if (After != null && AfterEndAt < 0f && !After.Alive) EndAfter(After.End, Inst.Rel(After.EndedUs));
            if (IsHit) OnLive(t, t - HitAt);
        }

        /// <summary>The look's own tail after the last end (seconds).</summary>
        protected virtual float Tail => 0.5f;

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            if (!IsHit) return false;
            if (EndRel < 0f) return false;
            float by = Mathf.Max(EndRel, HitAt + 0.8f);
            foreach (var p in Pods)
            {
                if (!p.Ended) return false;                                    // a seed still waits: its end is the executor's
                by = Mathf.Max(by, p.EndAt + PodExitSeconds(p.Why));
            }
            if (After != null) { if (AfterEndAt < 0f) return false; by = Mathf.Max(by, AfterEndAt + AfterExitSeconds); }
            return t > by + Tail && (!ParticlesAlive() || t > by + Tail + 2.5f);
        }
        protected virtual float AfterExitSeconds => 1.2f;
    }

    /// <summary>The skeleton's plain look (a placeholder until a glyph has its own): a dot of the body's accent pulsing on its heartbeat where the seed waits,
    /// a burning-down countdown ring, an ink-and-accent burst when it works, a flash on every struck body, a fizzle when it withers. Also proves the facts chain.</summary>
    public class Vol2FsSeedPlain : Vol2FsSeed
    {
        ParticleSystem _dot, _burst, _ink; Mesh _ringMesh; readonly Vol2FsStrips _ring = new Vol2FsStrips(); Light _light;
        protected override void OnBegin()
        {
            var root = Root.transform; var acc = Accent(Element);
            _dot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsMat.Make("fs_dot", acc, 2f, 1f)), Life0 = 0.12f, Life1 = 0.2f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.35f, Size1 = 0.5f, Max = 60 }, root, "Dot");
            _burst = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsMat.Make("fs_streak", acc, 2.5f, 1f)), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.12f, Drag = 1.5f, Gravity = 0.6f, Stretch = true, Max = 400 }, root, "Burst");
            var ink = Vol2FsMat.Make("fs_ink", new Color(0.03f, 0.025f, 0.02f, 1f), 1f, 0f); ink.Soft = 0.1f;
            _ink = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.15f, Size1 = 0.4f, Drag = 1.2f, Gravity = 0.8f, Tiles = 2, Max = 200 }, root, "Ink");
            foreach (var ps in new[] { _dot, _burst, _ink }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Countdown", Kit.Mat(Vol2FsMat.Make("fs_stroke2", acc, 1.6f, 0.6f)), out _ringMesh);
            _light = Vol2Fs.PointLight(root, "SeedLight", acc, 4f);
        }
        protected override void OnCharge(float t, float k) { }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }
        protected override void OnPlant(Pod p) { Vol2FsB1.Emit(_ink, Where(p), 12); }
        protected override void OnRefused(Struck s) { Vol2FsB1.Emit(_ink, CentreNow(s), 6); }
        protected override void OnFire(Pod p) { Vector3 at = Where(p); Vol2FsB1.Emit(_burst, at, 120); Vol2FsB1.Emit(_ink, at, 40); }
        protected override void OnStrike(Pod p, Struck s) { Vol2FsB1.Emit(_burst, CentreNow(s), 60); }
        protected override void OnPodEnd(Pod p) { if (!Worked(p)) Vol2FsB1.Emit(_ink, Where(p), 10); }
        Vector3 Where(Pod p) => Shape == Frame.Path && p.HostId != 0 ? HostTop(p) + Vector3.up * 0.25f : Shape == Frame.Attachment ? Sleeve() : p.Centre + Vector3.up * 0.3f + (Shape == Frame.Fan ? F * Depth * 0.5f : F * R);
        protected override void OnLive(float t, float h)
        {
            var cam = CamPos; _ring.Clear(); float glow = 0f;
            foreach (var p in Pods)
            {
                float life = PodLife(p, t); if (life <= 0.01f) continue;
                Vector3 at = Where(p);
                if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_dot, at, Vector3.zero, 0.25f + 0.35f * Beat(p) + 0.3f * PodWarn(p, t), 0.12f);
                float rr = Shape == Frame.Ring ? R : Shape == Frame.Fan ? 1.2f : 0.6f;
                Vector3 c = Shape == Frame.Ring ? Apex : Shape == Frame.Path ? G(HostFeet(p)) : Shape == Frame.Fan ? G(Apex + F * Depth * 0.5f) : G(CasterNow());
                Vol2FsB5.Countdown(_ring, c, Ground, rr, 0.08f, Remain01(p, t), life, 0.3f + p.Index * 0.1f, cam);
                glow = Mathf.Max(glow, life * (0.4f + 0.6f * Beat(p)));
                _light.transform.position = at;
            }
            _ring.Write(_ringMesh);
            _light.intensity = 1.2f * glow;
        }
    }

    /// <summary>Batch 5 helpers.</summary>
    public static class Vol2FsB5
    {
        /// <summary>A countdown arc on the ground round <paramref name="c"/>: the burning-down ring of a waiting seed (an incense fuse) - the arc left = remain01,
        /// its burning head a little brighter (alpha), drawn into a camera-facing-flat ground strip.</summary>
        public static void Countdown(Vol2FsStrips s, Vector3 c, float ground, float r, float w, float remain01, float life, float rnd, Vector3 cam, float a0 = 0f, int n = 40)
        {
            if (life <= 0.01f || remain01 <= 0.005f) return;
            int k = Mathf.Max(3, Mathf.CeilToInt(n * remain01));
            var pts = new List<Vector3>(k + 1); var wd = new List<float>(k + 1); var al = new List<float>(k + 1); var side = new List<Vector3>(k + 1);
            for (int i = 0; i <= k; i++)
            {
                float u = i / (float)k, ang = a0 + u * remain01 * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                pts.Add(new Vector3(c.x, ground + 0.03f, c.z) + d * r); side.Add(d); wd.Add(w * (u > 0.94f ? 1.5f : 1f)); al.Add(life * (u > 0.94f ? 1f : 0.85f));
            }
            s.Strip(pts, wd, side, Color.white, al, rnd, cam, 0f, remain01 * 4f);
        }

        /// <summary>A camera-facing countdown dial round <paramref name="c"/>: an arc from the top, clockwise, as long as <paramref name="remain01"/> of the circle,
        /// its burning head wider and stronger.</summary>
        public static void Dial(Vol2FsStrips s, Vector3 c, Vector3 cam, float r, float w, float remain01, float life, float rnd, int n = 36)
        {
            if (life <= 0.01f || remain01 <= 0.005f) return;
            Vector3 view = (cam - c).normalized, right = Vector3.Cross(Vector3.up, view).normalized;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            Vector3 up = Vector3.Cross(view, right);
            int k = Mathf.Max(3, Mathf.CeilToInt(n * remain01));
            var pts = new List<Vector3>(k + 1); var wd = new List<float>(k + 1); var al = new List<float>(k + 1); var side = new List<Vector3>(k + 1);
            for (int i = 0; i <= k; i++)
            {
                float u = i / (float)k, ang = u * remain01 * Mathf.PI * 2f;
                Vector3 d = up * Mathf.Cos(ang) + right * Mathf.Sin(ang);
                pts.Add(c + d * r); side.Add(d); wd.Add(w * (u > 0.9f ? 1.9f : 1f)); al.Add(life * (u > 0.9f ? 1f : 0.8f));
            }
            s.Strip(pts, wd, side, Color.white, al, rnd, cam, 0f, remain01 * 3f);
        }

        /// <summary>A small closed ring (a seal / halo) of radius r round c in the plane of <paramref name="normal"/>.</summary>
        public static void Halo(Vol2FsStrips s, Vector3 c, Vector3 normal, float r, float w, float life, float rnd, Vector3 cam, int n = 24, float spin = 0f)
        {
            if (life <= 0.01f) return;
            Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(normal, a);
            var pts = new List<Vector3>(n + 1); var wd = new List<float>(n + 1); var al = new List<float>(n + 1); var side = new List<Vector3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float ang = spin + i / (float)n * Mathf.PI * 2f;
                Vector3 d = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                pts.Add(c + d * r); side.Add(d); wd.Add(w); al.Add(life);
            }
            s.Strip(pts, wd, side, Color.white, al, rnd, cam, 0f, 3f);
        }

        /// <summary>A point inside the fan (apex, forward, depth, half angle) at radial u (0..1) and angle v (-1..1), on the ground.</summary>
        public static Vector3 InFan(Vector3 apex, Vector3 fwd, float depth, float half, float u, float v, float ground) => Vol2FsB3.InFan(apex, fwd, depth, half, u, v, ground);

        /// <summary>Seeds scattered over a fan (stable by seed): radial u in [u0, 1], angle -1..1.</summary>
        public static List<Vector2> FanScatter(int seed, int n, float u0 = 0.25f)
        {
            var l = new List<Vector2>(n);
            for (int k = 0; k < n; k++)
            {
                float u = Mathf.Lerp(u0, 0.98f, Mathf.Sqrt((k + Vol2Fs.Hash(seed, k)) / n)), v = (Vol2Fs.Hash(seed, k + 500) * 2f - 1f) * 0.92f;
                l.Add(new Vector2(u, v));
            }
            return l;
        }
    }
}
