using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang, final integration (SPEC-WORLD-HANYANG-308 §18): three small ledgered data fixes. Edit mode, no dialog, nothing deleted.
    //   final:layout:plan|apply|revert   WorldLayout_Main: Place + Route bends from Tools/Unity/Stage308_hanyang/final308.json (ledger Ledger/final_layout.json)
    //   final:vitals:plan|apply|revert   EnemyVitals._profile of the listed scene actors (ledger Ledger/final_vitals.json); saves the play scene
    //   final:roof:plan|apply|revert     switches the colliders under the listed keys off (ledger Ledger/final_roof.json); saves the scene that holds them
    public static partial class Hanyang308
    {
        static string FinalFile => Path.Combine(StageDir, "final308.json");
        static string FinalLedger(string part) => Path.Combine(StageDir, "Ledger", "final_" + part + ".json");

        [Serializable] sealed class FinalPlace { public string id = "", purpose = ""; public float x, z; public float[] anchor = Array.Empty<float>(); public bool bound; public string[] roots = Array.Empty<string>(); }
        [Serializable] sealed class FinalRoute { public string id = ""; public float[] bends = Array.Empty<float>(); }
        [Serializable] sealed class FinalVital { public string actor = "", profile = ""; }
        [Serializable] sealed class FinalData { public string version = "", layout = ""; public List<FinalPlace> places = new List<FinalPlace>(); public List<FinalRoute> routes = new List<FinalRoute>(); public List<FinalVital> vitals = new List<FinalVital>(); public List<string> colliders_off = new List<string>(), fields_remove = new List<string>(); public string fields_asset = ""; public float[] hull = Array.Empty<float>(); public List<FinalClearRow> clear = new List<FinalClearRow>(); }
        [Serializable] sealed class FinalRoofRow { public string scene = "", key = ""; public int index; public string type = ""; public bool was; }
        [Serializable] sealed class FinalClearRow { public string id = ""; public float[] a = Array.Empty<float>(), b = Array.Empty<float>(); public float reach = 12f, yaw = -999f; }
        [Serializable] sealed class FinalFieldsLedger { public string asset = ""; public List<string> rows = new List<string>(); }
        [Serializable] sealed class FinalRoofLedger { public List<FinalRoofRow> rows = new List<FinalRoofRow>(); }

        static string Final(string[] a)
        {
            string part = a.Length > 1 ? a[1] : "", verb = a.Length > 2 ? a[2] : "plan";
            if (part == "clear") verb = "plan";
            if (verb != "plan" && verb != "apply" && verb != "revert") throw new Refuse("use final:<layout|vitals|fields|roof>:plan|apply|revert");
            if (!File.Exists(FinalFile)) throw new Refuse("no " + FinalFile);
            var data = JsonUtility.FromJson<FinalData>(File.ReadAllText(FinalFile));
            switch (part)
            {
                case "layout": return FinalLayout(data, verb);
                case "vitals": return FinalVitals(data, verb);
                case "roof": return FinalRoof(data, verb);
                case "fields": return FinalFields(data, verb);
                case "clear": return FinalClear(data);
                default: throw new Refuse("use final:<layout|vitals|fields|roof>:plan|apply|revert");
            }
        }

        static float[] Flat(Vector2[] v) { var f = new float[v.Length * 2]; for (int i = 0; i < v.Length; i++) { f[2 * i] = v[i].x; f[2 * i + 1] = v[i].y; } return f; }
        static Vector2[] Pairs(float[] f) { var v = new Vector2[f.Length / 2]; for (int i = 0; i < v.Length; i++) v[i] = new Vector2(f[2 * i], f[2 * i + 1]); return v; }
        static float Len(Vector2[] v) { float l = 0; for (int i = 1; i < v.Length; i++) l += Vector2.Distance(v[i - 1], v[i]); return l; }

        static string FinalLayout(FinalData data, string verb)
        {
            var layout = AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(data.layout); if (layout == null) throw new Refuse("no layout " + data.layout);
            string ledger = FinalLedger("layout"); bool revert = verb == "revert", write = verb != "plan";
            FinalData src = data;
            if (revert) { if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger); src = JsonUtility.FromJson<FinalData>(File.ReadAllText(ledger)); }
            else if (write && File.Exists(ledger)) throw new Refuse("a layout ledger exists (final:layout:revert first)");
            var old = new FinalData { version = "ledger of " + data.version, layout = data.layout }; var sb = new StringBuilder("Hanyang308 final:layout:" + verb + " " + src.version + "\n"); int bad = 0;
            foreach (var p in src.places)
            {
                var place = layout.Places.FirstOrDefault(x => x.Id == p.id); if (place == null) { bad++; sb.AppendLine("  MISSING place " + p.id); continue; }
                old.places.Add(new FinalPlace { id = place.Id, purpose = place.Purpose ?? "", x = place.XZ.x, z = place.XZ.y, anchor = new[] { place.SourceAnchor.x, place.SourceAnchor.y, place.SourceAnchor.z }, bound = place.HasSourceBinding, roots = place.SceneRoots ?? Array.Empty<string>() });
                sb.AppendLine("  place " + p.id + " (" + F(place.XZ.x) + ", " + F(place.XZ.y) + ") bound " + place.HasSourceBinding + " roots " + (place.SceneRoots?.Length ?? 0) + " -> (" + F(p.x) + ", " + F(p.z) + ") bound " + p.bound + " roots " + p.roots.Length);
                if (!write) continue;
                place.XZ = new Vector2(p.x, p.z); if (p.anchor.Length == 3) place.SourceAnchor = new Vector3(p.anchor[0], p.anchor[1], p.anchor[2]); place.HasSourceBinding = p.bound; place.SceneRoots = p.roots; place.Purpose = p.purpose;
            }
            foreach (var r in src.routes)
            {
                var route = layout.Routes.FirstOrDefault(x => x.Id == r.id); if (route == null) { bad++; sb.AppendLine("  MISSING route " + r.id); continue; }
                if (r.bends.Length < 4 || r.bends.Length % 2 != 0) { bad++; sb.AppendLine("  BAD bends " + r.id); continue; }
                old.routes.Add(new FinalRoute { id = route.Id, bends = Flat(route.Bends) }); var to = Pairs(r.bends);
                sb.AppendLine("  route " + r.id + " bends " + route.Bends.Length + " (" + F(Len(route.Bends)) + " m, ends " + route.Bends[route.Bends.Length - 1].ToString("F0") + ") -> " + to.Length + " (" + F(Len(to)) + " m, ends " + to[to.Length - 1].ToString("F0") + ")");
                if (write) route.Bends = to;
            }
            if (bad > 0) { if (write) Resources.UnloadAsset(layout); throw new Refuse(sb + "  " + bad + " problem(s), nothing written"); }
            if (!write) return sb + "  plan only";
            BackupFiles("final_layout", Abs(data.layout));
            EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout);
            if (revert) File.Delete(ledger); else { Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(old, true)); }
            return sb + "  written " + data.layout + (revert ? " (reverted, ledger removed)" : " | ledger " + ledger);
        }

        static string FinalVitals(FinalData data, string verb)
        {
            var main = Main(); string ledger = FinalLedger("vitals"); bool revert = verb == "revert", write = verb != "plan";
            if (write && main.isDirty) throw new Refuse("the play scene has unsaved changes");
            FinalData src = data;
            if (revert) { if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger); src = JsonUtility.FromJson<FinalData>(File.ReadAllText(ledger)); }
            else if (write && File.Exists(ledger)) throw new Refuse("a vitals ledger exists (final:vitals:revert first)");
            var actors = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray();
            var old = new FinalData { version = "ledger of " + data.version }; var sb = new StringBuilder("Hanyang308 final:vitals:" + verb + "\n"); int bad = 0; var todo = new List<(EnemyVitals v, EnemyVitalsProfileSO p)>();
            foreach (var row in src.vitals)
            {
                var found = actors.Where(x => x.Id == row.actor).ToArray(); if (found.Length != 1) { bad++; sb.AppendLine("  ACTORS " + found.Length + " for " + row.actor); continue; }
                var vit = found[0].GetComponent<EnemyVitals>(); if (vit == null) { bad++; sb.AppendLine("  NO EnemyVitals on " + row.actor); continue; }
                var profile = row.profile.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(row.profile); if (row.profile.Length > 0 && profile == null) { bad++; sb.AppendLine("  NO profile " + row.profile); continue; }
                var was = vit.Profile; old.vitals.Add(new FinalVital { actor = row.actor, profile = was != null ? AssetDatabase.GetAssetPath(was) : "" });
                sb.AppendLine("  " + row.actor + ": " + (was != null ? was.name + " (boss " + was.IsBoss + ")" : "none") + " -> " + (profile != null ? profile.name + " (boss " + profile.IsBoss + ")" : "none"));
                todo.Add((vit, profile));
            }
            // context: what the neighbours carry
            foreach (var g in actors.Where(x => x.Id != null && x.Id.StartsWith("field308/hwanggyeong_", StringComparison.Ordinal)).GroupBy(x => { var v = x.GetComponent<EnemyVitals>(); return v != null && v.Profile != null ? v.Profile.name : "none"; }))
                sb.AppendLine("  INFO field308/hwanggyeong_* with " + g.Key + ": " + g.Count());
            sb.AppendLine("  INFO boss-profile actors in the play scene: " + string.Join(", ", actors.Where(x => { var v = x.GetComponent<EnemyVitals>(); return v != null && v.IsBoss; }).Select(x => x.Id)));
            if (bad > 0) throw new Refuse(sb + "  " + bad + " problem(s), nothing written");
            if (!write) return sb + "  plan only";
            BackupFiles("final_vitals", Abs(PlayScene));
            foreach (var (v, p) in todo) { var so = new SerializedObject(v); so.FindProperty("_profile").objectReferenceValue = p; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(v); }
            EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) throw new Refuse("the play scene was not saved");
            if (revert) File.Delete(ledger); else { Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(old, true)); }
            return sb + "  saved " + PlayScene + (revert ? " (reverted)" : " | ledger " + ledger);
        }

        // a vehicle boss field whose actor is no longer a boss: Vehicle308 fields-derive only adds / updates, so the stale entry is taken out here
        static string FinalFields(FinalData data, string verb)
        {
            var main = Main(); string ledger = FinalLedger("fields"); bool revert = verb == "revert", write = verb != "plan";
            var asset = AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(data.fields_asset); if (asset == null) throw new Refuse("no fields asset " + data.fields_asset);
            var sb = new StringBuilder("Hanyang308 final:fields:" + verb + (char)10); var list = (asset.Fields ?? Array.Empty<VehicleBossFieldSO.Field>()).ToList();
            if (revert)
            {
                if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger);
                var led = JsonUtility.FromJson<FinalFieldsLedger>(File.ReadAllText(ledger));
                foreach (var json in led.rows) { var f = JsonUtility.FromJson<VehicleBossFieldSO.Field>(json); if (list.Any(x => x.Id == f.Id)) continue; list.Add(f); sb.AppendLine("  + " + f.Id + " r " + F(f.Radius) + " at " + f.Centre.ToString("F0")); }
                BackupFiles("final_fields", Abs(data.fields_asset)); asset.Fields = list.ToArray(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); File.Delete(ledger);
                return sb + "  reverted, fields " + asset.Fields.Length;
            }
            if (write && File.Exists(ledger)) throw new Refuse("a fields ledger exists (final:fields:revert first)");
            var actors = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray(); var rows = new FinalFieldsLedger { asset = data.fields_asset }; int bad = 0;
            foreach (string id in data.fields_remove)
            {
                var entry = list.FirstOrDefault(x => x != null && x.Id == id); if (entry == null) { sb.AppendLine("  (no field " + id + " - nothing to remove)"); continue; }
                var actor = actors.FirstOrDefault(x => x.Id == id); var vit = actor != null ? actor.GetComponent<EnemyVitals>() : null;
                if (vit != null && vit.IsBoss) { bad++; sb.AppendLine("  REFUSED " + id + " is still a boss (its field stays)"); continue; }
                sb.AppendLine("  - " + id + " r " + F(entry.Radius) + " at " + entry.Centre.ToString("F0") + " | " + entry.Source + " | actor " + (actor == null ? "absent" : "boss " + (vit != null && vit.IsBoss)));
                rows.rows.Add(JsonUtility.ToJson(entry)); list.Remove(entry);
            }
            if (bad > 0) throw new Refuse(sb + "  " + bad + " problem(s), nothing written");
            if (!write || rows.rows.Count == 0) return sb + "  " + (write ? "nothing to write" : "plan only") + ", fields " + list.Count;
            BackupFiles("final_fields", Abs(data.fields_asset)); asset.Fields = list.ToArray(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(rows, true));
            return sb + "  written " + data.fields_asset + ", fields " + asset.Fields.Length + " | ledger " + ledger;
        }

        // read only: free width / headroom / hull fit along passages (physics colliders of the open scenes, triggers ignored).
        //   row with a and b = a street or deck from a to b; row with a only = an opening: the passage axis is found (longest free line at 1.5 m) and followed +-reach
        //   hull = [width, height, length, centre height] of the vehicle box (MagicStoneCarProfile HullSize / HullCentre)
        static string FinalClear(FinalData data)
        {
            Physics.SyncTransforms(); var sb = new StringBuilder("Hanyang308 final:clear rows " + data.clear.Count + " hull " + string.Join(" x ", data.hull.Select(v => F(v, "F2")))).Append((char)10);
            float hw = data.hull[0], hh = data.hull[1], hl = data.hull[2], hc = data.hull[3]; float[] heights = { 0.5f, 1.5f, 2.5f, hc + hh * 0.5f };
            bool Floor(Vector3 near, out Vector3 at) { if (Physics.Raycast(near + Vector3.up * 1.5f, Vector3.down, out var h, 5f, ~0, QueryTriggerInteraction.Ignore)) { at = h.point; return true; } at = near; return false; }
            float Free(Vector3 from, Vector3 dir, float max) => Physics.Raycast(from, dir, out var h, max, ~0, QueryTriggerInteraction.Ignore) ? h.distance : max;
            foreach (var row in data.clear)
            {
                var a = new Vector3(row.a[0], row.a[1], row.a[2]); Vector3 axis; var samples = new List<Vector3>();
                if (!Floor(a, out var fa)) { sb.Append("  ").Append(row.id).Append(": NO FLOOR within 3.5 m under (" + F(a.x) + ", " + F(a.y + 1.5f) + ", " + F(a.z) + ")").Append((char)10); continue; }
                if (row.b.Length == 3)
                {
                    var b = new Vector3(row.b[0], row.b[1], row.b[2]); axis = b - a; axis.y = 0; float len = axis.magnitude; axis /= len; var cur = fa;
                    for (float d = 0; d <= len; d += 1f) { var want = new Vector3(a.x + axis.x * d, cur.y, a.z + axis.z * d); if (Floor(want, out var f)) cur = f; else cur = want; samples.Add(cur); }
                }
                else
                {
                    float best = -1; axis = Quaternion.Euler(0, row.yaw, 0) * Vector3.forward;
                    if (row.yaw < -900f) for (int deg = 0; deg < 180; deg += 3) { var d = Quaternion.Euler(0, deg, 0) * Vector3.forward; float free = Free(fa + Vector3.up * 1.5f, d, 40f) + Free(fa + Vector3.up * 1.5f, -d, 40f); if (free > best) { best = free; axis = d; } }
                    for (int sign = -1; sign <= 1; sign += 2) { var cur = fa; for (float d = sign < 0 ? 1f : 0f; d <= row.reach; d += 1f) { var want = new Vector3(fa.x + axis.x * d * sign, cur.y, fa.z + axis.z * d * sign); if (Floor(want, out var f)) cur = f; else break; samples.Add(cur); } }
                }
                var right = Vector3.Cross(Vector3.up, axis); var rot = Quaternion.LookRotation(axis, Vector3.up); var minW = new float[heights.Length]; for (int i = 0; i < minW.Length; i++) minW[i] = float.MaxValue;
                float minHead = float.MaxValue; int blocked = 0; Vector3 worst = fa; float worstW = float.MaxValue; string blocker = ""; var names = new List<string>();
                foreach (var f in samples)
                {
                    for (int i = 0; i < heights.Length; i++) { var o = f + Vector3.up * heights[i]; float w = Free(o, right, 30f) + Free(o, -right, 30f); if (w < minW[i]) minW[i] = w; if (i == 1 && w < worstW) { worstW = w; worst = f; } }
                    float head = Free(f + Vector3.up * 0.3f, Vector3.up, 60f) + 0.3f; if (head < minHead) minHead = head;
                    var hits = Physics.OverlapBox(f + Vector3.up * (hc + 0.12f), new Vector3(hw, hh, hl) * 0.5f, rot, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Length > 0) { blocked++; if (blocker.Length == 0) blocker = "(" + F(f.x) + ", " + F(f.y) + ", " + F(f.z) + ")"; foreach (var h in hits) { string n = (h.transform.parent != null ? h.transform.parent.name + "/" : "") + h.name; if (!names.Contains(n) && names.Count < 5) names.Add(n); } }
                }
                sb.Append("  ").Append(row.id).Append(": axis yaw " + F(Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg, "F0") + " samples " + samples.Count + " floor " + F(samples.Min(v => v.y)) + " - " + F(samples.Max(v => v.y)) + " | min free width at " + string.Join(" / ", heights.Select((h, i) => F(h) + " m: " + (minW[i] >= 59.9f ? "open" : F(minW[i], "F2")))) + " | min headroom " + (minHead >= 60f ? "open" : F(minHead, "F2")) + " | narrowest at (" + F(worst.x) + ", " + F(worst.z) + ") | hull box blocked " + blocked + "/" + samples.Count + (blocker.Length > 0 ? " first at " + blocker + " by " + string.Join(", ", names) : "")).Append((char)10);
            }
            return sb.ToString().TrimEnd();
        }

        static string FinalRoof(FinalData data, string verb)
        {
            string ledger = FinalLedger("roof"); bool revert = verb == "revert", write = verb != "plan"; var sb = new StringBuilder("Hanyang308 final:roof:" + verb + "\n"); int bad = 0;
            var scenes = new List<Scene>(); for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) scenes.Add(s); }
            if (write && scenes.Any(s => s.isDirty)) throw new Refuse("a scene has unsaved changes");
            var touched = new HashSet<Scene>(); var rows = new FinalRoofLedger();
            if (revert)
            {
                if (!File.Exists(ledger)) throw new Refuse("no ledger " + ledger);
                var led = JsonUtility.FromJson<FinalRoofLedger>(File.ReadAllText(ledger));
                foreach (var r in led.rows)
                {
                    var sc = SceneManager.GetSceneByPath(r.scene); var t = sc.IsValid() && sc.isLoaded ? Find(sc, r.key) : null; var cols = t != null ? t.GetComponents<Collider>() : Array.Empty<Collider>();
                    if (r.index >= cols.Length) { bad++; sb.AppendLine("  MISSING " + r.scene + ":" + r.key + " #" + r.index + " (open its scene)"); continue; }
                    sb.AppendLine("  " + r.key + " #" + r.index + " " + cols[r.index].enabled + " -> " + r.was);
                }
                if (bad > 0) throw new Refuse(sb + "  " + bad + " problem(s), nothing written");
                BackupFiles("final_roof", led.rows.Select(r => Abs(r.scene)).Distinct().ToArray());
                foreach (var r in led.rows) { var sc = SceneManager.GetSceneByPath(r.scene); var c = Find(sc, r.key).GetComponents<Collider>()[r.index]; c.enabled = r.was; EditorUtility.SetDirty(c); touched.Add(sc); }
                foreach (var s in touched) { EditorSceneManager.MarkSceneDirty(s); if (!EditorSceneManager.SaveScene(s)) throw new Refuse("not saved: " + s.path); }
                File.Delete(ledger); return sb + "  reverted, saved " + string.Join(", ", touched.Select(s => s.name));
            }
            if (write && File.Exists(ledger)) throw new Refuse("a roof ledger exists (final:roof:revert first)");
            Physics.SyncTransforms();
            foreach (string key in data.colliders_off)
            {
                Transform t = null; Scene home = default; foreach (var s in scenes) { t = Find(s, key); if (t != null) { home = s; break; } }
                if (t == null) { bad++; sb.AppendLine("  MISSING " + key + " in the open scenes"); continue; }
                if (IsProtected(t)) { bad++; sb.AppendLine("  PROTECTED " + key); continue; }
                var cols = t.GetComponents<Collider>(); var rs = t.GetComponents<Renderer>();
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i]; var b = c.bounds;
                    // ground under the centre (anything but this collider): how far above walkable ground the lowest point is
                    float ground = float.NegativeInfinity; foreach (var h in Physics.RaycastAll(new Vector3(b.center.x, b.min.y - 0.05f, b.center.z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider != c && h.point.y > ground) ground = h.point.y;
                    sb.AppendLine("  " + home.name + ":" + key + " #" + i + " " + c.GetType().Name + " enabled " + c.enabled + " trigger " + c.isTrigger + " y " + F(b.min.y) + " - " + F(b.max.y) + " | first surface below " + (float.IsNegativeInfinity(ground) ? "none" : F(ground) + " (" + F(b.min.y - ground) + " m under it)") + " | renderers on " + rs.Count(r => r.enabled) + "/" + rs.Length + " | active " + t.gameObject.activeInHierarchy);
                    rows.rows.Add(new FinalRoofRow { scene = home.path, key = key, index = i, type = c.GetType().Name, was = c.enabled });
                }
                touched.Add(home);
            }
            if (bad > 0) throw new Refuse(sb + "  " + bad + " problem(s), nothing written");
            if (!write) return sb + "  plan only";
            BackupFiles("final_roof", touched.Select(s => Abs(s.path)).ToArray());
            foreach (var r in rows.rows) { var c = Find(SceneManager.GetSceneByPath(r.scene), r.key).GetComponents<Collider>()[r.index]; c.enabled = false; EditorUtility.SetDirty(c); }
            foreach (var s in touched) { EditorSceneManager.MarkSceneDirty(s); if (!EditorSceneManager.SaveScene(s)) throw new Refuse("not saved: " + s.path); }
            Directory.CreateDirectory(Path.GetDirectoryName(ledger)); File.WriteAllText(ledger, JsonUtility.ToJson(rows, true));
            return sb + "  colliders off " + rows.rows.Count + ", saved " + string.Join(", ", touched.Select(s => s.name)) + " | ledger " + ledger;
        }
    }
}
