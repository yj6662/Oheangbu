using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 6 (2026-10-27, the user's order "쇼케이스 방향이 낫다. 이 방식으로 다른 술식도 만들어 가자." / "잘 진행해둬"): the ㅅ book 가른다 -
    // P4 갓 낫 맛 삿 앗 · A4 것 넛 멋 섯 엇 · F4 곳 놋 못 솟 옷 · R4 굿 눗 뭇 숫 웃 (peaks 삿 섯 솟 숫 = metal).
    // ONE skeleton for the four frames, read only from Vol2SplitExecutor's facts on the scope clock:
    //   charge -> launch (the emitter's StartUs) -> path: the cut runs to the predicted first body (SingleCue), on through the bodies 삿 strips;
    //   fan / ring / attachment: the cut is down at the launch (the ring = a 10 s line, the attachment = a 6 s blade: the emitter's EndUs)
    //   -> the contacts: Instant / Second (the cut on the body, its body action once), Rear (섯's back cut), Residue (솟's metal on a walker), a refused Touch
    //   -> the SPLITS: the executor's own records (DataOf(cast).Splits: parent -> children, where, when, the halves placed either side of the cut line). The split
    //      materials belong to OTHER casts (the look never hears their events): the records are polled every tick, as 눈 polls Log.Burned
    //   -> the STRIPS: the executor's strip book (DataOf(cast).Strips + Strips.UntilOf: who is stripped until when; a recrossing renews; never a look timer)
    //   -> 솟's metal residue: this cast's own material (created / ended by its events)
    // Nothing here decides a target, a split, a strip or a lifetime. Presentation only: nothing here is read by a rule.

    /// <summary>The 가른다 skeleton of batch 6 (every frame). A glyph fills the hooks; the helpers give it the facts as numbers.</summary>
    public abstract class Vol2FsCut : Vol2FsRun
    {
        protected Frame Shape; protected int Seed; protected char Letter; protected Body Element; protected Color Acc;
        protected bool Launched, IsHit; protected float LaunchAt = -1f, ImpactRel = -1f, HitAt = -1f, EndRel = -1f, LastFrac;
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        // path: the cut to the predicted first body
        protected Vector3 From, HitPoint, AimPoint; protected bool HasTarget; protected long TargetId; protected Transform TargetT; protected Bounds TB;
        /// <summary>Path: how far along the path the first body stands (from the cast point), and the length the path may run.</summary>
        protected float AlongA = 4.6f, PathLen = 12f;
        // fan / ring (the spec's numbers)
        protected Vector3 Apex, F; protected float Depth, Half, R, Inner, Outer;
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        protected Vector3 OnBand(float ang, float r) { var p = Apex + Dir(ang) * r; p.y = Ground; return p; }
        protected float AngleOf(Vector3 p) => Vector3.SignedAngle(F, FlatN(p - Apex), Vector3.up) * Mathf.Deg2Rad;
        /// <summary>1 on the front arc .. 0 straight behind (the shoulder camera stands inside the ring).</summary>
        protected static float Front(float ang) => 1f - Vol2Fs.Smooth(100f, 165f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)));
        protected static Vector3 FlatN(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        protected Vector3 G(Vector3 p) { p.y = Ground; return p; }
        protected Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);
        protected long NowUs;
        /// <summary>The scope's 가른다 executor (its strip book and per-cast data); null outside a scope.</summary>
        protected Vol2SplitExecutor Ex;
        /// <summary>The ring line's / the attachment blade's end on the look clock (the emitter's EndUs); -1 = none (path / fan).</summary>
        protected float LineEndRel = -1f;
        protected bool LineEnded; protected float LineEndedAt = -1f;

        // ---- a body the cast touched ----
        public sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public float At; public string Kind; public bool BodyAction; public float Amount; public int Index; public object Data; }
        protected readonly List<Struck> Hits = new List<Struck>();
        /// <summary>Bodies the rules refused a paid touch (삿's second and third: FirstDamageOnly - they are still stripped; a fourth on ㅓ: TargetCap).</summary>
        protected readonly List<Struck> Refused = new List<Struck>();

        // ---- the strip book of this cast ----
        public sealed class Strip
        {
            public long Id; public Transform T; public Bounds B; public Vector3 Feet; public int Index;
            /// <summary>The first strip entry (rel), when the look shows it (the cut reaches the body), the live end (the book's UntilUs), how many entries (a recrossing renews).</summary>
            public float Born, ShowAt, Until; public int Count; public bool Shown, Ended; public float EndAt = -1f, RenewAt = -1f;
            /// <summary>The 0.4 the budget booked with it (only the cast's first strip).</summary>
            public decimal Value;
            public object Data;
        }
        protected readonly List<Strip> Strips = new List<Strip>();
        readonly Dictionary<long, Strip> _stripOf = new Dictionary<long, Strip>();
        int _stripSeen;

        // ---- the materials this cast cut in two (other casts' materials) ----
        public sealed class Cleave
        {
            public Vol2SplitRecord Rec; public Vol2Material Parent; public readonly List<Vol2Material> Kids = new List<Vol2Material>();
            /// <summary>Where it stood whole, its radius, the cut line's direction and its side (the halves stand either side of it), the halves now.</summary>
            public Vector3 Centre, CutDir, Side, PosA, PosB; public float Radius, HalfR; public bool Disc;
            public float At, ShowAt; public bool Shown, Ended; public float EndAt = -1f; public string Where = ""; public Body Body; public int Index; public int MatSeed;
            public object Data;
        }
        protected readonly List<Cleave> Cleaves = new List<Cleave>();
        int _splitSeen;

        // ---- 솟's metal residue (this cast's own material) ----
        protected Vol2Material Metal; protected float MetalAt = -1f, MetalEndAt = -1f; protected Vol2MaterialEnd MetalWhy = Vol2MaterialEnd.None;

        // ---- the brush tip's recent path ----
        protected readonly List<(Vector3 P, float T)> TipPath = new List<(Vector3, float)>(64);

        // ---- hooks ----
        protected abstract void OnBegin();
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>Path only: the cut runs to the predicted first body, f = 0..1 of that run.</summary>
        protected virtual void OnFly(float t, float f, float prev) { }
        /// <summary>The cut lands (path: at the impact clock on the first body; fan / ring / attachment: at the launch).</summary>
        protected abstract void OnHit(long nowUs);
        /// <summary>A paid contact on a body (Instant / Second / Rear / Residue). BodyAction = the body's own action landed with it (once per cast).</summary>
        protected virtual void OnStruck(Struck s) { }
        protected virtual void OnRefused(Struck s) { }
        /// <summary>A body is seen stripped (its guard and armour come off).</summary>
        protected virtual void OnStrip(Strip s) { }
        /// <summary>A stripped body's strip was renewed (a second ring crossing).</summary>
        protected virtual void OnStripRenew(Strip s) { }
        /// <summary>The strip book's UntilUs passed: the guard and armour come back.</summary>
        protected virtual void OnStripEnd(Strip s) { }
        /// <summary>A material is seen cut in two (the halves stand either side of the cut line).</summary>
        protected virtual void OnCleave(Cleave c) { }
        /// <summary>The halves are gone (both ended).</summary>
        protected virtual void OnCleaveEnd(Cleave c) { }
        protected virtual void OnMetal() { }
        protected virtual void OnMetalEnd() { }
        /// <summary>The ring line / attachment blade ended (the emitter's EndUs).</summary>
        protected virtual void OnLineEnd() { }
        /// <summary>Every frame after the landing: h = seconds since it.</summary>
        protected abstract void OnLive(float t, float h);

        public override void Began(long nowUs)
        {
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            Shape = Inst.Log.Glyph.Frame; Letter = Inst.Log.Glyph.Letter; Element = Inst.Log.Glyph.Body; Acc = Accent(Element);
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = FlatN(spec.Forward); if (spec.Forward.sqrMagnitude < 1e-6f) F = Fwd;
            Depth = Mathf.Max(1f, spec.FanDepth); Half = Mathf.Max(0.1f, spec.FanHalfAngleRad);
            R = Mathf.Max(0.5f, spec.RingRadius); Inner = spec.RingInner; Outer = Mathf.Max(spec.RingInner, spec.RingOuter);
            PathLen = Mathf.Max(1f, spec.PathLength);
            NowUs = nowUs;
            var co = Inst.Model.Coordinator;
            if (co != null && co.Verbs.TryGet(Verb.Split, out var ex)) Ex = ex as Vol2SplitExecutor;
            if (Inst.Log.Emitter is Vol2TimedEmitter te) LineEndRel = Inst.Rel(te.EndUs);
            else if (Inst.Log.Emitter is Vol2AttachmentHost ah) LineEndRel = Inst.Rel(ah.EndUs);
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
                    AlongA = Mathf.Max(0.5f, Vector3.Dot(G(TB.center) - G(Apex), F));
                }
                else { AimPoint = cue.FallbackPoint + Vector3.up * 1.0f; HitPoint = AimPoint; TB = new Bounds(cue.FallbackPoint + Vector3.up * 0.6f, Vector3.one * 1.2f); AlongA = Mathf.Max(0.5f, Vector3.Dot(G(cue.FallbackPoint) - G(Apex), F)); }
                Note = "launch target=" + TargetId + " impact=" + cue.ImpactClock.ToString("0.000") + "s";
            }
            OnLaunch(nowUs);
            if (Shape != Frame.Path) { IsHit = true; HitAt = LaunchAt; OnHit(nowUs); }
        }

        protected float Frac(long nowUs) => ImpactRel > LaunchRel ? Mathf.Clamp01((Now(nowUs) - LaunchRel) / (ImpactRel - LaunchRel)) : 1f;
        protected Vector3 CentreNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f).center : s.B.center;
        protected Vector3 FeetOf(Struck s) => s.T != null ? s.T.position : s.Feet;
        protected Bounds BodyNow(Strip s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
        protected Vector3 FeetOf(Strip s) => s.T != null ? s.T.position : s.Feet;
        protected Vector3 Shaft(float u) => Vector3.LerpUnclamped(Grip(), Tip(), u);
        protected Vector3 ShaftAxis { get { var d = Tip() - Grip(); return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up; } }
        protected Vector3 Sleeve() => Vector3.Lerp(Grip(), Inst.Origin + Vector3.up * 1.4f + Right * 0.22f, 0.35f);

        // ------------------------------------------------------------------------------------------------ the path's visible run (the executor's head is far faster: 36 m/s)

        /// <summary>Path: metres per second of the visible cut (the first body at the impact clock).</summary>
        protected float RunSpeed => AlongA / Mathf.Max(0.04f, ImpactRel - LaunchRel);
        /// <summary>Path: where the visible cut has run to (metres along the path), never past where the executor's head stopped.</summary>
        protected float RunAlong(float t)
        {
            if (!Launched || ImpactRel < 0f) return 0f;
            float a = Mathf.Max(0f, t - LaunchRel) * RunSpeed;
            var p = Inst.Path;
            float stop = p != null && p.Finished ? Mathf.Max(AlongA, p.HeadAlong) : PathLen;
            return Mathf.Min(a, stop);
        }
        /// <summary>Path: the look-clock time the visible cut reaches <paramref name="along"/> metres.</summary>
        protected float RunArrive(float along) => ImpactRel < 0f ? LaunchRel : LaunchRel + Mathf.Max(0f, along) / Mathf.Max(0.1f, RunSpeed);
        protected Vector3 OnPath(float along) => G(Apex + F * along);
        protected float AlongOf(Vector3 p) => Vector3.Dot(G(p) - G(Apex), F);

        // ------------------------------------------------------------------------------------------------ the executor's facts

        public override void Contact(Vol2ContactRecord rec)
        {
            if (rec == null || Root == null) return;
            string kind = rec.Kind ?? "";
            float at = Inst.Rel(rec.NowUs);
            var tg = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (kind == "Strip") { Watch(tr); PollStrips(); return; }
            if (kind == "Touch" && !rec.BudgetAccepted)
            {
                Watch(tr);
                var r = new Struck { Id = rec.TargetId, T = tr, Feet = tg != null ? tg.Position : Apex + F * rec.Along, At = at, Kind = "Refused:" + rec.BudgetReason, Index = Refused.Count };
                r.B = BodyBounds(tr, r.Feet, tg != null ? tg.Radius : 0.5f);
                Refused.Add(r); Note += " | refused " + rec.TargetId + " " + rec.BudgetReason;
                try { OnRefused(r); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (!rec.BudgetAccepted) return;
            if (kind != "Instant" && kind != "Second" && kind != "Rear" && kind != "Residue") return;
            Watch(tr);
            var s = new Struck { Id = rec.TargetId, T = tr, Feet = tg != null ? tg.Position : Apex + F * rec.Along, At = at, Kind = kind, BodyAction = rec.BodyEffect != null && rec.BodyEffect.Length > 0, Amount = (float)rec.Amount, Index = Hits.Count };
            s.B = BodyBounds(tr, s.Feet, tg != null ? tg.Radius : 0.5f);
            // the path's first body is seen hit when the visible cut lands on it; farther ones (삿) when the run passes them
            if (Shape == Frame.Path) s.At = rec.TargetId == TargetId && ImpactRel > 0f ? Mathf.Max(at, ImpactRel) : Mathf.Max(at, RunArrive(AlongOf(s.Feet)));
            Hits.Add(s);
            Note += " | " + kind + " " + rec.TargetId + " " + rec.Amount.ToString("0.###") + (s.BodyAction ? " [" + rec.BodyEffect + "]" : "") + " @" + at.ToString("0.00");
            _pendingHits.Add(s);
        }
        readonly List<Struck> _pendingHits = new List<Struck>();

        public override void MaterialCreated(Vol2Material m)
        {
            if (m == null || m.Cast != Inst.Cast || Root == null || Metal != null) return;
            if (m.Kind != Vol2MaterialKind.Residue) return;
            Metal = m; MetalAt = Mathf.Max(Inst.Rel(m.CreatedUs), LaunchRel);
            Note += " | metal " + m.Id + " life " + (m.LifeUs / 1e6).ToString("0.0") + "s";
            try { OnMetal(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        public override void MaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            if (m == null || Root == null || m != Metal || MetalEndAt >= 0f) return;
            EndMetal(why, Inst.Rel(m.EndedUs));
        }

        void EndMetal(Vol2MaterialEnd why, float at)
        {
            MetalEndAt = at; MetalWhy = why;
            Note += " | metal ended " + why + " at " + at.ToString("0.00");
            try { OnMetalEnd(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        Vol2SplitCastData Data => Ex != null ? Ex.DataOf(Inst.Cast) : null;

        void PollStrips()
        {
            var d = Data; if (d == null) return;
            for (; _stripSeen < d.Strips.Count; _stripSeen++)
            {
                var e = d.Strips[_stripSeen];
                float born = Inst.Rel(e.AtUs), until = Inst.Rel(e.UntilUs);
                if (_stripOf.TryGetValue(e.TargetId, out var s))
                {
                    s.Count++; s.Until = Mathf.Max(s.Until, until); s.RenewAt = born;
                    if (s.Ended) { s.Ended = false; s.EndAt = -1f; s.ShowAt = born; s.Shown = false; }     // a new crossing after the old strip ran out: shown again
                    else if (s.Shown) { Note += " | strip renewed " + e.TargetId + " @" + born.ToString("0.00"); try { OnStripRenew(s); } catch (System.Exception ex) { Debug.LogException(ex); } }
                    continue;
                }
                var tg = Target(e.TargetId); var tr = TargetTransform(e.TargetId);
                s = new Strip { Id = e.TargetId, T = tr, Born = born, Until = until, Count = 1, Value = e.Value, Index = Strips.Count };
                s.Feet = tg != null ? tg.Position : Apex + F * 3f;
                s.B = BodyBounds(tr, s.Feet, tg != null ? tg.Radius : 0.5f);
                s.ShowAt = Shape == Frame.Path ? (e.TargetId == TargetId && ImpactRel > 0f ? Mathf.Max(born, ImpactRel) : Mathf.Max(born, RunArrive(AlongOf(s.Feet)))) : born;
                Strips.Add(s); _stripOf[s.Id] = s;
                Watch(tr);
                Note += " | strip " + e.TargetId + " " + ((e.UntilUs - e.AtUs) / 1e6).ToString("0.0") + "s @" + born.ToString("0.00") + (e.Value > 0m ? " (0.4)" : "");
            }
        }

        void PollSplits()
        {
            var d = Data; if (d == null) return;
            for (; _splitSeen < d.Splits.Count; _splitSeen++)
            {
                var rec = d.Splits[_splitSeen];
                var c = new Cleave { Rec = rec, Parent = rec.Parent, At = Inst.Rel(rec.AtUs), Where = rec.Where ?? "", Body = rec.Parent != null ? rec.Parent.Body : Body.Earth, Index = Cleaves.Count };
                c.Kids.AddRange(rec.Children);
                c.MatSeed = rec.Parent != null ? (int)(rec.Parent.Id.Value % 9973) : Cleaves.Count;
                var fp = rec.Parent != null ? rec.Parent.Footprint : null;
                var kfp = rec.Children.Count > 0 ? rec.Children[0].Footprint : null;
                c.Disc = fp != null && fp.Kind == Vol2FootprintKind.Disc;
                c.PosA = fp != null ? G(fp.Origin) : G(Apex + F * 2f);
                c.PosB = kfp != null ? G(kfp.Origin) : c.PosA;
                c.Centre = (c.PosA + c.PosB) * 0.5f;
                c.HalfR = fp != null && c.Disc ? fp.Radius : 0.6f;
                c.Radius = c.Disc && Ex != null ? c.HalfR / Mathf.Max(0.1f, Ex.Tuning.HalfRadiusScale) : c.HalfR;
                // the cut line as the executor drew it (path / fan / ㅓ: the spec's forward or the swing; ring: the radius)
                c.CutDir = c.Where == "ring" ? FlatN(c.Centre - G(Apex)) : F;
                Vector3 ab = c.PosB - c.PosA; ab.y = 0f;
                c.Side = ab.sqrMagnitude > 1e-4f ? ab.normalized : Vector3.Cross(Vector3.up, c.CutDir);
                c.ShowAt = Shape == Frame.Path ? Mathf.Max(c.At, RunArrive(AlongOf(c.Centre))) : Mathf.Max(c.At, LaunchRel);
                Cleaves.Add(c);
                Note += " | split " + (rec.Parent != null ? rec.Parent.Id.ToString() : "?") + " by " + c.Where + " @" + c.At.ToString("0.00");
                try { CleaveCreated(c); } catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------------------------------------ the helpers a glyph draws with

        /// <summary>0 until shown .. 1 standing .. down over <paramref name="tail"/> s after the strip ended.</summary>
        protected float StripLife(Strip s, float t, float grow = 0.12f, float tail = 0.45f)
        {
            if (!s.Shown) return 0f;
            float a = Vol2Fs.Smooth(s.ShowAt, s.ShowAt + grow, t);
            return s.Ended ? a * (1f - Mathf.Clamp01((t - s.EndAt) / Mathf.Max(0.05f, tail))) : a;
        }
        /// <summary>1 at the strip .. 0 at the book's live end.</summary>
        protected float StripRemain01(Strip s, float t) => Mathf.Clamp01((s.Until - t) / Mathf.Max(0.1f, s.Until - Mathf.Min(s.ShowAt, s.Until - 0.1f)));
        /// <summary>0..1 over the template's warning window before the strip ends.</summary>
        protected float StripWarn(Strip s, float t)
        {
            if (!s.Shown || s.Ended) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            return Vol2Fs.Smooth(s.Until - w, s.Until, t);
        }
        protected float SinceStrip(Strip s, float t) => !s.Shown ? -1f : t - s.ShowAt;
        protected float SinceStripEnd(Strip s, float t) => !s.Ended ? -1f : t - s.EndAt;
        /// <summary>Seconds since a cleave was seen (-1 = not yet).</summary>
        protected float SinceCleave(Cleave c, float t) => !c.Shown ? -1f : t - c.ShowAt;
        /// <summary>The ring line / blade standing: 0..1 grown in <paramref name="grow"/> from the launch, down over <paramref name="tail"/> after the emitter's end.</summary>
        protected float LineLife(float t, float grow = 0.15f, float tail = 0.5f)
        {
            if (!Launched) return 0f;
            float a = Vol2Fs.Smooth(LaunchAt, LaunchAt + grow, t);
            return LineEnded ? a * (1f - Mathf.Clamp01((t - LineEndedAt) / Mathf.Max(0.05f, tail))) : a;
        }
        protected float LineWarn(float t)
        {
            if (LineEndRel < 0f || LineEnded) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            return Vol2Fs.Smooth(LineEndRel - w, LineEndRel, t);
        }
        protected float MetalLife(float t, float grow = 0.12f, float tail = 0.6f)
        {
            if (MetalAt < 0f) return 0f;
            float a = Vol2Fs.Smooth(MetalAt, MetalAt + grow, t);
            return MetalEndAt < 0f ? a : a * (1f - Mathf.Clamp01((t - MetalEndAt) / Mathf.Max(0.05f, tail)));
        }
        protected float MetalWarn(float t)
        {
            if (Metal == null || MetalEndAt >= 0f) return 0f;
            float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f;
            float e = Inst.Rel(Metal.ExpiresUs);
            return Vol2Fs.Smooth(e - w, e, t);
        }
        /// <summary>The strip of a body (null = not stripped by this cast).</summary>
        protected Strip StripOf(long id) => _stripOf.TryGetValue(id, out var s) ? s : null;
        protected float TipSpeed()
        {
            if (TipPath.Count < 2) return 0f;
            var a = TipPath[TipPath.Count - 1]; int i = TipPath.Count - 2;
            while (i > 0 && a.T - TipPath[i].T < 0.1f) i--;
            float dt = Mathf.Max(1e-3f, a.T - TipPath[i].T);
            return (a.P - TipPath[i].P).magnitude / dt;
        }

        // ------------------------------------------------------------------------------------------------ the shared pictures of the book (every glyph has them; the glyph colours / adds)

        // the halves of every cleave (earth walls: the stone slab cut in two - the lab's stand-in walls are built from the same halves, Vol2FsB6.WallHalf),
        // the stripped body's armour coming off and the countdown fuse round its feet, the gouged crack line of the cut (CSV 자국 윤곽: 아래로 파인 균열선)
        sealed class HalfLook { public Transform A, B; public Vector3 A0, B0; public Quaternion RotA, RotB; public float W, H; }
        ParticleSystem _shards, _sparks, _dust, _plates, _flash;
        Mesh _bookMesh, _bookGlowMesh; readonly Vol2FsStrips _book = new Vol2FsStrips(), _bookGlow = new Vol2FsStrips();
        Light _bookLight; float _bookLightUntil = -1f;

        /// <summary>The cut face / seam colour of this glyph (the accent, a little hotter).</summary>
        protected virtual Color CutGlow => Acc * 1.6f;
        /// <summary>How far apart the halves drift beyond the executor's placement (m) and lean out (deg).</summary>
        protected virtual float HalfGap => 0.16f;
        protected virtual float HalfLean => 7f;
        /// <summary>The shards of the stripped armour: count per strip.</summary>
        protected virtual int ArmourShards => 26;
        /// <summary>Draw the shared strip fuse / seam (a glyph may draw its own instead).</summary>
        protected virtual bool BookStrip => true;
        /// <summary>The body's own action on the exposed seam, drawn by the shared Vol2FsCutAct (bind / burn / push / wet / cut) when the contact carries it.</summary>
        protected virtual bool UseAct => true;
        protected Vol2FsCutAct Act;
        /// <summary>The ground direction of a body action (the push / the rootlets): away from the caster.</summary>
        protected virtual Vector3 ActDir(Struck s) => Shape == Frame.Path ? F : FlatN(FeetOf(s) - Apex);

        void BuildBook()
        {
            var root = Root.transform;
            var steel = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.04f, 0.05f, 0.07f));
            var plate = Kit.Custom("b6plate", () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_B6Plate" };
                m.SetColor("_BaseColor", new Color(0.09f, 0.085f, 0.08f)); m.SetFloat("_Metallic", 0.3f); m.SetFloat("_Smoothness", 0.55f);
                return m;
            });
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "B6Shards");
            _plates = Vol2Fs.Build(new Vol2FsPs { Mat = plate, Mesh = Kit.SharedMesh("shard1", () => Vol2FsB1.Shard(11)), Life0 = 0.9f, Life1 = 1.5f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.26f, Gravity = 2.4f, Drag = 0.3f, Spin = 600f, Max = 200 }, root, "B6Plates");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Acc, Color.white, 0.55f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 900 }, root, "B6Sparks");
            var dust = Vol2FsB3.Dust(new Color(0.42f, 0.3f, 0.24f, 1f), 0.5f);
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.5f, Drag = 1.2f, Gravity = -0.05f, Max = 200 }, root, "B6Dust");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Acc, Color.white, 0.25f), 2.2f, 1f)), Life0 = 0.06f, Life1 = 0.1f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.3f, Size1 = 0.45f, Max = 40 }, root, "B6Flash");
            foreach (var ps in new[] { _shards, _plates, _sparks, _dust, _flash }) ps.Play(false);
            var gouge = Kit.InkSolid("fs_stroke2", InkC, CutGlow, 0.18f, 0.45f, 0.3f, 1.4f);
            Vol2Fs.MeshObject(root, "B6Book", gouge, out _bookMesh);
            var glow = M("fs_streak", CutGlow, 2.4f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "B6BookGlow", Kit.Mat(glow), out _bookGlowMesh);
            _bookLight = Vol2Fs.PointLight(root, "B6Light", Color.Lerp(Acc, Color.white, 0.3f), 5f);
            if (UseAct) Act = new Vol2FsCutAct(Kit, root, Element, Seed, Acc);
        }

        void CleaveCreated(Cleave c)
        {
            // the halves exist from the split itself (the lab's whole wall is hidden that frame): adjacent, as the wall stood; they part at ShowAt
            if (c.Body != Body.Earth || !c.Disc) return;
            var dims = Vol2FsB6.WallDims(c.Radius);
            var h = new HalfLook { W = dims.x * 0.5f, H = dims.y };
            var mat = Vol2FsB6.WallMat(Kit);
            var ma = Vol2FsB6.HalfMesh(Kit, c.MatSeed, 0, dims); var mb = Vol2FsB6.HalfMesh(Kit, c.MatSeed, 1, dims);
            var ra = Vol2FsB1.Prop(Root.transform, "HalfA" + c.Index, ma, mat); var rb = Vol2FsB1.Prop(Root.transform, "HalfB" + c.Index, mb, mat);
            h.A = ra.transform; h.B = rb.transform;
            var rot = Vol2FsB6.WallRot(c.Side);
            h.RotA = rot; h.RotB = rot;
            h.A0 = c.Centre - c.Side * h.W * 0.5f; h.B0 = c.Centre + c.Side * h.W * 0.5f;
            h.A.SetPositionAndRotation(h.A0, rot); h.B.SetPositionAndRotation(h.B0, rot);
            c.Data = h;
        }

        void ShowCleave(Cleave c, float t)
        {
            c.Shown = true;
            HitStop(t, 0.06f);
            Vector3 at = c.Centre + Vector3.up * 0.5f;
            if (_flash != null) Vol2FsB1.EmitV(_flash, at, Vector3.zero, 0.5f, 0.08f);      // r2: was 1.3 (a white disc from the shoulder)
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_sparks, at + Vector3.up * Random.Range(-0.4f, 0.5f), (c.Side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(4f, 12f), -1f, -1f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_dust, G(c.Centre) + Random.insideUnitSphere * 0.4f + Vector3.up * 0.2f, (c.Side * (k % 2 == 0 ? 1f : -1f) * 1.2f + Vector3.up * 0.6f), -1f, -1f);
            FlashLight(at, 6f, t + 0.25f);
            Note += " | cleave " + c.Index + " shown @" + t.ToString("0.00");
            try { OnCleave(c); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void ShowStrip(Strip s, float t)
        {
            s.Shown = true;
            HitStop(t, 0.08f);
            var b = BodyNow(s);
            Vector3 c = b.center, side = Vector3.Cross(Vector3.up, FlatN(CamPos - c));
            if (_flash != null) Vol2FsB1.EmitV(_flash, c, Vector3.zero, 0.55f, 0.08f);     // r2: was 1.6 (a white disc)
            for (int k = 0; k < ArmourShards; k++)
            {
                Vector3 o = Random.insideUnitSphere; o.y = Mathf.Abs(o.y) * 0.6f + 0.2f;
                Vol2FsB1.EmitV(_plates, c + Vector3.Scale(o, b.extents), (side * (k % 2 == 0 ? 1f : -1f) * Random.Range(1.5f, 4.5f) + Vector3.up * Random.Range(1.5f, 4f) + Random.insideUnitSphere), -1f, -1f);
                if (k % 2 == 0) Vol2FsB1.EmitV(_shards, c + Vector3.Scale(o, b.extents) * 0.8f, (side * (k % 4 == 0 ? 1f : -1f) * Random.Range(3f, 8f) + Vector3.up * Random.Range(0.5f, 3f)), -1f, -1f);
            }
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_sparks, c, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.8f + Vector3.up * 0.4f).normalized * Random.Range(4f, 13f), -1f, -1f);
            FlashLight(c, 7f, t + 0.3f);
            try { OnStrip(s); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void EndStrip(Strip s, float t)
        {
            s.Ended = true; s.EndAt = Mathf.Max(s.ShowAt, s.Until);
            var b = BodyNow(s);
            // the guard comes back: the plates fly IN (a short dull clang)
            for (int k = 0; k < 14; k++)
            {
                Vector3 from = b.center + Random.onUnitSphere * 1.1f;
                Vol2FsB1.EmitV(_plates, from, (b.center - from) * 3.2f, 0.12f, 0.3f);
            }
            if (_flash != null) Vol2FsB1.EmitV(_flash, b.center, Vector3.zero, 0.4f, 0.08f);
            Note += " | strip ended " + s.Id + " at " + s.EndAt.ToString("0.00");
            try { OnStripEnd(s); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected void FlashLight(Vector3 at, float intensity, float until)
        {
            if (_bookLight == null) return;
            _bookLight.transform.position = at; _bookLight.intensity = Mathf.Max(_bookLight.intensity, intensity); _bookLightUntil = Mathf.Max(_bookLightUntil, until);
        }

        /// <summary>The shared particles (a glyph emits into them too).</summary>
        protected ParticleSystem BookSparks => _sparks;
        protected ParticleSystem BookShards => _shards;
        protected ParticleSystem BookDust => _dust;
        protected ParticleSystem BookFlash => _flash;

        void DrawBook(float t)
        {
            var cam = CamPos;
            _book.Clear(); _bookGlow.Clear();
            // the halves part: from adjacent to the executor's places (+ a gap), leaning out, a glowing cut face down the middle
            foreach (var c in Cleaves)
            {
                var h = c.Data as HalfLook;
                float since = SinceCleave(c, t);
                float alive = c.Ended ? 1f - Mathf.Clamp01((t - c.EndAt) / 0.6f) : 1f;
                if (h != null)
                {
                    float part = since < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(since / 0.22f));
                    float sink = 1f - alive;
                    Vector3 a1 = c.PosA - c.Side * HalfGap, b1 = c.PosB + c.Side * HalfGap;
                    Vector3 pa = Vector3.LerpUnclamped(h.A0, a1, part) + Vector3.down * h.H * sink, pb = Vector3.LerpUnclamped(h.B0, b1, part) + Vector3.down * h.H * sink;
                    Vector3 axis = Vector3.Cross(Vector3.up, c.Side);
                    h.A.SetPositionAndRotation(pa, Quaternion.AngleAxis(HalfLean * part, axis) * h.RotA);
                    h.B.SetPositionAndRotation(pb, Quaternion.AngleAxis(-HalfLean * part, axis) * h.RotB);
                    h.A.gameObject.SetActive(alive > 0.01f); h.B.gameObject.SetActive(alive > 0.01f);
                    if (since >= 0f)
                    {
                        // the two cut faces glow hot and cool down (seen through the gap)
                        float glow = (1f - Vol2Fs.Smooth(0.1f, 1.6f, since)) * alive;
                        if (glow > 0.01f)
                        {
                            Vector3 fa = pa + c.Side * (h.W * 0.5f) * 0.98f, fb = pb - c.Side * (h.W * 0.5f) * 0.98f;
                            _bookGlow.Strip(new[] { fa + Vector3.up * 0.05f, fa + Vector3.up * h.H * 0.92f }, new[] { 0.07f, 0.05f }, null, Color.white, new[] { glow, glow * 0.7f }, 0.3f + c.Index * 0.1f, cam);
                            _bookGlow.Strip(new[] { fb + Vector3.up * 0.05f, fb + Vector3.up * h.H * 0.92f }, new[] { 0.07f, 0.05f }, null, Color.white, new[] { glow, glow * 0.7f }, 0.5f + c.Index * 0.1f, cam);
                        }
                    }
                }
                // the cut line through the material on the ground (whatever its body): a gouge across its whole footprint
                if (since >= 0f)
                {
                    float r = c.Radius * 1.25f, life = alive * Vol2Fs.Smooth(0f, 0.08f, since);
                    Vector3 a = G(c.Centre) - c.CutDir * r, b = G(c.Centre) + c.CutDir * r;
                    Vol2FsB6.Crack(_book, a, b, Ground, 0.16f, 0f, 1f, life * 0.95f, 0.21f + c.Index * 0.13f, c.Index * 7 + 3, 0.1f);
                    Vol2FsB6.Crack(_bookGlow, a, b, Ground, 0.05f, 0f, 1f, life * (1f - Vol2Fs.Smooth(0.3f, 2.5f, since)) * 0.9f, 0.37f + c.Index * 0.13f, c.Index * 7 + 3, 0.1f, 0.012f);
                }
            }
            // the stripped bodies: an open seam on the body + the countdown fuse round the feet (the book's live end), flickering in the warning
            if (BookStrip)
                foreach (var s in Strips)
                {
                    float life = StripLife(s, t); if (life <= 0.01f) continue;
                    var b = BodyNow(s);
                    float warn = StripWarn(s, t), rem = s.Ended ? 0f : StripRemain01(s, t);
                    float flick = warn > 0f ? (Mathf.Sin(t * 42f) > 0f ? 1f : 0.4f) : 1f;
                    float since = SinceStrip(s, t), open = Vol2Fs.Smooth(0f, 0.1f, since);
                    float pulse = 0.75f + 0.25f * Mathf.Sin(t * 9f + s.Index);
                    Vol2FsB6.Seam(_bookGlow, b, cam, open, life * flick * pulse, 0.11f + s.Index * 0.17f, s.Index * 13 + 5);
                    Vol2FsB6.Seam(_book, b, cam, open * 1.05f, life * 0.8f, 0.71f + s.Index * 0.17f, s.Index * 13 + 5, 2.6f);
                    float rr = Mathf.Max(b.extents.x, b.extents.z) + 0.38f;
                    if (!s.Ended) Vol2FsB5.Countdown(_bookGlow, G(FeetOf(s)), Ground, rr, 0.055f * (1f + 0.7f * warn), rem, life * flick * 0.9f, 0.43f + s.Index * 0.1f, cam, Mathf.Atan2(-FlatN(CamPos - b.center).x, -FlatN(CamPos - b.center).z), 44);
                    // the renewal (a second crossing): a quick second flash ring
                    if (s.RenewAt > 0f && t - s.RenewAt < 0.35f) Vol2FsB5.Halo(_bookGlow, b.center, FlatN(cam - b.center), rr + 0.2f * (t - s.RenewAt) / 0.35f, 0.05f, 1f - (t - s.RenewAt) / 0.35f, 0.9f, cam, 28);
                }
            _book.Write(_bookMesh); _bookGlow.Write(_bookGlowMesh);
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
            PollStrips(); PollSplits();
            // the contacts, each when the visible cut reaches its body
            for (int i = _pendingHits.Count - 1; i >= 0; i--)
            {
                var s = _pendingHits[i];
                if (t < s.At) continue;
                _pendingHits.RemoveAt(i);
                if (s.BodyAction && Act != null) { try { Act.Begin(s.T, s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B, FeetOf(s), t, ActDir(s)); } catch (System.Exception e) { Debug.LogException(e); } }
                try { OnStruck(s); } catch (System.Exception e) { Debug.LogException(e); }
            }
            foreach (var c in Cleaves)
            {
                if (!c.Shown && t >= c.ShowAt) ShowCleave(c, t);
                if (!c.Ended)
                {
                    bool gone = c.Parent == null || !c.Parent.Alive; foreach (var k in c.Kids) if (k != null && k.Alive) gone = false;
                    if (gone) { c.Ended = true; c.EndAt = t; try { OnCleaveEnd(c); } catch (System.Exception e) { Debug.LogException(e); } }
                }
            }
            foreach (var s in Strips)
            {
                // the live end: the book's own UntilUs for this body (a strip never shortens a running one; another cast may lengthen it)
                var tg = Target(s.Id);
                if (Ex != null && tg != null) { long u = Ex.Strips.UntilOf(tg); if (u > 0) s.Until = Mathf.Max(s.Until, Inst.Rel(u)); }
                if (!s.Shown && !s.Ended && t >= s.ShowAt) ShowStrip(s, t);
                if (s.Shown && !s.Ended && t >= s.Until) EndStrip(s, t);
            }
            if (Metal != null && MetalEndAt < 0f && !Metal.Alive) EndMetal(Metal.End, Inst.Rel(Metal.EndedUs));
            if (!LineEnded && LineEndRel >= 0f && (t >= LineEndRel || (Inst.Log.Emitter != null && Inst.Log.Emitter.Finished && ended)))
            {
                LineEnded = true; LineEndedAt = Mathf.Min(t, Mathf.Max(LineEndRel, LaunchAt));
                try { OnLineEnd(); } catch (System.Exception e) { Debug.LogException(e); }
            }
            if (IsHit) OnLive(t, t - HitAt);
            DrawBook(t);
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
            if (_pendingHits.Count > 0) return false;
            if (Act != null && Act.Busy(t) && t < EndRel + 8f) return false;
            foreach (var s in Strips) { if (!s.Ended) return false; by = Mathf.Max(by, s.EndAt + 0.5f); }
            if (LineEndRel >= 0f) { if (!LineEnded) return false; by = Mathf.Max(by, LineEndedAt + 0.5f); }
            if (Metal != null) { if (MetalEndAt < 0f) return false; by = Mathf.Max(by, MetalEndAt + 0.7f); }
            // the halves: until they end, at most 2.5 s after the rest (then they sink with the look)
            float rest = by;
            foreach (var c in Cleaves) { if (!c.Ended && t < rest + 2.5f) return false; if (c.Ended) by = Mathf.Max(by, c.EndAt + 0.6f); }
            return t > by + Tail && (!ParticlesAlive() || t > by + Tail + 2.5f);
        }
    }

    /// <summary>The skeleton's plain look (a placeholder until a glyph has its own): the body's accent cut (a thin bright line along the path / a fan arc / the ring /
    /// a blade on the brush), a flash on every struck body, the shared halves / strip book. Also proves the facts chain.</summary>
    public class Vol2FsCutPlain : Vol2FsCut
    {
        Mesh _lineMesh; readonly Vol2FsStrips _line = new Vol2FsStrips();
        ParticleSystem _burst;
        protected override void OnBegin()
        {
            var root = Root.transform;
            var l = M("fs_streak", Acc, 2.6f, 1f); l.NoiseAmt = 0.2f; l.Soft = 0.03f;
            Vol2Fs.MeshObject(root, "PlainCut", Kit.Mat(l), out _lineMesh);
            _burst = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_streak", Acc, 2.5f, 1f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.1f, Drag = 1.5f, Stretch = true, Max = 300 }, root, "Burst");
            _burst.Play(false);
        }
        protected override void OnCharge(float t, float k) { }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }
        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_burst, CentreNow(s), 40); }
        protected override void OnLive(float t, float h)
        {
            var cam = CamPos; _line.Clear();
            float life = 1f - Vol2Fs.Smooth(LaunchAt + 0.4f, LaunchAt + 1.2f, t);
            switch (Shape)
            {
                case Frame.Path: { float a = RunAlong(t); if (a > 0.1f) Vol2FsB6.Crack(_line, OnPath(0.3f), OnPath(a), Ground, 0.06f, 0f, 1f, Mathf.Max(0.3f, life), 0.3f, Seed, 0.12f); break; }
                case Frame.Fan: Vol2FsB4A.GroundArc(_line, Apex, Ground, Depth * 0.8f, 0.08f, Mathf.Atan2(F.z, F.x) - Half, Half * 2f, life, 0.4f, cam); break;
                case Frame.Ring: Vol2FsB4A.GroundArc(_line, Apex, Ground, R, 0.06f, 0f, Mathf.PI * 2f, LineLife(t) * 0.8f, 0.4f, cam, 64); break;
                default: _line.Strip(new[] { Shaft(0.3f), Shaft(1.15f) }, new[] { 0.03f, 0.06f }, null, Color.white, new[] { LineLife(t), LineLife(t) }, 0.4f, cam); break;
            }
            _line.Write(_lineMesh);
        }
    }

    /// <summary>Batch 6 helpers: the stand-in wall of the lab (whole = two halves side by side, the same meshes the look parts), the gouged crack line, the body seam.</summary>
    public static class Vol2FsB6
    {
        /// <summary>A disc material of radius r stands as a wall this wide (x), high (y) and thick (z).</summary>
        public static Vector3 WallDims(float r) => new Vector3(Mathf.Max(0.6f, r * 2f), Mathf.Clamp(r * 1.25f, 0.55f, 1.1f), 0.34f);

        /// <summary>The wall's rotation: its long side along <paramref name="side"/> (the halves part along it).</summary>
        public static Quaternion WallRot(Vector3 side)
        {
            side.y = 0f; if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            return Quaternion.LookRotation(Vector3.Cross(side.normalized, Vector3.up), Vector3.up);
        }

        /// <summary>One half of a wall (0 = the -side half, 1 = the +side half): a chipped slab half as wide, pivot at its bottom middle; the kit owns it.</summary>
        public static Mesh HalfMesh(Vol2FsKit kit, int seed, int half, Vector3 dims)
            => kit.SharedMesh("b6half|" + seed + "|" + half + "|" + dims.x.ToString("0.00") + "|" + dims.y.ToString("0.00"), () => Vol2FsB1.Slab(seed * 2 + half + 1, new Vector3(dims.x * 0.5f, dims.y, dims.z), 0.95f));

        /// <summary>Weathered red-brown field stone (the 적로 walls).</summary>
        public static Material WallMat(Vol2FsKit kit) => Vol2FsB1.Lit(kit, "fs_rock", new Color(0.6f, 0.52f, 0.46f), 0f, 0.12f);

        /// <summary>The lab's stand-in of a standing earth material (a wall) - built from the same two halves the look parts (Vol2FsCut), so the cut has no pop.
        /// LAB-SIMULATED: in the game the material's own look stands there. Owner: the caller (destroy the returned object).</summary>
        public static GameObject WallProxy(Vol2FsKit kit, string name, Vector3 centre, Vector3 side, float radius, long materialId)
        {
            var dims = WallDims(radius); int seed = (int)(materialId % 9973);
            var go = new GameObject(name);
            var rot = WallRot(side); side = (rot * Vector3.right).normalized;
            for (int h = 0; h < 2; h++)
            {
                var r = Vol2FsB1.Prop(go.transform, "Half" + h, HalfMesh(kit, seed, h, dims), WallMat(kit));
                r.transform.SetPositionAndRotation(centre + side * dims.x * 0.25f * (h == 0 ? -1f : 1f), rot);
            }
            return go;
        }

        /// <summary>A gouged crack line on the ground from a to b (the CSV outline 아래로 파인 균열선): a jagged flat strip, revealed u0..u1, widest in its middle.</summary>
        public static void Crack(Vol2FsStrips s, Vector3 a, Vector3 b, float ground, float width, float u0, float u1, float life, float rnd, int seed, float jag = 0.12f, float lift = 0.02f, int n = 0)
        {
            if (life <= 0.01f || u1 <= u0) return;
            Vector3 d = b - a; d.y = 0f; float len = d.magnitude; if (len < 0.05f) return;
            Vector3 f = d / len, side = Vector3.Cross(Vector3.up, f);
            if (n <= 0) n = Mathf.Clamp(Mathf.CeilToInt(len / 0.18f), 4, 80);
            int k0 = Mathf.FloorToInt(u0 * n), k1 = Mathf.CeilToInt(u1 * n);
            var pts = new List<Vector3>(); var wd = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
            for (int k = k0; k <= k1; k++)
            {
                float u = Mathf.Clamp(k / (float)n, u0, u1);
                float j = (Vol2Fs.Hash(seed, k) - 0.5f) * 2f * jag * (k == 0 || k == n ? 0f : 1f);
                Vector3 p = a + f * len * u + side * j; p.y = ground + lift;
                pts.Add(p); sd.Add(side);
                float taper = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * 0.75f + 0.25f;
                wd.Add(width * taper * (0.7f + 0.6f * Vol2Fs.Hash(seed, k + 300))); al.Add(life);
            }
            if (pts.Count >= 2) s.Strip(pts, wd, sd, Color.white, al, rnd, Vector3.zero, u0 * len * 0.6f, u1 * len * 0.6f);
        }

        /// <summary>The open seam of a stripped body: a jagged diagonal line across the body facing the camera (its guard cut open), <paramref name="open"/> 0..1.</summary>
        public static void Seam(Vol2FsStrips s, Bounds b, Vector3 cam, float open, float life, float rnd, int seed, float widthK = 1f)
        {
            if (life <= 0.01f || open <= 0.01f) return;
            Vector3 c = b.center, view = (cam - c); view.y = 0f; view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, view);
            float ex = Mathf.Max(b.extents.x, b.extents.z);
            Vector3 front = c + view * ex * 0.85f;
            Vector3 a = front + right * ex * 0.75f + Vector3.up * b.extents.y * 0.85f, e = front - right * ex * 0.65f - Vector3.up * b.extents.y * 0.6f;
            int n = 9; var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);
                Vector3 p = Vector3.Lerp(a, e, u * open);
                p += Vector3.Cross(view, (e - a).normalized) * (Vol2Fs.Hash(seed, k) - 0.5f) * 0.12f;
                pts[k] = p; wd[k] = 0.09f * widthK * Mathf.Sin(Mathf.Clamp01(u * 0.95f + 0.03f) * Mathf.PI); al[k] = life;
            }
            s.Strip(pts, wd, null, Color.white, al, rnd, cam, 0f, 1f);
        }

        /// <summary>A blade / leaf spike: base at the origin, tip at (0, 1, 0), widest (<paramref name="width"/>) at <paramref name="waist"/> of its height, <paramref name="thick"/>
        /// through; four flat faces (a diamond section) so its edges catch the light. Scale it (x, height, z).</summary>
        public static Mesh Spike(float width = 0.24f, float thick = 0.05f, float waist = 0.32f, float curl = 0f)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            int n = 6; var L = new Vector3[n + 1]; var Rr = new Vector3[n + 1]; var Fr = new Vector3[n + 1]; var Bk = new Vector3[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n;
                float w = u < waist ? Mathf.Lerp(0.35f, 1f, u / waist) : Mathf.Lerp(1f, 0f, Mathf.Pow((u - waist) / (1f - waist), 0.8f));
                Vector3 c = new Vector3(0f, u, curl * u * u);
                L[k] = c + Vector3.left * width * 0.5f * w; Rr[k] = c + Vector3.right * width * 0.5f * w; Fr[k] = c + Vector3.back * thick * 0.5f * w; Bk[k] = c + Vector3.forward * thick * 0.5f * w;
            }
            void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float u1)
            {
                int s = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(new Vector2(0f, u0)); uv.Add(new Vector2(1f, u0)); uv.Add(new Vector2(1f, u1)); uv.Add(new Vector2(0f, u1));
                idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2);
            }
            for (int k = 0; k < n; k++)
            {
                float u0 = k / (float)n, u1 = (k + 1) / (float)n;
                Q(L[k], Fr[k], Fr[k + 1], L[k + 1], u0, u1); Q(Fr[k], Rr[k], Rr[k + 1], Fr[k + 1], u0, u1);
                Q(Rr[k], Bk[k], Bk[k + 1], Rr[k + 1], u0, u1); Q(Bk[k], L[k], L[k + 1], Bk[k + 1], u0, u1);
            }
            var m = new Mesh { name = "FsSpike" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A thin-to-wide crescent slash through <paramref name="c"/> facing the camera (Phantom Blade principle 2): from a to b bowed by <paramref name="bow"/>.</summary>
        public static void Slash(Vol2FsStrips s, Vector3 a, Vector3 b, Vector3 bow, float width, float reveal, float life, float rnd, Vector3 cam, int n = 14)
        {
            if (life <= 0.01f || reveal <= 0.01f) return;
            var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1) * reveal;
                pts[k] = Vector3.Lerp(a, b, u) + bow * 4f * u * (1f - u);
                float w = Mathf.Sin(Mathf.Pow(Mathf.Clamp01(u / Mathf.Max(0.01f, reveal)), 0.8f) * Mathf.PI);
                wd[k] = width * Mathf.Max(0.04f, w); al[k] = life;
            }
            s.Strip(pts, wd, null, Color.white, al, rnd, cam, 0f, 1f);
        }
    }
}
