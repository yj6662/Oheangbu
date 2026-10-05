using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-00 fixtures (SPEC-SPELL-120-308): the defects of glyphs that already resolve. Every cast comes from the resolver;
    // nothing injects a power. What stays for Play: the summon's own spawn, movement and strike cadence (DemoSummon*Checks cover
    // the manager with an injected power, which is exactly what hid the defect).
    public static partial class Spell120Checks308
    {
        static partial void FixturesWP00(Report r)
        {
            // grid cells by index: 16 / 40 / 64 / 88 / 112 = the five summons, 2 = wood giyeok attack, 13 = wood circle, 20 = wood field
            int[] summonCells = { 16, 40, 64, 88, 112 };
            char root = SpellGrammar308.LetterAt(2), circle = SpellGrammar308.LetterAt(13), lift = SpellGrammar308.LetterAt(20);
            if (!r.Wants(new string(summonCells.Select(SpellGrammar308.LetterAt).Concat(new[] { root, circle, lift }).ToArray()))) return;
            var mainGuids = SpellTable308.MainSceneGuids();
            using (var f = NewFixture(r, SpellEffectInstaller308.CreateAll(), FoundationSheets))
            {
                // ---- summons: resolver -> cast power 0 -> profile base power -> a strike that applies damage ----
                var profiles = AssetDatabase.FindAssets("t:SummonCombatProfile").Where(g => mainGuids.Contains(g))
                    .Select(g => AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).ToArray();
                r.Check("W00", profiles.Length == 5, "the five summon profiles W_Demo_Main references were found (" + profiles.Length + ")");
                var summoner = f.Obj("Spell308_Summoned", SpellFixture308.Origin + Vector3.right);
                foreach (int cell in summonCells)
                {
                    char letter = SpellGrammar308.LetterAt(cell); string clause = letter + "-1";
                    var drawn = new DrawnLetter(letter, default, default, null, .6f, .6f, 1f, 1f, 3);
                    var status = f.Resolver.Resolve(drawn, new LegacySpellGate(false, false, false, false, false), out SpellCast cast, out SpellRow row);
                    var profile = profiles.FirstOrDefault(p => p.Letter == letter.ToString());
                    r.Check(clause, status == SpellResolveStatus.Ok && cast.Kind == SpellKind.Summon && cast.Power == 0f && row.Gate == SpellGateMode.Open,
                        "a drawn summon resolves (open row) with the book's power 0");
                    if (!r.Check(clause, profile != null && profile.BasePower > 0f && profile.TryValidate(out _), "its profile carries a positive base power and still validates")) continue;
                    float before = cast.Power * f.Wiring.SummonDamageScale(cast.Element) * profile.DamageMultiplier;                       // the pre-#308 snapshot: 0
                    float after = SummonPowerRule308.CastPower(cast.Power, profile.BasePower, cast.Brush, cast.HoldScale) * f.Wiring.SummonDamageScale(cast.Element) * profile.DamageMultiplier;
                    f.RestoreAll();
                    var target = f.Inside[0]; float hp = target.Hp; float groggy = target.Groggy.Value01; int mask = target.WeakPointElementMask;
                    var refused = f.Wiring.ApplySummonHit(target, target.LifeRevision, before, SpellFixture308.Origin + Vector3.up * .4f,
                        AttackProvenance.Create(summoner, DamageSource.Summon, cast.Element), letter);
                    var applied = f.Wiring.ApplySummonHit(target, target.LifeRevision, after, SpellFixture308.Origin + Vector3.up * .4f,
                        AttackProvenance.Create(summoner, DamageSource.Summon, cast.Element), letter);
                    r.Check(clause, before == 0f && refused.AppliedDamage == 0f, "before the fix the strike power was 0 and the wiring refused it");
                    r.Check(clause, after > 0f && Near(applied.AppliedDamage, after) && Near(hp - target.Hp, after),
                        "with the profile base power the same strike applies damage (" + after.ToString("0.###") + ")");
                    r.Check(letter + "-2", target.Groggy.Value01 == groggy && target.WeakPointElementMask == mask, "the summon strike adds no groggy and no five-element credit");
                }

                // ---- wood giyeok attack: a boss (profile data) is slowed, not bound ----
                var giyeokProfile = ScriptableObject.CreateInstance<EAGiyeokProfileSO>();
                try
                {
                    f.RestoreAll();
                    f.Wiring.ConfigureEAGiyeok(giyeokProfile, () => true);
                    float now = Time.time;
                    foreach (bool boss in new[] { true, false })
                    {
                        var target = boss ? f.Boss : f.Inside[0];
                        var drawn = new DrawnLetter(root, default, default, null, .6f, .6f, 1f, 1f, 3);
                        var status = f.Resolver.Resolve(drawn, new LegacySpellGate(false, false, false, true, false), out SpellCast cast, out _);
                        bool cast1 = status == SpellResolveStatus.Ok && f.Wiring.EAGiyeok.CastSpell(cast, target, SpellFixture308.Origin + Vector3.up * .4f, target.transform.position, .5f, now);
                        now += .5f; f.Wiring.EAGiyeok.Tick(now);
                        if (boss)
                            r.Check("각-2", cast1 && target.IsBoss && !target.Control.BlocksActions(now) && Near(target.Control.MovementScale(now), giyeokProfile.BossRootSpeed),
                                "IsBoss profile target (no boss controller component): slowed to the boss root speed, actions not blocked");
                        else
                            r.Check("각-1", cast1 && !target.IsBoss && target.Control.BlocksActions(now) && target.Control.MovementScale(now) == 0f,
                                "ordinary target: fully bound (unchanged)");
                        f.Wiring.EAGiyeok.Clear();
                    }
                }
                finally { f.Wiring.ConfigureEAGiyeok(null, null); Object.DestroyImmediate(giyeokProfile); }

                // ---- wood circle and wood field: numbers come from the row and equal the constants they replace ----
                f.Resolver.TryRow(circle, out var circleRow); f.Resolver.TryRow(lift, out var liftRow);
                var spec = AreaSpikeSpec.From(circleRow); var legacy = AreaSpikeSpec.Legacy;
                r.Check("고-2", circleRow != null && circleRow.Has("spike.count") && spec.Count == legacy.Count && spec.Fill == legacy.Fill && spec.Window == legacy.Window &&
                    spec.Gap == legacy.Gap && spec.Inner == legacy.Inner, "spike numbers are row data and equal the previous constants");
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Cast(circle);
                r.Check("고-2", f.Plans.Count == 1 && f.Plans[0].Area.Spikes.Count == spec.Count && f.Plans[0].Area.Spikes.Select(s => s.RiseAt).Distinct().Count() == spec.Count,
                    "a drawn cast builds the staggered plan from the row: every spike has its own rise time");
                var fromRow = FieldLiftSpec.From(liftRow, new FieldLiftSpec(9f, 9f, 9f, 9f)); var consts = FieldSpellService.LegacyLift;
                r.Check("국-1", liftRow != null && fromRow.Height == consts.Height && fromRow.RiseSpeed == consts.RiseSpeed && fromRow.DescentSpeed == consts.DescentSpeed &&
                    fromRow.HoldSeconds == consts.HoldSeconds, "lift numbers are row data and equal the previous constants");
            }
        }
    }
}
