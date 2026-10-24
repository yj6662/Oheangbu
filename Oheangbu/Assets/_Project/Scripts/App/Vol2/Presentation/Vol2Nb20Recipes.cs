using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // NB20 (D308-150 V6, user 2026-10-25): the 20 no-batchim spells - P0 가나마사아, A0 거너머서어, F0 고노모소오, R0 구누무수우 - drawn with ink AUTHORED in Blender (bristle brushes,
    // metaball pools and drops, procedural paper grain) and After Effects (Roughen Edges / Turbulent Displace bleed, Cell Pattern cracks for the drying order), packed by
    // Tools/Art/SpellVFX308/nb20/nb20_pack.py into one atlas pair (NB20_Sheet / NB20_SheetTime) that the shared Vol2Ink material plays. A recipe here only PLACES elements:
    // every element is one quad (Vol2InkWriter.Sheet) that the shader reveals by the element's reveal-order map and dries by its drying-order map (cracks -> edges -> fragments, never a
    // uniform fade). Template x body: the element is chosen by the template, its body variant (the same brush, only the body tip differs) by the body - never 20 bespoke effects.
    // Every time below is an executor timestamp (cast start, contact frame, the head's stop, the parry window's end) plus a plan B look beat.

    /// <summary>The NB20 atlas: element order, cells and the authored sizes (metres) - the numbers of Tools/Art/SpellVFX308/nb20/nb20_blender_strokes.py ELEMENTS / SHARED.</summary>
    public static class Vol2Nb20Atlas
    {
        public enum E { Head = 0, Tail = 1, Contact = 2, SupportA = 3, SupportB = 4, FanRadial = 5, FanArc = 6, RingArc = 7, Standing = 8, Pulse = 9 }
        public const int Fleck0 = 50, Ignite = 54, Cells = 55;
        // L (along, the quad's length) x W (across, the quad's width): the authored element including its margin, so the texels keep their world size
        static readonly Vector2[] Size =
        {
            new Vector2(0.66f, 0.40f), new Vector2(1.50f, 0.36f), new Vector2(1.20f, 0.50f), new Vector2(0.80f, 0.26f), new Vector2(0.66f, 0.26f),
            new Vector2(4.40f, 0.50f), new Vector2(4.40f, 1.00f), new Vector2(2.24f, 0.86f), new Vector2(1.16f, 0.34f), new Vector2(0.48f, 0.14f),
        };
        // where the brush stroke itself starts and ends inside the quad (u), Blender "span"
        static readonly Vector2[] Span =
        {
            new Vector2(0.07f, 0.95f), new Vector2(0.06f, 0.94f), new Vector2(0.05f, 0.74f), new Vector2(0.05f, 0.82f), new Vector2(0.06f, 0.94f),
            new Vector2(0.06f, 0.94f), new Vector2(0.03f, 0.97f), new Vector2(0.03f, 0.97f), new Vector2(0.05f, 0.86f), new Vector2(0.06f, 0.94f),
        };
        public static readonly Vector2 FleckSize = new Vector2(0.30f, 0.12f), IgniteSize = new Vector2(0.60f, 0.60f);
        public static float L(E e) => Size[(int)e].x;
        public static float W(E e) => Size[(int)e].y;
        public static Vector2 StrokeSpan(E e) => Span[(int)e];
        public static int BodyIndex(Body b) => b == Body.Wood ? 0 : b == Body.Fire ? 1 : b == Body.Earth ? 2 : b == Body.Metal ? 3 : 4;
        public static int Cell(E e, Body b) => (int)e * 5 + BodyIndex(b);
    }

    /// <summary>Shared placing of the NB20 recipes.</summary>
    public abstract class Vol2Nb20RecipeBase : Vol2VfxRecipe
    {
        protected const int KIgnite = 1, KContact = 2;
        /// <summary>Plan B: the contact accent (0.09 s) and its settling (0.12 s): the contact look starts to dry 0.12 s after the contact and is gone 0.15 s later (".12 - .18 s").</summary>
        protected const float DryStart = 0.12f, DrySeconds = 0.15f;

        protected static Vol2StrokeAttr Sheet(Vol2VfxInstance inst, int cell, float width, float appear, float reveal, Vol2StrokeLayout layout, Vol2SheetFlags flags, float seedOffset = 0f)
        {
            var a = Vol2StrokeAttr.Default;
            a.Width = width; a.Cell = cell; a.Layout = layout; a.Sheet = flags; a.Appear = appear; a.Speed = reveal;
            a.Seed = inst.Seed + seedOffset; a.Hollow = 1f; a.Role = Vol2StrokeRole.Normal;
            return a;
        }

        /// <summary>The end of an element from its part (executor event frame + the look-tail), or none yet.</summary>
        protected static void EndOf(Vol2VfxInstance inst, Vol2VfxPart p, ref Vol2StrokeAttr a)
        {
            if (p.Ended)
            {
                a.End = inst.Rel(p.EndUs); a.EndCode = p.Code == Vol2EndCode.None ? Vol2EndCode.Expire : p.Code;
                a.EndSeconds = p.EndSeconds > 0f ? p.EndSeconds : inst.Template.EndSeconds(a.EndCode);
            }
            else { a.End = Vol2StrokeAttr.Forever; a.EndCode = Vol2EndCode.Expire; a.EndSeconds = inst.Template.endExpireSeconds; }
            a.Warn = 0f;
        }

        protected static Vector3 Rotate(Vector3 v, float degrees) => Quaternion.AngleAxis(degrees, Vector3.up) * v;

        /// <summary>Brush tip at the cast (the player's great brush when the scene has one; else the right hand at chest height a little ahead).</summary>
        protected static Vector3 TipAtCast(Vol2VfxInstance inst)
        {
            var t = inst.Model.BrushTip?.Invoke();
            return t ?? inst.Origin + inst.Right * 0.34f + Vector3.up * 1.22f + inst.Forward * 0.42f;
        }

        /// <summary>An element as a quad whose STROKE (not the quad margin) is centred on <paramref name="centre"/>, along <paramref name="dir"/>, scaled by <paramref name="scale"/>.</summary>
        protected static void Place(Vol2VfxInstance inst, Vol2InkWriter w, Vol2Nb20Atlas.E e, Vector3 centre, Vector3 dir, float scale, Vol2StrokeAttr a)
        {
            float L = Vol2Nb20Atlas.L(e) * scale; var sp = Vol2Nb20Atlas.StrokeSpan(e);
            float mid = (sp.x + sp.y) * 0.5f;
            Vector3 start = centre - dir * (L * mid);
            if ((a.Sheet & Vol2SheetFlags.FlipAlong) != 0) { start = centre + dir * (L * mid); dir = -dir; }
            a.Width = Vol2Nb20Atlas.W(e) * scale;
            w.Sheet(start, start + dir * L, a);
        }

        protected Vol2VfxPart AddIgnite(Vol2VfxInstance inst, long atUs, Vector3 at)
        {
            var p = inst.AddPart(KIgnite, atUs); p.Pos = at;
            inst.EndPart(p, atUs + Vol2Time.FromSeconds(0.09), Vol2EndCode.Child, 0.06f);       // reference 4.1: a blot + needle star for 0.1 - 0.15 s, dries in place
            return p;
        }

        protected void WriteIgnite(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, float scale = 0.6f)
        {
            var a = Sheet(inst, Vol2Nb20Atlas.Ignite, 0f, inst.Rel(p.AppearUs), 0.02f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.None, 3.1f);      // r5: no rim (thin needles + a rim read as a white star)
            EndOf(inst, p, ref a);
            float L = Vol2Nb20Atlas.IgniteSize.x * scale;
            a.Width = Vol2Nb20Atlas.IgniteSize.y * scale;
            Vector3 d = (inst.Right + Vector3.up * 0.15f).normalized;
            w.Sheet(p.Pos - d * (L * 0.5f), p.Pos + d * (L * 0.5f), a);
        }

        /// <summary>The contact look of a 한 번 at a body: the main stroke at the contact frame, two support strokes at +33 / +67 ms (scope clock), 3 - 5 flecks (fire: grey ash that rises).
        /// Standing in front of the body (toward the caster) so the body stays >= 70 % visible behind hollow strokes.</summary>
        protected Vol2VfxPart AddContact(Vol2VfxInstance inst, long atUs, Vector3 bodyPos, float radius, float scale)
        {
            var p = inst.AddPart(KContact, atUs);
            Vector3 to = bodyPos - inst.Origin; to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? to.normalized : inst.Forward;
            p.Pos = new Vector3(bodyPos.x, inst.Origin.y, bodyPos.z) - dir * (radius + 0.25f) + Vector3.up * 0.95f;
            p.Dir = dir; p.F0 = scale; p.F1 = radius;
            inst.EndPart(p, atUs + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            return p;
        }

        /// <summary>r2: the hit made BIG without hiding the body - three strokes thrown flat on the ground from under the body, away from the caster (main at F0, then +33 / +67 ms),
        /// 1.4 - 1.9 m long: the ink splash lands past and beside the body, so it reads from the shoulder camera while the body stays >= 70 % visible.</summary>
        protected void WriteGroundBurst(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, float t0, float s)
        {
            Body b = inst.Look.body;
            Vector3 foot = new Vector3(p.Pos.x, inst.Origin.y + 0.035f, p.Pos.z) + p.Dir * (p.F1 + 0.25f);
            float[] ang = { -52f, 6f, 48f }; float[] len = { 1.25f, 1.6f, 1.15f }; float[] at = { 0f, 0.0333f, 0.0667f };
            Vol2Nb20Atlas.E[] el = { Vol2Nb20Atlas.E.SupportA, Vol2Nb20Atlas.E.Contact, Vol2Nb20Atlas.E.SupportB };
            for (int k = 0; k < 3; k++)
            {
                Vector3 d = Rotate(p.Dir, ang[k] + (Vol2Curves.Hash(p.Seed, 20 + k) - 0.5f) * 14f);
                var a = Sheet(inst, Vol2Nb20Atlas.Cell(el[k], b), 0f, t0 + at[k], 0.03f, Vol2StrokeLayout.SheetFlat, (k & 1) == 1 ? Vol2SheetFlags.FlipAcross : Vol2SheetFlags.None, 7.3f + k);
                EndOf(inst, p, ref a);
                float sc = len[k] * s / (Vol2Nb20Atlas.L(el[k]) * 0.7f);
                // the press starts a little inside the body's footprint and the stroke runs outward
                Vector3 c = foot + d * (Vol2Nb20Atlas.L(el[k]) * sc * 0.42f - p.F1 * 0.5f);
                Place(inst, w, el[k], c, d, sc, a);
            }
        }

        protected void WriteContact(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, int fleckCount)
        {
            Body b = inst.Look.body; float t0 = inst.Rel(p.AppearUs), s = p.F0;
            Vector3 side = Vector3.Cross(Vector3.up, p.Dir).normalized;
            Vector3 c = p.Pos;
            Vector3 dMain = (side * Mathf.Cos(-26f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(-26f * Mathf.Deg2Rad)).normalized;
            Vector3 dA = (side * Mathf.Cos(38f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(38f * Mathf.Deg2Rad)).normalized;
            Vector3 dB = (side * Mathf.Cos(-72f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(-72f * Mathf.Deg2Rad)).normalized;
            if (p.F1 > 0f) WriteGroundBurst(inst, w, p, t0, s);
            // r3: a STAR out of the impact point (reference: the strokes appear one by one, each in its own direction) - the main slash crosses the point, the two support
            // strokes start at the point and fly out up-right and down-left, so the three never pile into one blob
            var a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Contact, b), 0f, t0, 0.03f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.None);
            EndOf(inst, p, ref a);
            Place(inst, w, Vol2Nb20Atlas.E.Contact, c - dMain * 0.10f * s, dMain, s * 1.1f, a);
            a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.SupportA, b), 0f, t0 + 0.0333f, 0.02f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.FlipAcross, 1.3f);
            EndOf(inst, p, ref a);
            Place(inst, w, Vol2Nb20Atlas.E.SupportA, c + dA * (0.34f * s), dA, s * 0.75f, a);
            a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.SupportB, b), 0f, t0 + 0.0667f, 0.02f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.None, 2.1f);
            EndOf(inst, p, ref a);
            Place(inst, w, Vol2Nb20Atlas.E.SupportB, c - dB * (0.30f * s) - side * 0.12f * s, -dB, s * 0.75f, a);
            bool ash = b == Body.Fire;
            for (int i = 0; i < fleckCount; i++)
            {
                float ang = (i + Vol2Curves.Hash(p.Seed, i)) * Mathf.PI * 2f / fleckCount;
                Vector3 fd = ash ? (side * Mathf.Cos(ang) * 0.45f - p.Dir * 0.25f + Vector3.up).normalized
                                 : (side * Mathf.Cos(ang) + Vector3.up * (Mathf.Sin(ang) * 0.7f + 0.2f) - p.Dir * 0.35f).normalized;
                var f = Sheet(inst, Vol2Nb20Atlas.Fleck0 + (i % 4), 0f, t0, 0f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.None, 4.0f + i);
                f.Role = Vol2StrokeRole.Drift; f.Drift = fd * (ash ? 1.4f : 2.6f); f.DriftSeconds = ash ? 0.5f : 0.22f; f.Ash = false;      // r4: fire's flecks rise like ash but stay ink (the flat grey read as white checkers)
                f.End = t0 + 0.10f; f.EndSeconds = 0.12f; f.EndCode = Vol2EndCode.Child;
                float fs = s * (0.75f + 0.5f * Vol2Curves.Hash(p.Seed, 10 + i));
                Vector3 at = c + fd * 0.12f;
                f.Width = Vol2Nb20Atlas.FleckSize.y * fs;
                // the drop flies head first: its round end (u = 0.7 of the cell) leads along the drift
                w.Sheet(at - fd * (Vol2Nb20Atlas.FleckSize.x * fs * 0.7f), at + fd * (Vol2Nb20Atlas.FleckSize.x * fs * 0.3f), f);
            }
        }

        protected static Vector3 TargetPos(Vol2VfxInstance inst, long targetId, Vector3 fallback, out float radius)
        {
            var t = inst.Model.Coordinator.Registry.Find(targetId);
            radius = t != null ? Mathf.Max(0.2f, t.Radius) : 0.45f;
            return t != null ? t.Position : fallback;
        }

        protected bool HasContact(Vol2VfxInstance inst, long targetId)
        {
            foreach (var p in inst.Parts) if (p.Kind == KContact && p.Aux == targetId) return true;
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // P0 한 번 (가 나 마 사 아)
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅏ 한 번: ignition at the brush tip, a hollow pressed ink head with a dry paper rim that FLIES at the path's 36 m/s (its position is the executor's head every frame,
    /// never frame-stepped) with a 1.5 m dry-brush tail that grows behind it, then at the first body the contact look. The head folds into the contact (0.08 s), the tail dries 0.12 s after.</summary>
    public sealed class Vol2Nb20PathRecipe : Vol2Nb20RecipeBase
    {
        const int KHead = 10, KTail = 11;
        public override Vol2Recipe Kind => Vol2Recipe.PathOneHit;
        const float HeadHeight = 1.1f;          // the judged lane height (chest)

        public override void OnBegan(Vol2VfxInstance inst)
        {
            var head = inst.AddPart(KHead, inst.StartUs);
            head.Pos = TipAtCast(inst);
            inst.AddPart(KTail, inst.StartUs).Pos = head.Pos;
            AddIgnite(inst, inst.StartUs, head.Pos);
        }

        /// <summary>The flying head's position for <paramref name="d"/> metres travelled: from the brush tip it joins the lane (chest height, the aim axis) within 3 m.</summary>
        static Vector3 PathPoint(Vol2VfxInstance inst, Vector3 tip, float d)
        {
            Vector3 lane = inst.Origin + inst.Forward * Mathf.Max(d, 0f) + Vector3.up * HeadHeight;
            Vector3 fromTip = tip + inst.Forward * Mathf.Max(d, 0f);
            return Vector3.Lerp(fromTip, lane, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / 3f)));
        }

        Vector3 Anchor(Vol2VfxInstance inst) => inst.Origin + Vector3.up * HeadHeight;

        public override Vector3 Offset(Vol2VfxInstance inst, long nowUs)
        {
            var head = inst.FirstOf(KHead);
            Vector3 tip = head != null ? head.Pos : TipAtCast(inst);
            return PathPoint(inst, tip, inst.HeadAlong(nowUs)) - Anchor(inst);
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (HasContact(inst, rec.TargetId)) return;
            Vector3 fallback = inst.Origin + inst.Forward * rec.Along;
            Vector3 pos = TargetPos(inst, rec.TargetId, fallback, out float r);
            var p = AddContact(inst, rec.NowUs, pos, r, 1f); p.Aux = rec.TargetId;
            inst.HasLanding = true; inst.Landing = pos;
        }

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            var pe = inst.Path;
            if (pe != null && pe.Finished && inst.HeadStopUs < 0)
            {
                long stop = inst.StartUs + Vol2Time.FromSeconds(pe.HeadAlong / Mathf.Max(0.01f, inst.Log.Spec.PathSpeed));
                inst.HeadStopUs = Math.Min(stop, nowUs);
                bool hit = pe.EndReason == "first";
                // the head folds into the contact (소: back to its root) - or, at the full length / a wall, dries where it stopped; the tail dries 0.12 s later with the body's own end
                inst.EndPart(inst.FirstOf(KHead), inst.HeadStopUs, hit ? Vol2EndCode.Consume : Vol2EndCode.Expire, hit ? 0.05f : DrySeconds);
                inst.EndPart(inst.FirstOf(KTail), inst.HeadStopUs + Vol2Time.FromSeconds(hit ? DryStart : 0.02f), Vol2EndCode.Expire, DrySeconds);
            }
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            Body b = inst.Look.body;
            Vector3 B0 = Anchor(inst);
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                switch (p.Kind)
                {
                    case KHead:
                    {
                        // rigid with the path head (the view sets _Offset = head position - anchor each frame); u = 0 (the wet press) LEADS
                        var a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Head, b), Vol2Nb20Atlas.W(Vol2Nb20Atlas.E.Head), 0f, 0.025f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.PaperRim | Vol2SheetFlags.MinAspect);
                        a.Role = Vol2StrokeRole.RigidOffset;
                        EndOf(inst, p, ref a);
                        Vector3 front = B0 + inst.Forward * 0.05f;
                        w.Sheet(front, front - inst.Forward * Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.Head), a);
                        break;
                    }
                    case KTail:
                    {
                        var a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Tail, b), Vol2Nb20Atlas.W(Vol2Nb20Atlas.E.Tail), 0f, 0f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.RevealByHead | Vol2SheetFlags.FlipAcross, 0.7f);
                        a.Role = Vol2StrokeRole.RigidOffset;
                        EndOf(inst, p, ref a);
                        Vector3 start = B0 - inst.Forward * 0.30f;
                        w.Sheet(start, start - inst.Forward * (Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.Tail) * 0.7f), a);      // r5: 1.05 m (the view-plane tail drawn at the head's depth read too long)
                        break;
                    }
                    case KIgnite: WriteIgnite(inst, w, p); break;
                    case KContact: WriteContact(inst, w, p, 4); break;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // A0 한 번 (거 너 머 서 어): the parry
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅓ 한 번 = the bare parry: a short dry ink pulse on the brush shaft (<= 0.45 m) and a smaller one at the sleeve for the parry window (the attachment host's 0.5 s), riding the brush;
    /// it dries in place when the window closes. Only a SUCCESSFUL parry (the combat wiring's verdict, relayed to <see cref="Vol2VfxModel.Parried"/>) splashes: a big needle star (reference
    /// 4.2 "패링") + a contact stroke + flecks at the impact point. A swing that only touches a body (ParryNoEffect) draws nothing.</summary>
    public sealed class Vol2Nb20AttachRecipe : Vol2Nb20RecipeBase
    {
        const int KPulse = 20, KSleeve = 21, KStar = 22;
        public override Vol2Recipe Kind => Vol2Recipe.AttachOnce;

        static long WindowEnd(Vol2VfxInstance inst)
        {
            var host = inst.Log.Emitter as Vol2AttachmentHost;
            return host != null ? host.EndUs : inst.StartUs + Vol2Time.FromSeconds(0.5);
        }

        public override void OnBegan(Vol2VfxInstance inst)
        {
            Vector3 tip = TipAtCast(inst);
            Vector3 grip = inst.Model.BrushGrip?.Invoke() ?? tip - inst.Forward * 0.55f - Vector3.up * 0.10f - inst.Right * 0.08f;
            long end = WindowEnd(inst);
            var pulse = inst.AddPart(KPulse, inst.StartUs); pulse.Pos = tip; pulse.Dir = (tip - grip).sqrMagnitude > 1e-4f ? (tip - grip).normalized : inst.Forward;
            inst.EndPart(pulse, end, Vol2EndCode.Child, 0.12f);
            var sleeve = inst.AddPart(KSleeve, inst.StartUs + Vol2Time.FromSeconds(0.03)); sleeve.Pos = grip; sleeve.Dir = pulse.Dir;
            inst.EndPart(sleeve, end, Vol2EndCode.Child, 0.12f);
        }

        public override Vector3 Offset(Vol2VfxInstance inst, long nowUs)
        {
            var pulse = inst.FirstOf(KPulse);
            var now = inst.Model.BrushTip?.Invoke();
            return pulse != null && now.HasValue ? now.Value - pulse.Pos : Vector3.zero;
        }

        public override bool OnParried(Vol2VfxInstance inst, Vector3 point, long nowUs)
        {
            foreach (var q in inst.Parts) if (q.Kind == KStar && nowUs - q.AppearUs < Vol2Time.FromSeconds(0.2)) return false;     // one splash per impact
            Vector3 to = point - inst.Origin; to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? to.normalized : inst.Forward;
            var star = inst.AddPart(KStar, nowUs); star.Pos = point - dir * 0.15f;
            inst.EndPart(star, nowUs + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            var c = inst.AddPart(KContact, nowUs); c.Pos = point - dir * 0.25f; c.Dir = dir; c.F0 = 0.8f;
            inst.EndPart(c, nowUs + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            return true;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            Body b = inst.Look.body;
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                switch (p.Kind)
                {
                    case KPulse:
                    case KSleeve:
                    {
                        bool sleeve = p.Kind == KSleeve;
                        var a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Pulse, b), 0f, inst.Rel(p.AppearUs), 0.05f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.PaperRim | (sleeve ? Vol2SheetFlags.FlipAcross : 0), sleeve ? 2.2f : 0f);
                        a.Role = Vol2StrokeRole.RigidOffset;
                        EndOf(inst, p, ref a);
                        float s = sleeve ? 0.65f : 1f;
                        a.Width = Vol2Nb20Atlas.W(Vol2Nb20Atlas.E.Pulse) * s * 1.25f;
                        // the shaft pulse runs from the tip back along the shaft (the wet bead at the tip); the sleeve's from the hand back up the forearm
                        Vector3 start = sleeve ? p.Pos + p.Dir * 0.04f : p.Pos + p.Dir * 0.03f;
                        w.Sheet(start, start - p.Dir * (Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.Pulse) * s), a);
                        break;
                    }
                    case KStar:
                    {
                        var a = Sheet(inst, Vol2Nb20Atlas.Ignite, 0f, inst.Rel(p.AppearUs), 0.02f, Vol2StrokeLayout.Sheet, Vol2SheetFlags.PaperRim, 5.5f);
                        EndOf(inst, p, ref a);
                        float L = Vol2Nb20Atlas.IgniteSize.x * 2.2f;
                        a.Width = Vol2Nb20Atlas.IgniteSize.y * 2.2f;
                        Vector3 d = (inst.Right + Vector3.up * 0.2f).normalized;
                        w.Sheet(p.Pos - d * (L * 0.5f), p.Pos + d * (L * 0.5f), a);
                        break;
                    }
                    case KContact: WriteContact(inst, w, p, 3); break;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // F0 한 번 (고 노 모 소 오): the instant fan bundle
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅗ 한 번: on the ground, one by one in 0.1 s - both radial edges (0 ms), the far arc (+33 ms), two inner strokes (+67 / +100 ms); the fan is never a filled black face.
    /// A contact look on each body the executor touched. Everything starts to dry 0.12 s after and is gone 0.15 s later (no residue).</summary>
    public sealed class Vol2Nb20FanRecipe : Vol2Nb20RecipeBase
    {
        const int KFan = 30;
        public override Vol2Recipe Kind => Vol2Recipe.FanOnce;

        public override void OnBegan(Vol2VfxInstance inst)
        {
            var p = inst.AddPart(KFan, inst.StartUs);
            inst.EndPart(p, inst.StartUs + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            AddIgnite(inst, inst.StartUs, TipAtCast(inst));
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (HasContact(inst, rec.TargetId)) return;
            Vector3 pos = TargetPos(inst, rec.TargetId, inst.Origin + inst.Forward * rec.Along, out float r);
            AddContact(inst, rec.NowUs, pos, r, 0.85f).Aux = rec.TargetId;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            Body b = inst.Look.body; var spec = inst.Log.Spec;
            float depth = spec.FanDepth > 0.5f ? spec.FanDepth : 5f, half = spec.FanHalfAngleRad > 0.01f ? spec.FanHalfAngleRad * Mathf.Rad2Deg : 23.578f;
            Vector3 O = inst.Origin + Vector3.up * 0.03f;
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KIgnite) { WriteIgnite(inst, w, p); continue; }
                if (p.Kind == KContact) { WriteContact(inst, w, p, 3); continue; }
                if (p.Kind != KFan) continue;
                float t0 = inst.Rel(p.AppearUs);
                int radial = Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.FanRadial, b);
                float Lr = Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.FanRadial), sc = (depth - 0.45f) / (Lr * 0.94f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 dir = Rotate(inst.Forward, s * half);
                    var a = Sheet(inst, radial, 0f, t0, 0.035f, Vol2StrokeLayout.SheetFlat, s > 0 ? Vol2SheetFlags.FlipAcross : Vol2SheetFlags.None, s * 0.9f);
                    EndOf(inst, p, ref a);
                    Place(inst, w, Vol2Nb20Atlas.E.FanRadial, O + dir * (0.45f + (depth - 0.45f) * 0.5f), dir, sc, a);
                }
                {
                    // the far arc (R = depth): authored bulging to +across, laid with its axis along -right so the bulge points away from the apex
                    float sag = depth * (1f - Mathf.Cos(half * Mathf.Deg2Rad));
                    var a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.FanArc, b), 0f, t0 + 0.0333f, 0.035f, Vol2StrokeLayout.SheetFlat, Vol2SheetFlags.None, 1.7f);
                    EndOf(inst, p, ref a);
                    float chord = 2f * depth * Mathf.Sin(half * Mathf.Deg2Rad);
                    float scA = chord / (Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.FanArc) * 0.94f);
                    Place(inst, w, Vol2Nb20Atlas.E.FanArc, O + inst.Forward * (depth - sag * 0.5f), Vector3.Cross(inst.Forward, Vector3.up).normalized, scA, a);
                }
                for (int k = 0; k < 2; k++)
                {
                    float ang = (k == 0 ? -1f : 1f) * half * 0.36f;
                    Vector3 dir = Rotate(inst.Forward, ang);
                    var a = Sheet(inst, radial, 0f, t0 + 0.0667f + 0.0333f * k, 0.035f, Vol2StrokeLayout.SheetFlat, k == 0 ? Vol2SheetFlags.FlipAcross : Vol2SheetFlags.None, 2.6f + k);
                    EndOf(inst, p, ref a);
                    Place(inst, w, Vol2Nb20Atlas.E.FanRadial, O + dir * (1.0f + 1.45f), dir, 2.9f / (Lr * 0.94f), a);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // R0 한 번 (구 누 무 수 우): the instant hollow band
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅜ 한 번: six 60 degree brush strokes on the band (radius 2 m, inside the 0.6 m band, the centre empty), front first then the sides then the back (0 / 33 / 67 / 100 ms),
    /// and a standing pair (1.1 m) at +/-20 degrees on the front of the band; a contact look on each body crossing it. All of it dries together 0.12 s after (no residue).</summary>
    public sealed class Vol2Nb20RingRecipe : Vol2Nb20RecipeBase
    {
        const int KBand = 40;
        public override Vol2Recipe Kind => Vol2Recipe.RingOnce;

        public override void OnBegan(Vol2VfxInstance inst)
        {
            var p = inst.AddPart(KBand, inst.StartUs);
            inst.EndPart(p, inst.StartUs + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            AddIgnite(inst, inst.StartUs, TipAtCast(inst));
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (HasContact(inst, rec.TargetId)) return;
            Vector3 pos = TargetPos(inst, rec.TargetId, inst.Origin + inst.Forward * rec.Along, out float r);
            AddContact(inst, rec.NowUs, pos, r, 0.85f).Aux = rec.TargetId;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            Body b = inst.Look.body; var spec = inst.Log.Spec;
            float R = spec.RingRadius > 0.5f ? spec.RingRadius : 2f;
            Vector3 O = inst.Origin + Vector3.up * 0.03f;
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KIgnite) { WriteIgnite(inst, w, p); continue; }
                if (p.Kind == KContact) { WriteContact(inst, w, p, 3); continue; }
                if (p.Kind != KBand) continue;
                float t0 = inst.Rel(p.AppearUs);
                float sag = R * (1f - Mathf.Cos(30f * Mathf.Deg2Rad));
                float sc = (2f * R * Mathf.Sin(31.5f * Mathf.Deg2Rad)) / (Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.RingArc) * 0.94f);
                int cell = Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.RingArc, b);
                float[] order = { 0f, 0.0333f, 0.0333f, 0.0667f, 0.0667f, 0.1f };
                float[] angles = { 0f, -60f, 60f, -120f, 120f, 180f };
                for (int k = 0; k < 6; k++)
                {
                    Vector3 dir = Rotate(inst.Forward, angles[k]);
                    var flags = (k & 1) == 1 ? Vol2SheetFlags.FlipAlong : Vol2SheetFlags.None;
                    var a = Sheet(inst, cell, 0f, t0 + order[k], 0.035f, Vol2StrokeLayout.SheetFlat, flags, k * 0.77f);
                    EndOf(inst, p, ref a);
                    Place(inst, w, Vol2Nb20Atlas.E.RingArc, O + dir * (R - sag * 0.5f), Vector3.Cross(dir, Vector3.up).normalized, sc, a);
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 at = O + Rotate(inst.Forward, s * 20f) * R;
                    var a = Sheet(inst, Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Standing, b), 0f, t0 + 0.05f, 0.03f, Vol2StrokeLayout.Sheet, s > 0 ? Vol2SheetFlags.FlipAcross : Vol2SheetFlags.None, 6f + s);
                    EndOf(inst, p, ref a);
                    float L = Vol2Nb20Atlas.L(Vol2Nb20Atlas.E.Standing);
                    a.Width = Vol2Nb20Atlas.W(Vol2Nb20Atlas.E.Standing);
                    // the press at the ground, the stroke rises 1.1 m (the authored stroke spans u 0.05 .. 0.86)
                    Vector3 start = at - Vector3.up * (L * 0.05f) + Vector3.up * 0.02f;
                    w.Sheet(start, start + Vector3.up * L, a);
                }
            }
        }
    }
}
