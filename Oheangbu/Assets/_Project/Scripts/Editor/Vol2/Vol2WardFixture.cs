using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-156 (SPEC-VOL2-WARD-308) edit-mode fixture of the ㅜ ward (구 누 무 수 우). No scene, no Play, a hand-driven clock. Queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2WardFixture Run "all"
    ///   W0  the ward life: "ward.life" from the numbers when the data has it, else TE-W1 (2.0 s); the cast log notes the source
    ///   W1  bodies: the band body at the commit, a walker crossing the band later, each ONCE; the hollow / outside never; after the life nothing; running until the life, then Done
    ///   W2  shots: a shot coming in is taken on the band (point at the band's outer face); one that arrives after the life passes; one fired from the hollow is not taken; each taken once
    ///   W3  melee (coordinator): blocked while the caster stands within space.ring.radius of the centre, not outside it, not after the life; recorded on the cast log and announced
    ///   W4  melee (the real seam): PlayerVitals + Vol2PlayerMeleeWardPort + EnemyStrike308.Deliver: a melee hit inside the ward takes no HP and raises no hit; a ranged hit is not touched
    ///       by the block; after the life the melee hit lands; the port binds once and is released with the coordinator
    ///   W5  the other glyphs keep their behaviour: 국 (ㅜ + ㄱ) is no ward, 거 still opens the parry window, 궁 (ㅜ + ㅇ) still returns; the five ward glyphs all stand
    ///   W6  a reset (AbortAll) takes the ward down at once: no block, no shot after it
    /// Expected values are written from the Spec (radius 2 m, band 0.6 m, life 2 s TEST), never read back from a run.</summary>
    public static class Vol2WardFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-ward-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-ward-fixture: editor busy (play / compile / import)";
            var r = new Report();
            var made = new List<Object>();
            try { using (var errors = new ErrorCounter()) { Cases(r, made); Check(r, "E0", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        sealed class FakeShot : IVol2Projectile
        {
            public Vector3 Pos, Vel; public bool Flying = true; public long TakenBy; public string Name;
            public object Identity => this;
            public bool InFlight => Flying;
            public Vector3 Position => Pos;
            public Vector3 Velocity => Vel;
            public bool Intercept(long owner) { if (!Flying) return false; Flying = false; TakenBy = owner; return true; }
            public float PowerHp => 6f;
            public EnemyVitals Shooter => null;
        }

        sealed class RecPort : IVol2MeleeWardPort
        {
            public int Ensured, Released; public Func<string, bool> Ask;
            public void Ensure(Func<string, bool> tryBlock) { Ensured++; Ask = tryBlock; }
            public void Release() { Released++; }
            public string Describe => "rec";
        }

        static Vol2ReturnExecutor ReturnOf(Rig rig)
        {
            rig.Coord.Verbs.TryGet(Verb.Return, out var e);
            var ex = e as Vol2ReturnExecutor;
            if (ex != null) { ex.Hub.ScanScene = false; ex.Hub.ScanPhysics = false; ex.Hub.ClearRegistered(); }
            return ex;
        }

        static Vol2OnceExecutor OnceOf(Rig rig) { rig.Coord.Verbs.TryGet(Verb.Once, out var e); return e as Vol2OnceExecutor; }

        /// <summary>Moves the shots along their velocity and ticks the coordinator, step by step.</summary>
        static void Fly(Rig rig, List<FakeShot> shots, double seconds, int steps)
        {
            double dt = seconds / steps;
            for (int i = 0; i < steps; i++)
            {
                foreach (var s in shots) if (s.Flying) s.Pos += s.Vel * (float)dt;
                rig.Advance(dt, 1);
            }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }
        static float Hp(PlayerVitals p) => p.Hp01 * p.MaxHp;

        static void Cases(Report r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            float R = (float)n.Get("space.ring.radius"), thick = (float)n.Get("space.ring.thickness");
            float outer = R + thick * 0.5f;
            Check(r, "F1", Mathf.Approximately(R, 2f) && Mathf.Approximately(thick, 0.6f), "the frame numbers: ring radius 2 m, band 0.6 m (the ward's wall)");

            // ---- W0 life ----
            long lifeUs;
            {
                var rig = Rig.Create(book); var once = OnceOf(rig);
                Check(r, "W0a", once != null, "the Once executor is registered");
                if (once == null) return;
                lifeUs = once.WardTuning.LifeUs(n, out string source);
                bool dataHas = n.Rows.Any(x => x.Key == Vol2WardTuning.LifeKey && x.Value.HasValue);
                Check(r, "W0b", source == (dataHas ? "numbers" : "TE-W1") && (dataHas || lifeUs == 2000000), "ward life " + lifeUs + " us from " + source + " (data has ward.life: " + dataHas + "; TE-W1 = 2.0 s)");
                var log = rig.Cast('우', out var dec);
                Check(r, "W0c", dec.Accepted && log != null && log.WardUntilUs - log.StartUs == lifeUs && log.Notes.Any(x => x.StartsWith("ward ", StringComparison.Ordinal) && x.Contains(source)),
                    "우 is a ward: it stands until commit + life, and the log notes the life's source");
            }

            // ---- W1 bodies ----
            {
                var rig = Rig.Create(book);
                rig.Add("Band", 2f, 0f); rig.Add("Hole", 0.8f, 0f); rig.Add("Out", 3.4f, 0f);
                var walker = rig.Add("Walker", 4f, 1f);
                var late = rig.Add("Late", 4f, -1f);
                var log = rig.Cast('구', out _);
                bool atCommit = log.Contacts.Select(c => c.TargetName).SequenceEqual(new[] { "Band" }) && log.Contacts[0].BudgetAccepted && log.Contacts[0].BudgetReason == "FirstContact";
                rig.Advance(0.5, 10);
                walker.MoveTo(At(2.1f, 1f) * 0.9f);                   // steps onto the band at 0.5 s
                rig.Advance(0.1, 2);
                walker.MoveTo(At(1.0f, 0.5f));                        // and across it, into the hollow
                rig.Advance(0.6, 12);
                bool runningMid = !log.Finished;
                rig.Advance(Vol2Time.ToSecondsF(lifeUs) - 1.2 + 0.05, 20);   // past the life
                late.MoveTo(At(2f, -0.2f));                           // on the band after the ward is gone
                rig.Advance(0.2, 4);
                var names = log.Contacts.Select(c => c.TargetName).ToList();
                Check(r, "W1a", atCommit, "구: the body on the band is touched at the commit (FirstContact); the hollow and the outside are not");
                Check(r, "W1b", names.Count(x => x == "Walker") == 1 && log.Contacts.First(c => c.TargetName == "Walker").NowUs - log.StartUs >= 500000,
                    "a walker that steps onto the band at 0.5 s is touched then, once, though it stays on / crosses the band for several ticks");
                Check(r, "W1c", !names.Contains("Late") && !names.Contains("Hole") && !names.Contains("Out") && names.Count == 2 && log.PaidContacts == 2,
                    "nothing after the life (Late), never the hollow or the outside: two paid contacts in all (" + string.Join(",", names) + ")");
                Check(r, "W1d", runningMid && log.Finished && log.Status == "Done" && rig.Status.Requests.Count == 2 && rig.Status.Requests.All(q => q.Kind == Vol2BodyActionKind.Bind),
                    "the cast runs while the ward stands and is Done after it; the body action (bind) once per touched body");
            }

            // ---- W2 shots ----
            {
                var rig = Rig.Create(book); var ret = ReturnOf(rig);
                Check(r, "W2a", ret != null, "the Return executor's hub is the scope's shot source (the ward borrows it)");
                if (ret == null) return;
                var shots = new List<FakeShot>();
                FakeShot Shot(string name, Vector3 at, Vector3 vel) { var s = new FakeShot { Name = name, Pos = at + Vector3.up * 1.1f, Vel = vel }; ret.Hub.Register(s); shots.Add(s); return s; }
                var near = Shot("Near", At(6f, 0f), new Vector3(0, 0, -8f));          // reaches the band at about 0.4 s
                var far = Shot("Far", At(0f, 21f), new Vector3(-8f, 0, 0));           // reaches the band at about 2.3 s: after the life
                var inside = Shot("Inside", At(0.5f, 0f), new Vector3(0, 0, 8f));      // fired from the hollow outward
                var log = rig.Cast('우', out _);
                Fly(rig, shots, Vol2Time.ToSecondsF(lifeUs) + 0.8, 80);
                var taken = log.WardEvents.Where(e => e.Kind == "Intercept").ToList();
                Check(r, "W2b", !near.Flying && near.TakenBy == log.Cast.Value && taken.Count == 1, "the shot coming in is taken by the ward (" + taken.Count + " intercept(s))");
                float d = taken.Count > 0 ? Flat(taken[0].Point, Vector3.zero) : -1f;
                float shotR = ret.Tuning.ProjectileRadius;
                Check(r, "W2c", taken.Count == 1 && Mathf.Abs(d - (outer + shotR)) < 0.05f && taken[0].AtUs - log.StartUs > 300000 && taken[0].AtUs - log.StartUs < 600000 && Mathf.Abs(taken[0].Point.y - 1.1f) < 1e-3f,
                    "it is taken where it meets the band's outer face (" + d.ToString("0.00") + " m = " + outer.ToString("0.00") + " + shot " + shotR.ToString("0.00") + "), at its own height, about 0.4 s after the cast");
                Check(r, "W2d", far.Flying && Flat(far.Pos, Vector3.zero) < R, "the shot that arrives after the life passes through (still flying, now " + Flat(far.Pos, Vector3.zero).ToString("0.0") + " m from the centre)");
                Check(r, "W2e", inside.Flying, "a shot fired from the hollow outward is not taken (the ward faces outward)");
            }

            // ---- W3 melee through the coordinator ----
            {
                var rig = Rig.Create(book); var port = new RecPort(); rig.Coord.MeleeWard = port;
                int raised = 0; Vector3 last = Vector3.zero;
                rig.Coord.MeleeBlocked += (l, p) => { raised++; last = p; };
                rig.Add("Enemy", 1.5f, 0f);
                bool before = rig.Coord.TryBlockMelee("Melee");
                var log = rig.Cast('무', out _);
                rig.Advance(0.3, 3);
                bool inside = rig.Coord.TryBlockMelee("Melee");
                rig.Caster.Position = At(R - 0.1f, 0f);
                bool nearEdge = rig.Coord.TryBlockMelee("Melee");
                rig.Caster.Position = At(R + 0.1f, 0f);
                bool outside = rig.Coord.TryBlockMelee("Melee");
                rig.Caster.Position = Vector3.zero;
                rig.Advance(Vol2Time.ToSecondsF(lifeUs) - 0.3 + 0.05, 10);
                bool after = rig.Coord.TryBlockMelee("Melee");
                var blocks = log.WardEvents.Where(e => e.Kind == "Block").ToList();
                Check(r, "W3a", !before && inside && nearEdge && !outside && !after, "melee: no ward = no block; blocked at the centre and at R-0.1 m; not at R+0.1 m (the caster left the ward); not after the life");
                Check(r, "W3b", blocks.Count == 2 && raised == 2 && rig.Coord.MeleeBlocks == 2 && Mathf.Abs(Flat(last, Vector3.zero) - R) < 1e-3f && Mathf.Abs(last.y - rig.Coord.Tuning.ChestHeight) < 1e-3f,
                    "each block is on the cast log and announced (2); it is drawn on the ward's wall toward the nearest enemy, chest high");
                Check(r, "W3c", port.Ensured == 1 && port.Ask != null, "the ward asked the melee port to bind when it began (once)");
                Check(r, "W3d", log.Contacts.All(c => c.Kind != "Parry") && rig.Damage.Requests.Count == log.PaidContacts, "a block pays nothing and asks no damage: the only damage is the ward's own body touch");
            }

            // ---- W4 the real seam ----
            {
                var rig = Rig.Create(book);
                var go = new GameObject("Vol2WardFixture_Player") { hideFlags = HideFlags.HideAndDontSave }; made.Add(go);
                var player = go.AddComponent<PlayerVitals>(); player.Restore(1f);
                int hits = 0; player.Damaged += _ => hits++;
                var port = new Vol2PlayerMeleeWardPort(() => player); rig.Coord.MeleeWard = port;
                float hp0 = Hp(player);
                bool m0 = EnemyStrike308.Deliver(player, null, 10f, IncomingDamageKind.Melee);
                float hp1 = Hp(player);
                var log = rig.Cast('수', out _);
                rig.Advance(0.2, 2);
                bool m1 = EnemyStrike308.Deliver(player, null, 10f, IncomingDamageKind.Melee);
                bool m2 = EnemyStrike308.Deliver(player, null, 10f, IncomingDamageKind.ElementalMelee);
                float hp2 = Hp(player); int hits2 = hits;
                bool rg = EnemyStrike308.Deliver(player, null, 10f, IncomingDamageKind.Ranged);
                float hp3 = Hp(player);
                rig.Advance(Vol2Time.ToSecondsF(lifeUs), 10);
                bool m3 = EnemyStrike308.Deliver(player, null, 10f, IncomingDamageKind.Melee);
                float hp4 = Hp(player);
                Check(r, "W4a", m0 && Mathf.Approximately(hp0 - hp1, 10f), "before the ward a melee hit lands (" + hp0 + " -> " + hp1 + ")");
                Check(r, "W4b", !m1 && !m2 && Mathf.Approximately(hp2, hp1) && hits2 == 1 && log.WardCount("Block") == 2 && port.Blocked == 2,
                    "inside the ward a melee and an elemental melee hit take no HP and raise no hit event; both are on the log as blocks");
                Check(r, "W4c", rg && Mathf.Approximately(hp2 - hp3, 10f), "a ranged hit is not the block's: it lands (a flying shot is the ward's to shoot down, not to block)");
                Check(r, "W4d", m3 && Mathf.Approximately(hp3 - hp4, 10f) && log.Finished, "after the ward's life a melee hit lands again");
                rig.Coord.Dispose();
                bool m4 = EnemyStrike308.Deliver(player, null, 10f, IncomingDamageKind.Melee);
                Check(r, "W4e", m4 && port.Describe == "no player", "the port lets go of the player with the coordinator (a later melee hit lands, nothing bound)");
            }

            // ---- W5 the other glyphs ----
            {
                var rig = Rig.Create(book);
                rig.Add("A", 2f, 0f);
                var guk = rig.Cast('국', out _);
                var geo = rig.Cast('거', out _);
                var gung = rig.Cast('궁', out _);
                Check(r, "W5a", guk != null && guk.WardUntilUs < 0 && geo != null && geo.WardUntilUs < 0 && geo.ParryWindowOpened && gung != null && gung.WardUntilUs < 0,
                    "국 (ㅜ+ㄱ) and 궁 (ㅜ+ㅇ) are no ward; 거 still opens its parry window");
                bool all = true; string bad = "";
                foreach (char c in "구누무수우")
                {
                    var rg = Rig.Create(book);
                    var l = rg.Cast(c, out var dc);
                    if (l == null || l.WardUntilUs - l.StartUs != lifeUs || l.Finished) { all = false; bad += c; }
                }
                Check(r, "W5b", all && Vol2OnceExecutor.IsWard(new Glyph('우', Body.Water, Frame.Ring, Verb.Once)) && !Vol2OnceExecutor.IsWard(new Glyph('오', Body.Water, Frame.Fan, Verb.Once)),
                    "the five 무받침 ㅜ glyphs all stand as wards" + (bad.Length > 0 ? " (not: " + bad + ")" : ""));
            }

            // ---- W6 reset ----
            {
                var rig = Rig.Create(book); var ret = ReturnOf(rig);
                var shot = new FakeShot { Name = "S", Pos = At(6f, 0f) + Vector3.up * 1.1f, Vel = new Vector3(0, 0, -8f) }; ret?.Hub.Register(shot);
                var log = rig.Cast('우', out _);
                rig.Advance(0.1, 1);
                rig.Coord.AbortAll();
                bool blocked = rig.Coord.TryBlockMelee("Melee");
                Fly(rig, new List<FakeShot> { shot }, 1.0, 20);
                Check(r, "W6", log.Status == "Aborted" && !blocked && shot.Flying && rig.Coord.ActiveCount == 0, "AbortAll takes the ward down at once: no block, the shot is not taken, nothing active");
            }
        }
    }
}
