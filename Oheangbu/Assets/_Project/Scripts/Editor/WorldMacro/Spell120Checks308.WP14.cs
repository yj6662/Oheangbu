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

        // Writes down every cue with its step and finisher flag (debate 15: the presenter learns the combo from the cue).
        sealed class WP14CuePresenter : ISpellPresenter
        {
            public readonly System.Collections.Generic.List<(SpellFxCue Cue, int Index, bool Finish, float At)> Cues = new System.Collections.Generic.List<(SpellFxCue, int, bool, float)>();
            public SpellFxHandle Begin(in SpellFxRequest request) => new SpellFxHandle(1);
            public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args) { Cues.Add((cue, args.Index, args.Finish, args.At)); }
            public void End(SpellFxHandle handle, bool immediate = false) { }
            public void EndAll() { }
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

                // ---- D308-127 debate 15 (TEST) the metal form is a two-handed sword: a chain of strikes, the last one the finisher,
                //      then a rest; the cue carries the step (the presenter swings the finisher from it). Clock steps sit .01 s
                //      past each boundary. The buffered click needs a pressed key (Play); the pure harness checks its rule ----
                char metal = SpellGrammar308.LetterAt(cells[3]);
                if (r.Check(metal + "-7", f.Resolver.TryRow(metal, out var comboRow) && comboRow.F("combo.hits", 0f) >= 2f, "the metal form's row strikes in a chain (combo.hits)"))
                {
                    int chain = Mathf.RoundToInt(comboRow.F("combo.hits", 0f));
                    float gap = comboRow.F("sword.interval", 0f), rest = comboRow.F("combo.rest", 0f), window = comboRow.F("combo.window", 0f), finish = comboRow.F("combo.finish", 1f);
                    float whole = comboRow.BasePower * brush;
                    var cues = new WP14CuePresenter();
                    try
                    {
                        f.Wiring.ConstructSpells(f.Registry, f.Finals, cues);
                        Reset(); f.Cast(metal); f.Aim(null);
                        target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, comboRow.F("sword.reach", 0f) * .75f);
                        SpellTickContext Clock(float seconds) => new SpellTickContext(t + seconds, 0f, host, cues);
                        var chained = new System.Collections.Generic.List<SwordStrike308>(); var dealt = new System.Collections.Generic.List<float>();
                        float when = 1f;
                        for (int i = 0; i < chain; i++) { float hpBefore = target.Hp; chained.Add(sword.Strike(Clock(when))); dealt.Add(hpBefore - target.Hp); when += gap + .01f; }
                        bool order = chained.Select((s, i) => s.Swung && s.Step == i + 1).All(x => x);
                        bool shares = dealt.Take(chain - 1).All(d => Near(d, whole)) && Near(dealt[chain - 1], whole * finish);
                        r.Check(metal + "-7", order && shares, "a chain of " + chain + " strikes " + gap + " s apart: steps 1.." + chain + ", the last one x" + finish + " (" + string.Join(" / ", dealt.Select(d => d.ToString("0.##"))) + ")");
                        float finisherAt = 1f + (chain - 1) * (gap + .01f);
                        var resting = sword.Strike(Clock(finisherAt + gap + .01f));
                        var anew = sword.Strike(Clock(finisherAt + rest + .01f));
                        r.Check(metal + "-7", !resting.Swung && anew.Swung && anew.Step == 1, "after the finisher the sword rests combo.rest (" + rest + " s, not the " + gap + " s interval), then the chain starts over at 1");
                        float late = finisherAt + rest + .01f + gap + window + .1f;
                        var dropped = sword.Strike(Clock(late));
                        r.Check(metal + "-7", dropped.Swung && dropped.Step == 1, "a strike later than combo.window after the allowed moment starts the chain over (step 1, not 2)");
                        var hitCues = cues.Cues.Where(c => c.Cue == SpellFxCue.Hit).ToList();
                        r.Check(metal + "-7", hitCues.Count == chain + 2 && hitCues.Take(chain).Select((c, i) => c.Index == i + 1 && c.Finish == (i == chain - 1)).All(x => x) && !hitCues[chain].Finish,
                            "each Hit cue carries its step, and only the finisher's says Finish (the presenter swings it differently)");
                    }
                    finally { f.Wiring.ConstructSpells(f.Registry, f.Finals, f.Fx); target.transform.position = home; }
                }

                // ---- D308-127 debate 15 (TEST) the water form is a flail: up to three enemies in a wide cone, the ones after the
                //      first weaker; every landed swing holds the ink upkeep (per enemy, capped) through the player's domain ----
                char water = SpellGrammar308.LetterAt(cells[4]);
                if (r.Check(water + "-5", f.Resolver.TryRow(water, out var flailRow) && Mathf.RoundToInt(flailRow.F("strike.count", 1f)) >= 3 && flailRow.F("hold.per", 0f) > 0f,
                    "the water form's row strikes a crowd (strike.count) and holds the upkeep (hold.per)"))
                {
                    float fReach = flailRow.F("sword.reach", 0f), fThrough = flailRow.F("strike.through", 1f), per = flailRow.F("hold.per", 0f), cap = flailRow.F("hold.cap", 0f);
                    float fDrain = flailRow.F("sword.drain", 0f), fWhole = flailRow.BasePower * brush;
                    var crowd = f.Inside; var homes = crowd.Select(e => e.transform.position).ToArray();
                    Vector3 P(float degrees, float distance) => SpellFixture308.Origin + new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad)) * distance;
                    try
                    {
                        Reset(); f.Cast(water); f.Aim(null);
                        crowd[0].transform.position = P(0f, fReach * .4f); crowd[1].transform.position = P(40f, fReach * .6f); crowd[2].transform.position = P(-55f, fReach * .8f);
                        float[] hpBefore = crowd.Select(e => e.Hp).ToArray(); f.ClearLog();
                        var swing = sword.Strike(At(1f));
                        r.Check(water + "-5", swing.Struck == 3 && swing.Target == crowd[0] && Near(hpBefore[0] - crowd[0].Hp, fWhole) && Near(hpBefore[1] - crowd[1].Hp, fWhole * fThrough) &&
                            Near(hpBefore[2] - crowd[2].Hp, fWhole * fThrough) && f.Hits.Count == 3,
                            "three enemies ahead and at 40 / -55 degrees: all struck, the nearest for the whole swing, the other two for strike.through (" + fThrough + ") of it");
                        float held3 = 1f + Mathf.Min(cap, per * 3f);
                        // the domain's reader drops what has run out as it is asked: ask in clock order, as the game does
                        f.Ink.Restore(.5f);
                        sword.Tick(At(held3 - .05f, .5f)); float inkHeld = f.Ink.Value;
                        bool on3 = f.Wiring.PlayerDomain308.DrainHeld(t + held3 - .01f), off3 = !f.Wiring.PlayerDomain308.DrainHeld(t + held3 + .01f);
                        sword.Tick(At(held3 + .05f, .5f)); float inkAfter = f.Ink.Value;
                        r.Check(water + "-6", on3 && off3 && Near(inkHeld, .5f) && Near(inkAfter, .5f - fDrain * .5f),
                            "three landed hold the upkeep " + Mathf.Min(cap, per * 3f) + " s (the cap): no ink drains inside it, the drain runs again after it (held " + on3 + "/" + !off3 +
                            ", ink " + inkHeld.ToString("0.###") + " -> " + inkAfter.ToString("0.###") + ", drain " + fDrain + ")");
                        crowd[1].transform.position = homes[1] + new Vector3(0f, 0f, 60f); crowd[2].transform.position = homes[2] + new Vector3(0f, 0f, 60f);
                        float second = held3 + .2f;   // past the interval's boundary (1 + 1.0 s), not on it
                        var lone = sword.Strike(At(second));
                        bool on1 = f.Wiring.PlayerDomain308.DrainHeld(t + second + per - .01f), off1 = !f.Wiring.PlayerDomain308.DrainHeld(t + second + per + .01f);
                        r.Check(water + "-6", lone.Struck == 1 && on1 && off1, "one enemy landed holds hold.per (" + per + " s)");
                        Vector3 ahead = crowd[0].transform.position; crowd[0].transform.position = homes[0] + new Vector3(0f, 0f, 60f);
                        var nobody = sword.Strike(At(second + 1.5f));
                        bool none = nobody.Swung && nobody.Struck == 0 && !f.Wiring.PlayerDomain308.DrainHeld(t + second + 1.5f + .01f);
                        crowd[0].transform.position = ahead;
                        sword.Strike(At(second + 3f));
                        f.Wiring.ClearSpells308(SpellClearReason.Rest);
                        r.Check(water + "-6", none && !f.Wiring.PlayerDomain308.DrainHeld(t + second + 3f + .01f), "a swing that met no one holds nothing; the form's end lets its hold go at once");
                    }
                    finally { for (int i = 0; i < crowd.Length; i++) crowd[i].transform.position = homes[i]; }
                }

                // ---- D308-127 debate 16 (TEST) the fire form is a horn bow: hold the click shot.draw, let go to loose one arrow at
                //      the aimed enemy inside shot.range; it lands distance / shot.speed later and pays shot.ink once. A short
                //      draw fizzles (nothing spent), the draw slows the walk to shot.move. Clocks are driven by hand (Edit Mode
                //      cannot hold a key); the pure harness checks the 60 fps input ----
                char fire = SpellGrammar308.LetterAt(cells[1]);
                if (r.Check(fire + "-8", f.Resolver.TryRow(fire, out var bowRow) && bowRow.F("shot.draw", 0f) > 0f && bowRow.F("shot.speed", 0f) > 0f && bowRow.F("shot.range", 0f) > 0f,
                    "the fire form's row is a bow (shot.draw, shot.speed, shot.range)"))
                {
                    float draw = bowRow.F("shot.draw", 0f), speed = bowRow.F("shot.speed", 0f), range = bowRow.F("shot.range", 0f), shotInk = bowRow.F("shot.ink", 0f);
                    float move = bowRow.F("shot.move", 1f), recover = bowRow.F("sword.interval", 0f), bowWhole = bowRow.BasePower * brush;
                    string b8 = fire + "-8", b9 = fire + "-9";
                    var cues = new WP14CuePresenter();
                    var motorBefore = SpellFixture308.Get(f.Wiring, "_motor");
                    var motor = f.Obj("Spell308_BowMotor", SpellFixture308.Origin).AddComponent<PlayerMotor>();
                    try
                    {
                        f.Wiring.ConstructSpells(f.Registry, f.Finals, cues);
                        SpellFixture308.Set(f.Wiring, "_motor", motor);
                        SpellTickContext Clock(float seconds) => new SpellTickContext(t + seconds, 0f, host, cues);
                        BowStep308 Bow(float seconds, bool down, bool up) => sword.BowInput(Clock(seconds), down, up);
                        float dist = range * .8f, flight = dist / speed;
                        Reset(); f.Cast(fire); f.Aim(target);
                        target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, dist);
                        r.Check(b8, sword.IsBow && !sword.Strike(Clock(1f)).Swung, "the bow form holds a bow: a strike is no swing (the click draws)");

                        // a short draw fizzles: nothing leaves, nothing is spent; letting go of nothing cancels
                        float bowInk = f.Ink.Value; cues.Cues.Clear();
                        var begin = Bow(1f, true, false); bool drawingNow = sword.Drawing; float slowed = motor.FormMovementScale308;
                        Bow(1f + draw * .5f - .01f, true, false);
                        var midCue = cues.Cues.LastOrDefault(c => c.Cue == SpellFxCue.Tick);
                        var fizzle = Bow(1f + draw * .5f, false, true);
                        r.Check(b8, begin == BowStep308.Begin && drawingNow && fizzle == BowStep308.Fizzle && !sword.Drawing && sword.ArrowsInFlight == 0 && f.Ink.Value == bowInk &&
                            !cues.Cues.Any(c => c.Cue == SpellFxCue.Secondary) && cues.Cues.Last().Cue == SpellFxCue.Tick,
                            "let go before shot.draw (" + draw + " s): the draw fizzles, no arrow and no ink");
                        r.Check(b8, midCue.Cue == SpellFxCue.Tick && Mathf.Abs(midCue.At - (t + 1f + draw * .5f - .01f)) < 1e-3f,
                            "while drawn, each frame sends how far drawn as a Tick cue (the presenter bends the bow from it)");
                        r.Check(b9, Near(slowed, Mathf.Clamp(move, .05f, 1f)) && Near(motor.FormMovementScale308, 1f),
                            "the draw slows the walk to shot.move (" + move + "); the walk is whole again once the draw ends");
                        var again = Bow(2f, true, false); var cancel = Bow(2.1f, false, false);
                        r.Check(b8, again == BowStep308.Begin && cancel == BowStep308.Cancel && !sword.Drawing && f.Ink.Value == bowInk, "a draw that ends without a release (the click lost) is cancelled, nothing spent");

                        // the full draw: one arrow at the aimed enemy, shot.ink once, the hit lands after the flight
                        cues.Cues.Clear(); float bowHp = target.Hp; f.ClearLog();
                        Bow(3f, true, false);
                        float loosed = 3f + draw + .01f;
                        var shot = Bow(loosed, false, true);
                        var arrowCue = cues.Cues.FirstOrDefault(c => c.Cue == SpellFxCue.Secondary);
                        r.Check(b8, shot == BowStep308.Fire && sword.ArrowsInFlight == 1 && Near(bowInk - f.Ink.Value, shotInk) && target.Hp == bowHp && f.Hits.Count == 0 &&
                            arrowCue.Cue == SpellFxCue.Secondary && Mathf.Abs(arrowCue.At - flight) < 1e-3f,
                            "drawn " + draw + " s and let go: one arrow flies " + flight.ToString("0.###") + " s (" + dist + " m at " + speed + " m/s), shot.ink (" + shotInk + ") is paid once, nothing hits yet");
                        var tooSoon = Bow(loosed + recover * .5f, true, false);
                        var ready = Bow(loosed + recover + .01f, true, false);
                        Bow(loosed + recover + .02f, false, false);
                        r.Check(b8, tooSoon == BowStep308.None && ready == BowStep308.Begin, "after a shot the bow recovers sword.interval (" + recover + " s) before the next draw");
                        sword.Tick(Clock(loosed + flight - .02f));
                        bool waits = target.Hp == bowHp && sword.ArrowsInFlight == 1;
                        sword.Tick(Clock(loosed + flight + .01f));
                        r.Check(b8, waits && sword.ArrowsInFlight == 0 && Near(bowHp - target.Hp, bowWhole) && f.Hits.Count == 1 && f.Hits[0].Attack.Source == DamageSource.PlayerDirect &&
                            f.Hits[0].Attack.Element == bowRow.Element,
                            "the arrow lands when its flight is over: the form's power (row base power x brush) as the player's own elemental attack");

                        // no aimed enemy, or one beyond shot.range: the arrow flies straight ahead to a miss (ink still paid)
                        f.Aim(null); bowInk = f.Ink.Value; cues.Cues.Clear();
                        Bow(10f, true, false); Bow(10f + draw + .01f, false, true);
                        var loose = cues.Cues.FirstOrDefault(c => c.Cue == SpellFxCue.Secondary);
                        f.Aim(target); target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, range * 1.2f);
                        Bow(12f, true, false); Bow(12f + draw + .01f, false, true);
                        r.Check(b8, loose.Cue == SpellFxCue.Secondary && sword.ArrowsInFlight == 0 && Near(bowInk - f.Ink.Value, shotInk * 2f),
                            "no aimed enemy, or the aimed one beyond shot.range: the arrow is loosed at nothing (a miss to look at, no hit kept)");

                        // an arrow in the air outlives the form; rest (Clear) takes it back
                        target.transform.position = SpellFixture308.Origin + new Vector3(0f, 0f, dist);
                        Bow(14f, true, false); Bow(14f + draw + .01f, false, true);
                        bowHp = target.Hp;
                        bool putAway = sword.ReleaseByPlayer(Clock(14.1f + draw));
                        sword.Tick(Clock(14f + draw + flight + .05f));
                        r.Check(b8, putAway && !sword.Active && sword.ArrowsInFlight == 0 && Near(bowHp - target.Hp, bowWhole) && Near(motor.FormMovementScale308, 1f),
                            "the sword put away with an arrow in the air: the arrow still lands; the walk is whole");
                        Reset(); f.Cast(fire); f.Aim(target);
                        Bow(20f, true, false); Bow(20f + draw + .01f, false, true);
                        bowHp = target.Hp;
                        f.Wiring.ClearSpells308(SpellClearReason.Rest);
                        sword.Tick(Clock(20f + draw + flight + .05f));
                        r.Check(b8, sword.ArrowsInFlight == 0 && target.Hp == bowHp, "rest (Clear) takes back an arrow in the air: nothing lands");

                        // the last of the ink: the shot that empties the pool ends the form; its arrow still flies
                        Reset(); f.Cast(fire); f.Aim(target);
                        f.Ink.Restore(shotInk * .5f);
                        Bow(30f, true, false); var lastShot = Bow(30f + draw + .01f, false, true);
                        r.Check(b8, lastShot == BowStep308.Fire && f.Ink.Value == 0f && !sword.Active && sword.ArrowsInFlight == 1, "the shot that empties the ink ends the form; the arrow is already on its way");
                        sword.Tick(Clock(30f + draw + flight + .05f));
                    }
                    finally
                    {
                        SpellFixture308.Set(f.Wiring, "_motor", motorBefore);
                        f.Wiring.ConstructSpells(f.Registry, f.Finals, f.Fx); f.Aim(null); target.transform.position = home;
                    }
                }

                // ---- the other forms (the bow has its own block): they cast, strike with their element and lock spells (each at its own reach) ----
                foreach (int cell in cells.Skip(1).Where(c => c != cells[1]))
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
