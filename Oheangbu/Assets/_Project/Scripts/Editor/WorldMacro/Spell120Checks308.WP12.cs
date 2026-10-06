using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-12 fixtures (SPEC-SPELL-120-308, opened by D308-13 Q5): the three path-opening field glyphs on the real wiring,
    // against TEST gates born in the fixture's own scene (SpellFixture308.Gate: never in an open scene, gone with the fixture).
    // Every cast goes through the resolver.
    // What stays for Play: a gate standing in the real world (placement is level design), the "an enemy is hunting the player"
    // half of the non-combat rule (it needs a live encounter), and the look of the burn, the cut and the cleansing.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP12Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP12" };

        static partial void FixturesWP12(Report r)
        {
            // grid cells: 45 = fire field (burns vines), 95 = metal field (cuts a boulder), 120 = water field (cleanses pollution)
            char[] letters = { SpellGrammar308.LetterAt(45), SpellGrammar308.LetterAt(95), SpellGrammar308.LetterAt(120) };
            if (!r.Wants(new string(letters))) return;
            const float room = .05f;
            var effects = new FieldGateEffect308[] { new FieldBurnEffect308(), new FieldCutEffect308(), new FieldPurifyEffect308() };
            var kinds = new[] { SpellFieldGateKind.Vine, SpellFieldGateKind.Boulder, SpellFieldGateKind.Pollution };
            var finals = new[] { SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung };
            var built = SpellEffectInstaller308.CreateAll().OfType<FieldGateEffect308>().Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            r.Check("W12", built.SequenceEqual(new[] { FieldBurnEffect308.HandlerId, FieldCutEffect308.HandlerId, FieldPurifyEffect308.HandlerId }),
                "the installer registers the three field handlers (" + string.Join(" ", built) + ")");

            using (var f = NewFixture(r, effects, WP12Sheets))
            {
                var fx = new WP06Presenter();
                f.Wiring.ConstructSpells(f.Registry, f.Finals, fx);
                ISpellCastHost host = f.Wiring;
                float cost = f.Config.SpellInkCost, misfire = f.Config.MisfireInkCost, now = Time.time;
                Vector3 home = f.Player.transform.position;
                var all = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                // the fixture's enemies stand a few metres from the caster: far away they are, a field cast is a non-combat cast
                for (int i = 0; i < all.Length; i++) all[i].transform.position = home + new Vector3(300f + i * 10f, 0f, 300f);
                void Sync()
                {
                    Physics.SyncTransforms();
                    var physics = f.Scene.GetPhysicsScene();
                    if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("The fixture scene needs its own physics scene.");
                    physics.Simulate(.001f);
                }
                // three TEST gates side by side, three metres ahead: every one of them is in reach and faced
                var gates = new SpellFieldGate308[3]; var opened = new int[3];
                for (int k = 0; k < 3; k++)
                {
                    int slot = k;
                    gates[k] = f.Gate(kinds[k], home + new Vector3((k - 1) * 2.5f, 0f, 3f), 1f);
                    gates[k].Opened += _ => opened[slot]++;
                }
                Sync();
                GameObject Block(SpellFieldGate308 gate) => gate.transform.GetChild(0).gameObject;
                void Reset()
                {
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled); f.RestoreAll();
                    f.Player.transform.SetPositionAndRotation(home, Quaternion.identity);
                    foreach (var gate in gates) gate.SetOpen(false);
                    Sync();
                }
                r.Check("W12", gates.All(g => !g.IsOpen && Block(g).activeSelf && g.Radius > 0f) && Block(gates[2]).GetComponent<Collider>().isTrigger && !Block(gates[0]).GetComponent<Collider>().isTrigger,
                    "TEST targets: three closed gates, each with a block that stands while it is closed (the polluted patch is a trigger)");

                for (int k = 0; k < 3; k++)
                {
                    char letter = letters[k]; var effect = effects[k]; var gate = gates[k];
                    if (!r.Wants(letter.ToString())) continue;
                    string open = letter + "-1", peace = letter + "-2";
                    f.Resolver.TryRow(letter, out var row);
                    float reach = row.F("gate.reach", 0f), delay = row.F("gate.delay", 0f), combat = row.F("combat.radius", 0f);
                    void Tick(float at) => effect.Tick(new SpellTickContext(at, .1f, host, fx));

                    // locked until its final consonant is owned: the one misfire, nothing opens
                    Reset(); f.Finals.Open.Clear();
                    float ink = f.Ink.Value; f.Cast(letter);
                    r.Check(open, f.Misfires.Count == 1 && f.Misfires[0].Value == SpellResolveStatus.Locked && Near(ink - f.Ink.Value, misfire) && gates.All(g => !g.IsOpen),
                        "without its final consonant the glyph is the usual misfire and no gate moves");
                    f.Finals.Open.Add(finals[k]);

                    // ---- non-combat only ----
                    Reset(); all[0].transform.position = home + Vector3.back * (combat * .5f);
                    ink = f.Ink.Value; f.Cast(letter); Tick(now + delay + 1f);
                    r.Check(peace, combat > 0f && f.Accepted.Count == 0 && f.Misfires.Count == 0 && f.Ink.Value == ink && !gate.IsOpen && effect.OpeningCount == 0,
                        "a living enemy within combat.radius: the cast is refused before any ink is spent, the gate stays shut");
                    all[0].TakeDamage(100000f);
                    f.Cast(letter);
                    bool deadIgnored = f.Accepted.Count == 1;
                    all[0].transform.position = home + new Vector3(300f, 0f, 300f);
                    r.Check(peace, deadIgnored, "a dead enemy lying there does not make it a combat cast");

                    // ---- the cast opens its own gate, after its delay ----
                    Reset();
                    ink = f.Ink.Value; int begun = fx.Begun.Count, hitCues = fx.Count(SpellFxCue.Hit); opened[k] = 0;
                    f.Cast(letter);
                    bool accepted = f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Field && Near(ink - f.Ink.Value, cost) &&
                        f.Plans.Count == 0 && f.PendingCount == 0 && fx.Begun.Count == begun + 1 && fx.Begun.Last().Role == SpellFxRole.Field && fx.Begun.Last().Target == gate.transform;
                    r.Check(open, accepted, "a drawn cast in reach of its gate costs one spell, is accepted as a field cast and begins one presentation aimed at the gate; nothing is scheduled against an enemy");
                    bool waits = delay <= 0f || (!gate.IsOpen && effect.OpeningCount == 1);
                    if (delay > .2f) { Tick(now + delay * .5f); waits &= !gate.IsOpen; }
                    float inkMid = f.Ink.Value; int acceptedMid = f.Accepted.Count;
                    if (delay > 0f) f.Cast(letter);
                    bool noSecond = f.Ink.Value == inkMid && f.Accepted.Count == acceptedMid && f.Misfires.Count == 0;
                    Tick(now + delay + room);
                    r.Check(open, waits && gate.IsOpen && !Block(gate).activeSelf && opened[k] == 1 && effect.OpeningCount == 0 && fx.Count(SpellFxCue.Hit) == hitCues + 1,
                        "the gate opens gate.delay seconds after the cast: its block goes, it announces itself once, the presenter hears one hit");
                    r.Check(open, noSecond && gates.Where(g => g != gate).All(g => !g.IsOpen && Block(g).activeSelf),
                        "a gate that is already being opened is not cast at twice, and the two gates of the other kinds, just as near, stay shut");
                    Sync(); ink = f.Ink.Value; int acceptedAfter = f.Accepted.Count;
                    f.Cast(letter);
                    r.Check(open, f.Ink.Value == ink && f.Accepted.Count == acceptedAfter && f.Misfires.Count == 0, "with nothing left to open the cast is no cast: no ink, not even misfire ink");

                    // ---- reach and facing ----
                    Reset(); f.Player.transform.position = home + Vector3.back * (reach + 2f); ink = f.Ink.Value;
                    f.Cast(letter);
                    bool tooFar = f.Accepted.Count == 0 && f.Ink.Value == ink;
                    f.Player.transform.SetPositionAndRotation(home, Quaternion.Euler(0f, 180f, 0f));
                    f.Cast(letter);
                    r.Check(open, tooFar && f.Accepted.Count == 0 && f.Ink.Value == ink && !gate.IsOpen, "out of gate.reach, or with the caster's back to it, the gate is not cast at");

                    // ---- a cast cut short ----
                    if (delay > 0f)
                    {
                        Reset(); f.Cast(letter);
                        bool pending = effect.OpeningCount == 1;
                        f.Wiring.ClearSpells308(SpellClearReason.Rest); Tick(now + delay + 1f);
                        r.Check(open, pending && effect.OpeningCount == 0 && !gate.IsOpen, "rest, death or scene leave before the delay is over drops the cast: the gate stays as it was");
                    }
                }
            }   // the gates live in the fixture scene: closing it destroys them, also when a check above throws
        }

        // TEST targets for a Play check of the three field glyphs (world placement of real gates is level design and not part
        // of #308). Play Mode only, so nothing can end up in a saved scene. Puts a vine, a boulder and a pollution test gate
        // side by side four metres ahead of the player; "clear" removes them. Call:
        //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Spell120Checks308 TestGatesWP12 ""
        public static string TestGatesWP12(string argument)
        {
            if (!Application.isPlaying) return "refused: TestGatesWP12 places its targets in Play Mode only";
            var old = Object.FindObjectsByType<SpellFieldGate308>(FindObjectsSortMode.None).Where(g => g.Id.StartsWith("test_", StringComparison.Ordinal)).ToArray();
            foreach (var gate in old) Object.Destroy(gate.gameObject);
            if ((argument ?? "").Trim() == "clear") return "removed " + old.Length + " test gates";
            var wiring = Object.FindObjectsByType<CombatLoopWiring>(FindObjectsSortMode.None).FirstOrDefault(w => w.isActiveAndEnabled);
            if (wiring == null) return "refused: no active CombatLoopWiring in the loaded scenes";
            ISpellCastHost host = wiring;
            Vector3 origin = host.PlayerPosition, forward = host.PlayerForward, right = Vector3.Cross(Vector3.up, forward);
            var kinds = new[] { SpellFieldGateKind.Vine, SpellFieldGateKind.Boulder, SpellFieldGateKind.Pollution };
            for (int k = 0; k < kinds.Length; k++)
            {
                var gate = SpellFieldGate308.CreateTest(kinds[k], origin + forward * 4f + right * ((k - 1) * 3f), 1.5f);
                SceneManager.MoveGameObjectToScene(gate.gameObject, wiring.gameObject.scene);
            }
            return "placed 3 test gates ahead of the player, left to right: vine, boulder, pollution (removed " + old.Length + " older ones)";
        }
    }
}
