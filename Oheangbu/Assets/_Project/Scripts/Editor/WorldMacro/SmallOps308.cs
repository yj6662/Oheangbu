using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff boundary 1b - three small reversible ledgers of D308-9e (SPEC-WORLD-CLIFF-BOUNDARY-308 "1b 작은 원장"):
    //   escort   the escort commission start onto the ground of the village's present place: content point Y + checkpoint feet Y
    //            (the scene's own content asset), the stop root, every child node on its own ground     (SmallOps308.Escort.cs)
    //   bell     the temple bell of 청림 산사 under a beam: ONE named prefab-instance transform under a protected tree root -
    //            a by-name exception that needs the user's confirmation (bell.user_confirmed in the data)   (SmallOps308.Bell.cs)
    //   face0    the collider of the 적로 잔도 rock wall re-pointed to the mesh that matches its visible face (SmallOps308.Face0.cs)
    //   stops    D308-16 fix F5: the other escort stops (checkpoint_1 / checkpoint_2 / cargo_delivery) onto their own ground, the same
    //            way `escort` grounded the start: content point Y (+ owned checkpoint feet Y), every child node on its own ground,
    //            XZ unchanged; data smallops308.cfg.json escort_stops{}                                   (SmallOps308.Stops.cs)
    //   delivery D308-22 answer 1 (relayout fix 3): the guesthouse of the cargo delivery stop onto the rim of the packed road end
    //            (plinth top = the packed surface) and the stop's person nodes onto the flat in front of it; XZ / yaw / limits from
    //            small_1b/delivery308.json, Y measured; moves colliders -> needs the data's bake_decision      (SmallOps308.Delivery.cs)
    // Queue: Oheangbu.EditorTools.WorldMacro.SmallOps308 Run "<command>"      (<scene> = arch296 | folk298 | main)
    //   status
    //   <op>:plan:<scene>      dry: measured numbers and what apply would write; nothing changes
    //   <op>:apply:<scene>     byte backup of every file written -> write -> ledger Out/cb308-small-<op>-<scene>.json; second run = "변경 없음"
    //   <op>:verify:<scene>    read only
    //   <op>:revert:<scene>    puts the recorded before-values back
    // Numbers: Art/World/Compact/Rebuild/CliffBoundary308/small_1b/smallops308.cfg.json (+ face0_308.json for the measured choice).
    // Refusals are strings: Play, compiling, a dirty scene, a scene outside the three, a protected path, a look preview up.
    public static partial class SmallOps308
    {
        const string Usage = "status | escort|bell|face0|stops|delivery : plan|apply|verify|revert : <scene>  (stops: optional 4th part = one stop id; stops:revert:<scene>:force / delivery:revert:<scene>:force = revert although a value moved after the apply)";

        public static string Run(string command)
        {
            string[] a = (command ?? "").Trim().Split(':');
            try
            {
                if (a[0] == "status") return Status();
                if (a.Length < 3) throw new PostLedger308.Refused("use " + Usage);
                var cfg = CliffCore308.LoadConfig(); string scene = CliffCore308.ScenePath(cfg, a[2]); string key = CliffCore308.SceneKey(cfg, scene);
                var data = Pieces308.Json(Pieces308.OpsConfigFile, out string sha);
                string verb = a[1];
                if (verb != "plan" && verb != "apply" && verb != "verify" && verb != "revert") throw new PostLedger308.Refused("unknown verb '" + verb + "' (" + Usage + ")");
                switch (a[0])
                {
                    case "escort": return Escort(data, sha, scene, key, verb);
                    case "bell": return Bell(data, sha, scene, key, verb);
                    case "face0": return Face0(cfg, data, sha, scene, key, verb);
                    case "stops": return Stops(data, sha, scene, key, verb, a.Length > 3 ? a[3] : "");   // D308-16 fix F5 (SmallOps308.Stops.cs)
                    case "delivery": return Delivery(data, scene, key, verb, a.Length > 3 ? a[3] : "");   // D308-22 answer 1 (SmallOps308.Delivery.cs)
                    default: throw new PostLedger308.Refused("unknown op '" + a[0] + "' (" + Usage + ")");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { Debug.LogException(e); return "error: " + e.GetType().Name + ": " + e.Message + " (see the console; the ledger shows how far the command got)"; }
        }

        static string LedgerName(string op, string key) => "cb308-small-" + op + "-" + key + ".json";

        static string Status()
        {
            var sb = new StringBuilder("SmallOps308 status | playing=" + EditorApplication.isPlaying + "\n");
            var data = Pieces308.Json(Pieces308.OpsConfigFile, out string sha); var cfg = CliffCore308.LoadConfig();
            sb.AppendLine("data " + Pieces308.OpsConfigFile + " sha " + Pieces308.Short(sha) + " | bell.user_confirmed = " + Pieces308.Flag(Pieces308.Req(data, "bell"), "user_confirmed"));
            foreach (var sc in cfg.scenes)
            {
                var e = Pieces308.ReadLedger<EscortLedger>(LedgerName("escort", sc.key)); var b = Pieces308.ReadLedger<BellLedger>(LedgerName("bell", sc.key)); var f = Pieces308.ReadLedger<Face0Ledger>(LedgerName("face0", sc.key));
                var st = Pieces308.ReadLedger<StopsLedger>(LedgerName("stops", sc.key)); var dl = Pieces308.ReadLedger<DelLedger>(LedgerName("delivery", sc.key));
                sb.AppendLine("  " + sc.key + ": escort " + (e == null ? "none" : e.state + " " + e.utc) + " | bell " + (b == null ? "none" : b.state + " " + b.utc) + " | face0 " + (f == null ? "none" : f.state + " " + f.utc)
                    + " | stops " + (st == null ? "none" : st.state + " " + st.utc + " [" + string.Join(",", st.stops.Select(x => x.id)) + "]")
                    + " | delivery " + (dl == null ? "none" : dl.state + " " + dl.utc + " [" + dl.variant + "]"));
            }
            return sb.ToString().TrimEnd();
        }

        static int Bits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0);
        static float FromBits(int b) => BitConverter.ToSingle(BitConverter.GetBytes(b), 0);
        static string[] Add(string[] history, string text) => (history ?? new string[0]).Concat(new[] { PostLedger308.Utc() + " " + text }).ToArray();
    }
}
