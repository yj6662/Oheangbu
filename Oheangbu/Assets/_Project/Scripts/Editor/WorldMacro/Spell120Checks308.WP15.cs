using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-15 fixtures (SPEC-SPELL-120-308, D308-113): the two glyphs that give the enemy-side answers a real counter
    // (낭 = mod.guardbreak, 몽 = zone.blind) and the two enemy-side rules they lean on (the defence cap, the guard seam that
    // stays off on every shipped brain profile). Every cast goes through the resolver; effects are driven with an explicit
    // clock (Edit Mode has no frame loop). The guarding dummy is the WP-13 TEST target (SpellGuardTestTarget308).
    // The main world's brains losing the player in the cloud are H125's (Spell120Checks308.H125.cs).
    // What stays for Play: a field enemy walking to the last known place and searching, a real enemy that guards
    // in its Wait phase, the look of both glyphs (the fixtures use the null presenter).
    public static partial class Spell120Checks308
    {
        static readonly string[] WP15Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP11", "Rules308_WP15" };

        static partial void FixturesWP15(Report r)
        {
            // grid cells by index (no glyph is named): 30 fire single + ieung, 66 earth o (path) + ieung, 73 = the metal single
            // shot without a final (the plain hit a guard stops)
            char breaker = SpellGrammar308.LetterAt(30), blind = SpellGrammar308.LetterAt(66), plain = SpellGrammar308.LetterAt(73);
            if (!r.Wants(new string(new[] { breaker, blind }))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var breakFx = effects.OfType<GuardBreakEffect308>().FirstOrDefault(); var blindFx = effects.OfType<BlindZoneEffect308>().FirstOrDefault();
            if (!r.Check("W15", breakFx != null && blindFx != null, "the two WP-15 effects are registered")) return;

            const float Heavy = .9f;
            var profile = ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();
            try
            {
                SpellFixture308.Set(profile, "_defence", Heavy); SpellFixture308.Set(profile, "_maxHp", 1000f);
                using (var f = NewFixture(r, effects, WP15Sheets))
                {
                    var host = (ISpellCastHost)f.Wiring;
                    foreach (SpellFinal final in new[] { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung }) f.Finals.Open.Add(final);
                    float brush = f.Brush(), cost = f.Config.SpellInkCost;
                    Vector3 origin = SpellFixture308.Origin;
                    var everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                    var home = everyone.Select(e => e.transform.position).ToArray();
                    void Sync()
                    {
                        Physics.SyncTransforms();
                        var physics = f.Scene.GetPhysicsScene();
                        if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("The fixture scene needs its own physics scene.");
                        physics.Simulate(.001f);
                    }
                    void Reset()
                    {
                        for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = home[i];
                        f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                        f.RestoreAll(); f.Aim(null);
                        Sync();
                    }
                    void SendAway()
                    {
                        for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = origin + new Vector3(300f + i * 10f, 0f, 300f);
                        Sync();
                    }
                    void Tick(ISpellEffect effect, float clock) => effect.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx));
                    int Strikes(EnemyVitals attacker, int count, IncomingDamageKind kind)
                    {
                        int landed = 0;
                        for (int i = 0; i < count; i++) { f.Player.Restore(); if (EnemyStrike308.Deliver(f.Player, attacker, 1f, kind)) landed++; }
                        return landed;
                    }
                    float planned = 0f;
                    float PlainHit(EnemyVitals target)
                    {
                        f.ClearLog(); f.Ink.Restore(); f.Aim(target); f.Cast(plain);
                        planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : -1f;
                        f.Land();
                        return f.Hits.Count == 1 ? f.Hits[0].AppliedDamage : -1f;
                    }

                    // ---- 낭: the guard breaker against the guarding TEST dummy ----
                    if (r.Wants(breaker.ToString()))
                    {
                        Reset();
                        var dummy = f.Inside[0];
                        var guard = dummy.gameObject.AddComponent<SpellGuardTestTarget308>();
                        try
                        {
                            guard.Configure(true, 1f);
                            f.Resolver.TryRow(breaker, out var row);
                            float lockFor = row.F("guard.lock", 0f);
                            f.Aim(dummy); float hp = dummy.Hp; f.Cast(plain); f.Land();
                            r.Check("낭-1", dummy.Modifiers.Stance.Raised && dummy.Hp == hp && f.Hits.Count == 0, "TEST target: a guarding dummy stops the plain single shot");
                            f.ClearLog(); f.Ink.Restore(); f.Aim(dummy); float ink = f.Ink.Value; f.Cast(breaker);
                            bool told = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && dummy.Modifiers.Stance.Passes(f.Plans[0].Hits[0].AttackId);
                            bool oneCost = f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost);
                            f.Land(); float now = Time.time;
                            var stance = dummy.Modifiers.Stance;
                            r.Check("낭-1", told && oneCost && Near(hp - dummy.Hp, row.BasePower * brush) && f.Hits.Count == 1 && breakFx.HitsOnTheWay == 0,
                                "the breaker costs one spell, goes through the raised guard and lands whole  [" + (hp - dummy.Hp) + " of " + row.BasePower * brush + "]");
                            r.Check("낭-1", lockFor > 0f && !stance.Raised && stance.Broken && Near(stance.BreakUntil - now, lockFor, .05f),
                                "on impact the guard drops and is broken for guard.lock seconds  [" + (stance.BreakUntil - now) + " of " + lockFor + "]");
                            guard.Apply();
                            r.Check("낭-2", !stance.Raised && !stance.Raise(1f, now) && !stance.CanRaise(now + lockFor * .5f),
                                "while broken the dummy (or a brain) cannot raise the guard again");
                            f.ClearLog(); hp = dummy.Hp; f.Aim(dummy); f.Cast(plain); f.Land();
                            r.Check("낭-2", f.Hits.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && hp - dummy.Hp > 0f && Near(hp - dummy.Hp, f.Plans[0].Hits[0].Power),
                                "a plain shot during the break lands whole (nothing to stop it)");
                            float firstUntil = stance.BreakUntil;
                            f.ClearLog(); f.Ink.Restore(); f.Aim(dummy); f.Cast(breaker); f.Land();
                            r.Check("낭-2", stance.Broken && stance.BreakUntil >= firstUntil - .001f && Near(stance.BreakUntil - Time.time, lockFor, .05f),
                                "a second break only moves the time (no stacking: still guard.lock from the latest hit)");
                            float until = stance.BreakUntil;
                            r.Check("낭-2", stance.CanRaise(until + .01f) && stance.Raise(1f, until + .01f) && stance.Raised && !stance.Broken,
                                "after guard.lock the guard may be raised again");
                            r.Check("낭-3", everyone.All(e => e.Groggy.Value01 == 0f && !e.WeakPointActive), "no groggy and no weak point from the break (groggy sources stay three)");
                            // a broken guard ends with the caster's fight: the handler's clear ends the break it set
                            stance.Lower(); f.ClearLog(); f.Ink.Restore(); f.Aim(dummy); f.Cast(breaker); f.Land();
                            bool brokenAgain = stance.Broken;
                            f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                            r.Check("낭-2", brokenAgain && !stance.Broken, "the caster's death, rest or leave ends the break it set");
                            guard.Configure(false, 1f);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(guard); }
                        Reset();
                        r.Check("낭-1", f.Inside[0].Modifiers.IsEmpty, "a new life starts with no guard, no break and no entry");
                    }

                    // ---- 몽: the blinding cloud ----
                    if (r.Wants(blind.ToString()))
                    {
                        Reset(); SendAway();
                        f.Resolver.TryRow(blind, out var row);
                        float radius = row.F("haze.radius", 0f), life = row.F("haze.life", 0f), miss = row.F("haze.miss", 0f);
                        float castAt = Time.time; float ink = f.Ink.Value; f.Cast(blind);
                        bool planned2 = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == row.AreaShape;
                        r.Check("몽-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && planned2 && blindFx.Zones.CountOf(BlindZoneEffect308.HandlerId) == 1,
                            "the cast is the base glyph's own path (one spell cost) and leaves one cloud");
                        if (planned2)
                        {
                            var area = f.Plans[0].Area;
                            Vector3 centre = area.Point + area.Direction * (area.Length * .5f);
                            var zone = blindFx.Zones.Zones.First(z => z.Owner == BlindZoneEffect308.HandlerId);
                            r.Check("몽-1", (zone.Centre - centre).magnitude < .01f && Near(zone.Radius, radius) && Near(zone.Until - zone.From, life) && Near(zone.From - castAt, area.Delay),
                                "the cloud stands at the middle of the path, haze.radius wide, for haze.life seconds after the path forms");
                            var inside = f.Outside[1]; var outside = f.Outside[2];
                            inside.transform.position = centre; outside.transform.position = centre + Vector3.right * (radius + 2f); Sync();
                            float during = zone.From + .1f;
                            Tick(blindFx, during);
                            int strikes = 20, expected = strikes - Mathf.RoundToInt(strikes * miss);
                            r.Check("몽-2", inside.Modifiers.Veil.Blinded(during) && !outside.Modifiers.Veil.Blinded(during) && blindFx.BlindedEnemies == 1,
                                "the enemy inside the cloud is blind (its brain loses the player); the one outside sees");
                            r.Check("몽-2", miss > 0f && Strikes(inside, strikes, IncomingDamageKind.Melee) == expected && Strikes(outside, strikes, IncomingDamageKind.Melee) == strikes,
                                "strikes of the blind enemy miss haze.miss of the time at the doorway; the enemy outside never misses");
                            inside.transform.position = centre + Vector3.right * (radius + 2f); Sync();
                            Tick(blindFx, during + .1f);
                            r.Check("몽-3", !inside.Modifiers.Veil.Blinded(during + .1f) && Strikes(inside, strikes, IncomingDamageKind.Melee) == strikes && blindFx.BlindedEnemies == 0 && inside.Modifiers.IsEmpty,
                                "an enemy that walked out sees and strikes true again at once");
                            inside.transform.position = centre; Sync();
                            Tick(blindFx, zone.Until + .1f);
                            r.Check("몽-3", blindFx.Zones.CountOf(BlindZoneEffect308.HandlerId) == 0 && !inside.Modifiers.Veil.Blinded(zone.Until + .1f) && inside.Modifiers.IsEmpty,
                                "after haze.life the cloud is gone and nobody is blind");
                            r.Check("몽-3", inside.Hp == inside.MaxHp && outside.Hp == outside.MaxHp && everyone.All(e => e.Groggy.Value01 == 0f && !e.WeakPointActive),
                                "the cloud deals no damage of its own, no groggy, no weak point");
                        }
                    }

                    // ---- the defence cap (no enemy is immune) ----
                    {
                        Reset();
                        var heavy = f.Inside[2];
                        heavy.ConfigureProfile(profile); heavy.Restore();
                        float cap = f.Config.DefenceCap;
                        float open = PlainHit(f.Inside[1]); float openPlanned = planned;
                        r.Check("방어-2", openPlanned > 0f && Near(open, openPlanned), "an enemy without defence takes the planned power, as before WP-15");
                        float shut = PlainHit(heavy);
                        r.Check("방어-1", cap > 0f && cap < Heavy && heavy.Defence == Heavy && Near(shut, planned * (1f - cap)),
                            "a TEST enemy with defence " + Heavy + " loses only the cap (" + cap + ") of a plain hit  [" + shut + " of " + planned + "]");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }

            // ---- a TEST-only row (Gate test in its sheet; D308-13 Q5: an enemy / world dependent glyph is the spell rule plus a TEST
            // target). A normal save that earned its final opens the other rows of that final, never this one. The rock row, the thrust
            // row and then the blind row held this case until the main world gave each its consumer (D308-125 R125, S125, H125); the
            // anchor domain (grid cell 94, metal u + mieum: no enemy pushes or pulls the player yet) holds it now, beside the spring
            // domain of the same final (cell 118, water u + mieum).
            char anchor = SpellGrammar308.LetterAt(94), spring = SpellGrammar308.LetterAt(118);
            if (r.Wants(anchor.ToString()))
            {
                using (var g = NewFixture(r, effects, WP15Sheets.Append("Rules308_WP17")))
                {
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    g.Resolver.TryRow(anchor, out var anchorRow); g.Resolver.TryRow(spring, out var springRow);   // the spring: a row of the same final that the main game does open
                    g.Finals.Proven.Add(SpellFinal.Mieum);                          // the main-game proof, no TEST unlock
                    g.Aim(g.Inside[0]); g.Cast(spring);
                    bool mainOpens = springRow != null && springRow.Gate == SpellGateMode.Final && springRow.Final == anchorRow?.Final && g.Accepted.Count == 1 && g.Misfires.Count == 0;
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled); g.RestoreAll(); g.Aim(g.Inside[0]);
                    float ink = g.Ink.Value; g.Cast(anchor);
                    r.Check("A5", anchorRow != null && anchorRow.Gate == SpellGateMode.Test && mainOpens && g.Accepted.Count == 0 && g.Misfires.Count == 1 &&
                        g.Misfires[0].Value == SpellResolveStatus.Locked && Near(ink - g.Ink.Value, g.Config.MisfireInkCost) && g.PendingCount == 0,
                        "a normal save that proved the final: the other row of that final casts, the TEST-only anchor row is a Locked misfire (misfire ink, nothing scheduled)");
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    g.Finals.Proven.Clear(); g.Finals.Open.Add(SpellFinal.Mieum);   // the TEST unlock of the isolated store
                    g.RestoreAll(); g.Aim(g.Inside[0]); g.Cast(anchor);
                    r.Check("A5", g.Accepted.Count == 1 && g.Misfires.Count == 0, "the TEST unlock of its final opens the anchor row");
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    g.Finals.Proven.Clear(); g.Finals.Open.Clear();
                }
            }

            // ---- the brain's guard stays off until a profile opts in ----
            var guids = AssetDatabase.FindAssets("t:" + nameof(EnemyBrainProfile308));
            var raising = guids.Select(AssetDatabase.GUIDToAssetPath).Where(p => { var b = AssetDatabase.LoadAssetAtPath<EnemyBrainProfile308>(p); return b != null && b.GuardShare > 0f; }).ToArray();
            var fresh = ScriptableObject.CreateInstance<EnemyBrainProfile308>();
            bool freshOff = fresh.GuardShare == 0f; UnityEngine.Object.DestroyImmediate(fresh);
            r.Check("가드-1", freshOff && raising.Length == 0,
                "GuardShare is 0 on a new brain profile and on every shipped one (" + guids.Length + "): no field enemy guards until its profile says so" + (raising.Length > 0 ? " | raising: " + string.Join(", ", raising) : ""));

            // ---- #308 WP-16 (D308-114): the guard opts in per content (session clones), never on the shipped asset ----
            foreach (var path in AssetDatabase.FindAssets("t:" + nameof(WorldMacroPlaytestSO)).Select(AssetDatabase.GUIDToAssetPath))
            {
                var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(path);
                if (content == null || content.GuardBrains == null || content.GuardBrains.Length == 0) continue;
                var kinds = new System.Collections.Generic.HashSet<string>((content.Encounters ?? Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e => e != null && !string.IsNullOrEmpty(e.ContentId)).Select(e => e.ContentId));
                var bad = content.GuardBrains.Where(g => g == null || !kinds.Contains(g.ContentId) || !(g.GuardShare > 0f) || g.GuardShare > 1f || g.GuardFacingDegrees < 1f || g.GuardFacingDegrees > 180f)
                    .Select(g => g == null ? "(null)" : g.ContentId).ToArray();
                r.Check("가드-2", bad.Length == 0 && content.EnemyBrain != null,
                    System.IO.Path.GetFileName(path) + ": " + content.GuardBrains.Length + " guard entries name enemy kinds of this content with a share in (0, 1] and a front of 1-180 degrees, and the content hands out a brain to clone" + (bad.Length > 0 ? " | bad: " + string.Join(", ", bad) : ""));
            }
        }
    }
}