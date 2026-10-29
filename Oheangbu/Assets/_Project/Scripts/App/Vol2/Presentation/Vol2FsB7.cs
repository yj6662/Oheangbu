using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 7 (2026-10-27, the user's order "쇼케이스 방향이 낫다. 이 방식으로 다른 술식도 만들어 가자." / "잘 진행해둬"): the ㅇ book 돌아온다 -
    // P5 강 낭 망 상 앙 · A5 겅 넝 멍 성 엉 · F5 공 농 몽 송 옹 · R5 궁 눙 뭉 숭 웅 (peaks 앙 엉 옹 웅 = water).
    // ONE skeleton for the four frames, read only from Vol2ReturnExecutor's facts on the scope clock:
    //   launch (the emitter's StartUs) -> OUT: path = the emitter's live head (36 m/s, to 12 m or a wall); fan = the whole sector slides out FanTravel metres;
    //   ring = the band spreads 2 m -> space.returnRing.outer; orbit (ㅓ) = a point on a 1.6 m circle round the cast point, one turn a second, 6 s (no trip)
    //   -> TURN: the report's BackStartUs (path: the frame the emitter stopped; fan / ring: start + LegUs) -> BACK along the same shape to the caster
    //   -> HOME: the report's BackEndUs (= TripEndUs); ㅓ ends with the attachment host's EndUs
    //   -> contacts: Instant (first touch, out or back; the body's action lands only here), Return (the second pass over a body hit before, x0.5 / 앙 x0.75),
    //      Second (ㅓ: a later turn of the circle), Reflect (엉: a shot turned back, booked at the intercept, delivered at its arrival), Drag (옹)
    //   -> the shots it took out of the air: the report's Intercepts (no position) + the executor's own shot source polled each tick (where each shot last flew)
    //   -> 앙's carry (report CarriedId / From / To: another cast's material, moved, never remade), 옹's drags (report Drags), 웅's cleanse (report Cleansed ids of
    //      the enemy materials snapshotted at the launch) + its wet requests on my materials + the pollution gate's field note.
    // Nothing here decides a target, a touch, a shot, a carry or a lifetime. Presentation only: nothing here is read by a rule.

    /// <summary>The 돌아온다 skeleton of batch 7 (every frame). A glyph fills the hooks; the helpers give it the facts as numbers.</summary>
    public abstract class Vol2FsBack : Vol2FsRun
    {
        protected Frame Shape; protected int Seed; protected char Letter; protected Body Element; protected Color Acc;
        protected bool Launched; protected float LaunchAt = -1f, EndRel = -1f;
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        protected Vector3 Apex, F, Side; protected float Depth, Half, R0 = 2f, R1 = 4f, Thick = 0.6f, PathLen = 12f, PathSpeed = 36f, PathHalf = 0.15f;
        protected float OrbitR = 1.6f, OrbitHalf = 0.3f, RevSeconds = 1f, Travel = 5f;
        protected long NowUs;
        /// <summary>The scope's 돌아온다 executor (its report and tuning); null outside a scope.</summary>
        protected Vol2ReturnExecutor Ex;
        Vol2ReturnReport _rep;
        /// <summary>The executor's report of this cast (legs, shots, carry, drags, cleanse); null until it exists.</summary>
        protected Vol2ReturnReport Rep { get { if (_rep == null && Ex != null) _rep = Ex.ReportOf(Inst.Cast); return _rep; } }
        /// <summary>The trip on the look clock: the turn (back start) and home (back end); -1 = not known yet (a path learns them when its head stops).</summary>
        protected float TurnRel = -1f, HomeRel = -1f, OutStartRel = -1f;
        protected bool Turned, IsHome; protected float TurnAt = -1f, HomeAt = -1f;
        /// <summary>Path: where the head stopped (m along the path from the cast point).</summary>
        protected float HeadEnd = -1f;
        /// <summary>ㅓ: the attachment host's end on the look clock (the orbit's 6 s).</summary>
        protected float LineEndRel = -1f; protected bool LineEnded; protected float LineEndedAt = -1f;

        // ---- bodies the cast touched ----
        public sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public float At; public string Kind; public bool BodyAction, Back; public float Amount; public int Index; public long Swing; public object Data; }
        protected readonly List<Struck> Hits = new List<Struck>();
        protected readonly List<Struck> Refused = new List<Struck>();

        // ---- shots taken out of the air ----
        public sealed class Shot
        {
            public int Index; public Vector3 Pos; public float At; public string Leg = ""; public bool Reflected; public long ShooterId; public Transform Shooter;
            /// <summary>엉: when the turned shot reaches its shooter (the executor's own flight time, capped) and when the Reflect record came (-1 = not yet).</summary>
            public float ArriveAt = -1f, HitAt = -1f; public float PowerHp; public string Reason = ""; public object Data;
        }
        protected readonly List<Shot> Shots = new List<Shot>();
        readonly Dictionary<object, (Vector3 P, float T)> _shotSeen = new Dictionary<object, (Vector3, float)>();
        readonly HashSet<object> _shotUsed = new HashSet<object>();
        readonly List<IVol2Projectile> _gather = new List<IVol2Projectile>();
        readonly HashSet<object> _inFlight = new HashSet<object>();
        int _interSeen;

        // ---- 앙: the carried material ----
        public sealed class Carry { public long Id; public Vol2Material M; public Vector3 From, To; public float At, SetAt = -1f, EndAt = -1f; public Body Body; public float Radius; public object Data; }
        protected Carry Carried;

        // ---- 옹: the drags ----
        public sealed class Dragged { public long Id; public Transform T; public Vector3 From, To; public float At, Dur; public bool Air; public string Sink = ""; public Struck Hit; public object Data; }
        protected readonly List<Dragged> Drags = new List<Dragged>();

        // ---- 웅: the materials the band can touch (snapshotted at the launch) ----
        public sealed class Patch { public long Id; public Vol2Material M; public Vector3 Centre; public float Radius; public Body Body; public bool Enemy; public float At = -1f; public object Data; }
        protected readonly List<Patch> Patches = new List<Patch>();
        readonly Dictionary<long, Patch> _patchOf = new Dictionary<long, Patch>();
        int _cleanSeen, _wetSeen;
        protected readonly List<Patch> Cleansed = new List<Patch>(), WetMine = new List<Patch>();

        // ---- hooks ----
        protected abstract void OnBegin();
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>The trip turned (path: the head stopped; fan / ring: the out leg's end). Not on ㅓ.</summary>
        protected virtual void OnTurn() { }
        /// <summary>The trip came home (the report's BackEndUs), or ㅓ's circle ended (the host's end).</summary>
        protected virtual void OnHome() { }
        /// <summary>A paid first touch (Instant) or ㅓ's second contact (Second). BodyAction = the body's own action landed with it (only the Instant).</summary>
        protected virtual void OnStruck(Struck s) { }
        /// <summary>The way back passed over a body hit before: the return term (x0.5; 앙 x0.75).</summary>
        protected virtual void OnReturnHit(Struck s) { }
        protected virtual void OnRefused(Struck s) { }
        /// <summary>The shape took an enemy shot out of the air (where it last flew). 엉: Reflected = turned back at its shooter.</summary>
        protected virtual void OnShot(Shot s) { }
        /// <summary>엉: the turned shot's booked damage reached its shooter (the Reflect record).</summary>
        protected virtual void OnReflectHit(Shot s, Struck rec) { }
        /// <summary>앙: the returning head picked up one of my materials (moved to 2 m ahead of the caster: Carried.To).</summary>
        protected virtual void OnCarry(Carry c) { }
        protected virtual void OnCarryEnd(Carry c) { }
        /// <summary>옹: a body is pulled toward the caster on the way back (the status sink moves it).</summary>
        protected virtual void OnDrag(Dragged d) { }
        /// <summary>웅: an enemy material the band passed is wiped (ended Cleansed, no product).</summary>
        protected virtual void OnCleanse(Patch p) { }
        /// <summary>웅: one of my materials in the band is noted wet.</summary>
        protected virtual void OnWet(Patch p) { }
        /// <summary>Every frame after the launch.</summary>
        protected abstract void OnLive(float t);

        public override void Began(long nowUs)
        {
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            Shape = Inst.Log.Glyph.Frame; Letter = Inst.Log.Glyph.Letter; Element = Inst.Log.Glyph.Body; Acc = Accent(Element);
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = FlatN(spec.Forward); if (spec.Forward.sqrMagnitude < 1e-6f) F = Fwd;
            Side = Vector3.Cross(Vector3.up, F);
            Depth = Mathf.Max(1f, spec.FanDepth); Half = Mathf.Max(0.1f, spec.FanHalfAngleRad);
            R0 = Mathf.Max(0.5f, spec.RingRadius); Thick = Mathf.Max(0.1f, spec.RingThickness);
            PathLen = Mathf.Max(1f, spec.PathLength); PathSpeed = Mathf.Max(1f, spec.PathSpeed); PathHalf = Mathf.Max(0.05f, spec.PathWidth * 0.5f);
            NowUs = nowUs;
            var co = Inst.Model.Coordinator;
            if (co != null && co.Verbs.TryGet(Verb.Return, out var ex)) Ex = ex as Vol2ReturnExecutor;
            if (co != null) { try { R1 = Mathf.Max(R0, (float)co.Numbers.Get("space.returnRing.outer")); } catch (System.Exception) { R1 = R0 * 2f; } }
            if (Ex != null) { OrbitR = Ex.Tuning.OrbitRadius; OrbitHalf = Ex.Tuning.OrbitHalfWidth; RevSeconds = Mathf.Max(0.05f, Ex.Tuning.OrbitRevolutionUs / 1e6f); Travel = Ex.Tuning.FanTravel > 0f ? Ex.Tuning.FanTravel : Depth; }
            else Travel = Depth;
            if (Inst.Log.Emitter is Vol2AttachmentHost ah) LineEndRel = Inst.Rel(ah.EndUs);
            // 웅 (and anything else that wants them): every material the band could pass, as it stood at the cast (an ended one's footprint is still read from here)
            if (co != null)
                foreach (var m in co.Materials.AliveSnapshot())
                {
                    if (m == null || m.Attached || m.Cast == Inst.Cast || m.Footprint == null) continue;
                    var p = new Patch { Id = m.Id.Value, M = m, Centre = G(m.Footprint.Centre), Radius = m.Footprint.Kind == Vol2FootprintKind.Disc ? m.Footprint.Radius : 0.8f, Body = m.Body, Enemy = m.FactionId == Vol2Faction.Enemy };
                    Patches.Add(p); _patchOf[p.Id] = p;
                }
            OnBegin();
        }

        protected static Vector3 FlatN(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        protected Vector3 G(Vector3 p) { p.y = Ground; return p; }
        protected Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);
        protected Vector3 OnPath(float along) => G(Apex + F * along);
        protected float AlongOf(Vector3 p) => Vector3.Dot(G(p) - G(Apex), F);
        protected Vector3 CentreNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f).center : s.B.center;
        protected Bounds BodyNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
        protected Vector3 FeetOf(Struck s) => s.T != null ? s.T.position : s.Feet;
        protected Vector3 Shaft(float u) => Vector3.LerpUnclamped(Grip(), Tip(), u);
        protected Vector3 Sleeve() => Vector3.Lerp(Grip(), Inst.Origin + Vector3.up * 1.4f + Right * 0.22f, 0.35f);
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        /// <summary>1 on the front arc .. 0 straight behind (the shoulder camera stands inside the ring).</summary>
        protected static float Front(float ang) => 1f - Vol2Fs.Smooth(100f, 165f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)));

        // ------------------------------------------------------------------------------------------------ the trip on the look clock (the report's legs)

        /// <summary>Path: metres along the path the head is at now (out = the emitter's live head; back = from where it stopped, at the path speed). 0 before the launch.</summary>
        protected float HeadAlong(float t)
        {
            if (!Launched) return 0f;
            if (TurnRel >= 0f && t >= TurnRel) return Mathf.Max(0f, HeadEnd - PathSpeed * (t - TurnRel));
            var p = Inst.Path;
            return p != null ? p.HeadAlong : Mathf.Min(PathLen, PathSpeed * Mathf.Max(0f, t - LaunchAt));
        }
        /// <summary>Fan / ring: 0 at the launch .. 1 at the turn .. 0 home (the report's legs, as the executor's Frac).</summary>
        protected float Trip01(float t)
        {
            if (!Launched || TurnRel < 0f) return 0f;
            if (t <= TurnRel) return Mathf.Clamp01((t - OutStartRel) / Mathf.Max(0.01f, TurnRel - OutStartRel));
            if (HomeRel <= TurnRel) return 0f;
            return 1f - Mathf.Clamp01((t - TurnRel) / Mathf.Max(0.01f, HomeRel - TurnRel));
        }
        /// <summary>Fan: how far the sector's apex has slid out (m).</summary>
        protected float FanOut(float t) => Travel * Trip01(t);
        /// <summary>Ring: the band's centre radius now (m).</summary>
        protected float RingNow(float t) => Mathf.Lerp(R0, R1, Trip01(t));
        /// <summary>On the way back now?</summary>
        protected bool Back(float t) => TurnRel >= 0f && t >= TurnRel;
        /// <summary>ㅓ: the orbit angle now (0 = ahead, + toward the right, clockwise from above; unbounded).</summary>
        protected float Psi(float t) => Vol2ReturnGeometry.Tau * Mathf.Max(0f, t - LaunchAt) / RevSeconds;
        /// <summary>ㅓ: the orbit point at angle <paramref name="psi"/>, at height <paramref name="h"/> above the ground.</summary>
        protected Vector3 OrbitPoint(float psi, float h = 1.0f) => G(Vol2ReturnGeometry.PointOnOrbit(Apex, F, OrbitR, psi)) + Vector3.up * h;
        /// <summary>0..1 the trip (or ㅓ's circle) standing: grown in <paramref name="grow"/> from the launch, down over <paramref name="tail"/> after home.</summary>
        protected float TripLife(float t, float grow = 0.06f, float tail = 0.6f)
        {
            if (!Launched) return 0f;
            float a = Vol2Fs.Smooth(LaunchAt, LaunchAt + grow, t);
            float end = Shape == Frame.Attachment ? (LineEnded ? LineEndedAt : -1f) : (IsHome ? HomeAt : -1f);
            return end >= 0f ? a * (1f - Mathf.Clamp01((t - end) / Mathf.Max(0.05f, tail))) : a;
        }
        /// <summary>ㅓ: 0..1 over the template's warning window before the circle ends.</summary>
        protected float LineWarn(float t)
        {
            if (LineEndRel < 0f || LineEnded) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            return Vol2Fs.Smooth(LineEndRel - w, LineEndRel, t);
        }
        protected Vol2FsCutAct Act;
        /// <summary>The body's own action on a first touch, drawn by the shared Vol2FsCutAct (bind / burn / push / wet / cut) when the contact carries it.</summary>
        protected virtual bool UseAct => true;
        protected virtual Vector3 ActDir(Struck s) => Shape == Frame.Path ? F : FlatN(FeetOf(s) - Apex);

        void Launch(long nowUs)
        {
            Launched = true; LaunchAt = Now(nowUs); OutStartRel = LaunchAt;
            var rep = Rep;
            if (Shape == Frame.Fan || Shape == Frame.Ring)
            {
                if (rep != null && rep.OutEndUs > 0) { TurnRel = Inst.Rel(rep.OutEndUs); HomeRel = Inst.Rel(rep.BackEndUs); OutStartRel = Inst.Rel(rep.OutEndUs - rep.LegUs); }
                else if (Inst.Log.Emitter is Vol2TimedEmitter te) { HomeRel = Inst.Rel(te.EndUs); TurnRel = (LaunchAt + HomeRel) * 0.5f; }
            }
            Note = "launch " + Shape + (TurnRel >= 0f ? " turn " + TurnRel.ToString("0.00") + " home " + HomeRel.ToString("0.00") : "");
            OnLaunch(nowUs);
        }

        // ------------------------------------------------------------------------------------------------ the executor's facts

        public override void Contact(Vol2ContactRecord rec)
        {
            if (rec == null || Root == null) return;
            string kind = rec.Kind ?? "";
            float at = Inst.Rel(rec.NowUs);
            var tg = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (kind == "Touch" && !rec.BudgetAccepted)
            {
                Watch(tr);
                var r = new Struck { Id = rec.TargetId, T = tr, Feet = tg != null ? tg.Position : Apex + F * rec.Along, At = at, Kind = "Refused:" + rec.BudgetReason, Index = Refused.Count };
                r.B = BodyBounds(tr, r.Feet, tg != null ? tg.Radius : 0.5f);
                Refused.Add(r); Note += " | refused " + rec.TargetId + " " + rec.BudgetReason;
                try { OnRefused(r); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (kind == "Reflect")
            {
                // 엉: the borrowed damage arrived at the shooter (booked at the intercept); the shot it belongs to = the oldest turned shot of that shooter not yet hit
                Shot sh = null;
                foreach (var x in Shots) if (x.Reflected && x.HitAt < 0f && (x.ShooterId == rec.TargetId || x.ShooterId == 0)) { sh = x; break; }
                Note += " | reflect -> " + rec.TargetId + " " + rec.Amount.ToString("0.###") + " applied=" + rec.DamageApplied + " @" + at.ToString("0.00");
                if (sh == null || !rec.DamageRequested) return;
                // the record carries the intercept's time (booked then); it reaches us at the delivery - drawn at the arrival
                sh.HitAt = Mathf.Max(at, sh.ArriveAt);
                var rs = new Struck { Id = rec.TargetId, T = tr, Feet = tg != null ? tg.Position : sh.Pos, At = at, Kind = "Reflect", Amount = (float)rec.Amount, Index = Hits.Count };
                rs.B = BodyBounds(tr, rs.Feet, tg != null ? tg.Radius : 0.5f);
                Watch(tr);
                try { OnReflectHit(sh, rs); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (kind == "Drag")
            {
                var rep = Rep; Vol2ReturnReport.Drag d = null;
                if (rep != null) foreach (var x in rep.Drags) if (x.TargetId == rec.TargetId) d = x;
                var dr = new Dragged { Id = rec.TargetId, T = tr, At = at, From = d != null ? G(d.From) : (tg != null ? G(tg.Position) : Apex), To = d != null ? G(d.To) : Apex + F * 1.5f, Dur = d != null ? d.DurationUs / 1e6f : 0.5f, Air = d != null && d.Airborne, Sink = d != null ? d.Sink : "" };
                foreach (var h in Hits) if (h.Id == rec.TargetId) dr.Hit = h;
                Drags.Add(dr); Watch(tr);
                Note += " | drag " + rec.TargetId + " " + rec.BodyEffect + " @" + at.ToString("0.00");
                try { OnDrag(dr); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (!rec.BudgetAccepted) return;
            if (kind != "Instant" && kind != "Second" && kind != "Return") return;
            Watch(tr);
            var s = new Struck { Id = rec.TargetId, T = tr, Feet = tg != null ? tg.Position : Apex + F * rec.Along, At = at, Kind = kind, BodyAction = kind == "Instant" && rec.BodyEffect != null && rec.BodyEffect.Length > 0, Amount = (float)rec.Amount, Index = Hits.Count, Swing = rec.SwingId };
            s.Back = kind == "Return" || Back(at);
            s.B = BodyBounds(tr, s.Feet, tg != null ? tg.Radius : 0.5f);
            Hits.Add(s);
            Note += " | " + kind + " " + rec.TargetId + " " + rec.Amount.ToString("0.###") + (s.BodyAction ? " [" + rec.BodyEffect + "]" : "") + " @" + at.ToString("0.00");
            _pending.Add(s);
        }
        readonly List<Struck> _pending = new List<Struck>();

        void PollShots(float t)
        {
            if (Ex == null) return;
            var co = Inst.Model.Coordinator;
            var src = Ex.Projectiles;
            _gather.Clear(); _inFlight.Clear();
            if (src != null) { try { src.Gather(co, Apex, Ex.Tuning.ProjectileReach, _gather); } catch (System.Exception) { _gather.Clear(); } }
            foreach (var p in _gather) { if (p == null || p.Identity == null) continue; _inFlight.Add(p.Identity); _shotSeen[p.Identity] = (p.Position, t); }
            var rep = Rep; if (rep == null) return;
            for (; _interSeen < rep.Intercepts.Count; _interSeen++)
            {
                var e = rep.Intercepts[_interSeen];
                // the shot this entry took: one that was in the air until now and is not any more (where it last flew); the nearest to the shape if several
                object pick = null; Vector3 pos = Vector3.zero; float best = float.MaxValue; Vector3 near = ShapePointNow(t);
                foreach (var kv in _shotSeen)
                {
                    if (_shotUsed.Contains(kv.Key) || _inFlight.Contains(kv.Key)) continue;
                    float d = (kv.Value.P - near).sqrMagnitude;
                    if (d < best) { best = d; pick = kv.Key; pos = kv.Value.P; }
                }
                if (pick != null) _shotUsed.Add(pick); else pos = near;
                float at = Inst.Rel(e.AtUs);
                var sh = new Shot { Index = Shots.Count, Pos = pos, At = at, Leg = e.Leg ?? "", Reflected = e.Reflected && e.HadShooter, ShooterId = e.ShooterId, PowerHp = e.PowerHp, Reason = e.Reason ?? "" };
                if (sh.Reflected)
                {
                    sh.Shooter = TargetTransform(e.ShooterId); Watch(sh.Shooter);
                    var tg = Target(e.ShooterId);
                    float dist = tg != null ? Vol2ReturnGeometry.FlatDistance(pos, tg.Position) : 6f;
                    float flight = Mathf.Min(Ex.Tuning.ReflectMaxFlightUs / 1e6f, dist / Mathf.Max(0.1f, Ex.Tuning.ReflectSpeed));
                    sh.ArriveAt = at + flight;
                }
                Shots.Add(sh);
                Note += " | shot " + e.Key + " " + e.Leg + (sh.Reflected ? " reflected" : e.Reason.Length > 0 ? " " + e.Reason : "") + " @" + at.ToString("0.00");
                ShowShot(sh, t);
            }
        }

        /// <summary>Where the shape is now (a stand-in position for a shot taken with no tracked flight).</summary>
        protected Vector3 ShapePointNow(float t)
        {
            switch (Shape)
            {
                case Frame.Path: return OnPath(HeadAlong(t)) + Vector3.up * 1.1f;
                case Frame.Fan: return G(Apex + F * (FanOut(t) + Depth * 0.5f)) + Vector3.up * 1.1f;
                case Frame.Ring: return G(Apex + F * RingNow(t)) + Vector3.up * 1.1f;
                default: return OrbitPoint(Psi(t), 1.1f);
            }
        }

        void PollCarry(float t)
        {
            var rep = Rep; if (rep == null) return;
            if (Carried == null && rep.CarriedId != 0)
            {
                var co = Inst.Model.Coordinator;
                Vol2Material m = null; if (co != null) co.Materials.TryGet(new MaterialId(rep.CarriedId), out m);
                Carried = new Carry { Id = rep.CarriedId, M = m, From = G(rep.CarriedFrom), To = G(rep.CarriedTo), At = t, Body = m != null ? m.Body : Body.Earth, Radius = m != null && m.Footprint != null && m.Footprint.Kind == Vol2FootprintKind.Disc ? m.Footprint.Radius : 0.5f };
                // the head meets it on the way back: seen picked up where the visible head passes its place
                if (Shape == Frame.Path && TurnRel >= 0f) { float along = AlongOf(Carried.From); Carried.At = Mathf.Max(t, TurnRel + Mathf.Max(0f, HeadEnd - along) / PathSpeed); }
                // set down where the head passes the destination (2 m ahead) on its way home
                Carried.SetAt = Shape == Frame.Path && TurnRel >= 0f ? TurnRel + Mathf.Max(0f, HeadEnd - AlongOf(Carried.To)) / PathSpeed : Carried.At + 0.3f;
                Carried.SetAt = Mathf.Max(Carried.SetAt, Carried.At + 0.05f);
                Note += " | " + rep.CarryNote;
                try { OnCarry(Carried); } catch (System.Exception e) { Debug.LogException(e); }
            }
            if (Carried != null && Carried.EndAt < 0f && Carried.M != null && !Carried.M.Alive)
            {
                Carried.EndAt = Mathf.Max(Inst.Rel(Carried.M.EndedUs), Carried.SetAt);
                Note += " | carried ended " + Carried.M.End + " @" + Carried.EndAt.ToString("0.00");
                try { OnCarryEnd(Carried); } catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        void PollCleanse(float t)
        {
            var rep = Rep; if (rep == null) return;
            for (; _cleanSeen < rep.Cleansed.Count; _cleanSeen++)
            {
                if (!_patchOf.TryGetValue(rep.Cleansed[_cleanSeen], out var p)) continue;
                var m = p.M; p.At = m != null && !m.Alive ? Inst.Rel(m.EndedUs) : t;
                Cleansed.Add(p); Note += " | cleansed " + p.Id + " @" + p.At.ToString("0.00");
                try { OnCleanse(p); } catch (System.Exception e) { Debug.LogException(e); }
            }
            for (; _wetSeen < rep.WetRequests.Count; _wetSeen++)
            {
                if (!_patchOf.TryGetValue(rep.WetRequests[_wetSeen], out var p)) continue;
                p.At = t; WetMine.Add(p); Note += " | wet " + p.Id + " @" + t.ToString("0.00");
                try { OnWet(p); } catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------------------------------------ the shared pictures of the book (every glyph has them; the glyph adds its own)

        ParticleSystem _sparks, _ink, _flash, _drop;
        Mesh _bookMesh; readonly Vol2FsStrips _book = new Vol2FsStrips();
        Light _bookLight; float _bookLightUntil = -1f;
        /// <summary>The echo ring a return hit leaves on the body (the second, weaker strike) - colour of this glyph.</summary>
        protected virtual Color EchoColor => Acc * 1.4f;
        protected ParticleSystem BookSparks => _sparks;
        protected ParticleSystem BookInk => _ink;
        protected ParticleSystem BookFlash => _flash;
        sealed class Echo { public Vector3 C, N; public float At, R; public bool Strong; }
        readonly List<Echo> _echoes = new List<Echo>();

        void BuildBook()
        {
            var root = Root.transform;
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Acc, Color.white, 0.5f), 4.5f, 1f)), Life0 = 0.2f, Life1 = 0.5f, Speed0 = 4f, Speed1 = 11f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 700 }, root, "B7Sparks");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.Edge = Acc * 1.3f; ink.EdgeWidth = 0.15f; ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            _ink = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 3.5f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 1.4f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "B7Ink");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Acc, Color.white, 0.25f), 2.2f, 1f)), Life0 = 0.06f, Life1 = 0.1f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.3f, Size1 = 0.45f, Max = 40 }, root, "B7Flash");
            _drop = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0.5f, Size0 = 0.05f, Size1 = 0.12f, Gravity = 2.4f, Drag = 0.2f, Tiles = 2, Max = 120 }, root, "B7ShotInk");
            foreach (var ps in new[] { _sparks, _ink, _flash, _drop }) ps.Play(false);
            var glow = M("fs_streak", EchoColor, 2.2f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "B7Echo", Kit.Mat(glow), out _bookMesh);
            _bookLight = Vol2Fs.PointLight(root, "B7Light", Color.Lerp(Acc, Color.white, 0.3f), 5f);
            if (UseAct) Act = new Vol2FsCutAct(Kit, root, Element, Seed, Acc);
        }

        protected void FlashLight(Vector3 at, float intensity, float until)
        {
            if (_bookLight == null) return;
            _bookLight.transform.position = at; _bookLight.intensity = Mathf.Max(_bookLight.intensity, intensity); _bookLightUntil = Mathf.Max(_bookLightUntil, until);
        }

        /// <summary>The shared echo: a ring on the body facing the camera (strong = the first touch, weak = the return's x0.5).</summary>
        protected void AddEcho(Vector3 c, float r, bool strong, float t) => _echoes.Add(new Echo { C = c, N = FlatN(CamPos - c), At = t, R = r, Strong = strong });

        void ShowShot(Shot s, float t)
        {
            // the shot knocked out of the air: an ink pop and sparks where it was; a turned shot (엉) flashes brighter
            HitStop(t, 0.05f);
            for (int k = 0; k < (s.Reflected ? 40 : 24); k++) Vol2FsB1.EmitV(_sparks, s.Pos, (Random.onUnitSphere + Vector3.up * 0.3f).normalized * Random.Range(3f, 9f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_drop, s.Pos + Random.insideUnitSphere * 0.1f, Random.insideUnitSphere * 0.6f + Vector3.down * 0.4f, -1f, -1f);
            if (_flash != null) Vol2FsB1.EmitV(_flash, s.Pos, Vector3.zero, s.Reflected ? 0.6f : 0.4f, 0.08f);
            FlashLight(s.Pos, s.Reflected ? 5f : 3f, t + 0.2f);
            try { OnShot(s); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void ShowHit(Struck s, float t)
        {
            var b = BodyNow(s);
            bool ret = s.Kind == "Return";
            HitStop(t, ret ? 0.06f : 0.08f);
            AddEcho(b.center, Mathf.Max(b.extents.x, b.extents.y) + (ret ? 0.15f : 0.3f), !ret, t);
            int n = ret ? 30 : 60;
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_sparks, b.center + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.35f).normalized * Random.Range(4f, ret ? 8f : 12f), -1f, -1f);
            for (int k = 0; k < (ret ? 5 : 10); k++) Vol2FsB1.EmitV(_ink, b.center, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(1.5f, 4f), -1f, -1f);
            FlashLight(b.center, ret ? 3.5f : 6f, t + 0.25f);
            if (s.BodyAction && Act != null) { try { Act.Begin(s.T, b, FeetOf(s), t, ActDir(s)); } catch (System.Exception e) { Debug.LogException(e); } }
            try { if (ret) OnReturnHit(s); else OnStruck(s); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void DrawBook(float t)
        {
            var cam = CamPos;
            _book.Clear();
            for (int i = _echoes.Count - 1; i >= 0; i--)
            {
                var e = _echoes[i]; float a = t - e.At;
                if (a > 0.6f) { _echoes.RemoveAt(i); continue; }
                if (a < 0f) continue;
                float life = (1f - Vol2Fs.Smooth(0.08f, 0.6f, a)) * (e.Strong ? 1f : 0.7f);
                Vol2FsB5.Halo(_book, e.C, e.N, e.R * (0.8f + 0.6f * Vol2Fs.Smooth(0f, 0.4f, a)), e.Strong ? 0.045f : 0.03f, life * 0.8f, 0.3f, cam, 28);
                if (!e.Strong) Vol2FsB5.Halo(_book, e.C, e.N, e.R * (0.6f + 0.5f * Vol2Fs.Smooth(0f, 0.4f, a)), 0.03f, life * 0.7f, 0.6f, cam, 24);
            }
            _book.Write(_bookMesh);
            if (Act != null) Act.Tick(t, cam, Ground);
            if (_bookLight != null) _bookLight.intensity = t < _bookLightUntil ? _bookLight.intensity * 0.86f : Mathf.MoveTowards(_bookLight.intensity, 0f, Time.deltaTime * 30f);
        }

        // ------------------------------------------------------------------------------------------------ the clock

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            NowUs = nowUs;
            float t = Now(nowUs);
            if (_bookMesh == null) BuildBook();
            if (!Launched)
            {
                float k = LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f;
                OnCharge(t, k);
                if (t >= LaunchRel - 1e-4f) Launch(nowUs); else return;
            }
            var rep = Rep;
            // the path learns its turn when its emitter stopped (the report's BackStartUs); fan / ring know theirs from the launch
            if (Shape == Frame.Path && TurnRel < 0f && rep != null && rep.BackStartUs > 0)
            {
                TurnRel = Inst.Rel(rep.BackStartUs); HomeRel = Inst.Rel(rep.BackEndUs);
                HeadEnd = Inst.Path != null ? Inst.Path.HeadAlong : PathSpeed * Mathf.Max(0f, TurnRel - LaunchAt);
                Note += " | turn " + TurnRel.ToString("0.00") + " at " + HeadEnd.ToString("0.0") + "m (" + (Inst.Path != null ? Inst.Path.EndReason : "-") + ") home " + HomeRel.ToString("0.00");
            }
            if (!Turned && TurnRel >= 0f && t >= TurnRel && Shape != Frame.Attachment)
            {
                Turned = true; TurnAt = t; HitStop(t, 0.05f);
                try { OnTurn(); } catch (System.Exception e) { Debug.LogException(e); }
            }
            if (!IsHome && HomeRel >= 0f && t >= HomeRel && Shape != Frame.Attachment)
            {
                IsHome = true; HomeAt = t;
                try { OnHome(); } catch (System.Exception e) { Debug.LogException(e); }
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) EndRel = t;
            if (Shape == Frame.Attachment && !LineEnded && LineEndRel >= 0f && (t >= LineEndRel || (Inst.Log.Emitter != null && Inst.Log.Emitter.Finished && ended)))
            {
                LineEnded = true; LineEndedAt = Mathf.Min(t, Mathf.Max(LineEndRel, LaunchAt));
                try { OnHome(); } catch (System.Exception e) { Debug.LogException(e); }
            }
            // a cast that ended before its trip finished (aborted / refused): home now
            if (ended && !IsHome && Shape != Frame.Attachment && (HomeRel < 0f || t > HomeRel + 0.5f)) { IsHome = true; HomeAt = t; try { OnHome(); } catch (System.Exception e) { Debug.LogException(e); } }
            PollShots(t); PollCarry(t); PollCleanse(t);
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var s = _pending[i];
                if (t < s.At) continue;
                _pending.RemoveAt(i);
                ShowHit(s, t);
            }
            OnLive(t);
            DrawBook(t);
        }

        /// <summary>The look's own tail after the last end (seconds).</summary>
        protected virtual float Tail => 0.6f;
        /// <summary>How long the look keeps drawing a carried material after it was set down, at most (the material's own look stands there in the game).</summary>
        protected virtual float CarryHold => 12f;

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            if (EndRel < 0f) return false;
            if (_pending.Count > 0) return false;
            float by = Mathf.Max(EndRel, LaunchAt + 0.8f);
            if (Shape == Frame.Attachment) { if (!LineEnded) return false; by = Mathf.Max(by, LineEndedAt + 0.5f); }
            else { if (!IsHome) return false; by = Mathf.Max(by, HomeAt + 0.5f); }
            if (Act != null && Act.Busy(t) && t < EndRel + 8f) return false;
            foreach (var s in Shots) if (s.Reflected && s.ArriveAt > 0f) by = Mathf.Max(by, s.ArriveAt + 0.5f);
            if (Carried != null) { if (Carried.EndAt < 0f && t < Carried.SetAt + CarryHold) return false; by = Mathf.Max(by, (Carried.EndAt >= 0f ? Carried.EndAt : t) + 0.6f); }
            return t > by + Tail && (!ParticlesAlive() || t > by + Tail + 2.5f);
        }
    }

    /// <summary>The skeleton's plain look (a placeholder until a glyph has its own): the body's accent carrier at the trip's point (path head / fan front / ring band /
    /// orbit point), the shared hits / echoes / shot pops / body actions. Also proves the facts chain.</summary>
    public class Vol2FsBackPlain : Vol2FsBack
    {
        Mesh _lineMesh; readonly Vol2FsStrips _line = new Vol2FsStrips();
        protected override void OnBegin()
        {
            var l = M("fs_streak", Acc, 2.6f, 1f); l.NoiseAmt = 0.2f; l.Soft = 0.03f;
            Vol2Fs.MeshObject(Root.transform, "PlainTrip", Kit.Mat(l), out _lineMesh);
        }
        protected override void OnCharge(float t, float k) { }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnLive(float t)
        {
            var cam = CamPos; _line.Clear();
            float life = TripLife(t);
            switch (Shape)
            {
                case Frame.Path: { float a = HeadAlong(t); if (a > 0.3f) Vol2FsB6.Crack(_line, OnPath(0.3f), OnPath(a), Ground, 0.08f, 0f, 1f, life, 0.3f, Seed, 0.1f); break; }
                case Frame.Fan: { float o = FanOut(t); Vol2FsB4A.GroundArc(_line, G(Apex + F * o), Ground, Depth, 0.08f, Mathf.Atan2(F.z, F.x) - Half, Half * 2f, life, 0.4f, cam); break; }
                case Frame.Ring: Vol2FsB4A.GroundArc(_line, Apex, Ground, RingNow(t), 0.08f, 0f, Mathf.PI * 2f, life, 0.4f, cam, 64); break;
                default:
                    {
                        float p = Psi(t); var pts = new Vector3[10]; var w = new float[10]; var al = new float[10];
                        for (int k = 0; k < 10; k++) { pts[k] = OrbitPoint(p - k * 0.12f); w[k] = 0.08f * (1f - k / 10f); al[k] = life; }
                        _line.Strip(pts, w, null, Color.white, al, 0.4f, cam);
                        break;
                    }
            }
            _line.Write(_lineMesh);
        }
    }

    /// <summary>Batch 7 helpers: the lab's stand-in of an enemy patch (a dark pool, no light: 오염은 빛나지 않는다), the trip loop ribbon.</summary>
    public static class Vol2FsB7
    {
        /// <summary>A flat pool mesh of radius 1 (pivot at its middle on the ground), lumpy rim; the kit owns it.</summary>
        public static Mesh PoolMesh(Vol2FsKit kit, int seed)
            => kit.SharedMesh("b7pool|" + seed, () =>
            {
                int n = 28; var v = new List<Vector3> { new Vector3(0f, 0.02f, 0f) }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var idx = new List<int>();
                for (int k = 0; k < n; k++)
                {
                    float a = k / (float)n * Mathf.PI * 2f, r = 0.82f + 0.3f * Vol2Fs.Hash(seed, k) * (0.6f + 0.4f * Mathf.Sin(a * 3f + seed));
                    v.Add(new Vector3(Mathf.Cos(a) * r, 0.006f, Mathf.Sin(a) * r)); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int k = 0; k < n; k++) { idx.Add(0); idx.Add(1 + (k + 1) % n); idx.Add(1 + k); }
                var m = new Mesh { name = "FsB7Pool" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            });

        /// <summary>Dark corrupted ink (an enemy's patch): a deep violet-black, a little sheen, no emission.</summary>
        public static Material PoolMat(Vol2FsKit kit) => kit.Custom("b7pool", () =>
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_B7Pool" };
            m.SetColor("_BaseColor", new Color(0.11f, 0.05f, 0.13f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.78f);
            return m;
        });

        /// <summary>The lab's stand-in of an enemy patch on the ground (a dark pool with a few bubbles of ink) - LAB-SIMULATED: in the game the patch's own look stands
        /// there. Owner: the caller (destroy the returned object).</summary>
        public static GameObject PoolProxy(Vol2FsKit kit, string name, Vector3 centre, float radius, long materialId)
        {
            var go = new GameObject(name);
            int seed = (int)(materialId % 9973);
            var pool = Vol2FsB1.Prop(go.transform, "Pool", PoolMesh(kit, seed), PoolMat(kit), false);
            pool.transform.localScale = new Vector3(radius, 1f, radius);
            for (int k = 0; k < 4; k++)
            {
                var b = Vol2FsB1.Prop(go.transform, "Blob" + k, kit.SharedMesh("b7blob" + (k % 2), () => Vol2FsB1.Rock(310 + k % 2, 0.5f, 0.45f, 10, 7)), PoolMat(kit), false);
                float a = Vol2Fs.Hash(seed, k + 40) * Mathf.PI * 2f, r = radius * 0.55f * Vol2Fs.Hash(seed, k + 60);
                b.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                b.transform.localScale = Vector3.one * (0.28f + 0.25f * Vol2Fs.Hash(seed, k + 80)) * radius;
            }
            go.transform.position = centre;
            return go;
        }

        /// <summary>A standing stone of mine (a residue of another cast) for a Disc of radius r: a chipped slab this wide (x), high (y) and thick (z).</summary>
        public static Vector3 StoneDims(float r) => new Vector3(Mathf.Max(0.45f, r * 1.1f), Mathf.Clamp(r * 2.4f, 0.8f, 1.5f), Mathf.Max(0.3f, r * 0.7f));
        /// <summary>The standing stone's mesh (pivot at its bottom middle); the kit owns it. The 앙 look carries the same mesh.</summary>
        public static Mesh StoneMesh(Vol2FsKit kit, long materialId, Vector3 dims)
        {
            int seed = (int)(materialId % 9973);
            return kit.SharedMesh("b7stone|" + seed + "|" + dims.x.ToString("0.00") + "|" + dims.y.ToString("0.00"), () => Vol2FsB1.Slab(seed * 3 + 7, dims, 0.9f));
        }
        /// <summary>The lab's stand-in of MY standing stone (another cast's earth residue): LAB-SIMULATED - in the game the material's own look stands there.
        /// Owner: the caller (destroy the returned object).</summary>
        public static GameObject StoneProxy(Vol2FsKit kit, string name, Vector3 feet, Vector3 facing, float radius, long materialId)
        {
            var go = new GameObject(name);
            var dims = StoneDims(radius);
            Vol2FsB1.Prop(go.transform, "Stone", StoneMesh(kit, materialId, dims), Vol2FsB6.WallMat(kit));
            facing.y = 0f; if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            go.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(facing.normalized, Vector3.up) * Quaternion.Euler(0f, 18f + materialId % 23, 0f));
            return go;
        }

        /// <summary>The trip loop of a path (CSV 자국 윤곽 닫힌 곡선 · 착점 한 줄기): the out lane on one side of the axis, a U at the far end, the back lane on the
        /// other side - points from the start of the out lane to <paramref name="upTo"/> (0..1 of the whole loop).</summary>
        public static List<Vector3> Loop(Vector3 origin, Vector3 f, float ground, float a0, float far, float lane, float upTo, int n = 48)
        {
            var pts = new List<Vector3>(n + 1);
            Vector3 side = Vector3.Cross(Vector3.up, f);
            float straight = Mathf.Max(0f, far - a0), arc = Mathf.PI * lane, total = straight * 2f + arc;
            for (int k = 0; k <= n; k++)
            {
                float s = Mathf.Clamp01(k / (float)n * upTo) * total;
                Vector3 p;
                if (s <= straight) p = origin + f * (a0 + s) - side * lane;
                else if (s <= straight + arc) { float a = (s - straight) / Mathf.Max(1e-4f, arc) * Mathf.PI; p = origin + f * (far + Mathf.Sin(a) * lane * 0.8f) - side * lane * Mathf.Cos(a); }
                else p = origin + f * (far - (s - straight - arc)) + side * lane;
                p.y = ground;
                pts.Add(p);
            }
            return pts;
        }
    }
}
