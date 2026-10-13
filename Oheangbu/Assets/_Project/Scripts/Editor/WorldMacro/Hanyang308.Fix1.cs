using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang fix 1 (SPEC-WORLD-HANYANG-308 section 19, D308-81). Edit mode only, queue-safe, ledgers in Ledger/fix1_*.json.
    //   fix1:door:plan|apply|revert    Donuimun gets the victory-bound door of the long-wall pass (WorldSealGate308, fact =
    //                                  WorldMacroPlaytestSession.SouthGateOpenedId) in the PLAY scene (the session reference cannot cross scenes):
    //                                  Hanyang308_GateDoors/Donui = two hinges + copies of the realm's leaves and bands (closed), a blocker box,
    //                                  a carving NavMeshObstacle. The realm's fixed open leaves are switched off. Saved CLOSED (blocker and carve on).
    //   fix1:tops:plan|apply|revert    NavMeshModifierVolume boxes (area 1 = not walkable, as the cliff galleries) in the PLAY scene under
    //                                  Hanyang308_GateDoors/NavOff, one over each stair / base / lintel / tower collider box of every Gate_* group of the
    //                                  realm scene that has a BaseCol (the Fort_GateBase gates). The realm scene is not written (it must stay script-free,
    //                                  RealmStream308 AC-S2). Colliders are not changed (the player still walks there); the next bake leaves no NavMesh there.
    //   fix1:hyemin:plan|apply|revert[:<dz>]   moves the Hyeminseo compound off the Gwanghuimun lane (default +5.5 m north) and reports seat + lane clearance
    //   fix1:door:pose                 puts the door of the open play scene back in its closed pose in memory (nothing is saved)
    // Edit-mode note: right after NavMain308 bake the carving obstacles of the gates are NOT applied to the new NavMesh until the next domain
    // reload (seen 2026-10-13: every closed gate read PathComplete until a recompile). Probe closed-gate paths after a reload. Play loads fresh.
    //   fix1:check                     the checks of this fix (read only; realm scene is opened for the call when it is not loaded)
    public static partial class Hanyang308
    {
        const string DoorRootName = "Hanyang308_GateDoors", DoorName = "Donui", NavOffName = "NavOff", DoorGateKey = "Realm_Hwanggyeong/Hanyang308_Gates/Gate_donui", HyeminKey = "Realm_Hwanggyeong/Hanyang308_Civic/Hyeminseo";
        static readonly string[] TopColliderNames = { "StairCol", "StairFill", "BaseCol", "LintelCol", "MunruCol" };
        static readonly Vector2[] GwanghuiLane = { new Vector2(2019, 2900), new Vector2(2100, 2890), new Vector2(2180, 2846), new Vector2(2240, 2800) };
        const float GwanghuiHalf = 2.55f;
        static string Fix1Ledger(string part) => Path.Combine(StageDir, "Ledger", "fix1_" + part + ".json");
        [Serializable] sealed class Fix1Door { public string root = "", door = ""; public List<string> off = new List<string>(); }
        [Serializable] sealed class Fix1Tops { public List<string> keys = new List<string>(), volumes = new List<string>(); }
        [Serializable] sealed class Fix1Hyemin { public string key = ""; public float dz; }

        static string Fix1(string[] a)
        {
            string part = a.Length > 1 ? a[1] : "", verb = a.Length > 2 ? a[2] : "plan";
            if (part == "check") return Fix1Check();
            if (part == "door" && verb == "pose") return Fix1DoorPose();
            if (verb != "plan" && verb != "apply" && verb != "revert") throw new Refuse("use fix1:<door|tops|hyemin>:plan|apply|revert | fix1:door:pose | fix1:check");
            var main = Main(); var opened = new List<Scene>();
            if (verb != "plan") for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new Refuse("unsaved scene " + SceneManager.GetSceneAt(i).name);
            try
            {
                var realm = OpenAdditive(RealmScene, main, opened);
                switch (part)
                {
                    case "door": return Fix1DoorCmd(main, realm, verb);
                    case "tops": return Fix1TopsCmd(main, realm, verb);
                    case "hyemin": return Fix1HyeminCmd(main, realm, verb, a.Length > 3 ? float.Parse(a[3], Inv) : 5.5f);
                    default: throw new Refuse("use fix1:<door|tops|hyemin>:plan|apply|revert | fix1:door:pose | fix1:check");
                }
            }
            finally { foreach (var s in opened) if (s.IsValid() && s.isLoaded) EditorSceneManager.CloseScene(s, true); }
        }

        static void SaveBoth(Scene main, Scene realm, bool mainToo, bool realmToo)
        {
            if (realmToo) { EditorSceneManager.MarkSceneDirty(realm); if (!EditorSceneManager.SaveScene(realm)) throw new Refuse("the realm scene was not saved"); }
            if (mainToo) { EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) throw new Refuse("the play scene was not saved"); }
        }

        // ---------- F1: Donuimun door ----------
        static string Fix1DoorCmd(Scene main, Scene realm, string verb)
        {
            string ledger = Fix1Ledger("door"); var sb = new StringBuilder("Hanyang308 fix1:door:" + verb + "\n");
            if (verb == "revert")
            {
                if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger);
                var led = JsonUtility.FromJson<Fix1Door>(File.ReadAllText(ledger));
                BackupFiles("fix1_door_revert", Abs(PlayScene), Abs(RealmScene));
                var rootGo = main.GetRootGameObjects().FirstOrDefault(g => g.name == led.root); var d = rootGo != null ? rootGo.transform.Find(led.door) : null;
                if (d != null) Object.DestroyImmediate(d.gameObject); if (rootGo != null && rootGo.transform.childCount == 0) Object.DestroyImmediate(rootGo);
                int on = 0; foreach (string k in led.off) { var t = Find(realm, k); if (t != null) { t.gameObject.SetActive(true); on++; } else sb.AppendLine("  MISSING " + k); }
                SaveBoth(main, realm, true, true); File.Delete(ledger);
                return sb + "  door removed, realm leaves back on " + on + " of " + led.off.Count + ", saved both scenes";
            }
            var gate = Find(realm, DoorGateKey); if (gate == null) throw new Refuse("no " + DoorGateKey);
            var leaves = new List<Transform>(); var bands = new List<Transform>();
            for (int i = 0; i < gate.childCount; i++) { var c = gate.GetChild(i); if (c.name == "Leaf_open") leaves.Add(c); else if (c.name == "Band") bands.Add(c); }
            if (leaves.Count != 2) throw new Refuse("expected 2 Leaf_open under Gate_donui, found " + leaves.Count);
            var baseT = gate.Find("Base"); if (baseT == null) throw new Refuse("no Base under Gate_donui");
            var session = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault(); if (session == null) throw new Refuse("no WorldMacroPlaytestSession in the play scene");
            foreach (var t in leaves.Concat(bands)) { var mf = t.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !AssetDatabase.Contains(mf.sharedMesh)) throw new Refuse(t.name + " has no mesh ASSET (a scene mesh cannot be shared with the play scene)"); }
            // gate frame: u = along the wall from the left hinge (opens with -deg) to the right hinge; inward = u x up (the leaves stand open along it today)
            Vector3 inward = leaves[0].right; inward.y = 0; inward.Normalize();
            Transform left = leaves[0], right = leaves[1]; Vector3 u = right.position - left.position; u.y = 0;
            if (Vector3.Dot(Vector3.Cross(u.normalized, Vector3.up), inward) < 0) { var tmp = left; left = right; right = tmp; u = -u; }
            float span = u.magnitude; u /= span; float leafW = left.lossyScale.x, leafH = left.lossyScale.y; Vector3 mid = (left.position + right.position) * .5f; mid.y = Mathf.Min(left.position.y, right.position.y);
            var rot = Quaternion.LookRotation(Vector3.Cross(u, Vector3.up), Vector3.up);
            sb.AppendLine("  hinges " + left.position.ToString("F2") + " / " + right.position.ToString("F2") + " | span " + F(span, "F2") + " m | leaf " + F(leafW, "F2") + " x " + F(leafH, "F2") + " m | inward " + inward.ToString("F3") + " | bands " + bands.Count);
            sb.AppendLine("  door frame at " + mid.ToString("F2") + " yaw " + F(rot.eulerAngles.y, "F2") + " | fact " + WorldMacroPlaytestSession.SouthGateOpenedId + " | session " + KeyOf(session.transform));
            if (Mathf.Abs(span - 2f * leafW) > .25f) throw new Refuse("the two leaves (" + F(2f * leafW, "F2") + " m) do not close the hinge span " + F(span, "F2") + " m");
            if (main.GetRootGameObjects().Any(g => g.name == DoorRootName && g.transform.Find(DoorName) != null)) throw new Refuse(DoorRootName + "/" + DoorName + " exists (fix1:door:revert first)");
            if (File.Exists(ledger)) throw new Refuse("a door ledger exists (fix1:door:revert first)");
            if (verb == "plan") return sb + "  plan only";
            BackupFiles("fix1_door", Abs(PlayScene), Abs(RealmScene));
            var rootObj = main.GetRootGameObjects().FirstOrDefault(g => g.name == DoorRootName);
            if (rootObj == null) { rootObj = new GameObject(DoorRootName); SceneManager.MoveGameObjectToScene(rootObj, main); }
            var door = new GameObject(DoorName); door.layer = left.gameObject.layer; door.transform.SetParent(rootObj.transform, false); door.transform.SetPositionAndRotation(mid, rot);
            Transform Hinge(string name, Transform srcLeaf, float x, float yaw, out Collider open)
            {
                var h = new GameObject(name); h.layer = door.layer; h.transform.SetParent(door.transform, false); h.transform.localPosition = new Vector3(x, srcLeaf.position.y - mid.y, 0); h.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                Transform Copy(Transform src, string n)
                {
                    var go = Object.Instantiate(src.gameObject); SceneManager.MoveGameObjectToScene(go, main); go.name = n; GameObjectUtility.SetStaticEditorFlags(go, 0);
                    go.transform.SetParent(h.transform, false); go.transform.localPosition = new Vector3(0, src.position.y - srcLeaf.position.y, 0); go.transform.localRotation = Quaternion.identity; go.transform.localScale = src.lossyScale; go.SetActive(true); return go.transform;
                }
                var leaf = Copy(srcLeaf, "Leaf"); open = leaf.GetComponent<Collider>(); if (open == null) { var bc = leaf.gameObject.AddComponent<BoxCollider>(); bc.center = new Vector3(.5f, .5f, 0); bc.size = new Vector3(1, 1, .18f); open = bc; }
                open.enabled = false;
                foreach (var b in bands) if (Vector2.Distance(new Vector2(b.position.x, b.position.z), new Vector2(srcLeaf.position.x, srcLeaf.position.z)) < .05f) { var bt = Copy(b, "Band"); foreach (var bc in bt.GetComponents<Collider>()) Object.DestroyImmediate(bc); }
                return h.transform;
            }
            var lh = Hinge("LeftHinge", left, -span * .5f, 0f, out var openL); var rh = Hinge("RightHinge", right, span * .5f, 180f, out var openR);
            var blockGo = new GameObject("Blocker"); blockGo.layer = door.layer; blockGo.transform.SetParent(door.transform, false);
            var blocker = blockGo.AddComponent<BoxCollider>(); blocker.center = new Vector3(0, (leafH + 1.3f) * .5f, 0); blocker.size = new Vector3(span + .8f, leafH + 1.3f, .5f);
            var carveGo = new GameObject("Carve"); carveGo.layer = door.layer; carveGo.transform.SetParent(door.transform, false);
            var ob = carveGo.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Box; ob.center = new Vector3(0, 1.5f, 0); ob.size = new Vector3(span + .8f, 3f, 1.4f); ob.carving = true; ob.carveOnlyStationary = true;
            var seal = door.AddComponent<WorldSealGate308>();
            seal.Session = session; seal.RequiredCompleted = WorldMacroPlaytestSession.SouthGateOpenedId; seal.LeftLeaf = lh; seal.RightLeaf = rh; seal.OpenDegrees = 90f; seal.Duration = 2.4f;
            seal.ClosedBlockers = new Collider[] { blocker }; seal.ClosedNavigation = new[] { ob }; seal.OpenColliders = new[] { openL, openR };
            // saved CLOSED: blocker + carve on, open-leaf colliders off. The NavMesh bake poses it open itself (GateProbeScope296).
            blocker.enabled = true; ob.enabled = true;
            var led2 = new Fix1Door { root = DoorRootName, door = DoorName };
            foreach (var t in leaves.Concat(bands)) { led2.off.Add(KeyOf(t)); }
            foreach (var t in leaves.Concat(bands)) t.gameObject.SetActive(false);
            SaveBoth(main, realm, true, true); Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(led2, true));
            return sb + "  built " + DoorRootName + "/" + DoorName + " (WorldSealGate308, closed, blocker " + blocker.size.ToString("F2") + ", carve " + ob.size.ToString("F2") + ") | realm leaves + bands off " + led2.off.Count + " | saved both scenes | ledger " + ledger;
        }

        static string Fix1DoorPose()
        {
            var main = Main(); var rootGo = main.GetRootGameObjects().FirstOrDefault(g => g.name == DoorRootName); var doorT = rootGo != null ? rootGo.transform.Find(DoorName) : null; var seal = doorT != null ? doorT.GetComponent<WorldSealGate308>() : null;
            if (seal == null) throw new Refuse("no " + DoorRootName + "/" + DoorName);
            string was = "left " + F(seal.LeftLeaf.localEulerAngles.y) + " right " + F(seal.RightLeaf.localEulerAngles.y);
            seal.LeftLeaf.localRotation = Quaternion.identity; seal.RightLeaf.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var x in seal.ClosedBlockers) x.enabled = true; foreach (var x in seal.ClosedNavigation) x.enabled = true; foreach (var x in seal.OpenColliders) x.enabled = false; seal.ResyncAfterProbe(); Physics.SyncTransforms();
            return "Hanyang308 fix1:door:pose: " + was + " -> left 0.0 right 180.0, blocker + carve on, open-leaf colliders off (memory only, play scene dirty " + main.isDirty + ")";
        }

        // ---------- F3: gate tops off the NavMesh ----------
        static List<Transform> Fix1GateGroups(Scene realm)
        {
            var list = new List<Transform>();
            foreach (var g in realm.GetRootGameObjects()) foreach (var t in g.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Gate_", StringComparison.Ordinal) && t.Find("BaseCol") != null && t.parent != null && t.parent.name.StartsWith("Hanyang308_", StringComparison.Ordinal)) list.Add(t);
            return list;
        }
        static List<BoxCollider> Fix1TopBoxes(Transform gate)
        {
            var list = new List<BoxCollider>();
            for (int i = 0; i < gate.childCount; i++) { var c = gate.GetChild(i); if (Array.IndexOf(TopColliderNames, c.name) < 0) continue; var box = c.GetComponent<BoxCollider>(); if (box != null && box.enabled) list.Add(box); }
            return list;
        }
        static Transform Fix1NavOff(Scene main)
        {
            var rootGo = main.GetRootGameObjects().FirstOrDefault(g => g.name == DoorRootName); return rootGo != null ? rootGo.transform.Find(NavOffName) : null;
        }
        static string Fix1TopsCmd(Scene main, Scene realm, string verb)
        {
            string ledger = Fix1Ledger("tops"); var sb = new StringBuilder("Hanyang308 fix1:tops:" + verb + "\n");
            if (verb == "revert")
            {
                if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger);
                var led = JsonUtility.FromJson<Fix1Tops>(File.ReadAllText(ledger));
                if (led.keys.Count > 0)
                {
                    // first form of this fix (NavMeshModifier components on the realm colliders - refused by RealmStream308 AC-S2)
                    BackupFiles("fix1_tops_revert", Abs(RealmScene)); int n = 0;
                    foreach (string k in led.keys) { var t = Find(realm, k); var m = t != null ? t.GetComponent<NavMeshModifier>() : null; if (m != null) { Object.DestroyImmediate(m); n++; } }
                    SaveBoth(main, realm, false, true); File.Delete(ledger); return sb + "  removed " + n + " modifiers of " + led.keys.Count + ", saved the realm scene";
                }
                BackupFiles("fix1_tops_revert", Abs(PlayScene)); var off = Fix1NavOff(main); int vols = off != null ? off.GetComponentsInChildren<NavMeshModifierVolume>(true).Length : 0;
                if (off != null) { var parent = off.parent; Object.DestroyImmediate(off.gameObject); if (parent.childCount == 0) Object.DestroyImmediate(parent.gameObject); }
                SaveBoth(main, realm, true, false); File.Delete(ledger); return sb + "  removed " + vols + " volumes, saved the play scene";
            }
            int total = 0;
            foreach (var gate in Fix1GateGroups(realm)) { int n = Fix1TopBoxes(gate).Count; total += n; sb.AppendLine("  " + KeyOf(gate) + ": boxes " + n); }
            if (verb == "plan") return sb + "  plan only, " + total + " volumes";
            if (File.Exists(ledger)) throw new Refuse("a tops ledger exists (fix1:tops:revert first)");
            if (Fix1NavOff(main) != null) throw new Refuse(DoorRootName + "/" + NavOffName + " exists");
            if (total == 0) throw new Refuse("nothing to mark");
            BackupFiles("fix1_tops", Abs(PlayScene));
            var rootObj = main.GetRootGameObjects().FirstOrDefault(g => g.name == DoorRootName);
            if (rootObj == null) { rootObj = new GameObject(DoorRootName); SceneManager.MoveGameObjectToScene(rootObj, main); }
            var home = new GameObject(NavOffName).transform; home.SetParent(rootObj.transform, false); var led2 = new Fix1Tops();
            foreach (var gate in Fix1GateGroups(realm))
            {
                var g = new GameObject(gate.name).transform; g.SetParent(home, false);
                foreach (var box in Fix1TopBoxes(gate))
                {
                    var t = box.transform; var v = new GameObject(box.name).transform; v.SetParent(g, false); v.SetPositionAndRotation(t.position, t.rotation);
                    Vector3 size = Vector3.Scale(box.size, t.lossyScale), centre = Vector3.Scale(box.center, t.lossyScale); size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    var vol = v.gameObject.AddComponent<NavMeshModifierVolume>(); vol.area = 1;
                    // level box: 0.2 m wider, 1 m higher (the walk surface on its top). Pitched stair slab: 1.2 m to either side of the slab along its normal.
                    if (Mathf.Abs(t.up.y) > .99f) { vol.size = size + new Vector3(.4f, 1f, .4f); vol.center = centre + new Vector3(0, .5f, 0); }
                    else { vol.size = size + new Vector3(.4f, 2.4f, .4f); vol.center = centre; }
                    led2.volumes.Add(gate.name + "/" + box.name);
                }
            }
            SaveBoth(main, realm, true, false); Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(led2, true));
            return sb + "  " + led2.volumes.Count + " not-walkable volumes (area 1) under " + DoorRootName + "/" + NavOffName + ", saved the play scene (the realm scene is not written) | ledger " + ledger;
        }

        // ---------- F4: Hyeminseo off the lane ----------
        static float LaneDistance(Vector3 p)
        {
            float best = float.MaxValue; var q = new Vector2(p.x, p.z);
            for (int i = 1; i < GwanghuiLane.Length; i++)
            {
                Vector2 a = GwanghuiLane[i - 1], b = GwanghuiLane[i]; float t = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / (b - a).sqrMagnitude); best = Mathf.Min(best, Vector2.Distance(q, a + (b - a) * t));
            }
            return best;
        }
        static string Fix1HyeminMeasure(Transform group)
        {
            Physics.SyncTransforms(); float clear = float.MaxValue; string who = ""; int onLane = 0;
            foreach (var c in group.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled) continue; var b = c.bounds; float d = float.MaxValue;
                for (int ix = 0; ix <= 8; ix++) for (int iz = 0; iz <= 8; iz++) d = Mathf.Min(d, LaneDistance(new Vector3(Mathf.Lerp(b.min.x, b.max.x, ix / 8f), 0, Mathf.Lerp(b.min.z, b.max.z, iz / 8f))));
                if (d < clear) { clear = d; who = c.name; } if (d < GwanghuiHalf) onLane++;
            }
            // seat: physics ground (terrain tiles of the play scene) under the outline of the compound against the lowest masonry
            float low = float.MaxValue; var all = new Bounds(); bool has = false;
            foreach (var r in group.GetComponentsInChildren<Renderer>(true)) { low = Mathf.Min(low, r.bounds.min.y); if (!has) { all = r.bounds; has = true; } else all.Encapsulate(r.bounds); }
            float gMin = float.MaxValue, gMax = float.MinValue; int miss = 0;
            for (int ix = 0; ix <= 6; ix++) for (int iz = 0; iz <= 6; iz++)
            {
                var o = new Vector3(Mathf.Lerp(all.min.x + .5f, all.max.x - .5f, ix / 6f), all.max.y + 60f, Mathf.Lerp(all.min.z + .5f, all.max.z - .5f, iz / 6f)); float g = float.MinValue;
                foreach (var h in Physics.RaycastAll(o, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.transform.root.name.StartsWith("Reworld292_Terrain", StringComparison.Ordinal)) g = Mathf.Max(g, h.point.y);
                if (g == float.MinValue) { miss++; continue; } gMin = Mathf.Min(gMin, g); gMax = Mathf.Max(gMax, g);
            }
            return "lane centre -> nearest collider " + F(clear, "F2") + " m (" + who + "), lane half width " + F(GwanghuiHalf, "F2") + " -> free beside the lane edge " + F(clear - GwanghuiHalf, "F2") + " m, colliders on the lane " + onLane
                + " | footprint x " + F(all.min.x) + "-" + F(all.max.x) + " z " + F(all.min.z) + "-" + F(all.max.z) + " | masonry bottom " + F(low, "F2") + ", ground under it " + F(gMin, "F2") + " - " + F(gMax, "F2") + " (no ground " + miss + ") -> buried at least " + F(gMin - low, "F2") + " m";
        }
        static string Fix1HyeminCmd(Scene main, Scene realm, string verb, float dz)
        {
            string ledger = Fix1Ledger("hyemin"); var sb = new StringBuilder("Hanyang308 fix1:hyemin:" + verb + "\n");
            var group = Find(realm, HyeminKey); if (group == null) throw new Refuse("no " + HyeminKey);
            sb.AppendLine("  now: " + Fix1HyeminMeasure(group));
            if (verb == "revert")
            {
                if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger);
                var led = JsonUtility.FromJson<Fix1Hyemin>(File.ReadAllText(ledger)); BackupFiles("fix1_hyemin_revert", Abs(RealmScene));
                group.position -= new Vector3(0, 0, led.dz); sb.AppendLine("  back: " + Fix1HyeminMeasure(group)); SaveBoth(main, realm, false, true); File.Delete(ledger); return sb + "  moved back " + F(led.dz, "F2") + " m, saved the realm scene";
            }
            if (File.Exists(ledger)) throw new Refuse("a hyemin ledger exists (fix1:hyemin:revert first)");
            // other colliders in the way of the moved footprint (anything of the realm scene that is not this group and not terrain)
            var before = group.position; group.position = before + new Vector3(0, 0, dz); string after = Fix1HyeminMeasure(group); var hits = new HashSet<string>();
            foreach (var c in group.GetComponentsInChildren<Collider>(true)) { var b = c.bounds; foreach (var o in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) { if (o.transform.IsChildOf(group) || o.transform.root.name.StartsWith("Reworld292_", StringComparison.Ordinal)) continue; hits.Add(KeyOf(o.transform)); } }
            sb.AppendLine("  +" + F(dz, "F2") + " m north: " + after); sb.AppendLine("  other colliders touching the moved compound: " + (hits.Count == 0 ? "none" : string.Join(", ", hits.Take(12))));
            if (verb == "plan") { group.position = before; Physics.SyncTransforms(); return sb + "  plan only"; }
            if (hits.Count > 0) { group.position = before; Physics.SyncTransforms(); throw new Refuse(sb + "  the moved compound touches other colliders, nothing written"); }
            group.position = before; BackupFiles("fix1_hyemin", Abs(RealmScene)); group.position = before + new Vector3(0, 0, dz);
            SaveBoth(main, realm, false, true); Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(new Fix1Hyemin { key = HyeminKey, dz = dz }, true));
            return sb + "  moved, saved the realm scene | ledger " + ledger;
        }

        // ---------- checks ----------
        static string Fix1Check()
        {
            var main = Main(); var opened = new List<Scene>(); var sb = new StringBuilder("Hanyang308 fix1:check\n"); int pass = 0, fail = 0;
            void C(bool ok, string text) { sb.Append(ok ? "  [PASS] " : "  [FAIL] ").Append(text).Append('\n'); if (ok) pass++; else fail++; }
            try
            {
                var realm = OpenAdditive(RealmScene, main, opened); Physics.SyncTransforms();
                var rootGo = main.GetRootGameObjects().FirstOrDefault(g => g.name == DoorRootName); var doorT = rootGo != null ? rootGo.transform.Find(DoorName) : null; var seal = doorT != null ? doorT.GetComponent<WorldSealGate308>() : null;
                var session = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
                C(seal != null && seal.IsConfigured && seal.isActiveAndEnabled, "Donuimun has one configured WorldSealGate308 in the play scene (" + DoorRootName + "/" + DoorName + ")");
                if (seal != null)
                {
                    C(seal.Session == session && session != null && seal.RequiredCompleted == WorldMacroPlaytestSession.SouthGateOpenedId, "it reads the play session and opens on the south-gate fact '" + seal.RequiredCompleted + "' (the same fact as the long-wall pass gate)");
                    bool closedPose = seal.LeftLeaf != null && seal.RightLeaf != null && Quaternion.Angle(seal.LeftLeaf.localRotation, Quaternion.identity) < .01f && Quaternion.Angle(seal.RightLeaf.localRotation, Quaternion.Euler(0, 180, 0)) < .01f;
                    bool flags = seal.ClosedBlockers.All(b => b != null && b.enabled) && seal.ClosedNavigation.All(o => o != null && o.enabled && o.carving) && seal.OpenColliders.All(o => o != null && !o.enabled);
                    C(closedPose && flags, "saved CLOSED: leaves shut, blocker and carve on, open-leaf colliders off");
                    // rays through the arch along the inward axis, closed and posed open
                    Vector3 n = doorT.forward, c0 = doorT.position; float[] hs = { .6f, 2f, 3.9f };
                    int Through(bool doorOnly) { int k = 0; foreach (float h in hs) foreach (float side in new[] { -1.6f, 0f, 1.6f }) { var o = c0 + doorT.right * side + Vector3.up * h - n * 4f; var hits = Physics.RaycastAll(o, n, 10f, ~0, QueryTriggerInteraction.Ignore); if (!hits.Any(x => !doorOnly || x.collider.transform.IsChildOf(doorT))) k++; } return k; }
                    int clearClosed = Through(false);
                    // SetProbePose re-reads the CURRENT leaf pose as "closed" in Edit mode, so the saved poses are put back by hand (GateProbeScope296 does the same)
                    bool en = seal.enabled; Quaternion l0 = seal.LeftLeaf.localRotation, r0 = seal.RightLeaf.localRotation; seal.enabled = false; seal.SetProbePose(true); Physics.SyncTransforms(); int clearOpen = Through(false);
                    seal.LeftLeaf.localRotation = l0; seal.RightLeaf.localRotation = r0; foreach (var x in seal.ClosedBlockers) x.enabled = true; foreach (var x in seal.ClosedNavigation) x.enabled = true; foreach (var x in seal.OpenColliders) x.enabled = false;
                    seal.ResyncAfterProbe(); seal.enabled = en; Physics.SyncTransforms();
                    C(clearClosed == 0, "closed: 9 rays through the arch (0.6 / 2.0 / 3.9 m, centre and 1.6 m to each side) all stopped - clear " + clearClosed);
                    C(clearOpen == 9, "posed open (what the fact does): the same 9 rays all pass - clear " + clearOpen + " of 9");
                    bool flags2 = seal.ClosedBlockers.All(b => b.enabled) && seal.ClosedNavigation.All(o => o.enabled) && seal.OpenColliders.All(o => !o.enabled);
                    C(flags2 && !main.isDirty, "the probe put the closed state back and left the play scene clean");
                    // NavMesh: outside -> inside with the carve on
                    Vector3 a = c0 - n * 14.5f + Vector3.up * 2.5f, b = c0 + n * 25f; bool ea = NavMesh.SamplePosition(a, out var ha, 5f, NavMesh.AllAreas), eb = NavMesh.SamplePosition(b, out var hb, 5f, NavMesh.AllAreas); var path = new NavMeshPath(); if (ea && eb) NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path);
                    C(ea && eb && path.status != NavMeshPathStatus.PathComplete, "NavMesh, door closed: outside " + (ea ? ha.position.ToString("F1") : "none") + " -> inside " + (eb ? hb.position.ToString("F1") : "none") + " = " + (ea && eb ? path.status.ToString() : "an end is off the mesh") + " (expected: not complete)");
                }
                var gate = Find(realm, DoorGateKey); int stillOn = 0, fixedLeaves = 0;
                if (gate != null) for (int i = 0; i < gate.childCount; i++) { var c = gate.GetChild(i); if (c.name == "Leaf_open" || c.name == "Band") { fixedLeaves++; if (c.gameObject.activeSelf) stillOn++; } }
                C(gate != null && fixedLeaves == 10 && stillOn == 0, "the realm's fixed open leaves and bands are off (" + (fixedLeaves - stillOn) + " of " + fixedLeaves + ")");
                C(Object.FindObjectsByType<Oheangbu.App.Demo.SouthGateDoorPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(d => d.gameObject.scene == main) == 4, "the four session doors (SouthGateDoorPresentation) are untouched: still 4 in the play scene");
                // gate tops
                int need = 0, marked = 0, onMesh = 0; var where = new List<string>();
                foreach (var g in Fix1GateGroups(realm))
                {
                    float top = float.MinValue; Vector3 centre = default;
                    for (int i = 0; i < g.childCount; i++)
                    {
                        var c = g.GetChild(i); var col = c.GetComponent<Collider>(); if (col == null || Array.IndexOf(TopColliderNames, c.name) < 0) continue; if (!(col is BoxCollider)) continue; need++;
                        if (c.name == "BaseCol" || c.name == "LintelCol") { top = Mathf.Max(top, col.bounds.max.y); } if (c.name == "LintelCol") centre = col.bounds.center;
                        if (c.name == "BaseCol" || c.name == "StairCol") { var p = new Vector3(col.bounds.center.x, c.name == "BaseCol" ? col.bounds.max.y : col.bounds.center.y, col.bounds.center.z); if (NavMesh.SamplePosition(p, out var h, 1.2f, NavMesh.AllAreas) && Mathf.Abs(h.position.y - p.y) < 1.2f) { onMesh++; where.Add(g.name + "/" + c.name); } }
                    }
                }
                var navOff = Fix1NavOff(main); marked = navOff != null ? navOff.GetComponentsInChildren<NavMeshModifierVolume>(true).Count(v => v.area == 1 && v.isActiveAndEnabled) : 0;
                C(need > 0 && marked == need, "not-walkable volumes over the gate-top boxes: " + marked + " of " + need + " (Fort_GateBase gates " + Fix1GateGroups(realm).Count + ", play scene " + DoorRootName + "/" + NavOffName + ")");
                int scripts = realm.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<MonoBehaviour>(true).Length);
                C(scripts == 0, "the realm scene carries no script component (RealmStream308 AC-S2) - found " + scripts);
                C(onMesh == 0, "no NavMesh on the gate tops and stairs (after the bake) - on the mesh: " + onMesh + (where.Count > 0 ? " [" + string.Join(", ", where.Take(8)) + "]" : ""));
                var hy = Find(realm, HyeminKey); string hm = hy != null ? Fix1HyeminMeasure(hy) : "no group"; float free = -1f; if (hy != null) { float clear = float.MaxValue; foreach (var c in hy.GetComponentsInChildren<Collider>(true)) { var b = c.bounds; for (int ix = 0; ix <= 8; ix++) for (int iz = 0; iz <= 8; iz++) clear = Mathf.Min(clear, LaneDistance(new Vector3(Mathf.Lerp(b.min.x, b.max.x, ix / 8f), 0, Mathf.Lerp(b.min.z, b.max.z, iz / 8f)))); } free = clear; }
                C(hy != null && free >= GwanghuiHalf, "Gwanghuimun lane is clear of the Hyeminseo compound (lane half width " + F(GwanghuiHalf, "F2") + " m): " + hm);
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            return sb.Append(pass + fail).Append(" checks, ").Append(fail).Append(" failed").ToString();
        }
    }
}
