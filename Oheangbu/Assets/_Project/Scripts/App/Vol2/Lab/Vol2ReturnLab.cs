using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>A runtime enemy shot for the spell lab (slice 7): flies slowly in a straight line, carries a power in HP and a shooter, and stops when a shape takes it. It is
    /// the lab's own object, registered in the executor's <see cref="Vol2ProjectileHub"/>; the game's enemies fire through EnemyController instead. A small dark unlit sphere, no collider.</summary>
    public sealed class Vol2LabShot : MonoBehaviour, IVol2Projectile
    {
        Vector3 _velocity;
        bool _frozen;
        Material _material;
        public float Power;
        public EnemyVitals ShooterVitals;
        public long InterceptedBy { get; private set; }
        public object Identity => this;
        public bool InFlight => !_frozen && isActiveAndEnabled;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => _velocity;
        public float PowerHp => Power;
        public EnemyVitals Shooter => ShooterVitals;

        public bool Intercept(long owner)
        {
            if (_frozen) return false;
            _frozen = true; InterceptedBy = owner;       // it stays (falling) until the row's verdict has read it; the provider destroys it then
            return true;
        }

        void Update()
        {
            if (!_frozen) transform.position += _velocity * Time.deltaTime;
            else transform.position += Vector3.down * 2f * Time.deltaTime;
        }

        void OnDestroy() { if (_material != null) Destroy(_material); }

        public static Vol2LabShot Create(string name, Vector3 position, Vector3 velocity, float powerHp, EnemyVitals shooter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Vol2LabShot_" + name;
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.position = position; go.transform.localScale = Vector3.one * 0.3f;
            var shot = go.AddComponent<Vol2LabShot>();
            shot._velocity = velocity; shot.Power = powerHp; shot.ShooterVitals = shooter;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                shot._material = new Material(shader) { name = "Vol2LabShot_ink" };
                if (shot._material.HasProperty("_BaseColor")) shot._material.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f));
                go.GetComponent<Renderer>().sharedMaterial = shot._material;
            }
            Destroy(go, 15f);
            return shot;
        }
    }

    /// <summary>The 20 glyphs of 돌아온다 (ㅇ) for the spell lab: P5 (ㅏ 강 낭 망 상 앙), F5 (ㅗ 공 농 몽 송 옹), R5 (ㅜ 궁 눙 뭉 숭 웅), A5 (ㅓ 겅 넝 멍 성 엉). Every row builds
    /// its own dummies, runtime enemy shots and planted materials; the verdict is written by hand from the geometry and the table sentences, never read back from a run.
    /// The executor's shot source is switched to the lab's own shots for the row (the lab scene may hold stray WP-13 test targets) and put back in Evaluate.</summary>
    public sealed class Vol2ReturnLab : IVol2LabProvider
    {
        public string Name => "Return";
        public bool Handles(Vol2SpellRowDto row) => row.Verb == Verb.Return;

        sealed class State
        {
            public Vol2ReturnExecutor Ex;
            public readonly List<Vol2LabShot> Shots = new List<Vol2LabShot>();
            public readonly Dictionary<Vol2LabShot, string> ShotNames = new Dictionary<Vol2LabShot, string>();
            public Func<IVol2Target, bool> PrevBoss;
            public bool BossSet, PrevScene, PrevPhysics;
            public IVol2ProjectileSource PrevSource;
            public bool ReturnOrdered;
            public long CreatedAtBuild;
            public string[] Intercepted = new string[0], Standing = new string[0];
            public string[] DragNames = new string[0];
            public string Boss = "";
            public string CarriedPlant = "";
            public string[] Cleansed = new string[0], Kept = new string[0], Wet = new string[0];
            public string Orbit = "";
        }

        readonly Dictionary<Vol2LabScenario, State> _state = new Dictionary<Vol2LabScenario, State>();
        readonly List<Vol2LabShot> _alive = new List<Vol2LabShot>();

        static Vol2ReturnExecutor FindExecutor(Vol2LabContext ctx)
            => ctx.Coordinator.Verbs.TryGet(Verb.Return, out var e) ? e as Vol2ReturnExecutor : null;

        // ------------------------------------------------------------------ scenarios ------------------------------------------------------------------

        public Vol2LabScenario Build(Vol2SpellRowDto row, Vol2LabContext ctx)
        {
            foreach (var s in _alive) if (s != null) UnityEngine.Object.Destroy(s.gameObject);
            _alive.Clear();
            // a row whose cast never started has no Evaluate: put back what it patched (the boss test, the shot source) before this row patches them again
            foreach (var stale in new List<State>(_state.Values)) Restore(stale, ctx.Coordinator);
            _state.Clear();
            var caster = ctx.Shapes.Caster;
            var sc = new Vol2LabScenario { Origin = caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(caster.AimDirection) };
            ctx.Scenario = sc;
            var st = new State { Ex = FindExecutor(ctx) };
            _state[sc] = st;
            if (st.Ex != null)
            {
                st.PrevSource = st.Ex.Projectiles; st.PrevScene = st.Ex.Hub.ScanScene; st.PrevPhysics = st.Ex.Hub.ScanPhysics;
                st.Ex.Projectiles = st.Ex.Hub; st.Ex.Hub.ScanScene = false; st.Ex.Hub.ScanPhysics = false; st.Ex.Hub.ClearRegistered();
            }
            var g = row.Glyph; var e = sc.Expect; char letter = g.Letter;
            var co = ctx.Coordinator;
            Vol2LabShot Shot(string name, float ahead, float side, float power, Vol2LabDummy shooter)
            {
                Vector3 at = ctx.At(ahead, side);
                Vector3 toward = sc.Origin - at; toward.y = 0f; toward = toward.sqrMagnitude > 1e-4f ? toward.normalized * 0.3f : Vector3.zero;
                var shot = Vol2LabShot.Create(name, at + Vector3.up * 1.1f, toward, power, shooter != null ? shooter.Vitals : null);
                if (st.Ex != null) st.Ex.Hub.Register(shot);
                st.Shots.Add(shot); st.ShotNames[shot] = name; _alive.Add(shot);
                return shot;
            }
            switch (g.Frame)
            {
                case Frame.Path:
                    {
                        ctx.Dummy("A", 4f, 0f); ctx.Dummy("B", 8f, 0f); ctx.Dummy("C", 4f, 3.0f); var sh = ctx.Dummy("Sh", 11f, 5f);
                        Shot("S1", 10f, 0f, 12f, sh); Shot("D1", 6f, 3.5f, 12f, sh);
                        e.Instant = new[] { "A", "B" }; e.Return = new[] { "B", "A" }; e.Absent = new[] { "C" };
                        st.ReturnOrdered = true; st.Intercepted = new[] { "S1" }; st.Standing = new[] { "D1" };
                        sc.MinSeconds = 1.4; sc.SettleSeconds = 1.4;
                        sc.Describe = "path goes out 12 m and back: A (4 m) and B (8 m) are hit on the way out, hit again on the way back (the head meets B first); C (4 m, 3 m to the side) is outside the corridor; the shot S1 on the axis (10 m) is taken, the shot D1 off the axis is not";
                        if (letter == '앙')
                        {
                            ctx.Plant("M1_mine_6m", Body.Wood, 6f, 0f, Vol2Faction.Player);
                            ctx.Plant("M2_mine_3m", Body.Wood, 3f, 0f, Vol2Faction.Player);
                            ctx.Plant("M3_mine_side", Body.Wood, 6f, 3f, Vol2Faction.Player);
                            ctx.Plant("M4_enemy_9m", Body.Wood, 9f, 0f, Vol2Faction.Enemy);
                            ctx.Plant("M5_mine_14m", Body.Wood, 14f, 0f, Vol2Faction.Player);
                            st.CarriedPlant = "M1_mine_6m"; st.CreatedAtBuild = co.Materials.TotalCreated;
                            sc.Describe += "; 앙: of my materials on the path (M1 6 m, M2 3 m) the returning head meets M1 first and carries it to 2 m ahead of the caster; M2, the one beside the path (M3), the enemy's (M4) and the one past its end (M5) stay";
                        }
                        break;
                    }
                case Frame.Fan:
                    {
                        ctx.Dummy("In3", 3f, 0f); ctx.Dummy("In4.8", 4.8f, -0.3f); ctx.Dummy("Mid7", 7f, 0f); ctx.Dummy("Out10.8", 10.8f, 0f); ctx.Dummy("Side", 5f, 4.5f); var sh = ctx.Dummy("Sh", 11f, 6f);
                        e.Instant = new[] { "In3", "In4.8", "Mid7" }; e.Return = new[] { "In3", "In4.8", "Mid7" }; e.Absent = new[] { "Out10.8", "Side" };
                        Shot("S1", 6f, 0f, 12f, sh); Shot("D1", 6f, 5f, 12f, sh);
                        st.Intercepted = new[] { "S1" }; st.Standing = new[] { "D1" };
                        sc.MinSeconds = 1.4; sc.SettleSeconds = 1.4;
                        sc.Describe = "the fan itself goes 5 m out and comes back (0.5 s each way): In3, In4.8 are hit at once, Mid7 when the fan has travelled to it, all three again on the way back; Out10.8 is past the fan's farthest reach (10 m + body), Side is beside it; the shot on the axis (6 m) is taken";
                        if (letter == '옹')
                        {
                            var air = ctx.Dummy("Air", 3f, 0.6f); air.Go.transform.position += Vector3.up * 2.5f; Physics.SyncTransforms();
                            ctx.Dummy("Boss", 4f, -1.2f);
                            e.Instant = new[] { "In3", "In4.8", "Mid7", "Air", "Boss" }; e.Return = e.Instant;
                            st.PrevBoss = co.BossTest; st.BossSet = true;
                            var prev = st.PrevBoss;
                            co.BossTest = t => (t != null && t.Name != null && t.Name.EndsWith("_Boss", StringComparison.Ordinal)) || (prev != null && prev(t));
                            st.DragNames = new[] { "In3", "In4.8", "Mid7", "Air" }; st.Boss = "Boss";
                            sc.Describe += "; 옹: on the way back the fan pulls every body it hit toward the caster (1.5 m out), Air (2.5 m up) is pulled down to the ground, Boss is not pulled at all; everyone is wet";
                        }
                        break;
                    }
                case Frame.Ring:
                    {
                        ctx.Dummy("R2", 2f, 0f); ctx.Dummy("R3.4", 3.4f, 0.5f); ctx.Dummy("Hole", 0.4f, 0.2f); ctx.Dummy("Far6", 6f, 0f); var sh = ctx.Dummy("Sh", 11f, 6f);
                        e.Instant = new[] { "R2", "R3.4" }; e.Return = new[] { "R2", "R3.4" }; e.Absent = new[] { "Hole", "Far6" };
                        Shot("S1", 3.0f, -0.6f, 12f, sh); Shot("D1", 6f, 2f, 12f, sh);
                        st.Intercepted = new[] { "S1" }; st.Standing = new[] { "D1" };
                        sc.MinSeconds = 1.4; sc.SettleSeconds = 1.4;
                        sc.Describe = "the ring spreads from radius 2 m to 4 m and shrinks back (0.5 s each way): R2 is on the band at once, R3.4 when the band reaches it, both again on the way back; Hole (inside), Far6 (outside) are never on the band; the shot at 3 m is taken";
                        if (letter == '웅')
                        {
                            Vector3 P(float r, float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)); }
                            var radii = new[] { 2.0f, 3.0f, 3.6f, 4.0f, 4.6f }; var angles = new[] { 0f, 72f, 144f, 216f, 288f };
                            for (int i = 0; i < 5; i++) ctx.Plant("E" + (i + 1) + "_enemy_r" + radii[i].ToString("0.0", CultureInfo.InvariantCulture), Body.Water, P(radii[i], angles[i]).x, P(radii[i], angles[i]).z, Vol2Faction.Enemy);
                            ctx.Plant("E6_enemy_far", Body.Water, 7f, 0f, Vol2Faction.Enemy);
                            ctx.Plant("V_world_vine", Body.Wood, P(3.0f, 36f).x, P(3.0f, 36f).z, Vol2Faction.World);
                            ctx.Plant("M_mine_2.5", Body.Fire, P(2.5f, 108f).x, P(2.5f, 108f).z, Vol2Faction.Player);
                            st.Cleansed = new[] { "E1", "E2", "E3", "E4" }; st.Kept = new[] { "E5", "E6", "V_world", "M_mine" }; st.Wet = new[] { "M_mine" };
                            st.CreatedAtBuild = co.Materials.TotalCreated;
                            sc.Describe += "; 웅: the band cleanses the ENEMY patches it passes over, four at most (E1 to E4 in the order it reaches them; E5 stays, E6 is out of reach), they end Cleansed and leave no product; the world's vine and my residue stay (mine is noted as wet); the pollution gate is asked for";
                        }
                        break;
                    }
                default:
                    {
                        ctx.Dummy("A", 1.2f, 0f); ctx.Dummy("B", 0f, 1.2f); ctx.Dummy("C", -1.2f, 0f); ctx.Dummy("D", 0f, -1.2f); ctx.Dummy("E", 4f, 0f); var sh = ctx.Dummy("Sh", 9f, 4f);
                        e.Instant = new[] { "A", "B", "C" }; e.Second = new[] { "A", "B", "C" }; e.Rejected["D"] = "TargetCap"; e.Absent = new[] { "E" };
                        st.Orbit = "A5";
                        float r = 1.6f, c45 = r * 0.70710678f;
                        if (letter == '엉')
                        {
                            Shot("S1", c45, c45, 6f, sh); Shot("S2", -c45, c45, 6f, sh); Shot("S3", -c45, -c45, 6f, sh); Shot("S4", c45, -c45, 6f, sh); Shot("D1", 4f, 3f, 6f, sh);
                            st.Intercepted = new[] { "S1", "S2", "S3", "S4" }; st.Standing = new[] { "D1" };
                        }
                        else
                        {
                            Shot("S1", -c45, c45, 12f, sh); Shot("S2", -c45, -c45, 12f, sh); Shot("D1", 4f, 3f, 12f, sh);
                            st.Intercepted = new[] { "S1", "S2" }; st.Standing = new[] { "D1" };
                        }
                        sc.MinSeconds = 2.0; sc.SettleSeconds = 2.0;
                        sc.Describe = "the vine circles the caster (1.6 m, one turn a second, 6 s): A (ahead, 1.2 m: on the band whether or not the earth pushes it outward), B (right), C (behind) are touched by the first turn, again by the second (the second contact, a separate swing) - three bodies in all, so D (left, a fourth) is refused (TargetCap); E is out of reach; no return damage; shots on the circle are taken" + (letter == '엉' ? " - 엉: the first three are turned back at their shooter (0.3 each, three at most), the fourth is only stopped" : "");
                        break;
                    }
            }
            return sc;
        }

        // ------------------------------------------------------------------ verdict ------------------------------------------------------------------

        static string Strip(string name) => name.Replace("Vol2LabDummy_", "");
        static string Word(Body b) => b == Body.Wood ? "bind" : b == Body.Fire ? "burn" : b == Body.Earth ? "push" : b == Body.Metal ? "cut" : "wet";
        static string Fmt(decimal d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        static string FmtF(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);
        static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.000001m;
        static List<string> NamesOf(Vol2CastLog log, string kind) => log.Contacts.Where(c => c.Kind == kind && c.BudgetAccepted).Select(c => Strip(c.TargetName)).ToList();

        public Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx)
        {
            var v = new Vol2OnceVerdict();
            var problems = new List<string>(); var checks = new List<string>();
            _state.TryGetValue(sc, out var st); _state.Remove(sc);
            var e = sc.Expect; var g = row.Glyph; var n = ctx.Services.Rulebook.Numbers; var co = ctx.Coordinator;
            void Need(bool ok, string what) { if (!ok) problems.Add(what); else checks.Add(what); }
            Vol2ReturnReport rep = null;
            try
            {
                Need(st != null && st.Ex != null, "the Return executor is registered");
                if (st != null && st.Ex != null) rep = st.Ex.ReportOf(log.Cast);
                Need(rep != null, "the executor kept a report of the cast");
                Need(log.Status == "Done", "cast status " + log.Status);
                var plan = decision.Plan;
                bool hasReturn = plan.Term(EffectKind.Return) > 0m;
                decimal returnAmount = plan.Term(EffectKind.Return);
                void SameSet(string kind, string[] want)
                {
                    var got = NamesOf(log, kind).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var exp = want.OrderBy(x => x, StringComparer.Ordinal).ToList();
                    Need(got.SequenceEqual(exp), kind + " [" + string.Join("|", got) + "] expected [" + string.Join("|", exp) + "]");
                }
                SameSet("Instant", e.Instant); SameSet("Second", e.Second);
                if (st != null && st.ReturnOrdered) { var got = NamesOf(log, "Return"); Need(got.SequenceEqual(e.Return), "Return [" + string.Join("|", got) + "] expected [" + string.Join("|", e.Return) + "] (the returning head meets the farthest first)"); }
                else SameSet("Return", e.Return);
                foreach (var a in e.Absent) Need(!log.Contacts.Any(c => Strip(c.TargetName) == a), a + " was not touched");
                foreach (var kv in e.Rejected)
                {
                    var recs = log.Contacts.Where(c => Strip(c.TargetName) == kv.Key).ToList();
                    Need(recs.Count > 0 && recs.All(c => !c.BudgetAccepted && c.BudgetReason == kv.Value), kv.Key + " is refused with " + kv.Value + " (" + string.Join("|", recs.Select(c => c.BudgetReason)) + ")");
                }
                foreach (var c in log.Contacts.Where(c => c.BudgetAccepted))
                    if (!c.DamageApplied) problems.Add(Strip(c.TargetName) + " " + c.Kind + " damage not applied (" + c.DamageNote + ")");
                // the amounts: the instant 0.7, the second contact 0.3, the return 0.35 (앙 0.525); an orbit has no return
                Need(log.Contacts.Where(c => c.BudgetAccepted && c.Kind == "Instant").All(c => Near(c.Amount, n.Get("action.instant"))), "every instant term is 0.7");
                Need(log.Contacts.Where(c => c.BudgetAccepted && c.Kind == "Second").All(c => Near(c.Amount, n.Get("action.eo.second"))), "every second contact is 0.3");
                if (hasReturn) Need(log.Contacts.Where(c => c.BudgetAccepted && c.Kind == "Return").All(c => Near(c.Amount, returnAmount)), "every return is " + Fmt(returnAmount) + (g.Letter == '앙' ? " (0.7 x 0.75)" : " (0.7 x 0.5)"));
                Need(hasReturn == (g.Frame != Frame.Attachment), "a ㅓ cast has no return term, the others have one");
                if (g.Frame == Frame.Attachment) Need(!log.Contacts.Any(c => c.Kind == "Return"), "no return damage on the orbit (action.eo.return = 0)");
                // one body action per body, the body's own; a drag is not one
                string word = Word(g.Body);
                foreach (var grp in log.Contacts.Where(c => c.Kind != "Drag" && c.BodyEffect.Length > 0).GroupBy(c => c.TargetId))
                    if (grp.Count() != 1) problems.Add(Strip(grp.First().TargetName) + " took " + grp.Count() + " body actions");
                foreach (var d in sc.Dummies)
                {
                    var affected = log.Contacts.Where(c => c.BudgetAccepted && c.Kind != "Reflect" && Strip(c.TargetName) == d.Name).ToList();       // a reflect record belongs to the shooter's ledger line, not to a touch
                    if (affected.Count == 0) continue;
                    var first = affected.FirstOrDefault(c => c.BodyEffect.Length > 0 && c.Kind != "Drag");
                    if (first == null || !first.BodyEffect.StartsWith(word, StringComparison.Ordinal)) problems.Add(d.Name + " body action '" + (first != null ? first.BodyEffect : "none") + "', expected " + word);
                }
                // the budget and the clock
                var budget = log.Budget;
                decimal expectedPaid = log.Contacts.Where(c => c.BudgetAccepted && (c.Kind == "Instant" || c.Kind == "Second" || c.Kind == "Return")).Sum(c => c.Amount);
                Need(Near(budget.PaidPerTarget, expectedPaid), "budget PaidPerTarget " + Fmt(budget.PaidPerTarget) + " = the accepted terms " + Fmt(expectedPaid));
                Need(Near(budget.PaidShared, 0m) && Near(budget.Transferred, 0m), "no shared pay, no transfer");
                if (g.Frame != Frame.Attachment)
                {
                    var rets = log.Contacts.Where(c => c.Kind == "Return").ToList();
                    long roundTrip = RuleTypesV3.Microseconds(n.Get("timing.return.roundTrip"));
                    Need(rets.Count > 0 && rets.All(c => c.BudgetAccepted && c.NowUs - log.StartUs < roundTrip), "every return came inside the round trip (" + (rets.Count > 0 ? ((rets.Max(c => c.NowUs) - log.StartUs) / 1000.0).ToString("0") : "-") + " ms of " + roundTrip / 1000 + ")");
                    var firstOf = log.Contacts.Where(c => c.Kind == "Instant" && c.BudgetAccepted).ToDictionary(c => c.TargetId, c => c.NowUs);
                    Need(rets.All(c => firstOf.TryGetValue(c.TargetId, out long at) && c.NowUs > at), "a return comes after the same body's first touch");
                    Need(rep != null && rep.TripEndUs > 0 && rep.TripEndUs - log.StartUs <= roundTrip, "the trip itself ends inside timing.return.roundTrip");
                }
                // shots
                foreach (var name in st.Intercepted)
                {
                    var shot = st.Shots.FirstOrDefault(s => s != null && st.ShotNames[s] == name);
                    Need(shot != null && shot.InterceptedBy == log.Cast.Value, "shot " + name + " was taken by this cast (owner " + (shot != null ? shot.InterceptedBy.ToString(CultureInfo.InvariantCulture) : "-") + ")");
                }
                foreach (var name in st.Standing)
                {
                    var shot = st.Shots.FirstOrDefault(s => s != null && st.ShotNames[s] == name);
                    Need(shot != null && shot.InFlight && shot.InterceptedBy == 0, "shot " + name + " outside the shape stays in the air");
                }
                if (rep != null) Need(rep.Intercepts.Count == st.Intercepted.Length, "the report counts " + rep.Intercepts.Count + " shots taken, expected " + st.Intercepted.Length);
                Need(co.Materials.Audit().Count == 0, "material audit: " + string.Join("; ", co.Materials.Audit()));
                PeakChecks(row, sc, st, log, rep, n, co, ctx, Need);
                v.Checks = string.Join(" | ", checks);
                v.MaterialIds = string.Join("|", sc.Planted.Select(kv => kv.Key + "=" + kv.Value.Id.Value.ToString(CultureInfo.InvariantCulture)));
                v.Created = string.Join("|", log.Materials.Select(m => m.Id + ":" + m.Kind + ":" + Fmt(m.Initial)));
                v.Consumed = string.Join("|", sc.Planted.Select(kv => kv.Value.Id + ":" + kv.Value.End));
                v.Depth = log.Materials.Count > 0 ? log.Materials.Max(m => m.Depth).ToString(CultureInfo.InvariantCulture) : "";
                v.Owner = log.Materials.Count > 0 || sc.Planted.Count > 0 ? "caster" : "";
                v.Balance = Fmt(budget.SharedRemaining);
                var ledger = log.Contacts.Where(c => c.BudgetAccepted && c.Amount > 0m).Select(c => c.Kind + ":" + Strip(c.TargetName) + ":" + Fmt(c.Amount)).ToList();
                if (rep != null)
                {
                    foreach (var i in rep.Intercepts) ledger.Add("Shot:" + i.Key + ":" + i.Leg + (i.Reflected ? ":reflected " + Fmt(i.Borrowed) + " of " + Fmt(i.Original) : i.Reason.Length > 0 ? ":" + i.Reason : ""));
                    if (g.Letter == '엉') ledger.Add("Borrowed:" + Fmt(rep.BorrowedPaid) + ":n=" + rep.Reflections + ":hpPerUnit=" + FmtF(rep.HpPerUnit));
                }
                v.Ledger = string.Join("|", ledger);
                v.Hit = string.Join("|", log.Contacts.Where(c => c.Kind != "Drag").Select(c => Strip(c.TargetName)).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                v.Expected = string.Join("|", e.Instant.Concat(e.Return).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                v.Unexpected = "";
                // the cast's own damage only: the borrowed (reflected) damage is its own ledger line, not part of the attack total
                v.Units = log.Contacts.Where(c => c.DamageApplied && c.Kind != "Reflect").Sum(c => c.Units) + log.Ticks.Where(t => t.Applied).Sum(t => t.Units);
                v.Hp = log.Contacts.Where(c => c.Kind != "Reflect").Sum(c => c.HpApplied) + log.Ticks.Sum(t => t.HpApplied);
                v.BodyObserved = string.Join("; ", log.Contacts.Where(c => c.BodyEffect.Length > 0).Select(c => Strip(c.TargetName) + " " + c.BodyEffect));
            }
            catch (Exception ex) { problems.Add("verdict exception " + ex.GetType().Name + " " + ex.Message); }
            finally { Restore(st, co); }
            v.Pass = problems.Count == 0; v.Note = string.Join("; ", problems);
            return v;
        }

        static void Restore(State st, Vol2CastCoordinator co)
        {
            if (st == null) return;
            if (st.BossSet) co.BossTest = st.PrevBoss;
            if (st.Ex != null) { st.Ex.Projectiles = st.PrevSource ?? st.Ex.Hub; st.Ex.Hub.ScanScene = st.PrevScene; st.Ex.Hub.ScanPhysics = st.PrevPhysics; st.Ex.Hub.ClearRegistered(); }
            foreach (var s in st.Shots) if (s != null) UnityEngine.Object.Destroy(s.gameObject);
        }

        static void PeakChecks(Vol2SpellRowDto row, Vol2LabScenario sc, State st, Vol2CastLog log, Vol2ReturnReport rep, NumbersV3Dto n, Vol2CastCoordinator co, Vol2LabContext ctx, Action<bool, string> Need)
        {
            char letter = row.Letter;
            Vol2Material Planted(string key) { sc.Planted.TryGetValue(key, out var m); return m; }
            switch (letter)
            {
                case '앙':
                    {
                        var m1 = Planted(st.CarriedPlant);
                        Vector3 dest = sc.Origin + sc.Forward * (float)n.Get("peak.앙.destinationDistance"); dest.y = sc.Origin.y;
                        Need(m1 != null && rep != null && rep.CarriedId == m1.Id.Value, "앙: the material carried is M1, the first my returning head met (" + (rep != null ? rep.CarriedId.ToString(CultureInfo.InvariantCulture) : "-") + ")");
                        Need(m1 != null && Vector3.Distance(Flat(m1.Footprint.Centre), Flat(dest)) < 0.01f, "앙: M1 now stands 2 m ahead of the caster");
                        long life = RuleTypesV3.Microseconds(n.Get("timing.g.life"));
                        Need(m1 != null && m1.Initial == 0.9m && m1.Remaining == 0.9m && m1.Drawn == 0m && m1.LifeUs == life && !m1.LifeExtended && m1.Splits == 0 && m1.Depth == 0, "앙: it keeps its amount (0.9, nothing drawn), its life (10 s) and its lineage - moved, not remade");
                        Need(m1 != null && m1.End == Vol2MaterialEnd.Expired, "앙: it ends by its own expiry, " + (m1 != null ? m1.End.ToString() : "-"));
                        bool others = true;
                        foreach (var key in new[] { "M2_mine_3m", "M3_mine_side", "M4_enemy_9m", "M5_mine_14m" })
                        {
                            var m = Planted(key); if (m == null) { others = false; continue; }
                            float expectedAhead = key == "M2_mine_3m" ? 3f : key == "M3_mine_side" ? 6f : key == "M4_enemy_9m" ? 9f : 14f;
                            float expectedSide = key == "M3_mine_side" ? 3f : 0f;
                            if (Vector3.Distance(Flat(m.Footprint.Centre), Flat(ctx.At(expectedAhead, expectedSide))) > 0.01f) others = false;
                        }
                        Need(others, "앙: the other four stay where they were (only ONE is carried)");
                        Need(co.Materials.TotalCreated == st.CreatedAtBuild && log.Materials.Count == 0, "앙: nothing was made, only moved");
                        break;
                    }
                case '엉':
                    {
                        var refl = log.Contacts.Where(c => c.Kind == "Reflect").ToList();
                        var ok = refl.Where(c => c.BudgetAccepted).ToList();
                        Need(rep != null && rep.Reflections == (int)n.Get("peak.엉.reflectionCount") && ok.Count == 3, "엉: three shots are turned back (the cap), " + (rep != null ? rep.Reflections : -1));
                        Need(rep != null && Near(rep.BorrowedPaid, 0.9m) && ok.All(c => Near(c.Amount, 0.3m)), "엉: each lends 0.5 x (6 HP / 10 per unit) = 0.3, together " + (rep != null ? Fmt(rep.BorrowedPaid) : "-") + " (never above 1.0)");
                        Need(rep != null && rep.BorrowedPaid <= n.Get("combat.borrowed.reflectTotalCap"), "엉: the borrowed total stays within combat.borrowed.reflectTotalCap");
                        Need(rep != null && rep.Intercepts.Count == 4 && rep.Intercepts.Count(i => i.Reflected) == 3 && rep.Intercepts.Count(i => !i.Reflected && i.Reason == "ReflectionCount") == 1, "엉: the fourth shot is only stopped (ReflectionCount): " + (rep != null ? string.Join("/", rep.Intercepts.Select(i => i.Reflected ? "R" : i.Reason)) : "-"));
                        var sh = sc.Find("Sh");
                        Need(sh != null && sh.Vitals != null && sh.Vitals.Hp < sh.HpBefore && Math.Abs((sh.HpBefore - sh.Vitals.Hp) - ok.Sum(c => c.HpApplied)) < 0.01f, "엉: the shooter lost exactly what the reflections applied (" + (sh != null ? FmtF(sh.HpBefore - sh.Vitals.Hp) : "-") + " HP)");
                        Need(ok.All(c => c.DamageNote.Contains("HP per unit")), "엉: the conversion is written into every reflect record");
                        decimal attackTotal = log.Contacts.Where(c => c.BudgetAccepted && (c.Kind == "Instant" || c.Kind == "Second")).Sum(c => c.Amount);
                        Need(Near(attackTotal, 3m) && Near(log.Budget.PaidPerTarget, 3m), "엉: the borrowed power is a separate ledger: the cast's own total is still 3 x (0.7 + 0.3)");
                        break;
                    }
                case '옹':
                    {
                        long max = RuleTypesV3.Microseconds(n.Get("combat.bind.maxDuration"));
                        var drags = rep != null ? rep.Drags : new List<Vol2ReturnReport.Drag>();
                        foreach (var name in st.DragNames)
                        {
                            var d = drags.FirstOrDefault(x => Strip(x.Name) == name);
                            var dummy = sc.Find(name);
                            Need(d != null && !d.Boss && d.DurationUs > 0 && d.DurationUs <= max && d.Meters > 0.05f && d.Sink.Length > 0 && d.Sink != "boss exempt", "옹: " + name + " is pulled, at most 1.5 s (" + (d != null ? d.Sink + " " + d.DurationUs + " us" : "none") + ")");
                            if (dummy != null && dummy.Go != null)
                            {
                                float away = Vector3.Distance(Flat(dummy.Go.transform.position), Flat(sc.Origin));
                                Need(away <= 1.5f + 0.25f, "옹: " + name + " ended " + FmtF(away) + " m from the caster (about 1.5)");
                            }
                        }
                        var air = drags.FirstOrDefault(x => Strip(x.Name) == "Air"); var airDummy = sc.Find("Air");
                        Need(air != null && air.Airborne && airDummy != null && airDummy.Go != null && airDummy.Go.transform.position.y <= sc.Origin.y + 0.35f, "옹: the airborne body is pulled down to the ground (y " + (airDummy != null && airDummy.Go != null ? FmtF(airDummy.Go.transform.position.y - sc.Origin.y) : "-") + " above the caster's feet)");
                        var boss = drags.FirstOrDefault(x => Strip(x.Name) == st.Boss); var bossDummy = sc.Find(st.Boss);
                        Need(boss != null && boss.Boss && boss.Sink == "boss exempt" && bossDummy != null && bossDummy.Go != null && Vector3.Distance(bossDummy.Go.transform.position, bossDummy.Start) < 0.01f, "옹: the boss is exempt from the drag (not moved)");
                        Need(log.Contacts.Where(c => c.Kind == "Touch" || c.Kind == "Instant").All(c => c.BodyEffect.Length == 0 || c.BodyEffect.StartsWith("wet", StringComparison.Ordinal)), "옹: everyone it touches is wet");
                        break;
                    }
                case '웅':
                    {
                        var gone = st.Cleansed.Select(k => sc.Planted.FirstOrDefault(kv => kv.Key.StartsWith(k + "_", StringComparison.Ordinal)).Value).ToList();
                        Need(gone.All(m => m != null && m.End == Vol2MaterialEnd.Cleansed) && rep != null && rep.Cleansed.Count == (int)n.Get("peak.웅.cleanseCount"), "웅: four enemy patches end Cleansed (the cap), the first four the band reached");
                        var order = gone.Where(m => m != null).Select(m => m.Id.Value).ToList();
                        Need(rep != null && rep.Cleansed.SequenceEqual(order), "웅: in the order the band reached them (E1, E2, E3, E4)");
                        var kept = st.Kept.Select(k => sc.Planted.FirstOrDefault(kv => kv.Key.StartsWith(k, StringComparison.Ordinal)).Value).ToList();
                        Need(kept.All(m => m != null && m.End == Vol2MaterialEnd.Expired), "웅: the fifth patch, the far one, the world's vine and my residue are not cleansed: " + string.Join(",", kept.Select(m => m != null ? m.End.ToString() : "?")));
                        Need(co.Materials.TotalCreated == st.CreatedAtBuild && log.Materials.Count == 0, "웅: removing created no material (no product)");
                        var mine = sc.Planted.FirstOrDefault(kv => kv.Key.StartsWith("M_mine", StringComparison.Ordinal)).Value;
                        Need(rep != null && mine != null && rep.WetRequests.Contains(mine.Id.Value), "웅: my residue in the band is noted as wet (no wet flag on a material yet)");
                        Need(log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("pollution"), "웅: the pollution gate is asked for once (" + string.Join(" / ", log.FieldNotes) + ")");
                        break;
                    }
            }
            if (row.Frame == Frame.Attachment)
            {
                Need(rep != null && rep.OrbitRevolutions >= 2, "the circle turned at least twice before the budget ended the contacts (" + (rep != null ? rep.OrbitRevolutions : -1) + ")");
                var inst = log.Contacts.Where(c => c.Kind == "Instant" && c.BudgetAccepted).ToDictionary(c => c.TargetId, c => c);
                var sec = log.Contacts.Where(c => c.Kind == "Second" && c.BudgetAccepted).ToList();
                long delay = RuleTypesV3.Microseconds(n.Get("timing.eo.secondDelay"));
                Need(sec.Count == 3 && sec.All(c => inst.TryGetValue(c.TargetId, out var f) && c.NowUs - f.NowUs >= delay && c.SwingId != f.SwingId && c.SwingId > 0), "the second contact is 0.3 s or more after the first, on another swing (another turn), once per body");
                Need(log.Budget.State.Targets.Count == 3, "three bodies in all");
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
