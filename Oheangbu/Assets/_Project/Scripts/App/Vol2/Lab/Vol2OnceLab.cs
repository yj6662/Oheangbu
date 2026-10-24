using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 3 lab scenarios for the 20 무받침 (한 번) glyphs: where the dummies stand for each frame and what the cast must do to
    /// them. The EXPECTED sets are written here by hand from the frame geometry (not read back from a run, design section 12); the
    /// coordinates are chosen clearly inside / outside the numbers of NumbersV3 (path 0.3 x 12 m, fan 4 x 5 m, ring r 2 m / 0.6 m).
    /// Dummies are runtime objects (an EnemyVitals and a capsule), registered as extras of the scope's target registry; the lab scene is not touched.</summary>
    public sealed class Vol2LabDummy
    {
        public GameObject Go;
        public EnemyVitals Vitals;
        public string Name;
        public bool Expected;
        public Vector3 Start;
        public float HpBefore;
    }

    public sealed class Vol2OnceScenario
    {
        public string Variant = "";
        public Vector3 Origin, Forward;
        public readonly List<Vol2LabDummy> Dummies = new List<Vol2LabDummy>();
        public GameObject Wall;
        public bool NeedsSwing;
        public string Describe = "";
    }

    /// <summary>The verdict of one scenario against the cast log.</summary>
    public sealed class Vol2OnceVerdict
    {
        public bool Pass;
        public string Note = "";
        public string Hit = "", Expected = "", Unexpected = "", BodyObserved = "";
        public decimal Units; public float Hp;
    }

    public static class Vol2OnceLab
    {
        public const string Scenario = "S3-once";
        public const int DummyLayer = 0;

        /// <summary>Variants of a glyph: every frame has its main one; the path adds the wall.</summary>
        public static string[] Variants(Frame frame)
            => frame == Frame.Path ? new[] { "line", "wall" } : frame == Frame.Fan ? new[] { "fan" } : frame == Frame.Ring ? new[] { "ring" } : new[] { "attach" };

        public static Vol2OnceScenario Build(Frame frame, string variant, IVol2Caster caster, Vol2TargetRegistry registry)
        {
            var sc = new Vol2OnceScenario { Variant = variant, Origin = caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(caster.AimDirection) };
            Vector3 f = sc.Forward, right = Vector3.Cross(Vector3.up, f);
            Vector3 At(float ahead, float side) => sc.Origin + f * ahead + right * side;
            void Dummy(string name, float ahead, float side, bool expected)
            {
                var go = new GameObject("Vol2LabDummy_" + name);
                go.transform.position = At(ahead, side);
                var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.4f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f);
                var vitals = go.AddComponent<EnemyVitals>();      // Awake runs now: full default hp
                registry.AddExtra(vitals);
                sc.Dummies.Add(new Vol2LabDummy { Go = go, Vitals = vitals, Name = name, Expected = expected, Start = go.transform.position, HpBefore = vitals.Hp });
            }
            switch (variant)
            {
                case "line":
                    Dummy("A4m", 4f, 0f, true); Dummy("B7m", 7f, 0f, false); Dummy("C10m", 10f, 0f, false); Dummy("D6m_side0.9", 6f, 0.9f, false);
                    sc.Describe = "three in line at 4/7/10 m + one 0.9 m off the axis: only the 4 m one is touched (first body, path 0.3 m wide)"; break;
                case "wall":
                    sc.Wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sc.Wall.name = "Vol2LabWall"; sc.Wall.transform.position = At(5f, 0f) + Vector3.up * 1.5f; sc.Wall.transform.rotation = Quaternion.LookRotation(f);
                    sc.Wall.transform.localScale = new Vector3(6f, 3f, 0.4f);
                    Dummy("W8m_behind", 8f, 0f, false); Dummy("X3m_side1.6", 3f, 1.6f, false);
                    sc.Describe = "a wall 5 m ahead, one body behind it, one off the axis: nothing is touched (the path stops at the wall)"; break;
                case "fan":
                    Dummy("In3m", 3f, 0f, true); Dummy("In4m_side1", 4f, 1f, true); Dummy("In4.8m_side-0.3", 4.8f, -0.3f, true);
                    Dummy("Out5.8m", 5.8f, 0f, false); Dummy("Out3m_side2.6", 3f, 2.6f, false); Dummy("Out-2m_behind", -2f, 0f, false);
                    sc.Describe = "fan 5 m deep, 4 m across the far chord: three inside, one too far, one too wide, one behind"; break;
                case "ring":
                    Dummy("Band2.0m", 2f, 0f, true); Dummy("Band-2.1m_side", 0f, -2.1f, true);
                    Dummy("Hole0.5m", 0.5f, 0.3f, false); Dummy("Hole1.0m", 1f, 0f, false); Dummy("Out3.4m", 3.4f, 0f, false);
                    sc.Describe = "ring r 2 m, band 0.6 m: two bodies on the band; the hollow middle and the outside are not touched"; break;
                default:    // attach
                    Dummy("Near1.0m", 1f, 0f, true); Dummy("Far3.0m", 3f, 0f, false);
                    sc.NeedsSwing = true;
                    sc.Describe = "ㅓ 한 번 = parry: the attachment window opens for 0.5 s, no effect on bodies. A lab-simulated swing reported by 3 colliders: one body in reach is contacted ONCE, no damage"; break;
            }
            Physics.SyncTransforms();
            return sc;
        }

        public static void Cleanup(Vol2OnceScenario sc, Vol2TargetRegistry registry)
        {
            if (sc == null) return;
            foreach (var d in sc.Dummies) { registry.RemoveExtra(d.Vitals); if (d.Go != null) UnityEngine.Object.Destroy(d.Go); }
            if (sc.Wall != null) UnityEngine.Object.Destroy(sc.Wall);
            sc.Dummies.Clear();
        }

        static string ActionWord(Body b) => b == Body.Wood ? "bind" : b == Body.Fire ? "burn" : b == Body.Earth ? "push" : b == Body.Metal ? "cut" : "wet";

        /// <summary>Compares what the cast did with what the frame must do. Independent of the code under test: the action words come from the
        /// 한줄정의 of the 20 rows (붙든다 / 데운다 / 민다 / 벤다 / 적신다), the sets from the coordinates above.</summary>
        public static Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2OnceScenario sc, Vol2CastLog log, Vol2CastCoordinator coordinator, NumbersV3Dto numbers)
        {
            var v = new Vol2OnceVerdict();
            var problems = new List<string>();
            var inv = CultureInfo.InvariantCulture;
            var hit = log.Contacts.Select(c => c.TargetName.Replace("Vol2LabDummy_", "")).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var expected = sc.Dummies.Where(d => d.Expected).Select(d => d.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var unexpected = hit.Where(h => !expected.Contains(h)).ToList();
            v.Hit = string.Join("|", hit); v.Expected = string.Join("|", expected); v.Unexpected = string.Join("|", unexpected);
            if (log.Status != "Done") problems.Add("cast status " + log.Status);
            if (!hit.SequenceEqual(expected)) problems.Add("touched [" + v.Hit + "] expected [" + v.Expected + "]");
            if (log.Contacts.Count != hit.Count) problems.Add("a body touched more than once: " + log.Contacts.Count + " contacts for " + hit.Count + " bodies");
            v.Units = log.TotalUnits; v.Hp = log.TotalHp;
            var body = row.Body; string word = ActionWord(body);

            if (row.Glyph.IsParry)
            {
                long window = RuleTypesV3.Microseconds(numbers.Get("combat.parry.window"));
                if (!log.ParryWindowOpened || log.WindowEndUs - log.WindowStartUs != window) problems.Add("parry window " + (log.WindowEndUs - log.WindowStartUs) + " us, expected " + window);
                if (log.SwingReports != 3 || log.SwingDuplicates != 2) problems.Add("swing reports " + log.SwingReports + " duplicates " + log.SwingDuplicates + ", expected 3 and 2");
                if (log.Contacts.Any(c => c.DamageRequested || c.BodyEffect.Length > 0 || c.BudgetAccepted)) problems.Add("a parry glyph produced an effect");
                if (log.InkRequested != 0m || log.InkPaid != 0m) problems.Add("parry ink must be 0 here (post-payment is slice 2)");
            }
            else if (sc.Variant == "wall")
            {
                if (log.Contacts.Count != 0) problems.Add("the path passed the wall");
                var e = log.Emitter as Vol2PathEmitter;
                if (e == null || e.EndReason != "wall") problems.Add("path end reason " + (e != null ? e.EndReason : "n/a") + ", expected wall");
            }
            else
            {
                foreach (var c in log.Contacts)
                {
                    if (!c.BudgetAccepted) problems.Add(c.TargetName + " budget " + c.BudgetReason);
                    if (!c.DamageApplied) problems.Add(c.TargetName + " damage not applied (" + c.DamageNote + ")");
                    if (!c.BodyEffect.StartsWith(word, StringComparison.Ordinal)) problems.Add(c.TargetName + " body effect '" + c.BodyEffect + "', expected " + word);
                }
                if (sc.Variant == "line" && log.PaidContacts != 1) problems.Add("paid contacts " + log.PaidContacts + ", expected 1 (first body only)");
                // what the game objects show (observed, not just recorded)
                var observed = new List<string>();
                foreach (var d in sc.Dummies)
                {
                    float lost = d.HpBefore - d.Vitals.Hp;
                    if (!d.Expected)
                    {
                        if (lost > 1e-4f) problems.Add(d.Name + " lost " + lost.ToString("0.##", inv) + " hp but was not to be touched");
                        continue;
                    }
                    if (lost <= 0f) problems.Add(d.Name + " lost no hp");
                    switch (body)
                    {
                        case Body.Wood:
                            { float scale = d.Vitals.Control.MovementScale(Time.time); observed.Add(d.Name + " move x" + scale.ToString("0.##", inv)); if (Math.Abs(scale - (float)numbers.Get("body.wood.moveMultiplier")) > 1e-3f) problems.Add(d.Name + " movement scale " + scale); break; }
                        case Body.Fire:
                            {
                                int ticks = log.Ticks.Count(t => t.TargetId == coordinator.Registry.IdOf(d.Vitals) && t.Applied);
                                observed.Add(d.Name + " burn ticks " + ticks);
                                decimal total = numbers.Get("body.fire.utility"); long dur = RuleTypesV3.Microseconds(numbers.Get("body.fire.duration"));
                                long tick = coordinator.Tuning.DotTickUs; int want = (int)Math.Max(1, dur / tick);
                                if (ticks != want) problems.Add(d.Name + " burn ticks " + ticks + ", expected " + want);
                                decimal burned = log.Ticks.Where(t => t.TargetId == coordinator.Registry.IdOf(d.Vitals) && t.Applied).Sum(t => t.Units);
                                if (Math.Abs((double)(burned - total)) > 1e-9) problems.Add(d.Name + " burn total " + burned + ", expected " + total);
                                break;
                            }
                        case Body.Earth:
                            { float moved = Vector3.Distance(d.Go.transform.position, d.Start); observed.Add(d.Name + " pushed " + moved.ToString("0.##", inv) + "m"); if (moved < coordinator.Tuning.PushMeters * 0.9f) problems.Add(d.Name + " pushed only " + moved); break; }
                        case Body.Water:
                            { var tgt = coordinator.Registry.Find(coordinator.Registry.IdOf(d.Vitals)); bool wet = tgt != null && coordinator.Wet.IsWet(tgt, log.StartUs); observed.Add(d.Name + (wet ? " wet" : " dry")); if (!wet) problems.Add(d.Name + " not wet"); break; }
                        default:
                            observed.Add(d.Name + " cut"); break;
                    }
                }
                v.BodyObserved = string.Join("; ", observed);
            }
            v.Pass = problems.Count == 0; v.Note = string.Join("; ", problems);
            return v;
        }
    }
}
