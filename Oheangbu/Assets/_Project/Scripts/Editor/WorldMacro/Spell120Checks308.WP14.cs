using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-14 fixtures (SPEC-SPELL-120-308, sword form): the real wiring, real EnemyVitals and the registered
    // SwordFormEffect308. The form is cast through the resolver; strikes are called the way the click seam calls them
    // (SwordFormEffect308.Strike with the wiring as host); the effect's clock is explicit (Edit Mode time does not advance).
    // The TEST target of the counter strike is a fixture enemy carrying an EnemyController whose element is set by hand.
    // The input seam (ISwordFormSeam308 on the wiring) is checked as far as Edit Mode reaches: the draw gate is shut and
    // handed back, the harvest click is off and on again, a presenter that throws changes no rule result.
    // What stays for Play: a real click and a real release press arriving through the input actions, the sword in the hand
    // and its motion, real enemy AI.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP14Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP14" };

        // A presenter that fails at every call: a rule result must not depend on it (decision: presentation is best effort).
        sealed class WP14BrokenPresenter : ISpellPresenter
        {
            public int Calls;
            public SpellFxHandle Begin(in SpellFxRequest request) { Calls++; throw new InvalidOperationException("WP14 test: the presenter fails"); }
            public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args) { Calls++; throw new InvalidOperationException("WP14 test: the presenter fails"); }
            public void End(SpellFxHandle handle, bool immediate = false) { Calls++; throw new InvalidOperationException("WP14 test: the presenter fails"); }
            public void EndAll() { Calls++; }
        }

        // A counter whose answers are set by hand (D308-123, Q with the sword out): what the wiring's arbitration does with them.
        sealed class WP14FakeRiposte : ISwordRiposte308
        {
            public bool Can, Fires; public int Asked, Fired; public EnemyVitals Fallback;
            public bool CanRiposteFor308(EnemyVitals fallback) { Asked++; Fallback = fallback; return Can; }
            public bool RiposteFor308(EnemyVitals fallback) { Fired++; Fallback = fallback; return Fires; }
        }

        static partial void FixturesWP14(Report r)
        {
            // grid cells by index: 11 / 35 / 59 / 83 / 107 = the five swords (eo + siot), 1 = wood single attack, 7 = wood parry
            int[] cells = { 11, 35, 59, 83, 107 };
            char single = SpellGrammar308.LetterAt(1), parry = SpellGrammar308.LetterAt(7), wood = SpellGrammar308.LetterAt(cells[0]);
            if (!r.Wants(new string(cells.Select(SpellGrammar308.LetterAt).ToArray()))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var sword = effects.OfType<SwordFormEffect308>().FirstOrDefault();
            if (!r.Check("W14", sword != null, "SwordFormEffect308 is registered (SpellEffectInstaller308.WP14)")) return;
            using (var f = NewFixture(r, effects, WP14Sheets))
            {
                ISpellCastHost host = f.Wiring;
                f.Finals.Open.Add(SpellFinal.Siot);
                float t = Time.time, cost = f.Config.SpellInkCost, brush = f.Brush();
                SpellTickContext At(float seconds, float delta = 0f) => new SpellTickContext(t + seconds, delta, host, f.Fx);
                void Reset() { f.RestoreAll(); f.Wiring.ClearSpells308(SpellClearReason.Rest); }
                if (!r.Check("W14", f.Resolver.TryRow(wood, out var row) && row.Handler == SwordFormEffect308.HandlerId, "the wood sword's row runs sword.form")) return;
                float reach = row.F("sword.reach", 0f), interval = row.F("sword.interval", 0f), drain = row.F("sword.drain", 0f);
                int hits = Mathf.RoundToInt(row.F("groggy.hits", 0f)), steps = Mathf.RoundToInt(row.F("groggy.steps", 0f));
                string c1 = wood + "-1", c2 = wood + "-2", c3 = wood + "-3", c4 = wood + "-4";
                var target = f.Inside[0];
                Vector3 home = target.transform.position;
                Vector3 close = SpellFixture308.Origin + new Vector3(0f, 0f, reach * .75f), far = SpellFixture308.Origin + new Vector3(0f, 0f, reach * 1.2f);

                // ---- the form ----
                Reset();
                float ink = f.Ink.Value; int begun = f.Fx.Begun;
                f.Cast(wood);
                r.Check(c2, f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Buff && Near(f.Accepted[0].Power, row.BasePower * brush) &&
                    Near(ink - f.Ink.Value, cost) && sword.Active && sword.SwordElement == row.Element && f.Fx.Begun == begun + 1 && f.PendingCount == 0,
                    "a drawn sword glyph: one spell cost, accepted as a buff, the form is on with the glyph's element, presented through ISpellPresenter");

                // ---- clause 1: no spell while the form lasts ----
                ink = f.Ink.Value; f.ClearLog(); f.Aim(target);
                f.Cast(single); f.Cast(parry); f.Cast(wood);
                r.Check(c1, sword.BlocksDrawing && f.Accepted.Count == 0 && f.Plans.Count == 0 && f.PendingCount == 0 && f.Misfires.Count == 0 && f.Ink.Value == ink,
                    "in sword form an attack, a parry and a second sword glyph cast nothing and cost nothing (no misfire ink either)");

                // ---- clause 2: the strike (clock steps are 1.5 intervals: float sums must not sit on the boundary) ----
                target.transform.position = close;
                f.ClearLog(); float hp = target.Hp;
                var struck = sword.Strike(At(1f));
                r.Check(c2, struck.Swung && struck.Target == target && Near(struck.Damage, row.BasePower * brush) && Near(hp - target.Hp, row.BasePower * brush) && f.Hits.Count == 1 &&
                    f.Hits[0].Attack.Source == DamageSource.PlayerDirect && f.Hits[0].Attack.Element == row.Element,
                    "a strike hits the enemy inside the reach for the form's power (row base power x brush) as the player's own elemental attack");
                var early = sword.Strike(At(1f + interval * .5f));
                var onTime = sword.Strike(At(1f + interval * 1.5f));
                r.Check(c2, !early.Swung && onTime.Swung && onTime.Target == target && f.Hits.Count == 2, "one strike per sword.interval: a click inside the interval does nothing");
                target.transform.position = far;
                var air = sword.Strike(At(1f + interval * 3f));
                r.Check(c2, air.Swung && air.Target == null && air.Damage == 0f && f.Hits.Count == 2, "no enemy inside the reach: the strike cuts air (and still takes its interval)");
                r.Check(c2, !struck.Counter && !struck.GroggyGained && target.Groggy.Value01 == 0f, "an enemy without an element: plain strikes add no groggy");

                // ---- clause 3: the counter strike (TEST target: an enemy whose element the wood sword overcomes) ----
                var controller = target.gameObject.AddComponent<EnemyController>();
                Element beaten = System.Enum.GetValues(typeof(Element)).Cast<Element>().First(e => ElementRelations.Overcomes(row.Element, e));
                Element other = System.Enum.GetValues(typeof(Element)).Cast<Element>().First(e => !ElementRelations.Overcomes(row.Element, e) && e != row.Element);
                SpellFixture308.Set(controller, "_rangedElement", other);
                target.transform.position = close;
                float clock = 10f; bool anyGain = false;
                for (int i = 0; i < hits * 2; i++) { var s = sword.Strike(At(clock)); clock += interval * 1.5f; anyGain |= s.Counter || s.GroggyGained; }
                r.Check(c3, !anyGain && target.Groggy.Value01 == 0f, "strikes on an enemy whose element the sword does not overcome are not counter strikes: no groggy");
                SpellFixture308.Set(controller, "_rangedElement", beaten);
                int gained = 0; bool allCounter = true; float before = target.Groggy.Value01;
                for (int i = 1; i <= hits; i++)
                {
                    var s = sword.Strike(At(clock)); clock += interval * 1.5f; allCounter &= s.Counter;
                    if (s.GroggyGained) gained++;
                    if (i < hits) r.Check(c3, !s.GroggyGained && target.Groggy.Value01 == before, "counter strike " + i + " of " + hits + ": no groggy yet");
                }
                r.Check(c3, allCounter && gained == 1 && Near(target.Groggy.Value01, Mathf.Clamp01(steps / (float)f.Config.ParriesToBlossom)),
                    "the " + hits + "th counter strike raises the enemy's groggy by " + steps + " step(s) through the sword-counter source");
                r.Check(c3, steps < f.Config.ParriesToBlossom, "small: " + hits + " counter strikes together are worth " + steps + " step(s), never the whole meter");
                Object.DestroyImmediate(controller);

                // ---- clause 4: upkeep, intake stop, and the end with the ink ----
                f.Ink.Restore(.5f);
                float received = 0f; System.Action<float> listen = amount => received += amount;
                f.Ink.Gained += listen; f.Ink.Gain(.3f); f.Ink.Gained -= listen;
                r.Check(c4, Near(received, .3f) && Near(f.Ink.Value, .5f), "ink that comes in while the form lasts is handed back at once (intake stop)");
                sword.Tick(At(clock + 1f, 1f));
                r.Check(c4, Near(f.Ink.Value, .5f - drain) && sword.Active, "one second of the form costs sword.drain ink");
                r.Check(c4, f.Ink.LastSpendTime == Time.time && f.Config.InkRegenDelay > 0f && drain > 0f,
                    "every upkeep tick renews the pool's last-spend time, so natural regeneration (delay " + f.Config.InkRegenDelay + " s) cannot start while the form lasts");
                f.Ink.Restore(drain * .5f);
                sword.Tick(At(clock + 2f, 1f));
                r.Check(c4, f.Ink.Value == 0f && !sword.Active && !sword.BlocksDrawing, "the tick that empties the ink ends the form");
                var after = sword.Strike(At(clock + 10f));
                f.Ink.Restore(.5f); f.Ink.Gain(.3f);
                r.Check(c4, Near(f.Ink.Value, .8f), "after the form ink comes in again (the form no longer listens to the pool)");
                f.Ink.Restore(); f.ClearLog(); target.transform.position = home; f.Aim(target); f.Cast(single);
                r.Check(c4, !after.Swung && f.Accepted.Count == 1 && f.Plans.Count == 1, "after the form a click is no strike and spells cast again");

                // ---- manual release (TEST: allowed) and clear ----
                Reset(); f.Cast(wood);
                bool allowed = row.F("release.manual", 0f) > 0f;
                r.Check("W14", sword.Active && sword.ManualReleaseAllowed == allowed && sword.ReleaseByPlayer(At(1f)) == allowed && sword.Active == !allowed,
                    "release.manual decides whether the player may put the sword away");
                Reset(); f.Cast(wood); f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                r.Check("W14", !sword.Active, "death, rest, scene leave and disable end the form");

                // ---- the input seam: draw mode shut, harvest click off, both handed back (Edit Mode: no key can be pressed) ----
                var seam = (ISwordFormSeam308)f.Wiring;
                var drawing = f.Obj("Spell308_Drawing", SpellFixture308.Origin).AddComponent<DrawingInputController>();
                var harvest = f.Player.gameObject.AddComponent<HarvestAction>();
                SpellFixture308.Set(f.Wiring, "_drawingInput", drawing); SpellFixture308.Set(f.Wiring, "_harvest", harvest);
                int asked = 0; Func<bool> owner = () => { asked++; return true; };
                drawing.EntryAllowed = owner;
                Reset(); f.Cast(wood);
                r.Check("W14", sword.Active && f.Wiring.SwordFormHeld308 && !ReferenceEquals(drawing.EntryAllowed, owner) && drawing.EntryAllowed != null && !drawing.EntryAllowed() && asked == 0 && !harvest.enabled,
                    "the cast shuts the draw mode (the seam's gate stands in front of the owner's and answers no) and switches the harvest click off");
                drawing.EntryAllowed = owner;                                   // somebody puts their gate back (the walker re-enabled)
                sword.Tick(At(1f));
                r.Check("W14", !ReferenceEquals(drawing.EntryAllowed, owner) && !drawing.EntryAllowed() && !harvest.enabled, "the form holds the seam every frame: a replaced gate is shut again on the next tick");
                r.Check("W14", !seam.SwordStrikePressed308 && !seam.SwordReleasePressed308 && sword.Active, "without a pressed key the seam reports no click and no release");
                harvest.enabled = true; sword.Tick(At(1.1f));
                r.Check("W14", !harvest.enabled, "a harvest click switched on behind the form's back is off again on the next tick");
                f.Wiring.ClearSpells308(SpellClearReason.Rest);
                r.Check("W14", !sword.Active && !f.Wiring.SwordFormHeld308 && ReferenceEquals(drawing.EntryAllowed, owner) && drawing.EntryAllowed() && asked == 1 && harvest.enabled,
                    "rest ends the form: the owner's gate is back in place and asked again, the harvest click is on");
                f.RestoreAll(); f.Cast(wood);
                bool released = sword.ReleaseByPlayer(At(1f));
                bool answer = drawing.EntryAllowed != null && drawing.EntryAllowed();     // no key is down in Edit Mode: the gate hands itself back
                r.Check("W14", released == allowed && (!allowed || (!sword.Active && harvest.enabled && answer && ReferenceEquals(drawing.EntryAllowed, owner))),
                    "put away by the player: the form is off, the click is the harvest again, the draw gate goes back to its owner once the release key is up");
                Reset();
                drawing.EntryAllowed = null;
                f.Cast(wood); f.Wiring.ClearSpells308(SpellClearReason.SceneLeave);
                r.Check("W14", drawing.EntryAllowed == null && harvest.enabled, "a draw mode without an owner's gate has none again after the form");
                SpellFixture308.Set(f.Wiring, "_drawingInput", null); SpellFixture308.Set(f.Wiring, "_harvest", null);
                Object.DestroyImmediate(harvest);

                // ---- Q with the sword out (D308-123, TEST): one press, one action. The arbitration behind a pressed release key
                //      (Edit Mode cannot press it): the volley when it can fire, and the sword goes with it; else the sword only ----
                bool Arbitrate() => (bool)SpellFixture308.Call(f.Wiring, "SwordReleaseOrRiposte308");
                Reset(); f.Cast(wood); f.Aim(target);
                var counter = new WP14FakeRiposte();
                bool alone = Arbitrate();
                f.Wiring.SwordRiposte308 = counter;
                bool cannot = Arbitrate();
                r.Check("W14", alone && cannot && counter.Asked == 1 && counter.Fired == 0 && counter.Fallback == target,
                    "no counter, or one that cannot fire: the press puts the sword away (the counter is asked with the aimed target)");
                counter.Can = counter.Fires = true;
                bool fired = Arbitrate();
                counter.Fires = false;
                bool failed = Arbitrate();
                bool held = sword.Active;                                       // the answer alone ends nothing
                bool away = fired && sword.ReleaseByPlayer(At(2f));             // what the form's tick does with a yes
                r.Check("W14", fired && !failed && counter.Fired == 2 && held && away && !sword.Active,
                    "a counter that can fire takes the press and the answer is a release (the tick then puts the sword away); a counter chosen but not fired answers no release");
                var real = f.Obj("Spell308_Riposte", SpellFixture308.Origin).AddComponent<Oheangbu.App.World.Riposte301>();
                f.Wiring.SwordRiposte308 = real;
                bool bare = Arbitrate();
                Object.DestroyImmediate(real);
                bool gone = Arbitrate();
                r.Check("W14", bare && gone, "the volley without a profile or walker cannot fire, a destroyed one is none: the press puts the sword away");
                f.Wiring.SwordRiposte308 = null; f.Aim(null);

                // ---- a presenter that throws changes nothing (no refund with the form on, no lost strike) ----
                var broken = new WP14BrokenPresenter();
                bool logging = Debug.unityLogger.logEnabled;
                Debug.unityLogger.logEnabled = false;                           // the handler reports each failure; keep the console clean
                try
                {
                    f.Wiring.ConstructSpells(f.Registry, f.Finals, broken);
                    Reset(); ink = f.Ink.Value; f.Cast(wood);
                    r.Check("W14", broken.Calls > 0 && sword.Active && f.Accepted.Count == 1 && f.Misfires.Count == 0 && Near(ink - f.Ink.Value, cost),
                        "the presenter throws at Begin: the cast is still accepted, the form is on and its ink stays spent (never a refund with the form held)");
                    target.transform.position = close; hp = target.Hp; f.ClearLog();
                    var blind = sword.Strike(new SpellTickContext(t + 1f, 0f, host, broken));
                    r.Check("W14", blind.Swung && blind.Target == target && Near(hp - target.Hp, row.BasePower * brush) && f.Hits.Count == 1,
                        "the presenter throws at the strike cue: the strike lands as judged");
                    f.Ink.Restore(drain * .5f);
                    sword.Tick(new SpellTickContext(t + 2f, 1f, host, broken));
                    r.Check("W14", !sword.Active && !f.Wiring.SwordFormHeld308 && f.Ink.Value == 0f, "the presenter throws at the end: the form is off all the same");
                    target.transform.position = home;
                }
                finally { Debug.unityLogger.logEnabled = logging; f.Wiring.ConstructSpells(f.Registry, f.Finals, f.Fx); }

                // ---- D308-127 (TEST) the wood form is a spear: the line through two, weaker behind and too close ----
                float width = row.F("strike.width", 0f), through = row.F("strike.through", 1f), near = row.F("strike.near", 0f), inner = row.F("strike.inner", 1f);
                if (r.Check(wood + "-5", width > 0f, "the wood form's row is a line weapon (strike.width)"))
                {
                    var second = f.Inside[1]; Vector3 secondHome = second.transform.position;
                    Reset(); f.Cast(wood); f.Aim(null);
                    target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, reach * .5f);
                    second.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, reach * .9f);
                    float hp0 = target.Hp, hp1 = second.Hp; f.ClearLog();
                    var pierce = sword.Strike(At(1f));
                    r.Check(wood + "-5", pierce.Struck == 2 && pierce.Target == target && Near(hp0 - target.Hp, row.BasePower * brush) && Near(hp1 - second.Hp, row.BasePower * brush * through) &&
                        f.Hits.Count == 2 && Near(pierce.TotalDamage, row.BasePower * brush * (1f + through)),
                        "the spear strikes both enemies on its line: the first for the whole strike, the one behind for strike.through of it");
                    second.transform.position = SpellFixture308.Origin + new Vector3(width + .1f, 0f, reach * .9f);
                    hp1 = second.Hp; f.ClearLog();
                    var beside = sword.Strike(At(1f + interval * 1.5f));
                    r.Check(wood + "-5", beside.Struck == 1 && beside.Target == target && second.Hp == hp1 && f.Hits.Count == 1, "an enemy beside the line (half width + 0.1 m) is not struck");
                    target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, near * .5f);
                    hp0 = target.Hp; f.ClearLog();
                    var tooClose = sword.Strike(At(1f + interval * 3f));
                    r.Check(wood + "-6", tooClose.Struck == 1 && tooClose.Target == target && Near(hp0 - target.Hp, row.BasePower * brush * inner),
                        "an enemy inside strike.near takes strike.inner of the strike");
                    second.transform.position = secondHome; target.transform.position = home;
                }

                // ---- D308-127 (TEST) the earth form is an axe: a landed strike lowers defence; the form's end leaves it, Clear takes it back ----
                char axe = SpellGrammar308.LetterAt(cells[2]);
                if (r.Check(axe + "-2", f.Resolver.TryRow(axe, out var axeRow) && axeRow.F("break.defence", 0f) > 0f && axeRow.F("break.duration", 0f) > 0f,
                    "the earth form's row lowers defence (break.defence, break.duration)"))
                {
                    float shred = axeRow.F("break.defence", 0f), axeReach = axeRow.F("sword.reach", 0f), axeDrain = axeRow.F("sword.drain", 0f);
                    Reset(); f.Cast(axe);
                    target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, axeReach * .75f);
                    float hpA = target.Hp; f.ClearLog();
                    var chop = sword.Strike(At(1f));
                    float now = t + 1f;
                    r.Check(axe + "-1", chop.Struck == 1 && chop.Target == target && Near(hpA - target.Hp, axeRow.BasePower * brush) && f.Hits.Count == 1,
                        "one heavy strike on one enemy for the axe's power (row base power x brush)");
                    r.Check(axe + "-2", Near(target.Modifiers.DefenceShred(now), shred) &&
                        SpellPresentRules308.StateOf(null, false, 1f, target.Modifiers.DamageTakenScale(now), target.Modifiers.DefenceShred(now), 1f) == PresentState308.Armour,
                        "the struck enemy's defence is lowered by break.defence and reads as Armour (not Exposed)");
                    f.Ink.Restore(axeDrain * .5f);
                    sword.Tick(At(2f, 1f));
                    r.Check(axe + "-2", !sword.Active && Near(target.Modifiers.DefenceShred(t + 2f), shred), "the form running out of ink leaves the running shred alone");
                    f.Wiring.ClearSpells308(SpellClearReason.Rest);
                    r.Check(axe + "-2", target.Modifiers.DefenceShred(t + 2f) == 0f, "rest (Clear) takes the shred back");
                    target.transform.position = home;
                }

                // ---- the other four forms: they cast, strike with their element and lock spells (each at its own reach) ----
                foreach (int cell in cells.Skip(1))
                {
                    char letter = SpellGrammar308.LetterAt(cell);
                    Reset(); f.Cast(letter);
                    bool on = f.Resolver.TryRow(letter, out var other308) && sword.Active && sword.SwordElement == other308.Element && f.Accepted.Count == 1;
                    target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, (other308 != null ? other308.F("sword.reach", 0f) : reach) * .75f);
                    var hit = sword.Strike(At(1f));
                    r.Check(letter + "-1", on && hit.Swung && hit.Target == target && f.Hits.Count == 1 && f.Hits[0].Attack.Element == other308.Element,
                        "the glyph turns the brush into a sword of its own element and strikes with it");
                    f.ClearLog(); f.Cast(single);
                    r.Check(letter + "-1", f.Accepted.Count == 0 && f.Misfires.Count == 0, "no spell while this sword is out");
                    target.transform.position = home;
                }
                Reset();
            }
        }
    }
}
