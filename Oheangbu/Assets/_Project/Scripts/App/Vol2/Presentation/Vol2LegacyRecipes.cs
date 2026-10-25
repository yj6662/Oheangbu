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

    /// <summary>What kind of old body a cue asks for (the old route's presentation of the four 한 번 frames).</summary>
    public enum Vol2LegacyKind { Single = 0, Area = 1, Guard = 2, Ward = 3 }

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
            if (stage == null) return;
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
            var p = State(inst);
            if (p == null) return true;
            if (p.F0 <= 0f) return true;           // the cast ended before its launch (aborted / refused): nothing to wait for
            var stage = Stage(inst);
            long end = Math.Max(inst.CastEndUs, p.AppearUs);
            bool alive = stage != null && p.Aux != 0 && stage.Alive((int)p.Aux);
            if (alive && nowUs < end + Vol2Time.FromSeconds(HoldAfterEndSeconds)) return false;
            return true;
        }

        public override void OnRetired(Vol2VfxInstance inst, bool aborted)
        {
            _early.Remove(inst);
            var p = State(inst);
            var stage = Stage(inst);
            if (p == null || p.F1 > 0f) return;
            p.F1 = 1f;
            if (stage != null && p.Aux != 0) stage.End((int)p.Aux, aborted);
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
        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs)
        {
            var cue = Base(inst);
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
        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs)
        {
            var cue = Base(inst);
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
