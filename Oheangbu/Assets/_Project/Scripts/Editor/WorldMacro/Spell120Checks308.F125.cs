using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 F125 fixtures: the three path-opening field glyphs against the main world's own gates. Every gate row of the
    // dungeon layouts (Art/World/Compact/Rebuild/Dungeon308/<id>/layout.json, read, never written) is armed in the fixture scene
    // by Dungeon308.Arm, the code Dungeon308 build arms the play scene's gates with, at its real place; W_Demo_Main is read as
    // text to see the same gates built there, closed. From both sides of every gate, two and four metres off and facing it, its
    // own glyph opens that gate and no other; from two metres off a gate of another kind the glyph is no cast; a living enemy
    // close by refuses it; the glyph casts on the main game's proof alone (Gate final). Left to Play: walking a real dungeon, the
    // hunting half of the non-combat rule, the saved fact across a reload, the look of the burn, the cut and the cleansing.
    public static partial class Spell120Checks308
    {
        const string MainSceneName = "W_Demo_Main";

        static partial void FixturesF125(Report r)
        {
            // grid cells: 45 = fire field (burns vines), 95 = metal field (cuts a boulder), 120 = water field (cleanses pollution)
            char[] letters = { SpellGrammar308.LetterAt(45), SpellGrammar308.LetterAt(95), SpellGrammar308.LetterAt(120) };
            if (!r.Wants(new string(letters))) return;
            var kinds = new[] { SpellFieldGateKind.Vine, SpellFieldGateKind.Boulder, SpellFieldGateKind.Pollution };
            var finals = new[] { SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung };
            var effects = new FieldGateEffect308[] { new FieldBurnEffect308(), new FieldCutEffect308(), new FieldPurifyEffect308() };

            var specs = Dungeon308.GateSpecs();
            string counts = string.Join(" ", kinds.Select(k => k + " " + specs.Count(s => s.Kind == k)));
            if (!r.Check("F125", kinds.All(k => specs.Any(s => s.Kind == k)) && specs.Select(s => s.Id).Distinct().Count() == specs.Count &&
                specs.All(s => s.Size.x > 0f && s.Size.y > 0f && s.Size.z > 0f && Dungeon308.MeshFile(s.Dungeon, s.Closed) && Dungeon308.MeshFile(s.Dungeon, s.Open)),
                "the dungeon layouts carry " + specs.Count + " field gates (" + counts + "), every kind at least once, ids unique, each with its closed and open mesh")) return;

            string scenePath = AssetDatabase.FindAssets(MainSceneName + " t:SceneAsset").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == MainSceneName);
            string script = AssetDatabase.FindAssets("SpellFieldGate308 t:MonoScript").FirstOrDefault(g => Path.GetFileName(AssetDatabase.GUIDToAssetPath(g)) == "SpellFieldGate308.cs");
            var built = scenePath != null && script != null ? SceneGates(scenePath, script) : new Dictionary<string, SceneGate>();
            var miss = specs.Where(s => !built.TryGetValue(s.Id, out var b) || b.Kind != (int)s.Kind || !Near(b.Radius, s.Radius) || b.Closed != 1 || b.Open != 1 || b.IsOpen).Select(s => s.Id).ToList();
            r.Check("F125", scenePath != null && built.Count == specs.Count && miss.Count == 0,
                MainSceneName + " (saved file, read as text) holds the same " + built.Count + " gates, each of its layout's kind and reach, one closed and one open part, saved closed (the session opens them from the ledger)" +
                (miss.Count > 0 ? "; differs: " + string.Join(" ", miss) : ""));

            using (var f = NewFixture(r, effects, WP12Sheets))
            {
                var fx = new WP06Presenter();
                f.Wiring.ConstructSpells(f.Registry, f.Finals, fx);
                ISpellCastHost host = f.Wiring;
                float now = Time.time;
                var everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                void Sync()
                {
                    Physics.SyncTransforms();
                    var physics = f.Scene.GetPhysicsScene();
                    if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("The fixture scene needs its own physics scene.");
                    physics.Simulate(.001f);
                }
                // the fixture's enemies wait far from every gate: a field cast is a non-combat cast
                void SendAway() { for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = new Vector3(-5000f - i * 10f, 0f, -5000f); }

                var gates = new SpellFieldGate308[specs.Count]; var opened = new List<string>();
                for (int i = 0; i < specs.Count; i++)
                {
                    var g = f.Obj("Spell308_Gate_" + specs[i].Id, specs[i].Pos);
                    var closed = f.Child(g, "Closed");
                    gates[i] = Dungeon308.Arm(specs[i], g, closed, f.Child(closed, "Body"), f.Child(g, "Open"));
                    gates[i].Opened += x => opened.Add(x.Id);
                }
                SendAway(); Sync();
                Collider Body(SpellFieldGate308 gate) => gate.transform.GetChild(0).GetComponentInChildren<Collider>(true);
                r.Check("F125", gates.All(g => !g.IsOpen && g.transform.GetChild(0).gameObject.activeSelf && !g.transform.GetChild(1).gameObject.activeSelf && Body(g) is BoxCollider && !Body(g).isTrigger),
                    "armed as Dungeon308 arms them: closed, the closed part standing and the open part hidden, one solid BoxCollider in the closed mass (a dungeon's polluted gate blocks the passage like the others)");

                void Reset()
                {
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled); f.RestoreAll();
                    foreach (var gate in gates) gate.SetOpen(false);
                    opened.Clear(); SendAway(); Sync();
                }
                // the passage runs through the gate's thin side: side +1 / -1 = which way it is walked, back = metres off the gate
                Vector3 Stand(Dungeon308.GateSpec s, float side, float back)
                {
                    Vector3 across = Quaternion.Euler(0f, s.Yaw, 0f) * Vector3.forward * side;
                    f.Player.transform.SetPositionAndRotation(s.Pos - across * back, Quaternion.LookRotation(across, Vector3.up)); Sync();
                    return across;
                }

                try
                {
                    for (int k = 0; k < 3; k++)
                    {
                        char letter = letters[k]; var effect = effects[k];
                        if (!r.Wants(letter.ToString())) continue;
                        string tag = letter + "-F";
                        f.Resolver.TryRow(letter, out var row);
                        float delay = row.F("gate.delay", 0f), combat = row.F("combat.radius", 0f);
                        void Tick(float at) => effect.Tick(new SpellTickContext(at, .1f, host, fx));
                        f.Finals.Open.Clear(); f.Finals.Proven.Clear(); f.Finals.Proven.Add(finals[k]);
                        r.Check(tag, row.Gate == SpellGateMode.Final, letter + " is Gate final: the main game's proof of its final casts it, with no TEST unlock");

                        // ---- every real gate of its kind, from both sides, two and four metres off ----
                        int tries = 0, good = 0; var wrong = new List<string>();
                        for (int i = 0; i < specs.Count; i++)
                        {
                            if (specs[i].Kind != kinds[k]) continue;
                            foreach (float side in new[] { 1f, -1f })
                                foreach (float back in new[] { 2f, 4f })
                                {
                                    tries++; Reset(); Stand(specs[i], side, back);
                                    float ink = f.Ink.Value; f.Cast(letter);
                                    bool waits = delay <= 0f || (!gates[i].IsOpen && effect.OpeningCount == 1);
                                    Tick(now + delay + .05f);
                                    if (waits && f.Accepted.Count == 1 && f.Misfires.Count == 0 && f.Ink.Value < ink && opened.Count == 1 && opened[0] == specs[i].Id &&
                                        gates[i].IsOpen && gates.Count(g => g.IsOpen) == 1) good++;
                                    else wrong.Add(specs[i].Id + (side > 0f ? "+" : "-") + back.ToString(CultureInfo.InvariantCulture) + ":" + string.Join("/", opened));
                                }
                        }
                        r.Check(tag, tries > 0 && good == tries, "from both sides of each of the main world's " + (tries / 4) + " " + kinds[k] + " gates, two and four metres off and facing it, one cast opens that gate after gate.delay and no other (" +
                            good + "/" + tries + (wrong.Count > 0 ? "; wrong: " + string.Join(" ", wrong) : "") + ")");

                        // ---- two metres off a gate of another kind: nothing of its own kind in reach, no cast ----
                        tries = 0; good = 0; wrong.Clear();
                        for (int i = 0; i < specs.Count; i++)
                        {
                            if (specs[i].Kind == kinds[k]) continue;
                            foreach (float side in new[] { 1f, -1f })
                            {
                                tries++; Reset(); Stand(specs[i], side, 2f);
                                float ink = f.Ink.Value; f.Cast(letter); Tick(now + delay + .05f);
                                if (f.Accepted.Count == 0 && f.Misfires.Count == 0 && f.Ink.Value == ink && opened.Count == 0) good++;
                                else wrong.Add(specs[i].Id + (side > 0f ? "+" : "-") + ":" + string.Join("/", opened));
                            }
                        }
                        r.Check(tag, tries == 0 || good == tries, "two metres off a real gate of another kind the glyph is no cast: no ink, no misfire, nothing opens (" + good + "/" + tries +
                            (wrong.Count > 0 ? "; opened: " + string.Join(" ", wrong) : "") + ")");

                        // ---- a living enemy close by at a real gate ----
                        int first = specs.FindIndex(s => s.Kind == kinds[k]);
                        Reset(); var way = Stand(specs[first], 1f, 2f);
                        everyone[0].transform.position = f.Player.transform.position - way * (combat * .5f); Sync();
                        float before = f.Ink.Value; f.Cast(letter); Tick(now + delay + 1f);
                        r.Check(tag, combat > 0f && f.Accepted.Count == 0 && f.Misfires.Count == 0 && f.Ink.Value == before && !gates[first].IsOpen && opened.Count == 0,
                            "at " + specs[first].Id + " a living enemy within combat.radius refuses the cast before any ink is spent; the gate stays shut");
                    }
                }
                finally
                {
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }   // the gates live in the fixture scene: closing it destroys them, also when a check above throws
        }

        struct SceneGate { public int Kind, Closed, Open; public float Radius; public bool IsOpen; }

        // The SpellFieldGate308 blocks of a saved scene file (YAML text, read only), by gate id. A block is recognised by its script
        // guid; the closed / open parts are counted from the two object lists.
        static Dictionary<string, SceneGate> SceneGates(string path, string scriptGuid)
        {
            var found = new Dictionary<string, SceneGate>(StringComparer.Ordinal);
            bool inGate = false; string id = null; var gate = new SceneGate(); int list = 0;
            void Flush() { if (inGate && !string.IsNullOrEmpty(id)) found[id] = gate; inGate = false; id = null; gate = new SceneGate { Kind = -1, Radius = -1f }; list = 0; }
            Flush();
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("--- ", StringComparison.Ordinal)) { Flush(); continue; }
                if (line.StartsWith("m_Script:", StringComparison.Ordinal)) { inGate = line.Contains("guid: " + scriptGuid); continue; }
                if (!inGate) continue;
                if (line.StartsWith("_id: ", StringComparison.Ordinal)) id = line.Substring(5).Trim();
                else if (line.StartsWith("_kind: ", StringComparison.Ordinal)) gate.Kind = int.Parse(line.Substring(7).Trim(), CultureInfo.InvariantCulture);
                else if (line.StartsWith("_radius: ", StringComparison.Ordinal)) gate.Radius = float.Parse(line.Substring(9).Trim(), CultureInfo.InvariantCulture);
                else if (line.StartsWith("_closedOnly:", StringComparison.Ordinal)) list = 1;
                else if (line.StartsWith("_openOnly:", StringComparison.Ordinal)) list = 2;
                else if (line.StartsWith("- {fileID:", StringComparison.Ordinal)) { if (list == 1) gate.Closed++; else if (list == 2) gate.Open++; }
                else if (line.StartsWith("_open: ", StringComparison.Ordinal)) { gate.IsOpen = line.Substring(7).Trim() != "0"; list = 0; }
            }
            Flush();
            return found;
        }
    }
}
