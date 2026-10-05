using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-08 fixtures (SPEC-SPELL-120-308): the nine self buffs on the real wiring, PlayerVitals, InkPool and EnemyVitals.
    // Every cast comes from the resolver (f.Cast). The wiring's clock does not move inside one edit-mode call, so the time
    // after a buff is reached by ticking the effect with an explicit clock; hits arrive through EnemyStrike308 and the
    // wiring's own OnPlayerDamaged (Edit Mode never runs the OnEnable subscription, the foundation check does the same).
    // What stays for Play: real enemy AI striking in melee, the drawing layer really keeping its letter, the ink vessel on
    // the HUD while it is widened, every aura and cue, and all of the insight buff's legibility.
    public static partial class Spell120Checks308
    {
        static partial void FixturesWP08(Report r)
        {
            // grid cells of the nine self buffs (no glyph is named): eo medial with the nieun (3rd) or mieum (4th) final
            int[] cells = { 9, 33, 34, 57, 58, 81, 82, 105, 106 };
            if (!r.Wants(new string(cells.Select(SpellGrammar308.LetterAt).ToArray()))) return;
            string[] ids =
            {
                BurstHealEffect308.HandlerId, EmpowerEffect308.HandlerId, SteadfastEffect308.HandlerId, SwiftEffect308.HandlerId, ChainDiscountEffect308.HandlerId,
                InkReserveEffect308.HandlerId, ReflectEffect308.HandlerId, SlowbackEffect308.HandlerId, InsightEffect308.HandlerId,
            };
            var effects = SpellEffectInstaller308.CreateAll();
            r.Check("W08", ids.All(id => effects.Count(e => e.Id == id) == 1), "the nine self-buff handlers are registered, each exactly once (SpellEffectInstaller308.WP08)");
            using (var f = NewFixture(r, effects, FoundationSheets.Concat(new[] { "Rules308_WP08" }).ToArray()))
            {
                char Glyph(string handler)
                {
                    for (int i = 1; i <= SpellGrammar308.Count; i++)
                    {
                        char letter = SpellGrammar308.LetterAt(i);
                        if (f.Resolver.TryRow(letter, out var found) && found.Feature == SpellLegacyFeature.None && found.Handler == handler) return letter;
                    }
                    return default;
                }
                SpellRow RowOf(char letter) => f.Resolver.TryRow(letter, out var found) ? found : null;
                char[] glyphs = ids.Select(Glyph).ToArray();
                if (!r.Check("W08", glyphs.All(g => g != default(char)) && glyphs.OrderBy(g => g).SequenceEqual(cells.Select(SpellGrammar308.LetterAt).OrderBy(g => g)),
                    "the nine rows of Rules308_WP08 are in the fixture table, one per handler, on the nine buff cells")) return;
                char healGlyph = glyphs[0], empowerGlyph = glyphs[1], steadfastGlyph = glyphs[2], swiftGlyph = glyphs[3], chainGlyph = glyphs[4],
                    reserveGlyph = glyphs[5], reflectGlyph = glyphs[6], slowGlyph = glyphs[7], insightGlyph = glyphs[8];
                // cells 1 / 25 = the wood and fire single attacks, 7 = the wood parry (existing glyphs: hooks reach legacy rows too)
                char attack = SpellGrammar308.LetterAt(1), fireAttack = SpellGrammar308.LetterAt(25), parry = SpellGrammar308.LetterAt(7);
                var host = (ISpellCastHost)f.Wiring;
                float cost = f.Config.SpellInkCost, t0 = Time.time, ink;
                SpellTickContext At(float now, float delta = 0f) => new SpellTickContext(now, delta, host, f.Fx);
                void Reset() { f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); }
                float PlayerHp() => f.Player.Hp01 * f.Player.MaxHp;
                // an enemy strike as the game delivers it, then the wiring's own reaction to the damage
                void Strike(EnemyVitals from, IncomingDamageKind kind)
                {
                    if (EnemyStrike308.Deliver(f.Player, from, 5f, kind)) SpellFixture308.Call(f.Wiring, "OnPlayerDamaged", 5f);
                }

                // ---- gate, cost, presentation request: the same for all nine ----
                Reset(); f.Cast(healGlyph);
                r.Check("W08", f.Misfires.Count == 1 && f.Misfires[0].Value == SpellResolveStatus.Locked && f.Accepted.Count == 0, "without the final unlocked a self buff is the Locked misfire");
                f.Finals.Open.Add(SpellFinal.Nieun); f.Finals.Open.Add(SpellFinal.Mieum);
                bool allCast = true;
                foreach (char glyph in glyphs)
                {
                    Reset(); float inkAmount = f.Ink.Value * f.Ink.TotalCapacity; int begun = f.Fx.Begun; f.Cast(glyph);
                    var effect = (SelfBuffEffect308)f.Registry.Find(RowOf(glyph).Handler);
                    float seconds = RowOf(glyph).F("duration", 0f);
                    allCast &= f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Buff && f.Accepted[0].Power == 0f &&
                        Near(inkAmount - f.Ink.Value * f.Ink.TotalCapacity, cost) && f.Plans.Count == 0 && f.PendingCount == 0 && f.Fx.Begun == begun + 1 &&
                        effect.Active(t0 + seconds * .5f) && !effect.Active(t0 + seconds);
                }
                r.Check("W08", allCast, "each of the nine: one accepted Buff cast for one spell cost, no hit scheduled, one Aura request, active for its row's duration");
                Reset();
                r.Check("W08", ids.All(id => !((SelfBuffEffect308)f.Registry.Find(id)).Active(t0)) && f.Ink.TemporaryCapacity == 1f, "a clear (rest, death, scene leave) ends all nine and leaves no ink coefficient behind");

                // ---- burst heal ----
                var healRow = RowOf(healGlyph); var healFx = (BurstHealEffect308)f.Registry.Find(BurstHealEffect308.HandlerId);
                float healSeconds = healRow.F("duration", 0f), healTotal = healRow.F("heal.hp01", 0f);
                Reset(); f.Player.TakeDamage(f.Player.MaxHp * .6f);
                float hp01 = f.Player.Hp01; f.Cast(healGlyph);
                r.Check("건-2", f.Accepted.Count == 1 && Near(f.Player.Hp01, hp01), "the cast itself heals nothing yet: the share is paid over the window");
                healFx.Tick(At(t0 + healSeconds * .5f));
                r.Check("건-2", Near(f.Player.Hp01, hp01 + healTotal * .5f), "half-way through the window half of the share is healed (" + f.Player.Hp01.ToString("0.###") + ")");
                healFx.Tick(At(t0 + healSeconds + 1f));
                float healed = f.Player.Hp01;
                healFx.Tick(At(t0 + healSeconds + 2f));
                r.Check("건-2", Near(healed, hp01 + healTotal) && f.Player.Hp01 == healed, "by the end the whole share of max HP is healed, and nothing after it");
                r.Check("건-1", healFx.Active(t0 + healSeconds * .5f) && !healFx.Active(t0 + healSeconds), "the buff is over after its short duration (" + healSeconds + " s)");
                Reset(); f.Player.TakeDamage(f.Player.MaxHp * 2f); ink = f.Ink.Value; f.Cast(healGlyph);
                healFx.Tick(At(t0 + healSeconds));
                r.Check("건-2", f.Player.Hp01 == 0f && f.Accepted.Count == 0 && f.Misfires.Count == 0 && f.Ink.Value == ink, "a dead player: the cast is refused (no ink, no misfire) and nothing revives");

                // ---- empower ----
                float empowerMul = RowOf(empowerGlyph).F("empower.mul", 0f);
                Reset(); f.Aim(f.Inside[0]); f.Cast(attack);
                float basePower = f.Plans[0].Hits[0].Power, baseFlight = f.Plans[0].Hits[0].ImpactTime - Time.time;
                Reset(); f.Aim(f.Inside[0]); f.Cast(empowerGlyph); f.Cast(parry); f.Cast(attack); f.Cast(attack);
                var attackPlans = f.Plans.Where(p => p.Cast.Letter == attack).ToList();
                r.Check("넌-1", attackPlans.Count == 2 && Near(attackPlans[1].Hits[0].Power, basePower) && f.Accepted.Count == 4,
                    "one attack only: a parry in between does not use the buff, the second attack is back to its own power");
                r.Check("넌-2", attackPlans.Count == 2 && Near(attackPlans[0].Hits[0].Power, basePower * empowerMul) && Near(attackPlans[0].Hits[0].ImpactTime - Time.time, baseFlight),
                    "the next attack's planned hit carries power x " + empowerMul + "; its flight time is unchanged");
                Reset(); f.Aim(f.Inside[0]); f.Cast(empowerGlyph); f.Ink.Restore(cost * .5f); f.Cast(attack);
                int refusedAttacks = f.Accepted.Count(c => c.Letter == attack);
                f.Ink.Restore(); f.Cast(attack);
                attackPlans = f.Plans.Where(p => p.Cast.Letter == attack).ToList();
                r.Check("넌-1", refusedAttacks == 0 && attackPlans.Count == 1 && Near(attackPlans[0].Hits[0].Power, basePower * empowerMul),
                    "an attack refused for ink does not use the buff: the next accepted attack is the amplified one");

                // ---- swift ----
                float swiftMul = RowOf(swiftGlyph).F("speed.mul", 0f);
                Reset(); f.Aim(f.Inside[0]); f.Cast(swiftGlyph); f.Cast(attack);
                var swiftHit = f.Plans.Last(p => p.Cast.Letter == attack).Hits[0];
                r.Check("선-1", baseFlight > 0f && swiftMul > 1f && Near(swiftHit.ImpactTime - Time.time, baseFlight / swiftMul) && Near(swiftHit.Power, basePower),
                    "an attack while the buff runs lands after flight / " + swiftMul + " with the same power");

                // ---- steadfast ----
                float steadfastSeconds = RowOf(steadfastGlyph).F("duration", 0f);
                Reset(); float hp = PlayerHp(); Strike(f.Inside[1], IncomingDamageKind.Melee);
                float lossWithout = hp - PlayerHp(); bool interrupts = true;
                f.Registry.OnPlayerHit(f.Player.LastHit, At(t0), ref interrupts); bool without = interrupts;
                Reset(); f.Cast(steadfastGlyph); ink = f.Ink.Value; hp = PlayerHp(); Strike(f.Inside[1], IncomingDamageKind.Melee);
                float lossWith = hp - PlayerHp();
                interrupts = true; f.Registry.OnPlayerHit(f.Player.LastHit, At(t0 + steadfastSeconds * .5f), ref interrupts); bool during = interrupts;
                interrupts = true; f.Registry.OnPlayerHit(f.Player.LastHit, At(t0 + steadfastSeconds), ref interrupts); bool afterEnd = interrupts;
                r.Check("넘-1", without && !during && afterEnd, "a hit asks for the letter to be dropped before and after the buff, and not while it runs");
                r.Check("넘-2", Near(lossWithout, 5f) && Near(lossWith, 5f) && f.Ink.Value == ink, "the same strike costs the same HP with and without the buff, and gives no ink");

                // ---- chain discount ----
                float discount = RowOf(chainGlyph).F("chain.discount", 0f);
                Reset(); f.Aim(f.Inside[0]);
                ink = f.Ink.Value; f.Cast(attack); float plainFirst = ink - f.Ink.Value;
                ink = f.Ink.Value; f.Cast(fireAttack); float plainSecond = ink - f.Ink.Value;
                Reset(); f.Aim(f.Inside[0]);
                ink = f.Ink.Value; f.Cast(chainGlyph); float paidBuff = ink - f.Ink.Value;
                ink = f.Ink.Value; f.Cast(attack); float paidOther = ink - f.Ink.Value;          // wood right after the water buff
                ink = f.Ink.Value; f.Cast(attack); float paidSame = ink - f.Ink.Value;           // wood after wood
                ink = f.Ink.Value; f.Cast(fireAttack); float paidFire = ink - f.Ink.Value;       // fire after wood
                r.Check("언-1", Near(plainFirst, cost) && Near(plainSecond, cost) && Near(paidBuff, cost) && Near(paidSame, cost) && f.Accepted.Count == 4,
                    "full cost without the buff, for the buff's own cast, and for a cast of the same element as the one before");
                r.Check("언-2", discount > 0f && Near(paidOther, cost * (1f - discount)) && Near(paidFire, cost * (1f - discount)),
                    "a cast whose element differs from the previous accepted cast spends cost x (1 - " + discount + ")");

                // ---- ink reserve ----
                var reserveRow = RowOf(reserveGlyph); var reserveFx = (InkReserveEffect308)f.Registry.Find(InkReserveEffect308.HandlerId);
                float capacity = reserveRow.F("reserve.capacity", 0f), decay = reserveRow.F("reserve.decay", 0f), reserveSeconds = reserveRow.F("duration", 0f);
                Reset(); float amount = f.Ink.Value * f.Ink.TotalCapacity; f.Cast(reserveGlyph);
                r.Check("엄-1", f.Accepted.Count == 1 && Near(f.Ink.TemporaryCapacity, capacity) && capacity > 1f && f.Ink.CapacityMultiplier == 1f &&
                    Near(f.Ink.Value * f.Ink.TotalCapacity, amount - cost), "the pool is widened by the row's coefficient (equipment coefficient untouched); the amount of ink only fell by the cast's cost");
                f.Ink.Gain(10f);
                r.Check("엄-1", Near(f.Ink.Value, 1f) && Near(f.Ink.Value * f.Ink.TotalCapacity, capacity), "filled, the widened pool holds more than the normal maximum");
                reserveFx.Tick(At(t0 + reserveSeconds * .5f, 1f));
                r.Check("엄-1", Near(f.Ink.TemporaryCapacity, capacity) && reserveFx.Reserve == 0f, "it stays widened while the buff runs");
                reserveFx.Tick(At(t0 + reserveSeconds));
                r.Check("엄-2", Near(reserveFx.Reserve, capacity - 1f) && Near(f.Ink.Value * f.Ink.TotalCapacity, capacity),
                    "when the buff ends the ink above the normal maximum is the reserve; nothing is lost at that moment (" + reserveFx.Reserve.ToString("0.###") + ")");
                reserveFx.Tick(At(t0 + reserveSeconds + 5f, 5f));
                r.Check("엄-3", Near(reserveFx.Reserve, capacity - 1f - decay * 5f) && Near(f.Ink.Value * f.Ink.TotalCapacity, 1f + reserveFx.Reserve),
                    "five seconds later the reserve has shrunk by 5 x the row's rate, and the pool holds the normal maximum plus what is left");
                float reserveBefore = reserveFx.Reserve; bool spentOk = f.Ink.TrySpend(.05f); reserveFx.Tick(At(t0 + reserveSeconds + 5f));
                r.Check("엄-3", spentOk && Near(reserveFx.Reserve, reserveBefore - .05f) && Near(f.Ink.Value, 1f), "ink spent meanwhile comes out of the reserve first");
                reserveFx.Tick(At(t0 + reserveSeconds + 1000f, 1000f));
                r.Check("엄-3", reserveFx.Reserve == 0f && f.Ink.TemporaryCapacity == 1f && Near(f.Ink.Value, 1f), "the reserve runs out: the normal pool again, full");
                f.Cast(reserveGlyph); f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                r.Check("엄-3", f.Ink.TemporaryCapacity == 1f && reserveFx.Reserve == 0f, "death puts the coefficient back to 1 at once (nothing is saved)");

                // ---- reflect ----
                var reflectFx = (ReflectEffect308)f.Registry.Find(ReflectEffect308.HandlerId);
                float reflectPower = RowOf(reflectGlyph).F("reflect.power", 0f), reflectSeconds = RowOf(reflectGlyph).F("duration", 0f);
                var attacker = f.Inside[1]; var bystander = f.Inside[0];
                Reset(); f.Cast(reflectGlyph); f.ClearLog();
                float enemyHp = attacker.Hp, otherHp = bystander.Hp, groggy = attacker.Groggy.Value01; int mask = attacker.WeakPointElementMask;
                hp = PlayerHp(); ink = f.Ink.Value;
                Strike(attacker, IncomingDamageKind.Ranged); Strike(attacker, IncomingDamageKind.ElementalRanged); Strike(attacker, IncomingDamageKind.Unspecified); Strike(null, IncomingDamageKind.Melee);
                bool flag = true; f.Registry.OnPlayerHit(new PlayerHitInfo(attacker, 5f, IncomingDamageKind.Melee), At(t0 + reflectSeconds), ref flag);
                r.Check("먼-1", reflectFx.PendingCount == 0 && attacker.Hp == enemyHp, "ranged and unspecified hits, a melee hit without an attacker, and a melee hit after the buff: no answer");
                Strike(attacker, IncomingDamageKind.Melee);
                r.Check("먼-2", reflectFx.PendingCount == 1 && attacker.Hp == enemyHp, "a melee strike: one answer is recorded and nothing happens inside the enemy's own strike call");
                reflectFx.Tick(At(Time.time));
                r.Check("먼-1", Near(enemyHp - attacker.Hp, reflectPower) && bystander.Hp == otherHp && f.Boss.Hp == f.Boss.MaxHp, "on the next tick the attacker, and only the attacker, takes the row's damage");
                r.Check("먼-2", f.Hits.Count == 1 && f.Hits[0].Target == attacker && f.Hits[0].Attack.Source == DamageSource.Retaliation && reflectFx.PendingCount == 0 &&
                    attacker.Groggy.Value01 == groggy && attacker.WeakPointElementMask == mask && Near(hp - PlayerHp(), 25f) && f.Ink.Value == ink,
                    "exactly one Retaliation hit: no groggy, no five-element credit; the player lost every strike's HP and got no ink or HP back");
                Strike(attacker, IncomingDamageKind.ElementalMelee); reflectFx.Tick(At(Time.time)); reflectFx.Tick(At(Time.time));
                r.Check("먼-2", Near(enemyHp - attacker.Hp, reflectPower * 2f) && f.Hits.Count == 2, "an elemental melee strike is answered the same way, once (a second tick repeats nothing)");

                // ---- slowback ----
                var slowRow = RowOf(slowGlyph);
                float slowSpeed = slowRow.F("slow.speed", 0f), slowSeconds = slowRow.F("slow.duration", 0f), slowBuff = slowRow.F("duration", 0f);
                Reset(); f.Cast(slowGlyph); hp = PlayerHp(); ink = f.Ink.Value; float struckAt = Time.time;
                Strike(attacker, IncomingDamageKind.Ranged); Strike(null, IncomingDamageKind.Melee);
                flag = true; f.Registry.OnPlayerHit(new PlayerHitInfo(bystander, 5f, IncomingDamageKind.Melee), At(t0 + slowBuff), ref flag);
                bool untouched = attacker.Control.MovementScale(struckAt) == 1f && bystander.Control.MovementScale(struckAt) == 1f;
                Strike(attacker, IncomingDamageKind.Melee); Strike(f.Boss, IncomingDamageKind.ElementalMelee);
                r.Check("멈-1", untouched && Near(attacker.Control.MovementScale(struckAt), slowSpeed) && bystander.Control.MovementScale(struckAt) == 1f && !attacker.Control.BlocksActions(struckAt),
                    "only the enemy that struck in melee while the buff runs is slowed; its actions are not blocked");
                r.Check("멈-1", f.Boss.IsBoss && Near(f.Boss.Control.MovementScale(struckAt), slowSpeed) && !f.Boss.Control.BlocksActions(struckAt), "a boss that strikes in melee is slowed the same way (movement only)");
                r.Check("멈-2", slowSpeed < 1f && Near(attacker.Control.MovementScale(struckAt + slowSeconds - .01f), slowSpeed) && attacker.Control.MovementScale(struckAt + slowSeconds) == 1f,
                    "movement scale " + slowSpeed + " for " + slowSeconds + " s, then full speed again");
                r.Check("멈-2", Near(hp - PlayerHp(), 20f) && f.Ink.Value == ink && attacker.Hp == attacker.MaxHp, "the slow is all there is: no damage to the enemy, nothing given to the player");

                // ---- insight: state and signal only ----
                Reset(); f.Aim(f.Inside[0]); f.Cast(insightGlyph); f.Cast(attack); Strike(attacker, IncomingDamageKind.Melee);
                var insightHit = f.Plans.Last(p => p.Cast.Letter == attack).Hits[0];
                r.Check("W08", Near(insightHit.Power, basePower) && Near(insightHit.ImpactTime - Time.time, baseFlight) && attacker.Hp == attacker.MaxHp && attacker.Control.MovementScale(Time.time) == 1f,
                    "the insight buff changes no number of the fight (attack power, flight time, enemy state); what it makes readable is presentation (Play)");
            }
        }
    }
}
