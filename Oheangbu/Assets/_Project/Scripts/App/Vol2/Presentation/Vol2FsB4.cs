using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 4 (2026-10-26, the user: "번진다 20자(묶음 4)를 쇼케이스 방식으로 시작해줘."): the ㄴ book 번진다 -
    // P2 간 난 만 산 안 · A2 건 넌 먼 선 언 · F2 곤 논 몬 손 온 · R2 군 눈 문 순 운 (peaks 난 넌 논 눈).
    // ONE skeleton for the four frames, read only from Vol2SpreadExecutor's facts on the scope clock:
    //   charge -> launch (the emitter's StartUs) -> the strike (path: the flight to the predicted first body on the vol.2 head's clock; fan / ring: at the launch;
    //   attachment: the brush is dressed from the launch for the attachment host's life, every swing's touch is the executor's "Instant" / "Second" contact)
    //   -> the SPREAD: every "Spread" contact (and 논's "ReturnSpread") is one HOP from where the executor spread it from (path / attachment / ring: the body whose touch
    //   spread it; fan: the nearest point of the fan to the neighbour; 눈: the burning wood the body stands in) to that body, drawn in the executor's order; its share
    //   (the contact's paid amount = the mark's Initial) scales the hop and the mark: the visible dilution of a normal 번진다 (1 / n each) against the peaks' fixed 0.5
    //   -> the MARKS: each mark material (3 s, timing.n.life) stands from its creation to its end event (or its end read back), warns in the template's last
    //   expiryWarnSeconds, and leaves by its end reason -> 넌's carrier (the 6 s fire bundle on the caster, amount 0) from its creation to its end -> 눈: the casts'
    //   burned materials (Log.Burned "id:Consumed:wood|enemy", resolved through the registry) burn along their chain.
    // The hops are staggered in the executor's order by a few frames each (presentation of an order the executor already decided - all of a spread's contacts are
    // paid on one tick); nothing here decides a target, a share or a lifetime. Presentation only: nothing here is read by a rule.

    /// <summary>The 번진다 skeleton of batch 4 (every frame). A glyph fills the hooks; the helpers give it the hops / marks as numbers.</summary>
    public abstract class Vol2FsSpread : Vol2FsRun
    {
        protected Frame Shape; protected int Seed; protected char Letter;
        protected bool Launched, IsHit; protected float LaunchAt = -1f, ImpactRel = -1f, HitAt = -1f, EndRel = -1f, LastFrac;
        // path: the shot to the predicted first body
        protected Vector3 From, HitPoint, AimPoint; protected bool HasTarget; protected long TargetId; protected Transform TargetT; protected Bounds TB;
        protected virtual float LobHeight => 0.45f;
        protected Vector3 Line(float s) => Vector3.Lerp(From, HitPoint, s) + Vector3.up * 4f * LobHeight * s * (1f - s);
        // fan / ring (the spec's numbers)
        protected Vector3 Apex, F; protected float Depth, Half, R, Inner, Outer;
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        protected Vector3 OnBand(float ang, float r) { var p = Apex + Dir(ang) * r; p.y = Ground; return p; }
        /// <summary>1 on the front arc .. 0 from ~100 deg round (a ring's standing pieces stay low on the sides and are gone behind: the camera is inside the ring).</summary>
        protected static float Front(float ang) => 1f - Vol2Fs.Smooth(95f, 165f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)));      // 2026-10-27 polish: the whole ring but the arc behind the caster
        protected static Vector3 FlatN(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        protected Vector3 G(Vector3 p) { p.y = Ground; return p; }
        /// <summary>The attachment host's / timed ring's end (the emitter's EndUs, rel); -1 = none.</summary>
        protected float EmitterEnd = -1f;
        protected long NowUs;

        // ---- the bodies the executor touched directly (Instant / Second) ----
        public sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public float At; public string Kind; public object Data; }
        protected readonly List<Struck> Direct = new List<Struck>();
        Struck _lastDirect;

        // ---- the hops (one per Spread / ReturnSpread contact) ----
        public sealed class Hop
        {
            public long TargetId, SourceId, MaterialId; public Transform T; public Bounds B; public Vector3 Feet, FromP;
            /// <summary>The contact's time (rel), the visual start (in the executor's order), the visual travel.</summary>
            public float At, Start, Travel; public int Order; public float Share; public bool Back, Landed; public Mark Mark; public object Data;
        }
        protected readonly List<Hop> Hops = new List<Hop>();
        int _forwardOrder, _backOrder;
        /// <summary>The visual gap between two hops of one spread (seconds; the executor paid them on one tick).</summary>
        protected virtual float HopStagger => 0.09f;
        /// <summary>The hop's visual speed (m/s); the travel is clamped to [HopMin, HopMax].</summary>
        protected virtual float HopSpeed => 12f;
        protected virtual float HopMin => 0.14f;
        protected virtual float HopMax => 0.45f;

        // ---- the marks (the executor's Mark materials of this cast) ----
        public sealed class Mark
        {
            public Vol2Material M; public long Id; public Vector3 Centre; public float Radius, Born = -1f, EndAt = -1f, Share; public bool Back; public Vol2MaterialEnd Why = Vol2MaterialEnd.None;
            public Hop Hop; public object Data; public bool Started, ExitDone;
        }
        protected readonly Dictionary<long, Mark> Marks = new Dictionary<long, Mark>();
        protected readonly List<Mark> MarkList = new List<Mark>();

        // ---- 넌: the carrier (the 6 s fire bundle on the caster, amount 0: the budget's vehicle) ----
        protected Vol2Material Carrier; protected float CarrierAt = -1f, CarrierEndAt = -1f; protected Vol2MaterialEnd CarrierWhy = Vol2MaterialEnd.None;

        // ---- 눈: what burned (other casts' materials; their events are not ours: read from the log, resolved through the registry) ----
        public sealed class Burn { public long Id; public Vol2Material M; public Vector3 Centre; public float Radius, At, Delay; public bool Enemy; public object Data; }
        protected readonly List<Burn> Burns = new List<Burn>();
        int _burnSeen;
        /// <summary>눈: the wood / enemy materials standing near the ring at the cast's start (the world's facts, drawn by the look until they burn or end).</summary>
        protected readonly List<Vol2Material> Fuel = new List<Vol2Material>();

        // ---- the brush tip's recent path (attachment swings) ----
        protected readonly List<(Vector3 P, float T)> TipPath = new List<(Vector3, float)>(64);

        // ---- hooks ----
        protected abstract void OnBegin();
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>Path only: in flight, f = 0..1 of the flight now.</summary>
        protected virtual void OnFly(float t, float f, float prev) { }
        /// <summary>The strike lands (path: at the impact clock; fan / ring / attachment: at the launch).</summary>
        protected abstract void OnHit(long nowUs);
        /// <summary>A body touched directly (Instant / Second) - may come before the path's visual hit; <see cref="Struck.At"/> is the executor's time.</summary>
        protected virtual void OnStruck(Struck s) { }
        /// <summary>A spread contact registered (its visual starts at <see cref="Hop.Start"/>).</summary>
        protected virtual void OnHop(Hop h) { }
        /// <summary>A hop's visual arrival at its body.</summary>
        protected virtual void OnLand(Hop h) { }
        protected virtual void OnMark(Mark m) { }
        protected virtual void OnMarkEnd(Mark m) { }
        protected virtual void OnCarrier() { }
        protected virtual void OnCarrierEnd() { }
        protected virtual void OnBurn(Burn b) { }
        /// <summary>Every frame after the strike: h = seconds since it.</summary>
        protected abstract void OnLive(float t, float h);

        public override void Began(long nowUs)
        {
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            Shape = Inst.Log.Glyph.Frame; Letter = Inst.Log.Glyph.Letter;
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = FlatN(spec.Forward); if (spec.Forward.sqrMagnitude < 1e-6f) F = Fwd;
            Depth = Mathf.Max(1f, spec.FanDepth); Half = Mathf.Max(0.1f, spec.FanHalfAngleRad);
            R = Mathf.Max(0.5f, spec.RingRadius); Inner = spec.RingInner; Outer = Mathf.Max(spec.RingInner, spec.RingOuter);
            NowUs = nowUs;
            ReadEmitterEnd();
            if (Letter == '눈') GatherFuel(nowUs);
            OnBegin();
        }

        void ReadEmitterEnd()
        {
            var em = Inst.Log.Emitter;
            if (em is Vol2AttachmentHost h) EmitterEnd = Inst.Rel(h.EndUs);
            else if (em is Vol2TimedEmitter te) EmitterEnd = Inst.Rel(te.EndUs);
        }

        void GatherFuel(long nowUs)
        {
            var co = Inst.Model.Coordinator; if (co == null) return;
            foreach (var m in co.Materials.AliveSnapshot())
            {
                if (m == null || m.Cast == Inst.Cast || m.Attached || m.Footprint == null) continue;
                bool wood = m.Body == Body.Wood && (m.FactionId == Vol2Faction.Player || m.FactionId == Vol2Faction.World);
                bool enemy = m.FactionId == Vol2Faction.Enemy;
                if (!wood && !enemy) continue;
                Vector3 d = m.Footprint.Centre - Apex; d.y = 0f;
                if (d.magnitude > 12f) continue;
                Fuel.Add(m);
            }
            Note += " | fuel " + Fuel.Count;
        }

        void Launch(long nowUs)
        {
            Launched = true; LaunchAt = Now(nowUs);
            if (EmitterEnd < 0f) ReadEmitterEnd();
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
        protected Vector3 CentreNow(Hop h) => h.T != null ? BodyBounds(h.T, h.T.position, 0.5f).center : h.B.center;
        protected Vector3 FeetOf(Hop h) => h.T != null ? h.T.position : h.Feet;
        /// <summary>A point on the brush shaft: 0 = the grip (sleeve), 1 = the tip.</summary>
        protected Vector3 Shaft(float u) => Vector3.LerpUnclamped(Grip(), Tip(), u);
        protected Vector3 ShaftAxis { get { var d = Tip() - Grip(); return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up; } }
        protected Vector3 Sleeve() => Vector3.Lerp(Grip(), Inst.Origin + Vector3.up * 1.4f + Right * 0.22f, 0.35f);

        // ------------------------------------------------------------------------------------------------ the executor's facts

        public override void Contact(Vol2ContactRecord rec)
        {
            if (rec == null || !rec.BudgetAccepted || Root == null) return;
            var tg = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (tg == null) return;
            Watch(tr);
            string kind = rec.Kind ?? "";
            float at = Inst.Rel(rec.NowUs);
            if (kind == "Spread" || kind == "ReturnSpread")
            {
                bool back = kind == "ReturnSpread";
                var h = new Hop { TargetId = rec.TargetId, T = tr, Feet = tg.Position, B = BodyBounds(tr, tg.Position, tg.Radius), At = at, Back = back, MaterialId = rec.MaterialId, Share = (float)rec.Amount };
                h.Order = back ? _backOrder++ : _forwardOrder++;
                // the place in its own spread (the hops the executor paid on the same tick): the visual stagger
                int inSpread = 0; foreach (var o in Hops) if (o.Back == back && Mathf.Abs(o.At - at) < 0.02f) inSpread++;
                SourceOf(h);
                float dist = Vector3.Distance(G(h.FromP), G(h.Feet));
                h.Travel = Mathf.Clamp(dist / Mathf.Max(0.5f, HopSpeed), HopMin, HopMax);
                float earliest = Shape == Frame.Path && ImpactRel > 0f ? Mathf.Max(at, ImpactRel) : at;
                h.Start = (back ? at : earliest) + Mathf.Min(inSpread, 7) * HopStagger;
                if (h.MaterialId != 0 && Marks.TryGetValue(h.MaterialId, out var mk)) { h.Mark = mk; mk.Hop = h; }
                Hops.Add(h);
                Note += " | " + (back ? "back" : "hop") + " " + rec.TargetId + " " + rec.Amount.ToString("0.##") + " @" + at.ToString("0.00");
                OnHop(h);
                return;
            }
            if (kind == "Instant" || kind == "Second" || kind == "Touch")
            {
                var s = new Struck { Id = rec.TargetId, T = tr, Feet = tg.Position, B = BodyBounds(tr, tg.Position, tg.Radius), At = at, Kind = kind };
                Direct.Add(s); _lastDirect = s;
                Note += " | " + kind + " " + rec.TargetId + " @" + at.ToString("0.00");
                OnStruck(s);
            }
        }

        /// <summary>Where the executor spread this hop from (Vol2SpreadExecutor: SpreadFrom / SpreadOutward / the fan's edge / 눈's burning wood).</summary>
        void SourceOf(Hop h)
        {
            if (h.Back)
            {
                // 논's back-spread: the same bodies in reverse - the flame returns from outside the body toward the caster
                Vector3 o = FlatN(h.Feet - Apex);
                h.FromP = G(h.Feet) + o * 1.4f; h.SourceId = h.TargetId; return;
            }
            if (Shape == Frame.Fan)
            {
                Vector3 d = h.Feet - Apex; d.y = 0f; float dist = d.magnitude;
                float ang = Vector3.SignedAngle(F, d, Vector3.up), half = Half * Mathf.Rad2Deg;
                Vector3 dir = Quaternion.AngleAxis(Mathf.Clamp(ang, -half, half), Vector3.up) * F;
                h.FromP = G(Apex + dir * Mathf.Min(dist, Depth)); h.SourceId = 0; return;
            }
            if (Letter == '눈')
            {
                // the burning wood the body stands in
                float best = 99f; Vector3 p = Apex + FlatN(h.Feet - Apex) * R;
                foreach (var b in Burns) { if (b.Enemy) continue; float d = Vector3.Distance(G(b.Centre), G(h.Feet)); if (d < best) { best = d; p = b.Centre; } }
                h.FromP = G(p); h.SourceId = 0; return;
            }
            if (_lastDirect != null) { h.FromP = G(FeetOf(_lastDirect)); h.SourceId = _lastDirect.Id; return; }
            h.FromP = Shape == Frame.Path && HasTarget ? G(FeetNow()) : G(Apex + F * 2f); h.SourceId = TargetId;
        }

        public override void MaterialCreated(Vol2Material m)
        {
            if (m == null || m.Cast != Inst.Cast || Root == null) return;
            float t = Inst.Rel(m.CreatedUs);
            if (m.Kind == Vol2MaterialKind.Carrier)
            {
                if (Carrier != null) return;
                Carrier = m; CarrierAt = t; Note += " | carrier " + (m.LifeUs / 1e6).ToString("0.0") + "s";
                try { OnCarrier(); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (m.Kind != Vol2MaterialKind.Mark || Marks.ContainsKey(m.Id.Value)) return;
            var fp = m.Footprint;
            var mk = new Mark { M = m, Id = m.Id.Value, Centre = fp != null ? G(fp.Origin) : G(Apex), Radius = fp != null ? Mathf.Max(0.3f, fp.Radius) : 0.6f, Born = t, Share = (float)m.Initial };
            foreach (var h in Hops) if (h.MaterialId == mk.Id) { h.Mark = mk; mk.Hop = h; mk.Back = h.Back; }
            Marks[mk.Id] = mk; MarkList.Add(mk);
            Note += " | mark " + mk.Id + " " + m.Initial.ToString("0.##") + " life " + (m.LifeUs / 1e6).ToString("0.0") + "s";
            // OnMark waits for its hop (the executor creates the mark just before it notifies the contact) - see Tick
        }

        void StartMarks(float t)
        {
            foreach (var mk in MarkList)
            {
                if (mk.Started || (mk.Hop == null && t - mk.Born < 0.1f)) continue;
                mk.Started = true;
                if (mk.Hop != null) mk.Back = mk.Hop.Back;
                try { OnMark(mk); } catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        public override void MaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            if (m == null || Root == null) return;
            if (m == Carrier) { EndCarrier(m, why); return; }
            if (Marks.TryGetValue(m.Id.Value, out var mk)) EndMark(mk, why, Inst.Rel(m.EndedUs));
        }

        void EndCarrier(Vol2Material m, Vol2MaterialEnd why)
        {
            if (CarrierEndAt >= 0f) return;
            CarrierEndAt = Inst.Rel(m.EndedUs); CarrierWhy = why; Note += " | carrier ended " + why + " at " + CarrierEndAt.ToString("0.00");
            try { OnCarrierEnd(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void EndMark(Mark mk, Vol2MaterialEnd why, float at)
        {
            if (mk.EndAt >= 0f) return;
            if (!mk.Started) { mk.Started = true; try { OnMark(mk); } catch (System.Exception e) { Debug.LogException(e); } }
            mk.EndAt = at; mk.Why = why;
            Note += " | mark " + mk.Id + " ended " + why + " at " + at.ToString("0.00");
            try { OnMarkEnd(mk); } catch (System.Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------------------------------------ the helpers a glyph draws with

        /// <summary>0..1 of a hop's visual travel at t.</summary>
        protected float HopK(Hop h, float t) => h.Travel <= 0f ? 1f : Mathf.Clamp01((t - h.Start) / h.Travel);
        /// <summary>A point along a hop: u 0..1 from its source to the body's centre (or feet when <paramref name="toFeet"/>), lifted on a lob of <paramref name="lob"/> m.</summary>
        protected Vector3 HopPoint(Hop h, float u, float lob, float startLift = 0.9f, bool toFeet = false)
        {
            Vector3 a = h.FromP + Vector3.up * startLift, b = toFeet ? G(FeetOf(h)) + Vector3.up * 0.05f : CentreNow(h);
            return Vector3.Lerp(a, b, u) + Vector3.up * 4f * lob * u * (1f - u);
        }
        /// <summary>The share as a visual scale: sqrt(share / 0.5) clamped - a normal spread split three ways reads thinner, the peaks' fixed 0.5 full, 눈's whole 1.0 bigger.</summary>
        protected static float ShareScale(float share) => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0.05f, share) / 0.5f), 0.55f, 1.45f);
        /// <summary>When a mark is seen to begin: its creation, never before its hop has landed.</summary>
        protected float MarkStart(Mark m) => m.Hop != null ? Mathf.Max(m.Born, m.Hop.Start + m.Hop.Travel) : m.Born;
        /// <summary>Seconds a mark has stood (-1 = not yet).</summary>
        protected float MarkAge(Mark m, float t) { float s = MarkStart(m); return t < s ? -1f : t - s; }
        /// <summary>0..1 over the template's warning window before the mark's live expiry; 0 once it ended.</summary>
        protected float MarkWarn(Mark m, float t)
        {
            if (m.EndAt >= 0f || m.M == null) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            float e = Inst.Rel(m.M.ExpiresUs);
            return Vol2Fs.Smooth(e - w, e, t);
        }
        /// <summary>The look's own tail after a mark's end event, by its reason (natural expiry = the body's own end clause).</summary>
        protected virtual float MarkExitSeconds(Vol2MaterialEnd why)
        {
            switch (why)
            {
                case Vol2MaterialEnd.Expired: return MarkExpireSeconds;
                case Vol2MaterialEnd.Consumed: return 0.6f;
                case Vol2MaterialEnd.ChildConsumed: return 0.5f;
                case Vol2MaterialEnd.Replaced: return 0.3f;
                case Vol2MaterialEnd.HitCancelled: return 0.25f;
                case Vol2MaterialEnd.Cleansed: return 0.7f;
                case Vol2MaterialEnd.Aborted: return 0.05f;
                default: return 0.3f;
            }
        }
        protected virtual float MarkExpireSeconds => 0.9f;
        /// <summary>0 while the mark stands, 0..1 over its exit tail after the end event.</summary>
        protected float MarkExit(Mark m, float t) => m.EndAt < 0f ? 0f : Mathf.Clamp01((t - m.EndAt) / Mathf.Max(0.05f, MarkExitSeconds(m.Why)));
        /// <summary>The mark standing: 0..1 grown in <paramref name="grow"/> s from its start, times (1 - exit).</summary>
        protected float MarkLife(Mark m, float t, float grow = 0.25f) { float a = MarkAge(m, t); return a < 0f ? 0f : Vol2Fs.Smooth(0f, grow, a) * (1f - MarkExit(m, t)); }
        protected bool Natural(Mark m) => m.Why == Vol2MaterialEnd.Expired;

        /// <summary>Attachment / ring: 0..1 standing from the strike to the emitter's end (+ the tail <paramref name="tail"/>).</summary>
        protected float HostLife(float t, float up = 0.18f, float tail = 0.35f)
        {
            if (!IsHit) return 0f;
            float a = Vol2Fs.Smooth(HitAt, HitAt + up, t);
            float end = EmitterEnd > 0f ? EmitterEnd : (EndRel >= 0f ? EndRel : float.MaxValue);
            return a * (1f - Vol2Fs.Smooth(end, end + tail, t));
        }
        /// <summary>0..1 over the template's warning window before the emitter's end.</summary>
        protected float HostWarn(float t)
        {
            if (EmitterEnd <= 0f) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            return Vol2Fs.Smooth(EmitterEnd - w, EmitterEnd, t) * (t < EmitterEnd ? 1f : 0f);
        }
        /// <summary>넌: 0..1 the carrier standing (its creation to its end + tail).</summary>
        protected float CarrierLife(float t, float tail = 0.5f)
        {
            if (CarrierAt < 0f) return 0f;
            float a = Vol2Fs.Smooth(Mathf.Max(CarrierAt, HitAt), Mathf.Max(CarrierAt, HitAt) + 0.2f, t);
            return CarrierEndAt < 0f ? a : a * (1f - Mathf.Clamp01((t - CarrierEndAt) / tail));
        }
        /// <summary>How many forward hops have visually set out by t (넌: the bundle's portions spent).</summary>
        protected int HopsSent(float t) { int n = 0; foreach (var h in Hops) if (!h.Back && t >= h.Start) n++; return n; }

        /// <summary>How fast the tip moved over the last 0.1 s (m/s).</summary>
        protected float TipSpeed()
        {
            if (TipPath.Count < 2) return 0f;
            var a = TipPath[TipPath.Count - 1]; int i = TipPath.Count - 2;
            while (i > 0 && a.T - TipPath[i].T < 0.1f) i--;
            float dt = Mathf.Max(1e-3f, a.T - TipPath[i].T);
            return (a.P - TipPath[i].P).magnitude / dt;
        }

        /// <summary>A ribbon through the tip's path of the last <paramref name="span"/> s.</summary>
        protected void SwingTrail(Vol2FsStrips s, float t, float span, float width, float life, float rnd)
        {
            if (life <= 0.01f || TipPath.Count < 3) return;
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
            foreach (var p in TipPath)
            {
                float age = t - p.T; if (age > span || age < 0f) continue;
                float u = 1f - age / span;
                pts.Add(p.P); w.Add(width * (0.25f + 0.75f * u)); al.Add(life * u);
            }
            if (pts.Count >= 3 && (pts[pts.Count - 1] - pts[0]).magnitude > 0.35f) s.Strip(pts, w, null, Color.white, al, rnd, CamPos, 0f, 1f);
        }

        // ------------------------------------------------------------------------------------------------ the clock

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            NowUs = nowUs;
            float t = Now(nowUs);
            if (Shape == Frame.Attachment)
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
                else return;
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) EndRel = t;
            // 눈: the burns the executor logged (other casts' materials: read back through the registry)
            var log = Inst.Log;
            while (_burnSeen < log.Burned.Count)
            {
                string s = log.Burned[_burnSeen++];
                var parts = s.Split(':');
                if (parts.Length < 3 || !long.TryParse(parts[0], out long id)) continue;
                var co = Inst.Model.Coordinator;
                Vol2Material m = null;
                if (co != null) co.Materials.TryGet(new MaterialId(id), out m);
                var b = new Burn { Id = id, M = m, Enemy = parts[2] == "enemy", At = m != null && m.EndedUs > 0 ? Inst.Rel(m.EndedUs) : t };
                b.Centre = m != null && m.Footprint != null ? G(m.Footprint.Centre) : G(Apex + F * R);
                b.Radius = m != null && m.Footprint != null ? Mathf.Max(0.4f, m.Footprint.Radius) : 0.6f;
                // the chain: a burn reached from the band first, the wood next to it after (distance from the band, presentation of the executor's chain)
                float band = Mathf.Abs(Vector3.Distance(G(b.Centre), G(Apex)) - R);
                b.Delay = Mathf.Clamp(band / 6f, 0f, 0.6f);
                Burns.Add(b);
                Note += " | burned " + s;
                try { OnBurn(b); } catch (System.Exception e) { Debug.LogException(e); }
            }
            StartMarks(t);
            // a mark / the carrier that ended without this instance hearing it: the executor's end, read back
            foreach (var mk in MarkList) if (mk.EndAt < 0f && mk.M != null && !mk.M.Alive) EndMark(mk, mk.M.End, Inst.Rel(mk.M.EndedUs));
            if (Carrier != null && CarrierEndAt < 0f && !Carrier.Alive) EndCarrier(Carrier, Carrier.End);
            // the hops' visual arrivals (in the executor's order)
            foreach (var h in Hops)
                if (!h.Landed && t >= h.Start + h.Travel) { h.Landed = true; HitStop(t); try { OnLand(h); } catch (System.Exception e) { Debug.LogException(e); } }
            OnLive(t, t - HitAt);
        }

        /// <summary>The look's own tail after the last end (seconds).</summary>
        protected virtual float Tail => 0.6f;

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            if (!IsHit) return false;
            if (EndRel < 0f) return false;
            float by = Mathf.Max(EndRel, HitAt + 0.8f);
            foreach (var mk in MarkList)
            {
                if (mk.EndAt < 0f) return false;                                   // a mark still stands: its end is the executor's
                by = Mathf.Max(by, mk.EndAt + MarkExitSeconds(mk.Why));
            }
            foreach (var h in Hops) by = Mathf.Max(by, h.Start + h.Travel + 0.3f);
            if (Carrier != null) { if (CarrierEndAt < 0f) return false; by = Mathf.Max(by, CarrierEndAt + 0.5f); }
            if (EmitterEnd > 0f) by = Mathf.Max(by, EmitterEnd + 0.4f);
            return t > by + Tail && (!ParticlesAlive() || t > by + Tail + 2.5f);
        }
    }

    /// <summary>Batch 4 helpers: the hop ribbons and the ground runners every 번진다 look draws its propagation with.</summary>
    public static class Vol2FsB4
    {
        /// <summary>An arc ribbon from a to b (lob <paramref name="lob"/> m), drawn from u0 to u1 of its length (a growing / travelling segment), width tapering to the ends.</summary>
        public static void Arc(Vol2FsStrips s, Vector3 a, Vector3 b, float lob, float u0, float u1, float width, float life, float rnd, Vector3 cam, int n = 14, float wobble = 0f, float phase = 0f, Vector3 side = default)
        {
            if (life <= 0.01f || u1 - u0 < 0.005f) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            Vector3 sd = side == default ? Vector3.Cross(Vector3.up, (b - a).normalized) : side;
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                pts[k] = Vector3.Lerp(a, b, u) + Vector3.up * 4f * lob * u * (1f - u) + sd * Mathf.Sin(u * 9f + phase) * wobble * Mathf.Sin(u * Mathf.PI);
                w[k] = width * (0.35f + 0.65f * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI));
                al[k] = life * (0.45f + 0.55f * v);
            }
            s.Strip(pts, w, null, Color.white, al, rnd, cam, u0, u1);
        }

        /// <summary>A ribbon lying flat on the ground from a to b (side = flat perpendicular), drawn from u0 to u1, with a lateral meander.</summary>
        public static void Runner(Vol2FsStrips s, Vector3 a, Vector3 b, float ground, float u0, float u1, float width, float life, float rnd, Vector3 cam, float meander = 0.25f, float phase = 0f, int n = 16, float lift = 0.04f)
        {
            if (life <= 0.01f || u1 - u0 < 0.005f) return;
            a.y = ground + lift; b.y = ground + lift;
            Vector3 dir = (b - a); dir.y = 0f; if (dir.sqrMagnitude < 1e-6f) return; dir.Normalize();
            Vector3 sd = Vector3.Cross(Vector3.up, dir);
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var side = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                pts[k] = Vector3.Lerp(a, b, u) + sd * Mathf.Sin(u * 7.3f + phase) * meander * Mathf.Sin(u * Mathf.PI);
                w[k] = width * (0.5f + 0.5f * Mathf.Sin(Mathf.Clamp01(v * 0.9f + 0.1f) * Mathf.PI));
                al[k] = life; side[k] = sd;
            }
            s.Strip(pts, w, side, Color.white, al, rnd, cam, u0, u1);
        }

        /// <summary>The point at u of a lobbed arc a -> b.</summary>
        public static Vector3 ArcPoint(Vector3 a, Vector3 b, float lob, float u) => Vector3.Lerp(a, b, u) + Vector3.up * 4f * lob * u * (1f - u);

        /// <summary>batch 4 steel (batches 1 - 3 read pinkish on the red 적로 ground: URP Lit metal mirrored the red environment): a cool silver that does not take the
        /// environment's reflection (_ENVIRONMENTREFLECTIONS_OFF), keeps its specular glint, and carries its own cool self-light through the steel texture.
        /// <paramref name="glow"/> 0..2 scales the self-light (a hot edge = a warmer, brighter one).</summary>
        public static Material Steel(Vol2FsKit kit, float glow = 1f, bool warm = false)
        {
            // 2026-10-27 common material pass: the showcase steel shader (silver after the ink wash) for every batch; this URP Lit graphite is the fallback
            var metal = kit.Metal(0.9f + 0.1f * glow, warm ? Mathf.Clamp01(0.35f * glow) : 0f, warm ? default : new Color(0.01f, 0.014f, 0.022f) * glow);
            if (metal != null) return metal;
            string key = "b4steel|" + glow + "|" + warm;
            return kit.Custom(key, () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_B4Steel" };
                m.SetTexture("_BaseMap", kit.Tex("fs_steel"));
                // r2: r1 was still pink - the red came through the DIFFUSE (the red ground's ambient light), not the reflection: a dark albedo takes almost no
                // ambient, the silver is the steel texture's own cool self-light, the sun still gives a specular glint
                m.SetColor("_BaseColor", warm ? new Color(0.2f, 0.19f, 0.17f) : new Color(0.12f, 0.13f, 0.15f));
                m.SetFloat("_Metallic", 0.2f); m.SetFloat("_Smoothness", 0.85f);
                m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetTexture("_EmissionMap", kit.Tex("fs_steel"));
                // r3: a pale self-light was still graded to pink by the main ink look (r2 chakram); a dark blue-grey graphite with a cool sheen reads as steel
                m.SetColor("_EmissionColor", (warm ? new Color(0.75f, 0.58f, 0.36f) : new Color(0.26f, 0.38f, 0.58f)) * glow);
                m.SetFloat("_Smoothness", 0.92f);
                m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                return m;
            });
        }

        /// <summary>batch 4 water (batches 1 - 3 read as thin translucent ribbons): the same particle shader water, more covering (less additive) with a brighter
        /// foam edge, so a sheet of water has body. Earlier glyphs keep Vol2FsB1.Water.</summary>
        public static Material WaterDeep(Vol2FsKit kit, float intensity = 1.5f, string tex = "fs_water")
        {
            // r2: r1 (addMix .16, bright wide edge) read as ICE - softer: a little more light, a soft dim edge, softer erosion
            var w = Vol2FsMat.Make(tex, Color.white, intensity, 0.26f); w.Ramp = "fs_ramp_water"; w.RampScale = 1.5f; w.NoiseAmt = 0.3f; w.NoiseScale = 1.2f; w.Erode = 0.1f; w.Soft = 0.3f;
            w.Scroll = new Vector2(-0.9f, 0f); w.Edge = new Color(0.55f, 0.7f, 0.75f, 1f); w.EdgeWidth = 0.08f; w.Opacity = 0.95f;
            return kit.Mat(w);
        }
    }
}
