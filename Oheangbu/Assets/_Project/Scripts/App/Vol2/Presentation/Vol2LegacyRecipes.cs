using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V9 (2026-10-25, "기존 투사체 방식이 전부 낫다"): the vol.2 20 no-batchim glyphs (P0 A0 F0 R0 x five bodies) LOOK like the old main game again.
    // These recipes are the PURE half of the bridge: they draw no ink mesh of their own (Write writes nothing, so the vol.2 view never rents a mesh for them);
    // on the vol.2 executor events (cast commit, launch, contact approved, end) they ask a stage (IVol2LegacyStage, Unity side: Vol2LegacyStage) to play the
    // glyph's OLD visuals - the swing of the great brush, the brush-tip ink, the glyph's own body from the main game's visual set (projectile / area / guard /
    // ward) and its impact. Every time comes from the scope clock and the executor's own objects; nothing here decides or delays a hit (visuals never affect
    // damage). The launch moment is the emitter's StartUs: at the commit, or at the swing apex when the TEST launch switch is on (Vol2ShapeTuning.LaunchAtApex).
    // A fixture drives these recipes with a recording stage (no Unity object); with no stage they still keep their bookkeeping and end on the cast's end.

    /// <summary>What kind of old body a cue asks for (the old route's presentation of the four 한 번 frames). Step 4 (the batchim glyphs): Self = a glyph body the
    /// old route showed on the caster (a buff / sword / companion / heal letter: drawn as a lasting part, the cast has no flying body); Domain = a ㅜ glyph's own cue
    /// body at the caster's feet (the old 결계 / field letters; its rim is a lasting part).</summary>
    public enum Vol2LegacyKind { Single = 0, Area = 1, Guard = 2, Ward = 3, Self = 4, Domain = 5 }

    /// <summary>Step 4: which field of a donor glyph's OLD profile (the main game's glyph table, read only) a part is made from.</summary>
    public enum Vol2LegacyField
    {
        /// <summary>Zone308Prefab: the 결계 rims PF_V308d_* and the zone floors PF_V308z_* (Vfx308ZoneBody).</summary>
        Zone = 0,
        /// <summary>Mark308Prefab: the install seals PF_V308m_* (Vfx308ZoneBody, sits on a body).</summary>
        Mark = 1,
        /// <summary>AreaStainPrefab: the foot stains PF_V308b_stain_* (one particle, held as long as the material lives).</summary>
        Stain = 2,
        /// <summary>The donor's glyph body played as a lasting aura on the caster (its Aura308Prefab: buff band, tree, sword, companion).</summary>
        Aura = 3,
        /// <summary>Guard308Prefab: the guard membrane PF_V308d_guard_* (three upright strokes), held as long as the state lives.</summary>
        Guard = 4,
        /// <summary>AreaContactPrefab: the element's hit with its final's marker (one shot).</summary>
        Contact = 5,
        /// <summary>Burst308Prefab: the short pictures (fate / buff-hit / field birth, one shot).</summary>
        Burst = 6,
        /// <summary>Derived308Prefab: the derived shot of the second-strike letters (flies from a point to a body).</summary>
        Derived = 7,
        /// <summary>The donor glyph's own body flown from a point to a point.</summary>
        Body = 8,
        /// <summary>The old 풀림 (InkUnravel308, SPEC 11.25): ink drops leave the points of a line From -> To and are pulled into the swinging brush tip (the back leg of a return).</summary>
        Unravel = 9,
        /// <summary>The old ward membrane of the element (the ward letter 구 누 무 수 우 = donor; EAWardRuntime's presentation prefab): ink membranes standing on a ring of
        /// ground ink, held as long as the state lives (a lasting aura, ended by DryAura308), its radius the state's.</summary>
        Ward = 10,
    }

    /// <summary>Step 4: one LASTING old part of a vol.2 verb state (a residue's rim, a mark's stain, a seed's seal, a strip's strokes, a carried aura, a moving ring).
    /// Its end is the executor's event (the recipe calls Close); the part has no life of its own.</summary>
    public struct Vol2LegacyPart
    {
        public char Donor, Fallback;
        public Vol2LegacyField Field;
        public Body Body;
        public Vector3 Point, Facing;
        public float Radius;
        /// <summary>The body the part sits on and follows (0 = none).</summary>
        public long HostId;
        /// <summary>The part goes with the caster.</summary>
        public bool OnCaster;
        /// <summary>When the executor already scheduled the end (a strip's UntilUs - now), its length: the old part's lifetime-normalised look is fitted to it (0 = none).
        /// The end itself is still the executor's event.</summary>
        public float Seconds;
        public string Role;
        /// <summary>Step 5a: the shape of the footprint the part shows (read from the material's footprint or the cast's spec; presentation only): a ring (its band),
        /// or a fan (its apex on the ground, forward, depth, half angle in degrees) - a pattern laid on the state follows it (a fan's pattern is clipped to the fan).</summary>
        public bool IsRing, IsFan;
        public Vector3 FanApex, FanForward;
        public float FanDepth, FanHalfAngle;
    }

    /// <summary>Step 4: one short old picture (a hit, a burst, a derived shot, the back leg's body). It plays out on its own (a beat, never a state).</summary>
    public struct Vol2LegacyPulse
    {
        public char Donor, Fallback;
        public Vol2LegacyField Field;
        public Body Body;
        public Vector3 From, To;
        /// <summary>The body it lands on (0 = a point).</summary>
        public long TargetId;
        /// <summary>A flight's clock (Derived / Body), 0 = the field's own.</summary>
        public float Seconds;
        public string Role;
    }

    /// <summary>One request to the stage: the glyph, its old category and everything the old route handed its body, read from the vol.2 cast.</summary>
    public struct Vol2LegacyCue
    {
        public char Letter;
        public Body Body;
        public Vol2LegacyKind Kind;
        public Vector3 Origin, Forward;
        /// <summary>The body the vol.2 shape will touch first (predicted from the same pure geometry the emitter runs), or 0 / null when none.</summary>
        public long TargetId;
        public Vector3 TargetPoint;
        public bool HasTarget;
        /// <summary>Single: seconds from the launch to the vol.2 contact (the path head's travel), the old SetImpactClock. 0 = none.</summary>
        public float ImpactClock;
        /// <summary>Single without a target: where the body flies to (the end of the vol.2 path).</summary>
        public Vector3 FallbackPoint;
        /// <summary>Guard: the vol.2 parry window (seconds), the bright part of the old guard clock.</summary>
        public float GuardWindow;
        /// <summary>Ward: the vol.2 ring's centre-line radius (the old membrane is drawn on the band the ring judges).</summary>
        public float RingRadius;
        public float PathLength, FanDepth;
    }

    /// <summary>The swing request: the old category's motion and when its apex falls.</summary>
    public struct Vol2LegacySwing
    {
        public char Letter;
        public Body Body;
        public Vol2LegacyKind Kind;
        /// <summary>Seconds from now to the apex the brush comes down at (the launch for a waiting category).</summary>
        public float ToApex;
        /// <summary>The old ward lead (the short slam strikes this soon); 0 for the others.</summary>
        public bool FromClipStart;
        public float Distance;
    }

    /// <summary>The Unity side of the bridge. Plays the OLD visuals; answers nothing a rule could read.</summary>
    public interface IVol2LegacyStage
    {
        /// <summary>The body begins (the old adapter's SpawnPattern at the apex). 0 = nothing shown.</summary>
        int Begin(in Vol2LegacyCue cue);
        /// <summary>A budget-approved contact of the cast (bookkeeping only: the old body lands on its own clock).</summary>
        void Contact(int handle, long targetId, Vector3 point);
        /// <summary>Is the body still on screen (its own life not over)?</summary>
        bool Alive(int handle);
        /// <summary>Drop the body (immediate = the lab reset; otherwise it is left to finish its own life).</summary>
        void End(int handle, bool immediate);
        /// <summary>The swing motion and the brush-tip ink of the old route (presentation only, never a hit).</summary>
        void Swing(in Vol2LegacySwing swing);

        // ---- step 4: the verb states of the batchim glyphs ----
        /// <summary>Lays a lasting part (0 = nothing shown: no donor part, no Play).</summary>
        int Lay(in Vol2LegacyPart part);
        /// <summary>Moves / resizes a part (a carried material, a growing fan, a ring going out and back).</summary>
        void Place(int part, Vector3 point, float radius);
        /// <summary>The expiry warning: the part pales (once).</summary>
        void Warn(int part);
        /// <summary>Seconds before its scheduled end a part warns (the old prefab's own LateBefore when it has one, else 0 = the recipe's).</summary>
        float WarnLead(int part);
        /// <summary>The executor ended what the part shows: it goes the way the end reason says (immediate = the lab reset: at once).</summary>
        void Close(int part, Vol2EndCode code, Vector3 direction, bool immediate);
        /// <summary>Is anything of the part still on screen (its drying included)?</summary>
        bool Standing(int part);
        /// <summary>One short old picture.</summary>
        void Pulse(in Vol2LegacyPulse pulse);
    }

    /// <summary>Shared logic of the four bridge recipes.</summary>
    public abstract class Vol2LegacyRecipeBase : Vol2VfxRecipe
    {
        /// <summary>The bookkeeping part's kind (the recipe's own number).</summary>
        public const int KindState = 900;
        /// <summary>How long after the cast ended a body may still be on screen before the instance lets go of it anyway (its own life normally ends first).</summary>
        public const float HoldAfterEndSeconds = 4f;
        /// <summary>A swing whose apex is at the commit (no launch delay) is still shown coming down: the clip starts this soon before its apex (stroke20 used the same).</summary>
        public const float MinToApex = 0.06f;

        public abstract Vol2LegacyKind LegacyKind { get; }
        /// <summary>When the body begins, from the cast's own start and launch (scope clock).</summary>
        protected virtual long SpawnUs(Vol2VfxInstance inst) => LaunchUs(inst);
        /// <summary>false = the cast shows no flying / standing glyph body of its own (an ㅓ batchim glyph: its old picture is the aura part on the caster).</summary>
        protected virtual bool HasBody => true;

        /// <summary>Step 4: the kind of a lasting part (Aux = the stage handle, Mat = the material it shows or null, EndSchedule = the executor's scheduled end when it has
        /// no material, F1 = 1 once warned, Pos / Dir = where it stands). Ended (EndUs) = closed by an executor event.</summary>
        public const int KindLay = 901;

        public static long LaunchUs(Vol2VfxInstance inst) => inst.Log.Emitter != null ? Math.Max(inst.StartUs, inst.Log.Emitter.StartUs) : inst.StartUs;

        static IVol2LegacyStage Stage(Vol2VfxInstance inst) => inst.Model.Legacy;

        /// <summary>The bookkeeping part: Aux = the stage handle (0 = not begun / nothing shown), F0 = 1 once begun, F1 = 1 once let go.</summary>
        static Vol2VfxPart State(Vol2VfxInstance inst) => inst.FirstOf(KindState);

        // contacts the executor approved before this cast's body began (an instant fan / ring touches in the commit or launch frame, before the view's tick): relayed when it begins
        readonly System.Collections.Generic.Dictionary<Vol2VfxInstance, System.Collections.Generic.List<long>> _early = new System.Collections.Generic.Dictionary<Vol2VfxInstance, System.Collections.Generic.List<long>>();

        public override void OnBegan(Vol2VfxInstance inst)
        {
            var p = inst.AddPart(KindState, inst.StartUs);
            p.Pos = inst.Origin; p.Dir = inst.Forward;
            inst.Dirty = false;                    // no ink mesh: the view never rents one for a bridged cast
            var stage = Stage(inst);
            if (stage == null) return;
            float toApex = Mathf.Max(MinToApex, (float)((LaunchUs(inst) - inst.StartUs) / 1000000.0));
            var target = Predict(inst, out _, out _);
            stage.Swing(new Vol2LegacySwing
            {
                Letter = inst.Log.Glyph.Letter, Body = inst.Log.Glyph.Body, Kind = LegacyKind, ToApex = toApex,
                FromClipStart = LegacyKind == Vol2LegacyKind.Guard || LegacyKind == Vol2LegacyKind.Ward,
                Distance = target != null ? Flat(target.Position - inst.Origin).magnitude : 0f,
            });
            Update(inst, inst.StartUs);           // a cast that launches at its commit shows its body in the commit frame (before its first contact)
        }

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            inst.Dirty = false;
            var p = State(inst);
            if (p == null || p.F0 > 0f || inst.Log.Status == "Aborted") return;
            if (nowUs < SpawnUs(inst)) return;
            p.F0 = 1f; p.AppearUs = nowUs;
            var stage = Stage(inst);
            if (stage == null || !HasBody) return;
            var cue = Cue(inst, nowUs);
            p.Aux = stage.Begin(cue);
            if (_early.TryGetValue(inst, out var early))
            {
                _early.Remove(inst);
                if (p.Aux != 0) foreach (long id in early) stage.Contact((int)p.Aux, id, inst.Origin);
            }
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            var p = State(inst);
            var stage = Stage(inst);
            if (p == null) return;
            p.Seed++;                              // contacts drawn (bookkeeping, read by the fixture)
            if (stage == null) return;
            if (p.F0 > 0f) { if (p.Aux != 0) stage.Contact((int)p.Aux, rec.TargetId, inst.Origin); return; }
            if (!_early.TryGetValue(inst, out var early)) _early[inst] = early = new System.Collections.Generic.List<long>(2);
            early.Add(rec.TargetId);
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w) { }

        public override bool IsFinished(Vol2VfxInstance inst, long nowUs)
        {
            if (!inst.CastIsEnded && !inst.Log.Finished) return false;
            var stage = Stage(inst);
            // step 4: every lasting part waits for the executor's event that ends it (a material's end, a strip's UntilUs, the trip's end); once closed it may still be drying
            foreach (var lay in inst.Parts)
            {
                if (lay.Kind != KindLay || lay.Gone) continue;
                if (!lay.Ended) return false;
                if (stage != null && lay.Aux != 0 && stage.Standing((int)lay.Aux) && nowUs < lay.EndUs + Vol2Time.FromSeconds(HoldAfterEndSeconds)) return false;
            }
            var p = State(inst);
            if (p == null) return true;
            if (p.F0 <= 0f) return true;           // the cast ended before its launch (aborted / refused): nothing to wait for
            long end = Math.Max(inst.CastEndUs, p.AppearUs);
            bool alive = stage != null && p.Aux != 0 && stage.Alive((int)p.Aux);
            if (alive && nowUs < end + Vol2Time.FromSeconds(HoldAfterEndSeconds)) return false;
            return true;
        }

        public override void OnRetired(Vol2VfxInstance inst, bool aborted)
        {
            _early.Remove(inst);
            var stage = Stage(inst);
            // step 4: a part still open here is a lab reset / disposal (aborted) or a material the model dropped: it goes at once
            foreach (var lay in inst.Parts)
            {
                if (lay.Kind != KindLay || lay.Aux == 0) continue;
                if (stage != null && (!lay.Ended || aborted)) stage.Close((int)lay.Aux, lay.Ended ? lay.Code : Vol2EndCode.None, Vector3.zero, true);
                lay.Aux = 0;
            }
            var p = State(inst);
            if (p == null || p.F1 > 0f) return;
            p.F1 = 1f;
            if (stage != null && p.Aux != 0) stage.End((int)p.Aux, aborted);
        }

        // ---- step 4: lasting parts (pure bookkeeping; the stage draws) ----

        /// <summary>Lays a lasting part and books it on the instance (even with no stage: the fixture reads the bookkeeping).</summary>
        protected Vol2VfxPart LayPart(Vol2VfxInstance inst, in Vol2LegacyPart def, Vol2Material mat, long nowUs)
        {
            var p = inst.AddPart(KindLay, nowUs);
            p.Mat = mat; p.Pos = def.Point; p.Dir = def.Facing; p.F0 = def.Radius; p.Aux = 0;
            inst.Dirty = false;
            var stage = Stage(inst);
            if (stage != null) p.Aux = stage.Lay(def);
            Laid(inst, p, def);
            return p;
        }

        /// <summary>A hook for the fixture's bookkeeping (what part with what role was laid).</summary>
        protected virtual void Laid(Vol2VfxInstance inst, Vol2VfxPart p, in Vol2LegacyPart def) { }

        /// <summary>The executor ended what the part shows: book the end (scope clock) and let the stage play the end reason.</summary>
        protected void ClosePart(Vol2VfxInstance inst, Vol2VfxPart p, long atUs, Vol2EndCode code, Vector3 direction)
        {
            if (p == null || p.Ended) return;
            p.EndUs = atUs; p.Code = code; p.EndSeconds = 0f;
            inst.Dirty = false;
            var stage = Stage(inst);
            if (stage != null && p.Aux != 0) stage.Close((int)p.Aux, code, direction, false);
        }

        protected static void Place(Vol2VfxInstance inst, Vol2VfxPart p, Vector3 point, float radius)
        {
            if (p == null || p.Ended) return;
            if ((p.Pos - point).sqrMagnitude < 1e-6f && Mathf.Abs(p.F0 - radius) < 1e-4f) return;
            p.Pos = point; p.F0 = radius;
            var stage = Stage(inst);
            if (stage != null && p.Aux != 0) stage.Place((int)p.Aux, point, radius);
        }

        protected static void Pulse(Vol2VfxInstance inst, in Vol2LegacyPulse pulse)
        {
            var p = inst.FirstOf(KindState);
            if (p != null) p.Density += 1f;        // pulses asked (bookkeeping, read by the fixture; the part's Density starts at 1)
            Stage(inst)?.Pulse(pulse);
        }

        /// <summary>The warning before a scheduled end (a material's live expiry, a strip's UntilUs): once, when the old prefab's own lead (or <paramref name="fallbackLead"/>) is reached.</summary>
        protected static void WarnIfDue(Vol2VfxInstance inst, Vol2VfxPart p, long endUs, long nowUs, float fallbackLead)
        {
            if (p == null || p.Ended || p.F1 > 0f || endUs <= 0) return;
            var stage = Stage(inst);
            float lead = stage != null && p.Aux != 0 ? stage.WarnLead((int)p.Aux) : 0f;
            if (lead <= 0f) lead = fallbackLead;
            if (nowUs < endUs - Vol2Time.FromSeconds(lead)) return;
            p.F1 = 1f;
            if (stage != null && p.Aux != 0) stage.Warn((int)p.Aux);
        }

        protected abstract Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs);

        protected Vol2LegacyCue Base(Vol2VfxInstance inst)
        {
            var spec = inst.Log.Spec;
            return new Vol2LegacyCue
            {
                Letter = inst.Log.Glyph.Letter, Body = inst.Log.Glyph.Body, Kind = LegacyKind, Origin = spec.Origin, Forward = spec.Forward,
                PathLength = spec.PathLength, FanDepth = spec.FanDepth, RingRadius = spec.RingRadius,
            };
        }

        protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The body the cast's shape touches first, with the same pure geometry the emitter runs on the coordinator's candidates. Path: entry metres along the
        /// axis; fan: distance from the apex. Null when none (or no coordinator, a bare fixture).</summary>
        protected static IVol2Target Predict(Vol2VfxInstance inst, out float along, out bool contacted)
        {
            along = 0f; contacted = false;
            // a contact the executor already made is the truth (an adjacent body on the launch frame)
            foreach (var c in inst.Log.Contacts)
                if (c.BudgetAccepted)
                {
                    var known = inst.Model.Coordinator != null ? inst.Model.Coordinator.Registry.Find(c.TargetId) : null;
                    if (known != null) { along = c.Along; contacted = true; return known; }
                }
            var coord = inst.Model.Coordinator;
            if (coord == null) return null;
            var spec = inst.Log.Spec; IVol2Target best = null; float bestAlong = float.MaxValue;
            var targets = coord.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                float a;
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path:
                        if (!Vol2ShapeGeometry.PathStep(spec.Origin, spec.Forward, 0f, spec.PathLength, spec.PathWidth * 0.5f, t.Position, t.Radius, out a, out _)) continue;
                        break;
                    case Vol2ShapeKind.Fan:
                        if (!Vol2ShapeGeometry.FanHit(spec.Origin, spec.Forward, spec.FanHalfAngleRad, spec.FanDepth, t.Position, t.Radius)) continue;
                        a = Flat(t.Position - spec.Origin).magnitude;
                        break;
                    case Vol2ShapeKind.Ring:
                        a = Flat(t.Position - spec.Origin).magnitude;
                        if (!Vol2ShapeGeometry.RingHit(spec.Origin, spec.RingInner, spec.RingOuter, t.Position, t.Radius)) continue;
                        break;
                    default:
                        a = Flat(t.Position - spec.Origin).magnitude;
                        break;
                }
                if (a < bestAlong || (a == bestAlong && best != null && t.Id < best.Id)) { best = t; bestAlong = a; }
            }
            if (best != null) along = bestAlong;
            return best;
        }
    }

    /// <summary>P0 (가 나 마 사 아): the old single shot. The glyph's body leaves the brush tip at the launch and lands on the predicted vol.2 contact (impact clock =
    /// entry metres / path speed); with nothing on the path it flies to the path's end.</summary>
    public sealed class Vol2LegacyPathRecipe : Vol2LegacyRecipeBase
    {
        public override Vol2Recipe Kind => Vol2Recipe.PathOneHit;
        public override Vol2LegacyKind LegacyKind => Vol2LegacyKind.Single;
        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs) => SingleCue(inst, Base(inst), nowUs);

        /// <summary>The old single shot's cue of a path cast (shared with the batchim ㅏ glyphs of step 4).</summary>
        public static Vol2LegacyCue SingleCue(Vol2VfxInstance inst, Vol2LegacyCue cue, long nowUs)
        {
            var spec = inst.Log.Spec;
            var t = Predict(inst, out float along, out bool contacted);
            float speed = Mathf.Max(0.1f, spec.PathSpeed);
            // the head has already run (launchUs .. now): the body's own clock starts now
            float run = Mathf.Max(0f, (float)((nowUs - LaunchUs(inst)) / 1000000.0)) * speed;
            if (t != null)
            {
                cue.HasTarget = true; cue.TargetId = t.Id; cue.TargetPoint = t.Position;
                cue.ImpactClock = contacted ? 0.02f : Mathf.Max(0.02f, (along - run) / speed);
            }
            else cue.ImpactClock = Mathf.Max(0.02f, (spec.PathLength - run) / speed);
            cue.FallbackPoint = spec.Origin + spec.Forward * spec.PathLength;
            return cue;
        }
    }

    /// <summary>A0 (거 너 머 서 어): the old guard. It stands at the commit with the old guard clock; the vol.2 parry window is its bright part.</summary>
    public sealed class Vol2LegacyAttachRecipe : Vol2LegacyRecipeBase
    {
        public override Vol2Recipe Kind => Vol2Recipe.AttachOnce;
        public override Vol2LegacyKind LegacyKind => Vol2LegacyKind.Guard;
        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs)
        {
            var cue = Base(inst);
            long w = inst.Log.ParryWindowOpened ? inst.Log.WindowEndUs - inst.Log.WindowStartUs : 0;
            cue.GuardWindow = (float)(w / 1000000.0);
            return cue;
        }
    }

    /// <summary>F0 (고 노 모 소 오): the old area body of each glyph (circle spikes / cone / path wave / volley), laid at the launch toward the body the fan touches first.</summary>
    public sealed class Vol2LegacyFanRecipe : Vol2LegacyRecipeBase
    {
        public override Vol2Recipe Kind => Vol2Recipe.FanOnce;
        public override Vol2LegacyKind LegacyKind => Vol2LegacyKind.Area;
        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs) => AreaCue(inst, Base(inst));

        /// <summary>The old area body's cue of a fan cast (shared with the batchim ㅗ glyphs of step 4).</summary>
        public static Vol2LegacyCue AreaCue(Vol2VfxInstance inst, Vol2LegacyCue cue)
        {
            var t = Predict(inst, out _, out _);
            if (t != null) { cue.HasTarget = true; cue.TargetId = t.Id; cue.TargetPoint = t.Position; }
            cue.FallbackPoint = inst.Log.Spec.Origin + inst.Log.Spec.Forward * inst.Log.Spec.FanDepth;
            return cue;
        }
    }

    /// <summary>R0 (구 누 무 수 우): the old ward membrane of the glyph, raised on the ring's band at the commit for a short cue life (the ring itself is one instant).</summary>
    public sealed class Vol2LegacyRingRecipe : Vol2LegacyRecipeBase
    {
        public override Vol2Recipe Kind => Vol2Recipe.RingOnce;
        public override Vol2LegacyKind LegacyKind => Vol2LegacyKind.Ward;
        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs) => Base(inst);
    }
}
