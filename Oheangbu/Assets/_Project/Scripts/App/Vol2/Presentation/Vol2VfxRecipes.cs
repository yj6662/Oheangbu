using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 9a: the six ㅏ (path) recipes P0 - P5. A recipe is a small stateless object: the model calls its hooks from the coordinator's events, the view calls Write when an instance
    // is dirty. Every time in a stroke is a scope-clock timestamp an executor gave (contact frame, material creation / expiry / end frame, the head stop, the return report, the strip's
    // UntilUs) turned into seconds after the instance started. The ONLY constants of time here are the plan's look beats (template fields: reveal 0.08, outline 0.18, surface 0.30, accent 0.09
    // + 0.12, dwell after the head stops, the look-tails of the end reasons) - they shape a look that an executor event started, they never decide that something lives.
    // Geometry: the path is a ground stroke from 0.3 m to the head (plan G samples (0,0,.3) -> (0,0,3)); the verb is told by what stands at the landing:
    //   P0 a vertical landing stroke + flecks | P1 a trunk that forks upward | P2 flat strokes fanning on the ground to each approved neighbour | P3 a vertical dot on a stem |
    //   P4 an engraved line with two dry lips + a pair of vertical cut faces on the armour | P5 a small closed loop riding the head out and back.

    public abstract class Vol2PathRecipeBase : Vol2VfxRecipe
    {
        /// <summary>Where the ground stroke starts (metres from the cast point, plan G path sample).</summary>
        public const float LineStart = 0.3f;
        protected const int KLine = 0, KLanding = 10, KAccent = 11;
        protected readonly List<Vector3> Pts = new List<Vector3>(64);

        public override void OnBegan(Vol2VfxInstance inst) { inst.AddPart(KLine, inst.StartUs); }

        /// <summary>The scope-clock moment the ground stroke's own end begins (-1 = not known yet).</summary>
        protected virtual long LineEndUs(Vol2VfxInstance inst)
            => inst.HeadStopUs < 0 ? -1 : inst.HeadStopUs + Vol2Time.FromSeconds(inst.Template.holdAfterHeadSeconds);

        protected virtual long LineEndSeconds => 0;

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            var pe = inst.Path;
            if (pe != null && pe.Finished && inst.HeadStopUs < 0)
            {
                long stop = inst.StartUs + Vol2Time.FromSeconds(pe.HeadAlong / Mathf.Max(0.01f, inst.Log.Spec.PathSpeed));
                inst.HeadStopUs = Math.Min(stop, nowUs);
                inst.Dirty = true;
            }
            var line = inst.FirstOf(KLine);
            if (line != null && !line.Ended)
            {
                long e = LineEndUs(inst);
                if (e >= 0) inst.EndPart(line, e, Vol2EndCode.Expire, inst.Template.dryAfterContactSeconds);
            }
            UpdateParts(inst, nowUs);
        }

        protected virtual void UpdateParts(Vol2VfxInstance inst, long nowUs) { }

        // ---- shared drawing ----

        protected static Vol2StrokeAttr Attr(Vol2VfxInstance inst, float width)
        {
            var a = Vol2StrokeAttr.Default;
            a.Width = width; a.Seed = inst.Seed; a.Hollow = inst.Template.hollow;
            return a;
        }

        /// <summary>The end of a stroke from its part: the executor's end frame and code if it ended, the scheduled expiry (with the warning window) of a material, or no end yet.</summary>
        protected static void EndOf(Vol2VfxInstance inst, Vol2VfxPart p, ref Vol2StrokeAttr a)
        {
            var t = inst.Template;
            if (p.Ended)
            {
                a.End = inst.Rel(p.EndUs); a.EndCode = p.Code == Vol2EndCode.None ? Vol2EndCode.Expire : p.Code;
                a.EndSeconds = p.EndSeconds > 0f ? p.EndSeconds : t.EndSeconds(a.EndCode); a.Warn = 0f;
            }
            else if (p.Mat != null)
            {
                a.End = inst.Rel(p.Mat.ExpiresUs); a.EndCode = Vol2EndCode.Expire; a.EndSeconds = t.endExpireSeconds; a.Warn = t.expiryWarnSeconds;
                p.BuiltExpiresUs = p.Mat.ExpiresUs;
            }
            else if (p.EndSchedule > 0)
            {
                a.End = inst.Rel(p.EndSchedule); a.EndCode = Vol2EndCode.Expire; a.EndSeconds = t.endExpireSeconds; a.Warn = p.WarnSeconds;
            }
            else { a.End = Vol2StrokeAttr.Forever; a.EndCode = Vol2EndCode.Expire; a.EndSeconds = t.endExpireSeconds; a.Warn = 0f; }
        }

        /// <summary>The ground stroke of the path, revealed up to the head the view sets each frame.</summary>
        protected void WriteLine(Vol2VfxInstance inst, Vol2InkWriter w, float width)
        {
            var part = inst.FirstOf(KLine); if (part == null || part.Dead) return;
            var t = inst.Template; var spec = inst.Log.Spec;
            // the stroke leaves the brush tip (chest height, a little to the right of the body so the first metres are not hidden behind the caster) and DESCENDS toward the ground,
            // reaching it about 5 m out and running on along it (a swoop, not a standing plank); the judged lane is the straight axis: the visual leaves it by at most 0.22 m, back on it at 3.2 m
            float h0 = inst.ChestY - inst.Origin.y;
            Pts.Clear();
            const int n = 32;
            for (int i = 0; i <= n; i++)
            {
                float along = LineStart + (spec.PathLength - LineStart) * Mathf.Pow(i / (float)n, 1.7f);
                float lat = 0.22f * Mathf.Max(0f, 1f - along / 3.2f);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((along - 0.3f) / 2.7f));
                Pts.Add(inst.Origin + inst.Forward * along + inst.Right * lat + Vector3.up * Mathf.Lerp(h0, 0.06f, k));
            }
            var at = Attr(inst, width);
            at.Layout = Vol2StrokeLayout.Billboard; at.Role = Vol2StrokeRole.FollowHead; at.TaperTip = true;
            EndOf(inst, part, ref at);
            w.Stroke(Pts, at);
        }

        /// <summary>A vertical stroke standing on the ground at <paramref name="at"/> (the landing / contact stroke of the path).</summary>
        protected void WriteStanding(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, Vector3 at, float height, float width, float appear, float speed, bool taperTip = true)
        {
            Vol2Curves.Rise(Pts, at, Vector3.zero, height, 6);
            var a = Attr(inst, width);
            a.Appear = appear; a.Speed = speed; a.TaperTip = taperTip; a.Layout = Vol2StrokeLayout.Billboard;
            EndOf(inst, p, ref a);
            w.Stroke(Pts, a);
        }

        /// <summary>The contact accent (plan B-1, 0.09 s then 0.12 s of settling): two crossing short scratches over the body and 3 - 5 flecks of spatter that fly out and dry.</summary>
        protected void WriteAccent(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p)
        {
            var t = inst.Template;
            float t0 = inst.Rel(p.AppearUs);
            Vector3 c = p.Pos + Vector3.up * 0.34f;
            float half = t.accentLength * 0.5f;
            for (int k = 0; k < 2; k++)
            {
                float sign = k == 0 ? 1f : -1f;
                Vector3 d = (inst.Right * (Mathf.Cos(40f * Mathf.Deg2Rad) * sign) + Vector3.up * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized;
                Vol2Curves.Line(Pts, c - d * half, c + d * half, 3);
                var a = Attr(inst, 0.14f);
                a.Appear = t0; a.Speed = 30f; a.TaperTip = true; a.TaperBase = true; a.Layout = Vol2StrokeLayout.Billboard; a.Hollow = 0.6f;
                a.End = t0 + t.accentSeconds; a.EndSeconds = t.accentStableSeconds; a.EndCode = Vol2EndCode.Expire;
                w.Stroke(Pts, a);
            }
            bool ashy = inst.Look.end == Vol2EndBehaviour.AshRise;           // fire: grey ash that RISES; the other bodies: slivers that fly out
            int n = ashy ? 6 : Mathf.Clamp(t.fleckCount, 3, 5);
            for (int i = 0; i < n; i++)
            {
                float ang = (i + Vol2Curves.Hash(p.Seed, i)) * Mathf.PI * 2f / n;
                Vector3 dir = ashy
                    ? (inst.Right * Mathf.Cos(ang) * 0.45f + inst.Forward * Mathf.Sin(ang) * 0.3f + Vector3.up).normalized
                    : (inst.Right * Mathf.Cos(ang) + Vector3.up * (Mathf.Sin(ang) * 0.8f + 0.25f) - inst.Forward * 0.35f).normalized;
                Vol2Curves.Line(Pts, c, c + dir * (ashy ? 0.10f : 0.2f), 2);
                var a = Attr(inst, ashy ? 0.09f : 0.10f);
                a.Appear = t0; a.Speed = 1e5f; a.Role = Vol2StrokeRole.Drift; a.Hollow = 0f; a.TaperTip = true; a.Layout = Vol2StrokeLayout.Billboard; a.Ash = ashy;
                a.Drift = dir * (ashy ? 1.5f : 2.4f); a.DriftSeconds = ashy ? 0.6f : 0.2f;
                a.Seed = inst.Seed + i * 3.7f;
                a.End = t0 + t.accentSeconds + (ashy ? 0.2f : 0f); a.EndSeconds = t.accentStableSeconds * (ashy ? 1.5f : 0.8f); a.EndCode = Vol2EndCode.Expire;
                w.Stroke(Pts, a);
            }
        }
        protected Vol2VfxPart AddAccent(Vol2VfxInstance inst, long atUs, Vector3 ground, long targetId)
        {
            var acc = inst.AddPart(KAccent, atUs);
            acc.Pos = ground; acc.Aux = targetId;
            inst.EndPart(acc, atUs + Vol2Time.FromSeconds(inst.Template.accentSeconds), Vol2EndCode.Expire, inst.Template.accentStableSeconds);
            return acc;
        }

        protected static Vector3 GroundOf(Vol2VfxInstance inst, Vector3 p) { p.y = inst.Origin.y; return p; }

        protected bool AccentDone(Vol2VfxInstance inst, long targetId)
        {
            foreach (var p in inst.Parts) if (p.Kind == KAccent && p.Aux == targetId) return true;
            return false;
        }

        protected Vector3 TargetGround(Vol2VfxInstance inst, long targetId, float fallbackAlong)
        {
            var t = inst.Model.Coordinator.Registry.Find(targetId);
            if (t != null) return GroundOf(inst, t.Position);
            return GroundOf(inst, inst.Origin + inst.Forward * fallbackAlong);
        }

        /// <summary>The entry point of a path contact on the ground: where the head first touched the body (the record's Along).</summary>
        protected static Vector3 EntryPoint(Vol2VfxInstance inst, Vol2ContactRecord rec) => GroundOf(inst, inst.Origin + inst.Forward * rec.Along);

        /// <summary>A flat bundle of strokes fanning from <paramref name="c"/> along <paramref name="dir"/> (plan G sample: 0.6 m long, 0.6 m across).</summary>
        protected void WriteBundle(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, Vector3 c, Vector3 dir, float appear, float density)
        {
            var t = inst.Template;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int k = 0; k < 4; k++)
            {
                float lat = (k - 1.5f) / 1.5f;                       // -1 .. 1
                float len = t.bundleLength * (0.82f + 0.18f * Vol2Curves.Hash(p.Seed, k));
                Vector3 end = c + dir * len + side * (lat * t.bundleSpread * 0.5f);
                Vector3 mid = c + dir * (len * 0.5f) + side * (lat * t.bundleSpread * 0.2f + (Vol2Curves.Hash(p.Seed, 9 + k) - 0.5f) * 0.05f);
                Pts.Clear(); Pts.Add(c + Vector3.up * 0.03f); Pts.Add(mid + Vector3.up * 0.03f); Pts.Add(end + Vector3.up * 0.03f);
                var a = Attr(inst, 0.28f);
                a.Layout = Vol2StrokeLayout.Flat; a.Appear = appear + 0.01f * k; a.Speed = len / 0.08f; a.TaperTip = true; a.Hollow = 0.85f; a.Density = density;
                a.Seed = inst.Seed + p.Seed * 1.3f + k;
                EndOf(inst, p, ref a);
                w.Stroke(Pts, a);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P0 한 번
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    public sealed class Vol2PathOneHitRecipe : Vol2PathRecipeBase
    {
        public override Vol2Recipe Kind => Vol2Recipe.PathOneHit;

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (AccentDone(inst, rec.TargetId)) return;
            Vector3 g = EntryPoint(inst, rec);
            var land = inst.AddPart(KLanding, rec.NowUs); land.Pos = g; land.Aux = rec.TargetId;
            AddAccent(inst, rec.NowUs, g, rec.TargetId);
            inst.HasLanding = true; inst.Landing = g;
        }

        protected override void UpdateParts(Vol2VfxInstance inst, long nowUs)
        {
            var line = inst.FirstOf(KLine);
            if (line == null || !line.Ended) return;
            foreach (var p in inst.Parts) if (p.Kind == KLanding && !p.Ended) inst.EndPart(p, line.EndUs, Vol2EndCode.Expire, inst.Template.dryAfterContactSeconds);
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var t = inst.Template;
            WriteLine(inst, w, t.lineWidth);
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KLanding) WriteStanding(inst, w, p, p.Pos, 0.6f, 0.24f, inst.Rel(p.AppearUs), 0.6f / 0.05f);
                else if (p.Kind == KAccent) WriteAccent(inst, w, p);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P1 남긴다
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    public sealed class Vol2PathLeaveRecipe : Vol2PathRecipeBase
    {
        const int KTree = 20;
        public override Vol2Recipe Kind => Vol2Recipe.PathLeave;

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (rec.Kind != "Instant" && rec.Kind != "Second") return;
            if (AccentDone(inst, rec.TargetId)) return;
            AddAccent(inst, rec.NowUs, EntryPoint(inst, rec), rec.TargetId);
        }

        public override void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m)
        {
            if (m.Kind != Vol2MaterialKind.Residue || m.Attached) return;
            var p = inst.AddPart(KTree, m.CreatedUs);
            p.Mat = m; p.Pos = GroundOf(inst, m.Footprint.Origin) - inst.Forward * 0.8f;        // the residue lands at the first body's centre: the tree stands on its side that faces the caster, not inside it
            // plan G "가지 높이 결정": the height is fixed ONCE, at the commit, for the settled shoulder camera
            float d = Vector3.Distance(GroundOf(inst, Vol2VfxBeats.ShoulderGround(inst.Origin, inst.Forward)), p.Pos);
            p.F0 = Mathf.Min(inst.Template.height, Vol2VfxBeats.BranchMaxHeight(d));
            inst.HasLanding = true; inst.Landing = p.Pos;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var t = inst.Template;
            WriteLine(inst, w, t.lineWidth);
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KAccent) { WriteAccent(inst, w, p); continue; }
                if (p.Kind != KTree) continue;
                float t0 = inst.Rel(p.AppearUs), H = Mathf.Max(0.2f, p.F0);
                float trunkH = H * 0.62f;
                Vector3 lean = inst.Right * ((Vol2Curves.Hash(p.Seed, 3) - 0.5f) * 0.12f);
                Vol2Curves.Rise(Pts, p.Pos, lean, trunkH, 6);
                var a = Attr(inst, t.branchWidth);
                a.Appear = t0; a.Speed = trunkH / 0.07f; a.TaperTip = false; a.Seed = inst.Seed + 1.1f;
                EndOf(inst, p, ref a);
                w.Stroke(Pts, a);
                Vector3 fork = p.Pos + Vector3.up * trunkH + lean;
                float[] ang = { -t.forkAngleDegrees, t.forkAngleDegrees, 0f };
                float[] len = { 0.46f * H, 0.42f * H, 0.34f * H };
                for (int k = 0; k < 3; k++)
                {
                    float rad = ang[k] * Mathf.Deg2Rad;
                    Vector3 dir = (inst.Right * Mathf.Sin(rad) + Vector3.up * Mathf.Cos(rad)).normalized;
                    Vol2Curves.Line(Pts, fork, fork + dir * len[k], 5);
                    if (inst.Look.edge == Vol2EdgeKind.OpenHook) Vol2Curves.Curl(Pts, 4, -Mathf.Sign(ang[k] + 0.001f) * 70f, inst.Forward);
                    var b = Attr(inst, t.branchWidth * 0.72f);
                    b.Appear = t0 + 0.05f; b.Speed = len[k] / 0.09f; b.TaperTip = true; b.Seed = inst.Seed + 2.3f + k;
                    EndOf(inst, p, ref b);
                    w.Stroke(Pts, b);
                    if (k < 2)
                    {
                        float rad2 = (ang[k] + Mathf.Sign(ang[k]) * 32f) * Mathf.Deg2Rad;
                        Vector3 dir2 = (inst.Right * Mathf.Sin(rad2) + Vector3.up * Mathf.Cos(rad2)).normalized;
                        Vector3 from = fork + dir * (len[k] * 0.78f);
                        Vol2Curves.Line(Pts, from, from + dir2 * (0.14f * H / 0.9f), 2);
                        var c = Attr(inst, t.branchWidth * 0.5f);
                        c.Appear = t0 + 0.10f; c.Speed = 0.14f / 0.06f; c.TaperTip = true; c.Seed = inst.Seed + 5.1f + k;
                        EndOf(inst, p, ref c);
                        w.Stroke(Pts, c);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P2 번진다
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    public sealed class Vol2PathSpreadRecipe : Vol2PathRecipeBase
    {
        const int KBundle = 30, KMark = 31;
        public override Vol2Recipe Kind => Vol2Recipe.PathSpread;

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (rec.Kind != "Instant" && rec.Kind != "Second") return;
            if (inst.HasLanding) return;
            Vector3 g = EntryPoint(inst, rec);
            var p = inst.AddPart(KBundle, rec.NowUs); p.Pos = g; p.Aux = rec.TargetId; p.Dir = inst.Forward;
            inst.HasLanding = true; inst.Landing = g;
            AddAccent(inst, rec.NowUs, EntryPoint(inst, rec), rec.TargetId);
        }

        public override void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m)
        {
            if (m.Kind != Vol2MaterialKind.Mark) return;
            var p = inst.AddPart(KMark, m.CreatedUs);
            p.Mat = m;
            Vector3 body = GroundOf(inst, m.Footprint.Origin);
            Vector3 d = inst.HasLanding ? body - inst.Landing : inst.Forward; d.y = 0f;
            p.Dir = d.sqrMagnitude > 1e-4f ? d.normalized : inst.Forward;
            p.Pos = body - p.Dir * 0.9f;                  // the bundle lies in front of the neighbour along the way the spread came, not under its body
            p.F0 = inst.HasLanding ? Vector3.Distance(inst.Landing, p.Pos) : 0f;
            // "분배에 따라 묽어짐": the smaller the approved share, the thinner the bundle
            p.Density = Mathf.Clamp(0.35f + 0.65f * Mathf.Clamp01((float)m.Initial / 0.5f), 0.4f, 1f);
        }

        protected override void UpdateParts(Vol2VfxInstance inst, long nowUs)
        {
            // the landing bundle lives with the cast's marks (the 3 s outer traces): its scheduled end is the latest live expiry, it ends when the last mark ended;
            // with no neighbour it dries like the path
            Vol2VfxPart bundle = null; bool anyMark = false, allEnded = true; long lastEnd = 0, sched = 0;
            foreach (var p in inst.Parts)
            {
                if (p.Kind == KBundle) bundle = p;
                else if (p.Kind == KMark)
                {
                    anyMark = true;
                    if (!p.Ended) { allEnded = false; if (p.Mat != null) sched = Math.Max(sched, p.Mat.ExpiresUs); }
                    else lastEnd = Math.Max(lastEnd, p.EndUs);
                }
            }
            if (bundle == null || bundle.Ended) return;
            if (anyMark && !allEnded && sched != bundle.EndSchedule) { bundle.EndSchedule = sched; bundle.WarnSeconds = inst.Template.expiryWarnSeconds; inst.Dirty = true; }
            if (!inst.CastIsEnded) return;
            if (anyMark && allEnded) inst.EndPart(bundle, lastEnd, Vol2EndCode.Expire);
            else if (!anyMark) inst.EndPart(bundle, inst.CastEndUs + Vol2Time.FromSeconds(inst.Template.holdAfterHeadSeconds), Vol2EndCode.Expire, inst.Template.dryAfterContactSeconds);
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var t = inst.Template;
            WriteLine(inst, w, t.lineWidth);
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KAccent) { WriteAccent(inst, w, p); continue; }
                if (p.Kind == KBundle) WriteBundle(inst, w, p, p.Pos, p.Dir, inst.Rel(p.AppearUs), 1f);
                else if (p.Kind == KMark)
                {
                    float t0 = inst.Rel(p.AppearUs);
                    if (p.F0 > 0.05f)
                    {
                        Vector3 from = inst.Landing + p.Dir * 0.3f + Vector3.up * 0.03f, to = p.Pos - p.Dir * 0.2f + Vector3.up * 0.03f;
                        Vector3 sideDir = Vector3.Cross(Vector3.up, p.Dir) * ((Vol2Curves.Hash(p.Seed, 1) - 0.5f) * 0.3f);
                        Pts.Clear(); Pts.Add(from); Pts.Add(Vector3.Lerp(from, to, 0.5f) + sideDir); Pts.Add(to);
                        var a = Attr(inst, 0.18f);
                        a.Layout = Vol2StrokeLayout.Flat; a.Appear = t0 - 0.03f; a.Speed = Mathf.Max(1f, p.F0) / 0.1f; a.TaperTip = true; a.Hollow = 0.85f; a.Density = p.Density; a.Seed = inst.Seed + p.Seed * 2.1f;
                        EndOf(inst, p, ref a);
                        w.Stroke(Pts, a);
                    }
                    WriteBundle(inst, w, p, p.Pos, p.Dir, t0, p.Density);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P3 심는다
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    public sealed class Vol2PathSeedRecipe : Vol2PathRecipeBase
    {
        const int KSeed = 40;
        /// <summary>The seed stands on its host's side that faces the caster (the host's own body would hide a 0.45 m dot standing in its middle).</summary>
        static Vector3 SeedSpot(Vol2VfxInstance inst, Vol2Material m) => GroundOf(inst, m.Footprint.Origin) - inst.Forward * 0.75f;
        public override Vol2Recipe Kind => Vol2Recipe.PathSeed;

        public override void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m)
        {
            if (m.Kind != Vol2MaterialKind.Mark || m.Tag != Vol2SeedExecutor.TagSeed) return;
            var p = inst.AddPart(KSeed, m.CreatedUs);
            p.Mat = m; p.Pos = SeedSpot(inst, m);
        }

        public override void OnMaterialChanged(Vol2VfxInstance inst, Vol2Material m, string what)
        {
            var p = inst.PartOf(m);
            if (p == null || what != "moved") return;
            p.Pos = SeedSpot(inst, m); inst.Dirty = true;
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            // the seed worked (an accepted Activation): the dot is pressed (its material ends Consumed on the same frame -> the 소 fold) and the spatter flies
            if (rec.Kind != "Activation") return;
            Vector3 g = TargetGround(inst, rec.TargetId, rec.Along);
            foreach (var p in inst.Parts) if (p.Kind == KSeed && p.Mat != null && p.Mat.Id.Value == rec.MaterialId) { g = p.Pos; break; }
            AddAccent(inst, rec.NowUs, g, -rec.TargetId);
        }

        protected override void UpdateParts(Vol2VfxInstance inst, long nowUs)
        {
            foreach (var p in inst.Parts)
            {
                if (p.Kind != KSeed || p.Mat == null || p.Ended) continue;
                Vector3 now = SeedSpot(inst, p.Mat);
                if ((now - p.Pos).sqrMagnitude > 0.0004f) { p.Pos = now; inst.Dirty = true; }       // the seed stands on its host: it follows
            }
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var t = inst.Template;
            WriteLine(inst, w, t.lineWidth);
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KAccent) { WriteAccent(inst, w, p); continue; }
                if (p.Kind != KSeed) continue;
                float t0 = inst.Rel(p.AppearUs), H = t.dotHeight, r = t.dotRadius;
                float stemH = H - r;
                Vol2Curves.Rise(Pts, p.Pos, Vector3.zero, stemH, 4);
                var a = Attr(inst, 0.12f);
                a.Appear = t0; a.Speed = stemH / 0.05f; a.TaperBase = false; a.Hollow = 0.45f; a.Seed = inst.Seed + 1.7f;
                EndOf(inst, p, ref a);
                w.Stroke(Pts, a);
                // the dot: a ring standing on the stem, facing the camera (a waiting seed is an OPEN ring; plan P3 "대기점")
                Vol2Curves.Circle(Pts, p.Pos + Vector3.up * (stemH + r), inst.Right, Vector3.up, r, 14);
                var b = Attr(inst, 0.09f);
                b.Appear = t0 + 0.04f; b.Speed = (Mathf.PI * 2f * r) / 0.05f; b.Hollow = 0.3f; b.Seed = inst.Seed + 2.9f;
                EndOf(inst, p, ref b);
                w.Stroke(Pts, b);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P4 가른다
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    public sealed class Vol2PathSplitRecipe : Vol2PathRecipeBase
    {
        const int KCut = 50;
        public override Vol2Recipe Kind => Vol2Recipe.PathSplit;

        /// <summary>The engraved cut is a world trace of at most 0.3 s (plan B P4): it starts to dry 0.10 s after the head stops and is gone 0.2 s later.</summary>
        protected override long LineEndUs(Vol2VfxInstance inst) => inst.HeadStopUs < 0 ? -1 : inst.HeadStopUs + Vol2Time.FromSeconds(0.10);

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (rec.Kind != "Instant" && rec.Kind != "Second" && rec.Kind != "Touch") return;
            if (AccentDone(inst, rec.TargetId)) return;
            Vector3 g = EntryPoint(inst, rec);
            var cut = inst.AddPart(KCut, rec.NowUs); cut.Pos = g; cut.Aux = rec.TargetId;
            AddAccent(inst, rec.NowUs, g, rec.TargetId);
        }

        protected override void UpdateParts(Vol2VfxInstance inst, long nowUs)
        {
            Vol2SplitExecutor ex = null;
            if (inst.Model.Coordinator.Verbs.TryGet(Verb.Split, out var e)) ex = e as Vol2SplitExecutor;
            foreach (var p in inst.Parts)
            {
                if (p.Kind != KCut || p.Ended) continue;
                if (ex != null && p.EndSchedule <= 0)
                {
                    long until = 0;
                    foreach (var s in ex.Strips.Entries) if (s.Cast == inst.Cast && s.TargetId == p.Aux && s.UntilUs > until) until = s.UntilUs;
                    if (until > 0) { p.EndSchedule = until; inst.Dirty = true; }
                }
                if (p.EndSchedule > 0 && nowUs >= p.EndSchedule) inst.EndPart(p, p.EndSchedule, Vol2EndCode.Expire);
            }
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var t = inst.Template;
            WriteLine(inst, w, 0.14f);                                  // the blade's own stroke, thin, at chest height
            var line = inst.FirstOf(KLine);
            if (line != null && !line.Dead)
            {
                var spec = inst.Log.Spec;
                Vector3 a0 = inst.Origin + inst.Forward * LineStart + Vector3.up * t.lineLift, b0 = inst.Origin + inst.Forward * spec.PathLength + Vector3.up * t.lineLift;
                float lipW = 0.10f, off = t.lipGap * 0.5f + lipW * 0.5f;
                for (int k = 0; k < 3; k++)
                {
                    // k = 0 the cut itself (a dark thin line), 1 and 2 the two dry lips either side of it
                    float lat = k == 0 ? 0f : (k == 1 ? -off : off);
                    Vol2Curves.Line(Pts, a0 + inst.Right * lat, b0 + inst.Right * lat, 24);
                    var at = Attr(inst, k == 0 ? 0.07f : lipW);
                    at.Layout = Vol2StrokeLayout.Flat; at.Role = Vol2StrokeRole.FollowHead; at.TaperTip = true; at.Hollow = k == 0 ? 0f : 0.35f; at.Seed = inst.Seed + k * 1.9f;
                    EndOf(inst, line, ref at);
                    w.Stroke(Pts, at);
                }
            }
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KAccent) { WriteAccent(inst, w, p); continue; }
                if (p.Kind != KCut) continue;
                float t0 = inst.Rel(p.AppearUs);
                // the cut faces: two short dry lips standing at the armour contact, side by side (a pair, same length, a small gap)
                for (int k = 0; k < 2; k++)
                {
                    float lat = (k == 0 ? -1f : 1f) * (t.lipGap * 0.5f + 0.035f);
                    Vol2Curves.Rise(Pts, p.Pos + inst.Right * lat, Vector3.zero, t.cutFaceHeight, 4);
                    var a = Attr(inst, 0.11f);
                    a.Appear = t0; a.Speed = t.cutFaceHeight / 0.06f; a.TaperTip = true; a.Hollow = 0.3f; a.Seed = inst.Seed + 3.3f + k;
                    if (p.Ended) EndOf(inst, p, ref a);
                    else if (p.EndSchedule > 0) { a.End = inst.Rel(p.EndSchedule); a.EndSeconds = t.endExpireSeconds; a.EndCode = Vol2EndCode.Expire; }
                    w.Stroke(Pts, a);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P5 돌아온다
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    public sealed class Vol2PathReturnRecipe : Vol2PathRecipeBase
    {
        const int KLoop = 60;
        public override Vol2Recipe Kind => Vol2Recipe.PathReturn;

        public override void OnBegan(Vol2VfxInstance inst)
        {
            base.OnBegan(inst);
            inst.AddPart(KLoop, inst.StartUs);
        }

        static Vol2ReturnReport ReportOf(Vol2VfxInstance inst)
        {
            if (inst.Model.Coordinator.Verbs.TryGet(Verb.Return, out var e) && e is Vol2ReturnExecutor r) return r.ReportOf(inst.Cast);
            return null;
        }

        /// <summary>Out at the pen speed until the path stops, then back along the same line at the same speed (the executor's report gives the back leg's start and end).</summary>
        public override float Head(Vol2VfxInstance inst, long nowUs)
        {
            var rep = ReportOf(inst);
            if (rep != null && rep.BackStartUs > 0 && nowUs >= rep.BackStartUs)
            {
                float speed = inst.Log.Spec.PathSpeed;
                float headEnd = Mathf.Min(inst.Log.Spec.PathLength, speed * inst.Rel(rep.BackStartUs));
                return Mathf.Max(0f, headEnd - speed * Vol2Time.ToSecondsF(Math.Min(nowUs, rep.BackEndUs) - rep.BackStartUs));
            }
            return inst.HeadAlong(nowUs);
        }

        public override Vector3 Offset(Vol2VfxInstance inst, long nowUs) => inst.Forward * Head(inst, nowUs);

        protected override long LineEndUs(Vol2VfxInstance inst)
        {
            var rep = ReportOf(inst);
            return rep != null && rep.BackEndUs > 0 ? rep.BackEndUs : -1;
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (AccentDone(inst, rec.TargetId)) return;
            AddAccent(inst, rec.NowUs, EntryPoint(inst, rec), rec.TargetId);
        }

        protected override void UpdateParts(Vol2VfxInstance inst, long nowUs)
        {
            var line = inst.FirstOf(KLine); var loop = inst.FirstOf(KLoop);
            if (line != null && line.Ended && loop != null && !loop.Ended) inst.EndPart(loop, line.EndUs, Vol2EndCode.Expire, inst.Template.dryAfterContactSeconds);
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var t = inst.Template;
            WriteLine(inst, w, t.lineWidth);
            var loop = inst.FirstOf(KLoop);
            if (loop != null && !loop.Dead)
            {
                // authored around the cast point, a small closed loop (diameter < 1 m) in the plane across the path; the shader adds the head's offset
                Vol2Curves.Circle(Pts, inst.Origin + Vector3.up * t.loopRadius, inst.Right, Vector3.up, t.loopRadius, 24);
                var a = Attr(inst, 0.13f);
                a.Role = Vol2StrokeRole.RigidOffset; a.Layout = Vol2StrokeLayout.Billboard; a.Appear = 0f; a.Speed = 1e5f; a.Hollow = 0.6f; a.Seed = inst.Seed + 4.4f;
                EndOf(inst, loop, ref a);
                w.Stroke(Pts, a);
            }
            foreach (var p in inst.Parts) if (!p.Dead && p.Kind == KAccent) WriteAccent(inst, w, p);
        }
    }
}
