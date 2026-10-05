using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-18 (3): the case table of FallReferenceRule308 (pure: no UnityEngine, so the same file runs in the editor check
    // `FallRule308Checks Run "table"` and in the offline console test Tools/Unity/Stage308_world18/fall/Proof/RuleTest).
    // The numbers below are test inputs, not game data: the walker limit 45, the gap .12 and the height 6 mirror the TEST assets only
    // so that the rows read like the game; the rule itself takes them as arguments.
    public static class FallRule308Table
    {
        public sealed class Case
        {
            public string Id, What; public bool WalkableOnly = true, Grounded, Seated, Found; public float SlopeDeg, Gap, MaxGap = .12f, FieldDeg, ControllerDeg = 45f; public bool Expect;
        }
        public sealed class Walk { public string Id, What; public bool WalkableOnly = true; public float Fatal = 6f; public float[] FeetY; public float[] SlopeDeg; public bool[] Grounded; public int ExpectDeathAt; }

        static float Ny(float deg) => (float)Math.Cos(deg * Math.PI / 180.0);

        public static List<Case> Cases()
        {
            return new List<Case>
            {
                new Case { Id = "R01", What = "flat ground", Grounded = true, Found = true, SlopeDeg = 0, Gap = .01f, Expect = true },
                new Case { Id = "R02", What = "legal steep path 44 deg", Grounded = true, Found = true, SlopeDeg = 44, Gap = .02f, Expect = true },
                new Case { Id = "R03", What = "exactly the walker limit 45 deg", Grounded = true, Found = true, SlopeDeg = 45, Gap = .02f, Expect = true },
                new Case { Id = "R04", What = "cliff face 85 deg, contact below (the defect of the coded rule)", Grounded = true, Found = false, SlopeDeg = 85, Gap = .0f, Expect = false },
                new Case { Id = "R05", What = "46 deg face handed in as support by a caller that did not filter", Grounded = true, Found = true, SlopeDeg = 46, Gap = .0f, Expect = false },
                new Case { Id = "R06", What = "jump apex / falling (never in the air)", Grounded = false, Found = true, SlopeDeg = 0, Gap = .05f, Expect = false },
                new Case { Id = "R07", What = "air, nothing under the feet", Grounded = false, Found = false, Expect = false },
                new Case { Id = "R08", What = "seated in the vehicle on flat ground (the vehicle keeps its own reference)", Grounded = true, Seated = true, Found = true, SlopeDeg = 0, Gap = .01f, Expect = false },
                new Case { Id = "R09", What = "grounded flag but the walkable support is farther than the gap (hovering over a step edge)", Grounded = true, Found = true, SlopeDeg = 0, Gap = .2f, Expect = false },
                new Case { Id = "R10", What = "stair tread / step offset: flat tread within the gap", Grounded = true, Found = true, SlopeDeg = 0, Gap = .1f, Expect = true },
                new Case { Id = "R11", What = "short steep bump on a path: the walkable ground beside it is the nearest walkable support", Grounded = true, Found = true, SlopeDeg = 12, Gap = .04f, Expect = true },
                new Case { Id = "R12", What = "guk lift deck (built collider, flat)", Grounded = true, Found = true, SlopeDeg = 0, Gap = .0f, Expect = true },
                new Case { Id = "R13", What = "bridge / built floor 8 deg", Grounded = true, Found = true, SlopeDeg = 8, Gap = .01f, Expect = true },
                new Case { Id = "R14", What = "mine ramp 30 deg (indoor mesh)", Grounded = true, Found = true, SlopeDeg = 30, Gap = .02f, Expect = true },
                new Case { Id = "R15", What = "data field 30 deg: a 40 deg slope is not walkable for the rule", Grounded = true, Found = true, SlopeDeg = 40, Gap = .02f, FieldDeg = 30, Expect = false },
                new Case { Id = "R16", What = "data field 30 deg: a 25 deg slope is", Grounded = true, Found = true, SlopeDeg = 25, Gap = .02f, FieldDeg = 30, Expect = true },
                new Case { Id = "R17", What = "data field 60 deg is cut to the walker limit 45: 50 deg stays unwalkable", Grounded = true, Found = true, SlopeDeg = 50, Gap = .02f, FieldDeg = 60, Expect = false },
                new Case { Id = "R18", What = "switch OFF (rule as coded): steep contact refreshes", WalkableOnly = false, Grounded = true, Found = false, SlopeDeg = 85, Expect = true },
                new Case { Id = "R19", What = "switch OFF: still never in the air", WalkableOnly = false, Grounded = false, Found = false, Expect = false },
                new Case { Id = "R20", What = "switch OFF: still never while seated", WalkableOnly = false, Grounded = true, Seated = true, Found = true, Expect = false },
                new Case { Id = "R21", What = "NaN gap", Grounded = true, Found = true, SlopeDeg = 0, Gap = float.NaN, Expect = false },
                new Case { Id = "R22", What = "wading on a walkable river bed", Grounded = true, Found = true, SlopeDeg = 10, Gap = .01f, Expect = true },
            };
        }

        // Height traces: feet y per tick with the support slope under the feet (a slope above the limit = contact with a face).
        public static List<Walk> Walks()
        {
            return new List<Walk>
            {
                new Walk { Id = "W01", What = "slide down a cliff face 12 m, contact all the way: death at 6 m (new rule)",
                    FeetY = new float[] { 100, 99, 97, 95, 94.1f, 94, 92, 88 }, SlopeDeg = new float[] { 0, 85, 85, 85, 85, 85, 85, 85 }, Grounded = new[] { true, true, true, true, true, true, true, true }, ExpectDeathAt = 5 },
                new Walk { Id = "W02", What = "the same slide with the switch OFF (rule as coded): no death", WalkableOnly = false,
                    FeetY = new float[] { 100, 99, 97, 95, 94.1f, 94, 92, 88 }, SlopeDeg = new float[] { 0, 85, 85, 85, 85, 85, 85, 85 }, Grounded = new[] { true, true, true, true, true, true, true, true }, ExpectDeathAt = -1 },
                new Walk { Id = "W03", What = "walk down a legal 40 deg path 28 m: no death",
                    FeetY = new float[] { 100, 96, 92, 88, 84, 80, 76, 72 }, SlopeDeg = new float[] { 40, 40, 40, 40, 40, 40, 40, 40 }, Grounded = new[] { true, true, true, true, true, true, true, true }, ExpectDeathAt = -1 },
                new Walk { Id = "W04", What = "4.06 m ledge chain x4 (the UP-1 descent): every landing is walkable, no death",
                    FeetY = new float[] { 280.4f, 276.3f, 276.3f, 272.3f, 272.3f, 268.2f, 268.2f, 264.1f }, SlopeDeg = new float[] { 0, 0, 0, 0, 0, 0, 0, 0 }, Grounded = new[] { true, true, true, true, true, true, true, true }, ExpectDeathAt = -1 },
                new Walk { Id = "W05", What = "land on a steep face 5 m down, keep contact 1.2 m more: death (cumulative on unwalkable ground)",
                    FeetY = new float[] { 100, 97, 95, 94.5f, 93.8f }, SlopeDeg = new float[] { 0, 0, 60, 60, 60 }, Grounded = new[] { true, false, true, true, true }, ExpectDeathAt = 4 },
                new Walk { Id = "W06", What = "land on a steep face 5 m down, step onto walkable ground 0.5 m lower, then drop 5 m more: no death",
                    FeetY = new float[] { 100, 95, 94.5f, 94.5f, 89.6f }, SlopeDeg = new float[] { 0, 60, 20, 20, 0 }, Grounded = new[] { true, true, true, true, true }, ExpectDeathAt = -1 },
                new Walk { Id = "W07", What = "jump 0.75 m up from flat ground and land: the apex never raises the reference",
                    FeetY = new float[] { 100, 100.75f, 100, 94.2f }, SlopeDeg = new float[] { 0, 0, 0, 0 }, Grounded = new[] { true, false, true, false }, ExpectDeathAt = -1 },
                new Walk { Id = "W08", What = "the same jump off a cliff edge: the 6.1 m fall is counted from the ground, not from the apex",
                    FeetY = new float[] { 100, 100.75f, 96, 93.9f }, SlopeDeg = new float[] { 0, 0, 0, 0 }, Grounded = new[] { true, false, false, false }, ExpectDeathAt = 3 },
                new Walk { Id = "W09", What = "guk lift up 16.7 m (deck walkable), step onto the shelf, ride the deck back down: no death",
                    FeetY = new float[] { 259.6f, 265, 270, 275, 276.3f, 276.3f, 272, 267, 262, 259.6f }, SlopeDeg = new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Grounded = new[] { true, true, true, true, true, true, true, true, true, true }, ExpectDeathAt = -1 },
                new Walk { Id = "W10", What = "short steep bump on a path (1.5 m of 50 deg) between walkable ground: no death",
                    FeetY = new float[] { 100, 99.5f, 98.5f, 98, 97 }, SlopeDeg = new float[] { 10, 50, 50, 10, 10 }, Grounded = new[] { true, true, true, true, true }, ExpectDeathAt = -1 },
            };
        }

        public static bool Eval(Case k) => FallReferenceRule308.Refreshes(k.WalkableOnly, k.Grounded, k.Seated, k.Found, Ny(k.SlopeDeg), k.Gap, k.MaxGap, k.FieldDeg, k.ControllerDeg);

        /// <summary>The tick of death (-1 = none). found = the slope passes the walker limit (what the session probe hands in).</summary>
        public static int Eval(Walk w, float controllerDeg = 45f, float maxGap = .12f)
        {
            bool known = true; float reference = w.FeetY[0];
            for (int i = 0; i < w.FeetY.Length; i++)
            {
                if (FallReferenceRule308.Fatal(known, reference, w.FeetY[i], w.Fatal)) return i;
                bool found = w.SlopeDeg[i] <= controllerDeg;
                if (FallReferenceRule308.Refreshes(w.WalkableOnly, w.Grounded[i], false, found, Ny(w.SlopeDeg[i]), 0f, maxGap, 0f, controllerDeg)) { reference = w.FeetY[i]; known = true; }
            }
            return -1;
        }

        /// <summary>One line per row, then the verdict line "GREEN: PASS n, FAIL 0" / "RED: PASS n, FAIL m".</summary>
        public static string Report(out int fail)
        {
            var sb = new System.Text.StringBuilder(); int pass = 0; fail = 0;
            foreach (var k in Cases())
            {
                bool got = Eval(k); bool ok = got == k.Expect; if (ok) pass++; else fail++;
                sb.Append(ok ? "PASS " : "FAIL ").Append(k.Id).Append(" refresh=").Append(got ? "yes" : "no").Append(" (expected ").Append(k.Expect ? "yes" : "no").Append(") ").Append(k.What).Append('\n');
            }
            foreach (var w in Walks())
            {
                int got = Eval(w); bool ok = got == w.ExpectDeathAt; if (ok) pass++; else fail++;
                sb.Append(ok ? "PASS " : "FAIL ").Append(w.Id).Append(" death tick=").Append(got.ToString(CultureInfo.InvariantCulture)).Append(" (expected ").Append(w.ExpectDeathAt.ToString(CultureInfo.InvariantCulture)).Append(") ").Append(w.What).Append('\n');
            }
            float e0 = FallReferenceRule308.EffectiveSlopeDeg(0f, 45f), e1 = FallReferenceRule308.EffectiveSlopeDeg(30f, 45f), e2 = FallReferenceRule308.EffectiveSlopeDeg(60f, 45f), e3 = FallReferenceRule308.EffectiveSlopeDeg(float.NaN, 45f);
            bool eff = e0 == 45f && e1 == 30f && e2 == 45f && e3 == 45f; if (eff) pass++; else fail++;
            sb.Append(eff ? "PASS " : "FAIL ").Append("E01 effective limit: field 0 -> 45, 30 -> 30, 60 -> 45, NaN -> 45\n");
            sb.Append(fail == 0 ? "GREEN" : "RED").Append(": PASS ").Append(pass).Append(", FAIL ").Append(fail);
            return sb.ToString();
        }
    }
}
