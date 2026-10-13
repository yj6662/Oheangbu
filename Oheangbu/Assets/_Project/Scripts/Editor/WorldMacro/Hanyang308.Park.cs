using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // park = nothing is deleted. "off": the object is switched off where it stands (its former activeSelf is kept in the ledger).
    // "out": a unit of the realm scene (a child of its Realm_<Id> root) is moved into Stream/HanyangParked308.unity, a scene that is never
    // loaded and is not in the build, and its row is taken out of the RealmStream308 manifest (kept in the ledger) so that tool's counts stay true.
    // content = coordinates of content rows (and the scene objects that belong to them) moved by a ledger.
    public static partial class Hanyang308
    {
        const string ParkScenePath = "Assets/_Project/Scenes/World/Stream/HanyangParked308.unity", ContentAsset = "Assets/_Project/Scenes/World/Main/WorldContent_Main.asset";
        static string ParkFile => Path.Combine(StageDir, "park308.json");
        static string ParkLedgerFile => Path.Combine(StageDir, "Ledger", "park.json");
        static string ContentFile => Path.Combine(StageDir, "content308.json");
        static string ContentLedgerFile => Path.Combine(StageDir, "Ledger", "content.json");
        static string ManifestFile => Path.Combine(Repo, "Tools", "Unity", "Stage308_stream", "manifest.json");

        [Serializable] sealed class ParkItem { public string scene = "main", key = "", mode = "off", why = ""; }
        [Serializable] sealed class ParkList { public string version = ""; public List<ParkItem> items = new List<ParkItem>(); }
        [Serializable] sealed class MUnit { public string realm = "", name = "", parentGid = "", parentPath = ""; public int sibling, renderers; public long vertices; }
        [Serializable] sealed class MFile { public string mainScene = "", stamp = ""; public List<MUnit> units = new List<MUnit>(); }
        [Serializable] sealed class ParkRow { public string scene = "", key = "", mode = "", why = "", name = ""; public bool wasActive; public int childIndex = -1, realmOrder = -1; public MUnit unit; public bool hasUnit; public int renderers; public long vertices; }
        [Serializable] sealed class ParkLedger { public string stamp = "", backup = ""; public List<ParkRow> rows = new List<ParkRow>(); }

        static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", Inv);
        static string BackupFiles(string label, params string[] files)
        {
            string dir = Path.Combine(StageDir, "Backup", Stamp() + "_" + label); Directory.CreateDirectory(dir);
            foreach (var f in files) { if (File.Exists(f)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true); }
            return dir;
        }
        static string Abs(string assetPath) => Path.Combine(Repo, "Oheangbu", assetPath);
        static Scene OpenAdditive(string path, Scene main, List<Scene> opened)
        {
            var s = SceneManager.GetSceneByPath(path);
            if (!s.IsValid() || !s.isLoaded) { s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened.Add(s); SceneManager.SetActiveScene(main); }
            return s;
        }
        static Transform RealmRootOf(Scene realm) { foreach (var g in realm.GetRootGameObjects()) if (g.name == "Realm_Hwanggyeong") return g.transform; return null; }
        static void Count(Transform t, out int renderers, out long vertices)
        {
            renderers = 0; vertices = 0;
            foreach (var c in t.GetComponentsInChildren<Component>(true)) { if (c is MeshRenderer) renderers++; if (c is MeshFilter f && f.sharedMesh != null) vertices += f.sharedMesh.vertexCount; }
        }

        // the stream sheet keeps the unit count of each realm (RealmStream308 check AC-S8): it follows the manifest
        static string SyncSheet()
        {
            const string sheetPath = "Assets/_Project/Scenes/World/Stream/RealmStreamSheet308.asset"; var sheet = AssetDatabase.LoadMainAssetAtPath(sheetPath); if (sheet == null) return "no sheet";
            var manifest = JsonUtility.FromJson<MFile>(File.ReadAllText(ManifestFile)); int want = manifest.units.Count(u => u.realm == "Hwanggyeong"); var so = new SerializedObject(sheet); var realms = so.FindProperty("Realms"); string said = "sheet: no Hwanggyeong row";
            for (int i = 0; realms != null && i < realms.arraySize; i++)
            {
                var e = realms.GetArrayElementAtIndex(i); if (e.FindPropertyRelative("Id").stringValue != "Hwanggyeong") continue;
                var u = e.FindPropertyRelative("Units"); said = "sheet Hwanggyeong units " + u.intValue + " -> " + want; if (u.intValue != want) { BackupFiles("sheet", Abs(sheetPath)); u.intValue = want; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet); }
            }
            return said;
        }

        static string Park(string verb)
        {
            var main = Main(); if (verb == "revert") return ParkRevert(main) + " | " + SyncSheet();
            if (verb == "sheet") return "Hanyang308 park:sheet - " + SyncSheet();
            if (verb != "plan" && verb != "apply") throw new Refuse("use park:plan | park:apply | park:revert");
            if (!File.Exists(ParkFile)) throw new Refuse("no " + ParkFile);
            var list = JsonUtility.FromJson<ParkList>(File.ReadAllText(ParkFile)); bool write = verb == "apply";
            var ledger = File.Exists(ParkLedgerFile) ? JsonUtility.FromJson<ParkLedger>(File.ReadAllText(ParkLedgerFile)) : new ParkLedger();
            var done = new HashSet<string>(ledger.rows.Select(r => r.scene + "|" + r.key));
            if (write && main.isDirty) throw new Refuse("the play scene has unsaved changes");
            var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var realmRoot = RealmRootOf(realm);
            var sb = new StringBuilder("Hanyang308 park:" + verb + " " + list.version + " items " + list.items.Count + "\n"); int off = 0, outN = 0, skip = 0, missing = 0, prot = 0; long vOff = 0, vOut = 0; var todo = new List<(ParkItem item, Transform t)>();
            foreach (var it in list.items)
            {
                if (done.Contains(it.scene + "|" + it.key)) { skip++; continue; }
                var scene = it.scene == "realm" ? realm : main; var t = Find(scene, it.key);
                if (t == null) { missing++; sb.AppendLine("  MISSING " + it.scene + " " + it.key); continue; }
                if (IsProtected(t)) { prot++; sb.AppendLine("  PROTECTED (not touched) " + it.key); continue; }
                if (it.mode == "out" && (it.scene != "realm" || realmRoot == null || t.parent != realmRoot)) { missing++; sb.AppendLine("  NOT A REALM UNIT (out refused) " + it.key); continue; }
                Count(t, out int rn, out long vn); if (it.mode == "out") { outN++; vOut += vn; } else { off++; vOff += vn; }
                todo.Add((it, t));
            }
            sb.AppendLine("  switch off " + off + " (vertices " + vOff + ") | move out " + outN + " (vertices " + vOut + ") | already parked " + skip + " | missing " + missing + " | protected " + prot);
            if (!write || todo.Count == 0 || missing > 0 || prot > 0)
            {
                foreach (var s in opened) EditorSceneManager.CloseScene(s, true);
                return sb.Append(write ? (todo.Count == 0 ? "nothing to do" : "REFUSED: missing or protected items; nothing changed") : "plan only").ToString();
            }
            // manifest rows of the realm, in child order
            var manifest = JsonUtility.FromJson<MFile>(File.ReadAllText(ManifestFile)); var mine = manifest.units.Where(u => u.realm == "Hwanggyeong").ToList();
            if (realmRoot == null || realmRoot.childCount != mine.Count) { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); throw new Refuse("realm scene units " + (realmRoot != null ? realmRoot.childCount : -1) + " != manifest " + mine.Count); }
            for (int k = 0; k < mine.Count; k++) if (realmRoot.GetChild(k).name != mine[k].name) { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); throw new Refuse("realm unit " + k + " '" + realmRoot.GetChild(k).name + "' != manifest '" + mine[k].name + "'"); }
            string backup = BackupFiles("park", Abs(PlayScene), Abs(RealmScene), Abs(ParkScenePath), ManifestFile, ParkLedgerFile);
            Scene park = default;
            if (outN > 0)
            {
                if (File.Exists(Abs(ParkScenePath))) park = OpenAdditive(ParkScenePath, main, opened);
                else { park = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(main); EditorSceneManager.SaveScene(park, ParkScenePath); opened.Add(park); }
            }
            var index = new Dictionary<Transform, int>(); for (int k = 0; k < realmRoot.childCount; k++) index[realmRoot.GetChild(k)] = k;
            var rows = new List<ParkRow>();
            foreach (var (it, t) in todo)
            {
                Count(t, out int rn, out long vn);
                var row = new ParkRow { scene = it.scene, key = it.key, mode = it.mode, why = it.why, name = t.name, wasActive = t.gameObject.activeSelf, renderers = rn, vertices = vn, childIndex = t.GetSiblingIndex() };
                if (it.mode == "out") { row.realmOrder = index[t]; row.unit = mine[row.realmOrder]; row.hasUnit = true; }
                rows.Add(row);
            }
            foreach (var (it, t) in todo.Where(x => x.item.mode != "out")) t.gameObject.SetActive(false);
            var removed = new HashSet<MUnit>();
            foreach (var (it, t) in todo.Where(x => x.item.mode == "out").OrderByDescending(x => index[x.t]))
            {
                removed.Add(mine[index[t]]); t.SetParent(null, true); SceneManager.MoveGameObjectToScene(t.gameObject, park);
            }
            if (removed.Count > 0) { manifest.units.RemoveAll(u => removed.Contains(u)); File.WriteAllText(ManifestFile, JsonUtility.ToJson(manifest, true)); }
            ledger.stamp = Stamp(); ledger.backup = backup; ledger.rows.AddRange(rows);
            Directory.CreateDirectory(Path.GetDirectoryName(ParkLedgerFile)); File.WriteAllText(ParkLedgerFile, JsonUtility.ToJson(ledger, true));
            EditorSceneManager.MarkSceneDirty(main); EditorSceneManager.MarkSceneDirty(realm); bool ok = EditorSceneManager.SaveScene(realm);
            if (outN > 0) { EditorSceneManager.MarkSceneDirty(park); ok &= EditorSceneManager.SaveScene(park); }
            ok &= EditorSceneManager.SaveScene(main);
            foreach (var s in opened) EditorSceneManager.CloseScene(s, true);
            return sb.Append(ok ? "applied" : "FAILED a scene was not saved").Append(" | ledger rows " + ledger.rows.Count + " | backup " + backup + " | " + SyncSheet()).ToString();
        }

        static string ParkRevert(Scene main)
        {
            if (!File.Exists(ParkLedgerFile)) return "Hanyang308 park:revert - no ledger, nothing parked";
            if (main.isDirty) throw new Refuse("the play scene has unsaved changes");
            var ledger = JsonUtility.FromJson<ParkLedger>(File.ReadAllText(ParkLedgerFile)); var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var realmRoot = RealmRootOf(realm);
            string backup = BackupFiles("unpark", Abs(PlayScene), Abs(RealmScene), Abs(ParkScenePath), ManifestFile, ParkLedgerFile);
            int on = 0, back = 0, missing = 0;
            foreach (var r in ledger.rows.Where(x => x.mode != "out"))
            {
                var t = Find(r.scene == "realm" ? realm : main, r.key); if (t == null) { missing++; continue; }
                t.gameObject.SetActive(r.wasActive); on++;
            }
            var outs = ledger.rows.Where(x => x.mode == "out").OrderBy(x => x.realmOrder).ToList();
            if (outs.Count > 0)
            {
                var park = OpenAdditive(ParkScenePath, main, opened); var manifest = JsonUtility.FromJson<MFile>(File.ReadAllText(ManifestFile));
                var pool = park.GetRootGameObjects().ToList();
                foreach (var r in outs)
                {
                    var g = pool.FirstOrDefault(x => x.name == r.name); if (g == null) { missing++; continue; }
                    pool.Remove(g); SceneManager.MoveGameObjectToScene(g, realm); g.transform.SetParent(realmRoot, true); g.transform.SetSiblingIndex(Mathf.Min(r.realmOrder, realmRoot.childCount - 1));
                    // the manifest row goes back at the same order among the realm's rows
                    int seen = 0, at = manifest.units.Count;
                    for (int i = 0; i < manifest.units.Count; i++) if (manifest.units[i].realm == "Hwanggyeong") { if (seen == r.realmOrder) { at = i; break; } seen++; at = i + 1; }
                    manifest.units.Insert(at, r.unit); back++;
                }
                File.WriteAllText(ManifestFile, JsonUtility.ToJson(manifest, true)); EditorSceneManager.MarkSceneDirty(park); EditorSceneManager.SaveScene(park);
            }
            EditorSceneManager.MarkSceneDirty(realm); EditorSceneManager.MarkSceneDirty(main); bool ok = EditorSceneManager.SaveScene(realm) & EditorSceneManager.SaveScene(main);
            foreach (var s in opened) EditorSceneManager.CloseScene(s, true);
            if (missing == 0) File.Move(ParkLedgerFile, Path.Combine(backup, "park.reverted.json"));
            return "Hanyang308 park:revert - switched back " + on + ", moved back " + back + ", missing " + missing + (ok ? "" : " | FAILED a scene was not saved") + " | backup " + backup;
        }

        // ---------------------------------------------------------------- content coordinates
        [Serializable] sealed class ObjRef { public string scene = "main", key = ""; }
        [Serializable] sealed class Move { public string section = "", id = "", why = ""; public float x, y, z; public bool nav; public float yaw = float.NaN; public List<ObjRef> objects = new List<ObjRef>(); }
        [Serializable] sealed class MoveList { public string version = ""; public List<Move> moves = new List<Move>(); }
        [Serializable] sealed class MovedObj { public string scene = "", key = ""; public Vector3 from, to; }
        [Serializable] sealed class MoveRow { public string section = "", id = ""; public Vector3 from, to; public List<Vector3> patrolFrom = new List<Vector3>(), patrolTo = new List<Vector3>(); public List<MovedObj> objects = new List<MovedObj>(); }
        [Serializable] sealed class MoveLedger { public string stamp = "", backup = ""; public List<MoveRow> rows = new List<MoveRow>(); }

        static SerializedProperty RowOf(SerializedObject so, string section, string id)
        {
            var arr = so.FindProperty(section); if (arr == null || !arr.isArray) return null;
            for (int i = 0; i < arr.arraySize; i++) { var e = arr.GetArrayElementAtIndex(i); var pid = e.FindPropertyRelative("Id"); if (pid != null && pid.stringValue == id) return e; }
            return null;
        }
        static string VectorField(string section) => section == "Points" ? "Position" : "Feet";

        static string Content(string verb)
        {
            var main = Main(); var asset = AssetDatabase.LoadMainAssetAtPath(ContentAsset); if (asset == null) throw new Refuse("no " + ContentAsset);
            if (verb == "revert") return ContentRevert(main, asset);
            if (verb != "plan" && verb != "apply") throw new Refuse("use content:plan | content:apply | content:revert");
            if (!File.Exists(ContentFile)) throw new Refuse("no " + ContentFile);
            var list = JsonUtility.FromJson<MoveList>(File.ReadAllText(ContentFile)); bool write = verb == "apply";
            if (write && main.isDirty) throw new Refuse("the play scene has unsaved changes");
            if (write && File.Exists(ContentLedgerFile)) throw new Refuse("a content ledger exists (content:revert first)");
            var so = new SerializedObject(asset); var sb = new StringBuilder("Hanyang308 content:" + verb + " " + list.version + " moves " + list.moves.Count + "\n"); int bad = 0; var rows = new List<MoveRow>();
            var actors = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray();
            var plan = new List<(Move m, SerializedProperty row, Vector3 to, PrologueEncounter actor, List<Transform> objs)>();
            foreach (var m in list.moves)
            {
                var row = RowOf(so, m.section, m.id); if (row == null) { bad++; sb.AppendLine("  MISSING row " + m.section + "/" + m.id); continue; }
                var to = new Vector3(m.x, m.y, m.z); PrologueEncounter actor = null;
                if (m.nav)
                {
                    if (!NavMesh.SamplePosition(to, out var hit, 4f, NavMesh.AllAreas)) { bad++; sb.AppendLine("  NO NAVMESH within 4 m of " + m.id + " (" + F(m.x) + ", " + F(m.y) + ", " + F(m.z) + ")"); continue; }
                    to = hit.position;
                }
                if (m.section == "Encounters")
                {
                    var found = actors.Where(a => a.Id == m.id).ToArray(); if (found.Length != 1) { bad++; sb.AppendLine("  ACTORS " + found.Length + " for " + m.id); continue; }
                    actor = found[0];
                }
                var objs = new List<Transform>();
                foreach (var o in m.objects) { var t = Find(main, o.key); if (t == null) { bad++; sb.AppendLine("  MISSING object " + o.key); } else if (IsProtected(t)) { bad++; sb.AppendLine("  PROTECTED object " + o.key); } else objs.Add(t); }
                var from = row.FindPropertyRelative(VectorField(m.section)).vector3Value;
                sb.AppendLine("  " + m.section + "/" + m.id + " (" + F(from.x) + ", " + F(from.y) + ", " + F(from.z) + ") -> (" + F(to.x) + ", " + F(to.y) + ", " + F(to.z) + ")" + (actor != null ? " + actor" : "") + (objs.Count > 0 ? " + objects " + objs.Count : ""));
                plan.Add((m, row, to, actor, objs));
            }
            if (!write || bad > 0) return sb.Append(bad > 0 ? "REFUSED: " + bad + " problems; nothing changed" : "plan only").ToString();
            string backup = BackupFiles("content", Abs(PlayScene), Abs(ContentAsset), ContentLedgerFile);
            foreach (var (m, row, to, actor, objs) in plan)
            {
                var prop = row.FindPropertyRelative(VectorField(m.section)); var from = prop.vector3Value; var delta = to - from; var lr = new MoveRow { section = m.section, id = m.id, from = from, to = to };
                prop.vector3Value = to;
                var patrol = row.FindPropertyRelative("Patrol");
                if (m.section == "Encounters" && patrol != null && patrol.isArray)
                    for (int i = 0; i < patrol.arraySize; i++)
                    {
                        var pe = patrol.GetArrayElementAtIndex(i); var pf = pe.vector3Value; var pt = pf + delta;
                        if (NavMesh.SamplePosition(pt, out var ph, 6f, NavMesh.AllAreas)) pt = ph.position; else pt = to;
                        if (i == 0) pt = to;
                        lr.patrolFrom.Add(pf); lr.patrolTo.Add(pt); pe.vector3Value = pt;
                    }
                if (actor != null)
                {
                    var ag = actor.GetComponent<NavMeshAgent>(); float baseOff = ag != null ? ag.baseOffset : 0f; var t = actor.transform; var was = t.position;
                    t.position = to + Vector3.up * baseOff; lr.objects.Add(new MovedObj { scene = "main", key = KeyOf(t), from = was, to = t.position });
                }
                foreach (var t in objs) { var was = t.position; t.position = was + delta; lr.objects.Add(new MovedObj { scene = "main", key = KeyOf(t), from = was, to = t.position }); }
                rows.Add(lr);
            }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            var ledger = new MoveLedger { stamp = Stamp(), backup = backup, rows = rows }; Directory.CreateDirectory(Path.GetDirectoryName(ContentLedgerFile)); File.WriteAllText(ContentLedgerFile, JsonUtility.ToJson(ledger, true));
            EditorSceneManager.MarkSceneDirty(main); bool ok = EditorSceneManager.SaveScene(main);
            return sb.Append(ok ? "applied" : "FAILED the play scene was not saved").Append(" | rows " + rows.Count + " | backup " + backup).ToString();
        }

        static string ContentRevert(Scene main, UnityEngine.Object asset)
        {
            if (!File.Exists(ContentLedgerFile)) return "Hanyang308 content:revert - no ledger";
            if (main.isDirty) throw new Refuse("the play scene has unsaved changes");
            var ledger = JsonUtility.FromJson<MoveLedger>(File.ReadAllText(ContentLedgerFile)); var so = new SerializedObject(asset); string backup = BackupFiles("uncontent", Abs(PlayScene), Abs(ContentAsset), ContentLedgerFile); int n = 0, missing = 0;
            foreach (var r in ledger.rows)
            {
                var row = RowOf(so, r.section, r.id); if (row == null) { missing++; continue; }
                row.FindPropertyRelative(VectorField(r.section)).vector3Value = r.from; var patrol = row.FindPropertyRelative("Patrol");
                if (patrol != null && patrol.isArray) for (int i = 0; i < patrol.arraySize && i < r.patrolFrom.Count; i++) patrol.GetArrayElementAtIndex(i).vector3Value = r.patrolFrom[i];
                foreach (var o in r.objects) { var t = Find(main, o.key); if (t == null) { missing++; continue; } t.position = o.from; }
                n++;
            }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); EditorSceneManager.MarkSceneDirty(main); bool ok = EditorSceneManager.SaveScene(main);
            if (missing == 0) File.Move(ContentLedgerFile, Path.Combine(backup, "content.reverted.json"));
            return "Hanyang308 content:revert - rows " + n + " missing " + missing + (ok ? "" : " | FAILED the play scene was not saved") + " | backup " + backup;
        }
    }
}
