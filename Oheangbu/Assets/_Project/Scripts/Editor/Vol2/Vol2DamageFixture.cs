using System;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static Oheangbu.EditorTools.Vol2.Vol2DefenceFixtureSupport;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 2 (D308-149, design section 5) edit-mode fixture of the vol.2 damage entry and the unit boundary. In memory only.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2DamageFixture Run "all"
    ///   D1  the discriminating pair: with the window open and a counter-element source the OLD entry loses 10 x 1.5 x 1.25 = 18.75, the vol.2 entry exactly 10
    ///   D2  none of the old state is touched (five-element mask, completion, legacy DamageMultiplier stays for the riposte)
    ///   D3  the target's own armour is applied ONCE: defence 0.5 -> 5 (not 2.5); a strip (shred) lowers it; the old entry gives the same 5
    ///   D4  the one-HP floor (갈무리) and a lethal hit without it; signals HpChanged / DamageResolved / Died
    ///   D5  the terms: D equals the product of its terms (window, counter, spell, structure x force cap, equipment cap) and are kept in LastVol2Hit
    ///   D6  B at the damage frame: the same call before / after the window opens
    ///   D7  the unit boundary: HP shift keeps the floor at one real HP; ink points <-> pool fractions with a capacity multiplier
    ///   D8  Element <-> Body mapping is explicit and total
    ///   D9  Vol2Attack: NoCredit set, ids from the one AttackProvenance counter
    /// Not covered (Play): the real executors that call the entry (slices 3 to 8).</summary>
    public static class Vol2DamageFixture
    {
        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-damage-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-damage-fixture: editor busy (play / compile / import)";
            var r = new Vol2FixtureReport();
            var made = new List<Object>();
            try { RunAll(r, made); } catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            return JsonUtility.ToJson(r, true);
        }

        static void RunAll(Vol2FixtureReport r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "D0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var numbers = new DefenceNumbersV3Dto(book.Numbers);
            var owner = new GameObject("Vol2Damage_Owner") { hideFlags = HideFlags.HideAndDontSave }; made.Add(owner);

            // D1: old entry vs vol.2 entry, window open, counter source
            {
                var oldE = NewEnemy(made, "D1_old"); var newE = NewEnemy(made, "D1_new");
                foreach (var e in new[] { oldE, newE }) { e.SetAttackElementSource308(new Vol2FakeAttackElement { Element = Element.Fire }); e.OpenWeakPointFor(2f); }
                var attack = new AttackProvenance(AttackProvenance.Create(owner, DamageSource.PlayerDirect, Element.Water).AttackId, owner, DamageSource.PlayerDirect, Element.Water);
                float oldBefore = oldE.Hp; oldE.TakeDamage(10f, attack);
                float oldLoss = oldBefore - oldE.Hp;
                int resolved = 0; newE.DamageResolved += _ => resolved++;
                float newBefore = newE.Hp;
                var result = newE.TakeResolvedVol2Damage(10f, new AttackProvenance(attack.AttackId + 100000, owner, DamageSource.PlayerDirect, Element.Water));
                float newLoss = newBefore - newE.Hp;
                Check(r, "D1", Near(oldLoss, 18.75) && Near(newLoss, 10.0) && resolved == 1 && Near(result.AppliedDamage, 10.0) && Near(result.CompletionBonus, 0.0),
                    "window open + counter source: old entry lost " + oldLoss + " (10 x 1.5 x 1.25), vol.2 entry lost " + newLoss + " (exactly the 10 given), one DamageResolved");
                Check(r, "D2", newE.DamageMultiplier == 1.5f && newE.WeakPointElementMask == 0 && !newE.CompletionAwarded && newE.WeakPointActive,
                    "vol.2 entry left the old state alone: DamageMultiplier " + newE.DamageMultiplier + " (the riposte still reads it), element mask " + newE.WeakPointElementMask + ", completion " + newE.CompletionAwarded);
                // a PlayerDirect hit with an element and an instigator would feed the five-element mask in the old entry: five different elements complete it
                var mask = NewEnemy(made, "D2_mask"); mask.OpenWeakPointFor(2f);
                for (int i = 0; i < 5; i++)
                    mask.TakeResolvedVol2Damage(1f, new AttackProvenance(9000 + i, owner, DamageSource.PlayerDirect, (Element)i));
                Check(r, "D2b", mask.WeakPointElementMask == 0 && !mask.CompletionAwarded && Near(mask.Hp, 55.0), "five different elements through the vol.2 entry: mask " + mask.WeakPointElementMask + ", no completion bonus, HP " + mask.Hp + " (5 x 1)");
            }

            // D3: armour once, strip lowers it, old entry agrees
            {
                var armoured = NewEnemy(made, "D3_a", 60f, false, 0.5f); var oldA = NewEnemy(made, "D3_old", 60f, false, 0.5f);
                var attack = new AttackProvenance(70001, owner, DamageSource.PlayerDirect, Element.Wood);
                float b = armoured.Hp; armoured.TakeResolvedVol2Damage(10f, attack); float loss = b - armoured.Hp;
                float ob = oldA.Hp; oldA.TakeDamage(10f, attack); float oldLoss = ob - oldA.Hp;
                Check(r, "D3a", Near(loss, 5.0) && Near(oldLoss, 5.0), "defence 0.5: vol.2 entry lost " + loss + ", old entry " + oldLoss + " (the armour is applied once: 5, not 2.5)");
                armoured.Modifiers.ShredDefence(Time.time + 100f, 0.3f);
                b = armoured.Hp; armoured.TakeResolvedVol2Damage(10f, new AttackProvenance(70002, owner, DamageSource.PlayerDirect, Element.Wood)); loss = b - armoured.Hp;
                Check(r, "D3b", Near(loss, 8.0), "strip (shred 0.3) leaves defence 0.2: lost " + loss + " (8)");
                var brand = NewEnemy(made, "D3_brand"); brand.Modifiers.Brand(Time.time + 100f, 1.5f);
                b = brand.Hp; brand.TakeResolvedVol2Damage(10f, new AttackProvenance(70003, owner, DamageSource.PlayerDirect, Element.Wood)); loss = b - brand.Hp;
                Check(r, "D3c", Near(loss, 15.0), "a brand left on the target is its own state, applied by the same one place: lost " + loss + " (15)");
            }

            // D4: floor, lethal, signals
            {
                var f = NewEnemy(made, "D4_floor", 1.5f);
                int hpChanged = 0, resolved = 0, died = 0; f.HpChanged += () => hpChanged++; f.DamageResolved += _ => resolved++; f.Died += () => died++;
                var res1 = f.TakeResolvedVol2Damage(100f, new AttackProvenance(71001, owner, DamageSource.Harvest, null), true);
                Check(r, "D4a", Near(f.Hp, 1.0) && f.IsAlive && died == 0 && f.LastVol2Hit.Floored && Near(res1.AppliedDamage, 0.5) && hpChanged == 1 && resolved == 1,
                    "floor: HP 1.5 hit by 100 stays at " + f.Hp + ", alive, floored flag " + f.LastVol2Hit.Floored + ", applied " + res1.AppliedDamage);
                var res2 = f.TakeResolvedVol2Damage(100f, new AttackProvenance(71002, owner, DamageSource.Harvest, null), true);
                Check(r, "D4b", Near(f.Hp, 1.0) && f.IsAlive && Near(res2.AppliedDamage, 0.0) && !res2.Killed, "a second floored hit at HP 1 takes nothing and never kills");
                uint lifeBefore = f.LifeRevision;
                var res3 = f.TakeResolvedVol2Damage(100f, new AttackProvenance(71003, owner, DamageSource.PlayerDirect, Element.Fire));
                Check(r, "D4c", !f.IsAlive && res3.Killed && died == 1 && f.LifeRevision == lifeBefore + 1, "without the floor the same hit kills: Died once, LifeRevision " + lifeBefore + " -> " + f.LifeRevision);
                var dead = f.TakeResolvedVol2Damage(5f, new AttackProvenance(71004, owner, DamageSource.PlayerDirect, Element.Fire));
                Check(r, "D4d", dead.Target == null && resolved == 3, "a dead target takes nothing and signals nothing more (3 DamageResolved so far: floor, floor at 1 HP, the kill)");
                var zero = NewEnemy(made, "D4_zero"); float hp0 = zero.Hp;
                zero.TakeResolvedVol2Damage(0f, default); zero.TakeResolvedVol2Damage(-3f, default); zero.TakeResolvedVol2Damage(float.NaN, default);
                Check(r, "D4e", zero.Hp == hp0, "zero / negative / NaN damage changes nothing");
            }

            // D5 + D6: the terms
            {
                var t = Vol2DamageResolver.Resolve(numbers, 1m, 1.4m, 1.15m, true, true, true, 10f);
                Check(r, "D5a", Near(t.C, 1.5m) && Near(t.K, 1.25m) && Near(t.B, 1.5m) && Near(t.D, 2.8125m) && Near(t.Product, t.D),
                    "spell, structure 1.4 x force 1.15 capped at 1.5, counter in the window: C=" + t.C + " K=" + t.K + " B=" + t.B + " D=" + t.D + " (product " + t.Product + ")");
                var s = Vol2DamageResolver.Resolve(numbers, 0.3m, 1m, 1m, false, false, true, 10f);
                Check(r, "D5b", Near(s.C, 1m) && Near(s.K, 1m) && Near(s.B, 1.5m) && Near(s.D, 0.45m) && Near(s.Product, s.D), "the bare swing (not a spell) has no C and no K but B at the frame: D=" + s.D);
                var e = Vol2DamageResolver.Resolve(numbers, 1m, 1m, 1m, false, true, false, 10f, 0.2m, 25m);
                Check(r, "D5c", Near(e.A, 10m) && Near(e.D, 1m * 1.2m * 1.10m) && Near(e.Product, e.D), "equipment capped at 10 percent, ink power 0.2: A=" + e.A + " D=" + e.D);
                var outside = Vol2DamageResolver.Resolve(numbers, 1m, 1m, 1m, true, true, false, 10f);
                Check(r, "D5d", Near(outside.K, 1m) && Near(outside.B, 1m) && Near(outside.D, 1m), "the counter K applies only inside the window: K=" + outside.K);
                var target = NewEnemy(made, "D5_target");
                target.TakeResolvedVol2Damage(t.RawHp, new AttackProvenance(72001, owner, DamageSource.PlayerDirect, Element.Fire), t);
                var rec = target.LastVol2Hit;
                Check(r, "D5e", rec.HasTerms && Near(rec.Terms.D, t.D) && Near(rec.RequestedHp, 28.125) && Near(rec.AppliedHp, 28.125) && rec.AttackId == 72001,
                    "LastVol2Hit keeps the terms and the amounts: " + rec);
                // D6: B judged at the damage frame
                var w = NewEnemy(made, "D6_w");
                var before = Vol2DamageResolver.Resolve(numbers, 1m, 1m, 1m, false, true, w.WeakPointActive, 10f);
                w.OpenWeakPointFor(2f);
                var after = Vol2DamageResolver.Resolve(numbers, 1m, 1m, 1m, false, true, w.WeakPointActive, 10f);
                Check(r, "D6", Near(before.B, 1m) && Near(after.B, 1.5m) && Near(after.D, before.D * 1.5m), "the same hit before the window opens B=" + before.B + ", after B=" + after.B + " (read from the target at that frame)");
            }

            // D7: units
            {
                Check(r, "D7a", Vol2Units.RuleHp(21f, 10f, 1m) == 3m && Vol2Units.RuleHp(1f, 10f, 1m) == 1m && Vol2Units.RuleHp(0f, 10f, 1m) == 0m && Vol2Units.RuleHp(11f, 1f, 1m) == 11m,
                    "rule HP keeps the floor at one real HP: 21 HP -> 3 units, 1 HP -> exactly the floor, 0 HP -> 0");
                var pool = new InkPool(null, null);
                var ink = new Vol2InkAccount(pool);
                bool spent = ink.TrySpend(26m);
                Check(r, "D7b", spent && Near(ink.Units, 74m, 0.01m) && Near(ink.CapacityUnits, 100m), "26 points from a full pool: " + ink.Units + " left of " + ink.CapacityUnits);
                pool.SetCapacityMultiplier(2f);
                Check(r, "D7c", Near(ink.Units, 148m, 0.05m) && Near(ink.CapacityUnits, 200m), "capacity x2 keeps the fill fraction: " + ink.Units + " of " + ink.CapacityUnits);
                bool spent2 = ink.TrySpend(26m);
                Check(r, "D7d", spent2 && Near(ink.Units, 122m, 0.05m), "26 points cost 26 points whatever the capacity: " + ink.Units);
                decimal got = ink.Gain(10m);
                Check(r, "D7e", Near(got, 10m, 0.05m) && Near(ink.Units, 132m, 0.05m), "gain 10: received " + got + ", now " + ink.Units);
                decimal taken = ink.SpendClamped(1000m);
                Check(r, "D7f", Near(taken, 132m, 0.05m) && ink.Units < 0.01m, "a debt larger than the pool takes what is there: " + taken);
                Check(r, "D7g", !ink.TrySpend(1m) && ink.Units < 0.01m, "an empty pool refuses a spend and keeps nothing taken");
                var bare = new Vol2InkAccount(null);
                Check(r, "D7h", !bare.Available && bare.TrySpend(50m) && bare.SpendClamped(50m) == 0m && bare.Gain(5m) == 0m, "no pool = free, nothing gained");
            }

            // D8: mapping
            {
                bool all = true;
                for (int i = 0; i < 5; i++) { var el = (Element)i; all &= Vol2Mapping.ToElement(Vol2Mapping.ToBody(el)) == el; }
                Check(r, "D8", all && Vol2Mapping.ToBody(Element.Metal) == Body.Metal && Vol2Mapping.ToBody((Element?)null) == null && Vol2Mapping.ToBody((Element?)Element.Water) == Body.Water,
                    "Element -> Body -> Element round trip for all five, null stays null");
            }

            // D9: the attack identity
            {
                long issuedBefore = AttackProvenance.Issued;
                var a = Vol2Attack.Create(owner, DamageSource.PlayerDirect, Element.Fire);
                Check(r, "D9", a.NoCredit && a.AttackId > issuedBefore && a.Source == DamageSource.PlayerDirect && a.Element == Element.Fire && !a.Melee, "NoCredit set, AttackId " + a.AttackId + " from the one counter");
            }
        }
    }
}
