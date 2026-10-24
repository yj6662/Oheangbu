using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 9a (D308-150 V5): the DATA of the vol.2 spell look. Plan canon: Docs/Research/vfx120/plan_v10_final.md (B beats / end reasons, C body and ink,
    // E ownership, G height, H slots) and s5b_design_v1.md section 11: ONE template per Frame x Verb (24), ONE look per body (5), the peaks (20) and the
    // reactions (25) come later as more assets of the same kind. An instance on screen is (template x body x [peak]) - there is no prefab per glyph.
    // Everything below is a number or a choice; the code that draws reads it (Vol2VfxRecipes / the shader) and invents nothing.

    /// <summary>Plan B "종료 이유": 만 natural expiry | 소 consumed (a seed worked / a normal use) | 자 a child consumed it | 교 replaced by another bundle | 피 cancelled by a hit | 정 cleansed.
    /// The numbers are the shader's endMode: 1..6.</summary>
    public enum Vol2EndCode { None = 0, Expire = 1, Consume = 2, Child = 3, Replace = 4, HitCancel = 5, Cleanse = 6 }

    /// <summary>Plan C, "몸은 큰 가장자리로 읽는다": the first-glance cue of each body, an edge or an end of the stroke. Colour only supports it.</summary>
    public enum Vol2EdgeKind { FibreTear = 0, SawTooth = 1, SteppedPlate = 2, StraightCut = 3, OpenHook = 4 }

    /// <summary>Plan C "자연 종료": what a body does when it ends by expiry - wood fibres loosen forward, fire turns to ash that rises, earth sinks, metal snaps and parts sideways, water runs down.</summary>
    public enum Vol2EndBehaviour { UnravelForward = 0, AshRise = 1, SinkLift = 2, SnapSideways = 3, RunDown = 4 }

    /// <summary>Which drawing routine a template uses (slice 9a: the six ㅏ templates; A / F / R and the peaks are later slices).</summary>
    public enum Vol2Recipe { None = 0, PathOneHit = 1, PathLeave = 2, PathSpread = 3, PathSeed = 4, PathSplit = 5, PathReturn = 6 }

    public static class Vol2EndCodes
    {
        public static Vol2EndCode From(Vol2MaterialEnd why)
        {
            switch (why)
            {
                case Vol2MaterialEnd.Expired: return Vol2EndCode.Expire;
                case Vol2MaterialEnd.Consumed: return Vol2EndCode.Consume;
                case Vol2MaterialEnd.ChildConsumed: return Vol2EndCode.Child;
                case Vol2MaterialEnd.Replaced: return Vol2EndCode.Replace;
                case Vol2MaterialEnd.HitCancelled: return Vol2EndCode.HitCancel;
                case Vol2MaterialEnd.Cleansed: return Vol2EndCode.Cleanse;
                default: return Vol2EndCode.None;       // Aborted (lab reset) is never a gameplay end: the view clears at once
            }
        }
        public static string Name(Vol2EndCode c)
        {
            switch (c)
            {
                case Vol2EndCode.Expire: return "만";
                case Vol2EndCode.Consume: return "소";
                case Vol2EndCode.Child: return "자";
                case Vol2EndCode.Replace: return "교";
                case Vol2EndCode.HitCancel: return "피";
                case Vol2EndCode.Cleanse: return "정";
                default: return "-";
            }
        }
    }

    /// <summary>The default numbers - the single place the builder and the in-memory fallback take them from. Colours are the plan C table:
    /// base (wet) #368B75 / #BE543B / #B68A3D / #A6B0AD / water rim #4F6A84; dry pigment L* 60 #4B9F88 / #DF7155 / #B5893C / #899390 / #7893AF; reading ink Lab(18,0,0).</summary>
    public static class Vol2VfxDefaults
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); c.a = 1f; return c; }

        public static Vol2BodyLook NewLook(Body body)
        {
            var l = ScriptableObject.CreateInstance<Vol2BodyLook>();
            l.name = "BodyLook_" + body;
            l.body = body;
            switch (body)
            {
                case Body.Wood:
                    l.wetColor = Hex("#368B75"); l.dryPigment = Hex("#4B9F88"); l.edge = Vol2EdgeKind.FibreTear; l.end = Vol2EndBehaviour.UnravelForward;
                    l.endDirection = new Vector3(0f, 0f, 1f); l.endTravel = 0.55f; l.fray = 0.75f; l.rimGap = 0.20f; l.note = "torn fibre ends | roots loosen forward"; break;
                case Body.Fire:
                    l.wetColor = Hex("#BE543B"); l.dryPigment = Hex("#DF7155"); l.edge = Vol2EdgeKind.SawTooth; l.end = Vol2EndBehaviour.AshRise;
                    l.endDirection = new Vector3(0f, 1f, 0f); l.endTravel = 0.7f; l.fray = 0.55f; l.rimGap = 0.24f; l.note = "burnt voids, saw-tooth ends | ash rises"; break;
                case Body.Earth:
                    l.wetColor = Hex("#B68A3D"); l.dryPigment = Hex("#B5893C"); l.edge = Vol2EdgeKind.SteppedPlate; l.end = Vol2EndBehaviour.SinkLift;
                    l.endDirection = new Vector3(0f, -1f, 0f); l.endTravel = 0.35f; l.fray = 0.25f; l.rimGap = 0.14f; l.note = "stacked low plates | sinks, the ground lifts"; break;
                case Body.Metal:
                    l.wetColor = Hex("#A6B0AD"); l.dryPigment = Hex("#899390"); l.edge = Vol2EdgeKind.StraightCut; l.end = Vol2EndBehaviour.SnapSideways;
                    l.endDirection = new Vector3(1f, 0f, 0f); l.endTravel = 0.5f; l.fray = 0.12f; l.rimGap = 0.10f; l.note = "straight cut ends, short cross breaks | snaps sideways"; break;
                default:
                    l.wetColor = Hex("#4F6A84"); l.dryPigment = Hex("#7893AF"); l.edge = Vol2EdgeKind.OpenHook; l.end = Vol2EndBehaviour.RunDown;
                    l.endDirection = new Vector3(0f, -1f, 0f); l.endTravel = 0.6f; l.fray = 0.35f; l.rimGap = 0.16f; l.note = "open hooks, hollow parallel streams | runs down"; break;
            }
            return l;
        }

        public static Vol2VfxTemplate NewTemplate(Vol2TemplateId id)
        {
            var t = ScriptableObject.CreateInstance<Vol2VfxTemplate>();
            t.name = "Template_" + id.Name;
            t.id = id.Name; t.frame = id.Frame; t.verb = id.Verb;
            t.recipe = id.Frame == Frame.Path ? (Vol2Recipe)((int)id.Verb + 1) : Vol2Recipe.None;
            t.filled = t.recipe != Vol2Recipe.None;
            string[] verbs = { "once", "leave", "spread", "seed", "split", "return" };
            t.summary = (t.filled ? "" : "(later slice) ") + id.Name + " " + id.Frame + " x " + verbs[(int)id.Verb];
            if (!t.filled) return t;
            switch (id.Verb)
            {
                case Verb.Once:   // P0: one narrow flick, contact accent, dries 0.12 - 0.18 s after the contact
                    t.summary = "P0: ground stroke to the first body, a vertical landing stroke and 3 - 5 flecks (spatter), dry 0.15 s after the contact"; break;
                case Verb.Leave:  // P1: the path lands, branches rise from the landing point
                    t.summary = "P1: ground stroke, then a trunk that forks upward at the landing (10 s residue); height by distance (G)"; t.height = 0.9f; break;
                case Verb.Spread: // P2: flat bundle at the landing, flat strokes to each approved neighbour (3 s marks)
                    t.summary = "P2: ground stroke, a flat bundle at the landing, a flat connector and bundle to each approved neighbour (3 s marks)"; break;
                case Verb.Seed:   // P3: vertical dot on the target (8 s), pressed once when it works
                    t.summary = "P3: ground stroke, a vertical dot standing on the target (waits 8 s), pressed to its base when it works"; break;
                case Verb.Split:  // P4: engraved cut with dry lips on the ground, a cut-face pair on the armour contact
                    t.summary = "P4: engraved cut with two dry lips on the ground (<= 0.3 s), a pair of cut faces at the armour contact (strip 3 s)"; t.hollow = 0f; break;
                default:          // P5: thin ground stroke out and back, a small closed loop riding its head
                    t.summary = "P5: thin stroke out and back at chest height, a small closed loop (diameter < 1 m) riding the head, dry 0.15 s after the return"; t.tailMeters = 12f; t.lineWidth = 0.12f; break;
            }
            return t;
        }

        public static Vol2VfxCatalogue CreateCatalogue(Material ink)
        {
            var c = ScriptableObject.CreateInstance<Vol2VfxCatalogue>();
            c.name = "Vol2VfxCatalogue(default)";
            var ts = new System.Collections.Generic.List<Vol2VfxTemplate>();
            foreach (var id in Vol2TemplateId.All()) ts.Add(NewTemplate(id));
            c.templates = ts.ToArray();
            var bs = new System.Collections.Generic.List<Vol2BodyLook>();
            foreach (Body b in Enum.GetValues(typeof(Body))) bs.Add(NewLook(b));
            c.bodies = bs.ToArray();
            c.inkMaterial = ink;
            return c;
        }
    }
}
