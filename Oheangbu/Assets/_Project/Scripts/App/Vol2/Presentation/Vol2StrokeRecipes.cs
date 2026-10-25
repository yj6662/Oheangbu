using System;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // stroke20 (D308-150 V7, user questionnaire 2026-10-25: "앞잡 한 획을 작게"): the 20 no-batchim spells - P0 가나마사아, A0 거너머서어, F0 고노모소오, R0 구누무수우 - drawn with the
    // user-approved 앞잡 one-stroke ink (D308-148: RiposteStroke308Path's geometry, RiposteInk308's dry-brush cut), scaled down to the vol.2 sizes (head ~0.3 m against the 앞잡's 1.5 m) and
    // shortened to the plan B beats. Every stroke is a Vol2InkWriter.Brush ribbon (layout Brush / BrushFlat of the shared Vol2Ink material): per-point width and ink as the 앞잡 writes them
    // (pressed wet start, thin starved end), written in brush order with a ragged head, starved from the edges in, dried through the crack field. NOT copied from the 앞잡: its colour drain,
    // its value-inversion flicker, its size and its 1.2 s linger. The body (오행) shows only as a faint pigment in the starved fibres and a small end accent (wood fibres forward, fire ash up and
    // burnt holes, earth plates stepping down, metal a straight snapped bar, water an open hook and a drip). Every time is an executor timestamp (cast start, contact frame, head stop, the parry
    // window's end) plus a plan B look beat. The NB20 / slice 9a recipes stay in the code, unregistered.

    /// <summary>Shared drawing of the stroke20 recipes.</summary>
    public abstract class Vol2StrokeRecipeBase : Vol2VfxRecipe
    {
        protected const int KIgnite = 1, KContact = 2, KSwing = 3;
        /// <summary>r9: the body's swing state the presenter asks SpellSwing308 for (SPEC-SPELL-SWING-308 states), and how soon its apex falls after the cast (plan B's first beat).</summary>
        public abstract string SwingState { get; }
        public const float SwingToApex = 0.06f;

        /// <summary>D308-150 V8 ("전체를 옛 것처럼 진하게"): the body colour of the stroke20 ink = the OLD 120 effects' pigment of the row's body (Art/SpellVFX120/Profiles
        /// 001_AC00 가 / 025_B098 나 / 049_B9C8 마 / 073_C0AC 사 / 097_C544 아, field Pigment; read once 2026-10-25, sRGB). The presenter puts it on the stroke's property block
        /// in place of the plan C base colour (fire and metal are the same; wood, earth and water are the old, darker ones).</summary>
        public static Color OldPigment(Body b)
        {
            switch (b)
            {
                case Body.Wood: return new Color(0.2588f, 0.4314f, 0.3451f, 1f);
                case Body.Fire: return new Color(0.7451f, 0.3294f, 0.2314f, 1f);
                case Body.Earth: return new Color(0.5922f, 0.4510f, 0.2627f, 1f);
                case Body.Metal: return new Color(0.6510f, 0.6902f, 0.6784f, 1f);
                default: return new Color(0.1725f, 0.2745f, 0.3529f, 1f);
            }
        }
        /// <summary>D308-150 V8 (무받침 맞은 자국 0.6 - 1 s, visual only - the judgement still ends per plan B): the contact mark holds this long, then cracks over <see cref="MarkDry"/>.</summary>
        public const float MarkHold = 0.5f, MarkDry = 0.3f;
        /// <summary>r9: the swing's ink - the brush tip's own path as a 오의 stroke - is sampled this long after the cast, then cracks with the cast's look.</summary>
        public const float SwingSampleSeconds = 0.26f;
        /// <summary>Plan B: the contact accent (0.09 s) and its settling: the contact look starts to crack 0.12 s after the contact and is gone 0.15 s later (".12 - .18 s").</summary>
        public const float DryStart = 0.12f, DrySeconds = 0.15f;
        /// <summary>The 앞잡 / vol.2 scale: the 앞잡 head is 1.5 m wide, a vol.2 contact stroke head ~0.36 m.</summary>
        public const float SlashScale = 0.24f;
        /// <summary>The torso height of a body's contact stroke above its ground (the 앞잡 aims at the collider centre; a vol.2 target gives a ground point and a radius).</summary>
        public const float TorsoHeight = 0.85f;

        protected readonly List<Vector3> P = new List<Vector3>(64);
        protected readonly List<float> W = new List<float>(64), K = new List<float>(64);
        readonly RiposteStroke308Path _slash = new RiposteStroke308Path();

        /// <summary>A brush attribute: written over <paramref name="revealSeconds"/> from <paramref name="appear"/> (0 = at once), on the plane of <paramref name="normal"/> (zero = facing the camera).</summary>
        protected static Vol2StrokeAttr Brush(Vol2VfxInstance inst, float appear, float revealSeconds, float length, Vol2StrokeLayout layout, Vector3 normal, float seedOffset)
        {
            var a = Vol2StrokeAttr.Default;
            a.Layout = layout; a.Appear = appear; a.Speed = revealSeconds <= 0f ? 1e5f : Mathf.Max(0.5f, length / revealSeconds);
            a.Normal = normal; a.Seed = inst.Seed + seedOffset; a.Hollow = 1f; a.Cell = -1; a.Role = Vol2StrokeRole.Normal;
            return a;
        }

        /// <summary>The end of a stroke from its part (executor event frame + the look-tail), or none yet.</summary>
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

        protected static Vector3 Rotate(Vector3 v, float degrees, Vector3 axis) => Quaternion.AngleAxis(degrees, axis) * v;
        protected static float Len(List<Vector3> pts) { float l = 0f; for (int i = 1; i < pts.Count; i++) l += Vector3.Distance(pts[i - 1], pts[i]); return l; }

        /// <summary>Writes the collected points as one brush ribbon.</summary>
        protected void Emit(Vol2InkWriter w, Vol2StrokeAttr a)
        {
            if (P.Count < 2) return;
            float m = 0f; foreach (float x in W) m = Mathf.Max(m, x);
            a.Width = m;
            w.Brush(P, W, K, a);
        }

        /// <summary>A brush stroke from <paramref name="a"/> to <paramref name="b"/> bowed by <paramref name="bow"/> metres toward <paramref name="bowDir"/>: the 앞잡 width law (the press swells to
        /// <paramref name="w0"/> at <paramref name="peak"/> of the way, then runs down to <paramref name="w1"/>), ink <paramref name="k0"/> -> <paramref name="k1"/>.</summary>
        protected void Line(Vector3 a, Vector3 b, Vector3 bowDir, float bow, float w0, float w1, float k0, float k1, int n = 10, float peak = 0.14f)
        {
            P.Clear(); W.Clear(); K.Clear();
            for (int i = 0; i <= n; i++)
            {
                float q = i / (float)n;
                P.Add(Vector3.Lerp(a, b, q) + bowDir * (bow * 4f * q * (1f - q)));
                float swell = Mathf.SmoothStep(0f, 1f, q / Mathf.Max(0.01f, peak));
                float ease = Mathf.SmoothStep(0f, 1f, (q - peak) / Mathf.Max(0.01f, 1f - peak));
                W.Add(Mathf.Lerp(Mathf.Lerp(w0 * 0.55f, w0, swell), w1, Mathf.Pow(ease, 0.8f)));
                K.Add(Mathf.Lerp(k0, k1, Mathf.Pow(q, 1.2f)));
            }
        }

        /// <summary>Brush tip at the cast (the player's great brush when the scene has one; else the right hand at chest height a little ahead).</summary>
        protected static Vector3 TipAtCast(Vol2VfxInstance inst)
        {
            var t = inst.Model.BrushTip?.Invoke();
            return t ?? inst.Origin + inst.Right * 0.34f + Vector3.up * 1.22f + inst.Forward * 0.42f;
        }

        /// <summary>The settled shoulder camera of the cast (plan G: pivot 1.7 m, local (0.7, 0.55, -2.8), pitch 0): the eye the 앞잡 path is aimed with.</summary>
        protected static Vector3 Eye(Vol2VfxInstance inst) => inst.Origin + Vector3.up * 2.25f + inst.Right * 0.7f - inst.Forward * 2.8f;

        // ---------------------------------------------------------------- the swing's ink (r9): the 오의 stroke the brush tip itself writes

        /// <summary>Starts the brush tip's ink at the cast. It cracks over 0.15 s from the end of the sampling window (the slam and its follow-through; inside the plan B dry-out of a no-batchim cast): the old route's SwingInk308 arc, written with
        /// the 오의 ribbon. Nothing is drawn without a brush in the scene (a fixture).</summary>
        protected Vol2VfxPart AddSwing(Vol2VfxInstance inst)
        {
            var p = inst.AddPart(KSwing, inst.StartUs);
            p.Trail = new List<Vector3>(32);
            inst.EndPart(p, inst.StartUs + Vol2Time.FromSeconds(SwingSampleSeconds), Vol2EndCode.Expire, DrySeconds);
            return p;
        }

        /// <summary>Samples the live brush tip into the swing part (on the scope clock, inside the window, a point every 3 cm).</summary>
        protected static void SampleSwing(Vol2VfxInstance inst, long nowUs)
        {
            var p = inst.FirstOf(KSwing);
            // r10: not in the first 0.03 s - the arm snaps into the clip there (it starts 0.08 s before its apex), and that jump is not a brush movement
            if (p == null || p.Trail == null || inst.Rel(nowUs) > SwingSampleSeconds || inst.Rel(nowUs) < 0.03f) return;
            var tip = inst.Model.BrushTip?.Invoke();
            if (!tip.HasValue) return;
            if (p.Trail.Count == 0 || (p.Trail[p.Trail.Count - 1] - tip.Value).sqrMagnitude > 0.0009f) { p.Trail.Add(tip.Value); inst.Dirty = true; }
        }

        /// <summary>The tip's path as one brush ribbon facing the camera: the newest point is the press (wet, 0.2 m), the oldest the dry start of the arc.</summary>
        protected void WriteSwing(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p)
        {
            var tr = p.Trail;
            if (tr == null || tr.Count < 3) return;
            P.Clear(); W.Clear(); K.Clear();
            int n = tr.Count;
            // r10: the samples are a frame apart on a fast swing: a Catmull-Rom curve through them (4 points a span), so the arc is a curve, not a polygon of tape
            const int sub = 4;
            int total = (n - 1) * sub;
            for (int i = 0; i <= total; i++)
            {
                int seg = Mathf.Min(i / sub, n - 2); float f = (i - seg * sub) / (float)sub;
                Vector3 p0 = tr[Mathf.Max(0, seg - 1)], p1 = tr[seg], p2 = tr[seg + 1], p3 = tr[Mathf.Min(n - 1, seg + 2)];
                float f2 = f * f, f3 = f2 * f;
                P.Add(0.5f * (2f * p1 + (-p0 + p2) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (-p0 + 3f * p1 - 3f * p2 + p3) * f3));
                float q = i / (float)total;                                         // 0 = the wind-up (dry), 1 = where the brush is now (pressed)
                W.Add(Mathf.Lerp(0.02f, 0.15f, Mathf.SmoothStep(0f, 1f, Mathf.Pow(q, 0.7f))));
                K.Add(Mathf.Lerp(0.3f, 1f, Mathf.Pow(q, 0.8f)));
            }
            var a = Brush(inst, 0f, 0f, 1f, Vol2StrokeLayout.Brush, Vector3.zero, 8.8f);
            EndOf(inst, p, ref a); Emit(w, a);
        }

        public override void Update(Vol2VfxInstance inst, long nowUs) { SampleSwing(inst, nowUs); }

        // ---------------------------------------------------------------- ignition (the first cue, <= 0.08 s)

        protected Vol2VfxPart AddIgnite(Vol2VfxInstance inst, long atUs, Vector3 at)
        {
            var p = inst.AddPart(KIgnite, atUs); p.Pos = at;
            inst.EndPart(p, atUs + Vol2Time.FromSeconds(0.05), Vol2EndCode.Child, 0.06f);       // reference: a blot + needle lines at the brush tip for ~0.1 s, dries in place
            return p;
        }

        /// <summary>The brush tip ignites: a small pressed blot and three needle lines thrown forward (Sonnet r1: the first 0.08 s is the brush tip's own mark).</summary>
        protected void WriteIgnite(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p)
        {
            float t0 = inst.Rel(p.AppearUs);
            Vector3 f = (inst.Forward + Vector3.up * 0.15f).normalized;
            Line(p.Pos - f * 0.05f, p.Pos + f * 0.11f, Vector3.up, 0.01f, 0.13f, 0.07f, 1f, 0.8f, 4, 0.3f);
            var a = Brush(inst, t0, 0.02f, 0.16f, Vol2StrokeLayout.Brush, Vector3.zero, 3.1f);
            EndOf(inst, p, ref a); Emit(w, a);
            Needles(inst, w, p, p.Pos + f * 0.06f, f, t0, 0.32f, 3);
        }

        /// <summary>Very thin straight lines out of a point (the reference's needle lines): written in 0.02 s, gone with their part.</summary>
        protected void Needles(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, Vector3 at, Vector3 dir, float t0, float length, int n)
        {
            Vector3 side = Vector3.Cross(dir, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = inst.Right; side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;
            for (int k = 0; k < n; k++)
            {
                float ang = (k - (n - 1) * 0.5f) * 28f + (Vol2Curves.Hash(p.Seed, 40 + k) - 0.5f) * 14f;
                Vector3 d = Rotate(dir, ang, up);
                float len = length * (0.7f + 0.5f * Vol2Curves.Hash(p.Seed, 50 + k));
                Line(at + d * 0.04f, at + d * len, up, 0f, 0.016f, 0.008f, 1f, 0.7f, 3, 0.05f);
                var a = Brush(inst, t0 + 0.01f * k, 0.02f, len, Vol2StrokeLayout.Brush, Vector3.zero, 11f + k);
                EndOf(inst, p, ref a);
                a.End = Mathf.Min(a.End, t0 + 0.05f); a.EndSeconds = Mathf.Min(a.EndSeconds, 0.06f);
                Emit(w, a);
            }
        }

        // ---------------------------------------------------------------- the contact stroke: a small 앞잡

        /// <summary>A contact look at a body: its torso mark is on the body's side facing the caster (so the stroke is not buried in the body mesh), at <see cref="TorsoHeight"/>.</summary>
        protected Vol2VfxPart AddContact(Vol2VfxInstance inst, long atUs, Vector3 bodyPos, float radius, float scale)
        {
            atUs = Math.Max(atUs, inst.StartUs + Vol2Time.FromSeconds(SwingToApex));      // V8: nothing lands before the brush's apex
            var p = inst.AddPart(KContact, atUs);
            Vector3 to = bodyPos - inst.Origin; to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? to.normalized : inst.Forward;
            p.Pos = new Vector3(bodyPos.x, inst.Origin.y + TorsoHeight, bodyPos.z) - dir * (radius * 0.7f);
            p.Dir = dir; p.F0 = scale; p.F1 = radius;
            inst.EndPart(p, atUs + Vol2Time.FromSeconds(MarkHold), Vol2EndCode.Expire, MarkDry);
            return p;
        }

        /// <summary>The 앞잡 one stroke, made small: RiposteStroke308Path laid out with the 앞잡 shape scaled by <see cref="SlashScale"/> x <paramref name="scale"/> through the torso mark
        /// <paramref name="torso"/> (plane yawed 35 degrees off the caster -> body line, slash 38 degrees, lead from the upper left, the dry tail down-right), written in 0.06 s.</summary>
        protected Vector3 Slash(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, Vector3 caster, Vector3 torso, float t0, float scale, out Vector3 tailDir, out Vector3 planeN)
        {
            float k = SlashScale * scale;
            var shape = RiposteStroke308Shape.Default;
            shape.Samples = 32;
            // r2: the 앞잡 proportions read as a sweep only when the stroke is long against its width - a straight 1/4 scale made a club; the lead and the tail are kept longer
            // (lead 1.2 m, tail 0.95 m at scale 1) and the head a little narrower (0.32 m); the plane faces the shoulder camera more (yaw 15) so the tail does not run behind the body
            // r7: yawed the other way (-20) and a flatter slash (30): the dry tail - where the body accent is - comes out in front of the body and past its side instead of into it
            shape.PlaneYawDegrees = -20f; shape.SlashDegrees = 30f;
            shape.LeadIn *= k * 1.5f; shape.TorsoHalf = Mathf.Max(0.15f * scale, shape.TorsoHalf * k); shape.TailLength *= k * 2.2f; shape.TailMaxReach *= k * 2f; shape.TailFloorDrop *= k * 2f;
            shape.HeadWidth *= k * 0.9f; shape.TorsoWidth *= k * 1.3f; shape.TailWidth = Mathf.Max(0.012f, shape.TailWidth * k);
            var aim = new RiposteStroke308Aim { Caster = caster, Eye = Eye(inst), Torso = torso, TorsoRadius = 0f, HasBrushTip = false };
            _slash.Build(shape, aim);
            P.Clear(); W.Clear(); K.Clear();
            for (int i = 0; i < _slash.Count; i++) { P.Add(_slash.SampleWorld(i)); W.Add(_slash.Width[i]); K.Add(_slash.Ink[i]); }
            var a = Brush(inst, t0, 0.06f, _slash.Length, Vol2StrokeLayout.Brush, _slash.N, p.Seed * 1.3f);
            EndOf(inst, p, ref a); Emit(w, a);
            int n = _slash.Count;
            tailDir = (_slash.SampleWorld(n - 1) - _slash.SampleWorld(n - 2)).normalized;
            planeN = _slash.N;
            return _slash.SampleWorld(n - 1);
        }

        /// <summary>The contact look: the small 앞잡 through the torso mark, two needle lines and 3 - 5 flecks out of it, and the body's end accent at the stroke's dry tail.</summary>
        protected void WriteContact(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, int fleckCount)
        {
            float t0 = inst.Rel(p.AppearUs), s = p.F0;
            Vector3 caster = p.Pos - p.Dir * 3f; caster.y = inst.Origin.y;
            Vector3 tail = Slash(inst, w, p, caster, p.Pos, t0, s, out Vector3 tailDir, out Vector3 n);
            Needles(inst, w, p, p.Pos, (-p.Dir + Vector3.up * 0.6f).normalized, t0 + 0.01f, 0.42f * s, 2);
            Flecks(inst, w, p, p.Pos, t0, fleckCount, s);
            EndAccent(inst, w, p, tail, tailDir, n, t0 + 0.05f, s);
        }

        /// <summary>3 - 5 flecks of dry ink thrown out of the contact (fire: burnt ash that rises), short drift with drag, dried 0.1 s after.</summary>
        protected void Flecks(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, Vector3 at, float t0, int count, float s)
        {
            bool ash = inst.Look.body == Body.Fire;
            Vector3 side = Vector3.Cross(Vector3.up, p.Dir).normalized;
            for (int i = 0; i < count; i++)
            {
                float ang = (i + Vol2Curves.Hash(p.Seed, i)) * Mathf.PI * 2f / count;
                Vector3 fd = ash ? (side * Mathf.Cos(ang) * 0.45f - p.Dir * 0.25f + Vector3.up).normalized
                                 : (side * Mathf.Cos(ang) + Vector3.up * (Mathf.Sin(ang) * 0.7f + 0.2f) - p.Dir * 0.35f).normalized;
                float sz = s * (0.06f + 0.05f * Vol2Curves.Hash(p.Seed, 10 + i));
                Vector3 c = at + fd * 0.10f;
                Line(c - fd * sz, c + fd * sz * 0.4f, Vector3.up, 0f, sz * 1.1f, sz * 0.6f, 1f, 0.85f, 2, 0.3f);
                var a = Brush(inst, t0, 0f, sz, Vol2StrokeLayout.Brush, Vector3.zero, 4f + i);
                a.Role = Vol2StrokeRole.Drift; a.Drift = fd * (ash ? 1.2f : 2.4f); a.DriftSeconds = ash ? 0.45f : 0.2f; a.Ash = ash;
                a.End = t0 + 0.10f; a.EndSeconds = 0.12f; a.EndCode = Vol2EndCode.Child;
                Emit(w, a);
            }
        }

        /// <summary>The body's end (plan C, as a small accent at the dry end of a stroke): wood three torn fibres running on forward, fire ash rising, earth two low plates stepping down,
        /// metal a straight bar snapped across, water an open hook and a drip. Ends with the stroke's part.</summary>
        protected void EndAccent(Vol2VfxInstance inst, Vol2InkWriter w, Vol2VfxPart p, Vector3 at, Vector3 dir, Vector3 planeN, float t0, float s)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = inst.Forward;
            Vector3 axis = planeN.sqrMagnitude > 1e-4f ? planeN.normalized : Vector3.Cross(dir, Vector3.up).normalized;
            switch (inst.Look.body)
            {
                case Body.Wood:
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector3 d = Rotate(dir, k * 15f + (Vol2Curves.Hash(p.Seed, 60 + k) - 0.5f) * 8f, axis);
                        float len = s * (k == 0 ? 0.30f : 0.20f + 0.06f * Vol2Curves.Hash(p.Seed, 64 + k));
                        Line(at - dir * 0.03f, at + d * len, axis, 0.01f, 0.035f * s, 0.008f, 0.75f, 0.25f, 5, 0.1f);
                        var a = Brush(inst, t0 + 0.012f * (k + 1), 0.04f, len, Vol2StrokeLayout.Brush, planeN, 21f + k);
                        EndOf(inst, p, ref a); Emit(w, a);
                    }
                    break;
                case Body.Fire:
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 fd = (Vector3.up + inst.Right * ((Vol2Curves.Hash(p.Seed, 70 + k) - 0.5f) * 0.9f)).normalized;
                        float sz = s * (0.035f + 0.03f * Vol2Curves.Hash(p.Seed, 74 + k));
                        Vector3 c = at - dir * (0.05f * k);
                        Line(c, c + fd * sz, Vector3.up, 0f, sz * 1.2f, sz * 0.8f, 0.9f, 0.7f, 2, 0.3f);
                        var a = Brush(inst, t0 + 0.02f * k, 0f, sz, Vol2StrokeLayout.Brush, Vector3.zero, 31f + k);
                        a.Role = Vol2StrokeRole.Drift; a.Drift = fd * 0.9f; a.DriftSeconds = 0.5f; a.Ash = true;
                        EndOf(inst, p, ref a);
                        Emit(w, a);
                    }
                    break;
                case Body.Earth:
                {
                    // two short low plates: stepping DOWN from a stroke end in the air, stepping OUT along a stroke end on the ground (lying flat)
                    bool ground = at.y - inst.Origin.y < 0.2f;
                    Vector3 h = dir; h.y = 0f; h = h.sqrMagnitude > 1e-4f ? h.normalized : inst.Right;
                    Vector3 hs = Vector3.Cross(Vector3.up, h);
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 c = ground ? at + h * ((0.10f + 0.13f * k) * s) + hs * ((k == 0 ? 0.05f : -0.04f) * s)
                                           : at + Vector3.down * ((0.07f + 0.08f * k) * s) + h * (0.04f * k);
                        float len = s * (0.17f - 0.04f * k);
                        Vector3 along = ground ? hs : h;
                        Line(c - along * len * 0.5f, c + along * len * 0.5f, Vector3.up, 0f, 0.085f * s, 0.075f * s, 1f, 0.8f, 3, 0.2f);
                        var a = Brush(inst, t0 + 0.02f * k, 0.03f, len, ground ? Vol2StrokeLayout.BrushFlat : Vol2StrokeLayout.Brush, ground ? Vector3.up : Vector3.zero, 41f + k);
                        EndOf(inst, p, ref a); Emit(w, a);
                    }
                    break;
                }
                case Body.Metal:
                {
                    Vector3 bar = Vector3.Cross(dir, axis).normalized;
                    float half = 0.12f * s;
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 a0 = k == 0 ? at - bar * half : at + bar * 0.025f, a1 = k == 0 ? at - bar * 0.025f : at + bar * half;
                        Line(a0, a1, axis, 0f, 0.03f * s + 0.004f, 0.028f * s + 0.004f, 1f, 1f, 2, 0.05f);
                        var a = Brush(inst, t0 + 0.015f * k, 0.02f, half, Vol2StrokeLayout.Brush, planeN, 51f + k);
                        EndOf(inst, p, ref a); Emit(w, a);
                    }
                    break;
                }
                default:
                {
                    // water: the end hooks back open (a curl of ~150 degrees in the stroke's plane), and one drop runs down
                    P.Clear(); W.Clear(); K.Clear();
                    float seg = 0.045f * s; Vector3 cur = at, d = dir;
                    for (int i = 0; i <= 6; i++)
                    {
                        P.Add(cur); float q = i / 6f;
                        W.Add(Mathf.Lerp(0.045f * s, 0.012f, q)); K.Add(Mathf.Lerp(0.8f, 0.3f, q));
                        d = Rotate(d, 25f, axis); cur += d * seg;
                    }
                    var a = Brush(inst, t0, 0.04f, seg * 6f, Vol2StrokeLayout.Brush, planeN, 61f);
                    EndOf(inst, p, ref a); Emit(w, a);
                    float sz = 0.05f * s;
                    Line(at, at + Vector3.down * sz, Vector3.right, 0f, sz * 0.9f, sz * 0.6f, 1f, 0.8f, 2, 0.3f);
                    var b = Brush(inst, t0 + 0.03f, 0f, sz, Vol2StrokeLayout.Brush, Vector3.zero, 62f);
                    b.Role = Vol2StrokeRole.Drift; b.Drift = Vector3.down * 0.8f; b.DriftSeconds = 0.4f;
                    EndOf(inst, p, ref b);
                    Emit(w, b);
                    break;
                }
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
    /// <summary>ㅏ 한 번: the brush tip ignites, ONE flying brush stroke leaves it and runs the lane at the path's 36 m/s - pressed wet head (0.3 m), a short starving tail (1.4 m) that
    /// cracks away behind it - and at the first body a small 앞잡 slashes through the torso mark with two needles, flecks and the body's end accent. 0.12 s after the contact everything
    /// cracks and is gone 0.15 s later (no residue). A miss dries where the head stopped.</summary>
    public sealed class Vol2StrokePathRecipe : Vol2StrokeRecipeBase
    {
        const int KTrail = 10;
        public override Vol2Recipe Kind => Vol2Recipe.PathOneHit;
        public override string SwingState => "SwingSweep";
        /// <summary>The judged lane height (chest) and the flying stroke's numbers.</summary>
        public const float LaneHeight = 1.1f, TrailWidth = 0.30f, TrailTail = 1.6f, ArcHeight = 0.45f;

        public override void OnBegan(Vol2VfxInstance inst)
        {
            var trail = inst.AddPart(KTrail, inst.StartUs);
            trail.Pos = TipAtCast(inst);
            // r4 (look only, decides nothing): the first body in the lane at the cast, so the flying stroke can arc over and come down into its torso mark where the small 앞잡 starts
            var spec = inst.Log.Spec;
            float hit = spec.PathLength; bool found = false;
            foreach (var t in inst.Model.Coordinator.Targets)
            {
                if (t == null || !t.Alive) continue;
                Vector3 rel = t.Position - inst.Origin;
                float along = Vector3.Dot(rel, inst.Forward), lat = Mathf.Abs(Vector3.Dot(rel, inst.Right));
                if (along > 0.3f && along - t.Radius * 0.7f < hit && lat <= spec.PathWidth * 0.5f + t.Radius) { hit = Mathf.Max(0.6f, along - t.Radius * 0.7f); found = true; }
            }
            trail.F0 = hit; trail.F1 = found ? 1f : 0f;
            AddIgnite(inst, inst.StartUs + Vol2Time.FromSeconds(SwingToApex), trail.Pos);
            AddSwing(inst);
        }

        static float TipAlong(Vol2VfxInstance inst, Vector3 tip) => Mathf.Max(0f, Vector3.Dot(tip - inst.Origin, inst.Forward));

        /// <summary>The flying stroke's centre line at <paramref name="d"/> metres along the aim (the trail part: Pos = the brush tip at the cast, F0 = the predicted landing, F1 = 1 when a body).</summary>
        static Vector3 TrailPoint(Vol2VfxInstance inst, Vol2VfxPart p, float d, float a0, float len)
        {
            float hit = Mathf.Max(a0 + 0.3f, p.F0 > 0f ? p.F0 : len);
            Vector3 tipRel = p.Pos - inst.Origin;
            float tipLat = Vector3.Dot(tipRel, inst.Right), tipY = tipRel.y, endY = p.F1 > 0.5f ? TorsoHeight : LaneHeight;
            float arc = ArcHeight * Mathf.Clamp(hit / 4f, 0.4f, 1f), join = Mathf.Min(5f, (hit - a0) * 0.85f);
            float q = Mathf.Clamp01((d - a0) / (hit - a0));
            float y = Mathf.Lerp(tipY, endY, Mathf.SmoothStep(0f, 1f, q)) + arc * Mathf.Sin(Mathf.PI * q);
            float lat = tipLat * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - a0) / join)));
            return inst.Origin + inst.Forward * d + inst.Right * lat + Vector3.up * y;
        }

        /// <summary>Arc length of the flying stroke from the brush tip to <paramref name="along"/> metres (what the shader reveals up to).</summary>
        static float TrailArc(Vol2VfxInstance inst, Vol2VfxPart p, float along)
        {
            float a0 = TipAlong(inst, p.Pos), len = Mathf.Max(a0 + 1f, inst.Log.Spec.PathLength);
            float to = Mathf.Clamp(along, a0, len), s = 0f;
            const int n = 24;
            Vector3 prev = TrailPoint(inst, p, a0, a0, len);
            for (int i = 1; i <= n; i++) { Vector3 c = TrailPoint(inst, p, Mathf.Lerp(a0, to, i / (float)n), a0, len); s += Vector3.Distance(prev, c); prev = c; }
            return s;
        }

        /// <summary>The shader reveals the trail up to (Head - LineStart) metres of its arc; the trail's arc starts at the brush tip, so the head is the travelled distance past the tip.</summary>
        public override float Head(Vol2VfxInstance inst, long nowUs)
        {
            var trail = inst.FirstOf(KTrail);
            if (trail == null) return inst.HeadAlong(nowUs);
            // V8 (visual timing only; the executor's launch and contact are unchanged): the head waits at the brush tip until the swing's apex, then flies so that it
            // reaches the predicted first body exactly when the executor touches it (faster than 36 m/s over a shorter time); never ahead of the executor's own head
            float exec = inst.HeadAlong(nowUs), t = inst.Rel(nowUs), a0 = TipAlong(inst, trail.Pos);
            float hit = trail.F0 > 0f ? trail.F0 : inst.Log.Spec.PathLength, tHit = hit / Mathf.Max(0.01f, inst.Log.Spec.PathSpeed);
            float along;
            if (t <= SwingToApex) along = Mathf.Min(a0, exec);
            else if (t < tHit && tHit > SwingToApex + 0.005f) along = Mathf.Min(exec, a0 + (hit - a0) * Mathf.Clamp01((t - SwingToApex) / (tHit - SwingToApex)));
            else along = exec;
            return TrailArc(inst, trail, along) + Vol2PathRecipeBase.LineStart;
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (HasContact(inst, rec.TargetId) || inst.HasLanding) return;          // the first body only (ㅏ 한 번)
            Vector3 pos = TargetPos(inst, rec.TargetId, inst.Origin + inst.Forward * rec.Along, out float r);
            var p = AddContact(inst, rec.NowUs, pos, r, 1.35f); p.Aux = rec.TargetId;      // V8 r14: the hit mark as big as the old 가's impact reads (still ~4:1 under the 오의)
            inst.HasLanding = true; inst.Landing = pos;
        }

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            SampleSwing(inst, nowUs);
            var pe = inst.Path;
            if (pe != null && pe.Finished && inst.HeadStopUs < 0)
            {
                long stop = inst.StartUs + Vol2Time.FromSeconds(pe.HeadAlong / Mathf.Max(0.01f, inst.Log.Spec.PathSpeed));
                inst.HeadStopUs = Math.Min(stop, nowUs);
                bool hit = pe.EndReason == "first";
                // the head stays pressed on the body through the accent, then the whole trail cracks with the contact stroke; a miss dries where it stopped
                inst.EndPart(inst.FirstOf(KTrail), inst.HeadStopUs + Vol2Time.FromSeconds(hit ? DryStart : 0.02f), Vol2EndCode.Expire, DrySeconds);
            }
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var spec = inst.Log.Spec;
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                switch (p.Kind)
                {
                    case KTrail:
                    {
                        // from the brush tip it joins the lane (chest height on the aim axis) within 3 m and runs on to the full path length; facing the camera around its axis
                        float a0 = TipAlong(inst, p.Pos), len = Mathf.Max(a0 + 1f, spec.PathLength);
                        P.Clear(); W.Clear(); K.Clear();
                        const int n = 40;
                        // r4: the stroke keeps to the brush hand's side first (the lane on the caster's axis is hidden behind the caster from the shoulder camera) and joins the lane
                        // before the body; it arcs over the lane (up to 0.45 m) so it is seen against the ground, not lost on the dark caster and body, and comes down into the torso mark
                        // of the first body (or the lane at full length); it leaves the judged lane by at most the hand's offset and the arc
                        for (int i = 0; i <= n; i++)
                        {
                            float d = a0 + (len - a0) * Mathf.Pow(i / (float)n, 1.4f);
                            P.Add(TrailPoint(inst, p, d, a0, len));
                            W.Add(TrailWidth); K.Add(1f);
                        }
                        var a = Brush(inst, 0f, 0f, len, Vol2StrokeLayout.Brush, Vector3.zero, 0.7f);
                        a.Role = Vol2StrokeRole.FollowHead; a.TailLen = TrailTail;
                        EndOf(inst, p, ref a); Emit(w, a);
                        break;
                    }
                    case KIgnite: WriteIgnite(inst, w, p); break;
                    case KContact: WriteContact(inst, w, p, 4); break;
                    case KSwing: WriteSwing(inst, w, p); break;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // A0 한 번 (거 너 머 서 어): the parry
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅓ 한 번 = the bare parry: a short pressed ink pulse runs back down the brush shaft from the tip (0.4 m) and a smaller one at the sleeve, riding the brush for the parry window
    /// (the attachment host's 0.5 s); they crack where they are when the window closes. Only a SUCCESSFUL parry (the combat wiring's verdict, relayed to <see cref="Vol2VfxModel.Parried"/>)
    /// splashes: a small 앞잡 across the impact, two pressed strokes thrown out of it, needles, flecks and the body's end accent. A touch the rules refused (ParryNoEffect) draws nothing.</summary>
    public sealed class Vol2StrokeAttachRecipe : Vol2StrokeRecipeBase
    {
        const int KPulse = 20, KSleeve = 21, KSplash = 22;
        public override Vol2Recipe Kind => Vol2Recipe.AttachOnce;
        public override string SwingState => "SwingPlant";

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
            AddSwing(inst);
        }

        public override Vector3 Offset(Vol2VfxInstance inst, long nowUs)
        {
            var pulse = inst.FirstOf(KPulse);
            var now = inst.Model.BrushTip?.Invoke();
            return pulse != null && now.HasValue ? now.Value - pulse.Pos : Vector3.zero;
        }

        public override bool OnParried(Vol2VfxInstance inst, Vector3 point, long nowUs)
        {
            foreach (var q in inst.Parts) if (q.Kind == KSplash && nowUs - q.AppearUs < Vol2Time.FromSeconds(0.2)) return false;     // one splash per impact
            Vector3 to = point - inst.Origin; to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? to.normalized : inst.Forward;
            // r5: the burst is centred where the BRUSH met the blow (the brush tip now, pulled 30 % toward the reported impact): from the shoulder camera the space straight in front of
            // the caster is behind the caster's own body
            var tipNow = inst.Model.BrushTip?.Invoke();
            Vector3 c = tipNow.HasValue ? Vector3.Lerp(tipNow.Value, point, 0.3f) : point - dir * 0.10f;
            var sp = inst.AddPart(KSplash, nowUs); sp.Pos = c; sp.Dir = dir; sp.F0 = 1f;
            inst.EndPart(sp, nowUs + Vol2Time.FromSeconds(MarkHold), Vol2EndCode.Expire, MarkDry);
            return true;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                switch (p.Kind)
                {
                    case KSwing: WriteSwing(inst, w, p); break;
                    case KPulse:
                    case KSleeve:
                    {
                        bool sleeve = p.Kind == KSleeve;
                        float len = sleeve ? 0.22f : 0.40f;
                        // the press at the tip (the sleeve's at the hand), running back along the shaft and starving
                        Vector3 start = p.Pos + p.Dir * 0.03f;
                        Line(start, start - p.Dir * len, Vector3.up, 0.01f, sleeve ? 0.055f : 0.075f, 0.022f, 1f, 0.45f, 6, 0.2f);
                        var a = Brush(inst, inst.Rel(p.AppearUs), 0.05f, len, Vol2StrokeLayout.Brush, Vector3.zero, sleeve ? 2.2f : 0f);
                        a.Role = Vol2StrokeRole.RigidOffset;
                        EndOf(inst, p, ref a); Emit(w, a);
                        if (!sleeve)
                        {
                            // r4: the loaded tip: a short pressed bead off the brush tip, out past the hand (the shaft itself lies on the dark sleeve)
                            Vector3 f = (p.Dir + Vector3.up * 0.25f).normalized;
                            Line(p.Pos, p.Pos + f * 0.28f, Vector3.up, 0.01f, 0.14f, 0.03f, 1f, 0.5f, 6, 0.3f);
                            var b = Brush(inst, inst.Rel(p.AppearUs), 0.03f, 0.2f, Vol2StrokeLayout.Brush, Vector3.zero, 3.3f);
                            b.Role = Vol2StrokeRole.RigidOffset;
                            EndOf(inst, p, ref b); Emit(w, b);
                        }
                        break;
                    }
                    case KSplash:
                    {
                        // r4: the parry is a BURST, not a slash (the reference "패링": strokes thrown out of the impact one after another, each its own direction): five 앞잡 strokes
                        // that leave the impact dry and land pressed (blunt, wet ends outward), in 0 / 17 / 33 / 50 / 67 ms, + needles + flecks + the body's end accent on the longest
                        float t0 = inst.Rel(p.AppearUs), s = p.F0;
                        Vector3 side = Vector3.Cross(Vector3.up, p.Dir).normalized;
                        Vector3 view = -p.Dir;                                                    // the burst faces the caster (the camera behind them)
                        float[] ang = { 60f, 15f, 110f, -25f, 165f };              // mostly up and out to the camera side (the left half is behind the caster)
                        float[] lens = { 0.85f, 0.75f, 0.62f, 0.55f, 0.45f };
                        Vector3 longest = p.Pos, longestDir = side;
                        for (int k = 0; k < 5; k++)
                        {
                            float a = (ang[k] + (Vol2Curves.Hash(p.Seed, 80 + k) - 0.5f) * 16f) * Mathf.Deg2Rad;
                            Vector3 d = (side * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a) * 0.85f + view * 0.15f).normalized;
                            float len = lens[k] * s;
                            Vector3 from = p.Pos + d * 0.05f, to = p.Pos + d * len;
                            P.Clear(); W.Clear(); K.Clear();
                            for (int i = 0; i <= 10; i++)
                            {
                                float q = i / 10f;
                                P.Add(Vector3.Lerp(from, to, q) + Vector3.Cross(d, view).normalized * (0.03f * s * Mathf.Sin(q * Mathf.PI)));
                                // dry at the impact, swelling to a blunt pressed end (the 앞잡 width law read backwards, so the wet ink lands outward)
                                W.Add(Mathf.Lerp(0.015f, 0.17f * s, Mathf.SmoothStep(0f, 1f, Mathf.Pow(q, 0.8f))) * (q > 0.92f ? Mathf.Lerp(1f, 0.75f, (q - 0.92f) / 0.08f) : 1f));
                                K.Add(Mathf.Lerp(0.35f, 1f, Mathf.Pow(q, 0.7f)));
                            }
                            var at = Brush(inst, t0 + 0.0167f * k, 0.035f, len, Vol2StrokeLayout.Brush, view, 7.3f + k);
                            EndOf(inst, p, ref at); Emit(w, at);
                            if (k == 0) { longest = to; longestDir = d; }
                        }
                        Needles(inst, w, p, p.Pos, (view + Vector3.up * 0.5f).normalized, t0, 0.5f * s, 3);
                        Flecks(inst, w, p, p.Pos, t0, 3, s);
                        EndAccent(inst, w, p, longest, longestDir, view, t0 + 0.06f, s);
                        break;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // F0 한 번 (고 노 모 소 오): the instant fan bundle
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅗ 한 번: on the ground, one by one in 0.1 s - both radial edges (0 ms), the far arc (+33 ms), two inner strokes (+67 / +100 ms); each a 앞잡 stroke pressed at its start and
    /// starving to a dry end (the far arc stands a little toward the camera so it is not a sliver). The fan is never a filled face. A small 앞잡 on each body the executor touched; the edge
    /// ends carry the body accent. Everything cracks 0.12 s after and is gone 0.15 s later (no residue).</summary>
    public sealed class Vol2StrokeFanRecipe : Vol2StrokeRecipeBase
    {
        const int KFan = 30;
        public override Vol2Recipe Kind => Vol2Recipe.FanOnce;
        public override string SwingState => "SwingArc";

        public override void OnBegan(Vol2VfxInstance inst)
        {
            long apex = inst.StartUs + Vol2Time.FromSeconds(SwingToApex);       // V8: the fan is laid when the brush comes down (visual; the executor judged at the cast)
            var p = inst.AddPart(KFan, apex);
            inst.EndPart(p, apex + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            AddIgnite(inst, apex, TipAtCast(inst));
            AddSwing(inst);
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (HasContact(inst, rec.TargetId)) return;
            Vector3 pos = TargetPos(inst, rec.TargetId, inst.Origin + inst.Forward * rec.Along, out float r);
            AddContact(inst, rec.NowUs, pos, r, 1.15f).Aux = rec.TargetId;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var spec = inst.Log.Spec;
            float depth = spec.FanDepth > 0.5f ? spec.FanDepth : 5f, half = spec.FanHalfAngleRad > 0.01f ? spec.FanHalfAngleRad * Mathf.Rad2Deg : 23.578f;
            Vector3 O = inst.Origin + Vector3.up * 0.03f;
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KIgnite) { WriteIgnite(inst, w, p); continue; }
                if (p.Kind == KContact) { WriteContact(inst, w, p, 3); continue; }
                if (p.Kind == KSwing) { WriteSwing(inst, w, p); continue; }
                if (p.Kind != KFan) continue;
                float t0 = inst.Rel(p.AppearUs);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 dir = Rotate(inst.Forward, sgn * half, Vector3.up);
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * -sgn;          // bow outward
                    // r5: written from the far end IN (pressed at the far chord, starving into dry streaks toward the apex): seen from behind the caster the near end is the big one
                    // on screen - pressed there it read as a strip of tape; dry there it reads as the brush running out
                    Vector3 a0 = O + dir * 0.45f, a1 = O + dir * depth;
                    Line(a1, a0, side, 0.08f, 0.30f, 0.05f, 1f, 0.3f, 14, 0.08f);
                    var a = Brush(inst, t0, 0.05f, depth, Vol2StrokeLayout.BrushFlat, Vector3.up, sgn * 0.9f);
                    EndOf(inst, p, ref a); Emit(w, a);
                    EndAccent(inst, w, p, a1 + Vector3.up * 0.04f, dir, Vector3.up, t0 + 0.06f, 0.9f);        // the body accent past the far chord (the apex end is under the camera)
                }
                {
                    // the far arc: on a plane tilted 35 degrees up toward the caster (a flat 0.34 m stroke 7.8 m from the shoulder camera is ~10 px tall), pressed at the left end
                    const float tilt = 35f;
                    P.Clear(); W.Clear(); K.Clear();
                    const int n = 16;
                    for (int i = 0; i <= n; i++)
                    {
                        float q = i / (float)n, ang = Mathf.Lerp(-half, half, q);
                        P.Add(O + Rotate(inst.Forward, ang, Vector3.up) * depth + Vector3.up * (0.34f * 0.5f * Mathf.Sin(tilt * Mathf.Deg2Rad) + 0.02f));
                        float swell = Mathf.SmoothStep(0f, 1f, q / 0.12f), ease = Mathf.SmoothStep(0f, 1f, (q - 0.12f) / 0.88f);
                        W.Add(Mathf.Lerp(Mathf.Lerp(0.19f, 0.34f, swell), 0.09f, Mathf.Pow(ease, 0.8f))); K.Add(Mathf.Lerp(1f, 0.45f, Mathf.Pow(q, 1.2f)));
                    }
                    Vector3 nrm = (Vector3.up * Mathf.Cos(tilt * Mathf.Deg2Rad) - inst.Forward * Mathf.Sin(tilt * Mathf.Deg2Rad)).normalized;
                    var a = Brush(inst, t0 + 0.0333f, 0.05f, Len(P), Vol2StrokeLayout.BrushFlat, nrm, 1.7f);
                    EndOf(inst, p, ref a); Emit(w, a);
                }
                for (int k = 0; k < 2; k++)
                {
                    float ang = (k == 0 ? -1f : 1f) * half * 0.36f;
                    Vector3 dir = Rotate(inst.Forward, ang, Vector3.up);
                    Line(O + dir * 1.0f, O + dir * 3.9f, Vector3.Cross(Vector3.up, dir), (k == 0 ? 1f : -1f) * 0.05f, 0.26f, 0.05f, 0.95f, 0.35f, 10, 0.12f);
                    var a = Brush(inst, t0 + 0.0667f + 0.0333f * k, 0.04f, 2.9f, Vol2StrokeLayout.BrushFlat, Vector3.up, 2.6f + k);
                    EndOf(inst, p, ref a); Emit(w, a);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    // R0 한 번 (구 누 무 수 우): the instant hollow band
    // ------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>ㅜ 한 번: six 52 degree 앞잡 strokes on the band (radius 2 m, inside the 0.6 m band, the centre empty), front first then the sides then the back (0 / 33 / 67 / 100 ms), each
    /// pressed at one end (alternating) and starving to a dry end, and a standing pair (1.1 m) at +/-20 degrees on the front of the band with the body accent at their dry tips; a small 앞잡
    /// on each body crossing it. All of it cracks together 0.12 s after (no residue).</summary>
    public sealed class Vol2StrokeRingRecipe : Vol2StrokeRecipeBase
    {
        const int KBand = 40;
        public override Vol2Recipe Kind => Vol2Recipe.RingOnce;
        public override string SwingState => "SwingSlam";

        public override void OnBegan(Vol2VfxInstance inst)
        {
            long apex = inst.StartUs + Vol2Time.FromSeconds(SwingToApex);       // V8: the band is laid when the brush comes down (visual)
            var p = inst.AddPart(KBand, apex);
            inst.EndPart(p, apex + Vol2Time.FromSeconds(DryStart), Vol2EndCode.Expire, DrySeconds);
            AddIgnite(inst, apex, TipAtCast(inst));
            AddSwing(inst);
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            if (HasContact(inst, rec.TargetId)) return;
            Vector3 pos = TargetPos(inst, rec.TargetId, inst.Origin + inst.Forward * rec.Along, out float r);
            AddContact(inst, rec.NowUs, pos, r, 1.15f).Aux = rec.TargetId;
        }

        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w)
        {
            var spec = inst.Log.Spec;
            float R = spec.RingRadius > 0.5f ? spec.RingRadius : 2f;
            Vector3 O = inst.Origin + Vector3.up * 0.03f;
            foreach (var p in inst.Parts)
            {
                if (p.Dead) continue;
                if (p.Kind == KIgnite) { WriteIgnite(inst, w, p); continue; }
                if (p.Kind == KContact) { WriteContact(inst, w, p, 3); continue; }
                if (p.Kind == KSwing) { WriteSwing(inst, w, p); continue; }
                if (p.Kind != KBand) continue;
                float t0 = inst.Rel(p.AppearUs);
                float[] order = { 0f, 0.0333f, 0.0333f, 0.0667f, 0.0667f, 0.1f };
                float[] angles = { 0f, -60f, 60f, -120f, 120f, 180f };
                const float span = 52f, tilt = 20f, wide = 0.44f;
                for (int k = 0; k < 6; k++)
                {
                    bool flip = (k & 1) == 1;
                    P.Clear(); W.Clear(); K.Clear();
                    const int n = 14;
                    for (int i = 0; i <= n; i++)
                    {
                        float q = i / (float)n, ang = angles[k] + (flip ? 1f : -1f) * span * (0.5f - q);
                        float rr = R + (Vol2Curves.Hash(p.Seed + k, i / 5) - 0.5f) * 0.06f;
                        P.Add(O + Rotate(inst.Forward, ang, Vector3.up) * rr + Vector3.up * (wide * 0.5f * Mathf.Sin(tilt * Mathf.Deg2Rad)));
                        float swell = Mathf.SmoothStep(0f, 1f, q / 0.12f), ease = Mathf.SmoothStep(0f, 1f, (q - 0.12f) / 0.88f);
                        W.Add(Mathf.Lerp(Mathf.Lerp(wide * 0.55f, wide, swell), 0.11f, Mathf.Pow(ease, 0.8f))); K.Add(Mathf.Lerp(1f, 0.45f, Mathf.Pow(q, 1.2f)));
                    }
                    Vector3 radial = Rotate(inst.Forward, angles[k], Vector3.up);
                    Vector3 nrm = (Vector3.up * Mathf.Cos(tilt * Mathf.Deg2Rad) - radial * Mathf.Sin(tilt * Mathf.Deg2Rad)).normalized;     // leans toward the centre: faces the camera on the front of the band
                    var a = Brush(inst, t0 + order[k], 0.045f, Len(P), Vol2StrokeLayout.BrushFlat, nrm, k * 0.77f);
                    EndOf(inst, p, ref a); Emit(w, a);
                }
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 at = O + Rotate(inst.Forward, sgn * 20f, Vector3.up) * R;
                    Vector3 lean = inst.Right * (sgn * 0.08f);
                    Line(at, at + Vector3.up * 1.1f + lean, inst.Right, sgn * 0.03f, 0.17f, 0.035f, 1f, 0.35f, 10, 0.08f);
                    var a = Brush(inst, t0 + 0.05f, 0.05f, 1.1f, Vol2StrokeLayout.Brush, Vector3.zero, 6f + sgn);
                    EndOf(inst, p, ref a); Emit(w, a);
                    EndAccent(inst, w, p, at + Vector3.up * 1.1f + lean, (Vector3.up + lean).normalized, inst.Forward, t0 + 0.10f, 0.8f);
                }
            }
        }
    }
}
